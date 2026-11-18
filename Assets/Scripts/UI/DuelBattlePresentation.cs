using System.Collections.Generic;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DuelFieldUI
    {
        private sealed class BattleCardDisplay
        {
            internal (byte Controller, uint Sequence) Slot;
            internal LiveCardState Card;
            internal uint Position;
            internal BattleCardStats? Stats;
            internal bool UseBattleStats;
            internal bool Hidden;
            internal bool HideReadout;
        }

        // The engine can resolve a whole battle before its queued animations have played.
        private readonly Dictionary<(byte Controller, uint Sequence), Queue<BattleCardDisplay>> _battleCardDisplays = new();

        private BattleCardDisplay GetBattleCardDisplay(byte controller, uint sequence) =>
            _battleCardDisplays.TryGetValue((controller, sequence), out var cards) ? cards.Peek() : null;

        private BattleCardDisplay QueueBattleCardDisplay(byte controller, uint sequence, LiveCardState card, uint position)
        {
            if (card == null) return null;
            var key = (controller, sequence);
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
            var display = QueueBattleCardDisplay(stats.Controller, stats.Sequence, card, position);
            display.Stats = stats;
            return display;
        }

        private BattleCardDisplay HoldBattleMove(CardMoveEvent move, CardMotion motion)
        {
            if (move.fromLoc != YgoConstants.LOCATION_MZONE) return null;
            bool battleDestruction = motion == CardMotion.Destroy && (move.reason & YgoConstants.REASON_BATTLE) != 0;
            if (!battleDestruction && (motion != CardMotion.ChangePosition || !IsBattling(move.fromCon, move.fromSeq)))
                return null;

            var key = (move.fromCon, move.fromSeq);
            var previous = _battleCardDisplays.TryGetValue(key, out var cards) ? cards.Last() : null;
            var card = previous?.Card ?? FindLiveCard(move.fromCon, move.fromLoc, move.fromSeq);
            if (card == null || move.code != 0 && card.code != move.code) return null;

            var display = QueueBattleCardDisplay(move.fromCon, move.fromSeq, card, move.fromPos);
            if (battleDestruction && previous?.Stats is { } stats)
            {
                display.Stats = stats;
                display.UseBattleStats = true;
            }
            return display;
        }

        private bool IsBattling(byte controller, uint sequence) => _session.HasActiveAttack &&
            (_session.AttackerLoc == YgoConstants.LOCATION_MZONE && controller == _session.AttackerCon && sequence == _session.AttackerSeq ||
             _session.TargetLoc == YgoConstants.LOCATION_MZONE && controller == _session.TargetCon && sequence == _session.TargetSeq);
    }
}
