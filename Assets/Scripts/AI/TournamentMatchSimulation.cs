using System;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal sealed class TournamentMatchSimulation : IDisposable
    {
        private readonly DuelSession _duel = new();
        private readonly TacticalAI[] _players = { new(), new() };
        private readonly int _left;
        private readonly int _right;
        private readonly bool _leftFirst;
        private int _steps;
        internal int Winner { get; private set; }
        internal bool Finished => Winner != 0;

        internal TournamentMatchSimulation(int left, int right, int seed, int node, int slots)
        {
            _left = left;
            _right = right;
            var random = new Random(unchecked(seed * 16777619 ^ node));
            _leftFirst = random.Next(2) == 0;
            var first = DimensionTournament.HighestDeck(left);
            var second = DimensionTournament.HighestDeck(right);
            ulong[] seeds = Enumerable.Range(0, 4).Select(_ => (ulong)random.Next()).ToArray();
            var opening = DimensionTournament.Opening(slots, first);
            if (!_duel.StartNewDuel(first.main, first.extra, second.main, second.extra, _leftFirst,
                seed: seeds, opponentOpening: opening, playerOpening: opening))
                Winner = _leftFirst ? _right : _left;
        }

        internal void Step()
        {
            if (Finished) return;
            if (!_duel.IsFinished && _duel.TurnNumber <= 18 && _duel.RetryCount == 0 && _steps++ < 320)
            {
                if (_duel.ActivePrompt is { } prompt)
                    _duel.SubmitResponse(_players[prompt.playerId].ComputeResponse(_duel, prompt));
                else _duel.StepUntilPrompt();
                return;
            }

            int seat = _duel.Winner is 0 or 1 ? _duel.Winner : ResolveTimeLimit();
            Winner = (seat == 0) == _leftFirst ? _left : _right;
        }

        private int ResolveTimeLimit()
        {
            int first = Score(_duel.Players[0]);
            int second = Score(_duel.Players[1]);
            return first > second ? 0 : 1;

            static int Score(PlayerFieldSnapshot player) => player.lp + player.hand.Count * 200 +
                player.mzone.Where(card => card != null).Sum(card => 250 + Math.Min(450, Math.Max(0, card.attack) / 10));
        }

        public void Dispose() => _duel.Dispose();
    }
}
