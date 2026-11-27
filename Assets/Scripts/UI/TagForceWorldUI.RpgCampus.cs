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
        private const string CampusAcademy = "spot_main_academy", CampusDorm = "spot_red_dorm";
        private const string CampusBreadId = "gx_melon_bread";
        private const int CampusBreadPrice = 40, CampusBreadLimit = 9, CampusOpenAfter = 3;
        private const int CampusAsuka = 107, CampusJudai = 119;
        private static readonly string[] CampusPeriods = { "아침", "방과 후", "저녁" };
        private Color _campusActorTint = Color.white;
        private TagForceSave GxCampusSave => CollectionProgress.GetTagForceProgress(RpgCampaignId);
        private bool GxCampusActive => IsGxCampaign && RpgClears >= CampusOpenAfter && RpgPlayerId == 0;

        private static bool IsCampusSpot(string id) => id is CampusAcademy or CampusDorm;
        private static bool IsCampusFriend(int id) => id is CampusAsuka or CampusJudai;
        private static string CampusFriendKey(int id) => $"campus/{id}";

        private bool IsCampusEvening(string spot) => GxCampusActive && IsCampusSpot(spot) && GxCampusSave.campusTime == 2;

        private Sprite CampusEveningArt(string spot) => spot == CampusDorm && IsCampusEvening(spot)
            ? _images.Get(RpgArt + "Maps/spot_red_dorm_evening.png") : null;

        private string GxCampusResidentSpot(int id, string storySpot)
        {
            if (!GxCampusActive || !IsCampusSpot(storySpot)) return storySpot;
            return (id, GxCampusSave.campusTime) switch
            {
                (CampusAsuka, 2) when storySpot == CampusAcademy => CampusDorm,
                (CampusJudai, 1) when storySpot == CampusDorm => CampusAcademy,
                _ => storySpot,
            };
        }

        private void BuildCampusClock(RectTransform mission)
        {
            var state = GxCampusSave;
            var clock = RpgButton(Area(mission, "CampusClock", .724f, .64f, .951f, .92f),
                $"{state.campusDay}일 · {CampusPeriods[state.campusTime]}", () =>
                {
                    if (CanWalk) Show(Page.GxCampus);
                }, 17);
            clock.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        private void AddCampusDormStation()
        {
            var point = RpgGroundMark(_rpgLocation, new Vector2(.315f, .36f));
            var station = RpgFieldProp("CampusDormStation", point, new Vector2(150f, 43f));
            void Open() => MoveTo(point, () => { if (CanWalk) Show(Page.GxCampus); });
            RpgButton(station, "기숙사 · 휴식", Open, 18, primary: true);
            RegisterRpgInteraction(station, "기숙사 · 휴식", Open, RpgActionPriority.Service);
        }

        private void BuildGxCampus()
        {
            BuildRpgMenuBackdrop();
            var state = GxCampusSave;
            _header.text = "아카데미아 일정";
            var agenda = Area(_body, "CampusSchedule", .012f, .04f, .567f, .977f);
            RpgUiStyle.Panel(agenda);
            RpgUiStyle.Display(HudLabel(Area(agenda, "Title", .066f, .827f, .93f, .948f),
                $"{state.campusDay}일차", 38, Color.white));
            string[] places = { "본관", "본관 매점", "오시리스 레드 기숙사" };
            string[] things = { "친구에게 인사하고 오늘 약속을 확인한다.", "멜론빵을 사고, 연습 듀얼을 하거나 선물을 건넨다.",
                "기숙사 앞에서 이야기를 나누고, 쉬면서 다음 날을 맞는다." };
            for (int i = 0; i < CampusPeriods.Length; i++)
            {
                float top = .786f - i * .242f;
                var row = Area(agenda, $"Period_{i}", .067f, top - .208f, .933f, top);
                RpgUiStyle.Panel(row, accent: i == state.campusTime);
                HudLabel(Area(row, "Period", .065f, .48f, .92f, .92f),
                    $"{CampusPeriods[i]}  ·  {places[i]}", 25, i == state.campusTime ? (Color)RpgUiStyle.Gold : Color.white, heading: true);
                RpgUiStyle.Reading(HudLabel(Area(row, "Activity", .065f, .10f, .92f, .48f), things[i], 21,
                    new Color(.86f, .91f, .96f)));
            }

            var plans = Area(_body, "CampusPlans", .588f, .04f, .987f, .977f);
            RpgUiStyle.Glass(plans);
            var spot = _rpgConfiguration.spots[_rpgLocation];
            var art = CampusEveningArt(spot.spotId) ?? GetRpgFieldArt(CampaignRegions[_rpgLocation]);
            DuelUiKit.AddImage(Area(plans, "LocationArt", .066f, .531f, .934f, .944f).gameObject,
                art ?? DuelUiKit.WhiteSprite, art == null ? new Color(.07f, .15f, .23f) : Color.white, true, false);
            HudLabel(Area(plans, "CurrentLocation", .075f, .417f, .93f, .533f),
                spot.name, 26, Color.white, heading: true);
            var notes = Area(plans, "LocalNotes", .075f, .282f, .928f, .414f);
            var people = string.Join(" · ", GxCampusContacts(_rpgLocation).Select(contact => contact.opponentName));
            string note = people.Length > 0 ? $"지금 만날 수 있는 친구\n{people}" : "월드맵에서 다른 지역의 주민을 확인할 수 있어요.";
            string destination = state.campusTime == 2 && spot.spotId != CampusDorm ? CampusDorm :
                !IsCampusSpot(spot.spotId) ? CampusAcademy : null;
            if (destination == CampusDorm) note = "저녁에는 오시리스 레드 기숙사에서 쉴 수 있어요.";
            else if (destination == CampusAcademy) note = "본관에서 다음 시간대까지 자유시간을 보낼 수 있어요.";
            HudLabel(notes, note, 20, RpgUiStyle.Cyan);
            string next = state.campusTime == 2 ? "다음 날까지 쉬기" : $"{CampusPeriods[state.campusTime + 1]}까지 보내기";
            RpgButton(Area(plans, "Advance", .067f, .157f, .933f, .251f),
                destination == null ? next : destination == CampusDorm ? "기숙사로 이동" : "본관으로 이동",
                () =>
                {
                    if (destination == null) AdvanceCampusTime();
                    else if (_campaignRegionIndex.TryGetValue(destination, out int region)) BeginRpgTravel(region, RpgArrival);
                }, 22, primary: true);
            RpgButton(Area(plans, "Close", .067f, .039f, .933f, .134f), "필드로 돌아가기", () => Show(Page.Field), 21);
            UiEntranceMotion.Play(agenda.gameObject, .86f);
            UiEntranceMotion.Play(plans.gameObject, .86f);
        }

        private IEnumerable<StoryEpisodeData> GxCampusContacts(int region)
        {
            var target = CurrentRpgEpisode;
            var reserved = target != null && target.regionId == CampaignRegions[region].Id ? target : null;
            return GxResidents(region, RpgClears, reserved).Take(2);
        }

        private void AdvanceCampusTime()
        {
            if (_page != Page.GxCampus || !GxCampusActive || RpgInterludeActive) return;
            var next = GxCampusSave.Copy();
            if (next.campusTime == 2)
            {
                if (CampaignRegions[_rpgLocation].Id != CampusDorm || next.campusDay == int.MaxValue) return;
                next.campusDay++;
                next.campusTime = 0;
            }
            else next.campusTime++;
            // Campus time does not refresh the campaign's one-time pickups.
            next.campusConversations.Clear();
            if (!CollectionProgress.TrySaveTagForce(RpgCampaignId, next, 0, out string error))
            {
                ShowStatus(error);
                return;
            }
            ShowStatus("");
            BeginGxInterlude(_rpgLocation, null, () => Show(Page.Field), null,
                $"{next.campusDay}일차  ·  {CampusPeriods[next.campusTime]}");
        }

        private void AddCampusBreadOffer(RectTransform panel, Text conversation,
            List<(string Id, string Label, Action Select)> actions)
        {
            var offer = Area(panel, "CampusBreadOffer", .742f, .348f, .927f, .596f);
            RpgUiStyle.Glass(offer);
            var bread = _images.Get(RpgArt + "Items/campus_melon_bread.png") ?? RpgUiStyle.Glyph(RpgUiStyle.Symbol.Star);
            DuelUiKit.AddImage(Area(offer, "Bread", .20f, .31f, .80f, .93f).gameObject, bread, Color.white, true, false);
            var stock = HudLabel(Area(offer, "Stock", .08f, .065f, .92f, .30f), BreadStock(), 19,
                RpgUiStyle.Gold, align: TextAnchor.MiddleCenter);
            actions.Add(("BuyCampusBread", $"멜론빵 사기 · {CampusBreadPrice} DP", () =>
            {
                var next = GxCampusSave.Copy();
                if (next.Count(CampusBreadId) >= CampusBreadLimit)
                {
                    StartTypingDialogue(conversation, "가방에 빵이 꽉 찼어요. 몇 개 나눠 준 뒤에 다시 와요.");
                    return;
                }
                next.ChangeItem(CampusBreadId, 1);
                if (CollectionProgress.TrySaveTagForce(RpgCampaignId, next, -CampusBreadPrice, out string error))
                {
                    stock.text = BreadStock();
                    DuelAudio.PlayTagForceReward();
                    StartTypingDialogue(conversation, "방금 나온 멜론빵이에요. 들고 다닐 수 있게 종이봉투에 담아 줄게요.");
                }
                else StartTypingDialogue(conversation, error);
            }));
            string BreadStock() => $"멜론빵 · 보유 {GxCampusSave.Count(CampusBreadId)}개";
        }

        private void AddCampusGiftAction(RectTransform panel, int friend, Text conversation,
            List<(string Id, string Label, Action Select)> actions)
        {
            var plate = Area(panel, "CampusAffinity", .719f, .392f, .932f, .463f);
            RpgUiStyle.Speaker(plate);
            var caption = HudLabel(plate, CampusAffinityCaption(friend), 19,
                RpgUiStyle.Gold, align: TextAnchor.MiddleCenter, heading: true);
            actions.Add(("GiveCampusBread", "멜론빵 건네기", () =>
            {
                var next = GxCampusSave.Copy();
                var relation = next.EditRelationship(CampusFriendKey(friend));
                string feedback;
                if (relation.campusGiftDay == next.campusDay) feedback = "오늘은 아까 받은 걸로 충분해. 고마워.";
                else if (next.Count(CampusBreadId) == 0) feedback = "멜론빵은 본관 매점에서 살 수 있어.";
                else
                {
                    next.ChangeItem(CampusBreadId, -1);
                    relation.campusGiftDay = next.campusDay;
                    next.AddAffinity(CampusFriendKey(friend), 450);
                    if (CollectionProgress.TrySaveTagForce(RpgCampaignId, next, 0, out string error))
                    {
                        caption.text = CampusAffinityCaption(friend);
                        DuelAudio.PlayTagForceReward();
                        feedback = friend == CampusAsuka
                            ? "나 주는 거야? 고마워. 오늘 점심을 놓쳤는데 덕분에 살았네. 내일 보면 내가 먼저 인사할게."
                            : "겉이 아직 바삭하다! 고마워. 내일 또 보면 이번엔 내가 매점 자리 잡아 둘게.";
                    }
                    else feedback = error;
                }
                StartTypingDialogue(conversation, feedback);
            }));
        }

        private string CampusAffinityCaption(int friend)
        {
            var relation = GxCampusSave.Relationship(CampusFriendKey(friend));
            return $"친밀도  {relation.affinity:N0} / {TagForceRelation.MaxAffinity:N0}";
        }

        private void TalkAboutCampus(int friend, Text conversation)
        {
            var next = GxCampusSave.Copy();
            string key = CampusFriendKey(friend);
            var relation = next.EditRelationship(key);
            bool followUp = relation.campusGiftDay > relation.campusThankedGiftDay && relation.campusGiftDay < next.campusDay;
            if (followUp || !next.campusConversations.Contains(key))
            {
                if (!next.campusConversations.Contains(key)) next.campusConversations.Add(key);
                if (followUp) relation.campusThankedGiftDay = relation.campusGiftDay;
                next.AddAffinity(key, followUp ? 200 : 110);
                if (!CollectionProgress.TrySaveTagForce(RpgCampaignId, next, 0, out string error))
                {
                    StartTypingDialogue(conversation, error);
                    return;
                }
                var affinity = _body.Find("ResidentChoices/CampusAffinity")?.GetComponentInChildren<Text>();
                if (affinity != null) affinity.text = CampusAffinityCaption(friend);
            }
            string giftWhen = next.campusDay - relation.campusGiftDay == 1 ? "어제" : "지난번에 준";
            string line = followUp ? friend == CampusAsuka
                ? $"윤, {giftWhen} 빵 잘 먹었어. 오늘은 내가 매점 창가 자리를 봐 둘게. 시간 나면 들러."
                : $"윤! {giftWhen} 멜론빵 진짜 맛있더라. 오늘은 내가 먼저 와 있었어. 자리 맡아 뒀으니까 이따 같이 가자."
                : (friend, next.campusTime) switch
                {
                    (CampusAsuka, 0) => "아침 일찍 왔네. 첫 수업 전엔 본관이 이렇게 조용해서 좋아.",
                    (CampusAsuka, 1) => "수업 끝났어? 난 조금 더 있다 갈게. 오늘 연습한 덱을 한 번 더 보고 싶어.",
                    (CampusAsuka, _) => "저녁엔 기숙사 앞 바람이 시원해. 정신없을 때는 여기서 잠깐 쉬게 돼.",
                    (CampusJudai, 0) => "아직 비어 있는 테이블 봤어? 수업 끝나면 거기서 한 판 하자.",
                    (CampusJudai, 1) => "매점 쪽에서 빵 굽는 냄새 난다. 한 판 하고 갈까, 먼저 빵부터 살까?",
                    _ => "오늘 덱은 더 만지면 오히려 꼬일 것 같아. 한숨 자고 내일 다시 봐야지.",
                };
            StartTypingDialogue(conversation, line);
        }
    }
}
