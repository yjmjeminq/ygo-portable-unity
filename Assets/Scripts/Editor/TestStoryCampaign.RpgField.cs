using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.AI;
using YGOPortable.Core;
using YGOPortable.UI;
using static YGOPortable.UI.TagForceWorldUI;

namespace YGOPortable.Editor
{
    internal static partial class TestStoryCampaign
    {
        private const string MapNodes = "Atlas/MapInset/MapArt/Nodes";

        private static void VerifyRpgField(string save)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            string streaming = Application.streamingAssetsPath;
            var canvas = new GameObject("GX field verification", typeof(Canvas));
            var events = new GameObject("GX field controls", typeof(EventSystem));
            var priorEvents = EventSystem.current;
            var input = events.GetComponent<EventSystem>();
            if (EventSystem.current != input) typeof(EventSystem).GetMethod("OnEnable", flags).Invoke(input, null);
            EventSystem.current = input;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
            var app = canvas.AddComponent<GameAppController>();
            app.AiDifficulty = AIDifficulty.Normal;
            var story = new StoryModeUI(app, canvas.transform, new StoryHandSequence(new int[40]));
            bool touchLayout = Environment.GetEnvironmentVariable("YGO_GX_QA_TOUCH") == "1";
            var world = new TagForceWorldUI(app, canvas.transform, touchFieldLayout: touchLayout);
            var menu = new TitleAndMenuUI(app, canvas.transform);
            var campaigns = new BeyondDestinyUI(app, canvas.transform);
            var modals = new DuelPromptModals(canvas.transform, null, null);
            var mobile = world.Root.transform.Find("MobileSafeArea")?.GetComponent<MobileSafeArea>();
            var panel = (mobile == null ? world.Root.transform : mobile.transform).Find("World");
            if (touchLayout) mobile.Reflow(new Rect(96, 42, 1758, 858), new Vector2Int(1950, 900));
            var storyRoot = story.Root.transform.Find("StoryDialogue");
            try
            {
                Set("_titleAndMenu", menu);
                Set("_beyondDestiny", campaigns);
                Set("_tagForceWorld", world);
                Set("_storyMode", story);
                Set("_archetypeCodex", new ArchetypeCodexUI(canvas.transform, () => { }));
                Set("_tournament", new DimensionTournamentUI(app, canvas.transform));
                Set("_ranked", new OfflineRankedUI(app, canvas.transform));
                Set("_oppSelect", new OpponentSelectUI(app, canvas.transform));
                Set("_deckEditor", new DeckEditorUI(app, canvas.transform));
                Set("_modals", modals);
                Set("_collectionShop", new CollectionShopUI(canvas.transform,
                    () => typeof(GameAppController).GetMethod("CloseCollectionShop", flags).Invoke(app, null), _ => { }));
                Set("_duelField", new DuelFieldUI(app, canvas.transform, modals));
                Set("_menuPad", new DeckPadUi(DuelUiKit.FillRect("RpgTestMenuPad", canvas.transform), null));
                Set("_initialized", true);
                VerifyGxCampaign();
            }
            finally
            {
                (Get("_session") as DuelSession)?.Dispose();
                world.Close();
                story.Close();
                UnityEngine.Object.DestroyImmediate(canvas);
                if (EventSystem.current == input) typeof(EventSystem).GetMethod("OnDisable", flags).Invoke(input, null);
                if (priorEvents != null) EventSystem.current = priorEvents;
                UnityEngine.Object.DestroyImmediate(events);
            }

