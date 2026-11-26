using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private static int RpgPartnerId => CollectionProgress.ActiveRpgPartner.Id;
        private const int RpgShopkeeperId = -1;
        private const string RpgShopkeeperName = "카드 매점 직원";
        private static string RpgArtKey(int id) => id == RpgShopkeeperId ? "shopkeeper" : id.ToString();
        private static string RpgPartnerName => CollectionProgress.ActiveRpgPartner.Name;
        private static readonly string[] RpgPartnerLines =
        {
            "아카데미아 실습장부터 둘러보자. 교정에 오늘의 대진표가 있어.",
            "공구랑 부품은 작업대에 있어. 출발 전에 덱도 확인하자.",
            "은신처 안쪽까지 둘러보자. 구석에 남겨둔 물건이 있을 거야.",
            "너무 서두르지 마. 수용소에서는 게시판부터 확인하는 게 좋아.",
            "폿포 타임에서는 잠깐 쉬어 가자. 라디오 소리가 들려.",
            "관중석이 벌써 소란스럽군. 경기 전에 상대 덱을 생각해 보자.",
            "발밑을 조심해. 구 모멘트에는 아직 켜져 있는 장치가 있어.",
            "모래바람이 불기 전에 큰길을 건너자. 광산 입구 쪽은 조심해.",
            "바람이 세다. 난간에 걸린 물건이 없는지 살펴봐.",
            "출발선에서는 한 번의 실수도 크게 돌아와. 준비를 끝내자.",
            "여기까지 왔군. 마지막까지 네 덱을 믿어.",
        };
        private static readonly Vector2[] RpgCachePoints =
        {
            new(.84f, .32f), new(.14f, .34f), new(.79f, .26f), new(.84f, .32f), new(.14f, .30f),
            new(.83f, .29f), new(.16f, .32f), new(.15f, .31f), new(.81f, .34f), new(.17f, .32f), new(.82f, .26f),
        };
        private static readonly Dictionary<int, string[]> RpgPartnerAreaLines = new()
        {
            [1] = new[] { "교정의 장미는 아직 피기 전이네.", "너랑 연습한 날이 떠올라. 오늘은 내가 먼저 시동 걸어 볼게.", "은신처에 꽃 한 송이 두고 가면 달라 보일까?", "쇠창살을 보면 옛날 생각이 나. 너무 오래 있진 말자.", "라디오 옆 자리로 가자. 거긴 바깥 소리가 잘 들려.", "관중들이 무슨 표정인지 이제는 조금 궁금해졌어.", "여기선 장미 향도 묻힐 것 같아. 발밑을 봐.", "모래바람이 불면 고개를 숙여. 눈에 들어가면 앞이 안 보여.", "바람 소리 좋다. 잠깐만 더 서 있을래?", "페달을 부드럽게 밟는 연습, 여기서도 해 볼래?", "이 높이에서도 뿌리내릴 곳이 있을까. 끝나면 같이 내려가자." },
            [10] = new[] { "교정 엄청 넓네. 애들이 여기서 뛰면 하루 종일 놀겠다.", "공구함 왼쪽이 네 자리야. 물건 잃어버리지 마.", "이 골목은 눈 감고도 가. 그래도 따라올 땐 눈 떠, 카이.", "수용소에서는 발소리부터 줄여. 철판 위에선 두 배로 울려.", "폿포 타임에선 뭐 좀 먹자. 간식은 내가 살게.", "저 관중석까지 소리가 갈 만한 판을 만들어 보자고.", "한때 여기가 불을 밝혔다는 게 믿기냐. 지금은 서로를 챙기는 게 먼저야.", "바람이 세면 골목 안쪽으로 돌아. 채굴장 길은 나중에 보자.", "다리 끝에서 새틀라이트 쪽을 봐. 우리 애들이 저 불빛 아래 있어.", "WRGP라니. 열심히 뛰고 나면 정비는 같이 하자.", "돌아오라고 기다리는 사람이 있어. 혼자 앞서가지 마." },
            [16] = new[] { "교정에선 실수해도 된다. 연습할 땐 가장 어려운 수를 먼저 써 봐.", "러너의 상태부터 봐라. 오늘은 말보다 기록으로 확인하지.", "예전엔 이 골목이 전부인 줄 알았다. 지금은 여기서 출발했다는 게 마음에 들어.", "철문이 열릴 때까지 덱이나 점검해라. 조급하다고 빠르게 열리진 않아.", "잠깐 쉬지. 커피는 물 타지 말라고 전해 둬.", "관중이 있는 경기장에선 먼저 상대 눈을 봐. 답은 거기 있다.", "제단 앞이라고 물러설 이유는 없다. 단, 발판부터 확인해.", "모래를 들이마시면 엔진부터 멎는다. 차간 거리를 넓혀.", "저 다리를 너와 달리는 것도 나쁘지 않군.", "출발 신호가 울리면 생각보다 한 박자 늦게 밟아라. 미끄러짐이 줄어든다.", "여기가 마지막이라면 더 좋군. 돌아갈 때 떳떳한 판을 해 보자." },
        };
        private static readonly Dictionary<int, string[]> RpgBondLines = new()
        {
            [35] = new[] { "네 손에 맡기면 공구도 어디 있는지 기억할 수 있겠어.", "오늘은 내가 네 덱을 지켜볼게. 언제 출발할지 네가 정해.", "처음엔 혼자 해결하는 게 빠르다고 생각했어. 너랑 달리니까 달라지더라.", "내가 망설이면 먼저 불러 줘. 네 목소리는 잘 들리거든.", "어디까지 가든 돌아갈 정비소는 우리 둘이 지킨다." },
            [1] = new[] { "오늘은 네가 먼저 걷지 말고 나란히 가 줄래?", "정원에서 가장 먼저 핀 장미를 너한테 보여 주고 싶었어.", "한때는 내 힘이 누군가를 다치게 할까 봐 겁났어. 지금은 같이 지킬 수도 있다는 걸 알아.", "라이딩을 배울 때 넘어져도 좋았어. 너랑 다시 일어나면 됐으니까.", "자유롭게 달릴 수 있는 길을 찾으면 제일 먼저 너를 부를게." },
            [10] = new[] { "이제 네 공구 자리는 내가 지킨다. 애들한테도 말해 둘게.", "우리 애들이 널 동네 사람처럼 말해. 너도 그렇게 생각하면 좋겠다.", "나 혼자 지키는 동네가 아니라는 생각이 들 때 제일 힘이 나.", "한밤에 엔진 멈춰도 무전해. 어디든 갈 테니까.", "돌아오는 길은 같이 가자. 이제 둘 다 그 길을 잘 아니까." },
            [16] = new[] { "네 출발이 오늘따라 깔끔하더군. 한 번 더 보자.", "네 앞에선 변명할 시간에 기록을 고치는 게 빠르더군.", "네가 내 뒤를 따라온다고 생각했는데, 이제 보니 옆에서 달리고 있었어.", "승부가 끝나고 커피 한 잔 하지. 다음번에는 내가 산다.", "네가 도전하면 내가 달려갈 이유가 하나 더 생긴다." },
        };

        private StoryModeUI.StoryDialogueView _rpgIntroDialogue;
        private RectTransform _rpgCompanion, _rpgCompanionArt, _rpgGreeting, _rpgTravelBanner, _rpgRankBanner;
        private Image _rpgTravelFade;
        private Vector2 _rpgCompanionPoint;
        private float _rpgGreetingTime, _rpgTravelTime, _rpgRankTime, _rpgBondTime;
        private RectTransform _rpgBondBanner;
        private int _rpgRankNotice, _rpgPendingRegion = -1;
        private Vector2 _rpgPendingArrival;
        private bool _rpgIntroOnOpen, _rpgAnnounceArrival, _rpgWorkbenchServices, _rpgChoosingCompanion;
        private int? _rpgServiceSpeaker;

        private string RpgPartnerComment => RpgPartnerAreaLines.GetValueOrDefault(RpgPartnerId, RpgPartnerLines)[_rpgLocation];

        internal Sprite RpgStandingPortrait(int id, int episode = 0)
        {
            if (id == 0) return _images.Get(RpgArt + "Characters_Standing/full_protagonist.png");
            string outfit = RpgWardrobe.ForStory(id, episode);
            return (outfit == null ? null : _images.Get(RpgArt + RpgWardrobe.Preview(id, outfit))) ??
                _images.Get(RpgArt + RpgWardrobe.PortraitStem(id, null) + ".png");
        }

        private void BuildRpgIntro(bool guide)
        {
            if (guide) BeginRpgTalkBackdrop();
            else
            {
                Backdrop(RpgRegions[RpgGarage].Background, new Color(.055f, .091f, .135f));
                _rpgNavigation.gameObject.SetActive(false);
            }
            _rpgIntroDialogue = new StoryModeUI.StoryDialogueView(_body);
            var lines = guide ? new[]
            {
                new StoryDialogueLine { speaker = CollectionProgress.RpgPartners[0].Name, text = "카이, 일어났군. 스틱이나 방향키로 걸어 봐. 화면을 눌러도 그쪽으로 갈 수 있어." },
                new StoryDialogueLine { speaker = CollectionProgress.RpgPartners[0].Name, text = "가까이 가면 조사나 대화 버튼이 보여. 패드는 선택 버튼으로 실행하고, 위쪽 버튼으로 메뉴를 열어." },
                new StoryDialogueLine { speaker = CollectionProgress.RpgPartners[0].Name, text = "첫 약속은 듀얼 아카데미아에 있어. 교정에서 머리 위에 느낌표가 보이는 사람에게 다가가 봐." },
            } : new[]
            {
                new StoryDialogueLine { speaker = "내레이션", text = "새틀라이트의 밤. 철길 너머의 불빛은 새벽이 와도 좀처럼 가까워지지 않았다." },
                new StoryDialogueLine { speaker = "내레이션", text = "정비소에 남은 건 고장 난 듀얼 러너 한 대와 카이라는 이름뿐이었다. 문 너머로 발소리가 들려왔다." },
            };
            _rpgIntroDialogue.Open(guide ? "카이의 정비소" : "프롤로그 · 새틀라이트의 아침",
                "카이", DeckRepository.GetCharacter(CollectionProgress.RpgGuidePartnerId), lines,
                guide ? FinishRpgIntro : () => Show(Page.Guide), rpg: true, backdropBelow: guide,
                rpgBackdrop: "Maps/spot_kai_garage.png", onSkip: FinishRpgIntro);
        }

        private void FinishRpgIntro()
        {
            bool saved = CollectionProgress.TrySaveRpgCheckpoint(true, out var error);
            _rpgIntroOnOpen = false;
            _rpgAnnounceArrival = true;
            if (!TravelRpgRegion(RegionForEpisode(RpgClears + 1), RpgArrival)) Show(Page.Field);
            if (!saved) ShowStatus(error);
        }

        private void BuildRpgCache()
        {
            int region = _rpgLocation;
            int saveSlot = RpgRegions[region].AdventureSlot;
            if (CollectionProgress.RpgCacheFound(saveSlot)) return;
            var point = RpgCachePoints[region];
            var cache = Anchor(_rpgFieldObjects, "HiddenCache", point, new Vector2(56f, 52f));
            void Take()
            {
                if (!CollectionProgress.TryFindRpgCache(saveSlot, out string result)) { ShowStatus(result); return; }
                DuelAudio.PlayTagForceReward();
                Show(Page.Field);
                ShowStatus(result);
            }
            void Search() => MoveTo(point, Take);
            var click = DuelUiKit.AddImage(cache.gameObject, DuelUiKit.WhiteSprite, Color.clear, false, true);
            DuelUiKit.AddImage(DuelUiKit.FillRect("Glint", cache).gameObject, RpgSoftLight,
                new Color(1f, .76f, .33f, .62f), false, false);
            var open = cache.gameObject.AddComponent<Button>();
            open.targetGraphic = click;
            open.onClick.AddListener(Search);
            DrawRpgIcon(Area(cache, "Icon", .15f, .11f, .85f, .89f), "icon_treasure.png", "✦", DuelUiKit.GoldAccent);
            _questMarkers.Add(cache);
            RegisterRpgInteraction(cache, "반짝이는 물건 · 줍기", Search, RpgActionPriority.Service);
        }

        private void BuildRpgCompanion()
        {
            if (_rpgResidents.TryGetValue(RpgPartnerId, out var actor))
            {
                var resident = _rpgResidentMotion.Find(entry => entry.Actor == actor);
                if (resident == null || resident.Role == RpgResidentRole.Story || RpgPartnerId == CollectionProgress.RpgGuidePartnerId) return;
                _rpgCompanion = actor;
                _rpgCompanionArt = resident.Art;
                _rpgCompanionPoint = actor.anchorMin;
                _rpgInteractions.RemoveAll(candidate => candidate.Anchor == actor);
                RegisterRpgInteraction(actor, $"{RpgPartnerName} · 대화", () => OpenRpgService(RpgPartnerId), radius: .055f);
                return;
            }
            _rpgCompanionPoint = new Vector2(Mathf.Clamp(_position.x - .074f, .06f, .94f),
                Mathf.Max(.105f, _position.y - .026f));
            _rpgCompanion = Anchor(_rpgSceneSpace, "RpgCompanion", _rpgCompanionPoint, RpgFieldActorSize);
            int id = RpgPartnerId;
            string outfit = FieldOutfit(id);
            _rpgCompanionArt = WalkerSprite(_rpgCompanion, RpgArt + $"Characters_SD/sd_{id}.png", RpgPartnerName,
                outfit == null ? null : RpgWardrobe.WalkAtlas(id, outfit));
            if (_page != Page.Field) return;
            void Speak() => OpenRpgService(RpgPartnerId);
            if (_rpgCompanionArt != null) BindPortrait(_rpgCompanionArt, Speak);
            RegisterRpgInteraction(_rpgCompanion, $"{RpgPartnerName} · 대화", Speak, radius: .055f);
        }

        private void FollowRpgPlayer(float delta)
        {
            if (_rpgCompanion == null) return;
            var gap = _position - _rpgCompanionPoint;
            float distance = RpgArtDirection(gap).magnitude;
            var step = gap * (Mathf.Min(Mathf.Max(0f, distance - .068f), .41f * delta) / Mathf.Max(.0001f, distance));
            _rpgCompanionPoint += step;
            PoseRpgWalker(_rpgCompanion, _rpgCompanionArt, _rpgCompanionPoint, step, step.sqrMagnitude > .000001f, gap);
        }

        private static bool HasRpgMerchant(int region, int clears) => region == RpgGarage || region == RpgPoppo && clears >= 27;

        private void AddRpgShopkeeper()
        {
            if (!HasRpgMerchant(_rpgLocation, RpgClears)) return;
            AddRpgResident(RpgShopkeeperId, RpgShopkeeperName, "RpgCardMerchant", new Vector2(.82f, .38f),
                RpgResidentRole.Shop, () => OpenRpgService(RpgShopkeeperId));
        }

        private void OpenRpgService(int speaker)
        {
            if (!CanWalk) return;
            _rpgServiceSpeaker = speaker;
            string error = "";
            if (speaker == RpgPartnerId && CollectionProgress.TryTalkToRpgPartner(RpgRegions[_rpgLocation].AdventureSlot, out error))
                DuelAudio.PlayTagForceSfx(TagForceSfx.Confirm, .6f);
            Show(Page.Talk);
            if (!string.IsNullOrEmpty(error)) ShowStatus(error);
        }

        private void BuildRpgServiceTalk(int speaker)
        {
            BeginRpgTalkBackdrop();
            bool partner = speaker == RpgPartnerId;
            var panel = BuildRpgConversation(partner ? RpgPartnerName : RpgShopkeeperName,
                partner ? RpgPartnerComment : "시내까지 갈 필요 없어. 여기서도 DP로 팩을 살 수 있어.", out var conversation, speaker);
            var actions = new List<(string Id, string Label, Action Select)>();
            if (!partner) actions.Add(("OpenCardShop", "카드 팩 보기", _app.OpenRpgCardShop));
            int hearts = CollectionProgress.RpgAffinity / CollectionProgress.RpgAffinityPerHeart;
            if (partner && hearts > 0)
                actions.Add(("BondTalk", "특별 대화", () => StartTypingDialogue(conversation, RpgBondLines[speaker][hearts - 1])));
            actions.Add(("Leave", "돌아가기", () => Show(Page.Field)));
            AddRpgConversationActions(panel, actions);
        }

        private void BuildRpgWorkbenchServices()
        {
            BeginRpgTalkBackdrop();
            var panel = BuildRpgConversation("정비 작업대",
                "덱을 정리하거나 지금까지의 모험을 저장할 수 있다.", out var text);
            var actions = new List<(string Id, string Label, Action Select)>
            {
                ("EditDeck", "덱 편집", _app.OpenTagForceDeckEditor),
                ("ChangePartner", "동행 파트너", () => { _rpgChoosingCompanion = true; Show(Page.Talk); }),
                ("SaveRpg", "진행 저장", () =>
                {
                    bool saved = CollectionProgress.TrySaveRpgCheckpoint(false, out string result);
                    if (saved)
                    {
                        PlayerPrefs.SetString(RpgRegionPreference, RpgRegions[_rpgLocation].Id);
                        PlayerPrefs.Save();
                    }
                    StartTypingDialogue(text, saved ? "현재 진행을 저장했습니다." : result);
                }),
            };
            if (RpgClears > 0) actions.Add(("Recall", "지난 이야기", _app.OpenRpgRecap));
            actions.Add(("CloseWorkbench", "작업대 보기", () => { _rpgWorkbenchServices = false; Show(Page.Talk); }));
            AddRpgConversationActions(panel, actions);
        }

        private void BuildRpgPartnerPicker()
        {
            BeginRpgTalkBackdrop();
            var panel = BuildRpgConversation("동행 파트너", $"동행 중 · {RpgPartnerName}\n함께 갈 동료를 선택하세요.", out var text);
            var actions = new List<(string Id, string Label, Action Select)>();
            foreach (var partner in CollectionProgress.RpgPartners)
            {
                if (!CollectionProgress.IsRpgPartnerUnlocked(partner)) continue;
                string label = partner.Id == RpgPartnerId ? partner.Name + " · 동행 중" : partner.Name;
                actions.Add(($"Partner_{partner.Id}", label, () =>
                {
                    if (!CollectionProgress.TrySwitchRpgPartner(partner.Id, out string error)) { StartTypingDialogue(text, error); return; }
                    DuelAudio.PlayTagForceSfx(TagForceSfx.PartnerAccept);
                    Show(Page.Field);
                    BuildRpgPartnerGreeting();
                }));
            }
            actions.Add(("LeavePartnerPicker", "작업대로", () => { _rpgChoosingCompanion = false; Show(Page.Talk); }));
            AddRpgConversationActions(panel, actions);
        }

        internal void QueueRpgRankNotice(int previousRank)
        {
            if (CollectionProgress.RpgRank > previousRank) _rpgRankNotice = CollectionProgress.RpgRank;
        }

        private void BuildRpgArrivalBanner()
        {
            _rpgAnnounceArrival = false;
            _rpgTravelTime = 0f;
            _rpgTravelFade = DuelUiKit.AddImage(DuelUiKit.FillRect("TravelFade", _body).gameObject,
                DuelUiKit.WhiteSprite, new Color(.012f, .023f, .040f, 1f), false, false);
            _rpgTravelBanner = Area(_body, "RegionBanner", .27f, .108f, .73f, .201f);
            RpgUiStyle.Panel(_rpgTravelBanner, accent: true);
            HudLabel(_rpgTravelBanner, RpgRegions[_rpgLocation].Name, 27, DuelUiKit.GoldAccent,
                align: TextAnchor.MiddleCenter, heading: true);
            BuildRpgPartnerGreeting();
        }

        private void BuildRpgPartnerGreeting()
        {
            if (_rpgCompanion != null || _rpgResidents.ContainsKey(RpgPartnerId))
            {
                _rpgGreetingTime = 0f;
                _rpgGreeting = Area(_body, "PartnerGreeting", .19f, .10f, .81f, .181f);
                RpgUiStyle.Panel(_rpgGreeting);
                HudLabel(Area(_rpgGreeting, "Speech", .04f, .03f, .96f, .97f),
                    $"{RpgPartnerName}  ·  {RpgPartnerComment}", 18, Color.white, align: TextAnchor.MiddleCenter);
                Label(Area(_rpgGreeting, "Tail", .45f, -.21f, .55f, .10f), "▼", 13).color =
                    new Color(.031f, .098f, .15f, .94f);
                PositionRpgGreeting();
            }
        }

        private void BuildRpgRankNotice()
        {
            _rpgRankTime = 0f;
            _rpgRankBanner = Area(_body, "RankUp", .29f, .59f, .71f, .69f);
            RpgUiStyle.Panel(_rpgRankBanner, accent: true);
            HudLabel(_rpgRankBanner, $"DUELIST RANK UP!  ·  Lv.{_rpgRankNotice}", 25, DuelUiKit.GoldAccent,
                align: TextAnchor.MiddleCenter, heading: true);
            _rpgRankNotice = 0;
        }

        private void BeginRpgTravel(int region, Vector2 arrival)
        {
            if (_rpgPendingRegion >= 0 || !RpgRegions[region].IsOpen(RpgClears)) return;
            _rpgPendingRegion = region;
            _rpgPendingArrival = arrival;
            _rpgTravelTime = 0f;
            if (_rpgTravelFade != null) _rpgTravelFade.gameObject.SetActive(false);
            _rpgTravelFade = DuelUiKit.AddImage(DuelUiKit.FillRect("TravelOut", _body).gameObject,
                DuelUiKit.WhiteSprite, new Color(.012f, .023f, .04f, 0f), false, true);
        }

        private void UpdateRpgPresentation(float delta)
        {
            _rpgIntroDialogue?.Update(delta);
            _rpgTravelTime += delta;
            if (_rpgPendingRegion >= 0)
            {
                float progress = Mathf.Clamp01(_rpgTravelTime / .17f);
                _rpgTravelFade.color = new Color(.012f, .023f, .04f, progress);
                if (progress >= 1f)
                {
                    int destination = _rpgPendingRegion;
                    _rpgPendingRegion = -1;
                    TravelRpgRegion(destination, _rpgPendingArrival);
                }
                return;
            }
            if (_rpgTravelFade != null && _rpgTravelFade.gameObject.activeSelf)
            {
                float alpha = 1f - Mathf.Clamp01(_rpgTravelTime / .35f);
                _rpgTravelFade.color = new Color(.012f, .023f, .04f, alpha);
                if (alpha == 0f) _rpgTravelFade.gameObject.SetActive(false);
            }
            if (_rpgTravelBanner != null && _rpgTravelBanner.gameObject.activeSelf)
            {
                float entrance = Mathf.SmoothStep(0f, 1f, _rpgTravelTime / .29f);
                float exit = Mathf.SmoothStep(0f, 1f, (_rpgTravelTime - 1.96f) / .28f);
                _rpgTravelBanner.anchoredPosition = Vector2.right * (-270f * (1f - entrance) + 350f * exit);
                if (exit >= 1f) _rpgTravelBanner.gameObject.SetActive(false);
            }
            if (_rpgGreeting != null)
            {
                if ((_rpgGreetingTime += delta) >= 4f)
                {
                    _rpgGreeting.gameObject.SetActive(false);
                    _rpgGreeting = null;
                }
                else PositionRpgGreeting();
            }
            if (_rpgRankBanner != null && (_rpgRankTime += delta) >= 3f)
            {
                _rpgRankBanner.gameObject.SetActive(false);
                _rpgRankBanner = null;
            }
            UpdateRpgBondBanner(delta);
        }

        private void UpdateRpgBondBanner(float delta)
        {
            if (_rpgBondBanner != null && (_rpgBondTime += delta) >= 4.5f)
            {
                _rpgBondBanner.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(_rpgBondBanner.gameObject);
                else UnityEngine.Object.DestroyImmediate(_rpgBondBanner.gameObject);
                _rpgBondBanner = null;
            }
            if (_page != Page.Field || _rpgBondBanner != null || !CollectionProgress.TryReadRpgBondNotice(out var reward)) return;
            var partner = CollectionProgress.RpgPartners.First(entry => entry.Id == reward.PartnerId);
            _rpgBondTime = 0f;
            _rpgBondBanner = Area(_body, "PartnerBondReward", .326f, .724f, .780f, .826f);
            RpgUiStyle.Panel(_rpgBondBanner, gold: true);
            HudLabel(Area(_rpgBondBanner, "Reward", .037f, .52f, .963f, .96f),
                $"{partner.Name} · 하트 {reward.Hearts}칸 · {FormatRpgQuestReward(reward.Dp, 0, 0, reward.Tickets > 0)}",
                20, RpgUiStyle.Gold, align: TextAnchor.MiddleCenter, heading: true);
            HudLabel(Area(_rpgBondBanner, "SpecialLine", .037f, .055f, .963f, .49f), RpgBondLines[reward.PartnerId][reward.Hearts - 1],
                17, Color.white, align: TextAnchor.MiddleCenter);
            DuelAudio.PlayTagForceSfx(TagForceSfx.HeartUp);
        }

        private void PositionRpgGreeting()
        {
            var speaker = _rpgCompanion ?? _rpgResidents.GetValueOrDefault(RpgPartnerId);
            if (speaker == null) return;
            Vector2 point = Rect.PointToNormalized(_body.rect, _body.InverseTransformPoint(speaker.position));
            float x = Mathf.Clamp(point.x - .06f, .215f, .785f);
            float y = Mathf.Clamp(point.y + .145f, .20f, .58f);
            _rpgGreeting.anchorMin = new Vector2(x - .205f, y);
            _rpgGreeting.anchorMax = new Vector2(x + .205f, y + .075f);
        }

        private void ClearRpgPresentation()
        {
            _rpgIntroDialogue?.Close();
            _rpgIntroDialogue = null;
            _rpgCompanion = _rpgCompanionArt = _rpgGreeting = _rpgTravelBanner = _rpgRankBanner = _rpgBondBanner = null;
            _rpgTravelFade = null;
            _rpgPendingRegion = -1;
        }
    }
}
