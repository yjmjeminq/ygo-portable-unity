using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YGOPortable.AI;
using YGOPortable.Core;
using Debug = UnityEngine.Debug;

namespace YGOPortable.Editor
{
    internal static class TestComboSearch
    {
        private const uint Machinex = 46593546, Siegfried = 44852429, Gilgamesh = 9024198;
        private const uint Darkfluid = 68934651, Singularity = 21637210;
        private const uint NormalMonster = 97590747, NormalTrap = 44095762;

        private sealed class Output
        {
            internal uint[] opening;
            internal uint[] monsters;
            internal int rank, branches, changed, retries;
            internal double score;
            internal bool conflictReady;
            internal string line;
            internal double slowest;
        }

        internal static void Run()
        {
            VerifyMasterDifficulty();
            var failures = new List<string>();
            var general = DuelAiPolicy.LoadIncluded();
            var links = LinkComboPolicy.LoadIncluded();
            var firewall = ComboRegressionDecks.Get(495);
            var previous = PlayOpening(general, links, firewall, 4, 1, search: false);
            var upgraded = PlayOpening(general, links, firewall, 4, 1, search: true);
            var darkfluid = PlayOpening(general, links, firewall, 11, 1, search: true);
            var singularity = PlayOpening(general, links, firewall, 2, 0, search: true);
            var defendedOpening = PlayOpening(general, links, firewall, 6, 0, search: true);
            if (!previous.opening.SequenceEqual(upgraded.opening) || previous.rank != 4 ||
                !upgraded.monsters.Any(code => code is Darkfluid or Singularity) ||
                upgraded.score <= previous.score || upgraded.changed == 0)
                failures.Add($"Firewall did not improve the same hand into a stronger board with a Link-5 or Link-6 monster. " +
                    $"Reference: {Describe(previous)}. Planner: {Describe(upgraded)}.");
            if (darkfluid.rank != 5 || !darkfluid.monsters.Contains(Darkfluid) || darkfluid.branches == 0)
                failures.Add($"Firewall did not end with Darkfluid in the fixed Link-5 fixture. {Describe(darkfluid)}.");
            if (!singularity.monsters.Contains(Singularity))
                failures.Add($"Firewall could not resolve its first-turn Link-6 line. {Describe(singularity)}.");
            if (defendedOpening.rank < 3 || !defendedOpening.conflictReady)
                failures.Add($"Firewall lost its live Counter Trap when the opening could keep it. {Describe(defendedOpening)}.");

            var ddd = ComboRegressionDecks.Get(34043);
            var monster = PlayOpening(general, links, ddd, 1, 1, search: true);
            var backrow = PlayOpening(general, links, ddd, 1, 2, search: true);
            var scaleLine = PlayOpening(general, links, ddd, 17, 2, search: true);
            if (!monster.opening.SequenceEqual(backrow.opening) || !monster.monsters.Contains(Machinex) ||
                (!backrow.monsters.Contains(Siegfried) && !backrow.monsters.Contains(Gilgamesh)) ||
                monster.line == backrow.line)
                failures.Add($"D/D/D did not adapt the same hand to a monster versus backrow. " +
                    $"Monster board: {Describe(monster)}. Backrow: {Describe(backrow)}.");
            if (!scaleLine.line.Contains(Gilgamesh.ToString()) || !scaleLine.monsters.Contains(Machinex))
                failures.Add($"D/D/D could not continue from Gilgamesh into Machinex. {Describe(scaleLine)}.");
            if (new[] { previous, upgraded, darkfluid, singularity, defendedOpening, monster, backrow, scaleLine }.Any(result => result.retries != 0))
                failures.Add("An adaptive opening produced an illegal engine response.");
            VerifyUncertainDraw();
            Require(failures.Count == 0, string.Join("\n", failures));
            Debug.Log($"[VERIFY COMBO SEARCH] Firewall ended with Darkfluid at Link-5, improved the same comparison hand from Link-{previous.rank} to Link-{upgraded.rank}, and reached first-turn Singularity. " +
                "A different first-turn hand kept a live Counter Trap. D/D/D adapted the same hand to different public boards. Draw outcomes were excluded.");
        }

