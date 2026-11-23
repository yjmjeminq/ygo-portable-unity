using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                var story = new StoryModeUI(app, canvas.transform);
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
                Require(line.text.Length == 0, "The second advance did not move to the next line.");
                Control(scene, "DialogueBox/Previous").onClick.Invoke();
                Require(line.text.Length > 0, "Previous did not restore the last line.");
                Control(scene, "DialogueBox/Log").onClick.Invoke();
                Require(story.ActiveRoot.name == "Backlog", "The backlog did not take focus.");
                story.Back();
                Control(scene, "DialogueBox/Skip").onClick.Invoke();
                Require(scene.Find("Choices").gameObject.activeInHierarchy && story.ActiveRoot == dialogue.gameObject,
                    "Skipping the conversation did not leave the player at the choices.");
                Control(scene, "Choices/Choice_1").onClick.Invoke();
                var pick = dialogue.Find("DeckSelection/Window");
                Require(Control(pick, "Apply").IsInteractable(), "The popup did not allow choosing a deck.");
                pick.Find("Decks/StoryDeck").GetComponent<Dropdown>().value = 1;
                Control(pick, "Apply").onClick.Invoke();
                Require(scene.Find("Choices/Summary/Label").GetComponent<Text>().text.Contains(second.name),
                    "Changing decks lost the conversation or kept the old deck.");
                Control(scene, "Choices/Choice_2").onClick.Invoke();
                Require(!scene.Find("Choices").gameObject.activeSelf && line.text.Length == 0, "Replay did not return to the first unread line.");
                story.Back();
                story.Close();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(events);
                UnityEngine.Object.DestroyImmediate(canvas);
            }
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
                        LotdUiKit.LoadOpponentPortrait(opponent) != null &&
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
                }
            }
            Require(ids.Count == 300 && raids == 40, "Some episodes were not included.");
            Require(LotdUiKit.LoadSprite("Story/Protagonist") != null, "Missing standing player artwork.");
        }

        private static void VerifyOpeningAndSave(string save)
        {
            var raid = StoryRaid.For("arcv", 44).Value;
            const uint normal = 20721928;
            var deck = Enumerable.Repeat(normal, 40).ToArray();
            foreach (bool humanFirst in new[] { true, false })
            {
                using var duel = new DuelSession();
                Require(duel.StartNewDuel(deck, Array.Empty<uint>(), deck, Array.Empty<uint>(), humanFirst,
                    seed: new ulong[] { 222, 17, 821, 909 }, opponentOpening: raid.Opening), "Could not start the raid.");
                var boss = duel.Players[duel.AiPlayerIndex];
                Require(boss.lp == 30000 && boss.hand.Count == 10 && duel.Players[duel.HumanPlayerIndex].lp == 8000 &&
                    boss.mzone.Any(card => card?.code == raid.Opening.FieldCard && card.IsFaceUp),
                    "The boss opening was not applied to the engine seat.");
            }

            var opponent = OpponentDeckLevels.GetDecks(74)[^1];
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

        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
    }
}
