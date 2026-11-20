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
            internal int wins, losses, draws, retries, capped;
            internal double setup, breakthrough, control, lockProgress;
            internal double lockSurvival, lockKeys, lockDraws;
            internal readonly int[] styleGames = new int[3];
            internal readonly double[] stylePoints = new double[3];
            internal int Games => wins + losses + draws;
            internal double Rate => (wins + .5 * draws) / Games;
            internal double Objective => Rate + (.10 * setup + .12 * breakthrough + .12 * control + .12 * lockProgress - .5 * retries - .12 * capped) / Games;
            public override string ToString() => $"{wins}W {losses}L {draws}D in {Games} games ({Rate:P1}), setup {2 * setup / Games:F2}, " +
                $"breakthrough {2 * breakthrough / Games:F2}, control {control / Games:F2}, lock {lockProgress / Games:F2}, " +
                $"{retries} candidate retries, {capped} capped";

            internal string StyleSummary() => string.Join(", ", Enum.GetValues(typeof(DuelDeckStyle)).Cast<DuelDeckStyle>()
                .Select(style => $"{style} {stylePoints[(int)style]:0.0}/{styleGames[(int)style]}"));

            internal double StyleObjective(DuelDeckStyle style)
            {
                int index = (int)style;
                double score = style == DuelDeckStyle.Lock ? .35 * stylePoints[index] + lockProgress :
                    stylePoints[index] + (style == DuelDeckStyle.Control ? .12 * control : 0);
                return score / Math.Max(1, styleGames[index]);
            }

            internal bool PreservesLockResults(Results reference)
            {
                return PreservesStyleWins(reference, DuelDeckStyle.Lock) &&
                    lockSurvival >= reference.lockSurvival && lockKeys >= reference.lockKeys && lockDraws >= reference.lockDraws;
            }

            internal bool PreservesControlResults(Results reference) =>
                PreservesStyleWins(reference, DuelDeckStyle.Control) && control >= reference.control;

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
                var main = TrainDecisions(initial, DuelAiPolicy.Decisions.MainPhase, trainingDecks, validation, existingPolicy, random, elapsed);
                var chains = TrainDecisions(initial, DuelAiPolicy.Decisions.Chains, trainingDecks, validation, existingPolicy, random, elapsed);
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
                var stylePolicy = existingPolicy;
                if (existingPolicy != null)
                {
                    var previousResult = Evaluate(existingPolicy, validation, existingPolicy);
                    candidatesList.Add((existingPolicy, previousResult));
                    foreach (var style in new[] { DuelDeckStyle.Control, DuelDeckStyle.Lock })
                    {
                        var specialist = candidatesList.Where(entry => style != DuelDeckStyle.Lock || entry.results.PreservesLockResults(previousResult))
                            .OrderByDescending(entry => entry.results.StyleObjective(style)).First();
                        if (specialist.results.StyleObjective(style) <= previousResult.StyleObjective(style)) continue;
                        stylePolicy = stylePolicy.WithStyle(style, specialist.policy);
                        Debug.Log($"[AI TRAIN] {style} profile from {specialist.policy.EnabledDecisions}. " +
                            $"Validation {previousResult.StyleObjective(style):F3} -> {specialist.results.StyleObjective(style):F3}");
                    }
                    if (!ReferenceEquals(stylePolicy, existingPolicy))
                        candidatesList.Add((stylePolicy, Evaluate(stylePolicy, validation, existingPolicy)));
                }
                var linkPolicy = LinkComboPolicy.LoadIncluded() ?? throw new InvalidOperationException("Missing the Link AI model.");
                var plannerFixtures = CreateFixtures(trainingDecks, 18, 782449);
                var shortlist = candidatesList.OrderByDescending(entry => entry.results.Objective).Take(3)
                    .Select(entry => entry.policy).ToList();
                if (stylePolicy != null && !ReferenceEquals(stylePolicy, existingPolicy) && !shortlist.Contains(stylePolicy))
                    shortlist.Add(stylePolicy);
                var best = shortlist.Select(policy => (policy, results: Evaluate(policy, plannerFixtures, existingPolicy, linkPolicy)))
                    .OrderByDescending(entry => entry.results.Objective).First();
                const int trainingGames = Generations * PopulationSize * TrainingPairs * 4;
                Debug.Log($"[AI TRAIN] Selected {best.policy.EnabledDecisions} after {trainingGames} games with {trainingDecks.Length} training decks. Planner validation against the included policy {best.results}");

                var test = CreateFixtures(testDecks, TestPairs, 559236);
                var included = Evaluate(existingPolicy, test, existingPolicy, linkPolicy);
                var versusPrevious = Evaluate(best.policy, test, existingPolicy, linkPolicy);
                var versusRules = Evaluate(best.policy, test, null, linkPolicy);
                var rulesVersusPrevious = Evaluate(null, test, existingPolicy, linkPolicy);
                bool controlPreserved = versusPrevious.PreservesControlResults(included);
                bool lockPreserved = versusPrevious.PreservesLockResults(included);
                Debug.Log($"[AI EVAL] Held-out included policy {included.StyleSummary()}, {included.LockSummary()}");
                Debug.Log($"[AI EVAL] Held-out vs previous AI {versusPrevious}");
                Debug.Log($"[AI EVAL] Held-out style results {versusPrevious.StyleSummary()}");
                Debug.Log($"[AI EVAL] Held-out Lock {versusPrevious.LockSummary()}");
                Debug.Log($"[AI EVAL] Held-out preservation with the runtime planner: Control={controlPreserved}, Lock={lockPreserved}");
                Debug.Log($"[AI EVAL] Held-out vs rules-only AI {versusRules}");
                Debug.Log($"[AI EVAL] Held-out rules-only vs previous AI {rulesVersusPrevious}");
                Debug.Log($"[AI TRAIN] Time {elapsed.Elapsed.TotalSeconds:F1}s, native script errors {scriptErrors}");
                if (scriptErrors != 0 || versusPrevious.Rate < .50 || versusRules.Rate < .45 || versusPrevious.retries != 0 ||
                    versusPrevious.capped != 0 || !controlPreserved || !lockPreserved)
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
            DuelAiPolicy.Decisions decisions, PresetDeckInfo[] decks, Fixture[] validation,
            DuelAiPolicy previousPolicy, System.Random random, Stopwatch elapsed)
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
            var best = finalists.Where(weights => !weights.SequenceEqual(initial))
                .Select(weights => (weights, policy: new DuelAiPolicy(weights, decisions)))
                .Select(entry => (entry.weights, entry.policy, results: Evaluate(entry.policy, validation, previousPolicy)))
                .OrderByDescending(entry => entry.results.Objective).First();
            Debug.Log($"[AI TRAIN] {decisions} validation {best.results}");
            return best;
        }

        private static Fixture[] CreateFixtures(PresetDeckInfo[] decks, int count, int shuffleSeed)
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
                fixtures[i] = new Fixture
                {
                    first = first,
                    second = i % 4 == 0 ? first : orderedDecks[random.Next(orderedDecks.Length)],
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

        private static Results Evaluate(DuelAiPolicy policy, Fixture[] fixtures, DuelAiPolicy referencePolicy, LinkComboPolicy links = null)
        {
            var result = new Results();
            foreach (var match in fixtures)
            {
                // The same shuffled seats are played with the candidate on each side to cancel deck and turn-order bias.
                for (int candidateSeat = 0; candidateSeat < 2; candidateSeat++)
                {
                    using var duel = new DuelSession(recordPlanningReplay: links != null);
                    if (!duel.StartNewDuel(match.first.main, match.first.extra, match.second.main, match.second.extra, seed: match.seed))
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
                    for (int step = 0; step < 6400 && duel.TurnNumber <= 160 && !duel.IsFinished && duel.RetryCount <= 4; step++)
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
                        duel.SubmitResponse((candidateTurn ? candidate : reference).ComputeResponse(duel, prompt));
                        if (candidateTurn) result.retries += duel.RetryCount - retries;
                        bool turnEnded = duel.TurnNumber > turn || duel.IsFinished;
                        if (turn == openingTurn && turnEnded && opposingLpAtMain > 0)
                        {
                            double board = Math.Tanh(ComboSearch.BoardValue(duel, (byte)candidateSeat, canBattle: false) / 7);
                            if (candidateSeat == 0) result.setup += board;
                            else
                            {
                                double damage = Math.Clamp((opposingLpAtMain - duel.Players[0].lp) / (double)opposingLpAtMain, 0, 1);
                                result.breakthrough += duel.IsFinished && duel.Winner == candidateSeat ? 1 : .55 * damage + .45 * board;
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
                    if (!duel.IsFinished || duel.Winner < 0 || duel.Winner > 1)
                    {
                        result.draws++;
                        result.stylePoints[(int)style] += .5;
                    }
                    else if (duel.Winner == candidateSeat)
                    {
                        result.wins++;
                        result.stylePoints[(int)style]++;
                    }
                    else result.losses++;
                }
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
