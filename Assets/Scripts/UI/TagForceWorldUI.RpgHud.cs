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
        private Text _rpgMenuCaption;
        private CanvasGroup _rpgFieldInput;

        internal GameObject RpgFieldMenuScope => _rpgField && _fieldMenu && _rpgNavigation.gameObject.activeInHierarchy
            ? _rpgNavigation.gameObject : Root;

        private enum RpgActionPriority { Exit, Conversation, Service, Request, Story }

        private readonly struct RpgInteraction
        {
            internal readonly RectTransform Anchor;
            internal readonly string Caption;
            internal readonly Action Select;
            internal readonly RpgActionPriority Priority;
            internal readonly float Radius;

            internal RpgInteraction(RectTransform anchor, string caption, Action select, RpgActionPriority priority, float radius)
            {
                Anchor = anchor;
                Caption = caption;
                Select = select;
                Priority = priority;
                Radius = radius;
            }
        }

        private void RegisterRpgInteraction(RectTransform anchor, string caption, Action select,
            RpgActionPriority priority = RpgActionPriority.Conversation, float radius = .105f) =>
            _rpgInteractions.Add(new RpgInteraction(anchor, caption, select, priority, radius));

        private RpgInteraction NearbyRpgInteraction()
        {
            float bestDistance = float.PositiveInfinity;
            RpgInteraction best = default;
            foreach (var candidate in _rpgInteractions)
            {
                if (!candidate.Anchor.gameObject.activeInHierarchy) continue;
                float distance = RpgArtDirection(candidate.Anchor.anchorMin - _position).sqrMagnitude;
                if (distance >= candidate.Radius * candidate.Radius || candidate.Priority < best.Priority ||
                    (candidate.Priority == best.Priority && distance >= bestDistance)) continue;
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

        private void RefreshRpgFieldPrompt(float delta = 0f)
        {
            if (_rpgActionButton == null) return;
            var candidate = CanWalk && !_fieldMenu ? NearbyRpgInteraction() : default;
            bool available = candidate.Select != null;
            _rpgActionButton.interactable = available;
            string caption = candidate.Caption ?? "상호작용";
            if (_rpgActionText.text != caption) _rpgActionText.text = caption;
            _rpgActionText.color = available ? Color.white : new Color(.72f, .82f, .89f, .66f);
            if (_rpgFieldInput == null) return;
            UpdateRpgResidentMarkers(candidate.Anchor, delta);
            _rpgNavigation.gameObject.SetActive(_fieldMenu);
            _rpgFieldInput.interactable = _rpgFieldInput.blocksRaycasts = !_fieldMenu;
            string menuText = _fieldMenu ? "메뉴 닫기" : "메뉴";
            if (_rpgMenuCaption.text != menuText) _rpgMenuCaption.text = menuText;
        }

        private void ClearRpgFieldHud()
        {
            _rpgInteractions.Clear();
            _rpgActionButton = null;
            _rpgActionText = null;
            _rpgMenuCaption = null;
            _rpgFieldInput = null;
            _rpgQuestRewardBanner = null;
        }

        private void BuildRpgFieldHud(int cleared, int targetRegion, StoryEpisodeData[] episodes,
            (RpgSideQuest Quest, string Objective)? sideObjective)
        {
            var soft = new Color(.75f, .84f, .89f);
            var accent = new Color(.44f, .89f, .93f);
            _rpgFieldHud = DuelUiKit.FillRect("FieldHud", _body);

            var deckName = DeckRepository.UserDecks.Find(deck => deck.id == (_deckId ?? CollectionProgress.LastDuelDeckId))?.name ??
                DeckRepository.UserDecks.Find(deck => GameAppController.CanStartWithDeck(deck, out _))?.name ?? "덱 없음";
            var deckPlate = Area(_rpgFieldHud, "Deck", .019f, .928f, .242f, .980f);
            PaintRpgHudPlate(deckPlate);
            HudLabel(Area(deckPlate, "Icon", .045f, .07f, .145f, .93f), "▣", 19, accent, align: TextAnchor.MiddleCenter);
            HudLabel(Area(deckPlate, "DeckName", .168f, .07f, .967f, .93f), deckName, 22, Color.white, heading: true);

            bool hasEpisode = cleared < episodes.Length;
            string location = RpgRegions[_rpgLocation].Name;
            string caption = $"[메인] {(hasEpisode ? RpgRegions[targetRegion].Name : location)}";
            string objective = episodes.Length == 0 ? "대본을 불러오지 못했습니다." :
                !hasEpisode ? "자유 탐험" :
                _rpgLocation == targetRegion ? $"{episodes[cleared].opponentName}에게 다가가기" :
                $"이동 · {RpgRegions[targetRegion].Name}";
            var quest = Area(_rpgFieldHud, "QuestTrack", .019f, .837f, .284f, .915f);
            PaintRpgHudPlate(quest);
            quest.Find("Edge").GetComponent<Image>().color = DuelUiKit.GoldAccent;
            DrawRpgIcon(Area(quest, "GoalIcon", .039f, .56f, .105f, .94f), "icon_quest_main.png", "!", DuelUiKit.GoldAccent);
            HudLabel(Area(quest, "Chapter", .126f, .52f, .955f, .95f), caption, 16, DuelUiKit.GoldAccent);
            HudLabel(Area(quest, "Objective", .047f, .055f, .955f, .53f),
                objective, 19, Color.white, heading: true);
            if (sideObjective is { } side)
            {
                var sub = Area(_rpgFieldHud, "SideQuestTrack", .019f, .723f, .284f, .825f);
                PaintRpgHudPlate(sub);
                DrawRpgIcon(Area(sub, "RequestIcon", .039f, .63f, .105f, .95f), "icon_quest_sub.png", "?", accent);
                HudLabel(Area(sub, "Title", .126f, .63f, .955f, .95f), $"[의뢰] {side.Quest.Title}", 16, accent, heading: true);
                HudLabel(Area(sub, "Objective", .045f, .28f, .955f, .63f), side.Objective, 16, Color.white);
                HudLabel(Area(sub, "Reward", .045f, .055f, .955f, .29f), RpgQuestReward(side.Quest), 14, soft);
            }

            var stats = Area(_rpgFieldHud, "DuelistHud", .794f, .877f, .981f, .980f);
            var statsText = Area(stats, "Stats", 0f, .16f, .731f, 1f);
            PaintRpgHudPlate(statsText);
            HudLabel(Area(statsText, "Dp", .06f, .47f, .92f, .95f), $"DP   {CollectionProgress.Balance:N0}",
                24, Color.white, align: TextAnchor.MiddleRight, heading: true);
            HudLabel(Area(statsText, "Rank", .06f, .065f, .37f, .42f), $"Lv.{CollectionProgress.RpgRank}", 16, accent);
            var bar = Area(statsText, "Experience", .405f, .173f, .92f, .277f);
            Tint(bar, new Color(.18f, .29f, .36f));
            float rankProgress = Mathf.Clamp01(CollectionProgress.RpgRankProgress);
            if (rankProgress > 0f) Tint(Area(bar, "Fill", 0f, 0f, rankProgress, 1f), accent);

            var partner = Area(stats, "Partner", .768f, .246f, 1f, 1f);
            PaintRpgHudPlate(partner);
            var bust = _images.GetBust(RpgArt + $"Characters_Standing/full_{RpgPartnerId}.png");
            if (bust != null)
            {
                var frame = Area(partner, "PortraitFrame", .049f, .06f, .954f, .938f);
                frame.gameObject.AddComponent<RectMask2D>();
                Tint(frame, new Color(.14f, .21f, .29f));
                DuelUiKit.AddImage(Area(frame, "Portrait", -.52f, -.86f, 1.52f, 1.09f).gameObject,
                    bust, Color.white, false, false);
            }
            else HudLabel(partner, "유세이", 16, soft, align: TextAnchor.MiddleCenter);
            var affinity = Area(stats, "Affinity", .756f, -.022f, 1f, .228f);
            Tint(affinity, new Color(.055f, .073f, .117f, .91f));
            int hearts = Mathf.Clamp(CollectionProgress.RpgAffinity, 0, 100);
            for (int i = 0; i < 5; i++)
            {
                int level = hearts - i * 20;
                HudLabel(Area(affinity, $"Heart{i}", i * .2f, 0f, (i + 1) * .2f, 1f),
                    level > 0 ? "♥" : "♡", 15,
                    level >= 20 ? new Color(1f, .52f, .62f) : level > 0 ? new Color(1f, .74f, .81f) : soft,
                    align: TextAnchor.MiddleCenter);
            }

            _rpgActionButton = RpgButton(Area(_rpgFieldHud, "ContextAction", .575f, .027f, .851f, .105f),
                "", ActivateRpgInteraction, 21, primary: true, sound: null);
            _rpgActionButton.navigation = new Navigation { mode = Navigation.Mode.None };
            _rpgActionText = _rpgActionButton.GetComponentInChildren<Text>();
            var menu = RpgButton(Area(_rpgFieldHud, "MenuToggle", .873f, .028f, .981f, .088f),
                "메뉴", () => ToggleFieldMenu(), 20, sound: null);
            menu.navigation = new Navigation { mode = Navigation.Mode.None };
            _rpgMenuCaption = menu.GetComponentInChildren<Text>();

            if (_page == Page.Field) _rpgFieldInput = _rpgViewport.gameObject.AddComponent<CanvasGroup>();
            RefreshRpgFieldPrompt();
        }

        private static void PaintRpgHudPlate(RectTransform plate)
        {
            Tint(plate, new Color(.047f, .075f, .12f, .92f));
            Tint(Area(plate, "Edge", 0f, .08f, .013f, .92f), new Color(.39f, .74f, .79f, .95f));
            Tint(Area(plate, "Rule", .025f, .028f, .986f, .064f), new Color(.26f, .37f, .46f, .85f));
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
