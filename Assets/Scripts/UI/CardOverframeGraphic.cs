using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class CardOverframeGraphic : MaskableGraphic
    {
        private bool _reveal;
        private CanvasRenderer _foil;

        internal void Configure(bool reveal, CanvasRenderer foil)
        {
            _reveal = reveal;
            _foil = foil;
            raycastTarget = false;
        }

        private void LateUpdate()
        {
            if (_foil != null)
                _foil.SetAlpha(.79f + .21f * (.5f + .5f * Mathf.Sin(Time.unscaledTime * 2.3f)));
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect panel = rectTransform.rect;
            if (panel.width < 1f || panel.height < 1f) return;

            var shadow = new Color(.018f, .028f, .065f, .94f);
            var translucent = new Color(.038f, .058f, .115f, .77f);
            var clear = new Color(shadow.r, shadow.g, shadow.b, 0f);
            if (!_reveal)
            {
                Shade(0f, .20f, shadow, translucent);
                Shade(.20f, .365f, translucent, clear);
                Shade(.775f, .927f, clear, translucent);
                Shade(.927f, 1f, translucent, shadow);
            }

            var lavender = new Color(.79f, .68f, 1f, .88f);
            var teal = new Color(.27f, .86f, .90f, .79f);
            var edge = new Color(.40f, .59f, .86f, .36f);
            float stroke = panel.width * (_reveal ? .013f : .007f);
            Line(.035f, .858f, .035f, .946f, lavender);
            Line(.035f, .946f, .067f, .970f, lavender);
            Line(.067f, .970f, .316f, .970f, lavender);
            Line(.338f, .970f, .930f, .970f, edge);
            Line(.962f, .946f, .962f, .143f, edge);
            Line(.962f, .143f, .962f, .054f, teal);
            Line(.962f, .054f, .930f, .030f, teal);
            Line(.930f, .030f, .679f, .030f, teal);
            Line(.653f, .030f, .067f, .030f, edge);
            Line(.035f, .054f, .035f, .835f, edge);

            void Shade(float from, float to, Color bottom, Color top) =>
                Quad(At(0f, from), At(1f, from), At(1f, to), At(0f, to), bottom, top);

            void Line(float x1, float y1, float x2, float y2, Color tint)
            {
                Vector2 from = At(x1, y1);
                Vector2 to = At(x2, y2);
                Vector2 direction = (to - from).normalized;
                Vector2 normal = new Vector2(-direction.y, direction.x) * stroke * .5f;
                Quad(from - normal, to - normal, to + normal, from + normal, tint, tint);
            }

            Vector2 At(float x, float y) => panel.min + Vector2.Scale(panel.size, new Vector2(x, y));
            void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color bottom, Color top)
            {
                int start = mesh.currentVertCount;
                mesh.AddVert(a, bottom, Vector2.zero);
                mesh.AddVert(b, bottom, Vector2.zero);
                mesh.AddVert(c, top, Vector2.zero);
                mesh.AddVert(d, top, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2);
                mesh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}
