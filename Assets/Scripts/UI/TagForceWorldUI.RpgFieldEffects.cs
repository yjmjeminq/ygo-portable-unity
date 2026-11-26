using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal enum RpgSurface { Soil, Sand, Wood, Stone, Rock, Metal }

    internal sealed partial class TagForceWorldUI
    {
        private const int RpgFootfallCapacity = 36;
        private const float RpgForegroundTravel = .18f;
        private static readonly Dictionary<string, Sprite> RpgFringeSprites = new();
        private static Sprite _rpgRipple;
        private readonly RpgFootfall[] _rpgFootfalls = new RpgFootfall[RpgFootfallCapacity];
        private readonly Image[] _rpgFrontMotes = new Image[9];
        private RectTransform _rpgFootfallLayer, _rpgForeground;
        private Image _rpgLeftFringe, _rpgRightFringe, _rpgLightRays;
        private int _rpgNextFootfall;
        private float _rpgFootfallSoundGap;

        private struct RpgFootfall
        {
            internal Image Art;
            internal Vector2 Start, Drift, Size;
            internal Color Color;
            internal float Time, Duration;
        }

        private RpgSurface SurfaceAt(Vector2 position)
        {
            return _rpgTerrain.Spot.surfaceType switch
            {
                "soil" => RpgSurface.Soil,
                "sand" when position.x >= .522f && position.y > .375f - (position.x - .522f) * .70f => RpgSurface.Wood,
                "sand" => RpgSurface.Sand,
                "wood" => RpgSurface.Wood,
                "rock" => RpgSurface.Rock,
                "metal" => RpgSurface.Metal,
                _ => RpgSurface.Stone,
            };
        }

        private Color RpgActorLight(Vector2 position)
        {
            Color ambient = _rpgTerrain.Spot.fieldLight switch
            {
                "moon" => new Color32(220, 231, 255, 255),
                "embers" => new Color32(255, 224, 212, 255),
                "violet" => new Color32(232, 216, 245, 255),
                "warm" => new Color32(255, 229, 202, 255),
                _ => new Color32(255, 248, 235, 255),
            };
            Color tint = Color.Lerp(Color.white, ambient, .17f);
            if (_rpgTerrain.Spot.fieldLight is "moon" or "warm")
            {
                var lamp = _rpgTerrain.Spot.spotId == "spot_vending_bench" ? new Vector2(.60f, .48f) :
                    _rpgAtmosphereStyle.LightPosition;
                float glow = 1f - Mathf.Clamp01(RpgArtDirection(position - lamp).magnitude / .25f);
                tint = Color.Lerp(tint, new Color32(255, 202, 148, 255), glow * .13f);
            }
            return tint;
        }

        private void BuildRpgFieldEffects()
        {
            if (_rpgFootfallLayer == null)
            {
                _rpgFootfallLayer = DuelUiKit.FillRect("Footfalls", _rpgSceneSpace);
                for (int i = 0; i < _rpgFootfalls.Length; i++)
                {
                    var art = Anchor(_rpgFootfallLayer, "Footfall", Vector2.zero, Vector2.zero);
                    _rpgFootfalls[i].Art = DuelUiKit.AddImage(art.gameObject, RpgMote, Color.clear, false, false);
                    art.gameObject.SetActive(false);
                }
            }
            else
            {
                _rpgFootfallLayer.SetParent(_rpgSceneSpace, false);
                _rpgFootfallLayer.SetAsLastSibling();
                _rpgFootfallLayer.gameObject.SetActive(true);
            }

            _rpgForeground = DuelUiKit.FillRect("FieldForeground", _rpgSceneSpace);
            _rpgLeftFringe = DuelUiKit.AddImage(Area(_rpgForeground, "LeftSilhouette", -.025f, -.028f, .25f, .28f).gameObject,
                null, Color.clear, false, false);
            _rpgRightFringe = DuelUiKit.AddImage(Area(_rpgForeground, "RightSilhouette", .75f, -.028f, 1.025f, .28f).gameObject,
                null, Color.clear, false, false);
            _rpgRightFringe.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            var ray = Area(_rpgForeground, "LightRays", .50f, .24f, .94f, 1.1f);
            ray.localRotation = Quaternion.Euler(0f, 0f, -27f);
            _rpgLightRays = DuelUiKit.AddImage(ray.gameObject, RpgSoftLight, Color.clear, false, false);
            for (int i = 0; i < _rpgFrontMotes.Length; i++)
                _rpgFrontMotes[i] = DuelUiKit.AddImage(Anchor(_rpgForeground, "NearMote", Vector2.zero, Vector2.one * (4 + i)).gameObject,
                    RpgMote, Color.clear, false, false);
            _rpgRipple ??= CreateRpgRipple();
            RefreshRpgForeground();
            ApplyRpgForegroundParallax(RpgCameraSpan());
        }

        private void RefreshRpgForeground()
        {
            var surface = SurfaceAt(Vector2.zero);
            DuelAudio.GetRpgFootstep(surface, -1);
            DuelAudio.GetRpgFootstep(surface, 1);
            if (surface == RpgSurface.Sand)
            {
                DuelAudio.GetRpgFootstep(RpgSurface.Wood, -1);
                DuelAudio.GetRpgFootstep(RpgSurface.Wood, 1);
            }
            string id = _rpgTerrain.Spot.spotId;
            string shape = id switch
            {
                "spot_library" => "shelves",
                "spot_harbor_beach" => "palms",
                "spot_forest_duel_ground" or "spot_duel_academia" or "spot_red_dorm" or "spot_vending_bench" => "leaves",
                "spot_dark_dimension" or "spot_darkness_altar" or "spot_old_moment" => "crystals",
                "spot_volcano_arena" or "spot_crash_town" or "spot_abandoned_dorm" => "rocks",
                _ => "posts",
            };
            if (!RpgFringeSprites.TryGetValue(shape, out var fringe))
                RpgFringeSprites.Add(shape, fringe = CreateRpgFringe(shape));
            _rpgLeftFringe.sprite = _rpgRightFringe.sprite = fringe;
            Color color = _rpgTerrain.Spot.fieldLight switch
            {
                "moon" => new Color(.045f, .079f, .13f, .39f),
                "embers" => new Color(.20f, .064f, .046f, .37f),
                "violet" => new Color(.105f, .050f, .17f, .41f),
                _ => new Color(.077f, .088f, .068f, .33f),
            };
            _rpgLeftFringe.color = _rpgRightFringe.color = color;
            _rpgLightRays.enabled = _rpgTerrain.Spot.fieldLight == "day" &&
                id is "spot_harbor_beach" or "spot_infirmary" or "spot_library" or "spot_duel_academia";
        }

        private static Sprite CreateRpgFringe(string shape)
        {
            const int size = 96;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + .5f) / size, (y + .5f) / size);
                float outline = shape switch
                {
                    "leaves" => RpgFrond(p, new(.04f, .07f), new(.53f, .88f), .13f,
                        new(.80f, .54f), .14f, new(.98f, .28f), .12f),
                    "palms" => RpgFrond(p, new(.02f, .16f), new(.41f, 1.02f), .08f,
                        new(.78f, .86f), .095f, new(.97f, .62f), .09f),
                    "shelves" => Mathf.Max(Mathf.Min(.21f - p.x, .87f - p.y),
                        Mathf.Min(.60f - p.x - p.y * .18f, .26f - p.y)),
                    "crystals" => Mathf.Max(Mathf.Min(.88f - p.y, .14f - Mathf.Abs(p.x - (.11f + .35f * p.y)) - .10f * p.y),
                        Mathf.Min(.56f - p.y, .13f - Mathf.Abs(p.x - (.47f + .35f * p.y)) - .13f * p.y)),
                    "rocks" => Mathf.Max(.30f - p.x * .28f - p.y, .44f - Mathf.Abs(p.x + .35f * p.y) - p.y * .68f),
                    _ => Mathf.Max(.12f - p.y - p.x * .10f, Mathf.Min(.14f - p.x, .57f - p.y)),
                };
                float edge = Mathf.SmoothStep(0f, 1f, (.038f + outline) / .063f);
                float fade = (1f - p.x * .74f) * (1f - p.y * .38f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, edge * fade);
            }
            return DuelUiChrome.CreateSprite("Field foreground " + shape, size, size, pixels);
        }

        private static float RpgFrond(Vector2 position, Vector2 start, Vector2 a, float aw, Vector2 b, float bw,
            Vector2 c, float cw)
        {
            float Leaf(Vector2 tip, float width)
            {
                var line = tip - start;
                float t = Vector2.Dot(position - start, line) / line.sqrMagnitude;
                return t is > 0f and < 1f
                    ? width * Mathf.Sin(t * Mathf.PI) - (position - start - t * line).magnitude
                    : -.10f;
            }
            return Mathf.Max(Leaf(a, aw), Mathf.Max(Leaf(b, bw), Leaf(c, cw)));
        }

        private void ApplyRpgForegroundParallax(Vector2 span)
        {
            if (_rpgForeground == null) return;
            var shift = new Vector2(_rpgCameraPan.x / span.x, _rpgCameraPan.y / span.y) * RpgForegroundTravel;
            _rpgForeground.anchorMin = shift;
            _rpgForeground.anchorMax = Vector2.one + shift;
        }

        private void EmitRpgFootfall(RectTransform actor, Vector2 position, int foot)
        {
            if (_rpgFootfallLayer == null || !_rpgFootfallLayer.gameObject.activeSelf) return;
            var surface = SurfaceAt(position);
            string light = _rpgTerrain.Spot.fieldLight;
            bool waterline = surface == RpgSurface.Sand && position.x > .465f && position.y > .329f;
            bool hot = light == "embers";
            bool dark = light == "violet" && surface == RpgSurface.Rock;
            Vector2 at = _rpgTerrain.Ground(position + Vector2.right * (foot * .009f * actor.localScale.x));
            Color tint = surface switch
            {
                RpgSurface.Soil => new Color(.67f, .53f, .35f, .45f),
                RpgSurface.Sand => new Color(.97f, .84f, .60f, .51f),
                RpgSurface.Rock when hot => new Color(1f, .46f, .17f, .72f),
                RpgSurface.Rock when dark => new Color(.78f, .46f, 1f, .59f),
                _ => new Color(.84f, .93f, 1f, .28f),
            };
            int count = surface is RpgSurface.Soil or RpgSurface.Sand or RpgSurface.Rock ? 3 : 1;
            for (int i = 0; i < count; i++)
            {
                bool ripple = i == 0 && (waterline || count == 1);
                ref var particle = ref _rpgFootfalls[_rpgNextFootfall];
                _rpgNextFootfall = (_rpgNextFootfall + 1) % _rpgFootfalls.Length;
                particle.Start = at;
                particle.Drift = ripple ? Vector2.zero : new Vector2((i - 1) * .018f + foot * .006f, hot ? .061f : .017f);
                particle.Duration = ripple ? .41f : .30f + .065f * i;
                particle.Time = 0f;
                bool leaf = surface == RpgSurface.Soil && i == 2;
                particle.Color = leaf ? new Color(.59f, .60f, .32f, .52f) : ripple && waterline ?
                    new Color(.71f, .93f, 1f, .47f) : tint;
                particle.Size = new Vector2(ripple ? 28f : leaf ? 12f : 14f, ripple ? 13f : leaf ? 6f : 12f) * actor.localScale.x;
                particle.Art.sprite = ripple ? _rpgRipple ??= CreateRpgRipple() : RpgMote;
                particle.Art.rectTransform.localRotation = Quaternion.Euler(0f, 0f, leaf ? 34f * foot : 0f);
                PoseRpgFootfall(ref particle);
                particle.Art.gameObject.SetActive(true);
            }
            if (actor == _player || _rpgFootfallSoundGap <= 0f &&
                RpgArtDirection(position - _player.anchorMin).sqrMagnitude < .15f * .15f)
            {
                DuelAudio.PlayRpgFootstep(surface, foot, actor == _player ? .52f : .24f);
                _rpgFootfallSoundGap = .09f;
            }
        }

        private static Sprite CreateRpgRipple()
        {
            const int size = 32;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var point = new Vector2((x + .5f) / size, (y + .5f) / size) * 2f - Vector2.one;
                float opacity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(point.magnitude - .68f) / .24f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, opacity);
            }
            return DuelUiChrome.CreateSprite("Field contact ring", size, size, pixels);
        }

        private static void PoseRpgFootfall(ref RpgFootfall particle)
        {
            float t = particle.Time / particle.Duration;
            var rect = particle.Art.rectTransform;
            rect.anchorMin = rect.anchorMax = particle.Start + particle.Drift * t;
            rect.sizeDelta = particle.Size * Mathf.Lerp(.68f, 1.75f, t);
            Color color = particle.Color;
            color.a *= (1f - t) * (1f - t);
            particle.Art.color = color;
        }

        private void UpdateRpgFieldEffects(float delta)
        {
            if (_rpgForeground == null) return;
            _rpgFootfallSoundGap -= delta;
            for (int i = 0; i < _rpgFootfalls.Length; i++)
            {
                ref var particle = ref _rpgFootfalls[i];
                if (!particle.Art.gameObject.activeSelf) continue;
                particle.Time += delta;
                if (particle.Time >= particle.Duration) particle.Art.gameObject.SetActive(false);
                else PoseRpgFootfall(ref particle);
            }
            string light = _rpgTerrain.Spot.fieldLight;
            for (int i = 0; i < _rpgFrontMotes.Length; i++)
            {
                var image = _rpgFrontMotes[i];
                float t = Mathf.Repeat(_rpgAtmosphereTime * (.12f + i * .007f) + .147f * i, 1f);
                var point = new Vector2(.07f + .105f * i + .035f * Mathf.Sin(t * 5f + i),
                    .08f + t * (light == "embers" ? .51f : .29f));
                if (_rpgTerrain.Spot.surfaceType == "sand")
                    point = new Vector2(.50f + .051f * i, .46f + .037f * Mathf.Sin(t * 6.28f + i));
                image.rectTransform.anchorMin = image.rectTransform.anchorMax = point;
                var color = light switch
                {
                    "embers" => new Color(1f, .48f, .20f, .64f),
                    "violet" => new Color(.72f, .55f, .98f, .46f),
                    "moon" => new Color(.64f, .80f, 1f, .38f),
                    _ => new Color(1f, .92f, .73f, .37f),
                };
                color.a *= Mathf.Sin(t * Mathf.PI);
                image.color = color;
            }
            _rpgLightRays.color = new Color(1f, .94f, .76f, .14f + .035f * Mathf.Sin(_rpgAtmosphereTime * .72f));
        }

        private void ClearRpgFieldEffects()
        {
            _rpgForeground = null;
            if (_rpgFootfallLayer == null) return;
            foreach (var particle in _rpgFootfalls) particle.Art.gameObject.SetActive(false);
            _rpgFootfallLayer.gameObject.SetActive(false);
            _rpgFootfallLayer.SetParent(Root.transform, false);
            _rpgNextFootfall = 0;
        }
    }
}
