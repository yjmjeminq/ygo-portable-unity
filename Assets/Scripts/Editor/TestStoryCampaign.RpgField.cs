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

        private static void VerifyRpgField(UserDeck playerDeck, string save)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            string streaming = Application.streamingAssetsPath;
            var script = StoryModeUI.LoadRpgScript(streaming);
            var decks = StoryModeUI.LoadRpgDecks(streaming);
            var regions = TagForceWorldUI.RpgRegions;
            int[] gates = { 0, 0, 2, 6, 8, 8, 18, 27, 27, 34, 44 };
            int[] expectedRegions =
            {
                0, 0, 1, 2, 2, 2, 3, 3, 5, 5, 5, 5, 5, 4, 4, 4, 4, 4, 6, 6,
                6, 6, 6, 6, 6, 6, 6, 7, 7, 4, 4, 1, 4, 1, 9, 9, 9, 9, 9, 9,
                9, 9, 9, 9, 10, 10, 10, 10, 10, 8,
            };
            Require(Enumerable.Range(1, script.episodes.Length).Select(TagForceWorldUI.RegionForEpisode)
                .SequenceEqual(expectedRegions), "A 5D's episode was routed to the wrong field.");
            Require(regions.Select(region => region.RequiredEpisode).SequenceEqual(gates),
                "The eleven RPG region gates did not match the campaign.");
            Require(regions.Select(region => region.AdventureSlot).SequenceEqual(new[] { 9, 0, 1, 2, 3, 4, 5, 10, 6, 7, 8 }),
                "Reordering the map reassigned an existing cache or partner-conversation save slot.");
            Require(LightingForEpisode(regions[RpgBridge], 49) != null && LightingForEpisode(regions[RpgBridge], 50) == null,
                "The bridge must switch from daylight to sunset for the ending.");
            for (int i = 0; i <= 50; i++)
            {
                int count = TagForceWorldUI.CountClearedRpgEpisodes(script, id =>
                    script.episodes.Take(i).Any(ep => ep.id == id));
                Require(count == i && regions[TagForceWorldUI.RegionForEpisode(i + 1)].IsOpen(i),
                    $"No reachable target after {i} RPG clears.");
                foreach (var region in regions)
                    Require(region.IsOpen(i) == (i >= region.RequiredEpisode), $"Bad unlock gate: {region.Id}.");
            }
            foreach (var region in regions)
            {
                Require(File.Exists(Path.Combine(streaming, region.Background)), $"Missing 5D's map {region.Id}.");
                Require(File.Exists(Path.Combine(streaming, region.FieldBackground)), $"Missing 5D's field art {region.Id}.");
            }
            string[] lightingBackdrops =
            {
                "Maps/spot_kai_garage_morning.png", "Maps/spot_poppo_time_spring.png",
                "Maps/spot_daedalus_bridge_daylight.png", "Maps/spot_wrgp_circuit_blue_hour.png",
            };
            Require(regions.Select(region => region.LightingBackdrop).Where(path => path != null).SequenceEqual(lightingBackdrops),
                "The four relit maps were not registered in their intended regions.");

            const string preference = "ygo.5ds-rpg.last-region";
            string oldPreference = PlayerPrefs.GetString(preference);
            bool hadPreference = PlayerPrefs.HasKey(preference);
            PlayerPrefs.DeleteKey(preference);
            var canvas = new GameObject("5D's field verification", typeof(Canvas));
            var events = new GameObject("5D's field controls", typeof(EventSystem));
            var priorEvents = EventSystem.current;
            var input = events.GetComponent<EventSystem>();
            if (EventSystem.current != input) typeof(EventSystem).GetMethod("OnEnable", flags).Invoke(input, null);
            EventSystem.current = input;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
            var app = canvas.AddComponent<GameAppController>();
            app.AiDifficulty = AIDifficulty.Normal;
            var story = new StoryModeUI(app, canvas.transform, new StoryHandSequence(0));
            var world = new TagForceWorldUI(app, canvas.transform);
            var menu = new TitleAndMenuUI(app, canvas.transform);
            var modals = new DuelPromptModals(canvas.transform, null, null);
            var panel = world.Root.transform.Find("World");
            var storyRoot = story.Root.transform.Find("StoryDialogue");
            var loaded = story.GetRpgScript(StoryModeUI.RpgSeriesId);
            var episode6 = loaded.episodes[5];
            try
            {
                Set("_titleAndMenu", menu);
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

                app.ShowScreen(AppScreen.MainMenu);
                var entry = menu.MenuRoot.GetComponentsInChildren<Button>()
                    .Single(button => button.transform.parent.name == "RpgWorld");
                entry.onClick.Invoke();
                Require(!world.CanWalk && panel.Find("StoryDialogue/Scene/DialogueBox") != null,
                    "A new RPG save did not enter the skippable prologue.");
                Press(panel, "StoryDialogue/Scene/DialogueTools/Skip/Button");
                Require(CollectionProgress.RpgIntroSeen, "Skipping the prologue did not persist completion.");
                Require(world.Root.activeSelf && FieldScene().Find("Player/Sprite").GetComponent<Image>().sprite != null &&
                    FieldScene().Find("MainQuestNpc/MainQuestMarker").GetComponent<Image>().sprite != null &&
                    world.CanStartRpgEpisode(0) && !world.CanStartRpgEpisode(1),
                    "RPG did not start as a walkable field with the first main quest.");
                PressRpgNavigation("Map");
                var nodes = panel.Find(MapNodes);
                Require(nodes.childCount == regions.Length && nodes.GetComponentsInChildren<Button>().Length == 11,
                    "The world map did not contain eleven selectable locations.");
                var currentPulse = nodes.Find("spot_duel_academia/CurrentPulse").GetComponent<Image>();
                var goalPulse = nodes.Find("spot_duel_academia/GoalPulse").GetComponent<Image>();
                float currentAlpha = currentPulse.color.a, goalAlpha = goalPulse.color.a;
                world.Update(.23f);
                Require(currentPulse.color.a != currentAlpha && goalPulse.color.a != goalAlpha,
                    "The current position and the objective did not pulse independently.");
                var travel = panel.Find("RegionPreview/Travel").GetComponent<Button>();
                foreach (var region in regions)
                {
                    Press(nodes, region.Id);
                    Require(travel.interactable == region.IsOpen(0) &&
                        panel.Find("RegionPreview/RegionName/Label").GetComponent<Text>().text == region.Name,
                        "A world map pin selected an incorrect preview or did not respect its unlock gate.");
                }
                Press(nodes, "spot_facility_prison");
                travel.onClick.Invoke();
                Require(nodes.Find("spot_facility_prison/MapLock").GetComponent<Image>().sprite != null &&
                    !world.CanWalk && !world.EnterRpgRegion(RpgPrison), "The world map allowed early entry to the prison.");
                PressRpgNavigation("Decks");
                app.CloseDeckEditor();
                Require(panel.Find(MapNodes + "/spot_facility_prison/MapLock") != null,
                    "Returning from deck editing did not restore the RPG world map.");
                world.Back();

                VerifyRpgHud();
                VerifyOneButtonExit();
                VerifyFieldMovement();
                VerifyDirectionalArt();
                VerifyResidentProfiles();
                VerifyResidentActivity();
                VerifyDialoguePortraitAssets();
                VerifyWardrobe();
                VerifySceneGuests();
                VerifyStageExit();

                var local = FieldScene().Find($"Resident_{script.episodes[1].charId}");
                Require(local.Find("ResidentMarker").GetComponent<Image>().sprite != null, "The resident marker was missing.");
                Press(local, "SD");
                world.Update(4f);
                Press(panel, "ResidentChoices/TalkDaily");
                var ordinary = panel.Find("ResidentChoices");
                Require(ordinary.Find("DailyLine/Label").GetComponent<Text>().text.Length > 18 &&
                    ordinary.Find("DialogueWindow").GetComponent<Image>().sprite.name == "RPG dialogue glass" &&
                    ordinary.Find("NamePlate/Signature") != null &&
                    ((RectTransform)ordinary.Find("TalkDaily")).anchorMin.x > ((RectTransform)ordinary.Find("DialogueWindow")).anchorMax.x,
                    "The resident's dialogue or floating action menu did not use the RPG layout.");
                Press(panel, "ResidentChoices/Practice");
                int balance = CollectionProgress.Balance;
                int owned = decks[script.episodes[1].deckId].main.Distinct().Sum(CollectionProgress.GetTotalOwnedCount);
                Press(panel, "StartPractice");
                RequireMasterRpgAi();
                AwardDuel(win: true);
                Require(world.Root.activeSelf && world.RpgClears == 0 &&
                    CollectionProgress.Balance == balance + 300 &&
                    decks[script.episodes[1].deckId].main.Distinct().Sum(CollectionProgress.GetTotalOwnedCount) == owned + 1,
                    "A practice win did not award DP and a card without advancing the story.");
                for (int i = 0; i < 2; i++)
                {
                    var opened = script.episodes[i];
                    Require(CollectionProgress.TryAwardStoryDuel(opened.id, true, null, decks[opened.deckId],
                        out _, out _, out _), $"Could not stage {opened.id}.");
                }
                VerifySideQuests();

                foreach (var episode in script.episodes.Skip(world.RpgClears).Take(5 - world.RpgClears))
                    Require(CollectionProgress.TryAwardStoryDuel(episode.id, true, null, decks[episode.deckId],
                        out _, out _, out _), $"Could not stage {episode.id}.");
                Require(world.EnterRpgRegion(RpgHideout) && world.CanStartRpgEpisode(5), "Episode 6 NPC was not in the hideout.");
                OpenEpisode6();
                app.StartStoryDuel(episode6, playerDeck, true);
                RequireMasterRpgAi();
                AwardDuel(win: false);
                Require(storyRoot.gameObject.activeSelf && world.Root.activeSelf && !world.CanWalk && world.RpgClears == 5,
                    "A loss did not stage its dialogue in the locked field.");
                Press(storyRoot, "Scene/DialogueTools/Skip/Button");
                Require(world.Root.activeSelf && world.CanStartRpgEpisode(5) && !world.EnterRpgRegion(RpgPrison),
                    "A loss changed the active NPC or unlocked the next region.");

                OpenEpisode6();
                app.StartStoryDuel(episode6, playerDeck, true);
                balance = CollectionProgress.Balance;
                AwardDuel(win: true);
                Require(storyRoot.gameObject.activeSelf && world.RpgClears == 5 && CollectionProgress.Balance == balance,
                    "The win was settled before the post-win dialogue.");
                Press(storyRoot, "Scene/DialogueTools/Skip/Button");
                Require(world.Root.activeSelf && world.RpgClears == 6 && CollectionProgress.Balance > balance &&
                    panel.Find("RegionUnlockToast/Label").GetComponent<Text>().text.Contains("시큐리티 수용소"),
                    "Winning did not save the clear, pay DP and announce the new region.");
                CollectionProgress.Load(save);
                Require(world.EnterRpgRegion(RpgPrison) && world.CanStartRpgEpisode(6) && world.RpgClears == 6,
                    "The next quest or unlocked region was lost after reloading the save.");
                VerifyAutomaticScenes();
                foreach (int region in Enumerable.Range(0, regions.Length))
                {
                    var request = RpgSideQuest.All.First(quest => quest.Region == regions[region].Id);
                    Require(world.EnterRpgRegion(region) && FieldScene().Find($"QuestNpc_{request.Giver}/SD").GetComponent<Image>().sprite != null,
                        $"The region {request.Region} was missing its quest giver or SD art.");
                    VerifyAtmosphere(region);
                }
                VerifyMapLighting();
                PressRpgNavigation("Quests");
                Press(panel, "QuestPageNext");
                Require(panel.Find("QuestPage/Label").GetComponent<Text>().text == "2 / 3",
                    "The quest journal could not page through all regions.");
                world.Back();
                VerifyRpgExploration();
                VerifyRpgAdventure();
                VerifyReturningEpisodes();
                VerifyRpgPartnerPolish();
                VerifyRpgResort();
                Debug.Log("[VERIFY 5DS FIELD] Map gates, guest entrances, movement lock, practice rewards, story result order and saved progression passed.");
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
                if (hadPreference) PlayerPrefs.SetString(preference, oldPreference);
                else PlayerPrefs.DeleteKey(preference);
                PlayerPrefs.Save();
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
                var dialogue = typeof(StoryModeUI).GetField("_dialogue", flags).GetValue(story);
                var dialogueType = typeof(StoryModeUI.StoryDialogueView);
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
                Require(expectedCg.Length == 24 && expectedCg.All(id =>
                    File.Exists(Path.Combine(streaming, "TagForce/GX/Cutscenes", id + ".png"))), "Missing GX event CG.");

                try
                {
                    PlayerPrefs.DeleteKey(gxPreference);
                    DeckRepository.Load(streaming, isolatedSave, seedDefaultDecks: false);
                    CollectionProgress.Load(isolatedSave);
                    Require(CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out var startingDeck, out string error), error);
                    DeckRepository.SaveUserDeck(startingDeck);
                    CollectionProgress.RememberDuelDeck(startingDeck.id);
                    app.OpenGxRpgField();
                    Require(world.Root.activeSelf && world.RpgCampaignId == gx && !world.CanWalk, "GX prologue did not start automatically.");
                    int scenes = 0, duels = 0, pov = 0;
                    for (int index = 0; index < gxScript.episodes.Length; index++)
                    {
                        var episode = gxScript.episodes[index];
                        Require(world.RpgClears == index, $"GX sequence skipped or stalled at {episode.id}.");
                        int location = world.CampaignRegionForEpisode(index + 1);
                        Require(world.CampaignRegion(location).Id == episode.regionId &&
                            world.CampaignRegion(location).IsOpen(index), $"Unreachable GX destination for {episode.id}.");
                        if (episode.isCutsceneOnly)
                        {
                            for (int frame = 0; frame < 12 && !storyRoot.gameObject.activeInHierarchy; frame++)
                                world.Update(.18f);
                            Require(storyRoot.gameObject.activeInHierarchy && !world.CanWalk &&
                                FieldScene().Find("DialogueCast") != null && Get("_session") == null,
                                $"Automatic cutscene did not lock controls at {episode.id}.");
                            VerifyGxWalkerSizes();
                            Require(storyRoot.Find("Scene/DialogueTools/Auto/Button").GetComponentInChildren<Text>()
                                .text == "오토 켜짐", $"Automatic playback is off for {episode.id}.");
                            VerifyDialogueStart(episode.preDialogues, episode.id);
                            ReadDialogue(episode);
                            Require(world.RpgClears == index + 1 && Get("_session") == null,
                                $"A cutscene did not advance directly at {episode.id}.");
                            scenes++;
                            continue;
                        }

                        if (episode.id == "gx_ep_01")
                        {
                            Press(panel, "FieldHud/QuestTrack/Guide");
                            Require(panel.Find("RegionPreview/RegionName/Label").GetComponent<Text>().text == world.CampaignRegion(location).Name,
                                "The GX field guide did not preselect the objective region on the map.");
                            world.Back();
                        }
                        Require(world.EnterRpgRegion(location) && world.CanStartRpgEpisode(index) &&
                            FieldScene().Find("Scenery").GetComponent<Image>().sprite != null &&
                            panel.Find("FieldHud/FieldDetails/GxOpponentTier/Value/Label") != null,
                            $"GX duel HUD or field was missing for {episode.id}.");
                        bool isPov = episode.playAsCharacterId > 0;
                        string identity = FieldScene().Find("Player/GxIdentity/Label").GetComponent<Text>().text;
                        Require(identity.Contains(isPov ? "아스카" : "윤"), $"Wrong field point of view at {episode.id}.");
                        VerifyGxWalkerSizes();
                        if (episode.id == "gx_ep_01")
                        {
                            var fieldHud = panel.Find("FieldHud");
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
                        else app.OpenRpgFieldEpisode(index);
                        Require(story.IsGxRpg && story.GetOpponentMatch(episode).Deck?.id == episode.deckId &&
                            FieldScene().Find("DialogueCast") != null, $"GX episode used the wrong deck or stage at {episode.id}.");
                        VerifyGxWalkerSizes();
                        VerifyDialogueStart(episode.preDialogues, episode.id);
                        if (episode.id == "gx_ep_01")
                        {
                            VerifySpeaker(episode.preDialogues, "윤", player: true);
                            app.StartStoryDuel(episode, startingDeck, true);
                            AwardDuel(win: false);
                            VerifyDialogueStart(episode.postLoseDialogues, episode.id);
                            VerifySpeaker(episode.postLoseDialogues, "윤", player: true);
                            Press(storyRoot, "Scene/DialogueTools/Skip/Button");
                            Require(world.RpgClears == index && world.CanStartRpgEpisode(index),
                                "A GX loss advanced the story or prevented retrying.");
                            app.OpenRpgFieldEpisode(index);
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
                        VerifyGxDuelDialogueArt(episode);
                        app.StartStoryDuel(episode, startingDeck, true);
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
                        else Require(ReferenceEquals(Get("_activeMyDeck"), startingDeck), "GX POV deck was not restored.");
                        var fieldUi = Get("_duelField") as DuelFieldUI;
                        var name = typeof(DuelFieldUI).GetField("_myNameText", flags).GetValue(fieldUi) as Text;
                        Require(name?.text.Contains(isPov ? "아스카" : "윤") == true,
                            $"The duel HUD lost the current player name at {episode.id}.");
                        AwardDuel(win: true);
                        VerifyDialogueStart(episode.postWinDialogues, episode.id);
                        if (episode.id == "gx_ep_27") VerifySpeaker(episode.postWinDialogues, "윤", player: false);
                        Press(storyRoot, "Scene/DialogueTools/Skip/Button");
                        Require(world.RpgClears == index + 1 && CollectionProgress.LastDuelDeckId == startingDeck.id,
                            $"Duel result or previous deck was lost after {episode.id}.");
                        duels++;
                    }

                    CollectionProgress.Load(isolatedSave);
                    Require(world.RpgClears == 53 && scenes == 19 && duels == 34 && pov == 3 &&
                        !CollectionProgress.IsStoryEpisodeCleared("gx_ep_17") && CollectionProgress.RpgAffinity == 0,
                        "GX progress did not survive reloading or overwrote classic GX progress.");
                    foreach (int location in Enumerable.Range(0, gxMap.spots.Length))
                        Require(world.EnterRpgRegion(location) && FieldScene().Find("Scenery").GetComponent<Image>().sprite != null,
                            $"Unable to enter GX map {gxMap.spots[location].spotId}.");
                    PressRpgNavigation("Map");
                    Require(panel.Find(MapNodes)?.childCount == 12, "The GX world map lost pin buttons.");
                    world.Back();
                    VerifyGxArtwork(gxMap, expectedCg);
                    Debug.Log("[VERIFY GX DIALOGUE] Event, victory and defeat scripts rendered, with distinct Yun, Asuka and possessed Yun speaker roles.");
                    Debug.Log("[VERIFY GX FIELD] 53 consecutive sequences, 34 Master duels, 19 automatic lockouts, all 12 maps and three temporary POV decks passed.");
                }
                finally
                {
                    if (hadLocation) PlayerPrefs.SetString(gxPreference, previousLocation);
                    else PlayerPrefs.DeleteKey(gxPreference);
                    PlayerPrefs.Save();
                }

                void VerifyGxWalkerSizes()
                {
                    var walkers = FieldScene().GetComponentsInChildren<TagForceRpgWalkAnimator>(true);
                    Require(walkers.Length > 0 && walkers.All(walker => walker.transform.parent is RectTransform actor &&
                        actor.sizeDelta.y >= 130f && actor.pivot.y < .5f),
                        "A GX field or staged walker is too small or grows downward from the ground point.");
                }

                void VerifyDialogueStart(StoryDialogueLine[] lines, string episodeId)
                {
                    Require(storyRoot.gameObject.activeInHierarchy && CurrentLine() == 0 &&
                        ReferenceEquals(dialogueType.GetField("_dialogue", flags).GetValue(dialogue), lines),
                        $"The wrong dialogue branch opened at {episodeId}.");
                    VerifyDisplayedText(lines[0]);
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
                    for (int tick = 0; tick < lines.Length * 5 + 5 && storyRoot.gameObject.activeInHierarchy; ++tick)
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

            void VerifyAtmosphere(int region)
            {
                var field = FieldScene();
                var layer = field.Find("FieldAtmosphere");
                var shadow = field.Find("Player/GroundShadow").GetComponent<Image>();
                Require(layer != null && layer.GetSiblingIndex() > field.Find("Scenery").GetSiblingIndex() &&
                    layer.GetSiblingIndex() < field.Find("Player").GetSiblingIndex() &&
                    layer.GetComponentsInChildren<Image>().All(image => !image.raycastTarget) &&
                    shadow.sprite != null && !shadow.raycastTarget &&
                    (layer.Find("Haze") != null) == (region is RpgMoment or RpgBridge or RpgArk),
                    $"The atmosphere of {regions[region].Id} obscured the actors or blocked input.");
                var mote = (RectTransform)layer.Find("Mote_0");
                var light = layer.Find("Light").GetComponent<Image>();
                Vector2 origin = mote.anchorMin;
                float opacity = light.color.a;
                world.Update(.31f);
                Require(mote.anchorMin != origin && light.color.a != opacity,
                    $"The atmosphere of {regions[region].Id} did not animate in the field.");
            }

            void VerifyMapLighting()
            {
                var images = (TagForceWorldImages)typeof(TagForceWorldUI).GetField("_images", flags).GetValue(world);
                foreach (var (region, index) in regions.Select((region, index) => (region, index)))
                {
                    Require(world.EnterRpgRegion(index), $"Could not enter {region.Id} for the map lighting check.");
                    var baseScenery = images.Get(region.FieldBackground);
                    var lighting = LightingForEpisode(region, world.RpgClears + 1);
                    var expected = lighting == null ? baseScenery : images.Get("TagForce/5DS/" + lighting);
                    Require(expected != null && expected.rect.size == baseScenery.rect.size &&
                        FieldScene().Find("Scenery").GetComponent<Image>().sprite == expected,
                        $"The field used incorrect artwork or altered the framing of {region.Id}.");
                }
                PressRpgNavigation("Map");
                foreach (var region in regions.Where(region => region.LightingBackdrop != null))
                {
                    Press(panel, MapNodes + "/" + region.Id);
                    var lighting = LightingForEpisode(region, world.RpgClears + 1);
                    string path = lighting == null ? region.FieldBackground : "TagForce/5DS/" + lighting;
                    Require(panel.Find("RegionPreview/Thumbnail").GetComponent<Image>().sprite ==
                        images.Get(path), $"The map preview used old artwork for {region.Id}.");
                }
                Press(panel, MapNodes + "/spot_kai_garage");
                Press(panel, "RegionPreview/Travel");
                PressRpgNavigation("Quests");
                world.Update(.3f);
                world.Update(.6f);
                Require(world.CanWalk && FieldScene().Find("RpgCardMerchant") != null,
                    "The unlocked map pin did not start the normal travel sequence.");
                Require(world.EnterRpgRegion(RpgPoppo) && FieldScene().Find("RpgCardMerchant") != null,
                    "The late-campaign merchant was missing from Poppo Time.");
                Debug.Log("[VERIFY 5DS MAP LIGHTING] All eleven fields and the four revised map previews loaded with the original framing.");
            }

            Transform FieldScene() => panel.Find("FieldViewport/Camera");

            void PressRpgNavigation(string option)
            {
                var nav = world.Root.transform.Find("RpgNavigation");
                if (!nav.gameObject.activeInHierarchy) Press(panel, "FieldHud/MenuToggle");
                Require(nav.gameObject.activeInHierarchy, "Opening the field menu did not show its commands.");
                Press(nav, option);
            }

            void VerifyRpgHud()
            {
                var hud = panel.Find("FieldHud");
                var action = hud.Find("ContextAction");
                var padSubmit = new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Submit);
                var mapButton = world.Root.transform.Find("RpgNavigation/Map").GetComponent<Button>();
                Require(((Image)mapButton.targetGraphic).type == Image.Type.Sliced &&
                    mapButton.colors.fadeDuration > 0f && mapButton.colors.selectedColor != mapButton.colors.normalColor &&
                    mapButton.GetComponentInChildren<Text>().font == Resources.Load<Font>("StoryFonts/GowunDodum-Regular"),
                    "RPG navigation lost its rounded treatment or visible controller focus.");
                var player = (RectTransform)FieldScene().Find("Player");
                var radarPlayer = (RectTransform)hud.Find("Minimap/Radar/Points/Player");
                var points = radarPlayer.parent;
                Require(points.Find("Story").GetComponent<Image>().sprite == RpgUiStyle.Glyph(RpgUiStyle.Symbol.Alert) &&
                    points.Find("Cache").GetComponent<Image>().sprite == RpgUiStyle.Glyph(RpgUiStyle.Symbol.Star) &&
                    points.Find($"Exit_{RpgGarage}").GetComponent<Image>().sprite == RpgUiStyle.Glyph(RpgUiStyle.Symbol.Arrow) &&
                    points.GetComponentsInChildren<Image>().Any(icon => icon.sprite == RpgUiStyle.Glyph(RpgUiStyle.Symbol.Dot) &&
                        icon.transform != radarPlayer), "The radar did not expose the required live field marker types.");
                var radarStart = radarPlayer.anchorMin;
                var start = player.anchorMin;
                Require(action.gameObject.activeSelf && !action.GetComponent<Button>().interactable && !mapButton.gameObject.activeInHierarchy &&
                    panel.Find("FieldViewport").GetComponent<CanvasGroup>().blocksRaycasts &&
                    hud.Find("Deck/DeckName/Label").GetComponent<Text>().text == playerDeck.name &&
                    hud.Find("DuelistHud/Partner/PortraitFrame/Crop/Portrait").GetComponent<Image>().sprite != null &&
                    hud.Find("QuestTrack/Objective/Label").GetComponent<Text>().text.Contains(script.episodes[0].opponentName) &&
                    hud.Find("QuestTrack/GoalIcon").GetComponent<Image>().sprite != null,
                    "The field HUD exposed a far interaction, a duplicate map, or omitted the objective.");
                world.HandleFieldInput(padSubmit);
                world.Update(.25f);
                Require(player.anchorMin == start && world.CanWalk, "Selecting away from every target started an unintended walk.");

                var landmark = (RectTransform)FieldScene().Find("Exploration/Landmark");
                FieldScene().Find("Scenery").GetComponent<TagForceFloorTap>().Click(landmark.anchorMin);
                world.Update(4f);
                Require(radarPlayer.anchorMin != radarStart && radarPlayer.anchorMin == new Vector2(
                    Mathf.InverseLerp(.06f, .94f, player.anchorMin.x), Mathf.InverseLerp(.095f, .455f, player.anchorMin.y)),
                    "The radar did not track the player proportionally while walking.");
                Require(action.gameObject.activeSelf && action.Find("Label").GetComponent<Text>().text == "교정 게시판 · 조사",
                    "The prompt did not identify the nearby landmark.");
                var menuToggle = new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Search);
                world.HandleFieldInput(menuToggle);
                var atLandmark = player.anchorMin;
                world.Update(.4f, Vector2.left);
                Require(action.gameObject.activeSelf && !action.GetComponent<Button>().interactable && mapButton.gameObject.activeInHierarchy &&
                    world.RpgFieldMenuScope == mapButton.transform.parent.gameObject &&
                    EventSystem.current.currentSelectedGameObject == mapButton.gameObject &&
                    player.anchorMin == atLandmark && !panel.Find("FieldViewport").GetComponent<CanvasGroup>().blocksRaycasts,
                    "The field menu did not capture controller focus and suspend field interaction.");
                var motion = mapButton.GetComponent<MenuButtonMotion>();
                var surface = mapButton.GetComponent<ButtonSurfaceMotion>();
                var label = mapButton.GetComponentInChildren<Text>().rectTransform;
                var hitbox = ((RectTransform)mapButton.transform).rect;
                motion.Advance(.14f);
                Require(label.localScale.x > 1f && surface.Scale > 1f, "Controller focus did not animate the map button.");
                motion.OnPointerDown(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                motion.Advance(.12f);
                Require(label.localScale.x < 1f && surface.Scale < 1f && ((RectTransform)mapButton.transform).rect == hitbox,
                    "Pressing the button did not animate independently of its hitbox.");
                motion.OnPointerUp(null);
                var handlePad = typeof(GameAppController).GetMethod("HandleMenuInput", flags);
                handlePad.Invoke(app, new object[] { new DeckPadFrame(Vector2Int.down, DeckPadButtons.None), false });
                Require(EventSystem.current.currentSelectedGameObject == mapButton.transform.parent.Find("Quests").gameObject,
                    "The controller moved its focus outside the field commands.");
                handlePad.Invoke(app, new object[] { new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Cancel), false });
                Require(!mapButton.gameObject.activeInHierarchy && action.gameObject.activeSelf,
                    "Cancel did not return to the active field.");
                Press(hud, "ContextAction");
                world.Update(.1f);
                Require(!world.CanWalk && panel.Find("ResidentChoices/DailyLine/Label") != null,
                    "Tapping the action prompt opened the wrong target.");
                world.Back();
                Require(world.EnterRpgRegion(RpgGarage) && world.EnterRpgRegion(RpgAcademia), "Could not reset the field after HUD verification.");
                Debug.Log("[VERIFY 5DS HUD] Deck, partner, quest, proximity, touch action and controller menu passed.");
            }

            void VerifyOneButtonExit()
            {
                var start = FieldScene();
                start.Find("Scenery").GetComponent<TagForceFloorTap>().Click(new Vector2(.918f, .17f));
                world.Update(3f);
                var action = panel.Find("FieldHud/ContextAction").GetComponent<Button>();
                Require(FieldScene() == start && action.interactable && action.GetComponentInChildren<Text>().text ==
                    "카이의 정비소 · 이동", "An open exit could not be selected using the shared action button.");
                world.HandleFieldInput(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Submit));
                world.Update(.2f);
                world.Update(.8f);
                Require(FieldScene() != start && FieldScene().Find("RpgCardMerchant") != null,
                    "Controller confirm did not enter the selected exit.");
                Require(world.EnterRpgRegion(RpgAcademia), "Could not return to the academy.");
            }

            void VerifyFieldMovement()
            {
                Canvas.ForceUpdateCanvases();
                var camera = (RectTransform)FieldScene();
                var viewport = (RectTransform)camera.parent;
                var player = (RectTransform)camera.Find("Player");
                var map = (RectTransform)camera.Find("Scenery");
                var canvasRect = (RectTransform)canvas.transform;
                var desktopSize = canvasRect.sizeDelta;
                VerifySceneryAtArrival();
                canvasRect.sizeDelta = new Vector2(2340f, 1080f);
                Canvas.ForceUpdateCanvases();
                world.Update(.8f);
                VerifySceneryAtArrival();
                canvasRect.sizeDelta = desktopSize;
                Canvas.ForceUpdateCanvases();
                world.Update(.8f);

                var hud = (RectTransform)panel.Find("FieldHud");
                var hudPosition = hud.position;
                var art = player.Find("Sprite").GetComponent<Image>();
                var idle = art.sprite;
                Vector2 pan = camera.anchorMin;
                world.Update(.09f, Vector2.right);
                var firstStep = art.sprite;
                world.Update(.12f, Vector2.right);
                Require(firstStep != idle && art.sprite != firstStep && art.rectTransform.localScale.x > 0f &&
                    player.anchoredPosition == Vector2.zero && camera.anchorMin.x < pan.x &&
                    camera.Find("MainQuestNpc").parent == map.parent &&
                    panel.Find("FieldHud").parent != camera,
                    "The horizontal walk did not change frames or move the shared map camera.");
                world.Update(.12f, Vector2.left);
                Require(art.rectTransform.localScale.x < 0f, "Walking left did not face left.");
                world.Update(0f);
                Require(art.sprite == idle, "Stopping did not restore the idle pose.");
                world.Update(.15f, Vector2.up);
                var back = art.sprite;
                world.Update(.15f, Vector2.down);
                Require(back.texture == art.sprite.texture && back.texture == firstStep.texture &&
                    back.rect.y < art.sprite.rect.y && art.sprite.rect.y < firstStep.rect.y,
                    "Up and down did not use the separate back and front poses.");

                var point = new Vector2(.73f, .27f);
                var local = map.rect.min + Vector2.Scale(map.rect.size, point);
                var ev = new PointerEventData(events.GetComponent<EventSystem>())
                {
                    position = RectTransformUtility.WorldToScreenPoint(null, map.TransformPoint(local)),
                };
                map.GetComponent<TagForceFloorTap>().OnPointerClick(ev);
                world.Update(4f);
                Require(Vector2.Distance(player.anchorMin, point) < .001f, "A click used screen space instead of map space.");

                for (int frame = 0; frame < 27; frame++) world.Update(.16f, Vector2.left);
                world.Update(.6f);
                float left = CameraCenter();
                VerifyCameraBounds();
                for (int frame = 0; frame < 27; frame++) world.Update(.16f, Vector2.right);
                world.Update(.6f);
                float right = CameraCenter();
                VerifyCameraBounds();
                float travel = map.rect.width * camera.localScale.x - viewport.rect.width;
                Require(travel > viewport.rect.width * .18f && left - right > travel * .96f && hud.position == hudPosition,
                    "Walking across the field did not reach both sides of the scenery or moved the fixed HUD.");

                canvasRect.sizeDelta = new Vector2(2340f, 1080f);
                Canvas.ForceUpdateCanvases();
                world.Update(0f);
                VerifyCameraBounds();
                canvasRect.sizeDelta = desktopSize;
                Canvas.ForceUpdateCanvases();
                world.Update(0f);
                Debug.Log("[VERIFY 5DS SCROLL] Wide horizontal travel, aspect ratio, edge coverage, fixed HUD and panned clicks passed.");

                float CameraCenter() => viewport.InverseTransformPoint(camera.TransformPoint(camera.rect.center)).x;

                void VerifySceneryAtArrival()
                {
                    VerifyCameraBounds();
                    var landmark = new Vector2(map.rect.center.x, Mathf.Lerp(map.rect.yMin, map.rect.yMax, .56f));
                    var projected = viewport.InverseTransformPoint(map.TransformPoint(landmark));
                    Require(viewport.rect.Contains((Vector2)projected), "The opening camera cropped the garage landmarks.");
                }

                void VerifyCameraBounds()
                {
                    var low = viewport.InverseTransformPoint(map.TransformPoint(map.rect.min));
                    var high = viewport.InverseTransformPoint(map.TransformPoint(map.rect.max));
                    float artAspect = map.GetComponent<Image>().sprite.rect.width / map.GetComponent<Image>().sprite.rect.height;
                    Require(low.x <= viewport.rect.xMin + .1f && low.y <= viewport.rect.yMin + .1f &&
                        high.x >= viewport.rect.xMax - .1f && high.y >= viewport.rect.yMax - .1f &&
                        Mathf.Abs((high.x - low.x) / (high.y - low.y) - artAspect) < .005f,
                        "Camera edges uncovered the map or stretched its image after a pan or resize.");
                }
            }

            void VerifyDirectionalArt()
            {
                var buildArt = typeof(TagForceWorldUI).GetMethod("BuildRpgActorArt", flags);
                foreach (string id in new[] { "protagonist", "1", "10", "13", "16", "32", "34", "35", "138", "210", "3200", "3202" })
                {
                    var actor = DuelUiKit.FillRect($"Walk_{id}", canvas.transform);
                    var art = DuelUiKit.FillRect("Art", actor);
                    try
                    {
                        buildArt.Invoke(world, new object[] { art, $"TagForce/5DS/Characters_SD/sd_{id}.png", id, null });
                        var animator = art.GetComponent<TagForceRpgWalkAnimator>();
                        var image = art.GetComponent<Image>();
                        var sideIdle = image.sprite;
                        animator.Pose(.02f, true, Vector2.right);
                        var right = image.sprite;
                        animator.Pose(.08f, true, Vector2.right);
                        var nextStep = image.sprite;
                        animator.Pose(0f, false, Vector2.left);
                        var leftIdle = image.sprite;
                        bool flips = art.localScale.x < 0f;
                        animator.Pose(.02f, true, Vector2.up);
                        var rear = image.sprite;
                        animator.Pose(.08f, true, Vector2.up);
                        var rearStep = image.sprite;
                        animator.Pose(.02f, true, Vector2.down);
                        var front = image.sprite;
                        animator.Pose(0f, false, Vector2.down);
                        Require(right != nextStep && right != leftIdle && rear != rearStep && flips &&
                            leftIdle == sideIdle && image.sprite == sideIdle &&
                            right.texture == front.texture && right.rect.size == new Vector2(256f, 256f) &&
                            right.rect.y > front.rect.y && front.rect.y > rear.rect.y &&
                            art.GetComponent<TagForceRpgStride>() == null,
                            $"Character {id} did not use its front, back and side walking sheet.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(actor.gameObject); }
                }
                var fallback = DuelUiKit.FillRect("WalkFallback", canvas.transform);
                try
                {
                    var art = DuelUiKit.FillRect("Art", fallback);
                    buildArt.Invoke(world, new object[] { art, "TagForce/5DS/Characters_SD/sd_33.png", "야나기 텐젠", null });
                    VerifyStride(art.GetComponent<TagForceRpgStride>());
                }
                finally { UnityEngine.Object.DestroyImmediate(fallback.gameObject); }
                Debug.Log("[VERIFY 5DS WALK ART] Compact atlas directions and single-sprite walk fallback passed.");
            }

            void VerifyResidentProfiles()
            {
                var ids = script.episodes.Select(episode => episode.charId).Concat(RpgSideQuest.All.Select(quest => quest.Giver))
                    .Append(13).Distinct();
                foreach (int id in ids)
                {
                    string cycle = Path.Combine(streaming, $"TagForce/5DS/WalkCycles/sd_{id}");
                    Require(File.Exists(cycle + "_field.png") ||
                        File.Exists(cycle + "_directions.png") || File.Exists(cycle + "_side_idle.png"),
                        $"RPG field character {id} lacked a side-facing sprite.");
                }
                Require(world.EnterRpgRegion(RpgGarage), "Could not reset the garage.");
                var scene = FieldScene();
                var merchant = (RectTransform)scene.Find("RpgCardMerchant");
                var art = (RectTransform)merchant.Find("SD");
                var idle = art.GetComponent<Image>().sprite;
                Require(idle != null && art.localScale.x < 0f, "The merchant did not start facing inward.");
                var floor = scene.Find("Scenery").GetComponent<TagForceFloorTap>();
                floor.Click(merchant.anchorMin + new Vector2(.072f, -.042f));
                world.Update(4f);
                world.Update(.02f);
                Require(world.CanWalk && art.localScale.x > 0f && art.GetComponent<Image>().sprite == idle,
                    "The merchant did not turn toward the player while keeping the side profile.");
                Require(world.EnterRpgRegion(RpgAcademia), "Could not return to the academy after turning.");
                Debug.Log("[VERIFY 5DS PROFILES] Every field resident has side art, and facing responds to approach.");
            }

            void VerifyResidentActivity()
            {
                var field = FieldScene();
                var resident = (RectTransform)field.Find($"Resident_{script.episodes[1].charId}");
                var marker = resident.Find("ResidentMarker").GetComponent<CanvasGroup>();
                var art = resident.Find("SD").GetComponent<Image>();
                var idle = art.sprite;
                var home = resident.anchorMin;
                var player = (RectTransform)field.Find("Player");
                var playerHome = player.anchorMin;
                var main = (RectTransform)field.Find("MainQuestNpc");
                var storyHome = main.anchorMin;
                var questGiver = (RectTransform)field.Find("QuestNpc_24");
                var requestHome = questGiver.anchorMin;
                Require(marker.alpha == 0f && main.Find("MainQuestMarker/Icon").GetComponent<Image>().sprite != null &&
                    field.Find("QuestNpc_24/ResidentMarker/Icon").GetComponent<Image>().sprite != null &&
                    field.Find("QuestNpc_24/QuestBadge") == null,
                    "The field did not distinguish story, request, shop and nearby conversation markers.");
                bool stepped = false;
                for (int i = 0; i < 9; i++)
                {
                    world.Update(.1f);
                    stepped |= art.sprite != idle;
                }
                Require(stepped && resident.anchorMin.x < home.x - .03f && resident.anchorMin.y < home.y - .02f &&
                    player.anchorMin == playerHome && main.anchorMin == storyHome && marker.alpha == 0f &&
                    questGiver.anchorMin.x > requestHome.x + .003f &&
                    questGiver.anchorMin.x < requestHome.x + .02f,
                    "The resident did not use its walking poses and change position while the player stood still.");

                var stopped = resident.anchorMin;
                Press(panel, "FieldHud/MenuToggle");
                world.Update(.9f);
                Require(resident.anchorMin == stopped, "Background walkers did not pause for the field menu.");
                world.Back();

                Press(resident, "SD");
                world.Update(.15f);
                Require(resident.anchorMin == stopped && world.CanWalk,
                    "A resident kept walking after the player tapped it from far away.");
                var floor = field.Find("Scenery").GetComponent<TagForceFloorTap>();
                floor.Click(stopped + new Vector2(.055f, -.048f));
                for (int i = 0; i < 12; i++) world.Update(.1f);
                Require(world.CanWalk && marker.alpha > .95f && art.rectTransform.localScale.x > 0f &&
                    panel.Find("FieldHud/ContextAction/Label").GetComponent<Text>().text.Contains(script.episodes[1].opponentName),
                    "The walker did not pause, face the player or display the nearby conversation bubble.");
                var focused = resident.anchorMin;
                world.Update(.4f);
                Require(resident.anchorMin == focused, "A resident moved away during nearby interaction.");
                Press(panel, "FieldHud/ContextAction");
                world.Update(.5f);
                Require(!world.CanWalk && panel.Find("ResidentChoices/TalkDaily") != null,
                    "The approach did not open the moving resident's conversation.");
                world.Back();
                Require(world.EnterRpgRegion(RpgGarage) && world.EnterRpgRegion(RpgAcademia), "Could not reset the field after resident activity.");
                Debug.Log("[VERIFY 5DS RESIDENTS] Idle-player routes, separate footsteps, menu pause and contextual markers passed.");
            }

            void VerifySceneGuests()
            {
                var field = FieldScene();
                var player = (RectTransform)field.Find("Player");
                var opponent = (RectTransform)field.Find("MainQuestNpc");
                var action = panel.Find("FieldHud/ContextAction").GetComponent<Button>();
                for (int i = 0; i < 12 && !action.GetComponentInChildren<Text>().text.Contains("이야기 진행"); i++)
                    world.Update(.015f, Vector2.up);
                Require(action.interactable && action.GetComponentInChildren<Text>().text.Contains("이야기 진행"),
                    "The shared action did not present the nearby main quest.");
                world.HandleFieldInput(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Submit));
                world.Update(.5f);
                Vector2 position = player.anchorMin;
                var cast = field.Find("DialogueCast");
                Require(cast != null && world.RpgStageRunning, "Walking near the main quest did not start its opening shot.");
                var guest = (RectTransform)cast.Find("Guest_1");
                Require(storyRoot.gameObject.activeSelf && world.Root.activeSelf && !world.CanWalk &&
                    world.Root.transform.GetSiblingIndex() < story.Root.transform.GetSiblingIndex() &&
                    field.Find("Player") == player && field.Find("MainQuestNpc") == opponent &&
                    storyRoot.Find("Scene/Portraits/Protagonist").GetComponent<Image>().enabled &&
                    storyRoot.Find("Scene/Portraits/Opponent").GetComponent<Image>().enabled &&
                    player.Find("Sprite").GetComponent<Image>().enabled && opponent.Find("SD").GetComponent<Image>().enabled &&
                    !guest.gameObject.activeSelf && guest.Find("Sprite").GetComponent<Image>().sprite != null &&
                    !field.Find("QuestNpc_24").gameObject.activeSelf &&
                    !world.Root.GetComponent<CanvasGroup>().blocksRaycasts,
                    "The conversation did not preserve the real field or hold Aki until her first line.");
                world.Update(.5f, Vector2.right);
                Require(player.anchorMin == position && !world.EnterRpgRegion(RpgHideout) &&
                    !world.HandleFieldInput(new DeckPadFrame(Vector2Int.right, DeckPadButtons.Submit | DeckPadButtons.Search)),
                    "The player could move, interact or travel during the conversation.");
                Press(storyRoot, "Scene/DialogueBox/Next/Button");
                Press(storyRoot, "Scene/DialogueBox/Next/Button");
                Require(CurrentLine() == 0 && world.RpgStageRunning,
                    "Repeated advance input bypassed the opening camera move.");

                var lines = loaded.episodes[0].preDialogues;
                int arrival = Array.FindIndex(lines, line => line.stage?.Any(a => a.actor == 1 && a.action == "enter") == true);
                int firstAkiLine = Array.FindIndex(lines, line => line.speaker == "이자요이 아키");
                Require(arrival > 0 && arrival < firstAkiLine, "Aki should enter during narration before speaking.");
                AdvanceToLine(arrival);
                Vector2 entry = guest.anchorMin;
                Vector2 otherStart = opponent.anchorMin;
                Vector2 cameraStart = ((RectTransform)field).anchorMin;
                story.Update(.30f);
                Require(guest.gameObject.activeSelf && guest.anchorMin != entry && lines[arrival].speaker == "" &&
                    guest.Find("Sprite").GetComponent<Image>().enabled && guest.GetComponent<CanvasGroup>().alpha > 0f &&
                    opponent.gameObject.activeSelf && opponent.anchorMin == otherStart && player.gameObject.activeSelf &&
                    ((RectTransform)field).anchorMin != cameraStart,
                    "The first shot did not follow Aki as she entered before the other actor moved.");
                story.Update(.57f);
                Require(world.RpgStageRunning && opponent.anchorMin != otherStart,
                    "The second actor did not move automatically after the first beat.");
                Press(storyRoot, "Scene/DialogueTools/Previous/Button");
                Require(!guest.gameObject.activeSelf && opponent.anchorMin == otherStart && !world.RpgStageRunning,
                    "Rewinding did not restore the actors before the two-beat scene.");
                Press(storyRoot, "Scene/DialogueBox/Next/Button");
                story.Update(1.35f);
                Require(guest.gameObject.activeSelf && guest.GetComponent<CanvasGroup>().alpha == 1f &&
                    guest.anchorMin != entry && !world.RpgStageRunning && guest.Find("Sprite").localScale.x > 0f,
                    "The guest did not reach her mark and face the rival after replaying the entrance.");
                Canvas.ForceUpdateCanvases();
                foreach (var actor in new[] { player, opponent, guest })
                {
                    var viewport = (RectTransform)panel.Find("FieldViewport");
                    Vector3 positionInViewport = viewport.InverseTransformPoint(actor.position);
                    var normalized = Rect.PointToNormalized(viewport.rect, positionInViewport);
                    // Calculate without clamping so an actor off either side of the camera fails.
                    float horizontal = (positionInViewport.x - viewport.rect.xMin) / viewport.rect.width;
                    Require(horizontal > .015f && horizontal < .985f && normalized.y > .34f && normalized.y < .96f,
                        $"{actor.name} left the safe stage area while the camera followed the guest.");
                }
                int firstKaiLine = Array.FindIndex(lines, line => line.speaker == "카이" && line.emotion == "determined");
                AdvanceToLine(firstKaiLine);
                var kaiPortrait = storyRoot.Find("Scene/Portraits/Protagonist").GetComponent<Image>();
                var determined = kaiPortrait.sprite;
                Require(kaiPortrait.enabled && determined != null && determined.rect.height == determined.texture.height,
                    "Kai's determined line did not load the dedicated upper-body portrait.");
                AdvanceToLine(firstAkiLine);
                var portrait = storyRoot.Find("Scene/Portraits/Opponent").GetComponent<Image>();
                Require(portrait.enabled && portrait.sprite != null &&
                    guest.Find("Sprite").GetComponent<Image>().enabled,
                    "Aki's dialogue did not keep both the field actor and upper-body portrait visible.");
                var worldArt = (TagForceWorldImages)typeof(TagForceWorldUI).GetField("_images", flags).GetValue(world);
                var schoolWalk = worldArt.GetSlices("TagForce/5DS/WalkCycles/sd_1_school_field.png", 3, 3, fixedCells: true);
                Require(guest.Find("Sprite").GetComponent<Image>().sprite.texture == schoolWalk[0].texture,
                    "Aki's cutscene actor did not wear her academy outfit.");
                var dialogue = (StoryModeUI.StoryDialogueView)typeof(StoryModeUI).GetField("_dialogue", flags).GetValue(story);
                var sprites = (Dictionary<string, Sprite>)typeof(StoryModeUI.StoryDialogueView).GetField("_rpgSprites", flags).GetValue(dialogue);
                string schoolPose = Path.Combine(streaming, "TagForce/5DS", $"Characters_Standing/full_1_school_{lines[firstAkiLine].emotion}.png");
                Require(sprites.TryGetValue(schoolPose, out var schoolPortrait) && portrait.sprite == schoolPortrait,
                    "Aki's portrait did not use the same wardrobe as her stage actor.");
                int shocked = Array.FindIndex(lines, firstAkiLine + 1,
                    line => line.speaker == "이자요이 아키" && line.emotion == "shock");
                Require(shocked > firstAkiLine, "The narration fixture did not have an Aki reaction.");
                AdvanceToLine(shocked);
                Require(portrait.enabled && player.Find("Sprite").localScale.x < 0f,
                    "Aki's reaction or the scripted speaker facing was lost.");
                var mote = (RectTransform)field.Find("FieldAtmosphere/Mote_0");
                Vector2 beforeMote = mote.anchorMin;
                story.Update(1.4f);
                Require(mote.anchorMin != beforeMote, "The background stopped during the conversation.");
                int approach = Array.FindIndex(lines, line => line.stage?.Any(a => a.actor == 0 && a.action == "move") == true);
                AdvanceToLine(approach);
                field.Find("Scenery").GetComponent<TagForceFloorTap>().Click(new Vector2(.94f, .15f));
                world.Update(.25f, Vector2.right);
                story.Update(.65f);
                Require(Vector2.Distance(player.anchorMin, new Vector2(.41f, .37f)) < .001f && !world.CanWalk,
                    "Clicking or holding a direction overrode Kai's scripted movement.");
                int smileLine = Array.FindIndex(lines, approach, line => line.speaker == "카이" && line.emotion == "smile");
                AdvanceToLine(smileLine);
                Require(kaiPortrait.enabled && kaiPortrait.sprite != null && kaiPortrait.sprite != determined &&
                    kaiPortrait.sprite.rect.height == kaiPortrait.sprite.texture.height &&
                    storyRoot.Find("Scene/Portraits/Opponent").GetComponent<Image>().enabled,
                    "The smile did not switch to Kai's own expression while keeping the listener on screen.");
                app.OpenStoryDeckEditor();
                Require(!world.Root.activeSelf, "The field remained over the deck editor.");
                app.CloseDeckEditor();
                Require(guest.gameObject.activeInHierarchy && !world.CanWalk, "Deck editing lost the active field scene.");
                story.Back();
                Require(world.CanWalk && FieldScene().Find("DialogueCast") == null &&
                    FieldScene().Find("Player/Sprite").GetComponent<Image>().enabled &&
                    ((RectTransform)FieldScene().Find("Player")).anchorMin == position &&
                    world.Root.GetComponent<CanvasGroup>().blocksRaycasts && panel.Find("FieldHud").gameObject.activeSelf &&
                    FieldScene().Find("QuestNpc_24").gameObject.activeInHierarchy,
                    "Leaving the conversation did not remove its cast and restore movement at the original location.");
                world.Update(.25f, Vector2.down);
                for (int frame = 0; frame < 3; frame++) world.Update(.15f, Vector2.up);
                Require(world.CanWalk && !storyRoot.gameObject.activeSelf,
                    "The automatic scene immediately reopened after leaving it.");
            }

            void VerifyWardrobe()
            {
                var expected = new[]
                {
                    (1, 1, "school"), (1, 3, "school"), (1, 8, "everyday"), (1, 12, "duel"),
                    (1, 26, "duel"), (1, 32, "workshop"), (1, 34, "workshop"), (1, 39, "riding"),
                    (1, 48, "duel"), (1, 50, "everyday"), (32, 33, "city"), (32, 49, (string)null),
                    (220, 30, "press"), (230, 14, (string)null), (221, 22, (string)null),
                };
                var images = new TagForceWorldImages();
                try
                {
                    foreach (var (id, episode, outfit) in expected)
                    {
                        Require(RpgWardrobe.EpisodeNumber(script.episodes[episode - 1]) == episode &&
                            RpgWardrobe.ForStory(id, episode) == outfit &&
                            RpgWardrobe.ForField(id, RegionForEpisode(episode), episode) == outfit,
                            $"The field and dialogue chose different outfits for {id}, episode {episode}.");
                        if (outfit == null) continue;
                        var stem = "TagForce/5DS/" + RpgWardrobe.PortraitStem(id, outfit);
                        foreach (var mood in new[] { "smile", "blush", "sad", "determined", "shock", "hurt" })
                            Require(images.Get($"{stem}_{mood}.png") != null, $"Missing {outfit}/{mood} for {id}.");
                        var walk = images.GetSlices("TagForce/5DS/" + RpgWardrobe.WalkAtlas(id, outfit), 3, 3, fixedCells: true);
                        Require(walk is { Length: 9 } && walk.All(pose => pose.rect.size == new Vector2(256f, 256f)),
                            $"The {outfit} wear set for {id} lacked directional field art.");
                    }
                    Require(RpgWardrobe.ForField(1, RpgGarage, 39) == "everyday" &&
                        RpgWardrobe.ForField(1, RpgAcademia, 39) == "school" &&
                        RpgWardrobe.ForField(1, RpgWrgp, 3) == "riding",
                        "Free exploration did not dress residents for their actual map.");
                    var darkCarly = images.GetSlices("TagForce/5DS/WalkCycles/sd_221_field.png", 3, 3, fixedCells: true);
                    Require(darkCarly is { Length: 9 }, "Dark Signer Carly still lacked directional field art.");
                }
                finally { images.Clear(); }
                Debug.Log("[VERIFY 5DS WARDROBE] Scene outfits, matching portrait and SD assignments, and free exploration passed.");
            }

            void VerifyDialoguePortraitAssets()
            {
                const string standing = "TagForce/5DS/Characters_Standing";
                var cast = new HashSet<int>();
                foreach (var episode in loaded.episodes)
                foreach (var line in episode.preDialogues.Concat(episode.postWinDialogues).Concat(episode.postLoseDialogues))
                {
                    string name = line.speaker;
                    if (string.IsNullOrWhiteSpace(name) || name is "카이" or "설명" or "내레이션" or "나레이션") continue;
                    var actor = DeckRepository.Characters.FirstOrDefault(character => character.name == name) ??
                        DeckRepository.Characters.FirstOrDefault(character => character.aliases?.Contains(name) == true);
                    Require(actor != null && File.Exists(Path.Combine(streaming, standing, $"full_{actor.id}.png")),
                        $"Missing dialogue portrait for {name} in {episode.id}.");
                    cast.Add(actor.id);
                }

                string[] fullCast = { "protagonist", "1", "35" };
                string[] featuredCast = { "16", "10", "3204", "32", "21", "24", "220", "230", "19", "3200" };
                string[] featuredMoods = { "determined", "smile", "shock", "sad" };
                foreach (string id in fullCast.Concat(featuredCast))
                foreach (string mood in fullCast.Contains(id) ? featuredMoods.Concat(new[] { "blush", "hurt" }) : featuredMoods)
                    Require(File.Exists(Path.Combine(streaming, standing, $"full_{id}_{mood}.png")),
                        $"Missing authored dialogue expression {id}/{mood}.");
                var expressionSuffixes = featuredMoods.Concat(new[] { "blush", "hurt" }).ToArray();
                var files = Directory.GetFiles(Path.Combine(streaming, standing), "full_*.png");
                var expressions = files.Where(file => expressionSuffixes.Any(mood =>
                    file.EndsWith($"_{mood}.png", StringComparison.Ordinal))).ToArray();
                Require(expressions.Length >= 94, "A shipped RPG expression was removed.");
                var portraitFrame = new Rect(0, 0, 448, 576);
                foreach (string expression in expressions)
                    VerifyPortraitCrop(expression, portraitFrame);
                foreach (string id in fullCast.Concat(featuredCast))
                    VerifyPortraitCrop(Path.Combine(streaming, standing, $"full_{id}.png"), portraitFrame);

                void VerifyPortraitCrop(string path, Rect frame)
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    Sprite cropped = null;
                    Texture2D donor = null;
                    try
                    {
                        Require(texture.LoadImage(File.ReadAllBytes(path)), $"Invalid standing image {path}.");
                        bool alreadyUpperBody = texture.height / (float)texture.width < 1.38f;
                        cropped = RpgDialoguePortrait.Crop(texture, new Rect(0, 0, texture.width, texture.height),
                            Path.GetFileNameWithoutExtension(path));
                        donor = cropped.texture;
                        Require(cropped.rect == frame && (donor == texture) == alreadyUpperBody,
                            $"Standing image {path} did not retain the 448 × 576 dialogue frame.");
                    }
                    finally
                    {
                        if (cropped != null) UnityEngine.Object.DestroyImmediate(cropped);
                        if (donor != null && donor != texture) UnityEngine.Object.DestroyImmediate(donor);
                        UnityEngine.Object.DestroyImmediate(texture);
                    }
                }
                var images = (TagForceWorldImages)typeof(TagForceWorldUI).GetField("_images", flags).GetValue(world);
                foreach (int id in new[] { 21, 24, 3204, 220, 230, 28, 19, 4, 3203 })
                {
                    var walk = images.GetSlices($"TagForce/5DS/WalkCycles/sd_{id}_field.png", 3, 3, fixedCells: true);
                    Require(walk is { Length: 9 } && walk.All(frame => frame != null && frame.rect.size == new Vector2(256f, 256f)) &&
                        walk[0].rect.y > walk[3].rect.y && walk[3].rect.y > walk[6].rect.y,
                        $"Missing directional frames for actor {id}.");
                }
                Debug.Log($"[VERIFY 5DS PORTRAITS] Base portrait for {cast.Count} IDs, {expressions.Length} uncropped expressions, " +
                    "13 aligned neutral frames and nine directional atlases passed.");
            }

            void WalkToQuestScene(RectTransform player)
            {
                var approach = new Vector2(.48f, .35f);
                for (int frame = 0; frame < 45 && world.CanWalk; frame++)
                    world.Update(.04f, (approach - player.anchorMin).normalized);
            }

            void VerifyAutomaticScenes()
            {
                string auditSave = Path.Combine(save, "automatic-scene-audit");
                DeckRepository.Load(streaming, auditSave, seedDefaultDecks: false);
                CollectionProgress.Load(auditSave);
                Require(CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out _, out var error), error);
                world.PrepareRpgField();
                var automatic = loaded.episodes.Select((episode, index) => (episode, index))
                    .Where(item => item.episode.sceneOnApproach).ToArray();
                foreach (var (episode, index) in automatic)
                {
                    for (int i = world.RpgClears; i < index; i++)
                    {
                        var prior = script.episodes[i];
                        Require(CollectionProgress.TryAwardStoryDuel(prior.id, true, null, decks[prior.deckId],
                            out _, out _, out _), $"Could not stage {prior.id}.");
                    }
                    int region = TagForceWorldUI.RegionForEpisode(index + 1);
                    Require(world.EnterRpgRegion(region == RpgAcademia ? RpgGarage : RpgAcademia) && world.EnterRpgRegion(region),
                        $"Region for {episode.id} did not open.");
                    WalkToQuestScene((RectTransform)FieldScene().Find("Player"));
                    Require(storyRoot.gameObject.activeSelf && world.RpgStageRunning && !world.CanWalk && CurrentLine() == 0,
                        $"Approaching the NPC did not start {episode.id}.");
                    story.Update(3f);
                    Require(!world.RpgStageRunning && world.RpgClears == index && CurrentLine() == 0,
                        $"The opening stage of {episode.id} did not finish before its first dialogue.");
                    story.Back();
                    Require(world.CanWalk && world.RpgClears == index, $"Leaving {episode.id} lost the field or awarded a clear.");
                }
                Debug.Log($"[VERIFY 5DS AUTO SCENES] All {automatic.Length} scripted approaches, opening sequences and returns passed.");
            }

            void VerifyReturningEpisodes()
            {
                string audit = Path.Combine(save, "rpg-returning-episodes");
                DeckRepository.Load(streaming, audit, seedDefaultDecks: false);
                CollectionProgress.Load(audit);
                Require(CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out _, out var error), error);
                world.PrepareRpgField();
                foreach (int number in new[] { 3, 9, 14, 19, 28, 30, 31, 32, 33, 34, 35, 45, 50 })
                {
                    int index = number - 1;
                    for (int i = world.RpgClears; i < index; i++)
                    {
                        var prior = script.episodes[i];
                        Require(CollectionProgress.TryAwardStoryDuel(prior.id, true, null, decks[prior.deckId],
                            out _, out _, out _), $"Could not stage {prior.id}.");
                    }

                    int region = expectedRegions[index];
                    string target = regions[region].Name;
                    Require(world.EnterRpgRegion(region == RpgAcademia ? RpgGarage : RpgAcademia) && !world.CanStartRpgEpisode(index) &&
                        FieldScene().Find("MainQuestNpc") == null &&
                        panel.Find("FieldHud/QuestTrack/Chapter/Label").GetComponent<Text>().text == $"[메인] {target}" &&
                        panel.Find("FieldHud/QuestTrack/Objective/Label").GetComponent<Text>().text == $"이동 · {target}",
                        $"The field directed episode {number} to the wrong destination.");
                    PressRpgNavigation("Map");
                    foreach (var (area, i) in regions.Select((area, i) => (area, i)))
                        Require((panel.Find($"{MapNodes}/{area.Id}/GoalPulse") != null) == (i == region),
                            $"The map highlighted the wrong location for episode {number}.");
                    Require(world.EnterRpgRegion(region) && world.CanStartRpgEpisode(index) &&
                        FieldScene().Find("MainQuestNpc") != null &&
                        panel.Find("FieldHud/QuestTrack/Objective/Label").GetComponent<Text>().text.Contains(script.episodes[index].opponentName),
                        $"Episode {number} did not place the main quest on its assigned field.");
                }
                Debug.Log("[VERIFY 5DS REGIONS] All 50 locations, unlock gates, and 13 in-game map/HUD routes passed.");
            }

            void VerifySideQuests()
            {
                const string tools = "garage-tools";
                const string parts = "hideout-parts";
                int before = CollectionProgress.Balance;
                int beforeXp = CollectionProgress.RpgExperience;
                int beforeAffinity = CollectionProgress.RpgAffinity;
                Require(world.EnterRpgRegion(RpgGarage) && FieldScene().Find("SideQuestItem") == null,
                    "A quest item appeared before accepting its request.");
                TalkTo(10);
                Press(panel, "ResidentChoices/AcceptQuest");
                Require(CollectionProgress.GetRpgQuestState(tools) == RpgQuestState.Active && !world.CanWalk &&
                    FieldScene().Find("Player") != null, "Accepting a request left the field or lost its actors.");
                world.Back();
                var item = (RectTransform)FieldScene().Find("SideQuestItem");
                FieldScene().Find("Scenery").GetComponent<TagForceFloorTap>().Click(item.anchorMin);
                world.Update(3f);
                Require(panel.Find("FieldHud/SideQuestTrack/Reward/Label").GetComponent<Text>().text.Contains("EXP") &&
                    panel.Find("FieldHud/ContextAction/Label").GetComponent<Text>().text.Contains("줍기"),
                    "The side quest item and its expected rewards were missing from the field HUD.");
                Press(panel, "FieldHud/ContextAction");
                world.Update(4f);
                CollectionProgress.Load(save);
                Require(CollectionProgress.GetRpgQuestState(tools) == RpgQuestState.Ready &&
                    FieldScene().Find("SideQuestItem") == null && CollectionProgress.Balance == before &&
                    FieldScene().Find("QuestNpc_10/ResidentMarker/Icon").GetComponent<Image>().sprite != null,
                    "Investigating did not persist the objective or granted an early reward.");
                TalkTo(10);
                Press(panel, "ResidentChoices/ClaimQuest");
                Require(CollectionProgress.Balance == before + RpgSideQuest.Find(tools).Reward &&
                    CollectionProgress.RpgExperience == beforeXp + RpgSideQuest.Find(tools).Experience &&
                    panel.Find("QuestRewardBanner").GetComponentInChildren<Text>().text.Contains("EXP") &&
                    !CollectionProgress.TryAdvanceRpgQuest(tools, RpgQuestState.Ready, out _, out _),
                    "The reward was missing or could be claimed twice.");
                world.Back();

                Require(world.EnterRpgRegion(RpgHideout), "The hideout was unavailable for the delivery request.");
                TalkTo(33);
                Press(panel, "ResidentChoices/AcceptQuest");
                world.Back();
                PressRpgNavigation("Quests");
                var journal = panel.Find("QuestJournal/Items").GetComponentsInChildren<Text>();
                Require(journal.Any(text => text.text.Contains("크로우 호건에게 부품 상자 전하기")),
                    "The quest journal was missing the active delivery destination.");
                world.Back();
                Require(world.EnterRpgRegion(RpgGarage), "The garage was unreachable during delivery.");
                Require(FieldScene().Find("QuestNpc_10/ResidentMarker/Icon").GetComponent<Image>().sprite != null,
                    "The delivery receiver did not show the handoff marker.");
                TalkTo(10);
                Press(panel, "ResidentChoices/DeliverQuest");
                world.Back();
                CollectionProgress.Load(save);
                Require(CollectionProgress.GetRpgQuestState(tools) == RpgQuestState.Complete &&
                    CollectionProgress.GetRpgQuestState(parts) == RpgQuestState.Ready && world.RpgClears == 2,
                    "The delivery did not persist independently of the main story and the earlier reward.");
                Require(world.EnterRpgRegion(RpgHideout), "The sender's region was unavailable after delivery.");
                TalkTo(33);
                Press(panel, "ResidentChoices/ClaimQuest");
                Require(CollectionProgress.Balance == before + RpgSideQuest.Find(tools).Reward + RpgSideQuest.Find(parts).Reward &&
                    CollectionProgress.RpgAffinity == beforeAffinity + RpgSideQuest.Find(parts).Affinity,
                    "The delivery reward was incorrect.");
                world.Back();
                Debug.Log("[VERIFY 5DS QUESTS] Field conversation, item search, inter-region delivery, journal, reload and one-time rewards passed.");

                void TalkTo(int id)
                {
                    var actor = (RectTransform)FieldScene().Find($"QuestNpc_{id}");
                    FieldScene().Find("Scenery").GetComponent<TagForceFloorTap>().Click(actor.anchorMin - Vector2.up * .06f);
                    world.Update(3f);
                    Press(panel, "FieldHud/ContextAction");
                    world.Update(4f);
                    Require(!world.CanWalk && panel.Find("ResidentChoices") != null && FieldScene().Find($"QuestNpc_{id}") != null,
                        $"Quest giver {id} did not open a conversation on the field.");
                }
            }

            void VerifyRpgExploration()
            {
                const string coupler = RpgSideQuest.GarageCouplerId;
                foreach (bool askCrow in new[] { false, true })
                {
                    string audit = Path.Combine(save, askCrow ? "explore-npc" : "explore-log");
                    DeckRepository.Load(streaming, audit, seedDefaultDecks: false);
                    CollectionProgress.Load(audit);
                    Require(CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out _, out var error), error);
                    world.PrepareRpgField();
                    Require(world.EnterRpgRegion(RpgGarage), "The garage was unavailable in the exploration test.");
                    var viewport = (RectTransform)panel.Find("FieldViewport");
                    var track = (RectTransform)panel.Find("FieldHud/QuestTrack");
                    Require(viewport.anchorMax == Vector2.one && track.anchorMin.y > .80f && track.anchorMax.x < .32f,
                        "The quest tracker no longer left the middle of the full-screen field clear.");
                    ExamineLandmark();
                    Require(panel.Find("ResidentChoices/AskCrow") == null && !world.CanWalk,
                        "The workbench exposed the late bridge quest early, or allowed motion while inspecting.");
                    world.Back();
                    foreach (var episode in script.episodes.Take(2))
                        Require(CollectionProgress.TryAwardStoryDuel(episode.id, true, null, decks[episode.deckId],
                            out _, out _, out _), $"Could not stage {episode.id}.");
                    Require(world.EnterRpgRegion(RpgGarage), "Could not refresh the hideout gate.");
                    WalkOut(RpgHideout, Vector2.right, .18f);
                    var locked = FieldScene().Find($"Exploration/Exit_{RpgBridge}").GetComponent<Button>();
                    Require(!locked.IsInteractable(), "The bridge exit opened before the story gate.");
                    PointAt(new Vector2(.805f, .20f));
                    for (int i = 0; i < 3; i++) world.Update(.5f, Vector2.right);
                    Require(FieldScene().Find($"Exploration/Exit_{RpgBridge}") != null && !world.EnterRpgRegion(RpgBridge),
                        "Walking near a locked exit bypassed the story gate.");

                    foreach (var episode in script.episodes.Skip(world.RpgClears).Take(regions[RpgBridge].RequiredEpisode - world.RpgClears))
                        Require(CollectionProgress.TryAwardStoryDuel(episode.id, true, null, decks[episode.deckId],
                            out _, out _, out _), $"Could not stage {episode.id}.");
                    Require(world.EnterRpgRegion(RpgGarage), "Could not return to the garage.");
                    if (askCrow) ExamineLandmark(withController: true);
                    else ExamineLandmark();
                    if (askCrow)
                    {
                        Press(panel, "ResidentChoices/AskCrow");
                        world.Update(4f);
                        Require(panel.Find("ConversationPortrait").GetComponent<Image>().sprite != null &&
                            !FieldScene().Find("QuestNpc_10/SD").gameObject.activeSelf,
                            "Choosing to ask Crow did not replace his visible field sprite with his upper-body portrait.");
                        Press(panel, "ResidentChoices/AskCoupler");
                    }
                    else Press(panel, "ResidentChoices/ReadRunnerLog");
                    CollectionProgress.Load(audit);
                    Require(CollectionProgress.GetRpgQuestState(coupler) == RpgQuestState.Active,
                        "The chosen clue route did not save the quest.");
                    world.Back();
                    WalkOut(RpgHideout, Vector2.right, .18f);
                    WalkOut(RpgBridge, Vector2.right, .18f);
                    ExamineLandmark(withController: askCrow);
                    Press(panel, "ResidentChoices/RecoverCoupler");
                    CollectionProgress.Load(audit);
                    Require(CollectionProgress.GetRpgQuestState(coupler) == RpgQuestState.Ready,
                        "The bridge investigation did not persist the recovered component.");
                    world.Back();
                    WalkOut(RpgHideout, Vector2.left, .82f);
                    WalkOut(RpgGarage, Vector2.left, .82f);
                    ExamineLandmark();
                    var crow = (RectTransform)FieldScene().Find("QuestNpc_10");
                    var start = crow.anchorMin;
                    var fit = panel.Find("ResidentChoices/FitCoupler").GetComponent<Button>();
                    Require(!fit.IsInteractable() && !world.CanWalk, "The workbench cutscene allowed input while Crow was moving.");
                    world.Update(.31f, Vector2.right);
                    Require(crow.anchorMin != start && !fit.IsInteractable() && crow.Find("SD").GetComponent<Image>().enabled,
                        "Crow did not visibly walk to the workbench.");
                    world.Update(.9f);
                    int balance = CollectionProgress.Balance;
                    Press(panel, "ResidentChoices/FitCoupler");
                    CollectionProgress.Load(audit);
                    Require(CollectionProgress.GetRpgQuestState(coupler) == RpgQuestState.Complete &&
                        CollectionProgress.Balance == balance + RpgSideQuest.Find(coupler).Reward &&
                        !CollectionProgress.TryAdvanceRpgQuest(coupler, RpgQuestState.Ready, out _, out _),
                        "The repair reward did not persist exactly once.");
                    Require(panel.Find("ResidentChoices/DailyLine/Label").GetComponent<Text>().text.Contains("엔진"),
                        "The landmark text did not react to the completed repair.");
                    world.Back();
                    var repairedCrow = (RectTransform)FieldScene().Find("QuestNpc_10");
                    Require(repairedCrow.anchorMin != start, "The resident did not relocate after repairing the runner.");
                    Press(repairedCrow, "SD");
                    world.Update(4f);
                    Require(panel.Find("ResidentChoices/DailyLine/Label").GetComponent<Text>().text.Contains("시동"),
                        "The resident did not remember the completed repair.");
                    world.Back();
                }
                Debug.Log("[VERIFY 5DS EXPLORATION] Landmarks, both dialogue routes, locked exits, connected travel, animated walk-in, rewards and reload passed.");

                void WalkOut(int destination, Vector2 direction, float entranceX)
                {
                    var field = FieldScene();
                    var exit = field.Find($"Exploration/Exit_{destination}");
                    Require(exit != null && exit.GetComponent<Button>().IsInteractable(), "The expected path was unavailable.");
                    var point = ((RectTransform)exit).anchorMin;
                    PointAt(new Vector2(point.x - direction.x * .12f, .20f));
                    for (int i = 0; i < 4 && FieldScene() == field; i++) world.Update(.35f, direction);
                    Require(FieldScene() != field &&
                        Mathf.Abs(((RectTransform)FieldScene().Find("Player")).anchorMin.x - entranceX) < .001f,
                        "Crossing an exit did not spawn the player at its paired entrance.");
                    var arrived = FieldScene();
                    world.Update(.01f);
                    Require(FieldScene() == arrived, "The entrance immediately sent the player back.");
                }

                void ExamineLandmark(bool withController = false)
                {
                    var field = FieldScene();
                    if (!withController) Press(field, "Exploration/Landmark");
                    else
                    {
                        PointAt(((RectTransform)field.Find("Exploration/Landmark")).anchorMin);
                        world.HandleFieldInput(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Submit));
                    }
                    world.Update(4f);
                    Require(!world.CanWalk && panel.Find("ResidentChoices/DailyLine/Label") != null,
                        "The landmark did not open its inspection panel.");
                }

                void PointAt(Vector2 point)
                {
                    FieldScene().Find("Scenery").GetComponent<TagForceFloorTap>().Click(point);
                    world.Update(4f);
                }
            }

            void VerifyRpgAdventure()
            {
                string audit = Path.Combine(save, "rpg-adventure");
                DeckRepository.Load(streaming, audit, seedDefaultDecks: false);
                CollectionProgress.Load(audit);
                Require(CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out _, out var error), error);
                app.OpenRpgField();
                for (int i = 0; i < 4; i++) Press(panel, "StoryDialogue/Scene/DialogueBox/Next/Button");
                Require(!world.CanWalk && FieldScene().Find("RpgCompanion") != null &&
                    FieldScene().Find("RpgCompanion/Sprite").GetComponent<Image>().enabled &&
                    panel.Find("StoryDialogue/Scene/Portraits/Opponent").GetComponent<Image>().enabled,
                    "The prologue did not lead into the workshop guide with Yusei on the field and in the dialogue.");
                Press(panel, "StoryDialogue/Scene/DialogueTools/Skip/Button");
                CollectionProgress.Load(audit);
                app.OpenRpgField();
                Require(world.CanWalk && CollectionProgress.RpgIntroSeen && panel.Find("FieldHud/DuelistHud") != null,
                    "The guide repeated or the RPG HUD was absent after reloading.");
                var follower = (RectTransform)FieldScene().Find("RpgCompanion");
                var old = follower.anchorMin;
                for (int i = 0; i < 4; i++) world.Update(.10f, Vector2.right);
                Require(follower.anchorMin.x > old.x && panel.Find("PartnerGreeting").gameObject.activeSelf,
                    "The partner failed to follow the player or greet the location.");
                world.Update(0f);
                var talk = new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Alternate);
                FieldScene().Find("Scenery").GetComponent<TagForceFloorTap>().Click(follower.anchorMin);
                world.Update(.5f);
                Require(panel.Find("FieldHud/ContextAction/Label").GetComponent<Text>().text == "후도 유세이 · 대화",
                    "The shared action did not offer partner dialogue.");
                Press(panel, "FieldHud/ContextAction");
                Require(!world.CanWalk && CollectionProgress.RpgAffinity == 4, "Talking to the partner did not grant affinity.");
                world.Back();
                world.HandleFieldInput(talk);
                Require(CollectionProgress.RpgAffinity == 4, "Repeatedly opening the same dialogue allowed affinity farming.");
                world.Back();

                Require(world.EnterRpgRegion(RpgGarage), "Could not verify the garage cache.");
                int balance = CollectionProgress.Balance;
                var cache = (RectTransform)FieldScene().Find("Exploration/HiddenCache");
                FieldScene().Find("Scenery").GetComponent<TagForceFloorTap>().Click(cache.anchorMin);
                world.Update(3f);
                Require(panel.Find("FieldHud/ContextAction/Label").GetComponent<Text>().text.EndsWith("줍기", StringComparison.Ordinal),
                    "The shared action did not offer the hidden treasure.");
                Press(panel, "FieldHud/ContextAction");
                world.Update(4f);
                CollectionProgress.Load(audit);
                Require(CollectionProgress.Balance == balance + 180 && CollectionProgress.RpgCacheFound(0) &&
                    FieldScene().Find("Exploration/HiddenCache") == null &&
                    !CollectionProgress.TryFindRpgCache(0, out _), "The cache did not persist a one-time payout.");
                int tickets = CollectionProgress.PackUrTickets;
                Require(!world.EnterRpgRegion(RpgHideout) && CollectionProgress.TryFindRpgCache(regions[RpgHideout].AdventureSlot, out _) &&
                    CollectionProgress.PackUrTickets == tickets + 1, "The cache did not store the existing shop's ticket.");
                Require(world.EnterRpgRegion(RpgGarage), "Cannot return to the workshop.");

                Press(FieldScene(), "Exploration/Landmark");
                world.Update(4f);
                Press(panel, "ResidentChoices/WorkbenchMenu");
                Press(panel, "ResidentChoices/SaveRpg");
                Require(panel.Find("ResidentChoices/DailyLine/Label").GetComponent<Text>().text.Contains("진행을 저장"),
                    "Saving from the workbench did not provide confirmation.");
                world.Back();

                var rival = decks[script.episodes[0].deckId];
                balance = CollectionProgress.Balance;
                for (int i = 0; i < 3; i++) Require(CollectionProgress.TryAwardRpgPractice(rival, true, false, out _), "Practice reward failed.");
                Require(CollectionProgress.RpgRank == 2 && CollectionProgress.RpgExperience == 120 &&
                    CollectionProgress.RpgAffinity == 13 && CollectionProgress.Balance == balance + 1150,
                    "Practice XP, affinity or one-time level-up DP was incorrect.");
                world.QueueRpgRankNotice(1);
                world.EnterRpgRegion(RpgGarage);
                Require(panel.Find("RankUp") != null, "The rank-up banner was absent.");
                var merchant = (RectTransform)FieldScene().Find("RpgCardMerchant");
                FieldScene().Find("Scenery").GetComponent<TagForceFloorTap>().Click(merchant.anchorMin - Vector2.up * .05f);
                world.Update(2f);
                Require(panel.Find("FieldHud/ContextAction/Label").GetComponent<Text>().text.Contains("팩 구매"),
                    "The shared action did not offer the card merchant.");
                world.HandleFieldInput(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Submit));
                world.Update(4f);
                Press(panel, "ResidentChoices/OpenCardShop");
                Require(!world.Root.activeSelf && (Get("_collectionShop") as CollectionShopUI).Root.activeSelf,
                    "The workshop merchant did not open the collection shop.");
                typeof(GameAppController).GetMethod("CloseCollectionShop", flags).Invoke(app, null);
                Require(world.CanWalk, "Leaving the merchant did not return to the RPG.");
                Require(CollectionProgress.TryAwardStoryDuel(script.episodes[0].id, true, null, rival, default,
                    out _, out _, rpgField: true) && CollectionProgress.RpgExperience == 200,
                    "A story field result did not award its own RPG XP.");
                Press(FieldScene(), "Exploration/Landmark");
                world.Update(4f);
                Press(panel, "ResidentChoices/WorkbenchMenu");
                Press(panel, "ResidentChoices/Recall");
                Require(storyRoot.gameObject.activeSelf && !world.Root.activeSelf, "Workbench recall did not launch the last story.");
                var view = (StoryModeUI.StoryDialogueView)typeof(StoryModeUI).GetField("_dialogue", flags).GetValue(story);
                var storyArt = (Dictionary<string, Sprite>)typeof(StoryModeUI.StoryDialogueView).GetField("_rpgSprites", flags).GetValue(view);
                string recallPath = Path.Combine(streaming, "TagForce/5DS", regions[RpgAcademia].StoryBackdrop);
                Require(storyArt.TryGetValue(recallPath, out var replay) && replay != null &&
                    storyRoot.Find("Scene/RpgStage").GetComponent<Image>().sprite == replay,
                    "The recap did not use the episode location.");
                story.Back();
                Require(world.CanWalk && world.RpgClears == 1, "Leaving a recap altered the story progress.");
                int launches = 0;
                typeof(StoryModeUI).GetMethod("PlayRpgFaceoff", flags).Invoke(story,
                    new object[] { script.episodes[0], rival.title, (Action)(() => launches++) });
                story.Update(.19f);
                var faceoff = story.Root.transform.Find("RpgBossFaceoff");
                Require(faceoff != null && launches == 0 && faceoff.Find("Split").GetComponent<RpgFaceoffSplit>() != null &&
                    faceoff.Find("MatchTitle/DeckTitle/Label").GetComponent<Text>().text == rival.title,
                    "The boss faceoff skipped the split presentation or showed the wrong opponent deck.");
                story.Update(StoryModeUI.RpgFaceoffDuration - .20f);
                Require(launches == 0, "The faceoff launched before its display interval elapsed.");
                story.Update(.03f);
                Require(launches == 1 && story.Root.transform.Find("RpgBossFaceoff") == null, "The boss faceoff did not dispatch once.");

                var images = (TagForceWorldImages)typeof(TagForceWorldUI).GetField("_images", flags).GetValue(world);
                var loadedArt = (Dictionary<string, Sprite>)typeof(TagForceWorldImages).GetField("_loaded", flags).GetValue(images);
                images.Clear();
                loadedArt["TagForce/5DS/" + regions[RpgGarage].LightingBackdrop] = null;
                var previousField = images.Get(regions[RpgGarage].FieldBackground);
                Require(previousField != null && world.EnterRpgRegion(RpgGarage) &&
                    FieldScene().Find("Scenery").GetComponent<Image>().sprite == previousField,
                    "Missing lighting artwork did not fall back to the matching field version.");
                loadedArt[regions[RpgGarage].FieldBackground] = null;
                var originalGarage = images.Get(regions[RpgGarage].Background);
                Require(originalGarage != null && world.EnterRpgRegion(RpgGarage) &&
                    FieldScene().Find("Scenery").GetComponent<Image>().sprite == originalGarage,
                    "Missing field-specific art did not use the original map.");
                loadedArt[regions[RpgGarage].Background] = null;
                loadedArt["TagForce/UI/icon_quest_main.png"] = null;
                loadedArt["TagForce/UI/icon_quest_sub.png"] = null;
                loadedArt["TagForce/UI/hud_target.png"] = null;
                Require(world.EnterRpgRegion(RpgGarage) && FieldScene().Find("Scenery").GetComponent<Image>().sprite == DuelUiKit.WhiteSprite &&
                    FieldScene().Find("Scenery").GetComponent<Image>().color != Color.white &&
                    panel.Find("FieldHud/QuestTrack/GoalIcon/Label").GetComponent<Text>().text == "!",
                    "Missing optional map or quest-icon art did not fall back to colors and text.");
                loadedArt["TagForce/5DS/Maps/world_map_5ds.png"] = null;
                PressRpgNavigation("Map");
                Require(panel.Find(MapNodes + "/spot_facility_prison/MapLock").GetComponent<Image>().sprite != null &&
                    panel.Find("Atlas/MapInset/MapArt").GetComponent<Image>().sprite == DuelUiKit.WhiteSprite,
                    "The map nodes and lock symbols did not survive missing artwork.");
                world.Back();
                Debug.Log("[VERIFY 5DS ADVENTURE] Prologue and guide, map arrival, follower, affinity, saved cache, rank, workbench, shop, recap, faceoff and missing art passed.");
            }

            void RequireMasterRpgAi()
            {
                var ai = (TacticalAI)Get("_ai");
                var planner = typeof(TacticalAI).GetField("_comboSearch", flags).GetValue(ai);
                Require(ai.Difficulty == AIDifficulty.Master && planner != null &&
                    (int)typeof(ComboSearch).GetField("_responseLimit", flags).GetValue(planner) == 128 &&
                    (float)typeof(ComboSearch).GetField("_planningScale", flags).GetValue(planner) == ComboSearch.MaxPlanningScale,
                    "The RPG match allowed the global difficulty to override Master planning.");
            }

            void VerifyRpgPartnerPolish()
            {
                var music = Enumerable.Range(0, regions.Length).Select(MusicForRpgRegion).ToArray();
                Require(music.Distinct().Count() == 4 && music[RpgPoppo] != music[RpgMoment] &&
                    music.Distinct().Select(DuelAudio.GetRpgFieldMusic).Distinct().Count() == 4,
                    "The eleven fields did not resolve to four distinct background tracks.");
                string audit = Path.Combine(save, "rpg-partners");
                DeckRepository.Load(streaming, audit, seedDefaultDecks: false);
                CollectionProgress.Load(audit);
                Require(CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out _, out string error), error);
                Require(CollectionProgress.ActiveRpgPartner.Id == 35 && !CollectionProgress.TrySwitchRpgPartner(1, out _),
                    "A new profile did not honor the companion unlock gate.");
                foreach (var episode in script.episodes.Take(35))
                    Require(CollectionProgress.TryAwardStoryDuel(episode.id, true, null, decks[episode.deckId],
                        default, out _, out error), error);
                world.PrepareRpgField();
                world.Open();
                Require(world.EnterRpgRegion(RpgGarage), "The companion test could not reach the workbench.");
                int[] choices = { 1, 10, 16, 35, 1 };
                foreach (int id in choices)
                {
                    Sprite before = panel.Find("FieldHud/DuelistHud/Partner/PortraitFrame/Crop/Portrait").GetComponent<Image>().sprite;
                    Press(FieldScene(), "Exploration/Landmark");
                    world.Update(4f);
                    Press(panel, "ResidentChoices/WorkbenchMenu");
                    Press(panel, "ResidentChoices/ChangePartner");
                    Press(panel, $"ResidentChoices/Partner_{id}");
                    var companion = (RectTransform)typeof(TagForceWorldUI).GetField("_rpgCompanion", flags).GetValue(world);
                    Require(world.CanWalk && CollectionProgress.ActiveRpgPartner.Id == id && companion != null &&
                        panel.Find("FieldHud/DuelistHud/Partner/PortraitFrame/Crop/Portrait").GetComponent<Image>().sprite != before &&
                        panel.Find("PartnerGreeting/Speech/Label").GetComponent<Text>().text.StartsWith(CollectionProgress.ActiveRpgPartner.Name),
                        "Companion selection did not refresh the field, HUD portrait and region comment together.");
                    var actor = (RectTransform)typeof(TagForceWorldUI).GetField("_player", flags).GetValue(world);
                    var beforeWalk = actor.anchorMin;
                    var beforeFollow = companion.anchorMin;
                    var direction = beforeWalk.x < .5f ? Vector2.right : Vector2.left;
                    for (int step = 0; step < 12; step++) world.Update(.075f, direction);
                    Require(Vector2.Distance(beforeWalk, actor.anchorMin) > .15f &&
                        Vector2.Distance(beforeFollow, companion.anchorMin) > .04f &&
                        Vector2.Distance(actor.anchorMin, companion.anchorMin) < .13f,
                        $"Companion {id} did not follow the player from {beforeWalk} to {actor.anchorMin}. " +
                        $"Companion position: {beforeFollow} to {companion.anchorMin}.");
                }
                world.HandleFieldInput(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Alternate));
                Require(CollectionProgress.RpgAffinity == 4 && CollectionProgress.RpgAffinityFor(35) == 0,
                    "Partner affinity was not isolated by character.");
                world.Back();
                int tickets = CollectionProgress.PackUrTickets;
                int balance = CollectionProgress.Balance;
                for (int i = 0; i < 6; i++) Require(CollectionProgress.TryAwardRpgPractice(decks[59001], true, false, out error), error);
                world.EnterRpgRegion(RpgGarage);
                world.Update(.015f);
                Require(CollectionProgress.RpgAffinity == 22 && CollectionProgress.PackUrTickets == tickets + 1 &&
                    CollectionProgress.Balance >= balance + 1800 + 350 && panel.Find("PartnerBondReward/SpecialLine/Label") != null,
                    "The first heart did not atomically award DP, a ticket and the special dialogue notice.");
                CollectionProgress.Load(audit);
                Require(CollectionProgress.ActiveRpgPartner.Id == 1 && CollectionProgress.RpgAffinity == 22 &&
                    CollectionProgress.PackUrTickets == tickets + 1 && !CollectionProgress.TryTalkToRpgPartner(regions[RpgGarage].AdventureSlot, out _),
                    "Reloading lost the selected companion or allowed a reward conversation to repeat.");
                Require(world.EnterRpgRegion(RpgWrgp), "The timed duel check could not reach the WRGP field.");
                app.OpenRpgFieldEpisode(35);
                Press(storyRoot, "Scene/DialogueTools/Skip/Button");
                if (!storyRoot.Find("DeckSelection").gameObject.activeSelf)
                {
                    Press(storyRoot, "Scene/Choices/Choice_0/Button");
                    Press(storyRoot, "Scene/DialogueTools/Skip/Button");
                }
                Press(storyRoot, "DeckSelection/Window/Apply/Button");
                Press(storyRoot, "Scene/Choices/Choice_0/Button");
                Press(storyRoot, "Scene/Choices/Choice_1/Button");
                Press(storyRoot, "Scene/Choices/Choice_0/Button");
                Require(Get("_session") == null && story.Root.transform.Find("RpgBossFaceoff") != null,
                    "Story choices bypassed the VS animation.");
                story.Update(StoryModeUI.RpgFaceoffDuration + .02f);
                var launched = Get("_session") as DuelSession;
                Require(launched != null && launched.NativePtr != IntPtr.Zero && story.Root.transform.Find("RpgBossFaceoff") == null,
                    "The timed VS animation did not launch the native duel.");
                RequireMasterRpgAi();
                story.Update(.3f);
                Require(ReferenceEquals(launched, Get("_session")), "The VS animation launched twice.");
                AwardDuel(win: false);
                Press(storyRoot, "Scene/DialogueTools/Skip/Button");
                Require(world.CanWalk && world.RpgClears == 35, "The VS match did not return to the field on defeat.");
                Debug.Log("[VERIFY 5DS POLISH] Master settings, four regional themes, dialogue controls, timed faceoff and saved partner rewards passed.");
            }

            void VerifyRpgResort()
            {
                const string art = "TagForce/5DS/";
                const string resort = CollectionProgress.RpgResortSkin;
                const string automatic = CollectionProgress.RpgAutoOutfit;
                string resortSave = Path.Combine(save, "rpg-resort");
                DeckRepository.Load(streaming, resortSave, seedDefaultDecks: false);
                CollectionProgress.Load(resortSave);
                Require(CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out _, out string error), error);
                Require(!CollectionProgress.RpgEndingCleared &&
                    CollectionProgress.RpgResortCharacters.All(entry => !CollectionProgress.RpgResortUnlocked(entry.Id)) &&
                    !CollectionProgress.TryEquipRpgOutfit(1, resort, out _), "Resort outfits were usable before unlock.");

                var images = new TagForceWorldImages();
                try
                {
                    foreach (var entry in CollectionProgress.RpgResortCharacters)
                    {
                        string[] expressions = entry.Id == 1
                            ? new[] { "determined", "smile", "blush", "sad", "shock", "hurt" }
                            : new[] { "determined", "smile", "sad", "shock" };
                        var stem = art + RpgWardrobe.PortraitStem(entry.Id, resort);
                        foreach (var emotion in expressions)
                            Require(images.GetBust($"{stem}_{emotion}.png") is { } bust &&
                                bust.rect.size == new Vector2(448, 576), $"Resort portrait {entry.Id}/{emotion} is missing or cropped.");
                        var frames = images.GetSlices(art + RpgWardrobe.WalkAtlas(entry.Id, resort), 3, 3, fixedCells: true);
                        Require(frames is { Length: 9 } && frames.All(frame => frame.rect.size == new Vector2(256, 256)),
                            $"Resort field frames are missing for {entry.Id}.");
                    }
                }
                finally { images.Clear(); }

                world.PrepareRpgField();
                world.Open();
                Require(world.EnterRpgRegion(RpgGarage), "Could not reach the resort merchant.");
                Press(FieldScene(), "RpgCardMerchant/SD");
                world.Update(4f);
                Press(panel, "ResidentChoices/ResortShop");
                int balance = CollectionProgress.Balance;
                Press(panel, "Resort/Action");
                Require(CollectionProgress.RpgResortUnlocked(1) && CollectionProgress.RpgEquippedOutfit(1) == automatic &&
                    CollectionProgress.Balance == balance - CollectionProgress.RpgResortPrice &&
                    !CollectionProgress.RpgResortUnlocked(32), "The merchant did not charge for exactly one outfit.");
                Require(CollectionProgress.TryBuyRpgResort(1, out error) &&
                    CollectionProgress.Balance == balance - CollectionProgress.RpgResortPrice, "An outfit could charge twice.");
                world.Back();
                world.Back();
                ClearUntil(3);
                Require(CollectionProgress.TrySwitchRpgPartner(1, out error), error);
                Require(world.EnterRpgRegion(RpgGarage), "Could not reach the resort workbench.");
                OpenWorkbench();
                Require(panel.Find("ResidentChoices/SummerEpilogue") == null, "The bonus epilogue opened before the main ending.");
                Press(panel, "ResidentChoices/ChangeWardrobe");
                Press(panel, "Resort/Action");
                Require(RpgWardrobe.ForStory(1, 3) == resort && RpgWardrobe.ForField(1, RpgAcademia, 3) == resort &&
                    panel.Find("Resort/Portrait").GetComponent<Image>().sprite.rect.size == new Vector2(448, 576),
                    "The selected skin did not override both regular outfits or preview correctly.");
                world.Back();
                world.Back();
                var worldImages = (TagForceWorldImages)typeof(TagForceWorldUI).GetField("_images", flags).GetValue(world);
                var resortArt = worldImages.GetSlices(art + RpgWardrobe.WalkAtlas(1, resort), 3, 3, fixedCells: true);
                Require(FieldScene().Find("RpgCompanion/Sprite").GetComponent<Image>().sprite.texture == resortArt[0].texture,
                    "Aki's following sprite did not change into the equipped outfit.");
                world.HandleFieldInput(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Alternate));
                Require(panel.Find("ConversationPortrait").GetComponent<Image>().sprite ==
                    worldImages.Get(art + RpgWardrobe.Preview(1, resort)), "Partner conversation did not refresh the portrait.");
                world.Back();
                CollectionProgress.Load(resortSave);
                Require(CollectionProgress.RpgEquippedOutfit(1) == resort && CollectionProgress.RpgResortUnlocked(1),
                    "Purchased and equipped outfit state were lost after reload.");
                OpenWorkbench();
                Press(panel, "ResidentChoices/ChangeWardrobe");
                Press(panel, "Auto/Equip");
                Require(RpgWardrobe.ForStory(1, 3) == "school" && CollectionProgress.RpgResortUnlocked(1),
                    "Returning to automatic costumes removed the unlock or ignored the story outfit.");
                world.Back();
                world.Back();

                ClearUntil(50);
                Require(CollectionProgress.RpgEndingCleared &&
                    CollectionProgress.RpgResortCharacters.All(entry => CollectionProgress.RpgResortUnlocked(entry.Id)),
                    "The main ending did not unlock every resort outfit.");
                world.EnterRpgRegion(RpgGarage);
                OpenWorkbench();
                Press(panel, "ResidentChoices/ChangeWardrobe");
                foreach (var entry in CollectionProgress.RpgResortCharacters)
                {
                    Press(panel, $"WardrobeCharacters/Character_{entry.Id}");
                    Press(panel, "Resort/Action");
                    Require(RpgWardrobe.ForStory(entry.Id, 50) == resort &&
                        RpgWardrobe.ForField(entry.Id, RpgGarage, 50) == resort &&
                        panel.Find("Resort/FieldPreview").GetComponent<Image>().sprite.rect.size == new Vector2(256, 256),
                        $"The workbench could not apply and preview the resort outfit for {entry.Id}.");
                }
                CollectionProgress.Load(resortSave);
                Require(CollectionProgress.RpgResortCharacters.All(entry => CollectionProgress.RpgEquippedOutfit(entry.Id) == resort) &&
                    RpgWardrobe.ForStory(9, 50) == resort && RpgWardrobe.ForField(25, RpgGarage, 50) == resort,
                    "An owned outfit or the normal story/field variant mapping was lost after reload.");
                world.Back();
                Press(panel, "ResidentChoices/SummerEpilogue");
                var scene = world.Root.transform.Find("SummerEpilogue/StoryDialogue/Scene");
                var view = (StoryModeUI.StoryDialogueView)typeof(TagForceWorldUI).GetField("_rpgIntroDialogue", flags).GetValue(world);
                var artwork = (Dictionary<string, Sprite>)typeof(StoryModeUI.StoryDialogueView).GetField("_rpgSprites", flags).GetValue(view);
                string[] cg = { "cg_summer_beach_aki", "cg_summer_beach_resort_group", "cg_summer_beach_group" };
                for (int index = 0; index < cg.Length; index++)
                {
                    typeof(StoryModeUI.StoryDialogueView).GetMethod("BindLine", flags).Invoke(view, new object[] { index * 2, false });
                    world.Update(.5f);
                    string key = Path.Combine(streaming, art.TrimEnd('/'), $"Cutscenes/{cg[index]}.jpg");
                    // The story loader uses TagForce/5DS without a trailing separator for the cache key.
                    Require(artwork.TryGetValue(key, out var picture) && picture != null &&
                        scene.Find("CutsceneOverlay/CG").GetComponent<Image>().sprite == picture &&
                        Mathf.Approximately(picture.rect.width / picture.rect.height, 16f / 9f) &&
                        scene.Find("CutsceneOverlay").GetComponent<CanvasGroup>().alpha == 1f,
                        $"Summer CG {cg[index]} did not load as a full-screen story illustration.");
                }
                Press(scene, "DialogueTools/Skip/Button");
                Require(world.Root.transform.Find("SummerEpilogue") == null && panel.Find("ResidentChoices/ChangeWardrobe") != null,
                    "Skipping the summer story did not return to the workbench.");
                Debug.Log("[VERIFY 5DS RESORT] 22 expression portraits, 45 walk frames, early shop purchase, save persistence, live wardrobe and 3 epilogue CGs passed.");

                foreach (var (index, name, sceneId) in new[] { (0, "아키", "aki"), (1, "셰리", "sherry") })
                {
                    var romance = OpenSummerChoices();
                    Press(romance, $"Choices/Choice_{index}/Button");
                    Require(!romance.Find("Choices").gameObject.activeSelf &&
                        romance.Find("Episode/Label").GetComponent<Text>().text.StartsWith(name, StringComparison.Ordinal) &&
                        romance.Find("RpgStage").GetComponent<Image>().sprite is { } room &&
                        Mathf.Approximately(room.rect.width / room.rect.height, 16f / 9f),
                        $"The {name} choice did not open its room scene.");
                    for (int line = 0; line < 4; line++) Advance(romance);
                    var overlay = romance.Find("CutsceneOverlay");
                    var fade = overlay.GetComponent<CanvasGroup>();
                    var cgImage = overlay.Find("CG").GetComponent<Image>();
                    world.Update(.5f);
                    var routeView = (StoryModeUI.StoryDialogueView)typeof(TagForceWorldUI).GetField("_rpgIntroDialogue", flags).GetValue(world);
                    var routeArt = (Dictionary<string, Sprite>)typeof(StoryModeUI.StoryDialogueView).GetField("_rpgSprites", flags).GetValue(routeView);
                    string nightKey = Path.Combine(streaming, art.TrimEnd('/'), $"Cutscenes/cg_summer_romance_{sceneId}.jpg");
                    Require(routeArt.TryGetValue(nightKey, out var night) && night != null && cgImage.sprite == night &&
                        cgImage.enabled && fade.alpha == 1f, $"The {name} route did not show its night illustration.");
                    Advance(romance);
                    Require(cgImage.sprite == night && fade.alpha == 1f, $"The {name} scene disappeared during the conversation.");
                    Advance(romance);
                    string morningKey = Path.Combine(streaming, art.TrimEnd('/'), "Cutscenes/cg_summer_sea_morning.jpg");
                    Require(routeArt.TryGetValue(morningKey, out var morning) && morning != null && cgImage.sprite == morning &&
                        cgImage.enabled && fade.alpha == 1f && Mathf.Approximately(morning.rect.width / morning.rect.height, 16f / 9f),
                        $"The {name} route did not cut directly to the morning illustration.");
                    Advance(romance);
                    Require(AtWorkbench(), $"The {name} ending did not return to the workbench.");
                }
                var leave = OpenSummerChoices();
                Press(leave, "Choices/Choice_2/Button");
                Require(AtWorkbench(), "Leaving from the choice screen did not return to the workbench.");
                Debug.Log("[VERIFY 5DS SUMMER ROMANCE] Both character choices, distinct night CGs, direct morning transition and replay passed.");

                bool AtWorkbench() => world.Root.transform.Find("SummerEpilogue") == null &&
                    panel.Find("ResidentChoices/SummerEpilogue") != null;

                Transform OpenSummerChoices()
                {
                    Press(panel, "ResidentChoices/SummerEpilogue");
                    var opening = world.Root.transform.Find("SummerEpilogue/StoryDialogue/Scene");
                    for (int line = 0; line < 6; line++) Advance(opening);
                    Require(opening.Find("Choices").gameObject.activeSelf &&
                        opening.Find("Choices/Choice_0/Button").GetComponentInChildren<Text>().text == "이자요이 아키" &&
                        opening.Find("Choices/Choice_1/Button").GetComponentInChildren<Text>().text == "셰리 르블랑",
                        "Finishing the summer story did not offer the two character choices.");
                    return opening;
                }

                void Advance(Transform dialogue)
                {
                    world.Update(2f);
                    Press(dialogue, "DialogueBox/Next/Button");
                }

                void ClearUntil(int count)
                {
                    for (int index = world.RpgClears; index < count; index++)
                    {
                        var episode = script.episodes[index];
                        Require(CollectionProgress.TryAwardStoryDuel(episode.id, true, null, decks[episode.deckId],
                            default, out _, out error), error);
                    }
                }

                void OpenWorkbench()
                {
                    Press(FieldScene(), "Exploration/Landmark");
                    world.Update(4f);
                    Press(panel, "ResidentChoices/WorkbenchMenu");
                }
            }

            void VerifyStageExit()
            {
                var firstEpisode = loaded.episodes[0];
                Press(FieldScene(), "MainQuestNpc/SD");
                world.Update(4f);
                story.ShowFieldDuelResult(firstEpisode, true, false, () => "");
                var opponent = (RectTransform)FieldScene().Find("MainQuestNpc");
                Vector2 before = opponent.anchorMin;
                int exit = Array.FindIndex(firstEpisode.postWinDialogues,
                    line => line.stage?.Any(a => a.actor == firstEpisode.charId && a.action == "exit") == true);
                Require(exit > 0, "The first win scene did not have its scripted exit.");
                AdvanceToLine(exit);
                story.Update(.30f);
                Require(opponent.gameObject.activeSelf && opponent.anchorMin != before,
                    "The opponent did not begin walking out.");
                story.Update(.40f);
                Require(!opponent.gameObject.activeSelf && world.RpgStageRunning,
                    "The opponent did not finish leaving before the player's next beat.");
                Press(storyRoot, "Scene/DialogueTools/Previous/Button");
                Require(opponent.gameObject.activeSelf && opponent.anchorMin == before,
                    "Rewinding did not restore the actor before the exit.");
                Press(storyRoot, "Scene/DialogueTools/Skip/Button");
                Require(world.CanWalk && FieldScene().Find("DialogueCast") == null,
                    "Skipping a staged exit did not restore control of the field.");
            }

            void AdvanceToLine(int target)
            {
                for (int presses = 0; CurrentLine() < target && presses < 2 * (target + 1); presses++)
                {
                    if (world.RpgStageRunning) story.Update(3f);
                    Press(storyRoot, "Scene/DialogueBox/Next/Button");
                }
                Require(CurrentLine() == target, $"The dialogue did not advance to line {target}.");
            }

            int CurrentLine()
            {
                var dialogue = typeof(StoryModeUI).GetField("_dialogue", flags).GetValue(story);
                return (int)typeof(StoryModeUI.StoryDialogueView).GetField("_index", flags).GetValue(dialogue);
            }

            void VerifyStride(TagForceRpgStride stride)
            {
                Vector3[] corners = { Vector3.zero, new(0, 100), new(50, 100), new(50, 0) };
                using var mesh = new VertexHelper();
                Vector3 Foot(int frame, bool right)
                {
                    mesh.Clear();
                    foreach (var corner in corners)
                    {
                        var vertex = UIVertex.simpleVert;
                        vertex.position = corner;
                        mesh.AddVert(vertex);
                    }
                    stride.SetFrame(frame);
                    stride.ModifyMesh(mesh);
                    var foot = UIVertex.simpleVert;
                    mesh.PopulateUIVertex(ref foot, right ? (frame < 0 ? 3 : 8) : 0);
                    return foot.position;
                }

                Require(stride != null && Foot(0, false).x < Foot(2, false).x &&
                    Foot(1, true).y > Foot(1, false).y && Foot(3, false).y > Foot(3, true).y &&
                    Foot(-1, true) == corners[3] && mesh.currentVertCount == 4,
                    "A single-image NPC did not alternate its steps and restore its idle silhouette.");
            }

            void OpenEpisode6()
            {
                var field = FieldScene();
                Press(field, "MainQuestNpc/SD");
                world.Update(4f);
                Require(storyRoot.gameObject.activeSelf && story.FromRpgField && world.Root.activeSelf && !world.CanWalk &&
                    story.GetOpponentMatch(episode6).Deck.id == 59006,
                    "The current field NPC did not open the corresponding cinematic and RPG deck.");
                var guest = (RectTransform)field.Find("DialogueCast/Guest_1");
                Require(guest != null && !guest.gameObject.activeSelf &&
                    field.Find("Resident_1")?.gameObject.activeInHierarchy != true,
                    "Aki was visible before her scripted entrance in the hideout.");
                int entrance = Array.FindIndex(episode6.preDialogues,
                    line => line.stage?.Any(action => action.actor == 1 && action.action == "enter") == true);
                Require(entrance >= 0, "Episode six lost Aki's entrance.");
                Vector2 entryPosition = guest.anchorMin;
                AdvanceToLine(entrance);
                story.Update(2f);
                Require(guest.gameObject.activeInHierarchy && guest.anchorMin != entryPosition &&
                    guest.Find("Sprite").GetComponent<Image>().enabled,
                    "Aki did not visibly enter during the hideout conversation.");
                Press(storyRoot, "Scene/DialogueTools/Skip/Button");
                Press(storyRoot, "Scene/Choices/Choice_0/Button");
                Press(storyRoot, "Scene/DialogueTools/Skip/Button");
                Press(storyRoot, "DeckSelection/Window/Apply/Button");
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
