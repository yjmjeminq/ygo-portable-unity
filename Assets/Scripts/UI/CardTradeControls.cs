using System;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class CardTradeControls
    {
        private readonly Button[] _actions = new Button[4];
        private readonly bool _compact;
        private uint _code;

        internal CardTradeControls(Transform parent, Action<uint, CardTradeAction> open, bool compact = false,
            bool deckEditor = false)
        {
            _compact = compact;
            string[] labels = compact ? new[] { "제작", "특별판 제작", "분해", "특별판 분해" } :
                new[] { "일반 제작", "오버프레임 제작", "분해", "오버프레임 분해" };
            for (int i = 0; i < labels.Length; i++)
            {
                var action = (CardTradeAction)(i + 1);
                var holder = LotdUiKit.FillRect(action.ToString(), parent);
                int size = compact ? 14 : 18;
                var button = deckEditor
                    ? DeckEditorStyle.Button(holder, "Button", labels[i], () => open(_code, action), size, i < 2)
                    : LotdUiKit.CreateLotdButton(holder, "Button", labels[i], () => open(_code, action), size, i < 2);
                var text = button.GetComponentInChildren<Text>();
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = compact ? 8 : 11;
                text.resizeTextMaxSize = compact ? 14 : 18;
                _actions[i] = button;
            }
            Bind(0);
        }

        internal void Bind(uint code)
        {
            _code = code;
            bool ready = CollectionProgress.HasStarter && CollectionProgress.IsCraftableCard(CardDatabase.Get(code));
            bool supportsOverframe = CardArtLoader.SupportsOverframe(code);
            bool ownsOverframe = CollectionProgress.GetOwnedOverframeCount(code) > 0;
            bool showOverframeDisassembly = supportsOverframe || ownsOverframe;
            _actions[1].transform.parent.gameObject.SetActive(supportsOverframe);
            _actions[3].transform.parent.gameObject.SetActive(showOverframeDisassembly);
            _actions[0].interactable = ready;
            _actions[1].interactable = ready && supportsOverframe;
            _actions[2].interactable = ready && CollectionProgress.GetOwnedCount(code) > 0;
            _actions[3].interactable = ready && ownsOverframe;
            LayoutActions(2 + (supportsOverframe ? 1 : 0) + (showOverframeDisassembly ? 1 : 0));
        }

        private void LayoutActions(int visibleCount)
        {
            int columns = _compact ? visibleCount : 2;
            int rows = (visibleCount + columns - 1) / columns;
            int index = 0;
            foreach (var button in _actions)
            {
                var holder = (RectTransform)button.transform.parent;
                if (!holder.gameObject.activeSelf) continue;
                int row = index / columns;
                int rowColumns = Mathf.Min(columns, visibleCount - row * columns);
                float left = (index % columns) / (float)rowColumns;
                float top = 1f - (float)row / rows;
                holder.anchorMin = new Vector2(left + .008f, top - 1f / rows + .025f);
                holder.anchorMax = new Vector2(left + 1f / rowColumns - .008f, top - .025f);
                index++;
            }
        }
    }
}
