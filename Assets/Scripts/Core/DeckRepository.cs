using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace YGOPortable.Core
{
    [Serializable]
    public class OpponentCharacter
    {
        public int id;
        public int seriesIdx;
        public string seriesName;
        public string name;
        public string bio;
        public string portrait;
        public bool isMeta2026;
    }

    [Serializable]
    public class PresetDeckInfo
    {
        public int id;
        public int charId;
        public int seriesIdx;
        public string title;
        public string subtitle;
        public uint sigCard;
        public bool isMeta2026;
        public uint[] main;
        public uint[] extra;
        public uint[] side;
    }

    [Serializable]
    internal class OpponentsDbWrapper
    {
        public OpponentCharacter[] characters;
        public PresetDeckInfo[] decks;
    }

    [Serializable]
    public class UserDeck
    {
        public string id;
        public string name;
        public List<uint> main = new List<uint>();
        public List<uint> extra = new List<uint>();
        public List<uint> side = new List<uint>();

        public bool IsPlayable => main.Count >= 40 && main.Count <= 60 && extra.Count <= 15 && side.Count <= 15;
    }

    public static class DeckRepository
    {
        public static List<OpponentCharacter> Characters { get; private set; } = new List<OpponentCharacter>();
        public static List<PresetDeckInfo> PresetDecks { get; private set; } = new List<PresetDeckInfo>();
        public static List<UserDeck> UserDecks { get; private set; } = new List<UserDeck>();

        private static Dictionary<int, OpponentCharacter> _charById = new Dictionary<int, OpponentCharacter>();
        private static Dictionary<int, List<PresetDeckInfo>> _decksByChar = new Dictionary<int, List<PresetDeckInfo>>();
        private static string _userDeckDir;

        public static void Load(string streamingAssetsPath, string persistentDataPath)
        {
            Characters.Clear();
            PresetDecks.Clear();
            _charById.Clear();
            _decksByChar.Clear();

            string dbPath = Path.Combine(streamingAssetsPath, "opponents_db.json");
            if (File.Exists(dbPath))
            {
                string json = File.ReadAllText(dbPath);
                var wrapper = JsonUtility.FromJson<OpponentsDbWrapper>(json);
                if (wrapper?.characters != null)
                {
                    foreach (var c in wrapper.characters)
                    {
                        Characters.Add(c);
                        _charById[c.id] = c;
                    }
                }
                if (wrapper?.decks != null)
                {
                    foreach (var d in wrapper.decks)
                    {
                        PresetDecks.Add(d);
                        if (!_decksByChar.TryGetValue(d.charId, out var list))
                        {
                            list = new List<PresetDeckInfo>();
                            _decksByChar[d.charId] = list;
                        }
                        list.Add(d);
                    }
                }
            }

            _userDeckDir = Path.Combine(persistentDataPath, "Decks");
            LoadUserDecks();
            Debug.Log($"[DeckRepository] Loaded {Characters.Count} chars, {PresetDecks.Count} preset decks, {UserDecks.Count} user decks.");
        }

        public static OpponentCharacter GetCharacter(int charId)
        {
            return _charById.TryGetValue(charId, out var c) ? c : null;
        }

        public static List<PresetDeckInfo> GetDecksForCharacter(int charId)
        {
            return _decksByChar.TryGetValue(charId, out var list) ? list : new List<PresetDeckInfo>();
        }

        private static void LoadUserDecks()
        {
            UserDecks.Clear();
            if (!Directory.Exists(_userDeckDir)) Directory.CreateDirectory(_userDeckDir);

            var files = Directory.GetFiles(_userDeckDir, "*.json");
            if (files.Length == 0)
            {
                SeedDefaultUserDecks();
                return;
            }

            foreach (var f in files.OrderBy(x => x))
            {
                try
                {
                    var d = JsonUtility.FromJson<UserDeck>(File.ReadAllText(f));
                    if (d != null && !string.IsNullOrEmpty(d.id))
                        UserDecks.Add(d);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DeckRepository] Failed reading {f}: {e.Message}");
                }
            }
        }

        private static void SeedDefaultUserDecks()
        {
            // Seed with representative 2026 meta & classic decks so player can duel right away
            var metaPresets = PresetDecks.Where(d => d.isMeta2026).Take(6).ToList();
            if (metaPresets.Count == 0) metaPresets = PresetDecks.Take(4).ToList();

            int idx = 1;
            foreach (var p in metaPresets)
            {
                var ud = new UserDeck
                {
                    id = $"starter_{idx:D2}_{p.id}",
                    name = p.title,
                    main = new List<uint>(p.main ?? Array.Empty<uint>()),
                    extra = new List<uint>(p.extra ?? Array.Empty<uint>()),
                    side = new List<uint>(p.side ?? Array.Empty<uint>()),
                };
                SaveUserDeck(ud);
                idx++;
            }
        }

        public static void SaveUserDeck(UserDeck deck)
        {
            if (deck == null) return;
            if (string.IsNullOrEmpty(deck.id))
                deck.id = $"deck_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
            if (!Directory.Exists(_userDeckDir)) Directory.CreateDirectory(_userDeckDir);
            string path = Path.Combine(_userDeckDir, $"{deck.id}.json");
            File.WriteAllText(path, JsonUtility.ToJson(deck, true), Encoding.UTF8);

            int existing = UserDecks.FindIndex(d => d.id == deck.id);
            if (existing >= 0) UserDecks[existing] = deck;
            else UserDecks.Add(deck);
        }

        public static void DeleteUserDeck(string deckId)
        {
            UserDecks.RemoveAll(d => d.id == deckId);
            string path = Path.Combine(_userDeckDir, $"{deckId}.json");
            if (File.Exists(path)) File.Delete(path);
        }

        public static UserDeck ClonePresetToUserDeck(PresetDeckInfo preset, string customName = null)
        {
            var d = new UserDeck
            {
                id = $"copy_{preset.id}_{DateTime.UtcNow:HHmmssfff}",
                name = string.IsNullOrEmpty(customName) ? $"{preset.title} (커스텀)" : customName,
                main = new List<uint>(preset.main ?? Array.Empty<uint>()),
                extra = new List<uint>(preset.extra ?? Array.Empty<uint>()),
                side = new List<uint>(preset.side ?? Array.Empty<uint>()),
            };
            SaveUserDeck(d);
            return d;
        }

        public static string ExportToYdk(UserDeck deck)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"#created by YGO Portable Unity ({deck.name})");
            sb.AppendLine("#main");
            foreach (var c in deck.main) sb.AppendLine(c.ToString());
            sb.AppendLine("#extra");
            foreach (var c in deck.extra) sb.AppendLine(c.ToString());
            sb.AppendLine("!side");
            foreach (var c in deck.side) sb.AppendLine(c.ToString());
            return sb.ToString();
        }
    }
}
