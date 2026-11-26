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
        private bool IsGxHarbor(int region) => IsGxCampaign && CampaignRegions[region].Id == "spot_harbor_beach";

        // Convex footprints follow the floor plane in each GX background. Every straight path between
        // two grounded points stays inside the same footprint, including patrols and stage movement.
        private static readonly Dictionary<string, Vector2[]> GxGround = new()
        {
            ["spot_abandoned_dorm"] = new[]
            {
                new Vector2(.23f, .39f), new Vector2(.4f, .31f), new Vector2(.62f, .34f), new Vector2(.74f, .46f),
                new Vector2(.54f, .49f), new Vector2(.32f, .47f),
            },
            ["spot_blue_arena"] = new[]
            {
                new Vector2(.34f, .35f), new Vector2(.57f, .3f), new Vector2(.68f, .33f), new Vector2(.77f, .54f),
                new Vector2(.62f, .575f), new Vector2(.44f, .49f),
            },
            ["spot_dark_dimension"] = new[]
            {
                new Vector2(.23f, .44f), new Vector2(.41f, .32f), new Vector2(.62f, .32f), new Vector2(.77f, .4f),
                new Vector2(.75f, .6f), new Vector2(.6f, .67f), new Vector2(.38f, .65f), new Vector2(.23f, .57f),
            },
            ["spot_darkness_altar"] = new[]
            {
                new Vector2(.24f, .37f), new Vector2(.36f, .285f), new Vector2(.65f, .285f), new Vector2(.77f, .39f),
                new Vector2(.76f, .53f), new Vector2(.64f, .66f), new Vector2(.37f, .66f), new Vector2(.25f, .54f),
            },
            ["spot_forest_duel_ground"] = new[]
            {
                new Vector2(.44f, .17f), new Vector2(.56f, .17f), new Vector2(.7f, .39f), new Vector2(.69f, .6f),
                new Vector2(.59f, .67f), new Vector2(.4f, .67f), new Vector2(.3f, .54f), new Vector2(.3f, .4f),
            },
            ["spot_harbor_beach"] = new[]
            {
                new Vector2(.14f, .285f), new Vector2(.22f, .205f), new Vector2(.515f, .195f), new Vector2(.525f, .29f),
                new Vector2(.47f, .355f), new Vector2(.36f, .393f), new Vector2(.2f, .375f),
            },
            ["spot_infirmary"] = new[]
            {
                new Vector2(.28f, .115f), new Vector2(.73f, .115f), new Vector2(.7f, .41f), new Vector2(.625f, .7f),
                new Vector2(.41f, .7f), new Vector2(.32f, .49f),
            },
            ["spot_library"] = new[]
            {
                new Vector2(.455f, .105f), new Vector2(.545f, .105f), new Vector2(.535f, .73f), new Vector2(.473f, .73f),
            },
            ["spot_main_academy"] = new[]
            {
                new Vector2(.125f, .24f), new Vector2(.3f, .115f), new Vector2(.56f, .115f), new Vector2(.78f, .37f),
                new Vector2(.735f, .5f), new Vector2(.56f, .535f), new Vector2(.365f, .47f),
            },
            ["spot_red_dorm"] = new[]
            {
                new Vector2(.095f, .23f), new Vector2(.235f, .125f), new Vector2(.81f, .125f), new Vector2(.855f, .25f),
                new Vector2(.82f, .43f), new Vector2(.67f, .505f), new Vector2(.445f, .45f),
            },
            ["spot_vending_bench"] = new[]
            {
                new Vector2(.175f, .27f), new Vector2(.41f, .16f), new Vector2(.84f, .19f), new Vector2(.89f, .255f),
                new Vector2(.76f, .385f), new Vector2(.585f, .465f), new Vector2(.4f, .43f),
            },
            ["spot_volcano_arena"] = new[]
            {
                new Vector2(.345f, .365f), new Vector2(.66f, .365f), new Vector2(.695f, .425f), new Vector2(.695f, .595f),
                new Vector2(.62f, .69f), new Vector2(.39f, .69f), new Vector2(.315f, .595f), new Vector2(.315f, .425f),
            },
        };

        private Vector2 RpgGroundPoint(int region, Vector2 position)
        {
            if (!IsGxCampaign) return position;
            var polygon = GxGround[CampaignRegions[region].Id];
            var nearest = position;
            float distance = float.PositiveInfinity;
            bool inside = true;
            // X and Y are normalized to the artwork. Measure distances in its 16:9 proportions.
            var scale = new Vector2(1f, 9f / 16f);
            for (int i = 0; i < polygon.Length; ++i)
            {
                var start = polygon[i];
                var edge = polygon[(i + 1) % polygon.Length] - start;
                var offset = position - start;
                if (edge.x * offset.y - edge.y * offset.x < 0f) inside = false;

                var scaledEdge = Vector2.Scale(edge, scale);
                float along = Mathf.Clamp01(Vector2.Dot(Vector2.Scale(offset, scale), scaledEdge) / scaledEdge.sqrMagnitude);
                var candidate = start + edge * along;
                float squared = Vector2.Scale(position - candidate, scale).sqrMagnitude;
                if (squared >= distance) continue;
                nearest = candidate;
                distance = squared;
            }
            return inside ? position : nearest;
        }

        private Vector2 RpgGroundMark(int region, Vector2 position)
        {
            if (!IsGxCampaign) return position;
            var polygon = GxGround[CampaignRegions[region].Id];
            var center = Vector2.zero;
            foreach (var vertex in polygon) center += vertex;
            return Vector2.Lerp(RpgGroundPoint(region, position), center / polygon.Length, .12f);
        }

        private Vector2 RpgFieldArrival(int region)
        {
            var point = IsGxCampaign ? CampaignRegions[region].Id switch
            {
                "spot_main_academy" => new Vector2(.50f, .16f),
                "spot_harbor_beach" => new Vector2(.32f, .23f),
                _ => RpgArrival,
            } : RpgArrival;
            return RpgGroundMark(region, point);
        }

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
            var marker = spot.spotId switch
            {
                "spot_harbor_beach" => new Vector2(.435f, .285f),
                "spot_library" => new Vector2(.50f, .27f),
                _ => new Vector2(.67f, .30f),
            };
            return ("지역 안내", RpgGroundMark(index, marker), spot.subtitle);
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
                if (entry.unlockAfter > clears || entry.charId == active?.charId || entry.charId == RpgPlayerId) continue;
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
                bool narrowAisle = spot.spotId == "spot_library";
                var point = narrowAisle ? new Vector2(.50f, .18f + .07f * index) :
                    new Vector2(index % 2 == 0 ? .21f : .75f, .30f + .08f * (index / 2));
                var position = RpgGroundMark(_rpgLocation, point);
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
            HudLabel(Area(paper, "Title", .08f, .815f, .92f, .945f),
                $"여왕의 대관식  ·  {clears}/{total}", 30, Color.white, heading: true);
            var episode = CurrentRpgEpisode;
            string objective = episode == null ? "모든 이야기를 마쳤습니다. 아카데미아를 자유롭게 둘러보세요." :
                $"{episode.chapterTitle}\n{episode.title}\n{CampaignRegions[CampaignRegionForEpisode(clears + 1)].Name}";
            HudLabel(Area(paper, "Objective", .08f, .619f, .92f, .81f), objective, 23, RpgUiStyle.Cyan);
            HudLabel(Area(paper, "MemoryTitle", .08f, .493f, .92f, .600f),
                "아스카와 함께한 이야기", 24, Color.white, heading: true);
            var memoryList = Area(paper, "Memories", .08f, .215f, .92f, .484f);
            var memories = _rpgCampaign.episodes.Take(clears).SelectMany((entry, i) => entry.preDialogues
                .Concat(entry.postWinDialogues ?? Array.Empty<StoryDialogueLine>())
                .Where(line => !string.IsNullOrWhiteSpace(line.memoryTitle) && !string.IsNullOrWhiteSpace(line.cutscene))
                .Select(line => (Index: i, Cg: line.cutscene, Title: line.memoryTitle))).ToArray();
            if (memories.Length == 0)
                HudLabel(memoryList, "이야기를 진행하면 함께한 장면을 다시 볼 수 있습니다.", 20, RpgUiStyle.Cyan);
            float rowHeight = 1f / Mathf.Max(2, (memories.Length + 1) / 2);
            for (int i = 0; i < memories.Length; i++)
            {
                var memory = memories[i];
                float left = i % 2 * .52f;
                float top = 1f - i / 2 * rowHeight;
                RpgButton(Area(memoryList, memory.Cg, left, top - rowHeight * .85f, left + .48f, top), memory.Title,
                    () => _app.OpenRpgRecap(memory.Index, memory.Cg), 20);
            }
            if (clears > 0)
                RpgButton(Area(paper, "Recap", .08f, .06f, .47f, .179f), "지난 이야기", _app.OpenRpgRecap);
            RpgButton(Area(paper, "BackToField", .53f, .06f, .92f, .179f), "필드로 돌아가기", () => Show(Page.Field));
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
