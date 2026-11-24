using System;
using System.Collections.Generic;

namespace YGOPortable.Core
{
    internal readonly struct StoryRaid
    {
        private static readonly Dictionary<string, uint[]> ChapterCards = new(StringComparer.Ordinal)
        {
            ["dm"] = new uint[] { 43175858, 89631139, 11587414, 48179391, 79791878, 10000000 },
            ["gx"] = new uint[] { 35809262, 80312545, 23846921, 78371393, 60417395, 89943723 },
            ["5ds"] = new uint[] { 70902743, 39823987, 60634565, 93483212, 70902743, 44508094 },
            ["zexal"] = new uint[] { 93717133, 97403510, 49456901, 12744567, 847915, 26973555 },
            ["arcv"] = new uint[] { 46372010, 16195942, 80666118, 41209827, 79559912, 16178681 },
            ["vrains"] = new uint[] { 31833038, 85289965, 13203964, 13203964, 59054773, 86066372 },
        };

        private static readonly Dictionary<(string Series, int Episode), uint> MajorBossCards = new()
        {
            [("5ds", 27)] = 44710391,
            [("5ds", 45)] = 8967776,
            [("zexal", 44)] = 41418852,
            [("arcv", 44)] = 13331639,
        };

        private static readonly int[] Hand = { 6, 7, 8, 9, 10 };
        private static readonly int[] Rewards = { 1800, 2500, 3500, 4500, 6000 };

        internal readonly DuelOpening Opening;
        internal readonly int WinDp;
        internal string Summary => $"BOSS RAID  ·  시작 패 {Opening.Hand}장\n선배치 카드 · {CardDatabase.GetName(Opening.FieldCard)}";

        private StoryRaid(int chapter, uint fieldCard)
        {
            Opening = new DuelOpening(8000, Hand[chapter - 1], fieldCard);
            WinDp = Rewards[chapter - 1];
        }

        internal static StoryRaid? For(string series, int episode)
        {
            if (episode is < 1 or > 50 || !ChapterCards.TryGetValue(series, out var cards)) return null;
            int chapter = (episode - 1) / 10 + 1;
            if (MajorBossCards.TryGetValue((series, episode), out uint major)) return new StoryRaid(chapter, major);
            if (episode == 49) return new StoryRaid(chapter, cards[^2]);
            if (episode % 10 != 0) return null;
            return new StoryRaid(chapter, cards[episode == 50 ? cards.Length - 1 : chapter - 1]);
        }
    }
}
