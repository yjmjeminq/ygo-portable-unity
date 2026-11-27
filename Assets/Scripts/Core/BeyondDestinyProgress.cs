using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace YGOPortable.Core
{
    [Serializable]
    internal sealed class BeyondDestinyProgress
    {
        public int version = 1;
        public int episodeIndex, sceneIndex, cueIndex;
        public bool completed;
        public List<string> unlockedLocations = new();
        public List<int> unlockedDecks = new();
        public List<string> earnedRewards = new();
        public List<string> resolvedEncounters = new();

        internal BeyondDestinyCampaign.Scene Scene(BeyondDestinyCampaign campaign) =>
            campaign.Episodes[episodeIndex].scenes[sceneIndex];

        internal BeyondDestinyCampaign.Cue Cue(BeyondDestinyCampaign campaign) => Scene(campaign).events[cueIndex];

        internal bool Advance(BeyondDestinyCampaign campaign)
        {
            if (completed) return false;
            var episode = campaign.Episodes[episodeIndex];
            if (cueIndex + 1 < episode.scenes[sceneIndex].events.Length) ++cueIndex;
            else if (sceneIndex + 1 < episode.scenes.Length)
            {
                ++sceneIndex;
                cueIndex = 0;
            }
            else if (episodeIndex + 1 < campaign.Episodes.Count)
            {
                ++episodeIndex;
                sceneIndex = cueIndex = 0;
            }
            else completed = true;
            return !completed;
        }

        internal static BeyondDestinyProgress Load(string persistentDataPath, BeyondDestinyCampaign campaign, bool replay = false)
        {
            string path = SavePath(persistentDataPath, replay);
            if (!File.Exists(path)) return new BeyondDestinyProgress();
            var progress = JsonUtility.FromJson<BeyondDestinyProgress>(File.ReadAllText(path));
            if (progress == null) throw new InvalidDataException("The Beyond Destiny save could not be read.");
            progress.Validate(campaign);
            return progress;
        }

        internal void Save(string persistentDataPath, BeyondDestinyCampaign campaign, bool replay = false)
        {
            Validate(campaign);
            string path = SavePath(persistentDataPath, replay);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(pending, JsonUtility.ToJson(this));
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
            }
            finally
            {
                if (File.Exists(pending)) File.Delete(pending);
            }
        }

        private void Validate(BeyondDestinyCampaign campaign)
        {
            if (version != 1 || episodeIndex < 0 || episodeIndex >= campaign.Episodes.Count ||
                sceneIndex < 0 || sceneIndex >= campaign.Episodes[episodeIndex].scenes.Length ||
                cueIndex < 0 || cueIndex >= Scene(campaign).events.Length ||
                unlockedLocations == null || unlockedDecks == null || earnedRewards == null ||
                resolvedEncounters == null || unlockedDecks.Exists(id => campaign.DeckById(id) == null))
                throw new InvalidDataException("The Beyond Destiny save has an invalid checkpoint or unlock list.");
            if (completed && (episodeIndex + 1 != campaign.Episodes.Count ||
                sceneIndex + 1 != campaign.Episodes[episodeIndex].scenes.Length ||
                cueIndex + 1 != Scene(campaign).events.Length))
                throw new InvalidDataException("The final Beyond Destiny checkpoint is missing.");
        }

        internal static bool HasReplay(string root) => File.Exists(SavePath(root, replay: true));

        private static string SavePath(string root, bool replay) =>
            Path.Combine(root, "campaigns", replay ? "beyond_destiny_replay.json" : "beyond_destiny.json");
    }
}
