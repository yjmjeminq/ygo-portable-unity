using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DeckEditorUI
    {
        private const float CardActionWidth = 352f;
        private const float CardActionHeight = 360f;
        private GameObject _deckCardActions;
        private RectTransform _deckCardActionPanel;
        private Text _deckCardActionTitle;
        private Button _deckQuantityMinus;
        private Button _deckQuantityPlus;
        private Button _deckQuantityMax;
        private Button _deckCardActionMain;
        private Button _deckCardActionAlternate;
        private Button _deckCardActionRelated;
        private Button _deckCardActionDetails;
        private Button _deckCardActionCrafting;
        private Button _deckCardActionArtwork;
        private Button _deckCardActionEdition;
        private Button _deckCardActionDestiny;
        private Button _deckCardActionCancel;
        private Button _deckCardActionDefault;
        private Button[] _deckCardActionRows;

        private void BuildDeckCardActions(Transform root)
        {
            var overlay = LotdUiKit.FillRect("DeckCardActions", root);
            _deckCardActions = overlay.gameObject;
            var backdrop = LotdUiKit.FillRect("Dismiss", overlay);
            LotdUiKit.AddImage(backdrop.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            var dismiss = backdrop.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
            dismiss.onClick.AddListener(CloseDeckCardActions);

            _deckCardActionPanel = DeckEditorStyle.Panel(overlay, "Window", overlay.pivot, overlay.pivot);
            _deckCardActionPanel.pivot = new Vector2(0f, 1f);
            _deckCardActionPanel.sizeDelta = new Vector2(CardActionWidth, CardActionHeight);
            var title = LotdUiKit.CreateRect("Title", _deckCardActionPanel, new Vector2(.10f, .81f), new Vector2(.90f, .906f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(title.gameObject, LotdUiKit.WhiteSprite, DeckEditorStyle.Surface, false, false);
            _deckCardActionTitle = LotdUiKit.AddFittedText(title, "Text", "", 18, 12,
                TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

            Button AddRow(string name, string label = "", Action action = null)
            {
                var row = LotdUiKit.CreateRect(name, _deckCardActionPanel, Vector2.zero, Vector2.one,
                    Vector2.zero, Vector2.zero);
                return DeckEditorStyle.Button(row, "Button", label, action, 17);
            }

            var quantity = LotdUiKit.CreateRect("Quantity", _deckCardActionPanel, new Vector2(.10f, .685f),
                new Vector2(.90f, .803f), Vector2.zero, Vector2.zero);
            Button Amount(string name, string label, float min, float max)
            {
                var cell = LotdUiKit.CreateRect(name, quantity, new Vector2(min, 0f), new Vector2(max, 1f),
                    Vector2.zero, Vector2.zero);
                return DeckEditorStyle.Button(cell, "Button", label, null, 19);
            }
            _deckQuantityMinus = Amount("Remove", "−", 0f, .23f);
            _deckQuantityPlus = Amount("Add", "+", .26f, .49f);
            _deckQuantityMax = Amount("Max", "MAX", .52f, 1f);

            _deckCardActionMain = AddRow("MainAction");
            _deckCardActionAlternate = AddRow("AlternateAction");
            _deckCardActionRelated = AddRow("Related");
            _deckCardActionDetails = AddRow("Details");
            _deckCardActionCrafting = AddRow("Crafting");
            _deckCardActionArtwork = AddRow("Artwork");
            _deckCardActionEdition = AddRow("Edition");
            _deckCardActionDestiny = AddRow("DestinyDraw");
            _deckCardActionCancel = AddRow("Cancel", "취소", CloseDeckCardActions);
            _deckCardActionRows = new[] { _deckCardActionDetails, _deckCardActionMain, _deckCardActionAlternate,
                _deckCardActionRelated, _deckCardActionCrafting, _deckCardActionArtwork, _deckCardActionEdition,
                _deckCardActionDestiny, _deckCardActionCancel };
            overlay.gameObject.SetActive(false);
        }

        private void ShowDeckCardActions(RectTransform anchor, DragSourceType source, int index, uint code)
        {
            var card = CardDatabase.Get(code);
            if (card == null) return;
            var home = card.IsExtra ? DragSourceType.DeckExtra : DragSourceType.DeckMain;
            string homeName = card.IsExtra ? "엑스트라 덱" : "메인 덱";
            if (source == DragSourceType.Trunk)
            {
                _trunkScroll.StopMovement();
                uint addable = FindAddableCardCopy(code, showReason: false);
                bool canAdd = addable != 0;
                string alternateArt = canAdd && addable != code ? " · 다른 일러스트" : "";
                SetDeckCardAction(_deckCardActionMain, $"{homeName}에 추가{alternateArt}",
                    canAdd && CanPlaceCard(card, home, GetDeckList(home), showReason: false),
                    () => TryAddCardToDeck(code, home, CardVisual(anchor)));
                SetDeckCardAction(_deckCardActionAlternate, $"사이드 덱에 추가{alternateArt}",
                    canAdd && CanPlaceCard(card, DragSourceType.DeckSide, _workingDeck.side, showReason: false),
                    () => TryAddCardToDeck(code, DragSourceType.DeckSide, CardVisual(anchor)));
            }
            else
            {
                var sourceCards = GetDeckList(source);
                if (sourceCards == null || index < 0 || index >= sourceCards.Count || sourceCards[index] != code) return;
                var destination = source == DragSourceType.DeckSide ? home : DragSourceType.DeckSide;
                var target = GetDeckList(destination);
                string destinationName = destination == DragSourceType.DeckSide ? "사이드 덱" : homeName;
                bool StillSelected() => sourceCards == GetDeckList(source) && index < sourceCards.Count && sourceCards[index] == code;

                SetDeckCardAction(_deckCardActionMain, $"{destinationName}으로 이동",
                    CanPlaceCard(card, destination, target, showReason: false), () =>
                    {
                        if (StillSelected())
                            MoveDeckCard(source, index, destination, target.Count);
                    });
                SetDeckCardAction(_deckCardActionAlternate, "덱에서 제거", true, () =>
                {
                    if (!StillSelected()) return;
                    RemoveDeckCard(source, index);
                });
            }

            var quantityZone = source == DragSourceType.Trunk ? home : source;
            int max = CollectionProgress.DeckCopyLimit;
            bool canIncrease = FindAddableCardCopy(code, showReason: false) != 0 &&
                CanPlaceCard(card, quantityZone, GetDeckList(quantityZone), showReason: false);
            var removal = source == DragSourceType.Trunk ? FindTrunkRemoval(code, home) : (source, index);
            SetDeckCardAction(_deckQuantityMinus, "−", removal.Item2 >= 0,
                () => RemoveDeckCard(removal.Item1, removal.Item2));
            SetDeckCardAction(_deckQuantityPlus, "+", canIncrease,
                () => TryAddCardToDeck(code, quantityZone, CardVisual(anchor)));
            SetDeckCardAction(_deckQuantityMax, $"{max}장 투입 (MAX)", canIncrease,
                () => AddDeckCopies(code, quantityZone, max, CardVisual(anchor)));

            SetDeckCardAction(_deckCardActionRelated, "연관 카드 검색", true, () =>
            {
                FilterRelatedCards(code);
                SwitchPadArea(PadArea.Trunk);
            });
            SetDeckCardAction(_deckCardActionDetails, "카드 상세 보기", true, () => OpenExpandedDeckCard(code));
            SetDeckCardAction(_deckCardActionCrafting, "제작·분해",
                CollectionProgress.HasStarter && CollectionProgress.IsCraftableCard(card), () => OpenCardCrafting(code));
            _inspectedSource = anchor != null && anchor.GetComponent<CardDragTrigger>() != null ? anchor : null;
            ConfigureAppearanceActions(anchor, source, index, code);
            _deckCardActionTitle.text = card.name;
            if (_mobileLayout) SelectMobileCard(code, source);
            InspectCard(code);
            var overlay = (RectTransform)_deckCardActions.transform;
            PositionDeckCardActions(anchor, overlay);
            overlay.SetAsLastSibling();
            if (!_deckCardActions.activeSelf)
            {
                _deckCardActions.SetActive(true);
                UiEntranceMotion.Play(_deckCardActionPanel.gameObject);
            }
            SetDeckCardActionNavigation();
            EventSystem.current?.SetSelectedGameObject(_deckCardActionDefault.gameObject);
        }

        private void OpenInspectedCardActions()
        {
            var selected = _inspectedSource?.GetComponent<CardDragTrigger>();
            if (selected == null || selected.CardCodeGetter?.Invoke() != _inspectedCode)
            {
                int index = _trunkResults.FindIndex(card => card.code == _inspectedCode);
                if (index < 0 || _trunkPool.Count == 0) return;
                ShowDeckCardActions((RectTransform)_previewCard.transform, DragSourceType.Trunk, index, _inspectedCode);
                return;
            }
            ShowDeckCardActions((RectTransform)selected.transform, selected.SourceType, selected.IndexGetter(),
                _inspectedCode);
        }

        private (DragSourceType zone, int index) FindTrunkRemoval(uint code, DragSourceType home)
        {
            var homeCards = GetDeckList(home);
            var side = _workingDeck.side;
            int index = homeCards.LastIndexOf(code);
            if (index >= 0) return (home, index);
            index = side.LastIndexOf(code);
            if (index >= 0) return (DragSourceType.DeckSide, index);
            uint key = CollectionProgress.GetDeckCopyKey(code);
            bool SameName(uint other) => CollectionProgress.GetDeckCopyKey(other) == key;
            index = homeCards.FindLastIndex(SameName);
            return index >= 0 ? (home, index) : (DragSourceType.DeckSide, side.FindLastIndex(SameName));
        }

        private void ConfigureAppearanceActions(RectTransform anchor, DragSourceType source, int index, uint code)
        {
            var cards = GetDeckList(source);
            var appearance = ReadAppearance(code, cards, index);
            var choices = CardArtLoader.GetSelectableArtwork(code);
            bool artwork = choices.Length > 1;
            bool edition = appearance.overframe || CardArtLoader.SupportsOverframe(code);
            bool destiny = source == DragSourceType.DeckMain;
            _deckCardActionArtwork.gameObject.SetActive(artwork);
            _deckCardActionEdition.gameObject.SetActive(edition);
            _deckCardActionDestiny.gameObject.SetActive(destiny);
            if (destiny)
                SetDeckCardAction(_deckCardActionDestiny, appearance.destinyDraw ? "★ 데스티니 지정 해제" : "★ 데스티니 카드 지정",
                    true, () =>
                    {
                        ToggleDestiny(code, cards, index);
                        RefreshAppearanceMenu(_deckCardActionDestiny);
                    }, closeMenu: false);
            if (artwork)
            {
                int choice = CardArtLoader.ArtworkChoiceIndex(code, choices, appearance.ArtworkVariant);
                SetDeckCardAction(_deckCardActionArtwork, $"일러스트 변경 · {choice + 1}/{choices.Length}", true, () =>
                {
                    ChangeArtwork(code, cards, index);
                    RefreshAppearanceMenu(_deckCardActionArtwork);
                }, closeMenu: false);
            }
            if (edition)
                SetDeckCardAction(_deckCardActionEdition, appearance.overframe ? "일반판으로 변경" : "오버프레임으로 변경",
                    CanChangeEdition(code, cards, appearance), () =>
                    {
                        ChangeEdition(code, cards, index);
                        RefreshAppearanceMenu(_deckCardActionEdition);
                    }, closeMenu: false);

            void RefreshAppearanceMenu(Button focus)
            {
                uint selected = source == DragSourceType.Trunk ? _trunkResults[index].code : _inspectedCode;
                ShowDeckCardActions(anchor, source, index, selected);
                if (focus.isActiveAndEnabled && focus.IsInteractable())
                    EventSystem.current?.SetSelectedGameObject(focus.gameObject);
            }

            if (_mobileLayout)
            {
                LayoutMobileCardActions(artwork, edition, destiny);
                return;
            }
            int rows = _deckCardActionRows.Count(button => button.gameObject.activeSelf);
            _deckCardActionPanel.sizeDelta = new Vector2(CardActionWidth, CardActionHeight + Math.Max(0, rows - 6) * 36f);
            float step = .603f / rows;
            int rowIndex = 0;
            foreach (var button in _deckCardActionRows)
            {
                if (!button.gameObject.activeSelf) continue;
                var row = (RectTransform)button.transform.parent;
                float top = .666f - rowIndex++ * step;
                row.anchorMin = new Vector2(.10f, top - step + .012f);
                row.anchorMax = new Vector2(.90f, top);
            }
        }

        private void SetDeckCardActionNavigation()
        {
            _deckCardActionDefault = Array.Find(_deckCardActionRows,
                button => button.gameObject.activeSelf && button.interactable);
            DeckPadUi.LimitKeyboardNavigation(_deckCardActionPanel);
        }

        private void SetDeckCardAction(Button button, string label, bool available, Action action, bool closeMenu = true)
        {
            var caption = button.GetComponentInChildren<Text>();
            caption.text = label;
            caption.color = available ? LotdUiKit.TextPrimary : LotdUiKit.TextMuted;
            button.interactable = available;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                if (!button.isActiveAndEnabled || !button.IsInteractable()) return;
                LotdAudio.PlayClick();
                if (closeMenu) CloseDeckCardActions();
                action();
            });
        }

        private void PositionDeckCardActions(RectTransform anchor, RectTransform overlay)
        {
            if (_mobileLayout)
            {
                _deckCardActionPanel.anchoredPosition = new Vector2(-_deckCardActionPanel.rect.width * .5f,
                    _deckCardActionPanel.rect.height * .5f);
                return;
            }
            const float gap = 10f;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(overlay, anchor);
            var screen = overlay.rect;
            float maxLeft = screen.xMax - CardActionWidth - gap;
            float left = bounds.max.x + gap;
            if (left > maxLeft) left = bounds.min.x - CardActionWidth - gap;
            float bodyBottom = Mathf.Lerp(screen.yMin, screen.yMax, _deckPanel.anchorMin.y);
            float bodyTop = Mathf.Lerp(screen.yMin, screen.yMax, _deckPanel.anchorMax.y);
            float top = Mathf.Clamp(bounds.max.y, bodyBottom + _deckCardActionPanel.sizeDelta.y, bodyTop);
            _deckCardActionPanel.anchoredPosition = new Vector2(Mathf.Clamp(left, screen.xMin + gap, maxLeft), top);
        }

        private void CloseDeckCardActions()
        {
            if (!_deckCardActions.activeSelf) return;
            _padUi.Clear();
            _deckCardActions.SetActive(false);
            if (_padMode) SwitchPadArea(_padArea);
        }
    }
}
