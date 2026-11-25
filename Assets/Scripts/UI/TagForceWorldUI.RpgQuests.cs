using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private RpgSideQuest _rpgQuestResident;
        private int _rpgQuestPage;
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

        private string AddRpgSideQuest(RpgSideQuest quest)
        {
            var state = CollectionProgress.GetRpgQuestState(quest.Id);
            var delivery = RpgPendingDeliveries(quest).FirstOrDefault();
            if (!_rpgResidents.ContainsKey(quest.Giver))
            {
                var spot = quest.Giver == RpgCrow && RpgCouplerState == RpgQuestState.Complete
                    ? RpgQuestCrowBench : RpgQuestGiverPosition;
                AddRpgResident(quest.Giver, RpgQuestName(quest), $"QuestNpc_{quest.Giver}", spot,
                    false, () => OpenRpgQuestTalk(quest));
                string badge = delivery != null ? "전달" : state switch
                {
                    RpgQuestState.Available => "의뢰",
                    RpgQuestState.Ready => "보상",
                    _ => null,
                };
                if (badge != null)
                {
                    var marker = Area(_rpgResidents[quest.Giver], "QuestBadge", .19f, 1.238f, .81f, 1.49f);
                    Tint(marker, new Color(.025f, .085f, .12f, .96f));
                    Label(marker, badge, 15).color = DuelUiKit.GoldAccent;
                }
            }

            if (state == RpgQuestState.Active && quest.Receiver == null)
            {
                var item = Anchor(_rpgSceneSpace, "SideQuestItem", quest.Point, new Vector2(160f, 56f));
                void Investigate() => AdvanceRpgQuest(quest, RpgQuestState.Active, Page.Field,
                    $"{quest.Item} 확보 · {RpgQuestName(quest)}에게 돌아가세요.");
                void Approach() => MoveTo(quest.Point, Investigate);
                Btn(item, "조사 · " + quest.Item, Approach, 15);
                RegisterRpgInteraction(item, $"{quest.Item} · 조사", Approach);
            }

            string hint = delivery?.Objective ?? (state switch
            {
                RpgQuestState.Active => quest.Objective,
                RpgQuestState.Ready => RpgQuestReturn(quest),
                _ => null,
            });
            if ((_rpgLocation == RpgGarage || _rpgLocation == RpgBridge) &&
                RpgCouplerState is RpgQuestState.Active or RpgQuestState.Ready)
                hint = RpgCouplerState == RpgQuestState.Active ? RpgCouplerQuest.Objective : RpgQuestReturn(RpgCouplerQuest);
            return hint;
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
                _ => quest.Greeting,
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
            if (!CollectionProgress.TryAdvanceRpgQuest(quest.Id, from, out int reward, out string error))
            {
                ShowStatus(error);
                return;
            }
            if (from == RpgQuestState.Ready) DuelAudio.PlayTagForceReward();
            Show(page);
            ShowStatus(message ?? $"{quest.Title} · 완료 · +{reward:N0} DP");
        }

        private void BuildRpgQuestJournal()
        {
            const int perPage = 4;
            Backdrop(null, new Color(.035f, .083f, .135f));
            int clears = RpgClears;
            var quests = RpgSideQuest.All.Where(quest => IsRpgQuestOpen(quest, clears) &&
                (quest.Id != RpgSideQuest.GarageCouplerId || RpgCouplerState != RpgQuestState.Available))
                .Select(quest => (Quest: quest, State: CollectionProgress.GetRpgQuestState(quest.Id)))
                .OrderBy(item => item.State is RpgQuestState.Active or RpgQuestState.Ready ? 0 :
                    item.State == RpgQuestState.Available ? 1 : 2).ToArray();
            int complete = RpgSideQuest.All.Count(quest => CollectionProgress.GetRpgQuestState(quest.Id) == RpgQuestState.Complete);
            Label(Area(_body, "QuestJournalHeading", .08f, .882f, .92f, .98f),
                $"의뢰 수첩  ·  {complete}/{RpgSideQuest.All.Length} 완료", 25, TextAnchor.MiddleLeft).color = DuelUiKit.GoldAccent;
            int pages = Mathf.Max(1, (quests.Length + perPage - 1) / perPage);
            _rpgQuestPage = Mathf.Clamp(_rpgQuestPage, 0, pages - 1);
            var entries = List("QuestJournal", .08f, .16f, .92f, .86f);
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
                    RpgQuestState.Complete => $"{quest.Reward:N0} DP 보상 수령",
                    _ => $"{RpgQuestRegionName(quest)} · {RpgQuestName(quest)} · 보상 {quest.Reward:N0} DP",
                };
                var row = Row(entries, $"[{progress}]  {quest.Title}\n{detail}", null, 77);
                Tint(row, new Color(.04f, .13f, .18f, .93f));
            }
            if (pages == 1) return;
            Btn(Area(_body, "QuestPagePrev", .08f, .027f, .29f, .126f), "이전", () => ChangePage(-1))
                .interactable = _rpgQuestPage > 0;
            Btn(Area(_body, "QuestPageNext", .71f, .027f, .92f, .126f), "다음", () => ChangePage(1))
                .interactable = _rpgQuestPage + 1 < pages;
            Label(Area(_body, "QuestPage", .32f, .029f, .68f, .126f), $"{_rpgQuestPage + 1} / {pages}", 18);

            void ChangePage(int offset)
            {
                _rpgQuestPage += offset;
                Show(Page.Ledger);
            }
        }
    }
}
