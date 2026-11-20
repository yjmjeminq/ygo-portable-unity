using System;
using System.Collections.Generic;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal static class DddTurnPlan
    {
        private const uint Gate = 46372010;
        private const uint SwampKing = 73360025;
        private const uint SpiritKing = 32665564;
        private const uint Kepler = 11609969;
        private const uint Copernicus = 46796664;
        private const uint SwirlSlime = 45206713;
        private const uint NecroSlime = 72291412;
        private const uint Griffin = 28406301;
        private const uint CountSurveyor = 5997110;
        private const uint ScaleSurveyor = 42382265;
        private const uint Lamia = 19580308;
        private const uint Ragnarok = 74069667;
        private const uint Genghis = 74583607;
        private const uint HighGenghis = 16006416;
        private const uint Gilgamesh = 9024198;
        private const uint Solomon = 32232538;
        private const uint Machinex = 46593546;
        private const uint Siegfried = 44852429;

        private const uint DdSet = 0xaf;
        private const uint DddSet = 0x10af;
        private const uint DarkContractSet = 0xae;

        private const int NormalSummon = 0;
        private const int SpecialSummon = 1;
        private const int Activate = 5;
        private const ulong PlacePendulumScale = 1160;
        private const ulong DescriptionIndexMask = (1UL << 20) - 1;

        internal static bool IsContractSearch(SelectableCardItem item) => item.code == Gate &&
            item.location is YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_SZONE;

        internal static int? ChooseEarlyPendulumRecovery(DuelSession session, DuelPrompt prompt,
            Func<int, SelectableCardItem, bool> canUse)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_IDLECMD || session.TurnNumber > 2 ||
                session.TurnPlayer != prompt.playerId || session.CurrentPhase != YgoConstants.PHASE_MAIN1)
                return null;

            var own = session.Players[prompt.playerId];
            if (!IsDddDeck(own) || !own.mzone.Any(card =>
                card is { code: Gilgamesh, IsFaceUp: true, effectsDisabled: false }))
                return null;

            var left = own.szone[6] ?? own.szone[0];
            var right = own.szone[7] ?? own.szone[4];
            bool ActiveScale(LiveCardState card) => card is { IsFaceUp: true, effectsDisabled: false } &&
                IsSet(card.code, DdSet) && CardDatabase.Get(card.code)?.IsPendulum == true;
            if (!ActiveScale(left) || !ActiveScale(right) || PendulumTurnPlan.ReadyFollowUps(own) <= 0 ||
                !own.extra.Any(card => card.IsFaceUp && IsLevelMonster(card.code) &&
                    CardDatabase.Get(card.code)?.IsPendulum == true &&
                    IsBetween(card.level, (int)left.lscale, (int)right.rscale)))
                return null;

            // Recover an available body before ending the turn and setting its supporting Quick-Play cards.
            int index = prompt.spSummonable.FindIndex(item => item.controller == prompt.playerId &&
                item.location is YgoConstants.LOCATION_SZONE or YgoConstants.LOCATION_PZONE &&
                IsSet(item.code, DdSet) && CardDatabase.Get(item.code)?.IsPendulum == true &&
                canUse(SpecialSummon, item));
            return index < 0 ? null : (index << 16) | SpecialSummon;
        }

        internal static int? ChooseIdle(DuelSession session, DuelPrompt prompt, Func<int, SelectableCardItem, bool> canUse)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_IDLECMD) return null;
            var own = session.Players[prompt.playerId];
            if (!IsDddDeck(own)) return null;

            var rival = session.Players[1 - prompt.playerId];
            var board = own.mzone.Where(card => card != null && card.IsFaceUp).ToArray();
            var left = own.szone[6] ?? own.szone[0];
            var right = own.szone[7] ?? own.szone[4];
            bool openScales = left == null && right == null;
            bool opponentHasMonster = rival.mzone.Any(card => card != null && card.IsFaceUp);
            bool opponentHasBackrow = rival.szone.Any(card => card != null);
            bool hasContract = own.szone.Any(card => card != null && card.IsFaceUp && IsDarkContract(card.code));
            bool hasDdd = board.Any(card => IsSet(card.code, DdSet));
            bool hasSpareBody = board.Count(card => IsSet(card.code, DdSet)) >= 2;
            bool hasFusion = board.Any(card => card.code is Genghis or HighGenghis);
            bool hasPendulumTargets = own.hand.Any(card => IsLevelMonster(card.code)) ||
                own.extra.Any(card => card.IsFaceUp && IsLevelMonster(card.code));
            bool hasLinkMaterialToRecover = board.Any(card => IsLevelMonster(card.code) && CardDatabase.Get(card.code).IsPendulum);

            int? Action(List<SelectableCardItem> list, int command, Func<SelectableCardItem, bool> fits)
            {
                int index = list.FindIndex(item => item.controller == prompt.playerId && fits(item) && canUse(command, item));
                return index < 0 ? null : (index << 16) | command;
            }

            int? Normal(uint code) => Action(prompt.summonable, NormalSummon, item => item.code == code);
            int? Special(uint code) => Action(prompt.spSummonable, SpecialSummon, item => item.code == code);
            int? Effect(uint code, uint location, int index = -1) => Action(prompt.activatable, Activate, item =>
                item.code == code && item.location == location && item.description != PlacePendulumScale &&
                (index < 0 || IsDescription(item.description, code, index)));

            // Establish a way to find the part of the combo that the hand is missing.
            var search = Effect(Gate, YgoConstants.LOCATION_HAND) ?? Effect(Gate, YgoConstants.LOCATION_SZONE)
                ?? Effect(Solomon, YgoConstants.LOCATION_MZONE);
            if (search.HasValue) return search;
            if (!own.hand.Any(card => card.code == Gate) && !own.szone.Any(card => card?.code == Gate))
            {
                var starter = Normal(Kepler);
                if (starter.HasValue) return starter;
            }

            // Free monsters already available in hand or in the scales can unlock a new line.
            // Resolving the revival first leaves the Fusion monster alive for its follow-up.
            var pendulum = Action(prompt.spSummonable, SpecialSummon, item =>
                (item.location is YgoConstants.LOCATION_SZONE or YgoConstants.LOCATION_PZONE) &&
                IsSet(item.code, DdSet) && CardDatabase.Get(item.code)?.IsPendulum == true);
            if (pendulum.HasValue) return pendulum;
            var extension = Effect(SwirlSlime, YgoConstants.LOCATION_GRAVE) ??
                Effect(Griffin, YgoConstants.LOCATION_HAND, 1) ??
                Effect(ScaleSurveyor, YgoConstants.LOCATION_HAND, 0);
            if (extension.HasValue) return extension;

            // Clear a visible opposing monster once a spare D/D can pay Ragnarok's cost.
            if (opponentHasMonster && hasSpareBody)
            {
                var removal = Effect(Ragnarok, YgoConstants.LOCATION_MZONE, 2);
                if (removal.HasValue) return removal;
            }

            // Use the Rank 4 to find a Contract before committing it as material.
            if (!hasContract && Special(Solomon) is int contractSearch) return contractSearch;

            // Link into scales before overlaying the same bodies into an end-board monster.
            if (openScales && own.deckCount >= 2 && own.lp > 1000 &&
                !board.Any(card => card.code == Gilgamesh) && (hasPendulumTargets || hasLinkMaterialToRecover))
            {
                var openPendulumLine = Special(Gilgamesh);
                if (openPendulumLine.HasValue) return openPendulumLine;
            }

            // Rebuild missing bodies from hand or grave. The native prompt reflects interruptions
            // and summon locks, so an effect that failed does not make later steps mandatory.
            if (!hasFusion || !hasSpareBody)
            {
                var fusionFromHand = Effect(SwirlSlime, YgoConstants.LOCATION_HAND);
                if (fusionFromHand.HasValue) return fusionFromHand;
            }

            var normal = Normal(Copernicus) ?? Normal(Kepler);
            if (normal.HasValue) return normal;

            var extensionFromGrave = Effect(NecroSlime, YgoConstants.LOCATION_GRAVE) ??
                Effect(SwirlSlime, YgoConstants.LOCATION_HAND) ??
                Effect(SwampKing, YgoConstants.LOCATION_SZONE) ??
                Effect(SwampKing, YgoConstants.LOCATION_HAND);
            if (extensionFromGrave.HasValue) return extensionFromGrave;

            var buildScaleFromHand = Action(prompt.activatable, Activate, item =>
                item.location == YgoConstants.LOCATION_HAND && item.description == PlacePendulumScale &&
                CanBuildUsefulScales(own, item, left, right));
            if (buildScaleFromHand.HasValue) return buildScaleFromHand;

            // Choose interaction that addresses the current board, and check its costs.
            var siegfried = Special(Siegfried);
            var machinex = Special(Machinex);
            bool canPayForMachinex = hasContract || board.Any(card =>
                IsSet(card.code, DddSet) && card.overlayCodes.Count >= 1);
            if (opponentHasBackrow && siegfried.HasValue && !board.Any(card => card.code == Siegfried))
                return siegfried;
            if (machinex.HasValue && canPayForMachinex && (opponentHasMonster || !siegfried.HasValue))
                return machinex;
            if (siegfried.HasValue && !board.Any(card => card.code == Siegfried)) return siegfried;

            // A one-material Machinex without a Contract cannot use its interaction. If
            // overlaying now is the only move left, preserve the established D/D board.
            if (machinex.HasValue && !canPayForMachinex && hasDdd && prompt.canEp && !prompt.canBp &&
                prompt.spSummonable.All(item => item.code == Machinex || !canUse(SpecialSummon, item)) &&
                !prompt.summonable.Any() && !prompt.sSetable.Any() &&
                !prompt.activatable.Any(item => canUse(Activate, item)))
                return 7;
            return null;
        }

        internal static int? ChooseCard(DuelSession session, DuelPrompt prompt)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_CARD || prompt.min != 1 || prompt.max != 1 ||
                prompt.candidates.Count == 0 || !IsDddDeck(session.Players[prompt.playerId]))
                return null;

            var source = OwnChainSource(session, prompt.playerId);
            if (source == null) return null;
            var own = session.Players[prompt.playerId];
            bool OnlyFrom(uint location) => prompt.candidates.All(card =>
                card.controller == prompt.playerId && (card.location & ~YgoConstants.LOCATION_OVERLAY) == location);
            bool HasInHand(uint code) => own.hand.Any(card => card.code == code);
            bool OnField(uint code) => own.mzone.Any(card => card?.IsFaceUp == true && card.code == code);
            int? Find(uint code) => FindIndex(prompt.candidates, card => card.code == code);

            if (source.code is Gate or Solomon or CountSurveyor && OnlyFrom(YgoConstants.LOCATION_DECK))
            {
                bool hasDddBody = own.mzone.Any(card => card != null && card.IsFaceUp && IsSet(card.code, DdSet));
                bool hasPendulum = own.mzone.Concat(own.szone).Any(card =>
                    card?.IsFaceUp == true && CardDatabase.Get(card.code)?.IsPendulum == true && IsSet(card.code, DdSet));
                if (hasPendulum && !HasInHand(ScaleSurveyor) && !OnField(ScaleSurveyor) && Find(ScaleSurveyor) is int freeBody)
                    return freeBody;
                if (hasDddBody && !HasInHand(Griffin))
                {
                    var extender = Find(Griffin);
                    if (extender.HasValue) return extender;
                }
                if (!HasInHand(SwirlSlime) && own.hand.Any(card => IsSet(card.code, DdSet)) &&
                    own.extra.Any(card => card.code == Genghis))
                {
                    var starter = Find(SwirlSlime);
                    if (starter.HasValue) return starter;
                }
                if (!HasInHand(CountSurveyor) && !OnField(CountSurveyor) &&
                    own.hand.Any(card => IsSet(card.code, DdSet)) && Find(CountSurveyor) is int deployable)
                    return deployable;
                if (!HasInHand(Copernicus) && Find(Copernicus) is int millStarter) return millStarter;
                if (source.code == Solomon && !HasInHand(Gate) && !own.szone.Any(card => card?.IsFaceUp == true && card.code == Gate))
                    return Find(Gate) ?? Find(Kepler);
                return Find(Kepler);
            }

            if (source.code == SpiritKing && OnlyFrom(YgoConstants.LOCATION_DECK))
                return (!OnField(CountSurveyor) ? Find(CountSurveyor) : null) ?? Find(Kepler) ?? Find(ScaleSurveyor);

            if (source.code == Kepler && OnlyFrom(YgoConstants.LOCATION_DECK) &&
                prompt.candidates.All(card => IsDarkContract(card.code)))
            {
                bool gateAlreadyAvailable = HasInHand(Gate) || own.szone.Any(card => card?.code == Gate);
                if (!gateAlreadyAvailable) return Find(Gate) ?? Find(SpiritKing) ?? Find(SwampKing);
                if (!HasInHand(SpiritKing) && !own.szone.Any(card => card?.code == SpiritKing) && Find(SpiritKing) is int deployFromDeck)
                    return deployFromDeck;
                if (!HasInHand(SwampKing) && !own.szone.Any(card => card?.code == SwampKing))
                    return Find(SwampKing);
            }

            if (source.code == Copernicus && OnlyFrom(YgoConstants.LOCATION_DECK) &&
                prompt.candidates.All(card => IsSet(card.code, DdSet) || IsDarkContract(card.code)))
            {
                if (own.grave.Any(card => IsSet(card.code, DdSet) && card.code != NecroSlime) &&
                    !own.grave.Any(card => card.code == NecroSlime))
                    return Find(NecroSlime) ?? Find(SwirlSlime);
                if (own.hand.Any(card => IsSet(card.code, DdSet)))
                    return Find(SwirlSlime) ?? Find(NecroSlime);
            }

            bool fusionEffect = source.code is SwirlSlime or NecroSlime or SwampKing;
            if (fusionEffect && OnlyFrom(YgoConstants.LOCATION_EXTRA))
            {
                bool alreadyHasReviver = own.mzone.Any(card => card != null && card.IsFaceUp && card.code == Genghis);
                return alreadyHasReviver ? Find(HighGenghis) ?? Find(Genghis) : Find(Genghis) ?? Find(HighGenghis);
            }

            if (source.code == SwirlSlime && source.triggerLoc == YgoConstants.LOCATION_GRAVE &&
                OnlyFrom(YgoConstants.LOCATION_HAND))
                return Find(Ragnarok) ?? Find(Copernicus) ?? Find(Griffin) ?? Find(Kepler);

            if ((source.code is Genghis or HighGenghis or Ragnarok) && OnlyFrom(YgoConstants.LOCATION_GRAVE))
                return Find(Ragnarok) ?? Find(Griffin) ?? Find(Lamia) ?? Find(Copernicus);

            return null;
        }

        internal static int? ChooseOption(DuelSession session, DuelPrompt prompt)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_OPTION || prompt.options.Count != 2)
                return null;
            int search = prompt.options.FindIndex(description => IsDescription(description, Kepler, 3));
            if (search < 0 || !IsDescription(prompt.options[1 - search], Kepler, 2))
                return null;

            var own = session.Players[prompt.playerId];
            bool Available(uint code) => own.hand.Any(card => card.code == code) || own.szone.Any(card => card?.code == code);
            bool canUseModernContract = own.extra.Concat(own.mzone).Concat(own.grave).Any(card => card?.code == Solomon);
            return IsDddDeck(own) &&
                (!Available(Gate) || (canUseModernContract && !Available(SwampKing) && !Available(SpiritKing))) ? search : null;
        }

        internal static int? ChooseScale(DuelSession session, DuelPrompt prompt)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_UNSELECT_CARD || prompt.finishable ||
                prompt.min != 2 || prompt.max != 2 || prompt.unselectCandidates.Count > 1 ||
                prompt.candidates.Count == 0)
                return null;
            var source = OwnChainSource(session, prompt.playerId);
            if (source?.code != Gilgamesh || !IsDescription(source.description, Gilgamesh, 0)) return null;
            bool IsScale(SelectableCardItem item) => item.controller == prompt.playerId &&
                item.location == YgoConstants.LOCATION_DECK && IsSet(item.code, DdSet) &&
                CardDatabase.Get(item.code)?.IsPendulum == true;
            if (!prompt.candidates.All(IsScale) || !prompt.unselectCandidates.All(IsScale)) return null;

            var own = session.Players[prompt.playerId];
            var levels = own.hand.Concat(own.extra.Where(card => card.IsFaceUp))
                .Where(card => IsLevelMonster(card.code))
                .Select(card => CardDatabase.Get(card.code).level)
                .ToArray();

            int best = -1, mostReachable = -1, widest = -1;
            for (int i = 0; i < prompt.candidates.Count; i++)
            {
                var candidate = prompt.candidates[i];
                IEnumerable<SelectableCardItem> partners = prompt.unselectCandidates.Count == 0
                    ? prompt.candidates : prompt.unselectCandidates;
                foreach (var partner in partners)
                {
                    if (candidate.code == partner.code) continue;
                    int a = (int)CardDatabase.Get(candidate.code).lscale;
                    int b = (int)CardDatabase.Get(partner.code).lscale;
                    int reachable = levels.Count(level => IsBetween(level, a, b));
                    int width = Math.Abs(a - b);
                    if (reachable < mostReachable || reachable == mostReachable && width <= widest) continue;
                    best = i;
                    mostReachable = reachable;
                    widest = width;
                }
            }
            return best < 0 ? null : best;
        }

        private static bool CanBuildUsefulScales(PlayerFieldSnapshot own, SelectableCardItem item, LiveCardState left, LiveCardState right)
        {
            var card = CardDatabase.Get(item.code);
            if (card?.IsPendulum != true || !IsSet(item.code, DdSet) || left != null && right != null) return false;
            var existing = left ?? right;
            if (existing != null && (!existing.IsFaceUp || CardDatabase.Get(existing.code)?.IsPendulum != true))
                return false;

            bool CanSummonWith(int first, int second, LiveCardState partner)
            {
                return own.hand.Any(target => target.sequence != item.sequence && target != partner &&
                        IsLevelMonster(target.code) && IsBetween(CardDatabase.Get(target.code).level, first, second)) ||
                    own.extra.Any(target => target.IsFaceUp && IsLevelMonster(target.code) &&
                        IsBetween(CardDatabase.Get(target.code).level, first, second));
            }

            if (existing != null)
            {
                int first = left != null ? (int)existing.lscale : (int)card.lscale;
                int second = right != null ? (int)existing.rscale : (int)card.rscale;
                return CanSummonWith(first, second, null);
            }
            return own.hand.Any(partner => partner.sequence != item.sequence &&
                IsSet(partner.code, DdSet) && CardDatabase.Get(partner.code)?.IsPendulum == true &&
                CanSummonWith((int)card.lscale, (int)CardDatabase.Get(partner.code).rscale, partner));
        }

        private static bool IsBetween(uint level, int a, int b) => level > Math.Min(a, b) && level < Math.Max(a, b);

        private static bool IsLevelMonster(uint code) =>
            CardDatabase.Get(code) is CardInfo info && info.IsMonster && !info.IsXyz && !info.IsLink && info.level != 0 && IsSet(code, DdSet);

        internal static bool IsDarkContract(uint code) => IsSet(code, DarkContractSet);

        private static bool IsSet(uint code, uint target)
        {
            ulong sets = CardDatabase.Get(code)?.setcode ?? 0;
            uint subtype = target & 0xf000;
            while (sets != 0)
            {
                uint entry = (uint)(sets & 0xffff);
                if ((entry & 0x0fff) == (target & 0x0fff) && (entry & subtype) == subtype)
                    return true;
                sets >>= 16;
            }
            return false;
        }

        internal static bool IsDddDeck(PlayerFieldSnapshot own) =>
            own.extra.Count(card => IsSet(card.code, DddSet)) >= 2 &&
            (own.extra.Any(card => card.code is Gilgamesh or Machinex) ||
             own.mzone.Any(card => card != null && card.IsFaceUp && (card.code is Gilgamesh or Machinex)));

        // Some target prompts arrive before the effect appears in the chain.
        // Keep the generic chooser whenever the engine has not identified a unique source.
        private static ChainLinkInfo OwnChainSource(DuelSession session, byte player)
        {
            var link = session.SelectionChainLink;
            return link != null && link.triggerPlayer == player && !link.negated ? link : null;
        }

        private static bool IsDescription(ulong description, uint code, int index) =>
            CardDatabase.GetDescriptionCardCode(description) == code && (description & DescriptionIndexMask) == (uint)index;

        private static int? FindIndex(List<SelectableCardItem> items, Predicate<SelectableCardItem> predicate)
        {
            int index = items.FindIndex(predicate);
            return index < 0 ? null : index;
        }
    }
}
