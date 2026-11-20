using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace YGOPortable.Core
{
    // Explicitly attached to one live player duel. Planning copies never receive this object.
    // Response attempts and acknowledgements have matching sequence IDs; only acknowledged
    // accepted responses are suitable as decisions. Missing end rows identify interrupted files.
    internal sealed class LocalDuelReplayRecorder : IDisposable
    {
        private const int MaximumQueuedLines = 512;
        private const long MaximumBytes = 8 * 1024 * 1024;
        private const string TruncatedEnd = "{\"kind\":\"end\",\"status\":\"truncated\",\"complete\":false}";
        private readonly object _gate = new object();
        private readonly Queue<string> _lines = new Queue<string>();
        private readonly HashSet<int> _pending = new HashSet<int>();
        private readonly string _directory, _humanDeckId, _difficulty, _chainMode;
        private readonly int _opponentCharacter, _opponentDeck;
        private AutoResetEvent _wake;
        private Thread _worker;
        private string _partialPath, _finalPath, _terminalLine, _failure;
        private bool _started, _closing, _truncated, _workerFinished, _warningShown;
        private int _sequence, _nativeErrors, _rejectedResponses;
        private long _bytes;
        private EndRow _end;

        [Serializable]
        private sealed class StartRow
        {
            public string kind = "start";
            public int schema = 1;
            public string id, utc, applicationVersion, unityVersion, platform;
            public string protocol = "ygopro-core";
            public ulong duelMode = YgoConstants.DUEL_MODE_MR5;
            public string duelMode64 = YgoConstants.DUEL_MODE_MR5.ToString(CultureInfo.InvariantCulture);
            public int startingLp, startingHand = 5, drawPerTurn = 1, humanSeat, aiSeat;
            public bool humanGoesFirst, localOnly = true;
            public string[] seed;
            public uint[] humanMain, humanExtra, aiMain, aiExtra;
            public string humanDeckId, difficulty, initialChainMode, generalPolicySha256, linkPolicySha256;
            public int opponentCharacter, opponentDeck;
        }

        [Serializable]
        private sealed class ResponseRow
        {
            public string kind = "response";
            public int sequence, turn, phase, player;
            public string actor, origin, hint64, description64, effectDescription64, announceMask64, answer;
            public bool replayViaSession;
            public string[] options64;
            public DuelPrompt prompt;
        }

        [Serializable]
        private sealed class AcknowledgementRow
        {
            public string kind = "response_result";
            public int sequence, retryDelta;
            public bool accepted;
        }

        [Serializable]
        private sealed class EndRow
        {
            public string kind = "end";
            public string status, utc;
            public bool complete, protocolClean;
            public int winner, reason, turns, retries, nativeErrors, pendingResponses, responses, rejectedResponses;
            public int[] finalLife;
        }

        internal LocalDuelReplayRecorder(string persistentDirectory, string humanDeckId, int opponentCharacter,
            int opponentDeck, string difficulty, string chainMode)
        {
            _directory = Path.Combine(persistentDirectory, "PersonalReplays");
            _humanDeckId = humanDeckId;
            _opponentCharacter = opponentCharacter;
            _opponentDeck = opponentDeck;
            _difficulty = difficulty;
            _chainMode = chainMode;
        }

        internal void Begin(ulong[] seed, IReadOnlyList<uint> humanMain, IReadOnlyList<uint> humanExtra,
            IReadOnlyList<uint> aiMain, IReadOnlyList<uint> aiExtra, bool humanFirst, int startingLp)
        {
            try
            {
                if (_started) return;
                string id = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
                _partialPath = Path.Combine(_directory, id + ".partial.jsonl");
                _finalPath = Path.Combine(_directory, id + ".jsonl");
                var header = new StartRow
                {
                    id = id, utc = Utc(), applicationVersion = Application.version, unityVersion = Application.unityVersion,
                    platform = Application.platform.ToString(), startingLp = startingLp,
                    humanSeat = humanFirst ? 0 : 1, aiSeat = humanFirst ? 1 : 0, humanGoesFirst = humanFirst,
                    seed = seed.Take(4).Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray(),
                    humanMain = Copy(humanMain), humanExtra = Copy(humanExtra), aiMain = Copy(aiMain), aiExtra = Copy(aiExtra),
                    humanDeckId = _humanDeckId, opponentCharacter = _opponentCharacter, opponentDeck = _opponentDeck,
                    difficulty = _difficulty, initialChainMode = _chainMode,
                    generalPolicySha256 = PolicyHash("DuelAiPolicy"), linkPolicySha256 = PolicyHash("LinkComboPolicy"),
                };
                _wake = new AutoResetEvent(false);
                _started = true;
                if (!Enqueue(JsonUtility.ToJson(header))) { ReleaseUnstartedWorker(); return; }
                _worker = new Thread(WriteFile) { IsBackground = true, Name = "Local duel replay" };
                _worker.Start();
            }
            catch (Exception exception) { Fail(exception.GetType().Name); ReleaseUnstartedWorker(); }
        }

        internal int Submitted(DuelPrompt prompt, byte[] answer, int turn, int phase, int humanSeat,
            string origin = null, bool replayViaSession = true)
        {
            try
            {
                if (!_started || _end != null || prompt == null || answer == null || answer.Length == 0 || !CanRecord()) return 0;
                int sequence = ++_sequence;
                var row = new ResponseRow
                {
                    sequence = sequence, turn = turn, phase = phase, player = prompt.playerId,
                    actor = prompt.playerId == humanSeat ? "human" : "ai",
                    origin = origin ?? (prompt.playerId == humanSeat ? "player-input" : "ai"),
                    replayViaSession = replayViaSession, prompt = prompt, answer = BitConverter.ToString(answer),
                    hint64 = Number(prompt.SelectionHint), description64 = Number(prompt.promptDesc),
                    effectDescription64 = Number(prompt.promptEffectDesc), announceMask64 = Number(prompt.announceMask),
                    options64 = prompt.options.Select(Number).ToArray(),
                };
                // Serialize now, before the engine replaces this prompt or a caller reuses its buffer.
                if (!Enqueue(JsonUtility.ToJson(row))) return 0;
                _pending.Add(sequence);
                return sequence;
            }
            catch (Exception exception) { Fail(exception.GetType().Name); return 0; }
        }

        internal void Acknowledge(int sequence, bool accepted, int retryDelta)
        {
            try
            {
                if (sequence == 0 || !_pending.Remove(sequence)) return;
                if (!accepted) _rejectedResponses++;
                Enqueue(JsonUtility.ToJson(new AcknowledgementRow
                {
                    sequence = sequence, accepted = accepted, retryDelta = Math.Max(0, retryDelta),
                }));
                FinishIfReady();
            }
            catch (Exception exception) { Fail(exception.GetType().Name); }
        }

        internal void NativeError()
        {
            if (_end == null) _nativeErrors++;
        }

        internal void End(string status, int winner, int reason, int turns, int retries, int[] life, bool force = false)
        {
            try
            {
                if (!_started) return;
                _end ??= new EndRow
                {
                    status = status, utc = Utc(), complete = winner >= 0 && (status == "finished" || status == "surrender"),
                    winner = winner, reason = reason, turns = turns, retries = retries,
                    nativeErrors = _nativeErrors, finalLife = life, responses = _sequence,
                };
                if (force && _pending.Count != 0)
                {
                    _end.pendingResponses = _pending.Count;
                    _end.complete = false;
                    _pending.Clear();
                }
                FinishIfReady();
            }
            catch (Exception exception) { Fail(exception.GetType().Name); }
        }

        private void FinishIfReady()
        {
            if (_end == null || _pending.Count != 0) return;
            _end.nativeErrors = _nativeErrors;
            _end.rejectedResponses = _rejectedResponses;
            _end.protocolClean = _end.retries == 0 && _end.nativeErrors == 0 && _end.pendingResponses == 0 && _rejectedResponses == 0;
            lock (_gate)
            {
                if (_closing) return;
                _terminalLine = JsonUtility.ToJson(_end);
                _closing = true;
                _wake?.Set();
            }
        }

        private bool CanRecord()
        {
            lock (_gate) return !_closing && _failure == null;
        }

        private bool Enqueue(string line)
        {
            lock (_gate)
            {
                if (_closing || _failure != null) return false;
                long bytes = Encoding.UTF8.GetByteCount(line) + 1L;
                if (_lines.Count >= MaximumQueuedLines || _bytes + bytes > MaximumBytes)
                {
                    _failure = _lines.Count >= MaximumQueuedLines ? "queue_limit" : "size_limit";
                    _truncated = _closing = true;
                    _terminalLine = TruncatedEnd;
                    _wake?.Set();
                    return false;
                }
                _lines.Enqueue(line);
                _bytes += bytes;
                _wake?.Set();
                return true;
            }
        }

        private void WriteFile()
        {
            try
            {
                Directory.CreateDirectory(_directory);
                using (var stream = new FileStream(_partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
                {
                    while (true)
                    {
                        string line = null;
                        lock (_gate)
                        {
                            if (_lines.Count != 0) line = _lines.Dequeue();
                            else if (_closing) break;
                        }
                        if (line != null) writer.WriteLine(line);
                        else _wake.WaitOne(200);
                    }
                    if (_terminalLine != null) writer.WriteLine(_terminalLine);
                }
                // Partial files survive crashes, I/O errors and truncation. Never overwrite an older record.
                if (!_truncated && _terminalLine != null) File.Move(_partialPath, _finalPath);
            }
            catch (Exception exception) { Fail(exception.GetType().Name); }
            finally
            {
                lock (_gate)
                {
                    _workerFinished = true;
                    _lines.Clear();
                    _wake?.Dispose();
                    _wake = null;
                }
            }
        }

        private void Fail(string failure)
        {
            lock (_gate)
            {
                _failure ??= failure;
                _truncated = _closing = true;
                _terminalLine = TruncatedEnd;
                if (!_workerFinished) _wake?.Set();
            }
        }

        private void ReleaseUnstartedWorker()
        {
            lock (_gate)
            {
                if (_worker != null && (_worker.ThreadState & ThreadState.Unstarted) == 0) return;
                _workerFinished = true;
                _lines.Clear();
                _wake?.Dispose();
                _wake = null;
            }
        }

        // Polled on the Unity main thread. Disk errors must never escape into duel processing.
        internal void PollWarning()
        {
            string failure;
            lock (_gate)
            {
                if (_warningShown || _failure == null) return;
                _warningShown = true;
                failure = _failure;
            }
            Debug.LogWarning($"[Personal replay] Local recording stopped ({failure}). The duel can continue.");
        }

        public void Dispose()
        {
            if (!_started) return;
            End("interrupted", -1, 0, 0, 0, Array.Empty<int>(), force: true);
            var worker = _worker;
            try
            {
                if (worker != null && worker != Thread.CurrentThread && worker.IsAlive) worker.Join(250);
            }
            catch (Exception exception) { Fail(exception.GetType().Name); }
            // Do not block play or shutdown on a stalled disk. A missing footer remains a partial file.
        }

        private static uint[] Copy(IReadOnlyList<uint> values) => values?.ToArray() ?? Array.Empty<uint>();
        private static string Number(ulong value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Utc() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        private static string PolicyHash(string name)
        {
            var policy = Resources.Load<TextAsset>(name);
            if (policy == null) return "";
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(policy.text))).Replace("-", "").ToLowerInvariant();
        }
    }
}
