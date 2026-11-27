using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YGOPortable.AI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static class TestBeyondDestinyLateDuels
    {
        public static void RunHeadless()
        {
            int exit = 1, scriptErrors = 0;
            string root = Path.Combine(Path.GetTempPath(), "beyond-destiny-boss-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var streaming = Application.streamingAssetsPath;
                var campaign = BeyondDestinyCampaign.Load(streaming);
                CardDatabase.Load(streaming);
                Require(YgoNativeBridge.Initialize(streaming, (kind, message) =>
                {
                    if (kind != 0) return;
                    scriptErrors++;
                    Debug.LogError(message);
                }) == 0, "Could not initialize the native engine for the late 5D’s story duels.");
                var general = DuelAiPolicy.LoadIncluded();
                var links = LinkComboPolicy.LoadIncluded();
                foreach (int chapter in new[] { 13, 24, 31, 34, 35 })
                {
                    var episode = campaign.Episodes[chapter - 1];
                    var encounter = episode.scenes.SelectMany(scene => scene.events.Select(cue =>
                        campaign.EncounterAt(scene.id, cue.line)).Prepend(campaign.EncounterAt(scene.id, scene.line)))
                        .Single(match => match?.Playable == true);
                    int objective = BeyondDestinyPlayback.ObjectiveTurn(encounter.resolution);
                    var player = campaign.DeckById(encounter.playerDeckId);
                    var enemy = campaign.DeckById(encounter.opponentDeckId);
                    var opening = GameAppController.BeyondDestinyOpening(encounter);
                    using var duel = new DuelSession();
                    Require(duel.StartNewDuel(player.main, player.extra, enemy.main, enemy.extra,
                        seed: new ulong[] { 17, 31, 55, 87 }, playerOpening: opening.Player, opponentOpening: opening.Opponent,
                        storyGimmick: encounter.Riding ? BeyondDestinyPlayback.RidingRule(encounter) : null),
                        $"Native engine could not open {encounter.id}.");
                    var ai = new[]
                    {
                        new TacticalAI(general, links, searchCombos: false) { Difficulty = AIDifficulty.Master },
                        new TacticalAI(general, links, searchCombos: false) { Difficulty = AIDifficulty.Master },
                    };
                    int initialSpc = duel.Players.Sum(DuelFieldUI.ReadSpeedCounters);
                    int steps = 0;
                    while (!duel.IsFinished && (objective == 0 || duel.TurnNumber < objective))
                    {
                        Require(++steps <= 900 && duel.TurnNumber <= 70, $"Native duel did not finish: {encounter.id}.");
                        if (duel.ActivePrompt == null) duel.StepUntilPrompt();
                        if (duel.IsFinished) break;
                        var prompt = duel.ActivePrompt;
                        Require(prompt != null, $"No legal prompt or result: {encounter.id} (turn {duel.TurnNumber}).");
                        var response = ai[prompt.playerId].ComputeResponse(duel, prompt);
                        Require(response?.Length > 0, $"Empty AI response: {encounter.id}.");
                        duel.SubmitResponse(response);
                        if (encounter.Riding)
                            Require(duel.Players.All(field => field.szone[5]?.code == StoryGimmickRuntime.SpeedWorld &&
                                field.szone[5].IsFaceUp), $"{encounter.id}: a Field Spell or effect displaced Speed World 2.");
                        Require(duel.RetryCount == 0 && scriptErrors == 0,
                            $"Native retry or script error in {encounter.id} on turn {duel.TurnNumber}.");
                    }
                    bool metTurn = objective > 0 && duel.TurnNumber >= objective;
                    bool won = duel.IsFinished && duel.Winner == duel.HumanPlayerIndex;
                    Require(objective > 0 ? metTurn : won, $"The fixed native replay did not clear Chapter {chapter}.");
                    Require(BeyondDestinyPlayback.CanContinue(encounter.resolution, won, duel.IsFinished, metTurn),
                        $"A valid native result did not clear {encounter.id}.");
                    int finalSpc = duel.Players.Sum(DuelFieldUI.ReadSpeedCounters);
                    if (encounter.Riding)
                        Require(finalSpc > initialSpc && duel.Players.All(playerField => DuelFieldUI.ReadSpeedCounters(playerField) <= 12),
                            $"Speed World 2 did not advance or cap physical counters through {encounter.id}: {initialSpc}->{finalSpc}.");
                    Debug.Log($"[Beyond Destiny late duel] Chapter {chapter}; turns {duel.TurnNumber}; " +
                        $"native winner {duel.Winner}; objective {metTurn}; responses {steps}; SPC {initialSpc}->{finalSpc}.");
                    VerifyStoryCheckpoint(campaign, root, encounter);
                }
                Require(scriptErrors == 0, $"Native Lua errors: {scriptErrors}.");
                Debug.Log("[Beyond Destiny] Five late story duel gates verified with native actions and saved continuation.");
                exit = 0;
            }
            catch (Exception error) { Debug.LogException(error); }
            finally
            {
                YgoNativeBridge.Shutdown();
                Directory.Delete(root, recursive: true);
                if (Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }

        private static void VerifyStoryCheckpoint(BeyondDestinyCampaign campaign, string root, BeyondDestinyCampaign.Encounter encounter)
        {
            int chapter = Enumerable.Range(0, campaign.Episodes.Count).Single(i => campaign.Episodes[i].id == encounter.episodeId);
            var episode = campaign.Episodes[chapter];
            int sceneIndex = Array.FindIndex(episode.scenes, scene => scene.id == encounter.sceneId);
            var current = episode.scenes[sceneIndex];
            var progress = new BeyondDestinyProgress
            {
                episodeIndex = chapter,
                sceneIndex = sceneIndex,
                cueIndex = current.line == encounter.line ? 0 : Array.FindIndex(current.events, cue => cue.line == encounter.line),
            };
            progress.Save(root, campaign);
            var playback = new BeyondDestinyPlayback(campaign, root);
            Require(playback.Read().Duel == encounter, "The saved story checkpoint did not resume its native duel.");
            playback.Resolve(encounter);
            var resumed = new BeyondDestinyPlayback(campaign, root);
            Require(resumed.Progress.resolvedEncounters.Contains(encounter.id) && resumed.Read().Duel != encounter,
                "The native outcome left the encounter pending after reloading.");
            if (encounter.resolution == "canonicalOpponentWin")
                Require(episode.scenes.SelectMany(scene => scene.events).Any(cue => cue.kind == "system" &&
                    cue.text.Contains("[승자: 이자요이 아키", StringComparison.Ordinal) && cue.cutsceneKey == "ep31_mask"),
                    "The native objective no longer leads to Aki’s original cinematic victory.");
            if (chapter == 33)
            {
                while (resumed.Episode.number == 34)
                {
                    var segment = resumed.Read();
                    Require(segment.Duel == null, "The cleared Chapter 34 battle was reopened.");
                    resumed.Finish(segment);
                }
                var continuation = new BeyondDestinyPlayback(campaign, root);
                Require(continuation.Episode.number == 35 && continuation.Read().Duel?.playerDeckId == encounter.playerDeckId,
                    "The rescue did not continue from Chapter 34 to the Chapter 35 story deck.");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
