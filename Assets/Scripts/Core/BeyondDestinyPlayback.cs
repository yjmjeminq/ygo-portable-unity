using System;
using System.Collections.Generic;
using System.IO;

namespace YGOPortable.Core
{
    internal sealed class BeyondDestinyPlayback
    {
        internal static int OpeningSpeed(BeyondDestinyCampaign.Encounter encounter) => encounter.episodeId switch
        {
            "beyond_destiny_ep_29" when encounter.sceneId.EndsWith("_04", StringComparison.Ordinal) => 6,
            "beyond_destiny_ep_29" or "beyond_destiny_ep_31" => 4,
            "beyond_destiny_ep_30" => 6,
            _ => 1,
        };

        internal static string RidingRule(BeyondDestinyCampaign.Encounter encounter) =>
            "riding_beyond_destiny_" + OpeningSpeed(encounter);

        internal readonly struct Page
        {
            internal readonly int Index;
            internal readonly BeyondDestinyCampaign.Cue Text;
            internal readonly string Map, Cutscene;
            internal readonly BeyondDestinyVisual Visual;
            internal readonly BeyondDestinyCampaign.Cue[] Audio;

            internal Page(int index, BeyondDestinyCampaign.Cue text, string map,
                BeyondDestinyVisual visual, BeyondDestinyCampaign.Cue[] audio)
            {
                Index = index;
                Text = text;
                Map = map;
                Cutscene = text.cutsceneKey;
                Visual = visual;
                Audio = audio;
            }
        }

        internal readonly struct Segment
        {
            internal readonly Page[] Pages;
            internal readonly BeyondDestinyCampaign.Encounter Duel;
            internal readonly int EndIndex;
            internal readonly BeyondDestinyCampaign.Cue[] TailAudio;
            internal readonly BeyondDestinySoundscape InitialAudio;
            internal readonly BeyondDestinyVisual TailVisual;

            internal Segment(Page[] pages, BeyondDestinyCampaign.Encounter duel, int endIndex,
                BeyondDestinyCampaign.Cue[] tailAudio, BeyondDestinySoundscape initialAudio, BeyondDestinyVisual tailVisual)
            {
                InitialAudio = initialAudio;
                Pages = pages;
                Duel = duel;
                EndIndex = endIndex;
                TailAudio = tailAudio;
                TailVisual = tailVisual;
            }
        }

        private readonly BeyondDestinyCampaign _campaign;
        private readonly string _saveDirectory;
        private readonly bool _replay;
        internal BeyondDestinyProgress Progress { get; }
        internal BeyondDestinyCampaign.Episode Episode => _campaign.Episodes[Progress.episodeIndex];
        internal BeyondDestinyCampaign.Scene Scene => Progress.Scene(_campaign);
        internal BeyondDestinyCampaign.Deck Deck(int id) => _campaign.DeckById(id);

        internal BeyondDestinyPlayback(BeyondDestinyCampaign campaign, string saveDirectory, bool replay = false)
        {
            _campaign = campaign;
            _saveDirectory = saveDirectory;
            _replay = replay;
            Progress = BeyondDestinyProgress.Load(saveDirectory, campaign, replay);
        }

        internal bool ChangesLocationOrChapter(Segment segment)
        {
            if (segment.Duel != null) return false;
            int next = Progress.sceneIndex + 1;
            return next >= Episode.scenes.Length || !string.Equals(Scene.backgroundPath,
                Episode.scenes[next].backgroundPath, StringComparison.Ordinal);
        }

        internal string FieldGuest()
        {
            foreach (var cue in Scene.events)
            {
                if (HasFieldPose(cue.speakerKey)) return cue.speakerKey;
                foreach (var member in cue.cast ?? Array.Empty<BeyondDestinyCampaign.Standing>())
                    if (HasFieldPose(member.speakerKey)) return member.speakerKey;
            }
            return null;
        }

        private static bool HasFieldPose(string actor) => actor is "aki" or "sherry" or "devine" or "yusei" or
            "jack" or "crow" or "mizoguchi" or "misty" or "rex" or "zone" or "ghost_a";

