using System;
using UnityEngine;
using YGOPortable.AI;
using YGOPortable.Core;

namespace YGOPortable.Editor
{
    internal static class TestOtherDeckTurnPlans
    {
        public static void Run()
        {
            VerifySalamangreat();
            VerifySwordsoul();
            VerifySpright();
            VerifyTearlaments();
            VerifyPendulum();
            Debug.Log("[VERIFY OTHER DECK PLANS] Link, Synchro, Xyz, Fusion and Pendulum decisions passed.");
        }

        private static void VerifySalamangreat()
        {
            using var duel = new DuelSession();
            var own = duel.Players[0];
            own.mzone[0] = Live(52277807);
            own.mzone[1] = Live(26889158);
            own.extra.AddRange(new[] { Live(41463181), Live(87871125) });
            var prompt = Idle();
            prompt.spSummonable.AddRange(new[] { Extra(41463181), Extra(87871125) });
            Expect(duel, prompt, (1 << 16) | 1, "Salamangreat should prepare Sunlight Wolf's recovery.");

            duel.Players[1].szone[2] = new LiveCardState { position = YgoConstants.POS_FACEDOWN_DEFENSE };
            Expect(duel, prompt, 1, "Salamangreat should pick Heatleo when there is backrow to remove.");
        }

        private static void VerifySwordsoul()
        {
            using var duel = new DuelSession();
            var own = duel.Players[0];
            own.mzone[0] = Live(20001443);
            own.extra.AddRange(new[] { Live(83755611), Live(69248256) });
            var prompt = Idle();
            prompt.spSummonable.AddRange(new[] { Extra(83755611), Extra(69248256) });
            Expect(duel, prompt, (1 << 16) | 1, "Swordsoul should set up Chixiao on an empty rival board.");

            duel.Players[1].mzone[0] = Live(89631139);
            duel.Players[1].szone[2] = new LiveCardState { position = YgoConstants.POS_FACEDOWN_DEFENSE };
            Expect(duel, prompt, 1, "Swordsoul should choose Baxia to remove opposing cards.");

            prompt.spSummonable.Clear();
            prompt.summonable.Add(Hand(20001443));
            prompt.activatable.Add(new SelectableCardItem
            {
                code = 69248256, location = YgoConstants.LOCATION_MZONE, description = (69248256UL << 20) | 1,
            });
            var longyuan = Hand(93490856);
            longyuan.description = (ulong)longyuan.code << 20;
            prompt.activatable.Add(longyuan);
            Expect(duel, prompt, (1 << 16) | 5, "Swordsoul should use Longyuan after losing its first Synchro line.");

            prompt.activatable.RemoveAt(1);
            prompt.summonable.Clear();
            Expect(duel, prompt, 7, "Chixiao should keep its negate when there is no monster effect to answer.");
        }

        private static void VerifySpright()
        {
            using var duel = new DuelSession();
            var own = duel.Players[0];
            own.mzone[0] = Live(68353324);
            own.extra.Add(Live(54498517));
            var prompt = Idle();
            prompt.spSummonable.AddRange(new[] { Extra(54498517), Hand(76145933) });
            Expect(duel, prompt, (1 << 16) | 1, "Spright should summon Blue before spending its Level 2 material.");

            prompt.spSummonable.RemoveAt(1);
            Expect(duel, prompt, 1, "Spright should make Gigantic once the hand extender is unavailable.");

            own.mzone[1] = Live(54498517);
            prompt.spSummonable.Clear();
            prompt.spSummonable.Add(Extra(70369116));
            Expect(duel, prompt, 7, "Spright should keep Gigantic when the spare body cannot make a follow-up.");
        }

        private static void VerifyTearlaments()
        {
            using var duel = new DuelSession();
            duel.Players[0].extra.Add(Live(92731385));
            var prompt = Idle();
            prompt.summonable.Add(Hand(73956664));
            prompt.activatable.Add(Hand(77103950));
            Expect(duel, prompt, 5, "Tearlaments should search before committing its Normal Summon.");

            prompt.activatable.Clear();
            Expect(duel, prompt, 0, "Tearlaments should open the mill route with Reinoheart.");
        }

        private static void VerifyPendulum()
        {
            const uint stargazer = 94415058, timegazer = 20409757, oddEyes = 16178681;
            using var duel = new DuelSession();
            var own = duel.Players[0];
            own.szone[6] = Live(stargazer);
            own.hand.AddRange(new[] { Live(stargazer, 0), Live(timegazer, 1), Live(oddEyes, 2) });
            var prompt = Idle();
            var first = Hand(stargazer);
            var second = Hand(timegazer);
            first.description = second.description = 1160;
            second.sequence = 1;
            prompt.activatable.AddRange(new[] { first, second });
            Expect(duel, prompt, (1 << 16) | 5, "Pendulum should use a scale that reaches the monster in hand.");

            own.szone[7] = Live(timegazer);
            prompt.activatable.Clear();
            prompt.spSummonable.Add(Extra(91949988));
            prompt.spSummonable.Add(new SelectableCardItem { code = timegazer, location = YgoConstants.LOCATION_SZONE });
            Expect(duel, prompt, (1 << 16) | 1, "Pendulum should deploy legal Pendulum materials before the Extra Deck summon.");
        }

        private static DuelPrompt Idle() => new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };

        private static SelectableCardItem Hand(uint code) => new SelectableCardItem { code = code, location = YgoConstants.LOCATION_HAND };

        private static SelectableCardItem Extra(uint code) => new SelectableCardItem { code = code, location = YgoConstants.LOCATION_EXTRA };

        private static LiveCardState Live(uint code, uint sequence = 0)
        {
            var card = CardDatabase.Get(code);
            return new LiveCardState
            {
                code = code, sequence = sequence, position = YgoConstants.POS_FACEUP_ATTACK,
                level = card.level, lscale = card.lscale, rscale = card.rscale,
            };
        }

        private static void Expect(DuelSession duel, DuelPrompt prompt, int expected, string message)
        {
            int response = BitConverter.ToInt32(new TacticalAI().ComputeResponse(duel, prompt), 0);
            if (response != expected) throw new InvalidOperationException($"{message} Got {response}, expected {expected}.");
        }
    }
}
