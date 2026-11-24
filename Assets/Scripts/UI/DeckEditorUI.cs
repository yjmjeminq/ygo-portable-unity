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
        private UserDeck _workingDeckBaseline;
        private uint _inspectedCode = 89631139;

        private static readonly string[] CardKinds = { "종류: 전체", "메인 몬스터", "마법", "함정", "엑스트라" };
        private static readonly string[] SortOptions = { "기본순", "공격력 높은 순", "레벨 높은 순", "이름순", "카드 프레임별" };
        private const int DefaultTrunkSort = 4;

        // Virtualized Trunk Scroll Grid
        private const int TrunkCols = 4;
        private const float GridCardInset = .75f;
        private const float MobileTrunkColumnWidth = 98f;
        private float _rowHeight = 138f;
        private float _colWidth = 92f;
        private int _totalRows = 0;
        private int _lastFirstRow = -1;
        private Vector2 _trunkSize;

        private readonly List<CardInfo> _trunkResults = new List<CardInfo>();
        private readonly Dictionary<uint, uint> _trunkPrintChoices = new();
        private readonly List<(RectTransform rt, CompositeCardView card, Text sub, CardDragTrigger trigger)> _trunkPool
            = new List<(RectTransform, CompositeCardView, Text, CardDragTrigger)>();

        private const int DeckColumns = 8;
        private const int OtherDeckCapacity = 15;
        private const int MainDeckCapacity = 60;
        private const int MainDeckVisibleRows = 3;
        private const int SideDeckColumns = OtherDeckCapacity;
        private const int SideDeckVisibleColumns = DeckColumns;
        private const string UnownedLabel = "미보유";
        private static readonly Color UnownedBadgeColor = new Color(.39f, .075f, .11f, .96f);
        private static readonly Color TrunkCountBadgeColor = new Color(.02f, .07f, .14f, .93f);

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
        private Image _previewAttributeIcon;
        private Text _previewName;
        private Text _previewStats;
        private Text _previewDesc;
        private Text _previewOwnership;
        private Text _previewRarity;
        private Text _dustBalance;
        private ScrollRect _previewScroll;

        private ScrollRect _trunkScroll;
        private Scrollbar _trunkScrollbar;
        private RectTransform _trunkContent;
        private RectTransform _trunkViewport;
        private Text _trunkHeading;
        private Text _trunkCountText;
        private Button _showAllCards;
        private Button _trunkOwnershipToggle;
        private bool? _trunkOwnedOnly;
        private Text _noTrunkResults;
        private uint _relatedFilterCode = 0;
        private ArchetypeInfo _activeArchetypeFilter = null;
        private InputField _searchInp;
        private Dropdown _kindFilter;
        private Dropdown _sortFilter;

        // Shared archetype picker for deck examples and trunk search.
        private GameObject _archModalRoot;
        private GameObject _archReturnFocus;
        private Button _archCloseButton;
        private Text _archTitle;
        private Text _archSubtitle;
        private InputField _archSearchInp;
        private Text _archSortBtnText;
        private Text _archCountText;
        private ScrollRect _archScroll;
        private RectTransform _archViewport;
        private RectTransform _archContent;
        private bool _archForSearch;
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
        private GameObject _editorContent;
        private Button _cardCraftingButton;
        private CardCraftingUI _cardCrafting;
        private CardTradeControls _previewTrade;
        private GameObject _deckDialog;
        private Text _deckDialogTitle;
        private Text _deckDialogMessage;
        private Button _deckDialogPrimary;
        private Button _deckDialogSecondary;
        private Button _deckDialogCancel;
        private GameObject _deckDialogReturnFocus;

        private RectTransform _trunkPanel;
        private RectTransform _deckPanel;
        private RectTransform _mainGridRect;
        private ScrollRect _mainDeckScroll;
        private RectTransform _mainDeckSlotGrid;
        private RectTransform _extraGridRect;
        private RectTransform _sideGridRect;
        private ScrollRect _sideDeckScroll;
        private RectTransform _sideDeckSlotGrid;

        public DeckEditorUI(GameAppController app, Transform canvasParent, bool? mobileLayout = null)
        {
            _app = app;
            _mobileLayout = mobileLayout ?? Application.platform == RuntimePlatform.IPhonePlayer;
            BuildUI(canvasParent);

            if (CardDragManager.Instance != null)
            {
                CardDragManager.Instance.OnDragDropped += HandleCardDrop;
            }
        }

        public void Open()
        {
            Root.SetActive(true);
            StartPadNavigation();
            ShowDeckPicker();
        }

        private void EnterDeckEditor(int index)
        {
            if (index < 0 || index >= DeckRepository.UserDecks.Count) return;
            _userDeckIdx = index;
            _deckPicker.gameObject.SetActive(false);
            _editorContent.SetActive(true);
            _padUi.Clear();
            _padArea = PadArea.Trunk;
            CloseDeckCardActions();
            CloseDetailedSearch();
            Canvas.ForceUpdateCanvases();
            LoadWorkingDeckFromCurrent();
            RefreshTrunkFilter();
            if (CollectionProgress.RequiresOwnedCardsForDecks && CollectionProgress.GetTotalOwnedCount(_inspectedCode) == 0 &&
                _trunkResults.Count > 0)
                _inspectedCode = (_trunkResults.Find(card => DeckGroupKey(card.code) == DeckGroupKey(_inspectedCode)) ??
                    _trunkResults[0]).code;
            InspectCard(_inspectedCode);
            ShowToast("");
        }

        public void Close()
        {
            CancelDeckDrag();
            StopPadNavigation();
            EndDeckTransfer();
            ClearCardVisual(ref _hoveredSlot);
            _pointerSlot = _inspectedSource = null;
            CloseDeckCardActions();
            CloseDetailedSearch();
            CloseDeckDialog(restoreFocus: false);
            CloseArchetypeModal(restoreFocus: false);
            CloseMainDeckSort(restoreFocus: false);
            CloseExpandedDeckCard();
            DismissDeckUtility(restoreFocus: false);
            _cardCrafting?.Hide();
            Root.SetActive(false);
            _deckPicker.gameObject.SetActive(false);
            _editorContent.SetActive(true);
        }

        public void Update()
        {
            if (!Root.activeInHierarchy) return;
            if (IsDeckPickerOpen)
            {
                UpdateDeckPicker();
                return;
            }
            if (_cardCrafting?.IsOpen == true)
            {
                if (!PollDeckPad() && Input.GetKeyDown(KeyCode.Escape))
                {
                    if (_padUi.HasPopup)
                        _padUi.Handle(_cardCrafting.ActiveRoot.transform, Vector2Int.zero, false, true, null);
                    else _cardCrafting.Back();
                }
                return;
            }
            UpdateDeckTransfer(Time.unscaledDeltaTime);
            UpdateExpandedDeckCard(Time.unscaledDeltaTime);
            UpdateMobileEditor();
            if (_mobileLayout && _workingDeck != null && _deckPanel.rect.size != _mobileDeckSize)
                RefreshDeckGrids();
            if (_trunkViewport.rect.size != _trunkSize)
                ResizeTrunkGrid(false);
            if (PollDeckPad()) return;
            if (_deckCardActions.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CloseDeckCardActions();
                return;
            }
            if (_padUi.HasPopup)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                    _padUi.Handle(Root.transform, Vector2Int.zero, false, true, null);
                return;
            }

            if (_deckDialog.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CloseDeckDialog();
                return;
            }
            if (_expandedDeckOverlay.gameObject.activeSelf || _utilityOverlay != null)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (_expandedDeckOverlay.gameObject.activeSelf) CloseExpandedDeckCard();
                    else CloseDeckUtility();
                }
                return;
            }
            if (_mainSortMenu.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CloseMainDeckSort();
                return;
            }
            if (_cardFilterDialog.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            {
                CloseDetailedSearch();
                return;
            }
            if (_archModalRoot.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            {
                CloseArchetypeModal();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ReturnToDeckPicker();
                return;
            }
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return;
            if (Input.GetKeyDown(KeyCode.F))
            {
                var input = _archModalRoot.activeSelf ? _archSearchInp :
                    _cardFilterDialog.activeSelf ? _detailedQueryInput : _searchInp;
                EventSystem.current?.SetSelectedGameObject(input.gameObject);
                input.ActivateInputField();
            }
            if (Input.GetKeyDown(KeyCode.S) && !_archModalRoot.activeSelf)
                TrySaveCurrentDeck();
            if (Input.GetKeyDown(KeyCode.R) && !_archModalRoot.activeSelf)
                ResetTrunkFilters();
        }

        private void LoadWorkingDeckFromCurrent()
        {
            CancelDeckDrag();
            EndDeckTransfer();
            if (DeckRepository.UserDecks.Count == 0)
            {
                DeckRepository.SaveUserDeck(new UserDeck { name = "새 커스텀 덱" });
            }
            _workingDeck = CopyDeck(DeckRepository.UserDecks[_userDeckIdx]);
            _mobileSelectedCode = 0;
            OrderWorkingDeckCards(DeckMainSortMode.Frame);
            _workingDeckBaseline = CopyDeck(_workingDeck);
            _deckNameInput.SetTextWithoutNotify(_workingDeck.name);
            RefreshDeckGrids();
            _mainDeckScroll.StopMovement();
            _mainDeckScroll.content.anchoredPosition = Vector2.zero;
            _mainDeckScroll.verticalScrollbar.SetValueWithoutNotify(1f);
            _sideDeckScroll.StopMovement();
            _sideDeckScroll.content.anchoredPosition = Vector2.zero;
            _sideDeckScroll.horizontalScrollbar?.SetValueWithoutNotify(0f);
            if (_extraDeckScroll != null)
            {
                _extraDeckScroll.StopMovement();
                _extraDeckScroll.content.anchoredPosition = Vector2.zero;
            }
            UpdateVirtualTrunkSlots(_lastFirstRow, force: true);
        }

        private static UserDeck CopyDeck(UserDeck deck) => CollectionProgress.CopyDeck(deck);

        private void BuildUI(Transform parent)
        {
            var screen = DuelUiKit.FillRect("Screen_DeckEditor", parent);
            Root = screen.gameObject;
            if (_mobileLayout)
                DuelUiKit.AddImage(Root, DuelUiKit.WhiteSprite, DeckEditorStyle.Inset, false, false);
            var root = DuelUiKit.FillRect("Editor", _mobileLayout ? MobileSafeArea.Create(screen) : screen);
            _editorContent = root.gameObject;
            DeckEditorStyle.Backdrop(root);
            if (!_mobileLayout)
            {
                DeckEditorStyle.AddHeader(root, "덱 편집");

                _openDrawButton = DeckButton(root, "OpenTestDraw", "테스트 드로우", .027f, .918f, .172f, .976f,
                    OpenTestDraw, fontSize: 16);
                var wallet = DuelUiKit.CreateRect("CraftDust", root, new Vector2(.181f, .918f),
                    new Vector2(.341f, .976f), Vector2.zero, Vector2.zero);
                DeckEditorStyle.Fill(wallet, DeckEditorStyle.Inset, false);
                _dustBalance = DuelUiKit.AddFittedText(wallet, "Balance", "", 17, 12,
                    TextAnchor.MiddleCenter, DeckEditorStyle.Accent, FontStyle.Bold);
                _deckShortageButton = DeckButton(root, "DeckShortages", "", .633f, .918f, .755f, .976f,
                    OpenDeckShortages, fontSize: 16);
                var examples = DuelUiKit.CreateRect("Examples", root, new Vector2(0.765f, 0.918f), new Vector2(0.871f, 0.976f),
                    Vector2.zero, Vector2.zero);
                DeckEditorStyle.Button(examples, "Button", "카드군 예시 덱", OpenArchetypeModal, 15);
                var back = DuelUiKit.CreateRect("Back", root, new Vector2(0.878f, 0.918f), new Vector2(0.985f, 0.976f),
                    Vector2.zero, Vector2.zero);
                DeckEditorStyle.Button(back, "Button", "덱 목록", ReturnToDeckPicker, 15);
            }

            BuildTrunkPanel(root);
            BuildCardDetails(root);
            BuildDeckPanel(root);
            BuildDeckMotion(root);

            var footer = DuelUiKit.CreateRect("Controls", root, new Vector2(0.015f, 0.002f), new Vector2(0.985f, 0.041f),
                Vector2.zero, Vector2.zero);
            DeckEditorStyle.Fill(footer, DeckEditorStyle.Inset, false);
            _deckHelpLabel = DuelUiKit.AddFittedText(DuelUiKit.CreateRect("Help", footer, new Vector2(0.01f, 0f),
                new Vector2(0.58f, 1f), Vector2.zero, Vector2.zero), "Text", MouseHelp, 13, 9,
                TextAnchor.MiddleLeft, DuelUiKit.TextPrimary);
            _toastText = DuelUiKit.AddText(DuelUiKit.CreateRect("Status", footer, new Vector2(0.59f, 0f), new Vector2(0.99f, 1f),
                Vector2.zero, Vector2.zero), "Text", "", 14, TextAnchor.MiddleRight, DuelUiKit.GoldAccent, FontStyle.Bold);
            _toastText.resizeTextForBestFit = true;
            _toastText.resizeTextMinSize = 10;
            _toastText.resizeTextMaxSize = 14;
            BuildArchetypeModal(root);
            BuildDeckDialog(root);
            BuildDetailedSearch(root);
            BuildPadNavigation(_mobileLayout ? root.parent : screen);
            BuildDeckCardActions(root);
            BuildMainSortMenu(root);
            BuildDeckUtilities(root);
            BuildExpandedDeckCard(root);
            if (_mobileLayout) BuildMobileEditor(root);
            BuildDeckPicker(root.parent);
        }

        private void BuildCardDetails(Transform root)
        {
            var panel = DeckEditorStyle.Panel(root, "CardDetails",
                new Vector2(_mobileLayout ? .340f : .015f, .053f), new Vector2(_mobileLayout ? .566f : .240f, .899f));
            float inset = _mobileLayout ? 17f : 20f;
            var card = DuelUiKit.CreateRect("LargeCard", panel,
                new Vector2(.066f, _mobileLayout ? .677f : .558f), new Vector2(.934f, .917f), Vector2.zero, Vector2.zero);
            BuildAppearanceControls(panel, inset);
            _previewCard = CreatePrintedDeckCard(card, "Preview", detailedOverlay: true);
            DeckCardVisualMotion.Bind(_previewCard);
            var previewHit = DuelUiKit.FillRect("PreviewHit", card);
            var previewGraphic = DuelUiKit.AddImage(previewHit.gameObject, DuelUiKit.WhiteSprite, Color.clear);
            CardOverframeInput.Attach(previewHit, _previewCard, motionSensor: false);
            var previewButton = previewHit.gameObject.AddComponent<Button>();
            previewButton.targetGraphic = previewGraphic;
            previewButton.onClick.AddListener(() => OpenExpandedDeckCard(_inspectedCode));

            var stats = DuelUiKit.CreateRect("Stats", panel,
                new Vector2(0f, _mobileLayout ? .590f : .499f), new Vector2(1f, _mobileLayout ? .673f : .553f),
                new Vector2(inset, 0f), new Vector2(-inset, 0f));
            DeckEditorStyle.Fill(stats, DeckEditorStyle.Header, false);
            var attribute = DuelUiKit.CreateRect("AttributeIcon", stats, new Vector2(.78f, .06f), new Vector2(.88f, .94f),
                Vector2.zero, Vector2.zero);
            _previewAttributeIcon = DuelUiKit.AddImage(attribute.gameObject, null, Color.white, true, false);
            var icon = DuelUiKit.CreateRect("TypeIcon", stats, new Vector2(.885f, .06f), new Vector2(.985f, .94f),
                Vector2.zero, Vector2.zero);
            _previewRaceIcon = DuelUiKit.AddImage(icon.gameObject, null, Color.white, true, false);
            _previewStats = DuelUiKit.AddFittedText(DuelUiKit.CreateRect("Info", stats,
                new Vector2(0.03f, 0f), new Vector2(.765f, 1f), Vector2.zero, Vector2.zero),
                "Text", "", _mobileLayout ? 19 : 17, 12, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            var name = DuelUiKit.CreateRect("Name", panel,
                new Vector2(0f, _mobileLayout ? .511f : .441f), new Vector2(1f, _mobileLayout ? .585f : .493f),
                new Vector2(inset, 0f), new Vector2(-inset, 0f));
            DuelUiKit.AddImage(name.gameObject, DuelUiKit.WhiteSprite, DeckEditorStyle.Surface, false, false);
            _previewName = DuelUiKit.AddFittedText(name, "Text", "", _mobileLayout ? 25 : 22, _mobileLayout ? 14 : 13,
                TextAnchor.MiddleCenter, DuelUiKit.GoldAccent, FontStyle.Bold);

            var desc = DuelUiKit.CreateRect("Description", panel,
                new Vector2(0f, .211f), new Vector2(1f, _mobileLayout ? .506f : .434f),
                new Vector2(inset, 0f), new Vector2(-inset, 0f));
            DeckEditorStyle.Fill(desc, DeckEditorStyle.Inset);
            var ownership = DuelUiKit.CreateRect("Ownership", desc, Vector2.up, Vector2.one,
                new Vector2(8f, -29f), new Vector2(-10f, -4f));
            DuelUiKit.AddImage(ownership.gameObject, DuelUiKit.WhiteSprite, DeckEditorStyle.Surface, false, false);
            _previewOwnership = DuelUiKit.AddFittedText(ownership, "Count", "", 15, 11,
                TextAnchor.MiddleLeft, DuelUiKit.TextPrimary, FontStyle.Bold);
            _previewOwnership.rectTransform.anchorMax = new Vector2(.79f, .965f);
            var rarity = DuelUiKit.CreateRect("Rarity", ownership, new Vector2(.815f, .08f),
                new Vector2(.984f, .92f), Vector2.zero, Vector2.zero);
            DeckEditorStyle.Fill(rarity, DeckEditorStyle.Inset, false);
            _previewRarity = DuelUiKit.AddFittedText(rarity, "Value", "", 17, 12,
                TextAnchor.MiddleCenter, DuelUiKit.TextPrimary, FontStyle.Bold);
            ownership.gameObject.SetActive(false);
            var viewport = DuelUiKit.CreateRect("Viewport", desc, Vector2.zero, Vector2.one,
                new Vector2(8f, 7f), new Vector2(-21f, -5f));
            viewport.gameObject.AddComponent<RectMask2D>();
            _previewDesc = DuelUiKit.AddText(viewport, "Content", "", _mobileLayout ? 23 : 18, TextAnchor.UpperLeft, DuelUiKit.TextPrimary);
            _previewDesc.supportRichText = false;
            _previewDesc.verticalOverflow = VerticalWrapMode.Overflow;
            _previewDesc.lineSpacing = _mobileLayout ? 1.16f : 1.08f;
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
            _previewScroll.verticalScrollbar = AddDeckScrollbar(desc);
            _previewScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            ((RectTransform)_previewScroll.verticalScrollbar.transform).offsetMin = new Vector2(-14f, 7f);

            var trade = DuelUiKit.CreateRect("Trade", panel, new Vector2(0f, .150f), new Vector2(1f, .205f),
                new Vector2(inset, 0f), new Vector2(-inset, 0f));
            _previewTrade = new CardTradeControls(trade, OpenCardCrafting, compact: true, deckEditor: true);

            var related = DuelUiKit.CreateRect("RelatedCards", panel,
                new Vector2(0f, .094f), new Vector2(.5f, .145f), new Vector2(inset, 0f), new Vector2(-4f, 0f));
            DeckEditorStyle.Button(related, "Button", "연관 카드 검색", () => FilterRelatedCards(_inspectedCode), 14);
            var crafting = DuelUiKit.CreateRect("Crafting", panel,
                new Vector2(.5f, .094f), new Vector2(1f, .145f), new Vector2(4f, 0f), new Vector2(-inset, 0f));
            _cardCraftingButton = DeckEditorStyle.Button(crafting, "Button", "제작·분해",
                () => OpenCardCrafting(_inspectedCode), 14);
            var add = DuelUiKit.CreateRect("AddCard", panel,
                new Vector2(0f, .034f), new Vector2(_mobileLayout ? .5f : 1f, .085f),
                new Vector2(inset, 0f), new Vector2(_mobileLayout ? -4f : -inset, 0f));
            DeckEditorStyle.Button(add, "Button", "카드 조작", OpenInspectedCardActions, 14, true);
            if (_mobileLayout)
            {
                var read = DuelUiKit.CreateRect("ReadCard", panel, new Vector2(.5f, .034f), new Vector2(1f, .085f),
                    new Vector2(4f, 0f), new Vector2(-inset, 0f));
                DeckEditorStyle.Button(read, "Button", "효과 크게 보기", () => OpenExpandedDeckCard(_inspectedCode), 17);
            }
        }

        private void OpenCardCrafting(uint code, CardTradeAction action = CardTradeAction.Browse)
        {
            if (!CollectionProgress.HasStarter)
            {
                ShowToast("스타터 덱을 선택하면 카드를 제작하거나 분해할 수 있습니다.");
                return;
            }

            CancelDeckDrag();
            EndDeckTransfer();
            _padUi.Clear();
            _padCardFocus.Hide();
            ClearCardVisual(ref _hoveredSlot);
            ClearCardVisual(ref _padVisualSlot);
            _pointerSlot = _inspectedSource = null;
            _cardCrafting ??= new CardCraftingUI(Root.transform, ReturnFromCardCrafting,
                openSecretPack: _app == null ? null : _app.OpenSecretPackShop);
            _cardCrafting.Root.transform.SetAsLastSibling();
            _editorContent.SetActive(false);
            _cardCrafting.Show(code, action);
        }

        private void ReturnFromCardCrafting()
        {
            _padUi.Clear();
            _cardCrafting.Hide();
            _editorContent.SetActive(true);
            RefreshDeckGrids();
            RefreshTrunkFilter();
            InspectCard(_inspectedCode);
            if (_padMode && _padArea != PadArea.Controls) DrawPadCardFocus();
            else EventSystem.current?.SetSelectedGameObject(_cardCraftingButton.gameObject);
        }

        private void BuildTrunkPanel(Transform rt)
        {
            var col = DeckEditorStyle.Panel(rt, "Trunk",
                new Vector2(_mobileLayout ? .015f : .710f, .053f), new Vector2(_mobileLayout ? .334f : .985f, .899f));
            _trunkPanel = col;

            var insideLeft = new Vector2(_mobileLayout ? 17f : 28f, 0f);
            var insideRight = -insideLeft;
            var caption = DuelUiKit.CreateBorderedPanel(col, "TrunkHeading",
                new Vector2(0f, .889f), new Vector2(1f, .944f), insideLeft, insideRight,
                DeckEditorStyle.Header, DeckEditorStyle.Edge);
            var heading = DuelUiKit.CreateRect("Title", caption, new Vector2(.225f, 0f), new Vector2(.775f, 1f),
                Vector2.zero, Vector2.zero);
            _trunkHeading = DuelUiKit.AddFittedText(heading, "Label", "보관함", 21, 12,
                TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            var showAll = DuelUiKit.CreateRect("ShowAll", caption, new Vector2(.012f, .10f), new Vector2(.221f, .90f),
                Vector2.zero, Vector2.zero);
            _showAllCards = DeckEditorStyle.Button(showAll, "Button", "전체 카드", ExitTrunkScope, 13);
            var count = DuelUiKit.CreateRect("Total", caption, new Vector2(.785f, 0f), new Vector2(.978f, 1f),
                Vector2.zero, Vector2.zero);
            _trunkCountText = DuelUiKit.AddFittedText(count, "Count", "", 17, 12, TextAnchor.MiddleRight, Color.white, FontStyle.Bold);

            var searchRow = DuelUiKit.CreateRect("SearchRow", col, new Vector2(0f, .827f),
                new Vector2(1f, .878f), insideLeft, insideRight);
            var search = DuelUiKit.CreateRect("SearchBox", searchRow, Vector2.zero, new Vector2(0.74f, 1f),
                Vector2.zero, Vector2.zero);
            _searchInp = DeckEditorStyle.Input(search, "Search", "카드명·효과·카드 번호", 15);
            _searchInp.onValueChanged.AddListener(_ => RefreshTrunkFilter());
            var reset = DuelUiKit.CreateRect("ResetFilters", searchRow, new Vector2(0.76f, 0f), Vector2.one,
                Vector2.zero, Vector2.zero);
            DeckEditorStyle.Button(reset, "Button", "필터 초기화", ResetTrunkFilters, 14);

            var filters = DuelUiKit.CreateRect("Filters", col, new Vector2(0f, .764f), new Vector2(1f, .816f),
                insideLeft, insideRight);
            Dropdown AddFilter(string name, float left, float right, IReadOnlyList<string> choices, Action<int> changed)
            {
                var slot = DuelUiKit.CreateRect(name, filters, new Vector2(left, 0f), new Vector2(right, 1f),
                    Vector2.zero, Vector2.zero);
                var dropdown = DeckEditorStyle.Dropdown(slot, "Dropdown", choices, changed);
                dropdown.captionText.alignment = TextAnchor.MiddleCenter;
                return dropdown;
            }
            _kindFilter = AddFilter("Kind", 0f, .322f, CardKinds, ChangeTrunkKind);
            _sortFilter = AddFilter("Sort", .336f, .632f, SortOptions, _ => RefreshTrunkFilter());
            _sortFilter.SetValueWithoutNotify(DefaultTrunkSort);
            var ownership = DuelUiKit.CreateRect("Ownership", filters, new Vector2(.647f, 0f), Vector2.one,
                Vector2.zero, Vector2.zero);
            _trunkOwnershipToggle = DeckEditorStyle.Button(ownership, "Button", "보유 카드만", () =>
            {
                var selection = _trunkCollectionFilters.Selection;
                _trunkOwnedOnly = selection.Ownership == 0 ? !TrunkShowsOwnedOnly : selection.Ownership == 2;
                _trunkCollectionFilters.Selection = (selection.Rarity, 0, selection.Edition);
                RefreshTrunkFilter();
            }, 13);
            var ownershipLabel = _trunkOwnershipToggle.GetComponentInChildren<Text>();
            ownershipLabel.resizeTextForBestFit = true;
            ownershipLabel.resizeTextMinSize = 10;
            ownershipLabel.resizeTextMaxSize = 13;

            var searchOptions = DuelUiKit.CreateRect("SearchOptions", col, new Vector2(0f, .038f),
                new Vector2(1f, .093f), insideLeft, insideRight);
            var advanced = DuelUiKit.CreateRect("DetailedSearch", searchOptions, Vector2.zero,
                new Vector2(.49f, 1f), Vector2.zero, Vector2.zero);
            _detailButtonText = DeckEditorStyle.Button(advanced, "Button", "필터", ToggleDetailedSearch, 15, true)
                .GetComponentInChildren<Text>();
            var archetype = DuelUiKit.CreateRect("ArchetypeSearch", searchOptions, new Vector2(.51f, 0f),
                Vector2.one, Vector2.zero, Vector2.zero);
            DeckEditorStyle.Button(archetype, "Button", "카드군 검색", OpenArchetypeSearch, 15);

            BuildQuickFilters(col, insideLeft, insideRight);

            var scrollHolder = DuelUiKit.CreateRect("TrunkScrollHolder", col,
                new Vector2(0f, .101f), new Vector2(1f, .694f), insideLeft, insideRight);
            DuelUiKit.AddImage(scrollHolder.gameObject, DuelUiKit.WhiteSprite, Color.clear);
            _trunkScroll = scrollHolder.gameObject.AddComponent<ScrollRect>();
            _trunkScroll.horizontal = false;
            _trunkScroll.scrollSensitivity = 40f;
            _trunkScroll.movementType = ScrollRect.MovementType.Clamped;

            _trunkViewport = DuelUiKit.CreateRect("Viewport", scrollHolder, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-17f, 0f));
            _trunkViewport.gameObject.AddComponent<RectMask2D>();
            _trunkScroll.viewport = _trunkViewport;
            _trunkContent = DuelUiKit.CreateRect("Content", _trunkViewport, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
            _trunkContent.pivot = new Vector2(0f, 1f);
            _trunkScroll.content = _trunkContent;

            _trunkScrollbar = AddDeckScrollbar(scrollHolder);
            _trunkScrollbar.size = 0.075f;
            _trunkScrollbar.SetValueWithoutNotify(1f);
            _trunkScrollbar.onValueChanged.AddListener(value => _trunkScroll.verticalNormalizedPosition = value);
            _trunkScroll.onValueChanged.AddListener(position =>
            {
                _trunkScrollbar.SetValueWithoutNotify(position.y);
                OnTrunkScrolled();
            });
            _noTrunkResults = DuelUiKit.AddText(scrollHolder, "Empty", "조건에 맞는 카드가 없습니다.",
                17, TextAnchor.MiddleCenter, DuelUiKit.TextMuted);
        }

        private static Scrollbar AddDeckScrollbar(Transform parent, bool horizontal = false)
        {
            var scrollbar = DuelUiKit.AddVerticalScrollbar(parent);
            if (horizontal)
            {
                var track = (RectTransform)scrollbar.transform;
                track.anchorMin = Vector2.zero;
                track.anchorMax = Vector2.right;
                track.offsetMin = new Vector2(1f, 1f);
                track.offsetMax = new Vector2(-1f, 9f);
                scrollbar.direction = Scrollbar.Direction.LeftToRight;
            }
            DeckEditorStyle.ApplyScrollbar(scrollbar);
            return scrollbar;
        }

        private static void UpdateDeckScrollbar(ScrollRect scroll)
        {
            float viewportHeight = scroll.viewport.rect.height;
            float contentHeight = scroll.content.rect.height;
            bool hasOverflow = contentHeight > viewportHeight;
            scroll.verticalScrollbar.gameObject.SetActive(hasOverflow);
            scroll.verticalScrollbar.size = hasOverflow ? viewportHeight / contentHeight : 1f;
            scroll.verticalScrollbar.SetValueWithoutNotify(scroll.verticalNormalizedPosition);
        }

        private void EnsureTrunkPool(int count)
        {
            while (_trunkPool.Count < count)
            {
                var slot = DuelUiKit.CreateRect("TrunkCard", _trunkContent, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero);
                slot.pivot = new Vector2(0f, 1f);
                var hit = DuelUiKit.AddImage(slot.gameObject, DuelUiKit.WhiteSprite, Color.clear);
                var card = CreatePrintedDeckCard(DuelUiKit.FillRect("CardInset", slot, GridCardInset), "Card");
                DeckCardVisualMotion.Bind(card, hit);
                var countBox = DuelUiKit.CreateRect("Copies", slot, new Vector2(0.24f, 0.032f), new Vector2(0.76f, 0.215f),
                    Vector2.zero, Vector2.zero);
                DuelUiKit.AddImage(countBox.gameObject, DuelUiKit.WhiteSprite, TrunkCountBadgeColor, false, false);
                var label = DuelUiKit.AddFittedText(countBox, "Count", "", _mobileLayout ? 21 : 17, 8,
                    TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                if (_mobileLayout) label.verticalOverflow = VerticalWrapMode.Overflow;
                label.gameObject.AddComponent<Outline>().effectColor = Color.black;
                var trigger = slot.gameObject.AddComponent<CardDragTrigger>();
                trigger.SourceType = DragSourceType.Trunk;
                trigger.PreferTouchScroll = _mobileLayout;
                trigger.OnPointerExitAction = () => ClearMouseCardVisual(slot);
                _trunkPool.Add((slot, card, label, trigger));
            }
        }

        private void BuildDeckPanel(Transform root)
        {
            var panel = DeckEditorStyle.Panel(root, "Deck",
                new Vector2(_mobileLayout ? .574f : .250f, .053f), new Vector2(_mobileLayout ? .985f : .700f, .899f));
            _deckPanel = panel;

            var header = DuelUiKit.CreateRect("DeckControls", panel,
                new Vector2(0f, .889f), new Vector2(1f, .944f), new Vector2(28f, 0f), new Vector2(-28f, 0f));
            Button Control(Transform row, string name, string label, float x, float width, Action action, bool gold = false)
            {
                var holder = DuelUiKit.CreateRect(name, row, new Vector2(x, 0f), new Vector2(x + width, 1f),
                    Vector2.zero, Vector2.zero);
                return DeckEditorStyle.Button(holder, "Button", label, action, 14, gold);
            }

            Control(header, "Previous", "◀", 0f, 0.081f, () => SwitchUserDeck(-1));
            _deckHeaderText = DuelUiKit.AddFittedText(DuelUiKit.CreateRect("DeckNumber", header, new Vector2(0.083f, 0f),
                new Vector2(0.235f, 1f), Vector2.zero, Vector2.zero),
                "Text", "", 16, 11, TextAnchor.MiddleCenter, DuelUiKit.GoldAccent, FontStyle.Bold);
            Control(header, "Next", "▶", 0.24f, 0.081f, () => SwitchUserDeck(1));
            Control(header, "New", "새 덱", 0.34f, 0.18f, CreateNewUserDeck);
            _saveButtonText = Control(header, "Save", "저장", 0.53f, 0.21f,
                () => TrySaveCurrentDeck(), true).GetComponentInChildren<Text>();
            Control(header, "Delete", "삭제", 0.76f, 0.24f, DeleteCurrentDeck);

            var edit = DuelUiKit.CreateRect("EditControls", panel,
                new Vector2(0f, .827f), new Vector2(1f, .878f), new Vector2(28f, 0f), new Vector2(-28f, 0f));
            _deckNameInput = DeckEditorStyle.Input(DuelUiKit.CreateRect("DeckName", edit, Vector2.zero,
                new Vector2(0.430f, 1f), Vector2.zero, Vector2.zero), "Input", "덱 이름", 15);
            _deckNameInput.onValueChanged.AddListener(_ => RefreshSaveLabel());
            _wholeSortButton = Control(edit, "Sort", "정렬 옵션", .442f, .17f, OpenWholeDeckSort);
            Control(edit, "AutoSort", "자동 정렬", .625f, .18f, () => SortWorkingDeck(byFrame: true));
            Control(edit, "DeckTools", "덱 관리", .819f, .181f, OpenDeckTools);

            (_mainGridRect, _mainCountText) = BuildDeckSection(panel, "Main", "메인", .789f, .429f);
            _mainSortButton = AddMainSortButton(panel, OpenMainDeckSort);
            BuildComposition();
            (_extraGridRect, _extraCountText) = BuildDeckSection(panel, "Extra", "엑스트라", .392f, .181f);
            (_sideGridRect, _sideCountText) = BuildDeckSection(panel, "Side", "사이드", .144f, .025f);
            _mainDeckScroll = BuildVerticalDeckScroll(_mainGridRect, out _mainDeckSlotGrid);
            BuildSideDeckScroll();
            if (_mobileLayout)
                _extraDeckScroll = BuildVerticalDeckScroll(_extraGridRect, out _extraDeckSlotGrid);
            BuildDeckSlots(_mainDeckSlotGrid, _mainSlots, DeckColumns, MainDeckCapacity, DragSourceType.DeckMain);
            BuildDeckSlots(_extraDeckSlotGrid ?? _extraGridRect, _extraSlots, DeckColumns, OtherDeckCapacity, DragSourceType.DeckExtra);
            BuildDeckSlots(_sideDeckSlotGrid, _sideSlots, SideDeckColumns, OtherDeckCapacity, DragSourceType.DeckSide);
        }

        private static ScrollRect BuildVerticalDeckScroll(RectTransform grid, out RectTransform slots)
        {
            var scroll = grid.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 56f;
            var viewport = DuelUiKit.CreateRect("Viewport", grid, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-14f, 0f));
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;
            var content = DuelUiKit.FillRect("Content", viewport);
            content.pivot = new Vector2(.5f, 1f);
            scroll.content = content;
            slots = DuelUiKit.FillRect("Slots", content);
            scroll.verticalScrollbar = AddDeckScrollbar(grid);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }

        private void BuildSideDeckScroll()
        {
            _sideDeckScroll = _sideGridRect.gameObject.AddComponent<ScrollRect>();
            _sideDeckScroll.vertical = false;
            _sideDeckScroll.movementType = ScrollRect.MovementType.Clamped;
            _sideDeckScroll.scrollSensitivity = 56f;

            var viewport = DuelUiKit.CreateRect("Viewport", _sideGridRect, Vector2.zero, Vector2.one,
                new Vector2(0f, _mobileLayout ? 0f : 10f), Vector2.zero);
            viewport.gameObject.AddComponent<RectMask2D>();
            _sideDeckScroll.viewport = viewport;
            var content = DuelUiKit.FillRect("Content", viewport);
            content.pivot = new Vector2(0f, .5f);
            _sideDeckScroll.content = content;
            _sideDeckSlotGrid = DuelUiKit.FillRect("Slots", content);

            if (!_mobileLayout)
            {
                _sideDeckScroll.horizontalScrollbar = AddDeckScrollbar(_sideGridRect, horizontal: true);
                _sideDeckScroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            }
        }

        private static void ResizeVerticalDeckScroll(ScrollRect scroll, RectTransform slots, int cards,
            int capacity, int columns, int visibleRows)
        {
            int rows = Mathf.Max(visibleRows, (Mathf.Min(cards, capacity) + columns - 1) / columns);
            int slotRows = (capacity + columns - 1) / columns;
            var content = scroll.content;
            content.anchorMin = new Vector2(0f, 1f - rows / (float)visibleRows);
            slots.anchorMin = new Vector2(0f, 1f - slotRows / (float)rows);

            float maxOffset = Mathf.Max(0f, content.rect.height - scroll.viewport.rect.height);
            if (content.anchoredPosition.y > maxOffset)
            {
                scroll.StopMovement();
                content.anchoredPosition = new Vector2(0f, maxOffset);
            }
        }

        private void ResizeSideDeckScroll()
        {
            int columns = Mathf.Clamp(_workingDeck.side.Count, _sideVisibleColumns, SideDeckColumns);
            var content = _sideDeckScroll.content;
            content.anchorMax = new Vector2(columns / (float)_sideVisibleColumns, 1f);
            _sideDeckSlotGrid.anchorMax = new Vector2(SideDeckColumns / (float)columns, 1f);

            float maxOffset = Mathf.Max(0f, content.rect.width - _sideDeckScroll.viewport.rect.width);
            if (content.anchoredPosition.x < -maxOffset)
            {
                _sideDeckScroll.StopMovement();
                content.anchoredPosition = new Vector2(-maxOffset, 0f);
            }
        }

        private void RevealDeckSlot(DragSourceType zone, int index)
        {
            var scroll = zone switch
            {
                DragSourceType.DeckMain => _mainDeckScroll,
                DragSourceType.DeckExtra => _extraDeckScroll,
                DragSourceType.DeckSide => _sideDeckScroll,
                _ => null,
            };
            if (scroll == null) return;

            bool vertical = scroll.vertical;
            Vector2 axis = vertical ? Vector2.up : Vector2.left;
            float viewSize = vertical ? scroll.viewport.rect.height : scroll.viewport.rect.width;
            bool main = zone == DragSourceType.DeckMain;
            float slotSize = viewSize / (vertical ? (main ? _mainVisibleRows : _extraVisibleRows) : _sideVisibleColumns);
            int slot = vertical ? Mathf.Clamp(index, 0, (main ? MainDeckCapacity : OtherDeckCapacity) - 1) /
                (main ? _mainColumns : _extraColumns) : Mathf.Clamp(index, 0, SideDeckColumns - 1);
            float slotStart = slot * slotSize;
            var content = scroll.content;
            float offset = Vector2.Dot(content.anchoredPosition, axis);
            float target = Mathf.Clamp(offset, slotStart + slotSize - viewSize, slotStart);
            if (Mathf.Approximately(target, offset)) return;
            scroll.StopMovement();
            content.anchoredPosition = axis * target;
        }

        private static (RectTransform grid, Text count) BuildDeckSection(Transform panel, string name, string title,
            float top, float bottom)
        {
            var label = DuelUiKit.CreateRect(name + "Tab", panel, new Vector2(0f, top),
                new Vector2(0.194f, top + 0.033f), new Vector2(28f, 0f), Vector2.zero);
            DeckEditorStyle.Fill(label, DeckEditorStyle.Header, false);
            DuelUiKit.AddText(label, "Label", title, 14, TextAnchor.MiddleCenter,
                DuelUiKit.TextPrimary, FontStyle.Bold);
            var count = DuelUiKit.AddFittedText(DuelUiKit.CreateRect(name + "Count", panel,
                new Vector2(0.6f, top), new Vector2(1f, top + 0.034f), Vector2.zero, new Vector2(-28f, 0f)),
                "Text", "", 16, 11, TextAnchor.MiddleRight, Color.white, FontStyle.Bold);
            var grid = DuelUiKit.CreateRect(name + "Grid", panel, new Vector2(0f, bottom),
                new Vector2(1f, top), new Vector2(28f, 0f), new Vector2(-28f, 0f));
            DeckEditorStyle.Fill(grid, DeckEditorStyle.Inset);
            return (grid, count);
        }

        private void BuildDeckSlots(Transform parent, List<(RectTransform rt, CompositeCardView card, CardDragTrigger trigger)> target,
            int cols, int capacity, DragSourceType source)
        {
            int rows = (capacity + cols - 1) / cols;
            for (int i = 0; i < capacity; i++)
            {
                var slot = DuelUiKit.FillRect($"Card_{i}", parent, 1.2f);
                SetDeckSlotPosition(slot, i, cols, rows);
                var hit = DuelUiKit.AddImage(slot.gameObject, DuelUiKit.WhiteSprite, Color.clear);
                var card = CreatePrintedDeckCard(DuelUiKit.FillRect("CardInset", slot, GridCardInset), "Card");
                DeckCardVisualMotion.Bind(card, hit);
                var trigger = slot.gameObject.AddComponent<CardDragTrigger>();
                trigger.SourceType = source;
                var badge = DuelUiKit.CreateRect("Unowned", card.transform.Find("AspectBox"),
                    new Vector2(.03f, .12f), new Vector2(.97f, .39f), Vector2.zero, Vector2.zero);
                DuelUiKit.AddImage(badge.gameObject, DuelUiKit.WhiteSprite, UnownedBadgeColor, false, false);
                DuelUiKit.AddFittedText(badge, "Label", "부족", 17, 9, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                badge.gameObject.SetActive(false);
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
            CloseDetailedSearch();
            _relatedFilterCode = relatedCode;
            _activeArchetypeFilter = archetype;
            ClearTrunkFilterValues();
        }

        private void ResetTrunkFilters()
        {
            _trunkOwnedOnly = null;
            _relatedFilterCode = 0;
            _activeArchetypeFilter = null;
            ClearTrunkFilterValues();
        }

        private void ClearTrunkFilterValues()
        {
            _sortFilter.SetValueWithoutNotify(DefaultTrunkSort);
            if (_mobileTrunkSort != null) _mobileTrunkSort.text = SortOptions[DefaultTrunkSort];
            _queryBeforeFilters = "";
            ClearDetailedSearch();
            SetQuickFilter(0, refresh: false);
            _quickFilterScroll.StopMovement();
            _quickFilterScroll.horizontalNormalizedPosition = 0f;
            if (_cardFilterDialog.activeSelf)
            {
                ResetDetailedDraft();
                return;
            }

            _searchInp.SetTextWithoutNotify("");
            _kindFilter.SetValueWithoutNotify(0);
            RefreshTrunkFilter();
        }

        private void ExitTrunkScope()
        {
            _relatedFilterCode = 0;
            _activeArchetypeFilter = null;
            RefreshTrunkFilter();
        }

        private bool TrunkShowsOwnedOnly => CollectionProgress.Initialized &&
            (_trunkOwnedOnly ?? (CollectionProgress.RequiresOwnedCardsForDecks && _activeArchetypeFilter == null && _relatedFilterCode == 0));

        private void RefreshTrunkFilter()
        {
            _trunkResults.Clear();
            var filter = CreateTrunkFilter(_searchInp.text, (DeckCardKind)_kindFilter.value);
            UpdateDetailedSearchButton();

            IEnumerable<CardInfo> source = CardDatabase.AllCards;
            _showAllCards.transform.parent.gameObject.SetActive(_activeArchetypeFilter != null || _relatedFilterCode != 0);
            _trunkHeading.text = "보관함";
            _trunkHeading.color = Color.white;
            if (_activeArchetypeFilter != null)
            {
                source = _activeArchetypeFilter.coreCards.Union(_activeArchetypeFilter.supportCards);
                _trunkHeading.text = $"카드군 · {_activeArchetypeFilter.name}";
                _trunkHeading.color = DuelUiKit.GoldAccent;
            }
            else if (_relatedFilterCode != 0)
            {
                var related = CardDatabase.GetRelatedCards(_relatedFilterCode);
                source = related.Prepend(CardDatabase.Get(_relatedFilterCode));
                _trunkHeading.text = $"관련 카드 · {CardDatabase.GetName(_relatedFilterCode)}";
                _trunkHeading.color = DuelUiKit.GoldAccent;
            }

            bool explicitOwnership = _trunkCollectionFilters.Selection.Ownership != 0;
            bool ownedOnly = !explicitOwnership && TrunkShowsOwnedOnly;
            _trunkOwnershipToggle.interactable = CollectionProgress.Initialized;
            _trunkOwnershipToggle.GetComponentInChildren<Text>().text = explicitOwnership ? "보유 필터 적용" :
                ownedOnly ? "보유 카드만" : "미보유 포함";
            uint.TryParse(_searchInp.text, out uint searchedCode);
            var seen = new HashSet<uint>();
            foreach (var card in source)
            {
                if (!CollectionProgress.IsCraftableCard(card) ||
                    !MatchesQuickFilter(card) || !seen.Add(DeckGroupKey(card.code))) continue;
                var entry = PickTrunkPrinting(card, filter, searchedCode, ownedOnly);
                if (entry != null) _trunkResults.Add(entry);
            }

            int sort = _sortFilter.value;
            if (_mobileTrunkSort != null) _mobileTrunkSort.text = SortOptions[sort];
            if (sort == 1) _trunkResults.Sort((a, b) => b.atk.CompareTo(a.atk));
            else if (sort == 2) _trunkResults.Sort((a, b) => b.level.CompareTo(a.level));
            else if (sort == 3) _trunkResults.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            else if (sort == DefaultTrunkSort) _trunkResults.Sort((a, b) =>
            {
                int frame = DeckMainSorter.FrameOrder(a).CompareTo(DeckMainSorter.FrameOrder(b));
                return frame != 0 ? frame : string.Compare(a.name, b.name, StringComparison.Ordinal);
            });

            if (filter.HasSearchQuery)
            {
                var ranked = _trunkResults.OrderBy(filter.SearchRank).ToArray();
                _trunkResults.Clear();
                _trunkResults.AddRange(ranked);
            }

            Canvas.ForceUpdateCanvases();
            _trunkCountText.text = CollectionProgress.Initialized ? $"{_trunkResults.Count:N0}종" : $"{_trunkResults.Count:N0}장";
            _noTrunkResults.text = ownedOnly
                ? "조건에 맞는 보유 카드가 없습니다." : "조건에 맞는 카드가 없습니다.";
            _noTrunkResults.gameObject.SetActive(_trunkResults.Count == 0);
            _padIndices[(int)PadArea.Trunk] = 0;
            ResizeTrunkGrid(true);
        }

        private CardInfo PickTrunkPrinting(CardInfo card, DeckCardFilter filter, uint searchedCode, bool ownedOnly)
        {
            bool Matches(CardInfo candidate) => _trunkCollectionFilters.Matches(candidate.code) && filter.Matches(candidate);
            if (!CardDatabase.TryGetDeckPrintings(card.code, out var printings))
                return (!ownedOnly || CollectionProgress.GetTotalOwnedCount(card.code) > 0) && Matches(card) ? card : null;

            var owned = printings.FirstOrDefault(printing => CollectionProgress.GetTotalOwnedCount(printing.code) > 0 && Matches(printing));
            if (ownedOnly && owned == null) return null;
            var matching = printings.FirstOrDefault(Matches);
            if (matching == null) return null;

            var exact = Array.Find(printings, printing => printing.code == searchedCode);
            if (exact != null && (!ownedOnly || CollectionProgress.GetTotalOwnedCount(exact.code) > 0) && Matches(exact))
                return exact;
            if (_trunkPrintChoices.TryGetValue(printings[0].code, out uint preference) &&
                (!ownedOnly || CollectionProgress.GetTotalOwnedCount(preference) > 0) &&
                CardDatabase.Get(preference) is { } preferred && Matches(preferred))
                return preferred;
            return owned ?? matching;
        }

        private void ResizeTrunkGrid(bool startAtTop)
        {
            if (startAtTop) _trunkPadScrollTarget = null;
            LayoutMobileQuickFilters();
            _trunkSize = _trunkViewport.rect.size;
            float scroll = startAtTop ? 1f : _trunkScroll.verticalNormalizedPosition;
            float vpWidth = _trunkViewport.rect.width > 10 ? _trunkViewport.rect.width : 405f;
            _trunkColumns = _mobileLayout ? Mathf.Clamp(Mathf.FloorToInt(vpWidth / MobileTrunkColumnWidth), 1, 8) : TrunkCols;
            _colWidth = vpWidth / _trunkColumns;
            _rowHeight = Mathf.Max(20f, Mathf.Round((_colWidth - 4f) / DuelUiKit.CardAspectRatio + 4f));

            _totalRows = Mathf.Max(1, (_trunkResults.Count + _trunkColumns - 1) / _trunkColumns);
            int rows = Mathf.CeilToInt(_trunkViewport.rect.height / _rowHeight) + 2;
            EnsureTrunkPool(Mathf.Min(_totalRows, rows) * _trunkColumns);
            _trunkContent.sizeDelta = new Vector2(0f, _totalRows * _rowHeight);
            _trunkScrollbar.gameObject.SetActive(_trunkContent.rect.height > _trunkViewport.rect.height);

            _trunkScroll.StopMovement();
            _trunkScroll.verticalNormalizedPosition = scroll;
            _trunkScrollbar.SetValueWithoutNotify(scroll);
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
                int row = firstRow + i / _trunkColumns;
                int col = i % _trunkColumns;
                int index = row * _trunkColumns + col;
                if (index >= _trunkResults.Count)
                {
                    ResetCardBinding(rect, view);
                    rect.gameObject.SetActive(false);
                    continue;
                }

                rect.anchoredPosition = new Vector2(col * _colWidth + 2f, -row * _rowHeight - 2f);
                rect.sizeDelta = new Vector2(_colWidth - 4f, _rowHeight - 4f);
                rect.gameObject.SetActive(true);
                uint code = _trunkResults[index].code;
                if (view.CurrentCode != code) ResetCardBinding(rect, view);
                view.SetDeckCard(code, ReadAppearance(code, null, -1));

                var countBox = count.transform.parent;
                var countBackground = countBox.GetComponent<Image>();
                countBackground.color = TrunkCountBadgeColor;
                bool showOwnership = CollectionProgress.Initialized;
                if (showOwnership)
                {
                    long remaining = GetRemainingTrunkCopies(code, out long owned, out int inDeck);
                    countBox.gameObject.SetActive(true);
                    count.text = owned == 0 ? UnownedLabel : $"×{remaining}";
                    if (owned == 0) countBackground.color = UnownedBadgeColor;
                    count.color = owned == 0 ? Color.white : inDeck > owned ? DuelUiKit.RedAccent :
                        remaining == 0 ? DuelUiKit.TextMuted : Color.white;
                }
                else
                {
                    GetRemainingTrunkCopies(code, out _, out int copies);
                    countBox.gameObject.SetActive(copies > 0);
                    count.text = copies.ToString();
                    count.color = Color.white;
                }
                trigger.CardCodeGetter = () => code;
                trigger.IndexGetter = () => index;
                trigger.OnPointerEnterAction = () => SetMouseCardVisual(rect, code);
                BindDeckTileActions(rect, trigger, PadArea.Trunk, index, code);
            }
        }

        private void RefreshDeckGrids()
        {
            _deckHeaderText.text = $"{_userDeckIdx + 1} / {Mathf.Max(1, DeckRepository.UserDecks.Count)}";
            ReflowMobileDeck();
            ResizeVerticalDeckScroll(_mainDeckScroll, _mainDeckSlotGrid, _workingDeck.main.Count,
                MainDeckCapacity, _mainColumns, _mainVisibleRows);
            if (_extraDeckScroll != null)
                ResizeVerticalDeckScroll(_extraDeckScroll, _extraDeckSlotGrid, _workingDeck.extra.Count,
                    OtherDeckCapacity, _extraColumns, _extraVisibleRows);
            ResizeSideDeckScroll();
            var assigned = CollectionProgress.RequiresOwnedCardsForDecks
                ? CollectionProgress.AssignOwnedDeckCopies(_workingDeck) : null;
            int mainCount = _workingDeck.main.Count;
            var missing = new[]
            {
                SyncSlotList(_mainSlots, _workingDeck.main, PadArea.Main, assigned, 0),
                SyncSlotList(_extraSlots, _workingDeck.extra, PadArea.Extra, assigned, mainCount),
                SyncSlotList(_sideSlots, _workingDeck.side, PadArea.Side, assigned, mainCount + _workingDeck.extra.Count),
            };
            RefreshComposition(missing);
            RefreshDeckShortages(missing.Sum());
            RefreshPreviewOwnership();
            RefreshSaveLabel();
            RefreshMobileSectionLabels(missing);
            RefreshMobileCardSelection();
        }

        private int SyncSlotList(List<(RectTransform rt, CompositeCardView card, CardDragTrigger trigger)> slots,
            List<uint> cards, PadArea area, (uint Code, bool Overframe)[] assigned, int offset)
        {
            int missing = 0;
            int count = Mathf.Max(slots.Count, cards.Count);
            for (int i = 0; i < count; i++)
            {
                bool filled = i < cards.Count;
                uint code = filled ? cards[i] : 0;
                var appearance = filled ? _workingDeck.GetAppearance(cards, i) : default;
                bool shortage = filled && assigned != null && assigned[offset + i].Code == 0;
                if (shortage) missing++;

                if (i >= slots.Count) continue;
                var (rect, view, trigger) = slots[i];
                if (filled ? view.CurrentCode != code : view.gameObject.activeSelf) ResetCardBinding(rect, view);
                view.gameObject.SetActive(filled);
                trigger.CardCodeGetter = null;
                trigger.IndexGetter = null;
                trigger.OnLeftClick = null;
                trigger.OnTouchClick = null;
                trigger.OnTouchHold = null;
                trigger.OnRightClick = null;
                trigger.OnPointerEnterAction = null;
                trigger.OnPointerExitAction = () => ClearMouseCardVisual(rect);
                if (!filled) continue;

                int index = i;
                view.SetDeckCard(code, appearance);
                view.SetDestinyBadge(area == PadArea.Main && appearance.destinyDraw);
                view.transform.Find("AspectBox/Unowned").gameObject.SetActive(shortage);
                trigger.CardCodeGetter = () => code;
                trigger.IndexGetter = () => index;
                trigger.OnPointerEnterAction = () => SetMouseCardVisual(rect, code);
                trigger.PreferTouchScroll = _mobileLayout;
                BindDeckTileActions(rect, trigger, area, index, code);
            }
            return missing;
        }

        private void BindDeckTileActions(RectTransform rect, CardDragTrigger trigger, PadArea area, int index, uint code)
        {
            void FocusCard()
            {
                RememberMouseCard(area, index);
                SetMouseCardVisual(rect, code);
            }

            trigger.OnLeftClick = () =>
            {
                FocusCard();
                if (!_mobileLayout) return;
                SelectMobileCard(code, trigger.SourceType);
                OpenExpandedDeckCard(code);
            };
            trigger.OnRightClick = () =>
            {
                FocusCard();
                ShowDeckCardActions(rect, trigger.SourceType, index, code);
            };
            trigger.OnTouchClick = _mobileLayout ? trigger.OnLeftClick : trigger.OnRightClick;
            trigger.OnTouchHold = trigger.OnTouchClick;
        }

        private void HandleCardDrop(DragContext context)
        {
            if (!_editorContent.activeInHierarchy || _workingDeck == null || _deckDialog.activeSelf ||
                _deckCardActions.activeSelf || _archModalRoot.activeSelf || _cardFilterDialog.activeSelf ||
                _mainSortMenu.activeSelf || _utilityOverlay != null || _expandedDeckOverlay.gameObject.activeSelf) return;
            var destination = FindDropZone(context.DropPosition);
            if (context.SourceType == DragSourceType.Trunk)
            {
                if (destination != DragSourceType.None)
                    TryAddCardToDeck(context.CardCode, destination, dropPosition: context.DropPosition);
                return;
            }

            var source = GetDeckList(context.SourceType);
            if (source == null || context.SourceIndex < 0 || context.SourceIndex >= source.Count ||
                source[context.SourceIndex] != context.CardCode)
                return;

            if (destination == DragSourceType.None)
            {
                if (CardDragManager.ContainsScreenPoint(_trunkPanel, context.DropPosition))
                    RemoveDeckCard(context.SourceType, context.SourceIndex, context.DropPosition);
                return;
            }

            int index = FindHoveredSlotIndex(GetDeckSlots(destination), context.DropPosition);
            MoveDeckCard(context.SourceType, context.SourceIndex, destination, index, context.DropPosition);
        }

        private void MoveDeckCard(DragSourceType sourceZone, int sourceIndex, DragSourceType targetZone, int targetIndex,
            Vector2? dropPosition = null)
        {
            var source = GetDeckList(sourceZone);
            var target = GetDeckList(targetZone);
            if (source == null || target == null || sourceIndex < 0 || sourceIndex >= source.Count) return;
            if (source == target)
            {
                ReorderDeckGroup(sourceZone, source, sourceIndex, targetIndex, dropPosition);
                return;
            }

            uint code = source[sourceIndex];
            if (!CanPlaceCard(CardDatabase.Get(code), targetZone, target)) return;
            int landing = FindDeckInsertionIndex(target, code, targetIndex);
            var sourcePositions = CaptureDeckPositions(sourceZone, source.Count);
            var targetPositions = CaptureDeckPositions(targetZone, target.Count);
            var origin = dropPosition.HasValue ? DropPose(dropPosition.Value) :
                sourceIndex < sourcePositions.Length ? sourcePositions[sourceIndex] : null;
            var appearance = _workingDeck.RemoveCard(source, sourceIndex);
            _workingDeck.InsertCard(target, landing, code, appearance);
            FinishDeckEdit();
            RevealDeckSlot(targetZone, landing);
            if (dropPosition.HasValue && !_padMode) RememberMouseCard(DeckPadArea(targetZone), landing);

            AnimateDeckSlots(sourceZone, sourceIndex, source.Count, sourcePositions, i => i + 1);
            AnimateDeckSlots(targetZone, landing + 1, target.Count, targetPositions, i => i - 1);
            if (_padMode && CurrentPadDeck == sourceZone)
            {
                var destination = DeckPadArea(targetZone);
                _padIndices[(int)destination] = landing;
                SwitchPadArea(destination);
            }
            var targetSlots = GetDeckSlots(targetZone);
            if (landing < targetSlots.Count) AnimateTransfer(code, origin, (RectTransform)targetSlots[landing].card.transform);
        }

        private void ReorderDeckGroup(DragSourceType zone, List<uint> cards, int sourceIndex, int targetIndex,
            Vector2? dropPosition)
        {
            if (targetIndex < 0) return;
            targetIndex = Mathf.Min(targetIndex, cards.Count - 1);
            int start = DeckGroupStart(cards, sourceIndex);
            int end = DeckGroupEnd(cards, sourceIndex);
            if (targetIndex >= start && targetIndex < end) return;

            bool forward = targetIndex >= end;
            int length = end - start;
            int startAfterMove = forward ? DeckGroupEnd(cards, targetIndex) - length : DeckGroupStart(cards, targetIndex);
            int landing = startAfterMove + sourceIndex - start;
            var oldPositions = CaptureDeckPositions(zone, cards.Count);
            var origin = dropPosition.HasValue ? DropPose(dropPosition.Value) :
                sourceIndex < oldPositions.Length ? oldPositions[sourceIndex] : null;
            uint code = cards[sourceIndex];
            var reordered = new List<uint>(cards);
            var group = reordered.GetRange(start, length);
            reordered.RemoveRange(start, length);
            reordered.InsertRange(startAfterMove, group);
            _workingDeck.ReorderCards(cards, reordered);
            FinishDeckEdit();
            RevealDeckSlot(zone, landing);
            var area = DeckPadArea(zone);
            if (_padMode && CurrentPadDeck == zone) _padIndices[(int)area] = landing;
            else if (dropPosition.HasValue && !_padMode) RememberMouseCard(area, landing);

            int firstChanged = Mathf.Min(start, startAfterMove);
            int lastChanged = Mathf.Max(start, startAfterMove) + length;
            AnimateDeckSlots(zone, firstChanged, lastChanged, oldPositions, index =>
            {
                if (index == landing) return -1;
                if (index >= startAfterMove && index < startAfterMove + length) return start + index - startAfterMove;
                return index + (forward ? length : -length);
            });
            var slots = GetDeckSlots(zone);
            if (landing < slots.Count) AnimateTransfer(code, origin, (RectTransform)slots[landing].card.transform);
        }

        private static uint DeckGroupKey(uint code) => CardDatabase.GetDeckPrintingKey(code);

        private static int DeckGroupStart(List<uint> cards, int index)
        {
            uint key = DeckGroupKey(cards[index]);
            while (index > 0 && DeckGroupKey(cards[index - 1]) == key) index--;
            return index;
        }

        private static int DeckGroupEnd(List<uint> cards, int index)
        {
            uint key = DeckGroupKey(cards[index]);
            int end = index + 1;
            while (end < cards.Count && DeckGroupKey(cards[end]) == key) end++;
            return end;
        }

        private static int FindDeckInsertionIndex(List<uint> cards, uint code, int requestedIndex)
        {
            uint key = DeckGroupKey(code);
            int match = cards.FindLastIndex(candidate => DeckGroupKey(candidate) == key);
            if (match >= 0) return match + 1;
            return requestedIndex >= 0 && requestedIndex < cards.Count
                ? DeckGroupStart(cards, requestedIndex) : cards.Count;
        }

        private void RemoveDeckCard(DragSourceType zone, int index, Vector2? dropPosition = null)
        {
            var cards = GetDeckList(zone);
            if (cards == null || index < 0 || index >= cards.Count) return;
            uint code = cards[index];
            var positions = CaptureDeckPositions(zone, cards.Count);
            var origin = dropPosition.HasValue ? DropPose(dropPosition.Value) :
                index < positions.Length ? positions[index] : null;
            _workingDeck.RemoveCard(cards, index);
            FinishDeckEdit();
            AnimateDeckSlots(zone, index, cards.Count, positions, i => i + 1);
            var visibleCopy = _trunkPool.Find(slot => slot.rt.gameObject.activeInHierarchy &&
                DeckGroupKey(slot.card.CurrentCode) == DeckGroupKey(code) &&
                RectTransformUtility.CalculateRelativeRectTransformBounds(_trunkViewport, slot.rt).Intersects(
                    new Bounds(_trunkViewport.rect.center, (Vector3)_trunkViewport.rect.size)));
            AnimateTransfer(code, origin, (RectTransform)visibleCopy.card?.transform, leavingDeck: true);
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
            CancelDeckDrag();
            EndDeckTransfer();
            DuelAudio.PlayClick();
            RefreshDeckGrids();
            UpdateVirtualTrunkSlots(_lastFirstRow, force: true);
        }

        private static void CancelDeckDrag()
        {
            var drag = CardDragManager.Instance;
            if (drag?.CurrentSource is DragSourceType.Trunk or DragSourceType.DeckMain or DragSourceType.DeckExtra or DragSourceType.DeckSide)
                drag.CancelDrag();
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
            uint targetKey = CollectionProgress.GetDeckCopyKey(code);
            bool SameName(uint card) => CollectionProgress.GetDeckCopyKey(card) == targetKey;
            return _workingDeck.main.Count(SameName) + _workingDeck.extra.Count(SameName) + _workingDeck.side.Count(SameName);
        }

        private uint FindAddableCardCopy(uint code, bool showReason = true)
        {
            if (CollectionProgress.RequiresOwnedCardsForDecks)
            {
                if (GetRemainingTrunkCopies(code, out long owned, out _) == 0)
                {
                    if (showReason)
                        ShowToast(owned == 0 ? "보유하지 않은 카드입니다." : $"보유 카드 {owned}장을 모두 사용 중입니다.");
                    return 0;
                }
                if (!HasUnusedCopy(code) && CardDatabase.TryGetDeckPrintings(code, out var printings))
                    code = Array.Find(printings, printing => HasUnusedCopy(printing.code))?.code ?? code;
            }

            int limit = CollectionProgress.DeckCopyLimit;
            if (CountInWorkingDeck(code) < limit) return code;
            if (showReason) ShowToast($"동명 카드는 세 덱을 합해 최대 {limit}장입니다.");
            return 0;
        }

        private bool HasUnusedCopy(uint code) =>
            HasUnusedEdition(code, overframe: false) || HasUnusedEdition(code, overframe: true);

        private void TryAddCardToDeck(uint code, DragSourceType destination = DragSourceType.None,
            RectTransform origin = null, Vector2? dropPosition = null)
        {
            AddDeckCopies(code, destination, 1, origin, dropPosition);
        }

        private void AddDeckCopies(uint code, DragSourceType destination, int quantity,
            RectTransform origin = null, Vector2? dropPosition = null)
        {
            if (_workingDeck == null || CardDatabase.Get(code) == null) return;
            code = FindAddableCardCopy(code);
            if (code == 0) return;
            var card = CardDatabase.Get(code);

            if (destination == DragSourceType.None)
                destination = card.IsExtra ? DragSourceType.DeckExtra : DragSourceType.DeckMain;
            var target = GetDeckList(destination);
            if (!CanPlaceCard(card, destination, target)) return;
            uint key = DeckGroupKey(code);
            bool SameCard(CompositeCardView view) => view != null && DeckGroupKey(view.CurrentCode) == key;
            if (origin != null && !SameCard(origin.GetComponent<CompositeCardView>())) origin = null;
            if (origin == null && CardVisual(_inspectedSource) is RectTransform inspected &&
                SameCard(inspected.GetComponent<CompositeCardView>()))
                origin = inspected;
            if (origin == null && SameCard(_previewCard)) origin = (RectTransform)_previewCard.transform;
            var from = dropPosition.HasValue ? DropPose(dropPosition.Value) : CardPose(origin);
            int landing = -1;
            var targetPositions = CaptureDeckPositions(destination, target.Count);
            var inserted = new List<int>();
            for (int copy = 0; copy < quantity; copy++)
            {
                if (copy > 0)
                {
                    code = FindAddableCardCopy(code, showReason: false);
                    if (code == 0 || !CanPlaceCard(CardDatabase.Get(code), destination, target, showReason: false)) break;
                }
                landing = FindDeckInsertionIndex(target, code, -1);
                _workingDeck.InsertCard(target, landing, code, AppearanceForAddition(code));
                for (int i = 0; i < inserted.Count; i++)
                    if (inserted[i] >= landing) inserted[i]++;
                inserted.Add(landing);
            }
            FinishDeckEdit();
            RevealDeckSlot(destination, landing);
            int PreviousIndex(int index) => inserted.Contains(index) ? -1 : index - inserted.Count(added => added < index);
            AnimateDeckSlots(destination, inserted.Min(), target.Count, targetPositions, PreviousIndex);
            var landingCard = GetDeckSlots(destination)[landing].card;
            if (!_mobileLayout || landingCard.isActiveAndEnabled)
                AnimateTransfer(target[landing], from, (RectTransform)landingCard.transform);
            if (_mobileLayout) ShowToast($"{CardDatabase.GetName(target[landing])} · {inserted.Count}장 추가");
        }

        private bool CanPlaceCard(CardInfo card, DragSourceType destination, List<uint> target, bool showReason = true)
        {
            if (card == null || target == null || (card.type & YgoConstants.TYPE_TOKEN) != 0) return false;
            if (destination != DragSourceType.DeckSide && card.IsExtra != (destination == DragSourceType.DeckExtra))
            {
                if (showReason) ShowToast(card.IsExtra ? "엑스트라 또는 사이드 덱에 넣으세요." : "메인 또는 사이드 덱에 넣으세요.");
                return false;
            }

            int capacity = destination == DragSourceType.DeckMain ? MainDeckCapacity : OtherDeckCapacity;
            if (target.Count < capacity) return true;
            if (showReason) ShowToast($"이 덱은 {capacity}장까지 담을 수 있습니다.");
            return false;
        }

        private long GetRemainingTrunkCopies(uint code, out long owned, out int inDeck)
        {
            if (CollectionProgress.AllDeckCardsAvailable)
            {
                owned = CollectionProgress.IsCraftableCard(CardDatabase.Get(code)) ? CollectionProgress.DeckCopyLimit : 0;
                inDeck = CountInWorkingDeck(code);
                return Math.Max(0, owned - inDeck);
            }
            uint key = DeckGroupKey(code);
            bool SameCard(uint candidate) => DeckGroupKey(candidate) == key;
            owned = CollectionProgress.GetOwnedDeckCopies(code);
            inDeck = _workingDeck.main.Count(SameCard) + _workingDeck.extra.Count(SameCard) + _workingDeck.side.Count(SameCard);
            return Math.Max(0, owned - inDeck);
        }

        private void RefreshPreviewOwnership()
        {
            if (_dustBalance != null)
                _dustBalance.text = $"제작 가루 {CollectionProgress.CraftDust:N0}";
            bool visible = CollectionProgress.Initialized && _workingDeck != null && CardDatabase.Get(_inspectedCode) != null;
            _previewOwnership.transform.parent.gameObject.SetActive(visible);
            float top = visible ? -33f : -5f;
            _previewScroll.viewport.offsetMax = new Vector2(-21f, top);
            ((RectTransform)_previewScroll.verticalScrollbar.transform).offsetMax = new Vector2(-2f, top);
            if (!visible) return;

            long remaining = GetRemainingTrunkCopies(_inspectedCode, out long owned, out int inDeck);
            _previewOwnership.text = CollectionProgress.AllDeckCardsAvailable ? $"추가 가능 {remaining}장 · 덱에 {inDeck}장" :
                owned == 0 ? $"{UnownedLabel} · 덱에 {inDeck}장" :
                    $"보관함 {remaining}장 · 덱에 {inDeck}장 · 보유 {owned}장";
            if (!CollectionProgress.AllDeckCardsAvailable && inDeck > owned)
                _previewOwnership.text += $" · 부족 {inDeck - owned}장";
            _previewOwnership.color = inDeck > owned || owned == 0 ? DuelUiKit.RedAccent : DuelUiKit.TextPrimary;
            var rarity = CardRarityCatalog.GetRarity(_inspectedCode);
            _previewRarity.text = CardRarityCatalog.GetLabel(rarity);
            _previewRarity.color = CardRarityStyle.GetColor(rarity);
        }

        private void InspectCard(uint code)
        {
            _inspectedCode = code;
            bool changed = _previewCard.CurrentCode != code;
            RefreshInspectedAppearance(code);
            _previewTrade.Bind(code);
            RefreshPreviewOwnership();
            if (!changed) return;
            var ci = CardDatabase.Get(code);
            _previewCard.GetComponent<DeckCardVisualMotion>().Pulse();

            _previewName.text = ci?.name ?? "";
            _previewStats.text = ci?.FormatStatsLineKR() ?? "";
            _previewDesc.text = ci?.desc ?? "";
            _previewScroll.StopMovement();
            _previewScroll.content.anchoredPosition = Vector2.zero;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_previewScroll.content);
            UpdateDeckScrollbar(_previewScroll);

            Sprite raceSp = ci == null ? null : ci.IsMonster ? DuelUiKit.GetRaceSprite(ci.race) : DuelUiKit.GetSpellTrapSprite(ci.type);
            Sprite attributeSp = ci == null ? null : ci.IsMonster ? DuelUiKit.GetAttributeSprite(ci.attr) :
                DuelUiIcons.FindCardIdentity((ci.type & YgoConstants.TYPE_SPELL) != 0 ? "attribute/spell" : "attribute/trap");
            _previewRaceIcon.gameObject.SetActive(raceSp != null);
            _previewRaceIcon.sprite = raceSp;
            _previewAttributeIcon.gameObject.SetActive(attributeSp != null);
            _previewAttributeIcon.sprite = attributeSp;
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
                    int createdIndex = DeckRepository.UserDecks.Count - 1;
                    if (IsDeckPickerOpen) EnterDeckEditor(createdIndex);
                    else
                    {
                        _userDeckIdx = createdIndex;
                        LoadWorkingDeckFromCurrent();
                    }
                    ShowToast("새 빈 덱을 생성했습니다.");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"[DeckEditor] Could not create a deck: {ex.Message}");
                    const string message = "덱을 만들 수 없습니다. 저장 폴더를 확인하세요.";
                    if (IsDeckPickerOpen) _deckPickerStatus.text = message;
                    else ShowToast(message);
                }
            });
        }

        private string DeckNameForSave => string.IsNullOrWhiteSpace(_deckNameInput.text)
            ? "커스텀 덱" : _deckNameInput.text.Trim();

        private bool HasUnsavedChanges()
        {
            if (_workingDeck == null) return false;
            var saved = DeckRepository.UserDecks.FirstOrDefault(deck => deck.id == _workingDeck.id);
            if (saved == null || DeckNameForSave != saved.name) return true;
            var baseline = _workingDeckBaseline?.id == saved.id ? _workingDeckBaseline : saved;
            bool Changed(List<uint> current, List<uint> previous)
            {
                var ordered = previous.Select((code, index) => (Code: code, Appearance: baseline.GetAppearance(previous, index)));
                var working = current.Select((code, index) => (Code: code, Appearance: _workingDeck.GetAppearance(current, index)));
                return !working.SequenceEqual(ordered);
            }
            return Changed(_workingDeck.main, baseline.main) || Changed(_workingDeck.extra, baseline.extra) ||
                Changed(_workingDeck.side, baseline.side);
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
            _workingDeckBaseline = snapshot;
            _deckNameInput.SetTextWithoutNotify(snapshot.name);
            RefreshSaveLabel();
            ShowToast(GameAppController.CanStartWithDeck(snapshot, out string reason)
                ? $"'{snapshot.name}' 저장 완료" : $"저장 완료 · 출전 불가 · {reason}");
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
                    CloseDeckDialog();
                    next();
                },
                "저장 안 함", () =>
                {
                    CloseDeckDialog();
                    next();
                });
        }

        private void SortWorkingDeck(bool byFrame = true, bool byName = false)
        {
            var main = _workingDeck.main.ToArray();
            var extra = _workingDeck.extra.ToArray();
            var side = _workingDeck.side.ToArray();
            var mainPositions = CaptureDeckPositions(DragSourceType.DeckMain, main.Length);
            var extraPositions = CaptureDeckPositions(DragSourceType.DeckExtra, extra.Length);
            var sidePositions = CaptureDeckPositions(DragSourceType.DeckSide, side.Length);
            CancelDeckDrag();
            EndDeckTransfer();
            OrderWorkingDeckCards(byName ? DeckMainSortMode.Name : byFrame ? DeckMainSortMode.Frame : DeckMainSortMode.Auto);
            RefreshDeckGrids();
            AnimateOrderedDeck(DragSourceType.DeckMain, _workingDeck.main, main, mainPositions);
            AnimateOrderedDeck(DragSourceType.DeckExtra, _workingDeck.extra, extra, extraPositions);
            AnimateOrderedDeck(DragSourceType.DeckSide, _workingDeck.side, side, sidePositions);
            ShowToast(byFrame && !byName ? "전체 덱 정렬 · 카드 프레임별" : "덱 정렬 완료");
        }

        private void OrderWorkingDeckCards(DeckMainSortMode mode)
        {
            foreach (var section in new[] { _workingDeck.main, _workingDeck.extra, _workingDeck.side })
            {
                var ordered = OrderDeckGroups(section, mode);
                if (mode == DeckMainSortMode.Name && section == _workingDeck.main)
                    ordered = ordered.OrderBy(code =>
                    {
                        var card = CardDatabase.Get(code);
                        return card == null ? 3 : card.IsMonster ? 0 : card.IsSpell ? 1 : 2;
                    }).ToArray();
                _workingDeck.ReorderCards(section, ordered);
            }
        }

        private void ShuffleWorkingDeck()
        {
            if (_workingDeck.main.Count <= 1) return;
            var main = _workingDeck.main.ToArray();
            var groups = main.GroupBy(DeckGroupKey).ToArray();
            if (groups.Length <= 1) return;
            var positions = CaptureDeckPositions(DragSourceType.DeckMain, main.Length);
            CancelDeckDrag();
            EndDeckTransfer();
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                byte[] rb = new byte[4];
                for (int i = groups.Length - 1; i > 0; i--)
                {
                    rng.GetBytes(rb);
                    int j = (int)(BitConverter.ToUInt32(rb, 0) % (uint)(i + 1));
                    (groups[i], groups[j]) = (groups[j], groups[i]);
                }
            }
            _workingDeck.ReorderCards(_workingDeck.main, groups.SelectMany(group => group));
            DuelAudio.PlayDraw();
            RefreshDeckGrids();
            AnimateOrderedDeck(DragSourceType.DeckMain, _workingDeck.main, main, positions);
            ShowToast("메인 덱에서 카드 그룹의 순서를 섞었습니다.");
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
                CloseDeckDialog();
                _userDeckIdx = Mathf.Clamp(_userDeckIdx, 0, DeckRepository.UserDecks.Count - 1);
                LoadWorkingDeckFromCurrent();
                ShowToast("덱을 삭제했습니다.");
            });
        }

        private void ShowToast(string msg)
        {
            if (_toastText == null) return;
            _toastText.text = msg ?? "";
            if (_toastText.text.Length > 0) UiEntranceMotion.Play(_toastText.gameObject, .56f, .14f);
        }

        private void BuildDeckDialog(Transform parent)
        {
            var dim = DuelUiKit.FillRect("Modal_DeckChanges", parent);
            _deckDialog = dim.gameObject;
            DuelUiKit.AddImage(_deckDialog, DuelUiKit.WhiteSprite, DeckEditorStyle.Scrim);
            var window = DeckEditorStyle.Panel(dim, "Window", new Vector2(0.29f, 0.36f), new Vector2(0.71f, 0.64f));
            var title = DuelUiKit.CreateRect("Title", window, new Vector2(0.05f, 0.72f), new Vector2(0.95f, 0.93f),
                Vector2.zero, Vector2.zero);
            _deckDialogTitle = DuelUiKit.AddText(title, "Text", "", 23, TextAnchor.MiddleCenter,
                DuelUiKit.GoldAccent, FontStyle.Bold);
            var message = DuelUiKit.CreateRect("Message", window, new Vector2(0.07f, 0.33f), new Vector2(0.93f, 0.71f),
                Vector2.zero, Vector2.zero);
            _deckDialogMessage = DuelUiKit.AddText(message, "Text", "", 17, TextAnchor.MiddleCenter, DuelUiKit.TextPrimary);
            _deckDialogMessage.supportRichText = false;

            Button AddButton(string name, string label, float left, float right, Action action, bool gold = false)
            {
                var holder = DuelUiKit.CreateRect(name, window, new Vector2(left, 0.10f), new Vector2(right, 0.29f),
                    Vector2.zero, Vector2.zero);
                return DeckEditorStyle.Button(holder, "Button", label, action, 15, gold);
            }
            _deckDialogCancel = AddButton("Cancel", "취소", 0.05f, 0.33f, () => CloseDeckDialog());
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
                    if (!button.IsInteractable() || !button.isActiveAndEnabled) return;
                    DuelAudio.PlayClick();
                    action();
                });
            }

            _deckDialogTitle.text = title;
            _deckDialogMessage.text = message;
            SetAction(_deckDialogPrimary, primaryLabel, primary);
            SetAction(_deckDialogSecondary, secondaryLabel, secondary);
            var events = EventSystem.current;
            if (!_deckDialog.activeSelf) _deckDialogReturnFocus = events?.currentSelectedGameObject;
            _deckDialog.SetActive(true);
            UiEntranceMotion.Play(_deckDialog);
            Button previous = null;
            foreach (var choice in new[] { _deckDialogCancel, _deckDialogSecondary, _deckDialogPrimary })
            {
                if (!choice.gameObject.activeSelf) continue;
                choice.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = previous };
                if (previous != null)
                {
                    var navigation = previous.navigation;
                    navigation.selectOnRight = choice;
                    previous.navigation = navigation;
                }
                previous = choice;
            }
            events?.SetSelectedGameObject(_deckDialogCancel.gameObject);
        }

        private void CloseDeckDialog(bool restoreFocus = true)
        {
            if (!_deckDialog.activeSelf) return;
            var returnFocus = _deckDialogReturnFocus;
            _deckDialogReturnFocus = null;
            _deckDialog.SetActive(false);
            var events = EventSystem.current;
            if (events == null) return;
            var selected = events.currentSelectedGameObject;
            if (restoreFocus && returnFocus != null && returnFocus.TryGetComponent<Selectable>(out var control) &&
                control.isActiveAndEnabled && control.IsInteractable())
                events.SetSelectedGameObject(returnFocus);
            else if (selected != null && selected.transform.IsChildOf(_deckDialog.transform))
                events.SetSelectedGameObject(null);
        }

        // ==================== PURE ARCHETYPE EXAMPLE DECK BROWSER ====================
        private void BuildArchetypeModal(Transform parent)
        {
            var dim = DuelUiKit.FillRect("Modal_ArchetypeBrowser", parent);
            _archModalRoot = dim.gameObject;
            DeckEditorStyle.Fill(dim, DeckEditorStyle.Scrim);
            var backdrop = dim.gameObject.AddComponent<Button>();
            backdrop.navigation = new Navigation { mode = Navigation.Mode.None };
            backdrop.onClick.AddListener(() => CloseArchetypeModal());

            var win = DeckEditorStyle.Panel(dim, "Win",
                new Vector2(.105f, .075f), new Vector2(.895f, .925f));
            win.gameObject.AddComponent<Button>().interactable = false; // Catch clicks on the panel background without taking focus.

            var hdr = DuelUiKit.CreateRect("Hdr", win, new Vector2(.034f, .854f), new Vector2(.966f, .956f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddImage(hdr.gameObject, DuelUiKit.WhiteSprite, DeckEditorStyle.Header, false, false);
            var title = DuelUiKit.CreateRect("Title", hdr, new Vector2(.17f, .43f), new Vector2(.83f, .97f),
                Vector2.zero, Vector2.zero);
            _archTitle = DuelUiKit.AddFittedText(title, "T", "", 26, 20, TextAnchor.MiddleCenter,
                DuelUiKit.TextPrimary, FontStyle.Bold);
            var subtitle = DuelUiKit.CreateRect("Subtitle", hdr, new Vector2(.17f, .08f), new Vector2(.83f, .46f),
                Vector2.zero, Vector2.zero);
            _archSubtitle = DuelUiKit.AddFittedText(subtitle, "Sub", "", 15, 12,
                TextAnchor.MiddleCenter, DuelUiKit.TextPrimary);

            var closeRt = DuelUiKit.CreateRect("Close", hdr, new Vector2(.855f, .20f), new Vector2(.985f, .80f),
                Vector2.zero, Vector2.zero);
            _archCloseButton = DeckEditorStyle.Button(closeRt, "B", "닫기", () => CloseArchetypeModal(), 16);

            var filterRow = DuelUiKit.CreateRect("FilterRow", win, new Vector2(.034f, .777f), new Vector2(.966f, .839f),
                Vector2.zero, Vector2.zero);
            _archSearchInp = DeckEditorStyle.Input(
                DuelUiKit.CreateRect("ArchSearch", filterRow, Vector2.zero, new Vector2(.58f, 1f), Vector2.zero, Vector2.zero),
                "S", "카드군 이름·소속 카드명 검색", 17);
            _archSearchInp.onValueChanged.AddListener(_ => RefreshArchetypeModal());

            var sortBtn = DeckEditorStyle.Button(
                DuelUiKit.CreateRect("SortArch", filterRow, new Vector2(.60f, 0f), new Vector2(.80f, 1f), Vector2.zero, Vector2.zero),
                "B", "정렬 · 카드 수", () =>
                {
                    _archSortMode = (_archSortMode + 1) % 2;
                    _archSortBtnText.text = _archSortMode == 0 ? "정렬 · 카드 수" : "정렬 · 가나다순";
                    RefreshArchetypeModal();
                }, 16);
            _archSortBtnText = sortBtn.GetComponentInChildren<Text>();

            var count = DuelUiKit.CreateRect("ArchCnt", filterRow, new Vector2(.82f, .08f), new Vector2(1f, .92f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddImage(count.gameObject, DuelUiKit.WhiteSprite, DeckEditorStyle.Surface, false, false);
            _archCountText = DuelUiKit.AddFittedText(count, "C", "0개 카드군", 17, 13, TextAnchor.MiddleCenter,
                DuelUiKit.TextPrimary, FontStyle.Bold);

            // Virtualized Scroll Grid (3 cols x 7 rows = 21 pooled tiles)
            var scrollHolder = DuelUiKit.CreateRect("ArchScrollHolder", win, new Vector2(.034f, .042f), new Vector2(.966f, .761f),
                Vector2.zero, Vector2.zero);
            _archScroll = scrollHolder.gameObject.AddComponent<ScrollRect>();
            _archScroll.horizontal = false;
            _archScroll.vertical = true;
            _archScroll.scrollSensitivity = 36f;
            _archScroll.movementType = ScrollRect.MovementType.Clamped;

            _archViewport = DuelUiKit.CreateRect("VP", scrollHolder, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-18f, 0f));
            _archViewport.gameObject.AddComponent<RectMask2D>();
            _archScroll.viewport = _archViewport;

            _archContent = DuelUiKit.CreateRect("Content", _archViewport, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            _archContent.pivot = new Vector2(0f, 1f);
            _archScroll.content = _archContent;
            _archScroll.verticalScrollbar = AddDeckScrollbar(scrollHolder);
            _archScroll.verticalScrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            _archScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            for (int r = 0; r < ArchPoolRows; r++)
            {
                for (int c = 0; c < ArchCols; c++)
                {
                    var tile = DuelUiKit.CreateBorderedPanel(_archContent, $"ArchTile_{r}_{c}",
                        new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero,
                        DeckEditorStyle.Inset, DeckEditorStyle.Edge);
                    tile.pivot = new Vector2(0, 1);

                    var thumbRt = DuelUiKit.CreateRect("Card", tile, new Vector2(.025f, .043f), new Vector2(.365f, .957f), Vector2.zero, Vector2.zero);
                    var cv = CompositeCardView.Create(thumbRt, "V", false);
                    var inspectImg = DuelUiKit.AddImage(thumbRt.gameObject, DuelUiKit.WhiteSprite, Color.clear);
                    var inspectBtn = thumbRt.gameObject.AddComponent<Button>();
                    inspectBtn.targetGraphic = inspectImg;

                    var tTxt = DuelUiKit.AddFittedText(
                        DuelUiKit.CreateRect("Name", tile, new Vector2(.39f, .62f), new Vector2(.97f, .94f), Vector2.zero, Vector2.zero),
                        "N", "", 19, 13, TextAnchor.MiddleLeft, DuelUiKit.TextPrimary, FontStyle.Bold);

                    var sTxt = DuelUiKit.AddFittedText(
                        DuelUiKit.CreateRect("Info", tile, new Vector2(.39f, .38f), new Vector2(.97f, .62f), Vector2.zero, Vector2.zero),
                        "S", "", 14, 11, TextAnchor.MiddleLeft, DuelUiKit.TextPrimary);

                    var btnRt = DuelUiKit.CreateRect("LoadBtn", tile, new Vector2(.39f, .07f), new Vector2(.96f, .36f),
                        Vector2.zero, Vector2.zero);
                    var loadBtn = DeckEditorStyle.Button(btnRt, "B", "", null, 15, primary: true);

                    _archPool.Add((tile, cv, tTxt, sTxt, loadBtn, inspectBtn));
                }
            }

            _archScroll.onValueChanged.AddListener(_ => OnArchModalScrolled());
            _archModalRoot.SetActive(false);
        }

        private void OpenArchetypeModal() => ShowArchetypeModal(forSearch: false);

        private void OpenArchetypeSearch() => ShowArchetypeModal(forSearch: true);

        private void ShowArchetypeModal(bool forSearch)
        {
            if (_archModalRoot.activeSelf) return;
            CloseDetailedSearch();
            DuelAudio.PlayClick();
            if (_archForSearch != forSearch) _archSearchInp.SetTextWithoutNotify("");
            _archForSearch = forSearch;
            _archTitle.text = forSearch ? "카드군 검색" : "카드군 예시 덱";
            _archSubtitle.text = forSearch ? "소속 카드와 관련 지원 카드를 보관함에서 확인합니다." :
                "메인·엑스트라 덱을 교체하고 사이드 덱을 비웁니다.";
            var events = EventSystem.current;
            _archReturnFocus = events?.currentSelectedGameObject;
            _archModalRoot.SetActive(true);
            UiEntranceMotion.Play(_archModalRoot);
            RefreshArchetypeModal();
            events?.SetSelectedGameObject(_archCloseButton.gameObject);
        }

        private void CloseArchetypeModal(bool restoreFocus = true)
        {
            if (!_archModalRoot.activeSelf) return;
            _archModalRoot.SetActive(false);
            var returnFocus = _archReturnFocus;
            _archReturnFocus = null;
            var events = EventSystem.current;
            if (events == null) return;
            if (restoreFocus && returnFocus != null && returnFocus.TryGetComponent<Selectable>(out var control) &&
                control.isActiveAndEnabled && control.IsInteractable())
                events.SetSelectedGameObject(returnFocus);
            else if (events.currentSelectedGameObject?.transform.IsChildOf(_archModalRoot.transform) == true)
                events.SetSelectedGameObject(null);
        }

        private void RefreshArchetypeModal()
        {
            _archResults.Clear();
            _archResults.AddRange(StarterArchetypeCatalog.Search(_archSearchInp.text, includeSmallGroups: _archForSearch));

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
            _archRowHeight = Mathf.Max(108f, Mathf.Floor(_archViewport.rect.height / 4f));
            _archContent.sizeDelta = new Vector2(0f, _archTotalRows * _archRowHeight);
            _archCountText.text = $"총 {_archResults.Count:N0}개 카드군";
            _archScroll.verticalNormalizedPosition = 1f;
            UpdateDeckScrollbar(_archScroll);
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

        private static int ArchetypePoolSlot(int row, int col) => (row % ArchPoolRows) * ArchCols + col;

        private void UpdateVirtualArchSlots(int firstRow, bool force = false)
        {
            if (firstRow == _archLastFirstRow && !force) return;
            _archLastFirstRow = firstRow;

            float vpWidth = _archViewport.rect.width > 10 ? _archViewport.rect.width : 1320f;
            float colW = vpWidth / ArchCols;

            for (int r = 0; r < ArchPoolRows; r++)
            {
                int targetRow = firstRow + r;
                float y = -targetRow * _archRowHeight;

                for (int c = 0; c < ArchCols; c++)
                {
                    // Rows that remain visible keep the same buttons and controller focus.
                    int poolIdx = ArchetypePoolSlot(targetRow, c);
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
                    titleTxt.text = arch.name;
                    subTxt.text = $"{arch.TotalUniqueCount}종 · 메인 {arch.MainPoolCount} / 엑스트라 {arch.ExtraPoolCount}";

                    inspectBtn.onClick.RemoveAllListeners();
                    loadBtn.onClick.RemoveAllListeners();
                    loadBtn.GetComponentInChildren<Text>().text = _archForSearch ? "카드 보기" :
                        CollectionProgress.RequiresOwnedCardsForDecks ? "보유분 불러오기" : "불러오기";
                    if (_archForSearch)
                    {
                        inspectBtn.onClick.AddListener(() => ApplyArchetypeSearch(arch));
                        loadBtn.onClick.AddListener(() => ApplyArchetypeSearch(arch));
                    }
                    else
                    {
                        inspectBtn.onClick.AddListener(() => InspectCard(arch.representativeCode));
                        loadBtn.onClick.AddListener(() => ApplyPureArchetypeExampleDeck(arch));
                    }
                }
            }
            DeckPadUi.LimitKeyboardNavigation(_archModalRoot.transform);
        }

        private void ApplyArchetypeSearch(ArchetypeInfo group)
        {
            SetTrunkSource(archetype: group);
            CloseArchetypeModal();
            if (_padMode)
            {
                SwitchPadArea(PadArea.Trunk);
                DrawPadCardFocus();
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
            bool ownedOnly = CollectionProgress.RequiresOwnedCardsForDecks;
            var recipe = new UserDeck { main = main, extra = extra };
            if (ownedOnly)
            {
                recipe = new UserDeck();
                var usedByName = new Dictionary<uint, int>();
                var usedByCode = new Dictionary<uint, int>();
                void AddOwnedCopies(IEnumerable<uint> codes, List<uint> section)
                {
                    foreach (uint code in codes)
                    {
                        uint key = CollectionProgress.GetDeckCopyKey(code);
                        int copies = usedByCode.GetValueOrDefault(code);
                        int sameName = usedByName.GetValueOrDefault(key);
                        if (copies >= CollectionProgress.GetTotalOwnedCount(code) || sameName >= CollectionProgress.DeckCopyLimit)
                            continue;
                        usedByCode[code] = copies + 1;
                        usedByName[key] = sameName + 1;
                        var appearance = new DeckCardAppearance { overframe = copies >= CollectionProgress.GetOwnedCount(code) };
                        recipe.InsertCard(section, section.Count, code, appearance.WithAvailableArtwork(code));
                    }
                }

                AddOwnedCopies(main, recipe.main);
                AddOwnedCopies(extra, recipe.extra);
                if (recipe.main.Count + recipe.extra.Count == 0)
                {
                    CloseArchetypeModal();
                    ShowToast("이 예시 덱에 넣을 보유 카드가 없습니다.");
                    return;
                }
            }

            BeforeReplacingEdits(() =>
            {
                _workingDeck.name = $"[순수] {arch.name}";
                _workingDeck.ClearCards(_workingDeck.side);
                _workingDeck.main = recipe.main;
                _workingDeck.extra = recipe.extra;
                _workingDeck.mainAppearances = recipe.mainAppearances;
                _workingDeck.extraAppearances = recipe.extraAppearances;
                _deckNameInput.SetTextWithoutNotify(_workingDeck.name);
                SortWorkingDeck();
                SetTrunkSource(archetype: arch);
                if (arch.representativeCode != 0)
                    InspectCard(arch.representativeCode);
                CloseArchetypeModal();
                string action = ownedOnly ? "보유 카드로 구성" : "불러옴";
                ShowToast($"'{_workingDeck.name}' {action} · 메인 {recipe.main.Count}장 / 엑스트라 {recipe.extra.Count}장");
            });
        }
    }
}
