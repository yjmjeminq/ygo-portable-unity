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
        public uint portraitCard;
        public string[] aliases;
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
        public bool featured;
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
    public struct DeckCardAppearance
    {
        public bool overframe;
        // Zero uses bundled artwork. Other values store the local artwork variant plus one.
        public int artwork;
        public bool destinyDraw;
        internal int ArtworkVariant => artwork - 1;
        internal bool IsDefault => !overframe && artwork == 0 && !destinyDraw;
    }

    [Serializable]
    public class UserDeck
    {
        public string id;
        public string name;
        public List<uint> main = new List<uint>();
        public List<uint> extra = new List<uint>();
        public List<uint> side = new List<uint>();

        public List<DeckCardAppearance> mainAppearances = new();
        public List<DeckCardAppearance> extraAppearances = new();
        public List<DeckCardAppearance> sideAppearances = new();

        public bool IsPlayable => main.Count >= 40 && main.Count <= 60 && extra.Count <= 15 && side.Count <= 15;

        internal List<DeckCardAppearance> Appearances(List<uint> cards)
        {
            if (ReferenceEquals(cards, main)) return mainAppearances ??= new();
            if (ReferenceEquals(cards, extra)) return extraAppearances ??= new();
            if (ReferenceEquals(cards, side)) return sideAppearances ??= new();
            throw new ArgumentException("The cards do not belong to this deck.", nameof(cards));
        }

        internal DeckCardAppearance GetAppearance(List<uint> cards, int index)
        {
            var appearances = Appearances(cards);
            return index >= 0 && index < appearances.Count ? appearances[index] : default;
        }

        internal void SetAppearance(List<uint> cards, int index, DeckCardAppearance appearance)
        {
            var appearances = Appearances(cards);
            if (appearance.IsDefault && index >= appearances.Count) return;
            while (index >= appearances.Count) appearances.Add(default);
            appearances[index] = appearance;
            TrimAppearances(appearances);
        }

        internal void InsertCard(List<uint> cards, int index, uint code, DeckCardAppearance appearance)
        {
            if (!ReferenceEquals(cards, main)) appearance.destinyDraw = false;
            var appearances = Appearances(cards);
            cards.Insert(index, code);
            if (index < appearances.Count) appearances.Insert(index, appearance);
            else SetAppearance(cards, index, appearance);
        }

        internal DeckCardAppearance RemoveCard(List<uint> cards, int index)
        {
            var appearances = Appearances(cards);
            cards.RemoveAt(index);
            if (index >= appearances.Count) return default;
            var removed = appearances[index];
            appearances.RemoveAt(index);
            TrimAppearances(appearances);
            return removed;
        }

        internal void ClearCards(List<uint> cards)
        {
            cards.Clear();
            Appearances(cards).Clear();
        }

        internal void ReorderCards(List<uint> cards, IEnumerable<uint> order)
        {
            var ordered = order.ToArray();
            var appearances = Appearances(cards);
            if (appearances.Count > 0)
            {
                // A sort keeps the relative order of physical copies of the same printing.
                var copies = new Dictionary<uint, Queue<DeckCardAppearance>>();
                for (int i = 0; i < cards.Count; i++)
                {
                    if (!copies.TryGetValue(cards[i], out var available))
                        copies[cards[i]] = available = new Queue<DeckCardAppearance>();
                    available.Enqueue(GetAppearance(cards, i));
                }

                appearances.Clear();
                foreach (uint code in ordered)
                    appearances.Add(copies[code].Dequeue());
                TrimAppearances(appearances);
            }
            cards.Clear();
            cards.AddRange(ordered);
        }

        internal IEnumerable<(uint Code, DeckCardAppearance Appearance)> Copies()
        {
            foreach (var cards in new[] { main, extra, side })
                for (int i = 0; i < cards.Count; i++)
                    yield return (cards[i], GetAppearance(cards, i));
        }

        internal void NormalizeAppearances()
        {
            foreach (var cards in new[] { main, extra, side })
            {
                var appearances = Appearances(cards);
                if (appearances.Count > cards.Count)
                    appearances.RemoveRange(cards.Count, appearances.Count - cards.Count);
                for (int i = 0; i < appearances.Count; i++)
                    if (appearances[i].artwork < 0) appearances[i] = default;
                TrimAppearances(appearances);
            }
        }

        private static void TrimAppearances(List<DeckCardAppearance> appearances)
        {
            while (appearances.Count > 0 && appearances[^1].IsDefault) appearances.RemoveAt(appearances.Count - 1);
        }
    }

    public static class DeckRepository
    {
        public static List<OpponentCharacter> Characters { get; private set; } = new List<OpponentCharacter>();
        public static List<PresetDeckInfo> PresetDecks { get; private set; } = new List<PresetDeckInfo>();
        public static List<UserDeck> UserDecks { get; private set; } = new List<UserDeck>();
        internal static event Action SaveChanged;

        private static Dictionary<int, OpponentCharacter> _charById = new Dictionary<int, OpponentCharacter>();
        private static Dictionary<int, List<PresetDeckInfo>> _decksByChar = new Dictionary<int, List<PresetDeckInfo>>();
        private static string _userDeckDir;

        public static void Load(string streamingAssetsPath, string persistentDataPath, bool seedDefaultDecks = true)
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
                    foreach (int charId in _decksByChar.Keys.ToArray())
                        _decksByChar[charId] = _decksByChar[charId].OrderByDescending(d => d.featured).ToList();
                }
            }

            _userDeckDir = Path.Combine(persistentDataPath, "Decks");
            LoadUserDecks(seedDefaultDecks);
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

        private static void LoadUserDecks(bool seedDefaultDecks)
        {
            UserDecks.Clear();
            if (!Directory.Exists(_userDeckDir)) Directory.CreateDirectory(_userDeckDir);

            var files = Directory.GetFiles(_userDeckDir, "*.json");
            if (files.Length == 0)
            {
                if (seedDefaultDecks) SeedDefaultUserDecks();
                return;
            }

            foreach (var f in files.OrderBy(x => x))
            {
                try
                {
                    var d = JsonUtility.FromJson<UserDeck>(File.ReadAllText(f));
                    if (d != null && !string.IsNullOrEmpty(d.id))
                    {
                        d.main ??= new List<uint>();
                        d.extra ??= new List<uint>();
                        d.side ??= new List<uint>();
                        d.NormalizeAppearances();
                        UserDecks.Add(d);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DeckRepository] Failed reading {f}: {e.Message}");
                }
            }
        }

        internal static void ReloadUserDecks() => LoadUserDecks(seedDefaultDecks: false);

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
            using var profileLock = LockUserSave();
            if (!Directory.Exists(_userDeckDir)) Directory.CreateDirectory(_userDeckDir);
            string path = Path.Combine(_userDeckDir, $"{deck.id}.json");
            string pending = Path.Combine(_userDeckDir, Path.GetRandomFileName() + ".tmp");
            try
            {
                File.WriteAllText(pending, JsonUtility.ToJson(deck, true), Encoding.UTF8);
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
            }
            finally
            {
                if (File.Exists(pending)) File.Delete(pending);
            }

            int existing = UserDecks.FindIndex(d => d.id == deck.id);
            if (existing >= 0) UserDecks[existing] = deck;
            else UserDecks.Add(deck);
            SaveChanged?.Invoke();
        }

        public static void DeleteUserDeck(string deckId)
        {
            using var profileLock = LockUserSave();
            string path = Path.Combine(_userDeckDir, $"{deckId}.json");
            File.Delete(path);
            UserDecks.RemoveAll(d => d.id == deckId);
            SaveChanged?.Invoke();
        }

        private static FileStream LockUserSave() => SaveSyncFiles.LockProfile(
            Path.Combine(Path.GetDirectoryName(_userDeckDir), CollectionProgress.ProfileFileName));

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
