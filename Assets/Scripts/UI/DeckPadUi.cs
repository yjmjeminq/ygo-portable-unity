using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed class DeckPadUi
    {
        private const int VisibleChoices = 7;
        private readonly Transform _root;
        private readonly DeckPadKeyboard _keyboard;
        private readonly Button[] _choiceButtons = new Button[VisibleChoices];
        private readonly Text[] _choiceLabels = new Text[VisibleChoices];
        private readonly RectTransform _choicePanel;
        private readonly Text _choiceStatus;
        private Transform _scope;
        private Selectable _focus;
        private Selectable _returnFocus;
        private RectTransform _frame;
        private Dropdown _dropdown;
        private int _choiceIndex;
        private int _firstChoice;

        internal bool IsTyping => _keyboard.IsOpen;
        internal bool HasPopup => IsTyping || _choicePanel.gameObject.activeSelf;

        internal DeckPadUi(Transform root)
        {
            _root = root;
            var overlay = LotdUiKit.FillRect("PadChoices", root);
            _choicePanel = overlay;
            LotdUiKit.AddImage(overlay.gameObject, LotdUiKit.WhiteSprite, new Color(.01f, .025f, .07f, .80f));
            var panel = LotdUiKit.CreateMenuPanel(overlay, "Window", new Vector2(.265f, .147f), new Vector2(.735f, .853f));
            var header = LotdUiKit.CreateRect("Header", panel, new Vector2(.09f, .843f), new Vector2(.91f, .955f),
                Vector2.zero, Vector2.zero);
            _choiceStatus = LotdUiKit.AddFittedText(header, "Status", "", 20, 13, TextAnchor.MiddleCenter,
                LotdUiKit.CyanAccent, FontStyle.Bold);
            for (int i = 0; i < VisibleChoices; i++)
            {
                int rowIndex = i;
                float top = .822f - i * .100f;
                var row = LotdUiKit.CreateRect("Choice" + i, panel, new Vector2(.11f, top - .085f), new Vector2(.89f, top),
                    Vector2.zero, Vector2.zero);
                _choiceButtons[i] = LotdUiKit.CreateLotdButton(row, "Button", "", () => AcceptChoice(_firstChoice + rowIndex), 17);
                _choiceLabels[i] = _choiceButtons[i].GetComponentInChildren<Text>();
                _choiceLabels[i].resizeTextForBestFit = true;
                _choiceLabels[i].resizeTextMinSize = 11;
                _choiceLabels[i].resizeTextMaxSize = 17;
            }
            var hint = LotdUiKit.CreateRect("Hint", panel, new Vector2(.08f, .063f), new Vector2(.92f, .124f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(hint, "Text", "↑↓ 이동   A 선택   B 취소", 15, 10, TextAnchor.MiddleCenter, LotdUiKit.TextMuted);
            overlay.gameObject.SetActive(false);
            _keyboard = new DeckPadKeyboard(root);
        }

        internal void OpenText(InputField target)
        {
            if (target == null || !target.isActiveAndEnabled || !target.IsInteractable()) return;
            CloseChoice();
            _returnFocus = target;
            target.DeactivateInputField();
            _keyboard.Open(target);
            _scope = null;
            SetFocus(null);
        }

        internal void FocusInScope(Transform scope, Selectable control)
        {
            _scope = scope;
            _returnFocus = null;
            Focus(control);
        }

        private void Focus(Selectable control)
        {
            SetFocus(control);
            ScrollTo(control);
        }

        internal void Clear()
        {
            CloseChoice();
            _keyboard.Close();
            _returnFocus = null;
            _scope = null;
            SetFocus(null);
        }

        internal bool Handle(Transform scope, Vector2Int move, bool submit, bool cancel, Action onCancel,
            Selectable defaultFocus = null)
        {
            if (IsTyping)
            {
                HandleScope(_keyboard.Scope, move, submit, cancel, _keyboard.Close, _keyboard.DefaultFocus);
                return true;
            }
            if (_choicePanel.gameObject.activeSelf)
            {
                if (cancel || _dropdown == null || !_dropdown.isActiveAndEnabled)
                    CloseChoice();
                else if (submit)
                    AcceptChoice(_choiceIndex);
                else if (move.y != 0)
                {
                    _choiceIndex = Mathf.Clamp(_choiceIndex - move.y, 0, _dropdown.options.Count - 1);
                    RefreshChoices();
                }
                return true;
            }
            if (scope == null || !scope.gameObject.activeInHierarchy) return false;
            if (CanFocus(_returnFocus, scope))
            {
                _scope = scope;
                Focus(_returnFocus);
            }
            _returnFocus = null;
            HandleScope(scope, move, submit, cancel, onCancel, defaultFocus);
            return true;
        }

        private void HandleScope(Transform scope, Vector2Int move, bool submit, bool cancel, Action onCancel, Selectable defaultFocus)
        {
            if (cancel)
            {
                SetFocus(null);
                onCancel?.Invoke();
                return;
            }
            if (_scope != scope || !CanFocus(_focus, scope))
            {
                _scope = scope;
                Focus(CanFocus(defaultFocus, scope) ? defaultFocus : FindInitial(scope));
            }
            if (_focus == null) return;
            if (move != Vector2Int.zero)
            {
                Focus(FindNext(scope, _focus, move));
                return;
            }
            if (!submit) return;
            switch (_focus)
            {
                case Dropdown dropdown:
                    OpenChoice(dropdown);
                    break;
                case InputField field:
                    OpenText(field);
                    break;
                case Button button:
                    button.onClick.Invoke();
                    break;
                case Toggle toggle:
                    toggle.isOn = !toggle.isOn;
                    break;
            }
        }

        private static bool CanFocus(Selectable item, Transform scope) =>
            item != null && item.transform != scope && item.transform.IsChildOf(scope) &&
            item.isActiveAndEnabled && item.IsInteractable() && (item is Button || item is Dropdown || item is InputField || item is Toggle);

        private static Selectable FindInitial(Transform scope)
        {
            foreach (var option in scope.GetComponentsInChildren<Selectable>())
                if (CanFocus(option, scope)) return option;
            return null;
        }

        private static Selectable FindNext(Transform scope, Selectable current, Vector2Int move)
        {
            Vector2 center(Selectable item)
            {
                var rect = (RectTransform)item.transform;
                return scope.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            }

            var origin = center(current);
            var direction = ((Vector2)move).normalized;
            float bestScore = float.PositiveInfinity;
            Selectable next = current;
            foreach (var candidate in scope.GetComponentsInChildren<Selectable>())
            {
                if (!CanFocus(candidate, scope) || candidate == current) continue;
                var delta = center(candidate) - origin;
                float forward = Vector2.Dot(direction, delta);
                if (forward <= 1f) continue;
                float across = Mathf.Abs(Vector2.Dot(new Vector2(-direction.y, direction.x), delta));
                float score = forward + 3.5f * across;
                if (score >= bestScore) continue;
                bestScore = score;
                next = candidate;
            }
            return next;
        }

        private void SetFocus(Selectable control)
        {
            var events = EventSystem.current;
            if (control == null)
            {
                if (events != null && _focus != null && events.currentSelectedGameObject == _focus.gameObject)
                    events.SetSelectedGameObject(null);
                if (_frame != null)
                {
                    _frame.gameObject.SetActive(false);
                    _frame.SetParent(_root, false);
                }
                _focus = null;
                return;
            }
            _focus = control;
            if (events != null && events.currentSelectedGameObject != control.gameObject && !events.alreadySelecting)
                events.SetSelectedGameObject(control.gameObject);
            if (_frame == null) _frame = CreateFrame(control.transform);
            else if (_frame.parent != control.transform) _frame.SetParent(control.transform, false);
            _frame.anchorMin = Vector2.zero;
            _frame.anchorMax = Vector2.one;
            _frame.offsetMin = Vector2.zero;
            _frame.offsetMax = Vector2.zero;
            _frame.SetAsLastSibling();
            _frame.gameObject.SetActive(true);
        }

        private static RectTransform CreateFrame(Transform target)
        {
            var frame = LotdUiKit.FillRect("PadFocus", target);
            frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            void Edge(Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
            {
                var edge = LotdUiKit.CreateRect("FocusEdge", frame, min, max, offsetMin, offsetMax);
                LotdUiKit.AddImage(edge.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.GoldAccent, false, false);
            }
            Edge(Vector2.zero, Vector2.right, Vector2.zero, Vector2.up * 2f);
            Edge(Vector2.up, Vector2.one, Vector2.down * 2f, Vector2.zero);
            Edge(Vector2.zero, Vector2.up, Vector2.zero, Vector2.right * 2f);
            Edge(Vector2.right, Vector2.one, Vector2.left * 2f, Vector2.zero);
            return frame;
        }

        private static void ScrollTo(Selectable control)
        {
            if (control == null) return;
            var scroll = control.GetComponentInParent<ScrollRect>();
            if (scroll == null || !scroll.vertical || scroll.content == null || scroll.viewport == null ||
                !control.transform.IsChildOf(scroll.content)) return;
            var viewport = scroll.viewport;
            float overflow = scroll.content.rect.height - viewport.rect.height;
            if (overflow <= 0f) return;
            var rect = viewport.rect;
            var item = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, control.transform);
            float offset = item.min.y < rect.yMin ? item.min.y - rect.yMin :
                Mathf.Max(0f, item.max.y - rect.yMax);
            if (offset == 0f) return;
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + offset / overflow);
        }

        private void OpenChoice(Dropdown dropdown)
        {
            if (dropdown.options.Count == 0) return;
            if (dropdown.transform.Find("Dropdown List") != null) dropdown.Hide();
            _dropdown = dropdown;
            _returnFocus = dropdown;
            _choiceIndex = dropdown.value;
            _firstChoice = Mathf.Clamp(_choiceIndex - VisibleChoices / 2, 0, Mathf.Max(0, dropdown.options.Count - VisibleChoices));
            _choicePanel.SetAsLastSibling();
            _choicePanel.gameObject.SetActive(true);
            RefreshChoices();
        }

        private void RefreshChoices()
        {
            if (_choiceIndex < _firstChoice) _firstChoice = _choiceIndex;
            if (_choiceIndex >= _firstChoice + VisibleChoices) _firstChoice = _choiceIndex - VisibleChoices + 1;
            _choiceStatus.text = $"항목 선택   {_choiceIndex + 1} / {_dropdown.options.Count}";
            for (int i = 0; i < _choiceButtons.Length; i++)
            {
                int option = _firstChoice + i;
                var button = _choiceButtons[i];
                button.transform.parent.gameObject.SetActive(option < _dropdown.options.Count);
                if (option < _dropdown.options.Count) _choiceLabels[i].text = _dropdown.options[option].text;
            }
            SetFocus(_choiceButtons[_choiceIndex - _firstChoice]);
        }

        private void AcceptChoice(int index)
        {
            if (_dropdown == null || index < 0 || index >= _dropdown.options.Count) return;
            var dropdown = _dropdown;
            CloseChoice();
            dropdown.value = index;
        }

        private void CloseChoice()
        {
            if (!_choicePanel.gameObject.activeSelf) return;
            SetFocus(null);
            _choicePanel.gameObject.SetActive(false);
            _dropdown = null;
        }
    }
}
