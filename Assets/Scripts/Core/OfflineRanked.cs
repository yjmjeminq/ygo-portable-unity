using System;
using System.Collections.Generic;
using System.Linq;
using YGOPortable.AI;

namespace YGOPortable.Core
{
    [Serializable]
    internal sealed class RankedSave
    {
        public int season = 1;
        public int rank;
        public int points;
        public int streak;
        public int wins;
        public int losses;
        public int draws;
        public int bestStreak;
        public int bestRank;
        public int rating;
        public int bestRating;
        public int champions;
        public int claims;
        public string deckId;
        public List<int> championSeasons = new();
        public List<RankedHistoryEntry> history = new();
        internal long CompletedMatches => (long)wins + losses + draws;

        internal RankedSave Copy()
        {
            var copy = (RankedSave)MemberwiseClone();
            copy.history = new List<RankedHistoryEntry>(history);
            copy.championSeasons = new List<int>(championSeasons);
            return copy;
        }

        internal bool IsValid => season >= 1 && rank is >= 0 and <= OfflineRanked.Champion &&
            points >= 0 && points < OfflineRanked.WinsNeeded(rank) &&
            wins >= 0 && losses >= 0 && draws >= 0 && streak >= 0 && bestStreak >= streak &&
            bestRank >= rank && bestRank <= OfflineRanked.Champion && rating >= 0 && bestRating >= rating &&
            (rank == OfflineRanked.Champion ? rating >= OfflineRanked.MinRating : rating == 0) &&
            champions >= 0 && champions <= season && claims >= 0 &&
            championSeasons != null && championSeasons.Count == champions &&
            championSeasons.All(value => value >= 1 && value <= season) && championSeasons.Distinct().Count() == champions &&
            (claims & ~((1 << (OfflineRanked.Champion + 1)) - 1)) == 0 &&
            history != null && history.Count <= 10 && history.All(match => match?.IsValid == true);
    }

    [Serializable]
    internal sealed class RankedHistoryEntry
    {
        public string id;
        public string playerDeck;
        public string opponent;
        public string opponentDeck;
        public bool playerFirst;
        public RankedOutcome outcome;

        internal bool IsValid => Guid.TryParseExact(id, "N", out _) &&
            (int)outcome >= 0 && (int)outcome <= (int)RankedOutcome.Draw;
    }

    internal enum RankedOutcome { Win, Loss, Draw }

    internal readonly struct RankedOpponent
    {
        internal readonly OpponentCharacter Character;
        internal readonly PresetDeckInfo Deck;
        internal readonly int Level;
        internal readonly int Rating;
        internal readonly float PlanningScale;

        internal RankedOpponent(OpponentCharacter character, PresetDeckInfo deck, int level, float planningScale)
        {
            Character = character;
            Deck = deck;
            Level = level;
            PlanningScale = planningScale;
            Rating = deck.isMeta2026 || character.isMeta2026 ? 1700 + Math.Abs(deck.id % 101) : 1410 + level * 19 + Math.Abs(deck.id % 76);
        }
    }

    internal sealed class RankedMatch
    {
        internal readonly string Id = Guid.NewGuid().ToString("N");
        internal readonly int Season;
        internal readonly int Rank;
        internal readonly long CompletedMatches;
        internal readonly RankedOpponent Opponent;
        internal readonly UserDeck Deck;
        internal readonly uint Ace;
        internal readonly bool PlayerWonToss;
        internal readonly bool Promotion;
        internal readonly bool Demotion;
        internal bool PlayerFirst;

        internal RankedMatch(RankedSave state, RankedOpponent opponent, UserDeck deck, bool playerWonToss)
        {
            Season = state.season;
            Rank = state.rank;
            CompletedMatches = state.CompletedMatches;
            Opponent = opponent;
            Deck = CollectionProgress.CopyDeck(deck);
            Ace = OfflineRanked.Ace(Deck);
            PlayerWonToss = playerWonToss;
            Promotion = OfflineRanked.PromotionWin(state);
            Demotion = OfflineRanked.DemotionLoss(state);
        }
    }

    internal readonly struct RankedPrize
    {
        internal readonly int RankBefore, RankAfter, Dp, Dust, RatingChange;
        internal readonly uint Card;
        internal readonly bool Overframe;
        internal bool Promoted => RankAfter > RankBefore;
        internal bool Demoted => RankAfter < RankBefore;

        internal RankedPrize(int before, int after, int dp, int dust, int ratingChange, uint card, bool overframe)
        {
            RankBefore = before;
            RankAfter = after;
            Dp = dp;
            Dust = dust;
            RatingChange = ratingChange;
            Card = card;
            Overframe = overframe;
        }
    }

