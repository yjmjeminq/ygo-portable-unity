using System;
using System.Collections.Generic;
using UnityEngine;

namespace YGOPortable.UI
{
    internal static class DuelUiIcons
    {
        [Serializable]
        private sealed class AtlasMap
        {
            public int cell;
            public int columns;
            public string[] names;
        }

        private static readonly Dictionary<string, Sprite> Sprites = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Sprite> CardIdentity = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Rect> Cells = new(StringComparer.Ordinal);
        private static Texture2D _texture;
        private static bool _loaded;

        internal static Sprite Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!_loaded) Load();
            if (!Cells.TryGetValue(key, out var cell)) return null;
            if (!Sprites.TryGetValue(key, out var sprite) || sprite == null)
            {
                sprite = Sprite.Create(_texture, cell, Vector2.one * .5f, 100f, 0, SpriteMeshType.FullRect);
                sprite.name = key;
                Sprites[key] = sprite;
            }
            return sprite;
        }

        internal static Sprite Action(string key) => key == null ? null : Find("action/" + key.ToLowerInvariant());

        internal static Sprite Filter(string key) => Find("filter/" + key.ToLowerInvariant());

        internal static Sprite FindCardIdentity(string key)
        {
            if (!CardIdentity.TryGetValue(key, out var sprite))
                CardIdentity[key] = sprite = DuelUiKit.LoadSprite("Duel/CardIcons/" + key);
            return sprite;
        }

        private static void Load()
        {
            _loaded = true;
            _texture = Resources.Load<Texture2D>("Duel/DuelUiIcons");
            var json = Resources.Load<TextAsset>("Duel/DuelUiIconMap");
            if (_texture == null || json == null) return;
            var map = JsonUtility.FromJson<AtlasMap>(json.text);
            if (map == null || map.cell <= 0 || map.columns <= 0 || map.names == null ||
                map.columns != _texture.width / map.cell || map.names.Length > map.columns * (_texture.height / map.cell))
                return;
            for (int index = 0; index < map.names.Length; index++)
            {
                int row = index / map.columns;
                int column = index % map.columns;
                Cells.Add(map.names[index], new Rect(column * map.cell, _texture.height - (row + 1) * map.cell,
                    map.cell, map.cell));
            }
        }
    }
}
