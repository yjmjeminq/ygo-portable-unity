using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private const int RpgPartnerId = 35;
        private const int RpgShopkeeperId = 13;
        private const string RpgPartnerName = "후도 유세이";
        private static readonly string[] RpgPartnerLines =
        {
            "공구랑 부품은 작업대에 있어. 출발 전에 덱도 확인하자.",
            "은신처 안쪽까지 둘러보자. 구석에 남겨둔 물건이 있을 거야.",
            "너무 서두르지 마. 수용소에서는 게시판부터 확인하는 게 좋아.",
            "폿포 타임에서는 잠깐 쉬어 가자. 라디오 소리가 들려.",
            "관중석이 벌써 소란스럽군. 경기 전에 상대 덱을 생각해 보자.",
            "발밑을 조심해. 구 모멘트에는 아직 켜져 있는 장치가 있어.",
            "바람이 세다. 난간에 걸린 물건이 없는지 살펴봐.",
            "출발선에서는 한 번의 실수도 크게 돌아와. 준비를 끝내자.",
            "여기까지 왔군. 마지막까지 네 덱을 믿어.",
        };
        private static readonly Vector2[] RpgCachePoints =
        {
            new(.14f, .34f), new(.79f, .26f), new(.84f, .32f), new(.14f, .30f), new(.83f, .29f),
            new(.16f, .32f), new(.81f, .34f), new(.17f, .32f), new(.82f, .26f),
        };

        private readonly List<(RectTransform Actor, Vector2 Home)> _rpgRoamers = new();
        private StoryModeUI.StoryDialogueView _rpgIntroDialogue;
        private RectTransform _rpgCompanion, _rpgCompanionArt, _rpgGreeting, _rpgTravelBanner, _rpgRankBanner;
        private Image _rpgTravelFade;
        private Vector2 _rpgCompanionPoint;
        private float _rpgGreetingTime, _rpgTravelTime, _rpgRankTime, _rpgPatrolTime;
        private int _rpgRankNotice, _rpgPendingRegion = -1;
        private Vector2 _rpgPendingArrival;
        private bool _rpgIntroOnOpen, _rpgAnnounceArrival, _rpgWorkbenchServices;
        private int? _rpgServiceSpeaker;

        internal Sprite RpgStandingPortrait(int id) => _images.Get(RpgArt + "Characters_Standing/" +
            (id == 0 ? "full_protagonist.png" : $"full_{id}.png"));

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
                new StoryDialogueLine { speaker = RpgPartnerName, text = "카이, 일어났군. 스틱이나 방향키로 걸어 봐. 화면을 눌러도 그쪽으로 갈 수 있어." },
                new StoryDialogueLine { speaker = RpgPartnerName, text = "사람 곁에 가서 선택 버튼을 누르면 말할 수 있어. 머리 위에 느낌표가 보이면 그곳에서 다음 사건이 시작돼." },
                new StoryDialogueLine { speaker = RpgPartnerName, text = "작업대에서 덱을 정리하고, 정비소 출구를 따라가면 새틀라이트로 나갈 수 있어. 먼저 정비소 사람들한테 말을 걸어 보자." },
            } : new[]
            {
                new StoryDialogueLine { speaker = "내레이션", text = "새틀라이트의 밤. 철길 너머의 불빛은 새벽이 와도 좀처럼 가까워지지 않았다." },
                new StoryDialogueLine { speaker = "내레이션", text = "정비소에 남은 건 고장 난 듀얼 러너 한 대와 카이라는 이름뿐이었다. 문 너머로 발소리가 들려왔다." },
            };
            _rpgIntroDialogue.Open(guide ? "카이의 정비소" : "프롤로그 · 새틀라이트의 아침",
                "카이", DeckRepository.GetCharacter(RpgPartnerId), lines,
                guide ? FinishRpgIntro : () => Show(Page.Guide), rpg: true, backdropBelow: guide,
                rpgBackdrop: "Maps/spot_kai_garage.png", onSkip: FinishRpgIntro);
        }

        private void FinishRpgIntro()
        {
            bool saved = CollectionProgress.TrySaveRpgCheckpoint(true, out var error);
            _rpgIntroOnOpen = false;
            _rpgAnnounceArrival = true;
            Show(Page.Field);
            if (!saved) ShowStatus(error);
        }

        private void BuildRpgProgressHud()
        {
            var card = Area(_rpgFieldHud, "DuelistHud", .02f, .865f, .77f, .915f);
            Tint(card, new Color(.026f, .08f, .13f, .96f));
            var rank = Label(Area(card, "Rank", .02f, .05f, .22f, .98f),
                $"듀얼리스트 Lv.{CollectionProgress.RpgRank}", 16, TextAnchor.MiddleLeft);
            rank.color = DuelUiKit.GoldAccent;
            var track = Area(card, "Experience", .235f, .27f, .526f, .72f);
            Tint(track, new Color(.006f, .026f, .047f, .95f));
            var fill = Area(track, "Fill", 0f, 0f, CollectionProgress.RpgRankProgress, 1f);
            Tint(fill, new Color(.18f, .70f, .81f));
            Label(track, "EXP", 12).rectTransform.SetAsLastSibling();
            Label(Area(card, "Dp", .562f, .05f, .738f, .98f), $"{CollectionProgress.Balance:N0} DP", 16);
            Label(Area(card, "Affinity", .761f, .05f, .992f, .98f),
                $"♥ 유세이 {CollectionProgress.RpgAffinity}/100", 16).color = new Color(1f, .70f, .77f);
        }

        private void BuildRpgCache()
        {
            int region = _rpgLocation;
            if (CollectionProgress.RpgCacheFound(region)) return;
            var point = RpgCachePoints[region];
            var cache = Anchor(_rpgFieldObjects, "HiddenCache", point, new Vector2(56f, 52f));
            void Take()
            {
                if (!CollectionProgress.TryFindRpgCache(region, out string result)) { ShowStatus(result); return; }
                DuelAudio.PlayTagForceReward();
                Show(Page.Field);
                ShowStatus(result);
            }
            var open = Btn(cache, "✦", () => MoveTo(point, Take), 32);
            open.targetGraphic.color = new Color(.05f, .15f, .215f, .48f);
            cache.GetComponentInChildren<Text>().color = new Color(1f, .87f, .47f);
            _questMarkers.Add(cache);
            _interactions.Add((cache, Take));
        }

        private void BuildRpgCompanion()
        {
            if (_rpgResidents.ContainsKey(RpgPartnerId)) return;
            _rpgCompanionPoint = new Vector2(Mathf.Clamp(_position.x - .074f, .06f, .94f),
                Mathf.Max(.105f, _position.y - .026f));
            _rpgCompanion = Anchor(_rpgSceneSpace, "RpgCompanion", _rpgCompanionPoint, new Vector2(90f, 108f));
            _rpgCompanionArt = WalkerSprite(_rpgCompanion, RpgArt + "Characters_SD/sd_35.png", RpgPartnerName);
            if (_page != Page.Field) return;
            void Speak() => OpenRpgService(RpgPartnerId);
            var choice = Btn(Area(_rpgCompanion, "Interact", .01f, 0f, .99f, .235f), "유세이", Speak, 15);
            choice.targetGraphic.color = new Color(.06f, .23f, .33f, .85f);
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

        private void AddRpgShopkeeper()
        {
            if (_rpgLocation != RpgGarage) return;
            AddRpgResident(RpgShopkeeperId, "보머 · 카드 매점", "RpgCardMerchant", new Vector2(.315f, .37f),
                false, () => OpenRpgService(RpgShopkeeperId));
            var marker = _rpgResidents[RpgShopkeeperId].Find("ResidentMarker") as RectTransform;
            if (marker.TryGetComponent<Image>(out var icon)) icon.enabled = false;
            var fallback = marker.GetComponentInChildren<Text>();
            if (fallback != null) fallback.gameObject.SetActive(false);
            Label(marker, "상점", 15).color = new Color(.63f, 1f, .82f);
        }

        private void OpenRpgService(int speaker)
        {
            if (!CanWalk) return;
            _rpgServiceSpeaker = speaker;
            string error = "";
            if (speaker == RpgPartnerId && CollectionProgress.TryTalkToRpgPartner(_rpgLocation, out error))
                DuelAudio.PlayTagForceSfx(TagForceSfx.Confirm, .6f);
            Show(Page.Talk);
            if (!string.IsNullOrEmpty(error)) ShowStatus(error);
        }

        private void BuildRpgServiceTalk(int speaker)
        {
            BeginRpgTalkBackdrop();
            bool partner = speaker == RpgPartnerId;
            var target = (partner ? _rpgCompanion : null) ?? _rpgResidents.GetValueOrDefault(speaker);
            if (target != null)
                FaceRpgConversation(target, (target.Find("SD") ?? target.Find("Sprite")) as RectTransform);
            var panel = BuildRpgConversation(partner ? RpgPartnerName : "보머 · 카드 매점",
                partner ? RpgPartnerLines[_rpgLocation] : "시내까지 갈 필요 없어. 여기서도 DP로 팩을 살 수 있어.", out _);
            var actions = new List<(string Id, string Label, Action Select)>();
            if (!partner) actions.Add(("OpenCardShop", "카드 팩 보기", _app.OpenRpgCardShop));
            actions.Add(("Leave", "돌아가기", () => Show(Page.Field)));
            AddRpgConversationActions(panel, actions);
        }

        private void FaceRpgConversation(RectTransform speaker, RectTransform art)
        {
            var direction = speaker.anchorMin - _position;
            PoseRpgWalker(_player, _playerSprite, _position, Vector2.zero, false, direction);
            PoseRpgWalker(speaker, art, speaker.anchorMin, Vector2.zero, false, -direction);
            speaker.Find("ResidentMarker")?.gameObject.SetActive(false);
            speaker.Find("QuestBadge")?.gameObject.SetActive(false);
            BuildRpgSpeakerCue(speaker, out _);
        }

        private void BuildRpgWorkbenchServices()
        {
            BeginRpgTalkBackdrop();
            var panel = BuildRpgConversation("정비 작업대",
                "덱을 정리하거나 지금까지의 모험을 저장할 수 있다.", out var text);
            var actions = new List<(string Id, string Label, Action Select)>
            {
                ("EditDeck", "덱 편집", _app.OpenTagForceDeckEditor),
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
            _rpgTravelBanner = Area(_body, "RegionBanner", .25f, .723f, .75f, .83f);
            Tint(_rpgTravelBanner, new Color(.024f, .088f, .135f, .96f));
            Label(_rpgTravelBanner, RpgRegions[_rpgLocation].Name, 27).color = DuelUiKit.GoldAccent;
            if (_rpgCompanion != null || _rpgResidents.ContainsKey(RpgPartnerId))
            {
                _rpgGreetingTime = 0f;
                _rpgGreeting = Area(_body, "PartnerGreeting", .19f, .10f, .81f, .181f);
                Tint(_rpgGreeting, new Color(.031f, .098f, .15f, .94f));
                Label(Area(_rpgGreeting, "Speech", .04f, .03f, .96f, .97f),
                    $"유세이  ·  {RpgPartnerLines[_rpgLocation]}", 17);
                Label(Area(_rpgGreeting, "Tail", .45f, -.21f, .55f, .10f), "▼", 13).color =
                    new Color(.031f, .098f, .15f, .94f);
                PositionRpgGreeting();
            }
        }

        private void BuildRpgRankNotice()
        {
            _rpgRankTime = 0f;
            _rpgRankBanner = Area(_body, "RankUp", .29f, .59f, .71f, .69f);
            Tint(_rpgRankBanner, new Color(.22f, .13f, .04f, .96f));
            Label(_rpgRankBanner, $"DUELIST RANK UP!  ·  Lv.{_rpgRankNotice}", 25).color = DuelUiKit.GoldAccent;
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

            if (!CanWalk || _rpgRoamers.Count == 0 || _onArrival != null) return;
            _rpgPatrolTime += delta;
            foreach (var (actor, home) in _rpgRoamers)
            {
                if (Vector2.SqrMagnitude(actor.anchorMin - _position) < .11f * .11f) continue;
                float sway = Mathf.Sin(_rpgPatrolTime * .72f);
                var next = home + Vector2.right * (.037f * sway);
                var step = Vector2.MoveTowards(actor.anchorMin, next, delta * .042f) - actor.anchorMin;
                PoseRpgWalker(actor, actor.Find("SD") as RectTransform, actor.anchorMin + step, step, step.sqrMagnitude > .000001f);
            }
        }

        private void PositionRpgGreeting()
        {
            var speaker = _rpgCompanion ?? _rpgResidents.GetValueOrDefault(RpgPartnerId);
            if (speaker == null) return;
            Vector2 point = Rect.PointToNormalized(_body.rect, _body.InverseTransformPoint(speaker.position));
            float x = Mathf.Clamp(point.x, .25f, .75f);
            float y = Mathf.Clamp(point.y + .10f, .20f, .69f);
            _rpgGreeting.anchorMin = new Vector2(x - .23f, y);
            _rpgGreeting.anchorMax = new Vector2(x + .23f, y + .075f);
        }

        private void ClearRpgPresentation()
        {
            _rpgIntroDialogue?.Close();
            _rpgIntroDialogue = null;
            _rpgCompanion = _rpgCompanionArt = _rpgGreeting = _rpgTravelBanner = _rpgRankBanner = null;
            _rpgTravelFade = null;
            _rpgPendingRegion = -1;
            _rpgRoamers.Clear();
            _rpgPatrolTime = 0f;
        }
    }
}
