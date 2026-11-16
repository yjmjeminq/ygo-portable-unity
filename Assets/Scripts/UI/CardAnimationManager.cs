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
        SpecialSummon,
        ExtraDeckSummon,
        TokenSummon,
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
        private Sprite _summonArt;
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
            ReleaseSummonArt();
        }

        private void OnDestroy()
        {
            ReleaseSummonArt();
        }

        private void ReleaseSummonArt()
        {
            if (_summonArt == null) return;
            Destroy(_summonArt.texture);
            Destroy(_summonArt);
            _summonArt = null;
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

        internal void QueueDuelStart(Vector2 boardCenter)
        {
            _steps.Enqueue(PhaseBanner("DUEL", false, boardCenter, null, intro: true));
        }

        internal void QueueChain(CardPose source, uint code, int chainNumber, bool opponent)
        {
            _steps.Enqueue(ChainActivation(source, code, chainNumber, opponent));
        }

        internal void QueueAttackArrow(Vector2 from, Vector2 to, bool opponent)
        {
            _steps.Enqueue(AttackArrow(from, to, opponent));
        }

        internal void QueueAttack(CardPose from, Vector2 to, uint code, bool direct, bool opponent, Action onFinish)
        {
            _steps.Enqueue(Attack(from, to, code, direct, opponent, onFinish));
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
                view = CompositeCardView.Create(_layer, "MovingCard", detailed, duelArtwork: true);
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
            {
                LotdAudio.PlayDamage();
                yield return Burst(DuelEffectKind.Break, flight.From.Position, flight.From.Size * 2.1f,
                    new Color(.55f, .84f, 1f), .18f);
            }

            bool summons = motion == CardMotion.Summon || motion == CardMotion.SpecialSummon || motion == CardMotion.ExtraDeckSummon;
            if (summons && !flight.EndsFaceDown) _summonArt = DuelVisualAssets.SummonCutIn(flight.Code);
            bool reveal = motion == CardMotion.ExtraDeckSummon || _summonArt != null;
            var proxy = TakeCard(flight.StartsFaceDown ? 0u : flight.Code, flight.StartsFaceDown, reveal);
            if (reveal)
                yield return RevealSummon(proxy, flight, motion);
            else if (motion == CardMotion.TokenSummon || motion == CardMotion.Vanish)
            {
                bool appearing = motion == CardMotion.TokenSummon;
                var fade = proxy.GetComponent<CanvasGroup>() ?? proxy.gameObject.AddComponent<CanvasGroup>();
                var point = appearing ? flight.To.Position : flight.From.Position;
                var effect = CreateEffect(appearing ? DuelEffectKind.Token : DuelEffectKind.Break, point,
                    flight.To.Size * 2.4f, DuelStyle.Player);
                proxy.transform.SetAsLastSibling();
                if (appearing) LotdAudio.PlaySummon();
                yield return Animate(appearing ? .35f : .22f, t =>
                {
                    ApplyFlight(proxy, flight, t, appearing ? 10f : 6f);
                    fade.alpha = appearing ? Mathf.SmoothStep(0f, 1f, t) : 1f - t;
                    effect.Seek(t);
                });
                fade.alpha = 1f;
                Destroy(effect.gameObject);
            }
            else
            {
                float lift = motion == CardMotion.ChangePosition ? 13f : 31f;
                float time = motion == CardMotion.Transfer ? .23f : .27f;
                yield return Animate(time, t => ApplyFlight(proxy, flight, t, lift));
            }

            if (reveal)
            {
                proxy.gameObject.SetActive(false);
                Destroy(proxy.gameObject);
            }
            else RecycleCard(proxy);
            onArrive?.Invoke();

            if (summons && !reveal)
            {
                LotdAudio.PlaySummon();
                Color accent = motion == CardMotion.SpecialSummon ? DuelStyle.Gold : new Color(.58f, .87f, 1f);
                yield return Burst(DuelEffectKind.Summon, flight.To.Position, flight.To.Size * 1.9f, accent, .21f);
            }
            else if (motion == CardMotion.Set)
            {
                LotdAudio.PlayClick();
                yield return Burst(DuelEffectKind.Summon, flight.To.Position, flight.To.Size * 1.45f,
                    new Color(.54f, .73f, .90f, .64f), .14f);
            }
        }

        private IEnumerator RevealSummon(CompositeCardView proxy, CardFlight flight, CardMotion motion)
        {
            var (kind, accent, title) = SummonEffect(flight.Code, motion);
            var center = new Vector2(_layer.rect.width * .016f, _layer.rect.height * .071f);
            float height = Mathf.Min(352f, _layer.rect.height * .395f);
            var cardCenter = _summonArt == null ? center : center + Vector2.left * (height * .78f);
            var reveal = new CardPose(cardCenter, new Vector2(height * LotdUiKit.CardAspectRatio, height), -3f);
            var first = new CardFlight(flight.Code, flight.From, reveal, flight.StartsFaceDown, false);
            var background = LotdUiKit.AddImage(LotdUiKit.FillRect("SummonShade", _layer).gameObject, LotdUiKit.WhiteSprite,
                Color.clear, false, false);
            var effect = CreateEffect(kind, center, new Vector2(height * 2.55f, height * 1.53f), accent);
            Image artwork = null;
            if (_summonArt != null)
            {
                var middle = new Vector2(.5f, .5f);
                var holder = LotdUiKit.CreateRect("SummonArtwork", _layer, middle, middle, Vector2.zero, Vector2.zero);
                holder.sizeDelta = new Vector2(height * 2.12f, height * 1.36f);
                holder.anchoredPosition = center + new Vector2(height * .34f, height * .18f);
                artwork = LotdUiKit.AddImage(holder.gameObject, _summonArt, new Color(1f, 1f, 1f, 0f), true, false);
            }
            var band = Banner("SummonName", center - Vector2.up * (height * .59f),
                new Vector2(Mathf.Min(_layer.rect.width * .72f, 670f), 58f), accent);
            var label = LotdUiKit.AddFittedText(LotdUiKit.FillRect("Content", band, 4f), "Name",
                $"{title}  ·  {CardDatabase.GetName(flight.Code)}", 24, 12, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            DuelStyle.Shadow(label);
            var layout = band.gameObject.AddComponent<CanvasGroup>();
            layout.alpha = 0f;
            proxy.transform.SetAsLastSibling();

            LotdAudio.PlaySummon();
            yield return Animate(.28f, t =>
            {
                ApplyFlight(proxy, first, t, 38f);
                effect.Seek(t * .54f);
                background.color = new Color(.009f, .013f, .02f, Mathf.SmoothStep(0f, .63f, t));
                layout.alpha = Mathf.SmoothStep(0f, 1f, (t - .40f) / .60f);
                if (artwork != null)
                {
                    artwork.color = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, (t - .26f) / .65f));
                    artwork.rectTransform.localScale = Vector3.one * Mathf.Lerp(.84f, 1f, t);
                }
            });
            yield return Animate(artwork == null ? .18f : .37f, t => effect.Seek(.54f + t * .22f));

            var last = new CardFlight(flight.Code, reveal, flight.To, false, flight.EndsFaceDown);
            yield return Animate(.22f, t =>
            {
                ApplyFlight(proxy, last, t, 23f);
                effect.Seek(.76f + t * .24f);
                layout.alpha = 1f - t;
                background.color = new Color(.009f, .013f, .02f, .63f * (1f - t));
                if (artwork != null)
                    artwork.color = new Color(1f, 1f, 1f, 1f - t);
            });
            Destroy(background.gameObject);
            Destroy(effect.gameObject);
            Destroy(band.gameObject);
            if (artwork != null) Destroy(artwork.gameObject);
            ReleaseSummonArt();
        }

        internal static (DuelEffectKind kind, Color color, string label) SummonEffect(uint code, CardMotion motion)
        {
            if (motion == CardMotion.Summon)
                return (DuelEffectKind.Summon, DuelStyle.Player, "일반 소환");
            if (motion == CardMotion.SpecialSummon)
                return (DuelEffectKind.Summon, DuelStyle.Gold, "특수 소환");
            uint type = CardDatabase.Get(code)?.type ?? 0;
            if ((type & YgoConstants.TYPE_LINK) != 0)
                return (DuelEffectKind.Link, new Color(.22f, .85f, 1f), "링크 소환");
            if ((type & YgoConstants.TYPE_SYNCHRO) != 0)
                return (DuelEffectKind.Synchro, new Color(.29f, 1f, .57f), "싱크로 소환");
            if ((type & YgoConstants.TYPE_XYZ) != 0)
                return (DuelEffectKind.Xyz, new Color(.63f, .38f, 1f), "엑시즈 소환");
            if ((type & YgoConstants.TYPE_FUSION) != 0)
                return (DuelEffectKind.Fusion, new Color(.83f, .36f, 1f), "융합 소환");
            if ((type & YgoConstants.TYPE_RITUAL) != 0)
                return (DuelEffectKind.Ritual, new Color(.31f, .61f, 1f), "의식 소환");
            return (DuelEffectKind.Pendulum, new Color(.23f, .70f, 1f), "펜듈럼 소환");
        }

        private IEnumerator AttackArrow(Vector2 from, Vector2 to, bool opponent)
        {
            Vector2 direction = to - from;
            if (direction.sqrMagnitude < 1f) yield break;
            var color = opponent ? DuelStyle.Opponent : new Color(1f, .39f, .29f);
            var arrow = CreateEffect(DuelEffectKind.AttackPath, (from + to) * .5f,
                new Vector2(direction.magnitude, 58f), color);
            arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            yield return Animate(.23f, arrow.Seek);
            Destroy(arrow.gameObject);
        }

        private IEnumerator Attack(CardPose from, Vector2 to, uint code, bool direct, bool opponent, Action onFinish)
        {
            if (!from.IsValid || code == 0)
            {
                onFinish?.Invoke();
                yield break;
            }

            var proxy = TakeCard(code, false);
            var direction = (to - from.Position).normalized;
            var windup = new CardPose(from.Position - direction * 26f, from.Size * 1.10f, from.Angle);
            var first = new CardFlight(code, from, windup, false, false);
            yield return Animate(.085f, t => ApplyFlight(proxy, first, t, 5f));

            Vector2 hit = Vector2.Lerp(from.Position, to, direct ? .78f : .89f);
            var lunge = new CardPose(hit, from.Size * 1.14f, from.Angle);
            var outFlight = new CardFlight(code, windup, lunge, false, false);
            yield return Animate(.13f, t => ApplyFlight(proxy, outFlight, t, 27f));

            if (direct) LotdAudio.PlayDirectAttack();
            else LotdAudio.PlayAttack();
            Color accent = opponent ? DuelStyle.Opponent : new Color(.38f, 1f, .70f);
            var impact = CreateEffect(DuelEffectKind.Strike, to, Vector2.one * (direct ? 335f : 248f), accent);
            impact.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            var returnFlight = new CardFlight(code, lunge, from, false, false);
            yield return Animate(.27f, t =>
            {
                ApplyFlight(proxy, returnFlight, t, 10f);
                impact.Seek(t);
            });
            Destroy(impact.gameObject);
            RecycleCard(proxy);
            onFinish?.Invoke();
        }

        private IEnumerator ChainActivation(CardPose source, uint code, int chainNumber, bool opponent)
        {
            if (!source.IsValid) yield break;
            var center = new Vector2((opponent ? 1f : -1f) * _layer.rect.width * .074f, _layer.rect.height * .066f);
            float height = Mathf.Min(295f, _layer.rect.height * .38f);
            var reveal = new CardPose(center, new Vector2(height * LotdUiKit.CardAspectRatio, height), opponent ? 8f : -8f);
            var crest = CreateEffect(DuelEffectKind.Chain, center, new Vector2(height * 1.45f, height * 1.15f), DuelStyle.Gold);
            var card = TakeCard(code, code == 0, true);
            var label = Banner("ChainCount", center + Vector2.down * (height * .51f),
                new Vector2(183f, 45f), DuelStyle.Player);
            var labelText = LotdUiKit.AddText(label, "Link", $"CHAIN  {chainNumber}", 25, TextAnchor.MiddleCenter,
                Color.white, FontStyle.Bold);
            DuelStyle.Shadow(labelText);
            var opacity = label.gameObject.AddComponent<CanvasGroup>();
            LotdAudio.PlayChain();

            var lift = new CardFlight(code, source, reveal, code == 0, code == 0);
            yield return Animate(.23f, t =>
            {
                crest.Seek(t * .58f);
                opacity.alpha = Mathf.Clamp01((t - .23f) * 2.3f);
                ApplyFlight(card, lift, t, 22f);
            });
            var back = new CardFlight(code, reveal, source, code == 0, code == 0);
            yield return Animate(.20f, t =>
            {
                crest.Seek(.58f + t * .42f);
                opacity.alpha = 1f - t;
                ApplyFlight(card, back, t, 13f);
            });
            card.gameObject.SetActive(false);
            Destroy(card.gameObject);
            Destroy(crest.gameObject);
            Destroy(label.gameObject);
        }

        private IEnumerator PhaseBanner(string label, bool opponent, Vector2 center, Action onStart, bool intro = false)
        {
            onStart?.Invoke();
            LotdAudio.PlayPhaseTransition();
            Vector2 size = intro
                ? new Vector2(Mathf.Min(920f, _layer.rect.width * .76f), 112f)
                : new Vector2(Mathf.Min(530f, _layer.rect.width * .48f), 49f);
            var band = Banner(intro ? "DuelStart" : "Phase", center, size, intro ? DuelStyle.Gold : DuelStyle.Side(opponent));
            var text = LotdUiKit.AddFittedText(band, "Label", label, intro ? 75 : 25, intro ? 36 : 16,
                TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            DuelStyle.Shadow(text);
            var group = band.gameObject.AddComponent<CanvasGroup>();
            yield return Animate(intro ? .65f : .31f, t =>
            {
                group.alpha = Mathf.Min(1f, Mathf.Min(t * 7f, (1f - t) * 6f));
                band.anchoredPosition = center + Vector2.right * (Mathf.Lerp(-75f, 48f, t) * (1f - group.alpha));
                band.localScale = new Vector3(Mathf.Lerp(.85f, 1.065f, t), 1f, 1f);
            });
            Destroy(band.gameObject);
        }



        private IEnumerator LifeChange(Vector2 point, int delta, Action onStart)
        {
            onStart?.Invoke();
            if (delta == 0) yield break;
            if (delta < 0) LotdAudio.PlayDamage();

            var holder = LotdUiKit.CreateRect("LPChange", _layer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            holder.sizeDelta = new Vector2(218f, 61f);
            var text = LotdUiKit.AddFittedText(holder, "Value", delta > 0 ? $"+{delta}" : delta.ToString(),
                43, 17, TextAnchor.MiddleCenter, delta > 0 ? LotdUiKit.GreenAccent : new Color(1f, .28f, .23f), FontStyle.Bold);
            DuelStyle.Shadow(text, 2f);
            Color tint = text.color;
            yield return Animate(.46f, t =>
            {
                float vertical = point.y > _layer.rect.height * .24f ? -1f : 1f;
                holder.anchoredPosition = point + Vector2.up * vertical * (30f + 33f * t);
                holder.localScale = Vector3.one * (.94f + .26f * Mathf.Sin(Mathf.Clamp01(t * 2.4f) * Mathf.PI));
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

        private IEnumerator Burst(DuelEffectKind kind, Vector2 center, Vector2 size, Color color, float duration)
        {
            var effect = CreateEffect(kind, center, size, color);
            yield return Animate(duration, effect.Seek);
            Destroy(effect.gameObject);
        }

        private DuelEffectGraphic CreateEffect(DuelEffectKind kind, Vector2 center, Vector2 size, Color color)
        {
            var midpoint = new Vector2(.5f, .5f);
            var rect = LotdUiKit.CreateRect(kind.ToString(), _layer, midpoint, midpoint, Vector2.zero, Vector2.zero);
            rect.anchoredPosition = center;
            rect.sizeDelta = size;
            var effect = rect.gameObject.AddComponent<DuelEffectGraphic>();
            effect.texture = Texture2D.whiteTexture;
            effect.Kind = kind;
            effect.Tint = color;
            effect.raycastTarget = false;
            effect.ApplyTexture(DuelVisualAssets.Effect(kind));
            return effect;
        }

        private RectTransform Banner(string name, Vector2 center, Vector2 size, Color accent)
        {
            var middle = new Vector2(.5f, .5f);
            var band = DuelStyle.Panel(_layer, name, middle, middle, fill: new Color(.027f, .047f, .067f, .93f), border: accent);
            band.anchoredPosition = center;
            band.sizeDelta = size;
            var line = LotdUiKit.CreateRect("Accent", band, new Vector2(0f, .965f), Vector2.one, Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(line.gameObject, LotdUiKit.WhiteSprite, accent, false, false);
            return band;
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
