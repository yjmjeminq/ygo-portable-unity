using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DuelFieldUI
    {
        private enum PadScope { None, Board, Place, FieldCards, Piles, Commands, Phase, Log, Zoom }

        private readonly struct PadTarget
        {
            internal readonly RectTransform Rect;
            internal readonly Action Submit;
            internal readonly uint PreviewCode;
            internal readonly RectTransform Art;
            internal readonly RectTransform HandVisual;

            internal PadTarget(RectTransform rect, Action submit, uint previewCode = 0, RectTransform art = null,
                RectTransform handVisual = null)
            {
                Rect = rect;
                Submit = submit;
                PreviewCode = previewCode;
                Art = art;
                HandVisual = handVisual;
            }
        }

        private readonly DeckPadInput _duelPadInput = new DeckPadInput();
        private readonly List<PadTarget> _duelPadTargets = new List<PadTarget>(40);
        private Button _duelPadInspectButton;
        private Button _duelPadPauseButton;
        private Button _duelPadLogButton;
        private PadFocusIndicator _duelPadFrame;
        private RectTransform _duelPadFrameTarget;
        private RectTransform _duelPadFocus;
        private RectTransform _duelPadBoardFocus;
        private RectTransform _duelPadPlacementFocus;
        private RectTransform _duelPadRaisedCard;
        private RectTransform _duelPointerRaisedCard;
        private uint _duelPointerCode;
        private RectTransform _duelCommandRaisedCard;
        private RectTransform _raisedCard;
        private Vector2 _lastDuelPointer;
        private bool _hasDuelPointerSample;
        private readonly List<(RectTransform Rect, bool InHand)> _duelPadCardMotions =
            new List<(RectTransform, bool)>(4);
        private PadScope _duelPadScope;
        private bool _usingDuelPad;
        private string _duelPadBoardHint = "";

        private void ResetDuelPad()
        {
            _duelPadInput.Reset();
            _duelPadTargets.Clear();
            _duelPadScope = PadScope.None;
            _duelPadFocus = _duelPadBoardFocus = _duelPadPlacementFocus = null;
            _duelPointerRaisedCard = _duelCommandRaisedCard = _raisedCard = null;
            _duelPointerCode = 0;
            _hasDuelPointerSample = false;
            _usingDuelPad = false;
            HideDuelPadFrame();
            ResetDuelPadCardMotions();
        }

        private void UpdateDuelPad()
        {
            Vector2 pointer = Input.mousePosition;
            bool pointerMoved = _hasDuelPointerSample && (pointer - _lastDuelPointer).sqrMagnitude > 9f;
            if (!_hasDuelPointerSample || pointerMoved) _lastDuelPointer = pointer;
            _hasDuelPointerSample = true;
            if (_usingDuelPad && (pointerMoved || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) ||
                DeckPadInput.DirectTouchBegan()))
            {
                _usingDuelPad = false;
                _duelPadPlacementFocus = null;
                HideDuelPadFrame();
                InspectCard(_duelPointerCode);
                _hintBannerText.text = _duelPadBoardHint;
            }
            var frame = _duelPadInput.Read(Time.unscaledTime);
            if (frame.HasInput) HandleDuelPad(frame);
            RefreshDuelPadCursor();
            AdvanceDuelPadCardMotions(Time.unscaledDeltaTime);
        }

        internal void HandleDuelPad(DeckPadFrame frame)
        {
            if (_session == null || !Root.activeInHierarchy || !frame.HasInput ||
                CardDragManager.Instance?.IsDragging == true) return;
            if (frame.NextArea && CanToggleChainConfirmation && !_modals.IsShowingField)
            {
                _chainConfirmationButton.onClick.Invoke();
                return;
            }
            if (!_usingDuelPad)
            {
                _duelPadFocus = EventSystem.current?.currentSelectedGameObject?.transform as RectTransform;
                _usingDuelPad = true;
            }
            if (_modals.HandlePad(frame))
            {
                _duelPadScope = PadScope.None;
                _duelPadFocus = null;
                RefreshDuelPadCursor();
                return;
            }

            var scope = CurrentDuelPadScope();
            if (scope == PadScope.None) return;
            if (frame.Cancel || (frame.Save && scope == PadScope.Board))
            {
                switch (scope)
                {
                    case PadScope.Commands: CloseActionPopup(); break;
                    case PadScope.Phase: _phasePicker.SetActive(false); break;
                    case PadScope.Log: CloseDuelLog(); break;
                    case PadScope.Zoom: _cardZoomModal.SetActive(false); break;
                    case PadScope.FieldCards:
                    case PadScope.Piles: _modals.TryCancelOrPass(); break;
                    case PadScope.Place when _pendingPlaces.Count > 0:
                        _pendingPlaces.RemoveAt(_pendingPlaces.Count - 1);
                        Refresh();
                        break;
                    default: OnPauseClicked(); break;
                }
                RefreshDuelPadCursor();
                return;
            }

            if (scope == PadScope.Board && frame.Search && _phaseButton.IsInteractable())
            {
                OpenPhasePicker();
                RefreshDuelPadCursor();
                return;
            }

            if (frame.DescriptionScroll != 0f)
            {
                var scroll = scope == PadScope.Log ? _logScroll : scope == PadScope.Zoom ? _zoomScroll :
                    scope is PadScope.Board or PadScope.FieldCards ? _inspectScroll : null;
                if (scroll != null) ScrollDuelPadDescription(scroll, frame.DescriptionScroll);
            }
            if (frame.Direction == Vector2Int.zero && !frame.Submit && !frame.Alternate &&
                !frame.PreviousArea && !frame.NextArea) return;

            BuildDuelPadTargets(scope);
            if (_duelPadTargets.Count == 0) return;
            int index = FindDuelPadFocus(scope);
            if ((scope == PadScope.Board || scope == PadScope.Piles) && (frame.PreviousArea || frame.NextArea))
            {
                RectTransform shortcut = frame.PreviousArea
                    ? scope == PadScope.Piles ? _myExtraTile.root :
                        _myHandViews.FirstOrDefault(view => view.Rect.gameObject.activeInHierarchy)?.Rect
                    : _myGyTile.root;
                int shortcutIndex = FindDuelPadTarget(shortcut);
                if (shortcutIndex >= 0) index = shortcutIndex;
            }
            else if (frame.Direction != Vector2Int.zero && _duelPadFocus != null)
            {
                index = FindDuelPadNeighbor(index, frame.Direction);
            }
            var target = _duelPadTargets[index];
            SetDuelPadFocus(target, scope);
            if (frame.Submit) target.Submit?.Invoke();
            else if (frame.Alternate && scope != PadScope.FieldCards && target.PreviewCode != 0) ShowCardZoomModal(target.PreviewCode);
            RefreshDuelPadCursor();
        }

        private PadScope CurrentDuelPadScope()
        {
            if (_modals.ZoneViewRoot.activeSelf || _modals.ResultRoot.activeSelf) return PadScope.None;
            if (_modals.IsFieldCardSelection)
                return CanAcceptPlayerAction ? PadScope.FieldCards : PadScope.None;
            if (_modals.IsShowingField) return PadScope.Piles;
            if (_modals.IsPromptOpen) return PadScope.None;
            if (_logWindow.activeSelf) return PadScope.Log;
            if (_cardZoomModal.activeSelf) return PadScope.Zoom;
            if (_actionPopup.activeSelf) return PadScope.Commands;
            if (_phasePicker.activeSelf) return PadScope.Phase;
            var prompt = _session?.ActivePrompt;
            if (_session == null || _session.IsFinished) return PadScope.None;
            if (prompt != null && prompt.playerId == _session.HumanPlayerIndex)
            {
                if (prompt.messageType is YgoConstants.MSG_SELECT_PLACE or YgoConstants.MSG_SELECT_DISFIELD)
                    return IsPresenting ? PadScope.None : PadScope.Place;
                if (prompt.messageType is not (YgoConstants.MSG_SELECT_IDLECMD or YgoConstants.MSG_SELECT_BATTLECMD))
                    return PadScope.None;
            }
            return PadScope.Board;
        }

        private void BuildDuelPadTargets(PadScope scope)
        {
            _duelPadTargets.Clear();
            switch (scope)
            {
                case PadScope.None:
                    return;
                case PadScope.Commands:
                    AddDuelPadButtons(_actionList);
                    return;
                case PadScope.Phase:
                    AddDuelPadButton(_btnToBp);
                    AddDuelPadButton(_btnToMp2);
                    AddDuelPadButton(_btnToEp);
                    return;
                case PadScope.Log:
                    AddDuelPadButtons(_logWindow.transform);
                    return;
                case PadScope.Zoom:
                    AddDuelPadButtons(_cardZoomModal.transform);
                    return;
                case PadScope.Piles:
                    AddDuelPadButton(_modals.FieldViewControl as Button);
                    AddDuelPadPiles();
                    return;
            }


            if (scope == PadScope.Board)
            {
                var hand = _session.Players[_session.HumanPlayerIndex].hand;
                for (int i = 0; i < _myHandViews.Count && i < hand.Count; i++)
                {
                    var view = _myHandViews[i];
                    if (view.Rect.gameObject.activeInHierarchy && view.Trigger?.OnLeftClick != null)
                        _duelPadTargets.Add(new PadTarget(view.Rect, view.Trigger.OnLeftClick, hand[i].code,
                            view.FocusArt, (RectTransform)view.Card.transform));
                }
            }
            AddDuelPadSlots(_myMZone, scope);
            AddDuelPadSlots(_mySZone, scope);
            AddDuelPadSlots(_emz, scope);
            AddDuelPadSlots(_oppMZone, scope);
            AddDuelPadSlots(_oppSZone, scope);
            if (scope == PadScope.FieldCards) AddDuelPadButton(_modals.FieldCardConfirmButton);
            if (scope != PadScope.Board) return;

            AddDuelPadPiles();
            AddDuelPadButton(_phaseButton);
            AddDuelPadButton(_duelPadInspectButton);
            AddDuelPadButton(_chainConfirmationButton);
            AddDuelPadButton(_duelPadLogButton);
            AddDuelPadButton(_duelPadPauseButton);
        }

        private void AddDuelPadPiles()
        {
            AddDuelPadButton(_myGyTile.button);
            AddDuelPadButton(_myExtraTile.button);
            AddDuelPadButton(_myBanTile.button);
            AddDuelPadButton(_oppGyTile.button);
            AddDuelPadButton(_oppExtraTile.button);
            AddDuelPadButton(_oppBanTile.button);
        }

        private void AddDuelPadSlots(FieldSlotView[] slots, PadScope scope)
        {
            foreach (var slot in slots)
            {
                if (scope == PadScope.Place)
                {
                    if (IsPlaceSelectable(slot.mappedCon, slot.mappedLoc, slot.mappedSeq))
                        _duelPadTargets.Add(new PadTarget(slot.root, slot.dragTrigger.OnLeftClick));
                    continue;
                }
                var card = slot.liveCard;
                if (card == null || !slot.cardView.gameObject.activeInHierarchy) continue;
                if (scope == PadScope.FieldCards &&
                    !FindFieldCardChoice(slot.mappedCon, slot.mappedLoc, slot.mappedSeq, out _)) continue;
                bool publicCard = card.IsFaceUp || card.isPublic || slot.mappedCon == _session.HumanPlayerIndex;
                _duelPadTargets.Add(new PadTarget(slot.root, slot.dragTrigger.OnLeftClick,
                    publicCard ? card.code : 0, slot.focusArt));
            }
        }

        private void AddDuelPadButtons(Transform scope)
        {
            foreach (var button in scope.GetComponentsInChildren<Button>())
                if (button.transform != scope) AddDuelPadButton(button);
        }

        private void AddDuelPadButton(Button button)
        {
            if (button == null || !button.isActiveAndEnabled || !button.IsInteractable()) return;
            _duelPadTargets.Add(new PadTarget((RectTransform)button.transform, button.onClick.Invoke));
        }

        private int FindDuelPadFocus(PadScope scope)
        {
            int current = FindDuelPadTarget(_duelPadFocus);
            if (current >= 0) return current;
            if (scope == PadScope.Board)
            {
                int placement = FindDuelPadTarget(_duelPadPlacementFocus);
                if (placement >= 0)
                {
                    _duelPadPlacementFocus = null;
                    return placement;
                }
                int saved = FindDuelPadTarget(_duelPadBoardFocus);
                if (saved >= 0) return saved;
            }
            var selected = EventSystem.current?.currentSelectedGameObject?.transform as RectTransform;
            int uiFocus = FindDuelPadTarget(selected);
            return Mathf.Max(0, uiFocus);
        }

        private int FindDuelPadTarget(RectTransform rect)
        {
            if (rect == null) return -1;
            for (int i = 0; i < _duelPadTargets.Count; i++)
                if (_duelPadTargets[i].Rect == rect) return i;
            return -1;
        }

        private int FindDuelPadNeighbor(int current, Vector2 direction)
        {
            Vector2 Center(RectTransform rect) => Root.transform.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            Vector2 origin = Center(_duelPadTargets[current].Rect);
            var perpendicular = new Vector2(-direction.y, direction.x);
            float bestScore = float.PositiveInfinity;
            int best = current;
            for (int i = 0; i < _duelPadTargets.Count; i++)
            {
                Vector2 delta = Center(_duelPadTargets[i].Rect) - origin;
                float forward = Vector2.Dot(delta, direction);
                if (forward <= .5f) continue;
                float score = forward + 3.5f * Mathf.Abs(Vector2.Dot(delta, perpendicular));
                if (score >= bestScore) continue;
                bestScore = score;
                best = i;
            }
            return best;
        }

        private void SetDuelPadFocus(PadTarget target, PadScope scope)
        {
            bool sameScope = _duelPadScope == scope;
            _duelPadScope = scope;
            _duelPadFocus = target.Rect;
            if (scope == PadScope.Board) _duelPadBoardFocus = target.Rect;
            if (target.PreviewCode != 0) InspectCard(target.PreviewCode);
            var events = EventSystem.current;
            if (events != null && !events.alreadySelecting && events.currentSelectedGameObject != target.Rect.gameObject)
                events.SetSelectedGameObject(target.Rect.gameObject);
            RevealDuelPadTarget(target.Rect);
            var raisedCard = target.HandVisual != null ? target.HandVisual : target.Art;
            _duelPadRaisedCard = raisedCard;

            var host = target.Rect.IsChildOf(_modals.ModalRoot.transform)
                ? (RectTransform)_modals.ModalRoot.transform : (RectTransform)Root.transform;
            ShowDuelPadFrame(target.Art != null ? target.Art : target.Rect, host, sameScope);
        }

        private void RefreshDuelPadCursor(bool layoutChanged = false)
        {
            if (!_usingDuelPad) return;
            var scope = CurrentDuelPadScope();
            if (scope == PadScope.None)
            {
                _duelPadScope = scope;
                _duelPadFocus = null;
                _duelPadRaisedCard = null;
                if (_modals.TryGetPadFocus(out var target, out var host))
                    ShowDuelPadFrame(target, host);
                else
                    HideDuelPadFrame();
                return;
            }
            _hintBannerText.text = _duelPadBoardHint.Length > 0 ? _duelPadBoardHint :
                scope == PadScope.Board ? "A 선택  X 상세  Y 페이즈  RB 체인  B 정지" : "";
            if (scope == PadScope.Board && _duelPadPlacementFocus != null)
            {
                BuildDuelPadTargets(scope);
                int placement = FindDuelPadTarget(_duelPadPlacementFocus);
                if (placement >= 0)
                {
                    _duelPadPlacementFocus = null;
                    SetDuelPadFocus(_duelPadTargets[placement], scope);
                    return;
                }
                if (IsPresenting)
                {
                    HideDuelPadFrame();
                    return;
                }
                _duelPadPlacementFocus = null;
            }
            if (!layoutChanged && _duelPadScope == scope && _duelPadFocus != null &&
                _duelPadFocus.gameObject.activeInHierarchy) return;
            BuildDuelPadTargets(scope);
            if (_duelPadTargets.Count == 0)
            {
                HideDuelPadFrame();
                return;
            }
            SetDuelPadFocus(_duelPadTargets[FindDuelPadFocus(scope)], scope);
        }

        private void HideDuelPadFrame()
        {
            _duelPadRaisedCard = null;
            _duelPadFrameTarget = null;
            _duelPadFrame?.Hide();
        }

        private void ShowDuelPadFrame(RectTransform target, RectTransform host, bool animate = true)
        {
            if (_duelPadFrame != null && _duelPadFrame.gameObject.activeSelf && _duelPadFrameTarget == target) return;
            var scroll = target.GetComponentInParent<ScrollRect>();
            if (scroll != null && scroll.viewport != null && scroll.content != null && target.IsChildOf(scroll.content))
                host = scroll.viewport;
            if (_duelPadFrame == null)
                _duelPadFrame = PadFocusIndicator.Create((RectTransform)Root.transform, "DuelPadFocus");
            _duelPadFrameTarget = target;
            _duelPadFrame.Show(target, host, animate);
        }

        private void AdvanceDuelPadCardMotions(float dt)
        {
            var raised = _duelPadRaisedCard;
            if (_actionPopup.activeSelf && _duelCommandRaisedCard != null) raised = _duelCommandRaisedCard;
            else if (!_usingDuelPad && !_actionPopup.activeSelf && !_phasePicker.activeSelf && !IsReadingLog &&
                !_cardZoomModal.activeSelf && !_modals.IsPromptOpen && !_modals.IsZoneViewerOpen &&
                !_modals.ResultRoot.activeSelf && CardDragManager.Instance?.IsDragging != true)
                raised = _duelPointerRaisedCard;

            if (raised != _raisedCard)
            {
                _raisedCard = raised;
                if (raised != null && !_duelPadCardMotions.Exists(motion => motion.Rect == raised))
                    _duelPadCardMotions.Add((raised, raised.parent != null && raised.parent.parent == _myHandBar));
                UpdateHandDepth(_myHandViews, _raisedCard);
            }

            float step = 1f - Mathf.Exp(-24f * dt);
            for (int i = _duelPadCardMotions.Count - 1; i >= 0; i--)
            {
                var motion = _duelPadCardMotions[i];
                var rect = motion.Rect;
                if (rect == null)
                {
                    _duelPadCardMotions.RemoveAt(i);
                    continue;
                }

                bool focused = rect == _raisedCard && rect.gameObject.activeInHierarchy;
                float focusScale = motion.InHand ? 1.075f : 1.05f;
                Vector3 scale = Vector3.one * (focused ? focusScale : 1f);
                Vector3 nextScale = Vector3.Lerp(rect.localScale, scale, step);
                bool settled = (scale - nextScale).sqrMagnitude < .000001f;
                nextScale = settled ? scale : nextScale;
                if (rect.localScale != nextScale) rect.localScale = nextScale;
                if (motion.InHand)
                {
                    var position = focused ? Vector2.up * Mathf.Min(18f, rect.rect.height * .12f) : Vector2.zero;
                    var nextPosition = Vector2.Lerp(rect.anchoredPosition, position, step);
                    bool positionSettled = (position - nextPosition).sqrMagnitude < .0025f;
                    nextPosition = positionSettled ? position : nextPosition;
                    if (rect.anchoredPosition != nextPosition) rect.anchoredPosition = nextPosition;
                    settled &= positionSettled;
                }
                if (!focused && settled) _duelPadCardMotions.RemoveAt(i);
            }
        }

        private void ClearPointerCard(RectTransform card)
        {
            if (_duelPointerRaisedCard != card) return;
            _duelPointerRaisedCard = null;
            _duelPointerCode = 0;
        }

        private void TrackPointerCard(RectTransform card, uint previewCode)
        {
            _duelPointerRaisedCard = card;
            _duelPointerCode = previewCode;
            if (!_usingDuelPad) InspectCard(previewCode);
        }

        private void RefreshPointerCard(RectTransform card, uint previewCode)
        {
            if (_duelPointerRaisedCard != card || _duelPointerCode == previewCode) return;
            _duelPointerCode = previewCode;
            if (!_usingDuelPad && !_actionPopup.activeSelf && !_phasePicker.activeSelf && !IsReadingLog &&
                !_cardZoomModal.activeSelf && !_modals.IsPromptOpen && !_modals.IsZoneViewerOpen &&
                !_modals.ResultRoot.activeSelf && CardDragManager.Instance?.IsDragging != true)
                InspectCard(previewCode);
        }

        private void ResetDuelPadCardMotions()
        {
            foreach (var motion in _duelPadCardMotions)
            {
                if (motion.Rect == null) continue;
                motion.Rect.localScale = Vector3.one;
                if (motion.InHand) motion.Rect.anchoredPosition = Vector2.zero;
            }
            _duelPadCardMotions.Clear();
        }

        private static void RevealDuelPadTarget(RectTransform target)
        {
            var scroll = target.GetComponentInParent<ScrollRect>();
            if (scroll == null || !scroll.vertical || scroll.viewport == null || scroll.content == null ||
                !target.IsChildOf(scroll.content)) return;
            var view = scroll.viewport;
            float overflow = scroll.content.rect.height - view.rect.height;
            if (overflow <= 0f) return;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(view, target);
            float delta = bounds.min.y < view.rect.yMin ? bounds.min.y - view.rect.yMin :
                Mathf.Max(0f, bounds.max.y - view.rect.yMax);
            if (delta == 0f) return;
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + delta / overflow);
        }

        private static void ScrollDuelPadDescription(ScrollRect scroll, float direction)
        {
            float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
            if (overflow <= 0f) return;
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition +
                direction * 650f * Time.unscaledDeltaTime / overflow);
        }
    }
}
