using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal static class PackShopStyle
    {
        internal enum ButtonKind
        {
            Standard, Category, Filter, Tile, SinglePurchase, BundlePurchase, OpeningAction, OpeningSkip,
        }

        internal static readonly Color Accent = new Color32(204, 255, 0, 255);
        internal static readonly Color TextPrimary = new Color32(246, 246, 246, 255);
        internal static readonly Color TextMuted = new Color32(189, 191, 195, 255);
        internal static readonly Color Line = new Color32(143, 148, 156, 255);
        internal static readonly Color Scrim = new Color32(0, 0, 0, 210);
        internal static readonly Color SurfaceInset = new Color32(15, 16, 20, 245);
        internal static readonly Color SelectionFill = new Color32(23, 52, 70, 255);
        internal static readonly Color Danger = new Color32(255, 115, 115, 255);
        internal static readonly Color Ink = new Color32(5, 8, 10, 255);
        private static readonly Dictionary<(ButtonKind kind, bool primary, bool selected, bool focused), Sprite> ButtonSprites = new();
        private static Sprite _panelSprite;

        internal static void Backdrop(RectTransform root)
        {
            var background = DuelUiKit.FillRect("ShopBackdrop", root);
            DuelUiKit.AddImage(background.gameObject, DuelUiKit.LoadSprite("PackShop/ShopBGBase02"), Color.white);
        }

        internal static void Header(Transform parent, string label)
        {
            var box = DuelUiKit.CreateRect("ShopHeader", parent, new Vector2(.035f, .902f),
                new Vector2(.795f, .98f), Vector2.zero, Vector2.zero);
            DuelUiKit.AddFittedText(box, "Title", label, 33, 22, TextAnchor.MiddleLeft, TextPrimary, FontStyle.Bold);
            var rule = DuelUiKit.CreateRect("HeaderRule", parent, new Vector2(.032f, .901f),
                new Vector2(.969f, .903f), Vector2.zero, Vector2.zero);
            DuelUiKit.AddImage(rule.gameObject, DuelUiKit.WhiteSprite, Alpha(Line, .63f), false, false);
        }

        internal static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max, float opacity = .78f)
        {
            var box = DuelUiKit.CreateRect(name, parent, min, max, Vector2.zero, Vector2.zero);
            var panel = DuelUiKit.AddImage(box.gameObject, _panelSprite ??= CreatePanelSprite(),
                Alpha(Color.white, opacity));
            panel.type = Image.Type.Sliced;
            return box;
        }

        internal static InputField Search(Transform parent, string name, string placeholder, int fontSize = 16)
        {
            var field = DuelUiKit.CreateInputField(parent, name, placeholder, fontSize);
            field.GetComponent<Image>().color = new Color32(63, 65, 69, 247);
            field.GetComponent<Outline>().effectColor = Line;
            field.textComponent.color = TextPrimary;
            if (field.placeholder is Text hint)
            {
                hint.color = TextMuted;
                hint.fontStyle = FontStyle.Normal;
            }
            field.customCaretColor = true;
            field.caretColor = TextPrimary;
            field.selectionColor = Alpha(Accent, .45f);
            return field;
        }

        internal static Scrollbar AddVerticalScrollbar(Transform parent)
        {
            var scrollbar = DuelUiKit.AddVerticalScrollbar(parent);
            var track = scrollbar.GetComponent<Image>();
            track.sprite = DuelUiKit.WhiteSprite;
            track.color = new Color32(47, 49, 54, 255);
            var thumb = (Image)scrollbar.targetGraphic;
            thumb.sprite = DuelUiKit.WhiteSprite;
            thumb.color = Line;
            return scrollbar;
        }

        internal static Button Button(Transform parent, string name, string caption, Action click, int fontSize = 18,
            bool primary = false, ButtonKind kind = ButtonKind.Standard)
        {
            var holder = DuelUiKit.FillRect(name, parent);
            var image = DuelUiKit.AddImage(holder.gameObject, DuelUiKit.WhiteSprite, Color.white);
            image.type = Image.Type.Sliced;
            var button = holder.gameObject.AddComponent<PackShopButtonLabel>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.Label = DuelUiKit.AddFittedText(holder, "Label", caption, fontSize, Mathf.Max(10, fontSize - 6),
                TextAnchor.MiddleCenter, TextPrimary, FontStyle.Bold);
            button.Configure(kind, primary);
            button.onClick.AddListener(() =>
            {
                DuelAudio.PlayClick();
                click?.Invoke();
            });
            return button;
        }

        internal static void SetSelected(Button button, bool selected)
        {
            if (button is PackShopButtonLabel themed) themed.SetSelected(selected);
        }

        internal static void ShowCardPreview(CompositeCardView visual, uint code)
        {
            bool changed = visual.CurrentCode != code;
            if (changed) visual.SetCard(code);
            visual.gameObject.SetActive(code != 0);
            if (changed && code != 0) UiEntranceMotion.Play(visual.gameObject, .72f, .14f);
        }

        internal static Color RarityColor(Core.CardRarity rarity) => rarity switch
        {
            Core.CardRarity.Rare => new Color32(18, 147, 235, 255),
            Core.CardRarity.SuperRare => new Color32(255, 143, 26, 255),
            Core.CardRarity.UltraRare => new Color32(187, 68, 228, 255),
            _ => new Color32(126, 131, 141, 255),
        };

        internal static Sprite ButtonSprite(ButtonKind kind, bool primary, bool selected, bool focused)
        {
            var key = (kind, primary, selected, focused);
            if (ButtonSprites.TryGetValue(key, out var sprite)) return sprite;
            sprite = LoadMasterDuelButton(kind, selected, focused) ?? DrawButton(kind, primary, selected || focused);
            ButtonSprites.Add(key, sprite);
            return sprite;
        }

        private static Sprite LoadMasterDuelButton(ButtonKind kind, bool selected, bool focused)
        {
            var suffix = selected ? "_On" : focused ? "_Over" : "";
            string name;
            Vector4 slices;
            switch (kind)
            {
                case ButtonKind.Category:
                    name = "GUI_CommonButtonTab" + suffix;
                    slices = selected ? new Vector4(24, 14, 24, 21) : new Vector4(21, focused ? 6 : 7, 3, 19);
                    break;
                case ButtonKind.Filter:
                    name = "GUI_CommonButtonList" + suffix;
                    slices = selected || focused ? Vector4.one * 16f : new Vector4(13, 7, 3, 9);
                    break;
                case ButtonKind.SinglePurchase:
                    name = focused ? "GUI_CommonButtonShopR_Over" : "GUI_CommonButtonShopR";
                    slices = Vector4.zero;
                    break;
                case ButtonKind.BundlePurchase:
                    name = focused ? "GUI_ShopBuy_ButtonShopSR_Over" : "GUI_ShopBuy_ButtonShopSR";
                    slices = Vector4.zero;
                    break;
                default:
                    return null;
            }

            var texture = Resources.Load<Texture2D>("PackShop/" + name);
            return texture == null ? null : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, slices);
        }

        private static Sprite DrawButton(ButtonKind kind, bool primary, bool active)
        {
            Color bottom = Ink;
            Color top = Ink;
            Color edge = Line;
            Color marker = Color.clear;
            bool bevel = kind != ButtonKind.Standard;
            if (kind == ButtonKind.SinglePurchase)
            {
                top = new Color32(2, 125, 145, 255);
                bottom = new Color32(4, 177, 243, 255);
                edge = new Color32(48, 235, 239, 255);
            }
            else if (kind == ButtonKind.BundlePurchase)
            {
                top = new Color32(145, 130, 7, 255);
                bottom = new Color32(250, 239, 3, 255);
                edge = new Color32(255, 249, 89, 255);
            }
            else if (kind == ButtonKind.OpeningAction)
            {
                top = new Color32(30, 28, 34, 204);
                bottom = new Color32(17, 16, 21, 192);
                edge = active ? TextPrimary : new Color32(93, 90, 96, 230);
            }
            else if (kind == ButtonKind.OpeningSkip)
            {
                top = new Color32(12, 11, 14, 219);
                bottom = new Color32(1, 1, 3, 232);
                edge = active ? TextPrimary : new Color32(114, 114, 114, 230);
            }
            else if (primary || active && (kind is ButtonKind.Category or ButtonKind.Filter))
            {
                top = new Color32(170, 209, 49, 255);
                bottom = new Color32(134, 177, 35, 255);
                edge = new Color32(213, 255, 61, 255);
                bevel = true;
            }
            else if (kind is ButtonKind.Category or ButtonKind.Filter)
            {
                top = new Color32(43, 74, 94, 255);
                bottom = SelectionFill;
                marker = Accent;
            }
            else if (active)
            {
                top = kind == ButtonKind.Tile ? new Color32(16, 19, 10, 255) : SelectionFill;
                bottom = kind == ButtonKind.Tile ? Ink : SelectionFill;
                edge = Accent;
            }

            const int width = 72;
            const int height = 36;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int border = Mathf.Min(Mathf.Min(x, y), Mathf.Min(width - 1 - x, height - 1 - y));
                if (bevel)
                    border = Mathf.Min(border, Mathf.Min(x + height - 1 - y - 8, width - 1 - x + y - 8));
                if (border < 0) continue;
                Color color = Color.Lerp(bottom, top, y / (float)(height - 1));
                if (marker.a > 0f && y < 2) color = marker;
                else if (border == 0) color = edge;
                else if (primary || active || kind == ButtonKind.SinglePurchase || kind == ButtonKind.BundlePurchase)
                    color = Color.Lerp(color, edge, .57f * Mathf.Clamp01((6 - border) / 6f));
                pixels[y * width + x] = color;
            }
            var slices = kind == ButtonKind.Tile ? new Vector4(12f, 10f, 12f, 10f) : new Vector4(12f, 0, 12f, 0);
            return CreateSprite($"Shop {kind} Button", pixels, width, height, slices);
        }

        private static Sprite CreatePanelSprite()
        {
            const int size = 6;
            var pixels = new Color32[size * size];
            var border = new Color32(103, 105, 113, 255);
            var surface = new Color32(8, 9, 13, 255);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = x == 0 || y == 0 || x == size - 1 || y == size - 1 ? border : surface;
            return CreateSprite("Shop Panel", pixels, size, size, Vector4.one);
        }

        private static Color Alpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Sprite CreateSprite(string name, Color32[] pixels, int width, int height, Vector4 border)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f),
                100f, 0, SpriteMeshType.FullRect, border);
        }
    }

    internal sealed class PackShopButtonLabel : Button
    {
        internal Text Label { get; set; }
        private Color _normal = PackShopStyle.TextPrimary;
        private PackShopStyle.ButtonKind _kind;
        private bool _primary;
        private bool _selected;
        private PackTileArtMotion _artMotion;

        internal Color Normal
        {
            get => _normal;
            set
            {
                _normal = value;
                ApplyStyle(currentSelectionState);
            }
        }

        internal void Configure(PackShopStyle.ButtonKind kind, bool primary)
        {
            _kind = kind;
            _primary = primary;
            ApplyStyle(currentSelectionState);
        }

        internal void SetSelected(bool selected)
        {
            _selected = selected;
            ApplyStyle(currentSelectionState);
        }

        internal void SetMovingArtwork(RectTransform artwork)
        {
            _artMotion = artwork.gameObject.AddComponent<PackTileArtMotion>();
            ApplyStyle(currentSelectionState);
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            ApplyStyle(state);
        }

        private void ApplyStyle(SelectionState state)
        {
            if (targetGraphic is not Image image || Label == null) return;
            bool enabled = state != SelectionState.Disabled;
            bool focused = state is SelectionState.Highlighted or SelectionState.Selected or SelectionState.Pressed;
            bool darkLabel = _primary || _kind is PackShopStyle.ButtonKind.SinglePurchase or
                PackShopStyle.ButtonKind.BundlePurchase ||
                _selected && (_kind is PackShopStyle.ButtonKind.Category or PackShopStyle.ButtonKind.Filter);
            image.sprite = PackShopStyle.ButtonSprite(_kind, _primary, _selected, focused);
            image.color = !enabled ? new Color(.49f, .49f, .52f, .65f)
                : state == SelectionState.Pressed ? new Color(.82f, .82f, .82f) : Color.white;
            if (_artMotion != null) _artMotion.Raised = enabled && (_selected || focused);
            Label.color = !enabled ? PackShopStyle.TextMuted : darkLabel ? PackShopStyle.Ink :
                _kind is PackShopStyle.ButtonKind.OpeningAction or PackShopStyle.ButtonKind.OpeningSkip ? PackShopStyle.TextMuted : _normal;
        }
    }

    internal sealed class PackTileArtMotion : MonoBehaviour
    {
        internal bool Raised { get; set; }
        private RectTransform _art;
        private Vector2 _position;
        private float _amount;

        private void Awake()
        {
            _art = (RectTransform)transform;
            _position = _art.anchoredPosition;
        }

        private void Update()
        {
            float target = Raised ? 1f : 0f;
            if (_amount == target) return;
            _amount = Mathf.MoveTowards(_amount, target, Time.unscaledDeltaTime / .13f);
            float lift = Mathf.SmoothStep(0f, 1f, _amount);
            _art.anchoredPosition = _position + Vector2.up * (3.5f * lift);
            _art.localScale = Vector3.one * (1f + .038f * lift);
        }

        private void OnDisable()
        {
            if (_art == null) return;
            _amount = 0f;
            _art.anchoredPosition = _position;
            _art.localScale = Vector3.one;
        }
    }
}
