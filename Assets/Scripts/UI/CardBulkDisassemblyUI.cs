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
            internal string SearchName;
            internal CardRarity Rarity;
            internal int Owned;
            internal int Limit;
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
        private readonly Text[] _points;
        private readonly Text[] _reviewPoints;
        private readonly CompositeCardView _art;
        private readonly Text _cardName;
        private readonly Text _cardInfo;
        private readonly Text _cardRarity;
        private readonly Text _filterLabel;
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
        private readonly Button _autoSelect;
        private readonly Button _clear;
        private readonly Button _review;
        private readonly Button _reviewCancel;
        private CardRarity? _filter;
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
            _search = LotdUiKit.CreateInputField(Slot(catalog, "Search", .08f, .768f, .663f, .845f),
                "Input", "카드명·카드 번호 검색", 17);
            _search.GetComponent<Outline>().effectColor = PackShopStyle.TextMuted;
            _search.onValueChanged.AddListener(_ => Filter());
            var filter = AddButton(catalog, "RarityFilter", "", .675f, .768f, .92f, .845f, CycleRarity);
            _filterLabel = filter.GetComponentInChildren<Text>();
            _count = Label(catalog, "Count", "", .085f, .713f, .915f, .758f, 16, TextAnchor.MiddleLeft);
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
            _art = CompositeCardView.Create(Slot(details, "Art", .085f, .47f, .375f, .839f), "Card", true);
            _cardRarity = ShopRarityBadge.Create(details, new Vector2(.408f, .766f), new Vector2(.506f, .827f), 17);
            _cardInfo = Label(details, "CardInfo", "", .41f, .652f, .92f, .759f, 18, TextAnchor.MiddleLeft);
            _decrement = AddButton(details, "Decrease", "−", .408f, .563f, .538f, .638f, () => AdjustQuantity(-1));
            _increment = AddButton(details, "Increase", "+", .55f, .563f, .68f, .638f, () => AdjustQuantity(1));
            _maximum = AddButton(details, "Maximum", "최대", .696f, .563f, .92f, .638f, SelectMaximum);
            _autoSelect = AddButton(details, "SelectSpare", "카드별 1장 남기기", .08f, .375f, .60f, .45f, SelectSpare);
            _clear = AddButton(details, "Clear", "선택 초기화", .615f, .375f, .92f, .45f, ClearSelection);
            _summary = Label(details, "Summary", "", .085f, .298f, .915f, .365f, 18, TextAnchor.MiddleLeft);
            _points = BuildPointLabels(details, .217f, .292f);
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
            Heading(totals, "Heading", "받을 제작 포인트", .08f, .858f, .92f, .93f);
            _reviewSummary = Label(totals, "Summary", "", .08f, .732f, .92f, .815f, 22);
            _reviewPoints = BuildPointLabels(totals, .60f, .71f);
            Label(totals, "Explanation", "선택한 카드의 보유 수량이 줄어들고\n등급별 제작 포인트가 지급됩니다.",
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
            _filter = null;
            _selected.Clear();
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
            var limits = CollectionProgress.GetDisassembleLimits();
            foreach (var entry in limits)
            {
                string name = CardDatabase.GetName(entry.Key);
                _cards.Add(new CardEntry
                {
                    Code = entry.Key,
                    Name = name,
                    SearchName = Normalize(name),
                    Rarity = CardRarityCatalog.GetRarity(entry.Key),
                    Owned = CollectionProgress.GetOwnedCount(entry.Key),
                    Limit = entry.Value,
                });
            }
            _cards.Sort((left, right) =>
            {
                int comparison = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
                return comparison != 0 ? comparison : left.Code.CompareTo(right.Code);
            });
            foreach (var code in _selected.Keys.ToArray())
            {
                if (limits.TryGetValue(code, out int limit))
                    _selected[code] = Math.Min(_selected[code], limit);
                else
                    _selected.Remove(code);
            }
            _autoSelect.interactable = _cards.Any(card => card.Owned > 1);
        }

        private void CycleRarity()
        {
            int index = _filter.HasValue ? (int)_filter.Value + 1 : 0;
            _filter = index < CardRarityCatalog.TierCount ? (CardRarity)index : null;
            Filter();
        }

        private void Filter()
        {
            string query = _search.text.Trim();
            string normalized = Normalize(query);
            bool byCode = uint.TryParse(query, out uint code);
            _matches.Clear();
            foreach (var entry in _cards)
            {
                if (_filter.HasValue && entry.Rarity != _filter.Value) continue;
                if (query.Length == 0 || (byCode ? entry.Code == code :
                    entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalized.Length > 0 && entry.SearchName.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0))
                    _matches.Add(entry);
            }
            _filterLabel.text = _filter.HasValue ? $"등급: {CardRarityCatalog.GetLabel(_filter.Value)}" : "등급: 전체";
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
            _count.text = $"분해 가능 {_matches.Count:N0}종";
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
            if (_art.CurrentCode != code) _art.SetCard(code);
            _art.gameObject.SetActive(code != 0);
            ShopRarityBadge.Update(_cardRarity, code);
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            int quantity = _inspected == null ? 0 : Quantity(_inspected.Code);
            int limit = _inspected?.Limit ?? 0;
            _cardInfo.text = _inspected == null
                ? ""
                : $"보유 {_inspected.Owned:N0}장 · 분해 가능 {limit:N0}장\n선택 {quantity:N0}장";
            _decrement.interactable = quantity > 0;
            _increment.interactable = quantity < limit;
            _maximum.interactable = quantity < limit;
            _clear.interactable = _selected.Count > 0;
            _review.interactable = _selected.Count > 0;
            int first = _page * PageSize;
            for (int i = 0; i < _rows.Length && first + i < _matches.Count; i++)
            {
                var card = _matches[first + i];
                var row = _rows[i];
                row.Name.color = card == _inspected ? PackShopStyle.Accent : LotdUiKit.TextPrimary;
                int selected = Quantity(card.Code);
                row.Quantity.text = $"선택 {selected:N0} / {card.Limit:N0}";
                row.Quantity.color = selected > 0 ? PackShopStyle.Accent : PackShopStyle.TextMuted;
            }
            RefreshTotals(_summary, _points);
        }

        private void RefreshTotals(Text summary, Text[] points)
        {
            var earned = new long[CardRarityCatalog.TierCount];
            long count = 0;
            foreach (var selection in _selected)
            {
                count += selection.Value;
                var rarity = CardRarityCatalog.GetRarity(selection.Key);
                earned[(int)rarity] += (long)selection.Value * CollectionProgress.DisassemblePointValue;
            }
            summary.text = $"선택 {_selected.Count:N0}종 · {count:N0}장";
            for (int i = 0; i < earned.Length; i++)
                points[i].text = $"{CardRarityCatalog.GetLabel((CardRarity)i)}  +{earned[i]:N0}";
        }

        private int Quantity(uint code) => _selected.TryGetValue(code, out int count) ? count : 0;

        private void AdjustQuantity(int delta)
        {
            if (_inspected == null) return;
            int quantity = Mathf.Clamp(Quantity(_inspected.Code) + delta, 0, _inspected.Limit);
            if (quantity == 0) _selected.Remove(_inspected.Code);
            else _selected[_inspected.Code] = quantity;
            _status.text = "";
            RefreshSelection();
        }

        private void SelectMaximum()
        {
            if (_inspected == null) return;
            _selected[_inspected.Code] = _inspected.Limit;
            _status.text = "";
            RefreshSelection();
        }

        private void SelectSpare()
        {
            _selected.Clear();
            foreach (var card in _cards)
            {
                int quantity = Math.Min(card.Limit, card.Owned - 1);
                if (quantity > 0) _selected.Add(card.Code, quantity);
            }
            _status.text = _selected.Count == 0 ? "분해할 여분 카드가 없습니다." : "모든 카드에서 분해할 여분을 선택했습니다.";
            RefreshSelection();
        }

        private void ClearSelection()
        {
            _selected.Clear();
            _status.text = "";
            RefreshSelection();
        }

        private void OpenReview()
        {
            if (_selected.Count == 0) return;
            _reviewPage = 0;
            _reviewCards.Clear();
            _reviewCards.AddRange(_cards.Where(card => _selected.ContainsKey(card.Code)));
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
            RefreshTotals(_reviewSummary, _reviewPoints);
        }

        private void CancelReview()
        {
            _reviewRoot.SetActive(false);
            _catalogRoot.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(_review.gameObject);
        }

        private void ConfirmDisassembly()
        {
            bool success = CollectionProgress.TryDisassembleCards(_selected, out string error);
            if (success) _selected.Clear();
            ReloadCards();
            _reviewRoot.SetActive(false);
            _catalogRoot.SetActive(true);
            _status.text = success ? "제작 포인트를 받았습니다." : error;
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
            row.Name.text = card.Name;
            if (review) row.Quantity.text = $"{Quantity(card.Code):N0}장";
            ShopRarityBadge.Update(row.Rarity, card.Code);
        }

        private static CardRow BuildRow(Transform parent, int index, Action select)
        {
            bool selectable = select != null;
            float top = (selectable ? .704f : .815f) - index * (selectable ? .079f : .093f);
            var holder = Slot(parent, $"Card_{index}", .08f, top - .071f, .92f, top);
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
                LotdUiKit.AddImage(holder.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelNavy, false, false);
            }
            row.Name = Label(content, "Name", "", .06f, 0f, .633f, 1f, 18, TextAnchor.MiddleLeft);
            row.Quantity = Label(content, "Quantity", "", .635f, 0f, .814f, 1f, 16);
            row.Rarity = ShopRarityBadge.Create(content, new Vector2(.831f, .17f), new Vector2(.947f, .83f), 16);
            return row;
        }

        private static Text[] BuildPointLabels(Transform parent, float bottom, float top)
        {
            var labels = new Text[CardRarityCatalog.TierCount];
            for (int i = 0; i < labels.Length; i++)
            {
                var rarity = (CardRarity)i;
                float left = .08f + .213f * i;
                var slot = Slot(parent, CardRarityCatalog.GetLabel(rarity), left, bottom, left + .201f, top);
                LotdUiKit.AddImage(slot.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelNavy, false, false);
                labels[i] = LotdUiKit.AddFittedText(slot, "Points", "", 18, 11, TextAnchor.MiddleCenter,
                    CardRarityStyle.GetColor(rarity), FontStyle.Bold);
            }
            return labels;
        }

        private static RectTransform BuildScreen(Transform parent, string name, string title, string back, Action close)
        {
            var root = LotdUiKit.FillRect(name, parent);
            PackShopStyle.Backdrop(root);
            PackShopStyle.Header(root, title);
            AddButton(root, "Back", back, .825f, .917f, .968f, .975f, close);
            return root;
        }

        private static void Footer(Transform root, string controls)
        {
            var panel = PackShopStyle.Panel(root, "Footer", new Vector2(.035f, .017f), new Vector2(.965f, .059f));
            LotdUiKit.AddFittedText(panel, "Controls", controls, 16, 11, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);
        }

        private static string Normalize(string text) => new string(text.Where(char.IsLetterOrDigit).ToArray());

        private static int PageCount(int count) => Mathf.Max(1, (count + PageSize - 1) / PageSize);

        private static RectTransform Panel(Transform parent, string name, float left, float bottom, float right, float top) =>
            PackShopStyle.Panel(parent, name, new Vector2(left, bottom), new Vector2(right, top));

        private static RectTransform Slot(Transform parent, string name, float left, float bottom, float right, float top) =>
            LotdUiKit.CreateRect(name, parent, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Text Heading(Transform parent, string name, string value, float left, float bottom, float right, float top)
        {
            var slot = Slot(parent, name, left, bottom, right, top);
            return LotdUiKit.AddFittedText(slot, "Text", value, 23, 14, TextAnchor.MiddleCenter,
                LotdUiKit.TextPrimary, FontStyle.Bold);
        }

        private static Text Label(Transform parent, string name, string text, float left, float bottom, float right, float top,
            int size, TextAnchor align = TextAnchor.MiddleCenter) =>
            LotdUiKit.AddFittedText(Slot(parent, name, left, bottom, right, top),
                "Text", text, size, Mathf.Max(11, size - 6), align, LotdUiKit.TextPrimary);

        private static Button AddButton(Transform parent, string name, string title, float left, float bottom, float right,
            float top, Action action, bool primary = false) => PackShopStyle.Button(
            Slot(parent, name, left, bottom, right, top), "Button", title, action, 18, primary);
    }
}
