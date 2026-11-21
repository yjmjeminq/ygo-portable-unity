using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    public partial class DeckEditorUI
    {
        private const float MinimumMobileCardCellWidth = 111f;
        private readonly bool _mobileLayout;
        private int _trunkColumns = TrunkCols;
        private int _mainColumns = DeckColumns;
        private int _extraColumns = DeckColumns;
        private int _mainVisibleRows = MainDeckVisibleRows;
        private int _extraVisibleRows = 2;
        private int _sideVisibleColumns = SideDeckVisibleColumns;
        private Vector2 _mobileDeckSize;
        private float _mobileTouchSize;
        private ScrollRect _extraDeckScroll;
        private RectTransform _extraDeckSlotGrid;

        private void ReflowMobileDeck()
        {
            if (!_mobileLayout || _deckPanel.rect.size == _mobileDeckSize) return;
            float height = _deckPanel.rect.height;
            if (height <= 0f) return;
            _mobileDeckSize = _deckPanel.rect.size;
            _mobileTouchSize = MobileSafeArea.MinimumTouchSize(_deckPanel);

            const float bodyTop = .822f;
            const float bodyBottom = .025f;
            float gutter = height * .04f;
            float gridHeight = height * (bodyTop - bodyBottom) - gutter * 3f;
            float rowGoal = Mathf.Min(_mobileTouchSize + 3f, Mathf.Max(1f, gridHeight / 3f));
            float sideHeight = rowGoal;
            float extraHeight = gridHeight >= 5f * rowGoal ? 2f * rowGoal : rowGoal;
            float mainHeight = gridHeight - sideHeight - extraHeight;

            float sideTop = bodyBottom + sideHeight / height;
            float extraBottom = sideTop + gutter / height;
            float extraTop = extraBottom + extraHeight / height;
            float mainBottom = extraTop + gutter / height;
            float mainTop = mainBottom + mainHeight / height;

            PositionDeckSection("Main", _mainGridRect, _mainCountText, mainTop, mainBottom);
            PositionDeckSection("Extra", _extraGridRect, _extraCountText, extraTop, extraBottom);
            PositionDeckSection("Side", _sideGridRect, _sideCountText, sideTop, bodyBottom);
            SetVerticalAnchors((RectTransform)_mainSortButton.transform.parent, mainTop, mainTop + .034f);

            _mainDeckScroll.viewport.ForceUpdateRectTransforms();
            _extraDeckScroll.viewport.ForceUpdateRectTransforms();
            _sideDeckScroll.viewport.ForceUpdateRectTransforms();
            _mainColumns = ColumnsForTouch(_mainDeckScroll.viewport.rect.width, DeckColumns);
            _extraColumns = ColumnsForTouch(_extraDeckScroll.viewport.rect.width, DeckColumns);
            _sideVisibleColumns = ColumnsForTouch(_sideDeckScroll.viewport.rect.width, SideDeckVisibleColumns);
            _mainVisibleRows = Mathf.Clamp(Mathf.FloorToInt(mainHeight / rowGoal + .001f), 1, MainDeckVisibleRows);
            _extraVisibleRows = Mathf.Clamp(Mathf.FloorToInt(extraHeight / rowGoal + .001f), 1, 2);
            SetSlotLayout(_mainSlots, _mainColumns);
            SetSlotLayout(_extraSlots, _extraColumns);
        }

        private int ColumnsForTouch(float width, int max) =>
            Mathf.Clamp(Mathf.FloorToInt(width / (Mathf.Max(MinimumMobileCardCellWidth, _mobileTouchSize) + 4f)), 1, max);

        private void PositionDeckSection(string name, RectTransform grid, Text count, float top, float bottom)
        {
            SetVerticalAnchors(grid, bottom, top);
            SetVerticalAnchors((RectTransform)_deckPanel.Find(name + "Tab"), top, top + .033f);
            SetVerticalAnchors((RectTransform)count.rectTransform.parent, top, top + .034f);
        }

        private static void SetVerticalAnchors(RectTransform rect, float bottom, float top)
        {
            rect.anchorMin = new Vector2(rect.anchorMin.x, bottom);
            rect.anchorMax = new Vector2(rect.anchorMax.x, top);
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

        private void LayoutMobileCardActions(bool showArtwork)
        {
            var overlay = (RectTransform)_deckCardActions.transform;
            float touch = MobileSafeArea.MinimumTouchSize(overlay);
            float width = Mathf.Min(overlay.rect.width - 20f, Mathf.Max(550f, 5.2f * touch));
            float height = Mathf.Min(overlay.rect.height - 18f, touch * 5f + 49f);
            _deckCardActionPanel.sizeDelta = new Vector2(width, height);
            float step = (height - 32f) / 5f;
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
            PlaceButton(_deckCardActionMain, 2, .07f, .495f);
            PlaceButton(_deckCardActionAlternate, 2, .505f, .93f);
            PlaceButton(_deckCardActionRelated, 3, .07f, .495f);
            PlaceButton(_deckCardActionDetails, 3, .505f, .93f);
            PlaceButton(_deckCardActionCrafting, 4, .07f, showArtwork ? .495f : .93f);
            PlaceButton(_deckCardActionArtwork, 4, .505f, .93f);
            PlaceButton(_deckCardActionCancel, 0, .724f, .93f);
        }
    }
}
