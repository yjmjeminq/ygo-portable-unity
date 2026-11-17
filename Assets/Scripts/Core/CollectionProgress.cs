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
        public IReadOnlyList<uint> ThemeCards { get; }
        public int MainCount => Deck.main.Count;
        public int ExtraCount => Deck.extra.Count;

        private readonly ArchetypeInfo _archetype;
        private readonly PresetDeckInfo _preset;
        private UserDeck _deck;

        private UserDeck Deck => _deck ??= CollectionProgress.BuildStarterDeck(Id, _archetype, _preset);

        internal CollectionStarter(ArchetypeInfo archetype, PresetDeckInfo preset)
        {
            Id = $"archetype-{archetype.setcodes.Min():x4}";
            Name = archetype.name;
            FeaturedCode = preset != null && preset.sigCard != 0 ? preset.sigCard : archetype.representativeCode;
            ThemeCards = Array.AsReadOnly(archetype.coreCards.Select(card => card.code).Distinct().ToArray());
            _archetype = archetype;
            _preset = preset;
        }

        internal UserDeck CreateDeck() => CollectionProgress.CopyDeck(Deck);
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
        public const int PackBundleSize = 10;
        public const int CardsPerPack = 8;
        public const int WinReward = 120;
        public const int LossReward = 60;
        public const int StartingBalance = 3000;
        public const int CraftPointCost = 30;
        public const int DisassemblePointValue = 10;
        public const int DeckCopyLimit = 3;

        private const int ProfileVersion = 5;
        private const int GenericPackSize = 160;
        internal const string ProfileFileName = "collection-profile.json";
        private const int FirstOpponentId = 99;
        private const int SecondOpponentId = 81;

        [Serializable]
        private struct CardCopies
        {
            public uint code;
            public int count;
        }

        [Serializable]
        private sealed class OpponentDeckProgress
        {
            public int opponentId;
            public List<int> unlockedDeckIds;
            public List<int> clearedDeckIds;
        }

        [Serializable]
        private sealed class SavedProfile
        {
            public int version;
            public int balance;
            public long[] craftPoints;
            public string starterId;
            public UserDeck starterDeck;
            public List<CardCopies> cards;
            public List<int> unlockedOpponentIds;
            public List<int> clearedOpponentIds;
            public List<OpponentDeckProgress> opponentDeckProgress;
        }

        private static readonly (ushort setcode, int presetId)[] StarterPresets =
        {
            (0x10a2, 30001), // 블랙 매지션
            (0x00dd, 258), // 푸른 눈
            (0x003b, 279), // 붉은 눈
            (0x3008, 289), // 엘리멘틀 히어로
            (0x1093, 312), // 사이버 드래곤
            (0x10af, 34043), // DD(디디)
            (0x017f, 20672), // 라뷰린스
            (0x1115, 20687), // 섬도희
        };

        private static readonly uint[] SupportCodes =
        {
            55144522, 79571449, 12580477, 18144506, 14558127, 23434538, 59438930, 73642296,
            44095762, 53129443, 83764718, 40619825, 8267140, 23924608, 94145021, 10045474,
            92512625, 24224830, 41578483, 68468459, 32807846, 97268402, 70368879, 81223446,
        };

        private static readonly System.Random PackRandom = new System.Random();
        private static string _profilePath;
        private static string _savedJson;
        private static SavedProfile _profile;
        private static Dictionary<uint, int> _owned = new Dictionary<uint, int>();
        private static IReadOnlyList<CollectionStarter> _starters;
        private static IReadOnlyList<CollectionPack> _packs;
        private static IReadOnlyList<OpponentCharacter> _opponents;

        public static bool Initialized => _profilePath != null;
        internal static event Action SaveChanged;
        public static bool HasStarter => _profile?.starterDeck != null;
        public static int Balance => _profile?.balance ?? 0;
        public static string LoadError { get; private set; } = "";
        public static IReadOnlyList<CollectionStarter> Starters => _starters ??= BuildStarters();
        public static IReadOnlyList<CollectionPack> Packs => _packs ??= BuildPacks();
        public static int OpponentCount => OpponentOrder.Count;
        public static int UnlockedOpponentCount => OpponentOrder.Count(opponent => IsOpponentUnlocked(opponent.id));
        internal static IReadOnlyList<OpponentCharacter> UnlockedOpponents =>
            OpponentOrder.Where(opponent => IsOpponentUnlocked(opponent.id)).ToArray();

        private static IReadOnlyList<OpponentCharacter> OpponentOrder =>
            Initialized ? _opponents ??= BuildOpponentOrder() : BuildOpponentOrder();

        public static void Load(string persistentDataPath)
        {
            _profilePath = Path.Combine(persistentDataPath, ProfileFileName);
            _starters = null;
            _packs = null;
            _opponents = null;
            _profile = null;
            _savedJson = null;
            _owned = new Dictionary<uint, int>();
            LoadError = "";
            try
            {
                // Reading directly distinguishes an absent profile from an inaccessible one.
                string json = ReadProfileJson();
                if (json == null) return;
                var saved = ParseProfile(json, out var owned);
                InitializeOpponentProgress(saved);
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

        internal static void ValidateSaveProfile(string json) => ParseProfile(json, out _);

        private static SavedProfile ParseProfile(string json, out Dictionary<uint, int> owned)
        {
            var saved = JsonUtility.FromJson<SavedProfile>(json);
            if (saved == null || saved.version < 1 || saved.version > ProfileVersion || saved.balance < 0 ||
                saved.cards == null || !IsStarterId(saved.starterId) || saved.starterDeck == null ||
                saved.starterDeck.id != "collection_" + saved.starterId ||
                saved.starterDeck.main == null || saved.starterDeck.extra == null || saved.starterDeck.side == null)
                throw new InvalidDataException("Invalid collection profile.");

            saved.craftPoints ??= new long[CardRarityCatalog.TierCount];
            if (saved.craftPoints.Length != CardRarityCatalog.TierCount || saved.craftPoints.Any(points => points < 0))
                throw new InvalidDataException("Invalid craft point balances.");

            owned = new Dictionary<uint, int>(saved.cards.Count);
            foreach (var entry in saved.cards)
            {
                if (entry.code == 0 || entry.count < 1 || !owned.TryAdd(entry.code, entry.count))
                    throw new InvalidDataException("Invalid card ownership entry.");
            }
            return saved;
        }

        public static int GetOwnedCount(uint code) => _owned.TryGetValue(code, out int copies) ? copies : 0;

        public static long GetCraftPoints(CardRarity rarity) =>
            (uint)rarity < CardRarityCatalog.TierCount ? _profile?.craftPoints[(int)rarity] ?? 0 : 0;

        public static bool IsCraftableCard(CardInfo card) => IsDeckCard(card);

        public static int GetCraftLimit(uint code) => HasStarter && IsCraftableCard(CardDatabase.Get(code))
            ? int.MaxValue - GetOwnedCount(code) : 0;

        public static int GetDisassembleLimit(uint code) => HasStarter && IsCraftableCard(CardDatabase.Get(code))
            ? GetOwnedCount(code) : 0;

        public static IReadOnlyDictionary<uint, int> GetDisassembleLimits() => HasStarter
            ? _owned.Where(entry => IsCraftableCard(CardDatabase.Get(entry.Key)))
                .ToDictionary(entry => entry.Key, entry => entry.Value)
            : new Dictionary<uint, int>();

        public static bool TryCraftCard(uint code, int quantity, out string error)
        {
            if (!CanTradeCards(out error)) return false;
            if (quantity <= 0 || !IsCraftableCard(CardDatabase.Get(code)))
            {
                error = "제작할 카드와 수량을 확인하세요.";
                return false;
            }
            if (quantity > GetCraftLimit(code))
            {
                error = "카드 보유 수량을 저장할 수 있는 범위를 초과합니다.";
                return false;
            }

            var rarity = CardRarityCatalog.GetRarity(code);
            long cost = (long)quantity * CraftPointCost;
            if (GetCraftPoints(rarity) < cost)
            {
                error = $"{CardRarityCatalog.GetLabel(rarity)} 제작 포인트가 {cost:N0} 필요합니다.";
                return false;
            }

            var owned = new Dictionary<uint, int>(_owned) { [code] = GetOwnedCount(code) + quantity };
            var next = WithBalance(Balance);
            next.craftPoints = (long[])_profile.craftPoints.Clone();
            next.craftPoints[(int)rarity] -= cost;
            return Write(next, owned, out error);
        }

        public static bool TryDisassembleCards(IReadOnlyDictionary<uint, int> quantities, out string error)
        {
            if (!CanTradeCards(out error)) return false;
            if (quantities == null || quantities.Count == 0)
            {
                error = "분해할 카드를 선택하세요.";
                return false;
            }

            var owned = new Dictionary<uint, int>(_owned);
            var next = WithBalance(Balance);
            next.craftPoints = (long[])_profile.craftPoints.Clone();
            foreach (var entry in quantities)
            {
                uint code = entry.Key;
                int quantity = entry.Value;
                if (quantity <= 0 || !IsCraftableCard(CardDatabase.Get(code)))
                {
                    error = "분해할 카드와 수량을 확인하세요.";
                    return false;
                }
                int copies = owned.GetValueOrDefault(code);
                if (quantity > copies)
                {
                    error = $"{CardDatabase.GetName(code)} 분해 가능 수량은 {copies}장입니다.";
                    return false;
                }

                int tier = (int)CardRarityCatalog.GetRarity(code);
                long gain = (long)quantity * DisassemblePointValue;
                if (next.craftPoints[tier] > long.MaxValue - gain)
                {
                    error = "제작 포인트 보유 한도를 초과합니다.";
                    return false;
                }
                next.craftPoints[tier] += gain;
                int remaining = copies - quantity;
                if (remaining == 0) owned.Remove(code);
                else owned[code] = remaining;
            }
            return Write(next, owned, out error);
        }

        private static bool CanTradeCards(out string error)
        {
            if (!CanTransact(out error)) return false;
            if (HasStarter) return true;
            error = "먼저 스타터 덱을 선택하세요.";
            return false;
        }

        public static bool IsOpponentUnlocked(int id)
        {
            if (!Initialized) return true;
            if (_profile != null) return _profile.unlockedOpponentIds.Contains(id);
            var first = OpponentOrder.FirstOrDefault();
            return first != null && first.id == id;
        }

        public static bool IsOpponentCleared(int id) => _profile?.clearedOpponentIds.Contains(id) == true;

        public static int GetUnlockedDeckCount(int opponentId)
        {
            if (!IsOpponentUnlocked(opponentId)) return 0;
            var levels = OpponentDeckLevels.GetDecks(opponentId);
            if (levels.Count == 0 || !Initialized) return levels.Count;

            var progress = FindDeckProgress(_profile, opponentId);
            int firstDeckId = levels[0].id;
            return levels.Count(deck => IsDeckLevelOpen(deck.id, firstDeckId, progress));
        }

        public static bool IsOpponentDeckUnlocked(int opponentId, int deckId)
        {
            if (!IsOpponentUnlocked(opponentId)) return false;
            var levels = OpponentDeckLevels.GetDecks(opponentId);
            if (!levels.Any(deck => deck.id == deckId)) return false;

            return !Initialized || IsDeckLevelOpen(deckId, levels[0].id, FindDeckProgress(_profile, opponentId));
        }

        public static bool IsOpponentDeckCleared(int opponentId, int deckId) =>
            FindDeckProgress(_profile, opponentId)?.clearedDeckIds.Contains(deckId) == true;

        public static uint GetDeckCopyKey(uint code)
        {
            var card = CardDatabase.Get(code);
            return card != null && card.alias != 0 ? card.alias : code;
        }

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
            if (!ValidateDeck(selected, out error)) return false;
            var owned = GetDeckCards(selected).GroupBy(code => code).ToDictionary(group => group.Key, group => group.Count());
            var next = new SavedProfile
            {
                version = ProfileVersion,
                starterId = starter.Id,
                starterDeck = selected,
                balance = StartingBalance,
                craftPoints = new long[CardRarityCatalog.TierCount],
            };
            InitializeOpponentProgress(next);
            if (!Write(next, owned, out error)) return false;
            deck = CopyDeck(selected);
            return true;
        }

        public static bool TryBuyPack(string id, out IReadOnlyList<uint> pulls, out string error)
        {
            pulls = Array.Empty<uint>();
            if (!TryBuyPacks(id, 1, out var packs, out error)) return false;
            pulls = packs[0];
            return true;
        }

        public static bool TryBuyPacks(string id, int packCount, out IReadOnlyList<IReadOnlyList<uint>> pulls, out string error)
        {
            pulls = Array.Empty<IReadOnlyList<uint>>();
            if (!CanTransact(out error)) return false;
            if (!HasStarter)
            {
                error = "먼저 스타터 덱을 선택하세요.";
                return false;
            }
            if (packCount != 1 && packCount != PackBundleSize)
            {
                error = $"팩은 1개 또는 {PackBundleSize}개씩 구매할 수 있습니다.";
                return false;
            }
            int cost = PackCost * packCount;
            if (Balance < cost)
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

            var packGroups = new List<uint>[CardRarityCatalog.TierCount];
            for (int tier = 0; tier < packGroups.Length; tier++)
                packGroups[tier] = new List<uint>();
            foreach (uint code in pack.CardPool)
                packGroups[(int)CardRarityCatalog.GetRarity(code)].Add(code);

            var owned = new Dictionary<uint, int>(_owned);
            var openedPacks = new IReadOnlyList<uint>[packCount];
            for (int packIndex = 0; packIndex < openedPacks.Length; packIndex++)
            {
                var opened = new uint[CardsPerPack];
                for (int cardIndex = 0; cardIndex < opened.Length; cardIndex++)
                {
                    var group = RollPackRarity(packGroups, PackRandom);
                    uint code = group[PackRandom.Next(group.Count)];
                    int count = owned.GetValueOrDefault(code);
                    if (count == int.MaxValue)
                    {
                        error = "카드 보유 수량을 저장할 수 있는 범위를 초과합니다.";
                        return false;
                    }

                    opened[cardIndex] = code;
                    owned[code] = count + 1;
                }
                openedPacks[packIndex] = Array.AsReadOnly(opened);
            }

            if (!Write(WithBalance(Balance - cost), owned, out error)) return false;
            pulls = Array.AsReadOnly(openedPacks);
            return true;
        }

        private static List<uint> RollPackRarity(List<uint>[] groups, System.Random random)
        {
            int weight = 0;
            for (int tier = 0; tier < groups.Length; tier++)
                if (groups[tier].Count > 0) weight += CardRarityCatalog.GetBaseWeight((CardRarity)tier);
            if (weight == 0) return null;

            int roll = random.Next(weight);
            for (int tier = 0; tier < groups.Length; tier++)
            {
                if (groups[tier].Count == 0) continue;
                roll -= CardRarityCatalog.GetBaseWeight((CardRarity)tier);
                if (roll < 0) return groups[tier];
            }
            throw new InvalidOperationException("Invalid pack rarity weights.");
        }

        public static bool TryAwardDuel(bool won, out int amount, out string error) =>
            TryAwardDuel(won, -1, out amount, out _, out error);

        public static bool TryAwardDuel(bool won, int opponentId, out int amount, out OpponentCharacter unlockedOpponent,
            out string error) =>
            TryAwardDuel(won, opponentId, null, out amount, out unlockedOpponent, out _, out error);

        public static bool TryAwardDuel(bool won, int opponentId, int deckId, out int amount,
            out OpponentCharacter unlockedOpponent, out PresetDeckInfo unlockedDeck, out string error) =>
            TryAwardDuel(won, opponentId, (int?)deckId, out amount, out unlockedOpponent, out unlockedDeck, out error);

        private static bool TryAwardDuel(bool won, int opponentId, int? deckId, out int amount,
            out OpponentCharacter unlockedOpponent, out PresetDeckInfo unlockedDeck, out string error)
        {
            amount = 0;
            unlockedOpponent = null;
            unlockedDeck = null;
            if (!CanTransact(out error)) return false;
            if (!HasStarter)
            {
                error = "먼저 스타터 덱을 선택하세요.";
                return false;
            }
            if (opponentId >= 0 && !IsOpponentUnlocked(opponentId))
            {
                error = "아직 해금하지 않은 상대입니다.";
                return false;
            }
            if (deckId.HasValue && !IsOpponentDeckUnlocked(opponentId, deckId.Value))
            {
                error = "아직 해금하지 않은 상대 덱입니다.";
                return false;
            }

            int reward = Math.Min(int.MaxValue - Balance, won ? WinReward : LossReward);
            bool firstOpponentWin = won && opponentId >= 0 && !IsOpponentCleared(opponentId);
            bool firstDeckWin = won && deckId.HasValue && !IsOpponentDeckCleared(opponentId, deckId.Value);
            if (reward == 0 && !firstOpponentWin && !firstDeckWin) return true;

            var next = WithBalance(Balance + reward);
            OpponentCharacter newlyUnlocked = null;
            if (firstOpponentWin)
            {
                next.clearedOpponentIds = new List<int>(_profile.clearedOpponentIds) { opponentId };
                newlyUnlocked = OpponentOrder.FirstOrDefault(opponent => !IsOpponentUnlocked(opponent.id));
                if (newlyUnlocked != null)
                    next.unlockedOpponentIds = new List<int>(_profile.unlockedOpponentIds) { newlyUnlocked.id };
            }
            var newlyUnlockedDeck = firstDeckWin ? RecordFirstDeckWin(next, opponentId, deckId.Value) : null;

            if (!Write(next, _owned, out error)) return false;
            amount = reward;
            unlockedOpponent = newlyUnlocked;
            unlockedDeck = newlyUnlockedDeck;
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
            craftPoints = _profile.craftPoints,
            unlockedOpponentIds = _profile.unlockedOpponentIds,
            clearedOpponentIds = _profile.clearedOpponentIds,
            opponentDeckProgress = _profile.opponentDeckProgress,
        };

        private static OpponentDeckProgress FindDeckProgress(SavedProfile profile, int opponentId) =>
            profile?.opponentDeckProgress.Find(progress => progress != null && progress.opponentId == opponentId);

        private static bool IsDeckLevelOpen(int deckId, int firstDeckId, OpponentDeckProgress progress) =>
            deckId == firstDeckId || progress?.unlockedDeckIds.Contains(deckId) == true ||
            progress?.clearedDeckIds.Contains(deckId) == true;

        private static PresetDeckInfo RecordFirstDeckWin(SavedProfile next, int opponentId, int deckId)
        {
            var previous = FindDeckProgress(next, opponentId);
            var updated = new OpponentDeckProgress
            {
                opponentId = opponentId,
                unlockedDeckIds = previous == null ? new List<int>() : new List<int>(previous.unlockedDeckIds),
                clearedDeckIds = previous == null ? new List<int>() : new List<int>(previous.clearedDeckIds),
            };
            updated.clearedDeckIds.Add(deckId);
            var levels = OpponentDeckLevels.GetDecks(opponentId);
            int firstDeckId = levels[0].id;
            var unlocked = levels.FirstOrDefault(deck => !IsDeckLevelOpen(deck.id, firstDeckId, updated));
            if (unlocked != null) updated.unlockedDeckIds.Add(unlocked.id);

            next.opponentDeckProgress = new List<OpponentDeckProgress>(next.opponentDeckProgress);
            if (previous != null)
                next.opponentDeckProgress[next.opponentDeckProgress.IndexOf(previous)] = updated;
            else
                next.opponentDeckProgress.Add(updated);
            return unlocked;
        }

        private static IReadOnlyList<OpponentCharacter> BuildOpponentOrder() => DeckRepository.Characters
            .Where(opponent => DeckRepository.GetDecksForCharacter(opponent.id).Any(deck =>
                deck.main?.Length >= 40 && deck.main.Length <= 60 &&
                (deck.extra?.Length ?? 0) <= 15 && (deck.side?.Length ?? 0) <= 15))
            .OrderBy(opponent => opponent.id == FirstOpponentId ? 0 : opponent.id == SecondOpponentId ? 1 : 2)
            .ThenBy(opponent => opponent.seriesIdx)
            .ToArray();

        private static void InitializeOpponentProgress(SavedProfile profile)
        {
            profile.clearedOpponentIds ??= new List<int>();
            profile.unlockedOpponentIds ??= new List<int>();
            profile.opponentDeckProgress ??= new List<OpponentDeckProgress>();
            if (!OpponentOrder.Any(opponent => profile.unlockedOpponentIds.Contains(opponent.id) &&
                                              !profile.clearedOpponentIds.Contains(opponent.id)))
            {
                // Keep character progression available when the roster changes.
                var next = OpponentOrder.FirstOrDefault(opponent => !profile.unlockedOpponentIds.Contains(opponent.id));
                if (next != null) profile.unlockedOpponentIds.Add(next.id);
            }

            foreach (var progress in profile.opponentDeckProgress)
            {
                if (progress == null) continue;
                progress.clearedDeckIds ??= new List<int>();
                progress.unlockedDeckIds ??= new List<int>();
                var levels = OpponentDeckLevels.GetDecks(progress.opponentId);
                if (levels.Count == 0) continue;
                int firstDeckId = levels[0].id;
                if (levels.Any(deck => IsDeckLevelOpen(deck.id, firstDeckId, progress) && !progress.clearedDeckIds.Contains(deck.id)))
                    continue;

                // Preserve recorded decks by ID while offering a path through added or reordered levels.
                var next = levels.FirstOrDefault(deck => !IsDeckLevelOpen(deck.id, firstDeckId, progress));
                if (next != null) progress.unlockedDeckIds.Add(next.id);
            }
        }

        private static bool Write(SavedProfile next, Dictionary<uint, int> owned, out string error)
        {
            string pending = null;
            error = "";
            try
            {
                string directory = Path.GetDirectoryName(_profilePath);
                Directory.CreateDirectory(directory);
                using var profileLock = SaveSyncFiles.LockProfile(_profilePath);
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
            SaveChanged?.Invoke();
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
            foreach (var archetype in StarterArchetypeCatalog.GetAll().OrderBy(archetype => archetype.name, StringComparer.Ordinal))
            {
                var (_, presetId) = StarterPresets.FirstOrDefault(entry => archetype.setcodes.Contains(entry.setcode));
                var preset = presetId != 0 ? DeckRepository.PresetDecks.Find(item => item.id == presetId) : null;
                starters.Add(new CollectionStarter(archetype, preset));
            }
            return starters.AsReadOnly();
        }

        internal static UserDeck BuildStarterDeck(string id, ArchetypeInfo archetype, PresetDeckInfo preset)
        {
            const int mainSize = 40;
            const int extraSize = 15;
            var deck = new UserDeck { id = "collection_" + id, name = archetype.name + " 스타터" };
            if (preset != null)
            {
                deck.main.AddRange(preset.main ?? Array.Empty<uint>());
                deck.extra.AddRange(preset.extra ?? Array.Empty<uint>());
                deck.side.AddRange(preset.side ?? Array.Empty<uint>());
                if (ValidateDeck(deck, out _)) return deck;
                deck.main.Clear();
                deck.extra.Clear();
                deck.side.Clear();
            }

            var counts = new Dictionary<uint, int>();
            bool AddCard(List<uint> pile, int size, uint code, bool extraPile)
            {
                var card = CardDatabase.Get(code);
                if (pile.Count >= size || !IsDeckCard(card) || card.IsExtra != extraPile) return false;
                uint key = GetDeckCopyKey(code);
                counts.TryGetValue(key, out int count);
                if (count >= DeckCopyLimit) return false;
                counts[key] = count + 1;
                pile.Add(code);
                return true;
            }

            var (main, extra) = CardDatabase.BuildPureArchetypeDeck(archetype);
            var ace = CardDatabase.Get(archetype.representativeCode);
            if (IsDeckCard(ace))
            {
                var generated = ace.IsExtra ? extra : main;
                if (!generated.Contains(ace.code))
                    AddCard(ace.IsExtra ? deck.extra : deck.main, ace.IsExtra ? extraSize : mainSize, ace.code, ace.IsExtra);
            }

            foreach (uint code in extra) AddCard(deck.extra, extraSize, code, extraPile: true);

            var theme = new HashSet<uint>(archetype.coreCards.Concat(archetype.supportCards).Select(card => card.code))
            {
                24094653, // 융합, added by the existing generator when needed
            };
            foreach (uint code in main)
                if (theme.Contains(code)) AddCard(deck.main, mainSize, code, extraPile: false);

            // Sparse themes can otherwise get a deck with many spells and too few monsters they can Normal Summon.
            int normalSummons = deck.main.Count(code => IsStarterNormalSummon(CardDatabase.Get(code)));
            if (normalSummons < 9 && deck.main.Count < mainSize)
            {
                var monsters = CardDatabase.AllCards.Where(card => IsStarterNormalSummon(card) && card.alias == 0 &&
                    (card.type & YgoConstants.TYPE_NORMAL) != 0 && card.atk >= 1600)
                    .OrderByDescending(card => card.race == ace?.race)
                    .ThenByDescending(card => card.attr == ace?.attr)
                    .ThenByDescending(card => card.atk)
                    .ThenBy(card => card.code);
                foreach (var card in monsters)
                {
                    while (normalSummons < 9 && AddCard(deck.main, mainSize, card.code, extraPile: false)) normalSummons++;
                    if (normalSummons >= 9 || deck.main.Count == mainSize) break;
                }
            }

            foreach (uint code in main)
                if (!theme.Contains(code)) AddCard(deck.main, mainSize, code, extraPile: false);

            foreach (uint code in SupportCodes)
            {
                if (deck.main.Count == mainSize) break;
                while (AddCard(deck.main, mainSize, code, extraPile: false)) { }
            }

            return deck;
        }

        private static bool IsStarterNormalSummon(CardInfo card) => IsDeckCard(card) && card.IsMonster &&
            !card.IsExtra && card.level is >= 1 and <= 4 &&
            (card.type & (YgoConstants.TYPE_RITUAL | YgoConstants.TYPE_SPSUMMON)) == 0;

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
                if (group.Count() <= DeckCopyLimit) continue;
                reason = $"{CardDatabase.GetName(group.Key)}은(는) 덱에 {DeckCopyLimit}장까지 넣을 수 있습니다.";
                return false;
            }
            reason = "";
            return true;
        }
    }
}
