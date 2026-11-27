using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
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
            Require(scenes[0].backgroundPath == "Maps/spot_neodomino_rain_alley.png" &&
                cues["background"].Any(cue => cue.assetPath == "Maps/spot_ark_cradle.png"),
                "The campaign did not deserialize new environment artwork bindings.");
            var boss = encounters.Single(encounter => encounter.episodeId == "beyond_destiny_ep_31");
            Require(boss.Playable && boss.Riding && boss.resolution == "canonicalOpponentWin" &&
                campaign.DeckById(boss.opponentDeckId).sigCard == 73580471 &&
                campaign.DeckById(boss.playerDeckId).main.Length == 40,
                "Episode 31 must preserve its original result and use the correct riding decks.");
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
                int readable = 0, duels = 0;
                while (!playback.Progress.completed)
                {
                    var segment = playback.Read();
                    readable += segment.Pages.Length;
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
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
