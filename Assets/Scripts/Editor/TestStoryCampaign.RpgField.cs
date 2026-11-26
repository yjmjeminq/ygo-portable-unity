using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static partial class TestStoryCampaign
    {
        private static void VerifyRpgField(UserDeck playerDeck, string save)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            string streaming = Application.streamingAssetsPath;
            var script = StoryModeUI.LoadRpgScript(streaming);
            var decks = StoryModeUI.LoadRpgDecks(streaming);
            var regions = TagForceWorldUI.RpgRegions;
            int[] gates = { 0, 0, 6, 8, 8, 18, 27, 34, 44 };
            int[] expectedRegions =
            {
                0, 0, 0, 1, 1, 1, 2, 2, 4, 4, 4, 4, 4, 3, 3, 3, 3, 3, 5, 5,
                5, 5, 5, 5, 5, 5, 5, 6, 6, 3, 3, 0, 3, 0, 7, 7, 7, 7, 7, 7,
                7, 7, 7, 7, 8, 8, 8, 8, 8, 6,
            };
            Require(Enumerable.Range(1, script.episodes.Length).Select(TagForceWorldUI.RegionForEpisode)
                .SequenceEqual(expectedRegions), "A 5D's episode was routed to the wrong field.");
            Require(regions.Select(region => region.RequiredEpisode).SequenceEqual(gates),
                "The nine RPG region gates did not match the campaign.");
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
            var story = new StoryModeUI(app, canvas.transform);
            var world = new TagForceWorldUI(app, canvas.transform);
            var menu = new TitleAndMenuUI(app, canvas.transform);
            var modals = new DuelPromptModals(canvas.transform, null, null);
            var panel = world.Root.transform.Find("World");
            var storyRoot = story.Root.transform.Find("StoryDialogue");
            var loaded = (StoryScriptRoot)typeof(StoryModeUI).GetField("_rpgScript", flags).GetValue(story);
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
                Press(panel, "StoryDialogue/Scene/DialogueBox/Skip/Button");
                Require(CollectionProgress.RpgIntroSeen, "Skipping the prologue did not persist completion.");
                Require(world.Root.activeSelf && FieldScene().Find("Player/Sprite").GetComponent<Image>().sprite != null &&
                    FieldScene().Find("MainQuestNpc/MainQuestMarker").GetComponent<Image>().sprite != null &&
                    world.CanStartRpgEpisode(0) && !world.CanStartRpgEpisode(1),
                    "RPG did not start as a walkable field with the first main quest.");
                PressRpgNavigation("Map");
                Require(panel.Find("spot_kai_garage/Travel").GetComponent<Button>().interactable &&
                    panel.Find("spot_satellite_hideout/Travel").GetComponent<Button>().interactable &&
                    panel.Find("spot_facility_prison/MapLock").GetComponent<Image>().sprite != null &&
                    !world.EnterRpgRegion(2), "The world map allowed early entry to the prison.");
                PressRpgNavigation("Decks");
                app.CloseDeckEditor();
                Require(panel.Find("spot_facility_prison/MapLock") != null,
                    "Returning from deck editing did not restore the RPG world map.");
                world.Back();

                VerifyRpgHud();
                VerifyFieldMovement();
                VerifyDirectionalArt();
                VerifyResidentProfiles();
                VerifyResidentActivity();
                VerifySceneGuests();
                VerifyStageExit();

                var local = FieldScene().Find($"Resident_{script.episodes[1].charId}");
                Require(local.Find("ResidentMarker").GetComponent<Image>().sprite != null, "The resident marker was missing.");
                Press(local, "SD");
                world.Update(4f);
                Press(panel, "ResidentChoices/TalkDaily");
                Require(panel.Find("ResidentChoices/DailyLine/Label").GetComponent<Text>().text.Contains("정비소"),
                    "The resident did not offer local dialogue.");
                Press(panel, "ResidentChoices/Practice");
                int balance = CollectionProgress.Balance;
                int owned = decks[script.episodes[1].deckId].main.Distinct().Sum(CollectionProgress.GetTotalOwnedCount);
                Press(panel, "StartPractice");
                AwardDuel(win: true);
                Require(world.Root.activeSelf && world.RpgClears == 0 &&
                    CollectionProgress.Balance == balance + 300 &&
                    decks[script.episodes[1].deckId].main.Distinct().Sum(CollectionProgress.GetTotalOwnedCount) == owned + 1,
                    "A practice win did not award DP and a card without advancing the story.");
                VerifySideQuests();

                foreach (var episode in script.episodes.Take(5))
                    Require(CollectionProgress.TryAwardStoryDuel(episode.id, true, null, decks[episode.deckId],
                        out _, out _, out _), $"Could not stage {episode.id}.");
                Require(world.EnterRpgRegion(1) && world.CanStartRpgEpisode(5), "Episode 6 NPC was not in the hideout.");
                OpenEpisode6();
                app.StartStoryDuel(episode6, playerDeck, true);
                AwardDuel(win: false);
                Require(storyRoot.gameObject.activeSelf && world.Root.activeSelf && !world.CanWalk && world.RpgClears == 5,
                    "A loss did not stage its dialogue in the locked field.");
                Press(storyRoot, "Scene/DialogueBox/Skip/Button");
                Require(world.Root.activeSelf && world.CanStartRpgEpisode(5) && !world.EnterRpgRegion(2),
                    "A loss changed the active NPC or unlocked the next region.");

                OpenEpisode6();
                app.StartStoryDuel(episode6, playerDeck, true);
                balance = CollectionProgress.Balance;
                AwardDuel(win: true);
                Require(storyRoot.gameObject.activeSelf && world.RpgClears == 5 && CollectionProgress.Balance == balance,
                    "The win was settled before the post-win dialogue.");
                Press(storyRoot, "Scene/DialogueBox/Skip/Button");
                Require(world.Root.activeSelf && world.RpgClears == 6 && CollectionProgress.Balance > balance &&
                    panel.Find("RegionUnlockToast/Label").GetComponent<Text>().text.Contains("시큐리티 수용소"),
                    "Winning did not save the clear, pay DP and announce the new region.");
                CollectionProgress.Load(save);
                Require(world.EnterRpgRegion(2) && world.CanStartRpgEpisode(6) && world.RpgClears == 6,
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
                Debug.Log("[VERIFY 5DS FIELD] Map gates, guest entrances, movement lock, practice rewards, story result order and saved progression passed.");
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

            void VerifyAtmosphere(int region)
            {
                var field = FieldScene();
                var layer = field.Find("FieldAtmosphere");
                var shadow = field.Find("Player/GroundShadow").GetComponent<Image>();
                Require(layer != null && layer.GetSiblingIndex() > field.Find("Scenery").GetSiblingIndex() &&
                    layer.GetSiblingIndex() < field.Find("Player").GetSiblingIndex() &&
                    layer.GetComponentsInChildren<Image>().All(image => !image.raycastTarget) &&
                    shadow.sprite != null && !shadow.raycastTarget &&
                    (layer.Find("Haze") != null) == (region is 5 or 6 or 8),
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
                    var expected = region.LightingBackdrop == null ? baseScenery : images.Get("TagForce/5DS/" + region.LightingBackdrop);
                    Require(expected != null && expected.rect.size == baseScenery.rect.size &&
                        FieldScene().Find("Scenery").GetComponent<Image>().sprite == expected,
                        $"The field used incorrect artwork or altered the framing of {region.Id}.");
                }
                PressRpgNavigation("Map");
                foreach (var region in regions.Where(region => region.LightingBackdrop != null))
                    Require(panel.Find($"{region.Id}/MapPreview").GetComponent<Image>().sprite ==
                        images.Get("TagForce/5DS/" + region.LightingBackdrop), $"The map preview used old artwork for {region.Id}.");
                world.Back();
                Debug.Log("[VERIFY 5DS MAP LIGHTING] All nine fields and the four revised map previews loaded with the original framing.");
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
                    mapButton.GetComponentInChildren<Text>().font == Resources.Load<Font>("StoryFonts/Pretendard-Medium"),
                    "RPG navigation lost its rounded treatment or visible controller focus.");
                var player = (RectTransform)FieldScene().Find("Player");
                var start = player.anchorMin;
                Require(!action.gameObject.activeSelf && !mapButton.gameObject.activeInHierarchy &&
                    panel.Find("FieldViewport").GetComponent<CanvasGroup>().blocksRaycasts &&
                    hud.Find("Deck/DeckName/Label").GetComponent<Text>().text == playerDeck.name &&
                    hud.Find("DuelistHud/Partner/PortraitFrame/Portrait").GetComponent<Image>().sprite != null &&
                    hud.Find("QuestTrack/Objective/Label").GetComponent<Text>().text.Contains(script.episodes[0].opponentName) &&
                    hud.Find("QuestTrack/GoalIcon").GetComponent<Image>().sprite != null,
                    "The field HUD exposed a far interaction, a duplicate map, or omitted the objective.");
                world.HandleFieldInput(padSubmit);
                world.Update(.25f);
                Require(player.anchorMin == start && world.CanWalk, "Selecting away from every target started an unintended walk.");

                var landmark = (RectTransform)FieldScene().Find("Exploration/Landmark");
                FieldScene().Find("Scenery").GetComponent<TagForceFloorTap>().Click(landmark.anchorMin);
                world.Update(4f);
                Require(action.gameObject.activeSelf && action.Find("Label").GetComponent<Text>().text == "정비 작업대 · 조사",
                    "The prompt did not identify the nearby landmark.");
                var menuToggle = new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Search);
                world.HandleFieldInput(menuToggle);
                var atLandmark = player.anchorMin;
                world.Update(.4f, Vector2.left);
                Require(!action.gameObject.activeSelf && mapButton.gameObject.activeInHierarchy &&
                    world.RpgFieldMenuScope == mapButton.transform.parent.gameObject &&
                    EventSystem.current.currentSelectedGameObject == mapButton.gameObject &&
                    player.anchorMin == atLandmark && !panel.Find("FieldViewport").GetComponent<CanvasGroup>().blocksRaycasts,
                    "The field menu did not capture controller focus and suspend field interaction.");
                var handlePad = typeof(GameAppController).GetMethod("HandleMenuInput", flags);
                handlePad.Invoke(app, new object[] { new DeckPadFrame(Vector2Int.down, DeckPadButtons.None), false });
                Require(EventSystem.current.currentSelectedGameObject == mapButton.transform.parent.Find("Quests").gameObject,
                    "The controller moved its focus outside the field commands.");
                handlePad.Invoke(app, new object[] { new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Cancel), false });
                Require(!mapButton.gameObject.activeInHierarchy && action.gameObject.activeSelf,
                    "Cancel did not return to the active field.");
                Press(hud, "ContextAction");
                world.Update(.1f);
                Require(!world.CanWalk && panel.Find("ResidentChoices/WorkbenchMenu") != null,
                    "Tapping the action prompt opened the wrong target.");
                world.Back();
                Require(world.EnterRpgRegion(1) && world.EnterRpgRegion(0), "Could not reset the field after HUD verification.");
                Debug.Log("[VERIFY 5DS HUD] Deck, partner, quest, proximity, touch action and controller menu passed.");
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
                        buildArt.Invoke(world, new object[] { art, $"TagForce/5DS/Characters_SD/sd_{id}.png", id });
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
                    buildArt.Invoke(world, new object[] { art, "TagForce/5DS/Characters_SD/sd_33.png", "야나기 텐젠" });
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
                    Require(File.Exists(cycle + "_directions.png") || File.Exists(cycle + "_side_idle.png"),
                        $"RPG field character {id} lacked a side-facing sprite.");
                }
                Require(world.EnterRpgRegion(1) && world.EnterRpgRegion(0), "Could not reset the garage.");
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
                Require(world.EnterRpgRegion(1) && world.EnterRpgRegion(0), "Could not reset the garage after turning.");
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
                var questGiver = (RectTransform)field.Find("QuestNpc_10");
                var requestHome = questGiver.anchorMin;
                Require(marker.alpha == 0f && main.Find("MainQuestMarker/Label").GetComponent<Text>().text == "!" &&
                    field.Find("QuestNpc_10/ResidentMarker/Label").GetComponent<Text>().text == "?" &&
                    field.Find("QuestNpc_10/QuestBadge") == null &&
                    field.Find("RpgCardMerchant/ResidentMarker/Label").GetComponent<Text>().text == "▣",
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
                Require(world.EnterRpgRegion(1) && world.EnterRpgRegion(0), "Could not reset the field after resident activity.");
                Debug.Log("[VERIFY 5DS RESIDENTS] Idle-player routes, separate footsteps, menu pause and contextual markers passed.");
            }

            void VerifySceneGuests()
            {
                var field = FieldScene();
                var player = (RectTransform)field.Find("Player");
                var opponent = (RectTransform)field.Find("MainQuestNpc");
                WalkToQuestScene(player);
                Vector2 position = player.anchorMin;
                var cast = field.Find("DialogueCast");
                Require(cast != null && world.RpgStageRunning, "Walking near the main quest did not start its opening shot.");
                var guest = (RectTransform)cast.Find("Guest_1");
                Require(storyRoot.gameObject.activeSelf && world.Root.activeSelf && !world.CanWalk &&
                    world.Root.transform.GetSiblingIndex() < story.Root.transform.GetSiblingIndex() &&
                    field.Find("Player") == player && field.Find("MainQuestNpc") == opponent &&
                    !storyRoot.Find("Scene/Portraits/Protagonist").GetComponent<Image>().enabled &&
                    !storyRoot.Find("Scene/Portraits/Opponent").GetComponent<Image>().enabled &&
                    player.Find("Sprite").GetComponent<Image>().enabled && opponent.Find("SD").GetComponent<Image>().enabled &&
                    !guest.gameObject.activeSelf && guest.Find("Sprite").GetComponent<Image>().sprite != null &&
                    !field.Find("RpgCardMerchant").gameObject.activeSelf &&
                    !world.Root.GetComponent<CanvasGroup>().blocksRaycasts,
                    "The conversation did not preserve the real field or hold Aki until her first line.");
                world.Update(.5f, Vector2.right);
                Require(player.anchorMin == position && !world.EnterRpgRegion(1) &&
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
                Press(storyRoot, "Scene/DialogueBox/Previous/Button");
                Require(!guest.gameObject.activeSelf && opponent.anchorMin == otherStart && !world.RpgStageRunning,
                    "Rewinding did not restore the actors before the two-beat scene.");
                Press(storyRoot, "Scene/DialogueBox/Next/Button");
                story.Update(1.35f);
                Require(guest.gameObject.activeSelf && guest.GetComponent<CanvasGroup>().alpha == 1f &&
                    guest.anchorMin != entry && !world.RpgStageRunning && guest.Find("Sprite").localScale.x > 0f,
                    "The guest did not reach her mark and face the rival after replaying the entrance.");
                AdvanceToLine(firstAkiLine);
                var portrait = storyRoot.Find("Scene/Portraits/Opponent").GetComponent<Image>();
                Require(portrait.enabled && portrait.sprite.rect.height < portrait.sprite.texture.height &&
                    guest.Find("Sprite").GetComponent<Image>().enabled,
                    "Aki's dialogue did not keep both the field actor and upper-body portrait visible.");
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
                story.Update(.65f);
                Require(player.anchorMin != position && !world.CanWalk, "The hero did not respond to the scripted movement.");
                app.OpenStoryDeckEditor();
                Require(!world.Root.activeSelf, "The field remained over the deck editor.");
                app.CloseDeckEditor();
                Require(guest.gameObject.activeInHierarchy && !world.CanWalk, "Deck editing lost the active field scene.");
                story.Back();
                Require(world.CanWalk && FieldScene().Find("DialogueCast") == null &&
                    FieldScene().Find("Player/Sprite").GetComponent<Image>().enabled &&
                    ((RectTransform)FieldScene().Find("Player")).anchorMin == position &&
                    world.Root.GetComponent<CanvasGroup>().blocksRaycasts && panel.Find("FieldHud").gameObject.activeSelf &&
                    FieldScene().Find("RpgCardMerchant").gameObject.activeInHierarchy,
                    "Leaving the conversation did not remove its cast and restore movement at the original location.");
                world.Update(.25f, Vector2.down);
                for (int frame = 0; frame < 3; frame++) world.Update(.15f, Vector2.up);
                Require(world.CanWalk && !storyRoot.gameObject.activeSelf,
                    "The automatic scene immediately reopened after leaving it.");
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
                    Require(world.EnterRpgRegion(region == 0 ? 1 : 0) && world.EnterRpgRegion(region),
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
                    Require(world.EnterRpgRegion(region == 0 ? 1 : 0) && !world.CanStartRpgEpisode(index) &&
                        FieldScene().Find("MainQuestNpc") == null &&
                        panel.Find("FieldHud/QuestTrack/Chapter/Label").GetComponent<Text>().text == $"EP {number:00} · {target}" &&
                        panel.Find("FieldHud/QuestTrack/Objective/Label").GetComponent<Text>().text == $"이동 · {target}",
                        $"The field directed episode {number} to the wrong destination.");
                    PressRpgNavigation("Map");
                    foreach (var (area, i) in regions.Select((area, i) => (area, i)))
                        Require((panel.Find(area.Id).GetComponent<Image>().sprite.name == "RPG active panel") == (i == region),
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
                Require(world.EnterRpgRegion(0) && FieldScene().Find("SideQuestItem") == null,
                    "A quest item appeared before accepting its request.");
                TalkTo(10);
                Press(panel, "ResidentChoices/AcceptQuest");
                Require(CollectionProgress.GetRpgQuestState(tools) == RpgQuestState.Active && !world.CanWalk &&
                    FieldScene().Find("Player") != null, "Accepting a request left the field or lost its actors.");
                world.Back();
                Press(FieldScene(), "SideQuestItem");
                world.Update(4f);
                CollectionProgress.Load(save);
                Require(CollectionProgress.GetRpgQuestState(tools) == RpgQuestState.Ready &&
                    FieldScene().Find("SideQuestItem") == null && CollectionProgress.Balance == before &&
                    FieldScene().Find("QuestNpc_10/ResidentMarker/Label").GetComponent<Text>().text == "✓",
                    "Investigating did not persist the objective or granted an early reward.");
                TalkTo(10);
                Press(panel, "ResidentChoices/ClaimQuest");
                Require(CollectionProgress.Balance == before + RpgSideQuest.Find(tools).Reward &&
                    !CollectionProgress.TryAdvanceRpgQuest(tools, RpgQuestState.Ready, out _, out _),
                    "The reward was missing or could be claimed twice.");
                world.Back();

                Require(world.EnterRpgRegion(1), "The hideout was unavailable for the delivery request.");
                TalkTo(33);
                Press(panel, "ResidentChoices/AcceptQuest");
                world.Back();
                PressRpgNavigation("Quests");
                var journal = panel.Find("QuestJournal/Items").GetComponentsInChildren<Text>();
                Require(journal.Any(text => text.text.Contains("크로우 호건에게 부품 상자 전하기")),
                    "The quest journal was missing the active delivery destination.");
                world.Back();
                Require(world.EnterRpgRegion(0), "The garage was unreachable during delivery.");
                Require(FieldScene().Find("QuestNpc_10/ResidentMarker/Label").GetComponent<Text>().text == "›",
                    "The delivery receiver did not show the handoff marker.");
                TalkTo(10);
                Press(panel, "ResidentChoices/DeliverQuest");
                world.Back();
                CollectionProgress.Load(save);
                Require(CollectionProgress.GetRpgQuestState(tools) == RpgQuestState.Complete &&
                    CollectionProgress.GetRpgQuestState(parts) == RpgQuestState.Ready && world.RpgClears == 0,
                    "The delivery did not persist independently of the main story and the earlier reward.");
                Require(world.EnterRpgRegion(1), "The sender's region was unavailable after delivery.");
                TalkTo(33);
                Press(panel, "ResidentChoices/ClaimQuest");
                Require(CollectionProgress.Balance == before + RpgSideQuest.Find(tools).Reward + RpgSideQuest.Find(parts).Reward,
                    "The delivery reward was incorrect.");
                world.Back();
                Debug.Log("[VERIFY 5DS QUESTS] Field conversation, item search, inter-region delivery, journal, reload and one-time rewards passed.");

                void TalkTo(int id)
                {
                    Press(FieldScene(), $"QuestNpc_{id}/SD");
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
                    Require(world.EnterRpgRegion(0), "The garage was unavailable in the exploration test.");
                    var viewport = (RectTransform)panel.Find("FieldViewport");
                    var track = (RectTransform)panel.Find("FieldHud/QuestTrack");
                    Require(viewport.anchorMax == Vector2.one && track.anchorMin.y > .80f && track.anchorMax.x < .32f,
                        "The quest tracker no longer left the middle of the full-screen field clear.");
                    ExamineLandmark();
                    Require(panel.Find("ResidentChoices/AskCrow") == null && !world.CanWalk,
                        "The workbench exposed the late bridge quest early, or allowed motion while inspecting.");
                    world.Back();
                    WalkOut(1, Vector2.right, .18f);
                    var locked = FieldScene().Find("Exploration/Exit_6").GetComponent<Button>();
                    Require(!locked.IsInteractable(), "The bridge exit opened before the story gate.");
                    PointAt(new Vector2(.805f, .20f));
                    for (int i = 0; i < 3; i++) world.Update(.5f, Vector2.right);
                    Require(FieldScene().Find("Exploration/Exit_6") != null && !world.EnterRpgRegion(6),
                        "Walking near a locked exit bypassed the story gate.");

                    foreach (var episode in script.episodes.Take(regions[6].RequiredEpisode))
                        Require(CollectionProgress.TryAwardStoryDuel(episode.id, true, null, decks[episode.deckId],
                            out _, out _, out _), $"Could not stage {episode.id}.");
                    Require(world.EnterRpgRegion(0), "Could not return to the garage.");
                    if (askCrow) ExamineLandmark(withController: true);
                    else ExamineLandmark();
                    if (askCrow)
                    {
                        Press(panel, "ResidentChoices/AskCrow");
                        world.Update(4f);
                        Require(panel.Find("ConversationPortrait").GetComponent<Image>().sprite != null &&
                            FieldScene().Find("QuestNpc_10/SD").GetComponent<Image>().enabled,
                            "Choosing to ask Crow did not show both his field actor and his upper-body portrait.");
                        Press(panel, "ResidentChoices/AskCoupler");
                    }
                    else Press(panel, "ResidentChoices/ReadRunnerLog");
                    CollectionProgress.Load(audit);
                    Require(CollectionProgress.GetRpgQuestState(coupler) == RpgQuestState.Active,
                        "The chosen clue route did not save the quest.");
                    world.Back();
                    WalkOut(1, Vector2.right, .18f);
                    WalkOut(6, Vector2.right, .18f);
                    ExamineLandmark(withController: askCrow);
                    Press(panel, "ResidentChoices/RecoverCoupler");
                    CollectionProgress.Load(audit);
                    Require(CollectionProgress.GetRpgQuestState(coupler) == RpgQuestState.Ready,
                        "The bridge investigation did not persist the recovered component.");
                    world.Back();
                    WalkOut(1, Vector2.left, .82f);
                    WalkOut(0, Vector2.left, .82f);
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
                Press(panel, "StoryDialogue/Scene/DialogueBox/Skip/Button");
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
                world.HandleFieldInput(talk);
                Require(!world.CanWalk && CollectionProgress.RpgAffinity == 4, "Talking to the partner did not grant affinity.");
                world.Back();
                world.HandleFieldInput(talk);
                Require(CollectionProgress.RpgAffinity == 4, "Repeatedly opening the same dialogue allowed affinity farming.");
                world.Back();

                int balance = CollectionProgress.Balance;
                Press(FieldScene(), "Exploration/HiddenCache");
                world.Update(4f);
                CollectionProgress.Load(audit);
                Require(CollectionProgress.Balance == balance + 180 && CollectionProgress.RpgCacheFound(0) &&
                    FieldScene().Find("Exploration/HiddenCache") == null &&
                    !CollectionProgress.TryFindRpgCache(0, out _), "The cache did not persist a one-time payout.");
                int tickets = CollectionProgress.PackUrTickets;
                Require(world.EnterRpgRegion(1) && CollectionProgress.TryFindRpgCache(1, out _) &&
                    CollectionProgress.PackUrTickets == tickets + 1, "The cache did not store the existing shop's ticket.");
                Require(world.EnterRpgRegion(0), "Cannot return to the workshop.");

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
                world.EnterRpgRegion(0);
                Require(panel.Find("RankUp") != null, "The rank-up banner was absent.");
                Press(FieldScene(), "RpgCardMerchant/SD");
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
                string recallPath = Path.Combine(streaming, "TagForce/5DS", regions[0].LightingBackdrop);
                Require(storyArt.TryGetValue(recallPath, out var relitRecall) && relitRecall != null &&
                    storyRoot.Find("Scene/RpgStage").GetComponent<Image>().sprite == relitRecall,
                    "The recap did not load the new lighting artwork.");
                story.Back();
                Require(world.CanWalk && world.RpgClears == 1, "Leaving a recap altered the story progress.");
                int launches = 0;
                typeof(StoryModeUI).GetMethod("PlayRpgFaceoff", flags).Invoke(story,
                    new object[] { script.episodes[0], (Action)(() => launches++) });
                story.Update(.19f);
                Require(story.Root.transform.Find("RpgBossFaceoff") != null && launches == 0, "The boss faceoff skipped its presentation.");
                story.Update(1.5f);
                Require(launches == 1 && story.Root.transform.Find("RpgBossFaceoff") == null, "The boss faceoff did not dispatch once.");

                var images = (TagForceWorldImages)typeof(TagForceWorldUI).GetField("_images", flags).GetValue(world);
                var loadedArt = (Dictionary<string, Sprite>)typeof(TagForceWorldImages).GetField("_loaded", flags).GetValue(images);
                images.Clear();
                loadedArt["TagForce/5DS/" + regions[0].LightingBackdrop] = null;
                var previousField = images.Get(regions[0].FieldBackground);
                Require(previousField != null && world.EnterRpgRegion(0) &&
                    FieldScene().Find("Scenery").GetComponent<Image>().sprite == previousField,
                    "Missing lighting artwork did not fall back to the matching field version.");
                loadedArt[regions[0].FieldBackground] = null;
                var originalGarage = images.Get(regions[0].Background);
                Require(originalGarage != null && world.EnterRpgRegion(0) &&
                    FieldScene().Find("Scenery").GetComponent<Image>().sprite == originalGarage,
                    "Missing field-specific art did not use the original map.");
                loadedArt[regions[0].Background] = null;
                loadedArt["TagForce/UI/icon_quest_main.png"] = null;
                loadedArt["TagForce/UI/icon_quest_sub.png"] = null;
                loadedArt["TagForce/UI/hud_target.png"] = null;
                Require(world.EnterRpgRegion(0) && FieldScene().Find("Scenery").GetComponent<Image>().sprite == DuelUiKit.WhiteSprite &&
                    FieldScene().Find("Scenery").GetComponent<Image>().color != Color.white &&
                    FieldScene().Find("MainQuestNpc/MainQuestMarker/Label").GetComponent<Text>().text == "!" &&
                    panel.Find("FieldHud/QuestTrack/GoalIcon/Label").GetComponent<Text>().text == "!",
                    "Missing optional map or quest-icon art did not fall back to colors and text.");
                loadedArt["TagForce/UI/icon_map_lock.png"] = null;
                PressRpgNavigation("Map");
                Require(panel.Find("spot_facility_prison/MapLock/Label") != null,
                    "The locked map needed external icon art.");
                world.Back();
                Debug.Log("[VERIFY 5DS ADVENTURE] Prologue and guide, map arrival, follower, affinity, saved cache, rank, workbench, shop, recap, faceoff and missing art passed.");
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
                Press(storyRoot, "Scene/DialogueBox/Previous/Button");
                Require(opponent.gameObject.activeSelf && opponent.anchorMin == before,
                    "Rewinding did not restore the actor before the exit.");
                Press(storyRoot, "Scene/DialogueBox/Skip/Button");
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
                Press(storyRoot, "Scene/DialogueBox/Skip/Button");
                Press(storyRoot, "Scene/Choices/Choice_0/Button");
                Press(storyRoot, "Scene/DialogueBox/Skip/Button");
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
