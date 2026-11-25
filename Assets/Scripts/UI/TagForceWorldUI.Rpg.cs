using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        internal const string RpgPracticeId = "5ds-rpg";
        private const string RpgArt = "TagForce/5DS/";
        private const string RpgIcons = "TagForce/UI/";
        private const string RpgFieldHelp = "방향키 · 스틱 이동   선택 조사/대화   위쪽 버튼 메뉴";
        private const string RpgRegionPreference = "ygo.5ds-rpg.last-region";
        private const float RpgSceneRadius = .096f;
        private static readonly Vector2 RpgArrival = new(.50f, .17f);

        internal readonly struct RpgRegion
        {
            internal readonly string Id, Name;
            internal readonly int RequiredEpisode;

            internal RpgRegion(string id, string name, int requiredEpisode)
            {
                Id = id;
                Name = name;
                RequiredEpisode = requiredEpisode;
            }

            internal string Background => RpgArt + "Maps/" + Id + ".png";
            internal bool IsOpen(int clears) => clears >= RequiredEpisode;
        }

        internal static readonly RpgRegion[] RpgRegions =
        {
            new("spot_kai_garage", "카이의 정비소", 0),
            new("spot_satellite_hideout", "새틀라이트 은신처", 0),
            new("spot_facility_prison", "시큐리티 수용소", 6),
            new("spot_poppo_time", "폿포 타임", 9),
            new("spot_fortune_stadium", "포춘 컵 스타디움", 11),
            new("spot_old_moment", "구 모멘트 폐허", 19),
            new("spot_daedalus_bridge", "다이달로스 브리지", 27),
            new("spot_wrgp_circuit", "WRGP 서킷", 35),
            new("spot_ark_cradle", "아크 크레이들", 44),
        };

        private static readonly Vector2[] RpgNpcSlots = { new(.48f, .40f), new(.23f, .33f), new(.74f, .36f) };
        private readonly List<RectTransform> _questMarkers = new();
        private readonly Dictionary<int, RectTransform> _rpgResidents = new();
        private RectTransform _rpgNavigation;
        private RectTransform _rpgFieldHud;
        private StoryScriptRoot _rpgCampaign;
        private Dictionary<int, PresetDeckInfo> _rpgWorldDecks;
        private StoryEpisodeData _resident;
        private StoryEpisodeData _rpgNearbyScene;
        private string _rpgLastPlayedSceneId;
        private Text _rpgToast;
        private float _rpgToastTime;
        private int _rpgLocation, _observedRpgClears;
        private bool _rpgField;

        internal int RpgClears => CountClearedRpgEpisodes(_rpgCampaign, CollectionProgress.IsStoryEpisodeCleared);

        internal static int CountClearedRpgEpisodes(StoryScriptRoot script, Func<string, bool> isCleared)
        {
            int cleared = 0;
            foreach (var episode in script?.episodes ?? Array.Empty<StoryEpisodeData>())
            {
                if (!isCleared(episode.id)) break;
                cleared++;
            }
            return cleared;
        }

        internal static int RegionForEpisode(int episodeNumber)
        {
            if (episodeNumber <= 2) return 0;
            for (int i = RpgRegions.Length - 1; i >= 2; i--)
                if (episodeNumber > RpgRegions[i].RequiredEpisode) return i;
            return 1;
        }

        internal static string NewlyUnlockedRegions(int before, int after) => string.Join(" · ",
            RpgRegions.Where(region => !region.IsOpen(before) && region.IsOpen(after)).Select(region => region.Name));

        internal void PrepareRpgField()
        {
            _rpgField = true;
            _rpgCampaign ??= StoryModeUI.LoadRpgScript(Application.streamingAssetsPath);
            _rpgWorldDecks ??= StoryModeUI.LoadRpgDecks(Application.streamingAssetsPath);
            _observedRpgClears = RpgClears;
            _rpgLocation = Array.FindIndex(RpgRegions, region => region.Id == PlayerPrefs.GetString(RpgRegionPreference));
            if (_rpgLocation < 0 || !RpgRegions[_rpgLocation].IsOpen(_observedRpgClears))
                _rpgLocation = RegionForEpisode(_observedRpgClears + 1);
            _position = RpgArrival;
            _resident = null;
            _rpgQuestResident = null;
            _rpgLastPlayedSceneId = null;
        }

        internal bool CanStartRpgEpisode(int index) =>
            _rpgField && CanWalk && _rpgCampaign?.episodes != null && index >= 0 &&
            index == RpgClears && index < _rpgCampaign.episodes.Length && RegionForEpisode(index + 1) == _rpgLocation;

        private RectTransform BuildRpgNavigation(Transform parent)
        {
            var bar = Area(parent, "RpgNavigation", .05f, .027f, .95f, .101f);
            Btn(Area(bar, "Map", 0f, 0f, .235f, 1f), "월드맵", () => Show(Page.Map));
            Btn(Area(bar, "Quests", .255f, 0f, .490f, 1f), "의뢰 수첩", () => Show(Page.Ledger));
            Btn(Area(bar, "Decks", .510f, 0f, .745f, 1f), "덱 편집", _app.OpenTagForceDeckEditor);
            Btn(Area(bar, "Menu", .765f, 0f, 1f, 1f), "메인 메뉴", () => _app.ShowScreen(AppScreen.MainMenu));
            return bar;
        }

        private void ShowRpgPage(Page page)
        {
            _navigation.gameObject.SetActive(false);
            _rpgNavigation.gameObject.SetActive(true);
            _header.text = $"5D's RPG 월드 · {RpgRegions[_rpgLocation].Name} · {CollectionProgress.Balance:N0} DP";
            DuelAudio.PlayTagForceBgm(_rpgLocation is 3 or 4 or 5);
            switch (page)
            {
                case Page.Map: BuildRpgMap(); break;
                case Page.Field:
                    _rpgInspecting = false;
                    BuildRpgField();
                    break;
                case Page.Talk: BuildRpgTalk(); break;
                case Page.Duel: BuildRpgPractice(); break;
                case Page.Ledger: BuildRpgQuestJournal(); break;
            }

            int clears = RpgClears;
            string opened = NewlyUnlockedRegions(_observedRpgClears, clears);
            _observedRpgClears = clears;
            if (opened.Length > 0)
            {
                var toast = Area(_body, "RegionUnlockToast", .19f, .79f, .81f, .93f);
                Tint(toast, new Color(.118f, .10f, .046f, .94f));
                _rpgToast = Label(toast, $"신규 지역 해금: {opened}", 21);
                _rpgToast.color = DuelUiKit.GoldAccent;
                _rpgToastTime = 4.5f;
            }
        }

        private void BuildRpgMap()
        {
            Backdrop(null, new Color(.035f, .083f, .135f));
            int cleared = RpgClears;
            int target = RegionForEpisode(cleared + 1);
            for (int i = 0; i < RpgRegions.Length; i++)
            {
                int index = i;
                var region = RpgRegions[i];
                float x = .025f + i % 3 * .324f;
                float y = .678f - i / 3 * .323f;
                var tile = DuelUiKit.CreateMenuPanel(_body, region.Id,
                    new Vector2(x, y), new Vector2(x + .30f, y + .292f), i == target && cleared < 50);
                bool open = region.IsOpen(cleared);
                var poster = _images.Get(region.Background);
                DuelUiKit.AddImage(Area(tile, "MapPreview", .036f, .292f, .964f, .947f).gameObject, poster,
                    poster == null ? new Color(.09f, .18f, .24f) : open ? Color.white : new Color(.24f, .26f, .30f),
                    false, false);
                if (!open)
                    DrawRpgIcon(Area(tile, "MapLock", .413f, .47f, .587f, .83f), "icon_map_lock.png", "잠김", DuelUiKit.GoldAccent);
                var travel = Btn(Area(tile, "Travel", .037f, .047f, .965f, .273f),
                    open ? i == _rpgLocation ? $"{region.Name} · 현재" : region.Name :
                        $"{region.Name} · {region.RequiredEpisode}화 클리어", () => EnterRpgRegion(index));
                travel.interactable = open;
            }
        }

        internal bool EnterRpgRegion(int index) => TravelRpgRegion(index, RpgArrival);

        private bool TravelRpgRegion(int index, Vector2 arrival)
        {
            if (!_rpgField || _rpgDialogueCast != null || index < 0 || index >= RpgRegions.Length ||
                !RpgRegions[index].IsOpen(RpgClears))
                return false;
            if (index != _rpgLocation) _position = arrival;
            _rpgLocation = index;
            PlayerPrefs.SetString(RpgRegionPreference, RpgRegions[index].Id);
            PlayerPrefs.Save();
            Show(Page.Field);
            return true;
        }

        private void BuildRpgField()
        {
            var floor = BuildRpgCamera(RpgRegions[_rpgLocation].Background, _page == Page.Field);
            floor.raycastTarget = true;
            floor.gameObject.AddComponent<TagForceFloorTap>().Click = target => MoveTo(target);
            BuildRpgAtmosphere();
            _rpgFieldHud = DuelUiKit.FillRect("FieldHud", _body);
            var status = Area(_rpgFieldHud, "QuestTrack", .02f, .921f, .77f, .986f);
            Tint(status, new Color(.014f, .03f, .065f, .90f));
            var episodes = _rpgCampaign?.episodes ?? Array.Empty<StoryEpisodeData>();
            int cleared = RpgClears;
            bool complete = cleared == episodes.Length;
            int targetRegion = RegionForEpisode(cleared + 1);
            string quest = episodes.Length == 0 ? "RPG 대본을 불러오지 못했습니다." :
                complete ? "50화 스토리 완료 · 자유 탐험" :
                $"{cleared + 1:00}화 · {episodes[cleared].title}  ·  {RpgRegions[targetRegion].Name}";
            Label(status, quest, 20, TextAnchor.MiddleLeft).color = DuelUiKit.GoldAccent;
            Btn(Area(_rpgFieldHud, "FieldMap", .80f, .872f, .98f, .986f), "월드맵", () => Show(Page.Map));
            _fieldControls = Label(Area(_rpgFieldHud, "FieldControls", .02f, .012f, .98f, .078f), RpgFieldHelp, 15,
                TextAnchor.MiddleRight);

            StoryEpisodeData questNpc = !complete && _rpgLocation == targetRegion ? episodes[cleared] : null;
            var sideQuest = RpgSideQuest.All.FirstOrDefault(item => item.Region == RpgRegions[_rpgLocation].Id);
            if (_page == Page.Field) BuildRpgExploration(cleared);
            _rpgNearbyScene = questNpc?.sceneOnApproach == true ? questNpc : null;
            if (questNpc != null)
                AddRpgResident(questNpc, RpgNpcSlots[0], true, () => _app.OpenRpgFieldEpisode(cleared));
            var locals = episodes.Where((episode, i) => RegionForEpisode(i + 1) == _rpgLocation)
                .Concat(episodes.Take(cleared)).Concat(episodes.Take(2))
                .Where(episode => episode.charId != questNpc?.charId && episode.charId != sideQuest?.Giver &&
                    _rpgWorldDecks.ContainsKey(episode.deckId))
                .GroupBy(episode => episode.charId).Select(group => group.First()).Take(2).ToArray();
            for (int i = 0; i < locals.Length; i++)
            {
                var resident = locals[i];
                AddRpgResident(resident, RpgNpcSlots[i + 1], false, () =>
                {
                    _resident = resident;
                    _rpgQuestResident = null;
                    Show(Page.Talk);
                });
            }
            if (sideQuest != null) AddRpgSideQuest(sideQuest);
            _player = Anchor(_rpgSceneSpace, "Player", _position, new Vector2(91f, 116f));
            _playerSprite = WalkerSprite(_player, RpgArt + "Characters_SD/sd_protagonist.png", "카이");
        }

        private void CheckRpgSceneEncounter(Vector2 previous)
        {
            if (_rpgNearbyScene == null || _rpgNearbyScene.id == _rpgLastPlayedSceneId || !CanWalk) return;
            var distance = RpgNpcSlots[0] - previous;
            var step = _position - previous;
            if (step.sqrMagnitude < 1e-8f || distance.sqrMagnitude <= RpgSceneRadius * RpgSceneRadius) return;
            float closest = Mathf.Clamp01(Vector2.Dot(distance, step) / step.sqrMagnitude);
            if ((distance - step * closest).sqrMagnitude <= RpgSceneRadius * RpgSceneRadius)
                _app.OpenRpgFieldEpisode(RpgClears);
        }

        private void AddRpgResident(StoryEpisodeData resident, Vector2 spot, bool story, Action interact) =>
            AddRpgResident(resident.charId, resident.opponentName,
                story ? "MainQuestNpc" : $"Resident_{resident.charId}", spot, story, interact);

        private void AddRpgResident(int id, string name, string node, Vector2 spot, bool story, Action interact)
        {
            var actor = Anchor(_rpgSceneSpace, node, spot, new Vector2(133f, 117f));
            _rpgResidents[id] = actor;
            var art = Area(actor, "SD", .08f, .22f, .92f, 1f);
            BuildRpgActorArt(art, RpgArt + $"Characters_SD/sd_{id}.png", name);
            void Approach() => MoveTo(actor.anchorMin - new Vector2(0f, .06f), interact);
            BindPortrait(art, Approach);
            var marker = Anchor(actor, story ? "MainQuestMarker" : "ResidentMarker", new Vector2(.50f, 1.07f),
                new Vector2(45f, 45f));
            DrawRpgIcon(marker, story ? "icon_quest_main.png" : "icon_quest_sub.png", story ? "!" : "…",
                story ? DuelUiKit.GoldAccent : new Color(.40f, .78f, 1f));
            if (story) _questMarkers.Add(marker);
            Btn(Area(actor, "Interact", 0f, 0f, 1f, .265f), name, Approach, 15);
            _interactions.Add((actor, interact));
        }

        private void DrawRpgIcon(RectTransform anchor, string name, string fallback, Color color)
        {
            var icon = _images.Get(RpgIcons + name);
            if (icon != null) DuelUiKit.AddImage(anchor.gameObject, icon, Color.white, true, false);
            else Label(anchor, fallback, 21).color = color;
        }

        private void BuildRpgTalk()
        {
            if (_rpgInspecting) { BuildRpgLandmarkTalk(); return; }
            if (_resident == null && _rpgQuestResident == null) { Show(Page.Field); return; }
            BeginRpgTalkBackdrop();
            var quest = _rpgQuestResident;
            string name = quest == null ? _resident.opponentName : RpgQuestName(quest);
            if (_rpgResidents.TryGetValue(quest?.Giver ?? _resident.charId, out var speaker))
            {
                var direction = speaker.anchorMin - _position;
                PoseRpgWalker(_player, _playerSprite, _position, Vector2.zero, false, direction);
                PoseRpgWalker(speaker, speaker.Find("SD") as RectTransform, speaker.anchorMin, Vector2.zero, false, -direction);
                speaker.Find("ResidentMarker")?.gameObject.SetActive(false);
                speaker.Find("QuestBadge")?.gameObject.SetActive(false);
                BuildRpgSpeakerCue(speaker, out _);
            }
            var panel = BuildRpgConversation(name, quest != null ? RpgQuestDialogue(quest) : "무슨 일로 왔어?",
                out var conversation);
            var actions = new List<(string Id, string Label, Action Select)>
            {
                ("TalkDaily", "일상 대화", () => conversation.text = quest != null
                    ? RpgQuestDialogue(quest) : $"이곳은 {RpgRegions[_rpgLocation].Name}이야. 덱을 시험해 보고 싶으면 한 판 하자."),
            };
            if (quest == null) actions.Add(("Practice", "연습 듀얼", () => Show(Page.Duel)));
            else AppendRpgQuestActions(quest, actions);
            actions.Add(("Leave", "돌아가기", () => Show(Page.Field)));
            AddRpgConversationActions(panel, actions);
        }

        private void BeginRpgTalkBackdrop()
        {
            BuildRpgField();
            _rpgFieldHud.gameObject.SetActive(false);
            _rpgNavigation.gameObject.SetActive(false);
            var fieldInput = _rpgViewport.gameObject.AddComponent<CanvasGroup>();
            fieldInput.interactable = fieldInput.blocksRaycasts = false;
            var shade = DuelUiKit.FillRect("ResidentTalkShade", _body);
            DuelUiKit.AddImage(shade.gameObject, DuelUiKit.WhiteSprite, new Color(.01f, .03f, .06f, .14f));
        }

        private RectTransform BuildRpgConversation(string name, string line, out Text conversation)
        {
            var panel = DuelUiKit.CreateMenuPanel(_body, "ResidentChoices",
                new Vector2(.065f, .025f), new Vector2(.935f, .35f));
            Label(Area(panel, "Name", .035f, .72f, .96f, .94f), name, 22, TextAnchor.MiddleLeft).color = DuelUiKit.GoldAccent;
            conversation = Label(Area(panel, "DailyLine", .035f, .335f, .962f, .735f), line, 21, TextAnchor.MiddleLeft);
            return panel;
        }

        private static Button AddRpgConversationActions(RectTransform panel, List<(string Id, string Label, Action Select)> actions)
        {
            Button first = null;
            float width = .93f / actions.Count;
            for (int i = 0; i < actions.Count; i++)
            {
                float left = .035f + i * width;
                var (id, label, select) = actions[i];
                var button = Btn(Area(panel, id, left, .065f, left + width - .012f, .278f), label, select);
                first ??= button;
            }
            DeckPadUi.LimitKeyboardNavigation(panel);
            EventSystem.current?.SetSelectedGameObject(first?.gameObject);
            return first;
        }

        private void BuildRpgPractice()
        {
            if (_resident == null || !_rpgWorldDecks.TryGetValue(_resident.deckId, out var rival))
            {
                Show(Page.Field);
                return;
            }
            var candidates = DeckRepository.UserDecks.Where(deck => GameAppController.CanStartWithDeck(deck, out _)).ToArray();
            var preferred = _deckId ?? CollectionProgress.LastDuelDeckId;
            var selected = candidates.FirstOrDefault(deck => deck.id == preferred) ?? candidates.FirstOrDefault();
            Label(Area(_body, "Opponent", .05f, .82f, .95f, .97f), $"{_resident.opponentName} · {rival.title}", 23);
            var list = List("YourDeck", .14f, .19f, .86f, .79f);
            foreach (var deck in candidates)
                Row(list, (deck == selected ? "✓ " : "") + deck.name, () =>
                {
                    _deckId = deck.id;
                    Show(Page.Duel);
                });
            if (selected == null) Row(list, DuelDeckAvailability.ExplainEmptyList(preferred), null);
            var start = Btn(Area(_body, "StartPractice", .36f, .028f, .64f, .152f), "연습 듀얼 시작", () =>
            {
                var persona = new TagForcePersona { personaId = $"rpg_{_resident.charId}", baseCharId = _resident.charId,
                    displayName = _resident.opponentName };
                var form = new TagForceForm { formCharId = _resident.charId, formName = _resident.opponentName };
                _app.StartTagForceDuel(new TagForceDuel(RpgPracticeId, persona, form, null, rival, 1,
                    null, null, false, 0), selected);
            });
            start.interactable = selected != null;
        }

        private void UpdateRpgField(float delta)
        {
            UpdateRpgAtmosphere(delta);
            UpdateRpgExploration(delta);
            if (_rpgToast != null && (_rpgToastTime -= delta) <= 0f)
            {
                _rpgToast.transform.parent.gameObject.SetActive(false);
                _rpgToast = null;
            }
            float phase = Time.unscaledTime * 3.6f;
            foreach (var marker in _questMarkers)
                marker.anchoredPosition = new Vector2(0f, 5.5f * Mathf.Sin(phase));
        }
    }
}
