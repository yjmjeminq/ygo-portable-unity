using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using YGOPortable.AI;
using YGOPortable.Core;
using Debug = UnityEngine.Debug;

namespace YGOPortable.Editor
{
    /// <summary>
    /// Replays immutable native prefixes, then releases both seats to the real Smart planner.
    /// These local decision regressions complement, rather than replace, unforced full matches.
    /// Run only with -batchmode -nographics -quit -executeMethod YGOPortable.Editor.AiFixedScenarioRegression.Run.
    /// </summary>
    public static class AiFixedScenarioRegression
    {
        [Serializable] private sealed class FilePin { public string path, sha256; }
        [Serializable] private sealed class Manifest
        {
            public int schemaVersion;
            public FilePin[] files;
            public string[] fixtures, builtInFixtures;
        }
        [Serializable] private sealed class Deck { public int id; public uint[] main, extra; }
        [Serializable] private sealed class Response
        {
            public int turn, phase;
            public byte player, message;
            public ulong hint;
            public string answer;
        }
        [Serializable] private sealed class Fixture
        {
            public int schemaVersion, matchup, seed, candidate, resume, stopTurn, maxLiveResponses;
            public string id, sourceInput, sourceInputSha256, expectation;
            public string[] nativeSeed;
            public Deck[] decks;
            public Response[] prefix;
            public Response boundary;
        }
        [Serializable] private sealed class AcceptedResponse
        {
            public int step, turn, phase;
            public byte player, message;
            public ulong hint;
            public bool prefix;
            public string answer;
            public double planMs, computeMs;
        }
        [Serializable] private sealed class CardView
        {
            public uint code, location, sequence, position;
            public int attack, defense;
            public bool disabled;
        }
        [Serializable] private sealed class PlayerView
        {
            public int life, deckCount;
            public CardView[] monsters, spells, hand, grave, banished, extra;
        }
        [Serializable] private sealed class CaseResult
        {
            public string id, error, expectation;
            public bool passed;
            public int prefixResponses, liveResponses, turn, winner = -1, retries, scriptErrors;
            public double meanComputeMs, maxComputeMs, maxPlanMs, elapsedMs;
            public string[] observations;
            public PlayerView[] finalState;
        }
        [Serializable] private sealed class RunResult
        {
            public int schemaVersion = 1, passed, failed;
            public bool complete;
            public string startedUtc, finishedUtc, fixtureManifestSha256;
            public FilePin[] testedFiles;
            public CaseResult[] cases;
        }

        private static int _scriptErrors;
        private static StreamWriter _log;
        private const string SummonNegation = "solemn-strike-summon-owner";
        private const string KeepOwnFusion = "predaprimitive-own-fusion";

