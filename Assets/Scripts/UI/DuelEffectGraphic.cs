using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal enum DuelEffectKind
    {
        AttackPath, Strike, Break, Token, Summon, Chain, Fusion, Synchro, Xyz, Link, Ritual, Pendulum, XyzMaterial,
        SummonMaterial, BanishTransit
    }

    // Canvas particles and tracks use one mesh, with white fill and vertex tints.
    internal sealed class DuelEffectGraphic : RawImage
    {
        internal DuelEffectKind Kind;
        internal Color Tint;
        private float _progress;
        private Image _texture;

        internal void ApplyTexture(Sprite sprite)
        {
            if (sprite == null) return;
            var layer = LotdUiKit.FillRect("Particles", transform);
            _texture = LotdUiKit.AddImage(layer.gameObject, sprite, new Color(1f, 1f, 1f, 0f), true, false);
        }

        internal void Seek(float progress)
        {
            if (Mathf.Approximately(progress, _progress)) return;
            _progress = progress;
            if (_texture != null)
            {
                float opacity = Mathf.Clamp01(Mathf.Min(progress * 7f, (1f - progress) * 3.2f));
                _texture.color = new Color(1f, 1f, 1f, opacity * .84f);
                _texture.rectTransform.localScale = Vector3.one * Mathf.Lerp(.66f, 1.22f, progress);
            }
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = rectTransform.rect;
            float radius = Mathf.Min(rect.width, rect.height) * .46f;
            if (radius < 1f) return;

            float t = _progress;
            float glow = Mathf.SmoothStep(0f, 1f, Mathf.Min(t * 6f, (1f - t) * 4.2f));
            if (glow <= 0f) return;
            float travel = 1f - (1f - t) * (1f - t);
            if (Kind == DuelEffectKind.AttackPath)
            {
                AttackPath(mesh, rect, t, glow);
                return;
            }

            switch (Kind)
            {
                case DuelEffectKind.Strike:
                    Strike(mesh, rect.center, radius, travel, glow);
                    break;
                case DuelEffectKind.Break:
                    Glow(mesh, rect.center, radius * (.48f + .92f * t), .83f, Shade(Tint, glow * .55f));
                    Sparks(mesh, rect.center, radius * 1.12f, travel, glow, 23, false);
                    break;
                case DuelEffectKind.Token:
                    Glow(mesh, rect.center, radius * .82f, .74f, Shade(Tint, glow * .62f));
                    for (int i = 0; i < 3; i++)
                    {
                        float v = Mathf.Repeat(t * .92f + i / 3f, 1f);
                        var point = rect.center + Vector2.up * (v - .26f) * radius * .64f;
                        Ring(mesh, point, radius * (.27f + v * .49f), .51f, 2.6f, 0f, 1f,
                            Shade(Color.Lerp(Tint, Color.white, .42f), glow * (1f - v)), 48);
                    }
                    Sparks(mesh, rect.center, radius * .74f, travel, glow, 22, true);
                    break;
                case DuelEffectKind.Summon:
                    Glow(mesh, rect.center, radius * (.32f + .52f * t), .83f, Shade(Tint, glow * .58f));
                    Ring(mesh, rect.center, radius * (.32f + .57f * travel), .64f, 3.6f, 0f, 1f, Shade(Tint, glow), 48);
                    Sparks(mesh, rect.center, radius * .91f, travel, glow, 21, true);
                    break;
                case DuelEffectKind.Chain:
                    Glow(mesh, rect.center, radius * 1.19f, 1.06f, Shade(Tint, glow * .67f));
                    for (int i = 0; i < 4; i++)
                        Ring(mesh, rect.center, radius * (.65f + .07f * i), 1.18f, (i + 1) * 1.4f, i / 4f + t * .35f,
                            .15f, Shade(Color.Lerp(Tint, Color.white, .37f), glow), 44);
                    Sparks(mesh, rect.center, radius, travel, glow, 22, true);
                    break;
                case DuelEffectKind.XyzMaterial:
                    Glow(mesh, rect.center, radius * .69f, .48f, Shade(Tint, glow * .22f));
                    Ring(mesh, rect.center, radius * (.48f + .31f * travel), .43f, 2.3f, t * .69f, .48f,
                        Shade(Tint, glow * .67f), 36);
                    Ring(mesh, rect.center, radius * (.62f + .24f * travel), .46f, 1.3f, .50f + t * .69f, .42f,
                        Shade(new Color(.85f, .76f, 1f), glow * .68f), 36);
                    break;
                case DuelEffectKind.SummonMaterial:
                    Glow(mesh, rect.center, radius * .77f, .68f, Shade(Tint, glow * .31f));
                    for (int i = 0; i < 4; i++)
                        Ring(mesh, rect.center, radius * (.82f + .12f * travel), .73f, 2.6f,
                            .125f + i * .25f + t * .09f, .14f, Shade(Tint, glow * .88f), 40);
                    break;
                case DuelEffectKind.BanishTransit:
                    Glow(mesh, rect.center, radius * .9f, .7f, Shade(Tint, glow * .32f));
                    Ring(mesh, rect.center, radius * (.38f + .46f * travel), .82f, 2.8f,
                        t * .52f, .77f, Shade(Tint, glow), 46);
                    Ring(mesh, rect.center, radius * (.81f - .48f * travel), .82f, 1.5f,
                        -.31f * t, .69f, Shade(Color.Lerp(Tint, Color.white, .35f), glow * .72f), 38);
                    break;
                default:
                    SummoningPattern(mesh, rect.center, radius, t, glow);
                    break;
            }
        }

        private void AttackPath(VertexHelper mesh, Rect rect, float t, float glow)
        {
            float y = rect.center.y;
            var from = new Vector2(rect.xMin, y);
            var to = new Vector2(rect.xMax, y);
            float width = Mathf.Clamp(rect.height * .20f, 3f, 15f);
            float travel = Mathf.SmoothStep(0f, 1f, t * 1.65f);
            Vector2 head = Vector2.Lerp(from, to, travel);
            float length = Mathf.Min(width * 2.6f, rect.width * travel);
            var neck = head - Vector2.right * (length * .66f);
            var light = Color.Lerp(Tint, Color.white, .72f);
            Line(mesh, from, neck, width * 1.5f, Shade(Tint, glow * .24f));
            Line(mesh, from, neck, width * .55f, Shade(Tint, glow * .93f));
            Line(mesh, from, neck, width * .15f, Shade(light, glow));
            Triangle(mesh, head, neck + new Vector2(-length * .34f, width), neck, Shade(Tint, glow));
            Triangle(mesh, head, neck, neck + new Vector2(-length * .34f, -width), Shade(Tint, glow));
            Triangle(mesh, head, neck + Vector2.up * width * .37f, neck - Vector2.up * width * .37f, Shade(light, glow));
            for (int i = 1; i <= 3; i++)
            {
                float x = head.x - i * width * 2.5f;
                if (x < from.x + width) break;
                var point = new Vector2(x, y);
                var color = Shade(light, glow * (.65f - i * .13f));
                Line(mesh, point + new Vector2(-width * .65f, width * .48f), point, width * .17f, color);
                Line(mesh, point, point + new Vector2(-width * .65f, -width * .48f), width * .17f, color);
            }
        }

        private void Strike(VertexHelper mesh, Vector2 center, float radius, float travel, float glow)
        {
            var white = Color.Lerp(Tint, Color.white, .84f);
            Glow(mesh, center, radius * (.41f + .59f * travel), .65f, Shade(Tint, glow * .72f));
            Glow(mesh, center, radius * (.29f + .47f * travel), .34f, Shade(white, glow * .89f));
            float flash = glow * Mathf.Clamp01((1f - travel) * 1.8f);
            Triangle(mesh, center - Vector2.right * radius, center + Vector2.right * radius,
                center + Vector2.up * radius * .14f, Shade(white, flash));
            Triangle(mesh, center + Vector2.right * radius, center - Vector2.right * radius,
                center - Vector2.up * radius * .14f, Shade(white, flash));
            Ring(mesh, center, radius * (.22f + .77f * travel), .73f, 5.2f * (1f - travel) + 1f,
                .18f + .12f * travel, .80f, Shade(Tint, glow), 48);
            Ring(mesh, center + new Vector2(radius * .09f, radius * .04f), radius * (.16f + .61f * travel),
                .78f, 2.7f, .59f, .69f, Shade(white, glow), 48);
            Sparks(mesh, center, radius, travel, glow, 24, false);
        }

        private void SummoningPattern(VertexHelper mesh, Vector2 center, float radius, float t, float glow)
        {
            Glow(mesh, center, radius * (.80f + .24f * t), 1f, Shade(Tint, glow * .54f));
            var pale = Color.Lerp(Tint, Color.white, .62f);
            switch (Kind)
            {
                case DuelEffectKind.Fusion:
                    var orange = new Color(1f, .54f, .28f);
                    Ring(mesh, center - Vector2.right * radius * .16f, radius * .80f, .80f, 8f, t + .04f, .74f, Shade(Tint, glow), 48);
                    Ring(mesh, center + Vector2.right * radius * .16f, radius * .80f, .80f, 5.6f, .48f - t * .93f, .63f, Shade(orange, glow), 48);
                    break;
                case DuelEffectKind.Synchro:
                    for (int i = 0; i < 4; i++)
                    {
                        float ring = Mathf.Repeat(t * 1.12f + i * .24f, 1f);
                        float r = radius * (.31f + .64f * ring);
                        Ring(mesh, center, r, .77f, 1.6f + 5f * (1f - ring), t * .25f, 1f,
                            Shade(i % 2 == 0 ? pale : Tint, glow * (1f - ring * .58f)), 60);
                    }
                    break;
                case DuelEffectKind.Xyz:
                    Glow(mesh, center, radius * .64f, .84f, Shade(new Color(.025f, .012f, .074f), glow * .94f));
                    for (int i = 0; i < 3; i++)
                        Ring(mesh, center, radius * (.45f + i * .22f), .76f, 4.2f, t * .9f + i * .31f, .52f,
                            Shade(i == 2 ? DuelStyle.Gold : Tint, glow), 48);
                    break;
                case DuelEffectKind.Link:
                    Ring(mesh, center, radius * (.77f + .13f * t), .88f, 5f, .166f * t, 1f, Shade(Tint, glow), 6);
                    Ring(mesh, center, radius * .69f, .88f, 2f, -.12f * t, 1f, Shade(pale, glow), 6);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = (i + t * .6f) * Mathf.PI * .25f;
                        var outward = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Line(mesh, center + outward * radius * .83f, center + outward * radius, 3.2f, Shade(pale, glow));
                    }
                    break;
                case DuelEffectKind.Ritual:
                    Ring(mesh, center, radius * .97f, .88f, 2.4f, .1f - t * .1f, 1f, Shade(pale, glow), 52);
                    Ring(mesh, center, radius * .86f, .88f, 4.6f, t * .22f, 1f, Shade(Tint, glow), 5);
                    Ring(mesh, center, radius * .77f, .88f, 2.5f, .1f - t * .17f, 1f, Shade(Tint, glow), 5);
                    break;
                case DuelEffectKind.Pendulum:
                    Ring(mesh, center - Vector2.right * radius * .23f, radius * .72f, .79f, 6.4f, t * .72f, .77f, Shade(Tint, glow), 52);
                    Ring(mesh, center + Vector2.right * radius * .23f, radius * .72f, .79f, 6.4f, -t * .7f, .77f,
                        Shade(new Color(.46f, 1f, .60f), glow), 52);
                    break;
            }
            Sparks(mesh, center, radius * 1.07f, t, glow, 34, Kind != DuelEffectKind.Xyz);
        }

        private void Sparks(VertexHelper mesh, Vector2 center, float radius, float t, float glow, int count, bool rising)
        {
            const float goldenAngle = 2.399963f;
            for (int i = 0; i < count; i++)
            {
                float variation = 0.29f + ((i * 29) % 41) / 59f;
                float angle = i * goldenAngle + (rising ? t * .32f : 0f);
                var travel = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * .78f);
                var point = center + travel * (radius * variation * (.15f + t * 1.03f));
                if (rising) point.y += radius * (.06f + t * .52f) * variation;
                float length = 3f + radius * (1f - t) * .10f * variation;
                Color tint = Shade(i % 3 == 0 ? Color.white : Tint, glow * (1f - t * .63f));
                Line(mesh, point - travel * length, point, 1.4f + variation * 2.2f, tint);
            }
        }

        private static Color Shade(Color color, float alpha)
        {
            color.a *= Mathf.Clamp01(alpha);
            return color;
        }

        private static Vector2 OnEllipse(Vector2 center, float radius, float flatten, float turns)
        {
            float a = turns * (Mathf.PI * 2f);
            return center + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * flatten);
        }

        private static void Ring(VertexHelper mesh, Vector2 center, float radius, float flatten, float width,
            float startTurn, float fraction, Color color, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = startTurn + fraction * i / segments;
                float b = startTurn + fraction * (i + 1) / segments;
                var from = OnEllipse(center, radius, flatten, a);
                var to = OnEllipse(center, radius, flatten, b);
                Line(mesh, from, to, width * 2.9f, Shade(color, .19f));
                Line(mesh, from, to, width, color);
            }
        }

        private static void Glow(VertexHelper mesh, Vector2 center, float radius, float flatten, Color tint)
        {
            const int steps = 40;
            int index = mesh.currentVertCount;
            mesh.AddVert(center, tint, Vector2.zero);
            var edge = Shade(tint, 0f);
            for (int i = 0; i < steps; i++)
                mesh.AddVert(OnEllipse(center, radius, flatten, (float)i / steps), edge, Vector2.zero);
            for (int i = 0; i < steps; i++)
                mesh.AddTriangle(index, index + i + 1, index + (i + 1) % steps + 1);
        }

        private static void Line(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color color)
        {
            var direction = (to - from).normalized;
            var normal = new Vector2(-direction.y, direction.x) * width * .5f;
            DuelMat.AddQuad(mesh, from - normal, from + normal, to + normal, to - normal,
                color, color, Vector2.zero, Vector2.zero);
        }

        private static void Triangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero);
            mesh.AddVert(b, color, Vector2.zero);
            mesh.AddVert(c, color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
        }
    }
}
