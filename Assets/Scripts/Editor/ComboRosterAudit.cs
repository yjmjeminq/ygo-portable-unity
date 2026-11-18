using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YGOPortable.AI;
using YGOPortable.Core;
using Debug = UnityEngine.Debug;

namespace YGOPortable.Editor
{
    internal static class ComboRosterAudit
    {
        private const int OpeningsPerDeck = 6;
        private const int MatchesPerDeck = 4;
        private const uint ExtraTypes = YgoConstants.TYPE_FUSION | YgoConstants.TYPE_SYNCHRO | YgoConstants.TYPE_XYZ | YgoConstants.TYPE_LINK;
        private static readonly int[] RivalIds = { 207, 20672, 20671, 20674, 20686, 20, 20670, 481 };
        private static readonly int[] MatchIds =
        {
            289, 20671, 207, 288, 485, 20, 128, 369, 37050, 36017, 20674, 20686, 33013, 94, 155,
            158, 620, 37037, 37192, 37193, 37053, 495, 520, 35003, 34043, 136, 20675, 20684, 20677,
        };

        private sealed class Result
        {
            internal uint summonedTypes;
            internal int extraBodies, specialSummons, pendulumSummons, retries, branches, changes;
            internal bool stopped, won, lost;
            internal double field;
            internal byte failedMessage;
            internal ulong failedHint;
            internal string diagnostic;
            internal readonly List<uint> finishers = new List<uint>();
        }

        internal static void VerifyOtherComboRoutes()
        {
            var decks = DeckRepository.PresetDecks.ToDictionary(deck => deck.id);
            var rivals = RivalIds.Select(id => decks[id]).ToArray();
            var general = DuelAiPolicy.LoadIncluded();
            var links = LinkComboPolicy.LoadIncluded();
            var routes = new (int deck, int test, uint type, bool pendulum)[]
            {
                (245, 5, YgoConstants.TYPE_FUSION, false),
                (47, 2, YgoConstants.TYPE_SYNCHRO, false),
                (426, 0, YgoConstants.TYPE_XYZ, false),
                (37074, 5, YgoConstants.TYPE_FUSION, true),
                (37037, 1, YgoConstants.TYPE_RITUAL, false),
            };
            foreach (var route in routes)
            {
                var deck = decks[route.deck];
                var result = Play(deck, PickRival(rivals, route.deck, route.test, false), route.test, (route.test & 1) == 0,
                    false, general, links);
                if (!result.stopped || result.retries != 0 || result.branches == 0 ||
                    (result.summonedTypes & route.type) == 0 || (route.pendulum && result.pendulumSummons == 0))
                    throw new InvalidOperationException($"The adaptive combo did not resolve for deck {route.deck} ({deck.title}).");
                Debug.Log($"[VERIFY COMBO METHODS] {deck.title} used the planner and summoned {string.Join(", ", result.finishers.Select(CardDatabase.GetName))}.");
            }
        }

