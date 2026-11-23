using UnityEngine;
using UnityEngine.EventSystems;

namespace YGOPortable.UI
{
    internal sealed class CardOverframeInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private CompositeCardView _card;
        private RectTransform _holder;
        private Rect _flatArea;
        private Rect _depthArea;
        private Gyroscope _gyro;
        private bool _ownsGyro;
        private Vector2? _gravityReference;
        private bool _pressed;
        private int _pointer;
        private Vector2 _origin;
        private Vector2 _drag;
        private Vector2 _depth;
        private bool _laidOutForDepth;

        internal static void Attach(RectTransform holder, CompositeCardView card, bool motionSensor, Rect flatArea, Rect depthArea)
        {
            var hit = LotdUiKit.AddImage(holder.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            var input = holder.gameObject.AddComponent<CardOverframeInput>();
            input._card = card;
            input._holder = holder;
            input._flatArea = flatArea;
            input._depthArea = depthArea;
            input._gyro = motionSensor && Application.isMobilePlatform && SystemInfo.supportsGyroscope ? Input.gyro : null;
            hit.raycastTarget = true;
        }

        private void LateUpdate()
        {
            if (_card == null) return;
            bool visible = _card.HasOverframe;
            if (visible != _laidOutForDepth)
            {
                _laidOutForDepth = visible;
                Rect area = visible ? _depthArea : _flatArea;
                _holder.anchorMin = area.min;
                _holder.anchorMax = area.max;
                SetSensorEnabled(visible);
            }
            if (!visible)
            {
                ResetPose();
                return;
            }

            Vector2 target = _pressed ? _drag : ReadMotion();
            _depth = Vector2.Lerp(_depth, target, 1f - Mathf.Exp(-17f * Time.unscaledDeltaTime));
            ShowDepth(_depth, Time.unscaledTime);
        }

        private void SetSensorEnabled(bool visible)
        {
            _gravityReference = null;
            if (_gyro == null) return;
            if (visible && !_gyro.enabled)
            {
                _gyro.enabled = true;
                _ownsGyro = true;
            }
            else if (!visible && _ownsGyro)
            {
                _gyro.enabled = false;
                _ownsGyro = false;
            }
        }

        private Vector2 ReadMotion()
        {
            if (_gyro == null) return Vector2.zero;
            Vector3 gravity = _gyro.gravity;
            if (gravity.sqrMagnitude < .1f) return Vector2.zero;
            Vector2 projected = Screen.orientation == ScreenOrientation.LandscapeRight
                ? new Vector2(gravity.y, -gravity.x) : new Vector2(-gravity.y, gravity.x);
            _gravityReference ??= projected;
            return Vector2.ClampMagnitude((projected - _gravityReference.Value) * 3.4f, 1f);
        }

        private void ShowDepth(Vector2 depth, float elapsed)
        {
            _card.transform.localRotation = Quaternion.Euler(-depth.y * 6f, depth.x * 9f, 0f);
            _card.PoseOverframe(1f, 1f, depth, elapsed * .27f + depth.x * .18f);
        }

        private void ResetPose()
        {
            if (_depth == Vector2.zero && !_pressed) return;
            _pressed = false;
            _drag = _depth = Vector2.zero;
            ShowDepth(Vector2.zero, 0f);
        }

        private void OnDisable()
        {
            SetSensorEnabled(false);
            _laidOutForDepth = false;
            if (_holder != null)
            {
                _holder.anchorMin = _flatArea.min;
                _holder.anchorMax = _flatArea.max;
                ResetPose();
            }
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (!_card.HasOverframe || _pressed || data.button != PointerEventData.InputButton.Left) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_holder, data.position, data.pressEventCamera, out _origin)) return;
            _pointer = data.pointerId;
            _pressed = true;
            _drag = Vector2.zero;
        }

        public void OnBeginDrag(PointerEventData data) => OnDrag(data);

        public void OnDrag(PointerEventData data)
        {
            if (!_pressed || data.pointerId != _pointer ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(_holder, data.position, data.pressEventCamera, out var point)) return;
            _drag = Vector2.ClampMagnitude((point - _origin) / Mathf.Max(1f, _holder.rect.width * .35f), 1f);
        }

        public void OnEndDrag(PointerEventData data) => OnPointerUp(data);

        public void OnPointerUp(PointerEventData data)
        {
            if (_pressed && _pointer == data.pointerId) _pressed = false;
        }
    }

}
