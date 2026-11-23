using System;
using System.Collections.Generic;
using System.Linq;

namespace YGOPortable.Core
{
    public static partial class CollectionProgress
    {
        internal static DimensionTournament Tournament => _profile?.tournament;
        internal static int TournamentWins => _profile?.tournamentWins ?? 0;
        internal static int FreeSecretBundles => _profile?.shop?.freeSecretBundles ?? 0;

        internal static bool TryStartTournament(int size, out string error)
        {
            if (!CanTransact(out error)) return false;
            if (!HasStarter || !DimensionTournament.Unlocked || size is not (8 or 16) || Tournament?.Active == true)
            {
                error = "시리즈 완주 후 참가할 수 있습니다. 진행 중인 대진표를 확인하세요.";
                return false;
            }
            var next = WithBalance(Balance);
            try { next.tournament = DimensionTournament.Create(size, PackRandom.Next()); }
            catch (InvalidOperationException)
            {
                error = "참가 가능한 전설의 듀얼리스트를 불러오지 못했습니다.";
                return false;
            }
            return Write(next, _owned, out error);
        }

        internal static bool TrySaveTournamentAi(int seed, int round, IReadOnlyDictionary<int, int> results, out string error)
        {
            if (!CanTransact(out error)) return false;
            var run = Tournament;
            var pending = run?.PendingAiMatches().ToArray();
            if (run == null || !run.Active || run.seed != seed || run.round != round ||
                pending.Length != results.Count || pending.Any(node => !results.TryGetValue(node, out int winner) ||
                    (winner != run.bracket[node * 2] && winner != run.bracket[node * 2 + 1])))
            {
                error = "대진표가 변경되었습니다.";
                return false;
            }
            var next = WithBalance(Balance);
            next.tournament = run.Copy();
            foreach (var (node, winner) in results) next.tournament.bracket[node] = winner;
            return Write(next, _owned, out error);
        }

        internal static bool TryFinishTournamentRound(int seed, int round, int opponent, bool won,
            out (int Dp, int Dust, bool Champion) prize, out string error)
        {
            prize = default;
            if (!CanTransact(out error)) return false;
            var run = Tournament;
            if (run == null || !run.Active || run.seed != seed || run.round != round || run.Opponent != opponent ||
                opponent <= 0 || run.PendingAiMatches().Any())
            {
                error = "이 경기의 대진 정보를 확인할 수 없습니다.";
                return false;
            }
            bool champion = won && run.PlayerMatch == 1;
            int baseDp = champion ? run.size == 16 ? 5000 : 3000 : won ? WinReward : 0;
            int dp = Math.Min(int.MaxValue - Balance, baseDp);
            int dust = champion ? run.size == 16 ? 3000 : 2000 : 0;
            var next = WithBalance(Balance + dp);
            next.tournament = run.Copy();
            next.tournament.bracket[run.PlayerMatch] = won ? DimensionTournament.Player : opponent;
            if (won) next.tournament.round++;
            else next.tournament.lost = true;
            if (champion)
            {
                next.dust += dust;
                if (next.tournamentWins < int.MaxValue) next.tournamentWins++;
                next.shop = next.shop.Copy();
                if (next.shop.freeSecretBundles < int.MaxValue) next.shop.freeSecretBundles++;
            }
            if (!Write(next, _owned, out error)) return false;
            prize = (dp, dust, champion);
            return true;
        }
    }
}
