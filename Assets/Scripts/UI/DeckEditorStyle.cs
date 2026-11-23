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
        private static Sprite _backdrop, _panel, _normal, _primary, _focused, _disabled;

        internal static void Backdrop(Transform parent) =>
            LotdUiKit.AddImage(LotdUiKit.FillRect("Backdrop", parent).gameObject,
                _backdrop ??= DrawBackdrop(), Color.white, false, false);

        internal static void AddHeader(Transform parent, string title)
        {
            var heading = LotdUiKit.CreateRect("MenuHeader", parent, new Vector2(.25f, .912f),
                new Vector2(.75f, .98f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(heading, "Heading", title, 34, 20, TextAnchor.MiddleCenter,
                LotdUiKit.TextPrimary, FontStyle.Bold);
            var line = LotdUiKit.CreateRect("HeaderRule", parent, new Vector2(.018f, .907f), new Vector2(.982f, .907f),
                Vector2.zero, new Vector2(0f, 1f));
            Fill(line, Edge, false);
        }

        internal static Image Fill(RectTransform rect, Color color, bool raycast = true) =>
            LotdUiKit.AddImage(rect.gameObject, LotdUiKit.WhiteSprite, color, false, raycast);

        internal static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var rect = LotdUiKit.CreateRect(name, parent, min, max, Vector2.zero, Vector2.zero);
            var image = LotdUiKit.AddImage(rect.gameObject,
                _panel ??= DrawPlate("Deck panel", Surface, new Color32(20, 26, 36, 255), Edge), Color.white);
            image.type = Image.Type.Sliced;
            return rect;
        }

        internal static Button Button(Transform parent, string name, string label, Action action, int fontSize = 18, bool primary = false)
        {
            var rect = LotdUiKit.FillRect(name, parent);
            var image = LotdUiKit.AddImage(rect.gameObject, LotdUiKit.WhiteSprite, Color.white);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ApplyControl(button, image, primary);
            LotdUiKit.AddFittedText(rect, "Label", label, fontSize, Mathf.Min(10, fontSize),
                TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);
            button.onClick.AddListener(() =>
            {
                LotdAudio.PlayClick();
                action?.Invoke();
            });
            return button;
        }

        internal static InputField Input(Transform parent, string name, string hint, int fontSize = 16)
        {
            var field = LotdUiKit.CreateInputField(parent, name, hint, fontSize);
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
            var dropdown = LotdUiKit.CreateLotdDropdown(parent, name, choices, changed);
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
            track.sprite = LotdUiKit.WhiteSprite;
            track.color = Inset;
            var thumb = (Image)scrollbar.targetGraphic;
            thumb.sprite = LotdUiKit.WhiteSprite;
            thumb.color = new Color32(104, 142, 156, 255);
        }

        private static void ApplyControl(Selectable control, Image image, bool primary = false)
        {
            _normal ??= DrawPlate("Deck control", Header, Surface, Edge);
            _primary ??= DrawPlate("Deck primary control", new Color32(38, 79, 78, 255),
                new Color32(26, 52, 56, 255), new Color32(91, 168, 157, 255));
            _focused ??= DrawPlate("Focused deck control", new Color32(61, 102, 102, 255),
                new Color32(32, 61, 71, 255), Accent);
            _disabled ??= DrawPlate("Disabled deck control", Surface, Inset, new Color32(47, 58, 69, 255));
            image.sprite = primary ? _primary : _normal;
            image.type = Image.Type.Sliced;
            control.transition = Selectable.Transition.SpriteSwap;
            control.spriteState = new SpriteState
            {
                selectedSprite = _focused, highlightedSprite = _focused, pressedSprite = _focused, disabledSprite = _disabled,
            };
        }

        private static Sprite DrawPlate(string name, Color top, Color bottom, Color edge)
        {
            const int size = 40;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                Color fill = Color.Lerp(bottom, top, y / (size - 1f));
                for (int x = 0; x < size; x++)
                {
                    int nearestHorizontal = Mathf.Min(x, size - 1 - x);
                    int nearestVertical = Mathf.Min(y, size - 1 - y);
                    int corner = nearestHorizontal + nearestVertical;
                    if (corner < 3) continue;
                    bool border = nearestHorizontal == 0 || nearestVertical == 0 || corner == 3;
                    pixels[y * size + x] = border ? edge : fill;
                }
            }
            return MakeSprite(name, size, size, pixels, Vector4.one * 5f);
        }

        private static Sprite DrawBackdrop()
        {
            const int width = 384, height = 216;
            var pixels = new Color32[width * height];
            var ink = new Color32(9, 14, 23, 255);
            var light = new Color32(45, 66, 78, 255);
            var plum = new Color32(45, 36, 58, 255);
            for (int y = 0; y < height; y++)
            {
                float v = y / (height - 1f);
                for (int x = 0; x < width; x++)
                {
                    float u = x / (width - 1f);
                    float glow = Mathf.Clamp01(1f - new Vector2((u - .32f) * 1.86f, (v - .97f) * 1.55f).sqrMagnitude);
                    float reflection = Mathf.Clamp01(1f - Mathf.Abs(u + v * .76f - .91f) * 2.4f);
                    Color color = Color.Lerp(ink, light, glow * glow * .69f);
                    color = Color.Lerp(color, plum, reflection * reflection * .24f);
                    pixels[y * width + x] = color;
                }
            }
            return MakeSprite("Deck background", width, height, pixels);
        }

        private static Sprite MakeSprite(string name, int width, int height, Color32[] pixels, Vector4 border = default)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 100f, 0,
                SpriteMeshType.FullRect, border);
        }
    }
}
