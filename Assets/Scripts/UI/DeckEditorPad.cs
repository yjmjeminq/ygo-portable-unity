using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DeckEditorUI
    {
        private enum PadArea { Trunk, Main, Extra, Side, Controls, Count }

        private const string MouseHelp = "드래그  이동    우클릭  추가 / 제거    Ctrl+F  검색    Ctrl+R  필터 초기화    Ctrl+S  저장";
        private const string PadFilterResetHelp = "−/View 필터 초기화";
        private readonly DeckPadInput _padInput = new DeckPadInput();
        private readonly int[] _padIndices = new int[(int)PadArea.Count];
        private DeckPadUi _padUi;
        private PadFocusIndicator _padCardFocus;
        private Text _deckHelpLabel;
        private bool _padMode;
        private PadArea _padArea;
        private PadArea _padFocusArea;
        private float _padFocusTrunkOffset;
        private float? _trunkPadScrollTarget;
        private int _padReorderOrigin = -1;
        private Vector3 _padMousePoint;
        private StandaloneInputModule _deckInputModule;
        private (string horizontal, string vertical, string submit, string cancel) _previousAxes;

        private void BuildPadNavigation(Transform root)
        {
            _padUi = new DeckPadUi(root);
            _padCardFocus = PadFocusIndicator.Create(root, "PadCardFocus");
        }

        private void StartPadNavigation()
        {
            Array.Clear(_padIndices, 0, _padIndices.Length);
            _padArea = PadArea.Trunk;
            ResetPadMode();

            var events = EventSystem.current;
            if (events == null) events = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            var module = events != null ? events.GetComponent<StandaloneInputModule>() : null;
            if (module == null || _deckInputModule != null) return;
            _deckInputModule = module;
            _previousAxes = (module.horizontalAxis, module.verticalAxis, module.submitButton, module.cancelButton);
            module.horizontalAxis = "DeckUiHorizontal";
            module.verticalAxis = "DeckUiVertical";
            module.submitButton = "DeckUiSubmit";
            module.cancelButton = "DeckUiCancel";
        }

        private void StopPadNavigation()
        {
            ResetPadMode();
            if (_deckInputModule != null)
            {
                _deckInputModule.horizontalAxis = _previousAxes.horizontal;
                _deckInputModule.verticalAxis = _previousAxes.vertical;
                _deckInputModule.submitButton = _previousAxes.submit;
                _deckInputModule.cancelButton = _previousAxes.cancel;
            }
            _deckInputModule = null;
        }

        private void ResetPadMode()
        {
            _padMode = false;
            _padReorderOrigin = -1;
            _padInput.Reset();
            _padUi.Clear(preserveSelection: true);
            _padCardFocus.Hide();
            ClearCardVisual(ref _padVisualSlot);
            _trunkPadScrollTarget = null;
            _deckHelpLabel.text = _mobileLayout
                ? "카드 탭  조작    길게 눌러 상세 보기    드래그  이동"
                : MouseHelp;
            _padMousePoint = Input.mousePosition;
        }

        private bool PollDeckPad()
        {
            if (_padMode && !_padUi.HasPopup &&
                (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.mouseScrollDelta.y != 0 ||
                 (Input.mousePosition - _padMousePoint).sqrMagnitude > 36f || DeckPadInput.DirectTouchBegan()))
            {
                ResetPadMode();
                RestoreMouseCardVisual();
                return false;
            }

            var frame = _padInput.Read(Time.unscaledTime, includeRightTrigger: true);
            if (CardDragManager.Instance?.IsDragging == true) return false;
            if (!_padMode && !frame.HasInput) return false;
            HandleDeckPad(frame);
            return frame.HasInput;
        }

        private void HandleDeckPad(DeckPadFrame frame)
        {
            if (_workingDeck == null) return;
            if (_cardCrafting?.IsOpen == true)
            {
                HandleCardCraftingPad(frame);
                return;
            }
            bool cardActionsOpen = _deckCardActions.activeSelf;
            bool mainSortOpen = _mainSortMenu.activeSelf;
            bool expandedOpen = _expandedDeckOverlay.gameObject.activeSelf;
            var overlay = expandedOpen ? _expandedDeckOverlay.gameObject : cardActionsOpen ? _deckCardActionPanel.gameObject :
                _deckDialog.activeSelf ? _deckDialog : _utilityOverlay != null ? _utilityOverlay.gameObject :
                _cardFilterDialog.activeSelf ? _cardFilterDialog :
                mainSortOpen ? _mainSortMenu :
                _archModalRoot.activeSelf ? _archModalRoot : null;
            var scope = overlay != null ? overlay.transform : Root.transform;
            bool resetFilters = frame.ResetFilters && !expandedOpen && _utilityOverlay == null && !cardActionsOpen && !mainSortOpen &&
                !_deckDialog.activeSelf && !_archModalRoot.activeSelf;
            if (!resetFilters && !_padUi.HasPopup &&
                _padUi.HandleOpenedDropdown(scope, frame.Direction, frame.Submit, frame.Cancel, findAny: frame.HasInput))
            {
                _padMode = true;
                _padMousePoint = Input.mousePosition;
                if (overlay == null)
                {
                    _padArea = PadArea.Controls;
                    _padReorderOrigin = -1;
                    SwitchPadArea(_padArea);
                }
                _padCardFocus.Hide();
                return;
            }
            if (!_padMode)
            {
                _padMode = true;
                _padMousePoint = Input.mousePosition;
                if (_padUi.AdoptPointerFocus(scope))
                {
                    if (overlay == null) _padArea = PadArea.Controls;
                }
                else EventSystem.current?.SetSelectedGameObject(null);
                SwitchPadArea(_padArea);
            }

            if (cardActionsOpen)
            {
                HandleDeckCardActionsPad(frame);
                return;
            }

            if (resetFilters)
            {
                if (_padUi.HasPopup)
                    _padUi.Clear();
                else
                    _padUi.HandleOpenedDropdown(scope, Vector2Int.zero, false, true);
                ResetTrunkFilters();
                if (_cardFilterDialog.activeSelf)
                    HandleDetailedSearchPad(default);
                else if (_padArea != PadArea.Controls)
                {
                    if (_padArea == PadArea.Trunk) SwitchPadArea(PadArea.Trunk);
                    DrawPadCardFocus();
                }
                return;
            }

            if (_padUi.HasPopup)
            {
                _padCardFocus.Hide();
                _padUi.Handle(Root.transform, frame.Direction, frame.Submit, frame.Cancel, null);
                return;
            }
            if (_deckDialog.activeSelf)
            {
                _padCardFocus.Hide();
                if (frame.HasInput) _padUi.AdoptPointerFocus(_deckDialog.transform);
                _padUi.Handle(_deckDialog.transform, frame.Direction, frame.Submit, frame.Cancel,
                    () => CloseDeckDialog(), _deckDialogCancel);
                return;
            }
            if (expandedOpen || _utilityOverlay != null)
            {
                _padCardFocus.Hide();
                if (frame.HasInput) _padUi.AdoptPointerFocus(scope);
                if (expandedOpen)
                {
                    if (frame.Alternate || frame.Search)
                        _expandedDeckScroll.verticalNormalizedPosition = Mathf.Clamp01(
                            _expandedDeckScroll.verticalNormalizedPosition + (frame.Alternate ? .18f : -.18f));
                    _padUi.Handle(scope, frame.Direction, frame.Submit, frame.Cancel, CloseExpandedDeckCard, _expandedDeckClose);
                }
                else
                    _padUi.Handle(scope, frame.Direction, frame.Submit, frame.Cancel, CloseDeckUtility, _utilityDefault);
                return;
            }
            if (mainSortOpen)
            {
                _padCardFocus.Hide();
                if (frame.HasInput) _padUi.AdoptPointerFocus(_mainSortMenu.transform);
                _padUi.Handle(_mainSortMenu.transform, frame.Direction, frame.Submit, frame.Cancel,
                    () => CloseMainDeckSort(), ActiveDeckSortOption);
                return;
            }
            if (_cardFilterDialog.activeSelf)
            {
                HandleDetailedSearchPad(frame);
                return;
            }
            if (_archModalRoot.activeSelf)
            {
                _padCardFocus.Hide();
                if (frame.Search) _padUi.OpenText(_archSearchInp);
                else if (frame.PreviousArea || frame.NextArea) PagePadArchetypes(frame.PreviousArea);
                else _padUi.Handle(_archModalRoot.transform, frame.Direction, frame.Submit, frame.Cancel,
                    () => CloseArchetypeModal(), _archSearchInp);
                return;
            }

            if (_padReorderOrigin >= 0 || (frame.Reorder && CurrentPadDeck != DragSourceType.None))
            {
                HandlePadReorder(frame);
                DrawPadCardFocus();
                return;
            }

            if (frame.Search)
            {
                SwitchPadArea(PadArea.Trunk);
                _padCardFocus.Hide();
                _padUi.OpenText(_searchInp);
                return;
            }
            if (frame.Save)
            {
                if (_padArea == PadArea.Trunk)
                {
                    OpenDetailedSearch();
                    HandleDetailedSearchPad(default);
                }
                else
                {
                    TrySaveCurrentDeck();
                }
                return;
            }

            if (frame.PreviousArea || frame.NextArea)
            {
                int count = (int)PadArea.Count;
                SwitchPadArea((PadArea)(((int)_padArea + (frame.NextArea ? 1 : count - 1)) % count));
            }

            if (frame.Alternate && !frame.Cancel)
            {
                if (_padArea == PadArea.Main)
                {
                    OpenMainDeckSort();
                    return;
                }
                CyclePadTrunkSort();
                if (_padArea != PadArea.Controls) DrawPadCardFocus();
                return;
            }

            if (_padArea == PadArea.Controls)
            {
                _padCardFocus.Hide();
                _padUi.Handle(Root.transform, frame.Direction, frame.Submit, frame.Cancel,
                    () => SwitchPadArea(PadArea.Trunk), _detailButtonText.GetComponentInParent<Button>());
                return;
            }
            if (_padArea == PadArea.Trunk &&
                (frame.Cancel && (_relatedFilterCode != 0 || _activeArchetypeFilter != null) ||
                 frame.RelatedCards && _relatedFilterCode != 0))
            {
                ExitTrunkScope();
                SwitchPadArea(PadArea.Trunk);
                DrawPadCardFocus();
                return;
            }
            if (frame.Cancel)
            {
                BeforeReplacingEdits(() => _app.ShowScreen(AppScreen.MainMenu));
                if (_deckDialog.activeSelf) _padCardFocus.Hide();
                return;
            }

            NavigatePadCards(frame.Direction);
            if (frame.RelatedCards) OpenRelatedCardsForPadSelection();
            else if (frame.Submit)
            {
                OpenCardActionsForPadSelection();
                if (_deckCardActions.activeSelf)
                {
                    HandleDeckCardActionsPad(default);
                    return;
                }
            }
            DrawPadCardFocus();
            ScrollPadDescription(frame.DescriptionScroll);
        }

        private uint GetPadSelectedCard(out int index)
        {
            if (_padArea == PadArea.Trunk)
            {
                index = ClampPadIndex(_padIndices[(int)_padArea], _trunkResults.Count);
                return _trunkResults.Count == 0 ? 0 : _trunkResults[index].code;
            }

            var cards = GetDeckList(CurrentPadDeck);
            int count = VisiblePadDeckCount;
            index = ClampPadIndex(_padIndices[(int)_padArea], count);
            return count == 0 ? 0 : cards[index];
        }

        private void OpenRelatedCardsForPadSelection()
        {
            uint code = GetPadSelectedCard(out _);
            if (code == 0) return;
            FilterRelatedCards(code);
            SwitchPadArea(PadArea.Trunk);
        }

        private void CyclePadTrunkSort()
        {
            int trunk = (int)PadArea.Trunk;
            int index = ClampPadIndex(_padIndices[trunk], _trunkResults.Count);
            uint code = _trunkResults.Count == 0 ? 0 : _trunkResults[index].code;
            _sortFilter.value = (_sortFilter.value + 1) % _sortFilter.options.Count;
            if (code != 0)
                _padIndices[trunk] = Mathf.Max(0, _trunkResults.FindIndex(card => card.code == code));
        }

        private void HandleCardCraftingPad(DeckPadFrame frame)
        {
            if (frame.HasInput)
            {
                _padMode = true;
                _padMousePoint = Input.mousePosition;
            }

            var scope = _cardCrafting.ActiveRoot.transform;
            if (_padUi.HasPopup)
                _padUi.Handle(scope, frame.Direction, frame.Submit, frame.Cancel, null);
            else if (frame.Cancel)
                _cardCrafting.Back();
            else if (frame.Search)
                _padUi.OpenText(_cardCrafting.Search);
            else if (frame.PreviousArea || frame.NextArea)
                _cardCrafting.ChangePage(frame.PreviousArea ? -1 : 1);
            else
            {
                _cardCrafting.ScrollDescription(frame.DescriptionScroll);
                if (frame.HasInput) _padUi.AdoptPointerFocus(scope);
                _padUi.Handle(scope, frame.Direction, frame.Submit, false, null);
            }
        }

        private void HandleDeckCardActionsPad(DeckPadFrame frame)
        {
            _padCardFocus.Hide();
            _deckHelpLabel.text = "카드 메뉴   방향키 이동   A 선택   B 취소";
            if (frame.HasInput) _padUi.AdoptPointerFocus(_deckCardActionPanel);
            _padUi.Handle(_deckCardActionPanel, frame.Direction, frame.Submit, frame.Cancel,
                CloseDeckCardActions, _deckCardActionDefault);
            if (_deckCardActions.activeSelf || _cardCrafting?.IsOpen == true) return;

            if (_padArea != PadArea.Controls) DrawPadCardFocus();
        }

        private void HandleDetailedSearchPad(DeckPadFrame frame)
        {
            _padCardFocus.Hide();
            _deckHelpLabel.text = $"조회 조건   방향키 이동   A 선택   X 정렬   Y 검색   {PadFilterResetHelp}   B 취소";
            if (frame.Alternate && !frame.Cancel)
            {
                CyclePadTrunkSort();
                return;
            }
            if (frame.Search && !frame.Cancel)
            {
                _padUi.OpenText(_detailedQueryInput);
                return;
            }
            if (!frame.Cancel && frame.Direction.x != 0 && MoveBetweenFilterColumns(frame.Direction.x)) return;
            _padUi.Handle(_cardFilterDialog.transform, frame.Direction, frame.Submit, frame.Cancel,
                CloseDetailedSearch, _detailedQueryInput);
            if (_cardFilterDialog.activeSelf) return;

            _padUi.Clear();
            SwitchPadArea(_padArea);
            if (_padArea == PadArea.Controls)
                _padUi.FocusInScope(Root.transform, _detailButtonText.GetComponentInParent<Button>());
            else
                DrawPadCardFocus();
        }

        private bool MoveBetweenFilterColumns(int direction)
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected == null) return false;

            var scope = _cardFilterDialog.transform;
            if (direction < 0 && selected.transform.IsChildOf(_optionScroll.transform.parent))
            {
                _padUi.FocusInScope(scope, _facetButtons[(int)_activeFacet]);
                return true;
            }

            if (direction < 0) return false;
            int index = Array.FindIndex(_facetButtons, button => button.gameObject == selected);
            if (index < 0) return false;
            var facet = (Facet)index;
            ShowDetailedFacet(facet);
            Selectable value = facet switch
            {
                Facet.Attack => _statInputs[0],
                Facet.Defense => _statInputs[2],
                Facet.EffectText => _effectTextInput,
                _ => _optionButtons[_draftFacets[index]],
            };
            _padUi.FocusInScope(scope, value);
            return true;
        }

        private void PagePadArchetypes(bool previous)
        {
            float viewport = _archViewport.rect.height;
            float overflow = _archContent.rect.height - viewport;
            if (overflow <= 0f || viewport <= 0f || _archResults.Count == 0) return;

            _archScroll.StopMovement();
            _archScroll.verticalNormalizedPosition = Mathf.Clamp01(_archScroll.verticalNormalizedPosition +
                (previous ? viewport : -viewport) / overflow);
            OnArchModalScrolled();
            int firstWholeRow = Mathf.Clamp(Mathf.CeilToInt((_archContent.anchoredPosition.y - .01f) / _archRowHeight),
                0, _archTotalRows - 1);
            var button = _archPool[ArchetypePoolSlot(firstWholeRow, 0)].loadBtn;
            _padUi.FocusInScope(_archModalRoot.transform, button);
        }

        private void SwitchPadArea(PadArea area)
        {
            if (_padArea != area)
            {
                _padReorderOrigin = -1;
                _trunkPadScrollTarget = null;
                ClearCardVisual(ref _padVisualSlot);
                _padUi.Clear();
            }
            _padArea = area;
            var actions = area switch
            {
                PadArea.Trunk => "보관함   A 카드 메뉴",
                PadArea.Controls => "메뉴   A 선택",
                PadArea.Side => "사이드   A 카드 메뉴",
                PadArea.Extra => "엑스트라   A 카드 메뉴",
                _ => "메인   A 카드 메뉴",
            };
            bool relatedList = area == PadArea.Trunk && _relatedFilterCode != 0;
            bool scopedTrunk = relatedList || area == PadArea.Trunk && _activeArchetypeFilter != null;
            string back = area == PadArea.Controls ? "B 카드 목록" :
                scopedTrunk ? "우스틱 설명   B 전체 카드" : "우스틱 설명   B 뒤로";
            var reorder = CurrentPadDeck != DragSourceType.None ? "   R스틱 클릭 순서" : "";
            string menu = area == PadArea.Trunk ? "Menu/+ 조회" : "Menu/+ 저장";
            string related = area == PadArea.Controls ? "" : relatedList ? "   R2/ZR/RT 전체 카드" : "   R2/ZR/RT 관련 카드";
            string sortHelp = area == PadArea.Main ? "X 메인 정렬" : "X 보관함 정렬";
            _deckHelpLabel.text = $"{actions}{reorder}   {sortHelp}   Y 검색{related}   LB/RB 영역   {menu}   {PadFilterResetHelp}   {back}";
        }

        private DragSourceType CurrentPadDeck => _padArea switch
        {
            PadArea.Main => DragSourceType.DeckMain,
            PadArea.Extra => DragSourceType.DeckExtra,
            PadArea.Side => DragSourceType.DeckSide,
            _ => DragSourceType.None,
        };

        private int VisiblePadDeckCount =>
            Mathf.Min(GetDeckList(CurrentPadDeck)?.Count ?? 0, GetDeckSlots(CurrentPadDeck).Count);

        private static int ClampPadIndex(int index, int count) => Mathf.Clamp(index, 0, Mathf.Max(0, count - 1));

        private void RememberMouseCard(PadArea area, int index)
        {
            if (_padMode) ResetPadMode();
            _padReorderOrigin = -1;
            _padArea = area;
            _padIndices[(int)area] = index;
            var events = EventSystem.current;
            if (events != null && !events.alreadySelecting) events.SetSelectedGameObject(null);
        }

        private void NavigatePadCards(Vector2Int move, bool stayInArea = false)
        {
            if (move == Vector2Int.zero) return;
            int count = _padArea == PadArea.Trunk ? _trunkResults.Count : VisiblePadDeckCount;
            int cols = _padArea switch
            {
                PadArea.Trunk => _trunkColumns,
                PadArea.Side => SideDeckColumns,
                PadArea.Extra => _extraColumns,
                _ => _mainColumns,
            };
            int area = (int)_padArea;
            int index = ClampPadIndex(_padIndices[area], count);
            int row = index / cols;
            int col = index % cols;
            int next = index + move.x - move.y * cols;

            if (!stayInArea && move.x > 0 && _padArea == PadArea.Trunk && (col == cols - 1 || next >= count))
                SwitchPadArea(PadArea.Main);
            else if (!stayInArea && move.x < 0 && col == 0 && _padArea != PadArea.Trunk)
                SwitchPadArea(PadArea.Trunk);
            else if (!stayInArea && move.y < 0 && (row + 1) * cols >= count && (_padArea == PadArea.Main || _padArea == PadArea.Extra))
                SwitchPadArea(_padArea == PadArea.Main ? PadArea.Extra : PadArea.Side);
            else if (!stayInArea && move.y > 0 && row == 0 && (_padArea == PadArea.Side || _padArea == PadArea.Extra))
                SwitchPadArea(_padArea == PadArea.Side ? PadArea.Extra : PadArea.Main);
            else if (move.x == 0 && next >= 0 && (row - move.y) * cols < count ||
                move.x != 0 && next >= 0 && (next / cols) == row && next < count)
                _padIndices[area] = ClampPadIndex(next, count);
        }

        private void HandlePadReorder(DeckPadFrame frame)
        {
            int area = (int)_padArea;
            if (_padReorderOrigin < 0)
            {
                int count = VisiblePadDeckCount;
                if (count < 2) return;
                _padReorderOrigin = ClampPadIndex(_padIndices[area], count);
                _deckHelpLabel.text = "순서 이동   방향키로 위치 선택   A 놓기   B 취소";
                return;
            }
            if (frame.Cancel)
            {
                _padIndices[area] = _padReorderOrigin;
                _padReorderOrigin = -1;
                SwitchPadArea(_padArea);
                return;
            }
            NavigatePadCards(frame.Direction, stayInArea: true);
            if (!frame.Submit && !frame.Reorder) return;
            MoveDeckCard(CurrentPadDeck, _padReorderOrigin, CurrentPadDeck, _padIndices[area]);
            _padReorderOrigin = -1;
            SwitchPadArea(_padArea);
        }

        private void OpenCardActionsForPadSelection()
        {
            uint code = GetPadSelectedCard(out int index);
            if (code == 0) return;

            if (_padArea == PadArea.Trunk)
            {
                EnsureTrunkPadVisible(index, snap: true);
                int slot = index - _lastFirstRow * _trunkColumns;
                if (slot >= 0 && slot < _trunkPool.Count)
                    ShowDeckCardActions(_trunkPool[slot].rt, DragSourceType.Trunk, index, code);
                return;
            }

            RevealDeckSlot(CurrentPadDeck, index);
            ShowDeckCardActions(GetDeckSlots(CurrentPadDeck)[index].rt, CurrentPadDeck, index, code);
        }

        private void DrawPadCardFocus()
        {
            int area = (int)_padArea;
            if (_padArea == PadArea.Trunk)
            {
                if (_trunkResults.Count == 0)
                {
                    _padCardFocus.Hide();
                    ClearCardVisual(ref _padVisualSlot);
                    return;
                }
                int index = _padIndices[area] = ClampPadIndex(_padIndices[area], _trunkResults.Count);
                EnsureTrunkPadVisible(index);
                UpdateTrunkPadScroll(Time.unscaledDeltaTime);
                int slot = index - _lastFirstRow * _trunkColumns;
                if (slot >= 0 && slot < _trunkPool.Count)
                    ShowPadCardFocus(_trunkPool[slot].rt, _trunkResults[index].code);
                else
                {
                    _padCardFocus.Hide();
                    ClearCardVisual(ref _padVisualSlot);
                    InspectCard(_trunkResults[index].code);
                }
                return;
            }

            var cards = GetDeckList(CurrentPadDeck);
            int count = VisiblePadDeckCount;
            int deckIndex = _padIndices[area] = ClampPadIndex(_padIndices[area], count);
            RevealDeckSlot(CurrentPadDeck, deckIndex);
            ShowPadCardFocus(GetDeckSlots(CurrentPadDeck)[deckIndex].rt, count > 0 ? cards[deckIndex] : 0);
        }

        private void EnsureTrunkPadVisible(int index, bool snap = false)
        {
            float height = _trunkViewport.rect.height;
            if (height <= 0f) return;
            float top = (index / _trunkColumns) * _rowHeight;
            float offset = _trunkContent.anchoredPosition.y;
            float destination = offset;
            if (top < offset) destination = top;
            else if (top + _rowHeight > offset + height) destination = top + _rowHeight - height;
            _trunkPadScrollTarget = Mathf.Abs(destination - offset) > .05f ? destination : null;
            if (snap && _trunkPadScrollTarget.HasValue)
            {
                SetTrunkPadOffset(destination);
                _trunkPadScrollTarget = null;
            }
        }

        private void UpdateTrunkPadScroll(float dt)
        {
            if (!_trunkPadScrollTarget.HasValue) return;
            float offset = _trunkContent.anchoredPosition.y;
            float target = _trunkPadScrollTarget.Value;
            float next = Mathf.Lerp(offset, target, 1f - Mathf.Exp(-26f * dt));
            if (Mathf.Abs(next - target) < .15f)
            {
                next = target;
                _trunkPadScrollTarget = null;
            }
            SetTrunkPadOffset(next);
        }

        private void SetTrunkPadOffset(float destination)
        {
            float height = _trunkViewport.rect.height;
            float max = Mathf.Max(0f, _trunkContent.rect.height - height);
            _trunkScroll.StopMovement();
            _trunkScroll.verticalNormalizedPosition = max > 0f ? 1f - Mathf.Clamp01(destination / max) : 1f;
            _trunkScrollbar.SetValueWithoutNotify(_trunkScroll.verticalNormalizedPosition);
            OnTrunkScrolled();
        }

        private void ShowPadCardFocus(RectTransform target, uint code)
        {
            bool inTrunk = _padArea == PadArea.Trunk;
            bool animate = _padArea == _padFocusArea;
            if (inTrunk)
            {
                float scrollOffset = _trunkContent.anchoredPosition.y;
                animate &= Mathf.Abs(scrollOffset - _padFocusTrunkOffset) < 0.5f;
                _padFocusTrunkOffset = scrollOffset;
            }
            _padFocusArea = _padArea;
            var host = _padArea switch
            {
                PadArea.Trunk => _trunkViewport,
                PadArea.Main => _mainDeckScroll.viewport,
                PadArea.Extra when _extraDeckScroll != null => _extraDeckScroll.viewport,
                PadArea.Side => _sideDeckScroll.viewport,
                _ => _deckPanel,
            };
            _padCardFocus.Show(target, host, animate);
            SetPadCardVisual(target, code);
            if (code != 0) InspectCard(code);
        }

        private void ScrollPadDescription(float direction)
        {
            if (direction == 0f) return;
            float height = _previewScroll.content.rect.height - _previewScroll.viewport.rect.height;
            if (height <= 0f) return;
            _previewScroll.StopMovement();
            _previewScroll.verticalNormalizedPosition = Mathf.Clamp01(
                _previewScroll.verticalNormalizedPosition + direction * 240f * Time.unscaledDeltaTime / height);
        }
    }
}
