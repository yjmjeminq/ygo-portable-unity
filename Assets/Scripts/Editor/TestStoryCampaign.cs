using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.AI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static partial class TestStoryCampaign
    {
        public static void Run()
        {
            string streaming = Application.streamingAssetsPath;
            string save = Path.Combine(Path.GetTempPath(), "ygo-story-" + Guid.NewGuid().ToString("N"));
            int exit = 1;
            var nativeErrors = new List<string>();
            try
            {
                CardDatabase.Load(streaming);
                DeckRepository.Load(streaming, save, seedDefaultDecks: false);
                CardArtLoader.Initialize(streaming, Path.Combine(save, "Art"));
                CollectionProgress.Load(save);
                Require(CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out var starter, out string error), error);
                Require(YgoNativeBridge.Initialize(streaming, (type, message) =>
                {
                    if (type == 0) nativeErrors.Add(message);
                }) == 0, "The native engine did not start.");
                VerifyCampaigns();
                VerifyOpeningCardRemoval();
                nativeErrors.Clear();
                VerifyDmScenarioEffects();
                VerifyGxScenarioEffects();
                Verify5DsScenarioEffects();
                VerifyRpgCampaign(starter);
                VerifyZexalScenarioEffects();
                VerifyArcVScenarioEffects();
                VerifyVrainsScenarioEffects();
                Require(nativeErrors.Count == 0, string.Join("\n", nativeErrors));
                VerifyDialogueChoices(starter);
                VerifyOpeningAndSave(save);
                VerifyRpgField(starter, save);
                Debug.Log("[VERIFY STORY] 180 episodes, scrolling, cast portraits, boss openings, rewards and saved campaign progress passed.");
                exit = 0;
            }
            catch (Exception error)
            {
                if (nativeErrors.Count != 0) Debug.LogError(string.Join("\n", nativeErrors.TakeLast(12)));
                Debug.LogException(error);
            }
            finally
            {
                CardArtLoader.Shutdown();
                YgoNativeBridge.Shutdown();
                if (Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }

        private static void VerifyRpgCampaign(UserDeck playerDeck)
        {
            string streaming = Application.streamingAssetsPath;
            var script = StoryModeUI.LoadRpgScript(streaming);
            var decks = StoryModeUI.LoadRpgDecks(streaming);
            Require(script?.episodes.Length == 50 && decks.Count == 50, "The entire RPG campaign did not load.");
            var canvas = new GameObject("RPG campaign verification", typeof(Canvas));
            var events = new GameObject("RPG input verification", typeof(EventSystem));
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
            try
            {
                DeckRepository.SaveUserDeck(playerDeck);
                var story = new StoryModeUI(canvas.AddComponent<GameAppController>(), canvas.transform);
                story.UseRpgCampaign(true);
                story.Open();
                var root = story.Root.transform;
                var rows = root.GetComponentsInChildren<ScrollRect>().Single().content;
                Require(rows.childCount == 50 && rows.Find("Episode_50").gameObject.activeInHierarchy,
                    "Episode 50 was missing from the RPG selection.");

                var loaded = (StoryScriptRoot)typeof(StoryModeUI).GetField("_rpgScript",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(story);
                for (int i = 0; i < loaded.episodes.Length; i++)
                {
                    var episode = loaded.episodes[i];
                    var match = story.GetOpponentMatch(episode).Deck;
                    Require(match?.id == 59001 + i && match.charId == episode.charId,
                        $"Wrong deck for RPG episode {i + 1}.");
                    Require(match.main.Length is >= 40 and <= 60 && match.extra.Length <= 15 && match.side.Length <= 15,
                        $"Invalid deck size in 5D's RPG deck {match.id}.");
                    var cards = match.main.Concat(match.extra).Concat(match.side).ToArray();
                    Require(cards.Contains(match.sigCard), $"The signature card is absent from 5D's RPG deck {match.id}.");
                    foreach (uint code in cards)
                    {
                        var card = CardDatabase.Get(code);
                        Require(card != null && YgoNativeBridge.YGO_HasCard(code) != 0 &&
                            (card.type & (YgoConstants.TYPE_XYZ | YgoConstants.TYPE_PENDULUM |
                            YgoConstants.TYPE_LINK | YgoConstants.TYPE_TOKEN)) == 0,
                            $"Illegal or unavailable 5D's RPG card {code} in deck {match.id}.");
                    }
                    var main = match.main.Select(CardDatabase.Get).ToArray();
                    var extra = match.extra.Select(CardDatabase.Get).ToArray();
                    Require(main.All(card => !card.IsExtra) && extra.All(card => card.IsExtra),
                        $"A card is in the wrong zone in 5D's RPG deck {match.id}.");
                    if (extra.Any(card => card.IsSynchro))
                    {
                        Require(main.Any(card => card.IsMonster && (card.type & YgoConstants.TYPE_TUNER) != 0) &&
                            main.Any(card => card.IsMonster && (card.type & YgoConstants.TYPE_TUNER) == 0),
                            $"Missing Synchro materials in 5D's RPG deck {match.id}.");
                    }
                    var raid = story.GetRaid(episode);
                    var opening = default(StoryBattlePlan).Apply(raid, match, episode.gimmick);
                    using var duel = new DuelSession();
                    Require(duel.StartNewDuel(playerDeck.main, playerDeck.extra, match.main, match.extra,
                        seed: new ulong[] { 21, 906, 711, 399 }, storyGimmick: episode.gimmick,
                        playerOpening: opening.Player, opponentOpening: opening.Opponent),
                        $"RPG episode {i + 1} could not start.");
                    if (i is 6 or 26)
                    {
                        var pilot = new TacticalAI { Difficulty = AIDifficulty.Easy };
                        for (int step = 0; step < 30 && !InMain(duel, 1) && !duel.IsFinished && duel.RetryCount == 0; step++)
                        {
                            if (duel.ActivePrompt is { } prompt) duel.SubmitResponse(pilot.ComputeResponse(duel, prompt));
                            else duel.StepUntilPrompt();
                        }
                        Require(InMain(duel, 1) && duel.RetryCount == 0,
                            $"RPG episode {i + 1} did not resolve its startup prompts.");
                    }
                    var rival = duel.Players[1];
                    if (StoryGimmickRuntime.IsRidingDuel(episode.gimmick))
                        Require(story.RidingMachineFor(episode) == "스트라이크 팽" &&
                            duel.Players.All(seat => seat.szone[5]?.code == StoryGimmickRuntime.SpeedWorld) &&
                            duel.Players[0].hand.Count == 5 && rival.hand.Count == opening.Opponent.Hand,
                            $"Riding duel {i + 1} had the wrong machine, field or hand.");
                    if (i == 6)
                        Require(duel.Players[0].mzone.Count(card => card?.code == StoryGimmickRuntime.HandcuffToken) == 2,
                            "The prison episode did not begin with both handcuffs.");
                    if (i == 26)
                        Require(rival.mzone.Any(card => card?.code == 39823987) &&
                            rival.grave.Any(card => card.code == 66818682), "The Godwin raid did not prepare Inti and Quilla.");
                    if (raid.HasValue && episode.raidCard != 0)
                        Require(rival.mzone.Concat(rival.szone).Any(card => card?.code == episode.raidCard),
                            $"The RPG raid had no opening ace in episode {i + 1}.");
                    foreach (var (code, _) in StoryGimmickRuntime.StartingCards(episode.gimmick))
                        Require(rival.mzone.Concat(rival.szone).Any(card => card?.code == code),
                            $"Missing starting card {code} for episode {i + 1}.");
                }
                var dialogue = new StoryModeUI.StoryDialogueView(canvas.transform);
                var scene = canvas.transform.Find("StoryDialogue/Scene");
                var cutscene = scene.Find("CutsceneOverlay").GetComponent<CanvasGroup>();
                var portraits = scene.Find("Portraits").GetComponent<CanvasGroup>();
                var badge = scene.Find("DialogueBox/SpeakerBadge").gameObject;
                var cg = cutscene.transform.Find("CG").GetComponent<Image>();
                var body = scene.Find("DialogueBox/Text/Label").GetComponent<Text>();
                var caret = scene.Find("DialogueBox/AdvanceCaret/Label").GetComponent<Text>();
                var next = scene.Find("DialogueBox/Next/Button").GetComponent<Button>();
                var playerPortrait = portraits.transform.Find("Protagonist").GetComponent<Image>();
                var speakerPortrait = portraits.transform.Find("Opponent").GetComponent<Image>();
                var speakerReaction = speakerPortrait.transform.Find("Reaction/Label").GetComponent<Text>();
                try
                {
                    var cgLines = loaded.episodes.SelectMany(episode =>
                        episode.preDialogues.Concat(episode.postWinDialogues).Concat(episode.postLoseDialogues))
                        .Where(line => !string.IsNullOrEmpty(line.cutscene)).ToArray();
                    Require(cgLines.Length == 10 && cgLines.Select(line => line.cutscene).Distinct().Count() == 7,
                        "The authored RPG cutscenes were missing.");
                    foreach (var line in cgLines)
                    {
                        dialogue.Open("컷씬 확인", "카이", DeckRepository.GetCharacter(1),
                            new[]
                            {
                                new StoryDialogueLine { speaker = line.speaker, text = new string('가', 40), cutscene = line.cutscene },
                                new StoryDialogueLine { speaker = "이자요이 아키", text = "<b>고마워.</b>", emotion = "blush" },
                                new StoryDialogueLine { speaker = "카이", text = "그래." },
                                new StoryDialogueLine { speaker = "설명", text = "바람이 스쳤다." },
                            }, () => { }, rpg: true);
                        var restingPortrait = speakerPortrait.sprite;
                        Require(!badge.activeSelf && playerPortrait.enabled && speakerPortrait.enabled &&
                            scene.Find("ProtagonistSD") == null && scene.Find("OpponentSD") == null,
                            "Narration lost the two stage portraits or displayed an SD character inside the panel.");
                        Require(body.supportRichText && Mathf.Approximately(body.lineSpacing, 1.25f) &&
                            body.font == Resources.Load<Font>("StoryFonts/Pretendard-Medium") &&
                            scene.Find("DialogueBox").GetComponent<Image>() is { type: Image.Type.Sliced } panel &&
                            panel.sprite.name == "RPG panel" &&
                            badge.GetComponentInChildren<Text>(true).font == Resources.Load<Font>("StoryFonts/Pretendard-SemiBold") &&
                            body.GetComponent<Shadow>().effectDistance == new Vector2(1.5f, -1.5f) &&
                            body.color == new Color(.92f, .93f, .96f) && !caret.enabled &&
                            cg.GetComponent<AspectRatioFitter>().aspectMode == AspectRatioFitter.AspectMode.FitInParent,
                            "RPG dialogue typography or the cutscene fit did not match the visual novel layout.");
                        dialogue.Update(.2f);
                        Require(Mathf.Approximately(cutscene.alpha, .5f) && Mathf.Approximately(portraits.alpha, .5f),
                            "The midpoint of the CG fade was incorrect.");
                        dialogue.Update(.2f);
                        Require(cutscene.alpha == 1f && portraits.alpha == 0f && cg.sprite != null,
                            "The CG did not cover the scene.");
                        next.onClick.Invoke();
                        next.onClick.Invoke();
                        dialogue.Update(.05f);
                        Require(body.text == "<b>고</b>" && !caret.enabled,
                            "Typing leaked rich-text markup or showed the advance caret too soon.");
                        dialogue.Update(.35f);
                        Require(cutscene.alpha == 0f && portraits.alpha == 1f && badge.activeSelf &&
                            playerPortrait.enabled && speakerPortrait.enabled &&
                            caret.enabled && body.text == "<b>고마워.</b>" &&
                            badge.GetComponentInChildren<Text>().color == (Color)new Color32(0xFF, 0x40, 0x81, 255) &&
                            speakerPortrait.sprite != restingPortrait && !speakerReaction.enabled &&
                            speakerPortrait.color.g > .98f && speakerPortrait.color.b > .98f,
                            "The authored blush did not return cleanly after fading out the CG.");
                        next.onClick.Invoke();
                        dialogue.Update(.4f);
                        Require(playerPortrait.enabled && speakerPortrait.enabled &&
                            playerPortrait.sprite.rect.height < playerPortrait.sprite.texture.height,
                            "Switching speakers did not retain the listening character.");
                        next.onClick.Invoke();
                        dialogue.Update(.4f);
                        Require(!badge.activeSelf && playerPortrait.enabled && speakerPortrait.enabled &&
                            playerPortrait.rectTransform.localScale.x < 1f && speakerPortrait.rectTransform.localScale.x < 1f,
                            "A narration line did not dim the two portraits.");
                    }
                    var fx = scene.GetComponent<StoryDialogueFx>();
                    var dialogueBox = (RectTransform)scene.Find("DialogueBox");
                    var flash = scene.Find("DialogueFlash").GetComponent<Image>();
                    var rest = dialogueBox.anchoredPosition;
                    fx.DrawFrame("shake", .125f);
                    Require(dialogueBox.anchoredPosition != rest, "Shake did not move the text box.");
                    fx.ResetEffect();
                    fx.DrawFrame("flash", .25f);
                    Require(flash.color.a > 0f && flash.color.r == flash.color.g, "Flash did not fill the scene in white.");
                    fx.DrawFrame("heartbeat", .125f);
                    float firstPulse = flash.color.a;
                    fx.DrawFrame("heartbeat", .375f);
                    Require(firstPulse > 0f && flash.color.a == 0f, "Heartbeat did not pulse.");
                    fx.DrawFrame("heartbeat", .625f);
                    Require(flash.color.a > 0f && flash.color.r > flash.color.g, "The second pink heartbeat pulse was missing.");
                    fx.ResetEffect();
                }
                finally { dialogue.Close(); }
                root.Find("StoryBrowser/EpisodeDetails/StartEpisode/Button").GetComponent<Button>().onClick.Invoke();
                var preparation = root.Find("StoryDialogue");
                var preparationScene = preparation.Find("Scene");
                var skip = preparationScene.Find("DialogueBox/Skip/Button").GetComponent<Button>();
                skip.onClick.Invoke();
                preparationScene.Find("Choices/Choice_0/Button").GetComponent<Button>().onClick.Invoke();
                skip.onClick.Invoke();
                var picker = preparation.Find("DeckSelection");
                Require(picker.gameObject.activeSelf &&
                    picker.GetComponentInChildren<Dropdown>().options.Any(option => option.text == playerDeck.name),
                    "The RPG preparation did not offer the player's saved deck.");
                picker.Find("Window/Apply/Button").GetComponent<Button>().onClick.Invoke();
                story.UseRpgCampaign(false);
                story.Open();
                Require(!rows.Find("Episode_50").gameObject.activeSelf,
                    "Returning to the original campaign left the RPG episodes on screen.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
                UnityEngine.Object.DestroyImmediate(events);
            }
            Debug.Log("[VERIFY 5DS RPG] Fifty episodes, dedicated decks, riding openings, portrait transitions and all seven CG assets passed.");
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
                var browser = root.Find("StoryBrowser");
                var scroll = browser.Find("Episodes").GetComponent<ScrollRect>();
                Require(scroll.vertical && !scroll.horizontal && scroll.content.childCount == StoryCampaign.EpisodeCount &&
                    browser.Find("Chapter_1") == null, "The campaign did not expose a single 30-episode list.");
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 0f;
                var finale = Control(scroll.content, "Episode_30");
                Require(finale.GetComponentInChildren<Text>().text.TrimStart().StartsWith("30화"),
                    "Scrolling to the end did not expose episode thirty.");
                finale.onClick.Invoke();
                var detail = browser.Find("EpisodeDetails");
                Require(detail.Find("OpponentName/Label").GetComponent<Text>().text.Contains("어둠의 유우기") &&
                    detail.Find("OpponentDeck/Label").GetComponent<Text>().text.Contains("Lv.") &&
                    !Control(detail, "StartEpisode").IsInteractable(), "Selecting a distant locked episode did not refresh its profile and deck.");
                var finalePortrait = DuelUiKit.LoadSprite("Duel/Characters/104");
                Require(finalePortrait != null && detail.Find("OpponentPortrait").GetComponent<Image>().sprite == finalePortrait,
                    "The episode detail did not show Yugi's character portrait.");
                scroll.verticalNormalizedPosition = 1f;
                Control(scroll.content, "Episode_1").onClick.Invoke();
                Control(detail, "StartEpisode").onClick.Invoke();
                var scene = dialogue.Find("Scene");
                Require(scene.Find("Portraits/Opponent").GetComponent<Image>().sprite ==
                    detail.Find("OpponentPortrait").GetComponent<Image>().sprite,
                    "The dialogue did not use the selected episode's character portrait.");
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
            foreach (string series in new[] { "dm", "gx", "5ds", "zexal", "arcv", "vrains" })
            {
                var script = StoryModeUI.LoadScript(Application.streamingAssetsPath, series);
                Require(script?.episodes.Length == StoryCampaign.EpisodeCount &&
                    script.episodes.Count(episode => !string.IsNullOrEmpty(episode.gimmick)) >= 18 &&
                    script.episodes[^1].raidCard > 0, $"Incomplete lineup for {series}.");
                for (int i = 0; i < script.episodes.Length; i++)
                {
                    var episode = script.episodes[i];
                    var opponent = DeckRepository.GetCharacter(episode.charId);
                    Require(episode.id == StoryCampaign.EpisodeId(series, i + 1) && ids.Add(episode.id) &&
                        opponent != null && !string.IsNullOrWhiteSpace(episode.title) &&
                        DuelUiKit.LoadOpponentPortrait(opponent) != null &&
                        OpponentDeckLevels.GetDecks(episode.charId).Any(deck => deck.main?.Length >= 40),
                        $"Missing cast or decks for {episode.id}.");
                    var levels = OpponentDeckLevels.GetDecks(episode.charId);
                    bool raid = episode.raidCard != 0 || episode.raidLife != 0;
                    int assignedLevel = StoryModeUI.SelectDeckLevel(script.episodes, i, levels.Count, raid);
                    Require(assignedLevel >= 1 && assignedLevel <= levels.Count &&
                        levels[assignedLevel - 1].charId == episode.charId &&
                        (i != script.episodes.Length - 1 || assignedLevel == levels.Count),
                        $"The runtime level assignment is invalid for {episode.id}.");
                    var scripted = StoryGimmickRuntime.StartingCards(episode.gimmick);
                    if (!((series is "gx" or "5ds" or "zexal" or "arcv" or "vrains") && !string.IsNullOrEmpty(episode.gimmick)) &&
                        episode.raidCard == 0 && episode.raidLife == 0 && scripted.Count == 0 &&
                        !StoryGimmickRuntime.IsRidingDuel(episode.gimmick)) continue;
                    StoryRaid? boss = episode.raidCard != 0 || episode.raidLife != 0 ?
                        StoryRaid.ForEncounter(i + 1, episode.raidCard, episode.raidLife) : null;
                    if (episode.raidCard != 0)
                    {
                        var card = CardDatabase.Get(episode.raidCard);
                        Require(card != null && (card.IsMonster ||
                            (card.type & (YgoConstants.TYPE_FIELD | YgoConstants.TYPE_CONTINUOUS)) != 0) &&
                            boss.Value.Opening.Hand == 5 + episode.chapter && boss.Value.WinDp > CollectionProgress.WinReward,
                            $"Invalid raid conditions for {episode.id}.");
                    }
                    var rival = levels[assignedLevel - 1];
                    VerifyNativeOpening(episode.id, boss, rival.main, rival.extra, humanFirst: true, gimmick: episode.gimmick);
                }
            }
            Require(ids.Count == 6 * StoryCampaign.EpisodeCount, "Some episodes were not included.");
            var legacy = Enumerable.Range(0, StoryCampaign.LegacyEpisodeCount)
                .Select(i => new StoryEpisodeData { charId = i is 0 or 25 or 49 ? 104 : 94 }).ToArray();
            Require(StoryModeUI.SelectDeckLevel(legacy, 0, 10, false) == 1 &&
                StoryModeUI.SelectDeckLevel(legacy, 25, 10, false) is > 1 and < 10 &&
                StoryModeUI.SelectDeckLevel(legacy, legacy.Length - 1, 10, true) == 10,
                "A legacy 50-episode progression did not retain its opening, rematch, and finale levels.");
            Require(DuelUiKit.LoadSprite("Story/Protagonist") != null, "Missing standing player artwork.");
        }

        private static void VerifyOpeningAndSave(string save)
        {
            const int encounter = 24;
            string episodeId = StoryCampaign.EpisodeId("arcv", encounter);
            var raid = StoryRaid.ForEncounter(encounter, 13331639, 10000);
            var opponent = OpponentDeckLevels.GetDecks(74)[^1];
            VerifyNativeOpening(episodeId, raid, opponent.main, opponent.extra, humanFirst: false);
            int before = CollectionProgress.Balance;
            Require(CollectionProgress.TryAwardStoryDuel(episodeId, true, raid, opponent, out int gained,
                out var cardReward, out string error), error);
            Require(gained == raid.WinDp && CollectionProgress.Balance == before + gained &&
                cardReward is { } pull && CardRarityCatalog.GetRarity(pull.Code) >= CardRarity.SuperRare,
                "The boss clear did not grant its DP and high rarity card.");
            Require(CollectionProgress.TryAwardStoryDuel(episodeId, true, raid, opponent, out _, out var repeat, out error) &&
                !repeat.HasValue, "A repeat win gave a second first-clear bonus.");
            CollectionProgress.Load(save);
            Require(CollectionProgress.IsStoryEpisodeCleared(episodeId) &&
                !CollectionProgress.IsStoryEpisodeCleared(StoryCampaign.EpisodeId("vrains", encounter)),
                "Campaign progress did not survive an independent reload.");
            Require(!DimensionTournament.Unlocked, "The new finale was cleared prematurely.");
            Require(CollectionProgress.TryAwardStoryDuel(StoryCampaign.LegacyEpisodeId("dm", 50), true,
                null, null, out _, out _, out error) &&
                CollectionProgress.IsStoryEpisodeCleared(StoryCampaign.EpisodeId("dm", 30)) && DimensionTournament.Unlocked,
                "The legacy finale did not preserve the campaign and tournament unlock.");
        }

        private static void VerifyNativeOpening(string name, StoryRaid? raid, uint[] rival, uint[] extra, bool humanFirst, string gimmick = null)
        {
            var player = Enumerable.Repeat(20721928u, 40).ToArray();
            using var duel = new DuelSession();
            Require(duel.StartNewDuel(player, Array.Empty<uint>(), rival, extra ?? Array.Empty<uint>(), humanFirst,
                seed: new ulong[] { 222, 17, 821, 909 }, opponentOpening: raid?.Opening, storyGimmick: gimmick),
                $"Could not start the raid {name}.");
            if (humanFirst && (name.StartsWith("gx_", StringComparison.Ordinal) || name.StartsWith("5ds_", StringComparison.Ordinal) ||
                name.StartsWith("zexal_", StringComparison.Ordinal) || name.StartsWith("arcv_", StringComparison.Ordinal) ||
                name.StartsWith("vrains_", StringComparison.Ordinal)))
            {
                var ai = new TacticalAI { Difficulty = AIDifficulty.Easy };
                for (int step = 0; step < 30 && !InMain(duel, 1) && !duel.IsFinished && duel.RetryCount == 0; step++)
                {
                    var prompt = duel.ActivePrompt;
                    if (prompt == null) duel.StepUntilPrompt();
                    else if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced) duel.SubmitResponseInt(-1);
                    else duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
                }
                Require(InMain(duel, 1) && duel.RetryCount == 0, $"Opening effects in {name} did not reach the first Main Phase.");
            }
            var boss = duel.Players[duel.AiPlayerIndex];
            int playerLife = gimmick switch { "heartland_cannon" => 7500, "numeronius" => 2000, _ => 8000 };
            bool riding = StoryGimmickRuntime.IsRidingDuel(gimmick);
            Require(boss.lp == (raid?.Opening.Life ?? 8000) &&
                boss.hand.Count == (raid?.Opening.Hand ?? 5) &&
                duel.Players[duel.HumanPlayerIndex].lp == playerLife,
                $"The configured LP, hand or opening field was missing in {name} (boss LP {boss.lp}, hand {boss.hand.Count}).");
            if (raid?.Opening.FieldCard is > 0)
                VerifyCard(raid.Value.Opening.FieldCard, YgoConstants.POS_FACEUP_ATTACK);
            foreach (var (code, position) in StoryGimmickRuntime.StartingCards(gimmick))
                VerifyCard(code, position);
            if (riding)
                Require(duel.Players.All(player => player.szone[5]?.code == StoryGimmickRuntime.SpeedWorld),
                    "The riding duel lacked Speed World 2 in a field zone.");

            void VerifyCard(uint code, uint position)
            {
                var info = CardDatabase.Get(code);
                var zone = info.IsMonster ? boss.mzone : boss.szone;
                var placed = zone.SingleOrDefault(card => card?.code == code);
                Require(placed?.position == position &&
                    ((info.type & YgoConstants.TYPE_FIELD) == 0 || placed.sequence == 5) &&
                    (StoryGimmickRuntime.OpeningMonsterSequence(gimmick, code) is not uint sequence || placed.sequence == sequence),
                    $"Wrong zone or position for {code} in {name}.");
            }
        }

        private static void VerifyOpeningCardRemoval()
        {
            AssertRemoval("jinzo", 77585513, 12580477, YgoConstants.LOCATION_MZONE, null);
            AssertRemoval("plasma", 83965310, 53129443, YgoConstants.LOCATION_MZONE, null);
            AssertRemoval("numeron_network", 41418852, 5318639, YgoConstants.LOCATION_SZONE, null);
            AssertRemoval("orichalcos", 48179391, 5318639, YgoConstants.LOCATION_SZONE, StoryRaid.ForEncounter(28, 48179391));
            Debug.Log("[VERIFY STORY OPENING CARDS] Real monster and field spell destruction, including Orichalcos's first protection, passed.");

            static void AssertRemoval(string gimmick, uint target, uint removal, uint location, StoryRaid? raid)
            {
                var player = Enumerable.Repeat(removal, 40).ToArray();
                var rival = Enumerable.Repeat(20721928u, 40).ToArray();
                var ai = new TacticalAI { Difficulty = AIDifficulty.Easy };
                using var duel = new DuelSession(recordPlanningReplay: true);
                Require(duel.StartNewDuel(player, Array.Empty<uint>(), rival, Array.Empty<uint>(),
                    seed: new ulong[] { 21, 906, 711, 399 }, opponentOpening: raid?.Opening, storyGimmick: gimmick),
                    $"Could not open {gimmick}.");
                var opponent = duel.Players[duel.AiPlayerIndex];
                var zone = location == YgoConstants.LOCATION_MZONE ? opponent.mzone : opponent.szone;
                Require(zone.Any(card => card?.code == target && card.IsFaceUp), $"{target} did not start in its native zone.");
                for (int step = 0; step < 35 && opponent.grave.All(card => card.code != target) && duel.RetryCount == 0; step++)
                {
                    var prompt = duel.ActivePrompt;
                    if (prompt == null) { duel.StepUntilPrompt(); continue; }
                    if (prompt.playerId == duel.HumanPlayerIndex && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                    {
                        int index = prompt.activatable.FindIndex(item => item.code == removal);
                        if (index >= 0) { duel.SubmitResponseInt((index << 16) | 5); continue; }
                    }
                    if (prompt.messageType == YgoConstants.MSG_SELECT_CARD)
                    {
                        int index = prompt.candidates.FindIndex(item => item.code == target);
                        if (index >= 0) { duel.SubmitResponse(DuelSession.PackCardSelection(index)); continue; }
                    }
                    duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
                }
                Require(opponent.grave.Any(card => card.code == target) && duel.RetryCount == 0,
                    $"{target} could not be destroyed by the native effect in {gimmick}.");
            }
        }

        private static void VerifyDmScenarioEffects()
        {
            using (var forest = Start("insect_forest"))
            {
                var beetle = forest.Players[forest.AiPlayerIndex].mzone.Single(card => card?.code == 89091579);
                Require(beetle.attack == 650 && forest.Players[forest.HumanPlayerIndex].deckCount == 36,
                    "Forest did not give the insect exactly 30 percent ATK or infiltrate the deck.");
            }
            using (var parasite = Start("insect_forest", playerDeckSize: 6))
            {
                Advance(parasite, () => parasite.TurnNumber == 5 &&
                    parasite.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD);
                var player = parasite.Players[parasite.HumanPlayerIndex];
                Require(player.lp == 7000 && player.mzone.Any(card => card is { code: 27911549 } && card.IsDefense),
                    "Drawing the infiltrating Parasite did not summon it in defense or deal 1000 damage.");
            }
            const uint submarineroid = 99861526;
            using (var openSea = Start(null, submarineroid, humanFirst: false))
            {
                EnterBattle(openSea);
                Require(openSea.ActivePrompt.attackable.Any(card => card.code == submarineroid && card.directAttackable),
                    "The battle control did not offer a direct attack.");
            }
            using (var ambush = Start("ocean_ambush", submarineroid, humanFirst: false))
            {
                EnterBattle(ambush);
                Require(ambush.ActivePrompt.attackable.Any(card => card.code == submarineroid && !card.directAttackable),
                    "The underwater ambush did not prevent the direct attack.");
            }
            using (var curtain = Start("darkness_curtain", humanFirst: false))
            {
                EnterBattle(curtain);
                Require(curtain.ActivePrompt.attackable.Count == 0,
                    "The curtain allowed the player to attack the opponent's set monster.");
            }
            using (var zombies = Start("undead_renewal", 12580477))
            {
                PlaySpell(zombies, 12580477);
                var rival = zombies.Players[zombies.AiPlayerIndex];
                Require(rival.mzone.Any(card => card is { code: 20277860, attack: 1650 }) &&
                    rival.grave.All(card => card.code != 20277860), "The destroyed Zombie was not revived with its 10 percent bonus.");
            }
            using (var machines = Start("machine_immunity", 12580477))
            {
                PlaySpell(machines, 12580477);
                Require(machines.Players[machines.AiPlayerIndex].mzone.Any(card => card?.code == 3797883),
                    "Raigeki destroyed the spell-immune Machine.");
            }
            using (var eyes = Start("toon_mind_read"))
                Require(eyes.IsKnownInOpposingHand((byte)eyes.AiPlayerIndex, 20721928),
                    "The Millennium Eye did not reveal the player's cards to the opponent.");
            foreach (string rule in new[] { "exodia_draw", "slifer_draw" })
            {
                using var duel = Start(rule);
                Advance(duel, () => duel.TurnNumber == 2 && duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD);
                Require(duel.Players[duel.AiPlayerIndex].hand.Count == 7, $"{rule} did not draw two cards on the opponent's turn.");
                if (rule == "slifer_draw")
                {
                    Advance(duel, () => duel.TurnNumber >= 3);
                    Require(duel.Players[duel.AiPlayerIndex].hand.Count == 7, "Slifer's hand limit discarded a seventh card.");
                }
            }
            using (var circus = Start("circus_saw"))
            {
                Require(circus.Players[circus.HumanPlayerIndex].lp == 7700, "The first saw hit missed the player.");
                Advance(circus, () => circus.TurnNumber == 2 && circus.CurrentPhase == YgoConstants.PHASE_MAIN1);
                Require(circus.Players[circus.HumanPlayerIndex].lp == 7400 &&
                    circus.Players[circus.AiPlayerIndex].lp == 8000, "The saw did not hit once per turn on the correct side.");
            }
            using (var prophecy = Start("ishtar_foresight"))
                Require(prophecy.Logs.Any(log => log.Contains("덱 위 확인") && log.Split('[').Length >= 4),
                    "Ishizu did not reveal the three cards before the draw.");
            using (var shadow = Start("shadow_game", rivalCard: 46918794, humanFirst: false))
            {
                PlaySpell(shadow, 46918794);
                var player = shadow.Players[shadow.HumanPlayerIndex];
                Require(player.lp == 7000 && player.banished.Count == 1 && !player.banished[0].IsFaceUp &&
                    player.deckCount == 34 && shadow.Players[shadow.AiPlayerIndex].banished.Count == 0,
                    $"The Shadow Game did not banish one card from the damaged player's deck (LP {player.lp}, " +
                    $"deck {player.deckCount}, banished {player.banished.Count}): " + string.Join(" / ", shadow.Logs.TakeLast(14)));
            }
            using (var timer = Start("voodoo_countdown", humanFirst: false))
            {
                Advance(timer, () => timer.IsFinished);
                Require(timer.TurnNumber == 15 && timer.Winner == timer.AiPlayerIndex &&
                    timer.WinReason == StoryGimmickRuntime.CountdownWinReason, "The voodoo countdown was not enforced on turn 15.");
            }
            Debug.Log("[VERIFY DM SCENARIOS] Field strength, Parasite, revival, immunity, opponent knowledge, draw and hand limits, damage, reveal, banish and countdown passed.");

            static DuelSession Start(string rule, uint playerCard = 20721928, uint rivalCard = 20721928,
                bool humanFirst = true, int playerDeckSize = 40)
            {
                var duel = new DuelSession();
                Require(duel.StartNewDuel(Enumerable.Repeat(playerCard, playerDeckSize).ToArray(), Array.Empty<uint>(),
                    Enumerable.Repeat(rivalCard, 40).ToArray(), Array.Empty<uint>(), humanFirst,
                    seed: new ulong[] { 41, 32, 7, 11 }, storyGimmick: rule), $"Could not start {rule}.");
                return duel;
            }

            static void EnterBattle(DuelSession duel)
            {
                bool MainPhase() => duel.TurnNumber == 2 && duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD;
                Advance(duel, MainPhase);
                Require(duel.ActivePrompt.summonable.Count > 0, "Missing test attacker.");
                duel.SubmitResponseInt(0);
                Advance(duel, MainPhase);
                Require(duel.ActivePrompt.canBp, "The player could not enter the battle phase.");
                duel.SubmitResponseInt(6);
                Advance(duel, () => duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD);
            }

            static void PlaySpell(DuelSession duel, uint code)
            {
                var prompt = duel.ActivePrompt;
                Require(prompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD, "The duel did not reach the first main phase.");
                int index = prompt.activatable.FindIndex(choice => choice.code == code);
                Require(index >= 0, $"Could not activate test card {code}.");
                duel.SubmitResponseInt((index << 16) | 5);
                Advance(duel, () => duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD);
            }

            static void Advance(DuelSession duel, Func<bool> stop)
            {
                var ai = new TacticalAI { Difficulty = AIDifficulty.Easy };
                for (int step = 0; step < 150 && !stop() && !duel.IsFinished && duel.RetryCount == 0; step++)
                {
                    var prompt = duel.ActivePrompt;
                    if (prompt == null) { duel.StepUntilPrompt(); continue; }
                    if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && prompt.canEp) duel.SubmitResponseInt(7);
                    else if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced) duel.SubmitResponseInt(-1);
                    else duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
                }
                Require(stop() && duel.RetryCount == 0, $"The scenario checkpoint was missed on turn {duel.TurnNumber} " +
                    $"(phase {duel.CurrentPhase}, prompt {duel.ActivePrompt?.messageType}, retries {duel.RetryCount}): " +
                    string.Join(" / ", duel.Logs.TakeLast(16)));
            }
        }

        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
    }
}
