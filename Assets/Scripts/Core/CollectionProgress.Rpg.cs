using System;
using System.Collections.Generic;
using System.Linq;

namespace YGOPortable.Core
{
    internal readonly struct RpgPartner
    {
        internal readonly int Id, UnlockEpisode;
        internal readonly string Name;

        internal RpgPartner(int id, string name, int unlockEpisode) =>
            (Id, Name, UnlockEpisode) = (id, name, unlockEpisode);
    }

    internal readonly struct RpgBondReward
    {
        internal readonly int PartnerId, Hearts, Dp, Tickets;

        internal RpgBondReward(int partnerId, int hearts, int dp, int tickets) =>
            (PartnerId, Hearts, Dp, Tickets) = (partnerId, hearts, dp, tickets);
    }

    [Serializable]
    internal sealed class RpgPartnerBond
    {
        public int id, affinity;
        public List<int> conversations = new();

        internal RpgPartnerBond Copy() => new() { id = id, affinity = affinity, conversations = new List<int>(conversations) };
    }

    [Serializable]
    internal sealed class RpgAdventureSave
    {
        public bool introSeen;
        public int xp, affinity, partnerId;
        public List<int> caches = new(), conversations = new();
        public List<RpgPartnerBond> partnerBonds = new();

        internal RpgAdventureSave Copy() => new()
        {
            introSeen = introSeen,
            xp = xp,
            affinity = affinity,
            partnerId = partnerId,
            caches = new List<int>(caches),
            conversations = new List<int>(conversations),
            partnerBonds = partnerBonds.Select(bond => bond.Copy()).ToList(),
        };
    }

    public static partial class CollectionProgress
    {
        private const int RpgMaxRank = 30;
        private const int RpgMaxAffinity = 100;
        internal const int RpgGuidePartnerId = 35;
        internal const int RpgAffinityPerHeart = 20;
        internal static IReadOnlyList<RpgPartner> RpgPartners { get; } = Array.AsReadOnly(new RpgPartner[]
        {
            new(RpgGuidePartnerId, "후도 유세이", 0), new(1, "이자요이 아키", 3),
            new(10, "크로우 호건", 8), new(16, "잭 아틀라스", 35),
        });
        private static readonly Queue<RpgBondReward> RpgBondNotices = new();
        private static readonly (int Dp, uint Card, bool Ticket)[] RpgCacheRewards =
        {
            (180, 0u, false), (0, 0u, true), (280, 0u, false), (0, 44508094u, false),
            (380, 0u, false), (0, 0u, true), (0, 2469606u, false), (480, 0u, false), (0, 0u, true),
            (150, 0u, false), (400, 0u, false),
        };

        internal static bool RpgIntroSeen => _profile?.rpgAdventure?.introSeen == true;
        internal static int RpgExperience => _profile?.rpgAdventure?.xp ?? 0;
        internal static RpgPartner ActiveRpgPartner => RpgPartners.FirstOrDefault(partner => partner.Id == _profile?.rpgAdventure?.partnerId) is { Id: > 0 } active
            ? active : RpgPartners[0];
        internal static int RpgAffinity => RpgAffinityFor(ActiveRpgPartner.Id);
        internal static int RpgAffinityFor(int partnerId) => PartnerAffinity(_profile?.rpgAdventure, partnerId);
        internal static bool IsRpgPartnerUnlocked(RpgPartner partner) => partner.UnlockEpisode == 0 ||
            IsStoryEpisodeCleared($"5ds_rpg_ep_{partner.UnlockEpisode:00}");
        internal static bool TryReadRpgBondNotice(out RpgBondReward reward) => RpgBondNotices.TryDequeue(out reward);

        private static int PartnerAffinity(RpgAdventureSave adventure, int id) => id == RpgGuidePartnerId ? adventure?.affinity ?? 0 :
            adventure?.partnerBonds.Find(bond => bond.id == id)?.affinity ?? 0;

        private static RpgPartnerBond GetOrAddPartnerBond(RpgAdventureSave adventure, int id)
        {
            var bond = adventure.partnerBonds.Find(bond => bond.id == id);
            if (bond != null) return bond;
            bond = new RpgPartnerBond { id = id };
            adventure.partnerBonds.Add(bond);
            return bond;
        }
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
            if (!RpgPartners.Any(partner => partner.Id == adventure.partnerId && ClearedPartnerGate(partner)))
                adventure.partnerId = RpgGuidePartnerId;
            adventure.partnerBonds = (adventure.partnerBonds ?? new List<RpgPartnerBond>())
                .Where(bond => bond != null && bond.id != RpgGuidePartnerId && RpgPartners.Any(partner => partner.Id == bond.id))
                .GroupBy(bond => bond.id).Select(group => new RpgPartnerBond
                {
                    id = group.Key,
                    affinity = Math.Clamp(group.Max(bond => bond.affinity), 0, RpgMaxAffinity),
                    conversations = Normalize(group.SelectMany(bond => bond.conversations ?? Enumerable.Empty<int>()).ToList()),
                }).ToList();

