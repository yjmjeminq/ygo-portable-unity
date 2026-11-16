using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DeckEditorUI
    {
        public GameObject Root { get; private set; }

        private readonly GameAppController _app;

        // Current edited deck
        private int _userDeckIdx = 0;
        private UserDeck _workingDeck;
        private uint _inspectedCode = 89631139;

        private static readonly string[] CardKinds = { "종류: 전체", "메인 몬스터", "마법", "함정", "엑스트라" };
        private static readonly string[] SortOptions = { "기본순", "공격력 높은 순", "레벨 높은 순", "이름순" };

        // Virtualized Trunk Scroll Grid
        private const int TrunkCols = 5;
        private float _rowHeight = 138f;
        private float _colWidth = 92f;
        private int _totalRows = 0;
        private int _lastFirstRow = -1;
        private Vector2 _trunkSize;

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

        // UI widgets
        private CompositeCardView _previewCard;
        private Image _previewRaceIcon;
        private Text _previewName;
        private Text _previewStats;
        private Text _previewDesc;
        private ScrollRect _previewScroll;

        private ScrollRect _trunkScroll;
        private RectTransform _trunkContent;
        private RectTransform _trunkViewport;
        private Text _trunkHeading;
        private Text _trunkCountText;
        private Button _showAllCards;
        private Text _noTrunkResults;
        private uint _relatedFilterCode = 0;
        private ArchetypeInfo _activeArchetypeFilter = null;
        private InputField _searchInp;
        private Dropdown _kindFilter;
        private Dropdown _sortFilter;

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
        private const int ArchPoolRows = 7;
        private float _archRowHeight = 124f;
        private int _archTotalRows = 0;
        private int _archLastFirstRow = -1;
        private readonly List<ArchetypeInfo> _archResults = new List<ArchetypeInfo>();
        private readonly List<(RectTransform rt, CompositeCardView card, Text titleTxt, Text subTxt, Button loadBtn, Button inspectBtn)> _archPool
            = new List<(RectTransform, CompositeCardView, Text, Text, Button, Button)>();

        private InputField _deckNameInput;
        private Text _deckHeaderText;
        private Text _mainCountText;
        private Text _extraCountText;
        private Text _sideCountText;
        private Text _toastText;
        private Text _saveButtonText;
        private GameObject _deckDialog;
        private Text _deckDialogTitle;
        private Text _deckDialogMessage;
        private Button _deckDialogPrimary;
        private Button _deckDialogSecondary;

        private RectTransform _trunkPanel;
        private RectTransform _mainGridRect;
        private RectTransform _extraGridRect;
        private RectTransform _sideGridRect;

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
            _cardFilterDialog.SetActive(false);
            if (DeckRepository.UserDecks.Count == 0)
                DeckRepository.Load(Application.streamingAssetsPath, Application.persistentDataPath);

            _userDeckIdx = Mathf.Clamp(_userDeckIdx, 0, Mathf.Max(0, DeckRepository.UserDecks.Count - 1));
            LoadWorkingDeckFromCurrent();
            RefreshTrunkFilter();
            InspectCard(_inspectedCode);
            ShowToast("");
        }

        public void Update()
        {
            if (!Root.activeInHierarchy) return;
            if (_trunkViewport.rect.size != _trunkSize)
                ResizeTrunkGrid(false);

            if (_deckDialog.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) _deckDialog.SetActive(false);
                return;
            }
            if (_cardFilterDialog.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) _cardFilterDialog.SetActive(false);
                return;
            }

            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return;
            if (Input.GetKeyDown(KeyCode.F))
            {
                var input = _archModalRoot.activeSelf ? _archSearchInp : _searchInp;
                EventSystem.current?.SetSelectedGameObject(input.gameObject);
                input.ActivateInputField();
            }
            if (Input.GetKeyDown(KeyCode.S) && !_archModalRoot.activeSelf)
                TrySaveCurrentDeck();
        }

        private void LoadWorkingDeckFromCurrent()
        {
            if (DeckRepository.UserDecks.Count == 0)
            {
                DeckRepository.SaveUserDeck(new UserDeck { name = "새 커스텀 덱" });
            }
            _workingDeck = CopyDeck(DeckRepository.UserDecks[_userDeckIdx]);
            _deckNameInput.SetTextWithoutNotify(_workingDeck.name);
            RefreshDeckGrids();
            UpdateVirtualTrunkSlots(_lastFirstRow, force: true);
        }

        private static UserDeck CopyDeck(UserDeck deck)
        {
            return new UserDeck
            {
                id = deck.id,
                name = deck.name,
                main = new List<uint>(deck.main),
                extra = new List<uint>(deck.extra),
                side = new List<uint>(deck.side),
            };
        }

        private void BuildUI(Transform parent)
        {
            var root = LotdUiKit.FillRect("Screen_DeckEditor", parent);
            Root = root.gameObject;
            LotdUiKit.AddImage(LotdUiKit.FillRect("DeckBackground", root).gameObject,
                LotdUiKit.LoadSprite("LOTD/Background/bg_vrains"), new Color(0.86f, 0.53f, 0.61f), false, false);
            LotdUiKit.AddImage(LotdUiKit.FillRect("Backdrop", root).gameObject,
                LotdUiKit.WhiteSprite, new Color(0.12f, 0.014f, 0.033f, 0.60f), false, false);
            LotdUiKit.AddMenuHeader(root, "덱 편집");

            var examples = LotdUiKit.CreateRect("Examples", root, new Vector2(0.765f, 0.918f), new Vector2(0.871f, 0.976f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(examples, "Button", "카드군 예시 덱", OpenArchetypeModal, 15);
            var back = LotdUiKit.CreateRect("Back", root, new Vector2(0.878f, 0.918f), new Vector2(0.985f, 0.976f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(back, "Button", "메인 메뉴",
                () => BeforeReplacingEdits(() => _app.ShowScreen(AppScreen.MainMenu)), 15);

            BuildTrunkPanel(root);
            BuildCardDetails(root);
            BuildDeckPanel(root);

            var footer = LotdUiKit.CreateRect("Controls", root, new Vector2(0.015f, 0.002f), new Vector2(0.985f, 0.041f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(footer.gameObject, LotdUiKit.WhiteSprite, new Color(0.02f, 0.045f, 0.10f, 0.92f), false, false);
            LotdUiKit.AddText(LotdUiKit.CreateRect("Help", footer, new Vector2(0.01f, 0f), new Vector2(0.53f, 1f),
                Vector2.zero, Vector2.zero), "Text", "드래그  이동    우클릭  추가 / 제거    Ctrl+F  검색    Ctrl+S  저장", 13,
                TextAnchor.MiddleLeft, LotdUiKit.TextPrimary);
            _toastText = LotdUiKit.AddText(LotdUiKit.CreateRect("Status", footer, new Vector2(0.54f, 0f), new Vector2(0.99f, 1f),
                Vector2.zero, Vector2.zero), "Text", "", 14, TextAnchor.MiddleRight, LotdUiKit.GoldAccent, FontStyle.Bold);
            _toastText.resizeTextForBestFit = true;
            _toastText.resizeTextMinSize = 10;
            _toastText.resizeTextMaxSize = 14;
            BuildArchetypeModal(root);
            BuildDeckDialog(root);
            BuildDetailedSearch(root);
        }

        private void BuildCardDetails(Transform root)
        {
            var panel = LotdUiKit.CreateMenuPanel(root, "CardDetails",
                new Vector2(0.301f, 0.053f), new Vector2(0.553f, 0.899f));
            var card = LotdUiKit.CreateRect("LargeCard", panel,
                new Vector2(0.085f, 0.578f), new Vector2(0.915f, 0.979f), Vector2.zero, Vector2.zero);
            _previewCard = CompositeCardView.Create(card, "Preview", true);

            var stats = LotdUiKit.CreateRect("Stats", panel,
                new Vector2(0f, 0.493f), new Vector2(1f, 0.577f), new Vector2(17f, 0f), new Vector2(-17f, 0f));
            LotdUiKit.AddImage(stats.gameObject, LotdUiKit.LoadSprite("LOTD/HUD/cardbar_texture"),
                new Color(0.86f, 0.68f, 0.38f, 0.86f), false, false);
            var icon = LotdUiKit.CreateRect("TypeIcon", stats, new Vector2(0.83f, 0.06f), new Vector2(0.975f, 0.94f),
                Vector2.zero, Vector2.zero);
            _previewRaceIcon = LotdUiKit.AddImage(icon.gameObject, null, Color.white, true, false);
            _previewStats = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Info", stats,
                new Vector2(0.03f, 0f), new Vector2(0.81f, 1f), Vector2.zero, Vector2.zero),
                "Text", "", 14, 9, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            _previewName = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Name", panel,
                new Vector2(0f, 0.427f), new Vector2(1f, 0.492f), new Vector2(17f, 0f), new Vector2(-17f, 0f)),
                "Text", "", 20, 10, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);

            var desc = LotdUiKit.CreateRect("Description", panel,
                new Vector2(0f, 0.155f), new Vector2(1f, 0.427f), new Vector2(17f, 0f), new Vector2(-17f, 0f));
            LotdUiKit.AddImage(desc.gameObject, LotdUiKit.WhiteSprite, new Color(0.025f, 0.067f, 0.118f, 0.64f));
            var viewport = LotdUiKit.CreateRect("Viewport", desc, Vector2.zero, Vector2.one,
                new Vector2(8f, 5f), new Vector2(-21f, -5f));
            viewport.gameObject.AddComponent<RectMask2D>();
            _previewDesc = LotdUiKit.AddText(viewport, "Content", "", 16, TextAnchor.UpperLeft, LotdUiKit.TextPrimary);
            _previewDesc.supportRichText = false;
            _previewDesc.verticalOverflow = VerticalWrapMode.Overflow;
            _previewDesc.lineSpacing = 1.08f;
            var content = _previewDesc.rectTransform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _previewScroll = desc.gameObject.AddComponent<ScrollRect>();
            _previewScroll.viewport = viewport;
            _previewScroll.content = content;
            _previewScroll.horizontal = false;
            _previewScroll.scrollSensitivity = 32f;
            _previewScroll.movementType = ScrollRect.MovementType.Clamped;
            _previewScroll.verticalScrollbar = LotdUiKit.AddVerticalScrollbar(desc);
            _previewScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            var related = LotdUiKit.CreateRect("RelatedCards", panel,
                new Vector2(0.035f, 0.087f), new Vector2(0.965f, 0.145f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(related, "Button", "관련 카드", () => FilterRelatedCards(_inspectedCode), 14);
            var add = LotdUiKit.CreateRect("AddCard", panel,
                new Vector2(0.035f, 0.020f), new Vector2(0.965f, 0.080f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(add, "Button", "덱에 추가", () => TryAddCardToDeck(_inspectedCode), 14, true);
        }

        private void BuildTrunkPanel(Transform rt)
        {
            var col = LotdUiKit.CreateMenuPanel(rt, "Trunk",
                new Vector2(0.015f, 0.053f), new Vector2(0.296f, 0.899f));
            _trunkPanel = col;

            var caption = LotdUiKit.CreateRect("TrunkHeading", col, new Vector2(0.034f, 0.929f), new Vector2(0.97f, 0.986f),
                Vector2.zero, Vector2.zero);
            var heading = LotdUiKit.CreateRect("Title", caption, Vector2.zero, new Vector2(0.56f, 1f),
                Vector2.zero, Vector2.zero);
            _trunkHeading = LotdUiKit.AddText(heading, "Label", "보관함", 19, TextAnchor.MiddleLeft,
                LotdUiKit.CyanAccent, FontStyle.Bold);
            _trunkHeading.resizeTextForBestFit = true;
            _trunkHeading.resizeTextMinSize = 12;
            _trunkHeading.resizeTextMaxSize = 19;
            var showAll = LotdUiKit.CreateRect("ShowAll", caption, new Vector2(.57f, .095f), new Vector2(.82f, .905f),
                Vector2.zero, Vector2.zero);
            _showAllCards = LotdUiKit.CreateLotdButton(showAll, "Button", "전체 카드", ExitTrunkScope, 12);
            var count = LotdUiKit.CreateRect("Total", caption, new Vector2(.835f, 0f), Vector2.one,
                Vector2.zero, Vector2.zero);
            _trunkCountText = LotdUiKit.AddFittedText(count, "Count", "", 16, 9, TextAnchor.MiddleRight, Color.white, FontStyle.Bold);

            var searchRow = LotdUiKit.CreateRect("SearchRow", col, new Vector2(0.034f, 0.874f),
                new Vector2(0.97f, 0.926f), Vector2.zero, Vector2.zero);
            var search = LotdUiKit.CreateRect("SearchBox", searchRow, Vector2.zero, new Vector2(0.79f, 1f),
                Vector2.zero, Vector2.zero);
            _searchInp = LotdUiKit.CreateInputField(search, "Search", "카드명·효과·카드 번호", 15);
            _searchInp.onValueChanged.AddListener(_ => RefreshTrunkFilter());
            var reset = LotdUiKit.CreateRect("ResetFilters", searchRow, new Vector2(0.81f, 0f), Vector2.one,
                Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(reset, "Button", "초기화", () => SetTrunkSource(), 13);

            var filters = LotdUiKit.CreateRect("Filters", col, new Vector2(0.034f, 0.816f), new Vector2(0.97f, 0.867f),
                Vector2.zero, Vector2.zero);
            Dropdown AddFilter(string name, float left, float right, IReadOnlyList<string> choices, Action<int> changed)
            {
                var slot = LotdUiKit.CreateRect(name, filters, new Vector2(left, 0f), new Vector2(right, 1f),
                    Vector2.zero, Vector2.zero);
                return LotdUiKit.CreateLotdDropdown(slot, "Dropdown", choices, changed);
            }
            _kindFilter = AddFilter("Kind", 0f, 0.32f, CardKinds, ChangeTrunkKind);
            var advanced = LotdUiKit.CreateRect("DetailedSearch", filters, new Vector2(.337f, 0f),
                new Vector2(.675f, 1f), Vector2.zero, Vector2.zero);
            _detailButtonText = LotdUiKit.CreateLotdButton(advanced, "Button", "상세 검색", OpenDetailedSearch, 14, true)
                .GetComponentInChildren<Text>();
            _sortFilter = AddFilter("Sort", 0.69f, 1f, SortOptions, _ => RefreshTrunkFilter());

            var scrollHolder = LotdUiKit.CreateRect("TrunkScrollHolder", col,
                new Vector2(0.034f, 0.035f), new Vector2(0.97f, 0.805f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(scrollHolder.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            _trunkScroll = scrollHolder.gameObject.AddComponent<ScrollRect>();
            _trunkScroll.horizontal = false;
            _trunkScroll.scrollSensitivity = 40f;
            _trunkScroll.movementType = ScrollRect.MovementType.Clamped;

            _trunkViewport = LotdUiKit.CreateRect("Viewport", scrollHolder, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-17f, 0f));
            _trunkViewport.gameObject.AddComponent<RectMask2D>();
            _trunkScroll.viewport = _trunkViewport;
            _trunkContent = LotdUiKit.CreateRect("Content", _trunkViewport, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
            _trunkContent.pivot = new Vector2(0f, 1f);
            _trunkScroll.content = _trunkContent;

            var scrollbar = LotdUiKit.AddVerticalScrollbar(scrollHolder);
            scrollbar.size = 0.075f;
            scrollbar.SetValueWithoutNotify(1f);
            scrollbar.onValueChanged.AddListener(value => _trunkScroll.verticalNormalizedPosition = value);
            _trunkScroll.onValueChanged.AddListener(position =>
            {
                scrollbar.SetValueWithoutNotify(position.y);
                OnTrunkScrolled();
            });
            _noTrunkResults = LotdUiKit.AddText(scrollHolder, "Empty", "조건에 맞는 카드가 없습니다.",
                17, TextAnchor.MiddleCenter, LotdUiKit.TextMuted);
        }

        private void EnsureTrunkPool(int count)
        {
            while (_trunkPool.Count < count)
            {
                var slot = LotdUiKit.CreateRect("TrunkCard", _trunkContent, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero);
                slot.pivot = new Vector2(0f, 1f);
                var hit = LotdUiKit.AddImage(slot.gameObject, LotdUiKit.WhiteSprite, Color.clear);
                var card = CompositeCardView.Create(LotdUiKit.FillRect("CardInset", slot, 1.8f), "Card");
                var countBox = LotdUiKit.CreateRect("Copies", slot, new Vector2(0.30f, 0.018f), new Vector2(0.70f, 0.22f),
                    Vector2.zero, Vector2.zero);
                var label = LotdUiKit.AddText(countBox, "Count", "", 18, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                label.gameObject.AddComponent<Outline>().effectColor = Color.black;
                var trigger = slot.gameObject.AddComponent<CardDragTrigger>();
                trigger.SourceType = DragSourceType.Trunk;
                trigger.OnPointerExitAction = () => hit.color = Color.clear;
                _trunkPool.Add((slot, card, label, trigger));
            }
        }

        private void BuildDeckPanel(Transform root)
        {
            var panel = LotdUiKit.CreateMenuPanel(root, "Deck",
                new Vector2(0.558f, 0.053f), new Vector2(0.985f, 0.899f), true);

            var header = LotdUiKit.CreateRect("DeckControls", panel,
                new Vector2(0.025f, 0.928f), new Vector2(0.975f, 0.985f), Vector2.zero, Vector2.zero);
            Button Control(Transform row, string name, string label, float x, float width, Action action, bool gold = false)
            {
                var holder = LotdUiKit.CreateRect(name, row, new Vector2(x, 0f), new Vector2(x + width, 1f),
                    Vector2.zero, Vector2.zero);
                return LotdUiKit.CreateLotdButton(holder, "Button", label, action, 14, gold);
            }

            Control(header, "Previous", "◀", 0f, 0.081f, () => SwitchUserDeck(-1));
            _deckHeaderText = LotdUiKit.AddText(LotdUiKit.CreateRect("DeckNumber", header, new Vector2(0.083f, 0f),
                new Vector2(0.235f, 1f), Vector2.zero, Vector2.zero),
                "Text", "", 15, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);
            Control(header, "Next", "▶", 0.24f, 0.081f, () => SwitchUserDeck(1));
            Control(header, "New", "새 덱", 0.34f, 0.18f, CreateNewUserDeck);
            _saveButtonText = Control(header, "Save", "저장", 0.53f, 0.21f,
                () => TrySaveCurrentDeck(), true).GetComponentInChildren<Text>();
            Control(header, "Delete", "삭제", 0.76f, 0.24f, DeleteCurrentDeck);

            var edit = LotdUiKit.CreateRect("EditControls", panel,
                new Vector2(0.025f, 0.867f), new Vector2(0.975f, 0.916f), Vector2.zero, Vector2.zero);
            _deckNameInput = LotdUiKit.CreateInputField(LotdUiKit.CreateRect("DeckName", edit, Vector2.zero,
                new Vector2(0.579f, 1f), Vector2.zero, Vector2.zero), "Input", "덱 이름", 15);
            _deckNameInput.onValueChanged.AddListener(_ => RefreshSaveLabel());
            Control(edit, "Sort", "정렬", 0.595f, 0.19f, SortWorkingDeck);
            Control(edit, "Shuffle", "셔플", 0.8f, 0.2f, ShuffleWorkingDeck);

            (_mainGridRect, _mainCountText) = BuildDeckSection(panel, "Main", "메인", 0.830f, 0.397f);
            (_extraGridRect, _extraCountText) = BuildDeckSection(panel, "Extra", "엑스트라", 0.350f, 0.224f);
            (_sideGridRect, _sideCountText) = BuildDeckSection(panel, "Side", "사이드", 0.176f, 0.047f);
            BuildDeckSlots(_mainGridRect, _mainSlots, 13, 5, 60, DragSourceType.DeckMain);
            BuildDeckSlots(_extraGridRect, _extraSlots, 15, 1, 15, DragSourceType.DeckExtra);
            BuildDeckSlots(_sideGridRect, _sideSlots, 15, 1, 15, DragSourceType.DeckSide);
        }

        private static (RectTransform grid, Text count) BuildDeckSection(Transform panel, string name, string title,
            float top, float bottom)
        {
            var label = LotdUiKit.CreateRect(name + "Tab", panel, new Vector2(0.026f, top),
                new Vector2(0.191f, top + 0.033f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(label.gameObject, LotdUiKit.LoadSprite("LOTD/Shared/tab-title-box"), Color.white, false, false);
            LotdUiKit.AddText(label, "Label", title, 14, TextAnchor.MiddleCenter,
                new Color(0.02f, 0.09f, 0.17f), FontStyle.Bold);
            var count = LotdUiKit.AddText(LotdUiKit.CreateRect(name + "Count", panel,
                new Vector2(0.6f, top), new Vector2(0.972f, top + 0.034f), Vector2.zero, Vector2.zero),
                "Text", "", 16, TextAnchor.MiddleRight, Color.white, FontStyle.Bold);
            var grid = LotdUiKit.CreateRect(name + "Grid", panel, new Vector2(0.026f, bottom),
                new Vector2(0.974f, top), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(grid.gameObject, LotdUiKit.WhiteSprite, new Color(0.08f, 0.35f, 0.47f, 0.68f));
            return (grid, count);
        }

        private static void BuildDeckSlots(Transform parent, List<(RectTransform rt, CompositeCardView card, CardDragTrigger trigger)> target,
            int cols, int rows, int capacity, DragSourceType source)
        {
            for (int i = 0; i < capacity; i++)
            {
                int row = i / cols;
                int col = i % cols;
                var slot = LotdUiKit.CreateRect($"Card_{i}", parent, new Vector2(col / (float)cols, 1f - (row + 1f) / rows),
                    new Vector2((col + 1f) / cols, 1f - row / (float)rows), new Vector2(1.2f, 1.2f), new Vector2(-1.2f, -1.2f));
                LotdUiKit.AddImage(slot.gameObject, LotdUiKit.WhiteSprite, Color.clear);
                var card = CompositeCardView.Create(LotdUiKit.FillRect("CardInset", slot, 1.8f), "Card");
                var trigger = slot.gameObject.AddComponent<CardDragTrigger>();
                trigger.SourceType = source;
                target.Add((slot, card, trigger));
            }
        }

        private void FilterRelatedCards(uint code)
        {
            if (code == 0) return;
            SetTrunkSource(relatedCode: code);
        }

        private void SetTrunkSource(uint relatedCode = 0, ArchetypeInfo archetype = null)
        {
            _relatedFilterCode = relatedCode;
            _activeArchetypeFilter = archetype;
            _searchInp.SetTextWithoutNotify("");
            _kindFilter.SetValueWithoutNotify(0);
            _sortFilter.SetValueWithoutNotify(0);
            ClearDetailedSearch();
            RefreshTrunkFilter();
        }

        private void ExitTrunkScope()
        {
            _relatedFilterCode = 0;
            _activeArchetypeFilter = null;
            RefreshTrunkFilter();
        }

        private void RefreshTrunkFilter()
        {
            _trunkResults.Clear();
            var filter = CreateTrunkFilter(_searchInp.text, (DeckCardKind)_kindFilter.value);
            UpdateDetailedSearchButton();

            IEnumerable<CardInfo> source = CardDatabase.AllCards;
            _showAllCards.transform.parent.gameObject.SetActive(_activeArchetypeFilter != null || _relatedFilterCode != 0);
            _trunkHeading.text = "보관함";
            _trunkHeading.color = LotdUiKit.CyanAccent;
            if (_activeArchetypeFilter != null)
            {
                source = _activeArchetypeFilter.coreCards.Union(_activeArchetypeFilter.supportCards);
                _trunkHeading.text = $"카드군 · {_activeArchetypeFilter.name}";
                _trunkHeading.color = LotdUiKit.GoldAccent;
            }
            else if (_relatedFilterCode != 0)
            {
                source = CardDatabase.GetRelatedCards(_relatedFilterCode);
                _trunkHeading.text = $"관련 카드 · {CardDatabase.GetName(_relatedFilterCode)}";
                _trunkHeading.color = LotdUiKit.GoldAccent;
            }

            foreach (var card in source)
                if (filter.Matches(card)) _trunkResults.Add(card);

            int sort = _sortFilter.value;
            if (sort == 1) _trunkResults.Sort((a, b) => b.atk.CompareTo(a.atk));
            else if (sort == 2) _trunkResults.Sort((a, b) => b.level.CompareTo(a.level));
            else if (sort == 3) _trunkResults.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

            Canvas.ForceUpdateCanvases();
            _trunkCountText.text = $"{_trunkResults.Count:N0}장";
            _noTrunkResults.gameObject.SetActive(_trunkResults.Count == 0);
            ResizeTrunkGrid(true);
        }

        private void ResizeTrunkGrid(bool startAtTop)
        {
            _trunkSize = _trunkViewport.rect.size;
            float scroll = startAtTop ? 1f : _trunkScroll.verticalNormalizedPosition;
            float vpWidth = _trunkViewport.rect.width > 10 ? _trunkViewport.rect.width : 405f;
            _colWidth = vpWidth / TrunkCols;
            _rowHeight = Mathf.Max(20f, Mathf.Round((_colWidth - 4f) / LotdUiKit.CardAspectRatio + 4f));

            _totalRows = Mathf.Max(1, (_trunkResults.Count + TrunkCols - 1) / TrunkCols);
            int rows = Mathf.CeilToInt(_trunkViewport.rect.height / _rowHeight) + 2;
            EnsureTrunkPool(Mathf.Min(_totalRows, rows) * TrunkCols);
            _trunkContent.sizeDelta = new Vector2(0f, _totalRows * _rowHeight);

            _trunkScroll.StopMovement();
            _trunkScroll.verticalNormalizedPosition = scroll;
            _lastFirstRow = -1;
            OnTrunkScrolled();
        }

        private void OnTrunkScrolled()
        {
            if (_totalRows == 0)
            {
                UpdateVirtualTrunkSlots(0);
                return;
            }
            float distance = Mathf.Max(0f, _trunkContent.anchoredPosition.y);
            int first = Mathf.Clamp(Mathf.FloorToInt(distance / _rowHeight), 0, _totalRows - 1);
            UpdateVirtualTrunkSlots(first);
        }

        private void UpdateVirtualTrunkSlots(int firstRow, bool force = false)
        {
            firstRow = Mathf.Max(0, firstRow);
            if (firstRow == _lastFirstRow && !force) return;
            _lastFirstRow = firstRow;

            for (int i = 0; i < _trunkPool.Count; i++)
            {
                var (rect, view, count, trigger) = _trunkPool[i];
                int row = firstRow + i / TrunkCols;
                int col = i % TrunkCols;
                int index = row * TrunkCols + col;
                if (index >= _trunkResults.Count)
                {
                    rect.gameObject.SetActive(false);
                    continue;
                }

                rect.anchoredPosition = new Vector2(col * _colWidth + 2f, -row * _rowHeight - 2f);
                rect.sizeDelta = new Vector2(_colWidth - 4f, _rowHeight - 4f);
                rect.gameObject.SetActive(true);
                uint code = _trunkResults[index].code;
                view.SetCard(code);

                int copies = CountInWorkingDeck(code);
                count.transform.parent.gameObject.SetActive(copies > 0);
                count.text = copies.ToString();
                var highlight = rect.GetComponent<Image>();
                highlight.color = Color.clear;

                trigger.CardCodeGetter = () => code;
                trigger.IndexGetter = () => index;
                trigger.OnPointerEnterAction = () =>
                {
                    highlight.color = LotdUiKit.GoldAccent;
                    InspectCard(code);
                };
                trigger.OnLeftClick = () => InspectCard(code);
                trigger.OnRightClick = () => TryAddCardToDeck(code);
            }
        }

        private void RefreshDeckGrids()
        {
            _deckHeaderText.text = $"{_userDeckIdx + 1} / {Mathf.Max(1, DeckRepository.UserDecks.Count)}";
            _mainCountText.text = $"{_workingDeck.main.Count}장";
            _mainCountText.color = _workingDeck.main.Count >= 40 && _workingDeck.main.Count <= 60
                ? Color.white : new Color(1f, 0.55f, 0.45f);
            _extraCountText.text = $"{_workingDeck.extra.Count}장";
            _sideCountText.text = $"{_workingDeck.side.Count}장";
            SyncSlotList(_mainSlots, _workingDeck.main);
            SyncSlotList(_extraSlots, _workingDeck.extra);
            SyncSlotList(_sideSlots, _workingDeck.side);
            RefreshSaveLabel();
        }

        private void SyncSlotList(List<(RectTransform rt, CompositeCardView card, CardDragTrigger trigger)> slots, List<uint> cards)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var (rect, view, trigger) = slots[i];
                var highlight = rect.GetComponent<Image>();
                highlight.color = Color.clear;
                bool filled = i < cards.Count;
                view.gameObject.SetActive(filled);
                trigger.CardCodeGetter = null;
                trigger.IndexGetter = null;
                trigger.OnLeftClick = null;
                trigger.OnRightClick = null;
                trigger.OnPointerEnterAction = null;
                trigger.OnPointerExitAction = () => highlight.color = Color.clear;
                if (!filled) continue;

                uint code = cards[i];
                int index = i;
                view.SetCard(code);
                trigger.CardCodeGetter = () => code;
                trigger.IndexGetter = () => index;
                trigger.OnLeftClick = () => InspectCard(code);
                trigger.OnPointerEnterAction = () =>
                {
                    highlight.color = LotdUiKit.GoldAccent;
                    InspectCard(code);
                };
                trigger.OnRightClick = () =>
                {
                    cards.RemoveAt(index);
                    FinishDeckEdit();
                };
            }
        }

        private void HandleCardDrop(DragContext context)
        {
            if (!Root.activeInHierarchy || _workingDeck == null || _deckDialog.activeSelf || _archModalRoot.activeSelf) return;
            var destination = FindDropZone(context.DropPosition);
            if (context.SourceType == DragSourceType.Trunk)
            {
                if (destination != DragSourceType.None) TryAddCardToDeck(context.CardCode, destination);
                return;
            }

            var source = GetDeckList(context.SourceType);
            if (source == null || context.SourceIndex < 0 || context.SourceIndex >= source.Count ||
                source[context.SourceIndex] != context.CardCode)
                return;

            if (destination == DragSourceType.None)
            {
                if (CardDragManager.ContainsScreenPoint(_trunkPanel, context.DropPosition))
                {
                    source.RemoveAt(context.SourceIndex);
                    FinishDeckEdit();
                }
                return;
            }

            var target = GetDeckList(destination);
            if (source != target && !CanPlaceCard(CardDatabase.Get(context.CardCode), destination, target)) return;
            int index = FindHoveredSlotIndex(GetDeckSlots(destination), context.DropPosition);
            if (source == target && index < 0) return;
            if (source == target && index == context.SourceIndex) return;
            source.RemoveAt(context.SourceIndex);
            target.Insert(index < 0 ? target.Count : Mathf.Min(index, target.Count), context.CardCode);
            FinishDeckEdit();
        }

        private DragSourceType FindDropZone(Vector2 point)
        {
            if (CardDragManager.ContainsScreenPoint(_mainGridRect, point)) return DragSourceType.DeckMain;
            if (CardDragManager.ContainsScreenPoint(_extraGridRect, point)) return DragSourceType.DeckExtra;
            if (CardDragManager.ContainsScreenPoint(_sideGridRect, point)) return DragSourceType.DeckSide;
            return DragSourceType.None;
        }

        private List<uint> GetDeckList(DragSourceType zone)
        {
            return zone switch
            {
                DragSourceType.DeckMain => _workingDeck.main,
                DragSourceType.DeckExtra => _workingDeck.extra,
                DragSourceType.DeckSide => _workingDeck.side,
                _ => null,
            };
        }

        private List<(RectTransform rt, CompositeCardView card, CardDragTrigger trigger)> GetDeckSlots(DragSourceType zone)
        {
            return zone switch
            {
                DragSourceType.DeckMain => _mainSlots,
                DragSourceType.DeckExtra => _extraSlots,
                _ => _sideSlots,
            };
        }

        private void FinishDeckEdit()
        {
            LotdAudio.PlayClick();
            RefreshDeckGrids();
            UpdateVirtualTrunkSlots(_lastFirstRow, force: true);
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

        private void TryAddCardToDeck(uint code, DragSourceType destination = DragSourceType.None)
        {
            var card = CardDatabase.Get(code);
            if (card == null || _workingDeck == null) return;
            if (CountInWorkingDeck(code) >= 3)
            {
                ShowToast("동명 카드는 세 덱을 합해 최대 3장입니다.");
                return;
            }

            if (destination == DragSourceType.None)
                destination = card.IsExtra ? DragSourceType.DeckExtra : DragSourceType.DeckMain;
            var target = GetDeckList(destination);
            if (!CanPlaceCard(card, destination, target)) return;
            target.Add(code);
            FinishDeckEdit();
        }

        private bool CanPlaceCard(CardInfo card, DragSourceType destination, List<uint> target)
        {
            if (card == null || target == null || (card.type & YgoConstants.TYPE_TOKEN) != 0) return false;
            if (destination != DragSourceType.DeckSide && card.IsExtra != (destination == DragSourceType.DeckExtra))
            {
                ShowToast(card.IsExtra ? "엑스트라 또는 사이드 덱에 넣으세요." : "메인 또는 사이드 덱에 넣으세요.");
                return false;
            }

            int capacity = destination == DragSourceType.DeckMain ? 60 : 15;
            if (target.Count < capacity) return true;
            ShowToast($"이 덱은 {capacity}장까지 담을 수 있습니다.");
            return false;
        }

        private void InspectCard(uint code)
        {
            _inspectedCode = code;
            if (_previewCard.CurrentCode == code) return;
            var ci = CardDatabase.Get(code);
            _previewCard.SetCard(code);

            _previewName.text = ci?.name ?? "";
            _previewStats.text = ci?.FormatStatsLineKR() ?? "";
            _previewDesc.text = ci?.desc ?? "";
            _previewScroll.StopMovement();
            _previewScroll.content.anchoredPosition = Vector2.zero;

            Sprite raceSp = null;
            if (ci != null)
                raceSp = ci.IsMonster ? LotdUiKit.GetRaceSprite(ci.race) : LotdUiKit.LoadSprite($"LOTD/Icons/{YgoConstants.GetSpellTrapSubSpriteName(ci.type)}");
            _previewRaceIcon.gameObject.SetActive(raceSp != null);
            _previewRaceIcon.sprite = raceSp;
        }

        private void SwitchUserDeck(int delta)
        {
            int count = DeckRepository.UserDecks.Count;
            if (count <= 1) return;
            BeforeReplacingEdits(() =>
            {
                _userDeckIdx = (_userDeckIdx + delta + count) % count;
                LoadWorkingDeckFromCurrent();
            });
        }

        private void CreateNewUserDeck()
        {
            BeforeReplacingEdits(() =>
            {
                try
                {
                    DeckRepository.SaveUserDeck(new UserDeck { name = $"새 커스텀 덱 {DeckRepository.UserDecks.Count + 1}" });
                    _userDeckIdx = DeckRepository.UserDecks.Count - 1;
                    LoadWorkingDeckFromCurrent();
                    ShowToast("새 빈 덱을 생성했습니다.");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"[DeckEditor] Could not create a deck: {ex.Message}");
                    ShowToast("덱을 만들 수 없습니다. 저장 폴더를 확인하세요.");
                }
            });
        }

        private string DeckNameForSave => string.IsNullOrWhiteSpace(_deckNameInput.text)
            ? "커스텀 덱" : _deckNameInput.text.Trim();

        private bool HasUnsavedChanges()
        {
            if (_workingDeck == null) return false;
            var saved = DeckRepository.UserDecks.FirstOrDefault(deck => deck.id == _workingDeck.id);
            return saved == null || DeckNameForSave != saved.name ||
                !_workingDeck.main.SequenceEqual(saved.main) || !_workingDeck.extra.SequenceEqual(saved.extra) ||
                !_workingDeck.side.SequenceEqual(saved.side);
        }

        private void RefreshSaveLabel()
        {
            _saveButtonText.text = HasUnsavedChanges() ? "저장 *" : "저장";
        }

        private bool TrySaveCurrentDeck()
        {
            if (_workingDeck == null) return false;
            var snapshot = CopyDeck(_workingDeck);
            snapshot.name = DeckNameForSave;
            try
            {
                DeckRepository.SaveUserDeck(snapshot);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[DeckEditor] Could not save the deck: {ex.Message}");
                const string message = "덱을 저장할 수 없습니다. 저장 폴더를 확인하세요.";
                if (_deckDialog.activeSelf)
                    _deckDialogMessage.text = message;
                else
                    ShowToast(message);
                return false;
            }
            _workingDeck.name = snapshot.name;
            _deckNameInput.SetTextWithoutNotify(snapshot.name);
            RefreshSaveLabel();
            ShowToast($"'{snapshot.name}' 저장 완료");
            return true;
        }

        private void BeforeReplacingEdits(Action next)
        {
            if (!HasUnsavedChanges())
            {
                next();
                return;
            }

            ShowDeckDialog("덱 변경사항", $"'{DeckNameForSave}' 덱의 변경사항을 저장할까요?",
                "저장 후 계속", () =>
                {
                    if (!TrySaveCurrentDeck()) return;
                    _deckDialog.SetActive(false);
                    next();
                },
                "저장 안 함", () =>
                {
                    _deckDialog.SetActive(false);
                    next();
                });
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
            ShowDeckDialog("덱 삭제", $"'{DeckNameForSave}' 덱을 삭제할까요?", "삭제", () =>
            {
                try
                {
                    DeckRepository.DeleteUserDeck(_workingDeck.id);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"[DeckEditor] Could not delete the deck: {ex.Message}");
                    _deckDialogMessage.text = "덱을 삭제할 수 없습니다. 저장 폴더를 확인하세요.";
                    return;
                }
                _deckDialog.SetActive(false);
                _userDeckIdx = Mathf.Clamp(_userDeckIdx, 0, DeckRepository.UserDecks.Count - 1);
                LoadWorkingDeckFromCurrent();
                ShowToast("덱을 삭제했습니다.");
            });
        }

        private void ShowToast(string msg)
        {
            if (_toastText != null) _toastText.text = msg ?? "";
        }

        private void BuildDeckDialog(Transform parent)
        {
            var dim = LotdUiKit.FillRect("Modal_DeckChanges", parent);
            _deckDialog = dim.gameObject;
            LotdUiKit.AddImage(_deckDialog, LotdUiKit.WhiteSprite, new Color(0f, 0.02f, 0.08f, 0.80f));
            var window = LotdUiKit.CreateMenuPanel(dim, "Window", new Vector2(0.29f, 0.36f), new Vector2(0.71f, 0.64f));
            var title = LotdUiKit.CreateRect("Title", window, new Vector2(0.05f, 0.72f), new Vector2(0.95f, 0.93f),
                Vector2.zero, Vector2.zero);
            _deckDialogTitle = LotdUiKit.AddText(title, "Text", "", 23, TextAnchor.MiddleCenter,
                LotdUiKit.GoldAccent, FontStyle.Bold);
            var message = LotdUiKit.CreateRect("Message", window, new Vector2(0.07f, 0.33f), new Vector2(0.93f, 0.71f),
                Vector2.zero, Vector2.zero);
            _deckDialogMessage = LotdUiKit.AddText(message, "Text", "", 17, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary);
            _deckDialogMessage.supportRichText = false;

            Button AddButton(string name, string label, float left, float right, Action action, bool gold = false)
            {
                var holder = LotdUiKit.CreateRect(name, window, new Vector2(left, 0.10f), new Vector2(right, 0.29f),
                    Vector2.zero, Vector2.zero);
                return LotdUiKit.CreateLotdButton(holder, "Button", label, action, 15, gold);
            }
            AddButton("Cancel", "취소", 0.05f, 0.33f, () => _deckDialog.SetActive(false));
            _deckDialogSecondary = AddButton("Secondary", "", 0.36f, 0.64f, null);
            _deckDialogPrimary = AddButton("Primary", "", 0.67f, 0.95f, null, true);
            _deckDialog.SetActive(false);
        }

        private void ShowDeckDialog(string title, string message, string primaryLabel, Action primary,
            string secondaryLabel = null, Action secondary = null)
        {
            void SetAction(Button button, string label, Action action)
            {
                button.gameObject.SetActive(action != null);
                button.GetComponentInChildren<Text>(true).text = label ?? "";
                button.onClick.RemoveAllListeners();
                if (action == null) return;
                button.onClick.AddListener(() =>
                {
                    LotdAudio.PlayClick();
                    action();
                });
            }

            _deckDialogTitle.text = title;
            _deckDialogMessage.text = message;
            SetAction(_deckDialogPrimary, primaryLabel, primary);
            SetAction(_deckDialogSecondary, secondaryLabel, secondary);
            _deckDialog.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        // ==================== PURE ARCHETYPE EXAMPLE DECK BROWSER ====================
        private void BuildArchetypeModal(Transform parent)
        {
            var dim = LotdUiKit.FillRect("Modal_ArchetypeBrowser", parent);
            _archModalRoot = dim.gameObject;
            LotdUiKit.AddImage(dim.gameObject, LotdUiKit.WhiteSprite, new Color(0f, 0.02f, 0.08f, 0.80f));
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => _archModalRoot.SetActive(false));

            var win = LotdUiKit.CreateBorderedPanel(dim, "Win",
                new Vector2(0.262f, 0.02f), new Vector2(0.99f, 0.905f), Vector2.zero, Vector2.zero,
                LotdUiKit.PanelNavy, LotdUiKit.GoldAccent);
            win.gameObject.AddComponent<Button>(); // block click-through to dim

            var hdr = LotdUiKit.CreateRect("Hdr", win, new Vector2(0f, 0.90f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(hdr.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelHeader);
            var title = LotdUiKit.AddText(hdr, "T", "📚 순수 카드군 예시 덱 선택 (PURE ARCHETYPE DECKS)", 19, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);
            title.rectTransform.offsetMin = new Vector2(16, 18);
            var sub = LotdUiKit.AddText(hdr, "Sub", "원하는 카드군을 선택하면 해당 순수 카드군 예시 덱(메인 40장 + 엑스트라 덱)이 자동 구성되며 보관함도 필터링됩니다.", 13, TextAnchor.LowerLeft, LotdUiKit.TextPrimary);
            sub.rectTransform.offsetMin = new Vector2(16, 4);

            var closeRt = LotdUiKit.CreateRect("Close", hdr, new Vector2(0.85f, 0.16f), new Vector2(0.985f, 0.84f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(closeRt, "B", "✕ 닫기", () => _archModalRoot.SetActive(false), 14);

            // Search & Sort row
            var filterRow = LotdUiKit.CreateRect("FilterRow", win, new Vector2(0.015f, 0.825f), new Vector2(0.985f, 0.888f), Vector2.zero, Vector2.zero);
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

            // Virtualized Scroll Grid (3 cols x 7 rows = 21 pooled tiles)
            var scrollHolder = LotdUiKit.CreateRect("ArchScrollHolder", win, new Vector2(0.012f, 0.015f), new Vector2(0.988f, 0.812f), Vector2.zero, Vector2.zero);
            _archScroll = scrollHolder.gameObject.AddComponent<ScrollRect>();
            _archScroll.horizontal = false;
            _archScroll.vertical = true;
            _archScroll.scrollSensitivity = 36f;
            _archScroll.movementType = ScrollRect.MovementType.Clamped;

            _archViewport = LotdUiKit.FillRect("VP", scrollHolder);
            _archViewport.gameObject.AddComponent<RectMask2D>();
            _archScroll.viewport = _archViewport;

            _archContent = LotdUiKit.CreateRect("Content", _archViewport, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            _archContent.pivot = new Vector2(0f, 1f);
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

            Canvas.ForceUpdateCanvases();
            _archTotalRows = Mathf.Max(1, (_archResults.Count + ArchCols - 1) / ArchCols);
            float vpW = _archViewport.rect.width > 10 ? _archViewport.rect.width : 1320f;
            float colW = vpW / ArchCols;
            _archRowHeight = Mathf.Clamp(Mathf.Round(colW * 0.30f), 116f, 140f);
            _archContent.sizeDelta = new Vector2(0f, _archTotalRows * _archRowHeight);
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

            float vpWidth = _archViewport.rect.width > 10 ? _archViewport.rect.width : 1320f;
            float colW = vpWidth / ArchCols;
            _archRowHeight = Mathf.Clamp(Mathf.Round(colW * 0.30f), 116f, 140f);

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
            BeforeReplacingEdits(() =>
            {
                var (main, extra) = CardDatabase.BuildPureArchetypeDeck(arch);
                _workingDeck.name = $"[순수] {arch.name}";
                _workingDeck.main = main;
                _workingDeck.extra = extra;
                _workingDeck.side.Clear();
                _deckNameInput.SetTextWithoutNotify(_workingDeck.name);
                SortWorkingDeck();
                SetTrunkSource(archetype: arch);
                if (arch.representativeCode != 0)
                    InspectCard(arch.representativeCode);
                _archModalRoot.SetActive(false);
                ShowToast($"'{_workingDeck.name}' 불러옴 · 메인 {main.Count}장 / 엑스트라 {extra.Count}장");
            });
        }
    }
}
