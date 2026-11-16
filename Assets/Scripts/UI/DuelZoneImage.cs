using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    // The hit area follows the same projected edges as the board art.
    internal sealed class DuelZoneImage : Image
    {
        private static readonly float[] RowEdges = { 1f, 0.846f, 0.674f, 0.476f, 0.247f, 0f };
        private Vector4 _edges = new Vector4(0f, 1f, 0f, 1f);

        internal static RectTransform CreateCell(Transform board, int column, int row, bool drawZone = true)
        {
            float bottom = RowEdges[row + 1] + 0.004f;
            float top = RowEdges[row] - 0.004f;
            float left = (column + 0.025f) / 9f;
            float right = (column + 0.975f) / 9f;
            float bottomLeft = ProjectX(left, bottom);
            float bottomRight = ProjectX(right, bottom);
            float topLeft = ProjectX(left, top);
            float topRight = ProjectX(right, top);
            float min = Mathf.Min(bottomLeft, topLeft);
            float max = Mathf.Max(bottomRight, topRight);
            float width = max - min;

            var cell = LotdUiKit.CreateRect($"Zone_{column}_{row}", board,
                new Vector2(min, bottom), new Vector2(max, top), Vector2.zero, Vector2.zero);
            if (drawZone)
            {
                cell.gameObject.AddComponent<DuelZoneImage>()._edges = new Vector4(
                    (bottomLeft - min) / width, (bottomRight - min) / width,
                    (topLeft - min) / width, (topRight - min) / width);
            }
            return cell;
        }

        private static float ProjectX(float x, float y)
        {
            return 0.5f + (x - 0.5f) * Mathf.Lerp(1f, 0.72f, y);
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            base.OnPopulateMesh(mesh);
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f) return;

            var vertex = new UIVertex();
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                float u = (vertex.position.x - rect.xMin) / rect.width;
                float v = (vertex.position.y - rect.yMin) / rect.height;
                vertex.position.x = rect.xMin + rect.width * Mathf.Lerp(
                    Mathf.Lerp(_edges.x, _edges.z, v), Mathf.Lerp(_edges.y, _edges.w, v), u);
                mesh.SetUIVertex(vertex, i);
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

    internal sealed class DuelMat : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            var nearLeft = Point(rect, 0.026f, 0.02f);
            var nearRight = Point(rect, 0.974f, 0.02f);
            var farLeft = Point(rect, 0.15f, 0.99f);
            var farRight = Point(rect, 0.85f, 0.99f);
            var nearColor = new Color(0.06f, 0.71f, 0.74f, 0.61f);
            var farColor = new Color(0.13f, 0.47f, 0.68f, 0.56f);
            AddQuad(mesh, nearLeft, farLeft, farRight, nearRight, nearColor, farColor);

            var edge = new Color(0.60f, 0.94f, 1f, 0.62f);
            AddLine(mesh, nearLeft, farLeft, 2.4f, edge);
            AddLine(mesh, farLeft, farRight, 2.4f, edge);
            AddLine(mesh, farRight, nearRight, 2.4f, edge);
            AddLine(mesh, nearRight, nearLeft, 2.4f, edge);

            var trace = new Color(0.49f, 0.91f, 1f, 0.16f);
            for (int i = 1; i < 5; i++)
            {
                float t = i / 5f;
                Vector2 a = Vector2.Lerp(nearLeft, farLeft, t);
                Vector2 b = Vector2.Lerp(nearRight, farRight, t);
                AddLine(mesh, a, b, 1.2f, trace);
                AddLine(mesh, Vector2.Lerp(nearLeft, nearRight, t), Vector2.Lerp(farLeft, farRight, t), 1.2f, trace);
            }
        }

        private static Vector2 Point(Rect rect, float x, float y)
        {
            return new Vector2(rect.xMin + rect.width * x, rect.yMin + rect.height * y);
        }

        private static void AddLine(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
        {
            var delta = (b - a).normalized;
            var normal = new Vector2(-delta.y, delta.x) * (width * 0.5f);
            AddQuad(mesh, a - normal, a + normal, b + normal, b - normal, color, color);
        }

        private static void AddQuad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color low, Color high)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(a, low, Vector2.zero);
            mesh.AddVert(b, high, Vector2.zero);
            mesh.AddVert(c, high, Vector2.zero);
            mesh.AddVert(d, low, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
