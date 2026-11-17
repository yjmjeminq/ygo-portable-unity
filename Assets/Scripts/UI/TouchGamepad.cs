using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed class TouchGamepad : MonoBehaviour
    {
        private static readonly Vector2 BottomLeft = Vector2.zero;
        private static readonly Vector2 BottomRight = Vector2.right;
        private static readonly Vector2 TopCenter = new Vector2(.5f, 1f);
        private static readonly Vector2 MainKey = new Vector2(92f, 92f);
        private static readonly Vector2 ShortcutKey = new Vector2(86f, 66f);

        private static TouchGamepad _active;
        private static Sprite _disc;
        private readonly List<TouchGamepadControl> _keys = new List<TouchGamepadControl>();
        private RectTransform _safeArea;
        private RectTransform _controls;
        private Text _toggleLabel;
        private GameObject _selectedControl;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;
        internal static DeckPadButtons HeldButtons => _active != null ? _active.ReadButtons() : DeckPadButtons.None;
        internal static float DescriptionScroll => _active != null ? _active.ReadScroll() : 0f;

        internal static TouchGamepad Create(Transform canvasParent)
        {
            var root = LotdUiKit.FillRect("TouchGamepad", canvasParent);
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 31000; // Above Unity's dropdown list and its blocker.
            root.gameObject.AddComponent<GraphicRaycaster>();
            var gamepad = root.gameObject.AddComponent<TouchGamepad>();
            _active = gamepad;
            gamepad.Build(root);
            return gamepad;
        }

        internal static void ReleaseAll()
        {
            if (_active != null) _active.ClearPresses();
        }

        internal static bool IsControlAt(Vector2 screenPosition)
        {
            if (_active == null || !_active.isActiveAndEnabled) return false;
            foreach (var key in _active._keys)
            {
                if (key.isActiveAndEnabled && RectTransformUtility.RectangleContainsScreenPoint(key.Rect, screenPosition))
                    return true;
            }
            return false;
        }

        private void Build(Transform root)
        {
            _safeArea = LotdUiKit.FillRect("SafeArea", root);
            _controls = LotdUiKit.FillRect("Controls", _safeArea);

            AddKey(_controls, "Up", "▲", BottomLeft, 151f, 249f, MainKey, DeckPadButtons.Up);
            AddKey(_controls, "Left", "◀", BottomLeft, 53f, 151f, MainKey, DeckPadButtons.Left);
            AddKey(_controls, "Right", "▶", BottomLeft, 249f, 151f, MainKey, DeckPadButtons.Right);
            AddKey(_controls, "Down", "▼", BottomLeft, 151f, 53f, MainKey, DeckPadButtons.Down);
            AddKey(_controls, "L", "L", BottomLeft, 53f, 340f, ShortcutKey, DeckPadButtons.PreviousArea);
            AddKey(_controls, "Minus", "−", BottomLeft, 151f, 340f, ShortcutKey, DeckPadButtons.ResetFilters);

            AddKey(_controls, "X", "X", BottomRight, -151f, 249f, MainKey, DeckPadButtons.Alternate);
            AddKey(_controls, "Y", "Y", BottomRight, -249f, 151f, MainKey, DeckPadButtons.Search);
            AddKey(_controls, "A", "A", BottomRight, -53f, 151f, MainKey, DeckPadButtons.Submit);
            AddKey(_controls, "B", "B", BottomRight, -151f, 53f, MainKey, DeckPadButtons.Cancel);
            AddKey(_controls, "R", "R", BottomRight, -53f, 340f, ShortcutKey, DeckPadButtons.NextArea);
            AddKey(_controls, "ZR", "ZR", BottomRight, -151f, 340f, ShortcutKey, DeckPadButtons.RelatedCards);
            AddKey(_controls, "Plus", "+", BottomRight, -249f, 340f, ShortcutKey, DeckPadButtons.Save);
            AddKey(_controls, "ScrollUp", "R↑", BottomRight, -53f, 249f, ShortcutKey, scroll: 1f);
            AddKey(_controls, "ScrollDown", "R↓", BottomRight, -53f, 53f, ShortcutKey, scroll: -1f);
            AddKey(_controls, "RightStick", "R3", BottomRight, -249f, 53f, ShortcutKey, DeckPadButtons.Reorder);

            _toggleLabel = AddKey(_safeArea, "Toggle", "패드 끄기", TopCenter, -220f, -29f,
                new Vector2(156f, 48f), onClick: ToggleControls);
            UpdateSafeArea();
        }

        private Text AddKey(Transform parent, string name, string label, Vector2 anchor, float x, float y, Vector2 size,
            DeckPadButtons button = DeckPadButtons.None, float scroll = 0f, Action onClick = null)
        {
            var rect = LotdUiKit.CreateRect(name, parent, anchor, anchor, Vector2.zero, Vector2.zero);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(x, y);

            LotdUiKit.AddImage(rect.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            var face = LotdUiKit.FillRect("Face", rect);
            var sprite = _disc != null ? _disc : (_disc = CreateDisc());
            LotdUiKit.AddImage(face.gameObject, sprite, new Color(.47f, .73f, .86f, .35f), false, false);
            var fill = LotdUiKit.AddImage(LotdUiKit.FillRect("Fill", face, 3f).gameObject, sprite, Color.clear, false, false);
            var caption = LotdUiKit.AddText(face, "Label", label, size == MainKey ? 36 : 23, TextAnchor.MiddleCenter,
                LotdUiKit.TextPrimary, FontStyle.Bold);
            caption.horizontalOverflow = HorizontalWrapMode.Overflow;
            caption.verticalOverflow = VerticalWrapMode.Overflow;

            var control = rect.gameObject.AddComponent<TouchGamepadControl>();
            control.Initialize(this, rect, face, fill, button, scroll, onClick);
            _keys.Add(control);
            return caption;
        }

        private static Sprite CreateDisc()
        {
            const int size = 64;
            const float center = size * .5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = new Vector2(x + .5f - center, y + .5f - center).magnitude;
                    byte alpha = (byte)(Mathf.Clamp01(center - distance) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TouchGamepadDisc",
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
        }

        private DeckPadButtons ReadButtons()
        {
            var buttons = DeckPadButtons.None;
            foreach (var key in _keys)
                if (key.IsHeld) buttons |= key.Button;
            return buttons;
        }

        private float ReadScroll()
        {
            float scroll = 0f;
            foreach (var key in _keys)
                if (key.IsHeld) scroll += key.Scroll;
            return Mathf.Clamp(scroll, -1f, 1f);
        }

        internal void RestoreSelection()
        {
            // The touch module deselects the game control before dispatching a pointer down here.
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject == null && _selectedControl != null &&
                _selectedControl.activeInHierarchy)
            {
                events.SetSelectedGameObject(_selectedControl);
            }
        }

        private void ToggleControls()
        {
            ClearPresses();
            bool show = !_controls.gameObject.activeSelf;
            _controls.gameObject.SetActive(show);
            _toggleLabel.text = show ? "패드 끄기" : "패드 켜기";
        }

        private void UpdateSafeArea()
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            var area = Screen.safeArea;
            if (size.x <= 0 || size.y <= 0 || (_lastScreenSize == size && _lastSafeArea == area)) return;
            _lastScreenSize = size;
            _lastSafeArea = area;
            if (area.width <= 0f || area.height <= 0f) area = new Rect(0f, 0f, size.x, size.y);
            _safeArea.anchorMin = new Vector2(area.xMin / size.x, area.yMin / size.y);
            _safeArea.anchorMax = new Vector2(area.xMax / size.x, area.yMax / size.y);
        }

        private void ClearPresses()
        {
            foreach (var key in _keys) key.ClearPresses();
        }

        private void LateUpdate()
        {
            UpdateSafeArea();
            var selected = EventSystem.current?.currentSelectedGameObject;
            _selectedControl = selected != null && !selected.TryGetComponent<InputField>(out _) ? selected : null;
        }

        private void OnDisable() => ClearPresses();

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) ClearPresses();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) ClearPresses();
        }

        private void OnDestroy()
        {
            if (_active == this) _active = null;
        }
    }

    internal sealed class TouchGamepadControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
        IPointerExitHandler
    {
        private static readonly Color IdleFill = new Color(.035f, .08f, .13f, .5f);
        private static readonly Color HeldFill = new Color(.11f, .47f, .62f, .93f);
        private readonly HashSet<int> _pointers = new HashSet<int>();
        private TouchGamepad _owner;
        private RectTransform _face;
        private Image _fill;
        private Action _onClick;
        private float _pressure;

        internal RectTransform Rect { get; private set; }
        internal DeckPadButtons Button { get; private set; }
        internal float Scroll { get; private set; }
        internal bool IsHeld => _pointers.Count > 0;

        internal void Initialize(TouchGamepad owner, RectTransform rect, RectTransform face, Image fill,
            DeckPadButtons button, float scroll, Action onClick)
        {
            _owner = owner;
            Rect = rect;
            _face = face;
            _fill = fill;
            Button = button;
            Scroll = scroll;
            _onClick = onClick;
            Paint();
        }

        internal void ClearPresses()
        {
            _pointers.Clear();
            _pressure = 0f;
            if (_fill != null) Paint();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _pointers.Add(eventData.pointerId);
            _owner.RestoreSelection();
        }

        public void OnPointerUp(PointerEventData eventData) => _pointers.Remove(eventData.pointerId);

        public void OnPointerExit(PointerEventData eventData) => _pointers.Remove(eventData.pointerId);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) _onClick?.Invoke();
        }

        private void LateUpdate()
        {
            float target = IsHeld ? 1f : 0f;
            if (_pressure == target) return;
            _pressure = Mathf.MoveTowards(_pressure, target, Time.unscaledDeltaTime * (IsHeld ? 18f : 10f));
            Paint();
        }

        private void Paint()
        {
            _fill.color = Color.Lerp(IdleFill, HeldFill, _pressure);
            _face.localScale = Vector3.one * Mathf.Lerp(1f, .9f, _pressure);
        }

        private void OnDisable() => ClearPresses();
    }
}
