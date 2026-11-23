using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class DuelCutInCinematic : MonoBehaviour, IPointerDownHandler
    {
        internal const float SlideStart = .20f;
        internal const float LandingStart = .80f;
        internal const float Duration = 1.36f;
        private Image _dim;
        private Image _flash;
        private RectTransform _stage;
        private RectTransform _subject;
        private CanvasGroup _picture;
        private CanvasGroup _letterbox;
        private CanvasGroup _caption;
        private Sprite _portrait;
        private bool _skip;

        internal static DuelCutInCinematic Create(RectTransform parent, uint cardCode, Sprite portrait,
            DuelCardReadout card, string method, Color accent, bool opponent)
        {
            var root = DuelUiKit.FillRect("DuelCutInCinematic", parent);
            var cinematic = root.gameObject.AddComponent<DuelCutInCinematic>();
            cinematic.Build(cardCode, portrait, card, method, accent, opponent);
            return cinematic;
        }

        private void Build(uint code, Sprite portrait, DuelCardReadout card, string method, Color accent, bool opponent)
        {
            _portrait = portrait;
            var input = gameObject.AddComponent<CanvasGroup>();
            input.ignoreParentGroups = true;
            _dim = DuelUiKit.AddImage(gameObject, DuelUiKit.WhiteSprite, Color.clear);

            var bars = DuelUiKit.FillRect("Letterbox", transform);
            _letterbox = bars.gameObject.AddComponent<CanvasGroup>();
            foreach (float y in new[] { 0f, .94f })
                DuelUiKit.AddImage(Area("Bar", bars, 0f, y, 1f, y + .06f).gameObject,
                    DuelUiKit.WhiteSprite, new Color(.012f, .021f, .032f, .98f), false, false);

            _stage = DuelUiKit.FillRect("WideStage", transform);
            var fit = _stage.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 16f / 9f;

            _subject = Area("Portrait", _stage, .12f, .085f, .94f, .955f);
            _picture = _subject.gameObject.AddComponent<CanvasGroup>();
            var source = DuelUiKit.FillRect("SourceCanvas", _subject);
            var image = source.gameObject.AddComponent<RawImage>();
            image.texture = portrait.texture;
            image.raycastTarget = false;
            var imageFit = source.gameObject.AddComponent<AspectRatioFitter>();
            imageFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            imageFit.aspectRatio = portrait.texture.width / (float)portrait.texture.height;

            var band = Area("MonsterReadout", _stage, .045f, .086f, .445f, .271f);
            _caption = band.gameObject.AddComponent<CanvasGroup>();
            DuelReadoutPlate.Add(band, new Color(.035f, .066f, .088f, .95f));
            DuelUiKit.AddImage(Area("Accent", band, 0f, 0f, .012f, 1f).gameObject,
                DuelUiKit.WhiteSprite, accent, false, false);
            DuelUiKit.AddFittedText(Area("Method", band, .043f, .76f, .83f, .96f), "Text", method,
                22, 12, TextAnchor.MiddleLeft, accent, FontStyle.Bold);
            DuelUiKit.AddFittedText(Area("MonsterName", band, .043f, .40f, .83f, .75f), "Text",
                CardDatabase.GetName(code), 32, 13, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            var symbol = DuelUiKit.GetAttributeSprite(card.Attribute);
            if (symbol != null)
                DuelUiKit.AddImage(Area("Attribute", band, .84f, .43f, .97f, .91f).gameObject,
                    symbol, Color.white, true, false);
            var values = Area("Values", band, .043f, .07f, card.IsLink ? .66f : .96f, .36f);
            new DuelStatStrip(values, compact: false).Bind(card, opponent);
            if (card.IsLink)
            {
                var directions = Area("Arrows", band, .55f, .045f, .96f, .40f);
                new DuelLinkCompass(directions).Bind(card.LinkMarkers, opponent);
            }
            _flash = DuelUiKit.AddImage(DuelUiKit.FillRect("Impact", transform).gameObject,
                DuelUiKit.WhiteSprite, Color.clear, false, false);
            Pose(0f);
        }

        internal IEnumerator Play(Func<bool> paused, Action<float> land = null)
        {
            float elapsed = 0f;
            bool played = false;
            while (!_skip && elapsed < Duration)
            {
                Pose(elapsed);
                if (!played && elapsed >= SlideStart)
                {
                    played = true;
                    DuelAudio.PlaySummon();
                }
                if (elapsed >= LandingStart)
                    land?.Invoke(Mathf.InverseLerp(LandingStart, Duration, elapsed));
                yield return null;
                if (!paused()) elapsed += Time.unscaledDeltaTime;
            }
            Pose(Duration);
            land?.Invoke(1f);
        }

        internal void Pose(float elapsed)
        {
            float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, SlideStart, elapsed));
            float reveal = Mathf.InverseLerp(SlideStart, LandingStart, elapsed);
            float enter = 1f - Mathf.Pow(1f - reveal, 3f);
            float leave = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LandingStart, Duration, elapsed));
            _dim.color = new Color(0f, 0f, 0f, .75f * fadeIn * (1f - leave));
            _letterbox.alpha = fadeIn * (1f - leave);
            _picture.alpha = enter * (1f - leave);
            _subject.anchoredPosition = Vector2.Scale(_stage.rect.size, new Vector2(.23f, .071f)) * (1f - enter);
            _subject.localScale = Vector3.one * Mathf.Lerp(.96f, 1.04f, enter);
            _caption.alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.37f, .67f, elapsed)) * (1f - leave);
            float flash = Mathf.Sin(Mathf.PI * Mathf.InverseLerp(.77f, .99f, elapsed)) * .24f;
            _flash.color = new Color(.86f, .95f, 1f, flash);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) _skip = true;
        }

        private void OnDisable()
        {
            DuelVisualAssets.ReleaseCutIn(_portrait);
            _portrait = null;
        }

        private static RectTransform Area(string name, Transform parent, float left, float bottom, float right, float top) =>
            DuelUiKit.CreateRect(name, parent, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);
    }
}
