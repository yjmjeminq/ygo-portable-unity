using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal static class DuelProfileArt
    {
        private const int Size = 112;
        private static Sprite _player, _missingOpponent;

        internal static Sprite Player => DuelUiKit.LoadSprite("Story/Protagonist") ?? (_player ??= DrawEmblem());
        internal static Sprite MissingOpponent => _missingOpponent ??= DrawOpponentPlaceholder();

        internal static Sprite Opponent(OpponentCharacter opponent)
        {
            if (opponent == null) return null;
            return DuelUiKit.LoadSprite($"Duel/Characters/{opponent.id}") ?? MissingOpponent;
        }

        private static Sprite DrawOpponentPlaceholder()
        {
            var pixels = new Color32[Size * Size];
            Color silhouette = new Color32(139, 176, 190, 255);
            Color backdrop = new Color32(31, 56, 74, 255);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var point = new Vector2((x + .5f) / Size - .5f, (y + .5f) / Size - .5f);
                var head = point - new Vector2(0f, .13f);
                var shoulders = (point - new Vector2(0f, -.34f)) * new Vector2(1f, 1.26f);
                float shape = Mathf.Max(.145f - head.magnitude, .264f - shoulders.magnitude);
                Color color = Color.Lerp(backdrop, silhouette, Mathf.Clamp01(shape * Size + .5f));
                color.a = Mathf.Clamp01((.465f - point.magnitude) * Size);
                pixels[y * Size + x] = color;
            }
            return DuelUiChrome.CreateSprite("Missing opponent portrait", Size, Size, pixels);
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
