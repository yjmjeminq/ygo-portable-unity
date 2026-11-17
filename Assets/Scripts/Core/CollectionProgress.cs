using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace YGOPortable.Core
{
    public sealed class CollectionStarter
    {
        public string Id { get; }
        public string Name { get; }
        public uint FeaturedCode { get; }
        public int MainCount => _deck.main.Count;
        public int ExtraCount => _deck.extra.Count;
        private readonly UserDeck _deck;

        internal CollectionStarter(string id, string name, PresetDeckInfo preset)
        {
            Id = id;
            Name = name;
            FeaturedCode = preset.sigCard;
            _deck = new UserDeck
            {
                id = "collection_" + id,
                name = name + " 스타터",
                main = new List<uint>(preset.main ?? Array.Empty<uint>()),
                extra = new List<uint>(preset.extra ?? Array.Empty<uint>()),
                side = new List<uint>(preset.side ?? Array.Empty<uint>()),
            };
        }

        internal UserDeck CreateDeck() => CollectionProgress.CopyDeck(_deck);
    }

    public sealed class CollectionPack
    {
        public string Id { get; }
        public string Name { get; }
        public uint FeaturedCode { get; }
        public IReadOnlyList<uint> CardPool { get; }

        internal CollectionPack(string id, string name, uint featuredCode, uint[] codes)
        {
            Id = id;
            Name = name;
            FeaturedCode = codes.Contains(featuredCode) ? featuredCode : codes[0];
            CardPool = Array.AsReadOnly(codes);
        }
    }

    public static class CollectionProgress
    {
        public const int PackCost = 100;
        public const int CardsPerPack = 8;
        public const int WinReward = 120;
        public const int LossReward = 60;
        public const int StartingBalance = 300;

        private const int ProfileVersion = 1;
        private const int GenericPackSize = 160;
        private const string ProfileFileName = "collection-profile.json";

        [Serializable]
        private struct CardCopies
        {
            public uint code;
            public int count;
        }

        [Serializable]
        private sealed class SavedProfile
        {
            public int version;
            public int balance;
            public string starterId;
            public UserDeck starterDeck;
            public List<CardCopies> cards;
        }

        private static readonly (string id, string name, int preset)[] StarterPresets =
        {
            ("dark-magician", "블랙 매지션", 30001),
            ("blue-eyes", "푸른 눈", 258),
            ("red-eyes", "붉은 눈", 279),
            ("elemental-hero", "엘리멘틀 히어로", 289),
            ("cyber-dragon", "사이버 드래곤", 312),
            ("ddd", "DDD", 34043),
            ("labrynth", "라뷰린스", 20672),
            ("sky-striker", "섬도희", 20687),
        };

        private static readonly uint[] SupportCodes =
        {
            55144522, 79571449, 12580477, 18144506, 14558127, 23434538, 59438930, 73642296,
            44095762, 53129443, 83764718, 40619825, 8267140, 23924608, 94145021, 10045474,
            92512625, 24224830, 41578483, 68468459, 32807846, 97268402, 70368879, 81223446,
        };

        private static readonly System.Random PackRandom = new System.Random();
        private static readonly HashSet<uint> SingleCopyCards = new HashSet<uint> { 55144522, 79571449, 12580477, 18144506 };
        private static string _profilePath;
        private static string _savedJson;
        private static SavedProfile _profile;
        private static Dictionary<uint, int> _owned = new Dictionary<uint, int>();
        private static IReadOnlyList<CollectionStarter> _starters;
        private static IReadOnlyList<CollectionPack> _packs;

        public static bool Initialized => _profilePath != null;
        public static bool HasStarter => _profile?.starterDeck != null;
        public static int Balance => _profile?.balance ?? 0;
        public static string LoadError { get; private set; } = "";
        public static IReadOnlyList<CollectionStarter> Starters => _starters ??= BuildStarters();
        public static IReadOnlyList<CollectionPack> Packs => _packs ??= BuildPacks();

        public static void Load(string persistentDataPath)
        {
            _profilePath = Path.Combine(persistentDataPath, ProfileFileName);
            _starters = null;
            _packs = null;
            _profile = null;
            _savedJson = null;
            _owned = new Dictionary<uint, int>();
            LoadError = "";
            try
            {
                // Reading directly distinguishes an absent profile from an inaccessible one.
                string json = ReadProfileJson();
                if (json == null) return;
                var saved = JsonUtility.FromJson<SavedProfile>(json);
                if (saved == null || saved.version != ProfileVersion || saved.balance < 0 || saved.cards == null ||
                    !IsStarterId(saved.starterId) || saved.starterDeck == null ||
                    saved.starterDeck.id != "collection_" + saved.starterId ||
                    saved.starterDeck.main == null || saved.starterDeck.extra == null || saved.starterDeck.side == null)
                    throw new InvalidDataException("Invalid collection profile.");

                var owned = new Dictionary<uint, int>(saved.cards.Count);
                foreach (var entry in saved.cards)
                {
                    if (entry.code == 0 || entry.count < 1 || entry.count > 3 || owned.ContainsKey(entry.code))
                        throw new InvalidDataException("Invalid card ownership entry.");
                    owned.Add(entry.code, entry.count);
                }
                _profile = saved;
                _owned = owned;
                _savedJson = json;
            }
            catch (DirectoryNotFoundException)
            {
                // The first write creates the profile directory.
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                LoadError = "카드 보유 정보를 불러올 수 없습니다. 기존 수집 데이터는 보존했습니다.";
                Debug.LogWarning($"[CollectionProgress] Could not load profile: {ex.Message}");
            }
        }

        public static int GetOwnedCount(uint code) => _owned.TryGetValue(code, out int copies) ? copies : 0;

        public static uint GetDeckCopyKey(uint code)
        {
            var card = CardDatabase.Get(code);
            return card != null && card.alias != 0 ? card.alias : code;
        }

        public static int GetDeckCopyLimit(uint code) => SingleCopyCards.Contains(GetDeckCopyKey(code)) ? 1 : 3;

        public static UserDeck CreateStarterDeck() => HasStarter ? CopyDeck(_profile.starterDeck) : null;

        public static bool CanUseDeck(UserDeck deck, out string reason)
        {
            if (!ValidateDeck(deck, out reason)) return false;
            if (!Initialized) return true;
            if (!CanTransact(out reason)) return false;
            if (!HasStarter)
            {
                reason = "먼저 스타터 덱을 선택하세요.";
                return false;
            }

            foreach (var copies in GetDeckCards(deck).GroupBy(code => code))
            {
                int needed = copies.Count();
                int owned = GetOwnedCount(copies.Key);
                if (owned >= needed) continue;
                reason = $"{CardDatabase.GetName(copies.Key)} 보유 {owned}장 / 필요 {needed}장";
                return false;
            }
            return true;
        }

        public static bool TryChooseStarter(string id, out UserDeck deck, out string error)
        {
            deck = null;
            if (!CanTransact(out error)) return false;
            if (HasStarter)
            {
                error = "이미 스타터 덱을 선택했습니다.";
                return false;
            }
            var starter = Starters.FirstOrDefault(option => option.Id == id);
            if (starter == null)
            {
                error = "사용 가능한 스타터 덱을 선택하세요.";
                return false;
            }

            var selected = starter.CreateDeck();
            var owned = GetDeckCards(selected).GroupBy(code => code).ToDictionary(group => group.Key, group => group.Count());
            var next = new SavedProfile
            {
                version = ProfileVersion,
                starterId = starter.Id,
                starterDeck = selected,
                balance = StartingBalance,
            };
            if (!Write(next, owned, out error)) return false;
            deck = CopyDeck(selected);
            return true;
        }

        public static bool TryBuyPack(string id, out IReadOnlyList<uint> pulls, out string error)
        {
            pulls = Array.Empty<uint>();
            if (!CanTransact(out error)) return false;
            if (!HasStarter)
            {
                error = "먼저 스타터 덱을 선택하세요.";
                return false;
            }
            if (Balance < PackCost)
            {
                error = "DP가 부족합니다.";
                return false;
            }
            var pack = Packs.FirstOrDefault(candidate => candidate.Id == id);
            if (pack == null)
            {
                error = "판매 중인 팩을 선택하세요.";
                return false;
            }

            var available = pack.CardPool.Where(code => GetOwnedCount(code) < GetDeckCopyLimit(code)).ToList();
            if (available.Count == 0)
            {
                error = "이 팩의 카드를 모두 최대 수량까지 보유하고 있습니다.";
                return false;
            }

            var owned = new Dictionary<uint, int>(_owned);
            var opened = new uint[CardsPerPack];
            for (int i = 0; i < opened.Length; i++)
            {
                if (available.Count == 0)
                {
                    opened[i] = pack.CardPool[PackRandom.Next(pack.CardPool.Count)];
                    continue;
                }
                int index = PackRandom.Next(available.Count);
                uint code = available[index];
                owned.TryGetValue(code, out int count);
                owned[code] = ++count;
                opened[i] = code;
                if (count >= GetDeckCopyLimit(code))
                    available.RemoveAt(index);
            }

            if (!Write(WithBalance(Balance - PackCost), owned, out error)) return false;
            pulls = Array.AsReadOnly(opened);
            return true;
        }

        public static bool TryAwardDuel(bool won, out int amount, out string error)
        {
            amount = 0;
            if (!CanTransact(out error)) return false;
            if (!HasStarter)
            {
                error = "먼저 스타터 덱을 선택하세요.";
                return false;
            }
            int reward = Math.Min(int.MaxValue - Balance, won ? WinReward : LossReward);
            if (reward == 0) return true;
            if (!Write(WithBalance(Balance + reward), _owned, out error)) return false;
            amount = reward;
            return true;
        }

        internal static UserDeck CopyDeck(UserDeck deck) => new UserDeck
        {
            id = deck.id,
            name = deck.name,
            main = new List<uint>(deck.main),
            extra = new List<uint>(deck.extra),
            side = new List<uint>(deck.side),
        };

        private static bool IsStarterId(string id) => !string.IsNullOrEmpty(id) && id.Length <= 64 &&
            id.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

        private static bool CanTransact(out string error)
        {
            error = Initialized ? LoadError : "카드 보유 정보가 아직 준비되지 않았습니다.";
            return error.Length == 0;
        }

        private static SavedProfile WithBalance(int balance) => new SavedProfile
        {
            version = ProfileVersion,
            starterId = _profile.starterId,
            starterDeck = _profile.starterDeck,
            balance = balance,
        };

        private static bool Write(SavedProfile next, Dictionary<uint, int> owned, out string error)
        {
            string pending = null;
            error = "";
            try
            {
                string directory = Path.GetDirectoryName(_profilePath);
                Directory.CreateDirectory(directory);
                using var profileLock = new FileStream(_profilePath + ".lock",
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                if (!string.Equals(ReadProfileJson(), _savedJson, StringComparison.Ordinal))
                {
                    error = "다른 게임 창에서 수집 데이터를 변경했습니다. 게임을 다시 시작해 주세요.";
                    return false;
                }

                pending = Path.Combine(directory, Path.GetRandomFileName() + ".tmp");
                next.cards = owned.OrderBy(pair => pair.Key)
                    .Select(pair => new CardCopies { code = pair.Key, count = pair.Value }).ToList();
                string json = JsonUtility.ToJson(next, true);
                File.WriteAllText(pending, json);
                if (File.Exists(_profilePath)) File.Replace(pending, _profilePath, null);
                else File.Move(pending, _profilePath);
                _savedJson = json;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = "수집 데이터를 저장할 수 없습니다. 잠시 후 다시 시도하세요.";
                Debug.LogWarning($"[CollectionProgress] Could not save profile: {ex.Message}");
                return false;
            }
            finally
            {
                if (pending != null)
                {
                    try { File.Delete(pending); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            _profile = next;
            _owned = owned;
            return true;
        }

        private static string ReadProfileJson()
        {
            try
            {
                using var stream = new FileStream(_profilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        private static IReadOnlyList<CollectionStarter> BuildStarters()
        {
            var starters = new List<CollectionStarter>();
            foreach (var (id, name, presetId) in StarterPresets)
            {
                var preset = DeckRepository.PresetDecks.FirstOrDefault(item => item.id == presetId);
                if (preset == null) continue;
                var starter = new CollectionStarter(id, name, preset);
                if (ValidateDeck(starter.CreateDeck(), out _))
                    starters.Add(starter);
            }
            return starters.AsReadOnly();
        }

        private static IReadOnlyList<CollectionPack> BuildPacks()
        {
            var packs = new List<CollectionPack>();
            var packed = new HashSet<uint>();

            void AddPack(string id, string name, uint featured, IEnumerable<uint> codes)
            {
                var pool = codes.Distinct().ToArray();
                if (pool.Length == 0) return;
                packs.Add(new CollectionPack(id, name, featured, pool));
                packed.UnionWith(pool);
            }

            AddPack("support-staples", "범용 지원 팩", 55144522, SupportCodes.Where(code => IsDeckCard(CardDatabase.Get(code))));
            foreach (var archetype in CardDatabase.GetAllArchetypes().OrderBy(archetype => archetype.name, StringComparer.Ordinal))
            {
                string id = "archetype-" + archetype.setcodes.Min().ToString("X4");
                var cards = archetype.coreCards.Concat(archetype.supportCards).Where(IsDeckCard).Select(card => card.code);
                AddPack(id, archetype.name + " 팩", archetype.representativeCode, cards);
            }

            var remaining = CardDatabase.AllCards.Where(card => IsDeckCard(card) && !packed.Contains(card.code));
            foreach (var group in remaining.GroupBy(GenericPackName).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var cards = group.OrderBy(card => card.name, StringComparer.Ordinal).ThenBy(card => card.code).ToArray();
                int count = (cards.Length + GenericPackSize - 1) / GenericPackSize;
                for (int i = 0; i < count; i++)
                {
                    var contents = cards.Skip(i * GenericPackSize).Take(GenericPackSize).Select(card => card.code);
                    string name = count == 1 ? $"{group.Key} 팩" : $"{group.Key} 팩 {i + 1}";
                    AddPack($"general-{group.Key}-{i + 1}", name, 0, contents);
                }
            }
            return packs.AsReadOnly();
        }

        private static string GenericPackName(CardInfo card)
        {
            if (card.IsExtra)
                return (card.type & YgoConstants.TYPE_FUSION) != 0 ? "융합" :
                    (card.type & YgoConstants.TYPE_SYNCHRO) != 0 ? "싱크로" :
                    card.IsXyz ? "엑시즈" : "링크";
            if (card.IsSpell) return "공용 마법";
            if (card.IsTrap) return "공용 함정";
            return YgoConstants.AttributeNamesKR.TryGetValue(card.attr, out string attribute)
                ? $"{attribute} 속성 몬스터" : "공용 몬스터";
        }

        private static bool IsDeckCard(CardInfo card) => card != null && card.code != 0 &&
            (card.IsMonster || card.IsSpell || card.IsTrap) && (card.type & YgoConstants.TYPE_TOKEN) == 0;

        private static IEnumerable<uint> GetDeckCards(UserDeck deck) => deck.main.Concat(deck.extra).Concat(deck.side);

        private static bool ValidateDeck(UserDeck deck, out string reason)
        {
            if (deck == null || deck.main == null || deck.extra == null || deck.side == null || !deck.IsPlayable)
            {
                reason = "메인 덱은 40~60장, 엑스트라 덱과 사이드 덱은 각각 15장까지 넣을 수 있습니다.";
                return false;
            }
            foreach (var code in GetDeckCards(deck))
            {
                if (IsDeckCard(CardDatabase.Get(code))) continue;
                reason = "덱에 사용할 수 없는 카드가 들어 있습니다.";
                return false;
            }
            if (deck.main.Any(code => CardDatabase.Get(code).IsExtra) || deck.extra.Any(code => !CardDatabase.Get(code).IsExtra))
            {
                reason = "메인 덱과 엑스트라 덱의 카드 구성을 확인하세요.";
                return false;
            }
            foreach (var group in GetDeckCards(deck).GroupBy(GetDeckCopyKey))
            {
                int limit = GetDeckCopyLimit(group.Key);
                if (group.Count() <= limit) continue;
                reason = $"{CardDatabase.GetName(group.Key)}은(는) 덱에 {limit}장까지 넣을 수 있습니다.";
                return false;
            }
            reason = "";
            return true;
        }
    }
}
