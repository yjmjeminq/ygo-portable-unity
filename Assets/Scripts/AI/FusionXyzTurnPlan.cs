using System;
using System.Collections.Generic;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal static class FusionXyzTurnPlan
    {
        private const uint Beaver = 68353324;
        private const uint Blue = 76145933;
        private const uint Jet = 13533678;
        private const uint Red = 75922381;
        private const uint Carrot = 2311090;
        private const uint Starter = 15443125;
        private const uint Gigantic = 54498517;
        private const uint Masquerena = 65741786;
        private const uint Reinoheart = 73956664;
        private const uint Scheiren = 572850;
        private const uint Merrli = 74078255;
        private const uint Havnis = 37961969;
        private const uint Perlereino = 77103950;
        private const uint Kitkallos = 92731385;
        private const uint KaleidoHeart = 28226490;
        private const uint Dragostapelia = 69946549;
        private const uint FoolishBurial = 81439173;

        private const int NormalSummon = 0;
        private const int SpecialSummon = 1;
        private const int Activate = 5;
        private const ulong SendToGrave = 504;
        private const ulong AddToHand = 506;
        private const ulong SelectSpecialSummon = 509;
        private const ulong DescriptionIndexMask = (1UL << 20) - 1;

        internal static int? ChooseIdle(DuelSession session, DuelPrompt prompt, Func<int, SelectableCardItem, bool> canUse)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_IDLECMD) return null;

            var own = session.Players[prompt.playerId];
            var rival = session.Players[1 - prompt.playerId];
            int? Action(List<SelectableCardItem> choices, int command, uint code, uint location, int effect = -1)
            {
                int index = choices.FindIndex(card => card.controller == prompt.playerId && card.code == code &&
                    card.location == location && (effect < 0 || IsEffect(card.description, code, effect)) &&
                    canUse(command, card));
                return index < 0 ? null : (index << 16) | command;
            }

            int? Normal(uint code) => Action(prompt.summonable, NormalSummon, code, YgoConstants.LOCATION_HAND);
            int? Special(uint code, uint location) => Action(prompt.spSummonable, SpecialSummon, code, location);
            int? Effect(uint code, uint location, int effect = -1) =>
                Action(prompt.activatable, Activate, code, location, effect);

            if (own.extra.Any(card => card.code == Gigantic) || OnBoard(own, Gigantic))
            {
                // Use the existing Level 2 body to Special Summon our searchers before overlaying it.
                var searcher = Special(Blue, YgoConstants.LOCATION_HAND) ?? Special(Jet, YgoConstants.LOCATION_HAND);
                if (searcher.HasValue) return searcher;

                var recruit = Effect(Gigantic, YgoConstants.LOCATION_MZONE, 0);
                if (recruit.HasValue) return recruit;

                bool hasLevelOrRankTwo = own.mzone.Any(card => card != null && card.IsFaceUp &&
                    (card.level == 2 || card.rank == 2));
                var openBoard = Normal(Beaver) ?? Effect(Starter, YgoConstants.LOCATION_HAND) ??
                    Effect(Starter, YgoConstants.LOCATION_SZONE);
                if (!hasLevelOrRankTwo && openBoard.HasValue) return openBoard;

                // Keep an established negate on the field unless spare material can make Gigantic.
                bool hasLiveNegate = (OnBoard(own, Red) || OnBoard(own, Carrot)) &&
                    own.mzone.Count(IsSprightTribute) >= 2;
                int spareBodies = own.mzone.Count(card => IsGiganticMaterial(card) && card.code is not (Red or Carrot));
                if (!OnBoard(own, Gigantic) && (!hasLiveNegate || spareBodies >= 3))
                {
                    var xyz = Special(Gigantic, YgoConstants.LOCATION_EXTRA);
                    if (xyz.HasValue) return xyz;
                }

                uint firstNegate = rival.szone.Any(card => card != null) ? Carrot : Red;
                uint secondNegate = firstNegate == Red ? Carrot : Red;
                var interaction = Special(firstNegate, YgoConstants.LOCATION_HAND) ??
                    Special(secondNegate, YgoConstants.LOCATION_HAND);
                if (interaction.HasValue) return interaction;
                if (openBoard.HasValue) return openBoard;
                var masquerena = own.mzone.Count(card => card?.IsFaceUp == true &&
                    card.code is not (Gigantic or Red or Carrot) && card.link == 0) >= 2
                    ? Special(Masquerena, YgoConstants.LOCATION_EXTRA) : null;
                if (masquerena.HasValue) return masquerena;
            }

            if (own.extra.Any(card => card.code is Kitkallos or KaleidoHeart) || OnBoard(own, Kitkallos))
            {
                // Open with the search and retain the Normal Summon until the missing card is known.
                var search = Effect(Perlereino, YgoConstants.LOCATION_HAND);
                if (search.HasValue) return search;

                bool canMillWithMerrli = own.deckCount >= 8 &&
                    (InHand(own, Merrli) || own.grave.Any(card => card.code == Merrli));
                if (canMillWithMerrli)
                {
                    var recycle = Effect(Kitkallos, YgoConstants.LOCATION_MZONE, 1);
                    if (recycle.HasValue) return recycle;
                }

                return Normal(Reinoheart) ?? Effect(Scheiren, YgoConstants.LOCATION_HAND, 0) ??
                    Effect(FoolishBurial, YgoConstants.LOCATION_HAND) ?? Normal(Merrli);
            }

            return null;
        }

        internal static bool PreserveOpeningBoard(DuelSession session, byte player)
        {
            if (session.TurnNumber > 1) return false;
            var own = session.Players[player];
            bool spright = (own.extra.Any(card => card.code == Gigantic) || OnBoard(own, Gigantic)) &&
                (OnBoard(own, Gigantic) || OnBoard(own, Masquerena) ||
                 ((OnBoard(own, Red) || OnBoard(own, Carrot)) && own.mzone.Count(IsSprightTribute) >= 2));
            return spright || OnBoard(own, Kitkallos) || OnBoard(own, KaleidoHeart);
        }

        internal static int? ChooseLinkMaterial(DuelSession session, DuelPrompt prompt)
        {
            if (session.LastHintMsg != 533 || !PreserveOpeningBoard(session, prompt.playerId) ||
                prompt.candidates.Any(card => card.controller != prompt.playerId || card.location != YgoConstants.LOCATION_MZONE))
                return null;

            int material = prompt.candidates.FindIndex(card =>
                card.code is not (Gigantic or Red or Carrot or Masquerena or Kitkallos or KaleidoHeart));
            return material < 0 ? null : material;
        }

        internal static int? ChooseCard(DuelSession session, DuelPrompt prompt)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_CARD || prompt.min != 1 || prompt.max != 1 ||
                prompt.candidates.Count == 0)
                return null;

            var source = session.SelectionChainLink;
            if (source == null || source.negated || source.triggerPlayer != prompt.playerId ||
                !prompt.candidates.All(card => card.controller == prompt.playerId))
                return null;

            var own = session.Players[prompt.playerId];
            var rival = session.Players[1 - prompt.playerId];
            bool OnlyFrom(uint location) => prompt.candidates.All(card => card.location == location);
            bool Available(uint code) => InHand(own, code) || OnBoard(own, code);
            int? Find(uint code)
            {
                int index = prompt.candidates.FindIndex(card => card.code == code);
                return index < 0 ? null : index;
            }

            var hint = session.LastHintMsg;
            if (hint == SelectSpecialSummon && (source.code is Gigantic or Starter) && OnlyFrom(YgoConstants.LOCATION_DECK))
            {
                // Red and Carrot require a different Level, Rank, or Link 2 monster to tribute.
                if (!Available(Blue) && Find(Blue) is int blue) return blue;
                if (own.mzone.Any(IsSprightTribute))
                {
                    bool backrow = rival.szone.Any(card => card != null);
                    uint primary = backrow ? Carrot : Red;
                    uint secondary = backrow ? Red : Carrot;
                    if (!OnBoard(own, primary) && Find(primary) is int primaryNegate) return primaryNegate;
                    if (!OnBoard(own, secondary) && Find(secondary) is int secondaryNegate) return secondaryNegate;
                }

                return Find(Jet) ?? Find(Blue) ?? Find(Red) ?? Find(Carrot);
            }

            if (hint == AddToHand && OnlyFrom(YgoConstants.LOCATION_DECK))
            {
                if (source.code == Blue)
                {
                    if (!Available(Jet) && Find(Jet) is int jet) return jet;
                    if (rival.szone.Any(card => card != null) && !Available(Carrot) && Find(Carrot) is int carrot)
                        return carrot;
                    if (!Available(Red) && Find(Red) is int red) return red;
                    return Find(Carrot) ?? Find(Jet);
                }

                if (source.code is Perlereino or Kitkallos)
                {
                    if (source.code == Kitkallos && own.deckCount >= 8 && !Available(Merrli) &&
                        !own.grave.Any(card => card.code == Merrli) && Find(Merrli) is int miller)
                        return miller;
                    if (!Available(Reinoheart) && Find(Reinoheart) is int starter) return starter;
                    if (!Available(Scheiren) && Find(Scheiren) is int extender) return extender;
                    return Find(Merrli) ?? Find(Scheiren);
                }
            }

            if (hint == SelectSpecialSummon && source.code == Kitkallos &&
                IsEffect(source.description, Kitkallos, 1) &&
                prompt.candidates.All(card => card.location is YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_GRAVE))
            {
                if (own.deckCount >= 3 && Find(Merrli) is int miller) return miller;
                return Find(Reinoheart);
            }

            if (hint == SendToGrave &&
                ((source.code == Reinoheart && source.triggerLoc == YgoConstants.LOCATION_MZONE && OnlyFrom(YgoConstants.LOCATION_DECK)) ||
                 (source.code == Scheiren && source.triggerLoc == YgoConstants.LOCATION_HAND && OnlyFrom(YgoConstants.LOCATION_HAND)) ||
                 (source.code == FoolishBurial && OnlyFrom(YgoConstants.LOCATION_DECK) &&
                    own.extra.Any(card => card.code is Kitkallos or KaleidoHeart))))
            {
                // Retain Reinoheart as a Normal Summon and spare Scheiren when it can still extend from hand.
                return Find(Merrli) ?? Find(Havnis) ?? Find(Scheiren);
            }

            if (hint == SelectSpecialSummon && OnlyFrom(YgoConstants.LOCATION_EXTRA) &&
                source.triggerLoc == YgoConstants.LOCATION_GRAVE && (source.code is Merrli or Havnis or Scheiren))
            {
                bool hasPublicTarget = rival.mzone.Any(card => card != null && card.IsFaceUp) ||
                    rival.szone.Any(card => card != null);
                if (hasPublicTarget && Find(KaleidoHeart) is int removal) return removal;
                if (own.deckCount >= 5 && !OnBoard(own, Kitkallos) && Find(Kitkallos) is int extender) return extender;
                if (hasPublicTarget && Find(Dragostapelia) is int negate) return negate;
                return Find(KaleidoHeart) ?? Find(Dragostapelia) ?? Find(Kitkallos);
            }

            return null;
        }

        private static bool InHand(PlayerFieldSnapshot own, uint code) => own.hand.Any(card => card.code == code);

        private static bool OnBoard(PlayerFieldSnapshot own, uint code) =>
            own.mzone.Any(card => card != null && card.IsFaceUp && card.code == code);

        private static bool IsGiganticMaterial(LiveCardState card) =>
            card != null && card.IsFaceUp && (card.level == 2 || card.link == 2);

        private static bool IsSprightTribute(LiveCardState card) =>
            IsGiganticMaterial(card) || (card != null && card.IsFaceUp && card.rank == 2);

        private static bool IsEffect(ulong description, uint code, int index) =>
            CardDatabase.GetDescriptionCardCode(description) == code && (description & DescriptionIndexMask) == (ulong)index;
    }
}
