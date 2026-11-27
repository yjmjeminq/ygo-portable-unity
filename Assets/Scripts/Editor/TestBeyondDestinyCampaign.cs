using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static class TestBeyondDestinyCampaign
    {
        public static void RunHeadless()
        {
            int exitCode = 1;
            try
            {
                var campaign = BeyondDestinyCampaign.Load(Application.streamingAssetsPath);
                VerifyScriptAndEncounters(campaign);
                VerifyAudio(campaign);
                VerifyPortraits(campaign);
                VerifyIndependentProgress(campaign);
                VerifyPlayback(campaign);
                VerifyNativeDuelOpenings(campaign);
                VerifyWalkIn();
                Debug.Log("[Beyond Destiny] All 37 episodes and 182 scenes, 15 native duel openings, riding SPC, and field entry passed.");
                exitCode = 0;
            }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { if (Application.isBatchMode) EditorApplication.Exit(exitCode); }
        }

        private static void VerifyScriptAndEncounters(BeyondDestinyCampaign campaign)
        {
            var scenes = campaign.Episodes.SelectMany(episode => episode.scenes).ToArray();
            var encounters = scenes.SelectMany(scene =>
                scene.events.Select(cue => cue.line).Prepend(scene.line)
                    .Select(line => campaign.EncounterAt(scene.id, line))).Where(encounter => encounter != null).ToArray();
            Require(campaign.Episodes.Count == 37 && scenes.Length == 182 && encounters.Length == 21,
                "The complete manuscript or an encounter anchor is missing.");
            var cues = scenes.SelectMany(scene => scene.events).ToLookup(cue => cue.kind);
            Require(cues["dialogue"].Count() == 1128 && cues["thought"].Count() == 52 &&
                cues["narration"].Count() == 512 && cues["system"].Count() == 91,
                "A dialogue, thought, narration or system cue was dropped.");
            Require(cues["standing"].Any(cue => cue.cast?.Length > 1) &&
                cues["music"].Any(cue => cue.clipKeys?.Contains("BGM_Dark_Alley_Loop") == true),
                "The Unity loader did not deserialize cast or music cues.");
            string assetFolder = Path.Combine(Application.streamingAssetsPath, "TagForce", BeyondDestinyCampaign.AssetFolder);
            bool Exists(string path) => !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(assetFolder, path));
            Require(scenes[0].backgroundPath == "Maps/spot_neodomino_rain_alley.png" &&
                scenes.All(scene => Exists(scene.backgroundPath)) && cues["background"].Count() == 153 &&
                cues["background"].All(cue => Exists(cue.assetPath)),
                "A manuscript background could not be loaded from the campaign art folder.");
            var cutscenes = scenes.SelectMany(scene => scene.events).Where(cue => cue.cutsceneKey != null).ToArray();
            Require(cutscenes.All(cue => cue.IsReadable && Exists($"Cutscenes/{cue.cutsceneKey}.png")) &&
                cutscenes.Select(cue => cue.cutsceneKey).Distinct().Count() == 10 &&
                cutscenes.All(cue => cue.cutsceneKey != "ep02_bandage" || cue.line is >= 136 and <= 153),
                "A CG is missing or appears before its manuscript beat.");
            var boss = encounters.Single(encounter => encounter.episodeId == "beyond_destiny_ep_31");
            Require(boss.Playable && boss.Riding && boss.resolution == "canonicalOpponentWin" &&
                campaign.DeckById(boss.opponentDeckId).sigCard == 73580471 &&
                campaign.DeckById(boss.playerDeckId).main.Length == 40,
                "Episode 31 must preserve its original result and use the correct riding decks.");
        }

        private static void VerifyPortraits(BeyondDestinyCampaign campaign)
        {
            var scenes = campaign.Episodes.SelectMany(episode => episode.scenes).ToArray();
            var pages = scenes.SelectMany(scene => scene.events).Where(cue => cue.IsReadable).ToArray();
            string root = Path.Combine(Application.streamingAssetsPath, "TagForce", BeyondDestinyCampaign.AssetFolder);
            Require(pages.All(cue => cue.heroPortrait != null && cue.speakerSide is >= 0 and <= 3 &&
                ((cue.speakerSide & 2) == 0 || cue.guestPortrait != null)),
                "A page has no left portrait or assigns an empty speaker lane.");
            var paths = pages.SelectMany(cue => new[] { cue.heroPortrait, cue.guestPortrait })
                .Where(path => path != null).Distinct().ToArray();
            Require(paths.Length >= 49 && paths.All(path => File.Exists(Path.Combine(root, path + ".png"))),
                "A named cast or portrait variant points at missing artwork.");

            BeyondDestinyCampaign.Cue At(int episode, int scene, int line) =>
                campaign.Episodes[episode - 1].scenes[scene - 1].events.Single(cue => cue.line == line);
            Require(At(2, 3, 91).guestPortrait == "Characters_Standing/aki_distress" &&
                At(20, 1, 15).guestPortrait == "Characters_Standing/rex_fervor", "Early distress or the narrated opponent lost their painted pose.");
            Require(At(3, 3, 156).guestPortrait == null && At(3, 3, 156).speakerSide == 0,
                "An off-camera intercepted voice incorrectly put its speaker in the room.");
            var rooftop = At(32, 4, 146);
            Require(rooftop.heroPortrait == "Characters_Standing/sherry" && rooftop.guestPortrait == "Characters_Standing/zone_distant" &&
                rooftop.speakerSide == 2, "The rooftop cutaway must stage Sherry and Z-ONE as the active cast.");
            var both = At(36, 1, 56);
            Require(both.heroPortrait == "Characters_Standing/aki_riding" && both.guestPortrait == "Characters_Standing/sherry" &&
                both.speakerSide == 3, "The synchronized dialogue must highlight both characters.");
        }

        private static void VerifyAudio(BeyondDestinyCampaign campaign)
        {
            var all = campaign.Episodes.SelectMany(episode => episode.scenes).SelectMany(scene => scene.events).ToArray();
            var music = all.Where(cue => cue.kind == "music").ToArray();
            var sounds = all.Where(cue => cue.kind == "sound").ToArray();
            Require(music.Length == 208 && sounds.Length == 412 && all.Count(cue => cue.kind == "audio") == 10 &&
                music.All(cue => cue.action is "fadeOut" or "stop" || cue.assetPath != null) &&
                sounds.All(cue => cue.action == "silence" || cue.assetPath != null),
                "The authored audio directions must be mapped to playable files or an explicit stop.");
            var paths = all.Where(cue => cue.kind is "sound" or "music" or "audio")
                .SelectMany(cue => new[] { cue.assetPath, cue.audioAccent }).Where(path => path != null).Distinct().ToArray();
            Require(paths.Length == 57, "The assembled score did not contain its entire audio library.");
            string art = Path.Combine(Application.streamingAssetsPath, "TagForce", BeyondDestinyCampaign.AssetFolder);
            foreach (string path in paths)
            {
                var clip = DuelVisualAssets.ReadSound(Path.Combine(art, path), path);
                Require(clip != null && clip.channels == 2 && clip.frequency == 22050 && clip.samples > 1000,
                    $"The shipped Unity audio reader could not decode {path}.");
                var lead = new float[2048];
                clip.GetData(lead, Math.Min(clip.samples - 1024, clip.frequency / 5));
                Require(lead.All(sample => float.IsFinite(sample) && Math.Abs(sample) < .8f), $"Invalid audio samples in {path}.");
                UnityEngine.Object.DestroyImmediate(clip);
            }

            var opening = new BeyondDestinySoundscape(null, null);
            foreach (var cue in campaign.Episodes[0].scenes[0].events.TakeWhile(cue => !cue.IsReadable))
                opening = opening.After(cue);
            Require(opening.Music == "Audio/Music/rain_signal.wav" && opening.Ambience == "Audio/Ambience/storm.wav",
                "The opening must mix the original rain theme with the storm bed.");
            var quietRain = all.Single(cue => cue.kind == "audio" && cue.assetPath != null);
            var after = opening.After(quietRain);
            Require(after.Music == null && after.Ambience == "Audio/Ambience/rain.wav" && quietRain.action == "fadeOut",
                "The manuscript's rain-only transition must fade out music and keep rain audible.");
            Require(music.Any(cue => cue.action == "crossfade" && cue.assetPath == "Audio/Music/infiltration.wav") &&
                music.Any(cue => cue.action == "cut" && cue.assetPath == "Audio/Music/infiltration.wav") &&
                music.Any(cue => cue.action == "crossfade" && cue.assetPath == "Audio/Music/arcadia_low.wav"),
                "Crossfade, hard cut or the half-step direction was lost.");
        }

        private static void VerifyIndependentProgress(BeyondDestinyCampaign campaign)
        {
            string root = Path.Combine(Path.GetTempPath(), "beyond-destiny-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string originalGx = Path.Combine(root, CollectionProgress.ProfileFileName);
            byte[] gxBytes = { 0xef, 0xbb, 0xbf, 0x7b, 0x7d, 0x0a };
            File.WriteAllBytes(originalGx, gxBytes);
            try
            {
                var progress = BeyondDestinyProgress.Load(root, campaign);
                Require(progress.episodeIndex == 0 && progress.sceneIndex == 0 && progress.cueIndex == 0,
                    "New 5D's saves must start at the beginning.");
                int firstEpisodeCues = campaign.Episodes[0].scenes.Sum(scene => scene.events.Length);
                for (int i = 0; i < firstEpisodeCues; ++i) Require(progress.Advance(campaign), "Unexpected campaign ending.");
                progress.unlockedLocations.Add("spot_kaiba_dome_plaza");
                progress.unlockedDecks.Add(75001);
                progress.earnedRewards.Add("episode_01_clear");
                progress.Save(root, campaign);
                var reopened = BeyondDestinyProgress.Load(root, campaign);
                Require(reopened.episodeIndex == 1 && reopened.sceneIndex == 0 && reopened.cueIndex == 0 &&
                    reopened.unlockedLocations.SequenceEqual(progress.unlockedLocations) &&
                    reopened.unlockedDecks.SequenceEqual(progress.unlockedDecks) &&
                    reopened.earnedRewards.SequenceEqual(progress.earnedRewards),
                    "The next episode or its campaign-specific unlocks were lost.");
                while (reopened.Advance(campaign)) { }
                reopened.Save(root, campaign);
                var ending = BeyondDestinyProgress.Load(root, campaign);
                Require(ending.completed && ending.episodeIndex == 36 && !ending.Advance(campaign) &&
                    File.ReadAllBytes(originalGx).SequenceEqual(gxBytes),
                    "Campaign completion failed to persist or touched the original profile.");
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        private static void Require(bool result, string message)
        {
            if (!result) throw new InvalidOperationException(message);
        }

        private static void VerifyPlayback(BeyondDestinyCampaign campaign)
        {
            string root = Path.Combine(Path.GetTempPath(), "beyond-destiny-playback-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var playback = new BeyondDestinyPlayback(campaign, root);
                var opening = playback.Read();
                Require(opening.Pages.Length > 2 && opening.Pages[0].Map == "Maps/spot_neodomino_rain_alley.png" &&
                    opening.Pages[0].Audio.Any(cue => cue.clipKeys?.Contains("BGM_Dark_Alley_Loop") == true),
                    "The opening must pass its painted background and original music cue to the dialogue.");
                string savedAudio = Path.Combine(root, "soundscape");
                var beforeReload = new BeyondDestinyPlayback(campaign, savedAudio);
                beforeReload.RememberPage(opening.Pages[1].Index);
                var afterReload = new BeyondDestinyPlayback(campaign, savedAudio).Read();
                Require(afterReload.InitialAudio.Music == "Audio/Music/rain_signal.wav" &&
                    afterReload.InitialAudio.Ambience == "Audio/Ambience/storm.wav" &&
                    afterReload.Pages[0].Audio.All(cue => cue.assetPath != "Audio/Ambience/storm.wav"),
                    "Saving on a later line must recover music and ambience without replaying earlier cues.");
                int readable = 0, duels = 0;
                var wardrobe = new HashSet<string>();
                while (!playback.Progress.completed)
                {
                    var segment = playback.Read();
                    readable += segment.Pages.Length;
                    foreach (var page in segment.Pages)
                    {
                        wardrobe.Add(page.Text.heroPortrait);
                        wardrobe.Add(page.Text.guestPortrait);
                    }
                    playback.Finish(segment);
                    if (segment.Duel == null) continue;
                    var continueFromSave = new BeyondDestinyPlayback(campaign, root);
                    Require(continueFromSave.Read().Duel == segment.Duel,
                        "Closing at the duel gate must preserve the pending match.");
                    continueFromSave.Resolve(segment.Duel);
                    playback = new BeyondDestinyPlayback(campaign, root);
                    ++duels;
                }
                var allCues = campaign.Episodes.SelectMany(episode => episode.scenes).SelectMany(scene => scene.events);
                foreach (string variant in new[] { "hero_scar", "aki_masked", "aki_casual", "aki_riding", "sherry_fallen" })
                    Require(wardrobe.Contains($"Characters_Standing/{variant}"), $"No manuscript page selected costume {variant}.");
                Require(readable == allCues.Count(cue => cue.IsReadable), "Playback dropped or repeated source dialogue.");
                Require(duels == 15 && playback.Progress.earnedRewards.Contains("episode_37_clear"),
                    $"Playback must reach every playable duel and its finale (actual duels: {duels}).");
                Require(!BeyondDestinyPlayback.CanContinue("win", false, true, false) &&
                    BeyondDestinyPlayback.CanContinue("canonicalOpponentWin", false, false, true),
                    "A failed boss challenge must retry and the Episode 31 objective must continue into its scripted result.");
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        private static void VerifyNativeDuelOpenings(BeyondDestinyCampaign campaign)
        {
            int scriptErrors = 0, launched = 0;
            Require(YgoNativeBridge.Initialize(Application.streamingAssetsPath, (kind, _) =>
            {
                if (kind == 0) scriptErrors++;
            }) == 0, "OCGCore could not load the Beyond Destiny card data.");
            try
            {
                foreach (var scene in campaign.Episodes.SelectMany(episode => episode.scenes))
                foreach (int line in scene.events.Select(cue => cue.line).Prepend(scene.line))
                {
                    var encounter = campaign.EncounterAt(scene.id, line);
                    if (encounter?.Playable != true) continue;
                    var player = campaign.DeckById(encounter.playerDeckId);
                    var enemy = campaign.DeckById(encounter.opponentDeckId);
                    var lp = GameAppController.BeyondDestinyOpening(encounter);
                    using var duel = new DuelSession();
                    Require(duel.StartNewDuel(player.main, player.extra, enemy.main, enemy.extra,
                        seed: new ulong[] { 17, 31, 55, 87 }, playerOpening: lp.Player, opponentOpening: lp.Opponent,
                        storyGimmick: encounter.Riding ? BeyondDestinyPlayback.RidingRule(encounter) : null),
                        $"The native engine could not open {encounter.id}.");
                    var human = duel.Players[duel.HumanPlayerIndex];
                    var rival = duel.Players[duel.AiPlayerIndex];
                    Require(human.lp == lp.Player.Life && rival.lp == lp.Opponent.Life, $"Bad opening LP for {encounter.id}.");
                    if (encounter.Riding)
                    {
                        Require(human.szone[5]?.code == StoryGimmickRuntime.SpeedWorld &&
                            rival.szone[5]?.code == StoryGimmickRuntime.SpeedWorld &&
                            DuelFieldUI.ReadSpeedCounters(human) >= BeyondDestinyPlayback.OpeningSpeed(encounter) &&
                            DuelFieldUI.ReadSpeedCounters(rival) >= BeyondDestinyPlayback.OpeningSpeed(encounter),
                            $"Speed World 2 did not publish the starting counters in {encounter.id} " +
                            $"(field {human.szone[5]?.code}/{rival.szone[5]?.code}, " +
                            $"SPC {DuelFieldUI.ReadSpeedCounters(human)}/{DuelFieldUI.ReadSpeedCounters(rival)}).");
                    }
                    ++launched;
                }
            }
            finally { YgoNativeBridge.Shutdown(); }
            Require(launched == 15 && scriptErrors == 0, $"Native duel validation: {launched} launches, {scriptErrors} Lua errors.");
        }

        private static void VerifyWalkIn()
        {
            var root = new GameObject("Beyond Destiny test canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var field = new BeyondDestinyField(root.transform);
                bool entered = false;
                Require(field.Open("Maps/spot_neodomino_rain_alley.png", "네오 도미노 시티", () => entered = true),
                    "The opening map and all twelve walk-cycle poses must load.");
                field.MoveToStory();
                for (int i = 0; i < 60 && !entered; ++i) field.Update(.05f, Vector2.zero);
                Require(entered && !field.IsOpen, "The scene could not be reached on foot or left the field controls open.");
                foreach (string actor in new[] { "aki", "sherry", "devine", "yusei", "jack", "crow", "mizoguchi",
                    "misty", "rex", "zone", "ghost_a" })
                {
                    Require(field.Open("Maps/spot_kaiba_dome_plaza.png", "카이바 돔 스타디움", () => { }, actor),
                        $"The field could not stage {actor}.");
                    var guest = field.Root.transform.Find("FieldProjection/StoryGuest").GetComponent<Image>();
                    Require(guest.isActiveAndEnabled && guest.sprite != null, $"Missing direction frames for guest {actor}.");
                    var before = guest.sprite;
                    for (int step = 0; step < 38; ++step) field.Update(.05f, Vector2.right);
                    Require(guest.sprite != before, $"The field guest {actor} did not face the approaching player.");
                    field.Close();
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
