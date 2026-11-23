using System;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class CardTradeControls
    {
        private readonly Button[] _actions = new Button[4];
        private uint _code;

        internal CardTradeControls(Transform parent, Action<uint, CardTradeAction> open, bool compact = false,
            bool deckEditor = false)
        {
            string[] labels = compact ? new[] { "제작", "특별판 제작", "분해", "특별판 분해" } :
                new[] { "일반 제작", "오버프레임 제작", "분해", "오버프레임 분해" };
            int columns = compact ? 4 : 2;
            int rows = compact ? 1 : 2;
            for (int i = 0; i < labels.Length; i++)
            {
                var action = (CardTradeAction)(i + 1);
                float left = i % columns / (float)columns;
                float top = 1f - (i / columns) / (float)rows;
                var holder = LotdUiKit.CreateRect(action.ToString(), parent,
                    new Vector2(left + .008f, top - 1f / rows + .025f),
                    new Vector2(left + 1f / columns - .008f, top - .025f), Vector2.zero, Vector2.zero);
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
            _actions[0].interactable = ready;
            _actions[1].interactable = ready && CardArtLoader.SupportsOverframe(code);
            _actions[2].interactable = ready && CollectionProgress.GetOwnedCount(code) > 0;
            _actions[3].interactable = ready && CollectionProgress.GetOwnedOverframeCount(code) > 0;
        }
    }
}
