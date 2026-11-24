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
                Require(GameAppController.TagForceEnabled, "The main menu must expose the world.");
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
                Debug.Log("[VERIFY TAG FORCE] Six worlds, 233 NPCs, common heroine, fallback assets, walking, dialogue, BGM and save isolation passed.");
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
            CheckImage(TagForceWorldData.PlayerSd, 512, 512, true);
            CheckImage(TagForceWorldData.PlayerStanding, 900, 1400, true);
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
            var dm = TagForceWorldData.Series[0];
            Require(TagForceWorldData.Load(directory, dm) == null, "Missing data did not fall back safely.");
            const string legacy = TagForceWorldData.LegacyGxFile;
            File.Copy(Path.Combine(streaming, legacy), Path.Combine(directory, legacy));
            Require(TagForceWorldData.Load(directory, dm)?.seriesId == TagForceWorldData.GxId,
                "Missing series file did not resolve to GX.");
            File.WriteAllText(Path.Combine(directory, "tagforce_dm.json"), "{ }");
            Require(TagForceWorldData.Load(directory, dm)?.seriesId == TagForceWorldData.GxId,
                "An incomplete series file did not resolve to GX.");
        }

        private static void VerifyTravel(TagForceWorldUI world, Transform panel, string streaming)
        {
            int[] expected = { 46, 36, 46, 37, 41, 27 };
            for (int i = 0; i < expected.Length; i++)
            {
                var series = TagForceWorldData.Series[i];
                var data = TagForceWorldData.Load(streaming, series);
                Require(data?.seriesId == series.Id && data.personas.Length == expected[i],
                    $"Bad roster for {series.Id}: expected {expected[i]}, loaded {data?.seriesId} with {data?.personas.Length}.");
                world.Open();
                Require(TagForceWorldData.Series.All(entry => panel.Find(entry.AssetFolder) != null), "Incomplete series hub.");
                Press(panel, series.AssetFolder);
                CheckField(data.spots[0]);
                var seen = new HashSet<string>();
                for (int time = 0; time < TagForceWorldData.TimeKeys.Length; time++)
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
                            if (i <= 3)
                                Require(File.Exists(Path.Combine(streaming, form.sdSpritePath)) &&
                                    File.Exists(Path.Combine(streaming, form.standingSpritePath)), $"Missing prepared art: {form.formName}.");
                            if (!File.Exists(Path.Combine(streaming, form.sdSpritePath)))
                                Require(figure.Find("Face/Label")?.GetComponent<Text>().text == form.formName,
                                    $"Missing name fallback for {form.formName}.");
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
            const string gx = TagForceWorldData.GxId;
            var state = CollectionProgress.GetTagForceProgress(gx).Copy();
            state.spotId = "spot_red_dorm";
            state.time = 0;
            state.partnerId = "gx_judai";
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
                Mathf.Abs(player.anchoredPosition.y - 5f) < .01f && partner.anchoredPosition.y != 0,
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

            var data = TagForceWorldData.Load(Application.streamingAssetsPath, TagForceWorldData.Series[1]);
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
            try
            {
                DuelAudio.SfxVolume = .2f;
                Press(panel, "Pickup0");
                Call(world, "AdvanceWalk", 4f, 0f);
                var reward = (AudioClip)typeof(DuelAudio).GetField("_clipWorldReward",
                    BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                Require(reward != null && reward.length > .3f, "A reward did not select the dedicated fanfare.");
            }
            finally
            {
                DuelAudio.SfxVolume = 0f;
            }
            Require(CollectionProgress.Balance == balance + 150 && panel.Find("Pickup0") == null,
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
                dm.spotId == "spot_museum" &&
                gx.spotId == "spot_red_dorm" && gx.partnerId == "gx_judai",
                "Per-world save or the legacy GX progress did not survive reload.");
            world.Close();
            world.Open(resume: true);
            Require(panel.Find("SpotName/Label").GetComponent<Text>().text == "도미노 박물관",
                "Re-entering from a duel or editor did not restore the current world.");
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
