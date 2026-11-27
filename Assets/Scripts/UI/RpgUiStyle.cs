using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal static class RpgUiStyle
    {
        private static Font _body, _strong, _reading, _display;
        private static readonly Dictionary<string, Sprite> Skins = new();
        private static readonly Vector4 FrameBorder = new(54f, 48f, 54f, 48f);
        private static readonly Vector4 ButtonBorder = new(45f, 28f, 45f, 28f);
        private static readonly Color32 TextColor = new(239, 244, 252, 255);
        internal static readonly Color32 Cyan = new(111, 230, 244, 255);
        internal static readonly Color32 Gold = new(255, 205, 117, 255);

        internal static Color SpeakerColor(string name) => (name ?? "").Trim() switch
        {
            "윤" or "주인공" or "빙의된 윤" => new Color32(246, 132, 152, 255),
            "텐죠인 아스카" or "아스카" => new Color32(152, 192, 255, 255),
            "유우키 쥬다이" or "쥬다이" => new Color32(255, 133, 114, 255),
            "만죠메 쥰" or "만죠메" or "만죠메 썬더" => new Color32(198, 174, 247, 255),
            "마루후지 료" or "카이저" => new Color32(212, 229, 241, 255),
            "헬 카이저" or "마루후지 료 (헬 카이저)" => new Color32(237, 119, 140, 255),
            "에드 피닉스" or "에드" => new Color32(170, 167, 255, 255),
            "요한 안데르센" or "요한" => new Color32(125, 233, 191, 255),
            "유벨" => new Color32(222, 160, 243, 255),
            "사이오 타쿠마" or "사이오" => new Color32(255, 231, 167, 255),
            "이자요이 아키" => new Color32(255, 64, 129, 255),
            "후도 유세이" => new Color32(0, 229, 255, 255),
            "잭 아틀라스" => new Color32(255, 82, 82, 255),
            "크로우 호건" => new Color32(255, 160, 83, 255),
            "예거" => new Color32(224, 64, 251, 255),
            "카드 매점 직원" => new Color32(131, 232, 190, 255),
            _ => new Color32(228, 213, 178, 255),
        };
        internal enum Symbol { Dot, Arrow, Star, Hexagon, Ring, Lock, Alert, Chevron, Map, Menu }
        private static readonly Sprite[] Symbols = new Sprite[(int)Symbol.Menu + 1];

        internal static Sprite Glyph(Symbol symbol) => Symbols[(int)symbol] ??=
            Skin(symbol.ToString().ToLowerInvariant(), default, DuelUiKit.WhiteSprite);

        internal static Sprite Joystick(bool thumb) => Skin(thumb ? "stick-thumb" : "stick-base", default, DuelUiKit.WhiteSprite);
        internal static Sprite ButtonSkin(bool primary = false, bool prominent = false) =>
            Skin(prominent ? "button-gold" : primary ? "button-primary" : "button", ButtonBorder,
                primary || prominent ? DuelUiChrome.Primary : DuelUiChrome.Control);

        private static Sprite Skin(string name, Vector4 border, Sprite fallback)
        {
            if (Skins.TryGetValue(name, out var sprite)) return sprite;
            sprite = TagForceWorldImages.Read(Path.Combine(Application.streamingAssetsPath,
                "TagForce", "UI", "GxChrome", name + ".png"), readable: false, border: border, pixelsPerUnit: 200f) ?? fallback;
            Skins.Add(name, sprite);
            return sprite;
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

        internal static void Glass(RectTransform rect) => SetPanel(rect, Skin("glass", FrameBorder, DuelUiChrome.Panel));

        internal static void Panel(RectTransform rect, bool accent = false, bool gold = false)
        {
            SetPanel(rect, Skin(gold ? "gold-panel" : accent ? "accent-panel" : "panel", FrameBorder, DuelUiChrome.Panel));
        }

        internal static void Dialogue(RectTransform rect) => SetPanel(rect, Skin("dialogue", FrameBorder, DuelUiChrome.Panel));

        internal static void Speaker(RectTransform rect) => SetPanel(rect, Skin("speaker", ButtonBorder, DuelUiChrome.Panel));

        internal static void Interlude(RectTransform rect) => SetPanel(rect, Skin("interlude", FrameBorder, DuelUiChrome.Panel));

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
            image.sprite = ButtonSkin(primary, prominent);
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
                Type(label, strong: primary || prominent);
                label.color = TextColor;
            }
            if (animated) MenuButtonMotion.Attach(button, label?.rectTransform,
                prominent ? Gold : primary ? Cyan : new Color32(192, 203, 247, 255), surface: image);
        }
    }
}
