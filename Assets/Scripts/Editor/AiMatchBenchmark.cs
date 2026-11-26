using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using YGOPortable.AI;
using YGOPortable.Core;
using YGOPortable.UI;
using Debug = UnityEngine.Debug;

namespace YGOPortable.Editor
{
    internal static class AiMatchBenchmark
    {
        private const int DefaultSeeds = 8;
        private const int TurnLimit = 160;
        private const int ResponseLimit = 6400;
        private static readonly (string Era, int FirstDeck, int SecondDeck, uint Method)[] Matchups =
        {
            ("DM", 51043, 50940, YgoConstants.SUMMON_TYPE_FUSION),
            ("DM", 51049, 50947, YgoConstants.SUMMON_TYPE_FUSION),
            ("GX", 51190, 51370, YgoConstants.SUMMON_TYPE_FUSION),
            ("GX", 51199, 51377, YgoConstants.SUMMON_TYPE_FUSION),
            ("5D's", 50350, 50160, YgoConstants.SUMMON_TYPE_SYNCHRO),
            ("5D's", 50359, 50169, YgoConstants.SUMMON_TYPE_SYNCHRO),
            ("ZEXAL", 51740, 51560, YgoConstants.SUMMON_TYPE_XYZ),
            ("ZEXAL", 51749, 51569, YgoConstants.SUMMON_TYPE_XYZ),
            ("ARC-V", 50720, 50430, YgoConstants.SUMMON_TYPE_PENDULUM),
            ("ARC-V", 50729, 50439, YgoConstants.SUMMON_TYPE_PENDULUM),
            ("VRAINS", 51430, 51870, YgoConstants.SUMMON_TYPE_LINK),
            ("VRAINS", 51439, 51879, YgoConstants.SUMMON_TYPE_LINK),
            ("2026", 20671, 20678, YgoConstants.SUMMON_TYPE_FUSION),
            ("2026", 20686, 20681, YgoConstants.SUMMON_TYPE_XYZ),
        };
        private static readonly (string Era, int FirstDeck, int SecondDeck, uint Method)[] StyleMatchups =
        {
            ("Control/Runick", 20672, 20673, 0),
            ("Trap/Lock", 20679, 20675, 0),
            ("Burn/Exodia", 52150, 50964, 0),
            ("Development/Destiny Board", 51199, 51029, 0),
            ("Burn/Millennium", 52150, 50961, 0),
            ("Sky Striker/Development", 20687, 20686, YgoConstants.SUMMON_TYPE_LINK),
            ("Sky Striker/Control", 20687, 20682, YgoConstants.SUMMON_TYPE_LINK),
            ("DM/Ritual", 51049, 51954, YgoConstants.SUMMON_TYPE_RITUAL),
        };
        private static readonly (string Era, int FirstDeck, int SecondDeck, uint Method)[] GxMatchups =
        {
            ("GX/Cyber Angel–Destiny HERO", 69101, 69011, YgoConstants.SUMMON_TYPE_RITUAL),
            ("GX/Drytron–Branded", 51991, 69033, YgoConstants.SUMMON_TYPE_RITUAL),
            ("GX/Yubel–HERO", 69027, 20689, YgoConstants.SUMMON_TYPE_FUSION),
            ("GX/Modern Yubel–Masked HERO", 20680, 69029, YgoConstants.SUMMON_TYPE_LINK),
            ("GX/Cyberdark–Ancient Gear", 69016, 69028, YgoConstants.SUMMON_TYPE_FUSION),
            ("GX/Cyber Dragon–Sacred Beasts", 69001, 69009, YgoConstants.SUMMON_TYPE_FUSION),
            ("GX/Dark Magician–Despia", 69034, 50698, YgoConstants.SUMMON_TYPE_FUSION),
            ("GX/Exodia–Evil HERO", 69023, 69025, YgoConstants.SUMMON_TYPE_FUSION),
            ("GX/Nasateiya–Branded", 69102, 69033, YgoConstants.SUMMON_TYPE_RITUAL),
            ("GX/Cyber Angel–Fiendsmith", 69103, 20685, YgoConstants.SUMMON_TYPE_RITUAL),
        };

        internal readonly struct Pilot
        {
            internal readonly Func<DuelSession, DuelPrompt, byte[]> Choose;
            internal readonly Func<int> Branches;
            internal readonly Func<int> ChosenAlternatives;
            internal readonly Func<double> MaxPlanMilliseconds;
            internal readonly Func<double> LastPlanMilliseconds;

            internal Pilot(Func<DuelSession, DuelPrompt, byte[]> choose, Func<int> branches, Func<int> chosenAlternatives = null)
                : this(choose, branches, chosenAlternatives, null) { }

            internal Pilot(Func<DuelSession, DuelPrompt, byte[]> choose, Func<int> branches,
                Func<int> chosenAlternatives, Func<double> maxPlanMilliseconds)
                : this(choose, branches, chosenAlternatives, maxPlanMilliseconds, null) { }

            internal Pilot(Func<DuelSession, DuelPrompt, byte[]> choose, Func<int> branches,
                Func<int> chosenAlternatives, Func<double> maxPlanMilliseconds, Func<double> lastPlanMilliseconds)
            {
                Choose = choose;
                Branches = branches;
                ChosenAlternatives = chosenAlternatives;
                MaxPlanMilliseconds = maxPlanMilliseconds;
                LastPlanMilliseconds = lastPlanMilliseconds;
            }
        }

        internal static Pilot TimedPilot(Func<DuelSession, DuelPrompt, byte[]> choose,
            Func<DuelSession, DuelPrompt, bool> prepare, Func<int> branches, Func<int> chosenAlternatives)
        {
            double maxPlanMs = 0, lastPlanMs = 0;
            return new Pilot((duel, prompt) =>
            {
                long started = Stopwatch.GetTimestamp();
                while (!prepare(duel, prompt)) { }
                lastPlanMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                maxPlanMs = Math.Max(maxPlanMs, lastPlanMs);
                return choose(duel, prompt);
            }, branches, chosenAlternatives, () => maxPlanMs, () => lastPlanMs);
        }

        [Serializable]
        private sealed class Match
        {
            public string era;
            public int matchup, seed, candidate, winner, turns, nativeErrors;
            public bool capped, openingOnly;
            public uint method;
            public int interleavedOrder;
            public Side[] sides;
            public ReplayResponse[] replay;
            public int[] finalLife;
        }

        [Serializable]
        private sealed class ReplayResponse
        {
            public int turn, phase;
            public byte player, message;
            public ulong hint;
            public string answer;
        }

        [Serializable]
        private sealed class Side
        {
            public int deck, level, decisions, branches, chosenAlternatives, retries, endTurns, openings, threatsSpent, lethalMisses;
            public int exposedAttackers, selfDestruction, wastedHandtraps;
            public int completedTurns, emptyFieldEndTurns, emptyMonsterEndTurns, trackedResponses;
            public int provenMissedBattleWins, battleFinishes;
            public int equipRuleLosses, selfDestructionWithoutEquipRuleLoss;
            public int selfDefeatTurn, selfDefeatDamage;
            public uint selfDefeatEffect;
            public int unusedDevelopmentEndTurns, unusedDevelopmentOptions;
            public int unusedDevelopmentEndTurnsOnLoss, unusedDevelopmentOptionsOnLoss;
            public int linkMaterialDowngrades, higherLinkMaterialsSpent;
            public int slowestTurn, slowestMessage, slowestPhase, slowestStep;
            public int slowestOrdinaryTurn, slowestOrdinaryMessage, slowestOrdinaryPhase, slowestOrdinaryStep;
            public bool reachedMain, methodAvailable, methodReached, aceReached;
            public bool signatureMonsterReached;
            public bool planTimingAvailable;
            public bool ordinaryTimingSeparated;
            public uint method;
            public uint[] initial;
            public double decisionMs, slowestMs, maxPlanMs, boardTotal, earlyBoard;
            public double ordinaryDecisionMs, slowestOrdinaryMs;
            public string style;
            public string[] earlyActions, failures;
            public DestructionTrace[] destructionTraces;
            public ExposureTrace[] exposureTraces;
            public UnusedDevelopmentTrace[] unusedDevelopmentTraces;
            public LinkMaterialDowngradeTrace[] linkMaterialDowngradeTraces;
        }

        [Serializable]
        private sealed class DestructionTrace
        {
            public uint effect;
            public int turn, opposingRemovals, draws, lpSwing;
            public bool resolved;
            public List<uint> destroyed = new(), fieldArrivals = new(), handArrivals = new();
        }

        [Serializable]
        private sealed class ExposureTrace
        {
            public uint card;
            public int turn, attack, opponentAttack, life, damageTaken;
            public string alternative;
            public bool endPhaseReposition, attacked, battlePayoff, fieldEffectResolved;
            public bool targetedNextTurn, lostByBattle;
        }

        // Native options and material movements are raw observations, not proof of a blunder.
        [Serializable]
        private sealed class DevelopmentOption
        {
            public int command;
            public uint card, location, sequence;
            public ulong description;
        }

        [Serializable]
        private sealed class UnusedDevelopmentTrace
        {
            public int turn, phase, step;
            public bool eventualLoss;
            public DevelopmentOption[] options;
        }

        [Serializable]
        private sealed class LinkMaterialDowngradeTrace
        {
            public int turn, phase, step, targetRank;
            public uint target;
            public uint[] materials;
            public int[] materialRanks;
        }

        private static readonly HashSet<uint> Handtraps = new()
        {
            14558127, 23434538, 97268402, 10045474, 24224830, 73642296, 27204311, 94145021,
            42141493, 59438930, 38814750, 10443957, 52038441,
        };
        private static readonly HashSet<uint> ReactiveHandtraps = new() { 14558127, 97268402, 10045474 };
        private static readonly HashSet<uint> GxTrackedResponses = new() { 14558127, 23434538, 10045474, 24224830, 24299458, 48130397 };
        private const uint Negation = 1u << 8;

