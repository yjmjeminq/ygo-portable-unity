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
        private RpgSideQuest _rpgQuestResident;
        private int _rpgQuestPage;
        private RectTransform _rpgQuestRewardBanner;
        private float _rpgQuestRewardTime;
        private static readonly Vector2 RpgQuestGiverPosition = new(.34f, .245f);

        private static string RpgQuestName(RpgSideQuest quest) => DeckRepository.GetCharacter(quest.Giver)?.name ?? "의뢰인";

        private static string RpgQuestRegionName(RpgSideQuest quest) =>
            RpgRegions.First(region => region.Id == quest.Region).Name;

        private static string RpgQuestReturn(RpgSideQuest quest) => quest.Id == RpgSideQuest.GarageCouplerId
            ? "카이의 정비소 작업대로 돌아가 커플러 건네기"
            : $"{RpgQuestRegionName(quest)}의 {RpgQuestName(quest)}에게 돌아가기";

        private static IEnumerable<RpgSideQuest> RpgPendingDeliveries(RpgSideQuest receiver) =>
            RpgSideQuest.All.Where(quest => quest.Receiver == receiver.Id &&
                CollectionProgress.GetRpgQuestState(quest.Id) == RpgQuestState.Active);

        private void OpenRpgQuestTalk(RpgSideQuest quest)
        {
            _resident = null;
            _rpgQuestResident = quest;
            Show(Page.Talk);
        }

        private static string RpgQuestReward(RpgSideQuest quest) => FormatRpgQuestReward(
            quest.Reward, quest.Experience, quest.Affinity, quest.PackTicket);

        private static string FormatRpgQuestReward(int dp, int experience, int affinity, bool packTicket)
        {
            var rewards = new List<string>();
            if (dp > 0) rewards.Add($"+{dp:N0} DP");
            if (experience > 0) rewards.Add($"+{experience} EXP");
            if (affinity > 0) rewards.Add($"호감도 +{affinity}");
            if (packTicket) rewards.Add("UR 팩 획득권 ×1");
            return string.Join(" · ", rewards);
        }

        private (RpgSideQuest Quest, string Objective)? AddRpgSideQuest(RpgSideQuest quest)
        {
            var state = CollectionProgress.GetRpgQuestState(quest.Id);
            var delivery = RpgPendingDeliveries(quest).FirstOrDefault();
            if (!_rpgResidents.ContainsKey(quest.Giver))
            {
                bool atWorkbench = quest.Giver == RpgCrow && RpgCouplerState == RpgQuestState.Complete;
                var spot = atWorkbench ? RpgQuestCrowBench : RpgQuestGiverPosition;
                var role = delivery != null ? RpgResidentRole.Delivery : state switch
                {
                    RpgQuestState.Available => RpgResidentRole.Request,
                    RpgQuestState.Ready => RpgResidentRole.Reward,
                    _ => RpgResidentRole.Conversation,
                };
                AddRpgResident(quest.Giver, RpgQuestName(quest), $"QuestNpc_{quest.Giver}", spot,
                    role, () => OpenRpgQuestTalk(quest), patrol: role == RpgResidentRole.Request && !atWorkbench);
            }

            if (state == RpgQuestState.Active && quest.Receiver == null)
            {
                var item = Anchor(_rpgSceneSpace, "SideQuestItem", quest.Point, new Vector2(160f, 56f));
                void Investigate() => AdvanceRpgQuest(quest, RpgQuestState.Active, Page.Field,
                    $"{quest.Item} 확보 · {RpgQuestName(quest)}에게 돌아가세요.");
                void Approach() => MoveTo(quest.Point, Investigate);
                RpgButton(item, "조사 · " + quest.Item, Approach, 16, primary: true);
                RegisterRpgInteraction(item, $"{quest.Item} · 줍기", Approach, RpgActionPriority.Request);
            }

            if ((_rpgLocation == RpgGarage || _rpgLocation == RpgBridge) &&
                RpgCouplerState is RpgQuestState.Active or RpgQuestState.Ready)
                return (RpgCouplerQuest, RpgCouplerState == RpgQuestState.Active ? RpgCouplerQuest.Objective : RpgQuestReturn(RpgCouplerQuest));
            if (delivery != null) return (delivery, delivery.Objective);
            if (state == RpgQuestState.Active) return (quest, quest.Objective);
            if (state == RpgQuestState.Ready) return (quest, RpgQuestReturn(quest));
            foreach (var pending in RpgSideQuest.All)
            {
                var status = CollectionProgress.GetRpgQuestState(pending.Id);
                if (status == RpgQuestState.Active) return (pending, pending.Objective);
                if (status == RpgQuestState.Ready) return (pending, RpgQuestReturn(pending));
            }
            return null;
        }

        private static string RpgQuestDialogue(RpgSideQuest quest)
        {
            var delivery = RpgPendingDeliveries(quest).FirstOrDefault();
            if (delivery != null) return $"혹시 {delivery.Item} 가져왔어?";
            var state = CollectionProgress.GetRpgQuestState(quest.Id);
            if (quest.Giver == RpgCrow && state is RpgQuestState.Available or RpgQuestState.Complete)
            {
                if (RpgCouplerState == RpgQuestState.Complete)
                    return "커플러 덕에 시동이 잘 걸려. 오늘은 작업대 옆에서 러너를 마저 손볼게.";
                if (RpgCouplerState == RpgQuestState.Active)
                    return "다리 난간 가까이 세워 뒀었어. 거기부터 찾아보면 될 거야.";
                if (RpgCouplerState == RpgQuestState.Ready)
                    return "찾았구나! 작업대로 가져오면 바로 끼워 볼게.";
            }
            return state switch
            {
                RpgQuestState.Active => quest.Objective,
                RpgQuestState.Ready => "수고했어. 약속한 보상을 받아 가.",
                RpgQuestState.Complete => $"{quest.Item} 건은 지난번에 도와줘서 고마웠어.",
                _ => $"{quest.Greeting}\n보상  {RpgQuestReward(quest)}",
            };
        }

        private void AppendRpgQuestActions(RpgSideQuest quest, List<(string Id, string Label, Action Select)> actions)
        {
            var state = CollectionProgress.GetRpgQuestState(quest.Id);
            if (state == RpgQuestState.Available)
                actions.Add(("AcceptQuest", "의뢰 받기", () => AdvanceRpgQuest(quest, RpgQuestState.Available,
                    Page.Talk, $"{quest.Title} · 의뢰 수락")));
            else if (state == RpgQuestState.Ready)
                actions.Add(("ClaimQuest", "보상 받기", () => AdvanceRpgQuest(quest, RpgQuestState.Ready, Page.Talk, null)));

            foreach (var delivery in RpgPendingDeliveries(quest))
                actions.Add(("DeliverQuest", "물품 전달", () => AdvanceRpgQuest(delivery, RpgQuestState.Active,
                    Page.Talk, $"{delivery.Item} 전달 완료 · {RpgQuestName(delivery)}에게 알려 주세요.")));

            if (quest.Giver == RpgCrow && RpgCouplerState == RpgQuestState.Available && IsRpgQuestOpen(RpgCouplerQuest, RpgClears))
                actions.Add(("AskCoupler", "커플러 행방 묻기", () => AdvanceRpgQuest(RpgCouplerQuest, RpgQuestState.Available,
                    Page.Talk, "크로우가 러너를 세웠던 다리 난간을 알려 줬습니다.")));
        }

        private void AdvanceRpgQuest(RpgSideQuest quest, RpgQuestState from, Page page, string message)
        {
            int experience = CollectionProgress.RpgExperience;
            int affinity = CollectionProgress.RpgAffinity;
            int tickets = CollectionProgress.PackUrTickets;
            if (!CollectionProgress.TryAdvanceRpgQuest(quest.Id, from, out int reward, out string error))
            {
                ShowStatus(error);
                return;
            }
            Show(page);
            if (from != RpgQuestState.Ready)
            {
                ShowStatus(message);
                return;
            }
            DuelAudio.PlayTagForceReward();
            string received = FormatRpgQuestReward(reward, CollectionProgress.RpgExperience - experience,
                CollectionProgress.RpgAffinity - affinity, CollectionProgress.PackUrTickets > tickets);
            _rpgQuestRewardBanner = Area(_body, "QuestRewardBanner", .327f, .852f, .778f, .953f);
            RpgUiStyle.Panel(_rpgQuestRewardBanner, gold: true);
            HudLabel(_rpgQuestRewardBanner, $"의뢰 완료!  {quest.Title}\n{received}", 21, Color.white,
                align: TextAnchor.MiddleCenter, heading: true);
            _rpgQuestRewardTime = 3.4f;
            UiEntranceMotion.Play(_rpgQuestRewardBanner.gameObject, .84f, .2f);
        }

        private void UpdateRpgQuestReward(float delta)
        {
            if (_rpgQuestRewardBanner == null) return;
            _rpgQuestRewardTime -= delta;
            if (_rpgQuestRewardTime <= 0f) _rpgQuestRewardBanner.gameObject.SetActive(false);
        }

        private void BuildRpgQuestJournal()
        {
            if (IsGxCampaign) { BuildGxStoryJournal(); return; }
            const int perPage = 4;
            BuildRpgMenuBackdrop();
            int clears = RpgClears;
            var quests = RpgSideQuest.All.Where(quest => IsRpgQuestOpen(quest, clears) &&
                (quest.Id != RpgSideQuest.GarageCouplerId || RpgCouplerState != RpgQuestState.Available))
                .Select(quest => (Quest: quest, State: CollectionProgress.GetRpgQuestState(quest.Id)))
                .OrderBy(item => item.State is RpgQuestState.Active or RpgQuestState.Ready ? 0 :
                    item.State == RpgQuestState.Available ? 1 : 2).ToArray();
            int complete = RpgSideQuest.All.Count(quest => CollectionProgress.GetRpgQuestState(quest.Id) == RpgQuestState.Complete);
            HudLabel(Area(_body, "QuestJournalHeading", .08f, .882f, .92f, .98f),
                $"의뢰 수첩  ·  {complete}/{RpgSideQuest.All.Length} 완료", 26, Color.white, heading: true);
            int pages = Mathf.Max(1, (quests.Length + perPage - 1) / perPage);
            _rpgQuestPage = Mathf.Clamp(_rpgQuestPage, 0, pages - 1);
            var journal = Area(_body, "QuestJournal", .08f, .16f, .92f, .86f);
            var entries = DuelUiKit.FillRect("Items", journal);
            int rowIndex = 0;
            foreach (var (quest, state) in quests.Skip(_rpgQuestPage * perPage).Take(perPage))
            {
                string progress = state switch
                {
                    RpgQuestState.Active => "진행 중",
                    RpgQuestState.Ready => "보고 대기",
                    RpgQuestState.Complete => "완료",
                    _ => "수락 가능",
                };
                string detail = state switch
                {
                    RpgQuestState.Active => quest.Objective,
                    RpgQuestState.Ready => RpgQuestReturn(quest),
                    RpgQuestState.Complete => "보상 수령 완료",
                    _ => $"{RpgQuestRegionName(quest)} · {RpgQuestName(quest)}",
                };
                float top = 1f - rowIndex++ / (float)perPage;
                var row = Area(entries, quest.Id, 0f, top - .223f, 1f, top - .01f);
                RpgUiStyle.Panel(row, accent: state is RpgQuestState.Active or RpgQuestState.Ready);
                HudLabel(Area(row, "Title", .026f, .48f, .974f, .93f), $"[{progress}]  {quest.Title}",
                    22, Color.white, heading: true);
                HudLabel(Area(row, "Detail", .026f, .22f, .974f, .49f), detail, 18, new Color(.72f, .83f, .90f));
                if (state != RpgQuestState.Complete)
                    HudLabel(Area(row, "Reward", .026f, .024f, .974f, .23f), RpgQuestReward(quest), 17, DuelUiKit.GoldAccent);
            }
            if (pages == 1) return;
            RpgButton(Area(_body, "QuestPagePrev", .08f, .027f, .29f, .126f), "이전", () => ChangePage(-1))
                .interactable = _rpgQuestPage > 0;
            RpgButton(Area(_body, "QuestPageNext", .71f, .027f, .92f, .126f), "다음", () => ChangePage(1))
                .interactable = _rpgQuestPage + 1 < pages;
            HudLabel(Area(_body, "QuestPage", .32f, .029f, .68f, .126f), $"{_rpgQuestPage + 1} / {pages}", 18, Color.white,
                align: TextAnchor.MiddleCenter);

            void ChangePage(int offset)
            {
                _rpgQuestPage += offset;
                Show(Page.Ledger);
            }
        }
    }
}
