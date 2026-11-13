using System;
using System.Collections;
using System.Collections.Generic;
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

        public static CardAnimationManager Ensure(Canvas canvas)
        {
            if (Instance != null) return Instance;
            var go = new GameObject("CardAnimationManager", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Instance = go.AddComponent<CardAnimationManager>();
            Instance.Initialize(rt);
            return Instance;
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
            rt.sizeDelta = new Vector2(90f, 130f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            var ol = go.AddComponent<Outline>();
            ol.effectColor = LotdUiKit.CyanAccent;
            ol.effectDistance = new Vector2(2f, -2f);

            CompositeCardView.Create(rt, "CV", false);
            return go;
        }

        private void RecycleProxy(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            _proxyPool.Enqueue(go);
        }

        public void PlayCardFlight(uint code, Vector2 startScreenPos, Vector2 endScreenPos,
            float duration = 0.32f, bool faceDown = false, float arcHeight = 45f,
            Action onComplete = null)
        {
            if (startScreenPos == Vector2.zero || endScreenPos == Vector2.zero)
            {
                onComplete?.Invoke();
                return;
            }

            StartCoroutine(CardFlightRoutine(code, startScreenPos, endScreenPos, duration, faceDown, arcHeight, onComplete));
        }

        private IEnumerator CardFlightRoutine(uint code, Vector2 startPos, Vector2 endPos,
            float duration, bool faceDown, float arcHeight, Action onComplete)
        {
            var proxy = GetOrCreateProxy();
            var rt = (RectTransform)proxy.transform;
            var cv = proxy.GetComponentInChildren<CompositeCardView>();
            if (cv != null) cv.SetCard(code, faceDown);

            float timer = 0f;
            duration = Mathf.Max(0.12f, duration);

            while (timer < duration)
            {
                timer += Time.deltaTime;
                float t = Mathf.Clamp01(timer / duration);
                float ease = Mathf.SmoothStep(0f, 1f, t);

                Vector2 linear = Vector2.Lerp(startPos, endPos, ease);
                float arc = Mathf.Sin(t * Mathf.PI) * arcHeight;
                rt.position = new Vector3(linear.x, linear.y + arc, 0f);

                float scalePunch = 1.0f + Mathf.Sin(t * Mathf.PI) * 0.16f;
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
    }
}
