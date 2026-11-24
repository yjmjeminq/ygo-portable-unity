using System;
using System.Linq;

namespace YGOPortable.Core
{
    internal enum StoryStance { Neutral, Challenge, Calm, Bond }
    internal enum StoryOpponentOpening { Standard, ExtraHand, PreplacedMonster, ExtraHandAndMonster }

    [Flags]
    internal enum RaidSupport { None = 0, QuickDraw = 1, Aegis = 2, Awakening = 4, Breaker = 8, Victory = 16 }

    internal readonly struct StoryBattlePlan
    {
        internal const int ExtraOpeningCards = 2;
        internal readonly StoryStance Stance;
        internal readonly RaidSupport Supports;
        internal readonly StoryOpponentOpening OpponentOpening;

        internal StoryBattlePlan(StoryStance stance, RaidSupport supports,
            StoryOpponentOpening opponentOpening = StoryOpponentOpening.Standard)
        {
            Stance = stance;
            Supports = supports;
            OpponentOpening = stance == StoryStance.Challenge && opponentOpening == StoryOpponentOpening.Standard
                ? StoryOpponentOpening.ExtraHand : opponentOpening;
        }

        internal (DuelOpening Player, DuelOpening Opponent) Apply(StoryRaid? raid, PresetDeckInfo deck = null)
        {
            var boss = raid?.Opening ?? new DuelOpening(8000, 5, 0);
            int playerLp = Has(RaidSupport.Aegis) ? 12000 : Stance == StoryStance.Calm ? 9000 : 8000;
            int opponentHand = boss.Hand;
            uint baseFieldCard = Has(RaidSupport.Breaker) ? 0 : boss.FieldCard;
            uint preplaced = OpponentOpening is StoryOpponentOpening.PreplacedMonster or StoryOpponentOpening.ExtraHandAndMonster
                ? SelectOpeningMonster(deck, raid) : 0;
            if (OpponentOpening is StoryOpponentOpening.ExtraHand or StoryOpponentOpening.ExtraHandAndMonster ||
                (Stance == StoryStance.Challenge && preplaced == 0))
                opponentHand += ExtraOpeningCards;
            bool awakening = Has(RaidSupport.Awakening);
            return (new DuelOpening(playerLp, Has(RaidSupport.QuickDraw) ? 7 : 5, 0, awakening ? 4000 : 2000, awakening ? 2 : 1),
                new DuelOpening(boss.Life, opponentHand, baseFieldCard, deckMonster: preplaced));
        }

        internal static uint SelectOpeningMonster(PresetDeckInfo deck, StoryRaid? raid)
        {
            if (deck?.main == null) return 0;
            uint bossCard = raid?.Opening.FieldCard ?? 0;
            // A preplaced Xyz monster would lack materials for its effects.
            return deck.main.Concat(deck.extra ?? Array.Empty<uint>()).Select(CardDatabase.Get)
                .Where(card => card?.IsMonster == true && !card.IsXyz && card.atk > 0 &&
                    (card.type & YgoConstants.TYPE_TOKEN) == 0 && card.code != bossCard)
                .OrderByDescending(card => card.code == deck.sigCard)
                .ThenByDescending(card => card.atk)
                .FirstOrDefault()?.code ?? 0;
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
            (RaidSupport.Breaker, "초기 필드 브레이커", "레이드 보스의 기본 선배치 카드 제거", 700),
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
