using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DuelFieldUI
    {
        private void HandleCardsTargeted((byte Controller, uint Location, uint Sequence)? source,
            IReadOnlyList<(byte Controller, uint Location, uint Sequence)> targets)
        {
            var animator = CardAnimationManager.Instance;
            if (animator == null || !Root.activeInHierarchy) return;
            CardPose origin = source is { } slot
                ? GetCardPose(slot.Controller, slot.Location, slot.Sequence, YgoConstants.POS_FACEUP_ATTACK)
                : default;
            foreach (var target in targets)
            {
                var view = GetSlot(target.Controller, target.Location, target.Sequence);
                if (view == null) continue;
                animator.QueueTargetLock(source, origin, view.root, () =>
                    view.cardView.gameObject.activeInHierarchy && InspectionLocation(view) == target
                        ? animator.GetVisualPose(view.focusArt) : default);
            }
        }

        private static void SetTargetChoice(FieldSlotView slot, bool available, bool selected)
        {
            if (!available)
            {
                if (slot.targetChoice != null) slot.targetChoice.gameObject.SetActive(false);
                return;
            }
            if (slot.targetChoice == null)
            {
                var outline = DuelUiKit.FillRect("TargetChoice", slot.focusArt, -2.5f);
                slot.targetChoice = outline.gameObject.AddComponent<DuelEffectGraphic>();
                slot.targetChoice.texture = Texture2D.whiteTexture;
                slot.targetChoice.Kind = DuelEffectKind.TargetReticle;
                slot.targetChoice.TargetChoice = true;
                slot.targetChoice.raycastTarget = false;
            }
            var graphic = slot.targetChoice;
            graphic.gameObject.SetActive(true);
            if (graphic.TargetLocked != selected)
            {
                graphic.TargetLocked = selected;
                graphic.canvasRenderer.SetAlpha(1f);
                graphic.SetVerticesDirty();
            }
        }

        private void UpdateTargetChoices()
        {
            if (IsPaused || !_modals.IsFieldTargetSelection) return;
            float pulse = .69f + .26f * Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / 1.36f));
            foreach (var slot in _fieldSlotsByRect.Values)
                if (slot.targetChoice != null && slot.targetChoice.isActiveAndEnabled && !slot.targetChoice.TargetLocked)
                    slot.targetChoice.canvasRenderer.SetAlpha(pulse);
        }
    }

    public partial class CardAnimationManager
    {
        private sealed class TargetLockGroup
        {
            internal (byte, uint, uint)? Source;
            internal CardPose Origin;
            internal readonly Dictionary<RectTransform, Func<CardPose>> Targets = new();
        }

        private TargetLockGroup _openTargetLocks;

        internal void QueueTargetLock((byte, uint, uint)? source, CardPose origin, RectTransform target, Func<CardPose> read)
        {
            if (_openTargetLocks == null || _openTargetLocks.Source != source)
            {
                var group = new TargetLockGroup { Source = source, Origin = origin };
                QueueStep(PlayTargetLocks(group));
                _openTargetLocks = group;
            }
            _openTargetLocks.Targets.TryAdd(target, read);
        }

        private IEnumerator PlayTargetLocks(TargetLockGroup group)
        {
            if (ReferenceEquals(_openTargetLocks, group)) _openTargetLocks = null;
            var effects = new List<(Func<CardPose> Read, DuelEffectGraphic Ring, DuelEffectGraphic Beam)>(group.Targets.Count);
            foreach (var read in group.Targets.Values)
            {
                if (!read().IsValid) continue;
                var marker = CreateEffect(DuelEffectKind.TargetReticle, Vector2.zero, Vector2.one, DuelStyle.Gold);
                marker.TargetLocked = true;
                var beam = group.Origin.IsValid
                    ? CreateEffect(DuelEffectKind.TargetBeam, Vector2.zero, Vector2.one, DuelStyle.Gold) : null;
                effects.Add((read, marker, beam));
            }

            if (effects.Count == 0) yield break;
            yield return Animate(.33f, time =>
            {
                Vector2 origin = group.Origin.AtLayout().Position;
                foreach (var (read, ring, beam) in effects)
                {
                    var pose = read();
                    ring.gameObject.SetActive(pose.IsValid);
                    if (!pose.IsValid)
                    {
                        if (beam != null) beam.gameObject.SetActive(false);
                        continue;
                    }
                    ring.rectTransform.anchoredPosition = pose.Position;
                    ring.rectTransform.sizeDelta = pose.Size + Vector2.one * 7f;
                    ring.rectTransform.localRotation = Quaternion.Euler(0f, 0f, pose.Angle);
                    ring.Seek(time);
                    if (beam == null) continue;
                    Vector2 delta = pose.Position - origin;
                    beam.gameObject.SetActive(delta.sqrMagnitude > 25f);
                    beam.rectTransform.anchoredPosition = origin + delta * .5f;
                    beam.rectTransform.sizeDelta = new Vector2(delta.magnitude, 23f);
                    beam.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                    beam.Seek(time);
                }
            });

            foreach (var (_, ring, beam) in effects)
            {
                RemoveTransient(ring.gameObject);
                if (beam != null) RemoveTransient(beam.gameObject);
            }
        }
    }

    internal sealed partial class DuelEffectGraphic
    {
        internal bool TargetChoice;
        internal bool TargetLocked;
        private static readonly Color TargetRed = new Color(1f, .24f, .16f);

        private void DrawTargetReticle(VertexHelper mesh, Rect rect, float phase)
        {
            float opacity = TargetChoice ? 1f :
                Mathf.Min(Mathf.SmoothStep(0f, 1f, phase * 4.5f), 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.62f, 1f, phase)));
            if (opacity <= 0f) return;
            float arm = Mathf.Min(rect.width, rect.height) * .23f;
            float weight = Mathf.Clamp(rect.width * .048f, 2f, 4.5f);
            var ink = TargetLocked ? DuelStyle.Gold : TargetRed;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    var corner = rect.center + Vector2.Scale(rect.size * .485f, new Vector2(x, y));
                    var horizontal = corner - Vector2.right * (x * arm);
                    var vertical = corner - Vector2.up * (y * arm);
                    Line(mesh, horizontal, corner, weight * 3f, Shade(TargetRed, opacity * .31f));
                    Line(mesh, corner, vertical, weight * 3f, Shade(TargetRed, opacity * .31f));
                    Line(mesh, horizontal, corner, weight, Shade(ink, opacity));
                    Line(mesh, corner, vertical, weight, Shade(ink, opacity));
                    Glow(mesh, corner, weight * 2.5f, 1f, Shade(ink, opacity * .73f));
                }
            }
        }

        private static void DrawTargetBeam(VertexHelper mesh, Rect rect, float time)
        {
            float travel = Mathf.SmoothStep(0f, 1f, time * 4.4f);
            float opacity = Mathf.Clamp01(time * 9f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.40f, 1f, time)));
            var start = new Vector2(rect.xMin, rect.center.y);
            var head = start + Vector2.right * (rect.width * travel);
            float width = Mathf.Min(6.4f, rect.height * .28f);
            Line(mesh, start, head, width * 2.6f, Shade(TargetRed, opacity * .36f));
            Line(mesh, start, head, width, Shade(TargetRed, opacity * .91f));
            Line(mesh, start, head, width * .44f, Shade(DuelStyle.Gold, opacity));
            Glow(mesh, head, width * 2.1f, 1f, Shade(DuelStyle.Gold, opacity * .76f));
        }
    }
}
