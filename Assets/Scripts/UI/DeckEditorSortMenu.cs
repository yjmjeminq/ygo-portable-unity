using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DeckEditorUI
    {
        private static readonly (DeckMainSortMode Mode, string Label)[] MainSortOptions =
        {
            (DeckMainSortMode.Frame, "카드 프레임별"),
            (DeckMainSortMode.Name, "이름 가나다순"),
            (DeckMainSortMode.Attack, "공격력 높은 순"),
            (DeckMainSortMode.Defense, "수비력 높은 순"),
            (DeckMainSortMode.Level, "레벨·랭크·링크 높은 순"),
        };

        private GameObject _mainSortMenu;
        private GameObject _mainSortReturnFocus;
        private Button _mainSortButton;
        private Button _wholeSortButton;
        private Text _deckSortTitle;
        private readonly Button[] _mainSortOptions = new Button[MainSortOptions.Length];
        private readonly Button[] _wholeSortOptions = new Button[2];
        private int _lastMainSort;
        private int _lastWholeSort;
        private bool _sortingWholeDeck;

        private Button ActiveDeckSortOption =>
            _sortingWholeDeck ? _wholeSortOptions[_lastWholeSort] : _mainSortOptions[_lastMainSort];

        private static Button AddMainSortButton(Transform panel, Action open)
        {
            var holder = LotdUiKit.CreateRect("MainSort", panel, new Vector2(.23f, .79f),
                new Vector2(.56f, .824f), Vector2.zero, Vector2.zero);
            var button = DeckEditorStyle.Button(holder, "Button", "메인 정렬 ▼", open, 15);
            var caption = button.GetComponentInChildren<Text>();
            caption.resizeTextForBestFit = true;
            caption.resizeTextMaxSize = 15;
            caption.resizeTextMinSize = 10;
            return button;
        }

        private void BuildMainSortMenu(Transform parent)
        {
            var overlay = LotdUiKit.FillRect("MainDeckSort", parent);
            _mainSortMenu = overlay.gameObject;
            var backdrop = LotdUiKit.FillRect("Dismiss", overlay);
            DeckEditorStyle.Fill(backdrop, new Color(DeckEditorStyle.Scrim.r, DeckEditorStyle.Scrim.g, DeckEditorStyle.Scrim.b, .48f));
            var dismiss = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            dismiss.callback.AddListener(_ => CloseMainDeckSort());
            backdrop.gameObject.AddComponent<EventTrigger>().triggers.Add(dismiss);

            var menu = DeckEditorStyle.Panel(overlay, "Window", new Vector2(.635f, .274f), new Vector2(.96f, .723f));
            var title = SortMenuRow(menu, "Title", .833f, .956f);
            _deckSortTitle = LotdUiKit.AddFittedText(title, "Text", "메인 덱 정렬", 23, 15, TextAnchor.MiddleCenter,
                LotdUiKit.GoldAccent, FontStyle.Bold);
            for (int index = 0; index < MainSortOptions.Length; index++)
            {
                int choice = index;
                float top = .813f - index * .123f;
                var row = SortMenuRow(menu, $"Order_{index}", top - .103f, top);
                _mainSortOptions[index] = DeckEditorStyle.Button(row, "Button", MainSortOptions[index].Label,
                    () => ChooseMainSort(choice), 17);
            }
            _wholeSortOptions[0] = DeckEditorStyle.Button(SortMenuRow(menu, "WholeDefault", .640f, .767f),
                "Button", "종류·이름순", () => ChooseWholeSort(byFrame: false), 17);
            _wholeSortOptions[1] = DeckEditorStyle.Button(SortMenuRow(menu, "WholeFrame", .465f, .592f),
                "Button", "카드 프레임별", () => ChooseWholeSort(byFrame: true), 17);
            var cancel = SortMenuRow(menu, "Cancel", .075f, .182f);
            DeckEditorStyle.Button(cancel, "Button", "취소", () => CloseMainDeckSort(), 16);
            _mainSortMenu.SetActive(false);
        }

        private static RectTransform SortMenuRow(Transform parent, string name, float bottom, float top) =>
            LotdUiKit.CreateRect(name, parent, new Vector2(.092f, bottom), new Vector2(.908f, top),
                Vector2.zero, Vector2.zero);

        private void OpenMainDeckSort() => OpenDeckSort(wholeDeck: false);

        private void OpenWholeDeckSort() => OpenDeckSort(wholeDeck: true);

        private void OpenDeckSort(bool wholeDeck)
        {
            if (_mainSortMenu.activeSelf || _workingDeck == null) return;
            _sortingWholeDeck = wholeDeck;
            _deckSortTitle.text = wholeDeck ? "전체 덱 정렬" : "메인 덱 정렬";
            foreach (var option in _mainSortOptions) option.transform.parent.gameObject.SetActive(!wholeDeck);
            foreach (var option in _wholeSortOptions) option.transform.parent.gameObject.SetActive(wholeDeck);
            var events = EventSystem.current;
            _mainSortReturnFocus = _padMode && _padArea == PadArea.Main ? null : events?.currentSelectedGameObject;
            _padUi.Clear();
            _padCardFocus.Hide();
            _mainSortMenu.transform.SetAsLastSibling();
            _mainSortMenu.SetActive(true);
            DeckPadUi.LimitKeyboardNavigation(_mainSortMenu.transform);
            UiEntranceMotion.Play(_mainSortMenu.transform.Find("Window").gameObject);
            events?.SetSelectedGameObject(ActiveDeckSortOption.gameObject);
        }

        private void CloseMainDeckSort(bool restoreFocus = true)
        {
            if (!_mainSortMenu.activeSelf) return;
            _padUi.Clear();
            _mainSortMenu.SetActive(false);
            var previous = _mainSortReturnFocus;
            _mainSortReturnFocus = null;
            if (!restoreFocus) return;
            if (_padMode && _padArea == PadArea.Main)
            {
                EventSystem.current?.SetSelectedGameObject(null);
                DrawPadCardFocus();
            }
            else
            {
                var control = previous != null ? previous.GetComponent<Selectable>() : null;
                EventSystem.current?.SetSelectedGameObject(control != null && control.isActiveAndEnabled && control.IsInteractable()
                    ? previous : (_sortingWholeDeck ? _wholeSortButton : _mainSortButton).gameObject);
            }
        }

        private void ChooseMainSort(int index)
        {
            if (!_mainSortMenu.activeSelf) return;
            _lastMainSort = index;
            var (mode, label) = MainSortOptions[index];
            SortMainDeck(mode);
            CloseMainDeckSort();
            ShowToast($"메인 덱 정렬 · {label}");
        }

        private void ChooseWholeSort(bool byFrame)
        {
            if (!_mainSortMenu.activeSelf) return;
            _lastWholeSort = byFrame ? 1 : 0;
            SortWorkingDeck(byFrame, byName: !byFrame);
            CloseMainDeckSort();
        }

        private static uint[] OrderDeckGroups(IEnumerable<uint> cards, DeckMainSortMode mode)
        {
            var groups = cards.GroupBy(DeckGroupKey).ToDictionary(group => group.Key);
            return DeckMainSorter.Order(groups.Keys, mode).SelectMany(key => groups[key]).ToArray();
        }

        private void SortMainDeck(DeckMainSortMode mode)
        {
            var main = _workingDeck.main;
            var sorted = OrderDeckGroups(main, mode);
            if (main.SequenceEqual(sorted)) return;

            var before = main.ToArray();
            var positions = CaptureDeckPositions(DragSourceType.DeckMain, before.Length);
            CancelDeckDrag();
            EndDeckTransfer();
            _workingDeck.ReorderCards(main, sorted);
            RefreshDeckGrids();
            AnimateOrderedDeck(DragSourceType.DeckMain, main, before, positions);
        }
    }
}
