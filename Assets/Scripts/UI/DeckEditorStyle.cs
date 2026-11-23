using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal static class DeckEditorStyle
    {
        internal static readonly Color Inset = new Color32(16, 21, 29, 255);
        internal static readonly Color Surface = new Color32(29, 37, 48, 255);
        internal static readonly Color Header = new Color32(39, 52, 65, 255);
        internal static readonly Color Edge = new Color32(72, 91, 109, 255);
        internal static readonly Color Accent = new Color32(143, 230, 214, 255);
        internal static readonly Color FocusTrack = new Color32(112, 194, 188, 164);
        internal static readonly Color Scrim = new Color32(9, 14, 21, 221);
        private static Sprite _panel, _normal, _primary, _focused, _disabled;

        internal static void Backdrop(Transform parent) =>
            DuelUiKit.AddImage(DuelUiKit.FillRect("Backdrop", parent).gameObject,
                DuelUiChrome.Backdrop, Color.white, false, false);

        internal static void AddHeader(Transform parent, string title)
        {
            var heading = DuelUiKit.CreateRect("MenuHeader", parent, new Vector2(.25f, .912f),
                new Vector2(.75f, .98f), Vector2.zero, Vector2.zero);
            DuelUiKit.AddFittedText(heading, "Heading", title, 34, 20, TextAnchor.MiddleCenter,
                DuelUiKit.TextPrimary, FontStyle.Bold);
            var line = DuelUiKit.CreateRect("HeaderRule", parent, new Vector2(.018f, .907f), new Vector2(.982f, .907f),
                Vector2.zero, new Vector2(0f, 1f));
            Fill(line, Edge, false);
        }

        internal static Image Fill(RectTransform rect, Color color, bool raycast = true) =>
            DuelUiKit.AddImage(rect.gameObject, DuelUiKit.WhiteSprite, color, false, raycast);

        internal static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var rect = DuelUiKit.CreateRect(name, parent, min, max, Vector2.zero, Vector2.zero);
            var image = DuelUiKit.AddImage(rect.gameObject,
                _panel ??= DuelUiChrome.DrawPlate("Deck panel", Surface, new Color32(20, 26, 36, 255), Edge), Color.white);
            image.type = Image.Type.Sliced;
            return rect;
        }

        internal static Button Button(Transform parent, string name, string label, Action action, int fontSize = 18, bool primary = false)
        {
            var rect = DuelUiKit.FillRect(name, parent);
            var image = DuelUiKit.AddImage(rect.gameObject, DuelUiKit.WhiteSprite, Color.white);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ApplyControl(button, image, primary);
            DuelUiKit.AddFittedText(rect, "Label", label, fontSize, Mathf.Min(10, fontSize),
                TextAnchor.MiddleCenter, DuelUiKit.TextPrimary, FontStyle.Bold);
            button.onClick.AddListener(() =>
            {
                DuelAudio.PlayClick();
                action?.Invoke();
            });
            return button;
        }

        internal static InputField Input(Transform parent, string name, string hint, int fontSize = 16)
        {
            var field = DuelUiKit.CreateInputField(parent, name, hint, fontSize);
            field.GetComponent<Image>().color = Inset;
            field.GetComponent<Outline>().effectColor = Edge;
            if (field.placeholder is Text text)
            {
                text.fontStyle = FontStyle.Normal;
                text.color = new Color32(166, 182, 196, 255);
            }
            field.selectionColor = new Color(Accent.r, Accent.g, Accent.b, .3f);
            return field;
        }

        internal static Dropdown Dropdown(Transform parent, string name, IReadOnlyList<string> choices,
            Action<int> changed)
        {
            var dropdown = DuelUiKit.CreateDropdown(parent, name, choices, changed);
            ApplyControl(dropdown, dropdown.image);
            dropdown.transform.Find("Arrow").GetComponent<Text>().color = Accent;
            var list = dropdown.template;
            list.GetComponent<Image>().color = Inset;
            list.GetComponent<Outline>().effectColor = Edge;
            var row = list.Find("Viewport/Content/Item").GetComponent<Toggle>();
            row.image.color = Surface;
            row.graphic.color = new Color(Accent.r, Accent.g, Accent.b, .27f);
            if (list.GetComponent<ScrollRect>().verticalScrollbar is { } scroll)
                ApplyScrollbar(scroll);
            return dropdown;
        }

        internal static void ApplyScrollbar(Scrollbar scrollbar)
        {
            var track = scrollbar.GetComponent<Image>();
            track.sprite = DuelUiKit.WhiteSprite;
            track.color = Inset;
            var thumb = (Image)scrollbar.targetGraphic;
            thumb.sprite = DuelUiKit.WhiteSprite;
            thumb.color = new Color32(104, 142, 156, 255);
        }

        private static void ApplyControl(Selectable control, Image image, bool primary = false)
        {
            _normal ??= DuelUiChrome.DrawPlate("Deck control", Header, Surface, Edge);
            _primary ??= DuelUiChrome.DrawPlate("Deck primary control", new Color32(38, 79, 78, 255),
                new Color32(26, 52, 56, 255), new Color32(91, 168, 157, 255));
            _focused ??= DuelUiChrome.DrawPlate("Focused deck control", new Color32(61, 102, 102, 255),
                new Color32(32, 61, 71, 255), Accent);
            _disabled ??= DuelUiChrome.DrawPlate("Disabled deck control", Surface, Inset, new Color32(47, 58, 69, 255));
            image.sprite = primary ? _primary : _normal;
            image.type = Image.Type.Sliced;
            control.transition = Selectable.Transition.SpriteSwap;
            control.spriteState = new SpriteState
            {
                selectedSprite = _focused, highlightedSprite = _focused, pressedSprite = _focused, disabledSprite = _disabled,
            };
        }

    }
}