            static List<int> Normalize(List<int> entries) =>
                (entries ?? new List<int>()).Where(index => (uint)index < RpgCacheRewards.Length).Distinct().ToList();

            bool ClearedPartnerGate(RpgPartner partner)
            {
                if (partner.UnlockEpisode == 0) return true;
                string id = $"5ds_rpg_ep_{partner.UnlockEpisode:00}";
                var clears = profile.clearedStoryEpisodes;
                return clears?.Contains(id) == true ||
                    (StoryCampaign.LegacyClearFor(id) is { } legacy && clears?.Contains(legacy) == true);
            }
        }

        private static int ApplyRpgDuelGrowth(SavedProfile next, bool story, out RpgBondReward bondReward)
        {
            var adventure = next.rpgAdventure = next.rpgAdventure.Copy();
            int previous = RpgRankAt(adventure.xp);
            adventure.xp = Math.Min(RpgRankThreshold(RpgMaxRank), adventure.xp + (story ? 80 : 40));
            bondReward = ApplyRpgAffinity(next, story ? 5 : 3);
            int bonus = 0;
            for (int rank = previous + 1; rank <= RpgRankAt(adventure.xp); rank++)
            {
                int dp = Math.Min(int.MaxValue - next.balance, 150 + 50 * rank);
                next.balance += dp;
                bonus += dp;
                if (rank % 5 == 0) AwardRpgTicket(next);
            }
            return bonus + bondReward.Dp;
        }

        private static RpgBondReward ApplyRpgAffinity(SavedProfile next, int points)
        {
            if (points <= 0) return default;
            var adventure = next.rpgAdventure;
            int partner = ActiveRpgPartner.Id;
            int before = PartnerAffinity(adventure, partner);
            int after = Math.Min(RpgMaxAffinity, before + points);
            if (partner == RpgGuidePartnerId) adventure.affinity = after;
            else GetOrAddPartnerBond(adventure, partner).affinity = after;
            int dp = 0, tickets = 0;
            int goal = after / RpgAffinityPerHeart;
            for (int heart = before / RpgAffinityPerHeart + 1; heart <= goal; heart++)
            {
                int bonus = Math.Min(int.MaxValue - next.balance, 250 + heart * 100);
                next.balance += bonus;
                dp += bonus;
                int previousTickets = next.shop?.urTickets ?? 0;
                AwardRpgTicket(next);
                tickets += next.shop.urTickets - previousTickets;
            }
            return goal > before / RpgAffinityPerHeart ? new RpgBondReward(partner, goal, dp, tickets) : default;
        }

        private static void PublishRpgBondReward(RpgBondReward reward)
        {
            if (reward.Hearts > 0) RpgBondNotices.Enqueue(reward);
        }

        internal static bool TrySwitchRpgPartner(int id, out string error)
        {
            if (!CanTransact(out error)) return false;
            var partner = RpgPartners.FirstOrDefault(entry => entry.Id == id);
            if (partner.Id == 0 || !IsRpgPartnerUnlocked(partner))
            {
                error = "아직 동행할 수 없는 파트너입니다.";
                return false;
            }
            if (ActiveRpgPartner.Id == id) return true;
            var next = WithBalance(Balance);
            next.rpgAdventure = next.rpgAdventure.Copy();
            next.rpgAdventure.partnerId = id;
            return Write(next, _owned, out error);
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
            int id = ActiveRpgPartner.Id;
            var previous = id == RpgGuidePartnerId ? _profile.rpgAdventure.conversations :
                _profile.rpgAdventure.partnerBonds.Find(bond => bond.id == id)?.conversations;
            if ((uint)region >= RpgCacheRewards.Length || previous?.Contains(region) == true || RpgAffinity == RpgMaxAffinity) return false;
            var next = WithBalance(Balance);
            next.rpgAdventure = next.rpgAdventure.Copy();
            var conversations = id == RpgGuidePartnerId ? next.rpgAdventure.conversations : GetOrAddPartnerBond(next.rpgAdventure, id).conversations;
            conversations.Add(region);
            var reward = ApplyRpgAffinity(next, 4);
            if (!Write(next, _owned, out error)) return false;
            PublishRpgBondReward(reward);
            return true;
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
