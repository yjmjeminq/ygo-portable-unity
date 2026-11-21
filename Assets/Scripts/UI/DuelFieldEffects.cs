using System.Collections.Generic;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DuelFieldUI
    {
        private const uint InfiniteImpermanence = 10045474;
        private (byte Controller, uint Sequence)? _revealedImpermanence;
        private readonly List<(byte Controller, uint Sequence, uint Markers)> _arrivalLinks = new();
        private int _guidedColumn = -1;

        private void ObserveImpermanenceOrigin(CardMoveEvent move)
        {
            if (move == null) return;
            if (_revealedImpermanence is { } slot &&
                (move.fromCon == slot.Controller && move.fromLoc == YgoConstants.LOCATION_SZONE && move.fromSeq == slot.Sequence ||
                 move.toCon == slot.Controller && move.toLoc == YgoConstants.LOCATION_SZONE && move.toSeq == slot.Sequence))
                _revealedImpermanence = null;

            if (move.code == InfiniteImpermanence && move.ChangesPositionInPlace &&
                move.fromLoc == YgoConstants.LOCATION_SZONE && move.fromSeq < 5 &&
                (move.fromPos & YgoConstants.POS_FACEDOWN) != 0 && (move.toPos & YgoConstants.POS_FACEUP) != 0)
                _revealedImpermanence = (move.fromCon, move.fromSeq);
        }

        private void QueueColumnActivation(CardAnimationManager animator, ChainLinkInfo link)
        {
            bool wasSet = _revealedImpermanence == (link.triggerPlayer, link.triggerSeq);
            _revealedImpermanence = null;
            if (!wasSet || link.code != InfiniteImpermanence || link.triggerLoc != YgoConstants.LOCATION_SZONE) return;

            int column = link.triggerPlayer == _session.HumanPlayerIndex ? (int)link.triggerSeq : 4 - (int)link.triggerSeq;
            var upper = _linkGrid[column, 0].root;
            var lower = _linkGrid[column, 4].root;
            animator.QueueFieldRays(upper, new[] { lower }, DuelEffectKind.ColumnRay, new Color(.74f, .41f, 1f));
        }

        private void QueueLinkArrivalRays(CardAnimationManager animator, byte controller, uint sequence, uint markers)
        {
            var source = GetSlot(controller, YgoConstants.LOCATION_MZONE, sequence);
            if (source?.linkCell is not { } origin) return;
            var targets = new List<RectTransform>(3);
            int facing = controller == _session.HumanPlayerIndex ? 1 : -1;
            foreach (var arrow in DuelLinkCompass.Marks)
            {
                if ((markers & arrow.mask) == 0) continue;
                var target = origin + new Vector2Int(arrow.direction.x * facing, -arrow.direction.y * facing);
                if ((uint)target.x < 5 && (uint)target.y < 5 && _linkGrid[target.x, target.y] is { } slot)
                    targets.Add(slot.root);
            }
            animator.QueueFieldRays(source.root, targets, DuelEffectKind.LinkRay,
                controller == _session.HumanPlayerIndex ? DuelStyle.ChainCyan : DuelStyle.ChainRed);
        }

        private void UpdateDragColumnGuide()
        {
            int column = -1;
            if (CardDragManager.Instance is { IsDragging: true, CurrentSource: DragSourceType.Hand } drag)
            {
                var point = drag.PointerPosition;
                for (int index = 0; index < 5; index++)
                {
                    if ((_dragHighlightMZone && Matches(_linkGrid[index, 3])) ||
                        (_dragHighlightSZone && Matches(_linkGrid[index, 4])))
                    {
                        column = index;
                        break;
                    }
                }

                bool Matches(FieldSlotView slot) => slot.liveCard == null && CardDragManager.ContainsScreenPoint(slot.root, point);
            }
            SetDragColumnGuide(column);
        }

        private void SetDragColumnGuide(int column)
        {
            if (_guidedColumn == column) return;
            for (int row = 0; row < 5; row++)
            {
                if (_guidedColumn >= 0) _linkGrid[_guidedColumn, row]?.bgImg.SetColumnGuide(false);
                if (column >= 0) _linkGrid[column, row]?.bgImg.SetColumnGuide(true);
            }
            _guidedColumn = column;
        }
    }
}
