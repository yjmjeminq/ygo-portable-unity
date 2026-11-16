using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal readonly struct CardPose
    {
        public readonly Vector2 Position;
        public readonly Vector2 Size;
        public readonly float Angle;

        public CardPose(Vector2 position, Vector2 size, float angle = 0f)
        {
            Position = position;
            Size = size;
            Angle = angle;
        }

        public bool IsValid => Size.x > 0f && Size.y > 0f;
    }

    internal readonly struct CardFlight
    {
        public readonly uint Code;
        public readonly CardPose From;
        public readonly CardPose To;
        public readonly bool StartsFaceDown;
        public readonly bool EndsFaceDown;

        public CardFlight(uint code, CardPose from, CardPose to, bool startsFaceDown, bool endsFaceDown)
        {
            Code = code;
            From = from;
            To = to;
            StartsFaceDown = startsFaceDown;
            EndsFaceDown = endsFaceDown;
        }
    }

    internal enum CardMotion
    {
        Transfer,
        Summon,
        ExtraDeckSummon,
        Set,
        ChangePosition,
        Destroy,
        Vanish,
    }

    public class CardAnimationManager : MonoBehaviour
    {
        public static CardAnimationManager Instance { get; private set; }

        private readonly Queue<IEnumerator> _steps = new Queue<IEnumerator>();
        private readonly Stack<CompositeCardView> _cards = new Stack<CompositeCardView>();
        private RectTransform _layer;
        private bool _playing;

        public bool IsPresenting => _playing || _steps.Count > 0;
        public bool Paused { get; set; }

        public static CardAnimationManager Ensure(Canvas canvas)
        {
            if (Instance != null) return Instance;

            var layer = LotdUiKit.FillRect("DuelAnimations", canvas.transform);
            var group = layer.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            Instance = layer.gameObject.AddComponent<CardAnimationManager>();
            Instance._layer = layer;
            Instance.BringToFront();
            return Instance;
        }

        public void BringToFront()
        {
            _layer.SetAsLastSibling();
            CardDragManager.Instance?.transform.SetAsLastSibling();
        }

        public void Clear()
        {
            StopAllCoroutines();
            _steps.Clear();
            _cards.Clear();
            _playing = false;
            Paused = false;
            for (int i = _layer.childCount - 1; i >= 0; i--)
            {
                var child = _layer.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        private void LateUpdate()
        {
            if (Paused || _playing || _steps.Count == 0) return;
            _playing = true;
            StartCoroutine(PlaySteps());
        }

        private IEnumerator PlaySteps()
        {
            BringToFront();
            while (_steps.Count > 0) yield return _steps.Dequeue();
            _playing = false;
        }

        internal Vector2 GetPoint(RectTransform rect, Vector2 normalizedPoint)
        {
            var local = rect.rect.min + Vector2.Scale(rect.rect.size, normalizedPoint);
            return _layer.InverseTransformPoint(rect.TransformPoint(local));
        }

        internal CardPose GetPose(RectTransform rect, Vector2 normalizedCenter, Vector2 normalizedSize,
            float angle = 0f, float scale = 1f)
        {
            Vector2 center = GetPoint(rect, normalizedCenter);
            Vector2 size = Vector2.Scale(rect.rect.size, normalizedSize);
            Vector2 width = _layer.InverseTransformVector(rect.TransformVector(Vector3.right * size.x));
            Vector2 height = _layer.InverseTransformVector(rect.TransformVector(Vector3.up * size.y));
            float cardHeight = Mathf.Min(height.magnitude, width.magnitude / LotdUiKit.CardAspectRatio) * scale;
            return new CardPose(center, new Vector2(cardHeight * LotdUiKit.CardAspectRatio, cardHeight), angle);
        }

        internal void QueueDraw(IReadOnlyList<CardFlight> flights, Action<int> onArrive)
        {
            if (flights.Count > 0) _steps.Enqueue(DrawCards(flights, onArrive));
        }

        internal void QueueMove(CardFlight flight, CardMotion motion, Action onArrive)
        {
            _steps.Enqueue(MoveCard(flight, motion, onArrive));
        }

        internal void QueuePhase(string label, bool opponent, Vector2 boardCenter, Action onStart)
        {
            _steps.Enqueue(PhaseBanner(label, opponent, boardCenter, onStart));
        }

        internal void QueueChain(CardPose source, uint code, int chainNumber)
        {
            _steps.Enqueue(ChainActivation(source, code, chainNumber));
        }

        internal void QueueAttackArrow(Vector2 from, Vector2 to)
        {
            _steps.Enqueue(AttackArrow(from, to));
        }

        internal void QueueAttack(CardPose from, Vector2 to, uint code, bool direct, Action onFinish)
        {
            _steps.Enqueue(Attack(from, to, code, direct, onFinish));
        }

        internal void QueueLifeChange(Vector2 point, int delta, Action onStart)
        {
            _steps.Enqueue(LifeChange(point, delta, onStart));
        }

        private CompositeCardView TakeCard(uint code, bool faceDown, bool detailed = false)
        {
            CompositeCardView view;
            if (detailed || !_cards.TryPop(out view))
            {
                view = CompositeCardView.Create(_layer, "MovingCard", detailed);
                var rect = (RectTransform)view.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            }
            view.SetCard(code, faceDown);
            view.gameObject.SetActive(true);
            view.transform.SetAsLastSibling();
            return view;
        }

        private void RecycleCard(CompositeCardView view)
        {
            view.gameObject.SetActive(false);
            _cards.Push(view);
        }

        private static void ApplyPose(CompositeCardView view, CardPose pose, float horizontalScale = 1f)
        {
            var rect = (RectTransform)view.transform;
            rect.sizeDelta = pose.Size;
            rect.anchoredPosition = pose.Position;
            rect.localRotation = Quaternion.Euler(0f, 0f, pose.Angle);
            rect.localScale = new Vector3(horizontalScale, 1f, 1f);
        }

        private static void ApplyFlight(CompositeCardView view, CardFlight flight, float t, float lift)
        {
            float eased = t * t * (3f - 2f * t);
            float arc = Mathf.Sin(t * Mathf.PI);
            Vector2 point = Vector2.Lerp(flight.From.Position, flight.To.Position, eased) + Vector2.up * (arc * lift);
            Vector2 size = Vector2.Lerp(flight.From.Size, flight.To.Size, eased) * (1f + arc * 0.10f);
            float angle = Mathf.LerpAngle(flight.From.Angle, flight.To.Angle, eased);
            bool turnsOver = flight.StartsFaceDown != flight.EndsFaceDown;
            bool showBack = t < 0.5f ? flight.StartsFaceDown : flight.EndsFaceDown;
            uint visibleCode = showBack ? 0u : flight.Code;
            if (view.CurrentCode != visibleCode) view.SetCard(visibleCode, showBack);
            float flip = turnsOver ? Mathf.Max(0.035f, Mathf.Abs(Mathf.Cos(t * Mathf.PI))) : 1f;
            ApplyPose(view, new CardPose(point, size, angle), flip);
        }

        private IEnumerator DrawCards(IReadOnlyList<CardFlight> flights, Action<int> onArrive)
        {
            const float travelTime = 0.26f;
            const float stagger = 0.062f;
            var views = new CompositeCardView[flights.Count];
            int started = 0;
            int arrived = 0;
            float elapsed = 0f;

            while (arrived < flights.Count)
            {
                while (started < flights.Count && elapsed >= started * stagger)
                {
                    views[started] = TakeCard(0, true);
                    LotdAudio.PlayDraw();
                    started++;
                }
                for (int i = arrived; i < started; i++)
                {
                    float t = Mathf.Clamp01((elapsed - i * stagger) / travelTime);
                    ApplyFlight(views[i], flights[i], t, 20f);
                    if (t < 1f) continue;

                    RecycleCard(views[i]);
                    onArrive(i);
                    arrived++;
                }
                yield return null;
                if (!Paused) elapsed += Time.unscaledDeltaTime;
            }
        }

        private IEnumerator MoveCard(CardFlight flight, CardMotion motion, Action onArrive)
        {
            if (!flight.From.IsValid || !flight.To.IsValid)
            {
                onArrive?.Invoke();
                yield break;
            }

            if (motion == CardMotion.Destroy)
                yield return Pulse("LOTD/Effects/card_break_flare", flight.From.Position, flight.From.Size * 1.75f,
                    new Color(0.72f, 0.91f, 1f), 0.20f);

            bool showDetails = motion == CardMotion.ExtraDeckSummon;
            var proxy = TakeCard(flight.StartsFaceDown ? 0u : flight.Code, flight.StartsFaceDown, showDetails);
            if (motion == CardMotion.Vanish)
            {
                var fade = proxy.GetComponent<CanvasGroup>() ?? proxy.gameObject.AddComponent<CanvasGroup>();
                yield return Animate(0.21f, t =>
                {
                    ApplyFlight(proxy, flight, t, 8f);
                    fade.alpha = 1f - t;
                });
                fade.alpha = 1f;
            }
            else if (motion == CardMotion.ExtraDeckSummon)
            {
                Vector2 center = Vector2.Scale(_layer.rect.size, new Vector2(0.015f, 0.045f));
                float height = Mathf.Min(316f, _layer.rect.height * 0.38f);
                var reveal = new CardPose(center, new Vector2(height * LotdUiKit.CardAspectRatio, height));
                var first = new CardFlight(flight.Code, flight.From, reveal, flight.StartsFaceDown, false);
                yield return Animate(0.25f, t => ApplyFlight(proxy, first, t, 32f));

                LotdAudio.PlaySummon();
                var mark = CreateImage("SummonMark", SummonMark(flight.Code), center, new Vector2(height * 1.4f, height * 0.68f));
                yield return Animate(0.26f, t =>
                {
                    mark.color = new Color(0.62f, 0.87f, 1f, Mathf.Sin(t * Mathf.PI) * 0.88f);
                    mark.transform.localScale = Vector3.one * Mathf.Lerp(0.72f, 1.18f, t);
                });
                Destroy(mark.gameObject);

                var last = new CardFlight(flight.Code, reveal, flight.To, false, flight.EndsFaceDown);
                yield return Animate(0.25f, t => ApplyFlight(proxy, last, t, 22f));
            }
            else
            {
                if (motion == CardMotion.Destroy) LotdAudio.PlayDamage();
                float lift = motion == CardMotion.ChangePosition ? 13f : 31f;
                float time = motion == CardMotion.Transfer ? 0.24f : 0.29f;
                yield return Animate(time, t => ApplyFlight(proxy, flight, t, lift));
            }

            if (showDetails)
            {
                proxy.gameObject.SetActive(false);
                Destroy(proxy.gameObject);
            }
            else
                RecycleCard(proxy);
            onArrive?.Invoke();

            if (motion == CardMotion.Summon || motion == CardMotion.ExtraDeckSummon)
            {
                if (motion == CardMotion.Summon) LotdAudio.PlaySummon();
                var glow = CreateImage("SummonBeam", "LOTD/Effects/summon_beam",
                    flight.To.Position + Vector2.up * (flight.To.Size.y * 0.55f),
                    new Vector2(flight.To.Size.x * 1.05f, flight.To.Size.y * 2.35f));
                glow.preserveAspect = false;
                yield return Animate(0.22f, t => glow.color = new Color(0.54f, 0.86f, 1f, (1f - t) * 0.9f));
                Destroy(glow.gameObject);
            }
            else if (motion == CardMotion.Set)
            {
                LotdAudio.PlayClick();
                yield return Pulse("LOTD/Effects/set_cloud", flight.To.Position, flight.To.Size * 1.6f,
                    new Color(0.76f, 0.89f, 1f), 0.18f);
            }
        }

        private static string SummonMark(uint code)
        {
            uint type = CardDatabase.Get(code)?.type ?? 0;
            if ((type & YgoConstants.TYPE_LINK) != 0) return "LOTD/Effects/link_ring";
            if ((type & YgoConstants.TYPE_SYNCHRO) != 0) return "LOTD/Effects/synchro_ring";
            if ((type & YgoConstants.TYPE_XYZ) != 0) return "LOTD/Effects/xyz_mark";
            return "LOTD/Effects/fusion_mark";
        }

        private IEnumerator AttackArrow(Vector2 from, Vector2 to)
        {
            var direction = to - from;
            if (direction.sqrMagnitude < 1f) yield break;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            var arrows = new Image[3];
            for (int i = 0; i < arrows.Length; i++)
            {
                arrows[i] = CreateImage("AttackArrow", "LOTD/Shared/bookmark_wide", from, new Vector2(54f, 34f));
                arrows[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
            LotdAudio.PlayClick();
            yield return Animate(0.25f, t =>
            {
                for (int i = 0; i < arrows.Length; i++)
                {
                    float progress = Mathf.Clamp01(0.1f + t * 0.72f + i * 0.13f);
                    arrows[i].rectTransform.anchoredPosition = Vector2.Lerp(from, to, progress);
                    arrows[i].color = new Color(1f, 0.88f, 0.64f, Mathf.Sin(t * Mathf.PI));
                }
            });
            foreach (var arrow in arrows) Destroy(arrow.gameObject);
        }

        private IEnumerator Attack(CardPose from, Vector2 to, uint code, bool direct, Action onFinish)
        {
            if (!from.IsValid || code == 0)
            {
                onFinish?.Invoke();
                yield break;
            }

            var proxy = TakeCard(code, false);
            Vector2 hit = Vector2.Lerp(from.Position, to, direct ? 0.65f : 0.82f);
            var lunge = new CardPose(hit, from.Size * 1.15f, from.Angle);
            var outFlight = new CardFlight(code, from, lunge, false, false);
            yield return Animate(0.19f, t => ApplyFlight(proxy, outFlight, t, 24f));

            if (direct) LotdAudio.PlayDirectAttack();
            else LotdAudio.PlayAttack();
            var flare = CreateImage("Hit", "LOTD/Effects/summon_flare", to, Vector2.one * (direct ? 235f : 166f));
            var returnFlight = new CardFlight(code, lunge, from, false, false);
            yield return Animate(0.21f, t =>
            {
                ApplyFlight(proxy, returnFlight, t, 7f);
                flare.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.45f, 1.25f, t);
                flare.color = new Color(1f, 0.72f, 0.37f, 1f - t);
            });
            Destroy(flare.gameObject);
            RecycleCard(proxy);
            onFinish?.Invoke();
        }

        private IEnumerator ChainActivation(CardPose source, uint code, int chainNumber)
        {
            if (!source.IsValid) yield break;
            var card = TakeCard(code, code == 0);
            var ring = CreateImage("Chain", "LOTD/HUD/chain", source.Position, source.Size * 1.5f);
            var label = LotdUiKit.AddText(ring.transform, "Link", chainNumber.ToString(), 24,
                TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            AddShadow(label);
            LotdAudio.PlayChain();
            yield return Animate(0.34f, t =>
            {
                float pulse = Mathf.Sin(t * Mathf.PI);
                ApplyPose(card, new CardPose(source.Position + Vector2.up * (22f * pulse),
                    source.Size * (1f + 0.27f * pulse), source.Angle));
                var color = Color.white;
                color.a = pulse;
                ring.color = color;
                label.color = color;
            });
            Destroy(ring.gameObject);
            RecycleCard(card);
        }

        private IEnumerator PhaseBanner(string label, bool opponent, Vector2 center, Action onStart)
        {
            onStart?.Invoke();
            LotdAudio.PlayPhaseTransition();
            var banner = CreateImage("Phase", "LOTD/HUD/phase_dialog_bg", center, new Vector2(455f, 53f));
            banner.preserveAspect = false;
            var text = LotdUiKit.AddText(banner.transform, "Label", label, 23, TextAnchor.MiddleCenter,
                opponent ? new Color(1f, 0.74f, 0.73f) : new Color(0.78f, 0.96f, 1f), FontStyle.Bold);
            AddShadow(text);
            var group = banner.gameObject.AddComponent<CanvasGroup>();
            yield return Animate(0.39f, t =>
            {
                group.alpha = Mathf.Clamp01(t * 8f) * Mathf.Clamp01((1f - t) * 7f);
                banner.rectTransform.anchoredPosition = center + Vector2.right * Mathf.Lerp(-48f, 35f, t);
            });
            Destroy(banner.gameObject);
        }

        private IEnumerator LifeChange(Vector2 point, int delta, Action onStart)
        {
            onStart?.Invoke();
            if (delta == 0) yield break;
            if (delta < 0) LotdAudio.PlayDamage();

            var holder = LotdUiKit.CreateRect("LPChange", _layer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            holder.sizeDelta = new Vector2(180f, 48f);
            var text = LotdUiKit.AddText(holder, "Value", delta > 0 ? $"+{delta}" : delta.ToString(),
                30, TextAnchor.MiddleCenter, delta > 0 ? LotdUiKit.GreenAccent : new Color(1f, 0.29f, 0.2f), FontStyle.Bold);
            AddShadow(text);
            Color tint = text.color;
            yield return Animate(0.42f, t =>
            {
                holder.anchoredPosition = point + Vector2.up * (20f + 30f * t);
                tint.a = Mathf.Clamp01((1f - t) * 3.8f);
                text.color = tint;
            });
            Destroy(holder.gameObject);
        }

        internal void QueueRandomResult(string label, Vector2 boardCenter, bool opponent, Action playSound)
        {
            _steps.Enqueue(RandomResult(label, boardCenter, opponent, playSound));
        }

        private IEnumerator RandomResult(string label, Vector2 center, bool opponent, Action playSound)
        {
            playSound?.Invoke();
            yield return PhaseBanner(label, opponent, center, null);
        }

        private IEnumerator Pulse(string path, Vector2 center, Vector2 size, Color color, float duration)
        {
            var image = CreateImage("Pulse", path, center, size);
            yield return Animate(duration, t =>
            {
                image.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.65f, 1.2f, t);
                color.a = Mathf.Sin(t * Mathf.PI);
                image.color = color;
            });
            Destroy(image.gameObject);
        }

        private Image CreateImage(string name, string spritePath, Vector2 point, Vector2 size)
        {
            var rect = LotdUiKit.CreateRect(name, _layer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            rect.anchoredPosition = point;
            rect.sizeDelta = size;
            return LotdUiKit.AddImage(rect.gameObject, LotdUiKit.LoadSprite(spritePath), Color.white, true, false);
        }

        private static void AddShadow(Text label)
        {
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1.25f, -1.25f);
        }

        private IEnumerator Animate(float duration, Action<float> apply)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                apply(elapsed / duration);
                yield return null;
                if (!Paused) elapsed += Time.unscaledDeltaTime;
            }
            apply(1f);
        }
    }
}
