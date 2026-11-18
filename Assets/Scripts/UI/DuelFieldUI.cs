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
        private Image _inspTitleAccent;
        private Button _inspectorArtworkButton;
        private Text _inspStats;
        private DuelCounterBadge _inspCounters;
        private DuelStatStrip _inspCombat;
        private Text _inspDesc;
        private Text _logFeed;
        private GameObject _logWindow;
        private ScrollRect _logScroll;
        private readonly List<Text> _logBlocks = new List<Text>();
        private ScrollRect _inspectScroll;
        private uint _inspectedCode;
        private (byte controller, uint location, uint sequence)? _inspectedFieldCard;

        // Opponent and player HUD
        private Image _oppBustImg;
        private Text _oppNameText;
        private Text _oppLpText;
        private RectTransform _oppLpFill;
        private Text _myLpText;
        private RectTransform _myLpFill;
        private Text _turnBadgeText;
        private Text _hintBannerText;
        private Text _chainBannerText;
        private DuelEffectGraphic _responseAttackArrow;
        private Image _chainIcon;
        private Button _chainConfirmationButton;
        private Text _chainConfirmationLabel;

        // Phase indicator and shortcuts
        private Image _phaseRim;
        private readonly Image[] _phaseSteps = new Image[PhaseEntries.Length];
        private Button _phaseButton;
        private GameObject _phasePicker;
        private Text _phaseLabel;
        private Text _phaseControlHint;
        private readonly List<(int phase, Button button, Text label, Image icon)> _phaseChoices = new();
        private Button _phaseCancel;
        private GameObject _placementBanner;
        private Text _placementHint;
        private Button _placementCancel;

        private static readonly Color HudEdge = new Color(.30f, .34f, .33f, .94f);
        private static readonly Color HudSurface = new Color(.035f, .062f, .076f, .975f);
        private static readonly Color HudText = new Color(.94f, .95f, .93f);
        private static readonly (string name, string label, int phase, string icon)[] PhaseEntries =
        {
            ("Draw", "드로우", YgoConstants.PHASE_DRAW, "dp"),
            ("Standby", "스탠바이", YgoConstants.PHASE_STANDBY, "sp"),
            ("Main1", "메인 1", YgoConstants.PHASE_MAIN1, "mp1"),
            ("Battle", "배틀", YgoConstants.PHASE_BATTLE, "bp"),
            ("Main2", "메인 2", YgoConstants.PHASE_MAIN2, "mp2"),
            ("End", "엔드", YgoConstants.PHASE_END, "ep"),
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
            public Button button;
            public byte controller;
            public uint location;
        }

        private StackTileView _myDeckTile, _myExtraTile, _myGyTile, _myBanTile;
        private StackTileView _oppDeckTile, _oppExtraTile, _oppGyTile, _oppBanTile;
        private Button _extraSummonButton;
        private Button _graveEffectButton;

        // High-res Card Zoom Modal
        private GameObject _cardZoomModal;
        private Button _zoomCloseButton;
        private Button _zoomRelatedButton;
        private GameObject _focusBeforeZoom;
        private CompositeCardView _zoomCardView;
        private GameObject _zoomArtworkControls;
        private Text _zoomArtworkLabel;
        private Text _zoomName;
        private Text _zoomStats;
        private DuelCounterBadge _zoomCounters;
        private DuelStatStrip _zoomCombat;
        private (byte controller, uint location, uint sequence)? _zoomFieldCard;
        private Text _zoomPasscode;
        private Text _zoomDesc;
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
        private readonly Dictionary<(byte con, uint loc, uint seq), int> _arrivingCards = new Dictionary<(byte, uint, uint), int>();
        private readonly Dictionary<(byte Controller, uint Sequence), CardMoveReadoutSnapshot> _pendingCardReadouts = new();
        private const uint FieldMaterials = YgoConstants.LOCATION_MZONE | YgoConstants.LOCATION_OVERLAY;
        private readonly Dictionary<(byte Controller, uint Sequence), MaterialReadout> _materialReadouts = new();
        private readonly Dictionary<(byte Controller, uint Location, uint Sequence), ChainCardReadout> _chainCardReadouts = new();

        private sealed class MaterialReadout
        {
            internal LiveCardState Source;
            internal int Count;
            internal int Pending;
        }
        private readonly Dictionary<(byte controller, uint sequence), List<(uint code, CardPose from, bool faceDown, uint index)>>
            _pendingXyzMaterials = new Dictionary<(byte, uint), List<(uint, CardPose, bool, uint)>>();
        private bool _dragHighlightMZone;
        private bool _dragHighlightSZone;
        private bool _dragHighlightAttack;
        private RectTransform _boardRect;
        private DuelMat _mat;

        // Hand rows
        private RectTransform _myHandBar;
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
            public CardDragTrigger Trigger;
            public (Vector2 min, Vector2 max, float angle) Layout;
            public bool HasLayout;
            public object PendingArrival;
        }

        private sealed class ChainCardReadout
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
            _modals.SetInspectorControls(_inspectorArtworkButton, _duelPadInspectButton);
            _modals.SetCandidateInspector(InspectCandidate);
            _modals.SetBrowsablePiles(
                _myGyTile.root.GetComponent<DuelZoneImage>(), _myExtraTile.root.GetComponent<DuelZoneImage>(),
                _myBanTile.root.GetComponent<DuelZoneImage>(), _oppGyTile.root.GetComponent<DuelZoneImage>(),
                _oppExtraTile.root.GetComponent<DuelZoneImage>(), _oppBanTile.root.GetComponent<DuelZoneImage>());
            _modals.SetZoneBrowserActions(SwitchBrowsablePile, ShowCardZoomModal);

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
            session.OnCardMoved += HandleCardMovedEvent;
            session.OnCardLocationsChanged += HandleCardLocationsChanged;
            session.OnSpecialSummonBoundary += CompleteSpecialSummons;
            session.OnCardsDrawn += HandleCardsDrawn;
            session.OnLifePointChange += HandleLifePointsChanged;
            session.OnTurnStarted += HandleTurnStarted;
            session.OnPhaseChanged += HandlePhaseChanged;
            session.OnChainActivated += HandleChainActivatedEvent;
            session.OnChainResolving += HandleChainResolvingEvent;
            session.OnChainReadoutChanged += QueueChainReadouts;
            session.OnAttackDeclared += HandleAttackEvent;
            session.OnBattleResolved += HandleBattleResolved;
            session.OnCoinTossed += HandleCoinTossedEvent;
            session.OnDiceRolled += HandleDiceRolledEvent;

            Root.SetActive(true);
            _oppNameText.text = opponent?.name ?? opponentDeck?.title ?? "AI 듀얼리스트";
            _oppBustImg.sprite = LotdUiKit.LoadOpponentPortrait(opponent);
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
                _session.OnCardMoved -= HandleCardMovedEvent;
                _session.OnCardLocationsChanged -= HandleCardLocationsChanged;
                _session.OnSpecialSummonBoundary -= CompleteSpecialSummons;
                _session.OnCardsDrawn -= HandleCardsDrawn;
                _session.OnLifePointChange -= HandleLifePointsChanged;
                _session.OnTurnStarted -= HandleTurnStarted;
                _session.OnPhaseChanged -= HandlePhaseChanged;
                _session.OnChainActivated -= HandleChainActivatedEvent;
                _session.OnChainResolving -= HandleChainResolvingEvent;
                _session.OnChainReadoutChanged -= QueueChainReadouts;
                _session.OnAttackDeclared -= HandleAttackEvent;
                _session.OnBattleResolved -= HandleBattleResolved;
                _session.OnCoinTossed -= HandleCoinTossedEvent;
                _session.OnDiceRolled -= HandleDiceRolledEvent;
                _session = null;
            }

            ClearArrivals();
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
            _extraSummonButton.gameObject.SetActive(false);
            _graveEffectButton.gameObject.SetActive(false);
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
            InspectCardAt(code, (item.controller, item.location, item.sequence));

        private void InspectCardAt(uint code, (byte controller, uint location, uint sequence)? fieldCard)
        {
            if (code == 0) return;
            bool changed = code != _inspectedCode;
            if (!changed && _inspectedFieldCard == fieldCard) return;
            _inspectedCode = code;
            _inspectedFieldCard = fieldCard;
            RefreshInspectorReadout(showChanges: false);
            RefreshLinkedZones();
            if (!changed) return;

            _duelPadInspectButton.interactable = true;
            _inspectorArtworkButton.interactable = true;
            _inspCard.gameObject.SetActive(true);
            _inspCard.SetCard(code);
            var info = CardDatabase.Get(code);
            _inspTitleAccent.color = InspectorTitleAccent(info?.type ?? 0);
            _inspName.text = info?.name ?? $"카드 #{code}";
            _inspDesc.text = info?.desc ?? "";
            _inspectScroll.StopMovement();
            _inspectScroll.content.anchoredPosition = Vector2.zero;
        }

        private void ResetCardInspector()
        {
            _inspectedCode = 0;
            _inspectedFieldCard = null;
            _inspCard.SetCard(0);
            _inspCard.gameObject.SetActive(false);
            _duelPadInspectButton.interactable = false;
            _inspectorArtworkButton.interactable = false;
            _inspTitleAccent.color = InspectorTitleAccent(0);
            _inspName.text = "카드 정보";
            _inspStats.text = "";
            _inspCombat.Hide();
            SizeInspectorDescription(hasCombat: false);
            _inspCounters.Bind(0);
            SetLinkedZones(0);
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
            ClearMovedReadouts(move);
            if (swapPartner != null) ClearMovedReadouts(swapPartner);
            if (move.fromLoc == YgoConstants.LOCATION_MZONE)
                _pendingCardReadouts.Remove((move.fromCon, move.fromSeq));
            if (swapPartner?.fromLoc == YgoConstants.LOCATION_MZONE)
                _pendingCardReadouts.Remove((swapPartner.fromCon, swapPartner.fromSeq));

            var before = _inspectedFieldCard;
            _inspectedFieldCard = FollowFieldCard(before, _inspectedCode, move, swapPartner);
            if (_zoomFieldCard.HasValue)
                _zoomFieldCard = FollowFieldCard(_zoomFieldCard, _zoomCardView.CurrentCode, move, swapPartner);

            if (before is not { } source || before == _inspectedFieldCard) return;
            var slot = GetSlot(source.controller, source.location, source.sequence);
            if (slot == null) return;
            ClearPointerCard(slot.focusArt);
            if (_usingDuelPad && (_duelPadBoardFocus == slot.root || _duelPadPendingFieldFocus == slot.root))
                _duelPadPendingFieldFocus = _inspectedFieldCard is { } destination
                    ? GetSlot(destination.controller, destination.location, destination.sequence)?.root : null;
        }

        private static (byte controller, uint location, uint sequence)? FollowFieldCard(
            (byte controller, uint location, uint sequence)? inspected, uint code, CardMoveEvent move, CardMoveEvent swapPartner)
        {
            if (inspected is not { } source) return null;
            if (source != (move.fromCon, move.fromLoc, move.fromSeq))
            {
                if (swapPartner == null || source != (swapPartner.fromCon, swapPartner.fromLoc, swapPartner.fromSeq))
                    return source;
                move = swapPartner;
            }
            if (move.code != code || (move.toPos & YgoConstants.POS_FACEUP) == 0 ||
                move.toLoc is not (YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE))
                return null;
            return (move.toCon, move.toLoc, move.toSeq);
        }

        private void RefreshInspectorReadout(bool showChanges = true)
        {
            if (_inspectedCode == 0) return;
            var live = ReadVisibleFieldCard(_inspectedCode, _inspectedFieldCard);
            if (live == null) _inspectedFieldCard = null;
            var card = ReadFieldStats(_inspectedCode, live);
            _inspStats.text = FormatInspectorStats(card, compact: true);
            BindIdentityIcons(card, _inspRaceIcon, _inspAttributeIcon);
            _inspCounters.Bind(card.CounterCount, showChanges);
            _inspCombat.Bind(card, live != null && _inspectedFieldCard.Value.controller != _session.HumanPlayerIndex,
                showChanges && live != null);
            SizeInspectorDescription(card.IsMonster);
        }

        private void SizeInspectorDescription(bool hasCombat)
        {
            var description = (RectTransform)_inspectScroll.transform;
            description.anchorMax = new Vector2(description.anchorMax.x, hasCombat ? .275f : .337f);
        }

        private static Color InspectorTitleAccent(uint type)
        {
            if (type == 0) return DuelStyle.Muted;
            if ((type & YgoConstants.TYPE_SPELL) != 0) return new Color(.42f, .89f, .75f);
            if ((type & YgoConstants.TYPE_TRAP) != 0) return new Color(.98f, .60f, .84f);
            if ((type & YgoConstants.TYPE_LINK) != 0) return DuelStyle.Player;
            return DuelStyle.Gold;
        }

        private void RefreshLinkedZones() => SetLinkedZones(GetSelectedLinkCells());

        private uint GetSelectedLinkCells()
        {
            if (_session == null || _session.IsFinished || IsPresenting || IsPaused || IsReadingLog ||
                IsReadingCardDetails || _phasePicker.activeSelf || IsStagingPlacement || IsNativePlacement(_session.ActivePrompt) ||
                _modals.IsPromptOpen || _modals.IsZoneViewerOpen || CardDragManager.Instance?.IsDragging == true ||
                _inspectedFieldCard is not { } source)
                return 0;

            var card = ReadVisibleFieldCard(_inspectedCode, source);
            var slot = GetSlot(source.controller, source.location, source.sequence);
            if (card == null || (card.type & YgoConstants.TYPE_LINK) == 0 || card.linkMarker == 0 ||
                slot?.linkCell is not { } origin)
                return 0;

            int facing = source.controller == _session.HumanPlayerIndex ? 1 : -1;
            uint cells = 0;
            foreach (var arrow in DuelLinkCompass.Marks)
            {
                if ((card.linkMarker & arrow.mask) == 0) continue;
                var target = origin + new Vector2Int(arrow.direction.x, -arrow.direction.y) * facing;
                if ((uint)target.x < 5 && (uint)target.y < 5 && _linkGrid[target.x, target.y] != null)
                    cells |= 1u << (target.y * 5 + target.x);
            }
            return cells;
        }

        private void SetLinkedZones(uint cells)
        {
            uint changed = _highlightedLinkCells ^ cells;
            if (changed == 0) return;
            _highlightedLinkCells = cells;
            for (int row = 0; row < 5; row++)
            for (int column = 0; column < 5; column++)
            {
                uint bit = 1u << (row * 5 + column);
                if ((changed & bit) != 0) _linkGrid[column, row].bgImg.SetLinkHighlight((cells & bit) != 0);
            }
        }

        private static void BindIdentityIcons(DuelCardReadout card, Image raceIcon, Image attributeIcon)
        {
            Sprite race = null;
            Sprite attribute = null;
            if (card.IsMonster)
            {
                race = LotdUiKit.GetRaceSprite(card.Race);
                attribute = LotdUiKit.GetAttributeSprite(card.Attribute);
            }
            else if ((card.Type & (YgoConstants.TYPE_SPELL | YgoConstants.TYPE_TRAP)) != 0)
                race = LotdUiKit.LoadSprite($"LOTD/Icons/{YgoConstants.GetSpellTrapSubSpriteName(card.Type)}");
            raceIcon.sprite = race;
            raceIcon.gameObject.SetActive(race != null);
            attributeIcon.sprite = attribute;
            attributeIcon.gameObject.SetActive(attribute != null);
        }

        private static string FormatInspectorStats(DuelCardReadout card, bool compact = false)
        {
            if (card.Type == 0) return "";
            string type = card.IsScale ? "펜듈럼 마법" : YgoConstants.GetTypeTagKR(card.Type).Replace(" / ", " · ");
            if (card.IsPendulum) type += $" · 스케일 {card.LeftScale}/{card.RightScale}";
            if (!card.IsMonster) return type;

            if (!YgoConstants.RaceNamesKR.TryGetValue(card.Race, out string race))
                race = string.Join("/", YgoConstants.RaceNamesKR.Where(pair => (pair.Key & card.Race) != 0).Select(pair => pair.Value));
            if (!YgoConstants.AttributeNamesKR.TryGetValue(card.Attribute, out string attribute))
                attribute = string.Join("/", YgoConstants.AttributeNamesKR.Where(pair => (pair.Key & card.Attribute) != 0).Select(pair => pair.Value));
            if (compact && race.Contains('/')) race = "복수 종족";
            if (compact && attribute.Length > 8) attribute = "복수 속성";
            string identity = string.Join(" · ", new[] { race, attribute, card.Rating }.Where(part => !string.IsNullOrEmpty(part)));
            if (card.Live == null) return $"{identity}\n{type}";

            string position = card.Live.IsDefense ? "수비 표시" : "공격 표시";
            string status = card.Status;
            if (status.Length > 0) position += " · " + status;
            return compact ? $"{identity}\n{type} · {position}" : $"{identity}\n{type}\n{position}";
        }

        private void ShowZoomRelatedCards()
        {
            uint code = _zoomCardView.CurrentCode;
            if (code == 0) return;
            var related = CardDatabase.GetRelatedCards(code);
            var codes = related.Select(card => card.code).ToArray();
            CloseCardZoomModal();
            _modals.ShowCodeListViewer($"[{CardDatabase.GetName(code)}] 관련 카드", codes);
        }


        private void BuildUI(Transform parent)
        {
            var root = LotdUiKit.FillRect("Screen_DuelField", parent);
            Root = root.gameObject;

            var bg = LotdUiKit.FillRect("ArenaGround", root);
            var ground = bg.gameObject.AddComponent<DuelArenaGround>();
            ground.texture = DuelVisualAssets.ArenaBackdrop ?? DuelStyle.Stone;
            ground.raycastTarget = false;

            var playArea = _mobileLayout ? MobileSafeArea.Create(root) : root;
            BuildCenterBoard(playArea);
            var arrow = LotdUiKit.CreateRect("ResponseAttackArrow", playArea, Vector2.one * .5f, Vector2.one * .5f,
                Vector2.zero, Vector2.zero);
            _responseAttackArrow = arrow.gameObject.AddComponent<DuelEffectGraphic>();
            _responseAttackArrow.texture = Texture2D.whiteTexture;
            _responseAttackArrow.Kind = DuelEffectKind.AttackPath;
            _responseAttackArrow.Tint = DuelStyle.Player;
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
            Root.SetActive(false);
        }

        private void BuildLeftPanel(Transform root)
        {
            var left = CreateInspectorSurface(root, "LeftInspector", new Vector2(.015f, .195f),
                new Vector2(0.186f, 0.922f));
            var inside = LotdUiKit.FillRect("CardDetails", left, 9f);
            var card = LotdUiKit.CreateRect("CardPreview", left,
                new Vector2(.055f, .483f), new Vector2(.945f, .901f), Vector2.zero, Vector2.zero);
            _inspCard = CompositeCardView.Create(card, "Card", duelArtwork: true);
            var aspect = _inspCard.transform.Find("AspectBox");
            AddInspectorArtworkFrame(aspect);
            var zoomHit = LotdUiKit.FillRect("ZoomHit", aspect);
            var hitImage = LotdUiKit.AddImage(zoomHit.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            _inspectorArtworkButton = zoomHit.gameObject.AddComponent<Button>();
            _inspectorArtworkButton.targetGraphic = hitImage;
            _inspectorArtworkButton.transition = Selectable.Transition.None;
            _inspectorArtworkButton.navigation = new Navigation { mode = Navigation.Mode.None };
            _inspectorArtworkButton.onClick.AddListener(() =>
            {
                LotdAudio.PlayClick();
                OpenInspectedCardDetails();
            });

            var artworkControls = LotdUiKit.CreateRect("ArtworkControls", inside, new Vector2(.02f, .425f),
                new Vector2(.98f, .477f), Vector2.zero, Vector2.zero);
            var details = LotdUiKit.CreateRect("Details", artworkControls, new Vector2(.542f, 0f), Vector2.one,
                Vector2.zero, Vector2.zero);
            _duelPadInspectButton = DuelStyle.Button(details, "B", "상세 보기", OpenInspectedCardDetails, 14);
            var detailsLabel = _duelPadInspectButton.GetComponentInChildren<Text>();
            detailsLabel.resizeTextForBestFit = true;
            detailsLabel.resizeTextMinSize = 9;
            detailsLabel.resizeTextMaxSize = 14;
            var counterChip = LotdUiKit.CreateRect("Counters", artworkControls, Vector2.zero,
                new Vector2(.512f, 1f), Vector2.zero, Vector2.zero);
            _inspCounters = new DuelCounterBadge(counterChip, compact: false);

            var stats = LotdUiKit.CreateRect("Stats", inside, new Vector2(.016f, .342f), new Vector2(.825f, .419f),
                Vector2.zero, Vector2.zero);
            _inspStats = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Info", stats,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "Text", "", 14, 9, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            var combat = LotdUiKit.CreateRect("CombatStats", inside, new Vector2(.018f, .281f), new Vector2(.982f, .337f),
                Vector2.zero, Vector2.zero);
            _inspCombat = new DuelStatStrip(combat, compact: false);
            var race = LotdUiKit.CreateRect("Race", inside, new Vector2(.841f, .384f), new Vector2(.987f, .418f),
                Vector2.zero, Vector2.zero);
            _inspRaceIcon = LotdUiKit.AddImage(race.gameObject, null, Color.white, true, false);
            var attribute = LotdUiKit.CreateRect("Attribute", inside, new Vector2(.841f, .345f), new Vector2(.987f, .379f),
                Vector2.zero, Vector2.zero);
            _inspAttributeIcon = LotdUiKit.AddImage(attribute.gameObject, null, Color.white, true, false);

            var title = LotdUiKit.CreateRect("Name", inside, new Vector2(0f, .912f), Vector2.one, Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(title, new Color(.065f, .113f, .133f, .92f));
            var accent = LotdUiKit.CreateRect("TypeAccent", title, new Vector2(0f, .18f), new Vector2(0f, .82f),
                new Vector2(4f, 0f), new Vector2(6f, 0f));
            _inspTitleAccent = LotdUiKit.AddImage(accent.gameObject, LotdUiKit.WhiteSprite, DuelStyle.Muted, false, false);
            var titleContent = LotdUiKit.CreateRect("Content", title, Vector2.zero, Vector2.one,
                new Vector2(13f, 0f), new Vector2(-5f, 0f));
            _inspName = LotdUiKit.AddFittedText(titleContent, "Text", "", 19, 9,
                TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);

            var desc = LotdUiKit.CreateRect("Description", inside,
                new Vector2(.018f, .081f), new Vector2(.982f, .275f), Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(desc, new Color(.018f, .045f, .061f, .91f)).raycastTarget = true;
            var viewport = LotdUiKit.CreateRect("Viewport", desc, Vector2.zero, Vector2.one,
                new Vector2(8f, 5f), new Vector2(-14f, -5f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = LotdUiKit.CreateRect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            _inspDesc = content.gameObject.AddComponent<Text>();
            _inspDesc.font = LotdUiKit.FontKR;
            _inspDesc.fontSize = _mobileLayout ? 16 : 15;
            _inspDesc.color = LotdUiKit.TextPrimary;
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

            var log = LotdUiKit.CreateRect("RecentActions", inside, new Vector2(.023f, .011f), new Vector2(.977f, .073f),
                Vector2.zero, Vector2.zero);
            _logFeed = LotdUiKit.AddText(log, "Log", "", 11, TextAnchor.UpperLeft, DuelStyle.Muted);
            _logFeed.supportRichText = false;
            log.gameObject.AddComponent<RectMask2D>();
        }

        private void OpenInspectedCardDetails() => ShowCardZoomModal(_inspectedCode, _inspectedFieldCard);

        private static void AddInspectorArtworkFrame(Transform aspect)
        {
            var frame = LotdUiKit.FillRect("ArtworkFrame", aspect, -2.5f);
            frame.SetAsFirstSibling();
            DuelReadoutPlate.Add(frame, new Color(.30f, .27f, .22f, .97f));
        }

        private static RectTransform CreateInspectorSurface(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var shell = LotdUiKit.CreateRect(name, parent, min, max, Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(shell, new Color(.38f, .34f, .27f, .95f)).raycastTarget = true;
            var surface = LotdUiKit.FillRect("Surface", shell, 1.2f);
            DuelReadoutPlate.Add(surface, new Color(.029f, .060f, .081f, .975f));
            return shell;
        }

        private static Scrollbar CreateInspectorScrollbar(RectTransform parent)
        {
            var scrollbar = LotdUiKit.AddVerticalScrollbar(parent);
            var track = (RectTransform)scrollbar.transform;
            track.offsetMin = new Vector2(-10f, 5f);
            track.offsetMax = new Vector2(-3f, -5f);
            var rail = track.GetComponent<Image>();
            rail.sprite = LotdUiKit.WhiteSprite;
            rail.color = new Color(.084f, .16f, .20f, .85f);
            var thumb = (Image)scrollbar.targetGraphic;
            thumb.sprite = LotdUiKit.WhiteSprite;
            thumb.color = new Color(.40f, .70f, .76f, .95f);
            return scrollbar;
        }

        private void BuildTopHud(Transform root)
        {
            _oppHandBar = LotdUiKit.CreateRect("OpponentHand", root, new Vector2(.29f, .873f),
                new Vector2(.735f, .994f), Vector2.zero, Vector2.zero);
            var opponent = LotdUiKit.CreateRect("OpponentHUD", root,
                new Vector2(0.762f, 0.894f), new Vector2(0.985f, 0.982f), Vector2.zero, Vector2.zero);
            (_oppNameText, _oppLpText, _oppLpFill, _oppBustImg) = BuildPlayerHud(opponent, true);
            _oppLpFeedbackAnchor = (RectTransform)_oppNameText.transform.parent;

            var menu = LotdUiKit.CreateRect("PauseMenu", root, new Vector2(0.016f, 0.936f),
                new Vector2(0.103f, 0.982f), Vector2.zero, Vector2.zero);
            _duelPadPauseButton = BuildHudControl(menu, DuelVisualAssets.PauseIcon, "일시 정지", OnPauseClicked);
        }

        private static Button BuildHudControl(RectTransform holder, Sprite sprite, string label, Action action)
        {
            if (sprite == null)
                return DuelStyle.Button(holder, "Button", label, action, 13);

            var icon = LotdUiKit.CreateRect(label, holder, Vector2.zero, new Vector2(.45f, 1f), Vector2.zero, Vector2.zero);
            var image = LotdUiKit.AddImage(icon.gameObject, sprite, Color.white, true, false);
            var button = holder.gameObject.AddComponent<Button>();
            LotdUiKit.AddImage(holder.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            button.targetGraphic = image;
            button.onClick.AddListener(action.Invoke);
            var text = LotdUiKit.CreateRect("Label", holder, new Vector2(.43f, .10f), new Vector2(1f, .90f), Vector2.zero, Vector2.zero);
            var caption = LotdUiKit.AddFittedText(text, "Text", label, 12, 8, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            DuelStyle.Shadow(caption, 1.75f);
            return button;
        }

        private static (Text name, Text lp, RectTransform fill, Image portrait) BuildPlayerHud(RectTransform root, bool opponent)
        {
            var side = DuelStyle.Side(opponent);
            var panel = LotdUiKit.FillRect("Status", root, 1f);
            AddHudSurface(panel, HudEdge, HudSurface);

            var avatarHolder = LotdUiKit.CreateRect("AvatarFrame", panel, new Vector2(opponent ? .781f : .017f, .095f),
                new Vector2(opponent ? .983f : .219f, .905f), Vector2.zero, Vector2.zero);
            var avatar = LotdUiKit.FillRect("PortraitPlate", avatarHolder);
            var square = avatar.gameObject.AddComponent<AspectRatioFitter>();
            square.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            square.aspectRatio = 1f;
            AddHudSurface(avatar, Color.Lerp(HudEdge, side, .19f), new Color(.016f, .032f, .041f));
            var portrait = LotdUiKit.AddImage(LotdUiKit.FillRect("Portrait", avatar, 4f).gameObject, null, Color.white, true, false);

            var readout = LotdUiKit.CreateRect("Readout", panel, new Vector2(opponent ? .038f : .241f, 0f),
                new Vector2(opponent ? .759f : .962f, 1f), Vector2.zero, Vector2.zero);
            var name = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Name", readout, new Vector2(0f, .62f),
                new Vector2(1f, .932f), Vector2.zero, Vector2.zero), "Text", "", 17, 9,
                opponent ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, DuelStyle.Muted, FontStyle.Bold);
            var track = LotdUiKit.CreateRect("LPTrack", readout, new Vector2(0f, .095f), new Vector2(1f, .125f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(track.gameObject, LotdUiKit.WhiteSprite, new Color(.19f, .24f, .27f), false, false);
            var fill = LotdUiKit.FillRect("Fill", track);
            LotdUiKit.AddImage(fill.gameObject, LotdUiKit.WhiteSprite, Color.Lerp(HudEdge, side, .69f), false, false);
            LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Caption", readout, new Vector2(0f, .15f),
                new Vector2(.18f, .57f), Vector2.zero, Vector2.zero), "Label", "LP", 16, 9,
                TextAnchor.MiddleLeft, Color.Lerp(DuelStyle.Muted, side, .39f), FontStyle.Bold);
            var lp = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Value", readout, new Vector2(.18f, .139f),
                new Vector2(1f, .65f), Vector2.zero, Vector2.zero), "Text", "", 38, 12,
                TextAnchor.MiddleRight, HudText, FontStyle.Bold);
            return (name, lp, fill, portrait);
        }

        private static Image AddHudSurface(RectTransform root, Color edge, Color face)
        {
            var rim = DuelReadoutPlate.Add(root, edge);
            DuelReadoutPlate.Add(LotdUiKit.FillRect("Surface", root, 1.3f), face);
            return rim;
        }

        private void BuildCenterBoard(Transform root)
        {
            var board = LotdUiKit.CreateRect("DuelBoard", root,
                new Vector2(.145f, .221f), new Vector2(.894f, .88f), Vector2.zero, Vector2.zero);
            _boardRect = board;
            _mat = LotdUiKit.FillRect("StoneMat", board).gameObject.AddComponent<DuelMat>();
            _mat.texture = Texture2D.whiteTexture;
            _mat.raycastTarget = false;
            var surface = LotdUiKit.FillRect("SlabSurface", board).gameObject.AddComponent<DuelMatSurface>();
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
            var chain = LotdUiKit.CreateRect("Chain", board, new Vector2(0.472f, 0.486f), new Vector2(0.528f, 0.582f),
                Vector2.zero, Vector2.zero);
            _chainIcon = LotdUiKit.AddImage(chain.gameObject,
                DuelVisualAssets.ChainActive ?? LotdUiKit.LoadSprite("LOTD/HUD/chain"), Color.white, true, false);
            _chainBannerText = LotdUiKit.AddText(chain, "Count", "", 21, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _chainBannerText.gameObject.AddComponent<Outline>().effectColor = Color.black;

            _mySZone[5] = CreateZoneSlot(Cell(1, 3));
            _oppSZone[5] = CreateZoneSlot(Cell(7, 1));
            _myExtraTile = CreateStackTile(Cell(1, 4), false, YgoConstants.LOCATION_EXTRA, "엑스트라 덱");
            _myDeckTile = CreateStackTile(Cell(7, 4), false, YgoConstants.LOCATION_DECK, "덱");
            _myGyTile = CreateStackTile(Cell(7, 3), false, YgoConstants.LOCATION_GRAVE, "묘지");
            _myBanTile = CreateStackTile(Cell(7, 2), false, YgoConstants.LOCATION_REMOVED, "제외");

            _oppExtraTile = CreateStackTile(Cell(7, 0), true, YgoConstants.LOCATION_EXTRA, "엑스트라 덱");
            _oppDeckTile = CreateStackTile(Cell(1, 0), true, YgoConstants.LOCATION_DECK, "덱");
            _oppGyTile = CreateStackTile(Cell(1, 1), true, YgoConstants.LOCATION_GRAVE, "묘지");
            _oppBanTile = CreateStackTile(Cell(1, 2), true, YgoConstants.LOCATION_REMOVED, "제외");

            _extraSummonButton = BuildPileActionButton(_myExtraTile, "ExtraSummon", "소환 가능", OpenExtraSummonPicker);
            _graveEffectButton = BuildPileActionButton(_myGyTile, "GraveEffect", "효과 발동", OpenGraveEffectPicker);
        }

        private static Button BuildPileActionButton(StackTileView tile, string name, string label, Action action)
        {
            var indicator = LotdUiKit.CreateRect(name, tile.root, new Vector2(.11f, .714f),
                new Vector2(.89f, .949f), Vector2.zero, Vector2.zero);
            var rect = LotdUiKit.FillRect("Button", indicator);
            var rim = AddHudSurface(rect, DuelStyle.Gold, new Color(.23f, .175f, .087f, .99f));
            var button = AddHudButton(rim, action);
            LotdUiKit.AddFittedText(rect, "Label", label, 15, 9, TextAnchor.MiddleCenter, DuelStyle.Gold, FontStyle.Bold);
            button.gameObject.SetActive(false);
            return button;
        }

        private static readonly Color ZoneTint = new Color(.032f, .07f, .092f, .42f);
        private static readonly Vector2 SlotCardMin = new Vector2(.045f, .012f);
        private static readonly Vector2 SlotCardMax = new Vector2(.955f, .988f);

        private FieldSlotView CreateZoneSlot(RectTransform cell)
        {
            var background = LotdUiKit.AddImage(cell.gameObject, LotdUiKit.WhiteSprite, ZoneTint);
            var border = cell.gameObject.AddComponent<Outline>();
            border.effectColor = Color.clear;
            border.effectDistance = new Vector2(2f, -2f);

            var card = LotdUiKit.CreateRect("Card", cell, SlotCardMin, SlotCardMax, Vector2.zero, Vector2.zero);
            var view = CompositeCardView.Create(card, "CardView", duelArtwork: true);
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
            _fieldSlotsByRect.Add(cell, slot);
            return slot;
        }

        private StackTileView CreateStackTile(RectTransform cell, bool opponent, uint location, string name)
        {
            var background = (DuelZoneImage)LotdUiKit.AddImage(cell.gameObject, LotdUiKit.WhiteSprite, ZoneTint);

            bool grave = location == YgoConstants.LOCATION_GRAVE;
            bool usesWell = grave || location == YgoConstants.LOCATION_REMOVED;
            DuelPileWell well = null;
            Graphic wellGlyph = null;
            if (usesWell)
            {
                var basin = LotdUiKit.CreateRect("Well", cell, new Vector2(.12f, .229f), new Vector2(.88f, .985f),
                    Vector2.zero, Vector2.zero);
                well = basin.gameObject.AddComponent<DuelPileWell>();
                well.texture = Texture2D.whiteTexture;
                well.raycastTarget = false;
                well.Accent = grave ? new Color(.38f, .68f, .73f) : new Color(.67f, .55f, .81f);
                var glyph = LotdUiKit.CreateRect("Location", basin, new Vector2(.31f, .31f), new Vector2(.69f, .69f),
                    Vector2.zero, Vector2.zero);
                wellGlyph = DuelPromptLocationIcon.Add(glyph, location, opponent, glyphOnly: true);
                wellGlyph.color = Color.Lerp(DuelStyle.Muted, well.Accent, .52f);
            }
            var card = LotdUiKit.CreateRect("TopCard", cell, usesWell ? new Vector2(.19f, .322f) : new Vector2(.15f, .312f),
                usesWell ? new Vector2(.81f, .889f) : new Vector2(.85f, .949f), Vector2.zero, Vector2.zero);
            var view = CompositeCardView.Create(card, "CardView", duelArtwork: true);
            view.transform.localRotation = Quaternion.Euler(0f, 0f, opponent ? 180f : 0f);
            DuelPileDepth depth = null;
            if (!usesWell)
            {
                var layer = LotdUiKit.FillRect("StackDepth", view.transform.Find("AspectBox"));
                layer.SetAsFirstSibling();
                depth = layer.gameObject.AddComponent<DuelPileDepth>();
                depth.texture = Texture2D.whiteTexture;
                depth.Opponent = opponent;
                depth.raycastTarget = false;
            }
            view.gameObject.SetActive(false);
            var counter = LotdUiKit.CreateRect("Total", cell, new Vector2(.075f, .063f), new Vector2(.925f, .255f),
                Vector2.zero, Vector2.zero);
            Color side = DuelStyle.Side(opponent);
            AddHudSurface(counter, Color.Lerp(HudEdge, side, .24f), HudSurface);
            var nameRect = LotdUiKit.CreateRect("Caption", counter, new Vector2(.046f, .04f), new Vector2(.745f, .958f),
                Vector2.zero, Vector2.zero);
            var label = LotdUiKit.AddFittedText(nameRect, "Label", name, 14, 8, TextAnchor.MiddleLeft, DuelStyle.Muted, FontStyle.Bold);
            var amountRect = LotdUiKit.CreateRect("Amount", counter, new Vector2(.748f, .04f), new Vector2(.953f, .958f),
                Vector2.zero, Vector2.zero);
            var count = LotdUiKit.AddFittedText(amountRect, "Count", "", 17, 8, TextAnchor.MiddleRight, HudText, FontStyle.Bold);

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
            _stackTilesByRect.Add(cell, tile);
            return tile;
        }

        private void BuildBottomHudAndHands(Transform root)
        {
            _myHandBar = LotdUiKit.CreateRect("PlayerHand", root, new Vector2(.228f, .012f), new Vector2(.784f, .215f),
                Vector2.zero, Vector2.zero);
            var player = LotdUiKit.CreateRect("PlayerHUD", root, new Vector2(0.016f, 0.035f), new Vector2(0.221f, 0.132f),
                Vector2.zero, Vector2.zero);
            var (name, lp, fill, portrait) = BuildPlayerHud(player, false);
            name.text = "플레이어";
            _myLpFeedbackAnchor = (RectTransform)name.transform.parent;
            _myLpText = lp;
            _myLpFill = fill;
            portrait.sprite = LotdUiKit.LoadSprite("LOTD/Busts/yamiyugi_neutral") ?? LotdUiKit.LoadSprite("LOTD/Busts/atem_neutral");
            portrait.enabled = portrait.sprite != null;

            BuildTurnHud(root);
            BuildPhasePicker(root);
            BuildPlacementControls(root);
            var log = LotdUiKit.CreateRect("OpenLog", root, new Vector2(0.910f, 0.035f), new Vector2(0.984f, 0.092f),
                Vector2.zero, Vector2.zero);
            _duelPadLogButton = BuildHudControl(log, DuelVisualAssets.LogIcon, "대전 기록", OpenDuelLog);
            var chainConfirmation = LotdUiKit.CreateRect("ChainConfirmation", root,
                new Vector2(.825f, .035f), new Vector2(.901f, .092f), Vector2.zero, Vector2.zero);
            _chainConfirmationButton = DuelStyle.Button(chainConfirmation, "Button", "AUTO", CycleChainConfirmation, 15);
            _chainConfirmationButton.navigation = new Navigation { mode = Navigation.Mode.None };
            _chainConfirmationLabel = _chainConfirmationButton.GetComponentInChildren<Text>();
            _chainConfirmationLabel.rectTransform.anchorMax = new Vector2(1f, .66f);
            var chainCaption = LotdUiKit.CreateRect("Caption", _chainConfirmationButton.transform,
                new Vector2(.04f, .58f), new Vector2(.96f, .99f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(chainCaption, "Label", "체인 확인", 11, 8, TextAnchor.MiddleCenter, DuelStyle.Muted);
            _modals.SetChainConfirmationControl(_chainConfirmationButton);

            var hint = LotdUiKit.CreateRect("ActionHint", root, new Vector2(0.294f, 0.187f), new Vector2(0.752f, 0.225f),
                Vector2.zero, Vector2.zero);
            _hintBannerText = LotdUiKit.AddText(hint, "Text", "", 15, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);
            _hintBannerText.gameObject.AddComponent<Outline>().effectColor = LotdUiKit.BgDeep;
        }

        private void BuildTurnHud(Transform root)
        {
            var holder = LotdUiKit.CreateRect("Turn", root, new Vector2(.904f, .451f), new Vector2(.984f, .615f),
                Vector2.zero, Vector2.zero);
            var turn = LotdUiKit.CreateRect("Dial", holder, new Vector2(0f, .12f), new Vector2(1f, .88f),
                Vector2.zero, Vector2.zero);
            _phaseRim = AddHudSurface(turn, HudEdge, HudSurface);
            _phaseButton = AddHudButton(_phaseRim, OpenPhasePicker);

            _turnBadgeText = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("TurnNumber", turn, new Vector2(.08f, .675f),
                new Vector2(.92f, .935f), Vector2.zero, Vector2.zero), "Value", "", 16, 9,
                TextAnchor.MiddleCenter, DuelStyle.Muted, FontStyle.Bold);
            _phaseLabel = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("PhaseName", turn, new Vector2(.07f, .295f),
                new Vector2(.93f, .682f), Vector2.zero, Vector2.zero), "Value", "", 26, 12,
                TextAnchor.MiddleCenter, HudText, FontStyle.Bold);
            var progress = LotdUiKit.CreateRect("PhaseSteps", turn, new Vector2(.13f, .235f), new Vector2(.87f, .268f),
                Vector2.zero, Vector2.zero);
            for (int i = 0; i < _phaseSteps.Length; i++)
            {
                var step = LotdUiKit.CreateRect(PhaseEntries[i].name, progress,
                    new Vector2((i + .12f) / _phaseSteps.Length, 0f), new Vector2((i + .88f) / _phaseSteps.Length, 1f),
                    Vector2.zero, Vector2.zero);
                _phaseSteps[i] = LotdUiKit.AddImage(step.gameObject, LotdUiKit.WhiteSprite, HudEdge, false, false);
            }
            _phaseControlHint = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("ChangePrompt", turn, new Vector2(.09f, .045f),
                new Vector2(.91f, .213f), Vector2.zero, Vector2.zero), "Label", "페이즈 변경", 13, 8,
                TextAnchor.MiddleCenter, DuelStyle.Gold, FontStyle.Bold);
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
                LotdAudio.PlayClick();
                onClick();
            });
            return button;
        }

        private void BuildPhasePicker(Transform root)
        {
            var overlay = LotdUiKit.FillRect("PhasePicker", root);
            _phasePicker = overlay.gameObject;
            var dismiss = overlay.gameObject.AddComponent<Button>();
            dismiss.targetGraphic = LotdUiKit.AddImage(overlay.gameObject, LotdUiKit.WhiteSprite, new Color(.018f, .03f, .047f, .28f));
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(() => _phasePicker.SetActive(false));

            var controls = LotdUiKit.CreateRect("PhaseControls", overlay, new Vector2(.302f, .223f), new Vector2(.879f, .415f),
                Vector2.zero, Vector2.zero);
            AddHudSurface(controls, HudEdge, HudSurface);
            var header = LotdUiKit.CreateRect("Header", controls, new Vector2(.027f, .775f), new Vector2(.973f, .953f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(header, "Title", "페이즈 변경", 19, 12, TextAnchor.MiddleLeft, HudText, FontStyle.Bold);

            for (int i = 0; i < PhaseEntries.Length; i++)
            {
                var (name, label, phase, asset) = PhaseEntries[i];
                float left = .028f + .158f * i;
                var slot = LotdUiKit.CreateRect(name, controls, new Vector2(left, .297f), new Vector2(left + .150f, .737f),
                    Vector2.zero, Vector2.zero);
                var rim = AddHudSurface(LotdUiKit.FillRect("Button", slot), HudEdge, new Color(.060f, .101f, .122f));
                var button = AddHudButton(rim, () =>
                {
                    _phasePicker.SetActive(false);
                    OnPhaseShortcut(phase);
                });
                var iconBox = LotdUiKit.CreateRect("Icon", button.transform, new Vector2(.20f, .48f), new Vector2(.80f, .87f),
                    Vector2.zero, Vector2.zero);
                Sprite sprite = DuelVisualAssets.PhaseIcon(phase) ?? LotdUiKit.LoadSprite("LOTD/PhaseBar/blue_" + asset);
                var icon = LotdUiKit.AddImage(iconBox.gameObject, sprite, Color.white, true, false);
                icon.enabled = sprite != null;
                var labelBox = LotdUiKit.CreateRect("Caption", button.transform, new Vector2(.06f, .088f), new Vector2(.94f, .448f),
                    Vector2.zero, Vector2.zero);
                var caption = LotdUiKit.AddFittedText(labelBox, "Label", label, 17, 9, TextAnchor.MiddleCenter, HudText, FontStyle.Bold);
                _phaseChoices.Add((phase, button, caption, icon));
            }

            var cancel = LotdUiKit.CreateRect("Cancel", controls, new Vector2(.793f, .060f), new Vector2(.968f, .248f),
                Vector2.zero, Vector2.zero);
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
            _phaseControlHint.enabled = any;
            if (!any) _phasePicker.SetActive(false);
        }

        private void BuildPlacementControls(Transform root)
        {
            var panel = DuelStyle.Panel(root, "PlacementHint", new Vector2(.286f, .842f), new Vector2(.771f, .895f),
                fill: new Color(.033f, .083f, .135f, .94f), border: new Color(.72f, .82f, .86f, .57f));
            panel.GetComponent<Image>().raycastTarget = false;
            _placementBanner = panel.gameObject;
            _placementHint = LotdUiKit.AddFittedText(panel, "Text", "", 22, 11, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _placementBanner.SetActive(false);

            var cancel = LotdUiKit.CreateRect("PlacementCancel", root, new Vector2(.904f, .368f), new Vector2(.984f, .450f),
                Vector2.zero, Vector2.zero);
            _placementCancel = DuelStyle.RoundButton(cancel, "Button", DuelStyle.Player, () => TryCancelPlacement());
            LotdUiKit.AddFittedText(_placementCancel.transform, "Label", "취소", 17, 10, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
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
            _chainConfirmationLabel.text = mode.ToString().ToUpperInvariant();
            _chainConfirmationLabel.color = mode switch
            {
                ChainConfirmationMode.On => DuelStyle.Player,
                ChainConfirmationMode.Off => DuelStyle.Muted,
                _ => Color.white
            };
            RefreshHandGlows(force: true);
        }

        private void BuildActionPopup(Transform root)
        {
            var overlay = LotdUiKit.FillRect("CardCommands", root);
            _actionPopup = overlay.gameObject;
            var backdrop = LotdUiKit.AddImage(_actionPopup, LotdUiKit.WhiteSprite, new Color(0f, 0.015f, 0.045f, 0.23f));
            var close = _actionPopup.AddComponent<Button>();
            close.targetGraphic = backdrop;
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(CloseActionPopup);

            var middle = new Vector2(0.5f, 0.5f);
            _actionWindow = DuelStyle.Panel(overlay, "CommandWindow", middle, middle);
            var header = LotdUiKit.CreateRect("Caption", _actionWindow, new Vector2(0f, 1f), Vector2.one,
                new Vector2(27f, -43f), new Vector2(-27f, -7f));
            _actionHeader = LotdUiKit.AddFittedText(header, "CardName", "", 17, 10, TextAnchor.MiddleCenter,
                LotdUiKit.GoldAccent, FontStyle.Bold);

            var scrollArea = LotdUiKit.CreateRect("ScrollArea", _actionWindow, Vector2.zero, Vector2.one,
                new Vector2(25f, 18f), new Vector2(-25f, -45f));
            var viewport = LotdUiKit.CreateRect("Viewport", scrollArea, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-15f, 0f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = _actionWindow.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.verticalScrollbar = LotdUiKit.AddVerticalScrollbar(scrollArea);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            _actionList = LotdUiKit.CreateRect("Actions", viewport, new Vector2(0f, 1f), Vector2.one,
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

            if (!IsPresenting) _chainCardReadouts.Clear();
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
            UpdateStackTile(_myBanTile, mine.banished.Count, me, mine.banished.LastOrDefault());
            UpdateStackTile(_oppDeckTile, theirs.deckCount, opponent);
            UpdateStackTile(_oppExtraTile, theirs.extra.Count, opponent, theirs.extra.LastOrDefault());
            UpdateStackTile(_oppGyTile, theirs.grave.Count, opponent, theirs.grave.LastOrDefault());
            UpdateStackTile(_oppBanTile, theirs.banished.Count, opponent, theirs.banished.LastOrDefault());

            int chain = _session.CurrentChain.Count;
            _chainIcon.gameObject.SetActive(chain > 0);
            _chainBannerText.text = chain > 0 ? chain.ToString() : "";

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
            RefreshLinkedZones();
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
            _turnBadgeText.text = $"{(opponent ? "상대 턴" : "내 턴")} · {turn}";
            _turnBadgeText.color = Color.Lerp(HudText, DuelStyle.Side(opponent), .52f);
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
                _phaseSteps[i].color = current ? Color.Lerp(HudText, accent, .66f) : HudEdge;
                if (current) label = PhaseEntries[i].label;
            }
            _phaseLabel.text = phase is YgoConstants.PHASE_DAMAGE or YgoConstants.PHASE_DAMAGE_CAL ? "데미지" : label;
            _mat.ShowTurn(opponent);
        }

        private void UpdateStackTile(StackTileView tile, int count, byte controller, LiveCardState top = null)
        {
            tile.controller = controller;
            if (IsArriving(controller, tile.location, 0)) return;

            bool canAct = CanAcceptPlayerAction && CountZoneActions(controller, tile.location) > 0;
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
            bool heldMine = GetBattleCardDisplay(myCon, mySeq) != null;
            bool heldOpponent = GetBattleCardDisplay(oppCon, oppSeq) != null;
            if (heldOpponent || !heldMine && oppEmz != null)
                BindSlot(slot, oppCon, YgoConstants.LOCATION_MZONE, oppSeq, oppEmz, true);
            else if (!heldMine && myEmz == null && !IsPlaceSelectable(myCon, YgoConstants.LOCATION_MZONE, mySeq) && IsPlaceSelectable(oppCon, YgoConstants.LOCATION_MZONE, oppSeq))
                BindSlot(slot, oppCon, YgoConstants.LOCATION_MZONE, oppSeq, null, true);
            else
                BindSlot(slot, myCon, YgoConstants.LOCATION_MZONE, mySeq, myEmz, false);
        }

        private void BindSlot(FieldSlotView slot, byte con, uint loc, uint seq, LiveCardState live, bool opponent)
        {
            slot.mappedCon = con;
            slot.mappedLoc = loc;
            slot.mappedSeq = seq;
            var battle = loc == YgoConstants.LOCATION_MZONE ? GetBattleCardDisplay(con, seq) : null;
            slot.liveCard = battle == null ? live : null;

            var shown = battle?.Card ?? live;
            uint position = battle?.Position ?? shown?.position ?? 0;
            bool faceUp = (position & YgoConstants.POS_FACEUP) != 0;
            bool visible = battle != null ? !battle.Hidden : live != null && !IsArriving(con, loc, seq);
            var readout = visible && faceUp && shown.code != 0
                ? battle?.UseBattleStats == true ? new DuelCardReadout(CardDatabase.Get(shown.code), shown, battle.Stats)
                    : ReadFieldStats(shown.code, shown)
                : default;
            slot.cardView.gameObject.SetActive(visible);
            if (visible)
            {
                slot.cardView.SetCard(faceUp ? shown.code : 0u, !faceUp, readout.Live ?? shown);
                slot.cardView.transform.localRotation = Quaternion.Euler(0f, 0f, CardAngle(loc, position, opponent));
                slot.cardView.transform.localScale = Vector3.one * CardScale(slot.root, loc, position);
            }
            if (visible && faceUp && shown.code != 0 && battle?.HideReadout != true)
            {
                uint? scale = readout.ScaleIn(loc, seq);
                if (loc == YgoConstants.LOCATION_MZONE && readout.IsMonster || scale.HasValue || readout.CounterCount != 0)
                {
                    slot.readout ??= new DuelFieldReadout(slot.root);
                    slot.readout.Bind(readout, opponent, scale, position);
                    slot.readout.FollowFocus(slot.cardArea.anchoredPosition, slot.focusArt.localScale.x);
                }
                else slot.readout?.Hide();
            }
            else slot.readout?.Hide();

            bool interactive = battle == null && visible;
            bool canDrag = interactive && CanAcceptPlayerAction && !IsStagingPlacement && !_modals.IsFieldCardSelection &&
                !opponent && loc == YgoConstants.LOCATION_MZONE;
            slot.dragTrigger.SourceType = canDrag ? DragSourceType.FieldMonster : DragSourceType.None;
            slot.dragTrigger.CardCodeGetter = canDrag ? () => live.code : null;
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
            bool drag = !opponent && live == null && (loc == YgoConstants.LOCATION_MZONE && _dragHighlightMZone ||
                loc == YgoConstants.LOCATION_SZONE && _dragHighlightSZone);
            bool attack = opponent && loc == YgoConstants.LOCATION_MZONE && _dragHighlightAttack && interactive;
            bool action = interactive && CanAcceptPlayerAction && HasFieldAction(con, loc, seq);
            int choiceIndex = -1;
            bool cardChoice = interactive && FindFieldCardChoice(con, loc, seq, out choiceIndex);
            bool cardSelected = cardChoice && _modals.IsFieldCardSelected(choiceIndex);
            Color highlight = cardSelected ? DuelStyle.Gold : attack ? DuelStyle.Opponent :
                cardChoice || selected || action ? DuelStyle.Player :
                place ? new Color(.98f, .89f, .29f) : drag ? DuelStyle.Gold : Color.clear;
            slot.bgImg.color = highlight.a > 0f
                ? new Color(highlight.r, highlight.g, highlight.b, selected || cardSelected || attack ? .64f : place ? .09f : .32f) : ZoneTint;
            slot.border.effectColor = place ? Color.clear : highlight;
            bool reserved = !visible && loc == YgoConstants.LOCATION_MZONE &&
                CardAnimationManager.Instance?.IsSummonReserved(con, seq) == true;
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
            float uprightHeight = Mathf.Min(size.y, size.x / LotdUiKit.CardAspectRatio);
            return Mathf.Clamp01(cell.rect.width * .94f / Mathf.Max(1f, uprightHeight));
        }

        private void RefreshHand(IReadOnlyList<LiveCardState> cards, byte controller, bool opponent)
        {
            var row = opponent ? _oppHandBar : _myHandBar;
            var views = opponent ? _oppHandViews : _myHandViews;
            if (opponent) _oppHandSize = row.rect.size;
            else _myHandSize = row.rect.size;
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
                trigger.CardCodeGetter = actionable ? () => card.code : null;
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
                trigger.OnRightClick = () => InspectCard(card.code);
                trigger.OnLeftClick = () =>
                {
                    InspectCard(card.code);
                    PointAtCardActions(view.Rect);
                };
                trigger.OnTouchClick = () =>
                {
                    InspectCard(card.code);
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
            float alpha = .84f + .16f * Mathf.Sin(Time.unscaledTime * 3.2f);
            for (int i = 0; i < _myHandViews.Count; i++)
            {
                var view = _myHandViews[i];
                var glow = view.PlayableGlow;
                if (changed)
                {
                    var card = hand != null && i < hand.Count ? hand[i] : null;
                    bool playable = view.Rect.gameObject.activeSelf && card != null &&
                        IsPlayableHandCard(prompt, (byte)_session.HumanPlayerIndex, card.sequence);
                    glow.gameObject.SetActive(playable);
                }
                if (glow.gameObject.activeSelf) glow.canvasRenderer.SetAlpha(alpha);
            }
        }

        private bool IsPlayableHandCard(DuelPrompt prompt, byte controller, uint sequence)
        {
            switch (prompt.messageType)
            {
                case YgoConstants.MSG_SELECT_IDLECMD:
                case YgoConstants.MSG_SELECT_BATTLECMD:
                    return HasHandAction(controller, sequence);
                case YgoConstants.MSG_SELECT_CHAIN:
                    var mode = _app?.ChainConfirmation ?? ChainConfirmationMode.Auto;
                    return prompt.candidates.Exists(item => item.controller == controller &&
                        item.location == YgoConstants.LOCATION_HAND && item.sequence == sequence &&
                        _session.IsChainCandidateVisible(prompt, item, mode));
                case YgoConstants.MSG_SELECT_EFFECTYN:
                    return prompt.promptCon == controller && prompt.promptLoc == YgoConstants.LOCATION_HAND &&
                        prompt.promptSeq == sequence;
                default:
                    return false;
            }
        }

        private static HandCardView CreateHandCard(RectTransform row, bool opponent)
        {
            var rect = LotdUiKit.FillRect("HandCard", row);
            var card = CompositeCardView.Create(rect, "CardView", duelArtwork: true);
            var art = (RectTransform)card.transform.Find("AspectBox");
            CardDragTrigger trigger = null;
            Image glow = null;
            if (!opponent)
            {
                LotdUiKit.AddImage(rect.gameObject, LotdUiKit.WhiteSprite, Color.clear);
                var original = DuelVisualAssets.HandGlow;
                var outline = LotdUiKit.FillRect("PlayableGlow", art, original != null ? -10f : -9f);
                outline.SetAsFirstSibling();
                glow = LotdUiKit.AddImage(outline.gameObject, original ?? DuelStyle.HandGlow, Color.white, false, false);
                glow.type = Image.Type.Sliced;
                glow.fillCenter = false;
                glow.pixelsPerUnitMultiplier = original != null ? 1.7f : 1f;
                outline.gameObject.SetActive(false);
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

        private static (Vector2 min, Vector2 max, float angle) HandCardBounds(RectTransform row, int count, int index, bool opponent)
        {
            const float height = .91f;
            float width = Mathf.Clamp(row.rect.height * height * LotdUiKit.CardAspectRatio / Mathf.Max(1f, row.rect.width),
                0.045f, 0.25f);
            float spacing = Mathf.Min(width * .90f, (0.93f - width) / Mathf.Max(1, count - 1));
            float x = (1f - width - spacing * (count - 1)) * 0.5f + spacing * index;
            float fan = count <= 1 ? 0f : (2f * index / (count - 1) - 1f) * Mathf.Min(1f, (count - 1) / 4f);
            float y = .04f + (opponent ? 1f : -1f) * fan * fan * .042f;
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
            var dim = LotdUiKit.FillRect("Modal_DuelLog", root);
            _logWindow = dim.gameObject;
            LotdUiKit.AddImage(_logWindow, LotdUiKit.WhiteSprite, new Color(0f, 0.02f, 0.08f, 0.82f));
            var panel = DuelStyle.Panel(dim, "Window", new Vector2(0.20f, 0.15f), new Vector2(0.80f, 0.86f));
            var header = LotdUiKit.CreateRect("Header", panel, new Vector2(0.03f, 0.87f), new Vector2(0.97f, 0.97f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddText(header, "Title", "대전 기록", 25, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);
            var close = LotdUiKit.CreateRect("Close", header, new Vector2(0.84f, 0.12f), new Vector2(1f, 0.88f),
                Vector2.zero, Vector2.zero);
            DuelStyle.Button(close, "Button", "닫기", CloseDuelLog, 15);

            var body = DuelStyle.Panel(panel, "Log", new Vector2(0.026f, 0.035f), new Vector2(0.974f, 0.85f));
            var viewport = LotdUiKit.CreateRect("Viewport", body, Vector2.zero, Vector2.one,
                new Vector2(8f, 5f), new Vector2(-20f, -5f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = LotdUiKit.CreateRect("Content", viewport, Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
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
            _logScroll.verticalScrollbar = LotdUiKit.AddVerticalScrollbar(body);
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
                var text = LotdUiKit.AddText(_logScroll.content, "Entries", "", 17, TextAnchor.UpperLeft, LotdUiKit.TextPrimary);
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
                Refresh();
            }
        }

        private void HandleDuelCardDrop(DragContext ctx)
        {
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
                    var summons = CollectActions(me, YgoConstants.LOCATION_EXTRA, card.sequence)
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
                        LotdAudio.PlayAttack();
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
                var row = opponent ? _oppHandBar : _myHandBar;
                int count = Mathf.Max(1, handCount >= 0 ? handCount : _handCounts[controller]);
                var bounds = HandCardBounds(row, count, Mathf.Clamp((int)sequence, 0, count - 1), opponent);
                return animator.GetPose(row, (bounds.min + bounds.max) * 0.5f, bounds.max - bounds.min, bounds.angle);
            }

            var slot = GetSlot(controller, location, sequence);
            var rect = slot?.root ?? GetStack(controller, location)?.cardArea;
            if (rect == null) return default;
            var center = Vector2.one * .5f;
            var size = slot != null ? SlotCardMax - SlotCardMin : Vector2.one;
            float scale = CardScale(rect, location, cardPosition);
            if (material)
            {
                // The engine stores the material index in the position field for overlay locations.
                center += new Vector2(.035f * Math.Min(position, 3u), opponent ? .13f : -.13f);
                scale *= .60f;
            }
            return animator.GetPose(rect, center, size, angle, scale);
        }

        private CardPose GetDisplayedCardPose(byte controller, uint location, uint sequence, uint code)
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
            return CardAnimationManager.Instance.GetVisualPose(art);
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
            _materialReadouts.Clear();
            _chainCardReadouts.Clear();
            _battleCardDisplays.Clear();
            _pendingCardReadouts.Clear();
            ResetHandViews(_myHandViews);
            ResetHandViews(_oppHandViews);
        }

        private HandCardView EnsureHandView(byte controller, int index)
        {
            var views = GetHandViews(controller);
            bool opponent = controller != _session.HumanPlayerIndex;
            var row = opponent ? _oppHandBar : _myHandBar;
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

        private void ShowArrival((byte, uint, uint) key)
        {
            ReleaseArrival(key);
            Refresh();
        }

        private void ReleaseArrival((byte, uint, uint) key)
        {
            if (_arrivingCards.TryGetValue(key, out int pending))
            {
                if (pending == 1) _arrivingCards.Remove(key);
                else _arrivingCards[key] = pending - 1;
            }
        }

        private Action HideUntilArrival(byte controller, uint location, uint sequence)
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
                    Refresh();
                };
            }
            var key = ReserveArrival(controller, location, sequence);
            return () => ShowArrival(key);
        }

        private (byte, uint, uint) ReserveArrival(byte controller, uint location, uint sequence)
        {
            var key = ArrivalKey(controller, location, sequence);
            _arrivingCards.TryGetValue(key, out int pending);
            _arrivingCards[key] = pending + 1;
            return key;
        }

        private static void CompleteSpecialSummons() => CardAnimationManager.Instance?.CompleteSpecialSummons();

        private DuelCardReadout ReadFieldStats(uint code, LiveCardState live)
        {
            if (live?.IsFaceUp == true &&
                _chainCardReadouts.TryGetValue((live.controller, live.location, live.sequence), out var chain) &&
                chain.Shown.code == code)
                live = chain.Shown;
            if (live != null && live.location == YgoConstants.LOCATION_MZONE &&
                _materialReadouts.TryGetValue((live.controller, live.sequence), out var held) && held.Source.code == code)
                return new DuelCardReadout(CardDatabase.Get(code), live,
                    materialReadout: (held.Count, held.Source.attack, held.Source.defense));
            return new DuelCardReadout(CardDatabase.Get(code), live);
        }

        private void ClearMovedReadouts(CardMoveEvent move)
        {
            bool changesSlot = (move.fromCon, move.fromLoc, move.fromSeq) != (move.toCon, move.toLoc, move.toSeq);
            if (changesSlot || (move.toPos & YgoConstants.POS_FACEUP) == 0) Clear(move.fromCon, move.fromLoc, move.fromSeq);
            if (changesSlot) Clear(move.toCon, move.toLoc, move.toSeq);

            void Clear(byte controller, uint location, uint sequence)
            {
                _chainCardReadouts.Remove((controller, location & ~YgoConstants.LOCATION_OVERLAY, sequence));
                if (location == YgoConstants.LOCATION_MZONE) _materialReadouts.Remove((controller, sequence));
            }
        }

        private void QueueChainReadouts(IReadOnlyList<(LiveCardState Before, LiveCardState After)> changes)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null || !Root.activeInHierarchy) return;

            var updates = new (ChainCardReadout Readout, LiveCardState After)[changes.Count];
            for (int i = 0; i < changes.Count; i++)
            {
                var (before, after) = changes[i];
                var key = (before.controller, before.location, before.sequence);
                if (!_chainCardReadouts.TryGetValue(key, out var readout))
                {
                    readout = new ChainCardReadout { Shown = before };
                    _chainCardReadouts.Add(key, readout);
                }
                updates[i] = (readout, after);
            }
            animator.QueueChainReadoutUpdate(() =>
            {
                foreach (var (readout, after) in updates)
                    readout.Shown = after;
                Refresh();
            });
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
                if (animator?.IsSummonReserved(slot.Controller, slot.Sequence) == true) continue;
                readout.DestinationCard = _session.GetVisibleFieldCard(readout.Code, slot.Controller, YgoConstants.LOCATION_MZONE, slot.Sequence);
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

        private void HandleCardsDrawn(byte controller, IReadOnlyList<uint> cards)
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
                flights.Add(new CardFlight(cards[i], from, to, true, cards[i] == 0));
            }
            var arrivals = new Action[flights.Count];
            for (int i = 0; i < arrivals.Length; i++)
                arrivals[i] = HideUntilArrival(controller, YgoConstants.LOCATION_HAND, (uint)(first + i));
            animator.QueueDraw(flights, index => arrivals[index]());
        }

        private void HandleCardMovedEvent(CardMoveEvent e)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null || !Root.activeInHierarchy) return;
            if (e.fromLoc == e.toLoc && e.fromCon == e.toCon && e.fromSeq == e.toSeq && e.fromPos == e.toPos) return;
            if (e.fromLoc == YgoConstants.LOCATION_DECK && e.toLoc == YgoConstants.LOCATION_DECK) return;

            var from = GetDisplayedCardPose(e.fromCon, e.fromLoc, e.fromSeq, e.code);
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
            if (!from.IsValid && !to.IsValid) return;

            bool disappearing = e.toLoc == 0;
            if (!from.IsValid)
                from = new CardPose(to.Position + Vector2.up * 52f, to.Size * 0.58f, to.Angle);
            if (!to.IsValid)
                to = new CardPose(from.Position + Vector2.up * 25f, from.Size * 0.72f, from.Angle);

            bool fromBack = e.code == 0 || IsFaceDown(e.fromCon, e.fromLoc, e.fromPos);
            bool toBack = disappearing ? fromBack : e.code == 0 || IsFaceDown(e.toCon, e.toLoc, e.toPos);
            if (e.toLoc == (YgoConstants.LOCATION_EXTRA | YgoConstants.LOCATION_OVERLAY))
            {
                // The materials arrive first, while the Xyz host is still indexed in the Extra Deck.
                // Wait for its move so their destination is the selected monster zone.
                var host = (e.toCon, e.toSeq);
                if (!_pendingXyzMaterials.TryGetValue(host, out var materials))
                {
                    materials = new List<(uint, CardPose, bool, uint)>();
                    _pendingXyzMaterials.Add(host, materials);
                }
                materials.Add((e.code, from, fromBack, e.toPos));
                return;
            }
            if (e.fromLoc == YgoConstants.LOCATION_EXTRA && _pendingXyzMaterials.Remove((e.fromCon, e.fromSeq), out var pending))
            {
                uint hostLocation = e.toLoc == YgoConstants.LOCATION_MZONE ? e.toLoc : e.fromLoc;
                byte hostController = hostLocation == e.toLoc ? e.toCon : e.fromCon;
                uint hostSequence = hostLocation == e.toLoc ? e.toSeq : e.fromSeq;
                var materialFlights = new List<CardFlight>(pending.Count);
                foreach (var material in pending)
                {
                    var arrival = GetCardPose(hostController, hostLocation | YgoConstants.LOCATION_OVERLAY, hostSequence, material.index);
                    if (material.from.IsValid && arrival.IsValid)
                        materialFlights.Add(new CardFlight(material.code, material.from, arrival, material.faceDown, material.code == 0));
                }
                animator.QueueXyzMaterials(materialFlights, to.Position, to.Size);
            }

            bool attaches = (e.toLoc & YgoConstants.LOCATION_OVERLAY) != 0;
            bool detaches = (e.fromLoc & YgoConstants.LOCATION_OVERLAY) != 0;
            bool sameZone = e.fromCon == e.toCon && e.fromLoc == e.toLoc && e.fromSeq == e.toSeq;
            CardMotion motion = CardMotion.Transfer;
            if (disappearing) motion = CardMotion.Vanish;
            else if ((e.reason & YgoConstants.REASON_DESTROY) != 0) motion = CardMotion.Destroy;
            else if (attaches || detaches) motion = attaches ? CardMotion.AttachMaterial : CardMotion.DetachMaterial;
            else if (sameZone) motion = CardMotion.ChangePosition;
            else if ((e.toLoc & YgoConstants.LOCATION_ONFIELD) != 0 && toBack) motion = CardMotion.Set;
            else if (e.toLoc == YgoConstants.LOCATION_MZONE && e.fromLoc != YgoConstants.LOCATION_MZONE)
                motion = MonsterArrivalMotion(e);

            var flight = new CardFlight(e.code, from, to, fromBack, toBack);
            var held = HoldBattleMove(e, motion);
            Action detach = sameZone ? null : HoldMaterialReadout(e.fromCon, e.fromLoc, e.fromSeq, -1);
            Action attach = sameZone ? null : HoldMaterialReadout(e.toCon, e.toLoc, e.toSeq, 1);
            Action depart = held == null && detach == null ? null : () =>
            {
                if (held != null) held.Hidden = true;
                detach?.Invoke();
                Refresh();
            };
            if (disappearing || attaches)
            {
                animator.QueueMove(flight, motion, held == null && attach == null ? null : () =>
                {
                    ReleaseBattleCardDisplay(held);
                    attach?.Invoke();
                    Refresh();
                }, onDepart: depart);
                return;
            }
            if (e.summonType.HasValue && e.toLoc == YgoConstants.LOCATION_MZONE)
            {
                var key = ReserveArrival(e.toCon, e.toLoc, e.toSeq);
                animator.StageSpecialSummon(flight, motion, e.toCon, e.toSeq, PrepareMoveReadout(animator, e, fromBack, toBack),
                    depart, () => ReleaseArrival(key), Refresh);
                return;
            }
            var onArrival = HideUntilArrival(e.toCon, e.toLoc, e.toSeq);
            if (motion == CardMotion.Transfer &&
                e.fromLoc is YgoConstants.LOCATION_DECK or YgoConstants.LOCATION_EXTRA &&
                e.toLoc is YgoConstants.LOCATION_GRAVE or YgoConstants.LOCATION_REMOVED)
                animator.QueuePileTransfer(flight, onArrival);
            else
            {
                var readout = PrepareMoveReadout(animator, e, fromBack, toBack);
                animator.QueueMove(flight, motion, held == null ? onArrival : () =>
                {
                    ReleaseBattleCardDisplay(held);
                    onArrival();
                }, readout, depart);
            }
        }

        private CardMoveReadoutSnapshot PrepareMoveReadout(CardAnimationManager animator, CardMoveEvent move,
            bool fromBack, bool toBack)
        {
            if (move.toLoc != YgoConstants.LOCATION_MZONE || move.code == 0) return null;
            var destination = GetSlot(move.toCon, move.toLoc, move.toSeq);
            if (destination == null) return null;

            var source = move.fromLoc == YgoConstants.LOCATION_MZONE ? GetSlot(move.fromCon, move.fromLoc, move.fromSeq) : null;
            var sourceCard = !fromBack && source != null
                ? _session.GetVisibleFieldCard(move.code, move.fromCon, move.fromLoc, move.fromSeq) : null;
            if (sourceCard?.position != move.fromPos) sourceCard = null;
            if (toBack && sourceCard == null) return null;

            var endSize = animator.GetAreaSize(destination.root);
            var startSize = sourceCard == null ? endSize : animator.GetAreaSize(source.readout?.Rect ?? source.root);
            int viewer = _session.HumanPlayerIndex;
            var readout = new CardMoveReadoutSnapshot(move.code, sourceCard,
                (startSize, endSize, move.fromCon != viewer, move.toCon != viewer));
            if (!toBack) _pendingCardReadouts[(move.toCon, move.toSeq)] = readout;
            return readout;
        }

        internal static CardMotion MonsterArrivalMotion(CardMoveEvent move)
        {
            uint type = CardDatabase.Get(move.code)?.type ?? 0;
            if ((type & YgoConstants.TYPE_TOKEN) != 0) return CardMotion.TokenSummon;
            if (move.summonType is { } summonType)
            {
                return (summonType & YgoConstants.SUMMON_METHOD_MASK) switch
                {
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
            var animator = CardAnimationManager.Instance;
            if (animator == null) return;
            var pose = GetCardPose(link.triggerPlayer, link.triggerLoc, link.triggerSeq, YgoConstants.POS_FACEUP_ATTACK);
            animator.QueueChain(pose, link.code, link.chainNumber, link.triggerPlayer != _session.HumanPlayerIndex);
        }

        private void HandleChainResolvingEvent(ChainLinkInfo link)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null) return;
            if (_mobileLayout)
            {
                // Reserve the space between the opponent's LP and the phase control.
                var playArea = (RectTransform)_boardRect.parent;
                var area = MobileChainResolutionArea;
                var size = animator.GetAreaSize(playArea);
                float height = Mathf.Min(size.x * area.width * .88f, size.y * area.height);
                animator.QueueChainResolution(animator.GetPoint(playArea, area.center), link.code, link.chainNumber, height);
                return;
            }
            var center = animator.GetPoint(_boardRect, new Vector2(.50f, .56f));
            animator.QueueChainResolution(center, link.code, link.chainNumber);
        }

        private Vector2 AttackTarget(CardAnimationManager animator, byte attacker, byte target, uint location, uint sequence)
        {
            if (location != 0)
            {
                var pose = GetCardPose(target, location, sequence, YgoConstants.POS_FACEUP_ATTACK);
                if (pose.IsValid) return pose.Position;
            }
            var lp = attacker == _session.HumanPlayerIndex ? _oppLpText : _myLpText;
            return animator.GetPoint(lp.rectTransform, new Vector2(0.5f, 0.5f));
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

            var target = _session.TargetLoc == 0 ? _myLpText.rectTransform :
                GetSlot((byte)_session.TargetCon, _session.TargetLoc, _session.TargetSeq)?.root;
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
            var from = GetDisplayedCardPose(attacker, location, sequence, 0);
            if (!from.IsValid) from = GetCardPose(attacker, location, sequence, YgoConstants.POS_FACEUP_ATTACK);
            if (from.IsValid)
                animator.QueueAttackArrow(from.Position, AttackTarget(animator, attacker, target, targetLoc, targetSeq),
                    attacker != _session.HumanPlayerIndex);
        }

        private void HandleBattleResolved(BattleCardStats attacker, BattleCardStats defender)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null || !attacker.IsFaceUp) return;
            var card = FindLiveCard(attacker.Controller, attacker.Location, attacker.Sequence);
            if (card == null || card.code == 0) return;

            var resting = GetCardPose(attacker.Controller, attacker.Location, attacker.Sequence, attacker.Position);
            var from = GetDisplayedCardPose(attacker.Controller, attacker.Location, attacker.Sequence, card.code);
            if (!from.IsValid) from = resting;
            var hit = AttackTarget(animator, attacker.Controller, defender.Controller, defender.Location, defender.Sequence);
            var defendingCard = defender.Location == YgoConstants.LOCATION_MZONE
                ? FindLiveCard(defender.Controller, defender.Location, defender.Sequence) : null;
            var combat = new DuelCardReadout(CardDatabase.Get(card.code), card, attacker);
            var defense = defender.IsFaceUp && defendingCard?.code > 0
                ? new DuelCardReadout(CardDatabase.Get(defendingCard.code), defendingCard, defender) : default;
            int viewer = _session.HumanPlayerIndex;
            var comparisonArea = _mobileLayout ? MobileBattleStatsArea : BattleStatsArea;
            var readout = animator.CreateBattleReadout(comparisonArea, attacker, defender, viewer, combat.IsLink, defense.IsLink,
                _mobileLayout);
            var flight = new CardFlight(card.code, from, resting, false, false);
            var slot = GetSlot(attacker.Controller, attacker.Location, attacker.Sequence);
            Vector2 statSize = slot == null ? Vector2.zero : animator.GetAreaSize(slot.readout?.Rect ?? slot.root);
            var source = QueueBattleCard(attacker, card);
            var target = QueueBattleCard(defender, defendingCard);
            (CardPose pose, Vector2 size, CardPose defenderPose, RectTransform defenderStats) Start()
            {
                var pose = GetDisplayedCardPose(attacker.Controller, attacker.Location, attacker.Sequence, card.code);
                Vector2 size = slot == null ? Vector2.zero : animator.GetAreaSize(slot.readout?.Rect ?? slot.root);
                RectTransform defenderStats = null;
                if (source != null) source.Hidden = true;
                if (target != null)
                {
                    target.Position = defender.Position;
                    target.UseBattleStats = true;
                    var targetSlot = GetSlot(defender.Controller, defender.Location, defender.Sequence);
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
                var targetPose = GetDisplayedCardPose(defender.Controller, defender.Location, defender.Sequence, defendingCard?.code ?? 0);
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
            animator.QueueAttack(flight, hit, defender.Location == 0, attacker.Controller != viewer, readout, combat, statSize,
                Finish, Start);
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
            animator.QueueLifeChange(mine ? _myLpFeedbackAnchor : _oppLpFeedbackAnchor, delta, kind, ApplyLifePoints);
        }

        private void HandleTurnStarted(int turn, int player)
        {
            _pendingXyzMaterials.Clear();
            bool opponent = player != _session.HumanPlayerIndex;
            void ApplyTurn()
            {
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
            AdvanceHandLayout(Time.unscaledDeltaTime * PlaybackRate);
            AdvanceActionWindow();
            RefreshPileActionButtons();
            UpdateDuelPad();
            if (_session != session || !Root.activeSelf) return;
            RefreshHandGlows();
            RefreshLinkedZones();
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

            AdvanceLifeReadouts(Time.deltaTime * PlaybackRate);

            // Global Cancel / Dismiss via ESC or Right Mouse Button
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                if (_cardZoomModal != null && _cardZoomModal.activeSelf)
                {
                    LotdAudio.PlayClick();
                    CloseCardZoomModal();
                }
                else if (_actionPopup != null && _actionPopup.activeSelf)
                {
                    LotdAudio.PlayClick();
                    CloseActionPopup();
                }
                else if (IsBrowsingZone)
                {
                    LotdAudio.PlayClick();
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
                    LotdAudio.PlayClick();
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
                double step = Math.Max(1200d, distance * 6.5d) * playbackDelta;
                displayed = step >= distance ? target : displayed + Math.Sign(remaining) * step;
                int lifePoints = (int)Math.Round(displayed);
                if (lifePoints != before)
                {
                    LotdAudio.PlayTick();
                    WriteLifeReadout(label, fill, lifePoints);
                }
            }

            var lowLife = new Color(1f, .66f, .57f);
            label.color = displayed > 0d && displayed <= 2000d
                ? Color.Lerp(HudText, lowLife, .34f + Mathf.PingPong(Time.time * 2.1f, .42f))
                : HudText;
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

            var player = _session.Players[source.controller];
            var zones = location == YgoConstants.LOCATION_MZONE ? player.mzone : player.szone;
            uint disabled = _session.FieldDisabledMask >> (16 * source.controller + (location == YgoConstants.LOCATION_SZONE ? 8 : 0));
            bool fieldSpell = (card.type & YgoConstants.TYPE_FIELD) != 0;
            uint available = 0;
            for (int i = fieldSpell ? 5 : 0; i < (fieldSpell ? 6 : 5); i++)
                if (zones[i] == null && (disabled & (1u << i)) == 0)
                    available |= 1u << i;
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
            string desc = CardDatabase.ResolveEffectDescription(item.description, item.code);
            return desc == null ? "카드 효과 발동" : $"발동 · {desc}";
        }

        private List<(string label, int responseVal, bool gold)> CollectActions(byte con, uint loc, uint seq)
        {
            var matched = new List<(SelectableCardItem item, string label, int responseVal, bool gold)>();
            AppendMatchingActions(it => it.controller == con && it.location == loc && it.sequence == seq, matched);
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
            LotdAudio.PlayClick();
            _actionPopup.SetActive(false);
            _modals.HideAll();
            _session.SubmitResponseInt(responseVal);
            Refresh();
        }

        private static int ExtraSummonIndex(DuelPrompt prompt, LiveCardState card) =>
            card == null || card.code == 0 ? -1 :
                prompt.spSummonable.FindIndex(item => item.controller == card.controller &&
                    item.location == YgoConstants.LOCATION_EXTRA && item.sequence == card.sequence);

        private bool ExtraSummonAvailable =>
            CanUseCardActions && _session.ActivePrompt.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
            _session.Players[_session.HumanPlayerIndex].extra.Any(card => ExtraSummonIndex(_session.ActivePrompt, card) >= 0);

        private static bool HasGraveEffect(DuelPrompt prompt, LiveCardState card) =>
            card != null && card.code != 0 && card.location == YgoConstants.LOCATION_GRAVE &&
            prompt.activatable.Exists(item => item.controller == card.controller &&
                item.location == card.location && item.sequence == card.sequence);

        private bool GraveEffectAvailable =>
            CanUseCardActions && _session.ActivePrompt.activatable.Count > 0 &&
            _session.Players[_session.HumanPlayerIndex].grave.Exists(card => HasGraveEffect(_session.ActivePrompt, card));

        private void RefreshPileActionButtons()
        {
            bool canSummon = ExtraSummonAvailable;
            if (_extraSummonButton.gameObject.activeSelf != canSummon)
                _extraSummonButton.gameObject.SetActive(canSummon);
            bool canActivate = GraveEffectAvailable;
            if (_graveEffectButton.gameObject.activeSelf != canActivate)
                _graveEffectButton.gameObject.SetActive(canActivate);
        }

        private void OpenExtraSummonPicker()
        {
            if (IsBrowsingZone || !ExtraSummonAvailable) return;
            var session = _session;
            var prompt = session.ActivePrompt;
            bool CanSummon() => _session == session && ReferenceEquals(session.ActivePrompt, prompt) && CanUseCardActions;

            HideCardActions();
            _modals.ShowZoneViewer(
                "엑스트라 소환 가능",
                session.Players[session.HumanPlayerIndex].extra,
                newestFirst: false,
                hasCardActions: card => ExtraSummonIndex(prompt, card) >= 0,
                onOpenCardActions: card =>
                {
                    if (!CanSummon()) return;
                    int index = ExtraSummonIndex(prompt, card);
                    if (index >= 0) ExecuteActionInt((index << 16) | 1);
                },
                canUseCardActions: CanSummon,
                icon: DuelVisualAssets.ZoneIcon(YgoConstants.LOCATION_EXTRA, false),
                actionsOnly: true, sidePanel: true);
        }

        private void OpenGraveEffectPicker()
        {
            if (IsBrowsingZone || !GraveEffectAvailable) return;
            var session = _session;
            var prompt = session.ActivePrompt;
            bool CanActivate() => _session == session && ReferenceEquals(session.ActivePrompt, prompt) && CanUseCardActions;

            HideCardActions();
            _modals.ShowZoneViewer(
                "묘지 효과 발동 가능",
                session.Players[session.HumanPlayerIndex].grave,
                hasCardActions: card => HasGraveEffect(prompt, card),
                onOpenCardActions: card =>
                {
                    if (!CanActivate()) return;
                    int activateCommand = prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD ? 5 : 0;
                    var actions = CollectActions(card.controller, card.location, card.sequence);
                    actions.RemoveAll(action => (action.responseVal & 0xffff) != activateCommand);
                    if (actions.Count == 1) ExecuteActionInt(actions[0].responseVal);
                    else if (actions.Count > 1)
                        ShowCardActionsPopup(card.controller, card.location, card.sequence, card.code, card.overlayCodes, actions: actions);
                },
                canUseCardActions: CanActivate,
                icon: DuelVisualAssets.ZoneIcon(YgoConstants.LOCATION_GRAVE, false),
                actionsOnly: true, sidePanel: true);
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
            if (session == null || session.IsFinished || IsStagingPlacement || IsPresenting || IsPaused || IsReadingLog ||
                _cardZoomModal.activeSelf || _actionPopup.activeSelf || con >= session.Players.Length ||
                _modals.IsPromptOpen && !_modals.IsShowingField) return;
            var prompt = session.ActivePrompt;
            bool CanUseActions() => !_modals.IsPromptOpen && _session == session &&
                ReferenceEquals(session.ActivePrompt, prompt) && CanAcceptPlayerAction;

            if (loc == YgoConstants.LOCATION_DECK)
            {
                var zoneActions = CanUseActions() ? CollectZoneActions(con, loc) : null;
                if (zoneActions != null && zoneActions.Count > 0)
                {
                    LotdAudio.PlayClick();
                    ShowZoneActionsPopup(title, zoneActions);
                }
                return;
            }

            if (actionsOnly && !CanUseActions()) return;

            List<(string label, int responseVal, bool gold)> GetZoneCardActions(LiveCardState card)
            {
                var actions = CollectActions(card.controller, card.location, card.sequence);
                if (loc == YgoConstants.LOCATION_EXTRA && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                    actions.RemoveAll(action => (action.responseVal & 0xffff) == 1);
                return actions;
            }

            LotdAudio.PlayClick();
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
                actionsOnly: actionsOnly);
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

                LotdAudio.PlayClick();
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
            if (canInspect) InspectCard(live.code, GetSlot(con, loc, seq));

            if (CanShowCardActions || (touch && canInspect && CanOpenTouchCardDetails))
            {
                var anchor = GetSlot(con, loc, seq)?.root;
                if (touch) SelectTouchCardActions(anchor);
                else PointAtCardActions(anchor);
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
            Sprite iconSprite = icon == null ? null : LotdUiKit.LoadSprite($"LOTD/Icons/ICON_ID_CMDID_48_{icon}");
            var button = LotdUiKit.CreateWrappedButton(_actionList, "Action", label, action, 15, selected, 42f, iconSprite);
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
                point, player != _session.HumanPlayerIndex, LotdAudio.PlayCoin);
        }

        private void HandleDiceRolledEvent(byte player, IReadOnlyList<int> dice)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null || dice.Count == 0) return;
            var point = animator.GetPoint(_boardRect, new Vector2(0.5f, 0.54f));
            animator.QueueRandomResult("주사위   " + string.Join("   /   ", dice),
                point, player != _session.HumanPlayerIndex, LotdAudio.PlayDice);
        }

        private void BuildCardZoomModal(Transform root, bool onIPhone)
        {
            var dim = LotdUiKit.FillRect("Overlay_CardZoom", root);
            _cardZoomModal = dim.gameObject;
            LotdUiKit.AddImage(dim.gameObject, LotdUiKit.WhiteSprite, new Color(.012f, .026f, .044f, .83f));
            var backdrop = dim.gameObject.AddComponent<Button>();
            backdrop.navigation = new Navigation { mode = Navigation.Mode.None };
            backdrop.onClick.AddListener(() => CloseCardZoomModal());

            var content = onIPhone ? MobileSafeArea.Create(dim) : dim;
            var win = CreateInspectorSurface(content, "ZoomWin", onIPhone ? new Vector2(.03f, .027f) : new Vector2(.11f, .045f),
                onIPhone ? new Vector2(.97f, .973f) : new Vector2(.89f, .955f));
            win.gameObject.AddComponent<Button>().interactable = false;

            // Header bar
            var hdr = LotdUiKit.CreateRect("Hdr", win, new Vector2(.012f, .9f), new Vector2(.988f, .985f),
                Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(hdr, new Color(.065f, .113f, .133f, .92f));
            var title = LotdUiKit.CreateRect("Title", hdr, new Vector2(0.02f, 0.12f), new Vector2(0.63f, 0.88f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(title, "T", "카드 상세 정보", 18, 12,
                TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);

            var relatedRt = LotdUiKit.CreateRect("Related", hdr, new Vector2(0.66f, 0.15f), new Vector2(0.82f, 0.85f),
                Vector2.zero, Vector2.zero);
            _zoomRelatedButton = DuelStyle.Button(relatedRt, "B", "관련 카드", ShowZoomRelatedCards, 15);

            var closeRt = LotdUiKit.CreateRect("Close", hdr, new Vector2(0.84f, 0.15f), new Vector2(0.98f, 0.85f), Vector2.zero, Vector2.zero);
            _zoomCloseButton = DuelStyle.Button(closeRt, "B", onIPhone ? "닫기" : "닫기 (ESC)", () => CloseCardZoomModal(), 15);

            // Left Side: Large Card View
            float cardRight = onIPhone ? .375f : .448f;
            var cardHolder = LotdUiKit.CreateRect("CardHolder", win, new Vector2(.033f, .111f), new Vector2(cardRight, .891f), Vector2.zero, Vector2.zero);
            _zoomCardView = CompositeCardView.Create(cardHolder, "ZoomCard", true, duelArtwork: true);
            AddInspectorArtworkFrame(_zoomCardView.transform.Find("AspectBox"));
            var artwork = LotdUiKit.CreateRect("ArtworkOptions", win, new Vector2(.04f, .035f), new Vector2(cardRight, .098f),
                Vector2.zero, Vector2.zero);
            _zoomArtworkControls = artwork.gameObject;
            var previous = LotdUiKit.CreateRect("Previous", artwork, Vector2.zero, new Vector2(.19f, 1f),
                Vector2.zero, Vector2.zero);
            DuelStyle.Button(previous, "Button", "‹", () => CycleArtwork(-1), 20);
            var next = LotdUiKit.CreateRect("Next", artwork, new Vector2(.81f, 0f), Vector2.one,
                Vector2.zero, Vector2.zero);
            DuelStyle.Button(next, "Button", "›", () => CycleArtwork(1), 20);
            var counter = LotdUiKit.CreateRect("Counter", artwork, new Vector2(.20f, 0f), new Vector2(.80f, 1f),
                Vector2.zero, Vector2.zero);
            _zoomArtworkLabel = LotdUiKit.AddFittedText(counter, "Label", "", 15, 9, TextAnchor.MiddleCenter, Color.white);

            // Right Side: Card Details
            var infoRt = LotdUiKit.CreateRect("Info", win, new Vector2(cardRight + .03f, .05f), new Vector2(.96f, .88f), Vector2.zero, Vector2.zero);

            _zoomName = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Name", infoRt,
                new Vector2(0f, 0.88f), new Vector2(0.82f, 1f), Vector2.zero, Vector2.zero),
                "N", "", 22, 12, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);

            var race = LotdUiKit.CreateRect("RaceIc", infoRt, new Vector2(.833f, .88f), new Vector2(.902f, 1f), Vector2.zero, Vector2.zero);
            _zoomRaceIcon = LotdUiKit.AddImage(race.gameObject, null, Color.white, true, false);
            var attribute = LotdUiKit.CreateRect("AttributeIc", infoRt, new Vector2(.916f, .88f), new Vector2(.985f, 1f), Vector2.zero, Vector2.zero);
            _zoomAttributeIcon = LotdUiKit.AddImage(attribute.gameObject, null, Color.white, true, false);

            _zoomStats = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Stats", infoRt,
                new Vector2(0f, .735f), new Vector2(1f, .875f), Vector2.zero, Vector2.zero),
                "S", "", 16, 10, TextAnchor.MiddleLeft, DuelStyle.Muted, FontStyle.Bold);
            var combat = LotdUiKit.CreateRect("CombatStats", infoRt,
                new Vector2(0f, .635f), new Vector2(1f, .721f), Vector2.zero, Vector2.zero);
            _zoomCombat = new DuelStatStrip(combat, compact: false);

            _zoomPasscode = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Passcode", infoRt,
                new Vector2(0f, .571f), new Vector2(.60f, .626f), Vector2.zero, Vector2.zero),
                "P", "", 12, 9, TextAnchor.MiddleLeft, LotdUiKit.CyanAccent);
            var counterChip = LotdUiKit.CreateRect("Counters", infoRt,
                new Vector2(.64f, .571f), new Vector2(1f, .626f), Vector2.zero, Vector2.zero);
            _zoomCounters = new DuelCounterBadge(counterChip, compact: false);

            var descBox = LotdUiKit.CreateRect("DescBox", infoRt, Vector2.zero, new Vector2(1f, .561f),
                Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(descBox, new Color(.018f, .045f, .061f, .96f)).raycastTarget = true;
            var vp = LotdUiKit.CreateRect("VP", descBox, Vector2.zero, Vector2.one,
                new Vector2(10f, 8f), new Vector2(-19f, -8f));
            vp.gameObject.AddComponent<RectMask2D>();
            var ct = LotdUiKit.CreateRect("C", vp, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            ct.pivot = new Vector2(0.5f, 1f);
            _zoomDesc = ct.gameObject.AddComponent<Text>();
            _zoomDesc.font = LotdUiKit.FontKR;
            _zoomDesc.fontSize = onIPhone ? 23 : 17;
            _zoomDesc.color = LotdUiKit.TextPrimary;
            _zoomDesc.alignment = TextAnchor.UpperLeft;
            _zoomDesc.lineSpacing = 1.15f;
            _zoomDesc.supportRichText = false;
            _zoomDesc.raycastTarget = false;
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
            ShowCardZoomModal(card.code, InspectionLocation(GetSlot(card.controller, card.location, card.sequence)));

        private void ShowCardZoomModal(uint code, (byte controller, uint location, uint sequence)? fieldCard)
        {
            if (code == 0 || !Root.activeInHierarchy) return;
            bool opening = !_cardZoomModal.activeSelf;
            if (opening)
            {
                _focusBeforeZoom = EventSystem.current?.currentSelectedGameObject;
                if (IsBrowsingZone || _modals.IsPromptOpen) _cardZoomModal.transform.SetParent(Root.transform.parent, false);
                _cardZoomModal.transform.SetAsLastSibling();
                _zoomRelatedButton.gameObject.SetActive(!IsBrowsingZone);
                _modals.SetCardDetailsOpen(true);
                _cardZoomModal.SetActive(true);
            }
            var info = CardDatabase.Get(code);
            _zoomFieldCard = fieldCard;
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
            var live = ReadVisibleFieldCard(code, _zoomFieldCard);
            if (live == null) _zoomFieldCard = null;
            var card = ReadFieldStats(code, live);
            _zoomCardView.SetCard(code, false, card.Live, (card.Attack, card.Defense));
            _zoomStats.text = FormatInspectorStats(card);
            BindIdentityIcons(card, _zoomRaceIcon, _zoomAttributeIcon);
            _zoomCounters.Bind(card.CounterCount, showChanges);
            _zoomCombat.Bind(card, live != null && _zoomFieldCard.Value.controller != _session.HumanPlayerIndex,
                showChanges && live != null);
        }

        private void CloseCardZoomModal(bool restoreFocus = true)
        {
            if (!_cardZoomModal.activeSelf) return;
            _zoomFieldCard = null;
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
            if (_inspectedCode == code) _inspCard.SetCard(code);
            Refresh();
        }

        private void UpdateArtworkOptions(uint code, int direction)
        {
            var choices = CardArtLoader.GetDuelArtworkChoices(code);
            _zoomArtworkControls.SetActive(choices.Count > 1);
            if (choices.Count < 2) return;
            int current = CardArtLoader.GetDuelArtworkVariant(code);
            int index = 0;
            while (index < choices.Count - 1 && choices[index] != current) index++;
            index = (index + direction + choices.Count) % choices.Count;
            if (direction != 0)
            {
                CardArtLoader.SetDuelArtworkVariant(code, choices[index]);
                PlayerPrefs.Save();
            }
            _zoomArtworkLabel.text = $"일러스트 {index + 1} / {choices.Count}";
        }
    }
}
