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
        internal static readonly Color IncreasedValue = new Color(.58f, 1f, .82f);
        internal static readonly Color DecreasedValue = new Color(1f, .59f, .56f);
        internal static readonly Color GlassColor = new Color(.034f, .055f, .086f, .96f);
        internal static readonly Color GlassEdge = new Color(.34f, .59f, .69f, .72f);
        internal static readonly Color ChainCyan = new Color32(0, 245, 255, 255);
        internal static readonly Color ChainRed = new Color32(255, 59, 48, 255);
        internal static readonly Color ChainGold = new Color32(255, 215, 0, 255);

        private static Texture2D _stone;
        private static Sprite _disc;
        private static Sprite _handGlow;
        private static Sprite _cardShadow;
        private static Sprite _hexagon;
        private static Sprite _overframeBackdrop;
        internal static Texture2D Stone => _stone ??= Resources.Load<Texture2D>("Duel/StonePaving");
        internal static Sprite OverframeBackdrop => _overframeBackdrop ??= BuildOverframeBackdrop();
        internal static Color Side(bool opponent) => opponent ? Opponent : Player;

        private static Sprite BuildOverframeBackdrop()
        {
            const int size = 32;
            var pixels = new Color32[size * size];
            var shadow = new Color32(13, 20, 36, 255);
            var light = new Color32(59, 91, 124, 255);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var position = new Vector2((x + .5f) / size, (y + .5f) / size);
                float falloff = Mathf.Clamp01(Vector2.Distance(position, new Vector2(.5f, .66f)) * 1.6f);
                pixels[y * size + x] = Color32.Lerp(light, shadow, Mathf.SmoothStep(0f, 1f, falloff));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Overframe backdrop",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f, 100f, 0, SpriteMeshType.FullRect);
        }

        internal static Image AddHexPlate(RectTransform root, Color rim, Color face)
        {
            _hexagon ??= BuildHexagon();
            var edge = LotdUiKit.AddImage(root.gameObject, _hexagon, rim, false, false);
            LotdUiKit.AddImage(LotdUiKit.FillRect("Surface", root, 2f).gameObject, _hexagon, face, false, false);
            return edge;
        }

        internal static void AddChainMedallion(RectTransform root, Color neon, float size)
        {
            // Separate the atmospheric halo, the lit edge, and the metal inset.
            _hexagon ??= BuildHexagon();
            var halo = LotdUiKit.FillRect("Halo", root, -size * .075f);
            LotdUiKit.AddImage(halo.gameObject, _hexagon, new Color(neon.r, neon.g, neon.b, .18f), false, false);
            var outer = LotdUiKit.FillRect("NeonEdge", root);
            AddHexPlate(outer, neon, new Color(.047f, .082f, .105f, .99f));
            var inset = LotdUiKit.FillRect("MetalInset", root, size * .074f);
            AddHexPlate(inset, Color.Lerp(new Color(.15f, .23f, .27f), neon, .28f),
                new Color(.021f, .037f, .054f, .98f));
        }

        private static Sprite BuildHexagon()
        {
            const int width = 120, height = 72;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float corner = Mathf.Abs(y + .5f - height * .5f) * .47f;
                float edge = Mathf.Min(x + .5f, width - x - .5f) - corner;
                byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(edge + .5f));
                pixels[y * width + x] = new Color32(255, 255, 255, alpha);
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Duel hexagonal plate",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.one * .5f, 100f, 0, SpriteMeshType.FullRect);
        }

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
            if (button.GetComponentInChildren<Text>(true) is { } label)
            {
                label.fontStyle = FontStyle.Normal;
                if (label.TryGetComponent<Shadow>(out var shadow)) shadow.enabled = false;
            }
            var image = button.image;
            image.sprite = LotdUiKit.WhiteSprite;
            image.overrideSprite = null;
            image.type = Image.Type.Simple;
            var normal = accent ? new Color(.30f, .22f, .10f) : new Color(.085f, .16f, .22f, .95f);
            var palette = button.colors;
            palette.normalColor = normal;
            palette.highlightedColor = accent ? new Color(.43f, .32f, .14f) : new Color(.15f, .29f, .38f);
            palette.pressedColor = new Color(0.15f, 0.36f, 0.40f);
            palette.selectedColor = palette.normalColor;
            palette.disabledColor = new Color(0.08f, 0.13f, 0.16f, 0.66f);
            palette.fadeDuration = 0.09f;
            button.colors = palette;
            button.transition = Selectable.Transition.ColorTint;
            var stroke = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
            stroke.effectColor = accent ? new Color(.91f, .69f, .32f, .76f) : GlassEdge;
            stroke.effectDistance = new Vector2(1f, -1f);
        }

        internal static Button RoundButton(RectTransform parent, string name, Color accent, Action action)
        {
            var rect = LotdUiKit.FillRect(name, parent);
            var circle = rect.gameObject.AddComponent<AspectRatioFitter>();
            circle.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            circle.aspectRatio = 1f;
            var rim = LotdUiKit.AddImage(rect.gameObject, Disc, accent, true);
            var face = LotdUiKit.FillRect("Face", rect, 2.5f);
            LotdUiKit.AddImage(face.gameObject, Disc, new Color(.074f, .145f, .214f), true, false);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rim;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(.81f, .96f, 1f),
                selectedColor = new Color(.81f, .96f, 1f),
                pressedColor = new Color(.43f, .68f, .83f),
                disabledColor = Color.white,
                colorMultiplier = 1f,
                fadeDuration = .08f,
            };
            button.onClick.AddListener(() =>
            {
                LotdAudio.PlayClick();
                action?.Invoke();
            });
            return button;
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

        internal static Sprite HandGlow
        {
            get
            {
                if (DuelVisualAssets.HandGlow != null) return DuelVisualAssets.HandGlow;
                if (_handGlow != null) return _handGlow;
                const int size = 48;
                const float radius = 4f;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var p = new Vector2(Mathf.Abs(x + .5f - size * .5f), Mathf.Abs(y + .5f - size * .5f));
                        var q = p - Vector2.one * (size * .5f - 9f - radius);
                        float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
                        float distance = Mathf.Abs(outside + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius);
                        float core = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, 2.2f, distance));
                        float halo = Mathf.Clamp01(1f - distance / 8.5f);
                        var amber = Color.Lerp(new Color(1f, .64f, .10f), new Color(1f, .97f, .62f), core);
                        amber.a = Mathf.Max(core, halo * halo * .81f);
                        pixels[y * size + x] = amber;
                    }
                }

                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "Playable hand card glow",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                _handGlow = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f),
                    100f, 0, SpriteMeshType.FullRect, new Vector4(18f, 18f, 18f, 18f));
                return _handGlow;
            }
        }

        internal static Image AddActivationGlow(Transform parent, float spread = 9f, bool behind = true)
        {
            var outline = LotdUiKit.FillRect("PlayableGlow", parent, -spread);
            if (behind) outline.SetAsFirstSibling();
            var glow = LotdUiKit.AddImage(outline.gameObject, HandGlow, Color.white, false, false);
            glow.type = Image.Type.Sliced;
            glow.fillCenter = false;
            outline.gameObject.SetActive(false);
            return glow;
        }

        internal static float ActivationPulse => .77f + .21f * Mathf.Sin(Time.unscaledTime * 3.2f);

        internal static void SetActivationGlow(Image glow, bool active)
        {
            if (glow.gameObject.activeSelf != active) glow.gameObject.SetActive(active);
            if (active) glow.canvasRenderer.SetAlpha(ActivationPulse);
        }

        internal static Sprite CardShadow
        {
            get
            {
                if (_cardShadow != null) return _cardShadow;
                const int size = 24, feather = 7;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    float vertical = Mathf.SmoothStep(0f, 1f, Mathf.Min(y, size - 1 - y) / (float)feather);
                    for (int x = 0; x < size; x++)
                    {
                        float horizontal = Mathf.SmoothStep(0f, 1f, Mathf.Min(x, size - 1 - x) / (float)feather);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(vertical * horizontal * 255f));
                    }
                }
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "Duel card shadow",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                _cardShadow = Sprite.Create(texture, new Rect(0f, 0f, size, size), Vector2.one * .5f,
                    100f, 0, SpriteMeshType.FullRect, Vector4.one * feather);
                return _cardShadow;
            }
        }

        internal static void Shadow(Text label, float distance = 1.25f)
        {
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.01f, 0.02f, 0.025f, 0.9f);
            shadow.effectDistance = new Vector2(distance, -distance);
        }
    }

    internal sealed class DuelActivationPulse : MonoBehaviour
    {
        internal Image Glow;
        private void Update()
        {
            if (Glow != null) Glow.canvasRenderer.SetAlpha(DuelStyle.ActivationPulse);
        }
    }
}
