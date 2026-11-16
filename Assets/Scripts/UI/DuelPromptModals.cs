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
        private CanvasGroup _promptInput;
        private Image _promptShade;
        private Button _promptBackdrop;
        private RectTransform _zoneShortcuts;
        private const float ChainCardWidth = 78f;
        private const float ChainCardGap = 7f;
        private RectTransform _chainBox;
        private RectTransform _chainCards;
        private ScrollRect _chainCardScroll;
        private ScrollRect _chainEffectScroll;
        private Text _chainTitle;
        private Text _chainSummary;
        private Text _chainName;
        private Text _chainSource;
        private Text _chainEffect;
        private Button _chainActivate;
        private Button _chainPass;
        private int _chainSelection;

        // Zone viewer refs
        private Text _zoneTitle;
        private RectTransform _zoneGrid;
        private ScrollRect _zoneScroll;
        private Text _zoneEmpty;
        private Action<uint> _openOwnZone;

        // Result refs
        private Image _resAccent;
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
        public bool IsZoneViewerOpen => ZoneViewRoot != null && ZoneViewRoot.activeSelf;

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
            CloseZoneViewer();
        }

        internal void SetOwnZoneShortcuts(Action<uint> openOwnZone) => _openOwnZone = openOwnZone;

        public void HideResult()
        {
            if (ResultRoot != null) ResultRoot.SetActive(false);
        }

        private void ClearBodyAndFooter()
        {
            ClearChildren(_modalBody);
            ClearChildren(_modalFooter);
            ClearChildren(_chainCards);
            _selectedIndices.Clear();
            _chainSelection = -1;
            _confirmBtn = null;
            _confirmBtnText = null;
            _counterAlloc = null;
            _sortOrder = null;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (Application.isPlaying)
                {
                    child.gameObject.SetActive(false);
                    child.SetParent(null, false);
                    UnityEngine.Object.Destroy(child.gameObject);
                }
                else UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        private void BuildPromptModalShell(Transform parent)
        {
            var dim = LotdUiKit.FillRect("Modal_DuelPrompt", parent);
            ModalRoot = dim.gameObject;
            _promptInput = ModalRoot.AddComponent<CanvasGroup>();
            _promptShade = LotdUiKit.AddImage(dim.gameObject, LotdUiKit.WhiteSprite, Color.clear);

            _modalBox = DuelStyle.Panel(dim, "Win",
                new Vector2(0.225f, 0.12f), new Vector2(0.978f, 0.88f));
            _modalBox.gameObject.AddComponent<Button>(); // block click-through to dim

            var hdr = LotdUiKit.CreateRect("Hdr", _modalBox, new Vector2(0f, 0.87f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(hdr.gameObject, LotdUiKit.WhiteSprite, DuelStyle.HeaderColor);
            var titleBand = LotdUiKit.CreateRect("TitleBand", hdr, new Vector2(0.025f, 0.48f), new Vector2(0.80f, 0.92f),
                Vector2.zero, Vector2.zero);
            var subBand = LotdUiKit.CreateRect("SubtitleBand", hdr, new Vector2(0.025f, 0.12f), new Vector2(0.80f, 0.46f),
                Vector2.zero, Vector2.zero);
            _modalTitle = LotdUiKit.AddFittedText(titleBand, "Title", "", 21, 11, TextAnchor.MiddleLeft,
                DuelStyle.Gold, FontStyle.Bold);
            _modalSubtitle = LotdUiKit.AddFittedText(subBand, "Sub", "", 14, 10, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary);

            _modalBody = LotdUiKit.CreateRect("Body", _modalBox, new Vector2(0.02f, 0.14f), new Vector2(0.98f, 0.85f), Vector2.zero, Vector2.zero);
            _modalFooter = LotdUiKit.CreateRect("Footer", _modalBox, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.12f), Vector2.zero, Vector2.zero);

            var closeRt = LotdUiKit.CreateRect("CloseBtn", hdr, new Vector2(0.82f, 0.16f), new Vector2(0.98f, 0.84f), Vector2.zero, Vector2.zero);
            _modalCloseBtn = DuelStyle.Button(closeRt, "B", "✕ 취소", OnModalCloseClicked, 13);
            _promptBackdrop = dim.gameObject.AddComponent<Button>();
            _promptBackdrop.onClick.AddListener(OnModalCloseClicked);
            BuildChainPicker(dim);
            _zoneShortcuts = LotdUiKit.CreateRect("ZoneShortcuts", dim, new Vector2(.595f, .038f),
                new Vector2(.978f, .106f), Vector2.zero, Vector2.zero);
            AddZoneShortcut(_zoneShortcuts, "Grave", "내 묘지", YgoConstants.LOCATION_GRAVE, 0);
            AddZoneShortcut(_zoneShortcuts, "Extra", "내 엑스트라 덱", YgoConstants.LOCATION_EXTRA, 1);
            AddZoneShortcut(_zoneShortcuts, "Banished", "내 제외 존", YgoConstants.LOCATION_REMOVED, 2);

            ModalRoot.SetActive(false);
        }

        private void AddZoneShortcut(Transform parent, string name, string label, uint location, int column)
        {
            var tile = LotdUiKit.CreateRect(name, parent, new Vector2(column / 3f, 0f),
                new Vector2((column + 1) / 3f, 1f), new Vector2(3f, 0f), new Vector2(-3f, 0f));
            DuelStyle.Button(tile, "B", label, () => _openOwnZone?.Invoke(location), 14);
        }

        private void BuildChainPicker(Transform parent)
        {
            _chainBox = DuelStyle.Panel(parent, "ChainWin", new Vector2(.235f, .214f), new Vector2(.845f, .480f),
                fill: new Color(.022f, .038f, .054f, .98f), border: new Color(.45f, .65f, .75f, .58f));
            if (DuelVisualAssets.ChainFrame != null)
            {
                var frame = LotdUiKit.FillRect("Frame", _chainBox, -17f);
                var image = LotdUiKit.AddImage(frame.gameObject, DuelVisualAssets.ChainFrame, Color.white, false, false);
                image.type = Image.Type.Sliced;
                image.fillCenter = false;
                image.pixelsPerUnitMultiplier = 2.4f;
            }
            var header = LotdUiKit.CreateRect("Hdr", _chainBox, new Vector2(0f, .82f), Vector2.one, Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(header.gameObject, LotdUiKit.WhiteSprite, DuelStyle.HeaderColor, false, false);
            var title = LotdUiKit.CreateRect("Title", header, new Vector2(.024f, .05f), new Vector2(.62f, .95f),
                Vector2.zero, Vector2.zero);
            _chainTitle = LotdUiKit.AddFittedText(title, "Text", "", 19, 11, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            var summary = LotdUiKit.CreateRect("Summary", header, new Vector2(.67f, .07f), new Vector2(.972f, .93f),
                Vector2.zero, Vector2.zero);
            _chainSummary = LotdUiKit.AddFittedText(summary, "Text", "", 16, 10, TextAnchor.MiddleRight, DuelStyle.Gold, FontStyle.Bold);
            if (DuelVisualAssets.ChainLinks != null)
            {
                var links = LotdUiKit.CreateRect("Links", header, new Vector2(.62f, .34f), new Vector2(.695f, .66f),
                    Vector2.zero, Vector2.zero);
                LotdUiKit.AddImage(links.gameObject, DuelVisualAssets.ChainLinks, DuelStyle.Gold, true, false);
            }

            var cards = LotdUiKit.CreateRect("Cards", _chainBox, new Vector2(.019f, .208f), new Vector2(.563f, .798f),
                Vector2.zero, Vector2.zero);
            _chainCardScroll = cards.gameObject.AddComponent<ScrollRect>();
            _chainCardScroll.horizontal = true;
            _chainCardScroll.vertical = false;
            _chainCardScroll.movementType = ScrollRect.MovementType.Clamped;
            _chainCardScroll.scrollSensitivity = 30f;
            var viewport = LotdUiKit.CreateRect("Viewport", cards, new Vector2(0f, .085f), Vector2.one, Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(viewport.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            viewport.gameObject.AddComponent<RectMask2D>();
            _chainCards = LotdUiKit.CreateRect("Content", viewport, Vector2.zero, Vector2.up, Vector2.zero, Vector2.zero);
            _chainCards.pivot = new Vector2(0f, .5f);
            var layout = _chainCards.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = ChainCardGap;
            layout.padding = new RectOffset(3, 3, 3, 3);
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;
            _chainCards.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            _chainCardScroll.content = _chainCards;
            _chainCardScroll.viewport = viewport;

            var track = LotdUiKit.CreateRect("Scrollbar", cards, Vector2.zero, new Vector2(1f, .049f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(track.gameObject, LotdUiKit.WhiteSprite, new Color(.08f, .13f, .18f));
            var slide = LotdUiKit.FillRect("SlidingArea", track);
            var handle = LotdUiKit.FillRect("Handle", slide);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.LeftToRight;
            scrollbar.targetGraphic = LotdUiKit.AddImage(handle.gameObject, LotdUiKit.WhiteSprite, DuelStyle.Player);
            scrollbar.handleRect = handle;
            _chainCardScroll.horizontalScrollbar = scrollbar;
            _chainCardScroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            var details = DuelStyle.Panel(_chainBox, "Details", new Vector2(.583f, .218f), new Vector2(.977f, .798f));
            var name = LotdUiKit.CreateRect("Name", details, new Vector2(.04f, .72f), new Vector2(.96f, .98f),
                Vector2.zero, Vector2.zero);
            _chainName = LotdUiKit.AddFittedText(name, "Text", "", 16, 10, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            var source = LotdUiKit.CreateRect("Source", details, new Vector2(.04f, .57f), new Vector2(.96f, .73f),
                Vector2.zero, Vector2.zero);
            _chainSource = LotdUiKit.AddFittedText(source, "Text", "", 12, 9, TextAnchor.MiddleLeft, DuelStyle.Gold);
            var description = LotdUiKit.CreateRect("Description", details, new Vector2(.0f, .025f), new Vector2(1f, .565f),
                Vector2.zero, Vector2.zero);
            _chainEffectScroll = CreateVerticalScroll(description, out Transform content);
            _chainEffect = AddDescriptionText(content, "Effect", "", 13);

            var footer = LotdUiKit.CreateRect("Footer", _chainBox, new Vector2(.021f, .026f), new Vector2(.977f, .181f),
                Vector2.zero, Vector2.zero);
            var pass = LotdUiKit.CreateRect("Pass", footer, new Vector2(.655f, 0f), new Vector2(.817f, 1f), Vector2.zero, Vector2.zero);
            _chainPass = DuelStyle.Button(pass, "B", "패스", OnModalCloseClicked, 14);
            var activate = LotdUiKit.CreateRect("Activate", footer, new Vector2(.836f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
            _chainActivate = DuelStyle.Button(activate, "B", "발동", ConfirmChainChoice, 14, gold: true);
            _chainBox.gameObject.SetActive(false);
        }

        public bool TryCancelOrPass()
        {
            if (!IsPromptOpen || IsZoneViewerOpen || _curPrompt == null) return false;

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
            if (!IsPromptOpen || IsZoneViewerOpen || _curPrompt == null) return false;

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
            if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced && prompt.candidates.Count == 0)
            {
                ModalRoot.SetActive(false);
                _onSubmitResponse?.Invoke(BitConverter.GetBytes(-1));
                return;
            }

            bool chain = prompt.messageType == YgoConstants.MSG_SELECT_CHAIN ||
                prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN;
            _chainBox.gameObject.SetActive(chain);
            _modalBox.gameObject.SetActive(!chain);
            _promptShade.color = new Color(0f, .02f, .07f, chain ? .10f : .52f);
            _promptBackdrop.interactable = !chain;
            _zoneShortcuts.anchorMin = chain ? new Vector2(.247f, .221f) : new Vector2(.595f, .038f);
            _zoneShortcuts.anchorMax = chain ? new Vector2(.619f, .262f) : new Vector2(.978f, .106f);
            bool shortPrompt = prompt.messageType == YgoConstants.MSG_SELECT_YESNO ||
                prompt.messageType == YgoConstants.MSG_SELECT_POSITION;
            _modalBox.anchorMin = shortPrompt ? new Vector2(.294f, .26f) : new Vector2(.225f, .12f);
            _modalBox.anchorMax = shortPrompt ? new Vector2(.844f, .74f) : new Vector2(.978f, .88f);
            ModalRoot.transform.SetAsLastSibling();
            ModalRoot.SetActive(true);
            CardAnimationManager.Instance?.BringToFront();

            if (chain)
            {
                RenderChainPicker(session, prompt);
                return;
            }

            bool canCancel = false;
            if (prompt.messageType == YgoConstants.MSG_SELECT_YESNO) canCancel = true;
            else if ((prompt.messageType == YgoConstants.MSG_SELECT_CARD || prompt.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD) && prompt.cancelable) canCancel = true;

            if (_modalCloseBtn != null)
                _modalCloseBtn.gameObject.SetActive(canCancel);

            switch (prompt.messageType)
            {
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
            if (!IsPromptOpen || IsZoneViewerOpen) return;
            ModalRoot.SetActive(false);
            _onSubmitResponse?.Invoke(bytes);
        }

        private void RenderYesNo(DuelSession s, DuelPrompt p)
        {
            var card = CardDatabase.Get(CardDatabase.GetDescriptionCardCode(p.promptDesc));
            string msg = CardDatabase.ResolveEffectDescription(p.promptDesc, 0)
                ?? CardDatabase.ResolveDescription(s.LastHintMsg)
                ?? "선택한 처리를 실행하시겠습니까?";

            _modalTitle.text = "확인 선택 (YES / NO)";
            _modalSubtitle.text = card?.name ?? "질문에 대한 예/아니오를 선택하세요.";

            var box = DuelStyle.Panel(_modalBody, "Msg", new Vector2(0.10f, 0.12f), new Vector2(0.90f, 0.90f), Vector2.zero, Vector2.zero);
            CreateVerticalScroll(box, out Transform description);
            AddDescriptionText(description, "Question", msg, 18);
            if (!string.IsNullOrWhiteSpace(card?.desc) && msg != card.desc.Trim())
                AddDescriptionText(description, "CardText", card.desc);

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
                var slot = DuelStyle.Panel(_modalBody, $"Pos{i}",
                    new Vector2(i * w + 0.02f, 0.12f), new Vector2((i + 1) * w - 0.02f, 0.90f), Vector2.zero, Vector2.zero);

                var cardHolder = LotdUiKit.CreateRect("C", slot, new Vector2(0.18f, 0.30f), new Vector2(0.82f, 0.88f), Vector2.zero, Vector2.zero);
                if (ch.rot) cardHolder.localRotation = Quaternion.Euler(0, 0, -90f);
                var cv = CompositeCardView.Create(cardHolder, "V", false, duelArtwork: true);
                cv.SetCard(p.promptCardCode, faceDown: ch.fd);

                var bRt = LotdUiKit.CreateRect("Btn", slot, new Vector2(0.08f, 0.05f), new Vector2(0.92f, 0.22f), Vector2.zero, Vector2.zero);
                DuelStyle.Button(bRt, "B", ch.label, () => Submit(BitConverter.GetBytes((int)ch.pos)), 16, gold: ch.pos == YgoConstants.POS_FACEUP_ATTACK);
            }
        }

        private void RenderOptionList(DuelPrompt p)
        {
            bool isNum = p.messageType == YgoConstants.MSG_ANNOUNCE_NUMBER;
            _modalTitle.text = isNum ? "숫자 선언" : "효과 / 항목 선택";
            _modalSubtitle.text = "실행할 항목을 선택하세요.";

            CreateVerticalScroll(_modalBody, out Transform content);
            var sources = new List<CardInfo>();
            for (int i = 0; i < p.options.Count; i++)
            {
                int pick = i;
                ulong opt = p.options[i];
                var card = isNum ? null : CardDatabase.Get(CardDatabase.GetDescriptionCardCode(opt));
                if (card != null && !sources.Contains(card))
                    sources.Add(card);
                string text = isNum ? $"선언 숫자: {opt}" : CardDatabase.ResolveDescription(opt);
                if (text == null)
                    text = card != null ? $"{i + 1}. {card.name}" : $"선택지 {i + 1}";
                var option = LotdUiKit.CreateWrappedButton(content, $"Opt_{i}", text, () => Submit(BitConverter.GetBytes(pick)),
                    17, i == 0, 54f, sidePadding: 22);
                DuelStyle.ApplyButton(option, i == 0);
            }
            if (sources.Count == 1)
            {
                _modalSubtitle.text = $"{sources[0].name}의 효과를 선택하세요.";
                _onInspectCard?.Invoke(sources[0].code);
            }
            foreach (var card in sources)
            {
                if (!string.IsNullOrWhiteSpace(card.desc))
                    AddDescriptionText(content, "CardText", $"{card.name}\n{card.desc}");
            }
        }

        private void RenderChainPicker(DuelSession session, DuelPrompt prompt)
        {
            bool singleEffect = prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN;
            IReadOnlyList<SelectableCardItem> choices = singleEffect
                ? new[] { new SelectableCardItem
                {
                    code = prompt.promptCardCode,
                    controller = prompt.promptCon,
                    location = prompt.promptLoc,
                    sequence = prompt.promptSeq,
                    description = prompt.promptDesc,
                } }
                : prompt.candidates;
            _chainTitle.text = prompt.forced ? "필수 효과 순서 선택" : "발동할 효과 선택";
            _chainSummary.text = $"체인 {session.CurrentChain.Count + 1}  ·  {choices.Count}개";
            _chainPass.transform.parent.gameObject.SetActive(!prompt.forced);
            _chainActivate.interactable = choices.Count > 0;
            _chainName.text = _chainSource.text = _chainEffect.text = "";

            var frames = new Outline[choices.Count];
            var sources = new (byte controller, uint location, uint sequence)[choices.Count];
            for (int i = 0; i < choices.Count; i++)
                sources[i] = (choices[i].controller, choices[i].location, choices[i].sequence);
            var sourceCounts = sources.GroupBy(source => source).ToDictionary(group => group.Key, group => group.Count());
            var sourceNumbers = new Dictionary<(byte, uint, uint), int>();

            void Select(int index)
            {
                if (!IsPromptOpen || IsZoneViewerOpen || !ReferenceEquals(_curPrompt, prompt)) return;
                if (_chainSelection >= 0) frames[_chainSelection].effectColor = DuelStyle.BorderColor;
                _chainSelection = index;
                frames[index].effectColor = DuelStyle.Gold;
                var item = choices[index];
                _chainName.text = CardDatabase.GetName(item.code);
                _chainSource.text = ChainSourceLabel(session, item);
                _chainEffect.text = CardDatabase.ResolveEffectDescription(item.description, item.code)
                    ?? CardDatabase.Get(item.code)?.desc ?? "이 카드의 효과를 발동할 수 있습니다.";
                _chainEffectScroll.StopMovement();
                _chainEffectScroll.content.anchoredPosition = Vector2.zero;
                _onInspectCard?.Invoke(item.code);
            }

            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                var item = choices[index];
                var tile = DuelStyle.Panel(_chainCards, $"Ch_{index}", Vector2.zero, Vector2.one);
                var size = tile.gameObject.AddComponent<LayoutElement>();
                size.preferredWidth = ChainCardWidth;
                frames[index] = tile.GetComponent<Outline>();

                var art = LotdUiKit.CreateRect("Card", tile, new Vector2(.08f, .17f), new Vector2(.92f, .97f),
                    Vector2.zero, Vector2.zero);
                CompositeCardView.Create(art, "V", duelArtwork: true).SetCard(item.code);

                var source = sources[index];
                string label = YgoConstants.GetLocationNameKR(source.location);
                if (sourceCounts[source] > 1)
                {
                    sourceNumbers.TryGetValue(source, out int number);
                    sourceNumbers[source] = ++number;
                    label += $" · {number}";
                }
                var caption = LotdUiKit.CreateRect("Source", tile, new Vector2(.05f, .015f), new Vector2(.95f, .17f),
                    Vector2.zero, Vector2.zero);
                LotdUiKit.AddFittedText(caption, "Text", label, 11, 8, TextAnchor.MiddleCenter, DuelStyle.Muted);
                var button = tile.gameObject.AddComponent<Button>();
                button.targetGraphic = tile.GetComponent<Image>();
                button.onClick.AddListener(() => Select(index));
            }

            _chainCardScroll.StopMovement();
            _chainCards.anchoredPosition = Vector2.zero;
            float stripWidth = 6f + choices.Count * ChainCardWidth + Mathf.Max(0, choices.Count - 1) * ChainCardGap;
            _chainCardScroll.horizontalScrollbar.gameObject.SetActive(stripWidth > _chainCardScroll.viewport.rect.width);
            if (choices.Count > 0) Select(0);
        }

        private static string ChainSourceLabel(DuelSession session, SelectableCardItem card) =>
            $"{(card.controller == session.HumanPlayerIndex ? "내" : "상대")} {YgoConstants.GetLocationNameKR(card.location)}";

        private void ConfirmChainChoice()
        {
            if (_chainSelection < 0 || _curPrompt == null) return;
            if (_curPrompt.messageType == YgoConstants.MSG_SELECT_CHAIN && _chainSelection < _curPrompt.candidates.Count)
                Submit(BitConverter.GetBytes(_chainSelection));
            else if (_curPrompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN)
                Submit(BitConverter.GetBytes(1));
        }


        private void RenderCardMultiSelect(DuelSession s, DuelPrompt p)
        {
            string hint = CardDatabase.ResolveDescription(s.LastHintMsg);
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
            var tile = DuelStyle.Panel(grid, $"Tile_{idx}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var cardRt = LotdUiKit.CreateRect("Card", tile, new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.96f), Vector2.zero, Vector2.zero);
            var cv = CompositeCardView.Create(cardRt, "V", false, duelArtwork: true);
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
            var chkImg = LotdUiKit.AddImage(checkRt.gameObject, LotdUiKit.LoadSprite("LOTD/Icons/ICON_ID_CHECKMARK"), DuelStyle.Gold, true, false);
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
                        ol.effectColor = DuelStyle.Gold;
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
            var tile = DuelStyle.Panel(grid, "Imm", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                DuelStyle.PanelColor, checkedState ? DuelStyle.Gold : LotdUiKit.CyanAccent);
            var cRt = LotdUiKit.CreateRect("Card", tile, new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.96f), Vector2.zero, Vector2.zero);
            CompositeCardView.Create(cRt, "V", false, duelArtwork: true).SetCard(code, faceDown: code == 0);
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
                var tile = DuelStyle.Panel(grid, $"Cnt_{i}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var cRt = LotdUiKit.CreateRect("Card", tile, new Vector2(0.10f, 0.30f), new Vector2(0.90f, 0.95f), Vector2.zero, Vector2.zero);
                CompositeCardView.Create(cRt, "V", false, duelArtwork: true).SetCard(c.code);

                countLabels[i] = LotdUiKit.AddText(LotdUiKit.CreateRect("Lbl", tile, new Vector2(0.05f, 0.16f), new Vector2(0.95f, 0.30f), Vector2.zero, Vector2.zero),
                    "L", $"0 / {c.currentCounter}개", 14, TextAnchor.MiddleCenter, DuelStyle.Gold, FontStyle.Bold);

                var minus = LotdUiKit.CreateRect("M", tile, new Vector2(0.10f, 0.03f), new Vector2(0.46f, 0.15f), Vector2.zero, Vector2.zero);
                DuelStyle.Button(minus, "B", "-", () =>
                {
                    if (_counterAlloc[idx] > 0) _counterAlloc[idx]--;
                    countLabels[idx].text = $"{_counterAlloc[idx]} / {c.currentCounter}개";
                    UpdateCounterConfirm();
                }, 16);
                var plus = LotdUiKit.CreateRect("P", tile, new Vector2(0.54f, 0.03f), new Vector2(0.90f, 0.15f), Vector2.zero, Vector2.zero);
                DuelStyle.Button(plus, "B", "+", () =>
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
                    var tile = DuelStyle.Panel(grid, $"Sort_{pos}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    var cRt = LotdUiKit.CreateRect("Card", tile, new Vector2(0.10f, 0.25f), new Vector2(0.90f, 0.94f), Vector2.zero, Vector2.zero);
                    CompositeCardView.Create(cRt, "V", false, duelArtwork: true).SetCard(c.code);
                    LotdUiKit.AddText(LotdUiKit.CreateRect("Ord", tile, new Vector2(0.05f, 0.14f), new Vector2(0.95f, 0.25f), Vector2.zero, Vector2.zero),
                        "O", $"순서 #{pos + 1}", 14, TextAnchor.MiddleCenter, DuelStyle.Gold, FontStyle.Bold);

                    var swapRt = LotdUiKit.CreateRect("Swap", tile, new Vector2(0.10f, 0.03f), new Vector2(0.90f, 0.13f), Vector2.zero, Vector2.zero);
                    DuelStyle.Button(swapRt, "B", curPos > 0 ? "◀ 앞으로" : "▶ 뒤로", () =>
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
                var tile = DuelStyle.Panel(grid, $"Race_{bit}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
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
                        pair.ol.effectColor = (chosenMask & pair.bit) != 0 ? DuelStyle.Gold : LotdUiKit.CyanAccent;
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
                var tile = DuelStyle.Panel(grid, $"Attr_{bit}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
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
                        pair.ol.effectColor = (chosenMask & pair.bit) != 0 ? DuelStyle.Gold : LotdUiKit.CyanAccent;
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
                var slot = DuelStyle.Panel(_modalBody, $"Rps_{ch.val}",
                    new Vector2(i * w + 0.03f, 0.22f), new Vector2((i + 1) * w - 0.03f, 0.78f), Vector2.zero, Vector2.zero);
                LotdUiKit.AddText(LotdUiKit.CreateRect("Lbl", slot, new Vector2(0.08f, 0.35f), new Vector2(0.92f, 0.85f), Vector2.zero, Vector2.zero),
                    "T", ch.label, 28, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);
                var bRt = LotdUiKit.CreateRect("Btn", slot, new Vector2(0.12f, 0.10f), new Vector2(0.88f, 0.32f), Vector2.zero, Vector2.zero);
                DuelStyle.Button(bRt, "B", "선택", () => Submit(BitConverter.GetBytes(ch.val)), 16, gold: ch.gold);
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

                    var tile = DuelStyle.Panel(grid, $"Dec_{code}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    var cRt = LotdUiKit.CreateRect("C", tile, new Vector2(0.10f, 0.22f), new Vector2(0.90f, 0.95f), Vector2.zero, Vector2.zero);
                    CompositeCardView.Create(cRt, "V", false, duelArtwork: true).SetCard(code);
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
            LotdUiKit.AddImage(dim.gameObject, LotdUiKit.WhiteSprite, new Color(0.015f, 0.022f, 0.038f, 0.40f));
            dim.gameObject.AddComponent<Button>().onClick.AddListener(CloseZoneViewer);

            var win = DuelStyle.Panel(dim, "Win",
                new Vector2(0.226f, 0.205f), new Vector2(0.972f, 0.615f),
                fill: new Color(.039f, .071f, .093f, .97f));
            win.gameObject.AddComponent<Button>();

            var hdr = LotdUiKit.CreateRect("Hdr", win, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -53f), Vector2.zero);
            LotdUiKit.AddImage(hdr.gameObject, LotdUiKit.WhiteSprite, DuelStyle.HeaderColor);
            var accent = LotdUiKit.CreateRect("Accent", hdr, new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -2f), Vector2.zero);
            LotdUiKit.AddImage(accent.gameObject, LotdUiKit.WhiteSprite, DuelStyle.Player, false, false);
            var titleBand = LotdUiKit.CreateRect("TitleBand", hdr, new Vector2(0.025f, 0.12f), new Vector2(0.80f, 0.88f),
                Vector2.zero, Vector2.zero);
            _zoneTitle = LotdUiKit.AddFittedText(titleBand, "T", "존 확인", 20, 11, TextAnchor.MiddleLeft,
                DuelStyle.Gold, FontStyle.Bold);

            var closeRt = LotdUiKit.CreateRect("Close", hdr, new Vector2(0.82f, 0.15f), new Vector2(0.98f, 0.85f), Vector2.zero, Vector2.zero);
            DuelStyle.Button(closeRt, "B", "✕ 닫기", CloseZoneViewer, 16);

            var body = LotdUiKit.CreateRect("Body", win, Vector2.zero, Vector2.one,
                new Vector2(10f, 8f), new Vector2(-10f, -62f));
            _zoneScroll = CreateHorizontalCardGrid(body, out Transform g, cellW: 118, cellH: 174).GetComponent<ScrollRect>();
            _zoneScroll.movementType = ScrollRect.MovementType.Clamped;
            _zoneScroll.viewport.offsetMax = new Vector2(-14f, 0f);
            _zoneScroll.verticalScrollbar = LotdUiKit.AddVerticalScrollbar(body);
            _zoneScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            _zoneScroll.verticalScrollbar.GetComponent<Image>().sprite = LotdUiKit.WhiteSprite;
            _zoneScroll.verticalScrollbar.GetComponent<Image>().color = new Color(.095f, .16f, .20f);
            var thumb = _zoneScroll.verticalScrollbar.targetGraphic as Image;
            thumb.sprite = LotdUiKit.WhiteSprite;
            thumb.color = DuelStyle.Player;
            _zoneGrid = (RectTransform)g;
            _zoneEmpty = LotdUiKit.AddFittedText(body, "Empty", "이 존에 카드가 없습니다.", 20, 11,
                TextAnchor.MiddleCenter, DuelStyle.Muted);

            ZoneViewRoot.SetActive(false);
        }

        public void CloseZoneViewer()
        {
            if (!IsZoneViewerOpen) return;
            ZoneViewRoot.SetActive(false);
            _promptInput.interactable = true;
            for (int i = _zoneGrid.childCount - 1; i >= 0; i--)
            {
                var child = _zoneGrid.GetChild(i);
                if (Application.isPlaying)
                {
                    child.gameObject.SetActive(false);
                    child.SetParent(null, false);
                    UnityEngine.Object.Destroy(child.gameObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
            _zoneScroll.StopMovement();
            _zoneGrid.anchoredPosition = Vector2.zero;
        }

        public void ShowZoneViewer(
            string title,
            IReadOnlyList<LiveCardState> cards,
            bool hideFaceDown = false,
            bool newestFirst = true,
            Func<LiveCardState, bool> hasCardActions = null,
            Action<LiveCardState> onOpenCardActions = null,
            Func<bool> canUseCardActions = null)
        {
            StartZoneViewer();
            int count = cards?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                var card = cards[newestFirst ? count - 1 - i : i];
                if (card == null) continue;
                bool hidden = card.code == 0 || hideFaceDown && !card.IsFaceUp && !card.isPublic;
                bool actionable = !hidden && onOpenCardActions != null && hasCardActions?.Invoke(card) == true;
                AddZoneCardTile(hidden ? 0u : card.code, hidden, card, actionable, onOpenCardActions, canUseCardActions);
            }
            FinishZoneViewer(title);
        }

        public void ShowCodeListViewer(string title, IReadOnlyList<uint> codes)
        {
            StartZoneViewer();
            if (codes != null)
            {
                foreach (uint code in codes)
                    AddZoneCardTile(code, code == 0, null);
            }
            FinishZoneViewer(title);
        }

        private void StartZoneViewer()
        {
            CloseZoneViewer();
            EventSystem.current?.SetSelectedGameObject(null);
            _promptInput.interactable = false;
            ZoneViewRoot.SetActive(true);
        }

        private void FinishZoneViewer(string title)
        {
            int count = _zoneGrid.childCount;
            _zoneTitle.text = $"{title}   ·   {count}장";
            _zoneEmpty.gameObject.SetActive(count == 0);
            _zoneScroll.StopMovement();
            _zoneGrid.anchoredPosition = Vector2.zero;
            ZoneViewRoot.transform.SetAsLastSibling();
            CardAnimationManager.Instance?.BringToFront();
        }

        private void AddZoneCardTile(
            uint code,
            bool faceDown,
            LiveCardState live,
            bool hasActions = false,
            Action<LiveCardState> onOpenCardActions = null,
            Func<bool> canUseCardActions = null)
        {
            var tile = DuelStyle.Panel(_zoneGrid, $"Z_{code}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                DuelStyle.PanelColor, hasActions ? DuelStyle.Gold : DuelStyle.BorderColor);
            var cRt = LotdUiKit.CreateRect("C", tile,
                new Vector2(0.08f, hasActions ? 0.30f : 0.18f), new Vector2(0.92f, 0.96f), Vector2.zero, Vector2.zero);
            CompositeCardView.Create(cRt, "V", false, duelArtwork: true).SetCard(code, faceDown, faceDown ? null : live);
            string caption = faceDown ? "비공개 카드" : CardDatabase.GetName(code);
            LotdUiKit.AddFittedText(LotdUiKit.CreateRect("L", tile,
                new Vector2(0.05f, hasActions ? 0.17f : 0.02f), new Vector2(0.95f, hasActions ? 0.29f : 0.17f), Vector2.zero, Vector2.zero),
                "T", caption, 12, 8, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary);

            if (!faceDown && code != 0)
            {
                var btn = tile.gameObject.AddComponent<Button>();
                btn.targetGraphic = tile.GetComponent<Image>();
                btn.onClick.AddListener(() => _onInspectCard?.Invoke(code));
                var trig = tile.gameObject.AddComponent<CardDragTrigger>();
                trig.OnPointerEnterAction = () => _onInspectCard?.Invoke(code);
                if (hasActions && live != null && live.location == YgoConstants.LOCATION_EXTRA)
                {
                    trig.SourceType = DragSourceType.ExtraDeck;
                    trig.CardCodeGetter = () => canUseCardActions?.Invoke() != false ? code : 0u;
                    trig.IndexGetter = () => (int)live.sequence;
                    trig.DataGetter = () => live;
                }
            }

            if (hasActions)
            {
                var actRt = LotdUiKit.CreateRect("Act", tile, new Vector2(0.06f, 0.02f), new Vector2(0.94f, 0.16f), Vector2.zero, Vector2.zero);
                DuelStyle.Button(actRt, "B", "행동 확인", () =>
                {
                    if (!IsZoneViewerOpen || canUseCardActions?.Invoke() == false) return;
                    _onInspectCard?.Invoke(code);
                    CloseZoneViewer();
                    onOpenCardActions(live);
                }, 11, true);
            }
        }

        // ==================== MATCH RESULT ====================
        private void BuildResultOverlay(Transform parent)
        {
            var dim = LotdUiKit.FillRect("Overlay_MatchResult", parent);
            ResultRoot = dim.gameObject;
            LotdUiKit.AddImage(dim.gameObject, LotdUiKit.WhiteSprite, new Color(0f, 0.02f, 0.08f, 0.84f));

            var win = DuelStyle.Panel(dim, "ResWin", new Vector2(.205f, .212f), new Vector2(.795f, .788f));
            var accent = LotdUiKit.CreateRect("Accent", win, new Vector2(0f, .972f), Vector2.one, Vector2.zero, Vector2.zero);
            _resAccent = LotdUiKit.AddImage(accent.gameObject, LotdUiKit.WhiteSprite, DuelStyle.Player, false, false);

            _resOppBust = LotdUiKit.AddImage(LotdUiKit.CreateRect("Bust", win, new Vector2(0.06f, 0.25f), new Vector2(0.40f, 0.88f), Vector2.zero, Vector2.zero).gameObject,
                null, Color.white, true, false);

            _resHeadline = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("HL", win, new Vector2(.44f, .55f), new Vector2(.95f, .91f), Vector2.zero, Vector2.zero),
                "T", "", 66, 29, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _resStats = LotdUiKit.AddText(LotdUiKit.CreateRect("St", win, new Vector2(0.44f, 0.22f), new Vector2(0.94f, 0.42f), Vector2.zero, Vector2.zero),
                "T", "", 17, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary);

            var r1 = LotdUiKit.CreateRect("B1", win, new Vector2(0.06f, 0.06f), new Vector2(0.35f, 0.18f), Vector2.zero, Vector2.zero);
            DuelStyle.Button(r1, "B", "재대결", () => { HideResult(); _resRematch?.Invoke(); }, 18, gold: true);

            var r2 = LotdUiKit.CreateRect("B2", win, new Vector2(0.37f, 0.06f), new Vector2(0.66f, 0.18f), Vector2.zero, Vector2.zero);
            DuelStyle.Button(r2, "B", "상대 선택", () => { HideResult(); _resOppSel?.Invoke(); }, 18);

            var r3 = LotdUiKit.CreateRect("B3", win, new Vector2(0.68f, 0.06f), new Vector2(0.94f, 0.18f), Vector2.zero, Vector2.zero);
            DuelStyle.Button(r3, "B", "메인 메뉴", () => { HideResult(); _resMenu?.Invoke(); }, 18);

            ResultRoot.SetActive(false);
        }

        public void ShowResult(bool playerWon, bool isDraw, OpponentCharacter opp, int turns, int myLp, int oppLp,
            Action onRematch, Action onOppSelect, Action onMenu)
        {
            _resRematch = onRematch;
            _resOppSel = onOppSelect;
            _resMenu = onMenu;

            _resHeadline.text = isDraw ? "무승부" : playerWon ? "승리" : "패배";
            _resAccent.color = _resHeadline.color = isDraw ? DuelStyle.Muted : playerWon ? DuelStyle.Gold : DuelStyle.Opponent;

            if (playerWon)
            {
                LotdAudio.PlayWin();
            }
            else
            {
                LotdAudio.PlayDamage();
            }

            Sprite bust = LotdUiKit.LoadOpponentPortrait(opp) ?? LotdUiKit.LoadSprite("LOTD/Busts/setokaiba_neutral");
            _resOppBust.sprite = bust;
            _resOppBust.gameObject.SetActive(bust != null);

            string oppName = opp != null ? opp.name : "AI 듀얼리스트";
            _resStats.text = $"대전 상대: {oppName}\n총 경과 턴: {turns}턴\n최종 라이프: 내 LP {myLp}  |  상대 LP {oppLp}";
            ResultRoot.transform.SetAsLastSibling();
            ResultRoot.SetActive(true);
            CardAnimationManager.Instance?.BringToFront();
        }

        // ==================== UI HELPERS ====================
        private Button AddFooterButton(float xMin, float xMax, string label, Action onClick, bool gold)
        {
            var slot = LotdUiKit.CreateRect($"Btn_{label}", _modalFooter, new Vector2(xMin, 0.08f), new Vector2(xMax, 0.92f), Vector2.zero, Vector2.zero);
            return DuelStyle.Button(slot, "B", label, onClick, 17, gold);
        }

        private static Text AddDescriptionText(Transform parent, string name, string value, int size = 15)
        {
            var text = LotdUiKit.AddText(parent, name, value, size, TextAnchor.UpperLeft, LotdUiKit.TextPrimary);
            text.supportRichText = false;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static ScrollRect CreateVerticalScroll(Transform parent, out Transform content)
        {
            var svRt = LotdUiKit.CreateRect("Scroll", parent, Vector2.zero, Vector2.one,
                new Vector2(20f, 4f), new Vector2(-20f, -4f));
            LotdUiKit.AddImage(svRt.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            var sr = svRt.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 28f;
            sr.movementType = ScrollRect.MovementType.Clamped;

            var vp = LotdUiKit.CreateRect("VP", svRt, Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-17f, -2f));
            vp.gameObject.AddComponent<RectMask2D>();

            var cRt = LotdUiKit.CreateRect("Content", vp, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            cRt.pivot = new Vector2(0.5f, 1f);
            var vlg = cRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            cRt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vp;
            sr.content = cRt;
            sr.verticalScrollbar = LotdUiKit.AddVerticalScrollbar(svRt);
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            content = cRt;
            return sr;
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