            void VerifyGxCampaign()
            {
                const string gx = StoryModeUI.GxRpgSeriesId;
                const string gxPreference = "ygo.gx-rpg.last-region";
                string previousLocation = PlayerPrefs.GetString(gxPreference);
                bool hadLocation = PlayerPrefs.HasKey(gxPreference);
                string isolatedSave = Path.Combine(save, "gx-open-world");
                var gxScript = story.GetRpgScript(gx);
                var gxDecks = StoryModeUI.LoadRpgDecks(streaming, gx);
                var gxMap = TagForceData.LoadRpgConfiguration(streaming, gx);
                Require(gxScript?.episodes.Length == 53 && gxScript.episodes.Count(episode => episode.isCutsceneOnly) == 19 &&
                    gxDecks.Count == 37 && gxMap?.spots.Length == 12 && gxMap.roster.Length == 26,
                    "GX campaign inputs are incomplete.");
                Require(gxScript.protagonist == "윤", "The GX protagonist name did not load.");
                VerifyDialogueBounds(gxScript, "GX");
                var authoredSound = gxScript.episodes.SelectMany(episode => episode.preDialogues.Concat(episode.postWinDialogues)
                    .Concat(episode.postLoseDialogues)).Select(line => line.sfx).Where(cue => !string.IsNullOrEmpty(cue)).ToHashSet();
                Require(authoredSound.SetEquals(new[] { "duel_disk", "footstep", "crowd", "rain", "thunder",
                    "can_open", "door_knock", "shatter", "alarm" }), "GX story sound tags did not load.");
                foreach (string cue in authoredSound.Append("transition"))
                {
                    var clip = DuelAudio.GetStorySfx(cue);
                    Require(clip != null && clip.samples > 0 && DuelAudio.GetStorySfx(cue) == clip, $"Missing GX sound {cue}.");
                }
                Require(DuelAudio.GetStorySfx("unknown-cue") == null && DuelAudio.GetStorySfx(null) == null,
                    "An unknown GX cue should be ignored.");
                var dialogue = typeof(StoryModeUI).GetField("_dialogue", flags).GetValue(story);
                var dialogueType = typeof(StoryModeUI.StoryDialogueView);
                var audioType = typeof(DuelAudio);
                const BindingFlags audioFlags = BindingFlags.NonPublic | BindingFlags.Static;
                var audioSource = audioType.GetField("_bgmSource", audioFlags);
                var pausedMusic = audioType.GetField("_bgmPaused", audioFlags);
                var oldMusic = audioSource.GetValue(null);
                var oldPaused = pausedMusic.GetValue(null);
                float oldVolume = DuelAudio.BgmVolume;
                var gxAudio = new GameObject("GX field music verification", typeof(AudioSource));
                var bgm = gxAudio.GetComponent<AudioSource>();
                bgm.playOnAwake = false;
                audioSource.SetValue(null, bgm);
                pausedMusic.SetValue(null, false);
                DuelAudio.BgmVolume = 0f;
                Require(File.Exists(Path.Combine(streaming, gxMap.assetConfig.worldMapPath)), "Missing GX world map.");
                Require(gxMap.roster.All(actor => DeckRepository.GetCharacter(actor.charId) != null), "GX resident has no character entry.");
                int forbiddenTypes = (int)(YgoConstants.TYPE_SYNCHRO | YgoConstants.TYPE_XYZ |
                    YgoConstants.TYPE_PENDULUM | YgoConstants.TYPE_LINK | YgoConstants.TYPE_TOKEN);
                Require(gxDecks.Values.All(deck => deck.main.Concat(deck.extra).Concat(deck.side)
                    .All(code => CardDatabase.Get(code) is { } card && (card.type & (uint)forbiddenTypes) == 0 &&
                        YgoNativeBridge.YGO_HasCard(code) != 0)), "A GX deck contains missing cards or another era's summons.");
                var expectedCg = gxScript.episodes.SelectMany(episode => episode.preDialogues.Concat(episode.postWinDialogues)
                    .Concat(episode.postLoseDialogues)).Select(line => line.cutscene)
                    .Where(id => !string.IsNullOrEmpty(id)).Distinct().ToArray();
                Require(expectedCg.Length == 39 && expectedCg.All(id =>
                    File.Exists(Path.Combine(streaming, "TagForce/GX/Cutscenes", id + ".png"))), "Missing GX event CG.");

                try
                {
                    PlayerPrefs.DeleteKey(gxPreference);
                    DeckRepository.Load(streaming, isolatedSave, seedDefaultDecks: false);
                    CollectionProgress.Load(isolatedSave);
                    Require(CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out var startingDeck, out string error), error);
                    DeckRepository.SaveUserDeck(startingDeck);
                    var alternateDeck = CollectionProgress.CopyDeck(startingDeck);
                    alternateDeck.id = null;
                    alternateDeck.name = "GX QA · 교체 덱";
                    DeckRepository.SaveUserDeck(alternateDeck);
                    CollectionProgress.RememberDuelDeck(startingDeck.id);
                    app.ShowScreen(AppScreen.MainMenu);
                    Require(menu.MenuRoot.transform.Find("GxPreview/AcademyIsland")?.GetComponent<Image>()?.sprite != null,
                        "The bundled GX preview did not load in the main menu.");
                    CaptureGx("00-main-menu");
                    var gxMenu = menu.MenuRoot.GetComponentsInChildren<Button>()
                        .Single(button => button.transform.parent.name == "GxRpgWorld");
                    Require(menu.MenuRoot.transform.Find("Menu/StoryMode") == null &&
                        menu.MenuRoot.transform.Find("Menu/RpgWorld") == null && gxMenu.IsInteractable(),
                        "The main menu must have one entry for the story campaign picker.");
                    gxMenu.onClick.Invoke();
                    var campaignOptions = campaigns.Root.GetComponentsInChildren<Button>();
                    var gxChoice = campaignOptions.Single(button => button.GetComponentInChildren<Text>()?.text == "GX: 듀얼 아카데미아");
                    Require(campaignOptions.Any(button => button.GetComponentInChildren<Text>()?.text == "5D’s: Beyond Destiny"),
                        "The campaign picker lost its 5D's entry.");
                    gxChoice.onClick.Invoke();
                    Require(world.Root.activeSelf && world.RpgCampaignId == gx && !world.CanWalk, "GX prologue did not start automatically.");
                    VerifyGxOpeningInterlude(gxScript.episodes[0]);
                    int scenes = 0, duels = 0, pov = 0, defeats = 0, retries = 0;
                    for (int index = 0; index < gxScript.episodes.Length; index++)
                    {
                        var episode = gxScript.episodes[index];
                        Require(world.RpgClears == index, $"GX sequence skipped or stalled at {episode.id}.");
                        int location = world.CampaignRegionForEpisode(index + 1);
                        Require(world.CampaignRegion(location).Id == episode.regionId &&
                            world.CampaignRegion(location).IsOpen(index), $"Unreachable GX destination for {episode.id}.");
                        if (episode.isCutsceneOnly)
                        {
                            for (int frame = 0; frame < 18 && !storyRoot.gameObject.activeInHierarchy; frame++)
                                world.Update(.18f);
                            Require(storyRoot.gameObject.activeInHierarchy && !world.CanWalk && !WorldInputRestored() &&
                                FieldScene().Find("DialogueCast") != null && Get("_session") == null,
                                $"Automatic cutscene did not lock controls at {episode.id}.");
                            VerifyGxWalkerSizes();
                            Require(storyRoot.Find("Scene/DialogueTools/Auto/Button").GetComponentInChildren<Text>()
                                .text == "오토 꺼짐" && AutoSpeed() == 0, $"Autoplay started without permission at {episode.id}.");
                            VerifyDialogueStart(episode.preDialogues, episode.id);
                            if (index == 0)
                            {
                                story.Back();
                                Require(menu.MenuRoot.activeSelf && !world.Root.activeSelf && !storyRoot.gameObject.activeInHierarchy &&
                                    world.RpgClears == 0, "Esc restarted or cleared the mandatory GX prologue instead of leaving it.");
                                Require(EventSystem.current.currentSelectedGameObject == gxMenu.gameObject,
                                    "Leaving a GX story should return main menu focus to the campaign picker entry.");
                                gxMenu.onClick.Invoke();
                                gxChoice.onClick.Invoke();
                                for (int frame = 0; frame < 18 && !storyRoot.gameObject.activeInHierarchy; frame++)
                                    world.Update(.18f);
                                VerifyDialogueStart(episode.preDialogues, episode.id);
                                Require(FieldScene().Find("RpgCompanion") == null, "The GX prologue spawned a partner.");
                                VerifyGxPrologue(episode);
                                VerifyGxManualDialogue(episode);
                                VerifyGxAutoReadTime(episode);
                                story.Update(.75f);
                                CaptureGx("01-opening");
                                Press(storyRoot, "Scene/DialogueTools/Auto/Button");
                                Require(storyRoot.Find("Scene/DialogueTools/Auto/Button").GetComponentInChildren<Text>()
                                    .text == "오토 2x" && AutoSpeed() == 2, "The cutscene did not switch to double speed.");
                            }
                            if (episode.id == "gx_cutscene_02_season2_arrival")
                            {
                                VerifyGxGuestArt(episode.preDialogues, "세레나 (여성 프로)", 213);
                                dialogueType.GetMethod("BindLine", flags).Invoke(dialogue, new object[] { 0, false });
                            }
                            if (index == 0) VerifyGxSaveRecovery(index, () => ReadDialogue(episode));
                            else ReadDialogue(episode);
                            FinishGxInterlude();
                            Require(world.RpgClears == index + 1 && Get("_session") == null,
                                $"A cutscene did not complete at {episode.id}.");
                            VerifyGxFieldOrNextScene(episode.id);
                            if (index == 0)
                            {
                                CollectionProgress.Load(isolatedSave);
                                Require(world.RpgClears == 1, "The GX prologue was not saved when it finished.");
                                Require(FieldScene().Find("Resident_133") == null,
                                    "Crowler stayed as a free-roam resident at the harbor after the prologue.");
                            }
                            scenes++;
                            continue;
                        }

                        if (episode.id == "gx_ep_01")
                        {
                            VerifyGxHarborWalk();
                            var travelObjective = panel.Find("FieldHud/QuestTrack/Objective/Label").GetComponent<Text>();
                            Require(travelObjective.text.Contains(world.CampaignRegion(location).Name) &&
                                travelObjective.text.Contains(episode.fieldObjective), "GX travel hid the current objective.");
                            world.Update(.5f);
                            CaptureGx("02-harbor");
                            VerifyGxAspectLayouts(everyMap: false);
                            PressRpgNavigation("Quests");
                            Require(panel.Find("GxStoryJournal/Memories").GetComponentsInChildren<Button>().Length == 0,
                                "GX memories were visible before their episodes were cleared.");
                            world.Back();
                            Press(panel, "FieldHud/QuestTrack");
                            VerifyGxTravelSkip(episode, location);
                            Require(world.CanStartRpgEpisode(index) && WorldInputRestored(),
                                "The GX objective banner did not travel directly from the harbor.");
                            VerifyGxDeckReturn(location, startingDeck, alternateDeck);
                            Require(world.EnterRpgRegion(world.CampaignRegionForEpisode(1)), "Could not reopen the harbor map.");
                            PressRpgNavigation("Map");
                            CaptureGx("03-world-map");
                            Press(panel, "TravelToGoal");
                            FinishGxInterlude();
                            Require(world.CanStartRpgEpisode(index) && WorldInputRestored(),
                                "The one-click map objective did not arrive or left field input locked.");
                        }
                        if (episode.id == "gx_ep_03") VerifyGxCampusRoutine();
                        Require(world.EnterRpgRegion(location) && world.CanStartRpgEpisode(index) &&
                            FieldScene().Find("Scenery").GetComponent<Image>().sprite != null &&
                            panel.Find("FieldHud/FieldDetails/GxOpponentTier/Value/Label") != null,
                            $"GX duel HUD or field was missing for {episode.id}.");
                        Require(panel.Find("FieldHud/QuestTrack/Objective/Label").GetComponent<Text>().text == episode.fieldObjective,
                            $"GX field did not explain the objective at {episode.id}.");
                        VerifyGxResidentTimeline(episode, location);
                        bool isPov = episode.playAsCharacterId > 0;
                        string identity = FieldScene().Find("Player/GxIdentity/Label").GetComponent<Text>().text;
                        Require(identity.Contains(isPov ? "아스카" : "윤"), $"Wrong field point of view at {episode.id}.");
                        VerifyGxWalkerSizes();
                        if (episode.id == "gx_ep_01")
                        {
                            if (touchLayout) VerifyGxTouchStick();
                            VerifyGxNpcCamera();
                            VerifyGxRunSpeed();
                        }
                        if (isPov) VerifyGxFieldWalker(107);
                        if (episode.id == "gx_ep_01")
                        {
                            var fieldHud = panel.Find("FieldHud");
                            Require(fieldHud.Find("FieldDetails/Partner") == null && FieldScene().Find("RpgCompanion") == null &&
                                FieldScene().Find("Resident_107") != null && FieldScene().Find("Resident_119") != null,
                                "Asuka and Jaden should appear as academy residents after the harbor introduction.");
                            Require(!fieldHud.Find("FieldDetails").gameObject.activeSelf &&
                                !fieldHud.Find("ContextAction").gameObject.activeSelf &&
                                panel.Find("RegionBanner") == null && panel.Find("PartnerGreeting") == null &&
                                FieldScene().Find("MainQuestNpc/MainQuestMarker/TargetName/Label").GetComponent<Text>()
                                    .text.Contains(episode.opponentName), "The GX field did not prioritize the active objective.");
                            Press(fieldHud, "MenuToggle");
                            Require(fieldHud.Find("FieldDetails").gameObject.activeSelf, "The GX field menu dropped its duel details.");
                            Press(fieldHud, "MenuToggle");
                            Press(fieldHud, "QuestTrack/Guide");
                            for (int tick = 0; tick < 40 && !storyRoot.gameObject.activeInHierarchy; tick++) world.Update(.15f);
                            Require(storyRoot.gameObject.activeInHierarchy, "The GX field guide did not reach the active NPC.");
                        }
                        else if (episode.id == "gx_ep_13")
                        {
                            var marker = FieldScene().Find("MainQuestNpc/MainQuestMarker");
                            Require(marker.GetComponent<Button>().IsInteractable() &&
                                marker.GetComponent<Image>().raycastTarget && marker.GetComponent<CanvasGroup>().blocksRaycasts,
                                "The GX quest marker was not clickable.");
                            Press(FieldScene(), "MainQuestNpc/MainQuestMarker");
                            for (int tick = 0; tick < 40 && !storyRoot.gameObject.activeInHierarchy; tick++) world.Update(.15f);
                        }
                        else app.OpenRpgFieldEpisode(index);
                        Require(story.IsGxRpg && story.GetOpponentMatch(episode).Deck?.id == episode.deckId &&
                            FieldScene().Find("DialogueCast") != null, $"GX episode used the wrong deck or stage at {episode.id}.");
                        VerifyGxWalkerSizes();
                        VerifyDialogueStart(episode.preDialogues, episode.id);
                        if (episode.id == "gx_ep_01")
                        {
                            VerifyGxGuestArt(episode.preDialogues, "크로노스 데 메디치", 133);
                            VerifySpeaker(episode.preDialogues, "윤", player: true);
                            VerifyGxHiddenDialogue(episode);
                        }
                        if (isPov)
                        {
                            VerifySpeaker(episode.preDialogues, "텐죠인 아스카", player: true);
                            if (episode.id == "gx_ep_27")
                                VerifySpeaker(episode.preDialogues, "유벨 (윤에게 빙의)", player: false);
                            else
                            {
                                VerifySpeaker(episode.preDialogues, "윤", player: false);
                                var actors = typeof(TagForceWorldUI).GetField("_rpgStageIds", flags).GetValue(world)
                                    as System.Collections.IDictionary;
                                Require(actors != null && actors.Contains(0) && actors.Contains(episode.playAsCharacterId),
                                    $"The field actors for Yun and Asuka were not separate at {episode.id}.");
                            }
                        }
                        if (episode.id == "gx_ep_02") VerifyGxGuestArt(episode.preDialogues, "준코", 205);
                        VerifyGxDuelDialogueArt(episode);
                        var fieldPosition = (Vector2)typeof(TagForceWorldUI).GetField("_position", flags).GetValue(world);
                        if (episode.id == "gx_ep_01")
                        {
                            story.ShowFieldDuelResult(episode, won: false, draw: true, () => (true, ""), default);
                            VerifyGxRetryMenu(forcedDeck: false);
                            var review = storyRoot.Find("Scene/Choices/Choice_3/Button");
                            Require(review.GetComponentInChildren<Text>().text == "무승부 장면 보기",
                                "A draw offered the defeat branch.");
                            Press(storyRoot, "Scene/Choices/Choice_3/Button");
                            var lines = (StoryDialogueLine[])dialogueType.GetField("_dialogue", flags).GetValue(dialogue);
                            Require(lines.Length == 1 && lines[0].text.Contains("무승부") && lines[0].speaker == null,
                                "A character claimed a win in a GX draw.");
                            Press(storyRoot, "Scene/DialogueTools/Skip/Button");
                            Press(storyRoot, "Scene/Choices/Choice_2/Button");
                            VerifyGxReturn(fieldPosition, episode.id);
                            Require(world.RpgClears == index, "A draw cleared the current GX episode.");
                            app.OpenRpgFieldEpisode(index);
                        }
                        if (episode.id is "gx_ep_13" or "gx_ep_26") StartGxDuelThroughDialogue(episode, startingDeck);
                        else app.StartStoryDuel(episode, startingDeck, true);
                        VerifyGxSurrender();
                        defeats++;
                        VerifyGxRetryMenu(isPov);
                        if (episode.id == "gx_ep_01")
                        {
                            CaptureGx("05-duel-loss");
                            Press(storyRoot, "Scene/Choices/Choice_3/Button");
                            VerifyDialogueStart(episode.postLoseDialogues, episode.id);
                            VerifySpeaker(episode.postLoseDialogues, "윤", player: true);
                            Press(storyRoot, "Scene/DialogueTools/Skip/Button");
                            VerifyGxRetryMenu(isPov);
                        }
                        Require(world.RpgClears == index && !world.CanWalk && !WorldInputRestored(),
                            $"A lost duel advanced or unlocked the field at {episode.id}.");
                        if (episode.id == "gx_ep_01")
                        {
                            Press(storyRoot, "Scene/Choices/Choice_2/Button");
                            VerifyGxReturn(fieldPosition, episode.id);
                            Require(world.CanStartRpgEpisode(index), "Returning after a loss removed the current quest.");
                            app.OpenRpgFieldEpisode(index);
                            app.StartStoryDuel(episode, startingDeck, true);
                            VerifyGxSurrender();
                            VerifyGxRetryMenu(isPov);
                            Press(storyRoot, "Scene/Choices/Choice_1/Button");
                            story.Back();
                            VerifyGxRetryMenu(isPov);
                            Press(storyRoot, "Scene/Choices/Choice_1/Button");
                            Press(storyRoot, "DeckSelection/Window/Edit/Button");
                            Require((AppScreen)Get("_screen") == AppScreen.DeckEditor,
                                "The GX retry picker did not open the deck editor.");
                            app.CloseDeckEditor();
                            Require(storyRoot.Find("DeckSelection").gameObject.activeInHierarchy &&
                                world.RpgClears == index, "Deck editor return lost the pending GX retry.");
                            var picker = (Dropdown)dialogueType.GetField("_picker", flags).GetValue(dialogue);
                            int option = picker.options.FindIndex(deck => deck.text == alternateDeck.name);
                            Require(option >= 0, "The alternate GX deck was missing from the retry picker.");
                            picker.SetValueWithoutNotify(option);
                            Press(storyRoot, "DeckSelection/Window/Apply/Button");
                        }
                        else Press(storyRoot, "Scene/Choices/Choice_0/Button");
                        Press(storyRoot, "Scene/Choices/Choice_1/Button");
                        Press(storyRoot, "Scene/Choices/Choice_0/Button");
                        story.Update(4f);
                        Require(Get("_session") is DuelSession { NativePtr: var handle } && handle != IntPtr.Zero,
                            $"The retry face-off never started a new duel at {episode.id}.");
                        retries++;
                        Require(Get("_ai") is TacticalAI { Difficulty: AIDifficulty.Master } &&
                            Get("_activeOppDeck") is PresetDeckInfo rival && rival.id == episode.deckId,
                            $"GX opponent setup did not use Master and its exact tier deck at {episode.id}.");
                        if (isPov)
                        {
                            Require(episode.playAsCharacterId == 107 && Get("_activeMyDeck") is UserDeck forced &&
                                forced.main.SequenceEqual(gxDecks[episode.forcedPlayerDeckId].main) &&
                                forced.extra.SequenceEqual(gxDecks[episode.forcedPlayerDeckId].extra),
                                $"Forced deck was not equipped at {episode.id}.");
                            pov++;
                        }
                        var expectedDeck = episode.id == "gx_ep_01" ? alternateDeck : startingDeck;
                        if (!isPov)
                            Require(Get("_activeMyDeck") is UserDeck resumed && resumed.id == expectedDeck.id &&
                                resumed.main.SequenceEqual(expectedDeck.main) && resumed.extra.SequenceEqual(expectedDeck.extra) &&
                                resumed.side.SequenceEqual(expectedDeck.side), "Retry did not use the selected custom deck.");
                        var fieldUi = Get("_duelField") as DuelFieldUI;
                        var name = typeof(DuelFieldUI).GetField("_myNameText", flags).GetValue(fieldUi) as Text;
                        Require(name?.text.Contains(isPov ? "아스카" : "윤") == true,
                            $"The duel HUD lost the current player name at {episode.id}.");
                        AwardDuel(win: true);
                        VerifyDialogueStart(episode.postWinDialogues, episode.id);
                        if (episode.id == "gx_ep_09") VerifyGxRecoveryScenery(episode.postWinDialogues);
                        int memoryIndex = Array.FindIndex(episode.postWinDialogues, line => !string.IsNullOrEmpty(line.memoryTitle));
                        if (memoryIndex >= 0)
                        {
                            dialogueType.GetMethod("BindLine", flags).Invoke(dialogue, new object[] { memoryIndex, false });
                            VerifyGxDialogueArt(episode, episode.postWinDialogues[memoryIndex].cutscene);
                        }
                        if (episode.id == "gx_ep_27") VerifySpeaker(episode.postWinDialogues, "윤", player: false);
                        void FinishVictory() => Press(storyRoot, "Scene/DialogueTools/Skip/Button");
                        if (episode.id == "gx_ep_01")
                        {
                            VerifyGxSaveRecovery(index, FinishVictory);
                            CollectionProgress.Load(isolatedSave);
                        }
                        else FinishVictory();
                        Require(world.RpgClears == index + 1 && CollectionProgress.LastDuelDeckId == expectedDeck.id,
                            $"Duel result or previous deck was lost after {episode.id}.");
                        if (episode.id == "gx_ep_01") CollectionProgress.RememberDuelDeck(startingDeck.id);
                        VerifyGxReturn(fieldPosition, episode.id);
                        if (isPov) VerifyGxFieldWalker(0);
                        duels++;
                    }

                    CollectionProgress.Load(isolatedSave);
                    Require(world.RpgClears == 53 && scenes == 19 && duels == 34 && defeats == 34 && retries == 34 && pov == 3 &&
                        !CollectionProgress.IsStoryEpisodeCleared("gx_ep_17") && CollectionProgress.RpgAffinity == 0,
                        "GX progress did not survive reloading or overwrote classic GX progress.");
                    foreach (int location in Enumerable.Range(0, gxMap.spots.Length))
                    {
                        string map = gxMap.spots[location].spotId;
                        Require(world.EnterRpgRegion(location) && FieldScene().Find("Scenery").GetComponent<Image>().sprite != null,
                            $"Unable to enter GX map {map}.");
                        Require(!FieldScene().Cast<Transform>().Any(child => gxMap.roster.Any(resident =>
                            resident.storyOnly && child.name == $"Resident_{resident.charId}")),
                            $"A story-only GX actor was patrolling {map} after the credits.");
                        VerifyGxGrounded(location);
                        VerifyGroundedPresentation();
                        var ground = CurrentTerrain();
                        var start = ((RectTransform)FieldScene().Find("Player")).anchorMin;
                        var far = ground.Spot.walkPolygon.OrderByDescending(vertex => (vertex - start).sqrMagnitude).First();
                        var target = ground.Ground(Vector2.Lerp(far, start, .15f));
                        WalkOnGxMap(target);
                        Require(Vector2.Distance(((RectTransform)FieldScene().Find("Player")).anchorMin, target) < .001f,
                            $"Click movement did not reach dry ground on {map}.");
                        var exitPoints = FieldScene().Find("Exploration").Cast<RectTransform>()
                            .Where(anchor => anchor.name.StartsWith("ExitTarget_", StringComparison.Ordinal))
                            .Select(anchor => anchor.anchorMin).ToArray();
                        for (int i = 0; i < exitPoints.Length; i++)
                            Require(!exitPoints.Skip(i + 1).Any(point => Vector2.Distance(point, exitPoints[i]) < .055f),
                                $"Two exits lead to the same point on {map}.");
                        if (map == "spot_library")
                        {
                            WalkOnGxMap(new Vector2(.89f, .66f));
                            var feet = ((RectTransform)FieldScene().Find("Player")).anchorMin;
                            Require(feet.x is > .455f and < .546f && feet.y > .455f,
                                "The player left the library aisle or could not use its back half.");
                        }
                        if (map == "spot_volcano_arena")
                        {
                            WalkOnGxMap(new Vector2(.89f, .30f));
                            var feet = ((RectTransform)FieldScene().Find("Player")).anchorMin;
                            Require(feet.x < .70f && feet.y > .36f, "The player walked off the platform into the lava.");
                        }
                    }
                    PressRpgNavigation("Map");
                    Require(panel.Find(MapNodes)?.childCount == 12, "The GX world map lost pin buttons.");
                    world.Back();
                    VerifyGxArtwork(gxMap, expectedCg);
                    VerifyGxMemories();
                    VerifyGxAspectLayouts(everyMap: true);
                    VerifyGxCgAndPortraitTransitions();
                    VerifyEveryStageFootprint(gxScript);
                    Debug.Log("[VERIFY GX DIALOGUE] Event, victory and defeat scripts rendered, with distinct Yun, Asuka and possessed Yun speaker roles.");
                    Debug.Log("[VERIFY GX FIELD] 53 sequences, 34 Master wins, 34 surrender/retry routes, 19 lockouts, 12 grounded fields and three POV decks passed.");
                    Debug.Log("[VERIFY GX RESIDENTS] Rescue, recovery, changed forms, missing characters and story-only patrol restrictions passed.");
                    Debug.Log("[VERIFY GX OBJECTIVES] Field guidance, marker approach, direct preparation and optional reading time passed.");
                    Debug.Log("[VERIFY GX QOL] One-click travel, NPC prompts, running, deck swaps and editor return, save recovery and three aspect ratios passed.");
                }
                finally
                {
                    audioSource.SetValue(null, oldMusic);
                    pausedMusic.SetValue(null, oldPaused);
                    DuelAudio.BgmVolume = oldVolume;
                    UnityEngine.Object.DestroyImmediate(gxAudio);
                    if (hadLocation) PlayerPrefs.SetString(gxPreference, previousLocation);
                    else PlayerPrefs.DeleteKey(gxPreference);
                    PlayerPrefs.Save();
                }

