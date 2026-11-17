using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal static class CardRarityStyle
    {
        internal static Color GetColor(CardRarity rarity) => rarity switch
        {
            CardRarity.Rare => new Color(.38f, .79f, 1f),
            CardRarity.SuperRare => new Color(1f, .82f, .32f),
            CardRarity.UltraRare => new Color(1f, .64f, .96f),
            _ => new Color(.78f, .83f, .91f),
        };
    }
}