        internal Segment Read()
        {
            var scene = Scene;
            var pages = new List<Page>();
            var audio = new List<BeyondDestinyCampaign.Cue>();
            BeyondDestinySoundscape initialAudio = default;
            for (int chapter = 0; chapter <= Progress.episodeIndex; ++chapter)
            {
                var previous = _campaign.Episodes[chapter];
                int sceneCount = chapter == Progress.episodeIndex ? Progress.sceneIndex : previous.scenes.Length;
                for (int prior = 0; prior < sceneCount; ++prior)
                    foreach (var priorCue in previous.scenes[prior].events)
                        initialAudio = initialAudio.After(priorCue);
            }
            string map = scene.backgroundPath;
            BeyondDestinyVisual visual = default;
            int start = Progress.cueIndex;
            int checkpoint = start;
            for (int index = 0; index < scene.events.Length; ++index)
            {
                var cue = scene.events[index];
                if (index < start) initialAudio = initialAudio.After(cue);
                var encounter = _campaign.EncounterAt(scene.id, index == 0 ? scene.line : cue.line) ??
                    _campaign.EncounterAt(scene.id, cue.line);
                if (index >= start && encounter?.Playable == true && !Progress.resolvedEncounters.Contains(encounter.id))
                    return new Segment(pages.ToArray(), encounter, index, audio.ToArray(), initialAudio, visual);

                switch (cue.kind)
                {
                    case "background":
                        map = cue.assetPath;
                        if (index >= start) audio.Add(cue);
                        break;
                    case "direction":
                    case "standing":
                        if (index >= start) visual = visual.After(cue);
                        break;
                    case "music":
                    case "sound":
                    case "audio":
                        if (index >= start) audio.Add(cue);
                        break;
                }
                if (!cue.IsReadable || index < start) continue;
                pages.Add(new Page(checkpoint, cue, map, visual, audio.ToArray()));
                checkpoint = index + 1;
                audio.Clear();
                visual = default;
            }
            return new Segment(pages.ToArray(), null, scene.events.Length, audio.ToArray(), initialAudio, visual);
        }

        internal void RememberPage(int page)
        {
            // Save at the start of the current cue group so music and effects replay after reopening.
            if (page <= Progress.cueIndex) return;
            Progress.cueIndex = page;
            Save();
        }

        internal void Finish(Segment segment)
        {
            if (segment.Duel != null) Progress.cueIndex = segment.EndIndex;
            else
            {
                int episode = Progress.episodeIndex;
                Progress.cueIndex = Scene.events.Length - 1;
                Progress.Advance(_campaign);
                if (episode != Progress.episodeIndex || Progress.completed)
                    AddOnce(Progress.earnedRewards, EpisodeReward(episode));
            }
            Save();
        }

        internal void Resolve(BeyondDestinyCampaign.Encounter encounter)
        {
            if (Read().Duel != encounter) throw new InvalidOperationException("The encounter is no longer current.");
            AddOnce(Progress.resolvedEncounters, encounter.id);
            AddOnce(Progress.unlockedDecks, encounter.playerDeckId);
            AddOnce(Progress.earnedRewards, encounter.id);
            Save();
        }

        internal void UnlockLocation(string path)
        {
            if (!string.IsNullOrEmpty(path) && AddOnce(Progress.unlockedLocations, Path.GetFileNameWithoutExtension(path)))
                Save();
        }

        internal static int ObjectiveTurn(string resolution) => resolution switch
        {
            "interrupted" => 4,
            "continues" => 5,
            "canonicalOpponentWin" => 5,
            _ => 0,
        };

        internal static bool CanContinue(string resolution, bool won, bool validResult, bool reachedObjective) =>
            reachedObjective || validResult && (won || resolution is "interrupted" or "sparring" or "canonicalOpponentWin");

        private void Save() => Progress.Save(_saveDirectory, _campaign, _replay);

        private static string EpisodeReward(int index) => $"episode_{index + 1:00}_clear";

        private static bool AddOnce<T>(List<T> values, T value)
        {
            if (values.Contains(value)) return false;
            values.Add(value);
            return true;
        }
    }
}
