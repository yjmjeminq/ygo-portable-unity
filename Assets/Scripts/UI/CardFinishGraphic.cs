using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class CardFinishGraphic : MaskableGraphic
    {
        private static readonly float[] ReflectionStops = { -.34f, -.16f, -.065f, .01f, .075f, .20f, .34f };
        private static readonly Color[] UltraReflection =
        {
            new Color(.35f, .73f, 1f, 0f), new Color(.42f, .80f, 1f, .28f),
            new Color(.90f, .77f, 1f, .58f), new Color(1f, .99f, .88f, 1f),
            new Color(1f, .62f, .38f, .58f), new Color(.43f, 1f, .79f, .26f),
            new Color(.43f, 1f, .79f, 0f),
        };

        private uint _code;
        private float _highlight;
        internal CardRarity Rarity { get; private set; }

        internal void Bind(uint code, CardRarity rarity)
        {
            if (_code == code && Rarity == rarity) return;
            _code = code;
            Rarity = rarity;
            _highlight = 0f;
            SetVerticesDirty();
        }

        internal void SetHighlight(float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (amount == _highlight || amount is > 0f and < 1f && Mathf.Abs(amount - _highlight) < .01f) return;
            _highlight = amount;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect image = rectTransform.rect;
            if (image.width < 1f || image.height < 1f || Rarity < CardRarity.SuperRare) return;
            bool ultra = Rarity == CardRarity.UltraRare;
            float center = .36f + (_code % 13) * .014f + _highlight * .29f;
            float strength = ultra ? .21f + _highlight * .19f : .10f + _highlight * .09f;
            const float slope = .29f;

            for (int i = 1; i < ReflectionStops.Length; i++)
            {
                float left = center + ReflectionStops[i - 1];
                float right = center + ReflectionStops[i];
                Color start = ReflectionColor(i - 1, ultra, strength);
                Color end = ReflectionColor(i, ultra, strength);
                Quad(mesh, At(image, left - slope, 0f), At(image, right - slope, 0f),
                    At(image, right + slope, 1f), At(image, left + slope, 1f), start, end, end, start);
            }

            // The artwork has a lit bevel and a shaded edge inside the frame.
            float edge = ultra ? .035f : .020f;
            Color light = ultra ? new Color(1f, .91f, .73f, .53f + _highlight * .21f) : new Color(.76f, .91f, 1f, .30f);
            Color shade = new Color(.035f, .026f, .12f, ultra ? .49f : .25f);
            Quad(mesh, At(image, 0f, 1f - edge), At(image, 1f, 1f - edge), At(image, 1f, 1f), At(image, 0f, 1f),
                Color.clear, Color.clear, light, light);
            Quad(mesh, At(image, 0f, 0f), At(image, 1f, 0f), At(image, 1f, edge), At(image, 0f, edge),
                shade, shade, Color.clear, Color.clear);
            if (ultra)
            {
                Quad(mesh, At(image, 0f, 0f), At(image, edge, 0f), At(image, edge, 1f), At(image, 0f, 1f),
                    light * .44f, Color.clear, Color.clear, light * .76f);
                Quad(mesh, At(image, 1f - edge, 0f), At(image, 1f, 0f), At(image, 1f, 1f), At(image, 1f - edge, 1f),
                    Color.clear, shade, shade * .72f, Color.clear);
            }
        }

        private static Color ReflectionColor(int index, bool ultra, float strength)
        {
            Color rainbow = UltraReflection[index];
            Color tint = ultra ? rainbow : new Color(.80f, .91f, 1f, rainbow.a);
            tint.a *= strength;
            return tint;
        }

        private static Vector2 At(Rect rect, float x, float y) =>
            new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, x), Mathf.Lerp(rect.yMin, rect.yMax, y));

        private static void Quad(VertexHelper mesh, Vector2 bottomLeft, Vector2 bottomRight, Vector2 topRight, Vector2 topLeft,
            Color a, Color b, Color c, Color d)
        {
            int i = mesh.currentVertCount;
            mesh.AddVert(bottomLeft, a, Vector2.zero);
            mesh.AddVert(bottomRight, b, Vector2.zero);
            mesh.AddVert(topRight, c, Vector2.zero);
            mesh.AddVert(topLeft, d, Vector2.zero);
            mesh.AddTriangle(i, i + 1, i + 2);
            mesh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
