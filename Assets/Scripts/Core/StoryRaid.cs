namespace YGOPortable.Core
{
    internal readonly struct StoryRaid
    {
        private static readonly int[] Hand = { 6, 7, 8, 9, 10 };
        private static readonly int[] Rewards = { 1800, 2500, 3500, 4500, 6000 };

        internal readonly DuelOpening Opening;
        internal readonly int WinDp;
        internal string Summary => $"BOSS RAID  ·  시작 LP {Opening.Life:N0}  ·  시작 패 {Opening.Hand}장" +
            (Opening.FieldCard == 0 ? "" : $"\n선배치 카드 · {CardDatabase.GetName(Opening.FieldCard)}");

        private StoryRaid(int chapter, uint fieldCard, int life)
        {
            Opening = new DuelOpening(life, Hand[chapter - 1], fieldCard);
            WinDp = Rewards[chapter - 1];
        }

        internal static StoryRaid ForEncounter(int episode, uint fieldCard, int life = 8000) =>
            new StoryRaid(1 + (episode - 1) * Rewards.Length / StoryCampaign.EpisodeCount, fieldCard, life > 0 ? life : 8000);
    }
}
