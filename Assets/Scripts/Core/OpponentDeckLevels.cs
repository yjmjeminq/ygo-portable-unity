using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace YGOPortable.Core
{
    public static class OpponentDeckLevels
    {
        [Serializable]
        private class OpponentLevels
        {
            public int charId;
            public PresetDeckInfo classic;
            public int[] advancedIds;
        }

        [Serializable]
        private class LevelCatalog
        {
            public OpponentLevels[] opponents;
        }

        private const string FileName = "opponent_deck_levels.json";
        private static Dictionary<int, OpponentLevels> _catalog;

        public static IReadOnlyList<PresetDeckInfo> GetDecks(int charId)
        {
            var roster = DeckRepository.GetDecksForCharacter(charId);
            if (roster.Count == 0) return roster;
            _catalog ??= LoadCatalog();
            if (!_catalog.TryGetValue(charId, out var levels)) return roster;

            var decks = new List<PresetDeckInfo>(roster.Count + 1) { levels.classic };
            var seen = new HashSet<int> { levels.classic.id };
            foreach (int id in levels.advancedIds)
            {
                var deck = roster.Find(candidate => candidate.id == id);
                if (deck != null && seen.Add(id)) decks.Add(deck);
            }
            // A roster update can add a preset before the level catalog is rebuilt.
            foreach (var deck in roster.OrderBy(candidate => candidate.id))
                if (seen.Add(deck.id)) decks.Add(deck);
            return decks;
        }

        public static int GetLevel(int charId, int deckId)
        {
            var decks = GetDecks(charId);
            for (int i = 0; i < decks.Count; i++)
                if (decks[i].id == deckId) return i + 1;
            return 0;
        }

        private static Dictionary<int, OpponentLevels> LoadCatalog()
        {
            var result = new Dictionary<int, OpponentLevels>();
            string path = Path.Combine(Application.streamingAssetsPath, FileName);
            try
            {
                var data = JsonUtility.FromJson<LevelCatalog>(File.ReadAllText(path));
                if (data?.opponents == null) return result;
                foreach (var entry in data.opponents)
                {
                    if (entry.classic?.main?.Length != 40 || entry.classic.charId != entry.charId ||
                        entry.classic.extra == null || entry.classic.side == null || entry.advancedIds == null)
                        continue;
                    result[entry.charId] = entry;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Debug.LogWarning($"[OpponentDeckLevels] Could not read {FileName}: {ex.Message}");
            }
            return result;
        }
    }
}
