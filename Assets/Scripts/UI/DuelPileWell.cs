using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed class DuelPileWell : RawImage
    {
        private const int Segments = 40;
        private static readonly float[] Radii = { 0f, .56f, .68f, .73f, .77f, .82f, .90f, 1f };
        private static readonly Vector2[] Directions = MakeDirections();
        private bool _occupied;
        private float _light;

        internal Color Accent { get; set; }

        internal void SetOccupied(bool occupied)
        {
            if (_occupied == occupied) return;
            _occupied = occupied;
            if (!Application.isPlaying)
            {
                _light = occupied ? 1f : 0f;
                SetVerticesDirty();
            }
        }

        private void Update()
        {
            float target = _occupied ? 1f : 0f;
            if (_light == target || CardAnimationManager.Instance?.Paused == true) return;
            _light = Mathf.MoveTowards(_light, target, Time.unscaledDeltaTime * DuelFieldUI.PlaybackRate * 5f);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float radius = Mathf.Min(rect.width, rect.height) * .5f;
            if (radius <= 0f) return;

            var inset = new Vector2(radius, radius * .88f);
            Color stone = new Color(.048f, .077f, .085f);
            Color interior = Color.Lerp(stone, Accent, _light * .23f);
            Color deep = Color.Lerp(new Color(.021f, .038f, .047f), Accent, _light * .11f);
            Color groove = Color.Lerp(new Color(.16f, .19f, .18f), Accent, _light * .38f);
            Color bronze = new Color(.48f, .43f, .33f);
            Color shadow = new Color(.016f, .028f, .035f, .62f);

            for (int spoke = 0; spoke < Segments; spoke++)
            {
                var direction = Directions[spoke];
                Vector2 extent = Vector2.Scale(direction, inset);
                Color rim = Color.Lerp(stone, bronze, .48f + .39f * direction.y);
                for (int ring = 0; ring < Radii.Length; ring++)
                {
                    Color tone = ring switch
                    {
                        0 => interior,
                        1 => deep,
                        2 => groove,
                        3 => stone,
                        4 => rim,
                        5 => shadow,
                        6 => shadow,
                        _ => Color.clear,
                    };
                    mesh.AddVert(rect.center + extent * Radii[ring], tone, Vector2.zero);
                }
            }

            int width = Radii.Length;
            for (int spoke = 0; spoke < Segments; spoke++)
            {
                int next = (spoke + 1) % Segments;
                for (int ring = 0; ring < width - 1; ring++)
                {
                    int a = spoke * width + ring;
                    int b = next * width + ring;
                    mesh.AddTriangle(a, b + 1, a + 1);
                    if (ring != 0) mesh.AddTriangle(a, b, b + 1);
                }
            }
        }

        private static Vector2[] MakeDirections()
        {
            var ring = new Vector2[Segments];
            for (int i = 0; i < ring.Length; i++)
            {
                float angle = 2f * Mathf.PI * i / ring.Length;
                ring[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            return ring;
        }
    }

    internal sealed class DuelPileDepth : RawImage
    {
        private int _count;

        internal bool Opponent { get; set; }

        internal void SetCount(int count)
        {
            if (_count == count) return;
            _count = count;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (_count < 2 || rect.width <= 0f || rect.height <= 0f) return;

            float side = Opponent ? -1f : 1f;
            float depth = Mathf.Lerp(.031f, .118f, Mathf.Sqrt(Mathf.Clamp01((_count - 2) / 58f))) * rect.height;
            var drop = new Vector2(.48f, -1f) * (depth * side);
            var left = Opponent ? rect.max : rect.min;
            var right = left + new Vector2(rect.width * side, 0f);
            var far = right + new Vector2(0f, rect.height * side);
            var bottom = new Color(.12f, .11f, .09f);
            var edge = new Color(.33f, .30f, .24f);

            DuelMat.AddQuad(mesh, left + drop, left, right, right + drop, bottom, edge);
            DuelMat.AddQuad(mesh, right + drop, right, far, far + drop, bottom, Color.Lerp(bottom, edge, .64f));

            int seams = Mathf.Min(_count - 1, 4);
            var paper = new Color(.60f, .56f, .45f, .55f);
            for (int i = 1; i <= seams; i++)
            {
                Vector2 level = drop * (i / (seams + 1f));
                Vector2 thickness = drop * Mathf.Min(.12f, .75f / depth);
                DuelMat.AddQuad(mesh, left + level + thickness, left + level,
                    right + level, right + level + thickness, paper, paper);
                DuelMat.AddQuad(mesh, right + level + thickness, right + level,
                    far + level, far + level + thickness, paper * .72f, paper * .72f);
            }
        }
    }
}
