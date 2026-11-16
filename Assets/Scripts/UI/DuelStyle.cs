using System;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal static class DuelStyle
    {
        internal static readonly Color PanelColor = new Color(0.052f, 0.085f, 0.104f, 0.96f);
        internal static readonly Color HeaderColor = new Color(0.10f, 0.17f, 0.20f, 0.98f);
        internal static readonly Color BorderColor = new Color(0.51f, 0.66f, 0.72f, 0.38f);
        internal static readonly Color Player = new Color(0.37f, 0.85f, 1f);
        internal static readonly Color Opponent = new Color(1f, 0.40f, 0.30f);
        internal static readonly Color Gold = new Color(0.98f, 0.82f, 0.45f);
        internal static readonly Color Muted = new Color(0.75f, 0.83f, 0.85f);

        private static Texture2D _stone;
        private static Sprite _disc;
        internal static Texture2D Stone => _stone ??= Resources.Load<Texture2D>("Duel/StonePaving");
        internal static Color Side(bool opponent) => opponent ? Opponent : Player;

        internal static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max,
            Vector2 offsetMin = default, Vector2 offsetMax = default, Color? fill = null, Color? border = null)
        {
            var panel = LotdUiKit.CreateBorderedPanel(parent, name, min, max, offsetMin, offsetMax,
                fill ?? PanelColor, border ?? BorderColor);
            panel.GetComponent<Outline>().effectDistance = new Vector2(1f, -1f);
            return panel;
        }

        internal static Button Button(Transform parent, string name, string label, Action action, int size,
            bool gold = false)
        {
            var button = LotdUiKit.CreateLotdButton(parent, name, label, action, size);
            ApplyButton(button, gold);
            return button;
        }

        internal static void ApplyButton(Button button, bool accent)
        {
            var image = button.image;
            image.sprite = LotdUiKit.WhiteSprite;
            image.overrideSprite = null;
            image.type = Image.Type.Simple;
            var normal = accent ? new Color(0.29f, 0.42f, 0.27f) : new Color(0.12f, 0.22f, 0.28f);
            var palette = button.colors;
            palette.normalColor = normal;
            palette.highlightedColor = accent ? new Color(0.39f, 0.54f, 0.26f) : new Color(0.19f, 0.35f, 0.42f);
            palette.pressedColor = new Color(0.15f, 0.36f, 0.40f);
            palette.selectedColor = palette.normalColor;
            palette.disabledColor = new Color(0.08f, 0.13f, 0.16f, 0.66f);
            palette.fadeDuration = 0.09f;
            button.colors = palette;
            button.transition = Selectable.Transition.ColorTint;
            var stroke = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
            stroke.effectColor = accent ? new Color(0.61f, 0.80f, 0.46f, 0.65f) : BorderColor;
            stroke.effectDistance = new Vector2(1f, -1f);
        }

        internal static Sprite Disc
        {
            get
            {
                if (_disc != null) return _disc;
                const int size = 96;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var p = new Vector2((x + .5f) * 2f / size - 1f, (y + .5f) * 2f / size - 1f);
                        float r = p.magnitude;
                        float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.73f, 0.94f, r));
                        float light = Mathf.Lerp(0.44f, 0.83f, (p.y + 1f) * 0.5f);
                        float rim = Mathf.Lerp(light, 1f, edge) * (r < 0.965f ? 1f : 0.55f);
                        pixels[y * size + x] = new Color(rim, rim, rim, Mathf.Clamp01((1f - r) * 47f));
                    }
                }
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Duel button rim", wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _disc = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                return _disc;
            }
        }

        internal static void Shadow(Text label, float distance = 1.25f)
        {
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.01f, 0.02f, 0.025f, 0.9f);
            shadow.effectDistance = new Vector2(distance, -distance);
        }
    }
}
