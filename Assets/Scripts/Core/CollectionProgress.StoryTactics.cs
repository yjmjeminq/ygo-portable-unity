using System;
using System.Collections.Generic;
using System.Linq;

namespace YGOPortable.Core
{
    public static partial class CollectionProgress
    {
        [Serializable]
        private sealed class StoryTacticsSave
        {
            public int[] supports = new int[RaidSupportCatalog.Items.Length];
            public List<StoryBond> bonds = new();

            internal StoryTacticsSave Copy() => new()
            {
                supports = (int[])supports.Clone(),
                bonds = new List<StoryBond>(bonds),
            };
        }

        [Serializable]
        private struct StoryBond
        {
            public int opponent;
            public int points;
        }

        internal readonly struct StoryPrize
        {
            internal readonly int Dp;
            internal readonly int Dust;
            internal readonly PackPull? RaidCard;
            internal readonly PackPull? ThemeCard;
            internal readonly RaidSupport Support;
            internal readonly int Bond;

            internal StoryPrize(int dp, int dust, PackPull? raidCard, PackPull? themeCard, RaidSupport support, int bond)
            {
                Dp = dp;
                Dust = dust;
                RaidCard = raidCard;
                ThemeCard = themeCard;
                Support = support;
                Bond = bond;
            }
        }

        internal static int GetStoryBond(int opponentId) =>
            _profile?.tactics?.bonds.Find(record => record.opponent == opponentId).points ?? 0;

        internal static int GetSupportCount(RaidSupport item)
        {
            int index = RaidSupportCatalog.IndexOf(item);
            return index < 0 ? 0 : _profile?.tactics?.supports[index] ?? 0;
        }

        internal static bool TryBuyRaidSupport(RaidSupport item, out string error)
        {
            if (!CanTransact(out error)) return false;
            int index = RaidSupportCatalog.IndexOf(item);
            if (!HasStarter || index < 0 || GetSupportCount(item) == int.MaxValue)
            {
                error = "전술 지원 구매 정보를 확인하세요.";
                return false;
            }
            int cost = RaidSupportCatalog.Items[index].Cost;
            if (Balance < cost)
            {
                error = "DP가 부족합니다.";
                return false;
            }
            var next = WithBalance(Balance - cost);
            next.tactics = next.tactics.Copy();
            next.tactics.supports[index]++;
            return Write(next, _owned, out error);
        }

        internal static bool CanEquipRaidSupports(RaidSupport items, bool raid) =>
            RaidSupportCatalog.IsSelectionValid(items) && (raid || (items & RaidSupport.Breaker) == 0) &&
            RaidSupportCatalog.Items.All(entry => (items & entry.Id) == 0 || GetSupportCount(entry.Id) > 0);

        internal static bool TryConsumeRaidSupports(RaidSupport items, bool raid, out string error)
        {
            error = "";
            if (items == RaidSupport.None) return true;
            if (!CanTransact(out error)) return false;
            if (!HasStarter || !CanEquipRaidSupports(items, raid))
            {
                error = "전술 지원 보유 수량과 장착 조건을 확인하세요.";
                return false;
            }
            var next = WithBalance(Balance);
            next.tactics = next.tactics.Copy();
            for (int i = 0; i < RaidSupportCatalog.Items.Length; i++)
                if ((items & RaidSupportCatalog.Items[i].Id) != 0) next.tactics.supports[i]--;
            return Write(next, _owned, out error);
        }

        private static int IncreaseStoryBond(SavedProfile profile, int opponent)
        {
            int index = profile.tactics.bonds.FindIndex(record => record.opponent == opponent);
            int points = index >= 0 ? profile.tactics.bonds[index].points : 0;
            var result = new StoryBond { opponent = opponent, points = points == int.MaxValue ? points : points + 1 };
            if (index < 0) profile.tactics.bonds.Add(result);
            else profile.tactics.bonds[index] = result;
            return result.points;
        }

        private static RaidSupport TryRollSupport(SavedProfile next, System.Random random)
        {
            if (random.Next(100) >= 20) return RaidSupport.None;
            int index = random.Next(RaidSupportCatalog.Items.Length);
            if (next.tactics.supports[index] == int.MaxValue) return RaidSupport.None;
            next.tactics.supports[index]++;
            return RaidSupportCatalog.Items[index].Id;
        }
    }
}
