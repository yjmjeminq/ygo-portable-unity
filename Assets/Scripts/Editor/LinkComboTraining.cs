using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YGOPortable.AI;
using YGOPortable.Core;
using Debug = UnityEngine.Debug;

namespace YGOPortable.Editor
{
    internal static class LinkComboTraining
    {
        private const int Rounds = 4;
        private const int Population = 10;
        private const int HandsPerDeck = 4;
        private const int RuntimeFinalists = 3;
        private const int MinimumRuntimeHeldoutMatches = 120;

        private static readonly int[] TrainingDecks = { 51433, 51435, 51439, 51873, 51875, 51879, 51895, 51899, 85003, 85004, 85014, 20676 };
        private static readonly int[] TestDecks = { 51404, 51409, 51775, 51779, 85024, 85033, 85043, 85053, 51414, 51784, 51814, 51884 };
        private static readonly int[] RivalDecks = { 20670, 20671, 20672, 20674, 20678, 20686, 51049, 50949 };

        private sealed class Opening
        {
            internal PresetDeckInfo deck;
            internal PresetDeckInfo opponent;
            internal ulong[] seed;
            internal bool first;
        }

        private sealed class SeatResults
        {
            internal int games, linkThree, linkFour, interactive, wins, losses, draws, capped;
            internal double score, board, damage, removed;
            internal double Mean => score / Math.Max(1, games);
            internal double MeanBoard => board / Math.Max(1, games);
            internal double MatchRate => (wins + .5 * draws) / Math.Max(1, games);
            internal string OpeningSummary() => $"{games} games, Link-3+ {linkThree}, Link-4+ {linkFour}, " +
                $"interactive {interactive}, board {MeanBoard:F2}, damage {damage / Math.Max(1, games):P1}, " +
                $"removed {removed / Math.Max(1, games):F2}, reward {Mean:F2}, {capped} capped";
            internal string MatchSummary() => $"{wins}W {losses}L {draws}D, {capped} capped";
        }

        private sealed class Results
        {
            internal int games, linkThree, linkFour, interactive, wins, losses, draws, retries, capped;
            internal double score;
            internal readonly SeatResults First = new SeatResults();
            internal readonly SeatResults Second = new SeatResults();
            internal readonly Dictionary<uint, int> Finishers = new Dictionary<uint, int>();
            internal double Mean => score / Math.Max(1, games);
            internal double SetupQuality => .5 * (First.Mean + Second.Mean) +
                (.65 * First.linkThree + 1.1 * First.linkFour) / Math.Max(1, First.games) -
                4.0 * capped / Math.Max(1, games);
            internal string OpeningSummary() => $"{games} openings, first [{First.OpeningSummary()}], " +
                $"second [{Second.OpeningSummary()}], {retries} retries";
            internal string MatchSummary() => $"{wins}W {losses}L {draws}D in {games}, first [{First.MatchSummary()}], " +
                $"second [{Second.MatchSummary()}], {retries} retries";
        }

        internal static void VerifyIncluded()
        {
            var general = DuelAiPolicy.LoadIncluded();
            var policy = LinkComboPolicy.LoadIncluded();
            if (general == null || policy == null) throw new InvalidOperationException("Missing included duel policies.");

            var decks = DeckRepository.PresetDecks.ToDictionary(deck => deck.id);
            var fixtures = CreateOpenings(decks, TestDecks, 12, 788353);
            var original = Run(general, null, fixtures, fullDuel: false);
            var trained = Run(general, policy, fixtures, fullDuel: false);
            if (trained.linkThree <= original.linkThree || trained.linkFour < original.linkFour || trained.retries > original.retries)
                throw new InvalidOperationException($"Link opening regression. Previous {original.OpeningSummary()}, current {trained.OpeningSummary()}");
            Debug.Log($"[VERIFY AI LINK] Previous {original.OpeningSummary()}. Trained {trained.OpeningSummary()}.");
        }

