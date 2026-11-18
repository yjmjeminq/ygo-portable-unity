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

        private static readonly int[] TrainingDeckIds =
        {
            20, 288, 289, 312, 385, 520, 34043, 36036, 20670, 20671, 20672, 20673,
            20674, 20675, 20676, 20678, 20684, 20686, 1000104, 1000094, 1000133, 1000016,
            36, 155, 309, 313, 340, 485, 644, 36017, 20677, 20679, 20680, 20681,
            20682, 20683, 20685, 20687, 20688, 20689, 1000119, 1000187, 1000137, 1000043,
        };
        private static readonly int[] TestDeckIds =
        {
            295, 298, 300, 302, 303, 304, 306, 576, 37095, 35000, 35001, 35002,
            35004, 35005, 33013, 37051, 37057, 588, 117, 157, 83, 97, 109, 36005,
        };

        private sealed class Fixture
        {
            internal PresetDeckInfo first;
            internal PresetDeckInfo second;
            internal ulong[] seed;
        }

        private sealed class Results
        {
            internal int wins, losses, draws, retries, capped;
            internal int Games => wins + losses + draws;
            internal double Rate => (wins + .5 * draws) / Games;
            internal double Objective => Rate - .25 * retries / Games;
            public override string ToString() => $"{wins}W {losses}L {draws}D in {Games} games ({Rate:P1}), {retries} candidate retries, {capped} capped";
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
                var trainingDecks = TrainingDeckIds.Select(id => available[id]).ToArray();
                var testDecks = TestDeckIds.Select(id => available[id]).ToArray();
                var elapsed = Stopwatch.StartNew();
                var random = new System.Random(38407);
                float[] initial = DuelAiPolicy.InitialWeights();
                var validation = CreateFixtures(trainingDecks, ValidationPairs, 483172);
                var main = TrainDecisions(initial, DuelAiPolicy.Decisions.MainPhase, trainingDecks, validation, random, elapsed);
                var chains = TrainDecisions(initial, DuelAiPolicy.Decisions.Chains, trainingDecks, validation, random, elapsed);
                var combinedWeights = (float[])main.weights.Clone();
                int chainStart = DuelAiPolicy.FirstChainFeature;
                Array.Copy(chains.weights, chainStart, combinedWeights, chainStart, combinedWeights.Length - chainStart);
                var combined = new DuelAiPolicy(combinedWeights, DuelAiPolicy.Decisions.Both);
                var best = new[] { (main.policy, main.results), (chains.policy, chains.results),
                        (policy: combined, results: Evaluate(combined, validation, tacticalReference: true)) }
                    .OrderByDescending(entry => entry.results.Objective).First();
                const int trainingGames = Generations * PopulationSize * TrainingPairs * 4;
                Debug.Log($"[AI TRAIN] Selected {best.policy.EnabledDecisions} after {trainingGames} games with {trainingDecks.Length} training decks. Validation vs rules-only AI {best.results}");

                var test = CreateFixtures(testDecks, TestPairs, 559236);
                var versusPrevious = Evaluate(best.policy, test, tacticalReference: false);
                var versusRules = Evaluate(best.policy, test, tacticalReference: true);
                var rulesVersusPrevious = Evaluate(null, test, tacticalReference: false);
                Debug.Log($"[AI EVAL] Held-out vs previous AI {versusPrevious}");
                Debug.Log($"[AI EVAL] Held-out vs rules-only AI {versusRules}");
                Debug.Log($"[AI EVAL] Held-out rules-only vs previous AI {rulesVersusPrevious}");
                Debug.Log($"[AI TRAIN] Time {elapsed.Elapsed.TotalSeconds:F1}s, native script errors {scriptErrors}");
                if (versusPrevious.Objective <= .52 || versusRules.Objective < .48)
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

        private static (float[] weights, DuelAiPolicy policy, Results results) TrainDecisions(float[] initial,
            DuelAiPolicy.Decisions decisions, PresetDeckInfo[] decks, Fixture[] validation, System.Random random, Stopwatch elapsed)
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
                var ranked = candidates.Select(weights => (weights, results: Evaluate(new DuelAiPolicy(weights, decisions), fixtures, tacticalReference: true)))
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
            var best = finalists.Where(weights => !weights.SequenceEqual(initial))
                .Select(weights => (weights, policy: new DuelAiPolicy(weights, decisions)))
                .Select(entry => (entry.weights, entry.policy, results: Evaluate(entry.policy, validation, tacticalReference: true)))
                .OrderByDescending(entry => entry.results.Objective).First();
            Debug.Log($"[AI TRAIN] {decisions} validation {best.results}");
            return best;
        }

        private static Fixture[] CreateFixtures(PresetDeckInfo[] decks, int count, int shuffleSeed)
        {
            var random = new System.Random(shuffleSeed);
            var orderedDecks = decks.OrderBy(_ => random.Next()).ToArray();
            var fixtures = new Fixture[count];
            for (int i = 0; i < count; i++)
            {
                int pair = i % decks.Length;
                fixtures[i] = new Fixture
                {
                    first = orderedDecks[pair],
                    second = i % 4 == 0 ? orderedDecks[pair] : orderedDecks[(pair + random.Next(1, decks.Length)) % decks.Length],
                    seed = Enumerable.Range(0, 4).Select(_ => (ulong)random.Next(1, int.MaxValue)).ToArray(),
                };
            }
            return fixtures;
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

        private static Results Evaluate(DuelAiPolicy policy, Fixture[] fixtures, bool tacticalReference)
        {
            var result = new Results();
            foreach (var match in fixtures)
            {
                // The same shuffled seats are played with the candidate on each side to cancel deck and turn-order bias.
                for (int candidateSeat = 0; candidateSeat < 2; candidateSeat++)
                {
                    using var duel = new DuelSession();
                    if (!duel.StartNewDuel(match.first.main, match.first.extra, match.second.main, match.second.extra, seed: match.seed))
                        throw new InvalidOperationException("Could not start a training duel.");
                    var candidate = new TacticalAI(policy);
                    var reference = tacticalReference ? new TacticalAI(null) : new TacticalAI();
                    for (int step = 0; step < 1200 && duel.TurnNumber <= 60 && !duel.IsFinished && duel.RetryCount <= 4; step++)
                    {
                        var prompt = duel.ActivePrompt;
                        if (prompt == null)
                        {
                            duel.StepUntilPrompt();
                            continue;
                        }

                        bool candidateTurn = prompt.playerId == candidateSeat;
                        int retries = duel.RetryCount;
                        duel.SubmitResponse((candidateTurn ? candidate : reference).ComputeResponse(duel, prompt));
                        if (candidateTurn) result.retries += duel.RetryCount - retries;
                    }

                    if (!duel.IsFinished) result.capped++;
                    if (!duel.IsFinished || duel.Winner < 0 || duel.Winner > 1) result.draws++;
                    else if (duel.Winner == candidateSeat) result.wins++;
                    else result.losses++;
                }
            }
            return result;
        }
    }
}
