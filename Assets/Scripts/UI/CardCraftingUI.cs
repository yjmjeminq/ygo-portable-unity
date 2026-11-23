using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal enum CardTradeAction { Browse, Craft, CraftOverframe, Disassemble, DisassembleOverframe }

    internal sealed class CardCraftingUI
    {
        private const int PageSize = 7;
        private static readonly CardRarity[] Rarities =
        {
            CardRarity.Normal, CardRarity.Rare, CardRarity.SuperRare, CardRarity.UltraRare,
        };

        private sealed class Entry
        {
            internal readonly CardInfo Card;
            internal readonly string Name;
            internal readonly CardRarity Rarity;

            internal Entry(CardInfo card)
            {
                Card = card;
                Name = CardDatabase.GetName(card.code);
                Rarity = CardRarityCatalog.GetRarity(card.code);
            }
        }

        private readonly Action _onClose;
        private readonly Action<string> _openSecretPack;
        private readonly GameObject _main;
        private readonly CanvasGroup _mainControls;
        private readonly InputField _finder;
        private readonly Button _bulkButton;
        private readonly CollectionCardFilters _collectionFilters;
        private readonly CardBulkDisassemblyUI _bulk;
        private readonly Button[] _rows = new Button[PageSize];
        private readonly Text[] _rowNames = new Text[PageSize];
        private readonly Text[] _rowRarities = new Text[PageSize];
        private readonly Text[] _filterLabels = new Text[Rarities.Length + 1];
        private readonly Text _balance;
        private readonly Text _count;
        private readonly Text _pageLabel;
        private readonly Button _previous;
        private readonly Button _next;
        private readonly Text _title;
        private readonly Text _cardInfo;
        private readonly Text _cardStats;
        private readonly Text _rarity;
        private readonly CompositeCardView _art;
        private readonly Button _editionPreview;
        private readonly ScrollRect _descriptionScroll;
        private readonly Text _description;
        private readonly Button _craft;
        private readonly Button _disassemble;
        private readonly Button _craftOverframe;
        private readonly Button _disassembleOverframe;
        private readonly Button _secretPack;
        private readonly Text _prices;
        private readonly Text _status;
        private readonly GameObject _confirmation;
        private readonly GameObject _confirmationWindow;
        private readonly Text _confirmTitle;
        private readonly Text _confirmBody;
        private readonly Text _quantityLabel;
        private readonly Button _minus;
        private readonly Button _plus;
        private readonly Button _maximum;
        private readonly Button _cancel;
        private readonly Button _confirm;
        private readonly List<Entry> _matches = new();
        private Entry[] _entries;
        private Entry _selected;
        private Entry _confirmedCard;
        private int _page;
        private int _filter;
        private int _quantity;
        private int _maxQuantity;
        private bool _crafting;
        private bool _overframe;
        private bool _previewOverframe;

        internal GameObject Root { get; }
        internal bool IsOpen => Root.activeSelf;
        internal GameObject ActiveRoot => _bulk.IsOpen ? _bulk.ActiveRoot :
            _confirmation.activeSelf ? _confirmation : _main;
        internal InputField Search => _bulk.IsOpen ? _bulk.Search :
            _confirmation.activeSelf ? null : _finder;

        internal CardCraftingUI(Transform parent, Action close, string backLabel = "덱 편집으로", Action<string> openSecretPack = null)
        {
            _onClose = close;
            _openSecretPack = openSecretPack;
            var root = LotdUiKit.FillRect("Screen_CardCrafting", parent);
            Root = root.gameObject;
            var main = LotdUiKit.FillRect("Main", root);
            _main = main.gameObject;
            _mainControls = _main.AddComponent<CanvasGroup>();
            PackShopStyle.Backdrop(main);
            PackShopStyle.Header(main, "카드 제작·분해");
            _bulkButton = AddButton(main, "Bulk", "일괄 분해", .655f, .917f, .812f, .975f, OpenBulk);
            AddButton(main, "Back", backLabel, .825f, .917f, .968f, .975f, Back);

            var catalog = PackShopStyle.Panel(main, "Catalog", new Vector2(.025f, .083f), new Vector2(.425f, .882f));
            _finder = PackShopStyle.Search(Slot(catalog, "Search", .08f, .849f, .92f, .925f),
                "Input", "카드 이름·카드 번호 검색", 18);
            for (int i = 0; i < _filterLabels.Length; i++)
            {
                int filter = i;
                float left = .08f + i * .17f;
                string label = i == 0 ? "전체" : CardRarityCatalog.GetLabel(Rarities[i - 1]);
                var button = AddButton(catalog, $"Filter_{i}", label, left, .769f, left + .16f, .831f,
                    () => ChangeFilter(filter));
                _filterLabels[i] = button.GetComponentInChildren<Text>();
            }
            _collectionFilters = new CollectionCardFilters(Slot(catalog, "CollectionFilters", .08f, .700f, .92f, .759f),
                () => Filter(), includeRarity: false);
            _count = Label(catalog, "Count", "", .087f, .655f, .915f, .695f, 16, TextAnchor.MiddleLeft, PackShopStyle.TextMuted);

            for (int i = 0; i < PageSize; i++)
            {
                int row = i;
                float top = .650f - i * .071f;
                var button = AddButton(catalog, $"Card_{i}", "", .08f, top - .063f, .92f, top, () => SelectRow(row));
                button.gameObject.AddComponent<StarterRowFocus>().ShowPreview = () => SelectRow(row);
                _rows[i] = button;
                _rowNames[i] = button.GetComponentInChildren<Text>();
                _rowNames[i].resizeTextForBestFit = true;
                _rowNames[i].resizeTextMinSize = 11;
                _rowNames[i].resizeTextMaxSize = 18;
                _rowNames[i].supportRichText = false;
                _rowNames[i].rectTransform.anchorMin = new Vector2(.055f, 0f);
                _rowNames[i].rectTransform.anchorMax = new Vector2(.806f, 1f);
                _rowRarities[i] = ShopRarityBadge.Create(button.transform, new Vector2(.83f, .19f), new Vector2(.945f, .81f), 16);
            }
            _previous = AddButton(catalog, "Previous", "이전", .08f, .074f, .28f, .14f, () => ChangePage(-1));
            _pageLabel = Label(catalog, "Page", "", .31f, .074f, .69f, .14f, 18, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);
            _next = AddButton(catalog, "Next", "다음", .72f, .074f, .92f, .14f, () => ChangePage(1));

            var details = PackShopStyle.Panel(main, "Details", new Vector2(.439f, .083f), new Vector2(.975f, .882f));
            var balance = Slot(details, "Balance", .075f, .867f, .926f, .933f);
            LotdUiKit.AddImage(balance.gameObject, LotdUiKit.WhiteSprite, PackShopStyle.SurfaceInset, false, false);
            _balance = LotdUiKit.AddFittedText(balance, "Text", "", 20, 12,
                TextAnchor.MiddleCenter, PackShopStyle.Accent, FontStyle.Bold);
            var heading = Slot(details, "Name", .075f, .768f, .926f, .851f);
            _title = LotdUiKit.AddFittedText(heading, "Text", "", 26, 16,
                TextAnchor.MiddleCenter, PackShopStyle.TextPrimary, FontStyle.Bold);
            var art = Slot(details, "Art", .061f, .090f, .463f, .718f);
            _art = CompositeCardView.Create(art, "SelectedCard", true);
            CardOverframeInput.Attach(art, _art, motionSensor: true);
            _editionPreview = AddButton(details, "EditionPreview", "오버프레임 보기", .087f, .029f, .435f, .082f,
                () => SetEditionPreview(!_previewOverframe));
            _rarity = ShopRarityBadge.Create(details, new Vector2(.491f, .679f), new Vector2(.572f, .738f), 17);
            _cardInfo = Label(details, "Ownership", "", .583f, .669f, .925f, .749f, 17,
                TextAnchor.MiddleLeft, PackShopStyle.Accent);
            _cardStats = Label(details, "Stats", "", .491f, .591f, .925f, .668f, 16, TextAnchor.MiddleLeft, PackShopStyle.TextPrimary);
            (_descriptionScroll, _description) = CreateDescription(details);
            _craft = AddButton(details, "Craft", "제작", .491f, .220f, .701f, .288f, () => OpenConfirmation(true), true);
            _disassemble = AddButton(details, "Disassemble", "분해", .715f, .220f, .926f, .288f, () => OpenConfirmation(false));
            _craftOverframe = AddButton(details, "CraftOverframe", "오버프레임 제작", .491f, .147f, .701f, .213f,
                () => OpenConfirmation(true, overframe: true), true);
            _disassembleOverframe = AddButton(details, "DisassembleOverframe", "오버프레임 분해", .715f, .147f, .926f, .213f,
                () => OpenConfirmation(false, overframe: true));
            _status = Label(details, "Status", "", .491f, .082f, .928f, .142f, 17, TextAnchor.MiddleCenter, PackShopStyle.TextPrimary);
            string prices = string.Join(" · ", Rarities.Select(rarity =>
                $"{CardRarityCatalog.GetLabel(rarity)} {CollectionProgress.GetCraftDustCost(rarity)}"));
            _prices = Label(details, "Rules", $"제작에 드는 가루   {prices}",
                .491f, .035f, .928f, .078f, 15, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);
            _secretPack = AddButton(details, "SecretPack", "해금된 시크릿 팩 열기", .491f, .032f, .928f, .079f, OpenSecretPack);

            var footer = PackShopStyle.Panel(main, "Footer", new Vector2(.035f, .017f), new Vector2(.965f, .059f));
            LotdUiKit.AddFittedText(footer, "Controls", "방향키 이동   A 선택   Y 검색   LB/RB 페이지   오른쪽 스틱 설명   B 뒤로",
                16, 11, TextAnchor.MiddleCenter, PackShopStyle.TextMuted);

            var modal = LotdUiKit.FillRect("Confirmation", root);
            _confirmation = modal.gameObject;
            LotdUiKit.AddImage(modal.gameObject, LotdUiKit.WhiteSprite, PackShopStyle.Scrim);
            var window = PackShopStyle.Panel(modal, "Window", new Vector2(.29f, .282f), new Vector2(.71f, .718f));
            _confirmationWindow = window.gameObject;
            _confirmTitle = Label(window, "Title", "", .09f, .78f, .91f, .92f, 27, TextAnchor.MiddleCenter, PackShopStyle.Accent);
            _confirmBody = Label(window, "Details", "", .085f, .483f, .915f, .751f, 22, TextAnchor.MiddleCenter, PackShopStyle.TextPrimary);
            _minus = AddButton(window, "Minus", "−", .12f, .29f, .25f, .437f, () => ChangeQuantity(-1));
            _quantityLabel = Label(window, "Quantity", "", .27f, .29f, .53f, .437f, 22, TextAnchor.MiddleCenter, PackShopStyle.TextPrimary);
            _plus = AddButton(window, "Plus", "+", .55f, .29f, .68f, .437f, () => ChangeQuantity(1));
            _maximum = AddButton(window, "Maximum", "최대", .71f, .29f, .88f, .437f, SelectMaximum);
            _cancel = AddButton(window, "Cancel", "취소", .092f, .095f, .465f, .24f, CloseConfirmation);
            _confirm = AddButton(window, "Confirm", "", .535f, .095f, .908f, .24f, ApplyConfirmed, true);
            _confirmation.SetActive(false);

            _bulk = new CardBulkDisassemblyUI(root, ReturnFromBulk, RefreshPointsAndOwnership);
            _finder.onValueChanged.AddListener(_ => Filter());
            Root.SetActive(false);
        }

        internal void Show(uint selectedCard, CardTradeAction action = CardTradeAction.Browse)
        {
            _entries ??= CardDatabase.AllCards.Where(CollectionProgress.IsCraftableCard).Select(card => new Entry(card))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Card.code).ToArray();
            Root.SetActive(true);
            _main.SetActive(true);
            _filter = 0;
            _previewOverframe = false;
            _collectionFilters.Selection = default;
            _finder.SetTextWithoutNotify("");
            RefreshDust();
            int selectedIndex = Filter(selectedCard);
            if (selectedIndex >= 0) Focus(_rows[selectedIndex % PageSize]);
            else Focus(_finder);
            switch (action)
            {
                case CardTradeAction.Craft: OpenConfirmation(true); break;
                case CardTradeAction.CraftOverframe: SetEditionPreview(true); OpenConfirmation(true, overframe: true); break;
                case CardTradeAction.Disassemble: OpenConfirmation(false); break;
                case CardTradeAction.DisassembleOverframe: SetEditionPreview(true); OpenConfirmation(false, overframe: true); break;
            }
        }

        internal void ShowExcessDisassembly()
        {
            Show(0);
            OpenBulk();
            _bulk.SelectExcess();
        }

        internal void Hide()
        {
            _bulk.Hide();
            HideConfirmation();
            _finder.DeactivateInputField();
            _art.SetCard(0);
            _selected = null;
            Root.SetActive(false);
        }

        internal void Back()
        {
            if (_bulk.IsOpen) _bulk.Back();
            else if (_confirmation.activeSelf) CloseConfirmation();
            else _onClose?.Invoke();
        }

        internal void ChangePage(int delta)
        {
            if (_bulk.IsOpen)
            {
                _bulk.ChangePage(delta);
                return;
            }
            if (_confirmation.activeSelf) return;

            int target = Mathf.Clamp(_page + delta, 0, PageCount - 1);
            if (target == _page) return;
            _page = target;
            RefreshPage();
            Focus(_rows[0]);
        }

        internal void ScrollDescription(float direction)
        {
            if (direction == 0 || !_main.activeInHierarchy || _confirmation.activeSelf) return;
            float range = _descriptionScroll.content.rect.height - _descriptionScroll.viewport.rect.height;
            if (range <= 0) return;
            _descriptionScroll.StopMovement();
            _descriptionScroll.verticalNormalizedPosition = Mathf.Clamp01(
                _descriptionScroll.verticalNormalizedPosition + direction * 300f * Time.unscaledDeltaTime / range);
        }

        private int PageCount => Mathf.Max(1, (_matches.Count + PageSize - 1) / PageSize);

        private void ChangeFilter(int filter)
        {
            if (_filter == filter) return;
            _filter = filter;
            Filter();
        }

        private int Filter(uint selectedCard = 0, bool clearStatus = true)
        {
            if (_entries == null) return -1;
            for (int i = 0; i < _filterLabels.Length; i++)
                SetButtonLabelColor(_filterLabels[i], i == _filter ? PackShopStyle.Accent :
                    i == 0 ? PackShopStyle.TextPrimary : CardRarityStyle.GetColor(Rarities[i - 1]));

            var query = new CardSearchQuery(_finder.text);
            _matches.Clear();
            _matches.AddRange(query.Order(_entries.Where(entry =>
                (_filter == 0 || entry.Rarity == Rarities[_filter - 1]) && _collectionFilters.Matches(entry.Card.code)),
                entry => entry.Card.code));
            int selectedIndex = _matches.FindIndex(entry => entry.Card.code == selectedCard);
            _page = selectedIndex < 0 ? 0 : selectedIndex / PageSize;
            if (clearStatus) _status.text = "";
            RefreshPage();
            if (selectedIndex >= 0) SelectRow(selectedIndex % PageSize);
            return selectedIndex;
        }

        private void RefreshPointsAndOwnership()
        {
            RefreshDust();
            Filter(_selected?.Card.code ?? 0, clearStatus: false);
        }

        private void RefreshDust() => _balance.text = $"보유 가루 {CollectionProgress.CraftDust:N0}";

        private void RefreshPage()
        {
            int first = _page * PageSize;
            for (int row = 0; row < _rows.Length; row++)
            {
                int index = first + row;
                bool visible = index < _matches.Count;
                _rows[row].transform.parent.gameObject.SetActive(visible);
                if (!visible) continue;
                var entry = _matches[index];
                uint code = entry.Card.code;
                _rowNames[row].text = $"{entry.Name} · {CollectionProgress.GetTotalOwnedCount(code)}장";
                ShopRarityBadge.Update(_rowRarities[row], code);
            }
            _count.text = _matches.Count == 0 ? "검색 결과 없음" : $"카드 {_matches.Count:N0}종";
            _pageLabel.text = $"{_page + 1} / {PageCount}";
            _previous.interactable = _page > 0;
            _next.interactable = _page + 1 < PageCount;

            int indexOfSelection = _matches.IndexOf(_selected);
            if (indexOfSelection < first || indexOfSelection >= first + PageSize) indexOfSelection = first;
            SetCard(indexOfSelection < _matches.Count ? _matches[indexOfSelection] : null);
        }

        private void SelectRow(int row)
        {
            int index = _page * PageSize + row;
            if (index >= _matches.Count) return;
            SetCard(_matches[index]);
        }

        private void SetCard(Entry entry)
        {
            uint code = entry?.Card.code ?? 0;
            if (entry != _selected)
            {
                _previewOverframe = false;
                _status.text = "";
                _description.text = entry?.Card.desc ?? "";
                _descriptionScroll.StopMovement();
                _descriptionScroll.content.anchoredPosition = Vector2.zero;
            }
            _selected = entry;
            _title.text = entry?.Name ?? "선택 카드 없음";
            _cardStats.text = entry?.Card.FormatStatsLineKR() ?? "";
            SetEditionPreview(_previewOverframe);
            ShopRarityBadge.Update(_rarity, code);
            _craft.interactable = _disassemble.interactable = code != 0;
            bool supports = CardArtLoader.SupportsOverframe(code);
            int owned = CollectionProgress.GetOwnedCount(code);
            int special = CollectionProgress.GetOwnedOverframeCount(code);
            bool hasSecretPack = UnlockedSecretPack(entry) != null;
            _secretPack.transform.parent.gameObject.SetActive(hasSecretPack);
            _prices.gameObject.SetActive(!hasSecretPack);
            _cardInfo.text = code == 0 ? "" : supports || special > 0
                ? $"일반판 {owned:N0}장 · 특별판 {special:N0}장" : $"보유 {owned:N0}장";
            _craftOverframe.transform.parent.gameObject.SetActive(supports);
            _disassembleOverframe.transform.parent.gameObject.SetActive(supports || special > 0);
            _disassembleOverframe.interactable = special > 0;
            if (entry != null)
            {
                SetButtonText(_craftOverframe, $"오버프레임 제작 · {CollectionProgress.GetCraftDustCost(entry.Rarity, true):N0}");
                SetButtonText(_disassembleOverframe, $"오버프레임 분해 · +{CollectionProgress.GetDisassemblyDustPerCard(code, true):N0}");
            }
            SetButtonText(_craft, entry == null ? "제작" : $"제작 · 가루 {CollectionProgress.GetCraftDustCost(entry.Rarity)}");
            SetButtonText(_disassemble, entry == null ? "분해" : $"분해 · 가루 +{CollectionProgress.GetDisassemblyDustPerCard(code)}");

            int first = _page * PageSize;
            for (int i = 0; i < _rows.Length && first + i < _matches.Count; i++)
                SetButtonLabelColor(_rowNames[i], _matches[first + i] == entry ? PackShopStyle.Accent : PackShopStyle.TextPrimary);
        }

        private void SetEditionPreview(bool overframe)
        {
            uint code = _selected?.Card.code ?? 0;
            bool available = CardArtLoader.SupportsOverframe(code) || CollectionProgress.GetOwnedOverframeCount(code) > 0;
            _previewOverframe = overframe && available;
            _editionPreview.transform.parent.gameObject.SetActive(available);
            SetButtonText(_editionPreview, _previewOverframe ? "일반판 보기" : "오버프레임 보기");
            bool changed = _art.CurrentCode != code;
            _art.gameObject.SetActive(code != 0);
            _art.SetDeckCard(code, _previewOverframe
                ? new DeckCardAppearance { artwork = CardArtLoader.FirstOverframeVariant(code) + 1, overframe = true }
                : null);
            if (changed && code != 0) UiEntranceMotion.Play(_art.gameObject, .72f, .14f);
        }

        private CollectionPack UnlockedSecretPack(Entry entry) => _openSecretPack == null || entry == null ||
            entry.Rarity < CardRarity.SuperRare ? null :
            CollectionProgress.SecretPacksForCard(entry.Card.code).FirstOrDefault(CollectionProgress.IsPackUnlocked);

        private void OpenSecretPack()
        {
            var pack = UnlockedSecretPack(_selected);
            if (pack != null) _openSecretPack.Invoke(pack.Id);
        }


        private void OpenBulk()
        {
            _finder.DeactivateInputField();
            _main.SetActive(false);
            _bulk.Show();
        }

        private void ReturnFromBulk()
        {
            _main.SetActive(true);
            _status.text = "";
            RefreshPointsAndOwnership();
            Focus(_bulkButton);
        }

        private void OpenConfirmation(bool crafting, bool overframe = false)
        {
            if (_selected == null || _confirmation.activeSelf) return;
            var card = _selected;
            uint code = card.Card.code;
            if (crafting)
            {
                int room = CollectionProgress.GetCraftLimit(code, overframe);
                int cost = CollectionProgress.GetCraftDustCost(card.Rarity, overframe);
                decimal usable = decimal.Floor(CollectionProgress.CraftDust / cost);
                if (room == 0)
                {
                    _status.color = PackShopStyle.Danger;
                    _status.text = "이 카드는 보유 한도에 도달했습니다.";
                    return;
                }
                if (usable == 0)
                {
                    _status.color = PackShopStyle.Danger;
                    _status.text = $"제작에 가루 {cost:N0}개가 필요합니다.";
                    return;
                }
                _maxQuantity = (int)Math.Min(room, usable);
            }
            else
            {
                _maxQuantity = CollectionProgress.GetDisassembleLimit(code, overframe);
                if (_maxQuantity == 0)
                {
                    _status.color = PackShopStyle.Danger;
                    _status.text = "이 카드를 보유하고 있지 않습니다.";
                    return;
                }
            }
            _status.text = "";
            _finder.DeactivateInputField();
            _confirmedCard = card;
            _quantity = 1;
            _crafting = crafting;
            _overframe = overframe;
            _mainControls.interactable = false;
            _confirmation.SetActive(true);
            RefreshConfirmation();
            UiEntranceMotion.Play(_confirmationWindow, .75f, .13f);
            Focus(_cancel);
        }

        private void ChangeQuantity(int delta)
        {
            if (!_confirmation.activeInHierarchy) return;
            _quantity = (int)Math.Clamp((long)_quantity + delta, 1, _maxQuantity);
            RefreshConfirmation();
            if (delta > 0 && !_plus.interactable) Focus(_minus);
            else if (delta < 0 && !_minus.interactable) Focus(_plus);
        }

        private void SelectMaximum()
        {
            if (!_confirmation.activeInHierarchy) return;
            _quantity = _maxQuantity;
            RefreshConfirmation();
            Focus(_minus);
        }

        private void RefreshConfirmation()
        {
            string action = _crafting ? "제작" : "분해";
            int perCard = _crafting
                ? CollectionProgress.GetCraftDustCost(_confirmedCard.Rarity, _overframe)
                : CollectionProgress.GetDisassemblyDustPerCard(_confirmedCard.Card.code, _overframe);
            long dust = (long)_quantity * perCard;
            decimal current = CollectionProgress.CraftDust;
            decimal result = _crafting ? current - dust : current + Math.Min(dust, decimal.MaxValue - current);
            _confirmTitle.text = _overframe ? $"오버프레임 {action}" : $"카드 {action}";
            string change = _crafting ? "사용" : "획득";
            _confirmBody.text = $"{_confirmedCard.Name}\n가루 {dust:N0} {change}\n보유 가루 {current:N0} → {result:N0}";
            if (!_crafting) _confirmBody.text += "\n보유 카드가 부족해진 덱은 수정 후 사용할 수 있습니다.";
            _quantityLabel.text = $"{_quantity:N0}장";
            _minus.transform.parent.gameObject.SetActive(_maxQuantity > 1);
            _plus.transform.parent.gameObject.SetActive(_maxQuantity > 1);
            _maximum.transform.parent.gameObject.SetActive(_maxQuantity > 1);
            _minus.interactable = _quantity > 1;
            _plus.interactable = _maximum.interactable = _quantity < _maxQuantity;
            SetButtonText(_confirm, $"{action}하기");
        }

        private void CloseConfirmation()
        {
            HideConfirmation();
            Focus(_overframe ? (_crafting ? _craftOverframe : _disassembleOverframe) : (_crafting ? _craft : _disassemble));
        }

        private void HideConfirmation()
        {
            _confirmation.SetActive(false);
            _mainControls.interactable = true;
            _confirmedCard = null;
        }

        private void ApplyConfirmed()
        {
            if (!_confirmation.activeInHierarchy || _confirmedCard == null) return;
            uint code = _confirmedCard.Card.code;
            string name = _confirmedCard.Name;
            string error;
            bool saved = _crafting ? CollectionProgress.TryCraftCard(code, _quantity, _overframe, out error) :
                CollectionProgress.TryDisassembleCards(new Dictionary<uint, int> { [code] = _quantity }, _overframe, out error);
            CloseConfirmation();
            RefreshPointsAndOwnership();
            _status.color = saved ? PackShopStyle.TextPrimary : PackShopStyle.Danger;
            _status.text = saved ? $"{name} {_quantity}장 {(_crafting ? "제작" : "분해")}했습니다." : error;
        }

        private static (ScrollRect, Text) CreateDescription(Transform parent)
        {
            var holder = Slot(parent, "Description", .491f, .305f, .925f, .585f);
            LotdUiKit.AddImage(holder.gameObject, LotdUiKit.WhiteSprite, PackShopStyle.SurfaceInset);
            var viewport = LotdUiKit.CreateRect("Viewport", holder, Vector2.zero, Vector2.one, new Vector2(8f, 5f), new Vector2(-20f, -5f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = LotdUiKit.AddText(viewport, "Text", "", 17, TextAnchor.UpperLeft, PackShopStyle.TextPrimary);
            text.supportRichText = false;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.rectTransform.anchorMin = Vector2.up;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.pivot = new Vector2(.5f, 1f);
            text.rectTransform.sizeDelta = Vector2.zero;
            text.rectTransform.anchoredPosition = Vector2.zero;
            text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = holder.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = text.rectTransform;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.verticalScrollbar = PackShopStyle.AddVerticalScrollbar(holder);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.scrollSensitivity = 25f;
            return (scroll, text);
        }


        private static void Focus(Selectable button)
        {
            var events = EventSystem.current;
            if (events != null && !events.alreadySelecting) events.SetSelectedGameObject(button.gameObject);
        }

        private static void SetButtonText(Button button, string label) => button.GetComponentInChildren<Text>().text = label;

        private static void SetButtonLabelColor(Text label, Color color)
        {
            label.transform.parent.GetComponent<PackShopButtonLabel>().Normal = color;
            label.color = color;
        }

        private static RectTransform Slot(Transform parent, string name, float left, float bottom, float right, float top) =>
            LotdUiKit.CreateRect(name, parent, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Text Label(Transform parent, string name, string text, float left, float bottom, float right, float top,
            int size, TextAnchor alignment, Color color) => LotdUiKit.AddFittedText(Slot(parent, name, left, bottom, right, top),
            "Text", text, size, Mathf.Max(11, size - 8), alignment, color);

        private static Button AddButton(Transform parent, string name, string text, float left, float bottom, float right, float top,
            Action action, bool primary = false)
        {
            var button = PackShopStyle.Button(Slot(parent, name, left, bottom, right, top), "Button", text, action, 18, primary);
            var caption = button.GetComponentInChildren<Text>();
            caption.resizeTextForBestFit = true;
            caption.resizeTextMinSize = 11;
            caption.resizeTextMaxSize = 18;
            caption.supportRichText = false;
            caption.rectTransform.anchorMin = new Vector2(.06f, .06f);
            caption.rectTransform.anchorMax = new Vector2(.94f, .94f);
            return button;
        }
    }
}
