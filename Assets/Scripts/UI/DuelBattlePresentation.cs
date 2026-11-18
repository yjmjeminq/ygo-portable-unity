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
            internal bool Hidden;
            internal bool HideReadout;
        }

        // The engine can resolve a whole battle before its queued animations have played.
        private readonly Dictionary<(byte Controller, uint Location, uint Sequence), Queue<BattleCardDisplay>> _battleCardDisplays = new();

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
            if (display == null || !_battleCardDisplays.TryGetValue(display.Slot, out var cards) ||
                !ReferenceEquals(cards.Peek(), display)) return;
            cards.Dequeue();
            if (cards.Count == 0) _battleCardDisplays.Remove(display.Slot);
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

        private BattleCardDisplay HoldMovingFieldCard(CardMoveEvent move, CardMotion motion,
            PresentedFieldReadout readout, bool paired)
        {
            var battle = HoldBattleMove(move, motion);
            if (battle != null) return battle;
            if (paired || IsArriving(move.fromCon, move.fromLoc, move.fromSeq)) return null;
            var source = move.SourceReadout;
            if (source == null && move.DestructionGroup != null &&
                (move.fromLoc is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE) &&
                (move.fromPos & YgoConstants.POS_FACEDOWN) != 0)
                source = new LiveCardState { position = move.fromPos };
            if (source == null) return null;

            var display = QueueBattleCardDisplay(move.fromCon, move.fromLoc, move.fromSeq, source, move.fromPos);
            if (source.IsFaceUp) display.Readout = readout ?? new PresentedFieldReadout { Shown = source };
            return display;
        }

        private bool IsBattling(byte controller, uint sequence) => _session.HasActiveAttack &&
            (_session.AttackerLoc == YgoConstants.LOCATION_MZONE && controller == _session.AttackerCon && sequence == _session.AttackerSeq ||
             _session.TargetLoc == YgoConstants.LOCATION_MZONE && controller == _session.TargetCon && sequence == _session.TargetSeq);
    }
}
