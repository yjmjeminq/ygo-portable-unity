using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    // Contours are measured in the bundled, 224-pixel card illustrations, with Y down.
    // The extra layer samples the original texture, without creating another copy of the art.
    internal sealed class CardOverframeProfile
    {
        internal readonly Vector2[] Uv;
        internal readonly Vector2[] Layout;
        private int[] _triangles;
        internal int[] Triangles => _triangles ??= Triangulate(Uv, Clockwise);
        internal readonly bool Clockwise;

        private static readonly Dictionary<uint, CardOverframeProfile> Profiles = new()
        {
            [84815190] = new(new[] {
                48,0, 56,22, 67,14, 79,16, 106,1, 126,3, 154,24, 170,24, 182,45, 193,75,
                223,66, 223,127, 204,141, 212,164, 219,196, 205,207, 190,182, 168,174, 161,214,
                145,222, 132,210, 120,185, 101,213, 86,220, 75,199, 58,198, 33,216, 13,211,
                3,198, 8,176, 21,150, 26,127, 0,142, 0,129, 27,105, 20,79, 31,64, 23,54, 38,32,
            }),
            [46986414] = new(new[] {
                94,0, 125,26, 149,16, 153,27, 149,50, 171,39, 182,58, 214,41, 222,48, 222,62,
                184,72, 163,83, 167,96, 153,106, 158,133, 181,151, 221,204, 215,217, 185,211,
                146,190, 128,171, 119,189, 112,223, 83,223, 86,194, 82,184, 56,215, 31,221,
                3,212, 47,186, 62,166, 66,139, 79,112, 57,108, 17,123, 12,118, 52,98,
                86,86, 96,67, 98,52, 112,34,
            }),
            [89631139] = new(new[] {
                23,0, 54,0, 66,11, 83,27, 88,54, 86,71, 80,91, 89,103, 105,94, 96,75,
                97,61, 108,43, 134,35, 153,29, 172,32, 169,41, 155,50, 146,56, 153,60, 140,64,
                135,77, 153,64, 167,45, 189,26, 201,18, 224,17, 223,30, 214,36, 223,57, 217,82,
                215,66, 201,60, 188,75, 175,92, 164,105, 166,124, 182,118, 193,121, 200,131,
                191,137, 175,141, 161,153, 147,146, 143,137, 134,155, 126,166, 119,190, 101,189,
                94,182, 84,193, 63,217, 43,224, 34,214, 31,198, 24,185, 8,182, 0,169, 7,151,
                27,138, 47,131, 33,119, 10,113, 0,104, 0,89, 22,96, 47,97, 57,85, 65,59, 52,44, 27,34, 0,28, 0,23, 39,31,
            }),
            [9674034] = new(new[] {
                2,24, 32,45, 51,74, 66,81, 58,64, 43,44, 40,22, 48,19, 55,37, 68,50, 85,78,
                79,53, 87,60, 96,78, 111,78, 119,62, 139,54, 145,64, 133,78, 147,74, 162,49,
                165,32, 176,22, 184,25, 173,42, 170,58, 186,47, 194,33, 209,19, 218,20, 215,36,
                203,43, 194,63, 180,73, 162,93, 153,99, 155,116, 167,123, 175,141, 195,145,
                203,156, 192,162, 173,159, 157,146, 158,167, 185,192, 173,194, 166,188, 154,190,
                150,208, 132,213, 138,200, 134,178, 116,155, 108,141, 94,131, 80,142, 74,159,
                58,164, 42,188, 22,194, 23,185, 43,155, 52,136, 69,121, 65,108, 40,102, 21,83, 5,63, 0,43,
            }),
        };

        private static readonly Dictionary<(uint Code, int Variant), CardOverframeProfile> LocalProfiles = new()
        {
            [(84815190, 0)] = Profiles[84815190],
            [(9674034, 0)] = Profiles[9674034],
            [(46986414, 0)] = new(new[] {
                43,0, 83,0, 96,30, 87,51, 84,63, 105,69, 121,69, 143,86, 153,104, 162,118, 158,132,
                148,151, 170,149, 183,153, 194,166, 224,166, 224,200, 187,191, 158,184, 144,183,
                135,181, 128,168, 127,199, 109,213, 94,216, 85,205, 80,189, 81,174, 60,165, 51,163,
                65,136, 60,125, 51,119, 45,127, 32,134, 24,128, 19,116, 5,91, 7,82, 30,98, 38,98,
                35,88, 27,81, 34,75, 21,68, 10,53, 15,40, 40,48,
            }),
            [(89631139, 3801)] = new(new[] {
                80,10, 113,0, 106,16, 153,8, 169,15, 188,28, 193,13, 196,3, 200,35, 202,69,
                194,101, 189,115, 198,111, 207,124, 208,145, 199,154, 183,155, 160,181, 170,184,
                174,194, 164,204, 149,205, 168,216, 156,224, 129,224, 105,217, 88,210, 72,220,
                64,218, 55,207, 53,216, 46,224, 22,222, 14,213, 17,202, 32,191, 43,193, 52,180,
                39,175, 43,161, 49,148, 62,139, 76,135, 82,125, 75,112, 67,102, 55,123, 44,150,
                36,164, 24,185, 20,179, 21,146, 28,108, 38,78, 48,53, 65,30,
            }),
            [(46986414, 3863)] = new(new[] {
                85,0, 105,31, 134,11, 164,2, 169,7, 147,44, 128,60, 126,75, 141,69, 145,77,
                141,86, 174,105, 192,100, 192,111, 175,120, 160,113, 141,111, 136,140, 140,151,
                164,142, 177,139, 195,141, 201,151, 187,170, 164,166, 143,174, 146,199, 157,224,
                119,224, 106,208, 96,224, 64,224, 72,196, 22,216, 12,224, 0,224, 0,214, 33,191,
                69,172, 69,166, 0,201, 0,193, 67,165, 67,121, 62,117, 68,86, 41,78, 58,67, 50,52,
                54,44, 74,51, 75,28,
            }),
            [(46986414, 3880)] = new(new[] {
                84,0, 94,0, 111,12, 124,33, 143,10, 152,18, 145,51, 159,41, 164,47, 161,59,
                180,50, 200,36, 224,27, 224,51, 214,63, 187,67, 160,78, 159,92, 164,102, 153,113,
                131,112, 126,133, 143,158, 176,177, 205,210, 206,224, 193,224, 158,204, 137,188,
                125,172, 116,197, 112,224, 82,224, 85,194, 96,174, 89,171, 79,196, 65,213, 60,224,
                24,224, 8,220, 33,213, 49,186, 64,164, 74,150, 83,120, 60,131, 48,135, 35,133, 19,140,
                11,136, 34,123, 50,114, 72,100, 92,82, 89,62, 99,55, 100,39, 95,20,
            }),
            [(46986414, 23487)] = new(new[] {
                61,0, 120,0, 121,19, 115,48, 113,73, 127,61, 149,58, 158,66, 153,81, 166,74,
                163,85, 154,94, 165,105, 176,91, 223,65, 224,75, 207,104, 188,119, 165,121,
                151,135, 153,148, 149,165, 145,190, 136,203, 125,215, 126,224, 110,224, 112,205,
                96,194, 91,196, 82,222, 64,224, 59,209, 60,189, 32,202, 22,192, 7,162, 0,149, 3,144,
                30,177, 50,182, 63,170, 80,151, 88,141, 78,138, 65,127, 38,145, 29,139, 21,126,
                1,126, 18,111, 24,101, 39,92, 42,76, 54,79, 52,56, 55,24,
            }),
        };

        private CardOverframeProfile(int[] points)
        {
            Uv = new Vector2[points.Length / 2];
            Layout = new Vector2[Uv.Length];
            Vector2 low = Vector2.one, high = Vector2.zero;
            for (int i = 0; i < Uv.Length; i++)
            {
                Uv[i] = new Vector2(points[i * 2] / 224f, 1f - points[i * 2 + 1] / 224f);
                low = Vector2.Min(low, Uv[i]);
                high = Vector2.Max(high, Uv[i]);
            }

            for (int i = 0; i < Uv.Length; i++)
            {
                Vector2 uv = Uv[i];
                Layout[i] = new Vector2(
                    Mathf.Lerp(-.10f, 1.10f, Mathf.InverseLerp(low.x, high.x, uv.x)),
                    Mathf.Lerp(.423f, 1.163f, Mathf.InverseLerp(low.y, high.y, uv.y)));
            }
            float area = 0f;
            for (int i = 0; i < Uv.Length; i++)
                area += Cross(Uv[i], Uv[(i + 1) % Uv.Length]);
            Clockwise = area < 0f;
        }

        internal static void PrepareContours()
        {
            // Build small CPU-side meshes before the player can open a view or start a duel.
            foreach (var profile in Profiles.Values) _ = profile.Triangles;
            foreach (var profile in LocalProfiles.Values) _ = profile.Triangles;
        }

        internal static bool MayHaveProfile(uint code)
        {
            if (Profiles.ContainsKey(code)) return true;
            foreach (uint printing in CardArtLoader.RelatedPrintings(code))
                if (Profiles.ContainsKey(printing)) return true;
            return false;
        }

        // Match the decoded artwork, since a fallback or alternate printing can change the silhouette.
        internal static CardOverframeProfile Get(uint artCode, int artVariant) =>
            artVariant >= 0 ? LocalProfiles.GetValueOrDefault((artCode, artVariant)) : Profiles.GetValueOrDefault(artCode);

        private static int[] Triangulate(Vector2[] points, bool clockwise)
        {
            var remaining = new List<int>(points.Length);
            var result = new List<int>((points.Length - 2) * 3);
            for (int i = 0; i < points.Length; i++)
                remaining.Add(clockwise ? points.Length - i - 1 : i);

            while (remaining.Count > 2)
            {
                bool found = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i],
                        c = remaining[(i + 1) % remaining.Count];
                    if (Cross(points[b] - points[a], points[c] - points[a]) <= .000001f) continue;
                    if (ContainsOtherVertex(points, remaining, a, b, c)) continue;
                    result.Add(a);
                    result.Add(b);
                    result.Add(c);
                    remaining.RemoveAt(i);
                    found = true;
                    break;
                }
                if (!found) return System.Array.Empty<int>();
            }
            return result.ToArray();
        }

        private static bool ContainsOtherVertex(Vector2[] points, List<int> remaining, int a, int b, int c)
        {
            Vector2 aa = points[a], bb = points[b], cc = points[c];
            Vector2 ab = bb - aa, bc = cc - bb, ca = aa - cc;
            foreach (int index in remaining)
            {
                if (index == a || index == b || index == c) continue;
                Vector2 point = points[index];
                if (Cross(ab, point - aa) >= 0f && Cross(bc, point - bb) >= 0f && Cross(ca, point - cc) >= 0f)
                    return true;
            }
            return false;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }

    internal sealed class CardOverframeInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private CompositeCardView _card;
        private RectTransform _holder;
        private Rect _flatArea;
        private Rect _depthArea;
        private Gyroscope _gyro;
        private bool _ownsGyro;
        private Vector2? _gravityReference;
        private bool _pressed;
        private int _pointer;
        private Vector2 _origin;
        private Vector2 _drag;
        private Vector2 _depth;
        private bool _laidOutForDepth;

        internal static void Attach(RectTransform holder, CompositeCardView card, bool motionSensor, Rect flatArea, Rect depthArea)
        {
            var hit = LotdUiKit.AddImage(holder.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            var input = holder.gameObject.AddComponent<CardOverframeInput>();
            input._card = card;
            input._holder = holder;
            input._flatArea = flatArea;
            input._depthArea = depthArea;
            input._gyro = motionSensor && Application.isMobilePlatform && SystemInfo.supportsGyroscope ? Input.gyro : null;
            hit.raycastTarget = true;
        }

        private void LateUpdate()
        {
            if (_card == null) return;
            bool visible = _card.HasOverframe;
            if (visible != _laidOutForDepth)
            {
                _laidOutForDepth = visible;
                Rect area = visible ? _depthArea : _flatArea;
                _holder.anchorMin = area.min;
                _holder.anchorMax = area.max;
                SetSensorEnabled(visible);
            }
            if (!visible)
            {
                ResetPose();
                return;
            }

            Vector2 target = _pressed ? _drag : ReadMotion();
            _depth = Vector2.Lerp(_depth, target, 1f - Mathf.Exp(-17f * Time.unscaledDeltaTime));
            ShowDepth(_depth, Time.unscaledTime);
        }

        private void SetSensorEnabled(bool visible)
        {
            _gravityReference = null;
            if (_gyro == null) return;
            if (visible && !_gyro.enabled)
            {
                _gyro.enabled = true;
                _ownsGyro = true;
            }
            else if (!visible && _ownsGyro)
            {
                _gyro.enabled = false;
                _ownsGyro = false;
            }
        }

        private Vector2 ReadMotion()
        {
            if (_gyro == null) return Vector2.zero;
            Vector3 gravity = _gyro.gravity;
            if (gravity.sqrMagnitude < .1f) return Vector2.zero;
            Vector2 projected = Screen.orientation == ScreenOrientation.LandscapeRight
                ? new Vector2(gravity.y, -gravity.x) : new Vector2(-gravity.y, gravity.x);
            _gravityReference ??= projected;
            return Vector2.ClampMagnitude((projected - _gravityReference.Value) * 3.4f, 1f);
        }

        private void ShowDepth(Vector2 depth, float elapsed)
        {
            _card.transform.localRotation = Quaternion.Euler(-depth.y * 6f, depth.x * 9f, 0f);
            _card.PoseOverframe(1f, 1f, depth, elapsed * .27f + depth.x * .18f);
        }

        private void ResetPose()
        {
            if (_depth == Vector2.zero && !_pressed) return;
            _pressed = false;
            _drag = _depth = Vector2.zero;
            ShowDepth(Vector2.zero, 0f);
        }

        private void OnDisable()
        {
            SetSensorEnabled(false);
            _laidOutForDepth = false;
            if (_holder != null)
            {
                _holder.anchorMin = _flatArea.min;
                _holder.anchorMax = _flatArea.max;
                ResetPose();
            }
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (!_card.HasOverframe || _pressed || data.button != PointerEventData.InputButton.Left) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_holder, data.position, data.pressEventCamera, out _origin)) return;
            _pointer = data.pointerId;
            _pressed = true;
            _drag = Vector2.zero;
        }

        public void OnBeginDrag(PointerEventData data) => OnDrag(data);

        public void OnDrag(PointerEventData data)
        {
            if (!_pressed || data.pointerId != _pointer ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(_holder, data.position, data.pressEventCamera, out var point)) return;
            _drag = Vector2.ClampMagnitude((point - _origin) / Mathf.Max(1f, _holder.rect.width * .35f), 1f);
        }

        public void OnEndDrag(PointerEventData data) => OnPointerUp(data);

        public void OnPointerUp(PointerEventData data)
        {
            if (_pressed && _pointer == data.pointerId) _pressed = false;
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class CardOverframeGraphic : MaskableGraphic
    {
        private Sprite _art;
        private CardOverframeProfile _profile;
        private bool _edge;

        public override Texture mainTexture => _edge || _art == null ? Texture2D.whiteTexture : _art.texture;

        internal void Bind(Sprite art, CardOverframeProfile profile, bool edge)
        {
            if (_art == art && _profile == profile && _edge == edge) return;
            _art = art;
            _profile = profile;
            _edge = edge;
            SetAllDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (_profile == null || _art == null) return;
            Rect rect = rectTransform.rect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            if (_edge)
            {
                float thickness = Mathf.Max(.7f, rect.width * .006f);
                for (int i = 0; i < _profile.Layout.Length; i++)
                {
                    Vector2 a = Point(rect, _profile.Layout[i]);
                    Vector2 b = Point(rect, _profile.Layout[(i + 1) % _profile.Layout.Length]);
                    Vector2 direction = (b - a).normalized;
                    Vector2 offset = new Vector2(-direction.y, direction.x) * thickness;
                    a -= direction * thickness * .5f;
                    b += direction * thickness * .5f;
                    int start = mesh.currentVertCount;
                    AddVertex(mesh, a - offset, Vector2.zero, Color.white);
                    AddVertex(mesh, a + offset, Vector2.zero, Color.white);
                    AddVertex(mesh, b + offset, Vector2.zero, Color.white);
                    AddVertex(mesh, b - offset, Vector2.zero, Color.white);
                    mesh.AddTriangle(start, start + 1, start + 2);
                    mesh.AddTriangle(start, start + 2, start + 3);
                }
                return;
            }

            // CardArtLoader creates full-rect sprites from individual card textures.
            int count = _profile.Uv.Length;
            for (int i = 0; i < count; i++)
                AddVertex(mesh, Point(rect, _profile.Layout[i]), _profile.Uv[i], Color.white);
            for (int i = 0; i < _profile.Triangles.Length; i += 3)
                mesh.AddTriangle(_profile.Triangles[i], _profile.Triangles[i + 1], _profile.Triangles[i + 2]);

            float fringe = Mathf.Clamp(rect.width * .009f, .55f, 2.3f);
            float winding = _profile.Clockwise ? 1f : -1f;
            for (int i = 0; i < count; i++)
            {
                Vector2 previous = Point(rect, _profile.Layout[(i + count - 1) % count]);
                Vector2 current = Point(rect, _profile.Layout[i]);
                Vector2 next = Point(rect, _profile.Layout[(i + 1) % count]);
                Vector2 normal = Vector2.Perpendicular((current - previous).normalized + (next - current).normalized).normalized;
                AddVertex(mesh, current + normal * (winding * fringe), _profile.Uv[i], new Color32(255, 255, 255, 0));
            }
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                mesh.AddTriangle(i, j, count + j);
                mesh.AddTriangle(i, count + j, count + i);
            }
        }

        private static Vector2 Point(Rect rect, Vector2 uv) => rect.min + Vector2.Scale(rect.size, uv);

        private static void AddVertex(VertexHelper mesh, Vector2 position, Vector2 uv, Color32 tint) =>
            mesh.AddVert(position, tint, uv);
    }
}
