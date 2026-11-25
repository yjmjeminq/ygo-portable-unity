using System;
using UnityEngine;

namespace YGOPortable.Core
{
    internal sealed class TagForceManager
    {
        private readonly TagForceData _data;

        internal TagForceManager(TagForceData data) => _data = data;

        internal TagForceSave Progress => CollectionProgress.GetTagForceProgress(_data.seriesId);
        internal TagForceSpot CurrentSpot => _data.Spot(Progress.spotId) ?? _data.spots[0];

        internal bool TrySave(TagForceSave next, int dp, out string error, uint reward = 0, bool foil = false, uint gift = 0)
        {
            if (_data.Spot(next.spotId) == null) next.spotId = CurrentSpot.spotId;
            return CollectionProgress.TrySaveTagForce(_data.seriesId, next, dp, out error, reward, foil, gift);
        }

        internal bool TryTalk(TagForcePersona persona, TagForceTopic topic, out string line, out uint reward, out string error)
        {
            var next = Progress.Copy();
            if (next.conversations.Contains(persona.personaId))
            {
                line = "이 시간대에는 이미 대화했습니다. 다음 시간대에 다시 찾아와 주세요.";
                reward = 0;
                error = "";
                return true;
            }

            bool favorite = topic.topicId == persona.favoriteTopic;
            next.AddAffinity(persona.personaId, favorite ? 256 : 128);
            next.conversations.Add(persona.personaId);
            line = (favorite ? "기분 최고조!\n" : "") + persona.topicReplies.Reply(topic.topicId);
            return TrySaveAffinity(next, persona, 0, out reward, out error);
        }

        internal bool TryGive(TagForcePersona persona, TagForceItem item, uint card,
            out string line, out uint reward, out string error)
        {
            var next = Progress.Copy();
            if ((item == null && card == 0) || (item != null && (card != 0 || next.Count(item.Id) < 1)))
            {
                line = "";
                reward = 0;
                error = "보유한 선물을 선택하세요.";
                return false;
            }

            bool favorite = item?.IsFavorite(persona) == true;
            int points = item != null ? favorite ? item.favoriteAffinity : item.baseAffinity :
                15 + 10 * (int)CardRarityCatalog.GetRarity(card);
            if (item != null) next.ChangeItem(item.Id, -1);
            next.AddAffinity(persona.personaId, points * 8);
            line = persona.giftReactions.Reply(favorite, item) ?? "고마워. 잘 받을게.";
            return TrySaveAffinity(next, persona, card, out reward, out error);
        }

        internal bool TrySetPartner(TagForcePersona persona, out string line, out string error)
        {
            var next = Progress.Copy();
            line = "";
            error = "";
            if (next.grandPrixRound > 0)
            {
                error = "그랑프리 진행 중에는 파트너를 변경할 수 없습니다.";
                return false;
            }
            if (next.partnerId == persona.personaId)
            {
                next.partnerId = null;
                line = "다음에 또 같이 가자.";
            }
            else if (persona.CanPartner)
            {
                next.partnerId = persona.personaId;
                line = persona.partnerQuotes?.followLine ?? persona.partnerQuotes?.duelReady ?? "좋아, 함께 가자!";
            }
            else
            {
                line = persona.partnerRefusal ?? "난 혼자 다닐게. 듀얼이라면 언제든 환영이야.";
                return true;
            }
            return TrySave(next, 0, out error);
        }

        private bool TrySaveAffinity(TagForceSave next, TagForcePersona persona, uint gift, out uint reward, out string error)
        {
            reward = 0;
            var relation = next.EditRelationship(persona.personaId);
            if (relation.Hearts == TagForceRelation.HeartCount && !relation.masterReward)
            {
                reward = CollectionProgress.TagForceAce(persona.DisplayForm(next));
                relation.masterReward = reward != 0;
            }
            return TrySave(next, 0, out error, reward, foil: true, gift: gift);
        }

        internal static bool TryFinishDuel(TagForceDuel duel, bool won, bool draw, out string result)
        {
            var next = CollectionProgress.GetTagForceProgress(duel.SeriesId).Copy();
            var relation = next.EditRelationship(duel.Persona.personaId);
            next.AddAffinity(duel.Persona.personaId, ((won ? 32 : 14) + (duel.Effect == "BondBoost" ? 18 : 0)) * 8);
            if (won && duel.Level >= relation.unlockedLevel)
                relation.unlockedLevel = Math.Min(duel.Form.Decks.Count, duel.Level + 1);
            if (won && duel.Bond != null && !relation.clearedBonds.Contains(duel.Bond.heartLevel))
                relation.clearedBonds.Add(duel.Bond.heartLevel);

            bool champion = false;
            if (duel.GrandPrix)
            {
                if (won)
                {
                    next.grandPrixPartner = duel.Partner.personaId;
                    champion = ++next.grandPrixRound == 3;
                    if (champion) next.EditRelationship(next.grandPrixPartner).wonGrandPrix = true;
                }
                if (!won || champion)
                {
                    next.grandPrixRound = 0;
                    next.grandPrixPartner = null;
                }
            }

            uint reward = 0;
            var awardFor = champion ? duel.Partner : duel.Persona;
            var awardRelation = next.EditRelationship(awardFor.personaId);
            if (!awardRelation.masterReward && (awardRelation.Hearts == TagForceRelation.HeartCount || champion))
            {
                reward = CollectionProgress.TagForceAce(awardFor.DisplayForm(next));
                awardRelation.masterReward = reward != 0;
            }
            float multiplier = won ? duel.Partner?.WinDpMultiplier ?? 1f : 1f;
            if (duel.Effect == "HighRiskHighReturn") multiplier *= 1.5f;
            int dp = Mathf.RoundToInt((won ? 300 : draw ? 160 : 130) * multiplier);
            if (!CollectionProgress.TrySaveTagForce(duel.SeriesId, next, dp, out result, reward, overframe: true)) return false;
            result = $"{(draw ? "무승부" : won ? "승리" : "패배")} · {dp:N0} DP · 인연 {relation.affinity:N0}/{TagForceRelation.MaxAffinity:N0}";
            if (duel.GrandPrix) result += champion ? " · 그랑프리 우승!" : $" · 그랑프리 {next.grandPrixRound}/3승";
            if (reward != 0) result += $" · 오버프레임 {CardDatabase.GetName(reward)}";
            return true;
        }
    }
}
