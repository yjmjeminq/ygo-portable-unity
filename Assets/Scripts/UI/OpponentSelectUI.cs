using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.AI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public class OpponentSelectUI
    {
        public GameObject Root { get; private set; }
        public GameObject JankenRoot { get; private set; }

        private readonly GameAppController _app;

        private int _seriesFilter;
        private string _searchQuery = "";
        private int _page = 0;
        private const int PageSize = 15; // 5x3 LOTD opponent frame grid

        private readonly List<OpponentCharacter> _filteredChars = new List<OpponentCharacter>();
        private readonly List<GameObject> _gridSlots = new List<GameObject>();
        private readonly Dictionary<int, Button> _tabButtons = new Dictionary<int, Button>();

        private OpponentCharacter _selectedChar;
        private readonly List<PresetDeckInfo> _charDecks = new List<PresetDeckInfo>();
        private readonly List<int> _charDeckLevels = new List<int>();
        private int _totalDeckLevels;
        private int _selectedCharDeckIdx = 0;
        private int _myDeckIdx = 0;

        // Detail UI refs
        private Image _detailBust;
        private Text _detailName;
        private Text _detailBio;
        private Dropdown _oppDeckDropdown;
        private Text _oppDeckStats;
        private Text _oppDeckProgress;
        private Text _oppDeckNote;
        private Button _cloneOppButton;
        private Button _previousOppDeckButton;
        private Button _nextOppDeckButton;
        private Button _startDuelButton;
        private Button _previousPageButton;
        private Button _nextPageButton;
        private Button _previousMyDeckButton;
        private Button _nextMyDeckButton;
        private InputField _searchInput;
        private CanvasGroup _opponentControls;
        private CompositeCardView _sigCardPreview;
        private Text _myDeckLabel;
        private Text _pageLabel;
        private Text _unlockProgress;
        private Text _diffLabel;
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
        private Button _jnkChooseSecond;

        public OpponentSelectUI(GameAppController app, Transform canvasParent)
        {
            _app = app;
            _seriesFilter = CollectionProgress.Initialized ? -1 : 0;
            BuildOpponentScreen(canvasParent);
            BuildJankenOverlay(canvasParent);
        }

        public void Open()
        {
            Root.SetActive(true);
            JankenRoot.SetActive(false);
            _opponentControls.interactable = true;
            if (_statusToast != null) _statusToast.text = "";
            EnsureValidMyDeckIndex();
            ApplyFilter();
            FocusCurrentCharacter();
        }

        internal InputField SearchInput => _searchInput;

        internal void FocusUnlockedDeck(PresetDeckInfo deck)
        {
            if (deck == null || _selectedChar?.id != deck.charId) return;
            int index = _charDecks.FindIndex(candidate => candidate.id == deck.id);
            if (index < 0) return;
            _selectedCharDeckIdx = index;
            RefreshSelectedOppDeck();
            if (_oppDeckDropdown.IsInteractable())
                EventSystem.current?.SetSelectedGameObject(_oppDeckDropdown.gameObject);
        }

        public void ShowStatus(string message, bool isError = false)
        {
            _statusToast.color = isError ? LotdUiKit.RedAccent : LotdUiKit.GreenAccent;
            _statusToast.text = message;
        }

        private void EnsureValidMyDeckIndex()
        {
            _myDeckIdx = Mathf.Clamp(_myDeckIdx, 0, Mathf.Max(0, DeckRepository.UserDecks.Count - 1));
            UpdateMyDeckLabel();
        }

        private void BuildOpponentScreen(Transform parent)
        {
            var rt = LotdUiKit.FillRect("Screen_OpponentSelect", parent);
            Root = rt.gameObject;
            _opponentControls = Root.AddComponent<CanvasGroup>();

            var bg = LotdUiKit.FillRect("Bg", rt);
            LotdUiKit.AddImage(bg.gameObject, LotdUiKit.LoadSprite("LOTD/Background/bg_vrains"), Color.white, false, false);
            LotdUiKit.AddMenuHeader(rt, "프리 듀얼");
            var progress = LotdUiKit.CreateBorderedPanel(rt, "UnlockProgress",
                new Vector2(0.02f, 0.918f), new Vector2(0.232f, 0.980f), Vector2.zero, Vector2.zero);
            _unlockProgress = LotdUiKit.AddFittedText(progress, "Value", "", 16, 12,
                TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);
            progress.gameObject.SetActive(false);
            var backRt = LotdUiKit.CreateRect("Back", rt, new Vector2(0.858f, 0.922f), new Vector2(0.981f, 0.977f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(backRt, "B", "메인 메뉴", () => _app.ShowScreen(AppScreen.MainMenu), 16);

            // Series Tabs row (LOTD series)
            var tabBar = LotdUiKit.CreateMenuPanel(rt, "SeriesTabs",
                new Vector2(0.02f, 0.825f), new Vector2(0.62f, 0.895f));
            (int sid, string label)[] tabs =
            {
                (0, "DM"), (1, "GX"), (2, "5D's"), (3, "ZEXAL"),
                (4, "ARC-V"), (5, "VRAINS"), (6, "2026 메타"), (-1, "전체")
            };
            float tw = 1f / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                int capId = tabs[i].sid;
                var slot = LotdUiKit.CreateRect($"Tab_{capId}", tabBar,
                    new Vector2(i * tw + 0.004f, 0.10f), new Vector2((i + 1) * tw - 0.004f, 0.90f),
                    Vector2.zero, Vector2.zero);
                var btn = LotdUiKit.CreateLotdButton(slot, "B", tabs[i].label, () =>
                {
                    _seriesFilter = capId;
                    _page = 0;
                    ApplyFilter();
                }, 14, gold: capId == 6);
                _tabButtons[capId] = btn;
            }

            // Search input right of tabs
            var searchPanel = LotdUiKit.CreateRect("SearchBox", rt,
                new Vector2(0.63f, 0.825f), new Vector2(0.98f, 0.895f), Vector2.zero, Vector2.zero);
            _searchInput = LotdUiKit.CreateInputField(searchPanel, "SearchInput", "상대 이름 또는 덱 검색...", 15);
            _searchInput.onValueChanged.AddListener(v =>
            {
                _searchQuery = v ?? "";
                _page = 0;
                ApplyFilter();
            });

            // Left 5x3 Character Portrait Grid
            var leftGridPanel = LotdUiKit.CreateMenuPanel(rt, "CharGridPanel",
                new Vector2(0.02f, 0.03f), new Vector2(0.62f, 0.81f));

            var gridArea = LotdUiKit.CreateRect("GridArea", leftGridPanel,
                new Vector2(0.02f, 0.10f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero);
            BuildGridSlots(gridArea);

            var pageBar = LotdUiKit.CreateRect("PageBar", leftGridPanel,
                new Vector2(0.02f, 0.015f), new Vector2(0.98f, 0.085f), Vector2.zero, Vector2.zero);
            var prevRt = LotdUiKit.CreateRect("Prev", pageBar, new Vector2(0.02f, 0.08f), new Vector2(0.24f, 0.92f), Vector2.zero, Vector2.zero);
            _previousPageButton = LotdUiKit.CreateLotdButton(prevRt, "B", "◀ 이전 페이지", () => ChangePage(-1), 14);

            var pageTxtRt = LotdUiKit.CreateRect("PageTxt", pageBar, new Vector2(0.25f, 0.08f), new Vector2(0.75f, 0.92f), Vector2.zero, Vector2.zero);
            _pageLabel = LotdUiKit.AddText(pageTxtRt, "L", "1 / 1", 16, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);

            var nextRt = LotdUiKit.CreateRect("Next", pageBar, new Vector2(0.76f, 0.08f), new Vector2(0.98f, 0.92f), Vector2.zero, Vector2.zero);
            _nextPageButton = LotdUiKit.CreateLotdButton(nextRt, "B", "다음 페이지 ▶", () => ChangePage(1), 14);

            // Right Detail & Duel Setup Panel
            var rightPanel = LotdUiKit.CreateMenuPanel(rt, "OppDetailPanel",
                new Vector2(0.635f, 0.03f), new Vector2(0.98f, 0.81f), true);

            // Top: Portrait + Name + Series/Bio
            var oppHeader = LotdUiKit.CreateRect("OppHdr", rightPanel,
                new Vector2(0.03f, 0.67f), new Vector2(0.97f, 0.97f), Vector2.zero, Vector2.zero);
            var bustBox = LotdUiKit.CreateBorderedPanel(oppHeader, "BustBox",
                new Vector2(0f, 0.04f), new Vector2(0.40f, 0.96f), Vector2.zero, Vector2.zero,
                new Color(0.02f, 0.05f, 0.12f, 1f), LotdUiKit.GoldAccent);
            _detailBust = LotdUiKit.AddImage(LotdUiKit.FillRect("Bust", bustBox, 4).gameObject, null, Color.white, true);

            var nameBox = LotdUiKit.CreateRect("NameBox", oppHeader,
                new Vector2(0.43f, 0.04f), new Vector2(1f, 0.96f), Vector2.zero, Vector2.zero);
            _detailName = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("N", nameBox, new Vector2(0, 0.62f), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                "N", "상대 선택", 21, 13, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);
            _detailBio = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Bio", nameBox, new Vector2(0, 0.02f), new Vector2(1, 0.60f), Vector2.zero, Vector2.zero),
                "B", "", 13, 10, TextAnchor.UpperLeft, LotdUiKit.TextMuted);

            // Middle: Opponent Signature Deck Switcher + Ace Card Preview
            var oppDeckBox = LotdUiKit.CreateBorderedPanel(rightPanel, "OppDeckBox",
                new Vector2(0.03f, 0.36f), new Vector2(0.97f, 0.65f), Vector2.zero, Vector2.zero,
                new Color(0.03f, 0.08f, 0.17f, 0.95f));

            var cardHolder = LotdUiKit.CreateRect("SigCard", oppDeckBox,
                new Vector2(0.02f, 0.06f), new Vector2(0.28f, 0.94f), Vector2.zero, Vector2.zero);
            _sigCardPreview = CompositeCardView.Create(cardHolder, "SigPreview", detailedOverlay: false);

            var deckMeta = LotdUiKit.CreateRect("DeckMeta", oppDeckBox,
                new Vector2(0.30f, 0.34f), new Vector2(0.98f, 0.95f), Vector2.zero, Vector2.zero);
            var deckPicker = LotdUiKit.CreateRect("DeckPicker", deckMeta, new Vector2(0, 0.67f), Vector2.one, Vector2.zero, Vector2.zero);
            _oppDeckDropdown = LotdUiKit.CreateLotdDropdown(deckPicker, "Dropdown", Array.Empty<string>(), index =>
            {
                _selectedCharDeckIdx = index;
                RefreshSelectedOppDeck();
            }, dynamicChoices: true);
            _oppDeckStats = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("DS", deckMeta, new Vector2(0, 0.32f), new Vector2(1, 0.64f), Vector2.zero, Vector2.zero),
                "DS", "", 13, 9, TextAnchor.MiddleLeft, LotdUiKit.CyanAccent);
            _oppDeckProgress = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("DeckProgress", deckMeta,
                new Vector2(0, 0.16f), new Vector2(1, 0.31f), Vector2.zero, Vector2.zero),
                "Label", "", 13, 9, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);
            _oppDeckNote = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("DeckNote", deckMeta, Vector2.zero,
                new Vector2(1, 0.15f), Vector2.zero, Vector2.zero), "Label", "", 12, 9,
                TextAnchor.MiddleLeft, LotdUiKit.TextMuted);

            var prevDeckRt = LotdUiKit.CreateRect("PrevOppDeck", oppDeckBox, new Vector2(0.30f, 0.06f), new Vector2(0.48f, 0.30f), Vector2.zero, Vector2.zero);
            _previousOppDeckButton = LotdUiKit.CreateLotdButton(prevDeckRt, "B",
                CollectionProgress.Initialized ? "◀ 레벨" : "◀ 덱", () => CycleOppDeck(-1), 13);
            var nextDeckRt = LotdUiKit.CreateRect("NextOppDeck", oppDeckBox, new Vector2(0.49f, 0.06f), new Vector2(0.67f, 0.30f), Vector2.zero, Vector2.zero);
            _nextOppDeckButton = LotdUiKit.CreateLotdButton(nextDeckRt, "B",
                CollectionProgress.Initialized ? "레벨 ▶" : "덱 ▶", () => CycleOppDeck(1), 13);
            var cloneRt = LotdUiKit.CreateRect("CloneOppDeck", oppDeckBox, new Vector2(0.69f, 0.06f), new Vector2(0.98f, 0.30f), Vector2.zero, Vector2.zero);
            _cloneOppButton = LotdUiKit.CreateLotdButton(cloneRt, "B", "덱 목록 복제", CloneCurrentOppDeck, 12);

            // My Deck Selector Box
            var myDeckBox = LotdUiKit.CreateBorderedPanel(rightPanel, "MyDeckBox",
                new Vector2(0.03f, 0.16f), new Vector2(0.97f, 0.34f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddText(LotdUiKit.CreateRect("Lbl", myDeckBox, new Vector2(0.03f, 0.56f), new Vector2(0.97f, 0.94f), Vector2.zero, Vector2.zero),
                "L", "내 덱", 14, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);

            _myDeckLabel = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Val", myDeckBox, new Vector2(0.18f, 0.08f), new Vector2(0.82f, 0.56f), Vector2.zero, Vector2.zero),
                "V", "", 15, 10, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);

            var myPrev = LotdUiKit.CreateRect("P", myDeckBox, new Vector2(0.03f, 0.10f), new Vector2(0.16f, 0.52f), Vector2.zero, Vector2.zero);
            _previousMyDeckButton = LotdUiKit.CreateLotdButton(myPrev, "B", "◀", () => CycleMyDeck(-1), 14);
            var myNext = LotdUiKit.CreateRect("N", myDeckBox, new Vector2(0.84f, 0.10f), new Vector2(0.97f, 0.52f), Vector2.zero, Vector2.zero);
            _nextMyDeckButton = LotdUiKit.CreateLotdButton(myNext, "B", "▶", () => CycleMyDeck(1), 14);

            // Difficulty & Toast
            var diffRt = LotdUiKit.CreateRect("DiffRow", rightPanel,
                new Vector2(0.03f, 0.105f), new Vector2(0.97f, 0.155f), Vector2.zero, Vector2.zero);
            _diffLabel = LotdUiKit.AddText(LotdUiKit.CreateRect("DL", diffRt, new Vector2(0, 0), new Vector2(0.55f, 1), Vector2.zero, Vector2.zero),
                "L", "AI: 스마트 (전술 최적화)", 13, TextAnchor.MiddleLeft, LotdUiKit.CyanAccent);
            _statusToast = LotdUiKit.AddText(LotdUiKit.CreateRect("Toast", diffRt, new Vector2(0.55f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                "T", "", 13, TextAnchor.MiddleRight, LotdUiKit.GreenAccent);
            _statusToast.resizeTextForBestFit = true;
            _statusToast.resizeTextMinSize = 9;
            _statusToast.resizeTextMaxSize = 13;

            // Start Duel Button
            var startRt = LotdUiKit.CreateRect("StartDuelBtn", rightPanel,
                new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.095f), Vector2.zero, Vector2.zero);
            _startDuelButton = MenuButtonMotion.Attach(
                LotdUiKit.CreateLotdButton(startRt, "B", "듀얼 시작", OpenJanken, 20, gold: true));
        }

        private void BuildGridSlots(Transform gridArea)
        {
            const int cols = 5, rows = 3;
            float cw = 1f / cols, rh = 1f / rows;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float xMin = c * cw + 0.008f, xMax = (c + 1) * cw - 0.008f;
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
                        LotdAudio.PlayClick();
                        PreviewCharacter(slotIndex);
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
            bool trackUnlocks = CollectionProgress.Initialized;
            IEnumerable<OpponentCharacter> opponents = trackUnlocks
                ? CollectionProgress.UnlockedOpponents : DeckRepository.Characters;
            var openSeries = trackUnlocks ? new HashSet<int>(opponents.Select(c => c.seriesIdx)) : null;
            if (trackUnlocks && _seriesFilter != -1 && !openSeries.Contains(_seriesFilter))
                _seriesFilter = -1;

            _unlockProgress.transform.parent.gameObject.SetActive(trackUnlocks);
            if (trackUnlocks)
            {
                int unlocked = CollectionProgress.UnlockedOpponentCount;
                int total = CollectionProgress.OpponentCount;
                _unlockProgress.text = $"해금한 상대 {unlocked:N0} / {total:N0}명";
                if (unlocked < total) _unlockProgress.text += "\n새 상대에게 처음 이기면 한 명 해금";
            }

            string query = NormalizeSearch(_searchQuery);
            OpponentCharacter deckMatch = null;
            foreach (var c in opponents)
            {
                if (_seriesFilter != -1 && c.seriesIdx != _seriesFilter) continue;
                var decks = DeckRepository.GetDecksForCharacter(c.id);
                if (decks.Count == 0) continue;
                if (query.Length != 0)
                {
                    bool nameHit = MatchesSearch(c.name, query) || MatchesSearch(c.bio, query) ||
                                   (c.aliases?.Any(alias => MatchesSearch(alias, query)) ?? false);
                    bool deckHit = (trackUnlocks ? OpponentDeckLevels.GetDecks(c.id) : decks)
                        .Any(d => IsSelectableDeck(c.id, d) && MatchesSearch(d.title, query));
                    if (!nameHit && !deckHit) continue;
                    if (deckHit && deckMatch == null) deckMatch = c;
                }
                _filteredChars.Add(c);
            }

            foreach (var kv in _tabButtons)
            {
                bool available = !trackUnlocks || kv.Key == -1 || openSeries.Contains(kv.Key);
                kv.Value.interactable = available;
                kv.Value.GetComponentInChildren<Text>().color = available ? LotdUiKit.TextPrimary : LotdUiKit.TextMuted;
                if (trackUnlocks) kv.Value.targetGraphic.color = available ? Color.white : new Color(.52f, .61f, .72f, .76f);
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

        private static bool IsSelectableDeck(int charId, PresetDeckInfo deck) =>
            !CollectionProgress.Initialized || CollectionProgress.IsOpponentDeckUnlocked(charId, deck.id);

        private void RefreshGridPage()
        {
            int totalPages = Mathf.Max(1, (_filteredChars.Count + PageSize - 1) / PageSize);
            _page = Mathf.Clamp(_page, 0, totalPages - 1);
            _pageLabel.text = $"{_page + 1} / {totalPages}  (상대 {_filteredChars.Count}명)";
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

        internal void ChangePage(int delta, bool focusCharacter = false)
        {
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
            IReadOnlyList<PresetDeckInfo> allDecks = c == null ? Array.Empty<PresetDeckInfo>() :
                CollectionProgress.Initialized ? OpponentDeckLevels.GetDecks(c.id) : DeckRepository.GetDecksForCharacter(c.id);
            _totalDeckLevels = allDecks.Count;
            _charDecks.Clear();
            _charDeckLevels.Clear();
            for (int i = 0; i < allDecks.Count; i++)
            {
                if (!IsSelectableDeck(c.id, allDecks[i])) continue;
                _charDecks.Add(allDecks[i]);
                _charDeckLevels.Add(i + 1);
            }
            _selectedCharDeckIdx = deckQuery.Length == 0
                ? Mathf.Max(0, _charDecks.FindIndex(d => d.id == previousDeckId))
                : Mathf.Max(0, _charDecks.FindIndex(d => MatchesSearch(d.title, deckQuery)));
            _detailBust.sprite = LotdUiKit.LoadOpponentPortrait(c);
            _detailBust.enabled = c != null;
            if (changed && c != null) UiEntranceMotion.Play(_detailBust.gameObject, .5f, .18f);
            _detailName.text = c?.name ?? "검색 결과 없음";
            _detailBio.text = c == null ? "" : string.IsNullOrWhiteSpace(c.bio)
                ? $"[{c.seriesName}] 상대 덱 {_totalDeckLevels}개"
                : $"[{c.seriesName}]\n{c.bio}";
            _oppDeckDropdown.ClearOptions();
            _oppDeckDropdown.AddOptions(_charDecks.Select((d, i) => CollectionProgress.Initialized
                ? $"Lv. {_charDeckLevels[i]}  {d.title}" : $"{i + 1}. {d.title}").ToList());
            bool canSwitchDeck = _charDecks.Count > 1;
            _oppDeckDropdown.interactable = canSwitchDeck;
            _previousOppDeckButton.interactable = _nextOppDeckButton.interactable = canSwitchDeck;
            Color deckArrowColor = canSwitchDeck ? Color.white : new Color(.52f, .61f, .72f, .76f);
            _previousOppDeckButton.targetGraphic.color = _nextOppDeckButton.targetGraphic.color = deckArrowColor;
            _cloneOppButton.interactable = _charDecks.Count > 0;
            _startDuelButton.interactable = _charDecks.Count > 0 && DeckRepository.UserDecks.Count > 0;
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
                _oppDeckStats.text = "";
                _oppDeckProgress.text = "";
                _oppDeckNote.text = "";
                _sigCardPreview.SetCard(0);
                return;
            }
            var d = _charDecks[_selectedCharDeckIdx];
            _oppDeckDropdown.SetValueWithoutNotify(_selectedCharDeckIdx);
            int mc = d.main != null ? d.main.Length : 0;
            int ec = d.extra != null ? d.extra.Length : 0;
            _oppDeckStats.text = $"메인 {mc}장 / 엑스트라 {ec}장\n에이스 {CardDatabase.GetName(d.sigCard)}";
            if (CollectionProgress.Initialized)
            {
                bool cleared = CollectionProgress.IsOpponentDeckCleared(_selectedChar.id, d.id);
                _oppDeckProgress.color = cleared ? LotdUiKit.GoldAccent : LotdUiKit.TextPrimary;
                _oppDeckProgress.text = $"덱 해금 {_charDecks.Count}/{_totalDeckLevels}  ·  {(cleared ? "승리 완료" : "도전 가능")}";
                _oppDeckNote.text = _charDecks.Count < _totalDeckLevels
                    ? $"Lv. {_charDeckLevels[_charDeckLevels.Count - 1]} 첫 승리 시 다음 덱 해금" : d.subtitle ?? "";
            }
            else
            {
                _oppDeckProgress.text = "";
                _oppDeckNote.text = d.subtitle ?? "";
            }
            if (_sigCardPreview.CurrentCode != d.sigCard)
            {
                _sigCardPreview.SetCard(d.sigCard);
                UiEntranceMotion.Play(_sigCardPreview.gameObject, .53f, .16f);
            }
        }

        private void CycleMyDeck(int delta)
        {
            if (DeckRepository.UserDecks.Count == 0) return;
            _myDeckIdx = (_myDeckIdx + delta + DeckRepository.UserDecks.Count) % DeckRepository.UserDecks.Count;
            UpdateMyDeckLabel();
            UiEntranceMotion.Play(_myDeckLabel.gameObject);
        }

        private void UpdateMyDeckLabel()
        {
            _previousMyDeckButton.interactable = _nextMyDeckButton.interactable = DeckRepository.UserDecks.Count > 1;
            _startDuelButton.interactable = _charDecks.Count > 0 && DeckRepository.UserDecks.Count > 0;
            if (DeckRepository.UserDecks.Count == 0)
            {
                _myDeckLabel.text = "저장된 내 덱 없음";
                return;
            }
            var md = DeckRepository.UserDecks[_myDeckIdx];
            _myDeckLabel.text = $"{md.name} (메인 {md.main.Count} / 엑스트라 {md.extra.Count})";
            if (CollectionProgress.Initialized && !GameAppController.CanStartWithDeck(md, out _))
                _myDeckLabel.text += "\n듀얼에 사용할 수 없는 덱";
        }

        private void CloneCurrentOppDeck()
        {
            if (!TryGetSelectedOpponentDeck(out var d)) return;
            var cloned = DeckRepository.ClonePresetToUserDeck(d);
            _myDeckIdx = DeckRepository.UserDecks.IndexOf(cloned);
            UpdateMyDeckLabel();
            bool playable = GameAppController.CanStartWithDeck(cloned, out _);
            ShowStatus(playable ? "덱 목록을 복제했습니다." : "덱 목록 복제 완료. 덱 편집에서 사용 조건을 확인하세요.");
        }

        private void OpenJanken()
        {
            if (!TryGetSelectedOpponentDeck(out _) || DeckRepository.UserDecks.Count == 0) return;
            var myDeck = DeckRepository.UserDecks[_myDeckIdx];
            if (!GameAppController.CanStartWithDeck(myDeck, out string reason))
            {
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
            _jnkChooseSecond = LotdUiKit.CreateLotdButton(b2, "B", "후공으로 시작", () => LaunchMatch(false), 20);

            var cancelRt = LotdUiKit.CreateRect("Cancel", win, new Vector2(0.82f, 0.02f), new Vector2(0.97f, 0.09f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(cancelRt, "B", "취소", CloseJanken, 14);
            JankenRoot.SetActive(false);
        }

        private void ResolveJanken(int playerHand, int oppHand) // 0=scissors, 1=rock, 2=paper
        {
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
            _jnkStatusText.text = playerWon ? "승리했습니다. 선공 또는 후공을 선택하세요." : "상대가 선공을 선택했습니다.";
            _jnkHandsRow.SetActive(false);
            _jnkChooseFirst.gameObject.SetActive(playerWon);
            var secondSlot = (RectTransform)_jnkChooseSecond.transform.parent;
            secondSlot.anchorMin = new Vector2(playerWon ? 0.53f : 0.265f, 0f);
            secondSlot.anchorMax = new Vector2(playerWon ? 1f : 0.735f, 1f);
            _jnkOrderRow.SetActive(true);
            UiEntranceMotion.Play(_jnkOrderRow, .58f);
            EventSystem.current?.SetSelectedGameObject((playerWon ? _jnkChooseFirst : _jnkChooseSecond).gameObject);
        }

        private void LaunchMatch(bool playerGoesFirst)
        {
            if (!TryGetSelectedOpponentDeck(out var oppDeck) || _myDeckIdx < 0 || _myDeckIdx >= DeckRepository.UserDecks.Count)
            {
                CloseJanken();
                return;
            }
            JankenRoot.SetActive(false);
            var myDeck = DeckRepository.UserDecks[_myDeckIdx];
            _app.StartDuelMatch(_selectedChar, oppDeck, myDeck, playerGoesFirst);
        }

        private bool TryGetSelectedOpponentDeck(out PresetDeckInfo deck)
        {
            deck = null;
            if (_selectedChar == null || _selectedCharDeckIdx < 0 || _selectedCharDeckIdx >= _charDecks.Count) return false;
            var selected = _charDecks[_selectedCharDeckIdx];
            if (!IsSelectableDeck(_selectedChar.id, selected)) return false;
            deck = selected;
            return true;
        }
    }
}
