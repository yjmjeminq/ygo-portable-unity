using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal sealed class ComboSearch
    {
        private const int MaxBranches = 18;
        private const int MaxMainActions = 12;
        private const int MaxResponses = 76;
        private const int MaxSearchesPerTurn = 3;
        private const int MaxLateLinkSearches = 2;
        private const int BudgetMilliseconds = 75;
        private const int BranchMilliseconds = 26;
        private const int LateLinkBranchMilliseconds = 42;
        private const uint Machinex = 46593546, Siegfried = 44852429, Gilgamesh = 9024198;

        private readonly Queue<(ulong state, int action)> _next = new();
        private int _turn = -1;
        private int _searches;
        private int _lateLinkSearches;
        private DuelPrompt _prepared;
        internal int SimulatedBranches { get; private set; }
        internal int ChangedLines { get; private set; }

        private sealed class Line
        {
            internal double score = double.NegativeInfinity;
            internal bool complete;
            internal int unpaidLinkTrade = int.MaxValue;
            internal readonly List<(ulong state, int action)> moves = new();
            internal readonly List<(int[] prefix, int[] options)> forks = new();
        }

        private readonly struct LinkPosition
        {
            internal readonly int rank, opposingCards, interaction;
            internal readonly bool lethal;

            internal LinkPosition(DuelSession duel, byte player, bool canBattle)
            {
                var own = duel.Players[player];
                var rival = duel.Players[1 - player];
                int attackers = 0;
                rank = interaction = 0;
                foreach (var monster in own.mzone)
                {
                    if (monster?.IsFaceUp != true) continue;
                    if (monster.IsAttack) attackers += Math.Max(0, monster.attack);
                    rank = Math.Max(rank, LinkComboPolicy.Rank(monster.code));
                    if (!monster.effectsDisabled && CardDatabase.Get(monster.code) is CardInfo card && LinkComboPolicy.IsInteractive(card))
                        interaction++;
                }
                int monsters = rival.mzone.Count(card => card != null);
                opposingCards = monsters + rival.szone.Count(card => card != null);
                lethal = canBattle && monsters == 0 && attackers >= rival.lp;
            }
        }

        internal static bool CanDevelop(DuelPrompt prompt)
        {
            // A usable engine action is stronger evidence than the deck name or the contents of a filler Extra Deck.
            if (prompt.spSummonable.Any(card => card.controller == prompt.playerId)) return true;
            return prompt.activatable.Any(card => card.controller == prompt.playerId &&
                    (card.description == 1160 || CanDevelopFromCard(CardDatabase.Get(card.code)))) ||
                prompt.summonable.Any(card => card.controller == prompt.playerId && CanDevelopFromCard(CardDatabase.Get(card.code)));
        }

        private static bool CanDevelopFromCard(CardInfo card) => card != null &&
            ((card.type & (YgoConstants.TYPE_TUNER | YgoConstants.TYPE_RITUAL)) != 0 ||
            (card.DeckEffectCategories & ((1u << 5) | (1u << 6) | (1u << 7) | (1u << 18))) != 0);

        internal int? Choose(DuelSession live, DuelPrompt prompt, TacticalAI advisor)
        {
            if (!ReferenceEquals(_prepared, prompt))
            {
                using var prepare = Prepare(live, prompt, advisor).GetEnumerator();
                while (prepare.MoveNext()) { }
            }
            return _next.Count > 0 && _next.Peek().state == Fingerprint(live, prompt) ? _next.Dequeue().action : null;
        }

        internal IEnumerable<bool> Prepare(DuelSession live, DuelPrompt prompt, TacticalAI advisor)
        {
            if (ReferenceEquals(_prepared, prompt)) yield break;
            _prepared = prompt;
            if (_turn != live.TurnNumber)
            {
                _turn = live.TurnNumber;
                _searches = 0;
                _lateLinkSearches = 0;
                _next.Clear();
            }

            ulong state = Fingerprint(live, prompt);
            var own = live.Players[prompt.playerId];
            bool CouldTrade(SelectableCardItem item) => LinkComboPolicy.NeedsMaterialProjection(own, item);
            bool cached = _next.Count > 0 && _next.Peek().state == state;
            // An opening search may run out before the first strong Link monster reaches the board.
            bool lateLinkTrade = _searches >= MaxSearchesPerTurn && _lateLinkSearches < MaxLateLinkSearches &&
                prompt.spSummonable.Any(CouldTrade);
            if (cached && !lateLinkTrade) yield break;
            if (!cached) _next.Clear();
            if (!lateLinkTrade && _searches >= MaxSearchesPerTurn) yield break;
            var options = advisor.ComboMainOptions(live, prompt);
            var lateOptions = lateLinkTrade ? options.Where(action => (action & 0xffff) == 1)
                .Select(action => (action, summon: prompt.spSummonable[action >> 16]))
                .Where(option => CouldTrade(option.summon))
                .OrderByDescending(option => LinkComboPolicy.IsBridge(option.summon.code))
                .ThenBy(option => LinkComboPolicy.Rank(option.summon.code))
                .Take(3).Select(option => option.action).ToArray() : Array.Empty<int>();
            if (lateLinkTrade ? lateOptions.Length == 0 : options.Length < 2) yield break;
            if (lateLinkTrade) _lateLinkSearches++;
            else _searches++;

            var watch = new Stopwatch();
            bool Expired() => watch.ElapsedMilliseconds >= BudgetMilliseconds;
            var queue = new Queue<int[]>();
            queue.Enqueue(cached ? _next.Select(move => move.action).ToArray() : Array.Empty<int>());
            var seen = new HashSet<string>();
            int branchBudget = lateLinkTrade ? LateLinkBranchMilliseconds : BranchMilliseconds;
            foreach (int option in lateOptions)
            {
                queue.Enqueue(new[] { option });
                seen.Add(option.ToString());
            }
            Line best = null;
            Line baseline = null;
            for (int attempt = 0; queue.Count != 0 && attempt < MaxBranches && !Expired(); attempt++)
            {
                int[] choices = queue.Dequeue();
                Line line;
                long branchStart = watch.ElapsedMilliseconds;
                watch.Start();
                bool BranchExpired() => Expired() || watch.ElapsedMilliseconds - branchStart >= branchBudget;
                using (var trial = live.ReplayForPlanning(BranchExpired))
                {
                    if (trial == null || Fingerprint(trial, trial.ActivePrompt) != state) break;
                    SimulatedBranches++;
                    trial.BeginSpeculation(prompt.playerId);
                    line = Explore(trial, advisor.ForkForComboSearch(), prompt.playerId, choices, BranchExpired);
                }
                baseline ??= line;
                bool better = line.unpaidLinkTrade == int.MaxValue && (best == null ||
                    ((best.complete || lateLinkTrade) && line.complete && line.moves.Count > 0 && line.score > best.score + .08));
                if (better) best = line;
                if (better || (line.complete && line.unpaidLinkTrade != int.MaxValue))
                {
                    // Examine both the opening choice and the transition into a possible finish.
                    var forks = better ? line.forks : line.forks.Where(fork => fork.prefix.Length > line.unpaidLinkTrade).ToList();
                    for (int offset = 0; offset < Math.Min(6, forks.Count) && queue.Count < MaxBranches; offset++)
                    {
                        int depth = offset % 2 == 0 ? offset / 2 : forks.Count - 1 - offset / 2;
                        var fork = forks[depth];
                        foreach (int option in fork.options.Take(2))
                        {
                            var branch = new int[fork.prefix.Length + 1];
                            Array.Copy(fork.prefix, branch, fork.prefix.Length);
                            branch[^1] = option;
                            if (seen.Add(string.Join(",", branch))) queue.Enqueue(branch);
                        }
                    }
                }
                watch.Stop();
                if (queue.Count != 0 && !Expired()) yield return true;
            }

            if (best == null || best.moves.Count == 0) yield break;
            if (best != baseline) ChangedLines++;
            _next.Clear();
            foreach (var move in best.moves) _next.Enqueue(move);
        }

        private static Line Explore(DuelSession trial, TacticalAI ai, byte player, int[] prefix, Func<bool> expired)
        {
            var line = new Line();
            int settledMoves = 0;
            int startTurn = trial.TurnNumber;
            uint linkTarget = 0;
            int targetRank = 0;
            bool canBattle = false;
            var beforeLink = default(LinkPosition);
            double beforeLinkScore = 0;
            var trades = new List<(int index, int rank, double score, LinkPosition before)>();
            trial.OnCardMoved += move =>
            {
                if (targetRank == 0) return;
                if (move.code == linkTarget && move.toCon == player && move.toLoc == YgoConstants.LOCATION_MZONE &&
                    (move.reason & YgoConstants.REASON_SPSUMMON) != 0)
                    targetRank = 0;
                if (move.fromCon != player || move.fromLoc != YgoConstants.LOCATION_MZONE ||
                    (move.reason & YgoConstants.REASON_LINK) == 0) return;
                int rank = LinkComboPolicy.Rank(move.code);
                if (LinkComboPolicy.IsDowngrade(rank, targetRank))
                    trades.Add((line.moves.Count - 1, rank, beforeLinkScore, beforeLink));
            };
            for (int i = 0; i < MaxResponses && !expired() && trial.TurnNumber == startTurn &&
                trial.RetryCount == 0 && !trial.SpeculationUncertain; i++)
            {
                if (trial.IsFinished)
                {
                    line.score = trial.Winner == player ? 100 : -100;
                    settledMoves = line.moves.Count;
                    line.complete = true;
                    break;
                }
                var prompt = trial.ActivePrompt;
                if (prompt == null) { trial.StepUntilPrompt(); continue; }
                if (prompt.playerId != player)
                {
                    if (prompt.messageType != YgoConstants.MSG_SELECT_CHAIN || prompt.forced) break;
                    trial.SubmitResponseInt(-1);
                    continue;
                }
                if (prompt.messageType != YgoConstants.MSG_SELECT_IDLECMD)
                {
                    if (prompt.candidates.Any(card => card.controller != player &&
                        card.location is YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_DECK or YgoConstants.LOCATION_EXTRA)) break;
                    trial.SubmitResponse(ai.ComputeResponse(trial, prompt));
                    continue;
                }

                canBattle = prompt.canBp;
                line.score = BoardValue(trial, player, canBattle) - line.moves.Count * .01;
                settledMoves = line.moves.Count;
                if (settledMoves >= MaxMainActions) break;
                var options = ai.ComboMainOptions(trial, prompt);
                int? forced = settledMoves < prefix.Length ? prefix[settledMoves] : null;
                byte[] response = ai.ComputeComboResponse(trial, prompt, forced);
                int move = BitConverter.ToInt32(response, 0);
                if (forced.HasValue && forced != move) break;
                targetRank = 0;
                if ((move & 0xffff) == 1 && prompt.spSummonable[move >> 16] is { location: YgoConstants.LOCATION_EXTRA } summon)
                {
                    int next = LinkComboPolicy.Rank(summon.code);
                    if (LinkComboPolicy.HasStrongerLink(trial.Players[player], next))
                    {
                        linkTarget = summon.code;
                        targetRank = next;
                        beforeLink = new LinkPosition(trial, player, canBattle);
                        beforeLinkScore = line.score;
                    }
                }
                int[] alternatives = options.Where(option => option != move).ToArray();
                if (alternatives.Length != 0)
                    line.forks.Add((line.moves.Select(entry => entry.action).ToArray(), alternatives));
                line.moves.Add((Fingerprint(trial, prompt), move));
                if ((move & 0xffff) is 6 or 7)
                {
                    settledMoves = line.moves.Count;
                    line.complete = true;
                    break;
                }
                trial.SubmitResponse(response);
            }
            if (line.moves.Count > settledMoves) line.moves.RemoveRange(settledMoves, line.moves.Count - settledMoves);
            if (trades.Count == 0 || (trial.IsFinished && trial.Winner == player)) return line;

            // Leaving more small bodies is insufficient. The projected line must recover the lost Link rating
            // or gain an actionable advantage against the actual board before committing the material.
            var end = new LinkPosition(trial, player, canBattle);
            foreach (var trade in trades)
            {
                if (trade.index >= settledMoves) continue;
                bool payoff = end.rank >= trade.rank || end.opposingCards < trade.before.opposingCards ||
                    end.interaction > trade.before.interaction || (end.lethal && !trade.before.lethal);
                if (!line.complete || !payoff || line.score <= trade.score + .08)
                    line.unpaidLinkTrade = Math.Min(line.unpaidLinkTrade, trade.index);
            }
            return line;
        }

        internal static double BoardValue(DuelSession duel, byte player, bool canBattle)
        {
            var own = duel.Players[player];
            var rival = duel.Players[1 - player];
            int opposingMonsters = rival.mzone.Count(card => card != null);
            int opposingSpells = rival.szone.Count(card => card != null);
            bool contract = own.szone.Any(card => card?.IsFaceUp == true && !card.effectsDisabled && DddTurnPlan.IsDarkContract(card.code));
            double score = .18 * own.hand.Count + .18 * own.szone.Count(card => card != null) -
                .75 * opposingMonsters - .43 * opposingSpells + (own.lp - rival.lp) / 4800.0;
            int attack = 0;
            foreach (var monster in own.mzone)
            {
                if (monster?.IsFaceUp != true || CardDatabase.Get(monster.code) is not CardInfo card) continue;
                if (monster.IsAttack) attack += Math.Max(0, monster.attack);
                score += .6 + Math.Clamp(monster.attack, 0, 4200) / 3200.0;
                bool developed = card.IsExtra || (card.type & YgoConstants.TYPE_RITUAL) != 0;
                if (developed) score += .42;
                if (card.IsLink) score += card.level * .35 + (card.level >= 5 ? 1.85 : card.level >= 4 ? .6 : 0);
                if (monster.effectsDisabled) continue;
                if (monster.code == Machinex && (contract || monster.overlayCodes.Count >= 2))
                    score += 1.7 + .26 * Math.Min(3, opposingMonsters);
                else if (monster.code == Siegfried) score += 1.65 + .36 * Math.Min(3, opposingSpells);
                else if (monster.code != Gilgamesh && developed && LinkComboPolicy.IsInteractive(card)) score += .85;
            }
            foreach (var monster in rival.mzone)
                if (monster?.IsFaceUp == true) score -= Math.Max(0, monster.IsDefense ? monster.defense : monster.attack) / 4300.0;
            if (canBattle && opposingMonsters == 0 && attack >= rival.lp) score += 4.5;
            return score;
        }

        private static ulong Fingerprint(DuelSession duel, DuelPrompt prompt)
        {
            ulong hash = 14695981039346656037UL;
            void Mix(ulong item) => hash = unchecked((hash ^ item) * 1099511628211UL);
            void Zone(IEnumerable<LiveCardState> cards, bool own)
            {
                foreach (var card in cards)
                {
                    Mix(card == null ? 0UL : 1UL);
                    if (card == null) continue;
                    Mix(card.position);
                    if (!own && !card.IsFaceUp) continue;
                    Mix(card.code);
                    Mix((uint)card.attack);
                    Mix((uint)card.defense);
                    Mix(card.effectsDisabled ? 1UL : 0);
                    foreach (uint material in card.overlayCodes) Mix(material);
                }
                Mix(0xffffffff);
            }
            void Options(List<SelectableCardItem> choices)
            {
                Mix((uint)choices.Count);
                foreach (var card in choices)
                {
                    Mix(card.code);
                    Mix(card.controller);
                    Mix(card.location);
                    Mix(card.sequence);
                    Mix(card.description);
                }
            }

            Mix((uint)duel.TurnNumber);
            Mix((uint)duel.CurrentPhase);
            Mix(duel.FieldDisabledMask);
            Mix(prompt.playerId);
            Mix(prompt.messageType);
            Mix((prompt.canBp ? 1UL : 0) | (prompt.canEp ? 2UL : 0));
            var own = duel.Players[prompt.playerId];
            var rival = duel.Players[1 - prompt.playerId];
            Mix((uint)own.lp);
            Mix((uint)rival.lp);
            Mix((uint)own.deckCount);
            Mix((uint)rival.deckCount);
            Mix((uint)rival.hand.Count);
            Zone(own.hand, true);
            Zone(own.mzone, true);
            Zone(own.szone, true);
            Zone(own.grave, true);
            Zone(own.extra, true);
            Zone(own.banished, true);
            Zone(rival.mzone, false);
            Zone(rival.szone, false);
            Zone(rival.grave, false);
            Zone(rival.banished, false);
            Options(prompt.summonable);
            Options(prompt.spSummonable);
            Options(prompt.repositionable);
            Options(prompt.mSetable);
            Options(prompt.sSetable);
            Options(prompt.activatable);
            return hash;
        }
    }
}
