using UnityEngine;
using UnityEngine.EventSystems;

namespace YGOPortable.UI
{
    internal sealed class GxFieldStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler,
        IEndDragHandler, IInitializePotentialDragHandler
    {
        private RectTransform _surface, _thumb;
        private int? _pointer;
        internal Vector2 Direction { get; private set; }

        internal static GxFieldStick Create(RectTransform parent)
        {
            var surface = DuelUiKit.CreateRect("VirtualStick", parent, new Vector2(.026f, .033f), new Vector2(.026f, .033f),
                Vector2.zero, Vector2.zero);
            surface.pivot = Vector2.zero;
            surface.anchoredPosition = Vector2.zero;
            surface.sizeDelta = Vector2.one * Mathf.Max(154f, MobileSafeArea.MinimumTouchSize(parent));
            DuelUiKit.AddImage(surface.gameObject, RpgUiStyle.Joystick(thumb: false), new Color(1f, 1f, 1f, .76f), true, true);
            var pad = surface.gameObject.AddComponent<GxFieldStick>();
            pad._surface = surface;
            pad._thumb = DuelUiKit.CreateRect("Thumb", surface, Vector2.one * .5f, Vector2.one * .5f,
                surface.sizeDelta * -.24f, surface.sizeDelta * .24f);
            DuelUiKit.AddImage(pad._thumb.gameObject, RpgUiStyle.Joystick(thumb: true), Color.white, true, false);
            return pad;
        }

        public void OnInitializePotentialDrag(PointerEventData data) => data.useDragThreshold = false;

        public void OnPointerDown(PointerEventData data)
        {
            if (_pointer.HasValue || data.button != PointerEventData.InputButton.Left) return;
            _pointer = data.pointerId;
            ReadPointer(data);
        }

        public void OnDrag(PointerEventData data)
        {
            if (_pointer == data.pointerId) ReadPointer(data);
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (_pointer == data.pointerId) ResetStick();
        }

        public void OnEndDrag(PointerEventData data) => OnPointerUp(data);

        private void ReadPointer(PointerEventData data)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_surface, data.position, data.pressEventCamera, out var point)) return;
            float radius = Mathf.Min(_surface.rect.width, _surface.rect.height) * .31f;
            var displacement = Vector2.ClampMagnitude((point - _surface.rect.center) / Mathf.Max(1f, radius), 1f);
            _thumb.anchoredPosition = displacement * radius;
            const float deadZone = .14f;
            float magnitude = displacement.magnitude;
            Direction = magnitude <= deadZone ? Vector2.zero : displacement.normalized * ((magnitude - deadZone) / (1f - deadZone));
        }

        internal void ResetStick()
        {
            _pointer = null;
            Direction = Vector2.zero;
            if (_thumb != null) _thumb.anchoredPosition = Vector2.zero;
        }

        private void OnDisable() => ResetStick();
    }
}
