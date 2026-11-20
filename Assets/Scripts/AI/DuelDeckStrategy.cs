using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal enum DuelDeckStyle { Development, Control, Lock, Resource }

    internal static class DuelDeckStrategy
    {
        internal const int StyleCount = (int)DuelDeckStyle.Resource + 1;
        internal const uint Engage = 63166095, Afterburners = 99550630, WidowAnchor = 98338152;
        internal const uint RunickFountain = 92107604, RunickTip = 31562086;
        internal const uint StrikerShizuku = 90673288, StrikerZero = 76072561;
        private const ushort SkyStriker = 0x115, SkyStrikerAce = 0x1115, Runick = 0x180;
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
            internal readonly bool StrikerEngine;

            internal Profile(DuelDeckStyle style, bool strikerEngine)
            {
                Style = style;
                StrikerEngine = strikerEngine;
            }
        }

        private static Profile GetProfile(DuelSession duel, byte player)
        {
            var deck = duel.StartingMainDeck(player);
            if (Profiles.TryGetValue(deck, out var profile)) return profile;
            return Profiles.GetValue(deck, cards => Analyze(cards, duel.StartingExtraCount(player)));
        }

        internal static DuelDeckStyle For(DuelSession duel, byte player) => GetProfile(duel, player).Style;

        internal static DuelDeckStyle Classify(IReadOnlyList<uint> deck, int extraCount) => Analyze(deck, extraCount).Style;

        private static Profile Analyze(IReadOnlyList<uint> deck, int extraCount)
        {
            int traps = 0, lockCards = 0, burn = 0, mill = 0, draw = 0, burnEngines = 0, trapEngines = 0;
            int spells = 0, strikerSpells = 0, runickSpells = 0;
            bool alternateWin = false, domain = false;
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
                if (card.IsSpell)
                {
                    spells++;
                    if (IsArchetype(card, SkyStriker)) strikerSpells++;
                    if (IsArchetype(card, Runick)) runickSpells++;
                }
                if ((effects & Damage) != 0) burn++;
                if ((effects & DeckOut) != 0) mill++;
                if ((effects & Draw) != 0) draw++;
            }
            if (alternateWin || (domain && extraCount == 0)) return new Profile(DuelDeckStyle.Lock, false);
            bool striker = strikerSpells >= 5 && extraCount > 0;
            if (spells >= deck.Count / 3 && (striker || runickSpells >= 5))
                return new Profile(DuelDeckStyle.Resource, striker);
            if (lockCards >= 5 || burnEngines >= 3 ||
                (burn >= 9 && (traps >= 7 || draw >= 5)) || mill >= 10)
                return new Profile(DuelDeckStyle.Lock, false);
            return new Profile(traps >= System.Math.Max(8, deck.Count / 4) || (traps >= 5 && trapEngines >= 4)
                ? DuelDeckStyle.Control : DuelDeckStyle.Development, false);
        }

        private static bool IsArchetype(CardInfo card, ushort archetype)
        {
            for (ulong codes = card.setcode; codes != 0; codes >>= 16)
                if ((codes & 0x0fff) == (uint)(archetype & 0x0fff) && (codes & archetype) == archetype)
                    return true;
            return false;
        }

        internal static bool IsStrikerSpell(CardInfo card) => card?.IsSpell == true && IsArchetype(card, SkyStriker);
        internal static bool IsRunickCard(CardInfo card) => IsArchetype(card, Runick);

        internal static bool RequiresOpenMainZone(uint code) => code is
            9726840 or 9726841 or 21623008 or 25733157 or 25955749 or 46271408 or 51227866 or 52340444 or
            Engage or 63166096 or 63166097 or WidowAnchor or 98338153 or Afterburners;

        internal static bool CanFillMainMonsterZone(DuelSession duel, DuelPrompt prompt, SelectableCardItem item, bool setting)
        {
            byte player = prompt.playerId;
            if (!ShouldKeepMainMonsterZoneOpen(duel, player)) return true;
            var own = duel.Players[player];

            // Raye and Roze can become an Extra Monster Zone Link and reopen the main zones.
            if (!setting && CardDatabase.Get(item.code) is CardInfo card &&
                (IsArchetype(card, SkyStrikerAce) && own.extra.Any(candidate => CardDatabase.Get(candidate.code) is CardInfo ace &&
                    ace.IsLink && IsArchetype(ace, SkyStrikerAce)) ||
                prompt.canBp && DuelCombat.CanTestLethalPosition(duel, player, card.atk)))
                return true;

            return own.mzone.All(card => card == null) && DuelCombat.VisibleAttackTotal(duel.Players[1 - player]) >= own.lp;
        }

        internal static bool ShouldKeepMainMonsterZoneOpen(DuelSession duel, byte player) =>
            GetProfile(duel, player).StrikerEngine && CountMainMonsters(duel.Players[player]) == 0 &&
            HasOpenZoneSpell(duel.Players[player]);

        internal static bool BlocksMainMonsterZoneSpells(DuelSession duel, byte player) =>
            GetProfile(duel, player).StrikerEngine && CountMainMonsters(duel.Players[player]) > 0 &&
            HasOpenZoneSpell(duel.Players[player]);

        private static int CountMainMonsters(PlayerFieldSnapshot field) => field.mzone.Take(5).Count(card => card != null);

        private static bool HasOpenZoneSpell(PlayerFieldSnapshot field) => field.hand.Any(card => RequiresOpenMainZone(card.code)) ||
            field.szone.Any(card => card != null && !card.IsFaceUp && RequiresOpenMainZone(card.code));

        internal static double ResourcePosition(DuelSession duel, byte player)
        {
            var own = duel.Players[player];
            if (GetProfile(duel, player).StrikerEngine)
            {
                int spells = own.grave.Count(card => (card.type & YgoConstants.TYPE_SPELL) != 0);
                int occupied = CountMainMonsters(own);
                return .26 * System.Math.Min(5, spells) + (spells >= 3 ? .50 : 0) +
                    (occupied == 0 ? .65 : HasOpenZoneSpell(own) ? -1.75 - .2 * occupied : -.40);
            }

            bool fountain = own.szone[YgoConstants.FIELD_ZONE_SEQUENCE] is { code: RunickFountain, IsFaceUp: true, effectsDisabled: false };
            int recoverable = own.grave.Count(card => (card.type & YgoConstants.TYPE_QUICKPLAY) != 0 &&
                CardDatabase.Get(card.code) is CardInfo spell && IsArchetype(spell, Runick));
            return (fountain ? 1.15 : 0) + (fountain ? .46 : .18) * System.Math.Min(3, recoverable);
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
