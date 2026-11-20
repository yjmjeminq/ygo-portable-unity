using System;
using System.Collections.Generic;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    /// <summary>
    /// Opening combo & endboard routing for iconic Anime + Manga signature archetypes
    /// (HERO / Masked HERO, Dark Magician, Blue-Eyes, Stardust / Junk Synchron, Red Dragon Archfiend).
    /// </summary>
    internal static class SignatureTurnPlan
    {
        private const int NormalSummon = 0;
        private const int SpecialSummon = 1;
        private const int Activate = 5;

        // HERO / Vision / Masked HERO (Judai / Edo)
        private const uint Faris = 18094166;
        private const uint Increase = 22865492;
        private const uint Vyon = 27780618;
        private const uint ShadowMist = 50720316;
        private const uint Stratos = 40044918;
        private const uint AHeroLives = 8949584;
        private const uint FusionDestiny = 52947044;
        private const uint Polymerization = 24094653;
        private const uint MiracleFusion = 45906428;
        private const uint MaskChange = 21143940;
        private const uint DarkLaw = 58481572;
        private const uint DestroyerPhoenix = 60461804;
        private const uint Sunrise = 22908820;
        private const uint WakeUpHero = 32828466;
        private const uint Malicious = 9411399;
        private const uint Denier = 16605586;

        // Dark Magician (Yami Yugi)
        private const uint IllusionOfChaos = 12266229;
        internal const uint MagiciansSouls = 97631303;
        private const uint MagiciansRod = 7084129;
        private const uint DarkMagicalCircle = 47222536;
        private const uint Dragoon = 37818794;
        private const uint DestructionMagician = 59400890;
        private const uint GallopingGaia = 2106266, SpiralFusion = 38590361;
        private const uint MagicalKnightGaia = 34130561, CursedDragon = 61525276, GaiaFusion = 15989522;
        private const uint CatapultTurtle = 7913375;

        // Blue-Eyes (Seto Kaiba)
        private const uint SageWithEyesOfBlue = 8240199;
        private const uint SpiritWithEyesOfBlue = 42097666;
        private const uint BlueEyesSpiritDragon = 59822133;
        private const uint BlueEyesJetDragon = 30576089;
        private const uint BlueEyesTyrant = 11443677;
        private const uint MajestyRoar = 17725109;
        private const uint MaidenOfWhite = 17947697, PrayersWithEyesOfBlue = 80326401, UltimateFusion = 71143015;
        private const uint DragonDictator = 66961194;
        private const uint TrueLight = 62089826, Kaibaman = 67768675;

        private const uint GalaxyHundred = 897409, GalaxyWizard = 98555327, GalaxySummoner = 863795;
        private const uint GalaxyExpedition = 60222582, GalaxyZero = 97617181;
        private const uint PhotonJumper = 97639441, PhotonEmperor = 73478096, PhotonDragon = 93717133;
        private const uint PrimePhoton = 31801517, NeoPrimePhoton = 48348921;
        private const uint Deadnader = 34909328, DuoDrive = 7511613;

        // Stardust / Junk Synchron (Yusei Fudo)
        private const uint Tuning = 96363153;
        private const uint StardustIlluminate = 37750912;
        private const uint SynchroOvertake = 99243014;
        private const uint JunkConverter = 11069680;
        private const uint JunkSynchron = 63977008;
        private const uint Doppelwarrior = 53855409;
        private const uint JunkSpeeder = 77075360;
        private const uint AccelSynchroStardust = 30983281;
        private const uint CrimsonDragon = 63436931;
        private const uint CosmicBlazer = 21123811;
        private const uint StardustChronicle = 89474727;
        private const uint StardustSpark = 83994433;
        private const uint Baronne = 84815190;

        // Red Dragon Archfiend / Resonator (Jack Atlas)
        private const uint SoulResonator = 62991792;
        private const uint BoneArchfiend = 25784595;
        private const uint CrimsonGaia = 98173209;
        internal const uint ScarredDragonArchfiend = 87451661;
        internal const uint RedDragonAbyss = 9753964;
        private const uint RedDragonBane = 36857073;
        private const uint RedSupernova = 99585850;
        private const uint ScarlightOrHotRed = 39765958;

        // Light and Darkness Dragonlord (Manjoume) & Playmaker Cyberse
        private const uint DragonsMirror = 71490127;
        private const uint LightEndSublimation = 35614780;
        private const uint DarkEndEvaporation = 62002838;
        private const uint LaddLord = 19652159;
        private const uint CynetMining = 57160136;
        private const uint SplashMage = 59859086;
        private const uint TranscodeTalker = 46947713;
        private const uint DecodeTalkerHeatsoul = 61245672;
        private const uint FirewallSingularity = 21637210;
        private const uint FirewallNeoTempest = 64211118;
        private const uint FirewallDarkfluid = 68934651;
        private const uint FirewallDragon = 5043010;
        private const uint AccesscodeTalker = 86066372;

        private const uint MonarchDomain = 84171830, MonarchPantheism = 22842126, MonarchPrime = 54241725;
        private const uint GracefulCharity = 79571449;

        private static bool HasMonarchPackage(DuelSession session, byte player)
        {
            var deck = session.StartingMainDeck(player);
            return session.Players[player].extra.Count == 0 && deck.Contains(MonarchDomain) &&
                deck.Contains(MonarchPantheism) && deck.Contains(MonarchPrime);
        }

        private static bool IsMonarchBoss(uint code) => CardDatabase.Get(code) is
            { IsMonster: true, level: >= 6, def: 1000 } card && card.atk is 2400 or 2800;

        private static bool IsMonarchSupport(uint code)
        {
            var card = CardDatabase.Get(code);
            if (card == null || (!card.IsSpell && !card.IsTrap)) return false;
            for (ulong sets = card.setcode; sets != 0; sets >>= 16)
                if ((sets & 0x0fff) == 0xbe) return true;
            return false;
        }

        private static bool HasMonarchDomain(PlayerFieldSnapshot own) =>
            own.hand.Any(card => card.code == MonarchDomain) || own.szone.Any(card => card?.code == MonarchDomain);

        internal static int? ChooseMonarchOpening(DuelSession session, DuelPrompt prompt,
            Func<int, SelectableCardItem, bool> canUse)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_IDLECMD || canUse == null ||
                !HasMonarchPackage(session, prompt.playerId)) return null;
            var own = session.Players[prompt.playerId];
            var bosses = own.hand.Where(card => IsMonarchBoss(card.code)).ToArray();
            bool fieldBoss = own.mzone.Any(card => card?.IsFaceUp == true && IsMonarchBoss(card.code));
            if (bosses.Length == 0 && !fieldBoss) return null;

            int? Effect(uint code, uint location) {
                int index = prompt.activatable.FindIndex(card => card.controller == prompt.playerId &&
                    card.code == code && card.location == location && canUse(Activate, card));
                return index >= 0 ? (index << 16) | Activate : null;
            }
            bool missingDomain = !HasMonarchDomain(own);
            bool activeDomain = own.szone.Any(card => card is
                { code: MonarchDomain, IsFaceUp: true, effectsDisabled: false });
            // Do not replace an unrelated Field Spell or spend another copy of an established Domain.
            if (!activeDomain && own.szone[YgoConstants.FIELD_ZONE_SEQUENCE] == null &&
                Effect(MonarchDomain, YgoConstants.LOCATION_HAND) is int domain) return domain;

            // Graceful can put Prime in the GY while retaining Pantheism and its draw cost.
            // Spending Pantheism first instead removes that preparation and changes the draw order.
            if (bosses.Length > 0 && !own.mzone.Take(5).Any(card => card != null) &&
                !own.grave.Any(card => card.code == MonarchPrime) &&
                own.hand.Any(card => card.code == MonarchPantheism) &&
                own.hand.Any(card => card.code != MonarchPantheism && IsMonarchSupport(card.code)) &&
                (own.hand.Any(card => card.code == MonarchPrime) ||
                    session.HasCardInDeck(prompt.playerId, card => card.code == MonarchPrime)) &&
                Effect(GracefulCharity, YgoConstants.LOCATION_HAND) is int prepareGraceful)
                return prepareGraceful;

            if (missingDomain && session.HasCardInDeck(prompt.playerId, card => card.code == MonarchDomain))
            {
                if (Effect(MonarchPantheism, YgoConstants.LOCATION_GRAVE) is int search) return search;
                if (Effect(MonarchPantheism, YgoConstants.LOCATION_HAND) is int draw) return draw;
            }

            // The native list, rather than a guessed tribute count, proves an available Tribute Summon.
            int ready = prompt.summonable.FindIndex(card => card.controller == prompt.playerId &&
                IsMonarchBoss(card.code) && canUse(NormalSummon, card));
            if (ready >= 0) return ready << 16;
            if (bosses.Length == 0) return null;

            int bodies = own.mzone.Take(5).Count(card => card != null);
            bool primeFuel = own.grave.Any(card => card.code == MonarchPrime) &&
                own.grave.Any(card => card.code != MonarchPrime && IsMonarchSupport(card.code));
            bool domainBoss = bosses.Any(card => CardDatabase.Get(card.code) is { atk: 2800, def: 1000 });
            if (activeDomain && domainBoss && (bodies > 0 || primeFuel) &&
                Effect(MonarchDomain, YgoConstants.LOCATION_SZONE) is int reduce) return reduce;

            bool oneBodyHelps = bosses.Any(card => card.level is > 0 and <= 6) || bodies > 0 ||
                (activeDomain && domainBoss);
            if (primeFuel && oneBodyHelps &&
                Effect(MonarchPrime, YgoConstants.LOCATION_GRAVE) is int revive) return revive;

            // Put Prime in the GY through a legal draw cost only when a tribute body is still missing.
            if (bodies == 0 && own.hand.Any(card => card.code == MonarchPrime) &&
                Effect(MonarchPantheism, YgoConstants.LOCATION_HAND) is int prepare) return prepare;
            return null;
        }

        internal static int[] ChooseMonarchSetupCards(DuelSession session, DuelPrompt prompt)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_CARD || !HasMonarchPackage(session, prompt.playerId) ||
                session.SelectionChainLink is not { negated: false } source || source.triggerPlayer != prompt.playerId)
                return null;
            var own = session.Players[prompt.playerId];
            if (source.code == MonarchPantheism && source.triggerLoc == YgoConstants.LOCATION_GRAVE &&
                prompt.SelectionHint == DuelTactics.AddToHand && prompt.min == 3 && prompt.max == 3 &&
                !HasMonarchDomain(own) && (own.hand.Any(card => IsMonarchBoss(card.code)) ||
                    own.mzone.Any(card => card?.IsFaceUp == true && IsMonarchBoss(card.code))))
            {
                // All three public choices are Domain: even the opponent's least helpful choice finds it.
                int[] domains = prompt.candidates.Select((card, index) => (card, index))
                    .Where(entry => entry.card.controller == prompt.playerId &&
                        entry.card.location == YgoConstants.LOCATION_DECK && entry.card.code == MonarchDomain)
                    .Take(3).Select(entry => entry.index).ToArray();
                return domains.Length == 3 ? domains : null;
            }

            if (source.code != GracefulCharity || prompt.SelectionHint != DuelTactics.Discard ||
                prompt.min != 2 || prompt.max != 2 ||
                prompt.candidates.Any(card => card.controller != prompt.playerId || card.location != YgoConstants.LOCATION_HAND) ||
                !own.hand.Any(card => IsMonarchBoss(card.code)) ||
                !own.hand.Any(card => card.code == MonarchPantheism) ||
                own.mzone.Take(5).Count(card => card != null) >= 2 ||
                own.grave.Any(card => card.code == MonarchPrime)) return null;

            int prime = prompt.candidates.FindIndex(card => card.code == MonarchPrime);
            if (prime < 0) return null;
            int second = -1, best = int.MinValue;
            for (int i = 0; i < prompt.candidates.Count; i++)
            {
                var card = prompt.candidates[i];
                if (i == prime || card.code is MonarchPantheism or MonarchDomain ||
                    IsMonarchBoss(card.code) || DuelDeckStrategy.IsKeyPiece(card.code)) continue;
                var remaining = prompt.candidates.Where((_, index) => index != prime && index != i).ToArray();
                // Retain both Pantheism and its separate Monarch S/T activation cost.
                if (!remaining.Any(item => item.code == MonarchPantheism) ||
                    !remaining.Any(item => item.code != MonarchPantheism && IsMonarchSupport(item.code) &&
                        (item.code != MonarchDomain || remaining.Count(other => other.code == MonarchDomain) > 1))) continue;

                int value = card.code switch
                {
                    42141493 => 900,
                    87126721 => 850,
                    84192580 => 800,
                    _ when ComboSearch.IsHandDisruption(card.code) => 100,
                    _ when prompt.candidates.Count(other => other.code == card.code) > 1 => 50,
                    _ => int.MinValue,
                };
                if (value <= best) continue;
                best = value;
                second = i;
            }
            return second >= 0 ? new[] { prime, second } : null;
        }

        internal static int? ChooseResourceOpening(DuelSession session, DuelPrompt prompt, Func<int, SelectableCardItem, bool> canUse)
        {
            int best = -1, priority = 0;
            for (int i = 0; i < prompt.activatable.Count; i++)
            {
                var action = prompt.activatable[i];
                if (action.controller != prompt.playerId || action.location is not (YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_SZONE))
                    continue;
                int value = action.code switch
                {
                    DuelDeckStrategy.RunickFountain when action.location == YgoConstants.LOCATION_HAND => 4,
                    DuelDeckStrategy.Engage or 63166096 or 63166097 => 3,
                    DuelDeckStrategy.RunickTip => 2,
                    _ when DuelDeckStrategy.RequiresOpenMainZone(action.code) &&
                        (CardDatabase.Get(action.code)?.type & YgoConstants.TYPE_QUICKPLAY) == 0 => 1,
                    _ => 0,
                };
                if (value <= priority || !canUse(Activate, action)) continue;
                best = i;
                priority = value;
            }
            if (best >= 0) return (best << 16) | Activate;
            if (session.TurnNumber != 1) return null;

            int shizuku = prompt.spSummonable.FindIndex(action => action.controller == prompt.playerId &&
                action.location == YgoConstants.LOCATION_EXTRA && action.code == DuelDeckStrategy.StrikerShizuku &&
                canUse(SpecialSummon, action));
            return shizuku >= 0 ? (shizuku << 16) | SpecialSummon : null;
        }

        internal static int? ChooseIdle(DuelSession session, DuelPrompt prompt, Func<int, SelectableCardItem, bool> canUse)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_IDLECMD) return null;

            int? Find(List<SelectableCardItem> list, int command, Func<SelectableCardItem, bool> matches)
            {
                int index = list.FindIndex(item => item.controller == prompt.playerId && matches(item) && canUse(command, item));
                return index < 0 ? null : (index << 16) | command;
            }

            int? Normal(uint code) => Find(prompt.summonable, NormalSummon, item => item.code == code);
            int? Special(uint code) => Find(prompt.spSummonable, SpecialSummon, item => item.code == code);
            int? ExtraSpecial(uint code) => Find(prompt.spSummonable, SpecialSummon,
                item => item.code == code && item.location == YgoConstants.LOCATION_EXTRA);
            int? Effect(uint code, uint location = 0, int effect = -1) => Find(prompt.activatable, Activate,
                item => item.code == code && (location == 0 || item.location == location) &&
                    (effect < 0 || item.description == (((ulong)code << 20) | (uint)effect)));

            var own = session.Players[prompt.playerId];
            var rival = session.Players[1 - prompt.playerId];
            int rivalMonsters = rival.mzone.Count(c => c != null);
            int rivalBackrow = rival.szone.Take(5).Count(c => c != null);
            bool rivalHeavyMonster = rival.mzone.Any(m => m?.IsFaceUp == true && Math.Max(m.attack, m.defense) >= 2200);
            bool isFirstTurnSetup = !prompt.canBp && rivalMonsters == 0 && rivalBackrow == 0;
            bool isBreakerTurn = prompt.canBp && (rivalMonsters > 0 || rivalBackrow > 0);

            // 0. Clear dangerous opponent backrow / board BEFORE committing combo starters!
            if (rivalBackrow > 0)
            {
                var featherDuster = Effect(18144506, YgoConstants.LOCATION_HAND) ?? Effect(18144506, YgoConstants.LOCATION_SZONE);
                if (featherDuster.HasValue) return featherDuster;
            }
            if (rivalHeavyMonster && (Effect(12580477, YgoConstants.LOCATION_HAND) ?? Effect(25311006, YgoConstants.LOCATION_HAND)) is int boardBreak)
                return boardBreak;

            // 0b. Draw & 60-card Grass mill boosters first (Pot of Greed / Graceful Charity / That Grass Looks Greener / Reasoning)
            var drawBoost = Effect(55144522, YgoConstants.LOCATION_HAND)
                ?? Effect(55144522, YgoConstants.LOCATION_SZONE)
                ?? Effect(79571449, YgoConstants.LOCATION_HAND)
                ?? Effect(79571449, YgoConstants.LOCATION_SZONE)
                ?? Effect(11110587, YgoConstants.LOCATION_HAND)
                ?? Effect(11110587, YgoConstants.LOCATION_SZONE)
                ?? Effect(58577036, YgoConstants.LOCATION_HAND);
            if (drawBoost.HasValue) return drawBoost;

            // 1. HERO / Masked HERO line (Adapts between Turn 1 Dark Law / DPE control vs Going-2nd Wake Up / Acid / Sunrise OTK):
            var heroBoss = isFirstTurnSetup
                ? (ExtraSpecial(DarkLaw) ?? ExtraSpecial(DestroyerPhoenix) ?? ExtraSpecial(Sunrise))
                : (ExtraSpecial(DestroyerPhoenix) ?? ExtraSpecial(WakeUpHero) ?? ExtraSpecial(Sunrise) ?? ExtraSpecial(DarkLaw));
            var heroMove =
                Effect(AHeroLives, YgoConstants.LOCATION_HAND)
                ?? Effect(Increase, YgoConstants.LOCATION_SZONE)
                ?? Effect(Faris, YgoConstants.LOCATION_HAND)
                ?? Special(Faris)
                ?? Normal(Stratos)
                ?? Effect(Vyon, YgoConstants.LOCATION_MZONE)
                ?? Normal(Vyon)
                ?? Effect(Malicious, YgoConstants.LOCATION_GRAVE)
                ?? Effect(Denier, YgoConstants.LOCATION_GRAVE)
                ?? Effect(Polymerization, YgoConstants.LOCATION_HAND)
                ?? Effect(MiracleFusion, YgoConstants.LOCATION_HAND)
                ?? heroBoss;
            if (heroMove.HasValue) return heroMove;

            bool hasMaskTarget = own.mzone.Any(m => m?.IsFaceUp == true &&
                m.code is ShadowMist or Faris or Vyon or Increase or Stratos or Malicious or Denier or 59392529);
            if (hasMaskTarget && (!own.mzone.Any(m => m?.IsFaceUp == true && m.code == DarkLaw) || rivalBackrow >= 2))
            {
                var maskChange = Effect(MaskChange, YgoConstants.LOCATION_HAND) ?? Effect(MaskChange, YgoConstants.LOCATION_SZONE);
                if (maskChange.HasValue) return maskChange;
            }

            var fusionDestiny = Effect(FusionDestiny, YgoConstants.LOCATION_HAND);
            if (fusionDestiny.HasValue) return fusionDestiny;

            var gaia = Effect(SpiralFusion, YgoConstants.LOCATION_HAND) ??
                Effect(GallopingGaia, YgoConstants.LOCATION_HAND) ?? Effect(GallopingGaia, YgoConstants.LOCATION_SZONE) ??
                Effect(CatapultTurtle, YgoConstants.LOCATION_MZONE) ??
                Normal(MagicalKnightGaia);
            if (gaia.HasValue) return gaia;

            // 2. Dark Magician line: Illusion of Chaos -> Magicians' Souls -> Rod -> Circle -> Dragoon
            var dmMove =
                Effect(IllusionOfChaos, YgoConstants.LOCATION_HAND)
                ?? Effect(MagiciansSouls, YgoConstants.LOCATION_HAND)
                ?? Effect(DarkMagicalCircle, YgoConstants.LOCATION_HAND)
                ?? Normal(MagiciansRod)
                ?? ExtraSpecial(Dragoon)
                ?? ExtraSpecial(DestructionMagician);
            if (dmMove.HasValue) return dmMove;

            // 3. Blue-Eyes line: Turn 1 prioritizes Spirit Dragon (GY negate + tag-out); Going 2nd vs backrow/board prioritizes Tyrant / Jet!
            var blueEyesBoss = (isBreakerTurn && (rivalBackrow >= 2 || rivalMonsters >= 2))
                ? (ExtraSpecial(BlueEyesTyrant) ?? ExtraSpecial(BlueEyesSpiritDragon))
                : (ExtraSpecial(BlueEyesSpiritDragon) ?? ExtraSpecial(BlueEyesTyrant));
            var blueEyesMove =
                Effect(MaidenOfWhite, YgoConstants.LOCATION_HAND)
                ?? Effect(PrayersWithEyesOfBlue, YgoConstants.LOCATION_HAND)
                ?? Effect(MajestyRoar, YgoConstants.LOCATION_HAND)
                ?? blueEyesBoss
                ?? ExtraSpecial(SpiritWithEyesOfBlue)
                ?? Effect(UltimateFusion, YgoConstants.LOCATION_HAND)
                ?? Effect(DragonDictator, YgoConstants.LOCATION_HAND)
                ?? Effect(DragonDictator, YgoConstants.LOCATION_MZONE)
                ?? Normal(Kaibaman)
                ?? Normal(SageWithEyesOfBlue)
                ?? Effect(BlueEyesJetDragon, YgoConstants.LOCATION_HAND)
                ?? Effect(BlueEyesJetDragon, YgoConstants.LOCATION_GRAVE);
            if (blueEyesMove.HasValue) return blueEyesMove;

            // 4. Stardust / Junk Synchron line: Turn 1 builds Cosmic Blazer / Chronicle / Spark protection; Going 2nd leads with Baronne pop!
            var stardustBoss = isBreakerTurn
                ? (ExtraSpecial(Baronne) ?? ExtraSpecial(CosmicBlazer) ?? ExtraSpecial(StardustChronicle))
                : (ExtraSpecial(CosmicBlazer) ?? ExtraSpecial(StardustChronicle) ?? ExtraSpecial(Baronne));
            var stardustMove =
                Effect(Tuning, YgoConstants.LOCATION_HAND)
                ?? Effect(SynchroOvertake, YgoConstants.LOCATION_HAND)
                ?? Effect(JunkConverter, YgoConstants.LOCATION_HAND)
                ?? Effect(StardustIlluminate, YgoConstants.LOCATION_HAND)
                ?? stardustBoss
                ?? ExtraSpecial(JunkSpeeder)
                ?? ExtraSpecial(AccelSynchroStardust)
                ?? ExtraSpecial(CrimsonDragon)
                ?? ExtraSpecial(StardustSpark)
                ?? Normal(JunkSynchron);
            if (stardustMove.HasValue) return stardustMove;

            bool levelEight = own.mzone.Any(card => card?.IsFaceUp == true && card.level == 8 && card.code != GalaxyWizard);
            var galaxyMove = Effect(GalaxyHundred, YgoConstants.LOCATION_HAND) ?? ExtraSpecial(NeoPrimePhoton) ??
                ExtraSpecial(PrimePhoton) ?? Effect(GalaxyExpedition, YgoConstants.LOCATION_HAND) ??
                (levelEight ? Effect(GalaxyWizard, YgoConstants.LOCATION_MZONE, 0) : null) ??
                Effect(GalaxyZero, YgoConstants.LOCATION_HAND) ??
                (own.grave.Any(card => card.code is PhotonJumper or PhotonEmperor or PhotonDragon) ? Normal(GalaxySummoner) : null) ??
                Effect(GalaxyWizard, YgoConstants.LOCATION_MZONE, 1);
            if (galaxyMove.HasValue) return galaxyMove;

            var ryzeal = Effect(DuoDrive, YgoConstants.LOCATION_MZONE, 1) ?? ExtraSpecial(Deadnader) ?? ExtraSpecial(DuoDrive);
            if (ryzeal.HasValue) return ryzeal;

            // 5. Red Dragon Archfiend / Resonator line:
            //    Turn 1 prioritizes Abyss (omni-negate); Going 2nd against multiple monsters prioritizes Scarlight/Hot Red board wipe or Supernova!
            var rdaBoss = (isBreakerTurn && rivalMonsters >= 2)
                ? (ExtraSpecial(RedSupernova) ?? ExtraSpecial(ScarlightOrHotRed) ?? ExtraSpecial(RedDragonAbyss) ?? ExtraSpecial(RedDragonBane))
                : (ExtraSpecial(RedSupernova) ?? ExtraSpecial(RedDragonAbyss) ?? ExtraSpecial(RedDragonBane) ?? ExtraSpecial(ScarlightOrHotRed));
            var rdaMove =
                Normal(SoulResonator)
                ?? Effect(BoneArchfiend, YgoConstants.LOCATION_HAND)
                ?? Effect(BoneArchfiend, YgoConstants.LOCATION_GRAVE)
                ?? Effect(BoneArchfiend, YgoConstants.LOCATION_MZONE)
                ?? rdaBoss
                ?? (!prompt.canBp ? ExtraSpecial(ScarredDragonArchfiend) : null);
            if (rdaMove.HasValue) return rdaMove;

            // 6. Manjoume Light and Darkness Dragonlord & Playmaker Cyberse Link Climb
            //    Turn 1 Cyberse prioritizes Heatsoul / Darkfluid / Singularity; Going 2nd prioritizes Accesscode Talker / Neo Tempest OTK!
            var cyberseBoss = isFirstTurnSetup
                ? (ExtraSpecial(FirewallSingularity) ?? ExtraSpecial(FirewallDarkfluid) ?? ExtraSpecial(DecodeTalkerHeatsoul) ?? ExtraSpecial(FirewallDragon))
                : (ExtraSpecial(AccesscodeTalker) ?? ExtraSpecial(FirewallNeoTempest) ?? ExtraSpecial(FirewallSingularity) ?? ExtraSpecial(FirewallDarkfluid));
            var laddOrCyberseMove =
                Effect(DragonsMirror, YgoConstants.LOCATION_HAND)
                ?? Effect(LightEndSublimation, YgoConstants.LOCATION_HAND)
                ?? Effect(DarkEndEvaporation, YgoConstants.LOCATION_HAND)
                ?? Effect(LightEndSublimation, YgoConstants.LOCATION_MZONE)
                ?? Effect(DarkEndEvaporation, YgoConstants.LOCATION_MZONE)
                ?? ExtraSpecial(LaddLord)
                ?? Effect(CynetMining, YgoConstants.LOCATION_HAND)
                ?? cyberseBoss
                ?? ExtraSpecial(TranscodeTalker)
                ?? ExtraSpecial(DecodeTalkerHeatsoul)
                ?? Effect(TranscodeTalker, YgoConstants.LOCATION_MZONE)
                ?? ExtraSpecial(SplashMage)
                ?? Effect(SplashMage, YgoConstants.LOCATION_MZONE);
            if (laddOrCyberseMove.HasValue) return laddOrCyberseMove;

            return null;
        }

        // Ordered search priority when adding a card from Deck to Hand (hint == 506)
        private static readonly uint[] SearchPriority =
        {
            // HERO
            Faris, Stratos, Vyon, ShadowMist, FusionDestiny, MiracleFusion, MaskChange, Polymerization,
            // Dark Magician
            IllusionOfChaos, MagiciansSouls, MagiciansRod, DarkMagicalCircle,
            // Blue-Eyes
            MaidenOfWhite, MajestyRoar, UltimateFusion, Kaibaman, PrayersWithEyesOfBlue, DragonDictator,
            SageWithEyesOfBlue, BlueEyesJetDragon,
            // Gaia
            MagicalKnightGaia, CursedDragon, SpiralFusion, GallopingGaia,
            // Stardust / Synchron
            JunkConverter, JunkSynchron, Doppelwarrior, StardustIlluminate,
            // Red Dragon Archfiend
            SoulResonator, BoneArchfiend,
            // Galaxy / Photon
            GalaxyHundred, GalaxySummoner, GalaxyExpedition, GalaxyZero,
            // Manjoume LADD
            LightEndSublimation, DarkEndEvaporation, DragonsMirror,
        };

        internal static int? ChooseCard(DuelSession session, DuelPrompt prompt)
        {
            const ulong placeInSpellTrapZone = 527;
            if (prompt.SelectionHint == placeInSpellTrapZone && prompt.min == 1 && prompt.max == 1 &&
                session.SelectionChainLink is { code: Faris } faris && faris.triggerPlayer == prompt.playerId &&
                prompt.candidates.All(card => card.controller == prompt.playerId && card.location == YgoConstants.LOCATION_DECK) &&
                prompt.candidates.Any(card => card.code == Vyon) &&
                !session.Players[prompt.playerId].szone.Any(card => card?.code == Increase && card.IsFaceUp))
            {
                int increase = prompt.candidates.FindIndex(card => card.code == Increase);
                if (increase >= 0) return increase;
            }

            if (prompt.SelectionHint == DuelTactics.SendToGrave && prompt.min == 1 && prompt.max == 1 &&
                session.SelectionChainLink is { code: GalaxyHundred } &&
                prompt.candidates.All(card => card.controller == prompt.playerId && card.location == YgoConstants.LOCATION_DECK))
            {
                var field = session.Players[prompt.playerId];
                if (field.mzone.Concat(field.grave).Any(card => card?.code is PhotonJumper or PhotonDragon or GalaxySummoner))
                {
                    int emperor = prompt.candidates.FindIndex(card => card.code == PhotonEmperor);
                    if (emperor >= 0) return emperor;
                }
                int jumper = prompt.candidates.FindIndex(card => card.code == PhotonJumper);
                if (jumper >= 0) return jumper;
            }
            if (prompt.SelectionHint == 509 && prompt.min == 1 && prompt.max == 1 &&
                prompt.candidates.All(card => card.controller == prompt.playerId && card.location == YgoConstants.LOCATION_EXTRA))
            {
                bool attackTurn = session.TurnNumber > 1 && session.CurrentPhase == YgoConstants.PHASE_MAIN1;
                uint[] finishers = attackTurn
                    ? new[] { GaiaFusion, BlueEyesTyrant, WakeUpHero, Sunrise, Dragoon, DestroyerPhoenix }
                    : new[] { Dragoon, DestroyerPhoenix, DarkLaw, BlueEyesSpiritDragon, GaiaFusion };
                foreach (uint code in finishers)
                {
                    int chosen = prompt.candidates.FindIndex(card => card.code == code);
                    if (chosen >= 0) return chosen;
                }
            }
            if (prompt.SelectionHint != DuelTactics.AddToHand || prompt.candidates.Count == 0)
                return null;
            var own = session.Players[prompt.playerId];
            bool canRecoverFromGrave = session.SelectionChainLink is { code: CrimsonGaia } search &&
                search.triggerPlayer == prompt.playerId;
            if (prompt.candidates.Any(c => c.controller != prompt.playerId ||
                (c.location != YgoConstants.LOCATION_DECK && (!canRecoverFromGrave || c.location != YgoConstants.LOCATION_GRAVE))))
                return null;
            bool canUseMaiden = !prompt.candidates.Any(card => card.code == MaidenOfWhite) ||
                own.hand.Concat(own.grave).Concat(own.szone).Any(card => card?.code == TrueLight) ||
                session.HasCardInDeck(prompt.playerId, card => card.code == TrueLight);

            // First pick a priority combo starter not already in hand
            foreach (uint code in SearchPriority)
            {
                if ((code == MaidenOfWhite && !canUseMaiden) || own.hand.Any(h => h.code == code)) continue;
                int idx = prompt.candidates.FindIndex(c => c.code == code);
                if (idx >= 0) return idx;
            }
            // Otherwise pick any priority starter available
            foreach (uint code in SearchPriority)
            {
                if (code == MaidenOfWhite && !canUseMaiden) continue;
                int idx = prompt.candidates.FindIndex(c => c.code == code);
                if (idx >= 0) return idx;
            }
            return null;
        }
    }
}
