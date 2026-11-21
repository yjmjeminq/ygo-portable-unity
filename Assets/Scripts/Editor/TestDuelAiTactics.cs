using System;
using System.Linq;
using UnityEngine;
using YGOPortable.AI;
using YGOPortable.Core;

namespace YGOPortable.Editor
{
    internal static class TestDuelAiTactics
    {
        private const uint Ash = 14558127, Upstart = 70368879, OriginalSpoils = 89023486, SnakeEyeAsh = 9674034;
        private const uint Stratos = 40044918, Reinforcement = 32807846, Baronne = 84815190;
        private const uint Nibiru = 27204311, LinkSpider = 98978921, Dandylion = 15341821;
        private const uint Foolish = 81439173, Passcode = 93104632, Imperm = 10045474, GeminiElf = 69140098;
        private const uint Merrli = 74078255, Sulliek = 74920585;
        private static readonly uint[] Empty = Array.Empty<uint>();

        internal static void Run()
        {
            VerifyNegationTargets();
            VerifyNativeFieldNegation(Imperm);
            VerifyNativeFieldNegation(DuelTactics.EffectVeiler);
            VerifyTriggerOrder();
            VerifyNativeTriggerProtection();
            VerifyAshCommitment();
            VerifyBaitAgainstBaronne();
            VerifySummonBudget();
            VerifyRevealedSweeper();
            Debug.Log("[VERIFY AI TACTICS] Ash conservation, Baronne bait, summon budget, revealed sweeper and chain ordering passed. Retry=0");
        }