                int AutoSpeed() => (int)dialogueType.GetField("_autoSpeed", flags).GetValue(dialogue);

                Transform Curtain() => canvas.transform.Find("GxSceneInterlude");

                void FinishGxInterlude()
                {
                    for (int tick = 0; tick < 30 && world.RpgInterludeActive; tick++) world.Update(.10f);
                    Require(!world.RpgInterludeActive && Curtain() == null, "The GX interlude left its curtain active.");
                }

                void VerifyGxOpeningInterlude(StoryEpisodeData episode)
                {
                    world.Update(0f);
                    var curtain = Curtain();
                    Require(curtain != null && !WorldInputRestored() && !world.CanWalk,
                        "The automatic GX scene did not lock input during its interlude.");
                    var group = curtain.GetComponent<CanvasGroup>();
                    var card = curtain.Find("TitleCard");
                    Require(card.Find("Location").GetComponentInChildren<Text>().text == "프로 리그 경기장" &&
                        card.Find("Episode").GetComponentInChildren<Text>().text == episode.title,
                        "The interlude omitted the upcoming location or title.");
                    world.Update(.20f);
                    Require(group.alpha is > 0f and < 1f && !storyRoot.gameObject.activeInHierarchy,
                        "The opening cut away before the fade-out completed.");
                    world.Update(.21f);
                    world.Update(1.12f);
                    Require(group.alpha == 1f && card.GetComponent<CanvasGroup>().alpha == 1f &&
                        !storyRoot.gameObject.activeInHierarchy && !WorldInputRestored(),
                        "The opening title did not hold over black before opening dialogue.");
                    CaptureGx("00-interlude-title");
                    world.Update(.40f);
                    Require(group.alpha is > 0f and < 1f && !storyRoot.gameObject.activeInHierarchy,
                        "The opening did not wait for its fade-in.");
                    FinishGxInterlude();
                    Require(storyRoot.gameObject.activeInHierarchy && AutoSpeed() == 0 && CurrentLine() == 0,
                        "The story was not opened at its first manual line after the final fade.");
                }

                void VerifyGxTravelSkip(StoryEpisodeData episode, int destination)
                {
                    var curtain = Curtain();
                    var originalField = FieldScene();
                    Require(curtain != null && !WorldInputRestored() && !world.CanWalk,
                        "GX travel began without a curtain and input lock.");
                    Require(curtain.Find("TitleCard/Location").GetComponentInChildren<Text>().text == world.CampaignRegion(destination).Name &&
                        curtain.Find("TitleCard/Episode").GetComponentInChildren<Text>().text == episode.title,
                        "GX travel showed the wrong destination title.");
                    world.Update(.30f);
                    Require(FieldScene() == originalField, "GX travel swapped the field before black.");
                    world.Update(.15f);
                    Require(FieldScene() != originalField && !WorldInputRestored(), "GX travel failed to swap the covered field.");
                    Press(canvas.transform, "GxSceneInterlude");
                    world.Update(.20f);
                    Require(world.RpgInterludeActive && curtain.GetComponent<CanvasGroup>().alpha is > 0f and < 1f &&
                        !WorldInputRestored(), "Click should skip the title and preserve the fade-in and input lock.");
                    world.Update(.21f);
                    Require(Curtain() == null && WorldInputRestored() && world.CanWalk,
                        "Skipping a GX interlude left the field's controls or raycasts disabled.");
                }

                void VerifyGxFieldOrNextScene(string episode)
                {
                    if (world.RpgClears < gxScript.episodes.Length && gxScript.episodes[world.RpgClears].isCutsceneOnly)
                    {
                        Require(storyRoot.gameObject.activeInHierarchy && !WorldInputRestored() &&
                            FieldScene().Find("DialogueCast") != null && AutoSpeed() == 0 && CurrentLine() == 0,
                            $"The next automatic scene did not open manually after {episode}.");
                    }
                    else Require(!storyRoot.gameObject.activeInHierarchy && WorldInputRestored() &&
                        FieldScene().Find("DialogueCast") == null && world.HandleFieldInput(default) == world.CanWalk,
                        $"The previous scene left the field locked after {episode}.");
                }

                void VerifyGxManualDialogue(StoryEpisodeData episode)
                {
                    var bind = dialogueType.GetMethod("BindLine", flags);
                    var readTime = dialogueType.GetField("_readTime", flags);
                    story.Update(10f);
                    Require(CurrentLine() == 0 && AutoSpeed() == 0 && (float)readTime.GetValue(dialogue) == 0f,
                        "A cutscene advanced after ten seconds with Auto off.");
                    int arrival = Array.FindIndex(episode.preDialogues, line => !string.IsNullOrEmpty(line.sceneTime));
                    Require(arrival >= 0, "The prologue lost its scripted stage transition.");
                    bind.Invoke(dialogue, new object[] { arrival, false });
                    story.Update(10f);
                    Require(CurrentLine() == arrival && (float)readTime.GetValue(dialogue) == 0f,
                        "A stage transition advanced with Auto off.");
                    bind.Invoke(dialogue, new object[] { 0, false });
                }

