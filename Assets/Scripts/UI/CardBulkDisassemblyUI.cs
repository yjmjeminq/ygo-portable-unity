using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class CardBulkDisassemblyUI
    {
        private const int PageSize = 7;

        private sealed class CardEntry
        {
            internal uint Code;
            internal string Name;
            internal bool Overframe;
            internal int Owned;
        }

        private sealed class CardRow
        {
            internal RectTransform Holder;
            internal Button Button;
            internal Text Name;
            internal Text Quantity;
            internal Text Rarity;
        }

        private readonly Action _close;
        private readonly Action _changed;
        private readonly GameObject _catalogRoot;
        private readonly GameObject _reviewRoot;
        private readonly InputField _search;
        private readonly List<CardEntry> _cards = new();
        private readonly List<CardEntry> _matches = new();
        private readonly List<CardEntry> _reviewCards = new();
        private readonly Dictionary<uint, int> _selected = new();
        private readonly CardRow[] _rows = new CardRow[PageSize];
        private readonly CardRow[] _reviewRows = new CardRow[PageSize];
        private readonly Text _dust;
        private readonly Text _reviewDust;
        private readonly CompositeCardView _art;
        private readonly Text _cardName;
        private readonly Text _cardInfo;
        private readonly Text _cardRarity;
        private readonly CollectionCardFilters _filters;
        private readonly Text _count;
        private readonly Text _pageLabel;
        private readonly Text _reviewPageLabel;
        private readonly Text _summary;
        private readonly Text _reviewSummary;
        private readonly Text _status;
        private readonly Button _previous;
        private readonly Button _next;
        private readonly Button _reviewPrevious;
        private readonly Button _reviewNext;
        private readonly Button _decrement;
        private readonly Button _increment;
        private readonly Button _maximum;
        private readonly Button _selectExcess;
        private readonly Button _autoSelect;
        private readonly Button _selectAll;
        private readonly Button _clear;
        private readonly Button _review;
        private readonly Button _reviewCancel;
        private readonly Dictionary<uint, int> _selectedOverframes = new();
        private CardEntry _inspected;
        private int _page;
        private int _reviewPage;

        internal bool IsOpen => _catalogRoot.activeSelf || _reviewRoot.activeSelf;
        internal GameObject ActiveRoot => _reviewRoot.activeSelf ? _reviewRoot : _catalogRoot;
        internal InputField Search => _reviewRoot.activeSelf ? null : _search;

        internal CardBulkDisassemblyUI(Transform parent, Action close, Action changed)
        {
            _close = close;
            _changed = changed;
            var root = BuildScreen(parent, "Screen_CardBulkDisassembly", "카드 일괄 분해", "제작으로", Back);
            _catalogRoot = root.gameObject;
            var catalog = Panel(root, "Catalog", .025f, .083f, .555f, .882f);
            Heading(catalog, "Heading", "분해할 카드 선택", .08f, .858f, .92f, .93f);
            _search = PackShopStyle.Search(Slot(catalog, "Search", .08f, .768f, .92f, .845f),
                "Input", "카드명·효과·카드 번호 검색", 17);
            _search.onValueChanged.AddListener(_ => Filter());
            _filters = new CollectionCardFilters(Slot(catalog, "CollectionFilters", .08f, .700f, .92f, .762f), Filter);
            _count = Label(catalog, "Count", "", .085f, .658f, .915f, .695f, 16, TextAnchor.MiddleLeft);
            for (int i = 0; i < _rows.Length; i++)
            {
                int row = i;
                _rows[i] = BuildRow(catalog, row, () => InspectRow(row));
            }
            _previous = AddButton(catalog, "Previous", "이전", .08f, .06f, .28f, .13f, () => ChangePage(-1));
            _pageLabel = Label(catalog, "Page", "", .30f, .06f, .70f, .13f, 18);
            _next = AddButton(catalog, "Next", "다음", .72f, .06f, .92f, .13f, () => ChangePage(1));

            var details = Panel(root, "Details", .57f, .083f, .975f, .882f);
            _cardName = Heading(details, "Name", "", .08f, .858f, .92f, .93f);
            _art = CompositeCardView.Create(Slot(details, "Art", .073f, .382f, .468f, .842f), "Card", true);
            _cardRarity = ShopRarityBadge.Create(details, new Vector2(.492f, .766f), new Vector2(.59f, .827f), 17);
            _cardInfo = Label(details, "CardInfo", "", .492f, .652f, .92f, .759f, 18, TextAnchor.MiddleLeft);
            _decrement = AddButton(details, "Decrease", "−", .492f, .563f, .596f, .638f, () => AdjustQuantity(-1));
            _increment = AddButton(details, "Increase", "+", .61f, .563f, .714f, .638f, () => AdjustQuantity(1));
            _maximum = AddButton(details, "Maximum", "최대", .728f, .563f, .92f, .638f, SelectMaximum);
            _selectExcess = AddButton(details, "SelectExcess", "3장 남기고 분해", .492f, .462f, .70f, .537f, SelectExcess);
            _autoSelect = AddButton(details, "SelectSpare", "카드별 1장 남기기", .712f, .462f, .92f, .537f, SelectSpare);
            _selectAll = AddButton(details, "SelectAll", "전체 보유 선택", .492f, .375f, .70f, .45f, SelectAll);
            _clear = AddButton(details, "Clear", "선택 초기화", .712f, .375f, .92f, .45f, ClearSelection);
            _summary = Label(details, "Summary", "", .085f, .298f, .915f, .365f, 18, TextAnchor.MiddleLeft);
            _dust = BuildDustLabel(details, .217f, .292f);
            _review = AddButton(details, "Review", "분해 내역 확인", .08f, .124f, .92f, .205f, OpenReview, true);
            _status = Label(details, "Status", "", .08f, .052f, .92f, .119f, 16);
            Footer(root, "방향키 이동   A 선택   Y 검색   LB/RB 페이지   B 뒤로");

            var review = BuildScreen(parent, "Screen_CardDisassemblyReview", "카드 분해 확인", "취소", CancelReview);
            _reviewRoot = review.gameObject;
            var list = Panel(review, "SelectedCards", .025f, .083f, .555f, .882f);
            Heading(list, "Heading", "분해할 카드", .08f, .858f, .92f, .93f);
            for (int i = 0; i < _reviewRows.Length; i++)
                _reviewRows[i] = BuildRow(list, i, null);
            _reviewPrevious = AddButton(list, "Previous", "이전", .08f, .06f, .28f, .13f, () => ChangePage(-1));
            _reviewPageLabel = Label(list, "Page", "", .30f, .06f, .70f, .13f, 18);
            _reviewNext = AddButton(list, "Next", "다음", .72f, .06f, .92f, .13f, () => ChangePage(1));
            var totals = Panel(review, "Details", .57f, .083f, .975f, .882f);
            Heading(totals, "Heading", "받을 가루", .08f, .858f, .92f, .93f);
            _reviewSummary = Label(totals, "Summary", "", .08f, .732f, .92f, .815f, 22);
            _reviewDust = BuildDustLabel(totals, .60f, .71f);
            Label(totals, "Explanation", "덱에 넣은 카드도 분해됩니다.\n보유 카드가 부족해진 덱은 수정 후 사용할 수 있습니다.",
                .10f, .397f, .90f, .556f, 19);
            _reviewCancel = AddButton(totals, "Cancel", "취소", .08f, .138f, .43f, .229f, CancelReview);
            AddButton(totals, "Confirm", "카드 분해", .47f, .138f, .92f, .229f, ConfirmDisassembly, true);
            Footer(review, "방향키 이동   A 선택   LB/RB 페이지   B 취소");
            _catalogRoot.SetActive(false);
            _reviewRoot.SetActive(false);
        }

        internal void Show()
        {
            _reviewRoot.SetActive(false);
            _filters.Selection = (0, 1, 0);
            _selected.Clear();
            _selectedOverframes.Clear();
            _reviewCards.Clear();
            _search.SetTextWithoutNotify("");
            _status.text = "";
            ReloadCards();
            _catalogRoot.SetActive(true);
            Filter();
            FocusFirstRow();
        }

        internal void Hide()
        {
            _catalogRoot.SetActive(false);
            _reviewRoot.SetActive(false);
            _selected.Clear();
            _selectedOverframes.Clear();
            _reviewCards.Clear();
            _art.SetCard(0);
        }

        internal void Back()
        {
            if (_reviewRoot.activeSelf)
            {
                CancelReview();
                return;
            }
            Hide();
            _close();
        }

        internal void ChangePage(int delta)
        {
            if (_reviewRoot.activeSelf)
            {
                _reviewPage = Mathf.Clamp(_reviewPage + delta, 0, PageCount(_reviewCards.Count) - 1);
                RefreshReview();
                return;
            }
            int page = Mathf.Clamp(_page + delta, 0, PageCount(_matches.Count) - 1);
            if (page == _page) return;
            _page = page;
            RefreshPage();
            FocusFirstRow();
        }

        private void ReloadCards()
        {
            _cards.Clear();
            foreach (var card in CardDatabase.AllCards.Where(CollectionProgress.IsCraftableCard)
                .OrderBy(card => card.name, StringComparer.OrdinalIgnoreCase).ThenBy(card => card.code))
            {
                uint code = card.code;
                _cards.Add(new CardEntry { Code = code, Name = card.name, Owned = CollectionProgress.GetOwnedCount(code) });
                int special = CollectionProgress.GetOwnedOverframeCount(code);
                if (special > 0)
                    _cards.Add(new CardEntry { Code = code, Name = card.name, Owned = special, Overframe = true });
            }
            foreach (var selection in new[] { _selected, _selectedOverframes })
            {
                bool overframe = selection == _selectedOverframes;
                foreach (uint code in selection.Keys.ToArray())
                {
                    int limit = CollectionProgress.GetDisassembleLimit(code, overframe);
                    if (limit > 0) selection[code] = Math.Min(selection[code], limit);
                    else selection.Remove(code);
                }
            }
            var counts = _cards.Where(card => card.Owned > 0).GroupBy(card => CollectionProgress.GetDeckCopyKey(card.Code))
                .Select(group => group.Sum(card => (long)card.Owned)).ToArray();
            _selectExcess.interactable = counts.Any(count => count > CollectionProgress.DeckCopyLimit);
            _autoSelect.interactable = counts.Any(count => count > 1);
            _selectAll.interactable = counts.Length > 0;
        }

        private void Filter()
        {
            var query = new CardSearchQuery(_search.text);
            _matches.Clear();
            _matches.AddRange(query.Order(_cards.Where(entry => _filters.Matches(entry.Code, entry.Owned)), entry => entry.Code));
            _page = 0;
            RefreshPage();
        }

        private void RefreshPage()
        {
            int first = _page * PageSize;
            for (int i = 0; i < _rows.Length; i++)
            {
                var card = first + i < _matches.Count ? _matches[first + i] : null;
                WriteRow(_rows[i], card, false);
            }
            _count.text = $"검색 결과 {_matches.Count:N0}종";
            _pageLabel.text = $"{_page + 1} / {PageCount(_matches.Count)}";
            _previous.interactable = _page > 0;
            _next.interactable = _page + 1 < PageCount(_matches.Count);
            Inspect(first < _matches.Count ? _matches[first] : null);
        }

        private void InspectRow(int row)
        {
            int index = _page * PageSize + row;
            if (index < _matches.Count) Inspect(_matches[index]);
        }

        private void Inspect(CardEntry card)
        {
            _inspected = card;
            _cardName.text = card?.Name ?? "분해할 카드가 없습니다";
            uint code = card?.Code ?? 0;
            PackShopStyle.ShowCardPreview(_art, code);
            ShopRarityBadge.Update(_cardRarity, code);
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            int quantity = _inspected == null ? 0 : Quantity(_inspected);
            int limit = _inspected?.Owned ?? 0;
            _cardInfo.text = _inspected == null
                ? ""
                : $"보유 {limit:N0}장\n선택 {quantity:N0}장";
            _decrement.interactable = quantity > 0;
            _increment.interactable = quantity < limit;
            _maximum.interactable = quantity < limit;
            _clear.interactable = _review.interactable = SelectionCount > 0;
            int first = _page * PageSize;
            for (int i = 0; i < _rows.Length && first + i < _matches.Count; i++)
            {
                var card = _matches[first + i];
                var row = _rows[i];
                row.Name.color = card == _inspected ? PackShopStyle.Accent : PackShopStyle.TextPrimary;
                int selected = Quantity(card);
                row.Quantity.text = $"선택 {selected:N0} / {card.Owned:N0}";
                row.Quantity.color = selected > 0 ? PackShopStyle.Accent : PackShopStyle.TextMuted;
            }
            RefreshTotals(_summary, _dust);
        }

        private void RefreshTotals(Text summary, Text dust)
        {
            long count = _selected.Values.Concat(_selectedOverframes.Values).Sum(quantity => (long)quantity);
            decimal earned = _selected.Sum(entry => (decimal)entry.Value * CollectionProgress.GetDisassemblyDustPerCard(entry.Key)) +
                _selectedOverframes.Sum(entry => (decimal)entry.Value * CollectionProgress.GetDisassemblyDustPerCard(entry.Key, true));
            summary.text = $"선택 {SelectionCount:N0}종 · {count:N0}장";
            dust.text = $"가루 +{earned:N0}";
        }

        private int SelectionCount => _selected.Count + _selectedOverframes.Count;
        private Dictionary<uint, int> Selection(CardEntry card) => card.Overframe ? _selectedOverframes : _selected;
        private int Quantity(CardEntry card) => Selection(card).GetValueOrDefault(card.Code);

        private void AdjustQuantity(int delta)
        {
            if (_inspected == null) return;
            int quantity = (int)Math.Clamp((long)Quantity(_inspected) + delta, 0, _inspected.Owned);
            var selection = Selection(_inspected);
            if (quantity == 0) selection.Remove(_inspected.Code);
            else selection[_inspected.Code] = quantity;
            _status.text = "";
            RefreshSelection();
        }

        private void SelectMaximum()
        {
            if (_inspected == null || _inspected.Owned == 0) return;
            Selection(_inspected)[_inspected.Code] = _inspected.Owned;
            _status.text = "";
            RefreshSelection();
        }

        internal void SelectExcess()
        {
            if (!_selectExcess.IsInteractable()) return;
            SelectFromCollection(CollectionProgress.DeckCopyLimit, "카드별로 3장을 초과한 수량만 선택했습니다.");
            OpenReview();
        }

        private void SelectSpare() => SelectFromCollection(1, "카드별로 1장씩 남겼습니다.");

        private void SelectAll() => SelectFromCollection(0, "보유한 카드를 전부 선택했습니다.");

        private void SelectFromCollection(int keepCopies, string status)
        {
            _selected.Clear();
            _selectedOverframes.Clear();
            foreach (var group in _cards.Where(card => card.Owned > 0).GroupBy(card => CollectionProgress.GetDeckCopyKey(card.Code)))
            {
                int remaining = keepCopies;
                foreach (var card in group.OrderByDescending(card => card.Overframe))
                {
                    int keep = Math.Min(remaining, card.Owned);
                    remaining -= keep;
                    int quantity = card.Owned - keep;
                    if (quantity > 0) Selection(card).Add(card.Code, quantity);
                }
            }
            _status.color = PackShopStyle.TextPrimary;
            _status.text = status;
            RefreshSelection();
        }

        private void ClearSelection()
        {
            _selected.Clear();
            _selectedOverframes.Clear();
            _status.text = "";
            RefreshSelection();
        }

        private void OpenReview()
        {
            if (SelectionCount == 0) return;
            _reviewPage = 0;
            _reviewCards.Clear();
            _reviewCards.AddRange(_cards.Where(card => Quantity(card) > 0));
            _catalogRoot.SetActive(false);
            _reviewRoot.SetActive(true);
            RefreshReview();
            EventSystem.current?.SetSelectedGameObject(_reviewCancel.gameObject);
        }

        private void RefreshReview()
        {
            int first = _reviewPage * PageSize;
            for (int i = 0; i < _reviewRows.Length; i++)
            {
                var card = first + i < _reviewCards.Count ? _reviewCards[first + i] : null;
                WriteRow(_reviewRows[i], card, true);
            }
            _reviewPageLabel.text = $"{_reviewPage + 1} / {PageCount(_reviewCards.Count)}";
            _reviewPrevious.interactable = _reviewPage > 0;
            _reviewNext.interactable = _reviewPage + 1 < PageCount(_reviewCards.Count);
            RefreshTotals(_reviewSummary, _reviewDust);
        }

        private void CancelReview()
        {
            _reviewRoot.SetActive(false);
            _catalogRoot.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(_review.gameObject);
        }

        private void ConfirmDisassembly()
        {
            if (!_reviewRoot.activeInHierarchy) return;
            bool success = CollectionProgress.TryDisassembleCards(_selected, _selectedOverframes, out string error);
            if (success)
            {
                _selected.Clear();
                _selectedOverframes.Clear();
            }
            ReloadCards();
            _reviewRoot.SetActive(false);
            _catalogRoot.SetActive(true);
            _status.color = success ? PackShopStyle.TextPrimary : PackShopStyle.Danger;
            _status.text = success ? "가루를 받았습니다." : error;
            Filter();
            FocusFirstRow();
            if (success) _changed?.Invoke();
        }

        private void FocusFirstRow() => EventSystem.current?.SetSelectedGameObject(_matches.Count > 0
            ? _rows[0].Button.gameObject : _search.gameObject);

        private void WriteRow(CardRow row, CardEntry card, bool review)
        {
            row.Holder.gameObject.SetActive(card != null);
            if (card == null) return;
            row.Name.text = card.Overframe ? $"[OF] {card.Name}" : card.Name;
            if (review) row.Quantity.text = $"{Quantity(card):N0}장";
            ShopRarityBadge.Update(row.Rarity, card.Code);
        }

        private static CardRow BuildRow(Transform parent, int index, Action select)
        {
            bool selectable = select != null;
            float top = (selectable ? .65f : .815f) - index * (selectable ? .071f : .093f);
            var holder = Slot(parent, $"Card_{index}", .08f, top - (selectable ? .064f : .071f), .92f, top);
            var row = new CardRow { Holder = holder };
            Transform content = holder;
            if (selectable)
            {
                row.Button = PackShopStyle.Button(holder, "Button", "", select);
                row.Button.gameObject.AddComponent<StarterRowFocus>().ShowPreview = select;
                row.Button.GetComponentInChildren<Text>().gameObject.SetActive(false);
                content = row.Button.transform;
            }
            else
            {
                DuelUiKit.AddImage(holder.gameObject, DuelUiKit.WhiteSprite, PackShopStyle.SurfaceInset, false, false);
            }
            row.Name = Label(content, "Name", "", .06f, 0f, .633f, 1f, 18, TextAnchor.MiddleLeft);
            row.Quantity = Label(content, "Quantity", "", .635f, 0f, .814f, 1f, 16);
            row.Rarity = ShopRarityBadge.Create(content, new Vector2(.831f, .17f), new Vector2(.947f, .83f), 16);
            return row;
        }

        private static Text BuildDustLabel(Transform parent, float bottom, float top)
        {
            var slot = Slot(parent, "Dust", .08f, bottom, .92f, top);
            DuelUiKit.AddImage(slot.gameObject, DuelUiKit.WhiteSprite, PackShopStyle.SurfaceInset, false, false);
            return DuelUiKit.AddFittedText(slot, "Text", "", 21, 12, TextAnchor.MiddleCenter,
                PackShopStyle.Accent, FontStyle.Bold);
        }

        private static RectTransform BuildScreen(Transform parent, string name, string title, string back, Action close)
        {
            var root = DuelUiKit.FillRect(name, parent);
            PackShopStyle.Backdrop(root);
            PackShopStyle.Header(root, title);
            AddButton(root, "Back", back, .825f, .917f, .968f, .975f, close);
            return root;
        }

        private static void Footer(Transform root, string controls)
        {
            var panel = PackShopStyle.Panel(root, "Footer", new Vector2(.035f, .017f), new Vector2(.965f, .059f));
            DuelUiKit.AddFittedText(panel, "Controls", controls, 16, 11, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);
        }


        private static int PageCount(int count) => Mathf.Max(1, (count + PageSize - 1) / PageSize);

        private static RectTransform Panel(Transform parent, string name, float left, float bottom, float right, float top) =>
            PackShopStyle.Panel(parent, name, new Vector2(left, bottom), new Vector2(right, top));

        private static RectTransform Slot(Transform parent, string name, float left, float bottom, float right, float top) =>
            DuelUiKit.CreateRect(name, parent, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Text Heading(Transform parent, string name, string value, float left, float bottom, float right, float top)
        {
            var slot = Slot(parent, name, left, bottom, right, top);
            return DuelUiKit.AddFittedText(slot, "Text", value, 23, 14, TextAnchor.MiddleCenter,
                PackShopStyle.TextPrimary, FontStyle.Bold);
        }

        private static Text Label(Transform parent, string name, string text, float left, float bottom, float right, float top,
            int size, TextAnchor align = TextAnchor.MiddleCenter) =>
            DuelUiKit.AddFittedText(Slot(parent, name, left, bottom, right, top),
                "Text", text, size, Mathf.Max(11, size - 6), align, PackShopStyle.TextPrimary);

        private static Button AddButton(Transform parent, string name, string title, float left, float bottom, float right,
            float top, Action action, bool primary = false) => PackShopStyle.Button(
            Slot(parent, name, left, bottom, right, top), "Button", title, action, 18, primary);
    }
}
