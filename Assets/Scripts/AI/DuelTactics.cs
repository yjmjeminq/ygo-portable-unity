using System;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal static class DuelTactics
    {
        internal const ulong Release = 500, Discard = 501, Destroy = 502, Banish = 503, SendToGrave = 504,
            ReturnToHand = 505, ReturnToDeck = 507, Negate = 575, ReturnToGrave = 579;
        private const uint DarkHole = 53129443, TorrentialTribute = 53582587;

        internal static bool CanActivate(DuelSession duel, DuelPrompt prompt, SelectableCardItem effect)
        {
            if (effect.code is not (DarkHole or TorrentialTribute)) return true;
            var own = duel.Players[prompt.playerId];
            var rival = duel.Players[1 - prompt.playerId];
            // Save whole-field removal when it would only clear our own monsters.
            return !own.mzone.Any(card => card != null) || rival.mzone.Any(card => card != null);
        }

        internal static bool IsRemoval(ulong hint) => hint is Destroy or Banish or SendToGrave or ReturnToHand or
            ReturnToDeck or Negate or ReturnToGrave;

        internal static int SelectionPriority(DuelSession duel, byte player, SelectableCardItem option, ulong hint)
        {
            if (option.controller >= duel.Players.Length) return -5000;
            bool own = option.controller == player;
            var side = duel.Players[option.controller];
            var live = option.location switch
            {
                YgoConstants.LOCATION_MZONE when option.sequence < side.mzone.Length => side.mzone[option.sequence],
                YgoConstants.LOCATION_SZONE when option.sequence < side.szone.Length => side.szone[option.sequence],
                _ => null,
            };
            bool visible = own || live?.IsFaceUp == true || option.location == YgoConstants.LOCATION_GRAVE;
            var card = visible ? CardDatabase.Get(option.code) : null;
            int attack = live != null && visible ? live.attack : card?.atk ?? 1000;
            int value = Math.Max(0, attack);
            if (card != null && (card.type & YgoConstants.TYPE_EFFECT) != 0 && live?.effectsDisabled != true) value += 350;
            if (card?.IsExtra == true) value += 500;

            if (hint == Discard && own)
            {
                int copies = side.hand.Count(candidate => candidate.code == option.code);
                return -value + (copies > 1 ? 1200 : 0);
            }
            if (IsRemoval(hint) || hint is Release or 511 or 512 or 513 or 533)
                return own ? -5000 - value : 10000 + value;
            return own ? 10000 + value : -5000 + value;
        }

        internal static int? ChoosePosition(DuelSession duel, DuelPrompt prompt)
        {
            const uint both = YgoConstants.POS_FACEUP_ATTACK | YgoConstants.POS_FACEUP_DEFENSE;
            if ((prompt.allowedPositions & both) != both || duel.TurnNumber <= 1 || duel.TurnPlayer != prompt.playerId)
                return null;
            var card = CardDatabase.Get(prompt.promptCardCode);
            var rivals = duel.Players[1 - prompt.playerId].mzone;
            if (card == null || card.atk <= 0 || duel.CurrentPhase is not (YgoConstants.PHASE_MAIN1 or YgoConstants.PHASE_BATTLE))
                return null;

            bool canInflictDamage = !rivals.Any(rival => rival != null) || rivals.Any(rival => rival?.IsFaceUp == true &&
                rival.IsAttack && card.atk > rival.attack);
            return canInflictDamage ? (int)YgoConstants.POS_FACEUP_ATTACK : null;
        }
    }
}
