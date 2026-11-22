using System.Collections.Generic;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DuelFieldUI
    {
        private sealed class BattleCardDisplay
        {
            internal (byte Controller, uint Location, uint Sequence) Slot;
            internal LiveCardState Card;
            internal uint Position;
            internal BattleCardStats? Stats;
            internal PresentedFieldReadout Readout;
            internal bool UseBattleStats;
            internal bool UsePrintedReadout;
            internal bool Hidden;
            internal bool AwaitingArrival;
            internal bool Released;
            internal bool HideReadout;
            internal bool InspectWhileHidden;
            internal bool PositionChanging;
            internal bool Visible => !Hidden && !AwaitingArrival;
        }

        // The engine can resolve a whole battle before its queued animations have played.
        private readonly Dictionary<(byte Controller, uint Location, uint Sequence), Queue<BattleCardDisplay>> _battleCardDisplays = new();
        private readonly Dictionary<(byte Controller, uint Location, uint Sequence), MovingFieldInspection> _movingInspections = new();

        private sealed class MovingFieldInspection
        {
            internal CardMoveReadoutSnapshot Move;
            internal PresentedFieldReadout Waiting;
            internal bool AtDestination;

            internal LiveCardState Shown => AtDestination
                ? Move.DestinationCard ?? Move.SourceCard
                : Waiting?.Shown ?? Move.SourceCard;
        }

        private bool TryQueueFieldSwap(CardMoveEvent first, CardMoveEvent second, PresentedFieldReadout firstReadout,
            PresentedFieldReadout secondReadout)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null || !Root.activeInHierarchy ||
                !TryGetSwapFlight(first, out var firstFlight) || !TryGetSwapFlight(second, out var secondFlight))
                return false;

            var firstHeld = HoldFieldCard(first, firstReadout);
            var secondHeld = HoldFieldCard(second, secondReadout);
            var firstMove = PrepareMoveReadout(animator, first, firstFlight.StartsFaceDown, firstFlight.EndsFaceDown, firstHeld);
            var secondMove = PrepareMoveReadout(animator, second, secondFlight.StartsFaceDown, secondFlight.EndsFaceDown, secondHeld);
            var firstInspection = TrackMovingInspection(first, firstMove, firstHeld?.Readout);
            var secondInspection = TrackMovingInspection(second, secondMove, secondHeld?.Readout);
            var releaseFirst = ReserveCardArrival(first.toCon, first.toLoc, first.toSeq);
            var releaseSecond = ReserveCardArrival(second.toCon, second.toLoc, second.toSeq);

            animator.QueueFieldSwap((firstFlight, firstMove), (secondFlight, secondMove), () =>
            {
                if (firstHeld != null) firstHeld.Hidden = true;
                if (secondHeld != null) secondHeld.Hidden = true;
                if (firstInspection != null) firstInspection.Waiting = null;
                if (secondInspection != null) secondInspection.Waiting = null;
                Refresh();
            }, () =>
            {
                if (firstInspection != null) firstInspection.AtDestination = true;
                if (secondInspection != null) secondInspection.AtDestination = true;
                RefreshInspectorReadout();
                if (_cardZoomModal.activeSelf) RefreshZoomReadout(_zoomCardView.CurrentCode);
            }, () =>
            {
                ReleaseBattleCardDisplay(firstHeld);
                ReleaseBattleCardDisplay(secondHeld);
                releaseFirst();
                releaseSecond();
                ReleaseMovingInspection(first, firstInspection);
                ReleaseMovingInspection(second, secondInspection);
                Refresh();
            });
            return true;
        }

        private bool TryGetSwapFlight(CardMoveEvent move, out CardFlight flight)
        {
            flight = default;
            if (move.fromLoc is not (YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE) ||
                move.toLoc is not (YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE))
                return false;
            var from = GetDisplayedCardPose(move.fromCon, move.fromLoc, move.fromSeq, move.code, move.fromPos);
            if (!from.IsValid) from = GetCardPose(move.fromCon, move.fromLoc, move.fromSeq, move.fromPos);
            var to = GetCardPose(move.toCon, move.toLoc, move.toSeq, move.toPos);
            if (!from.IsValid || !to.IsValid) return false;
            flight = new CardFlight(move.code, from, to,
                move.code == 0 || IsFaceDown(move.fromCon, move.fromLoc, move.fromPos),
                move.code == 0 || IsFaceDown(move.toCon, move.toLoc, move.toPos));
            return true;
        }

        private MovingFieldInspection TrackMovingInspection(CardMoveEvent move, CardMoveReadoutSnapshot readout,
            PresentedFieldReadout waiting)
        {
            if (readout == null || move.toLoc != YgoConstants.LOCATION_MZONE ||
                (move.toPos & YgoConstants.POS_FACEUP) == 0) return null;
            var inspection = new MovingFieldInspection { Move = readout, Waiting = waiting };
            _movingInspections[(move.toCon, move.toLoc, move.toSeq)] = inspection;
            return inspection;
        }

        private void ReleaseMovingInspection(CardMoveEvent move, MovingFieldInspection inspection)
        {
            var slot = (move.toCon, move.toLoc, move.toSeq);
            if (_movingInspections.TryGetValue(slot, out var shown) && ReferenceEquals(shown, inspection))
                _movingInspections.Remove(slot);
        }

        private BattleCardDisplay GetBattleCardDisplay(byte controller, uint sequence, uint location = YgoConstants.LOCATION_MZONE) =>
            _battleCardDisplays.TryGetValue((controller, location, sequence), out var cards) ? cards.Peek() : null;

        private BattleCardDisplay QueueBattleCardDisplay(byte controller, uint location, uint sequence, LiveCardState card, uint position)
        {
            if (card == null) return null;
            var key = (controller, location, sequence);
            if (!_battleCardDisplays.TryGetValue(key, out var cards))
                _battleCardDisplays.Add(key, cards = new Queue<BattleCardDisplay>());
            var display = new BattleCardDisplay { Slot = key, Card = card, Position = position };
            cards.Enqueue(display);
            return display;
        }

        private void ReleaseBattleCardDisplay(BattleCardDisplay display)
        {
            if (display == null || !_battleCardDisplays.TryGetValue(display.Slot, out var cards)) return;
            display.Released = true;
            while (cards.TryPeek(out var head) && head.Released) cards.Dequeue();
            if (cards.Count == 0) _battleCardDisplays.Remove(display.Slot);
        }

        private void ReleaseDepartingFieldCard(CardMoveEvent move, BattleCardDisplay display)
        {
            if (display?.Readout != null && move.toLoc is not (YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE))
            {
                if (_inspectedFieldCard == display.Slot && _inspectedCode == move.code) _inspectedFieldCard = null;
                if (_zoomFieldCard == display.Slot && _zoomCardView.CurrentCode == move.code) _zoomFieldCard = null;
            }
            ReleaseBattleCardDisplay(display);
        }

        private BattleCardDisplay QueueBattleCard(BattleCardStats stats, LiveCardState card)
        {
            if (card == null || stats.Location != YgoConstants.LOCATION_MZONE) return null;
            uint position = GetBattleCardDisplay(stats.Controller, stats.Sequence) != null ? stats.Position : card.position;
            var display = QueueBattleCardDisplay(stats.Controller, stats.Location, stats.Sequence, card, position);
            display.Stats = stats;
            return display;
        }

        private BattleCardDisplay HoldBattleMove(CardMoveEvent move, CardMotion motion)
        {
            if (move.fromLoc != YgoConstants.LOCATION_MZONE) return null;
            bool battleDestruction = motion == CardMotion.Destroy && (move.reason & YgoConstants.REASON_BATTLE) != 0;
            if (!battleDestruction && (motion != CardMotion.ChangePosition || !IsBattling(move.fromCon, move.fromSeq)))
                return null;

            var key = (move.fromCon, move.fromLoc, move.fromSeq);
            var previous = _battleCardDisplays.TryGetValue(key, out var cards) ? cards.Last() : null;
            var card = previous?.Card ?? FindLiveCard(move.fromCon, move.fromLoc, move.fromSeq);
            if (card == null || move.code != 0 && card.code != move.code) return null;

            var display = QueueBattleCardDisplay(move.fromCon, move.fromLoc, move.fromSeq, card, move.fromPos);
            if (battleDestruction && previous?.Stats is { } stats)
            {
                display.Stats = stats;
                display.UseBattleStats = true;
            }
            return display;
        }

        private BattleCardDisplay HoldMovingFieldCard(CardMoveEvent move, CardMotion motion, PresentedFieldReadout readout) =>
            HoldBattleMove(move, motion) ?? HoldFieldCard(move, readout);

        private void QueuePositionChange(CardAnimationManager animator, CardMoveEvent move, CardFlight flight,
            CardMoveReadoutSnapshot readout, BattleCardDisplay held)
        {
            if (held != null)
            {
                held.PositionChanging = true;
                if (move.SourceReadout != null)
                {
                    held.Readout ??= new PresentedFieldReadout { Shown = move.SourceReadout };
                    held.InspectWhileHidden = true;
                }
            }
            var release = ReserveCardArrival(move.toCon, move.toLoc, move.toSeq);
            animator.QueuePositionChange(move.PresentationGroup, flight, readout, () =>
            {
                if (held != null) held.Hidden = true;
            }, () =>
            {
                if (held == null) return;
                var live = flight.EndsFaceDown ? null : readout?.DestinationCard ?? move.DestinationReadout;
                held.InspectWhileHidden = live != null;
                if (live != null) held.Readout = new PresentedFieldReadout { Shown = live };
            }, () =>
            {
                ReleaseBattleCardDisplay(held);
                release();
            }, Refresh);
        }

        private BattleCardDisplay HoldFieldCard(CardMoveEvent move, PresentedFieldReadout readout)
        {
            if (move.fromLoc is not (YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE) || move.fromPos == 0)
                return null;

            var key = (move.fromCon, move.fromLoc, move.fromSeq);
            _arrivingCards.TryGetValue(key, out var arrivals);
            var arrival = arrivals?.Count > 0 ? arrivals[^1] : null;
            var source = move.SourceReadout;
            if (source == null && arrival == null &&
                FindLiveCard(move.fromCon, move.fromLoc, move.fromSeq) is { } live &&
                live.code == move.code && live.position == move.fromPos)
                source = live;

            // Hidden cards need a card back, and a short-lived face-up copy may only have its move packet.
            var display = QueueBattleCardDisplay(move.fromCon, move.fromLoc, move.fromSeq,
                source ?? new LiveCardState { code = move.code, position = move.fromPos }, move.fromPos);
            display.UsePrintedReadout = source == null;
            if (source?.IsFaceUp == true)
            {
                display.Readout = readout ?? new PresentedFieldReadout { Shown = source };
                display.InspectWhileHidden = true;
            }
            if (arrival != null && arrival.Departing == null)
            {
                display.AwaitingArrival = true;
                arrival.Departing = display;
            }
            return display;
        }

        private bool IsBattling(byte controller, uint sequence) => _session.HasActiveAttack &&
            (_session.AttackerLoc == YgoConstants.LOCATION_MZONE && controller == _session.AttackerCon && sequence == _session.AttackerSeq ||
             _session.TargetLoc == YgoConstants.LOCATION_MZONE && controller == _session.TargetCon && sequence == _session.TargetSeq);
    }
}