        public static void Run()
        {
            int exitCode = 1;
            string root = Directory.GetParent(Application.dataPath).FullName;
            string output = Environment.GetEnvironmentVariable("YGO_AI_REGRESSION_OUT");
            if (string.IsNullOrWhiteSpace(output))
                output = Path.Combine(root, "Logs", "AiBenchmark", "FixedRegressions",
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(output);
            var report = new RunResult { startedUtc = DateTime.UtcNow.ToString("O") };
            var results = new List<CaseResult>();
            try
            {
                _log = new StreamWriter(Path.Combine(output, "proof.txt"));
                var args = Environment.GetCommandLineArgs();
                Require(Application.isBatchMode && args.Contains("-nographics") && args.Contains("-quit"),
                    "This runner requires -batchmode -nographics -quit.");
                string fixtureRoot = Path.Combine(root, "Tools", "AiRegression");
                string manifestPath = Path.Combine(fixtureRoot, "manifest.json");
                var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
                Require(manifest != null && manifest.schemaVersion == 1 && manifest.files?.Length >= 11 &&
                    manifest.fixtures?.Length == 6 &&
                    manifest.builtInFixtures?.SequenceEqual(new[] { SummonNegation, KeepOwnFusion }) == true,
                    "Missing or unsupported fixed regression manifest.");
                foreach (var pin in manifest.files)
                {
                    string actual = Hash(Inside(root, pin.path));
                    Require(string.Equals(actual, pin.sha256, StringComparison.OrdinalIgnoreCase),
                        $"Pinned native/card/fixture data changed: {pin.path}; expected {pin.sha256}, actual {actual}.");
                }
                report.fixtureManifestSha256 = Hash(manifestPath);
                report.testedFiles = Fingerprints(root).ToArray();
                foreach (var pin in report.testedFiles) Write($"SOURCE {pin.path} {pin.sha256}");
                Require(!YgoNativeBridge.IsInitialized, "Run in a fresh headless process; native engine was already initialized.");
                _scriptErrors = 0;
                Require(YgoNativeBridge.Initialize(Application.streamingAssetsPath, (kind, message) =>
                {
                    if (kind != 0) return;
                    _scriptErrors++;
                    Write("SCRIPT_ERROR " + message);
                }) == 0, "Native initialization failed.");
                CardDatabase.Load(Application.streamingAssetsPath);
                DeckRepository.Load(Application.streamingAssetsPath, Path.Combine(output, "save"), false);
                var general = DuelAiPolicy.LoadIncluded() ?? throw new InvalidOperationException("General AI policy is missing.");
                var links = LinkComboPolicy.LoadIncluded() ?? throw new InvalidOperationException("Link AI policy is missing.");
                var filter = (Environment.GetEnvironmentVariable("YGO_AI_REGRESSION_CASES") ?? "")
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).ToArray();
                var known = new HashSet<string>(manifest.fixtures.Select(Path.GetFileNameWithoutExtension)
                    .Concat(manifest.builtInFixtures));
                Require(filter.All(known.Contains), "Unknown case in YGO_AI_REGRESSION_CASES.");
                foreach (string relative in manifest.fixtures)
                {
                    var fixture = JsonUtility.FromJson<Fixture>(File.ReadAllText(Inside(fixtureRoot, relative)));
                    Require(fixture != null && fixture.schemaVersion == 1 && fixture.id == Path.GetFileNameWithoutExtension(relative),
                        "Unsupported or misnamed fixture: " + relative);
                    if (filter.Length > 0 && !filter.Contains(fixture.id)) continue;
                    results.Add(Play(fixture, general, links, output));
                    if (_scriptErrors != 0) break;
                }
                if (_scriptErrors == 0 && (filter.Length == 0 || filter.Contains(SummonNegation)))
                    results.Add(VerifySummonNegation(general, links));
                if (_scriptErrors == 0 && (filter.Length == 0 || filter.Contains(KeepOwnFusion)))
                    results.Add(VerifyOwnFusion(general, links));
                Require(results.Count == (filter.Length == 0 ? known.Count : filter.Distinct().Count()),
                    "Not every requested regression completed.");
                Require(_scriptErrors == 0, "Native script errors occurred.");
                report.complete = true;
                exitCode = results.All(result => result.passed) ? 0 : 1;
            }
            catch (Exception exception)
            {
                Write("FATAL " + exception);
                Debug.LogException(exception);
            }
            finally
            {
                report.finishedUtc = DateTime.UtcNow.ToString("O");
                report.cases = results.ToArray();
                report.passed = results.Count(result => result.passed);
                report.failed = results.Count(result => !result.passed);
                File.WriteAllText(Path.Combine(output, "summary.json"), JsonUtility.ToJson(report, true));
                Write($"SUMMARY complete={report.complete} passed={report.passed} failed={report.failed} ScriptErrors={_scriptErrors}");
                _log?.Dispose();
                _log = null;
                YgoNativeBridge.Shutdown();
                Debug.Log($"[AI-FIXED-REGRESSION] complete={report.complete} passed={report.passed} failed={report.failed} output={output}");
                EditorApplication.Exit(exitCode);
            }
        }

