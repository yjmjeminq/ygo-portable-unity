using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal sealed class LinkComboPolicy
    {
        internal const string ResourceName = "LinkComboPolicy";
        internal const ulong LinkMaterial = 533;
        private const uint Deploy = (1u << 5) | (1u << 6) | (1u << 7) | (1u << 14);
        private const uint Interaction = (1u << 0) | (1u << 1) | (1u << 3) | (1u << 8) | (1u << 13);

        private enum Feature
        {
            Normal, Starter, Extend, Activate, EngineEffect, OnBoardEffect, Bridge, SmallLink,
            Boss, Rating, Interaction, SpendStrongLink, ExistingPlan, Count,
        }

        [Serializable]
        private sealed class Model
        {
            public int version;
            public float[] weights;
        }

        private readonly float[] _weights;

        internal LinkComboPolicy(float[] weights)
        {
            if (weights?.Length != (int)Feature.Count || weights.Any(w => float.IsNaN(w) || Math.Abs(w) > 8))
                throw new ArgumentException("Invalid Link policy weights.", nameof(weights));
            _weights = (float[])weights.Clone();
        }

        internal static LinkComboPolicy LoadIncluded()
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null) return null;
            try
            {
                var model = JsonUtility.FromJson<Model>(asset.text);
                return model?.version == 1 ? new LinkComboPolicy(model.weights) : null;
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning($"[Link AI] Could not load policy. {exception.Message}");
                return null;
            }
        }

        internal string ToJson() => JsonUtility.ToJson(new Model { version = 1, weights = _weights }, true);
        internal static float[] InitialWeights() => new[]
        {
            1.2f, 1.1f, 2.5f, 0.9f, 1.0f, .3f, 1.45f, .7f, 1.35f, .32f, .4f, -.8f, .45f,
        };

        internal static bool FitsDeck(PlayerFieldSnapshot player)
        {
            int links = 0, highest = 0;
            foreach (var card in player.extra)
            {
                int rank = Rank(card.code);
                if (rank == 0) continue;
                links++;
                highest = Math.Max(highest, rank);
            }
            return highest >= 3 && links >= 4 && links * 2 >= player.extra.Count;
        }

        internal int? Choose(DuelSession session, DuelPrompt prompt, Func<int, SelectableCardItem, bool> canUse)
        {
            var own = session.Players[prompt.playerId];
            var rival = session.Players[1 - prompt.playerId];
            var field = own.mzone.Where(card => card?.IsFaceUp == true).ToArray();
            int highest = field.Length == 0 ? 0 : field.Max(card => Rank(card.code));
            bool hasOpposition = rival.mzone.Any(card => card != null);
            if (prompt.canBp && !hasOpposition && field.Where(card => card.IsAttack).Sum(card => Math.Max(0, card.attack)) >= rival.lp)
                return 6;

            int? oldPlan = LinkTurnPlan.ChooseIdle(session, prompt, canUse);
            int? selected = null;
            float best = 0;
            void Consider(List<SelectableCardItem> choices, int command)
            {
                for (int i = 0; i < choices.Count; i++)
                {
                    var item = choices[i];
                    if (item.controller != prompt.playerId || !canUse(command, item)) continue;
                    var card = CardDatabase.Get(item.code);
                    uint tags = card?.DeckEffectCategories ?? 0;
                    bool engine = (tags & Deploy) != 0;
                    float score;
                    if (command == 0)
                    {
                        score = Weight(Feature.Normal) + Weight(Feature.Starter) * StarterValue(item.code, engine);
                        score += Math.Max(0, card?.atk ?? 0) / 10000f;
                    }
                    else if (command == 5)
                    {
                        score = Weight(Feature.Activate) + (engine ? Weight(Feature.EngineEffect) : 0) +
                            (item.location != YgoConstants.LOCATION_HAND ? Weight(Feature.OnBoardEffect) : 0);
                        if ((tags & Interaction) != 0 && hasOpposition) score += Weight(Feature.Interaction);
                        if (item.code == 36668118 && item.location == YgoConstants.LOCATION_SZONE) score += Weight(Feature.EngineEffect);
                    }
                    else if (item.location != YgoConstants.LOCATION_EXTRA)
                    {
                        score = Weight(Feature.Extend);
                    }
                    else if (card?.IsLink == true)
                    {
                        int rank = (int)card.level;
                        score = Weight(rank >= 3 ? Feature.Boss : Feature.SmallLink) + rank * Weight(Feature.Rating);
                        if (IsBridge(item.code)) score += Weight(Feature.Bridge);
                        if (rank >= 3 && IsInteractive(card)) score += Weight(Feature.Interaction);
                        if (highest >= 3 && rank <= highest) score += Weight(Feature.SpendStrongLink);
                        score += Math.Max(0, card.atk) / 15000f;
                    }
                    else
                    {
                        // An archetype's non-Link extension can still be the best legal route forward.
                        score = engine ? Weight(Feature.EngineEffect) : .15f;
                    }

                    int action = (i << 16) | command;
                    if (action == oldPlan) score += Weight(Feature.ExistingPlan);
                    if (score <= best) continue;
                    best = score;
                    selected = action;
                }
            }

            Consider(prompt.activatable, 5);
            Consider(prompt.spSummonable, 1);
            Consider(prompt.summonable, 0);
            return selected;
        }

        internal static bool CanUseSummon(PlayerFieldSnapshot own, SelectableCardItem summon)
        {
            if (summon.location != YgoConstants.LOCATION_EXTRA) return true;
            int next = Rank(summon.code);
            if (next == 0) return true;
            int largest = 0, spareBodies = 0;
            foreach (var card in own.mzone)
            {
                if (card?.IsFaceUp != true) continue;
                int rank = Rank(card.code);
                if (rank >= 3) largest = Math.Max(largest, rank);
                else spareBodies++;
            }
            if (largest == 0 || next > largest) return true;
            if (summon.code is 57134592 or 31313405 &&
                own.szone[YgoConstants.FIELD_ZONE_SEQUENCE] is { code: 1295111, IsFaceUp: true, effectsDisabled: false })
                return true;
            return spareBodies >= (next == 1 ? 1 : 2);
        }

        internal static int? ChooseMaterial(DuelSession session, DuelPrompt prompt, uint linkTarget, bool planLinks)
        {
            if ((prompt.SelectionHint != LinkMaterial && session.LastHintMsg != LinkMaterial) || linkTarget == 0 ||
                prompt.candidates.Count == 0 || prompt.finishable) return null;

            int targetRank = Rank(linkTarget);
            if (!planLinks && !prompt.candidates.Any(card => card.controller == prompt.playerId && IsDowngrade(Rank(card.code), targetRank)))
                return null;
            int bodies = session.Players[prompt.playerId].mzone.Count(card => card?.IsFaceUp == true);
            int remaining = targetRank - prompt.unselectCandidates.Sum(card => Math.Max(1, Rank(card.code)));
            int? choice = null;
            int best = int.MinValue;
            for (int i = 0; i < prompt.candidates.Count; i++)
            {
                var item = prompt.candidates[i];
                int rank = Rank(item.code);
                int score = DuelTactics.SelectionPriority(session, prompt.playerId, item, LinkMaterial);
                if (item.controller == prompt.playerId)
                {
                    if (rank >= targetRank && rank >= 3) score -= 4000;
                    if (rank > 1 && rank <= remaining && bodies < targetRank) score += 1300 + 250 * rank;
                    if (item.code is 20455229 or 18789533 or 30342076 or 27520594) score += 700;
                }
                if (score <= best) continue;
                best = score;
                choice = i;
            }
            return choice;
        }

        internal static int? ChooseSupport(DuelSession session, DuelPrompt prompt)
        {
            var link = session.SelectionChainLink;
            if (link?.triggerPlayer != prompt.playerId || link.negated || prompt.candidates.Count == 0 ||
                prompt.candidates.Any(card => card.controller != prompt.playerId)) return null;

            var own = session.Players[prompt.playerId];
            bool InHand(uint code) => own.hand.Any(card => card.code == code);
            bool OnField(uint code) => own.mzone.Any(card => card?.code == code);
            int? First(params uint[] codes)
            {
                foreach (uint code in codes)
                {
                    int index = prompt.candidates.FindIndex(card => card.code == code);
                    if (index >= 0) return index;
                }
                return null;
            }

            return (link.code, prompt.SelectionHint) switch
            {
                // Blue Tang sends a body that Blue Slug can return, then Sea Horse can summon itself.
                (91953000, DuelTactics.SendToGrave) => InHand(36492575) ? First(21057444, 28174796) : First(36492575, 21057444),
                (43735670, 506) => InHand(36492575) ? First(21057444, 91953000, 36492575) : First(36492575, 21057444, 91953000),
                (30691817, 506) => InHand(91027843) || own.szone[YgoConstants.FIELD_ZONE_SEQUENCE]?.code == 91027843
                    ? First(57329501, 91027843) : First(91027843, 57329501),
                (79130389, 509) => First(43735670, 30691817, 91953000, 36492575),
                (84546257, 506) => First(52945066),
                (31443476 or 68464358, 509) => OnField(68464358) ? First(5969957, 26655293, 67127799) : First(68464358, 5969957),
                (67748760, 506) => InHand(68464358) || OnField(68464358) ? First(67127799, 5969957) : First(68464358),
                (11969228, 506) => First(62265044),
                (20455229, 509) => First(86605184),
                (16020923 or 74567889, 506) => InHand(59054773) ? First(30118811) : First(59054773),
                (15808381, 506) => InHand(16020923) ? First(30118811, 41306080) : First(16020923, 30118811),
                _ => null,
            };
        }

        internal static int? ChooseMonsterZone(PlayerFieldSnapshot own, uint allowed, uint linkTarget)
        {
            int occupied = 0, linked = 0;
            for (int i = 0; i < own.mzone.Length; i++)
            {
                if (own.mzone[i] is not LiveCardState card) continue;
                occupied |= 1 << i;
                if (card.IsFaceUp) linked |= LinkedMainZones(i, card.linkMarker);
            }
            int? best = null;
            int bestValue = int.MinValue;
            var next = CardDatabase.Get(linkTarget);
            for (int i = 0; i < own.mzone.Length; i++)
            {
                if ((allowed & (1u << i)) == 0) continue;
                int links = next?.IsLink == true ? LinkedMainZones(i, next.link) & ~occupied : 0;
                int score = 0;
                for (int j = 0; j < 5; j++) if ((links & (1 << j)) != 0) score += 10;
                if (next?.IsLink == true && i >= 5) score += 3;
                if (next == null && (linked & (1 << i)) != 0) score -= 12;
                if (score <= bestValue) continue;
                best = i;
                bestValue = score;
            }
            return best;
        }

        private static int LinkedMainZones(int sequence, uint arrows)
        {
            int mask = 0;
            if (sequence >= 5)
            {
                int column = sequence == 5 ? 1 : 3;
                if ((arrows & 0x1) != 0) mask |= 1 << (column - 1);
                if ((arrows & 0x2) != 0) mask |= 1 << column;
                if ((arrows & 0x4) != 0) mask |= 1 << (column + 1);
            }
            else
            {
                if (sequence > 0 && (arrows & 0x8) != 0) mask |= 1 << (sequence - 1);
                if (sequence < 4 && (arrows & 0x20) != 0) mask |= 1 << (sequence + 1);
            }
            return mask;
        }

        internal static int Rank(uint code) => CardDatabase.Get(code) is { IsLink: true } card ? (int)card.level : 0;
        internal static bool IsDowngrade(int materialRank, int targetRank) => targetRank > 0 && materialRank >= 3 && materialRank > targetRank;
        internal static bool HasStrongerLink(PlayerFieldSnapshot own, int targetRank) => targetRank > 0 && own.mzone.Any(card =>
            card?.IsFaceUp == true && IsDowngrade(Rank(card.code), targetRank));
        internal static bool NeedsMaterialProjection(PlayerFieldSnapshot own, SelectableCardItem summon) =>
            summon.location == YgoConstants.LOCATION_EXTRA && HasStrongerLink(own, Rank(summon.code));
        internal static bool IsInteractive(CardInfo card) => (card.DeckEffectCategories & Interaction) != 0 ||
            card.code is 24151924 or 98630720 or 20934852 or 24361622;

        private static float StarterValue(uint code, bool engine) => code switch
        {
            // These starters replace themselves on summon or when first used as Link material.
            20455229 or 62706865 or 16188701 or 91953000 or 99885917 or 36492575 or
            11962031 or 26889158 or 15808381 or 16020923 or 27520594 or 25273572 => 1.8f,
            2347477 or 30114823 or 75130221 => .1f,
            _ => engine ? .85f : 0,
        };

        internal static bool IsBridge(uint code) => code is
            14812471 or 87871125 or 30342076 or 46947713 or 45462149 or 59859086 or 73539069 or 11969228 or
            43735670 or 30691817 or 79130389 or 61668670 or 97053215 or 74567889 or 93896655 or 21200905;

        private float Weight(Feature feature) => _weights[(int)feature];
    }
}
