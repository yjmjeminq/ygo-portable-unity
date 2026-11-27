using System;
using System.Collections.Generic;
using System.Linq;

namespace YGOPortable.Core
{
    [Serializable]
    internal sealed class DimensionTournament
    {
        internal const int Player = -1;
        private static readonly (string Series, int[] Finalists, int[] Rivals)[] Dimensions =
        {
            ("dm", new[] { 104, 105, 103, 79 }, new[] { 94 }),
            ("gx", new[] { 119, 136, 124 }, new[] { 118, 122 }),
            ("5ds", new[] { 35, 36, 3203 }, new[] { 16 }),
            ("zexal", new[] { 174, 146, 150 }, new[] { 156, 160 }),
            ("arcv", new[] { 72, 74 }, new[] { 43, 69 }),
            ("vrains", new[] { 143, 177, 182 }, new[] { 189, 190 }),
        };

        public int seed;
        public int size;
        public int round;
        public bool lost;
        public int[] bracket;

        internal int RoundCount => size == 16 ? 4 : 3;
        internal bool Champion => bracket[1] == Player;
        internal bool Active => !lost && !Champion;
        internal int PlayerNode => size >> round;
        internal int PlayerMatch => PlayerNode / 2;
        internal int Opponent => Active ? bracket[PlayerNode ^ 1] : 0;
        internal bool IsEmpty => size == 0 && seed == 0 && round == 0 && !lost && (bracket?.Length ?? 0) == 0;
        internal bool IsValid => size is 8 or 16 && bracket?.Length == size * 2 && bracket[size] == Player &&
            round >= 0 && round <= (size == 16 ? 4 : 3) && (round != RoundCount || Champion);

        internal static bool Unlocked => Dimensions.Any(entry =>
            CollectionProgress.IsStoryEpisodeCleared(StoryCampaign.EpisodeId(entry.Series, StoryCampaign.EpisodeCount)));

        internal static DimensionTournament Create(int size, int seed)
        {
            var random = new Random(seed);
            var bracket = new int[size * 2];
            bracket[size] = Player;
            var used = new HashSet<int>();
            // Each dimension gets one seed in the draw. The opposite half is reserved for title contenders.
            var guaranteed = Dimensions.Select(entry => Pick(entry.Finalists)).ToArray();
            int[] oppositeHalf = guaranteed.OrderBy(_ => random.Next()).Take(Math.Min(size / 2, guaranteed.Length)).ToArray();
            var opposite = oppositeHalf.Concat(Fill(Dimensions.SelectMany(entry => entry.Finalists), size / 2 - oppositeHalf.Length)).ToArray();
            int[] ownGuaranteed = guaranteed.Except(oppositeHalf).ToArray();
            var own = ownGuaranteed.Concat(Fill(Dimensions.SelectMany(entry => entry.Rivals).Concat(
                Dimensions.SelectMany(entry => entry.Finalists)), size / 2 - 1 - ownGuaranteed.Length)).ToArray();
            Shuffle(own).CopyTo(bracket, size + 1);
            Shuffle(opposite).CopyTo(bracket, size + size / 2);
            return new DimensionTournament { seed = seed, size = size, bracket = bracket };

            int Pick(IEnumerable<int> roster)
            {
                var eligible = roster.Where(id => !used.Contains(id) && HighestDeck(id) != null).ToArray();
                if (eligible.Length == 0) throw new InvalidOperationException("No eligible tournament contestant.");
                int selected = eligible[random.Next(eligible.Length)];
                used.Add(selected);
                return selected;
            }

            IEnumerable<int> Fill(IEnumerable<int> roster, int count) =>
                Enumerable.Range(0, count).Select(_ => Pick(roster));
            int[] Shuffle(int[] entries) => entries.OrderBy(_ => random.Next()).ToArray();
        }

        internal DimensionTournament Copy() => new()
        {
            seed = seed, size = size, round = round, lost = lost, bracket = (int[])bracket.Clone(),
        };

        internal IEnumerable<int> PendingAiMatches()
        {
            if (!Active) yield break;
            for (int node = PlayerMatch; node < PlayerNode; node++)
                if (node != PlayerMatch && bracket[node] == 0 && bracket[node * 2] > 0 && bracket[node * 2 + 1] > 0)
                    yield return node;
        }

        internal static PresetDeckInfo HighestDeck(int opponent) =>
            OpponentDeckLevels.GetDecks(opponent).LastOrDefault() ?? DeckRepository.GetDecksForCharacter(opponent).LastOrDefault();

        internal static string RoundTitle(int slots) => slots switch
        {
            2 => "결승", 4 => "4강", 8 => "8강", 16 => "16강", _ => "우승"
        };

        internal static DuelOpening Opening(int slots, PresetDeckInfo deck)
        {
            if (slots == 2)
            {
                var signature = CardDatabase.Get(deck.sigCard);
                uint ace = IsOpeningAce(signature)
                    ? signature.code : (deck.extra ?? Array.Empty<uint>()).Concat(deck.main).Select(CardDatabase.Get)
                        .Where(IsOpeningAce)
                        .OrderByDescending(card => card.atk).FirstOrDefault()?.code ?? 0;
                return new DuelOpening(20000, 7, ace);
            }
            return slots == 4 ? new DuelOpening(12000, 6, 0) : new DuelOpening(8000, 5, 0);
        }

        private static bool IsOpeningAce(CardInfo card)
        {
            if (card?.IsMonster != true || (card.type & YgoConstants.TYPE_TOKEN) != 0) return false;
            string text = card.desc ?? string.Empty;
            return !text.Contains("필드 마법 카드가 앞면 표시로 존재하지 않을 경우에 이 카드는 파괴된다", StringComparison.Ordinal) &&
                !text.Contains("필드 마법 카드가 존재하지 않을 경우 이 카드를 파괴한다", StringComparison.Ordinal) &&
                !text.Contains("필드 마법 카드가 존재하지 않을 경우, 이 카드를 파괴한다", StringComparison.Ordinal);
        }
    }
}
