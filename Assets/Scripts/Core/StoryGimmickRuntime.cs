using System;
using System.Collections.Generic;

namespace YGOPortable.Core
{
    internal enum StoryCrisisSkill { None, ShiningDraw, StormAccess }

    internal sealed class StoryGimmickRuntime
    {
        internal const uint SpeedWorld = 511090001;
        internal const uint AngelBaton = 511090002;
        internal const ushort SpeedCounter = 0x0091;
        internal const byte CountdownWinReason = 0x80;
        internal const uint DarknessZero = 511090101;
        internal const uint DarknessInfinity = 511090102;
        internal const uint HandcuffToken = 511090201;
        internal const uint ActionField = 511090301;
        internal const uint ActionEvasion = 511090302;
        internal const uint ActionMiracle = 511090303;
        private static readonly CardInfo SpeedWorldCard = new()
        {
            code = SpeedWorld,
            type = YgoConstants.TYPE_SPELL | YgoConstants.TYPE_FIELD,
            name = "스피드 월드 2",
            desc = "서로의 스탠바이 페이즈마다 이 카드에 스피드 카운터(SPC)를 1개 놓는다(최대 12개). 피해를 받아도 카운터는 줄어들지 않는다.",
        };
        private static readonly CardInfo AngelBatonCard = new()
        {
            code = AngelBaton,
            type = YgoConstants.TYPE_SPELL,
            name = "Sp－엔젤 배턴",
            desc = "자신의 「스피드 월드 2」에서 스피드 카운터(SPC)를 2개 제거하고 발동할 수 있다. 2장 드로우한 뒤 패 1장을 버린다.",
        };
        private static readonly CardInfo HandcuffTokenCard = new()
        {
            code = HandcuffToken,
            type = YgoConstants.TYPE_MONSTER | YgoConstants.TYPE_NORMAL | YgoConstants.TYPE_TOKEN,
            name = "수갑 토큰",
            desc = "스토리 전용 토큰. 공격할 수 없으며, 어드밴스 소환을 위해 릴리스할 수 없다.",
            level = 1,
            attr = 1,
            race = 0x2000,
        };
        private static readonly CardInfo[] DarknessTraps =
        {
            DarknessTrap(DarknessZero, "제로", "다크니스 스토리 전용. 이 카드와 인피니티 사이에 세트된 함정을 공개하고, 처음 공개한 다크니스의 효과를 적용한다."),
            DarknessTrap(DarknessInfinity, "인피니티", "다크니스 스토리 전용. 제로와 짝을 이루며, 두 카드 사이의 다크니스 함정만 효과에 사용한다."),
            DarknessTrap(511090103, "다크니스 1", "첫 번째로 공개되면, 공개한 다크니스 수만큼 상대 필드의 카드를 파괴한다."),
            DarknessTrap(511090104, "다크니스 2", "첫 번째로 공개되면, 자신의 몬스터 1장을 고른다. 공개한 다크니스 1장마다 그 몬스터의 공격력이 턴 종료까지 1000 오른다."),
            DarknessTrap(511090105, "다크니스 3", "첫 번째로 공개되면, 공개한 다크니스 1장당 상대에게 1000 데미지를 준다."),
        };

        private static readonly CardInfo ActionFieldCard = ActionSpell(ActionField, YgoConstants.TYPE_FIELD,
            "액션 필드", "자신 메인 페이즈에 1턴에 1번, 패에 액션 카드가 없으면 액션 카드를 탐색해 1장 얻는다.");
        private static readonly CardInfo ActionEvasionCard = ActionSpell(ActionEvasion, YgoConstants.TYPE_QUICKPLAY,
            "액션 마법－회피", "상대가 공격을 선언했을 때 패에서도 발동할 수 있다. 그 공격을 무효로 한다.");
        private static readonly CardInfo ActionMiracleCard = ActionSpell(ActionMiracle, YgoConstants.TYPE_QUICKPLAY,
            "액션 마법－미라클", "상대가 자신의 몬스터에 공격을 선언했을 때 패에서도 발동할 수 있다. 해당 몬스터는 이번 전투로 파괴되지 않으며 자신이 받는 전투 데미지는 절반이 된다.");

