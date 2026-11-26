using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static YGOPortable.Core.YgoConstants;

namespace YGOPortable.Core
{
    public enum CardRarity
    {
        Normal,
        Rare,
        SuperRare,
        UltraRare,
    }

    public static class CardRarityCatalog
    {
        internal const int TierCount = 4;
        private const string FileName = "master_duel_rarities.json";

        // Uses the same effect categories as the deck editor.
        private const uint Development = (1u << 4) | (1u << 5) | (1u << 6);
        private const uint Disruption = (1u << 1) | (1u << 8) | (1u << 13);

        [Serializable]
        private sealed class RarityData
        {
            public uint[] normal;
            public uint[] rare;
            public uint[] superRare;
            public uint[] ultraRare;
        }

        private static Dictionary<uint, CardRarity> _masterDuelRarities;

        public static CardRarity GetRarity(uint code) => GetRarity(code, out _);

        public static CardRarity GetRarity(uint code, out bool fromMasterDuel)
        {
            var rarities = _masterDuelRarities ??= LoadMasterDuelRarities();
            fromMasterDuel = true;
            if (rarities.TryGetValue(code, out var rarity)) return rarity;

            var card = CardDatabase.Get(code);
            if (card != null && card.alias != 0)
            {
                if (rarities.TryGetValue(card.alias, out rarity)) return rarity;
                card = CardDatabase.Get(card.alias) ?? card;
            }
            fromMasterDuel = false;
            return AssignLocalRarity(card);
        }

        public static string GetLabel(CardRarity rarity) => rarity switch
        {
            CardRarity.UltraRare => "UR",
            CardRarity.SuperRare => "SR",
            CardRarity.Rare => "R",
            _ => "N",
        };

        public static int GetBaseWeight(CardRarity rarity) => rarity switch
        {
            CardRarity.UltraRare => 25,
            CardRarity.SuperRare => 75,
            CardRarity.Rare => 350,
            _ => 550,
        };

        private static Dictionary<uint, CardRarity> LoadMasterDuelRarities()
        {
            var rarities = new Dictionary<uint, CardRarity>();
            string path = CardDataPack.RuntimePath(FileName);
            if (!File.Exists(path)) return rarities;

            try
            {
                var data = JsonUtility.FromJson<RarityData>(File.ReadAllText(path));
                if (data == null) return rarities;

                void Add(uint[] codes, CardRarity rarity)
                {
                    foreach (uint code in codes ?? Array.Empty<uint>())
                        if (code != 0) rarities[code] = rarity;
                }

                Add(data.normal, CardRarity.Normal);
                Add(data.rare, CardRarity.Rare);
                Add(data.superRare, CardRarity.SuperRare);
                Add(data.ultraRare, CardRarity.UltraRare);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException)
            {
                Debug.LogWarning($"[CardRarityCatalog] Could not read {FileName}: {error.Message}");
            }
            return rarities;
        }

        private static CardRarity AssignLocalRarity(CardInfo card)
        {
            if (card == null || (card.type & TYPE_TOKEN) != 0) return CardRarity.Normal;
            if (CollectionProgress.GetDeckCopyKey(card.code) is 55144522 or 79571449 or 12580477 or 18144506)
                return CardRarity.UltraRare;

            int score = card.IsExtra ? 2 : 0;
            if (card.IsMonster && card.atk >= 2600) score++;
            if ((card.DeckEffectCategories & Development) != 0) score++;
            if ((card.DeckEffectCategories & Disruption) != 0) score++;

            return score switch
            {
                >= 5 => CardRarity.UltraRare,
                >= 3 => CardRarity.SuperRare,
                >= 1 => CardRarity.Rare,
                _ => CardRarity.Normal,
            };
        }
    }
}
