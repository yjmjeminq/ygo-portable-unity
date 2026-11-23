using System.Collections.Generic;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal static class DuelProfileArt
    {
        private const int Size = 112;
        private static readonly Dictionary<uint, Sprite> AcePortraits = new();
        private static Sprite _player;

        internal static Sprite Player => _player ??= DrawEmblem();

        internal static Sprite Opponent(OpponentCharacter opponent)
        {
            if (opponent == null) return null;
            if (TryGetAcePortrait(opponent.portraitCard) is { } featured) return featured;
            foreach (var deck in DeckRepository.GetDecksForCharacter(opponent.id))
                if (TryGetAcePortrait(deck.sigCard) is { } art) return art;
            return Player;
        }

        private static Sprite TryGetAcePortrait(uint code)
        {
            if (code == 0) return null;
            if (AcePortraits.TryGetValue(code, out var portrait)) return portrait;
            var artwork = CardArtLoader.RetainArtwork(code);
            try
            {
                if (artwork == null || artwork == CardArtLoader.GetFallbackArt() || !artwork.texture.isReadable)
                    return null;

                var texture = artwork.texture;
                float crop = Mathf.Min(texture.width, texture.height);
                float width = crop / texture.width;
                float height = crop / texture.height;
                var pixels = new Color32[Size * Size];
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = .5f + width * ((x + .5f) / Size - .5f);
                    float v = .5f + height * ((y + .5f) / Size - .5f);
                    pixels[y * Size + x] = texture.GetPixelBilinear(u, v);
                }
                var result = DuelUiChrome.CreateSprite($"Ace portrait {code}", Size, Size, pixels);
                AcePortraits.Add(code, result);
                return result;
            }
            finally { CardArtLoader.ReleaseArtwork(code); }
        }

        private static Sprite DrawEmblem()
        {
            var pixels = new Color32[Size * Size];
            var blue = new Color32(34, 65, 79, 255);
            var dark = new Color32(18, 32, 46, 255);
            var silver = new Color32(172, 230, 218, 255);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var point = new Vector2((x + .5f) / Size - .5f, (y + .5f) / Size - .5f);
                float distance = Mathf.Abs(point.x) + Mathf.Abs(point.y) * .85f;
                Color color = distance < .205f ? Color.Lerp(blue, silver, point.y >= 0f ? .86f : .52f) :
                    Color.Lerp(dark, blue, Mathf.Clamp01(1f - point.magnitude * 2f));
                if (distance is > .274f and < .292f) color = silver;
                color.a = Mathf.Clamp01((.465f - point.magnitude) * Size);
                pixels[y * Size + x] = color;
            }
            return DuelUiChrome.CreateSprite("Player emblem", Size, Size, pixels);
        }
    }
}
