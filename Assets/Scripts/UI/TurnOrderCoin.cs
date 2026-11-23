using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed class TurnOrderCoin
    {
        internal const float RevealAt = 1.1f;
        internal const float LaunchAt = 1.94f;

        private readonly RectTransform _disc;
        private readonly Image _rim;
        private readonly Text _mark;

        internal TurnOrderCoin(RectTransform holder)
        {
            _disc = DuelUiKit.FillRect("Disc", holder, 3f);
            var aspect = _disc.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1f;
            _rim = DuelUiKit.AddImage(_disc.gameObject, DuelStyle.Disc, DuelStyle.Gold, true, false);
            var face = DuelUiKit.FillRect("Face", _disc, 6f);
            DuelUiKit.AddImage(face.gameObject, DuelStyle.Disc, new Color(.075f, .121f, .139f), true, false);
            _mark = DuelUiKit.AddFittedText(face, "Mark", "◆", 64, 22,
                TextAnchor.MiddleCenter, DuelStyle.Gold, FontStyle.Bold);
        }

        internal void Show(float elapsed, bool playerWon)
        {
            float progress = Mathf.Clamp01(elapsed / RevealAt);
            float spin = Mathf.Cos(Mathf.PI * 7f * progress);
            bool heads = progress >= 1f ? playerWon : spin >= 0f;
            _disc.localScale = new Vector3(Mathf.Max(.06f, Mathf.Abs(spin)), 1f + .14f * Mathf.Sin(Mathf.PI * progress), 1f);
            _rim.color = _mark.color = heads ? DuelStyle.Gold : DuelUiKit.CyanAccent;
            _mark.text = heads ? "◆" : "Ⅱ";
        }
    }
}
