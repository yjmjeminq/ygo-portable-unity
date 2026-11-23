using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal static class DuelUiChrome
    {
        private static readonly Color32 Ink = new(13, 21, 30, 248);
        private static readonly Color32 Glass = new(31, 48, 60, 244);
        private static readonly Color32 Edge = new(71, 103, 116, 255);
        private static readonly Color32 Teal = new(134, 223, 218, 255);
        private static readonly Color32 Gold = new(236, 203, 132, 255);
        private static Sprite _backdrop, _panel, _header, _control, _focus, _primary, _primaryFocus, _disabled;

        internal static Sprite Backdrop => _backdrop ??= DrawBackdrop();
        internal static Sprite Panel => _panel ??= DrawPlate("Menu panel", Glass, Ink, Edge);
        internal static Sprite Header => _header ??= DrawPlate("Menu header", new Color32(42, 71, 79, 255), Glass, Edge);
        internal static Sprite Control => _control ??= DrawPlate("Control", Glass, Ink, Edge);
        internal static Sprite Focus => _focus ??= DrawPlate("Focused control", new Color32(37, 85, 87, 255), Glass, Teal);
        internal static Sprite Primary => _primary ??= DrawPlate("Primary control",
            new Color32(76, 65, 44, 255), new Color32(36, 37, 38, 255), new Color32(151, 129, 88, 255));
        private static Sprite PrimaryFocus => _primaryFocus ??= DrawPlate("Focused primary control",
            new Color32(107, 86, 52, 255), new Color32(59, 51, 40, 255), Gold);
        private static Sprite Disabled => _disabled ??= DrawPlate("Disabled control",
            new Color32(38, 44, 50, 224), Ink, new Color32(59, 67, 76, 255));

        internal static void ApplyControl(Selectable control, Image image, bool primary = false)
        {
            image.sprite = primary ? Primary : Control;
            image.type = Image.Type.Sliced;
            var focus = primary ? PrimaryFocus : Focus;
            control.transition = Selectable.Transition.SpriteSwap;
            control.spriteState = new SpriteState
            {
                highlightedSprite = focus,
                selectedSprite = focus,
                pressedSprite = focus,
                disabledSprite = Disabled,
            };
        }

        internal static Sprite DrawPlate(string name, Color top, Color bottom, Color edge)
        {
            const int size = 40;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                Color fill = Color.Lerp(bottom, top, y / (size - 1f));
                for (int x = 0; x < size; x++)
                {
                    int nearestX = Mathf.Min(x, size - 1 - x);
                    int nearestY = Mathf.Min(y, size - 1 - y);
                    int corner = nearestX + nearestY;
                    if (corner < 3) continue;
                    pixels[y * size + x] = nearestX == 0 || nearestY == 0 || corner == 3 ? edge : fill;
                }
            }
            return CreateSprite(name, size, size, pixels, Vector4.one * 5f);
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
                    pixels[y * width + x] = Color.Lerp(color, plum, reflection * reflection * .24f);
                }
            }
            return CreateSprite("Duel menu backdrop", width, height, pixels);
        }

        internal static Sprite CreateSprite(string name, int width, int height, Color32[] pixels, Vector4 border = default)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.one * .5f, 100f, 0,
                SpriteMeshType.FullRect, border);
        }
    }
}
