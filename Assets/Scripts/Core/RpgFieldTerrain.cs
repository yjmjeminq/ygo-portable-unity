using System;
using System.Collections.Generic;
using UnityEngine;

namespace YGOPortable.Core
{
    internal sealed class RpgFieldTerrain
    {
        private const float Epsilon = .00001f;
        private static readonly Vector2 ArtScale = new(1f, 9f / 16f);
        private readonly Vector2[][] _rings;
        private readonly Vector2[] _vertices;
        private readonly List<float> _crossings;
        private readonly float[,] _links;
        private readonly float[] _cost;
        private readonly int[] _previous;
        private readonly bool[] _visited;
        private readonly Vector2 _min, _max;
        internal readonly TagForceSpot Spot;

        internal RpgFieldTerrain(TagForceSpot spot)
        {
            Spot = spot;
            var floor = spot.walkPolygon;
            var obstacles = spot.obstacles ?? Array.Empty<TagForceGroundObstacle>();
            _rings = new Vector2[1 + obstacles.Length][];
            _rings[0] = floor;
            int count = floor.Length;
            for (int i = 0; i < obstacles.Length; i++)
            {
                _rings[i + 1] = obstacles[i].vertices;
                count += _rings[i + 1].Length;
            }
            var connections = new List<Vector2>(count * 2);
            foreach (var ring in _rings) connections.AddRange(ring);
            for (int outer = 0; outer < _rings.Length; outer++)
            for (int inner = outer + 1; inner < _rings.Length; inner++)
            {
                var a = _rings[outer];
                var b = _rings[inner];
                for (int i = 0; i < a.Length; i++)
                for (int j = 0; j < b.Length; j++)
                {
                    var ab = a[(i + 1) % a.Length] - a[i];
                    var cd = b[(j + 1) % b.Length] - b[j];
                    float cross = Cross(ab, cd);
                    if (Mathf.Abs(cross) <= Epsilon * Epsilon) continue;
                    var offset = b[j] - a[i];
                    float position = Cross(offset, cd) / cross;
                    float other = Cross(offset, ab) / cross;
                    if (position is > 0f and < 1f && other is > 0f and < 1f) connections.Add(a[i] + position * ab);
                }
            }
            _vertices = connections.ToArray();
            _crossings = new List<float>(2 + 2 * count);
            count = _vertices.Length;
            _links = new float[count, count];
            _cost = new float[count];
            _previous = new int[count];
            _visited = new bool[count];
            _min = _max = floor[0];
            foreach (var vertex in floor)
            {
                _min = Vector2.Min(_min, vertex);
                _max = Vector2.Max(_max, vertex);
            }
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    _links[i, j] = _links[j, i] = IsClear(_vertices[i], _vertices[j])
                        ? Distance(_vertices[i], _vertices[j]) : float.PositiveInfinity;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static float Distance(Vector2 a, Vector2 b) => Vector2.Scale(a - b, ArtScale).magnitude;
        internal float ScaleAt(float y) => Mathf.Lerp(Spot.nearScale, Spot.farScale, Mathf.InverseLerp(_min.y, _max.y, y));
        internal Vector2 Normalize(Vector2 point) =>
            new(Mathf.InverseLerp(_min.x, _max.x, point.x), Mathf.InverseLerp(_min.y, _max.y, point.y));

        private static bool Inside(Vector2[] ring, Vector2 point, bool boundaryIsInside)
        {
            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var a = ring[j];
                var b = ring[i];
                var edge = b - a;
                var offset = point - a;
                if (Mathf.Abs(Cross(edge, offset)) <= Epsilon * edge.magnitude &&
                    Vector2.Dot(offset, point - b) <= Epsilon * Epsilon) return boundaryIsInside;
                if ((a.y > point.y) != (b.y > point.y) && point.x < a.x + (b.x - a.x) * (point.y - a.y) / (b.y - a.y))
                    inside = !inside;
            }
            return inside;
        }

        internal bool Contains(Vector2 point)
        {
            if (!Inside(_rings[0], point, true)) return false;
            for (int i = 1; i < _rings.Length; i++)
                if (Inside(_rings[i], point, false)) return false;
            return true;
        }

        internal Vector2 Ground(Vector2 point) => Contains(point) ? point : ClosestBoundary(point, out _);