        public static void Train()
        {
            int exit = 0;
            bool initialize = !YgoNativeBridge.IsInitialized;
            try
            {
                var elapsed = Stopwatch.StartNew();
                var errors = new HashSet<string>();
                string streaming = Application.streamingAssetsPath;
                if (initialize && YgoNativeBridge.Initialize(streaming, (type, message) => { if (type == 0) errors.Add(message); }) != 0)
                    throw new InvalidOperationException("Could not start the duel engine for Link training.");
                CardDatabase.Load(streaming);
                DeckRepository.Load(streaming, Path.Combine(Path.GetTempPath(), "ygo_link_training"), seedDefaultDecks: false);
                var decks = DeckRepository.PresetDecks.ToDictionary(deck => deck.id);
                var general = DuelAiPolicy.LoadIncluded() ?? throw new InvalidOperationException("Missing the general AI model.");
                var previousPolicy = LinkComboPolicy.LoadIncluded() ?? throw new InvalidOperationException("Missing the included Link AI model.");
                var initial = previousPolicy.CopyWeights();
                var center = (float[])initial.Clone();
                var random = new System.Random(273370);
                var finalists = new List<float[]>();
                const int trainingOpenings = Rounds * Population * HandsPerDeck;

                for (int round = 0; round < Rounds; round++)
                {
                    var fixtures = CreateOpenings(decks, TrainingDecks, HandsPerDeck, 59501 + round);
                    var candidates = new float[Population][];
                    candidates[0] = center;
                    candidates[1] = finalists.Count == 0 ? initial : finalists[^4];
                    for (int i = 2; i < candidates.Length - 2; i++)
                        candidates[i] = Sample(center, random, .8f / (1 + .3f * round));
                    candidates[^2] = Sample(initial, random, .08f);
                    candidates[^1] = Sample(initial, random, .08f);
                    var ranked = candidates.Select(weights => (weights, outcome: Run(general, new LinkComboPolicy(weights), fixtures, fullDuel: false)))
                        .OrderByDescending(candidate => candidate.outcome.SetupQuality).ToArray();
                    finalists.AddRange(ranked.Take(2).Select(candidate => candidate.weights));
                    finalists.Add(candidates[^2]);
                    finalists.Add(candidates[^1]);
                    var elite = ranked.Take(3).Select(candidate => candidate.weights).ToArray();
                    center = Enumerable.Range(0, initial.Length).Select(i => elite.Average(weights => weights[i])).ToArray();
                    Debug.Log($"[LINK TRAIN] Round {round + 1}/{Rounds}, {ranked[0].outcome.OpeningSummary()} at {elapsed.Elapsed.TotalSeconds:F1}s");
                }

                finalists.Add(center);
                var validation = CreateOpenings(decks, TrainingDecks, 7, 92180);
                var fullValidation = CreateOpenings(decks, TrainingDecks, 3, 66457);
                var previousValidation = Run(general, previousPolicy, validation, fullDuel: false);
                var previousValidationMatches = Run(general, previousPolicy, fullValidation, fullDuel: true);
                Debug.Log($"[LINK VALIDATE] Baseline openings {previousValidation.OpeningSummary()}");
                Debug.Log($"[LINK VALIDATE] Baseline matches {previousValidationMatches.MatchSummary()}");
                var validated = new List<(string name, LinkComboPolicy policy, Results opening)>();
                foreach (var candidate in ValidationCandidates(initial, finalists))
                {
                    var policy = new LinkComboPolicy(candidate.weights);
                    var opening = Run(general, policy, validation, fullDuel: false);
                    bool retained = KeepsLinkStrength(previousValidation, opening);
                    Debug.Log($"[LINK VALIDATE] {candidate.name}, retained={retained}, " +
                        $"objective {opening.SetupQuality:F4}, {opening.OpeningSummary()}, " +
                        $"gate deltas Link-3 {opening.linkThree - previousValidation.linkThree}, " +
                        $"Link-4 {opening.linkFour - previousValidation.linkFour}, " +
                        $"first board {opening.First.MeanBoard - previousValidation.First.MeanBoard:F6}, " +
                        $"first reward {opening.First.Mean - previousValidation.First.Mean:F6}, " +
                        $"second reward {opening.Second.Mean - previousValidation.Second.Mean:F6}");
                    if (retained) validated.Add((candidate.name, policy, opening));
                }

                var shortlist = validated
                    .OrderByDescending(candidate => candidate.opening.SetupQuality).Take(RuntimeFinalists)
                    .Select(candidate =>
                    {
                        var match = Run(general, candidate.policy, fullValidation, fullDuel: true);
                        Debug.Log($"[LINK VALIDATE] {candidate.name} matches, retained=" +
                            $"{KeepsMatchStrength(previousValidationMatches, match)}, {match.MatchSummary()}");
                        return (candidate.name, candidate.policy, candidate.opening, match);
                    })
                    .Where(candidate => KeepsMatchStrength(previousValidationMatches, candidate.match))
                    .ToArray();
                if (shortlist.Length == 0)
                    throw new InvalidOperationException($"No trained Link candidate passed opening and match preservation. " +
                        $"{validated.Count} candidates passed the opening gate.");

                var runtimeValidation = CreateRuntimeOpenings(decks, TrainingDecks, 1, 640813);
                var previousRuntimeValidation = Run(general, previousPolicy, runtimeValidation, fullDuel: true,
                    runtimePlanning: true, rivalLink: previousPolicy, stage: "validation included");
                if (errors.Count != 0 || previousRuntimeValidation.retries != 0 || previousRuntimeValidation.capped != 0)
                    throw new InvalidOperationException("The included runtime Link validation did not finish cleanly.");
                var runtimeFinalists = new List<(string name, LinkComboPolicy policy, Results opening, Results match, Results runtime)>();
                foreach (var candidate in shortlist)
                {
                    var runtime = Run(general, candidate.policy, runtimeValidation, fullDuel: true,
                        runtimePlanning: true, rivalLink: previousPolicy, stage: $"validation {candidate.name}");
                    bool retained = errors.Count == 0 && KeepsRuntimeStrength(previousRuntimeValidation, runtime);
                    Debug.Log($"[LINK RUNTIME] {candidate.name}, retained={retained}, {runtime.OpeningSummary()}, {runtime.MatchSummary()}");
                    if (retained) runtimeFinalists.Add((candidate.name, candidate.policy, candidate.opening, candidate.match, runtime));
                }
                var best = runtimeFinalists
                    .OrderByDescending(candidate => candidate.runtime.SetupQuality +
                        2.0 * (candidate.runtime.wins + .5 * candidate.runtime.draws) / candidate.runtime.games)
                    .FirstOrDefault();
                if (best.policy == null)
                    throw new InvalidOperationException("No Link finalist preserved runtime-planner boards, both seat roles and match strength.");
                Debug.Log($"[LINK TRAIN] Selected after {trainingOpenings * TrainingDecks.Length} trained openings. Validation {best.opening.OpeningSummary()}, {best.match.MatchSummary()}");

                var test = CreateOpenings(decks, TestDecks, 12, 788353);
                var previous = Run(general, previousPolicy, test, fullDuel: false);
                var untuned = Run(general, new LinkComboPolicy(initial), test, fullDuel: false);
                var trained = Run(general, best.policy, test, fullDuel: false);
                Debug.Log($"[LINK EVAL] Previous {previous.OpeningSummary()}");
                Debug.Log($"[LINK EVAL] Before Link training {untuned.OpeningSummary()}");
                Debug.Log($"[LINK EVAL] Trained {trained.OpeningSummary()}");
                Debug.Log($"[LINK EVAL] Finishers {string.Join(", ", trained.Finishers.OrderByDescending(pair => pair.Value).Take(10).Select(pair => $"{CardDatabase.GetName(pair.Key)} {pair.Value}"))}");

                var fullTest = CreateOpenings(decks, TestDecks, 8, 829274);
                var previousMatches = Run(general, previousPolicy, fullTest, fullDuel: true);
                var trainedMatches = Run(general, best.policy, fullTest, fullDuel: true);
                Debug.Log($"[LINK EVAL] Complete duels previous {previousMatches.MatchSummary()} and trained {trainedMatches.MatchSummary()}");
                if (errors.Count != 0 || !KeepsLinkStrength(previous, trained) || !KeepsMatchStrength(previousMatches, trainedMatches))
                    throw new InvalidOperationException("The trained Link policy did not preserve the fast held-out boards and match strength.");

                // Independent held-out decks and samples are used once, after the runtime finalist is fixed.
                var runtimeTest = CreateRuntimeOpenings(decks, TestDecks, 5, 1732493, MinimumRuntimeHeldoutMatches);
                var previousRuntimeMatches = Run(general, previousPolicy, runtimeTest, fullDuel: true,
                    runtimePlanning: true, rivalLink: previousPolicy, stage: "held-out included");
                if (errors.Count != 0 || !HasCompletedRuntimeHeldout(previousRuntimeMatches) ||
                    previousRuntimeMatches.retries != 0 || previousRuntimeMatches.capped != 0)
                    throw new InvalidOperationException("The included runtime Link held-out cohort did not complete at least 120 balanced clean duels.");
                var trainedRuntimeMatches = Run(general, best.policy, runtimeTest, fullDuel: true,
                    runtimePlanning: true, rivalLink: previousPolicy, stage: "held-out trained");
                Debug.Log($"[LINK EVAL] Runtime previous {previousRuntimeMatches.OpeningSummary()}, {previousRuntimeMatches.MatchSummary()}");
                Debug.Log($"[LINK EVAL] Runtime trained {trainedRuntimeMatches.OpeningSummary()}, {trainedRuntimeMatches.MatchSummary()}");
                Debug.Log($"[LINK TRAIN] Time {elapsed.Elapsed.TotalSeconds:F1}s, native script errors {errors.Count}");
                if (errors.Count > 0) Debug.LogWarning(string.Join("\n", errors.Take(3)));

                if (errors.Count != 0 || !HasCompletedRuntimeHeldout(previousRuntimeMatches) ||
                    !HasCompletedRuntimeHeldout(trainedRuntimeMatches) ||
                    !KeepsRuntimeStrength(previousRuntimeMatches, trainedRuntimeMatches) ||
                    trainedRuntimeMatches.SetupQuality <= previousRuntimeMatches.SetupQuality)
                    throw new InvalidOperationException("The trained Link policy did not improve the held-out boards without losing match strength.");

                string asset = $"Assets/Resources/{LinkComboPolicy.ResourceName}.json";
                File.WriteAllText(Path.Combine(Application.dataPath, "Resources", LinkComboPolicy.ResourceName + ".json"), best.policy.ToJson() + "\n");
                AssetDatabase.ImportAsset(asset);
                Debug.Log($"[LINK TRAIN SUCCEEDED] Wrote {asset}");
            }
            catch (Exception error)
            {
                exit = 1;
                Debug.LogError($"[LINK TRAIN FAILED] {error}");
                if (!Application.isBatchMode) throw;
            }
            finally
            {
                if (initialize) YgoNativeBridge.Shutdown();
            }
            if (Application.isBatchMode) EditorApplication.Exit(exit);
        }

