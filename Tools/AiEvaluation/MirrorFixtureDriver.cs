using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using YGOPortable.Core;
using static YGOPortable.Editor.AiMatchBenchmark;

namespace YGOPortable.Editor
{
    internal static class MirrorFixtureDriver
    {
        // Reuse the protected duel runner and event-based metrics without changing that source.
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly MethodInfo Play = typeof(AiMatchBenchmark).GetMethod("Play", PrivateStatic) ??
            throw new MissingMethodException(nameof(AiMatchBenchmark), "Play");
        private static readonly FieldInfo Errors = Play.ReturnType.GetField("nativeErrors") ??
            throw new MissingFieldException(Play.ReturnType.Name, "nativeErrors");

        [Serializable]
        private sealed class Fixture
        {
            public int id, deck, seed;
            public string era;
            public uint method;
        }

        public static void Run()
        {
            int exit = 0, nativeErrors = 0;
            try
            {
                string path = Environment.GetEnvironmentVariable("YGO_MIRROR_REPORT");
                string fixtures = Environment.GetEnvironmentVariable("YGO_MIRROR_FIXTURES");
                int worker = int.Parse(Environment.GetEnvironmentVariable("YGO_MIRROR_INDEX"));
                int count = int.Parse(Environment.GetEnvironmentVariable("YGO_MIRROR_WORKERS"));
                string streaming = Application.streamingAssetsPath;
                if (YgoNativeBridge.Initialize(streaming, (kind, message) =>
                {
                    if (kind != 0) return;
                    nativeErrors++;
                    Debug.LogWarning(message);
                }) != 0) throw new InvalidOperationException("Could not initialize the pinned engine.");
                CardDatabase.Load(streaming);
                DeckRepository.Load(streaming, Path.Combine(Path.GetDirectoryName(path), $"benchmark-save-{worker}"), false);
                var decks = DeckRepository.PresetDecks.ToDictionary(deck => deck.id);
                var factory = typeof(AiMatchBenchmark).GetMethod("LoadPilotFactory", PrivateStatic);
                Func<int, Pilot> Resolve(string name) => (Func<int, Pilot>)factory.Invoke(null, new object[] { name });
                var baseline = Resolve("AiBenchmarkBaselineFactory");
                var candidate = Resolve("AiBenchmarkMirrorCandidateFactory");
                bool league = Environment.GetEnvironmentVariable("YGO_MIRROR_LEAGUE") == "1";
                var arms = new List<(string Name, Func<int, Pilot> Tested, Func<int, ulong, Pilot> Opponent)>
                {
                    ("baseline", candidate, (level, _) => baseline(level)),
                };
                if (league)
                {
                    var previous = Resolve("AiBenchmarkPreviousFactory");
                    arms.Add(("previous-baseline", previous, (level, _) => baseline(level)));
                    arms.Add(("random", candidate, (_, seed) => RandomLegalPilot.Create(seed)));
                    arms.Add(("previous-random", previous, (_, seed) => RandomLegalPilot.Create(seed)));
                    arms.Add(("direct", candidate, (level, _) => previous(level)));
                }

                var writers = new List<StreamWriter>();
                try
                {
                    foreach (var arm in arms)
                    {
                        string output = Path.Combine(Path.GetDirectoryName(path), arm.Name, Path.GetFileName(path));
                        Directory.CreateDirectory(Path.GetDirectoryName(output));
                        writers.Add(new StreamWriter(output) { AutoFlush = true });
                    }
                    foreach (var fixture in File.ReadLines(fixtures).Select(JsonUtility.FromJson<Fixture>)
                        .Where((_, ordinal) => ordinal % count == worker))
                    {
                        var deck = decks[fixture.deck];
                        int level = OpponentDeckLevels.GetLevel(deck.charId, fixture.deck);
                        int rotation = fixture.id + fixture.seed;
                        for (int order = 0; order < 2; order++)
                        {
                            int seat = (order + rotation) & 1;
                            // Reuse the random pilot seed across candidate and previous at the same seat.
                            ulong randomSeed = 0xB2A9345F2C186B67UL ^ ((ulong)(uint)fixture.id << 33) ^
                                ((ulong)(uint)fixture.seed << 1) ^ (ulong)(1 - seat);
                            for (int offset = 0; offset < arms.Count; offset++)
                            {
                                int index = (offset + rotation + seat) % arms.Count;
                                var arm = arms[index];
                                int before = nativeErrors;
                                var rival = arm.Opponent(level, randomSeed);
                                var pilot = arm.Tested(level);
                                object result = Play.Invoke(null, new object[] { fixture.id, fixture.seed, seat, fixture.era,
                                    fixture.method, deck, deck, seat == 0 ? pilot : rival, seat == 1 ? pilot : rival, false, true });
                                Errors.SetValue(result, nativeErrors - before);
                                writers[index].WriteLine(JsonUtility.ToJson(result));
                                if (nativeErrors != before)
                                    throw new InvalidOperationException($"Native script error in {arm.Name} {fixture.id}/{fixture.seed}/{seat}.");
                            }
                        }
                    }
                }
                finally
                {
                    foreach (var writer in writers) writer.Dispose();
                }
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                exit = 1;
            }
            finally
            {
                YgoNativeBridge.Shutdown();
                if (Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }
    }
}