        private static CardInfo ActionSpell(uint code, uint subtype, string name, string description) => new()
        {
            code = code,
            type = YgoConstants.TYPE_SPELL | subtype,
            name = name,
            desc = description,
            strs = new[] { description },
        };

        private static CardInfo DarknessTrap(uint code, string name, string description) => new()
        {
            code = code,
            type = YgoConstants.TYPE_TRAP | YgoConstants.TYPE_CONTINUOUS,
            name = name,
            desc = description,
        };

        internal static CardInfo StoryCard(uint code) => code switch
        {
            SpeedWorld => SpeedWorldCard,
            AngelBaton => AngelBatonCard,
            HandcuffToken => HandcuffTokenCard,
            ActionField => ActionFieldCard,
            ActionEvasion => ActionEvasionCard,
            ActionMiracle => ActionMiracleCard,
            >= DarknessZero and <= 511090105 => DarknessTraps[code - DarknessZero],
            _ => null,
        };

        internal static bool IsRidingDuel(string gimmick) => gimmick?.StartsWith("riding_", StringComparison.Ordinal) == true;

        private const uint Attack = YgoConstants.POS_FACEUP_ATTACK;
        private const uint Defense = YgoConstants.POS_FACEUP_DEFENSE;
        private static readonly Dictionary<string, (uint Code, uint Position)[]> OpeningCards = new(StringComparer.Ordinal)
        {
            ["insect_forest"] = new[] { (87430998u, Attack), (89091579u, Attack) },
            ["ocean_ambush"] = new[] { (22702055u, Attack), (19089195u, Attack), (23771716u, Attack) },
            ["undead_renewal"] = new[] { (20277860u, Attack) },
            ["labyrinth"] = new[] { (67284908u, Defense) },
            ["darkness_curtain"] = new[] { (62121u, YgoConstants.POS_FACEDOWN_DEFENSE) },
            ["machine_immunity"] = new[] { (3797883u, Attack) },
            ["jinzo"] = new[] { (77585513u, Attack) },
            ["slifer_draw"] = new[] { (10000020u, Attack) },
            ["tribute_mask"] = new[] { (29549364u, Attack) },
            ["dark_sanctuary"] = new[] { (16625614u, Attack) },
            ["egyptian_gods"] = new[] { (10000020u, Attack), (10000010u, Attack) },
            ["ancient_gear"] = new[] { (37694547u, Attack), (83104731u, Attack) },
            ["helios_alchemy"] = new[] { (54493213u, Attack), (30241314u, Attack) },
            ["vampire_pact"] = new[] { (53839837u, Attack) },
            ["amazoness_colosseum"] = new[] { (712559u, Attack), (15951532u, Attack) },
            ["sacred_beasts"] = new[] { (6007213u, Attack), (32491822u, Attack) },
            ["plasma"] = new[] { (83965310u, Attack) },
            ["venom_counters"] = new[] { (54306223u, Attack), (22499463u, Attack) },
            ["fossil_fusion"] = new[] { (96897184u, Attack) },
            ["possessed_beasts"] = new[] { (6007213u, Attack), (32491822u, Attack), (69890967u, Attack) },
            ["graduation_duel"] = new[] { (42015635u, Attack) },
            ["reactor_barrage"] = new[] { (89493368u, Attack) },
            ["black_rose_thorns"] = new[] { (73580471u, Attack) },
            ["monkey_prison"] = new[] { (22858242u, Defense) },
            ["riding_ccapac_apu"] = new[] { (46263076u, Attack) },
            ["riding_fortune_lady"] = new[] { (70252926u, Attack) },
            ["riding_petrification"] = new[] { (89810518u, Attack) },
            ["riding_wisel"] = new[] { (68140974u, Attack) },
            ["riding_unicorn"] = new[] { (77506119u, Attack) },
            ["riding_odin"] = new[] { (93483212u, Attack) },
            ["riding_granel"] = new[] { (4545683u, Attack) },
            ["riding_delta_accel"] = new[] { (98558751u, Attack), (62560742u, Defense), (24943456u, Attack) },
            ["riding_mashinicle"] = new[] { (63468625u, Attack) },
            ["riding_timelords"] = new[] { (7733560u, Defense) },
            ["riding_stardust_finale"] = new[] { (50091196u, Defense) },
            ["numbers_protection"] = new[] { (31801517u, Attack) },
            ["chronomaly_colosseum"] = new[] { (87430304u, Attack), (5291803u, Defense) },
            ["gimmick_puppet_show"] = new[] { (88120966u, Attack) },
            ["barian_hand"] = new[] { (95929069u, Defense) },
            ["star_seraph_xyz"] = new[] { (38331564u, Attack), (91110378u, Attack), (70668285u, Attack) },
            ["barian_chaos"] = new[] { (2061963u, Attack) },
            ["boxing_ring"] = new[] { (59627393u, Attack) },
            ["tachyon_rewind"] = new[] { (88177324u, Attack) },
            ["dark_mist"] = new[] { (55727845u, Attack) },
            ["ark_knight_material"] = new[] { (48739166u, Attack) },
            ["gorgonic_ghost"] = new[] { (84401683u, Attack) },
            ["numeron_network"] = new[] { (41418852u, Attack) },
            ["hundred_numbers"] = new[] { (84013237u, Attack) },
            ["hope_roots"] = new[] { (84124261u, Attack) },
            ["action_opening"] = new[] { (16178681u, Attack) },
            ["showmanship"] = new[] { (25629622u, Attack) },
            ["frightfur_circus"] = new[] { (78778375u, Attack) },
            ["action_ddd"] = new[] { (48210156u, Attack), (12822541u, Attack), (19808608u, Attack), (46796664u, Attack) },
            ["raidraptor_resistance"] = new[] { (73887236u, Attack) },
            ["action_speedroid"] = new[] { (16725505u, Attack), (81275020u, Attack), (53932291u, Attack) },
            ["superheavy_defense"] = new[] { (3117804u, Defense) },
            ["action_dark_rebellion"] = new[] { (16195942u, Attack) },
            ["lunalight_dance"] = new[] { (51777272u, Attack) },
            ["windwitch_barrier"] = new[] { (14577226u, Attack) },
            ["action_king"] = new[] { (16172067u, Attack) },
            ["predator_counters"] = new[] { (69946549u, Attack) },
            ["destiny_hero_return"] = new[] { (60461804u, Attack) },
            ["starving_venom"] = new[] { (41209827u, Attack) },
            ["ddd_final_trial"] = new[] { (72181263u, Attack), (12822541u, Attack) },
            ["four_dragons"] = new[] { (96733134u, Attack) },
            ["trickstar_poison"] = new[] { (35371948u, Attack) },
            ["marincess_ocean"] = new[] { (91027843u, Attack) },
            ["ignister_arrival"] = new[] { (59054773u, Attack) },
        };

        internal static IReadOnlyList<(uint Code, uint Position)> StartingCards(string gimmick) =>
            gimmick != null && OpeningCards.TryGetValue(gimmick, out var cards) ? cards : Array.Empty<(uint, uint)>();

        internal StoryGimmickRuntime(string gimmick)
        {
            CrisisSkill = gimmick switch
            {
                "barian_chaos" or "numeronius" or "barian_emperors" => StoryCrisisSkill.ShiningDraw,
                "borreload_storm" or "mirror_link" or "borreload_final" => StoryCrisisSkill.StormAccess,
                _ => StoryCrisisSkill.None,
            };
        }

        internal StoryCrisisSkill CrisisSkill { get; }
        private bool _crisisUsed;

        internal bool CanActivateCrisisSkill(int currentLp) =>
            CrisisSkill != StoryCrisisSkill.None && !_crisisUsed && currentLp is > 0 and <= 2000;

        internal void RecordCrisisUse() => _crisisUsed = true;
    }
}
