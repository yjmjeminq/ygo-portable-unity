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

        private static string RpgQuestReturn(RpgSideQuest quest) => $"{RpgQuestRegionName(quest)}의 {RpgQuestName(quest)}에게 돌아가기";

        private static IEnumerable<RpgSideQuest> RpgPendingDeliveries(RpgSideQuest receiver) =>
            RpgSideQuest.All.Where(quest => quest.Receiver == receiver.Id &&
                CollectionProgress.GetRpgQuestState(quest.Id) == RpgQuestState.Active);

        private void AddRpgSideQuest(RpgSideQuest quest)
        {
            var state = CollectionProgress.GetRpgQuestState(quest.Id);
            var delivery = RpgPendingDeliveries(quest).FirstOrDefault();
            if (!_rpgResidents.ContainsKey(quest.Giver))
            {
                AddRpgResident(quest.Giver, RpgQuestName(quest), $"QuestNpc_{quest.Giver}", RpgQuestGiverPosition,
                    false, () =>
                    {
                        _resident = null;
                        _rpgQuestResident = quest;
                        Show(Page.Talk);
                    });
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
                Btn(item, "조사 · " + quest.Item, () => MoveTo(quest.Point, Investigate), 15);
                _interactions.Add((item, Investigate));
            }

            string hint = delivery?.Objective ?? (state switch
            {
                RpgQuestState.Active => quest.Objective,
                RpgQuestState.Ready => RpgQuestReturn(quest),
                _ => null,
            });
            if (hint == null) return;
            var tracker = Area(_rpgFieldHud, "SideQuestTrack", .02f, .805f, .77f, .875f);
            Tint(tracker, new Color(.018f, .07f, .10f, .92f));
            Label(tracker, $"의뢰  ·  {hint}", 17, TextAnchor.MiddleLeft).color = new Color(.63f, .86f, .95f);
        }

        private static string RpgQuestDialogue(RpgSideQuest quest)
        {
            var delivery = RpgPendingDeliveries(quest).FirstOrDefault();
            if (delivery != null) return $"혹시 {delivery.Item} 가져왔어?";
            return CollectionProgress.GetRpgQuestState(quest.Id) switch
            {
                RpgQuestState.Active => quest.Objective,
                RpgQuestState.Ready => "수고했어. 약속한 보상을 받아 가.",
                RpgQuestState.Complete => "지난번엔 정말 고마웠어.",
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
            var quests = RpgSideQuest.All.Where(quest => RpgRegions.Any(region => region.Id == quest.Region &&
                region.IsOpen(clears))).Select(quest => (Quest: quest, State: CollectionProgress.GetRpgQuestState(quest.Id)))
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
