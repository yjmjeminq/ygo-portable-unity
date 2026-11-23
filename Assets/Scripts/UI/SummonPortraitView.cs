using System;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed class SummonPortraitView : MonoBehaviour
    {
        private Action _release;
        private CanvasGroup _visibility;

        internal void Bind(Sprite portrait, Color accent, bool artworkPanel, Action release)
        {
            _release = release;
            _visibility = gameObject.AddComponent<CanvasGroup>();
            _visibility.interactable = _visibility.blocksRaycasts = false;
            _visibility.alpha = 0f;
            for (int i = 0; i < 2; i++)
            {
                float y = .21f + i * .48f;
                var light = DuelUiKit.CreateRect("Light", transform, new Vector2(-.08f, y),
                    new Vector2(1.08f, y + .013f), Vector2.zero, Vector2.zero);
                light.localRotation = Quaternion.Euler(0f, 0f, 17f);
                DuelUiKit.AddImage(light.gameObject, DuelUiKit.WhiteSprite,
                    new Color(accent.r, accent.g, accent.b, .39f - i * .16f), false, false);
            }
            RectTransform holder = (RectTransform)transform;
            if (artworkPanel)
            {
                holder = DuelUiKit.CreateRect("ArtworkPanel", transform, new Vector2(.17f, .08f),
                    new Vector2(.83f, .92f), Vector2.zero, Vector2.zero);
                var plate = DuelUiKit.FillRect("Projection", holder);
                var fit = plate.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = portrait.rect.width / portrait.rect.height;
                DuelReadoutPlate.Add(plate, new Color(accent.r * .45f, accent.g * .45f, accent.b * .45f, .97f));
                holder = DuelUiKit.FillRect("Inset", plate, 6f);
            }
            DuelUiKit.AddImage(DuelUiKit.FillRect("Subject", holder).gameObject, portrait, Color.white, true, false);
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
            var release = _release;
            _release = null;
            release?.Invoke();
        }
    }
}
