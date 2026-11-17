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
        private readonly DeckPadKeyboard _padKeyboard;

        private RectTransform _modalBox;
        private RectTransform _modalHeader;
        private Text _modalTitle;
        private Text _modalSubtitle;
        private RectTransform _modalBody;
        private RectTransform _modalFooter;
        private CanvasGroup _promptInput;
        private DuelPromptBackdrop _promptShade;
        private RectTransform _fieldViewTab;
        private Button _fieldViewButton;
        private Image _fieldViewIcon;
        private Text _fieldViewLabel;
        private bool _chainPrompt;
        private const float ChainCardWidth = 78f;
        private const float ChainCardGap = 7f;
        private const float ChoiceCardWidth = 113f;
        private const float ChoiceCardGap = 8f;
        private RectTransform _chainBox;
        private RectTransform _chainCards;
        private RectTransform _chainDetails;
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
        private int[] _chainCandidateIndices;

        // Zone viewer refs
        private RectTransform _zoneWindow;
        private Text _zoneTitle;
        private Image _zoneIcon;
        private RectTransform _zoneGrid;
        private ScrollRect _zoneScroll;
        private Text _zoneEmpty;
        private Button _zoneClose;
        private GameObject _focusBeforePrompt;
        private GameObject _focusBeforeZone;
        private GameObject _focusBeforeKeyboard;

        // Result refs
        private Image _resCrown;
        private Image _resTitleArt;
        private Image _resTitleRule;
        private Text _resHeadline;
        private Text _resStats;
        private Text _resOpponentUnlock;
        private Button _rematchButton;
        private Action _resRematch;
        private Action _resOppSel;
        private Action _resMenu;

        private DuelSession _curSession;
        private DuelPrompt _curPrompt;

        // State for interactive multi-select modals
        private readonly HashSet<int> _selectedIndices = new HashSet<int>();
        private readonly Dictionary<(byte controller, uint location, uint sequence), int> _fieldCardIndices = new();
        private int[] _counterAlloc;
        private int[] _sortOrder;
        private Button _confirmBtn;
        private RectTransform _materialConfirmRoot;
        private Button _materialConfirm;
        private bool _fieldMaterialPick;
        private (byte controller, uint location, uint sequence)? _lastMaterial;

        public bool IsPromptOpen => ModalRoot != null && ModalRoot.activeSelf;
        public bool IsZoneViewerOpen => ZoneViewRoot != null && ZoneViewRoot.activeSelf;
        internal bool IsChainPromptOpen => IsPromptOpen && _chainPrompt;
        internal bool IsShowingField => IsPromptOpen && _promptShade.ShowingField;
        internal Selectable FieldViewControl => _fieldViewButton;
        internal bool IsFieldCardSelection => IsPromptOpen && _fieldCardIndices.Count > 0;
        internal Button FieldCardConfirmButton => !IsFieldCardSelection ? null :
            _materialConfirmRoot.gameObject.activeSelf ? _materialConfirm : _confirmBtn;

        internal void SetFieldCardHitTest(Func<Vector2, Camera, bool> hitTest) => _promptShade.FieldCardHitTest = hitTest;

        internal bool FindFieldCard(DuelPrompt prompt, byte controller, uint location, uint sequence, out int index)
        {
            index = -1;
            return IsFieldCardSelection && CanAnswerPrompt && ReferenceEquals(prompt, _curPrompt) &&
                _fieldCardIndices.TryGetValue((controller, location, sequence), out index);
        }

        internal bool IsFieldCardSelected(int index) => index < 0 || _selectedIndices.Contains(index) ||
            (_curPrompt.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD && index >= _curPrompt.candidates.Count);

        internal void ToggleFieldCard(int index, byte controller, uint location, uint sequence)
        {
            if (!CanAnswerPrompt || index < 0) return;
            _lastMaterial = (controller, location, sequence);
            if (_curPrompt.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD)
            {
                LotdAudio.PlayClick();
                Submit(PackTwoInts(1, index));
                return;
            }

            bool capped = _curPrompt.messageType != YgoConstants.MSG_SELECT_SUM ||
                !_curPrompt.sumExactCountZeroAny && _curPrompt.max > 0;
            if (!_selectedIndices.Remove(index))
            {
                if (capped && _curPrompt.max == 1) _selectedIndices.Clear();
                if (capped && _selectedIndices.Count >= _curPrompt.max) return;
                _selectedIndices.Add(index);
            }

            LotdAudio.PlayClick();
            UpdateMultiSelectState();
        }

        internal void RefreshFieldMaterialConfirm(Func<byte, uint, uint, RectTransform> findSlot)
        {
            if (!IsFieldCardSelection || !_fieldMaterialPick || !CanAnswerPrompt || _confirmBtn?.interactable != true)
            {
                _materialConfirmRoot.gameObject.SetActive(false);
                if (_confirmBtn != null && _fieldMaterialPick) _confirmBtn.transform.parent.gameObject.SetActive(true);
                return;
            }

            RectTransform anchor = null;
            if (_lastMaterial is { } last && _fieldCardIndices.TryGetValue(last, out int index) && IsFieldCardSelected(index))
                anchor = findSlot(last.controller, last.location, last.sequence);
            if (anchor == null)
                foreach (var (key, choice) in _fieldCardIndices)
                    if (IsFieldCardSelected(choice))
                        anchor = findSlot(key.controller, key.location, key.sequence) ?? anchor;

            _materialConfirmRoot.gameObject.SetActive(anchor != null);
            _confirmBtn.transform.parent.gameObject.SetActive(anchor == null);
            if (anchor == null) return;

            var host = (RectTransform)ModalRoot.transform;
            var point = anchor.TransformPoint(new Vector2(anchor.rect.center.x, anchor.rect.yMax));
            Vector2 local = host.InverseTransformPoint(point);
            _materialConfirmRoot.anchoredPosition = new Vector2(local.x, Mathf.Min(local.y + 32f, host.rect.yMax - 32f));
        }
        private bool CanAnswerPrompt => IsPromptOpen && !IsZoneViewerOpen && !IsShowingField;

        public DuelPromptModals(Transform canvasParent, Action<uint> onInspectCard, Action<byte[]> onSubmitResponse)
        {
            _onInspectCard = onInspectCard;
            _onSubmitResponse = onSubmitResponse;
            BuildPromptModalShell(canvasParent);
            _padKeyboard = new DeckPadKeyboard(ModalRoot.transform, RestoreKeyboardFocus);
            BuildZoneViewer(canvasParent);
            BuildResultOverlay(canvasParent);
        }

        public void HideAll()
        {
            CloseZoneViewer();
            HidePrompt();
        }

        internal void SetBrowsablePiles(params DuelZoneImage[] piles) => _promptShade.BrowsablePiles = piles;
        internal void SetChainConfirmationControl(Button button) => _promptShade.ChainConfirmationControl = button;

        internal bool IsPointOverBrowsablePile(Vector2 point)
        {
            var canvas = _promptShade.canvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return _promptShade.IsPointOverPile(point, camera);
        }

        internal bool TryGetPadFocus(out RectTransform target, out RectTransform host)
        {
            host = (_padKeyboard.IsOpen ? _padKeyboard.Scope :
                IsZoneViewerOpen ? ZoneViewRoot.transform :
                ResultRoot.activeSelf ? ResultRoot.transform :
                IsPromptOpen && !IsShowingField && !IsFieldCardSelection ? ModalRoot.transform : null) as RectTransform;
            target = EventSystem.current?.currentSelectedGameObject?.transform as RectTransform;
            return host != null && target != null && target.IsChildOf(host) && target.gameObject.activeInHierarchy;
        }

        internal bool HandlePad(DeckPadFrame frame)
        {
            if (!frame.HasInput)
                return _padKeyboard.IsOpen || IsZoneViewerOpen || ResultRoot.activeSelf || IsPromptOpen && !IsShowingField && !IsFieldCardSelection;
            if (_padKeyboard.IsOpen)
                HandlePadScope(_padKeyboard.Scope, frame, _padKeyboard.DefaultFocus, _padKeyboard.Close);
            else if (IsZoneViewerOpen)
                HandlePadScope(ZoneViewRoot.transform, frame, FirstFocus(_zoneGrid) ?? _zoneClose, CloseZoneViewer, _zoneScroll);
            else if (ResultRoot.activeSelf)
                HandlePadScope(ResultRoot.transform, frame, _rematchButton, TriggerMenu);
            else if (IsPromptOpen && !IsShowingField && !IsFieldCardSelection)
                HandlePadScope(ModalRoot.transform, frame, PreferredPromptControl(), OnModalCloseClicked,
                    _chainPrompt ? _chainEffectScroll : _modalBody.GetComponentInChildren<ScrollRect>());
            else return false;

            return true;
        }

        private void HandlePadScope(Transform scope, DeckPadFrame frame, Selectable initial, Action cancel, ScrollRect scroll = null)
        {
            if (frame.Cancel)
            {
                cancel();
                return;
            }
            ScrollFromPad(scroll, frame.DescriptionScroll);
            var control = EventSystem.current?.currentSelectedGameObject?.GetComponent<Selectable>();
            if (!CanNavigate(control, scope))
            {
                control = CanNavigate(initial, scope) ? initial : FirstFocus(scope);
                SetFocus(control?.gameObject);
                return;
            }
            if (frame.Direction != Vector2Int.zero)
            {
                var next = PadNeighbor(scope, control, frame.Direction);
                SetFocus(next.gameObject);
                if (IsZoneViewerOpen && next.transform.IsChildOf(_zoneGrid))
                {
                    var tile = next.transform;
                    while (tile.parent != _zoneGrid) tile = tile.parent;
                    KeepZoneCardVisible((RectTransform)tile);
                }
                return;
            }
            if (!frame.Submit) return;
            if (control is Button button) button.onClick.Invoke();
            else if (control is InputField field) OpenPadKeyboard(field);
        }

        private void KeepZoneCardVisible(RectTransform tile)
        {
            var viewport = _zoneScroll.viewport;
            float overflow = _zoneGrid.rect.height - viewport.rect.height;
            if (overflow <= 0f) return;

            var upper = viewport.InverseTransformPoint(tile.TransformPoint(new Vector2(0f, tile.rect.yMax))).y;
            var lower = viewport.InverseTransformPoint(tile.TransformPoint(new Vector2(0f, tile.rect.yMin))).y;
            float offset = upper > viewport.rect.yMax ? upper - viewport.rect.yMax :
                Mathf.Min(0f, lower - viewport.rect.yMin);
            _zoneScroll.StopMovement();
            _zoneScroll.verticalNormalizedPosition = Mathf.Clamp01(_zoneScroll.verticalNormalizedPosition + offset / overflow);
        }


        private static void ScrollFromPad(ScrollRect scroll, float amount)
        {
            if (amount == 0f || scroll == null || !scroll.vertical || scroll.viewport == null || scroll.content == null) return;
            float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
            if (overflow <= 0f) return;
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition +
                amount * 630f * Time.unscaledDeltaTime / overflow);
        }

        private static Selectable PadNeighbor(Transform scope, Selectable current, Vector2Int direction)
        {
            Vector2 Center(Selectable control)
            {
                var rect = (RectTransform)control.transform;
                return scope.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            }

            Vector2 origin = Center(current);
            var next = current;
            float best = float.PositiveInfinity;
            foreach (var candidate in scope.GetComponentsInChildren<Selectable>())
            {
                if (candidate == current || !CanNavigate(candidate, scope)) continue;
                Vector2 edge = Center(candidate) - origin;
                float forward = Vector2.Dot(edge, direction);
                if (forward <= 1f) continue;
                float across = direction.x != 0 ? Mathf.Abs(edge.y) : Mathf.Abs(edge.x);
                float score = forward + across * 3.5f;
                if (score >= best) continue;
                best = score;
                next = candidate;
            }
            return next;
        }

        private void OpenPadKeyboard(InputField input)
        {
            if (_padKeyboard.IsOpen || !CanAnswerPrompt || input == null || !input.IsInteractable()) return;
            _focusBeforeKeyboard = EventSystem.current?.currentSelectedGameObject ?? input.gameObject;
            input.DeactivateInputField();
            _padKeyboard.Open(input);
            PrepareFocusControls(_padKeyboard.Scope, _padKeyboard.Close);
            SetFocus(_padKeyboard.DefaultFocus.gameObject);
        }

        private void RestoreKeyboardFocus()
        {
            var previous = _focusBeforeKeyboard;
            _focusBeforeKeyboard = null;
            if (previous != null && CanAnswerPrompt) SetFocus(previous);
        }

        public void HideResult()
        {
            if (ResultRoot == null) return;
            bool restoreFocus = FocusIsInside(ResultRoot);
            ResultRoot.SetActive(false);
            if (restoreFocus) SetFocus(null);
        }

        private void HidePrompt()
        {
            if (!IsPromptOpen) return;
            bool restoreFocus = FocusIsInside(ModalRoot);
            _focusBeforeKeyboard = null;
            _padKeyboard.Close();
            ModalRoot.SetActive(false);
            if (restoreFocus) SetFocus(_focusBeforePrompt);
            _focusBeforePrompt = null;
        }

        private void ClearBodyAndFooter()
        {
            ClearChildren(_modalBody);
            ClearChildren(_modalFooter);
            ClearChildren(_chainCards);
            _selectedIndices.Clear();
            _fieldCardIndices.Clear();
            _fieldMaterialPick = false;
            _materialConfirmRoot.gameObject.SetActive(false);
            _chainSelection = -1;
            _confirmBtn = null;
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
            var shade = LotdUiKit.FillRect("Shade", dim);
            _promptShade = shade.gameObject.AddComponent<DuelPromptBackdrop>();
            _promptShade.sprite = LotdUiKit.WhiteSprite;

            _modalBox = DuelStyle.Panel(dim, "Win", new Vector2(.235f, .17f), new Vector2(.845f, .474f),
                fill: new Color(.022f, .038f, .054f, .98f), border: new Color(.45f, .65f, .75f, .58f));
            AddSelectionFrame(_modalBox);

            _modalHeader = LotdUiKit.CreateRect("Hdr", _modalBox, new Vector2(0f, .81f), Vector2.one, Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(_modalHeader.gameObject, LotdUiKit.WhiteSprite, DuelStyle.HeaderColor, false, false);
            var titleBand = LotdUiKit.CreateRect("TitleBand", _modalHeader, new Vector2(.024f, .48f), new Vector2(.976f, .94f),
                Vector2.zero, Vector2.zero);
            var subBand = LotdUiKit.CreateRect("SubtitleBand", _modalHeader, new Vector2(.024f, .05f), new Vector2(.976f, .47f),
                Vector2.zero, Vector2.zero);
            _modalTitle = LotdUiKit.AddFittedText(titleBand, "Title", "", 21, 11, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            _modalSubtitle = LotdUiKit.AddFittedText(subBand, "Sub", "", 15, 10, TextAnchor.MiddleLeft, DuelStyle.Muted);

            _modalBody = LotdUiKit.CreateRect("Body", _modalBox, new Vector2(.02f, .202f), new Vector2(.98f, .785f), Vector2.zero, Vector2.zero);
            _modalFooter = LotdUiKit.CreateRect("Footer", _modalBox, new Vector2(.62f, .028f), new Vector2(.98f, .183f), Vector2.zero, Vector2.zero);

            BuildChainPicker(dim);
            _materialConfirmRoot = LotdUiKit.CreateRect("MaterialConfirm", dim, Vector2.one * .5f, Vector2.one * .5f,
                new Vector2(-30f, -30f), new Vector2(30f, 30f));
            _materialConfirm = DuelStyle.RoundButton(_materialConfirmRoot, "Button", DuelStyle.Gold, () => TryConfirm());
            var confirmIcon = LotdUiKit.FillRect("Check", _materialConfirm.transform, 13f);
            LotdUiKit.AddFittedText(confirmIcon, "Mark", "✓", 26, 17, TextAnchor.MiddleCenter, DuelStyle.Player, FontStyle.Bold);
            _materialConfirmRoot.gameObject.SetActive(false);
            _fieldViewTab = LotdUiKit.FillRect("FieldView", dim);
            _fieldViewButton = DuelStyle.Button(_fieldViewTab, "B", "", ToggleFieldView, 14);
            PrepareFocusControls(_fieldViewTab, OnModalCloseClicked);
            _fieldViewLabel = _fieldViewButton.GetComponentInChildren<Text>();
            _fieldViewLabel.rectTransform.anchorMin = new Vector2(.30f, 0f);
            _fieldViewLabel.rectTransform.anchorMax = new Vector2(.97f, 1f);
            _fieldViewLabel.rectTransform.offsetMin = _fieldViewLabel.rectTransform.offsetMax = Vector2.zero;
            var icon = LotdUiKit.CreateRect("Icon", _fieldViewButton.transform, new Vector2(.06f, .14f),
                new Vector2(.28f, .86f), Vector2.zero, Vector2.zero);
            _fieldViewIcon = LotdUiKit.AddImage(icon.gameObject, DuelVisualAssets.PromptFieldArrow, Color.white, true, false);
            ModalRoot.SetActive(false);
        }

        private void ToggleFieldView()
        {
            if (!IsPromptOpen || IsZoneViewerOpen || IsFieldCardSelection) return;
            LotdAudio.PlayClick();
            SetFieldView(!IsShowingField);
            if (IsShowingField) SetFocus(_fieldViewButton.gameObject);
            else
            {
                UiEntranceMotion.Play((_chainPrompt ? _chainBox : _modalBox).gameObject);
                FocusPrompt();
            }
        }

        private void SetFieldView(bool showing)
        {
            _promptShade.ShowingField = showing;
            _promptShade.ChainConfirmationAvailable = _chainPrompt;
            _promptShade.SelectingFieldCards = _fieldCardIndices.Count > 0;
            _promptShade.color = showing || _promptShade.SelectingFieldCards ? Color.clear : new Color(0f, .02f, .07f, .11f);
            _fieldViewTab.gameObject.SetActive(!_promptShade.SelectingFieldCards);
            _chainBox.gameObject.SetActive(_chainPrompt && !showing);
            _modalBox.gameObject.SetActive(!_chainPrompt && !showing);
            Vector2 corner = _chainPrompt ? _chainBox.anchorMax : _modalBox.anchorMax;
            _fieldViewTab.anchorMin = showing ? new Vector2(.777f, .112f) : corner - new Vector2(.121f, 0f);
            _fieldViewTab.anchorMax = showing ? new Vector2(.898f, .171f) : corner + new Vector2(0f, .059f);
            _fieldViewLabel.text = showing ? "선택창 보기" : "필드 보기";
            var glow = showing ? DuelVisualAssets.PromptFieldGlow : null;
            _fieldViewIcon.sprite = glow ?? DuelVisualAssets.PromptFieldArrow;
            _fieldViewIcon.enabled = _fieldViewIcon.sprite != null;
            _fieldViewIcon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, glow != null ? 0f : showing ? 90f : -90f);
        }

        private void BuildChainPicker(Transform parent)
        {
            _chainBox = DuelStyle.Panel(parent, "ChainWin", new Vector2(.235f, .214f), new Vector2(.845f, .480f),
                fill: new Color(.022f, .038f, .054f, .98f), border: new Color(.45f, .65f, .75f, .58f));
            AddSelectionFrame(_chainBox);
            var header = LotdUiKit.CreateRect("Hdr", _chainBox, new Vector2(0f, .82f), Vector2.one, Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(header.gameObject, LotdUiKit.WhiteSprite, DuelStyle.HeaderColor, false, false);
            var title = LotdUiKit.CreateRect("Title", header, new Vector2(.024f, .05f), new Vector2(.62f, .95f),
                Vector2.zero, Vector2.zero);
            _chainTitle = LotdUiKit.AddFittedText(title, "Text", "", 19, 11, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            var summary = LotdUiKit.CreateRect("Summary", header, new Vector2(.67f, .07f), new Vector2(.972f, .93f),
                Vector2.zero, Vector2.zero);
            _chainSummary = LotdUiKit.AddFittedText(summary, "Text", "", 16, 10, TextAnchor.MiddleRight, DuelStyle.Gold, FontStyle.Bold);

            var cards = LotdUiKit.CreateRect("Cards", _chainBox, new Vector2(.019f, .208f), new Vector2(.563f, .798f),
                Vector2.zero, Vector2.zero);
            _chainCardScroll = CreateCardStrip(cards, out _chainCards, ChainCardGap);

            var details = _chainDetails = DuelStyle.Panel(_chainBox, "Details", new Vector2(.583f, .218f), new Vector2(.977f, .798f));
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
            _chainPass = DuelStyle.Button(pass, "B", "취소", OnModalCloseClicked, 14);
            var activate = LotdUiKit.CreateRect("Activate", footer, new Vector2(.836f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
            _chainActivate = DuelStyle.Button(activate, "B", "효과 발동", ConfirmChainChoice, 14, gold: true);
            _chainBox.gameObject.SetActive(false);
        }

        public bool TryCancelOrPass()
        {
            if (!IsPromptOpen || IsZoneViewerOpen || _curPrompt == null) return false;
            if (_padKeyboard.IsOpen)
            {
                _padKeyboard.Close();
                return true;
            }
            if (IsShowingField)
            {
                ToggleFieldView();
                return true;
            }

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
            else if ((_curPrompt.messageType == YgoConstants.MSG_SELECT_CARD ||
                      _curPrompt.messageType == YgoConstants.MSG_SELECT_TRIBUTE ||
                      _curPrompt.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD) && _curPrompt.cancelable)
            {
                LotdAudio.PlayClick();
                Submit(BitConverter.GetBytes(-1));
                return true;
            }

            return false;
        }

        public bool TryConfirm()
        {
            if (!CanAnswerPrompt || _curPrompt == null) return false;

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
                _confirmBtn.onClick.Invoke();
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

        public void ShowPrompt(DuelSession session, DuelPrompt prompt,
            ChainConfirmationMode chainConfirmation = ChainConfirmationMode.On)
        {
            if (!IsPromptOpen) _focusBeforePrompt = EventSystem.current?.currentSelectedGameObject;
            _focusBeforeKeyboard = null;
            _padKeyboard.Close();
            if (session != _curSession || _curPrompt?.messageType != YgoConstants.MSG_SELECT_UNSELECT_CARD ||
                prompt.messageType != YgoConstants.MSG_SELECT_UNSELECT_CARD)
                _lastMaterial = null;
            _curSession = session;
            _curPrompt = prompt;
            ClearBodyAndFooter();
            if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced && prompt.candidates.Count == 0)
            {
                HidePrompt();
                _onSubmitResponse?.Invoke(BitConverter.GetBytes(-1));
                return;
            }

            _chainPrompt = prompt.messageType == YgoConstants.MSG_SELECT_CHAIN ||
                prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN;
            bool tall = prompt.messageType == YgoConstants.MSG_ANNOUNCE_CARD || prompt.messageType == YgoConstants.MSG_ANNOUNCE_RACE;
            _modalBox.anchorMax = new Vector2(.845f, tall ? .626f : .474f);
            _modalHeader.anchorMin = new Vector2(0f, tall ? .873f : .81f);
            _modalBody.anchorMin = new Vector2(.02f, tall ? .135f : .202f);
            _modalBody.anchorMax = new Vector2(.98f, tall ? .855f : .785f);
            _modalFooter.anchorMin = new Vector2(.62f, tall ? .019f : .028f);
            _modalFooter.anchorMax = new Vector2(.98f, tall ? .122f : .183f);
            _modalBox.anchorMin = new Vector2(.235f, .17f);
            _modalHeader.anchorMax = Vector2.one;
            BindFieldCardChoices(session, prompt);
            if (_fieldCardIndices.Count > 0)
            {
                _modalBox.anchorMin = _fieldMaterialPick ? new Vector2(.286f, .835f) : new Vector2(.305f, .151f);
                _modalBox.anchorMax = _fieldMaterialPick ? new Vector2(.800f, .898f) : new Vector2(.762f, .214f);
                _modalHeader.anchorMin = Vector2.zero;
                _modalHeader.anchorMax = new Vector2(.56f, 1f);
                _modalFooter.anchorMin = new Vector2(.56f, .08f);
                _modalFooter.anchorMax = new Vector2(.98f, .92f);
            }
            SetFieldView(false);
            ModalRoot.transform.SetAsLastSibling();
            ModalRoot.SetActive(true);
            UiEntranceMotion.Play((_chainPrompt ? _chainBox : _modalBox).gameObject);
            CardAnimationManager.Instance?.BringToFront();

            if (_chainPrompt)
            {
                RenderChainPicker(session, prompt, chainConfirmation);
                PrepareFocusControls(_chainBox, OnModalCloseClicked);
                FocusPrompt();
                return;
            }

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
                    RenderSortCards(session, prompt);
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
                    HidePrompt();
                    break;
            }
            if (IsPromptOpen)
            {
                PrepareFocusControls(_modalBox, OnModalCloseClicked);
                FocusPrompt();
            }
        }

        private void Submit(byte[] bytes)
        {
            if (!CanAnswerPrompt) return;
            HidePrompt();
            _onSubmitResponse?.Invoke(bytes);
        }

        private void RenderYesNo(DuelSession s, DuelPrompt p)
        {
            uint cardCode = CardDatabase.GetDescriptionCardCode(p.promptDesc);
            var card = CardDatabase.Get(cardCode);
            string msg = CardDatabase.ResolveEffectDescription(p.promptDesc, 0)
                ?? CardDatabase.ResolveDescription(s.LastHintMsg)
                ?? "선택한 처리를 실행하시겠습니까?";

            _modalTitle.text = "선택 확인";
            _modalSubtitle.text = card?.name ?? "";

            CreateVerticalScroll(_modalBody, out Transform description);
            AddDescriptionText(description, "Question", msg, 18);
            string cardText = CardDatabase.GetKoreanCardDescription(cardCode);
            if (cardText != null && msg != cardText)
                AddDescriptionText(description, "CardText", cardText);

            AddFooterButton("예", () => Submit(BitConverter.GetBytes(1)), true);
            AddFooterButton("아니오", () => Submit(BitConverter.GetBytes(0)), false);
        }

        private void RenderSelectPosition(DuelPrompt p)
        {
            _modalTitle.text = "표시 형식 선택";
            _modalSubtitle.text = CardDatabase.GetName(p.promptCardCode);
            _onInspectCard?.Invoke(p.promptCardCode);

            var row = LotdUiKit.FillRect("Positions", _modalBody);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;
            layout.spacing = 12f;

            var choices = new (uint position, string label, bool rotate, bool faceDown)[]
            {
                (YgoConstants.POS_FACEUP_ATTACK, "앞면 공격 표시", false, false),
                (YgoConstants.POS_FACEUP_DEFENSE, "앞면 수비 표시", true, false),
                (YgoConstants.POS_FACEDOWN_DEFENSE, "뒷면 수비 표시", true, true),
                (YgoConstants.POS_FACEDOWN_ATTACK, "뒷면 공격 표시", false, true),
            };
            foreach (var choice in choices)
            {
                if ((p.allowedPositions & choice.position) == 0) continue;
                var slot = CreateCardStripTile(row, $"Pos_{choice.position}", 153f);
                var art = LotdUiKit.CreateRect("Card", slot, new Vector2(.14f, .233f), new Vector2(.86f, .967f),
                    Vector2.zero, Vector2.zero);
                if (choice.rotate) art.localRotation = Quaternion.Euler(0f, 0f, -90f);
                CompositeCardView.Create(art, "V", duelArtwork: true).SetCard(p.promptCardCode, faceDown: choice.faceDown);
                var label = LotdUiKit.CreateRect("Label", slot, new Vector2(.05f, .035f), new Vector2(.95f, .21f),
                    Vector2.zero, Vector2.zero);
                LotdUiKit.AddFittedText(label, "Text", choice.label, 15, 10, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                var button = slot.gameObject.AddComponent<Button>();
                button.targetGraphic = slot.GetComponent<Image>();
                button.onClick.AddListener(() =>
                {
                    LotdAudio.PlayClick();
                    Submit(BitConverter.GetBytes((int)choice.position));
                });
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
                PreviewOnFocus(option, card?.code ?? 0);
            }
            if (sources.Count == 1)
            {
                _modalSubtitle.text = $"{sources[0].name}의 효과를 선택하세요.";
                _onInspectCard?.Invoke(sources[0].code);
            }
            foreach (var card in sources)
            {
                string cardText = CardDatabase.GetKoreanCardDescription(card.code);
                if (cardText != null)
                    AddDescriptionText(content, "CardText", $"{card.name}\n{cardText}");
            }
        }

        private void RenderChainPicker(DuelSession session, DuelPrompt prompt, ChainConfirmationMode chainConfirmation)
        {
            bool singleEffect = prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN;
            IReadOnlyList<SelectableCardItem> choices = singleEffect
                ? new[] { new SelectableCardItem
                {
                    code = prompt.promptCardCode,
                    controller = prompt.promptCon,
                    location = prompt.promptLoc,
                    sequence = prompt.promptSeq,
                    description = prompt.promptEffectDesc != 0 ? prompt.promptEffectDesc : prompt.promptDesc,
                } }
                : prompt.candidates;
            _chainCandidateIndices = null;
            if (chainConfirmation != ChainConfirmationMode.On && !singleEffect)
            {
                // Hide fast responses without changing the indices sent back to the engine.
                _chainCandidateIndices = Enumerable.Range(0, prompt.candidates.Count)
                    .Where(index => session.IsChainCandidateVisible(prompt, prompt.candidates[index], chainConfirmation)).ToArray();
                choices = _chainCandidateIndices.Select(index => prompt.candidates[index]).ToArray();
            }
            bool compact = choices.Count == 1;
            _chainBox.anchorMin = compact ? new Vector2(.324f, .218f) : new Vector2(.235f, .214f);
            _chainBox.anchorMax = compact ? new Vector2(.776f, .448f) : new Vector2(.845f, .480f);
            var cardStrip = (RectTransform)_chainCardScroll.transform;
            cardStrip.anchorMax = new Vector2(compact ? .262f : .563f, .798f);
            _chainDetails.anchorMin = new Vector2(compact ? .278f : .583f, .218f);
            SetFieldView(false);
            bool respondingToAttack = session.HasActiveAttack && session.AttackerCon != session.HumanPlayerIndex;
            _chainTitle.text = prompt.forced ? "필수 효과 순서 선택" :
                respondingToAttack && session.CurrentChain.Count == 0
                    ? (session.TargetLoc == 0 ? "상대의 직접 공격에 대응하시겠습니까?" : "상대의 공격 선언에 대응하시겠습니까?")
                    : compact ? "효과를 발동하시겠습니까?" : "발동할 효과 선택";
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
                if (!CanAnswerPrompt || !ReferenceEquals(_curPrompt, prompt)) return;
                if (_chainSelection >= 0) frames[_chainSelection].effectColor = DuelStyle.BorderColor;
                _chainSelection = index;
                frames[index].effectColor = DuelStyle.Gold;
                var item = choices[index];
                _chainName.text = CardDatabase.GetName(item.code);
                _chainSource.text = CardSourceLabel(session, item);
                _chainEffect.text = CardDatabase.ResolveEffectDescription(item.description, item.code)
                    ?? "이 카드의 효과를 발동할 수 있습니다.";
                _chainEffectScroll.StopMovement();
                _chainEffectScroll.content.anchoredPosition = Vector2.zero;
                _onInspectCard?.Invoke(item.code);
            }

            const float groupPadding = 10f;
            float stripWidth = 6f - ChainCardGap;
            var groups = Enumerable.Range(0, sources.Length).GroupBy(i => (sources[i].controller, sources[i].location));
            foreach (var group in groups)
            {
                var (controller, location) = group.Key;
                int count = group.Count();
                float width = count * ChainCardWidth + (count - 1) * ChainCardGap + groupPadding;
                var row = CreateChainGroup(location, controller != session.HumanPlayerIndex, width);
                stripWidth += width + ChainCardGap;
                foreach (int index in group)
                {
                    var tile = CreateCardStripTile(row, $"Ch_{index}", ChainCardWidth);
                    frames[index] = tile.GetComponent<Outline>();
                    var source = sources[index];
                    bool duplicate = sourceCounts[source] > 1;
                    var art = LotdUiKit.CreateRect("Card", tile, new Vector2(.055f, duplicate ? .16f : .035f),
                        new Vector2(.945f, .975f), Vector2.zero, Vector2.zero);
                    CompositeCardView.Create(art, "V", duelArtwork: true).SetCard(choices[index].code);

                    if (duplicate)
                    {
                        sourceNumbers.TryGetValue(source, out int number);
                        sourceNumbers[source] = ++number;
                        var caption = LotdUiKit.CreateRect("EffectNumber", tile, new Vector2(.04f, .01f),
                            new Vector2(.96f, .16f), Vector2.zero, Vector2.zero);
                        LotdUiKit.AddFittedText(caption, "Text", $"효과 {number}", 11, 8, TextAnchor.MiddleCenter, DuelStyle.Muted);
                    }
                    var button = tile.gameObject.AddComponent<Button>();
                    button.targetGraphic = tile.GetComponent<Image>();
                    button.onClick.AddListener(() => Select(index));
                    tile.gameObject.AddComponent<DuelPromptSelection>().OnSelected = () => Select(index);
                }
            }

            ResetCardStrip(_chainCardScroll, stripWidth);
            if (choices.Count > 0) Select(0);
        }

        private RectTransform CreateChainGroup(uint location, bool opponent, float width)
        {
            var group = LotdUiKit.FillRect("Source_" + location, _chainCards);
            group.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
            var groupTone = opponent ? new Color(.69f, .21f, .24f) : new Color(.23f, .43f, .77f);
            var edge = LotdUiKit.CreateRect("GroupFrame", group, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(0f, -21f));
            var tint = new Color(groupTone.r, groupTone.g, groupTone.b, .57f);
            LotdUiKit.AddImage(edge.gameObject, LotdUiKit.WhiteSprite, tint, false, false);
            var row = LotdUiKit.FillRect("Cards", edge, 5f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = ChainCardGap;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;

            var tab = LotdUiKit.CreateRect("LocationTab", group, Vector2.up, Vector2.up,
                new Vector2(0f, -21f), new Vector2(Mathf.Min(width, 112f), 0f));
            LotdUiKit.AddImage(tab.gameObject, DuelVisualAssets.PromptGroupLabel, groupTone, false, false);
            bool hasIcon = DuelPromptLocationIcon.Supports(location);
            if (hasIcon)
            {
                var icon = LotdUiKit.CreateRect("Icon", tab, Vector2.zero, Vector2.up,
                    new Vector2(4f, 1f), new Vector2(22f, -1f));
                DuelPromptLocationIcon.Add(icon, location, opponent);
            }
            var caption = LotdUiKit.CreateRect("Caption", tab, Vector2.zero, Vector2.one,
                new Vector2(hasIcon ? 25f : 4f, 0f), new Vector2(-3f, 0f));
            LotdUiKit.AddFittedText(caption, "Location", YgoConstants.GetLocationNameKR(location), 11, 8,
                TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            return row;
        }

        private static string CardSourceLabel(DuelSession session, SelectableCardItem card) =>
            $"{(card.controller == session.HumanPlayerIndex ? "내" : "상대")} {YgoConstants.GetLocationNameKR(card.location)}";

        private void ConfirmChainChoice()
        {
            if (_chainSelection < 0 || _curPrompt == null) return;
            if (_curPrompt.messageType == YgoConstants.MSG_SELECT_CHAIN &&
                _chainSelection < (_chainCandidateIndices?.Length ?? _curPrompt.candidates.Count))
                Submit(BitConverter.GetBytes(_chainCandidateIndices?[_chainSelection] ?? _chainSelection));
            else if (_curPrompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN)
                Submit(BitConverter.GetBytes(1));
        }


        private void BindFieldCardChoices(DuelSession session, DuelPrompt prompt)
        {
            if (_promptShade.FieldCardHitTest == null || prompt.playerId != session.HumanPlayerIndex ||
                prompt.messageType is not (YgoConstants.MSG_SELECT_CARD or YgoConstants.MSG_SELECT_SUM or
                    YgoConstants.MSG_SELECT_TRIBUTE or YgoConstants.MSG_SELECT_UNSELECT_CARD)) return;

            bool Add(SelectableCardItem card, int index)
            {
                var player = card.controller < session.Players.Length ? session.Players[card.controller] : null;
                bool onBoard = card.location switch
                {
                    YgoConstants.LOCATION_MZONE => card.sequence < 7 && player?.mzone[card.sequence] != null,
                    YgoConstants.LOCATION_SZONE => card.sequence < 6 && player?.szone[card.sequence] != null,
                    _ => false,
                };
                return onBoard && _fieldCardIndices.TryAdd((card.controller, card.location, card.sequence), index);
            }

            bool AddAll(List<SelectableCardItem> cards, int start, int direction = 1)
            {
                for (int i = 0; i < cards.Count; i++)
                    if (!Add(cards[i], start + direction * i)) return false;
                return true;
            }

            if (!AddAll(prompt.candidates, 0) || !AddAll(prompt.mustSelect, -1, -1) ||
                !AddAll(prompt.unselectCandidates, prompt.candidates.Count))
            {
                _fieldCardIndices.Clear();
                return;
            }

            _fieldMaterialPick = prompt.messageType != YgoConstants.MSG_SELECT_CARD ||
                session.LastHintMsg is 511 or 512 or 513 or 531 or 533;
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

            if (!IsFieldCardSelection)
            {
                RectTransform grid = BuildChoiceCardStrip(p.mustSelect.Count + p.candidates.Count);
                // Show locked must-select cards for SELECT_SUM
                for (int m = 0; m < p.mustSelect.Count; m++)
                    AddMultiSelectCardTile(grid, p.mustSelect[m], -1 - m, lockedMust: true, p, s);

                for (int i = 0; i < p.candidates.Count; i++)
                    AddMultiSelectCardTile(grid, p.candidates[i], i, lockedMust: false, p, s);
            }
            else if (p.messageType == YgoConstants.MSG_SELECT_CARD && string.IsNullOrEmpty(hint))
            {
                _modalTitle.text = "필드에서 카드를 선택하세요.";
            }

            _confirmBtn = AddFooterButton("선택 완료", OnConfirmMultiSelect, true);
            if (p.cancelable)
                AddFooterButton("취소", () => Submit(BitConverter.GetBytes(-1)), false);

            UpdateMultiSelectState();
        }

        private void AddMultiSelectCardTile(Transform grid, SelectableCardItem item, int idx, bool lockedMust, DuelPrompt p, DuelSession s)
        {
            uint code = s.GetVisibleSelectionCode(item);
            string detail = null;
            if (p.messageType == YgoConstants.MSG_SELECT_TRIBUTE && item.releaseParam > 1) detail = $"{item.releaseParam}장분";
            if (p.messageType == YgoConstants.MSG_SELECT_SUM)
            {
                uint v1 = item.sumParam & 0xFFFF;
                uint v2 = (item.sumParam >> 16) & 0xFFFF;
                detail = $"{(lockedMust ? "필수 · " : "")}{(v2 > 0 ? $"{v1}/{v2}" : $"{v1}")}";
            }

            var tile = CreateChoiceCardTile(grid, $"Tile_{idx}", code, item.location, item.controller != s.HumanPlayerIndex, detail);
            var mark = AddSelectionMark(tile, lockedMust);
            if (lockedMust) return;

            bool capped = p.messageType != YgoConstants.MSG_SELECT_SUM || !p.sumExactCountZeroAny && p.max > 0;
            var button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile.GetComponent<Image>();
            PreviewOnFocus(button, code);
            button.onClick.AddListener(() =>
            {
                if (!CanAnswerPrompt || !ReferenceEquals(_curPrompt, p)) return;
                if (code != 0) _onInspectCard?.Invoke(code);
                if (_selectedIndices.Contains(idx))
                {
                    if (capped && p.max == 1 && p.min == 1 && _confirmBtn.interactable)
                    {
                        _confirmBtn.onClick.Invoke();
                        return;
                    }
                    _selectedIndices.Remove(idx);
                }
                else
                {
                    if (capped && p.max == 1 && _selectedIndices.Count > 0)
                    {
                        var previous = grid.Find($"Tile_{_selectedIndices.First()}");
                        SetSelectionMark(previous.Find("Chk"), false);
                        _selectedIndices.Clear();
                    }
                    if (capped && _selectedIndices.Count >= p.max) return;
                    _selectedIndices.Add(idx);
                }
                LotdAudio.PlayClick();
                SetSelectionMark(mark, _selectedIndices.Contains(idx));
                UpdateMultiSelectState();
            });
        }

        private void UpdateMultiSelectState()
        {
            if (_curPrompt == null || _confirmBtn == null) return;
            bool valid = false;

            if (_curPrompt.messageType == YgoConstants.MSG_SELECT_CARD)
            {
                int cnt = _selectedIndices.Count;
                valid = cnt >= _curPrompt.min && cnt <= _curPrompt.max;
                string amount = _curPrompt.min == _curPrompt.max ? $"{_curPrompt.min}장" : $"{_curPrompt.min}~{_curPrompt.max}장";
                _modalSubtitle.text = $"{amount} 선택 · 현재 {cnt}장";
            }
            else if (_curPrompt.messageType == YgoConstants.MSG_SELECT_TRIBUTE)
            {
                int sumRel = _selectedIndices.Sum(i => Math.Max(1, (int)_curPrompt.candidates[i].releaseParam));
                valid = sumRel >= _curPrompt.min && _selectedIndices.Count <= _curPrompt.max;
                _modalSubtitle.text = $"릴리스 {_curPrompt.min}장분 필요 · 현재 {sumRel}장분 · 최대 {_curPrompt.max}장 선택";
            }
            else if (_curPrompt.messageType == YgoConstants.MSG_SELECT_SUM)
            {
                int target = (int)(_curPrompt.acc & 0xFFFF);
                valid = CheckSumSelectionValid(_curPrompt, _selectedIndices, target);
                _modalSubtitle.text = $"목표 합계 {target} · 필수 {_curPrompt.mustSelect.Count}장 · 선택 {_selectedIndices.Count}장";
            }

            _confirmBtn.interactable = valid;
        }

        private void OnConfirmMultiSelect()
        {
            if (_confirmBtn == null || !_confirmBtn.interactable) return;
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
            _modalTitle.text = CardDatabase.ResolveDescription(s.LastHintMsg) ?? "소재 / 대상 선택";
            _modalSubtitle.text = $"선택됨 {p.unselectCandidates.Count}장 · 추가 선택 가능 {p.candidates.Count}장";
            if (!IsFieldCardSelection)
            {
                RectTransform grid = BuildChoiceCardStrip(p.candidates.Count + p.unselectCandidates.Count);
                for (int j = 0; j < p.unselectCandidates.Count; j++)
                    AddImmediateClickTile(grid, s, p, p.unselectCandidates[j], p.candidates.Count + j, true);
                for (int i = 0; i < p.candidates.Count; i++)
                    AddImmediateClickTile(grid, s, p, p.candidates[i], i, false);
            }

            _confirmBtn = AddFooterButton("선택 완료", () =>
            {
                if (p.finishable && ReferenceEquals(_curPrompt, p)) Submit(BitConverter.GetBytes(-1));
            }, true);
            _confirmBtn.interactable = p.finishable;
            if (p.cancelable)
                AddFooterButton("취소", () => Submit(BitConverter.GetBytes(-1)), false);
        }

        private void AddImmediateClickTile(Transform grid, DuelSession session, DuelPrompt prompt, SelectableCardItem item,
            int nativeIndex, bool selected)
        {
            uint code = session.GetVisibleSelectionCode(item);
            var tile = CreateChoiceCardTile(grid, $"Imm_{nativeIndex}", code, item.location, item.controller != session.HumanPlayerIndex);
            if (selected) AddSelectionMark(tile, true);
            var button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile.GetComponent<Image>();
            PreviewOnFocus(button, code);
            button.onClick.AddListener(() =>
            {
                if (!CanAnswerPrompt || !ReferenceEquals(_curPrompt, prompt)) return;
                if (code != 0) _onInspectCard?.Invoke(code);
                LotdAudio.PlayClick();
                Submit(PackTwoInts(1, nativeIndex));
            });
        }

        private void RenderSelectCounter(DuelPrompt p)
        {
            _modalTitle.text = "카운터 제거 선택";
            _counterAlloc = new int[p.candidates.Count];

            RectTransform grid = BuildChoiceCardStrip(p.candidates.Count);
            var countLabels = new Text[p.candidates.Count];

            for (int i = 0; i < p.candidates.Count; i++)
            {
                int idx = i;
                var c = p.candidates[i];
                var tile = CreateCardStripTile(grid, $"Cnt_{i}", ChoiceCardWidth);
                var cRt = LotdUiKit.CreateRect("Card", tile, new Vector2(0.10f, 0.30f), new Vector2(0.90f, 0.95f), Vector2.zero, Vector2.zero);
                CompositeCardView.Create(cRt, "V", false, duelArtwork: true).SetCard(c.code);

                countLabels[i] = LotdUiKit.AddText(LotdUiKit.CreateRect("Lbl", tile, new Vector2(0.05f, 0.16f), new Vector2(0.95f, 0.30f), Vector2.zero, Vector2.zero),
                    "L", $"0 / {c.currentCounter}개", 14, TextAnchor.MiddleCenter, DuelStyle.Gold, FontStyle.Bold);

                var minus = LotdUiKit.CreateRect("M", tile, new Vector2(0.10f, 0.03f), new Vector2(0.46f, 0.15f), Vector2.zero, Vector2.zero);
                var decrease = DuelStyle.Button(minus, "B", "-", () =>
                {
                    if (_counterAlloc[idx] > 0) _counterAlloc[idx]--;
                    countLabels[idx].text = $"{_counterAlloc[idx]} / {c.currentCounter}개";
                    UpdateCounterConfirm();
                }, 16);
                var plus = LotdUiKit.CreateRect("P", tile, new Vector2(0.54f, 0.03f), new Vector2(0.90f, 0.15f), Vector2.zero, Vector2.zero);
                var increase = DuelStyle.Button(plus, "B", "+", () =>
                {
                    int curSum = _counterAlloc.Sum();
                    if (_counterAlloc[idx] < c.currentCounter && curSum < p.min) _counterAlloc[idx]++;
                    countLabels[idx].text = $"{_counterAlloc[idx]} / {c.currentCounter}개";
                    UpdateCounterConfirm();
                }, 16);
                PreviewOnFocus(decrease, c.code);
                PreviewOnFocus(increase, c.code);
            }

            _confirmBtn = AddFooterButton("확정", () =>
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
            UpdateCounterConfirm();
        }

        private void UpdateCounterConfirm()
        {
            if (_counterAlloc == null || _curPrompt == null) return;
            int sum = _counterAlloc.Sum();
            _modalSubtitle.text = $"제거할 총 카운터 수: {sum} / {_curPrompt.min}";
            _confirmBtn.interactable = sum == _curPrompt.min;
        }

        private void RenderSortCards(DuelSession session, DuelPrompt p)
        {
            // The core sends top cards in descending sequence and bottom cards in ascending sequence.
            bool deckOrder = !p.isChainSort && p.candidates.Count > 1 &&
                p.candidates.All(card => card.location == YgoConstants.LOCATION_DECK &&
                    card.controller == p.candidates[0].controller) &&
                p.candidates[0].sequence != p.candidates[p.candidates.Count - 1].sequence;
            string deckEdge = deckOrder && p.candidates[0].sequence < p.candidates[p.candidates.Count - 1].sequence
                ? "아래" : "위";
            string deckName = deckOrder && p.candidates[0].controller != session.HumanPlayerIndex ? "상대 덱" : "덱";
            _modalTitle.text = p.isChainSort ? "체인에 올릴 순서" :
                deckOrder ? $"{deckName} {deckEdge}쪽 카드 순서" : "카드 순서 지정";
            _modalSubtitle.text = p.isChainSort ? "왼쪽부터 발동 · 오른쪽부터 처리" :
                deckOrder ? $"왼쪽이 덱 맨 {deckEdge} · 화살표로 카드 이동" : "왼쪽부터 1번 · 화살표로 카드 이동";
            _sortOrder = Enumerable.Range(0, p.candidates.Count).ToArray();

            RectTransform grid = BuildChoiceCardStrip(p.candidates.Count);
            var scroll = grid.GetComponentInParent<ScrollRect>();
            void RebuildTiles(int focusPosition = -1, int focusDirection = 0)
            {
                ClearChildren(grid);
                Button restore = null;

                for (int pos = 0; pos < _sortOrder.Length; pos++)
                {
                    int position = pos;
                    uint code = p.candidates[_sortOrder[pos]].code;
                    var tile = CreateCardStripTile(grid, $"Sort_{pos}", ChoiceCardWidth);
                    string rank = p.isChainSort ? $"발동 {pos + 1} · 처리 {_sortOrder.Length - pos}" :
                        deckOrder && pos == 0 ? $"1번 · 맨 {deckEdge}" : $"{pos + 1}번";
                    var order = LotdUiKit.CreateRect("Order", tile, new Vector2(.042f, .805f),
                        new Vector2(.958f, .966f), Vector2.zero, Vector2.zero);
                    LotdUiKit.AddImage(order.gameObject, LotdUiKit.WhiteSprite, DuelStyle.HeaderColor, false, false);
                    LotdUiKit.AddFittedText(order, "Text", rank, 16, 9, TextAnchor.MiddleCenter,
                        pos == 0 && !p.isChainSort ? DuelStyle.Gold : LotdUiKit.TextPrimary, FontStyle.Bold);

                    var art = LotdUiKit.CreateRect("Card", tile, new Vector2(.08f, .219f),
                        new Vector2(.92f, .79f), Vector2.zero, Vector2.zero);
                    CompositeCardView.Create(art, "V", false, duelArtwork: true).SetCard(code, faceDown: code == 0);

                    Button MoveButton(int direction)
                    {
                        bool left = direction < 0;
                        int other = position + direction;
                        var holder = LotdUiKit.CreateRect(left ? "Earlier" : "Later", tile,
                            new Vector2(left ? .07f : .53f, .031f), new Vector2(left ? .47f : .93f, .196f),
                            Vector2.zero, Vector2.zero);
                        var sprite = LotdUiKit.LoadSprite(left ? "LOTD/Shared/arrow_left1" : "LOTD/Shared/arrow_right1");
                        var button = DuelStyle.Button(holder, "B", sprite != null ? "" : left ? "◀" : "▶", () =>
                        {
                            if (!CanAnswerPrompt || !ReferenceEquals(_curPrompt, p) ||
                                other < 0 || other >= _sortOrder.Length) return;
                            (_sortOrder[position], _sortOrder[other]) = (_sortOrder[other], _sortOrder[position]);
                            RebuildTiles(other, direction);
                        }, 15);
                        button.interactable = other >= 0 && other < _sortOrder.Length;
                        if (sprite != null)
                        {
                            var icon = LotdUiKit.CreateRect("Arrow", button.transform, new Vector2(.23f, .12f),
                                new Vector2(.77f, .88f), Vector2.zero, Vector2.zero);
                            LotdUiKit.AddImage(icon.gameObject, sprite,
                                button.interactable ? Color.white : new Color(1f, 1f, 1f, .26f), true, false);
                        }
                        SelectionHandler(button).OnSelected = () =>
                        {
                            RevealSortCard(scroll, tile);
                            if (code != 0) _onInspectCard?.Invoke(code);
                        };
                        return button;
                    }

                    var earlier = MoveButton(-1);
                    var later = MoveButton(1);
                    if (pos == focusPosition)
                    {
                        var preferred = focusDirection < 0 ? earlier : later;
                        restore = preferred.interactable ? preferred : focusDirection < 0 ? later : earlier;
                    }
                }
                PrepareFocusControls(grid, OnModalCloseClicked);
                if (restore != null)
                {
                    Canvas.ForceUpdateCanvases();
                    SetFocus(restore.gameObject);
                }
            }
            RebuildTiles();

            AddFooterButton("순서 확정", () =>
            {
                byte[] res = new byte[_sortOrder.Length];
                for (int i = 0; i < _sortOrder.Length; i++) res[_sortOrder[i]] = (byte)i;
                Submit(res);
            }, true);
            AddFooterButton("원래 순서로 확정", () => Submit(new byte[] { unchecked((byte)-1) }), false);
        }

        private static void RevealSortCard(ScrollRect scroll, RectTransform tile)
        {
            var viewport = scroll.viewport;
            float width = viewport.rect.width;
            if (width <= 0f || scroll.content.rect.width <= width) return;

            float left = viewport.InverseTransformPoint(tile.TransformPoint(tile.rect.min)).x;
            float right = viewport.InverseTransformPoint(tile.TransformPoint(tile.rect.max)).x;
            float shift = left < viewport.rect.xMin ? viewport.rect.xMin - left :
                right > viewport.rect.xMax ? viewport.rect.xMax - right : 0f;
            if (Mathf.Abs(shift) < .1f) return;
            scroll.StopMovement();
            scroll.horizontalNormalizedPosition -= shift / (scroll.content.rect.width - width);
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

            _confirmBtn = AddFooterButton("선언 확정", () => Submit(BitConverter.GetBytes(chosenMask)), true);
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

            _confirmBtn = AddFooterButton("선언 확정", () => Submit(BitConverter.GetBytes(chosenMask)), true);
            _confirmBtn.interactable = false;
        }

        private void RenderRockPaperScissors()
        {
            _modalTitle.text = "가위바위보";
            _modalSubtitle.text = "낼 패를 선택하세요.";

            var choices = new (int val, string label)[]
            {
                (1, "가위"),
                (2, "바위"),
                (3, "보"),
            };
            float w = 1f / choices.Length;
            for (int i = 0; i < choices.Length; i++)
            {
                var ch = choices[i];
                var slot = LotdUiKit.CreateRect($"Rps_{ch.val}", _modalBody,
                    new Vector2(i * w + .037f, .22f), new Vector2((i + 1) * w - .037f, .78f), Vector2.zero, Vector2.zero);
                DuelStyle.Button(slot, "B", ch.label, () => Submit(BitConverter.GetBytes(ch.val)), 22);
            }
        }

        private void RenderAnnounceCard(DuelPrompt p)
        {
            _modalTitle.text = "카드명 선언";

            uint[] validCodes = YgoNativeBridge.FilterDeclarableCards(p.options.ToArray(), 20000);
            uint pickedCode = 0;
            Outline pickedOutline = null;
            var confirm = AddFooterButton("선언 확정", () =>
            {
                if (pickedCode != 0) Submit(BitConverter.GetBytes(pickedCode));
            }, true);

            var inpRt = LotdUiKit.CreateRect("Search", _modalBody, new Vector2(0.02f, 0.87f), new Vector2(0.78f, 0.99f), Vector2.zero, Vector2.zero);
            var inp = LotdUiKit.CreateInputField(inpRt, "Inp", "카드 이름 또는 카드 번호", 16);
            var keyboardSlot = LotdUiKit.CreateRect("Keyboard", _modalBody,
                new Vector2(.80f, .87f), new Vector2(.98f, .99f), Vector2.zero, Vector2.zero);
            DuelStyle.Button(keyboardSlot, "B", "화상 입력", () => OpenPadKeyboard(inp), 14);

            var gridHolder = LotdUiKit.CreateRect("List", _modalBody, new Vector2(0.0f, 0.0f), new Vector2(1f, 0.84f), Vector2.zero, Vector2.zero);
            var scroll = CreateHorizontalCardGrid(gridHolder, out Transform grid, 128, 178).GetComponent<ScrollRect>();
            var empty = LotdUiKit.AddFittedText(gridHolder, "Empty", "조건에 맞는 카드가 없습니다.", 18, 12,
                TextAnchor.MiddleCenter, DuelStyle.Muted);

            void Refresh(string query)
            {
                const int maxVisible = 40;
                bool restoreFocus = FocusIsInside(grid.gameObject);
                pickedCode = 0;
                pickedOutline = null;
                confirm.interactable = false;
                ClearChildren(grid);
                scroll.StopMovement();
                scroll.content.anchoredPosition = Vector2.zero;
                var filter = new DeckCardFilter(query, "");
                int matches = 0;
                foreach (uint code in validCodes)
                {
                    string name = CardDatabase.GetName(code);
                    if (!filter.MatchesNameOrPasscode(code, name)) continue;
                    if (++matches > maxVisible) continue;

                    var tile = DuelStyle.Panel(grid, $"Dec_{code}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    var cRt = LotdUiKit.CreateRect("C", tile, new Vector2(0.10f, 0.22f), new Vector2(0.90f, 0.95f), Vector2.zero, Vector2.zero);
                    CompositeCardView.Create(cRt, "V", false, duelArtwork: true).SetCard(code);
                    LotdUiKit.AddFittedText(LotdUiKit.CreateRect("L", tile, new Vector2(0.04f, 0.02f),
                        new Vector2(0.96f, 0.20f), Vector2.zero, Vector2.zero), "T", name, 12, 8,
                        TextAnchor.MiddleCenter, LotdUiKit.TextPrimary);

                    var outline = tile.GetComponent<Outline>();
                    var btn = tile.gameObject.AddComponent<Button>();
                    btn.targetGraphic = tile.GetComponent<Image>();
                    PreviewOnFocus(btn, code);
                    var trig = tile.gameObject.AddComponent<CardDragTrigger>();
                    trig.OnPointerEnterAction = () => _onInspectCard?.Invoke(code);
                    btn.onClick.AddListener(() =>
                    {
                        if (!CanAnswerPrompt || !ReferenceEquals(_curPrompt, p) || !tile.gameObject.activeInHierarchy) return;
                        if (pickedOutline != null) pickedOutline.effectColor = DuelStyle.BorderColor;
                        pickedOutline = outline;
                        pickedOutline.effectColor = DuelStyle.Gold;
                        pickedCode = code;
                        confirm.interactable = true;
                        _onInspectCard?.Invoke(code);
                        _modalSubtitle.text = $"선택한 카드 · {name}";
                    });
                }
                empty.gameObject.SetActive(matches == 0);
                _modalSubtitle.text = matches > maxVisible
                    ? $"후보 {matches:N0}장 · 처음 {maxVisible}장 표시 · 카드 이름이나 번호로 좁혀 보세요."
                    : $"후보 {matches:N0}장";
                PrepareFocusControls(grid, OnModalCloseClicked);
                Canvas.ForceUpdateCanvases();
                if (restoreFocus)
                    SetFocus((FirstFocus(grid) ?? inp).gameObject);
            }
            inp.onValueChanged.AddListener(Refresh);
            Refresh("");
        }

        // ==================== ZONE VIEWER ====================
        private void BuildZoneViewer(Transform parent)
        {
            var dim = LotdUiKit.FillRect("Overlay_ZoneView", parent);
            ZoneViewRoot = dim.gameObject;
            LotdUiKit.AddImage(dim.gameObject, LotdUiKit.WhiteSprite, new Color(0.015f, 0.022f, 0.038f, 0.40f));
            var dismiss = dim.gameObject.AddComponent<Button>();
            dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
            dismiss.onClick.AddListener(CloseZoneViewer);

            var win = _zoneWindow = DuelStyle.Panel(dim, "Win",
                new Vector2(0.226f, 0.205f), new Vector2(0.972f, 0.615f),
                fill: new Color(.039f, .071f, .093f, .97f));
            win.gameObject.AddComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };

            var hdr = LotdUiKit.CreateRect("Hdr", win, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -53f), Vector2.zero);
            LotdUiKit.AddImage(hdr.gameObject, LotdUiKit.WhiteSprite, DuelStyle.HeaderColor);
            var accent = LotdUiKit.CreateRect("Accent", hdr, new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -2f), Vector2.zero);
            LotdUiKit.AddImage(accent.gameObject, LotdUiKit.WhiteSprite, DuelStyle.Player, false, false);
            var titleBand = LotdUiKit.CreateRect("TitleBand", hdr, new Vector2(0.025f, 0.12f), new Vector2(0.80f, 0.88f),
                Vector2.zero, Vector2.zero);
            var zoneIcon = LotdUiKit.CreateRect("LocationIcon", titleBand, Vector2.zero, Vector2.up,
                Vector2.zero, new Vector2(31f, 0f));
            _zoneIcon = LotdUiKit.AddImage(zoneIcon.gameObject, null, Color.white, true, false);
            _zoneTitle = LotdUiKit.AddFittedText(titleBand, "T", "존 확인", 20, 11, TextAnchor.MiddleLeft,
                DuelStyle.Gold, FontStyle.Bold);

            var closeRt = LotdUiKit.CreateRect("Close", hdr, new Vector2(0.82f, 0.15f), new Vector2(0.98f, 0.85f), Vector2.zero, Vector2.zero);
            _zoneClose = DuelStyle.Button(closeRt, "B", "✕ 닫기", CloseZoneViewer, 16);

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
            bool restoreFocus = FocusIsInside(ZoneViewRoot);
            ZoneViewRoot.SetActive(false);
            _promptInput.interactable = true;
            if (restoreFocus) SetFocus(IsShowingField ? _fieldViewButton.gameObject : _focusBeforeZone);
            _focusBeforeZone = null;
            ClearChildren(_zoneGrid);
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
            Func<bool> canUseCardActions = null,
            Sprite icon = null,
            bool actionsOnly = false,
            bool sidePanel = false)
        {
            StartZoneViewer(sidePanel);
            int count = cards?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                var card = cards[newestFirst ? count - 1 - i : i];
                if (card == null) continue;
                bool hidden = card.code == 0 || hideFaceDown && !card.IsFaceUp && !card.isPublic;
                bool actionable = !hidden && onOpenCardActions != null && hasCardActions?.Invoke(card) == true;
                if (actionsOnly && !actionable) continue;
                AddZoneCardTile(hidden ? 0u : card.code, hidden, card, actionable, onOpenCardActions, canUseCardActions, actionsOnly);
            }
            FinishZoneViewer(title, icon);
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

        private void StartZoneViewer(bool sidePanel = false)
        {
            CloseZoneViewer();
            _focusBeforeZone = EventSystem.current?.currentSelectedGameObject;
            _promptInput.interactable = false;
            ZoneViewRoot.SetActive(true);
            _zoneWindow.anchorMin = sidePanel ? new Vector2(.865f, .171f) : new Vector2(.226f, .205f);
            _zoneWindow.anchorMax = sidePanel ? new Vector2(.986f, .871f) : new Vector2(.972f, .615f);
            ZoneViewRoot.GetComponent<Image>().color = new Color(.015f, .022f, .038f, sidePanel ? .14f : .40f);

            var close = (RectTransform)_zoneClose.transform.parent;
            close.anchorMin = new Vector2(sidePanel ? .77f : .82f, .15f);
            _zoneClose.GetComponentInChildren<Text>().text = sidePanel ? "✕" : "✕ 닫기";
            var grid = _zoneGrid.GetComponent<GridLayoutGroup>();
            grid.constraint = sidePanel ? GridLayoutGroup.Constraint.FixedColumnCount : GridLayoutGroup.Constraint.Flexible;
            grid.constraintCount = 1;
            grid.childAlignment = sidePanel ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;
            grid.padding = sidePanel ? new RectOffset(4, 4, 6, 6) : new RectOffset(10, 10, 10, 10);
            grid.cellSize = sidePanel
                ? new Vector2(Mathf.Clamp(_zoneScroll.viewport.rect.width - 10f, 52f, 116f), 160f)
                : new Vector2(118f, 174f);
        }

        private void FinishZoneViewer(string title, Sprite icon = null)
        {
            int count = _zoneGrid.childCount;
            _zoneTitle.text = $"{title}   ·   {count}장";
            _zoneIcon.sprite = icon;
            _zoneIcon.enabled = icon != null;
            _zoneTitle.rectTransform.offsetMin = icon != null ? new Vector2(34f, 0f) : Vector2.zero;
            _zoneEmpty.gameObject.SetActive(count == 0);
            _zoneScroll.StopMovement();
            _zoneGrid.anchoredPosition = Vector2.zero;
            ZoneViewRoot.transform.SetAsLastSibling();
            CardAnimationManager.Instance?.BringToFront();
            PrepareFocusControls(ZoneViewRoot.transform, CloseZoneViewer);
            Canvas.ForceUpdateCanvases();
            SetFocus(FirstFocus(_zoneGrid)?.gameObject ?? _zoneClose.gameObject);
            UiEntranceMotion.Play(ZoneViewRoot, duration: .13f);
        }

        private void AddZoneCardTile(
            uint code,
            bool faceDown,
            LiveCardState live,
            bool hasActions = false,
            Action<LiveCardState> onOpenCardActions = null,
            Func<bool> canUseCardActions = null,
            bool actionSelection = false)
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
                PreviewOnFocus(btn, code);
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
                string actionText = actionSelection
                    ? live.location == YgoConstants.LOCATION_EXTRA ? "선택" : "효과 발동"
                    : "행동 확인";
                var action = DuelStyle.Button(actRt, "B", actionText, () =>
                {
                    if (!IsZoneViewerOpen || canUseCardActions?.Invoke() == false) return;
                    _onInspectCard?.Invoke(code);
                    CloseZoneViewer();
                    onOpenCardActions(live);
                }, 11, true);
                PreviewOnFocus(action, code);
            }
        }

        // ==================== MATCH RESULT ====================
        private void BuildResultOverlay(Transform parent)
        {
            var dim = LotdUiKit.FillRect("Overlay_MatchResult", parent);
            ResultRoot = dim.gameObject;
            LotdUiKit.AddImage(dim.gameObject, LotdUiKit.WhiteSprite, new Color(0f, 0.021f, 0.036f, 0.66f));

            var crown = LotdUiKit.CreateRect("WinnerCrown", dim, new Vector2(.448f, .685f), new Vector2(.552f, .825f),
                Vector2.zero, Vector2.zero);
            _resCrown = LotdUiKit.AddImage(crown.gameObject, DuelVisualAssets.WinnerCrown, Color.white, true, false);
            var hero = LotdUiKit.CreateRect("ResultTitle", dim, new Vector2(.17f, .485f), new Vector2(.83f, .698f),
                Vector2.zero, Vector2.zero);
            _resTitleArt = LotdUiKit.AddImage(hero.gameObject, null, Color.white, true, false);
            _resHeadline = LotdUiKit.AddFittedText(LotdUiKit.FillRect("Headline", hero), "Value", "", 87, 29,
                TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            DuelStyle.Shadow(_resHeadline, 2f);
            var rule = LotdUiKit.CreateRect("TitleRule", dim, new Vector2(.25f, .477f), new Vector2(.75f, .477f),
                Vector2.zero, new Vector2(0f, 1f));
            _resTitleRule = LotdUiKit.AddImage(rule.gameObject, LotdUiKit.WhiteSprite, DuelStyle.Gold, false, false);
            _resStats = LotdUiKit.AddFittedText(LotdUiKit.CreateRect("Summary", dim, new Vector2(.19f, .34f),
                new Vector2(.81f, .469f), Vector2.zero, Vector2.zero), "Value", "", 18, 11,
                TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);
            DuelStyle.Shadow(_resStats);
            var unlockSlot = LotdUiKit.CreateRect("OpponentUnlock", dim, new Vector2(.17f, .244f),
                new Vector2(.83f, .332f), Vector2.zero, Vector2.zero);
            _resOpponentUnlock = LotdUiKit.AddFittedText(unlockSlot, "Value", "", 24, 14,
                TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);
            DuelStyle.Shadow(_resOpponentUnlock);

            var r1 = LotdUiKit.CreateRect("Rematch", dim, new Vector2(.22f, .169f), new Vector2(.397f, .234f), Vector2.zero, Vector2.zero);
            _rematchButton = DuelStyle.Button(r1, "B", "재대결", () => { HideResult(); _resRematch?.Invoke(); }, 18, gold: true);

            var r2 = LotdUiKit.CreateRect("OpponentSelect", dim, new Vector2(.411f, .169f), new Vector2(.589f, .234f), Vector2.zero, Vector2.zero);
            DuelStyle.Button(r2, "B", "상대 선택", () => { HideResult(); _resOppSel?.Invoke(); }, 18);

            var r3 = LotdUiKit.CreateRect("MainMenu", dim, new Vector2(.603f, .169f), new Vector2(.78f, .234f), Vector2.zero, Vector2.zero);
            DuelStyle.Button(r3, "B", "메인 메뉴", () => { HideResult(); _resMenu?.Invoke(); }, 18);

            ResultRoot.SetActive(false);
        }

        public void ShowResult(bool playerWon, bool isDraw, OpponentCharacter opp, int turns, int myLp, int oppLp,
            Action onRematch, Action onOppSelect, Action onMenu, string rewardText = "",
            OpponentCharacter unlockedOpponent = null, PresetDeckInfo unlockedDeck = null)
        {
            _resRematch = onRematch;
            _resOppSel = onOppSelect;
            _resMenu = onMenu;

            string headline = isDraw ? "무승부" : playerWon ? "승리" : "패배";
            _resTitleArt.sprite = isDraw ? DuelVisualAssets.DuelDraw :
                playerWon ? DuelVisualAssets.DuelVictory : DuelVisualAssets.DuelDefeat;
            _resTitleArt.enabled = _resTitleArt.sprite != null;
            _resHeadline.enabled = !_resTitleArt.enabled;
            _resHeadline.text = headline;
            _resTitleRule.color = _resHeadline.color = isDraw ? DuelStyle.Muted :
                playerWon ? DuelStyle.Gold : DuelStyle.Opponent;
            _resCrown.enabled = playerWon && !isDraw && DuelVisualAssets.WinnerCrown != null;

            if (!isDraw && playerWon) LotdAudio.PlayWin();
            if (!isDraw && !playerWon) LotdAudio.PlayDamage();

            string oppName = opp != null ? opp.name : "AI 듀얼리스트";
            string result = _resTitleArt.enabled ? $"{headline}  ·  " : "";
            _resStats.text = $"{result}{oppName}  ·  {turns}턴\n내 LP {myLp}  /  상대 LP {oppLp}";
            if (!string.IsNullOrEmpty(rewardText)) _resStats.text += $"\n{rewardText}";
            string unlockText = unlockedOpponent == null ? "" : $"새 상대 해금 · {unlockedOpponent.name}";
            if (unlockedDeck != null)
            {
                int level = OpponentDeckLevels.GetLevel(unlockedDeck.charId, unlockedDeck.id);
                if (unlockText.Length != 0) unlockText += "\n";
                unlockText += $"레벨 {level} 덱 해금 · {unlockedDeck.title}";
            }
            _resOpponentUnlock.text = unlockText;
            _resOpponentUnlock.gameObject.SetActive(unlockText.Length != 0);
            ResultRoot.transform.SetAsLastSibling();
            ResultRoot.SetActive(true);
            CardAnimationManager.Instance?.BringToFront();
            PrepareFocusControls(ResultRoot.transform, TriggerMenu);
            SetFocus(_rematchButton.gameObject);
            UiEntranceMotion.Play(ResultRoot, duration: .22f);
        }

        // ==================== UI HELPERS ====================
        private void FocusPrompt()
        {
            if (EventSystem.current == null) return;
            Canvas.ForceUpdateCanvases();
            SetFocus(PreferredPromptControl().gameObject);
        }

        private Selectable PreferredPromptControl()
        {
            if (_chainPrompt)
            {
                return _chainCards.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == $"Ch_{_chainSelection}")
                    ?? _fieldViewButton;
            }
            return FirstFocus(_modalBody) ?? FirstFocus(_modalFooter) ?? _fieldViewButton;
        }

        private static bool CanNavigate(Selectable control, Transform scope) =>
            control != null && control.transform.IsChildOf(scope) && control.isActiveAndEnabled &&
            control.IsInteractable() && control.navigation.mode != Navigation.Mode.None && (control is Button || control is InputField);

        private static Selectable FirstFocus(Transform parent) =>
            parent.GetComponentsInChildren<Selectable>().FirstOrDefault(control => CanNavigate(control, parent));

        private void PreviewOnFocus(Selectable control, uint code)
        {
            if (code != 0) SelectionHandler(control).OnSelected = () => _onInspectCard?.Invoke(code);
        }

        private static DuelPromptSelection SelectionHandler(Selectable control) =>
            control.GetComponent<DuelPromptSelection>() ?? control.gameObject.AddComponent<DuelPromptSelection>();

        private static void PrepareFocusControls(Transform parent, Action onCancel)
        {
            foreach (var control in parent.GetComponentsInChildren<Selectable>())
            {
                var palette = control.colors;
                palette.selectedColor = palette.highlightedColor;
                control.colors = palette;
                SelectionHandler(control).OnCancelled = onCancel;
            }
        }

        private static bool FocusIsInside(GameObject overlay)
        {
            var current = EventSystem.current?.currentSelectedGameObject;
            return current != null && current.transform.IsChildOf(overlay.transform);
        }

        private static void SetFocus(GameObject target)
        {
            var events = EventSystem.current;
            if (events == null) return;
            if (target != null && (!target.TryGetComponent<Selectable>(out var control) ||
                !control.isActiveAndEnabled || !control.IsInteractable()))
                target = null;
            events.SetSelectedGameObject(target);
        }

        private Button AddFooterButton(string label, Action onClick, bool gold)
        {
            var slot = LotdUiKit.CreateRect($"Btn_{label}", _modalFooter,
                new Vector2(gold ? .52f : 0f, .03f), new Vector2(gold ? 1f : .48f, .97f), Vector2.zero, Vector2.zero);
            return DuelStyle.Button(slot, "B", label, onClick, 16, gold);
        }

        private static void AddSelectionFrame(RectTransform parent)
        {
            if (DuelVisualAssets.ChainFrame == null) return;
            var frame = LotdUiKit.FillRect("Frame", parent, -17f);
            var image = LotdUiKit.AddImage(frame.gameObject, DuelVisualAssets.ChainFrame, Color.white, false, false);
            image.type = Image.Type.Sliced;
            image.fillCenter = false;
            image.pixelsPerUnitMultiplier = 2.4f;
        }

        private RectTransform BuildChoiceCardStrip(int count)
        {
            var strip = LotdUiKit.FillRect("Cards", _modalBody);
            ScrollRect scroll = CreateCardStrip(strip, out RectTransform cards, ChoiceCardGap);
            ResetCardStrip(scroll, count, ChoiceCardWidth, ChoiceCardGap);
            return cards;
        }

        private RectTransform CreateChoiceCardTile(Transform parent, string name, uint code, uint location,
            bool opponent, string detail = null)
        {
            var tile = CreateCardStripTile(parent, name, ChoiceCardWidth);
            var art = LotdUiKit.CreateRect("Card", tile, new Vector2(.05f, .292f), new Vector2(.95f, .975f),
                Vector2.zero, Vector2.zero);
            CompositeCardView.Create(art, "V", duelArtwork: true).SetCard(code, faceDown: code == 0);

            var title = LotdUiKit.CreateRect("Name", tile, new Vector2(.04f, .174f), new Vector2(.96f, .29f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(title, "Text", code != 0 ? CardDatabase.GetName(code) : "뒷면 카드", 12, 8,
                TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            AddChoiceCardSource(tile, location, opponent, detail);
            if (code != 0)
                tile.gameObject.AddComponent<CardDragTrigger>().OnPointerEnterAction = () => _onInspectCard?.Invoke(code);
            return tile;
        }

        private static void AddChoiceCardSource(Transform tile, uint location, bool opponent, string detail)
        {
            bool hasIcon = DuelPromptLocationIcon.Supports(location);
            bool hasDetail = !string.IsNullOrEmpty(detail);
            if (!hasIcon && !hasDetail) return;

            var badge = LotdUiKit.CreateRect("Source", tile, new Vector2(hasDetail ? .065f : .32f, .02f),
                new Vector2(hasDetail ? .935f : .68f, .166f), Vector2.zero, Vector2.zero);
            var tone = DuelStyle.Side(opponent);
            LotdUiKit.AddImage(badge.gameObject, LotdUiKit.WhiteSprite, new Color(tone.r, tone.g, tone.b, .17f), false, false);
            if (hasIcon)
            {
                var icon = LotdUiKit.CreateRect("LocationIcon", badge, Vector2.zero,
                    hasDetail ? new Vector2(.32f, 1f) : Vector2.one, new Vector2(2f, 1f), new Vector2(-2f, -1f));
                DuelPromptLocationIcon.Add(icon, location, opponent);
            }
            if (hasDetail)
            {
                var caption = LotdUiKit.CreateRect("Detail", badge, new Vector2(hasIcon ? .32f : 0f, 0f), Vector2.one,
                    Vector2.zero, new Vector2(-2f, 0f));
                LotdUiKit.AddFittedText(caption, "Text", detail, 11, 8, TextAnchor.MiddleCenter, DuelStyle.Muted);
            }
        }

        private static RectTransform CreateCardStripTile(Transform parent, string name, float width)
        {
            var tile = DuelStyle.Panel(parent, name, Vector2.zero, Vector2.one);
            tile.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
            return tile;
        }

        private static Transform AddSelectionMark(RectTransform tile, bool selected)
        {
            var mark = LotdUiKit.CreateRect("Chk", tile, new Vector2(.68f, .76f), new Vector2(.94f, .96f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(mark.gameObject, LotdUiKit.LoadSprite("LOTD/Icons/ICON_ID_CHECKMARK"), DuelStyle.Gold, true, false);
            SetSelectionMark(mark, selected);
            return mark;
        }

        private static void SetSelectionMark(Transform mark, bool selected)
        {
            mark.gameObject.SetActive(selected);
            mark.parent.GetComponent<Outline>().effectColor = selected ? DuelStyle.Gold : DuelStyle.BorderColor;
        }

        private static ScrollRect CreateCardStrip(RectTransform cards, out RectTransform content, float gap)
        {
            var scroll = cards.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            var viewport = LotdUiKit.CreateRect("Viewport", cards, new Vector2(0f, .085f), Vector2.one, Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(viewport.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = LotdUiKit.CreateRect("Content", viewport, Vector2.zero, Vector2.up, Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0f, .5f);
            var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = gap;
            layout.padding = new RectOffset(3, 3, 3, 3);
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;
            content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = viewport;

            var track = LotdUiKit.CreateRect("Scrollbar", cards, Vector2.zero, new Vector2(1f, .049f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(track.gameObject, LotdUiKit.WhiteSprite, new Color(.08f, .13f, .18f));
            var slide = LotdUiKit.FillRect("SlidingArea", track);
            var handle = LotdUiKit.FillRect("Handle", slide);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.LeftToRight;
            scrollbar.targetGraphic = LotdUiKit.AddImage(handle.gameObject, LotdUiKit.WhiteSprite, DuelStyle.Player);
            scrollbar.handleRect = handle;
            scroll.horizontalScrollbar = scrollbar;
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }

        private static void ResetCardStrip(ScrollRect scroll, int count, float width, float gap) =>
            ResetCardStrip(scroll, 6f + count * width + Mathf.Max(0, count - 1) * gap);

        private static void ResetCardStrip(ScrollRect scroll, float stripWidth)
        {
            scroll.StopMovement();
            scroll.content.anchoredPosition = Vector2.zero;
            scroll.horizontalScrollbar.gameObject.SetActive(stripWidth > scroll.viewport.rect.width);
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

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class DuelPromptLocationIcon : MaskableGraphic
    {
        private uint _location;

        internal static bool Supports(uint location) => (location & YgoConstants.LOCATION_OVERLAY) != 0 ||
            location is YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_DECK or YgoConstants.LOCATION_EXTRA
                or YgoConstants.LOCATION_GRAVE or YgoConstants.LOCATION_REMOVED or YgoConstants.LOCATION_ONFIELD
                or YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE or YgoConstants.LOCATION_FZONE or YgoConstants.LOCATION_PZONE;

        internal static void Add(RectTransform holder, uint location, bool opponent)
        {
            var sprite = DuelVisualAssets.PromptZoneIcon(location, opponent);
            if (sprite != null)
            {
                LotdUiKit.AddImage(holder.gameObject, sprite, Color.white, true, false);
                return;
            }

            var glyph = holder.gameObject.AddComponent<DuelPromptLocationIcon>();
            glyph._location = (location & YgoConstants.LOCATION_OVERLAY) != 0 ? YgoConstants.LOCATION_OVERLAY : location;
            glyph.color = DuelStyle.Side(opponent);
            glyph.raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            float scale = Mathf.Min(rect.width, rect.height) / 24f;
            if (scale <= 0f) return;

            void Line(float x1, float y1, float x2, float y2)
            {
                Vector2 a = rect.center + new Vector2(x1 - 12f, y1 - 12f) * scale;
                Vector2 b = rect.center + new Vector2(x2 - 12f, y2 - 12f) * scale;
                var delta = (b - a).normalized;
                var normal = new Vector2(-delta.y, delta.x) * (.85f * scale);
                var cap = delta * (.65f * scale);
                DuelMat.AddQuad(mesh, a - cap - normal, a - cap + normal, b + cap + normal, b + cap - normal,
                    color, color, Vector2.zero, Vector2.zero);
            }

            void Path(params (float x, float y)[] points)
            {
                for (int i = 1; i < points.Length; i++)
                    Line(points[i - 1].x, points[i - 1].y, points[i].x, points[i].y);
            }

            void Box(float left, float bottom, float right, float top) =>
                Path((left, bottom), (left, top), (right, top), (right, bottom), (left, bottom));

            switch (_location)
            {
                case YgoConstants.LOCATION_HAND:
                    Path((4, 5), (2, 15), (7, 18));
                    Path((20, 5), (22, 15), (17, 18));
                    Box(8, 4, 16, 20);
                    break;
                case YgoConstants.LOCATION_DECK:
                    Box(4, 10, 20, 19);
                    Line(4, 7, 20, 7);
                    Line(4, 4, 20, 4);
                    break;
                case YgoConstants.LOCATION_EXTRA:
                    Box(5, 4, 16, 18);
                    Path((8, 21), (20, 21), (20, 8));
                    Path((10.5f, 8), (13, 11), (10.5f, 14), (8, 11), (10.5f, 8));
                    break;
                case YgoConstants.LOCATION_GRAVE:
                    Path((6, 5), (6, 15), (8, 19), (12, 21), (16, 19), (18, 15), (18, 5));
                    Line(3, 5, 21, 5);
                    Line(12, 8, 12, 16);
                    Line(9, 13, 15, 13);
                    break;
                case YgoConstants.LOCATION_REMOVED:
                    Path((8, 21), (16, 21), (21, 16), (21, 8), (16, 3), (8, 3), (3, 8), (3, 16), (8, 21));
                    Line(5, 5, 19, 19);
                    break;
                case YgoConstants.LOCATION_OVERLAY:
                    Path((3, 15), (12, 21), (21, 15), (12, 9), (3, 15));
                    Path((3, 9), (12, 3), (21, 9));
                    break;
                default:
                    Box(2, 6, 22, 18);
                    Line(2, 12, 22, 12);
                    Line(9, 6, 9, 18);
                    Line(15, 6, 15, 18);
                    break;
            }
        }
    }

    internal sealed class DuelPromptSelection : MonoBehaviour, ISelectHandler, ICancelHandler
    {
        internal Action OnSelected { get; set; }
        internal Action OnCancelled { get; set; }

        public void OnSelect(BaseEventData eventData)
        {
            var scroll = GetComponentInParent<ScrollRect>();
            if (scroll != null && scroll.content != null && scroll.viewport != null && transform.IsChildOf(scroll.content))
            {
                var view = scroll.viewport;
                var visible = view.rect;
                var choice = RectTransformUtility.CalculateRelativeRectTransformBounds(view, transform);
                var shift = Vector3.zero;
                if (scroll.horizontal)
                    shift.x = choice.min.x < visible.xMin ? visible.xMin - choice.min.x :
                        Mathf.Min(0f, visible.xMax - choice.max.x);
                if (scroll.vertical)
                    shift.y = choice.max.y > visible.yMax ? visible.yMax - choice.max.y :
                        Mathf.Max(0f, visible.yMin - choice.min.y);
                if (shift != Vector3.zero)
                {
                    scroll.StopMovement();
                    scroll.content.anchoredPosition += (Vector2)scroll.content.parent.InverseTransformVector(view.TransformVector(shift));
                }
            }
            OnSelected?.Invoke();
        }

        public void OnCancel(BaseEventData eventData)
        {
            // Escape is handled by the field's global shortcut.
            if (Input.GetKeyDown(KeyCode.Escape) || OnCancelled == null) return;
            OnCancelled();
            eventData.Use();
        }
    }

    internal sealed class DuelPromptBackdrop : Image
    {
        internal DuelZoneImage[] BrowsablePiles { get; set; } = Array.Empty<DuelZoneImage>();
        internal bool ShowingField { get; set; }
        internal bool SelectingFieldCards { get; set; }
        internal Func<Vector2, Camera, bool> FieldCardHitTest { get; set; }
        internal Button ChainConfirmationControl { get; set; }
        internal bool ChainConfirmationAvailable { get; set; }

        internal bool IsPointOverPile(Vector2 screenPoint, Camera eventCamera)
        {
            foreach (var pile in BrowsablePiles)
            {
                if (pile != null && pile.isActiveAndEnabled && pile.IsRaycastLocationValid(screenPoint, eventCamera))
                    return true;
            }
            return false;
        }

        public override bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            var control = ChainConfirmationControl;
            if (ChainConfirmationAvailable && control != null && control.isActiveAndEnabled && control.IsInteractable() &&
                RectTransformUtility.RectangleContainsScreenPoint((RectTransform)control.transform, screenPoint, eventCamera))
                return false;
            if (SelectingFieldCards) return !FieldCardHitTest(screenPoint, eventCamera);
            return !ShowingField || !IsPointOverPile(screenPoint, eventCamera);
        }
    }
}
