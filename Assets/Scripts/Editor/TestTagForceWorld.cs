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
    internal static class TestTagForceWorld
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            int exit = 1;
            var scratch = Path.Combine(Path.GetTempPath(), "ygo-tag-force-" + Guid.NewGuid().ToString("N"));
            var canvas = new GameObject("Tag Force world verification", typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
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
                var entries = menu.MenuRoot.GetComponentsInChildren<Button>(true);
                Require(entries.Any(button => button.transform.parent.name == "RpgWorld") &&
                    entries.All(button => button.transform.parent.name != "TagForceWorld"),
                    "The main menu did not offer RPG World in place of the old Tag Force entry.");
                world = new TagForceWorldUI(app, canvas.transform);
                var panel = world.Root.transform.Find("World");

                VerifyAssets(streaming);
                VerifyAudio(streaming);
                VerifyFallback(streaming, scratch);
                VerifyTravel(world, panel, streaming);
                VerifyBreadRoulette(world, panel);
                VerifyMotionAndDialogue(world, panel, bgm);
                VerifySavedWorlds(world, panel, scratch, bgm);
                VerifyRelationships(world, panel, streaming, scratch);
                VerifyDuelReturn(app, world, menu, canvas.transform, streaming);
                Debug.Log("[VERIFY TAG FORCE] Four worlds, shifts, 8 Hz walking, portraits, hearts, gifts, partners, bundled audio, sandwich roulette, saves and two native duel round trips passed.");
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
            foreach (var series in TagForceData.Series.Take(4))
            {
                var data = TagForceData.Load(streaming, series);
                foreach (var hub in data.Hubs)
                    Require(File.Exists(Path.Combine(streaming, hub.mapPath)), $"Missing hub artwork: {hub.mapPath}");
                foreach (var form in data.personas.SelectMany(persona => persona.forms))
                {
                    Require(File.Exists(Path.Combine(streaming, form.sdSpritePath)) &&
                        File.Exists(Path.Combine(streaming, form.standingSpritePath)), $"Missing SD or standing art for {form.formName}.");
                }
            }
            foreach (string hub in new[] { "GX/Maps/spot_red_dorm.png", "5DS/Maps/spot_daedalus_bridge.png",
                "ZEXAL/Maps/spot_yuma_house.png", "ZEXAL/Maps/spot_waterfront_pier.png" })
                CheckImage("TagForce/" + hub, 1920, 1080, false);
            foreach (int character in new[] { 9, 208 })
            {
                CheckImage($"TagForce/5DS/Characters_SD/sd_{character}.png", 512, 512, true);
                CheckImage($"TagForce/5DS/Characters_Standing/full_{character}.png", 900, 1400, true);
            }
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

        private static void VerifyAudio(string streaming)
        {
            const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Static;
            var duel = new Dictionary<string, string>
            {
                ["_clipClick"] = "Click", ["_clipDraw"] = "Draw", ["_clipSummon"] = "Summon",
                ["_clipSpecialSummon"] = "SpecialSummon", ["_clipAttack"] = "Attack", ["_clipDirectAttack"] = "DirectAttack",
                ["_clipDamage"] = "Damage", ["_clipChain"] = "Chain", ["_clipActivate"] = "Activate",
                ["_clipPhase"] = "Phase", ["_clipWin"] = "Win", ["_clipCoin"] = "Coin", ["_clipDice"] = "Dice",
                ["_clipSet"] = "Set", ["_clipTick"] = "DialogueBlip", ["_clipShuffle"] = "Shuffle",
            };
            foreach (var (field, name) in duel)
                CheckClip((AudioClip)typeof(DuelAudio).GetField(field, fields).GetValue(null), name);
            var world = new[] { "button_confirm", "button_cancel", "cursor_move", "dialogue_blip", "heart_up", "heart_down",
                "card_draw", "duel_encounter", "sandwich_roulette", "sandwich_win", "partner_accept" };
            var cues = (TagForceSfx[])Enum.GetValues(typeof(TagForceSfx));
            Require(cues.Length == world.Length, "Not all Tag Force SFX mappings are tested.");
            for (int i = 0; i < world.Length; i++)
            {
                var clip = DuelAudio.GetTagForceClip(cues[i]);
                CheckClip(clip, "sfx_" + world[i]);
                Require(ReferenceEquals(clip, DuelAudio.GetTagForceClip(cues[i])), $"No cached clip for {cues[i]}.");
            }
            Require(Directory.GetFiles(Path.Combine(streaming, "TagForce", "Audio"), "*.wav").Length == 33,
                "The supplied WAV set is incomplete.");
            Require(DuelVisualAssets.LoadSound("missing-tag-force-fixture") == null, "Missing sound did not fall back safely.");

            void CheckClip(AudioClip clip, string name)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(streaming, "TagForce", "Audio", name + ".wav"));
                int channels = BitConverter.ToUInt16(bytes, 22);
                int frames = BitConverter.ToInt32(bytes, 40) / (channels * 2);
                Require(clip != null && clip.name == name && clip.channels == channels &&
                    clip.frequency == BitConverter.ToInt32(bytes, 24) && clip.samples == frames,
                    $"Sound {name} did not prefer the packaged WAV.");
                // Compare a section of the decoded waveform, independently of the audio device.
                int frameOffset = frames / 4;
                var sample = new float[Math.Min(128, (frames - frameOffset) * channels)];
                Require(clip.GetData(sample, frameOffset), $"Could not read decoded samples for {name}.");
                for (int index = 0; index < sample.Length; index++)
                    Require(Mathf.Abs(sample[index] - BitConverter.ToInt16(bytes, 44 + (frameOffset * channels + index) * 2) /
                        32768f) < .0001f, $"Decoded sample differs for {name}.");
            }
        }

        private static void VerifyBreadRoulette(TagForceWorldUI world, Transform panel)
        {
            var data = TagForceData.Load(Application.streamingAssetsPath, TagForceData.Series[1]);
            int balance = CollectionProgress.Balance;
            world.Open();
            Press(panel, "GX");
            Press(world.Root.transform, "WorldNavigation/매점");
            Press(panel, "Bag0");
            var caption = panel.Find("BreadRoulette/Prize/Caption/Label")?.GetComponent<Text>();
            var saved = CollectionProgress.GetTagForceProgress(data.seriesId);
            Require(caption != null && saved.bags.SequenceEqual(new[] { 0 }) && saved.items.Count == 1 &&
                CollectionProgress.Balance == balance - data.drawBreadCatalog.drawPriceDp,
                "The selected sandwich prize and cost were not saved atomically before the roulette.");
            string initial = caption.text;
            var blocked = panel.Find("Bag1").GetComponent<Button>();
            Require(!blocked.IsInteractable(), "A second bag could be selected during the roulette.");
            blocked.onClick.Invoke();
            world.Update(.22f);
            Require(caption.text != initial, "The roulette did not cycle through sandwiches.");
            world.Update(.63f);
            Require(caption.text.Contains(data.Item(saved.items[0].id).name, StringComparison.Ordinal) &&
                caption.text.Contains("획득!", StringComparison.Ordinal), "The roulette did not reveal the saved prize.");
            world.Update(1f);
            Require(panel.Find("BreadRoulette") == null && !panel.Find("Bag0").GetComponent<Button>().IsInteractable() &&
                panel.Find("Bag1").GetComponent<Button>().IsInteractable() && saved.bags.Count == 1,
                "The purchased bag reopened or another bag was charged.");
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
            string[][] hubIds =
            {
                new[] { "spot_domino_plaza", "spot_kame_game", "spot_kaiba_corp" },
                new[] { "spot_red_dorm", "spot_main_academy", "spot_blue_arena" },
                new[] { "spot_satellite_hideout", "spot_daedalus_bridge", "spot_poppo_time" },
                new[] { "spot_heartland_city", "spot_yuma_house", "spot_waterfront_pier" },
            };
            Require(TagForceData.TimeKeys.SequenceEqual(new[] { "morning", "day", "evening" }), "Unexpected time slots.");
            for (int i = 0; i < expected.Length; i++)
            {
                var series = TagForceData.Series[i];
                var data = TagForceData.Load(streaming, series);
                Require(data?.seriesId == series.Id && data.personas.Length == expected[i],
                    $"Bad roster for {series.Id}: expected {expected[i]}, loaded {data?.seriesId} with {data?.personas.Length}.");
                bool scripted = i < firstBread.Length;
                if (scripted)
                {
                    Require(data.Hubs.Select(hub => hub.spotId).SequenceEqual(hubIds[i]), $"Wrong hubs for {series.Id}.");
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
                CheckField(data.Hubs[0]);
                var seen = new HashSet<string>();
                for (int time = 0; time < TagForceData.TimeKeys.Length; time++)
                {
                    var save = CollectionProgress.GetTagForceProgress(series.Id).Copy();
                    save.time = time;
                    Require(CollectionProgress.TrySaveTagForce(series.Id, save, 0, out var error), error);
                    foreach (var spot in data.Hubs)
                    {
                        Press(world.Root.transform, "WorldNavigation/월드맵");
                        Require(panel.GetComponentsInChildren<Button>().Count(button => button.name == "Visit") == data.Hubs.Length,
                            $"Unexpected selectable maps for {series.Id}.");
                        Press(panel, spot.spotId + "/Visit");
                        CheckField(spot);
                        var visitors = data.Visitors(save, spot.spotId).ToArray();
                        int screens = panel.GetComponentsInChildren<Button>().Count(button => button.name == "Face");
                        Require(screens == visitors.Length && screens <= 5 && (!scripted || screens >= 3),
                            $"{series.Id}/{spot.spotId} in shift {time} spawned {screens} visitors.");
                        if (scripted)
                        {
                            var scheduled = data.personas.Where(persona => persona.ActiveForm(save)?.spots.Contains(spot.spotId) == true);
                            Require(visitors.Select(visitor => visitor.Persona).SequenceEqual(scheduled),
                                $"Rendered roster differs from the script for {series.Id}/{spot.spotId}, shift {time}.");
                        }
                        foreach (var (persona, form) in visitors)
                        {
                            var figure = panel.Find(persona.personaId);
                            var face = figure?.Find("Face");
                            Require(face?.GetComponent<Button>() != null && form.Decks.Count > 0,
                                $"Missing selectable spawn or duel deck for {persona.personaId} in {spot.spotId}.");
                            Require(scripted ? face.GetComponent<Image>().sprite != DuelUiKit.WhiteSprite :
                                face.GetComponent<Image>().sprite != DuelUiKit.WhiteSprite || face.Find("Label")?.GetComponent<Text>().text == form.formName,
                                $"Missing SD art for {form.formName}.");
                            seen.Add(persona.personaId);
                        }
                        if (scripted)
                        {
                            var accompanied = save.Copy();
                            foreach (var partner in data.personas.Where(persona => persona.CanPartner))
                            {
                                accompanied.partnerId = partner.personaId;
                                var withPartner = data.Visitors(accompanied, spot.spotId).ToArray();
                                Require(withPartner.Length is >= 2 and <= 4 &&
                                    withPartner.All(visitor => visitor.Persona != partner),
                                    $"Partner caused duplicate or out-of-range map population in {spot.spotId}.");
                            }
                        }
                    }
                }
                if (scripted)
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
            float quarterStep = 1f / (4f * 8f);
            float peak = quarterStep;
            Call(world, "MoveTo", new Vector2(.1f, .16f), null);
            Call(world, "AdvanceWalk", .5f, peak);
            Require(playerSprite.localScale.x == -1 && partnerSprite.localScale.x == -1 &&
                Mathf.Abs(player.anchoredPosition.y - 6f) < .01f && partner.anchoredPosition.y != 0,
                "Leftward movement did not face left and bob.");
            Call(world, "AdvanceWalk", .1f, quarterStep * 3f);
            Require(Mathf.Abs(player.anchoredPosition.y + 6f) < .01f, "Walking bob half-cycle did not match 8 Hz.");
            Call(world, "AdvanceWalk", .1f, quarterStep * 5f);
            Require(Mathf.Abs(player.anchoredPosition.y - 6f) < .01f, "Walking bob full cycle did not match 8 Hz.");
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
            Require(panel.Find("gx_asuka") == null && panel.Find("Partner") != null, "The partner was spawned twice.");
            var visitor = (RectTransform)panel.Find(npc.Persona.personaId);
            var anchor = visitor.anchorMin;
            Call(world, "AdvanceVisitors", quarterStep);
            Require(visitor.anchorMin != anchor && visitor.anchoredPosition.y != 0, "SD visitor did not walk and bob.");
            var sdSprite = visitor.Find("Face");
            Require(sdSprite.GetComponent<Image>().raycastTarget, "The SD portrait could not receive a pointer click.");
            var click = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            Require(ExecuteEvents.Execute(sdSprite.gameObject, click, ExecuteEvents.pointerClickHandler),
                "No pointer-click handler was attached to the SD portrait.");
            Call(world, "AdvanceWalk", 4f, peak);
            var left = (RectTransform)panel.Find("Conversation/Protagonist");
            var right = (RectTransform)panel.Find("Conversation/Guest");
            var portrait = left.GetComponent<Image>().sprite;
            var guest = right.GetComponent<Image>().sprite;
            var bottom = (RectTransform)panel.Find("Conversation/Dialogue");
            var card = bottom.GetComponent<CanvasGroup>();
            Require(portrait.texture.width == 900 && portrait.texture.height == 1400 &&
                guest != DuelUiKit.WhiteSprite && guest.texture.height > guest.texture.width,
                "The conversation did not use both high-resolution standing portraits.");
            Require(bottom.anchorMax.y < .5f && left.anchoredPosition.x < 0 && right.anchoredPosition.x > 0 && card.alpha == 0f &&
                !world.CanWalk && !panel.Find(npc.Persona.personaId + "/Face").GetComponent<Button>().IsInteractable(),
                "Conversation did not begin with both standing portraits offscreen and field input suppressed.");
            var line = bottom.Find("DialogueText/Label").GetComponent<Text>();
            Require(line.supportRichText && line.text.StartsWith("<color="), "The first dialogue frame should hide unrevealed letters.");
            var slideStart = right.anchoredPosition.x;
            world.Update(.12f);
            Require(right.anchoredPosition.x > 0 && right.anchoredPosition.x < slideStart && !card.interactable,
                "Standing portraits did not visibly slide toward the scene during the transition.");
            world.Update(.24f);
            Require(left.anchoredPosition == Vector2.zero && right.anchoredPosition == Vector2.zero &&
                card.alpha == 1f && card.interactable && !line.text.StartsWith("<color="),
                "Standing portraits and the interactive dialogue card did not finish opening.");
            world.Update(15f);
            Require(line.text == npc.Form.Greet(state.Relationship(npc.Persona.personaId).Hearts),
                "The full dialogue line did not become readable.");
            var visitPosition = panel.Find("Player").GetComponent<RectTransform>().anchorMin;
            world.Back();
            world.Update(.11f);
            Require(right.anchoredPosition.x > 0 && !card.interactable, "Closing did not slide the portrait out.");
            world.Update(.12f);
            Require(panel.Find("Conversation") == null && world.CanWalk &&
                panel.Find("Player").GetComponent<RectTransform>().anchorMin == visitPosition &&
                panel.Find(npc.Persona.personaId + "/Face").GetComponent<Button>().IsInteractable(),
                "Closing dialogue did not restore the same SD exploration position and controls.");
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
            Require(night.name == "tag_force_night", "The saved evening state did not switch music.");
            Press(panel, "Rest");
            Require(bgm.clip.name == "tag_force_day" && bgm.clip != night, "Resting after evening did not start the next morning.");
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
            var legacy = JsonUtility.FromJson<TagForceSave>("{\"time\":3,\"relations\":[{\"personaId\":\"old\",\"affinity\":500}]}");
            legacy.Normalize();
            legacy.Normalize();
            Require(legacy.Relationship("old").affinity == 4000 && legacy.Relationship("old").Hearts == 4 && legacy.time == 2,
                "Legacy affinity and night-shift progress did not migrate correctly.");
            legacy.Advance();
            Require(legacy.time == 0 && legacy.day == 2, "The three shifts did not roll over to morning.");

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
            state.ChangeItem(bread.Id, 1 - state.Count(bread.Id));
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
            Require(panel.Find("Conversation/Dialogue/Hearts/Label").GetComponent<Text>().text.StartsWith("♥♥♥♥"), "The fourth heart did not render.");
            Require(panel.Find("Conversation/Dialogue/DialogueActions/Items").GetComponentsInChildren<Image>()
                .Count(icon => icon.name == "ActionIcon" && icon.sprite != null && !icon.raycastTarget) == 4,
                "Not all four dialogue action icons were shown.");
            foreach (string label in new[] { "이야기하기", "샌드위치·선물 주기", "듀얼 신청", "파트너 해제", "대화 기록" })
                Require(panel.GetComponentsInChildren<Button>().Any(button => button.GetComponentInChildren<Text>()?.text == label),
                    $"Missing conversation action: {label}");

            var stagedGift = manager.Progress.Copy();
            int priorBreadCount = stagedGift.Count(bread.Id);
            stagedGift.ChangeItem(bread.Id, 1);
            Require(manager.TrySave(stagedGift, 0, out error), error);
            world.Update(.35f);
            PressLabel("이야기하기");
            PressLabel(topic.name);
            world.Update(10f);
            var reply = panel.Find("Conversation/Dialogue/DialogueText/Label").GetComponent<Text>();
            Require(reply.text.EndsWith(heroine.topicReplies.Reply(topic.topicId)), "The talk action did not display the selected topic reply.");
            PressLabel("샌드위치·선물 주기");
            PressLabel(bread.name + " · ", prefix: true);
            world.Update(10f);
            reply = panel.Find("Conversation/Dialogue/DialogueText/Label").GetComponent<Text>();
            Require(manager.Progress.Count(bread.Id) == priorBreadCount && reply.text == heroine.giftReactions.Reply(bread.IsFavorite(heroine), bread),
                "The gift action did not consume the inventory item and show its response.");
            PressLabel("파트너 해제");
            Require(manager.Progress.partnerId == null, "The partner action did not end the partnership.");
            PressLabel("파트너 신청");
            Require(manager.Progress.partnerId == heroine.personaId, "The partner action did not restore the partnership.");

            void PressLabel(string label, bool prefix = false)
            {
                var button = panel.GetComponentsInChildren<Button>().FirstOrDefault(entry =>
                {
                    string value = entry.GetComponentInChildren<Text>()?.text;
                    return entry.IsInteractable() && (prefix ? value?.StartsWith(label, StringComparison.Ordinal) == true : value == label);
                });
                Require(button != null, $"Missing active action: {label}");
                button.onClick.Invoke();
            }
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
                app.ShowScreen(AppScreen.TagForceWorld);
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
                    world.Update(.35f);
                    var request = panel.GetComponentsInChildren<Button>().Single(button => button.GetComponentInChildren<Text>()?.text == "듀얼 신청");
                    Require(request.IsInteractable(), "The duel action was blocked after dialogue opened.");
                    request.onClick.Invoke();
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