        private static bool KeepsLinkStrength(Results previous, Results candidate) =>
            candidate.linkThree >= previous.linkThree && candidate.linkFour >= previous.linkFour &&
            candidate.retries == 0 && candidate.capped == 0 && candidate.Mean >= previous.Mean &&
            candidate.First.MeanBoard >= previous.First.MeanBoard && candidate.First.Mean >= previous.First.Mean &&
            candidate.Second.Mean >= previous.Second.Mean;

        private static bool KeepsMatchStrength(Results previous, Results candidate) =>
            candidate.retries == 0 && candidate.capped == 0 &&
            candidate.First.games == previous.First.games && candidate.Second.games == previous.Second.games &&
            candidate.First.MatchRate >= previous.First.MatchRate && candidate.Second.MatchRate >= previous.Second.MatchRate;

        private static bool KeepsRuntimeStrength(Results previous, Results candidate) =>
            previous.retries == 0 && previous.capped == 0 && KeepsLinkStrength(previous, candidate) &&
            KeepsMatchStrength(previous, candidate) &&
            candidate.First.linkThree >= previous.First.linkThree && candidate.First.linkFour >= previous.First.linkFour &&
            candidate.Second.linkThree >= previous.Second.linkThree && candidate.Second.linkFour >= previous.Second.linkFour &&
            candidate.First.interactive >= previous.First.interactive && candidate.Second.interactive >= previous.Second.interactive &&
            candidate.Second.MeanBoard >= previous.Second.MeanBoard;

