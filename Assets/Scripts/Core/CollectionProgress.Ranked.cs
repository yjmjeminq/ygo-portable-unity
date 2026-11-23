using System;
using System.Collections.Generic;
using System.Linq;

namespace YGOPortable.Core
{
    public static partial class CollectionProgress
    {
        internal static RankedSave Ranked => _profile?.ranked ?? new RankedSave();
        internal static bool IsRankedChampion => _profile?.ranked?.champions > 0;

        internal static bool TrySelectRankedDeck(UserDeck deck, out string error)
        {
            if (!CanTransact(out error) || !CanUseDeck(deck, out error)) return false;
            if (!HasStarter)
            {
                error = "스타터 덱을 먼저 선택하세요.";
                return false;
            }
            if (Ranked.deckId == deck.id) return true;
            var next = WithBalance(Balance);
            next.ranked = Ranked.Copy();
            next.ranked.deckId = deck.id;
            return Write(next, _owned, out error);
        }

        internal static bool TryResetRankedSeason(out string error)
        {
            if (!CanTransact(out error)) return false;
            if (!OfflineRanked.CanReset(Ranked))
            {
                error = "플래티넘 V부터 새 시즌을 시작할 수 있습니다.";
                return false;
            }
            var next = WithBalance(Balance);
            next.ranked = OfflineRanked.Reset(Ranked);
            return Write(next, _owned, out error);
        }

        internal static bool TryFinishRankedMatch(RankedMatch match, RankedOutcome outcome,
            out RankedPrize prize, out string error)
        {
            prize = default;
            if (!CanTransact(out error)) return false;
            var state = Ranked;
            if (!HasStarter || match == null || match.Season != state.season || match.Rank != state.rank ||
                match.CompletedMatches != state.CompletedMatches ||
                state.history.Any(entry => entry.id == match.Id))
            {
                error = "현재 시즌에 기록할 수 있는 경기가 아닙니다.";
                return false;
            }
            var next = WithBalance(Balance);
            next.ranked = state.Copy();
            var (dp, dust, rewardRank) = OfflineRanked.Apply(next.ranked, match, outcome);
            dp = Math.Min(int.MaxValue - Balance, dp);
            next.balance += dp;
            next.dust += dust;

            bool special = rewardRank is 20 or 25 or OfflineRanked.Champion;
            uint card = rewardRank == OfflineRanked.Champion ? match.Ace : PickRankedRewardCard(rewardRank);
            var owned = _owned;
            var overframes = _ownedOverframes;
            if (card != 0)
            {
                var inventory = special ? overframes : owned;
                if (inventory.GetValueOrDefault(card) < int.MaxValue)
                {
                    var updated = new Dictionary<uint, int>(inventory) { [card] = inventory.GetValueOrDefault(card) + 1 };
                    if (special) overframes = updated;
                    else owned = updated;
                    UnlockSecretPacks(next, new[] { card });
                }
                else card = 0;
            }
            if (!Write(next, owned, out error, overframes)) return false;
            prize = new RankedPrize(state.rank, next.ranked.rank, dp, dust,
                state.rank == OfflineRanked.Champion ? next.ranked.rating - state.rating : 0, card, special);
            return true;
        }

        private static uint PickRankedRewardCard(int rewardRank)
        {
            if (rewardRank is not (15 or 20 or 25)) return 0;
            var minimum = rewardRank == 20 ? CardRarity.Normal : rewardRank == 25 ? CardRarity.UltraRare : CardRarity.SuperRare;
            var pool = CardDatabase.AllCards.Where(IsDeckCard).Where(card => CardRarityCatalog.GetRarity(card.code) >= minimum)
                .Where(card => rewardRank == 15 ? GetOwnedCount(card.code) < int.MaxValue : GetOwnedOverframeCount(card.code) < int.MaxValue)
                .Select(card => card.code).ToArray();
            if (pool.Length == 0) return 0;
            if (rewardRank != 15)
            {
                var withPortraits = pool.Where(code => CardArtLoader.SupportsOverframe(code)).ToArray();
                if (withPortraits.Length > 0) pool = withPortraits;
            }
            return pool[PackRandom.Next(pool.Length)];
        }
    }
}
