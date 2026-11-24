using System;
using System.Collections.Generic;

namespace YGOPortable.Core
{
    internal enum StoryCrisisSkill { None, ShiningDraw, StormAccess }

    internal sealed class StoryGimmickRuntime
    {
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
