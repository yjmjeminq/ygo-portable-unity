using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed class PadFocusIndicator : MonoBehaviour
    {
        private const float MaxSlideDistance = 475f;
        private static readonly Color TrackColor = new Color(.28f, .79f, .93f, .65f);
        private static readonly Color CornerColor = new Color(1f, .89f, .58f);

        private readonly struct FocusPose
        {
            internal readonly Vector2 Center;
            internal readonly Vector2 Size;
            internal readonly float Angle;

            internal FocusPose(Vector2 center, Vector2 size, float angle)
            {
                Center = center;
                Size = size;
                Angle = angle;
            }
        }

        private readonly Vector3[] _corners = new Vector3[4];
        private readonly RectTransform[] _cornerStrokes = new RectTransform[8];
        private Transform _home;
        private RectTransform _frame;
        private RectTransform _outline;
        private RectTransform _host;
        private RectTransform _target;
        private CanvasGroup _track;
        private CanvasGroup _tips;
        private Color _trackTint = TrackColor;
        private Color _tipTint = CornerColor;
        private FocusPose _displayed;
        private FocusPose _from;
        private float _arrivalTime;
        private float _slideDuration = .12f;
        private bool _sliding;
        private bool _fadingOnArrival;
        private float _cornerLength;

        internal static PadFocusIndicator Create(Transform parent, string name)
        {
            var frame = LotdUiKit.FillRect(name, parent);
            frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var indicator = frame.gameObject.AddComponent<PadFocusIndicator>();
            indicator._home = parent;
            indicator._frame = frame;
            indicator.BuildOutline();
            frame.gameObject.SetActive(false);
            return indicator;
        }

        internal void Show(RectTransform target, RectTransform host, bool animate = true)
        {
            if (target == null || host == null || !target.gameObject.activeInHierarchy)
            {
                Hide();
                return;
            }

            bool sameHost = gameObject.activeSelf && _host == host;
            bool changed = _target != target || !sameHost;
            float now = Time.unscaledTime;
            if (!sameHost)
            {
                _host = host;
                _frame.SetParent(host, false);
                _frame.anchorMin = _frame.anchorMax = host.pivot;
                _frame.SetAsLastSibling();
                gameObject.SetActive(true);
            }
            _target = target;
            var destination = GetTargetPose();
            if (changed)
            {
                _arrivalTime = now;
                _from = _displayed;
                float distance = Vector2.Distance(_from.Center, destination.Center);
                _slideDuration = Mathf.Lerp(.09f, .165f, Mathf.Clamp01(distance / MaxSlideDistance));
                _sliding = animate && sameHost && distance <= MaxSlideDistance;
                _fadingOnArrival = Application.isPlaying && animate && !_sliding;
            }
            else if (!animate) _sliding = _fadingOnArrival = false;
            Draw(now, destination);
        }

        internal void Hide()
        {
            if (!gameObject.activeSelf) return;
            gameObject.SetActive(false);
            if (_home != null) _frame.SetParent(_home, false);
            _target = null;
            _host = null;
            _sliding = _fadingOnArrival = false;
        }

        internal void SetColors(Color? track = null, Color? corners = null)
        {
            SetStrokeColor(_track.transform, ref _trackTint, track ?? TrackColor);
            SetStrokeColor(_tips.transform, ref _tipTint, corners ?? CornerColor);
        }

        private static void SetStrokeColor(Transform layer, ref Color current, Color color)
        {
            if (current == color) return;
            current = color;
            foreach (Transform stroke in layer) stroke.GetComponent<Image>().color = color;
        }

        private void LateUpdate()
        {
            if (_target == null || _host == null || !_target.gameObject.activeInHierarchy)
            {
                Hide();
                return;
            }
            Draw(Time.unscaledTime, GetTargetPose());
        }

        private FocusPose GetTargetPose()
        {
            _target.GetWorldCorners(_corners);
            Vector2 lowerLeft = _host.InverseTransformPoint(_corners[0]);
            var right = (Vector2)_host.InverseTransformPoint(_corners[3]) - lowerLeft;
            var up = (Vector2)_host.InverseTransformPoint(_corners[1]) - lowerLeft;
            return new FocusPose(lowerLeft + (right + up) * .5f, new Vector2(right.magnitude, up.magnitude),
                Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg);
        }

        private void Draw(float now, FocusPose destination)
        {
            float age = Mathf.Max(0f, now - _arrivalTime);
            float t = Mathf.Clamp01(age / _slideDuration);
            _sliding &= t < 1f;
            if (_sliding)
            {
                float left = 1f - t;
                float eased = 1f - left * left * left;
                _displayed = new FocusPose(Vector2.LerpUnclamped(_from.Center, destination.Center, eased),
                    Vector2.LerpUnclamped(_from.Size, destination.Size, eased),
                    Mathf.LerpAngle(_from.Angle, destination.Angle, eased));
            }
            else
            {
                _displayed = destination;
            }

            _frame.anchoredPosition = _displayed.Center;
            _frame.sizeDelta = _displayed.Size;
            _frame.localRotation = Quaternion.Euler(0f, 0f, _displayed.Angle);
            float pulse = .5f + .5f * Mathf.Sin(now * 4.2f);
            float arrival = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / .14f));
            float visibility = _fadingOnArrival ? Mathf.Lerp(.32f, 1f, arrival) : 1f;
            _fadingOnArrival &= arrival < 1f;
            float landing = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / .22f));
            var padding = Vector2.one * (2f + .70f * pulse + 1.85f * landing);
            _outline.offsetMin = -padding;
            _outline.offsetMax = padding;
            _track.alpha = (.68f + .24f * pulse) * visibility;
            _tips.alpha = (.85f + .11f * pulse + .04f * landing) * visibility;

            float length = Mathf.Clamp(Mathf.Min(_displayed.Size.x, _displayed.Size.y) * .32f, 4f, 15f);
            if (Mathf.Approximately(length, _cornerLength)) return;
            _cornerLength = length;
            for (int i = 0; i < _cornerStrokes.Length; i += 2)
            {
                _cornerStrokes[i].sizeDelta = new Vector2(length, 3f);
                _cornerStrokes[i + 1].sizeDelta = new Vector2(3f, length);
            }
        }

        private void BuildOutline()
        {
            _outline = LotdUiKit.FillRect("Outline", _frame);
            _track = CreateLayer("Track");
            _tips = CreateLayer("Corners");

            AddStroke(_track.transform, Vector2.zero, Vector2.right, Vector2.zero, Vector2.up * 1.5f, TrackColor);
            AddStroke(_track.transform, Vector2.up, Vector2.one, Vector2.down * 1.5f, Vector2.zero, TrackColor);
            AddStroke(_track.transform, Vector2.zero, Vector2.up, Vector2.zero, Vector2.right * 1.5f, TrackColor);
            AddStroke(_track.transform, Vector2.right, Vector2.one, Vector2.left * 1.5f, Vector2.zero, TrackColor);

            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2, i / 2);
                for (int axis = 0; axis < 2; axis++)
                {
                    var stroke = AddStroke(_tips.transform, corner, corner, Vector2.zero, Vector2.zero, CornerColor);
                    stroke.pivot = corner;
                    stroke.anchoredPosition = Vector2.zero;
                    _cornerStrokes[i * 2 + axis] = stroke;
                }
            }
        }

        private CanvasGroup CreateLayer(string name)
        {
            var layer = LotdUiKit.FillRect(name, _outline).gameObject.AddComponent<CanvasGroup>();
            layer.interactable = false;
            layer.blocksRaycasts = false;
            return layer;
        }

        private static RectTransform AddStroke(Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin,
            Vector2 offsetMax, Color color)
        {
            var stroke = LotdUiKit.CreateRect("Stroke", parent, min, max, offsetMin, offsetMax);
            LotdUiKit.AddImage(stroke.gameObject, LotdUiKit.WhiteSprite, color, false, false);
            return stroke;
        }
    }
}
