using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    public partial class DeckEditorUI
    {
        private RectTransform _deckMotionLayer;
        private RectTransform _transferRect;
        private CompositeCardView _transferCard;
        private CanvasGroup _transferGroup;
        private DeckCardVisualMotion _arrivalMotion;
        private RectTransform _pointerSlot;
        private RectTransform _hoveredSlot;
        private RectTransform _padVisualSlot;
        private RectTransform _inspectedSource;
        private bool _transferActive;
        private float _transferElapsed;
        private VisualPose _transferFrom;
        private VisualPose _transferTo;
        private bool _transferLeavesDeck;

        private readonly struct VisualPose
        {
            internal readonly Vector2 Center;
            internal readonly Vector2 Size;

            internal VisualPose(Vector2 center, Vector2 size)
            {
                Center = center;
                Size = size;
            }
        }

        private void BuildDeckMotion(Transform root)
        {
            _deckMotionLayer = LotdUiKit.FillRect("DeckCardMotion", root);
            _transferRect = LotdUiKit.CreateRect("Transfer", _deckMotionLayer, _deckMotionLayer.pivot,
                _deckMotionLayer.pivot, Vector2.zero, Vector2.zero);
            _transferGroup = _transferRect.gameObject.AddComponent<CanvasGroup>();
            _transferGroup.blocksRaycasts = false;
            _transferGroup.interactable = false;
            _transferCard = CompositeCardView.Create(_transferRect, "Card");
            _transferRect.gameObject.SetActive(false);
        }

        private VisualPose? CardPose(RectTransform rect)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy || rect.rect.width < 1f || rect.rect.height < 1f)
                return null;
            Vector2 center = _deckMotionLayer.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            Vector2 size = _deckMotionLayer.InverseTransformVector(rect.TransformVector(rect.rect.size));
            float height = Mathf.Min(Mathf.Abs(size.y), Mathf.Abs(size.x) / LotdUiKit.CardAspectRatio);
            return new VisualPose(center, new Vector2(height * LotdUiKit.CardAspectRatio, height));
        }

        private VisualPose? DropPose(Vector2 screenPoint)
        {
            var canvas = _deckMotionLayer.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_deckMotionLayer, screenPoint, camera, out var local)
                ? new VisualPose(local, new Vector2(112f, 162f)) : null;
        }

        private static RectTransform CardVisual(RectTransform slot) =>
            slot != null && slot.childCount > 0 && slot.GetChild(0).childCount > 0
                ? slot.GetChild(0).GetChild(0) as RectTransform : null;

        private VisualPose?[] CaptureDeckPositions(DragSourceType zone, int count)
        {
            var slots = GetDeckSlots(zone);
            var positions = new VisualPose?[Mathf.Min(count, slots.Count)];
            for (int i = 0; i < positions.Length; i++)
            {
                var art = (RectTransform)slots[i].card.transform;
                // A rapid move should continue from the moving artwork that the player can see.
                positions[i] = CardPose(_transferActive && _arrivalMotion != null && _arrivalMotion.transform == art
                    ? _transferRect : art);
            }
            return positions;
        }

        private void AnimateDeckSlots(DragSourceType zone, int start, int end, VisualPose?[] oldPositions,
            Func<int, int> oldIndex)
        {
            var slots = GetDeckSlots(zone);
            for (int index = start; index < Mathf.Min(end, slots.Count); index++)
            {
                int origin = oldIndex(index);
                if (origin < 0 || origin >= oldPositions.Length ||
                    !oldPositions[origin].HasValue) continue;
                var view = slots[index].card;
                var visual = (RectTransform)view.transform;
                var position = oldPositions[origin].Value;
                var current = CardPose(visual);
                if (!current.HasValue) continue;
                Vector2 distance = position.Center - current.Value.Center;
                if (distance.sqrMagnitude < .04f) continue;
                Vector3 shift = _deckMotionLayer.TransformVector(distance);
                var motion = view.GetComponent<DeckCardVisualMotion>();
                motion.SlideFrom(visual.anchoredPosition + (Vector2)visual.parent.InverseTransformVector(shift));
            }
        }

        private void AnimateOrderedDeck(DragSourceType zone, List<uint> cards, uint[] previously, VisualPose?[] positions)
        {
            var origins = new Dictionary<uint, Queue<int>>();
            for (int i = 0; i < previously.Length; i++)
            {
                if (!origins.TryGetValue(previously[i], out var indexes))
                    origins.Add(previously[i], indexes = new Queue<int>());
                indexes.Enqueue(i);
            }
            PadArea area = DeckPadArea(zone);
            int oldFocus = _padIndices[(int)area];
            AnimateDeckSlots(zone, 0, cards.Count, positions, i =>
            {
                int origin = origins[cards[i]].Dequeue();
                if (origin == oldFocus) _padIndices[(int)area] = i;
                return origin;
            });
        }

        private static PadArea DeckPadArea(DragSourceType source) => source switch
        {
            DragSourceType.DeckExtra => PadArea.Extra,
            DragSourceType.DeckSide => PadArea.Side,
            _ => PadArea.Main,
        };

        private void AnimateTransfer(uint code, VisualPose? source, RectTransform destination, bool leavingDeck = false)
        {
            var target = CardPose(destination);
            if (!source.HasValue || !Root.activeInHierarchy || (!target.HasValue && !leavingDeck))
            {
                if (destination != null && destination.TryGetComponent<DeckCardVisualMotion>(out var motion))
                    motion.Pulse();
                return;
            }
            EndDeckTransfer();
            _transferFrom = source.Value;
            _transferTo = target ?? new VisualPose(_transferFrom.Center + Vector2.left * 36f,
                _transferFrom.Size * .82f);
            _transferLeavesDeck = leavingDeck;
            _transferElapsed = 0f;
            _transferActive = true;
            if (!leavingDeck && destination.TryGetComponent(out _arrivalMotion))
                _arrivalMotion.SetTransferAlpha(.17f);
            _transferRect.gameObject.SetActive(true);
            _transferCard.SetCard(code);
            UpdateDeckTransfer(0f);
        }

        private void UpdateDeckTransfer(float dt)
        {
            if (!_transferActive) return;
            _transferElapsed += dt;
            float t = Mathf.Clamp01(_transferElapsed / (_transferLeavesDeck ? .21f : .27f));
            float progress = Mathf.SmoothStep(0f, 1f, t);
            var travel = _transferTo.Center - _transferFrom.Center;
            float arc = Mathf.Min(34f, Mathf.Abs(travel.x) * .115f);
            _transferRect.anchoredPosition = Vector2.Lerp(_transferFrom.Center, _transferTo.Center, progress) +
                Vector2.up * (Mathf.Sin(progress * Mathf.PI) * arc);
            _transferRect.sizeDelta = Vector2.Lerp(_transferFrom.Size, _transferTo.Size, progress);
            _transferGroup.alpha = _transferLeavesDeck ? 1f - progress * progress :
                1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.72f, 1f, t));
            if (_arrivalMotion != null)
                _arrivalMotion.SetTransferAlpha(Mathf.Lerp(.17f, 1f, Mathf.InverseLerp(.72f, 1f, t)));
            if (t >= 1f) EndDeckTransfer();
        }

        private void EndDeckTransfer()
        {
            if (!_transferActive) return;
            _transferActive = false;
            if (_arrivalMotion != null) _arrivalMotion.SetTransferAlpha(1f);
            _arrivalMotion = null;
            _transferRect.gameObject.SetActive(false);
        }

        private void SetMouseCardVisual(RectTransform slot, uint code)
        {
            _pointerSlot = slot;
            if (_padMode) return;
            if (_deckCardActions.activeSelf || _deckDialog.activeSelf || _cardFilterDialog.activeSelf ||
                _archModalRoot.activeSelf || _mainSortMenu.activeSelf)
                return;
            if (_hoveredSlot != slot)
            {
                ClearCardVisual(ref _hoveredSlot);
                _hoveredSlot = slot;
            }
            _inspectedSource = slot;
            CardVisual(slot)?.GetComponent<DeckCardVisualMotion>()?.SetHighlighted(true);
            InspectCard(code);
        }

        private void ClearMouseCardVisual(RectTransform slot)
        {
            if (slot == _pointerSlot) _pointerSlot = null;
            if (slot == _hoveredSlot) ClearCardVisual(ref _hoveredSlot);
        }

        private void RestoreMouseCardVisual()
        {
            if (!CardDragManager.ContainsScreenPoint(_pointerSlot, Input.mousePosition)) return;
            var card = CardVisual(_pointerSlot)?.GetComponent<CompositeCardView>();
            if (card != null && card.gameObject.activeInHierarchy && card.CurrentCode != 0)
                SetMouseCardVisual(_pointerSlot, card.CurrentCode);
        }

        private void SetPadCardVisual(RectTransform slot, uint code)
        {
            ClearCardVisual(ref _hoveredSlot);
            if (_padVisualSlot != slot)
            {
                ClearCardVisual(ref _padVisualSlot);
                _padVisualSlot = slot;
            }
            CardVisual(slot)?.GetComponent<DeckCardVisualMotion>()?.SetHighlighted(code != 0);
            _inspectedSource = slot;
        }

        private static void ClearCardVisual(ref RectTransform slot)
        {
            CardVisual(slot)?.GetComponent<DeckCardVisualMotion>()?.SetHighlighted(false);
            slot = null;
        }

        private void ResetCardBinding(RectTransform slot, CompositeCardView card)
        {
            if (_hoveredSlot == slot) _hoveredSlot = null;
            if (_padVisualSlot == slot) _padVisualSlot = null;
            if (_inspectedSource == slot) _inspectedSource = null;
            card.GetComponent<DeckCardVisualMotion>().ResetVisual();
        }
    }

    internal sealed class DeckCardVisualMotion : MonoBehaviour
    {
        private CompositeCardView _card;
        private RectTransform _rect;
        private CanvasGroup _canvas;
        private Image _slotTint;
        private bool _highlighted;
        private float _hover;
        private float _pulse;
        private float _visibility = 1f;
        private Vector2 _offset;

        internal static void Bind(CompositeCardView card, Image tint = null)
        {
            var motion = card.gameObject.AddComponent<DeckCardVisualMotion>();
            motion._card = card;
            motion._rect = (RectTransform)card.transform;
            motion._canvas = card.gameObject.AddComponent<CanvasGroup>();
            motion._slotTint = tint;
            motion.ResetVisual();
        }

        internal void SetHighlighted(bool highlighted)
        {
            if (_highlighted == highlighted) return;
            _highlighted = highlighted;
            enabled = true;
        }

        internal void SetTransferAlpha(float alpha)
        {
            _visibility = alpha;
            _canvas.alpha = alpha;
        }

        internal void Pulse()
        {
            _pulse = 1f;
            _canvas.alpha = _visibility * .77f;
            _rect.localScale = Vector3.one * .97f;
            enabled = true;
        }

        internal void SlideFrom(Vector2 offset)
        {
            _offset = offset;
            Paint();
            enabled = true;
        }

        internal void ResetVisual()
        {
            _highlighted = false;
            _hover = _pulse = 0f;
            _visibility = 1f;
            _offset = Vector2.zero;
            Paint();
            enabled = false;
        }

        private void Update()
        {
            float smoothing = 1f - Mathf.Exp(-22f * Time.unscaledDeltaTime);
            float target = _highlighted ? 1f : 0f;
            _hover = Mathf.Lerp(_hover, target, smoothing);
            _offset = Vector2.Lerp(_offset, Vector2.zero, smoothing);
            _pulse = Mathf.MoveTowards(_pulse, 0f, 5f * Time.unscaledDeltaTime);
            bool settled = Mathf.Abs(_hover - target) < .01f && _offset.sqrMagnitude < .12f && _pulse == 0f;
            if (settled)
            {
                _hover = target;
                _offset = Vector2.zero;
                enabled = false;
            }
            Paint();
        }

        private void Paint()
        {
            _rect.anchoredPosition = _offset + Vector2.up * (_hover * 4f);
            _rect.localScale = Vector3.one * (1f + _hover * .045f - _pulse * .05f);
            _card.SetRarityHighlight(Mathf.Max(_hover, _pulse));
            _canvas.alpha = _visibility * (1f - _pulse * .24f);
            if (_slotTint != null)
            {
                Color tint = LotdUiKit.GoldAccent;
                tint.a = _hover * .83f;
                _slotTint.color = tint;
            }
        }
    }
}
