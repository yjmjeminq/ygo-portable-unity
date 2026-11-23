using System;
using System.Collections.Generic;

namespace YGOPortable.Core
{
    public partial class DuelSession
    {
        private const uint DestinyDrawTag = 0x31444459;
        private readonly HashSet<uint>[] _destinyCards = { new(), new() };
        private readonly bool[] _destinyUsed = new bool[2];
        private readonly List<uint> _destinyPositions = new();
        private Random _destinyRandom;

        internal event Action<byte> OnDestinyDraw;

        private void ResetDestinyDraw()
        {
            foreach (var cards in _destinyCards) cards.Clear();
            Array.Clear(_destinyUsed, 0, _destinyUsed.Length);
            _destinyPositions.Clear();
        }

        private void OfferDestinyDraw(byte player)
        {
            if (player > 1 || _destinyUsed[player] || _destinyCards[player].Count == 0) return;
            _destinyPositions.Clear();
            uint deckSize = YgoNativeBridge.YGO_QueryCount(NativePtr, player, YgoConstants.LOCATION_DECK);
            for (uint i = 0; i < deckSize; i++)
            {
                uint identity = YgoNativeBridge.YGO_QueryCardIdentity(NativePtr, player, YgoConstants.LOCATION_DECK, i);
                if (_destinyCards[player].Contains(identity)) _destinyPositions.Add(i);
            }
            if (_destinyPositions.Count != 0)
                ActivePrompt = new DuelPrompt { messageType = YgoConstants.MSG_DESTINY_DRAW, playerId = player };
        }

        private void ResolveDestinyDraw(byte player, bool activate)
        {
            if (activate && _destinyPositions.Count > 0)
            {
                uint selected = _destinyPositions[_destinyRandom.Next(_destinyPositions.Count)];
                if (YgoNativeBridge.YGO_MoveDeckCardToTop(NativePtr, player, selected) != 0)
                {
                    _destinyUsed[player] = true;
                    ForgetObservedDeckTop(player);
                    AddLog($"{(player == HumanPlayerIndex ? "플레이어" : "상대")} 데스티니 드로우 발동!");
                    OnDestinyDraw?.Invoke(player);
                }
            }
            _destinyPositions.Clear();
        }
    }
}
