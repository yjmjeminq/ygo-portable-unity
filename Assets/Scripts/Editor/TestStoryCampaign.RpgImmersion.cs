using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static partial class TestStoryCampaign
    {
        private static void VerifyRpgTerrainData(string streaming)
        {
            var gx = TagForceData.LoadRpgConfiguration(streaming, StoryModeUI.GxRpgSeriesId);
            Require(gx.spots.Length == 12 && gx.fieldScenes.Length == 2,
                "A GX campaign or prologue background omitted its terrain data.");
            var path = new List<Vector2>(24);
            foreach (var spot in gx.spots.Concat(gx.fieldScenes))
            {
                var terrain = new RpgFieldTerrain(spot);
                Require(spot.walkPolygon.All(terrain.Contains) &&
                    spot.nearScale is >= 1.12f and <= 1.20f && spot.farScale is >= .74f and <= .80f,
                    $"{spot.spotId} has invalid boundary vertices or perspective endpoints.");
                float previous = float.PositiveInfinity;
                for (int i = 0; i <= 100; i++)
                {
                    float scale = terrain.ScaleAt(i * .01f);
                    Require(scale <= previous, $"The perspective curve reverses in {spot.spotId}.");
                    previous = scale;
                }
                var from = terrain.Ground(new Vector2(.51f, .23f));
                for (int x = 0; x <= 5; x++)
                for (int y = 0; y <= 5; y++)
                {
                    var target = terrain.Ground(new Vector2(x * .20f, y * .20f));
                    Require(terrain.Contains(target), $"Ground projection left the floor of {spot.spotId}.");
                    Require(terrain.Contains(terrain.Slide(from, new Vector2(x * .20f, y * .20f))),
                        $"Manual sliding left the floor of {spot.spotId}.");
                    terrain.BuildPath(from, target, path);
                    var step = from;
                    foreach (var point in path)
                    {
                        Require(terrain.IsClear(step, point), $"A click route cut through scenery at {spot.spotId}.");
                        step = point;
                    }
                    Require(Vector2.Distance(step, target) < .0001f, $"A grounded destination was unreachable in {spot.spotId}.");
                }
            }

            var room = new RpgFieldTerrain(new TagForceSpot
            {
                walkPolygon = new Vector2[] { new(.1f, .1f), new(.9f, .1f), new(.9f, .9f), new(.1f, .9f) },
                obstacles = new[] { new TagForceGroundObstacle
                {
                    vertices = new Vector2[] { new(.4f, .4f), new(.6f, .4f), new(.6f, .6f), new(.4f, .6f) },
                } },
            });
            var start = new Vector2(.2f, .5f);
            var end = new Vector2(.8f, .5f);
            room.BuildPath(start, end, path);
            Require(path.Count >= 3 && path[^1] == end && !room.IsClear(start, end),
                "Clicking past an obstacle did not take the visible route around it.");
            var slideStart = new Vector2(.35f, .43f);
            var slideTarget = new Vector2(.5f, .53f);
            var slid = room.Slide(slideStart, slideTarget);
            Require(Mathf.Abs(slid.x - .4f) < .0001f && Mathf.Abs(slid.y - slideTarget.y) < .0001f,
                "A diagonal input did not preserve its motion along an obstacle wall.");
            room.BuildPath(start, end, path);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
            {
                room.Slide(slideStart, slideTarget);
                room.BuildPath(start, end, path);
            }
            Require(GC.GetAllocatedBytesForCurrentThread() == before, "Steady-state terrain movement allocated managed memory.");
            var footsteps = Enum.GetValues(typeof(RpgSurface)).Cast<RpgSurface>();
            Require(footsteps.Select(surface => DuelAudio.GetRpgFootstep(surface, -1)).Distinct().Count() == 6 &&
                footsteps.All(surface => DuelAudio.GetRpgFootstep(surface, -1) != DuelAudio.GetRpgFootstep(surface, 1)),
                "Floor materials or alternating feet shared the same footstep sound.");
            Debug.Log("[VERIFY GX TERRAIN] 12 fields and 2 prologue stages, perspective, obstacle routes, wall slide, allocation-free geometry and six step materials passed.");
        }
    }
}
