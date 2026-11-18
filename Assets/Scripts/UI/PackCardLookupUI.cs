using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class PackCardLookupUI
    {
        private const int CardsPerPage = 8;
        private const int PacksPerPage = 6;

        private sealed class Entry
        {
            public uint Code { get; }
            public string Name { get; }
            public ShopSearchKey SearchName { get; }
            public List<CollectionPack> Packs { get; } = new();

            public Entry(uint code)
            {
                Code = code;
                Name = CardDatabase.GetName(code);
                SearchName = ShopSearchKey.FromName(Name);
            }
        }

        private readonly Action<string> _onPackChosen;
        private readonly Button[] _cardRows = new Button[CardsPerPage];
        private readonly Text[] _cardLabels = new Text[CardsPerPage];
        private readonly Text[] _cardRarities = new Text[CardsPerPage];
        private readonly Button[] _packRows = new Button[PacksPerPage];
        private readonly Text[] _packLabels = new Text[PacksPerPage];
        private readonly Image[] _packCovers = new Image[PacksPerPage];
        private readonly List<Entry> _matches = new();
        private readonly Text _cardCount;
        private readonly Text _cardPageLabel;
        private readonly Text _packPageLabel;
        private readonly Text _packHeading;
        private readonly Text _cardHeading;
        private readonly Text _cardInfo;
        private readonly Text _cardRarity;
        private readonly Button _previousCard;
        private readonly Button _nextCard;
        private readonly Button _previousPack;
        private readonly Button _nextPack;
        private readonly RectTransform _packPane;
        private readonly CompositeCardView _preview;
        private IReadOnlyList<CollectionPack> _indexedPacks;
        private Entry[] _entries = Array.Empty<Entry>();
        private Entry _selected;
        private int _cardPage;
        private int _packPage;

        internal GameObject Root { get; }
        internal InputField Search { get; }

        internal PackCardLookupUI(Transform parent, Action onBack, Action<string> onPackChosen)
        {
            _onPackChosen = onPackChosen;
            var root = LotdUiKit.FillRect("Screen_PackCardLookup", parent);
            Root = root.gameObject;
            PackShopStyle.Backdrop(root);
            PackShopStyle.Header(root, "카드로 팩 찾기");
            AddButton(root, "Back", "뒤로", .825f, .917f, .968f, .975f, onBack);

            var catalog = PackShopStyle.Panel(root, "Catalog",
                new Vector2(.025f, .083f), new Vector2(.425f, .882f));
            var detail = PackShopStyle.Panel(root, "Details",
                new Vector2(.439f, .083f), new Vector2(.975f, .882f));
            var searchSlot = Slot("Search", catalog, .08f, .851f, .92f, .924f);
            Search = PackShopStyle.Search(searchSlot, "Input", "카드 이름·카드 번호 검색", 18);
            Search.onValueChanged.AddListener(_ => FilterCards());
            _cardCount = Label(catalog, "Count", .087f, .796f, .914f, .841f, 16,
                TextAnchor.MiddleLeft, PackShopStyle.TextMuted);

            for (int i = 0; i < CardsPerPage; i++)
            {
                int row = i;
                float top = .787f - i * .078f;
                var button = AddButton(catalog, $"Card_{i}", "", .08f, top - .067f, .92f, top,
                    () => ChooseCard(row));
                button.gameObject.AddComponent<StarterRowFocus>().ShowPreview = () => PreviewCard(row);
                _cardRows[i] = button;
                _cardLabels[i] = FitButtonLabel(button);
                _cardLabels[i].rectTransform.anchorMin = new Vector2(.05f, 0f);
                _cardLabels[i].rectTransform.anchorMax = new Vector2(.81f, 1f);
                _cardRarities[i] = ShopRarityBadge.Create(button.transform, new Vector2(.83f, .19f),
                    new Vector2(.945f, .81f), 16);
            }
            _previousCard = AddButton(catalog, "Previous", "이전", .08f, .074f, .28f, .14f, () => ChangeCardPage(-1));
            _nextCard = AddButton(catalog, "Next", "다음", .72f, .074f, .92f, .14f, () => ChangeCardPage(1));
            _cardPageLabel = Label(catalog, "Page", .31f, .074f, .69f, .14f, 18, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);

            var heading = Slot("Heading", detail, .073f, .853f, .927f, .929f);
            _cardHeading = LotdUiKit.AddFittedText(heading, "Title", "", 27, 16,
                TextAnchor.MiddleCenter, PackShopStyle.TextPrimary, FontStyle.Bold);
            var art = Slot("Art", detail, .06f, .25f, .45f, .834f);
            _preview = CompositeCardView.Create(art, "SelectedCard", true);
            _cardRarity = ShopRarityBadge.Create(detail, new Vector2(.198f, .188f), new Vector2(.312f, .24f), 17);
            _cardInfo = Label(detail, "CardInfo", .067f, .075f, .443f, .176f, 17,
                TextAnchor.UpperCenter, PackShopStyle.TextPrimary);

            _packPane = Slot("Packs", detail, .475f, .091f, .927f, .827f);
            _packHeading = Label(_packPane, "Count", 0f, .906f, 1f, .997f, 20,
                TextAnchor.MiddleLeft, PackShopStyle.TextPrimary);
            for (int i = 0; i < PacksPerPage; i++)
            {
                int row = i;
                float top = .893f - i * .117f;
                var button = AddButton(_packPane, $"Pack_{i}", "", 0f, top - .103f, 1f, top,
                    () => ChoosePack(row));
                _packRows[i] = button;
                _packLabels[i] = FitButtonLabel(button);
                _packLabels[i].alignment = TextAnchor.MiddleLeft;
                _packLabels[i].rectTransform.anchorMin = new Vector2(.205f, 0f);
                _packLabels[i].rectTransform.anchorMax = new Vector2(.94f, 1f);
                var cover = LotdUiKit.AddImage(Slot("Cover", button.transform, .028f, .075f, .179f, .925f).gameObject,
                    LotdUiKit.WhiteSprite, Color.white, false, false);
                cover.preserveAspect = true;
                _packCovers[i] = cover;
            }
            _previousPack = AddButton(_packPane, "Previous", "이전", 0f, .1f, .276f, .19f, () => ChangePackPage(-1));
            _nextPack = AddButton(_packPane, "Next", "다음", .724f, .1f, 1f, .19f, () => ChangePackPage(1));
            _packPageLabel = Label(_packPane, "Page", .31f, .1f, .69f, .19f, 18, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);

            var footer = PackShopStyle.Panel(root, "Footer", new Vector2(.035f, .017f), new Vector2(.965f, .059f));
            LotdUiKit.AddFittedText(footer, "Controls", "방향키 이동   A 선택   Y 검색   LB/RB 페이지   B 뒤로",
                16, 11, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);
            Root.SetActive(false);
        }

        internal void Open()
        {
            var packs = CollectionProgress.Packs;
            Root.SetActive(true);
            if (_indexedPacks != packs)
            {
                IndexPacks(packs);
                FilterCards();
            }
            else
                ShowCardPage();

            EventSystem.current?.SetSelectedGameObject(Search.gameObject);
        }

        internal void SetSearch(string query)
        {
            Search.SetTextWithoutNotify(query ?? "");
            if (Root.activeInHierarchy)
            {
                FilterCards();
                EventSystem.current?.SetSelectedGameObject(Search.gameObject);
            }
        }

        internal void ChangePage(int delta)
        {
            var focused = EventSystem.current?.currentSelectedGameObject;
            if (focused != null && focused.transform.IsChildOf(_packPane))
                ChangePackPage(delta);
            else
                ChangeCardPage(delta);
        }

        private void IndexPacks(IReadOnlyList<CollectionPack> packs)
        {
            var entries = new Dictionary<uint, Entry>();
            foreach (var pack in packs)
            {
                foreach (uint code in pack.CardPool)
                {
                    if (!entries.TryGetValue(code, out var entry))
                    {
                        entry = new Entry(code);
                        entries.Add(code, entry);
                    }
                    entry.Packs.Add(pack);
                }
            }
            _entries = entries.Values.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Code).ToArray();
            _indexedPacks = packs;
        }

        private void FilterCards()
        {
            string query = ShopSearchKey.NormalizeInput(Search.text);
            bool codeQuery = uint.TryParse(query, out uint code);
            var nameQuery = ShopSearchKey.FromName(query);
            _matches.Clear();
            foreach (var entry in _entries)
            {
                if (query.Length == 0 || (codeQuery ? entry.Code == code : entry.SearchName.Contains(nameQuery)))
                    _matches.Add(entry);
            }
            if (_matches.Count == 0 && !codeQuery && nameQuery.CanTrySimilar)
                _matches.AddRange(_entries.Where(entry => entry.SearchName.Contains(nameQuery, similar: true)));
            _cardPage = 0;
            ShowCardPage();
        }

        private void ChangeCardPage(int delta)
        {
            int page = Mathf.Clamp(_cardPage + delta, 0, PageCount(_matches.Count, CardsPerPage) - 1);
            if (page == _cardPage) return;
            _cardPage = page;
            ShowCardPage();
            EventSystem.current?.SetSelectedGameObject(_cardRows[0].gameObject);
        }

        private void ShowCardPage()
        {
            int first = _cardPage * CardsPerPage;
            for (int row = 0; row < CardsPerPage; row++)
            {
                int index = first + row;
                bool visible = index < _matches.Count;
                _cardRows[row].transform.parent.gameObject.SetActive(visible);
                if (visible)
                {
                    var entry = _matches[index];
                    _cardLabels[row].text = $"{entry.Name}  ·  {entry.Code}";
                    ShopRarityBadge.Update(_cardRarities[row], entry.Code);
                }
            }
            int pages = PageCount(_matches.Count, CardsPerPage);
            _cardCount.text = _matches.Count == 0 ? "검색 결과 없음" : $"카드 {_matches.Count:N0}종";
            _cardPageLabel.text = $"{_cardPage + 1} / {pages}";
            _previousCard.interactable = _cardPage > 0;
            _nextCard.interactable = _cardPage + 1 < pages;
            int selectedIndex = _matches.IndexOf(_selected);
            if (selectedIndex < first || selectedIndex >= first + CardsPerPage)
                selectedIndex = first;
            SetCard(selectedIndex < _matches.Count ? _matches[selectedIndex] : null);
        }

        private void PreviewCard(int row)
        {
            int index = _cardPage * CardsPerPage + row;
            if (index < _matches.Count && _selected != _matches[index])
                SetCard(_matches[index]);
        }

        private void ChooseCard(int row)
        {
            PreviewCard(row);
            if (_selected != null)
                EventSystem.current?.SetSelectedGameObject(_packRows[0].gameObject);
        }

        private void SetCard(Entry entry)
        {
            if (_selected != entry) _packPage = 0;
            _selected = entry;
            _cardHeading.text = entry?.Name ?? "선택 카드 없음";
            PackShopStyle.ShowCardPreview(_preview, entry?.Code ?? 0);
            _cardInfo.text = entry == null ? "" : $"카드 번호 {entry.Code}\n보유 {CollectionProgress.GetOwnedCount(entry.Code)}장";
            ShopRarityBadge.Update(_cardRarity, entry?.Code ?? 0);
            int first = _cardPage * CardsPerPage;
            for (int row = 0; row < CardsPerPage && first + row < _matches.Count; row++)
            {
                Color color = _matches[first + row] == entry ? PackShopStyle.Accent : PackShopStyle.TextPrimary;
                _cardRows[row].GetComponent<PackShopButtonLabel>().Normal = color;
                _cardLabels[row].color = color;
            }
            ShowPackPage();
        }

        private void ChangePackPage(int delta)
        {
            int page = Mathf.Clamp(_packPage + delta, 0, PageCount(_selected?.Packs.Count ?? 0, PacksPerPage) - 1);
            if (page == _packPage) return;
            _packPage = page;
            ShowPackPage();
            UiEntranceMotion.Play(_packPane.gameObject, .76f, .13f);
            EventSystem.current?.SetSelectedGameObject(_packRows[0].gameObject);
        }

        private void ShowPackPage()
        {
            int count = _selected?.Packs.Count ?? 0;
            int first = _packPage * PacksPerPage;
            for (int row = 0; row < PacksPerPage; row++)
            {
                int index = first + row;
                bool visible = index < count;
                _packRows[row].transform.parent.gameObject.SetActive(visible);
                if (!visible) continue;
                var pack = _selected.Packs[index];
                _packLabels[row].text = pack.Name;
                _packCovers[row].sprite = PackCoverArt.Get(pack.Id, pack.FeaturedCode, pack.CardPool);
                _packCovers[row].enabled = _packCovers[row].sprite != null;
            }
            int pages = PageCount(count, PacksPerPage);
            _packHeading.text = count == 0 ? "수록 팩" : $"수록 팩 {count:N0}개";
            _packPageLabel.text = pages > 1 ? $"{_packPage + 1} / {pages}" : "";
            _previousPack.gameObject.SetActive(pages > 1);
            _nextPack.gameObject.SetActive(pages > 1);
            _previousPack.interactable = _packPage > 0;
            _nextPack.interactable = _packPage + 1 < pages;
        }

        private void ChoosePack(int row)
        {
            int index = _packPage * PacksPerPage + row;
            if (_selected != null && index < _selected.Packs.Count)
                _onPackChosen?.Invoke(_selected.Packs[index].Id);
        }

        private static int PageCount(int count, int size) => Mathf.Max(1, (count + size - 1) / size);

        private static Text FitButtonLabel(Button button)
        {
            var label = button.GetComponentInChildren<Text>();
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 11;
            label.resizeTextMaxSize = 18;
            label.supportRichText = false;
            return label;
        }

        private static RectTransform Slot(string name, Transform parent, float left, float bottom, float right, float top) =>
            LotdUiKit.CreateRect(name, parent, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Text Label(Transform parent, string name, float left, float bottom, float right, float top,
            int size, TextAnchor alignment, Color color) =>
            LotdUiKit.AddFittedText(Slot(name, parent, left, bottom, right, top), "Text", "", size, 11, alignment, color);

        private static Button AddButton(Transform parent, string name, string label, float left, float bottom,
            float right, float top, Action action) =>
            PackShopStyle.Button(Slot(name, parent, left, bottom, right, top), "Button", label, action, 18);
    }
}
