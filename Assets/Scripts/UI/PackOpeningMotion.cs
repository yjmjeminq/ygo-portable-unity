using System;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class PackOpeningMotion : MonoBehaviour
    {
        internal const float Duration = 1.72f;
        internal const float TearAt = .94f;
        internal static readonly Vector2 TearHeights = new Vector2(.79f, .93f);

        private static Sprite _bloomSprite;
        private RectTransform _stage;
        private RectTransform _wrapper;
        private Image _reflection;
        private Image _burstBackdrop;
        private Image _highlight;
        private PackWrapperSlice _body;
        private PackWrapperSlice _seal;
        private Image _glow;
        private Image _flash;
        private PackOpeningSparks _sparks;
        private Sprite _cover;
        private Action _onFinished;
        private Color _accent;
        private CardRarity _rarity;
        private float _elapsed;

        internal bool IsPlaying { get; private set; }

        internal void Initialize(Sprite face, CardRarity highestRarity, Action onFinished)
        {
            if (_stage == null) Build();
            _cover = face ?? DuelUiKit.WhiteSprite;
            _rarity = highestRarity;
            _accent = highestRarity switch
            {
                CardRarity.UltraRare => new Color32(166, 224, 255, 255),
                CardRarity.SuperRare => new Color32(255, 194, 77, 255),
                _ => new Color32(131, 201, 251, 255),
            };
            _reflection.sprite = _cover;
            _highlight.sprite = DuelUiKit.LoadSprite("PackShop/PackHighlightFlare" + (highestRarity switch
            {
                CardRarity.UltraRare => "UR",
                CardRarity.SuperRare => "SR",
                _ => "N",
            }));
            _burstBackdrop.sprite = highestRarity switch
            {
                CardRarity.UltraRare => DuelUiKit.LoadSprite("PackShop/PackOpenBGBottom_ur"),
                CardRarity.SuperRare => DuelUiKit.LoadSprite("PackShop/PackOpenBGBottom_sr"),
                _ => null,
            };
            _burstBackdrop.enabled = _burstBackdrop.sprite != null;
            _body.SetFace(_cover, false);
            _seal.SetFace(_cover, true);
            _elapsed = 0f;
            _onFinished = onFinished;
            IsPlaying = true;
            _stage.gameObject.SetActive(true);
            RenderAt(0f);
        }

        internal void Skip()
        {
            if (!IsPlaying) return;
            var finished = _onFinished;
            Stop();
            finished?.Invoke();
        }

        internal void Stop()
        {
            IsPlaying = false;
            _onFinished = null;
            _cover = null;
            if (_stage == null) return;
            _reflection.sprite = null;
            _body.SetFace(null, false);
            _seal.SetFace(null, true);
            _stage.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!IsPlaying) return;
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed >= Duration) Skip();
            else RenderAt(_elapsed);
        }

        private void OnDisable() => Stop();

        internal void RenderAt(float elapsed)
        {
            if (_cover == null) return;
            elapsed = Mathf.Clamp(elapsed, 0f, Duration);
            float enter = EaseOut(elapsed / .43f);
            float open = Mathf.Clamp01((elapsed - TearAt) / (Duration - TearAt));
            float lift = EaseOut(open);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.28f, 1f, open));
            float hover = Mathf.Sin(elapsed * Mathf.PI * 2f) * 6f;
            float tilt = Mathf.Lerp(-8f, 0f, enter) + Mathf.Sin(elapsed * 4f) * 1.2f;
            float height = Mathf.Max(1f, _stage.rect.height * .48f);
            float aspect = Mathf.Clamp(_cover.rect.width / _cover.rect.height, .34f, 1.4f);
            height = Mathf.Min(height, _stage.rect.width * .36f / aspect);
            Vector2 size = new Vector2(height * aspect, height);
            Vector2 point = Vector2.up * (_stage.rect.height * .055f + Mathf.Lerp(-48f, 0f, enter) + hover);
            float scale = Mathf.Lerp(.79f, 1f, enter) + lift * .045f;

            _wrapper.sizeDelta = size;
            _wrapper.anchoredPosition = point;
            _wrapper.localRotation = Quaternion.Euler(0f, 0f, tilt);
            _wrapper.localScale = Vector3.one * scale;
            var mirrored = _reflection.rectTransform;
            mirrored.anchoredPosition = point - Vector2.up * height * scale * .745f;
            mirrored.sizeDelta = new Vector2(size.x, size.y * .49f) * scale;
            mirrored.localRotation = Quaternion.Euler(0f, 0f, -tilt * .45f);
            _reflection.color = new Color(.62f, .60f, .68f, .40f * (1f - lift));
            SetPiece(_body, new Vector2(-45f, -72f) * lift, -14f * lift, fade);
            SetPiece(_seal, new Vector2(126f, 153f) * lift, -45f * lift, fade * (1f - .27f * open));

            float incomingLight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.22f, TearAt, elapsed));
            float burst = Mathf.Max(0f, 1f - Mathf.Abs(elapsed - (TearAt + .07f)) / .16f);
            _burstBackdrop.color = WithAlpha(Color.white, burst * .78f);
            _highlight.rectTransform.sizeDelta = new Vector2(height * 1.54f, height * .51f);
            _highlight.rectTransform.anchoredPosition = point + Vector2.up * height * .62f;
            _highlight.color = WithAlpha(Color.white, burst * .95f);
            SetGlow(_glow, point, height * (1.82f + .45f * lift),
                WithAlpha(_accent, fade * (.31f + incomingLight * .31f + burst * .17f)));
            SetGlow(_flash, point + Vector2.up * height * .31f, height * (1.30f + lift * .34f),
                WithAlpha(Color.Lerp(Color.white, _accent, .17f), burst * .67f));

            var tearLeft = Rotate(new Vector2(-size.x / 2f, (TearHeights.x - .5f) * size.y) * scale, tilt);
            var tearRight = Rotate(new Vector2(size.x / 2f, (TearHeights.y - .5f) * size.y) * scale, tilt);
            _sparks.SetFrame(point, size.y, point + tearLeft, point + tearRight, _accent, _rarity, elapsed);
        }

        private void Build()
        {
            _stage = DuelUiKit.FillRect("PackOpeningStage", transform);
            _glow = MakeGlow("Aura", _stage);
            _reflection = DuelUiKit.AddImage(Centered("PackReflection", _stage).gameObject,
                null, Color.clear, false, false);
            _reflection.rectTransform.localScale = new Vector3(1f, -1f, 1f);
            PackOpeningStage.FloorFade(_stage, .058f, .234f);
            _burstBackdrop = DuelUiKit.AddImage(DuelUiKit.FillRect("BurstBackdrop", _stage).gameObject,
                null, Color.clear, false, false);
            _wrapper = Centered("Wrapper", _stage);
            _body = DuelUiKit.FillRect("Body", _wrapper).gameObject.AddComponent<PackWrapperSlice>();
            _seal = DuelUiKit.FillRect("Seal", _wrapper).gameObject.AddComponent<PackWrapperSlice>();
            _highlight = DuelUiKit.AddImage(Centered("Highlight", _stage).gameObject,
                null, Color.clear, false, false);
            _sparks = DuelUiKit.FillRect("LightTrails", _stage).gameObject.AddComponent<PackOpeningSparks>();
            _sparks.raycastTarget = false;
            _flash = MakeGlow("Burst", _stage);
        }

        private static RectTransform Centered(string name, Transform parent) => DuelUiKit.CreateRect(name,
            parent, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);

        private static Image MakeGlow(string name, Transform parent)
        {
            if (_bloomSprite == null)
            {
                const int size = 48;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Pack Opening Bloom" };
                texture.wrapMode = TextureWrapMode.Clamp;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var point = new Vector2((x + .5f) / size * 2f - 1f, (y + .5f) / size * 2f - 1f);
                    float power = Mathf.Pow(Mathf.Clamp01(1f - point.magnitude), 2.35f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * power));
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                _bloomSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                    new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
            }
            return DuelUiKit.AddImage(Centered(name, parent).gameObject, _bloomSprite, Color.clear, false, false);
        }

        private static void SetPiece(PackWrapperSlice slice, Vector2 movement, float rotation, float opacity)
        {
            slice.rectTransform.anchoredPosition = movement;
            slice.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            slice.color = new Color(1f, 1f, 1f, opacity);
        }

        private static void SetGlow(Image glow, Vector2 point, float diameter, Color color)
        {
            glow.rectTransform.sizeDelta = new Vector2(diameter, diameter);
            glow.rectTransform.anchoredPosition = point;
            glow.color = color;
        }

        private static float EaseOut(float progress)
        {
            float remaining = 1f - Mathf.Clamp01(progress);
            return 1f - remaining * remaining * remaining;
        }

        internal static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Vector2 Rotate(Vector2 point, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(point.x * cos - point.y * sin, point.x * sin + point.y * cos);
        }
    }

    internal static class PackOpeningStage
    {
        private static Sprite _floorFade;

        internal static void Backdrop(RectTransform root) => DuelUiKit.AddImage(
            DuelUiKit.FillRect("OpeningBackdrop", root).gameObject,
            DuelUiKit.LoadSprite("PackShop/ShopBGBase03"), Color.white);

        internal static void FloorFade(Transform parent, float bottom, float top) => DuelUiKit.AddImage(
            DuelUiKit.CreateRect("ReflectionFade", parent, new Vector2(0f, bottom), new Vector2(1f, top),
                Vector2.zero, Vector2.zero).gameObject,
            _floorFade ??= CreateFloorFade(), Color.white, false, false);

        private static Sprite Bake(string name, int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 100f);
        }

        private static Sprite CreateFloorFade()
        {
            const int height = 64;
            var pixels = new Color32[height];
            for (int row = 0; row < height; row++)
            {
                float position = row / (float)(height - 1);
                var tint = new Color32(20, 12, 30, 255);
                tint.a = (byte)Mathf.RoundToInt(232 * Mathf.SmoothStep(0f, 1f, 1f - position));
                pixels[row] = tint;
            }
            return Bake("Pack Stage Reflection Fade", 1, height, pixels);
        }
    }

    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class PackWrapperSlice : MaskableGraphic
    {
        private Sprite _face;
        private bool _isSeal;

        private PackWrapperSlice() => useLegacyMeshGeneration = false;

        public override Texture mainTexture => _face != null ? _face.texture : Texture2D.whiteTexture;

        internal void SetFace(Sprite sprite, bool isSeal)
        {
            _face = sprite;
            _isSeal = isSeal;
            raycastTarget = false;
            SetAllDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            if (_face == null) return;
            var rect = rectTransform.rect;
            Vector4 uv = DataUtility.GetOuterUV(_face);
            float left = PackOpeningMotion.TearHeights.x;
            float right = PackOpeningMotion.TearHeights.y;
            Add(0f, _isSeal ? left : 0f);
            Add(0f, _isSeal ? 1f : left);
            Add(1f, _isSeal ? 1f : right);
            Add(1f, _isSeal ? right : 0f);
            vertices.AddTriangle(0, 1, 2);
            vertices.AddTriangle(2, 3, 0);

            void Add(float x, float y)
            {
                var position = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, x), Mathf.Lerp(rect.yMin, rect.yMax, y));
                var texturePoint = new Vector2(Mathf.Lerp(uv.x, uv.z, x), Mathf.Lerp(uv.y, uv.w, y));
                vertices.AddVert(position, color, texturePoint);
            }
        }
    }

    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class PackOpeningSparks : MaskableGraphic
    {
        private Vector2 _center;
        private Vector2 _left;
        private Vector2 _right;
        private float _elapsed;
        private float _height;
        private Color _accent;
        private CardRarity _rarity;

        private PackOpeningSparks() => useLegacyMeshGeneration = false;

        internal void SetFrame(Vector2 center, float height, Vector2 left, Vector2 right, Color accent, CardRarity rarity, float elapsed)
        {
            _center = center;
            _height = height;
            _left = left;
            _right = right;
            _accent = accent;
            _rarity = rarity;
            _elapsed = elapsed;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            float opening = Mathf.InverseLerp(PackOpeningMotion.TearAt, PackOpeningMotion.Duration, _elapsed);
            DrawOrbit(vertices, opening);
            DrawTear(vertices, opening);
            DrawSparks(vertices, opening);
        }

        private void DrawOrbit(VertexHelper vertices, float opening)
        {
            const int segments = 48;
            float opacity = (.23f + .32f * Mathf.Clamp01(_elapsed / PackOpeningMotion.TearAt)) * (1f - opening);
            float growth = 1f + .36f * opening;
            var radius = new Vector2(.88f, .55f) * (_height * growth);
            float rotation = _elapsed * .40f;
            for (int i = 0; i < segments; i++)
            {
                if (i % 12 >= 8) continue;
                float angle = rotation + (i / (float)segments) * 2f * Mathf.PI;
                float next = angle + 2f * Mathf.PI / segments;
                var first = _center + Vector2.Scale(Direction(angle), radius);
                var last = _center + Vector2.Scale(Direction(next), radius);
                Line(vertices, first, last, i % 12 == 0 ? 3.2f : 1.5f, PackOpeningMotion.WithAlpha(_accent, opacity));
            }
        }

        private void DrawTear(VertexHelper vertices, float opening)
        {
            float sweep = Mathf.InverseLerp(PackOpeningMotion.TearAt - .22f, PackOpeningMotion.TearAt, _elapsed);
            if (sweep <= 0f) return;
            float opacity = Mathf.SmoothStep(0f, 1f, sweep) * (1f - Mathf.Clamp01(opening * 3.4f));
            Vector2 edge = Vector2.Lerp(_left, _right, sweep);
            Line(vertices, _left, edge, 12f, PackOpeningMotion.WithAlpha(_accent, .32f * opacity));
            Line(vertices, _left, edge, 5f, PackOpeningMotion.WithAlpha(_accent, .77f * opacity));
            Line(vertices, _left, edge, 2f, PackOpeningMotion.WithAlpha(Color.white, opacity));
            Line(vertices, edge + new Vector2(0f, -11f), edge + new Vector2(0f, 11f),
                1.3f, PackOpeningMotion.WithAlpha(Color.white, opacity));
        }

        private void DrawSparks(VertexHelper vertices, float opening)
        {
            if (opening <= 0f) return;
            float fade = Mathf.Clamp01((1f - opening) * 1.18f);
            int count = _rarity == CardRarity.UltraRare ? 40 : _rarity == CardRarity.SuperRare ? 33 : 25;
            for (int i = 0; i < count; i++)
            {
                float seed = Mathf.Repeat(i * .618034f, 1f);
                Vector2 direction = Direction(i * 2.399963f);
                Vector2 origin = Vector2.Lerp(_left, _right, seed);
                float reach = _height * (.20f + .79f * Mathf.Repeat(i * .37812f, 1f));
                var head = origin + direction * (reach * opening) + Vector2.down * (_height * .13f * opening * opening);
                float length = (.011f + .085f * seed) * (_height * fade);
                var light = _rarity == CardRarity.UltraRare ? Color.HSVToRGB(seed, .83f, 1f) : _accent;
                var tint = Color.Lerp(light, Color.white, i % 4 == 0 ? .89f : .20f);
                Line(vertices, head - direction * length, head, i % 4 == 0 ? 2.6f : 1.6f,
                    PackOpeningMotion.WithAlpha(tint, fade));
            }
        }

        private static Vector2 Direction(float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        private static void Line(VertexHelper mesh, Vector2 start, Vector2 end, float thickness, Color color)
        {
            Vector2 normal = Vector2.Perpendicular(end - start).normalized * (thickness / 2f);
            int index = mesh.currentVertCount;
            mesh.AddVert(start - normal, color, Vector2.zero);
            mesh.AddVert(start + normal, color, Vector2.zero);
            mesh.AddVert(end + normal, color, Vector2.zero);
            mesh.AddVert(end - normal, color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index + 2, index + 3, index);
        }
    }
}
