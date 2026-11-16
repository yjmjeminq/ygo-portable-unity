using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public class DeckEditorUI
    {
        public GameObject Root { get; private set; }

        private readonly GameAppController _app;

        // Current edited deck
        private int _userDeckIdx = 0;
        private UserDeck _workingDeck;
        private uint _inspectedCode = 89631139;

        // Filters & Search
        private string _searchText = "";
        private int _kindFilter = 0; // 0=All, 1=Monster, 2=Spell, 3=Trap, 4=Extra
        private int _raceFilterIdx = 0; // index into RaceFilterOptions
        private int _sortMode = 0; // 0=Default, 1=ATK Desc, 2=Level Desc, 3=Name Asc

        // Virtualized Trunk Scroll Grid
        private const int TrunkCols = 6;
        private const int PoolRows = 7;
        private const int PoolSlotCount = TrunkCols * PoolRows;
        private float _rowHeight = 138f;
        private float _colWidth = 92f;
        private int _totalRows = 0;
        private int _lastFirstRow = -1;

        private readonly List<CardInfo> _trunkResults = new List<CardInfo>();
        private readonly List<(RectTransform rt, CompositeCardView card, Text sub, CardDragTrigger trigger)> _trunkPool
            = new List<(RectTransform, CompositeCardView, Text, CardDragTrigger)>();

        // Deck Slot Lists
        private readonly List<(RectTransform rt, CompositeCardView card, CardDragTrigger trigger)> _mainSlots
            = new List<(RectTransform, CompositeCardView, CardDragTrigger)>();
        private readonly List<(RectTransform rt, CompositeCardView card, CardDragTrigger trigger)> _extraSlots
            = new List<(RectTransform, CompositeCardView, CardDragTrigger)>();
        private readonly List<(RectTransform rt, CompositeCardView card, CardDragTrigger trigger)> _sideSlots
            = new List<(RectTransform, CompositeCardView, CardDragTrigger)>();

        private bool _showingSide = false;

        // UI widgets
        private CompositeCardView _previewCard;
        private Image _previewRaceIcon;
        private Text _previewName;
        private Text _previewStats;
        private Text _previewDesc;

        private ScrollRect _trunkScroll;
        private RectTransform _trunkContent;
        private RectTransform _trunkViewport;
        private Text _trunkCountText;
        private Text _trunkScrollHint;
        private uint _relatedFilterCode = 0;
        private ArchetypeInfo _activeArchetypeFilter = null;
        private InputField _searchInp;
        private RectTransform _searchInpRt;
        private GameObject _relatedBanner;
        private Text _relatedBannerText;

        // Archetype Example Deck Browser Modal
        private GameObject _archModalRoot;
        private InputField _archSearchInp;
        private Text _archSortBtnText;
        private Text _archCountText;
        private ScrollRect _archScroll;
        private RectTransform _archViewport;
        private RectTransform _archContent;
        private string _archSearchText = "";
        private int _archSortMode = 0; // 0=Card Count Desc, 1=Name Asc
        private const int ArchCols = 3;
        private const int ArchPoolRows = 6;
        private float _archRowHeight = 112f;
        private int _archTotalRows = 0;
        private int _archLastFirstRow = -1;
        private readonly List<ArchetypeInfo> _archResults = new List<ArchetypeInfo>();
        private readonly List<(RectTransform rt, CompositeCardView card, Text titleTxt, Text subTxt, Button loadBtn, Button inspectBtn)> _archPool
            = new List<(RectTransform, CompositeCardView, Text, Text, Button, Button)>();

        private InputField _deckNameInput;
        private Text _deckHeaderText;
        private Text _mainCountText;
        private Text _extraOrSideTitle;
        private Text _toastText;
        private Text _kindFilterBtnText;
        private Text _raceFilterBtnText;
        private Text _sortBtnText;

        private RectTransform _deckColRect;
        private RectTransform _mainGridRect;
        private RectTransform _extraGridRect;
        private RectTransform _sideGridRect;
        private GameObject _extraGridHolder;
        private GameObject _sideGridHolder;

        private static readonly (ulong mask, string label)[] RaceOptions =
        {
            (0UL, "전 종족"),
            (YgoConstants.RACE_ILLUSION, "환상마족"),
            (YgoConstants.RACE_DRAGON, "드래곤족"),
            (YgoConstants.RACE_SPELLCASTER, "마법사족"),
            (YgoConstants.RACE_WARRIOR, "전사족"),
            (YgoConstants.RACE_FIEND, "악마족"),
            (YgoConstants.RACE_MACHINE, "기계족"),
            (YgoConstants.RACE_PYRO, "화염족"),
            (YgoConstants.RACE_FAIRY, "천사족"),
            (YgoConstants.RACE_CYBERSE, "사이버스족"),
            (YgoConstants.RACE_ZOMBIE, "언데드족"),
            (YgoConstants.RACE_THUNDER, "번개족"),
            (YgoConstants.RACE_BEAST, "야수족"),
            (YgoConstants.RACE_AQUA, "물족"),
            (YgoConstants.RACE_WYRM, "환룡족"),
            (YgoConstants.RACE_PSYCHIC, "사이킥족"),
        };

        public DeckEditorUI(GameAppController app, Transform canvasParent)
        {
            _app = app;
            BuildUI(canvasParent);

            if (CardDragManager.Instance != null)
            {
                CardDragManager.Instance.OnDragDropped += HandleCardDrop;
            }
        }

        public void Open()
        {
            Root.SetActive(true);
            if (DeckRepository.UserDecks.Count == 0)
                DeckRepository.Load(Application.streamingAssetsPath, Application.persistentDataPath);

            _userDeckIdx = Mathf.Clamp(_userDeckIdx, 0, Mathf.Max(0, DeckRepository.UserDecks.Count - 1));
            LoadWorkingDeckFromCurrent();
            RefreshTrunkFilter();
            InspectCard(_inspectedCode);
            ShowToast("");
        }

        private void LoadWorkingDeckFromCurrent()
        {
            if (DeckRepository.UserDecks.Count == 0)
            {
                _workingDeck = new UserDeck { id = $"custom_{DateTime.UtcNow:yyyyMMddHHmmss}", name = "새 커스텀 덱" };
                DeckRepository.SaveUserDeck(_workingDeck);
            }
            else
            {
                var src = DeckRepository.UserDecks[_userDeckIdx];
                _workingDeck = new UserDeck
                {
                    id = src.id,
                    name = src.name,
                    main = new List<uint>(src.main),
                    extra = new List<uint>(src.extra),
                    side = new List<uint>(src.side),
                };
            }
            _deckNameInput.text = _workingDeck.name;
            RefreshDeckGrids();
        }

        private void BuildUI(Transform parent)
        {
            var rt = LotdUiKit.FillRect("Screen_DeckEditor", parent);
            Root = rt.gameObject;

            LotdUiKit.AddImage(LotdUiKit.FillRect("Bg", rt).gameObject,
                LotdUiKit.LoadSprite("LOTD/Background/noise_bgtec_small"), new Color(0.50f, 0.72f, 0.96f, 1f));
            LotdUiKit.AddImage(LotdUiKit.FillRect("Dim", rt).gameObject,
                LotdUiKit.WhiteSprite, new Color(0.02f, 0.05f, 0.12f, 0.75f));

            // Header bar
            var hdr = LotdUiKit.CreateBorderedPanel(rt, "Header",
                new Vector2(0f, 0.92f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero,
                LotdUiKit.PanelHeader, LotdUiKit.CyanAccent);
            LotdUiKit.AddText(LotdUiKit.CreateRect("T", hdr, new Vector2(0.01f, 0), new Vector2(0.36f, 1), Vector2.zero, Vector2.zero),
                "Title", "  덱 편집기 (DECK EDITOR)", 20, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);

            _toastText = LotdUiKit.AddText(LotdUiKit.CreateRect("Toast", hdr, new Vector2(0.37f, 0), new Vector2(0.665f, 1), Vector2.zero, Vector2.zero),
                "Msg", "", 13, TextAnchor.MiddleRight, LotdUiKit.GoldAccent, FontStyle.Bold);

            var archModalBtn = LotdUiKit.CreateRect("ArchBtn", hdr, new Vector2(0.675f, 0.12f), new Vector2(0.855f, 0.88f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(archModalBtn, "B", "📚 순수 카드군 예시 덱", OpenArchetypeModal, 14, gold: true);

            var backBtn = LotdUiKit.CreateRect("Back", hdr, new Vector2(0.865f, 0.12f), new Vector2(0.988f, 0.88f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(backBtn, "B", "메인 메뉴", () => _app.ShowScreen(AppScreen.MainMenu), 15);

            // Columns
            BuildLeftColumn(rt);
            BuildCenterColumn(rt);
            BuildRightColumn(rt);
            BuildArchetypeModal(rt);
        }

        private void BuildLeftColumn(Transform rt)
        {
            var col = LotdUiKit.CreateBorderedPanel(rt, "Col_CardPreview",
                new Vector2(0.01f, 0.02f), new Vector2(0.255f, 0.905f), Vector2.zero, Vector2.zero);

            var cardFrame = LotdUiKit.CreateRect("BigCard", col,
                new Vector2(0.06f, 0.445f), new Vector2(0.94f, 0.98f), Vector2.zero, Vector2.zero);
            _previewCard = CompositeCardView.Create(cardFrame, "PreviewCardView", detailedOverlay: true);

            // Name & Race icon row
            var nameRow = LotdUiKit.CreateRect("NameRow", col,
                new Vector2(0.04f, 0.355f), new Vector2(0.96f, 0.43f), Vector2.zero, Vector2.zero);
            _previewRaceIcon = LotdUiKit.AddImage(
                LotdUiKit.CreateRect("RaceIcon", nameRow, new Vector2(0f, 0.10f), new Vector2(0.16f, 0.90f), Vector2.zero, Vector2.zero).gameObject,
                null, Color.white, true);
            _previewName = LotdUiKit.AddText(
                LotdUiKit.CreateRect("CardName", nameRow, new Vector2(0.18f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero),
                "N", "", 17, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);
            _previewName.resizeTextForBestFit = true;
            _previewName.resizeTextMinSize = 11;
            _previewName.resizeTextMaxSize = 18;

            _previewStats = LotdUiKit.AddText(
                LotdUiKit.CreateRect("Stats", col, new Vector2(0.04f, 0.275f), new Vector2(0.96f, 0.355f), Vector2.zero, Vector2.zero),
                "S", "", 12, TextAnchor.MiddleLeft, LotdUiKit.CyanAccent, FontStyle.Bold);

            // Scrollable Card Effect Text
            var descBox = LotdUiKit.CreateBorderedPanel(col, "DescBox",
                new Vector2(0.04f, 0.125f), new Vector2(0.96f, 0.27f), Vector2.zero, Vector2.zero,
                new Color(0.02f, 0.05f, 0.12f, 0.95f));
            var viewport = LotdUiKit.FillRect("VP", descBox, 6);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = LotdUiKit.CreateRect("Content", viewport, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            _previewDesc = content.gameObject.AddComponent<Text>();
            _previewDesc.font = LotdUiKit.FontKR;
            _previewDesc.fontSize = 13;
            _previewDesc.color = LotdUiKit.TextPrimary;
            _previewDesc.alignment = TextAnchor.UpperLeft;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = descBox.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 25f;

            // Row 1: Search Related Cards button + Build Pure Archetype Deck from inspected card
            var relBtnRt = LotdUiKit.CreateRect("RelCardBtn", col,
                new Vector2(0.04f, 0.070f), new Vector2(0.49f, 0.118f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(relBtnRt, "RelB", "🔍 연관 카드 찾기", () => FilterRelatedCards(_inspectedCode), 12, gold: false);

            var pureQuickRt = LotdUiKit.CreateRect("PureQuickBtn", col,
                new Vector2(0.51f, 0.070f), new Vector2(0.96f, 0.118f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(pureQuickRt, "PureB", "⚡ 이 카드군 순수덱", BuildPureDeckFromInspectedCard, 12, gold: true);

            // Row 2: Add button at bottom
            var addBtnRt = LotdUiKit.CreateRect("AddCardBtn", col,
                new Vector2(0.04f, 0.015f), new Vector2(0.96f, 0.063f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(addBtnRt, "AddB", "+ 내 덱에 1장 추가 (드래그/우클릭)", () => TryAddCardToDeck(_inspectedCode, _showingSide), 13, gold: true);
        }

        private void BuildCenterColumn(Transform rt)
        {
            var col = LotdUiKit.CreateBorderedPanel(rt, "Col_Trunk",
                new Vector2(0.262f, 0.02f), new Vector2(0.635f, 0.905f), Vector2.zero, Vector2.zero);

            // Row 1: Search bar + Result count
            var r1 = LotdUiKit.CreateRect("R1", col, new Vector2(0.02f, 0.925f), new Vector2(0.98f, 0.985f), Vector2.zero, Vector2.zero);
            _searchInp = LotdUiKit.CreateInputField(LotdUiKit.CreateRect("Inp", r1, new Vector2(0, 0), new Vector2(0.68f, 1), Vector2.zero, Vector2.zero),
                "Search", "카드 이름 / 효과 텍스트 검색...", 14);
            _searchInpRt = _searchInp.GetComponent<RectTransform>();
            _searchInp.onValueChanged.AddListener(v =>
            {
                _searchText = v ?? "";
                RefreshTrunkFilter();
            });

            // Related Cards Banner inside R1
            _relatedBanner = LotdUiKit.CreateBorderedPanel(r1, "RelBanner",
                new Vector2(0f, 0f), new Vector2(0.68f, 1f), Vector2.zero, Vector2.zero,
                new Color(0.05f, 0.15f, 0.28f, 0.95f), LotdUiKit.GoldAccent).gameObject;
            _relatedBannerText = LotdUiKit.AddText(LotdUiKit.CreateRect("T", _relatedBanner.transform,
                new Vector2(0.02f, 0f), new Vector2(0.72f, 1f), Vector2.zero, Vector2.zero),
                "T", "", 13, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);
            LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("ClrBtn", _relatedBanner.transform,
                new Vector2(0.74f, 0.08f), new Vector2(0.98f, 0.92f), Vector2.zero, Vector2.zero),
                "B", "전체 ✕", ClearRelatedFilter, 11, gold: true);
            _relatedBanner.SetActive(false);
            _trunkCountText = LotdUiKit.AddText(LotdUiKit.CreateRect("Cnt", r1, new Vector2(0.70f, 0), new Vector2(1f, 1), Vector2.zero, Vector2.zero),
                "C", "0장", 13, TextAnchor.MiddleRight, LotdUiKit.CyanAccent, FontStyle.Bold);

            // Row 2: Kind filter + Race filter + Sort
            var r2 = LotdUiKit.CreateRect("R2", col, new Vector2(0.02f, 0.865f), new Vector2(0.98f, 0.918f), Vector2.zero, Vector2.zero);
            var bKind = LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("FK", r2, new Vector2(0f, 0), new Vector2(0.32f, 1), Vector2.zero, Vector2.zero),
                "B", "종류: 전체", () =>
                {
                    _kindFilter = (_kindFilter + 1) % 5;
                    string[] lbls = { "종류: 전체", "종류: 몬스터", "종류: 마법", "종류: 함정", "종류: 엑스트라" };
                    _kindFilterBtnText.text = lbls[_kindFilter];
                    RefreshTrunkFilter();
                }, 13);
            _kindFilterBtnText = bKind.GetComponentInChildren<Text>();

            var bRace = LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("FR", r2, new Vector2(0.34f, 0), new Vector2(0.67f, 1), Vector2.zero, Vector2.zero),
                "B", "종족: 전 종족", () =>
                {
                    _raceFilterIdx = (_raceFilterIdx + 1) % RaceOptions.Length;
                    _raceFilterBtnText.text = $"종족: {RaceOptions[_raceFilterIdx].label}";
                    RefreshTrunkFilter();
                }, 13);
            _raceFilterBtnText = bRace.GetComponentInChildren<Text>();

            var bSort = LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("FS", r2, new Vector2(0.69f, 0), new Vector2(1f, 1), Vector2.zero, Vector2.zero),
                "B", "정렬: 기본순", () =>
                {
                    _sortMode = (_sortMode + 1) % 4;
                    string[] sLbls = { "정렬: 기본순", "정렬: ATK순", "정렬: 레벨순", "정렬: 이름순" };
                    _sortBtnText.text = sLbls[_sortMode];
                    RefreshTrunkFilter();
                }, 13);
            _sortBtnText = bSort.GetComponentInChildren<Text>();

            // Virtualized Scrollable Grid Area (0.055 .. 0.855)
            var scrollHolder = LotdUiKit.CreateRect("TrunkScrollHolder", col,
                new Vector2(0.015f, 0.055f), new Vector2(0.985f, 0.855f), Vector2.zero, Vector2.zero);

            _trunkScroll = scrollHolder.gameObject.AddComponent<ScrollRect>();
            _trunkScroll.horizontal = false;
            _trunkScroll.vertical = true;
            _trunkScroll.scrollSensitivity = 40f;
            _trunkScroll.movementType = ScrollRect.MovementType.Clamped;

            _trunkViewport = LotdUiKit.FillRect("Viewport", scrollHolder);
            _trunkViewport.gameObject.AddComponent<RectMask2D>();
            _trunkScroll.viewport = _trunkViewport;

            _trunkContent = LotdUiKit.CreateRect("Content", _trunkViewport, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            _trunkContent.pivot = new Vector2(0.5f, 1f);
            _trunkScroll.content = _trunkContent;

            // Instantiate Slot Pool (42 slots)
            for (int r = 0; r < PoolRows; r++)
            {
                for (int c = 0; c < TrunkCols; c++)
                {
                    var slot = LotdUiKit.CreateRect($"Slot_{r}_{c}", _trunkContent, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
                    slot.pivot = new Vector2(0, 1);
                    slot.sizeDelta = new Vector2(_colWidth, _rowHeight);

                    var card = CompositeCardView.Create(slot, "CV", false);
                    var cntBg = LotdUiKit.CreateRect("CntBg", slot, new Vector2(0.52f, 0.02f), new Vector2(0.98f, 0.22f), Vector2.zero, Vector2.zero);
                    LotdUiKit.AddImage(cntBg.gameObject, LotdUiKit.WhiteSprite, new Color(0, 0, 0, 0.80f), false, false);
                    var cntTxt = LotdUiKit.AddText(cntBg, "T", "", 11, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

                    var hit = slot.gameObject.AddComponent<Image>();
                    hit.color = Color.clear;

                    var trigger = slot.gameObject.AddComponent<CardDragTrigger>();
                    trigger.SourceType = DragSourceType.Trunk;

                    _trunkPool.Add((slot, card, cntTxt, trigger));
                }
            }

            _trunkScroll.onValueChanged.AddListener(OnTrunkScrolled);

            // Bottom bar: Scroll status hint & Jump to top button
            var botBar = LotdUiKit.CreateRect("TrunkBotBar", col, new Vector2(0.02f, 0.01f), new Vector2(0.98f, 0.05f), Vector2.zero, Vector2.zero);
            _trunkScrollHint = LotdUiKit.AddText(LotdUiKit.CreateRect("Hint", botBar, new Vector2(0f, 0), new Vector2(0.75f, 1), Vector2.zero, Vector2.zero),
                "H", "💡 마우스 휠로 자유롭게 스크롤하여 카드를 탐색하고 드래그하여 덱에 넣으세요.", 11, TextAnchor.MiddleLeft, LotdUiKit.TextMuted);

            var topBtn = LotdUiKit.CreateRect("TopBtn", botBar, new Vector2(0.78f, 0), new Vector2(1f, 1), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(topBtn, "B", "맨 위로 ▲", () =>
            {
                _trunkScroll.verticalNormalizedPosition = 1f;
                UpdateVirtualTrunkSlots(0, force: true);
            }, 12);
        }

        private void BuildRightColumn(Transform rt)
        {
            _deckColRect = LotdUiKit.CreateBorderedPanel(rt, "Col_Deck",
                new Vector2(0.642f, 0.02f), new Vector2(0.99f, 0.905f), Vector2.zero, Vector2.zero);

            // Top row: Prev Deck / Deck Selector / Next Deck / New / Save
            var top1 = LotdUiKit.CreateRect("Top1", _deckColRect, new Vector2(0.02f, 0.915f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("PD", top1, new Vector2(0, 0), new Vector2(0.11f, 1), Vector2.zero, Vector2.zero),
                "B", "◀", () => SwitchUserDeck(-1), 14);
            _deckHeaderText = LotdUiKit.AddText(LotdUiKit.CreateRect("DH", top1, new Vector2(0.12f, 0), new Vector2(0.46f, 1), Vector2.zero, Vector2.zero),
                "L", "덱 1/1", 13, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);
            LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("ND", top1, new Vector2(0.47f, 0), new Vector2(0.58f, 1), Vector2.zero, Vector2.zero),
                "B", "▶", () => SwitchUserDeck(1), 14);
            LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("New", top1, new Vector2(0.60f, 0), new Vector2(0.78f, 1), Vector2.zero, Vector2.zero),
                "B", "새 덱", CreateNewUserDeck, 13);
            LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("Save", top1, new Vector2(0.80f, 0), new Vector2(1f, 1), Vector2.zero, Vector2.zero),
                "B", "저장", SaveCurrentDeck, 14, gold: true);

            // Row 2: Deck Name Input + Sort + Shuffle + Toggle Extra/Side + Delete
            var top2 = LotdUiKit.CreateRect("Top2", _deckColRect, new Vector2(0.02f, 0.855f), new Vector2(0.98f, 0.908f), Vector2.zero, Vector2.zero);
            _deckNameInput = LotdUiKit.CreateInputField(LotdUiKit.CreateRect("DName", top2, new Vector2(0, 0), new Vector2(0.42f, 1), Vector2.zero, Vector2.zero),
                "DeckName", "덱 이름...", 14);
            LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("SortD", top2, new Vector2(0.44f, 0), new Vector2(0.57f, 1), Vector2.zero, Vector2.zero),
                "B", "정렬", SortWorkingDeck, 12);
            LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("ShufD", top2, new Vector2(0.58f, 0), new Vector2(0.71f, 1), Vector2.zero, Vector2.zero),
                "B", "셔플", ShuffleWorkingDeck, 12);
            LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("SwES", top2, new Vector2(0.72f, 0), new Vector2(0.86f, 1), Vector2.zero, Vector2.zero),
                "B", "Ex/사이드", () =>
                {
                    _showingSide = !_showingSide;
                    _extraGridHolder.SetActive(!_showingSide);
                    _sideGridHolder.SetActive(_showingSide);
                    RefreshDeckGrids();
                }, 12);
            LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("Del", top2, new Vector2(0.88f, 0), new Vector2(1f, 1), Vector2.zero, Vector2.zero),
                "B", "삭제", DeleteCurrentDeck, 12);

            // Main Deck Header + 10x6 Grid (60 slots)
            _mainCountText = LotdUiKit.AddText(LotdUiKit.CreateRect("MainHdr", _deckColRect, new Vector2(0.02f, 0.815f), new Vector2(0.98f, 0.85f), Vector2.zero, Vector2.zero),
                "L", "메인 덱 (0 / 40~60) — 카드 드래그 이동 / 정렬 지원", 13, TextAnchor.MiddleLeft, LotdUiKit.CyanAccent, FontStyle.Bold);

            _mainGridRect = LotdUiKit.CreateBorderedPanel(_deckColRect, "MainGrid",
                new Vector2(0.02f, 0.28f), new Vector2(0.98f, 0.812f), Vector2.zero, Vector2.zero,
                new Color(0.02f, 0.06f, 0.14f, 0.90f));
            BuildDeckSlots(_mainGridRect, _mainSlots, 10, 6, DragSourceType.DeckMain);

            // Extra / Side Header + 8x2 Grid (15 slots)
            _extraOrSideTitle = LotdUiKit.AddText(LotdUiKit.CreateRect("ExtraHdr", _deckColRect, new Vector2(0.02f, 0.235f), new Vector2(0.98f, 0.275f), Vector2.zero, Vector2.zero),
                "L", "엑스트라 덱 (0 / 15)  — 드래그 또는 우클릭 제거", 13, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);

            _extraGridRect = LotdUiKit.CreateBorderedPanel(_deckColRect, "ExtraGrid",
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.232f), Vector2.zero, Vector2.zero,
                new Color(0.02f, 0.06f, 0.14f, 0.90f));
            _extraGridHolder = _extraGridRect.gameObject;
            BuildDeckSlots(_extraGridRect, _extraSlots, 8, 2, DragSourceType.DeckExtra);

            _sideGridRect = LotdUiKit.CreateBorderedPanel(_deckColRect, "SideGrid",
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.232f), Vector2.zero, Vector2.zero,
                new Color(0.02f, 0.06f, 0.14f, 0.90f));
            _sideGridHolder = _sideGridRect.gameObject;
            BuildDeckSlots(_sideGridRect, _sideSlots, 8, 2, DragSourceType.DeckSide);
            _sideGridHolder.SetActive(false);
        }

        private void BuildDeckSlots(Transform parent, List<(RectTransform rt, CompositeCardView card, CardDragTrigger trigger)> target,
            int cols, int rows, DragSourceType sourceType)
        {
            float cw = 1f / cols, rh = 1f / rows;
            int idx = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (sourceType != DragSourceType.DeckMain && idx >= 15) return;
                    var slot = LotdUiKit.CreateRect($"DS_{idx}", parent,
                        new Vector2(c * cw + 0.003f, 1f - (r + 1) * rh + 0.006f),
                        new Vector2((c + 1) * cw - 0.003f, 1f - r * rh - 0.006f),
                        Vector2.zero, Vector2.zero);
                    slot.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.12f, 0.22f, 0.40f);
                    var card = CompositeCardView.Create(slot, "CV", false);

                    var trig = slot.gameObject.AddComponent<CardDragTrigger>();
                    trig.SourceType = sourceType;

                    target.Add((slot, card, trig));
                    idx++;
                }
            }
        }

        private void FilterRelatedCards(uint code)
        {
            if (code == 0) return;
            LotdAudio.PlayClick();
            _activeArchetypeFilter = null;
            _relatedFilterCode = code;
            _searchText = "";
            if (_searchInp != null) _searchInp.text = "";
            RefreshTrunkFilter();
            ShowToast($"[{CardDatabase.GetName(code)}] 연관 카드 {_trunkResults.Count}장 검색됨");
        }

        private void ClearRelatedFilter()
        {
            LotdAudio.PlayClick();
            _activeArchetypeFilter = null;
            _relatedFilterCode = 0;
            _searchText = "";
            if (_searchInp != null) _searchInp.text = "";
            RefreshTrunkFilter();
            ShowToast("전체 카드로 전환되었습니다.");
        }

        private void RefreshTrunkFilter()
        {
            _trunkResults.Clear();
            string q = (_searchText ?? "").Trim().ToLowerInvariant();
            ulong raceMask = RaceOptions[_raceFilterIdx].mask;

            IEnumerable<CardInfo> source = CardDatabase.AllCards;
            if (_activeArchetypeFilter != null)
            {
                var archPool = new List<CardInfo>(_activeArchetypeFilter.coreCards.Count + _activeArchetypeFilter.supportCards.Count);
                archPool.AddRange(_activeArchetypeFilter.coreCards);
                foreach (var sc in _activeArchetypeFilter.supportCards)
                    if (!archPool.Contains(sc)) archPool.Add(sc);
                source = archPool;
                if (_relatedBanner != null) _relatedBanner.SetActive(true);
                if (_relatedBannerText != null)
                    _relatedBannerText.text = $"★ [순수 카드군: {_activeArchetypeFilter.name}] ({archPool.Count}장)";
                if (_searchInpRt != null) _searchInpRt.gameObject.SetActive(false);
            }
            else if (_relatedFilterCode != 0)
            {
                var relList = CardDatabase.GetRelatedCards(_relatedFilterCode);
                source = relList;
                if (_relatedBanner != null) _relatedBanner.SetActive(true);
                if (_relatedBannerText != null)
                {
                    string baseName = CardDatabase.GetName(_relatedFilterCode);
                    _relatedBannerText.text = $"★ [{baseName}] 연관 ({relList.Count}장)";
                }
                if (_searchInpRt != null) _searchInpRt.gameObject.SetActive(false);
            }
            else
            {
                if (_relatedBanner != null) _relatedBanner.SetActive(false);
                if (_searchInpRt != null) _searchInpRt.gameObject.SetActive(true);
            }

            foreach (var c in source)
            {
                if ((c.type & YgoConstants.TYPE_TOKEN) != 0) continue;
                if (_kindFilter == 1 && (!c.IsMonster || c.IsExtra)) continue;
                if (_kindFilter == 2 && !c.IsSpell) continue;
                if (_kindFilter == 3 && !c.IsTrap) continue;
                if (_kindFilter == 4 && !c.IsExtra) continue;
                if (raceMask != 0 && (c.race & raceMask) == 0) continue;
                if (!string.IsNullOrEmpty(q))
                {
                    bool hit = (c.name ?? "").ToLowerInvariant().Contains(q) || (c.desc ?? "").ToLowerInvariant().Contains(q);
                    if (!hit) continue;
                }
                _trunkResults.Add(c);
            }

            if (_sortMode == 1) _trunkResults.Sort((a, b) => b.atk.CompareTo(a.atk));
            else if (_sortMode == 2) _trunkResults.Sort((a, b) => b.level.CompareTo(a.level));
            else if (_sortMode == 3) _trunkResults.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

            _totalRows = Mathf.Max(1, (_trunkResults.Count + TrunkCols - 1) / TrunkCols);
            float totalH = _totalRows * _rowHeight;
            _trunkContent.sizeDelta = new Vector2(_trunkViewport.rect.width, totalH);

            _trunkCountText.text = (_relatedFilterCode != 0 ? "연관 " : "검색 ") + $"{_trunkResults.Count:N0}장";
            _trunkScroll.verticalNormalizedPosition = 1f;
            _lastFirstRow = -1;
            UpdateVirtualTrunkSlots(0, force: true);
        }

        private void OnTrunkScrolled(Vector2 scrollPos)
        {
            if (_totalRows <= 0) return;
            float scrollY = Mathf.Max(0f, _trunkContent.anchoredPosition.y);
            int firstRow = Mathf.Clamp(Mathf.FloorToInt(scrollY / _rowHeight), 0, Mathf.Max(0, _totalRows - PoolRows + 1));
            UpdateVirtualTrunkSlots(firstRow);
        }

        private void UpdateVirtualTrunkSlots(int firstRow, bool force = false)
        {
            if (firstRow == _lastFirstRow && !force) return;
            _lastFirstRow = firstRow;

            float vpWidth = _trunkViewport.rect.width > 10 ? _trunkViewport.rect.width : 560f;
            _colWidth = vpWidth / TrunkCols;

            for (int r = 0; r < PoolRows; r++)
            {
                int targetRow = firstRow + r;
                float y = -targetRow * _rowHeight;

                for (int c = 0; c < TrunkCols; c++)
                {
                    int poolIdx = r * TrunkCols + c;
                    var (rt, cv, sub, trig) = _trunkPool[poolIdx];

                    float x = c * _colWidth;
                    rt.anchoredPosition = new Vector2(x + 2f, y - 2f);
                    rt.sizeDelta = new Vector2(_colWidth - 4f, _rowHeight - 4f);

                    int cardIdx = targetRow * TrunkCols + c;
                    if (cardIdx >= _trunkResults.Count)
                    {
                        rt.gameObject.SetActive(false);
                        continue;
                    }

                    rt.gameObject.SetActive(true);
                    var ci = _trunkResults[cardIdx];
                    uint code = ci.code;
                    cv.SetCard(code);

                    int inDeck = CountInWorkingDeck(code);
                    sub.transform.parent.gameObject.SetActive(inDeck > 0);
                    sub.text = inDeck > 0 ? $"x{inDeck}" : "";

                    trig.CardCodeGetter = () => code;
                    trig.IndexGetter = () => cardIdx;
                    trig.OnPointerEnterAction = () => InspectCard(code);
                    trig.OnLeftClick = () => InspectCard(code);
                    trig.OnRightClick = () => { InspectCard(code); TryAddCardToDeck(code, _showingSide); };
                }
            }
        }

        private void RefreshDeckGrids()
        {
            _deckHeaderText.text = $"덱 {_userDeckIdx + 1}/{Mathf.Max(1, DeckRepository.UserDecks.Count)}";
            _mainCountText.text = $"메인 덱 ({_workingDeck.main.Count} / 40~60) — 드래그 이동 / 정렬 지원";
            _mainCountText.color = (_workingDeck.main.Count >= 40 && _workingDeck.main.Count <= 60) ? LotdUiKit.CyanAccent : LotdUiKit.RedAccent;
            _extraOrSideTitle.text = _showingSide
                ? $"사이드 덱 ({_workingDeck.side.Count} / 15)  — 드래그 이동 / 우클릭 제거"
                : $"엑스트라 덱 ({_workingDeck.extra.Count} / 15)  — 드래그 이동 / 우클릭 제거";

            SyncSlotList(_mainSlots, _workingDeck.main, DragSourceType.DeckMain);
            SyncSlotList(_extraSlots, _workingDeck.extra, DragSourceType.DeckExtra);
            SyncSlotList(_sideSlots, _workingDeck.side, DragSourceType.DeckSide);
        }

        private void SyncSlotList(List<(RectTransform rt, CompositeCardView card, CardDragTrigger trigger)> slots, List<uint> list, DragSourceType st)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var (rt, cv, trig) = slots[i];
                if (i >= list.Count)
                {
                    cv.gameObject.SetActive(false);
                    trig.CardCodeGetter = null;
                    trig.IndexGetter = null;
                    trig.OnLeftClick = null;
                    trig.OnRightClick = null;
                    trig.OnPointerEnterAction = null;
                    continue;
                }
                cv.gameObject.SetActive(true);
                uint code = list[i];
                int capIdx = i;
                cv.SetCard(code);

                trig.CardCodeGetter = () => code;
                trig.IndexGetter = () => capIdx;
                trig.OnPointerEnterAction = () => InspectCard(code);
                trig.OnLeftClick = () => InspectCard(code);
                trig.OnRightClick = () =>
                {
                    list.RemoveAt(capIdx);
                    LotdAudio.PlayClick();
                    RefreshDeckGrids();
                    UpdateVirtualTrunkSlots(_lastFirstRow, force: true);
                };
            }
        }

        private void HandleCardDrop(DragContext ctx)
        {
            if (Root == null || !Root.activeInHierarchy) return;

            // 1. Dropped on Main Deck Grid
            if (CardDragManager.ContainsScreenPoint(_mainGridRect, ctx.DropPosition))
            {
                if (ctx.SourceType == DragSourceType.Trunk)
                {
                    TryAddCardToDeck(ctx.CardCode, false);
                }
                else if (ctx.SourceType == DragSourceType.DeckSide)
                {
                    if (ctx.SourceIndex >= 0 && ctx.SourceIndex < _workingDeck.side.Count)
                    {
                        uint code = _workingDeck.side[ctx.SourceIndex];
                        _workingDeck.side.RemoveAt(ctx.SourceIndex);
                        _workingDeck.main.Add(code);
                        LotdAudio.PlayClick();
                        RefreshDeckGrids();
                    }
                }
                else if (ctx.SourceType == DragSourceType.DeckMain)
                {
                    // Reorder within Main Deck
                    int targetIdx = FindHoveredSlotIndex(_mainSlots, ctx.DropPosition);
                    if (targetIdx >= 0 && targetIdx < _workingDeck.main.Count && targetIdx != ctx.SourceIndex)
                    {
                        uint code = _workingDeck.main[ctx.SourceIndex];
                        _workingDeck.main.RemoveAt(ctx.SourceIndex);
                        _workingDeck.main.Insert(targetIdx, code);
                        LotdAudio.PlayClick();
                        RefreshDeckGrids();
                    }
                }
                return;
            }

            // 2. Dropped on Extra Deck Grid
            if (_extraGridHolder.activeSelf && CardDragManager.ContainsScreenPoint(_extraGridRect, ctx.DropPosition))
            {
                if (ctx.SourceType == DragSourceType.Trunk)
                {
                    TryAddCardToDeck(ctx.CardCode, false);
                }
                else if (ctx.SourceType == DragSourceType.DeckSide)
                {
                    if (ctx.SourceIndex >= 0 && ctx.SourceIndex < _workingDeck.side.Count)
                    {
                        uint code = _workingDeck.side[ctx.SourceIndex];
                        _workingDeck.side.RemoveAt(ctx.SourceIndex);
                        _workingDeck.extra.Add(code);
                        LotdAudio.PlayClick();
                        RefreshDeckGrids();
                    }
                }
                return;
            }

            // 3. Dropped on Side Deck Grid
            if (_sideGridHolder.activeSelf && CardDragManager.ContainsScreenPoint(_sideGridRect, ctx.DropPosition))
            {
                if (ctx.SourceType == DragSourceType.Trunk)
                {
                    TryAddCardToDeck(ctx.CardCode, true);
                }
                else if (ctx.SourceType == DragSourceType.DeckMain)
                {
                    if (ctx.SourceIndex >= 0 && ctx.SourceIndex < _workingDeck.main.Count)
                    {
                        uint code = _workingDeck.main[ctx.SourceIndex];
                        _workingDeck.main.RemoveAt(ctx.SourceIndex);
                        _workingDeck.side.Add(code);
                        LotdAudio.PlayClick();
                        RefreshDeckGrids();
                    }
                }
                return;
            }

            // 4. Dragged out of Deck onto Trunk / background -> Remove card from deck
            if (ctx.SourceType == DragSourceType.DeckMain && !CardDragManager.ContainsScreenPoint(_deckColRect, ctx.DropPosition))
            {
                if (ctx.SourceIndex >= 0 && ctx.SourceIndex < _workingDeck.main.Count)
                {
                    _workingDeck.main.RemoveAt(ctx.SourceIndex);
                    LotdAudio.PlayClick();
                    RefreshDeckGrids();
                    UpdateVirtualTrunkSlots(_lastFirstRow, force: true);
                    ShowToast("메인 덱에서 카드를 제거했습니다.");
                }
            }
            else if (ctx.SourceType == DragSourceType.DeckExtra && !CardDragManager.ContainsScreenPoint(_deckColRect, ctx.DropPosition))
            {
                if (ctx.SourceIndex >= 0 && ctx.SourceIndex < _workingDeck.extra.Count)
                {
                    _workingDeck.extra.RemoveAt(ctx.SourceIndex);
                    LotdAudio.PlayClick();
                    RefreshDeckGrids();
                    UpdateVirtualTrunkSlots(_lastFirstRow, force: true);
                    ShowToast("엑스트라 덱에서 카드를 제거했습니다.");
                }
            }
            else if (ctx.SourceType == DragSourceType.DeckSide && !CardDragManager.ContainsScreenPoint(_deckColRect, ctx.DropPosition))
            {
                if (ctx.SourceIndex >= 0 && ctx.SourceIndex < _workingDeck.side.Count)
                {
                    _workingDeck.side.RemoveAt(ctx.SourceIndex);
                    LotdAudio.PlayClick();
                    RefreshDeckGrids();
                    UpdateVirtualTrunkSlots(_lastFirstRow, force: true);
                    ShowToast("사이드 덱에서 카드를 제거했습니다.");
                }
            }
        }

        private static int FindHoveredSlotIndex(List<(RectTransform rt, CompositeCardView card, CardDragTrigger trigger)> slots, Vector2 screenPos)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (CardDragManager.ContainsScreenPoint(slots[i].rt, screenPos))
                    return i;
            }
            return -1;
        }

        private int CountInWorkingDeck(uint code)
        {
            var ci = CardDatabase.Get(code);
            uint targetKey = ci != null && ci.alias != 0 ? ci.alias : code;
            int cnt = 0;
            void CountList(List<uint> l)
            {
                foreach (var c in l)
                {
                    var cc = CardDatabase.Get(c);
                    uint k = cc != null && cc.alias != 0 ? cc.alias : c;
                    if (k == targetKey) cnt++;
                }
            }
            CountList(_workingDeck.main);
            CountList(_workingDeck.extra);
            CountList(_workingDeck.side);
            return cnt;
        }

        private void TryAddCardToDeck(uint code, bool addToSide)
        {
            var ci = CardDatabase.Get(code);
            if (ci == null) return;
            if (CountInWorkingDeck(code) >= 3)
            {
                ShowToast("동명 카드는 최대 3장까지 넣을 수 있습니다.");
                return;
            }
            if (addToSide)
            {
                if (_workingDeck.side.Count >= 15) { ShowToast("사이드 덱이 가득 찼습니다 (최대 15장)."); return; }
                _workingDeck.side.Add(code);
            }
            else if (ci.IsExtra)
            {
                if (_workingDeck.extra.Count >= 15) { ShowToast("엑스트라 덱이 가득 찼습니다 (최대 15장)."); return; }
                _workingDeck.extra.Add(code);
            }
            else
            {
                if (_workingDeck.main.Count >= 60) { ShowToast("메인 덱이 가득 찼습니다 (최대 60장)."); return; }
                _workingDeck.main.Add(code);
            }
            LotdAudio.PlayClick();
            RefreshDeckGrids();
            UpdateVirtualTrunkSlots(_lastFirstRow, force: true);
        }

        private void InspectCard(uint code)
        {
            _inspectedCode = code;
            var ci = CardDatabase.Get(code);
            _previewCard.SetCard(code);
            if (ci == null) return;

            _previewName.text = ci.name;
            _previewStats.text = ci.FormatStatsLineKR();
            _previewDesc.text = ci.desc;

            Sprite raceSp = null;
            if (ci.IsMonster) raceSp = LotdUiKit.GetRaceSprite(ci.race);
            else raceSp = LotdUiKit.LoadSprite($"LOTD/Icons/{YgoConstants.GetSpellTrapSubSpriteName(ci.type)}");
            _previewRaceIcon.gameObject.SetActive(raceSp != null);
            _previewRaceIcon.sprite = raceSp;
        }

        private void SwitchUserDeck(int delta)
        {
            if (DeckRepository.UserDecks.Count == 0) return;
            _userDeckIdx = (_userDeckIdx + delta + DeckRepository.UserDecks.Count) % DeckRepository.UserDecks.Count;
            LoadWorkingDeckFromCurrent();
            UpdateVirtualTrunkSlots(_lastFirstRow, force: true);
        }

        private void CreateNewUserDeck()
        {
            var nd = new UserDeck
            {
                id = $"custom_{DateTime.UtcNow:yyyyMMddHHmmssfff}",
                name = $"새 커스텀 덱 {DeckRepository.UserDecks.Count + 1}",
            };
            DeckRepository.SaveUserDeck(nd);
            _userDeckIdx = DeckRepository.UserDecks.Count - 1;
            LoadWorkingDeckFromCurrent();
            ShowToast("새 빈 덱을 생성했습니다.");
        }

        private void SaveCurrentDeck()
        {
            _workingDeck.name = string.IsNullOrWhiteSpace(_deckNameInput.text) ? "커스텀 덱" : _deckNameInput.text.Trim();
            DeckRepository.SaveUserDeck(_workingDeck);
            ShowToast($"'{_workingDeck.name}' 저장 완료!");
        }

        private void SortWorkingDeck()
        {
            int RankCard(uint code)
            {
                var c = CardDatabase.Get(code);
                if (c == null) return 9;
                if (c.IsMonster) return 0;
                if (c.IsSpell) return 1;
                return 2;
            }
            _workingDeck.main.Sort((a, b) =>
            {
                int ra = RankCard(a).CompareTo(RankCard(b));
                if (ra != 0) return ra;
                return string.Compare(CardDatabase.GetName(a), CardDatabase.GetName(b), StringComparison.Ordinal);
            });
            _workingDeck.extra.Sort((a, b) => string.Compare(CardDatabase.GetName(a), CardDatabase.GetName(b), StringComparison.Ordinal));
            _workingDeck.side.Sort((a, b) => string.Compare(CardDatabase.GetName(a), CardDatabase.GetName(b), StringComparison.Ordinal));
            RefreshDeckGrids();
            ShowToast("덱 정렬 완료");
        }

        private void ShuffleWorkingDeck()
        {
            if (_workingDeck.main.Count <= 1) return;
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                byte[] rb = new byte[4];
                for (int i = _workingDeck.main.Count - 1; i > 0; i--)
                {
                    rng.GetBytes(rb);
                    int j = (int)(BitConverter.ToUInt32(rb, 0) % (uint)(i + 1));
                    (_workingDeck.main[i], _workingDeck.main[j]) = (_workingDeck.main[j], _workingDeck.main[i]);
                }
            }
            LotdAudio.PlayDraw();
            RefreshDeckGrids();
            ShowToast("메인 덱 순서를 무작위로 섞었습니다.");
        }

        private void DeleteCurrentDeck()
        {
            if (DeckRepository.UserDecks.Count <= 1)
            {
                ShowToast("최소 1개의 덱은 유지해야 합니다.");
                return;
            }
            DeckRepository.DeleteUserDeck(_workingDeck.id);
            _userDeckIdx = Mathf.Clamp(_userDeckIdx, 0, DeckRepository.UserDecks.Count - 1);
            LoadWorkingDeckFromCurrent();
            ShowToast("덱을 삭제했습니다.");
        }

        private void ShowToast(string msg)
        {
            if (_toastText != null) _toastText.text = msg ?? "";
        }

        // ==================== PURE ARCHETYPE EXAMPLE DECK BROWSER ====================
        private void BuildArchetypeModal(Transform parent)
        {
            var dim = LotdUiKit.FillRect("Modal_ArchetypeBrowser", parent);
            _archModalRoot = dim.gameObject;
            LotdUiKit.AddImage(dim.gameObject, LotdUiKit.WhiteSprite, new Color(0f, 0.02f, 0.08f, 0.80f));
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => _archModalRoot.SetActive(false));

            var win = LotdUiKit.CreateBorderedPanel(dim, "Win",
                new Vector2(0.262f, 0.04f), new Vector2(0.985f, 0.905f), Vector2.zero, Vector2.zero,
                LotdUiKit.PanelNavy, LotdUiKit.GoldAccent);
            win.gameObject.AddComponent<Button>(); // block click-through to dim

            var hdr = LotdUiKit.CreateRect("Hdr", win, new Vector2(0f, 0.89f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(hdr.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelHeader);
            var title = LotdUiKit.AddText(hdr, "T", "📚 순수 카드군 예시 덱 선택 (PURE ARCHETYPE DECKS)", 19, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);
            title.rectTransform.offsetMin = new Vector2(16, 18);
            var sub = LotdUiKit.AddText(hdr, "Sub", "원하는 카드군을 선택하면 해당 순수 카드군 예시 덱(메인 40장 + 엑스트라 덱)이 자동 구성되며 보관함도 필터링됩니다.", 13, TextAnchor.LowerLeft, LotdUiKit.TextPrimary);
            sub.rectTransform.offsetMin = new Vector2(16, 4);

            var closeRt = LotdUiKit.CreateRect("Close", hdr, new Vector2(0.84f, 0.16f), new Vector2(0.985f, 0.84f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(closeRt, "B", "✕ 닫기", () => _archModalRoot.SetActive(false), 14);

            // Search & Sort row
            var filterRow = LotdUiKit.CreateRect("FilterRow", win, new Vector2(0.02f, 0.81f), new Vector2(0.98f, 0.875f), Vector2.zero, Vector2.zero);
            _archSearchInp = LotdUiKit.CreateInputField(
                LotdUiKit.CreateRect("ArchSearch", filterRow, new Vector2(0f, 0f), new Vector2(0.58f, 1f), Vector2.zero, Vector2.zero),
                "S", "카드군 이름 또는 대표 카드명 검색 (예: 티아라멘츠, 푸른 눈, 히어로, 상검, 섬도희...)", 14);
            _archSearchInp.onValueChanged.AddListener(v =>
            {
                _archSearchText = v ?? "";
                RefreshArchetypeModal();
            });

            var sortBtn = LotdUiKit.CreateLotdButton(
                LotdUiKit.CreateRect("SortArch", filterRow, new Vector2(0.60f, 0f), new Vector2(0.80f, 1f), Vector2.zero, Vector2.zero),
                "B", "정렬: 카드수/인기순", () =>
                {
                    _archSortMode = (_archSortMode + 1) % 2;
                    _archSortBtnText.text = _archSortMode == 0 ? "정렬: 카드수/인기순" : "정렬: 가나다순";
                    RefreshArchetypeModal();
                }, 13);
            _archSortBtnText = sortBtn.GetComponentInChildren<Text>();

            _archCountText = LotdUiKit.AddText(
                LotdUiKit.CreateRect("ArchCnt", filterRow, new Vector2(0.82f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero),
                "C", "0개 카드군", 14, TextAnchor.MiddleRight, LotdUiKit.CyanAccent, FontStyle.Bold);

            // Virtualized Scroll Grid (3 cols x 6 rows = 18 pooled tiles)
            var scrollHolder = LotdUiKit.CreateRect("ArchScrollHolder", win, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.795f), Vector2.zero, Vector2.zero);
            _archScroll = scrollHolder.gameObject.AddComponent<ScrollRect>();
            _archScroll.horizontal = false;
            _archScroll.vertical = true;
            _archScroll.scrollSensitivity = 36f;
            _archScroll.movementType = ScrollRect.MovementType.Clamped;

            _archViewport = LotdUiKit.FillRect("VP", scrollHolder);
            _archViewport.gameObject.AddComponent<RectMask2D>();
            _archScroll.viewport = _archViewport;

            _archContent = LotdUiKit.CreateRect("Content", _archViewport, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            _archContent.pivot = new Vector2(0.5f, 1f);
            _archScroll.content = _archContent;

            for (int r = 0; r < ArchPoolRows; r++)
            {
                for (int c = 0; c < ArchCols; c++)
                {
                    var tile = LotdUiKit.CreateBorderedPanel(_archContent, $"ArchTile_{r}_{c}",
                        new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero,
                        new Color(0.04f, 0.10f, 0.22f, 0.96f), LotdUiKit.CyanAccent);
                    tile.pivot = new Vector2(0, 1);

                    var thumbRt = LotdUiKit.CreateRect("Card", tile, new Vector2(0.03f, 0.06f), new Vector2(0.28f, 0.94f), Vector2.zero, Vector2.zero);
                    var cv = CompositeCardView.Create(thumbRt, "V", false);
                    var inspectImg = LotdUiKit.AddImage(thumbRt.gameObject, LotdUiKit.WhiteSprite, Color.clear);
                    var inspectBtn = thumbRt.gameObject.AddComponent<Button>();
                    inspectBtn.targetGraphic = inspectImg;

                    var tTxt = LotdUiKit.AddText(
                        LotdUiKit.CreateRect("Name", tile, new Vector2(0.31f, 0.62f), new Vector2(0.97f, 0.94f), Vector2.zero, Vector2.zero),
                        "N", "", 15, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);
                    tTxt.resizeTextForBestFit = true;
                    tTxt.resizeTextMinSize = 11;
                    tTxt.resizeTextMaxSize = 16;

                    var sTxt = LotdUiKit.AddText(
                        LotdUiKit.CreateRect("Info", tile, new Vector2(0.31f, 0.38f), new Vector2(0.97f, 0.62f), Vector2.zero, Vector2.zero),
                        "S", "", 11, TextAnchor.MiddleLeft, LotdUiKit.CyanAccent);

                    var btnRt = LotdUiKit.CreateRect("LoadBtn", tile, new Vector2(0.31f, 0.07f), new Vector2(0.96f, 0.36f), Vector2.zero, Vector2.zero);
                    var loadBtn = LotdUiKit.CreateLotdButton(btnRt, "B", "⚡ 순수 예시 덱 불러오기", null, 12, gold: true);

                    _archPool.Add((tile, cv, tTxt, sTxt, loadBtn, inspectBtn));
                }
            }

            _archScroll.onValueChanged.AddListener(_ => OnArchModalScrolled());
            _archModalRoot.SetActive(false);
        }

        private void OpenArchetypeModal()
        {
            LotdAudio.PlayClick();
            _archModalRoot.SetActive(true);
            RefreshArchetypeModal();
        }

        private void RefreshArchetypeModal()
        {
            _archResults.Clear();
            var all = CardDatabase.GetAllArchetypes();
            string q = (_archSearchText ?? "").Trim().ToLowerInvariant();

            foreach (var arch in all)
            {
                if (!string.IsNullOrEmpty(q))
                {
                    bool hit = arch.name.ToLowerInvariant().Contains(q) || arch.baseName.ToLowerInvariant().Contains(q);
                    if (!hit)
                    {
                        for (int i = 0; i < arch.coreCards.Count; i++)
                        {
                            if ((arch.coreCards[i].name ?? "").ToLowerInvariant().Contains(q))
                            {
                                hit = true;
                                break;
                            }
                        }
                    }
                    if (!hit) continue;
                }
                _archResults.Add(arch);
            }

            if (_archSortMode == 1)
                _archResults.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            else
                _archResults.Sort((a, b) =>
                {
                    int cmp = b.coreCards.Count.CompareTo(a.coreCards.Count);
                    if (cmp != 0) return cmp;
                    return b.TotalUniqueCount.CompareTo(a.TotalUniqueCount);
                });

            _archTotalRows = Mathf.Max(1, (_archResults.Count + ArchCols - 1) / ArchCols);
            float vpW = _archViewport.rect.width > 10 ? _archViewport.rect.width : 960f;
            _archContent.sizeDelta = new Vector2(vpW, _archTotalRows * _archRowHeight);
            _archCountText.text = $"총 {_archResults.Count:N0}개 카드군";
            _archScroll.verticalNormalizedPosition = 1f;
            _archLastFirstRow = -1;
            UpdateVirtualArchSlots(0, force: true);
        }

        private void OnArchModalScrolled()
        {
            if (_archTotalRows <= 0) return;
            float scrollY = Mathf.Max(0f, _archContent.anchoredPosition.y);
            int firstRow = Mathf.Clamp(Mathf.FloorToInt(scrollY / _archRowHeight), 0, Mathf.Max(0, _archTotalRows - ArchPoolRows + 1));
            UpdateVirtualArchSlots(firstRow);
        }

        private void UpdateVirtualArchSlots(int firstRow, bool force = false)
        {
            if (firstRow == _archLastFirstRow && !force) return;
            _archLastFirstRow = firstRow;

            float vpWidth = _archViewport.rect.width > 10 ? _archViewport.rect.width : 960f;
            float colW = vpWidth / ArchCols;

            for (int r = 0; r < ArchPoolRows; r++)
            {
                int targetRow = firstRow + r;
                float y = -targetRow * _archRowHeight;

                for (int c = 0; c < ArchCols; c++)
                {
                    int poolIdx = r * ArchCols + c;
                    var (rt, cv, titleTxt, subTxt, loadBtn, inspectBtn) = _archPool[poolIdx];

                    float x = c * colW;
                    rt.anchoredPosition = new Vector2(x + 4f, y - 4f);
                    rt.sizeDelta = new Vector2(colW - 8f, _archRowHeight - 8f);

                    int archIdx = targetRow * ArchCols + c;
                    if (archIdx >= _archResults.Count)
                    {
                        rt.gameObject.SetActive(false);
                        continue;
                    }

                    rt.gameObject.SetActive(true);
                    var arch = _archResults[archIdx];
                    cv.SetCard(arch.representativeCode);
                    titleTxt.text = $"[{arch.name}]";
                    subTxt.text = $"고유 카드 {arch.TotalUniqueCount}종 (메인 {arch.MainPoolCount} / EX {arch.ExtraPoolCount})";

                    inspectBtn.onClick.RemoveAllListeners();
                    inspectBtn.onClick.AddListener(() => InspectCard(arch.representativeCode));

                    loadBtn.onClick.RemoveAllListeners();
                    loadBtn.onClick.AddListener(() => ApplyPureArchetypeExampleDeck(arch));
                }
            }
        }

        private void BuildPureDeckFromInspectedCard()
        {
            if (_inspectedCode == 0) return;
            var arch = CardDatabase.FindArchetypeForCard(_inspectedCode);
            if (arch == null)
            {
                ShowToast("해당 카드의 순수 카드군 정보를 찾을 수 없습니다.");
                return;
            }
            ApplyPureArchetypeExampleDeck(arch);
        }

        private void ApplyPureArchetypeExampleDeck(ArchetypeInfo arch)
        {
            if (arch == null) return;
            var (main, extra) = CardDatabase.BuildPureArchetypeDeck(arch);
            _workingDeck.name = $"[순수] {arch.name}";
            _workingDeck.main = main;
            _workingDeck.extra = extra;
            _workingDeck.side.Clear();

            if (_deckNameInput != null)
                _deckNameInput.text = _workingDeck.name;

            SortWorkingDeck();

            _activeArchetypeFilter = arch;
            _relatedFilterCode = 0;
            _searchText = "";
            if (_searchInp != null) _searchInp.text = "";
            RefreshTrunkFilter();

            if (arch.representativeCode != 0)
                InspectCard(arch.representativeCode);

            if (_archModalRoot != null)
                _archModalRoot.SetActive(false);

            ShowToast($"✨ '[순수] {arch.name}' 예시 덱 (메인 {main.Count}장 / EX {extra.Count}장) 구성 완료!");
        }
    }
}
