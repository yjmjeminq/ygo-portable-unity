using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal enum DuelDeckStyle { Development, Control, Lock }

    internal static class DuelDeckStrategy
    {
        private const uint Damage = 1u << 9, Draw = 1u << 4, DeckOut = 1u << 15;
        private const uint DestinyBoard = 94212438, DarkSanctuary = 16625614;
        internal const uint MillenniumAnkh = 37613663;
        private static readonly uint[] SpiritMessages = { 31893528, 67287533, 94772232, 30170981 };
        private static readonly HashSet<uint> WinConditions = new()
        {
            33396948, 7902349, 8124921, 44519536, 70903634, // Exodia
            94212438, 31893528, 67287533, 94772232, 30170981, // Destiny Board and Spirit Messages
            95308449, 81171949, 8062132, // Final Countdown, Jackpot 7, Vennominaga
        };
        private static readonly HashSet<uint> LockPieces = new()
        {
            84171830, 82732705, 61740673, 53334471, 76375976, 35059553, 47084486, 24207889,
            5851097, 15397015, 90846359, 30241314, 42009836, 23516703, 58921041, 47355498,
        };
        private static readonly HashSet<uint> BurnPieces = new() { 67316075, 40633297, 91623717, 39526584 };
        private static readonly HashSet<uint> TrapEngines = new()
        {
            5380979, 92714517, 2347656, 81497285, // Labrynth
            95440946, 20612097, // Eldlich
            16428514, 5697558, // Subterror
        };
        private static readonly ConditionalWeakTable<IReadOnlyList<uint>, Profile> Profiles = new();

        private sealed class Profile
        {
            internal readonly DuelDeckStyle Style;
            internal Profile(DuelDeckStyle style) => Style = style;
        }

        internal static DuelDeckStyle For(DuelSession duel, byte player) =>
            Profiles.GetValue(duel.StartingMainDeck(player), deck => new Profile(Classify(deck, duel.StartingExtraCount(player)))).Style;

        internal static DuelDeckStyle Classify(IReadOnlyList<uint> deck, int extraCount)
        {
            int traps = 0, lockCards = 0, burn = 0, mill = 0, draw = 0, burnEngines = 0, trapEngines = 0;
            bool alternateWin = false, domain = false, runick = false;
            foreach (uint code in deck)
            {
                if (CardDatabase.Get(code) is not CardInfo card) continue;
                uint name = CardDatabase.GetArchetypeCollectionKey(card);
                uint effects = card.DeckEffectCategories;
                // Infinite Impermanence serves as a hand trap in combo decks.
                if (card.IsTrap && name != 10045474) traps++;
                if (WinConditions.Contains(name)) alternateWin = true;
                if (LockPieces.Contains(name)) lockCards++;
                if (BurnPieces.Contains(name)) burnEngines++;
                if (TrapEngines.Contains(name)) trapEngines++;
                domain |= name == 84171830;
                runick |= name == 92107604;
                if ((effects & Damage) != 0) burn++;
                if ((effects & DeckOut) != 0) mill++;
                if ((effects & Draw) != 0) draw++;
            }
            if (alternateWin || (domain && extraCount == 0) || runick || lockCards >= 5 || burnEngines >= 3 ||
                (burn >= 9 && (traps >= 7 || draw >= 5)) || mill >= 10)
                return DuelDeckStyle.Lock;
            return traps >= System.Math.Max(8, deck.Count / 4) || (traps >= 5 && trapEngines >= 4)
                ? DuelDeckStyle.Control : DuelDeckStyle.Development;
        }

        internal static bool IsKeyPiece(uint code) => CardDatabase.Get(code) is CardInfo card &&
            (WinConditions.Contains(CardDatabase.GetArchetypeCollectionKey(card)) ||
             LockPieces.Contains(CardDatabase.GetArchetypeCollectionKey(card)) ||
             BurnPieces.Contains(CardDatabase.GetArchetypeCollectionKey(card)) || card.code is 92107604 or MillenniumAnkh);

        internal static bool IsExodiaPiece(uint code) => CardDatabase.Get(code) is CardInfo card &&
            CardDatabase.GetArchetypeCollectionKey(card) is 33396948 or 7902349 or 8124921 or 44519536 or 70903634;

        internal static bool IsMillenniumMonster(uint code) => code is 1164211 or 38775407 or 74169516;

        internal static bool CanRevealAnkhForSummon(PlayerFieldSnapshot own, SelectableCardItem effect) =>
            effect.location == YgoConstants.LOCATION_SZONE && IsMillenniumMonster(effect.code) &&
            effect.description == (((ulong)effect.code << 20) | 1) &&
            own.hand.Any(card => card.code == MillenniumAnkh);

        internal static bool CanSetBackrow(DuelSession duel, byte player, uint code)
        {
            var startingDeck = duel.StartingMainDeck(player);
            if (code == DestinyBoard || !startingDeck.Contains(DestinyBoard) || !SpiritMessages.All(startingDeck.Contains))
                return true;
            var own = duel.Players[player];
            bool placed = own.szone.Any(card => card?.code == DestinyBoard);
            if (!placed && !own.hand.Any(card => card.code == DestinyBoard)) return true;
            int pending = SpiritMessages.Count(message => !own.mzone.Any(card => card?.code == message) &&
                !own.szone.Any(card => card?.code == message));
            if (own.szone[YgoConstants.FIELD_ZONE_SEQUENCE] is { code: DarkSanctuary, IsFaceUp: true, effectsDisabled: false })
                pending = System.Math.Max(0, pending - own.mzone.Take(5).Count(card => card == null));
            return own.szone.Take(5).Count(card => card == null) > pending + (placed ? 0 : 1);
        }
    }
}