    internal static class OfflineRanked
    {
        internal const int Champion = 29;
        internal const int InitialRating = 1500;
        internal const int MinRating = 100;
        private static readonly string[] Tiers = { "BRONZE", "SILVER", "GOLD", "PLATINUM", "DIAMOND", "MASTER" };
        private static readonly string[] Divisions = { "V", "IV", "III", "II", "I" };
        private static readonly int[] Targets = { 2, 3, 3, 4, 4, 5 };
        private static readonly (int Dp, int Dust)[] DivisionRewards = { (600, 100), (750, 130), (900, 160), (1100, 200), (1300, 250), (1500, 300) };
        private static readonly (int Dp, int Dust)[] TierRewards = { (0, 0), (1500, 300), (2500, 600), (3500, 1000), (5000, 1500), (7000, 2000) };

        internal static string Label(int rank) => $"{Tiers[rank / 5]} {Divisions[rank % 5]}";
        internal static int WinsNeeded(int rank) => Targets[rank / 5];
        internal static bool PromotionWin(RankedSave state) => state.rank < Champion && state.points + PointsPerWin(state) >= WinsNeeded(state.rank);
        internal static bool DemotionLoss(RankedSave state) => state.rank is >= 10 and < Champion && state.rank % 5 > 0 && state.points == 0;
        internal static bool CanReset(RankedSave state) => state.rank >= 15 && state.season < int.MaxValue;
        private static int PointsPerWin(RankedSave state) => PointsPerWin(state.rank, state.streak);

        internal static int WinsUntilPromotion(RankedSave state)
        {
            int wins = 0, points = state.points;
            while (points < WinsNeeded(state.rank))
            {
                points += PointsPerWin(state.rank, (int)Math.Min(int.MaxValue, (long)state.streak + wins));
                wins++;
            }
            return wins;
        }

        private static int PointsPerWin(int rank, int streak) => (rank / 5) switch
        {
            1 when streak >= 1 => 2,
            2 when streak >= 2 => 2,
            _ => 1,
        };

        internal static RankedSave Reset(RankedSave state)
        {
            var next = state.Copy();
            next.season++;
            next.rank = (state.rank / 5 - 2) * 5;
            next.points = next.streak = next.rating = next.claims = 0;
            return next;
        }

        internal static (int Dp, int Dust, int NewRankReward) Apply(RankedSave next, RankedMatch match, RankedOutcome outcome)
        {
            int before = next.rank;
            int gained = outcome == RankedOutcome.Win ? PointsPerWin(next) : 0;
            bool demotion = outcome == RankedOutcome.Loss && DemotionLoss(next);
            int dp = 0, dust = 0, newRankReward = -1;
            if (outcome == RankedOutcome.Win)
            {
                next.wins = Increment(next.wins);
                next.streak = Increment(next.streak);
                next.bestStreak = Math.Max(next.streak, next.bestStreak);
                dp = next.streak >= 5 ? 900 : next.streak >= 3 ? 675 : next.streak >= 2 ? 540 : 450;
                dust = next.streak >= 5 ? 180 : next.streak >= 3 ? 120 : next.streak >= 2 ? 85 : 60;
            }
            else
            {
                next.streak = 0;
                if (outcome == RankedOutcome.Loss) { next.losses = Increment(next.losses); dp = CollectionProgress.LossReward; }
                else next.draws = Increment(next.draws);
            }
            if (before == Champion)
            {
                double score = outcome == RankedOutcome.Win ? 1d : outcome == RankedOutcome.Loss ? 0d : .5d;
                double expected = 1d / (1d + Math.Pow(10d, (match.Opponent.Rating - (double)next.rating) / 400d));
                int change = (int)Math.Round(32d * (score - expected), MidpointRounding.AwayFromZero);
                next.rating = (int)Math.Clamp((long)next.rating + change, MinRating, int.MaxValue);
            }
            else if (outcome == RankedOutcome.Win)
            {
                next.points += gained;
                if (next.points >= WinsNeeded(before))
                {
                    next.rank++;
                    next.points = 0;
                    int bit = 1 << next.rank;
                    if ((next.claims & bit) == 0)
                    {
                        next.claims |= bit;
                        newRankReward = next.rank;
                        var reward = next.rank == Champion ? (10000, 3000) :
                            next.rank % 5 == 0 ? TierRewards[next.rank / 5] : DivisionRewards[next.rank / 5];
                        dp += reward.Item1;
                        dust += reward.Item2;
                    }
                    if (next.rank == Champion)
                    {
                        next.rating = InitialRating;
                        next.champions = Increment(next.champions);
                        next.championSeasons.Add(next.season);
                    }
                }
            }
            else if (outcome == RankedOutcome.Loss && before >= 10)
            {
                if (demotion)
                {
                    next.rank--;
                    next.points = WinsNeeded(next.rank) - 1;
                }
                else next.points = Math.Max(0, next.points - 1);
            }
            next.bestRank = Math.Max(next.rank, next.bestRank);
            next.bestRating = Math.Max(next.rating, next.bestRating);
            next.history.Insert(0, new RankedHistoryEntry
            {
                id = match.Id, playerDeck = match.Deck.name, opponent = match.Opponent.Character.name,
                opponentDeck = match.Opponent.Deck.title, playerFirst = match.PlayerFirst, outcome = outcome,
            });
            if (next.history.Count > 10) next.history.RemoveAt(10);
            return (dp, dust, newRankReward);
        }

