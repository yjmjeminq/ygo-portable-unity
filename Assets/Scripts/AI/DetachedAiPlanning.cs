using System;
using System.Diagnostics;
using System.Threading.Tasks;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal sealed class DetachedAiPlanning
    {
        // Allow for the next 60 Hz poll and a frame of scheduling variation within the one-second response budget.
        private const int ResponseDeadlineMilliseconds = ComboSearch.MasterPlanningMilliseconds - 2 * 17;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Task<(TacticalAI Ai, DuelPrompt Replica, string Error)> _work;
        private volatile bool _cancelled;
        internal DuelPrompt Prompt { get; }

        internal DetachedAiPlanning(TacticalAI advisor, DuelSession session, DuelPrompt prompt)
        {
            Prompt = prompt;
            if (!ReferenceEquals(session.ActivePrompt, prompt)) return;
            var restore = session.CapturePlanningReplay();
            if (restore == null) return;
            var detached = advisor.ForkForDetachedPlanning();
            ulong fingerprint = ComboSearch.Fingerprint(session, prompt);
            _work = Task.Run(() => Prepare(restore, detached, fingerprint));
        }

        internal void Cancel() => _cancelled = true;

        internal bool TryTake(DuelPrompt current, out TacticalAI prepared, out string error)
        {
            prepared = null;
            error = null;
            if (_cancelled || !ReferenceEquals(Prompt, current) || _work == null)
            {
                Cancel();
                return true;
            }
            if (!_work.IsCompleted)
            {
                if (_clock.ElapsedMilliseconds < ResponseDeadlineMilliseconds) return false;
                Cancel();
                return true;
            }
            var result = _work.GetAwaiter().GetResult();
            error = result.Error;
            prepared = result.Ai;
            prepared?.RebindPreparedPrompt(result.Replica, current);
            return true;
        }

        private (TacticalAI Ai, DuelPrompt Replica, string Error) Prepare(Func<Func<bool>, DuelSession> restore,
            TacticalAI advisor, ulong fingerprint)
        {
            string error = null;
            int Remaining() => (int)Math.Max(0, ResponseDeadlineMilliseconds - _clock.ElapsedMilliseconds);
            bool Expired() => _cancelled || Remaining() == 0 || error != null;
            try
            {
                var replica = YgoNativeBridge.RunIsolatedPlanning(() =>
                {
                    if (Expired()) return null;
                    using var session = restore(Expired);
                    if (Expired() || session?.ActivePrompt is not { } prompt ||
                        ComboSearch.Fingerprint(session, prompt) != fingerprint) return null;

                    try
                    {
                        int budget = Math.Max(0, Remaining() - 2 * ComboSearch.PlanningSliceMilliseconds);
                        while (!Expired())
                            if (advisor.AdvanceComboPlanning(session, prompt, budget))
                                return Expired() ? null : prompt;
                        return null;
                    }
                    finally { advisor.ReleaseDetachedIterator(); }
                }, (kind, message) => { if (kind == 0) error ??= message; });
                return (replica == null ? null : advisor, replica, error);
            }
            catch (Exception exception)
            {
                return (null, null, exception.Message);
            }
        }
    }
}
