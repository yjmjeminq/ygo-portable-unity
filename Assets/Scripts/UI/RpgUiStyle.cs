using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal static class RpgUiStyle
    {
        private static Font _body, _strong;
        private static Sprite _panel, _accentPanel, _goldPanel, _dialogue, _speaker, _control, _beveledControl;
        private static readonly Color32 TextColor = new(234, 242, 247, 255);
        internal static readonly Color32 Cyan = new(111, 230, 244, 255);
        internal static readonly Color32 Gold = new(255, 205, 117, 255);
        internal enum Symbol { Dot, Arrow, Star, Hexagon, Ring, Lock, Alert }
        private static readonly Sprite[] Symbols = new Sprite[(int)Symbol.Alert + 1];

        internal static Sprite Glyph(Symbol symbol) => Symbols[(int)symbol] ??= DrawGlyph(symbol);

        private static Sprite DrawGlyph(Symbol symbol)
        {
            const int size = 48;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = 2f * (x + .5f) / size - 1f, py = 2f * (y + .5f) / size - 1f;
                float ax = Mathf.Abs(px), ay = Mathf.Abs(py);
                float radius = new Vector2(px, py).magnitude;
                float boundary = symbol switch
                {
                    Symbol.Dot => .72f - radius,
                    Symbol.Arrow => Mathf.Min(.8f - py, Mathf.Min((.8f - py) * .59f - ax, py + .41f - ax * .48f)),
                    Symbol.Hexagon => .95f - Mathf.Max(ay, .87f * ax + .5f * ay),
                    Symbol.Star => .77f - Mathf.Min(ax + ay * 2.6f, ay + ax * 2.6f),
                    Symbol.Ring => Mathf.Min(.94f - radius, radius - .80f),
                    Symbol.Lock => Mathf.Max(Mathf.Min(.58f - ax, .42f - Mathf.Abs(py + .28f)),
                        Mathf.Min(.45f - Mathf.Abs(new Vector2(px, py - .25f).magnitude),
                            Mathf.Min(new Vector2(px, py - .25f).magnitude - .29f, py - .06f))),
                    Symbol.Alert => Mathf.Max(Mathf.Min(.14f - ax, .40f - Mathf.Abs(py - .24f)),
                        .15f - new Vector2(px, py + .51f).magnitude),
                    _ => 0f,
                };
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(boundary * 22f + .5f));
            }
            return DuelUiChrome.CreateSprite($"RPG {symbol}", size, size, pixels);
        }

        internal static void Type(Text label, bool strong = false)
        {
            label.font = (strong
                ? _strong ??= Resources.Load<Font>("StoryFonts/Pretendard-SemiBold")
                : _body ??= Resources.Load<Font>("StoryFonts/Pretendard-Medium")) ?? DuelUiKit.FontKR;
            label.fontStyle = FontStyle.Normal;
        }

        internal static void Panel(RectTransform rect, bool accent = false, bool gold = false)
        {
            Sprite sprite;
            if (gold)
                sprite = _goldPanel ??= Bevel("RPG gold panel", 13, 236, Gold);
            else if (accent)
                sprite = _accentPanel ??= Bevel("RPG active panel", 13, 245, Cyan);
            else
                sprite = _panel ??= Bevel("RPG panel", 13, 229, new Color32(105, 157, 192, 255));
            SetPanel(rect, sprite);
        }

        internal static void Dialogue(RectTransform rect) => SetPanel(rect,
            _dialogue ??= Bevel("RPG dialogue glass", 22, 239, Cyan, Gold));

        internal static void Speaker(RectTransform rect) => SetPanel(rect,
            _speaker ??= Bevel("RPG dialogue nameplate", 13, 254, Cyan));

        private static void SetPanel(RectTransform rect, Sprite sprite)
        {
            var image = rect.GetComponent<Image>();
            if (image == null) image = DuelUiKit.AddImage(rect.gameObject, null, Color.white, false, false);
            image.sprite = sprite;
            image.color = Color.white;
            image.type = Image.Type.Sliced;
        }

        internal static void Control(Button button, bool primary = false, bool beveled = false)
        {
            var image = (Image)button.targetGraphic;
            image.overrideSprite = null;
            image.color = Color.white;
            image.sprite = beveled
                ? _beveledControl ??= Bevel("RPG action", 14, 255, Color.white, mask: true)
                : _control ??= Rounded("RPG control",
                    new Color32(245, 253, 255, 237), new Color32(216, 235, 249, 228), Color.white, 14f);
            image.type = Image.Type.Sliced;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = primary ? new Color32(34, 80, 91, 255) : new Color32(30, 50, 68, 250),
                highlightedColor = new Color32(54, 105, 125, 255),
                selectedColor = new Color32(64, 128, 142, 255),
                pressedColor = new Color32(45, 92, 109, 255),
                disabledColor = new Color32(22, 34, 47, 215),
                colorMultiplier = 1f,
                fadeDuration = .17f,
            };
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                Type(label, strong: primary);
                label.color = TextColor;
            }
        }

        private static Sprite Bevel(string name, float chamfer, byte opacity, Color line,
            Color? endLine = null, bool mask = false)
        {
            const int size = 96;
            var pixels = new Color32[size * size];
            Color dark = mask ? new Color32(185, 209, 229, opacity) : new Color32(7, 13, 25, opacity);
            Color upper = mask ? new Color32(222, 238, 249, opacity) : new Color32(18, 34, 53, opacity);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float right = size - x - 1, top = size - y - 1;
                float corner = Mathf.Min(x + top, right + y) - chamfer;
                float side = Mathf.Min(Mathf.Min(x, right), Mathf.Min(y, top));
                float edge = Mathf.Min(side - 1, corner * .7071f);
                float highlight = Mathf.Min(top, (x + top - chamfer) * .7071f + 1f);
                Color accent = endLine is Color tail && x >= 71 ? tail : line;
                Color color = Color.Lerp(dark, upper, y / (size - 1f));
                if ((x < 22 || x >= 74) && ((x + y) & 7) <= 1)
                    color = Color.Lerp(color, accent, .035f);
                color = Color.Lerp(color, accent, .10f * Mathf.Exp(-Mathf.Max(0f, highlight - 2f) / 4f));
                float stroke = Mathf.Max(.29f * Mathf.Clamp01(1.7f - edge),
                    .80f * Mathf.Clamp01(1.6f - Mathf.Abs(highlight - 2f)));
                color = Color.Lerp(color, accent, stroke);
                color.a *= Mathf.Clamp01(edge + .5f);
                pixels[y * size + x] = color;
            }
            var sprite = DuelUiChrome.CreateSprite(name, size, size, pixels, Vector4.one * 26f);
            sprite.name = name;
            return sprite;
        }

        private static Sprite Rounded(string name, Color top, Color bottom, Color edge, float radius)
        {
            const int size = 64;
            const float corner = size * .5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                var fill = Color.Lerp(bottom, top, y / (size - 1f));
                float py = Mathf.Abs(y + .5f - corner) - (corner - radius - 1f);
                for (int x = 0; x < size; x++)
                {
                    float px = Mathf.Abs(x + .5f - corner) - (corner - radius - 1f);
                    float distance = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude +
                        Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                    var color = Color.Lerp(fill, edge, Mathf.Clamp01(distance + 1.5f));
                    color.a *= Mathf.Clamp01(.5f - distance);
                    pixels[y * size + x] = color;
                }
            }
            var sprite = DuelUiChrome.CreateSprite(name, size, size, pixels, Vector4.one * 16f);
            sprite.name = name;
            return sprite;
        }
    }
}
