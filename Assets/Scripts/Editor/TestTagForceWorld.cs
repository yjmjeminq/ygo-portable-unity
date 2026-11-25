using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static class TestTagForceWorld
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            int exit = 1;
            var scratch = Path.Combine(Path.GetTempPath(), "ygo-tag-force-" + Guid.NewGuid().ToString("N"));
            var canvas = new GameObject("Tag Force world verification", typeof(Canvas));
            var app = canvas.AddComponent<GameAppController>();
            TagForceWorldUI world = null;
            float music = DuelAudio.BgmVolume;
            float effects = DuelAudio.SfxVolume;
            try
            {
                var streaming = Application.streamingAssetsPath;
                CardDatabase.Load(streaming);
                DeckRepository.Load(streaming, scratch, seedDefaultDecks: false);
                CollectionProgress.Load(scratch);
                Require(CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out _, out var error), error);
                DuelAudio.BgmVolume = 0;
                DuelAudio.SfxVolume = 0;
                DuelAudio.EnsureInit(canvas);
                var bgm = canvas.GetComponents<AudioSource>()[1];
                var menu = new TitleAndMenuUI(app, canvas.transform);
                Require(menu.MenuRoot.GetComponentsInChildren<Button>(true).Any(button => button.transform.parent.name == "TagForceWorld"),
                    "The main menu has no Tag Force button.");
                world = new TagForceWorldUI(app, canvas.transform);
                var panel = world.Root.transform.Find("World");

                VerifyAssets(streaming);
                VerifyFallback(streaming, scratch);
                VerifyTravel(world, panel, streaming);
                VerifyMotionAndDialogue(world, panel, bgm);
                VerifySavedWorlds(world, panel, scratch, bgm);
                VerifyRelationships(world, panel, streaming, scratch);
                VerifyDuelReturn(app, world, menu, canvas.transform, streaming);
                Debug.Log("[VERIFY TAG FORCE] Scripted worlds, form schedules, four hearts, partner responses, gifts, saved progress, walking, BGM and a native duel round trip passed.");
                exit = 0;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            finally
            {
                world?.Close();
                DuelAudio.BgmVolume = music;
                DuelAudio.SfxVolume = effects;
                UnityEngine.Object.DestroyImmediate(canvas);
                if (Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }

        private static void VerifyAssets(string streaming)
        {
            CheckImage(TagForceData.PlayerSd, 512, 512, true);
            CheckImage(TagForceData.PlayerStanding, 900, 1400, true);
            CheckImage("TagForce/GX/Maps/spot_red_dorm.png", 1920, 1080, false);
            void CheckImage(string path, int width, int height, bool transparent)
            {
                var texture = new Texture2D(2, 2);
                try
                {
                    Require(texture.LoadImage(File.ReadAllBytes(Path.Combine(streaming, path))), path);
                    Require(texture.width == width && texture.height == height, $"Unexpected dimensions: {path}");
                    if (transparent) Require(texture.GetPixel(0, 0).a == 0 && texture.GetPixels32().Any(pixel => pixel.a == 255),
                        $"Missing usable transparency: {path}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        private static void VerifyFallback(string streaming, string scratch)
        {
            string directory = Path.Combine(scratch, "missing-world");
            Directory.CreateDirectory(directory);
            var dm = TagForceData.Series[0];
            Require(TagForceData.Load(directory, dm) == null, "Missing data did not fall back safely.");
            const string legacy = TagForceData.LegacyGxFile;
            File.Copy(Path.Combine(streaming, "TagForce", legacy), Path.Combine(directory, legacy));
            Require(TagForceData.Load(directory, TagForceData.Series[1])?.seriesId == TagForceData.GxId &&
                TagForceData.Load(directory, dm) == null, "A legacy GX file did not stay scoped to GX.");
            File.WriteAllText(Path.Combine(directory, "tagforce_dm.json"), "{ }");
            Require(TagForceData.Load(directory, dm) == null, "An incomplete script did not fail safely.");
        }

        private static void VerifyTravel(TagForceWorldUI world, Transform panel, string streaming)
        {
            int[] expected = { 42, 36, 39, 30, 41, 27 };
            string[] firstBread = { "bread_domino_croquette", "bread_golden_egg", "bread_poppo_sandwich", "bread_heartland_burger" };
            for (int i = 0; i < expected.Length; i++)
            {
                var series = TagForceData.Series[i];
                var data = TagForceData.Load(streaming, series);
                Require(data?.seriesId == series.Id && data.personas.Length == expected[i],
                    $"Bad roster for {series.Id}: expected {expected[i]}, loaded {data?.seriesId} with {data?.personas.Length}.");
                if (i < firstBread.Length)
                {
                    var bread = data.drawBreadCatalog?.breads.FirstOrDefault();
                    Require(bread?.Id == firstBread[i] && bread.baseAffinity > 0 && data.drawBreadCatalog.drawPriceDp > 0 &&
                        data.personas.All(persona => data.conversationTopics.All(topic =>
                            !string.IsNullOrWhiteSpace(persona.topicReplies.Reply(topic.topicId))) &&
                            !string.IsNullOrWhiteSpace(persona.giftReactions.Reply(true, bread)) &&
                            !string.IsNullOrWhiteSpace(persona.giftReactions.Reply(false, bread))),
                        $"Series {series.Id} did not load its own shop, topic replies or gift reactions.");
                }
                world.Open();
                Require(TagForceData.Series.All(entry => panel.Find(entry.AssetFolder) != null), "Incomplete series hub.");
                Press(panel, series.AssetFolder);
                CheckField(data.spots[0]);
                var seen = new HashSet<string>();
                for (int time = 0; time < TagForceData.TimeKeys.Length; time++)
                {
                    var save = CollectionProgress.GetTagForceProgress(series.Id).Copy();
                    save.time = time;
                    Require(CollectionProgress.TrySaveTagForce(series.Id, save, 0, out var error), error);
                    foreach (var spot in data.spots)
                    {
                        Press(world.Root.transform, "WorldNavigation/월드맵");
                        Press(panel, spot.spotId + "/Visit");
                        CheckField(spot);
                        foreach (var (persona, form) in data.Visitors(save, spot.spotId))
                        {
                            var figure = panel.Find(persona.personaId);
                            Require(figure?.Find("Talk") != null && form.Decks.Count > 0,
                                $"Missing spawn or duel deck for {persona.personaId} in {spot.spotId}.");
                            var face = figure.Find("Face");
                            Require(face.GetComponent<Image>()?.sprite != null ||
                                face.Find("Label")?.GetComponent<Text>().text == form.formName,
                                $"Missing art and the name fallback for {form.formName}.");
                            seen.Add(persona.personaId);
                        }
                    }
                }
                Require(seen.Count == expected[i], $"Expected {expected[i]} visitors in {series.Id}, spawned {seen.Count}.");
                Press(world.Root.transform, "WorldNavigation/시리즈 선택");
                Require(panel.Find(series.AssetFolder) != null, "The travel button did not return to the hub.");

                void CheckField(TagForceSpot spot)
                {
                    var player = panel.Find("Player/Sprite").GetComponent<Image>().sprite;
                    var scene = panel.Find("Scenery").GetComponent<Image>();
                    Require(player.texture.width == 512 &&
                        panel.Find("SpotName/Label").GetComponent<Text>().text == spot.name, $"Wrong field for {series.Id}.");
                    Require((scene.sprite == DuelUiKit.WhiteSprite) == !File.Exists(Path.Combine(streaming, spot.mapPath)) &&
                        scene.raycastTarget, $"Map or fallback is incorrect for {spot.spotId}.");
                }
            }
        }

        private static void VerifyMotionAndDialogue(TagForceWorldUI world, Transform panel, AudioSource bgm)
        {
            const string gx = TagForceData.GxId;
            var state = CollectionProgress.GetTagForceProgress(gx).Copy();
            state.spotId = "spot_red_dorm";
            state.time = 0;
            state.partnerId = "gx_asuka";
            Require(CollectionProgress.TrySaveTagForce(gx, state, 0, out var error), error);
            world.Open();
            Press(panel, "GX");
            var player = (RectTransform)panel.Find("Player");
            var partner = (RectTransform)panel.Find("Partner");
            var playerSprite = player.Find("Sprite");
            var partnerSprite = partner.Find("Sprite");
            float peak = Mathf.PI / 32f;
            Call(world, "MoveTo", new Vector2(.1f, .16f), null);
            Call(world, "AdvanceWalk", .5f, peak);
            Require(playerSprite.localScale.x == -1 && partnerSprite.localScale.x == -1 &&
                Mathf.Abs(player.anchoredPosition.y - 6f) < .01f && partner.anchoredPosition.y != 0,
                "Leftward movement did not face left and bob.");
            Call(world, "AdvanceWalk", 2f, peak);
            Require(player.anchoredPosition == Vector2.zero && partner.anchoredPosition == Vector2.zero,
                "Idle actors retained a bob offset.");
            Call(world, "MoveTo", new Vector2(.1f, .5f), null);
            Call(world, "AdvanceWalk", .5f, peak);
            Require(playerSprite.localScale.x == -1, "Vertical movement reset the last direction.");
            Call(world, "MoveTo", new Vector2(.9f, .5f), null);
            Call(world, "AdvanceWalk", .5f, peak);
            Require(playerSprite.localScale.x == 1 && partnerSprite.localScale.x == 1, "Moving right did not flip both sprites.");
            float x = player.anchorMin.x;
            world.Update(.1f, Vector2.right);
            Require(player.anchorMin.x > x, "Held controller direction did not move the player.");
            Vector2 releasedPosition = player.anchorMin;
            world.Update(.1f);
            Require(player.anchorMin == releasedPosition && player.anchoredPosition == Vector2.zero,
                "Releasing the stick left movement or bobbing active.");
            Require(bgm.clip.name == "tag_force_day" && bgm.loop, "The field did not select the daytime loop.");

            var data = TagForceData.Load(Application.streamingAssetsPath, TagForceData.Series[1]);
            var npc = data.Visitors(state, state.spotId).First();
            Press(panel, npc.Persona.personaId + "/Talk");
            Call(world, "AdvanceWalk", 4f, peak);
            var portrait = panel.Find("Protagonist").GetComponent<Image>().sprite;
            Require(portrait.texture.width == 900 && portrait.texture.height == 1400,
                "The dialogue did not use the shared heroine portrait.");
            var line = panel.Find("Dialogue/DialogueText/Label").GetComponent<Text>();
            Require(line.supportRichText && line.text.StartsWith("<color="), "The first dialogue frame should hide unrevealed letters.");
            world.Update(.2f);
            Require(!line.text.StartsWith("<color="), "Dialogue text did not advance.");
            world.Update(15f);
            Require(line.text == npc.Form.Greet(state.Relationship(npc.Persona.personaId).Hearts),
                "The full dialogue line did not become readable.");
        }

        private static void VerifySavedWorlds(TagForceWorldUI world, Transform panel, string scratch, AudioSource bgm)
        {
            int balance = CollectionProgress.Balance;
            world.Open();
            Press(panel, "DM");
            Call(world, "GoTo", "spot_domino_plaza", null, null);
            try
            {
                DuelAudio.SfxVolume = .2f;
                Press(panel, "Pickup1");
                Call(world, "AdvanceWalk", 4f, 0f);
                var reward = (AudioClip)typeof(DuelAudio).GetField("_clipWorldReward",
                    BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                Require(reward != null && reward.length > .3f, "A reward did not select the dedicated fanfare.");
            }
            finally
            {
                DuelAudio.SfxVolume = 0f;
            }
            Require(CollectionProgress.Balance == balance + 150 && panel.Find("Pickup1") == null,
                "A DP pickup did not pay out exactly once.");
            Press(world.Root.transform, "WorldNavigation/내 방");
            var night = bgm.clip;
            Require(night.name == "tag_force_night", "The saved night state did not switch music.");
            Press(panel, "Rest");
            Require(bgm.clip.name == "tag_force_day" && bgm.clip != night, "Resting through midnight did not switch music.");
            CollectionProgress.Load(scratch);
            var dm = CollectionProgress.GetTagForceProgress("dm");
            var gx = CollectionProgress.GetTagForceProgress("gx");
            Require(dm.time == 0 && dm.day == 2 && gx.time == 0 && gx.day == 1 &&
                dm.spotId == "spot_domino_plaza" &&
                gx.spotId == "spot_red_dorm" && gx.partnerId == "gx_asuka",
                "Per-world save or the legacy GX progress did not survive reload.");
            world.Close();
            world.Open(resume: true);
            Require(panel.Find("SpotName/Label").GetComponent<Text>().text == "도미노 플라자 중앙 광장",
                "Re-entering from a duel or editor did not restore the current world.");
        }

        private static void VerifyRelationships(TagForceWorldUI world, Transform panel, string streaming, string scratch)
        {
            var legacy = JsonUtility.FromJson<TagForceSave>("{\"relations\":[{\"personaId\":\"old\",\"affinity\":500}]}");
            legacy.Normalize();
            legacy.Normalize();
            Require(legacy.Relationship("old").affinity == 4000 && legacy.Relationship("old").Hearts == 4,
                "Legacy affinity did not migrate exactly once to four hearts.");

            var data = TagForceData.Load(streaming, TagForceData.Series[1]);
            var manager = new TagForceManager(data);
            var rival = data.Persona("gx_judai");
            var heroine = data.Persona("gx_asuka");
            var state = manager.Progress.Copy();
            var visitedForms = new HashSet<int>();
            for (int day = 1; day <= rival.forms.Length; day++)
            {
                state.day = day;
                for (int time = 0; time < TagForceData.TimeKeys.Length; time++)
                {
                    state.time = time;
                    if (rival.ActiveForm(state) is { } form) visitedForms.Add(form.formCharId);
                }
            }
            Require(visitedForms.SetEquals(rival.forms.Select(form => form.formCharId)),
                "A transformation with a shared visit schedule was never available.");
            state.day = 1;
            state.time = 0;
            state.partnerId = null;
            state.spotId = "spot_red_dorm";
            Require(manager.TrySave(state, 0, out var error), error);
            Require(manager.TrySetPartner(rival, out var rejection, out error) && rejection == rival.partnerRefusal &&
                manager.Progress.partnerId == null, "The rival accepted a partner request or used the wrong dialogue.");
            Require(manager.TrySetPartner(heroine, out var acceptance, out error) && !string.IsNullOrEmpty(acceptance) &&
                manager.Progress.partnerId == heroine.personaId, "The heroine did not accept a partner request.");

            var bread = data.drawBreadCatalog.breads[0];
            state = manager.Progress.Copy();
            state.ChangeItem(bread.Id, 1);
            state.AddAffinity(heroine.personaId, 3999);
            Require(manager.TrySave(state, 0, out error), error);
            Require(manager.TryGive(heroine, bread, 0, out var reaction, out _, out error) &&
                reaction == (bread.IsFavorite(heroine) ? heroine.giftReactions.favoriteLine : heroine.giftReactions.normalLine) &&
                manager.Progress.Count(bread.Id) == 0 && !manager.TryGive(heroine, bread, 0, out _, out _, out _),
                "The bread was not consumed exactly once or used the wrong reaction.");
            var topic = data.conversationTopics.First();
            Require(manager.TryTalk(rival, topic, out _, out _, out error), error);
            int affinity = manager.Progress.Relationship(rival.personaId).affinity;
            Require(manager.TryTalk(rival, topic, out _, out _, out error) && affinity > 0 &&
                manager.Progress.Relationship(rival.personaId).affinity == affinity, "The same conversation changed affinity twice.");

            CollectionProgress.Load(scratch);
            Require(manager.Progress.partnerId == heroine.personaId && manager.Progress.Relationship(heroine.personaId).Hearts == 4 &&
                manager.Progress.Relationship(heroine.personaId).affinity == 4000 && manager.Progress.Relationship(rival.personaId).affinity == affinity,
                "Affinity and current partner did not survive reload.");
            world.Open();
            Press(panel, "GX");
            Call(world, "Talk", heroine);
            Require(panel.Find("Dialogue/Hearts/Label").GetComponent<Text>().text.StartsWith("♥♥♥♥"), "The fourth heart did not render.");
            foreach (string label in new[] { "이야기하기", "샌드위치·선물 주기", "듀얼 신청", "파트너 해제", "대화 기록" })
                Require(panel.GetComponentsInChildren<Button>().Any(button => button.GetComponentInChildren<Text>()?.text == label),
                    $"Missing conversation action: {label}");
        }

        private static void VerifyDuelReturn(GameAppController app, TagForceWorldUI world, TitleAndMenuUI menu,
            Transform canvas, string streaming)
        {
            Require(YgoNativeBridge.Initialize(streaming, (_, message) => Debug.Log(message)) == 0,
                "Could not initialize the native duel engine.");
            CardArtLoader.Initialize(streaming);
            var modals = new DuelPromptModals(canvas, null, null);
            Set("_titleAndMenu", menu);
            Set("_tagForceWorld", world);
            Set("_archetypeCodex", new ArchetypeCodexUI(canvas, () => { }));
            Set("_tournament", new DimensionTournamentUI(app, canvas));
            Set("_ranked", new OfflineRankedUI(app, canvas));
            Set("_storyMode", new StoryModeUI(app, canvas));
            Set("_oppSelect", new OpponentSelectUI(app, canvas));
            Set("_deckEditor", new DeckEditorUI(app, canvas));
            Set("_modals", modals);
            var field = new DuelFieldUI(app, canvas, modals);
            Set("_duelField", field);
            Set("_initialized", true);
            try
            {
                app.ShowScreen(AppScreen.MainMenu);
                menu.MenuRoot.GetComponentsInChildren<Button>().Single(button => button.transform.parent.name == "TagForceWorld").onClick.Invoke();
                var panel = world.Root.transform.Find("World");
                Press(panel, "GX");
                var data = TagForceData.Load(streaming, TagForceData.Series[1]);
                var rival = data.Persona("gx_judai");
                string spotName = panel.Find("SpotName/Label").GetComponent<Text>().text;
                int before = CollectionProgress.Balance;
                StartFromDialogue();
                var duel = (DuelSession)Get("_session");
                Require(duel?.NativePtr != IntPtr.Zero && !world.Root.activeSelf && field.Root.activeSelf,
                    "The native duel did not start from the NPC dialogue.");
                duel.Surrender(duel.HumanPlayerIndex);
                typeof(GameAppController).GetMethod("Update", Private).Invoke(app, null);
                Require(Get("_session") == null && duel.NativePtr == IntPtr.Zero &&
                    world.Root.activeSelf && !field.Root.activeSelf && !modals.ResultRoot.activeSelf &&
                    panel.Find("SpotName/Label").GetComponent<Text>().text == spotName && CollectionProgress.Balance == before + 130,
                    "Surrender did not settle into the same world map with the single loss reward.");
                StartFromDialogue();
                duel = (DuelSession)Get("_session");
                duel.Surrender(duel.AiPlayerIndex);
                typeof(GameAppController).GetMethod("Update", Private).Invoke(app, null);
                Require(Get("_session") == null && world.Root.activeSelf && CollectionProgress.Balance == before + 430 &&
                    panel.Find("SpotName/Label").GetComponent<Text>().text == spotName,
                    "Winning did not settle into the same world map with the single win reward.");

                void StartFromDialogue()
                {
                    Call(world, "Talk", rival);
                    panel.GetComponentsInChildren<Button>().Single(button => button.GetComponentInChildren<Text>()?.text == "듀얼 신청").onClick.Invoke();
                    Press(panel, "Start");
                }
            }
            finally
            {
                field.UnbindSession();
                (Get("_session") as DuelSession)?.Dispose();
                Set("_session", null);
                CardArtLoader.Shutdown();
                YgoNativeBridge.Shutdown();
            }

            void Set(string name, object value) => typeof(GameAppController).GetField(name, Private).SetValue(app, value);
            object Get(string name) => typeof(GameAppController).GetField(name, Private).GetValue(app);
        }

        private static void Press(Transform scope, string path)
        {
            var button = scope.Find(path)?.GetComponent<Button>();
            Require(button != null && button.interactable, $"Missing button: {path}");
            button.onClick.Invoke();
        }

        private static void Call(TagForceWorldUI world, string name, params object[] arguments) =>
            typeof(TagForceWorldUI).GetMethod(name, Private).Invoke(world, arguments);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
