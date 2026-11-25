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
        private const string RpgRegionPreference = "ygo.5ds-rpg.last-region";
        private const float RpgSceneRadius = .096f;
        private static readonly Vector2 RpgArrival = new(.50f, .20f);

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
            internal string FieldBackground => RpgArt + "Maps/" + Id + "_field.png";
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
        private readonly RectTransform[] _rpgNavItems = new RectTransform[4];
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
        private bool _rpgHeaderStyled;

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

        internal void PrepareRpgField(bool openIntro = false)
        {
            _rpgField = true;
            _rpgCampaign ??= StoryModeUI.LoadRpgScript(Application.streamingAssetsPath);
            _rpgWorldDecks ??= StoryModeUI.LoadRpgDecks(Application.streamingAssetsPath);
            _observedRpgClears = RpgClears;
            _rpgIntroOnOpen = openIntro && _observedRpgClears == 0 && !CollectionProgress.RpgIntroSeen;
            _rpgLocation = Array.FindIndex(RpgRegions, region => region.Id == PlayerPrefs.GetString(RpgRegionPreference));
            if (_rpgLocation < 0 || !RpgRegions[_rpgLocation].IsOpen(_observedRpgClears))
                _rpgLocation = RegionForEpisode(_observedRpgClears + 1);
            if (_rpgIntroOnOpen) _rpgLocation = RpgGarage;
            _rpgAnnounceArrival = openIntro;
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
            Add(0, "Map", "월드맵", () => Show(Page.Map));
            Add(1, "Quests", "의뢰 수첩", () => Show(Page.Ledger));
            Add(2, "Decks", "덱 편집", _app.OpenTagForceDeckEditor);
            Add(3, "Menu", "메인 메뉴", () => _app.ShowScreen(AppScreen.MainMenu));
            return bar;

            void Add(int index, string id, string caption, Action action)
            {
                _rpgNavItems[index] = DuelUiKit.FillRect(id, bar);
                RpgButton(_rpgNavItems[index], caption, action, 20);
            }
        }

        private void LayoutRpgField(bool field)
        {
            _body.anchorMin = field ? new Vector2(.012f, .012f) : new Vector2(.05f, .174f);
            _body.anchorMax = field ? new Vector2(.988f, .988f) : new Vector2(.95f, .895f);
            _status.rectTransform.anchorMin = field ? new Vector2(.025f, .105f) : new Vector2(.05f, .112f);
            _status.rectTransform.anchorMax = field ? new Vector2(.46f, .151f) : new Vector2(.95f, .168f);
            _header.gameObject.SetActive(!field);
            _back.gameObject.SetActive(!field);
            _rpgNavigation.anchorMin = field ? new Vector2(.777f, .113f) : new Vector2(.05f, .027f);
            _rpgNavigation.anchorMax = field ? new Vector2(.973f, .409f) : new Vector2(.95f, .101f);
            for (int i = 0; i < _rpgNavItems.Length; i++)
            {
                _rpgNavItems[i].anchorMin = field ? new Vector2(0f, .773f - .254f * i) : new Vector2(.255f * i, 0f);
                _rpgNavItems[i].anchorMax = field ? new Vector2(1f, 1f - .254f * i) : new Vector2(.235f + .255f * i, 1f);
            }
        }

        private static Button RpgButton(RectTransform area, string text, Action action, int size = 19,
            bool primary = false, TagForceSfx? sound = TagForceSfx.Confirm)
        {
            var button = Btn(area, text, action, size, sound);
            RpgUiStyle.Control(button, primary);
            return button;
        }

        private void UpdateRpgWorldChrome()
        {
            if (_rpgHeaderStyled == _rpgField) return;
            _rpgHeaderStyled = _rpgField;
            if (_rpgField)
            {
                RpgUiStyle.Type(_header, strong: true);
                RpgUiStyle.Type(_status);
                RpgUiStyle.Control(_back);
                return;
            }
            var image = (Image)_back.targetGraphic;
            image.sprite = DuelUiKit.WhiteSprite;
            image.type = Image.Type.Simple;
            image.color = new Color(.12f, .31f, .39f);
            _back.colors = ColorBlock.defaultColorBlock;
            var backLabel = _back.GetComponentInChildren<Text>();
            _header.font = _status.font = backLabel.font = DuelUiKit.FontKR;
            _header.fontStyle = _status.fontStyle = backLabel.fontStyle = FontStyle.Bold;
            backLabel.color = new Color(.95f, .97f, .93f);
        }

        private void ShowRpgPage(Page page)
        {
            _navigation.gameObject.SetActive(false);
            _rpgNavigation.gameObject.SetActive(page != Page.Field);
            _header.text = $"5D's RPG 월드 · {RpgRegions[_rpgLocation].Name}";
            DuelAudio.PlayTagForceBgm(_rpgLocation is 3 or 4 or 5);
            switch (page)
            {
                case Page.Map: BuildRpgMap(); break;
                case Page.Field:
                    _rpgInspecting = false;
                    _rpgWorkbenchServices = false;
                    _rpgServiceSpeaker = null;
                    BuildRpgField();
                    if (_rpgAnnounceArrival) BuildRpgArrivalBanner();
                    if (_rpgRankNotice > 0) BuildRpgRankNotice();
                    break;
                case Page.Prologue: BuildRpgIntro(false); break;
                case Page.Guide: BuildRpgIntro(true); break;
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
                RpgUiStyle.Panel(toast, accent: true);
                _rpgToast = HudLabel(toast, $"신규 지역 개방!  ·  {opened}", 21, DuelUiKit.GoldAccent,
                    align: TextAnchor.MiddleCenter, heading: true);
                _rpgToastTime = 4.5f;
            }
        }

        private void BuildRpgMap()
        {
            BuildRpgMenuBackdrop();
            int cleared = RpgClears;
            int target = RegionForEpisode(cleared + 1);
            for (int i = 0; i < RpgRegions.Length; i++)
            {
                int index = i;
                var region = RpgRegions[i];
                float x = .025f + i % 3 * .324f;
                float y = .678f - i / 3 * .323f;
                var tile = Area(_body, region.Id, x, y, x + .30f, y + .292f);
                RpgUiStyle.Panel(tile, accent: i == target && cleared < 50);
                bool open = region.IsOpen(cleared);
                var poster = GetRpgFieldArt(region);
                DuelUiKit.AddImage(Area(tile, "MapPreview", .036f, .292f, .964f, .947f).gameObject, poster,
                    poster == null ? new Color(.09f, .18f, .24f) : open ? Color.white : new Color(.24f, .26f, .30f),
                    false, false);
                if (!open)
                    DrawRpgIcon(Area(tile, "MapLock", .413f, .47f, .587f, .83f), "icon_map_lock.png", "잠김", DuelUiKit.GoldAccent);
                var travel = RpgButton(Area(tile, "Travel", .037f, .047f, .965f, .273f),
                    open ? i == _rpgLocation ? $"{region.Name} · 현재" : region.Name :
                        $"{region.Name} · {region.RequiredEpisode}화 클리어", () =>
                    {
                        if (index == _rpgLocation) Show(Page.Field);
                        else BeginRpgTravel(index, RpgArrival);
                    }, primary: i == target && cleared < 50);
                travel.interactable = open;
            }
        }

        private void BuildRpgMenuBackdrop()
        {
            var backdrop = DuelUiKit.FillRect("Scenery", _body);
            DuelUiKit.AddImage(backdrop.gameObject, DuelUiChrome.Backdrop, Color.white, false, false);
        }

        internal bool EnterRpgRegion(int index) => TravelRpgRegion(index, RpgArrival);

        private bool TravelRpgRegion(int index, Vector2 arrival)
        {
            if (!_rpgField || _rpgDialogueCast != null || index < 0 || index >= RpgRegions.Length ||
                !RpgRegions[index].IsOpen(RpgClears))
                return false;
            if (index != _rpgLocation)
            {
                _position = arrival;
                _rpgAnnounceArrival = true;
            }
            _rpgLocation = index;
            PlayerPrefs.SetString(RpgRegionPreference, RpgRegions[index].Id);
            PlayerPrefs.Save();
            Show(Page.Field);
            return true;
        }

        private Sprite GetRpgFieldArt(RpgRegion region) => _images.Get(region.FieldBackground) ?? _images.Get(region.Background);

        private void BuildRpgField()
        {
            var floor = BuildRpgCamera(GetRpgFieldArt(RpgRegions[_rpgLocation]));
            floor.raycastTarget = true;
            floor.gameObject.AddComponent<TagForceFloorTap>().Click = target => MoveTo(target);
            BuildRpgAtmosphere();
            var episodes = _rpgCampaign?.episodes ?? Array.Empty<StoryEpisodeData>();
            int cleared = RpgClears;
            bool complete = cleared == episodes.Length;
            int targetRegion = RegionForEpisode(cleared + 1);

            StoryEpisodeData questNpc = !complete && _rpgLocation == targetRegion ? episodes[cleared] : null;
            var sideQuest = RpgSideQuest.All.FirstOrDefault(item => item.Region == RpgRegions[_rpgLocation].Id);
            if (_page == Page.Field)
            {
                BuildRpgExploration(cleared);
                BuildRpgCache();
            }
            _rpgNearbyScene = questNpc?.sceneOnApproach == true ? questNpc : null;
            if (questNpc != null)
                AddRpgResident(questNpc, RpgNpcSlots[0], true, () => _app.OpenRpgFieldEpisode(cleared));
            var locals = episodes.Where((episode, i) => RegionForEpisode(i + 1) == _rpgLocation)
                .Concat(episodes.Take(cleared)).Concat(episodes.Take(2))
                .Where(episode => episode.charId != questNpc?.charId && episode.charId != sideQuest?.Giver &&
                    (episode.charId != RpgPartnerId || _rpgLocation == RpgBridge) &&
                    (episode.charId != RpgShopkeeperId || _rpgLocation != RpgGarage) &&
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
            string sideObjective = sideQuest != null ? AddRpgSideQuest(sideQuest) : null;
            AddRpgShopkeeper();
            BuildRpgCompanion();
            _player = Anchor(_rpgSceneSpace, "Player", _position, RpgFieldActorSize);
            _playerSprite = WalkerSprite(_player, RpgArt + "Characters_SD/sd_protagonist.png", "카이");
            PoseRpgWalker(_player, _playerSprite, _position, Vector2.zero, false);
            OrderRpgFieldActors();
            BuildRpgFieldHud(cleared, targetRegion, episodes, sideObjective);
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
            var actor = Anchor(_rpgSceneSpace, node, spot, RpgFieldActorSize);
            _rpgResidents[id] = actor;
            var art = DuelUiKit.FillRect("SD", actor);
            var profile = BuildRpgActorArt(art, RpgArt + $"Characters_SD/sd_{id}.png", name);
            PoseRpgWalker(actor, profile, spot, Vector2.zero, false, Vector2.right * (_position.x - spot.x));
            void Approach() => MoveTo(actor.anchorMin - new Vector2(0f, .06f), interact);
            BindPortrait(art, Approach);
            var marker = Anchor(actor, story ? "MainQuestMarker" : "ResidentMarker", new Vector2(.50f, 1.07f),
                new Vector2(28f, 28f));
            DrawRpgIcon(marker, story ? "icon_quest_main.png" : "icon_quest_sub.png", story ? "!" : "…",
                story ? DuelUiKit.GoldAccent : new Color(.40f, .78f, 1f));
            _questMarkers.Add(marker);
            if (!story && _rpgLocation > 1 && node.StartsWith("Resident_", StringComparison.Ordinal) && _rpgRoamers.Count == 0)
                _rpgRoamers.Add((actor, spot));
            string verb = story ? "이야기 진행" : node == "RpgCardMerchant" ? "팩 구매" : "대화";
            RegisterRpgInteraction(actor, $"{name} · {verb}", Approach);
        }

        private void DrawRpgIcon(RectTransform anchor, string name, string fallback, Color color)
        {
            var icon = _images.Get(RpgIcons + name);
            if (icon != null) DuelUiKit.AddImage(anchor.gameObject, icon, Color.white, true, false);
            else HudLabel(anchor, fallback, 21, color, align: TextAnchor.MiddleCenter, heading: true);
        }

        private void BuildRpgTalk()
        {
            if (_rpgServiceSpeaker is int speakerId) { BuildRpgServiceTalk(speakerId); return; }
            if (_rpgWorkbenchServices) { BuildRpgWorkbenchServices(); return; }
            if (_rpgInspecting) { BuildRpgLandmarkTalk(); return; }
            if (_resident == null && _rpgQuestResident == null) { Show(Page.Field); return; }
            BeginRpgTalkBackdrop();
            var quest = _rpgQuestResident;
            string name = quest == null ? _resident.opponentName : RpgQuestName(quest);
            var panel = BuildRpgConversation(name, quest != null ? RpgQuestDialogue(quest) : "무슨 일로 왔어?",
                out var conversation, quest?.Giver ?? _resident.charId);
            var actions = new List<(string Id, string Label, Action Select)>
            {
                ("TalkDaily", "일상 대화", () => StartTypingDialogue(conversation, quest != null
                    ? RpgQuestDialogue(quest) : $"이곳은 {RpgRegions[_rpgLocation].Name}이야. 덱을 시험해 보고 싶으면 한 판 하자.")),
            };
            if (quest == null) actions.Add(("Practice", "연습 듀얼", () => Show(Page.Duel)));
            else AppendRpgQuestActions(quest, actions);
            actions.Add(("Leave", "돌아가기", () => Show(Page.Field)));
            AddRpgConversationActions(panel, actions);
        }

        private void BeginRpgTalkBackdrop()
        {
            BuildRpgField();
            HideRpgActorGraphics();
            _rpgSceneSpace.Find("SideQuestItem")?.gameObject.SetActive(false);
            _rpgFieldHud.gameObject.SetActive(false);
            _rpgNavigation.gameObject.SetActive(false);
            var fieldInput = _rpgViewport.gameObject.AddComponent<CanvasGroup>();
            fieldInput.interactable = fieldInput.blocksRaycasts = false;
            var shade = DuelUiKit.FillRect("ResidentTalkShade", _body);
            DuelUiKit.AddImage(shade.gameObject, DuelUiKit.WhiteSprite, new Color(.01f, .03f, .06f, .14f));
        }

        private RectTransform BuildRpgConversation(string name, string line, out Text conversation, int? speakerId = null)
        {
            if (speakerId is int id)
            {
                var bust = _images.GetBust(RpgArt + $"Characters_Standing/full_{id}.png") ??
                    DuelProfileArt.Opponent(DeckRepository.GetCharacter(id));
                if (bust != null && bust != DuelProfileArt.MissingOpponent)
                {
                    var portrait = Area(_body, "ConversationPortrait", .25f, .31f, .75f, .96f);
                    DuelUiKit.AddImage(portrait.gameObject, bust, Color.white, true, false);
                    UiEntranceMotion.Play(portrait.gameObject, .65f, .20f);
                }
            }
            var panel = Area(_body, "ResidentChoices", .065f, .025f, .935f, .35f);
            RpgUiStyle.Panel(panel);
            HudLabel(Area(panel, "Name", .04f, .74f, .96f, .94f), name, 24,
                new Color(.57f, .86f, .89f), heading: true);
            conversation = HudLabel(Area(panel, "DailyLine", .04f, .335f, .96f, .725f), line, 23,
                new Color(.90f, .94f, .97f));
            conversation.lineSpacing = 1.16f;
            StartTypingDialogue(conversation, line);
            UiEntranceMotion.Play(panel.gameObject, .84f, .18f);
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
                var button = RpgButton(Area(panel, id, left, .065f, left + width - .014f, .278f), label, select);
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
            BuildRpgMenuBackdrop();
            HudLabel(Area(_body, "Opponent", .05f, .82f, .95f, .97f), $"{_resident.opponentName} · {rival.title}",
                25, Color.white, align: TextAnchor.MiddleCenter, heading: true);
            var list = List("YourDeck", .14f, .19f, .86f, .79f);
            foreach (var deck in candidates)
            {
                var row = Row(list, (deck == selected ? "✓ " : "") + deck.name, () =>
                {
                    _deckId = deck.id;
                    Show(Page.Duel);
                }, 50);
                RpgUiStyle.Control(row.GetComponent<Button>(), deck == selected);
            }
            if (selected == null) RpgUiStyle.Type(Row(list, DuelDeckAvailability.ExplainEmptyList(preferred), null)
                .GetComponentInChildren<Text>());
            var start = RpgButton(Area(_body, "StartPractice", .36f, .028f, .64f, .152f), "연습 듀얼 시작", () =>
            {
                var persona = new TagForcePersona { personaId = $"rpg_{_resident.charId}", baseCharId = _resident.charId,
                    displayName = _resident.opponentName };
                var form = new TagForceForm { formCharId = _resident.charId, formName = _resident.opponentName };
                _app.StartTagForceDuel(new TagForceDuel(RpgPracticeId, persona, form, null, rival, 1,
                    null, null, false, 0), selected);
            }, primary: true);
            start.interactable = selected != null;
        }

        private void UpdateRpgField(float delta)
        {
            UpdateRpgAtmosphere(delta);
            UpdateRpgExploration(delta);
            UpdateRpgPresentation(delta);
            if (_page == Page.Talk) UpdateDialogue(delta);
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
