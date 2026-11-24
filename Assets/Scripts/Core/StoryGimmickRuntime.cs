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

        internal static CardInfo StoryCard(uint code) => code switch
        {
            SpeedWorld => SpeedWorldCard,
            AngelBaton => AngelBatonCard,
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
            ["ancient_gear"] = new[] { (37694547u, Attack) },
            ["helios_alchemy"] = new[] { (54493213u, Attack) },
            ["amazoness_colosseum"] = new[] { (712559u, Attack) },
            ["sacred_beasts"] = new[] { (6007213u, Attack), (32491822u, Attack) },
            ["plasma"] = new[] { (83965310u, Attack) },
            ["possessed_beasts"] = new[] { (6007213u, Attack), (32491822u, Attack), (69890967u, Attack) },
            ["numeron_network"] = new[] { (41418852u, Attack) },
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
