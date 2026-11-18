using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class PackContentsUI
    {
        private const int PageSize = 8;
        private readonly Button[] _rows = new Button[PageSize];
        private readonly Text[] _labels = new Text[PageSize];
        private readonly Text[] _rarities = new Text[PageSize];
        private readonly List<(uint Code, string Name, string SearchName)> _cards = new();
        private readonly List<(uint Code, string Name)> _matches = new();
        private readonly Text _packName;
        private readonly Text _count;
        private readonly Text _pageLabel;
        private readonly Text _cardName;
        private readonly Text _owner;
        private readonly Text _rarity;
        private readonly Text _stats;
        private readonly Text _description;
        private readonly ScrollRect _descriptionScroll;
        private readonly CompositeCardView _art;
        private readonly Button _findPacks;
        private readonly Button _previous;
        private readonly Button _next;
        private int _page;
        private uint _selectedCode;

        internal GameObject Root { get; }
        internal InputField SearchInput { get; }

        internal PackContentsUI(Transform parent, Action onBack, Action<uint> onFindPacks = null)
        {
            var root = LotdUiKit.FillRect("Screen_PackContents", parent);
            Root = root.gameObject;
            PackShopStyle.Backdrop(root);
            PackShopStyle.Header(root, "팩 수록 카드");
            AddButton(root, "Back", "팩 상점으로", .825f, .917f, .968f, .975f, onBack);

            var catalog = PackShopStyle.Panel(root, "Catalog", new Vector2(.025f, .083f), new Vector2(.445f, .882f));
            _packName = Heading(catalog, "PackName", .08f, .862f, .92f, .93f);
            var search = Slot(catalog, "Search", .08f, .769f, .92f, .848f);
            SearchInput = PackShopStyle.Search(search, "Input", "이 팩에서 카드명·카드 번호 검색", 17);
            _count = Label(catalog, "Count", .087f, .721f, .914f, .76f, 16, TextAnchor.MiddleLeft, PackShopStyle.TextMuted);

            for (int i = 0; i < PageSize; i++)
            {
                int row = i;
                float top = .71f - i * .073f;
                var button = AddButton(catalog, $"Card_{i}", "", .08f, top - .065f, .92f, top,
                    () => SelectRow(row));
                button.gameObject.AddComponent<StarterRowFocus>().ShowPreview = () => SelectRow(row);
                _rows[i] = button;
                var label = button.GetComponentInChildren<Text>();
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 12;
                label.resizeTextMaxSize = 18;
                label.supportRichText = false;
                label.rectTransform.anchorMin = new Vector2(.05f, 0f);
                label.rectTransform.anchorMax = new Vector2(.81f, 1f);
                _labels[i] = label;
                _rarities[i] = ShopRarityBadge.Create(button.transform, new Vector2(.83f, .19f),
                    new Vector2(.945f, .81f), 16);
            }

            _previous = AddButton(catalog, "Previous", "이전", .08f, .063f, .28f, .127f, () => ChangePage(-1));
            _pageLabel = Label(catalog, "Page", .31f, .063f, .69f, .127f, 18, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);
            _next = AddButton(catalog, "Next", "다음", .72f, .063f, .92f, .127f, () => ChangePage(1));

            var details = PackShopStyle.Panel(root, "Details", new Vector2(.459f, .083f), new Vector2(.975f, .882f));
            _cardName = Heading(details, "Name", .075f, .862f, .925f, .93f);
            _art = CompositeCardView.Create(Slot(details, "Art", .055f, .093f, .497f, .846f), "Card", true);
            _owner = Label(details, "Owner", .52f, .748f, .822f, .828f, 19, TextAnchor.MiddleLeft, PackShopStyle.Accent);
            _rarity = ShopRarityBadge.Create(details, new Vector2(.835f, .759f), new Vector2(.919f, .816f), 17);
            _stats = Label(details, "Stats", .52f, .625f, .919f, .739f, 18, TextAnchor.MiddleLeft, PackShopStyle.TextPrimary);
            (_descriptionScroll, _description) = CreateDescription(details);
            if (onFindPacks != null)
            {
                _findPacks = AddButton(details, "FindPacks", "이 카드가 든 팩", .52f, .139f, .92f, .213f,
                    () => { if (_selectedCode != 0) onFindPacks(_selectedCode); }, true);
            }

            var footer = PackShopStyle.Panel(root, "Footer", new Vector2(.035f, .017f), new Vector2(.965f, .059f));
            LotdUiKit.AddFittedText(footer, "Controls", "방향키 이동   A 선택   Y 검색   LB/RB 페이지   오른쪽 스틱 설명   B 뒤로",
                16, 11, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);

            SearchInput.onValueChanged.AddListener(_ => Filter());
            Root.SetActive(false);
        }

        internal void Open(CollectionPack pack)
        {
            _packName.text = pack.Name;
            _cards.Clear();
            foreach (var code in pack.CardPool.OrderBy(CardDatabase.GetName, StringComparer.OrdinalIgnoreCase).ThenBy(code => code))
            {
                string name = CardDatabase.GetName(code);
                _cards.Add((code, name, Normalize(name)));
            }
            SearchInput.SetTextWithoutNotify("");
            Root.SetActive(true);
            Filter();
            FocusFirstRow();
        }

        internal void Close()
        {
            if (_art.CurrentCode != 0) _art.SetCard(0);
            _selectedCode = 0;
            _description.text = "";
            Root.SetActive(false);
        }

        internal void Resume()
        {
            Root.SetActive(true);
            SelectCard(_selectedCode);
            if (_findPacks != null && _findPacks.IsInteractable())
                EventSystem.current?.SetSelectedGameObject(_findPacks.gameObject);
            else
                FocusFirstRow();
        }

        internal void ChangePage(int delta)
        {
            int target = Mathf.Clamp(_page + delta, 0, PageCount - 1);
            if (target == _page) return;
            _page = target;
            RefreshPage();
            FocusFirstRow();
        }

        internal void ScrollDescription(float direction)
        {
            if (direction == 0f) return;
            float range = _descriptionScroll.content.rect.height - _descriptionScroll.viewport.rect.height;
            if (range <= 0) return;
            _descriptionScroll.StopMovement();
            _descriptionScroll.verticalNormalizedPosition = Mathf.Clamp01(_descriptionScroll.verticalNormalizedPosition +
                direction * 300f * Time.unscaledDeltaTime / range);
        }

        private void Filter()
        {
            string query = SearchInput.text.Trim();
            string normalized = Normalize(query);
            bool isPasscode = uint.TryParse(query, out uint passcode);
            _matches.Clear();
            foreach (var card in _cards)
            {
                if (query.Length == 0 || (isPasscode ? card.Code == passcode :
                    card.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalized.Length != 0 && card.SearchName.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0))
                    _matches.Add((card.Code, card.Name));
            }
            _page = 0;
            RefreshPage();
        }

        private int PageCount => Mathf.Max(1, (_matches.Count + PageSize - 1) / PageSize);

        private void RefreshPage()
        {
            int first = _page * PageSize;
            for (int i = 0; i < _rows.Length; i++)
            {
                int index = first + i;
                _rows[i].transform.parent.gameObject.SetActive(index < _matches.Count);
                if (index < _matches.Count)
                {
                    _labels[i].text = _matches[index].Name;
                    ShopRarityBadge.Update(_rarities[i], _matches[index].Code);
                }
            }

            _count.text = SearchInput.text.Trim().Length == 0
                ? $"수록 카드 {_cards.Count:N0}종"
                : $"검색 결과 {_matches.Count:N0} / {_cards.Count:N0}종";
            _pageLabel.text = $"{_page + 1} / {PageCount}";
            _previous.interactable = _page > 0;
            _next.interactable = _page + 1 < PageCount;
            SelectCard(first < _matches.Count ? _matches[first].Code : 0);
        }

        private void FocusFirstRow() => EventSystem.current?.SetSelectedGameObject(_matches.Count > 0
            ? _rows[0].gameObject : SearchInput.gameObject);

        private void SelectRow(int row)
        {
            int index = _page * PageSize + row;
            if (index < _matches.Count) SelectCard(_matches[index].Code);
        }

        private void SelectCard(uint code)
        {
            var info = CardDatabase.Get(code);
            _cardName.text = code == 0 ? "검색 결과 없음" : CardDatabase.GetName(code);
            _owner.text = code == 0 ? "" : $"보유 {CollectionProgress.GetOwnedCount(code):N0}장  ·  카드 번호 {code:D8}";
            ShopRarityBadge.Update(_rarity, code);
            _stats.text = info?.FormatStatsLineKR() ?? "";
            if (_selectedCode != code)
            {
                _description.text = info?.desc ?? "";
                _descriptionScroll.StopMovement();
                _descriptionScroll.content.anchoredPosition = Vector2.zero;
            }
            _selectedCode = code;
            PackShopStyle.ShowCardPreview(_art, code);
            if (_findPacks != null) _findPacks.interactable = code != 0;

            int first = _page * PageSize;
            for (int i = 0; i < _labels.Length && first + i < _matches.Count; i++)
            {
                Color color = _matches[first + i].Code == code ? PackShopStyle.Accent : PackShopStyle.TextPrimary;
                _rows[i].GetComponent<PackShopButtonLabel>().Normal = color;
                _labels[i].color = color;
            }
        }

        private static (ScrollRect, Text) CreateDescription(Transform parent)
        {
            var holder = Slot(parent, "Description", .52f, .232f, .921f, .614f);
            LotdUiKit.AddImage(holder.gameObject, LotdUiKit.WhiteSprite, PackShopStyle.SurfaceInset);
            var viewport = LotdUiKit.CreateRect("Viewport", holder, Vector2.zero, Vector2.one,
                new Vector2(8f, 5f), new Vector2(-20f, -5f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = LotdUiKit.AddText(viewport, "Text", "", 18, TextAnchor.UpperLeft, PackShopStyle.TextPrimary);
            content.supportRichText = false;
            content.verticalOverflow = VerticalWrapMode.Overflow;
            content.rectTransform.anchorMin = Vector2.up;
            content.rectTransform.anchorMax = Vector2.one;
            content.rectTransform.pivot = new Vector2(.5f, 1f);
            content.rectTransform.sizeDelta = Vector2.zero;
            content.rectTransform.anchoredPosition = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = holder.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content.rectTransform;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.verticalScrollbar = PackShopStyle.AddVerticalScrollbar(holder);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.scrollSensitivity = 25f;
            return (scroll, content);
        }

        private static string Normalize(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray());

        private static RectTransform Slot(Transform parent, string name, float left, float bottom, float right, float top) =>
            LotdUiKit.CreateRect(name, parent, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Text Heading(Transform parent, string name, float left, float bottom, float right, float top)
        {
            var slot = Slot(parent, name, left, bottom, right, top);
            return LotdUiKit.AddFittedText(slot, "Text", "", 24, 15, TextAnchor.MiddleCenter,
                PackShopStyle.TextPrimary, FontStyle.Bold);
        }

        private static Text Label(Transform parent, string name, float left, float bottom, float right, float top,
            int size, TextAnchor alignment, Color color) => LotdUiKit.AddFittedText(Slot(parent, name, left, bottom, right, top),
            "Text", "", size, Mathf.Max(11, size - 7), alignment, color);

        private static Button AddButton(Transform parent, string name, string text, float left, float bottom, float right, float top,
            Action action, bool primary = false) => PackShopStyle.Button(Slot(parent, name, left, bottom, right, top),
            "Button", text, action, 18, primary);
    }
}
