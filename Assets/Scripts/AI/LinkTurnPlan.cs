using System;
using System.Collections.Generic;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal static class LinkTurnPlan
    {
        private const uint SalamangreatSet = 0x119;
        private const uint Balelynx = 14812471;
        private const uint SunlightWolf = 87871125;
        private const uint Heatleo = 41463181;
        private const uint Miragestallio = 87327776;
        private const uint Circle = 52155219;
        private const uint Sanctuary = 1295111;
        private const uint Gazelle = 26889158;
        private const uint Spinny = 52277807;
        private const uint Jaguar = 56003780;
        private const uint Foxy = 94620082;
        private const uint SalamangreatOfFire = 11962031;
        private const uint Draconnet = 62706865;
        private const uint BackupSecretary = 63528891;
        private const uint LinkSlayer = 35595518;
        private const uint Dotscaper = 18789533;
        private const uint LinkSpider = 98978921;
        private const uint ProxyDragon = 22862454;
        private const uint DecodeTalker = 1861629;
        private const uint EncodeTalker = 6622715;
        private const int LinkMaterial = 533;

        internal static int? ChooseIdle(DuelSession session, DuelPrompt prompt, Func<int, SelectableCardItem, bool> canUse)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_IDLECMD) return null;
            var own = session.Players[prompt.playerId];
            bool salamangreat = HasLinkAccess(own, SunlightWolf) || HasLinkAccess(own, Balelynx);
            bool cyberse = HasLinkAccess(own, LinkSpider) && HasLinkAccess(own, DecodeTalker);
            if (!salamangreat && !cyberse) return null;

            var rival = session.Players[1 - prompt.playerId];
            var field = own.mzone.Where(card => card?.IsFaceUp == true).ToArray();

            int? Find(List<SelectableCardItem> list, int command, Func<SelectableCardItem, bool> matches)
            {
                int index = list.FindIndex(card => card.controller == prompt.playerId && matches(card) && canUse(command, card));
                return index < 0 ? null : (index << 16) | command;
            }
            int? Normal(uint code) => Find(prompt.summonable, 0, card => card.code == code);
            int? Special(uint code) => Find(prompt.spSummonable, 1, card => card.code == code);
            int? Effect(uint code, uint location) => Find(prompt.activatable, 5, card =>
                card.code == code && card.location == location);

            bool OnField(uint code) => field.Any(card => card.code == code);
            bool InHand(uint code) => own.hand.Any(card => card.code == code);
            bool OpponentBackrow() => rival.szone.Take(5).Any(card => card != null);

            if (salamangreat)
            {
                bool hasSalamangreat = field.Any(card => IsSalamangreat(card.code));
                bool wolfOnField = OnField(SunlightWolf);
                bool hasGazelle = InHand(Gazelle) || OnField(Gazelle);
                bool sanctuaryReady = own.szone.Any(card => card?.IsFaceUp == true && card.code == Sanctuary);
                bool rivalBackrow = OpponentBackrow();

                // Put the starter in hand before turning a body into a Link monster that can trigger it.
                if (!hasGazelle)
                {
                    var search = Effect(Circle, YgoConstants.LOCATION_HAND) ?? Effect(Circle, YgoConstants.LOCATION_SZONE);
                    if (search.HasValue) return search;
                }
                if (field.Length == 0 || !hasSalamangreat)
                {
                    var starter = Normal(SalamangreatOfFire) ??
                        (InHand(Gazelle) ? Normal(Spinny) ?? Normal(Foxy) ?? Normal(Jaguar) : null) ??
                        Normal(Gazelle) ?? Normal(Spinny) ?? Normal(Foxy);
                    if (starter.HasValue) return starter;
                }
                if (!sanctuaryReady)
                {
                    var fieldSpell = Effect(Sanctuary, YgoConstants.LOCATION_HAND) ?? Effect(Sanctuary, YgoConstants.LOCATION_SZONE);
                    if (fieldSpell.HasValue) return fieldSpell;
                }

                // Link-1 opens the Sanctuary route and sends a starter to the graveyard.
                if (!OnField(Balelynx) && !wolfOnField && (!sanctuaryReady || !hasSalamangreat || InHand(Gazelle)))
                {
                    var linkStarter = Special(Balelynx);
                    if (linkStarter.HasValue) return linkStarter;
                }

                // Resolve monsters that replace themselves before spending their potential materials.
                var extension = Effect(Spinny, YgoConstants.LOCATION_GRAVE) ??
                    Effect(Jaguar, YgoConstants.LOCATION_GRAVE) ?? Effect(Foxy, YgoConstants.LOCATION_GRAVE) ??
                    Special(BackupSecretary) ?? Effect(Miragestallio, YgoConstants.LOCATION_MZONE);
                if (extension.HasValue) return extension;
                var clearBackrow = rivalBackrow && !OnField(Heatleo) ? Special(Heatleo) : null;
                if (!OnField(Miragestallio) && !wolfOnField && !OnField(Heatleo) && own.deckCount > 0 &&
                    !clearBackrow.HasValue)
                {
                    var deployFromDeck = Special(Miragestallio);
                    if (deployFromDeck.HasValue) return deployFromDeck;
                }

                // Use Heatleo when it can clear an opposing back row; build Wolf's recycling engine otherwise.
                if (clearBackrow.HasValue) return clearBackrow;
                if (!wolfOnField && !OnField(Heatleo))
                {
                    var recursion = Special(SunlightWolf);
                    if (recursion.HasValue) return recursion;
                }
                if (wolfOnField && own.grave.Any(card => IsSalamangreat(card.code) && CardDatabase.Get(card.code)?.IsMonster == false))
                {
                    var recycleBackrow = Effect(SunlightWolf, YgoConstants.LOCATION_MZONE) ??
                        (sanctuaryReady ? Special(SunlightWolf) : null);
                    if (recycleBackrow.HasValue) return recycleBackrow;
                }
                if (wolfOnField && !rivalBackrow && prompt.canEp && !prompt.canBp && prompt.spSummonable.Count != 0 &&
                    prompt.spSummonable.All(card => card.code is Heatleo or Balelynx or SunlightWolf || !canUse(1, card)) &&
                    prompt.summonable.Count == 0 && prompt.sSetable.Count == 0 &&
                    !prompt.activatable.Any(card => canUse(5, card)))
                    return 7;
                return null;
            }

            // Linkslayer needs an empty board. Draconnet can still supply materials after it arrives.
            if (field.Length == 0 && Special(LinkSlayer) is int opening) return opening;
            if (OnField(LinkSlayer) && OpponentBackrow() && own.hand.Count > 1 &&
                Effect(LinkSlayer, YgoConstants.LOCATION_MZONE) is int clear)
                return clear;
            if (OnField(LinkSpider) && Effect(LinkSpider, YgoConstants.LOCATION_MZONE) is int unload)
                return unload;
            if (Normal(Draconnet) is int draconnet) return draconnet;
            if (Special(BackupSecretary) is int freeCyberse) return freeCyberse;
            if (!OnField(LinkSpider) && own.hand.Any(card => IsSmallNormal(card.code, 4)))
            {
                var makeExtender = Special(LinkSpider);
                if (makeExtender.HasValue) return makeExtender;
            }

            // Choose a useful Link endpoint before trading bodies for a vanilla Link-3.
            if (!OnField(DecodeTalker) && !OnField(EncodeTalker))
            {
                bool rivalHasMonster = rival.mzone.Any(card => card?.IsFaceUp == true);
                var boss = rivalHasMonster ? Special(EncodeTalker) ?? Special(DecodeTalker) :
                    Special(DecodeTalker) ?? Special(EncodeTalker);
                if (boss.HasValue) return boss;
            }
            if (field.Length >= 2 && !OnField(DecodeTalker) && !OnField(EncodeTalker) &&
                own.hand.Any(card => card.code == BackupSecretary) && !field.Any(card => IsCyberse(card.code)))
                return Special(ProxyDragon);
            return null;
        }

        internal static int? ChooseCard(DuelSession session, DuelPrompt prompt)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_CARD || prompt.min != 1 || prompt.max != 1 ||
                prompt.candidates.Count == 0)
                return null;
            var effect = session.SelectionChainLink;
            if (effect == null || effect.triggerPlayer != prompt.playerId || effect.negated ||
                prompt.candidates.Any(card => card.controller != prompt.playerId))
                return null;

            var own = session.Players[prompt.playerId];
            bool AllFrom(uint location) => prompt.candidates.All(card => card.location == location);
            int? Candidate(uint code)
            {
                int index = prompt.candidates.FindIndex(card => card.code == code);
                return index < 0 ? null : index;
            }

            if (effect.code is Circle or Miragestallio or SalamangreatOfFire && AllFrom(YgoConstants.LOCATION_DECK))
            {
                if (!own.hand.Any(card => card.code == Gazelle) &&
                    !own.mzone.Any(card => card?.IsFaceUp == true && card.code == Gazelle))
                    return Candidate(Gazelle) ?? Candidate(Spinny) ?? Candidate(Jaguar);
                return Candidate(Spinny) ?? Candidate(Jaguar) ?? Candidate(Foxy);
            }
            if (effect.code == Gazelle && AllFrom(YgoConstants.LOCATION_DECK))
            {
                if (!own.grave.Any(card => card.code == Spinny) && Candidate(Spinny) is int extender) return extender;
                if (own.mzone.Any(card => card?.IsFaceUp == true && IsSalamangreat(card.code) &&
                    CardDatabase.Get(card.code)?.IsLink == true) &&
                    !own.grave.Any(card => card.code == Jaguar))
                    return Candidate(Jaguar);
            }
            if (effect.code == SunlightWolf && session.LastHintMsg == 506 && AllFrom(YgoConstants.LOCATION_GRAVE))
            {
                // Wolf locks the chosen monster's name from summoning in this turn. Use Gazelle
                // as next-turn follow-up once a copy is already established on the field.
                if (own.mzone.Any(card => card?.IsFaceUp == true && card.code == Gazelle))
                    return Candidate(Gazelle);
            }
            if (effect.code == Draconnet && prompt.candidates.All(card =>
                card.location is YgoConstants.LOCATION_DECK or YgoConstants.LOCATION_HAND))
            {
                int body = prompt.candidates.FindIndex(card =>
                    card.location == YgoConstants.LOCATION_DECK && IsSmallNormal(card.code, 2) && IsCyberse(card.code));
                if (body < 0)
                    body = prompt.candidates.FindIndex(card => IsSmallNormal(card.code, 2) && IsCyberse(card.code));
                return body < 0 ? null : body;
            }
            return null;
        }

        internal static int? ChooseLinkMaterial(DuelSession session, DuelPrompt prompt)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_UNSELECT_CARD || session.LastHintMsg != LinkMaterial ||
                prompt.finishable || prompt.candidates.Count == 0 ||
                prompt.candidates.Any(card => card.controller != prompt.playerId || card.location != YgoConstants.LOCATION_MZONE))
                return null;
            var own = session.Players[prompt.playerId];
            bool salamangreat = HasLinkAccess(own, SunlightWolf) || HasLinkAccess(own, Balelynx);
            bool cyberse = HasLinkAccess(own, LinkSpider) && HasLinkAccess(own, DecodeTalker);
            if (!salamangreat && !cyberse) return null;

            // The engine controls material legality at every step. Save the established boss
            // when one of the offered smaller bodies can be used instead.
            int recycler = prompt.candidates.FindIndex(card => salamangreat
                ? card.code is Spinny or Balelynx : card.code == Dotscaper);
            if (recycler >= 0) return recycler;
            int other = prompt.candidates.FindIndex(card => salamangreat
                ? card.code is not (SunlightWolf or Heatleo or Miragestallio)
                : card.code is not (DecodeTalker or EncodeTalker));
            return other < 0 ? null : other;
        }

        private static bool HasLinkAccess(PlayerFieldSnapshot own, uint code) =>
            own.extra.Any(card => card.code == code) ||
            own.mzone.Any(card => card?.IsFaceUp == true && card.code == code);

        private static bool IsCyberse(uint code) => CardDatabase.Get(code)?.race == YgoConstants.RACE_CYBERSE;

        private static bool IsSmallNormal(uint code, uint maxLevel) => CardDatabase.Get(code) is CardInfo card && card.IsMonster &&
            (card.type & YgoConstants.TYPE_NORMAL) != 0 && card.level > 0 && card.level <= maxLevel;

        private static bool IsSalamangreat(uint code)
        {
            ulong sets = CardDatabase.Get(code)?.setcode ?? 0;
            while (sets != 0)
            {
                if ((sets & 0x0fff) == SalamangreatSet) return true;
                sets >>= 16;
            }
            return false;
        }
    }
}
