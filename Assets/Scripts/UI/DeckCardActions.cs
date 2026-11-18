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
        private const float CardActionWidth = 300f;
        private const float CardActionHeight = 292f;
        private GameObject _deckCardActions;
        private RectTransform _deckCardActionPanel;
        private Text _deckCardActionTitle;
        private Button _deckCardActionMain;
        private Button _deckCardActionAlternate;
        private Button _deckCardActionRelated;
        private Button _deckCardActionCrafting;
        private Button _deckCardActionArtwork;
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

            _deckCardActionPanel = LotdUiKit.CreateMenuPanel(overlay, "Window", overlay.pivot, overlay.pivot);
            _deckCardActionPanel.pivot = new Vector2(0f, 1f);
            _deckCardActionPanel.sizeDelta = new Vector2(CardActionWidth, CardActionHeight);
            var title = LotdUiKit.CreateRect("Title", _deckCardActionPanel, new Vector2(.10f, .81f), new Vector2(.90f, .906f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(title.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelNavy, false, false);
            _deckCardActionTitle = LotdUiKit.AddFittedText(title, "Text", "", 18, 12,
                TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

            Button AddRow(string name, string label = "", Action action = null)
            {
                var row = LotdUiKit.CreateRect(name, _deckCardActionPanel, Vector2.zero, Vector2.one,
                    Vector2.zero, Vector2.zero);
                return LotdUiKit.CreateLotdButton(row, "Button", label, action, 17);
            }

            _deckCardActionMain = AddRow("MainAction");
            _deckCardActionAlternate = AddRow("AlternateAction");
            _deckCardActionRelated = AddRow("Related");
            _deckCardActionCrafting = AddRow("Crafting");
            _deckCardActionArtwork = AddRow("Artwork");
            _deckCardActionCancel = AddRow("Cancel", "취소", CloseDeckCardActions);
            _deckCardActionRows = new[] { _deckCardActionMain, _deckCardActionAlternate, _deckCardActionRelated,
                _deckCardActionCrafting, _deckCardActionArtwork, _deckCardActionCancel };
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

            SetDeckCardAction(_deckCardActionRelated, "관련 카드", true, () =>
            {
                FilterRelatedCards(code);
                SwitchPadArea(PadArea.Trunk);
            });
            SetDeckCardAction(_deckCardActionCrafting, "제작·분해",
                CollectionProgress.HasStarter && CollectionProgress.IsCraftableCard(card), () => OpenCardCrafting(code));
            ConfigureTrunkArtworkAction(anchor, source, index, code);
            SetDeckCardActionNavigation();
            _deckCardActionTitle.text = card.name;
            InspectCard(code);
            var overlay = (RectTransform)_deckCardActions.transform;
            PositionDeckCardActions(anchor, overlay);
            overlay.SetAsLastSibling();
            if (!_deckCardActions.activeSelf)
            {
                _deckCardActions.SetActive(true);
                UiEntranceMotion.Play(_deckCardActionPanel.gameObject);
            }
            EventSystem.current?.SetSelectedGameObject(_deckCardActionDefault.gameObject);
        }

        private void ConfigureTrunkArtworkAction(RectTransform anchor, DragSourceType source, int index, uint code)
        {
            CardInfo[] choices = Array.Empty<CardInfo>();
            if (source == DragSourceType.Trunk && CardDatabase.TryGetDeckPrintings(code, out var printings))
                choices = CollectionProgress.Initialized
                    ? printings.Where(printing => printing.code == code || CollectionProgress.GetOwnedCount(printing.code) > 0).ToArray()
                    : printings;

            bool show = choices.Length > 1;
            _deckCardActionArtwork.gameObject.SetActive(show);
            if (show)
            {
                int current = Array.FindIndex(choices, printing => printing.code == code);
                SetDeckCardAction(_deckCardActionArtwork, $"일러스트 변경 · {current + 1}/{choices.Length}", true, () =>
                {
                    uint key = DeckGroupKey(code);
                    if (index >= _trunkResults.Count || DeckGroupKey(_trunkResults[index].code) != key) return;
                    var next = choices[(current + 1) % choices.Length];
                    _trunkPrintChoices[key] = next.code;
                    _trunkResults[index] = next;
                    UpdateVirtualTrunkSlots(_lastFirstRow, force: true);
                    ShowDeckCardActions(anchor, source, index, next.code);
                    EventSystem.current?.SetSelectedGameObject(_deckCardActionArtwork.gameObject);
                }, closeMenu: false);
            }

            _deckCardActionPanel.sizeDelta = new Vector2(CardActionWidth, show ? CardActionHeight + 40f : CardActionHeight);
            float step = show ? .120f : .138f;
            int rowIndex = 0;
            foreach (var button in _deckCardActionRows)
            {
                if (!button.gameObject.activeSelf) continue;
                var row = (RectTransform)button.transform.parent;
                float top = .785f - rowIndex++ * step;
                row.anchorMin = new Vector2(.10f, top - .112f);
                row.anchorMax = new Vector2(.90f, top);
            }
        }

        private void SetDeckCardActionNavigation()
        {
            _deckCardActionDefault = null;
            Button previous = null;
            foreach (var button in _deckCardActionRows)
            {
                if (!button.gameObject.activeSelf || !button.interactable) continue;
                if (_deckCardActionDefault == null) _deckCardActionDefault = button;
                button.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = previous };
                if (previous != null)
                {
                    var navigation = previous.navigation;
                    navigation.selectOnDown = button;
                    previous.navigation = navigation;
                }
                previous = button;
            }
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
