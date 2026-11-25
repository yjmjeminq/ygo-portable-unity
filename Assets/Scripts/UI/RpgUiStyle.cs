using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal static class RpgUiStyle
    {
        private static Font _body, _strong;
        private static Sprite _panel, _accentPanel, _control;
        private static readonly Color32 TextColor = new(234, 242, 247, 255);

        internal static void Type(Text label, bool strong = false)
        {
            label.font = (strong
                ? _strong ??= Resources.Load<Font>("StoryFonts/Pretendard-SemiBold")
                : _body ??= Resources.Load<Font>("StoryFonts/Pretendard-Medium")) ?? DuelUiKit.FontKR;
            label.fontStyle = FontStyle.Normal;
        }

        internal static void Panel(RectTransform rect, bool accent = false)
        {
            var image = rect.GetComponent<Image>();
            if (image == null) image = DuelUiKit.AddImage(rect.gameObject, null, Color.white, false, false);
            image.sprite = accent
                ? _accentPanel ??= Rounded("RPG active panel", new Color32(25, 45, 57, 246),
                    new Color32(14, 27, 39, 246), new Color32(98, 154, 166, 224), 13f)
                : _panel ??= Rounded("RPG panel", new Color32(25, 39, 54, 244),
                    new Color32(13, 24, 38, 241), new Color32(89, 113, 136, 189), 13f);
            image.color = Color.white;
            image.type = Image.Type.Sliced;
        }

        internal static void Control(Button button, bool primary = false)
        {
            var image = (Image)button.targetGraphic;
            image.overrideSprite = null;
            image.color = Color.white;
            image.sprite = _control ??= Rounded("RPG control",
                new Color32(237, 248, 255, 255), new Color32(210, 231, 246, 255), Color.white, 11f);
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