        private static int Increment(int value) => value == int.MaxValue ? value : value + 1;

        internal static IReadOnlyList<RankedOpponent> Pool(int rank)
        {
            int tier = rank / 5;
            var bounds = tier switch { 0 => (1, 3), 1 => (3, 5), 2 => (5, 7), 3 => (7, 9), 4 => (9, 10), _ => (10, 10) };
            var candidates = new List<RankedOpponent>();
            foreach (var character in DeckRepository.Characters)
            {
                var decks = OpponentDeckLevels.GetDecks(character.id);
                if (character.isMeta2026)
                {
                    if (tier < 4) continue;
                    // Use one current signature build per metagame archetype.
                    var signature = decks.LastOrDefault(deck => Playable(deck) && deck.isMeta2026) ??
                        DeckRepository.GetDecksForCharacter(character.id).LastOrDefault(deck => Playable(deck) && deck.isMeta2026);
                    if (signature != null) candidates.Add(new RankedOpponent(character, signature, 10, ComboSearch.MaxPlanningScale));
                    continue;
                }
                for (int i = bounds.Item1 - 1; i < Math.Min(bounds.Item2, decks.Count); i++)
                {
                    var deck = decks[i];
                    if (!Playable(deck) || deck.isMeta2026 || (tier == 3 && !deck.featured && decks.Count < 9)) continue;
                    float scale = tier switch
                    {
                        0 => 1f, 1 => 1.12f, 2 => 1.29f,
                        3 => Math.Min(ComboSearch.MaxPlanningScale, 1.45f + (i - 6) * .10f), _ => ComboSearch.MaxPlanningScale,
                    };
                    candidates.Add(new RankedOpponent(character, deck, i + 1, scale));
                }
            }
            if (tier == 4)
            {
                var featuredMeta = candidates.Where(entry => entry.Character.isMeta2026).OrderBy(entry => entry.Deck.id).Take(10);
                return candidates.Where(entry => !entry.Character.isMeta2026).Concat(featuredMeta).ToArray();
            }
            return candidates;
        }

        internal static RankedOpponent? Pick(RankedSave state, Random random)
        {
            var pool = Pool(state.rank);
            if (pool.Count == 0) return null;
            IEnumerable<RankedOpponent> candidates = pool;
            if (state.rank / 5 >= 4)
            {
                bool meta = random.Next(100) < (state.rank / 5 == 4 ? 25 : 50);
                var group = pool.Where(entry => (entry.Character.isMeta2026 || entry.Deck.isMeta2026) == meta).ToArray();
                if (group.Length > 0) candidates = group;
            }
            if (state.rank == Champion)
                candidates = candidates.OrderBy(entry => Math.Abs((long)entry.Rating - state.rating))
                    .Take(Math.Max(8, pool.Count / 3));
            var recent = state.history.Take(3).Select(entry => entry.opponent).ToHashSet(StringComparer.Ordinal);
            var choices = candidates.Where(entry => !recent.Contains(entry.Character.name)).ToArray();
            if (choices.Length == 0) choices = candidates.ToArray();
            return choices[random.Next(choices.Length)];
        }

        private static bool Playable(PresetDeckInfo deck) => deck?.main?.Length is >= 40 and <= 60 &&
            (deck.extra?.Length ?? 0) <= 15 && (deck.side?.Length ?? 0) <= 15;

        internal static uint Ace(UserDeck deck)
        {
            if (deck == null) return 0;
            var destiny = deck.Copies().FirstOrDefault(copy => copy.Appearance.destinyDraw && CollectionProgress.IsCraftableCard(CardDatabase.Get(copy.Code)));
            if (destiny.Code != 0) return destiny.Code;
            return deck.extra.Concat(deck.main).Distinct().Select(CardDatabase.Get)
                .Where(CollectionProgress.IsCraftableCard)
                .OrderByDescending(card => card.IsMonster)
                .ThenByDescending(card => card.IsExtra)
                .ThenByDescending(card => CardRarityCatalog.GetRarity(card.code))
                .ThenByDescending(card => card.atk)
                .FirstOrDefault()?.code ?? 0;
        }

        internal static bool OpponentTakesFirst(PresetDeckInfo deck)
        {
            // Board-breaking staples and dedicated battle decks favor a six-card going-second opening.
            uint[] breakers = { 14532163, 48130397, 15693423, 18144506, 24299458, 10045474, 8267140, 10000080, 25311006 };
            int goingSecondCards = deck.main.Count(code => breakers.Contains(code));
            string title = deck.title ?? "";
            return goingSecondCards < 5 && !title.Contains("후공", StringComparison.Ordinal) &&
                !title.Contains("괴수", StringComparison.Ordinal);
        }
    }
}
