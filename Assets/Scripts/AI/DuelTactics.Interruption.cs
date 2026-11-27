using System;
using System.Collections.Generic;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal static partial class DuelTactics
    {
        private const uint FeatherDuster = 18144506, CosmicCyclone = 8267140, DarkRulerNoMore = 54693926;
        private const uint MaskChange = 21143940, FavoriteContact = 75047173, DarkLaw = 58481572;
        private const uint RemovalCategories = (1u << 0) | (1u << 1) | (1u << 2) | (1u << 3);
        private const uint BoardImpact = RemovalCategories | Development | (1u << 8) | (1u << 9) | (1u << 13);
        private const uint SummonTiming = YgoConstants.TIMING_SUMMON | YgoConstants.TIMING_SPSUMMON |
            YgoConstants.TIMING_FLIPSUMMON;

        internal static int? ChooseBoardBreaker(DuelSession duel, DuelPrompt prompt, Func<int, SelectableCardItem, bool> canUse)
        {
            byte player = prompt.playerId;
            if (duel.TurnNumber == 1 || duel.TurnPlayer != player || duel.CurrentPhase != YgoConstants.PHASE_MAIN1 ||
                duel.CurrentChain.Count != 0) return null;

            var rival = duel.Players[1 - player];
            int monsters = rival.mzone.Count(card => card != null);
            int backrow = rival.szone.Count(card => card != null);
            if (monsters + backrow == 0) return null;

            int liveBlockers = rival.mzone.Count(card => IsUsefulFieldNegationTarget(card, ForbiddenDroplet) &&
                CardDatabase.Get(card.code) is { } info && LinkComboPolicy.IsInteractive(info));
            bool floodgate = rival.szone.Any(card => card is { IsFaceUp: true, effectsDisabled: false } &&
                card.code is VanityEmptiness or Necrovalley or ImperialIronWall or 61740673 or 82732705 or 40605147);
            int best = -1, bestPriority = 0;
            for (int i = 0; i < prompt.activatable.Count; i++)
            {
                var effect = prompt.activatable[i];
                if (effect.controller != player || effect.location is not (YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_SZONE))
                    continue;

                var info = CardDatabase.Get(effect.code);
                uint name = info == null ? effect.code : CardDatabase.GetArchetypeCollectionKey(info);
                int priority = name switch
                {
                    FeatherDuster when backrow > 0 => floodgate ? 90 : backrow >= 2 ? 70 : 20,
                    CosmicCyclone or MysticalSpaceTyphoon when floodgate => 80,
                    LightningStorm when ShouldLeadWithLightningStorm(duel, prompt) || backrow >= 2 || floodgate => 65,
                    DuelAiPolicy.SuperPoly when (monsters >= 2 || liveBlockers > 0) &&
                        CanSpendUnplannedBreaker(duel, prompt, effect, canUse) => 75,
                    DarkRulerNoMore when liveBlockers >= 2 => 60,
                    ForbiddenDroplet when liveBlockers > 0 &&
                        CanSpendUnplannedBreaker(duel, prompt, effect, canUse) => 50,
                    Raigeki when monsters >= 2 || liveBlockers > 0 => 55,
                    _ => 0,
                };
                if (priority <= bestPriority || !canUse(5, effect)) continue;
                best = i;
                bestPriority = priority;
            }
            return best < 0 ? null : (best << 16) | 5;
        }

        internal static bool CanSpendUnplannedBreaker(DuelSession duel, DuelPrompt prompt, SelectableCardItem spell,
            Func<int, SelectableCardItem, bool> canUse)
        {
            byte player = prompt.playerId;
            byte rival = (byte)(1 - player);
            bool fusion = spell.code == DuelAiPolicy.SuperPoly ||
                CardDatabase.Get(spell.code) is { } info && CardDatabase.GetArchetypeCollectionKey(info) == DuelAiPolicy.SuperPoly;
            var own = duel.Players[player];
            var starters = new HashSet<uint>();
            foreach (var item in prompt.summonable)
                if (item.location == YgoConstants.LOCATION_HAND && Develops(item) && canUse(0, item)) starters.Add(item.sequence);
            foreach (var item in prompt.spSummonable)
                if (item.location == YgoConstants.LOCATION_HAND && Develops(item) && canUse(1, item)) starters.Add(item.sequence);
            bool independentAction = false;
            foreach (var item in prompt.activatable)
            {
                if (item.code == spell.code || item.code is ForbiddenDroplet or DuelAiPolicy.SuperPoly ||
                    !Develops(item) || !canUse(5, item)) continue;
                if (item.location == YgoConstants.LOCATION_HAND) starters.Add(item.sequence);
                else if (item.location is YgoConstants.LOCATION_GRAVE or YgoConstants.LOCATION_SZONE ||
                    !fusion && item.location == YgoConstants.LOCATION_MZONE) independentAction = true;
            }

            int paymentsInHand = 0;
            for (int i = 0; i < own.hand.Count; i++)
            {
                if (spell.location == YgoConstants.LOCATION_HAND && i == spell.sequence) continue;
                if (!starters.Contains((uint)i)) paymentsInHand++;
            }
            bool hasHandPayment = paymentsInHand > 0 || starters.Count >= 2 || independentAction && starters.Count > 0;
            bool hasSpentFieldPayment = !fusion && own.szone.Any(card => card != null &&
                (spell.location != YgoConstants.LOCATION_SZONE || card.sequence != spell.sequence) &&
                card.effectsDisabled);
            if (!hasHandPayment && !hasSpentFieldPayment) return false;

            // The one available starter is never the automatic payment. Native planning can
            // compare unresolved costs or a lethal that depends on resolving the breaker first.
            return starters.Count > 0 || independentAction ||
                HasReadyNegation(duel, rival, monsters: false) || HasReadyNegation(duel, rival, monsters: true);

            bool Develops(SelectableCardItem item) => item.controller == player && !ComboSearch.IsHandDisruption(item.code) &&
                ((CardDatabase.Get(item.code)?.DeckEffectCategories ?? 0) & Development) != 0;
        }

        internal static bool ShouldBaitBeforeBreaker(DuelSession duel, DuelPrompt prompt, SelectableCardItem breaker) =>
            breaker.code is not (DuelAiPolicy.SuperPoly or DarkRulerNoMore or ForbiddenDroplet) &&
            HasReadyNegation(duel, (byte)(1 - prompt.playerId), monsters: false);

        internal static bool HasPublicMonsterBlocker(DuelSession duel, byte player) =>
            duel.Players[1 - player].mzone.Any(card => IsUsefulFieldNegationTarget(card, ForbiddenDroplet) &&
                CardDatabase.Get(card.code) is { } info && LinkComboPolicy.IsInteractive(info));

        internal static bool CanProjectCostedBreakerLethal(DuelSession duel, DuelPrompt prompt, SelectableCardItem spell)
        {
            if (spell.code is not (ForbiddenDroplet or DuelAiPolicy.SuperPoly) || spell.controller != prompt.playerId || !prompt.canBp ||
                duel.TurnPlayer != prompt.playerId || duel.CurrentPhase != YgoConstants.PHASE_MAIN1) return false;
            var rival = duel.Players[1 - prompt.playerId];
            if (rival.lp <= 0) return false;
            var own = duel.Players[prompt.playerId];
            int existingAttack = DuelCombat.VisibleAttackTotal(own);
            if (spell.code == ForbiddenDroplet)
                return existingAttack >= rival.lp && rival.mzone.Any(card => IsUsefulFieldNegationTarget(card, ForbiddenDroplet));
            // This is only an upper bound. The native branch determines the legal materials and joined attack total.
            int fusionAttack = own.extra.Select(card => CardDatabase.Get(card.code))
                .Where(card => card != null && (card.type & YgoConstants.TYPE_FUSION) != 0)
                .Select(card => card.atk).DefaultIfEmpty(0).Max();
            return existingAttack + Math.Max(0, fusionAttack) >= rival.lp && DuelAiPolicy.HasOpposingFusionMaterial(own, rival);
        }

        internal static int? ChooseRecoverySummon(DuelSession duel, DuelPrompt prompt, Func<int, SelectableCardItem, bool> canUse)
        {
            byte player = prompt.playerId;
            var own = duel.Players[player];
            if (own.mzone.Any(card => card != null) || duel.CurrentPhase != YgoConstants.PHASE_MAIN1) return null;
            int pressure = StrongestAttacker(duel.Players[1 - player]);
            for (int i = 0; i < prompt.summonable.Count; i++)
            {
                var candidate = prompt.summonable[i];
                if (candidate.controller != player || candidate.location != YgoConstants.LOCATION_HAND ||
                    CardDatabase.Get(candidate.code) is not { level: <= 4 } starter ||
                    ComboSearch.IsHandDisruption(candidate.code) ||
                    (starter.DeckEffectCategories & Development) == 0 ||
                    duel.HasActivatedMonsterEffect(player, (ulong)candidate.code << 20) ||
                    pressure > starter.atk + 300 ||
                    ChooseDefensiveSet(duel, prompt, candidate, followupUnproven: true).HasValue ||
                    !canUse(0, candidate)) continue;
                return i << 16;
            }
            return null;
        }

        internal static bool CanSpendMasterInterruption(DuelSession duel, DuelPrompt prompt, SelectableCardItem effect,
            bool projected = false)
        {
            if (effect.code == NightmarePain && effect.location == YgoConstants.LOCATION_SZONE &&
                effect.description == ((ulong)NightmarePain << 20))
                return CanSpendForOptionalSearch(duel, prompt, projected);
            if (effect.code is not (AshBlossom or MaskChange or FavoriteContact or DestroyerPhoenix or DuelAiPolicy.Mirrorjade))
                return true;
            byte player = prompt.playerId;
            var source = duel.CurrentChain.LastOrDefault();
            bool opposingEffect = source is { negated: false, Resolved: false } && source.triggerPlayer != player;
            uint categories = opposingEffect ? CardDatabase.Get(source.code)?.DeckEffectCategories ?? 0 : 0;
            var own = duel.Players[player];
            var rival = duel.Players[1 - player];
            if (effect.code == AshBlossom && opposingEffect && IsSmallOptionalDraw(source, categories) && rival.hand.Count > 0 &&
                !own.mzone.Any(card => IsActive(card, NurseReficule)) && !own.szone.Any(card => IsActive(card, BadReaction)))
                return false;

            if (effect.code == AshBlossom || duel.TurnPlayer == player || effect.isTriggeredEffect || prompt.IsTriggerActivationConfirmation)
                return true;
            bool targetsOwnMonster = opposingEffect && source.PublicTargets?.Any(target => target.Controller == player &&
                target.Location == YgoConstants.LOCATION_MZONE) == true;
            bool removingCard = opposingEffect && (categories & RemovalCategories) != 0;
            bool developing = opposingEffect && ((categories & BoardImpact) != 0 ||
                IsOpponentSummoningEffect(source, player));
            bool attacking = duel.HasActiveAttack && duel.AttackerCon != player;
            bool end = duel.CurrentPhase == YgoConstants.PHASE_END ||
                (prompt.hintTimingOther & YgoConstants.TIMING_END_PHASE) != 0;
            bool summon = (prompt.hintTimingOther & SummonTiming) != 0;
            bool Available(LiveCardState card) => card != null &&
                !HasPendingFieldAnswer(duel, player, card.controller, YgoConstants.LOCATION_MZONE, card.sequence, includeNegation: false);
            bool availableMonster = rival.mzone.Any(Available);
            bool threateningMonster = rival.mzone.Any(card => card is { IsFaceUp: true } && Available(card) &&
                (card.attack >= 2000 || CardDatabase.Get(card.code) is { } info &&
                    (info.IsExtra || (info.type & YgoConstants.TYPE_RITUAL) != 0)));
            if (effect.code == DestroyerPhoenix || effect.code == DuelAiPolicy.Mirrorjade)
            {
                if (effect.description >> 20 == effect.code && (effect.description & 0xfffff) != 0) return true;
                if (!availableMonster && (effect.code != DestroyerPhoenix ||
                    !rival.szone.Any(card => card != null && !HasPendingFieldAnswer(duel, player, card.controller,
                        YgoConstants.LOCATION_SZONE, card.sequence, includeNegation: false))))
                    return false;
                return attacking || targetsOwnMonster || developing || threateningMonster || summon || end;
            }
            if (effect.code == MaskChange)
            {
                bool canMakeDarkLaw = own.extra.Any(card => card.code == DarkLaw) && own.mzone.Any(card =>
                    card is { IsFaceUp: true, attribute: YgoConstants.ATTRIBUTE_DARK } &&
                    CardDatabase.Get(card.code) is { } info && CardDatabase.ExtractSetcodes(info.setcode).Contains(HeroSet));
                return targetsOwnMonster || removingCard || attacking || end ||
                    canMakeDarkLaw && (categories & (Development | (1u << 15) | (1u << 4))) != 0;
            }
            if (effect.code == FavoriteContact)
                return targetsOwnMonster || developing || threateningMonster || attacking || end || summon && HasFieldCard(rival);
            return true;
        }

        private static bool CanSpendForOptionalSearch(DuelSession duel, DuelPrompt prompt, bool projected)
        {
            byte player = prompt.playerId;
            var own = duel.Players[player];
            if (own.hand.Any(card => card.attribute == YgoConstants.ATTRIBUTE_DARK)) return true;
            if (prompt.canBp && DuelCombat.CanCloseBattleFromMain(duel, player)) return false;
            if (projected) return true;

            double searchValue = own.hand.Count <= 1 ? 1.3 : 1.0;
            double defenseValue = own.mzone.Count(card => card != null) == 1 ? .85 : 0;
            return own.mzone.Any(card => CanDestroyForNightmarePain(card) &&
                searchValue + (HasYubelDestructionFollowup(duel, player, card.code) ? 1.75 : 0) >
                BoardAssetValue(card) + defenseValue);
        }

        private static bool IsSmallOptionalDraw(ChainLinkInfo source, uint categories)
        {
            if ((categories & BoardImpact) != 0 || (categories & (1u << 4)) == 0) return false;
            var card = CardDatabase.Get(source.code);
            if (card == null || !card.IsSpell) return false;
            string text = CardDatabase.GetDescriptionCardCode(source.description) == source.code
                ? CardDatabase.ExtractIndexedEffectDescription(source.code, (int)(source.description & 0xfffff)) ?? card.desc
                : card.desc;
            if (text == null) return false;
            return Has(text, "1장 드로우", "1 장 드로우", "draw 1 card") &&
                !Has(text, "2장 드로우", "2 장 드로우", "draw 2", "그 수만큼", "draw cards equal");
        }

        internal static int FindPublicPlanningNegate(DuelSession trial, DuelPrompt prompt, byte planner,
            HashSet<(uint Code, uint Sequence, ulong Description)> used = null)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_CHAIN || prompt.IsTriggerChain ||
                trial.CurrentChain.LastOrDefault() is not { negated: false, Resolved: false } source ||
                source.triggerPlayer != planner) return -1;
            uint impact = CardDatabase.Get(source.code)?.DeckEffectCategories ?? 0;
            if ((impact & (BoardImpact | (1u << 4))) == 0) return -1;

            // Only simulate responses that can be confirmed from face-up public cards, with no
            // choices depending on an undisclosed hand, Deck, or face-down card.
            byte rival = prompt.playerId;
            var field = trial.Players[rival];
            for (int i = 0; i < prompt.candidates.Count; i++)
            {
                var effect = prompt.candidates[i];
                if (effect.controller != rival || effect.location != YgoConstants.LOCATION_MZONE ||
                    effect.sequence >= field.mzone.Length ||
                    field.mzone[effect.sequence] is not { IsFaceUp: true, effectsDisabled: false } card ||
                    card.code != effect.code || used?.Contains((effect.code, effect.sequence, effect.description)) == true) continue;
                ulong index = effect.description & 0xfffff;
                if (effect.description >> 20 != effect.code) continue;
                if (effect.code == Baronne && index == 1 || effect.code == Blazar && index <= 2 ||
                    effect.code == Apollousa && index == 0 ||
                    effect.code is Dragoon or CyberInfinity or Dragonlord or Savage or CrystalWing or Rulkallos or
                        BlueEyesUltimateSpirit or DuelAiPolicy.HeraldOfUltimateness or DuelAiPolicy.HeraldOfPerfection or
                        80453041 or 82135803 or 79559912 ||
                    CanIdentifyPublicNegation(effect, (int)index))
                    return i;
            }
            return -1;
        }

        private static bool CanIdentifyPublicNegation(SelectableCardItem effect, int index)
        {
            var info = CardDatabase.Get(effect.code);
            string rules = info?.desc;
            if (rules == null || (info.DeckEffectCategories & (1u << 8)) == 0 ||
                Has(rules, "패를", "패에서", "패의", "discard", "from your hand", "from the hand")) return false;

            // The script labels the offered effect. Printed numbering may include passive effects the script skips.
            string label = info.strs?.ElementAtOrDefault(index);
            if (string.IsNullOrWhiteSpace(label)) return false;
            return Has(label, "negate activation", "negate the activation", "발동을 무효") ||
                label.Trim().Equals("Negate", StringComparison.OrdinalIgnoreCase) && Has(rules, "그 발동을 무효", "negate the activation");
        }
    }
}
