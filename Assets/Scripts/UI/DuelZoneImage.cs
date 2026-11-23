using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal enum DuelLinkHighlight { None, Linked, CoLinked }

    // Positions and hit areas share the perspective of the engraved playing surface.
    internal sealed class DuelZoneImage : Image
    {
        private static readonly float[] RowEdges = { 1f, .80f, .60f, .40f, .20f, 0f };
        private static readonly Vector2[] Corners =
        {
            new Vector2(.12f, .078f), new Vector2(.88f, .078f), new Vector2(.947f, .153f), new Vector2(.947f, .847f),
            new Vector2(.88f, .922f), new Vector2(.12f, .922f), new Vector2(.053f, .847f), new Vector2(.053f, .153f),
        };

        private Vector4 _edges = new Vector4(0f, 1f, 0f, 1f);
        private bool _placementHighlighted;
        private bool _pileActionHighlighted;
        private bool _circuitRing;
        private bool _extraMonsterZone;
        internal bool ColumnGuided { get; private set; }
        internal bool ColumnSealed { get; private set; }
        internal bool DragCandidate { get; private set; }
        private float _placementPulse;
        private DuelLinkZoneRim _linkRim;
        internal DuelLinkHighlight LinkHighlight { get; private set; }
        internal bool LinkHighlighted => LinkHighlight != DuelLinkHighlight.None;
        internal bool EquipmentHighlighted { get; private set; }
        internal Vector2Int LinkDirection { get; private set; }
        internal bool SummonReserved { get; private set; }

        internal void ShowCircuitRing() => _circuitRing = true;
        internal void MarkExtraMonsterZone() => _extraMonsterZone = true;
        internal void SetColumnGuide(bool enabled)
        {
            if (ColumnGuided == enabled) return;
            ColumnGuided = enabled;
            SetVerticesDirty();
        }

        internal void SetColumnSealed(bool enabled)
        {
            if (ColumnSealed == enabled) return;
            ColumnSealed = enabled;
            SetVerticesDirty();
        }

        internal void SetDragCandidate(bool enabled)
        {
            if (DragCandidate == enabled) return;
            DragCandidate = enabled;
            _placementPulse = .45f;
            SetVerticesDirty();
        }

        private void Update()
        {
            if (!DragCandidate) return;
            float progress = Mathf.Repeat(Time.unscaledTime / 1.24f, 1f);
            if (Mathf.Abs(progress - _placementPulse) < .018f) return;
            _placementPulse = progress;
            SetVerticesDirty();
        }

        internal void SetPlacementHighlight(bool highlighted, bool summonReserved)
        {
            if (_placementHighlighted == highlighted && SummonReserved == summonReserved) return;
            _placementHighlighted = highlighted;
            SummonReserved = summonReserved;
            if (_linkRim != null) _linkRim.gameObject.SetActive(LinkHighlighted && !highlighted && !SummonReserved);
            SetVerticesDirty();
        }

        internal void SetPileActionHighlight(bool highlighted)
        {
            if (_pileActionHighlighted == highlighted) return;
            _pileActionHighlighted = highlighted;
            SetVerticesDirty();
        }

        internal void SetEquipmentHighlight(bool highlighted)
        {
            if (EquipmentHighlighted == highlighted) return;
            EquipmentHighlighted = highlighted;
            SetVerticesDirty();
        }

        internal void SetLinkHighlight(DuelLinkHighlight highlight, Vector2Int direction = default)
        {
            if (LinkHighlight == highlight && LinkDirection == direction) return;
            LinkHighlight = highlight;
            LinkDirection = direction;
            if (LinkHighlighted && _linkRim == null)
            {
                var outline = DuelUiKit.FillRect("LinkZoneRim", transform);
                outline.SetSiblingIndex(1);
                _linkRim = outline.gameObject.AddComponent<DuelLinkZoneRim>();
                _linkRim.Zone = this;
                _linkRim.texture = Texture2D.whiteTexture;
                _linkRim.raycastTarget = false;
            }
            if (_linkRim != null)
            {
                _linkRim.gameObject.SetActive(LinkHighlighted && !_placementHighlighted && !SummonReserved);
                _linkRim.SetVerticesDirty();
            }
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

            var cell = DuelUiKit.CreateRect($"Zone_{column}_{row}", board,
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
            Vector2 uv = Vector2.LerpUnclamped(new Vector2(.5f, .5f), Corners[index % Corners.Length], size);
            float x = Mathf.Lerp(Mathf.Lerp(_edges.x, _edges.z, uv.y), Mathf.Lerp(_edges.y, _edges.w, uv.y), uv.x);
            return new Vector2(rect.xMin + rect.width * x, rect.yMin + rect.height * uv.y);
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f) return;

            bool link = LinkHighlighted && !_placementHighlighted && !SummonReserved;
            Color tone = SummonReserved ? new Color(.11f, .45f, .49f, .48f) : color;
            if (link)
                tone = LinkHighlight == DuelLinkHighlight.CoLinked ? new Color(.16f, .54f, .41f, .62f) : new Color(.27f, .40f, .82f, .48f);
            int center = mesh.currentVertCount;
            mesh.AddVert(rect.center, tone, Vector2.zero);
            var recess = new Color(tone.r * .70f, tone.g * .72f, tone.b * .76f, Mathf.Min(1f, tone.a * 1.13f));
            for (int i = 0; i < Corners.Length; i++)
                mesh.AddVert(Corner(rect, i, .965f), recess, Vector2.zero);
            for (int i = 0; i < Corners.Length; i++)
                mesh.AddTriangle(center, center + 1 + i, center + 1 + (i + 1) % Corners.Length);

            if (_circuitRing) DrawCircuitRing(mesh, rect, _extraMonsterZone);
            if (ColumnSealed)
            {
                DrawSelectionRim(mesh, rect, new Color(.94f, .22f, .31f, .74f), .10f, .85f, .98f);
                DrawColumnSeal(mesh, rect);
            }
            if (ColumnGuided)
            {
                DrawSelectionRim(mesh, rect, new Color(.15f, .89f, 1f, .37f), .05f, .83f, .98f);
                DrawSelectionRim(mesh, rect, new Color(.56f, .98f, 1f, .90f), .22f, .92f, .985f);
            }

            if (DragCandidate)
                DrawPlacementPulse(mesh, rect, _placementPulse);

            if (_placementHighlighted)
            {
                DrawSelectionRim(mesh, rect, new Color(.99f, .90f, .32f, .96f), 0f);
                return;
            }

            if (SummonReserved)
            {
                DrawSelectionRim(mesh, rect, new Color(.41f, .83f, .79f, .78f), .09f);
                return;
            }

            if (EquipmentHighlighted)
                DrawSelectionRim(mesh, rect, new Color(1f, .76f, .42f, .96f), .13f, .85f, .945f);

            if (link) return;
            var shadow = new Color(.20f, .45f, .54f, .49f);
            var light = new Color(.38f, .69f, .72f, .44f);
            for (int i = 0; i < Corners.Length; i++)
            {
                var rim = i < 4 ? light : shadow;
                var fade = new Color(rim.r, rim.g, rim.b, 0f);
                Vector2 a = Corner(rect, i, .991f), b = Corner(rect, i + 1, .991f);
                DuelMat.AddQuad(mesh, Corner(rect, i, 1.022f), a, b, Corner(rect, i + 1, 1.022f), fade, rim);
                DuelMat.AddQuad(mesh, a, Corner(rect, i, .944f), Corner(rect, i + 1, .944f), b, rim, fade);
            }

            if (_pileActionHighlighted)
                DrawSelectionRim(mesh, rect, new Color(.91f, .74f, .41f, .82f), .09f, .96f);
            if (_extraMonsterZone)
            {
                DrawSelectionRim(mesh, rect, new Color(.21f, .83f, .94f, .27f), .15f, 1f, 1.085f);
                DrawSelectionRim(mesh, rect, new Color(.36f, .89f, .96f, .75f), .18f, .966f, 1.014f);
            }
        }

        private static void DrawPlacementPulse(VertexHelper mesh, Rect rect, float progress)
        {
            var radius = new Vector2(rect.width * .36f, rect.height * .31f);
            var center = rect.center;
            const int segments = 28;
            for (int i = 0; i < segments; i++)
            {
                float start = i * Mathf.PI * 2f / segments;
                float end = (i + 1) * Mathf.PI * 2f / segments;
                var a = new Vector2(Mathf.Cos(start), Mathf.Sin(start));
                var b = new Vector2(Mathf.Cos(end), Mathf.Sin(end));
                var edge = new Color(.37f, .83f, 1f, .80f);
                var glow = new Color(.09f, .59f, 1f, (1f - progress) * .57f);
                DrawRingPart(.75f, .82f, edge);
                DrawRingPart(.82f + .31f * progress, .90f + .31f * progress, glow);

                void DrawRingPart(float inner, float outer, Color color) =>
                    DuelMat.AddQuad(mesh, center + Vector2.Scale(radius, a * outer),
                        center + Vector2.Scale(radius, a * inner), center + Vector2.Scale(radius, b * inner),
                        center + Vector2.Scale(radius, b * outer), color, color);
            }
        }

        private static void DrawColumnSeal(VertexHelper mesh, Rect rect)
        {
            var center = rect.center;
            float width = rect.width * .23f, height = rect.height * .28f;
            var ink = new Color(1f, .31f, .43f, .65f);
            var dark = new Color(.42f, .027f, .062f, .23f);
            for (int i = 0; i < 4; i++)
            {
                float angle = (i + .5f) * Mathf.PI * .5f;
                float next = angle + Mathf.PI * .5f;
                var from = center + new Vector2(Mathf.Cos(angle) * width, Mathf.Sin(angle) * height);
                var to = center + new Vector2(Mathf.Cos(next) * width, Mathf.Sin(next) * height);
                DuelMat.AddQuad(mesh, from, Vector2.Lerp(center, from, .75f), Vector2.Lerp(center, to, .75f), to, ink, dark);
            }
            var vector = new Vector2(width * .57f, height * .55f);
            var normal = new Vector2(-vector.y, vector.x).normalized * 1.4f;
            DuelMat.AddQuad(mesh, center - vector - normal, center - vector + normal,
                center + vector + normal, center + vector - normal, ink, ink);
        }

        private static void DrawCircuitRing(VertexHelper mesh, Rect rect, bool extra)
        {
            const int segments = 28;
            var radius = new Vector2(rect.width * .33f, rect.height * .26f);
            var lit = extra ? new Color(.36f, .91f, .98f, .31f) : new Color(.31f, .76f, .82f, .14f);
            var shade = new Color(.012f, .025f, .044f, .26f);
            for (int i = 0; i < segments; i++)
            {
                if (i % 7 == 6) continue;
                float start = i * Mathf.PI * 2f / segments;
                float end = (i + 1) * Mathf.PI * 2f / segments;
                var a = Vector2.Scale(radius, new Vector2(Mathf.Cos(start), Mathf.Sin(start)));
                var b = Vector2.Scale(radius, new Vector2(Mathf.Cos(end), Mathf.Sin(end)));
                DuelMat.AddQuad(mesh, rect.center + a * 1.08f, rect.center + a * .92f,
                    rect.center + b * .92f, rect.center + b * 1.08f, shade, lit);
            }
        }

        internal void DrawSelectionRim(VertexHelper mesh, Rect rect, Color color, float gap, float innerSize = .90f, float outerSize = .99f)
        {
            for (int i = 0; i < Corners.Length; i++)
            {
                var outerA = Corner(rect, i, outerSize);
                var innerA = Corner(rect, i, innerSize);
                var innerB = Corner(rect, i + 1, innerSize);
                var outerB = Corner(rect, i + 1, outerSize);
                DuelMat.AddQuad(mesh, Vector2.Lerp(outerA, outerB, gap), Vector2.Lerp(innerA, innerB, gap),
                    Vector2.Lerp(innerB, innerA, gap), Vector2.Lerp(outerB, outerA, gap),
                    color, color, Vector2.zero, Vector2.zero);
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

    internal sealed class DuelLinkZoneRim : RawImage
    {
        private static readonly Color LinkedInk = new Color(.75f, .83f, 1f, .97f);
        private static readonly Color CoLinkedInk = new Color(.60f, 1f, .83f, .98f);
        internal DuelZoneImage Zone;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            if (Zone == null || rect.width <= 0f || rect.height <= 0f) return;
            if (Zone.LinkHighlight == DuelLinkHighlight.CoLinked)
            {
                Zone.DrawSelectionRim(mesh, rect, CoLinkedInk, .07f, .91f);
                Zone.DrawSelectionRim(mesh, rect, new Color(.60f, 1f, .83f, .65f), .07f, .79f, .86f);
                return;
            }

            Zone.DrawSelectionRim(mesh, rect, LinkedInk, .16f);
            Zone.DrawSelectionRim(mesh, rect, new Color(.52f, .74f, 1f, .22f), .16f, 1f, 1.115f);
            if (Zone.LinkDirection != Vector2Int.zero) DrawDirection(mesh, rect, Zone.LinkDirection);
        }

        private static void DrawDirection(VertexHelper mesh, Rect rect, Vector2 direction)
        {
            var forward = direction.normalized;
            var across = new Vector2(-forward.y, forward.x);
            float size = Mathf.Min(rect.width, rect.height) * .20f;
            var tip = rect.center + forward * size * .68f;
            var inside = rect.center + forward * size * .04f;
            var tail = rect.center - forward * size * .62f;
            var edge = across * size * .84f;
            var inset = across * size * .43f;
            var halo = new Color(.36f, .74f, 1f, .13f);
            DuelMat.AddQuad(mesh, tip + forward * size * .32f, tail + edge * 1.24f,
                tail - edge * 1.24f, tip - forward * size * .65f, halo, new Color(halo.r, halo.g, halo.b, 0f));
            var shade = new Color(.54f, .71f, 1f, .62f);
            DuelMat.AddQuad(mesh, tip, tail + edge, tail + inset, inside, LinkedInk, shade);
            DuelMat.AddQuad(mesh, tip, inside, tail - inset, tail - edge, LinkedInk, shade);
        }
    }

    internal sealed class DuelArenaGround : RawImage
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f) return;
            float screenAspect = rect.width / rect.height;
            float textureAspect = (float)mainTexture.width / mainTexture.height;
            // Fill the screen without stretching the architecture on wider displays.
            var extent = new Vector2(Mathf.Min(1f, screenAspect / textureAspect), Mathf.Min(1f, textureAspect / screenAspect));
            Vector2 cropMin = (Vector2.one - extent) * .5f;
            DuelMat.AddQuad(mesh, rect.min, new Vector2(rect.xMin, rect.yMax), rect.max, new Vector2(rect.xMax, rect.yMin),
                new Color(.72f, .79f, .87f), new Color(.86f, .92f, .98f), cropMin, cropMin + extent);
        }
    }

    internal sealed class DuelMatSurface : RawImage
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            var (nearLeft, nearRight, farLeft, farRight) = DuelMat.Corners(rect);
            var close = new Color(.53f, .65f, .72f, .93f);
            var far = new Color(.38f, .52f, .65f, .89f);
            const int bands = 16;
            for (int i = 0; i < bands; i++)
            {
                float low = (float)i / bands, high = (float)(i + 1) / bands;
                DuelMat.AddQuad(mesh, Vector2.Lerp(nearLeft, farLeft, low), Vector2.Lerp(nearLeft, farLeft, high),
                    Vector2.Lerp(nearRight, farRight, high), Vector2.Lerp(nearRight, farRight, low),
                    Color.Lerp(close, far, low), Color.Lerp(close, far, high), new Vector2(0f, low), new Vector2(1f, high));
            }
        }
    }

    internal sealed class DuelMat : RawImage
    {
        private static readonly float[] RailStops = { .13f, .19f, .81f, .87f };
        private static readonly float[] RailOffsets = { -8f, -1.7f, 0f, 1.7f, 8f };
        private static readonly float[] RailProfile = { 0f, .47f, 1f, .47f, 0f };
        private bool _opponentTurn;
        private float _opponentLight;

        internal void ShowTurn(bool opponent)
        {
            if (_opponentTurn == opponent) return;
            _opponentTurn = opponent;
            if (!Application.isPlaying) _opponentLight = opponent ? 1f : 0f;
            SetVerticesDirty();
        }

        private void Update()
        {
            float target = _opponentTurn ? 1f : 0f;
            if (_opponentLight == target || CardAnimationManager.Instance?.Paused == true) return;
            _opponentLight = Mathf.MoveTowards(_opponentLight, target, Time.unscaledDeltaTime * 5f * DuelFieldUI.PlaybackRate);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            var (nearLeft, nearRight, farLeft, farRight) = Corners(rect);
            Vector2 drop = Vector2.down * Mathf.Clamp(rect.height * .041f, 11f, 28f);
            Vector2 footInset = new Vector2(6f, 0f);
            Vector2 bottomLeft = nearLeft + drop + footInset;
            Vector2 bottomRight = nearRight + drop - footInset;
            float spread = Mathf.Clamp(rect.width * .023f, 10f, 36f);
            var shadow = new Color(.008f, .022f, .034f, .70f);
            var transparent = new Color(shadow.r, shadow.g, shadow.b, 0f);
            AddQuad(mesh, bottomLeft + drop * 1.6f + Vector2.left * spread, bottomLeft, bottomRight,
                bottomRight + drop * 1.6f + Vector2.right * spread, transparent, shadow);
            AddQuad(mesh, bottomLeft + Vector2.left * spread, bottomLeft, farLeft + drop * .35f,
                farLeft + Vector2.left * spread * .8f, transparent, shadow);
            AddQuad(mesh, farRight + Vector2.right * spread * .8f, farRight + drop * .35f, bottomRight,
                bottomRight + Vector2.right * spread, transparent, shadow);

            var basalt = new Color(.041f, .073f, .093f);
            var reflected = new Color(.17f, .243f, .278f);
            AddQuad(mesh, bottomLeft, nearLeft, nearRight, bottomRight, basalt, reflected);
            AddQuad(mesh, bottomLeft, nearLeft, farLeft, farLeft + drop * .33f, basalt, reflected * .68f);
            AddQuad(mesh, farRight + drop * .33f, farRight, nearRight, bottomRight, basalt, reflected * .86f);
            var lip = new Color(.29f, .60f, .67f, .79f);
            Line(mesh, nearLeft, nearRight, 3.2f, lip);
            Line(mesh, nearLeft, farLeft, 3f, lip);
            Line(mesh, farLeft, farRight, 3f, lip);
            Line(mesh, farRight, nearRight, 3f, lip);
            Line(mesh, bottomLeft, bottomRight, 1.4f, new Color(.36f, .47f, .48f, .62f));

            float farPower = Mathf.SmoothStep(0f, 1f, _opponentLight);
            DrawRail(mesh, nearLeft + drop * .41f, nearRight + drop * .41f, DuelStyle.Player, 1f - farPower);
            DrawRail(mesh, farLeft + Vector2.up * 3.5f, farRight + Vector2.up * 3.5f, DuelStyle.Opponent, farPower);
        }

        private static void DrawRail(VertexHelper mesh, Vector2 left, Vector2 right, Color accent, float power)
        {
            int first = mesh.currentVertCount;
            for (int i = 0; i < RailStops.Length; i++)
            {
                Vector2 origin = Vector2.Lerp(left, right, RailStops[i]);
                float intensity = i == 0 || i == RailStops.Length - 1 ? 0f : Mathf.Lerp(.12f, .91f, power);
                for (int band = 0; band < RailOffsets.Length; band++)
                {
                    var color = new Color(accent.r, accent.g, accent.b, intensity * RailProfile[band]);
                    mesh.AddVert(origin + Vector2.up * RailOffsets[band], color, Vector2.zero);
                }
            }
            int stride = RailOffsets.Length;
            for (int i = 0; i < RailStops.Length - 1; i++)
            for (int band = 0; band < stride - 1; band++)
            {
                int v = first + i * stride + band;
                mesh.AddTriangle(v, v + 1, v + stride + 1);
                mesh.AddTriangle(v, v + stride + 1, v + stride);
            }
        }

        private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
        {
            var delta = (b - a).normalized;
            var normal = new Vector2(-delta.y, delta.x) * (width * .5f);
            AddQuad(mesh, a - normal, a + normal, b + normal, b - normal, color, color);
        }

        internal static (Vector2 NearLeft, Vector2 NearRight, Vector2 FarLeft, Vector2 FarRight) Corners(Rect rect) =>
            (rect.min, new Vector2(rect.xMax, rect.yMin), Point(rect, DuelZoneImage.ProjectX(0f, 1f), 1f),
                Point(rect, DuelZoneImage.ProjectX(1f, 1f), 1f));

        internal static Vector2 Point(Rect rect, float x, float y)
        {
            return new Vector2(rect.xMin + rect.width * x, rect.yMin + rect.height * y);
        }

        internal static void AddQuad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            Color low, Color high, Vector2 minUv = default, Vector2 maxUv = default)
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
