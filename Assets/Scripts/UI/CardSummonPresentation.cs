using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class CardAnimationManager
    {
        private static Vector2 SummonShowcaseCenter(Vector2 view) => new Vector2(view.x * .035f, view.y * .045f);

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
            var portal = CreateSignatureEffect(kind, SummonShowcaseCenter(view), view, accent, finale: false);
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
            Vector2 origin = move.Flight.From.AtLayout().Position;
            float travel = Mathf.InverseLerp(.12f + Mathf.Min(index, 6) * .018f, .90f, time);
            Vector2 point;
            switch (portal.Kind)
            {
                case DuelEffectKind.Xyz:
                    point = center + RotateSummonVector(origin - center, travel * (5.7f + index * .31f)) *
                        Mathf.Pow(1f - travel, 1.3f);
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
            orb.OrbLight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.81f, 1f, time));
            orb.Seek(time);
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
            Vector2 spiral = RotateSummonVector(offset, pull * (index % 2 == 0 ? 4.9f : -4.9f)) *
                Mathf.Pow(1f - pull, 1.12f);
            rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, center + spiral, Mathf.SmoothStep(0f, 1f, pull * 4f));
            rect.localRotation *= Quaternion.Euler(0f, 0f, pull * (index % 2 == 0 ? -104f : 104f));
            rect.localScale *= Mathf.Lerp(1f, .22f, pull);
        }

        private Image SummonScreenTint(string name) =>
            LotdUiKit.AddImage(LotdUiKit.FillRect(name, _layer).gameObject, LotdUiKit.WhiteSprite, Color.clear, false, false);

        private void PoseSummonPeak(Image flash, Text circuitClosed, float progress, float canvasHeight)
        {
            float shock = Mathf.InverseLerp(.48f, .67f, progress);
            float pulse = Mathf.Sin(shock * Mathf.PI) * (1f - shock);
            _layer.anchoredPosition = new Vector2(Mathf.Sin(shock * 29f), Mathf.Sin(shock * 41f)) * (canvasHeight * .020f * pulse);
            float white = Mathf.Sin(Mathf.InverseLerp(.49f, .64f, progress) * Mathf.PI);
            flash.color = new Color(.92f, .97f, 1f, Mathf.Max(0f, white) * .33f);
            if (circuitClosed != null)
            {
                Color ink = DuelStyle.Gold;
                ink.a = Mathf.Sin(Mathf.InverseLerp(.43f, .82f, progress) * Mathf.PI);
                circuitClosed.color = ink;
            }
        }
    }
}