        private static void VerifyNegationTargets()
        {
            using var duel = new DuelSession();
            var chain = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 1 };
            var impermanence = new SelectableCardItem { code = Imperm, controller = 1, location = YgoConstants.LOCATION_HAND };
            var veiler = new SelectableCardItem { code = 97268402, controller = 1, location = YgoConstants.LOCATION_HAND };
            duel.Players[0].mzone[0] = new LiveCardState { code = 83011277, position = YgoConstants.POS_FACEUP_ATTACK, type = YgoConstants.TYPE_EFFECT };
            duel.Players[0].mzone[1] = new LiveCardState { code = 60806437, position = YgoConstants.POS_FACEUP_ATTACK, type = YgoConstants.TYPE_EFFECT };
            Require(!DuelTactics.CanActivate(duel, chain, impermanence) && !DuelTactics.CanActivate(duel, chain, veiler),
                "Field negation should be held against Mystic Tomato and UFO Turtle.");
            duel.Players[0].mzone[2] = new LiveCardState { code = Stratos, position = YgoConstants.POS_FACEUP_ATTACK, type = YgoConstants.TYPE_EFFECT };
            foreach (var phase in new[] { YgoConstants.PHASE_MAIN1, YgoConstants.PHASE_MAIN2, YgoConstants.PHASE_END })
            {
                typeof(DuelSession).GetProperty(nameof(DuelSession.CurrentPhase)).SetValue(duel, phase);
                Require(!DuelTactics.CanActivate(duel, chain, impermanence) && !DuelTactics.CanActivate(duel, chain, veiler),
                    $"Idle phase window {phase} spent field negation on a monster with no pending effect.");
            }
            duel.CurrentChain.Add(new ChainLinkInfo { code = Stratos, triggerLoc = YgoConstants.LOCATION_MZONE, triggerSeq = 2 });
            Require(DuelTactics.CanActivate(duel, chain, impermanence) && DuelTactics.CanActivate(duel, chain, veiler),
                "Field negation should answer the current Stratos effect, even in the End Phase.");
            duel.CurrentChain.Add(new ChainLinkInfo { code = Imperm, triggerPlayer = 1 });
            var targets = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, playerId = 1 };
            targets.candidates.Add(new SelectableCardItem { code = 83011277, location = YgoConstants.LOCATION_MZONE });
            targets.candidates.Add(new SelectableCardItem { code = Stratos, location = YgoConstants.LOCATION_MZONE, sequence = 2 });
            Require(DuelTactics.ChooseHandtrapTarget(duel, targets) == 1,
                "Field negation chose an unrelated face-up monster instead of the pending effect source.");
        }

        private static void VerifyNativeFieldNegation(uint negate)
        {
            const uint avian = 21844576;
            using var duel = YgoProjectBuilder.StartSeedMatching("Native field negation", Deck(Stratos, avian),
                Empty, Deck(negate), Empty, true, current => current.Players[0].hand.Any(card => card.code == Stratos));
            var ai = Smart();
            SubmitIdle(duel, 0, Stratos);
            var trigger = Drive(duel, prompt =>
                prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == Stratos ||
                IncludesChainEffect(prompt, Stratos));
            int activation = trigger.messageType == YgoConstants.MSG_SELECT_EFFECTYN ? 1 :
                trigger.candidates.FindIndex(card => card.code == Stratos);
            duel.SubmitResponseInt(activation);

            var responseWindow = Drive(duel, prompt => IncludesChainEffect(prompt, negate));
            var source = duel.CurrentChain.Single();
            int response = Read(ai.ComputeResponse(duel, responseWindow));
            Require(source.code == Stratos && response >= 0 && responseWindow.candidates[response].code == negate,
                $"{negate} did not answer Stratos as Chain Link 2.");
            duel.SubmitResponseInt(response);
            var target = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.playerId == 1);
            int index = target.candidates.FindIndex(card => card.code == Stratos && card.sequence == source.triggerSeq);
            Require(index >= 0 && ai.ComputeResponse(duel, target).SequenceEqual(DuelSession.PackCardSelection(index)),
                $"{negate} selected a different card from the live chain source.");
            duel.SubmitResponse(ai.ComputeResponse(duel, target));
            Drive(duel, IsIdle);
            Require(duel.SolvedChainLinks.Any(link => link.code == negate && link.chainNumber == 2) &&
                source.negated && duel.RetryCount == 0, $"{negate} did not resolve ahead of Stratos.");
            Debug.Log($"[VERIFY AI FIELD NEGATION] {negate} resolved as CL2 and negated Stratos. Retry=0");
        }

        private static void VerifyTriggerOrder()
        {
            using var duel = new DuelSession();
            var order = new DuelPrompt { messageType = YgoConstants.MSG_SORT_CHAIN, isChainSort = true };
            order.candidates.Add(Trigger(Sulliek, 1));
            order.candidates.Add(Trigger(Merrli, 0));
            order.candidates.Add(Trigger(Merrli, 1));
            var ai = new TacticalAI(null);
            Require(ai.ComputeResponse(duel, order).SequenceEqual(new byte[] { 1, 2, 0 }),
                "Chain permutation must protect the Graveyard Fusion before the search and mill.");
            var nativeChoice = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, speCount = 0x7f };
            nativeChoice.candidates.AddRange(order.candidates);
            Require(Read(ai.ComputeResponse(duel, nativeChoice)) == 2, "SEGOC should lead with the Fusion effect.");

            SelectableCardItem Trigger(uint code, int effect) => new()
            {
                code = code, location = YgoConstants.LOCATION_GRAVE,
                description = ((ulong)code << 20) | (uint)effect, isTriggeredEffect = true,
            };
        }

        private static void VerifyAshCommitment()
        {
            uint[] lead = Deck(Upstart, OriginalSpoils, SnakeEyeAsh, GeminiElf);
            using var duel = YgoProjectBuilder.StartSeedMatching("Ash conservation", lead, Empty, Deck(Ash), Empty, true,
                current => new[] { Upstart, OriginalSpoils, GeminiElf }.All(code => current.Players[0].hand.Any(card => card.code == code)));
            var ai = Smart();
            SubmitIdle(duel, 5, Upstart);
            var upstartWindow = Drive(duel, prompt => IncludesChainEffect(prompt, Ash));
            Require(Read(ai.ComputeResponse(duel, upstartWindow)) == -1, "Ash should pass on Upstart Goblin.");
            duel.SubmitResponseInt(-1);
            Drive(duel, IsIdle);
            SubmitIdle(duel, 0, GeminiElf);
            Drive(duel, IsIdle);
            SubmitIdle(duel, 5, OriginalSpoils);
            var snakeEyeWindow = Drive(duel, prompt => IncludesChainEffect(prompt, Ash));
            int response = Read(ai.ComputeResponse(duel, snakeEyeWindow));
            Require(response >= 0 && snakeEyeWindow.candidates[response].code == Ash,
                "Ash should be saved to answer Original Sinful Spoils.");
            duel.SubmitResponseInt(response);
            Drive(duel, IsIdle);
            Require(duel.SolvedChainLinks.Any(link => link.code == Ash) &&
                duel.Players[0].mzone.All(card => card?.code != SnakeEyeAsh), "The native engine did not resolve the Ash response.");
        }

        private static void VerifyNativeTriggerProtection()
        {
            const uint painfulChoice = 74191942, aquaSpirit = 40916023, kitkallos = 92731385;
            uint[] offered = { Merrli, Sulliek, aquaSpirit, Passcode, GeminiElf };
            using var duel = YgoProjectBuilder.StartSeedMatching("Native chain blocking",
                Deck(offered.Append(painfulChoice).ToArray()), new[] { kitkallos }, Deck(Ash), Empty, true,
                current => current.Players[0].hand.Any(card => card.code == painfulChoice));
            SubmitIdle(duel, 5, painfulChoice);
            var selectFive = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.min == 5);
            int[] indices = offered.Select(code => selectFive.candidates.FindIndex(card => card.code == code)).ToArray();
            Require(indices.All(index => index >= 0), "The five chain-blocking fixture cards must be available in the Deck.");
            duel.SubmitResponse(DuelSession.PackCardSelection(indices));
            var opponentPick = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.playerId == 1);
            int elf = opponentPick.candidates.FindIndex(card => card.code == GeminiElf);
            Require(elf >= 0, "The opponent must be able to return Gemini Elf to hand.");
            duel.SubmitResponse(DuelSession.PackCardSelection(elf));

            var ai = Smart();
            var simultaneous = Drive(duel, prompt => prompt.IsTriggerChain && prompt.candidates.Count == 2);
            int fusion = Read(ai.ComputeResponse(duel, simultaneous));
            Require(fusion >= 0 && simultaneous.candidates[fusion].code == Merrli,
                "The actual simultaneous window should put the Graveyard Fusion on Chain Link 1.");
            duel.SubmitResponseInt(fusion);
            var followUp = Drive(duel, prompt => prompt.IsTriggerChain);
            int search = Read(ai.ComputeResponse(duel, followUp));
            Require(search >= 0 && followUp.candidates[search].code == Sulliek,
                "The legal Sulliek search should occupy Chain Link 2.");
            duel.SubmitResponseInt(search);
            var ashWindow = Drive(duel, prompt => IncludesChainEffect(prompt, Ash));
            Require(duel.CurrentChain.Count == 2 && duel.CurrentChain[0].code == Merrli && duel.CurrentChain[1].code == Sulliek,
                "The engine must preserve the AI's chosen order before offering Ash to the other player.");
            var protectedFusion = duel.CurrentChain[0];
            var coverSearch = duel.CurrentChain[1];
            duel.SubmitResponseInt(ashWindow.candidates.FindIndex(card => card.code == Ash));
            Drive(duel, IsIdle);
            Require(protectedFusion.Resolved && !protectedFusion.negated && coverSearch.Resolved && coverSearch.negated &&
                duel.Players[0].mzone.Any(card => card?.code == kitkallos) && duel.RetryCount == 0,
                "The Graveyard Fusion should resolve after Ash negates the search on top of it.");
        }

        private static void VerifyBaitAgainstBaronne()
        {
            const uint instant = 1845204, ready = 63854005, theseus = 96334243, flameSwordsman = 45231177;
            using var duel = YgoProjectBuilder.StartSeedMatching("Going second bait", Deck(instant, ready),
                new[] { theseus, flameSwordsman, Baronne }, Deck(Reinforcement, Stratos), Empty, true,
                current => current.Players[0].hand.Any(card => card.code == instant) &&
                           current.Players[0].hand.Any(card => card.code == ready) &&
                           current.Players[1].hand.Any(card => card.code == Stratos) &&
                           current.Players[1].hand.Count(card => card.code == Reinforcement) >= 2);
            SubmitIdle(duel, 5, ready);
            Drive(duel, IsIdle, flameSwordsman);
            SubmitIdle(duel, 5, instant);
            Drive(duel, IsIdle, theseus);
            SubmitIdle(duel, 1, Baronne);
            Drive(duel, IsIdle);
            Require(duel.SummonedMonstersThisTurn(0) == 3, "Three successful Extra Deck summons were not tracked.");
            duel.SubmitResponseInt(7);
            var opening = Drive(duel, prompt => IsIdle(prompt) && prompt.playerId == 1);
            Require(duel.TurnNumber == 2 && duel.SummonedMonstersThisTurn(0) == 0,
                "The summon count should reset on a new turn.");
            var ai = Smart();
            int bait = Read(ai.ComputeResponse(duel, opening));
            Require((bait & 0xffff) == 5 && opening.activatable[bait >> 16].code == Reinforcement,
                "The AI should lead with Reinforcement while holding a legal Stratos follow-up.");
            duel.SubmitResponseInt(bait);
            var availableNegate = Drive(duel, prompt => IncludesChainEffect(prompt, Baronne));
            duel.SubmitResponseInt(availableNegate.candidates.FindIndex(card => card.code == Baronne));
            var followUp = Drive(duel, IsIdle);
            var boss = duel.Players[0].mzone.Single(card => card?.code == Baronne);
            Require(duel.HasSpentBaronneNegate(boss) && followUp.summonable.Any(card => card.code == Stratos) &&
                DuelTactics.ChooseGoingSecondBait(duel, followUp, (_, _) => true) == null,
                "Once Baronne spends its negate the AI should stop looking for bait and retain the starter.");
        }

        private static void VerifySummonBudget()
        {
            uint[] starters = Deck(Foolish, Passcode, Imperm, GeminiElf, Dandylion);
            using var duel = YgoProjectBuilder.StartSeedMatching("Nibiru threshold", starters, new[] { LinkSpider },
                Deck(Nibiru), Empty, true, current =>
                    new[] { Foolish, Passcode, Imperm, GeminiElf }.All(code => current.Players[0].hand.Any(card => card.code == code)));
            SubmitIdle(duel, 4, Imperm);
            Drive(duel, IsIdle);
            SubmitIdle(duel, 0, GeminiElf);
            Drive(duel, IsIdle);
            Require(duel.SummonedMonstersThisTurn(0) == 1, "A set must not increase the successful summon count.");
            SubmitIdle(duel, 5, Foolish);
            Drive(duel, IsIdle, Dandylion);
            Require(duel.SummonedMonstersThisTurn(0) == 3, "A two-token group should add two successful summons.");
            SubmitIdle(duel, 5, Passcode);
            var idle = Drive(duel, IsIdle);
            Require(duel.SummonedMonstersThisTurn(0) == 4 && idle.spSummonable.Any(card => card.code == LinkSpider),
                "Expected four successful summons and a legal fifth summon.");
            var risk = DuelTactics.GetExtensionRisk(duel, idle);
            Require(risk == DuelTactics.ExtensionRisk.Nibiru && !DuelTactics.CanExtend(risk, duel.Players[0], 1,
                idle.spSummonable.First(card => card.code == LinkSpider)), "The cheap fifth summon should be vetoed.");
            Require((Read(Smart().ComputeResponse(duel, idle)) & 0xffff) != 1,
                "The actual Smart pilot bypassed its extension check.");
            Require(DuelTactics.CanExtend(risk, duel.Players[0], 1,
                new SelectableCardItem { code = Baronne, location = YgoConstants.LOCATION_EXTRA }),
                "The fifth summon should be available if it establishes a live monster negate.");

            // Force that otherwise avoidable summon and check the real Nibiru script becomes legal.
            SubmitIdle(duel, 1, LinkSpider);
            var nibiruWindow = Drive(duel, prompt => IncludesChainEffect(prompt, Nibiru));
            Require(duel.SummonedMonstersThisTurn(0) == 5 && nibiruWindow.playerId == 1,
                "The public count and the engine's Nibiru window disagree.");
        }

        private static void VerifyRevealedSweeper()
        {
            const uint draggedDown = 16435215, raigeki = 12580477, ready = 63854005, swordsman = 45231177;
            using var duel = YgoProjectBuilder.StartSeedMatching("Revealed field wipe",
                Deck(draggedDown, ready, GeminiElf, Passcode), new[] { swordsman }, Deck(raigeki, GeminiElf), Empty, true,
                current => new[] { draggedDown, ready, GeminiElf, Passcode }.All(code => current.Players[0].hand.Any(card => card.code == code)) &&
                           current.Players[1].hand.Any(card => card.code == raigeki) &&
                           current.Players[1].hand.Any(card => card.code == GeminiElf));
            SubmitIdle(duel, 5, ready);
            Drive(duel, IsIdle, swordsman);
            SubmitIdle(duel, 0, GeminiElf);
            var beforeReveal = Drive(duel, IsIdle);
            Require(DuelTactics.GetExtensionRisk(duel, beforeReveal) == DuelTactics.ExtensionRisk.None,
                "An unrevealed Raigeki should not be treated as known.");

            SubmitIdle(duel, 5, draggedDown);
            var selection = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD);
            int keepRaigeki = selection.candidates.FindIndex(card => card.controller == 1 && card.code == GeminiElf);
            Require(keepRaigeki >= 0, "Dragged Down should be able to discard the monster while revealing Raigeki.");
            duel.SubmitResponse(TacticalAI.EncodePlannedSelection(selection, keepRaigeki));
            var discardOwn = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD);
            int spare = discardOwn.candidates.FindIndex(card => card.code != Passcode);
            duel.SubmitResponse(TacticalAI.EncodePlannedSelection(discardOwn, Mathf.Max(0, spare)));
            var afterReveal = Drive(duel, IsIdle);
            var risk = DuelTactics.GetExtensionRisk(duel, afterReveal);
            var extender = afterReveal.activatable.FirstOrDefault(card => card.code == Passcode);
            Require(duel.IsKnownInOpposingHand(0, raigeki) && risk == DuelTactics.ExtensionRisk.Sweeper &&
                extender != null && !DuelTactics.CanExtend(risk, duel.Players[0], 5, extender),
                "A revealed Raigeki should preserve the available token extender.");
        }

        private static TacticalAI Smart() => new(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded());

        private static uint[] Deck(params uint[] cycle) => Enumerable.Range(0, 40).Select(i => cycle[i % cycle.Length]).ToArray();

        private static bool IsIdle(DuelPrompt prompt) => prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD;

        private static bool IncludesChainEffect(DuelPrompt prompt, uint code) =>
            prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && prompt.candidates.Any(card => card.code == code);

        private static int Read(byte[] response) => BitConverter.ToInt32(response, 0);

        private static void SubmitIdle(DuelSession duel, int command, uint code)
        {
            var prompt = duel.ActivePrompt;
            Require(prompt != null && IsIdle(prompt), "Expected an idle command prompt.");
            var choices = command switch
            {
                0 => prompt.summonable,
                1 => prompt.spSummonable,
                4 => prompt.sSetable,
                5 => prompt.activatable,
                _ => throw new ArgumentOutOfRangeException(nameof(command)),
            };
            int index = choices.FindIndex(item => item.code == code);
            Require(index >= 0, $"Missing legal idle command {command} for {code}.");
            duel.SubmitResponseInt((index << 16) | command);
        }

        private static DuelPrompt Drive(DuelSession duel, Func<DuelPrompt, bool> stop, uint selected = 0)
        {
            var basic = new TacticalAI();
            for (int step = 0; step < 28 && !duel.IsFinished && duel.RetryCount == 0; step++)
            {
                var prompt = duel.ActivePrompt;
                if (prompt == null) { duel.StepUntilPrompt(); continue; }
                if (stop(prompt)) return prompt;
                if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
                    duel.SubmitResponseInt(prompt.forced ? 0 : -1);
                else if (prompt.messageType is YgoConstants.MSG_SELECT_EFFECTYN or YgoConstants.MSG_SELECT_YESNO)
                    duel.SubmitResponseInt(0);
                else if (prompt.messageType == YgoConstants.MSG_SELECT_CARD && selected != 0 &&
                    prompt.candidates.FindIndex(card => card.code == selected) is int index && index >= 0)
                    duel.SubmitResponse(TacticalAI.EncodePlannedSelection(prompt, index));
                else if (IsIdle(prompt))
                    throw new InvalidOperationException($"Unexpected idle before the expected native prompt on turn {duel.TurnNumber}.");
                else duel.SubmitResponse(basic.ComputeResponse(duel, prompt));
            }

            throw new InvalidOperationException($"Expected native prompt never arrived (turn {duel.TurnNumber}, retry {duel.RetryCount}). " +
                string.Join(" | ", duel.Logs.TakeLast(10)));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
