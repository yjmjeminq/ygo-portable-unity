using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.Editor
{
    internal static class ComboRegressionDecks
    {
        [Serializable]
        private sealed class Fixture
        {
            public PresetDeckInfo[] decks;
        }

        private static Dictionary<int, PresetDeckInfo> _decks;

        internal static PresetDeckInfo Get(int id)
        {
            if (_decks == null)
            {
                string path = Path.Combine(Application.dataPath, "..", "Tools", "AiEvaluation", "combo-regression-decks.json");
                var fixture = JsonUtility.FromJson<Fixture>(File.ReadAllText(path));
                if (fixture?.decks == null)
                    throw new InvalidDataException("Missing the seeded combo regression decks.");
                _decks = fixture.decks.ToDictionary(deck => deck.id);
            }

            if (!_decks.TryGetValue(id, out var recipe))
                throw new InvalidDataException($"No seeded combo regression fixture for deck {id}.");
            return recipe;
        }
    }
}
