using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.AI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    public static class TestOfflineRanked
    {
        public static void VerifyHeadless()
        {
            int exit = 1;
            string path = Path.Combine(Path.GetTempPath(), "ygo-ranked-verify-" + Guid.NewGuid().ToString("N"));
            string streaming = Application.streamingAssetsPath;
            try
            {
                Require(YgoNativeBridge.Initialize(streaming) == 0, "Native engine failed to initialize.");
                CardDatabase.Load(streaming);
                CardArtLoader.Initialize(streaming);
                DeckRepository.Load(streaming, path, false);
                CollectionProgress.Load(path);
                Require(CollectionProgress.Ranked.rank == 0 && CollectionProgress.Ranked.season == 1,
                    "An existing profile without ranked data did not start in Bronze V.");
                Require(CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out var deck, out var error), error);
                DeckRepository.SaveUserDeck(deck);
                string currentJson = File.ReadAllText(Path.Combine(path, CollectionProgress.ProfileFileName));
                int rankedProperty = currentJson.LastIndexOf("\"ranked\":", StringComparison.Ordinal);
                Require(rankedProperty > 0, "No ranked settings were written to the collection profile.");
                CollectionProgress.ValidateSaveProfile(currentJson[..rankedProperty].TrimEnd().TrimEnd(',') + "}");
                VerifyPools(deck);
                VerifyRules(deck);
                VerifyLobby(deck);
                VerifySave(path, deck);
                Debug.Log("[VERIFY OFFLINE RANKED] Native matchmaking, full ladder, stakes, payouts, save reload, lobby and season reset passed.");
                exit = 0;
            }
            catch (Exception error) { Debug.LogException(error); }
            finally
            {
                CardArtLoader.Shutdown();
                YgoNativeBridge.Shutdown();
                if (Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }

        private static void VerifyPools(UserDeck deck)
        {
            int[] from = { 1, 3, 5, 7, 9, 10 };
            int[] to = { 3, 5, 7, 9, 10, 10 };
            for (int tier = 0; tier < 6; tier++)
            {
                var pool = OfflineRanked.Pool(tier * 5);
                int meta = pool.Count(match => match.Character.isMeta2026);
                Require(pool.Any(match => !match.Character.isMeta2026) &&
                    pool.All(match => match.Level >= from[tier] && match.Level <= to[tier]) &&
                    (tier >= 4 ? meta == (tier == 4 ? 10 : 21) : meta == 0),
                    $"Tier {tier} missed a matching pool or admitted the wrong decks ({meta} meta).");
                Require(pool.All(match => tier >= 4 ? match.PlanningScale == ComboSearch.MaxPlanningScale :
                    tier != 3 || match.PlanningScale >= 1.45f && match.PlanningScale <= ComboSearch.MaxPlanningScale),
                    "A high-ranked matchup used the wrong planning budget.");
                var opponent = pool.First();
                using var duel = new DuelSession();
                Require(duel.StartNewDuel(deck.main, deck.extra, opponent.Deck.main, opponent.Deck.extra,
                    p0GoesFirst: tier % 2 == 0, seed: new ulong[] { 42, 14, 729, 3 }, playerDeckAppearance: deck) &&
                    duel.ActivePrompt != null && duel.RetryCount == 0, $"Tier {tier} could not start a real duel.");
                Debug.Log($"[VERIFY RANKED POOL] {OfflineRanked.Label(tier * 5)}: {pool.Count} builds, {meta} metagame builds.");
            }
        }

        private static void VerifyRules(UserDeck deck)
        {
            var opponent = OfflineRanked.Pool(0)[0];
            RankedMatch Match(RankedSave state) => new RankedMatch(state, opponent, deck, true);
            var silver = new RankedSave { rank = 5, points = 1, streak = 1, bestRank = 5, bestStreak = 1 };
            Require(OfflineRanked.PromotionWin(silver) && OfflineRanked.WinsUntilPromotion(silver) == 1,
                "Silver's second win in a row was not a promotion.");
            OfflineRanked.Apply(silver, Match(silver), RankedOutcome.Win);
            Require(silver.rank == 6 && silver.points == 0, "Silver streak acceleration failed.");
            var gold = new RankedSave { rank = 10, points = 1, streak = 2, bestRank = 10, bestStreak = 2 };
            Require(OfflineRanked.PromotionWin(gold), "Gold's third win in a row did not warn of promotion.");
            OfflineRanked.Apply(gold, Match(gold), RankedOutcome.Loss);
            OfflineRanked.Apply(gold, Match(gold), RankedOutcome.Loss);
            Require(gold.rank == 10 && gold.points == 0 && gold.streak == 0, "Gold V dropped through its floor.");
            var platinum = new RankedSave { rank = 16, bestRank = 16 };
            Require(OfflineRanked.DemotionLoss(platinum), "The demotion warning was missing.");
            OfflineRanked.Apply(platinum, Match(platinum), RankedOutcome.Loss);
            Require(platinum.rank == 15 && platinum.points == 3, "A Platinum division could not demote.");
            platinum.claims = 1 << 16;
            var recovered = OfflineRanked.Apply(platinum, Match(platinum), RankedOutcome.Win);
            Require(platinum.rank == 16 && recovered.NewRankReward == -1 && recovered.Dp == 450,
                "Recovering a previously claimed division awarded its prize twice in the same season.");
            Require(!OfflineRanked.OpponentTakesFirst(new PresetDeckInfo
                { title = "후공 돌파", main = new uint[] { 18144506 } }) &&
                OfflineRanked.OpponentTakesFirst(new PresetDeckInfo { main = Array.Empty<uint>() }),
                "AI turn choice ignored the deck's strategy.");
        }

        private static void VerifyLobby(UserDeck deck)
        {
            var canvas = new GameObject("Ranked GUI fixture", typeof(Canvas)).GetComponent<Canvas>();
            var events = new GameObject("Ranked GUI input", typeof(EventSystem));
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1500, 700);
            try
            {
                var ui = new OfflineRankedUI(null, canvas.transform, mobileLayout: true);
                ui.Open();
                var lobby = ui.ActiveRoot.transform;
                Require(lobby.Find("Rank/RankLabel/Text").GetComponent<Text>().text == "BRONZE V" &&
                    lobby.Find("Deck/DeckName/Text").GetComponent<Text>().text.StartsWith(deck.name, StringComparison.Ordinal) &&
                    !lobby.Find("ResetSeason").GetComponentInChildren<Button>(true).gameObject.activeSelf,
                    "The mobile ranked lobby did not initialize correctly.");
                lobby.Find("Queue/Button").GetComponent<Button>().onClick.Invoke();
                Require(ui.ActiveRoot.name == "Versus" && ui.ActiveRoot.transform.Find("Coin/Face/MintedFace/Face/Text") != null,
                    "Queueing did not open a VS coin toss.");
                ui.Update(1.2f);
                Require(ui.ActiveRoot.transform.Find("TossResult/Text").GetComponent<Text>().text.Contains("선공") ||
                    ui.ActiveRoot.transform.Find("TossResult/Text").GetComponent<Text>().text.Contains("후공"),
                    "The coin toss did not resolve to a choice.");
                ui.Back();
                Require(ui.ActiveRoot.name == "Lobby", "Back from the coin toss did not restore the lobby.");
                ui.Close();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(events);
                UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            }
        }

        private static void VerifySave(string save, UserDeck deck)
        {
            Require(CollectionProgress.TrySelectRankedDeck(deck, out var error), error);
            var opponent = OfflineRanked.Pool(0)[0];
            int wins = 0;
            int rewards = 0;
            while (CollectionProgress.Ranked.rank < OfflineRanked.Champion && wins < 140)
            {
                var state = CollectionProgress.Ranked;
                var match = new RankedMatch(state, opponent, deck, true) { PlayerFirst = wins % 2 == 0 };
                int balance = CollectionProgress.Balance;
                decimal dust = CollectionProgress.CraftDust;
                Require(CollectionProgress.TryFinishRankedMatch(match, RankedOutcome.Win, out var prize, out error), error);
                Require(CollectionProgress.Balance == balance + prize.Dp && CollectionProgress.CraftDust == dust + prize.Dust,
                    "The ladder and wallet did not commit together.");
                Require(!CollectionProgress.TryFinishRankedMatch(match, RankedOutcome.Win, out _, out _),
                    "A duplicate match produced a second reward.");
                if (prize.Promoted && prize.RankAfter is 15 or 20 or 25 or OfflineRanked.Champion)
                {
                    Require(prize.Card != 0 && (prize.RankAfter == 15 ? CollectionProgress.GetOwnedCount(prize.Card) :
                        CollectionProgress.GetOwnedOverframeCount(prize.Card)) > 0,
                        "A milestone's card was missing from inventory.");
                    if (prize.RankAfter == 25) Require(CardRarityCatalog.GetRarity(prize.Card) == CardRarity.UltraRare, "Master rewards must be UR.");
                    if (prize.RankAfter == OfflineRanked.Champion)
                        Require(prize.Card == match.Ace && CollectionProgress.IsRankedChampion, "Master I did not grant the actual ace and title.");
                    rewards++;
                }
                wins++;
            }
            var champion = CollectionProgress.Ranked;
            Require(wins < 140 && rewards == 4 && champion.IsValid && champion.rating == 1500 &&
                champion.history.Count == 10 && champion.wins == wins && champion.champions == 1,
                "The ladder did not reach Master I with the expected history.");
            CollectionProgress.Load(save);
            Require(CollectionProgress.Ranked.rank == OfflineRanked.Champion && CollectionProgress.IsRankedChampion &&
                CollectionProgress.Ranked.deckId == deck.id, "Rank and deck selection did not survive loading the saved profile.");
            var rankedMatch = new RankedMatch(CollectionProgress.Ranked, opponent, deck, false);
            Require(CollectionProgress.TryFinishRankedMatch(rankedMatch, RankedOutcome.Loss, out var loss, out error) &&
                loss.RatingChange < 0 && CollectionProgress.Ranked.rank == OfflineRanked.Champion, "Master I loss incorrectly demoted or kept its rating.");
            Require(CollectionProgress.TryResetRankedSeason(out error), error);
            CollectionProgress.Load(save);
            var reset = CollectionProgress.Ranked;
            Require(reset.season == 2 && reset.rank == 15 && reset.claims == 0 && reset.bestRank == OfflineRanked.Champion &&
                reset.rating == 0 && reset.champions == 1 && CollectionProgress.IsRankedChampion, "Season reset lost an honor or carried current rewards over.");
            for (int i = 0; i < 4; i++)
                Require(CollectionProgress.TryFinishRankedMatch(new RankedMatch(CollectionProgress.Ranked, opponent, deck, true),
                    RankedOutcome.Win, out _, out error), error);
            Require((CollectionProgress.Ranked.claims & (1 << 16)) != 0, "A second season could not earn a fresh division reward.");
        }

        private static void Require(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
        }
    }
}
