using System;
using System.Collections.Generic;
using System.Linq;

namespace YGOPortable.Core
{
    public enum DeckMainSortMode
    {
        Frame,
        Name,
        Attack,
        Defense,
        Level,
        Auto,
    }

    public static class DeckMainSorter
    {
        public static uint[] Order(IEnumerable<uint> cards, DeckMainSortMode mode)
        {
            var entries = cards.Select(code => (Code: code, Card: CardDatabase.Get(code)));
            var ordered = mode switch
            {
                DeckMainSortMode.Auto => entries.OrderBy(entry => AutoOrder(entry.Card))
                    .ThenByDescending(entry => Level(entry.Card))
                    .ThenByDescending(entry => Attack(entry.Card)),
                DeckMainSortMode.Frame => entries.OrderBy(entry => FrameOrder(entry.Card)),
                DeckMainSortMode.Attack => entries.OrderByDescending(entry => Attack(entry.Card)),
                DeckMainSortMode.Defense => entries.OrderByDescending(entry => Defense(entry.Card)),
                DeckMainSortMode.Level => entries.OrderByDescending(entry => Level(entry.Card)),
                _ => entries.OrderBy(entry => entry.Card == null),
            };

            return ordered
                .ThenBy(entry => string.IsNullOrEmpty(entry.Card?.name))
                .ThenBy(entry => entry.Card?.name, StringComparer.Ordinal)
                .Select(entry => entry.Code)
                .ToArray();
        }

        private static int AutoOrder(CardInfo card)
        {
            if (card == null) return int.MaxValue;
            uint type = card.type;
            if (card.IsExtra)
            {
                if ((type & YgoConstants.TYPE_FUSION) != 0) return 20;
                if ((type & YgoConstants.TYPE_SYNCHRO) != 0) return 21;
                if (card.IsXyz) return 22;
                return 23;
            }
            if (card.IsMonster) return 0;
            if (card.IsSpell)
            {
                if ((type & YgoConstants.TYPE_QUICKPLAY) != 0) return 2;
                if ((type & YgoConstants.TYPE_CONTINUOUS) != 0) return 3;
                if ((type & YgoConstants.TYPE_EQUIP) != 0) return 4;
                if ((type & YgoConstants.TYPE_FIELD) != 0) return 5;
                if ((type & YgoConstants.TYPE_RITUAL) != 0) return 6;
                return 1;
            }
            if ((type & YgoConstants.TYPE_CONTINUOUS) != 0) return 8;
            if ((type & YgoConstants.TYPE_COUNTER) != 0) return 9;
            return 7;
        }

        private static int? Attack(CardInfo card) => card?.IsMonster == true && card.atk >= 0 ? card.atk : null;

        private static int? Defense(CardInfo card) =>
            card?.IsMonster == true && !card.IsLink && card.def >= 0 ? card.def : null;

        private static uint? Level(CardInfo card) => card?.IsMonster == true ? card.level : null;

        internal static int FrameOrder(CardInfo card) => card?.FrameSpriteName switch
        {
            "card_nomal" => 0,
            "card_kouka" => 1,
            "card_gisiki" => 2,
            "card_pendulum_n" => 3,
            "card_pendulum" => 4,
            "card_yugo" => 5,
            "card_fusion_pendulum" => 6,
            "card_sync" => 7,
            "card_sync_pendulum" => 8,
            "card_xyz" => 9,
            "card_xyz_pendulum" => 10,
            "card_link" => 11,
            "card_mahou" => 12,
            "card_wana" => 13,
            "card_token" => 14,
            _ => 15,
        };
    }
}
