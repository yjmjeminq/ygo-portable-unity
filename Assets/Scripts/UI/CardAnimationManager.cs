using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public class CardAnimationManager : MonoBehaviour
    {
        public static CardAnimationManager Instance { get; private set; }

        private RectTransform _animLayer;
        private readonly Queue<GameObject> _proxyPool = new Queue<GameObject>();
        private float _nextFlightStartTime = 0f;

        public static CardAnimationManager Ensure(Canvas canvas)
        {
            if (Instance != null)
            {
                Instance.BringToFront();
                return Instance;
            }
            var go = new GameObject("CardAnimationManager", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Instance = go.AddComponent<CardAnimationManager>();
            Instance.Initialize(rt);
            Instance.BringToFront();
            return Instance;
        }

        public void BringToFront()
        {
            if (_animLayer != null) _animLayer.SetAsLastSibling();
            if (CardDragManager.Instance != null) CardDragManager.Instance.transform.SetAsLastSibling();
        }

        private void Initialize(RectTransform layer)
        {
            _animLayer = layer;
        }

        private GameObject GetOrCreateProxy()
        {
            if (_proxyPool.Count > 0)
            {
                var item = _proxyPool.Dequeue();
                if (item != null)
                {
                    item.SetActive(true);
                    item.transform.SetAsLastSibling();
                    return item;
                }
            }

            var go = new GameObject("AnimCardProxy", typeof(RectTransform));
            go.transform.SetParent(_animLayer, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(104f, 150f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            var ol = go.AddComponent<Outline>();
            ol.effectColor = LotdUiKit.GoldAccent;
            ol.effectDistance = new Vector2(2.5f, -2.5f);

            CompositeCardView.Create(rt, "CV", false);
            return go;
        }

        private void RecycleProxy(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            _proxyPool.Enqueue(go);
        }

        public float PlayCardFlight(uint code, Vector2 startScreenPos, Vector2 endScreenPos,
            float duration = 0.34f, bool faceDown = false, float arcHeight = 52f,
            Action onComplete = null)
        {
            if (startScreenPos == Vector2.zero || endScreenPos == Vector2.zero)
            {
                onComplete?.Invoke();
                return 0f;
            }

            BringToFront();
            float now = Time.unscaledTime;
            float delay = 0f;
            if (_nextFlightStartTime > now)
            {
                delay = Mathf.Min(0.45f, _nextFlightStartTime - now);
            }
            _nextFlightStartTime = Mathf.Max(now, _nextFlightStartTime) + 0.065f;

            StartCoroutine(CardFlightRoutine(code, startScreenPos, endScreenPos, delay, duration, faceDown, arcHeight, onComplete));
            return delay + duration;
        }

        private IEnumerator CardFlightRoutine(uint code, Vector2 startPos, Vector2 endPos,
            float delay, float duration, bool faceDown, float arcHeight, Action onComplete)
        {
            if (delay > 0.001f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            BringToFront();
            var proxy = GetOrCreateProxy();
            var rt = (RectTransform)proxy.transform;
            var cv = proxy.GetComponentInChildren<CompositeCardView>();
            if (cv != null) cv.SetCard(code, faceDown);

            float timer = 0f;
            duration = Mathf.Max(0.14f, duration);

            while (timer < duration)
            {
                timer += Time.deltaTime;
                float t = Mathf.Clamp01(timer / duration);
                float ease = Mathf.SmoothStep(0f, 1f, t);

                Vector2 linear = Vector2.Lerp(startPos, endPos, ease);
                float arc = Mathf.Sin(t * Mathf.PI) * arcHeight;
                rt.position = new Vector3(linear.x, linear.y + arc, 0f);

                float scalePunch = 1.0f + Mathf.Sin(t * Mathf.PI) * 0.24f;
                rt.localScale = new Vector3(scalePunch, scalePunch, 1f);

                yield return null;
            }

            rt.position = endPos;
            rt.localScale = Vector3.one;

            RecycleProxy(proxy);
            onComplete?.Invoke();
        }

        public void PlayAttackLunge(RectTransform attackerSlot, Vector2 targetScreenPos,
            float duration = 0.28f, Action onImpact = null, Action onComplete = null)
        {
            if (attackerSlot == null)
            {
                onImpact?.Invoke();
                onComplete?.Invoke();
                return;
            }

            StartCoroutine(AttackLungeRoutine(attackerSlot, targetScreenPos, duration, onImpact, onComplete));
        }

        private IEnumerator AttackLungeRoutine(RectTransform slot, Vector2 targetPos,
            float duration, Action onImpact, Action onComplete)
        {
            Vector3 origPos = slot.position;
            Vector3 dir = ((Vector3)targetPos - origPos).normalized;
            float maxLunge = Mathf.Min(60f, Vector3.Distance(origPos, targetPos) * 0.35f);

            float halfDur = Mathf.Max(0.08f, duration * 0.45f);
            float backDur = Mathf.Max(0.08f, duration * 0.55f);

            // Forward lunge
            float timer = 0f;
            while (timer < halfDur)
            {
                timer += Time.deltaTime;
                float t = Mathf.Clamp01(timer / halfDur);
                float ease = Mathf.Sin(t * Mathf.PI * 0.5f);
                slot.position = origPos + dir * (maxLunge * ease);
                yield return null;
            }

            onImpact?.Invoke();

            // Return recoil
            timer = 0f;
            Vector3 peakPos = slot.position;
            while (timer < backDur)
            {
                timer += Time.deltaTime;
                float t = Mathf.Clamp01(timer / backDur);
                float ease = Mathf.SmoothStep(0f, 1f, t);
                slot.position = Vector3.Lerp(peakPos, origPos, ease);
                yield return null;
            }

            slot.position = origPos;
            onComplete?.Invoke();
        }

        public void PlayFlashGlow(GameObject targetObj, Color flashColor, float duration = 0.25f)
        {
            if (targetObj == null) return;
            StartCoroutine(FlashGlowRoutine(targetObj, flashColor, duration));
        }

        private IEnumerator FlashGlowRoutine(GameObject targetObj, Color color, float duration)
        {
            var ol = targetObj.GetComponent<Outline>() ?? targetObj.AddComponent<Outline>();
            Color origColor = ol.effectColor;
            ol.effectColor = color;

            float timer = 0f;
            while (timer < duration && targetObj != null)
            {
                timer += Time.deltaTime;
                float t = Mathf.Clamp01(timer / duration);
                ol.effectColor = Color.Lerp(color, origColor, t);
                yield return null;
            }

            if (targetObj != null && ol != null)
            {
                ol.effectColor = origColor;
            }
        }

        public void PlayPhaseBanner(string title, string subtitle, bool isOpponent, Action onComplete = null)
        {
            if (_animLayer == null) return;
            BringToFront();
            StartCoroutine(PhaseBannerRoutine(title, subtitle, isOpponent, onComplete));
        }

        private IEnumerator PhaseBannerRoutine(string title, string subtitle, bool isOpponent, Action onComplete)
        {
            var banner = new GameObject("PhaseSplashBanner", typeof(RectTransform));
            banner.transform.SetParent(_animLayer, false);
            var rt = (RectTransform)banner.transform;
            rt.anchorMin = new Vector2(0.32f, 0.47f);
            rt.anchorMax = new Vector2(0.92f, 0.61f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var cg = banner.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            var bgImg = banner.AddComponent<Image>();
            bgImg.sprite = LotdUiKit.LoadSprite("LOTD/Panel/panel_background") ?? LotdUiKit.WhiteSprite;
            bgImg.color = new Color(0.02f, 0.05f, 0.12f, 0.90f);

            var ol = banner.AddComponent<Outline>();
            ol.effectColor = isOpponent ? LotdUiKit.RedAccent : LotdUiKit.CyanAccent;
            ol.effectDistance = new Vector2(2.5f, -2.5f);

            var titleRt = LotdUiKit.CreateRect("T", rt, new Vector2(0.02f, 0.38f), new Vector2(0.98f, 0.95f), Vector2.zero, Vector2.zero);
            var titleTxt = LotdUiKit.AddText(titleRt, "T", title, 26, TextAnchor.MiddleCenter,
                isOpponent ? LotdUiKit.RedAccent : LotdUiKit.GoldAccent, FontStyle.Bold);
            titleTxt.gameObject.AddComponent<Shadow>().effectColor = Color.black;

            var subRt = LotdUiKit.CreateRect("S", rt, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.42f), Vector2.zero, Vector2.zero);
            var subTxt = LotdUiKit.AddText(subRt, "S", subtitle, 15, TextAnchor.MiddleCenter,
                isOpponent ? new Color(1f, 0.65f, 0.65f, 1f) : LotdUiKit.CyanAccent, FontStyle.Bold);

            float totalDur = 0.82f;
            float punchDur = 0.15f;
            float fadeStart = 0.62f;
            float timer = 0f;

            while (timer < totalDur && banner != null)
            {
                timer += Time.deltaTime;
                float scale = timer < punchDur
                    ? Mathf.Lerp(1.28f, 1.0f, timer / punchDur)
                    : 1.0f;
                rt.localScale = new Vector3(scale, scale, 1f);

                if (timer > fadeStart)
                {
                    cg.alpha = Mathf.Clamp01(1f - (timer - fadeStart) / (totalDur - fadeStart));
                }
                yield return null;
            }

            if (banner != null) UnityEngine.Object.Destroy(banner);
            onComplete?.Invoke();
        }

        public void PlayDirectAttackSplash(Action onComplete = null)
        {
            if (_animLayer == null) return;
            BringToFront();
            StartCoroutine(DirectAttackSplashRoutine(onComplete));
        }

        private IEnumerator DirectAttackSplashRoutine(Action onComplete)
        {
            var banner = new GameObject("DirectAttackBanner", typeof(RectTransform));
            banner.transform.SetParent(_animLayer, false);
            var rt = (RectTransform)banner.transform;
            rt.anchorMin = new Vector2(0.30f, 0.44f);
            rt.anchorMax = new Vector2(0.94f, 0.63f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var cg = banner.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;

            var bgImg = banner.AddComponent<Image>();
            bgImg.sprite = LotdUiKit.LoadSprite("LOTD/Panel/panel_background") ?? LotdUiKit.WhiteSprite;
            bgImg.color = new Color(0.18f, 0.02f, 0.02f, 0.94f);

            var ol = banner.AddComponent<Outline>();
            ol.effectColor = LotdUiKit.RedAccent;
            ol.effectDistance = new Vector2(3.5f, -3.5f);

            var titleRt = LotdUiKit.CreateRect("T", rt, new Vector2(0.02f, 0.40f), new Vector2(0.98f, 0.95f), Vector2.zero, Vector2.zero);
            var titleTxt = LotdUiKit.AddText(titleRt, "T", "★ DIRECT ATTACK! ★", 30, TextAnchor.MiddleCenter,
                LotdUiKit.GoldAccent, FontStyle.Bold);
            titleTxt.gameObject.AddComponent<Shadow>().effectColor = Color.black;

            var subRt = LotdUiKit.CreateRect("S", rt, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.44f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddText(subRt, "S", "직접 공격 선언!!", 16, TextAnchor.MiddleCenter,
                LotdUiKit.RedAccent, FontStyle.Bold);

            float totalDur = 0.72f;
            float punchDur = 0.14f;
            float fadeStart = 0.50f;
            float timer = 0f;

            while (timer < totalDur && banner != null)
            {
                timer += Time.deltaTime;
                float scale = timer < punchDur
                    ? Mathf.Lerp(1.36f, 1.0f, timer / punchDur)
                    : 1.0f;
                rt.localScale = new Vector3(scale, scale, 1f);

                if (timer > fadeStart)
                {
                    cg.alpha = Mathf.Clamp01(1f - (timer - fadeStart) / (totalDur - fadeStart));
                }
                yield return null;
            }

            if (banner != null) UnityEngine.Object.Destroy(banner);
            onComplete?.Invoke();
        }

        public void PlayBoardShake(RectTransform target, float intensity = 12f, float duration = 0.22f)
        {
            if (target == null) return;
            StartCoroutine(BoardShakeRoutine(target, intensity, duration));
        }

        private IEnumerator BoardShakeRoutine(RectTransform target, float intensity, float duration)
        {
            Vector2 orig = target.anchoredPosition;
            float timer = 0f;
            while (timer < duration && target != null)
            {
                timer += Time.deltaTime;
                float decay = 1f - Mathf.Clamp01(timer / duration);
                float ox = UnityEngine.Random.Range(-intensity, intensity) * decay;
                float oy = UnityEngine.Random.Range(-intensity, intensity) * decay;
                target.anchoredPosition = orig + new Vector2(ox, oy);
                yield return null;
            }
            if (target != null) target.anchoredPosition = orig;
        }

        public void PlayDamagePopup(Vector2 screenPos, int delta)
        {
            if (_animLayer == null) return;
            BringToFront();
            StartCoroutine(DamagePopupRoutine(screenPos, delta));
        }

        private IEnumerator DamagePopupRoutine(Vector2 screenPos, int delta)
        {
            var go = new GameObject("DamagePopup", typeof(RectTransform));
            go.transform.SetParent(_animLayer, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(160f, 36f);
            rt.position = (Vector3)screenPos;

            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;

            var txt = go.AddComponent<Text>();
            txt.font = LotdUiKit.FontKR;
            txt.fontSize = 24;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text = delta < 0 ? $"{delta}" : $"+{delta}";
            txt.color = delta < 0 ? new Color(1f, 0.25f, 0.25f, 1f) : new Color(0.25f, 1f, 0.45f, 1f);

            var ol = go.AddComponent<Outline>();
            ol.effectColor = Color.black;
            ol.effectDistance = new Vector2(2f, -2f);

            float duration = 0.75f;
            float timer = 0f;
            Vector3 startPos = rt.position;

            while (timer < duration && go != null)
            {
                timer += Time.deltaTime;
                float t = Mathf.Clamp01(timer / duration);
                rt.position = startPos + new Vector3(0f, 38f * t, 0f);
                cg.alpha = Mathf.Clamp01(1f - t * t);
                yield return null;
            }

            if (go != null) UnityEngine.Object.Destroy(go);
        }

        public void PlayCoinTossSplash(IReadOnlyList<bool> coins, bool isOpponent, Action onComplete = null)
        {
            if (_animLayer == null || coins == null || coins.Count == 0) return;
            BringToFront();
            StartCoroutine(CoinTossSplashRoutine(coins, isOpponent, onComplete));
        }

        private IEnumerator CoinTossSplashRoutine(IReadOnlyList<bool> coins, bool isOpponent, Action onComplete)
        {
            var banner = new GameObject("CoinSplashBanner", typeof(RectTransform));
            banner.transform.SetParent(_animLayer, false);
            var rt = (RectTransform)banner.transform;
            rt.anchorMin = new Vector2(0.32f, 0.44f);
            rt.anchorMax = new Vector2(0.92f, 0.63f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var cg = banner.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;

            var bgImg = banner.AddComponent<Image>();
            bgImg.sprite = LotdUiKit.LoadSprite("LOTD/Panel/panel_background") ?? LotdUiKit.WhiteSprite;
            bgImg.color = new Color(0.04f, 0.08f, 0.18f, 0.94f);

            var ol = banner.AddComponent<Outline>();
            ol.effectColor = LotdUiKit.GoldAccent;
            ol.effectDistance = new Vector2(3f, -3f);

            var titleRt = LotdUiKit.CreateRect("T", rt, new Vector2(0.02f, 0.45f), new Vector2(0.98f, 0.95f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddText(titleRt, "T", "★ COIN TOSS (코인 토스) ★", 24, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

            string resultsText = string.Join("   ", coins.Select((c, idx) => c ? $"<color=#FFCC44>[앞면 / HEADS]</color>" : $"<color=#FF6666>[뒷면 / TAILS]</color>"));
            var subRt = LotdUiKit.CreateRect("S", rt, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.48f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddText(subRt, "S", resultsText, 18, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);

            float totalDur = 1.15f;
            float punchDur = 0.16f;
            float fadeStart = 0.90f;
            float timer = 0f;

            LotdAudio.PlayCoin();

            while (timer < totalDur && banner != null)
            {
                timer += Time.deltaTime;
                float scale = timer < punchDur
                    ? Mathf.Lerp(1.35f, 1.0f, timer / punchDur)
                    : 1.0f;
                rt.localScale = new Vector3(scale, scale, 1f);
                if (timer > fadeStart)
                {
                    cg.alpha = Mathf.Clamp01(1f - (timer - fadeStart) / (totalDur - fadeStart));
                }
                yield return null;
            }

            if (banner != null) UnityEngine.Object.Destroy(banner);
            onComplete?.Invoke();
        }

        public void PlayDiceRollSplash(IReadOnlyList<int> dice, bool isOpponent, Action onComplete = null)
        {
            if (_animLayer == null || dice == null || dice.Count == 0) return;
            BringToFront();
            StartCoroutine(DiceRollSplashRoutine(dice, isOpponent, onComplete));
        }

        public void PlayChainActivationPulse(uint code, Vector2 screenPos, int chainNumber)
        {
            if (_animLayer == null || screenPos == Vector2.zero) return;
            BringToFront();
            StartCoroutine(ChainActivationPulseRoutine(code, screenPos, chainNumber));
        }

        private IEnumerator ChainActivationPulseRoutine(uint code, Vector2 screenPos, int chainNumber)
        {
            var go = new GameObject("ChainPulseCard", typeof(RectTransform));
            go.transform.SetParent(_animLayer, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(96f, 138f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.position = (Vector3)screenPos;

            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            var ol = go.AddComponent<Outline>();
            ol.effectColor = LotdUiKit.GoldAccent;
            ol.effectDistance = new Vector2(3.5f, -3.5f);

            var cv = CompositeCardView.Create(rt, "CV", false);
            cv.SetCard(code, code == 0);

            var badgeRt = LotdUiKit.CreateRect("Badge", rt, new Vector2(0f, 0.72f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(badgeRt.gameObject, LotdUiKit.WhiteSprite, new Color(0.05f, 0.12f, 0.25f, 0.92f), false, false);
            LotdUiKit.AddText(badgeRt, "T", $"CHAIN {chainNumber}", 13, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

            float dur = 0.42f;
            float timer = 0f;
            while (timer < dur && go != null)
            {
                timer += Time.deltaTime;
                float t = Mathf.Clamp01(timer / dur);
                float scale = 1.0f + Mathf.Sin(t * Mathf.PI) * 0.35f;
                rt.localScale = new Vector3(scale, scale, 1f);
                cg.alpha = t < 0.65f ? 1f : Mathf.Clamp01(1f - (t - 0.65f) / 0.35f);
                yield return null;
            }

            if (go != null) UnityEngine.Object.Destroy(go);
        }

        private IEnumerator DiceRollSplashRoutine(IReadOnlyList<int> dice, bool isOpponent, Action onComplete)
        {
            var banner = new GameObject("DiceSplashBanner", typeof(RectTransform));
            banner.transform.SetParent(_animLayer, false);
            var rt = (RectTransform)banner.transform;
            rt.anchorMin = new Vector2(0.32f, 0.44f);
            rt.anchorMax = new Vector2(0.92f, 0.63f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var cg = banner.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;

            var bgImg = banner.AddComponent<Image>();
            bgImg.sprite = LotdUiKit.LoadSprite("LOTD/Panel/panel_background") ?? LotdUiKit.WhiteSprite;
            bgImg.color = new Color(0.04f, 0.08f, 0.18f, 0.94f);

            var ol = banner.AddComponent<Outline>();
            ol.effectColor = LotdUiKit.CyanAccent;
            ol.effectDistance = new Vector2(3f, -3f);

            var titleRt = LotdUiKit.CreateRect("T", rt, new Vector2(0.02f, 0.45f), new Vector2(0.98f, 0.95f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddText(titleRt, "T", "★ DICE ROLL (주사위 굴리기) ★", 24, TextAnchor.MiddleCenter, LotdUiKit.CyanAccent, FontStyle.Bold);

            string resultsText = string.Join("    ", dice.Select(d => $"<color=#FFCC44>◆ [ {d} ] ◆</color>"));
            var subRt = LotdUiKit.CreateRect("S", rt, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.48f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddText(subRt, "S", resultsText, 20, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);

            float totalDur = 1.15f;
            float punchDur = 0.16f;
            float fadeStart = 0.90f;
            float timer = 0f;

            LotdAudio.PlayDice();

            while (timer < totalDur && banner != null)
            {
                timer += Time.deltaTime;
                float scale = timer < punchDur
                    ? Mathf.Lerp(1.35f, 1.0f, timer / punchDur)
                    : 1.0f;
                rt.localScale = new Vector3(scale, scale, 1f);
                if (timer > fadeStart)
                {
                    cg.alpha = Mathf.Clamp01(1f - (timer - fadeStart) / (totalDur - fadeStart));
                }
                yield return null;
            }

            if (banner != null) UnityEngine.Object.Destroy(banner);
            onComplete?.Invoke();
        }
    }
}
