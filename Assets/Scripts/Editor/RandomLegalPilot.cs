using System;
using System.Collections.Generic;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.Editor
{
    // Evaluation only. The sampling law and PRNG are versioned independently of the engine seed.
    // Menus sample offered actions; constrained subsets use randomized, memoized feasible search.
    // This is not a uniform distribution over every complete material combination.
    internal sealed class RandomLegalPilot
    {
        internal const string Version = "RandomLegal-v1";
        private const int MaximumCandidates = 1024;
        private const int MaximumSearchStates = 100000;
        private const int MaximumDeclarationCapacity = 262144;
        private ulong _state;

        private RandomLegalPilot(ulong seed) => _state = seed;

        internal static AiMatchBenchmark.Pilot Create(ulong seed)
        {
            var pilot = new RandomLegalPilot(seed);
            return new AiMatchBenchmark.Pilot((_, prompt) => pilot.Choose(prompt), () => 0, () => 0);
        }

        private byte[] Choose(DuelPrompt prompt)
        {
            if (prompt == null) throw new ArgumentNullException(nameof(prompt));
            switch (prompt.messageType)
            {
                case YgoConstants.MSG_SELECT_IDLECMD:
                    return ChooseIdle(prompt);
                case YgoConstants.MSG_SELECT_BATTLECMD:
                    return ChooseBattle(prompt);
                case YgoConstants.MSG_SELECT_CHAIN:
                {
                    CheckCount(prompt.candidates.Count);
                    int count = prompt.candidates.Count + (prompt.forced ? 0 : 1);
                    int choice = Next(count);
                    return Int32(choice == prompt.candidates.Count ? -1 : choice);
                }
                case YgoConstants.MSG_SELECT_EFFECTYN:
                case YgoConstants.MSG_SELECT_YESNO:
                    return Int32(Next(2));
                case YgoConstants.MSG_SELECT_OPTION:
                case YgoConstants.MSG_ANNOUNCE_NUMBER:
                    CheckCount(prompt.options.Count);
                    return Int32(Next(prompt.options.Count));
                case YgoConstants.MSG_SELECT_CARD:
                    return ChooseCards(prompt);
                case YgoConstants.MSG_SELECT_UNSELECT_CARD:
                {
                    int count = checked(prompt.candidates.Count + prompt.unselectCandidates.Count);
                    CheckCount(count);
                    bool canFinish = prompt.finishable || prompt.cancelable;
                    int choice = Next(count + (canFinish ? 1 : 0));
                    return choice == count ? Int32(-1) : TwoInts(1, choice);
                }
                case YgoConstants.MSG_SELECT_TRIBUTE:
                    return ChooseTribute(prompt);
                case YgoConstants.MSG_SELECT_SUM:
                    return ChooseSum(prompt);
                case YgoConstants.MSG_SELECT_PLACE:
                case YgoConstants.MSG_SELECT_DISFIELD:
                    return ChoosePlaces(prompt);
                case YgoConstants.MSG_SELECT_POSITION:
                {
                    var positions = new[] { 1, 2, 4, 8 }.Where(bit => (prompt.allowedPositions & bit) != 0).ToArray();
                    return Int32(positions[Next(positions.Length)]);
                }
                case YgoConstants.MSG_SELECT_COUNTER:
                    return ChooseCounters(prompt);
                case YgoConstants.MSG_SORT_CARD:
                case YgoConstants.MSG_SORT_CHAIN:
                    return ChooseSort(prompt);
                case YgoConstants.MSG_ANNOUNCE_RACE:
                    return UInt64(ChooseMask(prompt.announceMask, Count(prompt.min)));
                case YgoConstants.MSG_ANNOUNCE_ATTRIB:
                    return Int32(unchecked((int)ChooseMask(prompt.announceMask & uint.MaxValue, Count(prompt.min))));
                case YgoConstants.MSG_ANNOUNCE_CARD:
                    return ChooseDeclaration(prompt);
                case YgoConstants.MSG_ROCK_PAPER_SCISSORS:
                    return Int32(1 + Next(3));
                default:
                    throw new NotSupportedException($"[{Version}] Unsupported prompt {prompt.messageType} for player {prompt.playerId}.");
            }
        }

        private byte[] ChooseIdle(DuelPrompt prompt)
        {
            var actions = new List<int>();
            Add(actions, prompt.summonable.Count, 0);
            Add(actions, prompt.spSummonable.Count, 1);
            Add(actions, prompt.repositionable.Count, 2);
            Add(actions, prompt.mSetable.Count, 3);
            Add(actions, prompt.sSetable.Count, 4);
            Add(actions, prompt.activatable.Count, 5);
            if (prompt.canBp) actions.Add(6);
            if (prompt.canEp) actions.Add(7);
            if (prompt.canShuffle) actions.Add(8);
            return Int32(actions[Next(actions.Count)]);
        }

        private byte[] ChooseBattle(DuelPrompt prompt)
        {
            var actions = new List<int>();
            Add(actions, prompt.activatable.Count, 0);
            // The index is the native attackable-list ordinal, not the monster-zone sequence.
            Add(actions, prompt.attackable.Count, 1);
            if (prompt.canM2) actions.Add(2);
            if (prompt.canEp) actions.Add(3);
            return Int32(actions[Next(actions.Count)]);
        }

        private static void Add(List<int> actions, int count, int command)
        {
            CheckCount(count);
            for (int index = 0; index < count; index++) actions.Add((index << 16) | command);
        }

        private byte[] ChooseCards(DuelPrompt prompt)
        {
            CheckCount(prompt.candidates.Count);
            int minimum = Count(prompt.min), maximum = Count(prompt.max);
            Require(minimum <= maximum && maximum <= prompt.candidates.Count, "Inconsistent card-count bounds.");
            int range = maximum - minimum + 1;
            int choice = Next(range + (prompt.cancelable ? 1 : 0));
            if (choice == range) return Int32(-1);
            var indices = Enumerable.Range(0, prompt.candidates.Count).ToArray();
            Shuffle(indices);
            return Pack(indices.Take(minimum + choice));
        }

        private byte[] ChooseTribute(DuelPrompt prompt)
        {
            CheckCount(prompt.candidates.Count);
            if (prompt.cancelable && Next(2) == 0) return Int32(-1);
            int required = Count(prompt.min), maximum = Math.Min(Count(prompt.max), prompt.candidates.Count);
            var order = Enumerable.Range(0, prompt.candidates.Count).ToArray();
            Shuffle(order);
            var chosen = new List<int>();
            var failed = new HashSet<(int Position, int Count, int Credit)>();
            var budget = new SearchBudget("tribute");
            bool Search(int position, int credit)
            {
                if (credit >= required) return true;
                if (position >= order.Length || chosen.Count >= maximum) return false;
                var key = (position, chosen.Count, credit);
                if (failed.Contains(key)) return false;
                budget.Visit();
                bool includeFirst = Next(2) == 0;
                for (int branch = 0; branch < 2; branch++)
                {
                    bool include = (branch == 0) == includeFirst;
                    if (include) chosen.Add(order[position]);
                    // releaseParam is a credit, including zero; it is not a card-count minimum.
                    int next = include ? Math.Min(required, credit + prompt.candidates[order[position]].releaseParam) : credit;
                    if (Search(position + 1, next)) return true;
                    if (include) chosen.RemoveAt(chosen.Count - 1);
                }
                failed.Add(key);
                return false;
            }
            Require(Search(0, 0), "No legal tribute subset found.");
            return Pack(chosen);
        }

        private readonly struct Amount
        {
            internal readonly int First, Alternative;
            internal Amount(uint parameter)
            {
                First = (int)(parameter & 0xffff);
                Alternative = unchecked((int)parameter) >> 16;
            }
            internal int Minimum => Alternative > 0 ? Math.Min(First, Alternative) : First;
            internal int Maximum => Math.Max(First, Alternative);
            internal int[] Positive => Alternative > 0 && Alternative != First
                ? First > 0 ? new[] { First, Alternative } : new[] { Alternative }
                : First > 0 ? new[] { First } : Array.Empty<int>();
        }

        private byte[] ChooseSum(DuelPrompt prompt)
        {
            CheckCount(prompt.candidates.Count);
            CheckCount(prompt.mustSelect.Count);
            int target = (int)(prompt.acc & 0xffff);
            var order = Enumerable.Range(0, prompt.candidates.Count).ToArray();
            Shuffle(order);
            var candidates = order.Select(index => new Amount(prompt.candidates[index].sumParam)).ToArray();
            var mandatory = prompt.mustSelect.Select(item => new Amount(item.sumParam)).ToArray();
            var chosen = new List<int>();
            var budget = new SearchBudget("sum");
            if (prompt.sumExactCountZeroAny)
                ChooseOverflowSum(target, order, candidates, mandatory, chosen, budget);
            else
                ChooseExactSum(prompt, target, order, candidates, mandatory, chosen, budget);
            // The mandatory cards are already selected by the core: only optional indices go on the wire.
            return Pack(chosen);
        }

        private void ChooseExactSum(DuelPrompt prompt, int target, int[] order, Amount[] candidates,
            Amount[] mandatory, List<int> chosen, SearchBudget budget)
        {
            int minimum = Count(prompt.min), maximum = Math.Min(Count(prompt.max), candidates.Length);
            Require(minimum <= maximum, "Inconsistent exact-sum count bounds.");
            var remaining = new HashSet<int> { target };
            foreach (var item in mandatory)
            {
                var values = item.Positive;
                // The core reorders selected pointers before checking the sum. Positive contributions
                // give order-independent legality; zero-only mandatory items cannot prove that here.
                Require(values.Length != 0, "Zero-only mandatory exact-sum material is unsupported.");
                var next = new HashSet<int>();
                foreach (int rest in remaining)
                    foreach (int value in values)
                    {
                        budget.Visit();
                        if (rest >= value) next.Add(rest - value);
                    }
                remaining = next;
            }
            var remainders = remaining.OrderBy(value => value).ToArray();
            Shuffle(remainders);
            var failed = new HashSet<(int Position, int Remaining, int Count)>();
            bool Search(int position, int rest)
            {
                if (rest == 0)
                    return chosen.Count >= minimum && chosen.Count <= maximum && (chosen.Count != 0 || mandatory.Length != 0);
                if (rest < 0 || position >= candidates.Length || chosen.Count >= maximum ||
                    chosen.Count + candidates.Length - position < minimum) return false;
                var key = (position, rest, chosen.Count);
                if (failed.Contains(key)) return false;
                budget.Visit();
                // Branch zero skips; positive branches pick one of that card's alternate contributions.
                var values = candidates[position].Positive;
                var branches = Enumerable.Range(0, values.Length + 1).ToArray();
                Shuffle(branches);
                foreach (int branch in branches)
                {
                    if (branch != 0) chosen.Add(order[position]);
                    int next = branch == 0 ? rest : rest - values[branch - 1];
                    if (Search(position + 1, next)) return true;
                    if (branch != 0) chosen.RemoveAt(chosen.Count - 1);
                }
                failed.Add(key);
                return false;
            }
            foreach (int rest in remainders)
                if (Search(0, rest)) return;
            throw new InvalidOperationException($"[{Version}] No positive, order-independent exact-sum subset found.");
        }

        private void ChooseOverflowSum(int target, int[] order, Amount[] candidates, Amount[] mandatory,
            List<int> chosen, SearchBudget budget)
        {
            int minimumTotal = 0, maximumTotal = 0, smallest = int.MaxValue;
            foreach (var item in mandatory)
            {
                minimumTotal = checked(minimumTotal + item.Minimum);
                maximumTotal = Math.Min(target, checked(maximumTotal + item.Maximum));
                smallest = Math.Min(smallest, item.Minimum);
            }
            var suffixMaximum = new int[candidates.Length + 1];
            for (int i = candidates.Length - 1; i >= 0; i--)
                suffixMaximum[i] = Math.Min(target, checked(suffixMaximum[i + 1] + candidates[i].Maximum));
            var failed = new HashSet<(int Position, int Minimum, int Maximum, int Smallest)>();
            bool Search(int position, int low, int high, int least)
            {
                // The core's overflow mode ignores the displayed min/max optional-card counts.
                if (high >= target && (long)low - least < target) return true;
                if (position >= candidates.Length || (long)low - least >= target ||
                    high + suffixMaximum[position] < target) return false;
                var key = (position, low, high, least);
                if (failed.Contains(key)) return false;
                budget.Visit();
                bool includeFirst = Next(2) == 0;
                for (int branch = 0; branch < 2; branch++)
                {
                    bool include = (branch == 0) == includeFirst;
                    if (include) chosen.Add(order[position]);
                    if (Search(position + 1,
                        include ? checked(low + candidates[position].Minimum) : low,
                        include ? Math.Min(target, checked(high + candidates[position].Maximum)) : high,
                        include ? Math.Min(least, candidates[position].Minimum) : least)) return true;
                    if (include) chosen.RemoveAt(chosen.Count - 1);
                }
                failed.Add(key);
                return false;
            }
            Require(Search(0, minimumTotal, maximumTotal, smallest), "No legal overflow-sum subset found.");
        }

        private byte[] ChoosePlaces(DuelPrompt prompt)
        {
            int needed = Count(prompt.min);
            Require(prompt.playerId < 2 && needed > 0, "Invalid place prompt.");
            var places = new List<(byte Player, byte Location, byte Sequence)>();
            uint available = ~prompt.placeFlag;
            for (int side = 0; side < 2; side++)
                for (int zone = 0; zone < 2; zone++)
                    for (int sequence = 0; sequence < (zone == 0 ? 7 : 8); sequence++)
                        if ((available & (1u << (side * 16 + zone * 8 + sequence))) != 0)
                            places.Add(((byte)(side == 0 ? prompt.playerId : 1 - prompt.playerId),
                                (byte)(zone == 0 ? YgoConstants.LOCATION_MZONE : YgoConstants.LOCATION_SZONE), (byte)sequence));
            Require(needed <= places.Count, "Not enough distinct permitted zones.");
            var choices = places.ToArray();
            Shuffle(choices);
            var response = new byte[needed * 3];
            for (int index = 0; index < needed; index++)
            {
                response[index * 3] = choices[index].Player;
                response[index * 3 + 1] = choices[index].Location;
                response[index * 3 + 2] = choices[index].Sequence;
            }
            return response;
        }

        private byte[] ChooseCounters(DuelPrompt prompt)
        {
            int count = prompt.candidates.Count, remaining = Count(prompt.min);
            CheckCount(count);
            Require(count > 0 && remaining <= ushort.MaxValue, "Invalid counter prompt.");
            var order = Enumerable.Range(0, count).ToArray();
            Shuffle(order);
            var capacity = prompt.candidates.Select(item => item.currentCounter).ToArray();
            Require(capacity.All(value => value <= ushort.MaxValue), "Counter capacity exceeds the wire format.");
            long future = capacity.Sum(value => (long)value);
            Require(future >= remaining, "Not enough removable counters.");
            var response = new byte[count * 2];
            foreach (int index in order)
            {
                future -= capacity[index];
                int lower = (int)Math.Max(0, remaining - future);
                int upper = Math.Min(remaining, (int)capacity[index]);
                int take = lower + Next(upper - lower + 1);
                response[index * 2] = (byte)(take & 0xff);
                response[index * 2 + 1] = (byte)(take >> 8);
                remaining -= take;
            }
            Require(remaining == 0, "Counter allocation did not reach its target.");
            return response;
        }

        private byte[] ChooseSort(DuelPrompt prompt)
        {
            int count = prompt.candidates.Count;
            Require(count <= sbyte.MaxValue + 1, "Sort exceeds the core's signed-byte permutation range.");
            if (count == 0) return new byte[] { 255 };
            var order = Enumerable.Range(0, count).ToArray();
            Shuffle(order);
            var response = new byte[count];
            for (int rank = 0; rank < count; rank++) response[order[rank]] = (byte)rank;
            return response;
        }

        private ulong ChooseMask(ulong available, int needed)
        {
            var bits = Enumerable.Range(0, 64).Where(bit => (available & (1UL << bit)) != 0).ToArray();
            Require(needed <= bits.Length, "Not enough permitted declaration bits.");
            Shuffle(bits);
            ulong result = 0;
            for (int i = 0; i < needed; i++) result |= 1UL << bits[i];
            return result;
        }

        private byte[] ChooseDeclaration(DuelPrompt prompt)
        {
            Require(prompt.options.Count > 0, "Empty declarable-card expression.");
            var opcodes = prompt.options.ToArray();
            for (int capacity = 1024; capacity <= MaximumDeclarationCapacity; capacity *= 2)
            {
                var codes = YgoNativeBridge.FilterDeclarableCards(opcodes, capacity);
                if (codes.Length == capacity) continue;
                Require(codes.Length > 0, "The native declaration filter returned no legal card.");
                Array.Sort(codes);
                return Int32(unchecked((int)codes[Next(codes.Length)]));
            }
            throw new NotSupportedException($"[{Version}] Declarable-card pool exceeds its bounded capacity.");
        }

        private sealed class SearchBudget
        {
            private int _remaining = MaximumSearchStates;
            private readonly string _kind;
            internal SearchBudget(string kind) => _kind = kind;
            internal void Visit()
            {
                if (--_remaining < 0)
                    throw new InvalidOperationException($"[{Version}] {_kind} constraint search exceeded {MaximumSearchStates} states.");
            }
        }

        private int Next(int bound)
        {
            Require(bound > 0, "No legal offered choice.");
            ulong range = (ulong)bound;
            ulong threshold = unchecked(0UL - range) % range;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                ulong value = Next64();
                if (value >= threshold) return (int)(value % range);
            }
            throw new InvalidOperationException($"[{Version}] Bounded random sampling did not produce a value.");
        }

        private ulong Next64()
        {
            unchecked
            {
                ulong value = _state += 0x9E3779B97F4A7C15UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }

        private void Shuffle<T>(T[] items)
        {
            for (int i = items.Length - 1; i > 0; i--)
            {
                int other = Next(i + 1);
                (items[i], items[other]) = (items[other], items[i]);
            }
        }

        private static int Count(uint value)
        {
            Require(value <= int.MaxValue, "Count exceeds the supported range.");
            return (int)value;
        }

        private static void CheckCount(int value) => Require(value >= 0 && value <= MaximumCandidates, "Candidate list exceeds its bounded capacity.");
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException($"[{Version}] {message}");
        }

        private static byte[] Pack(IEnumerable<int> indices) => DuelSession.PackCardSelection(indices.OrderBy(index => index).ToArray());
        private static byte[] Int32(int value)
        {
            unchecked { return new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) }; }
        }
        private static byte[] TwoInts(int first, int second) => Int32(first).Concat(Int32(second)).ToArray();
        private static byte[] UInt64(ulong value) => Enumerable.Range(0, 8).Select(index => unchecked((byte)(value >> (index * 8)))).ToArray();
    }
}
