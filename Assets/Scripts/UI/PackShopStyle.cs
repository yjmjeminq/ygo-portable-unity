using System;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal static class PackShopStyle
    {
        internal static readonly Color Accent = new(.79f, 1f, .38f);
        internal static readonly Color TextMuted = new(.72f, .76f, .87f);
        private static readonly Color Ink = new(.07f, .09f, .16f);
        private static Sprite _backdrop;

        internal static void Backdrop(RectTransform root)
        {
            var background = LotdUiKit.FillRect("ShopBackdrop", root);
            LotdUiKit.AddImage(background.gameObject, _backdrop ??= CreateGradient(), Color.white);
            var art = LotdUiKit.FillRect("Texture", background);
            LotdUiKit.AddImage(art.gameObject, LotdUiKit.LoadSprite("LOTD/Background/bg_vrains"),
                new Color(.45f, .54f, .79f, .12f), false, false);
        }

        internal static void Header(Transform parent, string label)
        {
            var box = LotdUiKit.CreateRect("ShopHeader", parent, new Vector2(.035f, .902f),
                new Vector2(.795f, .98f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(box, "Title", label, 33, 22, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            var rule = LotdUiKit.CreateRect("HeaderRule", parent, new Vector2(.032f, .901f),
                new Vector2(.969f, .903f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(rule.gameObject, LotdUiKit.WhiteSprite, new Color(.64f, .69f, .94f, .28f), false, false);
        }

        internal static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var box = LotdUiKit.CreateRect(name, parent, min, max, Vector2.zero, Vector2.zero);
            var panel = LotdUiKit.AddImage(box.gameObject, LotdUiKit.WhiteSprite, new Color(.071f, .083f, .158f, .96f));
            AddOutline(panel, new Color(.54f, .60f, .88f, .32f), 1f);
            return box;
        }

        internal static Button Button(Transform parent, string name, string caption, Action click, int fontSize = 18,
            bool primary = false)
        {
            var holder = LotdUiKit.FillRect(name, parent);
            var image = LotdUiKit.AddImage(holder.gameObject, LotdUiKit.WhiteSprite, Color.white);
            AddOutline(image, primary ? Accent : new Color(.50f, .55f, .76f, .64f), 1f);
            var button = holder.gameObject.AddComponent<PackShopButtonLabel>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = primary ? new Color(.70f, .90f, .30f) : new Color(.145f, .167f, .285f);
            colors.highlightedColor = colors.selectedColor = primary ? Accent : new Color(.27f, .33f, .48f);
            colors.pressedColor = primary ? new Color(.54f, .73f, .21f) : new Color(.20f, .24f, .38f);
            colors.disabledColor = new Color(.17f, .19f, .28f, .58f);
            button.colors = colors;
            var text = LotdUiKit.AddFittedText(holder, "Label", caption, fontSize, Mathf.Max(10, fontSize - 6),
                TextAnchor.MiddleCenter, primary ? Ink : Color.white, FontStyle.Bold);
            button.Label = text;
            button.Normal = primary ? Ink : Color.white;
            button.onClick.AddListener(() =>
            {
                LotdAudio.PlayClick();
                click?.Invoke();
            });
            return button;
        }

        internal static void AddOutline(Graphic target, Color color, float width)
        {
            var outline = target.gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(width, width);
            outline.effectColor = color;
            outline.useGraphicAlpha = false;
        }

        private static Sprite CreateGradient()
        {
            var texture = new Texture2D(2, 64, TextureFormat.RGBA32, false)
            {
                name = "Pack Shop Gradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var bottom = new Color(.18f, .105f, .26f);
            var middle = new Color(.084f, .09f, .175f);
            var top = new Color(.036f, .044f, .09f);
            var pixels = new Color[128];
            for (int row = 0; row < 64; row++)
            {
                float y = row / 63f;
                pixels[2 * row] = pixels[2 * row + 1] = y < .57f
                    ? Color.Lerp(bottom, middle, y / .57f)
                    : Color.Lerp(middle, top, (y - .57f) / .43f);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, 2f, 64f), new Vector2(.5f, .5f), 100f);
        }
    }

    internal sealed class PackShopButtonLabel : Button
    {
        internal Text Label { get; set; }
        private Color _normal;
        internal Color Normal
        {
            get => _normal;
            set
            {
                _normal = value;
                ApplyLabel(IsInteractable());
            }
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            ApplyLabel(state != SelectionState.Disabled);
        }

        private void ApplyLabel(bool interactable)
        {
            if (Label != null) Label.color = interactable ? _normal : PackShopStyle.TextMuted;
        }
    }
}
