using System;
using System.Collections.Generic;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal static class DuelTactics
    {
        internal const ulong Release = 500, Discard = 501, Destroy = 502, Banish = 503, SendToGrave = 504,
            ReturnToHand = 505, AddToHand = 506, ReturnToDeck = 507, Negate = 575, ApplyTo = 577, ReturnToGrave = 579;
        private const uint DarkHole = 53129443, TorrentialTribute = 53582587, HotRed = 39765958, BlackRose = 73580471;
        private const uint TornadoDragon = 6983839, Perlereino = 77103950, MysticalSpaceTyphoon = 5318639;
        private const uint TributeToTheDoomed = 79759861, Conquistador = 20590515, SkyIris = 27813661;
        private const uint Baronne = 84815190, CipherBlade = 2530830, DiamondBell = 73667937, Hornet = 69207766;
        private const uint Farewelcome = 32785578, DarkTimeWizard = 40235813;
        private const uint PendulumScale = 17885118;
        private const uint SalamangreatOfFire = 11962031, Achichi = 15808381, CynetRefresh = 36368606;
        private const uint EarthSlicer = 97584719, Linkslayer = 35595518, Artorigus = 21223277;
        private const uint DarklordRebellion = 50501121, NoblemanOfCrossout = 71044499, HexeTrude = 46294982;
        private const uint GiftCard = 39526584, DarkCure = 3701074, NurseReficule = 67316075, BadReaction = 40633297;
        private const uint DestroyerPhoenix = 60461804, Deadnader = 34909328;
        private const uint CyberEternityDragon = 82315403, AttachmentCybern = 79875526;
        private const uint PhotonHowling = 28331069, GalaxyPhotonDragon = 85747929, GalaxyZero = 97617181;
        private const uint GalaxyEyesPhoton = 93717133, GalaxyWarDrake = 19316241, GalaxySummoner = 863795, GalaxyHundred = 897409;
        private const uint UtopiaLightning = 56832966, UtopiaRayVictory = 87911394;
        private const uint GogogoGoblindbergh = 35886170, DodododoWarrior = 62880279, AscendedSage = 4647954, Onomatokage = 55088578;
        private const ushort UtopiaSet = 0x7f;
        private const uint GaiaTheMagicalKnightOfDragons = 15989522, Sunrise = 22908820, RedRain = 5376159;
        internal const uint LightningStorm = 14532163, CrossoutDesignator = 65681983;
        private const uint WingedKuriboh = 57116033, WingedKuribohLevel6 = 48486809, CyberdarkChimera = 5370235;
        private const ulong WingedKuribohInterrupt = ((ulong)WingedKuribohLevel6 << 20) | 1;
        private const uint InfiniteImpermanence = 10045474, EffectVeiler = 97268402, BlueEyesTyrant = 11443677;
        private const uint LittleKnight = 29301450, LittleKnightAlternateArt = 29301451, DoomcaliberKnight = 78700060;
        private const uint TripleTacticsTalent = 25311006, ForbiddenCrown = 98829635;
        private const uint RedDragonTyrant = 16172067;
        private const uint RampageDragon = 84058253, CurseOfDragon = 7241272, SpiralDischarge = 29477860;
        private const uint Utopia = 84013237, DoubleOrNothing = 94770493;
        private const uint Stratos = 40044918, RedDragonArchfiend = 70902743, RedZone = 50056656, CrimsonGaia = 98173209;
        private const ulong ApplyContinuousTrapEffect = 94;
        private const uint ForbiddenDroplet = 24299458, RisingSunUtopia = 76504386;
        private const uint DdGryphon = 28406301;
        private const uint GalaxyCyclone = 5133471, KaliYuga = 15939229, WhipViper = 79967395;
        private const uint Erebus = 23064604;
        private const ushort HeroSet = 0x8;
        private const uint Development = (1u << 5) | (1u << 6) | (1u << 7) | (1u << 18);

        // Cards that actively want to be in the Graveyard (for discard / foolish burial / Graceful Charity)
        private static readonly HashSet<uint> GraveyardBeneficiaries = new()
        {
            9411399,  // 데스티니 히어로 디아볼릭 가이
            16605586, // 데스티니 히어로 디나이얼 가이
            50720316, // 엘리멘틀 히어로 섀도우 미스트
            30576089, // 푸른 눈의 제트 드래곤
            89631139, // 푸른 눈의 백룡
            46986414, // 블랙 매지션
            25784595, // 본 데몬
            58775978, // 볼트 고슴도치
            52277807, // 샐러맨그레이트 스피니
            56003780, // 샐러맨그레이트 재규어
            94620082, // 샐러맨그레이트 폭시
            45206713, // DD 스월 슬라임
            72291412, // DD 네크로 슬라임
            572850,   // 티아라멘츠 셰이렌
            74078255, // 티아라멘츠 메이루
            37961969, // 티아라멘츠 허프니스
            36426778, // 팬텀 나이츠 사일런트부츠
            63503850, // 팬텀 나이츠 더스티로브
            80538728, // 팬텀 나이츠 래기드글로브
        };

        // High-value handtraps and power spells that should not be discarded just because their ATK is 0
        private static readonly HashSet<uint> ProtectInHand = new()
        {
            14558127, // 하루 우라라
            23434538, // 증식의 G
            42141493, // 마루챠미 후와로스
            InfiniteImpermanence,
            EffectVeiler,
            24299458, // 금지된 일적
            25311006, // 삼전의 재
            35269904, // 삼전의 호
            24224830, // 무덤의 지명자
            CrossoutDesignator,
            55144522, // 욕망의 항아리
            79571449, // 천사의 자비
        };

        // Handtraps / omni-negates that must never chain to the AI's own effects
        private static readonly HashSet<uint> ResponseNegates = new()
        {
            14558127, // 하루 우라라
            EffectVeiler,
            InfiniteImpermanence,
            24224830, // 무덤의 지명자
            Baronne,  // 플뢰르 드 바로네스
            37818794, // 초마도용기사-붉은 눈의 드라군
            21123811, // 코즈믹 블레이저 드래곤
            89474727, // 진섬광룡 스타더스트 크로니클
            9753964,  // 염마룡 레드 데몬 어비스
            4280258,  // 소명의 신궁-아폴로우사
        };

        internal static bool CanActivate(DuelSession duel, DuelPrompt prompt, SelectableCardItem effect)
        {
            var last = duel.CurrentChain.LastOrDefault();
            if (effect.code == WingedKuribohLevel6 && ChooseWingedKuribohTiming(duel, prompt, effect) is bool useWinged)
                return useWinged;
            if (effect.code == CrossoutDesignator) return FindCrossoutTarget(duel, prompt.playerId).HasValue;
            if (last != null && ResponseNegates.Contains(effect.code) && (last.negated || last.triggerPlayer == prompt.playerId))
                return false;
            if ((effect.code is 23434538 or 42141493 or 84192580 or 87126721) && !effect.isTriggeredEffect &&
                (duel.TurnPlayer == prompt.playerId || duel.CurrentPhase == YgoConstants.PHASE_END) &&
                (last == null || last.triggerPlayer == prompt.playerId || last.negated ||
                    ((CardDatabase.Get(last.code)?.DeckEffectCategories ?? 0) & Development) == 0))
                return false;

            var own = duel.Players[prompt.playerId];
            var rival = duel.Players[1 - prompt.playerId];
            if (effect.code == InfiniteImpermanence &&
                rival.mzone.Any(card => card?.IsFaceUp == true && !card.effectsDisabled && card.code == BlueEyesTyrant) &&
                !rival.mzone.Any(card => card?.IsFaceUp == true && !card.effectsDisabled && card.code != BlueEyesTyrant) &&
                (effect.location != YgoConstants.LOCATION_SZONE || effect.sequence >= 5 || rival.szone[4 - effect.sequence] == null))
                return false;
            if (effect.code == CrimsonGaia && effect.location == YgoConstants.LOCATION_HAND &&
                own.szone.Any(card => card?.code == CrimsonGaia && card.IsFaceUp && !card.effectsDisabled))
                return false;
            if (effect.code == ForbiddenCrown && last == null && duel.TurnPlayer == prompt.playerId)
                return rival.mzone.Any(card => card?.IsFaceUp == true);
            if (effect.code is GiftCard or DarkCure)
                return own.mzone.Concat(own.szone).Any(card => card?.IsFaceUp == true && !card.effectsDisabled &&
                    card.code is NurseReficule or BadReaction);
            if (effect.code is SalamangreatOfFire or Achichi && effect.location == YgoConstants.LOCATION_GRAVE)
                return LosesCurrentBattle(duel, prompt.playerId) && (duel.AttackerCon == prompt.playerId ||
                    !own.mzone[duel.TargetSeq].IsDefense || rival.mzone[duel.AttackerSeq].code == RedDragonArchfiend);
            if (effect.code is TornadoDragon or MysticalSpaceTyphoon)
                return HasOpposingSpellOrTrap(rival);
            if (effect.code == TributeToTheDoomed)
                return rival.mzone.Any(card => card != null);
            if (effect.code == NoblemanOfCrossout)
                return rival.mzone.Any(card => card != null && !card.IsFaceUp);
            if (effect.code == DarklordRebellion)
                return HasFieldCard(rival);
            if (effect.code == PendulumScale && MustSpendOnlyEqualScales(own, rival, effect))
                return false;
            if (effect.code == GalaxyCyclone)
                return rival.szone.Any(card => card != null && card.IsFaceUp == (effect.location == YgoConstants.LOCATION_GRAVE));
            if (effect.code == WhipViper)
                return duel.TurnNumber > 1 && duel.CurrentPhase == YgoConstants.PHASE_MAIN1 &&
                    own.mzone.Concat(rival.mzone).Any(card => ViperBattleGain(duel, prompt.playerId, card) > 0);
            if (effect.code == SignatureTurnPlan.ScarredDragonArchfiend && effect.location == YgoConstants.LOCATION_GRAVE &&
                duel.TurnPlayer == prompt.playerId && (duel.TurnNumber == 1 ||
                    duel.CurrentPhase is YgoConstants.PHASE_MAIN2 or YgoConstants.PHASE_END) &&
                own.mzone.Any(card => card?.IsFaceUp == true && !card.effectsDisabled &&
                    CardDatabase.Get(card.code) is { IsExtra: true } boss && LinkComboPolicy.IsInteractive(boss)))
                return false;
            bool cardEffect = (effect.description >> 20) == effect.code;
            ulong effectIndex = effect.description & 0xfffff;
            bool firstEffect = cardEffect && effectIndex == 0;
            bool summonTrigger = effect.isTriggeredEffect || prompt.IsTriggerActivationConfirmation;
            // Let Doomcaliber wait for an opposing monster effect instead of forcing it to negate our S:P.
            if (cardEffect && effectIndex == 1 && effect.code is LittleKnight or LittleKnightAlternateArt &&
                effect.location == YgoConstants.LOCATION_MZONE && prompt.messageType == YgoConstants.MSG_SELECT_CHAIN &&
                !prompt.forced && last is { negated: false } && last.triggerPlayer != prompt.playerId &&
                CardDatabase.Get(last.code) is { } source && (source.IsSpell || source.IsTrap) &&
                own.mzone.Any(card => card is { code: DoomcaliberKnight, IsFaceUp: true, effectsDisabled: false }))
                return false;
            if (cardEffect && effectIndex == 1 && effect.code is Linkslayer or Artorigus)
                return HasOpposingSpellOrTrap(rival);
            if (firstEffect && effect.code == DarkTimeWizard && rival.mzone.All(card => card == null))
                return duel.HasCardInDeck(prompt.playerId, IsDarkTimeWizardSearchTarget);
            if (firstEffect && effect.code == SkyIris)
                return own.mzone.Any(CanSpendForSkyIris) || own.szone.Any(CanSpendForSkyIris);
            if (effect.code == ForbiddenDroplet && last?.code == RisingSunUtopia && last.triggerPlayer != prompt.playerId &&
                last.description == ((ulong)RisingSunUtopia << 20) && !duel.HasActiveAttack && duel.TurnPlayer == prompt.playerId &&
                StrongestAttacker(own) < StrongestAttacker(rival) / 2)
                return false;
            if (firstEffect && effect.code == KaliYuga)
            {
                int enemySpells = rival.szone.Count(card => card != null);
                return enemySpells > 0 && enemySpells >= own.szone.Count(card => card != null);
            }
            // Rampage's optional summon trigger has no effect description. Its milling effect is an idle action.
            if (effect.code == RampageDragon && (summonTrigger ||
                prompt.messageType is YgoConstants.MSG_SELECT_CHAIN or YgoConstants.MSG_SELECT_EFFECTYN))
                return rival.szone.Any(card => card != null);
            if (effect.code == CurseOfDragon && (firstEffect || summonTrigger))
                return rival.szone[YgoConstants.FIELD_ZONE_SEQUENCE] != null;
            if (effect.code == Stratos && (firstEffect || summonTrigger) && rival.szone.All(card => card == null))
                return duel.HasCardInDeck(prompt.playerId, card => card.IsMonster && CardDatabase.ExtractSetcodes(card.setcode).Contains(HeroSet));
            if (firstEffect && effect.code == DdGryphon && effect.location is YgoConstants.LOCATION_PZONE or YgoConstants.LOCATION_SZONE)
                return prompt.canBp && own.mzone.Any(card => GryphonBattlePayoff(own, rival, card) > 0);
            if (effect.code == Utopia && firstEffect && duel.HasActiveAttack)
                return CanSpendUtopiaMaterial(duel, prompt.playerId);
            if ((firstEffect && effect.code == GaiaTheMagicalKnightOfDragons) ||
                (cardEffect && effectIndex == 1 && effect.code is Sunrise or SpiralDischarge))
                return rival.mzone.Any(card => card != null) || rival.szone.Any(card => card != null);
            if (effect.code == RedRain && effect.location == YgoConstants.LOCATION_SZONE)
                return CanUseRedRain(own, rival);
            // Farewelcome can stop an enemy attack, but stopping our own with only allied targets loses both the attack and a card.
            if (firstEffect && effect.code == Farewelcome && duel.HasActiveAttack && duel.AttackerCon == prompt.playerId)
                return rival.mzone.Any(card => card != null) || rival.szone.Any(card => card != null);
            // Turning Red Zone face-up can immediately offer its destruction effect, revival, or both.
            if (effect.code == RedZone && prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN &&
                prompt.promptDesc == ApplyContinuousTrapEffect && !HasFieldCard(rival))
                return own.banished.Any(IsRedZoneRevivalTarget);
            if (cardEffect && effectIndex == 2 && effect.code == PhotonHowling &&
                CanKeepPhotonHowlingMaterials(duel, prompt, effect, own, rival))
                return false;
            if (firstEffect && effect.code == DestroyerPhoenix)
                return CanUsePhoenixDestruction(duel, prompt, own, rival);
            if ((firstEffect && effect.code is Deadnader or RedZone or Baronne) ||
                (cardEffect && effectIndex == 1 && effect.code is CipherBlade or DiamondBell or Hornet or EarthSlicer or HexeTrude))
                return HasFieldCard(rival);
            if (effect.code == Perlereino && (effect.description >> 20) == Perlereino && (effect.description & 0xfffff) == 1)
                return rival.mzone.Any(card => card != null) || rival.szone.Any(card => card != null);
            bool tyrantWipe = firstEffect && effect.code == RedDragonTyrant;
            bool fieldClear = tyrantWipe || effect.code == BlackRose &&
                (firstEffect || prompt.IsTriggerActivationConfirmation || effect.isTriggeredEffect);
            bool mainZones = effect.code == CynetRefresh && effect.location != YgoConstants.LOCATION_GRAVE;
            if (!fieldClear && !mainZones && effect.code is not (DarkHole or TorrentialTribute or HotRed)) return true;

            double ownLoss = FieldLoss(own);
            double opposingLoss = FieldLoss(rival);
            return opposingLoss > 0 && ownLoss <= opposingLoss + .05;

            double FieldLoss(PlayerFieldSnapshot side)
            {
                double loss = 0;
                foreach (var card in side.mzone)
                {
                    if (card == null || (mainZones && card.sequence >= 5) ||
                        ((tyrantWipe || effect.code == HotRed) && card.controller == effect.controller && card.sequence == effect.sequence) ||
                        (effect.code == HotRed && (!card.IsFaceUp || !card.IsAttack))) continue;
                    loss += BoardAssetValue(card);
                }
                if (fieldClear)
                    foreach (var card in side.szone)
                        if (card != null) loss += .85;
                return loss;
            }
        }

        private static bool CanKeepPhotonHowlingMaterials(DuelSession duel, DuelPrompt prompt,
            SelectableCardItem effect, PlayerFieldSnapshot own, PlayerFieldSnapshot rival)
        {
            if (duel.TurnPlayer != prompt.playerId || duel.TurnNumber <= 1 || duel.CurrentChain.Count != 0 ||
                (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.CurrentPhase == YgoConstants.PHASE_MAIN1 && !prompt.canBp) ||
                rival.szone.Any(card => card is { IsFaceUp: true, effectsDisabled: false }))
                return false;

            if (effect.location != YgoConstants.LOCATION_MZONE || effect.sequence >= own.mzone.Length ||
                own.mzone[effect.sequence] is not { code: PhotonHowling, IsFaceUp: true, IsAttack: true } howling)
                return false;

            var tribute = own.mzone.FirstOrDefault(card => card is { code: GalaxyPhotonDragon, IsFaceUp: true });
            var revived = own.szone.FirstOrDefault(card => card is { code: GalaxyZero, IsFaceUp: true, effectsDisabled: false });
            if (tribute == null || revived == null ||
                own.mzone.Any(card => card != null && card != howling && card != tribute &&
                    card.code is not (GalaxyEyesPhoton or GalaxyWarDrake or GalaxySummoner)) ||
                own.szone.Any(card => card is { IsFaceUp: true } && card != revived && card.code != GalaxyHundred) ||
                (duel.HasActiveAttack && (duel.AttackerCon != prompt.playerId || duel.AttackerSeq != howling.sequence)))
                return false;

            // Keep the optional blanket negate available on boards with an unknown or reactive effect.
            foreach (var defender in rival.mzone)
            {
                if (defender == null) continue;
                if (!defender.IsFaceUp) return false;
                if (defender.code == UtopiaLightning)
                {
                    if (!defender.IsAttack || howling.attack <= defender.attack ||
                        !defender.effectsDisabled && CanPowerUpUtopiaLightning(defender))
                        return false;
                }
                else if (!defender.effectsDisabled && (defender.type & YgoConstants.TYPE_EFFECT) != 0 &&
                         defender.code is not (UtopiaRayVictory or GogogoGoblindbergh or DodododoWarrior or AscendedSage or Onomatokage))
                    return false;
            }

            if (!revived.EquipTarget.HasValue) duel.RefreshVisibleEquipTargets(prompt.playerId);
            return revived.EquipTarget == (tribute.controller, tribute.sequence);
        }

        private static bool CanPowerUpUtopiaLightning(LiveCardState defender) => defender.overlayCodes.Count >= 2 &&
            defender.overlayCodes.Any(code => CardDatabase.Get(code) is not { } material ||
                material.IsXyz && (material.setcode == 0 || CardDatabase.ExtractSetcodes(material.setcode).Any(set => (set & 0xfff) == UtopiaSet)));

        private static bool CanUsePhoenixDestruction(DuelSession duel, DuelPrompt prompt, PlayerFieldSnapshot own,
            PlayerFieldSnapshot rival)
        {
            if (!HasFieldCard(rival)) return false;

            var dragon = rival.mzone.FirstOrDefault(card => IsProtectedCyberEternity(card, rival));
            if (dragon == null || rival.mzone.Any(card => card != null && card != dragon) ||
                rival.szone.Any(card => card != null && card is not { code: AttachmentCybern, IsFaceUp: true, effectsDisabled: false }) ||
                duel.CurrentChain.Any(link => link.triggerPlayer != prompt.playerId && !link.negated))
                return true;

            // Restrict this comparison to confirmed, active copies of Cybern equipped to the protected Dragon.
            var equipment = rival.szone.Where(card => card != null).ToArray();
            if (equipment.Length == 0) return true;
            if (equipment.Any(card => !card.EquipTarget.HasValue))
                duel.RefreshVisibleEquipTargets(dragon.controller);
            if (equipment.Any(card => card.EquipTarget != (dragon.controller, dragon.sequence)))
                return true;

            if (duel.TurnPlayer == prompt.playerId)
                return !prompt.attackable.Any(card => card.sequence < own.mzone.Length &&
                    own.mzone[card.sequence] is { IsAttack: true } attacker &&
                    attacker.attack > (dragon.IsDefense ? dragon.defense : dragon.attack));

            if (!duel.HasActiveAttack || duel.AttackerCon != dragon.controller ||
                duel.AttackerLoc != YgoConstants.LOCATION_MZONE || duel.AttackerSeq != dragon.sequence)
                return true;

            // Phoenix destroys one opposing card, so other copies of Cybern keep their ATK bonus.
            int reducedAttack = Math.Max(0, dragon.attack - 600);
            if (duel.TargetLoc == 0) return dragon.attack >= own.lp && reducedAttack < own.lp;
            if (duel.TargetCon != prompt.playerId || duel.TargetSeq >= own.mzone.Length ||
                own.mzone[duel.TargetSeq] is not { IsFaceUp: true } defender) return true;
            if (defender.IsDefense) return dragon.attack > defender.defense && reducedAttack <= defender.defense;
            return dragon.attack >= defender.attack && reducedAttack < defender.attack ||
                dragon.attack - defender.attack >= own.lp && reducedAttack - defender.attack < own.lp;
        }

        private static bool IsProtectedCyberEternity(LiveCardState card, PlayerFieldSnapshot controller) =>
            card is { code: CyberEternityDragon, IsFaceUp: true, effectsDisabled: false } &&
            controller.grave.Any(c => (c.type & YgoConstants.TYPE_FUSION) != 0 && c.race == YgoConstants.RACE_MACHINE);

        private static bool HasFieldCard(PlayerFieldSnapshot side) =>
            side.mzone.Any(card => card != null) || side.szone.Any(card => card != null);

        internal static int FindWingedKuribohSearch(DuelSession duel, DuelPrompt prompt)
        {
            if (prompt.SelectionHint != AddToHand || prompt.min != 1 || prompt.max != 1 ||
                duel.SelectionChainLink is not { code: Stratos, triggerLoc: YgoConstants.LOCATION_MZONE } search ||
                search.triggerPlayer != prompt.playerId || search.description != (ulong)Stratos << 20)
                return -1;

            int target = prompt.candidates.FindIndex(card => card.code == WingedKuribohLevel6 &&
                card.controller == prompt.playerId && card.location == YgoConstants.LOCATION_DECK);
            if (target < 0) return -1;

            var own = duel.Players[prompt.playerId];
            var rival = duel.Players[1 - prompt.playerId];
            if (own.lp > StrongestAttacker(rival) ||
                own.hand.Concat(own.grave).Concat(own.mzone).Any(card => card?.code == WingedKuribohLevel6))
                return -1;

            var mainZones = own.mzone.Take(5);
            if (mainZones.All(card => card != null))
                return mainZones.Any(card => card.IsFaceUp && CanPayWingedKuribohCost(card)) ? target : -1;
            return own.hand.Concat(own.grave).Concat(own.mzone.Where(card => card?.IsFaceUp == true))
                .Any(CanPayWingedKuribohCost) ? target : -1;
        }

        private static bool CanPayWingedKuribohCost(LiveCardState card) => card != null &&
            (card.CurrentNameCode == WingedKuriboh || (card.type & YgoConstants.TYPE_FUSION) != 0 &&
                CardDatabase.Get(card.code) is CardInfo fusion &&
                CardDatabase.ExtractSetcodes(fusion.setcode).Any(set => (set & 0x3fff) == 0x3008));

        internal static bool? ChooseWingedKuribohTiming(DuelSession duel, DuelPrompt prompt, SelectableCardItem effect)
        {
            var own = duel.Players[prompt.playerId];
            // The attack response and the monster-effect response share this native description.
            if (effect.code != WingedKuribohLevel6 || effect.description != WingedKuribohInterrupt ||
                effect.controller != prompt.playerId || effect.location != YgoConstants.LOCATION_MZONE ||
                effect.sequence >= own.mzone.Length || own.mzone[effect.sequence] is not
                    { code: WingedKuribohLevel6, IsFaceUp: true, effectsDisabled: false })
                return null;

            int opponent = 1 - prompt.playerId;
            var rival = duel.Players[opponent];
            var last = duel.CurrentChain.LastOrDefault();
            if (last != null)
            {
                if (last.triggerPlayer != opponent || last.triggerLoc != YgoConstants.LOCATION_MZONE || last.negated ||
                    last.triggerSeq >= rival.mzone.Length || rival.mzone[last.triggerSeq] is not { IsFaceUp: true } source ||
                    source.code != last.code || (source.type & YgoConstants.TYPE_MONSTER) == 0)
                    return null;

                if (source.baseAttack >= rival.lp) return true;
                // Chimera still searches for Power Bond if it is destroyed after activation.
                if (source.code == CyberdarkChimera && last.description == (ulong)CyberdarkChimera << 20)
                    return false;
                return null;
            }

            if (!duel.HasActiveAttack || duel.AttackerCon != opponent || duel.AttackerLoc != YgoConstants.LOCATION_MZONE ||
                duel.AttackerSeq >= rival.mzone.Length || rival.mzone[duel.AttackerSeq] is not { IsFaceUp: true } attacker)
                return null;

            if (attacker.baseAttack >= rival.lp) return true;
            if (duel.TargetLoc == 0) return attacker.attack >= own.lp ? true : null;
            // The declared target must be visible in Attack Position to compare these battle stats.
            if (duel.TargetCon != prompt.playerId || duel.TargetLoc != YgoConstants.LOCATION_MZONE ||
                duel.TargetSeq >= own.mzone.Length || own.mzone[duel.TargetSeq] is not
                    { IsFaceUp: true, IsAttack: true, attack: >= 0 } defender || attacker.attack < 0)
                return null;

            int damage = attacker.attack - defender.attack;
            if (damage >= own.lp) return true;
            if (damage > 0 && defender.attack < 1800 && !rival.szone.Any(card => card is { IsFaceUp: false }) &&
                own.mzone.Any(card => card != null && card != defender && card.code != WingedKuribohLevel6))
                return false;
            return null;
        }

        internal static int ChooseHandtrapTarget(DuelSession duel, DuelPrompt prompt)
        {
            int count = duel.CurrentChain.Count;
            if (count == 0) return -1;
            var activation = duel.CurrentChain[count - 1];
            if (activation.triggerPlayer != prompt.playerId || activation.code is not (InfiniteImpermanence or EffectVeiler))
                return -1;

            if (count >= 2)
            {
                var preceding = duel.CurrentChain[count - 2];
                if (preceding.triggerPlayer != prompt.playerId && !preceding.negated && preceding.triggerLoc == YgoConstants.LOCATION_MZONE &&
                    (activation.code != InfiniteImpermanence || preceding.code != BlueEyesTyrant))
                {
                    int source = prompt.candidates.FindIndex(card => card.controller == preceding.triggerPlayer &&
                        card.location == preceding.triggerLoc && card.sequence == preceding.triggerSeq && card.code == preceding.code);
                    if (source >= 0) return source;
                }
            }

            if (activation.code != InfiniteImpermanence || !prompt.candidates.Any(card => card.code == BlueEyesTyrant)) return -1;
            var rival = duel.Players[1 - prompt.playerId];
            int best = -1;
            int bestPriority = int.MinValue;
            for (int i = 0; i < prompt.candidates.Count; i++)
            {
                var target = prompt.candidates[i];
                if (target.code == BlueEyesTyrant || target.controller == prompt.playerId || target.location != YgoConstants.LOCATION_MZONE ||
                    target.sequence >= rival.mzone.Length || rival.mzone[target.sequence]?.effectsDisabled != false) continue;
                int priority = SelectionPriority(duel, prompt.playerId, target, Negate);
                if (priority <= bestPriority) continue;
                best = i;
                bestPriority = priority;
            }
            return best;
        }

        private static bool IsRedZoneRevivalTarget(LiveCardState card) =>
            card.IsFaceUp && (card.type & YgoConstants.TYPE_SYNCHRO) != 0 &&
            card.attribute == YgoConstants.ATTRIBUTE_DARK && card.race == YgoConstants.RACE_DRAGON;

        private static bool MustSpendOnlyEqualScales(PlayerFieldSnapshot own, PlayerFieldSnapshot rival, SelectableCardItem effect)
        {
            var left = own.szone[6] ?? own.szone[0];
            var right = own.szone[7] ?? own.szone[4];
            if (left?.IsFaceUp != true || right?.IsFaceUp != true ||
                !YgoConstants.IsPendulumScale(left.type) || !YgoConstants.IsPendulumScale(right.type) ||
                left.lscale != right.rscale || HasOpposingSpellOrTrap(rival))
                return false;

            // At equal scales the native effect must destroy exactly two Spell/Trap cards, excluding itself.
            bool hasOtherTarget = own.szone.Any(card => card != null && card != left && card != right &&
                (effect.location != YgoConstants.LOCATION_SZONE || card.sequence != effect.sequence)) ||
                own.mzone.Any(IsFaceUpSpellTrapMonster);
            return !hasOtherTarget && !PendulumTurnPlan.CanRebuildEqualScales(own, rival, left, right);
        }

        private static bool IsFaceUpSpellTrapMonster(LiveCardState card) =>
            card?.IsFaceUp == true && (card.type & (YgoConstants.TYPE_SPELL | YgoConstants.TYPE_TRAP)) != 0;

        private static bool HasOpposingSpellOrTrap(PlayerFieldSnapshot opponent) =>
            opponent.szone.Any(card => card != null) || opponent.mzone.Any(IsFaceUpSpellTrapMonster);

        // The packed scripts' listed_names entries are what the safe search tests using ListsCode.
        private static bool IsDarkTimeWizardSearchTarget(CardInfo card) =>
            (card.alias != 0 ? card.alias : card.code) is 2606417 or 3129527 or 4881365 or 17242022 or 40275470 or
                65118318 or 76630812 or 76660178 or 77482666 or 101403017 or 101403036;

        private static bool CanSpendForSkyIris(LiveCardState card)
        {
            if (card?.IsFaceUp != true || card.code == SkyIris) return false;
            // Keep the established interaction when the only gain would be a card in hand.
            return card.effectsDisabled || CardDatabase.Get(card.code) is not { IsExtra: true } monster ||
                !LinkComboPolicy.IsInteractive(monster);
        }

        internal static int? ChooseResolutionYesNo(DuelSession duel, DuelPrompt prompt)
        {
            if (prompt.promptDesc != (((ulong)Conquistador << 20) | 2) ||
                duel.SelectionChainLink is not { code: Conquistador } source || source.triggerPlayer != prompt.playerId)
                return null;
            var opponent = duel.Players[1 - prompt.playerId];
            return opponent.mzone.Any(card => card?.IsFaceUp == true) ||
                opponent.szone.Any(card => card?.IsFaceUp == true) ? 1 : 0;
        }

        internal static uint? FindCrossoutTarget(DuelSession duel, byte player)
        {
            var last = duel.CurrentChain.LastOrDefault();
            if (last == null || last.negated || last.triggerPlayer == player ||
                CardDatabase.Get(last.code) is not CardInfo target) return null;
            uint name = CardDatabase.GetArchetypeCollectionKey(target);
            return duel.HasCardInDeck(player, card => CardDatabase.GetArchetypeCollectionKey(card) == name)
                ? name : null;
        }

        internal static bool ShouldLeadWithLightningStorm(DuelSession duel, DuelPrompt prompt)
        {
            if (!prompt.canBp) return false;
            var rival = duel.Players[1 - prompt.playerId];
            int attackers = 0;
            bool threat = false;
            foreach (var monster in rival.mzone)
            {
                if (monster?.IsFaceUp != true || !monster.IsAttack) continue;
                attackers++;
                threat |= monster.attack >= 2200 || !monster.effectsDisabled &&
                    CardDatabase.Get(monster.code) is CardInfo card && LinkComboPolicy.IsInteractive(card);
            }
            return attackers >= 2 && threat;
        }

        private static int ViperBattleGain(DuelSession duel, byte player, LiveCardState card)
        {
            if (card?.IsFaceUp != true || card.attack == card.defense) return 0;
            bool own = card.controller == player;
            int attackChange = Math.Max(0, card.defense) - Math.Max(0, card.attack);
            if (duel.TurnPlayer == player)
                return own ? (card.IsAttack ? attackChange : 0) : (card.IsAttack ? -attackChange : attackChange);
            if (!own) return card.IsAttack ? -attackChange : 0;
            return card.IsAttack ? attackChange : -attackChange;
        }

        private static bool CanSpendUtopiaMaterial(DuelSession duel, byte player)
        {
            if (duel.AttackerCon > 1 || duel.AttackerLoc != YgoConstants.LOCATION_MZONE ||
                duel.AttackerSeq >= duel.Players[duel.AttackerCon].mzone.Length) return false;
            var attacker = duel.Players[duel.AttackerCon].mzone[duel.AttackerSeq];
            if (attacker == null) return false;
            var own = duel.Players[player];
            if (duel.AttackerCon == player)
                return (duel.TargetLoc != 0 || attacker.attack < duel.Players[1 - player].lp) &&
                    own.hand.Any(card => card.code == DoubleOrNothing);
            if (duel.TargetLoc == 0)
                return attacker.attack >= Math.Min(2000, own.lp);
            return LosesCurrentBattle(duel, player);
        }

        private static int GryphonBattlePayoff(PlayerFieldSnapshot own, PlayerFieldSnapshot rival, LiveCardState target)
        {
            if (target?.IsFaceUp != true || !target.IsAttack || (target.race & YgoConstants.RACE_FIEND) == 0) return 0;
            int gain = 500 * own.grave.Concat(own.szone.Where(card => card?.IsFaceUp == true))
                .Where(card => card != null && DddTurnPlan.IsDarkContract(card.code)).Select(card => card.code).Distinct().Count();
            if (gain == 0) return 0;
            if (rival.mzone.All(card => card == null))
            {
                int damage = own.mzone.Where(card => card?.IsFaceUp == true && card.IsAttack).Sum(card => Math.Max(0, card.attack));
                return damage < rival.lp && damage + gain >= rival.lp ? 8000 + target.attack : 0;
            }
            int gainAgainstMonster = 0;
            foreach (var opponent in rival.mzone)
            {
                if (opponent?.IsFaceUp != true) continue;
                int threshold = opponent.IsDefense ? opponent.defense : opponent.attack;
                if (target.attack <= threshold && target.attack + gain > threshold)
                    gainAgainstMonster = Math.Max(gainAgainstMonster, threshold + 1);
            }
            return gainAgainstMonster;
        }

        private static bool CanUseRedRain(PlayerFieldSnapshot own, PlayerFieldSnapshot rival)
        {
            uint highestLevel = own.mzone.Concat(rival.mzone)
                .Where(card => card?.IsFaceUp == true).Select(card => card.level).DefaultIfEmpty(0u).Max();
            double Loss(PlayerFieldSnapshot side) => side.mzone.Where(card => card != null &&
                (!card.IsFaceUp || card.level < highestLevel)).Sum(BoardAssetValue);
            double opposingLoss = Loss(rival);
            return opposingLoss > 0 && Loss(own) <= opposingLoss + .05;
        }

        private static bool LosesCurrentBattle(DuelSession duel, byte player)
        {
            if (!duel.HasActiveAttack || duel.AttackerLoc != YgoConstants.LOCATION_MZONE ||
                duel.TargetLoc != YgoConstants.LOCATION_MZONE || duel.AttackerCon >= 2 || duel.TargetCon >= 2 ||
                duel.AttackerSeq >= duel.Players[duel.AttackerCon].mzone.Length || duel.TargetSeq >= duel.Players[duel.TargetCon].mzone.Length)
                return false;
            var attacker = duel.Players[duel.AttackerCon].mzone[duel.AttackerSeq];
            var target = duel.Players[duel.TargetCon].mzone[duel.TargetSeq];
            if (attacker?.IsFaceUp != true || target?.IsFaceUp != true) return false;
            return player == duel.AttackerCon ? !target.IsDefense && attacker.attack < target.attack :
                attacker.attack > (target.IsDefense ? target.defense : target.attack);
        }

        private static double BoardAssetValue(LiveCardState card)
        {
            if (!card.IsFaceUp) return .85;
            double score = .55 + Math.Max(card.attack, card.defense) / 2800.0;
            if (!card.effectsDisabled && CardDatabase.Get(card.code) is CardInfo info && LinkComboPolicy.IsInteractive(info))
                score += .7;
            return score;
        }

        internal static float ControlChainPriority(DuelSession duel, DuelPrompt prompt, SelectableCardItem effect)
        {
            if (DuelDeckStrategy.For(duel, prompt.playerId) == DuelDeckStyle.Development ||
                duel.TurnPlayer == prompt.playerId || CardDatabase.Get(effect.code) is not CardInfo card ||
                !LinkComboPolicy.IsInteractive(card)) return 0;
            var target = duel.CurrentChain.LastOrDefault();
            if (target == null || target.triggerPlayer == prompt.playerId || target.negated)
                return (prompt.hintTimingOther & (YgoConstants.TIMING_SPSUMMON | YgoConstants.TIMING_ATTACK)) == 0 ? -.65f : 0;
            uint effectTypes = CardDatabase.Get(target.code)?.DeckEffectCategories ?? 0;
            return (effectTypes & Development) != 0 ? 1.1f : -.2f;
        }

        internal static bool IsRemoval(ulong hint) => hint is Destroy or Banish or SendToGrave or ReturnToHand or
            ReturnToDeck or Negate or ReturnToGrave;

        internal static int? ChooseOption(DuelSession duel, DuelPrompt prompt)
        {
            if (prompt.options.Count < 2) return null;
            var rival = duel.Players[1 - prompt.playerId];
            int Option(uint code, int effect) => prompt.options.IndexOf(((ulong)code << 20) | (uint)effect);

            int searchHero = Option(Stratos, 2);
            if (searchHero >= 0 && rival.szone.All(card => card == null)) return searchHero;

            int reviveDragon = Option(RedZone, 1);
            if (reviveDragon >= 0 && !HasFieldCard(rival)) return reviveDragon;

            int returnFieldCard = Option(Erebus, 4);
            if (returnFieldCard >= 0 && (rival.mzone.Any(card => card != null) || rival.szone.Any(card => card != null)))
                return returnFieldCard;

            int revealAnkh = prompt.options.FindIndex(option =>
                (option & 0xfffffUL) == 3 && DuelDeckStrategy.IsMillenniumMonster((uint)(option >> 20)));
            if (revealAnkh >= 0) return revealAnkh;

            int clearMonsters = Option(LightningStorm, 0);
            int clearBackrow = Option(LightningStorm, 1);
            if (clearMonsters >= 0 && clearBackrow >= 0)
            {
                double monsters = rival.mzone.Where(card => card?.IsFaceUp == true && card.IsAttack).Sum(BoardAssetValue);
                double backrow = rival.szone.Where(card => card != null).Sum(card => card.IsFaceUp ? 1.35 : 1.0);
                return backrow > monsters ? clearBackrow : clearMonsters;
            }
            int takeControl = Option(TripleTacticsTalent, 2);
            if (takeControl >= 0 && duel.TurnNumber > 1 && duel.CurrentPhase == YgoConstants.PHASE_MAIN1 &&
                rival.mzone.Any(card => card?.IsFaceUp == true && card.attack >= 2500))
                return takeControl;
            int draw = Option(TripleTacticsTalent, 1);
            return draw >= 0 ? draw : null;
        }

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
            if (!own && hint == Destroy && duel.SelectionChainLink is { code: DestroyerPhoenix } phoenix &&
                phoenix.triggerPlayer == player && IsProtectedCyberEternity(live, side))
                return 0;
            if (live != null && duel.SelectionChainLink is { code: WhipViper } swap && swap.triggerPlayer == player)
                return ViperBattleGain(duel, player, live);
            // Conquistador uses the generic face-up selection hint for its optional destruction.
            if (live != null && duel.SelectionChainLink is { code: Conquistador } conquistador && conquistador.triggerPlayer == player)
                return own ? -5000 - value : 10000 + value;
            if (own && live != null && hint == Destroy && duel.SelectionChainLink is { code: SkyIris } iris &&
                iris.triggerPlayer == player && !CanSpendForSkyIris(live))
                return -10000 - value;
            if (hint == ApplyTo && duel.SelectionChainLink is { code: ForbiddenCrown, chainNumber: 1 } suppress &&
                suppress.triggerPlayer == player && duel.TurnPlayer == player)
                return own ? -5000 - value : 10000 + (live?.effectsDisabled == true ? value / 2 : value);
            if (own && live != null && duel.SelectionChainLink is { code: DdGryphon, triggerLoc: YgoConstants.LOCATION_SZONE or YgoConstants.LOCATION_PZONE })
                return 10000 + GryphonBattlePayoff(side, duel.Players[1 - player], live);

            if ((hint is Discard or SendToGrave) && own &&
                option.location is YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_DECK)
            {
                int copies = side.hand.Count(candidate => candidate.code == option.code);
                int score = -value + (copies > 1 ? 1400 : 0);
                if (GraveyardBeneficiaries.Contains(option.code)) score += 3500;
                if (DuelDeckStrategy.For(duel, player) == DuelDeckStyle.Lock && DuelDeckStrategy.IsKeyPiece(option.code))
                    score -= 9000;
                if (option.location == YgoConstants.LOCATION_HAND && ProtectInHand.Contains(option.code) && copies <= 1)
                    score -= 2500;
                if (card != null && card.IsMonster && !card.IsExtra && card.level >= 5)
                    score += 800;
                return score;
            }
            if (IsRemoval(hint) || hint is Release or 511 or 512 or 513 or 533)
                return own ? -5000 - value : 10000 + value;
            return own ? 10000 + value : -5000 + value;
        }

        internal static int? ChoosePosition(DuelSession duel, DuelPrompt prompt)
        {
            const uint both = YgoConstants.POS_FACEUP_ATTACK | YgoConstants.POS_FACEUP_DEFENSE;
            if ((prompt.allowedPositions & both) != both) return null;
            var card = CardDatabase.Get(prompt.promptCardCode);
            if (card == null) return null;

            var rival = duel.Players[1 - prompt.playerId];
            int rivalStrongest = StrongestAttacker(rival);
            bool canAttack = duel.TurnNumber > 1 && duel.TurnPlayer == prompt.playerId &&
                duel.CurrentPhase is YgoConstants.PHASE_MAIN1 or YgoConstants.PHASE_BATTLE;

            // Never Special Summon in Attack Position when the opponent already controls a stronger attacker!
            if (rivalStrongest > Math.Max(0, card.atk))
                return (int)YgoConstants.POS_FACEUP_DEFENSE;

            if (canAttack && CanWinBattle(rival, card.atk)) return (int)YgoConstants.POS_FACEUP_ATTACK;

            // On Turn 1, Main Phase 2, or opponent's turn, Special Summon sub-2000 ATK (or higher-DEF) monsters in Defense
            // so if a combo ends or gets interrupted, weak bodies are never left in Attack Position at End Phase!
            if (!canAttack && (card.atk < 2000 || card.def >= card.atk))
                return (int)YgoConstants.POS_FACEUP_DEFENSE;
            return null;
        }

        internal static int? ChooseDefensiveSet(DuelSession duel, DuelPrompt prompt, SelectableCardItem summon,
            bool followupUnproven = false, bool compareWithBattle = false, bool lethalOnly = false)
        {
            var card = CardDatabase.Get(summon.code);
            if (!followupUnproven && ComboSearch.CanDevelopFromCard(card)) return null;
            if (card == null || card.level > 4 || summon.location != YgoConstants.LOCATION_HAND) return null;

            var rival = duel.Players[1 - prompt.playerId];
            int rivalStrongest = StrongestAttacker(rival);
            var own = duel.Players[prompt.playerId];
            int exposedDamage = rivalStrongest - Math.Max(0, card.atk);
            if (exposedDamage <= 0) return null;
            bool hasFieldBodies = own.mzone.Any(m => m != null);
            // With a lone attacker, the other visible opponents can attack directly after it is destroyed.
            bool threatensLethal = exposedDamage >= own.lp ||
                (!hasFieldBodies && DuelCombat.VisibleAttackTotal(rival) - Math.Max(0, card.atk) >= own.lp);
            bool fragileAtLowLife = followupUnproven && DuelCombat.IsWeakBattleLiability(card.atk, rivalStrongest, own.lp);
            if (lethalOnly && !threatensLethal && !fragileAtLowLife) return null;
            if (!compareWithBattle && !threatensLethal && !fragileAtLowLife && prompt.canBp && CanWinBattle(rival, card.atk)) return null;

            bool isVanillaOrFlip = (card.type & (YgoConstants.TYPE_NORMAL | YgoConstants.TYPE_FLIP)) != 0;
            bool hasImmediateSpecial = prompt.spSummonable.Count > 0;

            // Existing defenders do not prevent a stronger attacker from choosing the new monster.
            bool unprovenRisk = fragileAtLowLife || followupUnproven && (threatensLethal || card.atk < 1800 && exposedDamage > 200);
            bool shouldSet = unprovenRisk || isVanillaOrFlip || (!hasFieldBodies && !hasImmediateSpecial && exposedDamage > 200);
            if (!shouldSet) return null;

            return FindSettableIndex(prompt, summon);
        }

        internal static int? FindSettableIndex(DuelPrompt prompt, SelectableCardItem summon)
        {
            int index = prompt.mSetable.FindIndex(item => item.controller == prompt.playerId &&
                item.location == summon.location && item.sequence == summon.sequence);
            return index >= 0 ? index : null;
        }

        internal static int? ChooseDefensiveReposition(DuelSession duel, DuelPrompt prompt, bool beforeBattle = false)
        {
            int threat = StrongestAttacker(duel.Players[1 - prompt.playerId]);
            if (threat <= 0) return null;
            var own = duel.Players[prompt.playerId];
            if (beforeBattle && StrongestAttacker(own) >= threat) return null;
            for (int i = 0; i < prompt.repositionable.Count; i++)
            {
                var option = prompt.repositionable[i];
                if (option.controller != prompt.playerId || option.sequence >= own.mzone.Length)
                    continue;
                var card = own.mzone[option.sequence];
                // Defense Position can protect LP even when the monster will still be destroyed.
                if (card?.IsFaceUp == true && card.IsAttack && (card.type & YgoConstants.TYPE_LINK) == 0 &&
                    card.attack < threat && (!beforeBattle || threat - Math.Max(0, card.attack) >= own.lp))
                    return i;
            }
            return null;
        }

        internal static bool RisksEndPhaseDestruction(DuelSession duel, DuelPrompt prompt) =>
            !prompt.canBp && duel.Players[prompt.playerId].mzone.Any(card => card?.IsFaceUp == true &&
                !card.effectsDisabled && card.code == RedDragonArchfiend);

        internal static bool NeedsTributeProjection(DuelSession duel, DuelPrompt prompt, SelectableCardItem summon)
        {
            if (summon.location != YgoConstants.LOCATION_HAND ||
                CardDatabase.Get(summon.code) is not { level: >= 5 } incoming)
                return false;

            var field = duel.Players[prompt.playerId].mzone;
            int disposable = 0;
            bool established = false;
            foreach (var monster in field)
            {
                if (monster == null) continue;
                bool developedMaterial = (monster.type & YgoConstants.TYPE_EXTRA) != 0 &&
                    BoardAssetValue(monster) > .55 + Math.Max(incoming.atk, incoming.def) / 2800.0;
                bool strongerMaterial = monster.attack >= 1800 &&
                    monster.attack >= Math.Max(0, incoming.atk) + 500 &&
                    monster.defense >= Math.Max(0, incoming.def) + 500;
                if (monster.IsFaceUp && (developedMaterial || strongerMaterial))
                    established = true;
                else
                    disposable++;
            }
            return established && disposable < (incoming.level >= 7 ? 2 : 1);
        }

        internal static bool CanSummonRedDragonSafely(DuelSession duel, DuelPrompt prompt, uint code)
        {
            if (code != RedDragonArchfiend ||
                !prompt.spSummonable.Any(card => card.code == SignatureTurnPlan.ScarredDragonArchfiend) ||
                duel.Players[prompt.playerId].mzone.Count(card => card != null) <= 2)
                return true;

            // Scarred uses the same kind of material and lets the remaining monsters stay on the field.
            return prompt.canBp && duel.Players[1 - prompt.playerId].mzone.Count(card => card?.IsDefense == true) >= 2;
        }

        internal static int? ChooseArchfiendEndPhaseUpgrade(DuelSession duel, DuelPrompt prompt)
        {
            if (!RisksEndPhaseDestruction(duel, prompt)) return null;
            var field = duel.Players[prompt.playerId].mzone;
            // With no other Synchro material, Abyss removes Archfiend's mandatory destruction trigger.
            if (!field.Any(card => card != null && !card.IsAttack) ||
                field.Count(card => card?.IsFaceUp == true && (card.type & YgoConstants.TYPE_SYNCHRO) != 0) != 1)
                return null;
            int index = prompt.spSummonable.FindIndex(card => card.code == SignatureTurnPlan.RedDragonAbyss &&
                card.controller == prompt.playerId && card.location == YgoConstants.LOCATION_EXTRA);
            return index < 0 ? null : index;
        }

        private static bool CanWinBattle(PlayerFieldSnapshot rival, int attack) => attack > 0 &&
            (rival.mzone.All(card => card == null) || rival.mzone.Any(card =>
                card?.IsFaceUp == true && attack > (card.IsDefense ? card.defense : card.attack)));

        internal static int StrongestAttacker(PlayerFieldSnapshot rival)
        {
            int attack = 0;
            foreach (var card in rival.mzone)
                if (card?.IsFaceUp == true && card.IsAttack) attack = Math.Max(attack, card.attack);
            return attack;
        }
    }
}
