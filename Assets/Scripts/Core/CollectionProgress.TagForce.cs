using System;
using System.Collections.Generic;
using System.Linq;

namespace YGOPortable.Core
{
    public static partial class CollectionProgress
    {
        internal static TagForceSave GetTagForceProgress(string seriesId) =>
            (seriesId == TagForceData.GxId ? _profile?.tagForce :
                _profile?.tagForceWorlds.Find(world => world.seriesId == seriesId)?.progress) ?? new TagForceSave { affinityVersion = TagForceSave.AffinityVersion };

        internal static RpgQuestState GetRpgQuestState(string id) =>
            _profile?.rpgQuests.Find(quest => quest.id == id)?.state ?? RpgQuestState.Available;

        internal static bool TryAdvanceRpgQuest(string id, RpgQuestState from, out int reward, out string error)
        {
            reward = 0;
            if (!CanTransact(out error)) return false;
            var quest = RpgSideQuest.Find(id);
            if (!HasStarter || quest == null || from is < RpgQuestState.Available or >= RpgQuestState.Complete ||
                GetRpgQuestState(id) != from)
            {
                error = "의뢰 진행 상태를 확인할 수 없습니다.";
                return false;
            }

            var state = (RpgQuestState)((int)from + 1);
            int dp = state == RpgQuestState.Complete ? Math.Min(int.MaxValue - Balance, quest.Reward) : 0;
            var next = WithBalance(Balance + dp);
            RpgBondReward bondReward = default;
            if (state == RpgQuestState.Complete)
            {
                next.rpgAdventure = next.rpgAdventure.Copy();
                next.rpgAdventure.xp = Math.Min(RpgRankThreshold(RpgMaxRank), next.rpgAdventure.xp + quest.Experience);
                bondReward = ApplyRpgAffinity(next, quest.Affinity);
                if (quest.PackTicket) AwardRpgTicket(next);
            }
            next.rpgQuests = _profile.rpgQuests.Where(entry => entry.id != id).ToList();
            next.rpgQuests.Add(new RpgQuestRecord { id = id, state = state });
            if (!Write(next, _owned, out error)) return false;
            PublishRpgBondReward(bondReward);
            reward = dp;
            return true;
        }

        internal static bool TrySaveTagForce(string seriesId, TagForceSave state, int dp, out string error,
            uint reward = 0, bool overframe = false, uint gift = 0)
        {
            if (!CanTransact(out error)) return false;
            if (dp < 0 && (long)Balance + dp < 0)
            {
                error = "보유 DP가 부족합니다.";
                return false;
            }
            if (gift != 0 && GetOwnedCount(gift) <= 3)
            {
                error = "일반판을 4장 이상 보유한 카드만 선물할 수 있습니다.";
                return false;
            }
            var owned = _owned;
            var special = _ownedOverframes;
            if (gift != 0)
                owned = new Dictionary<uint, int>(owned) { [gift] = owned[gift] - 1 };
            var next = WithBalance((int)Math.Min(int.MaxValue, (long)Balance + dp));
            if (seriesId == TagForceData.GxId) next.tagForce = state;
            else
            {
                next.tagForceWorlds = _profile.tagForceWorlds.Where(world => world.seriesId != seriesId).ToList();
                next.tagForceWorlds.Add(new TagForceWorldSave { seriesId = seriesId, progress = state });
            }
            if (IsDeckCard(CardDatabase.Get(reward)))
            {
                var inventory = overframe ? special : owned;
                int copies = inventory.GetValueOrDefault(reward);
                if (copies < int.MaxValue)
                {
                    var updated = new Dictionary<uint, int>(inventory) { [reward] = copies + 1 };
                    if (overframe) special = updated;
                    else owned = updated;
                    UnlockSecretPacks(next, new[] { reward });
                }
            }
            return Write(next, owned, out error, special);
        }

        internal static bool TryAwardRpgPractice(PresetDeckInfo deck, bool won, bool draw, out string result,
            bool awardPartnerAffinity = true)
        {
            if (!CanTransact(out result)) return false;
            int dp = Math.Min(int.MaxValue - Balance, won ? 300 : draw ? 160 : 130);
            var next = WithBalance(Balance + dp);
            int previousXp = RpgExperience;
            RpgBondReward bondReward = default;
            if (won) dp += ApplyRpgDuelGrowth(next, story: false, out bondReward, awardPartnerAffinity);
            var owned = _owned;
            uint reward = 0;
            if (won)
            {
                var choices = deck.main.Distinct().Where(code => IsDeckCard(CardDatabase.Get(code)) &&
                    _owned.GetValueOrDefault(code) < int.MaxValue).ToArray();
                if (choices.Length > 0)
                {
                    reward = choices[PackRandom.Next(choices.Length)];
                    owned = new Dictionary<uint, int>(_owned) { [reward] = _owned.GetValueOrDefault(reward) + 1 };
                    UnlockSecretPacks(next, new[] { reward });
                }
            }
            if (!Write(next, owned, out result)) return false;
            PublishRpgBondReward(bondReward);
            string outcome = won ? "연습 듀얼 승리" : draw ? "연습 듀얼 무승부" : "연습 듀얼 패배";
            result = $"{outcome} · +{dp:N0} DP";
            if (RpgExperience > previousXp) result += $" · +{RpgExperience - previousXp} EXP";
            if (reward != 0) result += $" · {CardDatabase.GetName(reward)} 획득";
            return true;
        }

        internal static uint TagForceAce(TagForceForm form)
        {
            var deck = form.Decks.LastOrDefault();
            if (deck == null) return 0;
            if (IsDeckCard(CardDatabase.Get(deck.sigCard))) return deck.sigCard;
            return deck.extra.Concat(deck.main).FirstOrDefault(code => IsDeckCard(CardDatabase.Get(code)));
        }

        internal static uint TagForceBonusCard() =>
            HighRarityPool.FirstOrDefault(code => GetOwnedCount(code) < int.MaxValue);
    }
}
