using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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
        private OpponentCharacter _oppChar;
        private PresetDeckInfo _oppDeck;

        // Left inspector & log
        private CompositeCardView _inspCard;
        private Image _inspRaceIcon;
        private Text _inspName;
        private Text _inspStats;
        private Text _inspDesc;
        private Text _logFeed;
        private uint _inspectedCode = 89631139;

        // Opponent & Player HUD
        private Image _oppBustImg;
        private Text _oppNameText;
        private Text _oppLpText;
        private RectTransform _oppLpFill;
        private Text _myLpText;
        private RectTransform _myLpFill;
        private Text _turnBadgeText;
        private Text _hintBannerText;
        private Text _chainBannerText;
        private GameObject _passChainBtnObj;

        // LOTD PhaseBar
        private readonly Dictionary<int, Image> _phaseImages = new Dictionary<int, Image>();
        private Button _btnToBp;
        private Button _btnToMp2;
        private Button _btnToEp;

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
            public GameObject placeGlow;
            public Button btn;
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

        // Stack counters (Deck / Extra / GY / Banished)
        private class StackTileView
        {
            public Text text;
            public Outline border;
        }

        private StackTileView _myDeckTile, _myExtraTile, _myGyTile, _myBanTile;
        private StackTileView _oppDeckTile, _oppExtraTile, _oppGyTile, _oppBanTile;
        private Text _oppHandCountTxt;

        // Multi-place selection accumulator (for MSG_SELECT_PLACE / MSG_SELECT_DISFIELD with min > 1)
        private readonly List<(byte con, uint loc, uint seq)> _pendingPlaces = new List<(byte con, uint loc, uint seq)>();
        private DuelPrompt _pendingPlacePrompt;

        // Hand rows
        private RectTransform _myHandBar;
        private RectTransform _oppHandBar;

        // Context Action Popup Menu
        private GameObject _actionPopup;
        private RectTransform _actionList;
        private Text _actionHeader;

        public DuelFieldUI(GameAppController app, Transform canvasParent, DuelPromptModals modals)
        {
            _app = app;
            _modals = modals;
            BuildUI(canvasParent);
        }

        public void BindSession(DuelSession session, OpponentCharacter oppChar, PresetDeckInfo oppDeck)
        {
            _session = session;
            _oppChar = oppChar;
            _oppDeck = oppDeck;
            _pendingPlaces.Clear();
            _pendingPlacePrompt = null;

            Root.SetActive(true);
            _actionPopup.SetActive(false);

            if (_oppChar != null)
            {
                _oppNameText.text = $"{_oppChar.name}\n<color=#88CCFF>{_oppDeck?.title ?? ""}</color>";
                var bust = LotdUiKit.LoadSprite($"LOTD/Busts/{_oppChar.portrait}");
                _oppBustImg.sprite = bust;
                _oppBustImg.gameObject.SetActive(bust != null);
            }
            else
            {
                _oppNameText.text = "AI 듀얼리스트";
                _oppBustImg.gameObject.SetActive(false);
            }

            InspectCard(89631139);
            Refresh();
        }

        public void InspectCard(uint code)
        {
            if (code == 0) return;
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
            _inspStats.text = info.FormatStatsLineKR();
            _inspDesc.text = info.desc;
            var rSp = info.IsMonster ? LotdUiKit.GetRaceSprite(info.race) : LotdUiKit.LoadSprite($"LOTD/Icons/{YgoConstants.GetSpellTrapSubSpriteName(info.type)}");
            _inspRaceIcon.sprite = rSp;
            _inspRaceIcon.gameObject.SetActive(rSp != null);
        }

        private void BuildUI(Transform parent)
        {
            var rt = LotdUiKit.FillRect("Screen_DuelField", parent);
            Root = rt.gameObject;

            var bg = LotdUiKit.FillRect("Bg", rt);
            LotdUiKit.AddImage(bg.gameObject, LotdUiKit.LoadSprite("LOTD/Background/noise_bgtec_small"), new Color(0.45f, 0.72f, 1f, 1f));
            LotdUiKit.AddImage(LotdUiKit.FillRect("Dim", rt).gameObject, LotdUiKit.WhiteSprite, new Color(0.015f, 0.045f, 0.11f, 0.76f));

            BuildLeftPanel(rt);
            BuildTopHud(rt);
            BuildCenterBoard(rt);
            BuildBottomHudAndHands(rt);
            BuildActionPopup(rt);

            Root.SetActive(false);
        }

        private void BuildLeftPanel(Transform root)
        {
            var left = LotdUiKit.CreateBorderedPanel(root, "LeftInspector",
                new Vector2(0.005f, 0.01f), new Vector2(0.255f, 0.99f), Vector2.zero, Vector2.zero);

            // Opponent mini HUD card at top of left column
            var oppHud = LotdUiKit.CreateRect("OppHud", left, new Vector2(0.02f, 0.855f), new Vector2(0.98f, 0.985f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(oppHud.gameObject, LotdUiKit.LoadSprite("LOTD/HUD/lp_frame_red") ?? LotdUiKit.WhiteSprite,
                new Color(0.9f, 0.35f, 0.35f, 0.85f));
            _oppBustImg = LotdUiKit.AddImage(LotdUiKit.CreateRect("Bust", oppHud, new Vector2(0.02f, 0.06f), new Vector2(0.24f, 0.94f), Vector2.zero, Vector2.zero).gameObject,
                null, Color.white, true);
            _oppNameText = LotdUiKit.AddText(LotdUiKit.CreateRect("Name", oppHud, new Vector2(0.26f, 0.48f), new Vector2(0.70f, 0.95f), Vector2.zero, Vector2.zero),
                "T", "", 13, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            _oppLpText = LotdUiKit.AddText(LotdUiKit.CreateRect("LP", oppHud, new Vector2(0.68f, 0.42f), new Vector2(0.97f, 0.94f), Vector2.zero, Vector2.zero),
                "LP", "8000", 22, TextAnchor.MiddleRight, Color.white, FontStyle.Bold);

            var lpTrack = LotdUiKit.CreateRect("LpTrack", oppHud, new Vector2(0.26f, 0.14f), new Vector2(0.96f, 0.36f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(lpTrack.gameObject, LotdUiKit.WhiteSprite, new Color(0.15f, 0.05f, 0.05f, 0.9f));
            _oppLpFill = LotdUiKit.FillRect("Fill", lpTrack, 1f);
            LotdUiKit.AddImage(_oppLpFill.gameObject, LotdUiKit.LoadSprite("LOTD/HUD/LP_FILL"), new Color(0.95f, 0.25f, 0.25f, 1f));

            // CompositeCardView Inspector
            var cRt = LotdUiKit.CreateRect("CardView", left, new Vector2(0.05f, 0.44f), new Vector2(0.64f, 0.845f), Vector2.zero, Vector2.zero);
            _inspCard = CompositeCardView.Create(cRt, "Insp", true);

            var rRt = LotdUiKit.CreateRect("RaceIc", left, new Vector2(0.67f, 0.72f), new Vector2(0.92f, 0.835f), Vector2.zero, Vector2.zero);
            _inspRaceIcon = LotdUiKit.AddImage(rRt.gameObject, null, Color.white, true);

            _inspStats = LotdUiKit.AddText(LotdUiKit.CreateRect("Stats", left, new Vector2(0.65f, 0.45f), new Vector2(0.97f, 0.71f), Vector2.zero, Vector2.zero),
                "St", "", 12, TextAnchor.UpperCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

            _inspName = LotdUiKit.AddText(LotdUiKit.CreateRect("CName", left, new Vector2(0.03f, 0.39f), new Vector2(0.97f, 0.44f), Vector2.zero, Vector2.zero),
                "N", "", 16, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);

            // Scrollable description
            var descBox = LotdUiKit.CreateBorderedPanel(left, "DescBox", new Vector2(0.03f, 0.16f), new Vector2(0.97f, 0.385f), Vector2.zero, Vector2.zero,
                new Color(0.03f, 0.07f, 0.15f, 0.95f));
            var vp = LotdUiKit.FillRect("VP", descBox, 6f);
            vp.gameObject.AddComponent<RectMask2D>();
            var ct = LotdUiKit.CreateRect("C", vp, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            ct.pivot = new Vector2(0.5f, 1f);
            _inspDesc = ct.gameObject.AddComponent<Text>();
            _inspDesc.font = LotdUiKit.FontKR;
            _inspDesc.fontSize = 13;
            _inspDesc.color = LotdUiKit.TextPrimary;
            _inspDesc.alignment = TextAnchor.UpperLeft;
            ct.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sv = descBox.gameObject.AddComponent<ScrollRect>();
            sv.horizontal = false; sv.vertical = true; sv.scrollSensitivity = 20f;
            sv.viewport = vp; sv.content = ct;

            // Compact duel log feed at bottom-left
            var logBox = LotdUiKit.CreateBorderedPanel(left, "LogBox", new Vector2(0.03f, 0.015f), new Vector2(0.97f, 0.15f), Vector2.zero, Vector2.zero,
                new Color(0.02f, 0.05f, 0.12f, 0.95f));
            _logFeed = LotdUiKit.AddText(logBox, "Log", "", 11, TextAnchor.LowerLeft, LotdUiKit.TextMuted);
            _logFeed.rectTransform.offsetMin = new Vector2(6, 4);
            _logFeed.rectTransform.offsetMax = new Vector2(-6, -4);
        }

        private void BuildTopHud(Transform root)
        {
            var top = LotdUiKit.CreateBorderedPanel(root, "TopBar",
                new Vector2(0.262f, 0.90f), new Vector2(0.995f, 0.99f), Vector2.zero, Vector2.zero,
                LotdUiKit.PanelHeader, LotdUiKit.CyanAccent);

            // Opponent hand strip + count on left of top bar
            _oppHandBar = LotdUiKit.CreateRect("OppHand", top, new Vector2(0.01f, 0.29f), new Vector2(0.28f, 0.96f), Vector2.zero, Vector2.zero);
            _oppHandCountTxt = LotdUiKit.AddText(LotdUiKit.CreateRect("OHCnt", top, new Vector2(0.01f, 0.02f), new Vector2(0.28f, 0.28f), Vector2.zero, Vector2.zero),
                "HC", "상대 패: 5장", 12, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);

            // Turn badge & Phase bar in center
            var tRt = LotdUiKit.CreateRect("TurnBadge", top, new Vector2(0.29f, 0.10f), new Vector2(0.41f, 0.90f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(tRt.gameObject, LotdUiKit.LoadSprite("LOTD/HUD/icon_turn"), Color.white, true);
            _turnBadgeText = LotdUiKit.AddText(tRt, "T", "TURN 1", 15, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);

            var phRow = LotdUiKit.CreateRect("PhaseBar", top, new Vector2(0.42f, 0.25f), new Vector2(0.84f, 0.92f), Vector2.zero, Vector2.zero);
            (int phase, string key, string label)[] phases =
            {
                (YgoConstants.PHASE_DRAW, "dp", "DP"),
                (YgoConstants.PHASE_STANDBY, "sp", "SP"),
                (YgoConstants.PHASE_MAIN1, "mp1", "MP1"),
                (YgoConstants.PHASE_BATTLE, "bp", "BP"),
                (YgoConstants.PHASE_MAIN2, "mp2", "MP2"),
                (YgoConstants.PHASE_END, "ep", "EP"),
            };
            for (int i = 0; i < phases.Length; i++)
            {
                var ph = phases[i];
                var slot = LotdUiKit.CreateRect($"Ph_{ph.key}", phRow, new Vector2(i / 6f, 0f), new Vector2((i + 1) / 6f - 0.008f, 1f), Vector2.zero, Vector2.zero);
                var img = LotdUiKit.AddImage(slot.gameObject, LotdUiKit.LoadSprite($"LOTD/PhaseBar/blue_{ph.key}"), Color.white, true);
                _phaseImages[ph.phase] = img;

                if (ph.phase == YgoConstants.PHASE_BATTLE || ph.phase == YgoConstants.PHASE_MAIN2 || ph.phase == YgoConstants.PHASE_END)
                {
                    var btn = slot.gameObject.AddComponent<Button>();
                    btn.targetGraphic = img;
                    int targetPh = ph.phase;
                    btn.onClick.AddListener(() => OnPhaseShortcut(targetPh));
                }
            }

            _hintBannerText = LotdUiKit.AddText(LotdUiKit.CreateRect("HintBanner", top, new Vector2(0.42f, 0.02f), new Vector2(0.84f, 0.25f), Vector2.zero, Vector2.zero),
                "H", "내 턴 — 카드를 클릭하여 행동하세요", 12, TextAnchor.LowerCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

            // Surrender / Back to Menu button on top-right
            var surRt = LotdUiKit.CreateRect("Surrender", top, new Vector2(0.855f, 0.14f), new Vector2(0.99f, 0.86f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(surRt, "B", "항복 / 메뉴", OnSurrenderClicked, 14);
        }

        private void BuildCenterBoard(Transform root)
        {
            var board = LotdUiKit.CreateRect("DuelBoard", root,
                new Vector2(0.265f, 0.21f), new Vector2(0.992f, 0.892f), Vector2.zero, Vector2.zero);

            // 7 columns x 5 rows grid
            // Rows (top to bottom): 0=Opp S/T, 1=Opp MZone, 2=Shared EMZ, 3=My MZone, 4=My S/T
            float[] rowYMin = { 0.805f, 0.605f, 0.415f, 0.215f, 0.010f };
            float[] rowYMax = { 0.995f, 0.795f, 0.595f, 0.405f, 0.200f };

            float colW = 1f / 7f;
            float padX = 0.004f;

            RectTransform CellRect(int col, int row) =>
                LotdUiKit.CreateRect($"C_{col}_{row}", board,
                    new Vector2(col * colW + padX, rowYMin[row]),
                    new Vector2((col + 1) * colW - padX, rowYMax[row]),
                    Vector2.zero, Vector2.zero);

            // Opponent S/T [4..0] across cols 1..5 in row 0
            for (int col = 1; col <= 5; col++)
            {
                int seq = 5 - col; // col 1->4, col 5->0
                _oppSZone[seq] = CreateZoneSlot(CellRect(col, 0), seq == 4 ? "LOTD/Field/pend_l_red" : seq == 0 ? "LOTD/Field/pend_r_red" : "LOTD/Field/monster_red",
                    false, (c, l, s) => OnSlotClicked(c, l, s));
            }
            // Opponent Monster [4..0] across cols 1..5 in row 1
            for (int col = 1; col <= 5; col++)
            {
                int seq = 5 - col;
                _oppMZone[seq] = CreateZoneSlot(CellRect(col, 1), "LOTD/Field/monster_red", false, (c, l, s) => OnSlotClicked(c, l, s));
            }

            // Shared EMZ: Left (col 2, above My MZone[1]) & Right (col 4, above My MZone[3])
            _emz[0] = CreateZoneSlot(CellRect(2, 2), "LOTD/Field/monster_neutral", true, (c, l, s) => OnSlotClicked(c, l, s));
            _emz[1] = CreateZoneSlot(CellRect(4, 2), "LOTD/Field/monster_neutral", true, (c, l, s) => OnSlotClicked(c, l, s));

            // Center Chain Banner between EMZ slots (col 3, row 2)
            var midBanner = LotdUiKit.CreateRect("MidBanner", board,
                new Vector2(3 * colW + padX, rowYMin[2]), new Vector2(4 * colW - padX, rowYMax[2]), Vector2.zero, Vector2.zero);
            _chainBannerText = LotdUiKit.AddText(midBanner, "Ch", "", 13, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

            // Player Monster [0..4] across cols 1..5 in row 3
            for (int col = 1; col <= 5; col++)
            {
                int seq = col - 1;
                _myMZone[seq] = CreateZoneSlot(CellRect(col, 3), "LOTD/Field/monster_blue", true, (c, l, s) => OnSlotClicked(c, l, s));
            }
            // Player S/T [0..4] across cols 1..5 in row 4
            for (int col = 1; col <= 5; col++)
            {
                int seq = col - 1;
                _mySZone[seq] = CreateZoneSlot(CellRect(col, 4), seq == 0 ? "LOTD/Field/pend_l_blue" : seq == 4 ? "LOTD/Field/pend_r_blue" : "LOTD/Field/monster_blue",
                    true, (c, l, s) => OnSlotClicked(c, l, s));
            }

            // Column 0 (Left Edge): Opp Banished (0), Opp GY (1), Opp Deck (2) | My Extra (3), My Field (4)
            _oppBanTile = CreateStackTile(CellRect(0, 0), "LOTD/Field/exclude_red", "상대 제외\n0",
                () => OpenStackZone((byte)_session.AiPlayerIndex, YgoConstants.LOCATION_REMOVED, "상대 제외 존", true));
            _oppGyTile = CreateStackTile(CellRect(0, 1), "LOTD/Field/graveyard_red", "상대 묘지\n0",
                () => OpenStackZone((byte)_session.AiPlayerIndex, YgoConstants.LOCATION_GRAVE, "상대 묘지"));
            _oppDeckTile = CreateStackTile(CellRect(0, 2), "LOTD/Field/monster_red", "상대 덱\n0",
                () => OpenStackZone((byte)_session.AiPlayerIndex, YgoConstants.LOCATION_DECK, "상대 덱"));
            _myExtraTile = CreateStackTile(CellRect(0, 3), "LOTD/Field/extra_blue", "엑스트라\n0",
                () => OpenStackZone((byte)_session.HumanPlayerIndex, YgoConstants.LOCATION_EXTRA, "내 엑스트라 덱"));
            _mySZone[5] = CreateZoneSlot(CellRect(0, 4), "LOTD/Field/field_blue", true, (c, l, s) => OnSlotClicked(c, l, s));

            // Column 6 (Right Edge): Opp Field (0), Opp Extra (1) | My Deck (2), My GY (3), My Banished (4)
            _oppSZone[5] = CreateZoneSlot(CellRect(6, 0), "LOTD/Field/field_red", false, (c, l, s) => OnSlotClicked(c, l, s));
            _oppExtraTile = CreateStackTile(CellRect(6, 1), "LOTD/Field/extra_red", "상대 EX\n0",
                () => OpenStackZone((byte)_session.AiPlayerIndex, YgoConstants.LOCATION_EXTRA, "상대 엑스트라 덱"));
            _myDeckTile = CreateStackTile(CellRect(6, 2), "LOTD/Field/monster_blue", "내 덱\n0",
                () => OpenStackZone((byte)_session.HumanPlayerIndex, YgoConstants.LOCATION_DECK, "내 덱"));
            _myGyTile = CreateStackTile(CellRect(6, 3), "LOTD/Field/graveyard_blue", "내 묘지\n0",
                () => OpenStackZone((byte)_session.HumanPlayerIndex, YgoConstants.LOCATION_GRAVE, "내 묘지"));
            _myBanTile = CreateStackTile(CellRect(6, 4), "LOTD/Field/exclude_blue", "내 제외\n0",
                () => OpenStackZone((byte)_session.HumanPlayerIndex, YgoConstants.LOCATION_REMOVED, "내 제외 존"));
        }

        private FieldSlotView CreateZoneSlot(RectTransform cell, string frameSpritePath, bool isBlue, Action<byte, uint, uint> onClick)
        {
            var sp = LotdUiKit.LoadSprite(frameSpritePath);
            var bg = LotdUiKit.AddImage(cell.gameObject, sp ?? LotdUiKit.WhiteSprite, sp != null ? Color.white : new Color(0.08f, 0.16f, 0.30f, 0.8f), false);
            var ol = cell.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0, 0, 0, 0);
            ol.effectDistance = new Vector2(2f, -2f);

            var glowRt = LotdUiKit.FillRect("PlaceGlow", cell);
            LotdUiKit.AddImage(glowRt.gameObject, LotdUiKit.WhiteSprite, new Color(0.15f, 0.90f, 1f, 0.32f), false, false);
            glowRt.gameObject.SetActive(false);

            var cardRt = LotdUiKit.CreateRect("Card", cell, new Vector2(0.14f, 0.06f), new Vector2(0.86f, 0.94f), Vector2.zero, Vector2.zero);
            var cv = CompositeCardView.Create(cardRt, "CardView", false);
            cv.gameObject.SetActive(false);

            var pillRt = LotdUiKit.CreateRect("AtkPill", cell, new Vector2(0.06f, 0.0f), new Vector2(0.94f, 0.24f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(pillRt.gameObject, LotdUiKit.LoadSprite("LOTD/HUD/atk_def_frame") ?? LotdUiKit.WhiteSprite, new Color(0.1f, 0.1f, 0.15f, 0.92f), false, false);
            var atkTxt = LotdUiKit.AddText(pillRt, "Stat", "", 11, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            pillRt.gameObject.SetActive(false);

            var badgeTxt = LotdUiKit.AddText(LotdUiKit.CreateRect("Badge", cell, new Vector2(0.65f, 0.72f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                "B", "", 11, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

            var slot = new FieldSlotView
            {
                root = cell,
                bgImg = bg,
                border = ol,
                cardView = cv,
                atkPill = pillRt,
                atkText = atkTxt,
                badgeText = badgeTxt,
                placeGlow = glowRt.gameObject,
            };

            var btn = cell.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick?.Invoke(slot.mappedCon, slot.mappedLoc, slot.mappedSeq));
            slot.btn = btn;
            return slot;
        }

        private StackTileView CreateStackTile(RectTransform cell, string spritePath, string label, Action onClick)
        {
            var sp = LotdUiKit.LoadSprite(spritePath);
            var bg = LotdUiKit.AddImage(cell.gameObject, sp ?? LotdUiKit.WhiteSprite, sp != null ? Color.white : LotdUiKit.PanelNavy, false);
            var ol = cell.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0, 0, 0, 0);
            ol.effectDistance = new Vector2(2f, -2f);

            var pill = LotdUiKit.CreateRect("Pill", cell, new Vector2(0.06f, 0.18f), new Vector2(0.94f, 0.82f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(pill.gameObject, LotdUiKit.WhiteSprite, new Color(0.02f, 0.06f, 0.14f, 0.80f), false, false);
            var txt = LotdUiKit.AddText(pill, "T", label, 11, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);
            if (onClick != null)
            {
                var btn = cell.gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(() =>
                {
                    if (_session == null) return;
                    LotdAudio.PlayClick();
                    onClick();
                });
            }
            return new StackTileView { text = txt, border = ol };
        }

        private void BuildBottomHudAndHands(Transform root)
        {
            var bot = LotdUiKit.CreateBorderedPanel(root, "BottomZone",
                new Vector2(0.262f, 0.01f), new Vector2(0.995f, 0.20f), Vector2.zero, Vector2.zero);

            // Human hand container on left 68% of bottom bar
            _myHandBar = LotdUiKit.CreateRect("MyHand", bot, new Vector2(0.01f, 0.04f), new Vector2(0.69f, 0.96f), Vector2.zero, Vector2.zero);

            // Human LP + Quick Phase control buttons on right 30%
            var myHud = LotdUiKit.CreateRect("MyHud", bot, new Vector2(0.70f, 0.44f), new Vector2(0.99f, 0.95f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(myHud.gameObject, LotdUiKit.LoadSprite("LOTD/HUD/lp_frame_blue") ?? LotdUiKit.WhiteSprite, new Color(0.55f, 0.82f, 1f, 0.92f));
            LotdUiKit.AddText(LotdUiKit.CreateRect("Lbl", myHud, new Vector2(0.05f, 0.42f), new Vector2(0.45f, 0.95f), Vector2.zero, Vector2.zero),
                "L", "PLAYER LP", 13, TextAnchor.MiddleLeft, LotdUiKit.CyanAccent, FontStyle.Bold);
            _myLpText = LotdUiKit.AddText(LotdUiKit.CreateRect("LP", myHud, new Vector2(0.42f, 0.40f), new Vector2(0.96f, 0.95f), Vector2.zero, Vector2.zero),
                "V", "8000", 24, TextAnchor.MiddleRight, Color.white, FontStyle.Bold);

            var lpTrack = LotdUiKit.CreateRect("LpTrack", myHud, new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.36f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(lpTrack.gameObject, LotdUiKit.WhiteSprite, new Color(0.04f, 0.10f, 0.20f, 0.95f));
            _myLpFill = LotdUiKit.FillRect("Fill", lpTrack, 1f);
            LotdUiKit.AddImage(_myLpFill.gameObject, LotdUiKit.LoadSprite("LOTD/HUD/LP_FILL"), LotdUiKit.CyanAccent);

            // Quick Phase transition buttons
            var phBtns = LotdUiKit.CreateRect("QuickPhase", bot, new Vector2(0.70f, 0.05f), new Vector2(0.99f, 0.40f), Vector2.zero, Vector2.zero);
            _btnToBp = LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("B1", phBtns, new Vector2(0f, 0f), new Vector2(0.32f, 1f), Vector2.zero, Vector2.zero),
                "B", "배틀", () => OnPhaseShortcut(YgoConstants.PHASE_BATTLE), 13);
            _btnToMp2 = LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("B2", phBtns, new Vector2(0.34f, 0f), new Vector2(0.66f, 1f), Vector2.zero, Vector2.zero),
                "B", "메인 2", () => OnPhaseShortcut(YgoConstants.PHASE_MAIN2), 13);
            _btnToEp = LotdUiKit.CreateLotdButton(LotdUiKit.CreateRect("B3", phBtns, new Vector2(0.68f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero),
                "B", "턴 종료", () => OnPhaseShortcut(YgoConstants.PHASE_END), 13, gold: true);
        }

        private void BuildActionPopup(Transform root)
        {
            var dim = LotdUiKit.FillRect("Overlay_CardActions", root);
            _actionPopup = dim.gameObject;
            var bgBtn = LotdUiKit.AddImage(dim.gameObject, LotdUiKit.WhiteSprite, new Color(0, 0, 0, 0.35f));
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => _actionPopup.SetActive(false));

            var win = LotdUiKit.CreateBorderedPanel(dim, "Win",
                new Vector2(0.50f, 0.25f), new Vector2(0.76f, 0.65f), Vector2.zero, Vector2.zero,
                LotdUiKit.PanelNavy, LotdUiKit.GoldAccent);
            _actionHeader = LotdUiKit.AddText(LotdUiKit.CreateRect("Hdr", win, new Vector2(0f, 0.84f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero),
                "T", "행동 선택", 17, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

            _actionList = LotdUiKit.CreateRect("List", win, new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.82f), Vector2.zero, Vector2.zero);
            var vlg = _actionList.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            _actionPopup.SetActive(false);
        }

        // ==================== REFRESH & BINDING ====================
        public void Refresh()
        {
            if (_session == null || Root == null || !Root.activeSelf) return;

            var p = _session.ActivePrompt;
            if (!ReferenceEquals(p, _pendingPlacePrompt))
            {
                _pendingPlacePrompt = p;
                _pendingPlaces.Clear();
            }

            int me = _session.HumanPlayerIndex;
            int opp = _session.AiPlayerIndex;
            var myP = _session.Players[me];
            var opP = _session.Players[opp];

            int myLpDisp = Mathf.Max(0, myP.lp);
            int opLpDisp = Mathf.Max(0, opP.lp);
            _myLpText.text = myLpDisp.ToString();
            _myLpFill.anchorMax = new Vector2(Mathf.Clamp01(myLpDisp / 8000f), 1f);
            _oppLpText.text = opLpDisp.ToString();
            _oppLpFill.anchorMax = new Vector2(Mathf.Clamp01(opLpDisp / 8000f), 1f);

            _turnBadgeText.text = $"TURN {_session.TurnNumber}";
            string turnOwner = _session.TurnPlayer == me ? "<color=#44DDFF>내 턴</color>" : "<color=#FF6666>상대 턴</color>";
            string phKr = YgoConstants.GetPhaseNameKR(_session.CurrentPhase);
            bool waitingMyAction = p != null && p.playerId == me;
            string extraCue = "";
            if (waitingMyAction && (p.messageType == YgoConstants.MSG_SELECT_PLACE || p.messageType == YgoConstants.MSG_SELECT_DISFIELD))
            {
                int needPlaces = Mathf.Max(1, (int)p.min);
                extraCue = needPlaces > 1
                    ? $"  •  <color=#FFCC44>존 {needPlaces}곳 선택 ({_pendingPlaces.Count}/{needPlaces})</color>"
                    : "  •  <color=#FFCC44>빈 존을 클릭하세요!</color>";
            }
            _hintBannerText.text = $"{turnOwner} — {phKr}{extraCue}";

            // Phase bar colors
            foreach (var kv in _phaseImages)
            {
                bool active = (_session.CurrentPhase == kv.Key) ||
                    (kv.Key == YgoConstants.PHASE_BATTLE && _session.CurrentPhase >= YgoConstants.PHASE_BATTLE_START && _session.CurrentPhase <= YgoConstants.PHASE_BATTLE);
                string suffix = kv.Key == YgoConstants.PHASE_DRAW ? "dp" :
                                kv.Key == YgoConstants.PHASE_STANDBY ? "sp" :
                                kv.Key == YgoConstants.PHASE_MAIN1 ? "mp1" :
                                kv.Key == YgoConstants.PHASE_BATTLE ? "bp" :
                                kv.Key == YgoConstants.PHASE_MAIN2 ? "mp2" : "ep";
                string prefix = active ? "hi" : (_session.TurnPlayer == me ? "blue" : "red");
                kv.Value.sprite = LotdUiKit.LoadSprite($"LOTD/PhaseBar/{prefix}_{suffix}");
            }

            // Phase quick buttons interactability
            bool myIdle = waitingMyAction && p.messageType == YgoConstants.MSG_SELECT_IDLECMD;
            bool myBattle = waitingMyAction && p.messageType == YgoConstants.MSG_SELECT_BATTLECMD;
            _btnToBp.interactable = myIdle && p.canBp;
            _btnToMp2.interactable = myBattle && p.canM2;
            _btnToEp.interactable = (myIdle && p.canEp) || (myBattle && p.canEp);

            // Stacks (with playable EX / GY / Banished / Deck action highlight)
            UpdateStackTile(_myDeckTile, "내 덱", myP.deckCount, (byte)me, YgoConstants.LOCATION_DECK, waitingMyAction);
            UpdateStackTile(_myExtraTile, "엑스트라", myP.extra.Count, (byte)me, YgoConstants.LOCATION_EXTRA, waitingMyAction);
            UpdateStackTile(_myGyTile, "내 묘지", myP.grave.Count, (byte)me, YgoConstants.LOCATION_GRAVE, waitingMyAction);
            UpdateStackTile(_myBanTile, "내 제외", myP.banished.Count, (byte)me, YgoConstants.LOCATION_REMOVED, waitingMyAction);

            UpdateStackTile(_oppDeckTile, "상대 덱", opP.deckCount, (byte)opp, YgoConstants.LOCATION_DECK, waitingMyAction);
            UpdateStackTile(_oppExtraTile, "상대 EX", opP.extra.Count, (byte)opp, YgoConstants.LOCATION_EXTRA, waitingMyAction);
            UpdateStackTile(_oppGyTile, "상대 묘지", opP.grave.Count, (byte)opp, YgoConstants.LOCATION_GRAVE, waitingMyAction);
            UpdateStackTile(_oppBanTile, "상대 제외", opP.banished.Count, (byte)opp, YgoConstants.LOCATION_REMOVED, waitingMyAction);
            _oppHandCountTxt.text = $"상대 패: {opP.hand.Count}장";

            // Chain banner
            _chainBannerText.text = _session.CurrentChain.Count > 0 ? $"CHAIN\n{_session.CurrentChain.Count} LINK" : "";

            // Board slots
            for (int i = 0; i < 5; i++)
            {
                BindSlot(_myMZone[i], (byte)me, YgoConstants.LOCATION_MZONE, (uint)i, myP.mzone[i], isOpponent: false);
                BindSlot(_mySZone[i], (byte)me, YgoConstants.LOCATION_SZONE, (uint)i, myP.szone[i], isOpponent: false);
                BindSlot(_oppMZone[i], (byte)opp, YgoConstants.LOCATION_MZONE, (uint)i, opP.mzone[i], isOpponent: true);
                BindSlot(_oppSZone[i], (byte)opp, YgoConstants.LOCATION_SZONE, (uint)i, opP.szone[i], isOpponent: true);
            }
            BindSlot(_mySZone[5], (byte)me, YgoConstants.LOCATION_SZONE, 5, myP.szone[5], isOpponent: false);
            BindSlot(_oppSZone[5], (byte)opp, YgoConstants.LOCATION_SZONE, 5, opP.szone[5], isOpponent: true);

            // EMZ Left ([0]) = my mzone[5] OR opp mzone[6]; EMZ Right ([1]) = my mzone[6] OR opp mzone[5]
            BindEmz(_emz[0], (byte)me, 5, (byte)opp, 6, myP.mzone[5], opP.mzone[6]);
            BindEmz(_emz[1], (byte)me, 6, (byte)opp, 5, myP.mzone[6], opP.mzone[5]);

            RefreshHands(myP, opP, p, waitingMyAction);
            RefreshLogFeed();
        }

        private void UpdateStackTile(StackTileView tile, string baseLabel, int count, byte con, uint loc, bool waitingMyAction)
        {
            if (tile == null) return;
            int actCount = waitingMyAction ? CountZoneActions(con, loc) : 0;
            tile.text.text = actCount > 0
                ? $"{baseLabel}\n{count} <color=#FFCC44>[가능 {actCount}]</color>"
                : $"{baseLabel}\n{count}";
            tile.border.effectColor = actCount > 0 ? LotdUiKit.GoldAccent : new Color(0, 0, 0, 0);
        }

        private void BindEmz(FieldSlotView slot, byte myCon, uint mySeq, byte oppCon, uint oppSeq, LiveCardState myEmz, LiveCardState oppEmz)
        {
            if (oppEmz != null)
                BindSlot(slot, oppCon, YgoConstants.LOCATION_MZONE, oppSeq, oppEmz, isOpponent: true);
            else if (myEmz == null && !IsPlaceSelectable(myCon, YgoConstants.LOCATION_MZONE, mySeq) && IsPlaceSelectable(oppCon, YgoConstants.LOCATION_MZONE, oppSeq))
                BindSlot(slot, oppCon, YgoConstants.LOCATION_MZONE, oppSeq, null, isOpponent: true);
            else
                BindSlot(slot, myCon, YgoConstants.LOCATION_MZONE, mySeq, myEmz, isOpponent: false);
        }

        private void BindSlot(FieldSlotView slot, byte con, uint loc, uint seq, LiveCardState live, bool isOpponent)
        {
            slot.mappedCon = con;
            slot.mappedLoc = loc;
            slot.mappedSeq = seq;
            slot.liveCard = live;

            if (live != null)
            {
                slot.cardView.gameObject.SetActive(true);
                bool showBack = !live.IsFaceUp;
                slot.cardView.SetCard(showBack ? 0u : live.code, faceDown: showBack, live: live);
                slot.cardView.transform.localRotation = (loc == YgoConstants.LOCATION_MZONE && live.IsDefense)
                    ? Quaternion.Euler(0, 0, isOpponent ? 90f : -90f)
                    : Quaternion.identity;

                if (loc == YgoConstants.LOCATION_MZONE && !showBack)
                {
                    slot.atkPill.gameObject.SetActive(true);
                    string a = live.attack < 0 ? "?" : live.attack.ToString();
                    string d = (live.type & YgoConstants.TYPE_LINK) != 0 ? $"L-{live.link}" : (live.defense < 0 ? "?" : live.defense.ToString());
                    slot.atkText.text = $"{a} / {d}";
                    slot.atkText.color = live.attack > live.baseAttack ? LotdUiKit.CyanAccent : (live.attack < live.baseAttack ? LotdUiKit.RedAccent : Color.white);
                }
                else slot.atkPill.gameObject.SetActive(false);

                slot.badgeText.text = live.overlayCodes.Count > 0 ? $"★{live.overlayCodes.Count}" : "";
            }
            else
            {
                slot.cardView.gameObject.SetActive(false);
                slot.atkPill.gameObject.SetActive(false);
                slot.badgeText.text = "";
            }

            // Highlight selectable places, pending multi-place picks, or playable field cards
            bool alreadyPicked = IsPlacePending(con, loc, seq);
            bool placeValid = !alreadyPicked && IsPlaceSelectable(con, loc, seq);
            slot.placeGlow.SetActive(placeValid || alreadyPicked);
            slot.border.effectColor = alreadyPicked ? LotdUiKit.CyanAccent
                : (placeValid ? LotdUiKit.GoldAccent
                : (live != null && HasFieldAction(con, loc, seq) ? LotdUiKit.CyanAccent : new Color(0, 0, 0, 0)));
        }

        private void RefreshHands(PlayerFieldSnapshot myP, PlayerFieldSnapshot opP, DuelPrompt p, bool waitingMyAction)
        {
            for (int i = _myHandBar.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_myHandBar.GetChild(i).gameObject);
            for (int i = _oppHandBar.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_oppHandBar.GetChild(i).gameObject);

            // Opponent hand face-down mini cards
            int opCnt = Mathf.Min(10, opP.hand.Count);
            float ow = opCnt > 0 ? Mathf.Min(0.11f, 0.98f / opCnt) : 0.1f;
            for (int i = 0; i < opCnt; i++)
            {
                var rt = LotdUiKit.CreateRect($"OH_{i}", _oppHandBar, new Vector2(i * ow, 0.05f), new Vector2(i * ow + ow - 0.008f, 0.95f), Vector2.zero, Vector2.zero);
                var c = opP.hand[i];
                bool vis = c != null && (c.isPublic || c.code != 0 && c.IsFaceUp);
                CompositeCardView.Create(rt, "V", false).SetCard(vis ? c.code : 0u, !vis, c);
            }

            // Human hand cards
            int cnt = myP.hand.Count;
            if (cnt == 0) return;
            float cw = Mathf.Min(0.115f, 0.98f / cnt);
            for (int i = 0; i < cnt; i++)
            {
                var hc = myP.hand[i];
                if (hc == null) continue;
                uint seq = (uint)i;
                float x = i * cw;
                bool actionable = waitingMyAction && HasHandAction((byte)_session.HumanPlayerIndex, seq);

                var slot = LotdUiKit.CreateRect($"MH_{i}", _myHandBar,
                    new Vector2(x, actionable ? 0.06f : 0.01f), new Vector2(x + cw - 0.006f, actionable ? 0.99f : 0.92f),
                    Vector2.zero, Vector2.zero);
                var ol = slot.gameObject.AddComponent<Outline>();
                ol.effectColor = actionable ? LotdUiKit.CyanAccent : new Color(0, 0, 0, 0);
                ol.effectDistance = new Vector2(2, -2);

                CompositeCardView.Create(slot, "V", false).SetCard(hc.code, false, hc);
                var hit = LotdUiKit.AddImage(LotdUiKit.FillRect("Hit", slot).gameObject, LotdUiKit.WhiteSprite, Color.clear);
                var btn = slot.gameObject.AddComponent<Button>();
                btn.targetGraphic = hit;
                uint cardCode = hc.code;
                btn.onClick.AddListener(() =>
                {
                    InspectCard(cardCode);
                    ShowCardActionsPopup((byte)_session.HumanPlayerIndex, YgoConstants.LOCATION_HAND, seq, cardCode, hc.overlayCodes);
                });
            }
        }

        private void RefreshLogFeed()
        {
            if (_session == null) return;
            int start = Mathf.Max(0, _session.Logs.Count - 5);
            _logFeed.text = string.Join("\n", _session.Logs.Skip(start));
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
                    if (predicate(p.mSetable[i])) dest.Add((p.mSetable[i], "몬스터 뒷면 세트", (i << 16) | 3, false));
                for (int i = 0; i < p.sSetable.Count; i++)
                    if (predicate(p.sSetable[i])) dest.Add((p.sSetable[i], "마법/함정 세트", (i << 16) | 4, false));
                for (int i = 0; i < p.activatable.Count; i++)
                {
                    if (!predicate(p.activatable[i])) continue;
                    string desc = CardDatabase.ResolveDescription(p.activatable[i].description, p.activatable[i].code);
                    string lbl = string.IsNullOrEmpty(desc) ? "효과 발동" : $"발동: {desc}";
                    dest.Add((p.activatable[i], lbl, (i << 16) | 5, true));
                }
            }
            else if (p.messageType == YgoConstants.MSG_SELECT_BATTLECMD)
            {
                for (int i = 0; i < p.attackable.Count; i++)
                    if (predicate(p.attackable[i])) dest.Add((p.attackable[i], "공격 선언!", (i << 16) | 1, true));
                for (int i = 0; i < p.activatable.Count; i++)
                {
                    if (!predicate(p.activatable[i])) continue;
                    string desc = CardDatabase.ResolveDescription(p.activatable[i].description, p.activatable[i].code);
                    string lbl = string.IsNullOrEmpty(desc) ? "효과 발동" : $"발동: {desc}";
                    dest.Add((p.activatable[i], lbl, (i << 16) | 0, true));
                }
            }
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
            if (_session == null) return;
            LotdAudio.PlayClick();
            _actionPopup.SetActive(false);
            _session.SubmitResponseInt(responseVal);
            Refresh();
        }

        private void OpenStackZone(byte con, uint loc, string title, bool hideFaceDown = false)
        {
            if (_session == null) return;
            var p = _session.ActivePrompt;
            bool waitingMyAction = p != null && p.playerId == _session.HumanPlayerIndex;
            var zoneActions = waitingMyAction ? CollectZoneActions(con, loc) : null;

            // Keep Deck & Opponent Extra Deck hidden unless the player has legal actions there
            if (loc == YgoConstants.LOCATION_DECK || (con == _session.AiPlayerIndex && loc == YgoConstants.LOCATION_EXTRA))
            {
                if (zoneActions != null && zoneActions.Count > 0)
                    ShowZoneActionsPopup(title, zoneActions);
                return;
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
            if (_session == null) return;

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
            var actions = CollectActions(con, loc, seq);
            PopulateAndShowActionPopup(code != 0 ? CardDatabase.GetName(code) : "카드 행동", actions, overlays);
        }

        private void ShowZoneActionsPopup(string title, List<(SelectableCardItem item, string label, int responseVal, bool gold)> zoneActions)
        {
            var actions = new List<(string label, int responseVal, bool gold)>(zoneActions.Count);
            foreach (var za in zoneActions)
            {
                string cname = CardDatabase.GetName(za.item.code);
                actions.Add(($"{cname} — {za.label}", za.responseVal, za.gold));
            }
            PopulateAndShowActionPopup(title, actions, null);
        }

        private void PopulateAndShowActionPopup(string header, List<(string label, int responseVal, bool gold)> actions, List<uint> overlays)
        {
            bool hasOverlays = overlays != null && overlays.Count > 0;
            if ((actions == null || actions.Count == 0) && !hasOverlays)
            {
                _actionPopup.SetActive(false);
                return;
            }

            for (int i = _actionList.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_actionList.GetChild(i).gameObject);

            _actionHeader.text = header;

            if (actions != null)
            {
                foreach (var act in actions)
                {
                    int val = act.responseVal;
                    var row = new GameObject("ActBtn", typeof(RectTransform), typeof(LayoutElement));
                    row.transform.SetParent(_actionList, false);
                    row.GetComponent<LayoutElement>().preferredHeight = 46f;
                    LotdUiKit.CreateLotdButton(row.transform, "B", act.label, () => ExecuteActionInt(val), 15, act.gold);
                }
            }

            if (hasOverlays)
            {
                var row = new GameObject("OvlBtn", typeof(RectTransform), typeof(LayoutElement));
                row.transform.SetParent(_actionList, false);
                row.GetComponent<LayoutElement>().preferredHeight = 42f;
                LotdUiKit.CreateLotdButton(row.transform, "B", $"엑시즈 소재 확인 ({overlays.Count}장)", () =>
                {
                    _actionPopup.SetActive(false);
                    _modals.ShowCodeListViewer("엑시즈 소재", overlays);
                }, 14, false);
            }

            _actionPopup.SetActive(true);
        }

        private void OnPhaseShortcut(int targetPhase)
        {
            if (_session == null || _session.ActivePrompt == null) return;
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

        private void OnSurrenderClicked()
        {
            if (_session == null || _session.IsFinished)
            {
                _app.ShowScreen(AppScreen.MainMenu);
                return;
            }
            _session.Surrender(_session.HumanPlayerIndex);
        }
    }
}
