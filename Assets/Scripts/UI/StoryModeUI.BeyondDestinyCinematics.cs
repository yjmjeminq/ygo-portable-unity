using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed partial class StoryModeUI
    {
        internal sealed partial class StoryDialogueView
        {
            private Image _storyLight;
            private BeyondDestinyVisual _storyVisual;
            private float _previousScale = 1f, _storyScale = 1f;
            private Vector2 _previousFocus = Center, _storyFocus = Center;
            private static readonly Vector2 Center = new(.5f, .5f);

            private void CreateStoryCamera(RectTransform content)
            {
                _storyLight = DuelUiKit.AddImage(DuelUiKit.FillRect("StoryLighting", content).gameObject,
                    DuelUiKit.WhiteSprite, Color.clear, false, false);
                _storyLight.enabled = false;
            }

            private void BeginStoryCamera(BeyondDestinyVisual visual, bool animate)
            {
                _previousScale = _storyScale;
                _previousFocus = _storyFocus;
                _storyVisual = animate ? visual : default;
                if (visual.Camera == BeyondDestinyCamera.Pullback && animate)
                {
                    _previousScale = 1.19f;
                    _previousFocus = new Vector2(.50f, .43f);
                }
                RenderStoryCamera(0f);
            }

            internal void PlayEndingDirection(BeyondDestinyVisual visual)
            {
                _poseTime = 0f;
                BeginStoryCamera(visual, animate: true);
            }

            private void ResetStoryCamera()
            {
                _storyVisual = default;
                _previousScale = _storyScale = 1f;
                _previousFocus = _storyFocus = Center;
                RenderStoryCamera(1f);
            }

            private void RenderStoryCamera(float elapsed)
            {
                var (scale, focus, duration) = _storyVisual.Camera switch
                {
                    BeyondDestinyCamera.Push => (1.105f, new Vector2(.50f, .57f), 1.25f),
                    BeyondDestinyCamera.Hero => (1.22f, new Vector2(.22f, .62f), .65f),
                    BeyondDestinyCamera.Guest => (1.19f, new Vector2(.77f, .69f), .62f),
                    BeyondDestinyCamera.Detail => (1.13f, new Vector2(.49f, .38f), .58f),
                    BeyondDestinyCamera.Pullback => (1f, Center, 1.45f),
                    BeyondDestinyCamera.Pan => (1.10f, new Vector2(.75f, .60f), 1.4f),
                    BeyondDestinyCamera.LookUp => (1.16f, new Vector2(.55f, .89f), 1f),
                    BeyondDestinyCamera.Pit => (1.12f, new Vector2(.8f, .6f), .85f),
                    _ => (1f, Center, .28f),
                };
                float eased = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                _storyScale = Mathf.Lerp(_previousScale, scale, eased);
                _storyFocus = Vector2.Lerp(_previousFocus, focus, eased);
                float shake = _storyVisual.Shake && elapsed < .44f
                    ? Mathf.Sin(elapsed * 54f) * 8f * Mathf.Pow(1f - elapsed / .44f, 2f) : 0f;
                float backdropScale = _storyScale + (shake != 0f ? .018f : 0f);
                MoveStoryArt(_stage.rectTransform, backdropScale, _storyFocus, shake);
                MoveStoryArt(_cutsceneImage.rectTransform, backdropScale, _storyFocus, shake);
                MoveStoryArt(_cutscenePrevious.rectTransform, backdropScale, _storyFocus, shake);
                MoveStoryArt((RectTransform)_portraitGroup.transform,
                    1f + (_storyScale - 1f) * .7f, _storyFocus, shake);
                float durationLight = _storyVisual.Light == BeyondDestinyLight.White ? .32f : .65f;
                float t = Mathf.Clamp01(elapsed / durationLight);
                bool lit = _storyVisual.Light != BeyondDestinyLight.None && t < 1f;
                _storyLight.enabled = lit;
                if (lit)
                {
                    Color color = BeyondDestinyUI.LightColor(_storyVisual.Light);
                    color.a = (_storyVisual.Light == BeyondDestinyLight.White ? .45f : .27f) *
                        Mathf.Sin(Mathf.PI * Mathf.Pow(t, .60f));
                    _storyLight.color = color;
                }
            }

            private static void MoveStoryArt(RectTransform rect, float scale, Vector2 focus, float shake)
            {
                rect.localScale = new Vector3(scale, scale, 1f);
                rect.anchoredPosition = Vector2.Scale(Center - focus, rect.rect.size) * (scale - 1f) + Vector2.right * shake;
            }
        }
    }
}
