using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private TagForceData _rpgConfiguration;
        private RpgRegion[] _campaignRegions = RpgRegions;
        private (int From, int Exit, int To, int Entry)[] _campaignRoutes;
        private readonly Dictionary<string, int> _campaignRegionIndex = new(StringComparer.Ordinal);
        internal string RpgCampaignId { get; private set; } = StoryModeUI.RpgSeriesId;
        internal bool IsGxCampaign => RpgCampaignId == StoryModeUI.GxRpgSeriesId;
        internal string RpgAssetFolder => IsGxCampaign ? "GX" : "5DS";
        private string RpgArt => "TagForce/" + RpgAssetFolder + "/";
        private string RpgRegionPreference => $"ygo.{RpgCampaignId}-rpg.last-region";
        private RpgRegion[] CampaignRegions => _campaignRegions;
        private string CampaignFieldTheme => _rpgConfiguration?.spots[_rpgLocation].fieldTheme;
        private (int From, int Exit, int To, int Entry)[] CampaignRoutes => _campaignRoutes ?? RpgPaths;
        private StoryEpisodeData CurrentRpgEpisode => _rpgCampaign?.episodes != null && RpgClears < _rpgCampaign.episodes.Length
            ? _rpgCampaign.episodes[RpgClears] : null;
        private int RpgPlayerId => IsGxCampaign ? CurrentRpgEpisode?.playAsCharacterId ?? 0 : 0;
        internal string RpgHeroName => _rpgCampaign?.protagonist ?? "카이";
        internal Sprite RpgDuelPortrait(int id) => _images.GetBust(CampaignBustPath(id));
        private string RpgPlayerName => RpgPlayerId > 0 ? DeckRepository.GetCharacter(RpgPlayerId)?.name ?? "아스카" :
            RpgHeroName;
        private bool AutomaticRpgScenePending => _rpgField && _page == Page.Field &&
            CurrentRpgEpisode?.isCutsceneOnly == true;

        private bool LoadWorldCampaign(string seriesId)
        {
            if (_rpgCampaign != null && RpgCampaignId == seriesId) return true;
            var script = StoryModeUI.LoadRpgScript(Application.streamingAssetsPath, seriesId);
            var decks = StoryModeUI.LoadRpgDecks(Application.streamingAssetsPath, seriesId);
            if (script == null || script.episodes.Any(episode => !episode.isCutsceneOnly &&
                (!decks.ContainsKey(episode.deckId) || episode.forcedPlayerDeckId != 0 &&
                    !decks.ContainsKey(episode.forcedPlayerDeckId))))
                return false;

            var configuration = seriesId == StoryModeUI.GxRpgSeriesId
                ? TagForceData.LoadRpgConfiguration(Application.streamingAssetsPath, seriesId) : null;
            if (seriesId == StoryModeUI.GxRpgSeriesId && (configuration == null ||
                script.episodes.Any(episode => configuration.Spot(episode.regionId) == null))) return false;

            RpgCampaignId = seriesId;
            _rpgCampaign = script;
            _rpgWorldDecks = decks;
            _rpgConfiguration = configuration;
            _campaignRegionIndex.Clear();
            _campaignRoutes = null;
            _campaignRegions = RpgRegions;
            if (configuration == null) return true;

            _campaignRegions = configuration.spots.Select((spot, index) =>
                new RpgRegion(spot.spotId, spot.name, spot.unlockAfter, index, mapPath: spot.mapPath)).ToArray();
            for (int index = 0; index < _campaignRegions.Length; ++index)
                _campaignRegionIndex.Add(_campaignRegions[index].Id, index);

            var edges = new List<(int From, int Exit, int To, int Entry)>();
            var usedExitSlots = new int[_campaignRegions.Length];
            for (int index = 0; index < configuration.spots.Length; ++index)
            {
                foreach (string neighbor in configuration.spots[index].routes ?? Array.Empty<string>())
                    if (_campaignRegionIndex.TryGetValue(neighbor, out int target) && target != index &&
                        usedExitSlots[index] < RpgExitPoints.Length && usedExitSlots[target] < RpgExitPoints.Length)
                        edges.Add((index, usedExitSlots[index]++, target, usedExitSlots[target]++));
            }
            _campaignRoutes = edges.ToArray();
            return true;
        }

        internal int CampaignRegionForEpisode(int number)
        {
            if (!IsGxCampaign) return RegionForEpisode(number);
            var episodes = _rpgCampaign.episodes;
            return _campaignRegionIndex[episodes[Mathf.Clamp(number, 1, episodes.Length) - 1].regionId];
        }

        internal RpgRegion CampaignRegion(int index) => CampaignRegions[index];

        private Vector2 CampaignMapPoint(int index) => IsGxCampaign
            ? new Vector2(_rpgConfiguration.spots[index].worldX, _rpgConfiguration.spots[index].worldY)
            : RpgNodePoints[index];

        private (string Name, Vector2 Position, string Description) CampaignLandmark(int index)
        {
            if (!IsGxCampaign) return RpgLandmarks[index];
            var spot = _rpgConfiguration.spots[index];
            return ("지역 안내", new Vector2(.67f, .30f), spot.subtitle);
        }

        private bool AdvanceAutomaticRpgScene()
        {
            if (!AutomaticRpgScenePending) return false;
            if (_rpgDialogueCast != null || _rpgPendingRegion >= 0 ||
                _rpgTravelFade != null && _rpgTravelFade.gameObject.activeSelf) return true;
            int next = RpgClears;
            int region = CampaignRegionForEpisode(next + 1);
            if (_rpgLocation != region) BeginRpgTravel(region, RpgArrival);
            else if (_player != null) _app.OpenRpgFieldEpisode(next);
            return true;
        }

        private IEnumerable<StoryEpisodeData> GxResidents(int region, int clears, StoryEpisodeData active)
        {
            foreach (var entry in _rpgConfiguration.roster ?? Array.Empty<TagForceVisitor>())
            {
                if (entry.unlockAfter > clears || entry.charId == active?.charId || entry.charId == RpgPlayerId ||
                    entry.charId == RpgPartnerId) continue;
                var move = entry.storyMoves?.LastOrDefault(change => change.after <= clears);
                if ((move?.spotId ?? entry.spotId) != CampaignRegions[region].Id) continue;
                var character = DeckRepository.GetCharacter(entry.charId);
                if (character == null) continue;
                var duel = _rpgCampaign.episodes.FirstOrDefault(episode => !episode.isCutsceneOnly && episode.charId == entry.charId);
                yield return new StoryEpisodeData
                {
                    charId = entry.charId, opponentName = character.name,
                    deckId = duel?.deckId ?? 0,
                };
            }
        }

        private void BuildGxPickups()
        {
            var spot = _rpgConfiguration.spots[_rpgLocation];
            var pickups = spot.defaultPickups ?? Array.Empty<string>();
            var progress = CollectionProgress.GetTagForceProgress(RpgCampaignId);
            for (int index = 0; index < pickups.Length; ++index)
            {
                string item = pickups[index];
                string claim = $"rpg/{spot.spotId}/{item}";
                if (progress.pickups.Contains(claim)) continue;
                string name = item switch
                {
                    "golden_egg_bread" => "황금 달걀빵",
                    "blue_queen_tea" => "블루 퀸 티",
                    "dp_small" => "150 DP",
                    _ => item,
                };
                var position = new Vector2(index % 2 == 0 ? .21f : .75f, .30f + .08f * (index / 2));
                var marker = Anchor(_rpgFieldObjects, $"Pickup_{item}", position, new Vector2(89f, 35f));
                RpgButton(marker, "✦ 줍기", Approach, 17, primary: true);
                _questMarkers.Add(marker);
                RegisterRpgInteraction(marker, "반짝이는 물건 · 줍기", Approach, RpgActionPriority.Service);

                void Approach() => MoveTo(position, Take);
                void Take()
                {
                    var next = CollectionProgress.GetTagForceProgress(RpgCampaignId).Copy();
                    if (next.pickups.Contains(claim)) return;
                    next.pickups.Add(claim);
                    int dp = item == "dp_small" ? 150 : 0;
                    if (dp == 0) next.ChangeItem(item, 1);
                    if (!CollectionProgress.TrySaveTagForce(RpgCampaignId, next, dp, out string error))
                    {
                        ShowStatus(error);
                        return;
                    }
                    DuelAudio.PlayTagForceReward();
                    Show(Page.Field);
                    ShowStatus($"{name} 획득");
                }
            }
        }

        private void BuildGxStoryJournal()
        {
            BuildRpgMenuBackdrop();
            int clears = RpgClears;
            int total = _rpgCampaign.episodes.Length;
            var paper = Area(_body, "GxStoryJournal", .13f, .12f, .87f, .91f);
            RpgUiStyle.Dialogue(paper);
            HudLabel(Area(paper, "Title", .08f, .77f, .92f, .92f),
                $"여왕의 대관식  ·  {clears}/{total}", 30, Color.white, heading: true);
            var episode = CurrentRpgEpisode;
            string objective = episode == null ? "모든 이야기를 마쳤습니다. 아카데미아를 자유롭게 둘러보세요." :
                $"{episode.chapterTitle}\n{episode.title}\n{CampaignRegions[CampaignRegionForEpisode(clears + 1)].Name}";
            HudLabel(Area(paper, "Objective", .08f, .35f, .92f, .74f), objective, 24, RpgUiStyle.Cyan);
            if (clears > 0)
                RpgButton(Area(paper, "Recap", .08f, .13f, .47f, .29f), "지난 이야기", _app.OpenRpgRecap);
            RpgButton(Area(paper, "BackToField", .53f, .13f, .92f, .29f), "필드로 돌아가기", () => Show(Page.Field));
        }

        internal static int StageOpponentId(StoryEpisodeData episode) =>
            episode.playAsCharacterId > 0 && episode.playAsCharacterId == episode.charId ? 0 : episode.charId;

        private string CampaignSdPath(int id) =>
            RpgArt + $"Characters_SD/sd_{(id == 0 ? "protagonist" : RpgArtKey(id))}.png";

        private string CampaignBustPath(int id) => IsGxCampaign && id == 0
            ? RpgArt + "Standing/full_campaign_protagonist.png"
            : RpgArt + (IsGxCampaign ? "Standing/" : "Characters_Standing/") +
                $"full_{(id == 0 ? "protagonist" : RpgArtKey(id))}.png";
    }
}
