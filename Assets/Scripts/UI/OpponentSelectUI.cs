using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public class OpponentSelectUI
    {
        public GameObject Root { get; private set; }
        public GameObject TurnOrderRoot { get; private set; }

        private readonly Action _returnToMenu;
        private readonly Action<OpponentCharacter, PresetDeckInfo, UserDeck, bool> _startMatch;

        private int _seriesFilter = -1;
        private string _searchQuery = "";
        private int _page = 0;
        private const int GridColumns = 8;
        private const int GridRows = 3;
        private const int PageSize = GridColumns * GridRows;
        private const float MyDeckRowHeight = 52f;
        private const float MyDeckRowStep = 58f;
        private const uint NumeronNetwork = 41418852;
        private const uint CyberDragonCore = 23893227;
        private const uint CyberTwinDragon = 74157028;
        private const uint AncientGearFusion = 64061284;
        private const uint ChaosAncientGearGiant = 51788412;

        private static readonly HashSet<uint> BoardBreakers = new()
        {
            12580477, // Raigeki
            14532163, // Lightning Storm
            15693423, // Evenly Matched
            18144506, // Harpie's Feather Duster
            54693926, // Dark Ruler No More
            55063751, 29726552, 36956512, 48770333, 28674152, // Kaijus
        };

        private readonly List<OpponentCharacter> _filteredChars = new List<OpponentCharacter>();
        private readonly List<GameObject> _gridSlots = new List<GameObject>();
        private readonly Dictionary<int, Button> _tabButtons = new Dictionary<int, Button>();

        private OpponentCharacter _selectedChar;
        private readonly List<PresetDeckInfo> _charDecks = new List<PresetDeckInfo>();
        private int _selectedCharDeckIdx = 0;
        private readonly List<UserDeck> _playableMyDecks = new List<UserDeck>();
        private readonly List<(Button Button, Text Name, Text Counts)> _myDeckRows = new();
        private UserDeck _selectedMyDeck;
        private int _selectedMyDeckIdx;

        private GameObject _browserRoot;
        private GameObject _deckSetupRoot;
        private Image _detailBust;
        private Text _detailName;
        private Text _oppDeckName;
        private Text _oppDeckPosition;
        private Text _oppDeckStats;
        private Button _cloneOppButton;
        private Button _previousOppDeckButton;
        private Button _nextOppDeckButton;
        private Button _startDuelButton;
        private Button _backToOpponentsButton;
        private Button _previousPageButton;
        private Button _nextPageButton;
        private ScrollRect _myDeckList;
        private Text _emptyMyDecks;
        private InputField _searchInput;
        private CanvasGroup _opponentControls;
        private CompositeCardView _sigCardPreview;
        private Text _pageLabel;
        private Text _browserStatus;
        private Text _statusToast;

        private Text _tossStatus;
        private Text _tossMatchup;
        private RectTransform _tossPanel;
        private RectTransform _orderChoices;
        private Button _chooseFirst;
        private Button _tossCancel;
        private TurnOrderCoin _coin;
        private bool _tossing;
        private bool _playerWonToss;
        private float _tossAge;

        public OpponentSelectUI(GameAppController app, Transform canvasParent)
            : this(canvasParent, () => app?.ShowScreen(AppScreen.MainMenu),
                (opponent, deck, playerDeck, playerGoesFirst) => app?.StartDuelMatch(opponent, deck, playerDeck, playerGoesFirst))
        {
        }

        internal OpponentSelectUI(Transform canvasParent, Action returnToMenu,
            Action<OpponentCharacter, PresetDeckInfo, UserDeck, bool> startMatch)
        {
            _returnToMenu = returnToMenu;
            _startMatch = startMatch;
            BuildOpponentScreen(canvasParent);
            BuildTurnOrderOverlay(canvasParent);
        }

        public void Open()
        {
            Root.SetActive(true);
            TurnOrderRoot.SetActive(false);
            _deckSetupRoot.SetActive(false);
            _browserRoot.SetActive(true);
            _opponentControls.interactable = true;
            _statusToast.text = "";
            _selectedMyDeck = null;
            ApplyFilter();
            FocusCurrentCharacter();
        }

        internal InputField SearchInput => _searchInput;
        internal bool IsDeckSetupOpen => _deckSetupRoot.activeSelf;

        internal void CloseDeckSetup()
        {
            _deckSetupRoot.SetActive(false);
            _browserRoot.SetActive(true);
            FocusCurrentCharacter();
        }

        public void ShowStatus(string message, bool isError = false)
        {
            var label = IsDeckSetupOpen ? _statusToast : _browserStatus;
            label.color = isError ? DuelUiKit.RedAccent : DuelUiKit.GreenAccent;
            label.text = message;
        }


        private void BuildOpponentScreen(Transform parent)
        {
            var root = DuelUiKit.FillRect("Screen_OpponentSelect", parent);
            Root = root.gameObject;
            _opponentControls = Root.AddComponent<CanvasGroup>();

            var bg = DuelUiKit.FillRect("Bg", root);
            DuelUiKit.AddImage(bg.gameObject, DuelUiChrome.Backdrop, Color.white, false, false);
            var browser = DuelUiKit.FillRect("OpponentBrowser", root);
            _browserRoot = browser.gameObject;
            DuelUiKit.AddMenuHeader(browser, "상대 선택");
            var count = DuelUiKit.CreateBorderedPanel(browser, "OpponentCount",
                new Vector2(.02f, .918f), new Vector2(.235f, .98f), Vector2.zero, Vector2.zero);
            _browserStatus = DuelUiKit.AddFittedText(count, "Value", "", 17, 12,
                TextAnchor.MiddleCenter, DuelUiKit.TextPrimary, FontStyle.Bold);
            var back = DuelUiKit.CreateRect("Back", browser, new Vector2(.858f, .922f), new Vector2(.981f, .977f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.CreateButton(back, "B", "메인 메뉴", _returnToMenu, 16);

            var tabs = DuelUiKit.CreateMenuPanel(browser, "SeriesTabs", new Vector2(.02f, .825f), new Vector2(.67f, .895f));
            (int id, string label)[] series =
            {
                (-1, "전체"), (0, "DM"), (1, "GX"), (2, "5D's"), (3, "ZEXAL"),
                (4, "ARC-V"), (5, "VRAINS"), (6, "2026 메타")
            };
            float width = 1f / series.Length;
            for (int i = 0; i < series.Length; i++)
            {
                int id = series[i].id;
                var slot = DuelUiKit.CreateRect($"Tab_{id}", tabs,
                    new Vector2(i * width + .004f, .10f), new Vector2((i + 1) * width - .004f, .9f), Vector2.zero, Vector2.zero);
                _tabButtons[id] = DuelUiKit.CreateButton(slot, "B", series[i].label, () =>
                {
                    _seriesFilter = id;
                    _page = 0;
                    ApplyFilter();
                }, 15);
            }

            var search = DuelUiKit.CreateRect("SearchBox", browser, new Vector2(.68f, .825f), new Vector2(.98f, .895f),
                Vector2.zero, Vector2.zero);
            _searchInput = DuelUiKit.CreateInputField(search, "SearchInput", "상대 이름 또는 덱 검색...", 16);
            _searchInput.onValueChanged.AddListener(value =>
            {
                _searchQuery = value ?? "";
                _page = 0;
                ApplyFilter();
            });

            var grid = DuelUiKit.CreateMenuPanel(browser, "CharGridPanel", new Vector2(.02f, .03f), new Vector2(.98f, .81f));
            var gridArea = DuelUiKit.CreateRect("GridArea", grid, new Vector2(.018f, .10f), new Vector2(.982f, .96f),
                Vector2.zero, Vector2.zero);
            BuildGridSlots(gridArea);
            var pageBar = DuelUiKit.CreateRect("PageBar", grid, new Vector2(.02f, .038f), new Vector2(.98f, .10f),
                Vector2.zero, Vector2.zero);
            var prev = DuelUiKit.CreateRect("Prev", pageBar, new Vector2(.02f, .04f), new Vector2(.20f, .96f), Vector2.zero, Vector2.zero);
            _previousPageButton = DuelUiKit.CreateButton(prev, "B", "◀ 이전 페이지", () => ChangePage(-1), 15);
            var page = DuelUiKit.CreateRect("PageTxt", pageBar, new Vector2(.24f, 0), new Vector2(.76f, 1), Vector2.zero, Vector2.zero);
            _pageLabel = DuelUiKit.AddText(page, "L", "", 16, TextAnchor.MiddleCenter, DuelUiKit.TextPrimary, FontStyle.Bold);
            var next = DuelUiKit.CreateRect("Next", pageBar, new Vector2(.80f, .04f), new Vector2(.98f, .96f), Vector2.zero, Vector2.zero);
            _nextPageButton = DuelUiKit.CreateButton(next, "B", "다음 페이지 ▶", () => ChangePage(1), 15);

            BuildDeckSetup(root);
        }

        private void BuildDeckSetup(Transform parent)
        {
            var setup = DuelUiKit.FillRect("DuelSetup", parent);
            _deckSetupRoot = setup.gameObject;
            DuelUiKit.AddMenuHeader(setup, "대전 준비");
            var back = DuelUiKit.CreateRect("BackToOpponents", setup, new Vector2(.035f, .916f), new Vector2(.209f, .979f),
                Vector2.zero, Vector2.zero);
            _backToOpponentsButton = DuelUiKit.CreateButton(back, "B", "◀ 상대 다시 선택", CloseDeckSetup, 16);

            var myPanel = DuelUiKit.CreateMenuPanel(setup, "MyDeck", new Vector2(.045f, .235f), new Vector2(.487f, .865f));
            var opponentPanel = DuelUiKit.CreateMenuPanel(setup, "OpponentDeck", new Vector2(.513f, .235f), new Vector2(.955f, .865f));
            SetupLabel(myPanel, "Heading", "내 덱", new Vector2(.11f, .83f), new Vector2(.89f, .95f),
                30, DuelUiKit.GoldAccent);
            _detailName = SetupLabel(opponentPanel, "OpponentName", "", new Vector2(.11f, .83f), new Vector2(.89f, .95f),
                30, DuelUiKit.GoldAccent);

            BuildMyDeckList(myPanel);
            var bust = DuelUiKit.CreateRect("Portrait", opponentPanel, new Vector2(.193f, .33f), new Vector2(.674f, .81f),
                Vector2.zero, Vector2.zero);
            _detailBust = DuelUiKit.AddImage(bust.gameObject, null, Color.white, true, false);
            var signature = DuelUiKit.CreateRect("Ace", opponentPanel, new Vector2(.646f, .37f), new Vector2(.805f, .664f),
                Vector2.zero, Vector2.zero);
            _sigCardPreview = CompositeCardView.Create(signature, "AceCard", detailedOverlay: false);
            (_previousOppDeckButton, _nextOppDeckButton) = BuildDeckArrows(opponentPanel, CycleOppDeck);

            _oppDeckName = SetupLabel(opponentPanel, "DeckName", "", new Vector2(.12f, .208f), new Vector2(.88f, .318f),
                24, DuelUiKit.TextPrimary);
            _oppDeckStats = SetupLabel(opponentPanel, "DeckStats", "", new Vector2(.10f, .154f), new Vector2(.90f, .207f),
                16, DuelUiKit.TextPrimary);
            _oppDeckPosition = SetupLabel(opponentPanel, "DeckPosition", "", new Vector2(.07f, .078f), new Vector2(.61f, .133f),
                16, DuelUiKit.TextMuted);
            var clone = DuelUiKit.CreateRect("CloneOppDeck", opponentPanel, new Vector2(.644f, .073f), new Vector2(.94f, .143f),
                Vector2.zero, Vector2.zero);
            _cloneOppButton = DuelUiKit.CreateButton(clone, "B", "덱 목록 복제", CloneCurrentOppDeck, 14);

            _statusToast = SetupLabel(setup, "Toast", "", new Vector2(.15f, .169f), new Vector2(.85f, .22f),
                17, DuelUiKit.TextPrimary);
            var start = DuelUiKit.CreateRect("StartDuelBtn", setup, new Vector2(.327f, .061f), new Vector2(.673f, .158f),
                Vector2.zero, Vector2.zero);
            _startDuelButton = MenuButtonMotion.Attach(
                DuelUiKit.CreateButton(start, "B", "듀얼 시작", OpenTurnOrder, 24, gold: true));
            _deckSetupRoot.SetActive(false);
        }

        private void BuildMyDeckList(Transform parent)
        {
            var list = DuelUiKit.CreateRect("DeckList", parent, new Vector2(.058f, .055f), new Vector2(.942f, .81f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddImage(list.gameObject, DuelUiKit.WhiteSprite, DuelUiKit.PanelNavy);
            var viewport = DuelUiKit.CreateRect("Viewport", list, Vector2.zero, Vector2.one,
                new Vector2(7f, 7f), new Vector2(-18f, -7f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = DuelUiKit.CreateRect("Content", viewport, Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(.5f, 1f);
            _myDeckList = list.gameObject.AddComponent<ScrollRect>();
            _myDeckList.viewport = viewport;
            _myDeckList.content = content;
            _myDeckList.horizontal = false;
            _myDeckList.movementType = ScrollRect.MovementType.Clamped;
            _myDeckList.verticalScrollbar = DuelUiKit.AddVerticalScrollbar(list);
            _myDeckList.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            _myDeckList.scrollSensitivity = 36f;
            _emptyMyDecks = SetupLabel(list, "Empty", "", new Vector2(.05f, .22f), new Vector2(.95f, .78f),
                18, DuelUiKit.TextMuted);
        }

        private static Text SetupLabel(Transform parent, string name, string text, Vector2 min, Vector2 max, int size, Color color)
        {
            var rect = DuelUiKit.CreateRect(name, parent, min, max, Vector2.zero, Vector2.zero);
            return DuelUiKit.AddFittedText(rect, "Value", text, size, size * 2 / 3, TextAnchor.MiddleCenter, color, FontStyle.Bold);
        }

        private static (Button previous, Button next) BuildDeckArrows(Transform parent, Action<int> cycle)
        {
            var left = DuelUiKit.CreateRect("PreviousDeck", parent, new Vector2(.05f, .49f), new Vector2(.173f, .634f),
                Vector2.zero, Vector2.zero);
            var right = DuelUiKit.CreateRect("NextDeck", parent, new Vector2(.825f, .49f), new Vector2(.948f, .634f),
                Vector2.zero, Vector2.zero);
            return (
                MenuButtonMotion.Attach(DuelUiKit.CreateButton(left, "B", "◀", () => cycle(-1), 24)),
                MenuButtonMotion.Attach(DuelUiKit.CreateButton(right, "B", "▶", () => cycle(1), 24))
            );
        }

        private void BuildGridSlots(Transform gridArea)
        {
            float cw = 1f / GridColumns, rh = 1f / GridRows;
            for (int r = 0; r < GridRows; r++)
            {
                for (int c = 0; c < GridColumns; c++)
                {
                    float xMin = c * cw + 0.005f, xMax = (c + 1) * cw - 0.005f;
                    float yMax = 1f - r * rh - 0.01f, yMin = 1f - (r + 1) * rh + 0.01f;
                    var slot = DuelUiKit.CreateRect($"CharSlot_{r}_{c}", gridArea,
                        new Vector2(xMin, yMin), new Vector2(xMax, yMax), Vector2.zero, Vector2.zero);

                    var bgImg = DuelUiKit.AddImage(slot.gameObject, DuelUiChrome.Control, Color.white);
                    bgImg.type = Image.Type.Sliced;

                    var portraitRt = DuelUiKit.CreateRect("Portrait", slot,
                        new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.92f), Vector2.zero, Vector2.zero);
                    DuelUiKit.AddImage(portraitRt.gameObject, null, Color.white, true, false);

                    var lblBg = DuelUiKit.CreateRect("NameBg", slot,
                        new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.24f), Vector2.zero, Vector2.zero);
                    DuelUiKit.AddImage(lblBg.gameObject, DuelUiKit.WhiteSprite, new Color(0.02f, 0.06f, 0.14f, 0.88f), false, false);
                    DuelUiKit.AddFittedText(lblBg, "Nm", "", 14, 9, TextAnchor.MiddleCenter, DuelUiKit.TextPrimary, FontStyle.Bold);

                    var cleared = DuelUiKit.CreateBorderedPanel(slot, "Cleared",
                        new Vector2(0.66f, 0.79f), new Vector2(0.95f, 0.94f), Vector2.zero, Vector2.zero,
                        new Color(0.02f, 0.07f, 0.10f, 0.92f), DuelUiKit.GoldAccent);
                    cleared.GetComponent<Image>().raycastTarget = false;
                    DuelUiKit.AddFittedText(cleared, "Label", "승리", 13, 9,
                        TextAnchor.MiddleCenter, DuelUiKit.GoldAccent, FontStyle.Bold);
                    cleared.gameObject.SetActive(false);

                    var btn = slot.gameObject.AddComponent<Button>();
                    btn.targetGraphic = bgImg;
                    int slotIndex = _gridSlots.Count;
                    btn.onClick.AddListener(() =>
                    {
                        if (!_opponentControls.interactable || _page * PageSize + slotIndex >= _filteredChars.Count) return;
                        DuelAudio.PlayClick();
                        PreviewCharacter(slotIndex);
                        OpenDeckSetup();
                    });
                    btn.gameObject.AddComponent<StarterRowFocus>().ShowPreview = () => PreviewCharacter(slotIndex);
                    MenuButtonMotion.Attach(btn);
                    _gridSlots.Add(slot.gameObject);
                }
            }
        }

        private void ApplyFilter()
        {
            _filteredChars.Clear();
            var opponents = CollectionProgress.AvailableOpponents;
            var availableSeries = new HashSet<int>(opponents.Select(c => c.seriesIdx));
            if (_seriesFilter != -1 && !availableSeries.Contains(_seriesFilter))
                _seriesFilter = -1;

            _browserStatus.color = DuelUiKit.TextPrimary;
            _browserStatus.text = $"전체 상대 {opponents.Count:N0}명";

            string query = NormalizeSearch(_searchQuery);
            OpponentCharacter deckMatch = null;
            foreach (var c in opponents)
            {
                if (_seriesFilter != -1 && c.seriesIdx != _seriesFilter) continue;
                if (query.Length != 0)
                {
                    bool nameHit = MatchesSearch(c.name, query) || MatchesSearch(c.bio, query) ||
                                   (c.aliases?.Any(alias => MatchesSearch(alias, query)) ?? false);
                    bool deckHit = OpponentDeckLevels.GetDecks(c.id).Any(d => MatchesSearch(d.title, query));
                    if (!nameHit && !deckHit) continue;
                    if (deckHit && deckMatch == null) deckMatch = c;
                }
                _filteredChars.Add(c);
            }

            foreach (var kv in _tabButtons)
            {
                bool available = kv.Key == -1 || availableSeries.Contains(kv.Key);
                kv.Value.interactable = available;
                kv.Value.GetComponentInChildren<Text>().color = available ? DuelUiKit.TextPrimary : DuelUiKit.TextMuted;
                kv.Value.targetGraphic.color = available ? Color.white : new Color(.52f, .61f, .72f, .76f);
                var ol = kv.Value.GetComponent<Outline>();
                if (ol == null) ol = kv.Value.gameObject.AddComponent<Outline>();
                ol.effectColor = kv.Key == _seriesFilter ? DuelUiKit.GoldAccent : new Color(0, 0, 0, 0);
                ol.effectDistance = new Vector2(2, -2);
            }

            if (deckMatch != null)
            {
                SelectCharacter(deckMatch, query);
                _page = _filteredChars.IndexOf(deckMatch) / PageSize;
            }
            else if (!_filteredChars.Contains(_selectedChar))
                SelectCharacter(_filteredChars.FirstOrDefault());
            else
            {
                SelectCharacter(_selectedChar);
                _page = _filteredChars.IndexOf(_selectedChar) / PageSize;
            }
            RefreshGridPage();
        }

        private static string NormalizeSearch(string value) =>
            string.Concat((value ?? "").Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();

        private static bool MatchesSearch(string value, string query) => NormalizeSearch(value).Contains(query);

        private void RefreshGridPage()
        {
            int totalPages = Mathf.Max(1, (_filteredChars.Count + PageSize - 1) / PageSize);
            _page = Mathf.Clamp(_page, 0, totalPages - 1);
            _pageLabel.text = _filteredChars.Count == 0 ? "검색 결과 없음" : $"{_page + 1} / {totalPages}";
            _previousPageButton.interactable = _page > 0;
            _nextPageButton.interactable = _page < totalPages - 1;

            int start = _page * PageSize;
            for (int i = 0; i < _gridSlots.Count; i++)
            {
                var slot = _gridSlots[i];
                int idx = start + i;
                if (idx >= _filteredChars.Count)
                {
                    slot.SetActive(false);
                    continue;
                }
                slot.SetActive(true);
                var c = _filteredChars[idx];
                bool isSel = _selectedChar != null && _selectedChar.id == c.id;
                slot.GetComponent<Image>().sprite = isSel ? DuelUiChrome.Focus : DuelUiChrome.Control;
                slot.transform.Find("Portrait").GetComponent<Image>().sprite = DuelUiKit.LoadOpponentPortrait(c);
                slot.transform.Find("NameBg/Nm").GetComponent<Text>().text = c.name;
                slot.transform.Find("Cleared").gameObject.SetActive(CollectionProgress.Initialized &&
                    CollectionProgress.IsOpponentCleared(c.id));

            }
        }

        private void PreviewCharacter(int slotIndex)
        {
            int index = _page * PageSize + slotIndex;
            if (!_opponentControls.interactable || index >= _filteredChars.Count) return;
            var character = _filteredChars[index];
            if (character.id == _selectedChar?.id) return;
            SelectCharacter(character);
            RefreshGridPage();
        }

        private void OpenDeckSetup()
        {
            if (_charDecks.Count == 0) return;
            _statusToast.text = "";
            _browserRoot.SetActive(false);
            _deckSetupRoot.SetActive(true);
            RefreshMyDecks();
            UiEntranceMotion.Play(_deckSetupRoot, .55f, .18f);
            var focus = _selectedMyDeck != null ? _myDeckRows[_selectedMyDeckIdx].Button : _backToOpponentsButton;
            EventSystem.current?.SetSelectedGameObject(focus.gameObject);
        }

        internal void ChangePage(int delta, bool focusCharacter = false)
        {
            if (IsDeckSetupOpen) return;
            int start = (_page + delta) * PageSize;
            if (start < 0 || start >= _filteredChars.Count) return;
            var source = delta < 0 ? _previousPageButton : _nextPageButton;
            _page += delta;
            SelectCharacter(_filteredChars[start]);
            RefreshGridPage();
            if (focusCharacter || (!source.interactable && EventSystem.current?.currentSelectedGameObject == source.gameObject))
                FocusCurrentCharacter();
        }

        private void FocusCurrentCharacter()
        {
            int position = _filteredChars.IndexOf(_selectedChar) - _page * PageSize;
            var target = position >= 0 && position < PageSize ? _gridSlots[position] : _searchInput.gameObject;
            EventSystem.current?.SetSelectedGameObject(target);
        }

        private void SelectCharacter(OpponentCharacter c, string deckQuery = "")
        {
            bool changed = c?.id != _selectedChar?.id;
            int? previousDeckId = _selectedChar != null && c?.id == _selectedChar.id &&
                _selectedCharDeckIdx >= 0 && _selectedCharDeckIdx < _charDecks.Count
                ? _charDecks[_selectedCharDeckIdx].id : null;
            _selectedChar = c;
            _charDecks.Clear();
            if (c != null) _charDecks.AddRange(OpponentDeckLevels.GetDecks(c.id));
            _selectedCharDeckIdx = deckQuery.Length == 0
                ? Mathf.Max(0, _charDecks.FindIndex(d => d.id == previousDeckId))
                : Mathf.Max(0, _charDecks.FindIndex(d => MatchesSearch(d.title, deckQuery)));
            _detailBust.sprite = DuelUiKit.LoadOpponentPortrait(c);
            _detailBust.enabled = c != null;
            if (changed && IsDeckSetupOpen && c != null) UiEntranceMotion.Play(_detailBust.gameObject, .5f, .18f);
            _detailName.text = c?.name ?? "";
            SetDeckArrows(_previousOppDeckButton, _nextOppDeckButton, _charDecks.Count > 1);
            _cloneOppButton.interactable = _charDecks.Count > 0;
            UpdateStartDuelAvailability();
            RefreshSelectedOppDeck();
        }

        private void CycleOppDeck(int delta)
        {
            if (_charDecks.Count < 2) return;
            _selectedCharDeckIdx = (_selectedCharDeckIdx + delta + _charDecks.Count) % _charDecks.Count;
            RefreshSelectedOppDeck();
        }

        private void RefreshSelectedOppDeck()
        {
            if (_charDecks.Count == 0)
            {
                _oppDeckName.text = "";
                _oppDeckStats.text = "";
                _oppDeckPosition.text = "";
                _sigCardPreview.SetCard(0);
                return;
            }
            var d = _charDecks[_selectedCharDeckIdx];
            _oppDeckName.text = d.title;
            _oppDeckStats.text = $"메인 {d.main?.Length ?? 0}장  ·  엑스트라 {d.extra?.Length ?? 0}장";
            bool cleared = CollectionProgress.IsOpponentDeckCleared(_selectedChar.id, d.id);
            _oppDeckPosition.color = cleared ? DuelUiKit.GoldAccent : DuelUiKit.TextMuted;
            _oppDeckPosition.text = $"Lv. {_selectedCharDeckIdx + 1} / {_charDecks.Count}" + (cleared ? "  ·  승리 완료" : "");
            if (_sigCardPreview.CurrentCode != d.sigCard)
            {
                _sigCardPreview.SetCard(d.sigCard);
                UiEntranceMotion.Play(_sigCardPreview.gameObject, .53f, .16f);
            }
        }

        private void RefreshMyDecks(UserDeck preferredDeck = null)
        {
            string lastUsedId = CollectionProgress.LastDuelDeckId;
            string preferredId = preferredDeck?.id ?? _selectedMyDeck?.id ?? lastUsedId;
            _playableMyDecks.Clear();
            _playableMyDecks.AddRange(DeckRepository.UserDecks.Where(deck => GameAppController.CanStartWithDeck(deck, out _)));
            int index = _playableMyDecks.FindIndex(deck => deck.id == preferredId);
            if (index < 0 && lastUsedId != preferredId)
                index = _playableMyDecks.FindIndex(deck => deck.id == lastUsedId);
            int count = _playableMyDecks.Count;
            while (_myDeckRows.Count < count) AddMyDeckRow(_myDeckRows.Count);
            for (int i = 0; i < _myDeckRows.Count; i++)
            {
                var row = _myDeckRows[i];
                row.Button.gameObject.SetActive(i < count);
                if (i >= count) continue;
                var deck = _playableMyDecks[i];
                row.Name.text = deck.name;
                row.Counts.text = $"메인 {deck.main.Count}  ·  엑스트라 {deck.extra.Count}";
            }
            _myDeckList.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                count == 0 ? 0f : (count - 1) * MyDeckRowStep + MyDeckRowHeight);
            _emptyMyDecks.gameObject.SetActive(count == 0);
            if (count == 0)
                _emptyMyDecks.text = $"사용 가능한 덱 없음\n{DuelDeckAvailability.ExplainEmptyList(preferredId)}";
            Canvas.ForceUpdateCanvases();
            SelectMyDeck(Mathf.Max(0, index));
        }

        private static void SetDeckArrows(Button previous, Button next, bool available)
        {
            previous.interactable = next.interactable = available;
            previous.targetGraphic.color = next.targetGraphic.color = available ? Color.white : new Color(.52f, .61f, .72f, .76f);
        }

        private void AddMyDeckRow(int index)
        {
            float top = -index * MyDeckRowStep;
            var row = DuelUiKit.CreateRect($"Deck_{index}", _myDeckList.content, Vector2.up, Vector2.one,
                new Vector2(2f, top - MyDeckRowHeight), new Vector2(-2f, top));
            var image = DuelUiKit.AddImage(row.gameObject, DuelUiKit.WhiteSprite, DuelUiKit.PanelNavy);
            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                DuelAudio.PlayClick();
                SelectMyDeck(index);
            });
            var border = row.gameObject.AddComponent<Outline>();
            border.effectColor = Color.clear;
            var focus = row.gameObject.AddComponent<StarterRowFocus>();
            focus.ShowPreview = () => RevealMyDeck(index);
            focus.ShowHighlight = focused => border.effectColor = focused ? DuelUiKit.CyanAccent : Color.clear;
            var name = DuelUiKit.CreateRect("Name", row, new Vector2(.035f, .14f), new Vector2(.68f, .86f),
                Vector2.zero, Vector2.zero);
            var counts = DuelUiKit.CreateRect("Counts", row, new Vector2(.69f, .14f), new Vector2(.965f, .86f),
                Vector2.zero, Vector2.zero);
            var nameLabel = DuelUiKit.AddFittedText(name, "Value", "", 18, 12,
                TextAnchor.MiddleLeft, DuelUiKit.TextPrimary, FontStyle.Bold);
            var countLabel = DuelUiKit.AddFittedText(counts, "Value", "", 13, 10,
                TextAnchor.MiddleRight, DuelUiKit.TextMuted);
            nameLabel.supportRichText = false;
            _myDeckRows.Add((button, nameLabel, countLabel));
        }

        private void RevealMyDeck(int index)
        {
            if (index < 0 || index >= _playableMyDecks.Count) return;
            float viewportHeight = _myDeckList.viewport.rect.height;
            float limit = Mathf.Max(0f, _myDeckList.content.rect.height - viewportHeight);
            if (limit <= 0f) return;
            float visibleTop = (1f - Mathf.Clamp01(_myDeckList.verticalNormalizedPosition)) * limit;
            float rowTop = index * MyDeckRowStep;
            float target = Mathf.Clamp(visibleTop, rowTop + MyDeckRowHeight - viewportHeight, rowTop);
            _myDeckList.StopMovement();
            _myDeckList.verticalNormalizedPosition = 1f - Mathf.Clamp01(target / limit);
        }

        private void SelectMyDeck(int index)
        {
            _selectedMyDeckIdx = index;
            _selectedMyDeck = index >= 0 && index < _playableMyDecks.Count ? _playableMyDecks[index] : null;
            for (int i = 0; i < _playableMyDecks.Count; i++)
            {
                var row = _myDeckRows[i];
                bool selected = i == index;
                row.Button.targetGraphic.color = selected ? new Color(.075f, .29f, .44f) : new Color(.06f, .12f, .225f);
                row.Name.color = selected ? DuelUiKit.GoldAccent : DuelUiKit.TextPrimary;
            }
            RevealMyDeck(index);
            UpdateStartDuelAvailability();
        }

        private void UpdateStartDuelAvailability() =>
            _startDuelButton.interactable = _charDecks.Count > 0 && _selectedMyDeck != null;

        private void CloneCurrentOppDeck()
        {
            if (!TryGetSelectedOpponentDeck(out var d)) return;
            var cloned = DeckRepository.ClonePresetToUserDeck(d);
            bool playable = GameAppController.CanStartWithDeck(cloned, out string reason);
            RefreshMyDecks(playable ? cloned : null);
            ShowStatus(playable ? "덱 목록을 복제했습니다." : $"덱 목록 복제 완료 · {reason}");
        }

        private void OpenTurnOrder()
        {
            if (TurnOrderRoot.activeSelf || !TryGetSelectedOpponentDeck(out var opponentDeck) || _selectedMyDeck == null) return;
            if (!GameAppController.CanStartWithDeck(_selectedMyDeck, out string reason))
            {
                RefreshMyDecks();
                ShowStatus(reason, true);
                return;
            }

            _opponentControls.interactable = false;
            _tossMatchup.text = $"플레이어   VS   {_selectedChar.name}  ·  {opponentDeck.title}";
            TurnOrderRoot.SetActive(true);
            BeginCoinToss(UnityEngine.Random.value < .5f);
            UiEntranceMotion.Play(_tossPanel.gameObject, .62f);
        }

        private void BeginCoinToss(bool playerWon)
        {
            _playerWonToss = playerWon;
            _tossing = true;
            _tossAge = 0f;
            _tossStatus.text = "코인을 던지는 중";
            _orderChoices.gameObject.SetActive(false);
            _coin.Show(0f, playerWon);
            EventSystem.current?.SetSelectedGameObject(_tossCancel.gameObject);
            DuelAudio.PlayCoin();
        }

        internal void CloseTurnOrder()
        {
            _tossing = false;
            TurnOrderRoot.SetActive(false);
            _opponentControls.interactable = true;
            EventSystem.current?.SetSelectedGameObject(_startDuelButton.gameObject);
        }

        internal void UpdateTurnOrder(float delta)
        {
            if (!_tossing || !TurnOrderRoot.activeInHierarchy) return;
            _tossAge += delta;
            _coin.Show(_tossAge, _playerWonToss);
            if (_tossAge < TurnOrderCoin.RevealAt) return;

            if (_playerWonToss)
            {
                _tossing = false;
                _tossStatus.text = "토스 승리 · 선공 또는 후공을 선택하세요";
                _orderChoices.gameObject.SetActive(true);
                UiEntranceMotion.Play(_orderChoices.gameObject, .67f);
                EventSystem.current?.SetSelectedGameObject(_chooseFirst.gameObject);
                return;
            }

            if (!TryGetSelectedOpponentDeck(out var deck))
            {
                CloseTurnOrder();
                return;
            }
            bool playerFirst = OpponentPrefersSecond(deck);
            _tossStatus.text = playerFirst ? "상대가 후공을 선택했습니다" : "상대가 선공을 선택했습니다";
            if (_tossAge >= TurnOrderCoin.LaunchAt) LaunchMatch(playerFirst);
        }

        private void BuildTurnOrderOverlay(Transform parent)
        {
            var overlay = DuelUiKit.FillRect("Overlay_TurnOrder", parent);
            TurnOrderRoot = overlay.gameObject;
            DuelUiKit.AddImage(overlay.gameObject, DuelUiKit.WhiteSprite, new Color(.01f, .019f, .035f, .81f));
            var content = Application.isMobilePlatform ? MobileSafeArea.Create(overlay) : overlay;
            var panel = _tossPanel = DuelUiKit.CreateMenuPanel(content, "TurnOrder",
                new Vector2(.20f, .175f), new Vector2(.80f, .825f));

            RectTransform Place(Transform container, string name, float left, float bottom, float right, float top) =>
                DuelUiKit.CreateRect(name, container, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);

            var matchup = Place(panel, "Matchup", .08f, .824f, .92f, .955f);
            _tossMatchup = DuelUiKit.AddFittedText(matchup, "Label", "", 27, 13,
                TextAnchor.MiddleCenter, DuelUiKit.TextPrimary, FontStyle.Bold);
            _coin = new TurnOrderCoin(Place(panel, "Coin", .32f, .373f, .68f, .79f));
            var result = Place(panel, "Status", .08f, .264f, .92f, .377f);
            _tossStatus = DuelUiKit.AddFittedText(result, "Label", "", 24, 13,
                TextAnchor.MiddleCenter, DuelUiKit.GoldAccent, FontStyle.Bold);
            _orderChoices = Place(panel, "Order", .14f, .079f, .86f, .245f);
            var first = Place(_orderChoices, "First", 0f, 0f, .48f, 1f);
            _chooseFirst = DuelUiKit.CreateButton(first, "B", "선공 선택", () => LaunchMatch(true), 20, gold: true);
            var second = Place(_orderChoices, "Second", .52f, 0f, 1f, 1f);
            DuelUiKit.CreateButton(second, "B", "후공 선택", () => LaunchMatch(false), 20);

            var cancel = Place(panel, "Cancel", .83f, .017f, .976f, .073f);
            _tossCancel = DuelUiKit.CreateButton(cancel, "B", "취소", CloseTurnOrder, 14);
            TurnOrderRoot.SetActive(false);
        }

        private static bool OpponentPrefersSecond(PresetDeckInfo deck)
        {
            if (deck.main is not { } cards) return false;

            // Give attack-focused decks a Battle Phase on their first turn.
            return cards.Contains(NumeronNetwork) ||
                (cards.Contains(CyberDragonCore) && deck.extra?.Contains(CyberTwinDragon) == true) ||
                (cards.Contains(AncientGearFusion) && deck.extra?.Contains(ChaosAncientGearGiant) == true) ||
                cards.Count(BoardBreakers.Contains) >= 4;
        }

        private void LaunchMatch(bool playerGoesFirst)
        {
            if (!TurnOrderRoot.activeSelf || _tossAge < TurnOrderCoin.RevealAt) return;
            if (!TryGetSelectedOpponentDeck(out var oppDeck) || _selectedMyDeck == null)
            {
                CloseTurnOrder();
                return;
            }
            _tossing = false;
            TurnOrderRoot.SetActive(false);
            _opponentControls.interactable = true;
            _startMatch(_selectedChar, oppDeck, _selectedMyDeck, playerGoesFirst);
        }

        private bool TryGetSelectedOpponentDeck(out PresetDeckInfo deck)
        {
            deck = null;
            if (_selectedChar == null || _selectedCharDeckIdx < 0 || _selectedCharDeckIdx >= _charDecks.Count) return false;
            deck = _charDecks[_selectedCharDeckIdx];
            return true;
        }
    }
}
