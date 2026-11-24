using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static class TestStoryCampaign
    {
        public static void Run()
        {
            string streaming = Application.streamingAssetsPath;
            string save = Path.Combine(Path.GetTempPath(), "ygo-story-" + Guid.NewGuid().ToString("N"));
            int exit = 1;
            try
            {
                CardDatabase.Load(streaming);
                DeckRepository.Load(streaming, save, seedDefaultDecks: false);
                CardArtLoader.Initialize(streaming, Path.Combine(save, "Art"));
                CollectionProgress.Load(save);
                Require(CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out var starter, out string error), error);
                Require(YgoNativeBridge.Initialize(streaming, null) == 0, "The native engine did not start.");
                VerifyCampaigns();
                VerifyDialogueChoices(starter);
                VerifyOpeningAndSave(save);
                Debug.Log("[VERIFY STORY] 300 episodes, cast portraits, boss openings, DP, high rarity rewards and separate saved campaign progress passed.");
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

        private static void VerifyDialogueChoices(UserDeck starter)
        {
            DeckRepository.SaveUserDeck(starter);
            var second = CollectionProgress.CopyDeck(starter);
            second.id = null;
            second.name = "스토리 테스트 덱";
            DeckRepository.SaveUserDeck(second);
            var canvas = new GameObject("Story Verification", typeof(Canvas));
            var events = new GameObject("Story Input", typeof(EventSystem));
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600, 900);
            var app = canvas.AddComponent<GameAppController>();
            try
            {
                var story = new StoryModeUI(app, canvas.transform, new StoryHandSequence(0, 0, 2));
                var root = story.Root.transform;
                Transform dialogue = root.Find("StoryDialogue");
                Button Control(Transform scope, string path) => scope.Find(path + "/Button").GetComponent<Button>();
                story.Open();
                foreach (string id in new[] { "gx", "5ds", "zexal", "arcv", "vrains", "dm" })
                {
                    var tab = Control(root.Find("StoryBrowser"), $"Series_{id}");
                    Require(tab.IsInteractable(), $"The {id} tab was disabled.");
                    tab.onClick.Invoke();
                }
                Control(root.Find("StoryBrowser"), "EpisodeDetails/StartEpisode").onClick.Invoke();
                var scene = dialogue.Find("Scene");
                var line = scene.Find("DialogueBox/Text/Label").GetComponent<Text>();
                Require(line.text.Length == 0, "The dialogue did not start in the typewriter state.");
                story.Update(.15f);
                int partial = line.text.Length;
                Control(scene, "DialogueBox/Next").onClick.Invoke();
                Require(partial > 0 && line.text.Length > partial, "The first advance did not finish revealing the current line.");
                Control(scene, "DialogueBox/Next").onClick.Invoke();
                var branch = Control(scene, "Choices/Choice_1");
                Require(branch.IsInteractable() && branch.GetComponentInChildren<Text>().text.Contains("한 장씩 읽고 준비"),
                    "The authored dialogue choice did not interrupt the conversation.");
                branch.onClick.Invoke();
                Require(line.text.Length == 0, "The branch did not move to the player's reply.");
                Control(scene, "DialogueBox/Next").onClick.Invoke();
                Control(scene, "DialogueBox/Next").onClick.Invoke();
                Control(scene, "DialogueBox/Next").onClick.Invoke();
                Require(line.text.Contains("그 태도가 중요하지"), "The chosen opponent response was not shown.");
                Control(scene, "DialogueBox/Previous").onClick.Invoke();
                Require(line.text.Length > 0, "Previous did not restore the last line.");
                Control(scene, "DialogueBox/Log").onClick.Invoke();
                Require(story.ActiveRoot.name == "Backlog", "The backlog did not take focus.");
                story.Back();
                Control(scene, "DialogueBox/Skip").onClick.Invoke();
                Require(scene.Find("Choices").gameObject.activeInHierarchy && story.ActiveRoot == dialogue.gameObject,
                    "Skipping the conversation did not leave the player at the choices.");
                Require(scene.Find("Choices/Summary/Label").GetComponent<Text>().text.Contains("내 LP 9,000"),
                    "The selected Calm stance did not change the preparation readout.");
                Control(scene, "Choices/Choice_4").onClick.Invoke();
                var support = dialogue.Find("RaidSupport/Window");
                Control(support, "Item_0/Buy").onClick.Invoke();
                Control(support, "Item_0/Equip").onClick.Invoke();
                Control(support, "Apply").onClick.Invoke();
                Require(scene.Find("Choices/Summary/Label").GetComponent<Text>().text.Contains("패 7장") &&
                    CollectionProgress.GetSupportCount(RaidSupport.QuickDraw) == 1,
                    "Buying and equipping an item from the story did not update preparation, or consumed it too early.");
                Control(scene, "Choices/Choice_1").onClick.Invoke();
                var pick = dialogue.Find("DeckSelection/Window");
                Require(Control(pick, "Apply").IsInteractable(), "The popup did not allow choosing a deck.");
                pick.Find("Decks/StoryDeck").GetComponent<Dropdown>().value = 1;
                Control(pick, "Apply").onClick.Invoke();
                Require(scene.Find("Choices/Summary/Label").GetComponent<Text>().text.Contains(second.name),
                    "Changing decks lost the conversation or kept the old deck.");
                var summary = scene.Find("Choices/Summary/Label").GetComponent<Text>();
                Button Choice(int index) => Control(scene, $"Choices/Choice_{index}");
                Require(Choice(5).IsInteractable() && Choice(5).GetComponentInChildren<Text>().text.Contains("상대 시작 조건"),
                    "The story preparation did not expose opening settings.");
                Choice(5).onClick.Invoke();
                Require(Choice(3).IsInteractable() && Choice(3).GetComponentInChildren<Text>().text.Contains("몬스터 선배치"),
                    "The player could not choose both opponent bonuses.");
                story.Back();
                Require(summary.text.Contains("상대 시작 패 5장"), "Backing out changed the existing opening setting.");
                Choice(5).onClick.Invoke();
                Choice(3).onClick.Invoke();
                Require(summary.text.Contains("상대 시작 패 7장") && summary.text.Contains("선배치 카드"),
                    "The preparation summary did not reflect the stronger opening.");
                Choice(0).onClick.Invoke();
                Require(summary.text.Contains("가위바위보") && Choice(1).GetComponentInChildren<Text>().text == "바위",
                    "A story duel bypassed the turn order chooser.");
                Choice(0).onClick.Invoke();
                Require(summary.text.Contains("무승부") && Choice(2).IsInteractable(), "A tied hand did not reopen the choices.");
                Choice(1).onClick.Invoke();
                Require(summary.text.Contains("승리") && Choice(0).GetComponentInChildren<Text>().text == "선공" &&
                    Choice(1).GetComponentInChildren<Text>().text == "후공", "A won hand did not let the player choose either order.");
                story.Back();
                Require(summary.text.Contains(second.name) && summary.text.Contains("상대 시작 패 7장") &&
                    summary.text.Contains("선배치 카드"), "Backing out of turn order discarded the selected opponent opening.");
                Choice(0).onClick.Invoke();
                Choice(1).onClick.Invoke();
                Require(summary.text.Contains("상대가 선공을 선택했습니다") || summary.text.Contains("상대가 후공을 선택했습니다"),
                    "A lost hand did not show the opponent's turn choice.");
                story.Update(.4f);
                Require(summary.text.Contains("상대가"), "A lost hand skipped the turn-order result.");
                story.Update(.8f);
                Require(summary.text.Contains(second.name), "A lost hand did not proceed after the result display.");
                Control(scene, "Choices/Choice_2").onClick.Invoke();
                Require(!scene.Find("Choices").gameObject.activeSelf && line.text.Length == 0, "Replay did not return to the first unread line.");
                story.Back();
                var dm = (StoryScriptRoot)typeof(StoryModeUI).GetProperty("Script", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(story);
                var first = dm.episodes[0];
                story.ShowDuelResult(first, false, false, "+150 DP",
                    new StoryBattlePlan(StoryStance.Calm, RaidSupport.None, StoryOpponentOpening.ExtraHandAndMonster));
                Control(scene, "DialogueBox/Skip").onClick.Invoke();
                Require(Control(scene, "Choices/Choice_0").GetComponentInChildren<Text>().text.Contains("재도전") &&
                    Control(scene, "Choices/Choice_1").GetComponentInChildren<Text>().text.Contains("덱 변경"),
                    "The loss result did not expose retry and deck change.");
                Choice(4).onClick.Invoke();
                Require(summary.text.Contains("상대 시작 패 7장") && summary.text.Contains("선배치 카드"),
                    "The rematch discarded the last opponent opening.");
                story.Back();
                Choice(0).onClick.Invoke();
                Require(summary.text.Contains("가위바위보"), "A story rematch bypassed the turn order chooser.");
                story.Back();
                Require(Choice(0).GetComponentInChildren<Text>().text.Contains("재도전"),
                    "Backing out of a rematch did not restore the result options.");
                Debug.Log("[VERIFY STORY TURN ORDER] Draw, choice after a win, automatic loss result, cancel and replay passed.");
                story.Back();
                Require(CollectionProgress.TryAwardStoryDuel(first.id, true, null, null, out _, out _, out string error), error);
                story.Open();
                story.ShowDuelResult(first, true, false, "+300 DP");
                Control(scene, "DialogueBox/Skip").onClick.Invoke();
                var next = Control(scene, "Choices/Choice_0");
                Require(next.IsInteractable() && next.GetComponentInChildren<Text>().text.Contains("다음 에피소드"),
                    "The win result did not expose the next episode.");
                next.onClick.Invoke();
                Require(scene.Find("Episode/Label").GetComponent<Text>().text == dm.episodes[1].title,
                    "The next-episode choice did not begin the newly unlocked episode.");
                story.Close();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(events);
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        private sealed class StoryHandSequence : System.Random
        {
            private readonly Queue<int> _hands;

            internal StoryHandSequence(params int[] hands) => _hands = new Queue<int>(hands);

            public override int Next(int maxValue) => _hands.Dequeue();
        }

        private static void VerifyCampaigns()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int raids = 0;
            foreach (string series in new[] { "dm", "gx", "5ds", "zexal", "arcv", "vrains" })
            {
                var script = StoryModeUI.LoadScript(Application.streamingAssetsPath, series);
                Require(script?.episodes.Length == 50, $"Missing episodes for {series}.");
                for (int i = 0; i < script.episodes.Length; i++)
                {
                    var episode = script.episodes[i];
                    var opponent = DeckRepository.GetCharacter(episode.charId);
                    Require(ids.Add(episode.id) && opponent != null && !string.IsNullOrWhiteSpace(episode.title) &&
                        DuelUiKit.LoadOpponentPortrait(opponent) != null &&
                        OpponentDeckLevels.GetDecks(episode.charId).Any(deck => deck.main?.Length >= 40),
                        $"Missing cast or decks for {episode.id}.");
                    var raid = StoryRaid.For(series, i + 1);
                    if (i % 10 == 9 || i == 48) Require(raid.HasValue, $"Missing chapter boss at {episode.id}.");
                    if (raid is not { } boss) continue;
                    raids++;
                    var card = CardDatabase.Get(boss.Opening.FieldCard);
                    Require(card != null && (card.IsMonster ||
                        (card.type & (YgoConstants.TYPE_FIELD | YgoConstants.TYPE_CONTINUOUS)) != 0) &&
                        boss.Opening.Hand == 5 + episode.chapter && boss.WinDp > CollectionProgress.WinReward,
                        $"Invalid raid conditions for {episode.id}.");
                    var rival = OpponentDeckLevels.GetDecks(episode.charId)[^1];
                    VerifyNativeOpening(episode.id, boss, rival.main, rival.extra, humanFirst: true);
                }
            }
            Require(ids.Count == 300 && raids == 40, "Some episodes were not included.");
            Require(DuelUiKit.LoadSprite("Story/Protagonist") != null, "Missing standing player artwork.");
        }

        private static void VerifyOpeningAndSave(string save)
        {
            var raid = StoryRaid.For("arcv", 44).Value;
            var opponent = OpponentDeckLevels.GetDecks(74)[^1];
            VerifyNativeOpening("arcv_ep_44", raid, opponent.main, opponent.extra, humanFirst: false);
            int before = CollectionProgress.Balance;
            Require(CollectionProgress.TryAwardStoryDuel("arcv_ep_44", true, raid, opponent, out int gained,
                out var cardReward, out string error), error);
            Require(gained == raid.WinDp && CollectionProgress.Balance == before + gained &&
                cardReward is { } pull && CardRarityCatalog.GetRarity(pull.Code) >= CardRarity.SuperRare,
                "The boss clear did not grant its DP and high rarity card.");
            Require(CollectionProgress.TryAwardStoryDuel("arcv_ep_44", true, raid, opponent, out _, out var repeat, out error) &&
                !repeat.HasValue, "A repeat win gave a second first-clear bonus.");
            CollectionProgress.Load(save);
            Require(CollectionProgress.IsStoryEpisodeCleared("arcv_ep_44") &&
                !CollectionProgress.IsStoryEpisodeCleared("vrains_ep_44"), "Campaign progress did not survive an independent reload.");
        }

        private static void VerifyNativeOpening(string name, StoryRaid raid, uint[] rival, uint[] extra, bool humanFirst)
        {
            var player = Enumerable.Repeat(20721928u, 40).ToArray();
            using var duel = new DuelSession();
            Require(duel.StartNewDuel(player, Array.Empty<uint>(), rival, extra ?? Array.Empty<uint>(), humanFirst,
                seed: new ulong[] { 222, 17, 821, 909 }, opponentOpening: raid.Opening), $"Could not start the raid {name}.");
            var boss = duel.Players[duel.AiPlayerIndex];
            Require(boss.lp == 8000 && boss.hand.Count == raid.Opening.Hand &&
                duel.Players[duel.HumanPlayerIndex].lp == 8000 && boss.mzone.Concat(boss.szone)
                    .Any(card => card?.code == raid.Opening.FieldCard && card.IsFaceUp),
                $"The configured LP, hand or opening field was missing in {name}.");
        }

        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
    }
}