                void VerifyGxHiddenDialogue(StoryEpisodeData episode)
                {
                    var bind = dialogueType.GetMethod("BindLine", flags);
                    var readTime = dialogueType.GetField("_readTime", flags);
                    var next = (Button)dialogueType.GetField("_next", flags).GetValue(dialogue);
                    int picture = Array.FindIndex(episode.preDialogues, line => !string.IsNullOrEmpty(line.cutscene));
                    Require(picture >= 0, "The GX exhibition duel lost its CG fixture.");
                    bind.Invoke(dialogue, new object[] { picture, false });
                    VerifyDisplayedText(episode.preDialogues[picture]);
                    story.Update(10f);
                    Require(CurrentLine() == picture && (float)readTime.GetValue(dialogue) == 0f,
                        "A stage or CG transition advanced with Auto off.");
                    var cg = (Image)dialogueType.GetField("_cutsceneImage", flags).GetValue(dialogue);
                    var image = cg.sprite;
                    Require(image != null, "The dialogue hide fixture has no CG.");
                    var box = storyRoot.Find("Scene/DialogueBox");
                    var heading = storyRoot.Find("Scene/Episode");
                    var tools = storyRoot.Find("Scene/DialogueTools");
                    var surface = storyRoot.Find("RestoreDialogue");
                    var hide = tools.Find("Hide/Button").GetComponent<Button>();
                    Require(((Image)hide.targetGraphic).sprite == ((Image)tools.Find("Auto/Button").GetComponent<Button>().targetGraphic).sprite,
                        "The new hide button did not use the same RPG toolbar style.");
                    CaptureGx("02-exhibition-cg-dialogue");
                    Press(storyRoot, "Scene/DialogueTools/Auto/Button");
                    story.Update(.4f);
                    float elapsed = (float)readTime.GetValue(dialogue);
                    Require(AutoSpeed() == 1 && elapsed > 0f, "The dialogue hide fixture did not activate Auto.");
                    Press(storyRoot, "Scene/DialogueTools/Hide/Button");
                    Require(box.gameObject.activeSelf && surface.gameObject.activeSelf,
                        "The hide command removed the dialogue before its fade.");
                    story.Update(.20f);
                    Require(!box.gameObject.activeSelf && !heading.gameObject.activeSelf && !tools.gameObject.activeSelf &&
                        cg.gameObject.activeInHierarchy && cg.sprite == image, "Hide did not leave the CG free of dialogue UI.");
                    CaptureGx("02-exhibition-cg-hidden");
                    story.Update(10f);
                    Require(CurrentLine() == picture && (float)readTime.GetValue(dialogue) == elapsed,
                        "Auto advanced or lost its reading time while the dialogue was hidden.");
                    ExecuteEvents.Execute<IPointerClickHandler>(surface.gameObject, new PointerEventData(EventSystem.current)
                        { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
                    next.onClick.Invoke();
                    story.Update(.20f);
                    Require(box.gameObject.activeSelf && heading.gameObject.activeSelf && tools.gameObject.activeSelf &&
                        !surface.gameObject.activeSelf && CurrentLine() == picture && (float)readTime.GetValue(dialogue) == elapsed,
                        "A click advanced the dialogue while restoring it.");

                    var view = (StoryModeUI.StoryDialogueView)dialogue;
                    Require(view.HandleDisplayInput(toggle: true), "H/right-click could not hide dialogue.");
                    story.Update(.20f);
                    Require(view.HandleDisplayInput(restore: true), "Space did not restore dialogue.");
                    story.Update(.20f);
                    Require(box.gameObject.activeSelf && CurrentLine() == picture, "Space advanced the hidden dialogue.");
                    view.HandleDisplayInput(toggle: true);
                    story.Update(.20f);
                    story.Back();
                    story.Update(.20f);
                    Require(box.gameObject.activeInHierarchy && CurrentLine() == picture,
                        "Escape closed or advanced the story instead of restoring its dialogue.");
                    story.Update(.1f);
                    Require((float)readTime.GetValue(dialogue) > elapsed, "Auto did not resume after the dialogue was restored.");
                    Press(storyRoot, "Scene/DialogueTools/Auto/Button");
                    Press(storyRoot, "Scene/DialogueTools/Auto/Button");
                    Require(AutoSpeed() == 0, "Auto did not return to its off setting.");
                    bind.Invoke(dialogue, new object[] { 0, false });
                    Debug.Log("[VERIFY GX PRESENTATION] Manual cutscenes, curtain timing and skipping, CG viewing and input restoration passed.");
                }

                void VerifyGxAutoReadTime(StoryEpisodeData episode)
                {
                    Press(storyRoot, "Scene/DialogueTools/Auto/Button");
                    Require(AutoSpeed() == 1, "Auto required more than one press to enable.");
                    const int shortLine = 2;
                    var bind = dialogueType.GetMethod("BindLine", flags);
                    bind.Invoke(dialogue, new object[] { shortLine, false });
                    VerifyDisplayedText(episode.preDialogues[shortLine]);
                    story.Update(1.1f);
                    Require(CurrentLine() == shortLine, "GX autoplay advanced before the short line could be read.");
                    story.Update(1.5f);
                    Require(CurrentLine() == shortLine + 1, "GX autoplay stopped advancing after the reading pause.");

                    int arrival = Array.FindIndex(episode.preDialogues, line => !string.IsNullOrEmpty(line.sceneTime));
                    Require(arrival >= 0, "The prologue lost its scripted time jump.");
                    var arrivalLine = episode.preDialogues[arrival];
                    bind.Invoke(dialogue, new object[] { arrival, false });
                    VerifyDisplayedText(arrivalLine);
                    var timeBadge = storyRoot.Find("Scene/SceneTime");
                    Require(timeBadge.gameObject.activeInHierarchy &&
                        timeBadge.GetComponentInChildren<Text>().text == arrivalLine.sceneTime,
                        "The GX arrival did not show its time caption.");
                    float reading = Mathf.Max(1.65f, arrivalLine.text.Length * .055f);
                    story.Update(reading + .5f);
                    Require(CurrentLine() == arrival, "GX autoplay cut away before the time transition could be read.");
                    story.Update(.75f);
                    Require(CurrentLine() == arrival + 1 && !timeBadge.gameObject.activeSelf,
                        "The time caption persisted beyond its transition line.");
                    bind.Invoke(dialogue, new object[] { 0, false });
                }

                void StartGxDuelThroughDialogue(StoryEpisodeData episode, UserDeck deck)
                {
                    var next = (Button)dialogueType.GetField("_next", flags).GetValue(dialogue);
                    var choices = storyRoot.Find("Scene/Choices");
                    for (int line = 0; line < episode.preDialogues.Length; line++)
                    {
                        Require(!choices.gameObject.activeSelf && Get("_session") == null,
                            $"A three-answer prompt interrupted the GX dialogue at {episode.id}/{line}.");
                        Require(CurrentLine() == line, $"Unexpected GX dialogue order at {episode.id}.");
                        story.Update(2f);
                        VerifyDisplayedText(episode.preDialogues[line]);
                        next.onClick.Invoke();
                    }

                    Require(storyRoot.Find("DeckSelection").gameObject.activeInHierarchy && Get("_session") == null,
                        $"The dialogue did not reach deck preparation at {episode.id}.");
                    var picker = (Dropdown)dialogueType.GetField("_picker", flags).GetValue(dialogue);
                    int deckIndex = picker.options.FindIndex(option => option.text == deck.name);
                    Require(deckIndex >= 0, $"The selected deck was unavailable at {episode.id}.");
                    picker.SetValueWithoutNotify(deckIndex);
                    Press(storyRoot, "DeckSelection/Window/Apply/Button");
                    var firstChoice = choices.Find("Choice_0/Button");
                    Require(firstChoice.GetComponentInChildren<Text>().text == "듀얼 개시!", $"No way to start {episode.id}.");
                    Press(choices, "Choice_0/Button");
                    for (int attempt = 0; attempt < 15; attempt++)
                    {
                        string label = firstChoice.GetComponentInChildren<Text>().text;
                        if (label is "선공" or "듀얼 시작")
                        {
                            Press(choices, "Choice_0/Button");
                            break;
                        }
                        Press(choices, $"Choice_{attempt % 3}/Button");
                    }
                    story.Update(4f);
                    Require(Get("_session") is DuelSession { NativePtr: var handle } && handle != IntPtr.Zero,
                        $"The quest dialogue could not reach a duel at {episode.id}.");

                }

                void VerifyGxResidentTimeline(StoryEpisodeData episode, int location)
                {
                    switch (episode.id)
                    {
                        case "gx_ep_04":
                        case "gx_ep_08":
                        case "gx_ep_09":
                            ExpectResident(111, present: false);
                            break;
                        case "gx_ep_10":
                            Visit("spot_harbor_beach");
                            ExpectResident(111, present: true);
                            break;
                        case "gx_ep_13":
                            ExpectResident(199, present: false);
                            Visit("spot_forest_duel_ground");
                            ExpectResident(107, present: false);
                            ExpectResident(117, present: false);
                            break;
                        case "gx_ep_14":
                            ExpectResident(117, present: true);
                            ExpectResident(3100, present: false);
                            break;
                        case "gx_ep_15":
                            ExpectResident(107, present: false);
                            break;
                        case "gx_ep_16":
                            Visit("spot_main_academy");
                            ExpectResident(137, present: false);
                            break;
                        case "gx_ep_22":
                        case "gx_ep_25":
                            ExpectResident(119, present: false);
                            ExpectResident(120, present: false);
                            break;
                        case "gx_ep_26":
                            ExpectResident(119, present: true);
                            ExpectResident(214, present: false);
                            break;
                    }

                    void Visit(string spotId)
                    {
                        int index = Array.FindIndex(gxMap.spots, spot => spot.spotId == spotId);
                        Require(world.EnterRpgRegion(index), $"Cannot check GX residents at {spotId}.");
                    }

                    void ExpectResident(int actorId, bool present)
                    {
                        Require((FieldScene().Find($"Resident_{actorId}") != null) == present,
                            $"Unexpected field presence for GX resident {actorId} at {episode.id}.");
                    }

                    if (!world.CanStartRpgEpisode(world.RpgClears))
                        Require(world.EnterRpgRegion(location), $"Cannot restore the objective at {episode.id}.");
                }

                void CaptureGx(string name)
                {
                    var output = Environment.GetEnvironmentVariable("YGO_GX_QA_SCREENSHOTS");
                    if (string.IsNullOrWhiteSpace(output)) return;
                    Directory.CreateDirectory(output);
                    var ui = canvas.GetComponent<Canvas>();
                    var cameraObject = new GameObject("GX QA screenshot camera", typeof(Camera));
                    var camera = cameraObject.GetComponent<Camera>();
                    var frame = RenderTexture.GetTemporary(touchLayout ? 1950 : 1600, 900, 24);
                    var pixels = new Texture2D(frame.width, frame.height, TextureFormat.RGB24, false);
                    var previousFrame = RenderTexture.active;
                    var oldRenderMode = ui.renderMode;
                    var oldCamera = ui.worldCamera;
                    var canvasRect = (RectTransform)ui.transform;
                    var size = canvasRect.sizeDelta;
                    try
                    {
                        camera.targetTexture = frame;
                        camera.clearFlags = CameraClearFlags.SolidColor;
                        camera.backgroundColor = new Color(.012f, .018f, .025f);
                        ui.worldCamera = camera;
                        ui.renderMode = RenderMode.ScreenSpaceCamera;
                        Canvas.ForceUpdateCanvases();
                        camera.Render();
                        RenderTexture.active = frame;
                        pixels.ReadPixels(new Rect(0, 0, frame.width, frame.height), 0, 0);
                        File.WriteAllBytes(Path.Combine(output, name + ".png"), pixels.EncodeToPNG());
                    }
                    finally
                    {
                        ui.renderMode = oldRenderMode;
                        ui.worldCamera = oldCamera;
                        camera.targetTexture = null;
                        RenderTexture.active = previousFrame;
                        RenderTexture.ReleaseTemporary(frame);
                        UnityEngine.Object.DestroyImmediate(pixels);
                        UnityEngine.Object.DestroyImmediate(cameraObject);
                        canvasRect.sizeDelta = size;
                        Canvas.ForceUpdateCanvases();
                    }
                }

                void VerifyGxSaveRecovery(int progress, Action finish)
                {
                    string profile = Path.Combine(isolatedSave, CollectionProgress.ProfileFileName);
                    using (SaveSyncFiles.LockProfile(profile)) finish();
                    var choice = storyRoot.Find("Scene/Choices/Choice_0/Button");
                    Require(world.RpgClears == progress && !world.CanWalk && choice.gameObject.activeInHierarchy &&
                        choice.GetComponentInChildren<Text>().text == "진행도 다시 저장",
                        "A failed GX save closed the result or advanced to the next scene.");
                    Press(storyRoot, "Scene/Choices/Choice_0/Button");
                    FinishGxInterlude();
                    Require(world.RpgClears == progress + 1 && WorldInputRestored(),
                        "The GX campaign did not recover after the profile became writable.");
                }

                void VerifyGxRetryMenu(bool forcedDeck)
                {
                    var choices = storyRoot.Find("Scene/Choices");
                    Require(choices.gameObject.activeInHierarchy &&
                        !storyRoot.Find("Scene/DialogueTools").gameObject.activeInHierarchy &&
                        Choice(0).interactable && LabelOf(0) == "바로 재도전" &&
                        Choice(1).interactable != forcedDeck && LabelOf(1) == "덱 변경 후 재도전" &&
                        Choice(2).interactable && LabelOf(2) == "필드로 복귀",
                        "The GX defeat menu did not offer immediate actions or allowed overriding a POV deck.");

                    Button Choice(int index) => choices.Find($"Choice_{index}/Button").GetComponent<Button>();
                    string LabelOf(int index) => Choice(index).GetComponentInChildren<Text>().text;
                }

                void VerifyGxDeckReturn(int location, UserDeck startingDeck, UserDeck alternateDeck)
                {
                    WalkOnGxMap(new Vector2(.39f, .24f));
                    var position = ((RectTransform)FieldScene().Find("Player")).anchorMin;
                    var framing = CameraFrame();
                    int progress = world.RpgClears;
                    PressRpgNavigation("Decks");
                    Press(panel, "EditGxDecks");
                    Require((AppScreen)Get("_screen") == AppScreen.DeckEditor, "GX field deck editing did not open.");
                    app.CloseDeckEditor();
                    Require(panel.Find("GxDeckList") != null, "GX deck editor did not return to the field deck selector.");
                    ChooseDeck(alternateDeck);
                    Require(CollectionProgress.LastDuelDeckId == alternateDeck.id &&
                        world.CanStartRpgEpisode(progress) && world.CampaignRegion(location).IsOpen(progress) &&
                        world.RpgClears == progress && ((RectTransform)FieldScene().Find("Player")).anchorMin == position &&
                        CameraFrame() == framing && WorldInputRestored(), "Changing a GX field deck disturbed the field or quest.");
                    PressRpgNavigation("Decks");
                    ChooseDeck(startingDeck);

                    void ChooseDeck(UserDeck deck)
                    {
                        var row = panel.Find("GxDeckList").GetComponentsInChildren<Button>()
                            .Single(button => button.GetComponentInChildren<Text>()?.text == deck.name);
                        row.onClick.Invoke();
                    }
                }

                void VerifyGxRunSpeed()
                {
                    var walk = typeof(TagForceWorldUI).GetMethod("AdvanceRpgWalk", flags);
                    FieldScene().Find("Scenery").GetComponent<TagForceFloorTap>().Click(new Vector2(.61f, .21f));
                    var position = ((RectTransform)FieldScene().Find("Player")).anchorMin;
                    var walking = (Vector2)walk.Invoke(world, new object[] { .10f, false });
                    var running = (Vector2)walk.Invoke(world, new object[] { .10f, true });
                    Require(Vector2.Distance(running, position) > Vector2.Distance(walking, position) * 1.5f &&
                        CurrentTerrain().Contains(running), "GX running did not speed up a grounded route.");
                    world.Update(1f);
                    WalkOnGxMap(new Vector2(.5f, .16f));
                }

                void VerifyGxTouchStick()
                {
                    Canvas.ForceUpdateCanvases();
                    var stick = panel.Find("FieldHud/VirtualStick").GetComponent<GxFieldStick>();
                    var rect = (RectTransform)stick.transform;
                    var safe = (RectTransform)mobile.transform;
                    Require(rect.rect.width >= MobileSafeArea.MinimumTouchSize(rect) && safe.anchorMin.x > 0f && safe.anchorMax.x < 1f,
                        "The GX mobile movement area did not allow for the notch or minimum touch target.");
                    var center = (Vector2)rect.TransformPoint(rect.rect.center);
                    var contact = new PointerEventData(input) { pointerId = 21, position = center, button = PointerEventData.InputButton.Left };
                    var other = new PointerEventData(input) { pointerId = 22, position = center, button = PointerEventData.InputButton.Left };
                    stick.OnPointerDown(contact);
                    Require(stick.Direction == Vector2.zero, "The centered mobile stick caused drift.");
                    contact.position += Vector2.right * rect.rect.width * .30f;
                    stick.OnDrag(contact);
                    stick.OnPointerUp(other);
                    Require(stick.Direction.x > .75f && Mathf.Abs(stick.Direction.y) < .02f,
                        "A second finger interrupted GX movement or the touch direction was lost.");
                    var actor = (RectTransform)FieldScene().Find("Player");
                    Vector2 before = actor.anchorMin;
                    world.Update(.075f);
                    Require(actor.anchorMin.x > before.x && CurrentTerrain().Contains(actor.anchorMin),
                        "The virtual stick did not move the actual player inside the walkable field.");
                    stick.OnPointerUp(contact);
                    Vector2 stopped = actor.anchorMin;
                    world.Update(.075f);
                    Require(stick.Direction == Vector2.zero && actor.anchorMin == stopped,
                        "The player continued to move after releasing the virtual stick.");
                    CaptureGx("04-mobile-safe-area");
                    Debug.Log("[VERIFY GX TOUCH] 19.5:9 safe area, pointer isolation, grounded movement and release passed.");

                }

                void VerifyGxCampusRoutine()
                {
                    const string friend = "campus/107", bun = "gx_melon_bread";
                    var original = CollectionProgress.GetTagForceProgress(gx).Copy();
                    int balance = CollectionProgress.Balance;
                    int academy = world.CampaignRegionForEpisode(2), dorm = world.CampaignRegionForEpisode(4);
                    int chapter = world.RpgClears;
                    try
                    {
                        var legacy = original.Copy();
                        legacy.day = 23;
                        legacy.time = 2;
                        legacy.conversations.Add("legacy/visit");
                        Require(CollectionProgress.TrySaveTagForce(gx, legacy, 0, out string setupError), setupError);
                        Require(world.EnterRpgRegion(academy), "Could not open the campus morning.");
                        Require(FieldScene().Find("Resident_107") != null && FieldScene().Find("Resident_119") == null,
                            "Morning campus residents were incorrect.");
                        PressRpgNavigation("Schedule");
                        CaptureGx("campus-schedule");
                        Press(panel, "CampusPlans/Advance");
                        Require(world.RpgInterludeActive && !WorldInputRestored(), "Campus time did not lock field input during the curtain.");
                        FinishGxInterlude();
                        Require(WorldInputRestored() && Campus().campusTime == 1 && FieldScene().Find("Resident_119") != null,
                            "The afternoon did not restore input and bring Jaden to the academy.");

                        Approach("RpgCardMerchant");
                        var offer = panel.Find("ResidentChoices/CampusBreadOffer/Bread").GetComponent<Image>();
                        Require(offer.sprite != null && offer.sprite != RpgUiStyle.Glyph(RpgUiStyle.Symbol.Star),
                            "The cafeteria did not load its illustrated melon bread.");
                        Press(panel, "ResidentChoices/BuyCampusBread");
                        Press(panel, "ResidentChoices/BuyCampusBread");
                        world.Update(3f);
                        Require(Campus().Count(bun) == 2 && CollectionProgress.Balance == balance - 80,
                            "A cafeteria purchase did not atomically spend DP and stock the bread.");
                        CaptureGx("campus-bread");

                        world.Back();
                        Approach("Resident_107");
                        Press(panel, "ResidentChoices/TalkDaily");
                        Press(panel, "ResidentChoices/TalkDaily");
                        Press(panel, "ResidentChoices/GiveCampusBread");
                        Press(panel, "ResidentChoices/GiveCampusBread");
                        Require(Campus().Count(bun) == 1 && Campus().Relationship(friend).affinity == 560,
                            "Campus conversation or gifting rewarded the same action twice.");
                        world.Back();

                        OpenSchedule();
                        Press(panel, "CampusPlans/Advance");
                        FinishGxInterlude();
                        Require(Campus().campusTime == 2 && FieldScene().Find("Resident_107") == null,
                            "Asuka did not leave the academy for the evening.");
                        OpenSchedule();
                        Press(panel, "CampusPlans/Advance");
                        FinishGxInterlude();
                        var restPoint = FieldScene().Find("CampusDormStation") as RectTransform;
                        Require(restPoint != null && CurrentTerrain().Contains(restPoint.anchorMin) && FieldScene().Find("Resident_107") != null,
                            "Evening travel did not expose the dorm rest point and visiting friend.");
                        world.Update(.5f);
                        var residentArt = FieldScene().Find("Resident_107/SD").GetComponent<Image>();
                        Require(residentArt.color.a == 1f && residentArt.canvasRenderer.GetAlpha() == 1f,
                            "A rebuilt resident kept the disabled tint after the evening curtain opened.");
                        CaptureGx("campus-dorm-evening");
                        Press(FieldScene(), "CampusDormStation");
                        world.Update(3f);
                        Require(panel.Find("CampusSchedule") != null, "The physical dorm station did not open the schedule.");
                        Press(panel, "CampusPlans/Advance");
                        FinishGxInterlude();
                        CollectionProgress.Load(isolatedSave);
                        Require(WorldInputRestored() && Campus().campusDay == original.campusDay + 1 && Campus().campusTime == 0 &&
                            Campus().Count(bun) == 1 && Campus().pickups.SequenceEqual(original.pickups) && world.RpgClears == chapter,
                            "Rest did not persist day and inventory or changed one-time pickups or story progress.");
                        Require(Campus().day == 23 && Campus().time == 2 && Campus().conversations.Contains("legacy/visit"),
                            "The GX campus clock altered the previous Tag Force world's saved date or conversations.");

                        Require(world.EnterRpgRegion(academy), "Could not return to the academy the next morning.");
                        Approach("Resident_107");
                        Press(panel, "ResidentChoices/TalkDaily");
                        world.Update(3f);
                        Require(panel.Find("ResidentChoices/DailyLine/Label").GetComponent<Text>().text.Contains("어제 빵") &&
                            Campus().Relationship(friend).affinity == 760,
                            "The next-day gift conversation did not survive reloading the save.");
                        CaptureGx("campus-next-day");
                        Press(panel, "ResidentChoices/TalkDaily");
                        Require(Campus().Relationship(friend).affinity == 760,
                            "The next-day conversation could be rewarded more than once.");
                        world.Back();
                        Debug.Log("[VERIFY GX CAMPUS] Schedule, NPC visits, purchase, gifts, dorm rest, follow-up and persisted state passed.");
                    }
                    finally
                    {
                        Require(CollectionProgress.TrySaveTagForce(gx, original, balance - CollectionProgress.Balance, out string error), error);
                    }

                    TagForceSave Campus() => CollectionProgress.GetTagForceProgress(gx);
                    void OpenSchedule() => Press(panel, "FieldHud/QuestTrack/CampusClock");
                    void Approach(string actor)
                    {
                        Press(FieldScene(), actor + "/SD");
                        world.Update(3f);
                        Require(panel.Find("ResidentChoices") != null, $"Could not approach {actor}.");
                    }
                }

                void VerifyGxNpcCamera()
                {
                    var local = (RectTransform)FieldScene().Find("Resident_107");
                    Require(FieldScene().Find("RpgCardMerchant/SD").GetComponent<Image>().sprite != null,
                        "The GX academy merchant was shown without SD art.");
                    WalkOnGxMap(local.anchorMin - new Vector2(.061f, .03f));
                    var viewport = ViewportSize();
                    var framing = CameraFrame();
                    var prompt = panel.Find("FieldHud/ContextAction");
                    string hint = prompt.GetComponentInChildren<Text>(true).text;
                    CaptureGx("04-academy");
                    Require(((RectTransform)panel).anchorMin == Vector2.zero && ((RectTransform)panel).anchorMax == Vector2.one,
                        "The GX field camera left padding around the viewport.");
                    Require(prompt.gameObject.activeInHierarchy && hint.Contains("대화") &&
                        hint.Contains("[Space / 클릭]") == !touchLayout,
                        $"GX did not show the interaction hint near a resident. Hint={hint}, Player={FieldScene().Find("Player").GetComponent<RectTransform>().anchorMin}, NPC={local.anchorMin}.");
                    if (touchLayout) Press(panel, "FieldHud/ContextAction");
                    else Press(local, "SD");
                    world.Update(0f);
                    Require(panel.Find("ResidentChoices") != null && ViewportSize() == viewport && CameraFrame() == framing,
                        "The field shrank or moved when a GX NPC began talking.");
                    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("YGO_GX_QA_SCREENSHOTS")))
                    {
                        world.Update(.8f);
                        CaptureGx("04-npc-dialogue");
                    }
                    world.Back();
                    Require(world.CanWalk && WorldInputRestored() && ViewportSize() == viewport && CameraFrame() == framing,
                        "The GX NPC dialogue did not restore the field and its camera.");
                    WalkOnGxMap(new Vector2(.5f, .16f));
                }

