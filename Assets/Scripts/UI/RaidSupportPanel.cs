using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class RaidSupportPanel
    {
        private readonly Text _wallet;
        private readonly Text _status;
        private readonly Button _close;
        private readonly Button _apply;
        private readonly Text[] _counts = new Text[RaidSupportCatalog.Items.Length];
        private readonly Button[] _buy = new Button[RaidSupportCatalog.Items.Length];
        private readonly Button[] _equip = new Button[RaidSupportCatalog.Items.Length];
        private Action<RaidSupport> _save;
        private RaidSupport _selection;
        private bool _raid;

        internal GameObject Root { get; }
        internal Button DefaultFocus => _apply.gameObject.activeSelf ? _apply : _close;

        internal RaidSupportPanel(Transform parent, Action close)
        {
            var overlay = DuelUiKit.FillRect("RaidSupport", parent);
            Root = overlay.gameObject;
            DuelUiKit.AddImage(Root, DuelUiKit.WhiteSprite, new Color(.008f, .019f, .038f, .93f));
            var panel = DuelUiKit.CreateMenuPanel(overlay, "Window", new Vector2(.13f, .095f), new Vector2(.87f, .91f));
            Label(panel, "Title", "전술 서포트", .055f, .85f, .53f, .965f, 28, DuelUiKit.GoldAccent);
            _wallet = Label(panel, "Wallet", "", .55f, .851f, .939f, .965f, 22, DuelUiKit.GoldAccent);
            for (int i = 0; i < RaidSupportCatalog.Items.Length; i++)
            {
                int index = i;
                var item = RaidSupportCatalog.Items[i];
                float top = .825f - i * .125f;
                var row = Region(panel, $"Item_{i}", .06f, top - .111f, .94f, top);
                DuelUiKit.AddImage(row.gameObject, DuelUiKit.WhiteSprite, new Color(.026f, .074f, .13f, .94f), false, false);
                Label(row, "Description", $"{item.Name}\n{item.Description}", .025f, .05f, .48f, .95f, 19);
                _counts[i] = Label(row, "Count", "", .485f, .10f, .562f, .90f, 18, DuelUiKit.TextMuted);
                _equip[i] = MakeButton(row, "Equip", "", .57f, .08f, .734f, .92f, () => Toggle(index));
                _buy[i] = MakeButton(row, "Buy", $"구매 {item.Cost:N0} DP", .745f, .08f, .975f, .92f, () => Buy(index));
            }
            _status = Label(panel, "Status", "", .061f, .118f, .939f, .193f, 18, DuelUiKit.GoldAccent);
            _close = MakeButton(panel, "Close", "돌아가기", .58f, .036f, .749f, .112f, close);
            _apply = MakeButton(panel, "Apply", "장착 적용", .762f, .036f, .94f, .112f, () =>
            {
                _save?.Invoke(_selection);
                close();
            }, true);
            Root.SetActive(false);
        }

        internal void Prepare(bool raid, RaidSupport selection = RaidSupport.None, Action<RaidSupport> save = null)
        {
            _raid = raid;
            _save = save;
            _selection = selection;
            _status.text = save == null ? "보유한 아이템은 스토리에서 장착할 수 있습니다." : "최대 2개 · 듀얼을 시작하면 각각 1개 소모";
            Refresh();
        }

        private void Refresh()
        {
            _wallet.text = $"보유 DP {CollectionProgress.Balance:N0}";
            bool picking = _save != null;
            _apply.gameObject.SetActive(picking);
            for (int i = 0; i < RaidSupportCatalog.Items.Length; i++)
            {
                var item = RaidSupportCatalog.Items[i];
                int count = CollectionProgress.GetSupportCount(item.Id);
                _counts[i].text = $"{count:N0}개";
                _buy[i].interactable = CollectionProgress.HasStarter && CollectionProgress.Balance >= item.Cost && count < int.MaxValue;
                _equip[i].gameObject.SetActive(picking);
                bool allowed = _raid || item.Id != RaidSupport.Breaker;
                _equip[i].interactable = count > 0 && allowed;
                _equip[i].GetComponentInChildren<Text>().text = !allowed ? "레이드 전용" :
                    (_selection & item.Id) != 0 ? "✓ 장착" : "□ 장착";
            }
        }

        private void Toggle(int index)
        {
            var proposed = _selection ^ RaidSupportCatalog.Items[index].Id;
            if (!CollectionProgress.CanEquipRaidSupports(proposed, _raid))
            {
                _status.text = "장착할 수 있는 아이템은 최대 2개입니다.";
                return;
            }
            _selection = proposed;
            int count = RaidSupportCatalog.Items.Count(entry => (_selection & entry.Id) != 0);
            _status.text = $"장착 {count} / 2 · 듀얼을 시작하면 각각 1개 소모";
            Refresh();
        }

        private void Buy(int index)
        {
            var item = RaidSupportCatalog.Items[index];
            _status.text = CollectionProgress.TryBuyRaidSupport(item.Id, out string error) ? $"{item.Name} 1개 구매" : error;
            Refresh();
        }

        private static RectTransform Region(Transform parent, string name, float left, float bottom, float right, float top) =>
            DuelUiKit.CreateRect(name, parent, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Text Label(Transform parent, string name, string text, float left, float bottom, float right, float top,
            int size, Color? color = null) => DuelUiKit.AddFittedText(Region(parent, name, left, bottom, right, top),
                "Text", text, size, 12, TextAnchor.MiddleCenter, color ?? DuelUiKit.TextPrimary);

        private static Button MakeButton(Transform parent, string name, string text, float left, float bottom, float right, float top,
            Action click, bool gold = false) => DuelUiKit.CreateButton(Region(parent, name, left, bottom, right, top),
                "Button", text, click, 17, gold);
    }
}
