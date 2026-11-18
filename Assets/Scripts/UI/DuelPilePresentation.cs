using System;
using System.Collections.Generic;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DuelFieldUI
    {
        private sealed class BanishedPilePresentation
        {
            internal readonly List<LiveCardState> Shown;
            internal readonly List<LiveCardState> Resolved;
            internal int Pending;

            internal BanishedPilePresentation(IReadOnlyList<LiveCardState> cards)
            {
                Shown = new List<LiveCardState>(cards);
                Resolved = new List<LiveCardState>(cards);
            }
        }

        private readonly Dictionary<byte, BanishedPilePresentation> _banishedPilePresentation = new();
        private uint? _inspectedBanishedCard;
        private uint? _zoomBanishedCard;

        private void FollowTemporaryBanishment(CardMoveEvent move)
        {
            if (move.TemporaryBanishCardId is not uint id) return;
            if (move.ReturnEffectId.HasValue)
            {
                var destination = (move.toCon, move.toLoc, move.toSeq);
                if (_inspectedBanishedCard == id && _inspectedCode == move.code)
                {
                    _inspectedFieldCard = destination;
                    _inspectedBanishedCard = null;
                }
                if (_zoomBanishedCard == id && _zoomCardView.CurrentCode == move.code)
                {
                    _zoomFieldCard = destination;
                    _zoomBanishedCard = null;
                }
                return;
            }
            if (move.toLoc != YgoConstants.LOCATION_REMOVED) return;
            var origin = (move.fromCon, move.fromLoc, move.fromSeq);
            if (_inspectedFieldCard == origin && _inspectedCode == move.code) _inspectedBanishedCard = id;
            else if (_inspectedBanishedCard == id) _inspectedBanishedCard = null;
            if (_zoomFieldCard == origin && _zoomCardView.CurrentCode == move.code) _zoomBanishedCard = id;
            else if (_zoomBanishedCard == id) _zoomBanishedCard = null;
        }

        private IReadOnlyList<LiveCardState> DisplayedBanishedCards(byte controller) =>
            _banishedPilePresentation.TryGetValue(controller, out var pile) ? pile.Shown : _session.Players[controller].banished;

        private BanishedPilePresentation PendingBanishedPile(byte controller)
        {
            if (!_banishedPilePresentation.TryGetValue(controller, out var pile))
            {
                pile = new BanishedPilePresentation(_session.Players[controller].banished);
                _banishedPilePresentation.Add(controller, pile);
            }
            return pile;
        }

        private Action StageBanishedDeparture(CardMoveEvent move)
        {
            if (move.fromLoc != YgoConstants.LOCATION_REMOVED) return null;
            var pile = PendingBanishedPile(move.fromCon);
            if (move.fromSeq >= pile.Resolved.Count ||
                move.code != 0 && pile.Resolved[(int)move.fromSeq].code != move.code)
            {
                _banishedPilePresentation.Remove(move.fromCon);
                return null;
            }
            // Native indices advance as cards move. The presented copy is bound to the original object.
            var card = pile.Resolved[(int)move.fromSeq];
            pile.Resolved.RemoveAt((int)move.fromSeq);
            pile.Pending++;
            return () =>
            {
                pile.Shown.Remove(card);
                CompleteBanishedMove(move.fromCon, pile);
            };
        }

        private Action ReserveMoveArrival(CardMoveEvent move)
        {
            if (move.toLoc == 0) return null;
            if (move.toLoc != YgoConstants.LOCATION_REMOVED)
                return ReserveCardArrival(move.toCon, move.toLoc, move.toSeq);

            var pile = PendingBanishedPile(move.toCon);
            var entry = new LiveCardState
            {
                code = move.code,
                controller = move.toCon,
                location = move.toLoc,
                sequence = move.toSeq,
                position = move.toPos,
            };
            int index = (int)Math.Min(move.toSeq, (uint)pile.Resolved.Count);
            pile.Resolved.Insert(index, entry);
            pile.Pending++;
            return () =>
            {
                pile.Shown.Insert(Math.Min(index, pile.Shown.Count), entry);
                CompleteBanishedMove(move.toCon, pile);
            };
        }

        private void CompleteBanishedMove(byte controller, BanishedPilePresentation pile)
        {
            if (--pile.Pending == 0 && _banishedPilePresentation.TryGetValue(controller, out var current) && ReferenceEquals(current, pile))
                _banishedPilePresentation.Remove(controller);
        }
    }
}