                void VerifyGxFieldWalker(int id)
                {
                    var scene = FieldScene();
                    var art = (scene.Find("Player/Sprite") ?? scene.Find("DialogueCast/Player/Sprite"))?.GetComponent<Image>();
                    var images = (TagForceWorldImages)typeof(TagForceWorldUI).GetField("_images", flags).GetValue(world);
                    string key = id == 0 ? "protagonist" : id.ToString();
                    var expected = images.Get($"TagForce/GX/WalkCycles/sd_{key}_field.png");
                    Require(expected != null && art != null && art.sprite != null && art.sprite.texture == expected.texture,
                        $"The field did not switch to the GX walker {key}.");
                }

                void VerifyGxSurrender()
                {
                    var duel = Get("_session") as DuelSession;
                    var fieldUi = (DuelFieldUI)Get("_duelField");
                    Require(duel != null && duel.NativePtr != IntPtr.Zero, "The GX duel did not start.");
                    typeof(DuelFieldUI).GetMethod("OnPauseClicked", flags).Invoke(fieldUi, null);
                    PressSurrender();
                    PressSurrender();
                    typeof(GameAppController).GetMethod("Update", flags).Invoke(app, null);
                    Require(Get("_session") == null && duel.NativePtr == IntPtr.Zero,
                        "GX surrender did not close the native duel.");

                    void PressSurrender()
                    {
                        var controls = (RectTransform)typeof(DuelFieldUI).GetField("_actionList", flags).GetValue(fieldUi);
                        var surrender = controls.GetComponentsInChildren<Button>()
                            .Single(button => button.GetComponentInChildren<Text>()?.text == "항복");
                        Require(surrender.IsInteractable(), "The GX surrender confirmation could not be selected.");
                        surrender.onClick.Invoke();
                    }
                }

                void VerifyGxReturn(Vector2 position, string episode)
                {
                    FinishGxInterlude();
                    if (storyRoot.gameObject.activeInHierarchy && world.RpgClears < gxScript.episodes.Length &&
                        gxScript.episodes[world.RpgClears].isCutsceneOnly)
                    {
                        VerifyGxFieldOrNextScene(episode);
                        return;
                    }
                    var player = FieldScene()?.Find("Player") as RectTransform;
                    var hud = panel.Find("FieldHud");
                    Require(world.Root.activeSelf && !storyRoot.gameObject.activeInHierarchy && WorldInputRestored() &&
                        player != null && player.anchorMin == position && FieldScene().Find("DialogueCast") == null &&
                        hud != null && hud.gameObject.activeInHierarchy && Get("_session") == null,
                        $"The field, position, HUD or input did not return after {episode}.");
                    var theme = CurrentTerrain().Spot.fieldTheme;
                    var track = theme switch
                    {
                        "arena" => RpgFieldMusic.Circuit,
                        "dark" or "volcano" => RpgFieldMusic.Crisis,
                        "forest" or "shore" => RpgFieldMusic.Outskirts,
                        _ => RpgFieldMusic.Home,
                    };
                    Require(bgm.clip == DuelAudio.GetRpgFieldMusic(track),
                        $"The {theme} field music did not return after {episode}.");
                }

                void VerifyGxAspectLayouts(bool everyMap)
                {
                    var screen = (RectTransform)canvas.transform;
                    Vector2 originalSize = screen.sizeDelta;
                    int current = (int)typeof(TagForceWorldUI).GetField("_rpgLocation", flags).GetValue(world);
                    var regionsToCheck = everyMap ? Enumerable.Range(0, gxMap.spots.Length) : new[] { current };
                    try
                    {
                        foreach (var size in new[] { new Vector2(1600f, 900f), new Vector2(1950f, 900f), new Vector2(2100f, 900f), new Vector2(1200f, 900f) })
                        {
                            screen.sizeDelta = size;
                            foreach (int index in regionsToCheck)
                            {
                                Require(world.EnterRpgRegion(index), $"Unable to check layout on {gxMap.spots[index].spotId}.");
                                Canvas.ForceUpdateCanvases();
                                var hud = panel.Find("FieldHud");
                                string movement = touchLayout ? "VirtualStick" : "Controls";
                                VerifyDisjoint(hud, "QuestTrack", "Minimap", "DuelistHud", movement, "OpenMap", "MenuToggle");
                                var mission = hud.Find("QuestTrack");
                                VerifyDisjoint(mission, mission.Find("Guide") != null ?
                                    new[] { "RegionName", "Chapter", "Objective", "Guide" } :
                                    new[] { "RegionName", "Chapter", "Objective" });
                                VerifyDisjoint(hud.Find("DuelistHud"), "Rank", "Dp");
                                Press(hud, "MenuToggle");
                                VerifyDisjoint(hud, "FieldDetails", "Minimap", "DuelistHud", movement, "OpenMap", "MenuToggle");
                                Press(hud, "MenuToggle");
                                Press(hud, "OpenMap");
                                Canvas.ForceUpdateCanvases();
                                VerifyDisjoint(panel, panel.Find("TravelToGoal") != null ?
                                    new[] { "Atlas", "RegionPreview", "MapTitle", "MapArea", "TravelToGoal" } :
                                    new[] { "Atlas", "RegionPreview", "MapTitle", "MapArea" });
                                var atlas = panel.Find(MapNodes);
                                VerifyDisjoint(atlas, gxMap.spots.SelectMany(spot => new[] { spot.spotId, spot.spotId + "/Name" }).ToArray());
                                var preview = panel.Find("RegionPreview");
                                VerifyDisjoint(preview, "Thumbnail", "RegionState", "RegionName", "Residents", "Travel");
                                world.Back();
                            }
                        }
                    }
                    finally
                    {
                        screen.sizeDelta = originalSize;
                        Canvas.ForceUpdateCanvases();
                        world.Update(0f);
                    }

                    void VerifyDisjoint(Transform host, params string[] paths)
                    {
                        var bounds = new List<(string Name, Rect Rect)>();
                        foreach (var path in paths)
                        {
                            var rect = (RectTransform)host.Find(path);
                            Require(rect != null && rect.gameObject.activeInHierarchy, $"Missing visible GX UI {path}.");
                            var corners = new Vector3[4];
                            rect.GetWorldCorners(corners);
                            var min = screen.InverseTransformPoint(corners[0]);
                            var max = screen.InverseTransformPoint(corners[2]);
                            Require(min.x >= screen.rect.xMin - .05f && max.x <= screen.rect.xMax + .05f &&
                                min.y >= screen.rect.yMin - .05f && max.y <= screen.rect.yMax + .05f,
                                $"GX {screen.sizeDelta}: {path} escaped the screen.");
                            var area = Rect.MinMaxRect(min.x + .15f, min.y + .15f, max.x - .15f, max.y - .15f);
                            if (rect.GetComponentInChildren<Text>() is { } label && !string.IsNullOrEmpty(label.text) &&
                                (path.EndsWith("/Name", StringComparison.Ordinal) || rect.GetComponent<Button>() != null ||
                                 host.name is "DuelistHud" or "QuestTrack"))
                            {
                                var settings = label.GetGenerationSettings(label.rectTransform.rect.size);
                                settings.resizeTextForBestFit = false;
                                settings.fontSize = label.resizeTextForBestFit ? label.resizeTextMinSize : label.fontSize;
                                settings.verticalOverflow = VerticalWrapMode.Overflow;
                                var height = new TextGenerator().GetPreferredHeight(label.text, settings) / label.pixelsPerUnit;
                                Require(height > 0f && height <= label.rectTransform.rect.height + 1f,
                                    $"GX {screen.sizeDelta}: {path} label would be clipped.");
                            }
                            foreach (var other in bounds)
                                Require(!other.Rect.Overlaps(area), $"GX {screen.sizeDelta}: {path} overlaps {other.Name}.");
                            bounds.Add((path, area));
                        }
                    }
                }

