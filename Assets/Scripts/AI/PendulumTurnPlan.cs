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
                .Where(candidate => HasReachableLevel(candidate.card))
                .ToArray();
            var faceUpExtra = extraZoneOpen
                ? own.extra.Where(live => live.IsFaceUp).Select(live => CardDatabase.Get(live.code))
                    .Where(card => HasReachableLevel(card) && !card.IsExtra).ToArray()
                : Array.Empty<CardInfo>();

            int Reachable(uint first, uint second, uint firstScaleHandSequence, uint secondScaleHandSequence = uint.MaxValue)
            {
                int handCount = hand.Count(candidate => candidate.sequence != firstScaleHandSequence &&
                    candidate.sequence != secondScaleHandSequence && WithinScales(candidate.card.level, first, second));
                int extraCount = faceUpExtra.Any(card => WithinScales(card.level, first, second)) ? 1 : 0;
                return Math.Min(mainSlots, handCount) + extraCount;
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
            card != null && card.IsFaceUp && CardDatabase.Get(card.code)?.IsPendulum == true;

        private static bool HasReachableLevel(CardInfo card) => card != null &&
            card.IsMonster && !card.IsLink && !card.IsXyz && card.level > 0 &&
            (card.type & (YgoConstants.TYPE_RITUAL | YgoConstants.TYPE_SPSUMMON | YgoConstants.TYPE_TOKEN)) == 0;

        private static bool WithinScales(uint level, uint first, uint second) =>
            level > Math.Min(first, second) && level < Math.Max(first, second);
    }
}
