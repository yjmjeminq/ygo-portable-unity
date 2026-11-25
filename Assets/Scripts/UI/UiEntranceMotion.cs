using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed class UiEntranceMotion : MonoBehaviour
    {
        private CanvasGroup _group;
        private float _restAlpha;
        private float _startAlpha;
        private float _duration;
        private float _elapsed;
        private bool _active;

        internal static void Play(GameObject target, float startAlpha = .78f, float duration = .16f)
        {
            if (!Application.isPlaying || !target.activeInHierarchy) return;
            if (!target.TryGetComponent<UiEntranceMotion>(out var motion))
                motion = target.AddComponent<UiEntranceMotion>();
            motion.Begin(startAlpha, duration);
        }

        private void Begin(float startAlpha, float duration)
        {
            if (_group == null && !TryGetComponent(out _group))
                _group = gameObject.AddComponent<CanvasGroup>();
            if (!_active) _restAlpha = _group.alpha;
            float requested = _restAlpha * startAlpha;
            _startAlpha = _active ? Mathf.Max(requested, _group.alpha) : requested;
            _duration = Mathf.Max(.01f, duration);
            _elapsed = 0f;
            _active = true;
            _group.alpha = _startAlpha;
            enabled = true;
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        private void Advance(float delta)
        {
            _elapsed += delta;
            float remaining = 1f - Mathf.Clamp01(_elapsed / _duration);
            _group.alpha = Mathf.LerpUnclamped(_restAlpha, _startAlpha, remaining * remaining * remaining);
            if (remaining <= 0f) enabled = false;
        }

        private void OnDisable()
        {
            if (!_active) return;
            _group.alpha = _restAlpha;
            _active = false;
            enabled = false;
        }
    }

    internal sealed class MenuButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        private Image _shine;
        private Button _button;
        private bool _hovered;
        private bool _selected;
        private bool _pressed;
        private float _amount;

        internal static Button Attach(Button button)
        {
            var motion = button.gameObject.AddComponent<MenuButtonMotion>();
            motion._button = button;
            var shine = DuelUiKit.CreateRect("HighlightMotion", button.transform, new Vector2(.08f, .04f),
                new Vector2(.92f, .095f), Vector2.zero, Vector2.zero);
            motion._shine = DuelUiKit.AddImage(shine.gameObject, DuelUiKit.WhiteSprite, Color.clear, false, false);
            return button;
        }

        public void OnPointerEnter(PointerEventData data) => _hovered = true;
        public void OnPointerExit(PointerEventData data) => _pressed = _hovered = false;
        public void OnPointerDown(PointerEventData data) => _pressed = data.button == PointerEventData.InputButton.Left;
        public void OnPointerUp(PointerEventData data) => _pressed = false;
        public void OnSelect(BaseEventData data) => _selected = true;
        public void OnDeselect(BaseEventData data) => _selected = _pressed = false;

        private void Update() => Advance(Time.unscaledDeltaTime);

        private void Advance(float delta)
        {
            if (!_hovered && !_selected && _amount <= 0f) return;
            float target = _button.IsInteractable() && (_hovered || _selected) ? (_pressed ? .54f : 1f) : 0f;
            if (Mathf.Approximately(_amount, target)) return;
            _amount = Mathf.MoveTowards(_amount, target, delta * (_pressed ? 12f : 7f));
            Paint(_amount);
        }

        private void Paint(float amount)
        {
            float eased = Mathf.SmoothStep(0f, 1f, amount);
            var color = DuelUiKit.CyanAccent;
            color.a = .78f * eased;
            _shine.color = color;
            _shine.rectTransform.localScale = new Vector3(Mathf.Lerp(.38f, 1f, eased), 1f, 1f);
        }

        private void OnDisable()
        {
            if (_shine != null) Paint(0f);
            _amount = 0f;
            _hovered = _selected = _pressed = false;
        }
    }
}
