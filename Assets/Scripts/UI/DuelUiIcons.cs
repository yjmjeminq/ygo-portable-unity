using System;
using System.Collections.Generic;
using UnityEngine;
using YGOPortable.Core;

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
        private static readonly Dictionary<string, string> IdentityAssetNames = new(StringComparer.Ordinal)
        {
            { "attribute/god", "attribute/divine" }, { "race/soldier", "race/warrior" },
            { "race/wizard", "race/spellcaster" }, { "race/angel", "race/fairy" },
            { "race/devil", "race/fiend" }, { "race/undead", "race/zombie" },
            { "race/aquarius", "race/aqua" }, { "race/flame", "race/pyro" },
            { "race/sandrock", "race/rock" }, { "race/bird", "race/wingedbeast" },
            { "race/botanical", "race/plant" }, { "race/beastbtl", "race/beastwarrior" },
            { "race/dinosaurs", "race/dinosaur" }, { "race/poseidon", "race/seaserpent" },
            { "race/reptiles", "race/reptile" }, { "race/god", "race/divinebeast" },
            { "race/creator", "race/creatorgod" }, { "race/mystdragon", "race/wyrm" },
            { "race/cyverse", "race/cyberse" },
        };
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

        internal static Sprite FindPhaseArtwork(string phase) => CardArtLoader.GetDuelVisualsSprite($"Hud/Phase{phase}.png");

        internal static string IdentityAssetName(string key) => IdentityAssetNames.GetValueOrDefault(key, key);

        internal static Sprite FindCardIdentity(string key)
        {
            var assetName = IdentityAssetName(key);
            var supplied = CardArtLoader.GetDuelVisualsSprite("CardIcons/" + assetName + ".png");
            if (supplied == null && assetName != key)
                supplied = CardArtLoader.GetDuelVisualsSprite("CardIcons/" + key + ".png");
            if (supplied != null) return supplied;
            if (!CardIdentity.TryGetValue(key, out var sprite))
                CardIdentity[key] = sprite = DuelUiKit.LoadSprite("Duel/CardIcons/" + assetName) ??
                    (assetName != key ? DuelUiKit.LoadSprite("Duel/CardIcons/" + key) : null);
            return sprite ?? Find(key);
        }

        internal static Sprite FindSpellTrap(uint type)
        {
            string key = YgoConstants.GetSpellTrapIconKey(type);
            int slash = key.IndexOf('/');
            if (slash < 0) return null;
            string subtype = key[(slash + 1)..];
            return FindCardIdentity("spelltrap/" + (subtype == "quick_play" ? "quickplay" : subtype)) ?? Find(key);
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
