using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
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
        private const uint BrandedFusion = 44362883;
        private static readonly uint[] Empty = Array.Empty<uint>();

        public static void RunHeadless()
        {
            int exit = 1;
            int scriptErrors = 0;
            try
            {
                string streaming = Application.streamingAssetsPath;
                Require(YgoNativeBridge.Initialize(streaming, (kind, message) =>
                {
                    if (kind != 0) return;
                    scriptErrors++;
                    Debug.LogError(message);
                }) == 0, "Could not initialize the duel engine.");
                CardDatabase.Load(streaming);
                TestChainChooser.Run();
                Run();
                AiBattleProbe.VerifyFixtures();
                Require(scriptErrors == 0, $"Native AI test script errors: {scriptErrors}");
                exit = 0;
            }
            catch (Exception error) { Debug.LogException(error); }
            finally
            {
                YgoNativeBridge.Shutdown();
                if (Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }

        internal static void Run()
        {
            VerifyTemporaryBattleTricks();
            VerifyReadyBoardValue();
            VerifyUnconfirmedLethalScore();
            VerifyNativeFieldDrawPlanning();
            VerifyNativeBattlePlanningSlices();
            VerifyLethalOnFinalPlanningResponse();
            VerifyMasterInterruptionTiming();
            VerifyPublicPlanningNegation();
            VerifyDetachedPlanningReplay();
            VerifyCostedBreakerPriority();
            VerifyNegationTargets();
            VerifyNativeFieldNegation(Imperm);
            VerifyNativeFieldNegation(DuelTactics.EffectVeiler);
            VerifyTriggerOrder();
            VerifyNativeTriggerProtection();
            VerifyAshCommitment();
            VerifyBaitAgainstBaronne();
            VerifySummonBudget();
            VerifyRevealedSweeper();
            VerifyGxResponses();
            VerifyGxSummonChoices();
            VerifyNativeBentenSearch();
            VerifyNativeRitualAcePreservation();
            VerifyNativeRitualWithSpareMaterials();
            VerifyNativeExtraDeckAceTrades();
            VerifyAdditionalRitualAces();
            VerifyGxSelfDamageAndDestruction();
            VerifySearchedReplacementProof();
            VerifyVolcanicEmperorSiege();
            VerifyDddContractRescueSearch();
            VerifyFieldAceEffects();
            VerifyDrytronAceCosts();
            VerifyAccesscodeCosts();
            VerifyGxBattlePriority();
            Debug.Log("[VERIFY AI TACTICS] Ash conservation, Baronne bait, summon budget, revealed sweeper and chain ordering passed. Retry=0");
        }

        private static void VerifyTemporaryBattleTricks()
        {
            const uint neos = 14124483, limiter = 23171610, rush = 70046172, score = 41767843;
            const uint honest = 37742478, chalice = 25789292, forbiddenLance = 27243130, exodia = 83257450;
            using var duel = new DuelSession();
            var prompt = new DuelPrompt { playerId = 0, messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };
            var master = new TacticalAI(null) { Difficulty = AIDifficulty.Master };
            SetState(1, YgoConstants.PHASE_MAIN1);
            duel.Players[0].mzone[0] = new LiveCardState { code = Stratos, controller = 0, attack = 1800,
                position = YgoConstants.POS_FACEUP_ATTACK };
            foreach (uint code in new[] { neos, limiter, rush, score })
            {
                var card = Effect(code);
                Require(DuelTactics.TryGetTemporaryBattleTrick(code, card.description, out _) &&
                    !DuelTactics.CanSpendBattleTrick(duel, prompt, card), $"Temporary combat effect {code} escaped the first-turn gate.");
            }
            prompt.activatable.Add(Effect(rush));
            Require(AiBattleProbe.TryFindDecisionBlunder(duel, prompt, BitConverter.GetBytes(5), out string kind, out uint flagged) &&
                kind == "temporary_combat_waste" && flagged == rush,
                "The benchmark did not identify a known wasted boost with its card code.");
            Require(BitConverter.ToInt32(master.ComputeResponse(duel, prompt), 0) == 7,
                "Master spent a combat-only card on its opening Main Phase.");
            Require(!DuelTactics.TryGetTemporaryBattleTrick(honest, (ulong)honest << 20, out _) &&
                DuelTactics.TryGetTemporaryBattleTrick(honest, ((ulong)honest << 20) | 1, out _) &&
                !DuelTactics.TryGetTemporaryBattleTrick(chalice, (ulong)chalice << 20, out _) &&
                !DuelTactics.TryGetTemporaryBattleTrick(forbiddenLance, (ulong)forbiddenLance << 20, out _) &&
                !DuelTactics.TryGetTemporaryBattleTrick(exodia, (ulong)exodia << 20, out _),
                "Mixed effects or Exodia's lasting attack gain were classified as temporary boosts.");
            SetState(2, YgoConstants.PHASE_MAIN2);
            Require(!DuelTactics.CanSpendBattleTrick(duel, prompt, Effect(rush)),
                "A temporary battle boost remained available after combat.");

            prompt.messageType = YgoConstants.MSG_SELECT_CHAIN;
            SetState(2, YgoConstants.PHASE_DAMAGE);
            typeof(DuelSession).GetProperty(nameof(DuelSession.HasActiveAttack)).SetValue(duel, true);
            duel.AttackerLoc = duel.TargetLoc = YgoConstants.LOCATION_MZONE;
            duel.TargetCon = 1;
            var target = new LiveCardState { code = GeminiElf, controller = 1, attack = 2200, position = YgoConstants.POS_FACEUP_ATTACK };
            duel.Players[1].mzone[0] = target;
            Require(DuelTactics.CanSpendBattleTrick(duel, prompt, Effect(rush)), "A +700 trick was held while it could win combat.");
            var choose = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, min = 1, max = 1,
                SelectionHint = DuelTactics.ApplyTo };
            duel.CurrentChain.Add(new ChainLinkInfo { code = rush, description = (ulong)rush << 20 });
            duel.Players[0].mzone[1] = FieldMonster(Baronne, 1);
            choose.candidates.Add(new SelectableCardItem { code = Baronne, location = YgoConstants.LOCATION_MZONE, sequence = 1 });
            choose.candidates.Add(new SelectableCardItem { code = Stratos, location = YgoConstants.LOCATION_MZONE });
            Require(master.ComputeResponse(duel, choose).SequenceEqual(DuelSession.PackCardSelection(1)),
                "Master applied a temporary boost to an uninvolved higher-ATK monster.");
            duel.CurrentChain.Clear();
            Require(!DuelTactics.CanSpendBattleTrick(duel, prompt, Effect(limiter)), "Limiter was valued on a Warrior.");
            duel.Players[0].mzone[0].race = YgoConstants.RACE_MACHINE;
            Require(!DuelTactics.CanSpendBattleTrick(duel, prompt, Effect(limiter)),
                "Master traded its machine for a nonlethal boost with End Phase destruction.");
            duel.Players[1].lp = 1200;
            Require(DuelTactics.CanSpendBattleTrick(duel, prompt, Effect(limiter)),
                "Master declined a lethal machine boost with no End Phase to pay.");
            target.attack = 1500;
            Require(!DuelTactics.CanSpendBattleTrick(duel, prompt, Effect(rush)), "A resolved win spent a redundant combat boost.");
            target.code = 78371393;
            Require(!DuelTactics.CanSpendBattleTrick(duel, prompt, Effect(neos)), "A burn reflector caused a friendly attack boost.");
            duel.TargetLoc = 0;
            duel.Players[1].lp = 4200;
            Require(DuelTactics.CanSpendBattleTrick(duel, prompt, Effect(neos)), "The boost missed a 4300-point direct lethal.");
            duel.Players[1].lp = 1500;
            Require(!DuelTactics.CanSpendBattleTrick(duel, prompt, Effect(neos)), "The boost was wasted on existing direct lethal.");

            Debug.Log("[VERIFY MASTER COMBAT TRICKS] Opening, Main 2, effect distinction, combat swing, reflection and direct lethal passed.");

            static SelectableCardItem Effect(uint code) => new() { code = code, controller = 0,
                location = YgoConstants.LOCATION_HAND, description = (ulong)code << 20 };
            void SetState(int turn, int phase)
            {
                typeof(DuelSession).GetProperty(nameof(DuelSession.TurnNumber)).SetValue(duel, turn);
                typeof(DuelSession).GetProperty(nameof(DuelSession.CurrentPhase)).SetValue(duel, phase);
            }
        }

        private static void VerifyReadyBoardValue()
        {
            const uint dpe = 60461804, herald = 48546368, fairy = 39618799;
            using var duel = new DuelSession();
            var own = duel.Players[0];
            const uint conflict = 7403341, transcode = 46947713, firewall = 5043010;
            var trap = CardDatabase.Get(conflict);
            own.szone[0] = new LiveCardState { code = conflict, position = YgoConstants.POS_FACEDOWN_DEFENSE };
            own.mzone[0] = FieldMonster(firewall);
            Require(DuelAiPolicy.BackrowFieldCondition(own, trap) == false,
                "A Firewall Dragon alone must not make Cynet Conflict usable.");
            own.mzone[0] = FieldMonster(transcode);
            Require(DuelAiPolicy.BackrowFieldCondition(own, trap) == true &&
                DuelAiPolicy.ReadyInteractionCount(duel, 0, inspectPrivateCards: true) == 1,
                "A face-up Code Talker must make the set Counter Trap usable.");
            own.szone[0] = null;
            own.mzone[0] = new LiveCardState { code = dpe, controller = 0, attack = 2500, baseAttack = 2500,
                position = YgoConstants.POS_FACEUP_ATTACK };
            own.mzone[1] = new LiveCardState { code = herald, controller = 0, attack = 2000, baseAttack = 2000,
                position = YgoConstants.POS_FACEUP_DEFENSE };
            Require(DuelAiPolicy.ReadyInteractionCount(duel, 0, inspectPrivateCards: true) == 1,
                "Endboard values did not distinguish available removal from a negate with no payment.");
            own.hand.Add(new LiveCardState { code = fairy, race = YgoConstants.RACE_FAIRY });
            Require(DuelAiPolicy.ReadyInteractionCount(duel, 0, inspectPrivateCards: true) == 2,
                "An affordable Fairy negate did not count on its endboard.");
            own.mzone[0].effectsDisabled = true;
            Require(DuelAiPolicy.ReadyInteractionCount(duel, 0, inspectPrivateCards: true) == 1,
                "A disabled effect was counted as live interaction.");
            own.mzone[0].attack = 5000;
            Require(DuelAiPolicy.StableAttack(own.mzone[0], CardDatabase.Get(dpe), canBattle: false) == 2500,
                "The endboard kept an attack increase that is irrelevant after combat.");
            Require(ComboSearch.BoardValue(duel, 0, canBattle: false, master: true, turnEndVerified: true) >
                ComboSearch.BoardValue(duel, 0, canBattle: false, master: true),
                "An increase that survived the confirmed turn transition was discarded.");
            var lower = new SelectableCardItem { code = 12014404, location = YgoConstants.LOCATION_EXTRA };
            Require(DuelAiPolicy.NeedsAceMaterialProjection(own, lower), "Master risks both established aces on an unproven Xyz.");
            own.mzone[2] = FieldMonster(Stratos);
            Require(DuelAiPolicy.NeedsAceMaterialProjection(own, lower), "An unrelated disposable body must not bypass the native material check.");
            Require(ComboSearch.IsPaidAceUpgrade(4, 4.02, true, false) &&
                ComboSearch.IsPaidAceUpgrade(4, 2, true, true) &&
                !ComboSearch.IsPaidAceUpgrade(4, 2, true, false) &&
                !ComboSearch.IsPaidAceUpgrade(4, 5, false, false),
                "Projected ace trades did not distinguish verified upgrades and lethal from a downgrade.");

            own.hand.Clear();
            own.deckCount = 30;
            own.hand.Add(new LiveCardState { code = Ash });
            own.hand.Add(new LiveCardState { code = Ash });
            double duplicate = ComboSearch.BoardValue(duel, 0, canBattle: false, master: true);
            own.hand[1].code = DuelTactics.MaxxC;
            double distinct = ComboSearch.BoardValue(duel, 0, canBattle: false, master: true);
            Require(DuelAiPolicy.ReadyHandInterruptions(own) == 2 && distinct > duplicate,
                "Master valued two copies of the same once-per-turn handtrap as two distinct interruptions.");
            own.deckCount = 2;
            own.hand.Add(new LiveCardState { code = DuelTactics.CalledBy });
            own.hand.Add(new LiveCardState { code = DuelTactics.InfiniteImpermanence });
            Require(DuelAiPolicy.ReadyHandInterruptions(own) == 1,
                "Master valued an unsafe draw handtrap or interruption that still needed to be Set.");
        }

        private static void VerifyUnconfirmedLethalScore()
        {
            using var duel = new DuelSession();
            typeof(DuelSession).GetProperty(nameof(DuelSession.TurnNumber)).SetValue(duel, 2);
            duel.Players[0].mzone[0] = FieldMonster(Stratos);
            duel.Players[1].lp = 1801;
            double aboveAttack = ComboSearch.BoardValue(duel, 0, canBattle: true, master: true);
            duel.Players[1].lp = 1800;
            double atAttack = ComboSearch.BoardValue(duel, 0, canBattle: true, master: true);
            Require(atAttack > aboveAttack && atAttack - aboveAttack < .1,
                "An unverified attack total jumped ahead of other lines before the native battle simulation.");
        }

        private static void VerifyNativeFieldDrawPlanning()
        {
            const uint fallenParadise = 13301895, hamon = 32491822;
            using var duel = new DuelSession(recordPlanningReplay: true);
            var main = Deck(GeminiElf);
            Require(duel.StartNewDuel(main, Empty, main.Append(hamon).ToArray(), Empty,
                p0GoesFirst: false, seed: new[] { 37UL, 29UL, 71UL, 97UL },
                opponentOpening: new DuelOpening(8000, 0, fallenParadise, destinyUses: 0, deckMonster: hamon)),
                "Could not start the native field draw fixture.");
            var prompt = Drive(duel, IsIdle);
            var own = duel.Players[prompt.playerId];
            int drawIndex = prompt.activatable.FindIndex(card => card.code == fallenParadise);
            Require(drawIndex >= 0 && ComboSearch.IsPublicFieldDraw(own, prompt.activatable[drawIndex]),
                "Fallen Paradise did not offer its draw with Hamon already on the field.");
            var master = new TacticalAI(null, searchCombos: true) { Difficulty = AIDifficulty.Master };
            Require(new ComboSearch().Choose(duel, prompt, master) == ((drawIndex << 16) | 5),
                "Master discarded the proven draw when the newly drawn cards became unknown.");
            int previous = own.hand.Count;
            DuelSession reopened = null;
            int replayPauses = 0;
            foreach (var state in duel.ReplayForPlanning(() => false, () => true))
            {
                if (state != null) reopened = state;
                else replayPauses++;
            }
            using (var sliced = reopened)
            {
                Require(replayPauses >= main.Length * 2 && sliced?.ActivePrompt?.messageType == prompt.messageType,
                    "The replay did not yield while loading both decks and restore the same native prompt.");
                int resolutionPauses = 0;
                foreach (bool paused in sliced.SubmitPlanningResponse(BitConverter.GetBytes((drawIndex << 16) | 5),
                    () => true, preserveDeckSelection: false))
                    if (paused) resolutionPauses++;
                Drive(sliced, IsIdle);
                Require(resolutionPauses > 0 && sliced.Players[prompt.playerId].hand.Count == previous + 2 &&
                    own.hand.Count == previous && sliced.RetryCount == 0,
                    "Resuming a paused native draw changed the response or the live session.");
            }
            duel.SubmitResponseInt((drawIndex << 16) | 5);
            Drive(duel, IsIdle);
            Require(own.hand.Count == previous + 2 && own.mzone.Any(card => card?.code == hamon) && duel.RetryCount == 0,
                "The native draw did not preserve Hamon or gain both cards.");
            Require(!ComboSearch.HasSafeDrawGain(2, 0, 10, 8000, 8000) &&
                !ComboSearch.HasSafeDrawGain(2, 2, 0, 8000, 8000) &&
                !ComboSearch.HasSafeDrawGain(2, 2, 10, 1000, 500),
                "A draw with no net gain, an empty deck, or a life loss received the safe draw benefit.");
            Debug.Log("[VERIFY MASTER FIELD DRAW] Native Fallen Paradise draw, no field loss, no hidden card selection.");
        }

        private static void VerifyNativeBattlePlanningSlices()
        {
            using var duel = new DuelSession(recordPlanningReplay: true);
            Require(duel.StartNewDuel(Deck(GeminiElf), Empty, Deck(GeminiElf), Empty,
                seed: new[] { 11UL, 29UL, 71UL, 97UL },
                playerOpening: new DuelOpening(3000, 5, 0, destinyUses: 0),
                opponentOpening: new DuelOpening(8000, 5, GeminiElf, destinyUses: 0)),
                "Could not start the native battle fixture.");
            Drive(duel, IsIdle);
            duel.SubmitResponseInt(7);
            Drive(duel, IsIdle);
            SubmitIdle(duel, 0, GeminiElf);
            Drive(duel, IsIdle);
            duel.SubmitResponseInt(6);
            var battle = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD);
            var master = new TacticalAI(null, searchCombos: true) { Difficulty = AIDifficulty.Master };
            var lethal = DuelCombat.FindLethal(duel, battle);
            Require(battle.attackable.Count == 2 && lethal.HasValue && lethal.Value.Target == null,
                "Expected two direct attacks to be necessary for lethal.");
            var slices = DuelCombat.VerifyDirectAttackOrderInSlices(duel, battle, lethal.Value.Attacker, master, () => true).ToArray();
            Require(slices.Any(slice => !slice.HasValue) && slices.LastOrDefault() is int candidate &&
                battle.attackable[candidate].directAttackable && ReferenceEquals(duel.ActivePrompt, battle) && duel.Players[0].lp == 3000,
                "Sliced battle proof did not return a legal decision or modified the live duel.");
            for (int frame = 0; !master.AdvanceComboPlanning(duel, battle); frame++)
                Require(frame < 256, "Battle decision did not complete.");
            int selected = Read(master.ComputeResponse(duel, battle));
            Require((selected & 0xffff) == 1 && battle.attackable[selected >> 16].directAttackable && duel.RetryCount == 0,
                "The prepared battle decision did not choose a legal direct attack.");
            var detachedMaster = new TacticalAI(null, searchCombos: true) { Difficulty = AIDifficulty.Master };
            var pending = new DetachedAiPlanning(detachedMaster, duel, battle);
            TacticalAI detached;
            string error;
            while (!pending.TryTake(battle, out detached, out error)) System.Threading.Thread.Sleep(1);
            Require(detached != null && error == null && Read(detached.ComputeWithoutSpeculation(duel, battle)) == selected &&
                ReferenceEquals(duel.ActivePrompt, battle), "The detached Master chose a different attack or touched the live prompt.");
            var abandoned = new DetachedAiPlanning(detachedMaster, duel, battle);
            Require(abandoned.TryTake(new DuelPrompt(), out var stale, out _) && stale == null,
                "A detached decision was applied to a different prompt.");
            Debug.Log("[VERIFY MASTER BATTLE PLANNING] Native battle proof can yield and preserve the live duel.");
        }

        private static void VerifyLethalOnFinalPlanningResponse()
        {
            const uint damageSpell = 19523799;
            using var duel = new DuelSession(recordPlanningReplay: true);
            Require(duel.StartNewDuel(Deck(damageSpell), Empty, Deck(GeminiElf), Empty,
                seed: new[] { 17UL, 31UL, 41UL, 59UL },
                opponentOpening: new DuelOpening(800, 0, 0, destinyUses: 0)),
                "Could not start the native lethal fixture.");
            var prompt = Drive(duel, IsIdle);
            var master = new TacticalAI(null, searchCombos: true) { Difficulty = AIDifficulty.Master };
            int spell = prompt.activatable.FindIndex(card => card.code == damageSpell);
            Require(spell >= 0, "Expected an immediately available damage spell.");
            using var trial = duel.ReplayForPlanning(() => false);
            Require(trial != null, "The native lethal fixture did not replay.");
            int stepsToWin = ResolveBurn(trial, (spell << 16) | 5);
            int? decision = new ComboSearch(responseLimit: stepsToWin).Choose(duel, prompt, master);
            Require(decision.HasValue && (decision.Value & 0xffff) == 5 &&
                prompt.activatable[decision.Value >> 16].code == damageSpell && !duel.IsFinished,
                "The final native planning response did not retain its confirmed lethal.");
            Require(ResolveBurn(duel, decision.Value) == stepsToWin, "The live win differed from the projected response count.");
            Debug.Log("[VERIFY MASTER FINAL RESPONSE] A win on the last search response is retained.");

            int ResolveBurn(DuelSession session, int activation)
            {
                var pilot = master.ForkForComboSearch();
                session.SubmitResponseInt(activation);
                int steps = 1;
                for (; !session.IsFinished && steps < 8; steps++)
                {
                    var next = session.ActivePrompt;
                    Require(next != null && !IsIdle(next), "The damage spell did not resolve in a single main action.");
                    session.SubmitResponse(pilot.ComputeResponse(session, next));
                }
                Require(session.IsFinished && session.Winner == prompt.playerId && session.RetryCount == 0,
                    "The chosen damage spell did not end the native duel.");
                return steps;
            }
        }

        private static void VerifyMasterInterruptionTiming()
        {
            const uint duster = 18144506, dpe = 60461804, mirrorjade = DuelAiPolicy.Mirrorjade, mask = 21143940,
                favorite = 75047173, targetedRemoval = 79759861;
            using var duel = new DuelSession();
            var own = duel.Players[0];
            var rival = duel.Players[1];
            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, playerId = 0, canBp = true };
            Set(nameof(DuelSession.TurnNumber), 2);
            Set(nameof(DuelSession.TurnPlayer), (byte)0);
            Set(nameof(DuelSession.CurrentPhase), YgoConstants.PHASE_MAIN1);
            rival.szone[0] = new LiveCardState { controller = 1, sequence = 0, position = YgoConstants.POS_FACEDOWN_DEFENSE };
            rival.szone[1] = new LiveCardState { controller = 1, sequence = 1, position = YgoConstants.POS_FACEDOWN_DEFENSE };
            prompt.activatable.Add(new SelectableCardItem { code = Upstart, location = YgoConstants.LOCATION_HAND });
            prompt.activatable.Add(new SelectableCardItem { code = duster, location = YgoConstants.LOCATION_HAND });
            Require(DuelTactics.ChooseBoardBreaker(duel, prompt, (_, _) => true) == ((1 << 16) | 5),
                "Master did not clear two opposing back-row cards before developing.");
            Require(DuelTactics.ChooseBoardBreaker(duel, prompt, (_, effect) => effect.code != duster) == null,
                "Master put an unavailable breaker before development.");
            prompt.summonable.Add(new SelectableCardItem { code = Ash, location = YgoConstants.LOCATION_HAND });
            prompt.summonable.Add(new SelectableCardItem { code = Stratos, location = YgoConstants.LOCATION_HAND });
            Require(DuelTactics.ChooseRecoverySummon(duel, prompt, (_, _) => true) == (1 << 16),
                "Master used a handtrap for recovery instead of an independent starter.");
            rival.mzone[0] = new LiveCardState { code = Baronne, controller = 1, sequence = 0,
                position = YgoConstants.POS_FACEUP_ATTACK, attack = 3000 };
            Require(DuelTactics.ChooseRecoverySummon(duel, prompt, (_, _) => true) == null,
                "Master exposed a recovery starter to an already established attacker.");

            Set(nameof(DuelSession.TurnPlayer), (byte)1);
            own.mzone[0] = FieldMonster(Stratos);
            rival.szone[0] = rival.szone[1] = null;
            rival.mzone[0] = new LiveCardState { code = GeminiElf, controller = 1, sequence = 0,
                position = YgoConstants.POS_FACEUP_ATTACK, attack = 1900 };
            prompt.messageType = YgoConstants.MSG_SELECT_CHAIN;
            foreach (uint code in new[] { dpe, mirrorjade, mask, favorite })
                Require(!DuelTactics.CanSpendMasterInterruption(duel, prompt, FastEffect(code)),
                    $"Master spent {code} in an empty response window.");

            duel.CurrentChain.Add(new ChainLinkInfo { code = BrandedFusion, triggerPlayer = 1 });
            foreach (uint code in new[] { dpe, mirrorjade, favorite })
                Require(DuelTactics.CanSpendMasterInterruption(duel, prompt, FastEffect(code)),
                    $"Master kept {code} during an opposing development effect.");
            duel.CurrentChain.Clear();
            rival.mzone[0].attack = 2000;
            foreach (uint code in new[] { dpe, mirrorjade, favorite })
                Require(DuelTactics.CanSpendMasterInterruption(duel, prompt, FastEffect(code)),
                    $"Master waited to use {code} against an exposed threat.");
            rival.mzone[0].attack = 1900;
            prompt.hintTimingOther = YgoConstants.TIMING_SPSUMMON;
            foreach (uint code in new[] { dpe, mirrorjade, favorite })
                Require(DuelTactics.CanSpendMasterInterruption(duel, prompt, FastEffect(code)),
                    $"Master did not consider {code} in response to a special summon.");
            duel.CurrentChain.Add(new ChainLinkInfo { code = mirrorjade, triggerPlayer = 0,
                PublicTargets = new[] { ((byte)1, YgoConstants.LOCATION_MZONE, 0u) } });
            Require(!DuelTactics.CanSpendMasterInterruption(duel, prompt, FastEffect(dpe)),
                "Master used another removal when its chain already addressed the same monster.");

            duel.CurrentChain[0] = new ChainLinkInfo { code = targetedRemoval, triggerPlayer = 1,
                PublicTargets = new[] { ((byte)0, YgoConstants.LOCATION_MZONE, 0u) } };
            prompt.hintTimingOther = 0;
            Require(DuelTactics.CanSpendMasterInterruption(duel, prompt, FastEffect(mask)),
                "Master did not consider saving a targeted HERO with Mask Change.");

            prompt.playerId = 1;
            duel.CurrentChain[0] = new ChainLinkInfo { code = BrandedFusion, triggerPlayer = 0 };
            rival.mzone[0].code = Baronne;
            prompt.candidates.Add(new SelectableCardItem { code = Ash, controller = 1, location = YgoConstants.LOCATION_HAND });
            prompt.candidates.Add(new SelectableCardItem { code = Baronne, controller = 1,
                location = YgoConstants.LOCATION_MZONE, description = ((ulong)Baronne << 20) | 1 });
            Require(DuelTactics.FindPublicPlanningNegate(duel, prompt, 0) == 1,
                "Master did not model the visible Baronne before the first development line.");
            rival.mzone[0].position = YgoConstants.POS_FACEDOWN_DEFENSE;
            Require(DuelTactics.FindPublicPlanningNegate(duel, prompt, 0) == -1,
                "Master projected a reaction from the rival's hidden cards.");
            Debug.Log("[VERIFY MASTER INTERRUPTION] Board breakers, empty-window conservation, summon interruption and targeted rescue passed.");

            void Set(string name, object value) => typeof(DuelSession).GetProperty(name).SetValue(duel, value);
            SelectableCardItem FastEffect(uint code) => new() { code = code, description = (ulong)code << 20,
                location = YgoConstants.LOCATION_MZONE };
        }

        private static void VerifyPublicPlanningNegation()
        {
            const uint savage = 27548199, quasar = 35952884, dragoon = 37818794;
            using var duel = new DuelSession();
            duel.CurrentChain.Add(new ChainLinkInfo { code = BrandedFusion, triggerPlayer = 0 });
            var prompt = new DuelPrompt { playerId = 1, messageType = YgoConstants.MSG_SELECT_CHAIN };
            var opponent = duel.Players[1];
            foreach (var (code, index) in new (uint, uint)[] { (dragoon, 1), (savage, 1), (quasar, 0) })
            {
                uint sequence = (uint)prompt.candidates.Count;
                opponent.mzone[sequence] = FieldMonster(code, sequence);
                var option = Effect(code, index, YgoConstants.LOCATION_MZONE);
                option.sequence = sequence;
                option.controller = 1;
                prompt.candidates.Add(option);
            }
            Require(DuelTactics.FindPublicPlanningNegate(duel, prompt, 0) == 1,
                "Public planning overlooked Savage's native negate or assumed the contents of Dragoon's hand.");
            var used = new HashSet<(uint, uint, ulong)> { (savage, 1, ((ulong)savage << 20) | 1) };
            Require(DuelTactics.FindPublicPlanningNegate(duel, prompt, 0, used) == 2,
                "Public planning did not distinguish an already simulated negate from the next public threat.");
            opponent.mzone[2].position = YgoConstants.POS_FACEDOWN_DEFENSE;
            Require(DuelTactics.FindPublicPlanningNegate(duel, prompt, 0, used) == -1,
                "Public planning used a hidden monster or a private discard.");

            using var native = new DuelSession(recordPlanningReplay: true);
            Require(native.StartNewDuel(Deck(Upstart), Empty, Deck(GeminiElf).Append(quasar).ToArray(), Empty,
                seed: new[] { 211UL, 41UL, 29UL, 7UL }, opponentOpening: new DuelOpening(8000, 5, Baronne, deckMonster: quasar)),
                "Could not start the native public negation fixture.");
            var idle = Drive(native, IsIdle);
            using var trial = native.ReplayForPlanning(() => false);
            Require(trial != null, "Could not restore the two live negates in the planning copy.");
            trial.BeginSpeculation(0, stopAfterTurn: true);
            used.Clear();
            for (int i = 0; i < 2; i++)
            {
                SubmitIdle(trial, 5, Upstart);
                var reply = Drive(trial, choice => choice.messageType == YgoConstants.MSG_SELECT_CHAIN && choice.playerId == 1);
                int selected = DuelTactics.FindPublicPlanningNegate(trial, reply, 0, used);
                Require(selected >= 0, "A public native negate was available for the second development check.");
                var answer = reply.candidates[selected];
                Require(used.Add((answer.code, answer.sequence, answer.description)),
                    "A simulated negate was reused instead of counting the other available resource.");
                foreach (bool paused in trial.SubmitVisiblePlanningReaction(selected, () => false)) { }
                Drive(trial, IsIdle);
                Require(!trial.SpeculationUncertain,
                    "The verified public response prevented the planner from examining the rest of the line.");
            }
            Require(used.Select(answer => answer.Item1).ToHashSet().SetEquals(new[] { Baronne, quasar }) &&
                trial.Players[1].lp == 8000 && trial.RetryCount == 0 && ReferenceEquals(native.ActivePrompt, idle),
                "The two public negates did not resolve separately in the planning copy.");
            using var unplanned = native.ReplayForPlanning(() => false);
            Require(unplanned != null, "Could not restore the unexpected-response fixture.");
            unplanned.BeginSpeculation(0, stopAfterTurn: true);
            SubmitIdle(unplanned, 5, Upstart);
            var chain = Drive(unplanned, choice => choice.messageType == YgoConstants.MSG_SELECT_CHAIN && choice.playerId == 1);
            unplanned.SubmitResponseInt(DuelTactics.FindPublicPlanningNegate(unplanned, chain, 0));
            Require(unplanned.SpeculationUncertain, "An unplanned opposing activation did not stop speculation.");
            Debug.Log("[VERIFY PUBLIC PLANNING NEGATION] Two native public responses were verified without mutating the live duel.");
        }

        private static void VerifyDetachedPlanningReplay()
        {
            using var live = new DuelSession(recordPlanningReplay: true);
            Require(live.StartNewDuel(Deck(Upstart), Empty, Deck(GeminiElf), Empty,
                seed: new[] { 212UL, 41UL, 29UL, 7UL }), "Could not start the detached planning fixture.");
            Drive(live, IsIdle);
            SubmitIdle(live, 5, Upstart);
            var originalPrompt = Drive(live, IsIdle);
            ulong originalState = ComboSearch.Fingerprint(live, originalPrompt);
            var restore = live.CapturePlanningReplay();
            Require(restore != null && restore(() => true) == null, "An expired replay was allowed to start.");
            var worker = Task.Run(() => YgoNativeBridge.RunIsolatedPlanning(() =>
            {
                using var replica = restore(() => false);
                Require(replica != null && ComboSearch.Fingerprint(replica, replica.ActivePrompt) == originalState &&
                    replica.Players[1].lp == 9000, "The worker reconstructed a different prompt or board.");
                using var branch = replica.ReplayForPlanning(() => false);
                Require(branch != null && branch.NativePtr != replica.NativePtr, "The detached replay could not explore a native branch.");
                SubmitIdle(branch, 5, Upstart);
                Drive(branch, IsIdle);
                Require(branch.Players[1].lp == 10000 && replica.Players[1].lp == 9000,
                    "The detached branch mutated its starting position.");
                return replica.ActivePrompt;
            }, null));

            // The worker has a snapshot of the first activation even if the live duel is advanced and disposed.
            SubmitIdle(live, 5, Upstart);
            Drive(live, IsIdle);
            Require(live.Players[1].lp == 10000, "The live duel could not advance during detached planning.");
            live.Dispose();
            Require(worker.GetAwaiter().GetResult() is { } replicaPrompt && !ReferenceEquals(replicaPrompt, originalPrompt),
                "The worker used the live prompt instance instead of its own prompt.");
            Debug.Log("[VERIFY DETACHED PLANNING] Live and background duels advanced separately, including a nested native branch.");
        }

        private static void VerifyCostedBreakerPriority()
        {
            using var duel = new DuelSession();
            typeof(DuelSession).GetProperty(nameof(DuelSession.TurnNumber)).SetValue(duel, 2);
            typeof(DuelSession).GetProperty(nameof(DuelSession.CurrentPhase)).SetValue(duel, YgoConstants.PHASE_MAIN1);
            var prompt = new DuelPrompt { playerId = 0, canBp = true, messageType = YgoConstants.MSG_SELECT_IDLECMD };
            var hand = duel.Players[0].hand;
            var rival = duel.Players[1];
            var blocker = new LiveCardState { code = DuelAiPolicy.Mirrorjade, controller = 1,
                type = YgoConstants.TYPE_MONSTER | YgoConstants.TYPE_EFFECT, position = YgoConstants.POS_FACEUP_ATTACK };
            rival.mzone[0] = blocker;
            rival.mzone[1] = new LiveCardState { code = GeminiElf, controller = 1, position = YgoConstants.POS_FACEUP_ATTACK };
            prompt.summonable.Add(new SelectableCardItem { code = Stratos, location = YgoConstants.LOCATION_HAND, sequence = 1 });
            foreach (uint code in new[] { DuelTactics.ForbiddenDroplet, DuelAiPolicy.SuperPoly })
            {
                hand.Clear();
                hand.Add(new LiveCardState { code = code });
                hand.Add(new LiveCardState { code = Stratos });
                prompt.activatable.Clear();
                prompt.activatable.Add(new SelectableCardItem { code = code, location = YgoConstants.LOCATION_HAND });
                Require(DuelTactics.ChooseBoardBreaker(duel, prompt, (_, _) => true) == null,
                    $"{code} automatically discarded the only starter.");
                hand.Add(new LiveCardState { code = GeminiElf });
                Require(DuelTactics.ChooseBoardBreaker(duel, prompt, (_, _) => true) == 5,
                    $"{code} skipped the breaker despite an available payment and follow-up.");
                hand.RemoveAt(2);
                blocker.code = Baronne;
                Require(DuelTactics.ChooseBoardBreaker(duel, prompt, (_, _) => true) == null,
                    $"{code} spent the only starter to answer a permission boss without projection.");
                prompt.summonable.Clear();
                hand[1].code = GeminiElf;
                Require(DuelTactics.ChooseBoardBreaker(duel, prompt, (_, _) => true) == 5,
                    $"{code} did not prioritize the permission boss with a disposable cost.");
                blocker.code = DuelAiPolicy.Mirrorjade;
                prompt.summonable.Add(new SelectableCardItem { code = Stratos, location = YgoConstants.LOCATION_HAND, sequence = 1 });
            }
            var droplet = new SelectableCardItem { code = DuelTactics.ForbiddenDroplet, location = YgoConstants.LOCATION_HAND };
            Require(!DuelTactics.CanProjectCostedBreakerLethal(duel, prompt, droplet),
                "A breaker alone should not be considered a battle finish.");
            duel.Players[0].mzone[0] = FieldMonster(Stratos);
            rival.lp = 1500;
            Require(DuelTactics.CanProjectCostedBreakerLethal(duel, prompt, droplet),
                "A possible finish after enemy suppression was not left to native projection.");

            const uint starvingVenom = 41209827;
            var fusion = new SelectableCardItem { code = DuelAiPolicy.SuperPoly, location = YgoConstants.LOCATION_HAND };
            duel.Players[0].extra.Add(FieldMonster(starvingVenom));
            rival.mzone[0] = FieldMonster(10802915);
            rival.mzone[1] = FieldMonster(46986414, 1);
            rival.lp = 4300;
            Require(DuelTactics.CanProjectCostedBreakerLethal(duel, prompt, fusion),
                "Super Polymerization did not reserve a possible win from the opponent's materials and the remaining attacker.");
            rival.lp = 8000;
            Require(!DuelTactics.CanProjectCostedBreakerLethal(duel, prompt, fusion),
                "Super Polymerization queued a finish beyond the available attack ceiling.");
            hand.Clear();
            hand.Add(new LiveCardState { code = DuelAiPolicy.SuperPoly });
            hand.Add(new LiveCardState { code = Stratos });
            prompt.summonable.Clear();
            prompt.activatable.Clear();
            prompt.activatable.Add(fusion);
            prompt.canEp = true;
            var unplanned = new TacticalAI(DuelAiPolicy.LoadIncluded()) { Difficulty = AIDifficulty.Master };
            Require((Read(unplanned.ComputeResponse(duel, prompt)) & 0xffff) != 5,
                "The fallback main phase used Super Polymerization without a live follow-up or a permission boss.");
        }

        private static void VerifyGxResponses()
        {
            const uint maxxC = 23434538, calledBy = 24224830, droplet = 24299458, predaprimitive = 89176044;
            using var duel = new DuelSession();
            duel.Players[0].deckCount = 30;
            var chain = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
            var maxx = new SelectableCardItem { code = maxxC, controller = 0, location = YgoConstants.LOCATION_HAND };
            var called = new SelectableCardItem { code = calledBy, controller = 0, location = YgoConstants.LOCATION_SZONE };
            var upstart = new ChainLinkInfo { code = Upstart, triggerPlayer = 1 };
            duel.CurrentChain.Add(upstart);
            Require(!DuelTactics.CanActivate(duel, chain, maxx), "Maxx C was wasted on a draw effect.");
            duel.CurrentChain[0] = new ChainLinkInfo { code = BrandedFusion, triggerPlayer = 1 };
            Require(DuelTactics.CanActivate(duel, chain, maxx), "Maxx C missed an opponent's Branded Fusion.");
            duel.CurrentChain[0] = new ChainLinkInfo { code = BrandedFusion, triggerPlayer = 0 };
            Require(!DuelTactics.CanActivate(duel, chain, maxx), "Maxx C was chained to a friendly Branded Fusion.");

            duel.CurrentChain[0] = new ChainLinkInfo { code = Ash, triggerPlayer = 1, triggerLoc = YgoConstants.LOCATION_HAND };
            duel.Players[1].grave.Add(new LiveCardState { code = Ash });
            Require(DuelTactics.CanActivate(duel, chain, called), "Called by the Grave missed an opposing Ash in the GY.");
            duel.CurrentChain[0] = new ChainLinkInfo { code = Ash, triggerPlayer = 0, triggerLoc = YgoConstants.LOCATION_HAND };
            Require(!DuelTactics.CanActivate(duel, chain, called), "Called by the Grave responded to a friendly effect.");
            duel.CurrentChain[0] = new ChainLinkInfo { code = Ash, triggerPlayer = 1, triggerLoc = YgoConstants.LOCATION_HAND };
            duel.CurrentChain.Add(new ChainLinkInfo { code = calledBy, triggerPlayer = 0 });
            var target = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, playerId = 0,
                min = 1, max = 1, SelectionHint = DuelTactics.Banish };
            target.candidates.Add(new SelectableCardItem { code = Baronne, controller = 1, location = YgoConstants.LOCATION_GRAVE });
            target.candidates.Add(new SelectableCardItem { code = Ash, controller = 1, location = YgoConstants.LOCATION_GRAVE });
            Require(Smart().ComputeResponse(duel, target).SequenceEqual(DuelSession.PackCardSelection(1)),
                "Called by the Grave ignored the immediate chain source.");

            var cost = new SelectableCardItem { code = Stratos, controller = 0, location = YgoConstants.LOCATION_HAND };
            var boss = new SelectableCardItem { code = Baronne, controller = 0, location = YgoConstants.LOCATION_MZONE };
            target.SelectionHint = DuelTactics.SendToGrave;
            target.max = 2;
            target.candidates.Clear();
            target.candidates.AddRange(new[] { boss, cost });
            duel.CurrentChain.Clear();
            duel.CurrentChain.Add(new ChainLinkInfo { code = droplet });
            Require(Smart().ComputeResponse(duel, target).SequenceEqual(DuelSession.PackCardSelection(1)),
                "Droplet should send only the cheaper card when one cost suffices.");
            var primitive = new SelectableCardItem { code = predaprimitive, location = YgoConstants.LOCATION_GRAVE };
            Require(!DuelTactics.CanActivate(duel, chain, primitive), "Predaprimitive's optional trigger destroyed an own Fusion.");
            primitive.location = YgoConstants.LOCATION_HAND;
            Require(DuelTactics.CanActivate(duel, chain, primitive), "Predaprimitive lost its Fusion Spell search from hand.");
            Debug.Log("[VERIFY GX AI RESPONSE] Maxx C, Called by the Grave, and Droplet cost targeting passed.");
        }

        private static void VerifyGxSummonChoices()
        {
            using var duel = new DuelSession();
            var summon = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, playerId = 0, min = 1, max = 1 };
            summon.candidates.Add(new SelectableCardItem { code = 87746184, location = YgoConstants.LOCATION_EXTRA });
            summon.candidates.Add(new SelectableCardItem { code = 44146295, location = YgoConstants.LOCATION_EXTRA });
            duel.CurrentChain.Add(new ChainLinkInfo { code = BrandedFusion });
            Require(ChoosesSecond(), "Branded Fusion failed to choose a legal Mirrorjade.");
            summon.candidates[1].code = 60461804;
            duel.CurrentChain[0].code = 52947044;
            Require(ChoosesSecond(), "Fusion Destiny failed to choose a legal DPE.");
            summon.candidates[1].code = 58481572;
            duel.CurrentChain[0].code = 21143940;
            Require(ChoosesSecond(), "Mask Change failed to choose a legal Dark Law.");

            const uint garura = 11765832;
            var poly = new SelectableCardItem { code = 48130397 };
            var darkDragon = new LiveCardState { code = 74677422, position = YgoConstants.POS_FACEUP_ATTACK,
                attribute = YgoConstants.ATTRIBUTE_DARK, race = YgoConstants.RACE_DRAGON };
            var darkDragonTwo = new LiveCardState { code = 88264978, position = YgoConstants.POS_FACEUP_ATTACK,
                attribute = YgoConstants.ATTRIBUTE_DARK, race = YgoConstants.RACE_DRAGON };
            duel.Players[0].extra.Add(new LiveCardState { code = garura });
            duel.Players[0].mzone[0] = darkDragon;
            duel.Players[0].mzone[1] = darkDragonTwo;
            Require(!DuelTactics.CanActivate(duel, summon, poly), "Super Polymerization spent only friendly material.");
            duel.Players[0].mzone[1] = null;
            duel.Players[1].mzone[0] = darkDragonTwo;
            Require(DuelTactics.CanActivate(duel, summon, poly), "Super Polymerization missed usable opposing material.");

            var benten = new SelectableCardItem { code = 77235086, location = YgoConstants.LOCATION_HAND };
            var ace = new SelectableCardItem { code = 39618799, location = YgoConstants.LOCATION_MZONE };
            summon.candidates.Clear();
            summon.candidates.AddRange(new[] { ace, benten });
            summon.SelectionHint = DuelTactics.Release;
            duel.CurrentChain[0].code = 39996157;
            Require(ChoosesSecond(), "The Ritual tribute should use Benten's search before spending the established ace.");
            var powerBond = new SelectableCardItem { code = 37630732 };
            duel.Players[0].lp = 1000;
            duel.Players[0].extra.Clear();
            duel.Players[0].extra.Add(new LiveCardState { code = 74157028 });
            Require(!DuelTactics.CanActivate(duel, summon, powerBond), "Power Bond paid lethal burn when Battle Phase was unavailable.");
            Debug.Log("[VERIFY GX AI SUMMON] Branded, HERO, Super Polymerization and Benten choices passed.");

            bool ChoosesSecond() => Smart().ComputeResponse(duel, summon).SequenceEqual(DuelSession.PackCardSelection(1));
        }

        private static void VerifyGxBattlePriority()
        {
            using var duel = new DuelSession();
            var attacker = new LiveCardState { code = 89631139, position = YgoConstants.POS_FACEUP_ATTACK, attack = 3000 };
            var yubel = new LiveCardState { code = 78371393, position = YgoConstants.POS_FACEUP_ATTACK };
            var pain = new LiveCardState { code = 65261141, position = YgoConstants.POS_FACEUP_ATTACK };
            var own = duel.Players[0];
            var rival = duel.Players[1];
            own.mzone[0] = attacker;
            rival.lp = 2000;
            var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, playerId = 0, canBp = true, canEp = true };
            idle.repositionable.Add(new SelectableCardItem { code = attacker.code, location = YgoConstants.LOCATION_MZONE });
            Require(Read(Smart().ComputeResponse(duel, idle)) == 6, "A visible direct lethal should enter Battle Phase immediately.");
            rival.mzone[0] = yubel;
            var battle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_BATTLECMD, playerId = 0, canM2 = true };
            var attack = new SelectableCardItem { code = attacker.code, location = YgoConstants.LOCATION_MZONE };
            battle.attackable.Add(attack);
            Require(Read(Smart().ComputeResponse(duel, battle)) == 2, "The AI attacked into Yubel's lethal reflection.");
            typeof(DuelSession).GetProperty(nameof(DuelSession.TurnNumber)).SetValue(duel, 2);
            rival.szone[0] = pain;
            rival.mzone[1] = FieldMonster(GeminiElf, 1);
            var master = new TacticalAI(null) { Difficulty = AIDifficulty.Master };
            Require(Read(master.ComputeResponse(duel, idle)) == 7 && Read(master.ComputeResponse(duel, battle)) == 2,
                "The AI entered Battle Phase to attack a weaker monster it was not allowed to target.");
            var replay = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_YESNO, promptDesc = YgoConstants.DESC_ATTACK_REPLAY };
            Require(Read(master.ComputeResponse(duel, replay)) == 0, "The AI confirmed a replay against a forced reflector.");
            pain.effectsDisabled = true;
            Require(Read(master.ComputeResponse(duel, idle)) == 6, "A disabled attack restriction still blocked a safe attack.");
            pain.effectsDisabled = false;
            rival.szone[0] = rival.mzone[1] = null;
            own.mzone[0] = yubel;
            own.szone[0] = pain;
            rival.mzone[0] = attacker;
            attack.code = yubel.code;
            Require(Read(Smart().ComputeResponse(duel, battle)) == 1, "Yubel did not exploit Nightmare Pain's reflection.");
            own.szone[0] = null;
            own.mzone[0] = FieldMonster(Baronne);
            own.mzone[1] = FieldMonster(86066372, 1);
            own.mzone[1].attack = 4300;
            rival.mzone[0] = FieldMonster(60303688);
            rival.lp = 6200;
            Require(!DuelCombat.CanCloseBattleFromMain(duel, 0),
                "Extra Deck attackers were allowed to destroy the conditionally protected defender.");
            own.mzone[2] = new LiveCardState { code = 3285552, type = YgoConstants.TYPE_TOKEN,
                sequence = 2, attack = 2000, position = YgoConstants.POS_FACEUP_ATTACK };
            Require(DuelCombat.CanCloseBattleFromMain(duel, 0),
                "The lethal solver missed using a token to remove a defender protected only against Extra Deck monsters.");
            Debug.Log("[VERIFY GX AI BATTLE] Open lethal, Yubel defense and Nightmare Pain retaliation passed.");
        }

        private static void VerifyNativeBentenSearch()
        {
            const uint ritual = 39996157, dakini = 39618799, benten = 77235086, petitAngel = 76103404;
            uint[] required = { ritual, dakini, benten, petitAngel };
            using var duel = YgoProjectBuilder.StartSeedMatching("Dakini and Benten", Deck(required), Empty, Deck(GeminiElf), Empty, true,
                current => required.All(code => current.Players[0].hand.Any(card => card.code == code)));
            bool searched = false;
            duel.OnCardMoved += move => searched |= move.fromLoc == YgoConstants.LOCATION_DECK &&
                move.toCon == 0 && move.toLoc == YgoConstants.LOCATION_HAND && duel.ResolvingChainLink?.code == benten;
            SubmitIdle(duel, 5, ritual);
            var ai = Smart();
            for (int step = 0; step < 25 && !duel.IsFinished && duel.RetryCount == 0; step++)
            {
                var prompt = duel.ActivePrompt;
                if (prompt == null) { duel.StepUntilPrompt(); continue; }
                if (IsIdle(prompt)) break;
                duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
            }
            Require(duel.Players[0].mzone.Any(card => card?.code == dakini) &&
                duel.Players[0].grave.Any(card => card.code == benten) && searched &&
                duel.RetryCount == 0, "Native Dakini must use Benten and resolve the resulting LIGHT Fairy search.");
            Debug.Log("[VERIFY GX AI RITUAL] Native Dakini summoned and Benten's search resolved. Retry=0");
        }

        private static void VerifyNativeRitualAcePreservation()
        {
            const uint ritual = 39996157, vishnu = 78316184, idaten = 3629090, tribute = 98777036;
            var deck = Deck(ritual, ritual, vishnu, idaten, tribute);
            using var duel = YgoProjectBuilder.StartSeedMatching("Vishnu into Idaten", deck, Empty, Deck(GeminiElf), Empty, true,
                current => current.Players[0].hand.Count(card => card.code == ritual) == 2 &&
                    new[] { vishnu, idaten, tribute }.All(code => current.Players[0].hand.Any(card => card.code == code)));
            SubmitIdle(duel, 5, ritual);
            Drive(duel, IsIdle, vishnu);
            var own = duel.Players[0];
            var idle = duel.ActivePrompt;
            Require(own.mzone.Any(card => card is { code: vishnu, IsFaceUp: true }) &&
                idle.activatable.Any(card => card.code == ritual && card.location == YgoConstants.LOCATION_HAND),
                "The native engine must offer a second Ritual while Vishnu is the only usable material.");
            var ai = new TacticalAI(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded(), searchCombos: true)
            {
                Difficulty = AIDifficulty.Master,
            };
            int choice = Read(ai.ComputeResponse(duel, idle));
            Require(choice == 7, $"With only Vishnu as material, the AI must end its turn instead of summoning Idaten. Choice={choice}");
            duel.SubmitResponseInt(choice);
            Drive(duel, IsIdle);
            Require(duel.TurnNumber == 2 && own.mzone.Any(card => card is { code: vishnu, IsFaceUp: true }) &&
                own.hand.Any(card => card.code == idaten) && duel.RetryCount == 0, "Vishnu did not survive the turn.");
            Debug.Log("[VERIFY GX AI RITUAL] Kept Vishnu when the only available follow-up would tribute it for Idaten. Retry=0");
        }

        private static void VerifyNativeRitualWithSpareMaterials()
        {
            const uint ritual = 39996157, absolute = 11398951, vishnu = 78316184, idaten = 3629090,
                tribute = 98777036, petitAngel = 76103404;
            foreach (uint secondSpell in new[] { ritual, absolute })
            {
                uint[] openingHand = { ritual, secondSpell, vishnu, idaten, tribute, GeminiElf, petitAngel };
                using var duel = new DuelSession(recordPlanningReplay: true);
                var deck = Deck(openingHand);
                var opponent = Deck(GeminiElf);
                bool found = false;
                for (ulong n = 1; n <= 600 && !found; n++)
                {
                    Require(duel.StartNewDuel(deck, Empty, opponent, Empty, seed: new[] { n * 29 + 101, n * 47 + 7, n * 71 + 13, n * 97 + 3 },
                        playerOpening: new DuelOpening(8000, openingHand.Length, 0)), "Could not start the prepared Ritual duel.");
                    found = duel.Players[0].hand.Select(card => card.code).OrderBy(code => code)
                        .SequenceEqual(openingHand.OrderBy(code => code));
                }
                Require(found, "Could not draw the prepared Ritual opening.");
                SubmitIdle(duel, 5, ritual);
                var material = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_SUM, vishnu);
                int tributeIndex = material.candidates.FindIndex(card => card.code == tribute);
                Require(tributeIndex >= 0, "Vishnu's initial tribute was absent.");
                duel.SubmitResponse(DuelSession.PackCardSelection(tributeIndex));
                var idle = Drive(duel, IsIdle);
                var second = idle.activatable.Find(card => card.code == secondSpell && card.location == YgoConstants.LOCATION_HAND);
                Require(second != null, "The engine did not offer the second Ritual.");
                SubmitIdle(duel, 5, secondSpell);
                var ai = Smart();
                for (int step = 0; step < 25 && !duel.IsFinished && duel.RetryCount == 0; step++)
                {
                    var prompt = duel.ActivePrompt;
                    if (prompt == null) { duel.StepUntilPrompt(); continue; }
                    if (IsIdle(prompt)) break;
                    duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
                }
                Require(duel.Players[0].mzone.Any(card => card is { code: vishnu, IsFaceUp: true }) &&
                    duel.Players[0].mzone.Any(card => card is { code: idaten, IsFaceUp: true }) && duel.RetryCount == 0,
                    $"The native Ritual {secondSpell} did not summon Idaten while keeping Vishnu. " + string.Join(" | ", duel.Logs.TakeLast(8)));
                Debug.Log($"[VERIFY GX AI RITUAL] {secondSpell} summoned Idaten with spare materials while Vishnu stayed on the field. Retry=0");
            }
        }

        private static void VerifyNativeExtraDeckAceTrades()
        {
            const uint stardust = 44508094, mist = 9348522, plaguespreader = 33420078, underclock = 77058170;
            const uint blueEyes = 89631139, alternative = 38517737, giantKiller = 88120966, darkfluid = 68934651;
            const uint domitianus = 33652635, bestiari = 41470137, gyzarus = 48156348;
            foreach (var (ace, material, summon, upgrade) in new (uint, uint, uint, bool)[]
            {
                (stardust, DuelTactics.EffectVeiler, mist, false),
                (blueEyes, alternative, giantKiller, false),
                (darkfluid, DuelTactics.EffectVeiler, underclock, false),
                (domitianus, bestiari, gyzarus, false),
                (stardust, plaguespreader, Baronne, true),
            })
            {
                using var duel = new DuelSession(recordPlanningReplay: true);
                var deck = Deck(GeminiElf).Append(material).ToArray();
                Require(duel.StartNewDuel(Deck(GeminiElf), Empty, deck, new[] { summon }, p0GoesFirst: false,
                    seed: new[] { 317UL, 43UL, 13UL, 61UL }, opponentOpening: new DuelOpening(8000, 5, ace, deckMonster: material)),
                    $"Could not start Extra Deck ace fixture {summon}.");
                Drive(duel, IsIdle);
                SubmitIdle(duel, 0, GeminiElf);
                var prompt = Drive(duel, IsIdle);
                int index = prompt.spSummonable.FindIndex(card => card.code == summon);
                Require(index >= 0, $"The engine must offer Extra Deck summon {summon} using ace {ace}.");
                using (var projection = duel.ReplayForPlanning(() => false))
                {
                    Require(projection != null, $"Could not replay material fixture {ace} -> {summon}.");
                    var trace = new List<string>();
                    bool paid = false;
                    projection.OnCardMoved += move =>
                    {
                        if (move.code != ace || move.fromLoc != YgoConstants.LOCATION_MZONE) return;
                        trace.Add($"{move.fromLoc}/{move.fromSeq}/{move.fromPos}->{move.toLoc}, reason={move.reason:x}, source={move.SourceReadout?.code}");
                        paid |= DuelAiPolicy.SpentEstablishedFieldAce(move, projection.SelectionChainLink);
                    };
                    projection.BeginSpeculation(0, stopAfterTurn: true);
                    double before = ComboSearch.BoardValue(projection, 0, canBattle: false, master: true);
                    projection.SubmitResponseInt((index << 16) | 1);
                    Drive(projection, IsIdle);
                    double after = ComboSearch.BoardValue(projection, 0, canBattle: false, master: true);
                    Require(paid && (upgrade ? after > before : after < before),
                        $"Fixture {ace} -> {summon} did not observe a scored material trade ({before:F2}->{after:F2}; {string.Join(" | ", trace)}).");
                }
                var master = new TacticalAI(DuelAiPolicy.LoadIncluded(), searchCombos: true) { Difficulty = AIDifficulty.Master };
                int response = Read(master.ComputeResponse(duel, prompt));
                Require(master.SimulatedComboBranches > 0 && (upgrade ? response == ((index << 16) | 1) : response == 7),
                    $"Unexpected Extra Deck material decision for {ace} -> {summon}, response {response}, branches {master.SimulatedComboBranches}.");
                duel.SubmitResponseInt(response);
                Drive(duel, IsIdle);
                var field = duel.Players[0].mzone;
                Require(duel.RetryCount == 0 && field.Any(card => card?.code == (upgrade ? summon : ace)) &&
                    !field.Any(card => card?.code == (upgrade ? ace : summon)),
                    $"Extra Deck ace fixture {ace} -> {summon} resolved to a different board.");
                Debug.Log($"[VERIFY MASTER EXTRA ACE] {ace} -> {summon}, verified upgrade={upgrade}, retry=0.");
            }

            const uint polymerization = 24094653, firstOfTheDragons = 10817524;
            using var fusion = new DuelSession(recordPlanningReplay: true);
            Require(fusion.StartNewDuel(Deck(GeminiElf), Empty, Deck(polymerization).Append(blueEyes).ToArray(),
                new[] { firstOfTheDragons }, p0GoesFirst: false, seed: new[] { 317UL, 43UL, 13UL, 61UL },
                opponentOpening: new DuelOpening(8000, 5, blueEyes, deckMonster: blueEyes)),
                "Could not start the activated Fusion fixture.");
            var fusionPrompt = Drive(fusion, IsIdle);
            Require(fusionPrompt.activatable.Any(card => card.code == polymerization &&
                DuelAiPolicy.NeedsAceFusionProjection(fusion.Players[0], card)), "An actual Fusion activation was not checked for ace costs.");
            var fusionMaster = new TacticalAI(DuelAiPolicy.LoadIncluded(), searchCombos: true) { Difficulty = AIDifficulty.Master };
            int fusionResponse = Read(fusionMaster.ComputeResponse(fusion, fusionPrompt));
            Require(fusionResponse == 7 && fusionMaster.SimulatedComboBranches > 0,
                $"Master activated an unprofitable Fusion using two Blue-Eyes, response {fusionResponse}.");
            fusion.SubmitResponseInt(fusionResponse);
            Drive(fusion, IsIdle);
            Require(fusion.Players[0].mzone.Count(card => card?.code == blueEyes) == 2 && fusion.RetryCount == 0,
                "The activated Fusion fixture did not preserve both established monsters.");
            Debug.Log("[VERIFY MASTER EXTRA ACE] A Fusion Spell using two established monsters was declined, retry=0.");
        }

        private static void VerifyAdditionalRitualAces()
        {
            const uint ritual = DuelAiPolicy.MachineAngelRitual, idaten = 3629090, summonedSkull = 70781052;
            var ritualDeck = new[] { idaten }.Concat(Enumerable.Repeat(ritual, 39)).ToArray();
            foreach (uint ace in new[] { DuelAiPolicy.HeraldOfUltimateness, DuelAiPolicy.HeraldOfPerfection, DuelAiPolicy.Izana })
            {
                using var duel = new DuelSession(recordPlanningReplay: true);
                OpenGxAiFixture(duel, ritualDeck, Empty, 8000, ace,
                    own => own.hand.Any(card => card.code == ritual) && own.hand.Any(card => card.code == idaten));
                var idle = Drive(duel, IsIdle);
                Require(idle.activatable.Any(card => card.code == ritual), $"The engine did not offer to tribute {ace} for Idaten.");
                var pilot = new TacticalAI(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded(), searchCombos: true)
                    { Difficulty = AIDifficulty.Master };
                int action = Read(pilot.ComputeResponse(duel, idle));
                Require(action == 7, $"The AI chose {action} instead of preserving ritual ace {ace}.");
                duel.SubmitResponseInt(action);
                Drive(duel, IsIdle);
                Require(duel.Players[0].mzone.Any(card => card?.code == ace) && duel.RetryCount == 0,
                    $"Ritual ace {ace} did not survive a legal follow-up.");

                using var mock = new DuelSession();
                mock.Players[0].mzone[0] = FieldMonster(ace);
                var tribute = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };
                tribute.summonable.Add(Effect(summonedSkull, 0, YgoConstants.LOCATION_HAND));
                Require(Read(Smart().ComputeResponse(mock, tribute)) == 7, $"{ace} was offered for a normal tribute summon.");
                mock.Players[0].mzone[1] = FieldMonster(GeminiElf, 1);
                var cost = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_TRIBUTE, min = 1, max = 1 };
                cost.candidates.Add(new SelectableCardItem { code = ace, location = YgoConstants.LOCATION_MZONE });
                cost.candidates.Add(new SelectableCardItem { code = GeminiElf, location = YgoConstants.LOCATION_MZONE, sequence = 1 });
                Require(Smart().ComputeResponse(mock, cost).SequenceEqual(DuelSession.PackCardSelection(1)),
                    $"Tribute selection spent {ace} instead of the spare monster.");
                Debug.Log($"[VERIFY GX RITUAL ACE] {ace}: preserved through native ritual and spare-material tribute selection.");
            }
            using var merciful = new DuelSession(recordPlanningReplay: true);
            const uint draw = DuelAiPolicy.MercifulMachineAngel;
            OpenGxAiFixture(merciful, Deck(draw), Empty, 8000, DuelAiPolicy.Dakini, own => own.hand.Count == 5);
            var active = Drive(merciful, IsIdle);
            Require(active.activatable.Any(card => card.code == draw) && Read(Smart().ComputeResponse(merciful, active)) == 7,
                "Merciful Machine Angel sacrificed the lone Dakini for a draw.");
            var board = merciful.Players[0];
            var magnificent = Effect(DuelAiPolicy.MagnificentMachineAngel, 0);
            Require(!DuelTactics.CanActivate(merciful, active, magnificent), "Magnificent Machine Angel offered the lone Dakini.");
            board.hand.Add(FieldMonster(DuelAiPolicy.Benten));
            Require(DuelTactics.CanActivate(merciful, active, magnificent), "Magnificent Machine Angel rejected a spare Benten.");
            Debug.Log("[VERIFY GX RITUAL ACE] Merciful and Magnificent Machine Angel preserved a lone Dakini.");
        }

        private static void VerifySearchedReplacementProof()
        {
            const uint searched = 24215921;
            var move = new CardMoveEvent { code = searched, fromCon = 1, fromLoc = YgoConstants.LOCATION_HAND,
                toCon = 1, toLoc = YgoConstants.LOCATION_MZONE, toPos = YgoConstants.POS_FACEUP_DEFENSE,
                reason = YgoConstants.REASON_SPSUMMON };
            var effect = new ChainLinkInfo { code = searched, triggerPlayer = 1, triggerLoc = YgoConstants.LOCATION_HAND };
            Require(AiBattleProbe.ConfirmsSearchedReplacement(move, effect, 1, searched),
                "The searched card's own resolved hand effect did not count as a special-summon replacement.");
            Require(!AiBattleProbe.ConfirmsSearchedReplacement(move, effect, 0, searched) &&
                !AiBattleProbe.ConfirmsSearchedReplacement(move, effect, 1, searched + 1) &&
                !AiBattleProbe.ConfirmsSearchedReplacement(move, null, 1, searched),
                "A different card, player, or unobserved effect was credited for the replacement.");
            effect.negated = true;
            Require(!AiBattleProbe.ConfirmsSearchedReplacement(move, effect, 1, searched),
                "A negated hand effect was credited for the replacement.");
        }

        private static void VerifyGxSelfDamageAndDestruction()
        {
            const uint bond = DuelAiPolicy.PowerBond, cyberDragon = 70095154, cyberTwin = 74157028,
                fortress = 79229522, overdragon = 64599569, squirmer = 24215921, spirit = 90829280, yubel = 78371393;
            using (var duel = new DuelSession(recordPlanningReplay: true))
            {
                OpenGxAiFixture(duel, Deck(bond, cyberDragon, cyberDragon), new[] { cyberTwin, fortress }, 2800, 0,
                    own => own.hand.Any(card => card.code == bond) && own.hand.Count(card => card.code == cyberDragon) >= 2);
                var idle = Drive(duel, IsIdle);
                Require(idle.activatable.Any(card => card.code == bond), "The native Power Bond fixture lacks a legal Fusion.");
                Require(!DuelTactics.CanActivate(duel, idle, Effect(bond, 0, YgoConstants.LOCATION_HAND)),
                    "An unavailable Fortress made lethal Power Bond damage appear safe.");
                int action = Read(Smart().ComputeResponse(duel, idle));
                Require((action & 0xffff) != 5, "The AI activated lethal Power Bond.");
            }
            using (var duel = new DuelSession(recordPlanningReplay: true))
            {
                OpenGxAiFixture(duel, Deck(bond, cyberDragon, cyberDragon), new[] { cyberTwin }, 2800, 0,
                    own => own.hand.Any(card => card.code == bond) && own.hand.Count(card => card.code == cyberDragon) >= 2,
                    second: true, rivalLife: 4000);
                Drive(duel, IsIdle);
                duel.SubmitResponseInt(7);
                var lethal = Drive(duel, IsIdle);
                Require(lethal.playerId == 1 && lethal.canBp && (Read(Smart().ComputeResponse(duel, lethal)) & 0xffff) == 5,
                    "The AI missed a native Power Bond lethal that ends before the burn.");
            }
            using (var duel = new DuelSession())
            {
                var own = duel.Players[0];
                own.lp = 2800;
                own.extra.Add(FieldMonster(overdragon));
                for (int i = 0; i < 4; i++) own.hand.Add(FieldMonster(cyberDragon));
                var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD };
                Require(!DuelAiPolicy.CanRiskPowerBond(duel, idle), "Variable original ATK made Overdragon look free of damage.");
                own.lp = 3201;
                Require(DuelAiPolicy.CanRiskPowerBond(duel, idle), "Power Bond rejected a nonlethal damage ceiling.");
            }
            foreach (bool replacement in new[] { false, true })
            {
                using var duel = new DuelSession();
                OpenGxAiFixture(duel, replacement ? Deck(squirmer, squirmer, yubel) : Deck(squirmer), Empty, 8000,
                    spirit, own => own.hand.Any(card => card.code == squirmer));
                Drive(duel, IsIdle);
                SubmitIdle(duel, 5, squirmer);
                var optional = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_YESNO);
                int choice = Read(Smart().ComputeResponse(duel, optional));
                Require(choice == (replacement ? 1 : 0), "Squirmer did not distinguish free development from pointless self-destruction.");
                if (!replacement)
                {
                    duel.SubmitResponseInt(choice);
                    Drive(duel, IsIdle);
                    Require(duel.Players[0].mzone.Any(card => card?.code == spirit) && duel.RetryCount == 0,
                        "Squirmer did not preserve Spirit of Yubel without a replacement.");
                }
            }
            using (var duel = new DuelSession())
            {
                const uint exodia = 83257450, frame = 64043465, strike = 40605147;
                var own = duel.Players[0];
                var rival = duel.Players[1];
                own.lp = 5000;
                rival.lp = 6000;
                own.mzone[0] = FieldMonster(exodia);
                own.szone[0] = FieldMonster(strike);
                own.szone[1] = FieldMonster(frame, 1);
                own.szone[2] = FieldMonster(frame, 2);
                var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };
                var blaze = new SelectableCardItem { code = exodia, location = YgoConstants.LOCATION_MZONE,
                    description = DuelTactics.ExoddBlazeEquipEffect };
                Require(!DuelTactics.CanActivate(duel, idle, blaze), "Exodd Blaze halved life and swept only its own back row in Main Phase 2.");
                idle.canBp = true;
                Require(DuelTactics.CanActivate(duel, idle, blaze), "Exodd Blaze missed an open lethal attack.");
                rival.mzone[0] = FieldMonster(yubel);
                Require(!DuelTactics.CanActivate(duel, idle, blaze), "Exodd Blaze opened an attack into lethal Yubel reflection.");
            }
            using (var duel = new DuelSession())
            {
                var chain = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
                var guide = FieldMonster(10802915);
                duel.Players[1].mzone[0] = guide;
                duel.CurrentChain.Add(new ChainLinkInfo { code = guide.code, triggerPlayer = 1,
                    triggerLoc = YgoConstants.LOCATION_MZONE });
                duel.Players[0].szone[0] = FieldMonster(5851097);
                var droplet = Effect(DuelTactics.ForbiddenDroplet, 0);
                Require(!DuelTactics.CanActivate(duel, chain, droplet),
                    "Droplet broke Vanity's Emptiness to negate a summon already forbidden by it.");
            }
            using (var duel = new DuelSession())
            {
                const uint droplet = DuelTactics.ForbiddenDroplet, herald = DuelAiPolicy.HeraldOfUltimateness;
                var own = duel.Players[0];
                own.mzone[0] = FieldMonster(herald);
                duel.Players[1].mzone[0] = FieldMonster(Stratos);
                var chain = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN };
                var spell = new SelectableCardItem { code = droplet, location = YgoConstants.LOCATION_SZONE, sequence = 2 };
                own.szone[2] = FieldMonster(droplet, 2);
                Require(!DuelTactics.CanActivate(duel, chain, spell),
                    "Droplet activated with only the established Herald available as its cost.");
                own.hand.Add(FieldMonster(GeminiElf));
                Require(DuelTactics.CanActivate(duel, chain, spell), "Droplet ignored a spare hand cost.");
                duel.CurrentChain.Add(new ChainLinkInfo { code = droplet });
                var cost = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, SelectionHint = DuelTactics.SendToGrave,
                    min = 1, max = 1 };
                cost.candidates.Add(new SelectableCardItem { code = herald, location = YgoConstants.LOCATION_MZONE });
                cost.candidates.Add(new SelectableCardItem { code = GeminiElf, location = YgoConstants.LOCATION_HAND });
                Require(Smart().ComputeResponse(duel, cost).SequenceEqual(DuelSession.PackCardSelection(1)),
                    "Droplet sent Herald instead of the expendable card.");
            }
            Debug.Log("[VERIFY GX SELF DAMAGE] Power Bond, native Squirmer, and Exodd Blaze timing passed.");
        }

        private static void VerifyVolcanicEmperorSiege()
        {
            const uint emperor = DuelTactics.VolcanicEmperor, excluder = DuelTactics.MiracleExcluder, dolphin = 17955766;
            using var duel = new DuelSession();
            Require(duel.StartNewDuel(Deck(excluder, dolphin), Empty, Deck(GeminiElf), Empty,
                seed: new[] { 11UL, 29UL, 71UL, 97UL }, playerOpening: new DuelOpening(500, 5, 0, destinyUses: 0),
                opponentOpening: new DuelOpening(8000, 5, emperor)), "Could not start the Volcanic Emperor fixture.");
            var prompt = duel.ActivePrompt;
            Require(prompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD, "The Volcanic fixture did not open in Main Phase.");
            int summon = prompt.activatable.FindIndex(card => card.code == excluder);
            Require(summon >= 0 && DuelTactics.HasLethalSpecialSummonBurn(duel, 0) &&
                !DuelTactics.CanActivate(duel, prompt, prompt.activatable[summon]),
                "The native Excluder summon was not rejected at 500 LP against Volcanic Emperor.");
            Require(BitConverter.ToInt32(Smart().ComputeResponse(duel, prompt), 0) != ((summon << 16) | 5),
                "The main-phase AI activated the fatal special summon.");

            var chain = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN };
            var increase = Effect(DuelTactics.VisionIncrease, 1, YgoConstants.LOCATION_SZONE);
            Require(!DuelTactics.CanActivate(duel, chain, increase), "Increase still chains into fatal summon damage.");
            Require(DuelTactics.CanActivate(duel, chain, Effect(excluder, 2, YgoConstants.LOCATION_GRAVE)),
                "The AI blocked Excluder's non-summoning banish effect.");
            duel.Players[0].lp = 501;
            Require(DuelTactics.CanActivate(duel, prompt, prompt.activatable[summon]),
                "A single 500 damage trigger should not disable a recoverable summon line at 501 LP.");
            duel.Players[0].lp = 500;
            duel.Players[1].mzone[2].effectsDisabled = true;
            Require(DuelTactics.CanActivate(duel, prompt, prompt.activatable[summon]),
                "The AI treated a negated Emperor as an active burn threat.");
            Require(duel.RetryCount == 0, "The Volcanic Emperor fixture retried a response.");
            Debug.Log("[VERIFY GX VOLCANIC BURN] Fatal optional summons rejected, non-summoning effects and safe windows kept.");
        }

        private static void VerifyDddContractRescueSearch()
        {
            const uint gate = 46372010, lamia = 19580308, kepler = 11609969;
            using var duel = new DuelSession();
            var deck = Deck(kepler).Append(lamia).ToArray();
            var extra = new uint[] { 9024198, 46593546 };
            Require(duel.StartNewDuel(Deck(GeminiElf), Empty, deck, extra, p0GoesFirst: false,
                seed: new[] { 17UL, 29UL, 71UL, 97UL }, opponentOpening: new DuelOpening(1500, 5, gate)),
                "Could not start the D/D/D contract fixture.");
            Drive(duel, IsIdle);
            Require(duel.Players[0].lp == 500 && !duel.Players[0].hand.Any(card => card.code == lamia),
                "The contract fixture did not start with the next standby payment lethal and Lamia in the deck.");
            SubmitIdle(duel, 5, gate);
            var search = Drive(duel, prompt => prompt.SelectionHint == DuelTactics.AddToHand);
            int lamiaIndex = search.candidates.FindIndex(card => card.code == lamia);
            Require(lamiaIndex >= 0 && DddTurnPlan.ChooseCard(duel, search) == lamiaIndex,
                "The lethal contract searched for an extender instead of Lamia.");
            var ai = Smart();
            duel.SubmitResponse(ai.ComputeResponse(duel, search));
            var main = Drive(duel, IsIdle);
            int rescue = main.activatable.FindIndex(card => card.code == lamia);
            Require(rescue >= 0 && BitConverter.ToInt32(ai.ComputeResponse(duel, main), 0) == ((rescue << 16) | 5),
                "The AI did not use Lamia to remove the lethal contract during Main Phase 1.");
            duel.SubmitResponseInt((rescue << 16) | 5);
            var cost = Drive(duel, prompt => prompt.SelectionHint == DuelTactics.SendToGrave);
            int gateIndex = cost.candidates.FindIndex(card => card.code == gate && card.location == YgoConstants.LOCATION_SZONE);
            Require(gateIndex >= 0 && ai.ComputeResponse(duel, cost).SequenceEqual(DuelSession.PackCardSelection(gateIndex)),
                "Lamia paid a harmless card rather than the lethal contract.");
            duel.SubmitResponse(DuelSession.PackCardSelection(gateIndex));
            Drive(duel, IsIdle);
            Require(duel.Players[0].lp == 500 && duel.Players[0].szone.All(card => card?.code != gate) && duel.RetryCount == 0,
                "The native engine did not resolve the contract rescue.");
            Debug.Log("[VERIFY DDD CONTRACT RESCUE] Gate found Lamia and Lamia removed the lethal upkeep in Main Phase 1.");
        }

        private static void OpenGxAiFixture(DuelSession duel, uint[] deck, uint[] extra, int life, uint ace,
            Func<PlayerFieldSnapshot, bool> ready, bool second = false, int rivalLife = 8000)
        {
            for (ulong seed = 1; seed <= 32; seed++)
            {
                Require(duel.StartNewDuel(Deck(GeminiElf), Empty, deck, extra, p0GoesFirst: second,
                    seed: new[] { seed, 29UL, 71UL, 97UL }, opponentOpening: new DuelOpening(life, 5, ace),
                    playerOpening: new DuelOpening(rivalLife, 5, 0)),
                    "Could not start the GX native fixture.");
                if (ready(duel.Players[duel.AiPlayerIndex])) return;
            }
            throw new InvalidOperationException("Could not draw the GX native fixture opening.");
        }

        private static void VerifyFieldAceEffects()
        {
            const uint mirrorjade = 44146295, advance = 4064925, pain = 65261141, inferno = 44352516,
                cartesia = 95515789, increase = 22865492, chaosGiant = 51788412, unicorn = 38342335,
                spirit = 90829280, horn = 41230939, sunrise = 22908820, avian = 21844576;
            using var duel = new DuelSession();
            var own = duel.Players[0];
            var rival = duel.Players[1];
            var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };
            var spare = FieldMonster(GeminiElf, 1);
            rival.szone[0] = new LiveCardState { code = Foolish };
            var mirrorEffect = Effect(mirrorjade, 0, YgoConstants.LOCATION_MZONE);
            Require(!DuelTactics.CanActivate(duel, idle, mirrorEffect), "Mirrorjade spent its banish on its empty opposing monster field.");
            Require(!DuelTactics.CanActivate(duel, idle, Effect(64043465, 0)), "Exodd Frame returned a friendly monster on an empty opposing field.");
            rival.szone[0] = null;
            Require(!DuelTactics.CanActivate(duel, idle, Effect(unicorn, 0, YgoConstants.LOCATION_MZONE)),
                "Knightmare Unicorn returned itself with an empty opposing field.");
            rival.mzone[0] = FieldMonster(Stratos);
            Require(DuelTactics.CanActivate(duel, idle, mirrorEffect) && DuelTactics.CanActivate(duel, idle, Effect(64043465, 0)),
                "Mirrorjade or Exodd Frame missed a valid opposing monster.");

            own.mzone[0] = FieldMonster(chaosGiant);
            var draw = Effect(advance, 1);
            Require(!DuelTactics.CanActivate(duel, idle, draw), "Ancient Gear Advance traded Chaos Giant for a draw.");
            own.mzone[1] = spare;
            Require(DuelTactics.CanActivate(duel, idle, draw), "Ancient Gear Advance rejected a spare tribute.");
            Require(ChooseCost(advance, DuelTactics.Release, chaosGiant, spare.code), "Ancient Gear Advance chose Chaos Giant over spare material.");

            own.mzone[0] = FieldMonster(unicorn);
            own.mzone[1] = null;
            var search = Effect(pain, 0);
            Require(!DuelTactics.CanActivate(duel, idle, search), "Nightmare Pain destroyed its only Unicorn.");
            own.mzone[1] = FieldMonster(spirit, 1);
            Require(DuelTactics.CanActivate(duel, idle, search), "Nightmare Pain rejected the Spirit of Yubel destruction trigger.");
            Require(ChooseCost(pain, DuelTactics.Destroy, unicorn, spirit), "Nightmare Pain destroyed Unicorn instead of Spirit of Yubel.");
            duel.CurrentChain.Clear();
            own.mzone[0] = FieldMonster(40044918);
            own.mzone[1] = FieldMonster(10802915, 1);
            rival.mzone[0] = null;
            rival.lp = 1000;
            idle.canBp = true;
            Require(!DuelTactics.CanSpendMasterInterruption(duel, idle, search),
                "Master traded a body for an optional search with a battle finish available.");
            rival.lp = 8000;
            idle.canBp = false;
            own.mzone[0] = null;
            Require(!DuelTactics.CanSpendMasterInterruption(duel, idle, search) &&
                DuelTactics.CanSpendMasterInterruption(duel, idle, search, projected: true),
                "An unproven search spent the only defender, or was not offered for native projection.");

            own.mzone[0] = FieldMonster(horn);
            own.mzone[1] = null;
            own.grave.Add(FieldMonster(82562802));
            own.szone[0] = new LiveCardState { code = 45078193, EquipTarget = (0, 0) };
            var recycle = Effect(inferno, 0);
            Require(!DuelTactics.CanActivate(duel, idle, recycle), "Cyberdark Inferno discarded live equipment.");
            own.szone[0] = null;
            Require(DuelTactics.CanActivate(duel, idle, recycle), "Cyberdark Inferno could not equip a previously unequipped monster.");

            own.mzone[0] = FieldMonster(mirrorjade);
            own.mzone[0].position = YgoConstants.POS_FACEDOWN_DEFENSE;
            own.mzone[1] = FieldMonster(cartesia, 1);
            own.mzone[5] = FieldMonster(38811586, 5);
            own.extra.Add(FieldMonster(11321089));
            var fuse = Effect(cartesia, 1, YgoConstants.LOCATION_MZONE);
            Require(!DuelTactics.CanActivate(duel, idle, fuse), "Cartesia gave up Mirrorjade for Chimera without the Spell payoff.");
            own.extra.Add(FieldMonster(24915933));
            Require(!DuelTactics.CanActivate(duel, idle, fuse), "Cartesia counted Albion the Sanctifire Dragon as legal material.");
            own.hand.Add(FieldMonster(DuelTactics.EffectVeiler));
            Require(DuelTactics.CanActivate(duel, idle, fuse), "Cartesia skipped a Fusion that could preserve Mirrorjade.");
            own.mzone[5] = null;

            own.mzone[0] = FieldMonster(46986414);
            own.mzone[1] = null;
            var rod = Effect(7084129, 1, YgoConstants.LOCATION_GRAVE);
            Require(!DuelTactics.CanActivate(duel, idle, rod), "Magician's Rod tributed Dark Magician just to recover itself.");
            own.mzone[1] = FieldMonster(DuelTactics.EffectVeiler, 1);
            Require(DuelTactics.CanActivate(duel, idle, rod), "Magician's Rod could not use a spare Spellcaster.");

            own.mzone[0] = FieldMonster(sunrise);
            own.mzone[1] = null;
            var extend = Effect(increase, 1);
            Require(!DuelTactics.CanActivate(duel, idle, extend), "Increase tributed its only Sunrise.");
            own.mzone[1] = FieldMonster(avian, 1);
            Require(DuelTactics.CanActivate(duel, idle, extend) && ChooseCost(increase, DuelTactics.Release, sunrise, avian),
                "Increase did not extend using a spare HERO.");
            Require(LinkComboPolicy.NeedsMaterialProjection(own, new SelectableCardItem
                { code = 1948619, location = YgoConstants.LOCATION_EXTRA }), "Wonder Driver could trade away Sunrise without projection.");
            Debug.Log("[VERIFY GX AI FIELD COST] Mirrorjade, Advance, Nightmare Pain, Inferno, Cartesia and Increase passed.");

            bool ChooseCost(uint source, ulong hint, uint primary, uint secondary)
            {
                duel.CurrentChain.Clear();
                duel.CurrentChain.Add(new ChainLinkInfo { code = source });
                var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, min = 1, max = 1, SelectionHint = hint };
                prompt.candidates.Add(new SelectableCardItem { code = primary, location = YgoConstants.LOCATION_MZONE });
                prompt.candidates.Add(new SelectableCardItem { code = secondary, location = YgoConstants.LOCATION_MZONE, sequence = 1 });
                return Smart().ComputeResponse(duel, prompt).SequenceEqual(DuelSession.PackCardSelection(1));
            }
        }

        private static void VerifyDrytronAceCosts()
        {
            const uint dad = 56863746, qua = 95209656, meteonis = 22398665, meteoroa = 24166324,
                alpha = 97148796, beta = 33543890, gamma = 60037599, phoenix = 2857636;
            using var duel = new DuelSession();
            var own = duel.Players[0];
            own.mzone[0] = FieldMonster(dad);
            own.grave.Add(FieldMonster(qua));
            var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };
            var ritual = Effect(meteonis, 0, YgoConstants.LOCATION_HAND);
            idle.activatable.Add(ritual);
            Require(Read(Smart().ComputeResponse(duel, idle)) == 7, "Meteonis replaced the established DAD with QUA.");
            own.mzone[0].position = YgoConstants.POS_FACEDOWN_DEFENSE;
            Require(Read(Smart().ComputeResponse(duel, idle)) == 7, "Meteonis treated the face-down DAD as expendable.");
            own.mzone[0].position = YgoConstants.POS_FACEUP_ATTACK;
            own.mzone[1] = FieldMonster(beta, 1);
            own.mzone[2] = FieldMonster(gamma, 2);
            Require(Read(Smart().ComputeResponse(duel, idle)) == 5, "Meteonis rejected two spare Machines.");
            own.mzone[1] = own.mzone[2] = null;
            idle.activatable[0] = Effect(meteoroa, 1);
            Require(Read(Smart().ComputeResponse(duel, idle)) == 7, "Meteoroa replaced the established DAD with QUA.");
            idle.activatable[0] = Effect(meteoroa, 0);
            Require(Read(Smart().ComputeResponse(duel, idle)) == 7, "Meteoroa tributed DAD for banished resources.");
            idle.activatable[0] = Effect(alpha, 0, YgoConstants.LOCATION_GRAVE);
            own.hand.Add(FieldMonster(meteonis));
            Require(Read(Smart().ComputeResponse(duel, idle)) == 7, "Alpha tributed an established DAD.");
            own.mzone[1] = FieldMonster(beta, 1);
            Require(Read(Smart().ComputeResponse(duel, idle)) == 5, "Alpha rejected a spare Drytron monster.");
            Require(LinkComboPolicy.NeedsMaterialProjection(own, new SelectableCardItem
                { code = phoenix, location = YgoConstants.LOCATION_EXTRA }), "A small Link could trade away DAD without projection.");
            Require(LinkComboPolicy.NeedsMaterialProjection(own, new SelectableCardItem
                { code = 98127546, location = YgoConstants.LOCATION_EXTRA }), "A high-Link summon could trade away DAD without projection.");
            Debug.Log("[VERIFY GX AI FIELD COST] Drytron rituals, starter tributes and Link commitments passed.");
        }

        private static void VerifyAccesscodeCosts()
        {
            const uint accesscode = 86066372, unicorn = 38342335;
            using var duel = new DuelSession();
            var own = duel.Players[0];
            own.mzone[0] = FieldMonster(accesscode);
            own.mzone[0].attack = 5300;
            duel.Players[1].mzone[0] = FieldMonster(GeminiElf);
            var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };
            idle.activatable.Add(Effect(accesscode, 1, YgoConstants.LOCATION_MZONE));
            var ai = Smart();
            Require(Read(ai.ComputeResponse(duel, idle)) == 7, "Accesscode banished itself for an optional removal.");
            own.grave.Add(FieldMonster(unicorn));
            Require(Read(ai.ComputeResponse(duel, idle)) == 5, "Accesscode missed a Link in the GY.");
            var cost = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, min = 1, max = 1, SelectionHint = DuelTactics.Banish };
            cost.candidates.Add(new SelectableCardItem { code = accesscode, location = YgoConstants.LOCATION_MZONE });
            cost.candidates.Add(new SelectableCardItem { code = unicorn, location = YgoConstants.LOCATION_GRAVE });
            Require(ai.ComputeResponse(duel, cost).SequenceEqual(DuelSession.PackCardSelection(1)), "Accesscode did not reserve its own body.");
            Require(Read(ai.ComputeResponse(duel, idle)) == 7, "An already-spent Link Attribute was treated as another valid cost.");
            Debug.Log("[VERIFY GX AI FIELD COST] Accesscode protected its body and used the available GY Link.");
        }

        private static SelectableCardItem Effect(uint code, uint index, uint location = YgoConstants.LOCATION_SZONE) =>
            new() { code = code, location = location, description = ((ulong)code << 20) | index };

        private static LiveCardState FieldMonster(uint code, uint slot = 0)
        {
            var info = CardDatabase.Get(code);
            return new LiveCardState { code = code, sequence = slot, position = YgoConstants.POS_FACEUP_ATTACK,
                location = YgoConstants.LOCATION_MZONE, attack = info.atk, defense = info.def, level = info.level,
                type = info.type, race = info.race, attribute = info.attr };
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

        internal static uint[] Deck(params uint[] cycle) => Enumerable.Range(0, 40).Select(i => cycle[i % cycle.Length]).ToArray();

        internal static bool IsIdle(DuelPrompt prompt) => prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD;

        private static bool IncludesChainEffect(DuelPrompt prompt, uint code) =>
            prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && prompt.candidates.Any(card => card.code == code);

        private static int Read(byte[] response) => BitConverter.ToInt32(response, 0);

        internal static void SubmitIdle(DuelSession duel, int command, uint code)
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

        internal static DuelPrompt Drive(DuelSession duel, Func<DuelPrompt, bool> stop, uint selected = 0)
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
