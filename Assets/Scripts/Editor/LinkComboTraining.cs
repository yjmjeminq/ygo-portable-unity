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

        private sealed class Results
        {
            internal int games, linkThree, linkFour, interactive, wins, losses, draws, retries;
            internal double score;
            internal readonly Dictionary<uint, int> Finishers = new Dictionary<uint, int>();
            internal double Mean => score / games;
            internal double SetupQuality => Mean + (.65 * linkThree + 1.1 * linkFour) / games;
            internal string OpeningSummary() => $"{games} openings, Link-3+ {linkThree}, Link-4+ {linkFour}, interactive Link {interactive}, mean {Mean:F2}, {retries} retries";
            internal string MatchSummary() => $"{wins}W {losses}L {draws}D in {games}, {retries} retries";
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
                var previousPolicy = LinkComboPolicy.LoadIncluded();
                var initial = previousPolicy?.CopyWeights() ?? LinkComboPolicy.InitialWeights();
                var center = (float[])initial.Clone();
                var random = new System.Random(273370);
                var finalists = new List<float[]>();
                const int trainingOpenings = Rounds * Population * HandsPerDeck;

                for (int round = 0; round < Rounds; round++)
                {
                    var fixtures = CreateOpenings(decks, TrainingDecks, HandsPerDeck, 59501 + round);
                    var candidates = new float[Population][];
                    candidates[0] = center;
                    candidates[1] = finalists.Count == 0 ? initial : finalists[^1];
                    for (int i = 2; i < candidates.Length; i++) candidates[i] = Sample(center, random, .8f / (1 + .3f * round));
                    var ranked = candidates.Select(weights => (weights, outcome: Run(general, new LinkComboPolicy(weights), fixtures, fullDuel: false)))
                        .OrderByDescending(candidate => candidate.outcome.SetupQuality).ToArray();
                    finalists.Add(ranked[0].weights);
                    var elite = ranked.Take(3).Select(candidate => candidate.weights).ToArray();
                    center = Enumerable.Range(0, initial.Length).Select(i => elite.Average(weights => weights[i])).ToArray();
                    Debug.Log($"[LINK TRAIN] Round {round + 1}/{Rounds}, {ranked[0].outcome.OpeningSummary()} at {elapsed.Elapsed.TotalSeconds:F1}s");
                }

                finalists.Add(center);
                var validation = CreateOpenings(decks, TrainingDecks, 7, 92180);
                var fullValidation = CreateOpenings(decks, TrainingDecks, 3, 66457);
                var previousValidation = Run(general, previousPolicy, validation, fullDuel: false);
                var best = finalists.Where(weights => !weights.SequenceEqual(initial)).Select(weights => new LinkComboPolicy(weights))
                    .Select(policy => (policy, opening: Run(general, policy, validation, fullDuel: false)))
                    .Where(candidate => KeepsLinkStrength(previousValidation, candidate.opening))
                    .OrderByDescending(candidate => candidate.opening.SetupQuality).Take(3)
                    .Select(candidate => (candidate.policy, candidate.opening,
                        match: Run(general, candidate.policy, fullValidation, fullDuel: true)))
                    .OrderByDescending(candidate => candidate.opening.SetupQuality + 2.0 * (candidate.match.wins + .5 * candidate.match.draws) / candidate.match.games)
                    .FirstOrDefault();
                if (best.policy == null)
                    throw new InvalidOperationException("No trained Link candidate retained the validation Link-3 and Link-4 development rates.");
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
                Debug.Log($"[LINK TRAIN] Time {elapsed.Elapsed.TotalSeconds:F1}s, native script errors {errors.Count}");
                if (errors.Count > 0) Debug.LogWarning(string.Join("\n", errors.Take(3)));

                if (errors.Count != 0 || !KeepsLinkStrength(previous, trained) || trainedMatches.retries != 0 ||
                    trainedMatches.wins + .5 * trainedMatches.draws < previousMatches.wins + .5 * previousMatches.draws)
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
            candidate.retries == 0 && candidate.Mean >= previous.Mean - .15;

        private static Opening[] CreateOpenings(Dictionary<int, PresetDeckInfo> decks, int[] ids, int hands, int seed)
        {
            var random = new System.Random(seed);
            var validIds = ids.Where(id => decks.ContainsKey(id)).ToArray();
            var validRivals = RivalDecks.Where(id => decks.ContainsKey(id)).ToArray();
            return validIds.SelectMany(id => Enumerable.Range(0, hands).Select(hand => new Opening
            {
                deck = decks[id], opponent = decks[validRivals[random.Next(validRivals.Length)]], first = (hand & 1) == 0,
                seed = Enumerable.Range(0, 4).Select(_ => (ulong)random.Next(1, int.MaxValue)).ToArray(),
            })).ToArray();
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

        private static Results Run(DuelAiPolicy general, LinkComboPolicy link, Opening[] openings, bool fullDuel)
        {
            var results = new Results();
            foreach (var opening in openings)
            {
                using var duel = new DuelSession();
                if (!duel.StartNewDuel(opening.deck.main, opening.deck.extra, opening.opponent.main, opening.opponent.extra, opening.first, seed: opening.seed))
                    throw new InvalidOperationException($"Could not start {opening.deck.title} training duel.");
                int seat = duel.HumanPlayerIndex;
                int comboTurn = opening.first ? 1 : 2;
                var candidate = new TacticalAI(general, link);
                var opponent = new TacticalAI(general);
                int limit = fullDuel ? 1000 : 180;
                for (int step = 0; step < limit && !duel.IsFinished && duel.RetryCount <= 3; step++)
                {
                    if (duel.TurnNumber > (fullDuel ? 65 : comboTurn)) break;
                    var prompt = duel.ActivePrompt;
                    if (prompt == null) { duel.StepUntilPrompt(); continue; }
                    byte[] response = (prompt.playerId == seat ? candidate : opponent).ComputeResponse(duel, prompt);
                    if (!fullDuel && prompt.playerId == seat && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
                        (BitConverter.ToInt32(response, 0) & 0xffff) is 6 or 7) break;
                    duel.SubmitResponse(response);
                }
                results.games++;
                results.retries += duel.RetryCount;
                if (fullDuel)
                {
                    if (!duel.IsFinished || duel.Winner < 0 || duel.Winner > 1) results.draws++;
                    else if (duel.Winner == seat) results.wins++;
                    else results.losses++;
                }
                else MeasureBoard(duel, seat, !opening.first && duel.CurrentPhase == YgoConstants.PHASE_MAIN1, results);
            }
            return results;
        }

        private static void MeasureBoard(DuelSession duel, int seat, bool canBreakBoard, Results results)
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
            if (highest >= 3) results.linkThree++;
            if (highest >= 4) results.linkFour++;
            if (interaction > 0) results.interactive++;
            results.score += .7 * highest + .65 * interaction +
                .75 * ComboSearch.BoardValue(duel, (byte)seat, canBreakBoard) - duel.RetryCount * 1.5;
        }
    }
}
