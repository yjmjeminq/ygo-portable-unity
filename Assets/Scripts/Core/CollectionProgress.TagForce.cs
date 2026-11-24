using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YGOPortable.Core
{
    public static partial class CollectionProgress
    {
        internal static TagForceSave GetTagForceProgress(string seriesId) =>
            (seriesId == TagForceWorldData.GxId ? _profile?.tagForce :
                _profile?.tagForceWorlds.Find(world => world.seriesId == seriesId)?.progress) ?? new TagForceSave();

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
            if (seriesId == TagForceWorldData.GxId) next.tagForce = state;
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

        internal static bool TryFinishTagForce(TagForceDuel duel, bool won, bool draw, out string result)
        {
            var next = GetTagForceProgress(duel.SeriesId).Copy();
            var relation = next.EditRelationship(duel.Persona.personaId);
            next.AddAffinity(duel.Persona.personaId, (won ? 32 : 14) + (duel.Effect == "BondBoost" ? 18 : 0));
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
            if (!awardRelation.masterReward && (awardRelation.Hearts == 5 || champion))
            {
                reward = TagForceAce(awardFor.DisplayForm(next));
                awardRelation.masterReward = reward != 0;
            }
            float multiplier = won ? duel.Partner?.WinDpMultiplier ?? 1f : 1f;
            if (duel.Effect == "HighRiskHighReturn") multiplier *= 1.5f;
            int dp = Mathf.RoundToInt((won ? 300 : draw ? 160 : 130) * multiplier);
            if (!TrySaveTagForce(duel.SeriesId, next, dp, out result, reward, overframe: true)) return false;
            result = $"{(draw ? "무승부" : won ? "승리" : "패배")} · {dp:N0} DP · 인연 {relation.affinity}/500";
            if (duel.GrandPrix) result += champion ? " · 그랑프리 우승!" : $" · 그랑프리 {next.grandPrixRound}/3승";
            if (reward != 0) result += $" · 오버프레임 {CardDatabase.GetName(reward)}";
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
