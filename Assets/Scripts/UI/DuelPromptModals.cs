using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public class DuelPromptModals
    {
        public GameObject ModalRoot { get; private set; }
        public GameObject ZoneViewRoot { get; private set; }
        public GameObject ResultRoot { get; private set; }

        private readonly Action<uint> _onInspectCard;
        private readonly Action<byte[]> _onSubmitResponse;

        private RectTransform _modalBox;
        private Text _modalTitle;
        private Text _modalSubtitle;
        private RectTransform _modalBody;
        private RectTransform _modalFooter;
        private Button _modalCloseBtn;

        // Zone viewer refs
        private Text _zoneTitle;
        private RectTransform _zoneGrid;

        // Result refs
        private Image _resIcon;
        private Image _resOppBust;
        private Text _resHeadline;
        private Text _resStats;
        private Action _resRematch;
        private Action _resOppSel;
        private Action _resMenu;

        private DuelSession _curSession;
        private DuelPrompt _curPrompt;

        // State for interactive multi-select modals
        private readonly HashSet<int> _selectedIndices = new HashSet<int>();
        private int[] _counterAlloc;
        private int[] _sortOrder;
        private Button _confirmBtn;
        private Text _confirmBtnText;

        public bool IsPromptOpen => ModalRoot != null && ModalRoot.activeSelf;

        public DuelPromptModals(Transform canvasParent, Action<uint> onInspectCard, Action<byte[]> onSubmitResponse)
        {
            _onInspectCard = onInspectCard;
            _onSubmitResponse = onSubmitResponse;
            BuildPromptModalShell(canvasParent);
            BuildZoneViewer(canvasParent);
            BuildResultOverlay(canvasParent);
        }

        public void HideAll()
        {
            if (ModalRoot != null) ModalRoot.SetActive(false);
            if (ZoneViewRoot != null) ZoneViewRoot.SetActive(false);
        }

        public void HideResult()
        {
            if (ResultRoot != null) ResultRoot.SetActive(false);
        }

        private void ClearBodyAndFooter()
        {
            for (int i = _modalBody.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_modalBody.GetChild(i).gameObject);
            for (int i = _modalFooter.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_modalFooter.GetChild(i).gameObject);
            _selectedIndices.Clear();
            _confirmBtn = null;
            _confirmBtnText = null;
            _counterAlloc = null;
            _sortOrder = null;
        }

        private void BuildPromptModalShell(Transform parent)
        {
            var dim = LotdUiKit.FillRect("Modal_DuelPrompt", parent);
            ModalRoot = dim.gameObject;
            LotdUiKit.AddImage(dim.gameObject, LotdUiKit.WhiteSprite, new Color(0f, 0.02f, 0.07f, 0.52f));

            _modalBox = LotdUiKit.CreateMenuPanel(dim, "Win",
                new Vector2(0.225f, 0.12f), new Vector2(0.978f, 0.88f));
            _modalBox.gameObject.AddComponent<Button>(); // block click-through to dim

            var hdr = LotdUiKit.CreateRect("Hdr", _modalBox, new Vector2(0f, 0.87f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(hdr.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelHeader);
            _modalTitle = LotdUiKit.AddText(hdr, "Title", "", 21, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);
            _modalTitle.rectTransform.offsetMin = new Vector2(18, 20);
            _modalSubtitle = LotdUiKit.AddText(hdr, "Sub", "", 14, TextAnchor.LowerLeft, LotdUiKit.TextPrimary);
            _modalSubtitle.rectTransform.offsetMin = new Vector2(18, 4);

            _modalBody = LotdUiKit.CreateRect("Body", _modalBox, new Vector2(0.02f, 0.14f), new Vector2(0.98f, 0.85f), Vector2.zero, Vector2.zero);
            _modalFooter = LotdUiKit.CreateRect("Footer", _modalBox, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.12f), Vector2.zero, Vector2.zero);

            var closeRt = LotdUiKit.CreateRect("CloseBtn", hdr, new Vector2(0.82f, 0.16f), new Vector2(0.98f, 0.84f), Vector2.zero, Vector2.zero);
            _modalCloseBtn = LotdUiKit.CreateLotdButton(closeRt, "B", "✕ 취소 (PASS)", OnModalCloseClicked, 13, gold: false);
            dim.gameObject.AddComponent<Button>().onClick.AddListener(OnModalCloseClicked);

            ModalRoot.SetActive(false);
        }

        public bool TryCancelOrPass()
        {
            if (ModalRoot == null || !ModalRoot.activeSelf || _curPrompt == null) return false;

            if (_curPrompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
            {
                if (!_curPrompt.forced)
                {
                    LotdAudio.PlayClick();
                    Submit(BitConverter.GetBytes(-1));
                    return true;
                }
            }
            else if (_curPrompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN || _curPrompt.messageType == YgoConstants.MSG_SELECT_YESNO)
            {
                LotdAudio.PlayClick();
                Submit(BitConverter.GetBytes(0)); // NO
                return true;
            }
            else if ((_curPrompt.messageType == YgoConstants.MSG_SELECT_CARD || _curPrompt.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD) && _curPrompt.cancelable)
            {
                LotdAudio.PlayClick();
                Submit(BitConverter.GetBytes(-1));
                return true;
            }

            return false;
        }

        public bool TryConfirm()
        {
            if (ModalRoot == null || !ModalRoot.activeSelf || _curPrompt == null) return false;

            if (_curPrompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN || _curPrompt.messageType == YgoConstants.MSG_SELECT_YESNO)
            {
                LotdAudio.PlayClick();
                Submit(BitConverter.GetBytes(1)); // YES
                return true;
            }
            if ((_curPrompt.messageType == YgoConstants.MSG_SELECT_CARD ||
                 _curPrompt.messageType == YgoConstants.MSG_SELECT_TRIBUTE ||
                 _curPrompt.messageType == YgoConstants.MSG_SELECT_SUM ||
                 _curPrompt.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD) &&
                _confirmBtn != null && _confirmBtn.interactable)
            {
                LotdAudio.PlayClick();
                OnConfirmMultiSelect();
                return true;
            }
            return false;
        }

        public void TriggerRematch()
        {
            if (ResultRoot == null || !ResultRoot.activeSelf) return;
            LotdAudio.PlayClick();
            HideResult();
            _resRematch?.Invoke();
        }

        public void TriggerMenu()
        {
            if (ResultRoot == null || !ResultRoot.activeSelf) return;
            LotdAudio.PlayClick();
            HideResult();
            _resMenu?.Invoke();
        }

        private void OnModalCloseClicked()
        {
            TryCancelOrPass();
        }

        public void ShowPrompt(DuelSession session, DuelPrompt prompt)
        {
            _curSession = session;
            _curPrompt = prompt;
            ClearBodyAndFooter();
            ModalRoot.transform.SetAsLastSibling();
            ModalRoot.SetActive(true);
            CardAnimationManager.Instance?.BringToFront();

            bool canCancel = false;
            if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN) canCancel = !prompt.forced;
            else if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN || prompt.messageType == YgoConstants.MSG_SELECT_YESNO) canCancel = true;
            else if ((prompt.messageType == YgoConstants.MSG_SELECT_CARD || prompt.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD) && prompt.cancelable) canCancel = true;

            if (_modalCloseBtn != null)
                _modalCloseBtn.gameObject.SetActive(canCancel);

            switch (prompt.messageType)
            {
                case YgoConstants.MSG_SELECT_EFFECTYN:
                    RenderEffectYesNo(prompt);
                    break;
                case YgoConstants.MSG_SELECT_YESNO:
                    RenderYesNo(session, prompt);
                    break;
                case YgoConstants.MSG_SELECT_POSITION:
                    RenderSelectPosition(prompt);
                    break;
                case YgoConstants.MSG_SELECT_OPTION:
                case YgoConstants.MSG_ANNOUNCE_NUMBER:
                    RenderOptionList(prompt);
                    break;
                case YgoConstants.MSG_SELECT_CHAIN:
                    RenderSelectChain(session, prompt);
                    break;
                case YgoConstants.MSG_SELECT_CARD:
                case YgoConstants.MSG_SELECT_TRIBUTE:
                case YgoConstants.MSG_SELECT_SUM:
                    RenderCardMultiSelect(session, prompt);
                    break;
                case YgoConstants.MSG_SELECT_UNSELECT_CARD:
                    RenderUnselectCard(session, prompt);
                    break;
                case YgoConstants.MSG_SELECT_COUNTER:
                    RenderSelectCounter(prompt);
                    break;
                case YgoConstants.MSG_SORT_CARD:
                case YgoConstants.MSG_SORT_CHAIN:
                    RenderSortCards(prompt);
                    break;
                case YgoConstants.MSG_ANNOUNCE_RACE:
                    RenderAnnounceRace(prompt);
                    break;
                case YgoConstants.MSG_ANNOUNCE_ATTRIB:
                    RenderAnnounceAttrib(prompt);
                    break;
                case YgoConstants.MSG_ANNOUNCE_CARD:
                    RenderAnnounceCard(prompt);
                    break;
                case YgoConstants.MSG_ROCK_PAPER_SCISSORS:
                    RenderRockPaperScissors();
                    break;
                default:
                    ModalRoot.SetActive(false);
                    break;
            }
        }

        private void Submit(byte[] bytes)
        {
            ModalRoot.SetActive(false);
            _onSubmitResponse?.Invoke(bytes);
        }

        private void RenderEffectYesNo(DuelPrompt p)
        {
            var info = CardDatabase.Get(p.promptCardCode);
            string cardName = info != null ? info.name : $"카드 #{p.promptCardCode}";
            string eff = CardDatabase.ResolveDescription(p.promptDesc, p.promptCardCode);
            if (string.IsNullOrEmpty(eff)) eff = "이 카드의 효과를 발동하시겠습니까?";

            _modalTitle.text = "유발 효과 발동 확인 — [임의 효과 (선택 발동)]";
            _modalSubtitle.text = $"{cardName}  [{YgoConstants.GetLocationNameKR(p.promptLoc)}] — 효과를 발동할지 선택하세요 (취소/패스 가능)";

            var cardRt = LotdUiKit.CreateRect("Card", _modalBody, new Vector2(0.06f, 0.10f), new Vector2(0.32f, 0.92f), Vector2.zero, Vector2.zero);
            var view = CompositeCardView.Create(cardRt, "Preview", false);
            view.SetCard(p.promptCardCode);
            _onInspectCard?.Invoke(p.promptCardCode);

            var descBox = LotdUiKit.CreateBorderedPanel(_modalBody, "Desc", new Vector2(0.36f, 0.14f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero);
            var descTxt = LotdUiKit.AddText(descBox, "T", $"{cardName}\n\n{eff}\n\n{(info != null ? info.desc : "")}",
                16, TextAnchor.UpperLeft, LotdUiKit.TextPrimary);
            descTxt.rectTransform.offsetMin = new Vector2(14, 14);
            descTxt.rectTransform.offsetMax = new Vector2(-14, -14);

            AddFooterButton(0.18f, 0.48f, "발동한다 (YES)", () => Submit(BitConverter.GetBytes(1)), true);
            AddFooterButton(0.52f, 0.82f, "발동하지 않음 (NO)", () => Submit(BitConverter.GetBytes(0)), false);
        }

        private void RenderYesNo(DuelSession s, DuelPrompt p)
        {
            string msg = CardDatabase.ResolveDescription(p.promptDesc, 0);
            if (string.IsNullOrEmpty(msg)) msg = CardDatabase.ResolveDescription(s.LastHintMsg, 0);
            if (string.IsNullOrEmpty(msg)) msg = "선택한 처리를 실행하시겠습니까?";

            _modalTitle.text = "확인 선택 (YES / NO)";
            _modalSubtitle.text = "질문에 대한 예/아니오를 선택하세요.";

            var box = LotdUiKit.CreateBorderedPanel(_modalBody, "Msg", new Vector2(0.10f, 0.25f), new Vector2(0.90f, 0.78f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddText(box, "T", msg, 22, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);

            AddFooterButton(0.18f, 0.48f, "예 (YES)", () => Submit(BitConverter.GetBytes(1)), true);
            AddFooterButton(0.52f, 0.82f, "아니오 (NO)", () => Submit(BitConverter.GetBytes(0)), false);
        }

        private void RenderSelectPosition(DuelPrompt p)
        {
            string cname = CardDatabase.GetName(p.promptCardCode);
            _modalTitle.text = "표시 형식 선택";
            _modalSubtitle.text = $"{cname} — 필드에 놓을 표시 형식을 선택하세요.";
            _onInspectCard?.Invoke(p.promptCardCode);

            var choices = new List<(uint pos, string label, string sprite, bool rot, bool fd)>();
            if ((p.allowedPositions & YgoConstants.POS_FACEUP_ATTACK) != 0)
                choices.Add((YgoConstants.POS_FACEUP_ATTACK, "앞면 공격 표시", "ICON_ID_CMDID_48_TURN_ATK", false, false));
            if ((p.allowedPositions & YgoConstants.POS_FACEUP_DEFENSE) != 0)
                choices.Add((YgoConstants.POS_FACEUP_DEFENSE, "앞면 수비 표시", "ICON_ID_CMDID_48_TURN_DEF", true, false));
            if ((p.allowedPositions & YgoConstants.POS_FACEDOWN_DEFENSE) != 0)
                choices.Add((YgoConstants.POS_FACEDOWN_DEFENSE, "뒷면 수비 표시", "ICON_ID_CMDID_48_SETMONST", true, true));
            if ((p.allowedPositions & YgoConstants.POS_FACEDOWN_ATTACK) != 0)
                choices.Add((YgoConstants.POS_FACEDOWN_ATTACK, "뒷면 공격 표시", "ICON_ID_CMDID_48_SETMONST", false, true));

            if (choices.Count == 0)
                choices.Add((YgoConstants.POS_FACEUP_ATTACK, "앞면 공격 표시", "ICON_ID_CMDID_48_TURN_ATK", false, false));

            float w = 1f / choices.Count;
            for (int i = 0; i < choices.Count; i++)
            {
                var ch = choices[i];
                var slot = LotdUiKit.CreateBorderedPanel(_modalBody, $"Pos{i}",
                    new Vector2(i * w + 0.02f, 0.12f), new Vector2((i + 1) * w - 0.02f, 0.90f), Vector2.zero, Vector2.zero);

                var cardHolder = LotdUiKit.CreateRect("C", slot, new Vector2(0.18f, 0.30f), new Vector2(0.82f, 0.88f), Vector2.zero, Vector2.zero);
                if (ch.rot) cardHolder.localRotation = Quaternion.Euler(0, 0, -90f);
                var cv = CompositeCardView.Create(cardHolder, "V", false);
                cv.SetCard(p.promptCardCode, faceDown: ch.fd);

                var bRt = LotdUiKit.CreateRect("Btn", slot, new Vector2(0.08f, 0.05f), new Vector2(0.92f, 0.22f), Vector2.zero, Vector2.zero);
                LotdUiKit.CreateLotdButton(bRt, "B", ch.label, () => Submit(BitConverter.GetBytes((int)ch.pos)), 16, gold: ch.pos == YgoConstants.POS_FACEUP_ATTACK);
            }
        }

        private void RenderOptionList(DuelPrompt p)
        {
            bool isNum = p.messageType == YgoConstants.MSG_ANNOUNCE_NUMBER;
            _modalTitle.text = isNum ? "숫자 선언" : "효과 / 항목 선택";
            _modalSubtitle.text = "실행할 항목을 선택하세요.";

            var scroll = CreateVerticalScroll(_modalBody, out Transform content);
            for (int i = 0; i < p.options.Count; i++)
            {
                int pick = i;
                ulong opt = p.options[i];
                string text = isNum ? $"선언 숫자: {opt}" : CardDatabase.ResolveDescription(opt, p.promptCardCode);
                if (string.IsNullOrEmpty(text)) text = $"선택지 #{i + 1} ({opt})";

                var row = new GameObject($"Opt_{i}", typeof(RectTransform), typeof(LayoutElement));
                row.transform.SetParent(content, false);
                row.GetComponent<LayoutElement>().preferredHeight = 56;
                LotdUiKit.CreateLotdButton(row.transform, "B", text, () => Submit(BitConverter.GetBytes(pick)), 17, gold: i == 0);
            }
        }

        private void RenderSelectChain(DuelSession s, DuelPrompt p)
        {
            int nextLink = s.CurrentChain.Count + 1;
            if (p.forced)
            {
                _modalTitle.text = $"⚡ 강제 유발 효과 체인 순서 선택 (체인 {nextLink} / 필수 발동)";
                _modalSubtitle.text = $"강제 발동 효과입니다 (취소 불가) — 체인 블록 {nextLink} (Chain Link {nextLink})로 먼저 올릴 카드를 선택하세요.";
            }
            else if (p.IsTriggerChain)
            {
                _modalTitle.text = $"✨ 임의 유발 효과 발동 / 순서 선택 (체인 {nextLink} / 선택 발동)";
                _modalSubtitle.text = $"체인 블록 {nextLink} (Chain Link {nextLink})로 발동할 효과를 순서대로 선택하거나, 발동하지 않으려면 아래 취소(PASS)를 누르세요.";
            }
            else
            {
                _modalTitle.text = $"체인 / 즉시 효과 발동 확인 (체인 {nextLink} / 선택 발동)";
                _modalSubtitle.text = s.CurrentChain.Count > 0
                    ? $"현재 체인 블록 {s.CurrentChain.Count} — 체인 {nextLink}(으)로 발동할 카드를 선택하거나 패스하세요."
                    : "발동 가능한 카드 효과가 있습니다 (발동 여부 선택 가능).";
            }

            string badgeTag = p.forced
                ? "<color=#FFAA33>[강제 효과 — 필수]</color>"
                : (p.IsTriggerChain ? "<color=#66EEBB>[임의 유발 — 선택 가능]</color>" : "<color=#88CCFF>[임의 효과 — 선택 가능]</color>");
            string btnLabel = p.forced ? $"체인 {nextLink} 등록" : $"체인 {nextLink} 발동";

            CreateVerticalScroll(_modalBody, out Transform content);
            for (int i = 0; i < p.candidates.Count; i++)
            {
                int pick = i;
                var c = p.candidates[i];
                string cname = CardDatabase.GetName(c.code);
                string desc = CardDatabase.ResolveDescription(c.description, c.code);
                if (string.IsNullOrEmpty(desc)) desc = "카드 효과 발동";
                string locTag = $"{(c.controller == s.HumanPlayerIndex ? "내" : "상대")} {YgoConstants.GetLocationNameKR(c.location)}";

                var row = LotdUiKit.CreateBorderedPanel(content, $"Ch_{i}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 96;

                var thumbRt = LotdUiKit.CreateRect("Card", row, new Vector2(0.01f, 0.06f), new Vector2(0.11f, 0.94f), Vector2.zero, Vector2.zero);
                var cv = CompositeCardView.Create(thumbRt, "V", false);
                cv.SetCard(c.code);

                var txtRt = LotdUiKit.CreateRect("Txt", row, new Vector2(0.13f, 0.08f), new Vector2(0.75f, 0.92f), Vector2.zero, Vector2.zero);
                LotdUiKit.AddText(txtRt, "T", $"{cname}  [{locTag}]  {badgeTag}\n<color=#88CCFF>{desc}</color>", 16, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);

                var actRt = LotdUiKit.CreateRect("Act", row, new Vector2(0.77f, 0.18f), new Vector2(0.98f, 0.82f), Vector2.zero, Vector2.zero);
                LotdUiKit.CreateLotdButton(actRt, "B", btnLabel, () => Submit(BitConverter.GetBytes(pick)), 16, gold: true);

                var insRt = LotdUiKit.CreateRect("Ins", row, new Vector2(0.01f, 0.06f), new Vector2(0.11f, 0.94f), Vector2.zero, Vector2.zero);
                var insImg = LotdUiKit.AddImage(insRt.gameObject, LotdUiKit.WhiteSprite, Color.clear);
                var insBtn = insRt.gameObject.AddComponent<Button>();
                insBtn.targetGraphic = insImg;
                insBtn.onClick.AddListener(() => _onInspectCard?.Invoke(c.code));
                var insTrig = insRt.gameObject.AddComponent<CardDragTrigger>();
                insTrig.OnPointerEnterAction = () => _onInspectCard?.Invoke(c.code);
            }

            if (!p.forced)
            {
                string passLabel = p.IsTriggerChain
                    ? "❌ 효과 발동 안 함 (남은 임의 효과 패스 / PASS)"
                    : "❌ 효과 발동 취소 (발동하지 않고 패스 / PASS)";
                AddFooterButton(0.24f, 0.76f, passLabel, () => Submit(BitConverter.GetBytes(-1)), false);
            }
            else
            {
                var infoRt = LotdUiKit.FillRect("ForcedInfo", _modalFooter);
                LotdUiKit.AddText(infoRt, "T", "⚠️ 강제 유발 효과는 반드시 발동되며, 체인에 올릴 순서만 선택합니다.", 16, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);
            }
        }

        private void RenderCardMultiSelect(DuelSession s, DuelPrompt p)
        {
            string hint = CardDatabase.ResolveDescription(s.LastHintMsg, 0);
            if (p.messageType == YgoConstants.MSG_SELECT_TRIBUTE)
                _modalTitle.text = !string.IsNullOrEmpty(hint) ? hint : "릴리스할 몬스터 선택";
            else if (p.messageType == YgoConstants.MSG_SELECT_SUM)
                _modalTitle.text = !string.IsNullOrEmpty(hint) ? hint : $"소재/합계 선택 (목표 합계: {p.acc & 0xFFFF})";
            else
                _modalTitle.text = !string.IsNullOrEmpty(hint) ? hint : "대상 카드 선택";

            CreateHorizontalCardGrid(_modalBody, out Transform grid);

            // Show locked must-select cards for SELECT_SUM
            for (int m = 0; m < p.mustSelect.Count; m++)
            {
                var item = p.mustSelect[m];
                AddMultiSelectCardTile(grid, item, -1 - m, lockedMust: true, p, s);
            }

            for (int i = 0; i < p.candidates.Count; i++)
            {
                AddMultiSelectCardTile(grid, p.candidates[i], i, lockedMust: false, p, s);
            }

            _confirmBtn = AddFooterButton(0.55f, 0.88f, "선택 완료", OnConfirmMultiSelect, true);
            _confirmBtnText = _confirmBtn.GetComponentInChildren<Text>();
            if (p.cancelable)
                AddFooterButton(0.12f, 0.45f, "취소 / 선택 안 함", () => Submit(BitConverter.GetBytes(-1)), false);

            UpdateMultiSelectState();
        }

        private void AddMultiSelectCardTile(Transform grid, SelectableCardItem item, int idx, bool lockedMust, DuelPrompt p, DuelSession s)
        {
            var tile = LotdUiKit.CreateBorderedPanel(grid, $"Tile_{idx}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var cardRt = LotdUiKit.CreateRect("Card", tile, new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.96f), Vector2.zero, Vector2.zero);
            var cv = CompositeCardView.Create(cardRt, "V", false);
            uint code = s.GetVisibleSelectionCode(item);
            cv.SetCard(code, faceDown: code == 0);

            string owner = item.controller == s.HumanPlayerIndex ? "내" : "상대";
            string sub = $"{owner} {YgoConstants.GetLocationNameKR(item.location)}";
            if (lockedMust) sub = "[필수] " + sub;
            else if (p.messageType == YgoConstants.MSG_SELECT_TRIBUTE && item.releaseParam > 1) sub += $" (릴리스{item.releaseParam})";
            else if (p.messageType == YgoConstants.MSG_SELECT_SUM)
            {
                uint v1 = item.sumParam & 0xFFFF;
                uint v2 = (item.sumParam >> 16) & 0xFFFF;
                sub += v2 > 0 ? $" ({v1}/{v2})" : $" ({v1})";
            }

            var lbl = LotdUiKit.AddText(LotdUiKit.CreateRect("Sub", tile, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.21f), Vector2.zero, Vector2.zero),
                "S", $"{(code != 0 ? CardDatabase.GetName(code) : "뒷면 카드")}\n<color=#88CCFF>{sub}</color>",
                11, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary);

            var checkRt = LotdUiKit.CreateRect("Chk", tile, new Vector2(0.62f, 0.72f), new Vector2(0.95f, 0.95f), Vector2.zero, Vector2.zero);
            var chkImg = LotdUiKit.AddImage(checkRt.gameObject, LotdUiKit.LoadSprite("LOTD/Icons/ICON_ID_CHECKMARK"), LotdUiKit.GoldAccent, true, false);
            chkImg.gameObject.SetActive(lockedMust);

            if (!lockedMust)
            {
                var ol = tile.GetComponent<Outline>();
                var btn = tile.gameObject.AddComponent<Button>();
                btn.targetGraphic = tile.GetComponent<Image>();
                var trig = tile.gameObject.AddComponent<CardDragTrigger>();
                if (code != 0) trig.OnPointerEnterAction = () => _onInspectCard?.Invoke(code);
                btn.onClick.AddListener(() =>
                {
                    if (code != 0) _onInspectCard?.Invoke(code);
                    if (_selectedIndices.Contains(idx))
                    {
                        if (p.max == 1 && p.min == 1)
                        {
                            LotdAudio.PlayClick();
                            OnConfirmMultiSelect();
                            return;
                        }
                        _selectedIndices.Remove(idx);
                        chkImg.gameObject.SetActive(false);
                        ol.effectColor = LotdUiKit.CyanAccent;
                    }
                    else
                    {
                        if (p.max == 1)
                        {
                            // Single-select auto-replaces previous pick for smooth UX
                            _selectedIndices.Clear();
                            for (int c = 0; c < grid.childCount; c++)
                            {
                                var child = grid.GetChild(c);
                                var ch = child.Find("Chk");
                                if (ch != null && !child.name.StartsWith("Tile_-")) ch.gameObject.SetActive(false);
                                var col = child.GetComponent<Outline>();
                                if (col != null) col.effectColor = LotdUiKit.CyanAccent;
                            }
                        }
                        _selectedIndices.Add(idx);
                        chkImg.gameObject.SetActive(true);
                        ol.effectColor = LotdUiKit.GoldAccent;
                    }
                    UpdateMultiSelectState();
                });
            }
        }

        private void UpdateMultiSelectState()
        {
            if (_curPrompt == null || _confirmBtn == null) return;
            bool valid = false;

            if (_curPrompt.messageType == YgoConstants.MSG_SELECT_CARD)
            {
                int cnt = _selectedIndices.Count;
                valid = cnt >= _curPrompt.min && cnt <= _curPrompt.max;
                _modalSubtitle.text = $"필요 선택 매수: {_curPrompt.min}~{_curPrompt.max}장  (현재 {cnt}장 선택)";
            }
            else if (_curPrompt.messageType == YgoConstants.MSG_SELECT_TRIBUTE)
            {
                int sumRel = _selectedIndices.Sum(i => Math.Max(1, (int)_curPrompt.candidates[i].releaseParam));
                valid = sumRel >= _curPrompt.min && _selectedIndices.Count <= _curPrompt.max;
                _modalSubtitle.text = $"필요 릴리스 수: {_curPrompt.min} (최대 {_curPrompt.max}장)  |  현재 릴리스 합계: {sumRel}";
            }
            else if (_curPrompt.messageType == YgoConstants.MSG_SELECT_SUM)
            {
                int target = (int)(_curPrompt.acc & 0xFFFF);
                valid = CheckSumSelectionValid(_curPrompt, _selectedIndices, target);
                _modalSubtitle.text = $"목표 합계: {target}  (필수 {_curPrompt.mustSelect.Count}장 + 선택 {_selectedIndices.Count}장)";
            }

            _confirmBtn.interactable = valid;
            if (_confirmBtnText != null)
                _confirmBtnText.text = valid ? $"선택 완료 ({_selectedIndices.Count}장)" : "조건에 맞게 선택하세요";
        }

        private void OnConfirmMultiSelect()
        {
            var sorted = _selectedIndices.OrderBy(i => i).ToList();
            using (var ms = new MemoryStream(8 + sorted.Count * 4))
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(0);
                bw.Write(sorted.Count);
                foreach (int idx in sorted) bw.Write(idx);
                Submit(ms.ToArray());
            }
        }

        public static bool CheckSumSelectionValid(DuelPrompt p, HashSet<int> chosen, int target)
        {
            if (p == null || chosen == null) return false;

            var items = new List<(int a, int b)>();
            foreach (var m in p.mustSelect)
                items.Add(((int)(m.sumParam & 0xFFFF), (int)((m.sumParam >> 16) & 0xFFFF)));
            foreach (int i in chosen)
            {
                if (i < 0 || i >= p.candidates.Count) return false;
                var c = p.candidates[i];
                items.Add(((int)(c.sumParam & 0xFFFF), (int)((c.sumParam >> 16) & 0xFFFF)));
            }
            if (items.Count == 0) return false;

            if (p.sumExactCountZeroAny)
            {
                int sum = 0;
                int mx = 0;
                int mn = int.MaxValue;
                foreach (var it in items)
                {
                    int o1 = it.a;
                    int o2 = it.b;
                    int ms = (o2 > 0 && o2 < o1) ? o2 : o1;
                    sum += ms;
                    mx += Math.Max(o1, o2);
                    if (ms < mn) mn = ms;
                }
                return mx >= target && (sum - mn) < target;
            }

            // ygopro-core checks tot = return_cards.size() (excluding must_select_cards)
            int cnt = chosen.Count;
            if (cnt < p.min || (p.max > 0 && cnt > p.max))
                return false;

            bool Dfs(int k, int acc)
            {
                if (k == items.Count) return acc == target;
                int a = items[k].a;
                if (Dfs(k + 1, acc + a)) return true;
                int b = items[k].b;
                return b > 0 && b != a && Dfs(k + 1, acc + b);
            }
            return Dfs(0, 0);
        }

        private void RenderUnselectCard(DuelSession s, DuelPrompt p)
        {
            _modalTitle.text = "소재 / 대상 선택 및 해제";
            _modalSubtitle.text = $"카드를 클릭하여 선택({p.candidates.Count}) 또는 해제({p.unselectCandidates.Count})하세요.";
            CreateHorizontalCardGrid(_modalBody, out Transform grid);

            for (int i = 0; i < p.candidates.Count; i++)
            {
                int sendIdx = i;
                uint code = s.GetVisibleSelectionCode(p.candidates[i]);
                AddImmediateClickTile(grid, code, "선택", false, () =>
                {
                    if (code != 0) _onInspectCard?.Invoke(code);
                    Submit(PackTwoInts(1, sendIdx));
                });
            }
            for (int j = 0; j < p.unselectCandidates.Count; j++)
            {
                int sendIdx = p.candidates.Count + j;
                uint code = s.GetVisibleSelectionCode(p.unselectCandidates[j]);
                AddImmediateClickTile(grid, code, "선택됨 (클릭해제)", true, () =>
                {
                    if (code != 0) _onInspectCard?.Invoke(code);
                    Submit(PackTwoInts(1, sendIdx));
                });
            }

            if (p.finishable)
                AddFooterButton(0.54f, 0.88f, "선택 완료", () => Submit(BitConverter.GetBytes(-1)), true);
            if (p.cancelable)
                AddFooterButton(0.12f, 0.46f, "취소", () => Submit(BitConverter.GetBytes(-1)), false);
        }

        private void AddImmediateClickTile(Transform grid, uint code, string tag, bool checkedState, Action onClick)
        {
            var tile = LotdUiKit.CreateBorderedPanel(grid, "Imm", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                LotdUiKit.PanelNavy, checkedState ? LotdUiKit.GoldAccent : LotdUiKit.CyanAccent);
            var cRt = LotdUiKit.CreateRect("Card", tile, new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.96f), Vector2.zero, Vector2.zero);
            CompositeCardView.Create(cRt, "V", false).SetCard(code, faceDown: code == 0);
            LotdUiKit.AddText(LotdUiKit.CreateRect("Sub", tile, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.21f), Vector2.zero, Vector2.zero),
                "S", $"{(code != 0 ? CardDatabase.GetName(code) : "뒷면 카드")}\n<color=#FFCC44>{tag}</color>",
                11, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary);
            var btn = tile.gameObject.AddComponent<Button>();
            btn.targetGraphic = tile.GetComponent<Image>();
            btn.onClick.AddListener(() => onClick?.Invoke());
        }

        private void RenderSelectCounter(DuelPrompt p)
        {
            _modalTitle.text = "카운터 제거 선택";
            _counterAlloc = new int[p.candidates.Count];

            CreateHorizontalCardGrid(_modalBody, out Transform grid);
            var countLabels = new Text[p.candidates.Count];

            for (int i = 0; i < p.candidates.Count; i++)
            {
                int idx = i;
                var c = p.candidates[i];
                var tile = LotdUiKit.CreateBorderedPanel(grid, $"Cnt_{i}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var cRt = LotdUiKit.CreateRect("Card", tile, new Vector2(0.10f, 0.30f), new Vector2(0.90f, 0.95f), Vector2.zero, Vector2.zero);
                CompositeCardView.Create(cRt, "V", false).SetCard(c.code);

                countLabels[i] = LotdUiKit.AddText(LotdUiKit.CreateRect("Lbl", tile, new Vector2(0.05f, 0.16f), new Vector2(0.95f, 0.30f), Vector2.zero, Vector2.zero),
                    "L", $"0 / {c.currentCounter}개", 14, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

                var minus = LotdUiKit.CreateRect("M", tile, new Vector2(0.10f, 0.03f), new Vector2(0.46f, 0.15f), Vector2.zero, Vector2.zero);
                LotdUiKit.CreateLotdButton(minus, "B", "-", () =>
                {
                    if (_counterAlloc[idx] > 0) _counterAlloc[idx]--;
                    countLabels[idx].text = $"{_counterAlloc[idx]} / {c.currentCounter}개";
                    UpdateCounterConfirm();
                }, 16);
                var plus = LotdUiKit.CreateRect("P", tile, new Vector2(0.54f, 0.03f), new Vector2(0.90f, 0.15f), Vector2.zero, Vector2.zero);
                LotdUiKit.CreateLotdButton(plus, "B", "+", () =>
                {
                    int curSum = _counterAlloc.Sum();
                    if (_counterAlloc[idx] < c.currentCounter && curSum < p.min) _counterAlloc[idx]++;
                    countLabels[idx].text = $"{_counterAlloc[idx]} / {c.currentCounter}개";
                    UpdateCounterConfirm();
                }, 16);
            }

            _confirmBtn = AddFooterButton(0.32f, 0.68f, "확정", () =>
            {
                byte[] buf = new byte[_counterAlloc.Length * 2];
                for (int k = 0; k < _counterAlloc.Length; k++)
                {
                    byte[] sb = BitConverter.GetBytes((short)_counterAlloc[k]);
                    buf[k * 2] = sb[0];
                    buf[k * 2 + 1] = sb[1];
                }
                Submit(buf);
            }, true);
            _confirmBtnText = _confirmBtn.GetComponentInChildren<Text>();
            UpdateCounterConfirm();
        }

        private void UpdateCounterConfirm()
        {
            if (_counterAlloc == null || _curPrompt == null) return;
            int sum = _counterAlloc.Sum();
            _modalSubtitle.text = $"제거할 총 카운터 수: {sum} / {_curPrompt.min}";
            _confirmBtn.interactable = sum == _curPrompt.min;
        }

        private void RenderSortCards(DuelPrompt p)
        {
            _modalTitle.text = p.isChainSort ? "효과 처리 순서 지정" : "덱/카드 배치 순서 지정";
            _modalSubtitle.text = "카드의 순서를 조정하거나 기본 순서로 확정하세요.";
            _sortOrder = Enumerable.Range(0, p.candidates.Count).ToArray();

            CreateHorizontalCardGrid(_modalBody, out Transform grid);
            Action rebuildTiles = null;
            rebuildTiles = () =>
            {
                for (int i = grid.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(grid.GetChild(i).gameObject);

                for (int pos = 0; pos < _sortOrder.Length; pos++)
                {
                    int curPos = pos;
                    int origIdx = _sortOrder[pos];
                    var c = p.candidates[origIdx];
                    var tile = LotdUiKit.CreateBorderedPanel(grid, $"Sort_{pos}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    var cRt = LotdUiKit.CreateRect("Card", tile, new Vector2(0.10f, 0.25f), new Vector2(0.90f, 0.94f), Vector2.zero, Vector2.zero);
                    CompositeCardView.Create(cRt, "V", false).SetCard(c.code);
                    LotdUiKit.AddText(LotdUiKit.CreateRect("Ord", tile, new Vector2(0.05f, 0.14f), new Vector2(0.95f, 0.25f), Vector2.zero, Vector2.zero),
                        "O", $"순서 #{pos + 1}", 14, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

                    var swapRt = LotdUiKit.CreateRect("Swap", tile, new Vector2(0.10f, 0.03f), new Vector2(0.90f, 0.13f), Vector2.zero, Vector2.zero);
                    LotdUiKit.CreateLotdButton(swapRt, "B", curPos > 0 ? "◀ 앞으로" : "▶ 뒤로", () =>
                    {
                        int other = curPos > 0 ? curPos - 1 : Mathf.Min(curPos + 1, _sortOrder.Length - 1);
                        int tmp = _sortOrder[curPos];
                        _sortOrder[curPos] = _sortOrder[other];
                        _sortOrder[other] = tmp;
                        rebuildTiles();
                    }, 12);
                }
            };
            rebuildTiles();

            AddFooterButton(0.15f, 0.48f, "현재 순서 확정", () =>
            {
                byte[] res = new byte[_sortOrder.Length];
                for (int i = 0; i < _sortOrder.Length; i++) res[_sortOrder[i]] = (byte)i;
                Submit(res);
            }, true);
            AddFooterButton(0.52f, 0.85f, "기본 순서 (자동)", () => Submit(new byte[] { unchecked((byte)-1) }), false);
        }

        private void RenderAnnounceRace(DuelPrompt p)
        {
            _modalTitle.text = "종족 선언";
            _modalSubtitle.text = $"선언할 종족을 {p.min}개 선택하세요.";
            ulong chosenMask = 0;

            CreateHorizontalCardGrid(_modalBody, out Transform grid, 140, 88);
            var btns = new List<(ulong bit, Outline ol)>();

            foreach (var kv in YgoConstants.RaceNamesKR)
            {
                if ((p.announceMask & kv.Key) == 0) continue;
                ulong bit = kv.Key;
                var tile = LotdUiKit.CreateBorderedPanel(grid, $"Race_{bit}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var iconSp = LotdUiKit.GetRaceSprite(bit);
                if (iconSp != null)
                {
                    var ir = LotdUiKit.CreateRect("Ic", tile, new Vector2(0.32f, 0.42f), new Vector2(0.68f, 0.90f), Vector2.zero, Vector2.zero);
                    LotdUiKit.AddImage(ir.gameObject, iconSp, Color.white, true);
                }
                LotdUiKit.AddText(LotdUiKit.CreateRect("L", tile, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.42f), Vector2.zero, Vector2.zero),
                    "T", kv.Value, 14, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);

                var ol = tile.GetComponent<Outline>();
                var btn = tile.gameObject.AddComponent<Button>();
                btn.targetGraphic = tile.GetComponent<Image>();
                btn.onClick.AddListener(() =>
                {
                    if ((chosenMask & bit) != 0) chosenMask &= ~bit;
                    else
                    {
                        if (p.min == 1) chosenMask = bit;
                        else if (CountBits(chosenMask) < (int)p.min) chosenMask |= bit;
                    }
                    foreach (var pair in btns)
                        pair.ol.effectColor = (chosenMask & pair.bit) != 0 ? LotdUiKit.GoldAccent : LotdUiKit.CyanAccent;
                    if (_confirmBtn != null) _confirmBtn.interactable = CountBits(chosenMask) == (int)p.min;
                });
                btns.Add((bit, ol));
            }

            _confirmBtn = AddFooterButton(0.35f, 0.65f, "선언 확정", () => Submit(BitConverter.GetBytes(chosenMask)), true);
            _confirmBtn.interactable = false;
        }

        private void RenderAnnounceAttrib(DuelPrompt p)
        {
            _modalTitle.text = "속성 선언";
            _modalSubtitle.text = $"선언할 속성을 {p.min}개 선택하세요.";
            uint chosenMask = 0;

            CreateHorizontalCardGrid(_modalBody, out Transform grid, 145, 96);
            var btns = new List<(uint bit, Outline ol)>();

            foreach (var kv in YgoConstants.AttributeNamesKR)
            {
                if ((p.announceMask & kv.Key) == 0) continue;
                uint bit = kv.Key;
                var tile = LotdUiKit.CreateBorderedPanel(grid, $"Attr_{bit}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var sp = LotdUiKit.GetAttributeSprite(bit);
                if (sp != null)
                {
                    var ir = LotdUiKit.CreateRect("Ic", tile, new Vector2(0.30f, 0.42f), new Vector2(0.70f, 0.90f), Vector2.zero, Vector2.zero);
                    LotdUiKit.AddImage(ir.gameObject, sp, Color.white, true);
                }
                LotdUiKit.AddText(LotdUiKit.CreateRect("L", tile, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.42f), Vector2.zero, Vector2.zero),
                    "T", kv.Value, 15, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);

                var ol = tile.GetComponent<Outline>();
                var btn = tile.gameObject.AddComponent<Button>();
                btn.targetGraphic = tile.GetComponent<Image>();
                btn.onClick.AddListener(() =>
                {
                    if ((chosenMask & bit) != 0) chosenMask &= ~bit;
                    else
                    {
                        if (p.min == 1) chosenMask = bit;
                        else if (CountBits(chosenMask) < (int)p.min) chosenMask |= bit;
                    }
                    foreach (var pair in btns)
                        pair.ol.effectColor = (chosenMask & pair.bit) != 0 ? LotdUiKit.GoldAccent : LotdUiKit.CyanAccent;
                    if (_confirmBtn != null) _confirmBtn.interactable = CountBits(chosenMask) == (int)p.min;
                });
                btns.Add((bit, ol));
            }

            _confirmBtn = AddFooterButton(0.35f, 0.65f, "선언 확정", () => Submit(BitConverter.GetBytes(chosenMask)), true);
            _confirmBtn.interactable = false;
        }

        private void RenderRockPaperScissors()
        {
            _modalTitle.text = "가위바위보";
            _modalSubtitle.text = "낼 패를 선택하세요.";

            var choices = new (int val, string label, bool gold)[]
            {
                (1, "가위", false),
                (2, "바위", true),
                (3, "보", false),
            };
            float w = 1f / choices.Length;
            for (int i = 0; i < choices.Length; i++)
            {
                var ch = choices[i];
                var slot = LotdUiKit.CreateBorderedPanel(_modalBody, $"Rps_{ch.val}",
                    new Vector2(i * w + 0.03f, 0.22f), new Vector2((i + 1) * w - 0.03f, 0.78f), Vector2.zero, Vector2.zero);
                LotdUiKit.AddText(LotdUiKit.CreateRect("Lbl", slot, new Vector2(0.08f, 0.35f), new Vector2(0.92f, 0.85f), Vector2.zero, Vector2.zero),
                    "T", ch.label, 28, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);
                var bRt = LotdUiKit.CreateRect("Btn", slot, new Vector2(0.12f, 0.10f), new Vector2(0.88f, 0.32f), Vector2.zero, Vector2.zero);
                LotdUiKit.CreateLotdButton(bRt, "B", "선택", () => Submit(BitConverter.GetBytes(ch.val)), 16, gold: ch.gold);
            }
        }

        private void RenderAnnounceCard(DuelPrompt p)
        {
            _modalTitle.text = "카드명 선언";
            _modalSubtitle.text = "선언할 카드 이름을 검색하여 선택하세요.";

            uint[] validCodes = YgoNativeBridge.FilterDeclarableCards(p.options.ToArray(), 20000);
            if (validCodes.Length == 0) validCodes = new[] { 89631139u };
            uint pickedCode = validCodes[0];

            var inpRt = LotdUiKit.CreateRect("Search", _modalBody, new Vector2(0.02f, 0.87f), new Vector2(0.98f, 0.99f), Vector2.zero, Vector2.zero);
            var inp = LotdUiKit.CreateInputField(inpRt, "Inp", "선언할 카드명 입력 (한글)...", 16);

            var gridHolder = LotdUiKit.CreateRect("List", _modalBody, new Vector2(0.0f, 0.0f), new Vector2(1f, 0.84f), Vector2.zero, Vector2.zero);
            CreateHorizontalCardGrid(gridHolder, out Transform grid, 128, 178);

            Action<string> refresh = q =>
            {
                for (int i = grid.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(grid.GetChild(i).gameObject);

                string qLow = (q ?? "").Trim().ToLowerInvariant();
                int shown = 0;
                foreach (uint code in validCodes)
                {
                    var info = CardDatabase.Get(code);
                    string cname = info != null ? info.name : $"카드 #{code}";
                    if (!string.IsNullOrEmpty(qLow) && (cname == null || !cname.ToLowerInvariant().Contains(qLow)))
                        continue;

                    var tile = LotdUiKit.CreateBorderedPanel(grid, $"Dec_{code}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    var cRt = LotdUiKit.CreateRect("C", tile, new Vector2(0.10f, 0.22f), new Vector2(0.90f, 0.95f), Vector2.zero, Vector2.zero);
                    CompositeCardView.Create(cRt, "V", false).SetCard(code);
                    LotdUiKit.AddText(LotdUiKit.CreateRect("L", tile, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.20f), Vector2.zero, Vector2.zero),
                        "T", cname, 11, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary);

                    var btn = tile.gameObject.AddComponent<Button>();
                    btn.targetGraphic = tile.GetComponent<Image>();
                    var trig = tile.gameObject.AddComponent<CardDragTrigger>();
                    trig.OnPointerEnterAction = () => _onInspectCard?.Invoke(code);
                    btn.onClick.AddListener(() =>
                    {
                        pickedCode = code;
                        _onInspectCard?.Invoke(code);
                        _modalSubtitle.text = $"선택된 선언 카드: {cname}";
                    });
                    if (++shown >= 40) break;
                }
            };
            inp.onValueChanged.AddListener(v => refresh(v));
            refresh("");

            AddFooterButton(0.32f, 0.68f, "선언 확정", () => Submit(BitConverter.GetBytes(pickedCode)), true);
        }

        // ==================== ZONE VIEWER ====================
        private void BuildZoneViewer(Transform parent)
        {
            var dim = LotdUiKit.FillRect("Overlay_ZoneView", parent);
            ZoneViewRoot = dim.gameObject;
            LotdUiKit.AddImage(dim.gameObject, LotdUiKit.WhiteSprite, new Color(0f, 0.02f, 0.08f, 0.72f));
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => ZoneViewRoot.SetActive(false));

            var win = LotdUiKit.CreateMenuPanel(dim, "Win",
                new Vector2(0.225f, 0.12f), new Vector2(0.978f, 0.88f));
            win.gameObject.AddComponent<Button>(); // block click-through to dim

            var hdr = LotdUiKit.CreateRect("Hdr", win, new Vector2(0f, 0.88f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(hdr.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelHeader);
            _zoneTitle = LotdUiKit.AddText(hdr, "T", "존 확인", 20, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent, FontStyle.Bold);
            _zoneTitle.rectTransform.offsetMin = new Vector2(18, 0);

            var closeRt = LotdUiKit.CreateRect("Close", hdr, new Vector2(0.82f, 0.15f), new Vector2(0.98f, 0.85f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(closeRt, "B", "✕ 닫기", () => ZoneViewRoot.SetActive(false), 16);

            var body = LotdUiKit.CreateRect("Body", win, new Vector2(0.02f, 0.03f), new Vector2(0.98f, 0.86f), Vector2.zero, Vector2.zero);
            CreateHorizontalCardGrid(body, out Transform g);
            _zoneGrid = (RectTransform)g;

            ZoneViewRoot.SetActive(false);
        }

        public void ShowZoneViewer(
            string title,
            IReadOnlyList<LiveCardState> cards,
            bool hideFaceDown = false,
            Func<LiveCardState, List<(string label, int responseVal, bool gold)>> getCardActions = null,
            Action<int> onExecuteAction = null,
            Action<LiveCardState> onOpenCardActions = null)
        {
            _zoneTitle.text = $"{title}  {cards?.Count ?? 0}장";
            for (int i = _zoneGrid.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_zoneGrid.GetChild(i).gameObject);

            if (cards != null)
            {
                bool forwardOrder = title != null && title.Contains("엑스트라");
                if (forwardOrder)
                {
                    for (int i = 0; i < cards.Count; i++)
                    {
                        var c = cards[i];
                        if (c == null) continue;
                        var actions = getCardActions?.Invoke(c);
                        bool fd = hideFaceDown && !c.IsFaceUp && !c.isPublic && (actions == null || actions.Count == 0);
                        AddZoneCardTile(fd ? 0u : c.code, fd, c, actions, onExecuteAction, onOpenCardActions);
                    }
                }
                else
                {
                    for (int i = cards.Count - 1; i >= 0; i--)
                    {
                        var c = cards[i];
                        if (c == null) continue;
                        var actions = getCardActions?.Invoke(c);
                        bool fd = hideFaceDown && !c.IsFaceUp && !c.isPublic && (actions == null || actions.Count == 0);
                        AddZoneCardTile(fd ? 0u : c.code, fd, c, actions, onExecuteAction, onOpenCardActions);
                    }
                }
            }
            ZoneViewRoot.transform.SetAsLastSibling();
            ZoneViewRoot.SetActive(true);
            CardAnimationManager.Instance?.BringToFront();
        }

        public void ShowCodeListViewer(string title, IReadOnlyList<uint> codes)
        {
            _zoneTitle.text = $"{title} ({codes?.Count ?? 0}장)";
            for (int i = _zoneGrid.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_zoneGrid.GetChild(i).gameObject);

            if (codes != null)
            {
                foreach (uint code in codes)
                    AddZoneCardTile(code, code == 0, null);
            }
            ZoneViewRoot.transform.SetAsLastSibling();
            ZoneViewRoot.SetActive(true);
            CardAnimationManager.Instance?.BringToFront();
        }

        private void AddZoneCardTile(
            uint code,
            bool faceDown,
            LiveCardState live,
            List<(string label, int responseVal, bool gold)> actions = null,
            Action<int> onExecuteAction = null,
            Action<LiveCardState> onOpenCardActions = null)
        {
            bool hasActions = actions != null && actions.Count > 0;
            var tile = LotdUiKit.CreateBorderedPanel(_zoneGrid, $"Z_{code}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                LotdUiKit.PanelNavy, hasActions ? LotdUiKit.GoldAccent : LotdUiKit.CyanAccent);
            var cRt = LotdUiKit.CreateRect("C", tile,
                new Vector2(0.08f, hasActions ? 0.30f : 0.18f), new Vector2(0.92f, 0.96f), Vector2.zero, Vector2.zero);
            CompositeCardView.Create(cRt, "V", false).SetCard(code, faceDown, live);
            LotdUiKit.AddText(LotdUiKit.CreateRect("L", tile,
                new Vector2(0.04f, hasActions ? 0.17f : 0.01f), new Vector2(0.96f, hasActions ? 0.29f : 0.17f), Vector2.zero, Vector2.zero),
                "T", faceDown ? "뒷면" : CardDatabase.GetName(code), 11, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary);

            if (!faceDown && code != 0)
            {
                var btn = tile.gameObject.AddComponent<Button>();
                btn.targetGraphic = tile.GetComponent<Image>();
                btn.onClick.AddListener(() =>
                {
                    _onInspectCard?.Invoke(code);
                    if (hasActions)
                    {
                        if (actions.Count == 1 && onExecuteAction != null)
                        {
                            ZoneViewRoot.SetActive(false);
                            onExecuteAction(actions[0].responseVal);
                        }
                        else if (live != null && onOpenCardActions != null)
                        {
                            ZoneViewRoot.SetActive(false);
                            onOpenCardActions(live);
                        }
                    }
                });
                var trig = tile.gameObject.AddComponent<CardDragTrigger>();
                trig.OnPointerEnterAction = () => _onInspectCard?.Invoke(code);
                if (hasActions && live != null && live.location == YgoConstants.LOCATION_EXTRA)
                {
                    trig.SourceType = DragSourceType.ExtraDeck;
                    trig.CardCodeGetter = () => code;
                    trig.IndexGetter = () => (int)live.sequence;
                    trig.DataGetter = () => live;
                }
            }

            if (hasActions)
            {
                var actRt = LotdUiKit.CreateRect("Act", tile, new Vector2(0.06f, 0.02f), new Vector2(0.94f, 0.16f), Vector2.zero, Vector2.zero);
                if (actions.Count == 1 && onExecuteAction != null)
                {
                    int val = actions[0].responseVal;
                    string lbl = actions[0].label;
                    if (lbl.Length > 7 && lbl.Contains("발동")) lbl = "💥 효과 발동";
                    LotdUiKit.CreateLotdButton(actRt, "B", lbl, () =>
                    {
                        ZoneViewRoot.SetActive(false);
                        onExecuteAction(val);
                    }, 11, actions[0].gold);
                }
                else if (live != null && onOpenCardActions != null)
                {
                    LotdUiKit.CreateLotdButton(actRt, "B", $"행동 ({actions.Count})", () =>
                    {
                        ZoneViewRoot.SetActive(false);
                        onOpenCardActions(live);
                    }, 11, true);
                }
            }
        }

        // ==================== MATCH RESULT ====================
        private void BuildResultOverlay(Transform parent)
        {
            var dim = LotdUiKit.FillRect("Overlay_MatchResult", parent);
            ResultRoot = dim.gameObject;
            LotdUiKit.AddImage(dim.gameObject, LotdUiKit.WhiteSprite, new Color(0f, 0.02f, 0.08f, 0.84f));

            var win = LotdUiKit.CreateBorderedPanel(dim, "ResWin",
                new Vector2(0.20f, 0.15f), new Vector2(0.80f, 0.85f), Vector2.zero, Vector2.zero,
                LotdUiKit.PanelNavy, LotdUiKit.GoldAccent);

            _resOppBust = LotdUiKit.AddImage(LotdUiKit.CreateRect("Bust", win, new Vector2(0.06f, 0.25f), new Vector2(0.40f, 0.88f), Vector2.zero, Vector2.zero).gameObject,
                null, Color.white, true);
            _resIcon = LotdUiKit.AddImage(LotdUiKit.CreateRect("ResIcon", win, new Vector2(0.44f, 0.56f), new Vector2(0.94f, 0.90f), Vector2.zero, Vector2.zero).gameObject,
                null, Color.white, true);

            _resHeadline = LotdUiKit.AddText(LotdUiKit.CreateRect("HL", win, new Vector2(0.44f, 0.42f), new Vector2(0.94f, 0.56f), Vector2.zero, Vector2.zero),
                "T", "", 28, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);
            _resStats = LotdUiKit.AddText(LotdUiKit.CreateRect("St", win, new Vector2(0.44f, 0.22f), new Vector2(0.94f, 0.42f), Vector2.zero, Vector2.zero),
                "T", "", 17, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary);

            var r1 = LotdUiKit.CreateRect("B1", win, new Vector2(0.06f, 0.06f), new Vector2(0.35f, 0.18f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(r1, "B", "재대결", () => { HideResult(); _resRematch?.Invoke(); }, 18, gold: true);

            var r2 = LotdUiKit.CreateRect("B2", win, new Vector2(0.37f, 0.06f), new Vector2(0.66f, 0.18f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(r2, "B", "상대 선택", () => { HideResult(); _resOppSel?.Invoke(); }, 18);

            var r3 = LotdUiKit.CreateRect("B3", win, new Vector2(0.68f, 0.06f), new Vector2(0.94f, 0.18f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(r3, "B", "메인 메뉴", () => { HideResult(); _resMenu?.Invoke(); }, 18);

            ResultRoot.SetActive(false);
        }

        public void ShowResult(bool playerWon, bool isDraw, OpponentCharacter opp, int turns, int myLp, int oppLp,
            Action onRematch, Action onOppSelect, Action onMenu)
        {
            _resRematch = onRematch;
            _resOppSel = onOppSelect;
            _resMenu = onMenu;

            string iconPath = isDraw ? "LOTD/MatchResult/mr_icon_draw"
                : (playerWon ? "LOTD/MatchResult/mr_icon_win" : "LOTD/MatchResult/mr_icon_lose");
            _resIcon.sprite = LotdUiKit.LoadSprite(iconPath);
            _resHeadline.text = isDraw ? "DRAW (무승부)" : (playerWon ? "DUEL VICTORY! (승리)" : "DEFEAT... (패배)");
            _resHeadline.color = isDraw ? LotdUiKit.CyanAccent : (playerWon ? LotdUiKit.GoldAccent : LotdUiKit.RedAccent);

            if (playerWon)
            {
                LotdAudio.PlayWin();
            }
            else
            {
                LotdAudio.PlayDamage();
            }

            Sprite bust = opp != null && !string.IsNullOrEmpty(opp.portrait)
                ? LotdUiKit.LoadSprite($"LOTD/Busts/{opp.portrait}")
                : LotdUiKit.LoadSprite("LOTD/Busts/setokaiba_neutral");
            _resOppBust.sprite = bust;
            _resOppBust.gameObject.SetActive(bust != null);

            string oppName = opp != null ? opp.name : "AI 듀얼리스트";
            _resStats.text = $"대전 상대: {oppName}\n총 경과 턴: {turns}턴\n최종 라이프: 내 LP {myLp}  |  상대 LP {oppLp}";
            ResultRoot.transform.SetAsLastSibling();
            ResultRoot.SetActive(true);
            CardAnimationManager.Instance?.BringToFront();
            LotdAudio.PlayWin();
        }

        // ==================== UI HELPERS ====================
        private Button AddFooterButton(float xMin, float xMax, string label, Action onClick, bool gold)
        {
            var slot = LotdUiKit.CreateRect($"Btn_{label}", _modalFooter, new Vector2(xMin, 0.08f), new Vector2(xMax, 0.92f), Vector2.zero, Vector2.zero);
            return LotdUiKit.CreateLotdButton(slot, "B", label, onClick, 17, gold);
        }

        private static GameObject CreateVerticalScroll(Transform parent, out Transform content)
        {
            var svRt = LotdUiKit.FillRect("Scroll", parent);
            var sr = svRt.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 28f;

            var vp = LotdUiKit.FillRect("VP", svRt);
            LotdUiKit.AddImage(vp.gameObject, LotdUiKit.WhiteSprite, new Color(0, 0, 0, 0.1f));
            vp.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var cRt = LotdUiKit.CreateRect("Content", vp, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            cRt.pivot = new Vector2(0.5f, 1f);
            var vlg = cRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            cRt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vp;
            sr.content = cRt;
            content = cRt;
            return svRt.gameObject;
        }

        private static GameObject CreateHorizontalCardGrid(Transform parent, out Transform content, int cellW = 136, int cellH = 200)
        {
            var svRt = LotdUiKit.FillRect("GridScroll", parent);
            var sr = svRt.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 32f;

            var vp = LotdUiKit.FillRect("VP", svRt);
            LotdUiKit.AddImage(vp.gameObject, LotdUiKit.WhiteSprite, new Color(0, 0, 0, 0.1f));
            vp.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var cRt = LotdUiKit.CreateRect("Content", vp, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            cRt.pivot = new Vector2(0.5f, 1f);
            var glg = cRt.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(cellW, cellH);
            glg.spacing = new Vector2(10, 10);
            glg.padding = new RectOffset(10, 10, 10, 10);
            glg.constraint = GridLayoutGroup.Constraint.Flexible;
            cRt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vp;
            sr.content = cRt;
            content = cRt;
            return svRt.gameObject;
        }

        private static byte[] PackTwoInts(int a, int b)
        {
            byte[] buf = new byte[8];
            Buffer.BlockCopy(BitConverter.GetBytes(a), 0, buf, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(b), 0, buf, 4, 4);
            return buf;
        }

        private static int CountBits(ulong v)
        {
            int c = 0;
            while (v != 0) { v &= v - 1; c++; }
            return c;
        }
    }
}
