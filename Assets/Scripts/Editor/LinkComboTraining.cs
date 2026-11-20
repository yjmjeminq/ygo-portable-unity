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
        private const int RuntimeValidationPairsPerDeck = 2;
        private const int MinimumRuntimeHeldoutMatches = 120;
        private const double MaximumWinSignificance = .05;
        private const int NativeRescueRounds = 2;
        private const int MaximumNativeRescueGames = 240;

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
            internal double SelectionScore => Mean + 2 * MatchRate;
            internal string OpeningSummary() => $"{games} games, Link-3+ {linkThree}, Link-4+ {linkFour}, " +
                $"interactive {interactive}, board {MeanBoard:F2}, damage {damage / Math.Max(1, games):P1}, " +
                $"removed {removed / Math.Max(1, games):F2}, reward {Mean:F2}, {capped} capped";
            internal string MatchSummary() => $"{wins}W {losses}L {draws}D, {capped} capped";
        }

        private sealed class Results
        {
            internal int games, linkThree, linkFour, interactive, wins, losses, draws, retries, capped;
            internal int completedTurns;
            internal double score, turnEndBoard;
            internal readonly SeatResults First = new SeatResults();
            internal readonly SeatResults Second = new SeatResults();
            internal readonly Dictionary<uint, int> Finishers = new Dictionary<uint, int>();
            internal double Mean => score / Math.Max(1, games);
            internal double MeanTurnEndBoard => turnEndBoard / Math.Max(1, completedTurns);
            internal int WinPointsTwice => 2 * wins + draws;
            internal double SetupQuality => .5 * (First.Mean + Second.Mean) +
                (.65 * First.linkThree + 1.1 * First.linkFour) / Math.Max(1, First.games) -
                4.0 * capped / Math.Max(1, games);
            internal string OpeningSummary() => $"{games} openings, first [{First.OpeningSummary()}], " +
                $"second [{Second.OpeningSummary()}], {retries} retries";
            internal string MatchSummary() => $"{wins}W {losses}L {draws}D in {games}, first [{First.MatchSummary()}], " +
                $"second [{Second.MatchSummary()}], {retries} retries";
        }

        private sealed class PairedResults
        {
            internal readonly Results Previous = new Results();
            internal readonly Results Candidate = new Results();
            internal int improvedPairs, regressedPairs;

            internal double WinSignificance => DuelAiTraining.WinImprovementProbability(improvedPairs, regressedPairs);
        }

        private sealed class ConsumedResponse
        {
            internal string context, answer;
            internal bool own;
        }

        private sealed class RescueComparison
        {
            internal readonly Results Previous = new Results();
            internal readonly Results Candidate = new Results();
            internal readonly int[] ownFirstChanges = new int[2];
            internal int opponentFirstChanges, unmatchedContexts;
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
                var initialSecond = previousPolicy.CopyWeights(goingSecond: true);
                bool hasSeparateWeights = !initial.SequenceEqual(initialSecond);
                LinkComboPolicy CandidatePolicy(float[] weights) => hasSeparateWeights
                    ? new LinkComboPolicy(weights, initialSecond.Select((weight, index) =>
                        Math.Clamp(weight + (weights[index] - initial[index]), -8f, 8f)).ToArray())
                    : new LinkComboPolicy(weights);
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
                    var ranked = candidates.Select(weights => (weights, outcome: Run(general, CandidatePolicy(weights), fixtures, fullDuel: false)))
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
                var validated = new List<(int source, string name, LinkComboPolicy policy, Results opening)>();
                var fastAlternatives = new List<(string name, LinkComboPolicy policy, Results results)>();
                foreach (var candidate in ValidationCandidates(initial, finalists))
                {
                    var policy = CandidatePolicy(candidate.weights);
                    var opening = Run(general, policy, validation, fullDuel: false);
                    fastAlternatives.Add((candidate.name, policy, opening));
                    bool retained = KeepsLinkStrength(previousValidation, opening);
                    Debug.Log($"[LINK VALIDATE] {candidate.name}, retained={retained}, " +
                        $"objective {opening.SetupQuality:F4}, {opening.OpeningSummary()}, " +
                        $"gate deltas Link-3 {opening.linkThree - previousValidation.linkThree}, " +
                        $"Link-4 {opening.linkFour - previousValidation.linkFour}, " +
                        $"first board {opening.First.MeanBoard - previousValidation.First.MeanBoard:F6}, " +
                        $"first reward {opening.First.Mean - previousValidation.First.Mean:F6}, " +
                        $"second reward {opening.Second.Mean - previousValidation.Second.Mean:F6}");
                    if (retained) validated.Add((candidate.source, candidate.name, policy, opening));
                }

                // Residual strengths from the same learned weights compete for a single finalist slot.
                var shortlist = validated.OrderByDescending(candidate => candidate.opening.SetupQuality)
                    .GroupBy(candidate => candidate.source).Select(group => group.First()).Take(RuntimeFinalists)
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

                var runtimeValidation = CreateRuntimeOpenings(decks, TrainingDecks, RuntimeValidationPairsPerDeck, 640813);
                var previousRuntimeValidation = Run(general, previousPolicy, runtimeValidation, fullDuel: true,
                    runtimePlanning: true, rivalLink: previousPolicy, stage: "validation included");
                if (errors.Count != 0 || previousRuntimeValidation.retries != 0 || previousRuntimeValidation.capped != 0)
                    throw new InvalidOperationException("The included runtime Link validation did not finish cleanly.");
                Debug.Log($"[LINK RUNTIME] Included opening board {previousRuntimeValidation.OpeningSummary()}");
                var firstPolicy = previousPolicy;
                var secondPolicy = previousPolicy;
                var firstResult = previousRuntimeValidation.First;
                var secondResult = previousRuntimeValidation.Second;
                var runtimeFinalists = new List<(string name, LinkComboPolicy policy, Results opening, Results match, double improvement)>();
                var nativeAlternatives = new List<(string name, LinkComboPolicy policy, Results results)>();
                foreach (var candidate in shortlist)
                {
                    var runtime = Run(general, candidate.policy, runtimeValidation, fullDuel: true,
                        runtimePlanning: true, rivalLink: previousPolicy, stage: $"validation {candidate.name}");
                    nativeAlternatives.Add((candidate.name, candidate.policy, runtime));
                    bool retained = errors.Count == 0 && KeepsRuntimeStrength(previousRuntimeValidation, runtime);
                    Debug.Log($"[LINK RUNTIME] {candidate.name}, retained={retained}, {runtime.OpeningSummary()}, {runtime.MatchSummary()}");
                    if (retained) runtimeFinalists.Add((candidate.name, candidate.policy, candidate.opening, candidate.match,
                        RuntimeSelectionScore(runtime) - RuntimeSelectionScore(previousRuntimeValidation)));
                    if (errors.Count != 0 || runtime.retries != 0 || runtime.capped != 0) continue;
                    if (KeepsRuntimeSeat(previousRuntimeValidation.First, runtime.First) &&
                        runtime.First.SelectionScore > firstResult.SelectionScore + 1e-6)
                    {
                        firstPolicy = candidate.policy;
                        firstResult = runtime.First;
                    }
                    if (KeepsRuntimeSeat(previousRuntimeValidation.Second, runtime.Second) &&
                        runtime.Second.SelectionScore > secondResult.SelectionScore + 1e-6)
                    {
                        secondPolicy = candidate.policy;
                        secondResult = runtime.Second;
                    }
                }

                if (!ReferenceEquals(firstPolicy, secondPolicy))
                {
                    // Use the included role whenever no measured candidate preserves that role.
                    var splitPolicy = new LinkComboPolicy(firstPolicy.CopyWeights(), secondPolicy.CopyWeights(goingSecond: true));
                    var confirmation = CreateRuntimeOpenings(decks, TrainingDecks, RuntimeValidationPairsPerDeck, 401923);
                    var paired = RunPaired(general, previousPolicy, splitPolicy, confirmation, "validation separate seats");
                    var runtime = paired.Candidate;
                    bool retained = errors.Count == 0 && KeepsRuntimeStrength(paired.Previous, runtime);
                    Debug.Log($"[LINK RUNTIME] Separate seats baseline: {paired.Previous.OpeningSummary()}, {paired.Previous.MatchSummary()}");
                    Debug.Log($"[LINK RUNTIME] Separate seats, retained={retained}, {runtime.OpeningSummary()}, {runtime.MatchSummary()}");
                    if (retained)
                    {
                        var opening = Run(general, splitPolicy, validation, fullDuel: false);
                        var match = Run(general, splitPolicy, fullValidation, fullDuel: true);
                        if (errors.Count == 0 && KeepsLinkStrength(previousValidation, opening) &&
                            KeepsMatchStrength(previousValidationMatches, match))
                            runtimeFinalists.Add(("separate seats", splitPolicy, opening, match,
                                RuntimeSelectionScore(runtime) - RuntimeSelectionScore(paired.Previous)));
                    }
                }
                if (runtimeFinalists.Count == 0)
                {
                    var rescue = RecoverNativeRoles(general, previousPolicy, decks, fastAlternatives, nativeAlternatives,
                        validation, fullValidation, previousValidation, previousValidationMatches, () => errors.Count);
                    if (rescue.policy != null) runtimeFinalists.Add(rescue);
                }
                var best = runtimeFinalists
                    .OrderByDescending(candidate => candidate.improvement)
                    .FirstOrDefault();
                if (best.policy == null)
                    throw new InvalidOperationException("No Link finalist preserved runtime-planner boards, both seat roles and match strength.");
                Debug.Log($"[LINK TRAIN] Selected {best.name} after {trainingOpenings * TrainingDecks.Length} trained openings. Validation {best.opening.OpeningSummary()}, {best.match.MatchSummary()}");

                var test = CreateOpenings(decks, TestDecks, 12, 788353);
                var previous = Run(general, previousPolicy, test, fullDuel: false);
                var trained = Run(general, best.policy, test, fullDuel: false);
                Debug.Log($"[LINK EVAL] Previous {previous.OpeningSummary()}");
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
                var pairedTest = RunPaired(general, previousPolicy, best.policy, runtimeTest, "held-out");
                var previousRuntimeMatches = pairedTest.Previous;
                var trainedRuntimeMatches = pairedTest.Candidate;
                Debug.Log($"[LINK EVAL] Runtime previous {previousRuntimeMatches.OpeningSummary()}, {previousRuntimeMatches.MatchSummary()}");
                Debug.Log($"[LINK EVAL] Runtime trained {trainedRuntimeMatches.OpeningSummary()}, {trainedRuntimeMatches.MatchSummary()}");
                Debug.Log($"[LINK EVAL] Paired wins: {pairedTest.improvedPairs} improved, {pairedTest.regressedPairs} regressed, " +
                    $"p={pairedTest.WinSignificance:G6}. Own turn-end board {previousRuntimeMatches.MeanTurnEndBoard:F4} -> " +
                    $"{trainedRuntimeMatches.MeanTurnEndBoard:F4} ({previousRuntimeMatches.completedTurns}/{trainedRuntimeMatches.completedTurns} turns).");
                Debug.Log($"[LINK TRAIN] Time {elapsed.Elapsed.TotalSeconds:F1}s, native script errors {errors.Count}");
                if (errors.Count > 0) Debug.LogWarning(string.Join("\n", errors.Take(3)));

                if (errors.Count != 0 || !HasCompletedRuntimeHeldout(previousRuntimeMatches) ||
                    !HasCompletedRuntimeHeldout(trainedRuntimeMatches) ||
                    !KeepsRuntimeStrength(previousRuntimeMatches, trainedRuntimeMatches) ||
                    trainedRuntimeMatches.SetupQuality <= previousRuntimeMatches.SetupQuality ||
                    trainedRuntimeMatches.WinPointsTwice <= previousRuntimeMatches.WinPointsTwice ||
                    pairedTest.WinSignificance >= MaximumWinSignificance ||
                    trainedRuntimeMatches.MeanTurnEndBoard <= previousRuntimeMatches.MeanTurnEndBoard)
                    throw new InvalidOperationException("The trained Link policy did not preserve opening strength and significantly improve held-out wins and turn-end boards.");

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

        private static double RuntimeSelectionScore(Results results) =>
            results.SetupQuality + results.WinPointsTwice / (double)Math.Max(1, results.games);

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
            KeepsRuntimeSeat(previous.First, candidate.First) && KeepsRuntimeSeat(previous.Second, candidate.Second);

        private static bool KeepsRuntimeSeat(SeatResults previous, SeatResults candidate) =>
            candidate.games == previous.games && candidate.capped == 0 && candidate.MatchRate >= previous.MatchRate &&
            candidate.linkThree >= previous.linkThree && candidate.linkFour >= previous.linkFour &&
            candidate.interactive >= previous.interactive && candidate.MeanBoard >= previous.MeanBoard && candidate.Mean >= previous.Mean;

        private static bool HasCompletedRuntimeHeldout(Results results) =>
            results.wins + results.losses + results.draws == results.games &&
            results.games >= MinimumRuntimeHeldoutMatches && results.First.games == results.Second.games;

        private static (string name, LinkComboPolicy policy, Results opening, Results match, double improvement) RecoverNativeRoles(
            DuelAiPolicy general, LinkComboPolicy included, Dictionary<int, PresetDeckInfo> decks,
            List<(string name, LinkComboPolicy policy, Results results)> fastAlternatives,
            List<(string name, LinkComboPolicy policy, Results results)> nativeAlternatives,
            Opening[] fastValidation, Opening[] fastMatches, Results includedFast, Results includedMatches, Func<int> errors)
        {
            if (errors() != 0) throw new InvalidOperationException("Native role rescue requires zero preceding script errors.");
            var initial = new[] { included.CopyWeights(), included.CopyWeights(goingSecond: true) };
            var centers = initial.Select(weights => (float[])weights.Clone()).ToArray();
            var directions = new float[2][];
            var advanced = new bool[2];
            SeatResults Seat(Results results, int role) => role == 0 ? results.First : results.Second;
            for (int role = 0; role < 2; role++)
            {
                int selectedRole = role;
                var source = nativeAlternatives.Where(entry => entry.results.retries == 0 && entry.results.capped == 0)
                    .OrderByDescending(entry => Seat(entry.results, selectedRole).SelectionScore)
                    .Concat(fastAlternatives.Where(entry => entry.results.retries == 0 && entry.results.capped == 0)
                        .OrderByDescending(entry => Seat(entry.results, selectedRole).SelectionScore))
                    .FirstOrDefault(entry => !entry.policy.CopyWeights(selectedRole == 1).SequenceEqual(initial[selectedRole]));
                var target = source.policy?.CopyWeights(role == 1) ?? Sample(initial[role], new System.Random(551341 + role), .12f);
                var delta = target.Select((weight, index) => weight - initial[role][index]).ToArray();
                float largest = delta.Max(value => Math.Abs(value));
                directions[role] = largest > 0 ? delta.Select(value => .12f * value / largest).ToArray() : delta;
                Debug.Log($"[LINK RESCUE] Role {role}: direction from {source.name ?? "bounded seeded mutation"}, maximum weight step .12.");
            }

            int played = 0;
            void Charge(int games)
            {
                if (played + games > MaximumNativeRescueGames)
                    throw new InvalidOperationException("Native Link rescue exceeded its extra-game budget.");
                played += games;
            }
            bool Clean(Results result, int count) => result.games == count && result.retries == 0 && result.capped == 0 &&
                result.wins + result.losses + result.draws == count && result.First.games == result.Second.games && result.First.games > 0;
            RescueComparison Compare(LinkComboPolicy reference, LinkComboPolicy candidate, Opening[] fixtures, string name)
            {
                Charge(2 * fixtures.Length);
                var result = RunRescueComparison(general, included, reference, candidate, fixtures, name);
                if (errors() != 0) throw new InvalidOperationException("Native Link rescue produced script errors.");
                Debug.Log($"[LINK RESCUE] {name}: extra games {played}/{MaximumNativeRescueGames}, " +
                    $"included [{result.Previous.MatchSummary()}], candidate [{result.Candidate.MatchSummary()}]");
                return result;
            }
            for (int round = 0; round < NativeRescueRounds; round++)
            {
                var positions = round == 0 ? new[] { 0, 3, 6, 9 } : new[] { 1, 4, 7, 10 };
                var fixtures = CreateRescueOpenings(decks, positions, 542831 + 997 * round, 4 * round);
                var controlPolicy = new LinkComboPolicy(centers[0], centers[1]);
                var control = Compare(controlPolicy, controlPolicy, fixtures, $"round {round + 1} unchanged control");
                if (!Clean(control.Previous, fixtures.Length) || !Clean(control.Candidate, fixtures.Length))
                    throw new InvalidOperationException("Native Link rescue control did not complete clean balanced games.");
                for (int role = 0; role < 2; role++)
                {
                    float step = round == 0 ? 1f : advanced[role] ? .5f : -.5f;
                    var weights = centers[role].Select((weight, index) => Math.Clamp(weight + step * directions[role][index], -8f, 8f)).ToArray();
                    if (weights.SequenceEqual(centers[role])) continue;
                    // Compare only this role's update; its other role and the rival stay fixed.
                    var reference = new LinkComboPolicy(role == 0 ? centers[role] : initial[0], role == 1 ? centers[role] : initial[1]);
                    var candidate = new LinkComboPolicy(role == 0 ? weights : initial[0], role == 1 ? weights : initial[1]);
                    var measured = Compare(reference, candidate, fixtures, $"round {round + 1} role {role}");
                    var before = Seat(measured.Previous, role);
                    var after = Seat(measured.Candidate, role);
                    double noise = Math.Abs(Seat(control.Candidate, role).SelectionScore - Seat(control.Previous, role).SelectionScore);
                    double gain = after.SelectionScore - before.SelectionScore;
                    bool retained = Clean(measured.Previous, fixtures.Length) && Clean(measured.Candidate, fixtures.Length) &&
                        control.unmatchedContexts == 0 && measured.unmatchedContexts == 0 &&
                        KeepsRuntimeSeat(before, after) && gain > noise &&
                        measured.ownFirstChanges[role] > control.ownFirstChanges[role];
                    Debug.Log($"[LINK RESCUE] Round {round + 1}, role {role}, retained={retained}, gain {gain:F6}, " +
                        $"unchanged variation {noise:F6}, owned common-prefix changes " +
                        $"{measured.ownFirstChanges[role]}/{control.ownFirstChanges[role]}, " +
                        $"before [{before.OpeningSummary()}], after [{after.OpeningSummary()}]");
                    if (!retained) continue;
                    centers[role] = weights;
                    advanced[role] = true;
                }
            }
            if (!advanced.Any(value => value)) return default;
            var rescued = new LinkComboPolicy(centers[0], centers[1]);
            var confirmation = CreateRescueOpenings(decks, new[] { 2, 5, 8, 11, 0, 6 }, 542839, 2);
            var paired = Compare(included, rescued, confirmation, "fresh role confirmation");
            bool witnessed = Enumerable.Range(0, 2).All(role => !advanced[role] || paired.ownFirstChanges[role] > 0);
            bool confirmed = Clean(paired.Previous, confirmation.Length) && Clean(paired.Candidate, confirmation.Length) &&
                paired.unmatchedContexts == 0 &&
                KeepsRuntimeStrength(paired.Previous, paired.Candidate) &&
                paired.Candidate.MeanTurnEndBoard >= paired.Previous.MeanTurnEndBoard &&
                RuntimeSelectionScore(paired.Candidate) > RuntimeSelectionScore(paired.Previous) && witnessed;
            Debug.Log($"[LINK RESCUE] Confirmation retained={confirmed}, witnessed={witnessed}, " +
                $"before [{paired.Previous.OpeningSummary()}], after [{paired.Candidate.OpeningSummary()}]");
            if (!confirmed) return default;

            Charge(fastValidation.Length + fastMatches.Length);
            var opening = Run(general, rescued, fastValidation, fullDuel: false);
            var match = Run(general, rescued, fastMatches, fullDuel: true);
            bool fastPreserved = errors() == 0 && KeepsLinkStrength(includedFast, opening) && KeepsMatchStrength(includedMatches, match);
            Debug.Log($"[LINK RESCUE] Fast regression retained={fastPreserved}, {opening.OpeningSummary()}, {match.MatchSummary()}, " +
                $"extra games {played}/{MaximumNativeRescueGames}. Independent held-out games have not been used.");
            return fastPreserved ? ("native role rescue", rescued, opening, match,
                RuntimeSelectionScore(paired.Candidate) - RuntimeSelectionScore(paired.Previous)) : default;
        }

        private static Opening[] CreateRescueOpenings(Dictionary<int, PresetDeckInfo> decks, int[] positions, int seed, int rivalOffset)
        {
            var ids = positions.Select(index => TrainingDecks[index]).Where(decks.ContainsKey).Distinct().ToArray();
            if (ids.Any(id => TestDecks.Contains(id)))
                throw new InvalidOperationException("Role rescue must not use held-out decks.");
            var fixtures = CreateRuntimeOpenings(decks, ids, 1, seed, rivalOffset: rivalOffset);
            if (fixtures.Any(opening => TestDecks.Contains(opening.opponent.id)))
                throw new InvalidOperationException("Role rescue rivals must not use held-out decks.");
            return fixtures;
        }

        private static RescueComparison RunRescueComparison(DuelAiPolicy general, LinkComboPolicy frozenRival,
            LinkComboPolicy previous, LinkComboPolicy candidate, Opening[] openings, string stage)
        {
            var result = new RescueComparison();
            ulong fingerprint = 14695981039346656037UL;
            void Mix(ulong value) => fingerprint = unchecked((fingerprint ^ value) * 1099511628211UL);
            foreach (var opening in openings)
            {
                Mix((uint)opening.deck.id); Mix((uint)opening.opponent.id); Mix(opening.first ? 1UL : 0UL);
                foreach (var cards in new[] { opening.deck.main, opening.deck.extra, opening.opponent.main, opening.opponent.extra })
                {
                    Mix((uint)cards.Length);
                    foreach (uint code in cards) Mix(code);
                }
                foreach (ulong seed in opening.seed) Mix(seed);
            }
            Debug.Log($"[LINK RESCUE] Matched {stage}, fixtures {fingerprint:X16}, {openings.Length} games per arm. " +
                "Both arms use the same deck lists, four-word seeds and roles; rival policy is frozen included.");
            var sample = new Opening[1];
            for (int index = 0; index < openings.Length; index++)
            {
                var opening = openings[index];
                sample[0] = opening;
                var previousTrace = new List<ConsumedResponse>();
                var candidateTrace = new List<ConsumedResponse>();
                bool previousFirst = ((index / 2) % 2 == 0) == opening.first;
                if (previousFirst)
                    Run(general, previous, sample, true, true, frozenRival, aggregate: result.Previous, trace: previousTrace);
                Run(general, candidate, sample, true, true, frozenRival, aggregate: result.Candidate, trace: candidateTrace);
                if (!previousFirst)
                    Run(general, previous, sample, true, true, frozenRival, aggregate: result.Previous, trace: previousTrace);
                int prefix = 0;
                while (prefix < previousTrace.Count && prefix < candidateTrace.Count &&
                    previousTrace[prefix].context == candidateTrace[prefix].context &&
                    previousTrace[prefix].answer == candidateTrace[prefix].answer) prefix++;
                string difference = "identical consumed responses";
                if (prefix < previousTrace.Count && prefix < candidateTrace.Count)
                {
                    var before = previousTrace[prefix];
                    var after = candidateTrace[prefix];
                    if (before.context != after.context) { result.unmatchedContexts++; difference = "context mismatch"; }
                    else if (before.own && after.own)
                    {
                        result.ownFirstChanges[opening.first ? 0 : 1]++;
                        difference = $"candidate response {before.answer} -> {after.answer}, context {before.context}";
                    }
                    else { result.opponentFirstChanges++; difference = "opponent response changed first"; }
                }
                else if (previousTrace.Count != candidateTrace.Count) { result.unmatchedContexts++; difference = "unequal transcript lengths"; }
                Debug.Log($"[LINK RESCUE] {stage}, decks {opening.deck.id}/{opening.opponent.id}, first={opening.first}, " +
                    $"seed [{string.Join(",", opening.seed)}], common prefix {prefix}, {difference}.");
            }
            return result;
        }

        private static IEnumerable<(int source, string name, float[] weights)> ValidationCandidates(float[] initial, List<float[]> finalists)
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
                    yield return (i, $"candidate {i + 1}, residual {fraction:P0}", weights);
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
            int pairsPerDeck, int seed, int minimumMatches = 0, int rivalOffset = 0)
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
                    var opponent = decks[rivals[(rivalOffset + sample++) % rivals.Length]];
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

        private static PairedResults RunPaired(DuelAiPolicy general, LinkComboPolicy previous, LinkComboPolicy candidate,
            Opening[] openings, string stage)
        {
            var results = new PairedResults();
            var sample = new Opening[1];
            var elapsed = Stopwatch.StartNew();
            Debug.Log($"[LINK RUNTIME] Starting matched {stage}: {openings.Length} complete duels per policy.");
            int pairDifference = 0;
            for (int i = 0; i < openings.Length; i++)
            {
                sample[0] = openings[i];
                int before = results.Candidate.WinPointsTwice - results.Previous.WinPointsTwice;
                // Alternate execution order between seeds in each role to balance runtime warm-up.
                bool evenSeedPair = (i / 2) % 2 == 0;
                bool previousFirst = evenSeedPair == sample[0].first;
                if (previousFirst)
                    Run(general, previous, sample, fullDuel: true, runtimePlanning: true, rivalLink: previous, aggregate: results.Previous);
                Run(general, candidate, sample, fullDuel: true, runtimePlanning: true, rivalLink: previous, aggregate: results.Candidate);
                if (!previousFirst)
                    Run(general, previous, sample, fullDuel: true, runtimePlanning: true, rivalLink: previous, aggregate: results.Previous);
                pairDifference += results.Candidate.WinPointsTwice - results.Previous.WinPointsTwice - before;
                if ((i & 1) != 0)
                {
                    if (pairDifference > 0) results.improvedPairs++;
                    if (pairDifference < 0) results.regressedPairs++;
                    pairDifference = 0;
                }
                if ((i + 1) % 8 == 0 || i + 1 == openings.Length)
                    Debug.Log($"[LINK RUNTIME] Matched {stage}: {i + 1}/{openings.Length}, " +
                        $"included {results.Previous.MatchSummary()}, challenger {results.Candidate.MatchSummary()}, " +
                        $"{elapsed.Elapsed.TotalSeconds:F1}s");
            }
            return results;
        }

        private static Results Run(DuelAiPolicy general, LinkComboPolicy link, Opening[] openings, bool fullDuel,
            bool runtimePlanning = false, LinkComboPolicy rivalLink = null, string stage = null, Results aggregate = null,
            List<ConsumedResponse> trace = null)
        {
            if (runtimePlanning && (!fullDuel || rivalLink == null))
                throw new InvalidOperationException("Runtime Link evaluation needs full duels and a frozen rival Link policy.");
            var results = aggregate ?? new Results();
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
                for (int step = 0; step < limit && !duel.IsFinished && duel.RetryCount == 0; step++)
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
                    var ai = prompt.playerId == seat ? candidate : opponent;
                    if (runtimePlanning)
                        while (!ai.AdvanceComboPlanning(duel, prompt)) { }
                    byte[] response = ai.ComputeResponse(duel, prompt);
                    if (trace != null)
                        trace.Add(new ConsumedResponse
                        {
                            context = $"{duel.TurnNumber}/{duel.CurrentPhase}/{prompt.playerId}/{prompt.messageType}/" +
                                $"{prompt.SelectionHint}/{prompt.promptCardCode}/{prompt.promptDesc}",
                            answer = BitConverter.ToString(response), own = prompt.playerId == seat,
                        });
                    // Going-second openings include Battle and Main Phase 2, so a finisher earns its actual damage.
                    int turn = duel.TurnNumber;
                    int turnPlayer = duel.TurnPlayer;
                    duel.SubmitResponse(response);
                    if (runtimePlanning && turnPlayer == seat && (duel.IsFinished || duel.TurnNumber > turn))
                    {
                        results.turnEndBoard += ComboSearch.BoardValue(duel, (byte)seat, canBattle: false);
                        results.completedTurns++;
                    }
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
