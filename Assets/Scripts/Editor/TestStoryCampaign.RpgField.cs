using System;
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
            int[] gates = { 0, 0, 6, 9, 11, 19, 27, 35, 44 };
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
                Require(File.Exists(Path.Combine(streaming, region.Background)), $"Missing 5D's map {region.Id}.");

            const string preference = "ygo.5ds-rpg.last-region";
            string oldPreference = PlayerPrefs.GetString(preference);
            bool hadPreference = PlayerPrefs.HasKey(preference);
            PlayerPrefs.DeleteKey(preference);
            var canvas = new GameObject("5D's field verification", typeof(Canvas));
            var events = new GameObject("5D's field controls", typeof(EventSystem));
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
                Set("_duelField", new DuelFieldUI(app, canvas.transform, modals));
                Set("_initialized", true);

                app.ShowScreen(AppScreen.MainMenu);
                var entry = menu.MenuRoot.GetComponentsInChildren<Button>()
                    .Single(button => button.transform.parent.name == "RpgWorld");
                entry.onClick.Invoke();
                Require(world.Root.activeSelf && FieldScene().Find("Player/Sprite").GetComponent<Image>().sprite != null &&
                    FieldScene().Find("MainQuestNpc/MainQuestMarker").GetComponent<Image>().sprite != null &&
                    world.CanStartRpgEpisode(0) && !world.CanStartRpgEpisode(1),
                    "RPG did not start as a walkable field with the first main quest.");
                Press(panel, "FieldHud/FieldMap");
                Require(panel.Find("spot_kai_garage/Travel").GetComponent<Button>().interactable &&
                    panel.Find("spot_satellite_hideout/Travel").GetComponent<Button>().interactable &&
                    panel.Find("spot_facility_prison/MapLock").GetComponent<Image>().sprite != null &&
                    !world.EnterRpgRegion(2), "The world map allowed early entry to the prison.");
                Press(world.Root.transform, "RpgNavigation/Decks");
                app.CloseDeckEditor();
                Require(panel.Find("spot_facility_prison/MapLock") != null,
                    "Returning from deck editing did not restore the RPG world map.");
                world.Back();

                VerifyFieldMovement();
                VerifyDirectionalArt();
                VerifySceneGuests();
                VerifyStageExit();

                var local = FieldScene().Cast<Transform>().First(child => child.name.StartsWith("Resident_", StringComparison.Ordinal));
                Require(local.Find("ResidentMarker").GetComponent<Image>().sprite != null, "The resident marker was missing.");
                Press(local, "Interact");
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
                Debug.Log("[VERIFY 5DS FIELD] Map gates, guest entrances, movement lock, practice rewards, story result order and saved progression passed.");
            }
            finally
            {
                (Get("_session") as DuelSession)?.Dispose();
                world.Close();
                story.Close();
                UnityEngine.Object.DestroyImmediate(canvas);
                UnityEngine.Object.DestroyImmediate(events);
                if (hadPreference) PlayerPrefs.SetString(preference, oldPreference);
                else PlayerPrefs.DeleteKey(preference);
                PlayerPrefs.Save();
            }

            Transform FieldScene() => panel.Find("FieldViewport/Camera");

            void VerifyFieldMovement()
            {
                Canvas.ForceUpdateCanvases();
                var camera = (RectTransform)FieldScene();
                var viewport = (RectTransform)camera.parent;
                var player = (RectTransform)camera.Find("Player");
                var map = (RectTransform)camera.Find("Scenery");
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
                Require(back != art.sprite && back.texture == art.sprite.texture && back.texture != firstStep.texture,
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
                Require(left - right > viewport.rect.width * .70f && hud.position == hudPosition,
                    "Walking across the field did not reveal a wide portion of the map or moved the fixed HUD.");

                var canvasRect = (RectTransform)canvas.transform;
                var desktopSize = canvasRect.sizeDelta;
                canvasRect.sizeDelta = new Vector2(2340f, 1080f);
                Canvas.ForceUpdateCanvases();
                world.Update(0f);
                VerifyCameraBounds();
                canvasRect.sizeDelta = desktopSize;
                Canvas.ForceUpdateCanvases();
                world.Update(0f);
                Debug.Log("[VERIFY 5DS SCROLL] Wide horizontal travel, aspect ratio, edge coverage, fixed HUD and panned clicks passed.");

                float CameraCenter() => viewport.InverseTransformPoint(camera.TransformPoint(camera.rect.center)).x;

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
                foreach (int id in new[] { 1, 16, 35, 138 })
                {
                    var art = DuelUiKit.FillRect($"Walk_{id}", canvas.transform);
                    try
                    {
                        buildArt.Invoke(world, new object[] { art, $"TagForce/5DS/Characters_SD/sd_{id}.png", id.ToString() });
                        var animator = art.GetComponent<TagForceRpgWalkAnimator>();
                        var image = art.GetComponent<Image>();
                        var front = image.sprite;
                        animator.Pose(.02f, true, Vector2.right);
                        var right = image.sprite;
                        animator.Pose(.08f, true, Vector2.right);
                        var nextStep = image.sprite;
                        animator.Pose(0f, false, Vector2.left);
                        var leftIdle = image.sprite;
                        bool flips = art.localScale.x < 0f;
                        animator.Pose(0f, false, Vector2.up);
                        var rear = image.sprite;
                        animator.Pose(.02f, true, Vector2.up);
                        var rearStep = image.sprite;
                        animator.Pose(0f, false, Vector2.down);
                        Require(right != nextStep && right != leftIdle && rear != rearStep && flips &&
                            right.texture == front.texture && right.rect.y > front.rect.y && front.rect.y > rear.rect.y &&
                            image.sprite == front && art.GetComponent<TagForceRpgStride>() == null,
                            $"Character {id} did not use its front, back and side walking sheet.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(art.gameObject); }
                }
                Debug.Log("[VERIFY 5DS WALK ART] Aki, Jack, Yusei and Vagabond directional poses passed.");
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
                    !storyRoot.Find("Scene/Portraits").gameObject.activeSelf &&
                    !guest.gameObject.activeSelf && guest.Find("Sprite").GetComponent<Image>().sprite != null &&
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
                    opponent.gameObject.activeSelf && opponent.anchorMin == otherStart && player.gameObject.activeSelf &&
                    ((RectTransform)field).anchorMin != cameraStart,
                    "The first shot did not follow Aki as she entered before the other actor moved.");
                VerifyStride(field.Find("Resident_210/SD").GetComponent<TagForceRpgStride>());
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
                Require(guest.Find("SpeakingCue") != null, "The speaking marker did not follow the guest.");
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
                    ((RectTransform)FieldScene().Find("Player")).anchorMin == position &&
                    world.Root.GetComponent<CanvasGroup>().blocksRaycasts,
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

            void VerifyStageExit()
            {
                var firstEpisode = loaded.episodes[0];
                Press(FieldScene(), "MainQuestNpc/Interact");
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
                var resident = (RectTransform)field.Find("Resident_1");
                Require(resident != null, "Aki was missing from the hideout before the conversation.");
                Vector2 residentPosition = resident.anchorMin;
                Press(field, "MainQuestNpc/Interact");
                world.Update(4f);
                Require(storyRoot.gameObject.activeSelf && story.FromRpgField && world.Root.activeSelf && !world.CanWalk &&
                    story.GetOpponentMatch(episode6).Deck.id == 59006,
                    "The current field NPC did not open the corresponding cinematic and RPG deck.");
                Require(resident.gameObject.activeInHierarchy && field.Find("DialogueCast/Guest_1") == null,
                    "The dialogue replaced an existing field resident with a duplicate.");
                Press(storyRoot, "Scene/DialogueBox/Skip/Button");
                Press(storyRoot, "Scene/Choices/Choice_0/Button");
                Press(storyRoot, "Scene/DialogueBox/Skip/Button");
                Require(resident.anchorMin != residentPosition, "The resident did not join the conversation.");
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
