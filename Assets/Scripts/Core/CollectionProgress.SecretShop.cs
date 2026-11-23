using System;
using System.Collections.Generic;
using System.Linq;

namespace YGOPortable.Core
{
    public static partial class CollectionProgress
    {
        [Serializable]
        private sealed class SecretShopSave
        {
            public List<string> unlockedPacks = new();
            public bool nextBundleUr;
            public int mileage;
            public int urTickets;
            public int freeSecretBundles;

            internal SecretShopSave Copy() => new()
            {
                unlockedPacks = new List<string>(unlockedPacks ?? new List<string>()),
                nextBundleUr = nextBundleUr,
                mileage = mileage,
                urTickets = urTickets,
                freeSecretBundles = freeSecretBundles,
            };
        }

        internal const int PackMileageTarget = 100;
        internal static int PackMileage => _profile?.shop?.mileage ?? 0;
        internal static int PackUrTickets => _profile?.shop?.urTickets ?? 0;
        internal static bool NextBundleGuaranteesUr => _profile?.shop?.nextBundleUr == true;
        internal static event Action<IReadOnlyList<CollectionPack>> SecretPacksUnlocked;
        private static Dictionary<uint, CollectionPack[]> _secretPacksByCard;

        internal static bool IsPackUnlocked(CollectionPack pack) =>
            pack != null && (!pack.IsSecret || _profile?.shop?.unlockedPacks?.Contains(pack.Id) == true);

        internal static IReadOnlyList<CollectionPack> SecretPacksForCard(uint code)
        {
            _secretPacksByCard ??= Packs.Where(pack => pack.IsSecret).SelectMany(pack => pack.ThemeCards.Select(card =>
                    (Key: CardDatabase.GetDeckPrintingKey(card), Pack: pack)))
                .GroupBy(entry => entry.Key).ToDictionary(group => group.Key, group => group.Select(entry => entry.Pack).Distinct().ToArray());
            return _secretPacksByCard.GetValueOrDefault(CardDatabase.GetDeckPrintingKey(code)) ?? Array.Empty<CollectionPack>();
        }

        private static void UnlockSecretPacks(SavedProfile state, IEnumerable<uint> acquired)
        {
            var unlocked = state.shop?.unlockedPacks;
            string[] earned = acquired.Distinct().Where(card => CardRarityCatalog.GetRarity(card) >= CardRarity.SuperRare)
                .SelectMany(SecretPacksForCard).Select(pack => pack.Id).Distinct(StringComparer.Ordinal)
                .Where(id => unlocked == null || !unlocked.Contains(id)).ToArray();
            if (earned.Length == 0) return;
            state.shop = state.shop?.Copy() ?? new SecretShopSave();
            state.shop.unlockedPacks.AddRange(earned);
        }

        private static void NotifySecretPacks(SecretShopSave prior, SecretShopSave current)
        {
            if (SecretPacksUnlocked == null || current?.unlockedPacks == null || ReferenceEquals(prior, current)) return;
            var before = prior?.unlockedPacks;
            var added = current.unlockedPacks.Where(id => before == null || !before.Contains(id)).ToHashSet(StringComparer.Ordinal);
            if (added.Count > 0) SecretPacksUnlocked.Invoke(Packs.Where(pack => added.Contains(pack.Id)).ToArray());
        }

        private static void RecordPackOpening(SavedProfile state, int count, bool bundle, bool foundUr)
        {
            state.shop = state.shop?.Copy() ?? new SecretShopSave();
            var shop = state.shop;
            if (bundle) shop.nextBundleUr = !foundUr;
            int mileage = shop.mileage + count;
            shop.urTickets = (int)Math.Min(int.MaxValue, (long)shop.urTickets + mileage / PackMileageTarget);
            shop.mileage = mileage % PackMileageTarget;
        }

        internal static bool CanRedeemPackTicket(CollectionPack pack) => PackUrTickets > 0 && IsPackUnlocked(pack) &&
            GetMissingUr(pack).Any();

        private static IEnumerable<(uint Code, bool Overframe)> GetMissingUr(CollectionPack pack)
        {
            foreach (uint code in pack.CardPool)
            {
                if (CardRarityCatalog.GetRarity(code) != CardRarity.UltraRare) continue;
                if (GetTotalOwnedCount(code) == 0) yield return (code, false);
                else if (GetOwnedOverframeCount(code) == 0 && CardArtLoader.SupportsOverframe(code))
                    yield return (code, true);
            }
        }

        internal static bool TryRedeemPackTicket(string packId, out PackPull pull, out string error)
        {
            pull = default;
            if (!CanTransact(out error)) return false;
            var pack = Packs.FirstOrDefault(item => item.Id == packId);
            if (!HasStarter || PackUrTickets == 0 || !IsPackUnlocked(pack))
            {
                error = "확정 획득권과 해금된 팩을 확인하세요.";
                return false;
            }
            var eligible = GetMissingUr(pack).ToArray();
            if (eligible.Length == 0)
            {
                error = "이 팩에는 획득 가능한 미보유 UR 또는 오버프레임 UR 카드가 없습니다.";
                return false;
            }

            var (code, overframe) = eligible[PackRandom.Next(eligible.Length)];
            var regular = overframe ? _owned : new Dictionary<uint, int>(_owned) { [code] = 1 };
            var special = overframe ? new Dictionary<uint, int>(_ownedOverframes) { [code] = 1 } : _ownedOverframes;
            var next = WithBalance(Balance);
            next.shop = next.shop.Copy();
            next.shop.urTickets--;
            UnlockSecretPacks(next, new[] { code });
            if (!Write(next, regular, out error, special)) return false;
            pull = new PackPull(code, overframe, CombinedCount(regular.GetValueOrDefault(code), special.GetValueOrDefault(code)));
            return true;
        }
    }
}
