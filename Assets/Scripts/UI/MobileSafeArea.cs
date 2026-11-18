using UnityEngine;

namespace YGOPortable.UI
{
    [DefaultExecutionOrder(-100)]
    internal sealed class MobileSafeArea : MonoBehaviour
    {
        private RectTransform _content;
        private Rect? _area;

        internal static RectTransform Create(Transform fullScreenParent)
        {
            var content = LotdUiKit.FillRect("MobileSafeArea", fullScreenParent);
            Attach(content);
            return content;
        }

        internal static void Attach(RectTransform content, Rect? area = null)
        {
            var layout = content.gameObject.AddComponent<MobileSafeArea>();
            layout._content = content;
            layout._area = area;
            layout.Update();
        }

        private void OnEnable() => Update();

        private void Update()
        {
            if (_content != null) Reflow(Screen.safeArea, new Vector2Int(Screen.width, Screen.height));
        }

        internal void Reflow(Rect safeArea, Vector2Int displaySize)
        {
            if (displaySize.x <= 0 || displaySize.y <= 0 || safeArea.width <= 0f || safeArea.height <= 0f) return;

            var min = new Vector2(Mathf.Clamp01(safeArea.xMin / displaySize.x), Mathf.Clamp01(safeArea.yMin / displaySize.y));
            var max = new Vector2(Mathf.Clamp01(safeArea.xMax / displaySize.x), Mathf.Clamp01(safeArea.yMax / displaySize.y));
            if (min.x >= max.x || min.y >= max.y) return;

            if (_area is { } area)
            {
                var size = max - min;
                max = min + Vector2.Scale(size, area.max);
                min += Vector2.Scale(size, area.min);
            }
            if (_content.anchorMin != min) _content.anchorMin = min;
            if (_content.anchorMax != max) _content.anchorMax = max;
        }
    }
}
