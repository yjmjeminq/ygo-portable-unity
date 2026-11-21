using System;
using System.Collections.Generic;
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
        private Action<SelectableCardItem, uint> _onInspectCandidate;
        private readonly Action<byte[]> _onSubmitResponse;
        private readonly Func<DuelSession, DuelPrompt, byte[], bool> _trySubmitResponse;
        private readonly bool _mobileLayout;
        private readonly DeckPadKeyboard _padKeyboard;

        private RectTransform _modalBox;
        private RectTransform _modalHeader;
        private RectTransform _modalTitleBand;
        private Text _modalTitle;
        private Text _modalSubtitle;
        private DuelRatingBadge _positionRating;
        private RectTransform _modalBody;
        private RectTransform _modalFooter;
        private CanvasGroup _promptInput;
        private DuelPromptBackdrop _promptShade;
        private RectTransform _fieldViewTab;
        private Button _fieldViewButton;
        private Image _fieldViewIcon;
        private Text _fieldViewLabel;
        private bool _chainPrompt;
        private const float ChainCardWidth = 140f;
        private const float ChainCardGap = 10f;
        private const float ChainLocationTabHeight = 30f;
        private const float ChainGroupPadding = 6f;
        private const float ChoiceCardWidth = 142f;
        private const float CounterCardWidth = 172f;
        private const int ZoneCardWidth = 147;
        private const int ZoneCardHeight = 218;
        private const float ChoiceCardGap = 8f;
        private RectTransform _chainBox;
        private RectTransform _chainCards;
        private RectTransform _chainDetails;
        private RectTransform _chainFooter;
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
        private IReadOnlyList<SelectableCardItem> _activationChoices = Array.Empty<SelectableCardItem>();
        private Button[] _activationButtons = Array.Empty<Button>();
        private Action<int> _confirmPileActivation;

        // Zone viewer refs
        private static readonly (uint Location, string Label)[] PileTabs =
        {
            (YgoConstants.LOCATION_GRAVE, "묘지"),
            (YgoConstants.LOCATION_REMOVED, "제외"),
            (YgoConstants.LOCATION_EXTRA, "엑스트라"),
        };
        private RectTransform _zoneWindow;
        private RectTransform _zoneHeader;
        private DuelPromptBackdrop _zoneShade;
        private RectTransform _zoneBody;
        private RectTransform _zoneTabs;
        private Text _zoneTitle;
        private Image _zoneIcon;
        private RectTransform _zoneGrid;
        private ScrollRect _zoneScroll;
        private Text _zoneEmpty;
        private Button _zoneClose;
        private readonly Button[] _zoneOwnerButtons = new Button[2];
        private readonly Button[] _zonePileButtons = new Button[PileTabs.Length];
        private RectTransform _zoneDetailsRow;
        private Button _zoneDetailsButton;
        private Text _zoneSelectedName;
        private uint _zoneSelectedCode;
        private Outline _zoneSelectedBorder;
        private Color _zonePreviousBorder;
        private bool _zoneOpponent;
        private int _zonePileIndex;
        private Action<bool, uint> _onBrowsePile;
        private Action<uint> _onOpenZoneDetails;
        private GameObject _focusBeforePrompt;
        private GameObject _focusBeforeZone;
        private GameObject _focusBeforeKeyboard;

        // Result refs
        private Image _resCrown;
        private Image _resTitleArt;
        private Image _resTitleRule;
        private Text _resHeadline;
        private Text _resStats;
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
        private Button _attackCancel;
        private (Text Caption, Text Number) _sumAmount;
        private (Text Caption, Text Number) _sumTarget;
        private (Text Name, DuelStatStrip Values, DuelRatingBadge Rating) _attackSourceReadout;
        private (Text Name, DuelStatStrip Values, DuelRatingBadge Rating) _attackTargetReadout;
        private RectTransform _materialConfirmRoot;
        private Button _materialConfirm;
        private bool _fieldMaterialPick;
        private (byte controller, uint location, uint sequence)? _lastMaterial;

        public bool IsPromptOpen => ModalRoot != null && ModalRoot.activeSelf;
        public bool IsZoneViewerOpen => ZoneViewRoot != null && ZoneViewRoot.activeSelf;
        internal bool IsChainPromptOpen => IsPromptOpen && _chainPrompt && _confirmPileActivation == null;
        internal bool IsShowingField => IsPromptOpen && _promptShade.ShowingField;
        internal Selectable FieldViewControl => _fieldViewButton;
        internal bool IsFieldCardSelection => IsPromptOpen && _fieldCardIndices.Count > 0;
        internal Button FieldCardActionButton => !IsFieldCardSelection ? null :
            _materialConfirmRoot.gameObject.activeSelf ? _materialConfirm : _confirmBtn ?? _attackCancel;

        internal void SetFieldCardHitTest(Func<Vector2, Camera, bool> hitTest) => _promptShade.FieldCardHitTest = hitTest;

        internal bool FindFieldCard(DuelPrompt prompt, byte controller, uint location, uint sequence, out int index)
        {
            index = -1;
            return IsFieldCardSelection && CanAnswerPrompt && ReferenceEquals(prompt, _curPrompt) &&
                _fieldCardIndices.TryGetValue((controller, location, sequence), out index);
        }

        internal bool IsFieldCardSelected(int index) => index < 0 || _selectedIndices.Contains(index) ||
            (_curPrompt.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD && index >= _curPrompt.candidates.Count);

        internal string ReadFieldSumValue(int index)
        {
            if (_curPrompt?.messageType != YgoConstants.MSG_SELECT_SUM) return null;
            var cards = index < 0 ? _curPrompt.mustSelect : _curPrompt.candidates;
            int slot = index < 0 ? -1 - index : index;
            return slot < cards.Count ? FormatSumValue(cards[slot]) : null;
        }

        internal void ToggleFieldCard(int index, byte controller, uint location, uint sequence)
        {
            if (!CanAnswerPrompt || index < 0) return;
            if (_curPrompt.IsAttackTargetSelection)
            {
                SubmitAttackTarget(index);
                return;
            }
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
        private bool CanAnswerPrompt => IsPromptOpen && _promptInput.interactable && !IsShowingField;

        public DuelPromptModals(Transform canvasParent, Action<uint> onInspectCard, Action<byte[]> onSubmitResponse,
            Func<DuelSession, DuelPrompt, byte[], bool> trySubmitResponse = null, bool? mobileLayout = null)
        {
            _onInspectCard = onInspectCard;
            _onSubmitResponse = onSubmitResponse;
            _trySubmitResponse = trySubmitResponse;
            _mobileLayout = mobileLayout ?? Application.platform == RuntimePlatform.IPhonePlayer;
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
        internal void SetActivationHitTest(Func<Vector2, Camera, bool> hitTest) => _promptShade.ActivationHitTest = hitTest;
        internal bool IsShowingNativePrompt(DuelSession session, DuelPrompt prompt) =>
            IsPromptOpen && _confirmPileActivation == null && _curSession == session && _curPrompt == prompt;
        internal void SetPileActivationControls(Button grave, Button extra, Button banished)
        {
            _promptShade.GraveActivationControl = grave;
            _promptShade.ExtraActivationControl = extra;
            _promptShade.BanishedActivationControl = banished;
        }

        internal void SetInspectorControls(Button artwork, Button details)
        {
            _promptShade.InspectorArtworkControl = artwork;
            _zoneShade.InspectorArtworkControl = artwork;
            _promptShade.InspectorDetailsControl = details;
            _zoneShade.InspectorDetailsControl = details;
        }

        internal void SetCandidateInspector(Action<SelectableCardItem, uint> inspect) => _onInspectCandidate = inspect;

        internal bool HasChainActivationAt(DuelSession session, uint location) => FindChainActivationAt(session, location) >= 0;

        private int FindChainActivationAt(DuelSession session, uint location, uint? sequence = null)
        {
            if (!IsChainPromptOpen || !ReferenceEquals(session, _curSession) ||
                !ReferenceEquals(session.ActivePrompt, _curPrompt)) return -1;
            for (int i = 0; i < _activationChoices.Count; i++)
            {
                var choice = _activationChoices[i];
                if (choice.code != 0 && choice.controller == session.HumanPlayerIndex &&
                    DuelSession.IsActivationLocation(choice.location, location) && (sequence == null || choice.sequence == sequence))
                    return i;
            }
            return -1;
        }

        internal bool FocusChainActivationAt(DuelSession session, uint location, uint? sequence = null)
        {
            if (!_promptInput.interactable) return false;
            int index = FindChainActivationAt(session, location, sequence);
            if (index < 0) return false;
            if (IsShowingField) ToggleFieldView();
            var choice = _activationButtons[index];
            choice.onClick.Invoke();
            SetFocus(choice.gameObject);
            return true;
        }

        internal void SetCardDetailsOpen(bool open) => _promptInput.interactable = !open && !IsZoneViewerOpen;

        internal void SetZoneBrowserActions(Action<bool, uint> browsePile, Action<uint> openDetails)
        {
            _onBrowsePile = browsePile;
            _onOpenZoneDetails = openDetails;
        }

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
            var focused = EventSystem.current?.currentSelectedGameObject;
            target = focused != null ? focused.transform as RectTransform : null;
            return host != null && target != null && target.IsChildOf(host) && target.gameObject.activeInHierarchy;
        }

        internal bool HandlePad(DeckPadFrame frame)
        {
            if (!frame.HasInput)
                return _padKeyboard.IsOpen || IsZoneViewerOpen || ResultRoot.activeSelf || IsPromptOpen && !IsShowingField && !IsFieldCardSelection;
            if (_padKeyboard.IsOpen)
                HandlePadScope(_padKeyboard.Scope, frame, _padKeyboard.DefaultFocus, _padKeyboard.Close);
            else if (IsZoneViewerOpen)
                HandleZonePad(frame);
            else if (ResultRoot.activeSelf)
                HandlePadScope(ResultRoot.transform, frame, _rematchButton, TriggerMenu);
            else if (IsPromptOpen && !IsShowingField && !IsFieldCardSelection)
                HandlePadScope(ModalRoot.transform, frame, PreferredPromptControl(), OnModalCloseClicked,
                    _chainPrompt ? _chainEffectScroll : _modalBody.GetComponentInChildren<ScrollRect>());
            else return false;

            return true;
        }

        private void HandleZonePad(DeckPadFrame frame)
        {
            if (frame.Cancel)
            {
                CloseZoneViewer();
                return;
            }
            if (frame.Alternate && _zoneDetailsButton.isActiveAndEnabled && _zoneDetailsButton.IsInteractable())
            {
                _zoneDetailsButton.onClick.Invoke();
                return;
            }
            if (_zoneTabs.gameObject.activeSelf && (frame.PreviousArea || frame.NextArea))
            {
                int offset = frame.PreviousArea ? PileTabs.Length - 1 : 1;
                _zonePileButtons[(_zonePileIndex + offset) % PileTabs.Length].onClick.Invoke();
                return;
            }
            HandlePadScope(ZoneViewRoot.transform, frame, FirstFocus(_zoneGrid) ?? _zoneClose, CloseZoneViewer, _zoneScroll);
        }

        private void HandlePadScope(Transform scope, DeckPadFrame frame, Selectable initial, Action cancel, ScrollRect scroll = null)
        {
            if (frame.Cancel)
            {
                cancel();
                return;
            }
            ScrollFromPad(scroll, frame.DescriptionScroll);
            var focused = EventSystem.current?.currentSelectedGameObject;
            var control = focused != null ? focused.GetComponent<Selectable>() : null;
            if (!CanNavigate(control, scope))
            {
                control = CanNavigate(initial, scope) ? initial : FirstFocus(scope);
                SetFocus(control?.gameObject);
                return;
            }
            if (frame.Direction != Vector2Int.zero)
            {
                SetFocus(PadNeighbor(scope, control, frame.Direction).gameObject);
                return;
            }
            if (!frame.Submit) return;
            if (control is Button button) button.onClick.Invoke();
            else if (control is InputField field) OpenPadKeyboard(field);
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
            _confirmPileActivation = null;
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
            _chainCandidateIndices = null;
            _activationChoices = Array.Empty<SelectableCardItem>();
            _activationButtons = Array.Empty<Button>();
            _confirmPileActivation = null;
            _confirmBtn = null;
            _attackCancel = null;
            _sumAmount = _sumTarget = default;
            _attackSourceReadout = _attackTargetReadout = default;
            _positionRating.Bind(default);
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

            var content = _mobileLayout ? MobileSafeArea.Create(dim) : dim;
            _modalBox = DuelStyle.Panel(content, "Win", new Vector2(.235f, .17f), new Vector2(.845f, .474f),
                fill: DuelStyle.GlassColor, border: DuelStyle.GlassEdge);
            AddSelectionFrame(_modalBox);

            _modalHeader = LotdUiKit.CreateRect("Hdr", _modalBox, new Vector2(0f, .81f), Vector2.one, Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(_modalHeader.gameObject, LotdUiKit.WhiteSprite, DuelStyle.HeaderColor, false, false);
            _modalTitleBand = LotdUiKit.CreateRect("TitleBand", _modalHeader, new Vector2(.024f, .48f), new Vector2(.976f, .94f),
                Vector2.zero, Vector2.zero);
            var subBand = LotdUiKit.CreateRect("SubtitleBand", _modalHeader, new Vector2(.024f, .05f), new Vector2(.976f, .47f),
                Vector2.zero, Vector2.zero);
            _modalTitle = LotdUiKit.AddFittedText(_modalTitleBand, "Title", "", 22, 11, TextAnchor.MiddleLeft, Color.white);
            _modalSubtitle = LotdUiKit.AddFittedText(subBand, "Sub", "", 15, 10, TextAnchor.MiddleLeft, DuelStyle.Muted);
            _positionRating = DuelRatingBadge.AddToHeading(_modalSubtitle);

            _modalBody = LotdUiKit.CreateRect("Body", _modalBox, new Vector2(.02f, .202f), new Vector2(.98f, .785f), Vector2.zero, Vector2.zero);
            _modalFooter = LotdUiKit.CreateRect("Footer", _modalBox, new Vector2(.62f, .028f), new Vector2(.98f, .183f), Vector2.zero, Vector2.zero);

            BuildChainPicker(content);
            _materialConfirmRoot = LotdUiKit.CreateRect("MaterialConfirm", content, Vector2.one * .5f, Vector2.one * .5f,
                new Vector2(-30f, -30f), new Vector2(30f, 30f));
            _materialConfirm = DuelStyle.RoundButton(_materialConfirmRoot, "Button", DuelStyle.Gold, () => TryConfirm());
            var confirmIcon = LotdUiKit.FillRect("Check", _materialConfirm.transform, 13f);
            LotdUiKit.AddFittedText(confirmIcon, "Mark", "✓", 26, 17, TextAnchor.MiddleCenter, DuelStyle.Player, FontStyle.Bold);
            _materialConfirmRoot.gameObject.SetActive(false);
            _fieldViewTab = LotdUiKit.FillRect("FieldView", content);
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
            if (!IsPromptOpen || !_promptInput.interactable || IsFieldCardSelection) return;
            LotdAudio.PlayClick();
            SetFieldView(!IsShowingField);
            if (_curPrompt?.messageType == YgoConstants.MSG_SELECT_POSITION &&
                ReadSummonPosition(_curSession, _curPrompt, out var source).ShowsSummonPreview)
                InspectChoice(_curPrompt.promptCardCode, source);
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
            _promptShade.ChainConfirmationAvailable = _chainPrompt && _confirmPileActivation == null;
            _promptShade.SelectingFieldCards = _fieldCardIndices.Count > 0;
            _promptShade.color = showing || _promptShade.SelectingFieldCards ? Color.clear : new Color(0f, .02f, .07f, .11f);
            _fieldViewTab.gameObject.SetActive(!_promptShade.SelectingFieldCards);
            _chainBox.gameObject.SetActive(_chainPrompt && !showing);
            _modalBox.gameObject.SetActive(!_chainPrompt && !showing);
            Vector2 corner = _chainPrompt ? _chainBox.anchorMax : _modalBox.anchorMax;
            _fieldViewTab.anchorMin = showing ? new Vector2(.777f, .112f) : corner - new Vector2(.121f, 0f);
            _fieldViewTab.anchorMax = showing ? new Vector2(.898f, .171f) : corner + new Vector2(0f, .059f);
            if (_mobileLayout)
            {
                Vector2 size = ((RectTransform)_fieldViewTab.parent).rect.size;
                float touch = MobileSafeArea.MinimumTouchSize(_fieldViewTab);
                var min = _fieldViewTab.anchorMin;
                var max = _fieldViewTab.anchorMax;
                min.x = Mathf.Min(min.x, max.x - touch / Mathf.Max(1f, size.x));
                max.y = Mathf.Min(.985f, Mathf.Max(max.y, min.y + touch / Mathf.Max(1f, size.y)));
                min.y = Mathf.Min(min.y, max.y - touch / Mathf.Max(1f, size.y));
                _fieldViewTab.anchorMin = min;
                _fieldViewTab.anchorMax = max;
            }
            _fieldViewLabel.text = showing ? "선택창 보기" : "필드 보기";
            var glow = showing ? DuelVisualAssets.PromptFieldGlow : null;
            _fieldViewIcon.sprite = glow ?? DuelVisualAssets.PromptFieldArrow;
            _fieldViewIcon.enabled = _fieldViewIcon.sprite != null;
            _fieldViewIcon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, glow != null ? 0f : showing ? 90f : -90f);
        }

        private void BuildChainPicker(Transform parent)
        {
            _chainBox = DuelStyle.Panel(parent, "ChainWin", new Vector2(.222f, .185f), new Vector2(.922f, .585f),
                fill: DuelStyle.GlassColor, border: DuelStyle.GlassEdge);
            AddSelectionFrame(_chainBox);
            var header = LotdUiKit.CreateRect("Hdr", _chainBox, new Vector2(0f, .86f), Vector2.one, Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(header.gameObject, LotdUiKit.WhiteSprite, DuelStyle.HeaderColor, false, false);
            var title = LotdUiKit.CreateRect("Title", header, new Vector2(.024f, .05f), new Vector2(.62f, .95f),
                Vector2.zero, Vector2.zero);
            _chainTitle = LotdUiKit.AddFittedText(title, "Text", "", 24, 13, TextAnchor.MiddleLeft, Color.white);
            var summary = LotdUiKit.CreateRect("Summary", header, new Vector2(.67f, .07f), new Vector2(.972f, .93f),
                Vector2.zero, Vector2.zero);
            _chainSummary = LotdUiKit.AddFittedText(summary, "Text", "", 18, 11, TextAnchor.MiddleRight, DuelStyle.Gold, FontStyle.Bold);

            var cards = LotdUiKit.CreateRect("Cards", _chainBox, new Vector2(.019f, .149f), new Vector2(.695f, .852f),
                Vector2.zero, Vector2.zero);
            _chainCardScroll = CreateCardStrip(cards, out _chainCards, ChainCardGap);

            var details = _chainDetails = DuelStyle.Panel(_chainBox, "Details", new Vector2(.711f, .176f), new Vector2(.977f, .852f));
            var name = LotdUiKit.CreateRect("Name", details, new Vector2(.04f, .77f), new Vector2(.96f, .98f),
                Vector2.zero, Vector2.zero);
            _chainName = LotdUiKit.AddFittedText(name, "Text", "", 21, 12, TextAnchor.MiddleLeft, Color.white);
            var source = LotdUiKit.CreateRect("Source", details, new Vector2(.04f, .59f), new Vector2(.96f, .76f),
                Vector2.zero, Vector2.zero);
            _chainSource = LotdUiKit.AddFittedText(source, "Text", "", 16, 10, TextAnchor.MiddleLeft, DuelStyle.Gold);
            var description = LotdUiKit.CreateRect("Description", details, new Vector2(.0f, .025f), new Vector2(1f, .57f),
                Vector2.zero, Vector2.zero);
            _chainEffectScroll = CreateVerticalScroll(description, out Transform content);
            _chainEffect = AddDescriptionText(content, "Effect", "", 18);

            var footer = _chainFooter = LotdUiKit.CreateRect("Footer", _chainBox, new Vector2(.021f, .024f), new Vector2(.977f, .143f),
                Vector2.zero, Vector2.zero);
            var pass = LotdUiKit.CreateRect("Pass", footer, new Vector2(.655f, 0f), new Vector2(.817f, 1f), Vector2.zero, Vector2.zero);
            _chainPass = DuelStyle.Button(pass, "B", "취소", OnModalCloseClicked, 16);
            var activate = LotdUiKit.CreateRect("Activate", footer, new Vector2(.836f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
            _chainActivate = DuelStyle.Button(activate, "B", "효과 발동", ConfirmChainChoice, 16, gold: true);
            _chainBox.gameObject.SetActive(false);
        }

        public bool TryCancelOrPass()
        {
            if (!IsPromptOpen || !_promptInput.interactable || _curPrompt == null) return false;
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

            if (_confirmPileActivation != null)
            {
                LotdAudio.PlayClick();
                HidePrompt();
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

            if (_curPrompt.IsAttackDecision)
            {
                var selected = EventSystem.current?.currentSelectedGameObject;
                if (selected == null || !selected.TryGetComponent<Button>(out var option) || !CanNavigate(option, _modalFooter))
                    return false;
                option.onClick.Invoke();
                return true;
            }
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

        private void PreparePrompt(DuelSession session, DuelPrompt prompt)
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
        }

        private void ShowPreparedPrompt()
        {
            SetFieldView(false);
            ModalRoot.transform.SetAsLastSibling();
            ModalRoot.SetActive(true);
            UiEntranceMotion.Play((_chainPrompt ? _chainBox : _modalBox).gameObject);
            CardAnimationManager.Instance?.BringToFront();
        }

        internal void ShowPileActivationPicker(DuelSession session, DuelPrompt prompt, int[] indices,
            string title, Action<int> confirm)
        {
            if (indices.Length == 0 || IsPromptOpen || IsZoneViewerOpen ||
                !ReferenceEquals(session.ActivePrompt, prompt)) return;
            PreparePrompt(session, prompt);
            _chainPrompt = true;
            _chainCandidateIndices = indices;
            _confirmPileActivation = confirm;
            ShowPreparedPrompt();
            RenderActivationPicker(session, prompt, indices.Select(index => prompt.activatable[index]).ToArray(),
                title, $"발동 가능 {indices.Length}개", "닫기");
            PrepareFocusControls(_chainBox, OnModalCloseClicked);
            FocusPrompt();
        }

        public void ShowPrompt(DuelSession session, DuelPrompt prompt,
            ChainConfirmationMode chainConfirmation = ChainConfirmationMode.On)
        {
            PreparePrompt(session, prompt);
            if (session.ShouldPassChainPrompt(prompt, chainConfirmation))
            {
                SubmitToEngine(BitConverter.GetBytes(-1));
                return;
            }

            _chainPrompt = prompt.messageType == YgoConstants.MSG_SELECT_CHAIN ||
                prompt.IsTriggerActivationConfirmation;
            bool cardChoices = prompt.messageType is YgoConstants.MSG_SELECT_POSITION or YgoConstants.MSG_SELECT_CARD
                or YgoConstants.MSG_SELECT_TRIBUTE or YgoConstants.MSG_SELECT_SUM or YgoConstants.MSG_SELECT_UNSELECT_CARD
                or YgoConstants.MSG_SELECT_COUNTER or YgoConstants.MSG_SORT_CARD or YgoConstants.MSG_SORT_CHAIN
                or YgoConstants.MSG_ANNOUNCE_CARD;
            bool tall = cardChoices || prompt.messageType == YgoConstants.MSG_ANNOUNCE_RACE;
            bool showThreeOptions = prompt.messageType == YgoConstants.MSG_SELECT_OPTION && prompt.options.Count >= 3;
            bool attackTarget = prompt.IsAttackTargetSelection;
            _modalBox.anchorMax = new Vector2(cardChoices ? .923f : .845f, tall || showThreeOptions ? .626f : .474f);
            _modalHeader.anchorMin = new Vector2(0f, tall ? .873f : .81f);
            _modalTitleBand.anchorMin = new Vector2(.024f, attackTarget ? .06f : .48f);
            _modalBody.anchorMin = new Vector2(.02f, tall ? .135f : .202f);
            _modalBody.anchorMax = new Vector2(.98f, tall ? .855f : .785f);
            _modalFooter.anchorMin = new Vector2(.62f, tall ? .019f : .028f);
            _modalFooter.anchorMax = new Vector2(.98f, tall ? .122f : .183f);
            _modalBox.anchorMin = new Vector2(cardChoices ? .211f : .235f, .17f);
            _modalHeader.anchorMax = Vector2.one;
            if (prompt.messageType == YgoConstants.MSG_SELECT_COUNTER)
            {
                float viewportWidth = Mathf.Max(1f, ((RectTransform)ModalRoot.transform).rect.width);
                float stripWidth = prompt.candidates.Count * (CounterCardWidth + ChoiceCardGap) + 24f;
                float centerX = (_modalBox.anchorMin.x + _modalBox.anchorMax.x) * .5f;
                float halfWidth = Mathf.Min(_modalBox.anchorMax.x - centerX, Mathf.Max(266f, stripWidth) / viewportWidth * .5f);
                _modalBox.anchorMin = new Vector2(centerX - halfWidth, .146f);
                _modalBox.anchorMax = new Vector2(centerX + halfWidth, .699f);
                _modalFooter.anchorMin = new Vector2(.06f, _modalFooter.anchorMin.y);
            }
            else if (prompt.IsAttackDecision)
            {
                _modalBox.anchorMin = new Vector2(.347f, .168f);
                _modalBox.anchorMax = new Vector2(.773f, .429f);
                _modalHeader.anchorMin = new Vector2(0f, .79f);
                _modalTitleBand.anchorMin = new Vector2(.024f, .06f);
                _modalBody.anchorMin = new Vector2(.025f, .365f);
                _modalBody.anchorMax = new Vector2(.975f, .767f);
                _modalFooter.anchorMin = new Vector2(.025f, .035f);
                _modalFooter.anchorMax = new Vector2(.975f, .316f);
            }
            BindFieldCardChoices(session, prompt);
            if (_mobileLayout &&
                (prompt.messageType is YgoConstants.MSG_SELECT_EFFECTYN or YgoConstants.MSG_SORT_CHAIN or YgoConstants.MSG_SELECT_YESNO))
                EnlargeConfirmationFooter();
            if (_fieldCardIndices.Count > 0)
            {
                bool overhead = _fieldMaterialPick || attackTarget;
                float headerBottom = prompt.messageType == YgoConstants.MSG_SELECT_SUM ? .792f : .835f;
                _modalBox.anchorMin = overhead ? new Vector2(.286f, headerBottom) : new Vector2(.305f, .151f);
                _modalBox.anchorMax = overhead ? new Vector2(.800f, .898f) : new Vector2(.762f, .214f);
                _modalHeader.anchorMin = Vector2.zero;
                _modalHeader.anchorMax = new Vector2(attackTarget && !prompt.cancelable ? 1f : .56f, 1f);
                _modalFooter.anchorMin = new Vector2(.56f, .08f);
                _modalFooter.anchorMax = new Vector2(.98f, .92f);
            }
            ShowPreparedPrompt();

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
                case YgoConstants.MSG_SELECT_EFFECTYN:
                    if (prompt.IsAttackDecision) RenderAttackDecision(session, prompt);
                    else RenderYesNo(session, prompt);
                    break;
                case YgoConstants.MSG_SELECT_POSITION:
                    RenderSelectPosition(session, prompt);
                    break;
                case YgoConstants.MSG_SELECT_OPTION:
                case YgoConstants.MSG_ANNOUNCE_NUMBER:
                    RenderOptionList(session, prompt);
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
                    RenderSelectCounter(session, prompt);
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
            SubmitToEngine(bytes);
        }

        private void SubmitToEngine(byte[] bytes)
        {
            if (_trySubmitResponse != null && !_trySubmitResponse(_curSession, _curPrompt, bytes)) return;
            HidePrompt();
            _onSubmitResponse?.Invoke(bytes);
        }

        private void RenderYesNo(DuelSession session, DuelPrompt p)
        {
            uint cardCode = p.promptCardCode;
            if (cardCode == 0) cardCode = CardDatabase.GetDescriptionCardCode(p.promptDesc);
            if (cardCode == 0) cardCode = session.SelectionChainLink?.code ?? 0;
            var card = CardDatabase.Get(cardCode);
            string msg = CardDatabase.ResolveDescription(p.promptDesc) ?? CardDatabase.GetKoreanCardDescription(cardCode)
                ?? "선택한 처리를 실행하시겠습니까?";

            _modalTitle.text = "선택 확인";
            _modalSubtitle.text = card?.name ?? "";
            if (card != null) InspectChoice(cardCode, ReadFieldEffectSource(session, p, cardCode));

            CreateVerticalScroll(_modalBody, out Transform description);
            AddDescriptionText(description, "Question", msg, 18);

            AddFooterButton("예", () => Submit(BitConverter.GetBytes(1)), true);
            AddFooterButton("아니오", () => Submit(BitConverter.GetBytes(0)), false);
        }

        private void RenderAttackDecision(DuelSession session, DuelPrompt prompt)
        {
            var source = session.GetAttackSelectionSource(prompt);
            bool opponent = (source != null ? source.controller : session.TurnPlayer) != session.HumanPlayerIndex;
            bool replay = prompt.IsAttackReplayChoice;
            _modalTitle.text = replay ? "공격 대상 변경" : opponent ? "상대 공격 방식 선택" : "공격 방식 선택";
            _modalSubtitle.text = "";
            var tile = AddAttackSelectionCard("AttackSource", 0f, 1f);
            BindAttackSelectionCard(tile, source, opponent, opponent ? "상대 공격" : "공격", "공격 몬스터");
            if (source != null)
                InspectChoice(source.code, new SelectableCardItem
                {
                    code = source.code, controller = source.controller, location = source.location,
                    sequence = source.sequence, position = source.position,
                });
            AddFooterButton(replay ? "공격 중단" : "몬스터 공격", () => Submit(BitConverter.GetBytes(0)), false);
            AddFooterButton(replay ? "공격 계속" : "직접 공격", () => Submit(BitConverter.GetBytes(1)), true);
        }

        private static DuelCardReadout ReadSummonPosition(DuelSession session, DuelPrompt p, out SelectableCardItem source)
        {
            uint code = p.promptCardCode;
            source = ReadFieldEffectSource(session, p, code);
            var live = source == null ? null : session.GetVisibleFieldCard(code, source.controller, source.location, source.sequence);
            var card = DuelCardReadout.ForSummonPosition(CardDatabase.Get(code), live);
            if (!card.ShowsSummonPreview) source = null;
            return card;
        }

        internal DuelCardReadout? ReadInspectedSummon(uint code, (byte controller, uint location, uint sequence)? location)
        {
            var prompt = _curPrompt;
            if (!IsPromptOpen || IsShowingField || IsZoneViewerOpen || prompt?.messageType != YgoConstants.MSG_SELECT_POSITION ||
                prompt.promptCardCode != code) return null;

            var card = ReadSummonPosition(_curSession, prompt, out var source);
            if (!card.ShowsSummonPreview ||
                location.HasValue && (source == null || location.Value != (source.controller, source.location, source.sequence)))
                return null;
            return card;
        }

        private void RenderSelectPosition(DuelSession session, DuelPrompt p)
        {
            _modalTitle.text = "표시 형식 선택";
            var card = ReadSummonPosition(session, p, out var source);
            _modalSubtitle.text = p.promptCardCode == 0 ? "뒷면 카드" : CardDatabase.GetName(p.promptCardCode);
            _positionRating.Bind(card.RatingNumber);
            SetFieldView(false);
            InspectChoice(p.promptCardCode, source);

            var choices = new (uint position, string label, bool rotate, bool faceDown)[]
            {
                (YgoConstants.POS_FACEUP_ATTACK, "앞면 공격 표시", false, false),
                (YgoConstants.POS_FACEUP_DEFENSE, "앞면 수비 표시", true, false),
                (YgoConstants.POS_FACEDOWN_DEFENSE, "뒷면 수비 표시", true, true),
                (YgoConstants.POS_FACEDOWN_ATTACK, "뒷면 공격 표시", false, true),
            }.Where(choice => (p.allowedPositions & choice.position) != 0).ToArray();
            float halfWidth = Mathf.Clamp(.022f + choices.Length * .102f, .15f, .356f);
            const float center = .567f;
            _modalBox.anchorMin = new Vector2(center - halfWidth, _modalBox.anchorMin.y);
            _modalBox.anchorMax = new Vector2(center + halfWidth, _modalBox.anchorMax.y);
            _modalHeader.anchorMin = new Vector2(0f, .76f);
            _modalBody.anchorMin = new Vector2(.021f, .042f);
            _modalBody.anchorMax = new Vector2(.98f, .743f);
            for (int i = 0; i < choices.Length; i++)
            {
                var choice = choices[i];
                var slot = DuelStyle.Panel(_modalBody, $"Pos_{choice.position}",
                    new Vector2((float)i / choices.Length, 0f), new Vector2((float)(i + 1) / choices.Length, 1f),
                    new Vector2(4f, 0f), new Vector2(-4f, 0f));
                var label = LotdUiKit.CreateRect("Label", slot, new Vector2(.04f, .865f), new Vector2(.96f, .985f),
                    Vector2.zero, Vector2.zero);
                LotdUiKit.AddFittedText(label, "Text", choice.label, 15, 10, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                var art = LotdUiKit.CreateRect("Card", slot,
                    new Vector2(.042f, card.IsMonster ? .175f : .045f), new Vector2(.958f, .851f),
                    Vector2.zero, Vector2.zero);
                BuildPositionArtwork(art, p.promptCardCode, choice.rotate, choice.faceDown);
                if (card.IsMonster)
                {
                    var stats = LotdUiKit.CreateRect("CombatStats", slot, new Vector2(.04f, .02f), new Vector2(.96f, .159f),
                        Vector2.zero, Vector2.zero);
                    new DuelStatStrip(stats, compact: true).Bind(card, position: choice.position);
                }

                var button = slot.gameObject.AddComponent<Button>();
                button.targetGraphic = slot.GetComponent<Image>();
                button.onClick.AddListener(() =>
                {
                    LotdAudio.PlayClick();
                    Submit(BitConverter.GetBytes((int)choice.position));
                });
            }
        }

        private static void BuildPositionArtwork(RectTransform art, uint code, bool rotate, bool faceDown)
        {
            if (rotate)
            {
                var holder = LotdUiKit.FillRect("LandscapeFit", art);
                var fit = holder.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = 1f / LotdUiKit.CardAspectRatio;
                // Rotate a centered portrait inside the fitted landscape bounds.
                Vector2 halfSize = new Vector2(LotdUiKit.CardAspectRatio, fit.aspectRatio) * .5f;
                Vector2 center = Vector2.one * .5f;
                art = LotdUiKit.CreateRect("Rotated", holder, center - halfSize, center + halfSize, Vector2.zero, Vector2.zero);
                art.localRotation = Quaternion.Euler(0f, 0f, -90f);
            }
            CompositeCardView.Create(art, "V", duelArtwork: true).SetCard(code, faceDown: faceDown || code == 0);
        }

        private void RenderOptionList(DuelSession session, DuelPrompt p)
        {
            bool isNum = p.messageType == YgoConstants.MSG_ANNOUNCE_NUMBER;
            _modalTitle.text = CardDatabase.ResolveDescription(p.SelectionHint) ?? (isNum ? "숫자 선언" : "선택할 항목");
            _modalSubtitle.text = "";

            CreateVerticalScroll(_modalBody, out Transform content);
            var cards = isNum ? Array.Empty<CardInfo>() : p.options
                .Select(opt => CardDatabase.Get(CardDatabase.GetDescriptionCardCode(opt))).ToArray();
            var sources = cards.Where(card => card != null).Distinct().ToArray();
            var link = session.SelectionChainLink;
            var rulesNeeded = new HashSet<uint>();
            for (int i = 0; i < p.options.Count; i++)
            {
                int pick = i;
                ulong opt = p.options[i];
                var card = isNum ? null : cards[i];
                string text = isNum ? opt.ToString() : CardDatabase.ResolveOptionDescription(opt);
                if (text == null)
                {
                    uint source = card?.code ?? session.SelectionChainLink?.code ?? 0;
                    if (p.options.Count == 1) text = CardDatabase.GetKoreanCardDescription(source);
                    if (text == null)
                    {
                        text = $"선택지 {i + 1}";
                        if (source != 0) rulesNeeded.Add(source);
                    }
                }
                if (sources.Length > 1 && card != null)
                    text = $"{card.name}\n{text}";
                var option = LotdUiKit.CreateWrappedButton(content, $"Opt_{i}", text, () => Submit(BitConverter.GetBytes(pick)),
                    18, i == 0, 54f, sidePadding: 22);
                DuelStyle.ApplyButton(option, i == 0);
                if (isNum)
                {
                    var value = option.GetComponentInChildren<Text>();
                    value.font = DuelTypography.NumberFont;
                }
                uint inspectCode = card?.code ?? link?.code ?? 0;
                PreviewOnFocus(option, inspectCode, ReadFieldEffectSource(session, p, inspectCode));
            }
            if (sources.Length == 1)
            {
                _modalSubtitle.text = sources[0].name;
                uint code = sources[0].code;
                InspectChoice(code, ReadFieldEffectSource(session, p, code));
            }
            else if (sources.Length == 0 && link != null)
            {
                _modalSubtitle.text = CardDatabase.GetName(link.code);
                InspectChoice(link.code, ReadFieldEffectSource(session, p, link.code));
            }
            foreach (uint code in rulesNeeded)
            {
                string rules = CardDatabase.GetKoreanCardDescription(code);
                if (rules != null)
                    AddDescriptionText(content, "CardText", sources.Length == 1 ? rules : $"{CardDatabase.GetName(code)}\n{rules}");
            }
        }

        private void RenderChainPicker(DuelSession session, DuelPrompt prompt, ChainConfirmationMode chainConfirmation)
        {
            bool singleEffect = prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN;
            IReadOnlyList<SelectableCardItem> choices = singleEffect
                ? new[] { prompt.ReadEffectConfirmationSource() }
                : prompt.candidates;
            if (chainConfirmation != ChainConfirmationMode.On && !singleEffect)
            {
                // Hide fast responses without changing the indices sent back to the engine.
                _chainCandidateIndices = Enumerable.Range(0, prompt.candidates.Count)
                    .Where(index => session.IsChainCandidateVisible(prompt, prompt.candidates[index], chainConfirmation)).ToArray();
                choices = _chainCandidateIndices.Select(index => prompt.candidates[index]).ToArray();
            }
            bool respondingToAttack = session.HasActiveAttack && session.AttackerCon != session.HumanPlayerIndex;
            string title = prompt.forced ? "필수 효과 순서 선택" :
                respondingToAttack && session.CurrentChain.Count == 0
                    ? (session.TargetLoc == 0 ? "상대의 직접 공격에 대응하시겠습니까?" : "상대의 공격 선언에 대응하시겠습니까?")
                    : choices.Count == 1 ? "효과를 발동하시겠습니까?" : "발동할 효과 선택";
            RenderActivationPicker(session, prompt, choices, title, $"체인 {session.CurrentChain.Count + 1}  ·  {choices.Count}개",
                prompt.forced ? null : "패스");
        }

        private void RenderActivationPicker(DuelSession session, DuelPrompt prompt, IReadOnlyList<SelectableCardItem> choices,
            string title, string summary, string passLabel)
        {
            _activationChoices = choices;
            _activationButtons = new Button[choices.Count];
            bool compact = choices.Count == 1;
            _chainBox.anchorMin = compact ? new Vector2(.255f, .200f) : new Vector2(.222f, .185f);
            _chainBox.anchorMax = compact ? new Vector2(.749f, .655f) : new Vector2(.922f, .628f);
            float stripEnd = choices.Count switch { 1 => .33f, 2 => .455f, 3 => .615f, _ => .695f };
            var cardStrip = (RectTransform)_chainCardScroll.transform;
            var pass = (RectTransform)_chainPass.transform.parent;
            var activate = (RectTransform)_chainActivate.transform.parent;
            _chainFooter.anchorMin = new Vector2(.021f, .024f);
            _chainFooter.anchorMax = new Vector2(.977f, .143f);
            pass.anchorMin = new Vector2(.655f, 0f);
            pass.anchorMax = new Vector2(.817f, 1f);
            activate.anchorMin = new Vector2(.836f, 0f);
            activate.anchorMax = Vector2.one;
            cardStrip.anchorMin = new Vector2(.019f, .149f);
            cardStrip.anchorMax = new Vector2(stripEnd, .852f);
            _chainDetails.anchorMin = new Vector2(stripEnd + .016f, .176f);
            _chainDetails.anchorMax = new Vector2(.977f, .852f);
            if (_mobileLayout)
            {
                _chainBox.anchorMin = new Vector2(.187f, .155f);
                _chainBox.anchorMax = new Vector2(.905f, .758f);
                if (!compact)
                {
                    float footerTop = TouchFooterTop(_chainBox, _chainFooter, 1f);
                    _chainFooter.anchorMax = new Vector2(.977f, footerTop);
                    cardStrip.anchorMin = new Vector2(.019f, footerTop + .018f);
                    _chainDetails.anchorMin = new Vector2(stripEnd + .016f, footerTop + .018f);
                }
            }
            if (compact)
            {
                cardStrip.anchorMin = new Vector2(.019f, .058f);
                _chainDetails.anchorMin = new Vector2(stripEnd + .016f, .083f);
                _chainDetails.anchorMax = new Vector2(.726f, .851f);
                _chainFooter.anchorMin = new Vector2(.745f, .083f);
                _chainFooter.anchorMax = new Vector2(.977f, .851f);
                pass.anchorMin = new Vector2(0f, .045f);
                pass.anchorMax = new Vector2(1f, .453f);
                activate.anchorMin = new Vector2(0f, .531f);
                activate.anchorMax = new Vector2(1f, .954f);
            }
            SetFieldView(false);
            _chainTitle.text = title;
            _chainSummary.text = summary;
            _chainPass.transform.parent.gameObject.SetActive(passLabel != null);
            _chainPass.GetComponentInChildren<Text>(true).text = compact && passLabel != null ? "발동하지 않는다" : passLabel ?? "";
            _chainActivate.GetComponentInChildren<Text>(true).text = compact ? "발동한다" : "효과 발동";
            _chainActivate.interactable = choices.Count > 0;
            _chainName.text = _chainSource.text = _chainEffect.text = "";

            var frames = new Outline[choices.Count];
            var sources = new (byte controller, uint location, uint sequence, uint material)[choices.Count];
            for (int i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                // For material, the sequence belongs to the host and the position identifies the attached card.
                uint material = (choice.location & YgoConstants.LOCATION_OVERLAY) != 0 ? choice.position : 0;
                sources[i] = (choice.controller, choice.location, choice.sequence, material);
            }
            var sourceCounts = sources.GroupBy(source => source).ToDictionary(group => group.Key, group => group.Count());
            var sourceNumbers = new Dictionary<(byte, uint, uint, uint), int>();

            void Select(int index)
            {
                if (!CanAnswerPrompt || !ReferenceEquals(_curPrompt, prompt)) return;
                if (_chainSelection >= 0) SetSelectionOutline(frames[_chainSelection], false);
                _chainSelection = index;
                SetSelectionOutline(frames[index], true);
                var item = choices[index];
                _chainName.text = CardDatabase.GetName(item.code);
                _chainSource.text = CardSourceLabel(session, item);
                string text = _confirmPileActivation != null ? CardDatabase.ResolveCommandEffectDescription(item) :
                    CardDatabase.ResolveEffectDescription(item.description, item.code);
                _chainEffect.text = text ?? "이 카드의 효과를 발동할 수 있습니다.";
                _chainEffectScroll.StopMovement();
                _chainEffectScroll.content.anchoredPosition = Vector2.zero;
                InspectChoice(item.code, item);
            }

            float stripWidth = 6f - ChainCardGap;
            var groups = Enumerable.Range(0, sources.Length).GroupBy(i => (sources[i].controller, sources[i].location));
            foreach (var group in groups)
            {
                var (controller, location) = group.Key;
                int count = group.Count();
                float width = count * ChainCardWidth + (count - 1) * ChainCardGap + 2f * ChainGroupPadding;
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
                    BuildCandidateCard(art, session, choices[index], choices[index].code);

                    if (duplicate)
                    {
                        sourceNumbers.TryGetValue(source, out int number);
                        sourceNumbers[source] = ++number;
                        var caption = LotdUiKit.CreateRect("EffectNumber", tile, new Vector2(.04f, .01f),
                            new Vector2(.96f, .16f), Vector2.zero, Vector2.zero);
                        LotdUiKit.AddFittedText(caption, "Text", $"후보 {number}", 13, 9, TextAnchor.MiddleCenter, DuelStyle.Muted);
                    }
                    var button = tile.gameObject.AddComponent<Button>();
                    _activationButtons[index] = button;
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
            var groupTone = opponent ? new Color(.30f, .115f, .15f) : new Color(.086f, .24f, .34f);
            var edge = LotdUiKit.CreateRect("GroupFrame", group, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(0f, -ChainLocationTabHeight));
            var tint = new Color(groupTone.r, groupTone.g, groupTone.b, .60f);
            LotdUiKit.AddImage(edge.gameObject, LotdUiKit.WhiteSprite, tint, false, false);
            var row = LotdUiKit.FillRect("Cards", edge, ChainGroupPadding);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = ChainCardGap;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;

            var tab = LotdUiKit.CreateRect("LocationTab", group, Vector2.up, Vector2.up,
                new Vector2(0f, -ChainLocationTabHeight), new Vector2(Mathf.Min(width, 174f), 0f));
            LotdUiKit.AddImage(tab.gameObject, LotdUiKit.WhiteSprite, groupTone, false, false);
            if (DuelVisualAssets.PromptGroupLabel != null)
                LotdUiKit.AddImage(LotdUiKit.FillRect("LocalGroupLabel", tab).gameObject, DuelVisualAssets.PromptGroupLabel,
                    new Color(1f, 1f, 1f, .36f), false, false);
            LotdUiKit.AddImage(LotdUiKit.CreateRect("Accent", tab, Vector2.zero, Vector2.up,
                Vector2.zero, new Vector2(3f, 0f)).gameObject, LotdUiKit.WhiteSprite, DuelStyle.Side(opponent), false, false);
            bool hasIcon = DuelPromptLocationIcon.Supports(location);
            if (hasIcon)
            {
                var icon = LotdUiKit.CreateRect("Icon", tab, Vector2.zero, Vector2.up,
                    new Vector2(8f, 5.5f), new Vector2(26f, -5.5f));
                DuelPromptLocationIcon.Add(icon, location, opponent);
            }
            var caption = LotdUiKit.CreateRect("Caption", tab, Vector2.zero, Vector2.one,
                new Vector2(hasIcon ? 32f : 8f, 0f), new Vector2(-6f, 0f));
            LotdUiKit.AddFittedText(caption, "Location", $"{(opponent ? "상대" : "내")} {ChainLocationName(location)}", 15, 10,
                TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            return row;
        }

        private static string CardSourceLabel(DuelSession session, SelectableCardItem card) =>
            $"{(card.controller == session.HumanPlayerIndex ? "내" : "상대")} {ChainLocationName(card.location)}";

        private static string ChainLocationName(uint location) => YgoConstants.GetLocationNameKR(
            (location & YgoConstants.LOCATION_OVERLAY) != 0 ? YgoConstants.LOCATION_OVERLAY : location);

        private void ConfirmChainChoice()
        {
            if (!CanAnswerPrompt || _chainSelection < 0 || _curPrompt == null) return;
            if (_confirmPileActivation != null)
            {
                var confirm = _confirmPileActivation;
                int index = _chainCandidateIndices[_chainSelection];
                HidePrompt();
                confirm(index);
                return;
            }
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
                prompt.SelectionHint is 511 or 512 or 513 or 531 or 533;
        }

        private void RenderCardMultiSelect(DuelSession s, DuelPrompt p)
        {
            string hint = CardDatabase.ResolveDescription(p.SelectionHint);
            bool attackTarget = p.IsAttackTargetSelection;
            if (attackTarget)
            {
                _modalTitle.text = "공격 대상 선택";
                _modalSubtitle.text = "";
            }
            else if (p.messageType == YgoConstants.MSG_SELECT_TRIBUTE)
                _modalTitle.text = !string.IsNullOrEmpty(hint) ? hint : "릴리스할 몬스터 선택";
            else if (p.messageType == YgoConstants.MSG_SELECT_SUM)
                _modalTitle.text = !string.IsNullOrEmpty(hint) ? hint : "합계 선택";
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
            else if (!attackTarget && p.messageType == YgoConstants.MSG_SELECT_CARD && string.IsNullOrEmpty(hint))
            {
                _modalTitle.text = "필드에서 카드를 선택하세요.";
            }

            if (attackTarget)
            {
                if (IsFieldCardSelection) BuildAttackSelectionReadout(p);
                if (p.cancelable) _attackCancel = AddFooterButton("공격 취소", OnModalCloseClicked, false, alignRight: true);
                return;
            }

            bool fieldSum = IsFieldCardSelection && p.messageType == YgoConstants.MSG_SELECT_SUM;
            if (fieldSum) BuildFieldSumReadout(p.cancelable);
            _confirmBtn = AddFooterButton("선택 완료", OnConfirmMultiSelect, true);
            if (fieldSum && !p.cancelable)
                ((RectTransform)_confirmBtn.transform.parent).anchorMin = new Vector2(0f, .03f);
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
                detail = (lockedMust ? "필수 · " : "") + FormatSumValue(item);

            var tile = CreateChoiceCardTile(grid, $"Tile_{idx}", s, item, code, detail);
            var mark = AddSelectionMark(tile, lockedMust);
            if (lockedMust) return;

            bool capped = p.messageType != YgoConstants.MSG_SELECT_SUM || !p.sumExactCountZeroAny && p.max > 0;
            var button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile.GetComponent<Image>();
            PreviewOnFocus(button, code, item);
            button.onClick.AddListener(() =>
            {
                if (!CanAnswerPrompt || !ReferenceEquals(_curPrompt, p)) return;
                InspectChoice(code, item);
                if (p.IsAttackTargetSelection)
                {
                    SubmitAttackTarget(idx);
                    return;
                }
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
                int minimum = 0, maximum = 0;
                foreach (var card in _curPrompt.mustSelect.Concat(_selectedIndices.Select(index => _curPrompt.candidates[index])))
                {
                    var (first, alternative) = SumAmounts(card);
                    minimum += alternative > 0 ? Math.Min(first, alternative) : first;
                    maximum += Math.Max(first, alternative);
                }
                ShowSumTotal(minimum, maximum, target, valid);
            }

            _confirmBtn.interactable = valid;
        }

        private void BuildFieldSumReadout(bool cancelable)
        {
            float footerStart = cancelable ? .768f : .818f;
            _modalBody.anchorMin = new Vector2(.567f, .08f);
            _modalBody.anchorMax = new Vector2(footerStart - .012f, .92f);
            _modalFooter.anchorMin = new Vector2(footerStart, .08f);
            _sumAmount = AddMetric("SumAmount", 0f, .487f);
            _sumTarget = AddMetric("SumTarget", .513f, 1f);

            (Text Caption, Text Number) AddMetric(string name, float left, float right)
            {
                var tile = LotdUiKit.CreateRect(name, _modalBody, new Vector2(left, 0f), new Vector2(right, 1f),
                    Vector2.zero, Vector2.zero);
                DuelReadoutPlate.Add(tile, new Color(.075f, .134f, .160f, .98f));
                var heading = LotdUiKit.CreateRect("Heading", tile, new Vector2(.06f, .57f), new Vector2(.94f, .97f),
                    Vector2.zero, Vector2.zero);
                var caption = LotdUiKit.AddFittedText(heading, "Label", "", 14, 8, TextAnchor.MiddleCenter, DuelStyle.Muted);
                var digits = LotdUiKit.CreateRect("Digits", tile, new Vector2(.05f, .06f), new Vector2(.95f, .55f),
                    Vector2.zero, Vector2.zero);
                return (caption, DuelTypography.AddNumber(digits, "Value", "", 30, TextAnchor.MiddleCenter, Color.white));
            }
        }

        private void ShowSumTotal(int minimum, int maximum, int target, bool valid)
        {
            bool flexible = minimum != maximum;
            bool atLeast = _curPrompt.sumExactCountZeroAny;
            if (_sumAmount.Number == null)
            {
                string total = flexible ? $"최소 {minimum} · 최대 {maximum}" : $"합계 {maximum}";
                _modalSubtitle.text = $"{total} · 목표 {target}{(atLeast ? " 이상" : "")}";
                return;
            }

            _sumAmount.Caption.text = flexible ? "최소 / 최대" : "합계";
            _sumAmount.Number.text = flexible ? $"{minimum}/{maximum}" : maximum.ToString();
            _sumAmount.Number.color = valid ? DuelStyle.Gold : Color.white;
            _sumTarget.Caption.text = atLeast ? "최소 필요" : "목표 합계";
            _sumTarget.Number.text = target.ToString();
            int required = _curPrompt.mustSelect.Count;
            string fixedCards = required > 0 ? $"필수 {required}장 · " : "";
            _modalSubtitle.text = $"{fixedCards}선택 {_selectedIndices.Count}장";
        }

        private void OnConfirmMultiSelect()
        {
            if (_confirmBtn == null || !_confirmBtn.interactable) return;
            Submit(DuelSession.PackCardSelection(_selectedIndices.OrderBy(i => i).ToArray()));
        }

        private void BuildAttackSelectionReadout(DuelPrompt prompt)
        {
            _modalBox.anchorMin = new Vector2(.235f, .807f);
            _modalBox.anchorMax = new Vector2(.878f, .903f);
            _modalHeader.anchorMax = new Vector2(.215f, 1f);
            _modalBody.anchorMin = new Vector2(.225f, .055f);
            _modalBody.anchorMax = new Vector2(prompt.cancelable ? .819f : .987f, .945f);
            _modalFooter.anchorMin = new Vector2(.675f, .08f);
            _attackSourceReadout = AddAttackSelectionCard("AttackSource", 0f, .488f);
            _attackTargetReadout = AddAttackSelectionCard("AttackTarget", .512f, 1f);
            RefreshAttackSelectionReadout(prompt, null);
        }

        private (Text Name, DuelStatStrip Values, DuelRatingBadge Rating) AddAttackSelectionCard(string key, float left, float right)
        {
            var tile = LotdUiKit.CreateRect(key, _modalBody, new Vector2(left, 0f), new Vector2(right, 1f),
                Vector2.zero, Vector2.zero);
            var heading = LotdUiKit.CreateRect("Heading", tile, new Vector2(0f, .53f), Vector2.one,
                Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(heading, new Color(.077f, .115f, .139f, 1f));
            var name = LotdUiKit.AddFittedText(heading, "Name", "", 16, 9, TextAnchor.MiddleCenter, Color.white);
            var values = LotdUiKit.CreateRect("Combat", tile, Vector2.zero, new Vector2(1f, .51f),
                Vector2.zero, Vector2.zero);
            return (name, new DuelStatStrip(values, compact: true), DuelRatingBadge.AddToHeading(name));
        }

        internal void RefreshAttackSelectionReadout(DuelPrompt prompt, (byte controller, uint location, uint sequence)? focus)
        {
            if (!IsFieldCardSelection || _attackSourceReadout.Name == null || !ReferenceEquals(prompt, _curPrompt)) return;
            var source = _curSession.GetAttackSelectionSource(prompt);
            int index = prompt.candidates.Count == 1 ? 0 : -1;
            if (focus.HasValue && _fieldCardIndices.TryGetValue(focus.Value, out int focused))
                index = focused;
            var candidate = index >= 0 && index < prompt.candidates.Count ? prompt.candidates[index] : null;
            var target = candidate != null
                ? _curSession.GetVisibleFieldCard(candidate.code, candidate.controller, candidate.location, candidate.sequence)
                : null;
            int attackerSide = source != null ? source.controller : _curSession.TurnPlayer;
            bool opponentAttacks = attackerSide != _curSession.HumanPlayerIndex;
            bool opponentTarget = candidate != null ? candidate.controller != _curSession.HumanPlayerIndex : !opponentAttacks;
            BindAttackSelectionCard(_attackSourceReadout, source, opponentAttacks,
                opponentAttacks ? "상대 공격" : "공격", opponentAttacks ? "상대 공격 몬스터" : "공격 몬스터");
            BindAttackSelectionCard(_attackTargetReadout, target, opponentTarget, "대상",
                candidate != null ? "대상 · 뒷면 카드" : "공격 대상");
        }

        private static void BindAttackSelectionCard((Text Name, DuelStatStrip Values, DuelRatingBadge Rating) tile, LiveCardState card,
            bool opponent, string label, string fallback)
        {
            tile.Name.text = card == null ? fallback : $"{label} · {CardDatabase.GetName(card.code)}";
            tile.Name.color = DuelStyle.Side(opponent);
            ((RectTransform)tile.Name.transform.parent).anchorMin = new Vector2(0f, card == null ? 0f : .53f);
            if (card == null)
            {
                tile.Rating.Bind(default);
                tile.Values.Hide();
                return;
            }
            var readout = new DuelCardReadout(CardDatabase.Get(card.code), card);
            tile.Rating.Bind(readout.RatingNumber);
            tile.Values.Bind(readout, opponent);
        }

        private void SubmitAttackTarget(int index)
        {
            LotdAudio.PlayClick();
            Submit(DuelSession.PackCardSelection(index));
        }

        private static (int First, int Alternative) SumAmounts(SelectableCardItem item) =>
            ((int)(item.sumParam & 0xFFFF), (int)(item.sumParam >> 16));

        private static string FormatSumValue(SelectableCardItem item)
        {
            var (first, alternative) = SumAmounts(item);
            return alternative > 0 && alternative != first ? $"{first}/{alternative}" : first.ToString();
        }

        public static bool CheckSumSelectionValid(DuelPrompt p, HashSet<int> chosen, int target)
        {
            if (p == null || chosen == null) return false;

            var items = new List<(int a, int b)>();
            foreach (var m in p.mustSelect)
                items.Add(SumAmounts(m));
            foreach (int i in chosen)
            {
                if (i < 0 || i >= p.candidates.Count) return false;
                items.Add(SumAmounts(p.candidates[i]));
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
            _modalTitle.text = CardDatabase.ResolveDescription(p.SelectionHint) ?? "소재 / 대상 선택";
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
            var tile = CreateChoiceCardTile(grid, $"Imm_{nativeIndex}", session, item, code);
            if (selected) AddSelectionMark(tile, true);
            var button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile.GetComponent<Image>();
            PreviewOnFocus(button, code, item);
            button.onClick.AddListener(() =>
            {
                if (!CanAnswerPrompt || !ReferenceEquals(_curPrompt, prompt)) return;
                InspectChoice(code, item);
                LotdAudio.PlayClick();
                Submit(PackTwoInts(1, nativeIndex));
            });
        }

        private void RenderSelectCounter(DuelSession session, DuelPrompt p)
        {
            _modalTitle.text = $"{DuelCounterBadge.TypeName(p.counterType)} 제거";
            _counterAlloc = new int[p.candidates.Count];

            RectTransform grid = BuildChoiceCardStrip(p.candidates.Count, CounterCardWidth);
            var countLabels = new Text[p.candidates.Count];

            for (int i = 0; i < p.candidates.Count; i++)
            {
                int idx = i;
                var c = p.candidates[i];
                uint code = session.GetVisibleSelectionCode(c);
                var tile = CreateCardStripTile(grid, $"Cnt_{i}", CounterCardWidth);
                var cRt = LotdUiKit.CreateRect("Card", tile, new Vector2(.055f, .346f), new Vector2(.945f, .966f),
                    Vector2.zero, Vector2.zero);
                BuildCandidateCard(cRt, session, c, code, showCounters: false);

                countLabels[i] = DuelTypography.AddNumber(LotdUiKit.CreateRect("Lbl", tile, new Vector2(.05f, .215f),
                    new Vector2(.95f, .343f), Vector2.zero, Vector2.zero),
                    "L", $"0 / {c.currentCounter}", 21, TextAnchor.MiddleCenter, DuelCounterBadge.Ink);

                var minus = LotdUiKit.CreateRect("M", tile, new Vector2(.064f, .017f), new Vector2(.465f, .201f),
                    Vector2.zero, Vector2.zero);
                var decrease = DuelStyle.Button(minus, "B", "−", () =>
                {
                    if (_counterAlloc[idx] > 0) _counterAlloc[idx]--;
                    countLabels[idx].text = $"{_counterAlloc[idx]} / {c.currentCounter}";
                    UpdateCounterConfirm();
                }, 21);
                var plus = LotdUiKit.CreateRect("P", tile, new Vector2(.535f, .017f), new Vector2(.936f, .201f),
                    Vector2.zero, Vector2.zero);
                var increase = DuelStyle.Button(plus, "B", "+", () =>
                {
                    int curSum = _counterAlloc.Sum();
                    if (_counterAlloc[idx] < c.currentCounter && curSum < p.min) _counterAlloc[idx]++;
                    countLabels[idx].text = $"{_counterAlloc[idx]} / {c.currentCounter}";
                    UpdateCounterConfirm();
                }, 21);
                PreviewOnFocus(decrease, code, c);
                PreviewOnFocus(increase, code, c);
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
            _modalSubtitle.text = $"제거할 수량 {sum} / {_curPrompt.min}개";
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
            _modalSubtitle.text = p.isChainSort ? "카드를 눌러 발동 순서 지정 · 처리는 오른쪽부터" :
                deckOrder ? $"왼쪽이 덱 맨 {deckEdge} · 화살표로 카드 이동" : "왼쪽부터 1번 · 화살표로 카드 이동";
            _sortOrder = Enumerable.Range(0, p.candidates.Count).ToArray();
            int picked = 0;

            RectTransform grid = BuildChoiceCardStrip(p.candidates.Count);
            void RebuildTiles(int focusPosition = -1, int focusDirection = 0)
            {
                ClearChildren(grid);
                Button restore = null;

                for (int pos = 0; pos < _sortOrder.Length; pos++)
                {
                    int position = pos;
                    var item = p.candidates[_sortOrder[pos]];
                    uint code = item.code;
                    var tile = CreateCardStripTile(grid, $"Sort_{pos}", ChoiceCardWidth);
                    string rank = p.isChainSort ? $"발동 {pos + 1} · 처리 {_sortOrder.Length - pos}" :
                        deckOrder && pos == 0 ? $"1번 · 맨 {deckEdge}" : $"{pos + 1}번";
                    var order = LotdUiKit.CreateRect("Order", tile, new Vector2(.042f, .805f),
                        new Vector2(.958f, .966f), Vector2.zero, Vector2.zero);
                    LotdUiKit.AddImage(order.gameObject, LotdUiKit.WhiteSprite, DuelStyle.HeaderColor, false, false);
                    LotdUiKit.AddFittedText(order, "Text", rank, 16, 9, TextAnchor.MiddleCenter,
                        p.isChainSort ? pos < picked ? DuelStyle.Gold : LotdUiKit.TextPrimary :
                        pos == 0 ? DuelStyle.Gold : LotdUiKit.TextPrimary, FontStyle.Bold);

                    var art = LotdUiKit.CreateRect("Card", tile, new Vector2(.08f, .219f),
                        new Vector2(.92f, .79f), Vector2.zero, Vector2.zero);
                    BuildCandidateCard(art, session, item, code);

                    if (p.isChainSort)
                    {
                        var pick = DuelStyle.Button(art, "PickOrder", "", () =>
                        {
                            if (!CanAnswerPrompt || !ReferenceEquals(_curPrompt, p)) return;
                            if (position < picked || picked == _sortOrder.Length) picked = 0;
                            int original = _sortOrder[position];
                            Array.Copy(_sortOrder, picked, _sortOrder, picked + 1, position - picked);
                            _sortOrder[picked++] = original;
                            RebuildTiles(Mathf.Min(picked, _sortOrder.Length - 1), 0);
                        }, 16);
                        pick.targetGraphic.color = Color.clear;
                        pick.transition = Selectable.Transition.None;
                        if (pick.TryGetComponent<Outline>(out var outline)) outline.enabled = false;
                        PreviewOnFocus(pick, code, item);
                        if (pos == focusPosition && focusDirection == 0) restore = pick;
                        if (_mobileLayout) continue;
                    }

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
                            picked = 0;
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
                        PreviewOnFocus(button, code, item);
                        return button;
                    }

                    var earlier = MoveButton(-1);
                    var later = MoveButton(1);
                    if (p.isChainSort) art.SetAsLastSibling();
                    if (pos == focusPosition && (focusDirection != 0 || !p.isChainSort))
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

        private void RenderAnnounceRace(DuelPrompt p)
        {
            _modalTitle.text = CardDatabase.ResolveDescription(p.SelectionHint) ?? "종족 선언";
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
            _modalTitle.text = CardDatabase.ResolveDescription(p.SelectionHint) ?? "속성 선언";
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
            _modalTitle.text = CardDatabase.ResolveDescription(p.SelectionHint) ?? "카드명 선언";

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
            var scroll = CreateHorizontalCardGrid(gridHolder, out Transform grid, ZoneCardWidth, ZoneCardHeight)
                .GetComponent<ScrollRect>();
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
            var shade = LotdUiKit.FillRect("Shade", dim);
            _zoneShade = shade.gameObject.AddComponent<DuelPromptBackdrop>();
            _zoneShade.sprite = LotdUiKit.WhiteSprite;
            var dismiss = shade.gameObject.AddComponent<Button>();
            dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
            dismiss.onClick.AddListener(CloseZoneViewer);

            var content = _mobileLayout ? MobileSafeArea.Create(dim) : dim;
            var win = _zoneWindow = DuelStyle.Panel(content, "Win",
                new Vector2(.208f, .198f), new Vector2(.976f, .646f),
                fill: new Color(.039f, .071f, .093f, .97f));
            win.gameObject.AddComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };

            var hdr = _zoneHeader = LotdUiKit.CreateRect("Hdr", win, Vector2.up, Vector2.one, new Vector2(0f, -53f), Vector2.zero);
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

            BuildPileTabs(win);
            var body = _zoneBody = LotdUiKit.CreateRect("Body", win, Vector2.zero, Vector2.one,
                new Vector2(10f, 8f), new Vector2(-10f, -62f));
            _zoneScroll = CreateHorizontalCardGrid(body, out Transform g, ZoneCardWidth, ZoneCardHeight).GetComponent<ScrollRect>();
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

            _zoneDetailsRow = LotdUiKit.CreateRect("CardDetails", win, Vector2.zero, Vector2.right,
                new Vector2(10f, 8f), new Vector2(-10f, 45f));
            var selectedName = LotdUiKit.CreateRect("SelectedName", _zoneDetailsRow, Vector2.zero, new Vector2(.57f, 1f),
                Vector2.zero, Vector2.zero);
            _zoneSelectedName = LotdUiKit.AddFittedText(selectedName, "Name", "", 15, 9, TextAnchor.MiddleLeft, Color.white);
            var details = LotdUiKit.CreateRect("Open", _zoneDetailsRow, new Vector2(.60f, 0f), Vector2.one,
                Vector2.zero, Vector2.zero);
            _zoneDetailsButton = DuelStyle.Button(details, "Button", "카드 상세 (X)", () =>
            {
                if (IsZoneViewerOpen && _zoneSelectedCode != 0) _onOpenZoneDetails?.Invoke(_zoneSelectedCode);
            }, 13);
            _zoneDetailsButton.interactable = false;
            ZoneViewRoot.SetActive(false);
        }

        private void BuildPileTabs(RectTransform window)
        {
            _zoneTabs = LotdUiKit.CreateRect("PileTabs", window, Vector2.up, Vector2.one,
                new Vector2(10f, -143f), new Vector2(-10f, -60f));
            for (int i = 0; i < _zoneOwnerButtons.Length; i++)
            {
                bool opponent = i == 1;
                float left = i * .51f;
                var slot = LotdUiKit.CreateRect(opponent ? "Opponent" : "Player", _zoneTabs,
                    new Vector2(left, .54f), new Vector2(left + .49f, 1f), Vector2.zero, Vector2.zero);
                _zoneOwnerButtons[i] = CreatePileTab(slot, opponent ? "상대 카드" : "내 카드", () =>
                {
                    if (opponent != _zoneOpponent) _onBrowsePile?.Invoke(opponent, PileTabs[_zonePileIndex].Location);
                });
            }
            for (int i = 0; i < PileTabs.Length; i++)
            {
                var (location, label) = PileTabs[i];
                float left = i * .337f;
                var slot = LotdUiKit.CreateRect($"Pile_{i}", _zoneTabs,
                    new Vector2(left, 0f), new Vector2(left + .326f, .46f), Vector2.zero, Vector2.zero);
                _zonePileButtons[i] = CreatePileTab(slot, label, () =>
                {
                    if (location != PileTabs[_zonePileIndex].Location) _onBrowsePile?.Invoke(_zoneOpponent, location);
                });
            }
        }

        private static Button CreatePileTab(RectTransform slot, string label, Action onClick)
        {
            var button = DuelStyle.Button(slot, "Button", label, onClick, 15);
            var text = button.GetComponentInChildren<Text>();
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 9;
            text.resizeTextMaxSize = 15;
            return button;
        }

        public void CloseZoneViewer()
        {
            if (!IsZoneViewerOpen) return;
            bool restoreFocus = FocusIsInside(ZoneViewRoot);
            ZoneViewRoot.SetActive(false);
            _promptInput.interactable = true;
            if (restoreFocus) SetFocus(IsShowingField ? _fieldViewButton.gameObject : _focusBeforeZone);
            _focusBeforeZone = null;
            ClearZoneCards();
        }

        private void ClearZoneCards()
        {
            _zoneSelectedBorder = null;
            _zoneSelectedCode = 0;
            _zoneSelectedName.text = "카드를 선택하세요";
            _zoneDetailsButton.interactable = false;
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
            PopulateZoneCards(cards, hideFaceDown, newestFirst, hasCardActions, onOpenCardActions, canUseCardActions, actionsOnly);
            FinishZoneViewer(title, icon);
        }

        internal void ShowPileViewer(bool opponent, uint location, PlayerFieldSnapshot piles,
            Func<LiveCardState, bool> hasCardActions, Action<LiveCardState> onOpenCardActions,
            Func<bool> canUseCardActions, bool actionsOnly, IReadOnlyList<LiveCardState> banished = null)
        {
            int index = Array.FindIndex(PileTabs, tab => tab.Location == location);
            if (index < 0) return;
            IReadOnlyList<LiveCardState> cards = location switch
            {
                YgoConstants.LOCATION_GRAVE => piles.grave,
                YgoConstants.LOCATION_REMOVED => banished ?? piles.banished,
                _ => piles.extra,
            };
            bool browsePiles = !actionsOnly && _onBrowsePile != null;
            bool entering = StartZoneViewer(sidePanel: true, browsePiles: browsePiles);
            _zoneOpponent = opponent;
            _zonePileIndex = index;
            if (browsePiles)
            {
                DuelStyle.ApplyButton(_zoneOwnerButtons[0], !opponent);
                DuelStyle.ApplyButton(_zoneOwnerButtons[1], opponent);
                UpdatePileTab(0, piles.grave.Count);
                UpdatePileTab(1, piles.banished.Count);
                UpdatePileTab(2, piles.extra.Count);
            }

            bool extra = location == YgoConstants.LOCATION_EXTRA;
            PopulateZoneCards(cards, hideFaceDown: opponent && location != YgoConstants.LOCATION_GRAVE,
                newestFirst: !extra, hasCardActions, onOpenCardActions, canUseCardActions, actionsOnly);
            string label = extra ? "엑스트라 덱" : PileTabs[index].Label;
            FinishZoneViewer($"{(opponent ? "상대" : "내")} {label}",
                (location == YgoConstants.LOCATION_GRAVE ? DuelVisualAssets.PromptGrave : null) ??
                DuelVisualAssets.ZoneIcon(location, opponent), entering,
                browsePiles ? _zonePileButtons[index] : null);
        }

        private void UpdatePileTab(int index, int count)
        {
            var button = _zonePileButtons[index];
            button.GetComponentInChildren<Text>().text = $"{PileTabs[index].Label} {count}";
            DuelStyle.ApplyButton(button, index == _zonePileIndex);
        }

        private void PopulateZoneCards(IReadOnlyList<LiveCardState> cards, bool hideFaceDown, bool newestFirst,
            Func<LiveCardState, bool> hasCardActions, Action<LiveCardState> onOpenCardActions,
            Func<bool> canUseCardActions, bool actionsOnly)
        {
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

        private bool StartZoneViewer(bool sidePanel = false, bool browsePiles = false)
        {
            bool switchingPile = browsePiles && IsZoneViewerOpen && _zoneTabs.gameObject.activeSelf;
            if (switchingPile) ClearZoneCards();
            else
            {
                CloseZoneViewer();
                _focusBeforeZone = EventSystem.current?.currentSelectedGameObject;
                _promptInput.interactable = false;
                ZoneViewRoot.SetActive(true);
            }
            _zoneTabs.gameObject.SetActive(browsePiles);
            _zoneBody.offsetMax = new Vector2(-10f, browsePiles ? -151f : -62f);
            _zoneWindow.anchorMin = sidePanel ? new Vector2(browsePiles ? .76f : .817f, .163f) : new Vector2(.208f, .198f);
            _zoneWindow.anchorMax = sidePanel ? new Vector2(.988f, .871f) : new Vector2(.976f, .646f);
            if (_mobileLayout)
            {
                _zoneWindow.anchorMin = sidePanel
                    ? new Vector2(browsePiles ? .67f : .735f, .055f) : new Vector2(.208f, .14f);
                _zoneWindow.anchorMax = sidePanel ? new Vector2(.988f, .945f) : new Vector2(.976f, .805f);
            }
            _zoneShade.color = new Color(.015f, .022f, .038f, sidePanel ? .14f : .40f);

            var close = (RectTransform)_zoneClose.transform.parent;
            close.anchorMin = new Vector2(sidePanel ? .77f : .82f, .15f);
            _zoneClose.GetComponentInChildren<Text>().text = sidePanel ? "✕" : "✕ 닫기";
            var grid = _zoneGrid.GetComponent<GridLayoutGroup>();
            grid.constraint = sidePanel ? GridLayoutGroup.Constraint.FixedColumnCount : GridLayoutGroup.Constraint.Flexible;
            grid.constraintCount = 1;
            grid.childAlignment = sidePanel ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;
            grid.padding = sidePanel ? new RectOffset(4, 4, 6, 6) : new RectOffset(10, 10, 10, 10);
            grid.cellSize = sidePanel
                ? new Vector2(Mathf.Clamp(_zoneScroll.viewport.rect.width - 10f, 52f, ZoneCardWidth), ZoneCardHeight)
                : new Vector2(ZoneCardWidth, ZoneCardHeight);
            if (_mobileLayout)
            {
                float touch = MobileSafeArea.MinimumTouchSize(_zoneWindow);
                grid.cellSize = new Vector2(Mathf.Max(grid.cellSize.x, touch), Mathf.Max(grid.cellSize.y, touch * 1.22f));
            }
            return !switchingPile;
        }

        private void FinishZoneViewer(string title, Sprite icon = null, bool entering = true, Button emptyFocus = null)
        {
            int count = _zoneGrid.childCount;
            _zoneTitle.text = $"{title}   ·   {count}장";
            _zoneIcon.sprite = icon;
            _zoneIcon.enabled = icon != null;
            _zoneTitle.rectTransform.offsetMin = icon != null ? new Vector2(34f, 0f) : Vector2.zero;
            _zoneEmpty.gameObject.SetActive(count == 0);
            var firstCard = FirstFocus(_zoneGrid);
            bool showDetails = _onOpenZoneDetails != null && firstCard != null;
            _zoneDetailsRow.gameObject.SetActive(showDetails);
            _zoneBody.offsetMin = new Vector2(10f, showDetails ? 51f : 8f);
            if (_mobileLayout) LayoutMobileZoneControls(showDetails);
            _zoneScroll.StopMovement();
            _zoneGrid.anchoredPosition = Vector2.zero;
            ZoneViewRoot.transform.SetAsLastSibling();
            CardAnimationManager.Instance?.BringToFront();
            PrepareFocusControls(ZoneViewRoot.transform, CloseZoneViewer);
            Canvas.ForceUpdateCanvases();
            SetFocus((firstCard ?? emptyFocus ?? _zoneClose).gameObject);
            if (entering) UiEntranceMotion.Play(ZoneViewRoot, duration: .13f);
        }

        private void LayoutMobileZoneControls(bool details)
        {
            float touch = MobileSafeArea.MinimumTouchSize(_zoneWindow);
            float header = touch + 8f;
            _zoneHeader.offsetMin = new Vector2(0f, -header);
            var close = (RectTransform)_zoneClose.transform.parent;
            float closeLeft = Mathf.Min(close.anchorMin.x, close.anchorMax.x - touch / Mathf.Max(touch, _zoneWindow.rect.width));
            close.anchorMin = new Vector2(closeLeft, 0f);
            var title = (RectTransform)_zoneTitle.transform.parent;
            title.anchorMax = new Vector2(Mathf.Min(.80f, closeLeft - .02f), title.anchorMax.y);
            close.anchorMax = new Vector2(close.anchorMax.x, 1f);
            close.offsetMin = new Vector2(0f, 4f);
            close.offsetMax = new Vector2(0f, -4f);

            float tabs = Mathf.Ceil(touch / .46f);
            _zoneTabs.offsetMax = new Vector2(-10f, -header - 6f);
            _zoneTabs.offsetMin = new Vector2(10f, -header - 6f - tabs);
            _zoneDetailsRow.offsetMax = new Vector2(-10f, 8f + touch);
            _zoneBody.offsetMax = new Vector2(-10f, -header - (_zoneTabs.gameObject.activeSelf ? tabs + 14f : 8f));
            _zoneBody.offsetMin = new Vector2(10f, details ? touch + 14f : 8f);
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
            var card = CompositeCardView.Create(cRt, "V", false, duelArtwork: true);
            card.SetCard(code, faceDown, faceDown ? null : live);
            if (hasActions && live?.location is YgoConstants.LOCATION_GRAVE or YgoConstants.LOCATION_REMOVED)
            {
                var glow = DuelStyle.AddActivationGlow(card.transform.Find("AspectBox"), 6f);
                glow.gameObject.AddComponent<DuelActivationPulse>().Glow = glow;
                glow.gameObject.SetActive(true);
            }
            string caption = faceDown ? "비공개 카드" : CardDatabase.GetName(code);
            LotdUiKit.AddFittedText(LotdUiKit.CreateRect("L", tile,
                new Vector2(0.05f, hasActions ? 0.17f : 0.02f), new Vector2(0.95f, hasActions ? 0.29f : 0.17f), Vector2.zero, Vector2.zero),
                "T", caption, 12, 8, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary);

            void OpenAction()
            {
                if (!IsZoneViewerOpen || canUseCardActions?.Invoke() == false) return;
                _onInspectCard?.Invoke(code);
                CloseZoneViewer();
                onOpenCardActions?.Invoke(live);
            }

            bool fullTileAction = hasActions && _mobileLayout;
            if (!faceDown && code != 0)
            {
                var btn = tile.gameObject.AddComponent<Button>();
                btn.targetGraphic = tile.GetComponent<Image>();
                btn.onClick.AddListener(() =>
                {
                    if (fullTileAction) OpenAction();
                    else SelectZoneCard(tile, code);
                });
                SelectionHandler(btn).OnSelected = () => SelectZoneCard(tile, code);
                var trig = tile.gameObject.AddComponent<CardDragTrigger>();
                trig.OnPointerEnterAction = () => _onInspectCard?.Invoke(code);
                if (actionSelection && hasActions && live?.location == YgoConstants.LOCATION_EXTRA)
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
                    ? live.location == YgoConstants.LOCATION_EXTRA ? "특수 소환" : "효과 발동"
                    : "행동 확인";
                if (fullTileAction)
                {
                    LotdUiKit.AddFittedText(actRt, "Label", actionText, 13, 10, TextAnchor.MiddleCenter, DuelStyle.Gold);
                    return;
                }
                var action = DuelStyle.Button(actRt, "B", actionText, OpenAction, 11, true);
                SelectionHandler(action).OnSelected = () => SelectZoneCard(tile, code);
            }
        }

        private void SelectZoneCard(RectTransform tile, uint code)
        {
            var border = tile.GetComponent<Outline>();
            if (_zoneSelectedBorder != border)
            {
                if (_zoneSelectedBorder != null) _zoneSelectedBorder.effectColor = _zonePreviousBorder;
                _zoneSelectedBorder = border;
                _zonePreviousBorder = border.effectColor;
                border.effectColor = DuelStyle.Player;
            }
            _zoneSelectedCode = code;
            _zoneSelectedName.text = CardDatabase.GetName(code);
            _zoneDetailsButton.interactable = true;
            _onInspectCard?.Invoke(code);
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
            var r1 = LotdUiKit.CreateRect("Rematch", dim, new Vector2(.22f, .169f), new Vector2(.397f, .234f), Vector2.zero, Vector2.zero);
            _rematchButton = DuelStyle.Button(r1, "B", "재대결", () => { HideResult(); _resRematch?.Invoke(); }, 18, gold: true);

            var r2 = LotdUiKit.CreateRect("OpponentSelect", dim, new Vector2(.411f, .169f), new Vector2(.589f, .234f), Vector2.zero, Vector2.zero);
            DuelStyle.Button(r2, "B", "상대 선택", () => { HideResult(); _resOppSel?.Invoke(); }, 18);

            var r3 = LotdUiKit.CreateRect("MainMenu", dim, new Vector2(.603f, .169f), new Vector2(.78f, .234f), Vector2.zero, Vector2.zero);
            DuelStyle.Button(r3, "B", "메인 메뉴", () => { HideResult(); _resMenu?.Invoke(); }, 18);

            ResultRoot.SetActive(false);
        }

        public void ShowResult(bool playerWon, bool isDraw, OpponentCharacter opp, int turns, int myLp, int oppLp,
            Action onRematch, Action onOppSelect, Action onMenu, string rewardText = "")
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
            ResultRoot.transform.SetAsLastSibling();
            ResultRoot.SetActive(true);
            CardAnimationManager.Instance?.BringToFront();
            PrepareFocusControls(ResultRoot.transform, TriggerMenu);
            SetFocus(_rematchButton.gameObject);
            UiEntranceMotion.Play(ResultRoot, duration: .22f);
        }

        // ==================== UI HELPERS ====================
        internal bool RestoreFocusAfterCardDetails(GameObject previous)
        {
            if (!IsZoneViewerOpen && !IsPromptOpen) return false;

            var scope = IsZoneViewerOpen ? ZoneViewRoot.transform : _padKeyboard.IsOpen ? _padKeyboard.Scope : ModalRoot.transform;
            if (previous != null && CanNavigate(previous.GetComponent<Selectable>(), scope)) SetFocus(previous);
            else if (IsZoneViewerOpen) SetFocus(_zoneSelectedBorder != null ? _zoneSelectedBorder.gameObject : _zoneClose.gameObject);
            else if (_padKeyboard.IsOpen) SetFocus(_padKeyboard.DefaultFocus.gameObject);
            else if (IsShowingField) SetFocus(_fieldViewButton.gameObject);
            else FocusPrompt();
            return true;
        }

        private void FocusPrompt()
        {
            if (EventSystem.current == null) return;
            Canvas.ForceUpdateCanvases();
            SetFocus(IsFieldCardSelection && _curPrompt.IsAttackTargetSelection ? null : PreferredPromptControl().gameObject);
        }

        private Selectable PreferredPromptControl()
        {
            if (_chainPrompt)
            {
                return _chainSelection >= 0 ? _activationButtons[_chainSelection] : _fieldViewButton;
            }
            return FirstFocus(_modalBody) ?? FirstFocus(_modalFooter) ?? _fieldViewButton;
        }

        private static bool CanNavigate(Selectable control, Transform scope) =>
            control != null && control.transform.IsChildOf(scope) && control.isActiveAndEnabled &&
            control.IsInteractable() && control.navigation.mode != Navigation.Mode.None && (control is Button || control is InputField);

        private static Selectable FirstFocus(Transform parent) =>
            parent.GetComponentsInChildren<Selectable>().FirstOrDefault(control => CanNavigate(control, parent));

        private void InspectChoice(uint code, SelectableCardItem item)
        {
            if (code == 0) return;
            if (item != null && _onInspectCandidate != null) _onInspectCandidate(item, code);
            else _onInspectCard?.Invoke(code);
        }

        private static SelectableCardItem ReadFieldEffectSource(DuelSession session, DuelPrompt prompt, uint code)
        {
            if (code == 0) return null;
            if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == code)
                return PublicSource(prompt.promptCon, prompt.promptLoc, prompt.promptSeq);
            var link = session.SelectionChainLink;
            return link?.code == code ? PublicSource(link.triggerPlayer, link.triggerLoc, link.triggerSeq) : null;

            SelectableCardItem PublicSource(byte controller, uint location, uint sequence)
            {
                var live = session.GetVisibleFieldCard(code, controller, location, sequence);
                return live == null ? null : new SelectableCardItem
                {
                    code = code, controller = controller, location = location, sequence = sequence, position = live.position,
                };
            }
        }

        private void PreviewOnFocus(Selectable control, uint code, SelectableCardItem item = null)
        {
            if (code != 0) SelectionHandler(control).OnSelected = () => InspectChoice(code, item);
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

        private Button AddFooterButton(string label, Action onClick, bool gold, bool alignRight = false)
        {
            bool right = gold || alignRight;
            var slot = LotdUiKit.CreateRect($"Btn_{label}", _modalFooter,
                new Vector2(right ? .52f : 0f, .03f), new Vector2(right ? 1f : .48f, .97f), Vector2.zero, Vector2.zero);
            return DuelStyle.Button(slot, "B", label, onClick, 16, gold);
        }

        private static float TouchFooterTop(RectTransform box, RectTransform footer, float buttonFill)
        {
            float height = ((RectTransform)box.parent).rect.height * (box.anchorMax.y - box.anchorMin.y);
            float touch = MobileSafeArea.MinimumTouchSize(footer);
            return Mathf.Min(.41f, Mathf.Max(.19f, footer.anchorMin.y + touch / Mathf.Max(1f, height * buttonFill)));
        }

        private void EnlargeConfirmationFooter()
        {
            _modalBox.anchorMin = new Vector2(.194f, .16f);
            _modalBox.anchorMax = new Vector2(.901f, .735f);
            float top = TouchFooterTop(_modalBox, _modalFooter, .94f);
            _modalFooter.anchorMin = new Vector2(.42f, _modalFooter.anchorMin.y);
            _modalFooter.anchorMax = new Vector2(.977f, top);
            _modalBody.anchorMin = new Vector2(_modalBody.anchorMin.x, top + .019f);
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

        private RectTransform BuildChoiceCardStrip(int count, float cardWidth = ChoiceCardWidth)
        {
            var strip = LotdUiKit.FillRect("Cards", _modalBody);
            ScrollRect scroll = CreateCardStrip(strip, out RectTransform cards, ChoiceCardGap);
            ResetCardStrip(scroll, count, cardWidth, ChoiceCardGap);
            return cards;
        }

        private RectTransform CreateChoiceCardTile(Transform parent, string name, DuelSession session,
            SelectableCardItem item, uint code, string detail = null)
        {
            var tile = CreateCardStripTile(parent, name, ChoiceCardWidth);
            var art = LotdUiKit.CreateRect("Card", tile, new Vector2(.045f, .246f), new Vector2(.955f, .976f),
                Vector2.zero, Vector2.zero);
            BuildCandidateCard(art, session, item, code);

            var title = LotdUiKit.CreateRect("Name", tile, new Vector2(.04f, .137f), new Vector2(.96f, .244f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(title, "Text", code != 0 ? CardDatabase.GetName(code) : "뒷면 카드", 12, 8,
                TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            AddChoiceCardSource(tile, item.location, item.controller != session.HumanPlayerIndex, detail);
            if (code != 0)
                tile.gameObject.AddComponent<CardDragTrigger>().OnPointerEnterAction = () => InspectChoice(code, item);
            return tile;
        }

        private static void BuildCandidateCard(Transform art, DuelSession session, SelectableCardItem item, uint code,
            bool showCounters = true)
        {
            var live = session.GetVisibleFieldCard(code, item.controller, item.location, item.sequence);
            var view = CompositeCardView.Create(art, "V", duelArtwork: true);
            view.SetCard(code, faceDown: code == 0, live: live);
            if (live == null) return;

            var readout = new DuelCardReadout(CardDatabase.Get(code), live);
            var rating = readout.RatingAt(item.location, item.sequence);
            if (item.location == YgoConstants.LOCATION_MZONE && readout.IsMonster || rating.Kind == DuelRatingKind.Scale ||
                showCounters && readout.CounterCount != 0)
                new DuelFieldReadout(view.transform.Find("AspectBox"), inPreview: true)
                    .Bind(readout, item.controller != session.HumanPlayerIndex, rating, showCounters: showCounters);
        }

        private static void AddChoiceCardSource(Transform tile, uint location, bool opponent, string detail)
        {
            bool hasIcon = DuelPromptLocationIcon.Supports(location);
            bool hasDetail = !string.IsNullOrEmpty(detail);
            if (!hasIcon && !hasDetail) return;

            var badge = LotdUiKit.CreateRect("Source", tile, new Vector2(hasDetail ? .065f : .5f, .016f),
                new Vector2(hasDetail ? .935f : .5f, .016f), new Vector2(hasDetail ? 0f : -17f, 0f),
                new Vector2(hasDetail ? 0f : 17f, 22f));
            var tone = DuelStyle.Side(opponent);
            LotdUiKit.AddImage(badge.gameObject, LotdUiKit.WhiteSprite,
                new Color(tone.r * .22f, tone.g * .22f, tone.b * .22f, .95f), false, false);
            var edge = LotdUiKit.CreateRect("Accent", badge, Vector2.zero, Vector2.right, Vector2.zero, new Vector2(0f, 1f));
            LotdUiKit.AddImage(edge.gameObject, LotdUiKit.WhiteSprite, tone, false, false);
            if (hasIcon)
            {
                float iconLeft = hasDetail ? 5f : 8f;
                var icon = LotdUiKit.CreateRect("LocationIcon", badge, Vector2.zero, Vector2.zero,
                    new Vector2(iconLeft, 1.5f), new Vector2(iconLeft + 18f, 20.5f));
                DuelPromptLocationIcon.Add(icon, location, opponent);
            }
            if (hasDetail)
            {
                var caption = LotdUiKit.CreateRect("Detail", badge, Vector2.zero, Vector2.one,
                    new Vector2(hasIcon ? 27f : 0f, 0f), new Vector2(-2f, 0f));
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
            var mark = LotdUiKit.CreateRect("Chk", tile, Vector2.one, Vector2.one,
                new Vector2(-30f, -30f), new Vector2(-6f, -6f));
            LotdUiKit.AddImage(mark.gameObject, DuelStyle.Disc, DuelStyle.Gold, true, false);
            var tick = LotdUiKit.CreateRect("Tick", mark, Vector2.one * .17f, Vector2.one * .83f,
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(tick.gameObject, LotdUiKit.LoadSprite("LOTD/Icons/ICON_ID_CHECKMARK"),
                new Color(.10f, .15f, .19f), true, false);
            SetSelectionMark(mark, selected);
            return mark;
        }

        private static void SetSelectionMark(Transform mark, bool selected)
        {
            mark.gameObject.SetActive(selected);
            SetSelectionOutline(mark.parent.GetComponent<Outline>(), selected);
        }

        private static void SetSelectionOutline(Outline outline, bool selected)
        {
            outline.effectColor = selected ? DuelStyle.Gold : DuelStyle.BorderColor;
            outline.effectDistance = new Vector2(1f, -1f) * (selected ? 3f : 1f);
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
                or YgoConstants.LOCATION_GRAVE or YgoConstants.LOCATION_REMOVED || IsField(location);

        private static bool IsField(uint location) => location is YgoConstants.LOCATION_ONFIELD or YgoConstants.LOCATION_MZONE
            or YgoConstants.LOCATION_SZONE or YgoConstants.LOCATION_FZONE or YgoConstants.LOCATION_PZONE;

        internal static Graphic Add(RectTransform holder, uint location, bool opponent, bool glyphOnly = false)
        {
            var tint = location switch
            {
                YgoConstants.LOCATION_HAND => new Color(.42f, .96f, .87f),
                YgoConstants.LOCATION_GRAVE => new Color(.86f, .73f, 1f),
                _ => IsField(location) ? DuelStyle.Gold : DuelStyle.Side(opponent),
            };
            var sprite = glyphOnly ? null : DuelVisualAssets.PromptZoneIcon(location, opponent);
            if (sprite != null)
                return LotdUiKit.AddImage(holder.gameObject, sprite, Color.Lerp(Color.white, tint, .35f), true, false);

            var glyph = holder.gameObject.AddComponent<DuelPromptLocationIcon>();
            glyph._location = (location & YgoConstants.LOCATION_OVERLAY) != 0 ? YgoConstants.LOCATION_OVERLAY : location;
            glyph.color = tint;
            glyph.raycastTarget = false;
            return glyph;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            float scale = Mathf.Min(rect.width, rect.height) / 24f;
            if (scale <= 0f) return;

            Vector2 Point(float x, float y) => rect.center + new Vector2(x - 12f, y - 12f) * scale;

            void Shape(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tone) => DuelMat.AddQuad(mesh,
                Point(a.x, a.y), Point(b.x, b.y), Point(c.x, c.y), Point(d.x, d.y),
                tone, tone, Vector2.zero, Vector2.zero);

            void Fill(float left, float bottom, float right, float top, Color tone) => Shape(
                new Vector2(left, bottom), new Vector2(left, top), new Vector2(right, top), new Vector2(right, bottom), tone);

            void Line(float x1, float y1, float x2, float y2)
            {
                Vector2 a = Point(x1, y1);
                Vector2 b = Point(x2, y2);
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

            Color ink = new Color(.055f, .085f, .13f, color.a);
            Color shade = Color.Lerp(color, ink, .36f);
            switch (_location)
            {
                case YgoConstants.LOCATION_HAND:
                    Shape(new Vector2(4, 3), new Vector2(1, 17), new Vector2(8, 19), new Vector2(11, 5), shade);
                    Shape(new Vector2(13, 5), new Vector2(16, 19), new Vector2(23, 17), new Vector2(20, 3), shade);
                    Fill(7, 4, 17, 21, color);
                    Fill(9, 7, 15, 16, ink);
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
                    Fill(5, 5, 19, 17, color);
                    Shape(new Vector2(5, 17), new Vector2(9, 21), new Vector2(15, 21), new Vector2(19, 17), color);
                    Fill(2, 2.5f, 22, 5.5f, shade);
                    Fill(10.8f, 7, 13.2f, 16, ink);
                    Fill(8.2f, 11.3f, 15.8f, 13.7f, ink);
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
                    Fill(1, 4, 23, 20, shade);
                    for (int column = 0; column < 3; column++)
                    {
                        float left = 3 + column * 6;
                        Fill(left, 6, left + 5, 11, ink);
                        Fill(left, 13, left + 5, 18, column == 1 ? color : ink);
                    }
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
                var item = transform;
                if (scroll.content.GetComponent<GridLayoutGroup>() != null)
                {
                    while (item.parent != scroll.content) item = item.parent;
                }
                else if (scroll.horizontal && !scroll.vertical)
                {
                    var tile = GetComponentInParent<LayoutElement>();
                    if (tile != null && tile.transform.IsChildOf(scroll.content)) item = tile.transform;
                }
                var choice = RectTransformUtility.CalculateRelativeRectTransformBounds(view, item);
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
        internal Button GraveActivationControl { get; set; }
        internal Button ExtraActivationControl { get; set; }
        internal Button BanishedActivationControl { get; set; }
        internal Button InspectorArtworkControl { get; set; }
        internal Button InspectorDetailsControl { get; set; }
        internal bool ChainConfirmationAvailable { get; set; }
        internal Func<Vector2, Camera, bool> ActivationHitTest { get; set; }

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
            if (HitsControl(InspectorArtworkControl, screenPoint, eventCamera) ||
                HitsControl(InspectorDetailsControl, screenPoint, eventCamera) ||
                ChainConfirmationAvailable && (HitsControl(ChainConfirmationControl, screenPoint, eventCamera) ||
                    HitsControl(GraveActivationControl, screenPoint, eventCamera) ||
                    HitsControl(ExtraActivationControl, screenPoint, eventCamera) ||
                    HitsControl(BanishedActivationControl, screenPoint, eventCamera) ||
                    ActivationHitTest?.Invoke(screenPoint, eventCamera) == true))
                return false;
            if (SelectingFieldCards) return !FieldCardHitTest(screenPoint, eventCamera);
            return !ShowingField || !IsPointOverPile(screenPoint, eventCamera);
        }

        private static bool HitsControl(Button control, Vector2 point, Camera camera) =>
            control != null && control.isActiveAndEnabled && control.IsInteractable() &&
            RectTransformUtility.RectangleContainsScreenPoint((RectTransform)control.transform, point, camera);
    }
}
