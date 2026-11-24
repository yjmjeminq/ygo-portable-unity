using System;
using System.Collections.Generic;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    [Serializable]
    public sealed class StoryDialogueOption
    {
        public string stance;
        public string text;
        public string response;
    }

    internal static class StoryDialogueBranch
    {
        internal readonly struct Choice
        {
            internal readonly StoryStance Stance;
            internal readonly string Reply;
            internal readonly string Response;

            internal Choice(StoryStance stance, string reply, string response)
            {
                Stance = stance;
                Reply = reply;
                Response = response;
            }
        }

        private static readonly HashSet<int> Rivals = new() { 94, 16, 43, 50, 156, 160, 190, 189, 113 };
        private static readonly HashSet<int> Villains = new() { 79, 103, 124, 126, 136, 74, 150, 172, 182, 3203, 36 };

        internal static string Effect(StoryStance stance) => stance switch
        {
            StoryStance.Challenge => $"도발 · 기본 상대 시작 패 +{StoryBattlePlan.ExtraOpeningCards}장 · 승리 DP·가루 ×1.5",
            StoryStance.Calm => "침착 · 내 시작 LP +1,000",
            StoryStance.Bond => "유대 · 승리 시 유대도 +1 · 테마 카드 드롭 ×2",
            _ => "",
        };

        internal static int AfterLine(StoryEpisodeData episode)
        {
            var lines = episode.preDialogues;
            if (episode.choiceAfterLine >= 1 && episode.choiceAfterLine <= lines.Length) return episode.choiceAfterLine - 1;
            int opponentLine = Array.FindLastIndex(lines, line => line.speaker == episode.opponentName);
            return opponentLine >= 0 ? opponentLine : Math.Max(0, lines.Length / 2);
        }

        internal static Choice[] Create(StoryEpisodeData episode, bool raid, string ace)
        {
            var custom = episode.choices?.Where(option => option != null && !string.IsNullOrWhiteSpace(option.text) &&
                    !string.IsNullOrWhiteSpace(option.response))
                .Select(option => (Option: option, Valid: Enum.TryParse<StoryStance>(option.stance, true, out var stance), Stance: stance))
                .Where(entry => entry.Valid && entry.Stance is StoryStance.Challenge or StoryStance.Calm or StoryStance.Bond)
                .Select(entry => new Choice(entry.Stance, entry.Option.text, entry.Option.response)).Take(3).ToArray();
            if (custom?.Length >= 2) return custom;

            string subject = string.IsNullOrWhiteSpace(ace) ? "당신의 에이스" : $"「{ace}」";
            string attack = raid ? $"거대한 벽이라도 넘겠어. {subject}, 정면에서 꺾어 보이겠어!" :
                $"{subject}를 막아 내고 내 실력을 증명하겠어!";
            string focus = raid ? "시작부터 전력이라면, 나는 버틸 수부터 준비할게." : "한 수씩 풀어 보겠어. 내 덱의 가능성을 지켜봐 줘.";
            string respect = raid ? "이런 전력을 마주할 기회는 드물어. 당신의 한 수를 배워 갈게." :
                "한 판 부탁해. 당신이 덱을 운용하는 법을 배우고 싶어.";
            (string Challenge, string Calm, string Bond) replies;
            if (Villains.Contains(episode.charId))
                replies = ($"{subject} 앞에서도 그 말을 할 수 있는지 두고 보지.", "냉정함을 잃지 않는군. 그 판단이 언제까지 통할지 볼까.",
                    "배우겠다고? 그렇다면 내 승리로 첫 수업을 마쳐 주지.");
            else if (Rivals.Contains(episode.charId))
                replies = ($"좋아. 그 자신감만큼 {subject}를 상대할 수를 보여 줘.", "쓸데없는 말은 없군. 네 첫 수를 보겠다.",
                    "조언이라면 하나다. 내 전개가 끝날 때까지 기다리지 마라.");
            else if (raid)
                replies = ($"정면 승부라면 받아 주지. {subject}와 함께 전력을 보여 주겠어.", "좋은 판단이야. 준비한 수를 끝까지 잊지 마.",
                    "그 마음이면 배울 수 있어. 내 첫 카드가 다음 수를 어떻게 여는지 봐.");
            else
                replies = ($"그 말, 기억해 둘게. {subject}를 꺼내면 꼭 받아 줘!", "좋아. 침착한 듀얼에서 네 실력을 보여 줘.",
                    "나도 네 듀얼을 배울게. 카드 한 장보다 그 다음 수를 봐.");
            return new[]
            {
                new Choice(StoryStance.Challenge, attack, replies.Challenge),
                new Choice(StoryStance.Calm, focus, replies.Calm),
                new Choice(StoryStance.Bond, respect, replies.Bond),
            };
        }
    }
}
