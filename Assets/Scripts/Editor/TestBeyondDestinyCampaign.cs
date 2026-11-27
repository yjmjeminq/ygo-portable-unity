using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YGOPortable.Core;

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
                Debug.Log("[Beyond Destiny] Loaded 37 episodes, 182 scenes, 21 encounters and verified isolated save/resume.");
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
    }
}