        private static bool HasCompletedRuntimeHeldout(Results results) =>
            results.wins + results.losses + results.draws == results.games &&
            results.games >= MinimumRuntimeHeldoutMatches && results.First.games == results.Second.games;

        private static IEnumerable<(string name, float[] weights)> ValidationCandidates(float[] initial, List<float[]> finalists)
        {
            var seen = new List<float[]>();
            var fractions = new[] { 1f, .25f, .08f };
            for (int i = 0; i < finalists.Count; i++)
            {
                foreach (float fraction in fractions)
                {
                    var weights = Enumerable.Range(0, initial.Length)
                        .Select(feature => initial[feature] + fraction * (finalists[i][feature] - initial[feature])).ToArray();
                    if (weights.SequenceEqual(initial) || seen.Any(previous => weights.SequenceEqual(previous))) continue;
                    seen.Add(weights);
                    yield return ($"candidate {i + 1}, residual {fraction:P0}", weights);
                }
            }
        }

        private static Opening[] CreateOpenings(Dictionary<int, PresetDeckInfo> decks, int[] ids, int hands, int seed)
        {
            var random = new System.Random(seed);
            var validIds = ids.Where(id => decks.ContainsKey(id)).ToArray();
            var validRivals = RivalDecks.Where(id => decks.ContainsKey(id)).ToArray();
            if (validIds.Length == 0 || validRivals.Length == 0)
                throw new InvalidOperationException("Link training requires candidate and rival decks.");
            return validIds.SelectMany(id => Enumerable.Range(0, hands).Select(hand => new Opening
            {
                deck = decks[id], opponent = decks[validRivals[random.Next(validRivals.Length)]], first = (hand & 1) == 0,
                seed = Enumerable.Range(0, 4).Select(_ => (ulong)random.Next(1, int.MaxValue)).ToArray(),
            })).ToArray();
        }

