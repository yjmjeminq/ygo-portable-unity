using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public class DuelFieldUI
    {
        public GameObject Root { get; private set; }

        private readonly GameAppController _app;
        private readonly DuelPromptModals _modals;

        private DuelSession _session;

        // Left inspector & log
        private CompositeCardView _inspCard;
        private Image _inspRaceIcon;
        private Text _inspName;
        private Text _inspStats;
        private Text _inspDesc;
        private Text _logFeed;
        private GameObject _logWindow;
        private ScrollRect _logScroll;
        private readonly List<Text> _logBlocks = new List<Text>();
        private ScrollRect _inspectScroll;
        private uint _inspectedCode;

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
        private Image _chainIcon;

        // Phase indicator and shortcuts
        private Image _phasePlate;
        private Text _phaseLabel;
        private Button _btnToBp;
        private Button _btnToMp2;
        private Button _btnToEp;

        // Life Points Animated Rolling & Low LP State
        private float _myDisplayLp = 8000f;
        private float _oppDisplayLp = 8000f;
        private int _myTargetLp = 8000;
        private int _oppTargetLp = 8000;

        // Zone slot widgets
        private class FieldSlotView
        {
            public RectTransform root;
            public Image bgImg;
            public Outline border;
            public CompositeCardView cardView;
            public RectTransform atkPill;
            public Text atkText;
            public Text badgeText;
            public CardDragTrigger dragTrigger;
            public byte mappedCon;
            public uint mappedSeq;
            public LiveCardState liveCard;
        }

        private readonly FieldSlotView[] _myMZone = new FieldSlotView[5];
        private readonly FieldSlotView[] _mySZone = new FieldSlotView[6]; // 0..4 s/t, 5 field
        private readonly FieldSlotView[] _oppMZone = new FieldSlotView[5];
        private readonly FieldSlotView[] _oppSZone = new FieldSlotView[6]; // 0..4 s/t, 5 field
        private readonly FieldSlotView[] _emz = new FieldSlotView[2]; // [0]=Human seq5 (Opp seq6), [1]=Human seq6 (Opp seq5)

        // Stack counters (Deck / Extra / GY / Banished)
        private class StackTileView
        {
            public RectTransform root;
            public Text text;
            public Outline border;
            public CompositeCardView cardView;
        }

        private StackTileView _myDeckTile, _myExtraTile, _myGyTile, _myBanTile;
        private StackTileView _oppDeckTile, _oppExtraTile, _oppGyTile, _oppBanTile;

        // High-res Card Zoom Modal
        private GameObject _cardZoomModal;
        private CompositeCardView _zoomCardView;
        private GameObject _zoomArtworkControls;
        private Text _zoomArtworkLabel;
        private Text _zoomName;
        private Text _zoomStats;
        private Text _zoomPasscode;
        private Text _zoomDesc;
        private ScrollRect _zoomScroll;
        private Image _zoomRaceIcon;

        // Multi-place selection accumulator (for MSG_SELECT_PLACE / MSG_SELECT_DISFIELD with min > 1)
        private readonly List<(byte con, uint loc, uint seq)> _pendingPlaces = new List<(byte con, uint loc, uint seq)>();
        private DuelPrompt _pendingPlacePrompt;

        // Drag-to-zone & in-flight animation state
        private (byte con, uint loc, uint seq)? _preferredDropZone;
        private int _preferredAttackTargetSeq = -1;
        private readonly Dictionary<(byte con, uint loc, uint seq), int> _arrivingCards = new Dictionary<(byte, uint, uint), int>();
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
            public Image Border;
            public CardDragTrigger Trigger;
        }

        // Context Action Popup Menu
        private GameObject _actionPopup;
        private RectTransform _actionList;
        private RectTransform _actionWindow;
        private Text _actionHeader;

        public bool IsPresenting => CardAnimationManager.Instance != null && CardAnimationManager.Instance.IsPresenting;
        public bool IsPaused { get; private set; }
        public bool IsReadingLog => _logWindow.activeSelf;
        private bool CanAcceptPlayerAction => _session != null && !_session.IsFinished && !IsPresenting && !IsPaused && !IsReadingLog &&
            _session.ActivePrompt?.playerId == _session.HumanPlayerIndex;

        public DuelFieldUI(GameAppController app, Transform canvasParent, DuelPromptModals modals)
        {
            _app = app;
            _modals = modals;
            BuildUI(canvasParent);

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
            session.OnCardsDrawn += HandleCardsDrawn;
            session.OnLifePointsChanged += HandleLifePointsChanged;
            session.OnTurnStarted += HandleTurnStarted;
            session.OnPhaseChanged += HandlePhaseChanged;
            session.OnChainActivated += HandleChainActivatedEvent;
            session.OnAttackDeclared += HandleAttackEvent;
            session.OnBattleResolved += HandleBattleResolved;
            session.OnCoinTossed += HandleCoinTossedEvent;
            session.OnDiceRolled += HandleDiceRolledEvent;

            Root.SetActive(true);
            _oppNameText.text = opponent?.name ?? opponentDeck?.title ?? "AI 듀얼리스트";
            _oppBustImg.sprite = LotdUiKit.LoadOpponentPortrait(opponent);
            _oppBustImg.enabled = _oppBustImg.sprite != null;
            _turnBadgeText.text = $"턴 {Mathf.Max(session.TurnNumber, 1)}";
            SetPhasePlate(session.TurnNumber == 0 ? YgoConstants.PHASE_DRAW : session.CurrentPhase,
                session.TurnPlayer != session.HumanPlayerIndex);
            _hintBannerText.text = "";
            _chainIcon.gameObject.SetActive(false);
            _chainBannerText.text = "";
            _myTargetLp = _oppTargetLp = 8000;
            _myDisplayLp = _oppDisplayLp = 8000f;
            _myLpText.text = _oppLpText.text = "8000";
            _myLpFill.anchorMax = _oppLpFill.anchorMax = Vector2.one;
            Canvas.ForceUpdateCanvases();
            _inspectedCode = 0;
            InspectCard(89631139);
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
                _session.OnCardsDrawn -= HandleCardsDrawn;
                _session.OnLifePointsChanged -= HandleLifePointsChanged;
                _session.OnTurnStarted -= HandleTurnStarted;
                _session.OnPhaseChanged -= HandlePhaseChanged;
                _session.OnChainActivated -= HandleChainActivatedEvent;
                _session.OnAttackDeclared -= HandleAttackEvent;
                _session.OnBattleResolved -= HandleBattleResolved;
                _session.OnCoinTossed -= HandleCoinTossedEvent;
                _session.OnDiceRolled -= HandleDiceRolledEvent;
                _session = null;
            }

            _arrivingCards.Clear();
            Array.Clear(_handCounts, 0, _handCounts.Length);
            _pendingPlaces.Clear();
            _pendingPlacePrompt = null;
            _preferredDropZone = null;
            _preferredAttackTargetSeq = -1;
            _dragHighlightMZone = _dragHighlightSZone = _dragHighlightAttack = false;
            CloseActionPopup();
            CloseDuelLog();
            _cardZoomModal.SetActive(false);
            CardAnimationManager.Instance?.Clear();
        }

        public void InspectCard(uint code)
        {
            if (code == 0 || code == _inspectedCode) return;
            _inspectedCode = code;
            _inspCard.SetCard(code);
            var info = CardDatabase.Get(code);
            if (info == null)
            {
                _inspName.text = $"카드 #{code}";
                _inspStats.text = "";
                _inspDesc.text = "";
                _inspRaceIcon.gameObject.SetActive(false);
                return;
            }
            _inspName.text = info.name;
            _inspStats.text = FormatInspectorStats(info);
            _inspDesc.text = info.desc;
            _inspectScroll.StopMovement();
            _inspectScroll.content.anchoredPosition = Vector2.zero;
            var rSp = info.IsMonster ? LotdUiKit.GetRaceSprite(info.race) : LotdUiKit.LoadSprite($"LOTD/Icons/{YgoConstants.GetSpellTrapSubSpriteName(info.type)}");
            _inspRaceIcon.sprite = rSp;
            _inspRaceIcon.gameObject.SetActive(rSp != null);
        }

        private static string FormatInspectorStats(CardInfo info)
        {
            string type = YgoConstants.GetTypeTagKR(info.type).Replace(" / ", " · ");
            if (!info.IsMonster) return type;
            YgoConstants.RaceNamesKR.TryGetValue(info.race, out string race);
            YgoConstants.AttributeNamesKR.TryGetValue(info.attr, out string attribute);
            string rating = info.IsLink ? info.DefString : $"{(info.IsXyz ? "랭크" : "레벨")} {info.level}";
            string scale = info.IsPendulum ? $"\n스케일 {info.lscale} / {info.rscale}" : "";
            string defense = info.IsLink ? "" : $"\n수비력 {info.DefString}";
            return $"{race}\n{attribute} · {rating}\n{type}{scale}\n공격력 {info.AtkString}{defense}";
        }

        private void ShowZoomRelatedCards()
        {
            uint code = _zoomCardView.CurrentCode;
            if (code == 0) return;
            var related = CardDatabase.GetRelatedCards(code);
            var codes = related.Select(card => card.code).ToArray();
            _cardZoomModal.SetActive(false);
            _modals.ShowCodeListViewer($"[{CardDatabase.GetName(code)}] 관련 카드", codes);
        }


        private void BuildUI(Transform parent)
        {
            var root = LotdUiKit.FillRect("Screen_DuelField", parent);
            Root = root.gameObject;

            var bg = LotdUiKit.FillRect("ArenaGround", root);
            var ground = bg.gameObject.AddComponent<DuelArenaGround>();
            ground.texture = DuelVisualAssets.Ground ?? DuelStyle.Stone;
            ground.raycastTarget = false;

            BuildCenterBoard(root);
            BuildLeftPanel(root);
            BuildTopHud(root);
            BuildBottomHudAndHands(root);
            BuildActionPopup(root);
            BuildCardZoomModal(root);
            BuildDuelLog(root);
            Root.SetActive(false);
        }

        private void BuildLeftPanel(Transform root)
        {
            var left = DuelStyle.Panel(root, "LeftInspector", new Vector2(0.015f, 0.195f),
                new Vector2(0.186f, 0.922f));
            var card = LotdUiKit.CreateRect("CardPreview", left,
                new Vector2(0.045f, 0.482f), new Vector2(0.552f, 0.904f), Vector2.zero, Vector2.zero);
            _inspCard = CompositeCardView.Create(card, "Card", duelArtwork: true);
            var zoomHit = LotdUiKit.FillRect("ZoomHit", card);
            var zoom = zoomHit.gameObject.AddComponent<Button>();
            zoom.targetGraphic = LotdUiKit.AddImage(zoomHit.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            zoom.onClick.AddListener(() => ShowCardZoomModal(_inspectedCode));

            var inside = LotdUiKit.FillRect("CardDetails", left, 7f);
            var stats = LotdUiKit.CreateRect("Stats", inside, new Vector2(0.56f, 0.575f), new Vector2(0.98f, 0.874f),
                Vector2.zero, Vector2.zero);
            _inspStats = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Info", stats,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "Text", "", 13, 9, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            var race = LotdUiKit.CreateRect("Race", inside, new Vector2(0.565f, 0.493f), new Vector2(0.72f, 0.57f),
                Vector2.zero, Vector2.zero);
            _inspRaceIcon = LotdUiKit.AddImage(race.gameObject, null, Color.white, true, false);

            var title = LotdUiKit.CreateRect("Name", inside, new Vector2(0f, 0.908f), Vector2.one, Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(title.gameObject, LotdUiKit.WhiteSprite, DuelStyle.HeaderColor, false, false);
            _inspName = LotdUiKit.AddFittedText(LotdUiKit.FillRect("Content", title, 5f), "Text", "", 17, 10,
                TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);

            var desc = LotdUiKit.CreateRect("Description", inside,
                new Vector2(0.018f, 0.108f), new Vector2(0.989f, 0.468f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(desc.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            var viewport = LotdUiKit.CreateRect("Viewport", desc, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-16f, 0f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = LotdUiKit.CreateRect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            _inspDesc = content.gameObject.AddComponent<Text>();
            _inspDesc.font = LotdUiKit.FontKR;
            _inspDesc.fontSize = 15;
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
            scroll.verticalScrollbar = LotdUiKit.AddVerticalScrollbar(desc);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            _inspectScroll = scroll;

            var log = LotdUiKit.CreateRect("RecentActions", inside, new Vector2(0.01f, 0.012f), new Vector2(0.99f, 0.095f),
                Vector2.zero, Vector2.zero);
            _logFeed = LotdUiKit.AddText(log, "Log", "", 11, TextAnchor.UpperLeft, DuelStyle.Muted);
            _logFeed.supportRichText = false;
            log.gameObject.AddComponent<RectMask2D>();
        }

        private void BuildTopHud(Transform root)
        {
            _oppHandBar = LotdUiKit.CreateRect("OpponentHand", root, new Vector2(0.33f, 0.895f),
                new Vector2(0.70f, 1.005f), Vector2.zero, Vector2.zero);
            var opponent = LotdUiKit.CreateRect("OpponentHUD", root,
                new Vector2(0.762f, 0.894f), new Vector2(0.985f, 0.982f), Vector2.zero, Vector2.zero);
            (_oppNameText, _oppLpText, _oppLpFill, _oppBustImg) = BuildPlayerHud(opponent, true);

            var menu = LotdUiKit.CreateRect("PauseMenu", root, new Vector2(0.020f, 0.935f),
                new Vector2(0.103f, 0.982f), Vector2.zero, Vector2.zero);
            DuelStyle.Button(menu, "Button", "일시 정지", OnPauseClicked, 14);
        }

        private static (Text name, Text lp, RectTransform fill, Image portrait) BuildPlayerHud(RectTransform root, bool opponent)
        {
            var color = DuelStyle.Side(opponent);
            var panel = DuelStyle.Panel(root, "Status", new Vector2(opponent ? 0f : 0.19f, 0.11f),
                new Vector2(opponent ? 0.81f : 1f, 0.95f));
            panel.GetComponent<Image>().raycastTarget = false;
            var stripe = LotdUiKit.CreateRect("Side", panel, new Vector2(0f, 0.93f), Vector2.one, Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(stripe.gameObject, LotdUiKit.WhiteSprite, color, false, false);
            var name = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Name", panel, new Vector2(0.045f, 0.58f),
                new Vector2(0.95f, 0.93f), Vector2.zero, Vector2.zero), "Text", "", 16, 9,
                opponent ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            var track = LotdUiKit.CreateRect("LPTrack", panel, new Vector2(0.05f, 0.07f), new Vector2(0.95f, 0.115f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(track.gameObject, LotdUiKit.WhiteSprite, new Color(.76f, .82f, .84f, .18f), false, false);
            var fill = LotdUiKit.FillRect("Fill", track);
            LotdUiKit.AddImage(fill.gameObject, LotdUiKit.WhiteSprite, color, false, false);
            LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Caption", panel, new Vector2(0.045f, 0.13f),
                new Vector2(0.26f, 0.58f), Vector2.zero, Vector2.zero), "Label", "LP", 17, 10,
                TextAnchor.MiddleLeft, DuelStyle.Muted, FontStyle.Bold);
            var lp = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Value", panel, new Vector2(0.25f, 0.12f),
                new Vector2(0.95f, 0.62f), Vector2.zero, Vector2.zero), "Text", "8000", 32, 12,
                TextAnchor.MiddleRight, Color.white, FontStyle.Bold);

            var avatar = LotdUiKit.CreateRect("AvatarFrame", root, new Vector2(opponent ? 0.80f : 0f, 0.03f),
                new Vector2(opponent ? 1f : 0.20f, 1f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(avatar.gameObject, DuelStyle.Disc, color, true, false);
            var portrait = LotdUiKit.AddImage(LotdUiKit.FillRect("Portrait", avatar, 7f).gameObject, null, Color.white, true, false);
            return (name, lp, fill, portrait);
        }

        private void BuildCenterBoard(Transform root)
        {
            var board = LotdUiKit.CreateRect("DuelBoard", root,
                new Vector2(0.145f, 0.215f), new Vector2(0.894f, 0.897f), Vector2.zero, Vector2.zero);
            _boardRect = board;
            _mat = LotdUiKit.FillRect("StoneMat", board).gameObject.AddComponent<DuelMat>();
            _mat.texture = DuelVisualAssets.Field ?? DuelStyle.Stone;
            _mat.raycastTarget = false;
            RectTransform Cell(int col, int row) => DuelZoneImage.CreateCell(board, col, row);

            for (int i = 0; i < 5; i++)
            {
                int col = 2 + i;
                int oppSeq = 4 - i;
                _myMZone[i] = CreateZoneSlot(Cell(col, 3));
                _oppMZone[oppSeq] = CreateZoneSlot(Cell(col, 1));
                _mySZone[i] = CreateZoneSlot(Cell(col, 4));
                _oppSZone[oppSeq] = CreateZoneSlot(Cell(col, 0));
            }

            _emz[0] = CreateZoneSlot(Cell(3, 2));
            _emz[1] = CreateZoneSlot(Cell(5, 2));
            var chain = LotdUiKit.CreateRect("Chain", board, new Vector2(0.472f, 0.486f), new Vector2(0.528f, 0.582f),
                Vector2.zero, Vector2.zero);
            _chainIcon = LotdUiKit.AddImage(chain.gameObject, LotdUiKit.LoadSprite("LOTD/HUD/chain"), Color.white, true, false);
            _chainBannerText = LotdUiKit.AddText(chain, "Count", "", 21, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _chainBannerText.gameObject.AddComponent<Outline>().effectColor = Color.black;

            _mySZone[5] = CreateZoneSlot(Cell(1, 3));
            _oppSZone[5] = CreateZoneSlot(Cell(7, 1));
            _myExtraTile = CreateStackTile(Cell(1, 4), false, YgoConstants.LOCATION_EXTRA, "엑스트라 덱");
            _myDeckTile = CreateStackTile(Cell(7, 4), false, YgoConstants.LOCATION_DECK, "덱");
            _myGyTile = CreateStackTile(Cell(7, 3), false, YgoConstants.LOCATION_GRAVE, "묘지");
            _myBanTile = CreateStackTile(Cell(8, 3), false, YgoConstants.LOCATION_REMOVED, "제외");

            _oppExtraTile = CreateStackTile(Cell(7, 0), true, YgoConstants.LOCATION_EXTRA, "엑스트라 덱");
            _oppDeckTile = CreateStackTile(Cell(1, 0), true, YgoConstants.LOCATION_DECK, "덱");
            _oppGyTile = CreateStackTile(Cell(1, 1), true, YgoConstants.LOCATION_GRAVE, "묘지");
            _oppBanTile = CreateStackTile(Cell(0, 1), true, YgoConstants.LOCATION_REMOVED, "제외");
        }

        private static readonly Color ZoneTint = new Color(0.28f, 0.31f, 0.24f, 0.33f);
        private static readonly Vector2 SlotCardMin = new Vector2(0.10f, 0.07f);
        private static readonly Vector2 SlotCardMax = new Vector2(0.90f, 0.98f);

        private FieldSlotView CreateZoneSlot(RectTransform cell)
        {
            var background = LotdUiKit.AddImage(cell.gameObject, LotdUiKit.WhiteSprite, ZoneTint);
            var border = cell.gameObject.AddComponent<Outline>();
            border.effectColor = Color.clear;
            border.effectDistance = new Vector2(2f, -2f);

            var card = LotdUiKit.CreateRect("Card", cell, SlotCardMin, SlotCardMax, Vector2.zero, Vector2.zero);
            var view = CompositeCardView.Create(card, "CardView", duelArtwork: true);
            view.gameObject.SetActive(false);

            var stats = LotdUiKit.CreateRect("Stats", cell, new Vector2(0.125f, 0.01f), new Vector2(0.875f, 0.26f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(stats.gameObject, LotdUiKit.WhiteSprite, new Color(0.04f, 0.08f, 0.08f, 0.82f), false, false);
            var values = LotdUiKit.AddFittedText(LotdUiKit.FillRect("Content", stats, 2f), "Value", "", 14, 8,
                TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            values.gameObject.AddComponent<Outline>().effectColor = new Color(0.025f, 0.025f, 0.06f);
            stats.gameObject.SetActive(false);

            var badge = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Materials", cell, new Vector2(0.64f, 0.74f),
                new Vector2(0.99f, 0.97f), Vector2.zero, Vector2.zero), "Count", "", 12, 8,
                TextAnchor.MiddleCenter, DuelStyle.Gold, FontStyle.Bold);
            DuelStyle.Shadow(badge, 1.5f);

            return new FieldSlotView
            {
                root = cell,
                bgImg = background,
                border = border,
                cardView = view,
                atkPill = stats,
                atkText = values,
                badgeText = badge,
                dragTrigger = cell.gameObject.AddComponent<CardDragTrigger>(),
            };
        }

        private StackTileView CreateStackTile(RectTransform cell, bool opponent, uint location, string name)
        {
            var background = LotdUiKit.AddImage(cell.gameObject, LotdUiKit.WhiteSprite, ZoneTint);
            var border = cell.gameObject.AddComponent<Outline>();
            border.effectColor = Color.clear;
            border.effectDistance = new Vector2(2f, -2f);

            if (location == YgoConstants.LOCATION_GRAVE || location == YgoConstants.LOCATION_REMOVED)
            {
                var rim = LotdUiKit.FillRect("Well", cell, 10f);
                LotdUiKit.AddImage(rim.gameObject, DuelStyle.Disc,
                    location == YgoConstants.LOCATION_GRAVE ? new Color(.13f, .28f, .37f, .9f) : new Color(.30f, .17f, .32f, .9f),
                    true, false);
            }
            var card = LotdUiKit.CreateRect("TopCard", cell, SlotCardMin, SlotCardMax, Vector2.zero, Vector2.zero);
            var view = CompositeCardView.Create(card, "CardView", duelArtwork: true);
            view.gameObject.SetActive(false);
            var count = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Total", cell, new Vector2(0.27f, 0.06f),
                new Vector2(0.73f, 0.32f), Vector2.zero, Vector2.zero), "Count", "", 23, 11,
                TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            DuelStyle.Shadow(count, 2f);

            void Open()
            {
                if (_session == null || IsPresenting || IsPaused) return;
                byte player = (byte)(opponent ? _session.AiPlayerIndex : _session.HumanPlayerIndex);
                LotdAudio.PlayClick();
                OpenStackZone(player, location, (opponent ? "상대 " : "내 ") + name, opponent && location == YgoConstants.LOCATION_REMOVED);
            }

            var button = cell.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(Open);
            var trigger = cell.gameObject.AddComponent<CardDragTrigger>();
            trigger.OnRightClick = Open;
            return new StackTileView { root = cell, text = count, border = border, cardView = view };
        }

        private void BuildBottomHudAndHands(Transform root)
        {
            _myHandBar = LotdUiKit.CreateRect("PlayerHand", root, new Vector2(0.245f, 0.018f), new Vector2(0.760f, 0.199f),
                Vector2.zero, Vector2.zero);
            var player = LotdUiKit.CreateRect("PlayerHUD", root, new Vector2(0.016f, 0.035f), new Vector2(0.221f, 0.132f),
                Vector2.zero, Vector2.zero);
            var (name, lp, fill, portrait) = BuildPlayerHud(player, false);
            name.text = "플레이어";
            _myLpText = lp;
            _myLpFill = fill;
            portrait.sprite = LotdUiKit.LoadSprite("LOTD/Busts/yamiyugi_neutral") ?? LotdUiKit.LoadSprite("LOTD/Busts/atem_neutral");
            portrait.enabled = portrait.sprite != null;

            var turn = LotdUiKit.CreateRect("Turn", root, new Vector2(0.883f, 0.455f), new Vector2(0.981f, 0.621f),
                Vector2.zero, Vector2.zero);
            _phasePlate = LotdUiKit.AddImage(turn.gameObject, DuelStyle.Disc, Color.white, true, false);
            _turnBadgeText = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("TurnNumber", turn, new Vector2(0.10f, 0.51f),
                new Vector2(0.90f, 0.70f), Vector2.zero, Vector2.zero), "Value", "", 15, 9,
                TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _phaseLabel = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("PhaseName", turn, new Vector2(0.10f, 0.30f),
                new Vector2(0.90f, 0.52f), Vector2.zero, Vector2.zero), "Value", "", 20, 10,
                TextAnchor.MiddleCenter, DuelStyle.Gold, FontStyle.Bold);
            DuelStyle.Shadow(_turnBadgeText);
            DuelStyle.Shadow(_phaseLabel);

            var controls = LotdUiKit.CreateRect("PhaseControls", root, new Vector2(0.766f, 0.024f), new Vector2(0.984f, 0.086f),
                Vector2.zero, Vector2.zero);
            Button PhaseButton(string name, string label, int phase, float x, bool gold = false)
            {
                var slot = LotdUiKit.CreateRect(name, controls, new Vector2(x, 0f), new Vector2(x + 0.325f, 1f),
                    Vector2.zero, Vector2.zero);
                return DuelStyle.Button(slot, "Button", label, () => OnPhaseShortcut(phase), 14, gold);
            }
            _btnToBp = PhaseButton("Battle", "배틀", YgoConstants.PHASE_BATTLE, 0f);
            _btnToMp2 = PhaseButton("Main2", "메인 2", YgoConstants.PHASE_MAIN2, 0.337f);
            _btnToEp = PhaseButton("End", "턴 종료", YgoConstants.PHASE_END, 0.675f, true);
            var log = LotdUiKit.CreateRect("OpenLog", root, new Vector2(0.914f, 0.098f), new Vector2(0.984f, 0.153f),
                Vector2.zero, Vector2.zero);
            DuelStyle.Button(log, "Button", "대전 기록", OpenDuelLog, 13);

            var hint = LotdUiKit.CreateRect("ActionHint", root, new Vector2(0.294f, 0.187f), new Vector2(0.752f, 0.225f),
                Vector2.zero, Vector2.zero);
            _hintBannerText = LotdUiKit.AddText(hint, "Text", "", 15, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);
            _hintBannerText.gameObject.AddComponent<Outline>().effectColor = LotdUiKit.BgDeep;
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
            if (!CanAcceptPlayerAction || _actionPopup.activeSelf) return false;
            var p = _session.ActivePrompt;
            if (p == null || p.playerId != _session.HumanPlayerIndex) return false;

            if (_preferredDropZone.HasValue &&
                (p.messageType == YgoConstants.MSG_SELECT_PLACE || p.messageType == YgoConstants.MSG_SELECT_DISFIELD) &&
                p.min <= 1)
            {
                var target = _preferredDropZone.Value;
                _preferredDropZone = null;
                if (IsPlaceSelectable(target.con, target.loc, target.seq))
                {
                    byte[] resp = { target.con, (byte)target.loc, (byte)target.seq };
                    _pendingPlaces.Clear();
                    _actionPopup.SetActive(false);
                    _session.SubmitResponse(resp);
                    return true;
                }
            }

            if (_preferredAttackTargetSeq >= 0 &&
                p.messageType == YgoConstants.MSG_SELECT_CARD &&
                p.min == 1 && p.max == 1)
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
                        byte[] resp = new byte[12];
                        Buffer.BlockCopy(BitConverter.GetBytes(0), 0, resp, 0, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(1), 0, resp, 4, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(i), 0, resp, 8, 4);
                        _session.SubmitResponse(resp);
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

            var prompt = _session.ActivePrompt;
            if (!ReferenceEquals(prompt, _pendingPlacePrompt))
            {
                _pendingPlacePrompt = prompt;
                _pendingPlaces.Clear();
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

            bool waiting = CanAcceptPlayerAction;
            string hint = "";
            if (waiting && (prompt.messageType == YgoConstants.MSG_SELECT_PLACE ||
                prompt.messageType == YgoConstants.MSG_SELECT_DISFIELD))
            {
                int needed = Mathf.Max(1, (int)prompt.min);
                hint = needed > 1 ? $"존 선택  {_pendingPlaces.Count} / {needed}" : "카드를 놓을 존을 선택하세요.";
            }
            _hintBannerText.text = hint;
            bool idle = waiting && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD;
            bool battle = waiting && prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD;
            _btnToBp.interactable = idle && prompt.canBp;
            _btnToMp2.interactable = battle && prompt.canM2;
            _btnToEp.interactable = (idle || battle) && prompt.canEp;

            UpdateStackTile(_myDeckTile, mine.deckCount, me, YgoConstants.LOCATION_DECK);
            UpdateStackTile(_myExtraTile, mine.extra.Count, me, YgoConstants.LOCATION_EXTRA);
            UpdateStackTile(_myGyTile, mine.grave.Count, me, YgoConstants.LOCATION_GRAVE, mine.grave.LastOrDefault());
            UpdateStackTile(_myBanTile, mine.banished.Count, me, YgoConstants.LOCATION_REMOVED, mine.banished.LastOrDefault());
            UpdateStackTile(_oppDeckTile, theirs.deckCount, opponent, YgoConstants.LOCATION_DECK);
            UpdateStackTile(_oppExtraTile, theirs.extra.Count, opponent, YgoConstants.LOCATION_EXTRA);
            UpdateStackTile(_oppGyTile, theirs.grave.Count, opponent, YgoConstants.LOCATION_GRAVE, theirs.grave.LastOrDefault());
            UpdateStackTile(_oppBanTile, theirs.banished.Count, opponent, YgoConstants.LOCATION_REMOVED, theirs.banished.LastOrDefault());

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

            RefreshHand(mine.hand, me, false);
            RefreshHand(theirs.hand, opponent, true);
            RefreshLogFeed();
        }

        private void SetPhasePlate(int phase, bool opponent)
        {
            Color accent = DuelStyle.Side(opponent);
            _phasePlate.color = new Color(accent.r * .58f, accent.g * .58f, accent.b * .58f, .98f);
            _phaseLabel.text = YgoConstants.GetPhaseNameKR(phase);
            _phaseLabel.color = DuelStyle.Gold;
            _mat.ShowTurn(opponent);
        }

        private void UpdateStackTile(StackTileView tile, int count, byte controller, uint location, LiveCardState top = null)
        {
            if (IsArriving(controller, location, 0)) return;

            bool canAct = CanAcceptPlayerAction && CountZoneActions(controller, location) > 0;
            tile.text.text = count > 0 ? count.ToString() : "";
            tile.text.color = canAct ? LotdUiKit.GoldAccent : Color.white;
            tile.border.effectColor = canAct ? LotdUiKit.GoldAccent : Color.clear;
            tile.cardView.gameObject.SetActive(count > 0);
            if (count <= 0) return;

            bool showBack = top == null || location == YgoConstants.LOCATION_REMOVED && !top.IsFaceUp;
            tile.cardView.SetCard(showBack ? 0u : top.code, showBack, top);
            tile.cardView.transform.localRotation = Quaternion.Euler(0f, 0f,
                CardAngle(location, YgoConstants.POS_FACEUP_ATTACK, controller != _session.HumanPlayerIndex));
        }

        private void BindEmz(FieldSlotView slot, byte myCon, uint mySeq, byte oppCon, uint oppSeq, LiveCardState myEmz, LiveCardState oppEmz)
        {
            if (oppEmz != null)
                BindSlot(slot, oppCon, YgoConstants.LOCATION_MZONE, oppSeq, oppEmz, true);
            else if (myEmz == null && !IsPlaceSelectable(myCon, YgoConstants.LOCATION_MZONE, mySeq) && IsPlaceSelectable(oppCon, YgoConstants.LOCATION_MZONE, oppSeq))
                BindSlot(slot, oppCon, YgoConstants.LOCATION_MZONE, oppSeq, null, true);
            else
                BindSlot(slot, myCon, YgoConstants.LOCATION_MZONE, mySeq, myEmz, false);
        }

        private void BindSlot(FieldSlotView slot, byte con, uint loc, uint seq, LiveCardState live, bool opponent)
        {
            slot.mappedCon = con;
            slot.mappedSeq = seq;
            slot.liveCard = live;

            bool visible = live != null && !IsArriving(con, loc, seq);
            slot.cardView.gameObject.SetActive(visible);
            bool showStats = visible && loc == YgoConstants.LOCATION_MZONE && live.IsFaceUp;
            slot.atkPill.gameObject.SetActive(showStats);
            bool token = showStats && (live.type & YgoConstants.TYPE_TOKEN) != 0;
            slot.badgeText.text = token ? "토큰" : visible && live.overlayCodes.Count > 0 ? $"×{live.overlayCodes.Count}" : "";
            slot.badgeText.color = token ? DuelStyle.Player : DuelStyle.Gold;
            if (visible)
            {
                slot.cardView.SetCard(live.IsFaceUp ? live.code : 0u, !live.IsFaceUp, live);
                slot.cardView.transform.localRotation = Quaternion.Euler(0f, 0f, CardAngle(loc, live.position, opponent));
                slot.cardView.transform.localScale = Vector3.one * CardScale(loc, live.position);
            }
            if (showStats)
            {
                bool defense = live.IsDefense;
                int stat = defense ? live.defense : live.attack;
                int baseStat = defense ? live.baseDefense : live.baseAttack;
                slot.atkText.text = $"{(defense ? "DEF" : "ATK")} {(stat < 0 ? "?" : stat.ToString())}";
                slot.atkText.color = stat > baseStat ? LotdUiKit.CyanAccent : stat < baseStat ? LotdUiKit.RedAccent : Color.white;
            }

            bool canDrag = visible && CanAcceptPlayerAction && !opponent && loc == YgoConstants.LOCATION_MZONE;
            slot.dragTrigger.SourceType = canDrag ? DragSourceType.FieldMonster : DragSourceType.None;
            slot.dragTrigger.CardCodeGetter = canDrag ? () => live.code : null;
            slot.dragTrigger.IndexGetter = canDrag ? () => (int)seq : null;
            slot.dragTrigger.DataGetter = canDrag ? () => (con, loc, seq) : null;
            void Inspect()
            {
                if (visible && (live.IsFaceUp || live.isPublic || !opponent)) InspectCard(live.code);
            }
            slot.dragTrigger.OnPointerEnterAction = Inspect;
            slot.dragTrigger.OnRightClick = Inspect;
            slot.dragTrigger.OnLeftClick = () => OnSlotClicked(con, loc, seq);

            bool selected = IsPlacePending(con, loc, seq);
            bool place = IsPlaceSelectable(con, loc, seq);
            bool drag = !opponent && live == null && (loc == YgoConstants.LOCATION_MZONE && _dragHighlightMZone ||
                loc == YgoConstants.LOCATION_SZONE && _dragHighlightSZone);
            bool attack = opponent && loc == YgoConstants.LOCATION_MZONE && _dragHighlightAttack && live != null;
            bool action = visible && CanAcceptPlayerAction && HasFieldAction(con, loc, seq);
            Color highlight = attack ? DuelStyle.Opponent : selected || action ? DuelStyle.Player :
                place || drag ? DuelStyle.Gold : Color.clear;
            slot.bgImg.color = highlight.a > 0f
                ? new Color(highlight.r, highlight.g, highlight.b, selected || attack ? .64f : .32f) : ZoneTint;
            slot.border.effectColor = highlight;
        }

        private static float CardAngle(uint location, uint position, bool opponent)
        {
            if (location == YgoConstants.LOCATION_MZONE && (position & YgoConstants.POS_DEFENSE) != 0)
                return opponent ? 90f : -90f;
            if (location == YgoConstants.LOCATION_DECK)
                return opponent ? 166f : -14f;
            if (location == YgoConstants.LOCATION_EXTRA)
                return opponent ? 193f : 13f;
            return opponent && location != YgoConstants.LOCATION_HAND ? 180f : 0f;
        }

        private static float CardScale(uint location, uint position)
        {
            return location == YgoConstants.LOCATION_MZONE && (position & YgoConstants.POS_DEFENSE) != 0 ? 0.87f : 1f;
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
                bool visible = i < cards.Count && cards[i] != null && !IsArriving(controller, YgoConstants.LOCATION_HAND, (uint)i);
                view.Rect.gameObject.SetActive(visible);
                if (!visible) continue;

                var bounds = HandCardBounds(row, cards.Count, i, opponent);
                if (view.Rect.anchorMin != bounds.min || view.Rect.anchorMax != bounds.max)
                {
                    view.Rect.anchorMin = bounds.min;
                    view.Rect.anchorMax = bounds.max;
                }
                view.Rect.localRotation = Quaternion.Euler(0f, 0f, bounds.angle);

                var card = cards[i];
                uint sequence = (uint)i;
                bool showFront = !opponent || card.isPublic || card.IsFaceUp;
                view.Card.SetCard(showFront ? card.code : 0u, !showFront, card);
                bool actionable = !opponent && CanAcceptPlayerAction && HasHandAction(controller, sequence);
                view.Border.color = actionable ? new Color(1f, 0.73f, 0.13f, 0.85f) : Color.clear;

                var trigger = view.Trigger;
                if (trigger == null) continue;
                trigger.SourceType = actionable ? DragSourceType.Hand : DragSourceType.None;
                trigger.CardCodeGetter = actionable ? () => card.code : null;
                trigger.IndexGetter = actionable ? () => (int)sequence : null;
                trigger.OnPointerEnterAction = () => InspectCard(card.code);
                trigger.OnRightClick = () => InspectCard(card.code);
                trigger.OnLeftClick = () =>
                {
                    InspectCard(card.code);
                    ShowCardActionsPopup(controller, YgoConstants.LOCATION_HAND, sequence, card.code, card.overlayCodes);
                };
            }
        }

        private static HandCardView CreateHandCard(RectTransform row, bool opponent)
        {
            var rect = LotdUiKit.FillRect("HandCard", row);
            var border = LotdUiKit.AddImage(rect.gameObject, LotdUiKit.WhiteSprite, Color.clear, false, !opponent);
            var card = CompositeCardView.Create(rect, "CardView", duelArtwork: true);
            CardDragTrigger trigger = null;
            if (!opponent)
            {
                trigger = rect.gameObject.AddComponent<CardDragTrigger>();
                trigger.EnableHoverLift = true;
            }
            return new HandCardView { Rect = rect, Card = card, Border = border, Trigger = trigger };
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
            if (!Root.activeInHierarchy || !CanAcceptPlayerAction) return;
            InspectCard(cardCode);

            _dragHighlightMZone = false;
            _dragHighlightSZone = false;
            _dragHighlightAttack = false;

            var p = _session.ActivePrompt;
            if (p == null || p.playerId != _session.HumanPlayerIndex) return;

            byte me = (byte)_session.HumanPlayerIndex;
            if (source == DragSourceType.Hand)
            {
                var actions = CollectActions(me, YgoConstants.LOCATION_HAND, (uint)sourceIndex);
                foreach (var act in actions)
                {
                    if (act.label.Contains("소환") || act.label.Contains("몬스터 세트")) _dragHighlightMZone = true;
                    if (act.label.Contains("발동") || act.label.Contains("마법/함정 세트")) _dragHighlightSZone = true;
                }
            }
            else if (source == DragSourceType.ExtraDeck)
            {
                _dragHighlightMZone = true;
                if (_modals != null && _modals.ZoneViewRoot != null && _modals.ZoneViewRoot.activeSelf)
                {
                    _modals.ZoneViewRoot.SetActive(false);
                }
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

            if (!Root.activeInHierarchy || !CanAcceptPlayerAction) return;
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
                uint exSeq = (uint)ctx.SourceIndex;
                for (int i = 0; i < 5; i++)
                {
                    if (CardDragManager.ContainsScreenPoint(_myMZone[i].root, ctx.DropPosition))
                    {
                        _preferredDropZone = (me, YgoConstants.LOCATION_MZONE, (uint)i);
                        break;
                    }
                }
                for (int i = 0; i < 2; i++)
                {
                    if (CardDragManager.ContainsScreenPoint(_emz[i].root, ctx.DropPosition))
                    {
                        _preferredDropZone = (me, YgoConstants.LOCATION_MZONE, (uint)(i == 0 ? 5 : 6));
                        break;
                    }
                }

                var exActions = CollectActions(me, YgoConstants.LOCATION_EXTRA, exSeq);
                if (exActions.Count == 1)
                {
                    ExecuteActionInt(exActions[0].responseVal);
                    return;
                }
                else if (exActions.Count > 1)
                {
                    PopulateAndShowActionPopup(CardDatabase.GetName(ctx.CardCode), exActions, null);
                    return;
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
                PopulateAndShowActionPopup(CardDatabase.GetName(cardCode), actions, null);
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

            // Remember the target zone so if ygopro-core asks MSG_SELECT_PLACE next, we place it here automatically
            _preferredDropZone = (con, loc, seq);

            // In Main Phase (MSG_SELECT_IDLECMD) or Battle Phase (MSG_SELECT_BATTLECMD)
            var actions = CollectActions((byte)_session.HumanPlayerIndex, YgoConstants.LOCATION_HAND, handSeq);
            if (actions.Count == 0)
            {
                _preferredDropZone = null;
                Refresh();
                return;
            }

            if (loc == YgoConstants.LOCATION_MZONE)
            {
                // Filter monster actions (Summon, SpSummon, Set)
                var mActions = actions.Where(a => a.label.Contains("소환") || a.label.Contains("세트")).ToList();
                if (mActions.Count == 1)
                {
                    ExecuteActionInt(mActions[0].responseVal);
                    return;
                }
                else if (mActions.Count > 1)
                {
                    PopulateAndShowActionPopup(CardDatabase.GetName(cardCode), mActions, null);
                    return;
                }
            }
            else if (loc == YgoConstants.LOCATION_SZONE)
            {
                // Filter spell/trap actions (Activate, Set)
                var sActions = actions.Where(a => a.label.Contains("발동") || a.label.Contains("세트")).ToList();
                if (sActions.Count == 1)
                {
                    ExecuteActionInt(sActions[0].responseVal);
                    return;
                }
                else if (sActions.Count > 1)
                {
                    PopulateAndShowActionPopup(CardDatabase.GetName(cardCode), sActions, null);
                    return;
                }
            }

            if (actions.Count == 1)
            {
                ExecuteActionInt(actions[0].responseVal);
                return;
            }

            // Fallback: show all available actions
            PopulateAndShowActionPopup(CardDatabase.GetName(cardCode), actions, null);
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
            location &= ~YgoConstants.LOCATION_OVERLAY;
            if (animator == null || controller >= _handCounts.Length) return default;

            bool opponent = controller != _session.HumanPlayerIndex;
            float angle = CardAngle(location, position, opponent);
            if (location == YgoConstants.LOCATION_HAND)
            {
                var row = opponent ? _oppHandBar : _myHandBar;
                int count = Mathf.Max(1, handCount >= 0 ? handCount : _handCounts[controller]);
                var bounds = HandCardBounds(row, count, Mathf.Clamp((int)sequence, 0, count - 1), opponent);
                return animator.GetPose(row, (bounds.min + bounds.max) * 0.5f, bounds.max - bounds.min, bounds.angle);
            }

            var rect = GetSlot(controller, location, sequence)?.root ?? GetStack(controller, location)?.root;
            return rect == null ? default : animator.GetPose(rect, (SlotCardMin + SlotCardMax) * 0.5f,
                SlotCardMax - SlotCardMin, angle, CardScale(location, position));
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
            bool indexed = (location & (YgoConstants.LOCATION_HAND | YgoConstants.LOCATION_ONFIELD)) != 0;
            return (controller, location, indexed ? sequence : 0);
        }

        private bool IsArriving(byte controller, uint location, uint sequence)
        {
            return _arrivingCards.ContainsKey(ArrivalKey(controller, location, sequence));
        }

        private void ShowArrival((byte, uint, uint) key)
        {
            if (_arrivingCards.TryGetValue(key, out int pending))
            {
                if (pending == 1) _arrivingCards.Remove(key);
                else _arrivingCards[key] = pending - 1;
            }
            Refresh();
        }

        private (byte, uint, uint) HideUntilArrival(byte controller, uint location, uint sequence)
        {
            var key = ArrivalKey(controller, location, sequence);
            _arrivingCards.TryGetValue(key, out int pending);
            _arrivingCards[key] = pending + 1;
            return key;
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
                bool privateDraw = controller != _session.HumanPlayerIndex || cards[i] == 0;
                flights.Add(new CardFlight(privateDraw ? 0u : cards[i], from, to, true, privateDraw));
            }
            var keys = new (byte, uint, uint)[flights.Count];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = HideUntilArrival(controller, YgoConstants.LOCATION_HAND, (uint)(first + i));
            animator.QueueDraw(flights, index => ShowArrival(keys[index]));
        }

        private void HandleCardMovedEvent(CardMoveEvent e)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null || !Root.activeInHierarchy) return;
            if (e.fromLoc == e.toLoc && e.fromCon == e.toCon && e.fromSeq == e.toSeq && e.fromPos == e.toPos) return;
            if (e.fromLoc == YgoConstants.LOCATION_DECK && e.toLoc == YgoConstants.LOCATION_DECK) return;

            var from = GetCardPose(e.fromCon, e.fromLoc, e.fromSeq, e.fromPos);
            if (e.fromLoc == YgoConstants.LOCATION_HAND && e.fromCon < _handCounts.Length)
                _handCounts[e.fromCon] = Mathf.Max(0, _handCounts[e.fromCon] - 1);
            if (e.toLoc == YgoConstants.LOCATION_HAND && e.toCon < _handCounts.Length)
                _handCounts[e.toCon]++;

            var to = GetCardPose(e.toCon, e.toLoc, e.toSeq, e.toPos);
            if (!from.IsValid && !to.IsValid) return;

            bool disappearing = e.toLoc == 0;
            if (!from.IsValid)
                from = new CardPose(to.Position + Vector2.up * 52f, to.Size * 0.58f, to.Angle);
            if (!to.IsValid)
                to = new CardPose(from.Position + Vector2.up * 25f, from.Size * 0.72f, from.Angle);

            bool fromBack = e.code == 0 || IsFaceDown(e.fromCon, e.fromLoc, e.fromPos);
            bool toBack = disappearing ? fromBack : e.code == 0 || IsFaceDown(e.toCon, e.toLoc, e.toPos);
            bool sameZone = e.fromCon == e.toCon && e.fromLoc == e.toLoc && e.fromSeq == e.toSeq;
            CardMotion motion = CardMotion.Transfer;
            if (disappearing) motion = CardMotion.Vanish;
            else if ((e.reason & YgoConstants.REASON_DESTROY) != 0) motion = CardMotion.Destroy;
            else if (sameZone) motion = CardMotion.ChangePosition;
            else if ((e.toLoc & YgoConstants.LOCATION_ONFIELD) != 0 && toBack) motion = CardMotion.Set;
            else if (e.toLoc == YgoConstants.LOCATION_MZONE && e.fromLoc != YgoConstants.LOCATION_MZONE)
            {
                uint type = CardDatabase.Get(e.code)?.type ?? 0;
                bool special = (e.reason & YgoConstants.REASON_SPSUMMON) != 0;
                if ((type & YgoConstants.TYPE_TOKEN) != 0) motion = CardMotion.TokenSummon;
                else if (special && (e.fromLoc == YgoConstants.LOCATION_EXTRA && (type & (YgoConstants.TYPE_EXTRA | YgoConstants.TYPE_PENDULUM)) != 0 ||
                    e.fromLoc == YgoConstants.LOCATION_HAND && (type & YgoConstants.TYPE_RITUAL) != 0))
                    motion = CardMotion.ExtraDeckSummon;
                else motion = special ? CardMotion.SpecialSummon : CardMotion.Summon;
            }

            var flight = new CardFlight(e.code, from, to, fromBack, toBack);
            if (disappearing || (e.toLoc & YgoConstants.LOCATION_OVERLAY) != 0)
            {
                animator.QueueMove(flight, motion, null);
                return;
            }
            var key = HideUntilArrival(e.toCon, e.toLoc, e.toSeq);
            animator.QueueMove(flight, motion, () => ShowArrival(key));
        }

        private void HandleChainActivatedEvent(ChainLinkInfo link)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null) return;
            var pose = GetCardPose(link.triggerPlayer, link.triggerLoc, link.triggerSeq, YgoConstants.POS_FACEUP_ATTACK);
            animator.QueueChain(pose, link.code, link.chainNumber, link.triggerPlayer != _session.HumanPlayerIndex);
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

        private void HandleAttackEvent(byte attacker, uint location, uint sequence, byte target, uint targetLoc, uint targetSeq)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null) return;
            var from = GetCardPose(attacker, location, sequence, YgoConstants.POS_FACEUP_ATTACK);
            if (from.IsValid)
                animator.QueueAttackArrow(from.Position, AttackTarget(animator, attacker, target, targetLoc, targetSeq),
                    attacker != _session.HumanPlayerIndex);
        }

        private void HandleBattleResolved(byte attacker, uint location, uint sequence, byte target, uint targetLoc, uint targetSeq)
        {
            var animator = CardAnimationManager.Instance;
            var card = FindLiveCard(attacker, location, sequence);
            if (animator == null || card == null) return;

            var from = GetCardPose(attacker, location, sequence, card.position);
            var hit = AttackTarget(animator, attacker, target, targetLoc, targetSeq);
            var key = HideUntilArrival(attacker, location, sequence);
            animator.QueueAttack(from, hit, card.code, targetLoc == 0, attacker != _session.HumanPlayerIndex,
                () => ShowArrival(key));
        }

        private void HandleLifePointsChanged(byte player, int delta, int lifePoints)
        {
            bool mine = player == _session.HumanPlayerIndex;
            var animator = CardAnimationManager.Instance;
            if (animator == null) return;
            var point = animator.GetPoint((mine ? _myLpText : _oppLpText).rectTransform, new Vector2(0.64f, 0.5f));
            animator.QueueLifeChange(point, delta, () =>
            {
                if (mine) _myTargetLp = lifePoints;
                else _oppTargetLp = lifePoints;
            });
        }

        private void HandleTurnStarted(int turn, int player)
        {
            bool opponent = player != _session.HumanPlayerIndex;
            var animator = CardAnimationManager.Instance;
            if (animator == null) return;
            var center = animator.GetPoint(_boardRect, new Vector2(0.5f, 0.54f));
            animator.QueuePhase(opponent ? "상대 턴" : "내 턴", opponent, center, () =>
            {
                _turnBadgeText.text = $"턴 {turn}";
                SetPhasePlate(YgoConstants.PHASE_DRAW, opponent);
            });
        }

        private void HandlePhaseChanged(int phase, int player)
        {
            bool opponent = player != _session.HumanPlayerIndex;
            var animator = CardAnimationManager.Instance;
            if (animator == null) return;
            var center = animator.GetPoint(_boardRect, new Vector2(0.5f, 0.54f));
            animator.QueuePhase(YgoConstants.GetPhaseNameKR(phase), opponent, center, () => SetPhasePlate(phase, opponent));
        }

        public void Update()
        {
            if (_session == null || Root == null || !Root.activeSelf) return;
            if (IsReadingLog)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) CloseDuelLog();
                return;
            }
            byte me = (byte)_session.HumanPlayerIndex;
            byte opponent = (byte)_session.AiPlayerIndex;
            if (_myHandBar.rect.size != _myHandSize) RefreshHand(_session.Players[me].hand, me, false);
            if (_oppHandBar.rect.size != _oppHandSize) RefreshHand(_session.Players[opponent].hand, opponent, true);

            // Keep the current HP readable while the damage animation is playing.
            if (!IsPaused && Mathf.Abs(_myDisplayLp - _myTargetLp) > 0.5f)
            {
                float step = Mathf.Max(1200f * Time.deltaTime, Mathf.Abs(_myDisplayLp - _myTargetLp) * 6.5f * Time.deltaTime);
                int oldInt = (int)_myDisplayLp;
                _myDisplayLp = Mathf.MoveTowards(_myDisplayLp, _myTargetLp, step);
                int newInt = (int)_myDisplayLp;
                if (oldInt != newInt)
                {
                    LotdAudio.PlayTick();
                }
                _myLpText.text = ((int)_myDisplayLp).ToString();
                _myLpFill.anchorMax = new Vector2(Mathf.Clamp01(_myDisplayLp / 8000f), 1f);
            }

            if (!IsPaused && Mathf.Abs(_oppDisplayLp - _oppTargetLp) > 0.5f)
            {
                float step = Mathf.Max(1200f * Time.deltaTime, Mathf.Abs(_oppDisplayLp - _oppTargetLp) * 6.5f * Time.deltaTime);
                int oldInt = (int)_oppDisplayLp;
                _oppDisplayLp = Mathf.MoveTowards(_oppDisplayLp, _oppTargetLp, step);
                int newInt = (int)_oppDisplayLp;
                if (oldInt != newInt)
                {
                    LotdAudio.PlayTick();
                }
                _oppLpText.text = ((int)_oppDisplayLp).ToString();
                _oppLpFill.anchorMax = new Vector2(Mathf.Clamp01(_oppDisplayLp / 8000f), 1f);
            }

            // Critical low LP warning pulsation (<= 2000 LP)
            if (_myDisplayLp <= 2000f && _myDisplayLp > 0f)
            {
                _myLpText.color = Color.Lerp(Color.white, new Color(1f, 0.22f, 0.22f, 1f), Mathf.PingPong(Time.time * 3.5f, 1f));
            }
            else
            {
                _myLpText.color = Color.white;
            }

            if (_oppDisplayLp <= 2000f && _oppDisplayLp > 0f)
            {
                _oppLpText.color = Color.Lerp(Color.white, new Color(1f, 0.22f, 0.22f, 1f), Mathf.PingPong(Time.time * 3.5f, 1f));
            }
            else
            {
                _oppLpText.color = Color.white;
            }

            // Global Cancel / Dismiss via ESC or Right Mouse Button
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                if (_cardZoomModal != null && _cardZoomModal.activeSelf)
                {
                    LotdAudio.PlayClick();
                    _cardZoomModal.SetActive(false);
                }
                else if (_actionPopup != null && _actionPopup.activeSelf)
                {
                    LotdAudio.PlayClick();
                    CloseActionPopup();
                }
                else if (_modals != null && _modals.ZoneViewRoot != null && _modals.ZoneViewRoot.activeSelf)
                {
                    LotdAudio.PlayClick();
                    _modals.ZoneViewRoot.SetActive(false);
                }
                else if (_modals != null && _modals.ModalRoot != null && _modals.ModalRoot.activeSelf)
                {
                    _modals.TryCancelOrPass();
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

            // Global Confirm via Enter / KeypadEnter
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (IsPaused || IsPresenting) return;
                if (_modals != null && _modals.ResultRoot != null && _modals.ResultRoot.activeSelf)
                {
                    _modals.TriggerRematch();
                }
                else if (_modals != null && _modals.ModalRoot != null && _modals.ModalRoot.activeSelf)
                {
                    _modals.TryConfirm();
                }
            }
        }



        // ==================== PROMPT INTERACTIONS ====================
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
            var p = _session?.ActivePrompt;
            if (p == null || p.playerId != _session.HumanPlayerIndex) return false;
            if (p.messageType != YgoConstants.MSG_SELECT_PLACE && p.messageType != YgoConstants.MSG_SELECT_DISFIELD)
                return false;

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

        private void ExecuteActionInt(int responseVal)
        {
            if (!CanAcceptPlayerAction) return;
            LotdAudio.PlayClick();
            _actionPopup.SetActive(false);
            _modals.HideAll();
            _session.SubmitResponseInt(responseVal);
            Refresh();
        }

        private void OpenStackZone(byte con, uint loc, string title, bool hideFaceDown = false)
        {
            if (_session == null || IsPresenting || IsPaused) return;
            bool waitingMyAction = CanAcceptPlayerAction;
            var zoneActions = waitingMyAction ? CollectZoneActions(con, loc) : null;

            if (loc == YgoConstants.LOCATION_DECK)
            {
                if (zoneActions != null && zoneActions.Count > 0)
                    ShowZoneActionsPopup(title, zoneActions);
                return;
            }

            if (con == _session.AiPlayerIndex && loc == YgoConstants.LOCATION_EXTRA)
            {
                hideFaceDown = true;
            }

            var ps = _session.Players[con];
            IReadOnlyList<LiveCardState> cards = loc == YgoConstants.LOCATION_EXTRA ? ps.extra
                : loc == YgoConstants.LOCATION_GRAVE ? ps.grave
                : ps.banished;

            _modals.ShowZoneViewer(
                title,
                cards,
                hideFaceDown,
                waitingMyAction ? (Func<LiveCardState, List<(string label, int responseVal, bool gold)>>)(c => CollectActions(c.controller, c.location, c.sequence)) : null,
                waitingMyAction ? (Action<int>)ExecuteActionInt : null,
                waitingMyAction ? (Action<LiveCardState>)(c => ShowCardActionsPopup(c.controller, c.location, c.sequence, c.code, c.overlayCodes)) : null);
        }

        private void OnSlotClicked(byte con, uint loc, uint seq)
        {
            if (_session == null || IsPresenting || IsPaused) return;

            if (IsPlaceSelectable(con, loc, seq))
            {
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
                _actionPopup.SetActive(false);
                _session.SubmitResponse(resp);
                Refresh();
                return;
            }

            var live = FindLiveCard(con, loc, seq);
            if (live != null && (live.IsFaceUp || live.isPublic || con == _session.HumanPlayerIndex))
            {
                InspectCard(live.code);
            }

            ShowCardActionsPopup(con, loc, seq, live?.code ?? 0, live?.overlayCodes);
        }

        private LiveCardState FindLiveCard(byte con, uint loc, uint seq)
        {
            if (_session == null) return null;
            var ps = _session.Players[con];
            if (loc == YgoConstants.LOCATION_MZONE && seq < 7) return ps.mzone[seq];
            if (loc == YgoConstants.LOCATION_SZONE && seq < 8) return ps.szone[seq];
            return null;
        }

        private void ShowCardActionsPopup(byte con, uint loc, uint seq, uint code, List<uint> overlays)
        {
            if (IsPresenting || IsPaused || _session == null) return;
            var actions = CollectActions(con, loc, seq);
            var anchor = GetSlot(con, loc, seq)?.root ?? GetStack(con, loc)?.root;
            if (loc == YgoConstants.LOCATION_HAND && con == _session.HumanPlayerIndex && seq < _myHandViews.Count)
                anchor = _myHandViews[(int)seq].Rect;
            PopulateAndShowActionPopup(code == 0 ? "카드 행동" : CardDatabase.GetName(code), actions, overlays, anchor);
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
            List<uint> overlays, RectTransform anchor = null)
        {
            bool hasMaterials = overlays != null && overlays.Count > 0;
            if (actions.Count == 0 && !hasMaterials)
            {
                CloseActionPopup();
                return;
            }

            ClearActionRows();
            _actionHeader.text = header;
            foreach (var action in actions)
            {
                int command = action.responseVal;
                AddActionRow(action.label, () => ExecuteActionInt(command), action.gold, CommandIcon(command));
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
            for (int i = _actionList.childCount - 1; i >= 0; i--)
            {
                var row = _actionList.GetChild(i).gameObject;
                row.SetActive(false);
                UnityEngine.Object.Destroy(row);
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
            _actionWindow.anchoredPosition = center;
        }

        private void CloseActionPopup()
        {
            _actionPopup.SetActive(false);
            if (!IsPaused) return;
            IsPaused = false;
            if (CardAnimationManager.Instance != null) CardAnimationManager.Instance.Paused = false;
            Refresh();
        }

        private void OnPhaseShortcut(int targetPhase)
        {
            if (!CanAcceptPlayerAction) return;
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
                _arrivingCards.Clear();
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

        private void BuildCardZoomModal(Transform root)
        {
            var dim = LotdUiKit.FillRect("Overlay_CardZoom", root);
            _cardZoomModal = dim.gameObject;
            LotdUiKit.AddImage(dim.gameObject, LotdUiKit.WhiteSprite, new Color(0.01f, 0.03f, 0.08f, 0.88f));
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => _cardZoomModal.SetActive(false));

            var win = DuelStyle.Panel(dim, "ZoomWin", new Vector2(0.18f, 0.08f), new Vector2(0.82f, 0.92f));

            // Header bar
            var hdr = LotdUiKit.CreateRect("Hdr", win, new Vector2(0f, 0.90f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(hdr.gameObject, LotdUiKit.WhiteSprite, DuelStyle.HeaderColor);
            var title = LotdUiKit.CreateRect("Title", hdr, new Vector2(0.02f, 0.12f), new Vector2(0.63f, 0.88f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(title, "T", "카드 상세 정보 (CARD DETAILS)", 18, 12,
                TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);

            var relatedRt = LotdUiKit.CreateRect("Related", hdr, new Vector2(0.66f, 0.15f), new Vector2(0.82f, 0.85f),
                Vector2.zero, Vector2.zero);
            DuelStyle.Button(relatedRt, "B", "관련 카드", ShowZoomRelatedCards, 15);

            var closeRt = LotdUiKit.CreateRect("Close", hdr, new Vector2(0.84f, 0.15f), new Vector2(0.98f, 0.85f), Vector2.zero, Vector2.zero);
            DuelStyle.Button(closeRt, "B", "닫기 (ESC)", () => _cardZoomModal.SetActive(false), 15);

            // Left Side: Large Card View
            var cardHolder = LotdUiKit.CreateRect("CardHolder", win, new Vector2(0.04f, 0.12f), new Vector2(0.42f, 0.88f), Vector2.zero, Vector2.zero);
            _zoomCardView = CompositeCardView.Create(cardHolder, "ZoomCard", true, duelArtwork: true);
            var artwork = LotdUiKit.CreateRect("ArtworkOptions", win, new Vector2(.04f, .035f), new Vector2(.42f, .098f),
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
            var infoRt = LotdUiKit.CreateRect("Info", win, new Vector2(0.45f, 0.05f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero);

            _zoomName = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Name", infoRt,
                new Vector2(0f, 0.88f), new Vector2(0.82f, 1f), Vector2.zero, Vector2.zero),
                "N", "", 22, 12, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);

            var rRt = LotdUiKit.CreateRect("RaceIc", infoRt, new Vector2(0.84f, 0.88f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero);
            _zoomRaceIcon = LotdUiKit.AddImage(rRt.gameObject, null, Color.white, true);

            _zoomStats = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Stats", infoRt,
                new Vector2(0f, 0.74f), new Vector2(1f, 0.87f), Vector2.zero, Vector2.zero),
                "S", "", 14, 10, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);

            _zoomPasscode = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Passcode", infoRt,
                new Vector2(0f, 0.68f), new Vector2(1f, 0.74f), Vector2.zero, Vector2.zero),
                "P", "", 12, 9, TextAnchor.MiddleLeft, LotdUiKit.CyanAccent);

            var descBox = LotdUiKit.CreateBorderedPanel(infoRt, "DescBox", new Vector2(0f, 0f), new Vector2(1f, 0.66f), Vector2.zero, Vector2.zero,
                new Color(0.02f, 0.05f, 0.12f, 0.95f));
            var vp = LotdUiKit.CreateRect("VP", descBox, Vector2.zero, Vector2.one,
                new Vector2(10f, 8f), new Vector2(-19f, -8f));
            vp.gameObject.AddComponent<RectMask2D>();
            var ct = LotdUiKit.CreateRect("C", vp, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            ct.pivot = new Vector2(0.5f, 1f);
            _zoomDesc = ct.gameObject.AddComponent<Text>();
            _zoomDesc.font = LotdUiKit.FontKR;
            _zoomDesc.fontSize = 15;
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
            _zoomScroll.verticalScrollbar = LotdUiKit.AddVerticalScrollbar(descBox);
            _zoomScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            _cardZoomModal.SetActive(false);
        }

        public void ShowCardZoomModal(uint code)
        {
            if (code == 0 || _zoomCardView == null) return;
            _cardZoomModal.SetActive(true);
            var info = CardDatabase.Get(code);
            _zoomCardView.SetCard(code, false);
            if (info != null)
            {
                _zoomName.text = info.name;
                _zoomStats.text = info.FormatStatsLineKR();
                string attrStr = info.IsMonster && YgoConstants.AttributeNamesKR.TryGetValue(info.attr, out var an) ? $"[{an} 속성]  " : "";
                _zoomPasscode.text = $"카드 No. {info.code:D8}  |  {attrStr}{YgoConstants.GetTypeTagKR(info.type)}";
                _zoomDesc.text = info.desc;
                var rSp = info.IsMonster ? LotdUiKit.GetRaceSprite(info.race) : LotdUiKit.LoadSprite($"LOTD/Icons/{YgoConstants.GetSpellTrapSubSpriteName(info.type)}");
                _zoomRaceIcon.sprite = rSp;
                _zoomRaceIcon.gameObject.SetActive(rSp != null);
            }
            else
            {
                _zoomName.text = $"카드 #{code}";
                _zoomStats.text = "";
                _zoomPasscode.text = $"카드 No. {code}";
                _zoomDesc.text = "";
                _zoomRaceIcon.gameObject.SetActive(false);
            }
            UpdateArtworkOptions(code, 0);
            _zoomScroll.StopMovement();
            _zoomScroll.content.anchoredPosition = Vector2.zero;
        }

        private void CycleArtwork(int direction)
        {
            uint code = _zoomCardView.CurrentCode;
            UpdateArtworkOptions(code, direction);
            _zoomCardView.SetCard(code);
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