        private static CaseResult VerifySummonNegation(DuelAiPolicy general, LinkComboPolicy links)
        {
            const uint strike = 40605147, cyberDragon = 70095154, geminiElf = 69140098;
            var result = new CaseResult { id = SummonNegation, expectation = "Decline Strike for your Cyber Dragon; negate the opposing summon." };
            try
            {
                foreach (byte trapOwner in new byte[] { 0, 1 })
                {
                    uint[] lead = Enumerable.Range(0, 40).Select(i => i % 2 == 0 ? strike : cyberDragon).ToArray();
                    uint[] follow = Enumerable.Range(0, 40).Select(i => i % 2 == 0 ? strike : geminiElf).ToArray();
                    using var duel = YgoProjectBuilder.StartSeedMatching(SummonNegation,
                        lead, Array.Empty<uint>(), follow, Array.Empty<uint>(), true,
                        current => current.Players[0].hand.Any(card => card.code == strike) &&
                            current.Players[0].hand.Any(card => card.code == cyberDragon) &&
                            current.Players[1].hand.Any(card => card.code == strike) &&
                            current.Players[1].hand.Any(card => card.code == geminiElf));
                    var ai = new TacticalAI(general, links);
                    if (trapOwner == 0) SubmitAction(4, strike);
                    Advance(IsIdle);
                    duel.SubmitResponseInt(7);
                    Advance(IsIdle);
                    if (trapOwner == 1) SubmitAction(4, strike);
                    Advance(IsIdle);
                    SubmitAction(0, geminiElf);
                    Advance(IsIdle);
                    duel.SubmitResponseInt(7);
                    Advance(IsIdle);
                    SubmitAction(1, cyberDragon);
                    var window = Advance(prompt => prompt.playerId == trapOwner &&
                        prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && prompt.candidates.Any(card => card.code == strike));
                    int response = BitConverter.ToInt32(ai.ComputeResponse(duel, window), 0);
                    Write($"STRIKE trapOwner={trapOwner} summonSeat0={duel.HasPendingSpecialSummon(0)} " +
                        $"chainLinks={duel.CurrentChain.Count} choice={response}");
                    Require(trapOwner == 0 ? response == -1 : (uint)response < window.candidates.Count &&
                        window.candidates[response].code == strike, "Solemn Strike chose the wrong summon owner.");
                    duel.SubmitResponseInt(response);
                    Advance(IsIdle);
                    Require(duel.Players[trapOwner].lp == (trapOwner == 0 ? 8000 : 6500) &&
                        duel.Players[0].mzone.Any(card => card?.code == cyberDragon) == (trapOwner == 0),
                        "The actual summon or LP outcome diverged from the Strike decision.");
                    result.retries += duel.RetryCount;

                    static bool IsIdle(DuelPrompt prompt) => prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD;

                    void SubmitAction(int command, uint code)
                    {
                        var prompt = duel.ActivePrompt;
                        Require(prompt != null && IsIdle(prompt), "Missing native idle prompt.");
                        var actions = command == 4 ? prompt.sSetable : command == 1 ? prompt.spSummonable : prompt.summonable;
                        int index = actions.FindIndex(card => card.code == code);
                        Require(index >= 0, $"Missing native action {command}/{code}.");
                        duel.SubmitResponseInt((index << 16) | command);
                    }

                    DuelPrompt Advance(Func<DuelPrompt, bool> stop)
                    {
                        for (int step = 0; step < 32 && duel.RetryCount == 0 && !duel.IsFinished; step++)
                        {
                            var prompt = duel.ActivePrompt;
                            if (prompt == null) { duel.StepUntilPrompt(); continue; }
                            if (stop(prompt)) return prompt;
                            if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
                                duel.SubmitResponseInt(prompt.forced ? 0 : -1);
                            else if (prompt.messageType is YgoConstants.MSG_SELECT_POSITION or YgoConstants.MSG_SELECT_PLACE)
                                duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
                            else
                                throw new InvalidOperationException("Unexpected prompt " + prompt.messageType);
                        }
                        throw new InvalidOperationException("The native summon-negation prompt did not arrive.");
                    }
                }
                result.passed = true;
            }
            catch (Exception error) { result.error = error.ToString(); }
            result.scriptErrors = _scriptErrors;
            if (!result.passed) Write($"FAIL {result.id} {result.error}");
            Write($"END {result.id} passed={result.passed} Retry={result.retries} ScriptErrors={result.scriptErrors}");
            return result;
        }

