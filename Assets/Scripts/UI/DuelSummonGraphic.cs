using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed partial class DuelEffectGraphic
    {
        internal bool SummonSignature;
        internal bool SummonFinale;
        internal int SummonUnits;
        internal float OrbLight = 1f;

        private static readonly Color VortexAmber = new Color(1f, .50f, .19f);
        private static readonly Color LinkRed = new Color(1f, .24f, .18f);

        internal static bool HasSummonSignature(DuelEffectKind kind) =>
            kind is DuelEffectKind.Synchro or DuelEffectKind.Xyz or DuelEffectKind.Fusion or DuelEffectKind.Link;

        private void DrawSummonSignature(VertexHelper mesh, Rect rect, float radius, float time, float glow)
        {
            if (SummonFinale)
            {
                DrawSummonLanding(mesh, rect.center, radius, time, glow);
                return;
            }
            switch (Kind)
            {
                case DuelEffectKind.Synchro:
                    DrawSynchroCorridor(mesh, rect, radius, time, glow);
                    break;
                case DuelEffectKind.Xyz:
                    DrawXyzGate(mesh, rect.center, radius, time, glow);
                    break;
                case DuelEffectKind.Fusion:
                    DrawFusionVortex(mesh, rect, time, glow);
                    break;
                case DuelEffectKind.Link:
                    DrawLinkCircuit(mesh, rect.center, radius, time, glow);
                    break;
            }
        }

        private void DrawSynchroCorridor(VertexHelper mesh, Rect rect, float radius, float time, float glow)
        {
            var center = rect.center;
            float beam = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.60f, .94f, time));
            var floor = center - Vector2.up * radius * .82f;
            var ceiling = center + Vector2.up * radius * .92f;
            Glow(mesh, center, radius * .70f, 1.12f, Shade(Tint, glow * .38f));
            Line(mesh, floor, ceiling, radius * .48f, Shade(Tint, glow * beam * .26f));
            Line(mesh, floor, ceiling, radius * .105f, Shade(Color.white, glow * beam * .66f));
            for (int i = 0; i < SummonUnits; i++)
            {
                float depth = (i + .5f) / SummonUnits;
                float delay = .18f + depth * .30f;
                float form = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(delay, delay + .24f, time));
                var point = center + Vector2.up * (radius * Mathf.Lerp(-.69f, .57f, depth));
                float size = radius * Mathf.Lerp(.64f, .43f, depth) * Mathf.Lerp(.55f, 1f, form);
                var ink = Color.Lerp(Tint, Color.white, depth * .55f);
                Ring(mesh, point, size, .26f, radius * .025f, 0f, 1f, Shade(ink, glow * .33f * form), 32);
                Ring(mesh, point, size, .26f, 2.2f, 0f, 1f, Shade(ink, glow * form), 32);
                Ring(mesh, point, size * 1.11f, .26f, 1.4f, time + depth, .28f,
                    Shade(Color.white, glow * .64f * form), 14);
            }
        }

        private void DrawXyzGate(VertexHelper mesh, Vector2 center, float radius, float time, float glow)
        {
            var core = new Color(.023f, .011f, .063f);
            var indigo = new Color(.35f, .29f, .95f);
            Glow(mesh, center, radius * 1.18f, .69f, Shade(indigo, glow * .61f));
            for (int i = 0; i < 3; i++)
                DrawSpiral(mesh, center, new Vector2(radius * 1.21f, radius * .65f), time * .74f + i / 3f,
                    .81f, i == 2 ? 1.8f : 3f, Shade(i == 2 ? DuelStyle.Gold : Tint, glow * .82f), 36);
            Glow(mesh, center, radius * .39f, .61f, Shade(core, glow * .98f));
            for (int i = 0; i < 18; i++)
            {
                float range = radius * (.48f + (i % 7) * .108f);
                var point = OnEllipse(center, range, .65f, i * .618f + time * .43f);
                float opacity = glow * (.29f + .21f * Mathf.Sin(i + time * 8f));
                Glow(mesh, point, radius * .026f, 1f, Shade(Color.white, opacity));
            }
        }

        private void DrawFusionVortex(VertexHelper mesh, Rect rect, float time, float glow)
        {
            var reach = new Vector2(rect.width * .55f, rect.height * .47f);
            var center = rect.center;
            var inlet = new Color(.35f, .46f, 1f);
            Glow(mesh, center, reach.y * 1.14f, .81f, Shade(Tint, glow * .42f));
            for (int i = 0; i < 4; i++)
            {
                float turn = time * .79f + i * .25f;
                Color ink = i switch { 0 => inlet, 2 => VortexAmber, _ => Tint };
                DrawSpiral(mesh, center, reach, turn, .92f + i * .042f,
                    4.6f - i * .57f, Shade(ink, glow * .84f), 44);
            }
            float convergence = Mathf.InverseLerp(.66f, 1f, time);
            Glow(mesh, center, reach.y * (.13f + convergence * .25f), .82f,
                Shade(Color.Lerp(inlet, Color.white, convergence), glow * (.53f + convergence * .38f)));
        }

        internal static uint LitLinkMarkers(uint directionMask, float time)
        {
            uint lit = 0;
            int order = 0;
            foreach (var mark in DuelLinkCompass.Marks)
            {
                if ((directionMask & mark.mask) == 0) continue;
                if (time >= .18f + order * .045f) lit |= mark.mask;
                order++;
            }
            return lit;
        }

        private void DrawLinkCircuit(VertexHelper mesh, Vector2 center, float radius, float time, float glow)
        {
            float form = Mathf.SmoothStep(.44f, 1f, time * 4.6f);
            var baseInk = Shade(Tint, glow * .62f);
            Glow(mesh, center, radius * form, .72f, Shade(Tint, glow * .49f));
            Ring(mesh, center, radius * .89f * form, .76f, 3.8f, .0625f, 1f, baseInk, 8);
            Ring(mesh, center, radius * .61f * form, .76f, 2.2f, .0625f, 1f, baseInk, 8);
            uint lit = LitLinkMarkers(DirectionMask, Mathf.InverseLerp(.48f, .96f, time));

            foreach (var mark in DuelLinkCompass.Marks)
            {
                var angle = ((Vector2)mark.direction).normalized;
                var dir = new Vector2(angle.x, angle.y * .76f);
                var tangent = new Vector2(-dir.y, dir.x).normalized;
                var edge = center + dir * (radius * .93f * form);
                bool active = (lit & mark.mask) != 0;
                var ink = active ? LinkRed : Tint;
                var contact = center + dir * (radius * .50f * form);
                Line(mesh, contact, edge, active ? 4.2f : 2.2f, Shade(ink, glow * (active ? .98f : .39f)));
                var basePoint = edge - dir * (radius * .13f);
                float width = radius * .075f;
                Triangle(mesh, edge, basePoint + tangent * width, basePoint - tangent * width,
                    Shade(ink, glow * (active ? 1f : .38f)));
                if (active)
                {
                    Triangle(mesh, edge, basePoint + tangent * width * .33f, basePoint - tangent * width * .33f,
                        Shade(DuelStyle.Gold, glow));
                    Glow(mesh, contact, radius * .13f, 1f, Shade(DuelStyle.Gold, glow * .76f));
                }
            }
        }

        private void DrawSummonLanding(VertexHelper mesh, Vector2 center, float radius, float time, float glow)
        {
            float travel = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.28f, .94f, time));
            float core = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(time - .48f) / .24f);
            Glow(mesh, center, radius * .87f, 1f, Shade(Tint, glow * .32f));
            Glow(mesh, center, radius * (.45f + core * .34f), .88f, Shade(Color.white, glow * core * .63f));
            var floor = center - Vector2.up * radius * .37f;
            float opening = radius * Mathf.Lerp(.38f, 1.07f, travel);
            int edges = Kind == DuelEffectKind.Link ? 8 : 40;
            Ring(mesh, floor, opening, .38f, 2f, Kind == DuelEffectKind.Link ? .0625f : 0f, 1f,
                Shade(Tint, glow * (1f - travel) * .74f), edges);
            if (Kind == DuelEffectKind.Link)
            {
                foreach (var mark in DuelLinkCompass.Marks)
                {
                    if ((DirectionMask & mark.mask) == 0) continue;
                    var direction = ((Vector2)mark.direction).normalized;
                    var point = floor + new Vector2(direction.x, direction.y * .38f) * opening;
                    Glow(mesh, point, radius * .048f, 1f, Shade(LinkRed, glow * (1f - travel) * .84f));
                }
            }
            if (Kind != DuelEffectKind.Xyz) return;
            for (int i = 0; i < SummonUnits; i++)
            {
                float turn = i / (float)SummonUnits + time * .81f;
                var point = OnEllipse(floor, opening, .43f, turn);
                var tail = OnEllipse(floor, opening, .43f, turn - .053f);
                Line(mesh, tail, point, radius * .014f, Shade(DuelStyle.Gold, glow * .70f));
                DrawStar(mesh, point, radius * .049f, Shade(DuelStyle.Gold, glow));
            }
        }

        private void DrawSummonOrb(VertexHelper mesh, Rect rect, float radius, float glow)
        {
            glow *= OrbLight;
            if (glow <= 0f) return;
            if (SummonUnits <= 1)
            {
                DrawStar(mesh, rect.center, radius, Shade(Tint, glow));
                return;
            }
            for (int i = 0; i < SummonUnits; i++)
            {
                float spacing = (i - (SummonUnits - 1) * .5f) * radius * .53f;
                var center = rect.center + new Vector2(Mathf.Sin(i * 2.4f) * radius * .36f, spacing);
                DrawStar(mesh, center, radius * .45f, Shade(Tint, glow));
            }
        }

        private static void DrawStar(VertexHelper mesh, Vector2 point, float radius, Color ink)
        {
            Glow(mesh, point, radius * 2.0f, 1f, ink);
            Glow(mesh, point, radius * .71f, 1f, Shade(Color.white, ink.a));
            Triangle(mesh, point + Vector2.up * radius * 1.65f,
                point + new Vector2(-radius * .40f, -radius * .29f),
                point + new Vector2(radius * .40f, -radius * .29f), Shade(Color.white, ink.a * .88f));
            Triangle(mesh, point - Vector2.up * radius * 1.65f,
                point + new Vector2(-radius * .40f, radius * .29f),
                point + new Vector2(radius * .40f, radius * .29f), Shade(Color.white, ink.a * .88f));
            Line(mesh, point - Vector2.right * radius * 1.08f, point + Vector2.right * radius * 1.08f,
                radius * .22f, Shade(Color.white, ink.a * .78f));
        }

        private static void DrawSpiral(VertexHelper mesh, Vector2 center, Vector2 reach, float turn, float twist,
            float width, Color ink, int segments)
        {
            var previous = center;
            for (int i = 1; i <= segments; i++)
            {
                float along = (float)i / segments;
                float angle = (turn + twist * along) * (Mathf.PI * 2f);
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var point = center + Vector2.Scale(reach, direction) * Mathf.Pow(along, 1.28f);
                float thickness = width * Mathf.Lerp(.44f, 1.45f, along);
                Line(mesh, previous, point, thickness * 3f, Shade(ink, .22f));
                Line(mesh, previous, point, thickness, Shade(ink, 1f - along * .54f));
                previous = point;
            }
        }
    }
}
