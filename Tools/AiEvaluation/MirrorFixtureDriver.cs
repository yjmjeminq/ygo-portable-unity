using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using YGOPortable.AI;
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
                var baseline = (Func<int, Pilot>)typeof(AiMatchBenchmark).GetMethod("LoadPilotFactory", PrivateStatic)
                    .Invoke(null, new object[] { "AiBenchmarkBaselineFactory" });
                var general = DuelAiPolicy.LoadIncluded() ?? throw new InvalidOperationException("Missing AI policy.");
                var links = LinkComboPolicy.LoadIncluded() ?? throw new InvalidOperationException("Missing Link policy.");
                Pilot Candidate()
                {
                    var ai = new TacticalAI(general, links, searchCombos: true);
                    return TimedPilot(ai.ComputeResponse, ai.AdvanceComboPlanning,
                        () => ai.SimulatedComboBranches, () => ai.ChosenComboAlternatives);
                }
                using var output = new StreamWriter(path) { AutoFlush = true };
                foreach (var fixture in File.ReadLines(fixtures).Select(JsonUtility.FromJson<Fixture>)
                    .Where((_, ordinal) => ordinal % count == worker))
                {
                    var deck = decks[fixture.deck];
                    int level = OpponentDeckLevels.GetLevel(deck.charId, fixture.deck);
                    // Keep both seats in one process and alternate their execution order.
                    for (int order = 0; order < 2; order++)
                    {
                        int seat = (order + fixture.id + fixture.seed) & 1;
                        int before = nativeErrors;
                        var rival = baseline(level);
                        var pilot = Candidate();
                        object result = Play.Invoke(null, new object[] { fixture.id, fixture.seed, seat, fixture.era,
                            fixture.method, deck, deck, seat == 0 ? pilot : rival, seat == 1 ? pilot : rival, false, true });
                        Errors.SetValue(result, nativeErrors - before);
                        output.WriteLine(JsonUtility.ToJson(result));
                        if (nativeErrors != before)
                            throw new InvalidOperationException($"Native script error in mirror {fixture.id}/{fixture.seed}/{seat}.");
                    }
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
