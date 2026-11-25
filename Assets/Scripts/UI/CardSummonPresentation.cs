using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class CardAnimationManager
    {
        private static Vector2 SummonShowcaseCenter(Vector2 view) => new Vector2(view.x * .035f, view.y * .045f);

        private static Vector2 MaterialStageSize(Vector2 view) =>
            new Vector2(Mathf.Min(view.x * .76f, view.y * 1.36f), view.y * .76f);

        private static DuelEffectKind? MaterialSignature(uint method) => method switch
        {
            YgoConstants.REASON_SYNCHRO => DuelEffectKind.Synchro,
            YgoConstants.REASON_XYZ => DuelEffectKind.Xyz,
            YgoConstants.REASON_FUSION => DuelEffectKind.Fusion,
            YgoConstants.REASON_LINK => DuelEffectKind.Link,
            _ => null,
        };

        // Cap the decorative geometry for effects that can produce unusually high levels or material counts.
        private static int LimitSummonUnits(int count) => Mathf.Clamp(count, 0, 16);

        private static uint SummonedLinkMarkers(uint code, LiveCardState live) =>
            live is { linkMarker: not 0 } ? live.linkMarker : CardDatabase.Get(code)?.link ?? 0;

        private static (bool Tuner, int Level) MaterialRating(SummonMaterialFlight move)
        {
            var flight = move.Flight;
            if (flight.StartsFaceDown || flight.Code == 0) return (false, 0);
            var live = move.Readout?.SourceCard;
            var card = CardDatabase.Get(flight.Code);
            uint type = live?.type ?? card?.type ?? 0;
            uint level = live?.level ?? card?.level ?? 0;
            return ((type & YgoConstants.TYPE_TUNER) != 0, (int)System.Math.Min(level, 16u));
        }

        private static int SummonTunerLevels(IReadOnlyList<SummonMaterialFlight> moves)
        {
            int levels = 0;
            foreach (var move in moves)
            {
                var rating = MaterialRating(move);
                if (rating.Tuner) levels += rating.Level;
            }
            return LimitSummonUnits(levels);
        }

        private DuelEffectGraphic CreateSignatureEffect(DuelEffectKind kind, Vector2 center, Vector2 view, Color tint,
            bool finale)
        {
            // These meshes supply their own color and motion at both stages.
            var holder = CenteredVisual(kind.ToString(), _layer, center, view);
            var graphic = holder.gameObject.AddComponent<DuelEffectGraphic>();
            graphic.texture = Texture2D.whiteTexture;
            graphic.raycastTarget = false;
            graphic.Kind = kind;
            graphic.Tint = tint;
            graphic.SummonSignature = true;
            graphic.SummonFinale = finale;
            return graphic;
        }

        private DuelEffectGraphic CreateMaterialPortal(IReadOnlyList<SummonMaterialFlight> moves, DuelEffectKind kind,
            Color accent, uint linkMarkers)
        {
            var view = _layer.rect.size;
            var portal = CreateSignatureEffect(kind, SummonShowcaseCenter(view), MaterialStageSize(view), accent, finale: false);
            if (kind == DuelEffectKind.Synchro) portal.SummonUnits = SummonTunerLevels(moves);
            if (kind == DuelEffectKind.Link) portal.DirectionMask = linkMarkers;
            return portal;
        }

        private DuelEffectGraphic CreateMaterialOrb(SummonMaterialFlight move, DuelEffectKind kind, Color accent)
        {
            if (kind == DuelEffectKind.Fusion) return null;
            int units = 1;
            if (kind == DuelEffectKind.Synchro)
            {
                var rating = MaterialRating(move);
                if (rating.Tuner || rating.Level == 0) return null;
                units = rating.Level;
            }
            float size = Mathf.Clamp(move.Flight.From.AtLayout().Size.y * .34f, 19f, 39f);
            var tint = kind == DuelEffectKind.Xyz ? new Color(1f, .83f, .26f) : Color.Lerp(accent, Color.white, .31f);
            var orb = CreateEffect(DuelEffectKind.SummonOrb, move.Flight.From.AtLayout().Position, Vector2.one * size, tint);
            orb.SummonUnits = units;
            return orb;
        }

        private static void PoseMaterialOrb(DuelEffectGraphic orb, DuelEffectGraphic portal, SummonMaterialFlight move,
            int index, float time)
        {
            Vector2 center = portal.rectTransform.anchoredPosition;
            float radius = portal.rectTransform.rect.height * .46f;
            Vector2 origin = MaterialIntake(move.Flight, portal, index);
            float travel = Mathf.InverseLerp(.37f + Mathf.Min(index, 6) * .013f, .96f, time);
            Vector2 point;
            switch (portal.Kind)
            {
                case DuelEffectKind.Xyz:
                    var entry = origin - center;
                    var orbit = RotateSummonVector(new Vector2(entry.x, entry.y / .65f), travel * (5.1f + index * .23f));
                    point = center + new Vector2(orbit.x, orbit.y * .65f) * Mathf.Pow(1f - travel, 1.2f);
                    break;
                case DuelEffectKind.Synchro:
                    var lower = center - Vector2.up * radius * .89f;
                    var upper = center + Vector2.up * radius * .75f;
                    point = travel < .37f
                        ? Vector2.Lerp(origin, lower, Mathf.SmoothStep(0f, 1f, travel / .37f))
                        : Vector2.Lerp(lower, upper, Mathf.Pow(Mathf.InverseLerp(.37f, 1f, travel), 1.6f));
                    break;
                default:
                    Vector2 target = LinkMaterialTarget(portal.DirectionMask, index, center, radius);
                    point = Vector2.Lerp(origin, target, Mathf.SmoothStep(0f, 1f, travel)) +
                        Vector2.up * Mathf.Sin(travel * Mathf.PI) * radius * .22f;
                    break;
            }
            orb.rectTransform.anchoredPosition = point;
            orb.rectTransform.localScale = Vector3.one * Mathf.Lerp(.72f, 1.12f, travel);
            float formed = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.35f, .58f, time));
            float absorbed = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.86f, 1f, time));
            orb.OrbLight = formed * (1f - absorbed);
            orb.Seek(time);
        }

        private static Vector2 MaterialIntake(CardFlight flight, DuelEffectGraphic portal, int index)
        {
            var center = portal.rectTransform.anchoredPosition;
            float radius = portal.rectTransform.rect.height * .46f;
            Vector2 offset = flight.From.AtLayout().Position - center;
            Vector2 intake = portal.Kind == DuelEffectKind.Synchro
                ? new Vector2(Mathf.Sin(index * 2.4f) * .10f, -.64f) * radius
                : offset.normalized * (radius * .42f);
            return center + Vector2.ClampMagnitude(intake, offset.magnitude * .44f);
        }

        private static void ApproachMaterialPortal(CompositeCardView card, CardFlight flight, DuelEffectGraphic portal,
            int index, float time)
        {
            var from = flight.From.AtLayout();
            float travel = Mathf.Clamp01(time / (.62f + Mathf.Min(index, 6) * .012f));
            var intake = new CardPose(MaterialIntake(flight, portal, index), from.Size * .34f);
            ApplyFlight(card, new CardFlight(flight.Code, from, intake, flight.StartsFaceDown, flight.StartsFaceDown),
                travel, 26f);
        }

        private static Vector2 LinkMaterialTarget(uint mask, int index, Vector2 center, float radius)
        {
            var marks = DuelLinkCompass.Marks;
            int remaining = index;
            foreach (var mark in marks)
            {
                if ((mask & mark.mask) == 0) continue;
                Vector2 direction = ((Vector2)mark.direction).normalized;
                if (remaining == 0) return center + new Vector2(direction.x, direction.y * .76f) * radius * .50f;
                remaining--;
            }
            return center;
        }

        private static Vector2 RotateSummonVector(Vector2 vector, float angle)
        {
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            return new Vector2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos);
        }

        private static void PullFusionCard(CompositeCardView view, CardFlight flight, Vector2 center, int index, float time)
        {
            float pull = Mathf.InverseLerp(.20f, .94f, time);
            if (pull == 0f) return;
            var rect = (RectTransform)view.transform;
            var offset = flight.From.AtLayout().Position - center;
            Vector2 spiral = RotateSummonVector(offset, pull * (3.9f + Mathf.Min(index, 5) * .22f)) *
                Mathf.Pow(1f - pull, 1.12f);
            rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, center + spiral, Mathf.SmoothStep(0f, 1f, pull * 4f));
            rect.localRotation *= Quaternion.Euler(0f, 0f, -72f * pull);
            rect.localScale *= Mathf.Lerp(1f, .22f, pull);
        }

        private Image SummonScreenTint(string name) =>
            DuelUiKit.AddImage(DuelUiKit.FillRect(name, _layer).gameObject, DuelUiKit.WhiteSprite, Color.clear, false, false);

        private static Color SummonPortraitAccent(uint attribute, Color fallback) => attribute switch
        {
            YgoConstants.ATTRIBUTE_EARTH => new Color(.91f, .64f, .40f),
            YgoConstants.ATTRIBUTE_WATER => new Color(.31f, .79f, 1f),
            YgoConstants.ATTRIBUTE_FIRE => new Color(1f, .40f, .21f),
            YgoConstants.ATTRIBUTE_WIND => new Color(.36f, .91f, .63f),
            YgoConstants.ATTRIBUTE_LIGHT => new Color(1f, .87f, .54f),
            YgoConstants.ATTRIBUTE_DARK => new Color(.75f, .55f, 1f),
            YgoConstants.ATTRIBUTE_DIVINE => new Color(1f, .76f, .38f),
            _ => fallback,
        };

        private static CanvasGroup CreateSummonFraming(RectTransform parent, Color accent)
        {
            var framing = DuelUiKit.FillRect("SummonFraming", parent);
            var visibility = framing.gameObject.AddComponent<CanvasGroup>();
            visibility.interactable = visibility.blocksRaycasts = false;
            visibility.alpha = 0f;
            Color shade = new Color(.016f, .020f, .038f, .98f);
            var top = DuelUiKit.CreateRect("Top", framing, new Vector2(0f, .952f), Vector2.one,
                Vector2.zero, Vector2.zero);
            var bottom = DuelUiKit.CreateRect("Bottom", framing, Vector2.zero, new Vector2(1f, .057f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddImage(top.gameObject, DuelUiKit.WhiteSprite, shade, false, false);
            AddBandBackground(bottom, null, Color.Lerp(shade, accent, .40f));
            return visibility;
        }

        private SummonVideoOverlay _summonVideo;

        private static string FindSummonVideo(CardFlight flight, CardMotion motion)
        {
            if (flight.EndsFaceDown || motion != CardMotion.ExtraDeckSummon || !SummonVideoOverlay.CanRender) return null;
            return DuelVisualAssets.FindSummonVideo(SummonEffect(flight.Code, motion).kind);
        }

        private static bool HasSummonVideo(IReadOnlyList<FieldArrival> moves)
        {
            foreach (var move in moves)
                if (FindSummonVideo(move.Flight, move.Motion) != null) return true;
            return false;
        }

        private System.Collections.IEnumerator PlayVideoArrivalGroup(IReadOnlyList<FieldArrival> moves, System.Action refresh)
        {
            foreach (var move in moves)
            {
                yield return MoveCard(move.Flight, move.Motion, () => { move.Release(); refresh(); },
                    move.Readout, move.Depart, move.Handoff, move.SignatureUnits, move.LandingImpact, move.WideCutIn);
            }
        }

        private SummonVideoOverlay StartSummonVideo() =>
            _summonVideo = DuelUiKit.FillRect("SummonVideoOverlay", _layer).gameObject.AddComponent<SummonVideoOverlay>();

        private void StopSummonVideo()
        {
            var overlay = _summonVideo;
            _summonVideo = null;
            if (overlay == null) return;
            overlay.ReleasePlayback();
            RemoveTransient(overlay.gameObject);
        }

        private System.Collections.IEnumerator LandVideoSummon(CardFlight flight, CardMotion motion, LiveCardState live,
            bool showCutIn)
        {
            Vector2 area = _layer.rect.size;
            float height = Mathf.Min(area.y * .30f, 290f);
            var start = new CardPose(SummonShowcaseCenter(area), new Vector2(height * DuelUiKit.CardAspectRatio, height));
            var landing = new CardFlight(flight.Code, start, flight.To, false, flight.EndsFaceDown, flight.Appearance);
            var proxy = PrepareFlightCard(landing);
            var cinematic = showCutIn ? CreateWideCutIn(flight, motion, live) : null;
            if (cinematic != null) yield return PlayWideCutIn(cinematic, proxy, flight, motion, live);
            else
            {
                if (!IsSpecialEditionLanding(flight)) DuelAudio.PlaySummon(motion);
                yield return Animate(.20f, progress => ApplyFlight(proxy, landing, progress, 8f, live));
            }
            RecycleCard(proxy);
        }

    }
}