                void VerifyGxCgAndPortraitTransitions()
                {
                    var host = DuelUiKit.FillRect("GxTransitionTest", canvas.transform);
                    var view = new StoryModeUI.StoryDialogueView(host);
                    var bind = dialogueType.GetMethod("BindLine", flags);
                    var alpha = (CanvasGroup)dialogueType.GetField("_cutsceneOverlay", flags).GetValue(view);
                    var current = (Image)dialogueType.GetField("_cutsceneImage", flags).GetValue(view);
                    var previous = (Image)dialogueType.GetField("_cutscenePrevious", flags).GetValue(view);
                    var hero = (Image)dialogueType.GetField("_hero", flags).GetValue(view);
                    var sprites = (Dictionary<string, Sprite>)dialogueType.GetField("_rpgSprites", flags).GetValue(view);
                    var lines = gxScript.episodes.SelectMany(episode => episode.preDialogues.Concat(episode.postWinDialogues)
                            .Concat(episode.postLoseDialogues)).Where(line => !string.IsNullOrEmpty(line.cutscene))
                        .GroupBy(line => line.cutscene).Select(group => group.First()).ToList();
                    int cgCount = lines.Count;
                    lines.Add(new StoryDialogueLine { speaker = "", text = "대화 장면" });
                    try
                    {
                        Open(lines.ToArray());
                        view.Update(.4f);
                        Require(alpha.alpha == 1f && current.enabled, "The first GX CG did not fully fade in.");
                        for (int i = 1; i < cgCount; i++)
                        {
                            var outgoing = current.sprite;
                            bind.Invoke(view, new object[] { i, false });
                            var expected = sprites[Path.Combine(streaming, "TagForce/GX", $"Cutscenes/{lines[i].cutscene}.png")];
                            Require(previous.enabled && previous.sprite == outgoing && alpha.alpha == 1f &&
                                current.sprite == expected && current.color.a == 0f, $"GX CG flash at {lines[i].cutscene}.");
                            view.Update(.175f);
                            Require(previous.enabled && Mathf.Abs(current.color.a - .5f) < .01f && alpha.alpha == 1f,
                                $"GX CG faded through the field at {lines[i].cutscene}.");
                            view.Update(.175f);
                            Require(!previous.enabled && current.enabled && current.color.a == 1f,
                                $"GX CG did not clear its outgoing layer at {lines[i].cutscene}.");
                        }

                        bind.Invoke(view, new object[] { cgCount, false });
                        view.Update(.12f);
                        float opacity = alpha.alpha;
                        Require(opacity > 0f && opacity < 1f, "The GX CG did not begin fading to the field.");
                        bind.Invoke(view, new object[] { 0, false });
                        Require(Mathf.Approximately(alpha.alpha, opacity) && previous.enabled,
                            "Re-entering a CG during fade-out flashed the field.");

                        var moods = new[] { "angry", "blush", "determined", "sad", "shock", "smile" };
                        Open(moods.Select(emotion => new StoryDialogueLine { speaker = gxScript.protagonist, emotion = emotion, text = "표정 확인" }).ToArray());
                        for (int i = 0; i < moods.Length; i++)
                        {
                            bind.Invoke(view, new object[] { i, false });
                            view.Update(.25f);
                            var expected = sprites[Path.Combine(streaming, "TagForce/GX", $"DialoguePortraits/bust_campaign_protagonist_{moods[i]}.png")];
                            Require(hero.enabled && hero.sprite == expected && hero.color.a > 0f, $"GX portrait did not switch to {moods[i]}.");
                        }

                        view.Update(.6f);
                        var root = host.Find("StoryDialogue");
                        Press(root, "Scene/DialogueTools/Previous/Button");
                        var index = (int)dialogueType.GetField("_index", flags).GetValue(view);
                        Require(index == moods.Length - 2, "GX previous dialogue did not rewind.");
                        Press(root, "Scene/DialogueTools/Log/Button");
                        var history = (Text)dialogueType.GetField("_logText", flags).GetValue(view);
                        var group = (CanvasGroup)dialogueType.GetField("_scene", flags).GetValue(view);
                        Require(history.text.Contains(gxScript.protagonist) && history.text.Contains("표정 확인") &&
                            !group.interactable && !group.blocksRaycasts, "GX dialogue history did not display or isolate its input.");
                        Require(view.ClosePopup() && group.interactable && group.blocksRaycasts,
                            "The GX dialogue log left input locked after closing.");
                        Open(moods.Take(3).Select(emotion => new StoryDialogueLine
                            { speaker = gxScript.protagonist, emotion = emotion, text = "대사 입력 확인" }).ToArray());
                        var advance = dialogueType.GetMethod("AdvanceAt", flags);
                        var visible = dialogueType.GetField("_visible", flags);
                        var text = (Text)dialogueType.GetField("_text", flags).GetValue(view);
                        advance.Invoke(view, new object[] { 1f, 10 });
                        Require(text.text == "대사 입력 확인", "GX did not reveal the complete line on the first input.");
                        advance.Invoke(view, new object[] { 1.01f, 10 });
                        advance.Invoke(view, new object[] { 1.05f, 11 });
                        Require((int)dialogueType.GetField("_index", flags).GetValue(view) == 0,
                            "Repeated GX input skipped the just-completed line.");
                        advance.Invoke(view, new object[] { 1.2f, 12 });
                        Require((int)dialogueType.GetField("_index", flags).GetValue(view) == 1 &&
                            (int)visible.GetValue(view) < "대사 입력 확인".Length,
                            "The next valid GX input did not start the next line.");

                        Require(view.HandleDisplayInput(toggle: true), "Could not hide the dialogue before closing.");
                        view.Update(.2f);
                        Require(!root.Find("Scene/DialogueBox").gameObject.activeSelf, "The close/reset fixture was still visible.");
                        view.Close();
                        Open(lines.ToArray());
                        Require(root.Find("Scene/DialogueBox").gameObject.activeInHierarchy && group.interactable &&
                            group.blocksRaycasts && !root.Find("RestoreDialogue").gameObject.activeSelf &&
                            (int)dialogueType.GetField("_autoSpeed", flags).GetValue(view) == 0,
                            "Reopening dialogue inherited hidden UI or autoplay.");
                    }
                    finally
                    {
                        view.Close();
                        UnityEngine.Object.DestroyImmediate(host.gameObject);
                    }

                    void Open(StoryDialogueLine[] dialogueLines) => view.Open("GX rendering verification",
                        gxScript.protagonist, (OpponentCharacter)null, dialogueLines, null, rpg: true,
                        backdropBelow: true, rpgAssetFolder: world.RpgAssetFolder);
                }

                void VerifyGxHarborWalk()
                {
                    int harbor = world.CampaignRegionForEpisode(1);
                    Require(world.EnterRpgRegion(harbor), "Unable to return to the GX harbor after the opening.");
                    VerifyGxGrounded(harbor);
                    var field = FieldScene();
                    var player = (RectTransform)field.Find("Player");
                    var ground = CurrentTerrain();
                    WalkOnGxMap(new Vector2(.50f, .16f));
                    var art = player.GetComponentInChildren<TagForceRpgWalkAnimator>();
                    var atlas = (Sprite[])typeof(TagForceRpgWalkAnimator).GetField("_directions", flags).GetValue(art);
                    var playerImage = art.GetComponent<Image>();
                    var strideFrames = new HashSet<Sprite>();
                    var start = player.anchorMin;
                    for (int frame = 0; frame < 16; frame++)
                    {
                        world.Update(1f / 60f, Vector2.up);
                        Require(ground.Contains(player.anchorMin), "The GX walk left the walkable area between frames.");
                        strideFrames.Add(playerImage.sprite);
                        if (frame % 4 == 0) CaptureGx($"walk-{frame:D2}");
                    }
                    var stop = player.anchorMin;
                    world.Update(1f / 60f);
                    Require(stop.y > start.y && player.anchorMin == stop && strideFrames.Count > 1 &&
                        playerImage.sprite == atlas[6], "GX walking frames or the matching rear-facing idle pose were lost.");
                    CaptureGx("walk-16-idle");
                    var water = new Vector2(.82f, .70f);
                    WalkOnGxMap(water);
                    Require(!ground.Contains(water) && ground.Contains(player.anchorMin) &&
                        player.anchorMin != water && player.anchorMin.y < .58f,
                        "Touch movement crossed from the GX harbor onto the water.");
                    for (int i = 0; i < 5; i++) world.Update(.1f, new Vector2(1f, 1f));
                    Require(FieldScene() == field && ground.Contains(player.anchorMin) && player.anchorMin.y < .58f,
                        "Stick movement crossed from the GX harbor onto the water.");
                    world.Update(0f);
                    int academy = world.CampaignRegionForEpisode(2);
                    Press(field, $"Exploration/Exit_{academy}");
                    for (int i = 0; i < 20 && FieldScene() == field; i++) world.Update(.20f);
                    FinishGxInterlude();
                    Require(FieldScene() != field && world.CanStartRpgEpisode(1),
                        "The grounded harbor exit did not reach the academy.");
                    VerifyGxGrounded(academy);
                    Require(world.EnterRpgRegion(harbor), "Unable to return to the harbor for the map guide.");
                }

                void WalkOnGxMap(Vector2 point)
                {
                    FieldScene().Find("Scenery").GetComponent<TagForceFloorTap>().Click(point);
                    world.Update(4f);
                }

                void VerifyGxGrounded(int location)
                {
                    var project = typeof(TagForceWorldUI).GetMethod("RpgGroundPoint", flags);
                    var field = FieldScene();
                    Require(field.Find("RpgCompanion") == null, "A GX companion followed the player.");
                    foreach (var anchor in field.GetComponentsInChildren<RectTransform>())
                    {
                        string id = anchor.name;
                        if (id != "Player" && id != "MainQuestNpc" && id != "RpgCardMerchant" && id != "Landmark" &&
                            !id.StartsWith("Resident_", StringComparison.Ordinal) &&
                            !id.StartsWith("Pickup_", StringComparison.Ordinal) &&
                            !id.StartsWith("ExitTarget_", StringComparison.Ordinal)) continue;
                        var point = anchor.anchorMin;
                        var grounded = (Vector2)project.Invoke(world, new object[] { location, point });
                        Require(Vector2.Distance(grounded, point) < .0001f, $"{id} was outside the GX walking area.");
                    }
                }

                void VerifyGxPrologue(StoryEpisodeData episode)
                {
                    var lines = episode.preDialogues;
                    var bind = dialogueType.GetMethod("BindLine", flags);
                    var scenery = FieldScene().Find("Scenery").GetComponent<Image>();
                    var tournament = scenery.sprite;
                    var opponentPortrait = (Image)dialogueType.GetField("_opponent", flags).GetValue(dialogue);
                    var previousPortrait = (Image)dialogueType.GetField("_opponentPrevious", flags).GetValue(dialogue);
                    Require(!opponentPortrait.enabled, "Crowler's portrait appeared in the opening stadium.");
                    int backstageIndex = Array.FindIndex(lines, line => line.sceneMap == "spot_pro_backstage");
                    int harborIndex = Array.FindIndex(lines, line => line.sceneMap == null);
                    Require(tournament != null && lines[0].sceneMap == "spot_pro_tournament" &&
                        backstageIndex > 0 && harborIndex > backstageIndex, "The GX opening maps were in the wrong order.");
                    bind.Invoke(dialogue, new object[] { backstageIndex, false });
                    var backstage = scenery.sprite;
                    Require(!opponentPortrait.enabled, "Crowler's portrait appeared in the backstage room.");
                    bind.Invoke(dialogue, new object[] { harborIndex, false });
                    var harbor = scenery.sprite;
                    Require(backstage != null && harbor != null && backstage != tournament &&
                        harbor != backstage && harbor != tournament, "The GX opening did not switch all three stage backgrounds.");
                    int teacherEntrance = Array.FindIndex(lines, harborIndex, line =>
                        line.stage?.Any(action => action.actor == 133 && action.action == "enter") == true);
                    var teacher = (Transform)typeof(TagForceWorldUI).GetField("_rpgDialogueOpponent", flags).GetValue(world);
                    Require(teacherEntrance > harborIndex && teacher != null && !teacher.gameObject.activeSelf &&
                        !opponentPortrait.enabled, "Crowler appeared before his scripted harbor entrance.");
                    bind.Invoke(dialogue, new object[] { teacherEntrance, false });
                    Require(teacher.gameObject.activeInHierarchy && !opponentPortrait.enabled,
                        "Crowler's walker and portrait did not follow the port arrival and greeting separately.");
                    int greeting = Array.FindIndex(lines, teacherEntrance + 1, line => line.speaker == "크로노스 데 메디치");
                    Require(greeting > teacherEntrance && greeting + 1 < lines.Length && lines[greeting + 1].speaker == "윤",
                        "Crowler's scripted greeting did not receive Yun's reply.");
                    bind.Invoke(dialogue, new object[] { greeting, false });
                    VerifyDisplayedText(lines[greeting]);
                    Require(opponentPortrait.enabled && (bool)dialogueType.GetField("_opponentSpeaking", flags).GetValue(dialogue),
                        "Crowler's portrait did not appear on his first greeting.");
                    bind.Invoke(dialogue, new object[] { greeting + 1, false });
                    VerifyDisplayedText(lines[greeting + 1]);
                    Require(opponentPortrait.enabled && (bool)dialogueType.GetField("_heroSpeaking", flags).GetValue(dialogue),
                        "The original shared dialogue presentation did not resume after Crowler's greeting.");
                    int castEntrance = Array.FindIndex(lines, harborIndex, line => line.stage?.Length > 1 &&
                        line.stage.Any(action => action.actor == 119));
                    bind.Invoke(dialogue, new object[] { harborIndex, false });
                    Require(!opponentPortrait.enabled && !previousPortrait.enabled,
                        "Crowler's portrait persisted when rewinding to before his entrance.");
                    Require(!FieldScene().Find("DialogueCast/Guest_107").gameObject.activeSelf &&
                        !FieldScene().Find("DialogueCast/Guest_119").gameObject.activeSelf,
                        "Asuka or Jaden appeared before the scripted meeting.");
                    bind.Invoke(dialogue, new object[] { castEntrance, false });
                    var stage = FieldScene().Find("DialogueCast");
                    Require(stage.Find("Guest_107").gameObject.activeSelf && stage.Find("Guest_119").gameObject.activeSelf,
                        "Asuka or Jaden was missing at the scripted meeting.");
                    foreach (var actor in stage.Cast<RectTransform>().Where(actor => actor.gameObject.activeSelf))
                        Require(actor.anchorMin.x is > .14f and < .53f &&
                            actor.anchorMin.y is > .195f and < .395f, "An actor stood off of dry ground in the GX harbor.");
                    bind.Invoke(dialogue, new object[] { 0, false });
                    Require(scenery.sprite == tournament, "Rewinding the opening did not restore the tournament map.");
                }

