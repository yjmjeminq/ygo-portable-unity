using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    public enum AIDifficulty
    {
        Easy = 0,
        Normal = 1,
        Smart = 2,
    }

    public class TacticalAI
    {
        public AIDifficulty Difficulty { get; set; } = AIDifficulty.Smart;

        private int _lastTurn = -1;
        private int _actionsThisTurn;
        private int _battleActions;
        private int _unselectCount;
        private readonly Dictionary<string, int> _effectCounts = new Dictionary<string, int>();
        private readonly HashSet<uint> _repositionedSeqs = new HashSet<uint>();

        public void ResetTurnState(int turn)
        {
            if (_lastTurn != turn)
            {
                _lastTurn = turn;
                _actionsThisTurn = 0;
                _battleActions = 0;
                _unselectCount = 0;
                _effectCounts.Clear();
                _repositionedSeqs.Clear();
            }
        }

        public byte[] ComputeResponse(DuelSession session, DuelPrompt prompt)
        {
            if (prompt == null) return Array.Empty<byte>();
            ResetTurnState(session.TurnNumber);

            // Prevent retry deadlock: fallback safely if core rejected our last answer
            bool emergencyFallback = session.RetryCount > 3;

            switch (prompt.messageType)
            {
                case YgoConstants.MSG_SELECT_IDLECMD:
                    return BuildIdleCmd(session, prompt, emergencyFallback);

                case YgoConstants.MSG_SELECT_BATTLECMD:
                    return BuildBattleCmd(session, prompt, emergencyFallback);

                case YgoConstants.MSG_SELECT_CHAIN:
                    return BitConverter.GetBytes(PickChain(session, prompt, emergencyFallback));

                case YgoConstants.MSG_SELECT_EFFECTYN:
                    return BitConverter.GetBytes(DecideEffectYesNo(session, prompt));

                case YgoConstants.MSG_SELECT_YESNO:
                    return BitConverter.GetBytes(1);

                case YgoConstants.MSG_SELECT_OPTION:
                    return BitConverter.GetBytes(DddTurnPlan.ChooseOption(session, prompt) ?? 0);

                case YgoConstants.MSG_ANNOUNCE_NUMBER:
                    return BitConverter.GetBytes(0);

                case YgoConstants.MSG_SELECT_CARD:
                    return BuildSelectCard(session, prompt);

                case YgoConstants.MSG_SELECT_UNSELECT_CARD:
                    return BuildSelectUnselect(session, prompt);

                case YgoConstants.MSG_SELECT_TRIBUTE:
                    return BuildSelectTribute(session, prompt);

                case YgoConstants.MSG_SELECT_PLACE:
                case YgoConstants.MSG_SELECT_DISFIELD:
                    return BuildSelectPlace(prompt);

                case YgoConstants.MSG_SELECT_POSITION:
                    return BitConverter.GetBytes(PickPosition(prompt));

                case YgoConstants.MSG_SELECT_SUM:
                    return BuildSelectSum(prompt);

                case YgoConstants.MSG_SELECT_COUNTER:
                    return BuildSelectCounter(prompt);

                case YgoConstants.MSG_SORT_CHAIN:
                case YgoConstants.MSG_SORT_CARD:
                    return new byte[] { unchecked((byte)-1) };

                case YgoConstants.MSG_ANNOUNCE_RACE:
                    return BitConverter.GetBytes(PickBits64(prompt.announceMask, (int)prompt.min));

                case YgoConstants.MSG_ANNOUNCE_ATTRIB:
                    return BitConverter.GetBytes((uint)PickBits64(prompt.announceMask, (int)prompt.min));

                case YgoConstants.MSG_ANNOUNCE_CARD:
                {
                    uint[] valid = YgoNativeBridge.FilterDeclarableCards(prompt.options.ToArray(), 256);
                    uint pick = valid.Length > 0 ? valid[0] : 89631139u;
                    return BitConverter.GetBytes((int)pick);
                }

                case YgoConstants.MSG_ROCK_PAPER_SCISSORS:
                    return BitConverter.GetBytes(1 + Math.Abs(session.TurnNumber + session.Logs.Count) % 3);

                default:
                    return BitConverter.GetBytes(0);
            }
        }

        private byte[] BuildIdleCmd(DuelSession s, DuelPrompt p, bool fallback)
        {
            _unselectCount = 0;
            _actionsThisTurn++;

            if (fallback || _actionsThisTurn > 42)
            {
                if (p.canBp && HasWorthwhileAttack(s, p.playerId)) return BitConverter.GetBytes(6);
                if (p.canEp) return BitConverter.GetBytes(7);
            }
            else if (DddTurnPlan.ChooseIdle(s, p, CanUseIdleAction) is int planned)
            {
                return EncodeIdleAction(p, planned & 0xffff, planned >> 16);
            }

            // 1. High-ATK / Extra Deck Special Summons first
            if (p.spSummonable.Count > 0 && CountOwnMonsters(s, p.playerId) < 5)
            {
                int bestIdx = -1;
                int bestScore = int.MinValue;
                for (int i = 0; i < p.spSummonable.Count; i++)
                {
                    var item = p.spSummonable[i];
                    if (!CanUseIdleAction(1, item)) continue;
                    var ci = CardDatabase.Get(item.code);
                    int score = (ci != null ? ci.atk : 1500) + ((ci != null && ci.IsExtra) ? 1200 : 400);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIdx = i;
                    }
                }
                if (bestIdx >= 0) return EncodeIdleAction(p, 1, bestIdx);
            }

            // 2. Activate Spells / Monster Ignition Effects
            if (p.activatable.Count > 0)
            {
                int bestIdx = -1;
                int bestScore = int.MinValue;
                for (int i = 0; i < p.activatable.Count; i++)
                {
                    var act = p.activatable[i];
                    if (!CanUseIdleAction(5, act)) continue;
                    var ci = CardDatabase.Get(act.code);
                    int score = 500;
                    if (ci != null)
                    {
                        if (ci.IsMonster) score += 900;
                        else if (ci.IsSpell) score += 800;
                        else if (ci.IsTrap) score += 450;
                        if (ci.IsPendulum && act.location == YgoConstants.LOCATION_HAND) score += 300;
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIdx = i;
                    }
                }
                if (bestIdx >= 0) return EncodeIdleAction(p, 5, bestIdx);
            }

            // 3. Normal Summon strongest useful monster
            if (p.summonable.Count > 0)
            {
                int bestIdx = 0;
                int bestScore = int.MinValue;
                for (int i = 0; i < p.summonable.Count; i++)
                {
                    var ci = CardDatabase.Get(p.summonable[i].code);
                    int score = ci != null ? ci.atk + (ci.IsMonster && (ci.type & YgoConstants.TYPE_EFFECT) != 0 ? 400 : 0) : 1000;
                    if (score > bestScore) { bestScore = score; bestIdx = i; }
                }
                return BitConverter.GetBytes((bestIdx << 16) | 0);
            }

            // 4. Reposition weak attack monsters to defense, or defense beaters to attack before Battle Phase
            if (p.repositionable.Count > 0 && s.CurrentPhase == YgoConstants.PHASE_MAIN1)
            {
                for (int i = 0; i < p.repositionable.Count; i++)
                {
                    var rep = p.repositionable[i];
                    if (_repositionedSeqs.Contains(rep.sequence)) continue;
                    var live = s.Players[p.playerId].mzone[rep.sequence];
                    if (live == null) continue;
                    int oppMax = MaxOpponentAtk(s, p.playerId);
                    if (live.IsDefense && live.attack >= oppMax && live.attack >= 1400 && p.canBp)
                    {
                        _repositionedSeqs.Add(rep.sequence);
                        return BitConverter.GetBytes((i << 16) | 2);
                    }
                    if (!live.IsDefense && live.attack < oppMax && live.defense > live.attack + 400)
                    {
                        _repositionedSeqs.Add(rep.sequence);
                        return BitConverter.GetBytes((i << 16) | 2);
                    }
                }
            }

            // 5. Enter Battle Phase in MP1 if AI has profitable attacks
            if (p.canBp && HasWorthwhileAttack(s, p.playerId))
            {
                return BitConverter.GetBytes(6);
            }

            // 6. Monster Set only if no Normal Summon was possible & monster has solid DEF
            if (p.mSetable.Count > 0 && CountOwnMonsters(s, p.playerId) == 0)
            {
                int bestIdx = 0;
                int bestDef = -1;
                for (int i = 0; i < p.mSetable.Count; i++)
                {
                    var ci = CardDatabase.Get(p.mSetable[i].code);
                    int df = ci != null ? ci.def : 0;
                    if (df > bestDef) { bestDef = df; bestIdx = i; }
                }
                return BitConverter.GetBytes((bestIdx << 16) | 3);
            }

            // 7. Set Spell/Trap cards cleanly (Traps/Quick-Plays) in S/T zones
            if (p.sSetable.Count > 0)
            {
                for (int i = 0; i < p.sSetable.Count; i++)
                {
                    var ci = CardDatabase.Get(p.sSetable[i].code);
                    if (ci != null && (ci.IsTrap || (ci.type & YgoConstants.TYPE_QUICKPLAY) != 0))
                    {
                        return BitConverter.GetBytes((i << 16) | 4);
                    }
                }
            }

            if (p.canEp) return BitConverter.GetBytes(7);
            if (p.canBp) return BitConverter.GetBytes(6);
            return BitConverter.GetBytes(7);
        }

        private static string IdleActionKey(int command, SelectableCardItem item) => command == 1
            ? $"sp:{item.code}" : $"act:{item.code}:{item.description}:{item.location}:{item.sequence}";

        private bool CanUseIdleAction(int command, SelectableCardItem item) =>
            (command != 1 && command != 5) ||
            !_effectCounts.TryGetValue(IdleActionKey(command, item), out int count) || count < (command == 1 ? 2 : 1);

        private byte[] EncodeIdleAction(DuelPrompt prompt, int command, int index)
        {
            if (command == 1 || command == 5)
            {
                var item = command == 1 ? prompt.spSummonable[index] : prompt.activatable[index];
                BumpKey(IdleActionKey(command, item));
            }
            return BitConverter.GetBytes((index << 16) | command);
        }

        private byte[] BuildBattleCmd(DuelSession s, DuelPrompt p, bool fallback)
        {
            _battleActions++;
            bool canLeaveBattle = p.canM2 || p.canEp;
            if (canLeaveBattle && (fallback || _battleActions > 25))
                return BitConverter.GetBytes(p.canM2 ? 2 : 3);

            if (p.activatable.Count > 0)
            {
                for (int i = 0; i < p.activatable.Count; i++)
                {
                    var act = p.activatable[i];
                    string key = $"bpact:{act.code}:{act.description}:{act.sequence}";
                    if (_effectCounts.ContainsKey(key)) continue;
                    BumpKey(key);
                    return BitConverter.GetBytes((i << 16) | 0);
                }
            }

            if (p.attackable.Count > 0)
            {
                int opp = 1 - p.playerId;
                var oppMonsters = s.Players[opp].mzone.Where(m => m != null).ToList();

                // Pick attacker with guaranteed direct attack or favorable battle
                // A mandatory attacker still has to attack when no matchup is favorable.
                int bestIdx = canLeaveBattle ? -1 : 0;
                int bestGain = -1;

                for (int i = 0; i < p.attackable.Count; i++)
                {
                    var atkItem = p.attackable[i];
                    var live = s.Players[p.playerId].mzone[atkItem.sequence];
                    int myAtk = live != null ? live.attack : (CardDatabase.Get(atkItem.code)?.atk ?? 0);

                    if (atkItem.directAttackable)
                    {
                        int gain = myAtk + 500;
                        if (gain > bestGain)
                        {
                            bestGain = gain;
                            bestIdx = i;
                        }
                        continue;
                    }

                    foreach (var om in oppMonsters)
                    {
                        if (!om.IsFaceUp)
                        {
                            // Attack face-down conservatively with >= 1400 ATK or in Smart mode >= 1500
                            if (myAtk >= 1400)
                            {
                                int gain = myAtk - 800;
                                if (gain > bestGain) { bestGain = gain; bestIdx = i; }
                            }
                        }
                        else if (om.IsDefense)
                        {
                            if (myAtk > om.defense)
                            {
                                int gain = 600 + om.defense / 4;
                                if (gain > bestGain) { bestGain = gain; bestIdx = i; }
                            }
                        }
                        else
                        {
                            if (myAtk > om.attack)
                            {
                                int gain = (myAtk - om.attack) + 1200;
                                if (gain > bestGain) { bestGain = gain; bestIdx = i; }
                            }
                            else if (myAtk == om.attack && myAtk >= 1800 && CountOwnMonsters(s, p.playerId) > oppMonsters.Count)
                            {
                                int gain = 200;
                                if (gain > bestGain) { bestGain = gain; bestIdx = i; }
                            }
                        }
                    }
                }

                if (bestIdx >= 0)
                    return BitConverter.GetBytes((bestIdx << 16) | 1);
            }

            return BitConverter.GetBytes(p.canM2 ? 2 : 3);
        }

        private int PickChain(DuelSession s, DuelPrompt p, bool fallback)
        {
            if (p.candidates.Count == 0) return -1;
            if (p.forced) return 0;
            if (fallback || _actionsThisTurn > 48) return -1;

            // Avoid chaining a fast/quick effect to our own card if opponent hasn't responded,
            // UNLESS this is a simultaneous trigger chain (SEGOC: speCount == 0x7f, e.g. Tearlaments GY triggers).
            if (!p.IsTriggerChain && s.CurrentChain.Count > 0 && s.CurrentChain[s.CurrentChain.Count - 1].triggerPlayer == p.playerId)
                return -1;

            for (int i = 0; i < p.candidates.Count; i++)
            {
                var c = p.candidates[i];
                string key = $"ch:{s.TurnNumber}:{c.code}:{c.description}:{s.CurrentChain.Count}";
                if (_effectCounts.ContainsKey(key)) continue;
                BumpKey(key);
                return i;
            }
            return -1;
        }

        private int DecideEffectYesNo(DuelSession s, DuelPrompt p)
        {
            string key = $"eyn:{s.TurnNumber}:{p.promptCardCode}:{p.promptDesc}";
            if (_effectCounts.TryGetValue(key, out int cnt) && cnt >= 3) return 0;
            BumpKey(key);
            return 1;
        }

        private byte[] BuildSelectCard(DuelSession s, DuelPrompt p)
        {
            if (p.min == 1 && p.max == 1 && DddTurnPlan.ChooseCard(s, p) is int planned)
                return PackIndexList(new[] { planned });

            bool removalHint = s.LastHintMsg == 500 || s.LastHintMsg == 501 || s.LastHintMsg == 502 || s.LastHintMsg == 504;
            int opp = 1 - p.playerId;

            var ranked = p.candidates
                .Select((c, idx) => (idx, score: ScoreSelectCandidate(c, p.playerId, opp, removalHint)))
                .OrderByDescending(x => x.score)
                .ToList();

            int pickCount = (int)p.min;
            if (removalHint && p.max > p.min)
            {
                // Pick opponent targets up to p.max without touching own good cards
                int goodTargets = ranked.Count(r => r.score > 0);
                pickCount = Math.Max((int)p.min, Math.Min((int)p.max, goodTargets));
            }
            if (pickCount == 0 && p.cancelable && p.min == 0)
            {
                return BitConverter.GetBytes(-1);
            }
            pickCount = Math.Max(1, Math.Min(ranked.Count, pickCount));

            return PackIndexList(ranked.Take(pickCount).Select(x => x.idx));
        }

        private byte[] BuildSelectUnselect(DuelSession s, DuelPrompt p)
        {
            _unselectCount++;
            if (p.finishable && (p.unselectCandidates.Count > 0 || p.candidates.Count == 0 || _unselectCount >= p.candidates.Count + 2))
            {
                _unselectCount = 0;
                return BitConverter.GetBytes(-1);
            }
            if (p.cancelable && _unselectCount >= p.candidates.Count + 4)
            {
                _unselectCount = 0;
                return BitConverter.GetBytes(-1);
            }
            if (p.candidates.Count > 0)
            {
                if (DddTurnPlan.ChooseScale(s, p) is int scale)
                    return PackTwoInts(1, scale);

                int bestIdx = 0;
                int bestScore = int.MinValue;
                bool removal = s.LastHintMsg == 500 || s.LastHintMsg == 501 || s.LastHintMsg == 502 || s.LastHintMsg == 504;
                for (int i = 0; i < p.candidates.Count; i++)
                {
                    int sc = ScoreSelectCandidate(p.candidates[i], p.playerId, 1 - p.playerId, removal);
                    if (sc > bestScore) { bestScore = sc; bestIdx = i; }
                }
                return PackTwoInts(1, bestIdx);
            }
            return p.finishable ? BitConverter.GetBytes(-1) : PackTwoInts(1, 0);
        }

        private byte[] BuildSelectTribute(DuelSession s, DuelPrompt p)
        {
            var sorted = p.candidates
                .Select((c, idx) => (idx, rel: Math.Max(1, (int)c.releaseParam), atk: CardDatabase.Get(c.code)?.atk ?? 1000))
                .OrderBy(x => x.atk)
                .ToList();

            var chosen = new List<int>();
            int accRel = 0;
            foreach (var item in sorted)
            {
                if (chosen.Count >= p.max) break;
                chosen.Add(item.idx);
                accRel += item.rel;
                if (accRel >= p.min) break;
            }
            return PackIndexList(chosen);
        }

        private byte[] BuildSelectSum(DuelPrompt p)
        {
            int n = p.candidates.Count;
            int minCnt = (int)p.min;
            int maxCnt = p.max > 0 ? Math.Min(n, (int)p.max) : n;
            int target = (int)(p.acc & 0xFFFF);

            var mustParams = p.mustSelect.Select(m => ((int)(m.sumParam & 0xFFFF), (int)((m.sumParam >> 16) & 0xFFFF))).ToList();
            var candParams = p.candidates.Select(c => ((int)(c.sumParam & 0xFFFF), (int)((c.sumParam >> 16) & 0xFFFF))).ToList();
            var picked = new List<int>();

            if (!p.sumExactCountZeroAny && p.max > 0)
            {
                if (SolveExactSum(mustParams, candParams, target, minCnt, maxCnt, picked))
                    return PackIndexList(picked);
            }
            else
            {
                if (SolveOverflowSum(mustParams, candParams, target, picked))
                    return PackIndexList(picked);
            }

            // Fallback subset
            var fallback = Enumerable.Range(0, Math.Min(n, Math.Max(1, minCnt)));
            return PackIndexList(fallback);
        }

        private static bool CanReachExact(List<(int o1, int o2)> items, int idx, int rem)
        {
            if (rem == 0 && idx == items.Count) return true;
            if (idx >= items.Count || rem < 0) return false;
            var (o1, o2) = items[idx];
            if (o1 > 0 && CanReachExact(items, idx + 1, rem - o1)) return true;
            if (o2 > 0 && CanReachExact(items, idx + 1, rem - o2)) return true;
            return false;
        }

        private static bool SolveExactSum(
            List<(int o1, int o2)> must, List<(int o1, int o2)> cands,
            int target, int minCnt, int maxCnt, List<int> outIndices)
        {
            var cur = new List<int>();
            bool Dfs(int pos)
            {
                if (cur.Count >= minCnt && cur.Count <= maxCnt)
                {
                    var all = new List<(int, int)>(must.Count + cur.Count);
                    all.AddRange(must);
                    foreach (int i in cur) all.Add(cands[i]);
                    if (all.Count > 0 && CanReachExact(all, 0, target))
                    {
                        outIndices.AddRange(cur);
                        return true;
                    }
                }
                if (cur.Count >= maxCnt || pos >= cands.Count) return false;
                for (int i = pos; i < cands.Count; i++)
                {
                    cur.Add(i);
                    if (Dfs(i + 1)) return true;
                    cur.RemoveAt(cur.Count - 1);
                }
                return false;
            }
            return Dfs(0);
        }

        private static bool SolveOverflowSum(
            List<(int o1, int o2)> must, List<(int o1, int o2)> cands,
            int target, List<int> outIndices)
        {
            int mSumMin = 0, mSumMax = 0, mSmallest = int.MaxValue;
            foreach (var (o1, o2) in must)
            {
                int lo = (o2 > 0 && o2 < o1) ? o2 : o1;
                int hi = o2 > o1 ? o2 : o1;
                mSumMin += lo;
                mSumMax += hi;
                if (lo < mSmallest) mSmallest = lo;
            }

            var cur = new List<int>();
            bool Dfs(int pos, int sumMin, int sumMax, int smallest)
            {
                if ((cur.Count > 0 || must.Count > 0) && sumMax >= target && (sumMin - smallest) < target)
                {
                    outIndices.AddRange(cur);
                    return true;
                }
                if (sumMax >= target) return false;
                for (int i = pos; i < cands.Count; i++)
                {
                    var (o1, o2) = cands[i];
                    int lo = (o2 > 0 && o2 < o1) ? o2 : o1;
                    int hi = o2 > o1 ? o2 : o1;
                    cur.Add(i);
                    if (Dfs(i + 1, sumMin + lo, sumMax + hi, Math.Min(smallest, lo))) return true;
                    cur.RemoveAt(cur.Count - 1);
                }
                return false;
            }
            return Dfs(0, mSumMin, mSumMax, mSmallest);
        }

        private byte[] BuildSelectPlace(DuelPrompt p)
        {
            int needed = Math.Max(1, (int)p.min);
            uint avail = ~p.placeFlag;
            byte me = p.playerId;
            byte opp = (byte)(1 - me);
            var resp = new List<byte>(needed * 3);

            int[] mOrder = { 5, 6, 2, 1, 3, 0, 4 };
            int[] sOrder = { 2, 1, 3, 0, 4, 5, 6, 7 };

            void Scan(byte con, uint loc, int shift, int[] order)
            {
                foreach (int idx in order)
                {
                    if (resp.Count >= needed * 3) break;
                    if (((avail >> (shift + idx)) & 1u) != 0)
                    {
                        resp.Add(con);
                        resp.Add((byte)loc);
                        resp.Add((byte)idx);
                    }
                }
            }

            Scan(me, YgoConstants.LOCATION_MZONE, 0, mOrder);
            Scan(me, YgoConstants.LOCATION_SZONE, 8, sOrder);
            Scan(opp, YgoConstants.LOCATION_MZONE, 16, mOrder);
            Scan(opp, YgoConstants.LOCATION_SZONE, 24, sOrder);

            while (resp.Count < needed * 3)
            {
                resp.Add(me);
                resp.Add((byte)YgoConstants.LOCATION_MZONE);
                resp.Add(0);
            }
            return resp.ToArray();
        }

        private byte[] BuildSelectCounter(DuelPrompt p)
        {
            int rem = (int)p.min;
            byte[] buf = new byte[p.candidates.Count * 2];
            for (int i = 0; i < p.candidates.Count && rem > 0; i++)
            {
                int take = Math.Min(rem, (int)p.candidates[i].currentCounter);
                rem -= take;
                byte[] sbytes = BitConverter.GetBytes((short)take);
                buf[i * 2] = sbytes[0];
                buf[i * 2 + 1] = sbytes[1];
            }
            return buf;
        }

        private static int PickPosition(DuelPrompt p)
        {
            var ci = CardDatabase.Get(p.promptCardCode);
            bool preferAtk = ci == null || ci.atk >= ci.def;
            if (preferAtk && (p.allowedPositions & 0x1) != 0) return 0x1; // POS_FACEUP_ATTACK
            if (!preferAtk && (p.allowedPositions & 0x4) != 0) return 0x4; // POS_FACEUP_DEFENSE
            if ((p.allowedPositions & 0x1) != 0) return 0x1;
            if ((p.allowedPositions & 0x4) != 0) return 0x4;
            if ((p.allowedPositions & 0x8) != 0) return 0x8;
            return 0x2;
        }

        private static int ScoreSelectCandidate(SelectableCardItem item, byte self, int opp, bool isRemoval)
        {
            var ci = CardDatabase.Get(item.code);
            int atk = ci != null ? ci.atk : 1000;
            bool isOpp = item.controller == opp;
            if (isRemoval)
            {
                return (isOpp ? 10000 : -5000) + (isOpp ? atk : -atk);
            }
            return (isOpp ? -5000 : 10000) + atk + (item.location == YgoConstants.LOCATION_EXTRA ? 500 : 0);
        }

        private static bool HasWorthwhileAttack(DuelSession s, int playerId)
        {
            if (s.TurnNumber <= 1) return false;
            int opp = 1 - playerId;
            var oppMons = s.Players[opp].mzone.Where(m => m != null).ToList();
            foreach (var m in s.Players[playerId].mzone)
            {
                if (m == null || !m.IsAttack) continue;
                if (oppMons.Count == 0) return true;
                foreach (var om in oppMons)
                {
                    if (!om.IsFaceUp && m.attack >= 1400) return true;
                    if (om.IsDefense && m.attack > om.defense) return true;
                    if (!om.IsDefense && m.attack > om.attack) return true;
                }
            }
            return false;
        }

        private static int CountOwnMonsters(DuelSession s, int playerId)
        {
            int c = 0;
            foreach (var m in s.Players[playerId].mzone) if (m != null) c++;
            return c;
        }

        private static int MaxOpponentAtk(DuelSession s, int playerId)
        {
            int max = 0;
            foreach (var m in s.Players[1 - playerId].mzone)
            {
                if (m != null && m.IsFaceUp && m.attack > max) max = m.attack;
            }
            return max;
        }

        private static ulong PickBits64(ulong avail, int count)
        {
            ulong res = 0;
            int picked = 0;
            for (int b = 0; b < 64 && picked < count; b++)
            {
                if (((avail >> b) & 1UL) != 0)
                {
                    res |= 1UL << b;
                    picked++;
                }
            }
            return res;
        }

        private void BumpKey(string key)
        {
            _effectCounts[key] = _effectCounts.TryGetValue(key, out int c) ? c + 1 : 1;
        }

        private static byte[] PackTwoInts(int a, int b)
        {
            byte[] buf = new byte[8];
            Buffer.BlockCopy(BitConverter.GetBytes(a), 0, buf, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(b), 0, buf, 4, 4);
            return buf;
        }

        private static byte[] PackIndexList(IEnumerable<int> indices)
        {
            var list = indices.ToList();
            using (var ms = new MemoryStream(8 + list.Count * 4))
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(0); // format type 0
                bw.Write(list.Count);
                foreach (int idx in list) bw.Write(idx);
                return ms.ToArray();
            }
        }
    }
}
