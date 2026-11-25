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
