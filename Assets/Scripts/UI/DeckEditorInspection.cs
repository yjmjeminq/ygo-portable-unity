using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DeckEditorUI
    {
        private const float InspectorSlideSeconds = .24f;
        private RectTransform _expandedDeckOverlay;
        private RectTransform _expandedDeckPanel;
        private ScrollRect _expandedDeckScroll;
        private CompositeCardView _expandedDeckArt;
        private Button _expandedDeckClose;
        private Text _expandedDeckName;
        private Text _expandedDeckStats;
        private Text _expandedDeckDescription;
        private GameObject _expandedDeckReturnFocus;
        private float _inspectorSlide;
        private uint _expandedDeckCode;

        private void BuildExpandedDeckCard(Transform root)
        {
            var overlay = LotdUiKit.FillRect("ExpandedDeckCard", root);
            _expandedDeckOverlay = overlay;
            overlay.gameObject.AddComponent<RectMask2D>();
            var backdrop = LotdUiKit.FillRect("Backdrop", overlay);
            var shade = DeckEditorStyle.Fill(backdrop, DeckEditorStyle.Scrim);
            if (_mobileLayout)
            {
                var dismiss = backdrop.gameObject.AddComponent<Button>();
                dismiss.targetGraphic = shade;
                dismiss.transition = Selectable.Transition.None;
                dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
                dismiss.onClick.AddListener(CloseExpandedDeckCard);
            }
            _expandedDeckPanel = DeckEditorStyle.Panel(overlay, "Window",
                _mobileLayout ? new Vector2(.018f, 0f) : new Vector2(.13f, .081f),
                new Vector2(.982f, _mobileLayout ? .876f : .943f));
            if (_mobileLayout) _expandedDeckPanel.GetComponent<Image>().raycastTarget = true;
            float artRight = _mobileLayout ? .361f : .422f;
            float rulesLeft = artRight + .025f;
            var art = DeckArea(_expandedDeckPanel, "Artwork", .048f, .084f, artRight, .913f);
            _expandedDeckArt = CompositeCardView.Create(art, "Card", detailedOverlay: true);
            _expandedDeckArt.EnableOverframe();
            CardOverframeInput.Attach(art, _expandedDeckArt, _mobileLayout);
            var name = DeckArea(_expandedDeckPanel, "Name", rulesLeft, .804f, .759f, .928f);
            _expandedDeckName = LotdUiKit.AddFittedText(name, "Text", "", _mobileLayout ? 36 : 30, _mobileLayout ? 20 : 16, TextAnchor.MiddleLeft,
                LotdUiKit.GoldAccent, FontStyle.Bold);
            _expandedDeckName.supportRichText = false;
            _expandedDeckClose = DeckButton(_expandedDeckPanel, "Close", "닫기", .777f, .788f, .952f, .922f,
                CloseExpandedDeckCard);
            var stats = DeckArea(_expandedDeckPanel, "Stats", rulesLeft, _mobileLayout ? .708f : .669f, .952f, .787f);
            _expandedDeckStats = LotdUiKit.AddFittedText(stats, "Text", "", _mobileLayout ? 24 : 20, _mobileLayout ? 16 : 13,
                TextAnchor.MiddleLeft, LotdUiKit.TextMuted);
            _expandedDeckStats.supportRichText = false;

            var rules = DeckArea(_expandedDeckPanel, "Rules", rulesLeft, .229f, .952f, _mobileLayout ? .696f : .651f);
            DeckEditorStyle.Fill(rules, DeckEditorStyle.Inset);
            var viewport = LotdUiKit.FillRect("Viewport", rules, 13f);
            viewport.offsetMax = new Vector2(-31f, -13f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _expandedDeckDescription = LotdUiKit.AddText(viewport, "Effect", "", _mobileLayout ? 31 : 23, TextAnchor.UpperLeft,
                LotdUiKit.TextPrimary);
            _expandedDeckDescription.supportRichText = false;
            _expandedDeckDescription.verticalOverflow = VerticalWrapMode.Overflow;
            _expandedDeckDescription.lineSpacing = 1.13f;
            _expandedDeckDescription.raycastTarget = false;
            var content = _expandedDeckDescription.rectTransform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _expandedDeckScroll = rules.gameObject.AddComponent<ScrollRect>();
            _expandedDeckScroll.horizontal = false;
            _expandedDeckScroll.viewport = viewport;
            _expandedDeckScroll.content = content;
            _expandedDeckScroll.movementType = ScrollRect.MovementType.Clamped;
            _expandedDeckScroll.scrollSensitivity = 49f;
            _expandedDeckScroll.verticalScrollbar = AddDeckScrollbar(rules);
            _expandedDeckScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            DeckButton(_expandedDeckPanel, "Related", _mobileLayout ? "연관 카드 바로 보기" : "연관 카드 검색",
                rulesLeft, .073f, .71f, .207f, () =>
                {
                    uint code = _expandedDeckCode;
                    CloseExpandedDeckCard();
                    CloseDeckUtility();
                    if (_mobileLayout) ShowMobileRelatedCards(code);
                    else
                    {
                        FilterRelatedCards(code);
                        if (_padMode) SwitchPadArea(PadArea.Trunk);
                    }
                }, accent: _mobileLayout, fontSize: _mobileLayout ? 24 : 18);
            if (_mobileLayout)
                DeckButton(_expandedDeckPanel, "Operations", "카드 조작", .737f, .073f, .952f, .207f,
                    OpenMobileCardActions, fontSize: 23);
            overlay.gameObject.SetActive(false);
        }

        private void OpenExpandedDeckCard(uint code)
        {
            var card = CardDatabase.Get(code);
            if (card == null) return;
            CancelDeckDrag();
            EndDeckTransfer();
            _expandedDeckReturnFocus = EventSystem.current?.currentSelectedGameObject;
            _padUi.Clear();
            _padCardFocus.Hide();
            InspectCard(code);
            _expandedDeckCode = code;
            if (_mobileLayout && _mobileSelectedCode != code)
                SelectMobileCard(code, CurrentPadDeck == DragSourceType.None ? DragSourceType.Trunk : CurrentPadDeck);
            _expandedDeckName.text = card.name;
            _expandedDeckStats.text = card.FormatStatsLineKR();
            _expandedDeckDescription.text = card.desc;
            _expandedDeckOverlay.SetAsLastSibling();
            _expandedDeckOverlay.gameObject.SetActive(true);
            var (cards, index) = InspectedDeckSlot(code);
            _expandedDeckArt.SetDeckCard(code, ReadAppearance(code, cards, index));
            Canvas.ForceUpdateCanvases();
            _expandedDeckScroll.StopMovement();
            _expandedDeckScroll.content.anchoredPosition = Vector2.zero;
            UpdateDeckScrollbar(_expandedDeckScroll);
            _inspectorSlide = 0f;
            UpdateExpandedDeckCard(0f);
            DeckPadUi.LimitKeyboardNavigation(_expandedDeckOverlay);
            EventSystem.current?.SetSelectedGameObject(_expandedDeckClose.gameObject);
        }

        private void UpdateExpandedDeckCard(float delta)
        {
            if (!_expandedDeckOverlay.gameObject.activeSelf || _inspectorSlide >= 1f) return;
            _inspectorSlide = Mathf.MoveTowards(_inspectorSlide, 1f, delta / InspectorSlideSeconds);
            float remaining = 1f - _inspectorSlide;
            Vector2 direction = _mobileLayout ? Vector2.down : Vector2.right;
            float distance = (_mobileLayout ? _expandedDeckPanel.rect.height : _expandedDeckPanel.rect.width) + 36f;
            _expandedDeckPanel.anchoredPosition = direction * (distance * remaining * remaining * remaining);
        }

        private void CloseExpandedDeckCard()
        {
            if (!_expandedDeckOverlay.gameObject.activeSelf) return;
            _padUi.Clear();
            _expandedDeckOverlay.gameObject.SetActive(false);
            var focus = _expandedDeckReturnFocus;
            _expandedDeckReturnFocus = null;
            if (focus != null && focus.activeInHierarchy)
                EventSystem.current?.SetSelectedGameObject(focus);
        }
    }
}
