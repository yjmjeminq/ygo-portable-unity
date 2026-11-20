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
    internal static class DuelAiTraining
    {
        private const int Generations = 3;
        private const int PopulationSize = 10;
        private const int TrainingPairs = 19;
        private const int ValidationPairs = 64;
        private const int TestPairs = 120;
        private const int StyleObservationTurns = 6;
        private const int MaxPlannerCandidates = 4;
        private const int MaxRecoveryCandidates = 2;
        private const int RecoveryGamesPerStyleSeat = 4;
        private const int MaxRecoveryGames = 192;
        private const double MaximumWinSignificance = .05;

        private static readonly int[] TrainingDeckIds =
        {
            51045, 51049, 50947, 50949, 50819, 51039, 51195, 51199, 51379, 51175, 51179, 51099,
            51369, 50355, 50359, 50165, 50169, 50019, 50109, 50199, 50039, 51749, 51689, 51569,
            51589, 50725, 50729, 50439, 50719, 50689, 50699, 50619, 51435, 51439, 51899, 51879,
            20670, 20671, 20672, 20674, 20675, 20678, 20684, 20686,
            20673, 50962, 51028, 52150,
        };
        private static readonly int[] TestDeckIds =
        {
            51057, 50847, 51029, 51079, 51209, 50369, 51609, 51729, 51469, 50749, 51409, 51779,
            82024, 83054, 50474, 50404, 51824, 51854, 20677, 20679, 20680, 20682, 20687, 20689,
            50961, 50964, 52151,
        };

        private sealed class Fixture
        {
            internal PresetDeckInfo first;
            internal PresetDeckInfo second;
            internal ulong[] seed;
        }

        private sealed class Results
        {
            internal int wins, losses, draws, retries, capped, measuredTurns;
            internal double setup, breakthrough, control, lockProgress, resource;
            internal double developmentSetup, developmentBreakthrough;
            internal double lockSurvival, lockKeys, lockDraws;
            internal double totalTurnEndBoard;
            internal readonly int[] styleGames = new int[DuelDeckStrategy.StyleCount];
            internal readonly double[] stylePoints = new double[DuelDeckStrategy.StyleCount];
            internal readonly int[,] styleSeatGames = new int[DuelDeckStrategy.StyleCount, 2];
            internal readonly double[,] styleSeatPoints = new double[DuelDeckStrategy.StyleCount, 2];
            internal bool HasStyleCoverage => Enumerable.Range(0, DuelDeckStrategy.StyleCount)
                .All(style => styleSeatGames[style, 0] > 0 && styleSeatGames[style, 1] > 0);
            internal int Games => wins + losses + draws;
            internal int WinPointsTwice => 2 * wins + draws;
            internal double MeanTurnEndBoard => totalTurnEndBoard / Math.Max(1, measuredTurns);
            internal double Rate => (wins + .5 * draws) / Games;
            internal double Objective => Rate + (.10 * setup + .12 * breakthrough + .12 * control + .12 * lockProgress +
                .12 * resource - .5 * retries - .12 * capped) / Games;
            public override string ToString() => $"{wins}W {losses}L {draws}D in {Games} games ({Rate:P1}), setup {2 * setup / Games:F2}, " +
                $"breakthrough {2 * breakthrough / Games:F2}, control {control / Games:F2}, lock {lockProgress / Games:F2}, resource {resource / Games:F2}, " +
                $"{retries} retries, {capped} capped";

            internal string StyleSummary() => string.Join(", ", Enum.GetValues(typeof(DuelDeckStyle)).Cast<DuelDeckStyle>()
                .Select(style => $"{style} {stylePoints[(int)style]:0.0}/{styleGames[(int)style]}"));

            internal string CoverageSummary() => string.Join(", ", Enum.GetValues(typeof(DuelDeckStyle)).Cast<DuelDeckStyle>()
                .Select(style => $"{style} first {styleSeatGames[(int)style, 0]}, second {styleSeatGames[(int)style, 1]}"));

            internal double StyleObjective(DuelDeckStyle style)
            {
                int index = (int)style;
                double score = style switch
                {
                    DuelDeckStyle.Lock => .35 * stylePoints[index] + lockProgress,
                    DuelDeckStyle.Control => stylePoints[index] + .12 * control,
                    DuelDeckStyle.Resource => stylePoints[index] + .22 * resource,
                    _ => stylePoints[index],
                };
                return score / Math.Max(1, styleGames[index]);
            }

            internal bool PreservesLockResults(Results reference)
            {
                return PreservesStyleWins(reference, DuelDeckStyle.Lock) &&
                    lockSurvival >= reference.lockSurvival && lockKeys >= reference.lockKeys && lockDraws >= reference.lockDraws;
            }

            internal bool PreservesControlResults(Results reference) =>
                PreservesStyleWins(reference, DuelDeckStyle.Control) && control >= reference.control;

            internal bool PreservesResourceResults(Results reference) =>
                PreservesStyleWins(reference, DuelDeckStyle.Resource) && resource >= reference.resource;

            internal bool PreservesDevelopmentResults(Results reference) =>
                PreservesStyleWins(reference, DuelDeckStyle.Development) &&
                developmentSetup >= reference.developmentSetup && developmentBreakthrough >= reference.developmentBreakthrough;

            private bool PreservesStyleWins(Results reference, DuelDeckStyle style)
            {
                int index = (int)style;
                return styleGames[index] == reference.styleGames[index] && stylePoints[index] >= reference.stylePoints[index];
            }

            internal string LockSummary()
            {
                int games = Math.Max(1, styleGames[(int)DuelDeckStyle.Lock]);
                return $"survival {lockSurvival / games:F3}, retained keys {lockKeys / games:F3}, effect draws {lockDraws / games:F3}";
            }
        }

        // Unity batch entry point. Self-play uses preset decks and synthetic shuffle seeds only.
        public static void Train()
        {
            int exitCode = 0;
            bool initialize = !YgoNativeBridge.IsInitialized;
            try
            {
                string streaming = Application.streamingAssetsPath;
                int scriptErrors = 0;
                if (initialize && YgoNativeBridge.Initialize(streaming, (type, _) => { if (type == 0) scriptErrors++; }) != 0)
                    throw new InvalidOperationException("Could not initialize the duel core for training.");
                CardDatabase.Load(streaming);
                DeckRepository.Load(streaming, Path.Combine(Path.GetTempPath(), "ygo_ai_training"), seedDefaultDecks: false);
                var available = DeckRepository.Characters.SelectMany(character => OpponentDeckLevels.GetDecks(character.id))
                    .Where(deck => deck.main?.Length >= 40 && deck.extra != null).GroupBy(deck => deck.id)
                    .ToDictionary(group => group.Key, group => group.First());
                var trainingDecks = TrainingDeckIds.Where(id => available.ContainsKey(id)).Select(id => available[id]).ToArray();
                var testDecks = TestDeckIds.Where(id => available.ContainsKey(id)).Select(id => available[id]).ToArray();
                var elapsed = Stopwatch.StartNew();
                var random = new System.Random(38407);
                var existingPolicy = DuelAiPolicy.LoadIncluded();
                float[] initial = existingPolicy?.CopyWeights() ?? DuelAiPolicy.InitialWeights();
                var validation = CreateFixtures(trainingDecks, ValidationPairs, 483172);
                var fastSpecialists = new List<(string name, DuelAiPolicy policy, Results results)>();
                var main = TrainDecisions(initial, DuelAiPolicy.Decisions.MainPhase, trainingDecks, validation, existingPolicy, random, elapsed, fastSpecialists);
                var chains = TrainDecisions(initial, DuelAiPolicy.Decisions.Chains, trainingDecks, validation, existingPolicy, random, elapsed, fastSpecialists);
                var combinedWeights = (float[])main.weights.Clone();
                int chainStart = DuelAiPolicy.FirstChainFeature;
                Array.Copy(chains.weights, chainStart, combinedWeights, chainStart, combinedWeights.Length - chainStart);
                var combined = new DuelAiPolicy(combinedWeights, DuelAiPolicy.Decisions.Both);
                var candidatesList = new List<(DuelAiPolicy policy, Results results)>
                {
                    (main.policy, main.results),
                    (chains.policy, chains.results),
                    (combined, Evaluate(combined, validation, existingPolicy)),
                };
                var individualStyles = new List<(string name, DuelAiPolicy policy)>();
                var stylePolicy = existingPolicy;
                Results includedFastValidation = null;
                if (existingPolicy != null)
                {
                    var previousResult = Evaluate(existingPolicy, validation, existingPolicy);
                    includedFastValidation = previousResult;
                    candidatesList.Add((existingPolicy, previousResult));
                    foreach (var style in new[] { DuelDeckStyle.Control, DuelDeckStyle.Lock, DuelDeckStyle.Resource })
                    {
                        var specialist = candidatesList.Where(entry => style != DuelDeckStyle.Lock || entry.results.PreservesLockResults(previousResult))
                            .OrderByDescending(entry => entry.results.StyleObjective(style)).First();
                        if (specialist.results.StyleObjective(style) <= previousResult.StyleObjective(style)) continue;
                        stylePolicy = stylePolicy.WithStyle(style, specialist.policy);
                        individualStyles.Add(($"{style} overlay from {specialist.policy.EnabledDecisions}",
                            existingPolicy.WithStyle(style, specialist.policy)));
                        Debug.Log($"[AI TRAIN] {style} profile from {specialist.policy.EnabledDecisions}. " +
                            $"Validation {previousResult.StyleObjective(style):F3} -> {specialist.results.StyleObjective(style):F3}");
                    }
                    if (!ReferenceEquals(stylePolicy, existingPolicy))
                        candidatesList.Add((stylePolicy, Evaluate(stylePolicy, validation, existingPolicy)));
                }
                var linkPolicy = LinkComboPolicy.LoadIncluded() ?? throw new InvalidOperationException("Missing the Link AI model.");
                var plannerFixtures = CreateFixtures(trainingDecks, 18, 782449, alternatePrimarySeat: true);
                var plannerIncluded = Evaluate(existingPolicy, plannerFixtures, existingPolicy, linkPolicy, "planner included self-play");
                Debug.Log($"[AI EVAL] Planner included policy: {plannerIncluded.StyleSummary()}, {plannerIncluded.LockSummary()}");
                Debug.Log($"[AI EVAL] Planner coverage: {plannerIncluded.CoverageSummary()}");
                if (!plannerIncluded.HasStyleCoverage || plannerIncluded.retries != 0 || plannerIncluded.capped != 0 || scriptErrors != 0)
                    throw new InvalidOperationException("Runtime-planner validation requires clean included results covering both seats of all four styles.");
                var shortlist = new List<(string name, DuelAiPolicy policy)>();
                var seenPolicies = new HashSet<string>(StringComparer.Ordinal);
                if (existingPolicy != null) seenPolicies.Add(existingPolicy.ToJson());
                void AddPlannerCandidate(string name, DuelAiPolicy policy)
                {
                    if (policy != null && shortlist.Count < MaxPlannerCandidates && seenPolicies.Add(policy.ToJson()))
                        shortlist.Add((name, policy));
                }
                // Reserve room for isolated profiles, so an aggregate ranking cannot discard their gains.
                var bestGlobal = candidatesList.Where(entry => !ReferenceEquals(entry.policy, existingPolicy) &&
                    !ReferenceEquals(entry.policy, stylePolicy)).OrderByDescending(entry => entry.results.Objective).First();
                AddPlannerCandidate($"global {bestGlobal.policy.EnabledDecisions}", bestGlobal.policy);
                foreach (var overlay in individualStyles) AddPlannerCandidate(overlay.name, overlay.policy);
                if (!ReferenceEquals(stylePolicy, existingPolicy)) AddPlannerCandidate("combined style profiles", stylePolicy);
                foreach (var entry in candidatesList.OrderByDescending(entry => entry.results.Objective))
                    AddPlannerCandidate($"global {entry.policy.EnabledDecisions}", entry.policy);
                var retained = new List<(string name, DuelAiPolicy policy, Results results)>();
                void ScreenPlannerCandidate(string name, DuelAiPolicy policy)
                {
                    var result = Evaluate(policy, plannerFixtures, existingPolicy, linkPolicy, $"planner {name}");
                    var changedStyles = Enum.GetValues(typeof(DuelDeckStyle)).Cast<DuelDeckStyle>()
                        .Where(style => ChangesDecisions(policy, existingPolicy, style)).ToHashSet();
                    // This shortlist can include a single-style overlay. The independent held-out duels still check every style.
                    bool development = !changedStyles.Contains(DuelDeckStyle.Development) || result.PreservesDevelopmentResults(plannerIncluded);
                    bool control = !changedStyles.Contains(DuelDeckStyle.Control) || result.PreservesControlResults(plannerIncluded);
                    bool locked = !changedStyles.Contains(DuelDeckStyle.Lock) || result.PreservesLockResults(plannerIncluded);
                    bool resource = !changedStyles.Contains(DuelDeckStyle.Resource) || result.PreservesResourceResults(plannerIncluded);
                    // Equivalent policies can make different choices when a wall-clock search stops at a different branch.
                    double screenedRate = plannerIncluded.stylePoints.Select((points, index) =>
                        changedStyles.Contains((DuelDeckStyle)index) ? result.stylePoints[index] : points).Sum() / result.Games;
                    bool accepted = changedStyles.Count > 0 && result.HasStyleCoverage && screenedRate >= .50 &&
                        scriptErrors == 0 && result.retries == 0 && result.capped == 0 &&
                        development && control && locked && resource;
                    Debug.Log($"[AI EVAL] Planner candidate {name}: retained={accepted}, {result}, " +
                        $"{result.StyleSummary()}, changed styles {string.Join(", ", changedStyles)}, screening rate {screenedRate:P1}, " +
                        $"Development={development}, Control={control}, Lock={locked}, Resource={resource}; " +
                        $"development setup {result.developmentSetup:F6}/{plannerIncluded.developmentSetup:F6}, " +
                        $"breakthrough {result.developmentBreakthrough:F6}/{plannerIncluded.developmentBreakthrough:F6}; " +
                        $"control {result.control:F6}/{plannerIncluded.control:F6}, resource {result.resource:F6}/{plannerIncluded.resource:F6}; " +
                        $"Lock {result.LockSummary()} vs {plannerIncluded.LockSummary()}");
                    if (accepted) retained.Add((name, policy, result));
                }
                foreach (var entry in shortlist) ScreenPlannerCandidate(entry.name, entry.policy);
                if (scriptErrors != 0)
                    throw new InvalidOperationException("Runtime-planner candidates produced native script errors.");
                if (retained.Count == 0 && existingPolicy != null)
                {
                    var recoverySources = fastSpecialists.Concat(candidatesList.Select(entry =>
                        (name: $"global {entry.policy.EnabledDecisions}", entry.policy, entry.results))).ToArray();
                    var recovery = RecoverBalancedSpecialist(existingPolicy, linkPolicy, trainingDecks,
                        includedFastValidation, recoverySources, () => scriptErrors);
                    if (recovery.policy != null) retained.Add(recovery);
                }
                if (retained.Count == 0)
                    throw new InvalidOperationException("No candidate passed runtime-planner validation.");
                var best = retained.OrderByDescending(entry => entry.results.Objective).First();
                const int trainingGames = Generations * PopulationSize * TrainingPairs * 4;
                Debug.Log($"[AI TRAIN] Selected {best.name} after {trainingGames} games with {trainingDecks.Length} training decks. Planner validation against the included policy {best.results}");

                var test = CreateFixtures(testDecks, TestPairs, 559236);
                var (included, versusPrevious, winSignificance) = EvaluateHeldout(existingPolicy, best.policy, linkPolicy, test);
                if (scriptErrors != 0 || !included.HasStyleCoverage || included.retries != 0 || included.capped != 0 ||
                    included.Games != 2 * test.Length)
                    throw new InvalidOperationException("Held-out self-play requires clean included results covering both seats of every style.");
                bool developmentPreserved = versusPrevious.PreservesDevelopmentResults(included);
                bool controlPreserved = versusPrevious.PreservesControlResults(included);
                bool lockPreserved = versusPrevious.PreservesLockResults(included);
                bool resourcePreserved = versusPrevious.PreservesResourceResults(included);
                Debug.Log($"[AI EVAL] Held-out included policy {included.StyleSummary()}, {included.LockSummary()}");
                Debug.Log($"[AI EVAL] Held-out coverage: {included.CoverageSummary()}");
                Debug.Log($"[AI EVAL] Held-out vs previous AI {versusPrevious}");
                Debug.Log($"[AI EVAL] Held-out style results {versusPrevious.StyleSummary()}");
                Debug.Log($"[AI EVAL] Held-out Lock {versusPrevious.LockSummary()}");
                Debug.Log($"[AI EVAL] Held-out preservation with the runtime planner: Development={developmentPreserved}, Control={controlPreserved}, Lock={lockPreserved}, Resource={resourcePreserved}");
                Debug.Log($"[AI EVAL] Matched held-out win points {included.WinPointsTwice / 2.0:G} -> {versusPrevious.WinPointsTwice / 2.0:G}, " +
                    $"p={winSignificance:G6}, own turn-end board {included.MeanTurnEndBoard:F4} -> {versusPrevious.MeanTurnEndBoard:F4} " +
                    $"({included.measuredTurns}/{versusPrevious.measuredTurns} turns).");
                if (scriptErrors != 0 || !versusPrevious.HasStyleCoverage ||
                    versusPrevious.Rate < .50 || versusPrevious.retries != 0 ||
                    versusPrevious.capped != 0 || versusPrevious.Games != included.Games ||
                    versusPrevious.WinPointsTwice <= included.WinPointsTwice || winSignificance >= MaximumWinSignificance ||
                    versusPrevious.MeanTurnEndBoard <= included.MeanTurnEndBoard ||
                    !developmentPreserved || !controlPreserved || !lockPreserved || !resourcePreserved)
                    throw new InvalidOperationException($"The selected policy did not meet the held-out strength check. Native script errors: {scriptErrors}.");

                var versusRules = Evaluate(best.policy, test, null, linkPolicy, "candidate vs rules-only");
                var rulesVersusPrevious = Evaluate(null, test, existingPolicy, linkPolicy, "rules-only vs included");
                Debug.Log($"[AI EVAL] Held-out vs rules-only AI {versusRules}");
                Debug.Log($"[AI EVAL] Held-out rules-only vs previous AI {rulesVersusPrevious}");
                Debug.Log($"[AI TRAIN] Time {elapsed.Elapsed.TotalSeconds:F1}s, native script errors {scriptErrors}");
                if (scriptErrors != 0 || versusRules.Rate < .45 || versusRules.retries != 0 || versusRules.capped != 0)
                    throw new InvalidOperationException("The selected policy did not meet the held-out strength check.");

                string assetPath = $"Assets/Resources/{DuelAiPolicy.ResourceName}.json";
                File.WriteAllText(Path.Combine(Application.dataPath, "Resources", DuelAiPolicy.ResourceName + ".json"), best.policy.ToJson() + "\n");
                AssetDatabase.ImportAsset(assetPath);
                Debug.Log($"[AI TRAIN SUCCEEDED] Wrote {assetPath}");
            }
            catch (Exception exception)
            {
                exitCode = 1;
                Debug.LogError($"[AI TRAIN FAILED] {exception}");
                if (!Application.isBatchMode) throw;
            }
            finally
            {
                if (initialize) YgoNativeBridge.Shutdown();
            }
            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        private static bool ChangesDecisions(DuelAiPolicy candidate, DuelAiPolicy reference, DuelDeckStyle style)
        {
            var test = candidate.ForStyle(style);
            var current = reference?.ForStyle(style);
            return !ReferenceEquals(test, current) && (current == null || test.EnabledDecisions != current.EnabledDecisions ||
                !test.CopyWeights().SequenceEqual(current.CopyWeights()));
        }

        private static bool PreservesStyleResults(Results candidate, Results reference, DuelDeckStyle style) => style switch
        {
            DuelDeckStyle.Development => candidate.PreservesDevelopmentResults(reference),
            DuelDeckStyle.Control => candidate.PreservesControlResults(reference),
            DuelDeckStyle.Lock => candidate.PreservesLockResults(reference),
            DuelDeckStyle.Resource => candidate.PreservesResourceResults(reference),
            _ => false,
        };

        private static bool PreservesRecoveryStyle(Results candidate, Results reference, DuelDeckStyle style) =>
            PreservesStyleResults(candidate, reference, style) && Enumerable.Range(0, 2).All(seat =>
                candidate.styleSeatGames[(int)style, seat] == reference.styleSeatGames[(int)style, seat] &&
                candidate.styleSeatPoints[(int)style, seat] >= reference.styleSeatPoints[(int)style, seat]);

        private static (string name, DuelAiPolicy policy, Results results) RecoverBalancedSpecialist(DuelAiPolicy included,
            LinkComboPolicy links, PresetDeckInfo[] decks, Results fastReference,
            (string name, DuelAiPolicy policy, Results results)[] sources, Func<int> scriptErrors)
        {
            var styles = Enum.GetValues(typeof(DuelDeckStyle)).Cast<DuelDeckStyle>().ToArray();
            var proposals = new List<(string name, DuelAiPolicy policy, DuelDeckStyle style)>();
            foreach (var style in new[] { DuelDeckStyle.Control, DuelDeckStyle.Resource })
            {
                var source = sources.Where(entry => entry.results.retries == 0 && entry.results.capped == 0 &&
                        ChangesDecisions(entry.policy, included, style))
                    .OrderByDescending(entry => entry.results.StyleObjective(style)).ThenByDescending(entry => entry.results.Objective)
                    .FirstOrDefault();
                if (source.policy == null || source.results.StyleObjective(style) <= fastReference.StyleObjective(style)) continue;
                var policy = included.WithStyle(style, source.policy);
                if (styles.Any(other => other != style && ChangesDecisions(policy, included, other)))
                    throw new InvalidOperationException("A recovery overlay changed an unrelated style.");
                proposals.Add(($"balanced {style} from {source.name}", policy, style));
                Debug.Log($"[AI RECOVERY] Proposed {style} from fast validation {source.results.StyleObjective(style):F6} " +
                    $"vs included {fastReference.StyleObjective(style):F6}; other profiles remain included.");
            }
            if (proposals.Count == 0) return default;
            if (proposals.Count > MaxRecoveryCandidates)
                throw new InvalidOperationException("Too many recovery proposals.");

            int played = 0;
            Results Play(DuelAiPolicy policy, Fixture[] fixtures, string stage)
            {
                int games = 2 * fixtures.Length;
                if (played + games > MaxRecoveryGames)
                    throw new InvalidOperationException("Recovery exceeded its native game budget.");
                played += games;
                var result = Evaluate(policy, fixtures, included, links, $"recovery {stage}");
                if (scriptErrors() != 0) throw new InvalidOperationException("Recovery produced native script errors.");
                Debug.Log($"[AI RECOVERY] {stage}: {result}, {result.StyleSummary()}, " +
                    $"coverage [{result.CoverageSummary()}], native games {played}/{MaxRecoveryGames}");
                return result;
            }
            bool Clean(Results result) => result.retries == 0 && result.capped == 0 &&
                result.Games == 2 * styles.Length * RecoveryGamesPerStyleSeat &&
                styles.All(style => Enumerable.Range(0, 2).All(seat =>
                    result.styleSeatGames[(int)style, seat] == RecoveryGamesPerStyleSeat));

            var screening = CreateBalancedRecoveryFixtures(decks, 780353);
            var firstControl = Play(included, screening, "included A1");
            if (!Clean(firstControl)) throw new InvalidOperationException("The first recovery control did not complete clean balanced duels.");
            var measured = proposals.Select(proposal => (proposal, results: Play(proposal.policy, screening, proposal.name))).ToArray();
            var repeatControl = Play(included, screening, "included A2 unchanged repeat");
            if (!Clean(repeatControl)) throw new InvalidOperationException("The repeated recovery control did not complete clean balanced duels.");
            var candidates = new List<(string name, DuelAiPolicy policy, DuelDeckStyle style, double gain)>();
            foreach (var entry in measured)
            {
                var style = entry.proposal.style;
                double a1 = firstControl.StyleObjective(style), a2 = repeatControl.StyleObjective(style);
                double variation = Math.Abs(a2 - a1);
                double gain = entry.results.StyleObjective(style) - Math.Max(a1, a2);
                bool accepted = Clean(entry.results) && PreservesRecoveryStyle(entry.results, firstControl, style) &&
                    PreservesRecoveryStyle(entry.results, repeatControl, style) && gain > variation;
                Debug.Log($"[AI RECOVERY] Screen {entry.proposal.name}: retained={accepted}, " +
                    $"style gain over both controls {gain:F6}, unchanged variation {variation:F6}, " +
                    $"actual overall rate {entry.results.Rate:P1}. Unchanged-style outcomes are not substituted.");
                if (accepted) candidates.Add((entry.proposal.name, entry.proposal.policy, style, gain - variation));
            }
            if (candidates.Count == 0) return default;
            var best = candidates.OrderByDescending(entry => entry.gain).First();
            // Only this fixed candidate reaches a separate confirmation; neither cohort contains held-out decks or seeds.
            var confirmation = CreateBalancedRecoveryFixtures(decks, 780857);
            var confirmationControl = Play(included, confirmation, "included B fresh confirmation");
            if (!Clean(confirmationControl)) throw new InvalidOperationException("The recovery confirmation control did not complete clean balanced duels.");
            var confirmed = Play(best.policy, confirmation, $"{best.name} B fresh confirmation");
            double confirmationGain = confirmed.StyleObjective(best.style) - confirmationControl.StyleObjective(best.style);
            bool confirmedStrength = Clean(confirmed) && PreservesRecoveryStyle(confirmed, confirmationControl, best.style) &&
                confirmationGain > 0;
            Debug.Log($"[AI RECOVERY] Confirmation {best.name}: retained={confirmedStrength}, " +
                $"style gain {confirmationGain:F6}, actual overall rate {confirmed.Rate:P1}, games {played}/{MaxRecoveryGames}.");
            return confirmedStrength ? (best.name, best.policy, confirmed) : default;
        }

        private static (float[] weights, DuelAiPolicy policy, Results results) TrainDecisions(float[] initial,
            DuelAiPolicy.Decisions decisions, PresetDeckInfo[] decks, Fixture[] validation,
            DuelAiPolicy previousPolicy, System.Random random, Stopwatch elapsed,
            List<(string name, DuelAiPolicy policy, Results results)> specialists)
        {
            bool chains = decisions == DuelAiPolicy.Decisions.Chains;
            int first = chains ? DuelAiPolicy.FirstChainFeature : 0;
            int end = chains ? initial.Length : DuelAiPolicy.FirstChainFeature;
            float[] center = (float[])initial.Clone();
            var deviations = center.Select(_ => .7f).ToArray();
            var finalists = new List<float[]> { center };

            // Learn each decision policy from match rewards so changes in the other policy cannot mask its effect.
            for (int generation = 0; generation < Generations; generation++)
            {
                var fixtures = CreateFixtures(decks, TrainingPairs, 92400 + 57 * (int)decisions + generation);
                var candidates = new float[PopulationSize][];
                candidates[0] = center;
                candidates[1] = finalists[^1];
                for (int i = 2; i < candidates.Length; i++)
                    candidates[i] = Sample(center, deviations, random, first, end);
                var ranked = candidates.Select(weights => (weights, results: Evaluate(new DuelAiPolicy(weights, decisions), fixtures, previousPolicy)))
                    .OrderByDescending(entry => entry.results.Objective).ToArray();
                finalists.Add(ranked[0].weights);
                Debug.Log($"[AI TRAIN] {decisions} {generation + 1}/{Generations} best {ranked[0].results} at {elapsed.Elapsed.TotalSeconds:F0}s");
                var elite = ranked.Take(4).Select(entry => entry.weights).ToArray();
                var means = (float[])center.Clone();
                for (int feature = first; feature < end; feature++)
                {
                    float mean = elite.Average(weights => weights[feature]);
                    means[feature] = mean;
                    float deviation = (float)Math.Sqrt(elite.Average(weights => (weights[feature] - mean) * (weights[feature] - mean)));
                    deviations[feature] = Math.Clamp(.65f * deviation + .35f * deviations[feature], .18f, 1f);
                }
                center = means;
            }

            finalists.Add(center);
            var evaluated = finalists.Where(weights => !weights.SequenceEqual(initial))
                .Select(weights => (weights, policy: new DuelAiPolicy(weights, decisions)))
                .Select(entry => (entry.weights, entry.policy, results: Evaluate(entry.policy, validation, previousPolicy)))
                .OrderByDescending(entry => entry.results.Objective).ToArray();
            var best = evaluated.First();
            foreach (var style in new[] { DuelDeckStyle.Control, DuelDeckStyle.Resource })
            {
                var specialist = evaluated.OrderByDescending(entry => entry.results.StyleObjective(style)).First();
                specialists.Add(($"{decisions} {style} finalist", specialist.policy, specialist.results));
                Debug.Log($"[AI TRAIN] Retained fast {decisions} {style} alternative: " +
                    $"style objective {specialist.results.StyleObjective(style):F6}, global {specialist.results.Objective:F6}.");
            }
            Debug.Log($"[AI TRAIN] {decisions} validation {best.results}");
            return best;
        }

        private static Fixture[] CreateFixtures(PresetDeckInfo[] decks, int count, int shuffleSeed, bool alternatePrimarySeat = false)
        {
            var random = new System.Random(shuffleSeed);
            var orderedDecks = decks.OrderBy(_ => random.Next()).ToArray();
            var styles = orderedDecks.GroupBy(deck => DuelDeckStrategy.Classify(deck.main, deck.extra.Length))
                .Select(group => group.ToArray()).ToArray();
            var fixtures = new Fixture[count];
            for (int i = 0; i < count; i++)
            {
                var cohort = styles[i % styles.Length];
                var first = cohort[i / styles.Length % cohort.Length];
                var second = i % 4 == 0 ? first : orderedDecks[random.Next(orderedDecks.Length)];
                // Alternate each cohort's primary deck across seats, rather than pinning a style to turn one.
                if (alternatePrimarySeat && (i / styles.Length % 2) != 0) (first, second) = (second, first);
                fixtures[i] = new Fixture
                {
                    first = first,
                    second = second,
                    seed = Enumerable.Range(0, 4).Select(_ => (ulong)random.Next(1, int.MaxValue)).ToArray(),
                };
            }
            return fixtures;
        }

        private static Fixture[] CreateBalancedRecoveryFixtures(PresetDeckInfo[] decks, int shuffleSeed)
        {
            var styles = Enum.GetValues(typeof(DuelDeckStyle)).Cast<DuelDeckStyle>().ToArray();
            var cohorts = decks.OrderBy(deck => deck.id)
                .GroupBy(deck => DuelDeckStrategy.Classify(deck.main, deck.extra.Length))
                .ToDictionary(group => group.Key, group => group.ToArray());
            if (styles.Length < 2 || styles.Any(style => !cohorts.ContainsKey(style)))
                throw new InvalidOperationException("Recovery requires training decks from every style.");
            var random = new System.Random(shuffleSeed);
            var fixtures = new List<Fixture>();
            for (int sample = 0; sample < RecoveryGamesPerStyleSeat; sample++)
                for (int index = 0; index < styles.Length; index++)
                {
                    var first = cohorts[styles[index]];
                    var second = cohorts[styles[(index + 1 + sample % (styles.Length - 1)) % styles.Length]];
                    fixtures.Add(new Fixture
                    {
                        first = first[sample % first.Length],
                        second = second[(sample + index) % second.Length],
                        seed = Enumerable.Range(0, 4).Select(_ => (ulong)random.Next(1, int.MaxValue)).ToArray(),
                    });
                }
            return fixtures.ToArray();
        }

        private static float[] Sample(float[] center, float[] deviations, System.Random random, int first, int end)
        {
            var candidate = (float[])center.Clone();
            for (int i = first; i < end; i++)
            {
                double normal = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
                candidate[i] = Math.Clamp(center[i] + deviations[i] * (float)normal, -4f, 4f);
            }
            return candidate;
        }

        private static (Results included, Results candidate, double winSignificance) EvaluateHeldout(DuelAiPolicy reference,
            DuelAiPolicy challenger, LinkComboPolicy links, Fixture[] fixtures)
        {
            var included = new Results();
            var candidate = new Results();
            var sample = new Fixture[1];
            var clock = Stopwatch.StartNew();
            int improved = 0, regressed = 0;
            Debug.Log($"[AI EVAL] Starting matched held-out: {fixtures.Length} seat-swapped seeds per policy.");
            for (int i = 0; i < fixtures.Length; i++)
            {
                sample[0] = fixtures[i];
                int previousGap = candidate.WinPointsTwice - included.WinPointsTwice;
                bool includedFirst = (i & 1) == 0;
                if (includedFirst) Evaluate(reference, sample, reference, links, aggregate: included);
                Evaluate(challenger, sample, reference, links, aggregate: candidate);
                if (!includedFirst) Evaluate(reference, sample, reference, links, aggregate: included);
                int gain = candidate.WinPointsTwice - included.WinPointsTwice - previousGap;
                if (gain > 0) improved++;
                if (gain < 0) regressed++;
                if ((i + 1) % 8 == 0 || i + 1 == fixtures.Length)
                    Debug.Log($"[AI EVAL] Matched held-out {i + 1}/{fixtures.Length}: included {included}; " +
                        $"candidate {candidate}, {clock.Elapsed.TotalSeconds:F1}s");
            }
            Debug.Log($"[AI EVAL] Held-out paired seeds: {improved} improved, {regressed} regressed.");
            return (included, candidate, WinImprovementProbability(improved, regressed));
        }

        internal static double WinImprovementProbability(int improved, int regressed)
        {
            int trials = improved + regressed;
            double term = Math.Pow(.5, trials);
            double probability = 0;
            for (int wins = 0; wins <= trials; wins++)
            {
                if (wins >= improved) probability += term;
                term *= (trials - wins) / (double)(wins + 1);
            }
            return Math.Min(1, probability);
        }

        private static Results Evaluate(DuelAiPolicy policy, Fixture[] fixtures, DuelAiPolicy referencePolicy,
            LinkComboPolicy links = null, string stage = null, Results aggregate = null)
        {
            var result = aggregate ?? new Results();
            var clock = stage == null ? null : Stopwatch.StartNew();
            int completed = 0;
            if (clock != null) Debug.Log($"[AI EVAL] Starting {stage}: {fixtures.Length} paired fixtures.");
            foreach (var match in fixtures)
            {
                // The same shuffled seats are played with the candidate on each side to cancel deck and turn-order bias.
                for (int candidateSeat = 0; candidateSeat < 2; candidateSeat++)
                {
                    using var duel = new DuelSession(recordPlanningReplay: links != null);
                    if (!duel.StartNewDuel(match.first.main, match.first.extra, match.second.main, match.second.extra,
                        seed: (ulong[])match.seed.Clone()))
                        throw new InvalidOperationException("Could not start a training duel.");
                    var candidate = new TacticalAI(policy, links, searchCombos: links != null);
                    var reference = new TacticalAI(referencePolicy, links, searchCombos: links != null);
                    int openingTurn = candidateSeat + 1;
                    int opposingLpAtMain = 0;
                    var style = DuelDeckStrategy.For(duel, (byte)candidateSeat);
                    int cutEffects = 0, cardsDrawn = 0, previousDraws = 0, observedTurns = 0;
                    double styleProgress = 0;
                    double lifeProgress = 0, keysProgress = 0, drawProgress = 0;
                    duel.OnCardsDrawn += (player, cards) =>
                    {
                        if (style != DuelDeckStyle.Lock || player != candidateSeat) return;
                        bool normalDraw = duel.TurnPlayer == candidateSeat && duel.CurrentPhase == YgoConstants.PHASE_DRAW &&
                            duel.CurrentChain.Count == 0;
                        cardsDrawn += Math.Max(0, cards.Count - (normalDraw ? 1 : 0));
                    };
                    duel.OnChainResolving += link =>
                    {
                        if (style == DuelDeckStyle.Control && link.negated && link.triggerPlayer != candidateSeat &&
                            CardDatabase.Get(link.code) is CardInfo source && ComboSearch.CanDevelopFromCard(source) &&
                            duel.CurrentChain.Any(answer => answer.triggerPlayer == candidateSeat && answer.chainNumber > link.chainNumber))
                            cutEffects++;
                    };
                    for (int step = 0; step < 6400 && duel.TurnNumber <= 160 && !duel.IsFinished && duel.RetryCount == 0; step++)
                    {
                        var prompt = duel.ActivePrompt;
                        if (prompt == null)
                        {
                            duel.StepUntilPrompt();
                            continue;
                        }

                        bool candidateTurn = prompt.playerId == candidateSeat;
                        int turn = duel.TurnNumber;
                        int turnPlayer = duel.TurnPlayer;
                        if (candidateTurn && turn == openingTurn && opposingLpAtMain == 0 && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                            opposingLpAtMain = duel.Players[1 - candidateSeat].lp;
                        int retries = duel.RetryCount;
                        var ai = candidateTurn ? candidate : reference;
                        if (links != null)
                            while (!ai.AdvanceComboPlanning(duel, prompt)) { }
                        duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
                        result.retries += duel.RetryCount - retries;
                        bool turnEnded = duel.TurnNumber > turn || duel.IsFinished;
                        if (aggregate != null && turnEnded && turnPlayer == candidateSeat)
                        {
                            result.totalTurnEndBoard += ComboSearch.BoardValue(duel, (byte)candidateSeat, canBattle: false);
                            result.measuredTurns++;
                        }
                        if (turn == openingTurn && turnEnded && opposingLpAtMain > 0)
                        {
                            double board = Math.Tanh(ComboSearch.BoardValue(duel, (byte)candidateSeat, canBattle: false) / 7);
                            if (candidateSeat == 0)
                            {
                                result.setup += board;
                                if (style == DuelDeckStyle.Development) result.developmentSetup += board;
                            }
                            else
                            {
                                double damage = Math.Clamp((opposingLpAtMain - duel.Players[0].lp) / (double)opposingLpAtMain, 0, 1);
                                double breakthrough = duel.IsFinished && duel.Winner == candidateSeat ? 1 : .55 * damage + .45 * board;
                                result.breakthrough += breakthrough;
                                if (style == DuelDeckStyle.Development) result.developmentBreakthrough += breakthrough;
                            }
                        }
                        if (!turnEnded || observedTurns >= StyleObservationTurns || style == DuelDeckStyle.Development) continue;
                        var own = duel.Players[candidateSeat];
                        if (style == DuelDeckStyle.Control && turnPlayer == candidateSeat)
                        {
                            var rival = duel.Players[1 - candidateSeat];
                            double advantage = Math.Tanh((AvailableCards(own) - AvailableCards(rival)) / 5.0);
                            int setDisruption = own.szone.Count(card => card != null && !card.IsFaceUp &&
                                CardDatabase.Get(card.code) is CardInfo info && LinkComboPolicy.IsInteractive(info));
                            styleProgress += .45 * advantage + .25 * Math.Min(3, setDisruption) / 3;
                            observedTurns++;
                        }
                        if (style == DuelDeckStyle.Resource && turnPlayer == candidateSeat)
                        {
                            var rival = duel.Players[1 - candidateSeat];
                            styleProgress += Math.Tanh((DuelDeckStrategy.ResourcePosition(duel, (byte)candidateSeat) +
                                .30 * (AvailableCards(own) - AvailableCards(rival))) / 3);
                            observedTurns++;
                        }
                        if (style == DuelDeckStyle.Lock && turnPlayer != candidateSeat)
                        {
                            if (!duel.IsFinished || duel.Winner == candidateSeat)
                            {
                                lifeProgress += Math.Clamp(own.lp / 8000.0, 0, 1);
                                keysProgress += LockKeyProgress(own);
                            }
                            drawProgress += Math.Min(5, cardsDrawn - previousDraws) / 5.0;
                            previousDraws = cardsDrawn;
                            observedTurns++;
                        }
                    }

                    if (!duel.IsFinished) result.capped++;
                    if (style == DuelDeckStyle.Control && observedTurns > 0) result.control += styleProgress / observedTurns;
                    if (style == DuelDeckStyle.Resource && observedTurns > 0) result.resource += styleProgress / observedTurns;
                    if (style == DuelDeckStyle.Lock)
                    {
                        // A loss on the opponent's turn must not keep an earlier full-life score.
                        // An early win keeps the final state, but does not earn fictitious draws.
                        int remaining = StyleObservationTurns - observedTurns;
                        if (remaining > 0)
                        {
                            drawProgress += Math.Min(5, cardsDrawn - previousDraws) / 5.0;
                            if (duel.IsFinished && duel.Winner == candidateSeat)
                            {
                                var final = duel.Players[candidateSeat];
                                lifeProgress += remaining * Math.Clamp(final.lp / 8000.0, 0, 1);
                                keysProgress += remaining * LockKeyProgress(final);
                            }
                        }
                        result.lockSurvival += lifeProgress / StyleObservationTurns;
                        result.lockKeys += keysProgress / StyleObservationTurns;
                        result.lockDraws += drawProgress / StyleObservationTurns;
                        result.lockProgress += (.50 * lifeProgress + .25 * keysProgress + .25 * drawProgress) / StyleObservationTurns;
                    }
                    result.control += .30 * Math.Min(4, cutEffects) / 4;
                    result.styleGames[(int)style]++;
                    result.styleSeatGames[(int)style, candidateSeat]++;
                    if (!duel.IsFinished || duel.Winner < 0 || duel.Winner > 1)
                    {
                        result.draws++;
                        result.stylePoints[(int)style] += .5;
                        result.styleSeatPoints[(int)style, candidateSeat] += .5;
                    }
                    else if (duel.Winner == candidateSeat)
                    {
                        result.wins++;
                        result.stylePoints[(int)style]++;
                        result.styleSeatPoints[(int)style, candidateSeat]++;
                    }
                    else result.losses++;
                }
                completed++;
                if (clock != null && (completed % 8 == 0 || completed == fixtures.Length))
                    Debug.Log($"[AI EVAL] {stage}: {completed}/{fixtures.Length} pairs, " +
                        $"last decks {match.first.id}/{match.second.id}, seed {match.seed[0]}, {result}, {clock.Elapsed.TotalSeconds:F1}s");
            }
            return result;
        }

        private static int AvailableCards(PlayerFieldSnapshot field) =>
            field.hand.Count + field.mzone.Count(card => card != null) + field.szone.Count(card => card != null);

        private static double LockKeyProgress(PlayerFieldSnapshot field) => Math.Min(4,
            field.hand.Concat(field.mzone).Concat(field.szone)
                .Where(card => card != null && !card.effectsDisabled && DuelDeckStrategy.IsKeyPiece(card.code))
                .Select(card => CardDatabase.GetArchetypeCollectionKey(CardDatabase.Get(card.code))).Distinct().Count()) / 4.0;
    }
}