                void VerifyGxWalkerSizes()
                {
                    var walkers = FieldScene().GetComponentsInChildren<TagForceRpgWalkAnimator>(true);
                    Require(walkers.Length > 0 && walkers.All(walker => walker.transform.parent is RectTransform actor &&
                        actor.sizeDelta.y >= 220f && actor.pivot.y < .35f),
                        "A GX field or staged walker is too small or grows downward from the ground point.");
                    foreach (var walker in walkers)
                    {
                        var image = walker.GetComponent<Image>();
                        var actor = (RectTransform)walker.transform.parent;
                        var art = image.rectTransform;
                        float height = (art.anchorMax.y - art.anchorMin.y) * actor.sizeDelta.y;
                        float width = (art.anchorMax.x - art.anchorMin.x) * actor.sizeDelta.x;
                        float visibleHeight = Mathf.Min(height, width * image.sprite.rect.height / image.sprite.rect.width);
                        Require(image.preserveAspect && visibleHeight >= 155f && visibleHeight <= 173f &&
                            art.anchorMin.y == .15f && actor.Find("GroundShadow") != null,
                            $"GX walker {actor.name} did not fit the shared silhouette height and ground line. Height={visibleHeight}");
                    }
                }

                void VerifyDialogueStart(StoryDialogueLine[] lines, string episodeId)
                {
                    Require(storyRoot.gameObject.activeInHierarchy && CurrentLine() == 0 &&
                        ReferenceEquals(dialogueType.GetField("_dialogue", flags).GetValue(dialogue), lines),
                        $"The wrong dialogue branch opened at {episodeId}.");
                    VerifyDisplayedText(lines[0]);
                    var timeBadge = storyRoot.Find("Scene/SceneTime");
                    bool hasTime = !string.IsNullOrEmpty(lines[0].sceneTime);
                    Require(timeBadge.gameObject.activeSelf == hasTime &&
                        (!hasTime || timeBadge.GetComponentInChildren<Text>().text == lines[0].sceneTime),
                        $"The opening time caption did not match the active dialogue at {episodeId}.");
                }

                void VerifyGxRecoveryScenery(StoryDialogueLine[] lines)
                {
                    int infirmary = Array.FindIndex(lines, line => line.sceneMap == "spot_infirmary");
                    int harbor = Array.FindIndex(lines, line => line.sceneMap == "spot_harbor_beach");
                    Require(infirmary > 0 && harbor > infirmary, "The recovery scenes did not follow the seal in order.");
                    var bind = dialogueType.GetMethod("BindLine", flags);
                    var scenery = FieldScene().Find("Scenery").GetComponent<Image>();
                    var ruins = scenery.sprite;
                    var viewport = ViewportSize();
                    bind.Invoke(dialogue, new object[] { infirmary, false });
                    var recovery = scenery.sprite;
                    bind.Invoke(dialogue, new object[] { harbor, false });
                    Require(recovery != null && recovery != ruins && scenery.sprite != null &&
                        scenery.sprite != recovery && scenery.sprite != ruins,
                        "The recovery and harbor scenes reused the ruins backdrop.");
                    Require(ViewportSize() == viewport && !WorldInputRestored(),
                        "GX follow-up scene resized the field or released movement during dialogue.");
                    bind.Invoke(dialogue, new object[] { 0, false });
                    Require(scenery.sprite == ruins && ViewportSize() == viewport && !WorldInputRestored(),
                        "Reading backward from the follow-up did not restore the original framing and lock.");
                }

                void VerifySpeaker(StoryDialogueLine[] lines, string speaker, bool player)
                {
                    int index = Array.FindIndex(lines, line => line.speaker == speaker);
                    Require(index >= 0, $"Missing GX speaker {speaker}.");
                    dialogueType.GetMethod("BindLine", flags).Invoke(dialogue, new object[] { index, false });
                    VerifyDisplayedText(lines[index]);
                    var name = (Text)dialogueType.GetField("_speaker", flags).GetValue(dialogue);
                    string speaking = player ? "_heroSpeaking" : "_opponentSpeaking";
                    Require(name.text == speaker && (bool)dialogueType.GetField(speaking, flags).GetValue(dialogue),
                        $"The dialogue assigned the wrong portrait slot to {speaker}.");
                }

                void VerifyGxGuestArt(StoryDialogueLine[] lines, string speaker, int id)
                {
                    VerifySpeaker(lines, speaker, player: false);
                    var portrait = (Image)dialogueType.GetField("_opponent", flags).GetValue(dialogue);
                    var sprites = (Dictionary<string, Sprite>)dialogueType.GetField("_rpgSprites", flags).GetValue(dialogue);
                    var path = Path.Combine(streaming, "TagForce/GX", $"DialoguePortraits/bust_{id}.png");
                    Require(sprites.TryGetValue(path, out var expected) && expected != null && portrait.sprite == expected,
                        $"The dialogue did not display the portrait for {speaker}.");
                    var guest = FieldScene().Find($"DialogueCast/Guest_{id}");
                    Require(guest != null && guest.gameObject.activeInHierarchy &&
                        guest.Find("Sprite").GetComponent<Image>().sprite != null,
                        $"The stage did not display the SD sprite for {speaker}.");
                }

                void VerifyDisplayedText(StoryDialogueLine line)
                {
                    var characters = (int[])dialogueType.GetField("_characters", flags).GetValue(dialogue);
                    dialogueType.GetMethod("Reveal", flags).Invoke(dialogue, new object[] { characters.Length });
                    var text = (Text)dialogueType.GetField("_text", flags).GetValue(dialogue);
                    Require(text.text == line.text, "The GX dialogue did not render the scripted text.");
                }

                void ReadDialogue(StoryEpisodeData episode)
                {
                    var lines = episode.preDialogues;
                    var checkedScenes = new HashSet<string>();
                    if (AutoSpeed() == 0) Press(storyRoot, "Scene/DialogueTools/Auto/Button");
                    for (int tick = 0; tick < lines.Length * 5 + 5 && storyRoot.gameObject.activeInHierarchy &&
                        !world.RpgInterludeActive; ++tick)
                    {
                        string scene = lines[CurrentLine()].cutscene;
                        if (!string.IsNullOrEmpty(scene) && checkedScenes.Add(scene))
                            VerifyGxDialogueArt(episode, scene);
                        story.Update(4f);
                    }
                    Require(lines.All(line => string.IsNullOrEmpty(line.cutscene) || checkedScenes.Contains(line.cutscene)),
                        $"Automatic playback skipped a CG in {episode.id}.");
                }

                void VerifyGxDuelDialogueArt(StoryEpisodeData episode)
                {
                    int index = Array.FindIndex(episode.preDialogues, line => !string.IsNullOrEmpty(line.cutscene));
                    if (index < 0) return;
                    dialogueType.GetMethod("BindLine", flags).Invoke(dialogue, new object[] { index, false });
                    VerifyGxDialogueArt(episode, episode.preDialogues[index].cutscene);
                }

                void VerifyGxDialogueArt(StoryEpisodeData episode, string scene)
                {
                    var image = (Image)dialogueType.GetField("_cutsceneImage", flags).GetValue(dialogue);
                    var sprites = (Dictionary<string, Sprite>)dialogueType.GetField("_rpgSprites", flags).GetValue(dialogue);
                    string path = Path.Combine(streaming, "TagForce/GX", $"Cutscenes/{scene}.png");
                    Require(sprites.TryGetValue(path, out var sprite) && sprite != null && image.sprite == sprite,
                        $"The scripted CG did not load for {episode.id}: {scene}.");
                }

                void VerifyGxMemories()
                {
                    var memories = gxScript.episodes.SelectMany(episode => episode.preDialogues.Concat(episode.postWinDialogues)
                        .Where(line => !string.IsNullOrEmpty(line.memoryTitle)).Select(line => (episode, line))).ToArray();
                    Require(memories.Length == 4, "Wrong number of GX Asuka memories.");
                    int balance = CollectionProgress.Balance;
                    foreach (var (episode, memory) in memories)
                    {
                        PressRpgNavigation("Quests");
                        Press(panel, $"GxStoryJournal/Memories/{memory.cutscene}");
                        var lines = (StoryDialogueLine[])dialogueType.GetField("_dialogue", flags).GetValue(dialogue);
                        Require(storyRoot.gameObject.activeInHierarchy && ReferenceEquals(lines[0], memory) && lines.Length > 1 &&
                            lines.All(line => line.cutscene == memory.cutscene), "The journal did not open only the selected GX memory.");
                        VerifyGxDialogueArt(episode, memory.cutscene);
                        Press(storyRoot, "Scene/DialogueTools/Skip/Button");
                        Require(world.Root.activeSelf && world.RpgClears == 53 && CollectionProgress.Balance == balance,
                            "GX memory replay changed the save or awarded duel rewards again.");
                    }
                }

                void VerifyGxArtwork(TagForceData configuration, string[] cutscenes)
                {
                    var sprites = new TagForceWorldImages();
                    try
                    {
                        foreach (string key in configuration.roster.Select(actor => actor.charId.ToString()).Prepend("protagonist"))
                        {
                            string standing = key == "protagonist" ? "campaign_protagonist" : key;
                            Require(sprites.Get($"TagForce/GX/Standing/full_{standing}.png") != null &&
                                sprites.Get($"TagForce/GX/Characters_SD/sd_{key}.png") != null &&
                                sprites.GetSlices($"TagForce/GX/WalkCycles/sd_{key}_field.png", 3, 3, fixedCells: true)?.Length == 9,
                                $"Standing, SD or directional art did not load: {key}.");
                            var bust = sprites.Get($"TagForce/GX/DialoguePortraits/bust_{standing}.png");
                            Require(bust != null && bust.rect.width == bust.rect.height,
                                $"The GX conversation portrait is missing or has the wrong aspect ratio: {key}.");
                        }
                    }
                    finally { sprites.Clear(); }

                    var view = new StoryModeUI.StoryDialogueView(canvas.transform);
                    var example = cutscenes.Select(id => new StoryDialogueLine { speaker = "주인공", text = id, cutscene = id })
                        .Append(new StoryDialogueLine { speaker = "주인공", text = "필드로 돌아갑니다." }).ToArray();
                    view.Open("GX CG verification", "주인공", DeckRepository.GetCharacter(107), example, null,
                        rpg: true, rpgBackdrop: "Maps/spot_main_academy.png", rpgAssetFolder: "GX");
                    var viewType = typeof(StoryModeUI.StoryDialogueView);
                    var hero = (Image)viewType.GetField("_hero", flags).GetValue(view);
                    var opponent = (Image)viewType.GetField("_opponent", flags).GetValue(view);
                    Require(hero.sprite != null && opponent.sprite != null && hero.sprite.rect.width == hero.sprite.rect.height &&
                        opponent.sprite.rect.width == opponent.sprite.rect.height &&
                        hero.rectTransform.anchorMin.y == opponent.rectTransform.anchorMin.y &&
                        hero.rectTransform.pivot.y == 0f && opponent.rectTransform.pivot.y == 0f,
                        "GX conversation portraits did not share a square frame and bottom edge.");
                    var current = (Image)viewType.GetField("_cutsceneImage", flags).GetValue(view);
                    var previous = (Image)viewType.GetField("_cutscenePrevious", flags).GetValue(view);
                    var alpha = (CanvasGroup)viewType.GetField("_cutsceneOverlay", flags).GetValue(view);
                    var advance = viewType.GetMethod("Advance", flags);
                    for (int index = 0; index < cutscenes.Length; index++)
                    {
                        Require(current.sprite != null, $"GX CG did not load: {cutscenes[index]}.");
                        if (index > 0) Require(previous.sprite != null && current.color.a == 0f, "CG did not crossfade.");
                        view.Update(.5f);
                        Require(alpha.alpha == 1f && current.color.a == 1f && previous.sprite == null,
                            "A CG transition did not settle.");
                        advance.Invoke(view, null);
                        advance.Invoke(view, null);
                    }
                    view.Update(.5f);
                    Require(alpha.alpha == 0f, "CG did not fade back to the field.");
                    view.Close();
                    Debug.Log($"[VERIFY GX ART] All 27 sets of standing and directional character assets and all {cutscenes.Length} CG transition paths passed.");
                }
            }

            Transform FieldScene() => panel.Find("FieldViewport/Camera");

            RpgFieldTerrain CurrentTerrain() => (RpgFieldTerrain)typeof(TagForceWorldUI).GetField("_rpgTerrain", flags).GetValue(world);