        private static CaseResult VerifyOwnFusion(DuelAiPolicy general, LinkComboPolicy links)
        {
            const uint primitive = 89176044, polymerization = 24094653, geminiElf = 69140098,
                ophrysScorpio = 35272499, firstOfTheDragons = 10817524, cyberDragon = 70095154;
            var result = new CaseResult { id = KeepOwnFusion, expectation = "Keep the live Fusion when Predaprimitive offers to destroy it." };
            try
            {
                using var duel = YgoProjectBuilder.StartSeedMatching(KeepOwnFusion,
                    TestDuelAiTactics.Deck(primitive, polymerization, ophrysScorpio, geminiElf, geminiElf, geminiElf),
                    new[] { firstOfTheDragons }, TestDuelAiTactics.Deck(cyberDragon), Array.Empty<uint>(), true,
                    current => current.Players[0].hand.Any(card => card.code == primitive) &&
                        current.Players[0].hand.Count(card => card.code == geminiElf) >= 2);
                TestDuelAiTactics.SubmitIdle(duel, 5, primitive);
                TestDuelAiTactics.Drive(duel, TestDuelAiTactics.IsIdle);
                Require(duel.Players[0].grave.Any(card => card.code == primitive) &&
                    duel.Players[0].hand.Any(card => card.code == polymerization), "Predaprimitive did not resolve its hand search.");
                TestDuelAiTactics.SubmitIdle(duel, 5, polymerization);
                TestDuelAiTactics.Drive(duel, TestDuelAiTactics.IsIdle, firstOfTheDragons);
                Require(duel.Players[0].mzone.Any(card => card?.code == firstOfTheDragons), "The test Fusion did not appear.");
                duel.SubmitResponseInt(7);
                TestDuelAiTactics.Drive(duel, TestDuelAiTactics.IsIdle);
                TestDuelAiTactics.SubmitIdle(duel, 1, cyberDragon);
                var prompt = TestDuelAiTactics.Drive(duel, current => current.playerId == 0 &&
                    (current.messageType == YgoConstants.MSG_SELECT_EFFECTYN && current.promptCardCode == primitive ||
                    current.messageType == YgoConstants.MSG_SELECT_CHAIN && current.candidates.Any(card => card.code == primitive)));
                var ai = new TacticalAI(general, links);
                var answer = ai.ComputeResponse(duel, prompt);
                int pass = prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN ? 0 : -1;
                Require(BitConverter.ToInt32(answer, 0) == pass, "The AI accepted the optional self-destruction trigger.");
                duel.SubmitResponse(answer);
                TestDuelAiTactics.Drive(duel, TestDuelAiTactics.IsIdle);
                Require(duel.Players[0].mzone.Any(card => card?.code == firstOfTheDragons), "The native Fusion did not survive the pass.");
                result.retries = duel.RetryCount;
                result.passed = true;
            }
            catch (Exception error) { result.error = error.ToString(); }
            result.scriptErrors = _scriptErrors;
            if (!result.passed) Write($"FAIL {result.id} {result.error}");
            Write($"END {result.id} passed={result.passed} Retry={result.retries} ScriptErrors={result.scriptErrors}");
            return result;
        }

