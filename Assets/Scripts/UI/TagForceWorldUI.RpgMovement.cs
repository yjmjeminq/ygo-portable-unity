using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private const float RpgCameraZoom = 1.22f;
        private static readonly Vector2 RpgCameraCenter = new(.50f, .41f);
        private RectTransform _rpgSceneSpace;
        private Vector2 _rpgCameraPan;

        private void BuildRpgCamera()
        {
            var viewport = DuelUiKit.FillRect("FieldViewport", _body);
            viewport.gameObject.AddComponent<RectMask2D>();
            _rpgSceneSpace = DuelUiKit.FillRect("Camera", viewport);
            _rpgSceneSpace.localScale = Vector3.one * RpgCameraZoom;
            _rpgCameraPan = TargetRpgCameraPan();
            ApplyRpgCameraPan();
        }

        private Vector2 TargetRpgCameraPan()
        {
            const float limit = (RpgCameraZoom - 1f) * .5f;
            var target = (RpgCameraCenter - _position) * RpgCameraZoom;
            return new Vector2(Mathf.Clamp(target.x, -limit, limit), Mathf.Clamp(target.y, -limit, limit));
        }

        private void TrackRpgCamera(float delta)
        {
            _rpgCameraPan = Vector2.Lerp(_rpgCameraPan, TargetRpgCameraPan(), 1f - Mathf.Exp(-9f * delta));
            ApplyRpgCameraPan();
        }

        private void ApplyRpgCameraPan()
        {
            _rpgSceneSpace.anchorMin = _rpgCameraPan;
            _rpgSceneSpace.anchorMax = Vector2.one + _rpgCameraPan;
        }

        private RectTransform BuildRpgActorArt(RectTransform art, string path, string name)
        {
            string cycle = RpgArt + "WalkCycles/" + Path.GetFileNameWithoutExtension(path);
            var sides = path == null ? null : _images.GetSlices(cycle + ".png", 2, 2);
            var idle = sides == null ? _images.Get(path) : _images.GetSlices(path, 1, 1)?[0] ?? _images.Get(path);
            if (idle == null)
            {
                Portrait(art, path, name);
                return null;
            }

            if (sides != null)
            {
                float padding = (art.anchorMax.y - art.anchorMin.y) * .095f;
                art.anchorMin += Vector2.up * padding;
                art.anchorMax -= Vector2.up * padding;
            }
            var depth = sides == null ? null : _images.GetSlices(cycle + "_vertical.png", 3, 2);
            var image = DuelUiKit.AddImage(art.gameObject, idle, Color.white, true, false);
            art.gameObject.AddComponent<TagForceRpgWalkAnimator>().Initialize(image, idle, sides, depth);
            return art;
        }

        private static void PoseRpgWalker(RectTransform actor, RectTransform art, Vector2 position, Vector2 step,
            bool walking, Vector2 facing = default)
        {
            actor.anchorMin = actor.anchorMax = position;
            actor.anchoredPosition = Vector2.zero;
            if (art != null && art.TryGetComponent<TagForceRpgWalkAnimator>(out var animation))
                animation.Pose(step.magnitude, walking, facing == Vector2.zero ? step : facing);
        }
    }

    internal sealed class TagForceRpgWalkAnimator : MonoBehaviour
    {
        private enum Facing { Side, Front, Back }
        private Image _image;
        private Sprite _idle;
        private Sprite[] _side, _depth;
        private Facing _facing;
        private bool _left;
        private float _phase;

        internal void Initialize(Image image, Sprite idle, Sprite[] side, Sprite[] depth)
        {
            _image = image;
            _idle = idle;
            _side = side;
            _depth = depth;
        }

        internal void Pose(float distance, bool walking, Vector2 direction)
        {
            if (direction.sqrMagnitude > 1e-7f)
            {
                if (_depth != null && Mathf.Abs(direction.y) > Mathf.Abs(direction.x) * 1.1f)
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
            if (_facing != Facing.Side)
            {
                int row = _facing == Facing.Back ? 3 : 0;
                int offset = walking && (frame & 1) == 0 ? 1 + frame / 2 : 0;
                next = _depth[row + offset];
                flip = offset == 2;
            }
            else if (walking && _side != null) next = _side[frame];
            if (_image.sprite != next) _image.sprite = next;
            transform.localScale = new Vector3(flip ? -1f : 1f, 1f, 1f);
            float tilt = _side == null && walking ? Mathf.Sin(_phase * Mathf.PI * .5f) * 2.5f : 0f;
            transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
        }
    }
}
