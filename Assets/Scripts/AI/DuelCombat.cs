using System;
using System.Diagnostics;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal static class DuelCombat
    {
        private const int Lethal = 100000;
        private const int DirectOrderBudgetMilliseconds = 80;
        private const uint UtopiaDragonar = 95134948;
        private const ulong ReduceAttackerToZero = ((ulong)UtopiaDragonar << 20) | 1;

        internal static bool OpenLethal(DuelSession duel, byte player)
        {
            var rival = duel.Players[1 - player];
            return rival.lp > 0 && rival.mzone.All(card => card == null) && rival.szone.All(card => card == null) &&
                VisibleAttackTotal(duel.Players[player]) >= rival.lp;
        }

        internal static int VisibleAttackTotal(PlayerFieldSnapshot field) => field.mzone
            .Where(card => card?.IsFaceUp == true && card.IsAttack).Sum(card => Math.Max(0, card.attack));

        internal static bool IsWeakBattleLiability(int attack, int opposingAttack, int lifePoints) =>
            attack < 1800 && opposingAttack > Math.Max(0, attack) && opposingAttack >= lifePoints;

        internal static bool CanTestLethalPosition(DuelSession duel, byte player, int addedAttack)
        {
            var own = duel.Players[player];
            var rival = duel.Players[1 - player];
            var blockers = rival.mzone.Where(card => card != null).ToArray();
            if (rival.lp <= 0 || blockers.Any(card => !card.IsFaceUp)) return false;
            int availableAttack = own.mzone.Where(card => card?.IsFaceUp == true && card.IsAttack)
                .Sum(card => Math.Max(0, card.attack));
            int damageThreshold = rival.lp + blockers.Sum(card => Math.Max(0, card.IsDefense ? card.defense : card.attack));
            return availableAttack < damageThreshold && availableAttack + addedAttack >= damageThreshold;
        }

        internal static (int Attacker, uint? Target)? FindLethal(DuelSession duel, DuelPrompt prompt,
            uint previousAttacker = uint.MaxValue)
        {
            var own = duel.Players[prompt.playerId];
            var rival = duel.Players[1 - prompt.playerId];
            var attackers = prompt.attackable.Select((option, index) =>
                    (index, live: GetMonster(own, option.sequence), direct: option.directAttackable))
                .Where(entry => entry.live?.attack > 0).OrderBy(entry => entry.live.attack).ToArray();
            return FindLethal(rival, attackers, previousAttacker);
        }

        internal static bool CanCloseBattleFromMain(DuelSession duel, byte player)
        {
            var own = duel.Players[player];
            var rival = duel.Players[1 - player];
            if (rival.lp <= 0 || VisibleAttackTotal(own) < rival.lp) return false;
            bool openField = rival.mzone.All(card => card == null);
            var attackers = own.mzone.Select((live, index) => (index, live, direct: openField))
                .Where(entry => entry.live?.IsFaceUp == true && entry.live.IsAttack && entry.live.attack > 0)
                .OrderBy(entry => entry.live.attack).ToArray();
            return FindLethal(rival, attackers).HasValue;
        }

        private static (int Attacker, uint? Target)? FindLethal(PlayerFieldSnapshot rival,
            (int index, LiveCardState live, bool direct)[] attackers, uint previousAttacker = uint.MaxValue)
        {
            foreach (var entry in attackers)
                if (entry.direct && entry.live.attack >= rival.lp) return (entry.index, null);
            if (attackers.Sum(entry => entry.live.attack) < rival.lp) return null;
            var defenders = rival.mzone.Where(card => card != null).ToArray();
            if (attackers.Length == 0 || defenders.Any(card => !card.IsFaceUp)) return null;
            // An engine-offered consecutive attack can expire once a different monster attacks.
            int repeat = previousAttacker == uint.MaxValue ? -1 :
                Array.FindIndex(attackers, entry => entry.live.sequence == previousAttacker);
            if (defenders.Length == 0)
            {
                if (attackers.Where(entry => entry.direct).Sum(entry => entry.live.attack) < rival.lp) return null;
                if (repeat >= 0 && attackers[repeat].direct) return (attackers[repeat].index, null);
                return (attackers.Last(entry => entry.direct).index, null);
            }

            // Each attacker gets one attack. Recompute from the engine's legal attacks after every battle.
            int aMask = (1 << attackers.Length) - 1, dMask = (1 << defenders.Length) - 1;
            int[,] memo = new int[aMask + 1, dMask + 1];
            var breakable = defenders.Select(card =>
            {
                var text = CardDatabase.Get(card.code)?.desc;
                return card.effectsDisabled || text == null ||
                    (!text.Contains("전투로는 파괴되지", StringComparison.Ordinal) &&
                    !text.Contains("이 카드는 전투에 의해서는 파괴되지", StringComparison.Ordinal));
            }).ToArray();

            int BestDamage(int available, int blocking)
            {
                if (available == 0) return 0;
                if (memo[available, blocking] != 0) return memo[available, blocking] - 1;
                int best = 0;
                for (int i = 0; i < attackers.Length; i++)
                {
                    if ((available & (1 << i)) == 0) continue;
                    int remaining = available & ~(1 << i);
                    if (blocking == 0 || attackers[i].direct)
                        best = Math.Max(best, attackers[i].live.attack + BestDamage(remaining, blocking));
                    for (int j = 0; j < defenders.Length; j++)
                    {
                        if ((blocking & (1 << j)) == 0 || Damage(i, j) is not int hit) continue;
                        best = Math.Max(best, hit + BestDamage(remaining, AfterHit(blocking, j)));
                    }
                }
                memo[available, blocking] = best + 1;
                return best;
            }

            int? Damage(int i, int j)
            {
                var defender = defenders[j];
                int difference = attackers[i].live.attack - (defender.IsDefense ? defender.defense : defender.attack);
                return difference > 0 || difference == 0 && !defender.IsDefense && breakable[j]
                    ? (defender.IsDefense ? 0 : difference) : null;
            }

            int AfterHit(int blocking, int target) => breakable[target] ? blocking & ~(1 << target) : blocking;

            (int Attacker, uint? Target)? WinningFirstAttack(int i)
            {
                int remaining = aMask & ~(1 << i);
                if (attackers[i].direct && attackers[i].live.attack + BestDamage(remaining, dMask) >= rival.lp)
                    return (attackers[i].index, null);
                for (int j = 0; j < defenders.Length; j++)
                    if (Damage(i, j) is int hit && hit + BestDamage(remaining, AfterHit(dMask, j)) >= rival.lp)
                        return (attackers[i].index, defenders[j].sequence);
                return null;
            }
            if (repeat >= 0 && WinningFirstAttack(repeat) is { } continued) return continued;
            for (int i = 0; i < attackers.Length; i++)
                if (i != repeat && WinningFirstAttack(i) is { } attack) return attack;
            return null;
        }

        internal static int? VerifyDirectAttackOrder(DuelSession duel, DuelPrompt prompt, int preferred, TacticalAI advisor)
        {
            var own = duel.Players[prompt.playerId];
            var rival = duel.Players[1 - prompt.playerId];
            if (prompt.attackable.Count < 2 || !prompt.attackable[preferred].directAttackable ||
                GetMonster(own, prompt.attackable[preferred].sequence)?.attack >= rival.lp ||
                rival.mzone.Any(card => card != null) || rival.szone.Any(card => card != null))
                return null;

            var clock = Stopwatch.StartNew();
            bool Expired() => clock.ElapsedMilliseconds >= DirectOrderBudgetMilliseconds;
            var options = prompt.attackable.Select((card, index) => (card, index))
                .Where(entry => entry.card.directAttackable).OrderBy(entry => entry.index != preferred)
                .Take(3);
            foreach (var option in options)
            {
                using var trial = duel.ReplayForPlanning(Expired);
                if (trial?.ActivePrompt is not { messageType: YgoConstants.MSG_SELECT_BATTLECMD } start ||
                    start.playerId != prompt.playerId || option.index >= start.attackable.Count ||
                    start.attackable[option.index].sequence != option.card.sequence ||
                    !start.attackable[option.index].directAttackable)
                    break;

                var pilot = advisor.ForkForComboSearch();
                trial.BeginSpeculation(prompt.playerId, stopAfterTurn: true);
                trial.SubmitResponseInt((option.index << 16) | 1);
                for (int step = 0; step < 16 && !Expired() && !trial.IsFinished && !trial.SpeculationUncertain &&
                    trial.RetryCount == 0 && trial.TurnNumber == duel.TurnNumber; step++)
                {
                    var next = trial.ActivePrompt;
                    if (next == null || next.messageType == YgoConstants.MSG_SELECT_IDLECMD) break;
                    if (next.playerId != prompt.playerId)
                    {
                        if (next.messageType != YgoConstants.MSG_SELECT_CHAIN || next.forced) break;
                        // Project an unanswered attack. Do not look at the other player's optional cards.
                        trial.SubmitResponseInt(-1);
                        continue;
                    }
                    if (next.candidates.Any(card => card.controller != prompt.playerId &&
                        card.location is YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_DECK or YgoConstants.LOCATION_EXTRA))
                        break;
                    var response = pilot.ComputeResponse(trial, next);
                    if (next.messageType == YgoConstants.MSG_SELECT_BATTLECMD &&
                        (BitConverter.ToInt32(response, 0) & 0xffff) is 2 or 3)
                        break;
                    trial.SubmitPlanningResponse(response);
                }
                if (!trial.SpeculationUncertain && trial.RetryCount == 0 && trial.IsFinished &&
                    trial.Winner == prompt.playerId)
                    return option.index;
            }
            return preferred;
        }

        internal static int ChooseAttacker(DuelSession duel, DuelPrompt prompt, bool mustAttack)
        {
            var own = duel.Players[prompt.playerId];
            var rival = duel.Players[1 - prompt.playerId];
            if (!mustAttack && prompt.attackable.Count == 1 &&
                GetMonster(own, prompt.attackable[0].sequence) is { attack: < 1800 } &&
                rival.mzone.Any(card => card?.code == UtopiaDragonar && card.IsFaceUp && !card.effectsDisabled) &&
                !duel.HasActivatedMonsterEffect((byte)(1 - prompt.playerId), ReduceAttackerToZero) &&
                VisibleAttackTotal(rival) >= own.lp)
                return -1;

            int best = mustAttack ? int.MinValue : 0;
            int choice = mustAttack && prompt.attackable.Count > 0 ? 0 : -1;
            for (int i = 0; i < prompt.attackable.Count; i++)
            {
                var action = prompt.attackable[i];
                var attacker = GetMonster(own, action.sequence);
                if (attacker == null) continue;
                int value = int.MinValue;
                if (action.directAttackable)
                {
                    value = attacker.attack >= rival.lp ? Lethal + attacker.attack : attacker.attack + 500;
                }
                else
                {
                    foreach (var defender in rival.mzone)
                        if (defender != null) value = Math.Max(value, AttackValue(attacker, defender, rival.lp));
                }
                if (value <= best) continue;
                best = value;
                choice = i;
            }
            return choice;
        }

        internal static int ChooseTarget(DuelSession duel, DuelPrompt prompt, uint attackerSequence)
        {
            var attacker = GetMonster(duel.Players[prompt.playerId], attackerSequence);
            if (attacker == null) return -1;
            int best = int.MinValue;
            int choice = -1;
            for (int i = 0; i < prompt.candidates.Count; i++)
            {
                var action = prompt.candidates[i];
                if (action.controller == prompt.playerId || action.location != YgoConstants.LOCATION_MZONE) continue;
                var side = duel.Players[action.controller];
                var defender = GetMonster(side, action.sequence);
                if (defender == null) continue;
                int value = AttackValue(attacker, defender, side.lp);
                if (value <= best) continue;
                best = value;
                choice = i;
            }
            return choice;
        }

        private static int AttackValue(LiveCardState attacker, LiveCardState defender, int rivalLp)
        {
            // A set monster's printed stats and effects are hidden.
            if (!defender.IsFaceUp) return attacker.attack - 1550;

            int defense = defender.IsDefense ? defender.defense : defender.attack;
            int difference = attacker.attack - defense;
            int damage = defender.IsDefense ? 0 : difference;
            if (damage > 0 && damage >= rivalLp) return Lethal + damage;
            if (difference > 0)
            {
                const uint UnstoppableExodia = 83257450;
                int value = 900 + Threat(defender) + damage;
                // Exodia's visible zero DEF is a chance to remove its LP-based attack and
                // Spell/Trap negation in battle. Its printed stats understate that threat.
                if (defender.IsDefense && defender.code == UnstoppableExodia && !defender.effectsDisabled)
                    value += 1800;
                return value;
            }
            if (difference == 0) return defender.IsDefense ? -1 : Threat(defender) - Threat(attacker) - 200;
            return (defender.IsDefense ? 0 : -900 - Threat(attacker)) + difference;
        }

        private static int Threat(LiveCardState card) => Math.Max(card.attack, card.defense) / 3 +
            ((card.type & YgoConstants.TYPE_EXTRA) != 0 ? 500 : 0) +
            ((card.type & YgoConstants.TYPE_EFFECT) != 0 && !card.effectsDisabled ? 250 : 0);

        private static LiveCardState GetMonster(PlayerFieldSnapshot player, uint sequence) =>
            sequence < player.mzone.Length ? player.mzone[sequence] : null;
    }
}