        public static void RunHeadless()
        {
            int exit = 0;
            try
            {
                string streaming = Application.streamingAssetsPath;
                int scriptErrors = 0;
                var errors = new HashSet<string>();
                if (YgoNativeBridge.Initialize(streaming, (type, message) =>
                {
                    if (type != 0) return;
                    scriptErrors++;
                    errors.Add(message);
                }) != 0) throw new InvalidOperationException("Could not start the duel engine for the roster audit.");
                CardDatabase.Load(streaming);
                DeckRepository.Load(streaming, Path.Combine(Path.GetTempPath(), "ygo_combo_roster_audit"), seedDefaultDecks: false);
                var decks = DeckRepository.Characters.SelectMany(character => OpponentDeckLevels.GetDecks(character.id))
                    .GroupBy(deck => deck.id).Select(group => group.First()).OrderBy(deck => deck.id).ToArray();
                var byId = decks.ToDictionary(deck => deck.id);
                var rivals = RivalIds.Select(id => byId[id]).ToArray();
                var general = DuelAiPolicy.LoadIncluded();
                var links = LinkComboPolicy.LoadIncluded();
                if (general == null || links == null) throw new InvalidOperationException("Missing the included duel models.");
                string run = Environment.GetEnvironmentVariable("YGO_COMBO_AUDIT_RUN");
                if (run is not ("baseline" or "expanded" or "final" or "diagnostic")) run = "expanded";
                int filter = int.TryParse(Environment.GetEnvironmentVariable("YGO_COMBO_AUDIT_DECK"), out int idFilter) ? idFilter : 0;
                string logDirectory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
                Directory.CreateDirectory(logDirectory);
                string report = Path.Combine(logDirectory, $"combo_roster_{run}.tsv");
                using var writer = new StreamWriter(report);
                writer.WriteLine("deck\ttitle\tcase\tfirst\trival\tmode\tstopped\twin\tloss\ttypes\textra\tspecial\tpendulum\tretries\tretryMessage\tretryHint\tnativeErrors\tbranches\tchanges\tfield");
                var elapsed = Stopwatch.StartNew();
                int checkedDecks = 0, openings = 0, matches = 0, capped = 0, retries = 0;
                foreach (var deck in decks.Where(deck => filter == 0 || deck.id == filter))
                {
                    if (deck.main == null || deck.main.Length == 0)
                        throw new InvalidOperationException($"Unplayable roster entry {deck.id}.");
                    for (int test = 0; test < OpeningsPerDeck; test++) Record(deck, test, false);
                    checkedDecks++;
                    if (checkedDecks % 100 == 0 || checkedDecks == decks.Length)
                    {
                        writer.Flush();
                        Debug.Log($"[COMBO ROSTER] {checkedDecks}/{decks.Length} decks, {openings} openings, {elapsed.Elapsed.TotalSeconds:F0}s.");
                    }
                }
                foreach (int id in MatchIds.Where(id => filter == 0 || id == filter))
                    for (int test = 0; test < MatchesPerDeck; test++) Record(byId[id], test, true);
                Debug.Log($"[COMBO ROSTER] {checkedDecks} decks, {openings} openings, {matches} complete-match attempts, " +
                    $"{capped} capped, {retries} retries, {scriptErrors} native errors in {elapsed.Elapsed.TotalSeconds:F0}s. Path {report}");
                foreach (string error in errors.Take(8)) Debug.LogWarning($"[COMBO ROSTER LUA] {error}");
                Debug.Log("[COMBO ROSTER SUCCEEDED]");

                void Record(PresetDeckInfo deck, int test, bool full)
                {
                    int before = scriptErrors;
                    var rival = PickRival(rivals, deck.id, test, full);
                    bool first = (test & 1) == 0;
                    var result = Play(deck, rival, test, first, full, general, links);
                    if (full) matches++; else openings++;
                    if (!result.stopped)
                    {
                        capped++;
                        Debug.LogWarning($"[COMBO ROSTER STOP] deck {deck.id}, case {test}, rival {rival.id}, {(full ? "full" : "opening")}. {result.diagnostic}");
                    }
                    retries += result.retries;
                    writer.WriteLine(string.Join("\t", deck.id, deck.title.Replace('\t', ' '), test, first, rival.id,
                        full ? "full" : "opening", result.stopped, result.won, result.lost, result.summonedTypes, result.extraBodies,
                        result.specialSummons, result.pendulumSummons, result.retries, result.failedMessage, result.failedHint,
                        scriptErrors - before, result.branches, result.changes, result.field.ToString("F3", CultureInfo.InvariantCulture)));
                }
            }
            catch (Exception exception)
            {
                exit = 1;
                Debug.LogError($"[COMBO ROSTER FAILED] {exception}");
            }
            finally
            {
                YgoNativeBridge.Shutdown();
            }
            if (Application.isBatchMode) EditorApplication.Exit(exit);
        }

        private static PresetDeckInfo PickRival(PresetDeckInfo[] rivals, int deck, int test, bool full) =>
            rivals[(deck + 3 * test + (full ? 4 : 0)) % rivals.Length];

