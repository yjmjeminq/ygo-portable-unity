using UnityEngine;

namespace YGOPortable.UI
{
    [DefaultExecutionOrder(-100)]
    internal sealed class MobileSafeArea : MonoBehaviour
    {
        private RectTransform _content;

        internal static RectTransform Create(Transform fullScreenParent)
        {
            var content = LotdUiKit.FillRect("MobileSafeArea", fullScreenParent);
            var layout = content.gameObject.AddComponent<MobileSafeArea>();
            layout._content = content;
            layout.Update();
            return content;
        }

        private void OnEnable() => Update();

        private void Update()
        {
            if (_content != null) Apply(_content, Screen.safeArea, new Vector2Int(Screen.width, Screen.height));
        }

        internal static void Apply(RectTransform content, Rect safeArea, Vector2Int displaySize)
        {
            if (displaySize.x <= 0 || displaySize.y <= 0 || safeArea.width <= 0f || safeArea.height <= 0f) return;

            var min = new Vector2(Mathf.Clamp01(safeArea.xMin / displaySize.x), Mathf.Clamp01(safeArea.yMin / displaySize.y));
            var max = new Vector2(Mathf.Clamp01(safeArea.xMax / displaySize.x), Mathf.Clamp01(safeArea.yMax / displaySize.y));
            if (min.x >= max.x || min.y >= max.y) return;

            if (content.anchorMin != min) content.anchorMin = min;
            if (content.anchorMax != max) content.anchorMax = max;
        }
    }
}
