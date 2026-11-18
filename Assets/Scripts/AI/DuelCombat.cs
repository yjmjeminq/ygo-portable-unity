using System;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal static class DuelCombat
    {
        private const int Lethal = 100000;

        internal static int ChooseAttacker(DuelSession duel, DuelPrompt prompt, bool mustAttack)
        {
            var own = duel.Players[prompt.playerId];
            var rival = duel.Players[1 - prompt.playerId];
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
            if (difference > 0) return 900 + Threat(defender) + damage;
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
