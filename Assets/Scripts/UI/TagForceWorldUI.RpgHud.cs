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
        private RectTransform _gxFieldDetails;
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
            if (IsGxCampaign) _rpgActionButton.gameObject.SetActive(available);
            string caption = candidate.Caption ?? "상호작용";
            if (_rpgActionText.text != caption) _rpgActionText.text = caption;
            _rpgActionText.color = available ? Color.white : new Color(.72f, .82f, .89f, .66f);
            if (_rpgFieldInput == null) return;
            UpdateRpgResidentMarkers(candidate.Anchor, delta);
            UpdateRpgMinimap();
            _rpgNavigation.gameObject.SetActive(_fieldMenu);
            if (_gxFieldDetails != null) _gxFieldDetails.gameObject.SetActive(_fieldMenu);
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
            _gxFieldDetails = null;
            _rpgRadarMarkers.Clear();
            _rpgQuestRewardBanner = null;
        }

        private void BuildRpgFieldHud(int cleared, int targetRegion, StoryEpisodeData[] episodes,
            (RpgSideQuest Quest, string Objective)? sideObjective)
        {
            var soft = new Color(.75f, .84f, .89f);
            var accent = new Color(.44f, .89f, .93f);
            _rpgFieldHud = DuelUiKit.FillRect("FieldHud", _body);
            if (IsGxCampaign)
            {
                BuildGxFieldHud(cleared, targetRegion, episodes);
                return;
            }

            var (timeName, timeIcon) = RpgTimeOfDay(_rpgLocation, cleared + 1);
            var clock = Area(_rpgFieldHud, "TimeOfDay", .024f, .929f, .109f, .982f);
            RpgUiStyle.Panel(clock, gold: true);
            HudLabel(Area(clock, "TimeText", .08f, .08f, .91f, .94f), $"{timeIcon}  {timeName}", 17, RpgUiStyle.Gold,
                align: TextAnchor.MiddleCenter, heading: true);
            var locationRibbon = Area(_rpgFieldHud, "CurrentRegion", .113f, .929f, .322f, .982f);
            RpgUiStyle.Panel(locationRibbon, accent: true);
            RpgUiStyle.Display(HudLabel(Area(locationRibbon, "RegionName", .105f, .08f, .95f, .93f),
                CampaignRegions[_rpgLocation].Name, 26, Color.white));

            var deckName = RpgFieldDeckName();
            var episode = cleared < episodes.Length ? episodes[cleared] : null;
            var deckPlate = Area(_rpgFieldHud, "Deck", .024f, .89f, .233f, .925f);
            RpgUiStyle.Panel(deckPlate);
            HudLabel(Area(deckPlate, "Icon", .059f, .1f, .20f, .95f), "덱", 15, accent, heading: true);
            HudLabel(Area(deckPlate, "DeckName", .198f, .03f, .94f, .97f), deckName, 16, Color.white, heading: true);

            bool hasEpisode = cleared < episodes.Length;
            string location = CampaignRegions[_rpgLocation].Name;
            string caption = $"[메인] {(hasEpisode ? CampaignRegions[targetRegion].Name : location)}";
            string objective = episodes.Length == 0 ? "대본을 불러오지 못했습니다." :
                !hasEpisode ? "자유 탐험" :
                episode.isCutsceneOnly ? episode.title :
                _rpgLocation == targetRegion ? $"{episode.opponentName}에게 다가가기" :
                $"이동 · {CampaignRegions[targetRegion].Name}";
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
            int portraitId = RpgPlayerId > 0 ? RpgPlayerId : RpgPartnerId;
            string outfit = FieldOutfit(portraitId);
            var bust = (outfit == null ? null : _images.GetBust(RpgArt + RpgWardrobe.Preview(portraitId, outfit))) ??
                _images.GetBust(CampaignBustPath(portraitId));
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
            else HudLabel(partner, RpgPlayerId > 0 ? RpgPlayerName : RpgPartnerName, 16, soft, align: TextAnchor.MiddleCenter);
            var affinity = Area(stats, "Affinity", .733f, .01f, 1f, .294f);
            RpgUiStyle.Panel(affinity);
            int hearts = Mathf.Clamp(CollectionProgress.RpgAffinityFor(RpgPartnerId), 0, 100);
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

        private string RpgFieldDeckName() => DeckRepository.UserDecks.Find(deck =>
                deck.id == (_deckId ?? CollectionProgress.LastDuelDeckId))?.name ??
            DeckRepository.UserDecks.Find(deck => GameAppController.CanStartWithDeck(deck, out _))?.name ?? "덱 없음";

        private void BuildGxFieldHud(int cleared, int targetRegion, StoryEpisodeData[] episodes)
        {
            var episode = cleared < episodes.Length ? episodes[cleared] : null;
            var mission = Area(_rpgFieldHud, "QuestTrack", .025f, .810f, .369f, .972f);
            RpgUiStyle.Glass(mission);
            RpgUiStyle.Display(HudLabel(Area(mission, "RegionName", .056f, .627f, .95f, .942f),
                CampaignRegions[_rpgLocation].Name, 29, Color.white));
            Tint(Area(mission, "Divider", .056f, .602f, .946f, .609f), new Color(.46f, .63f, .80f, .32f));
            bool travel = episode != null && targetRegion != _rpgLocation;
            string objective = episode == null ? episodes.Length > 0 ? "자유 탐험" : "이야기를 불러오지 못했습니다" :
                travel ? $"{CampaignRegions[targetRegion].Name} · 이동" : episode.isCutsceneOnly ? episode.title :
                $"{episode.opponentName}에게 말 걸기";
            HudLabel(Area(mission, "Chapter", .056f, .338f, .69f, .594f),
                episode == null ? "현재 진행" : $"!  다음 목표 · {episode.chapter}기", 17, RpgUiStyle.Gold, heading: true);
            var approach = _rpgInteractions.Find(candidate => candidate.Priority == RpgActionPriority.Story).Select;
            var objectiveLabel = HudLabel(Area(mission, "Objective", .056f, .069f, approach != null || travel ? .705f : .947f, .344f),
                objective, 21, Color.white, heading: true);
            objectiveLabel.resizeTextMinSize = 15;
            objectiveLabel.resizeTextMaxSize = 21;
            objectiveLabel.resizeTextForBestFit = true;
            if (approach != null || travel)
            {
                var guide = RpgButton(Area(mission, "Guide", .724f, .103f, .951f, .482f), travel ? "지도에서 찾기" : "목표로 이동", () =>
                {
                    if (!CanWalk || _fieldMenu) return;
                    if (travel)
                    {
                        _gxMapFocusGoal = true;
                        Show(Page.Map);
                    }
                    else approach();
                }, 18, primary: true);
                guide.navigation = new Navigation { mode = Navigation.Mode.None };
            }

            BuildRpgMinimap(Area(_rpgFieldHud, "Minimap", .787f, .791f, .974f, .972f));
            var stats = Area(_rpgFieldHud, "DuelistHud", .787f, .715f, .974f, .782f);
            RpgUiStyle.Glass(stats);
            HudLabel(Area(stats, "Rank", .09f, .27f, .47f, .96f), $"Lv. {CollectionProgress.RpgRank}", 19, RpgUiStyle.Cyan, heading: true);
            HudLabel(Area(stats, "Dp", .43f, .27f, .92f, .96f), $"DP {CollectionProgress.Balance:N0}",
                20, Color.white, align: TextAnchor.MiddleRight, heading: true);
            var experience = Area(stats, "Experience", .09f, .18f, .92f, .25f);
            Tint(experience, new Color(.23f, .35f, .46f));
            float progress = Mathf.Clamp01(CollectionProgress.RpgRankProgress);
            if (progress > 0f) Tint(Area(experience, "Fill", 0f, 0f, progress, 1f), RpgUiStyle.Cyan);

            _gxFieldDetails = Area(_rpgFieldHud, "FieldDetails", .741f, .426f, .974f, .699f);
            RpgUiStyle.Glass(_gxFieldDetails);
            var deck = episode?.forcedPlayerDeckId is > 0 && _rpgWorldDecks.TryGetValue(episode.forcedPlayerDeckId, out var povDeck)
                ? povDeck.title : RpgFieldDeckName();
            Detail("Deck", .69f, "출전 덱", deck.TrimStart(' ', '/'));
            Detail("Partner", .40f, "동료", $"{RpgPartnerName}  ·  호감도 {CollectionProgress.RpgAffinityFor(RpgPartnerId)} / 100");
            PresetDeckInfo rival = null;
            if (episode != null) _rpgWorldDecks.TryGetValue(episode.deckId, out rival);
            Detail("GxOpponentTier", .11f, rival == null ? "상대 덱" : $"상대 덱 · ★{rival.difficultyTier}",
                rival?.title ?? "예정된 듀얼 없음");
            _gxFieldDetails.gameObject.SetActive(false);

            if (_page == Page.Field)
            {
                AddGxIdentity(_player, $"나 · {RpgPlayerName}", RpgUiStyle.Cyan);
                if (_rpgCompanion != null) AddGxIdentity(_rpgCompanion, $"동료 · {RpgPartnerName}", Color.white);
            }
            var controls = Area(_rpgFieldHud, "Controls", .025f, .035f, .263f, .085f);
            RpgUiStyle.Glass(controls);
            HudLabel(Area(controls, "Move", .074f, 0f, .94f, 1f), Application.isMobilePlatform ? "바닥을 터치해 이동" : "방향키 또는 스틱으로 이동",
                17, new Color(.82f, .90f, .97f));
            _rpgActionButton = RpgButton(Area(_rpgFieldHud, "ContextAction", .501f, .030f, .745f, .098f),
                "", ActivateRpgInteraction, 20, primary: true, sound: null, prominent: true);
            _rpgActionButton.navigation = new Navigation { mode = Navigation.Mode.None };
            _rpgActionText = _rpgActionButton.GetComponentInChildren<Text>();
            var map = RpgButton(Area(_rpgFieldHud, "OpenMap", .756f, .030f, .864f, .098f), "월드맵",
                () => { if (CanWalk) Show(Page.Map); }, 21);
            map.navigation = new Navigation { mode = Navigation.Mode.None };
            var menu = RpgButton(Area(_rpgFieldHud, "MenuToggle", .875f, .030f, .974f, .098f), "메뉴",
                () => ToggleFieldMenu(), 21, sound: null);
            menu.navigation = new Navigation { mode = Navigation.Mode.None };
            _rpgMenuCaption = menu.GetComponentInChildren<Text>();
            if (_page == Page.Field) _rpgFieldInput = _rpgViewport.gameObject.AddComponent<CanvasGroup>();
            RefreshRpgFieldPrompt();

            void Detail(string id, float y, string title, string text)
            {
                var section = Area(_gxFieldDetails, id, .075f, y, .925f, y + .25f);
                HudLabel(Area(section, "Heading", 0f, .55f, 1f, 1f), title, 16, RpgUiStyle.Cyan, heading: true);
                var value = HudLabel(Area(section, "Value", 0f, 0f, 1f, .55f), text, 18, Color.white, heading: true);
                value.resizeTextMinSize = 14;
                value.resizeTextMaxSize = 18;
                value.resizeTextForBestFit = true;
            }
        }

        private static void AddGxIdentity(RectTransform actor, string caption, Color color)
        {
            var tag = Anchor(actor, "GxIdentity", new Vector2(.5f, .012f), new Vector2(144f, 27f));
            RpgUiStyle.Glass(tag);
            HudLabel(tag, caption, 16, color, align: TextAnchor.MiddleCenter, heading: true).raycastTarget = false;
        }

        private static (string, string) RpgTimeOfDay(int region, int episode) => region switch
        {
            RpgArk or RpgWrgp or RpgMoment or RpgStadium or RpgPrison or RpgHideout => ("밤", "☾"),
            RpgBridge when episode >= EpisodeRegionMap.Length => ("노을", "◒"),
            _ => ("아침", "☀"),
        };

        private void BuildRpgMinimap(RectTransform plate)
        {
            bool gx = IsGxCampaign;
            if (gx) RpgUiStyle.Glass(plate);
            else RpgUiStyle.Panel(plate, accent: true);
            var radar = Area(plate, "Radar", .072f, gx ? .24f : .11f, .927f, .725f);
            Tint(radar, new Color(.043f, .10f, .167f, .94f));
            var grid = DuelUiKit.FillRect("Grid", radar);
            var rule = new Color(.36f, .66f, .77f, .17f);
            for (int i = 1; i < 8; i++)
                Tint(Area(grid, "Longitude", i / 8f, 0f, i / 8f + .003f, 1f), rule);
            for (int i = 1; i < 4; i++)
                Tint(Area(grid, "Latitude", 0f, i / 4f, 1f, i / 4f + .009f), rule);
            var points = Area(radar, "Points", .035f, .1f, .965f, .9f);
            HudLabel(Area(plate, "Title", .085f, .744f, gx ? .90f : .73f, .941f), gx ? "지역 미니맵" : "구역 레이더", gx ? 18 : 16,
                RpgUiStyle.Cyan, heading: true);
            if (gx)
            {
                HudLabel(Area(plate, "LegendPlayer", .085f, .015f, .33f, .22f), "● 나", 15, RpgUiStyle.Cyan, heading: true);
                HudLabel(Area(plate, "LegendGoal", .34f, .015f, .59f, .22f), "! 목표", 15, RpgUiStyle.Gold, heading: true);
                HudLabel(Area(plate, "LegendExit", .62f, .015f, .94f, .22f), "▲ 출구", 15, ExitGreen, heading: true);
            }
            else HudLabel(Area(plate, "Orientation", .74f, .744f, .915f, .941f), "N", 15, Color.white,
                align: TextAnchor.MiddleRight, heading: true);
            foreach (var (slot, destination, _) in _rpgExits)
            {
                var icon = Mark($"Exit_{destination}", RpgUiStyle.Symbol.Arrow, ExitGreen, 16f, RpgExitPoints[slot]);
                icon.localRotation = Quaternion.Euler(0f, 0f, slot == 0 ? 90f : slot == 1 ? -90f : 0f);
            }
            if (!gx && _rpgFieldObjects != null && _rpgFieldObjects.Find("HiddenCache") is RectTransform treasure)
                Mark("Cache", RpgUiStyle.Symbol.Star, RpgUiStyle.Gold, 18f, treasure.anchorMin);
            foreach (var resident in _rpgResidentMotion)
            {
                bool story = resident.Role == RpgResidentRole.Story;
                if (gx && !story) continue;
                var icon = Mark(story ? "Story" : resident.Actor.name, story ? RpgUiStyle.Symbol.Alert : RpgUiStyle.Symbol.Dot,
                    story ? RpgUiStyle.Gold : RpgUiStyle.Cyan, story ? 19f : 9f, resident.Actor.anchorMin);
                _rpgRadarMarkers.Add((icon, resident.Actor));
            }
            var player = Mark("Player", RpgUiStyle.Symbol.Dot, gx ? RpgUiStyle.Cyan : Color.white, gx ? 17f : 12f, _position);
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
