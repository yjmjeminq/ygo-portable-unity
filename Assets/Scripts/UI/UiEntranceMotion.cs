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
        private static Sprite _softUnderline;
        private Image _shine;
        private Button _button;
        private RectTransform _visual;
        private ButtonSurfaceMotion _surface;
        private Vector3 _restScale;
        private Color _accent;
        private bool _hovered;
        private bool _selected;
        private bool _pressed;
        private float _amount;
        private float _scale = 1f;
        private float _activation;

        internal static Button Attach(Button button, RectTransform visual = null, Color? accent = null,
            bool underline = true, Image surface = null)
        {
            if (!button.TryGetComponent<MenuButtonMotion>(out var motion))
            {
                motion = button.gameObject.AddComponent<MenuButtonMotion>();
                motion._button = button;
                var shine = DuelUiKit.CreateRect("HighlightMotion", button.transform, new Vector2(.08f, .04f),
                    new Vector2(.92f, .095f), Vector2.zero, Vector2.zero);
                motion._shine = DuelUiKit.AddImage(shine.gameObject, DuelUiKit.WhiteSprite, Color.clear, false, false);
                button.onClick.AddListener(motion.Activate);
            }
            if (motion._visual != visual)
            {
                if (motion._visual != null) motion._visual.localScale = motion._restScale;
                motion._visual = visual;
                if (visual != null) motion._restScale = visual.localScale;
            }
            motion._accent = accent ?? DuelUiKit.CyanAccent;
            motion._shine.enabled = underline;
            if (surface != null)
            {
                if (!surface.TryGetComponent(out motion._surface))
                    motion._surface = surface.gameObject.AddComponent<ButtonSurfaceMotion>();
                motion._shine.sprite = _softUnderline ??= CreateSoftUnderline();
                motion._shine.rectTransform.anchorMin = new Vector2(.13f, .048f);
                motion._shine.rectTransform.anchorMax = new Vector2(.87f, .16f);
            }
            motion._selected = EventSystem.current?.currentSelectedGameObject == button.gameObject;
            motion.enabled = true;
            motion.Paint(motion._amount);
            return button;
        }

        private void Activate() { if (isActiveAndEnabled && _visual != null) _activation = .075f; }

        public void OnPointerEnter(PointerEventData data) => _hovered = true;
        public void OnPointerExit(PointerEventData data) => _pressed = _hovered = false;
        public void OnPointerDown(PointerEventData data) => _pressed = data.button == PointerEventData.InputButton.Left;
        public void OnPointerUp(PointerEventData data) => _pressed = false;
        public void OnSelect(BaseEventData data) => _selected = true;
        public void OnDeselect(BaseEventData data) => _selected = _pressed = false;

        private void Update() => Advance(Time.unscaledDeltaTime);

        internal void Advance(float delta)
        {
            bool selected = _visual != null ? EventSystem.current?.currentSelectedGameObject == gameObject : _selected;
            if (!_hovered && !selected && !_pressed && _activation <= 0f && _amount <= 0f && _scale == 1f) return;
            bool held = _pressed || _activation > 0f;
            _activation = Mathf.Max(0f, _activation - delta);
            bool focused = _button.IsInteractable() && (_hovered || selected);
            float target = focused ? held ? .54f : 1f : 0f;
            if (!Mathf.Approximately(_amount, target))
            {
                _amount = Mathf.MoveTowards(_amount, target, delta * (held ? 12f : 7f));
                Paint(_amount);
            }
            if (_visual == null) return;
            float scaleTarget = _button.IsInteractable() && held ? .958f : focused ? 1.045f : 1f;
            if (_scale == scaleTarget) return;
            _scale = Mathf.Abs(_scale - scaleTarget) < .0002f ? scaleTarget :
                Mathf.Lerp(_scale, scaleTarget, 1f - Mathf.Exp(-(held ? 30f : 16f) * delta));
            _visual.localScale = _restScale * _scale;
            _surface?.SetScale(_scale);
        }

        private void Paint(float amount)
        {
            float eased = Mathf.SmoothStep(0f, 1f, amount);
            var color = _accent;
            color.a = .78f * eased;
            _shine.color = color;
            _shine.rectTransform.localScale = new Vector3(Mathf.Lerp(.38f, 1f, eased), 1f, 1f);
        }

        private static Sprite CreateSoftUnderline()
        {
            const int width = 96, height = 12;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = Mathf.Abs((x + .5f) * 2f / width - 1f);
                float v = Mathf.Abs((y + .5f) * 2f / height - 1f);
                float fade = (1f - Mathf.Pow(u, 3f)) * (1f - v) * (1f - v);
                pixels[y * width + x] = new Color(1f, 1f, 1f, fade);
            }
            return DuelUiChrome.CreateSprite("RPG button focus", width, height, pixels);
        }

        private void OnDisable()
        {
            if (_shine != null) Paint(0f);
            if (_visual != null) _visual.localScale = _restScale;
            _surface?.SetScale(1f);
            _amount = 0f;
            _activation = 0f;
            _scale = 1f;
            _hovered = _selected = _pressed = false;
        }
    }

    internal sealed class ButtonSurfaceMotion : BaseMeshEffect
    {
        internal float Scale { get; private set; } = 1f;

        internal void SetScale(float scale)
        {
            if (Scale == scale) return;
            Scale = scale;
            graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vertices)
        {
            if (!IsActive() || Scale == 1f) return;
            Vector2 center = graphic.rectTransform.rect.center;
            float width = Mathf.LerpUnclamped(1f, Scale, .52f);
            var vertex = new UIVertex();
            for (int i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                vertex.position.x = center.x + (vertex.position.x - center.x) * width;
                vertex.position.y = center.y + (vertex.position.y - center.y) * Scale;
                vertices.SetUIVertex(vertex, i);
            }
        }
    }
}
