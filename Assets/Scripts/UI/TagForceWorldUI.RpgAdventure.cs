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
        private const int RpgShopkeeperId = -1;
        private const string RpgShopkeeperName = "카드 매점 직원";
        private static string RpgArtKey(int id) => id == RpgShopkeeperId ? "shopkeeper" : id.ToString();
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

        private StoryModeUI.StoryDialogueView _rpgIntroDialogue;
        private RectTransform _rpgCompanion, _rpgCompanionArt, _rpgGreeting, _rpgTravelBanner, _rpgRankBanner;
        private Image _rpgTravelFade;
        private Vector2 _rpgCompanionPoint;
        private float _rpgGreetingTime, _rpgTravelTime, _rpgRankTime;
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
                new StoryDialogueLine { speaker = RpgPartnerName, text = "가까이 가면 조사나 대화 버튼이 보여. 패드는 선택 버튼으로 실행하고, 위쪽 버튼으로 메뉴를 열어." },
                new StoryDialogueLine { speaker = RpgPartnerName, text = "정비소 출구로 나가면 새틀라이트야. 머리 위에 느낌표가 보이는 사람에게 다가가면 다음 이야기가 시작돼." },
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
            void Search() => MoveTo(point, Take);
            var open = Btn(cache, "✦", Search, 32);
            open.targetGraphic.color = new Color(.05f, .15f, .215f, .48f);
            cache.GetComponentInChildren<Text>().color = new Color(1f, .87f, .47f);
            _questMarkers.Add(cache);
            RegisterRpgInteraction(cache, "반짝이는 물건 · 조사", Search);
        }

        private void BuildRpgCompanion()
        {
            if (_rpgResidents.ContainsKey(RpgPartnerId)) return;
            _rpgCompanionPoint = new Vector2(Mathf.Clamp(_position.x - .074f, .06f, .94f),
                Mathf.Max(.105f, _position.y - .026f));
            _rpgCompanion = Anchor(_rpgSceneSpace, "RpgCompanion", _rpgCompanionPoint, RpgFieldActorSize);
            _rpgCompanionArt = WalkerSprite(_rpgCompanion, RpgArt + "Characters_SD/sd_35.png", RpgPartnerName);
            if (_page != Page.Field) return;
            void Speak() => OpenRpgService(RpgPartnerId);
            if (_rpgCompanionArt != null) BindPortrait(_rpgCompanionArt, Speak);
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
            AddRpgResident(RpgShopkeeperId, RpgShopkeeperName, "RpgCardMerchant", new Vector2(.82f, .38f),
                RpgResidentRole.Shop, () => OpenRpgService(RpgShopkeeperId));
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
            var panel = BuildRpgConversation(partner ? RpgPartnerName : RpgShopkeeperName,
                partner ? RpgPartnerLines[_rpgLocation] : "시내까지 갈 필요 없어. 여기서도 DP로 팩을 살 수 있어.", out _, speaker);
            var actions = new List<(string Id, string Label, Action Select)>();
            if (!partner) actions.Add(("OpenCardShop", "카드 팩 보기", _app.OpenRpgCardShop));
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
            _rpgTravelBanner = Area(_body, "RegionBanner", .27f, .108f, .73f, .201f);
            RpgUiStyle.Panel(_rpgTravelBanner, accent: true);
            HudLabel(_rpgTravelBanner, RpgRegions[_rpgLocation].Name, 27, DuelUiKit.GoldAccent,
                align: TextAnchor.MiddleCenter, heading: true);
            if (_rpgCompanion != null || _rpgResidents.ContainsKey(RpgPartnerId))
            {
                _rpgGreetingTime = 0f;
                _rpgGreeting = Area(_body, "PartnerGreeting", .19f, .10f, .81f, .181f);
                RpgUiStyle.Panel(_rpgGreeting);
                HudLabel(Area(_rpgGreeting, "Speech", .04f, .03f, .96f, .97f),
                    $"유세이  ·  {RpgPartnerLines[_rpgLocation]}", 18, Color.white, align: TextAnchor.MiddleCenter);
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
        }

        private void PositionRpgGreeting()
        {
            var speaker = _rpgCompanion ?? _rpgResidents.GetValueOrDefault(RpgPartnerId);
            if (speaker == null) return;
            Vector2 point = Rect.PointToNormalized(_body.rect, _body.InverseTransformPoint(speaker.position));
            float x = Mathf.Clamp(point.x - .06f, .215f, .785f);
            float y = Mathf.Clamp(point.y + .145f, .20f, .69f);
            _rpgGreeting.anchorMin = new Vector2(x - .205f, y);
            _rpgGreeting.anchorMax = new Vector2(x + .205f, y + .075f);
        }

        private void ClearRpgPresentation()
        {
            _rpgIntroDialogue?.Close();
            _rpgIntroDialogue = null;
            _rpgCompanion = _rpgCompanionArt = _rpgGreeting = _rpgTravelBanner = _rpgRankBanner = null;
            _rpgTravelFade = null;
            _rpgPendingRegion = -1;
        }
    }
}
