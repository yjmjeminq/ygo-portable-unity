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
            Glow(mesh, center, radius * .71f, 1.12f, Shade(Tint, glow * .50f));
            for (int i = 0; i < SummonUnits; i++)
            {
                float depth = (i + .5f) / SummonUnits;
                float form = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(i * .038f, .38f + i * .014f, time));
                var point = center + Vector2.up * (radius * Mathf.Lerp(-.69f, .53f, depth) * form);
                float size = radius * Mathf.Lerp(.54f, .33f, depth) * form;
                var ink = Color.Lerp(Tint, Color.white, depth * .55f);
                Ring(mesh, point, size, .27f, 2.2f + 2f * form, 0f, 1f, Shade(ink, glow * form), 32);
                Ring(mesh, point, size * 1.15f, .27f, 1.8f, time + depth, .28f,
                    Shade(Color.white, glow * .53f * form), 14);
            }
            if (!SummonFinale) return;
            float beam = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(time - .54f) / .25f);
            var bottom = center - Vector2.up * rect.height * .54f;
            var top = center + Vector2.up * rect.height * .61f;
            Line(mesh, bottom, top, radius * .68f * beam, Shade(Tint, glow * beam * .28f));
            Line(mesh, bottom, top, radius * .23f * beam, Shade(Color.white, glow * beam * .95f));
            Ring(mesh, center, radius * Mathf.Lerp(.30f, 1.20f, Mathf.InverseLerp(.45f, .84f, time)), .39f,
                3.8f, 0f, 1f, Shade(Color.white, glow * beam * .81f), 40);
        }

        private void DrawXyzGate(VertexHelper mesh, Vector2 center, float radius, float time, float glow)
        {
            var core = new Color(.023f, .011f, .063f);
            var indigo = new Color(.35f, .29f, .95f);
            Glow(mesh, center, radius * 1.24f, .74f, Shade(indigo, glow * .66f));
            Glow(mesh, center, radius * .71f, .68f, Shade(core, glow));
            for (int i = 0; i < 3; i++)
                DrawSpiral(mesh, center, new Vector2(radius, radius * .70f), time * -.91f + i / 3f,
                    .95f, 3.5f, Shade(i == 2 ? DuelStyle.Gold : Tint, glow), 30);

            if (!SummonFinale) return;
            float emerged = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.44f, .61f, time));
            for (int i = 0; i < SummonUnits; i++)
            {
                float turn = i / (float)SummonUnits + time * .84f;
                var orbit = center - Vector2.up * radius * .19f;
                float range = radius * Mathf.Lerp(.32f, .82f, emerged);
                var point = OnEllipse(orbit, range, .47f, turn);
                var tail = OnEllipse(orbit, range, .47f, turn - .061f);
                float opacity = glow * emerged;
                Line(mesh, tail, point, radius * .019f, Shade(DuelStyle.Gold, opacity * .75f));
                DrawStar(mesh, point, radius * .057f, Shade(DuelStyle.Gold, opacity));
            }
        }

        private void DrawFusionVortex(VertexHelper mesh, Rect rect, float time, float glow)
        {
            var reach = rect.size * .66f;
            var center = rect.center;
            Glow(mesh, center, reach.y * .77f, 1f, Shade(Tint, glow * .63f));
            for (int i = 0; i < 4; i++)
            {
                float turn = time * (i % 2 == 0 ? .85f : -.72f) + i * .25f;
                Color ink = i % 2 == 0 ? Tint : VortexAmber;
                DrawSpiral(mesh, center, reach, turn, i % 2 == 0 ? 1.14f : -1.08f,
                    6.2f - i * .70f, Shade(ink, glow), 44);
            }
            if (SummonFinale)
                Glow(mesh, center, reach.y * (.17f + time * .62f), .81f,
                    Shade(Color.white, glow * Mathf.Clamp01(1f - 4.6f * Mathf.Abs(time - .54f))));
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
            uint lit = LitLinkMarkers(DirectionMask, time);

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
            if (SummonFinale)
            {
                float pulse = Mathf.InverseLerp(.49f, .84f, time);
                Ring(mesh, center, radius * Mathf.Lerp(.39f, 1.35f, pulse), .76f, 5.2f * (1f - pulse) + 1f,
                    .0625f, 1f, Shade(DuelStyle.Gold, glow * Mathf.Sin(pulse * Mathf.PI)), 8);
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