        public static void Run()
        {
            int exit = 0;
            try
            {
                string output = Environment.GetEnvironmentVariable("YGO_BENCH_REPORT") ??
                    throw new InvalidOperationException("YGO_BENCH_REPORT is required.");
                var baseline = LoadPilotFactory("AiBenchmarkBaselineFactory");
                int seeds = int.TryParse(Environment.GetEnvironmentVariable("YGO_BENCH_SEEDS"), out int parsed) ? parsed : DefaultSeeds;
                int start = int.TryParse(Environment.GetEnvironmentVariable("YGO_BENCH_START"), out parsed) ? parsed : 0;
                int stride = int.TryParse(Environment.GetEnvironmentVariable("YGO_BENCH_STRIDE"), out parsed) ? parsed : 1;
                if (seeds < 1 || start < 0 || stride < 1 || start >= stride) throw new ArgumentOutOfRangeException(nameof(seeds));
                int nativeErrors = 0;
                string streaming = Application.streamingAssetsPath;
                if (YgoNativeBridge.Initialize(streaming, (kind, message) =>
                {
                    if (kind != 0) return;
                    nativeErrors++;
                    Debug.LogWarning(message);
                }) != 0) throw new InvalidOperationException("Could not initialize the duel engine.");
                CardDatabase.Load(streaming);
                DeckRepository.Load(streaming, Path.Combine(Path.GetDirectoryName(output), "benchmark-save"), false);
                var decks = DeckRepository.PresetDecks.ToDictionary(deck => deck.id);
                bool gxSuite = Environment.GetEnvironmentVariable("YGO_BENCH_GX_SUITE") == "1";
                if (gxSuite)
                {
                    var gxDecks = StoryModeUI.LoadRpgDecks(streaming, StoryModeUI.GxRpgSeriesId);
                    if (gxDecks.Count != 37) throw new InvalidOperationException("The GX benchmark requires all 37 story decks.");
                    foreach (var deck in gxDecks) decks.Add(deck.Key, deck.Value);
                    if (!decks[51991].main.Contains(22398665u) || !decks[69033].main.Contains(44362883u))
                        throw new InvalidOperationException("The GX deck inputs lack the expected Drytron and Branded Ritual/Fusion engines.");
                }
                if (Environment.GetEnvironmentVariable("YGO_BENCH_REPLAY") is { Length: > 0 } replayPath)
                {
                    int inspect = int.TryParse(Environment.GetEnvironmentVariable("YGO_BENCH_INSPECT"), out int index) ? index : -1;
                    bool auditBattle = Environment.GetEnvironmentVariable("YGO_BENCH_AUDIT_BATTLE") == "1";
                    string answer = Environment.GetEnvironmentVariable("YGO_BENCH_TRY_RESPONSE");
                    using var replayOutput = new StreamWriter(output) { AutoFlush = true };
                    foreach (string line in File.ReadLines(replayPath).Where((_, row) => row % stride == start))
                    {
                        var replayed = ReplayRecordedMatch(JsonUtility.FromJson<Match>(line), inspect, answer, auditBattle, decks, () => nativeErrors);
                        replayOutput.WriteLine(JsonUtility.ToJson(replayed));
                    }
                    return;
                }
                VerifyDecisionRules();
                VerifyCrossoutResponse();
                VerifyNegationAfterCost();
                VerifyNormalSetUnderPressure();
                VerifyNativeMainBattleAudit();
                var general = DuelAiPolicy.LoadIncluded() ?? throw new InvalidOperationException("Missing general AI policy.");
                var links = LinkComboPolicy.LoadIncluded() ?? throw new InvalidOperationException("Missing Link AI policy.");
                bool styleSuite = Environment.GetEnvironmentVariable("YGO_BENCH_STYLE_SUITE") == "1";
                bool fullSuite = Environment.GetEnvironmentVariable("YGO_BENCH_FULL_SUITE") == "1";
                bool openingOnly = Environment.GetEnvironmentVariable("YGO_BENCH_OPENINGS_ONLY") == "1";
                bool captureReplays = Environment.GetEnvironmentVariable("YGO_BENCH_CAPTURE_REPLAYS") == "1";
                bool randomOpponent = Environment.GetEnvironmentVariable("YGO_BENCH_RANDOM_OPPONENT") == "1";
                string fixture = Environment.GetEnvironmentVariable("YGO_BENCH_FIXTURE");
                var matchups = gxSuite ? GxMatchups : fullSuite ? Matchups.Concat(StyleMatchups).ToArray() : styleSuite ? StyleMatchups : Matchups;
                if (randomOpponent) Debug.Log($"[AI BENCH] Facing {RandomLegalPilot.Version} with independent fixed seeds.");
                if (fullSuite || styleSuite)
                {
                    VerifyDeckStyles(decks);
                    VerifyResourceStrategy(decks, general, links);
                }
                bool referenceOnly = Environment.GetEnvironmentVariable("YGO_BENCH_REFERENCE_ONLY") == "1";
                string previousOutput = Environment.GetEnvironmentVariable("YGO_BENCH_PREVIOUS_REPORT");
                var previous = string.IsNullOrEmpty(previousOutput) ? null : LoadPilotFactory("AiBenchmarkPreviousFactory");
                Pilot Candidate(int level)
                {
                    if (referenceOnly) return baseline(level);
                    var ai = new TacticalAI(general, links, searchCombos: true);
                    return TimedPilot(ai.ComputeResponse, ai.AdvanceComboPlanning,
                        () => ai.SimulatedComboBranches, () => ai.ChosenComboAlternatives);
                }

                if (Environment.GetEnvironmentVariable("YGO_BENCH_ROSTER_LEVELS") == "1")
                {
                    AuditRoster(output, start, stride, Candidate, () => nativeErrors, captureReplays);
                    return;
                }

                using var file = new StreamWriter(output) { AutoFlush = true };
                using var previousFile = previous == null ? null : new StreamWriter(previousOutput) { AutoFlush = true };
                for (int index = 0; index < matchups.Length; index++)
                {
                    var scenario = matchups[index];
                    for (int seed = 0; seed < seeds; seed++)
                    {
                        if ((index * seeds + seed) % stride != start) continue;
                        if (!string.IsNullOrEmpty(fixture) && fixture != $"{index}:{seed}") continue;
                        var first = decks[(seed & 1) == 0 ? scenario.FirstDeck : scenario.SecondDeck];
                        var second = decks[(seed & 1) == 0 ? scenario.SecondDeck : scenario.FirstDeck];
                        int firstLevel = OpponentDeckLevels.GetLevel(first.charId, first.id);
                        int secondLevel = OpponentDeckLevels.GetLevel(second.charId, second.id);
                        ulong randomSeed = 0xB2A9345F2C186B67UL ^ ((ulong)(uint)index << 33) ^ ((ulong)(uint)seed << 1);
                        Match PlaySeat(Func<int, Pilot> tested, int seat)
                        {
                            int before = nativeErrors;
                            int rivalLevel = seat == 0 ? secondLevel : firstLevel;
                            var rival = randomOpponent ? RandomLegalPilot.Create(randomSeed ^ (ulong)(1 - seat)) : baseline(rivalLevel);
                            var result = Play(index, seed, seat, scenario.Era, scenario.Method, first, second,
                                seat == 0 ? tested(firstLevel) : rival, seat == 1 ? tested(secondLevel) : rival,
                                openingOnly, captureReplays);
                            result.nativeErrors = nativeErrors - before;
                            return result;
                        }

                        Match opening = null;
                        for (int seat = 0; seat < 2; seat++)
                        {
                            bool previousFirst = previous != null && ((index + seed + seat) & 1) == 0;
                            var prior = previousFirst ? PlaySeat(previous, seat) : null;
                            var current = PlaySeat(Candidate, seat);
                            opening ??= current;
                            VerifyOpeningHands(opening, current);
                            if (previous != null)
                            {
                                prior ??= PlaySeat(previous, seat);
                                VerifyOpeningHands(opening, prior);
                                prior.interleavedOrder = previousFirst ? 1 : 2;
                                current.interleavedOrder = previousFirst ? 2 : 1;
                                previousFile.WriteLine(JsonUtility.ToJson(prior));
                            }
                            file.WriteLine(JsonUtility.ToJson(current));
                        }
                    }
                    Debug.Log($"[AI BENCH] {scenario.Era} {scenario.FirstDeck}/{scenario.SecondDeck} done, native errors {nativeErrors}.");
                }
            }
            catch (Exception error)
            {
                exit = 1;
                Debug.LogException(error);
            }
            finally
            {
                YgoNativeBridge.Shutdown();
                if (Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }

        private static Func<int, Pilot> LoadPilotFactory(string name) =>
            (Func<int, Pilot>)Delegate.CreateDelegate(typeof(Func<int, Pilot>),
                typeof(AiMatchBenchmark).Assembly.GetType("YGOPortable.Editor." + name)?.GetMethod(
                    "Create", BindingFlags.Public | BindingFlags.Static) ??
                throw new InvalidOperationException($"Missing {name}. Run Tools/benchmark_duel_ai.py."));

        private static void VerifyOpeningHands(Match expected, Match actual)
        {
            for (int seat = 0; seat < 2; seat++)
                if (!expected.sides[seat].initial.SequenceEqual(actual.sides[seat].initial))
                    throw new InvalidOperationException($"Opening hands changed in fixture {expected.matchup}/{expected.seed}, seat {seat}.");
        }

        private static Match ReplayRecordedMatch(Match original, int inspectIndex, string alternateAnswer, bool auditBattle,
            Dictionary<int, PresetDeckInfo> decks, Func<int> nativeErrors)
        {
            if (original?.replay is not { Length: > 0 } || original.sides is not { Length: 2 } ||
                original.finalLife is not { Length: 2 } || inspectIndex >= original.replay.Length)
                throw new InvalidOperationException("The match lacks a valid recorded transcript or inspection step.");
            int next = 0;
            var pilot = new Pilot((duel, prompt) =>
            {
                if (next >= original.replay.Length)
                    throw new InvalidOperationException($"The duel requested an extra response at step {next}.");
                int step = next++;
                if (step == inspectIndex) InspectReplayPrompt(duel, prompt, step, original.replay[step].answer);
                var answer = ReadReplayResponse(duel, original.replay[step], step);
                if (auditBattle) AiBattleProbe.Audit(duel, prompt, answer, step);
                return answer;
            }, () => 0);
            int before = nativeErrors();
            var first = decks[original.sides[0].deck];
            var second = decks[original.sides[1].deck];
            var replayed = Play(original.matchup, original.seed, original.candidate, original.era, original.method,
                first, second, pilot, pilot, original.openingOnly);
            replayed.nativeErrors = nativeErrors() - before;
            bool same = next == original.replay.Length && replayed.turns == original.turns &&
                replayed.winner == original.winner && replayed.capped == original.capped &&
                replayed.nativeErrors == original.nativeErrors && replayed.finalLife.SequenceEqual(original.finalLife);
            for (int seat = 0; seat < 2 && same; seat++)
            {
                var actual = replayed.sides[seat];
                var expected = original.sides[seat];
                same = actual.retries == expected.retries && actual.initial.SequenceEqual(expected.initial) &&
                    actual.exposedAttackers == expected.exposedAttackers && actual.selfDestruction == expected.selfDestruction &&
                    actual.wastedHandtraps == expected.wastedHandtraps &&
                    actual.exposureTraces.Select(JsonUtility.ToJson).SequenceEqual(expected.exposureTraces.Select(JsonUtility.ToJson)) &&
                    actual.destructionTraces.Select(JsonUtility.ToJson).SequenceEqual(expected.destructionTraces.Select(JsonUtility.ToJson));
            }
            if (!same)
                throw new InvalidOperationException($"The transcript changed the final duel state or critical evidence after {next} responses.");
            if (!string.IsNullOrEmpty(alternateAnswer))
            {
                using var trial = new DuelSession();
                StartFixture(trial, original.matchup, original.seed, first, second);
                for (int step = 0; step <= inspectIndex; step++)
                {
                    if (trial.ActivePrompt == null && !trial.IsFinished) trial.StepUntilPrompt();
                    var response = ReadReplayResponse(trial, original.replay[step], step);
                    trial.SubmitResponse(step == inspectIndex ? ParseReplayAnswer(alternateAnswer) : response);
                    if (trial.RetryCount != 0 || nativeErrors() != before + replayed.nativeErrors)
                        throw new InvalidOperationException($"The engine rejected the alternate replay at step {step}.");
                }
                Debug.Log($"[AI REPLAY ALTERNATIVE] Accepted {alternateAnswer} at step {inspectIndex}. " +
                    $"After the response: turn {trial.TurnNumber}, message {trial.ActivePrompt?.messageType}, " +
                    $"LP {trial.Players[0].lp}/{trial.Players[1].lp}.");
                InspectReplayBoard(trial);
            }
            Debug.Log($"[AI REPLAY VERIFIED] Fixture {original.matchup}/{original.seed}/{original.candidate}, " +
                $"{next} responses, turn {original.turns}, winner {original.winner}.");
            return replayed;
        }

        private static byte[] ReadReplayResponse(DuelSession duel, ReplayResponse response, int step)
        {
            if (duel.ActivePrompt is not { } prompt || response.turn != duel.TurnNumber || response.phase != duel.CurrentPhase ||
                response.player != prompt.playerId || response.message != prompt.messageType || response.hint != prompt.SelectionHint)
                throw new InvalidOperationException($"The recorded duel diverged at step {step}.");
            return ParseReplayAnswer(response.answer);
        }

        private static byte[] ParseReplayAnswer(string answer) => string.IsNullOrEmpty(answer) ? Array.Empty<byte>() :
            answer.Split('-').Select(part => Convert.ToByte(part, 16)).ToArray();

        private static void InspectReplayPrompt(DuelSession duel, DuelPrompt prompt, int step, string answer)
        {
            string Describe(IReadOnlyList<SelectableCardItem> items) => string.Join(", ",
                items.Select((item, index) => $"{index}={item.code}@{item.controller}/{item.location}/{item.sequence}"));
            Debug.Log($"[AI REPLAY PROMPT] Step {step}, turn {duel.TurnNumber}, phase {duel.CurrentPhase}, " +
                $"player {prompt.playerId}, message {prompt.messageType}, hint {prompt.SelectionHint}, answer {answer}.\n" +
                $"Normal Summon: {Describe(prompt.summonable)}\nSpecial Summon: {Describe(prompt.spSummonable)}\n" +
                $"Set: {Describe(prompt.mSetable)}\nReposition: {Describe(prompt.repositionable)}\n" +
                $"Activate: {Describe(prompt.activatable)}\nCandidates: {Describe(prompt.candidates)}\n" +
                $"Positions: {prompt.allowedPositions}; LP: {duel.Players[0].lp}/{duel.Players[1].lp}");
            InspectReplayBoard(duel);
        }

        private static void InspectReplayBoard(DuelSession duel)
        {
            for (int seat = 0; seat < duel.Players.Length; seat++)
                Debug.Log($"[AI REPLAY BOARD] {seat}: " + string.Join(", ", duel.Players[seat].mzone
                    .Where(card => card != null)
                    .Select(card => $"{card.code}@{card.sequence}:ATK{card.attack}/DEF{card.defense}/pos{card.position}")));
        }

        private static void StartFixture(DuelSession duel, int matchup, int seed, PresetDeckInfo first, PresetDeckInfo second)
        {
            ulong n = 179426549UL + (ulong)matchup * 104729UL + (ulong)seed * 8089UL;
            if (!duel.StartNewDuel(first.main, first.extra, second.main, second.extra,
                seed: new[] { n * 29 + 101, n * 47 + 7, n * 71 + 13, n * 97 + 3 }))
                throw new InvalidOperationException($"Could not start fixture {matchup}/{seed}.");
        }

        private static void AuditRoster(string output, int start, int stride, Func<int, Pilot> candidate, Func<int> nativeErrors, bool captureReplays)
        {
            var roster = DeckRepository.Characters.SelectMany(character => OpponentDeckLevels.GetDecks(character.id)).ToArray();
            if (roster.Length < 2 || roster.Any(deck => deck.main == null || deck.main.Length is < 40 or > 60))
                throw new InvalidOperationException("Invalid opponent deck roster.");
            var levels = roster.ToDictionary(deck => deck.id, deck => OpponentDeckLevels.GetLevel(deck.charId, deck.id));
            File.WriteAllLines(output + ".roster", roster.Select(deck => deck.id.ToString()));
            using var file = new StreamWriter(output) { AutoFlush = true };
            for (int index = start; index < roster.Length; index += stride)
            {
                var first = roster[index];
                var second = roster[(index + roster.Length / 2) % roster.Length];
                int before = nativeErrors();
                var match = Play(index, 3, 0, "All deck levels", 0, first, second,
                    candidate(levels[first.id]), candidate(levels[second.id]), openingOnly: true, captureReplays: captureReplays);
                match.sides[0].level = levels[first.id];
                match.sides[1].level = levels[second.id];
                match.nativeErrors = nativeErrors() - before;
                file.WriteLine(JsonUtility.ToJson(match));
                if ((index / stride) % 100 == 99)
                    Debug.Log($"[AI ROSTER] {index / stride + 1} duels checked by worker {start}.");
            }
        }

        private static void VerifyDecisionRules()
        {
            VerifyOwnOnlyRemoval();
            VerifyReactiveNegation();
            VerifyEqualScalesProtection();
            VerifyStrongerTributeProtection();
            VerifyLethalAttackPreservesAllies();
            using var duel = new DuelSession();
            var own = duel.Players[0];
            var rival = duel.Players[1];
            const uint mzone = YgoConstants.LOCATION_MZONE;
            var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };
            var hotRed = new SelectableCardItem { code = 39765958, location = mzone, sequence = 0 };
            own.mzone[0] = new LiveCardState { code = hotRed.code, position = YgoConstants.POS_FACEUP_ATTACK, attack = 3000 };
            own.mzone[1] = new LiveCardState { code = 8240199, sequence = 1, position = YgoConstants.POS_FACEUP_ATTACK };
            if (DuelTactics.CanActivate(duel, idle, hotRed)) throw new InvalidOperationException("Hot Red would wipe only its own field.");
            rival.mzone[0] = new LiveCardState { controller = 1, code = 89631139, position = YgoConstants.POS_FACEUP_ATTACK, attack = 3000 };
            if (!DuelTactics.CanActivate(duel, idle, hotRed)) throw new InvalidOperationException("Hot Red lost a profitable opposing target.");
            var tyrant = new SelectableCardItem { code = 16172067, location = mzone, description = 16172067UL << 20 };
            own.mzone[0].code = tyrant.code;
            rival.mzone[0] = null;
            if (DuelTactics.CanActivate(duel, idle, tyrant)) throw new InvalidOperationException("Tyrant would wipe only allied cards.");
            rival.mzone[0] = new LiveCardState { controller = 1, code = 89631139, position = YgoConstants.POS_FACEUP_ATTACK, attack = 3000 };
            if (!DuelTactics.CanActivate(duel, idle, tyrant)) throw new InvalidOperationException("The Tyrant board clear lost its profitable line.");
            var rampage = new SelectableCardItem { code = 84058253, location = mzone, isTriggeredEffect = true };
            own.szone[1] = new LiveCardState { code = 82428674 };
            if (DuelTactics.CanActivate(duel, idle, rampage)) throw new InvalidOperationException("Rampage would target its own backrow.");
            var chain = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN };
            rampage.isTriggeredEffect = false;
            if (DuelTactics.CanActivate(duel, chain, rampage) || !DuelTactics.CanActivate(duel, idle, rampage))
                throw new InvalidOperationException("Rampage's unlabelled summon trigger and milling effect need different targets.");
            rival.szone[1] = new LiveCardState { code = 10045474 };
            if (!DuelTactics.CanActivate(duel, chain, rampage)) throw new InvalidOperationException("Rampage missed opposing backrow.");
            own.szone[1] = rival.szone[1] = null;
            var cyclone = new SelectableCardItem { code = 5133471, location = YgoConstants.LOCATION_GRAVE };
            own.szone[1] = new LiveCardState { code = 897409, position = YgoConstants.POS_FACEUP_ATTACK };
            if (DuelTactics.CanActivate(duel, idle, cyclone)) throw new InvalidOperationException("Galaxy Cyclone would destroy only its own continuous Spell.");
            rival.szone[1] = new LiveCardState { code = 66399653, position = YgoConstants.POS_FACEUP_ATTACK };
            if (!DuelTactics.CanActivate(duel, idle, cyclone)) throw new InvalidOperationException("Galaxy Cyclone missed an opposing face-up Spell.");
            own.szone[1] = rival.szone[1] = null;
            var gryphon = new SelectableCardItem { code = 28406301, location = YgoConstants.LOCATION_PZONE, description = 28406301UL << 20 };
            own.mzone[0] = new LiveCardState { attack = 2600, race = YgoConstants.RACE_FIEND, position = YgoConstants.POS_FACEUP_ATTACK };
            own.grave.Add(new LiveCardState { code = 46372010 });
            if (DuelTactics.CanActivate(duel, idle, gryphon)) throw new InvalidOperationException("Gryphon would destroy its scale without a Battle Phase.");
            idle.canBp = true;
            if (!DuelTactics.CanActivate(duel, idle, gryphon)) throw new InvalidOperationException("Gryphon missed a boost that beats a blocker.");
            idle.canBp = false;
            own.grave.Clear();
            var sage = new SelectableCardItem { code = 8240199, location = YgoConstants.LOCATION_HAND };
            idle.mSetable.Add(sage);
            Array.Clear(own.mzone, 0, own.mzone.Length);
            if (DuelTactics.ChooseDefensiveSet(duel, idle, sage, followupUnproven: true) != 0)
                throw new InvalidOperationException("The unproven Sage opening did not choose defense against Blue-Eyes.");
            var doppel = new SelectableCardItem { code = 53855409, location = YgoConstants.LOCATION_HAND, sequence = 1 };
            idle.mSetable.Add(doppel);
            own.mzone[0] = new LiveCardState { position = YgoConstants.POS_FACEUP_DEFENSE, defense = 1200 };
            own.lp = 1700;
            rival.mzone[0].attack = 3300;
            if (DuelTactics.ChooseDefensiveSet(duel, idle, doppel, followupUnproven: true) != 1)
                throw new InvalidOperationException("A defender elsewhere would not protect Doppelwarrior from a lethal attack.");
            own.mzone[1] = new LiveCardState { position = YgoConstants.POS_FACEUP_ATTACK, attack = 400, sequence = 1 };
            idle.repositionable.Add(new SelectableCardItem { location = mzone, sequence = 1 });
            if (DuelTactics.ChooseDefensiveReposition(duel, idle, beforeBattle: true) != 0)
                throw new InvalidOperationException("The weak attacker must change position before a battle prevents it from defending.");
            own.lp = 8000;
            var negated = new ChainLinkInfo { code = 32807846, triggerPlayer = 1, negated = true };
            duel.CurrentChain.Add(negated);
            var ash = new SelectableCardItem { code = 14558127, location = YgoConstants.LOCATION_HAND };
            if (DuelTactics.CanActivate(duel, idle, ash)) throw new InvalidOperationException("Ash would answer an already negated effect.");
            negated.negated = false;
            if (!DuelTactics.CanActivate(duel, idle, ash)) throw new InvalidOperationException("Ash lost a valid live opposing activation.");
            var gift = new SelectableCardItem { code = 39526584, location = YgoConstants.LOCATION_SZONE };
            if (DuelTactics.CanActivate(duel, idle, gift)) throw new InvalidOperationException("Gift Card would heal the opponent.");
            var darkCure = new SelectableCardItem { code = 3701074, location = YgoConstants.LOCATION_SZONE };
            if (DuelTactics.CanActivate(duel, idle, darkCure)) throw new InvalidOperationException("Dark Cure would heal the opponent without a conversion effect.");
            own.mzone[0] = new LiveCardState { code = 67316075, position = YgoConstants.POS_FACEUP_ATTACK };
            if (!DuelTactics.CanActivate(duel, idle, gift)) throw new InvalidOperationException("Gift Card missed the active burn conversion.");
            if (!DuelTactics.CanActivate(duel, idle, darkCure)) throw new InvalidOperationException("Dark Cure missed the active burn conversion.");

            duel.CurrentChain.Clear();
            duel.CurrentChain.Add(new ChainLinkInfo { code = 98829635, chainNumber = 1, triggerPlayer = 0 });
            var ally = new SelectableCardItem { code = 67316075, location = mzone, controller = 0 };
            var enemy = new SelectableCardItem { code = 89631139, location = mzone, controller = 1 };
            if (DuelTactics.SelectionPriority(duel, 0, enemy, DuelTactics.ApplyTo) <=
                DuelTactics.SelectionPriority(duel, 0, ally, DuelTactics.ApplyTo))
                throw new InvalidOperationException("A proactive Forbidden Crown would disable the AI's own monster.");

            var battle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_BATTLECMD, canEp = true };
            own.mzone[0] = new LiveCardState { attack = 3200, position = YgoConstants.POS_FACEUP_ATTACK };
            own.mzone[1] = new LiveCardState { attack = 1000, sequence = 1, position = YgoConstants.POS_FACEUP_ATTACK };
            rival.mzone[0] = new LiveCardState { controller = 1, defense = 900, position = YgoConstants.POS_FACEUP_DEFENSE };
            rival.lp = 3200;
            battle.attackable.Add(new SelectableCardItem { sequence = 0 });
            battle.attackable.Add(new SelectableCardItem { sequence = 1 });
            if (DuelCombat.FindLethal(duel, battle) is not { Attacker: 1, Target: 0u })
                throw new InvalidOperationException("The weaker attacker must remove the blocker before the 3200 direct attack.");
            if (!DuelCombat.CanCloseBattleFromMain(duel, 0))
                throw new InvalidOperationException("The AI could enter battle to remove the blocker and win this turn.");
            own.mzone[0].attack = 2500;
            own.mzone[1].attack = 2100;
            rival.mzone[0] = new LiveCardState { code = 23205979, controller = 1, attack = 300,
                position = YgoConstants.POS_FACEUP_ATTACK };
            rival.lp = 3900;
            if (DuelCombat.FindLethal(duel, battle) is not { Target: 0u })
                throw new InvalidOperationException("Two attacks against Spirit Reaper can deal lethal while it stays on the field.");
            rival.lp = 4100;
            if (DuelCombat.FindLethal(duel, battle) != null)
                throw new InvalidOperationException("Spirit Reaper still blocks the second direct attack.");
            rival.mzone[0] = null;
            foreach (var attacker in battle.attackable) attacker.directAttackable = true;
            var battlePilot = new TacticalAI(null);
            rival.lp = 2000;
            int firstAttack = BitConverter.ToInt32(battlePilot.ComputeResponse(duel, battle), 0);
            rival.lp = 4500;
            int consecutiveAttack = BitConverter.ToInt32(battlePilot.ComputeResponse(duel, battle), 0);
            if (firstAttack != ((1 << 16) | 1) || consecutiveAttack != firstAttack)
                throw new InvalidOperationException("A legal consecutive attack must stay available when both attacks are needed.");
            idle.canBp = true;
            rival.mzone[0] = new LiveCardState { attack = 2700, position = YgoConstants.POS_FACEUP_ATTACK };
            rival.mzone[1] = new LiveCardState { attack = 2300, position = YgoConstants.POS_FACEUP_ATTACK };
            if (!DuelTactics.ShouldLeadWithLightningStorm(duel, idle))
                throw new InvalidOperationException("Lightning Storm should use its activation window before the first summon.");
            rival.mzone[1].position = YgoConstants.POS_FACEUP_DEFENSE;
            if (DuelTactics.ShouldLeadWithLightningStorm(duel, idle))
                throw new InvalidOperationException("A defense-position monster does not count as a target for Lightning Storm's monster clear.");
            var mode = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_OPTION };
            mode.options.Add((23064604UL << 20) | 3);
            mode.options.Add((23064604UL << 20) | 4);
            if (DuelTactics.ChooseOption(duel, mode) != 1)
                throw new InvalidOperationException("Erebus missed a known opposing field target.");
            mode.options.Clear();
            mode.options.Add((74169516UL << 20) | 2);
            mode.options.Add((74169516UL << 20) | 3);
            if (DuelTactics.ChooseOption(duel, mode) != 1)
                throw new InvalidOperationException("The Millennium Golem should reveal the Ankh when both costs are offered.");
            Array.Clear(own.mzone, 0, own.mzone.Length);
            own.mzone[0] = new LiveCardState { code = SignatureTurnPlan.ScarredDragonArchfiend,
                type = YgoConstants.TYPE_SYNCHRO, position = YgoConstants.POS_FACEUP_ATTACK, attack = 3000, defense = 2500 };
            var powerVice = new SelectableCardItem { code = 19434243, location = YgoConstants.LOCATION_HAND };
            if (!DuelTactics.NeedsTributeProjection(duel, idle, powerVice))
                throw new InvalidOperationException("An unproven tribute would replace the established Synchro with Power Vice Dragon.");
            own.mzone[1] = new LiveCardState { attack = 100 };
            own.mzone[2] = new LiveCardState { attack = 600 };
            if (DuelTactics.NeedsTributeProjection(duel, idle, powerVice))
                throw new InvalidOperationException("A safe tribute is available without spending the Synchro.");
            idle.spSummonable.Add(new SelectableCardItem { code = SignatureTurnPlan.ScarredDragonArchfiend });
            if (DuelTactics.CanSummonRedDragonSafely(duel, idle, 70902743))
                throw new InvalidOperationException("Scarred keeps the other monsters when Red Dragon Archfiend has no board to clear.");
            rival.mzone[0].position = YgoConstants.POS_FACEUP_DEFENSE;
            if (!DuelTactics.CanSummonRedDragonSafely(duel, idle, 70902743))
                throw new InvalidOperationException("Red Dragon Archfiend can clear the opposing defenders during battle.");
            Array.Clear(own.mzone, 0, own.mzone.Length);
            own.lp = 5500;
            rival.mzone[0] = new LiveCardState { attack = 5000, position = YgoConstants.POS_FACEUP_ATTACK };
            rival.mzone[1] = new LiveCardState { attack = 1500, position = YgoConstants.POS_FACEUP_ATTACK };
            var wizard = new SelectableCardItem { code = 98555327, location = YgoConstants.LOCATION_HAND, sequence = 2 };
            idle.mSetable.Add(wizard);
            if (DuelTactics.ChooseDefensiveSet(duel, idle, wizard, followupUnproven: true, lethalOnly: true) != 2)
                throw new InvalidOperationException("A second attacker could finish the duel through Galaxy Wizard.");
            idle.canBp = false;
            idle.spSummonable.Clear();
            own.mzone[0] = new LiveCardState { code = 70902743, type = YgoConstants.TYPE_SYNCHRO,
                position = YgoConstants.POS_FACEUP_ATTACK };
            own.mzone[1] = new LiveCardState { position = YgoConstants.POS_FACEUP_DEFENSE };
            idle.spSummonable.Add(new SelectableCardItem { code = SignatureTurnPlan.RedDragonAbyss,
                location = YgoConstants.LOCATION_EXTRA });
            if (DuelTactics.ChooseArchfiendEndPhaseUpgrade(duel, idle) != 0)
                throw new InvalidOperationException("The Main Phase 2 Synchro can keep the defender through the End Phase.");
            battle.attackable.RemoveAt(1);
            own.mzone[0].attack = 1500;
            rival.mzone[0] = new LiveCardState { code = 95134948, attack = 6000, position = YgoConstants.POS_FACEUP_ATTACK };
            if (DuelCombat.ChooseAttacker(duel, battle, mustAttack: false) != -1 ||
                DuelCombat.ChooseAttacker(duel, battle, mustAttack: true) != 0)
                throw new InvalidOperationException("A lone weak attacker should avoid Dragonar unless an attack is required.");
            own.lp = 900;
            rival.mzone[0].attack = 1600;
            rival.mzone[1] = null;
            var gazelle = new SelectableCardItem { code = 26889158, location = YgoConstants.LOCATION_HAND, sequence = 3 };
            idle.mSetable.Add(gazelle);
            if (DuelTactics.ChooseDefensiveSet(duel, idle, gazelle, followupUnproven: true, lethalOnly: true) != idle.mSetable.Count - 1)
                throw new InvalidOperationException("A weak normal without a proven follow-up must respect the low-life battle risk.");
            VerifySpotRemoval();
            Debug.Log("[AI BENCH RULES] Field wipe, defensive set, active negate, disruption targets and lethal attack order passed.");
        }

        private static void VerifySpotRemoval()
        {
            const uint typhoon = 5318639, conquistador = 20590515, eldlich = 95440946, sapphire = 11091375;
            using var duel = new DuelSession();
            var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD };
            var owner = duel.Players[0];
            var opponent = duel.Players[1];
            owner.szone[0] = new LiveCardState { position = YgoConstants.POS_FACEDOWN_DEFENSE };
            var spell = new SelectableCardItem { code = typhoon, description = (ulong)typhoon << 20 };
            if (DuelTactics.CanActivate(duel, idle, spell))
                throw new InvalidOperationException("Typhoon would destroy only an allied set card.");
            opponent.szone[0] = new LiveCardState { position = YgoConstants.POS_FACEDOWN_DEFENSE };
            if (!DuelTactics.CanActivate(duel, idle, spell))
                throw new InvalidOperationException("Typhoon missed the opposing set card.");

            owner.mzone[0] = new LiveCardState { code = eldlich, attack = 2500, position = YgoConstants.POS_FACEUP_ATTACK };
            duel.CurrentChain.Add(new ChainLinkInfo { code = conquistador, triggerPlayer = 0 });
            var destroy = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_YESNO, promptDesc = ((ulong)conquistador << 20) | 2 };
            if (DuelTactics.ChooseResolutionYesNo(duel, destroy) != 0)
                throw new InvalidOperationException("Conquistador would destroy its own Eldlich with no face-up opponent.");
            opponent.mzone[0] = new LiveCardState { code = sapphire, attack = 1900, position = YgoConstants.POS_FACEUP_ATTACK };
            var rival = new SelectableCardItem { controller = 1, code = sapphire, location = YgoConstants.LOCATION_MZONE };
            var ally = new SelectableCardItem { controller = 0, code = eldlich, location = YgoConstants.LOCATION_MZONE };
            if (DuelTactics.ChooseResolutionYesNo(duel, destroy) != 1 ||
                DuelTactics.SelectionPriority(duel, 0, rival, 0) <= DuelTactics.SelectionPriority(duel, 0, ally, 0))
                throw new InvalidOperationException("Conquistador must destroy the face-up enemy and keep its own Eldlich.");
        }

        private static void VerifyOwnOnlyRemoval()
        {
            using var duel = new DuelSession();
            var own = duel.Players[0];
            var rival = duel.Players[1];
            const uint redZone = 50056656;
            own.szone[0] = new LiveCardState { code = redZone, position = YgoConstants.POS_FACEUP_ATTACK };
            var confirmation = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_EFFECTYN,
                promptCardCode = redZone, promptDesc = 94, promptLoc = YgoConstants.LOCATION_SZONE };
            int ConfirmRedZone() => BitConverter.ToInt32(new TacticalAI(null).ComputeResponse(duel, confirmation), 0);
            if (ConfirmRedZone() != 0)
                throw new InvalidOperationException("Red Zone would apply its additional effect with only allied targets.");
            own.banished.Add(new LiveCardState { code = 70902743, type = YgoConstants.TYPE_MONSTER | YgoConstants.TYPE_SYNCHRO,
                race = YgoConstants.RACE_DRAGON, attribute = YgoConstants.ATTRIBUTE_DARK, position = YgoConstants.POS_FACEUP_ATTACK });
            var mode = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_OPTION };
            mode.options.Add((ulong)redZone << 20);
            mode.options.Add(((ulong)redZone << 20) | 1);
            if (ConfirmRedZone() != 1 || DuelTactics.ChooseOption(duel, mode) != 1)
                throw new InvalidOperationException("Red Zone missed its revival when only allied cards could be destroyed.");
            own.banished.Clear();

            var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD };
            foreach (uint code in new uint[] { 97584719, 35595518, 21223277 })
            {
                var removal = new SelectableCardItem { code = code, location = YgoConstants.LOCATION_MZONE,
                    description = ((ulong)code << 20) | 1 };
                rival.szone[0] = null;
                if (DuelTactics.CanActivate(duel, idle, removal))
                    throw new InvalidOperationException($"{code} would pay to destroy only allied cards.");
                rival.szone[0] = new LiveCardState { code = 10045474, controller = 1 };
                if (!DuelTactics.CanActivate(duel, idle, removal))
                    throw new InvalidOperationException($"{code} missed available opposing backrow removal.");
            }
            if (ConfirmRedZone() != 1)
                throw new InvalidOperationException("Red Zone missed an available opposing removal target.");
        }

        private static void VerifyReactiveNegation()
        {
            using var duel = new DuelSession();
            const uint impermanence = 10045474, kaibaman = 67768675, tyrant = 11443677;
            const uint monsterZone = YgoConstants.LOCATION_MZONE, spellZone = YgoConstants.LOCATION_SZONE;
            var rival = duel.Players[1];
            rival.mzone[5] = new LiveCardState { code = tyrant, controller = 1, location = monsterZone, sequence = 5,
                position = YgoConstants.POS_FACEUP_ATTACK, attack = 3400 };
            var chain = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN };
            var negate = new SelectableCardItem { code = impermanence, location = spellZone, sequence = 2 };
            if (DuelTactics.CanActivate(duel, chain, negate))
                throw new InvalidOperationException("Impermanence targets a trap-immune monster across an empty column.");
            rival.szone[2] = new LiveCardState { code = 55144522, controller = 1, position = YgoConstants.POS_FACEUP_ATTACK };
            if (DuelTactics.CanActivate(duel, chain, negate))
                throw new InvalidOperationException("Set Impermanence spent its negation on an idle column.");
            duel.CurrentChain.Add(new ChainLinkInfo { code = 55144522, triggerPlayer = 1, triggerLoc = spellZone,
                triggerSeq = 2, ActivationSource = (1, spellZone, 2) });
            if (!DuelTactics.CanActivate(duel, chain, negate))
                throw new InvalidOperationException("Set Impermanence missed a Spell activated in its column.");
            duel.CurrentChain.Clear();
            rival.szone[2] = null;

            rival.mzone[3] = new LiveCardState { code = kaibaman, controller = 1, location = monsterZone, sequence = 3,
                position = YgoConstants.POS_FACEUP_ATTACK, attack = 200 };
            duel.CurrentChain.Add(new ChainLinkInfo { code = kaibaman, triggerPlayer = 1, triggerLoc = monsterZone, triggerSeq = 3 });
            if (!DuelTactics.CanActivate(duel, chain, negate))
                throw new InvalidOperationException("Impermanence missed the live opposing monster effect.");
            duel.CurrentChain.Add(new ChainLinkInfo { code = impermanence, triggerLoc = spellZone, triggerSeq = 2 });

            var target = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, min = 1, max = 1,
                SelectionHint = DuelTactics.Negate };
            target.candidates.Add(new SelectableCardItem { code = tyrant, controller = 1, location = monsterZone, sequence = 5 });
            target.candidates.Add(new SelectableCardItem { code = kaibaman, controller = 1, location = monsterZone, sequence = 3 });
            byte[] response = new TacticalAI(null).ComputeResponse(duel, target);
            if (response.Length != 12 || BitConverter.ToInt32(response, 8) != 1)
                throw new InvalidOperationException("Impermanence must target the effect it chained to.");
        }

        private static void VerifyEqualScalesProtection()
        {
            using var duel = new DuelSession();
            const uint trapCode = 17885118;
            var own = duel.Players[0];
            var rival = duel.Players[1];
            const uint scaleType = YgoConstants.TYPE_PENDULUM | YgoConstants.TYPE_SPELL;
            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN };
            var effect = new SelectableCardItem { code = trapCode, location = YgoConstants.LOCATION_SZONE, sequence = 2 };
            own.szone[0] = new LiveCardState { code = 41908872, type = scaleType, lscale = 8, sequence = 0,
                position = YgoConstants.POS_FACEUP_ATTACK };
            own.szone[4] = new LiveCardState { code = 40318957, type = scaleType, rscale = 8, sequence = 4,
                position = YgoConstants.POS_FACEUP_ATTACK };
            own.szone[2] = new LiveCardState { code = trapCode, sequence = 2 };
            if (DuelTactics.CanActivate(duel, prompt, effect))
                throw new InvalidOperationException("Pendulum Scale would destroy its only two scales without a known rebuild.");
            rival.szone[1] = new LiveCardState { code = 53582587, controller = 1, sequence = 1 };
            if (!DuelTactics.CanActivate(duel, prompt, effect))
                throw new InvalidOperationException("Equal scales missed a valid opposing Spell/Trap target.");
            rival.szone[1] = null;
            own.szone[4].rscale = 5;
            if (!DuelTactics.CanActivate(duel, prompt, effect))
                throw new InvalidOperationException("Pendulum Scale blocked an effect with unequal scales.");
        }

        private static void VerifyStrongerTributeProtection()
        {
            using var duel = new DuelSession();
            var own = duel.Players[0];
            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD };
            var extraSurveyor = new SelectableCardItem { code = 74387963, location = YgoConstants.LOCATION_HAND };
            own.mzone[0] = new LiveCardState { code = 5997110, attack = 2000, defense = 2000,
                position = YgoConstants.POS_FACEUP_ATTACK };
            if (!DuelTactics.NeedsTributeProjection(duel, prompt, extraSurveyor))
                throw new InvalidOperationException("A weaker tribute summon would replace the only established attacker.");
            own.mzone[1] = new LiveCardState { attack = 200, defense = 200, position = YgoConstants.POS_FACEUP_DEFENSE };
            if (DuelTactics.NeedsTributeProjection(duel, prompt, extraSurveyor))
                throw new InvalidOperationException("A disposable tribute is available without replacing the stronger attacker.");
        }

        private static void VerifyLethalAttackPreservesAllies()
        {
            using var duel = new DuelSession();
            var own = duel.Players[0];
            var rival = duel.Players[1];
            var battle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_BATTLECMD };
            own.mzone[0] = new LiveCardState { attack = 3000, position = YgoConstants.POS_FACEUP_ATTACK };
            own.mzone[1] = new LiveCardState { attack = 6000, sequence = 1, position = YgoConstants.POS_FACEUP_ATTACK };
            rival.mzone[0] = new LiveCardState { attack = 3000, position = YgoConstants.POS_FACEUP_ATTACK };
            battle.attackable.Add(new SelectableCardItem { sequence = 0 });
            battle.attackable.Add(new SelectableCardItem { sequence = 1 });
            rival.lp = 5200;
            if (DuelCombat.FindLethal(duel, battle) is not { Attacker: 1, Target: 0u })
                throw new InvalidOperationException("Lethal should keep both allies when the stronger attacker can clear the blocker.");
            own.mzone[1].attack = 3000;
            rival.lp = 1700;
            if (DuelCombat.FindLethal(duel, battle) is not { Attacker: 0, Target: 0u })
                throw new InvalidOperationException("Lethal may still trade one attacker when the other can finish.");
        }

        private static void VerifyNativeMainBattleAudit()
        {
            var monsters = Enumerable.Repeat(20721928u, 40).ToArray();
            var extra = Array.Empty<uint>();
            var ai = new TacticalAI(null);
            int featureCount = LinkComboPolicy.InitialWeights().Length;
            var legacy = new LinkComboPolicy(Enumerable.Repeat(1f, featureCount).ToArray());
            var split = new LinkComboPolicy(legacy.CopyWeights(), Enumerable.Repeat(-8f, featureCount).ToArray());
            var sharedPolicy = LinkComboPolicy.FromJson(legacy.ToJson());
            var splitPolicy = LinkComboPolicy.FromJson(split.ToJson());
            foreach (int startingLife in new[] { 1600, 1601 })
            {
                using var duel = new DuelSession(recordPlanningReplay: true);
                if (!duel.StartNewDuel(monsters, extra, monsters, extra, startingLp: startingLife,
                    seed: new[] { 11UL, 13UL, 17UL, 19UL }))
                    throw new InvalidOperationException("Could not start the Main Phase lethal test.");
                bool summoned = false;
                for (int step = 0; step < 12 && duel.ActivePrompt is { } prompt; step++)
                {
                    if (prompt.messageType != YgoConstants.MSG_SELECT_IDLECMD)
                    {
                        duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
                        continue;
                    }
                    if (summoned) break;
                    if (sharedPolicy?.Choose(duel, prompt, (command, _) => command == 0) != 0 ||
                        splitPolicy?.Choose(duel, prompt, (command, _) => command == 0) !=
                        (prompt.playerId == 0 ? 0 : (int?)null))
                        throw new InvalidOperationException($"Invalid Link policy after serialization at seat {prompt.playerId}.");
                    if (prompt.playerId == 0)
                    {
                        duel.SubmitResponseInt(7);
                    }
                    else
                    {
                        duel.SubmitResponseInt(0);
                        summoned = true;
                    }
                }

                if (!summoned || duel.ActivePrompt is not { messageType: YgoConstants.MSG_SELECT_IDLECMD, playerId: 1, canBp: true } main ||
                    CanReplayLethal(duel, main, fromMain: true) != (startingLife == 1600) || duel.RetryCount != 0 || duel.IsFinished)
                    throw new InvalidOperationException($"Unexpected Main Phase lethal audit at {startingLife} LP.");
            }
            Debug.Log("[AI BENCH RULES] Native Main Phase audit verified exact lethal, one-LP survival, and shared and separate Link weights.");
        }

        private static void VerifyCrossoutResponse()
        {
            const uint reinforcement = 32807846, ash = 14558127, stratos = 40044918;
            var main = Enumerable.Repeat(DuelTactics.CrossoutDesignator, 3).Concat(Enumerable.Repeat(reinforcement, 3))
                .Concat(Enumerable.Repeat(ash, 3)).Concat(Enumerable.Repeat(stratos, 3))
                .Concat(Enumerable.Repeat(89631139u, 28)).ToArray();
            var opposing = Enumerable.Repeat(ash, 40).ToArray();
            for (ulong seed = 1; seed <= 60; seed++)
            {
                using var duel = new DuelSession();
                if (!duel.StartNewDuel(main, Array.Empty<uint>(), opposing, Array.Empty<uint>(),
                    seed: new[] { seed, seed + 3, seed + 7, seed + 11 }))
                    throw new InvalidOperationException("Could not start the Crossout test duel.");
                var hand = duel.Players[0].hand;
                if (!hand.Any(card => card.code == reinforcement) || !hand.Any(card => card.code == DuelTactics.CrossoutDesignator) ||
                    hand.Count(card => card.code == ash) == 3 || hand.Count(card => card.code == stratos) == 3) continue;
                var ai = new TacticalAI(null);
                for (int i = 0; i < 12 && duel.ActivePrompt is { messageType: YgoConstants.MSG_SELECT_CHAIN } open; i++)
                    duel.SubmitResponse(ai.ComputeResponse(duel, open));
                var mainPrompt = duel.ActivePrompt;
                int search = mainPrompt?.activatable.FindIndex(card => card.code == reinforcement) ?? -1;
                if (mainPrompt?.messageType != YgoConstants.MSG_SELECT_IDLECMD || mainPrompt.playerId != 0 || search < 0)
                    throw new InvalidOperationException("The Crossout test did not reach a legal search effect.");
                var crossout = mainPrompt.activatable.First(card => card.code == DuelTactics.CrossoutDesignator);
                if (DuelTactics.CanActivate(duel, mainPrompt, crossout))
                    throw new InvalidOperationException("Crossout should retain its response before an opposing activation.");
                int previousStratos = duel.Players[0].hand.Count(card => card.code == stratos);
                duel.SubmitResponseInt((search << 16) | 5);
                bool declaredAsh = false;
                for (int i = 0; i < 32 && duel.ActivePrompt is { } prompt && prompt.messageType != YgoConstants.MSG_SELECT_IDLECMD; i++)
                {
                    if (prompt.playerId == 1 && prompt.messageType == YgoConstants.MSG_SELECT_CHAIN &&
                        prompt.candidates.FindIndex(card => card.code == ash) is int ashIndex && ashIndex >= 0)
                    {
                        duel.SubmitResponseInt(ashIndex);
                        continue;
                    }
                    byte[] response = ai.ComputeResponse(duel, prompt);
                    if (prompt.messageType == YgoConstants.MSG_ANNOUNCE_CARD)
                        declaredAsh = BitConverter.ToUInt32(response, 0) == ash;
                    duel.SubmitResponse(response);
                }
                if (!declaredAsh || !duel.Players[0].banished.Any(card => card.code == ash) ||
                    duel.Players[0].hand.Count(card => card.code == stratos) != previousStratos + 1 ||
                    duel.RetryCount != 0 || duel.ActivePrompt?.messageType != YgoConstants.MSG_SELECT_IDLECMD)
                    throw new InvalidOperationException("Crossout did not negate Ash Blossom and allow the original search to resolve.");
                Debug.Log("[AI BENCH RULES] Crossout declared the live chain's Ash Blossom and resolved without retry.");
                return;
            }
            throw new InvalidOperationException("Could not find an opening hand for the Crossout response test.");
        }

        private static void VerifyNegationAfterCost()
        {
            const uint rabbit = 85138716, thrasher = 65367484, vanilla = 37265642, ash = 14558127, veiler = 97268402;
            var main = Enumerable.Repeat(rabbit, 3).Concat(Enumerable.Repeat(thrasher, 3))
                .Concat(Enumerable.Repeat(vanilla, 34)).ToArray();
            var opposing = Enumerable.Repeat(ash, 20).Concat(Enumerable.Repeat(veiler, 20)).ToArray();
            for (ulong seed = 1; seed <= 60; seed++)
            {
                using var duel = new DuelSession();
                if (!duel.StartNewDuel(main, Array.Empty<uint>(), opposing, Array.Empty<uint>(),
                    seed: new[] { seed, seed + 3, seed + 7, seed + 11 }))
                    throw new InvalidOperationException("Could not start the Rescue Rabbit test duel.");
                if (!duel.Players[0].hand.Any(card => card.code == rabbit) || !duel.Players[0].hand.Any(card => card.code == thrasher) ||
                    !duel.Players[1].hand.Any(card => card.code == ash) || !duel.Players[1].hand.Any(card => card.code == veiler)) continue;
                bool answered = false;
                int moves = 0;
                var ai = new TacticalAI(DuelAiPolicy.LoadIncluded());
                for (int step = 0; step < 40 && duel.ActivePrompt is { } prompt; step++)
                {
                    if (prompt.playerId == 0 && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                    {
                        if (answered) break;
                        int index = moves switch
                        {
                            0 => prompt.spSummonable.FindIndex(card => card.code == thrasher),
                            1 => prompt.summonable.FindIndex(card => card.code == rabbit),
                            _ => prompt.activatable.FindIndex(card => card.code == rabbit),
                        };
                        if (index < 0 || moves > 2) throw new InvalidOperationException("The Rabbit setup did not reach a legal action.");
                        duel.SubmitResponseInt((index << 16) | (moves == 0 ? 1 : moves == 1 ? 0 : 5));
                        moves++;
                    }
                    else if (prompt.playerId == 1 && prompt.messageType == YgoConstants.MSG_SELECT_CHAIN &&
                        duel.CurrentChain.LastOrDefault()?.code == rabbit)
                    {
                        if (!prompt.candidates.Any(card => card.code == veiler))
                            throw new InvalidOperationException("The Rabbit test needs Veiler as an unrelated legal response.");
                        int index = BitConverter.ToInt32(ai.ComputeResponse(duel, prompt), 0);
                        if (index < 0 || prompt.candidates[index].code != ash)
                            throw new InvalidOperationException("Ash must answer Rabbit after it leaves the field.");
                        duel.SubmitResponseInt(index);
                        answered = true;
                    }
                    else if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced)
                        duel.SubmitResponseInt(-1);
                    else
                        duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
                }
                if (!answered || duel.RetryCount != 0 || !duel.Players[0].banished.Any(card => card.code == rabbit) ||
                    duel.Players[0].mzone.Any(card => card?.code == vanilla) ||
                    !duel.Players[1].hand.Any(card => card.code == veiler))
                    throw new InvalidOperationException("Rabbit should remain banished with no summoned monsters and Veiler still in hand.");
                Debug.Log("[AI BENCH RULES] Ash stopped Rescue Rabbit's Deck summons and preserved Veiler.");
                return;
            }
            throw new InvalidOperationException("Could not find the opening hands for the Rescue Rabbit response test.");
        }

        internal static void VerifyNormalSetUnderPressure()
        {
            const uint sparkman = 20721928, gazelle = 26889158;
            using var duel = new DuelSession(recordPlanningReplay: true);
            if (!duel.StartNewDuel(Enumerable.Repeat(sparkman, 40).ToArray(), Array.Empty<uint>(),
                Enumerable.Repeat(gazelle, 40).ToArray(), Array.Empty<uint>(), startingLp: 900,
                seed: new[] { 1UL, 2UL, 3UL, 4UL }))
                throw new InvalidOperationException("Could not start the low-life defense test duel.");
            var ai = new TacticalAI(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded(), searchCombos: true);
            bool set = false;
            for (int step = 0; step < 32 && duel.ActivePrompt is { } prompt; step++)
            {
                if (prompt.playerId == 0 && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                {
                    duel.SubmitResponseInt(prompt.summonable.Count != 0 ? 0 : 7);
                    continue;
                }
                if (prompt.playerId == 1 && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                {
                    if (set) break;
                    byte[] response = ai.ComputeResponse(duel, prompt);
                    int command = BitConverter.ToInt32(response, 0);
                    if ((command & 0xffff) != 3 || prompt.mSetable[command >> 16].code != gazelle)
                        throw new InvalidOperationException("Gazelle should be set at low LP when it cannot start a follow-up.");
                    duel.SubmitResponse(response);
                    set = true;
                    continue;
                }
                duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
            }
            if (!set || duel.RetryCount != 0 || !duel.Players[1].mzone.Any(card => card?.position == YgoConstants.POS_FACEDOWN_DEFENSE))
                throw new InvalidOperationException("Gazelle's defense must resolve on the actual field without a retry.");
            Debug.Log("[AI BENCH RULES] Gazelle was set at 900 LP against Sparkman's 1600 ATK.");
        }

        private static void VerifyDeckStyles(Dictionary<int, PresetDeckInfo> decks)
        {
            var cases = new (int Deck, DuelDeckStyle Expected)[]
            {
                (51199, DuelDeckStyle.Development), (51439, DuelDeckStyle.Development),
                (20672, DuelDeckStyle.Control), (20679, DuelDeckStyle.Control), (20682, DuelDeckStyle.Control),
                (20675, DuelDeckStyle.Lock), (52150, DuelDeckStyle.Lock), (50961, DuelDeckStyle.Lock),
                (50964, DuelDeckStyle.Lock), (51029, DuelDeckStyle.Lock),
                (20673, DuelDeckStyle.Resource), (20687, DuelDeckStyle.Resource),
            };
            foreach (var (id, expected) in cases)
            {
                var deck = decks[id];
                var actual = DuelDeckStrategy.Classify(deck.main, deck.extra.Length);
                if (actual != expected) throw new InvalidOperationException($"Deck {id} should play as {expected}, got {actual}.");
                Debug.Log($"[AI STYLE] {id} {deck.title}: {actual}");
            }
        }

        private static void VerifyResourceStrategy(Dictionary<int, PresetDeckInfo> decks, DuelAiPolicy general, LinkComboPolicy links)
        {
            const uint widowAnchor = 98338152, sparkman = 20721928, kagari = 63288573;
            var opponent = Enumerable.Repeat(sparkman, 40).ToArray();
            var skyStriker = Enumerable.Repeat(DuelDeckStrategy.Engage, 10).Concat(Enumerable.Repeat(widowAnchor, 10))
                .Concat(Enumerable.Repeat(sparkman, 20)).ToArray();
            var empty = Array.Empty<uint>();
            using var striker = YgoProjectBuilder.StartSeedMatching("Sky Striker space", skyStriker, new[] { kagari }, opponent, empty, true,
                duel => duel.ActivePrompt is { messageType: YgoConstants.MSG_SELECT_IDLECMD } prompt &&
                    prompt.activatable.Any(action => action.code == DuelDeckStrategy.Engage) && prompt.summonable.Any(action => action.code == sparkman));
            var pilot = new TacticalAI(general, links, searchCombos: true);
            var opening = striker.ActivePrompt;
            int response = BitConverter.ToInt32(pilot.ComputeResponse(striker, opening), 0);
            if ((response & 0xffff) != 5 || opening.activatable[response >> 16].code != DuelDeckStrategy.Engage)
                throw new InvalidOperationException("Sky Striker must resolve its legal search before filling the main monster zone.");
            striker.SubmitResponseInt(response);
            for (int i = 0; i < 16 && striker.ActivePrompt is { } next && next.messageType != YgoConstants.MSG_SELECT_IDLECMD; i++)
                striker.SubmitResponse(pilot.ComputeResponse(striker, next));
            var pending = striker.ActivePrompt;
            if (striker.RetryCount != 0 || pending?.messageType != YgoConstants.MSG_SELECT_IDLECMD ||
                !striker.Players[0].grave.Any(card => card.code == DuelDeckStrategy.Engage) ||
                striker.Players[0].mzone.Take(5).Any(card => card != null) ||
                !striker.Players[0].hand.Any(card => card.code == widowAnchor) ||
                !pending.summonable.Any(action => action.code == sparkman) ||
                DuelDeckStrategy.CanFillMainMonsterZone(striker, pending, pending.summonable.First(action => action.code == sparkman), setting: false))
                throw new InvalidOperationException("Sky Striker did not protect the main monster zone needed by Widow Anchor.");
            var place = new DuelPrompt { playerId = 0, messageType = YgoConstants.MSG_SELECT_PLACE, min = 1, placeFlag = ~0x44u };
            if (!pilot.ComputeResponse(striker, place).SequenceEqual(new byte[] { 0, (byte)YgoConstants.LOCATION_MZONE, 6 }))
                throw new InvalidOperationException("Sky Striker did not choose a legal Extra Monster Zone.");

            var runick = decks[20673];
            using var fountain = YgoProjectBuilder.StartSeedMatching("Runick Fountain", runick.main, runick.extra, opponent, empty, true,
                duel => duel.ActivePrompt is { messageType: YgoConstants.MSG_SELECT_IDLECMD } prompt &&
                    prompt.activatable.Any(action => action.code == DuelDeckStrategy.RunickFountain) &&
                    !prompt.activatable.Any(action => action.code is 55144522 or 79571449));
            int runickResponse = BitConverter.ToInt32(
                new TacticalAI(general, links, searchCombos: true).ComputeResponse(fountain, fountain.ActivePrompt), 0);
            if ((runickResponse & 0xffff) != 5 || fountain.ActivePrompt.activatable[runickResponse >> 16].code != DuelDeckStrategy.RunickFountain)
                throw new InvalidOperationException("Runick did not prioritize the legal Fountain activation.");
            fountain.SubmitResponseInt(runickResponse);
            if (fountain.RetryCount != 0 || fountain.Players[0].szone[YgoConstants.FIELD_ZONE_SEQUENCE]?.code != DuelDeckStrategy.RunickFountain)
                throw new InvalidOperationException("Runick Fountain did not resolve as a field spell.");

            const uint hugin = 55990317, geri = 28373620;
            const ulong summonOption = ((ulong)DuelDeckStrategy.RunickTip << 20) | 1;
            var runickSearch = Enumerable.Repeat(DuelDeckStrategy.RunickFountain, 3).Concat(Enumerable.Repeat(DuelDeckStrategy.RunickTip, 20))
                .Concat(Enumerable.Repeat(sparkman, 17)).ToArray();
            using var fusion = YgoProjectBuilder.StartSeedMatching("Runick Extra Deck", runickSearch, new[] { geri, hugin }, opponent, empty, true,
                duel =>
                {
                    YgoProjectBuilder.DriveUntilIdle(duel, new TacticalAI(), next =>
                        next.messageType == YgoConstants.MSG_SELECT_CHAIN && !next.forced ? BitConverter.GetBytes(-1) : null);
                    return duel.ActivePrompt?.activatable.Any(action => action.code == DuelDeckStrategy.RunickTip) == true &&
                        !duel.Players[0].hand.Any(card => card.code == DuelDeckStrategy.RunickFountain);
                });
            var selectionPilot = new TacticalAI(general, links, searchCombos: true);
            int activation = fusion.ActivePrompt.activatable.FindIndex(action => action.code == DuelDeckStrategy.RunickTip);
            fusion.SubmitResponseInt((activation << 16) | 5);
            for (int i = 0; i < 30 && fusion.ActivePrompt is { } next && next.messageType != YgoConstants.MSG_SELECT_IDLECMD; i++)
            {
                int summon = next.messageType == YgoConstants.MSG_SELECT_OPTION ? next.options.IndexOf(summonOption) : -1;
                fusion.SubmitResponse(summon >= 0 ? BitConverter.GetBytes(summon) : selectionPilot.ComputeResponse(fusion, next));
            }
            if (fusion.RetryCount != 0 || !fusion.Players[0].mzone.Skip(5).Any(card => card?.code == hugin) ||
                !fusion.Players[0].hand.Any(card => card.code == DuelDeckStrategy.RunickFountain))
                throw new InvalidOperationException("Runick did not select Hugin in an Extra Monster Zone and search the available Fountain.");
            Debug.Log("[AI RESOURCE] Native Sky Striker and Runick searches, Fountain, and Extra Monster Zone choices passed.");
        }

        private static Match Play(int matchup, int seed, int candidate, string era, uint method,
            PresetDeckInfo first, PresetDeckInfo second, Pilot firstPilot, Pilot secondPilot, bool openingOnly, bool captureReplays = false)
        {
            var result = new Match { matchup = matchup, seed = seed, candidate = candidate, era = era, method = method, openingOnly = openingOnly,
                sides = new[] { new Side { deck = first.id }, new Side { deck = second.id } } };
            var pilots = new[] { firstPilot, secondPilot };
            var presets = new[] { first, second };
            var responses = captureReplays ? new List<ReplayResponse>() : null;
            var actions = new[] { new List<string>(), new List<string>() };
            var failures = new[] { new List<string>(), new List<string>() };
            var defensiveOptions = new[] { new HashSet<(uint Sequence, uint Code)>(), new HashSet<(uint Sequence, uint Code)>() };
            var destroyed = new int[2];
            var lostEquipTargets = new int[2];
            string missedEffectWin = null;
            TacticalAI effectWinPilot = null;
            var exposed = new[] { new Dictionary<(uint, uint), string>(), new Dictionary<(uint, uint), string>() };
            var selectedDefense = new[] { new List<(uint Code, string Alternative)>(), new List<(uint Code, string Alternative)>() };
            var endRepositions = new[] { new HashSet<(uint, uint)>(), new HashSet<(uint, uint)>() };
            var combatParticipants = new[] { new HashSet<(uint, uint)>(), new HashSet<(uint, uint)>() };
            var combatPayoffs = new[] { new HashSet<(uint, uint)>(), new HashSet<(uint, uint)>() };
            (byte Player, uint Slot, uint Code)? lastAttack = null;
            ExposureTrace attackedExposure = null;
            var fieldEffectResults = new[] { new HashSet<(uint, uint)>(), new HashSet<(uint, uint)>() };
            var exposureTraces = new[] { new List<ExposureTrace>(), new List<ExposureTrace>() };
            var exposedNextTurn = new Dictionary<(int Player, uint Slot, uint Code), ExposureTrace>();
            var arrivals = new List<CardMoveEvent>();
            var destructionTraces = new Dictionary<ChainLinkInfo, DestructionTrace>();
            var unusedDevelopment = new[] { new List<UnusedDevelopmentTrace>(), new List<UnusedDevelopmentTrace>() };
            var linkDowngrades = new[] { new List<LinkMaterialDowngradeTrace>(), new List<LinkMaterialDowngradeTrace>() };
            var pendingLinkMaterials = new[] { new List<(uint Card, int Rank)>(), new List<(uint Card, int Rank)>() };
            var pendingLinkTurns = new int[2];
            var provenMissedBattleTurns = new[] { new HashSet<int>(), new HashSet<int>() };
            bool auditGx = era.StartsWith("GX/", StringComparison.Ordinal);
            int responseIndex = 0, activeResponseStep = -1;
            using var duel = new DuelSession(recordPlanningReplay: true);
            StartFixture(duel, matchup, seed, first, second);
            for (int player = 0; player < 2; player++)
            {
                result.sides[player].initial = duel.Players[player].hand.Select(card => card.code).ToArray();
                result.sides[player].style = DuelDeckStrategy.For(duel, (byte)player).ToString();
                result.sides[player].method = presets[player].id == 20678 ? YgoConstants.SUMMON_TYPE_SYNCHRO : method;
            }
            duel.OnCardMoved += move =>
            {
                TrackDestruction(duel.ResolvingChainLink, move);
                TrackLinkMaterialDowngrade(move);
                if (move.toCon < 2 && move.toLoc == YgoConstants.LOCATION_MZONE &&
                    move.fromLoc != YgoConstants.LOCATION_MZONE && (move.toPos & YgoConstants.POS_FACEUP) != 0 &&
                    move.code == presets[move.toCon].sigCard)
                    result.sides[move.toCon].signatureMonsterReached = true;
                if (lastAttack is { } attack && move.fromCon != attack.Player && move.fromLoc == YgoConstants.LOCATION_MZONE &&
                    move.toLoc != YgoConstants.LOCATION_MZONE && (move.reason & YgoConstants.REASON_BATTLE) != 0)
                    combatPayoffs[attack.Player].Add((attack.Slot, attack.Code));
                if (duel.TurnNumber <= 2 && move.toCon < 2 && move.toLoc == YgoConstants.LOCATION_MZONE &&
                    (move.toPos & YgoConstants.POS_FACEUP) != 0 && (move.fromLoc != move.toLoc || move.fromCon != move.toCon))
                {
                    uint expected = result.sides[move.toCon].method;
                    if (move.code == presets[move.toCon].sigCard || expected != 0 && (move.summonType & YgoConstants.SUMMON_METHOD_MASK) == expected)
                        arrivals.Add(move);
                }
                if (move.fromCon < 2 && move.fromLoc == YgoConstants.LOCATION_MZONE &&
                    (move.toCon != move.fromCon || move.toLoc != move.fromLoc || move.toSeq != move.fromSeq))
                {
                    if (exposedNextTurn.Remove((move.fromCon, move.fromSeq, move.code), out var moved) &&
                        ReferenceEquals(attackedExposure, moved) && (move.reason & YgoConstants.REASON_BATTLE) == 0)
                        attackedExposure = null;
                    defensiveOptions[move.fromCon].Remove((move.fromSeq, move.code));
                    exposed[move.fromCon].Remove((move.fromSeq, move.code));
                    endRepositions[move.fromCon].Remove((move.fromSeq, move.code));
                    combatParticipants[move.fromCon].Remove((move.fromSeq, move.code));
                    combatPayoffs[move.fromCon].Remove((move.fromSeq, move.code));
                    fieldEffectResults[move.fromCon].Remove((move.fromSeq, move.code));
                }
                if (move.toCon < 2 && move.toLoc == YgoConstants.LOCATION_MZONE && move.fromLoc != move.toLoc &&
                    (move.toPos & YgoConstants.POS_ATTACK) != 0)
                {
                    var pending = selectedDefense[move.toCon];
                    int index = pending.FindIndex(choice => choice.Code == move.code);
                    if (index >= 0)
                    {
                        exposed[move.toCon][(move.toSeq, move.code)] = pending[index].Alternative;
                        pending.RemoveAt(index);
                    }
                }
                if ((move.reason & YgoConstants.REASON_DESTROY) != 0 &&
                    move.fromLoc is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE &&
                    duel.SelectionChainLink is { } effect && !effect.negated)
                {
                    byte owner = effect.triggerPlayer;
                    bool own = move.fromCon == owner;
                    destroyed[owner] += own ? 1 : -1;
                    const uint equipRuleLoss = YgoConstants.REASON_LOST_TARGET | YgoConstants.REASON_RULE;
                    if (own && move.fromLoc == YgoConstants.LOCATION_SZONE &&
                        (move.reason & (equipRuleLoss | YgoConstants.REASON_EFFECT)) == equipRuleLoss)
                    {
                        lostEquipTargets[owner]++;
                        result.sides[owner].equipRuleLosses++;
                    }
                }
            };
            duel.OnFieldArrivalBoundary += () =>
            {
                foreach (var move in arrivals) RecordArrival(move);
                arrivals.Clear();
            };
            duel.OnChainResolved += link =>
            {
                if (!destructionTraces.TryGetValue(link, out var trace)) return;
                trace.resolved = true;
                if (!link.negated && link.triggerLoc == YgoConstants.LOCATION_MZONE &&
                    (trace.fieldArrivals.Count != 0 || trace.handArrivals.Count != 0 || trace.opposingRemovals != 0 || trace.draws != 0 || trace.lpSwing > 0))
                    fieldEffectResults[link.triggerPlayer].Add((link.triggerSeq, link.code));
            };
            duel.OnAttackDeclared += (player, location, slot, targetPlayer, targetLocation, targetSlot) =>
            {
                lastAttack = null;
                attackedExposure = null;
                if (location == YgoConstants.LOCATION_MZONE && slot < duel.Players[player].mzone.Length &&
                    duel.Players[player].mzone[slot] is { } monster)
                {
                    combatParticipants[player].Add((slot, monster.code));
                    lastAttack = (player, slot, monster.code);
                }
                if (targetPlayer < 2 && targetPlayer != player && targetLocation == YgoConstants.LOCATION_MZONE &&
                    targetSlot < duel.Players[targetPlayer].mzone.Length &&
                    duel.Players[targetPlayer].mzone[targetSlot] is { IsAttack: true } target &&
                    exposedNextTurn.TryGetValue((targetPlayer, targetSlot, target.code), out var exposure) &&
                    duel.TurnNumber == exposure.turn + 1)
                {
                    attackedExposure = exposure;
                    exposure.targetedNextTurn = true;
                }
            };
            duel.OnCardsDrawn += (player, cards) =>
            {
                if (duel.ResolvingChainLink is { } link && link.triggerPlayer == player)
                    Trace(link).draws += cards.Count;
            };
            duel.OnLifePointsChanged += (player, delta, _) =>
            {
                if (duel.ResolvingChainLink is { } link)
                    Trace(link).lpSwing += player == link.triggerPlayer ? delta : -delta;
            };
            duel.OnLifePointChange += (player, delta, life, kind) =>
            {
                if (kind == LifePointChangeKind.BattleDamage && delta < 0 && life <= 0 &&
                    lastAttack is { } strike && player != strike.Player)
                {
                    result.sides[strike.Player].battleFinishes++;
                    provenMissedBattleTurns[strike.Player].Remove(duel.TurnNumber);
                }
                if (kind is LifePointChangeKind.EffectDamage or LifePointChangeKind.Damage && delta < 0 && life <= 0 &&
                    duel.ResolvingChainLink is { negated: false } source && source.triggerPlayer == player)
                {
                    var side = result.sides[player];
                    side.selfDefeatEffect = source.code;
                    side.selfDefeatTurn = duel.TurnNumber;
                    side.selfDefeatDamage = -delta;
                }
                if (kind == LifePointChangeKind.BattleDamage && delta < 0 && lastAttack is { } attack && player != attack.Player)
                {
                    combatPayoffs[attack.Player].Add((attack.Slot, attack.Code));
                    if (attackedExposure != null)
                    {
                        attackedExposure.damageTaken -= delta;
                        attackedExposure.lostByBattle |= life <= 0;
                    }
                }
            };

            int lastTurn = openingOnly ? 2 : TurnLimit;
            for (int step = 0; step < ResponseLimit && duel.TurnNumber <= lastTurn && !duel.IsFinished && duel.RetryCount == 0; step++)
            {
                var prompt = duel.ActivePrompt;
                if (prompt == null) { duel.StepUntilPrompt(); continue; }
                if (prompt.messageType is YgoConstants.MSG_SELECT_IDLECMD or YgoConstants.MSG_SELECT_BATTLECMD)
                    foreach (var pending in pendingLinkMaterials) pending.Clear();
                byte player = prompt.playerId;
                int replayStep = responseIndex++;
                activeResponseStep = replayStep;
                int turn = duel.TurnNumber;
                int turnPlayer = duel.TurnPlayer;
                var side = result.sides[player];
                var own = duel.Players[player];
                var rival = duel.Players[1 - player];
                int before = duel.RetryCount;
                long started = Stopwatch.GetTimestamp();
                byte[] response = pilots[player].Choose(duel, prompt);
                double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                side.decisions++;
                side.decisionMs += ms;
                double lastPlanMs = pilots[player].LastPlanMilliseconds?.Invoke() ?? 0;
                if (double.IsNaN(lastPlanMs) || double.IsInfinity(lastPlanMs)) lastPlanMs = 0;
                double ordinaryMs = ms - Math.Clamp(lastPlanMs, 0, ms);
                side.ordinaryDecisionMs += ordinaryMs;
                if (ordinaryMs > side.slowestOrdinaryMs)
                {
                    side.slowestOrdinaryMs = ordinaryMs;
                    side.slowestOrdinaryTurn = turn;
                    side.slowestOrdinaryMessage = prompt.messageType;
                    side.slowestOrdinaryPhase = duel.CurrentPhase;
                    side.slowestOrdinaryStep = replayStep;
                }
                if (ms > side.slowestMs)
                {
                    side.slowestMs = ms;
                    side.slowestTurn = turn;
                    side.slowestMessage = prompt.messageType;
                    side.slowestPhase = duel.CurrentPhase;
                    side.slowestStep = replayStep;
                }
                int command = response.Length >= 4 ? BitConverter.ToInt32(response, 0) : -1;
                responses?.Add(new ReplayResponse {
                    turn = turn, phase = duel.CurrentPhase, player = player, message = prompt.messageType,
                    hint = prompt.SelectionHint, answer = BitConverter.ToString(response)
                });
                var card = ChosenCard(prompt, command);
                if (turn <= 2 && player == turnPlayer)
                {
                    if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                    {
                        side.reachedMain = true;
                        side.methodAvailable |= prompt.spSummonable.Any(item => item.code == presets[player].sigCard ||
                            IsMethodCandidate(item, side.method));
                    }
                    if (card != null || prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                        actions[player].Add($"{turn}/{prompt.messageType}:{command & 0xffff}:{card?.code ?? 0}");
                }

                if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                {
                    if (player == turnPlayer && command == 7 && prompt.canEp)
                    {
                        var options = NativeDevelopmentOptions(prompt);
                        if (options.Length != 0 && !unusedDevelopment[player].Any(trace => trace.turn == turn))
                            unusedDevelopment[player].Add(new UnusedDevelopmentTrace
                                { turn = turn, phase = duel.CurrentPhase, step = replayStep, options = options });
                    }
                    if (player == turnPlayer && missedEffectWin == null &&
                        DuelTactics.FindGiftCardWin(duel, prompt, (_, _) => true) is int effectWin && effectWin != command)
                    {
                        effectWinPilot ??= new TacticalAI(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded());
                        if (DuelCombat.VerifyEffectWin(duel, prompt, effectWin, effectWinPilot))
                            missedEffectWin = $"life={rival.lp}, winning first action={effectWin}, chosen={command}";
                    }
                    foreach (var option in prompt.repositionable)
                    {
                        defensiveOptions[player].Add((option.sequence, option.code));
                        if (command == 7 && prompt.canEp) endRepositions[player].Add((option.sequence, option.code));
                    }
                    if ((command & 0xffff) == 0 && card != null && StrongestAttacker(rival) > Attack(card.code) &&
                        prompt.mSetable.Any(set => set.code == card.code && set.sequence == card.sequence))
                        selectedDefense[player].Add((card.code, "normal_set"));
                }
                if (prompt.messageType == YgoConstants.MSG_SELECT_POSITION &&
                    (prompt.allowedPositions & YgoConstants.POS_FACEUP_DEFENSE) != 0 && command == YgoConstants.POS_FACEUP_ATTACK &&
                    StrongestAttacker(rival) > Attack(prompt.promptCardCode))
                    selectedDefense[player].Add((prompt.promptCardCode, "summon_in_defense"));
                bool skippedBattle = prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && prompt.canBp && command == 7;
                bool leftBattle = prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD && (command & 0xffff) is 2 or 3;
                if (auditGx && player == candidate && !provenMissedBattleTurns[player].Contains(turn) &&
                    (skippedBattle || leftBattle || prompt.IsAttackTargetSelection ||
                        prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD && prompt.attackable.Count > 1) &&
                    AiBattleProbe.TryProveMissedLethal(duel, prompt, response, out string proof))
                {
                    provenMissedBattleTurns[player].Add(turn);
                    Failure(player, "native_battle_win_missed", $"step={replayStep}, response={BitConverter.ToString(response)}, winning line={proof}");
                }
                if ((skippedBattle || leftBattle) && CanReplayLethal(duel, prompt, skippedBattle))
                {
                    side.lethalMisses++;
                    Failure(player, "missed_lethal", $"phase={duel.CurrentPhase}, LP={rival.lp}, " +
                        $"attackers={string.Join(",", own.mzone.Where(monster => monster?.IsAttack == true).Select(monster => monster.code))}");
                }
                if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && card != null && ReactiveHandtraps.Contains(card.code) &&
                    duel.CurrentChain.LastOrDefault() is { } last)
                {
                    side.threatsSpent++;
                    if (!prompt.forced && (last.negated || last.triggerPlayer == player))
                    {
                        side.wastedHandtraps++;
                        Failure(player, "wasted_handtrap", $"{card.code} chained to {last.code}, negated={last.negated}, sameSeat={last.triggerPlayer == player}");
                    }
                }
                if (card != null && GxTrackedResponses.Contains(card.code) &&
                    (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN ||
                        prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && (command & 0xffff) == 5))
                    side.trackedResponses++;

                double beforeBoard = Board(own);
                Array.Clear(destroyed, 0, destroyed.Length);
                Array.Clear(lostEquipTargets, 0, lostEquipTargets.Length);
                duel.SubmitResponse(response);
                side.retries += duel.RetryCount - before;
                if (duel.RetryCount != before) Failure(player, "engine_retry", $"prompt={prompt.messageType}, hint={prompt.SelectionHint}, answer={BitConverter.ToString(response)}");
                if (destroyed[player] > 0 && Board(duel.Players[player]) + 1 < beforeBoard && !duel.IsFinished)
                {
                    side.selfDestruction++;
                    if (destroyed[player] > lostEquipTargets[player]) side.selfDestructionWithoutEquipRuleLoss++;
                    Failure(player, "self_destruction", $"own net field losses={destroyed[player]}, choice={card?.code ?? 0}");
                }
                if (duel.TurnNumber != turn || duel.IsFinished)
                {
                    var ending = result.sides[turnPlayer];
                    if (missedEffectWin != null && duel.Winner != turnPlayer)
                    {
                        ending.lethalMisses++;
                        Failure(turnPlayer, "missed_effect_lethal", missedEffectWin, turn);
                    }
                    missedEffectWin = null;
                    var field = duel.Players[turnPlayer];
                    double board = Board(field);
                    ending.boardTotal += board;
                    ending.endTurns++;
                    if (duel.TurnNumber != turn)
                    {
                        ending.completedTurns++;
                        if (!field.mzone.Any(monster => monster != null))
                        {
                            ending.emptyMonsterEndTurns++;
                            if (!field.szone.Any(spell => spell != null)) ending.emptyFieldEndTurns++;
                        }
                    }
                    if (turn <= 2)
                    {
                        ending.earlyBoard = board;
                        ending.aceReached |= field.szone.Any(card => card?.code == presets[turnPlayer].sigCard &&
                            card.IsFaceUp && !card.effectsDisabled);
                    }
                    exposedNextTurn.Clear();
                    if (!duel.IsFinished)
                    {
                        int threat = StrongestAttacker(duel.Players[1 - turnPlayer]);
                        for (uint zone = 0; zone < field.mzone.Length; zone++)
                        {
                            var monster = field.mzone[zone];
                            if (monster?.IsFaceUp != true || !monster.IsAttack || monster.attack >= threat ||
                                monster.attack >= 1800 || (monster.type & YgoConstants.TYPE_LINK) != 0 || HasInteraction(monster)) continue;
                            var slot = (zone, monster.code);
                            if (!exposed[turnPlayer].TryGetValue(slot, out string alternative) && !defensiveOptions[turnPlayer].Contains(slot)) continue;
                            ending.exposedAttackers++;
                            var trace = new ExposureTrace
                            {
                                turn = turn, card = monster.code, attack = monster.attack, opponentAttack = threat, life = field.lp,
                                alternative = alternative ?? "reposition", endPhaseReposition = endRepositions[turnPlayer].Contains(slot),
                                attacked = combatParticipants[turnPlayer].Contains(slot), battlePayoff = combatPayoffs[turnPlayer].Contains(slot),
                                fieldEffectResolved = fieldEffectResults[turnPlayer].Contains(slot),
                            };
                            exposureTraces[turnPlayer].Add(trace);
                            exposedNextTurn.Add((turnPlayer, zone, monster.code), trace);
                            Failure(turnPlayer, "exposed_attacker", $"{monster.code} ATK {monster.attack} against {threat}");
                        }
                    }
                    defensiveOptions[turnPlayer].Clear();
                    exposed[turnPlayer].Clear();
                    selectedDefense[turnPlayer].Clear();
                    endRepositions[turnPlayer].Clear();
                    foreach (var entries in combatParticipants) entries.Clear();
                    foreach (var entries in combatPayoffs) entries.Clear();
                    lastAttack = null;
                    attackedExposure = null;
                    foreach (var entries in fieldEffectResults) entries.Clear();
                }
                if (duel.CurrentChain.Count == 0 && duel.ActivePrompt?.messageType is YgoConstants.MSG_SELECT_IDLECMD or YgoConstants.MSG_SELECT_BATTLECMD)
                {
                    foreach (var move in arrivals)
                        if (move.toSeq < duel.Players[move.toCon].mzone.Length && duel.Players[move.toCon].mzone[move.toSeq]?.code == move.code)
                            RecordArrival(move);
                    arrivals.Clear();
                }
            }
            result.winner = duel.Winner;
            result.turns = duel.TurnNumber;
            result.finalLife = duel.Players.Select(field => field.lp).ToArray();
            result.capped = !duel.IsFinished && duel.RetryCount == 0 && (!openingOnly || duel.TurnNumber <= 2);
            for (int player = 0; player < 2; player++)
            {
                var side = result.sides[player];
                side.provenMissedBattleWins = provenMissedBattleTurns[player].Count;
                side.branches = pilots[player].Branches();
                side.chosenAlternatives = pilots[player].ChosenAlternatives?.Invoke() ?? 0;
                side.planTimingAvailable = pilots[player].MaxPlanMilliseconds != null;
                side.ordinaryTimingSeparated = pilots[player].LastPlanMilliseconds != null;
                side.maxPlanMs = pilots[player].MaxPlanMilliseconds?.Invoke() ?? 0;
                side.openings = side.reachedMain ? 1 : 0;
                bool lost = result.winner == 1 - player;
                side.unusedDevelopmentEndTurns = unusedDevelopment[player].Count;
                side.unusedDevelopmentOptions = unusedDevelopment[player].Sum(trace => trace.options.Length);
                side.unusedDevelopmentEndTurnsOnLoss = lost ? side.unusedDevelopmentEndTurns : 0;
                side.unusedDevelopmentOptionsOnLoss = lost ? side.unusedDevelopmentOptions : 0;
                foreach (var trace in unusedDevelopment[player]) trace.eventualLoss = lost;
                side.unusedDevelopmentTraces = unusedDevelopment[player].ToArray();
                side.linkMaterialDowngradeTraces = linkDowngrades[player].ToArray();
                if (side.reachedMain && !side.methodReached && !side.aceReached)
                    Failure(player, side.methodAvailable ? "missed_development" : "no_progression", string.Join(" > ", actions[player]));
                if (result.winner == 1 - player && side.selfDefeatEffect != 0)
                    Failure(player, "own_effect_defeat", $"{side.selfDefeatEffect} dealt {side.selfDefeatDamage} on turn {side.selfDefeatTurn}");
                if (result.winner == 1 - player)
                    Failure(player, "lost", $"finished on turn {result.turns}, LP {duel.Players[player].lp}:{duel.Players[1 - player].lp}");
                side.earlyActions = actions[player].ToArray();
                side.failures = failures[player].ToArray();
                side.destructionTraces = destructionTraces.Where(item => item.Key.triggerPlayer == player && item.Value.destroyed.Count != 0)
                    .Select(item => item.Value).ToArray();
                side.exposureTraces = exposureTraces[player].ToArray();
            }
            result.replay = responses?.ToArray();
            return result;

            void TrackLinkMaterialDowngrade(CardMoveEvent move)
            {
                const uint linkMaterial = YgoConstants.REASON_MATERIAL | YgoConstants.REASON_LINK;
                if (move.fromCon < 2 && move.fromLoc == YgoConstants.LOCATION_MZONE &&
                    (move.fromPos & YgoConstants.POS_FACEUP) != 0 && (move.reason & linkMaterial) == linkMaterial)
                {
                    int rank = LinkComboPolicy.Rank(move.code);
                    if (rank > 0)
                    {
                        byte owner = move.fromCon;
                        if (pendingLinkTurns[owner] != duel.TurnNumber) pendingLinkMaterials[owner].Clear();
                        pendingLinkTurns[owner] = duel.TurnNumber;
                        pendingLinkMaterials[owner].Add((move.code, rank));
                    }
                }
                if (move.toCon >= 2 || move.fromLoc != YgoConstants.LOCATION_EXTRA ||
                    move.toLoc != YgoConstants.LOCATION_MZONE || (move.reason & YgoConstants.REASON_SPSUMMON) == 0 ||
                    (move.summonType & YgoConstants.SUMMON_METHOD_MASK) != YgoConstants.SUMMON_TYPE_LINK)
                    return;

                byte player = move.toCon;
                var materials = pendingLinkMaterials[player];
                int targetRank = LinkComboPolicy.Rank(move.code);
                if (pendingLinkTurns[player] == duel.TurnNumber && targetRank > 0)
                {
                    int higher = materials.Count(material => material.Rank > targetRank);
                    if (higher > 0)
                    {
                        result.sides[player].linkMaterialDowngrades++;
                        result.sides[player].higherLinkMaterialsSpent += higher;
                        linkDowngrades[player].Add(new LinkMaterialDowngradeTrace
                        {
                            turn = duel.TurnNumber, phase = duel.CurrentPhase, step = activeResponseStep,
                            target = move.code, targetRank = targetRank,
                            materials = materials.Select(material => material.Card).ToArray(),
                            materialRanks = materials.Select(material => material.Rank).ToArray(),
                        });
                    }
                }
                materials.Clear();
            }

            DestructionTrace Trace(ChainLinkInfo effect)
            {
                if (!destructionTraces.TryGetValue(effect, out var trace))
                    destructionTraces.Add(effect, trace = new DestructionTrace { effect = effect.code, turn = duel.TurnNumber });
                return trace;
            }

            void TrackDestruction(ChainLinkInfo effect, CardMoveEvent move)
            {
                if (effect == null || effect.negated) return;
                bool fromField = move.fromLoc is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE;
                bool toField = move.toLoc is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE;
                byte owner = effect.triggerPlayer;
                if (fromField && (!toField || move.toCon != move.fromCon))
                {
                    if (move.fromCon != owner) Trace(effect).opposingRemovals++;
                    else if ((move.reason & YgoConstants.REASON_DESTROY) != 0) Trace(effect).destroyed.Add(move.code);
                }
                if (move.toCon != owner || (move.fromCon == owner && fromField)) return;
                if (toField) Trace(effect).fieldArrivals.Add(move.code);
                else if (move.toLoc == YgoConstants.LOCATION_HAND && move.fromLoc != YgoConstants.LOCATION_HAND)
                    Trace(effect).handArrivals.Add(move.code);
            }

            void RecordArrival(CardMoveEvent move)
            {
                var side = result.sides[move.toCon];
                side.methodReached |= side.method != 0 && (move.summonType & YgoConstants.SUMMON_METHOD_MASK) == side.method;
                side.aceReached |= move.code == presets[move.toCon].sigCard;
            }

            void Failure(int player, string kind, string detail, int? turn = null)
            {
                if (failures[player].Count >= 40) return;
                failures[player].Add($"{kind}\tT{turn ?? duel.TurnNumber}\t{detail}\t{string.Join(" | ", duel.Logs.TakeLast(6))}");
            }
        }

        private static SelectableCardItem ChosenCard(DuelPrompt prompt, int command)
        {
            int index = command >> 16;
            var options = prompt.messageType switch
            {
                YgoConstants.MSG_SELECT_CHAIN => prompt.candidates,
                YgoConstants.MSG_SELECT_IDLECMD => (command & 0xffff) switch
                {
                    0 => prompt.summonable, 1 => prompt.spSummonable, 2 => prompt.repositionable,
                    3 => prompt.mSetable, 4 => prompt.sSetable, 5 => prompt.activatable, _ => null,
                },
                _ => null,
            };
            if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN) index = command;
            return options != null && index >= 0 && index < options.Count ? options[index] : null;
        }

        private static DevelopmentOption[] NativeDevelopmentOptions(DuelPrompt prompt)
        {
            var options = new List<DevelopmentOption>();
            void Add(IEnumerable<SelectableCardItem> choices, int command, bool requireDevelopment)
            {
                foreach (var item in choices)
                {
                    if (item.controller != prompt.playerId || requireDevelopment && item.description != 1160 &&
                        !ComboSearch.CanDevelopFromCard(CardDatabase.Get(item.code))) continue;
                    options.Add(new DevelopmentOption
                    {
                        command = command, card = item.code, location = item.location,
                        sequence = item.sequence, description = item.description,
                    });
                }
            }
            Add(prompt.summonable, 0, requireDevelopment: true);
            Add(prompt.spSummonable, 1, requireDevelopment: false);
            Add(prompt.activatable, 5, requireDevelopment: true);
            return options.GroupBy(option => (option.command, option.card, option.location, option.sequence, option.description))
                .Select(group => group.First()).ToArray();
        }

        private static bool IsMethodCandidate(SelectableCardItem item, uint method) => method switch
        {
            YgoConstants.SUMMON_TYPE_PENDULUM => item.location is YgoConstants.LOCATION_SZONE or YgoConstants.LOCATION_PZONE &&
                CardDatabase.Get(item.code)?.IsPendulum == true,
            YgoConstants.SUMMON_TYPE_FUSION => (CardDatabase.Get(item.code)?.type & YgoConstants.TYPE_FUSION) != 0 && item.location == YgoConstants.LOCATION_EXTRA,
            YgoConstants.SUMMON_TYPE_SYNCHRO => (CardDatabase.Get(item.code)?.type & YgoConstants.TYPE_SYNCHRO) != 0 && item.location == YgoConstants.LOCATION_EXTRA,
            YgoConstants.SUMMON_TYPE_XYZ => (CardDatabase.Get(item.code)?.type & YgoConstants.TYPE_XYZ) != 0 && item.location == YgoConstants.LOCATION_EXTRA,
            YgoConstants.SUMMON_TYPE_LINK => (CardDatabase.Get(item.code)?.type & YgoConstants.TYPE_LINK) != 0 && item.location == YgoConstants.LOCATION_EXTRA,
            _ => false,
        };

        private static int Attack(uint card) => CardDatabase.Get(card)?.atk ?? 0;
        private static int StrongestAttacker(PlayerFieldSnapshot side) => side.mzone
            .Where(card => card?.IsFaceUp == true && card.IsAttack).Select(card => card.attack).DefaultIfEmpty(0).Max();

        private static bool CanReplayLethal(DuelSession live, DuelPrompt prompt, bool fromMain)
        {
            byte player = prompt.playerId;
            if (fromMain ? !DuelCombat.CanCloseBattleFromMain(live, player) : DuelCombat.FindLethal(live, prompt) == null)
                return false;
            var clock = Stopwatch.StartNew();
            using var trial = live.ReplayForPlanning(() => clock.ElapsedMilliseconds >= 200);
            if (trial == null) return false;
            trial.BeginSpeculation(player);
            if (fromMain)
            {
                if (trial.ActivePrompt is not { messageType: YgoConstants.MSG_SELECT_IDLECMD, canBp: true } idle ||
                    idle.playerId != player) return false;
                trial.SubmitResponseInt(6);
            }
            var ai = new TacticalAI(null);
            for (int step = 0; step < 36 && clock.ElapsedMilliseconds < 200 && !trial.SpeculationUncertain &&
                !trial.IsFinished && trial.RetryCount == 0; step++)
            {
                var current = trial.ActivePrompt;
                if (current == null || current.messageType == YgoConstants.MSG_SELECT_IDLECMD) break;
                if (current.playerId == player) trial.SubmitResponse(ai.ComputeResponse(trial, current));
                else if (current.messageType == YgoConstants.MSG_SELECT_CHAIN && !current.forced) trial.SubmitResponseInt(-1);
                else break;
            }
            return !trial.SpeculationUncertain && trial.RetryCount == 0 && trial.IsFinished && trial.Winner == player;
        }

        private static bool HasInteraction(LiveCardState monster) => !monster.effectsDisabled &&
            CardDatabase.Get(monster.code) is CardInfo card && LinkComboPolicy.IsInteractive(card);

        private static double Board(PlayerFieldSnapshot player)
        {
            double value = .24 * player.hand.Count + .08 * player.grave.Count +
                .8 * player.hand.Count(card => Handtraps.Contains(card.code));
            foreach (var card in player.mzone.Concat(player.szone))
            {
                if (card == null || CardDatabase.Get(card.code) is not CardInfo info) continue;
                if (!card.IsFaceUp)
                {
                    if (info.IsTrap || (info.type & YgoConstants.TYPE_QUICKPLAY) != 0) value += .7;
                    else if (card.location == YgoConstants.LOCATION_MZONE) value += .25;
                    continue;
                }
                if (info.IsMonster) value += Math.Max(card.attack, card.defense) / 8000.0;
                if (card.effectsDisabled) continue;
                uint flags = info.DeckEffectCategories;
                if (ComboSearch.HasPassiveProtection(info)) value += .55;
                if ((flags & Negation) != 0) value += 1.6;
                else if (LinkComboPolicy.IsInteractive(info)) value += .75;
            }
            return value;
        }
    }
}
