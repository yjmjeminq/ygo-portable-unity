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
        Tournament,
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
                DeckMainSortMode.Tournament => entries.OrderBy(entry => TournamentOrder(entry.Card)),
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
            if (card.IsExtra) return ExtraOrder(card);
            if (card.IsMonster) return 0;
            return card.IsSpell ? 1 + SpellOrder(card) : 7 + TrapOrder(card);
        }

        private static int TournamentOrder(CardInfo card)
        {
            if (card == null) return int.MaxValue;
            if (card.IsExtra) return ExtraOrder(card);
            if (card.IsMonster)
            {
                if (card.IsPendulum) return 18;
                if ((card.type & YgoConstants.TYPE_RITUAL) != 0) return 15;
                return (card.type & YgoConstants.TYPE_NORMAL) != 0 ? 0 : 10;
            }
            if (card.IsSpell) return 30 + SpellOrder(card);
            if (card.IsTrap) return 40 + TrapOrder(card);
            return int.MaxValue;
        }

        private static int ExtraOrder(CardInfo card)
        {
            if (card.IsFusion) return 20;
            if (card.IsSynchro) return 21;
            if (card.IsXyz) return 22;
            return 23;
        }

        private static int SpellOrder(CardInfo card)
        {
            uint type = card.type;
            if ((type & YgoConstants.TYPE_QUICKPLAY) != 0) return 1;
            if ((type & YgoConstants.TYPE_CONTINUOUS) != 0) return 2;
            if ((type & YgoConstants.TYPE_EQUIP) != 0) return 3;
            if ((type & YgoConstants.TYPE_FIELD) != 0) return 4;
            if ((type & YgoConstants.TYPE_RITUAL) != 0) return 5;
            return 0;
        }

        private static int TrapOrder(CardInfo card)
        {
            if ((card.type & YgoConstants.TYPE_CONTINUOUS) != 0) return 1;
            if ((card.type & YgoConstants.TYPE_COUNTER) != 0) return 2;
            return 0;
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
