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
        private int _trunkPage = 0;
        private const int TrunkCols = 6;
        private const int TrunkRows = 4;
        private const int TrunkPageSize = TrunkCols * TrunkRows;

        private readonly List<CardInfo> _trunkResults = new List<CardInfo>();
        private readonly List<(GameObject obj, CompositeCardView card, Text sub)> _trunkSlots = new List<(GameObject, CompositeCardView, Text)>();
        private readonly List<(GameObject obj, CompositeCardView card)> _mainSlots = new List<(GameObject, CompositeCardView)>();
        private readonly List<(GameObject obj, CompositeCardView card)> _extraSlots = new List<(GameObject, CompositeCardView)>();
        private readonly List<(GameObject obj, CompositeCardView card)> _sideSlots = new List<(GameObject, CompositeCardView)>();

        private bool _showingSide = false;

        // UI widgets
        private CompositeCardView _previewCard;
        private Image _previewRaceIcon;
        private Text _previewName;
        private Text _previewStats;
        private Text _previewDesc;
        private Text _trunkPageText;
        private Text _trunkCountText;
        private InputField _deckNameInput;
        private Text _deckHeaderText;
        private Text _mainCountText;
        private Text _extraOrSideTitle;
        private Text _toastText;
        private Text _kindFilterBtnText;
        private Text _raceFilterBtnText;
        private Text _sortBtnText;
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
                LotdUiKit.WhiteSprite, new Color(0.02f, 0.05f, 0.12f, 0.72f));

            // Header bar
            var hdr = LotdUiKit.CreateBorderedPanel(rt, "Header",
                new Vector2(0f, 0.92f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero,
                LotdUiKit.PanelHeader, LotdUiKit.CyanAccent);
            LotdUiKit.AddText(LotdUiKit.CreateRect("T", hdr, new Vector2(0.01f, 0), new Vector2(0.45f, 1), Vector2.zero, Vector2.zero),
                "Title", "  덱 편집기 (DECK EDITOR)", 22, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);

            _toastText = LotdUiKit.AddText(LotdUiKit.CreateRect("Toast", hdr, new Vector2(0.46f, 0), new Vector2(0.82f, 1), Vector2.zero, Vector2.zero),
                "Msg", "", 15, TextAnchor.MiddleRight, LotdUiKit.GoldAccent, FontStyle.Bold);

            var backBtn = LotdUiKit.CreateRect("Back", hdr, new Vector2(0.84f, 0.12f), new Vector2(0.985f, 0.88f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(backBtn, "B", "메인 메뉴", () => _app.ShowScreen(AppScreen.MainMenu), 16);

            // LEFT COLUMN: Card Detail Preview (0.01 .. 0.255)
            BuildLeftColumn(rt);

            // CENTER COLUMN: Trunk Card Pool Search & Grid (0.262 .. 0.635)
            BuildCenterColumn(rt);

            // RIGHT COLUMN: Active Deck Main / Extra / Side (0.642 .. 0.992)
            BuildRightColumn(rt);
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
                new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.27f), Vector2.zero, Vector2.zero,
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

            // Add button at bottom
            var addBtnRt = LotdUiKit.CreateRect("AddCardBtn", col,
                new Vector2(0.04f, 0.015f), new Vector2(0.96f, 0.07f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(addBtnRt, "B", "+ 내 덱에 1장 추가 (우클릭 빠른 추가)", () => TryAddCardToDeck(_inspectedCode, _showingSide), 14, gold: true);
        }

        private void BuildCenterColumn(Transform rt)
        {
            var col = LotdUiKit.CreateBorderedPanel(rt, "Col_Trunk",
                new Vector2(0.262f, 0.02f), new Vector2(0.635f, 0.905f), Vector2.zero, Vector2.zero);

            // Row 1: Search bar + Result count
            var r1 = LotdUiKit.CreateRect("R1", col, new Vector2(0.02f, 0.915f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero);
            var inp = LotdUiKit.CreateInputField(LotdUiKit.CreateRect("Inp", r1, new Vector2(0, 0), new Vector2(0.68f, 1), Vector2.zero, Vector2.zero),
                "Search", "카드 이름 / 효과 텍스트 검색...", 15);
            inp.onValueChanged.AddListener(v =>
            {
                _searchText = v ?? "";
                _trunkPage = 0;
                RefreshTrunkFilter();
            });
            _trunkCountText = LotdUiKit.AddText(LotdUiKit.CreateRect("Cnt", r1, new Vector2(0.70f, 0), new Vector2(1f, 1), Vector2.zero, Vector2.zero),
                "C", "0장", 14, TextAnchor.MiddleRight, LotdUiKit.CyanAccent, FontStyle.Bold);

            // Row 2: Kind filter + Race filter (incl. Illusion) + Sort
            var r2 = LotdUiKit.CreateRect("R2", col, new Vector2(0.02f, 0.855f), new Vector2(0.98f, 0.908f), Vector2.zero, Vector2.zero);
            var bKind = LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("FK", r2, new Vector2(0f, 0), new Vector2(0.32f, 1), Vector2.zero, Vector2.zero),
                "B", "종류: 전체", () =>
                {
                    _kindFilter = (_kindFilter + 1) % 5;
                    string[] lbls = { "종류: 전체", "종류: 몬스터", "종류: 마법", "종류: 함정", "종류: 엑스트라" };
                    _kindFilterBtnText.text = lbls[_kindFilter];
                    _trunkPage = 0;
                    RefreshTrunkFilter();
                }, 13);
            _kindFilterBtnText = bKind.GetComponentInChildren<Text>();

            var bRace = LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("FR", r2, new Vector2(0.34f, 0), new Vector2(0.67f, 1), Vector2.zero, Vector2.zero),
                "B", "종족: 전 종족", () =>
                {
                    _raceFilterIdx = (_raceFilterIdx + 1) % RaceOptions.Length;
                    _raceFilterBtnText.text = $"종족: {RaceOptions[_raceFilterIdx].label}";
                    _trunkPage = 0;
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

            // Trunk 5x6 Card Grid
            var gridArea = LotdUiKit.CreateRect("TrunkGrid", col,
                new Vector2(0.02f, 0.085f), new Vector2(0.98f, 0.845f), Vector2.zero, Vector2.zero);
            float cw = 1f / TrunkCols, rh = 1f / TrunkRows;
            for (int r = 0; r < TrunkRows; r++)
            {
                for (int c = 0; c < TrunkCols; c++)
                {
                    var slot = LotdUiKit.CreateRect($"TS_{r}_{c}", gridArea,
                        new Vector2(c * cw + 0.006f, 1f - (r + 1) * rh + 0.008f),
                        new Vector2((c + 1) * cw - 0.006f, 1f - r * rh - 0.008f),
                        Vector2.zero, Vector2.zero);
                    var card = CompositeCardView.Create(slot, "CV", false);
                    var cntBg = LotdUiKit.CreateRect("CntBg", slot, new Vector2(0.55f, 0.02f), new Vector2(0.98f, 0.24f), Vector2.zero, Vector2.zero);
                    LotdUiKit.AddImage(cntBg.gameObject, LotdUiKit.WhiteSprite, new Color(0, 0, 0, 0.78f), false, false);
                    var cntTxt = LotdUiKit.AddText(cntBg, "T", "", 11, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);
                    slot.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
                    _trunkSlots.Add((slot.gameObject, card, cntTxt));
                }
            }

            // Trunk Page Bar
            var pgBar = LotdUiKit.CreateRect("PgBar", col, new Vector2(0.02f, 0.015f), new Vector2(0.98f, 0.075f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("P", pgBar, new Vector2(0, 0), new Vector2(0.26f, 1), Vector2.zero, Vector2.zero),
                "B", "◀ 이전", () => { if (_trunkPage > 0) { _trunkPage--; RefreshTrunkPage(); } }, 14);
            _trunkPageText = LotdUiKit.AddText(LotdUiKit.CreateRect("L", pgBar, new Vector2(0.28f, 0), new Vector2(0.72f, 1), Vector2.zero, Vector2.zero),
                "L", "1 / 1", 15, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);
            LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("N", pgBar, new Vector2(0.74f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                "B", "다음 ▶", () =>
                {
                    int maxP = Mathf.Max(0, (_trunkResults.Count - 1) / TrunkPageSize);
                    if (_trunkPage < maxP) { _trunkPage++; RefreshTrunkPage(); }
                }, 14);
        }

        private void BuildRightColumn(Transform rt)
        {
            var col = LotdUiKit.CreateBorderedPanel(rt, "Col_Deck",
                new Vector2(0.642f, 0.02f), new Vector2(0.99f, 0.905f), Vector2.zero, Vector2.zero);

            // Top row: Prev Deck / Deck Selector / Next Deck / New / Save
            var top1 = LotdUiKit.CreateRect("Top1", col, new Vector2(0.02f, 0.915f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero);
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

            // Row 2: Deck Name Input + Sort + Toggle Extra/Side + Delete
            var top2 = LotdUiKit.CreateRect("Top2", col, new Vector2(0.02f, 0.855f), new Vector2(0.98f, 0.908f), Vector2.zero, Vector2.zero);
            _deckNameInput = LotdUiKit.CreateInputField(LotdUiKit.CreateRect("DName", top2, new Vector2(0, 0), new Vector2(0.52f, 1), Vector2.zero, Vector2.zero),
                "DeckName", "덱 이름...", 14);
            LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("SortD", top2, new Vector2(0.54f, 0), new Vector2(0.70f, 1), Vector2.zero, Vector2.zero),
                "B", "정렬", SortWorkingDeck, 13);
            LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("SwES", top2, new Vector2(0.72f, 0), new Vector2(0.88f, 1), Vector2.zero, Vector2.zero),
                "B", "Ex/사이드", () =>
                {
                    _showingSide = !_showingSide;
                    _extraGridHolder.SetActive(!_showingSide);
                    _sideGridHolder.SetActive(_showingSide);
                    RefreshDeckGrids();
                }, 12);
            LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("Del", top2, new Vector2(0.90f, 0), new Vector2(1f, 1), Vector2.zero, Vector2.zero),
                "B", "삭제", DeleteCurrentDeck, 12);

            // Main Deck Header + 10x6 Grid (supports up to 60 cards cleanly!)
            _mainCountText = LotdUiKit.AddText(LotdUiKit.CreateRect("MainHdr", col, new Vector2(0.02f, 0.815f), new Vector2(0.98f, 0.85f), Vector2.zero, Vector2.zero),
                "L", "메인 덱 (0 / 40~60)", 13, TextAnchor.MiddleLeft, LotdUiKit.CyanAccent, FontStyle.Bold);

            var mainGrid = LotdUiKit.CreateBorderedPanel(col, "MainGrid",
                new Vector2(0.02f, 0.28f), new Vector2(0.98f, 0.812f), Vector2.zero, Vector2.zero,
                new Color(0.02f, 0.06f, 0.14f, 0.90f));
            BuildDeckSlots(mainGrid, _mainSlots, 10, 6, false, false);

            // Extra / Side Header + 8x2 Grid (15 slots)
            _extraOrSideTitle = LotdUiKit.AddText(LotdUiKit.CreateRect("ExtraHdr", col, new Vector2(0.02f, 0.235f), new Vector2(0.98f, 0.275f), Vector2.zero, Vector2.zero),
                "L", "엑스트라 덱 (0 / 15)", 13, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);

            _extraGridHolder = LotdUiKit.CreateBorderedPanel(col, "ExtraGrid",
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.232f), Vector2.zero, Vector2.zero,
                new Color(0.02f, 0.06f, 0.14f, 0.90f)).gameObject;
            BuildDeckSlots(_extraGridHolder.transform, _extraSlots, 8, 2, true, false);

            _sideGridHolder = LotdUiKit.CreateBorderedPanel(col, "SideGrid",
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.232f), Vector2.zero, Vector2.zero,
                new Color(0.02f, 0.06f, 0.14f, 0.90f)).gameObject;
            BuildDeckSlots(_sideGridHolder.transform, _sideSlots, 8, 2, false, true);
            _sideGridHolder.SetActive(false);
        }

        private void BuildDeckSlots(Transform parent, List<(GameObject, CompositeCardView)> target, int cols, int rows, bool isExtra, bool isSide)
        {
            float cw = 1f / cols, rh = 1f / rows;
            int idx = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if ((isExtra || isSide) && idx >= 15) return;
                    var slot = LotdUiKit.CreateRect($"DS_{idx}", parent,
                        new Vector2(c * cw + 0.004f, 1f - (r + 1) * rh + 0.008f),
                        new Vector2((c + 1) * cw - 0.004f, 1f - r * rh - 0.008f),
                        Vector2.zero, Vector2.zero);
                    slot.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.12f, 0.22f, 0.35f);
                    var card = CompositeCardView.Create(slot, "CV", false);
                    target.Add((slot.gameObject, card));
                    idx++;
                }
            }
        }

        private void RefreshTrunkFilter()
        {
            _trunkResults.Clear();
            string q = (_searchText ?? "").Trim().ToLowerInvariant();
            ulong raceMask = RaceOptions[_raceFilterIdx].mask;

            foreach (var c in CardDatabase.AllCards)
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

            _trunkCountText.text = $"검색 {_trunkResults.Count:N0}장";
            RefreshTrunkPage();
        }

        private void RefreshTrunkPage()
        {
            int maxPages = Mathf.Max(1, (_trunkResults.Count + TrunkPageSize - 1) / TrunkPageSize);
            _trunkPage = Mathf.Clamp(_trunkPage, 0, maxPages - 1);
            _trunkPageText.text = $"{_trunkPage + 1} / {maxPages}";

            int start = _trunkPage * TrunkPageSize;
            for (int i = 0; i < _trunkSlots.Count; i++)
            {
                var (obj, cv, sub) = _trunkSlots[i];
                int idx = start + i;
                if (idx >= _trunkResults.Count)
                {
                    obj.SetActive(false);
                    continue;
                }
                obj.SetActive(true);
                var ci = _trunkResults[idx];
                cv.SetCard(ci.code);
                int inDeck = CountInWorkingDeck(ci.code);
                sub.transform.parent.gameObject.SetActive(inDeck > 0);
                sub.text = inDeck > 0 ? $"x{inDeck}" : "";

                AttachClickHandler(obj,
                    onLeftClick: () => InspectCard(ci.code),
                    onRightClick: () => { InspectCard(ci.code); TryAddCardToDeck(ci.code, _showingSide); });
            }
        }

        private void RefreshDeckGrids()
        {
            _deckHeaderText.text = $"덱 {_userDeckIdx + 1}/{Mathf.Max(1, DeckRepository.UserDecks.Count)}";
            _mainCountText.text = $"메인 덱 ({_workingDeck.main.Count} / 40~60)";
            _mainCountText.color = (_workingDeck.main.Count >= 40 && _workingDeck.main.Count <= 60) ? LotdUiKit.CyanAccent : LotdUiKit.RedAccent;
            _extraOrSideTitle.text = _showingSide
                ? $"사이드 덱 ({_workingDeck.side.Count} / 15)  — 우클릭 제거"
                : $"엑스트라 덱 ({_workingDeck.extra.Count} / 15)  — 우클릭 제거";

            SyncSlotList(_mainSlots, _workingDeck.main, false, false);
            SyncSlotList(_extraSlots, _workingDeck.extra, true, false);
            SyncSlotList(_sideSlots, _workingDeck.side, false, true);
        }

        private void SyncSlotList(List<(GameObject obj, CompositeCardView card)> slots, List<uint> list, bool isExtra, bool isSide)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var (obj, cv) = slots[i];
                if (i >= list.Count)
                {
                    cv.gameObject.SetActive(false);
                    AttachClickHandler(obj, null, null);
                    continue;
                }
                cv.gameObject.SetActive(true);
                uint code = list[i];
                int capIdx = i;
                cv.SetCard(code);
                AttachClickHandler(obj,
                    onLeftClick: () => InspectCard(code),
                    onRightClick: () =>
                    {
                        list.RemoveAt(capIdx);
                        LotdAudio.PlayClick();
                        RefreshDeckGrids();
                        RefreshTrunkPage();
                    });
            }
        }

        private static void AttachClickHandler(GameObject go, Action onLeftClick, Action onRightClick)
        {
            var trig = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
            trig.triggers.Clear();
            if (onLeftClick == null && onRightClick == null) return;
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener(data =>
            {
                var pe = (PointerEventData)data;
                if (pe.button == PointerEventData.InputButton.Right) onRightClick?.Invoke();
                else onLeftClick?.Invoke();
            });
            trig.triggers.Add(entry);
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
            RefreshTrunkPage();
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
            RefreshTrunkPage();
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
    }
}
