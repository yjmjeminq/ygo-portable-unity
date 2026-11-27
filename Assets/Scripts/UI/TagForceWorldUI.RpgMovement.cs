using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private const float RpgActorScale = 1.22f;
        private const float RpgWalkSpeed = .27416f;
        private const float RpgStrideDistanceScale = 1.46f;
        private const float RpgSceneryFocusY = .55f;
        private static readonly Vector2 RpgFieldActorSize = new(82f, 83f);
        private static readonly Vector2 GxFieldActorSize = new(224f, 228f);
        private readonly List<RectTransform> _rpgDepthOrder = new();
        private readonly List<RectTransform> _rpgFieldProps = new();
        private static readonly System.Comparison<RectTransform> CompareDepth = (left, right) =>
        {
            int y = right.anchorMin.y.CompareTo(left.anchorMin.y);
            return y != 0 ? y : left.GetSiblingIndex().CompareTo(right.GetSiblingIndex());
        };
        private readonly List<Vector2> _rpgWalkPath = new(16);
        private int _rpgWalkWaypoint;
        private RectTransform _rpgSceneSpace, _rpgViewport;
        private Image _rpgFieldScenery;
        private Vector2 _rpgCameraPan, _rpgFramedViewport;
        private float _rpgBackdropAspect, _rpgCameraLead;
        private bool _preserveRpgCamera;
        private bool UsesRpgDialogueCamera => _rpgDialogueCast != null || _page == Page.Guide;

        private Image BuildRpgCamera(Sprite scenery)
        {
            _rpgViewport = DuelUiKit.FillRect("FieldViewport", _body);
            _rpgViewport.gameObject.AddComponent<RectMask2D>();
            _rpgSceneSpace = DuelUiKit.FillRect("Camera", _rpgViewport);
            _rpgSceneSpace.localScale = Vector3.one * RpgActorScale;
            _rpgFieldScenery = DuelUiKit.AddImage(DuelUiKit.FillRect("Scenery", _rpgSceneSpace).gameObject,
                scenery, scenery == null ? new Color(.10f, .21f, .27f) : Color.white, false, false);
            _rpgBackdropAspect = scenery != null ? scenery.rect.width / scenery.rect.height : 16f / 9f;
            if (_preserveRpgCamera)
            {
                var span = RpgCameraSpan();
                _rpgCameraPan = ClampRpgCameraPan(_rpgCameraPan, span);
                ApplyRpgCameraPan(span);
            }
            else FrameRpgCamera(_position);
            return _rpgFieldScenery;
        }

        private void FrameRpgCamera(Vector2 focus)
        {
            _rpgCameraLead = 0f;
            var span = RpgCameraSpan();
            _rpgCameraPan = ClampRpgCameraPan(TargetRpgCameraPan(focus, span), span);
            ApplyRpgCameraPan(span);
        }

        private Vector2 RpgCameraSpan()
        {
            var viewport = _rpgViewport.rect;
            float aspect = viewport.width > 0f && viewport.height > 0f ? viewport.width / viewport.height : 16f / 9f;
            float coverWidth = _rpgBackdropAspect / aspect;
            // Use one zoom for traversal and dialogue; always cover the viewport on both axes.
            float width = Mathf.Max(coverWidth, Mathf.Clamp(1.60f * coverWidth, 1.14f, 1.26f));
            return new Vector2(width, width / coverWidth);
        }

        private Vector2 TargetRpgCameraPan(Vector2 position, Vector2 span)
        {
            var center = new Vector2(.50f, .44f);
            if (UsesRpgDialogueCamera)
            {
                center.y = .61f;
                if (_rpgDialogueCast != null)
                {
                    var (min, max) = RpgStageBounds();
                    float leftCenter = .07f + (position.x - min.x) * span.x;
                    float rightCenter = .93f + (position.x - max.x) * span.x;
                    center.x = leftCenter <= rightCenter ? Mathf.Clamp(center.x, leftCenter, rightCenter) :
                        (leftCenter + rightCenter) * .5f;
                    float lowerCenter = .425f + (position.y - min.y) * span.y;
                    float upperCenter = (IsGxCampaign ? .735f : .875f) + (position.y - max.y) * span.y;
                    center.y = Mathf.Clamp(center.y, lowerCenter, Mathf.Max(lowerCenter, upperCenter));
                }
            }
            else if (_page == Page.Field)
            {
                // Let the player walk beneath the landmarks without pushing the back wall offscreen at the entrance.
                float sceneryHeight = (RpgSceneryFocusY - position.y) * span.y;
                center.y = Mathf.Clamp(.81f - sceneryHeight, .19f, center.y);
            }
            return center - Vector2.one * .5f - Vector2.Scale(position - Vector2.one * .5f, span) -
                Vector2.right * _rpgCameraLead;
        }

        private (Vector2 Min, Vector2 Max) RpgStageBounds()
        {
            var min = _player.anchorMin;
            var max = min;
            foreach (var actor in _rpgStageActors)
            {
                if (!actor.Root.gameObject.activeSelf) continue;
                min = Vector2.Min(min, actor.Root.anchorMin);
                max = Vector2.Max(max, actor.Root.anchorMin);
            }
            return (min, max);
        }

        private Vector2 ClampRpgCameraPan(Vector2 pan, Vector2 span)
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
            _rpgFramedViewport = _rpgViewport.rect.size;
            var center = Vector2.one * .5f + _rpgCameraPan;
            var halfSize = span * (.5f / RpgActorScale);
            _rpgSceneSpace.anchorMin = center - halfSize;
            _rpgSceneSpace.anchorMax = center + halfSize;
            ApplyRpgForegroundParallax(span);
            UpdateRpgExitSigns();
        }

        private void RefreshRpgCameraBounds()
        {
            if (_rpgViewport == null || _rpgViewport.rect.size == _rpgFramedViewport) return;
            var span = RpgCameraSpan();
            _rpgCameraPan = ClampRpgCameraPan(_rpgCameraPan, span);
            ApplyRpgCameraPan(span);
        }

        private Vector2 AdvanceRpgWalk(float delta, bool running)
        {
            var position = _position;
            float speed = IsGxCampaign ? .325f * (running ? 1.55f : 1f) : RpgWalkSpeed;
            float travel = speed * delta * Mathf.Lerp(1f, FieldTerrain.ScaleAt(position.y), .28f);
            if (_manualWalk)
            {
                travel *= _rpgWalkStrength;
                var step = _destination - position;
                float distance = RpgArtDirection(step).magnitude;
                var next = distance <= travel ? _destination : position + step * (travel / distance);
                return FieldTerrain.Slide(position, next);
            }
            while (_rpgWalkWaypoint < _rpgWalkPath.Count)
            {
                var waypoint = _rpgWalkPath[_rpgWalkWaypoint];
                var step = waypoint - position;
                float distance = RpgArtDirection(step).magnitude;
                if (distance > travel) return position + step * (travel / distance);
                position = waypoint;
                travel -= distance;
                ++_rpgWalkWaypoint;
            }
            return position;
        }

        private Vector2 RpgArtDirection(Vector2 direction) => new(direction.x, direction.y / _rpgBackdropAspect);

        private RectTransform RpgFieldProp(string name, Vector2 point, Vector2 size)
        {
            var prop = Anchor(_rpgSceneSpace, name, point, size);
            prop.localScale = Vector3.one * _rpgTerrain.ScaleAt(point.y);
            _rpgFieldProps.Add(prop);
            return prop;
        }

        private void OrderRpgFieldActors()
        {
            _rpgDepthOrder.Clear();
            if (_rpgDialogueCast != null)
            {
                foreach (var actor in _rpgStageActors) _rpgDepthOrder.Add(actor.Root);
            }
            else
            {
                _rpgDepthOrder.AddRange(_rpgFieldProps);
                foreach (var resident in _rpgResidents.Values) _rpgDepthOrder.Add(resident);
                if (_rpgCompanion != null && !_rpgDepthOrder.Contains(_rpgCompanion)) _rpgDepthOrder.Add(_rpgCompanion);
                _rpgDepthOrder.Add(_player);
            }
            _rpgDepthOrder.Sort(CompareDepth);
            var parent = _rpgDialogueCast ?? _rpgSceneSpace;
            if (_rpgForeground != null && _rpgForeground.GetSiblingIndex() + 1 != _rpgSceneSpace.childCount)
                _rpgForeground.SetAsLastSibling();
            int foreground = _rpgDialogueCast == null && _rpgForeground != null ? 1 : 0;
            int first = parent.childCount - _rpgDepthOrder.Count - foreground;
            for (int i = _rpgDepthOrder.Count - 1; i >= 0; i--)
            {
                var actor = _rpgDepthOrder[i];
                if (actor.GetSiblingIndex() != first + i) actor.SetSiblingIndex(first + i);
            }
        }

        private RectTransform BuildRpgActorArt(RectTransform art, string path, string name, string outfitAtlas = null)
        {
            string cycle = RpgArt + "WalkCycles/" + Path.GetFileNameWithoutExtension(path);
            var field = outfitAtlas == null ? null : _images.GetSlices(RpgArt + outfitAtlas, 3, 3, fixedCells: true, fitRows: IsGxCampaign);
            if (field == null && path != null)
                field = _images.GetSlices(cycle + "_field.png", 3, 3, fixedCells: true, fitRows: IsGxCampaign);
            var directions = field ?? (path == null ? null : _images.GetSlices(cycle + "_directions.png", 3, 3));
            var sides = path == null || directions != null ? null : _images.GetSlices(cycle + ".png", 2, 2);
            var profile = path == null || directions != null ? null : _images.GetSlices(cycle + "_side_idle.png", 1, 1)?[0];
            var idle = directions?[0] ?? profile ?? _images.GetSlices(path, 1, 1)?[0] ?? _images.Get(path);
            if (idle == null && IsGxCampaign && path != null)
            {
                // GX visitors outside the field roster may only have the original SD sprite.
                var legacy = RpgArt + "SD/" + Path.GetFileName(path);
                idle = _images.GetSlices(legacy, 1, 1)?[0] ?? _images.Get(legacy);
            }
            if (idle == null)
            {
                Portrait(art, path, name);
                return null;
            }

            if (IsGxCampaign)
            {
                // Grow the sprite upward from its ground position and leave room for the icon above it.
                var actor = (RectTransform)art.parent;
                actor.sizeDelta = GxFieldActorSize;
                actor.pivot = new Vector2(.5f, .277f);
                art.anchorMin = new Vector2(0f, .15f);
                art.anchorMax = new Vector2(1f, .90f);
            }
            else if (directions != null || sides != null || profile != null)
            {
                float padding = (art.anchorMax.y - art.anchorMin.y) * .095f;
                art.anchorMin += Vector2.up * padding;
                art.anchorMax -= Vector2.up * padding;
            }
            var depth = sides == null ? null : _images.GetSlices(cycle + "_vertical.png", 3, 2);
            var (penumbra, core) = AddRpgGroundShadow(art, field != null && !IsGxCampaign ? .06f : 0f);
            var image = DuelUiKit.AddImage(art.gameObject, idle, Color.white, true, false);
            if (IsGxCampaign)
            {
                image.preserveAspect = true;
                image.raycastPadding = new Vector4(29f, -10f, 29f, -8f);
            }
            art.gameObject.AddComponent<TagForceRpgWalkAnimator>().Initialize(image, idle, sides, depth, directions, field != null || IsGxCampaign,
                penumbra, core);
            return art;
        }

        private void PoseRpgWalker(RectTransform actor, RectTransform art, Vector2 position, Vector2 step,
            bool walking, Vector2 facing = default)
        {
            actor.anchorMin = actor.anchorMax = position;
            actor.anchoredPosition = Vector2.zero;
            float scale = _rpgTerrain.ScaleAt(position.y);
            actor.localScale = new Vector3(scale, scale, 1f);
            if (art == null || !art.TryGetComponent<TagForceRpgWalkAnimator>(out var animation)) return;
            float distance = RpgArtDirection(step).magnitude;
            animation.Tint(RpgActorLight(position));
            int foot = animation.Pose(distance * RpgStrideDistanceScale, walking && distance > .00001f,
                RpgArtDirection(facing == Vector2.zero ? step : facing));
            if (foot != 0) EmitRpgFootfall(actor, position, foot);
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
        private float _phase, _widthScale;
        private RectTransform _penumbra, _core;

        internal void Tint(Color color)
        {
            if (_image.color != color) _image.color = color;
        }

        internal void Initialize(Image image, Sprite idle, Sprite[] side, Sprite[] depth, Sprite[] directions, bool compactArt,
            RectTransform penumbra, RectTransform core)
        {
            _image = image;
            _penumbra = penumbra;
            _core = core;
            _idle = idle;
            _side = side;
            _depth = depth;
            _directions = directions;
            _facing = Facing.Side;
            _widthScale = compactArt ? 1f : 1.14f;
            if (directions == null && side == null) _stride = gameObject.AddComponent<TagForceRpgStride>();
        }

        internal int Pose(float distance, bool walking, Vector2 direction)
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

            int previousPlant = Mathf.FloorToInt(_phase * .5f);
            float advance = _phase + distance * 25f;
            int plant = Mathf.FloorToInt(advance * .5f);
            _phase = walking ? advance % 4f : 0f;
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
            var scale = new Vector3(flip ? -_widthScale : _widthScale, 1f, 1f);
            if (transform.localScale != scale) transform.localScale = scale;
            _stride?.SetFrame(walking ? frame : -1);
            float contact = walking ? .5f + .5f * Mathf.Cos(_phase * Mathf.PI) : 1f;
            _penumbra.localScale = new Vector3(.94f + .06f * contact, 1.04f - .04f * contact, 1f);
            _core.localScale = new Vector3(.86f + .14f * contact, .92f + .08f * contact, 1f);
            return walking && plant != previousPlant ? (plant % 2 == 0 ? -1 : 1) : 0;
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
