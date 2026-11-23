using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DeckEditorUI
    {
        private RectTransform _deckPicker;
        private RectTransform _deckPickerRows;
        private ScrollRect _deckPickerScroll;
        private Text _deckPickerStatus;
        private Button _deckPickerNew;
        private Button _deckPickerFocus;

        private bool IsDeckPickerOpen => _deckPicker != null && _deckPicker.gameObject.activeSelf;

        private void BuildDeckPicker(Transform parent)
        {
            _deckPicker = DuelUiKit.FillRect("DeckPicker", parent);
            DeckEditorStyle.Backdrop(_deckPicker);
            DeckEditorStyle.AddHeader(_deckPicker, "편집할 덱 선택");
            DeckButton(_deckPicker, "Back", "메인 메뉴", .833f, .914f, .981f, .979f,
                CloseDeckPickerToMenu, fontSize: _mobileLayout ? 22 : 17);

            var panel = DeckEditorStyle.Panel(_deckPicker, "SavedDecks", new Vector2(.047f, .053f), new Vector2(.953f, .9f));
            var title = DeckArea(panel, "Title", .025f, .843f, .62f, .95f);
            DuelUiKit.AddFittedText(title, "Label", "저장된 덱", _mobileLayout ? 30 : 26, 16,
                TextAnchor.MiddleLeft, DuelUiKit.TextPrimary, FontStyle.Bold);
            _deckPickerNew = DeckButton(panel, "NewDeck", "새 덱 만들기", .694f, .846f, .973f, .946f,
                CreateNewUserDeck, accent: true, fontSize: _mobileLayout ? 24 : 19);

            var list = DeckArea(panel, "List", .025f, .104f, .976f, .826f);
            DeckEditorStyle.Fill(list, DeckEditorStyle.Inset);
            var viewport = DuelUiKit.FillRect("Viewport", list, 6f);
            viewport.offsetMax = new Vector2(-20f, -6f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _deckPickerRows = DuelUiKit.CreateRect("Rows", viewport, Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
            _deckPickerRows.pivot = new Vector2(.5f, 1f);
            var layout = _deckPickerRows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 7f;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            _deckPickerRows.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _deckPickerScroll = list.gameObject.AddComponent<ScrollRect>();
            _deckPickerScroll.viewport = viewport;
            _deckPickerScroll.content = _deckPickerRows;
            _deckPickerScroll.horizontal = false;
            _deckPickerScroll.scrollSensitivity = 55f;
            _deckPickerScroll.movementType = ScrollRect.MovementType.Clamped;
            _deckPickerScroll.verticalScrollbar = AddDeckScrollbar(list);
            _deckPickerScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            _deckPickerStatus = DuelUiKit.AddFittedText(DeckArea(panel, "Status", .035f, .026f, .962f, .089f),
                "Text", "", _mobileLayout ? 19 : 16, 12, TextAnchor.MiddleLeft, DuelUiKit.TextPrimary);
            _deckPicker.gameObject.SetActive(false);
        }

        private void ShowDeckPicker()
        {
            CancelDeckDrag();
            EndDeckTransfer();
            ClearCardVisual(ref _hoveredSlot);
            _pointerSlot = _inspectedSource = null;
            _workingDeck = null;
            _padUi.Clear();
            _padCardFocus.Hide();
            _editorContent.SetActive(false);
            _deckPicker.gameObject.SetActive(true);
            _deckPicker.SetAsLastSibling();
            _deckPickerStatus.text = DeckRepository.UserDecks.Count == 0 ? "저장된 덱이 없습니다. 새 덱을 만들어 시작하세요." : "";
            for (int i = _deckPickerRows.childCount - 1; i >= 0; i--)
            {
                var child = _deckPickerRows.GetChild(i);
                child.SetParent(null, false);
                child.gameObject.SetActive(false);
                if (Application.isPlaying) Object.Destroy(child.gameObject);
                else Object.DestroyImmediate(child.gameObject);
            }

            _deckPickerFocus = _deckPickerNew;
            float rowHeight = _mobileLayout ? Mathf.Max(77f, MobileSafeArea.MinimumTouchSize(_deckPicker)) : 68f;
            for (int i = 0; i < DeckRepository.UserDecks.Count; i++)
            {
                var deck = DeckRepository.UserDecks[i];
                string id = deck.id;
                var item = DeckEditorStyle.Button(_deckPickerRows, $"SavedDeck_{i}", "", () =>
                {
                    int index = DeckRepository.UserDecks.FindIndex(candidate => candidate.id == id);
                    if (index >= 0) EnterDeckEditor(index);
                    else ShowDeckPicker();
                });
                item.gameObject.AddComponent<LayoutElement>().preferredHeight = rowHeight;
                DuelUiKit.AddFittedText(DeckArea(item.transform, "Name", .036f, .075f, .565f, .925f),
                    "Text", deck.name, _mobileLayout ? 25 : 22, 14, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
                DuelUiKit.AddFittedText(DeckArea(item.transform, "Counts", .575f, .075f, .965f, .925f), "Text",
                    $"메인 {deck.main.Count}장  ·  엑스트라 {deck.extra.Count}장  ·  사이드 {deck.side.Count}장",
                    _mobileLayout ? 19 : 17, 12, TextAnchor.MiddleRight, DuelUiKit.TextPrimary);
                if (i == 0 || i == _userDeckIdx) _deckPickerFocus = item;
            }

            Canvas.ForceUpdateCanvases();
            _deckPickerScroll.StopMovement();
            _deckPickerScroll.verticalNormalizedPosition = 1f;
            UpdateDeckScrollbar(_deckPickerScroll);
            DeckPadUi.LimitKeyboardNavigation(_deckPicker);
            if (EventSystem.current != null) _padUi.FocusInScope(_deckPicker, _deckPickerFocus);
        }

        private void UpdateDeckPicker()
        {
            var frame = _padInput.Read(Time.unscaledTime, includeRightTrigger: true);
            bool cancel = frame.Cancel || Input.GetKeyDown(KeyCode.Escape);
            if (!cancel && !frame.HasInput) return;
            _padUi.AdoptPointerFocus(_deckPicker);
            _padUi.Handle(_deckPicker, frame.Direction, frame.Submit, cancel, CloseDeckPickerToMenu, _deckPickerFocus);
        }

        private void ReturnToDeckPicker() => BeforeReplacingEdits(ShowDeckPicker);

        private void CloseDeckPickerToMenu() => _app?.CloseDeckEditor();
    }
}
