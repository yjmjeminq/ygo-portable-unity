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
        internal const int RpgAcademia = 0, RpgGarage = 1, RpgHideout = 2, RpgPrison = 3, RpgPoppo = 4,
            RpgStadium = 5, RpgMoment = 6, RpgCrashTown = 7, RpgBridge = 8, RpgWrgp = 9, RpgArk = 10;

        internal readonly struct RpgRegion
        {
            internal readonly string Id, Name;
            internal readonly int RequiredEpisode, AdventureSlot;
            private readonly string _lighting;

            internal RpgRegion(string id, string name, int requiredEpisode, int adventureSlot, string lighting = null)
            {
                Id = id;
                Name = name;
                RequiredEpisode = requiredEpisode;
                AdventureSlot = adventureSlot;
                _lighting = lighting;
            }

            internal string StoryBackdrop => $"Maps/{Id}.png";
            internal string LightingBackdrop => _lighting == null ? null : $"Maps/{Id}_{_lighting}.png";
            internal string Background => RpgArt + StoryBackdrop;
            internal string FieldBackground => RpgArt + "Maps/" + Id + "_field.png";
            internal bool IsOpen(int clears) => clears >= RequiredEpisode;
        }

        internal static readonly RpgRegion[] RpgRegions =
        {
            new("spot_duel_academia", "듀얼 아카데미아", 0, 9),
            new("spot_kai_garage", "카이의 정비소", 0, 0, "morning"),
            new("spot_satellite_hideout", "새틀라이트 은신처", 2, 1),
            new("spot_facility_prison", "시큐리티 수용소", 6, 2),
            new("spot_poppo_time", "폿포 타임", 8, 3, "spring"),
            new("spot_fortune_stadium", "포춘 컵 스타디움", 8, 4),
            new("spot_old_moment", "구 모멘트 폐허", 18, 5),
            new("spot_crash_town", "크래시 타운", 27, 10),
            new("spot_daedalus_bridge", "다이달로스 브리지", 27, 6, "daylight"),
            new("spot_wrgp_circuit", "WRGP 서킷", 34, 7, "blue_hour"),
            new("spot_ark_cradle", "아크 크레이들", 44, 8),
        };

        private static readonly int[] EpisodeRegionMap =
        {
            0, 0, 1, 2, 2, 2, 3, 3, 5, 5, // 01–10
            5, 5, 5, 4, 4, 4, 4, 4, 6, 6, // 11–20
            6, 6, 6, 6, 6, 6, 6, 7, 7, 4, // 21–30
            4, 1, 4, 1, 9, 9, 9, 9, 9, 9, // 31–40
            9, 9, 9, 9, 10, 10, 10, 10, 10, 8, // 41–50
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

        internal static int RegionForEpisode(int episodeNumber) =>
            EpisodeRegionMap[Mathf.Clamp(episodeNumber, 1, EpisodeRegionMap.Length) - 1];

        internal static string LightingForEpisode(RpgRegion region, int episodeNumber) =>
            region.Id == RpgRegions[RpgBridge].Id && episodeNumber >= EpisodeRegionMap.Length ? null : region.LightingBackdrop;

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
                RpgButton(_rpgNavItems[index], caption, () => { if (_rpgPendingRegion < 0) action(); }, 20);
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
                _rpgNavItems[i].gameObject.SetActive(true);
                _rpgNavItems[i].anchorMin = field ? new Vector2(0f, .773f - .254f * i) : new Vector2(.255f * i, 0f);
                _rpgNavItems[i].anchorMax = field ? new Vector2(1f, 1f - .254f * i) : new Vector2(.235f + .255f * i, 1f);
            }
        }

        private static Button RpgButton(RectTransform area, string text, Action action, int size = 19,
            bool primary = false, TagForceSfx? sound = TagForceSfx.Confirm, bool prominent = false)
        {
            var button = Btn(area, text, action, size, sound);
            RpgUiStyle.Control(button, primary, prominent);
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
            if (_back.TryGetComponent<MenuButtonMotion>(out var motion)) motion.enabled = false;
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
            DuelAudio.PlayRpgFieldMusic(MusicForRpgRegion(_rpgLocation));
            switch (page)
            {
                case Page.Map: BuildRpgMap(); break;
                case Page.Field:
                    _rpgInspecting = false;
                    _rpgWorkbenchServices = false;
                    _rpgChoosingCompanion = false;
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
                var toast = Area(_body, "RegionUnlockToast", .327f, .852f, .778f, .953f);
                RpgUiStyle.Panel(toast, accent: true);
                _rpgToast = HudLabel(toast, $"신규 지역 개방!  ·  {opened}", 21, DuelUiKit.GoldAccent,
                    align: TextAnchor.MiddleCenter, heading: true);
                _rpgToastTime = 4.5f;
            }
        }

        internal static RpgFieldMusic MusicForRpgRegion(int region) => region switch
        {
            RpgAcademia or RpgGarage or RpgPoppo => RpgFieldMusic.Home,
            RpgStadium or RpgWrgp => RpgFieldMusic.Circuit,
            RpgPrison or RpgMoment or RpgArk => RpgFieldMusic.Crisis,
            _ => RpgFieldMusic.Outskirts,
        };

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

        private Sprite GetRpgFieldArt(RpgRegion region) =>
            (LightingForEpisode(region, RpgClears + 1) is string lighting ? _images.Get(RpgArt + lighting) : null) ??
            _images.Get(region.FieldBackground) ?? _images.Get(region.Background);

        private string FieldOutfit(int id) => RpgWardrobe.ForField(id, _rpgLocation, RpgClears + 1);

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
            var locals = RpgLocalResidents(episodes, _rpgLocation, cleared, questNpc, sideQuest).ToArray();
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
            var sideObjective = sideQuest != null ? AddRpgSideQuest(sideQuest) : null;
            AddRpgShopkeeper();
            BuildRpgCompanion();
            _player = Anchor(_rpgSceneSpace, "Player", _position, RpgFieldActorSize);
            _playerSprite = WalkerSprite(_player, RpgArt + "Characters_SD/sd_protagonist.png", "카이");
            PoseRpgWalker(_player, _playerSprite, _position, Vector2.zero, false);
            OrderRpgFieldActors();
            BuildRpgFieldHud(cleared, targetRegion, episodes, sideObjective);
        }

        private IEnumerable<StoryEpisodeData> RpgLocalResidents(StoryEpisodeData[] episodes, int region, int cleared,
            StoryEpisodeData questNpc, RpgSideQuest sideQuest) =>
            episodes.Where((episode, i) => i < cleared && RegionForEpisode(i + 1) == region)
                .Concat(episodes.Take(cleared)).Concat(episodes.Take(2))
                .Where(episode => RpgResidentIdentity(episode.charId) != RpgResidentIdentity(questNpc?.charId) &&
                    RpgResidentIdentity(episode.charId) != RpgResidentIdentity(sideQuest?.Giver) &&
                    (episode.charId != RpgPartnerId || region == RpgBridge) &&
                    (_rpgWorldDecks?.ContainsKey(episode.deckId) ?? false))
                .GroupBy(episode => RpgResidentIdentity(episode.charId)).Select(group => group.First()).Take(2);

        private static int? RpgResidentIdentity(int? id) => id is 220 or 221 or 230 ? 9 : id;

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
                story ? "MainQuestNpc" : $"Resident_{resident.charId}", spot,
                story ? RpgResidentRole.Story : RpgResidentRole.Conversation, interact, patrol: !story);

        private void AddRpgResident(int id, string name, string node, Vector2 spot, RpgResidentRole role,
            Action interact, bool patrol = false)
        {
            var actor = Anchor(_rpgSceneSpace, node, spot, RpgFieldActorSize);
            _rpgResidents[id] = actor;
            var art = DuelUiKit.FillRect("SD", actor);
            var outfit = FieldOutfit(id);
            var profile = BuildRpgActorArt(art, RpgArt + $"Characters_SD/sd_{RpgArtKey(id)}.png", name,
                outfit == null ? null : RpgWardrobe.WalkAtlas(id, outfit));
            PoseRpgWalker(actor, profile, spot, Vector2.zero, false, Vector2.right * (_position.x - spot.x));
            void Approach() => MoveTo(actor.anchorMin - new Vector2(0f, .06f), interact, actor);
            BindPortrait(art, Approach);
            BuildRpgResidentMotion(actor, profile, spot, role, patrol);
            string verb = role switch
            {
                RpgResidentRole.Story => "이야기 진행",
                RpgResidentRole.Request => "의뢰",
                RpgResidentRole.Delivery => "전달",
                RpgResidentRole.Reward => "보상 받기",
                RpgResidentRole.Shop => "팩 구매",
                _ => "대화",
            };
            var priority = role switch
            {
                RpgResidentRole.Story => RpgActionPriority.Story,
                RpgResidentRole.Request or RpgResidentRole.Delivery or RpgResidentRole.Reward => RpgActionPriority.Request,
                RpgResidentRole.Shop => RpgActionPriority.Service,
                _ => RpgActionPriority.Conversation,
            };
            RegisterRpgInteraction(actor, $"{name} · {verb}", Approach, priority);
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
            if (_rpgChoosingCompanion) { BuildRpgPartnerPicker(); return; }
            if (_rpgWorkbenchServices) { BuildRpgWorkbenchServices(); return; }
            if (_rpgInspecting) { BuildRpgLandmarkTalk(); return; }
            if (_resident == null && _rpgQuestResident == null) { Show(Page.Field); return; }
            BeginRpgTalkBackdrop();
            var quest = _rpgQuestResident;
            string name = quest == null ? _resident.opponentName : RpgQuestName(quest);
            int speaker = quest?.Giver ?? _resident.charId;
            var panel = BuildRpgConversation(name, quest != null ? RpgQuestDialogue(quest) : RpgResidentLine(speaker, daily: false),
                out var conversation, speaker);
            var actions = new List<(string Id, string Label, Action Select)>
            {
                ("TalkDaily", "일상 대화", () => StartTypingDialogue(conversation, quest != null
                    ? RpgQuestDialogue(quest) : RpgResidentLine(speaker, daily: true))),
            };
            if (quest == null) actions.Add(("Practice", "연습 듀얼", () => Show(Page.Duel)));
            else AppendRpgQuestActions(quest, actions);
            actions.Add(("Leave", "돌아가기", () => Show(Page.Field)));
            AddRpgConversationActions(panel, actions);
        }

        private void BeginRpgTalkBackdrop()
        {
            BuildRpgField();
            foreach (var resident in _rpgResidentMotion) resident.Marker.gameObject.SetActive(false);
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
                var speaker = FaceRpgConversationSpeaker(id);
                var outfit = FieldOutfit(id);
                var bust = (outfit == null ? null : _images.Get(RpgArt + RpgWardrobe.Preview(id, outfit))) ??
                    _images.GetBust(RpgArt + $"Characters_Standing/full_{RpgArtKey(id)}.png");
                if (bust == null && id != RpgShopkeeperId)
                    bust = DuelProfileArt.Opponent(DeckRepository.GetCharacter(id));
                if (bust != null && bust != DuelProfileArt.MissingOpponent)
                {
                    bool showOnLeft = speaker != null && speaker.anchorMin.x > .62f;
                    var fieldArt = speaker?.Find("SD") ?? speaker?.Find("Sprite");
                    if (fieldArt != null) fieldArt.gameObject.SetActive(false);
                    var portrait = Area(_body, "ConversationPortrait", showOnLeft ? .045f : .404f, .262f,
                        showOnLeft ? .305f : .685f, .957f);
                    DuelUiKit.AddImage(portrait.gameObject, bust, Color.white, true, false);
                    UiEntranceMotion.Play(portrait.gameObject, .65f, .20f);
                }
            }
            var panel = DuelUiKit.FillRect("ResidentChoices", _body);
            var window = Area(panel, "DialogueWindow", .065f, .029f, .683f, .265f);
            RpgUiStyle.Dialogue(window);
            var badge = Area(panel, "NamePlate", .093f, .236f, .365f, .310f);
            RpgUiStyle.Speaker(badge);
            var color = RpgUiStyle.SpeakerColor(name);
            var stripe = Area(badge, "Signature", .069f, .22f, .085f, .78f);
            stripe.localRotation = Quaternion.Euler(0f, 0f, -16f);
            Tint(stripe, color);
            var speakerLabel = HudLabel(Area(badge, "Name", .125f, .04f, .944f, .96f), name, 25, color);
            RpgUiStyle.Display(speakerLabel);
            conversation = HudLabel(Area(panel, "DailyLine", .103f, .051f, .648f, .232f), line, 25,
                new Color(.90f, .94f, .97f));
            RpgUiStyle.Reading(conversation);
            conversation.lineSpacing = 1.28f;
            StartTypingDialogue(conversation, line);
            UiEntranceMotion.Play(panel.gameObject, .84f, .18f);
            return panel;
        }

        private RectTransform FaceRpgConversationSpeaker(int id)
        {
            _rpgResidents.TryGetValue(id, out var speaker);
            if (speaker == null && id == RpgPartnerId) speaker = _rpgCompanion;
            if (speaker == null) return null;

            var art = (speaker.Find("SD") ?? speaker.Find("Sprite")) as RectTransform;
            var towardSpeaker = speaker.anchorMin - _position;
            PoseRpgWalker(_player, _playerSprite, _position, Vector2.zero, false, towardSpeaker);
            PoseRpgWalker(speaker, art, speaker.anchorMin, Vector2.zero, false, -towardSpeaker);
            FrameRpgCamera(Vector2.Lerp(_position, speaker.anchorMin, .32f));
            return speaker;
        }

        private static Button AddRpgConversationActions(RectTransform panel, List<(string Id, string Label, Action Select)> actions)
        {
            Button first = null;
            const float bottom = .05f, stride = .076f;
            float top = bottom + stride * actions.Count;
            var surround = Area(panel, "ActionsFrame", .708f, bottom - .018f, .942f, top + .021f);
            RpgUiStyle.Speaker(surround);
            for (int i = 0; i < actions.Count; i++)
            {
                var (id, label, select) = actions[i];
                float y = top - stride * (i + 1);
                var button = RpgButton(Area(panel, id, .723f, y, .927f, y + .061f), label, select,
                    primary: i == 0);
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
            if (_page == Page.Map) UpdateRpgMapPulse(delta);
            UpdateRpgQuestReward(delta);
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
