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
        private readonly List<(RectTransform Icon, RectTransform Actor)> _rpgRadarMarkers = new();
        private static readonly Color32 ExitGreen = new(135, 250, 175, 255);

        internal GameObject RpgFieldMenuScope => _page switch
        {
            Page.SummerEpilogue => _rpgIntroDialogue?.ActiveRoot ?? Root,
            Page.ResortWardrobe => _body.gameObject,
            _ => _rpgField && _fieldMenu && _rpgNavigation.gameObject.activeInHierarchy ? _rpgNavigation.gameObject : Root,
        };

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
            UpdateRpgMinimap();
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
            _rpgRadarMarkers.Clear();
            _rpgQuestRewardBanner = null;
        }

        private void BuildRpgFieldHud(int cleared, int targetRegion, StoryEpisodeData[] episodes,
            (RpgSideQuest Quest, string Objective)? sideObjective)
        {
            var soft = new Color(.75f, .84f, .89f);
            var accent = new Color(.44f, .89f, .93f);
            _rpgFieldHud = DuelUiKit.FillRect("FieldHud", _body);

            var (timeName, timeIcon) = RpgTimeOfDay(_rpgLocation, cleared + 1);
            var clock = Area(_rpgFieldHud, "TimeOfDay", .024f, .929f, .109f, .982f);
            RpgUiStyle.Panel(clock, gold: true);
            HudLabel(Area(clock, "TimeText", .08f, .08f, .91f, .94f), $"{timeIcon}  {timeName}", 17, RpgUiStyle.Gold,
                align: TextAnchor.MiddleCenter, heading: true);
            var locationRibbon = Area(_rpgFieldHud, "CurrentRegion", .113f, .929f, .322f, .982f);
            RpgUiStyle.Panel(locationRibbon, accent: true);
            RpgUiStyle.Display(HudLabel(Area(locationRibbon, "RegionName", .105f, .08f, .95f, .93f),
                RpgRegions[_rpgLocation].Name, 26, Color.white));

            var deckName = DeckRepository.UserDecks.Find(deck => deck.id == (_deckId ?? CollectionProgress.LastDuelDeckId))?.name ??
                DeckRepository.UserDecks.Find(deck => GameAppController.CanStartWithDeck(deck, out _))?.name ?? "덱 없음";
            var deckPlate = Area(_rpgFieldHud, "Deck", .024f, .89f, .233f, .925f);
            RpgUiStyle.Panel(deckPlate);
            HudLabel(Area(deckPlate, "Icon", .059f, .1f, .20f, .95f), "덱", 15, accent, heading: true);
            HudLabel(Area(deckPlate, "DeckName", .198f, .03f, .94f, .97f), deckName, 16, Color.white, heading: true);

            bool hasEpisode = cleared < episodes.Length;
            string location = RpgRegions[_rpgLocation].Name;
            string caption = $"[메인] {(hasEpisode ? RpgRegions[targetRegion].Name : location)}";
            string objective = episodes.Length == 0 ? "대본을 불러오지 못했습니다." :
                !hasEpisode ? "자유 탐험" :
                _rpgLocation == targetRegion ? $"{episodes[cleared].opponentName}에게 다가가기" :
                $"이동 · {RpgRegions[targetRegion].Name}";
            var quest = Area(_rpgFieldHud, "QuestTrack", .024f, .802f, .303f, .885f);
            RpgUiStyle.Panel(quest, gold: true);
            DrawRpgIcon(Area(quest, "GoalIcon", .039f, .56f, .105f, .94f), "icon_quest_main.png", "!", DuelUiKit.GoldAccent);
            HudLabel(Area(quest, "Chapter", .126f, .51f, .955f, .975f), caption, 16, DuelUiKit.GoldAccent);
            HudLabel(Area(quest, "Objective", .047f, .035f, .955f, .50f),
                objective, 18, Color.white, heading: true);
            if (sideObjective is { } side)
            {
                var sub = Area(_rpgFieldHud, "SideQuestTrack", .024f, .684f, .303f, .792f);
                RpgUiStyle.Panel(sub, accent: true);
                DrawRpgIcon(Area(sub, "RequestIcon", .039f, .63f, .105f, .95f), "icon_quest_sub.png", "?", accent);
                HudLabel(Area(sub, "Title", .126f, .66f, .955f, .978f), $"[의뢰] {side.Quest.Title}", 16, accent, heading: true);
                HudLabel(Area(sub, "Objective", .045f, .33f, .955f, .657f), side.Objective, 16, Color.white);
                HudLabel(Area(sub, "Reward", .045f, .04f, .955f, .325f), RpgQuestReward(side.Quest), 14, soft);
            }

            BuildRpgMinimap(Area(_rpgFieldHud, "Minimap", .792f, .796f, .975f, .982f));
            var stats = Area(_rpgFieldHud, "DuelistHud", .792f, .696f, .975f, .787f);
            var statsText = Area(stats, "Stats", 0f, .10f, .697f, 1f);
            RpgUiStyle.Panel(statsText);
            HudLabel(Area(statsText, "Dp", .06f, .497f, .92f, .991f), $"DP   {CollectionProgress.Balance:N0}",
                21, Color.white, align: TextAnchor.MiddleRight, heading: true);
            HudLabel(Area(statsText, "Rank", .06f, .02f, .33f, .49f), $"Lv.{CollectionProgress.RpgRank}", 16, accent);
            HudLabel(Area(statsText, "ExperienceLabel", .354f, .02f, .535f, .49f), "EXP", 12, soft);
            var bar = Area(statsText, "Experience", .55f, .173f, .92f, .277f);
            Tint(bar, new Color(.18f, .29f, .36f));
            float rankProgress = Mathf.Clamp01(CollectionProgress.RpgRankProgress);
            if (rankProgress > 0f) Tint(Area(bar, "Fill", 0f, 0f, rankProgress, 1f), accent);

            var partner = Area(stats, "Partner", .733f, .307f, 1f, 1f);
            string outfit = FieldOutfit(RpgPartnerId);
            var bust = (outfit == null ? null : _images.GetBust(RpgArt + RpgWardrobe.Preview(RpgPartnerId, outfit))) ??
                _images.GetBust(RpgArt + $"Characters_Standing/full_{RpgPartnerId}.png");
            if (bust != null)
            {
                var frame = Area(partner, "PortraitFrame", .049f, .06f, .954f, .938f);
                var rim = RpgUiStyle.Glyph(RpgUiStyle.Symbol.Hexagon);
                DuelUiKit.AddImage(DuelUiKit.FillRect("Rim", frame).gameObject, rim, accent, false, false);
                var inner = Area(frame, "Crop", .065f, .065f, .935f, .935f);
                DuelUiKit.AddImage(inner.gameObject, rim, new Color(.13f, .24f, .34f), false, false);
                inner.gameObject.AddComponent<Mask>();
                DuelUiKit.AddImage(Area(inner, "Portrait", -.43f, -1.15f, 1.43f, 1.11f).gameObject,
                    bust, Color.white, false, false);
            }
            else HudLabel(partner, RpgPartnerName, 16, soft, align: TextAnchor.MiddleCenter);
            var affinity = Area(stats, "Affinity", .733f, .01f, 1f, .294f);
            RpgUiStyle.Panel(affinity);
            int hearts = Mathf.Clamp(CollectionProgress.RpgAffinity, 0, 100);
            for (int i = 0; i < 5; i++)
            {
                int level = hearts - i * CollectionProgress.RpgAffinityPerHeart;
                HudLabel(Area(affinity, $"Heart{i}", .04f + i * .184f, 0f, .20f + i * .184f, 1f),
                    level > 0 ? "♥" : "♡", 13,
                    level >= CollectionProgress.RpgAffinityPerHeart ? new Color(1f, .52f, .62f) : level > 0 ? new Color(1f, .74f, .81f) : soft,
                    align: TextAnchor.MiddleCenter);
            }

            var guide = Area(_rpgFieldHud, "Controls", .024f, .032f, .383f, .077f);
            RpgUiStyle.Panel(guide);
            bool touch = Application.isMobilePlatform;
            HudLabel(Area(guide, "Space", .04f, .07f, .416f, .94f), touch ? "다가가서 행동" : "SPACE   대상 · 행동", 15, soft, heading: true);
            var map = RpgButton(Area(guide, "OpenMap", .443f, .07f, .700f, .94f), touch ? "월드맵" : "M   월드맵",
                () => { if (CanWalk) Show(Page.Map); }, 16);
            map.navigation = new Navigation { mode = Navigation.Mode.None };
            HudLabel(Area(guide, "Tab", .755f, .07f, .979f, .94f), touch ? "탭으로 선택" : "TAB   메뉴", 15, soft, heading: true);
            _rpgActionButton = RpgButton(Area(_rpgFieldHud, "ContextAction", .653f, .028f, .851f, .087f),
                "", ActivateRpgInteraction, 21, primary: true, sound: null, prominent: true);
            _rpgActionButton.navigation = new Navigation { mode = Navigation.Mode.None };
            _rpgActionText = _rpgActionButton.GetComponentInChildren<Text>();
            var menu = RpgButton(Area(_rpgFieldHud, "MenuToggle", .873f, .028f, .981f, .088f),
                "메뉴", () => ToggleFieldMenu(), 20, sound: null);
            menu.navigation = new Navigation { mode = Navigation.Mode.None };
            _rpgMenuCaption = menu.GetComponentInChildren<Text>();

            if (_page == Page.Field) _rpgFieldInput = _rpgViewport.gameObject.AddComponent<CanvasGroup>();
            RefreshRpgFieldPrompt();
        }

        private static (string, string) RpgTimeOfDay(int region, int episode) => region switch
        {
            RpgArk or RpgWrgp or RpgMoment or RpgStadium or RpgPrison or RpgHideout => ("밤", "☾"),
            RpgBridge when episode >= EpisodeRegionMap.Length => ("노을", "◒"),
            _ => ("아침", "☀"),
        };

        private void BuildRpgMinimap(RectTransform plate)
        {
            RpgUiStyle.Panel(plate, accent: true);
            var radar = Area(plate, "Radar", .072f, .11f, .927f, .725f);
            Tint(radar, new Color(.043f, .10f, .167f, .94f));
            var grid = DuelUiKit.FillRect("Grid", radar);
            var rule = new Color(.36f, .66f, .77f, .17f);
            for (int i = 1; i < 8; i++)
                Tint(Area(grid, "Longitude", i / 8f, 0f, i / 8f + .003f, 1f), rule);
            for (int i = 1; i < 4; i++)
                Tint(Area(grid, "Latitude", 0f, i / 4f, 1f, i / 4f + .009f), rule);
            var points = Area(radar, "Points", .035f, .1f, .965f, .9f);
            HudLabel(Area(plate, "Title", .085f, .744f, .73f, .941f), "구역 레이더", 16,
                RpgUiStyle.Cyan, heading: true);
            HudLabel(Area(plate, "Orientation", .74f, .744f, .915f, .941f), "N", 15, Color.white,
                align: TextAnchor.MiddleRight, heading: true);
            foreach (var (slot, destination, _) in _rpgExits)
            {
                var icon = Mark($"Exit_{destination}", RpgUiStyle.Symbol.Arrow, ExitGreen, 16f, RpgExitPoints[slot]);
                icon.localRotation = Quaternion.Euler(0f, 0f, slot == 0 ? 90f : slot == 1 ? -90f : 0f);
            }
            if (_rpgFieldObjects != null && _rpgFieldObjects.Find("HiddenCache") is RectTransform treasure)
                Mark("Cache", RpgUiStyle.Symbol.Star, RpgUiStyle.Gold, 18f, treasure.anchorMin);
            foreach (var resident in _rpgResidentMotion)
            {
                bool story = resident.Role == RpgResidentRole.Story;
                var icon = Mark(story ? "Story" : resident.Actor.name, story ? RpgUiStyle.Symbol.Alert : RpgUiStyle.Symbol.Dot,
                    story ? RpgUiStyle.Gold : RpgUiStyle.Cyan, story ? 19f : 9f, resident.Actor.anchorMin);
                _rpgRadarMarkers.Add((icon, resident.Actor));
            }
            var player = Mark("Player", RpgUiStyle.Symbol.Dot, Color.white, 12f, _position);
            _rpgRadarMarkers.Add((player, _player));

            RectTransform Mark(string name, RpgUiStyle.Symbol symbol, Color color, float size, Vector2 point)
            {
                var marker = Anchor(points, name, RadarPosition(point), new Vector2(size, size));
                DuelUiKit.AddImage(marker.gameObject, RpgUiStyle.Glyph(symbol), color, false, false);
                return marker;
            }
        }

        private static Vector2 RadarPosition(Vector2 point) =>
            new(Mathf.InverseLerp(.06f, .94f, point.x), Mathf.InverseLerp(.095f, .455f, point.y));

        private void UpdateRpgMinimap()
        {
            foreach (var (icon, actor) in _rpgRadarMarkers)
            {
                icon.gameObject.SetActive(actor != null && actor.gameObject.activeInHierarchy);
                if (icon.gameObject.activeSelf) icon.anchorMin = icon.anchorMax = RadarPosition(actor.anchorMin);
            }
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