        internal static void VerifyMasterDifficulty()
        {
            var general = DuelAiPolicy.LoadIncluded();
            var links = LinkComboPolicy.LoadIncluded();
            var firewall = ComboRegressionDecks.Get(495);
            var ddd = ComboRegressionDecks.Get(34043);
            var smartLink = PlayOpening(general, links, firewall, 8, 2, search: true);
            var smartDdd = PlayOpening(general, links, ddd, 17, 2, search: true);
            var masterLink = PlayOpening(general, links, firewall, 8, 2, search: true, difficulty: AIDifficulty.Master);
            var masterDdd = PlayOpening(general, links, ddd, 17, 2, search: true, difficulty: AIDifficulty.Master);
            Require(masterLink.opening.SequenceEqual(smartLink.opening) && masterLink.rank >= 5 &&
                masterDdd.opening.SequenceEqual(smartDdd.opening) && masterDdd.monsters.Contains(Machinex) &&
                new[] { masterLink, masterDdd }.All(result => result.branches > 0 && result.retries == 0),
                $"Master did not finish the native combo routes. Firewall: {Describe(masterLink)}. D/D/D: {Describe(masterDdd)}.");
            Require(masterLink.branches + masterDdd.branches > smartLink.branches + smartDdd.branches,
                $"Master did not use the larger search on the tested boards. Firewall: {Describe(masterLink)}. D/D/D: {Describe(masterDdd)}.");
            Debug.Log($"[VERIFY MASTER COMBO] Firewall Link-{masterLink.rank}, D/D/D ended on Machinex. " +
                $"Master replayed {masterLink.branches} and {masterDdd.branches} lines (Smart: {smartLink.branches} and {smartDdd.branches}).");
        }

