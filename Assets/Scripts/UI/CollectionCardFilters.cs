using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class CollectionCardFilters
    {
        private static readonly string[] Rarities = { "전체 등급", "N", "R", "SR", "UR" };
        private static readonly string[] Ownership = { "전체 보유 상태", "보유", "미보유", "3장 초과" };
        private static readonly string[] Editions = { "전체 프레임", "오버프레임 지원", "오버프레임 보유" };
        private readonly Dropdown _rarity;
        private readonly Dropdown _ownership;
        private readonly Dropdown _edition;

        internal (int Rarity, int Ownership, int Edition) Selection
        {
            get => (_rarity?.value ?? 0, _ownership.value, _edition.value);
            set
            {
                _rarity?.SetValueWithoutNotify(value.Rarity);
                _ownership.SetValueWithoutNotify(value.Ownership);
                _edition.SetValueWithoutNotify(value.Edition);
            }
        }

        internal CollectionCardFilters(Transform parent, Action changed, bool includeRarity = true, bool deckEditor = false)
        {
            Dropdown Add(string name, string[] choices, float left, float right)
            {
                var holder = DuelUiKit.CreateRect(name, parent, new Vector2(left, 0f), new Vector2(right, 1f),
                    Vector2.zero, Vector2.zero);
                return deckEditor ? DeckEditorStyle.Dropdown(holder, "Dropdown", choices, _ => changed()) :
                    DuelUiKit.CreateDropdown(holder, "Dropdown", choices, _ => changed());
            }

            if (includeRarity) _rarity = Add("Rarity", Rarities, 0f, .265f);
            _ownership = Add("Ownership", Ownership, includeRarity ? .275f : 0f, includeRarity ? .58f : .45f);
            _edition = Add("Overframe", Editions, includeRarity ? .59f : .46f, 1f);
        }

        internal bool Matches(uint code, int? editionCopies = null)
        {
            var (rarity, ownership, edition) = Selection;
            if (rarity != 0 && (int)CardRarityCatalog.GetRarity(code) != rarity - 1) return false;
            if (edition == 1 && !CardArtLoader.SupportsOverframe(code) ||
                edition == 2 && CollectionProgress.GetOwnedOverframeCount(code) == 0) return false;

            int copies = editionCopies ?? CollectionProgress.GetTotalOwnedCount(code);
            return ownership switch
            {
                1 => copies > 0,
                2 => copies == 0,
                3 => copies > 0 && (CardDatabase.TryGetDeckPrintings(code, out var printings)
                    ? printings.Sum(printing => (long)CollectionProgress.GetTotalOwnedCount(printing.code))
                    : CollectionProgress.GetTotalOwnedCount(code)) > CollectionProgress.DeckCopyLimit,
                _ => true,
            };
        }
    }
}
