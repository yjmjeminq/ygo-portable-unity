using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal sealed class DuelAiPolicy
    {
        internal const string ResourceName = "DuelAiPolicy";
        private const int Version = 2;
        private const uint Development = (1u << 4) | (1u << 5) | (1u << 6) | (1u << 14) | (1u << 18);
        private const uint Disruption = (1u << 0) | (1u << 1) | (1u << 2) | (1u << 3) | (1u << 8) | (1u << 13);
        private const uint Negation = 1u << 8;

        [Flags]
        internal enum Decisions { MainPhase = 1, Chains = 2, Both = MainPhase | Chains }
        internal const int FirstChainFeature = (int)Feature.Chain;

        private enum Feature
        {
            NormalSummon, SpecialSummon, SummonAttack, SummonStarter, SummonControl, ExtraSummon, ExtraCommitment,
            Activate, ActivateStarter, ActivateExisting, ActivateControl, ActivateThreat,
            Battle, BattleOpen, BattleDamage,
            Chain, ChainResponse, ChainDisruption, ChainNegate, ChainWindow, ChainEnd, ChainDevelopment,
            Count,
        }

        [Serializable]
        private sealed class Model
        {
            public int version;
            public float[] weights;
            public Decisions decisions;
        }

        private readonly float[] _weights;
        internal Decisions EnabledDecisions { get; }
        internal bool UsesMainPhase => (EnabledDecisions & Decisions.MainPhase) != 0;
        internal bool UsesChains => (EnabledDecisions & Decisions.Chains) != 0;

        internal DuelAiPolicy(float[] weights, Decisions decisions)
        {
            if ((decisions & Decisions.Both) == 0 || (decisions & ~Decisions.Both) != 0 ||
                weights == null || weights.Length != (int)Feature.Count ||
                weights.Any(weight => float.IsNaN(weight) || Math.Abs(weight) > 8))
                throw new ArgumentException("Invalid duel policy weights.", nameof(weights));
            _weights = (float[])weights.Clone();
            EnabledDecisions = decisions;
        }

        internal static DuelAiPolicy LoadIncluded()
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null) return null;
            try
            {
                var model = JsonUtility.FromJson<Model>(asset.text);
                return model?.version == Version ? new DuelAiPolicy(model.weights, model.decisions) : null;
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning($"[Duel AI] Could not load the included policy. {exception.Message}");
                return null;
            }
        }

        internal string ToJson() => JsonUtility.ToJson(new Model { version = Version, weights = _weights, decisions = EnabledDecisions }, true);

        internal static float[] InitialWeights()
        {
            var weights = new float[(int)Feature.Count];
            weights[(int)Feature.NormalSummon] = 1;
            weights[(int)Feature.SpecialSummon] = 1.5f;
            weights[(int)Feature.SummonAttack] = 1;
            weights[(int)Feature.SummonStarter] = .65f;
            weights[(int)Feature.SummonControl] = .5f;
            weights[(int)Feature.ExtraSummon] = .1f;
            weights[(int)Feature.ExtraCommitment] = -.65f;
            weights[(int)Feature.Activate] = 1.25f;
            weights[(int)Feature.ActivateStarter] = .55f;
            weights[(int)Feature.ActivateExisting] = .25f;
            weights[(int)Feature.ActivateControl] = -.35f;
            weights[(int)Feature.ActivateThreat] = .85f;
            weights[(int)Feature.Battle] = .15f;
            weights[(int)Feature.BattleOpen] = .25f;
            weights[(int)Feature.BattleDamage] = 1.15f;
            weights[(int)Feature.Chain] = .2f;
            weights[(int)Feature.ChainResponse] = .8f;
            weights[(int)Feature.ChainDisruption] = -.35f;
            weights[(int)Feature.ChainNegate] = .8f;
            weights[(int)Feature.ChainWindow] = .6f;
            weights[(int)Feature.ChainEnd] = .35f;
            weights[(int)Feature.ChainDevelopment] = .45f;
            return weights;
        }

        internal int? ChooseMainAction(DuelSession session, DuelPrompt prompt, bool canBattle, bool preserveOpeningBoard,
            Func<int, SelectableCardItem, bool> canUse)
        {
            var own = session.Players[prompt.playerId];
            var rival = session.Players[1 - prompt.playerId];
            int bodies = 0, ownAttack = 0, strongestOwn = 0, strongestRival = 0, directDamage = 0, opposition = 0;
            foreach (var card in own.mzone)
            {
                if (card == null) continue;
                bodies++;
                if (!card.IsFaceUp) continue;
                ownAttack += Math.Max(0, card.attack);
                strongestOwn = Math.Max(strongestOwn, card.attack);
                if (card.IsAttack) directDamage += Math.Max(0, card.attack);
            }
            foreach (var card in rival.mzone)
            {
                if (card == null) continue;
                opposition++;
                if (card.IsFaceUp) strongestRival = Math.Max(strongestRival, card.IsDefense ? card.defense : card.attack);
            }
            bool openBoard = opposition == 0;
            foreach (var card in rival.szone)
                if (card != null) opposition++;
            float threat = Math.Max(Math.Min(4, opposition) * .25f, Math.Clamp((strongestRival - strongestOwn) / 3000f, 0, 1));

            int? choice = canBattle ? 6 : null;
            float best = canBattle ? Weight(Feature.Battle) + (openBoard ? Weight(Feature.BattleOpen) +
                Weight(Feature.BattleDamage) * Math.Min(1, (float)directDamage / Math.Max(rival.lp, 1)) : 0) : float.NegativeInfinity;

            void Consider(List<SelectableCardItem> items, int command)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    bool extra = command == 1 && item.location == YgoConstants.LOCATION_EXTRA;
                    if ((extra && preserveOpeningBoard) || !canUse(command, item)) continue;

                    var card = CardDatabase.Get(item.code);
                    uint flags = card?.DeckEffectCategories ?? 0;
                    float score;
                    if (command == 5)
                    {
                        score = Weight(Feature.Activate);
                        if ((flags & Development) != 0) score += Weight(Feature.ActivateStarter);
                        if (item.location != YgoConstants.LOCATION_HAND) score += Weight(Feature.ActivateExisting);
                        if ((flags & Disruption) != 0) score += Weight(Feature.ActivateControl) + threat * Weight(Feature.ActivateThreat);
                        if (score <= 0 && prompt.canEp) continue;
                    }
                    else
                    {
                        float attack = Math.Max(0, card?.atk ?? 1000);
                        score = Weight(command == 0 ? Feature.NormalSummon : Feature.SpecialSummon) +
                            Weight(Feature.SummonAttack) * Math.Min(1, attack / 4000);
                        if ((flags & Development) != 0) score += Weight(Feature.SummonStarter);
                        if ((flags & Disruption) != 0) score += Weight(Feature.SummonControl);
                        if (extra) score += Weight(Feature.ExtraSummon) +
                            Weight(Feature.ExtraCommitment) * Math.Clamp((ownAttack - attack) / 5000, 0, 1);
                        if (extra && bodies > 0 && score <= 0 && prompt.canEp) continue;
                    }

                    if (score <= best) continue;
                    best = score;
                    choice = (i << 16) | command;
                }
            }

            if (bodies < 5) Consider(prompt.spSummonable, 1);
            Consider(prompt.activatable, 5);
            Consider(prompt.summonable, 0);
            return choice;
        }

        internal float ChainPriority(DuelSession session, DuelPrompt prompt, SelectableCardItem effect)
        {
            uint flags = CardDatabase.Get(effect.code)?.DeckEffectCategories ?? 0;
            float score = Weight(Feature.Chain);
            if ((flags & Disruption) != 0) score += Weight(Feature.ChainDisruption);
            if ((flags & Development) != 0) score += Weight(Feature.ChainDevelopment);
            if (session.CurrentChain.Count > 0 && session.CurrentChain[^1] is { negated: false } last && last.triggerPlayer != prompt.playerId)
            {
                score += Weight(Feature.ChainResponse);
                if ((flags & Negation) != 0) score += Weight(Feature.ChainNegate);
            }

            const uint interaction = YgoConstants.TIMING_SUMMON | YgoConstants.TIMING_SPSUMMON |
                YgoConstants.TIMING_FLIPSUMMON | YgoConstants.TIMING_ATTACK;
            if (effect.isEventSpecificEffect || (prompt.hintTimingOther & interaction) != 0) score += Weight(Feature.ChainWindow);
            if (session.TurnPlayer != prompt.playerId && (prompt.hintTimingOther & YgoConstants.TIMING_END_PHASE) != 0)
                score += Weight(Feature.ChainEnd);
            return score;
        }

        private float Weight(Feature feature) => _weights[(int)feature];
    }
}
