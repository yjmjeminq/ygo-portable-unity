using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    // Positions and hit areas share the perspective of the engraved playing surface.
    internal sealed class DuelZoneImage : Image
    {
        private static readonly float[] RowEdges = { 1f, .80f, .60f, .40f, .20f, 0f };
        private static readonly Vector2[] Corners =
        {
            new Vector2(.20f, .08f), new Vector2(.80f, .08f), new Vector2(.92f, .22f), new Vector2(.92f, .78f),
            new Vector2(.80f, .92f), new Vector2(.20f, .92f), new Vector2(.08f, .78f), new Vector2(.08f, .22f),
        };

        private Vector4 _edges = new Vector4(0f, 1f, 0f, 1f);
        private bool _placementHighlighted;

        internal void SetPlacementHighlight(bool highlighted)
        {
            if (_placementHighlighted == highlighted) return;
            _placementHighlighted = highlighted;
            SetVerticesDirty();
        }

        internal static RectTransform CreateCell(Transform board, int column, int row)
        {
            float bottom = RowEdges[row + 1] + 0.002f;
            float top = RowEdges[row] - 0.002f;
            float left = (column + 0.016f) / 9f;
            float right = (column + 0.984f) / 9f;
            float bottomLeft = ProjectX(left, bottom);
            float bottomRight = ProjectX(right, bottom);
            float topLeft = ProjectX(left, top);
            float topRight = ProjectX(right, top);
            float min = Mathf.Min(bottomLeft, topLeft);
            float max = Mathf.Max(bottomRight, topRight);
            float width = max - min;

            var cell = LotdUiKit.CreateRect($"Zone_{column}_{row}", board,
                new Vector2(min, bottom), new Vector2(max, top), Vector2.zero, Vector2.zero);
            cell.gameObject.AddComponent<DuelZoneImage>()._edges = new Vector4(
                (bottomLeft - min) / width, (bottomRight - min) / width,
                (topLeft - min) / width, (topRight - min) / width);
            return cell;
        }

        internal static float ProjectX(float x, float y)
        {
            return 0.5f + (x - 0.5f) * Mathf.Lerp(1f, 0.81f, y);
        }

        private Vector2 Corner(Rect rect, int index, float size)
        {
            Vector2 uv = Vector2.Lerp(new Vector2(.5f, .5f), Corners[index % Corners.Length], size);
            float x = Mathf.Lerp(Mathf.Lerp(_edges.x, _edges.z, uv.y), Mathf.Lerp(_edges.y, _edges.w, uv.y), uv.x);
            return new Vector2(rect.xMin + rect.width * x, rect.yMin + rect.height * uv.y);
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f) return;

            Color tone = color;
            bool paintedZone = DuelVisualAssets.Field != null &&
                tone.r < .4f && tone.g < .4f && tone.b < .4f;
            if (paintedZone) tone.a *= .45f;
            int center = mesh.currentVertCount;
            mesh.AddVert(rect.center, tone, Vector2.zero);
            for (int i = 0; i < Corners.Length; i++)
                mesh.AddVert(Corner(rect, i, .92f), tone, Vector2.zero);
            for (int i = 0; i < Corners.Length; i++)
                mesh.AddTriangle(center, center + 1 + i, center + 1 + (i + 1) % Corners.Length);

            if (_placementHighlighted)
            {
                var edgeColor = new Color(.99f, .90f, .32f, .96f);
                for (int i = 0; i < Corners.Length; i++)
                    DuelMat.AddQuad(mesh, Corner(rect, i, .99f), Corner(rect, i, .90f),
                        Corner(rect, i + 1, .90f), Corner(rect, i + 1, .99f),
                        edgeColor, edgeColor, Vector2.zero, Vector2.zero);
                return;
            }

            if (paintedZone) return;
            var shadow = new Color(.07f, .08f, .065f, Mathf.Max(.30f, tone.a * .75f));
            var light = new Color(.83f, .82f, .65f, Mathf.Max(.27f, tone.a * .66f));
            for (int i = 0; i < Corners.Length; i++)
            {
                Vector2 a = Corner(rect, i, 1f);
                Vector2 b = Corner(rect, i + 1, 1f);
                var rim = i < 4 ? light : shadow;
                DuelMat.AddQuad(mesh, a, Corner(rect, i, .93f), Corner(rect, i + 1, .93f), b,
                    rim, rim, Vector2.zero, Vector2.zero);
            }
        }

        public override bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out var point))
                return false;
            var rect = rectTransform.rect;
            if (!rect.Contains(point)) return false;
            float u = Mathf.InverseLerp(rect.xMin, rect.xMax, point.x);
            float v = Mathf.InverseLerp(rect.yMin, rect.yMax, point.y);
            return u >= Mathf.Lerp(_edges.x, _edges.z, v) && u <= Mathf.Lerp(_edges.y, _edges.w, v);
        }
    }

    internal sealed class DuelArenaGround : RawImage
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            const int columns = 14, rows = 10;
            var rect = GetPixelAdjustedRect();
            for (int y = 0; y <= rows; y++)
            {
                for (int x = 0; x <= columns; x++)
                {
                    float u = (float)x / columns, v = (float)y / rows;
                    float patch = Mathf.PerlinNoise(u * 5.2f + 3f, v * 4.6f + 7f);
                    float edge = Mathf.SmoothStep(.2f, 1f, Mathf.Abs(u - .53f) * 1.85f + Mathf.Abs(v - .51f) * .68f);
                    var vegetation = Color.Lerp(new Color(.25f, .30f, .19f), new Color(.66f, .72f, .48f), patch);
                    vegetation *= Mathf.Lerp(1f, .55f, edge);
                    vegetation.a = 1f;
                    mesh.AddVert(DuelMat.Point(rect, u, v), vegetation, new Vector2(u * 1.9f, v * 1.37f));
                }
            }
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int corner = y * (columns + 1) + x;
                    mesh.AddTriangle(corner, corner + columns + 1, corner + columns + 2);
                    mesh.AddTriangle(corner, corner + columns + 2, corner + 1);
                }
            }
        }
    }

    internal sealed class DuelMat : RawImage
    {
        private Color _side = DuelStyle.Player;

        internal void ShowTurn(bool opponent)
        {
            var side = DuelStyle.Side(opponent);
            if (_side == side) return;
            _side = side;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            Vector2 nearLeft = Point(rect, 0f, 0f);
            Vector2 nearRight = Point(rect, 1f, 0f);
            Vector2 farLeft = Point(rect, DuelZoneImage.ProjectX(0f, 1f), 1f);
            Vector2 farRight = Point(rect, DuelZoneImage.ProjectX(1f, 1f), 1f);
            Vector2 drop = Vector2.down * Mathf.Clamp(rect.height * .047f, 10f, 33f);
            var shadow = new Color(0.10f, .11f, .08f, .86f);

            AddQuad(mesh, nearLeft + drop * 1.15f + Vector2.left * 14f, farLeft + Vector2.left * 14f,
                farRight + Vector2.right * 14f, nearRight + drop * 1.15f + Vector2.right * 14f,
                shadow, shadow, new Vector2(.07f, .16f), new Vector2(.34f, .54f));
            var stoneSide = new Color(.43f, .43f, .34f);
            AddQuad(mesh, nearLeft + drop, nearLeft, nearRight, nearRight + drop,
                stoneSide * .5f, stoneSide, new Vector2(.04f, .21f), new Vector2(1.14f, .29f));

            var close = new Color(.85f, .87f, .76f);
            var far = new Color(.67f, .71f, .61f);
            if (DuelVisualAssets.Field != null)
            {
                close = new Color(.95f, .96f, .91f);
                far = new Color(.78f, .82f, .74f);
            }
            const int rows = 12;
            for (int i = 0; i < rows; i++)
            {
                float low = (float)i / rows, high = (float)(i + 1) / rows;
                AddQuad(mesh, Vector2.Lerp(nearLeft, farLeft, low), Vector2.Lerp(nearLeft, farLeft, high),
                    Vector2.Lerp(nearRight, farRight, high), Vector2.Lerp(nearRight, farRight, low),
                    Color.Lerp(close, far, low), Color.Lerp(close, far, high), new Vector2(0f, low), new Vector2(1f, high));
            }

            var cutEdge = new Color(.34f, .38f, .25f);
            var upperEdge = new Color(.60f, .62f, .47f);
            DrawEdge(mesh, nearLeft, farLeft, upperEdge, cutEdge);
            DrawEdge(mesh, farLeft, farRight, upperEdge, cutEdge);
            DrawEdge(mesh, farRight, nearRight, upperEdge, cutEdge);
            DrawEdge(mesh, nearRight, nearLeft, upperEdge, cutEdge);

            var middleLeft = Vector2.Lerp(nearLeft, farLeft, .49f);
            var middleRight = Vector2.Lerp(nearRight, farRight, .49f);
            Line(mesh, middleLeft, middleRight, 1.5f, new Color(.43f, .46f, .32f, .62f));
            // Only the active side is illuminated.
            bool opposite = _side == DuelStyle.Opponent;
            var lineColor = new Color(_side.r, _side.g, _side.b, .77f);
            float inset = opposite ? .957f : .032f;
            Vector2 a = Vector2.Lerp(nearLeft, farLeft, inset), b = Vector2.Lerp(nearRight, farRight, inset);
            Line(mesh, Vector2.Lerp(a, b, .06f), Vector2.Lerp(a, b, .94f), 3f, lineColor);
        }

        private static void DrawEdge(VertexHelper mesh, Vector2 a, Vector2 b, Color light, Color dark)
        {
            Line(mesh, a, b, 9f, dark);
            Line(mesh, a, b, 2f, light);
        }

        private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
        {
            var delta = (b - a).normalized;
            var normal = new Vector2(-delta.y, delta.x) * (width * .5f);
            AddQuad(mesh, a - normal, a + normal, b + normal, b - normal, color, color,
                new Vector2(.355f, .365f), new Vector2(.355f, .365f));
        }

        internal static Vector2 Point(Rect rect, float x, float y)
        {
            return new Vector2(rect.xMin + rect.width * x, rect.yMin + rect.height * y);
        }

        internal static void AddQuad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            Color low, Color high, Vector2 minUv, Vector2 maxUv)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(a, low, minUv);
            mesh.AddVert(b, high, new Vector2(minUv.x, maxUv.y));
            mesh.AddVert(c, high, maxUv);
            mesh.AddVert(d, low, new Vector2(maxUv.x, minUv.y));
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
