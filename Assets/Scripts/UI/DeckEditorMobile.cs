using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.EventSystems;
using YGOPortable.Core;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    public partial class DeckEditorUI
    {
        private const float MobileTrunkRight = .447f;
        private readonly bool _mobileLayout;
        private int _trunkColumns = TrunkCols;
        private int _mainColumns = DeckColumns;
        private int _extraColumns = DeckColumns;
        private int _mainVisibleRows = MainDeckVisibleRows;
        private int _extraVisibleRows = 2;
        private int _sideVisibleColumns = SideDeckVisibleColumns;
        private Vector2 _mobileDeckSize;
        private ScrollRect _extraDeckScroll;
        private RectTransform _extraDeckSlotGrid;

        private static readonly DragSourceType[] MobileDeckSections =
            { DragSourceType.DeckMain, DragSourceType.DeckExtra, DragSourceType.DeckSide };
        private static readonly string[] MobileSectionNames = { "메인 덱", "엑스트라 덱", "사이드 덱" };
        private static readonly int[] MobileSectionRows = { MainDeckVisibleRows, 2, 1 };
        private RectTransform _mobileEditor;
        private RectTransform _mobileToolbar;
        private RectTransform _mobileTitle;
        private RectTransform _mobileSelection;
        private Text _mobileSelectionLabel;
        private Button[] _mobileQuantity;
        private Button _mobileRelated;
        private Button _mobileDetails;
        private Text _mobileTrunkSort;
        private readonly Button[] _mobileSectionButtons = new Button[3];
        private readonly bool[] _mobileSectionsOpen = { true, true, true };
        private DragSourceType _mobileSelectedZone;
        private DragSourceType _mobileSelectedSource;
        private uint _mobileSelectedCode;
        private float _mobileKeyboardInset;
        private Vector2 _mobileEditorSize;

        private void BuildMobileEditor(RectTransform root)
        {
            _mobileEditor = root;
            _mobileTitle = DeckArea(root, "MobileTitle", .027f, 0f, .265f, 1f);
            LotdUiKit.AddFittedText(_mobileTitle, "Title", "덱 편집", 35, 21, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary,
                FontStyle.Bold);
            _mobileToolbar = DeckArea(root, "MobileToolbar", .28f, 0f, .985f, 1f);
            DeckButton(_mobileToolbar, "AutoSort", "자동 정렬", .02f, 0f, .215f, 1f, () => SortWorkingDeck(), fontSize: 24);
            _openDrawButton = DeckButton(_mobileToolbar, "TestDraw", "5장 테스트 드로우", .227f, 0f, .504f, 1f,
                OpenTestDraw, fontSize: 24);
            _saveButtonText = DeckButton(_mobileToolbar, "Save", "저장", .516f, 0f, .716f, 1f,
                () => TrySaveCurrentDeck(), accent: true, fontSize: 24).GetComponentInChildren<Text>();
            DeckButton(_mobileToolbar, "Back", "덱 목록", .728f, 0f, .98f, 1f,
                ReturnToDeckPicker, fontSize: 23);

            _mobileSelection = LotdUiKit.FillRect("MobileCardSelection", root);
            LotdUiKit.AddImage(_mobileSelection.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelNavy, false, false);
            _mobileSelectionLabel = LotdUiKit.AddFittedText(DeckArea(_mobileSelection, "Card", .018f, .05f, .255f, .95f),
                "Label", "", 22, 12, TextAnchor.MiddleLeft, Color.white);
            _mobileQuantity = new Button[4];
            for (int i = 0; i < _mobileQuantity.Length; i++)
            {
                int quantity = (i + 1) % 4;
                float x = .26f + i * .093f;
                _mobileQuantity[i] = DeckButton(_mobileSelection, $"Quantity{quantity}", quantity == 0 ? "제거" : $"{quantity}장",
                    x, .07f, x + .085f, .93f, () => SetMobileCardQuantity(quantity), fontSize: 21);
            }
            _mobileRelated = DeckButton(_mobileSelection, "Related", "연관 카드", .635f, .07f, .806f, .93f,
                () => ShowMobileRelatedCards(_mobileSelectedCode), fontSize: 20);
            _mobileDetails = DeckButton(_mobileSelection, "Details", "효과 상세", .816f, .07f, .982f, .93f,
                () => OpenExpandedDeckCard(_mobileSelectedCode), fontSize: 20);

            var deckCommands = (RectTransform)_deckPanel.Find("DeckControls");
            deckCommands.Find("Save").gameObject.SetActive(false);
            DeckButton(deckCommands, "Examples", "예시 덱", .53f, 0f, .74f, 1f, OpenArchetypeModal, fontSize: 17);
            deckCommands.anchorMax = new Vector2(.46f, 1f);
            ((RectTransform)_deckPanel.Find("EditControls")).anchorMin = new Vector2(.46f, 0f);
            _deckPanel.Find("EditControls/AutoSort").gameObject.SetActive(false);
            ((RectTransform)_deckPanel.Find("EditControls/DeckTools")).anchorMin = new Vector2(.625f, 0f);
            for (int i = 0; i < MobileDeckSections.Length; i++)
            {
                int section = i;
                var button = _mobileSectionButtons[i] = DeckButton(_deckPanel, $"MobileSection{i}", MobileSectionNames[i],
                    .018f, 0f, .982f, 1f, () => ToggleMobileDeckSection(MobileDeckSections[section]), fontSize: 17);
                var label = button.GetComponentInChildren<Text>();
                label.verticalOverflow = VerticalWrapMode.Overflow;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 10;
                label.resizeTextMaxSize = 17;
            }
            foreach (string name in new[] { "MainTab", "ExtraTab", "SideTab", "MainCount", "ExtraCount", "SideCount" })
                _deckPanel.Find(name).gameObject.SetActive(false);
            _mainSortButton.transform.parent.gameObject.SetActive(false);
            _trunkPanel.Find("Filters").gameObject.SetActive(false);
            var searchTools = _trunkPanel.Find("SearchOptions");
            ((RectTransform)searchTools.Find("DetailedSearch")).anchorMax = new Vector2(.26f, 1f);
            ((RectTransform)searchTools.Find("ArchetypeSearch")).anchorMin = new Vector2(.28f, 0f);
            ((RectTransform)searchTools.Find("ArchetypeSearch")).anchorMax = new Vector2(.61f, 1f);
            _mobileTrunkSort = DeckButton(searchTools, "Sort", SortOptions[_sortFilter.value], .63f, 0f, 1f, 1f,
                () => _sortFilter.value = (_sortFilter.value + 1) % SortOptions.Length, fontSize: 18).GetComponentInChildren<Text>();
            _searchInp.textComponent.fontSize = 25;
            _searchInp.placeholder.GetComponent<Text>().fontSize = 24;
            _trunkPanel.Find("TrunkHeading").GetComponent<Image>().color = LotdUiKit.PanelNavy;
            root.Find("CardDetails").gameObject.SetActive(false);
            LayoutMobileEditor();
            _deckHelpLabel.text = "";
        }

        private void SwitchMobilePadArea(PadArea area)
        {
            if (!_mobileLayout || _mobileEditor == null) return;
            int section = area switch
            {
                PadArea.Main => 0,
                PadArea.Extra => 1,
                PadArea.Side => 2,
                _ => -1,
            };
            if (section < 0 || _mobileSectionsOpen[section]) return;
            _mobileSectionsOpen[section] = true;
            RelayoutMobileDeck();
        }

        private void ToggleMobileDeckSection(DragSourceType section)
        {
            int index = Array.IndexOf(MobileDeckSections, section);
            if (index < 0) return;
            _mobileSectionsOpen[index] = !_mobileSectionsOpen[index];
            RelayoutMobileDeck();
        }

        private void RelayoutMobileDeck()
        {
            _mobileDeckSize = Vector2.zero;
            if (_workingDeck != null) RefreshDeckGrids();
        }

        private float MobileHeightScale => Mathf.Lerp(.80f, 1f, Mathf.InverseLerp(680f, 840f, _mobileEditorSize.y));

        private void LayoutMobileEditor()
        {
            _mobileEditorSize = _mobileEditor.rect.size;
            if (_mobileEditorSize.y <= 0f) return;
            float touch = MobileSafeArea.MinimumTouchSize(_mobileEditor);
            float bar = Mathf.Clamp(touch * .48f, 47f, 66f) * MobileHeightScale;
            float selection = Mathf.Clamp(touch * .40f, 43f, 57f) * MobileHeightScale;
            PositionFromTop(_mobileToolbar, 8f, 8f + bar);
            PositionFromTop(_mobileTitle, 8f, 8f + bar);
            _mobileSelection.anchorMin = new Vector2(.02f, 0f);
            _mobileSelection.anchorMax = new Vector2(.98f, 0f);
            _mobileSelection.offsetMin = new Vector2(0f, 34f);
            _mobileSelection.offsetMax = new Vector2(0f, 34f + selection);
            _deckPanel.anchorMin = new Vector2(.458f, 0f);
            _deckPanel.anchorMax = new Vector2(.986f, 1f);
            _deckPanel.offsetMin = new Vector2(0f, 36f + selection);
            _deckPanel.offsetMax = new Vector2(0f, -(16f + bar));
            _deckPanel.ForceUpdateRectTransforms();
            PositionMobileTrunk();

            LayoutMobileQuickFilters();
            if (_workingDeck != null)
            {
                RelayoutMobileDeck();
                ResizeTrunkGrid(false);
            }
            RefreshMobileCardSelection();
        }

        private void UpdateMobileEditor()
        {
            if (!_mobileLayout || _mobileEditor == null) return;
            if (_mobileEditor.rect.size != _mobileEditorSize)
            {
                LayoutMobileEditor();
                _mobileKeyboardInset = -1f;
            }
            Rect keyboard = _searchInp.isFocused && TouchScreenKeyboard.visible ? TouchScreenKeyboard.area : Rect.zero;
            ApplyMobileKeyboardLayout(keyboard, Screen.safeArea);
        }

        private void ApplyMobileKeyboardLayout(Rect keyboard, Rect safeArea)
        {
            float visibleHeight = Mathf.Max(1f, safeArea.height);
            float covered = keyboard.height > 0f && keyboard.xMin < safeArea.xMax && keyboard.xMax > safeArea.xMin &&
                keyboard.yMin < safeArea.yMax && keyboard.yMax > safeArea.yMin
                ? Mathf.Clamp(keyboard.yMax - safeArea.yMin, 0f, visibleHeight) : 0f;
            float inset = covered * _mobileEditor.rect.height / visibleHeight;
            if (Mathf.Abs(inset - _mobileKeyboardInset) < .5f) return;
            _mobileKeyboardInset = inset;
            // Keep the search bar at the safe top while reclaiming the space used by the editor controls.
            PositionMobileTrunk();
            if (_workingDeck != null) ResizeTrunkGrid(false);
        }

        private void PositionMobileTrunk()
        {
            bool typing = _mobileKeyboardInset > 0f;
            _mobileToolbar.gameObject.SetActive(!typing);
            _mobileTitle.gameObject.SetActive(!typing);
            _mobileSelection.gameObject.SetActive(!typing);
            _deckPanel.gameObject.SetActive(!typing);
            _trunkPanel.anchorMin = new Vector2(.014f, 0f);
            _trunkPanel.anchorMax = new Vector2(typing ? .986f : MobileTrunkRight, 1f);
            _trunkPanel.offsetMin = new Vector2(0f, typing ? _mobileKeyboardInset + 4f : _deckPanel.offsetMin.y);
            _trunkPanel.offsetMax = new Vector2(0f, typing ? -5f : _deckPanel.offsetMax.y);
            _trunkPanel.ForceUpdateRectTransforms();
        }

        private static void PositionFromTop(RectTransform rect, float top, float bottom)
        {
            rect.anchorMin = new Vector2(rect.anchorMin.x, 1f);
            rect.anchorMax = new Vector2(rect.anchorMax.x, 1f);
            rect.offsetMin = new Vector2(rect.offsetMin.x, -bottom);
            rect.offsetMax = new Vector2(rect.offsetMax.x, -top);
            rect.ForceUpdateRectTransforms();
        }

        private void ReflowMobileDeck()
        {
            if (!_mobileLayout || _mobileEditor == null || _deckPanel.rect.size == _mobileDeckSize) return;
            float height = _deckPanel.rect.height;
            if (height <= 0f) return;
            _mobileDeckSize = _deckPanel.rect.size;
            float commands = 40f * MobileHeightScale;
            float header = 25f * MobileHeightScale;
            const float gap = 4f;
            PositionFromTop((RectTransform)_deckPanel.Find("DeckControls"), 8f, 8f + commands);
            PositionFromTop((RectTransform)_deckPanel.Find("EditControls"), 8f, 8f + commands);
            float y = 13f + commands;
            int openRows = 0;
            for (int i = 0; i < MobileSectionRows.Length; i++)
                if (_mobileSectionsOpen[i]) openRows += MobileSectionRows[i];

            float body = Mathf.Max(1f, height - y - (header + gap) * MobileDeckSections.Length - 9f);
            float rowHeight = body / Mathf.Max(1, openRows);
            var grids = new[] { _mainGridRect, _extraGridRect, _sideGridRect };
            for (int i = 0; i < grids.Length; i++)
            {
                PositionFromTop((RectTransform)_mobileSectionButtons[i].transform.parent, y, y + header);
                y += header + gap;
                float sectionHeight = rowHeight * MobileSectionRows[i];
                PositionFromTop(grids[i], y, y + sectionHeight);
                grids[i].gameObject.SetActive(_mobileSectionsOpen[i]);
                if (_mobileSectionsOpen[i]) y += sectionHeight;
            }

            _mainDeckScroll.viewport.ForceUpdateRectTransforms();
            _extraDeckScroll.viewport.ForceUpdateRectTransforms();
            _sideDeckScroll.viewport.ForceUpdateRectTransforms();
            _mainColumns = MobileDeckColumns(_mainDeckScroll.viewport.rect.width);
            _extraColumns = MobileDeckColumns(_extraDeckScroll.viewport.rect.width);
            _sideVisibleColumns = MobileDeckColumns(_sideDeckScroll.viewport.rect.width);
            SetSlotLayout(_mainSlots, _mainColumns);
            SetSlotLayout(_extraSlots, _extraColumns);
        }

        private static int MobileDeckColumns(float width) =>
            Mathf.Clamp(Mathf.FloorToInt(width / 72f), 1, DeckColumns);

        private void RefreshMobileSectionLabels()
        {
            if (!_mobileLayout || _mobileEditor == null || _workingDeck == null) return;
            for (int i = 0; i < MobileDeckSections.Length; i++)
            {
                var section = MobileDeckSections[i];
                bool expanded = _mobileSectionsOpen[i];
                _mobileSectionButtons[i].GetComponentInChildren<Text>().text =
                    $"{(expanded ? "▾" : "▸")}  {MobileSectionNames[i]}   {GetDeckList(section).Count}장";
            }
        }

        private void SelectMobileCard(uint code, DragSourceType source)
        {
            _mobileSelectedCode = code;
            _mobileSelectedSource = source;
            _mobileSelectedZone = source == DragSourceType.Trunk ? HomeDeckForCard(code) : source;
            RefreshMobileCardSelection();
        }

        private static DragSourceType HomeDeckForCard(uint code) =>
            CardDatabase.Get(code)?.IsExtra == true ? DragSourceType.DeckExtra : DragSourceType.DeckMain;

        private void SetMobileCardQuantity(int quantity)
        {
            var list = GetDeckList(_mobileSelectedZone);
            if (list == null || _mobileSelectedCode == 0) return;
            uint key = CollectionProgress.GetDeckCopyKey(_mobileSelectedCode);
            bool SameCard(uint code) => CollectionProgress.GetDeckCopyKey(code) == key;
            int current = list.Count(SameCard);
            if (current < quantity)
                AddDeckCopies(_mobileSelectedCode, _mobileSelectedZone, quantity - current);
            else if (current > quantity)
            {
                for (int i = current; i > quantity; i--)
                    _workingDeck.RemoveCard(list, list.FindLastIndex(SameCard));
                FinishDeckEdit();
            }
            RefreshMobileCardSelection();
        }

        private void RefreshMobileCardSelection()
        {
            if (!_mobileLayout || _mobileSelectionLabel == null) return;
            bool selected = _workingDeck != null && _mobileSelectedCode != 0 &&
                GetDeckList(_mobileSelectedZone) is List<uint>;
            int copies = 0;
            if (selected)
            {
                uint key = CollectionProgress.GetDeckCopyKey(_mobileSelectedCode);
                copies = GetDeckList(_mobileSelectedZone).Count(code => CollectionProgress.GetDeckCopyKey(code) == key);
            }
            _mobileSelectionLabel.text = selected
                ? $"{CardDatabase.GetName(_mobileSelectedCode)}  ·  {copies}장"
                : "카드를 탭하여 상세 보기";
            for (int i = 0; i < _mobileQuantity.Length; i++)
            {
                int target = (i + 1) % 4;
                _mobileQuantity[i].interactable = selected && target != copies;
                _mobileQuantity[i].GetComponentInChildren<Text>().color =
                    selected && target == copies ? LotdUiKit.GoldAccent : Color.white;
            }
            _mobileRelated.interactable = _mobileDetails.interactable = selected;
        }

        private void ShowMobileRelatedCards(uint code)
        {
            if (code == 0) return;
            SwitchPadArea(PadArea.Trunk);
            FilterRelatedCards(code);
        }

        private void OpenMobileCardActions()
        {
            CloseExpandedDeckCard();
            if (_mobileSelectedCode == 0) return;
            if (_mobileSelectedSource == DragSourceType.Trunk)
            {
                int index = _trunkResults.FindIndex(card => card.code == _mobileSelectedCode);
                if (index >= 0)
                    ShowDeckCardActions(_mobileSelection, DragSourceType.Trunk, index, _mobileSelectedCode);
                return;
            }

            int slot = GetDeckList(_mobileSelectedSource)?.IndexOf(_mobileSelectedCode) ?? -1;
            if (slot >= 0) ShowDeckCardActions(_mobileSelection, _mobileSelectedSource, slot, _mobileSelectedCode);
        }

        private static void SetSlotLayout(
            List<(RectTransform rt, CompositeCardView card, CardDragTrigger trigger)> slots, int columns)
        {
            int rows = (slots.Count + columns - 1) / columns;
            for (int i = 0; i < slots.Count; i++)
                SetDeckSlotPosition(slots[i].rt, i, columns, rows);
        }

        private static void SetDeckSlotPosition(RectTransform rt, int index, int cols, int rows)
        {
            int row = index / cols;
            int col = index % cols;
            rt.anchorMin = new Vector2(col / (float)cols, 1f - (row + 1f) / rows);
            rt.anchorMax = new Vector2((col + 1f) / cols, 1f - row / (float)rows);
        }

        private void LayoutMobileCardActions(bool showArtwork, bool showEdition)
        {
            var overlay = (RectTransform)_deckCardActions.transform;
            float touch = MobileSafeArea.MinimumTouchSize(overlay);
            float width = Mathf.Min(overlay.rect.width - 20f, Mathf.Max(550f, 5.2f * touch));
            int lines = showEdition ? 6 : 5;
            float height = Mathf.Min(overlay.rect.height - 18f, touch * lines + 49f);
            _deckCardActionPanel.sizeDelta = new Vector2(width, height);
            float step = (height - 32f) / lines;
            void Place(RectTransform row, int line, float left = .07f, float right = .93f)
            {
                float y = 1f - (16f + line * step) / height;
                row.anchorMin = new Vector2(left, y - (step - 3f) / height);
                row.anchorMax = new Vector2(right, y);
            }
            void PlaceButton(Button button, int line, float left, float right) =>
                Place((RectTransform)button.transform.parent, line, left, right);

            Place((RectTransform)_deckCardActionPanel.Find("Title"), 0, .07f, .71f);
            Place((RectTransform)_deckCardActionPanel.Find("Quantity"), 1);
            PlaceButton(_deckCardActionDetails, 2, .07f, .495f);
            PlaceButton(_deckCardActionRelated, 2, .505f, .93f);
            PlaceButton(_deckCardActionMain, 3, .07f, .495f);
            PlaceButton(_deckCardActionAlternate, 3, .505f, .93f);
            PlaceButton(_deckCardActionCrafting, 4, .07f, showArtwork ? .495f : .93f);
            PlaceButton(_deckCardActionArtwork, 4, .505f, .93f);
            if (showEdition) PlaceButton(_deckCardActionEdition, 5, .07f, .93f);
            PlaceButton(_deckCardActionCancel, 0, .724f, .93f);
        }
    }
}
