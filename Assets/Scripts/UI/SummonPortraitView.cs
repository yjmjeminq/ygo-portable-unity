using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed class SummonPortraitView : MonoBehaviour
    {
        private Sprite _portrait;
        private CanvasGroup _visibility;

        internal void Bind(Sprite portrait, Color accent)
        {
            _portrait = portrait;
            _visibility = gameObject.AddComponent<CanvasGroup>();
            _visibility.interactable = _visibility.blocksRaycasts = false;
            _visibility.alpha = 0f;
            for (int i = 0; i < 2; i++)
            {
                float y = .21f + i * .48f;
                var light = LotdUiKit.CreateRect("Light", transform, new Vector2(-.08f, y),
                    new Vector2(1.08f, y + .013f), Vector2.zero, Vector2.zero);
                light.localRotation = Quaternion.Euler(0f, 0f, 17f);
                LotdUiKit.AddImage(light.gameObject, LotdUiKit.WhiteSprite,
                    new Color(accent.r, accent.g, accent.b, .39f - i * .16f), false, false);
            }
            LotdUiKit.AddImage(LotdUiKit.FillRect("Subject", transform).gameObject, portrait, Color.white, true, false);
        }

        internal void Pose(Vector2 center, float opacity, float scale)
        {
            _visibility.alpha = opacity;
            var rect = (RectTransform)transform;
            rect.anchoredPosition = center;
            rect.localScale = Vector3.one * scale;
        }

        private void OnDisable()
        {
            DuelVisualAssets.ReleaseCutIn(_portrait);
            _portrait = null;
        }
    }
}
