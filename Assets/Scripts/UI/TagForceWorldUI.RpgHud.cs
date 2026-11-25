using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private readonly List<RpgInteraction> _rpgInteractions = new();
        private Button _rpgActionButton;
        private Text _rpgActionText;

        private readonly struct RpgInteraction
        {
            internal readonly RectTransform Anchor;
            internal readonly string Caption;
            internal readonly Action Select;

            internal RpgInteraction(RectTransform anchor, string caption, Action select)
            {
                Anchor = anchor;
                Caption = caption;
                Select = select;
            }
        }

        private void RegisterRpgInteraction(RectTransform anchor, string caption, Action select) =>
            _rpgInteractions.Add(new RpgInteraction(anchor, caption, select));

        private RpgInteraction NearbyRpgInteraction()
        {
            const float radius = .105f;
            float bestDistance = radius * radius;
            RpgInteraction best = default;
            foreach (var candidate in _rpgInteractions)
            {
                if (!candidate.Anchor.gameObject.activeInHierarchy) continue;
                float distance = RpgArtDirection(candidate.Anchor.anchorMin - _position).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = candidate;
            }
            return best;
        }

        private void ActivateRpgInteraction()
        {
            if (!CanWalk || _fieldMenu) return;
            var action = NearbyRpgInteraction().Select;
            if (action == null) return;
            DuelAudio.PlayTagForceSfx(TagForceSfx.Confirm);
            action();
        }

        private void RefreshRpgFieldPrompt()
        {
            if (_rpgActionButton == null) return;
            var candidate = CanWalk && !_fieldMenu ? NearbyRpgInteraction() : default;
            _rpgActionButton.gameObject.SetActive(candidate.Select != null);
            if (_rpgActionText.text != candidate.Caption) _rpgActionText.text = candidate.Caption ?? "";
            _fieldControls.gameObject.SetActive(_fieldMenu);
        }

        private void ClearRpgFieldHud()
        {
            _rpgInteractions.Clear();
            _rpgActionButton = null;
            _rpgActionText = null;
        }

        private void BuildRpgFieldHud(int cleared, int targetRegion, StoryEpisodeData[] episodes, string sideObjective)
        {
            var soft = new Color(.66f, .78f, .86f);
            var accent = new Color(.39f, .81f, .90f);
            _rpgFieldHud = DuelUiKit.FillRect("FieldHud", _body);
            var quest = Area(_rpgFieldHud, "QuestTrack", .02f, .865f, .635f, .989f);
            RpgUiStyle.Panel(quest);
            var iconSlot = Area(quest, "GoalIcon", .018f, .58f, .065f, .90f);
            DrawRpgIcon(iconSlot, "hud_target.png", "!", DuelUiKit.GoldAccent);
            bool hasEpisode = cleared < episodes.Length;
            string caption = hasEpisode ? $"EP {cleared + 1:00}   ·   {RpgRegions[targetRegion].Name}" : "스토리";
            string objective = episodes.Length == 0 ? "대본을 불러오지 못했습니다." :
                !hasEpisode ? $"전체 {episodes.Length}화 완료 · 자유 탐험" :
                _rpgLocation == targetRegion ? $"{episodes[cleared].opponentName}에게 다가가기" :
                "월드맵 또는 출구로 이동";
            HudLabel(Area(quest, "Chapter", .077f, .61f, .97f, .97f), caption, 18, DuelUiKit.GoldAccent);
            HudLabel(Area(quest, "Objective", .027f, sideObjective == null ? .10f : .29f, .973f, .62f),
                objective, 23, Color.white, heading: true);
            if (sideObjective != null)
                HudLabel(Area(quest, "SideQuestTrack", .027f, .003f, .973f, .31f),
                    $"의뢰 · {sideObjective}", 17, soft);

            var stats = Area(_rpgFieldHud, "DuelistHud", .651f, .865f, .98f, .989f);
            RpgUiStyle.Panel(stats);
            HudLabel(Area(stats, "Rank", .042f, .53f, .42f, .94f), $"Lv. {CollectionProgress.RpgRank}", 23, accent, heading: true);
            HudLabel(Area(stats, "Dp", .426f, .53f, .957f, .94f), $"{CollectionProgress.Balance:N0} DP",
                23, Color.white, align: TextAnchor.MiddleRight, heading: true);
            HudLabel(Area(stats, "ExperienceLabel", .042f, .09f, .17f, .46f), "EXP", 16, soft);
            var bar = Area(stats, "Experience", .18f, .19f, .52f, .35f);
            Tint(bar, new Color(.11f, .21f, .28f));
            Tint(Area(bar, "Fill", 0f, 0f, CollectionProgress.RpgRankProgress, 1f), accent);
            HudLabel(Area(stats, "Affinity", .56f, .09f, .957f, .46f),
                $"유세이 ♥ {CollectionProgress.RpgAffinity}/100", 17, new Color(1f, .72f, .80f), align: TextAnchor.MiddleRight);

            // Keep the tap target between the viewport and navigation so it cannot cover map objects.
            _rpgActionButton = RpgButton(Area(_rpgFieldHud, "ContextAction", .62f, -.079f, .98f, -.007f),
                "", ActivateRpgInteraction, 21, primary: true, sound: null);
            _rpgActionButton.navigation = new Navigation { mode = Navigation.Mode.None };
            _rpgActionText = _rpgActionButton.GetComponentInChildren<Text>();
            _fieldControls = HudLabel(Area(_rpgFieldHud, "FieldControls", .62f, -.079f, .98f, -.007f),
                "위쪽 버튼 · 필드로", 17, soft, align: TextAnchor.MiddleCenter);
            RefreshRpgFieldPrompt();
        }

        private static Text HudLabel(RectTransform slot, string text, int size, Color color,
            TextAnchor align = TextAnchor.MiddleLeft, bool heading = false)
        {
            var label = Label(slot, text, size, align);
            RpgUiStyle.Type(label, strong: heading);
            label.color = color;
            return label;
        }
    }
}
