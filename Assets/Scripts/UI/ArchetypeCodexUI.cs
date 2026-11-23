using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class ArchetypeCodexUI
    {
        private const int GroupsPerPage = 8;
        private const int CardsPerPage = 8;

        private sealed class Entry
        {
            internal readonly uint[] Members;
            internal readonly uint[] Support;
            internal int OwnedMembers;

            internal Entry(ArchetypeInfo group)
            {
                Members = CardKeys(group.coreCards);
                Support = CardKeys(group.supportCards).Except(Members).ToArray();
            }

            private static uint[] CardKeys(IEnumerable<CardInfo> cards) => cards
                .Select(CardDatabase.GetArchetypeCollectionKey).Distinct()
                .OrderBy(CardDatabase.GetName, StringComparer.Ordinal).ThenBy(code => code).ToArray();
        }

        private readonly Dictionary<ArchetypeInfo, Entry> _entries = new();
        private readonly Dictionary<uint, (uint DisplayCode, long Count)> _ownership = new();
        private readonly (Button Button, Text Name, Text Progress)[] _groupRows =
            new (Button, Text, Text)[GroupsPerPage];
        private readonly (Button Button, CompositeCardView Art, Text Count)[] _cardTiles =
            new (Button, CompositeCardView, Text)[CardsPerPage];
        private readonly GameObject _detail;
        private readonly Button _back;
        private readonly Button _previousGroup;
        private readonly Button _nextGroup;
        private readonly Button _previousCard;
        private readonly Button _nextCard;
        private readonly Button _members;
        private readonly Button _support;
        private readonly Text _groupCount;
        private readonly Text _groupPageLabel;
        private readonly Text _cardPageLabel;
        private readonly Text _heading;
        private readonly Text _progress;
        private readonly RectTransform _progressFill;
        private readonly Text _milestone;
        private readonly Text _emptyCards;
        private readonly CompositeCardView _preview;
        private readonly Text _cardName;
        private readonly Text _cardOwner;

        private ArchetypeInfo[] _matches = Array.Empty<ArchetypeInfo>();
        private ArchetypeInfo _selectedGroup;
        private uint[] _cards = Array.Empty<uint>();
        private int _groupPage;
        private int _cardPage;
        private bool _showSupport;

        internal GameObject Root { get; }
        internal InputField SearchInput { get; }

        internal ArchetypeCodexUI(Transform parent, Action onBack)
        {
            var root = DuelUiKit.FillRect("Screen_ArchetypeCodex", parent);
            Root = root.gameObject;
            DuelUiKit.AddImage(Root, DuelUiKit.WhiteSprite, new Color32(8, 18, 31, 255));
            var content = Application.platform == RuntimePlatform.IPhonePlayer ? MobileSafeArea.Create(root) : root;
            PackShopStyle.Header(content, "카드군 도감");
            _back = AddButton(content, "Back", "뒤로", .84f, .921f, .967f, .976f, onBack);

            var directory = PackShopStyle.Panel(content, "Directory", new Vector2(.033f, .083f), new Vector2(.325f, .882f));
            SearchInput = PackShopStyle.Search(Slot(directory, "Search", .062f, .85f, .938f, .95f),
                "Input", "카드군 이름·소속 카드명", 18);
            _groupCount = Label(directory, "Count", .067f, .792f, .933f, .841f, 16, TextAnchor.MiddleLeft);
            for (int i = 0; i < GroupsPerPage; i++)
            {
                int row = i;
                float top = .782f - i * .083f;
                var button = AddButton(directory, $"Group_{i}", "", .062f, top - .075f, .938f, top, () => SelectGroup(row));
                button.gameObject.AddComponent<StarterRowFocus>().ShowPreview = () => SelectGroup(row);
                var name = ButtonLabel(button);
                name.alignment = TextAnchor.MiddleLeft;
                name.rectTransform.anchorMin = new Vector2(.043f, .055f);
                name.rectTransform.anchorMax = new Vector2(.719f, .945f);
                var progress = Label(button.transform, "Owned", .729f, 0f, .957f, 1f, 16);
                _groupRows[i] = (button, name, progress);
            }
            _previousGroup = AddButton(directory, "Previous", "이전", .062f, .047f, .297f, .105f, () => ChangeGroupPage(-1));
            _nextGroup = AddButton(directory, "Next", "다음", .703f, .047f, .938f, .105f, () => ChangeGroupPage(1));
            _groupPageLabel = Label(directory, "Page", .31f, .047f, .69f, .105f, 17);

            var detail = PackShopStyle.Panel(content, "Details", new Vector2(.341f, .083f), new Vector2(.967f, .882f));
            _detail = detail.gameObject;
            _heading = Label(detail, "Archetype", .035f, .875f, .955f, .968f, 29, TextAnchor.MiddleLeft, bold: true);
            _progress = Label(detail, "Progress", .035f, .819f, .67f, .875f, 19, TextAnchor.MiddleLeft);
            _milestone = Label(detail, "Milestone", .69f, .798f, .961f, .875f, 19, bold: true);
            var track = Slot(detail, "ProgressTrack", .036f, .799f, .641f, .816f);
            DuelUiKit.AddImage(track.gameObject, DuelUiKit.WhiteSprite, PackShopStyle.SurfaceInset, false, false);
            _progressFill = DuelUiKit.FillRect("Fill", track);
            DuelUiKit.AddImage(_progressFill.gameObject, DuelUiKit.WhiteSprite, PackShopStyle.Accent, false, false);

            _members = AddButton(detail, "Members", "", .036f, .721f, .328f, .778f, () => ShowCards(support: false));
            _support = AddButton(detail, "Support", "", .343f, .721f, .636f, .778f, () => ShowCards(support: true));

            for (int i = 0; i < CardsPerPage; i++)
            {
                int slot = i;
                float left = .036f + i % 4 * .152f;
                float top = .705f - i / 4 * .291f;
                var tile = AddButton(detail, $"Card_{i}", "", left, top - .276f, left + .144f, top, () => SelectCard(slot));
                var badge = ButtonLabel(tile);
                badge.rectTransform.anchorMin = new Vector2(.045f, .013f);
                badge.rectTransform.anchorMax = new Vector2(.955f, .169f);
                var art = CompositeCardView.Create(Slot(tile.transform, "Art", .068f, .179f, .932f, .969f), "Card", false);
                var focus = tile.gameObject.AddComponent<StarterRowFocus>();
                focus.ShowPreview = () => SelectCard(slot);
                focus.PreviewOnHover = true;
                _cardTiles[i] = (tile, art, badge);
            }
            _emptyCards = Label(detail, "Empty", .036f, .32f, .636f, .49f, 20);
            _previousCard = AddButton(detail, "Previous", "이전", .036f, .047f, .188f, .105f, () => ChangeCardPage(-1));
            _nextCard = AddButton(detail, "Next", "다음", .484f, .047f, .636f, .105f, () => ChangeCardPage(1));
            _cardPageLabel = Label(detail, "Page", .203f, .047f, .469f, .105f, 17);

            var selectedCard = PackShopStyle.Panel(detail, "CardPreview", new Vector2(.653f, .048f), new Vector2(.963f, .778f));
            _preview = CompositeCardView.Create(Slot(selectedCard, "Art", .065f, .20f, .935f, .963f), "Preview", true);
            _cardName = Label(selectedCard, "Name", .05f, .085f, .95f, .197f, 20, bold: true);
            _cardOwner = Label(selectedCard, "Owner", .05f, .024f, .95f, .085f, 16);

            var footer = Slot(content, "Controls", .037f, .016f, .963f, .06f);
            DuelUiKit.AddFittedText(footer, "Help", "방향키 이동   A 선택   Y 검색   LB/RB 페이지   B 뒤로", 16, 11,
                TextAnchor.MiddleCenter, PackShopStyle.TextMuted);

            SearchInput.onValueChanged.AddListener(_ => FilterGroups());
            Close();
        }

        internal void Open()
        {
            foreach (var group in StarterArchetypeCatalog.GetAll())
                if (!_entries.ContainsKey(group)) _entries.Add(group, new Entry(group));
            RefreshOwnership();
            SearchInput.SetTextWithoutNotify("");
            Root.SetActive(true);
            FilterGroups();
            FocusFirstGroup();
        }

        internal void Close()
        {
            foreach (var tile in _cardTiles)
                if (tile.Art.CurrentCode != 0) tile.Art.SetCard(0);
            if (_preview.CurrentCode != 0) _preview.SetCard(0);
            Root.SetActive(false);
        }

        internal void ChangePage(int delta)
        {
            var focus = EventSystem.current?.currentSelectedGameObject;
            if (focus != null && focus.transform.IsChildOf(_detail.transform)) ChangeCardPage(delta);
            else ChangeGroupPage(delta);
        }

        private void RefreshOwnership()
        {
            _ownership.Clear();
            foreach (var card in CardDatabase.AllCards)
            {
                int count = CollectionProgress.GetTotalOwnedCount(card.code);
                if (count == 0) continue;
                uint key = CardDatabase.GetArchetypeCollectionKey(card);
                _ownership.TryGetValue(key, out var copies);
                uint display = copies.DisplayCode == 0 || card.code == key ? card.code : copies.DisplayCode;
                _ownership[key] = (display, copies.Count + count);
            }
            foreach (var entry in _entries.Values)
                entry.OwnedMembers = entry.Members.Count(_ownership.ContainsKey);
        }

        private void FilterGroups()
        {
            _matches = StarterArchetypeCatalog.Search(SearchInput.text, includeSmallGroups: true)
                .OrderBy(group => group.name, StringComparer.Ordinal).ToArray();
            _groupPage = 0;
            _groupCount.text = _matches.Length == 0 ? "일치하는 카드군이 없습니다." : $"{_matches.Length:N0}개 카드군";
            RefreshGroups();
        }

        private void RefreshGroups()
        {
            int pageCount = Pages(_matches.Length, GroupsPerPage);
            _groupPageLabel.text = $"{_groupPage + 1} / {pageCount}";
            _previousGroup.interactable = _groupPage > 0;
            _nextGroup.interactable = _groupPage + 1 < pageCount;
            for (int i = 0; i < GroupsPerPage; i++)
            {
                int index = _groupPage * GroupsPerPage + i;
                var row = _groupRows[i];
                bool visible = index < _matches.Length;
                row.Button.gameObject.SetActive(visible);
                if (!visible) continue;
                var group = _matches[index];
                var entry = _entries[group];
                row.Name.text = group.name;
                row.Progress.text = $"{entry.OwnedMembers} / {entry.Members.Length}";
            }
            _selectedGroup = null;
            _detail.SetActive(_matches.Length > 0);
            SelectGroup(0);
        }

        private void ChangeGroupPage(int delta)
        {
            int next = Mathf.Clamp(_groupPage + delta, 0, Pages(_matches.Length, GroupsPerPage) - 1);
            if (next == _groupPage) return;
            _groupPage = next;
            RefreshGroups();
            FocusFirstGroup();
        }

        private void FocusFirstGroup() => EventSystem.current?.SetSelectedGameObject(
            _matches.Length > 0 ? _groupRows[0].Button.gameObject : _back.gameObject);

        private void SelectGroup(int row)
        {
            int index = _groupPage * GroupsPerPage + row;
            if (index >= _matches.Length || _selectedGroup == _matches[index]) return;
            _selectedGroup = _matches[index];
            for (int i = 0; i < GroupsPerPage; i++) PackShopStyle.SetSelected(_groupRows[i].Button, i == row);

            var entry = _entries[_selectedGroup];
            _heading.text = _selectedGroup.name;
            _progress.text = $"소속 카드 수집 {entry.OwnedMembers} / {entry.Members.Length}종";
            _progressFill.anchorMax = new Vector2(entry.Members.Length > 0 ? (float)entry.OwnedMembers / entry.Members.Length : 0f, 1f);
            _milestone.text = entry.OwnedMembers == entry.Members.Length && entry.Members.Length > 0 ? "수집 완료" :
                entry.OwnedMembers >= 10 ? "10종 수집 달성" : "";
            _milestone.color = PackShopStyle.Accent;
            ButtonLabel(_members).text = $"소속 카드 {entry.Members.Length}종";
            ButtonLabel(_support).text = $"지원 카드 {entry.Support.Length}종";
            ShowCards(support: false);
        }

        private void ShowCards(bool support)
        {
            var entry = _entries[_selectedGroup];
            _showSupport = support;
            _cardPage = 0;
            _cards = support ? entry.Support : entry.Members;
            PackShopStyle.SetSelected(_members, !support);
            PackShopStyle.SetSelected(_support, support);
            RefreshCards();
        }

        private void ChangeCardPage(int delta)
        {
            int next = Mathf.Clamp(_cardPage + delta, 0, Pages(_cards.Length, CardsPerPage) - 1);
            if (next == _cardPage) return;
            _cardPage = next;
            RefreshCards();
            EventSystem.current?.SetSelectedGameObject(_cardTiles[0].Button.gameObject);
        }

        private void RefreshCards()
        {
            int pageCount = Pages(_cards.Length, CardsPerPage);
            _cardPageLabel.text = $"{_cardPage + 1} / {pageCount}";
            _previousCard.interactable = _cardPage > 0;
            _nextCard.interactable = _cardPage + 1 < pageCount;
            _emptyCards.text = _showSupport ? "등록된 지원 카드가 없습니다." : "등록된 소속 카드가 없습니다.";
            _emptyCards.gameObject.SetActive(_cards.Length == 0);
            for (int i = 0; i < CardsPerPage; i++)
            {
                int index = _cardPage * CardsPerPage + i;
                var tile = _cardTiles[i];
                bool visible = index < _cards.Length;
                tile.Button.gameObject.SetActive(visible);
                uint key = visible ? _cards[index] : 0;
                uint code = _ownership.TryGetValue(key, out var copies) ? copies.DisplayCode : key;
                if (tile.Art.CurrentCode != code) tile.Art.SetCard(code);
                tile.Count.text = copies.Count == 0 ? "미보유" : $"{copies.Count:N0}장";
            }
            SelectCard(0);
        }

        private void SelectCard(int slot)
        {
            int index = _cardPage * CardsPerPage + slot;
            uint key = index < _cards.Length ? _cards[index] : 0;
            uint code = _ownership.TryGetValue(key, out var copies) ? copies.DisplayCode : key;
            PackShopStyle.ShowCardPreview(_preview, code);
            _cardName.text = code == 0 ? "" : CardDatabase.GetName(code);
            _cardOwner.text = code == 0 ? "" : copies.Count == 0 ? "미보유" : $"보유 {copies.Count:N0}장";
            for (int i = 0; i < CardsPerPage; i++) PackShopStyle.SetSelected(_cardTiles[i].Button, key != 0 && i == slot);
        }

        private static int Pages(int count, int pageSize) => Mathf.Max(1, (count + pageSize - 1) / pageSize);

        private static Text ButtonLabel(Button button) => ((PackShopButtonLabel)button).Label;

        private static RectTransform Slot(Transform parent, string name, float left, float bottom, float right, float top) =>
            DuelUiKit.CreateRect(name, parent, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Button AddButton(Transform parent, string name, string text, float left, float bottom, float right,
            float top, Action click) => PackShopStyle.Button(Slot(parent, name, left, bottom, right, top), "Button", text, click);

        private static Text Label(Transform parent, string name, float left, float bottom, float right, float top,
            int size, TextAnchor anchor = TextAnchor.MiddleCenter, bool bold = false) =>
            DuelUiKit.AddFittedText(Slot(parent, name, left, bottom, right, top), "Text", "", size, Mathf.Max(11, size - 7),
                anchor, bold ? PackShopStyle.TextPrimary : PackShopStyle.TextMuted, bold ? FontStyle.Bold : FontStyle.Normal);
    }
}
