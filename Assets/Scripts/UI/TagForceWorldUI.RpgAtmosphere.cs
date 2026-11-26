using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private readonly struct RpgAtmosphereStyle
        {
            internal readonly Vector2 LightPosition, LightSize, Drift, Height;
            internal readonly Color Light, Particles;
            internal readonly int Count;
            internal readonly bool Streaks;

            internal RpgAtmosphereStyle(Vector2 lightPosition, Vector2 lightSize, Color light,
                Vector2 drift, Vector2 height, Color particles, int count, bool streaks = false)
            {
                (LightPosition, LightSize, Light, Drift, Height, Particles, Count, Streaks) =
                    (lightPosition, lightSize, light, drift, height, particles, count, streaks);
            }
        }

        private struct RpgAtmosphereParticle
        {
            internal Image Image;
            internal Vector2 Origin;
            internal float Phase, Rate, Opacity;
        }

        private static readonly RpgAtmosphereStyle[] RpgAtmospheres =
        {
            // The light positions correspond to the fixtures already painted into each map.
            new(new(.52f, .76f), new(.72f, .52f), new(.84f, .97f, 1f, .13f),
                new(.12f, .11f), new(.16f, .72f), new(.73f, .95f, .96f, .36f), 14),
            new(new(.26f, .61f), new(.53f, .61f), new(.87f, .95f, 1f, .15f),
                new(.095f, .13f), new(.13f, .68f), new(.90f, .97f, 1f, .34f), 16),
            new(new(.22f, .58f), new(.42f, .58f), new(1f, .68f, .35f, .25f),
                new(.37f, .025f), new(.18f, .70f), new(.70f, .83f, 1f, .33f), 14, true),
            new(new(.55f, .59f), new(.46f, .49f), new(1f, .20f, .23f, .24f),
                new(.049f, .087f), new(.24f, .76f), new(.78f, .84f, 1f, .34f), 10),
            new(new(.36f, .60f), new(.53f, .64f), new(.95f, 1f, .88f, .13f),
                new(.105f, .12f), new(.12f, .68f), new(.90f, 1f, .81f, .35f), 15),
            new(new(.49f, .57f), new(.50f, .54f), new(.23f, .81f, 1f, .23f),
                new(.17f, .056f), new(.18f, .69f), new(.49f, .90f, 1f, .36f), 12),
            new(new(.52f, .60f), new(.70f, .66f), new(.65f, .30f, 1f, .25f),
                new(-.13f, .22f), new(.09f, .63f), new(.75f, .56f, 1f, .53f), 18),
            new(new(.48f, .57f), new(.71f, .55f), new(.94f, .82f, .63f, .12f),
                new(.24f, .036f), new(.10f, .60f), new(.86f, .76f, .62f, .38f), 16, true),
            new(new(.34f, .72f), new(.73f, .45f), new(.86f, .96f, 1f, .12f),
                new(.45f, .048f), new(.13f, .69f), new(.86f, .96f, 1f, .30f), 16, true),
            new(new(.76f, .55f), new(.66f, .46f), new(.61f, .86f, 1f, .17f),
                new(.34f, .033f), new(.10f, .63f), new(.70f, .90f, 1f, .32f), 13, true),
            new(new(.69f, .67f), new(.74f, .68f), new(1f, .83f, .42f, .22f),
                new(.19f, .20f), new(.12f, .75f), new(1f, .87f, .52f, .52f), 18),
        };

        private readonly List<RpgAtmosphereParticle> _rpgAtmosphereParticles = new();
        private static Sprite _rpgSoftLight, _rpgMote;
        private Image _rpgAtmosphereLight, _rpgAtmosphereHaze;
        private RpgAtmosphereStyle _rpgAtmosphereStyle;
        private float _rpgAtmosphereTime;

        private static Sprite RpgSoftLight => _rpgSoftLight ??= CreateRpgFalloff("RPG soft light", 48, 2.6f);
        private static Sprite RpgMote => _rpgMote ??= CreateRpgFalloff("RPG atmospheric mote", 18, 1.35f);

        private static Sprite CreateRpgFalloff(string name, int size, float falloff)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var point = new Vector2((x + .5f) / size, (y + .5f) / size) * 2f - Vector2.one;
                float opacity = Mathf.Pow(Mathf.Clamp01(1f - point.sqrMagnitude), falloff);
                pixels[y * size + x] = new Color(1f, 1f, 1f, opacity);
            }
            return DuelUiChrome.CreateSprite(name, size, size, pixels);
        }

        private void BuildRpgAtmosphere()
        {
            _rpgAtmosphereStyle = RpgAtmospheres[_rpgLocation];
            var style = _rpgAtmosphereStyle;
            var layer = DuelUiKit.FillRect("FieldAtmosphere", _rpgSceneSpace);
            var input = layer.gameObject.AddComponent<CanvasGroup>();
            input.blocksRaycasts = input.interactable = false;
            var lower = style.LightPosition - style.LightSize * .5f;
            var upper = style.LightPosition + style.LightSize * .5f;
            var light = Area(layer, "Light", lower.x, lower.y, upper.x, upper.y);
            _rpgAtmosphereLight = DuelUiKit.AddImage(light.gameObject, RpgSoftLight, style.Light, false, false);
            if (_rpgLocation is RpgMoment or RpgBridge or RpgArk)
            {
                bool river = _rpgLocation == RpgBridge;
                float height = river ? .67f : .23f;
                var haze = Area(layer, "Haze", .01f, height - .075f, river ? .56f : .92f, height + .075f);
                var tint = style.Particles;
                tint.a = river ? .17f : .21f;
                _rpgAtmosphereHaze = DuelUiKit.AddImage(haze.gameObject, RpgSoftLight, tint, false, false);
            }
            var random = new System.Random(3329 + _rpgLocation * 71);
            for (int i = 0; i < style.Count; i++)
            {
                float scale = (float)random.NextDouble();
                var origin = new Vector2((float)random.NextDouble(),
                    Mathf.Lerp(style.Height.x, style.Height.y, (float)random.NextDouble()));
                var size = style.Streaks ? new Vector2(15f + 24f * scale, 4f + 2f * scale) :
                    Vector2.one * (5f + 9f * scale);
                var mote = Anchor(layer, $"Mote_{i}", origin, size);
                if (style.Streaks) mote.localRotation = Quaternion.Euler(0f, 0f, 7f);
                _rpgAtmosphereParticles.Add(new RpgAtmosphereParticle
                {
                    Image = DuelUiKit.AddImage(mote.gameObject, RpgMote, style.Particles, false, false),
                    Origin = origin,
                    Phase = (float)random.NextDouble(),
                    Rate = .10f + scale * .11f,
                    Opacity = .48f + scale * .50f,
                });
            }
            _rpgAtmosphereTime = 0f;
            UpdateRpgAtmosphere(0f);
        }

        private void UpdateRpgAtmosphere(float delta)
        {
            if (_rpgAtmosphereLight == null) return;
            _rpgAtmosphereTime += delta;
            var style = _rpgAtmosphereStyle;
            var light = style.Light;
            light.a *= .84f + .16f * Mathf.Sin(_rpgAtmosphereTime * 1.6f);
            _rpgAtmosphereLight.color = light;
            if (_rpgAtmosphereHaze != null)
                _rpgAtmosphereHaze.rectTransform.anchoredPosition = Vector2.right *
                    (_rpgSceneSpace.rect.width * .11f * Mathf.Sin(_rpgAtmosphereTime * .18f));
            foreach (var particle in _rpgAtmosphereParticles)
            {
                float progress = Mathf.Repeat(particle.Phase + _rpgAtmosphereTime * particle.Rate, 1f);
                Vector2 position = particle.Origin + style.Drift * (progress - .5f);
                position.x += _rpgCameraPan.x * .024f;
                particle.Image.rectTransform.anchorMin = particle.Image.rectTransform.anchorMax = position;
                var color = style.Particles;
                color.a *= particle.Opacity * Mathf.Sin(progress * Mathf.PI);
                particle.Image.color = color;
            }
        }

        private static void AddRpgGroundShadow(RectTransform art, float raisedFeet)
        {
            float bottom = art.anchorMin.y + raisedFeet;
            var shadow = Area(art.parent, "GroundShadow", .19f, bottom - .063f, .81f, bottom + .084f);
            shadow.SetSiblingIndex(art.GetSiblingIndex());
            DuelUiKit.AddImage(shadow.gameObject, RpgSoftLight, new Color(.01f, .02f, .035f, .55f), false, false);
        }

        private void ClearRpgAtmosphere()
        {
            _rpgAtmosphereLight = _rpgAtmosphereHaze = null;
            _rpgAtmosphereParticles.Clear();
        }
    }
}
