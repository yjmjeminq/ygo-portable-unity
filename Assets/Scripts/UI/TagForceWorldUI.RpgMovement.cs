using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private const float RpgCameraWidth = 1.78f;
        private const float RpgActorScale = 1.22f;
        private const float RpgWalkSpeed = .40f * RpgActorScale / RpgCameraWidth;
        private static readonly Vector2 RpgCameraCenter = new(.50f, .44f);
        private RectTransform _rpgSceneSpace, _rpgViewport;
        private Vector2 _rpgCameraPan;
        private float _rpgBackdropAspect, _rpgCameraLead;

        private Image BuildRpgCamera(string mapPath, bool reserveHud)
        {
            _rpgViewport = DuelUiKit.FillRect("FieldViewport", _body);
            if (reserveHud) _rpgViewport.anchorMax = new Vector2(1f, .86f);
            _rpgViewport.gameObject.AddComponent<RectMask2D>();
            _rpgSceneSpace = DuelUiKit.FillRect("Camera", _rpgViewport);
            _rpgSceneSpace.localScale = Vector3.one * RpgActorScale;
            var floor = Backdrop(mapPath, new Color(.10f, .21f, .27f), _rpgSceneSpace);
            _rpgBackdropAspect = floor.sprite != null ? floor.sprite.rect.width / floor.sprite.rect.height : 16f / 9f;
            _rpgCameraLead = 0f;
            var span = RpgCameraSpan();
            _rpgCameraPan = ClampRpgCameraPan(TargetRpgCameraPan(_position, span), span);
            ApplyRpgCameraPan(span);
            return floor;
        }

        private Vector2 RpgCameraSpan()
        {
            var viewport = _rpgViewport.rect;
            float aspect = viewport.width > 0f && viewport.height > 0f ? viewport.width / viewport.height : 16f / 9f;
            float width = Mathf.Max(RpgCameraWidth, _rpgBackdropAspect / aspect);
            return new Vector2(width, width * aspect / _rpgBackdropAspect);
        }

        private Vector2 TargetRpgCameraPan(Vector2 position, Vector2 span) =>
            RpgCameraCenter - Vector2.one * .5f - Vector2.Scale(position - Vector2.one * .5f, span) -
            Vector2.right * _rpgCameraLead;

        private static Vector2 ClampRpgCameraPan(Vector2 pan, Vector2 span)
        {
            var limit = Vector2.Max(Vector2.zero, (span - Vector2.one) * .5f);
            return Vector2.Min(Vector2.Max(pan, -limit), limit);
        }

        private void TrackRpgCamera(float delta, Vector2? focus = null, float horizontalTravel = 0f)
        {
            float lookAhead = Mathf.Abs(horizontalTravel) > 1e-5f ? Mathf.Sign(horizontalTravel) * .065f : 0f;
            _rpgCameraLead = Mathf.Lerp(_rpgCameraLead, lookAhead, 1f - Mathf.Exp(-4f * delta));
            var span = RpgCameraSpan();
            var target = ClampRpgCameraPan(TargetRpgCameraPan(focus ?? _position, span), span);
            _rpgCameraPan = ClampRpgCameraPan(Vector2.Lerp(_rpgCameraPan, target, 1f - Mathf.Exp(-8f * delta)), span);
            ApplyRpgCameraPan(span);
        }

        private void ApplyRpgCameraPan(Vector2 span)
        {
            var center = Vector2.one * .5f + _rpgCameraPan;
            var halfSize = span * (.5f / RpgActorScale);
            _rpgSceneSpace.anchorMin = center - halfSize;
            _rpgSceneSpace.anchorMax = center + halfSize;
        }

        private Vector2 AdvanceRpgWalk(float delta)
        {
            var remaining = _destination - _position;
            float distance = RpgArtDirection(remaining).magnitude;
            float travel = RpgWalkSpeed * delta;
            return distance <= travel ? _destination : _position + remaining * (travel / distance);
        }

        private Vector2 RpgArtDirection(Vector2 direction) => new(direction.x, direction.y / _rpgBackdropAspect);

        private RectTransform BuildRpgActorArt(RectTransform art, string path, string name)
        {
            string cycle = RpgArt + "WalkCycles/" + Path.GetFileNameWithoutExtension(path);
            var directions = path == null ? null : _images.GetSlices(cycle + "_directions.png", 3, 3);
            var sides = path == null || directions != null ? null : _images.GetSlices(cycle + ".png", 2, 2);
            var idle = directions?[3] ?? _images.GetSlices(path, 1, 1)?[0] ?? _images.Get(path);
            if (idle == null)
            {
                Portrait(art, path, name);
                return null;
            }

            if (directions != null || sides != null)
            {
                float padding = (art.anchorMax.y - art.anchorMin.y) * .095f;
                art.anchorMin += Vector2.up * padding;
                art.anchorMax -= Vector2.up * padding;
            }
            var depth = sides == null ? null : _images.GetSlices(cycle + "_vertical.png", 3, 2);
            AddRpgGroundShadow(art);
            var image = DuelUiKit.AddImage(art.gameObject, idle, Color.white, true, false);
            art.gameObject.AddComponent<TagForceRpgWalkAnimator>().Initialize(image, idle, sides, depth, directions);
            return art;
        }

        private void PoseRpgWalker(RectTransform actor, RectTransform art, Vector2 position, Vector2 step,
            bool walking, Vector2 facing = default)
        {
            actor.anchorMin = actor.anchorMax = position;
            actor.anchoredPosition = Vector2.zero;
            if (art != null && art.TryGetComponent<TagForceRpgWalkAnimator>(out var animation))
                animation.Pose(RpgArtDirection(step).magnitude * (RpgCameraWidth / RpgActorScale), walking,
                    RpgArtDirection(facing == Vector2.zero ? step : facing));
        }
    }

    internal sealed class TagForceRpgWalkAnimator : MonoBehaviour
    {
        private enum Facing { Side, Front, Back }
        private Image _image;
        private Sprite _idle;
        private Sprite[] _side, _depth, _directions;
        private TagForceRpgStride _stride;
        private Facing _facing;
        private bool _left;
        private float _phase;

        internal void Initialize(Image image, Sprite idle, Sprite[] side, Sprite[] depth, Sprite[] directions)
        {
            _image = image;
            _idle = idle;
            _side = side;
            _depth = depth;
            _directions = directions;
            if (directions != null) _facing = Facing.Front;
            else if (side == null) _stride = gameObject.AddComponent<TagForceRpgStride>();
        }

        internal void Pose(float distance, bool walking, Vector2 direction)
        {
            if (direction.sqrMagnitude > 1e-7f)
            {
                float threshold = _facing == Facing.Side ? 1.12f : .90f;
                if ((_depth != null || _directions != null) && Mathf.Abs(direction.y) > Mathf.Abs(direction.x) * threshold)
                    _facing = direction.y > 0f ? Facing.Back : Facing.Front;
                else if (Mathf.Abs(direction.x) > 1e-5f)
                {
                    _facing = Facing.Side;
                    _left = direction.x < 0f;
                }
            }

            _phase = walking ? (_phase + distance * 25f) % 4f : 0f;
            int frame = (int)_phase;
            Sprite next = _idle;
            bool flip = _left;
            if (_directions != null)
            {
                int row = _facing switch { Facing.Side => 0, Facing.Front => 3, _ => 6 };
                int offset = walking && (frame & 1) == 0 ? 1 + frame / 2 : 0;
                next = _directions[row + offset];
                flip = _facing == Facing.Side && _left;
            }
            else if (_facing != Facing.Side)
            {
                int row = _facing == Facing.Back ? 3 : 0;
                int offset = walking && (frame & 1) == 0 ? 1 + frame / 2 : 0;
                next = _depth[row + offset];
                flip = offset == 2;
            }
            else if (walking && _side != null) next = _side[frame];
            if (_image.sprite != next) _image.sprite = next;
            transform.localScale = new Vector3(flip ? -1f : 1f, 1f, 1f);
            _stride?.SetFrame(walking ? frame : -1);
        }
    }

    internal sealed class TagForceRpgStride : BaseMeshEffect
    {
        private const int Columns = 8;
        private const int Rows = 10;
        private int _frame = -1;

        internal void SetFrame(int frame)
        {
            if (_frame == frame) return;
            _frame = frame;
            graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (_frame < 0 || !IsActive() || mesh.currentVertCount != 4) return;

            var bottomLeft = UIVertex.simpleVert;
            var topLeft = UIVertex.simpleVert;
            var topRight = UIVertex.simpleVert;
            var bottomRight = UIVertex.simpleVert;
            mesh.PopulateUIVertex(ref bottomLeft, 0);
            mesh.PopulateUIVertex(ref topLeft, 1);
            mesh.PopulateUIVertex(ref topRight, 2);
            mesh.PopulateUIVertex(ref bottomRight, 3);
            float height = topLeft.position.y - bottomLeft.position.y;
            if (height <= 0f) return;
            float footReach = Mathf.Min(height * .062f, (bottomRight.position.x - bottomLeft.position.x) * .22f);

            float stride = _frame == 0 ? 1f : _frame == 2 ? -1f : 0f;
            float passing = _frame == 1 ? 1f : _frame == 3 ? -1f : 0f;
            mesh.Clear();
            for (int row = 0; row <= Rows; row++)
            {
                float y = (float)row / Rows;
                float legs = 1f - Mathf.SmoothStep(0f, 1f, y / .43f);
                float shoulders = Mathf.SmoothStep(0f, 1f, y);
                for (int column = 0; column <= Columns; column++)
                {
                    float x = (float)column / Columns;
                    float side = Mathf.Clamp((x - .5f) * 3.2f, -1f, 1f);
                    var vertex = bottomLeft;
                    vertex.position = Vector3.Lerp(Vector3.Lerp(bottomLeft.position, bottomRight.position, x),
                        Vector3.Lerp(topLeft.position, topRight.position, x), y);
                    vertex.uv0 = Vector4.Lerp(Vector4.Lerp(bottomLeft.uv0, bottomRight.uv0, x),
                        Vector4.Lerp(topLeft.uv0, topRight.uv0, x), y);
                    vertex.position.x += footReach * stride * side * legs + height * .014f * shoulders;
                    vertex.position.y += height * .071f * legs * Mathf.Max(0f, side * passing - .10f);
                    mesh.AddVert(vertex);
                    if (row == 0 || column == 0) continue;
                    int index = row * (Columns + 1) + column;
                    int previous = index - (Columns + 1);
                    mesh.AddTriangle(previous - 1, index - 1, index);
                    mesh.AddTriangle(index, previous, previous - 1);
                }
            }
        }
    }
}
