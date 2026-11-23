using System;

namespace YGOPortable.Core
{
    internal enum StoryStance { Neutral, Challenge, Calm, Bond }

    [Flags]
    internal enum RaidSupport { None = 0, QuickDraw = 1, Aegis = 2, Awakening = 4, Breaker = 8, Victory = 16 }

    internal readonly struct StoryBattlePlan
    {
        internal readonly StoryStance Stance;
        internal readonly RaidSupport Supports;

        internal StoryBattlePlan(StoryStance stance, RaidSupport supports)
        {
            Stance = stance;
            Supports = supports;
        }

        internal (DuelOpening Player, DuelOpening Opponent) Apply(StoryRaid? raid)
        {
            var boss = raid?.Opening ?? new DuelOpening(8000, 5, 0);
            int playerLp = Has(RaidSupport.Aegis) ? 12000 : Stance == StoryStance.Calm ? 9000 : 8000;
            int opponentLp = Has(RaidSupport.Breaker) && raid.HasValue ? boss.Life * 4 / 5 : boss.Life;
            if (Stance == StoryStance.Challenge) opponentLp += 2000;
            bool awakening = Has(RaidSupport.Awakening);
            return (new DuelOpening(playerLp, Has(RaidSupport.QuickDraw) ? 7 : 5, 0, awakening ? 4000 : 2000, awakening ? 2 : 1),
                new DuelOpening(opponentLp, boss.Hand, boss.FieldCard));
        }

        internal int ScaleWinReward(int amount)
        {
            if (Stance == StoryStance.Challenge) amount = amount * 3 / 2;
            return Has(RaidSupport.Victory) ? amount * 2 : amount;
        }

        internal int ThemeDropChance => Stance == StoryStance.Bond ? 30 : 15;
        private bool Has(RaidSupport item) => (Supports & item) == item;
    }

    internal static class RaidSupportCatalog
    {
        internal static readonly (RaidSupport Id, string Name, string Description, int Cost)[] Items =
        {
            (RaidSupport.QuickDraw, "속공 지원 드로우", "시작 패 7장", 350),
            (RaidSupport.Aegis, "이지스 라이프 실드", "시작 LP 12,000으로 고정", 550),
            (RaidSupport.Awakening, "운명의 각성 인장", "데스티니 드로우 LP 4,000 이하 · 최대 2회", 650),
            (RaidSupport.Breaker, "초기 필드 브레이커", "보스 기본 시작 LP 20% 감소", 700),
            (RaidSupport.Victory, "황금의 승리 휘장", "승리 DP·가루 2배", 800),
        };

        internal static int IndexOf(RaidSupport item) => Array.FindIndex(Items, entry => entry.Id == item);
        internal static bool IsSelectionValid(RaidSupport items)
        {
            const RaidSupport all = RaidSupport.QuickDraw | RaidSupport.Aegis | RaidSupport.Awakening |
                RaidSupport.Breaker | RaidSupport.Victory;
            int bits = (int)items;
            if ((items & ~all) != 0) return false;
            if (bits == 0) return true;
            bits &= bits - 1;
            return (bits & (bits - 1)) == 0;
        }
    }
}
