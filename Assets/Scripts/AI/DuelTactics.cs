using System;
using System.Collections.Generic;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal static partial class DuelTactics
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
        private const uint SalamangreatSanctuary = 1295111;
        private const ulong SanctuaryBattleRecovery = ((ulong)SalamangreatSanctuary << 20) | 1;
        private const uint EarthSlicer = 97584719, Linkslayer = 35595518, Artorigus = 21223277;
        private const uint DarklordRebellion = 50501121, NoblemanOfCrossout = 71044499, HexeTrude = 46294982;
        private const uint GiftCard = 39526584, DarkCure = 3701074, NurseReficule = 67316075, BadReaction = 40633297;
        private const uint DestroyerPhoenix = 60461804, Deadnader = 34909328;
        internal const uint AncientGearAdvance = 4064925, NightmarePain = 65261141, CyberdarkInferno = 44352516;
        private const uint GraveSquirmer = 24215921, EternalSoul = 48680970;
        internal const uint VolcanicEmperor = 46412900, MiracleExcluder = 30875635;
        private const uint VanityEmptiness = 5851097, TourGuide = 10802915;
        internal const ulong ExoddBlazeEquipEffect = ((ulong)402416 << 20) | 2;
        internal const uint Accesscode = 86066372, Cartesia = 95515789, VisionIncrease = 22865492;
        private const uint KnightmareUnicorn = 38342335, ExoddFrame = 64043465, MagiciansRod = 7084129;
        private const uint CyberEternityDragon = 82315403, AttachmentCybern = 79875526;
        private const uint PhotonHowling = 28331069, GalaxyPhotonDragon = 85747929, GalaxyZero = 97617181;
        private const uint GalaxyEyesPhoton = 93717133, GalaxyWarDrake = 19316241, GalaxySummoner = 863795, GalaxyHundred = 897409;
        private const uint UtopiaLightning = 56832966, UtopiaRayVictory = 87911394;
        private const uint UtopicOnomatopoeia = 8512558;
        private const uint GogogoGoblindbergh = 35886170, DodododoWarrior = 62880279, AscendedSage = 4647954, Onomatokage = 55088578;
        private const ushort UtopiaSet = 0x7f;
        private const uint GaiaTheMagicalKnightOfDragons = 15989522, Sunrise = 22908820, RedRain = 5376159;
        internal const uint LightningStorm = 14532163, CrossoutDesignator = 65681983;
        private const uint WingedKuriboh = 57116033, WingedKuribohLevel6 = 48486809, CyberdarkChimera = 5370235;
        private const ulong WingedKuribohInterrupt = ((ulong)WingedKuribohLevel6 << 20) | 1;
        internal const uint InfiniteImpermanence = 10045474, EffectVeiler = 97268402, BlueEyesTyrant = 11443677;
        private const uint AshBlossom = 14558127, UpstartGoblin = 70368879, OriginalSinfulSpoils = 89023486;
        internal const uint MaxxC = 23434538, CalledBy = 24224830, ForbiddenDroplet = 24299458;
        internal const uint SolemnStrike = 40605147;
        internal const uint Predaprimitive = 89176044;
        private const uint Nibiru = 27204311, Apollousa = 4280258, Blazar = 21123811,
            Dragoon = 37818794, CyberInfinity = 10443957, Dragonlord = 19652159, Quasar = 35952884,
            ShootingMajestic = 40939228, Savage = 27548199, CrystalWing = 50954680, Rulkallos = 84330567;
        private const uint ShiEn = 29981921, NaturiaBeast = 33198837;
        private const uint BlueEyesUltimateSpirit = 89604813;
        private const uint Raigeki = 12580477, EvenlyMatched = 15693423, InterruptedKaijuSlumber = 99330325;
        private const uint Reinforcement = 32807846, FossilDig = 47325505, Tuning = 96363153,
            Tenki = 57103969, Terraforming = 73628505;
        private const uint SummoningEffects = (1u << 6) | (1u << 7) | (1u << 18);

        [Flags]
        internal enum ExtensionRisk { None = 0, Nibiru = 1, Sweeper = 2 }
        private const uint LittleKnight = 29301450, LittleKnightAlternateArt = 29301451, DoomcaliberKnight = 78700060;
        private const uint TripleTacticsTalent = 25311006, ForbiddenCrown = 98829635;
        private const uint RedDragonTyrant = 16172067;
        private const uint RampageDragon = 84058253, CurseOfDragon = 7241272, SpiralDischarge = 29477860;
        private const uint Utopia = 84013237, DoubleOrNothing = 94770493;
        private const uint Stratos = 40044918, RedDragonArchfiend = 70902743, RedZone = 50056656, CrimsonGaia = 98173209;
        private const ulong ApplyContinuousTrapEffect = 94;
        private const uint RisingSunUtopia = 76504386;
        private const uint DdGryphon = 28406301;
        private const uint GalaxyCyclone = 5133471, KaliYuga = 15939229, WhipViper = 79967395;
        private const uint Erebus = 23064604;
        private const uint MiracleFusion = 45906428, WakeUpElementalHero = 32828466;
        private const uint RunickHugin = 55990317, RunickGeri = 28373620;
        private const uint IllusionOfChaos = 12266229, DarkMagician = 46986414;
        private const uint Necrovalley = 47355498, ImperialIronWall = 30459350, ChaosHunter = 97940434, Kycoo = 88240808;
        private const ushort HeroSet = 0x8, ElementalHeroSet = 0x3008;
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
            SolemnStrike,
        };

        internal static bool IsFieldMonsterSuppression(SelectableCardItem effect) =>
            effect.location is YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_SZONE &&
            effect.code is ForbiddenDroplet or ForbiddenCrown or InfiniteImpermanence or DuelDeckStrategy.WidowAnchor;

        internal static int? FindGiftCardWin(DuelSession duel, DuelPrompt prompt, Func<int, SelectableCardItem, bool> canUse)
        {
            int rivalLife = duel.Players[1 - prompt.playerId].lp;
            if (rivalLife <= 0 || rivalLife > 3000 * prompt.activatable.Count) return null;
            var own = duel.Players[prompt.playerId];
            int firstGift = -1, damage = 0;
            for (int i = 0; i < prompt.activatable.Count; i++)
            {
                var option = prompt.activatable[i];
                if (option.controller != prompt.playerId || option.code != GiftCard ||
                    option.location != YgoConstants.LOCATION_SZONE) continue;
                if (firstGift < 0) firstGift = i;
                damage += 3000;
            }
            if (damage < rivalLife) return null;

            if (own.mzone.Any(card => IsActive(card, NurseReficule)) || own.szone.Any(card => IsActive(card, BadReaction)))
                return canUse(5, prompt.activatable[firstGift]) ? (firstGift << 16) | 5 : null;

            int nurse = prompt.summonable.FindIndex(card => card.controller == prompt.playerId &&
                card.code == NurseReficule && canUse(0, card));
            return nurse < 0 ? null : nurse << 16;
        }

        internal static bool CanActivate(DuelSession duel, DuelPrompt prompt, SelectableCardItem effect)
        {
            if (DddTurnPlan.AddsLethalUpkeep(duel, prompt, effect)) return false;
            if (HasLethalSpecialSummonBurn(duel, prompt.playerId) && SpecialSummonsOnResolution(effect)) return false;
            var last = duel.CurrentChain.LastOrDefault();
            if (effect.code == WingedKuribohLevel6 && ChooseWingedKuribohTiming(duel, prompt, effect) is bool useWinged)
                return useWinged;
            if (effect.code == CrossoutDesignator) return FindCrossoutTarget(duel, prompt.playerId).HasValue;
            if (last != null && !prompt.IsTriggerChain && !effect.isTriggeredEffect &&
                ResponseNegates.Contains(effect.code) && (last.negated || last.triggerPlayer == prompt.playerId))
                return false;
            if (effect.code == SolemnStrike && last == null &&
                !duel.HasPendingSpecialSummon((byte)(1 - prompt.playerId))) return false;
            if (effect.code == Predaprimitive && effect.location == YgoConstants.LOCATION_GRAVE)
                return false; // The optional GY trigger destroys one of our own Fusion monsters.
            if (effect.code == CalledBy)
                return OpposingGraveResponse(duel, prompt.playerId, last) != 0;
            if (effect.code is ForbiddenDroplet or AshBlossom or InfiniteImpermanence or EffectVeiler &&
                last is { code: TourGuide } && last.triggerPlayer != prompt.playerId &&
                duel.Players.Any(side => side.szone.Any(card => IsActive(card, VanityEmptiness))))
                return false;
            if (effect.code == MaxxC)
                return duel.Players[prompt.playerId].deckCount > 2 && IsOpponentSummoningEffect(last, prompt.playerId);
            if (effect.code == DuelAiPolicy.SuperPoly)
                return DuelAiPolicy.HasOpposingFusionMaterial(duel.Players[prompt.playerId], duel.Players[1 - prompt.playerId]);
            if (effect.code == DuelAiPolicy.PowerBond && !DuelAiPolicy.CanRiskPowerBond(duel, prompt))
                return false;
            if ((effect.code is 42141493 or 84192580 or 87126721) && !effect.isTriggeredEffect &&
                (duel.TurnPlayer == prompt.playerId || duel.CurrentPhase == YgoConstants.PHASE_END) &&
                (last == null || last.triggerPlayer == prompt.playerId || last.negated ||
                    ((CardDatabase.Get(last.code)?.DeckEffectCategories ?? 0) & Development) == 0))
                return false;

            var own = duel.Players[prompt.playerId];

            // This ignition has no activation cost and cannot extend while its source is negated.
            if (effect.code == UtopicOnomatopoeia && effect.controller == prompt.playerId &&
                prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && effect.location == YgoConstants.LOCATION_MZONE &&
                effect.sequence < own.mzone.Length &&
                own.mzone[effect.sequence] is { code: UtopicOnomatopoeia, IsFaceUp: true, effectsDisabled: true })
                return false;

            var rival = duel.Players[1 - prompt.playerId];
            if (DuelAiPolicy.IsMachineAngelTribute(effect.code) && !DuelAiPolicy.CanSpendForMachineAngel(own))
                return false;
            if (effect.code == DuelDeckStrategy.StrikerZero && effect.controller == prompt.playerId &&
                effect.description == (((ulong)DuelDeckStrategy.StrikerZero << 20) | 1) &&
                duel.TurnNumber == 1 && duel.TurnPlayer == prompt.playerId && last == null &&
                !HasFieldCard(rival) && DuelDeckStrategy.ShouldKeepMainMonsterZoneOpen(duel, prompt.playerId))
                return false;
            if (effect.code == DuelDeckStrategy.Afterburners && !rival.mzone.Any(card => card?.IsFaceUp == true))
                return own.grave.Count(card => (card.type & YgoConstants.TYPE_SPELL) != 0) >= 3 &&
                    rival.szone.Any(card => card != null) && own.mzone.Any(card => card?.IsFaceUp == true &&
                        (card.type & YgoConstants.TYPE_TOKEN) != 0);
            if ((effect.code is DuelDeckStrategy.WidowAnchor or 98338153) &&
                !rival.mzone.Any(card => card is { IsFaceUp: true, effectsDisabled: false } && (card.type & YgoConstants.TYPE_EFFECT) != 0))
                return false;
            if (effect.code == AshBlossom && last?.code == UpstartGoblin &&
                !rival.szone.Any(card => IsActive(card, BadReaction)) && !rival.mzone.Any(card => IsActive(card, NurseReficule)))
                return false;
            if (IsTemporaryFieldNegation(effect) && !CanSpendFieldNegation(duel, prompt, effect))
                return false;
            if (effect.code == CrimsonGaia && effect.location == YgoConstants.LOCATION_HAND &&
                own.szone.Any(card => card?.code == CrimsonGaia && card.IsFaceUp && !card.effectsDisabled))
                return false;
            if (effect.code == ForbiddenCrown && last == null && duel.TurnPlayer == prompt.playerId)
                return rival.mzone.Any(card => card?.IsFaceUp == true);
            if (effect.code is GiftCard or DarkCure)
                return own.mzone.Concat(own.szone).Any(card => card?.IsFaceUp == true && !card.effectsDisabled &&
                    card.code is NurseReficule or BadReaction);
            if (effect.code == SalamangreatSanctuary && effect.description == SanctuaryBattleRecovery)
                return own.mzone.Any(card => SanctuaryLifeGain(duel, prompt.playerId, card) > 0);
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
            if (effect.description == ExoddBlazeEquipEffect && effect.location == YgoConstants.LOCATION_MZONE)
            {
                int opposingTraps = rival.szone.Take(5).Count(card => card != null);
                int ownTraps = own.szone.Take(5).Count(card => card != null);
                return (prompt.canBp && DuelCombat.CanWinWithExoddBlaze(own, rival, effect.sequence)) ||
                    (own.lp / 2 > 1000 && opposingTraps >= 2 && opposingTraps > ownTraps);
            }
            if (effect.code == DuelAiPolicy.Mirrorjade && effect.location == YgoConstants.LOCATION_MZONE && firstEffect)
                return rival.mzone.Any(card => card != null);
            if (effect.code == ExoddFrame && effect.location == YgoConstants.LOCATION_SZONE && (firstEffect || !cardEffect))
                return rival.mzone.Any(card => card != null);
            if (effect.code == KnightmareUnicorn && (firstEffect || summonTrigger))
                return HasFieldCard(rival);
            if (effect.code == MagiciansRod && effect.location == YgoConstants.LOCATION_GRAVE)
                return own.mzone.Any(card => card != null && card.race == YgoConstants.RACE_SPELLCASTER &&
                    !IsEstablishedFieldAce(card));
            if (effect.code == AncientGearAdvance && cardEffect && effectIndex == 1)
                return own.mzone.Any(card => card != null && !IsEstablishedFieldAce(card));
            if (effect.code == NightmarePain && firstEffect)
                return own.hand.Any(card => card.attribute == YgoConstants.ATTRIBUTE_DARK) ||
                    own.mzone.Any(CanDestroyForNightmarePain);
            if (effect.code == CyberdarkInferno && effect.location == YgoConstants.LOCATION_SZONE && firstEffect)
            {
                duel.RefreshVisibleEquipTargets(prompt.playerId);
                return own.grave.Any(card => card.race == YgoConstants.RACE_DRAGON && card.level is >= 1 and <= 3) &&
                    own.mzone.Any(card => CanRecycleCyberdark(own, card));
            }
            if (effect.code == DuelAiPolicy.MeteoroaDrytron && firstEffect)
                return !own.mzone.Any(DuelAiPolicy.IsEstablishedRitualAce) || own.mzone.Any(card =>
                    card is { IsFaceUp: true } && DuelAiPolicy.IsDrytronMonster(card.code) && !DuelAiPolicy.IsEstablishedRitualAce(card));
            if (effect.code == VisionIncrease && effect.location == YgoConstants.LOCATION_SZONE && (!cardEffect || effectIndex == 1))
                return own.mzone.Any(card => card != null && !IsEstablishedFieldAce(card) &&
                    CardDatabase.Get(card.code) is { } info && CardDatabase.ExtractSetcodes(info.setcode).Contains(HeroSet));
            if (effect.code == Cartesia && effect.location == YgoConstants.LOCATION_MZONE && cardEffect && effectIndex == 1)
                return DuelAiPolicy.CanFuseWithCartesia(own);
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
            if (effect.code == ForbiddenDroplet && !rival.mzone.Any(card =>
                card is { IsFaceUp: true, effectsDisabled: false } && (card.type & YgoConstants.TYPE_EFFECT) != 0))
                return false;
            if (effect.code == ForbiddenDroplet && own.mzone.Any(DuelAiPolicy.IsEstablishedRitualAce) &&
                !CanPayDropletWithoutRitualAce(own, effect))
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

        internal static bool IsOpponentSummoningEffect(ChainLinkInfo source, byte player) =>
            source is { negated: false, Resolved: false } && source.triggerPlayer != player &&
            (source.code is 44362883 or 52947044 or 39996157 or 22398665 or 37630732 or 48130397 ||
                ((CardDatabase.Get(source.code)?.DeckEffectCategories ?? 0) & SummoningEffects) != 0);

        internal static bool HasLethalSpecialSummonBurn(DuelSession duel, byte player)
        {
            int life = duel.Players[player].lp;
            var opponents = duel.Players[1 - player].mzone;
            return life > 0 && life <= 500 * opponents.Length &&
                life <= 500 * opponents.Count(card => IsActive(card, VolcanicEmperor));
        }

        private static bool SpecialSummonsOnResolution(SelectableCardItem effect) => effect.code switch
        {
            VisionIncrease => effect.location is YgoConstants.LOCATION_SZONE or YgoConstants.LOCATION_MZONE,
            MiracleExcluder => effect.location == YgoConstants.LOCATION_HAND,
            _ => ((CardDatabase.Get(effect.code)?.DeckEffectCategories ?? 0) & SummoningEffects) != 0,
        };

        private static uint OpposingGraveResponse(DuelSession duel, byte player, ChainLinkInfo source) =>
            source is { negated: false, Resolved: false } && source.triggerPlayer != player &&
            CardDatabase.Get(source.code)?.IsMonster == true &&
            duel.Players[1 - player].grave.Any(card => card.code == source.code)
                ? source.code : 0;

        internal static int ChooseCalledByTarget(DuelSession duel, DuelPrompt prompt)
        {
            int count = duel.CurrentChain.Count;
            if (count < 2 || duel.CurrentChain[count - 1] is not { code: CalledBy } activation ||
                activation.triggerPlayer != prompt.playerId) return -1;
            uint target = OpposingGraveResponse(duel, prompt.playerId, duel.CurrentChain[count - 2]);
            return target == 0 ? -1 : prompt.candidates.FindIndex(card => card.controller == 1 - prompt.playerId &&
                card.location == YgoConstants.LOCATION_GRAVE && card.code == target);
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

        // These shipped monster scripts resolve only away from the field.
        private static bool OffFieldOnly(uint code) => code is 18789533 or 22567609 or 39191307 or 41859700 or
            57116033 or 57839750 or 60806437 or 77044671 or 83011277 or 84834865 or 95178994 or 95956346 or 97017120;

        internal static bool IsTemporaryFieldNegation(SelectableCardItem effect) =>
            effect.code is InfiniteImpermanence or EffectVeiler;

        internal static bool CanSpendFieldNegation(DuelSession duel, DuelPrompt prompt, SelectableCardItem effect)
        {
            var rival = duel.Players[1 - prompt.playerId];
            if (duel.CurrentChain.LastOrDefault() is { negated: false, Resolved: false } source &&
                source.triggerPlayer != prompt.playerId)
            {
                if (source.triggerLoc == YgoConstants.LOCATION_MZONE && source.triggerSeq < rival.mzone.Length &&
                    rival.mzone[source.triggerSeq] is { IsFaceUp: true, effectsDisabled: false } active && active.code == source.code &&
                    !HasPendingFieldAnswer(duel, prompt.playerId, source.triggerPlayer, source.triggerLoc, source.triggerSeq, includeNegation: true) &&
                    (effect.code != InfiniteImpermanence || active.code != BlueEyesTyrant)) return true;

                if (effect.code == InfiniteImpermanence && effect.location == YgoConstants.LOCATION_SZONE &&
                    effect.sequence < 5 && source.ActivationSource == (source.triggerPlayer, YgoConstants.LOCATION_SZONE, 4u - effect.sequence) &&
                    rival.szone[4 - effect.sequence] is { IsFaceUp: true, effectsDisabled: false } spellTrap &&
                    spellTrap.code == source.code) return true;
            }

            // An empty fast-effect window can be the phase pass. Suppression without an active threat is only
            // considered as part of an actual main or battle action on our own turn.
            return duel.CurrentChain.Count == 0 && duel.TurnPlayer == prompt.playerId &&
                duel.CurrentPhase != YgoConstants.PHASE_MAIN2 &&
                (prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD ||
                    prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && !prompt.canEp) &&
                rival.mzone.Any(card => IsUsefulFieldNegationTarget(card, effect.code));
        }

        private static bool IsUsefulFieldNegationTarget(LiveCardState card, uint negate) =>
            card is { IsFaceUp: true, effectsDisabled: false } && (card.type & YgoConstants.TYPE_EFFECT) != 0 &&
            !OffFieldOnly(card.code) && (negate != InfiniteImpermanence || card.code != BlueEyesTyrant);

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

            var rival = duel.Players[1 - prompt.playerId];
            int best = -1;
            int bestPriority = int.MinValue;
            for (int i = 0; i < prompt.candidates.Count; i++)
            {
                var target = prompt.candidates[i];
                if (target.controller == prompt.playerId || target.location != YgoConstants.LOCATION_MZONE ||
                    target.sequence >= rival.mzone.Length || !IsUsefulFieldNegationTarget(rival.mzone[target.sequence], activation.code)) continue;
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

        internal static int? ChooseWidowAnchorTarget(DuelSession duel, DuelPrompt prompt)
        {
            if (duel.SelectionChainLink is not { code: (DuelDeckStrategy.WidowAnchor or 98338153) } source ||
                source.triggerPlayer != prompt.playerId) return null;

            var last = duel.CurrentChain.LastOrDefault(link => !link.negated && link.triggerPlayer != prompt.playerId);
            double bestValue = double.NegativeInfinity;
            int? best = null;
            for (int i = 0; i < prompt.candidates.Count; i++)
            {
                var target = prompt.candidates[i];
                if (target.controller == prompt.playerId || target.location != YgoConstants.LOCATION_MZONE ||
                    duel.GetVisibleFieldCard(target.code, target.controller, target.location, target.sequence) is not { } monster)
                    continue;
                if (!monster.effectsDisabled && last?.code == target.code && last.triggerLoc == target.location &&
                    last.triggerSeq == target.sequence)
                    return i;
                double value = BoardAssetValue(monster) - (monster.effectsDisabled ? 3 : 0);
                if (value <= bestValue) continue;
                bestValue = value;
                best = i;
            }
            return best;
        }

        internal static int? ChooseResourceCard(DuelSession duel, DuelPrompt prompt)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_CARD || prompt.min != 1 || prompt.max != 1 ||
                prompt.candidates.Count == 0 || DuelDeckStrategy.For(duel, prompt.playerId) != DuelDeckStyle.Resource)
                return null;

            byte player = prompt.playerId;
            var own = duel.Players[player];
            var source = duel.SelectionChainLink;
            bool ownSource = source is { negated: false } && source.triggerPlayer == player;
            if (prompt.SelectionHint == 509 && ownSource && source.code == 26077387 && duel.TurnNumber == 1)
            {
                int shizuku = prompt.candidates.FindIndex(card => card.controller == player &&
                    card.location == YgoConstants.LOCATION_EXTRA && card.code == DuelDeckStrategy.StrikerShizuku);
                if (shizuku >= 0) return shizuku;
            }
            if (prompt.SelectionHint == AddToHand && ownSource &&
                (source.code is DuelDeckStrategy.StrikerShizuku or DuelDeckStrategy.StrikerZero or DuelDeckStrategy.Engage or 63288573) &&
                prompt.candidates.All(card => card.controller == player && (card.location is YgoConstants.LOCATION_DECK or YgoConstants.LOCATION_GRAVE) &&
                    DuelDeckStrategy.IsStrikerSpell(CardDatabase.Get(card.code))))
            {
                int engage = prompt.candidates.FindIndex(card => card.code == DuelDeckStrategy.Engage);
                if (engage >= 0 && !own.hand.Any(card => card.code == DuelDeckStrategy.Engage)) return engage;
                int anchor = prompt.candidates.FindIndex(card => card.code == DuelDeckStrategy.WidowAnchor);
                return anchor >= 0 && !own.hand.Any(card => card.code == DuelDeckStrategy.WidowAnchor) ? anchor : null;
            }

            bool activeFountain = own.szone[YgoConstants.FIELD_ZONE_SEQUENCE] is
                { code: DuelDeckStrategy.RunickFountain, IsFaceUp: true, effectsDisabled: false };
            bool handFountain = own.hand.Any(card => card.code == DuelDeckStrategy.RunickFountain);

            if (prompt.SelectionHint == 509 && ownSource &&
                CardDatabase.Get(source.code) is { IsSpell: true } spell && DuelDeckStrategy.IsRunickCard(spell) &&
                (spell.type & YgoConstants.TYPE_QUICKPLAY) != 0 &&
                prompt.candidates.All(card => card.controller == player && card.location == YgoConstants.LOCATION_EXTRA &&
                    CardDatabase.Get(card.code) is { IsExtra: true } monster && DuelDeckStrategy.IsRunickCard(monster)))
            {
                int hugin = prompt.candidates.FindIndex(card => card.code == RunickHugin);
                if (!activeFountain && !handFountain)
                {
                    int geri = prompt.candidates.FindIndex(card => card.code == RunickGeri);
                    if (geri >= 0 && own.grave.Any(card => card.code == DuelDeckStrategy.RunickFountain)) return geri;
                    if (hugin >= 0 && own.hand.Count > 0 &&
                        duel.HasCardInDeck(player, card => card.code == DuelDeckStrategy.RunickFountain))
                        return hugin;
                }
                if (activeFountain && hugin >= 0 &&
                    !own.mzone.Any(card => card is { code: RunickHugin, IsFaceUp: true, effectsDisabled: false }))
                    return hugin;

                int threat = StrongestAttacker(duel.Players[1 - player]);
                if (threat > 0 && !own.mzone.Any(card => card?.IsFaceUp == true &&
                    Math.Max(card.attack, card.defense) >= threat))
                    return prompt.candidates.Select((card, index) => (index, defense: CardDatabase.Get(card.code)?.def ?? 0))
                        .OrderByDescending(card => card.defense).First().index;
                return null;
            }

            bool huginCost = source == null
                ? own.mzone.Any(card => card is { code: RunickHugin, IsFaceUp: true, effectsDisabled: false })
                : ownSource && source.code == RunickHugin;
            if (!huginCost || prompt.SelectionHint != Discard ||
                prompt.candidates.Any(card => card.controller != player || card.location != YgoConstants.LOCATION_HAND))
                return null;

            int KeepPriority(SelectableCardItem item)
            {
                var card = CardDatabase.Get(item.code);
                uint name = card == null ? item.code : CardDatabase.GetArchetypeCollectionKey(card);
                int copies = own.hand.Count(held => CardDatabase.Get(held.code) is CardInfo info &&
                    CardDatabase.GetArchetypeCollectionKey(info) == name);
                if (copies > 1) return name == DuelDeckStrategy.RunickFountain ? 0 : 1;
                if (name == DuelDeckStrategy.RunickFountain) return activeFountain ? 2 : 7;
                if (name == DuelDeckStrategy.RunickTip) return 6;
                if (ProtectInHand.Contains(name)) return 5;
                return card is { IsSpell: true } && DuelDeckStrategy.IsRunickCard(card) ? 4 : 3;
            }
            return prompt.candidates.Select((card, index) => (index, keep: KeepPriority(card)))
                .OrderBy(card => card.keep).First().index;
        }

        internal static int? ChooseResolutionYesNo(DuelSession duel, DuelPrompt prompt)
        {
            if (duel.SelectionChainLink is not { } source || source.triggerPlayer != prompt.playerId)
                return null;
            if (source.code == GraveSquirmer && prompt.promptDesc == (((ulong)GraveSquirmer << 20) | 2))
                return duel.Players[prompt.playerId].mzone.Any(card => card?.IsFaceUp == true &&
                    HasYubelDestructionFollowup(duel, prompt.playerId, card.code)) ? 1 : 0;
            if (source.code != Conquistador || prompt.promptDesc != (((ulong)Conquistador << 20) | 2))
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

        private static int SanctuaryLifeGain(DuelSession duel, byte player, LiveCardState link, bool costPaid = false)
        {
            const int cost = 1000;
            if (link?.IsFaceUp != true || (link.type & YgoConstants.TYPE_LINK) == 0 || link.attack <= 0)
                return int.MinValue;

            int gain = link.baseAttack - cost;
            if (gain <= 0 || !duel.HasActiveAttack) return gain;
            bool attacks = duel.AttackerCon == player && duel.AttackerLoc == YgoConstants.LOCATION_MZONE &&
                duel.AttackerSeq == link.sequence;
            bool defends = duel.TargetCon == player && duel.TargetLoc == YgoConstants.LOCATION_MZONE &&
                duel.TargetSeq == link.sequence;
            if (!attacks && !defends) return gain;

            var rival = duel.Players[1 - player];
            if (attacks && duel.TargetLoc == 0)
                return link.attack >= rival.lp ? int.MinValue : gain - link.attack;

            uint opposingController = attacks ? duel.TargetCon : duel.AttackerCon;
            uint opposingLocation = attacks ? duel.TargetLoc : duel.AttackerLoc;
            uint opposingSlot = attacks ? duel.TargetSeq : duel.AttackerSeq;
            if (opposingController != 1 - player || opposingLocation != YgoConstants.LOCATION_MZONE ||
                opposingSlot >= rival.mzone.Length || rival.mzone[opposingSlot] is not { IsFaceUp: true } opponent)
                return gain;

            int opposingStat = Math.Max(0, attacks && opponent.IsDefense ? opponent.defense : opponent.attack);
            // Keep a winning attack or an equal-ATK trade. Zeroing a losing Link adds its current ATK to the damage.
            if (link.attack > opposingStat || !opponent.IsDefense && link.attack == opposingStat)
                return int.MinValue;
            int previousLife = duel.Players[player].lp + (costPaid ? cost : 0);
            return previousLife + gain > opposingStat ? gain - link.attack : int.MinValue;
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
            if (duel.TurnPlayer == prompt.playerId || CardDatabase.Get(effect.code) is not CardInfo card ||
                !LinkComboPolicy.IsInteractive(card)) return 0;
            bool isDevelopment = DuelDeckStrategy.For(duel, prompt.playerId) == DuelDeckStyle.Development;
            var target = duel.CurrentChain.LastOrDefault();
            if (target == null || target.triggerPlayer == prompt.playerId || target.negated)
            {
                if (isDevelopment) return (prompt.hintTimingOther & (YgoConstants.TIMING_SPSUMMON | YgoConstants.TIMING_ATTACK)) != 0 ? .35f : 0f;
                return (prompt.hintTimingOther & (YgoConstants.TIMING_SPSUMMON | YgoConstants.TIMING_ATTACK)) == 0 ? -.65f : 0f;
            }
            uint effectTypes = CardDatabase.Get(target.code)?.DeckEffectCategories ?? 0;
            if ((effectTypes & Development) != 0) return 1.1f;
            return isDevelopment ? 0f : -.2f;
        }

        private static bool HasReadyNegation(DuelSession duel, byte player, bool monsters)
        {
            var field = duel.Players[player];
            bool Unused(uint code, int effect = 0) =>
                !duel.HasActivatedMonsterEffect(player, ((ulong)code << 20) | (uint)effect);
            foreach (var card in field.mzone)
            {
                if (card is not { IsFaceUp: true, effectsDisabled: false }) continue;
                bool ready = card.code switch
                {
                    Baronne => !duel.HasSpentBaronneNegate(card),
                    Blazar => true,
                    Dragoon => field.hand.Count > 0 && Unused(Dragoon, 1),
                    CyberInfinity => card.overlayCodes.Count > 0,
                    Dragonlord => card.attack >= 1000 && card.defense >= 1000,
                    Quasar => Unused(Quasar),
                    ShootingMajestic => Unused(ShootingMajestic, 1),
                    BlueEyesUltimateSpirit => Unused(BlueEyesUltimateSpirit),
                    Savage => Unused(Savage, 1) && Array.Exists(card.Counters, counter => counter.Type == 0x147 && counter.Count > 0),
                    Apollousa => monsters && card.attack >= 800,
                    CrystalWing => monsters && Unused(CrystalWing),
                    Rulkallos => monsters && Unused(Rulkallos),
                    _ => false,
                };
                if (ready) return true;
            }
            return false;
        }

        internal static int? ChooseGoingSecondBait(DuelSession duel, DuelPrompt prompt, Func<int, SelectableCardItem, bool> canUse)
        {
            if (duel.TurnNumber != 2 || duel.TurnPlayer != prompt.playerId ||
                duel.CurrentPhase != YgoConstants.PHASE_MAIN1) return null;
            byte rival = (byte)(1 - prompt.playerId);
            var opposingField = duel.Players[rival];
            bool spellNegate = HasReadyNegation(duel, rival, monsters: false) ||
                opposingField.mzone.Any(card => card is { IsFaceUp: true, effectsDisabled: false } &&
                    (card.code == NaturiaBeast && opposingField.deckCount >= 2 ||
                     card.code == ShiEn && !duel.HasActivatedMonsterEffect(rival, (ulong)ShiEn << 20)));
            if (!spellNegate && !HasReadyNegation(duel, rival, monsters: true)) return null;

            bool IsOwn(SelectableCardItem item) => item.controller == prompt.playerId;
            bool hasSummonLine = prompt.spSummonable.Any(item => IsOwn(item) && item.location == YgoConstants.LOCATION_EXTRA && canUse(1, item)) ||
                prompt.summonable.Any(item => IsOwn(item) && !ComboSearch.NeedsProvenNormalSummon(item.code) &&
                    ComboSearch.CanDevelopFromCard(CardDatabase.Get(item.code)) && canUse(0, item));
            int best = -1, bestValue = 0;
            for (int i = 0; i < prompt.activatable.Count; i++)
            {
                var item = prompt.activatable[i];
                if (!IsOwn(item)) continue;
                int value = item.location switch
                {
                    YgoConstants.LOCATION_MZONE when item.code == Baronne && item.description == ((ulong)Baronne << 20) &&
                        HasFieldCard(opposingField) => 3,
                    YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_SZONE when spellNegate &&
                        item.code is Reinforcement or FossilDig or Tuning or Tenki or Terraforming or MysticalSpaceTyphoon => 2,
                    _ => 0,
                };
                if (value <= bestValue || !canUse(5, item)) continue;

                // Do not sacrifice the only entry point. The second legal action must be independent of this copy.
                if (!hasSummonLine && !prompt.activatable.Any(other => other.code != item.code && IsOwn(other) &&
                    (CardDatabase.Get(other.code)?.DeckEffectCategories & SummoningEffects) != 0 && canUse(5, other))) continue;
                best = i;
                bestValue = value;
            }
            return best < 0 ? null : (best << 16) | 5;
        }

        internal static ExtensionRisk GetExtensionRisk(DuelSession duel, DuelPrompt prompt)
        {
            if (duel.TurnPlayer != prompt.playerId || (!prompt.canBp && !prompt.canEp)) return ExtensionRisk.None;

            var own = duel.Players[prompt.playerId];
            var rival = duel.Players[1 - prompt.playerId];
            int established = 0, faceUp = 0, fieldBodies = 0, attackPosition = 0;
            foreach (var card in own.mzone)
            {
                if (card == null) continue;
                fieldBodies++;
                if (!card.IsFaceUp) continue;
                faceUp++;
                if (card.IsAttack) attackPosition++;
                if (!card.effectsDisabled && !LinkComboPolicy.IsBridge(card.code) &&
                    CardDatabase.Get(card.code) is { IsExtra: true } info &&
                    (Math.Max(card.attack, card.defense) >= 2500 || LinkComboPolicy.IsInteractive(info)))
                    established++;
            }
            if (faceUp == 0) return ExtensionRisk.None;

            // An empty field or exposed starter still needs a line. Protect a credible board or two bodies
            // when the opponent has publicly revealed Nibiru. Its fifth monster may be part of a group summon.
            bool hasSetInteraction = own.szone.Any(card => card is { IsFaceUp: false } &&
                CardDatabase.Get(card.code) is { IsTrap: true } trap && LinkComboPolicy.IsInteractive(trap));
            ExtensionRisk risk = ExtensionRisk.None;
            int summoned = duel.SummonedMonstersThisTurn(prompt.playerId);
            if (summoned >= 4 && rival.hand.Count > 0 && !HasReadyNegation(duel, prompt.playerId, monsters: true))
            {
                bool knownNibiru = fieldBodies >= 2 && duel.IsKnownInOpposingHand(prompt.playerId, Nibiru);
                int opposingAttack = StrongestAttacker(rival);
                bool stableAgainstBattle = own.mzone.Any(card => card is { IsFaceUp: true } &&
                    Math.Max(card.attack, card.defense) >= opposingAttack);
                // A single bridge or a board that already loses combat still needs a playable continuation.
                bool canHold = stableAgainstBattle && (established >= 2 || hasSetInteraction && fieldBodies >= 3);
                if (knownNibiru || canHold) risk |= ExtensionRisk.Nibiru;
            }

            bool knownSweeper = faceUp >= 2 && (duel.IsKnownInOpposingHand(prompt.playerId, Raigeki) ||
                duel.IsKnownInOpposingHand(prompt.playerId, DarkHole) ||
                duel.IsKnownInOpposingHand(prompt.playerId, InterruptedKaijuSlumber) ||
                attackPosition >= 2 && duel.IsKnownInOpposingHand(prompt.playerId, LightningStorm) ||
                faceUp >= 3 && duel.IsKnownInOpposingHand(prompt.playerId, EvenlyMatched));
            // Keep recovery in hand behind a developed board even when the opponent has not revealed the wipe.
            bool exposedSwarm = duel.TurnNumber == 1 && rival.hand.Count > 0 && faceUp >= 3 && established >= 2 &&
                summoned >= 4;
            if ((knownSweeper || exposedSwarm) && !HasReadyNegation(duel, prompt.playerId, monsters: false))
                risk |= ExtensionRisk.Sweeper;
            return risk;
        }

        internal static bool CanExtend(ExtensionRisk risk, PlayerFieldSnapshot field, int command, SelectableCardItem item)
        {
            if (risk == ExtensionRisk.None || command is not (0 or 1 or 5)) return true;
            if (command == 1)
            {
                // A live answer on the fifth summon can respond to Nibiru. Do not trade into
                // monster-only negation when the actual concern is a spell or trap wipe.
                return item.location == YgoConstants.LOCATION_EXTRA && (item.code switch
                {
                    Baronne or Blazar or CyberInfinity or Dragonlord or Quasar or ShootingMajestic => true,
                    Dragoon => field.hand.Count > 0,
                    Apollousa or CrystalWing or Rulkallos => (risk & ExtensionRisk.Sweeper) == 0,
                    _ => false,
                });
            }
            return command == 5 && item.description != 1160 &&
                ((CardDatabase.Get(item.code)?.DeckEffectCategories ?? 0) & SummoningEffects) == 0;
        }

        internal static float ChokepointPriority(DuelSession duel, DuelPrompt prompt, SelectableCardItem response)
        {
            if (!ResponseNegates.Contains(response.code) || duel.CurrentChain.LastOrDefault() is not
                { negated: false } source || source.triggerPlayer == prompt.playerId) return 0;
            if (response.code == AshBlossom)
            {
                if (source.code is OriginalSinfulSpoils or MaxxC or DuelAiPolicy.BrandedFusion or DuelAiPolicy.FusionDestiny or
                    55144522 or 79571449 or 8949584 or 98567237 or 34447918 or 93729896 or 96729612 or 13048472)
                    return 4f;
                if (source.code is Reinforcement or FossilDig or Tuning or Tenki or Terraforming or 60600126)
                    return 2.6f;
            }
            if (response.code == CalledBy)
            {
                if (ComboSearch.IsHandDisruption(source.code) || source.code is DestroyerPhoenix or 2463794 or 60764609)
                    return 3.5f;
            }
            if (response.code is EffectVeiler or InfiniteImpermanence)
            {
                if (source.code is Stratos or 27780618 or 62962630 or 86377375 or 92919429 or 86120751 or
                    49867899 or 32909498 or 68304193 or 23893227 or 70534340 or 87746184)
                    return 3.2f;
            }
            uint categories = CardDatabase.Get(source.code)?.DeckEffectCategories ?? 0;
            return (categories & Development) != 0 ? 1.8f : 0f;
        }

        internal static int ChainProtectionPriority(SelectableCardItem trigger)
        {
            var card = CardDatabase.Get(trigger.code);
            if (card == null) return 0;
            uint categories = card.DeckEffectCategories;
            const uint search = 1u << 5, draw = 1u << 4;
            if (trigger.code is 74078255 or 37961969 or 572850 && (trigger.description & 0xfffff) == 0)
                return 2; // The on-field mill/extension and Graveyard Fusion have different blocking value.
            if ((categories & SummoningEffects) != 0) return 4;
            if ((categories & search) != 0) return 3;
            return (categories & draw) != 0 ? 1 : 0;
        }

        internal static bool IsRemoval(ulong hint) => hint is Destroy or Banish or SendToGrave or ReturnToHand or
            ReturnToDeck or Negate or ReturnToGrave;

        internal static bool HasPendingFieldAnswer(DuelSession duel, byte player, byte targetController, uint targetLocation,
            uint targetSequence, bool includeNegation, bool skipSelectionLink = false)
        {
            const uint removal = (1u << 0) | (1u << 1) | (1u << 2) | (1u << 3);
            foreach (var link in duel.CurrentChain)
            {
                if (link.triggerPlayer != player || link.negated || link.Resolved || link.PublicTargets == null ||
                    skipSelectionLink && ReferenceEquals(link, duel.SelectionChainLink) ||
                    CardDatabase.Get(link.code) is not { } card) continue;
                uint relevant = removal | (includeNegation ? (1u << 8) : 0u);
                if ((card.DeckEffectCategories & relevant) != 0 &&
                    link.PublicTargets.Contains((targetController, targetLocation, targetSequence))) return true;
            }
            return false;
        }

        internal static bool IsResponseNegate(uint code) => ResponseNegates.Contains(code);

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
            if (!own && live != null && IsRemoval(hint) &&
                (hint == Negate && live.effectsDisabled ||
                    HasPendingFieldAnswer(duel, player, option.controller, option.location, option.sequence,
                        includeNegation: hint == Negate, skipSelectionLink: true)))
                return -15000;
            bool visible = own || live?.IsFaceUp == true || option.location == YgoConstants.LOCATION_GRAVE;
            var card = visible ? CardDatabase.Get(option.code) : null;
            int attack = live != null && visible ? live.attack : card?.atk ?? 1000;
            int value = Math.Max(0, attack);
            if (card != null && (card.type & YgoConstants.TYPE_EFFECT) != 0 && live?.effectsDisabled != true) value += 350;
            if (card?.IsExtra == true) value += 500;
            if (own && hint is Release or Destroy or Banish or SendToGrave or ReturnToHand or ReturnToDeck or 511 or 512 or 513 or 533 &&
                (DuelAiPolicy.IsEstablishedRitualAce(live) ||
                    live is { code: EternalSoul, IsFaceUp: true } && side.mzone.Any(monster => monster != null)))
                return -20000 - value;
            if (own && hint == Destroy && duel.SelectionChainLink is { code: GraveSquirmer } squirmer &&
                squirmer.triggerPlayer == player)
                return HasYubelDestructionFollowup(duel, player, option.code) ? 5000 : -5000 - value;
            if (!own && hint == Destroy && duel.SelectionChainLink is { code: DestroyerPhoenix } phoenix &&
                phoenix.triggerPlayer == player && IsProtectedCyberEternity(live, side))
                return 0;
            if (own && duel.SelectionChainLink is { code: SalamangreatSanctuary, description: SanctuaryBattleRecovery } sanctuary &&
                sanctuary.triggerPlayer == player)
                return SanctuaryLifeGain(duel, player, live, costPaid: true);
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
                else if (DuelAiPolicy.HasGraveyardValue(card)) score += 2400;
                if (DuelDeckStrategy.For(duel, player) == DuelDeckStyle.Lock && DuelDeckStrategy.IsKeyPiece(option.code))
                    score -= 9000;
                if (option.location == YgoConstants.LOCATION_HAND && ProtectInHand.Contains(option.code) && copies <= 1)
                    score -= 2500;
                if (option.location == YgoConstants.LOCATION_DECK && ProtectInHand.Contains(option.code))
                    score -= 3500;
                if (card != null && card.IsMonster && !card.IsExtra && card.level >= 5 && !DuelAiPolicy.CanSelfSpecialFromHand(card))
                    score += 800;
                return score;
            }
            // Universal smart deck-search ranking (hint == AddToHand):
            // Prioritize missing combo spells, playable starters/extenders, Synchro/Ritual missing halves,
            // and avoid searching unplayable high-level bricks just because they have high printed ATK.
            if (hint == AddToHand && own && option.location == YgoConstants.LOCATION_DECK && card != null)
            {
                bool alreadyInHand = side.hand.Any(h => h.code == option.code);
                uint cats = card.DeckEffectCategories;
                int searchScore = 10000 + (alreadyInHand ? -900 : 1200);
                if (card.IsSpell || card.IsTrap)
                {
                    if ((cats & (Development | (1u << 4))) != 0) searchScore += 3200;
                    else if ((cats & ((1u << 0) | (1u << 1) | (1u << 2) | (1u << 3) | (1u << 8))) != 0) searchScore += 1800;
                    else searchScore += 900;
                    bool isRitualSpell = (card.type & YgoConstants.TYPE_RITUAL) != 0;
                    if (isRitualSpell && side.hand.Any(h => (CardDatabase.Get(h.code)?.type & YgoConstants.TYPE_RITUAL) != 0 &&
                        CardDatabase.Get(h.code)?.IsMonster == true))
                        searchScore += 2600;
                }
                else if (card.IsMonster)
                {
                    bool isRitual = (card.type & YgoConstants.TYPE_RITUAL) != 0;
                    bool isTuner = (card.type & YgoConstants.TYPE_TUNER) != 0;
                    bool selfSpecial = DuelAiPolicy.CanSelfSpecialFromHand(card);
                    if (isRitual)
                    {
                        bool hasRitualSpell = side.hand.Any(h => CardDatabase.Get(h.code) is { IsSpell: true } sp &&
                            (sp.type & YgoConstants.TYPE_RITUAL) != 0);
                        searchScore += hasRitualSpell ? 2800 : 800;
                    }
                    else if (card.level <= 4 || selfSpecial)
                    {
                        searchScore += (cats & Development) != 0 ? 2700 : 1200;
                        if (selfSpecial) searchScore += 700;
                    }
                    else
                    {
                        // Penalize unplayable Level 5+ bricks unless they have a hand/GY engine effect
                        // or are an iconic theme signature material (Blue-Eyes, Dark Magician, Neos, Yubel, Kashtira).
                        bool iconicSignature = option.code is 89631139 or 46986414 or 38033120 or 89943723 or
                            74677422 or 70095154 or 78371393 or 90829280 or 32909498 or 68304193;
                        searchScore += iconicSignature ? 1400 : (cats & Development) != 0 ? 400 : -1600;
                    }
                    if (ComboSearch.IsHandDisruption(option.code)) searchScore += 1300;
                    // Synchro missing-half synergy (Tuner <-> non-Tuner)
                    bool fieldHasTuner = side.mzone.Any(m => m is { IsFaceUp: true } && (m.type & YgoConstants.TYPE_TUNER) != 0);
                    bool fieldHasNonTuner = side.mzone.Any(m => m is { IsFaceUp: true } && (m.type & (YgoConstants.TYPE_TUNER | YgoConstants.TYPE_LINK | YgoConstants.TYPE_XYZ)) == 0);
                    if (fieldHasTuner && !fieldHasNonTuner && !isTuner && (card.level <= 4 || selfSpecial)) searchScore += 1300;
                    else if (fieldHasNonTuner && !fieldHasTuner && isTuner && (card.level <= 4 || selfSpecial)) searchScore += 1300;
                    searchScore += Math.Min(1200, Math.Max(0, card.atk) / 3);
                }
                return searchScore;
            }
            // Universal smart Special Summon / GY Revival / Deck Recruit ranking (hint == 509):
            if (hint == 509 && own && card != null)
            {
                int ssScore = 10000 + value;
                if (LinkComboPolicy.IsInteractive(card)) ssScore += 1400;
                if ((card.DeckEffectCategories & (1u << 8)) != 0) ssScore += 1200;
                if ((card.DeckEffectCategories & Development) != 0) ssScore += 900;
                return ssScore;
            }
            if (own && hint is Release or 511 or 512 or 513 or 533)
            {
                int matScore = -5000 - value;
                if (live != null && IsEstablishedFieldAce(live)) matScore -= 8000;
                if (live != null && (live.type & YgoConstants.TYPE_TOKEN) != 0) matScore += 1500;
                if (DuelAiPolicy.HasGraveyardValue(card)) matScore += 1200;
                return matScore;
            }
            if (IsRemoval(hint) || hint is Release or 511 or 512 or 513 or 533)
                return own ? -5000 - value : 10000 + value;
            return own ? 10000 + value : -5000 + value;
        }

        internal static bool IsEstablishedFieldAce(LiveCardState card) => card != null &&
            card.code is not (DuelAiPolicy.Albion or DuelAiPolicy.Lubellion or Sunrise or 49867899 or 2463794) &&
            (DuelAiPolicy.IsEstablishedRitualAce(card) ||
                card.code is 80453041 or 87182127 or 1948619 || card.attack >= 2500 ||
                CardDatabase.Get(card.code) is { IsExtra: true } info &&
                    (card.attack >= 2000 || !card.effectsDisabled && LinkComboPolicy.IsInteractive(info)));

        private static bool IsYubelDestructionStarter(uint code) => code is 78371393 or 90829280 or 4779091;

        private static bool HasYubelDestructionFollowup(DuelSession duel, byte player, uint code)
        {
            uint next = code switch { 90829280 => 78371393, 78371393 => 4779091, 4779091 => 31764700, _ => 0 };
            if (next == 0) return false;
            var own = duel.Players[player];
            return own.hand.Any(card => card.code == next) || own.grave.Any(card => card.code == next) ||
                code == 90829280 && own.banished.Any(card => card.code == next && card.IsFaceUp) ||
                duel.CountOwnDeckCopies(player, next) > 0;
        }

        private static bool CanDestroyForNightmarePain(LiveCardState card) =>
            card is { IsFaceUp: true, attribute: YgoConstants.ATTRIBUTE_DARK } &&
            (IsYubelDestructionStarter(card.code) || !IsEstablishedFieldAce(card));

        private static bool CanRecycleCyberdark(PlayerFieldSnapshot own, LiveCardState card) =>
            card is { IsFaceUp: true, code: 3019642 or 41230939 or 77625948 } &&
            !own.szone.Any(equip => equip?.EquipTarget == (card.controller, card.sequence));

        private static bool CanPayDropletWithoutRitualAce(PlayerFieldSnapshot own, SelectableCardItem spell) =>
            own.hand.Any(card => card.code != ForbiddenDroplet) ||
            own.hand.Count(card => card.code == ForbiddenDroplet) > (spell.location == YgoConstants.LOCATION_HAND ? 1 : 0) ||
            own.mzone.Any(card => card != null && !DuelAiPolicy.IsEstablishedRitualAce(card) &&
                (card.type & (YgoConstants.TYPE_TOKEN | YgoConstants.TYPE_PENDULUM)) == 0) ||
            own.szone.Where((card, index) => spell.location != YgoConstants.LOCATION_SZONE || index != spell.sequence)
                .Any(card => card != null && (card.type & YgoConstants.TYPE_PENDULUM) == 0 &&
                    !(card.code == EternalSoul && card.IsFaceUp));

        internal static bool IsGxFieldCost(uint effect, ulong hint) =>
            hint == Release && (effect is AncientGearAdvance or VisionIncrease or MagiciansRod or DuelAiPolicy.MeteoroaDrytron ||
                DuelAiPolicy.IsDrytronReleaseEffect(effect) || DuelAiPolicy.IsMachineAngelTribute(effect)) ||
            hint == Destroy && effect == NightmarePain || hint == ReturnToHand && effect == CyberdarkInferno ||
            hint == SendToGrave && effect == ForbiddenDroplet ||
            hint == 511 && effect == Cartesia;

        internal static int SummonMaterialPriority(DuelSession duel, byte player, SelectableCardItem option,
            uint source, ulong hint)
        {
            var own = duel.Players[player];
            if ((DuelAiPolicy.IsRitualSpell(source) || DuelAiPolicy.IsDrytronReleaseEffect(source) ||
                    DuelAiPolicy.IsMachineAngelTribute(source) || source == ForbiddenDroplet && hint == SendToGrave) &&
                DuelAiPolicy.IsProtectedRitualMaterial(own, player, option))
                return -20000;
            if (option.controller != player) return 10000 + SelectionPriority(duel, player, option, Destroy);
            if (source == NightmarePain && IsYubelDestructionStarter(option.code)) return 5000;
            var onField = option.location == YgoConstants.LOCATION_MZONE && option.sequence < own.mzone.Length
                ? own.mzone[option.sequence] : null;
            if ((IsGxFieldCost(source, hint) || source == DuelAiPolicy.SuperPoly) && IsEstablishedFieldAce(onField)) return -20000;
            if (option.code == EternalSoul && option.location == YgoConstants.LOCATION_SZONE &&
                option.sequence < own.szone.Length && own.szone[option.sequence]?.IsFaceUp == true &&
                own.mzone.Any(monster => monster != null))
                return -20000;
            if (source == CyberdarkInferno && !CanRecycleCyberdark(own, onField)) return -20000;
            var info = CardDatabase.Get(option.code);
            int score = -(Math.Max(0, info?.atk ?? 1000) + Math.Max(0, info?.def ?? 0) / 3);
            if (option.location == YgoConstants.LOCATION_MZONE)
                score -= 1000 + (info?.IsExtra == true || (info?.type & YgoConstants.TYPE_RITUAL) != 0 ? 4500 : 0);
            if (option.location == YgoConstants.LOCATION_DECK) score += 1700;
            if (option.location == YgoConstants.LOCATION_GRAVE) score += 2200;
            if (option.location == YgoConstants.LOCATION_HAND && ProtectInHand.Contains(option.code) &&
                own.hand.Count(card => card.code == option.code) < 2) score -= 3000;
            if ((DuelAiPolicy.IsRitualSpell(source) || DuelAiPolicy.IsMachineAngelTribute(source)) &&
                hint == Release && option.code == DuelAiPolicy.Benten)
                score += 4200;
            if (source == DuelAiPolicy.FusionDestiny && option.code is 9411399 or 16605586)
                score += 4000;
            if ((hint is SendToGrave or Discard || hint == 511 && option.location != YgoConstants.LOCATION_GRAVE) &&
                GraveyardBeneficiaries.Contains(option.code)) score += 2100;
            if (source == ForbiddenDroplet && option.location == YgoConstants.LOCATION_SZONE &&
                info is { IsSpell: true } && (info.type & (YgoConstants.TYPE_CONTINUOUS | YgoConstants.TYPE_FIELD | YgoConstants.TYPE_EQUIP)) == 0 &&
                duel.CurrentChain.Any(link => link.triggerPlayer == player && !link.Resolved && link.code == option.code &&
                    link.ActivationSource == (player, option.location, option.sequence)))
                score += 6500;
            return score;
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

            if (canAttack && DuelCombat.IsYubel(card.code) && DuelCombat.HasNightmarePain(duel.Players[prompt.playerId]) &&
                rivalStrongest > 0) return (int)YgoConstants.POS_FACEUP_ATTACK;
            if (canAttack && DuelCombat.CanTestLethalPosition(duel, prompt.playerId, card.atk))
                return (int)YgoConstants.POS_FACEUP_ATTACK;

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
            bool knownBurst = RisksRevealedBattleLethal(duel, prompt.playerId, card.atk, card.atk);
            if (knownBurst && (compareWithBattle || !prompt.canBp ||
                !DuelCombat.CanTestLethalPosition(duel, prompt.playerId, card.atk)))
                return FindSettableIndex(prompt, summon);

            int exposedDamage = rivalStrongest - Math.Max(0, card.atk);
            if (exposedDamage <= 0) return null;
            bool hasFieldBodies = own.mzone.Any(m => m != null);
            // With a lone attacker, the other visible opponents can attack directly after it is destroyed.
            bool threatensLethal = exposedDamage >= own.lp ||
                (!hasFieldBodies && DuelCombat.VisibleAttackTotal(rival) - Math.Max(0, card.atk) >= own.lp);
            bool fragileAtLowLife = followupUnproven && DuelCombat.IsWeakBattleLiability(card.atk, rivalStrongest, own.lp);
            if (lethalOnly && !threatensLethal && !fragileAtLowLife) return null;
            if (!compareWithBattle && !threatensLethal && !fragileAtLowLife && prompt.canBp &&
                AddsFavorableBattle(own, rival, card.atk)) return null;

            bool isVanillaOrFlip = (card.type & (YgoConstants.TYPE_NORMAL | YgoConstants.TYPE_FLIP)) != 0;
            bool hasImmediateSpecial = prompt.spSummonable.Count > 0;

            // Existing defenders do not prevent a stronger attacker from choosing the new monster.
            bool unprovenRisk = fragileAtLowLife || followupUnproven && (threatensLethal || card.atk < 1800 && exposedDamage > 200);
            bool shouldSet = unprovenRisk || isVanillaOrFlip || (!hasFieldBodies && !hasImmediateSpecial && exposedDamage > 200);
            if (!shouldSet) return null;

            return FindSettableIndex(prompt, summon);
        }

        internal static bool RisksRevealedBattleLethal(DuelSession duel, byte defender, int attack, int printedAttack)
        {
            int life = duel.Players[defender].lp;
            // A Set must also survive the possible effect damage after the battle.
            if (attack >= 1800 || life <= Math.Max(0, printedAttack) ||
                !duel.IsKnownInOpposingHand(defender, MiracleFusion)) return false;

            var own = duel.Players[defender];
            var rival = duel.Players[1 - defender];
            if (rival.extra.Count == 0 || own.szone.Concat(rival.szone).Any(card => IsActive(card, ImperialIronWall)) ||
                own.mzone.Any(card => IsActive(card, ChaosHunter))) return false;

            // Hidden Extra Deck identities are never needed. Estimate a plausible Fusion ceiling from public materials.
            var materials = rival.mzone.Where(card => card?.IsFaceUp == true);
            if (!own.mzone.Any(card => IsActive(card, Kycoo)) &&
                !own.szone.Concat(rival.szone).Any(card => IsActive(card, Necrovalley)))
                materials = materials.Concat(rival.grave);
            int warriors = 0;
            bool elementalFusion = false;
            foreach (var material in materials)
            {
                bool warrior = (material.race & YgoConstants.RACE_WARRIOR) != 0;
                warriors += warrior ? 1 : 0;
                if (!elementalFusion && (material.type & YgoConstants.TYPE_FUSION) != 0 &&
                    CardDatabase.Get(material.code) is CardInfo info && CardDatabase.ExtractSetcodes(info.setcode).Contains(ElementalHeroSet))
                    elementalFusion = true;
            }

            var fusion = CardDatabase.Get(WakeUpElementalHero);
            return elementalFusion && warriors >= 2 && fusion != null &&
                fusion.atk + warriors * 300 - Math.Max(0, attack) >= life;
        }

        private static bool IsActive(LiveCardState card, uint code) => card?.code == code && card.IsFaceUp && !card.effectsDisabled;

        internal static bool CanNegateWithIllusionOfChaos(LiveCardState monster, PlayerFieldSnapshot owner, PlayerFieldSnapshot rival) =>
            IsActive(monster, IllusionOfChaos) && owner.grave.Any(card => card.code == DarkMagician || card.alias == DarkMagician) &&
            (monster.sequence < 5 || owner.mzone.Take(5).Any(card => card == null)) &&
            !owner.szone.Concat(rival.szone).Any(card => IsActive(card, Necrovalley));

        internal static int? FindSettableIndex(DuelPrompt prompt, SelectableCardItem summon)
        {
            int index = prompt.mSetable.FindIndex(item => item.controller == prompt.playerId &&
                item.location == summon.location && item.sequence == summon.sequence);
            return index >= 0 ? index : null;
        }

        internal static int? ChooseDefensiveReposition(DuelSession duel, DuelPrompt prompt, bool beforeBattle = false)
        {
            int opponent = 1 - prompt.playerId;
            var rival = duel.Players[opponent];
            int threat = StrongestAttacker(rival);
            if (!beforeBattle) threat = Math.Max(threat, duel.PotentialReturningAttack(opponent));
            bool rivalCanSummon = !beforeBattle && (rival.hand.Count > 0 || rival.szone.Any(c => c != null));
            if (threat <= 0 && !rivalCanSummon) return null;
            var own = duel.Players[prompt.playerId];
            if (beforeBattle && StrongestAttacker(own) >= threat) return null;
            for (int i = 0; i < prompt.repositionable.Count; i++)
            {
                var option = prompt.repositionable[i];
                if (option.controller != prompt.playerId || option.sequence >= own.mzone.Length)
                    continue;
                var card = own.mzone[option.sequence];
                if (card?.IsFaceUp != true || !card.IsAttack || (card.type & YgoConstants.TYPE_LINK) != 0)
                    continue;
                // Defense Position can protect LP even when the monster will still be destroyed.
                if (threat > 0 && card.attack < threat && (!beforeBattle || threat - Math.Max(0, card.attack) >= own.lp))
                    return i;
                // Even into an empty board, don't leave a fragile <= 1200 ATK starter exposed in Attack Position.
                if (rivalCanSummon && card.attack <= 1200 && card.defense >= card.attack)
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
            if (field.Count(monster => monster != null) == 1 && field.Any(monster => monster?.IsDefense == true) &&
                DuelCombat.VisibleAttackTotal(duel.Players[1 - prompt.playerId]) >= duel.Players[prompt.playerId].lp)
                return true;
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

        internal static bool NeedsRitualTributeProjection(PlayerFieldSnapshot own, SelectableCardItem summon) =>
            summon.location == YgoConstants.LOCATION_HAND && CardDatabase.Get(summon.code) is { level: >= 5 } incoming &&
            own.mzone.Any(DuelAiPolicy.IsEstablishedRitualAce) &&
            own.mzone.Count(card => card != null && !DuelAiPolicy.IsEstablishedRitualAce(card)) < (incoming.level >= 7 ? 2 : 1);

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

        private static bool AddsFavorableBattle(PlayerFieldSnapshot own, PlayerFieldSnapshot rival, int attack)
        {
            if (!CanWinBattle(rival, attack)) return false;
            var monsters = rival.mzone.Where(card => card != null).ToArray();
            if (monsters.Length == 0) return true;
            var thresholds = monsters.Where(card => card.IsFaceUp)
                .Select(card => card.IsDefense ? card.defense : card.attack).ToArray();

            // An extra attacker adds value if it can clear another blocker or follow through to a direct attack.
            var existing = own.mzone.Where(card => card?.IsFaceUp == true && card.IsAttack).Select(card => card.attack).ToArray();
            int matched = DuelCombat.CountFavorableBattles(existing, thresholds);
            return matched == monsters.Length || DuelCombat.CountFavorableBattles(existing.Append(attack), thresholds) > matched;
        }

        internal static int StrongestAttacker(PlayerFieldSnapshot rival)
        {
            int attack = 0;
            foreach (var card in rival.mzone)
                if (card?.IsFaceUp == true && card.IsAttack) attack = Math.Max(attack, card.attack);
            return attack;
        }
    }
}
