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

        private static readonly Dictionary<string, (uint Code, bool Defense)[]> OpeningCards = new(StringComparer.Ordinal)
        {
            ["insect_forest"] = new[] { (87430998u, false) },
            ["ocean_ambush"] = new[] { (22702055u, false), (19089195u, false) },
            ["labyrinth"] = new[] { (67284908u, true) },
            ["jinzo"] = new[] { (77585513u, false) },
            ["slifer_draw"] = new[] { (10000020u, false) },
            ["tribute_mask"] = new[] { (29549364u, false) },
            ["egyptian_gods"] = new[] { (10000020u, false), (10000010u, false) },
            ["ancient_gear"] = new[] { (37694547u, false) },
            ["helios_alchemy"] = new[] { (54493213u, false) },
            ["amazoness_colosseum"] = new[] { (712559u, false) },
            ["sacred_beasts"] = new[] { (6007213u, false), (32491822u, false) },
            ["plasma"] = new[] { (83965310u, false) },
            ["possessed_beasts"] = new[] { (6007213u, false), (32491822u, false), (69890967u, false) },
            ["numeron_network"] = new[] { (41418852u, false) },
            ["trickstar_poison"] = new[] { (35371948u, false) },
            ["marincess_ocean"] = new[] { (91027843u, false) },
            ["ignister_arrival"] = new[] { (59054773u, false) },
        };

        internal static IReadOnlyList<(uint Code, bool Defense)> StartingCards(string gimmick) =>
            gimmick != null && OpeningCards.TryGetValue(gimmick, out var cards) ? cards : Array.Empty<(uint, bool)>();

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