        private static Result Play(PresetDeckInfo deck, PresetDeckInfo rival, int test, bool first, bool full,
            DuelAiPolicy general, LinkComboPolicy links)
        {
            using var duel = new DuelSession(recordPlanningReplay: true);
            ulong n = (ulong)deck.id * 97 + (ulong)test * 593 + (full ? 56251UL : 8191UL);
            ulong[] seed = { n * 29 + 101, n * 47 + 7, n * 71 + 13, n * 97 + 3 };
            if (!duel.StartNewDuel(deck.main, deck.extra, rival.main, rival.extra, first, seed: seed))
                throw new InvalidOperationException($"Could not start {deck.id} against {rival.id}.");
            byte seat = (byte)duel.HumanPlayerIndex;
            var testAi = new TacticalAI(general, links, searchCombos: true);
            var rivalAi = new TacticalAI(general, links);
            var result = new Result();
            int turn = first ? 1 : 2;
            int steps = 0;
            var last = new Queue<string>();
            duel.OnCardMoved += move =>
            {
                if (move.toCon != seat || move.toLoc != YgoConstants.LOCATION_MZONE ||
                    (move.reason & YgoConstants.REASON_SPSUMMON) == 0) return;
                result.specialSummons++;
                if (CardDatabase.Get(move.code) is not CardInfo card) return;
                if (move.fromLoc == YgoConstants.LOCATION_EXTRA) result.summonedTypes |= card.type & ExtraTypes;
                if (move.fromLoc == YgoConstants.LOCATION_HAND) result.summonedTypes |= card.type & YgoConstants.TYPE_RITUAL;
            };

            for (int step = 0; step < (full ? 1100 : 260) && !duel.IsFinished && duel.RetryCount <= 4; step++)
            {
                steps = step;
                if (duel.TurnNumber > (full ? 65 : turn))
                {
                    // A card effect can finish the opening without a phase-change button prompt.
                    result.stopped = !full;
                    break;
                }
                var prompt = duel.ActivePrompt;
                if (prompt == null) { duel.StepUntilPrompt(); continue; }
                bool tested = prompt.playerId == seat;
                byte[] response = (tested ? testAi : rivalAi).ComputeResponse(duel, prompt);
                if (last.Count == 8) last.Dequeue();
                last.Enqueue($"{prompt.playerId}/{prompt.messageType}/{prompt.SelectionHint} -> {BitConverter.ToString(response)}");
                if (tested && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                {
                    int action = BitConverter.ToInt32(response, 0);
                    int command = action & 0xffff;
                    if (command == 1 && action >> 16 < prompt.spSummonable.Count)
                    {
                        var summon = prompt.spSummonable[action >> 16];
                        if (summon.location is YgoConstants.LOCATION_SZONE or YgoConstants.LOCATION_PZONE &&
                            CardDatabase.Get(summon.code)?.IsPendulum == true) result.pendulumSummons++;
                    }
                    if (!full && command is 6 or 7)
                    {
                        result.stopped = true;
                        break;
                    }
                }
                int oldRetries = duel.RetryCount;
                duel.SubmitResponse(response);
                if (duel.RetryCount > oldRetries && result.failedMessage == 0)
                {
                    result.failedMessage = prompt.messageType;
                    result.failedHint = prompt.SelectionHint;
                }
            }

            result.stopped |= duel.IsFinished;
            if (!result.stopped)
                result.diagnostic = $"Turn {duel.TurnNumber} phase {duel.CurrentPhase}, step {steps}, next {duel.ActivePrompt?.messageType}. " +
                    $"Prompts {string.Join(" | ", last)}. Events {string.Join(" | ", duel.Logs.TakeLast(10))}";
            result.won = duel.IsFinished && duel.Winner == seat;
            result.lost = duel.IsFinished && duel.Winner == 1 - seat;
            result.retries = duel.RetryCount;
            result.branches = testAi.SimulatedComboBranches;
            result.changes = testAi.ChangedComboLines;
            var own = duel.Players[seat];
            foreach (var monster in own.mzone)
            {
                if (monster?.IsFaceUp != true || CardDatabase.Get(monster.code) is not CardInfo card) continue;
                if ((card.type & (ExtraTypes | YgoConstants.TYPE_RITUAL)) != 0)
                {
                    result.extraBodies++;
                    result.finishers.Add(card.code);
                }
                result.field += .5 + Math.Clamp(monster.attack, 0, 4000) * .0004 + (card.IsExtra ? .7 : 0) +
                    (!monster.effectsDisabled && LinkComboPolicy.IsInteractive(card) ? 1.05 : 0);
            }
            result.field += .15 * own.hand.Count + .2 * own.szone.Count(card => card != null) -
                .7 * duel.Players[1 - seat].mzone.Count(card => card != null);
            return result;
        }
    }
}
