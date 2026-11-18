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
        PendulumSummon,
        TokenSummon,
        Set,
        ChangePosition,
        Material,
        Destroy,
        Vanish,
    }

    internal sealed class CardMoveReadoutSnapshot
    {
        internal readonly uint Code;
        internal LiveCardState Card;
        internal (Vector2 StartSize, Vector2 EndSize, uint StartPosition, bool StartOpponent, bool EndOpponent)? Movement;

        internal CardMoveReadoutSnapshot(uint code) => Code = code;
    }

    public class CardAnimationManager : MonoBehaviour
    {
        private static CardAnimationManager _instance;
        public static CardAnimationManager Instance => _instance != null ? _instance : null;

        private readonly Queue<IEnumerator> _steps = new Queue<IEnumerator>();
        private List<(CardFlight flight, Action onArrive)> _openPileTransfers;
        private readonly Stack<CompositeCardView> _cards = new Stack<CompositeCardView>();
        private readonly HashSet<(bool Opponent, uint Code)> _featuredSummons = new();
        private RectTransform _layer;
        private Sprite _summonArt;
        private uint _previousChainCode;
        private int _previousChainNumber;
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
            _instance = layer.gameObject.AddComponent<CardAnimationManager>();
            _instance._layer = layer;
            _instance.BringToFront();
            return _instance;
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
            _openPileTransfers = null;
            _cards.Clear();
            _featuredSummons.Clear();
            _previousChainCode = 0;
            _previousChainNumber = 0;
            _playing = false;
            Paused = false;
            for (int i = _layer.childCount - 1; i >= 0; i--)
                RemoveTransient(_layer.GetChild(i).gameObject);
            ReleaseSummonArt();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
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

        internal Vector2 GetAreaSize(RectTransform rect) =>
            GetPoint(rect, Vector2.one) - GetPoint(rect, Vector2.zero);

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

        internal CardPose GetVisualPose(RectTransform art)
        {
            var right = _layer.InverseTransformVector(art.TransformVector(Vector3.right));
            float angle = Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg;
            return GetPose(art, Vector2.one * .5f, Vector2.one, angle);
        }

        internal void QueueDraw(IReadOnlyList<CardFlight> flights, Action<int> onArrive)
        {
            if (flights.Count > 0) QueueStep(FlyCards(flights, onArrive, draw: true));
        }

        internal void QueueMove(CardFlight flight, CardMotion motion, Action onArrive, CardMoveReadoutSnapshot readout = null)
        {
            QueueStep(MoveCard(flight, motion, onArrive, readout));
        }

        internal void QueuePileTransfer(CardFlight flight, Action onArrive)
        {
            // Moves emitted together by one effect overlap. Other animation steps still play in engine order.
            if (_openPileTransfers == null)
            {
                _openPileTransfers = new List<(CardFlight, Action)>();
                _steps.Enqueue(PlayPileTransfers(_openPileTransfers));
            }
            _openPileTransfers.Add((flight, onArrive));
        }

        private void QueueStep(IEnumerator step)
        {
            _openPileTransfers = null;
            _steps.Enqueue(step);
        }

        internal void QueuePhase(string label, bool opponent, Vector2 boardCenter, Action onStart)
        {
            QueueStep(PhaseBanner(label, opponent, boardCenter, onStart));
        }

        internal void QueueDuelStart(Vector2 boardCenter)
        {
            QueueStep(PhaseBanner("DUEL", false, boardCenter, null, intro: true));
        }

        internal void QueueChain(CardPose source, uint code, int chainNumber, bool opponent)
        {
            QueueStep(ChainActivation(source, code, chainNumber, opponent));
        }

        internal void QueueChainResolution(Vector2 center, uint code, int chainNumber)
        {
            QueueStep(ChainResolution(center, code, chainNumber));
        }

        internal void QueueAttackArrow(Vector2 from, Vector2 to, bool opponent)
        {
            QueueStep(AttackArrow(from, to, opponent));
        }

        internal void QueueAttack(CardFlight flight, Vector2 hit, bool direct, bool opponent,
            CanvasGroup readout, DuelCardReadout attacker, Vector2 statSize, Action onFinish)
        {
            QueueStep(Attack(flight, hit, direct, opponent, readout, attacker, statSize, onFinish));
        }

        internal void QueueLifeChange(Vector2 point, int delta, Action onStart)
        {
            QueueStep(LifeChange(point, delta, onStart));
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

        private static void ApplyFlight(CompositeCardView view, CardFlight flight, float t, float lift, LiveCardState live = null)
        {
            float eased = t * t * (3f - 2f * t);
            float arc = Mathf.Sin(t * Mathf.PI);
            Vector2 point = Vector2.Lerp(flight.From.Position, flight.To.Position, eased) + Vector2.up * (arc * lift);
            Vector2 size = Vector2.Lerp(flight.From.Size, flight.To.Size, eased) * (1f + arc * 0.10f);
            float angle = Mathf.LerpAngle(flight.From.Angle, flight.To.Angle, eased);
            bool turnsOver = flight.StartsFaceDown != flight.EndsFaceDown;
            bool showBack = t < 0.5f ? flight.StartsFaceDown : flight.EndsFaceDown;
            uint visibleCode = showBack ? 0u : flight.Code;
            if (view.CurrentCode != visibleCode) view.SetCard(visibleCode, showBack, live);
            float flip = turnsOver ? Mathf.Max(0.035f, Mathf.Abs(Mathf.Cos(t * Mathf.PI))) : 1f;
            ApplyPose(view, new CardPose(point, size, angle), flip);
        }

        private IEnumerator PlayPileTransfers(List<(CardFlight flight, Action onArrive)> transfers)
        {
            if (ReferenceEquals(_openPileTransfers, transfers)) _openPileTransfers = null;
            var flights = new CardFlight[transfers.Count];
            for (int i = 0; i < flights.Length; i++) flights[i] = transfers[i].flight;
            yield return FlyCards(flights, i => transfers[i].onArrive?.Invoke(), draw: false);
        }

        private IEnumerator FlyCards(IReadOnlyList<CardFlight> flights, Action<int> onArrive, bool draw)
        {
            float travelTime = draw ? .26f : .23f;
            float stagger = draw ? .062f : .034f;
            var views = new CompositeCardView[flights.Count];
            int started = 0;
            int arrived = 0;
            float elapsed = 0f;

            while (arrived < flights.Count)
            {
                while (started < flights.Count && elapsed >= started * stagger)
                {
                    var flight = flights[started];
                    views[started] = TakeCard(flight.StartsFaceDown ? 0u : flight.Code, flight.StartsFaceDown);
                    if (draw) LotdAudio.PlayDraw();
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
                if (!Paused) elapsed += Time.unscaledDeltaTime * DuelFieldUI.PlaybackRate;
            }
        }

        private IEnumerator MoveCard(CardFlight flight, CardMotion motion, Action onArrive, CardMoveReadoutSnapshot readout)
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

            bool extraSummon = motion is CardMotion.ExtraDeckSummon or CardMotion.PendulumSummon;
            bool summons = IsSummonMotion(motion);
            bool featured = summons && TakeFeaturedSummon(flight);
            if (summons && !featured && !flight.EndsFaceDown) _summonArt = DuelVisualAssets.SummonCutIn(flight.Code);
            bool reveal = !flight.EndsFaceDown && (extraSummon || featured || _summonArt != null);
            var proxy = TakeCard(flight.StartsFaceDown ? 0u : flight.Code, flight.StartsFaceDown, reveal);
            if (reveal)
                yield return RevealSummon(proxy, flight, motion, featured, readout?.Card);
            else if (motion == CardMotion.TokenSummon || motion == CardMotion.Vanish)
            {
                bool appearing = motion == CardMotion.TokenSummon;
                if (!proxy.TryGetComponent<CanvasGroup>(out var fade))
                    fade = proxy.gameObject.AddComponent<CanvasGroup>();
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
                float lift = motion == CardMotion.ChangePosition || motion == CardMotion.Material ? 13f : 31f;
                float time = motion == CardMotion.Material ? .20f : motion == CardMotion.Transfer ? .23f : .27f;
                DuelMovingReadout moving = null;
                if (!flight.StartsFaceDown && !flight.EndsFaceDown && readout?.Movement is { } movement && readout.Card?.IsFaceUp == true)
                {
                    var stats = new DuelCardReadout(CardDatabase.Get(flight.Code), readout.Card);
                    if (stats.IsMonster)
                    {
                        var holder = CenteredVisual("MovingCardReadout", _layer, flight.From.Position, movement.StartSize);
                        moving = new DuelMovingReadout(holder, readout, stats);
                        proxy.SetCard(flight.Code, live: readout.Card);
                    }
                }
                yield return Animate(time, t =>
                {
                    ApplyFlight(proxy, flight, t, lift);
                    moving?.Follow((RectTransform)proxy.transform, t);
                });
                if (moving != null)
                {
                    RemoveTransient(moving.Root.gameObject);
                }
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

        internal static bool IsSummonMotion(CardMotion motion) =>
            motion is CardMotion.Summon or CardMotion.SpecialSummon or CardMotion.ExtraDeckSummon or CardMotion.PendulumSummon;

        internal bool TakeFeaturedSummon(CardFlight flight)
        {
            if (flight.EndsFaceDown || flight.Code == 0) return false;
            var card = CardDatabase.Get(flight.Code);
            if (card?.IsMonster != true || (card.type & YgoConstants.TYPE_TOKEN) != 0) return false;
            uint key = CardDatabase.GetArchetypeCollectionKey(card);
            bool signature = DeckRepository.PresetDecks.Exists(deck => deck.sigCard == key || deck.sigCard == flight.Code);
            bool rareBoss = (card.IsExtra || card.atk >= 2400) && CardRarityCatalog.GetRarity(flight.Code) == CardRarity.UltraRare;
            return (signature || rareBoss) && _featuredSummons.Add((flight.To.Angle > 45f, key));
        }

        private IEnumerator RevealSummon(CompositeCardView proxy, CardFlight flight, CardMotion motion, bool featured, LiveCardState live)
        {
            if (!flight.StartsFaceDown && live != null) proxy.SetCard(flight.Code, live: live);
            var (kind, accent, title) = SummonEffect(flight.Code, motion);
            Vector2 view = _layer.rect.size;
            var center = new Vector2(view.x * .035f, view.y * .045f);
            float height = Mathf.Min(352f, view.y * .395f);
            var cardCenter = featured ? center + Vector2.left * (height * .48f) : center;
            var reveal = new CardPose(cardCenter, new Vector2(height * LotdUiKit.CardAspectRatio, height), 0f);
            var first = new CardFlight(flight.Code, flight.From, reveal, flight.StartsFaceDown, false);
            var background = LotdUiKit.AddImage(LotdUiKit.FillRect("SummonShade", _layer).gameObject, LotdUiKit.WhiteSprite,
                Color.clear, false, false);
            var effect = CreateEffect(kind, center, new Vector2(view.x * .76f, view.y * .83f), accent);
            Image artwork = null;
            Image portrait = null;
            CanvasGroup portraitFade = null;
            var cardFade = proxy.gameObject.AddComponent<CanvasGroup>();
            Vector2 portraitCenter = center + new Vector2(height * .40f, height * .03f);
            if (featured)
                portraitFade = CreateFeaturedPortrait(portraitCenter, Mathf.Min(height * 1.17f, view.x * .37f), accent, out portrait);
            else if (_summonArt != null)
            {
                var holder = CenteredVisual("SummonArtwork", _layer, center, new Vector2(view.x * .79f, view.y * .89f));
                artwork = LotdUiKit.AddImage(holder.gameObject, _summonArt, new Color(1f, 1f, 1f, 0f), true, false);
            }
            proxy.transform.SetAsLastSibling();
            var layout = SummonPlate(flight, title,
                center - Vector2.up * view.y * (featured ? .29f : artwork == null ? .24f : .32f), live);

            LotdAudio.PlaySummon();
            yield return Animate(.28f, t =>
            {
                ApplySummonEntrance(proxy, first, kind, t, live);
                effect.Seek(t * .54f);
                background.color = new Color(.009f, .013f, .02f, Mathf.SmoothStep(0f, .64f, t));
                layout.alpha = Mathf.SmoothStep(0f, 1f, (t - .40f) / .60f);
                if (portraitFade != null && t >= .5f)
                {
                    portrait.sprite ??= proxy.ArtworkSprite;
                    float emerge = Mathf.SmoothStep(0f, 1f, (t - .5f) * 2f);
                    portraitFade.alpha = emerge;
                    var rect = (RectTransform)portraitFade.transform;
                    rect.anchoredPosition = portraitCenter + Vector2.right * (34f * (1f - emerge));
                    rect.localScale = Vector3.one * Mathf.Lerp(.91f, 1f, emerge);
                }
                if (artwork != null)
                {
                    float crossfade = Mathf.SmoothStep(0f, 1f, (t - .39f) / .48f);
                    artwork.color = new Color(1f, 1f, 1f, crossfade);
                    artwork.rectTransform.localScale = Vector3.one * Mathf.Lerp(.82f, 1f, crossfade);
                    cardFade.alpha = 1f - crossfade;
                }
            });
            yield return Animate(featured ? .29f : artwork == null ? .18f : .37f, t =>
            {
                effect.Seek(.54f + t * .22f);
                proxy.SetRarityHighlight(Mathf.Lerp(.85f, .25f, t));
                if (portraitFade != null) portraitFade.transform.localScale = Vector3.one * Mathf.Lerp(1f, 1.025f, t);
            });

            var last = new CardFlight(flight.Code, reveal, flight.To, false, flight.EndsFaceDown);
            yield return Animate(.22f, t =>
            {
                ApplyFlight(proxy, last, t, 23f, live);
                proxy.SetRarityHighlight(.25f * (1f - t));
                effect.Seek(.76f + t * .24f);
                layout.alpha = 1f - t;
                background.color = new Color(.009f, .013f, .02f, .64f * (1f - t));
                if (portraitFade != null) portraitFade.alpha = 1f - Mathf.SmoothStep(0f, 1f, t * 1.25f);
                if (artwork != null)
                {
                    cardFade.alpha = Mathf.SmoothStep(0f, 1f, t * 3f);
                    artwork.color = new Color(1f, 1f, 1f, 1f - t);
                }
            });
            Destroy(background.gameObject);
            Destroy(effect.gameObject);
            Destroy(layout.gameObject);
            if (artwork != null) Destroy(artwork.gameObject);
            if (portraitFade != null) Destroy(portraitFade.gameObject);
            ReleaseSummonArt();
        }

        private CanvasGroup CreateFeaturedPortrait(Vector2 center, float size, Color accent, out Image artwork)
        {
            var holder = CenteredVisual("FeaturedArtwork", _layer, center, Vector2.one * size);
            var group = holder.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            var shadow = LotdUiKit.CreateRect("Shadow", holder, Vector2.zero, Vector2.one,
                new Vector2(8f, -11f), new Vector2(8f, -11f));
            LotdUiKit.AddImage(shadow.gameObject, LotdUiKit.WhiteSprite, new Color(.008f, .013f, .034f, .81f), false, false);
            var panel = DuelStyle.Panel(holder, "Portrait", Vector2.zero, Vector2.one, fill: new Color(.045f, .065f, .10f),
                border: Color.Lerp(accent, Color.white, .31f));
            artwork = LotdUiKit.AddImage(LotdUiKit.FillRect("Artwork", panel, 7f).gameObject, null, Color.white, true, false);
            var edge = LotdUiKit.CreateRect("LightEdge", panel, Vector2.up, Vector2.one, new Vector2(0f, -3f), Vector2.zero);
            LotdUiKit.AddImage(edge.gameObject, LotdUiKit.WhiteSprite, Color.Lerp(accent, Color.white, .62f), false, false);
            return group;
        }

        internal static void ApplySummonEntrance(CompositeCardView proxy, CardFlight flight, DuelEffectKind kind, float progress,
            LiveCardState live = null)
        {
            var (lift, shift, roll) = kind switch
            {
                DuelEffectKind.Fusion => (37f, -25f, -12f),
                DuelEffectKind.Synchro => (73f, 0f, 0f),
                DuelEffectKind.Xyz => (11f, 39f, 15f),
                DuelEffectKind.Link => (19f, -52f, 6f),
                DuelEffectKind.Ritual => (48f, 0f, -4f),
                DuelEffectKind.Pendulum => (51f, 32f, -10f),
                _ => (38f, 0f, 0f),
            };
            ApplyFlight(proxy, flight, progress, lift, live);
            float swing = Mathf.Sin(progress * Mathf.PI);
            var rect = (RectTransform)proxy.transform;
            rect.anchoredPosition += Vector2.right * (shift * swing);
            rect.localRotation *= Quaternion.Euler(0f, 0f, roll * swing);
            proxy.SetRarityHighlight(Mathf.SmoothStep(0f, .85f, progress));
        }

        internal CanvasGroup SummonPlate(CardFlight flight, string summonType, Vector2 center, LiveCardState live = null)
        {
            float width = Mathf.Min(_layer.rect.width * .64f, 678f);
            var holder = CenteredVisual("SummonName", _layer, center, new Vector2(width, 65f));
            var group = holder.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            var card = new DuelCardReadout(CardDatabase.Get(flight.Code), live);
            var accent = DuelStyle.Side(flight.To.Angle > 45f);
            DuelReadoutPlate.Add(holder, new Color(.045f, .068f, .085f, .98f));
            var rail = LotdUiKit.CreateRect("Accent", holder, new Vector2(.008f, .16f), new Vector2(.008f, .88f),
                Vector2.zero, new Vector2(3f, 0f));
            LotdUiKit.AddImage(rail.gameObject, LotdUiKit.WhiteSprite, accent, false, false);
            var caption = LotdUiKit.CreateRect("Caption", holder, new Vector2(.04f, .60f), new Vector2(.96f, .96f),
                Vector2.zero, Vector2.zero);
            string rating = card.Rating;
            LotdUiKit.AddFittedText(caption, "Value", rating.Length > 0 ? $"{summonType} · {rating}" : summonType,
                16, 11, TextAnchor.MiddleLeft, accent, FontStyle.Bold);
            var nameArea = LotdUiKit.CreateRect("SummonNameText", holder, new Vector2(.04f, .05f), new Vector2(.96f, .61f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(nameArea, "Name", CardDatabase.GetName(flight.Code), 27, 13,
                TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);

            var stats = CenteredVisual("SummonStats", holder, new Vector2(width * .20f, -55f), new Vector2(width * .60f, 40f));
            new DuelStatStrip(stats, compact: false).Bind(card);
            return group;
        }

        internal static (DuelEffectKind kind, Color color, string label) SummonEffect(uint code, CardMotion motion)
        {
            if (motion == CardMotion.Summon)
                return (DuelEffectKind.Summon, DuelStyle.Player, "일반 소환");
            if (motion == CardMotion.SpecialSummon)
                return (DuelEffectKind.Summon, DuelStyle.Gold, "특수 소환");
            if (motion == CardMotion.PendulumSummon)
                return (DuelEffectKind.Pendulum, new Color(.23f, .70f, 1f), "펜듈럼 소환");
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

        internal CanvasGroup CreateBattleReadout(Rect area, BattleCardStats attacker, BattleCardStats defender,
            int viewer, bool attackerIsLink, bool defenderIsLink, bool mobileLayout = false)
        {
            if (!attacker.IsFaceUp && !defender.IsFaceUp) return null;
            bool pair = attacker.IsFaceUp && defender.IsFaceUp;
            if (!pair) area.xMax = Mathf.Lerp(area.xMin, area.xMax, .62f);
            var root = LotdUiKit.CreateRect("BattleReadout", _layer, area.min, area.max, Vector2.zero, Vector2.zero);
            if (mobileLayout) MobileSafeArea.Attach(root, area);
            var fade = root.gameObject.AddComponent<CanvasGroup>();
            fade.alpha = 0f;
            fade.blocksRaycasts = fade.interactable = false;
            DuelReadoutPlate.Add(root, new Color(.028f, .052f, .065f, .97f));

            void AddCard(string name, BattleCardStats card, bool link, string role, float start, float end)
            {
                var segment = LotdUiKit.CreateRect(name, root, new Vector2(start, 0f), new Vector2(end, 1f),
                    new Vector2(4f, 3f), new Vector2(-4f, -3f));
                bool opponent = card.Controller != viewer;
                var title = LotdUiKit.CreateRect("Role", segment, new Vector2(0f, .677f), Vector2.one,
                    Vector2.zero, Vector2.zero);
                LotdUiKit.AddFittedText(title, "Text", $"{(opponent ? "상대" : "내")} {role}", 14, 8,
                    TextAnchor.MiddleCenter, DuelStyle.Side(opponent), FontStyle.Bold);
                var values = LotdUiKit.CreateRect("Values", segment, Vector2.zero, new Vector2(1f, .66f),
                    Vector2.zero, Vector2.zero);
                new DuelStatStrip(values, compact: true).BindBattle(card, link);
            }

            if (attacker.IsFaceUp)
                AddCard("Attacker", attacker, attackerIsLink, defender.Location == 0 ? "직접 공격" : "공격", 0f, pair ? .5f : 1f);
            if (defender.IsFaceUp)
                AddCard("Defender", defender, defenderIsLink, "대상", pair ? .5f : 0f, 1f);
            return fade;
        }

        private IEnumerator Attack(CardFlight flight, Vector2 to, bool direct, bool opponent,
            CanvasGroup readout, DuelCardReadout attacker, Vector2 statSize, Action onFinish)
        {
            var from = flight.From;
            uint code = flight.Code;
            if (!from.IsValid || code == 0)
            {
                if (readout != null) RemoveTransient(readout.gameObject);
                onFinish?.Invoke();
                yield break;
            }

            var proxy = TakeCard(code, false);
            var art = (RectTransform)proxy.transform;
            RectTransform stats = null;
            if (attacker.IsMonster && statSize.x > 0f && statSize.y > 0f)
            {
                proxy.SetCard(code, live: attacker.Live);
                stats = CenteredVisual("AttackingCardReadout", _layer, from.Position, statSize);
                new DuelFieldReadout(stats).Bind(attacker, opponent, null);
            }
            if (readout != null) readout.transform.SetAsLastSibling();

            void MoveWithStats(CardFlight path, float time, float lift)
            {
                ApplyFlight(proxy, path, time, lift);
                if (stats == null) return;
                stats.anchoredPosition = art.anchoredPosition;
                stats.sizeDelta = statSize * (art.sizeDelta.y / from.Size.y);
            }

            var direction = (to - from.Position).normalized;
            var windup = new CardPose(from.Position - direction * 26f, from.Size * 1.10f, from.Angle);
            var first = new CardFlight(code, from, windup, false, false);
            yield return Animate(.085f, t =>
            {
                MoveWithStats(first, t, 5f);
                if (readout != null) readout.alpha = Mathf.SmoothStep(0f, 1f, t);
            });

            Vector2 hit = Vector2.Lerp(from.Position, to, direct ? .78f : .89f);
            var lunge = new CardPose(hit, from.Size * 1.14f, from.Angle);
            var outFlight = new CardFlight(code, windup, lunge, false, false);
            yield return Animate(.13f, t => MoveWithStats(outFlight, t, 27f));

            if (direct) LotdAudio.PlayDirectAttack();
            else LotdAudio.PlayAttack();
            Color accent = opponent ? DuelStyle.Opponent : new Color(.38f, 1f, .70f);
            var impact = CreateEffect(DuelEffectKind.Strike, to, Vector2.one * (direct ? 335f : 248f), accent);
            impact.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            if (stats != null) stats.SetAsLastSibling();
            var returnFlight = new CardFlight(code, lunge, flight.To.IsValid ? flight.To : from, false, false);
            yield return Animate(.27f, t =>
            {
                MoveWithStats(returnFlight, t, 10f);
                impact.Seek(t);
                if (readout != null) readout.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.57f, 1f, t));
            });
            if (readout != null) RemoveTransient(readout.gameObject);
            if (stats != null) RemoveTransient(stats.gameObject);
            RemoveTransient(impact.gameObject);
            RecycleCard(proxy);
            onFinish?.Invoke();
        }

        private static void RemoveTransient(GameObject visual)
        {
            visual.SetActive(false);
            if (Application.isPlaying) Destroy(visual);
            else DestroyImmediate(visual);
        }

        private IEnumerator ChainActivation(CardPose source, uint code, int chainNumber, bool opponent)
        {
            uint previous = chainNumber > 1 && _previousChainNumber == chainNumber - 1 ? _previousChainCode : 0;
            _previousChainNumber = chainNumber;
            _previousChainCode = code;
            if (!source.IsValid) yield break;

            float side = opponent ? 1f : -1f;
            Vector2 view = _layer.rect.size;
            float height = Mathf.Min(350f, view.y * .45f, view.x * .31f);
            var center = new Vector2(side * view.x * (previous == 0 ? .04f : .088f), view.y * .055f);
            var reveal = new CardPose(center, new Vector2(height * LotdUiKit.CardAspectRatio, height), side * 8f);
            var overlay = LotdUiKit.FillRect("ChainActivation", _layer);
            LotdUiKit.AddImage(overlay.gameObject, LotdUiKit.WhiteSprite, new Color(.01f, .018f, .023f, .38f), false, false);
            var opacity = overlay.gameObject.AddComponent<CanvasGroup>();
            opacity.alpha = 0f;

            if (previous != 0)
            {
                var point = new Vector2(-side * view.x * .135f, center.y + height * .26f);
                AddChainConnector(overlay, center, point, height);
                var prior = TakeCard(previous, false, true);
                prior.transform.SetParent(overlay, false);
                ApplyPose(prior, new CardPose(point, reveal.Size * .66f, -side * 10f));
                ChainBadge(overlay, point + new Vector2(-side * height * .19f, -height * .27f), chainNumber - 1,
                    height * .18f, active: false);
            }

            var crest = CreateEffect(DuelEffectKind.Chain, center, new Vector2(height * 1.40f, height * 1.25f), DuelStyle.Gold);
            var card = TakeCard(code, code == 0, true);
            var highlight = LotdUiKit.FillRect("ChainCardHighlight", card.transform, -height * .035f);
            if (DuelVisualAssets.ChainCardHighlight != null)
                LotdUiKit.AddImage(highlight.gameObject, DuelVisualAssets.ChainCardHighlight, DuelStyle.Gold, false, false);
            var flash = highlight.gameObject.AddComponent<CanvasGroup>();
            flash.alpha = 0f;
            var badge = ChainBadge(_layer, center + new Vector2(side * reveal.Size.x * .47f, -height * .43f), chainNumber,
                height * .32f, active: true);
            var badgeOpacity = badge.gameObject.AddComponent<CanvasGroup>();
            badgeOpacity.alpha = 0f;
            LotdAudio.PlayChain();

            var lift = new CardFlight(code, source, reveal, code == 0, code == 0);
            yield return Animate(.23f, t =>
            {
                crest.Seek(t * .58f);
                opacity.alpha = Mathf.Clamp01(t * 2.5f);
                flash.alpha = badgeOpacity.alpha = Mathf.SmoothStep(0f, 1f, (t - .28f) * 2.3f);
                badge.localScale = Vector3.one * Mathf.Lerp(1.45f, 1f, badgeOpacity.alpha);
                ApplyFlight(card, lift, t, 22f);
            });
            var back = new CardFlight(code, reveal, source, code == 0, code == 0);
            yield return Animate(.20f, t =>
            {
                crest.Seek(.58f + t * .42f);
                float returning = Mathf.Clamp01((t - .22f) / .78f);
                flash.alpha = badgeOpacity.alpha = opacity.alpha = 1f - returning;
                ApplyFlight(card, back, returning, 13f);
            });
            card.gameObject.SetActive(false);
            Destroy(card.gameObject);
            Destroy(crest.gameObject);
            Destroy(badge.gameObject);
            Destroy(overlay.gameObject);
        }

        private IEnumerator ChainResolution(Vector2 center, uint code, int chainNumber)
        {
            float height = Mathf.Min(206f, _layer.rect.height * .29f);
            var size = new Vector2(height * LotdUiKit.CardAspectRatio, height);
            var overlay = LotdUiKit.FillRect("ChainResolution", _layer);
            var card = TakeCard(code, code == 0, true);
            card.transform.SetParent(overlay, false);
            ApplyPose(card, new CardPose(center, size, -5f));
            var badge = ChainBadge(overlay, center + new Vector2(size.x * .49f, -height * .32f),
                chainNumber, height * .35f, active: true, resolving: true);
            var fade = overlay.gameObject.AddComponent<CanvasGroup>();
            fade.alpha = 0f;
            yield return Animate(.34f, t =>
            {
                float appear = Mathf.SmoothStep(0f, 1f, t / .24f);
                fade.alpha = appear * (1f - Mathf.SmoothStep(0f, 1f, (t - .73f) / .27f));
                badge.localScale = Vector3.one * Mathf.Lerp(1.28f, 1f, appear);
            });
            overlay.gameObject.SetActive(false);
            Destroy(overlay.gameObject);
        }

        private static void AddChainConnector(RectTransform overlay, Vector2 from, Vector2 to, float height)
        {
            Vector2 direction = to - from;
            float width = height * .065f;
            var connector = CenteredVisual("ChainConnector", overlay, (from + to) * .5f,
                new Vector2(direction.magnitude, width));
            connector.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            var ink = DuelVisualAssets.ChainLinks;
            LotdUiKit.AddImage(connector.gameObject, ink, new Color(.77f, .88f, .94f, .94f), false, false);
            if (ink == null) connector.sizeDelta = new Vector2(direction.magnitude, 2f);
        }

        private static RectTransform ChainBadge(RectTransform parent, Vector2 point, int number, float size,
            bool active, bool resolving = false)
        {
            var holder = CenteredVisual("ChainCount", parent, point, Vector2.one * size);
            var crest = resolving ? DuelVisualAssets.ChainResolving ?? DuelVisualAssets.ChainActive : DuelVisualAssets.ChainActive;
            if (crest != null)
                LotdUiKit.AddImage(holder.gameObject, crest, new Color(1f, 1f, 1f, active ? .79f : .54f), true, false);
            var label = LotdUiKit.AddFittedText(holder, "Link", number.ToString(), Mathf.RoundToInt(size * .84f), 11,
                TextAnchor.MiddleCenter, Color.white, FontStyle.BoldAndItalic);
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = resolving ? new Color(.84f, .61f, .15f, .93f) :
                active ? new Color(.13f, .87f, 1f, .93f) : new Color(.12f, .42f, .49f, .95f);
            outline.effectDistance = Vector2.one * 2f;
            return holder;
        }

        private IEnumerator PhaseBanner(string label, bool opponent, Vector2 center, Action onStart, bool intro = false)
        {
            onStart?.Invoke();
            LotdAudio.PlayPhaseTransition();
            Vector2 size = intro
                ? new Vector2(Mathf.Min(1000f, _layer.rect.width * .78f), _layer.rect.height * .35f)
                : new Vector2(Mathf.Min(580f, _layer.rect.width * .61f), 71f);
            var band = CenteredVisual(intro ? "DuelStart" : "Phase", _layer, center, size);
            var introArt = intro ? DuelVisualAssets.DuelStart : null;
            if (introArt != null)
                LotdUiKit.AddImage(band.gameObject, introArt, Color.white, true, false);
            else
            {
                if (!intro)
                    AddBandBackground(band, opponent ? DuelVisualAssets.PhaseBannerOpponent : DuelVisualAssets.PhaseBannerPlayer,
                        DuelStyle.Side(opponent));
                var text = LotdUiKit.AddFittedText(band, "Label", label, intro ? 102 : 33, intro ? 36 : 16,
                    TextAnchor.MiddleCenter, Color.white, FontStyle.BoldAndItalic);
                DuelStyle.Shadow(text, 2f);
            }
            var group = band.gameObject.AddComponent<CanvasGroup>();
            yield return Animate(intro ? .65f : .31f, t =>
            {
                group.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Min(t * 7f, (1f - t) * 6f));
                float direction = opponent ? -1f : 1f;
                float offset = Mathf.Lerp(-size.x * .29f, size.x * .22f, t) * (1f - group.alpha);
                band.anchoredPosition = intro ? center : center + Vector2.right * (offset * direction);
                float scale = Mathf.Lerp(intro ? .80f : .94f, intro ? 1.14f : 1.04f, t);
                band.localScale = intro ? Vector3.one * scale : new Vector3(scale, 1f, 1f);
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
            QueueStep(RandomResult(label, boardCenter, opponent, playSound));
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
            var rect = CenteredVisual(kind.ToString(), _layer, center, size);
            var effect = rect.gameObject.AddComponent<DuelEffectGraphic>();
            effect.texture = Texture2D.whiteTexture;
            effect.Kind = kind;
            effect.Tint = color;
            effect.raycastTarget = false;
            effect.ApplyTexture(DuelVisualAssets.Effect(kind));
            return effect;
        }

        private static RectTransform CenteredVisual(string name, Transform parent, Vector2 center, Vector2 size)
        {
            var middle = new Vector2(.5f, .5f);
            var rect = LotdUiKit.CreateRect(name, parent, middle, middle, Vector2.zero, Vector2.zero);
            rect.anchoredPosition = center;
            rect.sizeDelta = size;
            return rect;
        }

        private static void AddBandBackground(RectTransform rect, Sprite sprite, Color accent)
        {
            if (sprite != null)
            {
                LotdUiKit.AddImage(rect.gameObject, sprite, Color.white, false, false);
                return;
            }
            Color fill = Color.Lerp(new Color(.016f, .023f, .041f, .96f), accent, .18f);
            LotdUiKit.AddImage(rect.gameObject, LotdUiKit.WhiteSprite, fill, false, false);
            var line = LotdUiKit.CreateRect("Accent", rect, new Vector2(0f, .965f), Vector2.one, Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(line.gameObject, LotdUiKit.WhiteSprite, accent, false, false);
        }


        private IEnumerator Animate(float duration, Action<float> apply)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                apply(elapsed / duration);
                yield return null;
                if (!Paused) elapsed += Time.unscaledDeltaTime * DuelFieldUI.PlaybackRate;
            }
            apply(1f);
        }
    }
}
