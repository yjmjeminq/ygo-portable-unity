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
        private readonly Func<CardPose> _readLayout;

        public CardPose(Vector2 position, Vector2 size, float angle = 0f, Func<CardPose> readLayout = null)
        {
            Position = position;
            Size = size;
            Angle = angle;
            _readLayout = readLayout;
        }

        public bool IsValid => Size.x > 0f && Size.y > 0f;

        internal CardPose FollowLayout(Func<CardPose> readLayout) => new(Position, Size, Angle, readLayout);

        internal CardPose AtLayout()
        {
            if (_readLayout == null) return this;
            var current = _readLayout();
            return current.IsValid ? current : this;
        }

        internal CardPose Shift(Vector2 offset, float scale)
        {
            var original = this;
            CardPose Sample()
            {
                var current = original.AtLayout();
                return new CardPose(current.Position + offset, current.Size * scale, current.Angle);
            }
            var initial = Sample();
            return _readLayout == null ? initial : initial.FollowLayout(Sample);
        }
    }

    internal readonly struct CardFlight
    {
        public readonly uint Code;
        public readonly CardPose From;
        public readonly CardPose To;
        public readonly bool StartsFaceDown;
        public readonly bool EndsFaceDown;
        public readonly DeckCardAppearance? Appearance;

        public CardFlight(uint code, CardPose from, CardPose to, bool startsFaceDown, bool endsFaceDown,
            DeckCardAppearance? appearance = null)
        {
            Code = code;
            From = from;
            To = to;
            StartsFaceDown = startsFaceDown;
            EndsFaceDown = endsFaceDown;
            Appearance = appearance;
        }

        internal CardFlight AtLayout() => new(Code, From.AtLayout(), To.AtLayout(), StartsFaceDown, EndsFaceDown, Appearance);
    }

    internal enum CardMotion
    {
        Transfer,
        Summon,
        SpecialSummon,
        ExtraDeckSummon,
        PendulumSummon,
        TokenSummon,
        FieldReturn,
        Set,
        ChangePosition,
        AttachMaterial,
        DetachMaterial,
        Destroy,
        Vanish,
    }

    internal sealed class CardMoveReadoutSnapshot
    {
        internal readonly uint Code;
        internal readonly LiveCardState SourceCard;
        private readonly Func<(Vector2 StartSize, Vector2 EndSize, bool StartOpponent, bool EndOpponent)> _readMovement;
        internal (Vector2 StartSize, Vector2 EndSize, bool StartOpponent, bool EndOpponent) Movement => _readMovement();
        internal LiveCardState DestinationCard;

        internal CardMoveReadoutSnapshot(uint code, LiveCardState sourceCard,
            Func<(Vector2 StartSize, Vector2 EndSize, bool StartOpponent, bool EndOpponent)> readMovement)
        {
            Code = code;
            SourceCard = sourceCard;
            _readMovement = readMovement;
        }
    }

    internal readonly struct SummonMaterialFlight
    {
        internal readonly CardFlight Flight;
        internal readonly CardMoveReadoutSnapshot Readout;
        internal readonly Action Depart;
        internal readonly Action Release;
        internal readonly bool Vanishes;

        internal SummonMaterialFlight(CardFlight flight, CardMoveReadoutSnapshot readout, Action depart, Action release, bool vanishes = false)
        {
            Flight = flight;
            Readout = readout;
            Depart = depart;
            Release = release;
            Vanishes = vanishes;
        }

        internal SummonMaterialFlight AimAt(CardPose to) =>
            new(new CardFlight(Flight.Code, Flight.From, to, Flight.StartsFaceDown, Flight.EndsFaceDown, Flight.Appearance),
                Readout, Depart, Release, Vanishes);
    }

    public partial class CardAnimationManager : MonoBehaviour
    {
        private static CardAnimationManager _instance;
        public static CardAnimationManager Instance => _instance != null ? _instance : null;

        private readonly Queue<IEnumerator> _steps = new Queue<IEnumerator>();
        private List<(CardFlight flight, Action onArrive)> _openPileTransfers;
        private List<LifeChangeCue> _openBattleLifeChanges;
        private FieldDepartureGroup _openFieldDepartures;
        private MaterialDepartureGroup _openMaterials;
        private PositionChangeGroup _openPositionChanges;
        private EquipCue _openEquip;
        private List<FieldArrival> _pendingFieldArrivals;
        private uint? _pendingReturnEffect;
        private Action _refreshAfterFieldArrivals;
        private readonly Stack<CompositeCardView> _cards = new Stack<CompositeCardView>();
        private readonly HashSet<(bool Opponent, uint Code)> _featuredSummons = new();
        private RectTransform _layer;
        private uint _previousChainCode;
        private int _previousChainNumber;
        private LiveCardState _previousChainReadout;
        private DeckCardAppearance? _previousChainAppearance;
        private bool _previousChainOpponent;
        private readonly HashSet<ChainLinkInfo> _shownChainOutcomes = new();
        private Action<float> _activeLandingImpact;
        private bool _playing;

        private const float LifeFeedbackDuration = .46f;
        private const float FieldSwapHandoff = .80f;
        private const float PositionHandoff = .5f;
        private const float FieldReturnHandoff = .62f;
        private static readonly Color LifeDamageTint = new Color(1f, .61f, .52f);
        private static readonly Color DestructionTint = new Color(.55f, .84f, 1f);
        private static readonly Color BanishTint = new Color(.65f, .53f, 1f);

        private sealed class LifeChangeCue
        {
            internal RectTransform Anchor;
            internal int Delta;
            internal LifePointChangeKind Kind;
            internal Action Apply;
            internal Action<float> Impact;
            internal CanvasGroup Feedback;
            internal DuelEffectGraphic Sparks;
        }

        private sealed class EquipCue
        {
            internal RectTransform Source;
            internal RectTransform Target;
            internal uint TargetCode;
            internal Action OnContact;
        }

        private sealed class FieldArrival
        {
            internal (byte Controller, uint Location, uint Sequence) Slot;
            internal CardFlight Flight;
            internal CardMotion Motion;
            internal CardMoveReadoutSnapshot Readout;
            internal Action Depart;
            internal Action Handoff;
            internal Action Release;
            internal Action<float> LandingImpact;
            internal int SignatureUnits;
        }

        private sealed class FieldDepartureGroup
        {
            internal object Key;
            internal Action Refresh;
            internal bool Temporary;
            internal readonly List<(CardFlight Flight, CardMotion Motion, CardMoveReadoutSnapshot Readout,
                Action Depart, Action Release)> Moves = new();
        }

        private sealed class MaterialDepartureGroup
        {
            internal object Key;
            internal uint Method;
            internal uint LinkMarkers;
            internal readonly List<SummonMaterialFlight> Moves = new();
        }

        private sealed class PositionChangeGroup
        {
            internal object Key;
            internal Action Refresh;
            internal readonly List<(CardFlight Flight, CardMoveReadoutSnapshot Readout, Action Depart,
                Action Handoff, Action Release)> Moves = new();
        }

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
            ResetLandingImpact();
            _steps.Clear();
            _openTargetLocks = null;
            _openPileTransfers = null;
            _openBattleLifeChanges = null;
            _openFieldDepartures = null;
            _openMaterials = null;
            _openPositionChanges = null;
            _openEquip = null;
            _pendingFieldArrivals = null;
            _pendingReturnEffect = null;
            _refreshAfterFieldArrivals = null;
            _cards.Clear();
            _featuredSummons.Clear();
            _previousChainCode = 0;
            _previousChainNumber = 0;
            _previousChainReadout = null;
            _previousChainAppearance = null;
            _shownChainOutcomes.Clear();
            _playing = false;
            Paused = false;
            _layer.anchoredPosition = Vector2.zero;
            for (int i = _layer.childCount - 1; i >= 0; i--)
                RemoveTransient(_layer.GetChild(i).gameObject);
        }

        private void OnDestroy()
        {
            ResetLandingImpact();
            if (ReferenceEquals(_instance, this)) _instance = null;
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

        internal void QueueMove(CardFlight flight, CardMotion motion, Action onArrive, CardMoveReadoutSnapshot readout = null,
            Action onDepart = null, Action<float> landingImpact = null)
        {
            QueueStep(MoveCard(flight, motion, onArrive, readout, onDepart, landingImpact: landingImpact));
        }

        internal void QueueFieldDeparture(object group, CardFlight flight, CardMotion motion,
            CardMoveReadoutSnapshot readout, Action depart, Action release, Action refresh, bool temporary = false)
        {
            if (group == null || _openFieldDepartures?.Key != group)
            {
                var next = new FieldDepartureGroup { Key = group, Refresh = refresh, Temporary = temporary };
                QueueStep(PlayFieldDepartures(next));
                _openFieldDepartures = next;
            }
            _openFieldDepartures.Moves.Add((flight, motion, readout, depart, release));
        }

        internal void QueueFieldSwap((CardFlight Flight, CardMoveReadoutSnapshot Readout) first,
            (CardFlight Flight, CardMoveReadoutSnapshot Readout) second, Action depart, Action handoff, Action arrive)
        {
            QueueStep(PlayFieldSwap(first, second, depart, handoff, arrive));
        }

        internal void QueueMaterialDeparture(object group, SummonMaterialFlight move, uint method, Action refresh)
        {
            if (group == null || _openMaterials?.Key != group)
            {
                var next = new MaterialDepartureGroup { Key = group, Method = method };
                QueueStep(PlaySummonMaterials(next.Moves, method, refresh, merging: false, group: next));
                _openMaterials = next;
            }
            _openMaterials.Moves.Add(move);
        }

        internal void QueuePositionChange(object group, CardFlight flight, CardMoveReadoutSnapshot readout,
            Action depart, Action handoff, Action release, Action refresh)
        {
            if (group == null)
            {
                QueueStep(MoveCard(flight, CardMotion.ChangePosition, () => { release(); refresh(); },
                    readout, () => { depart(); refresh(); }, () => { handoff(); refresh(); }));
                return;
            }
            if (_openPositionChanges?.Key != group)
            {
                var next = new PositionChangeGroup { Key = group, Refresh = refresh };
                QueueStep(PlayPositionChanges(next));
                _openPositionChanges = next;
            }
            _openPositionChanges.Moves.Add((flight, readout, depart, handoff, release));
        }

        internal void StageFieldArrival(CardFlight flight, CardMotion motion, byte controller, uint location, uint sequence,
            CardMoveReadoutSnapshot readout, Action depart, Action release, Action refresh, uint? returnEffect = null,
            Action handoff = null, int xyzMaterials = 0, Action<float> landingImpact = null)
        {
            if (_pendingFieldArrivals != null && (_pendingReturnEffect != returnEffect || returnEffect == 0))
                CompleteFieldArrivals();
            _openTargetLocks = null;
            _openFieldDepartures = null;
            var kind = SummonEffect(flight.Code, motion).kind;
            int signatureUnits = kind == DuelEffectKind.Xyz ? xyzMaterials : 0;
            if (_openMaterials != null && !flight.EndsFaceDown)
            {
                if (_openMaterials.Method == YgoConstants.REASON_LINK && kind == DuelEffectKind.Link)
                    _openMaterials.LinkMarkers = SummonedLinkMarkers(flight.Code, readout?.DestinationCard);
                if (_openMaterials.Method == YgoConstants.REASON_SYNCHRO && kind == DuelEffectKind.Synchro)
                    signatureUnits = SummonTunerLevels(_openMaterials.Moves);
            }
            _openMaterials = null;
            _openPositionChanges = null;
            _openPileTransfers = null;
            _openBattleLifeChanges = null;
            _pendingFieldArrivals ??= new List<FieldArrival>();
            _pendingFieldArrivals.Add(new FieldArrival
            {
                Slot = (controller, location, sequence), Flight = flight, Motion = motion, Readout = readout,
                Depart = depart, Handoff = handoff, Release = release, SignatureUnits = signatureUnits, LandingImpact = landingImpact,
            });
            _pendingReturnEffect = returnEffect;
            _refreshAfterFieldArrivals = refresh;
        }

        internal bool IsFieldArrivalReserved(byte controller, uint location, uint sequence) =>
            _pendingFieldArrivals?.Exists(move => move.Slot == (controller, location, sequence)) == true;

        internal void CompleteFieldArrivals()
        {
            var moves = _pendingFieldArrivals;
            if (moves == null) return;
            var refresh = _refreshAfterFieldArrivals;
            _pendingFieldArrivals = null;
            _pendingReturnEffect = null;
            _refreshAfterFieldArrivals = null;

            void Land()
            {
                foreach (var move in moves) move.Release();
                refresh();
            }

            var first = moves[0];
            _openEquip = null;
            _steps.Enqueue(moves.Count == 1 && first.Motion != CardMotion.FieldReturn
                ? MoveCard(first.Flight, first.Motion, Land, first.Readout, first.Depart, signatureUnits: first.SignatureUnits,
                    landingImpact: first.LandingImpact)
                : PlayFieldArrivalGroup(moves, Land, refresh));
        }

        internal void QueueXyzMaterials(IReadOnlyList<SummonMaterialFlight> moves, Action refresh)
        {
            if (moves.Count != 0) QueueStep(PlaySummonMaterials(moves, YgoConstants.REASON_XYZ, refresh, merging: true));
        }

        internal void QueuePileTransfer(CardFlight flight, Action onArrive)
        {
            // Moves emitted together by one effect overlap. Other animation steps still play in engine order.
            CompleteFieldArrivals();
            _openTargetLocks = null;
            _openFieldDepartures = null;
            _openMaterials = null;
            _openPositionChanges = null;
            _openBattleLifeChanges = null;
            _openEquip = null;
            if (_openPileTransfers == null)
            {
                _openPileTransfers = new List<(CardFlight, Action)>();
                _steps.Enqueue(PlayPileTransfers(_openPileTransfers));
            }
            _openPileTransfers.Add((flight, onArrive));
        }

        private void QueueStep(IEnumerator step)
        {
            CompleteFieldArrivals();
            _openTargetLocks = null;
            _openFieldDepartures = null;
            _openMaterials = null;
            _openPositionChanges = null;
            _openPileTransfers = null;
            _openBattleLifeChanges = null;
            _openEquip = null;
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

        internal void QueueDestinyDraw(bool opponent, Vector2 center) => QueueStep(PlayDestinyDraw(opponent, center));

        private IEnumerator PlayDestinyDraw(bool opponent, Vector2 center)
        {
            var shade = SummonScreenTint("DestinyDrawTint");
            Vector2 area = _layer.rect.size;
            var wave = CreateEffect(DuelEffectKind.ChainImpactWave, center, new Vector2(area.x * .71f, area.y * .43f),
                DuelStyle.ChainGold);
            var band = CenteredVisual("DestinyDraw", _layer, center,
                new Vector2(Mathf.Min(660f, area.x * .66f), Mathf.Min(132f, area.y * .15f)));
            LotdUiKit.AddImage(band.gameObject, LotdUiKit.WhiteSprite, new Color(.36f, .022f, .052f, .94f), false, false);
            LotdUiKit.AddFittedText(band, "Label", opponent ? "상대의 데스티니 드로우" : "데스티니 드로우",
                47, 20, TextAnchor.MiddleCenter, DuelStyle.ChainGold, FontStyle.Bold);
            var opacity = band.gameObject.AddComponent<CanvasGroup>();
            LotdAudio.PlayChain();
            yield return Animate(.65f, t =>
            {
                float appearance = Mathf.Clamp01(Mathf.Min(t * 8f, (1f - t) * 6f));
                shade.color = new Color(.63f, .055f, .095f, appearance * .24f);
                opacity.alpha = appearance;
                band.localScale = Vector3.one * Mathf.Lerp(.92f, 1.08f, t);
                wave.Seek(t);
            });
            RemoveTransient(wave.gameObject);
            RemoveTransient(band.gameObject);
            RemoveTransient(shade.gameObject);
        }

        internal void QueueChain(CardPose source, uint code, int chainNumber, bool opponent, LiveCardState live = null,
            Action onStart = null, DeckCardAppearance? appearance = null)
        {
            QueueStep(ChainActivation(source, code, chainNumber, opponent, live, onStart, appearance));
        }

        internal const float CompactChainReadoutHeightRatio = 1.25f;

        internal void QueueChainResolution(Func<(Vector2 Center, float MaxHeight)> readLayout, ChainLinkInfo link, bool compact = false)
        {
            QueueStep(ChainResolution(readLayout, link, compact));
        }

        internal void QueueChainOutcome(Func<(Vector2 Center, float MaxHeight)> readLayout, ChainLinkInfo link,
            Func<Vector2?> readGrave)
        {
            QueueStep(ChainOutcome(readLayout, link, readGrave));
        }

        internal void QueueFieldRays(RectTransform source, IReadOnlyList<RectTransform> targets, DuelEffectKind kind, Color tint)
        {
            if (targets.Count != 0) QueueStep(FieldRays(source, targets, kind, tint));
        }

        private IEnumerator FieldRays(RectTransform source, IReadOnlyList<RectTransform> targets, DuelEffectKind kind, Color tint)
        {
            bool column = kind == DuelEffectKind.ColumnRay;
            var rays = new DuelEffectGraphic[targets.Count];
            for (int i = 0; i < rays.Length; i++)
                rays[i] = CreateEffect(kind, Vector2.zero, Vector2.one, tint);
            yield return Animate(column ? .44f : .29f, t =>
            {
                var start = GetPoint(source, new Vector2(.5f, column ? .93f : .5f));
                for (int i = 0; i < rays.Length; i++)
                {
                    var end = GetPoint(targets[i], new Vector2(.5f, column ? .07f : .5f));
                    var direction = end - start;
                    var ray = rays[i].rectTransform;
                    ray.anchoredPosition = (start + end) * .5f;
                    ray.sizeDelta = new Vector2(direction.magnitude, column ? 47f : 35f);
                    ray.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                    rays[i].Seek(t);
                }
            });
            foreach (var ray in rays) RemoveTransient(ray.gameObject);
        }

        internal void QueueFieldReadoutUpdate(Action apply) => QueueStep(ApplyFieldReadoutUpdate(apply));

        internal void QueueEquip(RectTransform source, RectTransform target, uint targetCode, Action onContact)
        {
            var cue = new EquipCue { Source = source, Target = target, TargetCode = targetCode, OnContact = onContact };
            QueueStep(PlayEquip(cue));
            if (onContact == null) _openEquip = cue;
        }

        internal bool TryQueueEquipReadout(RectTransform target, uint code, Action onContact)
        {
            // Some effects register the stat change just after the equip packet.
            if (_openEquip == null || _pendingFieldArrivals != null || target != _openEquip.Target || code != _openEquip.TargetCode)
                return false;
            _openEquip.OnContact = onContact;
            _openEquip = null;
            return true;
        }

        private static IEnumerator ApplyFieldReadoutUpdate(Action apply)
        {
            apply();
            yield break;
        }

        internal void QueueAttackArrow(CardPose source, Func<Vector2> readTarget, bool opponent)
        {
            QueueStep(AttackArrow(source, readTarget, opponent));
        }

        internal void QueueAttack(CardFlight flight, Vector2 hit, bool direct, bool opponent,
            CanvasGroup readout, DuelCardReadout attacker, Vector2 statSize, Action onFinish,
            Func<(CardPose Pose, Vector2 ReadoutSize, CardPose Defender, RectTransform DefenderReadout)> onStart = null,
            Func<(Vector2 Hit, Vector2 AttackerArea, Vector2 DefenderArea)> readLayout = null)
        {
            var lifeChanges = new List<LifeChangeCue>(2);
            QueueStep(Attack(flight, hit, direct, opponent, readout, attacker, statSize, onFinish, onStart, lifeChanges, readLayout));
            _openBattleLifeChanges = lifeChanges;
        }

        internal void QueueLifeChange(RectTransform anchor, int delta, LifePointChangeKind kind, Action apply,
            Action<float> impact = null)
        {
            var change = new LifeChangeCue { Anchor = anchor, Delta = delta, Kind = kind, Apply = apply, Impact = impact };
            // Collect only consecutive, confirmed battle damage. A queued effect or a later step keeps its own timing.
            if (kind == LifePointChangeKind.BattleDamage && _openBattleLifeChanges != null &&
                !_openBattleLifeChanges.Exists(pending => pending.Anchor == anchor))
                _openBattleLifeChanges.Add(change);
            else QueueStep(PlayLifeChanges(new[] { change }));
        }

        private CompositeCardView TakeCard(uint code, bool faceDown, bool detailed = false, LiveCardState live = null,
            DeckCardAppearance? appearance = null)
        {
            CompositeCardView view;
            if (detailed || !_cards.TryPop(out view))
            {
                view = CompositeCardView.Create(_layer, "MovingCard", detailed, cinematicOverframe: true);
                var rect = (RectTransform)view.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            }
            view.SetDeckCard(code, appearance, faceDown, live);
            if (view.TryGetComponent<CanvasGroup>(out var opacity)) opacity.alpha = 1f;
            view.gameObject.SetActive(true);
            view.transform.SetAsLastSibling();
            return view;
        }

        private CompositeCardView PrepareFlightCard(CardFlight flight, bool detailed = false)
        {
            var view = TakeCard(flight.StartsFaceDown ? 0u : flight.Code, flight.StartsFaceDown, detailed,
                appearance: flight.Appearance);
            ApplyFlight(view, flight, 0f, 0f);
            return view;
        }

        private void RecycleCard(CompositeCardView view)
        {
            view.gameObject.SetActive(false);
            _cards.Push(view);
        }

        private static void ApplyPose(CompositeCardView view, CardPose pose, float horizontalScale = 1f, float layoutHeight = 0f)
        {
            var rect = (RectTransform)view.transform;
            float scale = 1f;
            if (layoutHeight > 0f)
            {
                // Scale the fitted face without rebuilding the artwork and outline meshes every frame.
                var layoutSize = new Vector2(layoutHeight * LotdUiKit.CardAspectRatio, layoutHeight);
                if (rect.sizeDelta != layoutSize) rect.sizeDelta = layoutSize;
                scale = Mathf.Min(pose.Size.x / layoutSize.x, pose.Size.y / layoutSize.y);
            }
            else rect.sizeDelta = pose.Size;
            rect.anchoredPosition = pose.Position;
            rect.localRotation = Quaternion.Euler(0f, 0f, pose.Angle);
            rect.localScale = new Vector3(horizontalScale * scale, scale, 1f);
        }

        private static void ApplyFlight(CompositeCardView view, CardFlight flight, float t, float lift,
            LiveCardState live = null, Vector2 lateral = default, float layoutHeight = 0f)
        {
            var from = flight.From.AtLayout();
            var to = flight.To.AtLayout();
            float eased = t * t * (3f - 2f * t);
            float arc = Mathf.Sin(t * Mathf.PI);
            Vector2 point = Vector2.Lerp(from.Position, to.Position, eased) + arc * (Vector2.up * lift + lateral);
            Vector2 size = Vector2.Lerp(from.Size, to.Size, eased) * (1f + arc * 0.10f);
            float angle = Mathf.LerpAngle(from.Angle, to.Angle, eased);
            bool turnsOver = flight.StartsFaceDown != flight.EndsFaceDown;
            bool showBack = t < 0.5f ? flight.StartsFaceDown : flight.EndsFaceDown;
            uint visibleCode = showBack ? 0u : flight.Code;
            if (view.CurrentCode != visibleCode) view.SetDeckCard(visibleCode, flight.Appearance, showBack, live);
            float flip = turnsOver ? Mathf.Max(0.035f, Mathf.Abs(Mathf.Cos(t * Mathf.PI))) : 1f;
            ApplyPose(view, new CardPose(point, size, angle), flip, layoutHeight);
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
                    views[started] = PrepareFlightCard(flight);
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

        private IEnumerator PlayFieldArrivalGroup(IReadOnlyList<FieldArrival> moves, Action land, Action refresh)
        {
            bool returning = moves[0].Motion == CardMotion.FieldReturn;
            var showcase = returning ? null : GroupSummonPoses(moves);
            foreach (var move in moves) move.Depart?.Invoke();
            if (returning) refresh();
            var effects = new DuelEffectGraphic[moves.Count];
            var views = new CompositeCardView[moves.Count];
            var opacity = new CanvasGroup[moves.Count];
            var readouts = new DuelMovingReadout[moves.Count];
            for (int i = 0; i < moves.Count; i++)
            {
                var move = moves[i];
                var (kind, tint, _) = returning ? (DuelEffectKind.BanishTransit, BanishTint, "") : SummonEffect(move.Flight.Code, move.Motion);
                effects[i] = CreateEffect(kind, move.Flight.To, 1.65f, tint);
                effects[i].Seek(0f);
            }

            for (int i = 0; i < moves.Count; i++)
            {
                var flight = moves[i].Flight;
                bool showAce = showcase != null && showcase[i].IsValid;
                var view = PrepareFlightCard(flight, detailed: showAce);
                if (!view.TryGetComponent<CanvasGroup>(out var fade))
                    fade = view.gameObject.AddComponent<CanvasGroup>();
                fade.alpha = showcase != null && moves[i].Motion != CardMotion.TokenSummon ? 1f : 0f;
                views[i] = view;
                opacity[i] = fade;
                if (showcase != null) ApplyPose(view, flight.From.AtLayout(), layoutHeight: showcase[i].Size.y);
            }
            if (showcase != null) yield return RevealSummonGroup(moves, showcase, views, opacity);

            for (int i = 0; i < moves.Count; i++)
                readouts[i] = CreateMovingReadout(GroupLandingFlight(moves[i].Flight, showcase, i),
                    moves[i].Motion, moves[i].Readout, handoff: returning ? FieldReturnHandoff : null);

            float stagger = returning ? 0f : .026f;
            float travel = returning ? .34f : .37f + (moves.Count - 1) * stagger;
            if (!returning && showcase == null) LotdAudio.PlaySummon();
            bool revealedReturn = false;
            yield return Animate(travel, progress =>
            {
                for (int i = 0; i < moves.Count; i++)
                {
                    var move = moves[i];
                    float delay = i * stagger;
                    float time = Mathf.Clamp01((progress * travel - delay) / (travel - delay));
                    var flight = GroupLandingFlight(move.Flight, showcase, i);
                    float layoutHeight = showcase != null ? showcase[i].Size.y : 0f;
                    ApplyFlight(views[i], flight, time, returning ? 18f : move.Motion == CardMotion.TokenSummon ? 11f : 32f,
                        layoutHeight: layoutHeight);
                    if (layoutHeight > 0f) FadeSummonOverframe(views[i], time);
                    if (showcase == null || move.Motion == CardMotion.TokenSummon)
                        opacity[i].alpha = Mathf.SmoothStep(0f, 1f, time / (move.Motion == CardMotion.TokenSummon ? .75f : .22f));
                    readouts[i]?.Follow(views[i], time);
                    TrackCardEffect(effects[i], move.Flight.To, 1.65f, time);
                }
                if (!returning || revealedReturn || progress < FieldReturnHandoff) return;
                revealedReturn = true;
                foreach (var move in moves) move.Handoff?.Invoke();
                refresh();
            });

            for (int i = 0; i < moves.Count; i++)
            {
                opacity[i].alpha = 1f;
                if (showcase != null && showcase[i].IsValid) RemoveTransient(views[i].gameObject);
                else RecycleCard(views[i]);
                if (readouts[i] != null) RemoveTransient(readouts[i].Root.gameObject);
                RemoveTransient(effects[i].gameObject);
            }
            land();
            if (!returning)
            {
                List<(CardPose Slot, Action<float> Impact)> slots = null;
                foreach (var move in moves)
                    if (IsSummonMotion(move.Motion) && IsSpecialEditionLanding(move.Flight))
                        (slots ??= new List<(CardPose, Action<float>)>(moves.Count)).Add((move.Flight.To, move.LandingImpact));
                if (slots != null) yield return PlayOverframeLanding(slots);
            }
        }

        private CardPose[] GroupSummonPoses(IReadOnlyList<FieldArrival> moves)
        {
            Vector2 area = _layer.rect.size;
            if (area.x <= 0f || area.y <= 0f) return null;
            List<int> aces = null;
            for (int i = 0; i < moves.Count; i++)
            {
                var move = moves[i];
                if (IsSummonMotion(move.Motion) && (IsSpecialEditionLanding(move.Flight) || HasSummonPortrait(move.Flight)))
                    (aces ??= new List<int>()).Add(i);
            }
            if (aces == null) return null;

            float height = Mathf.Min(352f, area.y * .395f, area.x * .88f / (aces.Count * 1.17f));
            float spacing = height * 1.17f;
            var poses = new CardPose[moves.Count];
            for (int i = 0; i < aces.Count; i++)
            {
                float x = (i - (aces.Count - 1) * .5f) * spacing;
                poses[aces[i]] = new CardPose(new Vector2(x, area.y * .045f),
                    new Vector2(height * LotdUiKit.CardAspectRatio, height));
            }
            return poses;
        }

        private IEnumerator RevealSummonGroup(IReadOnlyList<FieldArrival> moves, CardPose[] poses, CompositeCardView[] views,
            CanvasGroup[] opacity)
        {
            var shade = LotdUiKit.AddImage(LotdUiKit.FillRect("GroupSummonShade", _layer).gameObject,
                LotdUiKit.WhiteSprite, Color.clear, false, false);
            Vector2 area = _layer.rect.size;
            var aura = CreateEffect(DuelEffectKind.Chain, Vector2.up * area.y * .13f, area * .72f, DuelStyle.ChainGold,
                texture: DuelVisualAssets.Effect(DuelEffectKind.Summon));
            var wave = CreateEffect(DuelEffectKind.ChainImpactWave, Vector2.zero,
                new Vector2(area.x * .73f, area.y * .28f), DuelStyle.ChainGold);
            var flights = new CardFlight[moves.Count];
            var standardPortraits = new SummonPortraitView[moves.Count];
            for (int i = 0; i < views.Length; i++)
            {
                if (!poses[i].IsValid) continue;
                var flight = moves[i].Flight;
                flights[i] = new CardFlight(flight.Code, flight.From, poses[i], flight.StartsFaceDown, false, flight.Appearance);
                if (!flight.StartsFaceDown && moves[i].Readout?.DestinationCard is { } live)
                    views[i].SetDeckCard(flight.Code, flight.Appearance, live: live);
                if (flight.Appearance?.overframe == false || !HasSummonPortrait(flight))
                {
                    var (_, accent, _) = SummonEffect(flight.Code, moves[i].Motion);
                    float h = poses[i].Size.y;
                    standardPortraits[i] = CreateSummonPortrait("GroupSummonPortrait", flight, poses[i].Position,
                        new Vector2(h * 1.10f, h * 1.40f), accent, artworkFallback: IsSpecialEditionLanding(flight));
                }
                else
                {
                    views[i].EnableOverframe();
                    views[i].PoseOverframe(0f, 1f, Vector2.zero, 0f);
                }
                views[i].transform.SetAsLastSibling();
            }

            LotdAudio.PlaySummon();
            yield return Animate(.27f, t =>
            {
                shade.color = new Color(.009f, .013f, .02f, Mathf.SmoothStep(0f, .64f, t));
                aura.Seek(t * .49f);
                wave.Seek(Mathf.InverseLerp(.70f, 1f, t) * .43f);
                for (int i = 0; i < views.Length; i++)
                {
                    if (!poses[i].IsValid) continue;
                    ApplyFlight(views[i], flights[i], t, 29f, moves[i].Readout?.DestinationCard, layoutHeight: poses[i].Size.y);
                    if (standardPortraits[i] is { } portrait)
                    {
                        float emerge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.52f, .94f, t));
                        Vector2 center = poses[i].Position + Vector2.up * (poses[i].Size.y * .23f);
                        portrait.Pose(center + Vector2.down * (22f * (1f - emerge)), emerge, .92f + emerge * .08f);
                        opacity[i].alpha = Mathf.Lerp(1f, .32f, emerge);
                    }
                    else if (views[i].HasOverframe) PopSummonOverframe(views[i], t);
                }
            });
            yield return Animate(.27f, t =>
            {
                shade.color = new Color(.009f, .013f, .02f, .64f * (1f - t));
                aura.Seek(.49f + t * .51f);
                wave.Seek(.43f + t * .57f);
                for (int i = 0; i < views.Length; i++)
                {
                    if (!poses[i].IsValid) continue;
                    if (standardPortraits[i] is { } portrait)
                    {
                        Vector2 center = poses[i].Position + Vector2.up * (poses[i].Size.y * .23f);
                        portrait.Pose(center + Vector2.up * (18f * t), 1f - t, 1f + t * .035f);
                        opacity[i].alpha = Mathf.Lerp(.32f, 1f, t);
                    }
                    else if (views[i].HasOverframe) SettleSummonOverframe(views[i], t);
                }
            });

            foreach (var portrait in standardPortraits)
                if (portrait != null) RemoveTransient(portrait.gameObject);
            RemoveTransient(shade.gameObject);
            RemoveTransient(aura.gameObject);
            RemoveTransient(wave.gameObject);
        }

        private static CardFlight GroupLandingFlight(CardFlight flight, CardPose[] showcase, int index) =>
            showcase != null && showcase[index].IsValid
                ? new CardFlight(flight.Code, showcase[index], flight.To, false, flight.EndsFaceDown, flight.Appearance) : flight;

        private IEnumerator PlayFieldDepartures(FieldDepartureGroup group)
        {
            if (ReferenceEquals(_openFieldDepartures, group)) _openFieldDepartures = null;
            var moves = group.Moves;
            void Depart()
            {
                foreach (var move in moves) move.Depart?.Invoke();
                group.Refresh();
            }
            void Land()
            {
                foreach (var move in moves) move.Release?.Invoke();
                group.Refresh();
            }

            if (moves.Count == 1 && !group.Temporary)
            {
                var move = moves[0];
                yield return MoveCard(move.Flight, move.Motion, Land, move.Readout, Depart);
                yield break;
            }

            var effects = new DuelEffectGraphic[moves.Count];
            for (int index = 0; index < moves.Count; index++)
            {
                var origin = moves[index].Flight.From;
                var kind = group.Temporary ? DuelEffectKind.BanishTransit : DuelEffectKind.Break;
                effects[index] = CreateEffect(kind, origin, group.Temporary ? 1.62f : 2.1f,
                    group.Temporary ? BanishTint : DestructionTint);
                effects[index].Seek(0f);
            }
            if (!group.Temporary) LotdAudio.PlayDamage();
            yield return Animate(group.Temporary ? .12f : .18f, time =>
            {
                for (int index = 0; index < effects.Length; index++)
                    TrackCardEffect(effects[index], moves[index].Flight.From, group.Temporary ? 1.62f : 2.1f, time);
            });
            foreach (var effect in effects) RemoveTransient(effect.gameObject);

            var cards = new CompositeCardView[moves.Count];
            var readouts = new DuelMovingReadout[moves.Count];
            var fading = new CanvasGroup[moves.Count];
            for (int index = 0; index < moves.Count; index++)
            {
                var move = moves[index];
                var proxy = PrepareFlightCard(move.Flight);
                cards[index] = proxy;
                if (move.Motion == CardMotion.Vanish)
                {
                    if (!proxy.TryGetComponent<CanvasGroup>(out var fade))
                        fade = proxy.gameObject.AddComponent<CanvasGroup>();
                    fading[index] = fade;
                }
            }
            for (int index = 0; index < moves.Count; index++)
            {
                var move = moves[index];
                bool sharedPile = move.Motion != CardMotion.Vanish && moves.Exists(other =>
                    other.Motion != CardMotion.Vanish && other.Flight.From.Position != move.Flight.From.Position &&
                    other.Flight.To.Position == move.Flight.To.Position);
                readouts[index] = CreateMovingReadout(move.Flight, move.Motion, move.Readout, sharedPile,
                    handoff: group.Temporary ? sharedPile ? .53f : .68f : null);
                readouts[index]?.Follow(cards[index], 0f);
            }

            Depart();
            yield return Animate(group.Temporary ? .31f : .27f, time =>
            {
                for (int index = 0; index < moves.Count; index++)
                {
                    ApplyFlight(cards[index], moves[index].Flight, time, fading[index] != null ? 6f : 31f);
                    if (fading[index] != null) fading[index].alpha = 1f - Mathf.SmoothStep(0f, 1f, time);
                    readouts[index]?.Follow(cards[index], time);
                }
            });
            for (int index = 0; index < moves.Count; index++)
            {
                if (fading[index] != null) fading[index].alpha = 1f;
                RecycleCard(cards[index]);
                if (readouts[index] != null) RemoveTransient(readouts[index].Root.gameObject);
            }
            Land();
        }

        private IEnumerator PlayFieldSwap((CardFlight Flight, CardMoveReadoutSnapshot Readout) first,
            (CardFlight Flight, CardMoveReadoutSnapshot Readout) second, Action depart, Action handoff, Action arrive)
        {
            var firstCard = PrepareFlightCard(first.Flight);
            var secondCard = PrepareFlightCard(second.Flight);
            var firstStats = CreateMovingReadout(first.Flight, CardMotion.Transfer, first.Readout, handoff: FieldSwapHandoff);
            var secondStats = CreateMovingReadout(second.Flight, CardMotion.Transfer, second.Readout, handoff: FieldSwapHandoff);
            firstStats?.Follow(firstCard, 0f);
            secondStats?.Follow(secondCard, 0f);
            depart();

            bool updated = false;
            yield return Animate(.37f, time =>
            {
                var firstPath = first.Flight.AtLayout();
                var secondPath = second.Flight.AtLayout();
                Vector2 firstTravel = firstPath.To.Position - firstPath.From.Position;
                Vector2 secondTravel = secondPath.To.Position - secondPath.From.Position;
                Vector2 relativeTravel = firstTravel - secondTravel;
                Vector2 side = new Vector2(-relativeTravel.y, relativeTravel.x).normalized;
                if (side == Vector2.zero) side = Vector2.right;
                Vector2 midpointGap = (firstPath.From.Position + firstPath.To.Position -
                    secondPath.From.Position - secondPath.To.Position) * .5f;
                if (Vector2.Dot(side, midpointGap) < 0f) side = -side;
                Vector2 space = Vector2.Max(SwapSize(firstPath, first.Readout), SwapSize(secondPath, second.Readout));
                Vector2 curve = side * (.55f * Vector2.Dot(new Vector2(Mathf.Abs(side.x), Mathf.Abs(side.y)), space) + 6f);
                ApplyFlight(firstCard, firstPath, time, 0f, lateral: curve);
                ApplyFlight(secondCard, secondPath, time, 0f, lateral: -curve);
                firstStats?.Follow(firstCard, time);
                secondStats?.Follow(secondCard, time);
                if (updated || time < FieldSwapHandoff) return;
                updated = true;
                handoff();
            });
            RecycleCard(firstCard);
            RecycleCard(secondCard);
            if (firstStats != null) RemoveTransient(firstStats.Root.gameObject);
            if (secondStats != null) RemoveTransient(secondStats.Root.gameObject);
            arrive();
        }

        private IEnumerator PlayPositionChanges(PositionChangeGroup group)
        {
            if (ReferenceEquals(_openPositionChanges, group)) _openPositionChanges = null;
            var moves = group.Moves;
            var views = new CompositeCardView[moves.Count];
            var readouts = new DuelMovingReadout[moves.Count];
            for (int i = 0; i < moves.Count; i++)
            {
                views[i] = PrepareFlightCard(moves[i].Flight);
            }
            for (int i = 0; i < moves.Count; i++)
            {
                readouts[i] = CreateMovingReadout(moves[i].Flight, CardMotion.ChangePosition, moves[i].Readout);
                readouts[i]?.Follow(views[i], 0f);
            }

            foreach (var move in moves) move.Depart();
            group.Refresh();
            bool turned = false;
            yield return Animate(.27f, time =>
            {
                for (int i = 0; i < moves.Count; i++)
                {
                    ApplyFlight(views[i], moves[i].Flight, time, 13f);
                    readouts[i]?.Follow(views[i], time);
                }
                if (turned || time < PositionHandoff) return;
                turned = true;
                foreach (var move in moves) move.Handoff();
                group.Refresh();
            });
            for (int i = 0; i < moves.Count; i++)
            {
                RecycleCard(views[i]);
                if (readouts[i] != null) RemoveTransient(readouts[i].Root.gameObject);
                moves[i].Release();
            }
            group.Refresh();
        }

        private static Vector2 SwapSize(CardFlight flight, CardMoveReadoutSnapshot readout)
        {
            Vector2 size = Vector2.Max(flight.From.Size, flight.To.Size) * 1.10f;
            if (readout != null)
            {
                var movement = readout.Movement;
                size = Vector2.Max(size, Vector2.Max(movement.StartSize, movement.EndSize));
            }
            return size;
        }

        private IEnumerator PlaySummonMaterials(IReadOnlyList<SummonMaterialFlight> moves, uint method, Action refresh, bool merging,
            MaterialDepartureGroup group = null)
        {
            if (ReferenceEquals(_openMaterials?.Moves, moves)) _openMaterials = null;
            Color accent = method switch
            {
                YgoConstants.REASON_FUSION => new Color(.82f, .47f, 1f),
                YgoConstants.REASON_SYNCHRO => new Color(.42f, .95f, .65f),
                YgoConstants.REASON_RITUAL => new Color(.38f, .62f, 1f),
                YgoConstants.REASON_LINK => new Color(.26f, .80f, 1f),
                _ => new Color(.71f, .51f, 1f),
            };
            var cues = new DuelEffectGraphic[moves.Count];
            for (int i = 0; i < moves.Count; i++)
            {
                var origin = moves[i].Flight.From;
                cues[i] = CreateEffect(DuelEffectKind.SummonMaterial, origin, 1.58f, accent);
            }
            yield return Animate(.105f, time =>
            {
                for (int i = 0; i < cues.Length; i++)
                    TrackCardEffect(cues[i], moves[i].Flight.From, 1.58f, time * .56f);
            });

            var cards = new CompositeCardView[moves.Count];
            var fades = new CanvasGroup[moves.Count];
            var stats = new DuelMovingReadout[moves.Count];
            var spread = new Vector2[moves.Count];
            var kind = MaterialSignature(method);
            var shade = kind.HasValue ? SummonScreenTint("SummonMaterialShade") : null;
            var portal = kind.HasValue ? CreateMaterialPortal(moves, kind.Value, accent, group?.LinkMarkers ?? 0) : null;
            var orbs = new DuelEffectGraphic[moves.Count];
            for (int i = 0; i < moves.Count; i++)
            {
                var view = PrepareFlightCard(moves[i].Flight);
                if (!view.TryGetComponent<CanvasGroup>(out var fade))
                    fade = view.gameObject.AddComponent<CanvasGroup>();
                fade.alpha = 1f;
                cards[i] = view;
                fades[i] = fade;
            }
            if (portal != null)
                for (int i = 0; i < moves.Count; i++)
                    orbs[i] = CreateMaterialOrb(moves[i], portal.Kind, accent);
            for (int i = 0; i < moves.Count; i++)
            {
                var move = moves[i];
                stats[i] = CreateMovingReadout(move.Flight, CardMotion.Transfer, move.Readout,
                    handoff: moves.Count > 1 || move.Flight.EndsFaceDown ? .61f : .75f);
                if (stats[i] != null) spread[i] = MaterialSpread(moves, i, merging);
                stats[i]?.Follow(cards[i], 0f);
            }

            foreach (var move in moves) move.Depart?.Invoke();
            refresh();
            yield return Animate(portal != null ? .48f : .30f, time =>
            {
                if (portal != null)
                {
                    portal.Seek(time);
                    shade.color = new Color(.008f, .012f, .021f, Mathf.Sin(time * Mathf.PI) * .58f);
                }
                for (int i = 0; i < moves.Count; i++)
                {
                    var move = moves[i];
                    var flight = move.Flight;
                    TrackCardEffect(cues[i], flight.From, 1.58f, .56f + .44f * Mathf.Clamp01(time * 2.4f));
                    if (portal != null && kind != DuelEffectKind.Fusion)
                        ApproachMaterialPortal(cards[i], flight, portal, i, time);
                    else
                        ApplyFlight(cards[i], flight, time, move.Vanishes ? 6f : 18f, lateral: spread[i]);
                    if (orbs[i] != null) PoseMaterialOrb(orbs[i], portal, move, i, time);
                    if (kind == DuelEffectKind.Fusion)
                        PullFusionCard(cards[i], flight, portal.rectTransform.anchoredPosition, i, time);

                    if (portal != null)
                    {
                        float fadeStart = kind == DuelEffectKind.Fusion ? .50f : .36f;
                        float fadeEnd = kind == DuelEffectKind.Fusion ? .96f : .71f;
                        fades[i].alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, fadeEnd, time));
                    }
                    else if (move.Vanishes)
                        fades[i].alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.48f, .94f, time));
                    stats[i]?.Follow(cards[i], time, fades[i].alpha);
                }
            });
            if (portal != null)
            {
                RemoveTransient(portal.gameObject);
                RemoveTransient(shade.gameObject);
            }
            for (int i = 0; i < cards.Length; i++)
            {
                fades[i].alpha = 1f;
                RecycleCard(cards[i]);
                if (stats[i] != null) RemoveTransient(stats[i].Root.gameObject);
                RemoveTransient(cues[i].gameObject);
                if (orbs[i] != null) RemoveTransient(orbs[i].gameObject);
                moves[i].Release?.Invoke();
            }
            refresh();
        }

        private static Vector2 MaterialSpread(IReadOnlyList<SummonMaterialFlight> moves, int index, bool merging)
        {
            var move = moves[index];
            Vector2 center = move.Flight.From.Position;
            int nearby = 1;
            for (int i = 0; i < moves.Count; i++)
            {
                var other = moves[i];
                if (i == index || other.Readout?.SourceCard == null ||
                    !merging && other.Flight.To.Position != move.Flight.To.Position) continue;
                center += other.Flight.From.Position;
                nearby++;
            }
            // Keep the original slots' spacing as the readouts start converging on a shared destination.
            Vector2 direction = move.Flight.From.Position - center / nearby;
            return direction.normalized * (move.Readout.Movement.StartSize.x * .38f);
        }

        private DuelMovingReadout CreateMovingReadout(CardFlight flight, CardMotion motion, CardMoveReadoutSnapshot readout,
            bool sharedPile = false, float? handoff = null)
        {
            if (readout?.SourceCard == null && readout?.DestinationCard == null) return null;
            var holder = CenteredVisual("MovingCardReadout", _layer, flight.From.AtLayout().Position, readout.Movement.StartSize);
            float transition = handoff ?? (motion == CardMotion.ChangePosition ? PositionHandoff : motion == CardMotion.TokenSummon ? .27f
                : !flight.EndsFaceDown && readout.DestinationCard == null && !sharedPile ? .82f : .5f);
            return new DuelMovingReadout(holder, readout, transition);
        }

        private IEnumerator MoveCard(CardFlight flight, CardMotion motion, Action onArrive, CardMoveReadoutSnapshot readout,
            Action onDepart, Action onHandoff = null, int signatureUnits = 0, Action<float> landingImpact = null)
        {
            if (!flight.From.IsValid || !flight.To.IsValid)
            {
                onDepart?.Invoke();
                onArrive?.Invoke();
                yield break;
            }

            if (motion == CardMotion.Destroy)
            {
                LotdAudio.PlayDamage();
                yield return Burst(DuelEffectKind.Break, flight.From, 2.1f, DestructionTint, .18f);
            }

            bool extraSummon = motion is CardMotion.ExtraDeckSummon or CardMotion.PendulumSummon;
            bool summons = IsSummonMotion(motion);
            bool hasPortrait = summons && HasSummonPortrait(flight);
            bool overframe = summons && (IsSpecialEditionLanding(flight) || (hasPortrait && flight.Appearance?.overframe != false));
            bool featured = summons && (hasPortrait || TakeFeaturedSummon(flight));
            bool reveal = !flight.EndsFaceDown && (extraSummon || featured || overframe);
            var proxy = PrepareFlightCard(flight, reveal);
            var moving = reveal ? null : CreateMovingReadout(flight, motion, readout);
            moving?.Follow(proxy, 0f);
            onDepart?.Invoke();
            if (reveal)
                yield return RevealSummon(proxy, flight, motion, featured, overframe, readout?.DestinationCard, signatureUnits);
            else if (motion is CardMotion.AttachMaterial or CardMotion.DetachMaterial)
            {
                bool attaching = motion == CardMotion.AttachMaterial;
                var material = attaching ? flight.To : flight.From;
                var effect = CreateEffect(DuelEffectKind.XyzMaterial, material, 2.3f, new Color(.71f, .51f, 1f));
                effect.Seek(0f);
                proxy.transform.SetAsLastSibling();
                moving?.Root.SetAsLastSibling();
                if (!proxy.TryGetComponent<CanvasGroup>(out var fade))
                    fade = proxy.gameObject.AddComponent<CanvasGroup>();
                yield return Animate(.24f, t =>
                {
                    ApplyFlight(proxy, flight, t, 14f);
                    moving?.Follow(proxy, t);
                    fade.alpha = attaching ? 1f - Mathf.SmoothStep(0f, 1f, (t - .79f) / .21f) : 1f;
                    TrackCardEffect(effect, material, 2.3f, attaching ? Mathf.Clamp01((t - .36f) / .64f) : t);
                });
                fade.alpha = 1f;
                RemoveTransient(effect.gameObject);
            }
            else if (motion == CardMotion.TokenSummon || motion == CardMotion.Vanish)
            {
                bool appearing = motion == CardMotion.TokenSummon;
                if (!proxy.TryGetComponent<CanvasGroup>(out var fade))
                    fade = proxy.gameObject.AddComponent<CanvasGroup>();
                var origin = appearing ? flight.To : flight.From;
                float scale = 2.4f * flight.To.Size.y / origin.Size.y;
                var effect = CreateEffect(appearing ? DuelEffectKind.Token : DuelEffectKind.Break, origin, scale, DuelStyle.Player);
                proxy.transform.SetAsLastSibling();
                moving?.Root.SetAsLastSibling();
                fade.alpha = appearing ? 0f : 1f;
                if (appearing) LotdAudio.PlaySummon();
                yield return Animate(appearing ? .35f : .22f, t =>
                {
                    ApplyFlight(proxy, flight, t, appearing ? 10f : 6f);
                    moving?.Follow(proxy, t);
                    fade.alpha = appearing ? Mathf.SmoothStep(0f, 1f, t) : 1f - t;
                    TrackCardEffect(effect, origin, scale, t);
                });
                fade.alpha = 1f;
                Destroy(effect.gameObject);
            }
            else
            {
                float lift = motion == CardMotion.ChangePosition ? 13f : 31f;
                float time = motion == CardMotion.Transfer ? .23f : .27f;
                bool handedOff = false;
                yield return Animate(time, t =>
                {
                    ApplyFlight(proxy, flight, t, lift);
                    moving?.Follow(proxy, t);
                    if (onHandoff == null || handedOff || t < PositionHandoff) return;
                    handedOff = true;
                    onHandoff();
                });
            }

            if (moving != null) RemoveTransient(moving.Root.gameObject);
            if (reveal) RemoveTransient(proxy.gameObject);
            else RecycleCard(proxy);
            onArrive?.Invoke();
            if (summons && IsSpecialEditionLanding(flight))
                yield return PlayOverframeLanding(new[] { (flight.To, landingImpact) });
            else if (summons && !reveal)
            {
                LotdAudio.PlaySummon();
                Color accent = motion == CardMotion.SpecialSummon ? DuelStyle.Gold : new Color(.58f, .87f, 1f);
                yield return Burst(DuelEffectKind.Summon, flight.To, 1.9f, accent, .21f);
            }
            else if (motion == CardMotion.Set)
            {
                LotdAudio.PlayClick();
                yield return Burst(DuelEffectKind.Summon, flight.To, 1.45f,
                    new Color(.54f, .73f, .90f, .64f), .14f);
            }
        }

        internal static bool IsSummonMotion(CardMotion motion) =>
            motion is CardMotion.Summon or CardMotion.SpecialSummon or CardMotion.ExtraDeckSummon or CardMotion.PendulumSummon;

        private static bool IsSpecialEditionLanding(CardFlight flight) =>
            !flight.EndsFaceDown && flight.Appearance?.overframe == true;

        private IEnumerator PlayOverframeLanding(IReadOnlyList<(CardPose Slot, Action<float> Impact)> slots)
        {
            const float scale = 2.04f;
            var waves = new DuelEffectGraphic[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                var anchor = slots[i].Slot;
                var tint = Color.Lerp(DuelStyle.Side(anchor.Angle > 45f), DuelStyle.ChainGold, .36f);
                var wave = CreateEffect(DuelEffectKind.ChainImpactWave, anchor, scale, tint);
                wave.name = "OverframeLanding";
                wave.Seek(0f);
                waves[i] = wave;
            }
            _activeLandingImpact = progress =>
            {
                foreach (var slot in slots) slot.Impact?.Invoke(progress);
            };
            LotdAudio.PlaySummon();
            yield return Animate(.18f, t =>
            {
                _activeLandingImpact?.Invoke(t);
                for (int i = 0; i < waves.Length; i++)
                    TrackCardEffect(waves[i], slots[i].Slot, scale, t);
            });
            ResetLandingImpact();
            foreach (var wave in waves) RemoveTransient(wave.gameObject);
        }

        private void ResetLandingImpact()
        {
            var impact = _activeLandingImpact;
            _activeLandingImpact = null;
            impact?.Invoke(1f);
        }

        private static bool HasSummonPortrait(CardFlight flight)
        {
            if (flight.EndsFaceDown || flight.Code == 0) return false;
            var card = CardDatabase.Get(flight.Code);
            if (card?.IsMonster != true || (card.type & YgoConstants.TYPE_TOKEN) != 0) return false;
            var (artCode, artVariant) = ResolveSummonPortrait(flight);
            return DuelVisualAssets.CanDisplayCutIn(artCode, artVariant);
        }

        private static (uint, int) ResolveSummonPortrait(CardFlight flight)
        {
            int selected = flight.Appearance?.ArtworkVariant ?? CardArtLoader.GetDuelArtworkVariant(flight.Code);
            var (artCode, artVariant) = CardArtLoader.GetArtworkSource(flight.Code, selected);
            return artCode == 0 ? (flight.Code, selected) : (artCode, artVariant);
        }

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

        private SummonPortraitView CreateSummonPortrait(string name, CardFlight flight, Vector2 center, Vector2 size,
            Color accent, bool artworkFallback = false)
        {
            var (artCode, artVariant) = ResolveSummonPortrait(flight);
            var sprite = DuelVisualAssets.RetainCutIn(artCode, artVariant);
            bool usesArtwork = sprite == null;
            if (usesArtwork && !artworkFallback) return null;
            int selected = flight.Appearance?.ArtworkVariant ?? CardArtLoader.GetDuelArtworkVariant(flight.Code);
            if (usesArtwork)
            {
                sprite = CardArtLoader.RetainArtwork(flight.Code, selected);
                if (sprite == CardArtLoader.GetFallbackArt()) return null;
            }
            var holder = CenteredVisual(name, _layer, center, size);
            var portrait = holder.gameObject.AddComponent<SummonPortraitView>();
            portrait.Bind(sprite, accent, usesArtwork,
                usesArtwork ? () => CardArtLoader.ReleaseArtwork(flight.Code, selected) : () => DuelVisualAssets.ReleaseCutIn(sprite));
            return portrait;
        }

        private IEnumerator RevealSummon(CompositeCardView proxy, CardFlight flight, CardMotion motion, bool featured,
            bool overframe, LiveCardState live, int signatureUnits)
        {
            if (!flight.StartsFaceDown && live != null) proxy.SetDeckCard(flight.Code, flight.Appearance, live: live);
            var (kind, accent, title) = SummonEffect(flight.Code, motion);
            Vector2 view = _layer.rect.size;
            var center = SummonShowcaseCenter(view);
            bool signature = DuelEffectGraphic.HasSummonSignature(kind);
            float height = Mathf.Min(352f, view.y * .395f);
            float layoutHeight = overframe ? height : 0f;
            if (overframe)
            {
                proxy.EnableOverframe();
                proxy.PoseOverframe(0f, 1f, Vector2.zero, 0f);
            }
            var reveal = new CardPose(center, new Vector2(height * LotdUiKit.CardAspectRatio, height), 0f);
            var first = new CardFlight(flight.Code, flight.From, reveal, flight.StartsFaceDown, false, flight.Appearance);
            var background = SummonScreenTint("SummonShade");
            var effect = signature
                ? CreateSignatureEffect(kind, center, view, accent, finale: true)
                : CreateEffect(kind, center, new Vector2(view.x * .76f, view.y * .83f), accent);
            effect.SummonUnits = LimitSummonUnits(kind == DuelEffectKind.Xyz
                ? Mathf.Max(signatureUnits, live?.overlayCodes.Count ?? 0) : signatureUnits);
            if (kind == DuelEffectKind.Link) effect.DirectionMask = SummonedLinkMarkers(flight.Code, live);
            var flash = signature ? SummonScreenTint("SummonPeakFlash") : null;
            Text circuitClosed = null;
            if (kind == DuelEffectKind.Link)
            {
                var banner = CenteredVisual("LinkCircuitClosed", _layer, center + Vector2.up * view.y * .35f,
                    new Vector2(view.x * .35f, view.y * .066f));
                circuitClosed = LotdUiKit.AddFittedText(banner, "Message", "LINK CLOSED!", 30, 16,
                    TextAnchor.MiddleCenter, Color.clear, FontStyle.Bold);
            }
            Vector2 portraitCenter = center + new Vector2(view.x * .065f, view.y * .032f);
            DuelEffectGraphic aura = null;
            DuelEffectGraphic wave = null;
            if (overframe)
            {
                aura = CreateEffect(DuelEffectKind.Chain, center + Vector2.up * height * .19f,
                    new Vector2(height * 1.93f, height * 1.42f), DuelStyle.ChainGold,
                    texture: kind != DuelEffectKind.Summon ? DuelVisualAssets.Effect(DuelEffectKind.Summon) : null);
                wave = CreateEffect(DuelEffectKind.ChainImpactWave, center - Vector2.up * height * .13f,
                    new Vector2(height * 1.41f, height * .61f), DuelStyle.ChainGold);
            }
            proxy.transform.SetAsLastSibling();
            SummonPortraitView portrait = null;
            CanvasGroup framing = null;
            CanvasGroup movingFace = null;
            if (featured || overframe)
            {
                var tint = SummonPortraitAccent(new DuelCardReadout(CardDatabase.Get(flight.Code), live).Attribute, accent);
                portrait = CreateSummonPortrait("SummonPortrait", flight, portraitCenter, new Vector2(view.x * .86f, view.y * .77f),
                    tint, artworkFallback: IsSpecialEditionLanding(flight));
                if (portrait != null)
                {
                    framing = CreateSummonFraming(background.rectTransform, tint);
                    if (!proxy.TryGetComponent(out movingFace))
                        movingFace = proxy.gameObject.AddComponent<CanvasGroup>();
                }
            }
            var nameCenter = center - new Vector2(portrait != null ? view.x * .083f : 0f, view.y * (portrait != null ? .30f : .24f));
            var (name, stats) = SummonPlate(flight, title, nameCenter, live);
            var nameRect = (RectTransform)name.transform;
            var statsRect = (RectTransform)stats.transform;
            Vector2 statsCenter = statsRect.anchoredPosition;
            Vector2 statsEntrance = Vector2.right * Mathf.Min(62f, view.x * .045f);
            statsRect.anchoredPosition = statsCenter + statsEntrance;

            bool playedImpact = !signature;
            if (playedImpact) LotdAudio.PlaySummon();
            yield return Animate(signature ? .36f : .28f, t =>
            {
                if (!playedImpact && t >= .88f)
                {
                    playedImpact = true;
                    LotdAudio.PlaySummon();
                }
                float arrival = signature ? Mathf.InverseLerp(.20f, .97f, t) : t;
                ApplySummonEntrance(proxy, first, kind, arrival, live, layoutHeight);
                effect.Seek(t * .54f);
                if (signature) PoseSummonPeak(flash, circuitClosed, t * .54f, view.y);
                background.color = new Color(.009f, .013f, .02f, Mathf.SmoothStep(0f, .64f, t));
                name.alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(portrait != null ? .74f : .47f, 1f, t));
                nameRect.anchoredPosition = nameCenter + Vector2.left * (42f * (1f - name.alpha));
                if (overframe)
                {
                    aura.Seek(PopSummonOverframe(proxy, signature ? Mathf.InverseLerp(.30f, 1f, t) : t) * .49f);
                    wave.Seek(Mathf.InverseLerp(.72f, 1f, t) * .43f);
                }
                if (portrait != null)
                {
                    float emerge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(signature ? .58f : .48f, .95f, t));
                    portrait.Pose(portraitCenter + Vector2.right * (view.x * .17f * (1f - emerge)), emerge,
                        Mathf.Lerp(.90f, 1.055f, emerge));
                    framing.alpha = emerge;
                    movingFace.alpha = 1f - emerge;
                }
            });
            yield return Animate(featured || overframe ? .29f : .18f, t =>
            {
                effect.Seek(.54f + t * .22f);
                if (signature) PoseSummonPeak(flash, circuitClosed, .54f + t * .22f, view.y);
                stats.alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.04f, .67f, t));
                statsRect.anchoredPosition = statsCenter + statsEntrance * (1f - stats.alpha);
                proxy.SetRarityHighlight(Mathf.Lerp(.85f, .25f, t));
                if (portrait != null)
                {
                    float impact = (1f - t) * (1f - t);
                    var movement = new Vector2(Mathf.Sin(t * 25f), Mathf.Sin(t * 18f)) * (4f * impact);
                    portrait.Pose(portraitCenter + movement, 1f, 1f + .055f * impact);
                }
                if (overframe)
                {
                    SettleSummonOverframe(proxy, t);
                    aura.Seek(.49f + t * .28f);
                    wave.Seek(.43f + t * .57f);
                }
            });

            var last = new CardFlight(flight.Code, reveal, flight.To, false, flight.EndsFaceDown, flight.Appearance);
            yield return Animate(.22f, t =>
            {
                ApplyFlight(proxy, last, t, 23f, live, layoutHeight: layoutHeight);
                proxy.SetRarityHighlight(.25f * (1f - t));
                effect.Seek(.76f + t * .24f);
                if (signature) PoseSummonPeak(flash, circuitClosed, .76f + t * .24f, view.y);
                name.alpha = 1f - t;
                background.color = new Color(.009f, .013f, .02f, .64f * (1f - t));
                if (portrait != null)
                {
                    float dissolve = Mathf.SmoothStep(0f, 1f, t * 2.5f);
                    portrait.Pose(Vector2.Lerp(portraitCenter, flight.To.AtLayout().Position, dissolve * .28f),
                        1f - dissolve, Mathf.Lerp(1f, .84f, dissolve));
                    framing.alpha = 1f - dissolve;
                    movingFace.alpha = dissolve;
                }
                if (overframe)
                {
                    FadeSummonOverframe(proxy, t);
                    aura.Seek(.77f + t * .23f);
                }
            });
            RemoveTransient(background.gameObject);
            RemoveTransient(effect.gameObject);
            RemoveTransient(name.gameObject);
            if (flash != null) RemoveTransient(flash.gameObject);
            if (circuitClosed != null) RemoveTransient(circuitClosed.transform.parent.gameObject);
            if (portrait != null) RemoveTransient(portrait.gameObject);
            if (aura != null) RemoveTransient(aura.gameObject);
            if (wave != null) RemoveTransient(wave.gameObject);
        }

        private static float PopSummonOverframe(CompositeCardView card, float time)
        {
            float emerge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.49f, .86f, time));
            card.PoseOverframe(emerge, Mathf.Lerp(.88f, 1.25f, emerge), new Vector2(.44f, -.57f) * emerge, time * .5f);
            card.transform.localRotation *= Quaternion.Euler(-7f * emerge, -9f * emerge, 0f);
            return emerge;
        }

        private static void SettleSummonOverframe(CompositeCardView card, float time)
        {
            float spring = (1f - time) * (1f - time);
            float settle = spring * Mathf.Cos(time * Mathf.PI * 1.65f);
            card.PoseOverframe(1f, 1f + .25f * settle, new Vector2(.44f, -.57f) * spring, .5f + time * .34f);
            card.transform.localRotation = Quaternion.Euler(-7f * spring, -9f * spring, 0f);
        }

        private static void FadeSummonOverframe(CompositeCardView card, float time) =>
            card.PoseOverframe(1f - Mathf.SmoothStep(0f, 1f, time * 1.6f), 1f, Vector2.zero, .84f + time * .16f);

        internal static void ApplySummonEntrance(CompositeCardView proxy, CardFlight flight, DuelEffectKind kind, float progress,
            LiveCardState live = null, float layoutHeight = 0f)
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
            ApplyFlight(proxy, flight, progress, lift, live, layoutHeight: layoutHeight);
            float swing = Mathf.Sin(progress * Mathf.PI);
            var rect = (RectTransform)proxy.transform;
            rect.anchoredPosition += Vector2.right * (shift * swing);
            rect.localRotation *= Quaternion.Euler(0f, 0f, roll * swing);
            proxy.SetRarityHighlight(Mathf.SmoothStep(0f, .85f, progress));
        }

        internal (CanvasGroup Name, CanvasGroup Stats) SummonPlate(CardFlight flight, string summonType, Vector2 center,
            LiveCardState live = null)
        {
            float width = Mathf.Min(_layer.rect.width * .64f, 678f);
            var holder = CenteredVisual("SummonName", _layer, center, new Vector2(width, 65f));
            var group = holder.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            var card = new DuelCardReadout(CardDatabase.Get(flight.Code), live);
            var accent = DuelStyle.Side(flight.To.Angle > 45f);
            DuelReadoutPlate.Add(holder, new Color(.031f, .049f, .085f, .97f));
            var rail = LotdUiKit.CreateRect("Accent", holder, new Vector2(.008f, .16f), new Vector2(.008f, .88f),
                Vector2.zero, new Vector2(3f, 0f));
            LotdUiKit.AddImage(rail.gameObject, LotdUiKit.WhiteSprite, accent, false, false);
            var caption = LotdUiKit.CreateRect("Caption", holder, new Vector2(.04f, .60f), new Vector2(.96f, .96f),
                Vector2.zero, Vector2.zero);
            var kind = LotdUiKit.AddFittedText(caption, "Value", summonType,
                16, 11, TextAnchor.MiddleLeft, accent, FontStyle.Bold);
            var rating = card.RatingNumber;
            DuelRatingBadge.AddToHeading(kind).Bind(rating);
            var nameArea = LotdUiKit.CreateRect("SummonNameText", holder, new Vector2(.04f, .05f), new Vector2(.96f, .61f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(nameArea, "Name", CardDatabase.GetName(flight.Code), 27, 13,
                TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);

            var stats = CenteredVisual("SummonStats", holder, new Vector2(width * .20f, -55f), new Vector2(width * .60f, 40f));
            new DuelStatStrip(stats, compact: false).Bind(card, opponent: flight.To.Angle > 45f);
            var statsGroup = stats.gameObject.AddComponent<CanvasGroup>();
            statsGroup.alpha = 0f;
            return (group, statsGroup);
        }

        internal static (DuelEffectKind kind, Color color, string label) SummonEffect(uint code, CardMotion motion)
        {
            if (motion == CardMotion.TokenSummon)
                return (DuelEffectKind.Token, DuelStyle.Player, "토큰 소환");
            if (motion == CardMotion.Set)
                return (DuelEffectKind.Summon, new Color(.54f, .73f, .90f), "세트");
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

        private IEnumerator PlayEquip(EquipCue cue)
        {
            if (ReferenceEquals(_openEquip, cue)) _openEquip = null;
            if (cue.Source == null || cue.Target == null)
            {
                cue.OnContact?.Invoke();
                yield break;
            }

            var link = CreateEffect(DuelEffectKind.EquipLink, Vector2.zero, Vector2.one, new Color(1f, .74f, .37f));
            var rect = link.rectTransform;
            bool contacted = false;
            yield return Animate(.33f, progress =>
            {
                var center = new Vector2(.5f, .5f);
                Vector2 from = GetPoint(cue.Source, center);
                Vector2 to = GetPoint(cue.Target, center);
                Vector2 direction = to - from;
                rect.anchoredPosition = (from + to) * .5f;
                rect.sizeDelta = new Vector2(direction.magnitude, Mathf.Clamp(GetAreaSize(cue.Target).y * .33f, 19f, 36f));
                rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                link.Seek(progress);
                if (contacted || progress < DuelEffectGraphic.EquipContactProgress) return;
                contacted = true;
                cue.OnContact?.Invoke();
            });
            RemoveTransient(link.gameObject);
        }

        private IEnumerator AttackArrow(CardPose source, Func<Vector2> readTarget, bool opponent)
        {
            Vector2 from = source.AtLayout().Position;
            Vector2 to = readTarget();
            Vector2 direction = to - from;
            if (direction.sqrMagnitude < 1f) yield break;
            var color = opponent ? DuelStyle.Opponent : new Color(1f, .39f, .29f);
            var arrow = CreateEffect(DuelEffectKind.AttackPath, (from + to) * .5f,
                new Vector2(direction.magnitude, 58f), color);
            yield return Animate(.23f, progress =>
            {
                from = source.AtLayout().Position;
                to = readTarget();
                direction = to - from;
                arrow.rectTransform.anchoredPosition = (from + to) * .5f;
                arrow.rectTransform.sizeDelta = new Vector2(direction.magnitude, 58f);
                arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                arrow.Seek(progress);
            });
            RemoveTransient(arrow.gameObject);
        }

        internal CanvasGroup CreateBattleReadout(Rect area, BattleCardStats attacker, BattleCardStats defender,
            int viewer, bool attackerIsLink, bool defenderIsLink, bool mobileLayout = false)
        {
            if (!attacker.IsFaceUp && !defender.IsFaceUp) return null;
            bool pair = attacker.IsFaceUp && defender.IsFaceUp;
            if (!pair) area.xMax = Mathf.Lerp(area.xMin, area.xMax, mobileLayout ? .72f : .62f);
            var root = LotdUiKit.CreateRect("BattleReadout", _layer, area.min, area.max, Vector2.zero, Vector2.zero);
            if (mobileLayout) MobileSafeArea.Attach(root, area);
            var fade = root.gameObject.AddComponent<CanvasGroup>();
            fade.alpha = 0f;
            fade.blocksRaycasts = fade.interactable = false;
            DuelReadoutPlate.Add(root, new Color(.028f, .052f, .065f, .97f));
            bool inline = mobileLayout && (!attacker.IsDefense || attacker.ComparedValue.HasValue);

            void AddCard(string name, BattleCardStats card, bool link, bool attacking, string role, float start, float end)
            {
                var segment = LotdUiKit.CreateRect(name, root, new Vector2(start, 0f), new Vector2(end, 1f),
                    new Vector2(3f, 2f), new Vector2(-3f, -2f));
                bool opponent = card.Controller != viewer;
                var title = LotdUiKit.CreateRect("Role", segment, new Vector2(0f, inline ? 0f : .61f),
                    new Vector2(inline ? .48f : 1f, 1f), Vector2.zero, Vector2.zero);
                var titleColor = Color.Lerp(Color.white, DuelStyle.Side(opponent), .33f);
                LotdUiKit.AddFittedText(title, "Text", $"{(opponent ? "상대" : "내")} {role}", inline ? 20 : 17, 9,
                    TextAnchor.MiddleCenter, titleColor, FontStyle.Bold);
                var values = LotdUiKit.CreateRect("Values", segment, new Vector2(inline ? .48f : 0f, 0f),
                    new Vector2(1f, inline ? 1f : .60f), Vector2.zero, Vector2.zero);
                new DuelStatStrip(values, compact: true).BindBattle(card, link, attacking);
            }

            if (attacker.IsFaceUp)
                AddCard("Attacker", attacker, attackerIsLink, true,
                    defender.Location == 0 ? "직접 공격" : "공격", 0f, pair ? .5f : 1f);
            if (defender.IsFaceUp)
                AddCard("Defender", defender, defenderIsLink, false, defender.IsDefense ? "수비" : "대상", pair ? .5f : 0f, 1f);
            return fade;
        }

        private IEnumerator Attack(CardFlight flight, Vector2 to, bool direct, bool opponent,
            CanvasGroup readout, DuelCardReadout attacker, Vector2 statSize, Action onFinish,
            Func<(CardPose Pose, Vector2 ReadoutSize, CardPose Defender, RectTransform DefenderReadout)> onStart,
            List<LifeChangeCue> lifeChanges, Func<(Vector2 Hit, Vector2 AttackerArea, Vector2 DefenderArea)> readLayout)
        {
            if (ReferenceEquals(_openBattleLifeChanges, lifeChanges)) _openBattleLifeChanges = null;
            var from = flight.From;
            uint code = flight.Code;
            if (!from.IsValid || code == 0)
            {
                if (readout != null) RemoveTransient(readout.gameObject);
                onFinish?.Invoke();
                if (lifeChanges.Count != 0) yield return PlayLifeChanges(lifeChanges);
                yield break;
            }

            RectTransform defenderStats = null;
            CardPose defender = default;
            if (onStart != null)
            {
                var (pose, size, target, defending) = onStart();
                if (pose.IsValid) from = pose;
                if (size.x > 0f && size.y > 0f) statSize = size;
                defenderStats = defending;
                defender = target;
            }
            var proxy = TakeCard(code, false);
            var art = (RectTransform)proxy.transform;
            RectTransform stats = null;
            if (attacker.IsMonster && statSize.x > 0f && statSize.y > 0f)
            {
                proxy.SetCard(code, live: attacker.Live);
                stats = CreateBattleCardReadout("AttackingCardReadout", attacker, opponent, from.Position, statSize);
            }
            var statContent = (RectTransform)stats?.Find("CardReadout");
            if (defenderStats != null) defenderStats.SetAsLastSibling();
            if (readout != null) readout.transform.SetAsLastSibling();

            const float gap = 5f;
            CardPose origin = default, windup = default, lunge = default;
            Vector2 defenderSize = defenderStats?.sizeDelta ?? Vector2.zero;
            Vector2 defenderOffset = defenderStats == null ? Vector2.zero : defenderStats.anchoredPosition - defender.Position;
            var initialLayout = readLayout?.Invoke() ?? default;
            float attackerFocus = initialLayout.AttackerArea.y > 0f ? statSize.y / initialLayout.AttackerArea.y : 1f;
            float defenderFocus = initialLayout.DefenderArea.y > 0f ? defenderSize.y / initialLayout.DefenderArea.y : 1f;
            Vector2 hit = Vector2.zero, direction = Vector2.zero, currentStatSize = Vector2.zero;
            Vector2 approachAxis = Vector2.zero, extentAxis = Vector2.zero;
            float stopEdge = 0f;
            void LayoutAttack()
            {
                const float fullLungeScale = 1.14f;
                origin = from.AtLayout();
                var target = defender.AtLayout();
                var layout = readLayout?.Invoke() ?? (Hit: to, AttackerArea: Vector2.zero, DefenderArea: Vector2.zero);
                currentStatSize = layout.AttackerArea.y > 0f ? layout.AttackerArea * attackerFocus : statSize * (origin.Size.y / from.Size.y);
                if (defenderStats != null && target.IsValid)
                {
                    float scale = target.Size.y / defender.Size.y;
                    defenderStats.anchoredPosition = target.Position + defenderOffset * scale;
                    defenderStats.sizeDelta = layout.DefenderArea.y > 0f ? layout.DefenderArea * defenderFocus : defenderSize * scale;
                }
                hit = !direct && target.IsValid ? target.Position : layout.Hit;
                Vector2 delta = hit - origin.Position;
                direction = delta.normalized;
                Vector2 lungePoint = Vector2.Lerp(origin.Position, hit, direct ? .78f : .89f);
                float lungeScale = fullLungeScale;
                approachAxis = Vector2.zero;
                if (!direct && target.IsValid && delta.sqrMagnitude > 1f)
                {
                    Vector2 attackerHalf = BattleHalfSize(origin.Size, origin.Angle, stats != null ? currentStatSize : Vector2.zero);
                    Vector2 defenderHalf = BattleHalfSize(target.Size, target.Angle, defenderStats?.sizeDelta ?? Vector2.zero);
                    Vector2 reserve = attackerHalf * fullLungeScale + defenderHalf + Vector2.one * gap;
                    // Use the first axis on which both rectangles clear along the attack line.
                    bool horizontal = reserve.x / Mathf.Abs(delta.x) < reserve.y / Mathf.Abs(delta.y);
                    extentAxis = horizontal ? Vector2.right : Vector2.up;
                    approachAxis = extentAxis * Mathf.Sign(Vector2.Dot(extentAxis, delta));
                    float half = Vector2.Dot(extentAxis, attackerHalf);
                    stopEdge = Vector2.Dot(approachAxis, hit) - Vector2.Dot(extentAxis, defenderHalf) - gap;
                    float available = stopEdge - Vector2.Dot(approachAxis, origin.Position);
                    lungeScale = Mathf.Clamp((available - 8f) / half, 1f, fullLungeScale);
                    float reach = Mathf.Clamp01((available - half * lungeScale) / Vector2.Dot(approachAxis, delta));
                    lungePoint = Vector2.Lerp(origin.Position, hit, Mathf.Min(.89f, reach));
                }
                windup = new CardPose(origin.Position - direction * 26f, origin.Size * Mathf.Lerp(1f, lungeScale, .714f), origin.Angle);
                lunge = new CardPose(lungePoint, origin.Size * lungeScale, origin.Angle);
            }

            void MoveWithStats(CardFlight path, float time, float lift, float clearance = 1f)
            {
                ApplyFlight(proxy, path, time, lift);
                Vector2 scaledStats = stats == null ? Vector2.zero : currentStatSize * (art.sizeDelta.y / origin.Size.y);
                if (approachAxis != Vector2.zero && clearance > 0f)
                {
                    float extent = Vector2.Dot(extentAxis, BattleHalfSize(art.sizeDelta, art.localEulerAngles.z, scaledStats));
                    float edge = Vector2.Dot(approachAxis, art.anchoredPosition) + extent;
                    art.anchoredPosition -= approachAxis * (Mathf.Max(0f, edge - stopEdge) * clearance);
                }
                if (stats == null) return;
                stats.anchoredPosition = art.anchoredPosition;
                stats.sizeDelta = scaledStats;
                if (approachAxis == Vector2.zero)
                {
                    statContent.anchoredPosition = Vector2.zero;
                    return;
                }
                // A focused card can start closer to its target than its full stat plate allows.
                float statEdge = Vector2.Dot(approachAxis, art.anchoredPosition) + Vector2.Dot(extentAxis, scaledStats) * .5f;
                float frameEdge = stopEdge + (gap - 1f) * (1f - clearance);
                statContent.anchoredPosition = -approachAxis * Mathf.Max(0f, statEdge - frameEdge);
            }

            yield return Animate(.085f, t =>
            {
                LayoutAttack();
                MoveWithStats(new CardFlight(code, origin, windup, false, false), t, 5f, Mathf.SmoothStep(0f, 1f, t));
                if (readout != null) readout.alpha = Mathf.SmoothStep(0f, 1f, t);
            });

            yield return Animate(.13f, t =>
            {
                LayoutAttack();
                MoveWithStats(new CardFlight(code, windup, lunge, false, false), t, direct ? 27f : 16f);
            });

            if (direct) LotdAudio.PlayDirectAttack();
            else LotdAudio.PlayAttack();
            Color accent = opponent ? DuelStyle.Opponent : new Color(.38f, 1f, .70f);
            var impact = CreateEffect(DuelEffectKind.Strike, hit, Vector2.one * (direct ? 335f : 248f), accent);
            if (stats != null) stats.SetAsLastSibling();
            if (defenderStats != null) defenderStats.SetAsLastSibling();
            const float returnDuration = .27f;
            bool showDamage = BeginLifeChanges(lifeChanges);
            float duration = showDamage ? LifeFeedbackDuration : returnDuration;
            yield return Animate(duration, t =>
            {
                LayoutAttack();
                float motion = Mathf.Clamp01(t * duration / returnDuration);
                float clearance = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.8f, 1f, motion));
                var returnFlight = new CardFlight(code, lunge, flight.To.IsValid ? flight.To.AtLayout() : origin, false, false);
                MoveWithStats(returnFlight, motion, 10f, clearance);
                impact.rectTransform.anchoredPosition = hit;
                impact.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                impact.Seek(motion);
                if (readout != null) readout.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.57f, 1f, t));
                if (showDamage) UpdateLifeChanges(lifeChanges, t);
            });
            EndLifeChanges(lifeChanges);
            if (readout != null) RemoveTransient(readout.gameObject);
            if (stats != null) RemoveTransient(stats.gameObject);
            if (defenderStats != null) RemoveTransient(defenderStats.gameObject);
            RemoveTransient(impact.gameObject);
            RecycleCard(proxy);
            onFinish?.Invoke();
        }

        private static Vector2 BattleHalfSize(Vector2 cardSize, float angle, Vector2 readoutSize)
        {
            float radians = angle * Mathf.Deg2Rad;
            float cos = Mathf.Abs(Mathf.Cos(radians)), sin = Mathf.Abs(Mathf.Sin(radians));
            var cardBounds = new Vector2(cardSize.x * cos + cardSize.y * sin, cardSize.x * sin + cardSize.y * cos);
            return Vector2.Max(cardBounds, readoutSize) * .5f;
        }

        internal RectTransform CreateBattleCardReadout(string name, DuelCardReadout card, bool opponent,
            Vector2 position, Vector2 size)
        {
            var holder = CenteredVisual(name, _layer, position, size);
            new DuelFieldReadout(holder).Bind(card, opponent, card.RatingNumber);
            return holder;
        }

        private static void RemoveTransient(GameObject visual)
        {
            visual.SetActive(false);
            if (Application.isPlaying) Destroy(visual);
            else DestroyImmediate(visual);
        }

        private IEnumerator ChainActivation(CardPose source, uint code, int chainNumber, bool opponent, LiveCardState live,
            Action onStart, DeckCardAppearance? appearance)
        {
            onStart?.Invoke();
            appearance ??= live?.Appearance;
            uint previous = chainNumber > 1 && _previousChainNumber == chainNumber - 1 ? _previousChainCode : 0;
            var previousReadout = _previousChainReadout;
            var previousAppearance = _previousChainAppearance;
            bool previousOpponent = _previousChainOpponent;
            _previousChainOpponent = opponent;
            _previousChainNumber = chainNumber;
            _previousChainCode = code;
            _previousChainReadout = live;
            _previousChainAppearance = appearance;
            float side = opponent ? 1f : -1f;
            Vector2 view = _layer.rect.size;
            float height = Mathf.Min(350f, view.y * .45f, view.x * .31f);
            if (height <= 0f) yield break;
            var center = new Vector2(side * view.x * (previous == 0 ? .035f : .075f), view.y * .146f);
            var reveal = new CardPose(center, new Vector2(height * LotdUiKit.CardAspectRatio, height), side * 3f);
            if (!source.IsValid)
                source = new CardPose(center + Vector2.down * height * .27f, reveal.Size * .55f, 0f);
            var overlay = LotdUiKit.FillRect("ChainActivation", _layer);
            LotdUiKit.AddImage(overlay.gameObject, LotdUiKit.WhiteSprite, new Color(.01f, .018f, .023f, .38f), false, false);
            var opacity = overlay.gameObject.AddComponent<CanvasGroup>();
            opacity.alpha = 0f;

            if (previous != 0)
            {
                var point = new Vector2(-side * view.x * .135f, center.y + height * .26f);
                AddChainConnector(overlay, center, point, height);
                var prior = TakeCard(previous, false, detailed: true, live: previousReadout, appearance: previousAppearance);
                prior.transform.SetParent(overlay, false);
                Vector2 priorSize = reveal.Size * .66f;
                ApplyPose(prior, new CardPose(point, priorSize, -side * 10f));
                ChainBadge(overlay, point + new Vector2(-side * (priorSize.x * .5f + height * .17f), -height * .20f), chainNumber - 1,
                    height * .25f, active: false, opponent: previousOpponent);
            }

            var crest = CreateEffect(DuelEffectKind.Chain, center, new Vector2(height * 1.67f, height * 1.39f), DuelStyle.ChainGold);
            var card = TakeCard(code, code == 0, detailed: true, live: live, appearance: appearance);
            card.EnableOverframe();
            card.PoseOverframe(0f, 1f, Vector2.zero, 0f);
            float layoutHeight = card.HasOverframe ? height : 0f;
            bool special = code != 0 && appearance?.overframe == true;
            DuelEffectGraphic editionFlare = null;
            CanvasGroup projectedArt = null;
            Vector2 projectionCenter = Vector2.zero;
            float projectionSide = previous == 0 ? -side : side;
            if (special)
            {
                var tint = DuelStyle.ChainCyan;
                editionFlare = CreateEffect(DuelEffectKind.ChainApplied, center, reveal.Size * 1.6f, tint);
                editionFlare.name = "OverframeActivation";
                if (!card.HasOverframePortrait && card.ArtworkSprite != null &&
                    card.ArtworkSprite != CardArtLoader.GetFallbackArt())
                {
                    projectionCenter = center + new Vector2(projectionSide * height * .64f, height * .25f);
                    projectedArt = CreateChainArtProjection(projectionCenter, height, projectionSide, card.ArtworkSprite, tint);
                }
                card.transform.SetAsLastSibling();
            }
            var aspect = card.transform.Find("AspectBox");
            var flash = DuelStyle.AddActivationGlow(aspect, height * .025f, behind: false);
            flash.gameObject.SetActive(true);
            flash.canvasRenderer.SetAlpha(0f);
            Image highlight = null;
            if (!special && code != 0 && DuelVisualAssets.ChainCardHighlight != null)
            {
                var bounds = LotdUiKit.CreateRect("ChainCardHighlight", aspect, new Vector2(-.17f, -.06f),
                    new Vector2(1.17f, 1.06f), Vector2.zero, Vector2.zero);
                highlight = LotdUiKit.AddImage(bounds.gameObject, DuelVisualAssets.ChainCardHighlight, Color.white, false, false);
                highlight.canvasRenderer.SetAlpha(0f);
            }
            var badgePoint = center + new Vector2(side * (reveal.Size.x * .52f + height * .25f), -height * .35f);
            var wave = CreateEffect(DuelEffectKind.ChainImpactWave, badgePoint, Vector2.one * (height * .76f),
                opponent ? DuelStyle.ChainRed : DuelStyle.ChainCyan);
            var badge = ChainBadge(_layer, badgePoint, chainNumber, height * .32f, active: true, opponent: opponent);
            var rotor = badge.Find("ChainRotor");
            var badgeOpacity = badge.gameObject.AddComponent<CanvasGroup>();
            badgeOpacity.alpha = 0f;
            badge.localScale = Vector3.one * 1.6f;
            LotdAudio.PlayChain();

            var lift = new CardFlight(code, source, reveal, code == 0, code == 0);
            yield return Animate(.23f, t =>
            {
                crest.Seek(t * .58f);
                opacity.alpha = Mathf.Clamp01(t * 2.5f);
                float glow = Mathf.SmoothStep(0f, .86f, (t - .22f) * 2.2f);
                flash.canvasRenderer.SetAlpha(glow);
                if (highlight != null) highlight.canvasRenderer.SetAlpha(glow);
                if (rotor != null) rotor.localRotation = Quaternion.Euler(0f, 0f, -60f * t);
                float impact = Mathf.Clamp01((t - .26f) / .62f);
                badgeOpacity.alpha = Mathf.SmoothStep(0f, 1f, impact * 5f);
                wave.Seek(impact * .68f);
                float rebound = Mathf.Sin(impact * Mathf.PI * 2.25f) * impact * (1f - impact) * .18f;
                badge.localScale = Vector3.one * (Mathf.Lerp(1.6f, 1f, 1f - Mathf.Pow(1f - impact, 3f)) + rebound);
                ApplyFlight(card, lift, t, height * .095f, layoutHeight: layoutHeight);
                TiltChainCard(card, side, t);
                float depth = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.48f, .91f, t));
                card.PoseOverframe(depth, Mathf.Lerp(.92f, 1.08f, depth), Vector2.right * (side * depth * .4f), t * .56f);
                if (special)
                {
                    card.SetRarityHighlight(depth);
                    editionFlare.rectTransform.anchoredPosition = ((RectTransform)card.transform).anchoredPosition;
                    editionFlare.Seek(Mathf.InverseLerp(.34f, .94f, t) * .62f);
                    PoseChainArtProjection(projectedArt, projectionCenter, projectionSide, height, depth);
                }
            });
            var back = new CardFlight(code, reveal, source, code == 0, code == 0);
            yield return Animate(.24f, t =>
            {
                crest.Seek(.58f + t * .42f);
                wave.Seek(.68f + t * .32f);
                float returning = Mathf.Clamp01((t - .22f) / .78f);
                flash.canvasRenderer.SetAlpha(1f - returning);
                if (highlight != null) highlight.canvasRenderer.SetAlpha(1f - returning);
                if (rotor != null) rotor.localRotation = Quaternion.Euler(0f, 0f, -60f - 61f * t);
                badgeOpacity.alpha = opacity.alpha = 1f - returning;
                ApplyFlight(card, back, returning, height * .037f, layoutHeight: layoutHeight);
                TiltChainCard(card, side, (1f - returning) * .5f + .5f, 1f - returning);
                float depth = 1f - returning;
                card.PoseOverframe(depth, Mathf.Lerp(.92f, 1.08f, depth), Vector2.right * (side * depth * .4f), .56f + t * .44f);
                if (special)
                {
                    card.SetRarityHighlight(depth);
                    editionFlare.rectTransform.anchoredPosition = ((RectTransform)card.transform).anchoredPosition;
                    editionFlare.Seek(.62f + .38f * returning);
                    PoseChainArtProjection(projectedArt, projectionCenter, projectionSide, height, depth);
                }
            });
            if (highlight != null) RemoveTransient(highlight.gameObject);
            if (editionFlare != null) RemoveTransient(editionFlare.gameObject);
            if (projectedArt != null) RemoveTransient(projectedArt.gameObject);
            RemoveTransient(card.gameObject);
            RemoveTransient(crest.gameObject);
            RemoveTransient(wave.gameObject);
            RemoveTransient(badge.gameObject);
            RemoveTransient(overlay.gameObject);
        }

        private CanvasGroup CreateChainArtProjection(Vector2 center, float cardHeight, float side, Sprite sprite, Color tint)
        {
            var frame = CenteredVisual("OverframeArtProjection", _layer, center, Vector2.one * (cardHeight * .68f));
            frame.localRotation = Quaternion.Euler(0f, 0f, side * 8f);
            var group = frame.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = group.interactable = false;
            LotdUiKit.AddImage(frame.gameObject, LotdUiKit.WhiteSprite, new Color(tint.r, tint.g, tint.b, .72f), false, false);
            LotdUiKit.AddImage(LotdUiKit.FillRect("Artwork", frame, 2.5f).gameObject, sprite, Color.white, true, false);
            return group;
        }

        private static void PoseChainArtProjection(CanvasGroup projection, Vector2 center, float side, float height, float depth)
        {
            if (projection == null) return;
            projection.alpha = .84f * depth;
            var rect = (RectTransform)projection.transform;
            rect.anchoredPosition = center - Vector2.right * (side * height * .22f * (1f - depth));
            rect.localScale = Vector3.one * Mathf.Lerp(.87f, 1f, depth);
        }

        private static void TiltChainCard(CompositeCardView card, float side, float progress, float strength = 1f)
        {
            var rect = (RectTransform)card.transform;
            float x = Mathf.Sin(progress * Mathf.PI) * -6f * strength;
            float y = Mathf.Lerp(-6f, 6f, progress) * side * strength;
            rect.localRotation = Quaternion.Euler(x, y, rect.localEulerAngles.z);
        }

        private IEnumerator ChainResolution(Func<(Vector2 Center, float MaxHeight)> readLayout, ChainLinkInfo link, bool compact)
        {
            uint code = link.code;
            int chainNumber = link.chainNumber;
            var live = link.ResolutionReadout;

            CardPose ReadPose()
            {
                var (center, maximum) = readLayout();
                float height = Mathf.Min(maximum, _layer.rect.height * .29f);
                return new CardPose(center, new Vector2(height * LotdUiKit.CardAspectRatio, height), -5f);
            }

            var initial = ReadPose();
            if (!initial.IsValid) yield break;
            float initialHeight = initial.Size.y;
            var overlay = LotdUiKit.FillRect("ChainResolution", _layer);
            var readout = new DuelCardReadout(CardDatabase.Get(code), live);
            var rating = readout.InspectionRating;
            bool separateReadout = compact && code != 0 && (readout.IsMonster || rating.Kind != DuelRatingKind.None);
            var card = TakeCard(code, code == 0, detailed: !separateReadout, live: live, appearance: link.Appearance);
            card.transform.SetParent(overlay, false);
            ApplyPose(card, initial);

            RectTransform metrics = null;
            if (separateReadout)
            {
                metrics = CenteredVisual("ResolutionReadout", overlay, initial.Position, ReadoutSize(initialHeight));
                var grade = LotdUiKit.CreateRect("Rating", metrics, new Vector2(.035f, .835f), new Vector2(.465f, .985f),
                    Vector2.zero, Vector2.zero);
                new DuelRatingBadge(grade, compact: true).Bind(rating);
                var combat = LotdUiKit.CreateRect("Combat", metrics, new Vector2(.02f, .015f), new Vector2(.98f, .205f),
                    Vector2.zero, Vector2.zero);
                new DuelStatStrip(combat, compact: true).Bind(readout, showModifiers: false);
            }

            var badge = ChainBadge(overlay, BadgePoint(initial), chainNumber, initialHeight * .35f, active: true, resolving: true);
            var rotor = badge.Find("ChainRotor");
            var fade = overlay.gameObject.AddComponent<CanvasGroup>();
            fade.alpha = 0f;
            RectTransform result = null;
            yield return Animate(.36f, t =>
            {
                float appear = Mathf.SmoothStep(0f, 1f, t / .24f);
                fade.alpha = appear * (1f - Mathf.SmoothStep(0f, 1f, (t - .73f) / .27f));
                var pose = ReadPose();
                ApplyPose(card, pose);
                if (metrics != null)
                {
                    metrics.anchoredPosition = pose.Position;
                    metrics.sizeDelta = ReadoutSize(pose.Size.y);
                }
                badge.anchoredPosition = BadgePoint(pose);
                if (rotor != null) rotor.localRotation = Quaternion.Euler(0f, 0f, 94f * t);
                badge.localScale = Vector3.one * (Mathf.Lerp(1.28f, 1f, appear) * pose.Size.y / initialHeight);
                if (t < .42f || !link.Resolved || link.negated) return;
                if (result == null)
                {
                    result = CreateChainOutcomeVisual(overlay, link);
                    _shownChainOutcomes.Add(link);
                }
                ApplyChainOutcomePose(result, pose.Position, pose.Size, Mathf.InverseLerp(.42f, 1f, t));
            });
            overlay.gameObject.SetActive(false);
            if (separateReadout)
            {
                RecycleCard(card);
                card.transform.SetParent(_layer, false);
            }
            RemoveTransient(overlay.gameObject);

            static Vector2 ReadoutSize(float height) => new Vector2(height * 1.02f, height * CompactChainReadoutHeightRatio);
            static Vector2 BadgePoint(CardPose pose) => pose.Position + new Vector2(pose.Size.x * .49f, pose.Size.y * .03f);
        }

        private IEnumerator ChainOutcome(Func<(Vector2 Center, float MaxHeight)> readLayout, ChainLinkInfo link,
            Func<Vector2?> readGrave)
        {
            // A response prompt may have paused this link before its resolution preview could show the outcome.
            if (_shownChainOutcomes.Remove(link)) yield break;
            var card = link.negated ? TakeCard(link.code, link.code == 0, detailed: true,
                live: link.ResolutionReadout ?? link.ActivationReadout, appearance: link.Appearance) : null;
            var cardOpacity = card != null ? card.gameObject.AddComponent<CanvasGroup>() : null;
            var result = CreateChainOutcomeVisual(_layer, link);
            var impact = result.GetComponentInChildren<DuelEffectGraphic>();
            yield return Animate(link.negated ? .48f : .24f, t =>
            {
                var (center, maxHeight) = readLayout();
                float height = Mathf.Min(maxHeight, _layer.rect.height * (link.negated ? .40f : .29f));
                Vector2 size = new Vector2(height * LotdUiKit.CardAspectRatio, height);
                if (card != null)
                {
                    float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 5f));
                    float fadeAt = link.ActivationNegated || link.DestroyedCard ? .32f : .68f;
                    float remaining = 1f - Mathf.SmoothStep(fadeAt, 1f, t);
                    var shake = Vector2.right * (Mathf.Sin(t * 47f) * height * .025f * (1f - settle));
                    ApplyPose(card, new CardPose(center + shake, size * Mathf.Lerp(1.13f, 1f, settle), -2f));
                    cardOpacity.alpha = Mathf.Min(settle * 2f, remaining);
                }
                if (impact.Kind == DuelEffectKind.ChainShatter)
                    impact.ShardTravel = readGrave() - center;
                ApplyChainOutcomePose(result, center, size, t);
            });
            if (card != null) RemoveTransient(card.gameObject);
            RemoveTransient(result.gameObject);
        }

        private RectTransform CreateChainOutcomeVisual(Transform parent, ChainLinkInfo link)
        {
            var visual = CenteredVisual("ChainOutcome", parent, Vector2.zero, Vector2.one);
            var kind = link.negated
                ? (link.ActivationNegated || link.DestroyedCard ? DuelEffectKind.ChainShatter : DuelEffectKind.ChainNegated)
                : link.DestroyedCard ? DuelEffectKind.Break : DuelEffectKind.ChainApplied;
            Color tint = link.negated ? DuelStyle.ChainRed : DuelStyle.ChainGold;
            var effect = CreateEffect(kind, Vector2.zero, Vector2.one, tint, visual);
            effect.rectTransform.anchorMin = Vector2.zero;
            effect.rectTransform.anchorMax = Vector2.one;
            effect.rectTransform.offsetMin = effect.rectTransform.offsetMax = Vector2.zero;
            var label = LotdUiKit.CreateRect("Status", visual, new Vector2(.06f, link.negated ? .16f : .29f),
                new Vector2(.94f, link.negated ? .36f : .49f), Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(label, link.negated ? new Color(.19f, .022f, .045f, .97f) : new Color(.025f, .054f, .075f, .97f));
            LotdUiKit.AddFittedText(label, "Text", link.negated ? "무효 · NEGATED" : link.DestroyedCard ? "파괴" : "효과 처리",
                link.negated ? 30 : 19, 9, TextAnchor.MiddleCenter, tint, FontStyle.Bold);
            if (link.negated) label.localRotation = Quaternion.Euler(0f, 0f, -5f);
            label.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
            return visual;
        }

        private static void ApplyChainOutcomePose(RectTransform visual, Vector2 center, Vector2 cardSize, float progress)
        {
            visual.anchoredPosition = center;
            visual.sizeDelta = new Vector2(cardSize.x * 1.28f, cardSize.y * .88f);
            var effect = visual.GetComponentInChildren<DuelEffectGraphic>();
            effect.Seek(progress);
            var stamp = visual.Find("Status");
            if (effect.Kind is DuelEffectKind.ChainNegated or DuelEffectKind.ChainShatter)
                stamp.localScale = Vector3.one * Mathf.Lerp(1.48f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress * 7f)));
            stamp.GetComponent<CanvasGroup>().alpha = Mathf.Clamp01(Mathf.Min(progress * 7f, (1f - progress) * 5f));
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
            bool active, bool resolving = false, bool opponent = false)
        {
            var holder = CenteredVisual("ChainCount", parent, point, Vector2.one * size);
            Color ink = resolving ? DuelStyle.ChainGold : opponent ? DuelStyle.ChainRed : DuelStyle.ChainCyan;
            if (!active) ink = Color.Lerp(ink, DuelStyle.GlassEdge, .43f);
            DuelStyle.AddChainMedallion(holder, ink, size);
            var frame = active ? resolving ? DuelVisualAssets.ChainResolving : DuelVisualAssets.ChainActive : null;
            frame ??= DuelVisualAssets.ChainFrame;
            if (frame != null)
            {
                var face = CenteredVisual("LocalChainFrame", holder, Vector2.zero, Vector2.one * size * 1.04f);
                LotdUiKit.AddImage(face.gameObject, frame, new Color(1f, 1f, 1f, active ? .72f : .46f), true, false);
            }
            if (active) AddChainRotor(holder, size, resolving);
            var title = LotdUiKit.CreateRect("Caption", holder, new Vector2(.08f, .61f), new Vector2(.92f, .87f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(title, "Text", "CHAIN", Mathf.RoundToInt(size * .24f), 7,
                TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            var digit = LotdUiKit.CreateRect("Number", holder, new Vector2(.12f, .09f), new Vector2(.88f, .65f),
                Vector2.zero, Vector2.zero);
            var label = DuelTypography.AddNumber(digit, "Link", number.ToString(), Mathf.RoundToInt(size * .53f),
                TextAnchor.MiddleCenter, Color.white);
            DuelStyle.Shadow(label);
            return holder;
        }

        private static void AddChainRotor(Transform holder, float size, bool compact)
        {
            var links = DuelVisualAssets.ChainLinks;
            var winding = DuelVisualAssets.ChainWinding;
            if (links == null && winding == null) return;
            if (compact) size *= .48f;
            var rotor = CenteredVisual("ChainRotor", holder, Vector2.zero, Vector2.one * size * 1.66f);
            if (!compact) rotor.SetAsFirstSibling();
            if (winding != null)
                LotdUiKit.AddImage(rotor.gameObject, winding, new Color(.78f, .90f, 1f, compact ? .31f : .68f), true, false);
            if (links == null) return;

            const int segments = 12;
            for (int i = 0; i < segments; i++)
            {
                float angle = (i + .5f) * Mathf.PI * 2f / segments;
                var radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var segment = CenteredVisual("Links", rotor, radial * size * .70f, new Vector2(size * .35f, size * .054f));
                segment.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
                LotdUiKit.AddImage(segment.gameObject, links, new Color(.75f, .90f, 1f, compact ? .58f : .83f), false, false);
            }
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



        private IEnumerator PlayLifeChanges(IReadOnlyList<LifeChangeCue> changes)
        {
            if (!BeginLifeChanges(changes)) yield break;
            yield return Animate(LifeFeedbackDuration, t => UpdateLifeChanges(changes, t));
            EndLifeChanges(changes);
        }

        private bool BeginLifeChanges(IReadOnlyList<LifeChangeCue> changes)
        {
            bool visible = false, damage = false;
            foreach (var change in changes)
            {
                change.Apply?.Invoke();
                if (change.Delta == 0 || change.Anchor == null || !change.Anchor.gameObject.activeInHierarchy) continue;
                var (label, tint) = change.Kind switch
                {
                    LifePointChangeKind.BattleDamage => ("전투 피해", LifeDamageTint),
                    LifePointChangeKind.EffectDamage => ("효과 피해", LifeDamageTint),
                    LifePointChangeKind.Damage => ("피해", LifeDamageTint),
                    LifePointChangeKind.Recovery => ("회복", LotdUiKit.GreenAccent),
                    LifePointChangeKind.Payment => ("LP 지불", DuelStyle.Gold),
                    _ => ("LP 변경", DuelStyle.Muted),
                };
                var holder = CenteredVisual("LPChange", _layer, GetPoint(change.Anchor, Vector2.one * .5f), GetAreaSize(change.Anchor));
                var group = holder.gameObject.AddComponent<CanvasGroup>();
                group.interactable = group.blocksRaycasts = false;
                DuelReadoutPlate.Add(holder, new Color(.048f, .065f, .079f, .99f));
                var rim = LotdUiKit.CreateRect("Accent", holder, new Vector2(.025f, 0f), new Vector2(.975f, 0f),
                    Vector2.zero, new Vector2(0f, 1.35f));
                LotdUiKit.AddImage(rim.gameObject, LotdUiKit.WhiteSprite, tint, false, false);
                var kind = LotdUiKit.CreateRect("Kind", holder, new Vector2(.035f, .04f), new Vector2(.49f, .96f),
                    Vector2.zero, Vector2.zero);
                LotdUiKit.AddFittedText(kind, "Text", label, 17, 9, TextAnchor.MiddleLeft, tint, FontStyle.Bold);
                var value = LotdUiKit.CreateRect("Value", holder, new Vector2(.51f, .025f), new Vector2(.96f, .975f),
                    Vector2.zero, Vector2.zero);
                DuelTypography.AddNumber(value, "Text", change.Delta > 0 ? $"+{change.Delta}" : change.Delta.ToString(),
                    23, TextAnchor.MiddleRight, tint);
                change.Feedback = group;
                if (change.Kind is LifePointChangeKind.BattleDamage or LifePointChangeKind.EffectDamage or
                    LifePointChangeKind.Damage or LifePointChangeKind.Recovery)
                {
                    var sparks = CreateEffect(DuelEffectKind.LifeSparks, Vector2.zero, Vector2.one, tint, holder);
                    var rect = sparks.rectTransform;
                    rect.anchorMin = new Vector2(.12f, -.24f);
                    rect.anchorMax = new Vector2(.95f, 1.24f);
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                    change.Sparks = sparks;
                }
                visible = true;
                damage |= change.Kind is LifePointChangeKind.BattleDamage or LifePointChangeKind.EffectDamage or LifePointChangeKind.Damage;
            }
            if (damage) LotdAudio.PlayDamage();
            return visible;
        }

        private void UpdateLifeChanges(IReadOnlyList<LifeChangeCue> changes, float progress)
        {
            for (int i = 0; i < changes.Count; i++)
            {
                if (changes[i].Impact == null || changes[i].Feedback == null) continue;
                changes[i].Impact(progress);
                break;
            }
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.80f, 1f, progress));
            foreach (var change in changes)
            {
                if (change.Feedback == null || change.Anchor == null) continue;
                var holder = (RectTransform)change.Feedback.transform;
                holder.anchoredPosition = GetPoint(change.Anchor, Vector2.one * .5f);
                holder.sizeDelta = GetAreaSize(change.Anchor);
                change.Feedback.alpha = alpha;
                if (change.Sparks != null) change.Sparks.Seek(Mathf.Clamp01(progress * 1.7f));
            }
        }

        private static void EndLifeChanges(IReadOnlyList<LifeChangeCue> changes)
        {
            foreach (var change in changes)
            {
                if (change.Feedback == null) continue;
                change.Impact?.Invoke(1f);
                RemoveTransient(change.Feedback.gameObject);
            }
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

        private IEnumerator Burst(DuelEffectKind kind, CardPose anchor, float scale, Color color, float duration)
        {
            var effect = CreateEffect(kind, anchor, scale, color);
            yield return Animate(duration, time => TrackCardEffect(effect, anchor, scale, time));
            RemoveTransient(effect.gameObject);
        }

        private DuelEffectGraphic CreateEffect(DuelEffectKind kind, CardPose anchor, float scale, Color color)
        {
            var pose = anchor.AtLayout();
            return CreateEffect(kind, pose.Position, pose.Size * scale, color);
        }

        private static void TrackCardEffect(DuelEffectGraphic effect, CardPose anchor, float scale, float progress)
        {
            var pose = anchor.AtLayout();
            effect.rectTransform.anchoredPosition = pose.Position;
            effect.rectTransform.sizeDelta = pose.Size * scale;
            effect.Seek(progress);
        }

        private DuelEffectGraphic CreateEffect(DuelEffectKind kind, Vector2 center, Vector2 size, Color color,
            Transform parent = null, Sprite texture = null)
        {
            var rect = CenteredVisual(kind.ToString(), parent ?? _layer, center, size);
            var effect = rect.gameObject.AddComponent<DuelEffectGraphic>();
            effect.texture = Texture2D.whiteTexture;
            effect.Kind = kind;
            effect.Tint = color;
            effect.raycastTarget = false;
            effect.ApplyTexture(texture ?? DuelVisualAssets.Effect(kind));
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
