using System;
using System.Collections.Generic;
using System.Globalization;
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
        internal bool IsSecret => ThemeCards != null;
        internal IReadOnlyCollection<uint> ThemeCards { get; }

        internal CollectionPack(string id, string name, uint featuredCode, uint[] codes, IReadOnlyCollection<uint> themeCards = null)
        {
            Id = id;
            Name = name;
            FeaturedCode = codes.Contains(featuredCode) ? featuredCode : codes[0];
            CardPool = Array.AsReadOnly(codes);
            ThemeCards = themeCards;
        }
    }

    internal readonly struct PackPull
    {
        internal readonly uint Code;
        internal readonly bool Overframe;
        internal readonly int OwnedCopies;
        internal bool IsNew => OwnedCopies == 1;

        internal PackPull(uint code, bool overframe, int ownedCopies)
        {
            Code = code;
            Overframe = overframe;
            OwnedCopies = ownedCopies;
        }
    }

    public static partial class CollectionProgress
    {
        public const int PackCost = 100;
        public const int PackBundleSize = 10;
        public const int CardsPerPack = 8;
        public const int WinReward = 300;
        public const int LossReward = 150;
        public const int StartingBalance = 5_000;
        public const int DeckCopyLimit = 3;

        private const int SharedDustVersion = 6;
        private const int ProfileVersion = 9;
        private const int OverframePullChance = 15;
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
            // Store exact integers even when the four legacy balances sum to more than Int64.MaxValue.
            public string craftDust;
            [NonSerialized] internal decimal dust;
            public string starterId;
            public UserDeck starterDeck;
            public string lastDuelDeckId;
            public List<CardCopies> cards;
            public List<CardCopies> overframeCards;
            public List<int> unlockedOpponentIds;
            public List<int> clearedOpponentIds;
            public List<OpponentDeckProgress> opponentDeckProgress;
            public List<string> clearedStoryEpisodes = new();
            public bool storyProgressMigrated;
            public SecretShopSave shop;
            public StoryTacticsSave tactics = new();
            public DimensionTournament tournament;
            public int tournamentWins;
        }

        [Serializable]
        private sealed class LegacyCraftBalances
        {
            public long[] craftPoints;
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
        private static Dictionary<uint, int> _ownedOverframes = new Dictionary<uint, int>();
        private static List<uint> _bundleBonusPool;
        private static IReadOnlyList<CollectionStarter> _starters;
        private static IReadOnlyList<CollectionPack> _packs;
        private static IReadOnlyList<OpponentCharacter> _opponents;

        public static bool Initialized => _profilePath != null;
        internal static bool AllDeckCardsAvailable { get; set; } = false;
        internal static bool RequiresOwnedCardsForDecks => Initialized && !AllDeckCardsAvailable;
        internal static event Action SaveChanged;
        public static bool HasStarter => _profile?.starterDeck != null;
        public static int Balance => _profile?.balance ?? 0;
        public static string LoadError { get; private set; } = "";
        public static IReadOnlyList<CollectionStarter> Starters => _starters ??= BuildStarters();
        public static IReadOnlyList<CollectionPack> Packs => _packs ??= BuildPacks();
        public static int OpponentCount => AvailableOpponents.Count;
        public static int UnlockedOpponentCount => OpponentCount;
        internal static string LastDuelDeckId => _profile?.lastDuelDeckId;

        internal static IReadOnlyList<OpponentCharacter> AvailableOpponents =>
            Initialized ? _opponents ??= BuildOpponentOrder() : BuildOpponentOrder();

        public static void Load(string persistentDataPath)
        {
            _profilePath = Path.Combine(persistentDataPath, ProfileFileName);
            _starters = null;
            _packs = null;
            _secretPacksByCard = null;
            _opponents = null;
            _profile = null;
            _savedJson = null;
            _owned = new Dictionary<uint, int>();
            _ownedOverframes = new Dictionary<uint, int>();
            _bundleBonusPool = null;
            LoadError = "";
            try
            {
                // Reading directly distinguishes an absent profile from an inaccessible one.
                string json = ReadProfileJson();
                if (json == null) return;
                var saved = ParseProfile(json, out var owned, out var overframes);
                InitializeOpponentProgress(saved);
                saved.tactics ??= new StoryTacticsSave();
                saved.tactics.bonds ??= new List<StoryBond>();
                saved.tactics.supports ??= new int[RaidSupportCatalog.Items.Length];
                if (saved.shop == null)
                {
                    saved.shop = new SecretShopSave();
                    UnlockSecretPacks(saved, owned.Keys.Concat(overframes.Keys));
                }
                _profile = saved;
                _owned = owned;
                _ownedOverframes = overframes;
                _savedJson = json;
            }
            catch (DirectoryNotFoundException)
            {
                // The first write creates the profile directory.
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                ex is ArgumentException || ex is InvalidDataException)
            {
                LoadError = "카드 보유 정보를 불러올 수 없습니다. 기존 수집 데이터는 보존했습니다.";
                Debug.LogWarning($"[CollectionProgress] Could not load profile: {ex.Message}");
            }
        }

        internal static void ValidateSaveProfile(string json) => ParseProfile(json, out _, out _);

        private static SavedProfile ParseProfile(string json, out Dictionary<uint, int> owned,
            out Dictionary<uint, int> overframes)
        {
            var saved = JsonUtility.FromJson<SavedProfile>(json);
            if (saved == null || saved.version < 1 || saved.version > ProfileVersion || saved.balance < 0 ||
                saved.cards == null || !IsStarterId(saved.starterId) || saved.starterDeck == null ||
                saved.starterDeck.id != "collection_" + saved.starterId ||
                saved.starterDeck.main == null || saved.starterDeck.extra == null || saved.starterDeck.side == null)
                throw new InvalidDataException("Invalid collection profile.");
            if (saved.tactics?.supports is { } supports &&
                (supports.Length != RaidSupportCatalog.Items.Length || supports.Any(count => count < 0)))
                throw new InvalidDataException("Invalid raid support inventory.");
            if (saved.tournament?.IsEmpty == true) saved.tournament = null;
            if (saved.tournamentWins < 0 || (saved.tournament != null && !saved.tournament.IsValid) ||
                saved.shop?.freeSecretBundles < 0)
                throw new InvalidDataException("Invalid tournament progress.");

            if (saved.version < SharedDustVersion)
            {
                var legacy = JsonUtility.FromJson<LegacyCraftBalances>(json)?.craftPoints;
                if (legacy != null)
                {
                    if (legacy.Length != CardRarityCatalog.TierCount || legacy.Any(points => points < 0))
                        throw new InvalidDataException("Invalid craft point balances.");
                    saved.dust = legacy.Sum(points => (decimal)points);
                }
            }
            else if (!decimal.TryParse(saved.craftDust, NumberStyles.None, CultureInfo.InvariantCulture, out saved.dust))
            {
                throw new InvalidDataException("Invalid craft dust balance.");
            }

            owned = ReadOwnership(saved.cards);
            overframes = ReadOwnership(saved.overframeCards);
            if (saved.shop is { } shop && (shop.mileage < 0 || shop.mileage >= PackMileageTarget || shop.urTickets < 0))
                throw new InvalidDataException("Invalid shop mileage.");
            return saved;
        }

        private static Dictionary<uint, int> ReadOwnership(List<CardCopies> entries)
        {
            var result = new Dictionary<uint, int>(entries?.Count ?? 0);
            if (entries == null) return result;
            foreach (var entry in entries)
            {
                if (entry.code == 0 || entry.count < 1 || !result.TryAdd(entry.code, entry.count))
                    throw new InvalidDataException("Invalid card ownership entry.");
            }
            return result;
        }

        public static int GetOwnedCount(uint code) => _owned.GetValueOrDefault(code);
        internal static int GetOwnedOverframeCount(uint code) => _ownedOverframes.GetValueOrDefault(code);
        internal static int GetTotalOwnedCount(uint code) => CombinedCount(GetOwnedCount(code), GetOwnedOverframeCount(code));
        private static int CombinedCount(int regular, int overframes) => (int)Math.Min(int.MaxValue, (long)regular + overframes);

        public static decimal CraftDust => _profile?.dust ?? 0;

        public static int GetCraftDustCost(CardRarity rarity) => GetCraftDustCost(rarity, overframe: false);

        internal static int GetCraftDustCost(CardRarity rarity, bool overframe)
        {
            int regular = rarity switch
            {
                CardRarity.Rare => 100,
                CardRarity.SuperRare => 300,
                CardRarity.UltraRare => 900,
                _ => 30,
            };
            return overframe ? regular * 2 : regular;
        }

        public static int GetDisassemblyDustPerCard(uint code) => GetDisassemblyDustPerCard(code, overframe: false);

        internal static int GetDisassemblyDustPerCard(uint code, bool overframe) =>
            (CardRarityCatalog.GetRarity(code), overframe) switch
            {
                (CardRarity.Rare, false) => 20,
                (CardRarity.SuperRare, false) => 80,
                (CardRarity.UltraRare, false) => 250,
                (CardRarity.Rare, true) => 50,
                (CardRarity.SuperRare, true) => 200,
                (CardRarity.UltraRare, true) => 600,
                (_, true) => 15,
                _ => 5,
            };

        public static bool IsCraftableCard(CardInfo card) => IsDeckCard(card);

        public static int GetCraftLimit(uint code) => GetCraftLimit(code, overframe: false);

        internal static int GetCraftLimit(uint code, bool overframe) => HasStarter &&
            IsCraftableCard(CardDatabase.Get(code)) && (!overframe || CardArtLoader.SupportsOverframe(code))
            ? int.MaxValue - (overframe ? GetOwnedOverframeCount(code) : GetOwnedCount(code)) : 0;

        public static int GetDisassembleLimit(uint code) => GetDisassembleLimit(code, overframe: false);

        internal static int GetDisassembleLimit(uint code, bool overframe) => HasStarter && IsCraftableCard(CardDatabase.Get(code))
            ? (overframe ? GetOwnedOverframeCount(code) : GetOwnedCount(code)) : 0;

        public static IReadOnlyDictionary<uint, int> GetDisassembleLimits() => HasStarter
            ? _owned.Where(entry => IsCraftableCard(CardDatabase.Get(entry.Key)))
                .ToDictionary(entry => entry.Key, entry => entry.Value)
            : new Dictionary<uint, int>();

        public static bool TryCraftCard(uint code, int quantity, out string error) =>
            TryCraftCard(code, quantity, overframe: false, out error);

        internal static bool TryCraftCard(uint code, int quantity, bool overframe, out string error)
        {
            if (!CanTradeCards(out error)) return false;
            if (quantity <= 0 || !IsCraftableCard(CardDatabase.Get(code)) ||
                (overframe && !CardArtLoader.SupportsOverframe(code)))
            {
                error = "제작할 카드와 수량을 확인하세요.";
                return false;
            }
            if (quantity > GetCraftLimit(code, overframe))
            {
                error = "카드 보유 수량을 저장할 수 있는 범위를 초과합니다.";
                return false;
            }

            var rarity = CardRarityCatalog.GetRarity(code);
            long cost = (long)quantity * GetCraftDustCost(rarity, overframe);
            if (CraftDust < cost)
            {
                error = $"제작에 가루 {cost:N0}개가 필요합니다.";
                return false;
            }

            var copies = overframe ? _ownedOverframes : _owned;
            var updated = new Dictionary<uint, int>(copies) { [code] = copies.GetValueOrDefault(code) + quantity };
            var next = WithBalance(Balance);
            next.dust -= cost;
            UnlockSecretPacks(next, new[] { code });
            return Write(next, overframe ? _owned : updated, out error, overframe ? updated : null);
        }

        public static bool TryDisassembleCards(IReadOnlyDictionary<uint, int> quantities, out string error) =>
            TryDisassembleCards(quantities, overframe: false, out error);

        internal static bool TryDisassembleCards(IReadOnlyDictionary<uint, int> quantities, bool overframe, out string error) =>
            TryDisassembleCards(overframe ? null : quantities, overframe ? quantities : null, out error);

        internal static bool TryDisassembleCards(IReadOnlyDictionary<uint, int> regular,
            IReadOnlyDictionary<uint, int> special, out string error)
        {
            if (!CanTradeCards(out error)) return false;
            if ((regular == null || regular.Count == 0) && (special == null || special.Count == 0))
            {
                error = "분해할 카드를 선택하세요.";
                return false;
            }

            var owned = new Dictionary<uint, int>(_owned);
            var overframes = new Dictionary<uint, int>(_ownedOverframes);
            var next = WithBalance(Balance);
            if (!ApplyDisassembly(regular, owned, next, overframe: false, out error) ||
                !ApplyDisassembly(special, overframes, next, overframe: true, out error)) return false;
            return Write(next, owned, out error, overframes);
        }

        private static bool ApplyDisassembly(IReadOnlyDictionary<uint, int> quantities, Dictionary<uint, int> owned,
            SavedProfile next, bool overframe, out string error)
        {
            error = "";
            if (quantities == null) return true;
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

                long gain = (long)quantity * GetDisassemblyDustPerCard(code, overframe);
                if (next.dust > decimal.MaxValue - gain)
                {
                    error = "가루 보유 한도를 초과합니다.";
                    return false;
                }
                next.dust += gain;
                int remaining = copies - quantity;
                if (remaining == 0) owned.Remove(code);
                else owned[code] = remaining;
            }
            return true;
        }

        private static bool CanTradeCards(out string error)
        {
            if (!CanTransact(out error)) return false;
            if (HasStarter) return true;
            error = "먼저 스타터 덱을 선택하세요.";
            return false;
        }

        public static bool IsOpponentUnlocked(int id) => AvailableOpponents.Any(opponent => opponent.id == id);

        public static bool IsOpponentCleared(int id) => _profile?.clearedOpponentIds.Contains(id) == true;

        public static int GetUnlockedDeckCount(int opponentId) =>
            IsOpponentUnlocked(opponentId) ? OpponentDeckLevels.GetDecks(opponentId).Count : 0;

        public static bool IsOpponentDeckUnlocked(int opponentId, int deckId) =>
            IsOpponentUnlocked(opponentId) && OpponentDeckLevels.GetDecks(opponentId).Any(deck => deck.id == deckId);

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
            if (AllDeckCardsAvailable) return true;

            foreach (var copies in deck.Copies().GroupBy(copy => (copy.Code, copy.Appearance.overframe)))
            {
                int needed = copies.Count();
                var (code, overframe) = copies.Key;
                int owned = overframe ? GetOwnedOverframeCount(code) : GetOwnedCount(code);
                if (owned >= needed) continue;
                string edition = overframe ? " 오버프레임" : "";
                reason = $"{CardDatabase.GetName(code)}{edition} 보유 {owned}장 / 필요 {needed}장";
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
                shop = new SecretShopSave(),
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
            if (!TryBuyPackContents(id, packCount, out var packs, out error)) return false;
            pulls = packs.Select(pack => (IReadOnlyList<uint>)pack.Select(card => card.Code).ToArray()).ToArray();
            return true;
        }

        internal static bool TryBuyPackContents(string id, int packCount, out IReadOnlyList<IReadOnlyList<PackPull>> pulls,
            out string error, bool useFreeSecretBundle = false)
        {
            pulls = Array.Empty<IReadOnlyList<PackPull>>();
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
            int cost = useFreeSecretBundle ? 0 : PackCost * packCount;
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
            if (!IsPackUnlocked(pack))
            {
                error = "해당 카드군의 SR 또는 UR 카드를 얻으면 시크릿 팩이 열립니다.";
                return false;
            }
            if (useFreeSecretBundle && (!pack.IsSecret || packCount != PackBundleSize || FreeSecretBundles <= 0))
            {
                error = "해금된 시크릿 팩에서만 무료 10팩권을 사용할 수 있습니다.";
                return false;
            }

            var packGroups = new List<uint>[CardRarityCatalog.TierCount];
            for (int tier = 0; tier < packGroups.Length; tier++)
                packGroups[tier] = new List<uint>();
            foreach (uint code in pack.CardPool)
                packGroups[(int)CardRarityCatalog.GetRarity(code)].Add(code);

            var owned = new Dictionary<uint, int>(_owned);
            var overframes = new Dictionary<uint, int>(_ownedOverframes);
            var openedPacks = new IReadOnlyList<PackPull>[packCount];
            bool bundle = packCount == PackBundleSize;
            bool requireUltraRare = bundle && NextBundleGuaranteesUr;
            CardRarity guarantee = requireUltraRare ? CardRarity.UltraRare : CardRarity.SuperRare;
            bool guaranteedRarityFound = false;
            bool pulledUltraRare = false;
            for (int packIndex = 0; packIndex < openedPacks.Length; packIndex++)
            {
                var opened = new PackPull[CardsPerPack];
                for (int cardIndex = 0; cardIndex < opened.Length; cardIndex++)
                {
                    bool bonus = bundle && !guaranteedRarityFound &&
                        packIndex == packCount - 1 && cardIndex == opened.Length - 1;
                    var group = RollPackRarity(packGroups, PackRandom, bonus ? guarantee : CardRarity.Normal);
                    if (group == null && bonus)
                        group = requireUltraRare
                            ? HighRarityPool.Where(code => CardRarityCatalog.GetRarity(code) == CardRarity.UltraRare).ToList()
                            : HighRarityPool;
                    if (group == null || group.Count == 0)
                    {
                        error = "팩을 개봉할 수 없습니다. 수록 카드 정보를 확인하세요.";
                        return false;
                    }

                    uint code = group[PackRandom.Next(group.Count)];
                    var rarity = CardRarityCatalog.GetRarity(code);
                    guaranteedRarityFound |= rarity >= guarantee;
                    pulledUltraRare |= rarity == CardRarity.UltraRare;
                    bool overframe = CardArtLoader.SupportsOverframe(code) && PackRandom.Next(100) < OverframePullChance;
                    var inventory = overframe ? overframes : owned;
                    int count = inventory.GetValueOrDefault(code);
                    if (count == int.MaxValue)
                    {
                        error = "카드 보유 수량을 저장할 수 있는 범위를 초과합니다.";
                        return false;
                    }

                    inventory[code] = count + 1;
                    opened[cardIndex] = new PackPull(code, overframe, CombinedCount(owned.GetValueOrDefault(code),
                        overframes.GetValueOrDefault(code)));
                }
                openedPacks[packIndex] = Array.AsReadOnly(opened);
            }

            var next = WithBalance(Balance - cost);
            RecordPackOpening(next, packCount, bundle, pulledUltraRare);
            if (useFreeSecretBundle) next.shop.freeSecretBundles--;
            UnlockSecretPacks(next, openedPacks.SelectMany(opened => opened).Select(card => card.Code));
            if (!Write(next, owned, out error, overframes)) return false;
            pulls = Array.AsReadOnly(openedPacks);
            return true;
        }

        private static List<uint> RollPackRarity(List<uint>[] groups, System.Random random, CardRarity minimum)
        {
            int weight = 0;
            for (int tier = (int)minimum; tier < groups.Length; tier++)
                if (groups[tier].Count > 0) weight += CardRarityCatalog.GetBaseWeight((CardRarity)tier);
            if (weight == 0) return null;

            int roll = random.Next(weight);
            for (int tier = (int)minimum; tier < groups.Length; tier++)
            {
                if (groups[tier].Count == 0) continue;
                roll -= CardRarityCatalog.GetBaseWeight((CardRarity)tier);
                if (roll < 0) return groups[tier];
            }
            throw new InvalidOperationException("Invalid pack rarity weights.");
        }

        public static bool TryAwardDuel(bool won, out int amount, out string error) =>
            TryAwardDuel(won, -1, null, out amount, out error);

        public static bool TryAwardDuel(bool won, int opponentId, int deckId, out int amount, out string error) =>
            TryAwardDuel(won, opponentId, (int?)deckId, out amount, out error);

        internal static bool IsStoryEpisodeCleared(string episodeId) =>
            _profile?.clearedStoryEpisodes?.Contains(episodeId) == true;

        internal static void MigrateStoryClears(IEnumerable<string> legacyClears)
        {
            if (!HasStarter || _profile.storyProgressMigrated || !CanTransact(out _)) return;
            var cleared = _profile.clearedStoryEpisodes.Concat(legacyClears).Distinct(StringComparer.Ordinal).ToList();
            if (cleared.Count == 0) return;
            var next = WithBalance(Balance);
            next.clearedStoryEpisodes = cleared;
            next.storyProgressMigrated = true;
            if (!Write(next, _owned, out string error))
                Debug.LogWarning($"[Collection] 스토리 진행도 이전: {error}");
        }

        private static List<uint> HighRarityPool => _bundleBonusPool ??= CardDatabase.AllCards
            .Where(card => IsDeckCard(card) && CardRarityCatalog.GetRarity(card.code) >= CardRarity.SuperRare)
            .Select(card => card.code).ToList();

        internal static bool TryAwardStoryDuel(string episodeId, bool won, StoryRaid? raid, PresetDeckInfo opponentDeck,
            out int amount, out PackPull? awardedCard, out string error)
        {
            bool saved = TryAwardStoryDuel(episodeId, won, raid, opponentDeck, default, out var prize, out error);
            amount = prize.Dp;
            awardedCard = prize.RaidCard;
            return saved;
        }

        internal static bool TryAwardStoryDuel(string episodeId, bool won, StoryRaid? raid, PresetDeckInfo opponentDeck,
            StoryBattlePlan plan, out StoryPrize prize, out string error, System.Random random = null)
        {
            prize = default;
            random ??= PackRandom;
            if (!CanTransact(out error)) return false;
            if (!HasStarter || string.IsNullOrWhiteSpace(episodeId))
            {
                error = "스토리 듀얼 정보를 확인할 수 없습니다.";
                return false;
            }

            int reward = Math.Min(int.MaxValue - Balance, won ? plan.ScaleWinReward(raid?.WinDp ?? WinReward) : LossReward);
            int dust = won ? plan.ScaleWinReward(raid.HasValue ? 100 : 20) : 0;
            bool firstClear = won && !IsStoryEpisodeCleared(episodeId);
            var owned = _owned;
            var overframes = _ownedOverframes;
            PackPull? bonus = null;
            if (firstClear && raid is { } challenge)
            {
                var candidates = new[] { challenge.Opening.FieldCard, opponentDeck.sigCard }
                    .Concat(opponentDeck.main ?? Array.Empty<uint>()).Concat(opponentDeck.extra ?? Array.Empty<uint>())
                    .Where(code => IsDeckCard(CardDatabase.Get(code)) &&
                        CardRarityCatalog.GetRarity(code) >= CardRarity.SuperRare && GetOwnedCount(code) < int.MaxValue)
                    .Distinct().OrderBy(code => GetTotalOwnedCount(code))
                    .ThenByDescending(code => CardRarityCatalog.GetRarity(code)).ToArray();
                uint code = candidates.FirstOrDefault();
                if (code == 0) code = HighRarityPool.FirstOrDefault(candidate => GetOwnedCount(candidate) < int.MaxValue);
                if (code == 0)
                {
                    error = "보스 보상을 보관할 공간이 부족합니다.";
                    return false;
                }
                bonus = AwardCard(code);
            }
            PackPull? theme = null;
            if (won && opponentDeck != null && random.Next(100) < plan.ThemeDropChance)
            {
                var candidates = new[] { opponentDeck.sigCard }.Concat(opponentDeck.extra ?? Array.Empty<uint>())
                    .Concat(opponentDeck.main ?? Array.Empty<uint>()).Distinct()
                    .Where(code => owned.GetValueOrDefault(code) < int.MaxValue && IsDeckCard(CardDatabase.Get(code)))
                    .OrderByDescending(code => code == opponentDeck.sigCard)
                    .ThenByDescending(code => CardRarityCatalog.GetRarity(code)).Take(5).ToArray();
                if (candidates.Length > 0) theme = AwardCard(candidates[random.Next(candidates.Length)]);
            }
            var next = WithBalance(Balance + reward);
            next.dust += dust;
            next.tactics = next.tactics.Copy();
            int bond = won && plan.Stance == StoryStance.Bond && opponentDeck != null
                ? IncreaseStoryBond(next, opponentDeck.charId) : 0;
            RaidSupport drop = won && !raid.HasValue ? TryRollSupport(next, random) : RaidSupport.None;
            if (firstClear)
                next.clearedStoryEpisodes = new List<string>(_profile.clearedStoryEpisodes) { episodeId };
            next.storyProgressMigrated = true;
            UnlockSecretPacks(next, new[] { bonus?.Code ?? 0, theme?.Code ?? 0 }.Where(code => code != 0));
            if (!Write(next, owned, out error, overframes)) return false;
            prize = new StoryPrize(reward, dust, bonus, theme, drop, bond);
            return true;

            PackPull AwardCard(uint code)
            {
                bool overframe = CardArtLoader.SupportsOverframe(code) && overframes.GetValueOrDefault(code) < int.MaxValue &&
                    random.Next(100) < OverframePullChance;
                if (overframe && ReferenceEquals(overframes, _ownedOverframes)) overframes = new Dictionary<uint, int>(_ownedOverframes);
                if (!overframe && ReferenceEquals(owned, _owned)) owned = new Dictionary<uint, int>(_owned);
                var inventory = overframe ? overframes : owned;
                inventory[code] = inventory.GetValueOrDefault(code) + 1;
                return new PackPull(code, overframe, CombinedCount(owned.GetValueOrDefault(code), overframes.GetValueOrDefault(code)));
            }
        }

        private static bool TryAwardDuel(bool won, int opponentId, int? deckId, out int amount, out string error)
        {
            amount = 0;
            if (!CanTransact(out error)) return false;
            if (!HasStarter)
            {
                error = "먼저 스타터 덱을 선택하세요.";
                return false;
            }
            if (opponentId >= 0 && !IsOpponentUnlocked(opponentId))
            {
                error = "상대 목록에 없는 상대입니다.";
                return false;
            }
            if (deckId.HasValue && !IsOpponentDeckUnlocked(opponentId, deckId.Value))
            {
                error = "선택한 상대의 덱 목록에 없는 덱입니다.";
                return false;
            }

            int reward = Math.Min(int.MaxValue - Balance, won ? WinReward : LossReward);
            bool firstOpponentWin = won && opponentId >= 0 && !IsOpponentCleared(opponentId);
            bool firstDeckWin = won && deckId.HasValue && !IsOpponentDeckCleared(opponentId, deckId.Value);
            if (reward == 0 && !firstOpponentWin && !firstDeckWin) return true;

            var next = WithBalance(Balance + reward);
            if (firstOpponentWin)
                next.clearedOpponentIds = new List<int>(_profile.clearedOpponentIds) { opponentId };
            if (firstDeckWin) RecordFirstDeckWin(next, opponentId, deckId.Value);

            if (!Write(next, _owned, out error)) return false;
            amount = reward;
            return true;
        }

        internal static void RememberDuelDeck(string deckId)
        {
            if (!HasStarter || string.IsNullOrEmpty(deckId) || deckId == LastDuelDeckId || !CanTransact(out _)) return;
            var next = WithBalance(Balance);
            next.lastDuelDeckId = deckId;
            if (!Write(next, _owned, out string error)) Debug.LogWarning($"[Collection] 마지막 대전 덱 저장: {error}");
        }

        internal static UserDeck CopyDeck(UserDeck deck) => new UserDeck
        {
            id = deck.id,
            name = deck.name,
            main = new List<uint>(deck.main),
            extra = new List<uint>(deck.extra),
            side = new List<uint>(deck.side),
            mainAppearances = new List<DeckCardAppearance>(deck.Appearances(deck.main)),
            extraAppearances = new List<DeckCardAppearance>(deck.Appearances(deck.extra)),
            sideAppearances = new List<DeckCardAppearance>(deck.Appearances(deck.side)),
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
            lastDuelDeckId = _profile.lastDuelDeckId,
            balance = balance,
            dust = _profile.dust,
            unlockedOpponentIds = _profile.unlockedOpponentIds,
            clearedOpponentIds = _profile.clearedOpponentIds,
            opponentDeckProgress = _profile.opponentDeckProgress,
            clearedStoryEpisodes = _profile.clearedStoryEpisodes,
            storyProgressMigrated = _profile.storyProgressMigrated,
            shop = _profile.shop,
            tactics = _profile.tactics,
            tournament = _profile.tournament,
            tournamentWins = _profile.tournamentWins,
        };

        private static OpponentDeckProgress FindDeckProgress(SavedProfile profile, int opponentId) =>
            profile?.opponentDeckProgress.Find(progress => progress != null && progress.opponentId == opponentId);

        private static void RecordFirstDeckWin(SavedProfile next, int opponentId, int deckId)
        {
            var previous = FindDeckProgress(next, opponentId);
            var updated = new OpponentDeckProgress
            {
                opponentId = opponentId,
                unlockedDeckIds = previous?.unlockedDeckIds ?? new List<int>(),
                clearedDeckIds = previous == null ? new List<int>() : new List<int>(previous.clearedDeckIds),
            };
            updated.clearedDeckIds.Add(deckId);
            next.opponentDeckProgress = new List<OpponentDeckProgress>(next.opponentDeckProgress);
            if (previous != null)
                next.opponentDeckProgress[next.opponentDeckProgress.IndexOf(previous)] = updated;
            else
                next.opponentDeckProgress.Add(updated);
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
            profile.clearedStoryEpisodes ??= new List<string>();
            foreach (var progress in profile.opponentDeckProgress)
            {
                if (progress == null) continue;
                progress.clearedDeckIds ??= new List<int>();
                progress.unlockedDeckIds ??= new List<int>();
            }
        }

        private static bool Write(SavedProfile next, Dictionary<uint, int> owned, out string error,
            Dictionary<uint, int> overframes = null)
        {
            overframes ??= _ownedOverframes;
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
                next.cards = SerializeOwnership(owned);
                next.overframeCards = SerializeOwnership(overframes);
                next.craftDust = next.dust.ToString(CultureInfo.InvariantCulture);
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
            var priorShop = _profile?.shop;
            _profile = next;
            _owned = owned;
            _ownedOverframes = overframes;
            SaveChanged?.Invoke();
            NotifySecretPacks(priorShop, next.shop);
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

            void AddPack(string id, string name, uint featured, IEnumerable<uint> codes, IReadOnlyCollection<uint> themeCards = null)
            {
                var pool = codes.Distinct().ToArray();
                if (pool.Length == 0) return;
                packs.Add(new CollectionPack(id, name, featured, pool, themeCards));
                packed.UnionWith(pool);
            }

            AddPack("support-staples", "범용 지원 팩", 55144522, SupportCodes.Where(code => IsDeckCard(CardDatabase.Get(code))));
            foreach (var archetype in CardDatabase.GetAllArchetypes().OrderBy(archetype => archetype.name, StringComparer.Ordinal))
            {
                string id = "archetype-" + archetype.setcodes.Min().ToString("X4");
                var cards = archetype.coreCards.Concat(archetype.supportCards).Where(IsDeckCard).Select(card => card.code);
                AddPack(id, archetype.name + " 시크릿 팩", archetype.representativeCode, cards,
                    archetype.coreCards.Where(IsDeckCard).Select(card => card.code).ToArray());
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
            AddPack("general-master", "마스터 팩", 46986414, CardDatabase.AllCards.Where(IsDeckCard).Select(card => card.code));
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

        private static List<CardCopies> SerializeOwnership(Dictionary<uint, int> inventory) =>
            inventory.OrderBy(pair => pair.Key).Select(pair => new CardCopies { code = pair.Key, count = pair.Value }).ToList();

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
