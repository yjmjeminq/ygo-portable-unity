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

        private const string MouseHelp = "드래그  이동    우클릭  추가 / 제거    Ctrl+F  검색    Ctrl+S  저장";
        private readonly DeckPadInput _padInput = new DeckPadInput();
        private readonly int[] _padIndices = new int[(int)PadArea.Count];
        private DeckPadUi _padUi;
        private RectTransform _padCardFocus;
        private Text _deckHelpLabel;
        private bool _padMode;
        private PadArea _padArea;
        private int _padReorderOrigin = -1;
        private Vector3 _padMousePoint;
        private StandaloneInputModule _deckInputModule;
        private (string horizontal, string vertical, string submit, string cancel) _previousAxes;

        private void BuildPadNavigation(Transform root)
        {
            _padUi = new DeckPadUi(root);
            _padCardFocus = LotdUiKit.FillRect("PadCardFocus", root);
            Color gold = new Color(1f, .84f, .18f, 1f);
            void Line(string name, Vector2 a, Vector2 b, Vector2 min, Vector2 max)
            {
                var rect = LotdUiKit.CreateRect(name, _padCardFocus, a, b, min, max);
                LotdUiKit.AddImage(rect.gameObject, LotdUiKit.WhiteSprite, gold, false, false);
            }

            const float thickness = 3f;
            Line("Top", Vector2.up, Vector2.one, Vector2.down * thickness, Vector2.zero);
            Line("Bottom", Vector2.zero, Vector2.right, Vector2.zero, Vector2.up * thickness);
            Line("Left", Vector2.zero, Vector2.up, Vector2.zero, Vector2.right * thickness);
            Line("Right", Vector2.right, Vector2.one, Vector2.left * thickness, Vector2.zero);
            _padCardFocus.gameObject.SetActive(false);
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
            _padCardFocus.gameObject.SetActive(false);
            _deckHelpLabel.text = MouseHelp;
            _padMousePoint = Input.mousePosition;
        }

        private bool PollDeckPad()
        {
            if (_padMode && !_padUi.HasPopup &&
                (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.mouseScrollDelta.y != 0 ||
                 (Input.mousePosition - _padMousePoint).sqrMagnitude > 36f))
            {
                ResetPadMode();
                return false;
            }

            var frame = _padInput.Read(Time.unscaledTime);
            if (CardDragManager.Instance?.IsDragging == true) return false;
            if (!_padMode && !frame.HasInput) return false;
            HandleDeckPad(frame);
            return frame.HasInput;
        }

        private void HandleDeckPad(DeckPadFrame frame)
        {
            if (_workingDeck == null) return;
            var overlay = _deckDialog.activeSelf ? _deckDialog : _cardFilterDialog.activeSelf ? _cardFilterDialog :
                _archModalRoot.activeSelf ? _archModalRoot : null;
            var scope = overlay != null ? overlay.transform : Root.transform;
            if (!_padUi.HasPopup && _padUi.HandleOpenedDropdown(scope, frame.Direction, frame.Submit, frame.Cancel, findAny: frame.HasInput))
            {
                _padMode = true;
                _padMousePoint = Input.mousePosition;
                if (overlay == null)
                {
                    _padArea = PadArea.Controls;
                    _padReorderOrigin = -1;
                    SwitchPadArea(_padArea);
                }
                _padCardFocus.gameObject.SetActive(false);
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

            if (_padUi.HasPopup)
            {
                _padCardFocus.gameObject.SetActive(false);
                _padUi.Handle(Root.transform, frame.Direction, frame.Submit, frame.Cancel, null);
                return;
            }
            if (_deckDialog.activeSelf)
            {
                _padCardFocus.gameObject.SetActive(false);
                _padUi.Handle(_deckDialog.transform, frame.Direction, frame.Submit, frame.Cancel,
                    () => _deckDialog.SetActive(false), _deckDialog.transform.Find("Window/Cancel/Button")?.GetComponent<Button>());
                return;
            }
            if (_cardFilterDialog.activeSelf)
            {
                _padCardFocus.gameObject.SetActive(false);
                _padUi.Handle(_cardFilterDialog.transform, frame.Direction, frame.Submit, frame.Cancel,
                    () => _cardFilterDialog.SetActive(false));
                return;
            }
            if (_archModalRoot.activeSelf)
            {
                _padCardFocus.gameObject.SetActive(false);
                if (frame.Search) _padUi.OpenText(_archSearchInp);
                else if (frame.PreviousArea || frame.NextArea) PagePadArchetypes(frame.PreviousArea);
                else _padUi.Handle(_archModalRoot.transform, frame.Direction, frame.Submit, frame.Cancel,
                    () => _archModalRoot.SetActive(false), _archSearchInp);
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
                _padCardFocus.gameObject.SetActive(false);
                _padUi.OpenText(_searchInp);
                return;
            }
            if (frame.Save)
            {
                TrySaveCurrentDeck();
                return;
            }

            if (frame.PreviousArea || frame.NextArea)
            {
                int count = (int)PadArea.Count;
                SwitchPadArea((PadArea)(((int)_padArea + (frame.NextArea ? 1 : count - 1)) % count));
            }

            if (_padArea == PadArea.Controls)
            {
                _padCardFocus.gameObject.SetActive(false);
                _padUi.Handle(Root.transform, frame.Direction, frame.Submit, frame.Cancel,
                    () => SwitchPadArea(PadArea.Trunk), Root.transform.Find("Trunk/Filters/DetailedSearch/Button")?.GetComponent<Button>());
                return;
            }
            if (frame.Cancel)
            {
                BeforeReplacingEdits(() => _app.ShowScreen(AppScreen.MainMenu));
                if (_deckDialog.activeSelf) _padCardFocus.gameObject.SetActive(false);
                return;
            }

            NavigatePadCards(frame.Direction);
            if (frame.Submit || frame.Alternate) ExecutePadCard(frame.Alternate);
            DrawPadCardFocus();
            ScrollPadDescription(frame.DescriptionScroll);
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
                _padUi.Clear();
            }
            _padArea = area;
            var actions = area switch
            {
                PadArea.Trunk => "보관함   A 담기   X 사이드",
                PadArea.Controls => "메뉴   A 선택",
                PadArea.Side => "사이드   A 덱으로 이동   X 제거",
                PadArea.Extra => "엑스트라   A 사이드 이동   X 제거",
                _ => "메인   A 사이드 이동   X 제거",
            };
            string back = area == PadArea.Controls ? "B 카드 목록" : "우스틱 설명   B 뒤로";
            var reorder = CurrentPadDeck != DragSourceType.None ? "   R스틱 클릭 순서" : "";
            _deckHelpLabel.text = $"{actions}{reorder}   Y 검색   LB/RB 영역   Menu 저장   {back}";
        }

        private DragSourceType CurrentPadDeck => _padArea switch
        {
            PadArea.Main => DragSourceType.DeckMain,
            PadArea.Extra => DragSourceType.DeckExtra,
            PadArea.Side => DragSourceType.DeckSide,
            _ => DragSourceType.None,
        };

        private static int ClampPadIndex(int index, int count) => Mathf.Clamp(index, 0, Mathf.Max(0, count - 1));

        private void RememberMouseCard(PadArea area, int index)
        {
            _padReorderOrigin = -1;
            _padArea = area;
            _padIndices[(int)area] = index;
            var events = EventSystem.current;
            if (events != null && !events.alreadySelecting) events.SetSelectedGameObject(null);
        }

        private void NavigatePadCards(Vector2Int move, bool stayInArea = false)
        {
            if (move == Vector2Int.zero) return;
            int count = _padArea == PadArea.Trunk ? _trunkResults.Count : GetDeckList(CurrentPadDeck).Count;
            int cols = _padArea == PadArea.Trunk ? TrunkCols : _padArea == PadArea.Main ? 13 : 15;
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
            var cards = GetDeckList(CurrentPadDeck);
            int area = (int)_padArea;
            if (_padReorderOrigin < 0)
            {
                if (cards.Count < 2) return;
                _padReorderOrigin = ClampPadIndex(_padIndices[area], cards.Count);
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
            MoveDeckCard(cards, _padReorderOrigin, cards, _padIndices[area]);
            _padReorderOrigin = -1;
            SwitchPadArea(_padArea);
        }

        private void ExecutePadCard(bool alternate)
        {
            int area = (int)_padArea;
            if (_padArea == PadArea.Trunk)
            {
                if (_trunkResults.Count == 0) return;
                int index = ClampPadIndex(_padIndices[area], _trunkResults.Count);
                TryAddCardToDeck(_trunkResults[index].code, alternate ? DragSourceType.DeckSide : DragSourceType.None);
                return;
            }

            var source = GetDeckList(CurrentPadDeck);
            if (source.Count == 0) return;
            int slot = ClampPadIndex(_padIndices[area], source.Count);
            uint code = source[slot];
            if (alternate)
            {
                source.RemoveAt(slot);
                FinishDeckEdit();
                return;
            }

            var card = CardDatabase.Get(code);
            var destination = CurrentPadDeck == DragSourceType.DeckSide
                ? card?.IsExtra == true ? DragSourceType.DeckExtra : DragSourceType.DeckMain
                : DragSourceType.DeckSide;
            var target = GetDeckList(destination);
            if (!CanPlaceCard(card, destination, target)) return;
            MoveDeckCard(source, slot, target, target.Count);
        }

        private void DrawPadCardFocus()
        {
            int area = (int)_padArea;
            if (_padArea == PadArea.Trunk)
            {
                if (_trunkResults.Count == 0)
                {
                    _padCardFocus.gameObject.SetActive(false);
                    return;
                }
                int index = _padIndices[area] = ClampPadIndex(_padIndices[area], _trunkResults.Count);
                EnsureTrunkPadVisible(index);
                int slot = index - _lastFirstRow * TrunkCols;
                if (slot >= 0 && slot < _trunkPool.Count)
                    ShowPadCardFocus(_trunkPool[slot].rt, _trunkResults[index].code);
                return;
            }

            var cards = GetDeckList(CurrentPadDeck);
            int deckIndex = _padIndices[area] = ClampPadIndex(_padIndices[area], cards.Count);
            ShowPadCardFocus(GetDeckSlots(CurrentPadDeck)[deckIndex].rt, cards.Count > 0 ? cards[deckIndex] : 0);
        }

        private void EnsureTrunkPadVisible(int index)
        {
            float height = _trunkViewport.rect.height;
            if (height <= 0f) return;
            float top = (index / TrunkCols) * _rowHeight;
            float offset = _trunkContent.anchoredPosition.y;
            float destination = offset;
            if (top < offset) destination = top;
            else if (top + _rowHeight > offset + height) destination = top + _rowHeight - height;
            if (Mathf.Approximately(offset, destination)) return;
            float max = Mathf.Max(0f, _trunkContent.rect.height - height);
            _trunkScroll.StopMovement();
            _trunkScroll.verticalNormalizedPosition = max > 0f ? 1f - Mathf.Clamp01(destination / max) : 1f;
            OnTrunkScrolled();
        }

        private void ShowPadCardFocus(RectTransform target, uint code)
        {
            if (_padCardFocus.parent != target)
            {
                _padCardFocus.SetParent(target, false);
                _padCardFocus.anchorMin = Vector2.zero;
                _padCardFocus.anchorMax = Vector2.one;
                _padCardFocus.offsetMin = _padCardFocus.offsetMax = Vector2.zero;
                _padCardFocus.SetAsLastSibling();
            }
            _padCardFocus.gameObject.SetActive(true);
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
