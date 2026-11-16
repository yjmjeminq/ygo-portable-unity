using System;
using System.Collections.Generic;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal static class SynchroTurnPlan
    {
        private const uint MoYe = 20001443;
        private const uint Taia = 56495147;
        private const uint Longyuan = 93490856;
        private const uint Emergence = 56465981;
        private const uint Ecclesia = 55273560;
        private const uint Blackout = 14821890;
        private const uint Chixiao = 69248256;
        private const uint Qixing = 47710198;
        private const uint Chengying = 96633955;
        private const uint Baxia = 83755611;
        private const uint Baronne = 84815190;

        private const uint BlackWhirlwind = 91351370;
        private const uint Blizzard = 22835145;
        private const uint Gale = 2009101;
        private const uint Bora = 49003716;
        private const uint Oroshi = 73652465;
        private const uint Nothung = 95040215;
        private const uint Raikiri = 16051717;
        private const uint ArmorMaster = 69031175;
        private const uint Chidori = 23338098;
        private const uint ArmedWing = 76913983;

        private const uint SwordsoulSet = 0x16d;
        private const uint BlackwingSet = 0x33;
        private const int NormalSummon = 0;
        private const int SpecialSummon = 1;
        private const int Activate = 5;
        private const ulong DescriptionIndexMask = (1UL << 20) - 1;

        internal static bool CanActivate(DuelSession session, DuelPrompt prompt, SelectableCardItem card)
        {
            if (card.code != Chixiao || !IsDescription(card.description, Chixiao, 1)) return true;
            var enemy = session.CurrentChain.LastOrDefault();
            return enemy != null && !enemy.negated && enemy.triggerPlayer != prompt.playerId &&
                enemy.triggerLoc == YgoConstants.LOCATION_MZONE &&
                session.Players[enemy.triggerPlayer].mzone.Any(monster => monster?.IsFaceUp == true &&
                    monster.sequence == enemy.triggerSeq && monster.code == enemy.code);
        }

        internal static int? ChooseIdle(DuelSession session, DuelPrompt prompt,
            Func<int, SelectableCardItem, bool> canUse)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_IDLECMD) return null;
            var own = session.Players[prompt.playerId];
            var rival = session.Players[1 - prompt.playerId];
            if (IsSwordsoul(own))
            {
                var action = PlanSwordsoul(own, rival, prompt, canUse);
                if (action.HasValue) return action;
            }
            return IsBlackwing(own) ? PlanBlackwing(own, rival, prompt, canUse) : null;
        }

        private static int? PlanSwordsoul(PlayerFieldSnapshot own, PlayerFieldSnapshot rival,
            DuelPrompt prompt, Func<int, SelectableCardItem, bool> canUse)
        {
            int? Normal(uint code) => Pick(prompt.summonable, NormalSummon, prompt, canUse,
                card => card.code == code);
            int? Synchro(uint code) => Pick(prompt.spSummonable, SpecialSummon, prompt, canUse,
                card => card.code == code && card.location == YgoConstants.LOCATION_EXTRA);
            int? Effect(uint code, uint location, int index) => Pick(prompt.activatable, Activate, prompt, canUse,
                card => card.location == location && IsDescription(card.description, code, index));

            var board = FaceUpMonsters(own);
            bool hasChixiao = board.Any(card => card.code == Chixiao);
            int rivalCards = OccupiedZones(rival);
            var chixiao = Synchro(Chixiao);
            var qixing = Synchro(Qixing);

            // With separate material pairs, Qixing can draw when Chixiao follows it.
            if (!hasChixiao && qixing.HasValue && chixiao.HasValue && HasTwoWyrmSynchroPairs(board) && rivalCards < 2)
                return qixing;

            var baxia = Synchro(Baxia);
            if (rivalCards >= 2 && baxia.HasValue) return baxia;
            if (!hasChixiao && chixiao.HasValue) return chixiao;

            var baronne = Synchro(Baronne);
            if (rivalCards > 0 && baronne.HasValue && !board.Any(card => card.code == Baronne))
                return baronne;
            if (qixing.HasValue && !board.Any(card => card.code == Qixing)) return qixing;

            var chengying = Synchro(Chengying);
            bool canBanish = own.grave.Any(IsSwordsoulMaterial) &&
                (board.Any(card => card.code is Chixiao or Taia) || own.hand.Any(card => card.code == Taia));
            if (chengying.HasValue && canBanish && !board.Any(card => card.code == Chengying)) return chengying;

            bool hasMoYe = own.hand.Any(card => card.code == MoYe);
            bool canReveal = HasMoYeReveal(own);
            bool hasGraveCost = own.grave.Any(IsSwordsoulMaterial);
            var taia = Effect(Taia, YgoConstants.LOCATION_MZONE, 0);
            if (hasGraveCost && taia.HasValue) return taia;

            var ecclesiaEffect = Effect(Ecclesia, YgoConstants.LOCATION_MZONE, 1);
            if (!hasChixiao && ecclesiaEffect.HasValue) return ecclesiaEffect;
            var ecclesiaSpecial = Pick(prompt.spSummonable, SpecialSummon, prompt, canUse,
                card => card.code == Ecclesia && card.location == YgoConstants.LOCATION_HAND);
            bool canSummonStarterFromDeck = !hasChixiao &&
                (own.hand.Any(card => IsSwordsoulMaterial(card) && !card.isPublic) || hasGraveCost);
            if (canSummonStarterFromDeck && ecclesiaSpecial.HasValue) return ecclesiaSpecial;

            if (hasMoYe && canReveal)
            {
                var normal = Normal(MoYe);
                if (normal.HasValue) return normal;
            }
            if (hasGraveCost)
            {
                var normal = Normal(Taia);
                if (normal.HasValue) return normal;
            }
            if (canSummonStarterFromDeck)
            {
                var normal = Normal(Ecclesia);
                if (normal.HasValue) return normal;
            }

            // An interrupted Normal Summon may leave Longyuan as the remaining tuner line.
            var emergence = Effect(Emergence, YgoConstants.LOCATION_HAND, 0) ?? Effect(Emergence, YgoConstants.LOCATION_SZONE, 0);
            var longyuan = Effect(Longyuan, YgoConstants.LOCATION_HAND, 0);
            bool hasOtherDiscard = own.hand.Count(IsSwordsoulMaterial) >= 3;
            if (longyuan.HasValue && (hasOtherDiscard || hasChixiao || !emergence.HasValue)) return longyuan;
            if (emergence.HasValue) return emergence;
            if (baxia.HasValue && rivalCards > 0) return baxia;
            return baronne ?? chengying ?? chixiao;
        }

        private static int? PlanBlackwing(PlayerFieldSnapshot own, PlayerFieldSnapshot rival,
            DuelPrompt prompt, Func<int, SelectableCardItem, bool> canUse)
        {
            int? Normal(Func<SelectableCardItem, bool> fits) => Pick(prompt.summonable, NormalSummon, prompt, canUse,
                card => InSet(card.code, BlackwingSet) && fits(card));
            int? Synchro(uint code) => Pick(prompt.spSummonable, SpecialSummon, prompt, canUse,
                card => card.code == code && card.location == YgoConstants.LOCATION_EXTRA);
            int? Effect(uint code, uint location) => Pick(prompt.activatable, Activate, prompt, canUse,
                card => card.code == code && card.location == location);

            var board = FaceUpMonsters(own);
            if (rival.mzone.Any(card => card?.IsFaceUp == true && card.attack > 0))
            {
                var halve = Effect(Gale, YgoConstants.LOCATION_MZONE);
                if (halve.HasValue) return halve;
            }
            int faceUpBlackwings = board.Count(card => InSet(card.code, BlackwingSet));
            var raikiri = Synchro(Raikiri);
            int rivalCards = OccupiedZones(rival);
            if (raikiri.HasValue && rivalCards > 0 && faceUpBlackwings > 2) return raikiri;

            bool extraNormalAvailable = own.hand.Any(card => InSet(card.code, BlackwingSet)) &&
                !board.Any(card => card.code == Nothung);
            var nothung = Synchro(Nothung);
            if (nothung.HasValue && extraNormalAvailable) return nothung;

            var normalStarter = Normal(card => card.code == Blizzard && own.grave.Any(material =>
                IsBlackwingRevivalTarget(material.code) && CanBuildBlackwingSynchro(own, Blizzard, material.code))) ??
                Normal(card => own.hand.Any(extender => extender.code != card.code && IsBlackwingExtender(extender.code) &&
                    CanBuildBlackwingSynchro(own, card.code, extender.code)));
            if (normalStarter.HasValue && !own.szone.Any(card => card?.code == BlackWhirlwind && card.IsFaceUp))
            {
                var whirlwind = Effect(BlackWhirlwind, YgoConstants.LOCATION_HAND) ??
                    Effect(BlackWhirlwind, YgoConstants.LOCATION_SZONE);
                if (whirlwind.HasValue) return whirlwind;
            }

            var extension = Pick(prompt.spSummonable, SpecialSummon, prompt, canUse,
                card => card.location == YgoConstants.LOCATION_HAND && IsBlackwingExtender(card.code) &&
                    board.Any(material => CanBuildBlackwingSynchro(own, material.code, card.code, material)));
            if (extension.HasValue && (!raikiri.HasValue || rivalCards > 0)) return extension;
            if (rivalCards > 0)
            {
                var battle = Synchro(ArmorMaster) ?? Synchro(Chidori);
                if (battle.HasValue) return battle;
            }
            return nothung ?? raikiri ?? normalStarter ?? extension ?? Synchro(ArmedWing);
        }

        internal static int? ChooseCard(DuelSession session, DuelPrompt prompt)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_CARD || prompt.min != 1 || prompt.max != 1 ||
                prompt.candidates.Count == 0) return null;
            var source = session.SelectionChainLink;
            if (source == null || source.triggerPlayer != prompt.playerId || source.negated) return null;

            bool OnlyFrom(uint location) => prompt.candidates.All(card =>
                card.controller == prompt.playerId && card.location == location);
            var own = session.Players[prompt.playerId];
            if (IsSwordsoul(own))
            {
                if (OnlyFrom(YgoConstants.LOCATION_DECK) &&
                    ((source.code == Emergence && IsDescription(source.description, Emergence, 0)) ||
                     (source.code == Chixiao && IsDescription(source.description, Chixiao, 0))))
                    return ChooseSwordsoulSearch(own, prompt.candidates);

                if (source.code == Ecclesia && source.triggerLoc == YgoConstants.LOCATION_MZONE &&
                    IsDescription(source.description, Ecclesia, 1) && prompt.candidates.All(card =>
                        card.controller == prompt.playerId && card.location is YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_DECK))
                {
                    return Find(prompt.candidates, card => card.code == MoYe && CanRevealAfterSummon(own, card)) ??
                        (own.grave.Any(IsSwordsoulMaterial) ? Find(prompt.candidates, card => card.code == Taia) : null);
                }
            }

            if (!IsBlackwing(own)) return null;
            if (source.code == BlackWhirlwind && source.triggerLoc == YgoConstants.LOCATION_SZONE &&
                OnlyFrom(YgoConstants.LOCATION_DECK) && prompt.candidates.All(card => InSet(card.code, BlackwingSet)))
            {
                var board = FaceUpMonsters(own);
                return Find(prompt.candidates, card => IsBlackwingExtender(card.code) &&
                    !own.hand.Any(held => held.code == card.code) && board.Any(material => material.code != card.code &&
                        InSet(material.code, BlackwingSet) && CanBuildBlackwingSynchro(own, material.code, card.code, material))) ??
                    Find(prompt.candidates, card => card.code == Blizzard && own.grave.Any(material =>
                        IsBlackwingRevivalTarget(material.code) && CanBuildBlackwingSynchro(own, Blizzard, material.code)));
            }
            if (source.code == Blizzard && OnlyFrom(YgoConstants.LOCATION_GRAVE))
                return Find(prompt.candidates, card => IsBlackwingRevivalTarget(card.code) &&
                    CanBuildBlackwingSynchro(own, Blizzard, card.code));
            return null;
        }

        private static int? ChooseSwordsoulSearch(PlayerFieldSnapshot own, List<SelectableCardItem> choices)
        {
            bool Has(uint code) => own.hand.Any(card => card.code == code);
            int? Search(uint code) => Find(choices, card => card.code == code);
            bool hasLongyuan = Has(Longyuan);
            bool hasChixiao = FaceUpMonsters(own).Any(card => card.code == Chixiao);
            bool hasDiscard = own.hand.Any(card => card.code != Longyuan && IsSwordsoulMaterial(card));
            bool roomForTwo = own.mzone.Take(5).Count(card => card == null) >= 2;
            if (!hasLongyuan && hasDiscard && roomForTwo && (hasChixiao || Has(MoYe) || Has(Taia)))
            {
                var extender = Search(Longyuan);
                if (extender.HasValue) return extender;
            }
            if (!hasChixiao && !Has(MoYe) && own.hand.Any(card => IsSwordsoulMaterial(card) && !card.isPublic))
            {
                var starter = Search(MoYe);
                if (starter.HasValue) return starter;
            }
            if (!hasChixiao && !Has(Taia) && own.grave.Any(IsSwordsoulMaterial))
            {
                var starter = Search(Taia);
                if (starter.HasValue) return starter;
            }
            if (hasChixiao && hasLongyuan && hasDiscard && !Has(Blackout) &&
                !own.szone.Any(card => card?.code == Blackout))
            {
                var trap = Search(Blackout);
                if (trap.HasValue) return trap;
            }
            if (hasLongyuan && !hasDiscard) return Search(MoYe) ?? Search(Taia);
            return hasDiscard && !hasLongyuan && roomForTwo ? Search(Longyuan) : null;
        }

        private static bool IsSwordsoul(PlayerFieldSnapshot own) =>
            (own.extra.Any(card => card.code is Chixiao or Qixing or Chengying) ||
             own.mzone.Any(card => card?.code is Chixiao or Qixing or Chengying)) &&
            own.hand.Concat(own.grave).Concat(FaceUpMonsters(own)).Any(card =>
                card.code == Ecclesia || InSet(card.code, SwordsoulSet));

        private static bool IsBlackwing(PlayerFieldSnapshot own) =>
            (own.extra.Any(card => card.code is Raikiri or Nothung or ArmorMaster or ArmedWing) ||
             own.mzone.Any(card => card?.code is Raikiri or Nothung or ArmorMaster or ArmedWing)) &&
            own.hand.Concat(own.grave).Concat(FaceUpMonsters(own)).Any(card => InSet(card.code, BlackwingSet));

        private static bool HasMoYeReveal(PlayerFieldSnapshot own)
        {
            var available = own.hand.Where(card => !card.isPublic && IsSwordsoulMaterial(card)).ToArray();
            return available.Any(card => card.code != MoYe) || available.Length >= 2 ||
                (available.Length == 1 && own.hand.Any(card => card.code == MoYe && card.isPublic));
        }

        private static bool CanRevealAfterSummon(PlayerFieldSnapshot own, SelectableCardItem summon) =>
            own.hand.Any(card => IsSwordsoulMaterial(card) && !card.isPublic &&
                (summon.location == YgoConstants.LOCATION_DECK || card.sequence != summon.sequence));

        private static bool IsSwordsoulMaterial(LiveCardState card) => InSet(card.code, SwordsoulSet) ||
            (card.race == YgoConstants.RACE_WYRM || CardDatabase.Get(card.code)?.race == YgoConstants.RACE_WYRM);

        private static bool HasTwoWyrmSynchroPairs(LiveCardState[] board) =>
            board.Count(card => card.level == 4 && (card.type & (YgoConstants.TYPE_TUNER | YgoConstants.TYPE_TOKEN)) ==
                (YgoConstants.TYPE_TUNER | YgoConstants.TYPE_TOKEN)) >= 2 &&
            board.Any(card => card.level == 4 && card.race == YgoConstants.RACE_WYRM &&
                (card.type & YgoConstants.TYPE_TUNER) == 0) &&
            board.Any(card => card.code == Longyuan && card.level == 6 && (card.type & YgoConstants.TYPE_TUNER) == 0);

        private static bool IsBlackwingExtender(uint code) => code is Gale or Bora or Oroshi;

        private static bool IsBlackwingRevivalTarget(uint code)
        {
            var card = CardDatabase.Get(code);
            return InSet(code, BlackwingSet) && card != null && card.level is > 0 and <= 4 &&
                (card.type & (YgoConstants.TYPE_MONSTER | YgoConstants.TYPE_TUNER)) == YgoConstants.TYPE_MONSTER;
        }

        private static bool CanBuildBlackwingSynchro(PlayerFieldSnapshot own, uint leftCode, uint rightCode,
            LiveCardState liveLeft = null)
        {
            var left = CardDatabase.Get(leftCode);
            var right = CardDatabase.Get(rightCode);
            if (left == null || right == null) return false;
            uint leftType = liveLeft?.type > 0 ? liveLeft.type : left.type;
            uint leftLevel = liveLeft?.level > 0 ? liveLeft.level : left.level;
            if (leftLevel == 0 || right.level == 0 || (leftType & YgoConstants.TYPE_MONSTER) == 0 ||
                ((leftType ^ right.type) & YgoConstants.TYPE_TUNER) == 0) return false;
            uint targetLevel = leftLevel + right.level;
            bool blackwingTuner = InSet((leftType & YgoConstants.TYPE_TUNER) != 0 ? leftCode : rightCode, BlackwingSet);
            return own.extra.Any(card => card.code switch
            {
                Nothung => targetLevel == 6,
                ArmedWing => targetLevel == 6 && blackwingTuner,
                Raikiri or Chidori => targetLevel == 7,
                ArmorMaster => targetLevel == 7 && blackwingTuner,
                _ => false,
            });
        }

        private static LiveCardState[] FaceUpMonsters(PlayerFieldSnapshot player) =>
            player.mzone.Where(card => card?.IsFaceUp == true).ToArray();

        private static int OccupiedZones(PlayerFieldSnapshot player) =>
            player.mzone.Count(card => card != null) + player.szone.Count(card => card != null);

        private static int? Pick(List<SelectableCardItem> choices, int command, DuelPrompt prompt,
            Func<int, SelectableCardItem, bool> canUse, Predicate<SelectableCardItem> fits)
        {
            var index = Find(choices, card => card.controller == prompt.playerId && fits(card) && canUse(command, card));
            return index.HasValue ? (index.Value << 16) | command : null;
        }

        private static int? Find(List<SelectableCardItem> choices, Predicate<SelectableCardItem> fits)
        {
            int index = choices.FindIndex(fits);
            return index < 0 ? null : index;
        }

        private static bool InSet(uint code, uint archetype)
        {
            ulong setcode = CardDatabase.Get(code)?.setcode ?? 0;
            for (; setcode != 0; setcode >>= 16)
                if ((setcode & 0xfff) == archetype) return true;
            return false;
        }

        private static bool IsDescription(ulong description, uint code, int index) =>
            CardDatabase.GetDescriptionCardCode(description) == code &&
            (description & DescriptionIndexMask) == (uint)index;
    }
}
