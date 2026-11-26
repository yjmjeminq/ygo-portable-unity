using System;
using System.Collections.Generic;
using System.Linq;

namespace YGOPortable.Core
{
    [Serializable]
    internal sealed class RpgAdventureSave
    {
        public bool introSeen;
        public int xp, affinity;
        public List<int> caches = new(), conversations = new();

        internal RpgAdventureSave Copy() => new()
        {
            introSeen = introSeen,
            xp = xp,
            affinity = affinity,
            caches = new List<int>(caches),
            conversations = new List<int>(conversations),
        };
    }

    public static partial class CollectionProgress
    {
        private const int RpgMaxRank = 30;
        private const int RpgMaxAffinity = 100;
        private static readonly (int Dp, uint Card, bool Ticket)[] RpgCacheRewards =
        {
            (180, 0u, false), (0, 0u, true), (280, 0u, false), (0, 44508094u, false),
            (380, 0u, false), (0, 0u, true), (0, 2469606u, false), (480, 0u, false), (0, 0u, true),
            (150, 0u, false), (400, 0u, false),
        };

        internal static bool RpgIntroSeen => _profile?.rpgAdventure?.introSeen == true;
        internal static int RpgExperience => _profile?.rpgAdventure?.xp ?? 0;
        internal static int RpgAffinity => _profile?.rpgAdventure?.affinity ?? 0;
        internal static int RpgRank => RpgRankAt(RpgExperience);
        internal static float RpgRankProgress => RpgRank == RpgMaxRank ? 1f :
            (RpgExperience - RpgRankThreshold(RpgRank)) / (float)(RpgRankThreshold(RpgRank + 1) - RpgRankThreshold(RpgRank));
        internal static bool RpgCacheFound(int region) => _profile?.rpgAdventure?.caches.Contains(region) == true;

        private static int RpgRankThreshold(int rank) => 50 * rank * (rank - 1);

        private static int RpgRankAt(int xp)
        {
            int rank = 1;
            while (rank < RpgMaxRank && xp >= RpgRankThreshold(rank + 1)) rank++;
            return rank;
        }

        private static void NormalizeRpgAdventure(SavedProfile profile)
        {
            var adventure = profile.rpgAdventure ??= new RpgAdventureSave();
            adventure.xp = Math.Clamp(adventure.xp, 0, RpgRankThreshold(RpgMaxRank));
            adventure.affinity = Math.Clamp(adventure.affinity, 0, RpgMaxAffinity);
            adventure.caches = Normalize(adventure.caches);
            adventure.conversations = Normalize(adventure.conversations);

            static List<int> Normalize(List<int> entries) =>
                (entries ?? new List<int>()).Where(index => (uint)index < RpgCacheRewards.Length).Distinct().ToList();
        }

        private static int ApplyRpgDuelGrowth(SavedProfile next, bool story)
        {
            var adventure = next.rpgAdventure = next.rpgAdventure.Copy();
            int previous = RpgRankAt(adventure.xp);
            adventure.xp = Math.Min(RpgRankThreshold(RpgMaxRank), adventure.xp + (story ? 80 : 40));
            adventure.affinity = Math.Min(RpgMaxAffinity, adventure.affinity + (story ? 5 : 3));
            int bonus = 0;
            for (int rank = previous + 1; rank <= RpgRankAt(adventure.xp); rank++)
            {
                int dp = Math.Min(int.MaxValue - next.balance, 150 + 50 * rank);
                next.balance += dp;
                bonus += dp;
                if (rank % 5 == 0) AwardRpgTicket(next);
            }
            return bonus;
        }

        internal static bool TrySaveRpgCheckpoint(bool finishIntro, out string error)
        {
            if (!CanTransact(out error)) return false;
            var next = WithBalance(Balance);
            if (finishIntro)
            {
                next.rpgAdventure = next.rpgAdventure.Copy();
                next.rpgAdventure.introSeen = true;
            }
            return Write(next, _owned, out error);
        }

        internal static bool TryTalkToRpgPartner(int region, out string error)
        {
            if (!CanTransact(out error)) return false;
            if ((uint)region >= RpgCacheRewards.Length || _profile.rpgAdventure.conversations.Contains(region) ||
                _profile.rpgAdventure.affinity == RpgMaxAffinity) return false;
            var next = WithBalance(Balance);
            next.rpgAdventure = next.rpgAdventure.Copy();
            next.rpgAdventure.conversations.Add(region);
            next.rpgAdventure.affinity = Math.Min(RpgMaxAffinity, next.rpgAdventure.affinity + 4);
            return Write(next, _owned, out error);
        }

        internal static bool TryFindRpgCache(int region, out string message)
        {
            if (!CanTransact(out message)) return false;
            if (!HasStarter || (uint)region >= RpgCacheRewards.Length || RpgCacheFound(region))
            {
                message = "이미 조사했거나 찾을 수 없는 물건입니다.";
                return false;
            }

            var (dp, card, ticket) = RpgCacheRewards[region];
            var next = WithBalance(Balance);
            var owned = _owned;
            if (ticket && PackUrTickets < int.MaxValue)
            {
                AwardRpgTicket(next);
                message = "팩 UR 확정 획득권 1장 획득";
            }
            else if (card != 0 && IsDeckCard(CardDatabase.Get(card)) && GetOwnedCount(card) < int.MaxValue)
            {
                owned = new Dictionary<uint, int>(_owned) { [card] = GetOwnedCount(card) + 1 };
                UnlockSecretPacks(next, new[] { card });
                message = CardDatabase.GetName(card) + " 1장 획득";
            }
            else
            {
                dp = Math.Min(int.MaxValue - Balance, dp == 0 ? 300 : dp);
                if (dp == 0)
                {
                    message = "DP를 더 보관할 수 없습니다.";
                    return false;
                }
                next.balance += dp;
                message = $"+{dp:N0} DP 획득";
            }
            next.rpgAdventure = next.rpgAdventure.Copy();
            next.rpgAdventure.caches.Add(region);
            if (Write(next, owned, out var error)) return true;
            message = error;
            return false;
        }

        private static void AwardRpgTicket(SavedProfile next)
        {
            next.shop = next.shop?.Copy() ?? new SecretShopSave();
            if (next.shop.urTickets < int.MaxValue) next.shop.urTickets++;
        }
    }
}