        private static Opening[] CreateRuntimeOpenings(Dictionary<int, PresetDeckInfo> decks, int[] ids,
            int pairsPerDeck, int seed, int minimumMatches = 0)
        {
            var validIds = ids.Where(decks.ContainsKey).ToArray();
            var rivals = RivalDecks.Where(decks.ContainsKey).ToArray();
            if (validIds.Length == 0 || rivals.Length == 0)
                throw new InvalidOperationException("Runtime Link evaluation requires candidate and rival decks.");
            pairsPerDeck = Math.Max(pairsPerDeck, (minimumMatches + 2 * validIds.Length - 1) / (2 * validIds.Length));
            var random = new System.Random(seed);
            var fixtures = new List<Opening>();
            int sample = 0;
            foreach (int id in validIds)
                for (int pair = 0; pair < pairsPerDeck; pair++)
                {
                    var opponent = decks[rivals[sample++ % rivals.Length]];
                    var shuffle = Enumerable.Range(0, 4).Select(_ => (ulong)random.Next(1, int.MaxValue)).ToArray();
                    // Each sampled deck/opponent/shuffle is played in both roles, unchanged for every policy.
                    foreach (bool first in new[] { true, false })
                        fixtures.Add(new Opening { deck = decks[id], opponent = opponent, seed = (ulong[])shuffle.Clone(), first = first });
                }
            return fixtures.ToArray();
        }

        private static float[] Sample(float[] center, System.Random random, float deviation)
        {
            var weights = new float[center.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                double normal = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
                weights[i] = Math.Clamp(center[i] + deviation * (float)normal, -4, 5);
            }
            return weights;
        }