        private static CaseResult Play(Fixture fixture, DuelAiPolicy general, LinkComboPolicy links, string output)
        {
            var result = new CaseResult { id = fixture.id, expectation = fixture.expectation };
            var clock = Stopwatch.StartNew();
            using var duel = new DuelSession(recordPlanningReplay: true);
            using var transcript = new StreamWriter(Path.Combine(output, fixture.id + ".accepted.jsonl"));
            var observations = new List<string>();
            double computeTotal = 0;
            try
            {
                Require(fixture.decks?.Length == 2 && fixture.nativeSeed?.Length == 4 &&
                    fixture.prefix?.Length == fixture.resume && fixture.boundary != null &&
                    fixture.maxLiveResponses > 0 && fixture.stopTurn > 0, "Incomplete fixture " + fixture.id);
                foreach (var frozen in fixture.decks)
                {
                    var current = DeckRepository.PresetDecks.SingleOrDefault(deck => deck.id == frozen.id);
                    Require(current != null && current.main.SequenceEqual(frozen.main) &&
                        (current.extra ?? Array.Empty<uint>()).SequenceEqual(frozen.extra),
                        $"Fixture deck {frozen.id} differs from the current preset; review before rebasing a prefix.");
                }
                ulong n = 179426549UL + (ulong)fixture.matchup * 104729UL + (ulong)fixture.seed * 8089UL;
                var seed = fixture.nativeSeed.Select(value => ulong.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                Require(seed.SequenceEqual(new[] { n * 29 + 101, n * 47 + 7, n * 71 + 13, n * 97 + 3 }),
                    "Native seeds no longer match source benchmark provenance.");
                Require(duel.StartNewDuel(fixture.decks[0].main, fixture.decks[0].extra,
                    fixture.decks[1].main, fixture.decks[1].extra, seed: seed), "Cannot start native fixture.");
                for (int step = 0; step < fixture.prefix.Length; step++)
                {
                    var prompt = Prompt(duel);
                    Match(duel, prompt, fixture.prefix[step], step);
                    var answer = Parse(fixture.prefix[step].answer);
                    var accepted = Row(duel, prompt, answer, step, true);
                    duel.SubmitResponse(answer);
                    Stable(duel);
                    transcript.WriteLine(JsonUtility.ToJson(accepted));
                    result.prefixResponses++;
                }
                Match(duel, Prompt(duel), fixture.boundary, fixture.resume);
                Write($"BEGIN {fixture.id} resume={fixture.resume} T{duel.TurnNumber} phase={duel.CurrentPhase}");
                Write("PREFIX_STATE " + fixture.id + " " + JsonUtility.ToJson(new State { players = Snapshot(duel) }));
                var pilot = new[] { new TacticalAI(general, links, searchCombos: true),
                    new TacticalAI(general, links, searchCombos: true) };
                var gate = new ScenarioGate(fixture.id, duel, observations);
                while (!duel.IsFinished && duel.TurnNumber < fixture.stopTurn)
                {
                    Require(result.liveResponses < fixture.maxLiveResponses && clock.Elapsed.TotalSeconds < 120,
                        "Regression hit response/time cap before its semantic endpoint.");
                    var prompt = Prompt(duel);
                    if (duel.IsFinished || duel.TurnNumber >= fixture.stopTurn) break;
                    gate.Before(duel, prompt);
                    var planClock = Stopwatch.StartNew();
                    while (!pilot[prompt.playerId].AdvanceComboPlanning(duel, prompt))
                        Require(planClock.Elapsed.TotalSeconds < 5, "Smart planning did not complete within watchdog budget.");
                    double planMs = planClock.Elapsed.TotalMilliseconds;
                    var computeClock = Stopwatch.StartNew();
                    byte[] answer = pilot[prompt.playerId].ComputeResponse(duel, prompt);
                    double computeMs = computeClock.Elapsed.TotalMilliseconds;
                    Require(answer?.Length > 0, "Smart AI returned no native response.");
                    gate.Choice(duel, prompt, answer, result.liveResponses);
                    var accepted = Row(duel, prompt, answer, fixture.resume + result.liveResponses, false);
                    accepted.planMs = planMs;
                    accepted.computeMs = computeMs;
                    duel.SubmitResponse(answer);
                    Stable(duel);
                    gate.After(duel);
                    transcript.WriteLine(JsonUtility.ToJson(accepted));
                    transcript.Flush();
                    Write($"LIVE {fixture.id} step={accepted.step} T{accepted.turn} phase={accepted.phase} " +
                        $"p{accepted.player} msg={accepted.message} hint={accepted.hint} answer={accepted.answer}");
                    result.liveResponses++;
                    computeTotal += computeMs;
                    result.maxComputeMs = Math.Max(result.maxComputeMs, computeMs);
                    result.maxPlanMs = Math.Max(result.maxPlanMs, planMs);
                }
                Stable(duel);
                gate.Verify(duel);
                result.passed = true;
            }
            catch (Exception exception)
            {
                result.error = exception.ToString();
                Write("FAIL " + fixture.id + " " + exception);
            }
            result.elapsedMs = clock.Elapsed.TotalMilliseconds;
            result.meanComputeMs = computeTotal / Math.Max(1, result.liveResponses);
            result.turn = duel.TurnNumber;
            result.winner = duel.Winner;
            result.retries = duel.RetryCount;
            result.scriptErrors = _scriptErrors;
            result.observations = observations.ToArray();
            result.finalState = Snapshot(duel);
            Write($"END {fixture.id} passed={result.passed} T{result.turn} winner={result.winner} " +
                $"responses={result.liveResponses} Retry={result.retries} ScriptErrors={result.scriptErrors}");
            return result;
        }

        private sealed class ScenarioGate
        {
            private readonly string _id;
            private readonly List<string> _observations;
            private readonly uint[] _connectorExtra;
            private readonly List<(uint Attacker, uint Target)> _attacks = new();
            private bool _firstExpected, _sageDefended, _zeroAttack, _primeDiscard, _bossNormal, _tribute,
                _nurseNormal, _giftChain, _connectorTarget, _revivedDefense, _reachedMain9, _tributeErebusPresent,
                _pendingErebusNormal, _pendingErebusTribute;
            private uint _pendingAttacker;
            private int _ownBattleDamage;

            internal ScenarioGate(string id, DuelSession duel, List<string> observations)
            {
                _id = id;
                _observations = observations;
                _connectorExtra = duel.Players[0].extra.Select(card => card.code).OrderBy(code => code).ToArray();
                duel.OnLifePointChange += (player, delta, _, kind) =>
                {
                    if (id == "coyote" && duel.TurnNumber == 4 && player == 1 && delta < 0 &&
                        kind == LifePointChangeKind.BattleDamage) _ownBattleDamage -= delta;
                };
                duel.OnAttackDeclared += (player, location, sequence, targetPlayer, targetLocation, targetSequence) =>
                {
                    if (id != "coyote" || duel.TurnNumber != 4 || player != 1 || targetPlayer != 0 ||
                        location != YgoConstants.LOCATION_MZONE || targetLocation != YgoConstants.LOCATION_MZONE) return;
                    uint attacker = duel.Players[player].mzone[sequence]?.code ?? 0;
                    uint target = duel.Players[targetPlayer].mzone[targetSequence]?.code ?? 0;
                    _attacks.Add((attacker, target));
                    _observations.Add($"Native declared attack: {attacker}->{target}");
                };
                duel.OnCardMoved += move =>
                {
                    if (id != "monarch" || move.code != 23064604) return;
                    if (move.fromCon == 1 && move.fromLoc == YgoConstants.LOCATION_MZONE) _tributeErebusPresent = false;
                    // The current native presentation metadata covers Special Summons, not normal summons.
                    // Bind this hand arrival to the accepted native Normal Summon and Tribute prompts instead.
                    if (move.fromCon == 1 && move.fromLoc == YgoConstants.LOCATION_HAND && _pendingErebusNormal)
                    {
                        _tributeErebusPresent = move.toCon == 1 && move.toLoc == YgoConstants.LOCATION_MZONE && _pendingErebusTribute;
                        _pendingErebusNormal = _pendingErebusTribute = false;
                    }
                };
            }

            internal void Before(DuelSession duel, DuelPrompt prompt)
            {
                if (_id == "ddd" && duel.TurnNumber == 9 && duel.CurrentPhase == YgoConstants.PHASE_MAIN1 &&
                    prompt.playerId == 0) _reachedMain9 = true;
            }

            internal void Choice(DuelSession duel, DuelPrompt prompt, byte[] answer, int liveStep)
            {
                int command = answer.Length >= 4 ? BitConverter.ToInt32(answer, 0) : -999;
                int kind = command & 0xffff, index = command >> 16;
                SelectableCardItem action = null;
                if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                {
                    var list = kind switch { 0 => prompt.summonable, 1 => prompt.spSummonable,
                        2 => prompt.repositionable, 3 => prompt.mSetable, 4 => prompt.sSetable,
                        5 => prompt.activatable, _ => null };
                    if (list != null && (uint)index < list.Count) action = list[index];
                }
                if (liveStep == 0)
                {
                    _firstExpected = _id switch
                    {
                        // The LP and position checks still apply when an additional defender is set first.
                        "sage" => (kind == 2 && action?.code == 8240199) || (kind == 3 && action != null),
                        "coyote" => prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD && kind == 1 &&
                            (uint)index < prompt.attackable.Count && prompt.attackable[index].code == 31313405,
                        "ddd" => prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && kind == 7,
                        "snake" => kind == 0 && action?.code == 67316075,
                        "monarch" => kind == 5 && action?.code == 79571449,
                        "connector" => kind == 5 && action?.code == 85840608 && action.location == YgoConstants.LOCATION_MZONE,
                        _ => false,
                    };
                    _observations.Add($"First native choice: command={kind}, code={action?.code}, expected={_firstExpected}");
                }
                if (_id == "sage" && prompt.playerId == 1 &&
                    prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD && kind == 1 &&
                    (uint)index < prompt.attackable.Count)
                {
                    var card = prompt.attackable[index];
                    var live = duel.Players[1].mzone.FirstOrDefault(item => item?.sequence == card.sequence);
                    if (live?.code == 8240199 && live.attack == 0 && duel.Players[0].mzone.All(item => item == null))
                        _zeroAttack = true;
                }
                if (_id == "coyote" && duel.TurnNumber == 4 && prompt.playerId == 1)
                {
                    if (prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD && kind == 1 &&
                        (uint)index < prompt.attackable.Count) _pendingAttacker = prompt.attackable[index].code;
                    if (prompt.messageType == YgoConstants.MSG_SELECT_CARD &&
                        prompt.SelectionHint == YgoConstants.HINTMSG_ATTACKTARGET)
                    {
                        var selected = Indices(prompt, answer);
                        Require(selected.Length == 1, "Battle allocation did not select exactly one native target.");
                        var target = prompt.candidates[selected[0]];
                        Require(target.controller == 0, "Battle allocation selected a friendly target.");
                        _observations.Add($"Selected attack pair: {_pendingAttacker}->{target.code}");
                    }
                }
                if (_id == "snake")
                {
                    _nurseNormal |= prompt.playerId == 1 && kind == 0 && action?.code == 67316075;
                    _giftChain |= prompt.playerId == 1 && prompt.messageType == YgoConstants.MSG_SELECT_CHAIN &&
                        (uint)command < prompt.candidates.Count && prompt.candidates[command].code == 39526584;
                }
                if (_id == "monarch" && prompt.playerId == 1)
                {
                    if (prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.SelectionHint == DuelTactics.Discard &&
                        duel.SelectionChainLink?.code == 79571449)
                    {
                        var codes = Indices(prompt, answer).Select(i => prompt.candidates[i].code).ToArray();
                        _primeDiscard = codes.Length == 2 && codes.Contains(54241725u) &&
                            !codes.Contains(22842126u) && !codes.Contains(23064604u);
                        _observations.Add("Graceful discard: " + string.Join(",", codes));
                    }
                    if (kind == 0 && action?.code == 23064604) _bossNormal = _pendingErebusNormal = true;
                    if (prompt.messageType == YgoConstants.MSG_SELECT_TRIBUTE && _pendingErebusNormal)
                        _tribute = _pendingErebusTribute = true;
                }
                if (_id == "connector" && prompt.playerId == 0 &&
                    prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.SelectionHint == 509 &&
                    _firstExpected && prompt.min == 1 && prompt.max == 1 &&
                    (duel.SelectionChainLink == null || duel.SelectionChainLink is { code: 85840608, triggerPlayer: 0 }))
                {
                    // This ignition pays Connector as cost before CHAINING; a null selection source is normal here.
                    var choices = Indices(prompt, answer);
                    _connectorTarget = choices.Length == 1 && prompt.candidates[choices[0]] is
                        { code: 43237273, controller: 0, location: YgoConstants.LOCATION_GRAVE };
                }
            }

            internal void After(DuelSession duel)
            {
                if (_id == "sage") _sageDefended |= duel.Players[1].mzone.Any(card => card is
                    { code: 8240199, IsFaceUp: true, IsDefense: true });
                if (_id == "connector") _revivedDefense |= duel.Players[0].mzone.Any(card => card is
                    { code: 43237273, IsFaceUp: true, IsDefense: true });
            }

            internal void Verify(DuelSession duel)
            {
                Require(_firstExpected, "The fixed critical decision regressed (see first native choice).");
                switch (_id)
                {
                    case "sage":
                        Require(_sageDefended && !_zeroAttack && duel.TurnNumber >= 6 && duel.Players[1].lp >= 8000,
                            "Sage must defend, skip a direct zero-ATK attack, and avoid the recorded 4300 damage through T6.");
                        break;
                    case "coyote":
                        Require(_attacks.Count >= 2 && _attacks[0] == (31313405u, 21637210u) &&
                            _attacks[1] == (71861848u, 2347477u) && _ownBattleDamage == 0 && duel.TurnNumber >= 6 &&
                            !duel.Players[0].mzone.Any(card => card?.code is 21637210 or 2347477),
                            "Expected Pyro->disabled Singularity and Coyote->Micro Coder, both removed without own battle damage.");
                        break;
                    case "ddd":
                        Require((_reachedMain9 || duel.IsFinished && duel.Winner == 0) && duel.Players[0].lp > 0 &&
                            !(duel.IsFinished && duel.Winner != 0), "The avoided third Contract must survive its next upkeep/Main9.");
                        break;
                    case "snake":
                        Require(_nurseNormal && _giftChain && duel.IsFinished && duel.Winner == 1 &&
                            duel.TurnNumber == 6 && duel.Players[1].lp > 0,
                            "Native Nurse + Gift Card must actually win at the recorded T6 lethal.");
                        break;
                    case "monarch":
                        Require(_primeDiscard && _bossNormal && _tribute && _tributeErebusPresent && duel.TurnNumber >= 3 &&
                            duel.Players[1].mzone.Any(card => card is { code: 23064604, IsFaceUp: true }) &&
                            duel.Players[1].szone.Any(card => card is { code: 84171830, IsFaceUp: true, effectsDisabled: false }) &&
                            duel.Players[1].lp >= 8000, "Domain alone is insufficient: retain the actual Tribute Summoned Erebus.");
                        break;
                    case "connector":
                        Require(_connectorTarget && _revivedDefense && duel.TurnNumber >= 5 && duel.Players[0].lp >= 8000 &&
                            duel.Players[0].mzone.All(card => card == null) &&
                            duel.Players[0].hand.Select(card => card.code).OrderBy(code => code)
                                .SequenceEqual(new uint[] { 80344569, 16605586 }.OrderBy(code => code)) &&
                            duel.Players[0].extra.Select(card => card.code).OrderBy(code => code).SequenceEqual(_connectorExtra) &&
                            duel.Players[0].grave.Select(card => card.code).OrderBy(code => code)
                                .SequenceEqual(new uint[] { 40740224, 75047173, 24299458, 85840608, 43237273 }.OrderBy(code => code)),
                            "Connector must use the legal GY defense, preserve LP/hand/Extra, and avoid the recorded exposure.");
                        break;
                    default: throw new InvalidOperationException("Unknown semantic regression: " + _id);
                }
                _observations.Add("Native semantic endpoint passed; both seats used current Smart after the fixed prefix.");
            }
        }

        [Serializable] private sealed class State { public PlayerView[] players; }
        private static PlayerView[] Snapshot(DuelSession duel) => duel.Players.Select(player => new PlayerView
        {
            life = player.lp, deckCount = player.deckCount, monsters = Views(player.mzone), spells = Views(player.szone),
            hand = Views(player.hand), grave = Views(player.grave), banished = Views(player.banished), extra = Views(player.extra),
        }).ToArray();
        private static CardView[] Views(IEnumerable<LiveCardState> cards) => cards.Where(card => card != null)
            .Select(card => new CardView { code = card.code, location = card.location, sequence = card.sequence,
                position = card.position, attack = card.attack, defense = card.defense, disabled = card.effectsDisabled }).ToArray();
        private static DuelPrompt Prompt(DuelSession duel)
        {
            if (duel.ActivePrompt == null && !duel.IsFinished) duel.StepUntilPrompt();
            Require(!duel.IsFinished && duel.ActivePrompt != null, "Native duel ended or omitted the expected prompt.");
            return duel.ActivePrompt;
        }
        private static void Match(DuelSession duel, DuelPrompt prompt, Response record, int step) =>
            Require(duel.TurnNumber == record.turn && duel.CurrentPhase == record.phase && prompt.playerId == record.player &&
                prompt.messageType == record.message && prompt.SelectionHint == record.hint,
                $"Exact prefix mismatch at {step}: expected {record.turn}/{record.phase}/{record.player}/{record.message}/{record.hint}; " +
                $"actual {duel.TurnNumber}/{duel.CurrentPhase}/{prompt.playerId}/{prompt.messageType}/{prompt.SelectionHint}.");
        private static AcceptedResponse Row(DuelSession duel, DuelPrompt prompt, byte[] answer, int step, bool prefix) =>
            new AcceptedResponse { step = step, turn = duel.TurnNumber, phase = duel.CurrentPhase, player = prompt.playerId,
                message = prompt.messageType, hint = prompt.SelectionHint, prefix = prefix, answer = BitConverter.ToString(answer) };
        private static byte[] Parse(string answer) => answer.Split('-').Select(value => Convert.ToByte(value, 16)).ToArray();
        private static int[] Indices(DuelPrompt prompt, byte[] answer)
        {
            Require(answer.Length >= 8 && BitConverter.ToInt32(answer, 0) == 0, "Expected native index-list response.");
            int count = BitConverter.ToInt32(answer, 4);
            Require(count >= 0 && count <= prompt.candidates.Count && answer.Length == 8 + count * 4,
                "Malformed native index-list response.");
            int[] indices = Enumerable.Range(0, count).Select(index => BitConverter.ToInt32(answer, 8 + index * 4)).ToArray();
            Require(indices.All(index => (uint)index < prompt.candidates.Count) && indices.Distinct().Count() == count,
                "Invalid native selection index.");
            return indices;
        }
        private static void Stable(DuelSession duel) => Require(duel.RetryCount == 0 && _scriptErrors == 0,
            $"Native protocol regression: Retry={duel.RetryCount}, ScriptErrors={_scriptErrors}.");
        private static IEnumerable<FilePin> Fingerprints(string root)
        {
            var files = Directory.GetFiles(Path.Combine(root, "Assets", "Scripts", "AI"), "*.cs")
                .Concat(new[] { "Assets/Scripts/Core/DuelSession.cs", "Assets/Scripts/Core/CardDatabase.cs",
                    "Assets/Scripts/Core/DeckRepository.cs", "Assets/Scripts/Core/YgoConstants.cs",
                    "Assets/Scripts/Core/YgoNativeBridge.cs", "Assets/Resources/DuelAiPolicy.json",
                    "Assets/Resources/LinkComboPolicy.json", "Assets/Plugins/x86_64/ygoengine.dll",
                    "Assets/StreamingAssets/cards_prop.bin", "Assets/StreamingAssets/cardscripts.pack",
                    "Assets/StreamingAssets/cards_kr.json", "Assets/StreamingAssets/deck_effect_categories.bin",
                    "Assets/Scripts/Editor/AiFixedScenarioRegression.cs" }.Select(relative => Path.Combine(root, relative)));
            foreach (string file in files.OrderBy(value => value, StringComparer.Ordinal))
                yield return new FilePin { path = file.Substring(root.Length + 1).Replace('\\', '/'), sha256 = Hash(file) };
        }
        private static string Hash(string path)
        {
            using var algorithm = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static string Inside(string root, string relative)
        {
            string full = Path.GetFullPath(Path.Combine(root, relative));
            Require(!Path.IsPathRooted(relative) && full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), "Manifest path escapes its fixture root.");
            return full;
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        private static void Write(string message)
        {
            _log?.WriteLine(message);
            _log?.Flush();
        }
    }
}
