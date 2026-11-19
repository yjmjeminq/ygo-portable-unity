using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using YGOPortable.AI;
using YGOPortable.Core;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace YGOPortable.Editor
{
    /// <summary>Bounded native proof of a Battle Phase win from public combat choices.</summary>
    internal static class AiBattleProbe
    {
        private const int MaxBranches = 12;
        private const int MaxResponses = 36;
        private const int Milliseconds = 200;
        private const int Attack = 1;
        private const int Main2 = 2;
        private const int EndBattle = 3;
        private const int EnterBattle = 6;
        private const int EndMain = 7;

        private enum Outcome { NoWin, Win, Unproven }

        private readonly struct Move
        {
            internal readonly byte[] response;
            internal readonly string description;

            internal Move(byte[] response, string description)
            {
                this.response = response;
                this.description = description;
            }

            internal static Move Command(int command, string description) => new(BitConverter.GetBytes(command), description);
        }

        internal static void Audit(DuelSession duel, DuelPrompt prompt, byte[] answer, int step)
        {
            if (TryProveMissedLethal(duel, prompt, answer, out string evidence))
                Debug.Log($"[AI BATTLE AUDIT] response {step}, turn {duel.TurnNumber}, seat {prompt.playerId}, " +
                    $"prompt {prompt.messageType}, recorded {BitConverter.ToString(answer)}, winning line {evidence}");
        }

        internal static bool TryProveMissedLethal(DuelSession live, DuelPrompt prompt, byte[] actual, out string evidence)
        {
            evidence = null;
            if (prompt == null || prompt != live.ActivePrompt || prompt.playerId != live.TurnPlayer ||
                live.IsFinished || actual == null || actual.Length < 4)
                return false;

            int command = BitConverter.ToInt32(actual, 0) & 0xffff;
            bool skippedBattle = prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && command == EndMain && prompt.canBp;
            bool leftBattle = prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD && command is Main2 or EndBattle;
            bool choseAttack = prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD && command == Attack;
            if (!skippedBattle && !leftBattle && !choseAttack && !prompt.IsAttackTargetSelection) return false;

            byte player = prompt.playerId;
            var alternatives = skippedBattle
                ? new List<Move> { Move.Command(EnterBattle, "enter Battle Phase") }
                : PublicChoices(live, prompt, player, out _);
            alternatives.RemoveAll(move => actual.SequenceEqual(move.response));
            if (alternatives.Count == 0) return false;

            var clock = Stopwatch.StartNew();
            bool Expired() => clock.ElapsedMilliseconds >= Milliseconds;
            int branches = MaxBranches;
            // Ending the turn or leaving Battle closes its attack lines. An actual attack must
            // have no winning continuation within the public search to prove that it was a miss.
            if (!skippedBattle && !leftBattle &&
                Search(live, player, new Move(actual, "actual"), Expired, ref branches, out _) != Outcome.NoWin)
                return false;

            foreach (var alternative in alternatives)
            {
                if (Search(live, player, alternative, Expired, ref branches, out string path) != Outcome.Win) continue;
                evidence = path;
                return true;
            }
            return false;
        }

        private static Outcome Search(DuelSession live, byte player, Move first, Func<bool> expired,
            ref int branches, out string proof)
        {
            var alternatives = new Stack<List<Move>>();
            alternatives.Push(new List<Move> { first });
            bool unproven = false;
            proof = null;
            while (alternatives.Count > 0 && branches > 0 && !expired())
            {
                branches--;
                var path = alternatives.Pop();
                using var trial = live.ReplayForPlanning(expired);
                if (trial == null) return Outcome.Unproven;
                trial.BeginSpeculation(player, stopAfterTurn: true);
                bool attackedHiddenMonster = false;
                trial.OnAttackDeclared += (attacker, _, _, target, location, sequence) =>
                {
                    if (attacker != player || target == player || target > 1 || location != YgoConstants.LOCATION_MZONE)
                        return;
                    var monsters = trial.Players[target].mzone;
                    if (sequence >= monsters.Length || monsters[sequence]?.IsFaceUp != true)
                        attackedHiddenMonster = true;
                };
                int applied = 0;
                while (applied < MaxResponses && !trial.IsFinished && !trial.SpeculationUncertain && !attackedHiddenMonster &&
                       trial.RetryCount == 0 && trial.TurnNumber == live.TurnNumber && !expired())
                {
                    var prompt = trial.ActivePrompt;
                    if (prompt == null)
                    {
                        unproven = true;
                        break;
                    }
                    if (applied < path.Count)
                    {
                        trial.SubmitPlanningResponse(path[applied++].response);
                        continue;
                    }
                    if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD) break;

                    var choices = PublicChoices(trial, prompt, player, out bool omitted);
                    unproven |= omitted;
                    if (choices.Count == 0) break;
                    for (int i = choices.Count - 1; i > 0; i--)
                        alternatives.Push(new List<Move>(path) { choices[i] });
                    path.Add(choices[0]);
                }
                if (!trial.SpeculationUncertain && !attackedHiddenMonster && trial.RetryCount == 0 && !expired() &&
                    trial.IsFinished && trial.Winner == player && trial.TurnNumber == live.TurnNumber)
                {
                    proof = string.Join(" > ", path.Select(move => move.description).Where(text => text != null));
                    return Outcome.Win;
                }
                unproven |= trial.SpeculationUncertain || attackedHiddenMonster || trial.RetryCount != 0 || expired() || applied >= MaxResponses;
            }
            return unproven || alternatives.Count > 0 ? Outcome.Unproven : Outcome.NoWin;
        }

        private static List<Move> PublicChoices(DuelSession duel, DuelPrompt prompt, byte player, out bool omitted)
        {
            var choices = new List<Move>();
            omitted = false;
            if (prompt.playerId != player)
            {
                // A real response window could contain a hidden defense. Its forced or optional
                // actions remain unproven. Empty windows are the only rival input we advance.
                if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced && prompt.candidates.Count == 0)
                    choices.Add(Move.Command(-1, null));
                else omitted = true;
                return choices;
            }

            if (prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD)
            {
                for (int i = 0; i < prompt.attackable.Count; i++)
                    choices.Add(Move.Command((i << 16) | Attack, $"attack with {prompt.attackable[i].code}"));
                // Optional spells and monsters' effects are outside an attack-order proof.
                omitted = prompt.activatable.Count > 0;
            }
            else if (prompt.IsAttackTargetSelection)
            {
                var rival = duel.Players[1 - player];
                for (int i = 0; i < prompt.candidates.Count; i++)
                {
                    var target = prompt.candidates[i];
                    if (target.controller == player || target.location != YgoConstants.LOCATION_MZONE ||
                        target.sequence >= rival.mzone.Length || rival.mzone[target.sequence]?.IsFaceUp != true)
                    {
                        omitted = true;
                        continue;
                    }
                    choices.Add(new Move(Target(i), $"target {target.code}"));
                }
            }
            else if (prompt.IsAttackDecision)
            {
                choices.Add(Move.Command(1, prompt.IsDirectAttackChoice ? "attack directly" : "continue attack"));
                choices.Add(Move.Command(0, prompt.IsDirectAttackChoice ? "target a monster" : null));
            }
            else if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced)
            {
                choices.Add(Move.Command(-1, null));
                omitted = prompt.candidates.Count > 0;
            }
            else omitted = true;
            return choices;
        }

        private static byte[] Target(int index)
        {
            var response = new byte[12];
            Buffer.BlockCopy(new[] { 0, 1, index }, 0, response, 0, response.Length);
            return response;
        }

        // An isolated Unity Editor entry point. It never starts or builds a game Player.
        public static void Run()
        {
            int exit = 0;
            bool initialize = !YgoNativeBridge.IsInitialized;
            int nativeErrors = 0;
            try
            {
                if (initialize && YgoNativeBridge.Initialize(Application.streamingAssetsPath, (kind, message) =>
                    {
                        if (kind == 0) nativeErrors++;
                    }) != 0)
                    throw new InvalidOperationException("Could not initialize the duel core.");
                CardDatabase.Load(Application.streamingAssetsPath);
                VerifyFixtures();
                if (nativeErrors != 0) throw new InvalidOperationException($"Native battle fixture errors: {nativeErrors}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                exit = 1;
            }
            finally
            {
                if (initialize && YgoNativeBridge.IsInitialized) YgoNativeBridge.Shutdown();
                if (Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }

        internal static void VerifyFixtures()
        {
            const uint photonThrasher = 65367484, cyberDragon = 70095154, kuriboh = 40640057, bookOfMoon = 14087893;
            uint[] first = Enumerable.Repeat(photonThrasher, 40).ToArray();
            uint[] second = Enumerable.Repeat(cyberDragon, 20).Concat(Enumerable.Repeat(kuriboh, 20)).ToArray();
            using (var duel = OpenFixture(first, second, 2000, new[] { photonThrasher }, new[] { cyberDragon, kuriboh }))
            {
                PrepareBlocker(duel, photonThrasher, 0);
                PrepareTwoAttackers(duel, cyberDragon, kuriboh);
                var battle = Active(duel, YgoConstants.MSG_SELECT_BATTLECMD, 1);
                int wrong = battle.attackable.FindIndex(card => card.code == cyberDragon);
                int correct = battle.attackable.FindIndex(card => card.code == kuriboh);
                Require(wrong >= 0 && correct >= 0, "The alternate-order fixture lacks its two legal attacks.");
                Require(TryProveMissedLethal(duel, battle, BitConverter.GetBytes((wrong << 16) | Attack), out string proof),
                    "The native probe did not prove a win after reversing the first two attacks.");
                Require(!TryProveMissedLethal(duel, battle, BitConverter.GetBytes((correct << 16) | Attack), out _),
                    "The native probe incorrectly reported the winning attack order as a miss.");
                Debug.Log($"[AI BATTLE PROBE] Wrong first attack proved: {proof}");
            }

            const uint mataza = 22609617, blueEyes = 89631139;
            using (var duel = OpenFixture(Enumerable.Repeat(blueEyes, 40).ToArray(), Enumerable.Repeat(mataza, 40).ToArray(),
                2400, new[] { blueEyes }, new[] { mataza }))
            {
                Idle(duel, 0);
                duel.SubmitResponseInt(EndMain);
                var main = Idle(duel, 1);
                SubmitCard(duel, main.summonable, mataza, 0);
                main = Idle(duel, 1);
                Require(main.canBp, "The native multi-attack fixture did not reach a legal Battle Phase choice.");
                Require(TryProveMissedLethal(duel, main, BitConverter.GetBytes(EndMain), out string proof),
                    "The native probe failed to prove two attacks when the chosen action skipped Battle.");
                duel.SubmitResponseInt(EnterBattle);
                var battle = Active(duel, YgoConstants.MSG_SELECT_BATTLECMD, 1);
                Require(DuelCombat.FindLethal(duel, battle) == null,
                    "The multi-attack fixture should exercise a win that the static planner does not find.");
                Debug.Log($"[AI BATTLE PROBE] Skipped multi-attack Battle proved: {proof}");
            }

            first = Enumerable.Repeat(photonThrasher, 20).Concat(Enumerable.Repeat(bookOfMoon, 20)).ToArray();
            using (var duel = OpenFixture(first, second, 2000, new[] { photonThrasher, bookOfMoon }, new[] { cyberDragon, kuriboh }))
            {
                PrepareBlocker(duel, photonThrasher, bookOfMoon);
                PrepareTwoAttackers(duel, cyberDragon, kuriboh);
                var battle = Active(duel, YgoConstants.MSG_SELECT_BATTLECMD, 1);
                Require(!TryProveMissedLethal(duel, battle, BitConverter.GetBytes(Main2), out _),
                    "A private opposing response window was incorrectly treated as a proved win.");
                SubmitCard(duel, battle.attackable, kuriboh, Attack);
                if (duel.ActivePrompt?.IsAttackTargetSelection == true)
                    duel.SubmitResponse(Target(0));
                var privateWindow = Active(duel, YgoConstants.MSG_SELECT_CHAIN, 0);
                Require(privateWindow.candidates.Any(card => card.code == bookOfMoon),
                    "The negative fixture did not offer the opposing private response during the attack.");
                Debug.Log("[AI BATTLE PROBE] Opposing private response remained unproven.");
            }
        }

        private static DuelSession OpenFixture(uint[] first, uint[] second, int lp, uint[] needFirst, uint[] needSecond)
        {
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var duel = new DuelSession(recordPlanningReplay: true);
                if (!duel.StartNewDuel(first, Array.Empty<uint>(), second, Array.Empty<uint>(),
                                      startingLp: lp, seed: new[] { seed, seed + 3, seed + 7, seed + 11 }))
                {
                    duel.Dispose();
                    throw new InvalidOperationException("Could not start the native combat fixture.");
                }
                if (needFirst.All(code => duel.Players[0].hand.Any(card => card.code == code)) &&
                    needSecond.All(code => duel.Players[1].hand.Any(card => card.code == code)))
                    return duel;
                duel.Dispose();
            }
            throw new InvalidOperationException("Could not find the native combat fixture's starting hands.");
        }

        private static void PrepareBlocker(DuelSession duel, uint blocker, uint backrow)
        {
            SubmitCard(duel, Idle(duel, 0).spSummonable, blocker, 1);
            var main = Idle(duel, 0, YgoConstants.POS_FACEUP_DEFENSE);
            Require(duel.Players[0].mzone.Any(card => card?.code == blocker && card.IsFaceUp && card.IsDefense),
                "The native blocker was not placed face-up in defense.");
            if (backrow != 0)
            {
                SubmitCard(duel, main.sSetable, backrow, 4);
                Idle(duel, 0);
            }
            duel.SubmitResponseInt(EndMain);
        }

        private static void PrepareTwoAttackers(DuelSession duel, uint attacker, uint normal)
        {
            SubmitCard(duel, Idle(duel, 1).spSummonable, attacker, 1);
            var main = Idle(duel, 1, YgoConstants.POS_FACEUP_ATTACK);
            SubmitCard(duel, main.summonable, normal, 0);
            Idle(duel, 1);
            duel.SubmitResponseInt(EnterBattle);
        }

        private static DuelPrompt Idle(DuelSession duel, byte player, uint position = YgoConstants.POS_FACEUP_ATTACK) =>
            Active(duel, YgoConstants.MSG_SELECT_IDLECMD, player, position);

        private static DuelPrompt Active(DuelSession duel, byte message, byte player, uint position = YgoConstants.POS_FACEUP_ATTACK)
        {
            var ai = new TacticalAI(null);
            for (int step = 0; step < 24 && !duel.IsFinished && duel.RetryCount == 0; step++)
            {
                var prompt = duel.ActivePrompt;
                if (prompt == null) throw new InvalidOperationException("The native combat fixture stopped at an empty prompt.");
                if (prompt.messageType == message && prompt.playerId == player) return prompt;
                if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced)
                    duel.SubmitResponseInt(-1);
                else if (prompt.messageType == YgoConstants.MSG_SELECT_POSITION && (prompt.allowedPositions & position) != 0)
                    duel.SubmitResponseInt((int)position);
                else if (prompt.messageType == YgoConstants.MSG_SELECT_PLACE)
                    duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
                else
                    throw new InvalidOperationException($"Unexpected setup prompt {prompt.messageType} for seat {prompt.playerId}.");
            }
            throw new InvalidOperationException($"The native combat fixture did not reach prompt {message} for seat {player}.");
        }

        private static void SubmitCard(DuelSession duel, List<SelectableCardItem> options, uint code, int command)
        {
            int index = options.FindIndex(card => card.code == code);
            Require(index >= 0, $"Missing legal setup choice {code}/{command}.");
            duel.SubmitResponseInt((index << 16) | command);
        }

        private static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }
    }
}