        private static Results Run(DuelAiPolicy general, LinkComboPolicy link, Opening[] openings, bool fullDuel,
            bool runtimePlanning = false, LinkComboPolicy rivalLink = null, string stage = null)
        {
            if (runtimePlanning && (!fullDuel || rivalLink == null))
                throw new InvalidOperationException("Runtime Link evaluation needs full duels and a frozen rival Link policy.");
            var results = new Results();
            var elapsed = stage == null ? null : Stopwatch.StartNew();
            if (elapsed != null) Debug.Log($"[LINK RUNTIME] Starting {stage}: {openings.Length} complete duels, matched first/second seats.");
            foreach (var opening in openings)
            {
                using var duel = new DuelSession(recordPlanningReplay: runtimePlanning);
                if (!duel.StartNewDuel(opening.deck.main, opening.deck.extra, opening.opponent.main, opening.opponent.extra,
                    opening.first, seed: (ulong[])opening.seed.Clone()))
                    throw new InvalidOperationException($"Could not start {opening.deck.title} training duel.");
                int seat = duel.HumanPlayerIndex;
                int comboTurn = opening.first ? 1 : 2;
                var candidate = new TacticalAI(general, link, searchCombos: runtimePlanning);
                var opponent = new TacticalAI(general, rivalLink, searchCombos: runtimePlanning);
                var side = opening.first ? results.First : results.Second;
                bool measuredOpening = false;
                int opposingStartLife = 0, opposingStartCards = 0;
                int limit = fullDuel ? 1000 : 180;
                for (int step = 0; step < limit && !duel.IsFinished && duel.RetryCount <= 3; step++)
                {
                    if (duel.TurnNumber > (fullDuel ? 65 : comboTurn)) break;
                    var prompt = duel.ActivePrompt;
                    if (prompt == null) { duel.StepUntilPrompt(); continue; }
                    if (duel.TurnNumber == comboTurn && prompt.playerId == seat && opposingStartLife == 0)
                    {
                        var rival = duel.Players[1 - seat];
                        opposingStartLife = rival.lp;
                        opposingStartCards = PublicFieldCards(rival);
                    }
                    byte[] response = (prompt.playerId == seat ? candidate : opponent).ComputeResponse(duel, prompt);
                    // Going-second openings include Battle and Main Phase 2, so a finisher earns its actual damage.
                    int turn = duel.TurnNumber;
                    duel.SubmitResponse(response);
                    if (runtimePlanning && !measuredOpening && turn == comboTurn &&
                        (duel.IsFinished || duel.TurnNumber > comboTurn))
                    {
                        MeasureBoard(duel, seat, opening.first, opposingStartLife, opposingStartCards, side, results);
                        measuredOpening = true;
                    }
                }
                results.games++;
                side.games++;
                results.retries += duel.RetryCount;
                if (!duel.IsFinished && (fullDuel || duel.TurnNumber <= comboTurn))
                {
                    results.capped++;
                    side.capped++;
                }
                else if (duel.IsFinished)
                {
                    if (duel.Winner == 2) { results.draws++; side.draws++; }
                    else if (duel.Winner == seat) { results.wins++; side.wins++; }
                    else if (duel.Winner == 1 - seat) { results.losses++; side.losses++; }
                    else { results.capped++; side.capped++; }
                }
                if (!fullDuel || runtimePlanning && !measuredOpening)
                    MeasureBoard(duel, seat, opening.first, opposingStartLife, opposingStartCards, side, results);
                if (elapsed != null && (results.games % 8 == 0 || results.games == openings.Length))
                    Debug.Log($"[LINK RUNTIME] {stage}: {results.games}/{openings.Length}, " +
                        $"last decks {opening.deck.id}/{opening.opponent.id}, first={opening.first}, seed={opening.seed[0]}, " +
                        $"{results.MatchSummary()}, {elapsed.Elapsed.TotalSeconds:F1}s");
            }
            return results;
        }

        private static int PublicFieldCards(PlayerFieldSnapshot player) =>
            player.mzone.Count(card => card != null) + player.szone.Count(card => card != null);

        private static void MeasureBoard(DuelSession duel, int seat, bool first, int opposingStartLife,
            int opposingStartCards, SeatResults side, Results results)
        {
            var own = duel.Players[seat];
            int highest = 0, interaction = 0;
            foreach (var monster in own.mzone)
            {
                if (monster?.IsFaceUp != true) continue;
                int rating = LinkComboPolicy.Rank(monster.code);
                highest = Math.Max(highest, rating);
                if (rating >= 3 && !monster.effectsDisabled && CardDatabase.Get(monster.code) is CardInfo card && LinkComboPolicy.IsInteractive(card))
                    interaction++;
                if (rating < 3) continue;
                results.Finishers.TryGetValue(monster.code, out int count);
                results.Finishers[monster.code] = count + 1;
            }
            if (highest >= 3) { results.linkThree++; side.linkThree++; }
            if (highest >= 4) { results.linkFour++; side.linkFour++; }
            if (interaction > 0) { results.interactive++; side.interactive++; }
            var rival = duel.Players[1 - seat];
            double board = ComboSearch.BoardValue(duel, (byte)seat, canBattle: false);
            double damage = opposingStartLife > 0 ? Math.Clamp((opposingStartLife - rival.lp) / (double)opposingStartLife, 0, 1) : 0;
            int removed = Math.Max(0, opposingStartCards - PublicFieldCards(rival));
            bool won = duel.IsFinished && duel.Winner == seat;
            double reward = first ? .7 * highest + .65 * interaction + .75 * board :
                .25 * highest + .3 * interaction + .5 * board + 3.0 * damage + .75 * removed + (won ? 6.0 : 0);
            reward -= duel.RetryCount * 1.5;
            side.score += reward;
            side.board += board;
            side.damage += damage;
            side.removed += removed;
            results.score += reward;
        }
    }
}
