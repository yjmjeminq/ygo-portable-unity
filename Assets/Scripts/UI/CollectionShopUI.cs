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
        private enum PackFilter { All, Archetype, General }

        private sealed class CatalogItem
        {
            internal string Id;
            internal string Name;
            internal uint FeaturedCode;
            internal string Details;
            internal IReadOnlyList<uint> CardPool;
            internal CollectionStarter Starter;
            internal CollectionPack Pack;
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
            internal Button Action;
            internal Button Previous;
            internal Button Next;
            internal CompositeCardView Cover;
            internal Image PackCover;
            internal readonly Button[] Rows = new Button[PageSize];
            internal readonly Text[] Labels = new Text[PageSize];
            internal Image[] PackTiles;
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
        private PackOpeningView _openingView;
        private CardCraftingUI _crafting;
        private GameObject _craftingOrigin;
        private GameObject _craftingReturnFocus;
        private Text _balance;
        private Image _mileageFill;
        private Text _mileage;
        private Text _packGuarantee;
        private Text _filterCount;
        private Button[] _filterButtons;
        private PackFilter _packFilter;
        private CanvasGroup _shopControls;
        private GameObject _shopDialog;
        private GameObject _dialogWindow;
        private Text _dialogHeading;
        private Text _dialogText;
        private Text _dialogConfirmLabel;
        private Button _dialogCancel;
        private Button _dialogConfirm;
        private Button _dialogReturn;
        private CatalogItem _purchaseItem;
        private int _purchaseCount;
        private bool _redeemingTicket;
        private Button _ticketButton;
        private Button _cardLookupButton;
        private Button _viewContentsButton;
        private Button _oddsButton;
        private Button _buyBundleButton;
        private bool _lookupFromContents;

        public GameObject Root => _shop.Root;
        public GameObject StarterRoot => _starter.Root;
        private bool OpeningIsOpen => _openingView?.IsOpen == true;
        private bool ShopDialogIsOpen => _shopDialog.activeSelf;
        private bool CraftingIsOpen => _crafting?.IsOpen == true;
        internal GameObject ActiveShopRoot => CraftingIsOpen ? _crafting.ActiveRoot :
            OpeningIsOpen ? _openingView.ActiveRoot :
            ShopDialogIsOpen ? _shopDialog :
            _cardLookup.Root.activeSelf ? _cardLookup.Root :
            _packContents.Root.activeSelf ? _packContents.Root : Root;

        public CollectionShopUI(Transform parent, Action onBack, Action<UserDeck> onStarterClaimed)
        {
            _onBack = onBack;
            _onStarterClaimed = onStarterClaimed;
            _shop = BuildShop(parent);
            _starter = BuildStarter(parent);
            _packContents = new PackContentsUI(parent, ReturnFromContents, OpenLookupForCard, OpenCrafting);
            _cardLookup = new PackCardLookupUI(parent, ReturnFromLookup, ShowPackFromLookup, OpenCrafting);
        }

        public void OpenShop()
        {
            CloseShop();
            Root.SetActive(true);
            if (_shop.Items.Length == 0)
            {
                _shop.Items = CollectionProgress.Packs.Select(pack => new CatalogItem
                {
                    Id = pack.Id,
                    Name = pack.Name,
                    FeaturedCode = pack.FeaturedCode,
                    Details = $"수록 {pack.CardPool.Count:N0}종   ·   팩당 {CollectionProgress.CardsPerPack}장",
                    CardPool = pack.CardPool,
                    Pack = pack,
                }).ToArray();
            }
            RefreshBalance();
            SetPackFilter(_packFilter, refresh: false);
            OpenCatalog(_shop);
        }

        internal void CloseShop()
        {
            _openingView?.Hide();
            _crafting?.Hide();
            _craftingOrigin = _craftingReturnFocus = null;
            HideShopDialog();
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
                    CardPool = starter.ThemeCards,
                    Starter = starter,
                }).ToArray();
            }
            OpenCatalog(_starter);
        }

        internal InputField CatalogSearch(bool starter) => starter ? _starter.Search :
            CraftingIsOpen ? _crafting.Search :
            OpeningIsOpen || ShopDialogIsOpen ? null :
            _cardLookup.Root.activeSelf ? _cardLookup.Search :
            _packContents.Root.activeSelf ? _packContents.SearchInput : _shop.Search;

        internal void ChangeCatalogPage(bool starter, int delta)
        {
            if (!starter && CraftingIsOpen)
            {
                _crafting.ChangePage(delta);
                return;
            }
            if (!starter && OpeningIsOpen)
            {
                _openingView.ChangePage(delta);
                return;
            }
            if (!starter && ShopDialogIsOpen) return;

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
            if (OpeningIsOpen)
            {
                _openingView.Back();
                return true;
            }
            if (ShopDialogIsOpen)
            {
                CloseShopDialog();
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
            return false;
        }


        private void OpenCrafting(uint code, CardTradeAction action) => ShowCrafting(code, action, excess: false);

        private void ShowCrafting(uint code, CardTradeAction action, bool excess)
        {
            if (CraftingIsOpen || OpeningIsOpen || ShopDialogIsOpen || !CollectionProgress.HasStarter) return;
            _craftingOrigin = ActiveShopRoot;
            _craftingReturnFocus = EventSystem.current?.currentSelectedGameObject;
            _crafting ??= new CardCraftingUI(Root.transform.parent, ReturnFromCrafting, "상점으로", ShowPackFromLookup);
            _crafting.Root.transform.SetAsLastSibling();
            _craftingOrigin.SetActive(false);
            if (excess) _crafting.ShowExcessDisassembly();
            else _crafting.Show(code, action);
        }

        private void ReturnFromCrafting()
        {
            _crafting.Hide();
            _craftingOrigin.SetActive(true);
            if (_craftingOrigin == _cardLookup.Root) _cardLookup.RefreshOwnership();
            else if (_craftingOrigin == _packContents.Root) _packContents.RefreshOwnership();
            else RefreshBalance();
            if (_craftingReturnFocus != null && _craftingReturnFocus.activeInHierarchy)
                EventSystem.current?.SetSelectedGameObject(_craftingReturnFocus);
            _craftingOrigin = _craftingReturnFocus = null;
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
            RestoreShop();
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
            RestoreShop();
            EventSystem.current?.SetSelectedGameObject(_cardLookupButton.gameObject);
        }

        private void RestoreShop()
        {
            Root.SetActive(true);
            int first = _shop.Page * PageSize;
            for (int i = 0; i < PageSize && first + i < _shop.Matches.Count; i++)
                _shop.PackTiles[i].sprite = GetPackCover(_shop.Matches[first + i]);
            _shop.PackCover.sprite = _shop.Selected == null ? null : GetPackCover(_shop.Selected);
            RefreshShopActions();
        }

        internal void ShowPackFromLookup(string packId)
        {
            var item = _shop.Items.FirstOrDefault(candidate => candidate.Id == packId);
            if (item == null) return;
            CloseShop();
            Root.SetActive(true);
            int index = _shop.Matches.IndexOf(item);
            if (index < 0)
            {
                _shop.Search.SetTextWithoutNotify("");
                SetPackFilter(PackFilter.All, refresh: false);
                FilterCatalog(_shop);
                index = _shop.Matches.IndexOf(item);
            }
            if (index >= 0)
            {
                _shop.Page = index / PageSize;
                RefreshPage(_shop);
            }
            Select(_shop, item);
            EventSystem.current?.SetSelectedGameObject(_viewContentsButton.gameObject);
        }

        private Catalog BuildShop(Transform parent)
        {
            var catalog = new Catalog();
            var root = LotdUiKit.FillRect("Screen_CollectionShop", parent);
            catalog.Root = root.gameObject;
            _shopControls = root.gameObject.AddComponent<CanvasGroup>();
            PackShopStyle.Backdrop(root);
            PackShopStyle.Header(root, "상점");
            var wallet = Slot("Wallet", root, .716f, .918f, .835f, .969f);
            LotdUiKit.AddImage(wallet.gameObject, LotdUiKit.WhiteSprite, new Color32(82, 83, 86, 214), false, false);
            _balance = LotdUiKit.AddFittedText(wallet, "Balance", "", 21, 14, TextAnchor.MiddleCenter,
                PackShopStyle.TextPrimary, FontStyle.Bold);
            var mileage = Slot("Mileage", root, .037f, .922f, .283f, .969f);
            LotdUiKit.AddImage(mileage.gameObject, LotdUiKit.WhiteSprite, PackShopStyle.SurfaceInset, false, false);
            _mileageFill = LotdUiKit.AddImage(LotdUiKit.FillRect("Fill", mileage).gameObject,
                LotdUiKit.WhiteSprite, new Color(.23f, .51f, .5f, .82f), false, false);
            _mileage = LotdUiKit.AddFittedText(mileage, "Value", "", 18, 11, TextAnchor.MiddleCenter, PackShopStyle.TextPrimary);
            ShopButton(root, "Back", "메인 메뉴", .853f, .918f, .965f, .971f, _onBack);

            PackShopStyle.SetSelected(ShopButton(root, "Packs", "팩 상점", .035f, .819f, .192f, .885f,
                () => SetPackFilter(PackFilter.All), kind: PackShopStyle.ButtonKind.Category), true);
            _cardLookupButton = ShopButton(root, "CardLookup", "카드로 팩 찾기", .204f, .819f, .374f, .885f,
                OpenLookup, kind: PackShopStyle.ButtonKind.Category);
            ShopButton(root, "Craft", "카드 제작·분해", .386f, .819f, .556f, .885f,
                () => OpenCrafting(0, CardTradeAction.Browse), kind: PackShopStyle.ButtonKind.Category);
            ShopButton(root, "Excess", "3장 초과 일괄 분해", .568f, .819f, .758f, .885f,
                () => ShowCrafting(0, CardTradeAction.Browse, excess: true), kind: PackShopStyle.ButtonKind.Category);


            var rail = PackShopStyle.Panel(root, "Categories", new Vector2(.035f, .109f), new Vector2(.151f, .800f), .36f);
            string[] filterNames = { "전체 팩", "시크릿 팩", "공용 팩" };
            _filterButtons = new Button[filterNames.Length];
            for (int i = 0; i < filterNames.Length; i++)
            {
                PackFilter filter = (PackFilter)i;
                float top = .95f - i * .123f;
                _filterButtons[i] = ShopButton(rail, $"Filter_{i}", filterNames[i], .088f, top - .097f, .912f, top,
                    () => SetPackFilter(filter), kind: PackShopStyle.ButtonKind.Filter);
            }
            _filterCount = Label(rail, "Count", "", .088f, .050f, .912f, .131f, 16,
                TextAnchor.MiddleCenter, PackShopStyle.TextMuted);

            var grid = PackShopStyle.Panel(root, "Catalog", new Vector2(.16f, .109f), new Vector2(.698f, .800f), .34f);
            catalog.Search = PackShopStyle.Search(Slot("Search", grid, .027f, .870f, .973f, .974f),
                "Input", "팩 이름·카드명·카드 번호", 18);
            catalog.Search.onValueChanged.AddListener(_ => FilterCatalog(catalog));
            catalog.CountLabel = Label(grid, "Count", "", .042f, .796f, .960f, .856f, 17,
                TextAnchor.MiddleLeft, PackShopStyle.TextMuted);
            catalog.PackTiles = new Image[PageSize];
            for (int i = 0; i < PageSize; i++)
            {
                int row = i;
                float left = .027f + (i % 4) * .239f;
                float top = .785f - (i / 4) * .336f;
                var tile = Slot($"Pack_{i}", grid, left, top - .317f, left + .224f, top);
                var button = PackShopStyle.Button(tile, "Button", "", () =>
                {
                    int index = catalog.Page * PageSize + row;
                    if (index >= catalog.Matches.Count) return;
                    Select(catalog, catalog.Matches[index]);
                    EventSystem.current?.SetSelectedGameObject(_viewContentsButton.gameObject);
                }, kind: PackShopStyle.ButtonKind.Tile);
                button.gameObject.AddComponent<StarterRowFocus>().ShowPreview = () =>
                {
                    int index = catalog.Page * PageSize + row;
                    if (!ShopDialogIsOpen && index < catalog.Matches.Count && catalog.Selected != catalog.Matches[index])
                        Select(catalog, catalog.Matches[index]);
                };
                catalog.Rows[i] = button;
                var title = button.GetComponentInChildren<Text>();
                title.rectTransform.anchorMin = new Vector2(.045f, .015f);
                title.rectTransform.anchorMax = new Vector2(.955f, .232f);
                title.resizeTextMinSize = 12;
                title.resizeTextMaxSize = 17;
                title.supportRichText = false;
                catalog.Labels[i] = title;
                var art = Slot("PackCover", button.transform, .06f, .238f, .94f, .957f);
                catalog.PackTiles[i] = LotdUiKit.AddImage(art.gameObject, null, Color.white, true, false);
                ((PackShopButtonLabel)button).SetMovingArtwork(art);
            }
            catalog.Previous = ShopButton(grid, "Previous", "‹ 이전", .027f, .025f, .235f, .104f, () => ChangePage(catalog, -1));
            catalog.Next = ShopButton(grid, "Next", "다음 ›", .765f, .025f, .973f, .104f, () => ChangePage(catalog, 1));
            catalog.PageLabel = Label(grid, "Page", "", .33f, .026f, .67f, .103f, 17, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);

            var detail = PackShopStyle.Panel(root, "Details", new Vector2(.71f, .109f), new Vector2(.965f, .800f));
            catalog.Heading = Label(detail, "Heading", "", .067f, .870f, .933f, .972f, 26, TextAnchor.MiddleCenter, PackShopStyle.TextPrimary);
            var preview = Slot("Cover", detail, .113f, .47f, .887f, .866f);
            catalog.PackCover = LotdUiKit.AddImage(preview.gameObject, null, Color.white, true, false);
            catalog.Details = Label(detail, "Info", "", .073f, .395f, .927f, .463f, 16, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);
            _packGuarantee = Label(detail, "Guarantee", "", .066f, .324f, .934f, .392f, 15, TextAnchor.MiddleCenter, PackShopStyle.Accent);
            _viewContentsButton = ShopButton(detail, "ViewContents", "수록 카드", .065f, .237f, .490f, .315f, ViewSelectedPack);
            _oddsButton = ShopButton(detail, "Odds", "제공 비율", .510f, .237f, .935f, .315f, ShowPackOdds);
            catalog.Action = ShopButton(detail, "Buy", $"1팩    {CollectionProgress.PackCost:N0} DP",
                .065f, .136f, .490f, .219f, () => PromptPurchase(1), kind: PackShopStyle.ButtonKind.SinglePurchase);
            int bundle = CollectionProgress.PackBundleSize;
            _buyBundleButton = ShopButton(detail, "BuyBundle", $"{bundle}팩    {bundle * CollectionProgress.PackCost:N0} DP",
                .510f, .136f, .935f, .219f, () => PromptPurchase(bundle), kind: PackShopStyle.ButtonKind.BundlePurchase);
            _ticketButton = ShopButton(detail, "ClaimUr", "확정 UR 획득권 사용", .065f, .064f, .935f, .123f, PromptTicket);
            catalog.Status = Label(detail, "Status", "", .063f, .018f, .937f, .060f, 14, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);
            Label(root, "Controls", "방향키 이동   A 선택   Y 검색   LB/RB 페이지   B 뒤로",
                .033f, .024f, .967f, .077f, 16, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);

            BuildShopDialog(root);
            root.gameObject.SetActive(false);
            return catalog;
        }

        private Catalog BuildStarter(Transform parent)
        {
            var catalog = new Catalog();
            var root = LotdUiKit.FillRect("Screen_StarterSelection", parent);
            catalog.Root = root.gameObject;
            LotdUiKit.AddImage(LotdUiKit.FillRect("Backdrop", root).gameObject,
                LotdUiKit.LoadSprite("LOTD/Background/bg_vrains"), new Color(.52f, .63f, .79f), false, false);
            LotdUiKit.AddMenuHeader(root, "스타터 카드군 선택");
            AddButton(root, "Back", "타이틀", .825f, .917f, .968f, .975f, _onBack);

            var list = LotdUiKit.CreateMenuPanel(root, "Catalog", new Vector2(.025f, .083f), new Vector2(.405f, .882f));
            var details = LotdUiKit.CreateMenuPanel(root, "Details", new Vector2(.419f, .083f), new Vector2(.975f, .882f));
            var search = Slot("Search", list, .08f, .851f, .92f, .924f);
            catalog.Search = LotdUiKit.CreateInputField(search, "Input", "카드군·카드명·카드 번호 검색", 17);
            catalog.Search.onValueChanged.AddListener(_ => FilterCatalog(catalog));
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
                    if (catalog.Action.IsInteractable()) EventSystem.current?.SetSelectedGameObject(catalog.Action.gameObject);
                });
                catalog.Rows[i] = button;
                button.gameObject.AddComponent<StarterRowFocus>().ShowPreview = () =>
                {
                    int index = catalog.Page * PageSize + row;
                    if (index < catalog.Matches.Count && catalog.Matches[index] != catalog.Selected)
                        Select(catalog, catalog.Matches[index]);
                };
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
            catalog.Cover = CompositeCardView.Create(Slot("Cover", details, .062f, .10f, .45f, .838f), "FeaturedCard", true);
            catalog.Details = Label(details, "Info", "", .48f, .563f, .914f, .817f, 22, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary);
            catalog.Action = AddButton(details, "Choose", "이 카드군으로 시작", .48f, .466f, .923f, .547f, ChooseSelectedStarter, true);
            catalog.Status = Label(details, "Status", "", .48f, .395f, .923f, .454f, 17, TextAnchor.MiddleLeft, LotdUiKit.TextMuted);
            Label(details, "StarterInfo", $"전체 카드군 중 하나를 처음 한 번만 고릅니다.\n고른 카드군의 스타터 덱과 {CollectionProgress.StartingBalance:N0} DP를 받습니다.",
                .48f, .204f, .923f, .362f, 18, TextAnchor.UpperLeft, LotdUiKit.TextMuted);
            var footer = LotdUiKit.CreateBorderedPanel(root, "Footer", new Vector2(.035f, .017f), new Vector2(.965f, .059f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(footer, "Controls", "방향키 이동   A 선택   Y 검색   LB/RB 페이지   B 뒤로",
                16, 11, TextAnchor.MiddleCenter, LotdUiKit.TextMuted);
            root.gameObject.SetActive(false);
            return catalog;
        }

        private void BuildShopDialog(Transform parent)
        {
            var modal = LotdUiKit.FillRect("ShopDialog", parent);
            _shopDialog = modal.gameObject;
            // Keep the confirmation navigable while keyboard navigation behind it is blocked.
            _shopDialog.AddComponent<CanvasGroup>().ignoreParentGroups = true;
            LotdUiKit.AddImage(modal.gameObject, LotdUiKit.WhiteSprite, PackShopStyle.Scrim);
            var box = PackShopStyle.Panel(modal, "Window", new Vector2(.295f, .276f), new Vector2(.705f, .724f));
            _dialogWindow = box.gameObject;
            _dialogHeading = Label(box, "Heading", "", .07f, .770f, .93f, .931f, 27, TextAnchor.MiddleCenter, PackShopStyle.TextPrimary);
            _dialogText = Label(box, "Details", "", .077f, .310f, .923f, .746f, 22, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);
            _dialogCancel = ShopButton(box, "Cancel", "닫기", .090f, .090f, .475f, .239f, CloseShopDialog);
            _dialogConfirm = ShopButton(box, "Confirm", "", .525f, .090f, .910f, .239f, ConfirmShopPurchase, true);
            _dialogConfirmLabel = _dialogConfirm.GetComponentInChildren<Text>();
            modal.gameObject.SetActive(false);
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
            if (_purchaseItem == null || OpeningIsOpen || !ShopDialogIsOpen || _purchaseCount == 0) return;
            var item = _purchaseItem;
            int count = _purchaseCount;
            _openingView ??= new PackOpeningView(Root.transform.parent, ReturnFromOpening);
            if (!CollectionProgress.TryBuyPackContents(item.Id, count, out var cards, out string error))
            {
                CloseShopDialog();
                SetStatus(_shop, error, true);
                return;
            }
            HideShopDialog();
            RefreshBalance();
            SetStatus(_shop, $"{count}팩 구매 완료   ·   −{count * CollectionProgress.PackCost:N0} DP", false);
            Root.SetActive(false);
            _openingView.Show(GetPackCover(item), item.Name, cards);
        }

        private void ConfirmShopPurchase()
        {
            if (!_redeemingTicket) { BuySelectedPack(); return; }
            if (_purchaseItem == null || OpeningIsOpen || !ShopDialogIsOpen) return;
            var item = _purchaseItem;
            if (!CollectionProgress.TryRedeemPackTicket(item.Id, out var card, out string error))
            {
                CloseShopDialog();
                SetStatus(_shop, error, error: true);
                return;
            }
            HideShopDialog();
            RefreshBalance();
            _openingView ??= new PackOpeningView(Root.transform.parent, ReturnFromOpening);
            Root.SetActive(false);
            _openingView.Show(GetPackCover(item), item.Name, new IReadOnlyList<PackPull>[] { new[] { card } });
        }

        private void PromptTicket()
        {
            if (ShopDialogIsOpen || !CollectionProgress.CanRedeemPackTicket(_shop.Selected?.Pack)) return;
            _purchaseItem = _shop.Selected;
            _redeemingTicket = true;
            _dialogReturn = _ticketButton;
            _dialogHeading.text = "확정 UR 획득권 사용";
            _dialogText.text = $"{_purchaseItem.Name}\n미보유 UR 또는 새 오버프레임 UR 1장\n획득권 {CollectionProgress.PackUrTickets}장 → {CollectionProgress.PackUrTickets - 1}장";
            _dialogConfirmLabel.text = "획득권 사용";
            _dialogCancel.GetComponentInChildren<Text>().text = "취소";
            _dialogConfirm.gameObject.SetActive(true);
            ShowShopDialog();
        }

        private void PromptPurchase(int count)
        {
            if (!CollectionProgress.IsPackUnlocked(_shop.Selected?.Pack) || OpeningIsOpen || ShopDialogIsOpen) return;
            _purchaseItem = _shop.Selected;
            _purchaseCount = count;
            _dialogReturn = count == 1 ? _shop.Action : _buyBundleButton;
            int cost = count * CollectionProgress.PackCost;
            _dialogHeading.text = "팩 구매 확인";
            string guarantee = count != CollectionProgress.PackBundleSize ? "" :
                CollectionProgress.NextBundleGuaranteesUr ? "  ·  UR 1장 확정" : "  ·  SR 이상 1장 확정";
            _dialogText.text = $"{_purchaseItem.Name}   ×{count}\n카드 {count * CollectionProgress.CardsPerPack}장{guarantee}\n\n보유 DP   {CollectionProgress.Balance:N0} → {CollectionProgress.Balance - cost:N0}";
            _dialogConfirmLabel.text = $"구매   {cost:N0} DP";
            _dialogCancel.GetComponentInChildren<Text>().text = "취소";
            _dialogConfirm.gameObject.SetActive(true);
            ShowShopDialog();
        }

        private void ShowPackOdds()
        {
            _purchaseItem = null;
            _purchaseCount = 0;
            _dialogReturn = _oddsButton;
            _dialogHeading.text = "카드 제공 비율";
            string rates = string.Join("     ", Enum.GetValues(typeof(CardRarity)).Cast<CardRarity>()
                .Select(tier => $"{CardRarityCatalog.GetLabel(tier)}  {CardRarityCatalog.GetBaseWeight(tier) / 10f:0.#}%"));
            _dialogText.text = $"카드 1장당 기본 확률\n{rates}\n\n10팩에서 UR이 없으면 다음 10팩에서 UR이 확정됩니다.\n해당 등급이 팩에 없으면 공용 보너스가 나옵니다.";
            _dialogCancel.GetComponentInChildren<Text>().text = "닫기";
            _dialogConfirm.gameObject.SetActive(false);
            ShowShopDialog();
        }

        private void ShowShopDialog()
        {
            _shop.Search.DeactivateInputField();
            _shopControls.interactable = false;
            _shopDialog.SetActive(true);
            UiEntranceMotion.Play(_dialogWindow, .75f, .13f);
            EventSystem.current?.SetSelectedGameObject(_dialogCancel.gameObject);
        }

        private void CloseShopDialog()
        {
            HideShopDialog();
            EventSystem.current?.SetSelectedGameObject(_dialogReturn.gameObject);
        }

        private void HideShopDialog()
        {
            _shopDialog.SetActive(false);
            _shopControls.interactable = true;
            _purchaseItem = null;
            _purchaseCount = 0;
            _redeemingTicket = false;
        }

        private void ReturnFromOpening()
        {
            _openingView.Hide();
            Root.SetActive(true);
            var item = _shop.Selected;
            FilterCatalog(_shop);
            int index = _shop.Matches.IndexOf(item);
            if (index >= 0)
            {
                _shop.Page = index / PageSize;
                RefreshPage(_shop);
                Select(_shop, item);
            }
            RefreshShopActions();
            EventSystem.current?.SetSelectedGameObject(_dialogReturn?.IsInteractable() == true ?
                _dialogReturn.gameObject : _viewContentsButton.gameObject);
        }

        private void SetPackFilter(PackFilter filter, bool refresh = true)
        {
            _packFilter = filter;
            for (int i = 0; i < _filterButtons.Length; i++)
                PackShopStyle.SetSelected(_filterButtons[i], i == (int)filter);
            if (refresh) FilterCatalog(_shop);
        }

        private void RefreshBalance()
        {
            _balance.text = $"보유 {CollectionProgress.Balance:N0} DP";
            int progress = CollectionProgress.PackMileage;
            _mileage.text = $"천장 {progress}/{CollectionProgress.PackMileageTarget} · 확정권 {CollectionProgress.PackUrTickets}장";
            _mileageFill.rectTransform.anchorMax = new Vector2(progress / (float)CollectionProgress.PackMileageTarget, 1f);
        }

        private void OpenCatalog(Catalog catalog)
        {
            FilterCatalog(catalog);
            FocusFirstRow(catalog);
        }

        private void FilterCatalog(Catalog catalog)
        {
            var query = new CardSearchQuery(catalog.Search.text);
            IEnumerable<CatalogItem> candidates = catalog.Items;
            if (catalog == _shop)
            {
                candidates = candidates.Where(item => CollectionProgress.IsPackUnlocked(item.Pack));
                if (_packFilter != PackFilter.All)
                    candidates = candidates.Where(item => item.Pack.IsSecret == (_packFilter == PackFilter.Archetype));
            }

            catalog.Matches.Clear();
            if (query.IsEmpty)
            {
                catalog.Matches.AddRange(candidates);
            }
            else
            {
                var ranks = new Dictionary<uint, int>();
                int RankCard(uint code)
                {
                    if (!ranks.TryGetValue(code, out int rank)) ranks[code] = rank = query.Rank(code);
                    return rank;
                }
                int RankPack(CatalogItem item)
                {
                    int rank = Math.Min(query.RankName(item.Name), RankCard(item.FeaturedCode));
                    foreach (uint code in item.CardPool)
                    {
                        if (rank == 0) break;
                        rank = Math.Min(rank, RankCard(code));
                    }
                    return rank;
                }
                catalog.Matches.AddRange(candidates.Select(item => (Item: item, Rank: RankPack(item)))
                    .Where(match => match.Rank != CardSearchQuery.NoMatch)
                    .OrderBy(match => match.Rank).Select(match => match.Item));
            }
            catalog.Page = 0;
            RefreshPage(catalog);
        }

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
                if (catalog.PackTiles != null)
                    catalog.PackTiles[i].sprite = visible ? GetPackCover(catalog.Matches[index]) : null;
            }
            catalog.PageLabel.text = $"{catalog.Page + 1} / {PageCount(catalog)}";
            if (catalog.Matches.Count == 0)
                catalog.CountLabel.text = catalog == _shop && _packFilter != PackFilter.All
                    ? "이 분류에 결과 없음 · 전체 팩에서 검색" : "검색 결과 없음";
            else
                catalog.CountLabel.text = catalog == _starter
                    ? $"카드군 {catalog.Matches.Count:N0} / {catalog.Items.Length:N0}종" : $"팩 {catalog.Matches.Count:N0}개";
            if (catalog == _shop) _filterCount.text = $"전체 {catalog.Items.Length:N0}팩";
            catalog.Previous.interactable = catalog.Page > 0;
            catalog.Next.interactable = catalog.Page + 1 < PageCount(catalog);
            Select(catalog, first < catalog.Matches.Count ? catalog.Matches[first] : null);
        }

        private void Select(Catalog catalog, CatalogItem item)
        {
            bool starter = catalog == _starter;
            bool changed = catalog.Selected != item;
            catalog.Selected = item;
            catalog.Heading.text = item?.Name ?? "선택 항목 없음";
            catalog.Details.text = item?.Starter is CollectionStarter choice
                ? $"메인 덱 {choice.MainCount}장\n엑스트라 덱 {choice.ExtraCount}장"
                : item?.Details ?? "";
            if (starter)
            {
                catalog.Cover.gameObject.SetActive(item != null && item.FeaturedCode != 0);
                catalog.Cover.SetCard(item?.FeaturedCode ?? 0);
            }
            else
            {
                catalog.PackCover.gameObject.SetActive(item != null);
                catalog.PackCover.sprite = item != null ? GetPackCover(item) : null;
                if (changed && item != null) UiEntranceMotion.Play(catalog.PackCover.gameObject, .70f, .14f);
            }
            string loadError = CollectionProgress.LoadError;
            if (starter) catalog.Action.interactable = item != null && string.IsNullOrEmpty(loadError);
            else RefreshShopActions();
            SetStatus(catalog, loadError, !string.IsNullOrEmpty(loadError));
            int first = catalog.Page * PageSize;
            for (int i = 0; i < PageSize && first + i < catalog.Matches.Count; i++)
            {
                bool selected = catalog.Matches[first + i] == item;
                if (starter) catalog.Labels[i].color = selected ? LotdUiKit.GoldAccent : LotdUiKit.TextPrimary;
                else PackShopStyle.SetSelected(catalog.Rows[i], selected);
            }
        }

        private void RefreshShopActions()
        {
            bool selected = _shop.Selected != null;
            var pack = _shop.Selected?.Pack;
            bool unlocked = CollectionProgress.IsPackUnlocked(pack);
            bool canBuy = unlocked && string.IsNullOrEmpty(CollectionProgress.LoadError);
            _viewContentsButton.interactable = _oddsButton.interactable = selected;
            _shop.Action.interactable = canBuy && CollectionProgress.Balance >= CollectionProgress.PackCost;
            _buyBundleButton.interactable = canBuy && CollectionProgress.Balance >= CollectionProgress.PackCost * CollectionProgress.PackBundleSize;
            _ticketButton.interactable = canBuy && CollectionProgress.CanRedeemPackTicket(pack);
            string guarantee = CollectionProgress.NextBundleGuaranteesUr ? "다음 10팩 · UR 확정" : "10팩 · SR 이상 확정";
            _packGuarantee.text = !selected ? "" : !unlocked ? "잠긴 시크릿 팩 · 해당 카드군 SR/UR 획득으로 해금" :
                $"{guarantee}\n천장 {CollectionProgress.PackMileage}/{CollectionProgress.PackMileageTarget}";
        }

        private static Sprite GetPackCover(CatalogItem item) => PackCoverArt.Get(item.Id, item.FeaturedCode, item.CardPool);

        private void SetStatus(Catalog catalog, string message, bool error)
        {
            catalog.Status.text = message;
            if (catalog == _starter)
                catalog.Status.color = error ? LotdUiKit.RedAccent : LotdUiKit.TextMuted;
            else catalog.Status.color = error ? PackShopStyle.Danger : PackShopStyle.TextMuted;
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

        private static Button ShopButton(Transform parent, string name, string caption, float left, float bottom,
            float right, float top, Action click, bool primary = false,
            PackShopStyle.ButtonKind kind = PackShopStyle.ButtonKind.Standard) =>
            PackShopStyle.Button(Slot(name, parent, left, bottom, right, top), "Button", caption, click, 18, primary, kind);
    }

    internal static class ShopRarityBadge
    {
        internal static Text Create(Transform parent, Vector2 anchorMin, Vector2 anchorMax, int fontSize)
        {
            var holder = LotdUiKit.CreateRect("Rarity", parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(holder.gameObject, LotdUiKit.WhiteSprite, PackShopStyle.SurfaceInset, raycast: false);
            var text = LotdUiKit.AddFittedText(holder, "Text", "", fontSize, Mathf.Min(10, fontSize),
                TextAnchor.MiddleCenter, PackShopStyle.TextPrimary, FontStyle.Bold);
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = PackShopStyle.Ink;
            shadow.effectDistance = new Vector2(.8f, -.8f);
            return text;
        }

        internal static void Update(Text label, uint code)
        {
            label.transform.parent.gameObject.SetActive(code != 0);
            if (code == 0) return;
            var rarity = CardRarityCatalog.GetRarity(code);
            label.text = CardRarityCatalog.GetLabel(rarity);
            label.transform.parent.GetComponent<Image>().color = PackShopStyle.RarityColor(rarity);
        }
    }

    internal sealed class StarterRowFocus : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        internal Action ShowPreview { get; set; }
        internal Action<bool> ShowHighlight { get; set; }
        internal bool PreviewOnHover { get; set; }
        private bool _selected;
        private bool _hovered;

        public void OnSelect(BaseEventData eventData)
        {
            _selected = true;
            ShowPreview?.Invoke();
            ShowHighlight?.Invoke(true);
        }

        public void OnDeselect(BaseEventData eventData)
        {
            _selected = false;
            ShowHighlight?.Invoke(_hovered);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            if (PreviewOnHover) ShowPreview?.Invoke();
            ShowHighlight?.Invoke(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            ShowHighlight?.Invoke(_selected);
        }

        private void OnDisable()
        {
            _hovered = _selected = false;
            ShowHighlight?.Invoke(false);
        }
    }

}