            void VerifyGroundedPresentation()
            {
                var terrain = CurrentTerrain();
                var field = (RectTransform)FieldScene();
                var player = (RectTransform)field.Find("Player");
                var art = (RectTransform)player.Find("Sprite");
                var type = typeof(TagForceWorldUI);
                var pose = type.GetMethod("PoseRpgWalker", flags);
                var order = type.GetMethod("OrderRpgFieldActors", flags);
                var track = type.GetMethod("TrackRpgCamera", flags);
                var pan = type.GetField("_rpgCameraPan", flags);
                var prop = (RectTransform)field.Find("Landmark");
                var viewport = (RectTransform)field.parent;
                var floor = (RectTransform)field.Find("Scenery");
                var foreground = (RectTransform)field.Find("FieldForeground");
                var fieldPoint = player.anchorMin;
                var place = prop.anchorMin;
                var oldPan = (Vector2)pan.GetValue(world);
                var front = terrain.Spot.walkPolygon.OrderBy(point => point.y).First();
                var back = terrain.Spot.walkPolygon.OrderByDescending(point => point.y).First();
                var farWard = terrain.Ground(Vector2.Lerp(back, fieldPoint, .09f));
                var nearWard = terrain.Ground(Vector2.Lerp(front, fieldPoint, .09f));
                foreach (var foot in field.GetComponentsInChildren<TagForceRpgWalkAnimator>())
                    Require(terrain.Contains(((RectTransform)foot.transform.parent).anchorMin),
                        $"An actor spawned in an obstacle at {terrain.Spot.spotId}.");
                foreach (var anchor in field.GetComponentsInChildren<RectTransform>())
                {
                    string id = anchor.name;
                    if (id == "Player" || id == "Landmark" || id == "HiddenCache" || id == "SideQuestItem" ||
                        id.StartsWith("Pickup_", StringComparison.Ordinal) || id.StartsWith("ExitTarget_", StringComparison.Ordinal))
                        Require(terrain.Contains(anchor.anchorMin), $"Object {id} was off the floor of {terrain.Spot.spotId}.");
                }
                pose.Invoke(world, new object[] { player, art, nearWard, Vector2.zero, false, Vector2.left });
                var nearScale = player.localScale.x;
                Require(art.localScale.x < 0f && nearScale > 1f, "Near perspective inverted or shrank the actor.");
                pose.Invoke(world, new object[] { player, art, farWard, Vector2.zero, false, Vector2.right });
                Require(art.localScale.x > 0f && player.localScale.x < nearScale && player.localScale.x > 0f,
                    "Far perspective did not preserve facing or reduce scale.");
                Require(player.Find("GroundShadow/ContactCore").GetComponent<Image>().color.a >
                    player.Find("GroundShadow").GetComponent<Image>().color.a && art.GetComponent<Image>().color != Color.white,
                    "A field actor omitted its contact core or environmental tint.");
                var particles = field.Find("Footfalls");
                int poolSize = particles.childCount;
                pose.Invoke(world, new object[] { player, art, farWard, new Vector2(.1f, 0f), true, Vector2.right });
                Require(particles.Cast<Transform>().Any(particle => particle.gameObject.activeSelf),
                    "A planted foot did not emit a visible particle.");
                type.GetMethod("UpdateRpgFieldEffects", flags).Invoke(world, new object[] { 1f });
                Require(particles.childCount == poolSize &&
                    particles.Cast<Transform>().All(particle => !particle.gameObject.activeSelf),
                    "Footfall particles did not return to the pool.");

                prop.anchorMin = prop.anchorMax = (farWard + nearWard) * .5f;
                order.Invoke(world, null);
                Require(player.GetSiblingIndex() < prop.GetSiblingIndex(), "An actor behind a field object was drawn in front.");
                pose.Invoke(world, new object[] { player, art, nearWard, Vector2.zero, false, Vector2.left });
                order.Invoke(world, null);
                Require(player.GetSiblingIndex() > prop.GetSiblingIndex() &&
                    foreground.GetSiblingIndex() > player.GetSiblingIndex(), "Y ordering or the foreground broke at a depth crossing.");

                var corners = new Vector3[4];
                foreach (var edge in terrain.Spot.walkPolygon)
                {
                    track.Invoke(world, new object[] { 1f, edge, 0f });
                    Canvas.ForceUpdateCanvases();
                    floor.GetWorldCorners(corners);
                    var min = viewport.InverseTransformPoint(corners[0]);
                    var max = viewport.InverseTransformPoint(corners[2]);
                    Require(min.x <= viewport.rect.xMin + .01f && min.y <= viewport.rect.yMin + .01f &&
                        max.x >= viewport.rect.xMax - .01f && max.y >= viewport.rect.yMax - .01f,
                        $"The camera exposed empty space at the edge of {terrain.Spot.spotId}.");
                    var offset = (Vector2)pan.GetValue(world);
                    var span = (field.anchorMax - field.anchorMin) * field.localScale.x;
                    Require(Vector2.Distance(Vector2.Scale(foreground.anchorMin, span), .18f * offset) < .0001f,
                        $"The foreground drifted from its 1.18x camera motion in {terrain.Spot.spotId}.");
                }
                prop.anchorMin = prop.anchorMax = place;
                pose.Invoke(world, new object[] { player, art, fieldPoint, Vector2.zero, false, Vector2.zero });
                pan.SetValue(world, oldPan);
                track.Invoke(world, new object[] { 0f, fieldPoint, 0f });
                order.Invoke(world, null);
            }

            void VerifyEveryStageFootprint(StoryScriptRoot campaign)
            {
                var type = typeof(TagForceWorldUI);
                var show = type.GetMethod("Show", flags);
                var fieldPage = Enum.Parse(show.GetParameters()[0].ParameterType, "Field");
                var location = type.GetField("_rpgLocation", flags);
                var lastScene = type.GetField("_rpgLastPlayedSceneId", flags);
                int returnRegion = (int)location.GetValue(world);
                var returnScene = lastScene.GetValue(world);
                int branches = 0;
                for (int i = 0; i < campaign.episodes.Length; i++)
                {
                    var episode = campaign.episodes[i];
                    Require(world.EnterRpgRegion(world.CampaignRegionForEpisode(i + 1)), $"Unreachable stage {episode.id}.");
                    foreach (var lines in new[] { episode.preDialogues, episode.postWinDialogues, episode.postLoseDialogues })
                    {
                        if (lines == null || lines.Length == 0) continue;
                        world.BeginRpgDialogue(episode, campaign.protagonist, lines);
                        string previousMap = null;
                        for (int index = 0; index < lines.Length; index++)
                        {
                            var line = lines[index];
                            if (index != 0 && index + 1 != lines.Length && line.stage?.Length is not > 0 &&
                                previousMap == line.SceneMapPath) continue;
                            world.SetRpgDialogueLine(lines, index, playMotion: false);
                            previousMap = line.SceneMapPath;
                            var terrain = CurrentTerrain();
                            var cast = FieldScene().Find("DialogueCast");
                            foreach (RectTransform actor in cast)
                                Require(terrain.Contains(actor.anchorMin),
                                    $"{actor.name} was outside the floor of {terrain.Spot.spotId} at {episode.id} line {index + 1}.");
                        }
                        branches++;
                        show.Invoke(world, new[] { fieldPage });
                    }
                }
                Require(world.EnterRpgRegion(returnRegion), "The terrain stage test did not restore the field.");
                lastScene.SetValue(world, returnScene);
                Debug.Log($"[VERIFY 2.5D STAGES] {world.RpgCampaignId}: {branches} opening, victory and defeat branches grounded.");
            }

            (Vector2 Min, Vector2 Max) CameraFrame()
            {
                var camera = (RectTransform)FieldScene();
                return (camera.anchorMin, camera.anchorMax);
            }

            Vector2 ViewportSize()
            {
                Canvas.ForceUpdateCanvases();
                return ((RectTransform)panel.Find("FieldViewport")).rect.size;
            }

            bool WorldInputRestored()
            {
                var group = (CanvasGroup)typeof(TagForceWorldUI).GetField("_worldInput", flags).GetValue(world);
                return group.interactable && group.blocksRaycasts;
            }

            void VerifyDialogueBounds(StoryScriptRoot campaign, string assetFolder)
            {
                var host = DuelUiKit.FillRect("DialogueAspectVerification", canvas.transform);
                host.anchorMin = host.anchorMax = Vector2.one * .5f;
                var view = new StoryModeUI.StoryDialogueView(host);
                var dialogueLines = campaign.episodes.SelectMany(entry => entry.preDialogues
                    .Concat(entry.postWinDialogues).Concat(entry.postLoseDialogues)).ToArray();
                var longest = dialogueLines.OrderByDescending(line => line.text.Length).First();
                var dialogueType = typeof(StoryModeUI.StoryDialogueView);
                var panelImage = (Image)dialogueType.GetField("_dialoguePanel", flags).GetValue(view);
                var label = (Text)dialogueType.GetField("_text", flags).GetValue(view);
                var controls = host.Find("StoryDialogue/Scene/DialogueTools");
                Vector2[] displays = { new(1920, 1080), new(1920, 1200), new(2560, 1080),
                    new(1280, 960), new(2340, 1080) };
                try
                {
                    foreach (var display in displays)
                    {
                        float canvasScale = Mathf.Sqrt(display.x / 1600f * (display.y / 900f));
                        host.sizeDelta = display / canvasScale;
                        bool mobile = display.x == 2340;
                        if (mobile) host.sizeDelta -= new Vector2(160f, 44f) / canvasScale;
                        view.Open("Layout verification", campaign.protagonist, (OpponentCharacter)null, new[] { longest },
                            null, rpg: true, backdropBelow: true, rpgAssetFolder: assetFolder);
                        Canvas.ForceUpdateCanvases();
                        var characters = (int[])dialogueType.GetField("_characters", flags).GetValue(view);
                        dialogueType.GetMethod("Reveal", flags).Invoke(view, new object[] { characters.Length });
                        Require(panelImage.rectTransform.rect.height >= host.rect.height * .32f &&
                            label.resizeTextMinSize >= 23 && label.text == longest.text,
                            $"RPG dialogue was too small or incomplete on {assetFolder}, {display}.");
                        var settings = label.GetGenerationSettings(label.rectTransform.rect.size);
                        settings.resizeTextForBestFit = false;
                        settings.fontSize = label.resizeTextMinSize;
                        settings.verticalOverflow = VerticalWrapMode.Overflow;
                        float height = new TextGenerator().GetPreferredHeight(label.text, settings) / label.pixelsPerUnit;
                        Require(height > 0f && height <= label.rectTransform.rect.height + 1f,
                            $"RPG dialogue overflow on {assetFolder}, {display}: {height} vs {label.rectTransform.rect.height}.");
                        if (assetFolder == "GX")
                        {
                            var generator = new TextGenerator();
                            foreach (var line in dialogueLines)
                            {
                                float lineHeight = generator.GetPreferredHeight(line.text, settings) / label.pixelsPerUnit;
                                Require(lineHeight > 0f && lineHeight <= label.rectTransform.rect.height + 1f,
                                    $"A GX dialogue was clipped on {display}: {line.speaker} / {line.text}");
                            }
                        }
                        var buttons = controls.Cast<RectTransform>().Select(Bounds).ToArray();
                        var box = Bounds(panelImage.rectTransform);
                        Require(host.rect.Contains(box.min) && host.rect.Contains(box.max),
                            $"RPG dialogue escaped the content area on {assetFolder}, {display}.");
                        for (int i = 0; i < buttons.Length; ++i)
                        {
                            Require(host.rect.Contains(buttons[i].min) && host.rect.Contains(buttons[i].max) &&
                                buttons.Skip(i + 1).All(button => !buttons[i].Overlaps(button)),
                                $"Dialogue controls escaped or overlapped on {assetFolder}, {display}.");
                        }

                        var open = dialogueType.GetMethod("OpenLog", flags);
                        var advance = dialogueType.GetMethod("Advance", flags);
                        open.Invoke(view, null);
                        advance.Invoke(view, null);
                        Require(view.ClosePopup() && !view.ClosePopup(),
                            $"The RPG dialogue popup did not release input on {assetFolder}, {display}.");
                        view.Close();
                    }
                }
                finally
                {
                    view.Close();
                    UnityEngine.Object.DestroyImmediate(host.gameObject);
                }

                Rect Bounds(RectTransform item)
                {
                    var corners = new Vector3[4];
                    item.GetWorldCorners(corners);
                    return Rect.MinMaxRect(host.InverseTransformPoint(corners[0]).x,
                        host.InverseTransformPoint(corners[0]).y, host.InverseTransformPoint(corners[2]).x,
                        host.InverseTransformPoint(corners[2]).y);
                }
            }

            void PressRpgNavigation(string option)
            {
                var nav = panel.parent.Find("RpgNavigation");
                if (!nav.gameObject.activeInHierarchy) Press(panel, "FieldHud/MenuToggle");
                Require(nav.gameObject.activeInHierarchy, "Opening the field menu did not show its commands.");
                Press(nav, option);
            }

            int CurrentLine()
            {
                var dialogue = typeof(StoryModeUI).GetField("_dialogue", flags).GetValue(story);
                return (int)typeof(StoryModeUI.StoryDialogueView).GetField("_index", flags).GetValue(dialogue);
            }

            void AwardDuel(bool win)
            {
                var duel = Get("_session") as DuelSession;
                Require(duel?.NativePtr != IntPtr.Zero, "The native duel never started.");
                duel.Surrender(win ? duel.AiPlayerIndex : duel.HumanPlayerIndex);
                typeof(GameAppController).GetMethod("Update", flags).Invoke(app, null);
                Require(Get("_session") == null && duel.NativePtr == IntPtr.Zero, "The native duel never ended.");
            }

            void Set(string field, object value) => typeof(GameAppController).GetField(field, flags).SetValue(app, value);
            object Get(string field) => typeof(GameAppController).GetField(field, flags).GetValue(app);
            void Press(Transform parent, string path)
            {
                var button = parent.Find(path)?.GetComponent<Button>();
                Require(button != null && button.IsInteractable(), $"Expected button {path}.");
                button.onClick.Invoke();
            }
        }
    }
}
