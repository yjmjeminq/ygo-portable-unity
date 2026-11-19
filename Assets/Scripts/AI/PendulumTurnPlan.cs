using System;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal static class PendulumTurnPlan
    {
        private const ulong PlaceScale = 1160;
        private const int SpecialSummon = 1;
        private const int Activate = 5;
        private const uint Pendulumgraph = 66425726;

        internal static int? ChooseIdle(DuelSession session, DuelPrompt prompt, Func<int, SelectableCardItem, bool> canUse)
        {
            var own = session.Players[prompt.playerId];
            var left = own.szone[6] ?? own.szone[0];
            var right = own.szone[7] ?? own.szone[4];
            bool leftReady = IsActiveScale(left);
            bool rightReady = IsActiveScale(right);
            if (leftReady && rightReady)
            {
                int summon = prompt.spSummonable.FindIndex(item =>
                    item.controller == prompt.playerId &&
                    (item.location is YgoConstants.LOCATION_SZONE or YgoConstants.LOCATION_PZONE) &&
                    CardDatabase.Get(item.code)?.IsPendulum == true && canUse(SpecialSummon, item));
                return summon < 0 ? null : (summon << 16) | SpecialSummon;
            }

            if (left != null && !leftReady || right != null && !rightReady) return null;

            var scaleMoves = prompt.activatable
                .Select((item, index) => (item, index, card: CardDatabase.Get(item.code)))
                .Where(move => move.item.controller == prompt.playerId && move.item.location == YgoConstants.LOCATION_HAND &&
                    move.item.description == PlaceScale && move.card?.IsPendulum == true && canUse(Activate, move.item))
                .ToArray();
            if (scaleMoves.Length == 0 || (left == null && right == null && scaleMoves.Length < 2)) return null;

            var rival = session.Players[1 - prompt.playerId];
            bool extraZoneOpen = own.mzone[5] == null && rival.mzone[6] == null ||
                own.mzone[6] == null && rival.mzone[5] == null;
            int mainSlots = own.mzone.Take(5).Count(card => card == null);
            var hand = own.hand
                .Select(live => (live.sequence, card: CardDatabase.Get(live.code)))
                .Where(candidate => HasReachableLevel(candidate.card, fromHand: true))
                .ToArray();
            var faceUpExtra = extraZoneOpen
                ? own.extra.Where(live => live.IsFaceUp).Select(live => CardDatabase.Get(live.code))
                    .Where(card => HasReachableLevel(card, fromHand: false) && !card.IsExtra).ToArray()
                : Array.Empty<CardInfo>();

            int Reachable(uint first, uint second, uint firstScaleHandSequence, uint secondScaleHandSequence = uint.MaxValue)
            {
                bool Fits((uint sequence, CardInfo card) candidate) => candidate.sequence != firstScaleHandSequence &&
                    candidate.sequence != secondScaleHandSequence && WithinScales(candidate.card.level, first, second);
                int handCount = hand.Count(Fits);
                int extraCount = faceUpExtra.Any(card => WithinScales(card.level, first, second)) ? 1 : 0;
                // Pendulumgraph can be summoned from the hand and gives live Spell negation on its own.
                int interruption = mainSlots > 0 && hand.Any(candidate => candidate.card.code == Pendulumgraph && Fits(candidate)) ? 1 : 0;
                return Math.Min(mainSlots, handCount) + extraCount + interruption;
            }

            int? next = null;
            int mostReachable = (leftReady || rightReady) ? 0 : 1;
            foreach (var move in scaleMoves)
            {
                if (leftReady || rightReady)
                {
                    uint first = leftReady ? left.lscale : move.card.lscale;
                    uint second = rightReady ? right.rscale : move.card.rscale;
                    int reachable = Reachable(first, second, move.item.sequence);
                    if (reachable <= mostReachable) continue;
                    next = (move.index << 16) | Activate;
                    mostReachable = reachable;
                    continue;
                }

                foreach (var partner in scaleMoves)
                {
                    if (partner.item.sequence == move.item.sequence) continue;
                    int reachableWithLeft = Reachable(move.card.lscale, partner.card.rscale,
                        move.item.sequence, partner.item.sequence);
                    int reachableWithRight = Reachable(partner.card.lscale, move.card.rscale,
                        move.item.sequence, partner.item.sequence);
                    // Either zone can be the first legal Pendulum location.
                    int reachable = Math.Min(reachableWithLeft, reachableWithRight);
                    if (reachable <= mostReachable) continue;
                    next = (move.index << 16) | Activate;
                    mostReachable = reachable;
                }
            }
            return next;
        }

        private static bool IsActiveScale(LiveCardState card) =>
            card != null && card.IsFaceUp && !card.effectsDisabled && CardDatabase.Get(card.code)?.IsPendulum == true;

        internal static int ReadyFollowUps(PlayerFieldSnapshot own)
        {
            var left = own.szone[6] ?? own.szone[0];
            var right = own.szone[7] ?? own.szone[4];
            if (!IsActiveScale(left) || !IsActiveScale(right)) return 0;
            bool Fits(LiveCardState live, bool fromHand) => CardDatabase.Get(live.code) is CardInfo card &&
                HasReachableLevel(card, fromHand) && WithinScales(card.level, left.lscale, right.rscale);
            return own.hand.Count(card => Fits(card, fromHand: true)) +
                own.extra.Count(card => card.IsFaceUp && Fits(card, fromHand: false));
        }

        internal static bool CanRebuildEqualScales(PlayerFieldSnapshot own, PlayerFieldSnapshot rival,
            LiveCardState left, LiveCardState right)
        {
            var hand = own.hand.Select(live => CardDatabase.Get(live.code)).ToArray();
            if (hand.Count(card => card?.IsPendulum == true) < 2) return false;

            bool mainZoneOpen = own.mzone.Take(5).Any(card => card == null);
            bool extraZoneOpen = own.mzone[5] == null && rival.mzone[6] == null ||
                own.mzone[6] == null && rival.mzone[5] == null;
            var extra = extraZoneOpen
                ? own.extra.Where(card => card.IsFaceUp).Append(left).Append(right)
                    .Select(live => CardDatabase.Get(live.code))
                    .Where(card => HasReachableLevel(card, fromHand: false) && !card.IsExtra).ToArray()
                : Array.Empty<CardInfo>();
            for (int first = 0; first < hand.Length; first++)
            {
                if (hand[first]?.IsPendulum != true) continue;
                for (int second = 0; second < hand.Length; second++)
                {
                    if (second == first || hand[second]?.IsPendulum != true) continue;
                    uint lowSide = hand[first].lscale, highSide = hand[second].rscale;
                    if (extra.Any(card => WithinScales(card.level, lowSide, highSide))) return true;
                    if (mainZoneOpen && hand.Where((card, index) => index != first && index != second)
                        .Any(card => HasReachableLevel(card, fromHand: true) && WithinScales(card.level, lowSide, highSide)))
                        return true;
                }
            }
            return false;
        }

        private static bool HasReachableLevel(CardInfo card, bool fromHand) => card != null &&
            card.IsMonster && !card.IsLink && !card.IsXyz && card.level > 0 &&
            ((fromHand && card.code == Pendulumgraph) ||
                (card.type & (YgoConstants.TYPE_RITUAL | YgoConstants.TYPE_SPSUMMON | YgoConstants.TYPE_TOKEN)) == 0);

        private static bool WithinScales(uint level, uint first, uint second) =>
            level > Math.Min(first, second) && level < Math.Max(first, second);
    }
}
