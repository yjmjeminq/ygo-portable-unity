using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private enum RpgResidentRole { Conversation, Story, Request, Delivery, Reward, Shop }

        // Each lane stays in front of the painted structures and away from the story trigger and exits.
        private static readonly Vector2[] RpgLeftPatrol =
        {
            new(-.060f, -.040f), new(-.017f, -.115f), new(.048f, -.063f), Vector2.zero,
        };
        private static readonly Vector2[] RpgRightPatrol =
        {
            new(.044f, -.055f), new(.009f, -.140f), new(-.045f, -.057f), Vector2.zero,
        };
        private static readonly Vector2[] RpgRequestPatrol =
        {
            new(.028f, -.023f), new(.039f, .015f), Vector2.zero,
        };

        private sealed class RpgResidentMotion
        {
            internal RectTransform Actor, Art, Marker;
            internal CanvasGroup MarkerVisibility;
            internal RpgResidentRole Role;
            internal Vector2 Home, Facing;
            internal Vector2[] Patrol;
            internal int Stop;
            internal float Wait, Speed, Phase;
        }

        private readonly List<RpgResidentMotion> _rpgResidentMotion = new();
        private RectTransform _rpgApproachActor;
        private static Sprite _rpgSpeechBubble;
        private float _rpgMarkerTime;

        private void BuildRpgResidentMotion(RectTransform actor, RectTransform art, Vector2 home,
            RpgResidentRole role, bool patrol)
        {
            var (symbol, tint) = role switch
            {
                RpgResidentRole.Story => ("!", new Color32(255, 220, 128, 255)),
                RpgResidentRole.Request => ("?", new Color32(153, 225, 252, 255)),
                RpgResidentRole.Delivery => ("›", new Color32(166, 214, 255, 255)),
                RpgResidentRole.Reward => ("✓", new Color32(179, 239, 171, 255)),
                RpgResidentRole.Shop => ("▣", new Color32(173, 235, 209, 255)),
                _ => ("…", new Color32(240, 245, 249, 255)),
            };
            bool story = role == RpgResidentRole.Story;
            var marker = Anchor(actor, story ? "MainQuestMarker" : "ResidentMarker", new Vector2(.50f, 1.06f),
                story ? new Vector2(36f, 29f) : new Vector2(31f, 25f));
            DuelUiKit.AddImage(marker.gameObject, _rpgSpeechBubble ??= CreateRpgSpeechBubble(), tint, false, false);
            var glyph = HudLabel(marker, symbol, story ? 21 : 19, new Color(.10f, .15f, .21f),
                align: TextAnchor.MiddleCenter, heading: true);
            glyph.rectTransform.anchorMin = new Vector2(.07f, .23f);
            glyph.rectTransform.anchorMax = new Vector2(.93f, .96f);
            var visibility = marker.gameObject.AddComponent<CanvasGroup>();
            visibility.blocksRaycasts = visibility.interactable = false;
            visibility.alpha = role == RpgResidentRole.Conversation ? 0f : 1f;

            bool left = home.x < .5f;
            bool request = role == RpgResidentRole.Request;
            _rpgResidentMotion.Add(new RpgResidentMotion
            {
                Actor = actor, Art = art, Marker = marker, MarkerVisibility = visibility,
                Role = role, Home = home, Facing = Vector2.right * (_position.x - home.x),
                Patrol = !patrol ? null : request ? RpgRequestPatrol : left ? RpgLeftPatrol : RpgRightPatrol,
                Wait = request ? .72f : left ? .20f : .67f,
                Speed = request ? .056f : left ? .075f : .087f,
                Phase = _rpgResidentMotion.Count * 1.29f,
            });
        }

        private void UpdateRpgResidentMotion(float delta)
        {
            if (_fieldMenu || delta <= 0f) return;
            foreach (var resident in _rpgResidentMotion)
            {
                var position = resident.Actor.anchorMin;
                var toPlayer = _position - position;
                bool nearby = RpgArtDirection(toPlayer).sqrMagnitude <= .122f * .122f;
                if (nearby || resident.Actor == _rpgApproachActor)
                {
                    if (Mathf.Abs(toPlayer.x) >= .015f) resident.Facing = Vector2.right * toPlayer.x;
                    PoseRpgWalker(resident.Actor, resident.Art, position, Vector2.zero, false, resident.Facing);
                    continue;
                }
                if (resident.Patrol == null) continue;

                if (resident.Wait > 0f)
                {
                    resident.Wait = Mathf.Max(0f, resident.Wait - delta);
                    PoseRpgWalker(resident.Actor, resident.Art, position, Vector2.zero, false, resident.Facing);
                    continue;
                }

                var toStop = resident.Home + resident.Patrol[resident.Stop] - position;
                float distance = RpgArtDirection(toStop).magnitude;
                float travel = resident.Speed * delta;
                bool arrived = travel >= distance;
                var step = arrived ? toStop : toStop * (travel / distance);
                if (step.sqrMagnitude > 1e-8f) resident.Facing = step;
                PoseRpgWalker(resident.Actor, resident.Art, position + step, step, !arrived, resident.Facing);
                if (arrived)
                {
                    resident.Stop = (resident.Stop + 1) % resident.Patrol.Length;
                    resident.Wait = resident.Stop == 0 ? 1.36f : .48f + .17f * resident.Stop;
                }
            }
        }

        private void UpdateRpgResidentMarkers(RectTransform focused, float delta)
        {
            _rpgMarkerTime += delta;
            foreach (var resident in _rpgResidentMotion)
            {
                bool focus = resident.Actor == focused;
                bool story = resident.Role == RpgResidentRole.Story;
                bool show = focus || resident.Role != RpgResidentRole.Conversation;
                resident.MarkerVisibility.alpha = Mathf.MoveTowards(resident.MarkerVisibility.alpha,
                    show ? 1f : 0f, 8f * delta);
                float scale = focus ? 1.10f : 1f;
                resident.Marker.localScale = Vector3.one * Mathf.MoveTowards(resident.Marker.localScale.x, scale, 3.5f * delta);
                float lift = story ? 2.5f * Mathf.Sin(_rpgMarkerTime * 3.2f + resident.Phase) : 0f;
                resident.Marker.anchoredPosition = Vector2.up * (lift + (focus ? 2f : 0f));
            }
        }

        private static Sprite CreateRpgSpeechBubble()
        {
            const int width = 64, height = 52;
            var colors = new Color32[width * height];
            Color edge = new(.16f, .22f, .30f);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var p = new Vector2(x + .5f, y + .5f);
                var corner = new Vector2(Mathf.Abs(p.x - 32f) - 22f, Mathf.Abs(p.y - 30f) - 11f);
                float body = Vector2.Max(corner, Vector2.zero).magnitude +
                    Mathf.Min(Mathf.Max(corner.x, corner.y), 0f) - 8f;
                float tail = Mathf.Max(3.5f - p.y, Mathf.Max(p.y - 16f,
                    Mathf.Abs(p.x - 30f - .14f * p.y) - .64f * (p.y - 3f)));
                float silhouette = Mathf.Min(body, tail);
                var color = Color.Lerp(edge, Color.white, Mathf.Clamp01(-silhouette - 1f));
                color.a = Mathf.Clamp01(.5f - silhouette);
                colors[y * width + x] = color;
            }
            return DuelUiChrome.CreateSprite("Field speech bubble", width, height, colors);
        }

        private void ClearRpgResidentMotion()
        {
            _rpgResidentMotion.Clear();
            _rpgApproachActor = null;
            _rpgMarkerTime = 0f;
        }
    }
}
