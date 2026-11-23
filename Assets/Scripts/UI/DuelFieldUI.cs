using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DuelFieldUI
    {
        public GameObject Root { get; private set; }

        private readonly GameAppController _app;
        private readonly DuelPromptModals _modals;
        private readonly bool _mobileLayout;

        private DuelSession _session;

        // Left inspector & log
        private CompositeCardView _inspCard;
        private Image _inspRaceIcon;
        private Image _inspAttributeIcon;
        private Text _inspName;
        private Text _inspRulesName;
        private Image _inspTitleAccent;
        private Button _inspectorArtworkButton;
        private Text _inspIdentity;
        private Text _inspStats;
        private DuelRatingBadge _inspRating;
        private DuelPendulumScalePair _inspScales;
        private DuelCounterBadge _inspCounters;
        private DuelStatStrip _inspCombat;
        private Text _inspDesc;
        private Text _logFeed;
        private GameObject _logWindow;
        private ScrollRect _logScroll;
        private readonly List<Text> _logBlocks = new List<Text>();
        private ScrollRect _inspectScroll;
        private uint _inspectedCode;
        private ChainLinkInfo _inspectedActivation;
        private (byte controller, uint location, uint sequence)? _inspectedFieldCard;
        private DeckCardAppearance? _inspectedAppearance;

        // Opponent and player HUD
        private Image _oppBustImg;
        private Text _oppNameText;
        private Text _oppLpText;
        private RectTransform _oppLpFill;
        private Text _myLpText;
        private GameObject _rankedChampionBadge;
        private Image _rankedAvatarRim;
        private Color _normalAvatarRim;
        private RectTransform _myLpFill;
        private Text _turnBadgeText;
        private Text _turnNumber;
        private Text _hintBannerText;
        private Text _chainBannerText;
        private DuelEffectGraphic _responseAttackArrow;
        private Image _chainIcon;
        private Button _chainConfirmationButton;
        private Image _chainDialPulse;
        private Image _chainDialFace;
        private ChainConfirmationMode _chainDialMode;
        private Text _chainConfirmationLabel;
        private RectTransform _chainControlDock;
        private RectTransform _logControlDock;
        private RectTransform _pauseControlDock;
        private RectTransform _placementControlDock;

        // Phase indicator and shortcuts
        private Image _phaseRim;
        private readonly Image[] _phaseSteps = new Image[PhaseEntries.Length];
        private Button _phaseButton;
        private Button _battleShortcut;
        private Button _endShortcut;
        private RectTransform _phaseStrip;
        private GameObject _phasePicker;
        private Text _phaseLabel;
        private Text _phaseControlHint;
        private readonly List<(int phase, Button button, Text label, Image icon)> _phaseChoices = new();
        private Button _phaseCancel;
        private GameObject _placementBanner;
        private Text _placementHint;
        private Button _placementCancel;

        private static readonly Color HudEdge = new Color(.24f, .46f, .51f, .94f);
        private static readonly Color HudSurface = new Color(.025f, .048f, .068f, .965f);
        private static readonly Color HudText = new Color(.94f, .95f, .93f);
        private static readonly Color LifeTrackColor = new Color(.28f, .80f, .87f);
        private static readonly string[] PhaseShortNames = { "DP", "SP", "M1", "BP", "M2", "EP" };
        private readonly Graphic[] _phaseStepMarks = new Graphic[PhaseEntries.Length];
        private static readonly (string name, string label, int phase)[] PhaseEntries =
        {
            ("Draw", "드로우", YgoConstants.PHASE_DRAW),
            ("Standby", "스탠바이", YgoConstants.PHASE_STANDBY),
            ("Main1", "메인 1", YgoConstants.PHASE_MAIN1),
            ("Battle", "배틀", YgoConstants.PHASE_BATTLE),
            ("Main2", "메인 2", YgoConstants.PHASE_MAIN2),
            ("End", "엔드", YgoConstants.PHASE_END),
        };

        private const int FullLifeTrack = 8000;
        private double _myDisplayLp;
        private double _oppDisplayLp;
        private int _myTargetLp;
        private int _oppTargetLp;
        private RectTransform _myLpFeedbackAnchor;
        private RectTransform _oppLpFeedbackAnchor;

        // Zone slot widgets
        private class FieldSlotView
        {
            public RectTransform root;
            public DuelZoneImage bgImg;
            public Outline border;
            public CompositeCardView cardView;
            public RectTransform cardArea;
            public RectTransform focusArt;
            public Image activationGlow;
            public Image unavailableShade;
            public DuelEffectGraphic disabledChains;
            public DuelEffectGraphic linkBeams;
            public DuelEffectGraphic targetChoice;
            public Image focusShadow;
            public DuelFieldReadout readout;
            public CardDragTrigger dragTrigger;
            public Vector2Int? linkCell;
            public byte mappedCon;
            public uint mappedLoc;
            public uint mappedSeq;
            public LiveCardState liveCard;
        }

        private readonly FieldSlotView[] _myMZone = new FieldSlotView[5];
        private readonly FieldSlotView[] _mySZone = new FieldSlotView[6]; // 0..4 s/t, 5 field
        private readonly FieldSlotView[] _oppMZone = new FieldSlotView[5];
        private readonly FieldSlotView[] _oppSZone = new FieldSlotView[6]; // 0..4 s/t, 5 field
        private readonly FieldSlotView[] _emz = new FieldSlotView[2]; // [0]=Human seq5 (Opp seq6), [1]=Human seq6 (Opp seq5)
        private readonly FieldSlotView[,] _linkGrid = new FieldSlotView[5, 5];
        private uint _highlightedLinkCells;

        // Stack counters (Deck / Extra / GY / Banished)
        private class StackTileView
        {
            public RectTransform root;
            public RectTransform cardArea;
            public Text label;
            public Text count;
            public DuelZoneImage background;
            public CompositeCardView cardView;
            public DuelPileWell well;
            public Graphic wellGlyph;
            public DuelPileDepth depth;
            public Image activationGlow;
            public Button button;
            public byte controller;
            public uint location;
        }

        private StackTileView _myDeckTile, _myExtraTile, _myGyTile, _myBanTile;
        private StackTileView _oppDeckTile, _oppExtraTile, _oppGyTile, _oppBanTile;
        private Button _extraSummonButton;
        private Button _graveEffectButton;
        private Button _extraEffectButton;
        private Button _banishedEffectButton;

        // High-res Card Zoom Modal
        private GameObject _cardZoomModal;
        private Button _zoomCloseButton;
        private GameObject _focusBeforeZoom;
        private CompositeCardView _zoomCardView;
        private GameObject _zoomArtworkControls;
        private Text _zoomArtworkLabel;
        private Text _zoomName;
        private Text _zoomRulesName;
        private Text _zoomStats;
        private DuelRatingBadge _zoomRating;
        private DuelPendulumScalePair _zoomScales;
        private DuelCounterBadge _zoomCounters;
        private DuelStatStrip _zoomCombat;
        private (byte controller, uint location, uint sequence)? _zoomFieldCard;
        private DeckCardAppearance? _zoomAppearance;
        private Text _zoomPasscode;
        private Text _zoomCounterTypes;
        private Text _zoomDesc;
        private RectTransform _zoomDescriptionBox;
        private ScrollRect _zoomScroll;
        private Image _zoomRaceIcon;
        private Image _zoomAttributeIcon;

        // Multi-place selection accumulator (for MSG_SELECT_PLACE / MSG_SELECT_DISFIELD with min > 1)
        private readonly List<(byte con, uint loc, uint seq)> _pendingPlaces = new List<(byte con, uint loc, uint seq)>();
        private DuelPrompt _pendingPlacePrompt;
        private (DuelPrompt prompt, int command, uint code, uint location, uint availableZones, RectTransform art)? _stagedPlacement;

        // Drag-to-zone & in-flight animation state
        private (byte con, uint loc, uint seq)? _preferredDropZone;
        private int _preferredAttackTargetSeq = -1;
        private sealed class CardArrival
        {
            internal readonly (byte Controller, uint Location, uint Sequence) Slot;
            internal BattleCardDisplay Departing;

            internal CardArrival((byte, uint, uint) slot) => Slot = slot;
        }

        private readonly Dictionary<(byte Controller, uint Location, uint Sequence), List<CardArrival>> _arrivingCards = new();
        private readonly Dictionary<(byte Controller, uint Location, uint Sequence), CardMoveReadoutSnapshot> _pendingCardReadouts = new();
        private const uint FieldMaterials = YgoConstants.LOCATION_MZONE | YgoConstants.LOCATION_OVERLAY;
        private readonly Dictionary<(byte Controller, uint Sequence), MaterialReadout> _materialReadouts = new();
        private readonly Dictionary<(byte Controller, uint Location, uint Sequence), PresentedFieldReadout> _presentedFieldReadouts = new();

        private sealed class MaterialReadout
        {
            internal LiveCardState Source;
            internal int Count;
            internal int Pending;
        }
        private readonly Dictionary<(byte Controller, uint Sequence), List<(SummonMaterialFlight Move, uint Index)>>
            _pendingXyzMaterials = new();
        private bool _dragHighlightMZone;
        private bool _dragHighlightSZone;
        private bool _dragHighlightAttack;
        private RectTransform _boardRect;
        private Vector2 _boardLayoutSize;
        private DuelMat _mat;

        // Hand rows
        private RectTransform _myHandBar;
        private RectTransform _myHandContent;
        private ScrollRect _myHandScroll;
        private RectMask2D _myHandMask;
        private RectTransform _oppHandBar;
        private readonly int[] _handCounts = new int[2];
        private readonly List<HandCardView> _myHandViews = new List<HandCardView>();
        private readonly List<HandCardView> _oppHandViews = new List<HandCardView>();
        private Vector2 _myHandSize;
        private Vector2 _oppHandSize;

        private sealed class HandCardView
        {
            public RectTransform Rect;
            public CompositeCardView Card;
            public RectTransform FocusArt;
            public Image PlayableGlow;
            public Image UnavailableShade;
            public CardDragTrigger Trigger;
            public (Vector2 min, Vector2 max, float angle) Layout;
            public bool HasLayout;
            public object PendingArrival;
        }

        private sealed class PresentedFieldReadout
        {
            internal LiveCardState Shown;
        }

        private DuelPrompt _handGlowPrompt;

        // Context Action Popup Menu
        private GameObject _actionPopup;
        private RectTransform _actionList;
        private RectTransform _actionWindow;
        private Text _actionHeader;
        private Vector2 _actionWindowTarget;
        private Vector2 _actionWindowOffset;
        private float _actionWindowStart = -1f;

        public bool IsPresenting => CardAnimationManager.Instance != null && CardAnimationManager.Instance.IsPresenting;
        public bool IsPaused { get; private set; }
        public bool IsReadingLog => _logWindow.activeSelf;
        public bool IsBrowsingZone => _modals.IsZoneViewerOpen;
        internal bool IsReadingCardDetails => _cardZoomModal.activeSelf;
        internal static float PlaybackRate => Application.platform == RuntimePlatform.IPhonePlayer ? .75f : 1f;
        internal static readonly Rect BattleStatsArea = Rect.MinMaxRect(.015f, .135f, .186f, .193f);
        internal static readonly Rect MobileBattleStatsArea = Rect.MinMaxRect(.015f, .135f, .221f, .193f);
        private static readonly Rect MobileChainResolutionArea = Rect.MinMaxRect(.897f, .638f, .984f, .872f);
        private bool CanAcceptPlayerAction => _session != null && !_session.IsFinished && !IsPresenting && !IsPaused && !IsReadingLog &&
            _session.ActivePrompt?.playerId == _session.HumanPlayerIndex;

        public DuelFieldUI(GameAppController app, Transform canvasParent, DuelPromptModals modals)
            : this(app, canvasParent, modals, Application.platform == RuntimePlatform.IPhonePlayer)
        {
        }

        internal DuelFieldUI(GameAppController app, Transform canvasParent, DuelPromptModals modals, bool mobileLayout)
        {
            _app = app;
            _modals = modals;
            _mobileLayout = mobileLayout;
            BuildUI(canvasParent);
            _modals.SetFieldCardHitTest(IsPointOverFieldCardChoice);
            _modals.SetActivationHitTest(IsPointOverEffectSource);
            _modals.SetInspectorControls(_inspectorArtworkButton, _duelPadInspectButton, (RectTransform)_inspectScroll.transform);
            _modals.SetCandidateInspector(InspectCandidate);
            _modals.SetPileActivationControls(_graveEffectButton, _extraEffectButton, _banishedEffectButton);
            _modals.SetBrowsablePiles(
                _myGyTile.root.GetComponent<DuelZoneImage>(), _myExtraTile.root.GetComponent<DuelZoneImage>(),
                _myBanTile.root.GetComponent<DuelZoneImage>(), _oppGyTile.root.GetComponent<DuelZoneImage>(),
                _oppExtraTile.root.GetComponent<DuelZoneImage>(), _oppBanTile.root.GetComponent<DuelZoneImage>());
            _modals.SetZoneBrowserActions(SwitchBrowsablePile,
                (code, live) => { if (live == null) ShowCardZoomModal(code); else ShowCardZoomModal(live); },
                live => InspectCardAt(live.code, null, appearance: live.Appearance));

            if (CardDragManager.Instance != null)
            {
                CardDragManager.Instance.OnDragStarted += HandleDuelDragStarted;
                CardDragManager.Instance.OnDragDropped += HandleDuelCardDrop;
                CardDragManager.Instance.OnDragCancelled += HandleDuelDragCancelled;
            }
        }

        public void BindSession(DuelSession session, OpponentCharacter opponent, PresetDeckInfo opponentDeck)
        {
            UnbindSession();
            _session = session;
            session.OnCardLocationsChanged += HandleCardLocationsChanged;
            session.OnFieldArrivalBoundary += CompleteFieldArrivals;
            session.OnStyledCardsDrawn += HandleCardsDrawn;
            session.OnDestinyDraw += HandleDestinyDraw;
            session.OnLifePointChange += HandleLifePointsChanged;
            session.OnTurnStarted += HandleTurnStarted;
            session.OnPhaseChanged += HandlePhaseChanged;
            session.OnChainActivated += HandleChainActivatedEvent;
            session.OnCardsTargeted += HandleCardsTargeted;
            session.OnChainResolving += HandleChainResolvingEvent;
            session.OnChainResolved += HandleChainResolvedEvent;
            session.OnFieldReadoutsChanged += QueueFieldReadouts;
            session.OnCardEquipped += HandleCardEquipped;
            session.OnAttackDeclared += HandleAttackEvent;
            session.OnBattleResolved += HandleBattleResolved;
            session.OnCoinTossed += HandleCoinTossedEvent;
            session.OnDiceRolled += HandleDiceRolledEvent;

            Root.SetActive(true);
            _oppNameText.text = opponent?.name ?? opponentDeck?.title ?? "AI 듀얼리스트";
            bool champion = CollectionProgress.IsRankedChampion;
            _rankedChampionBadge.SetActive(champion);
            _rankedAvatarRim.color = champion ? DuelUiKit.GoldAccent : _normalAvatarRim;
            _oppBustImg.sprite = DuelUiKit.LoadOpponentPortrait(opponent);
            _oppBustImg.enabled = _oppBustImg.sprite != null;
            SetTurnCaption(Mathf.Max(session.TurnNumber, 1), session.TurnPlayer != session.HumanPlayerIndex);
            SetPhasePlate(session.TurnNumber == 0 ? YgoConstants.PHASE_DRAW : session.CurrentPhase,
                session.TurnPlayer != session.HumanPlayerIndex);
            _hintBannerText.text = "";
            _chainIcon.gameObject.SetActive(false);
            _chainBannerText.text = "";
            _myDisplayLp = _myTargetLp = Mathf.Max(0, session.Players[session.HumanPlayerIndex].lp);
            _oppDisplayLp = _oppTargetLp = Mathf.Max(0, session.Players[session.AiPlayerIndex].lp);
            WriteLifeReadout(_myLpText, _myLpFill, _myTargetLp);
            WriteLifeReadout(_oppLpText, _oppLpFill, _oppTargetLp);
            _myLpText.color = _oppLpText.color = HudText;
            Canvas.ForceUpdateCanvases();
            Refresh();
            var animator = CardAnimationManager.Instance;
            if (session.NativePtr == IntPtr.Zero && animator != null)
                animator.QueueDuelStart(animator.GetPoint(_boardRect, new Vector2(0.5f, 0.53f)));
        }

        public void UnbindSession()
        {
            if (_session != null)
            {
                _session.OnCardLocationsChanged -= HandleCardLocationsChanged;
                _session.OnFieldArrivalBoundary -= CompleteFieldArrivals;
                _session.OnStyledCardsDrawn -= HandleCardsDrawn;
                _session.OnDestinyDraw -= HandleDestinyDraw;
                _session.OnLifePointChange -= HandleLifePointsChanged;
                _session.OnTurnStarted -= HandleTurnStarted;
                _session.OnPhaseChanged -= HandlePhaseChanged;
                _session.OnChainActivated -= HandleChainActivatedEvent;
                _session.OnCardsTargeted -= HandleCardsTargeted;
                _session.OnChainResolving -= HandleChainResolvingEvent;
                _session.OnChainResolved -= HandleChainResolvedEvent;
                _session.OnFieldReadoutsChanged -= QueueFieldReadouts;
                _session.OnCardEquipped -= HandleCardEquipped;
                _session.OnAttackDeclared -= HandleAttackEvent;
                _session.OnBattleResolved -= HandleBattleResolved;
                _session.OnCoinTossed -= HandleCoinTossedEvent;
                _session.OnDiceRolled -= HandleDiceRolledEvent;
                _session = null;
            }

            ClearArrivals();
            _arrivalLinks.Clear();
            _revealedImpermanence = null;
            ClearColumnNegations();
            SetDragColumnGuide(-1);
            _pendingXyzMaterials.Clear();
            Array.Clear(_handCounts, 0, _handCounts.Length);
            _pendingPlaces.Clear();
            _pendingPlacePrompt = null;
            _stagedPlacement = null;
            _preferredDropZone = null;
            _preferredAttackTargetSeq = -1;
            _dragHighlightMZone = _dragHighlightSZone = _dragHighlightAttack = false;
            ResetDuelPad();
            _pointerActionAnchor = null;
            HideCardActions();
            HideActivationNotice();
            ShakeBoardForDamage(1f);
            _extraSummonButton.gameObject.SetActive(false);
            _graveEffectButton.gameObject.SetActive(false);
            _banishedEffectButton.gameObject.SetActive(false);
            _extraEffectButton.gameObject.SetActive(false);
            CloseActionPopup();
            CloseDuelLog();
            CloseCardZoomModal(restoreFocus: false);
            _modals.CloseZoneViewer();
            ResetCardInspector();
            _phasePicker.SetActive(false);
            _responseAttackArrow.gameObject.SetActive(false);
            _placementBanner.SetActive(false);
            _placementCancel.transform.parent.gameObject.SetActive(false);
            CardAnimationManager.Instance?.Clear();
            RefreshHandGlows(force: true);
        }

        public void InspectCard(uint code) => InspectCardAt(code, null);

        private void InspectCard(uint code, FieldSlotView slot) => InspectCardAt(code, InspectionLocation(slot));

        private void InspectCandidate(SelectableCardItem item, uint code) =>
            InspectCardAt(code, (item.controller, item.location, item.sequence),
                appearance: _session?.GetDeckAppearance(item.controller, item.location, item.sequence));

        private DeckCardAppearance? DefaultDuelAppearance =>
            _session?.UsesDeckAppearances == true ? default(DeckCardAppearance) : null;

        private void InspectCardAt(uint code, (byte controller, uint location, uint sequence)? fieldCard,
            ChainLinkInfo activation = null, DeckCardAppearance? appearance = null)
        {
            if (code == 0) return;
            _inspectedBanishedCard = null;
            bool changed = code != _inspectedCode || !ReferenceEquals(_inspectedActivation, activation);
            bool sameSource = !changed && _inspectedFieldCard == fieldCard;
            _inspectedCode = code;
            _inspectedActivation = activation;
            _inspectedFieldCard = fieldCard;
            _inspectedAppearance = appearance ?? activation?.Appearance;
            if (changed) _inspCard.gameObject.SetActive(true);
            RefreshInspectorReadout(showChanges: sameSource);
            RefreshFieldRelationships();
            if (!changed) return;

            _duelPadInspectButton.interactable = true;
            _inspectorArtworkButton.interactable = true;
            var info = CardDatabase.Get(code);
            _inspName.text = info?.name ?? $"카드 #{code}";
            string rules = info?.desc ?? "";
            if (activation != null)
            {
                rules = CardDatabase.GetKoreanCardDescription(code) ?? rules;
                string effect = CardDatabase.ResolveEffectDescription(activation.description, code);
                string activationLabel = $"상대 발동 · 체인 {activation.chainNumber}";
                if (!string.IsNullOrWhiteSpace(effect) && !string.Equals(effect.Trim(), rules.Trim(), StringComparison.Ordinal))
                    rules = string.IsNullOrWhiteSpace(rules) ? effect : $"{effect}\n\n카드 설명\n{rules}";
                rules = $"{activationLabel}\n{rules}";
            }
            _inspDesc.text = rules;
            _inspectScroll.StopMovement();
            _inspectScroll.content.anchoredPosition = Vector2.zero;
        }

        private void ResetCardInspector()
        {
            _inspectedCode = 0;
            _inspectedActivation = null;
            _inspectedFieldCard = null;
            _inspectedAppearance = null;
            _inspectedBanishedCard = null;
            _inspCard.SetCard(0);
            _inspCard.gameObject.SetActive(false);
            _duelPadInspectButton.interactable = false;
            _inspectorArtworkButton.interactable = false;
            _inspTitleAccent.color = InspectorTitleAccent(0);
            _inspName.text = "카드 정보";
            BindRulesName(_inspName, _inspRulesName, null);
            BindInspectorStats(default, showChanges: false);
            _inspCombat.Hide();
            _inspScales.Bind(default, showChanges: false);
            SizeInspectorDescription(hasCombat: false);
            _inspCounters.Bind(default);
            SetLinkedZones(null);
            RefreshEquipmentHighlights();
            _inspDesc.text = "카드를 선택하면 효과를 확인할 수 있습니다.";
            _inspRaceIcon.gameObject.SetActive(false);
            _inspAttributeIcon.gameObject.SetActive(false);
            _inspectScroll.StopMovement();
            _inspectScroll.content.anchoredPosition = Vector2.zero;
        }

        private static (byte controller, uint location, uint sequence)? InspectionLocation(FieldSlotView slot) =>
            slot == null ? null : (slot.mappedCon, slot.mappedLoc, slot.mappedSeq);

        private LiveCardState ReadVisibleFieldCard(uint code, (byte controller, uint location, uint sequence)? location) =>
            location is { } slot ? _session?.GetVisibleFieldCard(code, slot.controller, slot.location, slot.sequence) : null;

        private void HandleCardLocationsChanged(CardMoveEvent move, CardMoveEvent swapPartner)
        {
            ObserveImpermanenceOrigin(move);
            ObserveImpermanenceOrigin(swapPartner);
            var readout = ReadoutBeforeMove(move);
            var partnerReadout = ReadoutBeforeMove(swapPartner);
            ClearMovedReadouts(move);
            if (swapPartner != null) ClearMovedReadouts(swapPartner);
            _pendingCardReadouts.Remove((move.fromCon, move.fromLoc, move.fromSeq));
            if (swapPartner != null) _pendingCardReadouts.Remove((swapPartner.fromCon, swapPartner.fromLoc, swapPartner.fromSeq));

            BattleCardDisplay held = null;
            BattleCardDisplay partnerHeld = null;
            if (swapPartner == null || !TryQueueFieldSwap(move, swapPartner, readout, partnerReadout))
            {
                held = HandleCardMovedEvent(move, readout);
                if (swapPartner != null) partnerHeld = HandleCardMovedEvent(swapPartner, partnerReadout);
            }
            var before = _inspectedFieldCard;
            FollowTemporaryBanishment(move);
            _inspectedFieldCard = FollowFieldCard(_inspectedFieldCard, _inspectedCode, move, swapPartner, held, partnerHeld);
            if (_zoomFieldCard.HasValue)
                _zoomFieldCard = FollowFieldCard(_zoomFieldCard, _zoomCardView.CurrentCode, move, swapPartner, held, partnerHeld);

            if (before is { } source && before != _inspectedFieldCard &&
                GetSlot(source.controller, source.location, source.sequence) is { } slot)
            {
                ClearPointerCard(slot.focusArt);
                if (_usingDuelPad && (_duelPadBoardFocus == slot.root || _duelPadPendingFieldFocus == slot.root))
                    _duelPadPendingFieldFocus = _inspectedFieldCard is { } destination
                        ? GetSlot(destination.controller, destination.location, destination.sequence)?.root : null;
            }
        }

        private PresentedFieldReadout ReadoutBeforeMove(CardMoveEvent move)
        {
            if (move?.SourceReadout == null ||
                !_presentedFieldReadouts.TryGetValue((move.fromCon, move.fromLoc, move.fromSeq), out var readout)) return null;
            return readout.Shown.code == move.code ? readout : null;
        }

        private static (byte controller, uint location, uint sequence)? FollowFieldCard(
            (byte controller, uint location, uint sequence)? inspected, uint code, CardMoveEvent move, CardMoveEvent swapPartner,
            BattleCardDisplay held, BattleCardDisplay partnerHeld)
        {
            if (inspected is not { } source) return null;
            if (source != (move.fromCon, move.fromLoc, move.fromSeq))
            {
                if (swapPartner == null || source != (swapPartner.fromCon, swapPartner.fromLoc, swapPartner.fromSeq))
                    return source;
                move = swapPartner;
                held = partnerHeld;
            }
            if (move.code != code) return null;
            if (move.toLoc is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE)
                return (move.toPos & YgoConstants.POS_FACEUP) != 0 || move.ChangesPositionInPlace
                    ? (move.toCon, move.toLoc, move.toSeq) : null;
            return held?.Readout != null ? source : null;
        }

        private DuelCardReadout ReadInspectedStats(uint code, (byte controller, uint location, uint sequence)? location)
        {
            if (_modals.ReadInspectedSummon(code, location) is { } summon) return summon;
            if (location is { } slot)
            {
                if (_movingInspections.TryGetValue(slot, out var moving) && moving.Move.Code == code)
                    return new DuelCardReadout(CardDatabase.Get(code), moving.Shown);
                if (GetBattleCardDisplay(slot.controller, slot.sequence, slot.location) is { } held)
                {
                    if (held.Readout?.Shown is { } shown && shown.code == code)
                        return new DuelCardReadout(CardDatabase.Get(code),
                            held.AwaitingArrival || held.Hidden && !held.InspectWhileHidden ? null : shown);
                    if (held.PositionChanging) return new DuelCardReadout(CardDatabase.Get(code));
                }
            }
            return ReadFieldStats(code, ReadVisibleFieldCard(code, location));
        }

        private bool IsInspectedCardStillOnField(uint code, (byte controller, uint location, uint sequence)? location) =>
            location is { } slot && (slot.location is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE) &&
            FindLiveCard(slot.controller, slot.location, slot.sequence)?.code == code;

        private void RefreshInspectorReadout(bool showChanges = true)
        {
            if (_inspectedCode == 0) return;
            var card = ReadInspectedStats(_inspectedCode, _inspectedFieldCard);
            if (card.Live == null && !IsInspectedCardStillOnField(_inspectedCode, _inspectedFieldCard)) _inspectedFieldCard = null;
            _inspCard.SetDeckCard(_inspectedCode, card.Live?.Appearance ?? _inspectedAppearance ?? DefaultDuelAppearance, live: card.Live);
            _inspTitleAccent.color = InspectorTitleAccent(card.Type);
            BindRulesName(_inspName, _inspRulesName, card.RulesName);
            BindInspectorStats(card, showChanges && card.Live != null);
            BindIdentityIcons(card, _inspRaceIcon, _inspAttributeIcon);
            _inspCounters.Bind(card, showChanges);
            _inspCombat.Bind(card, card.Live != null && card.Live.controller != _session.HumanPlayerIndex,
                showChanges && card.Live != null);
            bool hasScales = _inspScales.Bind(card, showChanges && card.Live != null);
            SizeInspectorDescription(card.IsMonster, hasScales);
        }

        private void BindInspectorStats(DuelCardReadout card, bool showChanges)
        {
            var (identity, details) = FormatInspectorStats(card, compact: true);
            var rating = card.InspectionRating;
            bool hasRating = rating.Kind != DuelRatingKind.None;
            _inspIdentity.text = identity;
            _inspIdentity.rectTransform.anchorMax = new Vector2(hasRating ? .665f : .982f, .965f);
            _inspStats.text = details;
            _inspectorDisabledBadge.SetActive(card.Disabled);
            var info = (RectTransform)_inspStats.transform.parent;
            info.anchorMax = new Vector2(1f, identity.Length != 0 || hasRating ? .49f : 1f);
            _inspRating.Bind(rating, showChanges);
        }

        private void SizeInspectorDescription(bool hasCombat, bool hasScales = false)
        {
            var description = (RectTransform)_inspectScroll.transform;
            description.anchorMax = new Vector2(description.anchorMax.x, hasScales ? .214f : hasCombat ? .275f : .337f);
        }

        private static Text CreateRulesNameLabel(Transform parent, int size)
        {
            var label = DuelUiKit.AddFittedText(parent, "CurrentName", "", size, 9, TextAnchor.MiddleLeft, DuelStyle.Muted);
            label.rectTransform.anchorMax = new Vector2(label.rectTransform.anchorMax.x, .445f);
            label.gameObject.SetActive(false);
            return label;
        }

        private static void BindRulesName(Text title, Text label, string name)
        {
            bool visible = !string.IsNullOrEmpty(name);
            label.text = visible ? $"현재 카드명 · {name}" : "";
            label.gameObject.SetActive(visible);
            title.rectTransform.anchorMin = new Vector2(title.rectTransform.anchorMin.x, visible ? .455f : .035f);
        }

        private static Color InspectorTitleAccent(uint type)
        {
            if (type == 0) return DuelStyle.Muted;
            if ((type & YgoConstants.TYPE_SPELL) != 0) return new Color(.42f, .89f, .75f);
            if ((type & YgoConstants.TYPE_TRAP) != 0) return new Color(.98f, .60f, .84f);
            if ((type & YgoConstants.TYPE_LINK) != 0) return DuelStyle.Player;
            return DuelStyle.Gold;
        }

        private bool CanShowFieldRelationships => _session != null && !_session.IsFinished && !IsPresenting &&
            !IsPaused && !IsReadingLog && !IsReadingCardDetails && !_phasePicker.activeSelf && !IsStagingPlacement &&
            !IsNativePlacement(_session.ActivePrompt) && !_modals.IsPromptOpen && !_modals.IsZoneViewerOpen &&
            CardDragManager.Instance?.IsDragging != true;

        private void RefreshFieldRelationships()
        {
            SetLinkedZones(GetSelectedLink());
            RefreshEquipmentHighlights();
        }

        private (Vector2Int Origin, uint Markers, bool Opponent)? GetSelectedLink()
        {
            if (!CanShowFieldRelationships || _inspectedFieldCard is not { } source)
                return null;

            var card = ReadVisibleFieldCard(_inspectedCode, source);
            var slot = GetSlot(source.controller, source.location, source.sequence);
            if (card == null || (card.type & YgoConstants.TYPE_LINK) == 0 || card.linkMarker == 0 ||
                slot?.linkCell is not { } origin)
                return null;

            return (origin, card.linkMarker, source.controller != _session.HumanPlayerIndex);
        }

        private FieldSlotView GetEquipmentFocus()
        {
            if (!CanShowFieldRelationships || _actionPopup.activeSelf || (_usingDuelPad && _duelPadScope != PadScope.Board))
                return null;
            var anchor = _usingDuelPad ? _duelPadFocus : _touchActionAnchor;
            if (anchor != null) return _fieldSlotsByRect.TryGetValue(anchor, out var slot) ? slot : null;
            return _usingDuelPad ? null : _duelPointerSlot;
        }

        private void RefreshEquipmentHighlights()
        {
            var focus = GetEquipmentFocus();
            var card = VisibleBoardCard(focus);
            (byte Controller, uint Sequence)? focusedMonster = card?.location == YgoConstants.LOCATION_MZONE
                ? (card.controller, card.sequence) : null;
            FieldSlotView equippedMonster = null;
            if (card?.EquipTarget is { } attachment)
            {
                var target = GetSlot(attachment.Controller, YgoConstants.LOCATION_MZONE, attachment.Sequence);
                if (VisibleBoardCard(target) is { } host && host.controller == attachment.Controller &&
                    host.sequence == attachment.Sequence)
                    equippedMonster = target;
            }

            bool found = false;
            foreach (var slot in _fieldSlotsByRect.Values)
            {
                if (slot == focus) continue;
                bool related = slot == equippedMonster || (focusedMonster.HasValue &&
                    slot.mappedLoc == YgoConstants.LOCATION_SZONE && VisibleBoardCard(slot)?.EquipTarget == focusedMonster);
                slot.bgImg.SetEquipmentHighlight(related);
                found |= related;
            }
            focus?.bgImg.SetEquipmentHighlight(found);
        }

        private static LiveCardState VisibleBoardCard(FieldSlotView slot) =>
            slot?.liveCard is { IsFaceUp: true, code: not 0 } card && slot.cardView.gameObject.activeInHierarchy ? card : null;

        private void SetLinkedZones((Vector2Int Origin, uint Markers, bool Opponent)? source)
        {
            uint previous = _highlightedLinkCells;
            _highlightedLinkCells = 0;
            if (source is { } link)
            {
                var arrows = DuelLinkCompass.Marks;
                int facing = link.Opponent ? -1 : 1;
                for (int i = 0; i < arrows.Length; i++)
                {
                    var arrow = arrows[i];
                    if ((link.Markers & arrow.mask) == 0) continue;
                    var direction = arrow.direction * facing;
                    var target = link.Origin + new Vector2Int(direction.x, -direction.y);
                    if ((uint)target.x >= 5 || (uint)target.y >= 5 || _linkGrid[target.x, target.y] is not { } slot)
                        continue;

                    var neighbor = slot.liveCard;
                    bool sameFacing = (slot.mappedCon != _session.HumanPlayerIndex) == link.Opponent;
                    uint backMarker = arrows[sameFacing ? arrows.Length - 1 - i : i].mask;
                    bool coLinked = neighbor?.IsFaceUp == true && (neighbor.type & YgoConstants.TYPE_LINK) != 0 &&
                        (neighbor.linkMarker & backMarker) != 0;
                    slot.bgImg.SetLinkHighlight(coLinked ? DuelLinkHighlight.CoLinked : DuelLinkHighlight.Linked,
                        neighbor == null ? direction : Vector2Int.zero);
                    _highlightedLinkCells |= 1u << (target.y * 5 + target.x);
                }
            }

            uint cleared = previous & ~_highlightedLinkCells;
            if (cleared == 0) return;
            for (int row = 0; row < 5; row++)
            for (int column = 0; column < 5; column++)
            {
                uint bit = 1u << (row * 5 + column);
                if ((cleared & bit) != 0) _linkGrid[column, row].bgImg.SetLinkHighlight(DuelLinkHighlight.None);
            }
        }

        private static void BindIdentityIcons(DuelCardReadout card, Image raceIcon, Image attributeIcon)
        {
            Sprite race = null;
            Sprite attribute = null;
            if (card.IsMonster)
            {
                race = DuelUiKit.GetRaceSprite(card.Race);
                attribute = DuelUiKit.GetAttributeSprite(card.Attribute);
            }
            else if ((card.Type & (YgoConstants.TYPE_SPELL | YgoConstants.TYPE_TRAP)) != 0)
                race = DuelUiKit.GetSpellTrapSprite(card.Type);
            raceIcon.sprite = race;
            raceIcon.gameObject.SetActive(race != null);
            attributeIcon.sprite = attribute;
            attributeIcon.gameObject.SetActive(attribute != null);
        }

        private static (string Identity, string Details) FormatInspectorStats(DuelCardReadout card, bool compact = false)
        {
            if (card.Type == 0) return ("", "");
            string type = card.IsScale ? "펜듈럼 마법" :
                card.IsMonster && (card.Type & YgoConstants.TYPE_TRAP) != 0 ? "함정 몬스터" :
                YgoConstants.GetTypeTagKR(card.Type).Replace(" / ", " · ");
            if (card.InspectionRating.Kind == DuelRatingKind.Scale) return (type, "");
            if (!card.IsMonster) return ("", type);

            if (!YgoConstants.RaceNamesKR.TryGetValue(card.Race, out string race))
                race = string.Join("/", YgoConstants.RaceNamesKR.Where(pair => (pair.Key & card.Race) != 0).Select(pair => pair.Value));
            if (!YgoConstants.AttributeNamesKR.TryGetValue(card.Attribute, out string attribute))
                attribute = string.Join("/", YgoConstants.AttributeNamesKR.Where(pair => (pair.Key & card.Attribute) != 0).Select(pair => pair.Value));
            if (compact && race.Contains('/')) race = "복수 종족";
            if (compact && attribute.Length > 8) attribute = "복수 속성";
            string identity = string.Join(" · ", new[] { race, attribute }.Where(part => !string.IsNullOrEmpty(part)));
            if (card.ShowsSummonPreview) return (identity, $"{type} · 표시 형식 선택");
            if (card.Live == null) return (identity, type);

            string position = card.Live.IsDefense ? "수비 표시" : "공격 표시";
            string status = card.Status;
            if (status.Length > 0) position += " · " + status;
            return (identity, $"{type} · {position}");
        }

        private void BuildUI(Transform parent)
        {
            var root = DuelUiKit.FillRect("Screen_DuelField", parent);
            Root = root.gameObject;

            var bg = DuelUiKit.FillRect("ArenaGround", root);
            var ground = bg.gameObject.AddComponent<DuelArenaGround>();
            ground.texture = DuelVisualAssets.ArenaBackdrop ?? DuelStyle.Stone;
            ground.raycastTarget = false;

            var playArea = _mobileLayout ? MobileSafeArea.Create(root) : root;
            BuildCenterBoard(playArea);
            var arrow = DuelUiKit.CreateRect("ResponseAttackArrow", playArea, Vector2.one * .5f, Vector2.one * .5f,
                Vector2.zero, Vector2.zero);
            _responseAttackArrow = arrow.gameObject.AddComponent<DuelEffectGraphic>();
            _responseAttackArrow.texture = Texture2D.whiteTexture;
            _responseAttackArrow.Kind = DuelEffectKind.AttackPath;
            _responseAttackArrow.Tint = DuelStyle.ChainRed;
            _responseAttackArrow.raycastTarget = false;
            _responseAttackArrow.Seek(.66f);
            arrow.gameObject.SetActive(false);
            BuildLeftPanel(playArea);
            BuildTopHud(playArea);
            BuildBottomHudAndHands(playArea);
            BuildCardActions(playArea);
            BuildActionPopup(playArea);
            BuildCardZoomModal(root, _mobileLayout);
            BuildDuelLog(playArea);
            BuildActivationNotice(playArea);
            Root.SetActive(false);
        }

        private void BuildLeftPanel(Transform root)
        {
            var left = CreateInspectorSurface(root, "LeftInspector", new Vector2(.015f, .195f),
                new Vector2(0.186f, 0.922f));
            var inside = DuelUiKit.FillRect("CardDetails", left, 9f);
            var card = DuelUiKit.CreateRect("CardPreview", left,
                new Vector2(.055f, .483f), new Vector2(.945f, .901f), Vector2.zero, Vector2.zero);
            _inspCard = CompositeCardView.Create(card, "Card");
            _inspCard.UsePrintedFaceOnly();
            var aspect = _inspCard.transform.Find("AspectBox");
            AddInspectorArtworkFrame(aspect);
            _inspectorDisabledBadge = BuildDisabledBadge(_inspCard.transform, 17);
            var zoomHit = DuelUiKit.FillRect("ZoomHit", aspect);
            var hitImage = DuelUiKit.AddImage(zoomHit.gameObject, DuelUiKit.WhiteSprite, Color.clear);
            CardOverframeInput.Attach(zoomHit, _inspCard, motionSensor: false);
            _inspectorArtworkButton = zoomHit.gameObject.AddComponent<Button>();
            _inspectorArtworkButton.targetGraphic = hitImage;
            _inspectorArtworkButton.transition = Selectable.Transition.None;
            _inspectorArtworkButton.navigation = new Navigation { mode = Navigation.Mode.None };
            _inspectorArtworkButton.onClick.AddListener(() =>
            {
                DuelAudio.PlayClick();
                OpenInspectedCardDetails();
            });

            var artworkControls = DuelUiKit.CreateRect("ArtworkControls", inside, new Vector2(.02f, .425f),
                new Vector2(.98f, .477f), Vector2.zero, Vector2.zero);
            if (_mobileLayout)
            {
                _duelPadInspectButton = _inspectorArtworkButton;
                var cue = DuelUiKit.CreateRect("DetailsCue", zoomHit, new Vector2(.10f, .015f), new Vector2(.90f, .12f),
                    Vector2.zero, Vector2.zero);
                DuelReadoutPlate.Add(cue, new Color(.02f, .05f, .07f, .94f));
                DuelUiKit.AddFittedText(cue, "Label", "상세 보기", 16, 9, TextAnchor.MiddleCenter, HudText);
            }
            else
            {
                var details = DuelUiKit.CreateRect("Details", artworkControls, new Vector2(.542f, 0f), Vector2.one,
                    Vector2.zero, Vector2.zero);
                _duelPadInspectButton = DuelStyle.Button(details, "B", "상세 보기", OpenInspectedCardDetails, 14);
                var detailsLabel = _duelPadInspectButton.GetComponentInChildren<Text>();
                detailsLabel.resizeTextForBestFit = true;
                detailsLabel.resizeTextMinSize = 9;
                detailsLabel.resizeTextMaxSize = 14;
            }
            var counterChip = DuelUiKit.CreateRect("Counters", artworkControls, Vector2.zero,
                new Vector2(_mobileLayout ? 1f : .512f, 1f), Vector2.zero, Vector2.zero);
            _inspCounters = new DuelCounterBadge(counterChip, compact: false);

            var stats = DuelUiKit.CreateRect("Stats", inside, new Vector2(.016f, .342f), new Vector2(.825f, .419f),
                Vector2.zero, Vector2.zero);
            var identity = DuelUiKit.CreateRect("Identity", stats, new Vector2(0f, .505f), Vector2.one,
                Vector2.zero, Vector2.zero);
            _inspIdentity = DuelUiKit.AddFittedText(identity, "Text", "", 14, 9, TextAnchor.MiddleLeft, Color.white);
            var rating = DuelUiKit.CreateRect("Rating", stats, new Vector2(.695f, .515f), Vector2.one,
                Vector2.zero, Vector2.zero);
            _inspRating = new DuelRatingBadge(rating, compact: true);
            _inspStats = DuelUiKit.AddFittedText(DuelUiKit.CreateRect("Info", stats,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "Text", "", 14, 9, TextAnchor.MiddleLeft, Color.white);
            var combat = DuelUiKit.CreateRect("CombatStats", inside, new Vector2(.018f, .281f), new Vector2(.982f, .337f),
                Vector2.zero, Vector2.zero);
            _inspCombat = new DuelStatStrip(combat, compact: false);
            _inspScales = new DuelPendulumScalePair(DuelUiKit.CreateRect("Scales", inside,
                new Vector2(.018f, .220f), new Vector2(.982f, .275f), Vector2.zero, Vector2.zero));
            var race = DuelUiKit.CreateRect("Race", inside, new Vector2(.841f, .384f), new Vector2(.987f, .418f),
                Vector2.zero, Vector2.zero);
            _inspRaceIcon = DuelUiKit.AddImage(race.gameObject, null, Color.white, true, false);
            var attribute = DuelUiKit.CreateRect("Attribute", inside, new Vector2(.841f, .345f), new Vector2(.987f, .379f),
                Vector2.zero, Vector2.zero);
            _inspAttributeIcon = DuelUiKit.AddImage(attribute.gameObject, null, Color.white, true, false);

            var title = DuelUiKit.CreateRect("Name", inside, new Vector2(0f, .912f), Vector2.one, Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(title, new Color(.065f, .113f, .133f, .92f));
            var accent = DuelUiKit.CreateRect("TypeAccent", title, new Vector2(0f, .18f), new Vector2(0f, .82f),
                new Vector2(4f, 0f), new Vector2(6f, 0f));
            _inspTitleAccent = DuelUiKit.AddImage(accent.gameObject, DuelUiKit.WhiteSprite, DuelStyle.Muted, false, false);
            var titleContent = DuelUiKit.CreateRect("Content", title, Vector2.zero, Vector2.one,
                new Vector2(13f, 0f), new Vector2(-5f, 0f));
            _inspName = DuelUiKit.AddFittedText(titleContent, "Text", "", 21, 10,
                TextAnchor.MiddleLeft, Color.white);
            _inspRulesName = CreateRulesNameLabel(titleContent, 14);

            var desc = DuelUiKit.CreateRect("Description", inside,
                new Vector2(.018f, .081f), new Vector2(.982f, .275f), Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(desc, new Color(.018f, .045f, .061f, .91f)).raycastTarget = true;
            var viewport = DuelUiKit.CreateRect("Viewport", desc, Vector2.zero, Vector2.one,
                new Vector2(8f, 5f), new Vector2(-14f, -5f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = DuelUiKit.CreateRect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            _inspDesc = content.gameObject.AddComponent<Text>();
            _inspDesc.font = DuelUiKit.FontKR;
            _inspDesc.fontSize = _mobileLayout ? 18 : 15;
            _inspDesc.verticalOverflow = VerticalWrapMode.Overflow;
            _inspDesc.lineSpacing = 1.12f;
            _inspDesc.color = DuelUiKit.TextPrimary;
            _inspDesc.raycastTarget = false;
            _inspDesc.alignment = TextAnchor.UpperLeft;
            _inspDesc.supportRichText = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = desc.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 32f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.verticalScrollbar = CreateInspectorScrollbar(desc);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            _inspectScroll = scroll;

            var log = DuelUiKit.CreateRect("RecentActions", inside, new Vector2(.023f, .011f), new Vector2(.977f, .073f),
                Vector2.zero, Vector2.zero);
            _logFeed = DuelUiKit.AddText(log, "Log", "", 11, TextAnchor.UpperLeft, DuelStyle.Muted);
            _logFeed.supportRichText = false;
            log.gameObject.AddComponent<RectMask2D>();
        }

        private void OpenInspectedCardDetails()
        {
            ShowCardZoomModal(_inspectedCode, _inspectedFieldCard, _inspectedAppearance);
            if (_cardZoomModal.activeSelf) _zoomBanishedCard = _inspectedBanishedCard;
        }

        private static void AddInspectorArtworkFrame(Transform aspect)
        {
            var frame = DuelUiKit.FillRect("ArtworkFrame", aspect, -2.5f);
            frame.SetAsFirstSibling();
            DuelReadoutPlate.Add(frame, new Color(.30f, .27f, .22f, .97f));
        }

        private static RectTransform CreateInspectorSurface(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var shell = DuelUiKit.CreateRect(name, parent, min, max, Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(shell, new Color(.38f, .34f, .27f, .95f)).raycastTarget = true;
            var surface = DuelUiKit.FillRect("Surface", shell, 1.2f);
            DuelReadoutPlate.Add(surface, new Color(.029f, .060f, .081f, .975f));
            return shell;
        }

        private static Scrollbar CreateInspectorScrollbar(RectTransform parent)
        {
            var scrollbar = DuelUiKit.AddVerticalScrollbar(parent);
            var track = (RectTransform)scrollbar.transform;
            track.offsetMin = new Vector2(-10f, 5f);
            track.offsetMax = new Vector2(-3f, -5f);
            var rail = track.GetComponent<Image>();
            rail.sprite = DuelUiKit.WhiteSprite;
            rail.color = new Color(.084f, .16f, .20f, .85f);
            var thumb = (Image)scrollbar.targetGraphic;
            thumb.sprite = DuelUiKit.WhiteSprite;
            thumb.color = new Color(.40f, .70f, .76f, .95f);
            return scrollbar;
        }

        private void BuildTopHud(Transform root)
        {
            _oppHandBar = DuelUiKit.CreateRect("OpponentHand", root, new Vector2(.29f, .873f),
                new Vector2(.735f, .994f), Vector2.zero, Vector2.zero);
            var opponent = DuelUiKit.CreateRect("OpponentHUD", root,
                new Vector2(0.762f, 0.894f), new Vector2(0.985f, 0.982f), Vector2.zero, Vector2.zero);
            (_oppNameText, _oppLpText, _oppLpFill, _oppBustImg) = BuildPlayerHud(opponent, true);
            _oppLpFeedbackAnchor = (RectTransform)_oppNameText.transform.parent;

            var menu = _pauseControlDock = DuelUiKit.CreateRect("PauseMenu", root,
                _mobileLayout ? new Vector2(.904f, .724f) : new Vector2(.016f, .936f),
                _mobileLayout ? new Vector2(.984f, .874f) : new Vector2(.103f, .982f), Vector2.zero, Vector2.zero);
            _duelPadPauseButton = BuildHudControl(menu, DuelVisualAssets.PauseIcon, "일시 정지", OnPauseClicked);
        }

        private static Button BuildHudControl(RectTransform holder, Sprite sprite, string label, Action action)
        {
            if (sprite == null)
                return DuelStyle.Button(holder, "Button", label, action, 13);

            var icon = DuelUiKit.CreateRect(label, holder, Vector2.zero, new Vector2(.45f, 1f), Vector2.zero, Vector2.zero);
            var image = DuelUiKit.AddImage(icon.gameObject, sprite, Color.white, true, false);
            var button = holder.gameObject.AddComponent<Button>();
            DuelUiKit.AddImage(holder.gameObject, DuelUiKit.WhiteSprite, Color.clear);
            button.targetGraphic = image;
            button.onClick.AddListener(action.Invoke);
            var text = DuelUiKit.CreateRect("Label", holder, new Vector2(.43f, .10f), new Vector2(1f, .90f), Vector2.zero, Vector2.zero);
            var caption = DuelUiKit.AddFittedText(text, "Text", label, 13, 8, TextAnchor.MiddleLeft, Color.white);
            DuelStyle.Shadow(caption, 1.75f);
            return button;
        }

        private static (Text name, Text lp, RectTransform fill, Image portrait) BuildPlayerHud(RectTransform root, bool opponent)
        {
            var side = DuelStyle.Side(opponent);
            var panel = DuelUiKit.FillRect("Status", root, 1f);
            AddHudSurface(panel, Color.Lerp(HudEdge, side, .32f), HudSurface);
            var lpFace = opponent ? DuelVisualAssets.LpOpponent : DuelVisualAssets.LpPlayer;
            if (lpFace != null)
                DuelUiKit.AddImage(DuelUiKit.FillRect("LocalLpPanel", panel, 2f).gameObject, lpFace,
                    new Color(1f, 1f, 1f, .45f), false, false);

            var avatarHolder = DuelUiKit.CreateRect("AvatarFrame", panel, new Vector2(opponent ? .781f : .017f, .095f),
                new Vector2(opponent ? .983f : .219f, .905f), Vector2.zero, Vector2.zero);
            var avatar = DuelUiKit.FillRect("PortraitPlate", avatarHolder);
            var square = avatar.gameObject.AddComponent<AspectRatioFitter>();
            square.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            square.aspectRatio = 1f;
            DuelStyle.AddHexPlate(avatar, Color.Lerp(HudEdge, side, .55f), new Color(.020f, .038f, .050f));
            var portrait = DuelUiKit.AddImage(DuelUiKit.FillRect("Portrait", avatar, 4f).gameObject, null, Color.white, true, false);

            var readout = DuelUiKit.CreateRect("Readout", panel, new Vector2(opponent ? .038f : .241f, 0f),
                new Vector2(opponent ? .759f : .962f, 1f), Vector2.zero, Vector2.zero);
            var name = DuelUiKit.AddFittedText(DuelUiKit.CreateRect("Name", readout, new Vector2(0f, .62f),
                new Vector2(1f, .932f), Vector2.zero, Vector2.zero), "Text", "", 17, 9,
                opponent ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, DuelStyle.Muted);
            var track = DuelUiKit.CreateRect("LPTrack", readout, new Vector2(0f, .082f), new Vector2(1f, .172f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddImage(track.gameObject, DuelUiKit.WhiteSprite, new Color(.19f, .24f, .27f), false, false);
            var fill = DuelUiKit.FillRect("Fill", track);
            DuelUiKit.AddImage(fill.gameObject, DuelUiKit.WhiteSprite, LifeTrackColor, false, false);
            for (int i = 1; i < 8; i++)
            {
                float x = i / 8f;
                var divider = DuelUiKit.CreateRect("Tick", track, new Vector2(x, 0f), new Vector2(x, 1f),
                    Vector2.zero, new Vector2(1f, 0f));
                DuelUiKit.AddImage(divider.gameObject, DuelUiKit.WhiteSprite, new Color(.024f, .055f, .076f, .56f), false, false);
            }
            DuelTypography.AddNumber(DuelUiKit.CreateRect("Caption", readout, new Vector2(0f, .15f),
                new Vector2(.18f, .57f), Vector2.zero, Vector2.zero), "Label", "LP", 17,
                TextAnchor.MiddleLeft, Color.Lerp(DuelStyle.Muted, side, .39f));
            var lp = DuelTypography.AddNumber(DuelUiKit.CreateRect("Value", readout, new Vector2(.18f, .139f),
                new Vector2(1f, .65f), Vector2.zero, Vector2.zero), "Text", "", 38, TextAnchor.MiddleRight, HudText);
            return (name, lp, fill, portrait);
        }

        private static Image AddHudSurface(RectTransform root, Color edge, Color face)
        {
            var rim = DuelReadoutPlate.Add(root, edge);
            DuelReadoutPlate.Add(DuelUiKit.FillRect("Surface", root, 1.3f), face);
            return rim;
        }

        private void BuildCenterBoard(Transform root)
        {
            var board = DuelUiKit.CreateRect("DuelBoard", root,
                new Vector2(.142f, .221f), new Vector2(.903f, .886f), Vector2.zero, Vector2.zero);
            _boardRect = board;
            _mat = DuelUiKit.FillRect("StoneMat", board).gameObject.AddComponent<DuelMat>();
            _mat.texture = Texture2D.whiteTexture;
            _mat.raycastTarget = false;
            var surface = DuelUiKit.FillRect("SlabSurface", board).gameObject.AddComponent<DuelMatSurface>();
            surface.texture = DuelVisualAssets.ArenaSurface ?? DuelStyle.Stone;
            surface.raycastTarget = false;
            RectTransform Cell(int col, int row) => DuelZoneImage.CreateCell(board, col, row);
            FieldSlotView LinkCell(int column, int row)
            {
                var slot = CreateZoneSlot(Cell(column + 2, row));
                slot.linkCell = new Vector2Int(column, row);
                _linkGrid[column, row] = slot;
                return slot;
            }

            for (int i = 0; i < 5; i++)
            {
                int oppSeq = 4 - i;
                _myMZone[i] = LinkCell(i, 3);
                _oppMZone[oppSeq] = LinkCell(i, 1);
                _mySZone[i] = LinkCell(i, 4);
                _oppSZone[oppSeq] = LinkCell(i, 0);
            }

            _emz[0] = LinkCell(1, 2);
            _emz[1] = LinkCell(3, 2);
            _emz[0].bgImg.MarkExtraMonsterZone();
            _emz[1].bgImg.MarkExtraMonsterZone();
            var chain = DuelUiKit.CreateRect("Chain", board, new Vector2(.455f, .494f), new Vector2(.545f, .569f),
                Vector2.zero, Vector2.zero);
            _chainIcon = DuelStyle.AddHexPlate(chain, DuelStyle.Gold, new Color(.13f, .082f, .060f, .96f));
            _chainBannerText = DuelUiKit.AddFittedText(chain, "Count", "", 18, 9, TextAnchor.MiddleCenter, DuelStyle.Gold, FontStyle.Bold);

            _mySZone[5] = CreateZoneSlot(Cell(1, 3));
            _oppSZone[5] = CreateZoneSlot(Cell(7, 1));
            _myExtraTile = CreateStackTile(Cell(1, 4), false, YgoConstants.LOCATION_EXTRA, "엑스트라 덱");
            _myExtraTile.label.text = "엑스트라 확인";
            _myDeckTile = CreateStackTile(Cell(7, 4), false, YgoConstants.LOCATION_DECK, "덱");
            _myGyTile = CreateStackTile(Cell(7, 3), false, YgoConstants.LOCATION_GRAVE, "묘지");
            _myBanTile = CreateStackTile(Cell(7, 2), false, YgoConstants.LOCATION_REMOVED, "제외");

            _oppExtraTile = CreateStackTile(Cell(7, 0), true, YgoConstants.LOCATION_EXTRA, "엑스트라 덱");
            _oppDeckTile = CreateStackTile(Cell(1, 0), true, YgoConstants.LOCATION_DECK, "덱");
            _oppGyTile = CreateStackTile(Cell(1, 1), true, YgoConstants.LOCATION_GRAVE, "묘지");
            _oppBanTile = CreateStackTile(Cell(1, 2), true, YgoConstants.LOCATION_REMOVED, "제외");

            _extraSummonButton = BuildPileActionButton(_myExtraTile, "ExtraSummon", "특수 소환", OpenExtraSummonPicker);
            _extraEffectButton = BuildPileActionButton(_myExtraTile, "ExtraEffect", "발동",
                () => OpenPileEffectPicker(YgoConstants.LOCATION_EXTRA), activation: true);
            _graveEffectButton = BuildPileActionButton(_myGyTile, "GraveEffect", "효과 발동",
                () => OpenPileEffectPicker(YgoConstants.LOCATION_GRAVE), activation: true);
            _banishedEffectButton = BuildPileActionButton(_myBanTile, "BanishedEffect", "효과 발동",
                () => OpenPileEffectPicker(YgoConstants.LOCATION_REMOVED), activation: true);
        }

        private static Button BuildPileActionButton(StackTileView tile, string name, string label, Action action, bool activation = false)
        {
            var indicator = DuelUiKit.CreateRect(name, tile.root, new Vector2(.07f, .645f),
                new Vector2(.93f, .965f), Vector2.zero, Vector2.zero);
            var rect = DuelUiKit.FillRect("Button", indicator);
            Color tone = activation ? DuelStyle.Gold : DuelStyle.Player;
            var rim = AddHudSurface(rect, tone, activation
                ? new Color(.24f, .16f, .066f, .99f) : new Color(.063f, .18f, .22f, .99f));
            var button = AddHudButton(rim, action);
            var caption = rect;
            if (activation)
            {
                var symbol = DuelUiKit.CreateRect("Effect", rect, new Vector2(.055f, .13f),
                    new Vector2(.29f, .87f), Vector2.zero, Vector2.zero);
                DuelUiKit.AddImage(symbol.gameObject, DuelStatSymbols.BattleEffect, tone, true, false);
                caption = DuelUiKit.CreateRect("Caption", rect, new Vector2(.29f, 0f), new Vector2(.96f, 1f),
                    Vector2.zero, Vector2.zero);
            }
            DuelUiKit.AddFittedText(caption, "Label", label, 17, 9, TextAnchor.MiddleCenter, tone, FontStyle.Bold);
            button.gameObject.SetActive(false);
            return button;
        }

        private static readonly Color ZoneTint = new Color(.032f, .07f, .092f, .42f);
        // Use the spacing between field rows without letting face-up cards touch the next row.
        private static readonly Vector2 SlotCardMin = new Vector2(.025f, -.008f);
        private static readonly Vector2 SlotCardMax = new Vector2(.975f, 1.008f);

        private FieldSlotView CreateZoneSlot(RectTransform cell)
        {
            var background = DuelUiKit.AddImage(cell.gameObject, DuelUiKit.WhiteSprite, ZoneTint);
            ((DuelZoneImage)background).ShowCircuitRing();
            var border = cell.gameObject.AddComponent<Outline>();
            border.effectColor = Color.clear;
            border.effectDistance = new Vector2(2f, -2f);

            var card = DuelUiKit.CreateRect("Card", cell, SlotCardMin, SlotCardMax, Vector2.zero, Vector2.zero);
            var view = CompositeCardView.Create(card, "CardView");
            view.UsePrintedFaceOnly();
            view.gameObject.SetActive(false);

            var slot = new FieldSlotView
            {
                root = cell,
                bgImg = (DuelZoneImage)background,
                border = border,
                cardView = view,
                cardArea = card,
                focusArt = (RectTransform)view.transform.Find("AspectBox"),
                dragTrigger = cell.gameObject.AddComponent<CardDragTrigger>(),
            };
            slot.activationGlow = DuelStyle.AddActivationGlow(slot.focusArt);
            _fieldSlotsByRect.Add(cell, slot);
            return slot;
        }

        private StackTileView CreateStackTile(RectTransform cell, bool opponent, uint location, string name)
        {
            var background = (DuelZoneImage)DuelUiKit.AddImage(cell.gameObject, DuelUiKit.WhiteSprite, ZoneTint);

            bool grave = location == YgoConstants.LOCATION_GRAVE;
            bool usesWell = grave || location == YgoConstants.LOCATION_REMOVED;
            DuelPileWell well = null;
            Graphic wellGlyph = null;
            if (usesWell)
            {
                var basin = DuelUiKit.CreateRect("Well", cell, new Vector2(.12f, .229f), new Vector2(.88f, .985f),
                    Vector2.zero, Vector2.zero);
                well = basin.gameObject.AddComponent<DuelPileWell>();
                well.texture = Texture2D.whiteTexture;
                well.raycastTarget = false;
                well.Accent = grave ? new Color(.38f, .68f, .73f) : new Color(.67f, .55f, .81f);
                var wellArt = grave ? DuelVisualAssets.GraveWell : DuelVisualAssets.BanishedWell;
                if (wellArt != null)
                    DuelUiKit.AddImage(DuelUiKit.FillRect("LocalWell", basin).gameObject, wellArt,
                        new Color(1f, 1f, 1f, .64f), true, false);
                var glyph = DuelUiKit.CreateRect("Location", basin, new Vector2(.31f, .31f), new Vector2(.69f, .69f),
                    Vector2.zero, Vector2.zero);
                wellGlyph = DuelPromptLocationIcon.Add(glyph, location, opponent, glyphOnly: true);
                wellGlyph.color = Color.Lerp(DuelStyle.Muted, well.Accent, .52f);
            }
            var card = DuelUiKit.CreateRect("TopCard", cell, usesWell ? new Vector2(.19f, .322f) : new Vector2(.15f, .312f),
                usesWell ? new Vector2(.81f, .889f) : new Vector2(.85f, .949f), Vector2.zero, Vector2.zero);
            var view = CompositeCardView.Create(card, "CardView");
            view.UsePrintedFaceOnly();
            view.transform.localRotation = Quaternion.Euler(0f, 0f, opponent ? 180f : 0f);
            DuelPileDepth depth = null;
            if (!usesWell)
            {
                var layer = DuelUiKit.FillRect("StackDepth", view.transform.Find("AspectBox"));
                layer.SetAsFirstSibling();
                depth = layer.gameObject.AddComponent<DuelPileDepth>();
                depth.texture = Texture2D.whiteTexture;
                depth.Opponent = opponent;
                depth.raycastTarget = false;
            }
            view.gameObject.SetActive(false);
            var counter = DuelUiKit.CreateRect("Total", cell, new Vector2(.075f, .063f), new Vector2(.925f, .255f),
                Vector2.zero, Vector2.zero);
            Color side = DuelStyle.Side(opponent);
            AddHudSurface(counter, Color.Lerp(DuelStyle.GlassEdge, side, .25f), new Color(.06f, .14f, .19f, .76f));
            var shine = DuelUiKit.CreateRect("GlassEdge", counter, new Vector2(.038f, 1f), new Vector2(.962f, 1f),
                new Vector2(0f, -1f), Vector2.zero);
            DuelUiKit.AddImage(shine.gameObject, DuelUiKit.WhiteSprite, new Color(.68f, .88f, .96f, .48f), false, false);
            var nameRect = DuelUiKit.CreateRect("Caption", counter, new Vector2(.046f, .04f), new Vector2(.745f, .958f),
                Vector2.zero, Vector2.zero);
            var label = DuelUiKit.AddFittedText(nameRect, "Label", name, 14, 8, TextAnchor.MiddleLeft, DuelStyle.Muted);
            var amountRect = DuelUiKit.CreateRect("Amount", counter, new Vector2(.748f, .04f), new Vector2(.953f, .958f),
                Vector2.zero, Vector2.zero);
            var count = DuelTypography.AddNumber(amountRect, "Count", "", 18, TextAnchor.MiddleRight, HudText);

            void Open()
            {
                if (_session == null) return;
                byte player = (byte)(opponent ? _session.AiPlayerIndex : _session.HumanPlayerIndex);
                OpenStackZone(player, location, (opponent ? "상대 " : "내 ") + name);
            }

            var button = cell.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(Open);
            var trigger = cell.gameObject.AddComponent<CardDragTrigger>();
            trigger.OnRightClick = Open;
            if (location is YgoConstants.LOCATION_GRAVE or YgoConstants.LOCATION_REMOVED)
            {
                trigger.OnPointerEnterAction = () => PointAtCardActions(cell);
                trigger.OnPointerExitAction = () => LeaveCardActions(cell);
            }
            var tile = new StackTileView
            {
                root = cell, cardArea = card, label = label, count = count, background = background, cardView = view,
                well = well, wellGlyph = wellGlyph, depth = depth, button = button, location = location,
            };
            if (!opponent) tile.activationGlow = DuelStyle.AddActivationGlow(cell, 5f, behind: false);
            _stackTilesByRect.Add(cell, tile);
            return tile;
        }

        private void BuildBottomHudAndHands(Transform root)
        {
            _myHandBar = DuelUiKit.CreateRect("PlayerHand", root, new Vector2(.228f, .012f), new Vector2(.784f, .215f),
                Vector2.zero, Vector2.zero);
            _myHandContent = _myHandBar;
            if (_mobileLayout)
            {
                _myHandMask = _myHandBar.gameObject.AddComponent<RectMask2D>();
                DuelUiKit.AddImage(_myHandBar.gameObject, DuelUiKit.WhiteSprite, Color.clear);
                _myHandScroll = _myHandBar.gameObject.AddComponent<ScrollRect>();
                _myHandScroll.horizontal = true;
                _myHandScroll.vertical = false;
                _myHandScroll.movementType = ScrollRect.MovementType.Clamped;
                _myHandScroll.viewport = _myHandBar;
                _myHandContent = DuelUiKit.CreateRect("Cards", _myHandBar, Vector2.zero, Vector2.up, Vector2.zero, Vector2.zero);
                _myHandContent.pivot = new Vector2(0f, .5f);
                _myHandScroll.content = _myHandContent;
            }
            var player = DuelUiKit.CreateRect("PlayerHUD", root, new Vector2(0.016f, 0.035f), new Vector2(0.221f, 0.132f),
                Vector2.zero, Vector2.zero);
            var (name, lp, fill, portrait) = BuildPlayerHud(player, false);
            name.text = "플레이어";
            _myLpFeedbackAnchor = (RectTransform)name.transform.parent;
            _myLpText = lp;
            _myLpFill = fill;
            portrait.sprite = DuelProfileArt.Player;
            portrait.enabled = portrait.sprite != null;
            _rankedChampionBadge = OfflineRankedUI.AddChampionBadge(player, new Vector2(.23f, 1.01f), new Vector2(1f, 1.32f));
            _rankedChampionBadge.SetActive(false);
            _rankedAvatarRim = portrait.transform.parent.GetComponent<Image>();
            _normalAvatarRim = _rankedAvatarRim.color;

            BuildTurnHud(root);
            _battleShortcut = BuildPhaseShortcut(root, "BattleShortcut", "배틀", "BATTLE PHASE", .399f, YgoConstants.PHASE_BATTLE);
            _endShortcut = BuildPhaseShortcut(root, "EndShortcut", "턴 종료", "END PHASE", .257f, YgoConstants.PHASE_END);
            BuildPhasePicker(root);
            BuildPlacementControls(root);
            var log = _logControlDock = DuelUiKit.CreateRect("OpenLog", root, new Vector2(0.910f, 0.035f), new Vector2(0.984f, 0.092f),
                Vector2.zero, Vector2.zero);
            _duelPadLogButton = BuildHudControl(log, DuelVisualAssets.LogIcon, "대전 기록", OpenDuelLog);
            var chainConfirmation = _chainControlDock = DuelUiKit.CreateRect("ChainConfirmation", root,
                new Vector2(.825f, .035f), new Vector2(.901f, .092f), Vector2.zero, Vector2.zero);
            LayoutMobileDuelControls();
            BuildChainConfirmationDial(chainConfirmation);
            _modals.SetChainConfirmationControl(_chainConfirmationButton);

            var hint = DuelUiKit.CreateRect("ActionHint", root, new Vector2(0.294f, 0.187f), new Vector2(0.752f, 0.225f),
                Vector2.zero, Vector2.zero);
            _hintBannerText = DuelUiKit.AddText(hint, "Text", "", 15, TextAnchor.MiddleCenter, DuelUiKit.TextPrimary, FontStyle.Bold);
            _hintBannerText.gameObject.AddComponent<Outline>().effectColor = DuelUiKit.BgDeep;
        }

        private void BuildChainConfirmationDial(RectTransform dock)
        {
            if (!_mobileLayout)
                CenterHudControl(dock, new Vector2(.865f, .073f), 60f);
            _chainConfirmationButton = DuelStyle.RoundButton(dock, "Button", DuelStyle.ChainCyan, CycleChainConfirmation);
            var dial = _chainConfirmationButton.transform;
            var halo = DuelUiKit.FillRect("PulseRing", dial, -3f);
            halo.SetAsFirstSibling();
            _chainDialPulse = DuelUiKit.AddImage(halo.gameObject, DuelStyle.Disc, Color.clear, false, false);
            var inset = DuelUiKit.CreateRect("Inset", dial, Vector2.one * .12f, Vector2.one * .88f,
                Vector2.zero, Vector2.zero);
            _chainDialFace = DuelUiKit.AddImage(inset.gameObject, DuelStyle.Disc, HudSurface, false, false);
            var status = DuelUiKit.CreateRect("Status", dial, new Vector2(.08f, .21f), new Vector2(.92f, .63f),
                Vector2.zero, Vector2.zero);
            _chainConfirmationLabel = DuelUiKit.AddFittedText(status, "Mode", "AUTO", 20, 9,
                TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            var title = DuelUiKit.CreateRect("Caption", dial, new Vector2(.15f, .61f), new Vector2(.85f, .80f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddFittedText(title, "Label", "체인", 12, 8, TextAnchor.MiddleCenter, DuelStyle.Muted);
        }

        private static void PositionBottomControl(RectTransform control, float right, float width, float height)
        {
            control.anchorMin = control.anchorMax = new Vector2(1f, 0f);
            control.offsetMin = new Vector2(-right - width, 8f);
            control.offsetMax = new Vector2(-right, height + 8f);
        }

        private void LayoutMobileDuelControls()
        {
            if (!_mobileLayout) return;
            float touch = MobileSafeArea.MinimumTouchSize(_chainControlDock);
            PositionBottomControl(_logControlDock, 8f, touch, touch);
            PositionBottomControl(_chainControlDock, touch + 16f, Mathf.Max(112f, touch), touch);

            var safe = (RectTransform)_boardRect.parent;
            if (safe.rect.width <= 0f || safe.rect.height <= 0f) return;
            // Account for the narrowest perspective cell and the spacing between rows.
            float width = Mathf.Clamp(touch * 9f / (.968f * .81f * safe.rect.width), .761f, .901f);
            float height = Mathf.Clamp(touch / (.196f * safe.rect.height), .671f, .745f);
            float bottom = .55f - height * .5f;
            float left = Mathf.Min(.145f, .995f - width);
            _boardRect.anchorMin = new Vector2(left, bottom);
            _boardRect.anchorMax = new Vector2(left + width, bottom + height);
            _myHandBar.anchorMax = new Vector2(_myHandBar.anchorMax.x, bottom - .006f);
            _oppHandBar.anchorMin = new Vector2(_oppHandBar.anchorMin.x, Mathf.Max(.873f, bottom + height));

            // Keep the smaller HUD shortcuts finger-sized without covering the adjacent pile cells.
            CenterHudControl((RectTransform)_phaseButton.transform, Vector2.one * .5f, touch);
            CenterHudControl(_pauseControlDock, new Vector2(.944f, .799f), touch);
            CenterHudControl(_placementControlDock, new Vector2(.944f, .398f), touch);
            CenterHudControl((RectTransform)_battleShortcut.transform, new Vector2(.944f, .399f), touch);
            CenterHudControl((RectTransform)_endShortcut.transform, new Vector2(.944f, .257f), touch);
            float stripTop = Mathf.Min(.209f, .257f - (touch * .5f + 3f) / safe.rect.height);
            float stripBottom = Mathf.Min(stripTop - .03f, Mathf.Max(.143f, (touch + 10f) / safe.rect.height));
            _phaseStrip.anchorMin = new Vector2(.793f, stripBottom);
            _phaseStrip.anchorMax = new Vector2(.984f, stripTop);
        }

        private static void CenterHudControl(RectTransform rect, Vector2 anchor, float size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = Vector2.zero;
        }

        private void BuildTurnHud(Transform root)
        {
            var holder = DuelUiKit.CreateRect("Turn", root, new Vector2(.904f, .451f), new Vector2(.984f, .615f),
                Vector2.zero, Vector2.zero);
            var turn = DuelUiKit.CreateRect("Dial", holder, new Vector2(0f, .12f), new Vector2(1f, .88f),
                Vector2.zero, Vector2.zero);
            _phaseRim = DuelStyle.AddHexPlate(turn, HudEdge, HudSurface);
            _phaseButton = AddHudButton(_phaseRim, OpenPhasePicker);

            var turnReadout = DuelUiKit.CreateRect("TurnNumber", turn, new Vector2(.115f, .675f),
                new Vector2(.885f, .935f), Vector2.zero, Vector2.zero);
            _turnBadgeText = DuelUiKit.AddFittedText(DuelUiKit.CreateRect("Owner", turnReadout, Vector2.zero,
                new Vector2(.655f, 1f), Vector2.zero, Vector2.zero), "Label", "", 16, 9,
                TextAnchor.MiddleLeft, DuelStyle.Muted);
            _turnNumber = DuelTypography.AddNumber(DuelUiKit.CreateRect("Count", turnReadout, new Vector2(.670f, 0f),
                Vector2.one, Vector2.zero, Vector2.zero), "Value", "", 20, TextAnchor.MiddleRight, HudText);
            _phaseLabel = DuelUiKit.AddFittedText(DuelUiKit.CreateRect("PhaseName", turn, new Vector2(.07f, .42f),
                new Vector2(.93f, .682f), Vector2.zero, Vector2.zero), "Value", "", 26, 12,
                TextAnchor.MiddleCenter, HudText);
            var progress = _phaseStrip = DuelUiKit.CreateRect("PhaseSteps", root, new Vector2(.793f, .155f), new Vector2(.984f, .209f),
                Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(progress, new Color(.022f, .049f, .068f, .89f));
            for (int i = 0; i < _phaseSteps.Length; i++)
            {
                var step = DuelUiKit.CreateRect(PhaseEntries[i].name, progress,
                    new Vector2((i + .12f) / _phaseSteps.Length, 0f), new Vector2((i + .88f) / _phaseSteps.Length, 1f),
                    Vector2.zero, Vector2.zero);
                _phaseSteps[i] = DuelStyle.AddHexPlate(step, HudEdge, HudSurface);
                var artwork = DuelUiIcons.FindPhaseArtwork(PhaseEntries[i].name);
                if (artwork != null)
                {
                    var face = DuelUiKit.CreateRect("Phase", step, new Vector2(.21f, .22f), new Vector2(.79f, .78f),
                        Vector2.zero, Vector2.zero);
                    _phaseStepMarks[i] = DuelUiKit.AddImage(face.gameObject, artwork, DuelStyle.Muted, true, false);
                }
                else
                    _phaseStepMarks[i] = DuelUiKit.AddFittedText(step, "Phase", PhaseShortNames[i], 18, 9,
                        TextAnchor.MiddleCenter, DuelStyle.Muted, FontStyle.Bold);
            }
            _phaseControlHint = DuelUiKit.AddFittedText(DuelUiKit.CreateRect("ChangePrompt", turn, new Vector2(.09f, .045f),
                new Vector2(.91f, .213f), Vector2.zero, Vector2.zero), "Label", "페이즈 변경", 13, 8,
                TextAnchor.MiddleCenter, DuelStyle.Gold);
        }

        private Button BuildPhaseShortcut(Transform parent, string name, string label, string key, float y, int phase)
        {
            var root = DuelUiKit.CreateRect(name, parent, new Vector2(.904f, y - .035f),
                new Vector2(.984f, y + .035f), Vector2.zero, Vector2.zero);
            Color tint = phase == YgoConstants.PHASE_BATTLE ? DuelStyle.ChainCyan : DuelStyle.Gold;
            var button = AddHudButton(DuelStyle.AddHexPlate(root, tint, new Color(.032f, .074f, .103f)),
                () => OnPhaseShortcut(phase));
            var heading = DuelUiKit.CreateRect("Label", root, new Vector2(.07f, .34f), new Vector2(.93f, .83f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddFittedText(heading, "Text", label, 19, 10, TextAnchor.MiddleCenter, HudText, FontStyle.Bold);
            var hint = DuelUiKit.CreateRect("Hint", root, new Vector2(.08f, .11f), new Vector2(.92f, .36f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddFittedText(hint, "Text", key, 11, 7, TextAnchor.MiddleCenter, tint, FontStyle.Bold);
            button.gameObject.SetActive(false);
            return button;
        }

        private static Button AddHudButton(Image rim, Action onClick)
        {
            rim.raycastTarget = true;
            var button = rim.gameObject.AddComponent<Button>();
            button.targetGraphic = rim;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = colors.disabledColor = Color.white;
            colors.highlightedColor = colors.selectedColor = new Color(1f, .92f, .75f);
            colors.pressedColor = new Color(.78f, .73f, .68f);
            colors.fadeDuration = .08f;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                DuelAudio.PlayClick();
                onClick();
            });
            return button;
        }

        private void BuildPhasePicker(Transform root)
        {
            var overlay = DuelUiKit.FillRect("PhasePicker", root);
            _phasePicker = overlay.gameObject;
            var dismiss = overlay.gameObject.AddComponent<Button>();
            dismiss.targetGraphic = DuelUiKit.AddImage(overlay.gameObject, DuelUiKit.WhiteSprite, new Color(.018f, .03f, .047f, .28f));
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(() => _phasePicker.SetActive(false));

            var controls = DuelUiKit.CreateRect("PhaseControls", overlay,
                _mobileLayout ? new Vector2(.18f, .19f) : new Vector2(.285f, .223f),
                _mobileLayout ? new Vector2(.95f, .72f) : new Vector2(.90f, .494f),
                Vector2.zero, Vector2.zero);
            AddHudSurface(controls, HudEdge, HudSurface);
            var header = DuelUiKit.CreateRect("Header", controls, new Vector2(.027f, .775f), new Vector2(.973f, .953f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddFittedText(header, "Title", "페이즈 변경", 19, 12, TextAnchor.MiddleLeft, HudText, FontStyle.Bold);

            for (int i = 0; i < PhaseEntries.Length; i++)
            {
                var (name, label, phase) = PhaseEntries[i];
                float left = .028f + .158f * i;
                var slot = DuelUiKit.CreateRect(name, controls, new Vector2(left, .297f), new Vector2(left + .150f, .737f),
                    Vector2.zero, Vector2.zero);
                var rim = DuelStyle.AddHexPlate(DuelUiKit.FillRect("Button", slot), HudEdge, new Color(.060f, .101f, .122f));
                var button = AddHudButton(rim, () =>
                {
                    _phasePicker.SetActive(false);
                    OnPhaseShortcut(phase);
                });
                var iconBox = DuelUiKit.CreateRect("Icon", button.transform, new Vector2(.20f, .48f), new Vector2(.80f, .87f),
                    Vector2.zero, Vector2.zero);
                Sprite artwork = DuelUiIcons.FindPhaseArtwork(name);
                Sprite sprite = artwork ?? DuelUiIcons.Find("phase/" + name.ToLowerInvariant());
                var icon = DuelUiKit.AddImage(iconBox.gameObject, sprite, Color.white, true, false);
                icon.enabled = sprite != null;
                var labelBox = DuelUiKit.CreateRect("Caption", button.transform, new Vector2(.06f, .088f), new Vector2(.94f, .448f),
                    Vector2.zero, Vector2.zero);
                var caption = DuelUiKit.AddFittedText(labelBox, "Label", artwork != null ? label : $"{PhaseShortNames[i]}  {label}", 17, 9,
                    TextAnchor.MiddleCenter, HudText, FontStyle.Bold);
                _phaseChoices.Add((phase, button, caption, icon));
            }

            var cancel = DuelUiKit.CreateRect("Cancel", controls,
                new Vector2(.793f, _mobileLayout ? .010f : .028f),
                new Vector2(.968f, _mobileLayout ? .285f : .270f), Vector2.zero, Vector2.zero);
            _phaseCancel = DuelStyle.Button(cancel, "Button", "취소", () => _phasePicker.SetActive(false), 15);
            _phaseCancel.navigation = new Navigation { mode = Navigation.Mode.None };
            _phasePicker.SetActive(false);
        }

        private void RefreshPhaseChoices(DuelPrompt prompt)
        {
            bool waiting = CanAcceptPlayerAction && !IsStagingPlacement;
            bool idle = waiting && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD;
            bool battle = waiting && prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD;
            int current = PhaseStep(_session.CurrentPhase);

            bool any = false;
            foreach (var (phase, button, label, icon) in _phaseChoices)
            {
                bool enabled = phase switch
                {
                    YgoConstants.PHASE_BATTLE => idle && prompt.canBp,
                    YgoConstants.PHASE_MAIN2 => battle && prompt.canM2,
                    YgoConstants.PHASE_END => (idle || battle) && prompt.canEp,
                    _ => false,
                };
                bool active = phase == current;
                button.interactable = enabled;
                button.image.color = active ? DuelStyle.Gold : enabled
                    ? Color.Lerp(HudEdge, DuelStyle.Player, .41f) : new Color(.21f, .26f, .28f);
                label.color = active ? DuelStyle.Gold : enabled ? HudText : new Color(.61f, .67f, .70f);
                icon.color = active || enabled ? label.color : new Color(.64f, .68f, .71f, .64f);
                any |= enabled;
            }
            _phaseButton.interactable = any;
            bool showShortcuts = !_modals.IsPromptOpen && !_actionPopup.activeSelf && !_phasePicker.activeSelf &&
                !IsBrowsingZone && !IsReadingCardDetails;
            _battleShortcut.gameObject.SetActive(showShortcuts && idle && prompt.canBp);
            _endShortcut.gameObject.SetActive(showShortcuts && (idle || battle) && prompt.canEp);
            _phaseControlHint.enabled = any;
            if (!any) _phasePicker.SetActive(false);
        }

        private void BuildPlacementControls(Transform root)
        {
            var panel = DuelStyle.Panel(root, "PlacementHint", new Vector2(.286f, .842f), new Vector2(.771f, .895f),
                fill: new Color(.033f, .083f, .135f, .94f), border: new Color(.72f, .82f, .86f, .57f));
            panel.GetComponent<Image>().raycastTarget = false;
            _placementBanner = panel.gameObject;
            _placementHint = DuelUiKit.AddFittedText(panel, "Text", "", 22, 11, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _placementBanner.SetActive(false);

            var cancel = _placementControlDock = DuelUiKit.CreateRect("PlacementCancel", root, new Vector2(.904f, .368f), new Vector2(.984f, .450f),
                Vector2.zero, Vector2.zero);
            _placementCancel = DuelStyle.RoundButton(cancel, "Button", DuelStyle.Player, () => TryCancelPlacement());
            DuelUiKit.AddFittedText(_placementCancel.transform, "Label", "취소", 17, 10, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            cancel.gameObject.SetActive(false);
        }

        private bool CanToggleChainConfirmation =>
            _session != null && !_session.IsFinished && !IsPaused && !IsReadingLog && !IsBrowsingZone &&
            !_cardZoomModal.activeSelf && !_actionPopup.activeSelf && !_phasePicker.activeSelf &&
            !_modals.ResultRoot.activeSelf && !IsStagingPlacement && (!_modals.IsPromptOpen || _modals.IsChainPromptOpen);

        private void CycleChainConfirmation()
        {
            if (CanToggleChainConfirmation) _app?.CycleChainConfirmation();
        }

        internal void RefreshChainConfirmation(ChainConfirmationMode mode)
        {
            _chainDialMode = mode;
            _chainConfirmationLabel.text = mode.ToString().ToUpperInvariant();
            Color ring = mode switch
            {
                ChainConfirmationMode.On => DuelStyle.ChainRed,
                ChainConfirmationMode.Off => new Color(.29f, .34f, .39f),
                _ => DuelStyle.ChainCyan
            };
            _chainConfirmationButton.image.color = ring;
            _chainConfirmationLabel.color = mode == ChainConfirmationMode.Off ? DuelStyle.Muted : Color.Lerp(ring, Color.white, .36f);
            _chainDialFace.color = mode == ChainConfirmationMode.Off ? new Color(.045f, .054f, .067f) :
                Color.Lerp(new Color(.031f, .045f, .065f), ring, .14f);
            _chainDialPulse.color = new Color(ring.r, ring.g, ring.b, mode == ChainConfirmationMode.On ? .52f : .24f);
            UpdateChainDialPulse();
            RefreshHandGlows(force: true);
        }

        private void UpdateChainDialPulse()
        {
            float opacity = _chainDialMode switch
            {
                ChainConfirmationMode.On => DuelStyle.ActivationPulse,
                ChainConfirmationMode.Off => 0f,
                _ => .65f,
            };
            _chainDialPulse.canvasRenderer.SetAlpha(opacity);
        }

        private void BuildActionPopup(Transform root)
        {
            var overlay = DuelUiKit.FillRect("CardCommands", root);
            _actionPopup = overlay.gameObject;
            var backdrop = DuelUiKit.AddImage(_actionPopup, DuelUiKit.WhiteSprite, new Color(0f, 0.015f, 0.045f, 0.23f));
            var close = _actionPopup.AddComponent<Button>();
            close.targetGraphic = backdrop;
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(CloseActionPopup);

            var middle = new Vector2(0.5f, 0.5f);
            _actionWindow = DuelStyle.Panel(overlay, "CommandWindow", middle, middle,
                fill: DuelStyle.GlassColor, border: DuelStyle.GlassEdge);
            var header = DuelUiKit.CreateRect("Caption", _actionWindow, new Vector2(0f, 1f), Vector2.one,
                new Vector2(27f, -43f), new Vector2(-27f, -7f));
            _actionHeader = DuelUiKit.AddFittedText(header, "CardName", "", 17, 10, TextAnchor.MiddleCenter,
                DuelUiKit.GoldAccent, FontStyle.Bold);

            var scrollArea = DuelUiKit.CreateRect("ScrollArea", _actionWindow, Vector2.zero, Vector2.one,
                new Vector2(25f, 18f), new Vector2(-25f, -45f));
            var viewport = DuelUiKit.CreateRect("Viewport", scrollArea, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-15f, 0f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = _actionWindow.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.verticalScrollbar = DuelUiKit.AddVerticalScrollbar(scrollArea);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            _actionList = DuelUiKit.CreateRect("Actions", viewport, new Vector2(0f, 1f), Vector2.one,
                Vector2.zero, Vector2.zero);
            _actionList.pivot = new Vector2(0.5f, 1f);
            scroll.content = _actionList;
            var list = _actionList.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 4f;
            list.childControlWidth = true;
            list.childForceExpandWidth = true;
            list.childControlHeight = true;
            list.childForceExpandHeight = false;
            _actionList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _actionPopup.SetActive(false);
        }

        // ==================== REFRESH & BINDING ====================
        public bool TryAutoFulfillPreferredPrompt()
        {
            if (!CanAcceptPlayerAction || _actionPopup.activeSelf || IsBrowsingZone) return false;
            var p = _session.ActivePrompt;
            if (p == null || p.playerId != _session.HumanPlayerIndex) return false;

            if (_preferredDropZone.HasValue &&
                (p.messageType == YgoConstants.MSG_SELECT_PLACE || p.messageType == YgoConstants.MSG_SELECT_DISFIELD))
            {
                var target = _preferredDropZone.Value;
                _preferredDropZone = null;
                if (p.messageType == YgoConstants.MSG_SELECT_PLACE && p.min == 1 &&
                    IsPlaceSelectable(target.con, target.loc, target.seq))
                {
                    byte[] resp = { target.con, (byte)target.loc, (byte)target.seq };
                    _pendingPlaces.Clear();
                    _actionPopup.SetActive(false);
                    _session.SubmitResponse(resp);
                    return true;
                }
            }

            if (_preferredAttackTargetSeq >= 0 && p.IsAttackTargetSelection)
            {
                int targetSeq = _preferredAttackTargetSeq;
                _preferredAttackTargetSeq = -1;
                for (int i = 0; i < p.candidates.Count; i++)
                {
                    var c = p.candidates[i];
                    if (c.controller == (byte)_session.AiPlayerIndex &&
                        c.location == YgoConstants.LOCATION_MZONE &&
                        c.sequence == (uint)targetSeq)
                    {
                        _session.SubmitResponse(DuelSession.PackCardSelection(i));
                        return true;
                    }
                }
            }

            if (p.messageType == YgoConstants.MSG_SELECT_IDLECMD || p.messageType == YgoConstants.MSG_SELECT_BATTLECMD)
            {
                _preferredDropZone = null;
                _preferredAttackTargetSeq = -1;
            }
            return false;
        }

        public void Refresh()
        {
            if (_session == null || !Root.activeInHierarchy) return;
            if (_boardLayoutSize != _boardRect.rect.size) LayoutMobileDuelControls();
            _boardLayoutSize = _boardRect.rect.size;

            if (!IsPresenting) _presentedFieldReadouts.Clear();
            CaptureCardReadouts();
            var prompt = _session.ActivePrompt;
            if (!ReferenceEquals(prompt, _pendingPlacePrompt))
            {
                _pendingPlacePrompt = prompt;
                _pendingPlaces.Clear();
                if (!ReferenceEquals(_stagedPlacement?.prompt, prompt)) _stagedPlacement = null;
            }

            byte me = (byte)_session.HumanPlayerIndex;
            byte opponent = (byte)_session.AiPlayerIndex;
            var mine = _session.Players[me];
            var theirs = _session.Players[opponent];
            _handCounts[me] = mine.hand.Count;
            _handCounts[opponent] = theirs.hand.Count;
            if (!IsPresenting && _session.NativePtr != IntPtr.Zero)
            {
                _myTargetLp = Mathf.Max(0, mine.lp);
                _oppTargetLp = Mathf.Max(0, theirs.lp);
            }

            RefreshPlacementControls(prompt);
            RefreshPhaseChoices(prompt);

            UpdateStackTile(_myDeckTile, mine.deckCount, me);
            UpdateStackTile(_myExtraTile, mine.extra.Count, me, mine.extra.LastOrDefault());
            UpdateStackTile(_myGyTile, mine.grave.Count, me, mine.grave.LastOrDefault());
            var myBanished = DisplayedBanishedCards(me);
            UpdateStackTile(_myBanTile, myBanished.Count, me, myBanished.LastOrDefault());
            UpdateStackTile(_oppDeckTile, theirs.deckCount, opponent);
            UpdateStackTile(_oppExtraTile, theirs.extra.Count, opponent, theirs.extra.LastOrDefault());
            UpdateStackTile(_oppGyTile, theirs.grave.Count, opponent, theirs.grave.LastOrDefault());
            var theirBanished = DisplayedBanishedCards(opponent);
            UpdateStackTile(_oppBanTile, theirBanished.Count, opponent, theirBanished.LastOrDefault());

            int chain = _session.CurrentChain.Count;
            _chainIcon.gameObject.SetActive(chain > 0);
            _chainBannerText.text = chain > 0 ? $"CHAIN {chain}" : "";

            for (int i = 0; i < 5; i++)
            {
                BindSlot(_myMZone[i], me, YgoConstants.LOCATION_MZONE, (uint)i, mine.mzone[i], false);
                BindSlot(_mySZone[i], me, YgoConstants.LOCATION_SZONE, (uint)i, mine.szone[i], false);
                BindSlot(_oppMZone[i], opponent, YgoConstants.LOCATION_MZONE, (uint)i, theirs.mzone[i], true);
                BindSlot(_oppSZone[i], opponent, YgoConstants.LOCATION_SZONE, (uint)i, theirs.szone[i], true);
            }
            BindSlot(_mySZone[5], me, YgoConstants.LOCATION_SZONE, 5, mine.szone[5], false);
            BindSlot(_oppSZone[5], opponent, YgoConstants.LOCATION_SZONE, 5, theirs.szone[5], true);
            BindEmz(_emz[0], me, 5, opponent, 6, mine.mzone[5], theirs.mzone[6]);
            BindEmz(_emz[1], me, 6, opponent, 5, mine.mzone[6], theirs.mzone[5]);

            RefreshInspectorReadout();
            if (_cardZoomModal.activeSelf) RefreshZoomReadout(_zoomCardView.CurrentCode);
            RefreshHand(mine.hand, me, false);
            RefreshHand(theirs.hand, opponent, true);
            RefreshLogFeed();
            RefreshAttackResponseArrow();
            RefreshMaterialConfirm();
            RefreshPileActionButtons();
            RefreshDuelPadCursor(layoutChanged: true);
            RefreshAttackSelectionReadout();
            RefreshFieldRelationships();
            RefreshCardActions();
        }

        private void OpenPhasePicker()
        {
            if (!CanAcceptPlayerAction || IsBrowsingZone || !_phaseButton.interactable) return;
            CloseActionPopup();
            _phasePicker.transform.SetAsLastSibling();
            _phasePicker.SetActive(true);
            HideCardActions();
        }

        private void SetTurnCaption(int turn, bool opponent)
        {
            _turnBadgeText.text = opponent ? "상대 턴" : "내 턴";
            _turnNumber.text = turn.ToString();
            _turnBadgeText.color = _turnNumber.color = Color.Lerp(HudText, DuelStyle.Side(opponent), .52f);
        }

        private static int PhaseStep(int phase) => phase is YgoConstants.PHASE_BATTLE_START or YgoConstants.PHASE_BATTLE_STEP or
            YgoConstants.PHASE_DAMAGE or YgoConstants.PHASE_DAMAGE_CAL ? YgoConstants.PHASE_BATTLE : phase;

        private void SetPhasePlate(int phase, bool opponent)
        {
            Color accent = DuelStyle.Side(opponent);
            _phaseRim.color = Color.Lerp(HudEdge, accent, .30f);
            int stepPhase = PhaseStep(phase);
            string label = YgoConstants.GetPhaseNameKR(phase);
            for (int i = 0; i < _phaseSteps.Length; i++)
            {
                bool current = PhaseEntries[i].phase == stepPhase;
                _phaseSteps[i].color = current ? DuelStyle.Gold : HudEdge;
                _phaseStepMarks[i].color = current ? DuelStyle.Gold : DuelStyle.Muted;
                if (current) label = PhaseEntries[i].label;
            }
            _phaseLabel.text = phase is YgoConstants.PHASE_DAMAGE or YgoConstants.PHASE_DAMAGE_CAL ? "데미지" : label;
            _mat.ShowTurn(opponent);
        }

        private void UpdateStackTile(StackTileView tile, int count, byte controller, LiveCardState top = null)
        {
            tile.controller = controller;
            if (IsArriving(controller, tile.location, 0)) return;

            bool chainEffect = CanAcceptPlayerAction && controller == _session.HumanPlayerIndex &&
                tile.location is YgoConstants.LOCATION_GRAVE or YgoConstants.LOCATION_EXTRA &&
                _modals.HasChainActivationAt(_session, tile.location);
            bool canAct = chainEffect || CanAcceptPlayerAction && CountZoneActions(controller, tile.location) > 0;
            bool hasCards = count > 0;
            tile.count.text = count.ToString();
            tile.count.color = canAct ? DuelStyle.Gold : hasCards ? HudText : DuelStyle.Muted;
            tile.label.color = canAct ? DuelStyle.Gold : DuelStyle.Muted;
            tile.background.SetPileActionHighlight(canAct);
            tile.depth?.SetCount(count);
            if (tile.well != null)
            {
                tile.well.SetOccupied(hasCards);
                tile.wellGlyph.enabled = !hasCards;
            }
            tile.cardView.gameObject.SetActive(hasCards);
            if (!hasCards) return;

            bool showFace = top != null && top.code != 0 &&
                (tile.location == YgoConstants.LOCATION_GRAVE || top.IsFaceUp || top.isPublic);
            if (showFace) tile.cardView.SetCard(top.code, live: top);
            else tile.cardView.SetCard(0u, true);
        }

        private void BindEmz(FieldSlotView slot, byte myCon, uint mySeq, byte oppCon, uint oppSeq, LiveCardState myEmz, LiveCardState oppEmz)
        {
            var mine = GetBattleCardDisplay(myCon, mySeq);
            var theirs = GetBattleCardDisplay(oppCon, oppSeq);
            bool mineVisible = mine?.Visible ?? (myEmz != null && !IsArriving(myCon, YgoConstants.LOCATION_MZONE, mySeq));
            bool theirsVisible = theirs?.Visible ?? (oppEmz != null && !IsArriving(oppCon, YgoConstants.LOCATION_MZONE, oppSeq));
            bool opponentPlacement = mine == null && myEmz == null &&
                !IsPlaceSelectable(myCon, YgoConstants.LOCATION_MZONE, mySeq) &&
                IsPlaceSelectable(oppCon, YgoConstants.LOCATION_MZONE, oppSeq);
            bool opponent = !mineVisible && (theirsVisible || theirs != null || oppEmz != null || opponentPlacement);
            if (opponent) BindSlot(slot, oppCon, YgoConstants.LOCATION_MZONE, oppSeq, oppEmz, true);
            else BindSlot(slot, myCon, YgoConstants.LOCATION_MZONE, mySeq, myEmz, false);
        }

        private void BindSlot(FieldSlotView slot, byte con, uint loc, uint seq, LiveCardState live, bool opponent)
        {
            slot.mappedCon = con;
            slot.mappedLoc = loc;
            slot.mappedSeq = seq;
            var battle = GetBattleCardDisplay(con, seq, loc);
            slot.liveCard = battle == null ? live : null;

            var shown = battle?.Readout?.Shown ?? battle?.Card ?? live;
            uint position = battle?.Position ?? shown?.position ?? 0;
            bool faceUp = (position & YgoConstants.POS_FACEUP) != 0;
            bool visible = battle?.Visible ?? (live != null && !IsArriving(con, loc, seq));
            bool interactive = battle == null && visible;
            int choiceIndex = -1;
            bool cardChoice = interactive && FindFieldCardChoice(con, loc, seq, out choiceIndex);
            bool cardSelected = cardChoice && _modals.IsFieldCardSelected(choiceIndex);
            bool targetChoice = cardChoice && _modals.IsFieldTargetSelection;
            SetTargetChoice(slot, targetChoice, cardSelected);
            string sumValue = cardChoice ? _modals.ReadFieldSumValue(choiceIndex) : null;
            DuelCardReadout readout = default;
            if (visible && faceUp && shown.code != 0)
            {
                readout = battle?.Readout != null || battle?.UseBattleStats == true
                    ? new DuelCardReadout(CardDatabase.Get(shown.code), shown, battle.UseBattleStats ? battle.Stats : null)
                    : ReadFieldStats(shown.code, battle?.UsePrintedReadout == true ? null : shown);
            }
            slot.cardView.gameObject.SetActive(visible);
            if (visible)
            {
                slot.cardView.SetCard(faceUp ? shown.code : 0u, !faceUp, readout.Live ?? shown);
                slot.cardView.transform.localRotation = Quaternion.Euler(0f, 0f, CardAngle(loc, position, opponent));
                slot.cardView.transform.localScale = Vector3.one * CardScale(slot.root, loc, position);
            }
            SetLiveLinkBeams(slot, visible && faceUp && loc == YgoConstants.LOCATION_MZONE ? readout.Live ?? shown : null, opponent);
            SetDisabledChains(slot, visible && faceUp && loc == YgoConstants.LOCATION_MZONE && readout.Disabled);
            if (visible && battle?.HideReadout != true)
            {
                var rating = readout.RatingAt(loc, seq);
                if (loc == YgoConstants.LOCATION_MZONE && readout.IsMonster || rating.Kind == DuelRatingKind.Scale ||
                    readout.CounterCount != 0 || sumValue != null)
                {
                    slot.readout ??= new DuelFieldReadout(slot.root);
                    slot.readout.Bind(readout, opponent, rating, position, sum: (sumValue, choiceIndex < 0, cardSelected));
                    slot.readout.FollowFocus(slot.cardArea.anchoredPosition, slot.focusArt.localScale.x);
                }
                else slot.readout?.Hide();
            }
            else slot.readout?.Hide();

            bool canDrag = interactive && CanAcceptPlayerAction && !IsStagingPlacement && !_modals.IsFieldCardSelection &&
                !opponent && loc == YgoConstants.LOCATION_MZONE;
            slot.dragTrigger.SourceType = canDrag ? DragSourceType.FieldMonster : DragSourceType.None;
            slot.dragTrigger.IndexGetter = canDrag ? () => (int)seq : null;
            slot.dragTrigger.DataGetter = canDrag ? () => (con, loc, seq) : null;
            if (!visible)
                ResetFieldCardFocus(slot);
            if (!interactive)
            {
                ClearPointerCard(slot.focusArt);
                if (_pointerActionAnchor == slot.root) _pointerActionAnchor = null;
            }
            uint previewCode = interactive && (live.IsFaceUp || live.isPublic || !opponent) ? live.code : 0;
            slot.dragTrigger.CardCodeGetter = previewCode != 0 ? () => previewCode : null;
            slot.dragTrigger.OnTouchHold = previewCode != 0 ? () =>
            {
                if (CanOpenTouchCardDetails) ShowCardZoomModal(live);
            } : null;
            if (interactive) RefreshPointerCard(slot.focusArt, previewCode);
            slot.dragTrigger.OnPointerEnterAction = battle != null ? null : () =>
            {
                TrackPointerCard(visible ? slot.focusArt : null, previewCode, visible ? slot : null);
                PointAtCardActions(visible ? slot.root : null);
            };
            slot.dragTrigger.OnPointerExitAction = () =>
            {
                ClearPointerCard(slot.focusArt);
                LeaveCardActions(slot.root);
            };
            slot.dragTrigger.OnRightClick = battle != null ? null : () => InspectCard(previewCode, slot);
            slot.dragTrigger.OnLeftClick = () => OnSlotClicked(con, loc, seq);
            slot.dragTrigger.OnTouchClick = () => OnSlotClicked(con, loc, seq, touch: true);

            bool selected = IsPlacePending(con, loc, seq);
            bool place = IsPlaceSelectable(con, loc, seq);
            bool drag = !opponent && (loc == YgoConstants.LOCATION_MZONE && _dragHighlightMZone ||
                loc == YgoConstants.LOCATION_SZONE && _dragHighlightSZone);
            var gesture = CardDragManager.Instance;
            bool handDrag = gesture is { IsDragging: true, CurrentSource: DragSourceType.Hand };
            bool dragCandidate = handDrag && drag && (place ||
                CanPlaceHandCard(CardDatabase.Get(gesture.CurrentCode), con, loc, seq));
            slot.bgImg.SetDragCandidate(dragCandidate);
            bool attack = opponent && loc == YgoConstants.LOCATION_MZONE && _dragHighlightAttack && interactive;
            bool action = interactive && CanAcceptPlayerAction && HasFieldAction(con, loc, seq);
            Color highlight = targetChoice ? Color.clear : cardSelected ? DuelStyle.Gold : attack ? DuelStyle.Opponent :
                cardChoice || selected || action ? DuelStyle.Player :
                place ? new Color(.98f, .89f, .29f) :
                dragCandidate ? DuelStyle.ChainCyan : drag && !handDrag && live == null ? DuelStyle.Gold : Color.clear;
            slot.bgImg.color = highlight.a > 0f
                ? new Color(highlight.r, highlight.g, highlight.b, selected || cardSelected || attack ? .64f : place ? .09f : .32f) : ZoneTint;
            slot.border.effectColor = place ? Color.clear : highlight;
            bool reserved = !visible && CardAnimationManager.Instance?.IsFieldArrivalReserved(con, loc, seq) == true;
            slot.bgImg.SetPlacementHighlight(place, reserved);
        }

        private static float CardAngle(uint location, uint position, bool opponent)
        {
            if (location == YgoConstants.LOCATION_MZONE && (position & YgoConstants.POS_DEFENSE) != 0)
                return opponent ? 90f : -90f;
            return opponent && location != YgoConstants.LOCATION_HAND ? 180f : 0f;
        }

        private static float CardScale(RectTransform cell, uint location, uint position)
        {
            if (location != YgoConstants.LOCATION_MZONE || (position & YgoConstants.POS_DEFENSE) == 0)
                return 1f;
            var size = Vector2.Scale(cell.rect.size, SlotCardMax - SlotCardMin);
            float uprightHeight = Mathf.Min(size.y, size.x / DuelUiKit.CardAspectRatio);
            return Mathf.Clamp01(cell.rect.width * .94f / Mathf.Max(1f, uprightHeight));
        }

        private void RefreshHand(IReadOnlyList<LiveCardState> cards, byte controller, bool opponent)
        {
            var row = opponent ? _oppHandBar : _myHandContent;
            var views = opponent ? _oppHandViews : _myHandViews;
            if (opponent) _oppHandSize = row.rect.size;
            else
            {
                _myHandSize = _myHandBar.rect.size;
                if (_myHandScroll != null)
                {
                    // Preserve the horizontal scroll edge while giving a lifted portrait room above the row.
                    var padding = new Vector4(0f, 0f, 0f, -_myHandMask.canvasRect.height * .25f);
                    if (_myHandMask.padding != padding) _myHandMask.padding = padding;
                    float extent = Mathf.Max(_myHandSize.x, MobileHandCardWidth(row) * cards.Count + 4f * (cards.Count + 1));
                    row.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, extent);
                    if (row.anchoredPosition.x < _myHandSize.x - extent)
                        row.anchoredPosition = new Vector2(_myHandSize.x - extent, row.anchoredPosition.y);
                }
            }
            while (views.Count < cards.Count) views.Add(CreateHandCard(row, opponent));

            for (int i = 0; i < views.Count; i++)
            {
                var view = views[i];
                bool visible = i < cards.Count && cards[i] != null && view.PendingArrival == null;
                if (!visible)
                {
                    ClearPointerCard((RectTransform)view.Card.transform);
                    if (view.Rect == _pointerActionAnchor) _pointerActionAnchor = null;
                }
                bool wasVisible = view.Rect.gameObject.activeSelf;
                view.Rect.gameObject.SetActive(visible);
                if (!visible)
                {
                    view.HasLayout = false;
                    continue;
                }

                view.Layout = HandCardBounds(row, cards.Count, i, opponent);
                if (!view.HasLayout || !wasVisible || !Application.isPlaying)
                    ApplyHandLayout(view, 1f);
                view.HasLayout = true;

                var card = cards[i];
                uint sequence = (uint)i;
                bool showFront = !opponent || card.isPublic || card.IsFaceUp;
                view.Card.SetCard(showFront ? card.code : 0u, !showFront, card);
                bool actionable = !opponent && CanAcceptPlayerAction && !IsStagingPlacement && HasHandAction(controller, sequence);

                var trigger = view.Trigger;
                if (trigger == null) continue;
                trigger.SourceType = actionable ? DragSourceType.Hand : DragSourceType.None;
                trigger.CardCodeGetter = () => card.code;
                trigger.OnTouchHold = () =>
                {
                    if (CanOpenTouchCardDetails) ShowCardZoomModal(card);
                };
                trigger.IndexGetter = actionable ? () => (int)sequence : null;
                var art = (RectTransform)view.Card.transform;
                RefreshPointerCard(art, card.code);
                trigger.OnPointerEnterAction = () =>
                {
                    TrackPointerCard(art, card.code);
                    PointAtCardActions(view.Rect);
                };
                trigger.OnPointerExitAction = () =>
                {
                    ClearPointerCard(art);
                    LeaveCardActions(view.Rect);
                };
                trigger.OnRightClick = () => InspectCardAt(card.code, null, appearance: card.Appearance);
                trigger.OnLeftClick = () =>
                {
                    InspectCardAt(card.code, null, appearance: card.Appearance);
                    if (_modals.FocusChainActivationAt(_session, YgoConstants.LOCATION_HAND, sequence)) return;
                    ShowUnavailableEffect(card);
                    PointAtCardActions(view.Rect);
                };
                trigger.OnTouchClick = () =>
                {
                    InspectCardAt(card.code, null, appearance: card.Appearance);
                    if (_modals.FocusChainActivationAt(_session, YgoConstants.LOCATION_HAND, sequence)) return;
                    SelectTouchCardActions(view.Rect);
                };
            }
            UpdateHandDepth(views, opponent ? null : _raisedCard);
            if (!opponent) RefreshHandGlows(force: true);
        }

        private static void UpdateHandDepth(List<HandCardView> views, RectTransform raised)
        {
            RectTransform front = null;
            for (int i = 0; i < views.Count; i++)
            {
                var view = views[i];
                view.Rect.SetSiblingIndex(i);
                if (view.Card.transform == raised) front = view.Rect;
            }
            front?.SetAsLastSibling();
        }

        private static void ApplyHandLayout(HandCardView view, float step)
        {
            var target = view.Layout;
            var rect = view.Rect;
            Vector2 min = Vector2.Lerp(rect.anchorMin, target.min, step);
            Vector2 max = Vector2.Lerp(rect.anchorMax, target.max, step);
            float angle = Mathf.LerpAngle(rect.localEulerAngles.z, target.angle, step);
            min = (min - target.min).sqrMagnitude < .00000001f ? target.min : min;
            max = (max - target.max).sqrMagnitude < .00000001f ? target.max : max;
            if (rect.anchorMin != min) rect.anchorMin = min;
            if (rect.anchorMax != max) rect.anchorMax = max;
            if (Mathf.Abs(Mathf.DeltaAngle(angle, target.angle)) < .02f) angle = target.angle;
            if (Mathf.Abs(Mathf.DeltaAngle(rect.localEulerAngles.z, angle)) > .001f)
                rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void AdvanceHandLayout(float dt)
        {
            float step = 1f - Mathf.Exp(-20f * dt);
            foreach (var view in _myHandViews)
                if (view.HasLayout) ApplyHandLayout(view, step);
            foreach (var view in _oppHandViews)
                if (view.HasLayout) ApplyHandLayout(view, step);
        }
        private void RefreshHandGlows(bool force = false)
        {
            DuelPrompt prompt = CanAcceptPlayerAction && !IsStagingPlacement && !IsBrowsingZone && !_cardZoomModal.activeSelf && !_phasePicker.activeSelf
                ? _session.ActivePrompt : null;
            bool changed = force || !ReferenceEquals(prompt, _handGlowPrompt);
            if (!changed && prompt == null) return;
            _handGlowPrompt = prompt;
            var hand = prompt == null ? null : _session.Players[_session.HumanPlayerIndex].hand;
            var mode = _app?.ChainConfirmation ?? ChainConfirmationMode.Auto;
            for (int i = 0; i < _myHandViews.Count; i++)
            {
                var view = _myHandViews[i];
                var glow = view.PlayableGlow;
                if (changed)
                {
                    var card = hand != null && i < hand.Count ? hand[i] : null;
                    bool playable = view.Rect.gameObject.activeSelf && card != null &&
                        (HasHandAction(card.controller, card.sequence) ||
                         _session.HasEffectActivationAt(prompt, mode, card.controller, YgoConstants.LOCATION_HAND, card.sequence));
                    DuelStyle.SetActivationGlow(glow, playable);
                    SetAvailabilityDim(view.FocusArt, ref view.UnavailableShade, !playable && CanDimUnavailableEffect(prompt, card));
                }
                else if (glow.gameObject.activeSelf) glow.canvasRenderer.SetAlpha(DuelStyle.ActivationPulse);
            }

            foreach (var slot in _fieldSlotsByRect.Values)
            {
                var glow = slot.activationGlow;
                if (changed)
                {
                    bool playable = prompt != null && slot.cardView.gameObject.activeSelf &&
                        _session.HasEffectActivationAt(prompt, mode, slot.mappedCon, slot.mappedLoc, slot.mappedSeq);
                    DuelStyle.SetActivationGlow(glow, playable);
                    SetAvailabilityDim(slot.focusArt, ref slot.unavailableShade,
                        !playable && CanDimUnavailableEffect(prompt, slot.liveCard));
                }
                else if (glow.gameObject.activeSelf) glow.canvasRenderer.SetAlpha(DuelStyle.ActivationPulse);
            }
            foreach (var tile in _stackTilesByRect.Values)
            {
                var glow = tile.activationGlow;
                if (glow == null) continue;
                if (changed)
                    DuelStyle.SetActivationGlow(glow, prompt != null &&
                        _session.HasEffectActivationAt(prompt, mode, tile.controller, tile.location));
                else if (glow.gameObject.activeSelf) glow.canvasRenderer.SetAlpha(DuelStyle.ActivationPulse);
            }
        }

        private bool IsPointOverEffectSource(Vector2 point, Camera camera)
        {
            if (!CanAcceptPlayerAction || !_modals.IsChainPromptOpen || IsReadingCardDetails || IsBrowsingZone) return false;
            foreach (var view in _myHandViews)
                if (view.PlayableGlow.isActiveAndEnabled && RectTransformUtility.RectangleContainsScreenPoint(view.Rect, point, camera))
                    return true;
            foreach (var slot in _fieldSlotsByRect.Values)
                if (slot.activationGlow.isActiveAndEnabled && RectTransformUtility.RectangleContainsScreenPoint(slot.root, point, camera))
                    return true;
            foreach (var tile in _stackTilesByRect.Values)
                if (tile.activationGlow != null && tile.activationGlow.isActiveAndEnabled &&
                    RectTransformUtility.RectangleContainsScreenPoint(tile.root, point, camera)) return true;
            return false;
        }

        private static HandCardView CreateHandCard(RectTransform row, bool opponent)
        {
            var rect = DuelUiKit.FillRect("HandCard", row);
            var card = CompositeCardView.Create(rect, "CardView");
            card.UsePrintedFaceOnly(showEditionBadge: false);
            var art = (RectTransform)card.transform.Find("AspectBox");
            CardDragTrigger trigger = null;
            Image glow = null;
            if (!opponent)
            {
                DuelUiKit.AddImage(rect.gameObject, DuelUiKit.WhiteSprite, Color.clear);
                glow = DuelStyle.AddActivationGlow(art);
                trigger = rect.gameObject.AddComponent<CardDragTrigger>();
            }
            return new HandCardView
            {
                Rect = rect,
                Card = card,
                FocusArt = art,
                PlayableGlow = glow,
                Trigger = trigger,
            };
        }

        private const float HandFaceHeight = .96f;

        private static float MobileHandCardWidth(RectTransform row) =>
            Mathf.Max(MobileSafeArea.MinimumTouchSize(row), HandFaceHeight * row.rect.height * DuelUiKit.CardAspectRatio);

        private (Vector2 min, Vector2 max, float angle) HandCardBounds(RectTransform row, int count, int index, bool opponent)
        {
            if (!opponent && _myHandScroll != null)
            {
                float widthUnits = MobileHandCardWidth(row);
                float rowWidth = Mathf.Max(1f, row.rect.width);
                float left = Mathf.Max(4f, (rowWidth - widthUnits * count - 4f * (count - 1)) * .5f);
                float xStart = (left + (widthUnits + 4f) * index) / rowWidth;
                float cardHeight = Mathf.Clamp01(Mathf.Max(HandFaceHeight,
                    MobileSafeArea.MinimumTouchSize(row) / Mathf.Max(1f, row.rect.height)));
                return (new Vector2(xStart, .5f * (1f - cardHeight)),
                    new Vector2(xStart + widthUnits / rowWidth, .5f * (1f + cardHeight)), 0f);
            }
            const float height = HandFaceHeight;
            float width = Mathf.Clamp(row.rect.height * height * DuelUiKit.CardAspectRatio / Mathf.Max(1f, row.rect.width),
                0.045f, 0.25f);
            float spacing = Mathf.Min(width * .90f, (0.93f - width) / Mathf.Max(1, count - 1));
            float x = (1f - width - spacing * (count - 1)) * 0.5f + spacing * index;
            float fan = count <= 1 ? 0f : (2f * index / (count - 1) - 1f) * Mathf.Min(1f, (count - 1) / 4f);
            float y = (1f - height) * .5f + (opponent ? 1f : -1f) * fan * fan * .018f;
            return (new Vector2(x, y), new Vector2(x + width, y + height), -fan * 7f + (opponent ? 180f : 0f));
        }

        private void RefreshLogFeed()
        {
            if (_session == null) return;
            int start = Mathf.Max(0, _session.Logs.Count - 2);
            _logFeed.text = string.Join("\n", _session.Logs.Skip(start));
        }

        private void BuildDuelLog(Transform root)
        {
            var dim = DuelUiKit.FillRect("Modal_DuelLog", root);
            _logWindow = dim.gameObject;
            DuelUiKit.AddImage(_logWindow, DuelUiKit.WhiteSprite, new Color(0f, 0.02f, 0.08f, 0.82f));
            var panel = DuelStyle.Panel(dim, "Window", new Vector2(0.20f, 0.15f), new Vector2(0.80f, 0.86f));
            var header = DuelUiKit.CreateRect("Header", panel, new Vector2(.03f, _mobileLayout ? .72f : .87f),
                new Vector2(.97f, .97f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddText(header, "Title", "대전 기록", 25, TextAnchor.MiddleLeft, DuelUiKit.GoldAccent, FontStyle.Bold);
            var close = DuelUiKit.CreateRect("Close", header, new Vector2(.84f, _mobileLayout ? .07f : .12f),
                new Vector2(1f, _mobileLayout ? .93f : .88f),
                Vector2.zero, Vector2.zero);
            DuelStyle.Button(close, "Button", "닫기", CloseDuelLog, 15);

            var body = DuelStyle.Panel(panel, "Log", new Vector2(.026f, .035f), new Vector2(.974f, _mobileLayout ? .70f : .85f));
            var viewport = DuelUiKit.CreateRect("Viewport", body, Vector2.zero, Vector2.one,
                new Vector2(8f, 5f), new Vector2(-20f, -5f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = DuelUiKit.CreateRect("Content", viewport, Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 6, 6);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _logScroll = body.gameObject.AddComponent<ScrollRect>();
            _logScroll.viewport = viewport;
            _logScroll.content = content;
            _logScroll.horizontal = false;
            _logScroll.scrollSensitivity = 45f;
            _logScroll.movementType = ScrollRect.MovementType.Clamped;
            _logScroll.verticalScrollbar = DuelUiKit.AddVerticalScrollbar(body);
            _logScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            _logWindow.SetActive(false);
        }

        private void OpenDuelLog()
        {
            if (_session == null || IsPaused || _actionPopup.activeSelf || _cardZoomModal.activeSelf ||
                _modals.IsPromptOpen || _modals.ZoneViewRoot.activeSelf || _modals.ResultRoot.activeSelf) return;
            CardDragManager.Instance?.CancelDrag();
            if (CardAnimationManager.Instance != null) CardAnimationManager.Instance.Paused = true;
            _logWindow.transform.SetAsLastSibling();
            _logWindow.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(null);

            const int entriesPerBlock = 20;
            var log = _session.Logs;
            int blocks = Mathf.Max(1, (log.Count + entriesPerBlock - 1) / entriesPerBlock);
            while (_logBlocks.Count < blocks)
            {
                var text = DuelUiKit.AddText(_logScroll.content, "Entries", "", 17, TextAnchor.UpperLeft, DuelUiKit.TextPrimary);
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.supportRichText = false;
                text.lineSpacing = 1.16f;
                _logBlocks.Add(text);
            }
            for (int i = 0; i < _logBlocks.Count; i++)
            {
                _logBlocks[i].gameObject.SetActive(i < blocks);
                if (i >= blocks) continue;
                _logBlocks[i].text = log.Count == 0 ? "대전 기록이 없습니다."
                    : string.Join("\n", log.Skip(i * entriesPerBlock).Take(entriesPerBlock));
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_logScroll.content);
            Canvas.ForceUpdateCanvases();
            _logScroll.StopMovement();
            _logScroll.verticalNormalizedPosition = 0f;
        }

        private void CloseDuelLog()
        {
            if (!IsReadingLog) return;
            _logWindow.SetActive(false);
            if (CardAnimationManager.Instance != null) CardAnimationManager.Instance.Paused = IsPaused;
        }

        // ==================== DRAG & DROP LOGIC ====================
        private void HandleDuelDragStarted(DragSourceType source, uint cardCode, int sourceIndex, object customData)
        {
            if (!Root.activeInHierarchy || !CanAcceptPlayerAction || IsStagingPlacement) return;
            InspectCard(cardCode);

            _dragHighlightMZone = false;
            _dragHighlightSZone = false;
            _dragHighlightAttack = false;

            var p = _session.ActivePrompt;
            if (p == null || p.playerId != _session.HumanPlayerIndex) return;

            byte me = (byte)_session.HumanPlayerIndex;
            if (source == DragSourceType.Hand)
            {
                var card = CardDatabase.Get(cardCode);
                var actions = CollectActions(me, YgoConstants.LOCATION_HAND, (uint)sourceIndex);
                foreach (var act in actions)
                {
                    uint placement = HandActionPlacement(p.messageType, card, act.responseVal);
                    _dragHighlightMZone |= placement == YgoConstants.LOCATION_MZONE;
                    _dragHighlightSZone |= placement == YgoConstants.LOCATION_SZONE;
                }
            }
            else if (source == DragSourceType.ExtraDeck)
            {
                _dragHighlightMZone = true;
                _modals.CloseZoneViewer();
            }
            else if (source == DragSourceType.FieldMonster)
            {
                if (p.messageType == YgoConstants.MSG_SELECT_BATTLECMD)
                {
                    _dragHighlightAttack = p.attackable.Any(a => a.sequence == (uint)sourceIndex);
                }
            }

            Refresh();
        }

        private void HandleDuelDragCancelled()
        {
            if (_dragHighlightMZone || _dragHighlightSZone || _dragHighlightAttack)
            {
                _dragHighlightMZone = false;
                _dragHighlightSZone = false;
                _dragHighlightAttack = false;
                SetDragColumnGuide(-1);
                Refresh();
            }
        }

        private void HandleDuelCardDrop(DragContext ctx)
        {
            SetDragColumnGuide(-1);
            _dragHighlightMZone = false;
            _dragHighlightSZone = false;
            _dragHighlightAttack = false;

            if (!Root.activeInHierarchy || !CanAcceptPlayerAction || IsStagingPlacement) return;
            var p = _session.ActivePrompt;
            if (p == null || p.playerId != _session.HumanPlayerIndex)
            {
                Refresh();
                return;
            }

            byte me = (byte)_session.HumanPlayerIndex;

            // 1. Hand Card Dragged
            if (ctx.SourceType == DragSourceType.Hand)
            {
                uint handSeq = (uint)ctx.SourceIndex;

                // Check Monster Zones
                for (int i = 0; i < 5; i++)
                {
                    if (CardDragManager.ContainsScreenPoint(_myMZone[i].root, ctx.DropPosition))
                    {
                        HandleHandDropOnZone(me, YgoConstants.LOCATION_MZONE, (uint)i, handSeq, ctx.CardCode);
                        return;
                    }
                }
                for (int i = 0; i < 2; i++)
                {
                    if (CardDragManager.ContainsScreenPoint(_emz[i].root, ctx.DropPosition))
                    {
                        uint emzSeq = (uint)(i == 0 ? 5 : 6);
                        HandleHandDropOnZone(me, YgoConstants.LOCATION_MZONE, emzSeq, handSeq, ctx.CardCode);
                        return;
                    }
                }

                // Check Spell/Trap Zones
                for (int i = 0; i < 6; i++)
                {
                    if (CardDragManager.ContainsScreenPoint(_mySZone[i].root, ctx.DropPosition))
                    {
                        HandleHandDropOnZone(me, YgoConstants.LOCATION_SZONE, (uint)i, handSeq, ctx.CardCode);
                        return;
                    }
                }

                // Dropped anywhere on the Duel Board (above the hand bar)
                if (CardDragManager.ContainsScreenPoint(_boardRect, ctx.DropPosition))
                {
                    HandleHandDropOnBoard(handSeq, ctx.CardCode);
                    return;
                }

                Refresh();
            }
            // 2. Extra Deck Monster Dragged onto Board
            else if (ctx.SourceType == DragSourceType.ExtraDeck)
            {
                if (p.messageType != YgoConstants.MSG_SELECT_IDLECMD ||
                    ctx.CustomData is not LiveCardState card || !_session.Players[me].extra.Contains(card))
                {
                    Refresh();
                    return;
                }

                int target = Array.FindIndex(_myMZone, slot => CardDragManager.ContainsScreenPoint(slot.root, ctx.DropPosition));
                if (target < 0)
                {
                    int extraTarget = Array.FindIndex(_emz, slot => CardDragManager.ContainsScreenPoint(slot.root, ctx.DropPosition));
                    if (extraTarget >= 0) target = 5 + extraTarget;
                }

                if (target >= 0)
                {
                    var summons = CollectActions(me, YgoConstants.LOCATION_EXTRA, card.sequence, card.code)
                        .Where(action => (action.responseVal & 0xffff) == 1).ToList();
                    if (summons.Count > 0)
                    {
                        var placement = (me, YgoConstants.LOCATION_MZONE, (uint)target);
                        if (summons.Count == 1) ExecuteActionInt(summons[0].responseVal, placement);
                        else PopulateAndShowActionPopup(CardDatabase.GetName(card.code), summons, null, placement: placement);
                        return;
                    }
                }
                Refresh();
            }
            // 3. Field Monster Dragged (e.g. to Attack or Activate/Reposition!)
            else if (ctx.SourceType == DragSourceType.FieldMonster)
            {
                uint mySeq = (uint)ctx.SourceIndex;

                // Check Attack on Opponent Monster
                for (int i = 0; i < 5; i++)
                {
                    if (CardDragManager.ContainsScreenPoint(_oppMZone[i].root, ctx.DropPosition))
                    {
                        TryAttackOpponentSlot(mySeq, (uint)i, isDirect: false);
                        return;
                    }
                }

                // Check Attack on Opponent EMZ Monster
                for (int i = 0; i < 2; i++)
                {
                    if (_emz[i] != null && _emz[i].mappedCon == (byte)_session.AiPlayerIndex && _emz[i].liveCard != null)
                    {
                        if (CardDragManager.ContainsScreenPoint(_emz[i].root, ctx.DropPosition))
                        {
                            TryAttackOpponentSlot(mySeq, _emz[i].mappedSeq, isDirect: false);
                            return;
                        }
                    }
                }

                // Check Direct Attack on Opponent Bust / HUD / Opponent half of the screen
                if (CardDragManager.ContainsScreenPoint(_oppBustImg.rectTransform, ctx.DropPosition) ||
                    CardDragManager.ContainsScreenPoint(_oppLpText.rectTransform, ctx.DropPosition) ||
                    CardDragManager.ContainsScreenPoint(_oppHandBar, ctx.DropPosition) ||
                    (p.messageType == YgoConstants.MSG_SELECT_BATTLECMD && ctx.DropPosition.y > Screen.height * 0.54f))
                {
                    TryAttackOpponentSlot(mySeq, 0, isDirect: true);
                    return;
                }

                // Dropped on itself or elsewhere -> Show actions popup
                ShowCardActionsPopup(me, YgoConstants.LOCATION_MZONE, mySeq, ctx.CardCode, null);
                Refresh();
            }
        }

        private void HandleHandDropOnBoard(uint handSeq, uint cardCode)
        {
            var p = _session.ActivePrompt;
            if (p == null) return;

            byte me = (byte)_session.HumanPlayerIndex;
            if (!MatchesDraggedHandCard(me, handSeq, cardCode))
            {
                Refresh();
                return;
            }
            var actions = CollectActions(me, YgoConstants.LOCATION_HAND, handSeq);
            if (actions.Count == 1)
            {
                ExecuteActionInt(actions[0].responseVal);
            }
            else if (actions.Count > 1)
            {
                ShowCardActionsPopup(me, YgoConstants.LOCATION_HAND, handSeq, cardCode, null, actions: actions);
            }
            else
            {
                Refresh();
            }
        }

        private void HandleHandDropOnZone(byte con, uint loc, uint seq, uint handSeq, uint cardCode)
        {
            var p = _session.ActivePrompt;
            if (p == null) return;

            // If prompt is zone placement (MSG_SELECT_PLACE)
            if (p.messageType == YgoConstants.MSG_SELECT_PLACE || p.messageType == YgoConstants.MSG_SELECT_DISFIELD)
            {
                if (IsPlaceSelectable(con, loc, seq))
                {
                    OnSlotClicked(con, loc, seq);
                }
                else
                {
                    Refresh();
                }
                return;
            }

            var card = CardDatabase.Get(cardCode);
            if (!MatchesDraggedHandCard(con, handSeq, cardCode) || !CanPlaceHandCard(card, con, loc, seq))
            {
                Refresh();
                return;
            }

            var actions = CollectActions(con, YgoConstants.LOCATION_HAND, handSeq);
            actions.RemoveAll(action => HandActionPlacement(p.messageType, card, action.responseVal) != loc);
            var placement = (con, loc, seq);
            if (actions.Count == 1)
                ExecuteActionInt(actions[0].responseVal, placement);
            else if (actions.Count > 1)
                ShowCardActionsPopup(con, YgoConstants.LOCATION_HAND, handSeq, cardCode, null, placement, actions);
            else
                Refresh();
        }

        private bool MatchesDraggedHandCard(byte controller, uint sequence, uint code)
        {
            var hand = _session.Players[controller].hand;
            return sequence < hand.Count && hand[(int)sequence]?.code == code;
        }

        private static uint HandActionPlacement(byte messageType, CardInfo card, int response)
        {
            int command = response & 0xffff;
            if (messageType == YgoConstants.MSG_SELECT_IDLECMD)
            {
                if (command is 0 or 1 or 3) return YgoConstants.LOCATION_MZONE;
                if (command == 4) return YgoConstants.LOCATION_SZONE;
            }

            bool activation = messageType == YgoConstants.MSG_SELECT_IDLECMD && command == 5 ||
                messageType == YgoConstants.MSG_SELECT_BATTLECMD && command == 0;
            return activation && card != null && (card.IsSpell || card.IsTrap || card.IsPendulum)
                ? YgoConstants.LOCATION_SZONE : 0;
        }

        private void TryAttackOpponentSlot(uint mySeq, uint targetSeq, bool isDirect)
        {
            var p = _session.ActivePrompt;
            if (p == null || p.playerId != _session.HumanPlayerIndex) return;

            if (p.messageType == YgoConstants.MSG_SELECT_BATTLECMD)
            {
                for (int i = 0; i < p.attackable.Count; i++)
                {
                    var atker = p.attackable[i];
                    if (atker.sequence == mySeq)
                    {
                        _preferredAttackTargetSeq = isDirect ? -1 : (int)targetSeq;
                        DuelAudio.PlayAttack();
                        ExecuteActionInt((i << 16) | 1);
                        return;
                    }
                }
            }
            Refresh();
        }

        private StackTileView GetStack(byte controller, uint location)
        {
            bool opponent = controller != _session.HumanPlayerIndex;
            return location switch
            {
                YgoConstants.LOCATION_DECK => opponent ? _oppDeckTile : _myDeckTile,
                YgoConstants.LOCATION_EXTRA => opponent ? _oppExtraTile : _myExtraTile,
                YgoConstants.LOCATION_GRAVE => opponent ? _oppGyTile : _myGyTile,
                YgoConstants.LOCATION_REMOVED => opponent ? _oppBanTile : _myBanTile,
                _ => null,
            };
        }

        private CardPose GetCardPose(byte controller, uint location, uint sequence, uint position, int handCount = -1)
        {
            var animator = CardAnimationManager.Instance;
            bool material = (location & YgoConstants.LOCATION_OVERLAY) != 0;
            location &= ~YgoConstants.LOCATION_OVERLAY;
            if (animator == null || controller >= _handCounts.Length) return default;

            bool opponent = controller != _session.HumanPlayerIndex;
            uint cardPosition = material ? YgoConstants.POS_FACEUP_ATTACK : position;
            float angle = CardAngle(location, cardPosition, opponent);
            if (location == YgoConstants.LOCATION_HAND)
            {
                var row = opponent ? _oppHandBar : _myHandContent;
                int count = Mathf.Max(1, handCount >= 0 ? handCount : _handCounts[controller]);
                int index = Mathf.Clamp((int)sequence, 0, count - 1);
                CardPose ReadHandPose()
                {
                    if (row == null || animator == null) return default;
                    var bounds = HandCardBounds(row, count, index, opponent);
                    return animator.GetPose(row, (bounds.min + bounds.max) * .5f, bounds.max - bounds.min, bounds.angle);
                }
                return ReadHandPose().FollowLayout(ReadHandPose);
            }

            var slot = GetSlot(controller, location, sequence);
            var rect = slot?.root ?? GetStack(controller, location)?.cardArea;
            if (rect == null) return default;
            var center = Vector2.one * .5f;
            var size = slot != null ? SlotCardMax - SlotCardMin : Vector2.one;
            if (material)
            {
                // The engine stores the material index in the position field for overlay locations.
                center += new Vector2(.035f * Math.Min(position, 3u), opponent ? .13f : -.13f);
            }
            CardPose ReadSlotPose()
            {
                if (rect == null || animator == null) return default;
                float scale = CardScale(rect, location, cardPosition) * (material ? .60f : 1f);
                return animator.GetPose(rect, center, size, angle, scale);
            }
            return ReadSlotPose().FollowLayout(ReadSlotPose);
        }

        private CardPose GetDisplayedCardPose(byte controller, uint location, uint sequence, uint code, uint position)
        {
            CompositeCardView view;
            RectTransform art;
            if (location == YgoConstants.LOCATION_HAND)
            {
                var views = GetHandViews(controller);
                if (sequence >= views.Count) return default;
                var hand = views[(int)sequence];
                if (!hand.HasLayout) return default;
                view = hand.Card;
                art = hand.FocusArt;
            }
            else
            {
                var slot = GetSlot(controller, location, sequence);
                if (slot == null || slot.mappedCon != controller || slot.mappedLoc != location || slot.mappedSeq != sequence)
                    return default;
                view = slot.cardView;
                art = slot.focusArt;
            }

            if (!view.gameObject.activeInHierarchy || code != 0 && view.CurrentCode != 0 && view.CurrentCode != code)
                return default;
            var visual = CardAnimationManager.Instance.GetVisualPose(art);
            var resting = GetCardPose(controller, location, sequence, position);
            if (!visual.IsValid || !resting.IsValid) return visual;

            // A departing card keeps its hover lift even if its widget is recycled before playback.
            Vector2 lift = visual.Position - resting.Position;
            float focusScale = visual.Size.y / resting.Size.y;
            return visual.FollowLayout(() =>
            {
                var current = resting.AtLayout();
                return new CardPose(current.Position + lift * (current.Size.y / resting.Size.y),
                    current.Size * focusScale, visual.Angle);
            });
        }

        private List<HandCardView> GetHandViews(byte controller) =>
            controller == _session.HumanPlayerIndex ? _myHandViews : _oppHandViews;

        private static void ResetHandViews(List<HandCardView> views)
        {
            foreach (var view in views)
            {
                view.HasLayout = false;
                view.PendingArrival = null;
            }
        }

        private void ClearArrivals()
        {
            _arrivingCards.Clear();
            foreach (var slot in _fieldSlotsByRect.Values) SetTargetChoice(slot, available: false, selected: false);
            _banishedPilePresentation.Clear();
            _materialReadouts.Clear();
            _presentedFieldReadouts.Clear();
            _battleCardDisplays.Clear();
            _movingInspections.Clear();
            _pendingCardReadouts.Clear();
            ResetHandViews(_myHandViews);
            ResetHandViews(_oppHandViews);
            if (_myHandScroll != null)
            {
                _myHandScroll.StopMovement();
                _myHandContent.anchoredPosition = Vector2.zero;
            }
        }

        private HandCardView EnsureHandView(byte controller, int index)
        {
            var views = GetHandViews(controller);
            bool opponent = controller != _session.HumanPlayerIndex;
            var row = opponent ? _oppHandBar : _myHandContent;
            while (views.Count <= index) views.Add(CreateHandCard(row, opponent));
            return views[index];
        }

        private void RemoveHandView(byte controller, uint sequence)
        {
            var views = GetHandViews(controller);
            if (sequence >= views.Count) return;
            var view = views[(int)sequence];
            views.RemoveAt((int)sequence);
            views.Add(view);
            var art = (RectTransform)view.Card.transform;
            ClearPointerCard(art);
            if (_duelCommandRaisedCard == art) _duelCommandRaisedCard = null;
            view.HasLayout = false;
            view.PendingArrival = null;
            view.Rect.gameObject.SetActive(false);
        }

        private void InsertHandView(byte controller, uint sequence, int occupied)
        {
            if (sequence >= occupied) return;
            var views = GetHandViews(controller);
            var view = EnsureHandView(controller, occupied);
            views.RemoveAt(occupied);
            views.Insert((int)sequence, view);
        }

        private FieldSlotView GetSlot(byte con, uint loc, uint seq)
        {
            if (_session == null) return null;
            bool isMe = con == _session.HumanPlayerIndex;
            if (loc == YgoConstants.LOCATION_MZONE)
            {
                if (seq == 5) return isMe ? _emz[0] : _emz[1];
                if (seq == 6) return isMe ? _emz[1] : _emz[0];
                if (seq < 5) return isMe ? _myMZone[seq] : _oppMZone[seq];
            }
            if (loc == YgoConstants.LOCATION_SZONE && seq < 6)
                return isMe ? _mySZone[seq] : _oppSZone[seq];
            return null;
        }

        private static (byte, uint, uint) ArrivalKey(byte controller, uint location, uint sequence)
        {
            bool indexed = (location & YgoConstants.LOCATION_ONFIELD) != 0;
            return (controller, location, indexed ? sequence : 0);
        }

        private bool IsArriving(byte controller, uint location, uint sequence)
        {
            return _arrivingCards.ContainsKey(ArrivalKey(controller, location, sequence));
        }

        private void ReleaseArrival(CardArrival arrival)
        {
            if (!_arrivingCards.TryGetValue(arrival.Slot, out var pending) || !pending.Remove(arrival)) return;
            if (arrival.Departing != null) arrival.Departing.AwaitingArrival = false;
            if (pending.Count == 0) _arrivingCards.Remove(arrival.Slot);
        }

        private Action ReserveCardArrival(byte controller, uint location, uint sequence, Action refresh = null)
        {
            if (location == YgoConstants.LOCATION_HAND)
            {
                var view = EnsureHandView(controller, (int)sequence);
                // The view follows the card when hand indices shift. An old animation must not reveal a reused view.
                var arrival = new object();
                view.PendingArrival = arrival;
                return () =>
                {
                    if (view.PendingArrival != arrival) return;
                    view.PendingArrival = null;
                    refresh?.Invoke();
                };
            }
            var key = ReserveArrival(controller, location, sequence);
            return () =>
            {
                ReleaseArrival(key);
                refresh?.Invoke();
            };
        }

        private CardArrival ReserveArrival(byte controller, uint location, uint sequence)
        {
            var key = ArrivalKey(controller, location, sequence);
            if (!_arrivingCards.TryGetValue(key, out var pending))
                _arrivingCards.Add(key, pending = new());
            var arrival = new CardArrival(key);
            pending.Add(arrival);
            return arrival;
        }

        private void CompleteFieldArrivals()
        {
            var animator = CardAnimationManager.Instance;
            foreach (var pending in _pendingXyzMaterials.Values)
            {
                // Some effects attach to a monster in the Extra Deck without summoning it.
                if (animator != null) animator.QueueXyzMaterials(pending.Select(material => material.Move).ToArray(), Refresh);
                else foreach (var material in pending) material.Move.Release?.Invoke();
            }
            _pendingXyzMaterials.Clear();
            animator?.CompleteFieldArrivals();
            if (animator != null)
                foreach (var link in _arrivalLinks)
                    QueueLinkArrivalRays(animator, link.Controller, link.Sequence, link.Markers);
            _arrivalLinks.Clear();
        }

        private DuelCardReadout ReadFieldStats(uint code, LiveCardState live)
        {
            if (live?.IsFaceUp == true &&
                _presentedFieldReadouts.TryGetValue((live.controller, live.location, live.sequence), out var presented) &&
                presented.Shown.code == code)
                live = presented.Shown;
            if (live != null && live.location == YgoConstants.LOCATION_MZONE &&
                _materialReadouts.TryGetValue((live.controller, live.sequence), out var held) && held.Source.code == code)
                return new DuelCardReadout(CardDatabase.Get(code), live,
                    materialReadout: (held.Count, held.Source));
            return new DuelCardReadout(CardDatabase.Get(code), live);
        }

        private void ClearMovedReadouts(CardMoveEvent move)
        {
            bool changesSlot = (move.fromCon, move.fromLoc, move.fromSeq) != (move.toCon, move.toLoc, move.toSeq);
            if (changesSlot || move.ChangesPositionInPlace || (move.toPos & YgoConstants.POS_FACEUP) == 0)
                Clear(move.fromCon, move.fromLoc, move.fromSeq);
            if (changesSlot) Clear(move.toCon, move.toLoc, move.toSeq);

            void Clear(byte controller, uint location, uint sequence)
            {
                _presentedFieldReadouts.Remove((controller, location & ~YgoConstants.LOCATION_OVERLAY, sequence));
                _movingInspections.Remove((controller, location, sequence));
                if (location == YgoConstants.LOCATION_MZONE) _materialReadouts.Remove((controller, sequence));
            }
        }

        private void QueueFieldReadouts(IReadOnlyList<(LiveCardState Before, LiveCardState After)> changes)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null || !Root.activeInHierarchy) return;
            var apply = PrepareFieldReadoutUpdate(changes);
            if (changes.Count == 1)
            {
                var card = changes[0].Before;
                var anchor = GetSlot(card.controller, card.location, card.sequence)?.focusArt;
                if (anchor != null && animator.TryQueueEquipReadout(anchor, card.code, apply)) return;
            }
            animator.QueueFieldReadoutUpdate(apply);
        }

        private void HandleCardEquipped(LiveCardState source, LiveCardState target,
            IReadOnlyList<(LiveCardState Before, LiveCardState After)> changes)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null || !Root.activeInHierarchy) return;
            var onContact = changes == null ? null : PrepareFieldReadoutUpdate(changes);
            var from = GetSlot(source.controller, source.location, source.sequence)?.focusArt;
            var to = GetSlot(target.controller, target.location, target.sequence)?.focusArt;
            if (from != null && to != null) animator.QueueEquip(from, to, target.code, onContact);
            else if (onContact != null) animator.QueueFieldReadoutUpdate(onContact);
        }

        private Action PrepareFieldReadoutUpdate(IReadOnlyList<(LiveCardState Before, LiveCardState After)> changes)
        {
            var updates = new (PresentedFieldReadout Readout, LiveCardState After)[changes.Count];
            for (int i = 0; i < changes.Count; i++)
            {
                var (before, after) = changes[i];
                var key = (before.controller, before.location, before.sequence);
                if (!_presentedFieldReadouts.TryGetValue(key, out var readout))
                {
                    readout = new PresentedFieldReadout { Shown = before };
                    _presentedFieldReadouts.Add(key, readout);
                }
                updates[i] = (readout, after);
            }
            return () =>
            {
                foreach (var (readout, after) in updates)
                    readout.Shown = after;
                Refresh();
            };
        }

        private Action HoldMaterialReadout(byte controller, uint location, uint sequence, int change)
        {
            if (location != FieldMaterials || controller >= _session.Players.Length) return null;
            var host = FindLiveCard(controller, YgoConstants.LOCATION_MZONE, sequence);
            if (host?.IsFaceUp != true || host.code == 0) return null;

            var slot = (controller, sequence);
            if (!_materialReadouts.TryGetValue(slot, out var readout))
            {
                readout = new MaterialReadout { Source = host, Count = host.overlayCodes.Count };
                _materialReadouts.Add(slot, readout);
            }
            readout.Pending++;
            return () =>
            {
                if (!_materialReadouts.TryGetValue(slot, out var current) || !ReferenceEquals(readout, current)) return;
                readout.Count = Math.Max(0, readout.Count + change);
                if (--readout.Pending == 0) _materialReadouts.Remove(slot);
            };
        }

        private void CaptureCardReadouts()
        {
            // Read the field refreshed for this response before playing its queued animations.
            if (_pendingCardReadouts.Count == 0) return;
            var animator = CardAnimationManager.Instance;
            foreach (var (slot, readout) in _pendingCardReadouts.ToArray())
            {
                // Field modifiers may not apply during the placement of the other cards in this summon.
                if (animator?.IsFieldArrivalReserved(slot.Controller, slot.Location, slot.Sequence) == true) continue;
                if (readout.DestinationCard == null)
                {
                    var live = _session.GetVisibleFieldCard(readout.Code, slot.Controller, slot.Location, slot.Sequence);
                    if (slot.Location == YgoConstants.LOCATION_MZONE || live?.IsPendulumScale == true)
                        readout.DestinationCard = ReadFieldStats(readout.Code, live).Live;
                }
                _pendingCardReadouts.Remove(slot);
            }
        }

        private bool IsFaceDown(byte controller, uint location, uint position)
        {
            if ((location & YgoConstants.LOCATION_OVERLAY) != 0) return false;
            if (location == YgoConstants.LOCATION_HAND)
                return controller != _session.HumanPlayerIndex && (position & YgoConstants.POS_FACEUP) == 0;
            if (location == YgoConstants.LOCATION_DECK) return true;
            if (location == YgoConstants.LOCATION_EXTRA) return (position & YgoConstants.POS_FACEUP) == 0;
            return (position & YgoConstants.POS_FACEDOWN) != 0 && location != YgoConstants.LOCATION_GRAVE;
        }

        private void HandleCardsDrawn(byte controller, IReadOnlyList<uint> cards, IReadOnlyList<DeckCardAppearance?> appearances)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null || controller >= _handCounts.Length || cards.Count == 0) return;
            int first = _handCounts[controller];
            _handCounts[controller] += cards.Count;
            var from = GetCardPose(controller, YgoConstants.LOCATION_DECK, 0, 0);
            var flights = new List<CardFlight>(cards.Count);
            for (int i = 0; i < cards.Count; i++)
            {
                uint sequence = (uint)(first + i);
                var to = GetCardPose(controller, YgoConstants.LOCATION_HAND, sequence, 0);
                if (!from.IsValid || !to.IsValid) return;
                // The session masks hidden draws for the other duelist with code zero.
                flights.Add(new CardFlight(cards[i], from, to, true, cards[i] == 0, appearances[i]));
            }
            var arrivals = new Action[flights.Count];
            for (int i = 0; i < arrivals.Length; i++)
                arrivals[i] = ReserveCardArrival(controller, YgoConstants.LOCATION_HAND, (uint)(first + i), Refresh);
            animator.QueueDraw(flights, index => arrivals[index]());
        }

        private BattleCardDisplay HandleCardMovedEvent(CardMoveEvent e, PresentedFieldReadout previousReadout)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null || !Root.activeInHierarchy) return null;
            if (e.fromLoc == e.toLoc && e.fromCon == e.toCon && e.fromSeq == e.toSeq && e.fromPos == e.toPos) return null;
            if (e.fromLoc == YgoConstants.LOCATION_DECK && e.toLoc == YgoConstants.LOCATION_DECK) return null;

            var from = GetDisplayedCardPose(e.fromCon, e.fromLoc, e.fromSeq, e.code, e.fromPos);
            if (!from.IsValid) from = GetCardPose(e.fromCon, e.fromLoc, e.fromSeq, e.fromPos);
            if (e.fromLoc == YgoConstants.LOCATION_HAND && e.fromCon < _handCounts.Length)
            {
                if (e.fromSeq < _handCounts[e.fromCon]) RemoveHandView(e.fromCon, e.fromSeq);
                _handCounts[e.fromCon] = Mathf.Max(0, _handCounts[e.fromCon] - 1);
            }
            if (e.toLoc == YgoConstants.LOCATION_HAND && e.toCon < _handCounts.Length)
            {
                InsertHandView(e.toCon, e.toSeq, _handCounts[e.toCon]);
                _handCounts[e.toCon]++;
            }

            var to = GetCardPose(e.toCon, e.toLoc, e.toSeq, e.toPos);
            if (!from.IsValid && !to.IsValid) return null;

            bool disappearing = e.toLoc == 0;
            if (!from.IsValid)
                from = to.Shift(Vector2.up * 52f, .58f);
            if (!to.IsValid)
                to = from.Shift(Vector2.up * 25f, .72f);

            bool fromBack = e.code == 0 || IsFaceDown(e.fromCon, e.fromLoc, e.fromPos);
            bool toBack = disappearing ? fromBack : e.code == 0 || IsFaceDown(e.toCon, e.toLoc, e.toPos);
            var flight = new CardFlight(e.code, from, to, fromBack, toBack, MoveAppearance(e));
            if (e.toLoc == (YgoConstants.LOCATION_EXTRA | YgoConstants.LOCATION_OVERLAY))
                return QueueSummonMaterial(animator, e, flight, previousReadout, awaitingXyzHost: true);
            int xyzMaterials = 0;
            if (e.fromLoc == YgoConstants.LOCATION_EXTRA && _pendingXyzMaterials.Remove((e.fromCon, e.fromSeq), out var pending))
            {
                uint hostLocation = e.toLoc == YgoConstants.LOCATION_MZONE ? e.toLoc : e.fromLoc;
                byte hostController = hostLocation == e.toLoc ? e.toCon : e.fromCon;
                uint hostSequence = hostLocation == e.toLoc ? e.toSeq : e.fromSeq;
                var materialFlights = new List<SummonMaterialFlight>(pending.Count);
                foreach (var material in pending)
                {
                    var arrival = GetCardPose(hostController, hostLocation | YgoConstants.LOCATION_OVERLAY, hostSequence, material.Index);
                    materialFlights.Add(arrival.IsValid ? material.Move.AimAt(arrival) : material.Move);
                }
                xyzMaterials = materialFlights.Count;
                animator.QueueXyzMaterials(materialFlights, Refresh);
            }

            if (e.MaterialMethod != 0 && (e.toLoc & (YgoConstants.LOCATION_ONFIELD | YgoConstants.LOCATION_OVERLAY)) == 0)
                return QueueSummonMaterial(animator, e, flight, previousReadout);

            bool attaches = (e.toLoc & YgoConstants.LOCATION_OVERLAY) != 0;
            bool detaches = (e.fromLoc & YgoConstants.LOCATION_OVERLAY) != 0;
            bool sameZone = e.fromCon == e.toCon && e.fromLoc == e.toLoc && e.fromSeq == e.toSeq;
            CardMotion motion = CardMotion.Transfer;
            if (disappearing) motion = CardMotion.Vanish;
            else if (e.ReturnEffectId.HasValue) motion = CardMotion.FieldReturn;
            else if ((e.reason & YgoConstants.REASON_DESTROY) != 0) motion = CardMotion.Destroy;
            else if (attaches || detaches) motion = attaches ? CardMotion.AttachMaterial : CardMotion.DetachMaterial;
            else if (sameZone) motion = CardMotion.ChangePosition;
            else if ((e.toLoc & YgoConstants.LOCATION_ONFIELD) != 0 && toBack) motion = CardMotion.Set;
            else if (e.toLoc == YgoConstants.LOCATION_MZONE && e.fromLoc != YgoConstants.LOCATION_MZONE)
                motion = MonsterArrivalMotion(e);

            Action<float> landingImpact = !toBack && e.Appearance?.overframe == true && CardAnimationManager.IsSummonMotion(motion)
                ? progress => PoseSpecialEditionLanding(e.toCon, e.toLoc, e.toSeq, e.code, progress) : null;
            var held = HoldMovingFieldCard(e, motion, previousReadout);
            var readout = PrepareMoveReadout(animator, e, fromBack, toBack, held);
            if (motion == CardMotion.ChangePosition && e.ChangesPositionInPlace)
            {
                QueuePositionChange(animator, e, flight, readout, held);
                return held;
            }
            bool groupDestruction = e.PresentationGroup != null && (e.reason & YgoConstants.REASON_DESTROY) != 0;
            if ((e.TemporarilyBanished || groupDestruction) &&
                (e.fromLoc is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE) &&
                (e.toLoc & (YgoConstants.LOCATION_ONFIELD | YgoConstants.LOCATION_OVERLAY)) == 0)
            {
                var release = ReserveMoveArrival(e);
                animator.QueueFieldDeparture(e.PresentationGroup, flight, motion, readout,
                    held == null ? null : () => held.Hidden = true, () =>
                    {
                        ReleaseDepartingFieldCard(e, held);
                        release?.Invoke();
                    }, Refresh, temporary: e.TemporarilyBanished);
                return held;
            }
            Action detach = sameZone ? null : HoldMaterialReadout(e.fromCon, e.fromLoc, e.fromSeq, -1);
            Action attach = sameZone ? null : HoldMaterialReadout(e.toCon, e.toLoc, e.toSeq, 1);
            var leaveBanish = StageBanishedDeparture(e);
            Action depart = held == null && detach == null && leaveBanish == null ? null : () =>
            {
                if (held != null) held.Hidden = true;
                detach?.Invoke();
                leaveBanish?.Invoke();
            };
            Action departAndRefresh = depart == null ? null : () =>
            {
                depart();
                Refresh();
            };
            if (disappearing || attaches)
            {
                animator.QueueMove(flight, motion, held == null && attach == null ? null : () =>
                {
                    ReleaseDepartingFieldCard(e, held);
                    attach?.Invoke();
                    Refresh();
                }, readout, departAndRefresh);
                return held;
            }
            if (e.ReturnEffectId.HasValue || e.summonType.HasValue && e.toLoc == YgoConstants.LOCATION_MZONE)
            {
                var key = ReserveArrival(e.toCon, e.toLoc, e.toSeq);
                var inspection = e.ReturnEffectId.HasValue ? TrackMovingInspection(e, readout, null) : null;
                animator.StageFieldArrival(flight, motion, e.toCon, e.toLoc, e.toSeq, readout,
                    e.ReturnEffectId.HasValue ? depart : departAndRefresh, () =>
                {
                    ReleaseBattleCardDisplay(held);
                    ReleaseArrival(key);
                    ReleaseMovingInspection(e, inspection);
                }, Refresh, e.ReturnEffectId, inspection == null ? null : () => inspection.AtDestination = true, xyzMaterials,
                    landingImpact, wideCutIn: true);
                if (e.summonType.HasValue && CardDatabase.Get(e.code) is { IsLink: true, link: not 0 } summon)
                    _arrivalLinks.Add((e.toCon, e.toSeq, summon.link));
                return held;
            }
            var arrive = ReserveMoveArrival(e);
            void OnArrival()
            {
                ReleaseDepartingFieldCard(e, held);
                arrive?.Invoke();
                Refresh();
            }
            if (motion == CardMotion.Transfer &&
                e.fromLoc is YgoConstants.LOCATION_DECK or YgoConstants.LOCATION_EXTRA &&
                e.toLoc is YgoConstants.LOCATION_GRAVE or YgoConstants.LOCATION_REMOVED)
                animator.QueuePileTransfer(flight, OnArrival);
            else
                animator.QueueMove(flight, motion, OnArrival, readout, departAndRefresh, landingImpact,
                    wideCutIn: true);
            return held;
        }

        private void PoseSpecialEditionLanding(byte controller, uint location, uint sequence, uint code, float progress)
        {
            ShakeBoardForSummon(progress);
            if (_session == null) return;
            var view = GetSlot(controller, location, sequence)?.cardView;
            if (view == null || !view.isActiveAndEnabled || view.CurrentCode != code || !view.HasOverframe) return;
            float settle = 1f - Mathf.Clamp01(progress);
            float lift = Mathf.SmoothStep(0f, 1f, progress * 7f) * settle * settle;
            view.SetReadoutFocus(lift);
        }

        private BattleCardDisplay QueueSummonMaterial(CardAnimationManager animator, CardMoveEvent move, CardFlight flight,
            PresentedFieldReadout previous, bool awaitingXyzHost = false)
        {
            var held = HoldFieldCard(move, previous);
            var readout = PrepareMoveReadout(animator, move, flight.StartsFaceDown, flight.EndsFaceDown, held);
            var detach = HoldMaterialReadout(move.fromCon, move.fromLoc, move.fromSeq, -1);
            var leaveBanish = StageBanishedDeparture(move);
            bool vanishes = move.toLoc == 0;
            var release = awaitingXyzHost ? null : ReserveMoveArrival(move);
            var material = new SummonMaterialFlight(flight, readout, () =>
            {
                if (held != null) held.Hidden = true;
                detach?.Invoke();
                leaveBanish?.Invoke();
            }, () =>
            {
                ReleaseDepartingFieldCard(move, held);
                release?.Invoke();
            }, vanishes);
            if (awaitingXyzHost)
            {
                // Overlay moves precede the host's placement. Keep the origins until the final zone is known.
                var host = (move.toCon, move.toSeq);
                if (!_pendingXyzMaterials.TryGetValue(host, out var materials))
                    _pendingXyzMaterials.Add(host, materials = new());
                materials.Add((material, move.toPos));
            }
            else animator.QueueMaterialDeparture(move.PresentationGroup, material, move.MaterialMethod, Refresh);
            return held;
        }

        private CardMoveReadoutSnapshot PrepareMoveReadout(CardAnimationManager animator, CardMoveEvent move,
            bool fromBack, bool toBack, BattleCardDisplay held)
        {
            if (move.code == 0) return null;
            var departure = move.SourceReadout ?? held?.Readout?.Shown;
            bool leavesScale = departure?.IsPendulumScale == true;
            bool placesScale = YgoConstants.IsPendulumZone(move.toLoc, move.toSeq) &&
                CardDatabase.Get(move.code)?.IsPendulum == true;
            var sourceCard = !fromBack && (move.fromLoc == YgoConstants.LOCATION_MZONE || leavesScale) ? departure : null;
            var source = sourceCard == null ? null : GetSlot(move.fromCon, move.fromLoc, move.fromSeq);
            var destination = !toBack && (move.toLoc == YgoConstants.LOCATION_MZONE || placesScale)
                ? GetSlot(move.toCon, move.toLoc, move.toSeq) : null;
            var startArea = source?.root ?? destination?.root;
            if (startArea == null) return null;

            float focusScale = source?.readout?.Rect.localScale.x ?? 1f;
            int viewer = _session.HumanPlayerIndex;
            var readout = new CardMoveReadoutSnapshot(move.code, sourceCard, ReadMovement)
            {
                DestinationCard = destination == null ? null : move.DestinationReadout,
            };
            if (destination != null) _pendingCardReadouts[(move.toCon, move.toLoc, move.toSeq)] = readout;
            return readout;

            (Vector2 StartSize, Vector2 EndSize, bool StartOpponent, bool EndOpponent) ReadMovement()
            {
                var startSize = animator.GetAreaSize(startArea) * focusScale;
                var endSize = destination == null ? startSize : animator.GetAreaSize(destination.root);
                return (startSize, endSize, move.fromCon != viewer, move.toCon != viewer);
            }
        }

        internal static CardMotion MonsterArrivalMotion(CardMoveEvent move)
        {
            uint type = CardDatabase.Get(move.code)?.type ?? 0;
            if ((type & YgoConstants.TYPE_TOKEN) != 0) return CardMotion.TokenSummon;
            if (move.summonType is { } summonType)
            {
                return (summonType & YgoConstants.SUMMON_METHOD_MASK) switch
                {
                    YgoConstants.SUMMON_TYPE_ADVANCE => CardMotion.TributeSummon,
                    YgoConstants.SUMMON_TYPE_PENDULUM => CardMotion.PendulumSummon,
                    YgoConstants.SUMMON_TYPE_FUSION or YgoConstants.SUMMON_TYPE_RITUAL or YgoConstants.SUMMON_TYPE_SYNCHRO or
                        YgoConstants.SUMMON_TYPE_XYZ or YgoConstants.SUMMON_TYPE_LINK => CardMotion.ExtraDeckSummon,
                    _ => CardMotion.SpecialSummon,
                };
            }
            if ((move.reason & YgoConstants.REASON_SPSUMMON) == 0) return CardMotion.Summon;
            if (move.fromLoc == YgoConstants.LOCATION_EXTRA && (move.fromPos & YgoConstants.POS_FACEUP) != 0 &&
                (type & YgoConstants.TYPE_PENDULUM) != 0) return CardMotion.PendulumSummon;
            if (move.fromLoc == YgoConstants.LOCATION_EXTRA && (type & (YgoConstants.TYPE_EXTRA | YgoConstants.TYPE_PENDULUM)) != 0 ||
                move.fromLoc == YgoConstants.LOCATION_HAND && (type & YgoConstants.TYPE_RITUAL) != 0)
                return CardMotion.ExtraDeckSummon;
            return CardMotion.SpecialSummon;
        }

        private void HandleChainActivatedEvent(ChainLinkInfo link)
        {
            var session = _session;
            bool opponent = link.triggerPlayer != session.HumanPlayerIndex;
            Action showCard = opponent && link.code != 0 ? () =>
            {
                if (ReferenceEquals(_session, session))
                    InspectCardAt(link.code, link.PublicFieldSource, link);
            } : null;
            var animator = CardAnimationManager.Instance;
            if (animator == null)
            {
                showCard?.Invoke();
                return;
            }

            var pose = GetCardPose(link.triggerPlayer, link.triggerLoc, link.triggerSeq, YgoConstants.POS_FACEUP_ATTACK);
            animator.QueueChain(pose, link.code, link.chainNumber, opponent, link.ActivationReadout, showCard, link.Appearance);
            QueueColumnActivation(animator, link);
        }

        private void HandleChainResolvingEvent(ChainLinkInfo link)
        {
            var animator = CardAnimationManager.Instance;
            if (animator != null)
                animator.QueueChainResolution(() => ReadChainResolutionLayout(animator), link, compact: _mobileLayout);
        }

        private void HandleChainResolvedEvent(ChainLinkInfo link)
        {
            var animator = CardAnimationManager.Instance;
            if (animator != null)
                animator.QueueChainOutcome(() => link.negated
                    ? (animator.GetPoint(_boardRect, new Vector2(.50f, .62f)), 306f)
                    : ReadChainResolutionLayout(animator), link, () => link.NegatedGraveController is { } owner
                    ? animator.GetPoint(GetStack(owner, YgoConstants.LOCATION_GRAVE).root, Vector2.one * .5f)
                    : null);
            if (!link.AppliedColumnNegation || link.negated) return;
            if (animator != null)
                animator.QueueFieldReadoutUpdate(() => ApplyColumnNegation(link));
            else
                ApplyColumnNegation(link);
        }

        private (Vector2, float) ReadChainResolutionLayout(CardAnimationManager animator)
        {
            if (!_mobileLayout)
                return (animator.GetPoint(_boardRect, new Vector2(.50f, .56f)), 206f);

            // Keep the chain clear of the opponent LP and the phase dial, including on rotation.
            var playArea = (RectTransform)_boardRect.parent;
            var area = MobileChainResolutionArea;
            var size = animator.GetAreaSize(playArea);
            float height = Mathf.Min(size.x * area.width * .88f,
                size.y * area.height / CardAnimationManager.CompactChainReadoutHeightRatio);
            return (animator.GetPoint(playArea, area.center), height);
        }

        private RectTransform AttackTargetAnchor(byte attacker, byte target, uint location, uint sequence)
        {
            if (location == 0)
                return (attacker == _session.HumanPlayerIndex ? _oppLpText : _myLpText).rectTransform;
            return GetSlot(target, location, sequence)?.root;
        }

        private void RefreshAttackResponseArrow()
        {
            bool responding = _modals.IsChainPromptOpen && _session.HasActiveAttack &&
                _session.AttackerCon != _session.HumanPlayerIndex && !_modals.IsZoneViewerOpen;
            var source = responding ? GetSlot((byte)_session.AttackerCon, _session.AttackerLoc, _session.AttackerSeq) : null;
            if (source == null || source.liveCard == null)
            {
                _responseAttackArrow.gameObject.SetActive(false);
                return;
            }

            var target = AttackTargetAnchor((byte)_session.AttackerCon, (byte)_session.TargetCon,
                _session.TargetLoc, _session.TargetSeq);
            if (target == null)
            {
                _responseAttackArrow.gameObject.SetActive(false);
                return;
            }
            var arrow = _responseAttackArrow.rectTransform;
            var host = (RectTransform)arrow.parent;
            Vector2 from = host.InverseTransformPoint(source.root.TransformPoint(source.root.rect.center));
            Vector2 to = host.InverseTransformPoint(target.TransformPoint(target.rect.center));
            Vector2 delta = to - from;
            arrow.anchoredPosition = (from + to) * .5f;
            arrow.sizeDelta = new Vector2(delta.magnitude, 54f);
            arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            arrow.gameObject.SetActive(delta.sqrMagnitude > 1f);
        }

        private void HandleAttackEvent(byte attacker, uint location, uint sequence, byte target, uint targetLoc, uint targetSeq)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null) return;
            uint position = FindLiveCard(attacker, location, sequence)?.position ?? YgoConstants.POS_FACEUP_ATTACK;
            var from = GetDisplayedCardPose(attacker, location, sequence, 0, position);
            if (!from.IsValid) from = GetCardPose(attacker, location, sequence, position);
            var destination = AttackTargetAnchor(attacker, target, targetLoc, targetSeq);
            if (from.IsValid && destination != null)
                animator.QueueAttackArrow(from, () => animator.GetPoint(destination, Vector2.one * .5f),
                    attacker != _session.HumanPlayerIndex);
        }

        private void HandleBattleResolved(BattleCardStats attacker, BattleCardStats defender)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null || !attacker.IsFaceUp) return;
            var card = FindLiveCard(attacker.Controller, attacker.Location, attacker.Sequence);
            if (card == null || card.code == 0) return;

            var resting = GetCardPose(attacker.Controller, attacker.Location, attacker.Sequence, attacker.Position);
            var from = GetDisplayedCardPose(attacker.Controller, attacker.Location, attacker.Sequence, card.code, attacker.Position);
            if (!from.IsValid) from = resting;
            var destination = AttackTargetAnchor(attacker.Controller, defender.Controller, defender.Location, defender.Sequence);
            if (destination == null) return;
            Vector2 ReadHit() => animator.GetPoint(destination, Vector2.one * .5f);
            var defendingCard = defender.Location == YgoConstants.LOCATION_MZONE
                ? FindLiveCard(defender.Controller, defender.Location, defender.Sequence) : null;
            var combat = new DuelCardReadout(CardDatabase.Get(card.code), card, attacker);
            var defense = defender.IsFaceUp && defendingCard?.code > 0
                ? new DuelCardReadout(CardDatabase.Get(defendingCard.code), defendingCard, defender) : default;
            int viewer = _session.HumanPlayerIndex;
            var comparisonArea = _mobileLayout ? MobileBattleStatsArea : BattleStatsArea;
            var readout = animator.CreateBattleReadout(comparisonArea, attacker, defender, viewer, combat.IsLink, defense.IsLink,
                _mobileLayout);
            var flight = new CardFlight(card.code, from, resting, false, false, card.Appearance);
            var slot = GetSlot(attacker.Controller, attacker.Location, attacker.Sequence);
            var targetSlot = GetSlot(defender.Controller, defender.Location, defender.Sequence);
            Vector2 statSize = slot == null ? Vector2.zero : animator.GetAreaSize(slot.readout?.Rect ?? slot.root);
            var source = QueueBattleCard(attacker, card);
            var target = QueueBattleCard(defender, defendingCard);
            (CardPose pose, Vector2 size, CardPose defenderPose, RectTransform defenderStats) Start()
            {
                var pose = GetDisplayedCardPose(attacker.Controller, attacker.Location, attacker.Sequence, card.code, attacker.Position);
                Vector2 size = slot == null ? Vector2.zero : animator.GetAreaSize(slot.readout?.Rect ?? slot.root);
                RectTransform defenderStats = null;
                if (source != null) source.Hidden = true;
                if (target != null)
                {
                    target.Position = defender.Position;
                    target.UseBattleStats = true;
                    if (defense.IsMonster && targetSlot != null)
                    {
                        ResetFieldCardFocus(targetSlot);
                        var area = targetSlot.readout?.Rect ?? targetSlot.root;
                        defenderStats = animator.CreateBattleCardReadout("DefendingCardReadout", defense,
                            defender.Controller != viewer, animator.GetPoint(area, Vector2.one * .5f), animator.GetAreaSize(area));
                        target.HideReadout = true;
                    }
                }
                Refresh();
                var targetPose = GetDisplayedCardPose(defender.Controller, defender.Location, defender.Sequence,
                    defendingCard?.code ?? 0, defender.Position);
                if (!targetPose.IsValid)
                    targetPose = GetCardPose(defender.Controller, defender.Location, defender.Sequence, defender.Position);
                return (pose, size, targetPose, defenderStats);
            }
            void Finish()
            {
                ReleaseBattleCardDisplay(source);
                ReleaseBattleCardDisplay(target);
                Refresh();
            }
            (Vector2 Hit, Vector2 AttackerArea, Vector2 DefenderArea) ReadLayout() =>
                (ReadHit(), slot == null ? Vector2.zero : animator.GetAreaSize(slot.root),
                    targetSlot == null ? Vector2.zero : animator.GetAreaSize(targetSlot.root));
            animator.QueueAttack(flight, ReadHit(), defender.Location == 0, attacker.Controller != viewer, readout, combat, statSize,
                Finish, Start, ReadLayout);
        }

        private void HandleLifePointsChanged(byte player, int delta, int lifePoints, LifePointChangeKind kind)
        {
            bool mine = player == _session.HumanPlayerIndex;
            void ApplyLifePoints()
            {
                if (mine) _myTargetLp = Mathf.Max(0, lifePoints);
                else _oppTargetLp = Mathf.Max(0, lifePoints);
            }

            var animator = CardAnimationManager.Instance;
            if (animator == null)
            {
                ApplyLifePoints();
                return;
            }
            bool damaging = kind is LifePointChangeKind.BattleDamage or LifePointChangeKind.EffectDamage or LifePointChangeKind.Damage;
            animator.QueueLifeChange(mine ? _myLpFeedbackAnchor : _oppLpFeedbackAnchor, delta, kind, ApplyLifePoints,
                damaging ? ShakeBoardForDamage : null);
        }

        private void HandleTurnStarted(int turn, int player)
        {
            if (_pendingXyzMaterials.Count != 0) CompleteFieldArrivals();
            bool opponent = player != _session.HumanPlayerIndex;
            void ApplyTurn()
            {
                ClearColumnNegations();
                SetTurnCaption(turn, opponent);
                SetPhasePlate(YgoConstants.PHASE_DRAW, opponent);
            }

            var animator = CardAnimationManager.Instance;
            if (animator == null)
            {
                ApplyTurn();
                return;
            }
            var center = animator.GetPoint(_boardRect, new Vector2(0.5f, 0.54f));
            animator.QueuePhase(opponent ? "상대 턴" : "내 턴", opponent, center, ApplyTurn);
        }

        private void HandleDestinyDraw(byte player)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null) return;
            animator.QueueDestinyDraw(player != _session.HumanPlayerIndex, animator.GetPoint(_boardRect, new Vector2(.5f, .56f)));
        }

        private void HandlePhaseChanged(int phase, int player)
        {
            bool opponent = player != _session.HumanPlayerIndex;
            var animator = CardAnimationManager.Instance;
            if (animator == null)
            {
                SetPhasePlate(phase, opponent);
                return;
            }
            var center = animator.GetPoint(_boardRect, new Vector2(0.5f, 0.54f));
            animator.QueuePhase(YgoConstants.GetPhaseNameKR(phase), opponent, center, () => SetPhasePlate(phase, opponent));
        }

        public void Update()
        {
            if (_session == null || Root == null || !Root.activeSelf) return;
            var session = _session;
            if (_boardRect.rect.size != _boardLayoutSize) Refresh();
            AdvanceHandLayout(Time.unscaledDeltaTime * PlaybackRate);
            AdvanceActionWindow();
            RefreshPileActionButtons();
            UpdateDuelPad();
            if (_session != session || !Root.activeSelf) return;
            RefreshHandGlows();
            UpdateNegativeFeedback();
            UpdateTargetChoices();
            RefreshFieldRelationships();
            RefreshAttackResponseArrow();
            UpdateTouchCardActions();
            RefreshCardActions();
            if (_phasePicker.activeSelf && (!CanAcceptPlayerAction ||
                Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)))
            {
                _phasePicker.SetActive(false);
                return;
            }
            if (IsReadingLog)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) CloseDuelLog();
                return;
            }
            byte me = (byte)_session.HumanPlayerIndex;
            byte opponent = (byte)_session.AiPlayerIndex;
            if (_myHandBar.rect.size != _myHandSize) RefreshHand(_session.Players[me].hand, me, false);
            if (_oppHandBar.rect.size != _oppHandSize) RefreshHand(_session.Players[opponent].hand, opponent, true);

            UpdateChainDialPulse();
            UpdateDragColumnGuide();
            RefreshPhaseChoices(_session.ActivePrompt);
            AdvanceLifeReadouts(Time.deltaTime * PlaybackRate);

            // Global Cancel / Dismiss via ESC or Right Mouse Button
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                if (_cardZoomModal != null && _cardZoomModal.activeSelf)
                {
                    DuelAudio.PlayClick();
                    CloseCardZoomModal();
                }
                else if (_actionPopup != null && _actionPopup.activeSelf)
                {
                    DuelAudio.PlayClick();
                    CloseActionPopup();
                }
                else if (IsBrowsingZone)
                {
                    DuelAudio.PlayClick();
                    _modals.CloseZoneViewer();
                }
                else if (_modals != null && _modals.ModalRoot != null && _modals.ModalRoot.activeSelf)
                {
                    bool openingPile = !Input.GetKeyDown(KeyCode.Escape) && _modals.IsShowingField &&
                        _modals.IsPointOverBrowsablePile(Input.mousePosition);
                    if (!openingPile && (!_modals.IsFieldCardSelection || Input.GetKeyDown(KeyCode.Escape)))
                        _modals.TryCancelOrPass();
                }
                else if (TryCancelPlacement())
                {
                    DuelAudio.PlayClick();
                }
                else if (Input.GetKeyDown(KeyCode.Escape) && _modals != null && _modals.ResultRoot != null && _modals.ResultRoot.activeSelf)
                {
                    _modals.TriggerMenu();
                }
                else if (Input.GetKeyDown(KeyCode.Escape))
                {
                    OnPauseClicked();
                }
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                HandleKeyboardConfirm();
        }

        private void AdvanceLifeReadouts(float playbackDelta)
        {
            if (IsPaused) return;
            AdvanceLifeReadout(_myLpText, _myLpFill, ref _myDisplayLp, _myTargetLp, playbackDelta);
            AdvanceLifeReadout(_oppLpText, _oppLpFill, ref _oppDisplayLp, _oppTargetLp, playbackDelta);
        }

        private static void AdvanceLifeReadout(Text label, RectTransform fill, ref double displayed, int target, float playbackDelta)
        {
            double remaining = target - displayed;
            if (remaining != 0d && playbackDelta > 0f)
            {
                int before = (int)Math.Round(displayed);
                double distance = Math.Abs(remaining);
                const double decay = 6.5d, minimumRate = 1200d;
                const double threshold = minimumRate / decay;
                double decelerationTime = distance > threshold ? Math.Log(distance / threshold) / decay : 0d;
                double after = playbackDelta < decelerationTime
                    ? distance * Math.Exp(-decay * playbackDelta)
                    : Math.Max(0d, Math.Min(distance, threshold) - minimumRate * (playbackDelta - decelerationTime));
                displayed = target - Math.Sign(remaining) * after;
                int lifePoints = (int)Math.Round(displayed);
                if (lifePoints != before)
                {
                    DuelAudio.PlayTick();
                    WriteLifeReadout(label, fill, lifePoints);
                }
            }

            var lowLife = new Color(1f, .66f, .57f);
            label.color = displayed > 0d && displayed <= 2000d
                ? Color.Lerp(HudText, lowLife, .34f + Mathf.PingPong(Time.time * 2.1f, .42f))
                : HudText;
            if (fill.TryGetComponent<Image>(out var gauge))
            {
                Color tone = remaining > .01 ? new Color(.26f, 1f, .65f) :
                    remaining < -.01 ? new Color(1f, .31f, .30f) : LifeTrackColor;
                gauge.color = Color.Lerp(gauge.color, tone, Mathf.Clamp01(playbackDelta * 12f));
            }
        }

        private static void WriteLifeReadout(Text label, RectTransform fill, int lifePoints)
        {
            label.text = lifePoints.ToString();
            fill.anchorMax = new Vector2(Mathf.Clamp01((float)lifePoints / FullLifeTrack), 1f);
        }

        private void HandleKeyboardConfirm()
        {
            if (IsPaused || IsPresenting || IsBrowsingZone || _cardZoomModal.activeSelf ||
                _actionPopup.activeSelf || _phasePicker.activeSelf) return;

            bool result = _modals.ResultRoot.activeInHierarchy;
            var panel = result ? _modals.ResultRoot : _modals.ModalRoot;
            if (!panel.activeInHierarchy) return;

            var events = EventSystem.current;
            if (events != null && events.sendNavigationEvents && FocusHandlesSubmit(events.currentSelectedGameObject, panel))
                return;

            if (result) _modals.TriggerRematch();
            else _modals.TryConfirm();
        }

        private static bool FocusHandlesSubmit(GameObject control, GameObject panel) =>
            control != null && control.transform.IsChildOf(panel.transform) &&
            ExecuteEvents.CanHandleEvent<ISubmitHandler>(control);
        // ==================== PROMPT INTERACTIONS ====================
        private bool IsStagingPlacement => _stagedPlacement.HasValue &&
            ReferenceEquals(_stagedPlacement.Value.prompt, _session?.ActivePrompt);

        private static bool IsNativePlacement(DuelPrompt prompt) =>
            prompt?.messageType is YgoConstants.MSG_SELECT_PLACE or YgoConstants.MSG_SELECT_DISFIELD;

        private bool TryStageCardPlacement(LiveCardState source, int response)
        {
            if (_session.ActivePrompt.messageType != YgoConstants.MSG_SELECT_IDLECMD ||
                source.location != YgoConstants.LOCATION_HAND || source.controller != _session.HumanPlayerIndex)
                return false;

            var card = CardDatabase.Get(source.code);
            if (card == null) return false;
            int action = response & 0xffff;
            uint location;
            if (action is 0 or 3 && card.IsMonster && card.level is > 0 and <= 4)
                location = YgoConstants.LOCATION_MZONE;
            else if (action == 4 && (card.IsSpell || card.IsTrap))
                location = YgoConstants.LOCATION_SZONE;
            else return false;

            uint available = 0;
            for (uint i = 0; i < 6; i++)
                if (CanPlaceHandCard(card, source.controller, location, i))
                    available |= 1u << (int)i;
            if (available == 0) return false;

            _stagedPlacement = (_session.ActivePrompt, response, source.code, location, available, _cardActionArt);
            HideCardActions();
            Refresh();
            return true;
        }

        private void RefreshPlacementControls(DuelPrompt prompt)
        {
            bool staged = IsStagingPlacement;
            bool visible = CanAcceptPlayerAction && (staged || IsNativePlacement(prompt));
            _placementBanner.SetActive(visible);
            _placementCancel.transform.parent.gameObject.SetActive(visible && (staged || _pendingPlaces.Count > 0));
            _hintBannerText.text = "";
            if (!visible) return;

            if (staged)
            {
                _placementHint.text = $"「{CardDatabase.GetName(_stagedPlacement.Value.code)}」  ·  놓을 위치를 선택하세요.";
                return;
            }

            string instruction = prompt.messageType == YgoConstants.MSG_SELECT_DISFIELD
                ? "사용할 수 없게 할 존을 선택하세요." : "카드를 놓을 위치를 선택하세요.";
            _placementHint.text = prompt.min > 1 ? $"{instruction}  {_pendingPlaces.Count}/{prompt.min}" : instruction;
        }

        private bool TryCancelPlacement()
        {
            if (!CanAcceptPlayerAction) return false;
            if (IsStagingPlacement)
                _stagedPlacement = null;
            else if (IsNativePlacement(_session.ActivePrompt) && _pendingPlaces.Count > 0)
                _pendingPlaces.RemoveAt(_pendingPlaces.Count - 1);
            else return false;

            Refresh();
            return true;
        }

        private bool IsPlacePending(byte con, uint loc, uint seq)
        {
            for (int i = 0; i < _pendingPlaces.Count; i++)
                if (_pendingPlaces[i].con == con && _pendingPlaces[i].loc == loc && _pendingPlaces[i].seq == seq)
                    return true;
            return false;
        }

        private bool IsPlaceSelectable(byte con, uint loc, uint seq)
        {
            if (!CanAcceptPlayerAction) return false;
            if (IsStagingPlacement)
            {
                var placement = _stagedPlacement.Value;
                return con == _session.HumanPlayerIndex && loc == placement.location &&
                    seq < 6 && (placement.availableZones & (1u << (int)seq)) != 0;
            }

            var p = _session.ActivePrompt;
            if (!IsNativePlacement(p)) return false;

            uint avail = ~p.placeFlag;
            int shift = con == p.playerId ? 0 : 16;
            if (loc == YgoConstants.LOCATION_MZONE)
                return seq < 7 && ((avail >> (shift + (int)seq)) & 1u) != 0;
            if (loc == YgoConstants.LOCATION_SZONE)
            {
                if (seq < 6) return ((avail >> (shift + 8 + (int)seq)) & 1u) != 0;
                if (seq == 6 || seq == 7) return ((avail >> (shift + 14 + (int)(seq - 6))) & 1u) != 0;
            }
            return false;
        }

        private bool HasHandAction(byte con, uint seq)
        {
            return CollectActions(con, YgoConstants.LOCATION_HAND, seq).Count > 0;
        }

        private bool HasFieldAction(byte con, uint loc, uint seq)
        {
            return CollectActions(con, loc, seq).Count > 0;
        }

        private int CountZoneActions(byte con, uint loc)
        {
            return CollectZoneActions(con, loc).Count;
        }

        private void AppendMatchingActions(
            Func<SelectableCardItem, bool> predicate,
            List<(SelectableCardItem item, string label, int responseVal, bool gold)> dest)
        {
            var p = _session?.ActivePrompt;
            if (p == null || p.playerId != _session.HumanPlayerIndex) return;

            if (p.messageType == YgoConstants.MSG_SELECT_IDLECMD)
            {
                for (int i = 0; i < p.summonable.Count; i++)
                    if (predicate(p.summonable[i])) dest.Add((p.summonable[i], "일반 소환", (i << 16) | 0, true));
                for (int i = 0; i < p.spSummonable.Count; i++)
                    if (predicate(p.spSummonable[i])) dest.Add((p.spSummonable[i], "특수 소환", (i << 16) | 1, true));
                for (int i = 0; i < p.repositionable.Count; i++)
                    if (predicate(p.repositionable[i])) dest.Add((p.repositionable[i], "표시 형식 변경", (i << 16) | 2, false));
                for (int i = 0; i < p.mSetable.Count; i++)
                    if (predicate(p.mSetable[i])) dest.Add((p.mSetable[i], "몬스터 세트", (i << 16) | 3, false));
                for (int i = 0; i < p.sSetable.Count; i++)
                    if (predicate(p.sSetable[i])) dest.Add((p.sSetable[i], "마법·함정 세트", (i << 16) | 4, false));
                for (int i = 0; i < p.activatable.Count; i++)
                {
                    var item = p.activatable[i];
                    if (predicate(item)) dest.Add((item, EffectActionLabel(item), (i << 16) | 5, true));
                }
            }
            else if (p.messageType == YgoConstants.MSG_SELECT_BATTLECMD)
            {
                for (int i = 0; i < p.attackable.Count; i++)
                    if (predicate(p.attackable[i])) dest.Add((p.attackable[i], "공격", (i << 16) | 1, true));
                for (int i = 0; i < p.activatable.Count; i++)
                {
                    var item = p.activatable[i];
                    if (predicate(item)) dest.Add((item, EffectActionLabel(item), (i << 16) | 0, true));
                }
            }
        }

        private static string EffectActionLabel(SelectableCardItem item)
        {
            string desc = CardDatabase.ResolveCommandEffectDescription(item);
            return desc == null ? "카드 효과 발동" : $"발동 · {desc}";
        }

        private List<(string label, int responseVal, bool gold)> CollectActions(byte con, uint loc, uint seq, uint code = 0)
        {
            var matched = new List<(SelectableCardItem item, string label, int responseVal, bool gold)>();
            AppendMatchingActions(it => it.controller == con && it.location == loc && it.sequence == seq &&
                (code == 0 || it.code == code), matched);
            var res = new List<(string, int, bool)>(matched.Count);
            for (int i = 0; i < matched.Count; i++)
                res.Add((matched[i].label, matched[i].responseVal, matched[i].gold));
            return res;
        }

        private List<(SelectableCardItem item, string label, int responseVal, bool gold)> CollectZoneActions(byte con, uint loc)
        {
            var res = new List<(SelectableCardItem item, string label, int responseVal, bool gold)>();
            AppendMatchingActions(it => it.controller == con && it.location == loc, res);
            return res;
        }

        private void ExecuteActionInt(int responseVal, (byte con, uint loc, uint seq)? placement = null)
        {
            if (!CanAcceptPlayerAction || IsStagingPlacement || IsBrowsingZone) return;
            _preferredDropZone = placement;
            DuelAudio.PlayClick();
            _actionPopup.SetActive(false);
            _modals.HideAll();
            _session.SubmitResponseInt(responseVal);
            Refresh();
        }

        private static int ExtraSummonIndex(DuelPrompt prompt, LiveCardState card) =>
            card == null || card.code == 0 || card.location != YgoConstants.LOCATION_EXTRA ? -1 :
                prompt.spSummonable.FindIndex(item => item.controller == card.controller && item.code == card.code &&
                    item.location == card.location && item.sequence == card.sequence);

        private bool ExtraSummonAvailable =>
            CanUseCardActions && _session.ActivePrompt.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
            _session.Players[_session.HumanPlayerIndex].extra.Any(card => ExtraSummonIndex(_session.ActivePrompt, card) >= 0);

        private static bool IsPileEffect(DuelSession session, uint location, SelectableCardItem item)
        {
            if (item.code == 0 || item.controller != session.HumanPlayerIndex || item.location != location)
                return false;
            var player = session.Players[session.HumanPlayerIndex];
            var cards = location switch
            {
                YgoConstants.LOCATION_EXTRA => player.extra,
                YgoConstants.LOCATION_REMOVED => player.banished,
                _ => player.grave,
            };
            return cards.Exists(card => card != null && card.code == item.code && card.sequence == item.sequence);
        }

        private bool PileEffectAvailable(uint location)
        {
            return CanAcceptPlayerAction && !IsBrowsingZone && !IsReadingCardDetails &&
                (_modals.HasChainActivationAt(_session, location) ||
                 CanUseCardActions && _session.ActivePrompt.activatable.Exists(item => IsPileEffect(_session, location, item)));
        }

        private void RefreshPileActionButtons()
        {
            bool canSummon = ExtraSummonAvailable;
            bool extraEffect = PileEffectAvailable(YgoConstants.LOCATION_EXTRA);
            bool both = canSummon && extraEffect;
            SetAvailability(_extraSummonButton, canSummon, .07f, both ? .49f : .93f);
            SetAvailability(_extraEffectButton, extraEffect, both ? .51f : .07f, .93f);
            SetAvailability(_graveEffectButton, PileEffectAvailable(YgoConstants.LOCATION_GRAVE), .07f, .93f);
            SetAvailability(_banishedEffectButton, PileEffectAvailable(YgoConstants.LOCATION_REMOVED), .07f, .93f);

            void SetAvailability(Button button, bool available, float left, float right)
            {
                if (button.gameObject.activeSelf != available)
                {
                    button.gameObject.SetActive(available);
                    if (available) UiEntranceMotion.Play(button.gameObject);
                }
                if (!available) return;
                var rect = (RectTransform)button.transform.parent;
                if (_mobileLayout)
                {
                    float touch = MobileSafeArea.MinimumTouchSize(rect);
                    var pile = (RectTransform)rect.parent;
                    bool extra = pile == _myExtraTile.root;
                    rect.anchorMin = rect.anchorMax = new Vector2((left + right) * .5f, extra ? .79f : .62f);
                    rect.sizeDelta = extra
                        ? new Vector2(pile.rect.width * (right - left), Mathf.Min(touch, pile.rect.height * .42f))
                        : new Vector2(Mathf.Max(touch, pile.rect.width * .84f), touch);
                    rect.anchoredPosition = Vector2.zero;
                    return;
                }
                rect.anchorMin = new Vector2(left, rect.anchorMin.y);
                rect.anchorMax = new Vector2(right, rect.anchorMax.y);
            }
        }

        private void OpenExtraSummonPicker()
        {
            if (IsBrowsingZone || !ExtraSummonAvailable) return;
            var session = _session;
            var prompt = session.ActivePrompt;
            var extra = session.Players[session.HumanPlayerIndex].extra;
            bool CanSummon() => _session == session && ReferenceEquals(session.ActivePrompt, prompt) && CanUseCardActions;
            bool IsSummonChoice(LiveCardState card) => CanSummon() && ExtraSummonIndex(prompt, card) >= 0 &&
                session.Players[session.HumanPlayerIndex].extra.Exists(current => current != null &&
                    current.sequence == card.sequence && current.code == card.code);

            HideCardActions();
            _modals.ShowZoneViewer(
                "엑스트라 소환 가능",
                extra,
                newestFirst: false,
                hasCardActions: IsSummonChoice,
                onOpenCardActions: card =>
                {
                    if (!IsSummonChoice(card)) return;
                    int index = ExtraSummonIndex(prompt, card);
                    if (index >= 0) ExecuteActionInt((index << 16) | 1);
                },
                canUseCardActions: CanSummon,
                icon: DuelVisualAssets.ZoneIcon(YgoConstants.LOCATION_EXTRA, false),
                actionsOnly: true, sidePanel: true);
        }

        private void OpenPileEffectPicker(uint location)
        {
            if (IsBrowsingZone || !CanAcceptPlayerAction || IsReadingCardDetails) return;
            var session = _session;
            if (_modals.FocusChainActivationAt(session, location) || !CanUseCardActions) return;

            var prompt = session.ActivePrompt;
            var choices = prompt.activatable.Select((item, index) => (item, index))
                .Where(choice => IsPileEffect(session, location, choice.item)).ToArray();
            if (choices.Length == 0)
            {
                RefreshPileActionButtons();
                return;
            }
            HideCardActions();
            string title = $"{YgoConstants.GetLocationNameKR(location)} 효과 발동";
            int activateCommand = prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD ? 5 : 0;
            _modals.ShowPileActivationPicker(session, prompt, choices.Select(choice => choice.index).ToArray(), title, index =>
            {
                if (_session != session || !ReferenceEquals(session.ActivePrompt, prompt) || !CanUseCardActions ||
                    index >= prompt.activatable.Count) return;
                var item = prompt.activatable[index];
                if (choices.Any(choice => choice.index == index && ReferenceEquals(choice.item, item)) &&
                    IsPileEffect(session, location, item))
                    ExecuteActionInt((index << 16) | activateCommand);
            });
        }

        private void SwitchBrowsablePile(bool opponent, uint location)
        {
            if (_session == null) return;
            byte controller = (byte)(opponent ? _session.AiPlayerIndex : _session.HumanPlayerIndex);
            GetStack(controller, location)?.button.onClick.Invoke();
        }

        private void OpenStackZone(byte con, uint loc, string title, bool actionsOnly = false)
        {
            var session = _session;
            if (CanAcceptPlayerAction && !IsReadingCardDetails && !IsBrowsingZone && con == session.HumanPlayerIndex &&
                !_modals.IsShowingField && _modals.FocusChainActivationAt(session, loc)) return;
            if (session == null || session.IsFinished || IsStagingPlacement || IsPresenting || IsPaused || IsReadingLog ||
                _cardZoomModal.activeSelf || _actionPopup.activeSelf || con >= session.Players.Length ||
                _modals.IsPromptOpen && !_modals.IsShowingField) return;
            var prompt = session.ActivePrompt;
            bool CanUseActions() => !_modals.IsPromptOpen && _session == session &&
                ReferenceEquals(session.ActivePrompt, prompt) && CanAcceptPlayerAction &&
                (loc != YgoConstants.LOCATION_REMOVED || !_banishedPilePresentation.ContainsKey(con));

            if (loc == YgoConstants.LOCATION_DECK)
            {
                var zoneActions = CanUseActions() ? CollectZoneActions(con, loc) : null;
                if (zoneActions != null && zoneActions.Count > 0)
                {
                    DuelAudio.PlayClick();
                    ShowZoneActionsPopup(title, zoneActions);
                }
                return;
            }

            if (actionsOnly && !CanUseActions()) return;

            List<(string label, int responseVal, bool gold)> GetZoneCardActions(LiveCardState card)
            {
                var actions = CollectActions(card.controller, card.location, card.sequence, card.code);
                if (loc == YgoConstants.LOCATION_EXTRA && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                    actions.RemoveAll(action => (action.responseVal & 0xffff) == 1);
                return actions;
            }

            DuelAudio.PlayClick();
            _modals.ShowPileViewer(
                con != session.HumanPlayerIndex,
                loc,
                session.Players[con],
                hasCardActions: c => CanUseActions() && GetZoneCardActions(c).Count > 0,
                onOpenCardActions: c =>
                {
                    if (!CanUseActions()) return;
                    var actions = GetZoneCardActions(c);
                    if (actions.Count == 0) return;
                    if (actionsOnly && actions.Count == 1)
                        ExecuteActionInt(actions[0].responseVal);
                    else
                        ShowCardActionsPopup(c.controller, c.location, c.sequence, c.code, c.overlayCodes, actions: actions);
                },
                canUseCardActions: CanUseActions,
                actionsOnly: actionsOnly,
                banished: loc == YgoConstants.LOCATION_REMOVED ? DisplayedBanishedCards(con) : null);
        }

        private bool FindFieldCardChoice(byte con, uint loc, uint seq, out int index)
        {
            index = -1;
            return CanAcceptPlayerAction && !_cardZoomModal.activeSelf && !IsArriving(con, loc, seq) &&
                _modals.FindFieldCard(_session.ActivePrompt, con, loc, seq, out index);
        }

        private void RefreshMaterialConfirm()
        {
            _modals.RefreshFieldMaterialConfirm((controller, location, sequence) =>
                GetSlot(controller, location, sequence)?.root);
        }

        private bool IsPointOverFieldCardChoice(Vector2 point, Camera camera)
        {
            foreach (var slot in _fieldSlotsByRect.Values)
                if (slot.cardView.gameObject.activeInHierarchy &&
                    FindFieldCardChoice(slot.mappedCon, slot.mappedLoc, slot.mappedSeq, out _) &&
                    RectTransformUtility.RectangleContainsScreenPoint(slot.root, point, camera))
                    return true;
            return false;
        }

        private void OnSlotClicked(byte con, uint loc, uint seq, bool touch = false)
        {
            if (_session == null || IsPresenting || IsPaused || IsBrowsingZone) return;

            if (CanAcceptPlayerAction && !IsReadingCardDetails && con == _session.HumanPlayerIndex &&
                _modals.FocusChainActivationAt(_session, loc, seq)) return;

            if (_modals.IsFieldCardSelection)
            {
                if (FindFieldCardChoice(con, loc, seq, out int index))
                {
                    var prompt = _session.ActivePrompt;
                    var card = index < 0 ? prompt.mustSelect[-1 - index] :
                        index < prompt.candidates.Count ? prompt.candidates[index] :
                        prompt.unselectCandidates[index - prompt.candidates.Count];
                    uint code = _session.GetVisibleSelectionCode(card);
                    if (code != 0) InspectCard(code, GetSlot(con, loc, seq));
                    _modals.ToggleFieldCard(index, con, loc, seq);
                    Refresh();
                }
                return;
            }

            if (IsPlaceSelectable(con, loc, seq))
            {
                if (IsStagingPlacement)
                {
                    int response = _stagedPlacement.Value.command;
                    _stagedPlacement = null;
                    if (_usingDuelPad)
                        _duelPadPendingFieldFocus = GetSlot(con, loc, seq)?.root;
                    // The staged destination is validated against the engine's placement prompt before use.
                    ExecuteActionInt(response, (con, loc, seq));
                    if (TryAutoFulfillPreferredPrompt()) Refresh();
                    return;
                }

                DuelAudio.PlayClick();
                if (IsPlacePending(con, loc, seq))
                {
                    for (int i = 0; i < _pendingPlaces.Count; i++)
                    {
                        if (_pendingPlaces[i].con == con && _pendingPlaces[i].loc == loc && _pendingPlaces[i].seq == seq)
                        {
                            _pendingPlaces.RemoveAt(i);
                            break;
                        }
                    }
                    Refresh();
                    return;
                }

                _pendingPlaces.Add((con, loc, seq));
                int needed = Mathf.Max(1, (int)(_session.ActivePrompt?.min ?? 1));
                if (_pendingPlaces.Count < needed)
                {
                    Refresh();
                    return;
                }

                byte[] resp = new byte[_pendingPlaces.Count * 3];
                for (int i = 0; i < _pendingPlaces.Count; i++)
                {
                    resp[i * 3] = _pendingPlaces[i].con;
                    resp[i * 3 + 1] = (byte)_pendingPlaces[i].loc;
                    resp[i * 3 + 2] = (byte)_pendingPlaces[i].seq;
                }
                _pendingPlaces.Clear();
                if (_usingDuelPad)
                    _duelPadPendingFieldFocus = GetSlot(con, loc, seq)?.root;
                _actionPopup.SetActive(false);
                _session.SubmitResponse(resp);
                Refresh();
                return;
            }

            if (IsStagingPlacement) return;
            var live = FindLiveCard(con, loc, seq);
            if (live == null) return;
            bool canInspect = live.IsFaceUp || live.isPublic || con == _session.HumanPlayerIndex;
            var slot = GetSlot(con, loc, seq);
            if (canInspect) InspectCard(live.code, slot);
            if (touch && (CanShowCardActions || canInspect && CanOpenTouchCardDetails))
            {
                SelectTouchCardActions(slot?.root);
                return;
            }
            if (canInspect) ShowUnavailableEffect(live);

            if (CanShowCardActions)
            {
                PointAtCardActions(slot?.root);
                return;
            }

            if (live.overlayCodes.Count > 0)
                ShowCardActionsPopup(con, loc, seq, canInspect ? live.code : 0, live.overlayCodes);
        }

        private LiveCardState FindLiveCard(byte con, uint loc, uint seq)
        {
            if (_session == null) return null;
            var ps = _session.Players[con];
            if (loc == YgoConstants.LOCATION_MZONE && seq < 7) return ps.mzone[seq];
            if (loc == YgoConstants.LOCATION_SZONE && seq < 8) return ps.szone[seq];
            return null;
        }

        private void ShowCardActionsPopup(byte con, uint loc, uint seq, uint code, List<uint> overlays,
            (byte con, uint loc, uint seq)? placement = null,
            List<(string label, int responseVal, bool gold)> actions = null)
        {
            if (IsPresenting || IsPaused || _session == null) return;
            actions ??= CollectActions(con, loc, seq);
            var slot = GetSlot(con, loc, seq);
            var anchor = slot?.root ?? GetStack(con, loc)?.root;
            var art = slot?.focusArt;
            if (loc == YgoConstants.LOCATION_HAND && con == _session.HumanPlayerIndex && seq < _myHandViews.Count)
            {
                var view = _myHandViews[(int)seq];
                anchor = view.Rect;
                art = (RectTransform)view.Card.transform;
            }
            PopulateAndShowActionPopup(code == 0 ? "카드 행동" : CardDatabase.GetName(code), actions, overlays, anchor, placement, art);
        }

        private void ShowZoneActionsPopup(string title, List<(SelectableCardItem item, string label, int responseVal, bool gold)> zoneActions)
        {
            var actions = zoneActions.Select(action => (
                $"{CardDatabase.GetName(action.item.code)}  {action.label}", action.responseVal, action.gold)).ToList();
            var first = zoneActions.FirstOrDefault().item;
            var anchor = first != null ? GetStack(first.controller, first.location)?.root : null;
            PopulateAndShowActionPopup(title, actions, null, anchor);
        }

        private void PopulateAndShowActionPopup(string header, List<(string label, int responseVal, bool gold)> actions,
            List<uint> overlays, RectTransform anchor = null, (byte con, uint loc, uint seq)? placement = null,
            RectTransform sourceCard = null)
        {
            bool hasMaterials = overlays != null && overlays.Count > 0;
            if (actions.Count == 0 && !hasMaterials)
            {
                CloseActionPopup();
                return;
            }

            ClearActionRows();
            _duelCommandRaisedCard = sourceCard;
            _actionHeader.text = header;
            var session = _session;
            var prompt = session.ActivePrompt;
            foreach (var action in actions)
            {
                int command = action.responseVal;
                AddActionRow(action.label, () =>
                {
                    if (_session == session && ReferenceEquals(session.ActivePrompt, prompt))
                        ExecuteActionInt(command, placement);
                }, action.gold, CommandIcon(command));
            }
            if (hasMaterials)
            {
                AddActionRow($"엑시즈 소재  {overlays.Count}장", () =>
                {
                    CloseActionPopup();
                    _modals.ShowCodeListViewer("엑시즈 소재", overlays);
                }, false, "LOOK");
            }
            AddActionRow("취소", CloseActionPopup, false);
            PlaceActionWindow(anchor);
        }

        private string CommandIcon(int response)
        {
            int command = response & 0xFFFF;
            if (_session.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD)
                return command == 1 ? "ATTACK" : "ACTION";
            return command switch
            {
                0 => "SUMMON",
                1 => "SUMMONSP",
                2 => "TURN_DEF",
                3 => "SETMONST",
                4 => "SET",
                _ => "ACTION",
            };
        }

        private void AddActionRow(string label, Action action, bool selected, string icon = null)
        {
            Sprite iconSprite = DuelUiIcons.Action(icon);
            float height = _mobileLayout ? MobileSafeArea.MinimumTouchSize(_actionList) : 44f;
            var button = DuelUiKit.CreateWrappedButton(_actionList, "Action", label, action, 15, selected, height, iconSprite);
            DuelStyle.ApplyButton(button, selected);
        }

        private void ClearActionRows()
        {
            HideDuelPadFrame();
            _duelCommandRaisedCard = null;
            for (int i = _actionList.childCount - 1; i >= 0; i--)
            {
                var row = _actionList.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    row.SetActive(false);
                    UnityEngine.Object.Destroy(row);
                }
                else UnityEngine.Object.DestroyImmediate(row);
            }
            _actionList.anchoredPosition = Vector2.zero;
        }

        private void PlaceActionWindow(RectTransform anchor = null)
        {
            var parent = (RectTransform)_actionPopup.transform;
            float width = Mathf.Min(410f, parent.rect.width * 0.88f);
            float maxHeight = parent.rect.height * 0.85f;
            _actionWindow.sizeDelta = new Vector2(width, maxHeight);
            _actionPopup.transform.SetAsLastSibling();
            _actionPopup.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_actionList);
            float height = Mathf.Min(63f + LayoutUtility.GetPreferredHeight(_actionList), maxHeight);
            _actionWindow.sizeDelta = new Vector2(width, height);
            Vector2 center = Vector2.zero;
            if (anchor != null)
            {
                center = parent.InverseTransformPoint(anchor.position);
                float offset = width * 0.5f + 54f;
                center.x += center.x > parent.rect.width * 0.18f ? -offset : offset;
                center.x = Mathf.Clamp(center.x, parent.rect.xMin + width * 0.5f + 6f, parent.rect.xMax - width * 0.5f - 6f);
                center.y = Mathf.Clamp(center.y, parent.rect.yMin + height * 0.5f + 6f, parent.rect.yMax - height * 0.5f - 6f);
            }
            _actionWindowTarget = center;
            _actionWindowOffset = anchor != null
                ? Vector2.ClampMagnitude((Vector2)parent.InverseTransformPoint(anchor.position) - center, 16f)
                : Vector2.down * 10f;
            _actionWindowStart = Application.isPlaying ? Time.unscaledTime : -1f;
            SetActionWindowProgress(Application.isPlaying ? 0f : 1f);
            UiEntranceMotion.Play(_actionWindow.gameObject, .62f, .14f);
        }

        private void AdvanceActionWindow()
        {
            if (!_actionPopup.activeSelf || _actionWindowStart < 0f) return;
            float progress = Mathf.Clamp01((Time.unscaledTime - _actionWindowStart) / .14f);
            SetActionWindowProgress(progress);
            if (progress >= 1f) _actionWindowStart = -1f;
        }

        private void SetActionWindowProgress(float progress)
        {
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            _actionWindow.anchoredPosition = _actionWindowTarget + _actionWindowOffset * (1f - eased);
            _actionWindow.localScale = Vector3.one * Mathf.Lerp(.96f, 1f, eased);
        }

        private void CloseActionPopup()
        {
            _actionPopup.SetActive(false);
            _duelCommandRaisedCard = null;
            if (!IsPaused) return;
            IsPaused = false;
            if (CardAnimationManager.Instance != null) CardAnimationManager.Instance.Paused = false;
            Refresh();
        }

        private void OnPhaseShortcut(int targetPhase)
        {
            if (!CanAcceptPlayerAction || IsBrowsingZone) return;
            var p = _session.ActivePrompt;
            if (p.playerId != _session.HumanPlayerIndex) return;

            if (p.messageType == YgoConstants.MSG_SELECT_IDLECMD)
            {
                if (targetPhase == YgoConstants.PHASE_BATTLE && p.canBp)
                {
                    _actionPopup.SetActive(false);
                    _session.SubmitResponseInt(6);
                    Refresh();
                }
                else if (targetPhase == YgoConstants.PHASE_END && p.canEp)
                {
                    _actionPopup.SetActive(false);
                    _session.SubmitResponseInt(7);
                    Refresh();
                }
            }
            else if (p.messageType == YgoConstants.MSG_SELECT_BATTLECMD)
            {
                if (targetPhase == YgoConstants.PHASE_MAIN2 && p.canM2)
                {
                    _actionPopup.SetActive(false);
                    _session.SubmitResponseInt(2);
                    Refresh();
                }
                else if (targetPhase == YgoConstants.PHASE_END && p.canEp)
                {
                    _actionPopup.SetActive(false);
                    _session.SubmitResponseInt(3);
                    Refresh();
                }
            }
        }

        private void OnPauseClicked()
        {
            if (_session == null || _session.IsFinished) return;
            IsPaused = true;
            if (CardAnimationManager.Instance != null) CardAnimationManager.Instance.Paused = true;
            ClearActionRows();
            _actionHeader.text = "일시 정지";
            AddActionRow("계속하기", CloseActionPopup, true);
            AddActionRow("항복", ConfirmSurrender, false, "SURRENDER");
            PlaceActionWindow();
        }

        private void ConfirmSurrender()
        {
            ClearActionRows();
            _actionHeader.text = "듀얼을 항복하시겠습니까?";
            AddActionRow("취소", OnPauseClicked, true);
            AddActionRow("항복", () =>
            {
                CardAnimationManager.Instance?.Clear();
                ClearArrivals();
                _session.Surrender(_session.HumanPlayerIndex);
                CloseActionPopup();
            }, false, "SURRENDER");
            PlaceActionWindow();
        }

        private void HandleCoinTossedEvent(byte player, IReadOnlyList<bool> coins)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null || coins.Count == 0) return;
            var point = animator.GetPoint(_boardRect, new Vector2(0.5f, 0.54f));
            animator.QueueRandomResult(string.Join("   /   ", coins.Select(coin => coin ? "앞면" : "뒷면")),
                point, player != _session.HumanPlayerIndex, DuelAudio.PlayCoin);
        }

        private void HandleDiceRolledEvent(byte player, IReadOnlyList<int> dice)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null || dice.Count == 0) return;
            var point = animator.GetPoint(_boardRect, new Vector2(0.5f, 0.54f));
            animator.QueueRandomResult("주사위   " + string.Join("   /   ", dice),
                point, player != _session.HumanPlayerIndex, DuelAudio.PlayDice);
        }

        private void BuildCardZoomModal(Transform root, bool onIPhone)
        {
            var dim = DuelUiKit.FillRect("Overlay_CardZoom", root);
            _cardZoomModal = dim.gameObject;
            DuelUiKit.AddImage(dim.gameObject, DuelUiKit.WhiteSprite, new Color(.012f, .026f, .044f, .83f));
            var backdrop = dim.gameObject.AddComponent<Button>();
            backdrop.navigation = new Navigation { mode = Navigation.Mode.None };
            backdrop.onClick.AddListener(() => CloseCardZoomModal());

            var content = onIPhone ? MobileSafeArea.Create(dim) : dim;
            var win = CreateInspectorSurface(content, "ZoomWin", onIPhone ? new Vector2(.03f, .027f) : new Vector2(.11f, .045f),
                onIPhone ? new Vector2(.97f, .973f) : new Vector2(.89f, .955f));
            win.gameObject.AddComponent<Button>().interactable = false;

            // Header bar
            var hdr = DuelUiKit.CreateRect("Hdr", win, new Vector2(.012f, onIPhone ? .822f : .9f),
                new Vector2(.988f, onIPhone ? .991f : .985f),
                Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(hdr, new Color(.065f, .113f, .133f, .92f));
            var title = _zoomHeaderTitle = DuelUiKit.CreateRect("Title", hdr,
                new Vector2(.02f, .12f), new Vector2(.82f, .88f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddFittedText(title, "T", "카드 상세 정보", 19, 12,
                TextAnchor.MiddleLeft, DuelUiKit.GoldAccent);

            var closeRt = DuelUiKit.CreateRect("Close", hdr, new Vector2(.84f, onIPhone ? .04f : .15f),
                new Vector2(.98f, onIPhone ? .96f : .85f), Vector2.zero, Vector2.zero);
            _zoomCloseButton = DuelStyle.Button(closeRt, "B", onIPhone ? "닫기" : "닫기 (ESC)", () => CloseCardZoomModal(), 15);

            // Left Side: Large Card View
            float cardRight = onIPhone ? .375f : .448f;
            var cardHolder = DuelUiKit.CreateRect("CardHolder", win, new Vector2(.033f, onIPhone ? .172f : .111f),
                new Vector2(cardRight, onIPhone ? .812f : .891f), Vector2.zero, Vector2.zero);
            _zoomCardView = CompositeCardView.Create(cardHolder, "ZoomCard", true);
            _zoomCardView.UsePrintedFaceOnly();
            CardOverframeInput.Attach(cardHolder, _zoomCardView, onIPhone);
            AddInspectorArtworkFrame(_zoomCardView.transform.Find("AspectBox"));
            _zoomDisabledBadge = BuildDisabledBadge(_zoomCardView.transform, 24);
            var artwork = DuelUiKit.CreateRect("ArtworkOptions", win, new Vector2(.04f, onIPhone ? .018f : .035f),
                new Vector2(cardRight, onIPhone ? .157f : .098f),
                Vector2.zero, Vector2.zero);
            _zoomArtworkControls = artwork.gameObject;
            float artworkButtonWidth = onIPhone ? .30f : .19f;
            var previous = DuelUiKit.CreateRect("Previous", artwork, Vector2.zero, new Vector2(artworkButtonWidth, 1f),
                Vector2.zero, Vector2.zero);
            DuelStyle.Button(previous, "Button", "‹", () => CycleArtwork(-1), 20);
            var next = DuelUiKit.CreateRect("Next", artwork, new Vector2(1f - artworkButtonWidth, 0f), Vector2.one,
                Vector2.zero, Vector2.zero);
            DuelStyle.Button(next, "Button", "›", () => CycleArtwork(1), 20);
            var counter = DuelUiKit.CreateRect("Counter", artwork,
                new Vector2(artworkButtonWidth + .01f, 0f), new Vector2(.99f - artworkButtonWidth, 1f),
                Vector2.zero, Vector2.zero);
            _zoomArtworkLabel = DuelUiKit.AddFittedText(counter, "Label", "", 15, 9, TextAnchor.MiddleCenter, Color.white);

            // Right Side: Card Details
            var infoRt = DuelUiKit.CreateRect("Info", win, new Vector2(cardRight + .03f, .05f),
                new Vector2(.96f, onIPhone ? .81f : .88f), Vector2.zero, Vector2.zero);

            var name = DuelUiKit.CreateRect("Name", infoRt,
                new Vector2(0f, 0.88f), new Vector2(0.82f, 1f), Vector2.zero, Vector2.zero);
            _zoomName = DuelUiKit.AddFittedText(name, "N", "", 27, 12, TextAnchor.MiddleLeft, DuelUiKit.TextPrimary);
            _zoomRulesName = CreateRulesNameLabel(name, onIPhone ? 20 : 17);

            var race = DuelUiKit.CreateRect("RaceIc", infoRt, new Vector2(.833f, .88f), new Vector2(.902f, 1f), Vector2.zero, Vector2.zero);
            _zoomRaceIcon = DuelUiKit.AddImage(race.gameObject, null, Color.white, true, false);
            var attribute = DuelUiKit.CreateRect("AttributeIc", infoRt, new Vector2(.916f, .88f), new Vector2(.985f, 1f), Vector2.zero, Vector2.zero);
            _zoomAttributeIcon = DuelUiKit.AddImage(attribute.gameObject, null, Color.white, true, false);

            var stats = DuelUiKit.CreateRect("Stats", infoRt,
                new Vector2(0f, .735f), new Vector2(1f, .875f), Vector2.zero, Vector2.zero);
            _zoomStats = DuelUiKit.AddFittedText(stats, "S", "", 21, 10, TextAnchor.MiddleLeft, DuelStyle.Muted);
            var rating = DuelUiKit.CreateRect("Rating", stats, new Vector2(.725f, .19f), new Vector2(.985f, .81f),
                Vector2.zero, Vector2.zero);
            _zoomRating = new DuelRatingBadge(rating, compact: false);
            var combat = DuelUiKit.CreateRect("CombatStats", infoRt,
                new Vector2(0f, .635f), new Vector2(1f, .721f), Vector2.zero, Vector2.zero);
            _zoomCombat = new DuelStatStrip(combat, compact: false);

            _zoomPasscode = DuelUiKit.AddFittedText(DuelUiKit.CreateRect("Passcode", infoRt,
                new Vector2(0f, .571f), new Vector2(.60f, .626f), Vector2.zero, Vector2.zero),
                "P", "", 12, 9, TextAnchor.MiddleLeft, DuelUiKit.CyanAccent);
            var counterChip = DuelUiKit.CreateRect("Counters", infoRt,
                new Vector2(.64f, .571f), new Vector2(1f, .626f), Vector2.zero, Vector2.zero);
            _zoomCounters = new DuelCounterBadge(counterChip, compact: false);

            _zoomScales = new DuelPendulumScalePair(DuelUiKit.CreateRect("Scales", infoRt,
                new Vector2(0f, .503f), new Vector2(1f, .558f), Vector2.zero, Vector2.zero));
            var descBox = _zoomDescriptionBox = DuelUiKit.CreateRect("DescBox", infoRt, Vector2.zero, new Vector2(1f, .561f),
                Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(descBox, new Color(.018f, .045f, .061f, .96f)).raycastTarget = true;
            var vp = DuelUiKit.CreateRect("VP", descBox, Vector2.zero, Vector2.one,
                new Vector2(10f, 8f), new Vector2(-19f, -8f));
            vp.gameObject.AddComponent<RectMask2D>();
            var ct = DuelUiKit.CreateRect("C", vp, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            ct.pivot = new Vector2(0.5f, 1f);
            var contentLayout = ct.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.childControlWidth = contentLayout.childControlHeight = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.spacing = 8f;
            _zoomCounterTypes = DuelUiKit.AddText(ct, "CounterTypes", "", onIPhone ? 21 : 16,
                TextAnchor.UpperLeft, DuelCounterBadge.Ink);
            _zoomCounterTypes.gameObject.SetActive(false);
            _zoomDesc = DuelUiKit.AddText(ct, "EffectText", "", onIPhone ? 23 : 17,
                TextAnchor.UpperLeft, DuelUiKit.TextPrimary);
            _zoomDesc.lineSpacing = 1.15f;
            _zoomDesc.supportRichText = false;
            ct.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _zoomScroll = descBox.gameObject.AddComponent<ScrollRect>();
            _zoomScroll.horizontal = false;
            _zoomScroll.scrollSensitivity = 32f;
            _zoomScroll.movementType = ScrollRect.MovementType.Clamped;
            _zoomScroll.viewport = vp;
            _zoomScroll.content = ct;
            _zoomScroll.verticalScrollbar = CreateInspectorScrollbar(descBox);
            _zoomScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            _cardZoomModal.SetActive(false);
        }

        public void ShowCardZoomModal(uint code) => ShowCardZoomModal(code, null);

        private void ShowCardZoomModal(LiveCardState card) =>
            ShowCardZoomModal(card.code, InspectionLocation(GetSlot(card.controller, card.location, card.sequence)), card.Appearance);

        private void ShowCardZoomModal(uint code, (byte controller, uint location, uint sequence)? fieldCard,
            DeckCardAppearance? appearance = null)
        {
            if (code == 0 || !Root.activeInHierarchy) return;
            bool opening = !_cardZoomModal.activeSelf;
            if (opening)
            {
                _focusBeforeZoom = EventSystem.current?.currentSelectedGameObject;
                if (IsBrowsingZone || _modals.IsPromptOpen) _cardZoomModal.transform.SetParent(Root.transform.parent, false);
                _cardZoomModal.transform.SetAsLastSibling();
                _modals.SetCardDetailsOpen(true);
                _cardZoomModal.SetActive(true);
            }
            var info = CardDatabase.Get(code);
            _zoomFieldCard = fieldCard;
            _zoomAppearance = appearance;
            _zoomBanishedCard = null;
            RefreshZoomReadout(code, showChanges: false);
            if (info != null)
            {
                _zoomName.text = info.name;
                _zoomPasscode.text = $"카드 번호 {info.code:D8}";
                _zoomDesc.text = info.desc;
            }
            else
            {
                _zoomName.text = $"카드 #{code}";
                _zoomPasscode.text = $"카드 번호 {code}";
                _zoomDesc.text = "";
            }
            UpdateArtworkOptions(code, 0);
            _zoomScroll.StopMovement();
            _zoomScroll.content.anchoredPosition = Vector2.zero;
            DeckPadUi.LimitKeyboardNavigation(_cardZoomModal.transform);
            if (opening)
            {
                EventSystem.current?.SetSelectedGameObject(_zoomCloseButton.gameObject);
                UiEntranceMotion.Play(_cardZoomModal, duration: .15f);
            }
        }

        private void RefreshZoomReadout(uint code, bool showChanges = true)
        {
            var card = ReadInspectedStats(code, _zoomFieldCard);
            if (card.Live == null && !IsInspectedCardStillOnField(code, _zoomFieldCard)) _zoomFieldCard = null;
            _zoomCardView.SetCard(code, card, card.Live?.Appearance ?? _zoomAppearance ?? DefaultDuelAppearance);
            _zoomDisabledBadge.SetActive(card.Disabled);
            BindRulesName(_zoomName, _zoomRulesName, card.RulesName);
            var (identity, details) = FormatInspectorStats(card);
            _zoomStats.text = identity.Length == 0 ? details : details.Length == 0 ? identity : $"{identity}\n{details}";
            var rating = card.InspectionRating;
            _zoomStats.rectTransform.anchorMax = new Vector2(rating.Kind == DuelRatingKind.None ? .982f : .70f, .965f);
            _zoomRating.Bind(rating, showChanges && card.Live != null);
            bool hasScales = _zoomScales.Bind(card, showChanges && card.Live != null);
            _zoomDescriptionBox.anchorMax = new Vector2(1f, hasScales ? .494f : .561f);
            BindIdentityIcons(card, _zoomRaceIcon, _zoomAttributeIcon);
            _zoomCounters.Bind(card, showChanges);
            bool mixedCounters = card.Counters.Length > 1;
            _zoomCounterTypes.gameObject.SetActive(mixedCounters);
            if (mixedCounters)
                _zoomCounterTypes.text = string.Join("\n", card.Counters.Select(counter =>
                    $"{DuelCounterBadge.TypeName(counter.Type)} {counter.Count}개"));
            _zoomCombat.Bind(card, card.Live != null && card.Live.controller != _session.HumanPlayerIndex,
                showChanges && card.Live != null);
        }

        private void CloseCardZoomModal(bool restoreFocus = true)
        {
            if (!_cardZoomModal.activeSelf) return;
            _zoomFieldCard = null;
            _zoomAppearance = null;
            _zoomBanishedCard = null;
            if (ActivationNoticeInDetails) HideActivationNotice();
            _cardZoomModal.SetActive(false);
            if (_cardZoomModal.transform.parent != Root.transform) _cardZoomModal.transform.SetParent(Root.transform, false);
            _modals.SetCardDetailsOpen(false);
            var previous = _focusBeforeZoom;
            _focusBeforeZoom = null;
            var events = EventSystem.current;
            if (events == null) return;
            if (restoreFocus && _modals.RestoreFocusAfterCardDetails(previous)) return;
            if (restoreFocus && previous != null && previous.TryGetComponent<Selectable>(out var control) &&
                control.isActiveAndEnabled && control.IsInteractable())
                events.SetSelectedGameObject(previous);
            else if (events.currentSelectedGameObject?.transform.IsChildOf(_cardZoomModal.transform) == true)
                events.SetSelectedGameObject(null);
        }

        private void CycleArtwork(int direction)
        {
            uint code = _zoomCardView.CurrentCode;
            UpdateArtworkOptions(code, direction);
            RefreshZoomReadout(code);
            Refresh();
        }

        private void UpdateArtworkOptions(uint code, int direction)
        {
            var choices = CardArtLoader.GetSelectableArtwork(code, _zoomCardView.Appearance?.overframe == true);
            _zoomArtworkControls.SetActive(choices.Length > 1);
            if (choices.Length < 2) return;
            int index = CardArtLoader.ArtworkChoiceIndex(code, choices, _zoomCardView.Appearance?.ArtworkVariant);
            if (direction != 0)
            {
                int next = (index + direction + choices.Length) % choices.Length;
                if (CardArtLoader.SetDuelArtworkVariant(code, choices[next])) index = next;
            }
            _zoomArtworkLabel.text = $"일러스트 {index + 1} / {choices.Length}";
        }
    }
}
