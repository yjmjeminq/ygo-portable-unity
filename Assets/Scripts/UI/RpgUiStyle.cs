using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal static class RpgUiStyle
    {
        private static Font _body, _strong, _reading, _display;
        private static Sprite _panel, _accentPanel, _goldPanel, _dialogue, _speaker, _glass, _control, _action, _prominent;
        private static readonly Color32 TextColor = new(239, 244, 252, 255);
        internal static readonly Color32 Cyan = new(111, 230, 244, 255);
        internal static readonly Color32 Gold = new(255, 205, 117, 255);

        internal static Color SpeakerColor(string name) => name switch
        {
            "이자요이 아키" => new Color32(255, 64, 129, 255),
            "후도 유세이" => new Color32(0, 229, 255, 255),
            "잭 아틀라스" => new Color32(255, 82, 82, 255),
            "크로우 호건" => new Color32(255, 160, 83, 255),
            "예거" => new Color32(224, 64, 251, 255),
            "카드 매점 직원" => new Color32(131, 232, 190, 255),
            _ => new Color32(255, 193, 7, 255),
        };
        internal enum Symbol { Dot, Arrow, Star, Hexagon, Ring, Lock, Alert, Chevron }
        private static readonly Sprite[] Symbols = new Sprite[(int)Symbol.Chevron + 1];

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
                    Symbol.Chevron => Mathf.Min(.53f - ay, .103f - Mathf.Abs(px + ay * .92f - .27f)),
                    _ => 0f,
                };
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(boundary * 22f + .5f));
            }
            return DuelUiChrome.CreateSprite($"RPG {symbol}", size, size, pixels);
        }

        internal static void Type(Text label, bool strong = false)
        {
            SetFont(label, strong
                ? _strong ??= Resources.Load<Font>("StoryFonts/Pretendard-SemiBold")
                : _body ??= Resources.Load<Font>("StoryFonts/Pretendard-Medium"));
        }

        internal static void Reading(Text label) => SetFont(label,
            _reading ??= Resources.Load<Font>("StoryFonts/GowunDodum-Regular"));

        internal static void Display(Text label) => SetFont(label,
            _display ??= Resources.Load<Font>("StoryFonts/Paperlogy-7Bold"));

        private static void SetFont(Text label, Font font)
        {
            label.font = font ?? DuelUiKit.FontKR;
            label.fontStyle = FontStyle.Normal;
        }

        internal static void Glass(RectTransform rect) => SetPanel(rect, _glass ??= Rounded("RPG soft glass",
            new Color32(29, 43, 67, 250), new Color32(16, 22, 40, 250), new Color32(118, 153, 186, 125), 23f));

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

        internal static void Control(Button button, bool primary = false, bool prominent = false, bool animated = true)
        {
            var image = (Image)button.targetGraphic;
            image.overrideSprite = null;
            image.color = Color.white;
            if (prominent)
                image.sprite = _prominent ??= Rounded("RPG illuminated action", new Color32(64, 113, 127, 255),
                    new Color32(29, 55, 87, 255), new Color32(139, 226, 228, 231), 26f);
            else if (primary)
                image.sprite = _action ??= Rounded("RPG primary control", new Color32(48, 89, 109, 251),
                    new Color32(24, 49, 69, 248), new Color32(124, 204, 213, 195), 24f);
            else
                image.sprite = _control ??= Rounded("RPG secondary control", new Color32(42, 56, 82, 227),
                    new Color32(20, 32, 53, 234), new Color32(146, 165, 208, 131), 23f);
            image.type = Image.Type.Sliced;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = new Color32(219, 228, 239, 245),
                highlightedColor = Color.white,
                selectedColor = new Color32(240, 255, 255, 255),
                pressedColor = new Color32(157, 211, 228, 255),
                disabledColor = new Color32(113, 129, 159, 185),
                colorMultiplier = 1f,
                fadeDuration = .17f,
            };
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                if (primary) Type(label, strong: true);
                else Reading(label);
                label.color = TextColor;
            }
            if (animated) MenuButtonMotion.Attach(button, label?.rectTransform,
                primary ? Cyan : new Color32(192, 203, 247, 255), surface: image);
        }

        private static Sprite Bevel(string name, float chamfer, byte opacity, Color line,
            Color? endLine = null)
        {
            const int size = 96;
            var pixels = new Color32[size * size];
            Color dark = new Color32(7, 13, 25, opacity);
            Color upper = new Color32(18, 34, 53, opacity);
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
            const int size = 96;
            const int border = 28;
            const float corner = size * .5f;
            var pixels = new Color32[size * size];
            Color center = Color.Lerp(bottom, top, .48f);
            for (int y = 0; y < size; y++)
            {
                float lower = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((border - y) / (float)border));
                float upper = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((y - (size - border - 1)) / (float)border));
                // Keep the two sliced edges the same color when a short button collapses the center strip.
                var fill = Color.Lerp(Color.Lerp(center, bottom, lower), top, upper);
                float py = Mathf.Abs(y + .5f - corner) - (corner - radius - 1f);
                for (int x = 0; x < size; x++)
                {
                    float px = Mathf.Abs(x + .5f - corner) - (corner - radius - 1f);
                    float distance = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude +
                        Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                    float sheen = .10f * upper * Mathf.Exp(Mathf.Min(0f, distance + 3f) / 6f);
                    var color = Color.Lerp(fill, edge, Mathf.Max(sheen, Mathf.Clamp01(distance + 1.5f)));
                    color.a *= Mathf.Clamp01(.5f - distance);
                    pixels[y * size + x] = color;
                }
            }
            var sprite = DuelUiChrome.CreateSprite(name, size, size, pixels, Vector4.one * border);
            sprite.name = name;
            return sprite;
        }
    }
}
