using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.AI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static class TestStoryTactics
    {
        public static void Run()
        {
            string streaming = Application.streamingAssetsPath;
            string save = Path.Combine(Path.GetTempPath(), "ygo-tactics-" + Guid.NewGuid().ToString("N"));
            int exit = 1;
            try
            {
                CardDatabase.Load(streaming);
                DeckRepository.Load(streaming, save, seedDefaultDecks: false);
                CardArtLoader.Initialize(streaming, Path.Combine(save, "Art"));
                CollectionProgress.Load(save);
                Require(CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out _, out string error), error);
                Require(YgoNativeBridge.Initialize(streaming, null) == 0, "The native engine did not start.");
                VerifyBranchesAndSupports(save);
                VerifyOpponentOpenings();
                VerifyDestinyAwakening();
                VerifyTournament(save);
                VerifyNewScreens();
                Debug.Log("[VERIFY STORY TACTICS] Authored/automatic branches, per-fight supports, two boosted Destiny Draws, real bracket simulations and championship reward redemption passed.");
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

        private static void VerifyBranchesAndSupports(string save)
        {
            var dm = StoryModeUI.LoadScript(Application.streamingAssetsPath, "dm");
            var original = StoryDialogueBranch.Create(dm.episodes[0], false, "");
            var generated = StoryDialogueBranch.Create(dm.episodes[1], false, "에이스");
            Require(original.Length == 3 && original[0].Reply == dm.episodes[0].choices[0].text &&
                generated.Select(choice => choice.Stance).Distinct().Count() == 3,
                "The authored or automatic dialogue branches were lost.");
            var raid = StoryRaid.For("arcv", 44).Value;
            var rival = DimensionTournament.HighestDeck(74);
            foreach (var entry in RaidSupportCatalog.Items)
            {
                int balance = CollectionProgress.Balance;
                Require(CollectionProgress.TryBuyRaidSupport(entry.Id, out string error) &&
                    CollectionProgress.Balance == balance - entry.Cost && CollectionProgress.GetSupportCount(entry.Id) == 1, error);
            }
            CollectionProgress.Load(save);
            Require(RaidSupportCatalog.Items.All(item => CollectionProgress.GetSupportCount(item.Id) == 1),
                "The support inventory did not survive reload.");
            Require(!CollectionProgress.TryConsumeRaidSupports(RaidSupport.QuickDraw | RaidSupport.Breaker | RaidSupport.Victory,
                true, out _) && !CollectionProgress.TryConsumeRaidSupports(RaidSupport.Breaker, false, out _),
                "An illegal support loadout was consumed.");
            var plan = new StoryBattlePlan(StoryStance.Challenge, RaidSupport.QuickDraw | RaidSupport.Breaker);
            var (player, boss) = plan.Apply(raid);
            using var duel = new DuelSession();
            var deck = Enumerable.Repeat(20721928u, 40).ToArray();
            Require(duel.StartNewDuel(deck, Array.Empty<uint>(), rival.main, rival.extra, seed: new ulong[] { 11, 73, 19, 92 },
                opponentOpening: boss, playerOpening: player), "Could not start the enhanced raid fixture.");
            Require(duel.Players[duel.HumanPlayerIndex].hand.Count == 7 &&
                duel.Players[duel.AiPlayerIndex].lp == 8000 && duel.Players[duel.AiPlayerIndex].hand.Count == 12 &&
                duel.Players[duel.AiPlayerIndex].mzone.All(card => card == null) &&
                duel.Players[duel.AiPlayerIndex].szone.All(card => card == null),
                "The challenge starting hand or the field breaker did not reach the native engine.");
            Require(CollectionProgress.TryConsumeRaidSupports(plan.Supports, true, out string consumeError) &&
                CollectionProgress.GetSupportCount(RaidSupport.QuickDraw) == 0 && CollectionProgress.GetSupportCount(RaidSupport.Breaker) == 0,
                consumeError);
            Require(!CollectionProgress.TryConsumeRaidSupports(plan.Supports, true, out _), "A spent item was reused.");
            Require(new StoryBattlePlan(StoryStance.Calm, RaidSupport.None).Apply(null).Player.Life == 9000 &&
                new StoryBattlePlan(StoryStance.Calm, RaidSupport.Aegis).Apply(raid).Player.Life == 12000,
                "Aegis or the Calm stance did not deliver its agreed LP.");

            var rewardPlan = new StoryBattlePlan(StoryStance.Challenge, RaidSupport.Victory);
            Require(CollectionProgress.TryAwardStoryDuel("arcv_ep_44", true, raid, rival, rewardPlan, out var prize, out string rewardError,
                new FixedRandom(80)) && prize.Dp == 18000 && prize.Dust == 300 && prize.RaidCard.HasValue, rewardError);
            Require(CollectionProgress.TryAwardStoryDuel("gx_ep_01", true, null, rival, default, out var normal, out rewardError,
                new FixedRandom(25)) && !normal.ThemeCard.HasValue, rewardError);
            Require(CollectionProgress.TryAwardStoryDuel("gx_ep_01", true, null, rival, new StoryBattlePlan(StoryStance.Bond, RaidSupport.None),
                out var friendly, out rewardError, new FixedRandom(25)) && friendly.ThemeCard.HasValue && friendly.Bond == 1, rewardError);
            Require(CollectionProgress.TryAwardStoryDuel("gx_ep_02", true, null, rival, default, out var drop, out rewardError,
                new FixedRandom(0)) && drop.Support != RaidSupport.None, rewardError);
            CollectionProgress.Load(save);
            Require(CollectionProgress.GetStoryBond(rival.charId) == 1, "The opponent's bond was not persisted.");
        }

        private static void VerifyOpponentOpenings()
        {
            const uint blueEyes = 89631139, baronne = 84815190, utopia = 84013237;
            var player = Enumerable.Repeat(20721928u, 40).ToArray();
            var rival = new PresetDeckInfo { sigCard = blueEyes, main = player.Append(blueEyes).ToArray(), extra = new[] { baronne, utopia } };
            var raid = StoryRaid.For("dm", 20).Value;
            Require(raid.Opening.FieldCard == blueEyes &&
                StoryBattlePlan.SelectOpeningMonster(rival, raid) == baronne,
                "The enhanced opening duplicated the raid boss or selected an Xyz without materials.");
            VerifyNativeOpening(StoryOpponentOpening.Standard, null, humanFirst: true, 5, 0);
            VerifyNativeOpening(StoryOpponentOpening.ExtraHand, null, humanFirst: false, 7, 0);
            VerifyNativeOpening(StoryOpponentOpening.PreplacedMonster, null, humanFirst: false, 5, blueEyes);
            VerifyNativeOpening(StoryOpponentOpening.ExtraHandAndMonster, raid, humanFirst: true, 9, baronne);
            Debug.Log("[VERIFY STORY OPENINGS] Four settings, both turn orders, main/extra transfers and planning replay passed.");

            void VerifyNativeOpening(StoryOpponentOpening option, StoryRaid? battle, bool humanFirst, int hand, uint monster)
            {
                var plan = new StoryBattlePlan(StoryStance.Neutral, RaidSupport.None, option);
                var opening = plan.Apply(battle, rival);
                using var duel = new DuelSession(recordPlanningReplay: true);
                Require(duel.StartNewDuel(player, Array.Empty<uint>(), rival.main, rival.extra, humanFirst,
                    seed: new ulong[] { 222, 17, 821, 909 }, opponentOpening: opening.Opponent, playerOpening: opening.Player),
                    $"Could not start the {option} fixture.");
                VerifyBoard(duel);
                using var replay = duel.ReplayForPlanning(() => false);
                Require(replay != null, "The planning replay did not restore the enhanced opening.");
                VerifyBoard(replay);

                void VerifyBoard(DuelSession session)
                {
                    byte seat = (byte)session.AiPlayerIndex;
                    var ai = session.Players[seat];
                    var onField = ai.mzone.Where(card => card?.IsFaceUp == true).Select(card => card.code);
                    uint baseCard = battle?.Opening.FieldCard ?? 0;
                    var expected = new[] { baseCard, monster }.Where(code => code != 0);
                    Require(ai.lp == 8000 && ai.hand.Count == hand && session.Players[session.HumanPlayerIndex].hand.Count == 5 &&
                        onField.OrderBy(code => code).SequenceEqual(expected.OrderBy(code => code)) &&
                        YgoNativeBridge.YGO_QueryCount(session.NativePtr, seat, YgoConstants.LOCATION_DECK) ==
                            rival.main.Length - hand - (monster == blueEyes ? 1 : 0) &&
                        YgoNativeBridge.YGO_QueryCount(session.NativePtr, seat, YgoConstants.LOCATION_EXTRA) ==
                            rival.extra.Length - (monster == baronne ? 1 : 0),
                        $"Unexpected field or deck counts for {option}, humanFirst={humanFirst}.");
                }
            }
        }

        private static void VerifyDestinyAwakening()
        {
            var deck = new UserDeck();
            for (int i = 0; i < 40; i++)
                deck.InsertCard(deck.main, i, 20721928, new DeckCardAppearance { destinyDraw = true });
            var awake = new StoryBattlePlan(StoryStance.Neutral, RaidSupport.Awakening).Apply(null).Player;
            using var duel = new DuelSession(recordPlanningReplay: true);
            Require(duel.StartNewDuel(deck.main, deck.extra, deck.main, deck.extra, playerDeckAppearance: deck,
                playerOpening: new DuelOpening(3500, awake.Hand, 0, awake.DestinyLimit, awake.DestinyUses),
                seed: new ulong[] { 26, 91, 72, 41 }), "Could not start the Awakening test duel.");
            var ai = new TacticalAI { Difficulty = AIDifficulty.Easy };
            var offers = new List<int>();
            for (int step = 0; step < 150 && !duel.IsFinished && duel.TurnNumber <= 7 && duel.RetryCount == 0; step++)
            {
                var prompt = duel.ActivePrompt;
                if (prompt == null) { duel.StepUntilPrompt(); continue; }
                if (prompt.messageType == YgoConstants.MSG_DESTINY_DRAW)
                {
                    offers.Add(duel.TurnNumber);
                    duel.SubmitResponseInt(1);
                }
                else if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && prompt.canEp) duel.SubmitResponseInt(7);
                else if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced) duel.SubmitResponseInt(-1);
                else duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
            }
            Require(offers.SequenceEqual(new[] { 3, 5 }) && duel.DestinyUsesLeft((byte)duel.HumanPlayerIndex) == 0,
                "Awakening did not grant exactly two normal-draw offers above the usual LP threshold.");
        }

        private static void VerifyTournament(string save)
        {
            Require(!CollectionProgress.TryStartTournament(8, out _), "The tournament was open before completion.");
            Require(CollectionProgress.TryAwardStoryDuel("dm_ep_50", true, null, null, out _, out _, out string error), error);
            foreach (int size in new[] { 8, 16 })
            {
                Require(CollectionProgress.TryStartTournament(size, out error), error);
                var seats = CollectionProgress.Tournament;
                var opponents = seats.bracket.Skip(size + 1).ToArray();
                Require(opponents.Distinct().Count() == size - 1 &&
                    opponents.Select(id => DeckRepository.GetCharacter(id).seriesIdx).Distinct().Count() == 6,
                    "The seed did not represent all six dimensions without repeat participants.");
                int wins = 0;
                while (CollectionProgress.Tournament.Active)
                {
                    var run = CollectionProgress.Tournament;
                    var outcomes = new Dictionary<int, int>();
                    foreach (int node in run.PendingAiMatches())
                    {
                        using var simulation = new TournamentMatchSimulation(run.bracket[node * 2], run.bracket[node * 2 + 1],
                            run.seed, node, run.PlayerNode);
                        for (int step = 0; step < 350 && !simulation.Finished; step++) simulation.Step();
                        Require(simulation.Finished, "An AI tournament match did not resolve.");
                        outcomes[node] = simulation.Winner;
                    }
                    if (outcomes.Count > 0) Require(CollectionProgress.TrySaveTournamentAi(run.seed, run.round, outcomes, out error), error);

                    var rival = DimensionTournament.HighestDeck(run.Opponent);
                    var opening = DimensionTournament.Opening(run.PlayerNode, rival);
                    Require(run.PlayerNode switch
                    {
                        2 => opening.Life == 20000 && opening.Hand == 7 && opening.FieldCard != 0,
                        4 => opening.Life == 12000 && opening.Hand == 6,
                        _ => opening.Life == 8000 && opening.Hand == 5
                    }, "Incorrect round difficulty.");
                    if (run.PlayerNode == 2)
                    {
                        using var duel = new DuelSession();
                        var player = Enumerable.Repeat(20721928u, 40).ToArray();
                        Require(duel.StartNewDuel(player, Array.Empty<uint>(), rival.main, rival.extra,
                            opponentOpening: opening, seed: new ulong[] { 19, 131, 89, 291 }), "The tournament final could not start.");
                        var boss = duel.Players[duel.AiPlayerIndex];
                        Require(boss.lp == 20000 && boss.hand.Count == 7 &&
                            boss.mzone.Any(card => card?.code == opening.FieldCard && card.IsFaceUp),
                            "The final did not apply its boosted LP, hand or ace.");
                    }
                    Require(CollectionProgress.TryFinishTournamentRound(run.seed, run.round, run.Opponent, true, out var reward, out error), error);
                    Require(reward.Champion == (run.PlayerNode == 2) &&
                        (reward.Champion ? reward.Dp == (size == 16 ? 5000 : 3000) && reward.Dust > 0 : reward.Dp == CollectionProgress.WinReward),
                        "Wrong tournament prize.");
                    Require(!CollectionProgress.TryFinishTournamentRound(run.seed, run.round, run.Opponent, true, out _, out _),
                        "The same round paid twice.");
                    wins++;
                    CollectionProgress.Load(save);
                }
                Require(wins == (size == 16 ? 4 : 3) && CollectionProgress.Tournament.Champion, "The seeded bracket did not conclude.");
            }

            Require(CollectionProgress.TournamentWins == 2 && CollectionProgress.FreeSecretBundles == 2, "The hall of fame or tickets were not saved.");
            var secret = CollectionProgress.Packs.First(pack => pack.IsSecret && pack.ThemeCards.Any(
                code => CardRarityCatalog.GetRarity(code) >= CardRarity.SuperRare));
            uint trigger = secret.ThemeCards.First(code => CardRarityCatalog.GetRarity(code) >= CardRarity.SuperRare);
            Require(CollectionProgress.TryCraftCard(trigger, 1, out error), error);
            int balance = CollectionProgress.Balance;
            Require(!CollectionProgress.TryBuyPackContents("general-master", 10, out _, out _, useFreeSecretBundle: true),
                "A tournament ticket opened a general pack.");
            Require(CollectionProgress.TryBuyPackContents(secret.Id, 10, out var cards, out error, useFreeSecretBundle: true) &&
                cards.Count == 10 && CollectionProgress.Balance == balance && CollectionProgress.FreeSecretBundles == 1, error);
        }

        private static void VerifyNewScreens()
        {
            var canvas = new GameObject("Tactics screen verification", typeof(Canvas));
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600, 900);
            try
            {
                var shop = new CollectionShopUI(canvas.transform, () => { }, _ => { });
                shop.OpenShop();
                shop.Root.transform.Find("TacticalSupport/Button").GetComponent<Button>().onClick.Invoke();
                var supports = shop.ActiveShopRoot.transform;
                int before = CollectionProgress.GetSupportCount(RaidSupport.Aegis);
                supports.Find("Window/Item_1/Buy/Button").GetComponent<Button>().onClick.Invoke();
                Require(CollectionProgress.GetSupportCount(RaidSupport.Aegis) == before + 1 &&
                    shop.HandleShopBack() && shop.ActiveShopRoot == shop.Root, "The shop support tab did not purchase or return.");
                shop.CloseShop();

                var tournament = new DimensionTournamentUI(null, canvas.transform);
                tournament.Open();
                var root = tournament.Root.transform;
                var bracket = root.Find("BracketView/Bracket/Draw");
                Require(bracket.childCount > 31 && bracket.Find("Match_1/Name").GetComponent<Text>().text.Contains("우승") &&
                    bracket.Find("Match_31/Name").GetComponent<Text>().text != "미정" &&
                    root.Find("BracketView/HallOfFame/Text").GetComponent<Text>().text.Contains("2회"),
                    "The sixteen-player visual bracket and hall of fame did not render the saved results.");
                var winner = CollectionProgress.Tournament.bracket[3];
                tournament.ShowResult(DeckRepository.GetCharacter(winner), 2, true, false, (5000, 3000, true), "");
                var dialogue = root.Find("StoryDialogue/Scene");
                dialogue.Find("DialogueBox/Skip/Button").GetComponent<Button>().onClick.Invoke();
                Require(root.Find("ChampionTrophy").gameObject.activeSelf &&
                    dialogue.Find("Choices/Summary/Label").GetComponent<Text>().text.Contains("무료 10팩권"),
                    "The championship dialogue omitted its trophy or reward.");
                tournament.Back();
                Require(!root.Find("ChampionTrophy").gameObject.activeSelf && tournament.ActiveRoot == tournament.Root,
                    "Closing the championship dialogue did not restore the bracket.");
                tournament.Close();
            }
            finally { UnityEngine.Object.DestroyImmediate(canvas); }
        }

        private sealed class FixedRandom : System.Random
        {
            private readonly int _value;
            internal FixedRandom(int value) => _value = value;
            public override int Next(int maxValue) => maxValue > 0 ? _value % maxValue : 0;
        }

        private static void Require(bool test, string error)
        {
            if (!test) throw new InvalidOperationException(string.IsNullOrEmpty(error) ? "Assertion failed." : error);
        }
    }
}