        private Vector2 ClosestBoundary(Vector2 point, out Vector2 tangent, Vector2 motion = default)
        {
            tangent = Vector2.zero;
            Vector2 best = point;
            float distance = float.PositiveInfinity, alignment = -1f;
            foreach (var ring in _rings)
                for (int i = 0; i < ring.Length; i++)
                {
                    var start = ring[i];
                    var edge = ring[(i + 1) % ring.Length] - start;
                    var scaledEdge = Vector2.Scale(edge, ArtScale);
                    float along = Mathf.Clamp01(Vector2.Dot(Vector2.Scale(point - start, ArtScale), scaledEdge) /
                        scaledEdge.sqrMagnitude);
                    var candidate = start + edge * along;
                    if (!Contains(candidate)) continue;
                    float gap = Vector2.Scale(candidate - point, ArtScale).sqrMagnitude;
                    float heading = Mathf.Abs(Vector2.Dot(motion, edge.normalized));
                    if (gap > distance + Epsilon * Epsilon ||
                        Mathf.Abs(gap - distance) <= Epsilon * Epsilon && heading <= alignment) continue;
                    (best, tangent, distance, alignment) = (candidate, edge, gap, heading);
                }
            // Where an obstacle reaches the outer edge, the nearest usable point can be their intersection.
            if (distance > Epsilon * Epsilon)
                foreach (var corner in _vertices)
                {
                    float gap = Vector2.Scale(corner - point, ArtScale).sqrMagnitude;
                    if (gap >= distance || !Contains(corner)) continue;
                    (best, distance) = (corner, gap);
                }
            return best;
        }

        private float ClearFraction(Vector2 from, Vector2 to)
        {
            var segment = to - from;
            if (!Contains(from)) return 0f;
            if (segment.sqrMagnitude <= Epsilon * Epsilon) return 1f;
            _crossings.Clear();
            _crossings.Add(0f);
            _crossings.Add(1f);
            foreach (var ring in _rings)
                for (int i = 0; i < ring.Length; i++)
                {
                    var start = ring[i];
                    var end = ring[(i + 1) % ring.Length];
                    var edge = end - start;
                    float denominator = Cross(segment, edge);
                    if (Mathf.Abs(denominator) <= Epsilon * Epsilon)
                    {
                        if (Mathf.Abs(Cross(start - from, segment)) <= Epsilon * segment.magnitude)
                        {
                            _crossings.Add(Mathf.Clamp01(Vector2.Dot(start - from, segment) / segment.sqrMagnitude));
                            _crossings.Add(Mathf.Clamp01(Vector2.Dot(end - from, segment) / segment.sqrMagnitude));
                        }
                        continue;
                    }
                    float time = Cross(start - from, edge) / denominator;
                    float atEdge = Cross(start - from, segment) / denominator;
                    if (time > 0f && time < 1f && atEdge >= -Epsilon && atEdge <= 1f + Epsilon) _crossings.Add(time);
                }
            _crossings.Sort();
            for (int i = 1; i < _crossings.Count; i++)
            {
                float left = _crossings[i - 1], right = _crossings[i];
                if (right - left <= Epsilon) continue;
                if (!Contains(from + segment * ((left + right) * .5f))) return left;
            }
            return 1f;
        }

        internal bool IsClear(Vector2 from, Vector2 to) => Contains(to) && ClearFraction(from, to) >= 1f;

        internal Vector2 Slide(Vector2 from, Vector2 to)
        {
            from = Ground(from);
            float crossed = ClearFraction(from, to);
            if (crossed >= 1f) return Ground(to);
            var hit = Vector2.LerpUnclamped(from, to, crossed);
            var remaining = to - hit;
            hit = ClosestBoundary(hit, out var tangent, remaining);
            if (tangent == Vector2.zero) return hit;
            var scaled = Vector2.Scale(tangent, ArtScale);
            var parallel = tangent * (Vector2.Dot(Vector2.Scale(remaining, ArtScale), scaled) / scaled.sqrMagnitude);
            return Ground(hit + parallel * ClearFraction(hit, hit + parallel));
        }

        // The fixed visibility graph uses the map's vertices. Only its endpoints change for each click.
        internal void BuildPath(Vector2 from, Vector2 to, List<Vector2> path)
        {
            path.Clear();
            from = Ground(from);
            to = Ground(to);
            path.Add(to);
            if (IsClear(from, to)) return;

            Array.Clear(_visited, 0, _visited.Length);
            for (int i = 0; i < _vertices.Length; i++)
            {
                _previous[i] = -1;
                _cost[i] = IsClear(from, _vertices[i]) ? Distance(from, _vertices[i]) : float.PositiveInfinity;
            }
            int bestEnd = -1;
            float bestTotal = float.PositiveInfinity;
            for (int pass = 0; pass < _vertices.Length; pass++)
            {
                int next = -1;
                float nextCost = bestTotal;
                for (int i = 0; i < _vertices.Length; i++)
                    if (!_visited[i] && _cost[i] < nextCost)
                    {
                        next = i;
                        nextCost = _cost[i];
                    }
                if (next < 0) break;
                _visited[next] = true;
                float total = nextCost + Distance(_vertices[next], to);
                if (total < bestTotal && IsClear(_vertices[next], to)) (bestEnd, bestTotal) = (next, total);
                for (int i = 0; i < _vertices.Length; i++)
                {
                    if (i == next || _visited[i]) continue;
                    float alternative = nextCost + _links[next, i];
                    if (alternative >= _cost[i]) continue;
                    (_cost[i], _previous[i]) = (alternative, next);
                }
            }
            if (bestEnd < 0)
            {
                path[0] = from;
                return;
            }
            for (int i = bestEnd; i >= 0; i = _previous[i]) path.Add(_vertices[i]);
            path.Reverse();
        }
    }
}
