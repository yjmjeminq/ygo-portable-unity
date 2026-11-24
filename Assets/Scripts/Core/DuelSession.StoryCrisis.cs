using System;
using System.Collections.Generic;
using System.Linq;

namespace YGOPortable.Core
{
    public partial class DuelSession
    {
        private static readonly uint[] StormLinkPool = { 1861629, 86066372, 50588353 };
        private StoryGimmickRuntime _storyRuntime;

        internal StoryCrisisSkill PendingStoryCrisis => ActivePrompt?.messageType == YgoConstants.MSG_STORY_CRISIS
            ? _storyRuntime?.CrisisSkill ?? StoryCrisisSkill.None : StoryCrisisSkill.None;

        private static bool IsManagedDrawPrompt(DuelPrompt prompt) =>
            prompt?.messageType is YgoConstants.MSG_DESTINY_DRAW or YgoConstants.MSG_STORY_CRISIS;

        private void OfferDrawSkills(byte player)
        {
            if (player == HumanPlayerIndex && _storyRuntime?.CanActivateCrisisSkill(Players[player].lp) == true)
                ActivePrompt = new DuelPrompt { messageType = YgoConstants.MSG_STORY_CRISIS, playerId = player };
            else
                OfferDestinyDraw(player);
        }

        internal IReadOnlyList<LiveCardState> ShiningDrawChoices()
        {
            if (PendingStoryCrisis != StoryCrisisSkill.ShiningDraw) return Array.Empty<LiveCardState>();
            return QueryLocationCards((byte)HumanPlayerIndex, YgoConstants.LOCATION_DECK, keepNulls: false)
                .Where(card => card.code != 0)
                .GroupBy(card => card.code).Select(copies => copies.First())
                .OrderBy(card => CardDatabase.GetName(card.code), StringComparer.Ordinal).ToArray();
        }

        private void ResolveStoryCrisis(byte player, int answer)
        {
            if (answer <= 0 || _storyRuntime?.CanActivateCrisisSkill(Players[player].lp) != true)
            {
                OfferDestinyDraw(player);
                return;
            }

            if (_storyRuntime.CrisisSkill == StoryCrisisSkill.ShiningDraw)
            {
                uint sequence = (uint)(answer - 1);
                if (YgoNativeBridge.YGO_MoveDeckCardToTop(NativePtr, player, sequence) != 0)
                {
                    _storyRuntime.RecordCrisisUse();
                    ForgetObservedDeckTop(player);
                    AddLog("플레이어 샤이닝 드로우 발동!");
                    return;
                }
            }
            else if (TryAccessStormLink(player))
            {
                _storyRuntime.RecordCrisisUse();
            }
            OfferDestinyDraw(player);
        }

        private bool TryAccessStormLink(byte player)
        {
            var extra = QueryLocationCards(player, YgoConstants.LOCATION_EXTRA, keepNulls: false);
            uint code = StormLinkPool
                .Where(candidate => CardDatabase.Get(candidate)?.IsLink == true && YgoNativeBridge.YGO_HasCard(candidate) != 0)
                .Select(candidate => (Code: candidate, Copies: extra.Count(card => card.code == candidate)))
                .Where(candidate => candidate.Copies < 3)
                .OrderBy(candidate => candidate.Copies)
                .FirstOrDefault().Code;
            if (code == 0) return false;
            uint before = YgoNativeBridge.YGO_QueryCount(NativePtr, player, YgoConstants.LOCATION_EXTRA);
            YgoNativeBridge.YGO_AddCard(NativePtr, player, 0, code, player, YgoConstants.LOCATION_EXTRA, 0,
                YgoConstants.POS_FACEDOWN_DEFENSE);
            if (YgoNativeBridge.YGO_QueryCount(NativePtr, player, YgoConstants.LOCATION_EXTRA) != before + 1) return false;
            if (!_loadingPlanningReplay) RefreshZonesFromCore();
            AddLog($"플레이어 스톰 액세스 발동! {CardDatabase.GetName(code)}을(를) 엑스트라 덱에 추가했습니다.");
            return true;
        }
    }
}
