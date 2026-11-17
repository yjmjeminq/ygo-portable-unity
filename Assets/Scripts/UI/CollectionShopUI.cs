using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public sealed class CollectionShopUI
    {
        private const int PageSize = 8;

        private sealed class CatalogItem
        {
            internal string Id;
            internal string Name;
            internal uint FeaturedCode;
            internal string Details;
            internal string SearchText;
            internal IReadOnlyList<uint> CardPool;
            internal CollectionStarter Starter;
        }

        private sealed class Catalog
        {
            internal GameObject Root;
            internal InputField Search;
            internal Text PageLabel;
            internal Text CountLabel;
            internal Text Heading;
            internal Text Details;
            internal Text Status;
            internal Text ActionLabel;
            internal Button Action;
            internal Button Previous;
            internal Button Next;
            internal CompositeCardView Cover;
            internal readonly Button[] Rows = new Button[PageSize];
            internal readonly Text[] Labels = new Text[PageSize];
            internal CatalogItem[] Items = Array.Empty<CatalogItem>();
            internal readonly List<CatalogItem> Matches = new();
            internal CatalogItem Selected;
            internal int Page;
        }

        private readonly Action _onBack;
        private readonly Action<UserDeck> _onStarterClaimed;
        private readonly Catalog _shop;
        private readonly Catalog _starter;
        private readonly PackContentsUI _packContents;
        private readonly PackCardLookupUI _cardLookup;
        private CardCraftingUI _crafting;
        private readonly List<CompositeCardView> _pullViews = new();
        private readonly List<Text> _pullLabels = new();
        private readonly List<Text> _pullRarities = new();
        private Text _balance;
        private GameObject _reveal;
        private Text _revealHeading;
        private ShopCardRevealMotion _revealMotion;
        private Button _cardLookupButton;
        private Button _viewContentsButton;
        private Button _craftingButton;
        private bool _lookupFromContents;

        public GameObject Root => _shop.Root;
        public GameObject StarterRoot => _starter.Root;
        private bool CraftingIsOpen => _crafting?.IsOpen == true;
        internal GameObject ActiveShopRoot => CraftingIsOpen ? _crafting.ActiveRoot :
            _cardLookup.Root.activeSelf ? _cardLookup.Root :
            _packContents.Root.activeSelf ? _packContents.Root : Root;

        public CollectionShopUI(Transform parent, Action onBack, Action<UserDeck> onStarterClaimed)
        {
            _onBack = onBack;
            _onStarterClaimed = onStarterClaimed;
            _shop = BuildCatalog(parent, starter: false, BuySelectedPack);
            _starter = BuildCatalog(parent, starter: true, ChooseSelectedStarter);
            _packContents = new PackContentsUI(parent, ReturnFromContents, OpenLookupForCard);
            _cardLookup = new PackCardLookupUI(parent, ReturnFromLookup, ShowPackFromLookup);
        }

        public void OpenShop()
        {
            CloseShop();
            Root.SetActive(true);
            DismissReveal();
            if (_shop.Items.Length == 0)
            {
                _shop.Items = CollectionProgress.Packs.Select(pack => new CatalogItem
                {
                    Id = pack.Id,
                    Name = pack.Name,
                    FeaturedCode = pack.FeaturedCode,
                    Details = $"수록 카드 {pack.CardPool.Count:N0}종\n팩마다 카드 {CollectionProgress.CardsPerPack}장",
                    SearchText = string.Join("\n", pack.CardPool.Select(CardDatabase.GetName)),
                    CardPool = pack.CardPool,
                }).ToArray();
            }
            RefreshBalance();
            OpenCatalog(_shop);
        }

        internal void CloseShop()
        {
            _crafting?.Hide();
            _cardLookup.Root.SetActive(false);
            _packContents.Close();
            Root.SetActive(false);
            _lookupFromContents = false;
        }

        public void OpenStarter()
        {
            StarterRoot.SetActive(true);
            if (_starter.Items.Length == 0)
            {
                _starter.Items = CollectionProgress.Starters.Select(starter => new CatalogItem
                {
                    Id = starter.Id,
                    Name = starter.Name,
                    FeaturedCode = starter.FeaturedCode,
                    SearchText = string.Join("\n", starter.ThemeCards.Select(CardDatabase.GetName)
                        .Prepend(CardDatabase.GetName(starter.FeaturedCode))
                        .Prepend(starter.Name)
                        .Select(NormalizeStarterSearch)),
                    CardPool = starter.ThemeCards,
                    Starter = starter,
                }).ToArray();
            }
            OpenCatalog(_starter);
        }

        public bool DismissReveal()
        {
            if (!_reveal.activeSelf) return false;
            _reveal.SetActive(false);
            if (_shop.Action.gameObject.activeInHierarchy && _shop.Action.IsInteractable())
                EventSystem.current?.SetSelectedGameObject(_shop.Action.gameObject);
            return true;
        }

        internal InputField CatalogSearch(bool starter) => starter ? _starter.Search :
            CraftingIsOpen ? _crafting.Search :
            _cardLookup.Root.activeSelf ? _cardLookup.Search :
            _packContents.Root.activeSelf ? _packContents.SearchInput : _shop.Search;

        internal void ChangeCatalogPage(bool starter, int delta)
        {
            if (!starter && CraftingIsOpen)
            {
                _crafting.ChangePage(delta);
                return;
            }
            if (!starter && _cardLookup.Root.activeSelf)
            {
                _cardLookup.ChangePage(delta);
                return;
            }
            if (!starter && _packContents.Root.activeSelf)
            {
                _packContents.ChangePage(delta);
                return;
            }
            var catalog = starter ? _starter : _shop;
            if (ChangePage(catalog, delta)) FocusFirstRow(catalog);
        }

        internal void ScrollShopDescription(float direction)
        {
            if (CraftingIsOpen) _crafting.ScrollDescription(direction);
            else if (_packContents.Root.activeSelf) _packContents.ScrollDescription(direction);
        }

        internal bool HandleShopBack()
        {
            if (CraftingIsOpen)
            {
                _crafting.Back();
                return true;
            }
            if (_cardLookup.Root.activeSelf)
            {
                ReturnFromLookup();
                return true;
            }
            if (_packContents.Root.activeSelf)
            {
                ReturnFromContents();
                return true;
            }
            return DismissReveal();
        }

        private void OpenCrafting()
        {
            _crafting ??= new CardCraftingUI(Root.transform.parent, ReturnFromCrafting);
            DismissReveal();
            Root.SetActive(false);
            _crafting.Show();
        }

        private void ReturnFromCrafting()
        {
            _crafting.Hide();
            Root.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(_craftingButton.gameObject);
        }

        private void ViewSelectedPack()
        {
            var pack = CollectionProgress.Packs.FirstOrDefault(item => item.Id == _shop.Selected?.Id);
            if (pack == null) return;
            Root.SetActive(false);
            _packContents.Open(pack);
        }

        private void ReturnFromContents()
        {
            _packContents.Close();
            Root.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(_viewContentsButton.gameObject);
        }

        private void OpenLookup()
        {
            _lookupFromContents = false;
            Root.SetActive(false);
            _cardLookup.Open();
        }

        private void OpenLookupForCard(uint code)
        {
            _lookupFromContents = true;
            _packContents.Root.SetActive(false);
            _cardLookup.Open();
            _cardLookup.SetSearch(code.ToString());
        }

        private void ReturnFromLookup()
        {
            _cardLookup.Root.SetActive(false);
            if (_lookupFromContents)
            {
                _lookupFromContents = false;
                _packContents.Resume();
                return;
            }
            Root.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(_cardLookupButton.gameObject);
        }

        private void ShowPackFromLookup(string packId)
        {
            var item = _shop.Items.FirstOrDefault(candidate => candidate.Id == packId);
            if (item == null) return;
            CloseShop();
            Root.SetActive(true);
            DismissReveal();
            int index = _shop.Matches.IndexOf(item);
            if (index < 0)
            {
                _shop.Search.SetTextWithoutNotify("");
                FilterCatalog(_shop);
                index = _shop.Matches.IndexOf(item);
            }
            _shop.Page = index / PageSize;
            RefreshPage(_shop);
            Select(_shop, item);
            EventSystem.current?.SetSelectedGameObject(_viewContentsButton.gameObject);
        }

        private Catalog BuildCatalog(Transform parent, bool starter, Action onAction)
        {
            var catalog = new Catalog();
            var root = LotdUiKit.FillRect(starter ? "Screen_StarterSelection" : "Screen_CollectionShop", parent);
            catalog.Root = root.gameObject;
            LotdUiKit.AddImage(LotdUiKit.FillRect("Backdrop", root).gameObject,
                LotdUiKit.LoadSprite("LOTD/Background/bg_vrains"), new Color(.52f, .63f, .79f), false, false);
            LotdUiKit.AddMenuHeader(root, starter ? "스타터 카드군 선택" : "카드 팩 상점");

            if (!starter)
            {
                var wallet = LotdUiKit.CreateBorderedPanel(root, "Wallet", new Vector2(.035f, .917f),
                    new Vector2(.222f, .976f), Vector2.zero, Vector2.zero);
                _balance = LotdUiKit.AddFittedText(wallet, "Balance", "", 22, 14, TextAnchor.MiddleCenter,
                    LotdUiKit.GoldAccent, FontStyle.Bold);
                _craftingButton = AddButton(root, "Crafting", "제작·분해", .759f, .917f, .865f, .975f, OpenCrafting);
            }
            AddButton(root, "Back", starter ? "타이틀" : "메인 메뉴", starter ? .825f : .872f, .917f, .968f, .975f, _onBack);

            var list = LotdUiKit.CreateMenuPanel(root, "Catalog", new Vector2(.025f, .083f), new Vector2(.405f, .882f));
            var details = LotdUiKit.CreateMenuPanel(root, "Details", new Vector2(.419f, .083f), new Vector2(.975f, .882f));
            var search = Slot("Search", list, .08f, .851f, starter ? .92f : .632f, .924f);
            catalog.Search = LotdUiKit.CreateInputField(search, "Input", starter ? "카드군·카드명·카드 번호 검색" : "팩 이름·카드명·카드 번호 검색", 17);
            catalog.Search.onValueChanged.AddListener(_ => FilterCatalog(catalog));
            if (!starter)
                _cardLookupButton = AddButton(list, "CardLookup", "카드로 팩 찾기", .651f, .851f, .92f, .924f, OpenLookup);
            catalog.CountLabel = Label(list, "Count", "", .087f, .796f, .914f, .838f, 16,
                TextAnchor.MiddleLeft, LotdUiKit.TextMuted);
            for (int i = 0; i < PageSize; i++)
            {
                int row = i;
                float top = .786f - i * .078f;
                var button = AddButton(list, $"Pack_{i}", "", .08f, top - .067f, .92f, top, () =>
                {
                    int index = catalog.Page * PageSize + row;
                    if (index >= catalog.Matches.Count) return;
                    Select(catalog, catalog.Matches[index]);
                    var next = starter ? catalog.Action : _viewContentsButton;
                    if (next.IsInteractable())
                        EventSystem.current?.SetSelectedGameObject(next.gameObject);
                });
                catalog.Rows[i] = button;
                if (starter)
                {
                    button.gameObject.AddComponent<StarterRowFocus>().ShowPreview = () =>
                    {
                        int index = catalog.Page * PageSize + row;
                        if (index < catalog.Matches.Count && catalog.Matches[index] != catalog.Selected)
                            Select(catalog, catalog.Matches[index]);
                    };
                }
                var label = button.GetComponentInChildren<Text>();
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 12;
                label.resizeTextMaxSize = 18;
                label.supportRichText = false;
                catalog.Labels[i] = label;
            }
            catalog.Previous = AddButton(list, "Previous", "이전", .08f, .073f, .28f, .139f, () => ChangePage(catalog, -1));
            catalog.Next = AddButton(list, "Next", "다음", .72f, .073f, .92f, .139f, () => ChangePage(catalog, 1));
            catalog.PageLabel = Label(list, "Page", "", .31f, .073f, .69f, .139f, 18, TextAnchor.MiddleCenter, LotdUiKit.TextMuted);

            var heading = Slot("Heading", details, .075f, .853f, .925f, .928f);
            LotdUiKit.AddImage(heading.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelHeader, false, false);
            catalog.Heading = LotdUiKit.AddFittedText(heading, "Title", "", 28, 17, TextAnchor.MiddleCenter,
                LotdUiKit.GoldAccent, FontStyle.Bold);
            var cover = Slot("Cover", details, .08f, starter ? .194f : .411f, starter ? .42f : .38f, .835f);
            catalog.Cover = CompositeCardView.Create(cover, "FeaturedCard", true);
            catalog.Details = Label(details, "Info", "", starter ? .48f : .429f, starter ? .563f : .704f, .914f, .817f, 22,
                TextAnchor.MiddleLeft, LotdUiKit.TextPrimary);
            if (!starter)
                _viewContentsButton = AddButton(details, "ViewContents", "수록 카드", .429f, .466f, .659f, .547f, ViewSelectedPack);
            catalog.Action = AddButton(details, starter ? "Choose" : "Buy", "", starter ? .48f : .674f, .466f,
                .923f, .547f, onAction, true);
            catalog.ActionLabel = catalog.Action.GetComponentInChildren<Text>();
            catalog.Status = Label(details, "Status", "", starter ? .48f : .429f, .395f, .923f, .454f, 17,
                TextAnchor.MiddleLeft, LotdUiKit.TextMuted);

            if (starter)
                Label(details, "StarterInfo", $"전체 카드군 중 하나를 처음 한 번만 고릅니다.\n고른 카드군의 스타터 덱과 {CollectionProgress.StartingBalance:N0} DP를 받습니다.",
                    .48f, .204f, .923f, .362f, 18, TextAnchor.UpperLeft, LotdUiKit.TextMuted);
            else
            {
                string odds = string.Join("  ·  ", Enum.GetValues(typeof(CardRarity)).Cast<CardRarity>()
                    .Select(rarity => $"{CardRarityCatalog.GetLabel(rarity)} {CardRarityCatalog.GetBaseWeight(rarity) / 10f:0.#}%"));
                Label(details, "RarityOdds", $"카드 1장당 기본 확률\n{odds}\n팩에 남은 등급에 따라 확률이 조정됩니다.",
                    .429f, .558f, .923f, .702f, 17, TextAnchor.MiddleLeft, LotdUiKit.TextMuted);
                BuildReveal(details);
            }

            var footer = LotdUiKit.CreateBorderedPanel(root, "Footer", new Vector2(.035f, .017f), new Vector2(.965f, .059f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(footer, "Controls", "방향키 이동   A 선택   Y 검색   LB/RB 페이지   B 뒤로",
                16, 11, TextAnchor.MiddleCenter, LotdUiKit.TextMuted);
            root.gameObject.SetActive(false);
            return catalog;
        }

        private void BuildReveal(Transform details)
        {
            var area = LotdUiKit.CreateBorderedPanel(details, "PackContents", new Vector2(.074f, .057f),
                new Vector2(.927f, .382f), Vector2.zero, Vector2.zero, LotdUiKit.PanelNavy);
            _reveal = area.gameObject;
            _revealHeading = Label(area, "Heading", "", .026f, .764f, .772f, .951f, 20, TextAnchor.MiddleLeft, LotdUiKit.GoldAccent);
            AddButton(area, "Close", "닫기", .795f, .771f, .973f, .941f, () => DismissReveal());
            var cardGroups = new CanvasGroup[CollectionProgress.CardsPerPack];
            for (int i = 0; i < CollectionProgress.CardsPerPack; i++)
            {
                float stride = .956f / CollectionProgress.CardsPerPack;
                float left = .022f + stride * i;
                var item = Slot($"Card_{i}", area, left, .077f, left + stride - .005f, .748f);
                cardGroups[i] = item.gameObject.AddComponent<CanvasGroup>();
                _pullViews.Add(CompositeCardView.Create(Slot("Art", item, .015f, .365f, .985f, 1f), "Card", true));
                _pullRarities.Add(ShopRarityBadge.Create(item, new Vector2(.25f, .241f), new Vector2(.75f, .365f), 14));
                _pullLabels.Add(Label(item, "Name", "", 0f, 0f, 1f, .235f, 13, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary));
            }
            _revealMotion = _reveal.AddComponent<ShopCardRevealMotion>();
            _revealMotion.SetCards(cardGroups);
            _reveal.SetActive(false);
        }

        private void ChooseSelectedStarter()
        {
            if (_starter.Selected == null) return;
            if (CollectionProgress.TryChooseStarter(_starter.Selected.Id, out var deck, out string error))
            {
                _onStarterClaimed?.Invoke(deck);
                return;
            }
            SetStatus(_starter, error, true);
        }

        private void BuySelectedPack()
        {
            if (_shop.Selected == null) return;
            if (!CollectionProgress.TryBuyPack(_shop.Selected.Id, out var cards, out string error))
            {
                SetStatus(_shop, error, true);
                return;
            }

            RefreshBalance();
            _revealHeading.text = $"{_shop.Selected.Name} · 카드 {cards.Count}장";
            _reveal.SetActive(true);
            for (int i = 0; i < _pullViews.Count; i++)
            {
                uint code = i < cards.Count ? cards[i] : 0;
                _pullViews[i].transform.parent.parent.gameObject.SetActive(code != 0);
                _pullViews[i].SetCard(code);
                ShopRarityBadge.Update(_pullRarities[i], code);
                _pullLabels[i].text = code == 0 ? "" : $"{CardDatabase.GetName(code)}\n보유 {CollectionProgress.GetOwnedCount(code)}장";
            }
            _revealMotion.Play();
            SetStatus(_shop, $"팩을 열었습니다. −{CollectionProgress.PackCost} DP", false);
        }

        private void RefreshBalance() => _balance.text = $"보유 {CollectionProgress.Balance:N0} DP";

        private void OpenCatalog(Catalog catalog)
        {
            FilterCatalog(catalog);
            FocusFirstRow(catalog);
        }

        private void FilterCatalog(Catalog catalog)
        {
            string query = catalog.Search.text.Trim();
            string searchText = catalog == _starter ? NormalizeStarterSearch(query) : query;
            bool codeQuery = uint.TryParse(query, out uint code);
            catalog.Matches.Clear();
            foreach (var item in catalog.Items)
            {
                if (query.Length == 0 || item.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (codeQuery ? item.FeaturedCode == code || item.CardPool?.Contains(code) == true :
                        searchText.Length > 0 && item.SearchText?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0))
                    catalog.Matches.Add(item);
            }
            catalog.Page = 0;
            RefreshPage(catalog);
        }

        private static string NormalizeStarterSearch(string value) =>
            new string(value.Where(char.IsLetterOrDigit).ToArray());

        private bool ChangePage(Catalog catalog, int delta)
        {
            int page = Mathf.Clamp(catalog.Page + delta, 0, PageCount(catalog) - 1);
            if (page == catalog.Page) return false;
            catalog.Page = page;
            RefreshPage(catalog);
            return true;
        }

        private void RefreshPage(Catalog catalog)
        {
            int first = catalog.Page * PageSize;
            for (int i = 0; i < PageSize; i++)
            {
                int index = first + i;
                bool visible = index < catalog.Matches.Count;
                catalog.Rows[i].transform.parent.gameObject.SetActive(visible);
                if (visible) catalog.Labels[i].text = catalog.Matches[index].Name;
            }
            catalog.PageLabel.text = $"{catalog.Page + 1} / {PageCount(catalog)}";
            catalog.CountLabel.text = catalog.Matches.Count == 0 ? "검색 결과 없음" :
                catalog == _starter ? $"카드군 {catalog.Matches.Count:N0} / {catalog.Items.Length:N0}종" : $"{catalog.Matches.Count:N0}개";
            catalog.Previous.interactable = catalog.Page > 0;
            catalog.Next.interactable = catalog.Page + 1 < PageCount(catalog);
            Select(catalog, first < catalog.Matches.Count ? catalog.Matches[first] : null);
        }

        private void Select(Catalog catalog, CatalogItem item)
        {
            bool starter = catalog == _starter;
            catalog.Selected = item;
            catalog.Heading.text = item?.Name ?? "선택 항목 없음";
            catalog.Details.text = item?.Starter is CollectionStarter choice
                ? $"메인 덱 {choice.MainCount}장\n엑스트라 덱 {choice.ExtraCount}장"
                : item?.Details ?? "";
            catalog.Cover.gameObject.SetActive(item != null && item.FeaturedCode != 0);
            catalog.Cover.SetCard(item?.FeaturedCode ?? 0);
            catalog.ActionLabel.text = starter ? "이 카드군으로 시작" : $"팩 열기   {CollectionProgress.PackCost:N0} DP";
            string loadError = CollectionProgress.LoadError;
            catalog.Action.interactable = item != null && string.IsNullOrEmpty(loadError);
            if (!starter) _viewContentsButton.interactable = item != null;
            SetStatus(catalog, loadError, !string.IsNullOrEmpty(loadError));
            int first = catalog.Page * PageSize;
            for (int i = 0; i < PageSize && first + i < catalog.Matches.Count; i++)
            {
                bool selected = catalog.Matches[first + i] == item;
                catalog.Labels[i].color = selected ? LotdUiKit.GoldAccent : LotdUiKit.TextPrimary;
            }
        }

        private static void SetStatus(Catalog catalog, string message, bool error)
        {
            catalog.Status.text = message;
            catalog.Status.color = error ? LotdUiKit.RedAccent : LotdUiKit.TextMuted;
        }

        private static void FocusFirstRow(Catalog catalog) =>
            EventSystem.current?.SetSelectedGameObject(catalog.Matches.Count > 0
                ? catalog.Rows[0].gameObject : catalog.Search.gameObject);

        private static int PageCount(Catalog catalog) => Mathf.Max(1, (catalog.Matches.Count + PageSize - 1) / PageSize);

        private static RectTransform Slot(string name, Transform parent, float left, float bottom, float right, float top) =>
            LotdUiKit.CreateRect(name, parent, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Text Label(Transform parent, string name, string text, float left, float bottom, float right, float top,
            int size, TextAnchor alignment, Color color) =>
            LotdUiKit.AddFittedText(Slot(name, parent, left, bottom, right, top), "Text", text, size, Mathf.Max(11, size - 7),
                alignment, color);

        private static Button AddButton(Transform parent, string name, string caption, float left, float bottom,
            float right, float top, Action click, bool gold = false) =>
            LotdUiKit.CreateLotdButton(Slot(name, parent, left, bottom, right, top), "Button", caption, click, 18, gold);
    }

    internal static class ShopRarityBadge
    {
        internal static Text Create(Transform parent, Vector2 anchorMin, Vector2 anchorMax, int fontSize)
        {
            var holder = LotdUiKit.CreateRect("Rarity", parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(holder.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelNavy, raycast: false);
            return LotdUiKit.AddFittedText(holder, "Text", "", fontSize, Mathf.Min(10, fontSize),
                TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);
        }

        internal static void Update(Text label, uint code)
        {
            label.transform.parent.gameObject.SetActive(code != 0);
            if (code == 0) return;
            var rarity = CardRarityCatalog.GetRarity(code);
            label.text = CardRarityCatalog.GetLabel(rarity);
            label.color = CardRarityStyle.GetColor(rarity);
        }
    }

    internal sealed class StarterRowFocus : MonoBehaviour, ISelectHandler
    {
        internal Action ShowPreview { get; set; }

        public void OnSelect(BaseEventData eventData) => ShowPreview?.Invoke();
    }

    internal sealed class ShopCardRevealMotion : MonoBehaviour
    {
        private const float CardDuration = .18f;
        private const float Stagger = .052f;
        private CanvasGroup[] _cards = Array.Empty<CanvasGroup>();
        private float _start;

        internal void SetCards(CanvasGroup[] cards)
        {
            _cards = cards;
            enabled = false;
        }

        internal void Play()
        {
            if (!Application.isPlaying) return;
            _start = Time.unscaledTime;
            Render(0f);
            enabled = true;
        }

        private void Update()
        {
            float elapsed = Time.unscaledTime - _start;
            Render(elapsed);
            if (elapsed >= CardDuration + Stagger * (_cards.Length - 1))
                enabled = false;
        }

        private void OnDisable() => Render(CardDuration + Stagger * _cards.Length);

        private void Render(float elapsed)
        {
            for (int i = 0; i < _cards.Length; i++)
            {
                float progress = Mathf.Clamp01((elapsed - i * Stagger) / CardDuration);
                float ease = Mathf.SmoothStep(0f, 1f, progress);
                _cards[i].alpha = ease;
                _cards[i].transform.localScale = Vector3.one * Mathf.Lerp(.87f, 1f, ease);
            }
        }
    }
}