        private static void VerifyUncertainDraw()
        {
            const uint upstart = 70368879;
            using var live = new DuelSession(recordPlanningReplay: true);
            Require(live.StartNewDuel(Enumerable.Repeat(upstart, 40).ToArray(), Array.Empty<uint>(),
                Enumerable.Repeat(NormalMonster, 40).ToArray(), Array.Empty<uint>(), seed: new ulong[] { 13, 31, 47, 67 }),
                "Could not start the draw guard fixture.");
            var prompt = live.ActivePrompt;
            int deckCount = live.Players[0].deckCount;
            using var projected = live.ReplayForPlanning(() => false);
            Require(projected?.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD, "Could not recreate the draw guard fixture.");
            int index = projected.ActivePrompt.activatable.FindIndex(effect => effect.code == upstart);
            Require(index >= 0, "The draw guard fixture could not activate Upstart Goblin.");
            projected.BeginSpeculation(0);
            projected.SubmitResponseInt((index << 16) | 5);
            var ai = new TacticalAI();
            for (int step = 0; step < 8 && !projected.SpeculationUncertain; step++)
            {
                var next = projected.ActivePrompt;
                if (next == null || next.messageType == YgoConstants.MSG_SELECT_IDLECMD) break;
                projected.SubmitResponse(ai.ComputeResponse(projected, next));
            }
            Require(projected.SpeculationUncertain && projected.Players[0].deckCount == deckCount - 1,
                "The resolved Upstart Goblin draw was not marked uncertain.");
            Require(live.ActivePrompt == prompt && live.Players[0].deckCount == deckCount,
                "Simulating a draw modified the live duel.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static string Describe(Output output) =>
            $"hand [{string.Join(",", output.opening)}], Link-{output.rank}, " +
            $"field [{string.Join(",", output.monsters)}], {output.branches} branches, {output.changed} changes, {output.retries} retries";

        public static void Probe()
        {
            int exit = 0;
            try
            {
                string streaming = Application.streamingAssetsPath;
                if (YgoNativeBridge.Initialize(streaming, (type, message) => { if (type == 0) Debug.LogWarning(message); }) != 0)
                    throw new InvalidOperationException("Could not initialize the duel engine.");
                CardDatabase.Load(streaming);
                var general = DuelAiPolicy.LoadIncluded();
                var links = LinkComboPolicy.LoadIncluded();
                if (general == null || links == null) throw new InvalidOperationException("Missing trained policies.");

                foreach (int id in new[] { 495, 481, 498, 35006, 34043, 136 })
                {
                    var deck = ComboRegressionDecks.Get(id);
                    int oldFive = 0, newFive = 0, branches = 0, changed = 0, better = 0;
                    double maxTime = 0;
                    for (int seed = 1; seed <= 18; seed++)
                    {
                        int board = seed % 3;
                        var previous = PlayOpening(general, links, deck, seed, board, search: false);
                        var adaptive = PlayOpening(general, links, deck, seed, board, search: true);
                        if (previous.rank >= 5) oldFive++;
                        if (adaptive.rank >= 5) newFive++;
                        if (adaptive.score > previous.score + .05) better++;
                        branches += adaptive.branches;
                        changed += adaptive.changed;
                        maxTime = Math.Max(maxTime, adaptive.slowest);
                        if (adaptive.retries != 0) throw new InvalidOperationException($"{id} seed {seed} retried.");
                        if ((adaptive.rank >= 5 || previous.rank >= 5 || adaptive.monsters.Contains(Machinex) || adaptive.monsters.Contains(Siegfried)) && adaptive.changed > 0)
                        {
                            Debug.Log($"[COMBO SAMPLE] deck {id}, seed {seed}, board {board}, rank {previous.rank}/{adaptive.rank}, " +
                                $"value {previous.score:F2}/{adaptive.score:F2}, changed {adaptive.changed}, " +
                                $"monsters {string.Join(",", adaptive.monsters)}, hand {string.Join(",", adaptive.opening)}, line {adaptive.line}");
                        }
                    }
                    Debug.Log($"[COMBO PROBE] {id} {deck.title}, Link-5+ {oldFive}/{newFive}, better {better}/18, branches {branches}, changes {changed}, max {maxTime:F1} ms");
                }

                var ddd = ComboRegressionDecks.Get(34043);
                for (int seed = 1; seed <= 26; seed++)
                {
                    var monsters = PlayOpening(general, links, ddd, seed, 1, search: true);
                    var backrow = PlayOpening(general, links, ddd, seed, 2, search: true);
                    if (monsters.opening.SequenceEqual(backrow.opening) && monsters.monsters.Length >= 2 && backrow.monsters.Length >= 2 &&
                        (monsters.changed > 0 || backrow.changed > 0) && !monsters.monsters.OrderBy(card => card).SequenceEqual(backrow.monsters.OrderBy(card => card)))
                        Debug.Log($"[COMBO ADAPT] seed {seed}, monster {string.Join(",", monsters.monsters)} ({monsters.changed}), backrow {string.Join(",", backrow.monsters)} ({backrow.changed}), hand {string.Join(",", monsters.opening)}");
                }
                Debug.Log("[COMBO PROBE SUCCEEDED]");
            }
            catch (Exception exception)
            {
                exit = 1;
                Debug.LogError($"[COMBO PROBE FAILED] {exception}");
            }
            finally
            {
                YgoNativeBridge.Shutdown();
            }
            if (Application.isBatchMode) EditorApplication.Exit(exit);
        }

        private static Output PlayOpening(DuelAiPolicy general, LinkComboPolicy links, PresetDeckInfo deck, int seed, int board,
            bool search, AIDifficulty difficulty = AIDifficulty.Smart)
        {
            var enemy = Enumerable.Repeat(board == 2 ? NormalTrap : NormalMonster, 40).ToArray();
            var seeds = new ulong[] { (ulong)seed * 29 + 101, (ulong)seed * 47 + 7, (ulong)seed * 71 + 13, (ulong)seed * 97 + 3 };
            using var duel = new DuelSession(recordPlanningReplay: search);
            if (!duel.StartNewDuel(deck.main, deck.extra, enemy, Array.Empty<uint>(), board == 0, seed: seeds))
                throw new InvalidOperationException("Could not start the sampled duel.");
            byte seat = (byte)duel.HumanPlayerIndex;
            var adaptive = new TacticalAI(general, links, searchCombos: search) { Difficulty = difficulty };
            var enemyAi = new TacticalAI(general);
            int placed = 0;
            var opening = Array.Empty<uint>();
            var steps = new List<uint>();
            double slowest = 0;
            int planningBudget = difficulty == AIDifficulty.Master ? ComboSearch.MasterPlanningMilliseconds :
                ComboSearch.LinkOpeningPlanningMilliseconds;
            int planningStepLimit = (planningBudget + ComboSearch.PlanningSliceMilliseconds - 1) /
                ComboSearch.PlanningSliceMilliseconds;
            bool canBattle = false;
            for (int i = 0; !duel.IsFinished && duel.RetryCount == 0 && i < 180; i++)
            {
                if (duel.TurnNumber > (board == 0 ? 1 : 2)) break;
                var prompt = duel.ActivePrompt;
                if (prompt == null) { duel.StepUntilPrompt(); continue; }
                if (prompt.playerId != seat)
                {
                    if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced) duel.SubmitResponseInt(-1);
                    else if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && prompt.canEp)
                    {
                        if (board == 1 && placed++ == 0 && prompt.summonable.Count > 0) duel.SubmitResponseInt(0);
                        else if (board == 2 && placed++ < 2 && prompt.sSetable.Count > 0) duel.SubmitResponseInt(4);
                        else duel.SubmitResponseInt(7);
                    }
                    else duel.SubmitResponse(enemyAi.ComputeResponse(duel, prompt));
                    continue;
                }

                var watch = Stopwatch.StartNew();
                int planningSteps = 0;
                while (!adaptive.AdvanceComboPlanning(duel, prompt))
                {
                    slowest = Math.Max(slowest, watch.Elapsed.TotalMilliseconds);
                    if (++planningSteps > planningStepLimit)
                        throw new InvalidOperationException($"{difficulty} combo planning did not finish within {planningStepLimit} slices " +
                            $"for deck {deck.id}, seed {seed}, board {board}, turn {duel.TurnNumber}.");
                    watch.Restart();
                }
                var response = adaptive.ComputeResponse(duel, prompt);
                slowest = Math.Max(slowest, watch.Elapsed.TotalMilliseconds);
                if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                {
                    if (opening.Length == 0) opening = duel.Players[seat].hand.Select(card => card.code).ToArray();
                    int action = BitConverter.ToInt32(response, 0);
                    int index = action >> 16;
                    int command = action & 0xffff;
                    canBattle = prompt.canBp;
                    if (command is 6 or 7) break;
                    if (command == 1) steps.Add(prompt.spSummonable[index].code);
                }
                duel.SubmitResponse(response);
            }

            var own = duel.Players[seat];
            var final = own.mzone.Where(card => card?.IsFaceUp == true).Select(card => card.code).ToArray();
            return new Output
            {
                opening = opening, monsters = final, rank = final.Select(LinkComboPolicy.Rank).DefaultIfEmpty().Max(),
                branches = adaptive.SimulatedComboBranches, changed = adaptive.ChangedComboLines, retries = duel.RetryCount,
                conflictReady = own.szone.Any(card => card is { code: 7403341, IsFaceUp: false }) &&
                    final.Any(code => code is 1861629 or LinkTurnPlan.TranscodeTalker),
                score = ComboSearch.BoardValue(duel, seat, canBattle), line = string.Join(">", steps), slowest = slowest,
            };
        }
    }
}
