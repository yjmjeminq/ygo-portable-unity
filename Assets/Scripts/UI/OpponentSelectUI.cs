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
        public GameObject JankenRoot { get; private set; }

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

        // Janken refs
        private Text _jnkStatusText;
        private Image _jnkPlayerImg;
        private Image _jnkOppImg;
        private GameObject _jnkPanel;
        private GameObject _jnkHandsRow;
        private GameObject _jnkOrderRow;
        private Button _jnkFirstHand;
        private Button _jnkChooseFirst;

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
            BuildJankenOverlay(canvasParent);
        }

        public void Open()
        {
            Root.SetActive(true);
            JankenRoot.SetActive(false);
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
            label.color = isError ? LotdUiKit.RedAccent : LotdUiKit.GreenAccent;
            label.text = message;
        }


        private void BuildOpponentScreen(Transform parent)
        {
            var root = LotdUiKit.FillRect("Screen_OpponentSelect", parent);
            Root = root.gameObject;
            _opponentControls = Root.AddComponent<CanvasGroup>();

            var bg = LotdUiKit.FillRect("Bg", root);
            LotdUiKit.AddImage(bg.gameObject, LotdUiKit.LoadSprite("LOTD/Background/bg_vrains"), Color.white, false, false);
            var browser = LotdUiKit.FillRect("OpponentBrowser", root);
            _browserRoot = browser.gameObject;
            LotdUiKit.AddMenuHeader(browser, "상대 선택");
            var count = LotdUiKit.CreateBorderedPanel(browser, "OpponentCount",
                new Vector2(.02f, .918f), new Vector2(.235f, .98f), Vector2.zero, Vector2.zero);
            _browserStatus = LotdUiKit.AddFittedText(count, "Value", "", 17, 12,
                TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);
            var back = LotdUiKit.CreateRect("Back", browser, new Vector2(.858f, .922f), new Vector2(.981f, .977f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(back, "B", "메인 메뉴", _returnToMenu, 16);

            var tabs = LotdUiKit.CreateMenuPanel(browser, "SeriesTabs", new Vector2(.02f, .825f), new Vector2(.67f, .895f));
            (int id, string label)[] series =
            {
                (-1, "전체"), (0, "DM"), (1, "GX"), (2, "5D's"), (3, "ZEXAL"),
                (4, "ARC-V"), (5, "VRAINS"), (6, "2026 메타")
            };
            float width = 1f / series.Length;
            for (int i = 0; i < series.Length; i++)
            {
                int id = series[i].id;
                var slot = LotdUiKit.CreateRect($"Tab_{id}", tabs,
                    new Vector2(i * width + .004f, .10f), new Vector2((i + 1) * width - .004f, .9f), Vector2.zero, Vector2.zero);
                _tabButtons[id] = LotdUiKit.CreateLotdButton(slot, "B", series[i].label, () =>
                {
                    _seriesFilter = id;
                    _page = 0;
                    ApplyFilter();
                }, 15);
            }

            var search = LotdUiKit.CreateRect("SearchBox", browser, new Vector2(.68f, .825f), new Vector2(.98f, .895f),
                Vector2.zero, Vector2.zero);
            _searchInput = LotdUiKit.CreateInputField(search, "SearchInput", "상대 이름 또는 덱 검색...", 16);
            _searchInput.onValueChanged.AddListener(value =>
            {
                _searchQuery = value ?? "";
                _page = 0;
                ApplyFilter();
            });

            var grid = LotdUiKit.CreateMenuPanel(browser, "CharGridPanel", new Vector2(.02f, .03f), new Vector2(.98f, .81f));
            var gridArea = LotdUiKit.CreateRect("GridArea", grid, new Vector2(.018f, .10f), new Vector2(.982f, .96f),
                Vector2.zero, Vector2.zero);
            BuildGridSlots(gridArea);
            var pageBar = LotdUiKit.CreateRect("PageBar", grid, new Vector2(.02f, .038f), new Vector2(.98f, .10f),
                Vector2.zero, Vector2.zero);
            var prev = LotdUiKit.CreateRect("Prev", pageBar, new Vector2(.02f, .04f), new Vector2(.20f, .96f), Vector2.zero, Vector2.zero);
            _previousPageButton = LotdUiKit.CreateLotdButton(prev, "B", "◀ 이전 페이지", () => ChangePage(-1), 15);
            var page = LotdUiKit.CreateRect("PageTxt", pageBar, new Vector2(.24f, 0), new Vector2(.76f, 1), Vector2.zero, Vector2.zero);
            _pageLabel = LotdUiKit.AddText(page, "L", "", 16, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);
            var next = LotdUiKit.CreateRect("Next", pageBar, new Vector2(.80f, .04f), new Vector2(.98f, .96f), Vector2.zero, Vector2.zero);
            _nextPageButton = LotdUiKit.CreateLotdButton(next, "B", "다음 페이지 ▶", () => ChangePage(1), 15);

            BuildDeckSetup(root);
        }

        private void BuildDeckSetup(Transform parent)
        {
            var setup = LotdUiKit.FillRect("DuelSetup", parent);
            _deckSetupRoot = setup.gameObject;
            LotdUiKit.AddMenuHeader(setup, "대전 준비");
            var back = LotdUiKit.CreateRect("BackToOpponents", setup, new Vector2(.035f, .916f), new Vector2(.209f, .979f),
                Vector2.zero, Vector2.zero);
            _backToOpponentsButton = LotdUiKit.CreateLotdButton(back, "B", "◀ 상대 다시 선택", CloseDeckSetup, 16);

            var myPanel = LotdUiKit.CreateMenuPanel(setup, "MyDeck", new Vector2(.045f, .235f), new Vector2(.487f, .865f));
            var opponentPanel = LotdUiKit.CreateMenuPanel(setup, "OpponentDeck", new Vector2(.513f, .235f), new Vector2(.955f, .865f));
            SetupLabel(myPanel, "Heading", "내 덱", new Vector2(.11f, .83f), new Vector2(.89f, .95f),
                30, LotdUiKit.GoldAccent);
            _detailName = SetupLabel(opponentPanel, "OpponentName", "", new Vector2(.11f, .83f), new Vector2(.89f, .95f),
                30, LotdUiKit.GoldAccent);

            BuildMyDeckList(myPanel);
            var bust = LotdUiKit.CreateRect("Portrait", opponentPanel, new Vector2(.193f, .33f), new Vector2(.674f, .81f),
                Vector2.zero, Vector2.zero);
            _detailBust = LotdUiKit.AddImage(bust.gameObject, null, Color.white, true, false);
            var signature = LotdUiKit.CreateRect("Ace", opponentPanel, new Vector2(.646f, .37f), new Vector2(.805f, .664f),
                Vector2.zero, Vector2.zero);
            _sigCardPreview = CompositeCardView.Create(signature, "AceCard", detailedOverlay: false);
            (_previousOppDeckButton, _nextOppDeckButton) = BuildDeckArrows(opponentPanel, CycleOppDeck);

            _oppDeckName = SetupLabel(opponentPanel, "DeckName", "", new Vector2(.12f, .208f), new Vector2(.88f, .318f),
                24, LotdUiKit.TextPrimary);
            _oppDeckStats = SetupLabel(opponentPanel, "DeckStats", "", new Vector2(.10f, .154f), new Vector2(.90f, .207f),
                16, LotdUiKit.TextPrimary);
            _oppDeckPosition = SetupLabel(opponentPanel, "DeckPosition", "", new Vector2(.07f, .078f), new Vector2(.61f, .133f),
                16, LotdUiKit.TextMuted);
            var clone = LotdUiKit.CreateRect("CloneOppDeck", opponentPanel, new Vector2(.644f, .073f), new Vector2(.94f, .143f),
                Vector2.zero, Vector2.zero);
            _cloneOppButton = LotdUiKit.CreateLotdButton(clone, "B", "덱 목록 복제", CloneCurrentOppDeck, 14);

            _statusToast = SetupLabel(setup, "Toast", "", new Vector2(.15f, .169f), new Vector2(.85f, .22f),
                17, LotdUiKit.TextPrimary);
            var start = LotdUiKit.CreateRect("StartDuelBtn", setup, new Vector2(.327f, .061f), new Vector2(.673f, .158f),
                Vector2.zero, Vector2.zero);
            _startDuelButton = MenuButtonMotion.Attach(
                LotdUiKit.CreateLotdButton(start, "B", "듀얼 시작", OpenJanken, 24, gold: true));
            _deckSetupRoot.SetActive(false);
        }

        private void BuildMyDeckList(Transform parent)
        {
            var list = LotdUiKit.CreateRect("DeckList", parent, new Vector2(.058f, .055f), new Vector2(.942f, .81f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(list.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelNavy);
            var viewport = LotdUiKit.CreateRect("Viewport", list, Vector2.zero, Vector2.one,
                new Vector2(7f, 7f), new Vector2(-18f, -7f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = LotdUiKit.CreateRect("Content", viewport, Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(.5f, 1f);
            _myDeckList = list.gameObject.AddComponent<ScrollRect>();
            _myDeckList.viewport = viewport;
            _myDeckList.content = content;
            _myDeckList.horizontal = false;
            _myDeckList.movementType = ScrollRect.MovementType.Clamped;
            _myDeckList.verticalScrollbar = LotdUiKit.AddVerticalScrollbar(list);
            _myDeckList.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            _myDeckList.scrollSensitivity = 36f;
            _emptyMyDecks = SetupLabel(list, "Empty", "", new Vector2(.05f, .22f), new Vector2(.95f, .78f),
                18, LotdUiKit.TextMuted);
        }

        private static Text SetupLabel(Transform parent, string name, string text, Vector2 min, Vector2 max, int size, Color color)
        {
            var rect = LotdUiKit.CreateRect(name, parent, min, max, Vector2.zero, Vector2.zero);
            return LotdUiKit.AddFittedText(rect, "Value", text, size, size * 2 / 3, TextAnchor.MiddleCenter, color, FontStyle.Bold);
        }

        private static (Button previous, Button next) BuildDeckArrows(Transform parent, Action<int> cycle)
        {
            var left = LotdUiKit.CreateRect("PreviousDeck", parent, new Vector2(.05f, .49f), new Vector2(.173f, .634f),
                Vector2.zero, Vector2.zero);
            var right = LotdUiKit.CreateRect("NextDeck", parent, new Vector2(.825f, .49f), new Vector2(.948f, .634f),
                Vector2.zero, Vector2.zero);
            return (
                MenuButtonMotion.Attach(LotdUiKit.CreateLotdButton(left, "B", "◀", () => cycle(-1), 24)),
                MenuButtonMotion.Attach(LotdUiKit.CreateLotdButton(right, "B", "▶", () => cycle(1), 24))
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
                    var slot = LotdUiKit.CreateRect($"CharSlot_{r}_{c}", gridArea,
                        new Vector2(xMin, yMin), new Vector2(xMax, yMax), Vector2.zero, Vector2.zero);

                    var frameSp = LotdUiKit.LoadSprite("LOTD/Shared/opponentframe_normal");
                    var bgImg = LotdUiKit.AddImage(slot.gameObject, frameSp ?? LotdUiKit.WhiteSprite,
                        frameSp != null ? Color.white : new Color(0.08f, 0.16f, 0.30f, 1f), false);

                    var portraitRt = LotdUiKit.CreateRect("Portrait", slot,
                        new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.92f), Vector2.zero, Vector2.zero);
                    LotdUiKit.AddImage(portraitRt.gameObject, null, Color.white, true, false);

                    var lblBg = LotdUiKit.CreateRect("NameBg", slot,
                        new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.24f), Vector2.zero, Vector2.zero);
                    LotdUiKit.AddImage(lblBg.gameObject, LotdUiKit.WhiteSprite, new Color(0.02f, 0.06f, 0.14f, 0.88f), false, false);
                    LotdUiKit.AddFittedText(lblBg, "Nm", "", 14, 9, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);

                    var cleared = LotdUiKit.CreateBorderedPanel(slot, "Cleared",
                        new Vector2(0.66f, 0.79f), new Vector2(0.95f, 0.94f), Vector2.zero, Vector2.zero,
                        new Color(0.02f, 0.07f, 0.10f, 0.92f), LotdUiKit.GoldAccent);
                    cleared.GetComponent<Image>().raycastTarget = false;
                    LotdUiKit.AddFittedText(cleared, "Label", "승리", 13, 9,
                        TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);
                    cleared.gameObject.SetActive(false);

                    var btn = slot.gameObject.AddComponent<Button>();
                    btn.targetGraphic = bgImg;
                    int slotIndex = _gridSlots.Count;
                    btn.onClick.AddListener(() =>
                    {
                        if (!_opponentControls.interactable || _page * PageSize + slotIndex >= _filteredChars.Count) return;
                        LotdAudio.PlayClick();
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

            _browserStatus.color = LotdUiKit.TextPrimary;
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
                kv.Value.GetComponentInChildren<Text>().color = available ? LotdUiKit.TextPrimary : LotdUiKit.TextMuted;
                kv.Value.targetGraphic.color = available ? Color.white : new Color(.52f, .61f, .72f, .76f);
                var ol = kv.Value.GetComponent<Outline>();
                if (ol == null) ol = kv.Value.gameObject.AddComponent<Outline>();
                ol.effectColor = kv.Key == _seriesFilter ? LotdUiKit.GoldAccent : new Color(0, 0, 0, 0);
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
                var frame = LotdUiKit.LoadSprite(isSel ? "LOTD/Shared/opponentframe_highlight" : "LOTD/Shared/opponentframe_normal");
                slot.GetComponent<Image>().sprite = frame ?? LotdUiKit.WhiteSprite;
                slot.transform.Find("Portrait").GetComponent<Image>().sprite = LotdUiKit.LoadOpponentPortrait(c);
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
            _detailBust.sprite = LotdUiKit.LoadOpponentPortrait(c);
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
            _oppDeckPosition.color = cleared ? LotdUiKit.GoldAccent : LotdUiKit.TextMuted;
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
                _emptyMyDecks.text = DeckRepository.UserDecks.Count == 0 ? "저장된 덱 없음" :
                    "사용 가능한 덱 없음\n덱 편집에서 덱을 확인하세요.";
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
            var row = LotdUiKit.CreateRect($"Deck_{index}", _myDeckList.content, Vector2.up, Vector2.one,
                new Vector2(2f, top - MyDeckRowHeight), new Vector2(-2f, top));
            var image = LotdUiKit.AddImage(row.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelNavy);
            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                LotdAudio.PlayClick();
                SelectMyDeck(index);
            });
            var border = row.gameObject.AddComponent<Outline>();
            border.effectColor = Color.clear;
            var focus = row.gameObject.AddComponent<StarterRowFocus>();
            focus.ShowPreview = () => RevealMyDeck(index);
            focus.ShowHighlight = focused => border.effectColor = focused ? LotdUiKit.CyanAccent : Color.clear;
            var name = LotdUiKit.CreateRect("Name", row, new Vector2(.035f, .14f), new Vector2(.68f, .86f),
                Vector2.zero, Vector2.zero);
            var counts = LotdUiKit.CreateRect("Counts", row, new Vector2(.69f, .14f), new Vector2(.965f, .86f),
                Vector2.zero, Vector2.zero);
            var nameLabel = LotdUiKit.AddFittedText(name, "Value", "", 18, 12,
                TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);
            var countLabel = LotdUiKit.AddFittedText(counts, "Value", "", 13, 10,
                TextAnchor.MiddleRight, LotdUiKit.TextMuted);
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
                row.Name.color = selected ? LotdUiKit.GoldAccent : LotdUiKit.TextPrimary;
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
            bool playable = GameAppController.CanStartWithDeck(cloned, out _);
            RefreshMyDecks(playable ? cloned : null);
            ShowStatus(playable ? "덱 목록을 복제했습니다." : "덱 목록 복제 완료. 덱 편집에서 사용 조건을 확인하세요.");
        }

        private void OpenJanken()
        {
            if (!TryGetSelectedOpponentDeck(out _) || _selectedMyDeck == null) return;
            if (!GameAppController.CanStartWithDeck(_selectedMyDeck, out string reason))
            {
                RefreshMyDecks();
                ShowStatus(reason, true);
                return;
            }
            _opponentControls.interactable = false;
            JankenRoot.SetActive(true);
            _jnkHandsRow.SetActive(true);
            _jnkOrderRow.SetActive(false);
            _jnkStatusText.text = "선/후공 결정을 위해 가위·바위·보를 선택하세요!";
            _jnkPlayerImg.sprite = LotdUiKit.LoadSprite("LOTD/Janken/jnkn_player_stone_inactive");
            _jnkOppImg.sprite = LotdUiKit.LoadSprite("LOTD/Janken/jnkn_opp_stone_inactive");
            UiEntranceMotion.Play(_jnkPanel, .56f);
            EventSystem.current?.SetSelectedGameObject(_jnkFirstHand.gameObject);
        }

        internal void CloseJanken()
        {
            JankenRoot.SetActive(false);
            _opponentControls.interactable = true;
            EventSystem.current?.SetSelectedGameObject(_startDuelButton.gameObject);
        }

        private void BuildJankenOverlay(Transform parent)
        {
            var bg = LotdUiKit.FillRect("Overlay_Janken", parent);
            JankenRoot = bg.gameObject;
            LotdUiKit.AddImage(bg.gameObject, LotdUiKit.WhiteSprite, new Color(0f, 0f, 0f, 0.78f));

            var win = LotdUiKit.CreateBorderedPanel(bg, "JnkWin",
                new Vector2(0.18f, 0.16f), new Vector2(0.82f, 0.84f), Vector2.zero, Vector2.zero,
                LotdUiKit.PanelNavy, LotdUiKit.GoldAccent);
            _jnkPanel = win.gameObject;

            var vsBg = LotdUiKit.CreateRect("VsBg", win, new Vector2(0.05f, 0.35f), new Vector2(0.95f, 0.85f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(vsBg.gameObject, LotdUiKit.LoadSprite("LOTD/Janken/vs_background"), new Color(0.8f, 0.9f, 1f, 0.9f), true);

            _jnkStatusText = LotdUiKit.AddText(LotdUiKit.CreateRect("Hdr", win, new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.96f), Vector2.zero, Vector2.zero),
                "T", "가위·바위·보!", 24, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

            _jnkPlayerImg = LotdUiKit.AddImage(LotdUiKit.CreateRect("PHand", win, new Vector2(0.12f, 0.42f), new Vector2(0.38f, 0.78f), Vector2.zero, Vector2.zero).gameObject,
                null, Color.white, true);
            LotdUiKit.AddImage(LotdUiKit.CreateRect("VS", win, new Vector2(0.42f, 0.48f), new Vector2(0.58f, 0.72f), Vector2.zero, Vector2.zero).gameObject,
                LotdUiKit.LoadSprite("LOTD/Janken/vs_text"), Color.white, true);
            _jnkOppImg = LotdUiKit.AddImage(LotdUiKit.CreateRect("OHand", win, new Vector2(0.62f, 0.42f), new Vector2(0.88f, 0.78f), Vector2.zero, Vector2.zero).gameObject,
                null, Color.white, true);

            _jnkHandsRow = LotdUiKit.CreateRect("Hands", win, new Vector2(0.08f, 0.10f), new Vector2(0.92f, 0.30f), Vector2.zero, Vector2.zero).gameObject;
            string[] names = { "가위 (SCISSORS)", "바위 (ROCK)", "보 (PAPER)" };
            for (int i = 0; i < 3; i++)
            {
                int pick = i;
                var slot = LotdUiKit.CreateRect($"H{i}", _jnkHandsRow.transform,
                    new Vector2(i * 0.34f, 0f), new Vector2(i * 0.34f + 0.31f, 1f), Vector2.zero, Vector2.zero);
                var button = LotdUiKit.CreateLotdButton(slot, "B", names[i], () => ResolveJanken(pick, UnityEngine.Random.Range(0, 3)), 18);
                if (i == 0) _jnkFirstHand = button;
            }

            _jnkOrderRow = LotdUiKit.CreateRect("Order", win, new Vector2(0.12f, 0.10f), new Vector2(0.88f, 0.30f), Vector2.zero, Vector2.zero).gameObject;
            var b1 = LotdUiKit.CreateRect("First", _jnkOrderRow.transform, new Vector2(0f, 0f), new Vector2(0.47f, 1f), Vector2.zero, Vector2.zero);
            _jnkChooseFirst = LotdUiKit.CreateLotdButton(b1, "B", "선공 선택", () => LaunchMatch(true), 20, gold: true);
            var b2 = LotdUiKit.CreateRect("Second", _jnkOrderRow.transform, new Vector2(0.53f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(b2, "B", "후공 선택", () => LaunchMatch(false), 20);

            var cancelRt = LotdUiKit.CreateRect("Cancel", win, new Vector2(0.82f, 0.02f), new Vector2(0.97f, 0.09f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(cancelRt, "B", "취소", CloseJanken, 14);
            JankenRoot.SetActive(false);
        }

        private void ResolveJanken(int playerHand, int oppHand) // 0=scissors, 1=rock, 2=paper
        {
            if (!JankenRoot.activeSelf || !_jnkHandsRow.activeSelf) return;
            string[] pSprites = { "jnkn_player_scissor_active", "jnkn_player_stone_active", "jnkn_player_paper_active" };
            string[] oSprites = { "jnkn_opp_scissor_active", "jnkn_opp_stone_active", "jnkn_opp_paper_active" };
            _jnkPlayerImg.sprite = LotdUiKit.LoadSprite($"LOTD/Janken/{pSprites[playerHand]}");
            _jnkOppImg.sprite = LotdUiKit.LoadSprite($"LOTD/Janken/{oSprites[oppHand]}");
            UiEntranceMotion.Play(_jnkPlayerImg.gameObject, .44f);
            UiEntranceMotion.Play(_jnkOppImg.gameObject, .44f);

            if (playerHand == oppHand)
            {
                _jnkStatusText.text = "비겼습니다! 다시 선택하세요!";
                return;
            }
            bool playerWon = (playerHand == 0 && oppHand == 2) || (playerHand == 1 && oppHand == 0) || (playerHand == 2 && oppHand == 1);
            _jnkHandsRow.SetActive(false);
            if (!playerWon)
            {
                if (TryGetSelectedOpponentDeck(out var deck))
                    LaunchMatch(playerGoesFirst: OpponentPrefersSecond(deck));
                else
                    CloseJanken();
                return;
            }

            _jnkStatusText.text = "승리했습니다. 선공 또는 후공을 선택하세요.";
            _jnkOrderRow.SetActive(true);
            UiEntranceMotion.Play(_jnkOrderRow, .58f);
            EventSystem.current?.SetSelectedGameObject(_jnkChooseFirst.gameObject);
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
            if (!JankenRoot.activeSelf) return;
            if (!TryGetSelectedOpponentDeck(out var oppDeck) || _selectedMyDeck == null)
            {
                CloseJanken();
                return;
            }
            JankenRoot.SetActive(false);
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
