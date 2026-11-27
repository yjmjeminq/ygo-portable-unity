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
            internal readonly string Map, Hero, Other, Cutscene, Effect;
            internal readonly BeyondDestinyCampaign.Cue[] Audio;

            internal Page(int index, BeyondDestinyCampaign.Cue text, string map, string hero, string other,
                string cutscene, string effect, BeyondDestinyCampaign.Cue[] audio)
            {
                Index = index;
                Text = text;
                Map = map;
                Hero = hero;
                Other = other;
                Cutscene = cutscene;
                Effect = effect;
                Audio = audio;
            }
        }

        internal readonly struct Segment
        {
            internal readonly Page[] Pages;
            internal readonly BeyondDestinyCampaign.Encounter Duel;
            internal readonly int EndIndex;
            internal readonly BeyondDestinyCampaign.Cue[] TailAudio;

            internal Segment(Page[] pages, BeyondDestinyCampaign.Encounter duel, int endIndex,
                BeyondDestinyCampaign.Cue[] tailAudio)
            {
                Pages = pages;
                Duel = duel;
                EndIndex = endIndex;
                TailAudio = tailAudio;
            }
        }

        private readonly BeyondDestinyCampaign _campaign;
        private readonly string _saveDirectory;
        internal BeyondDestinyProgress Progress { get; }
        internal BeyondDestinyCampaign.Episode Episode => _campaign.Episodes[Progress.episodeIndex];
        internal BeyondDestinyCampaign.Scene Scene => Progress.Scene(_campaign);
        internal BeyondDestinyCampaign.Deck Deck(int id) => _campaign.DeckById(id);

        internal BeyondDestinyPlayback(BeyondDestinyCampaign campaign, string saveDirectory)
        {
            _campaign = campaign;
            _saveDirectory = saveDirectory;
            Progress = BeyondDestinyProgress.Load(saveDirectory, campaign);
        }

        internal Segment Read()
        {
            var scene = Scene;
            var pages = new List<Page>();
            var audio = new List<BeyondDestinyCampaign.Cue>();
            var poses = new Dictionary<string, string>(StringComparer.Ordinal);
            var outfits = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int prior = 0; prior < Progress.sceneIndex; ++prior)
                foreach (var priorCue in Episode.scenes[prior].events)
                    RememberCast(priorCue, poses: null, outfits);
            string map = scene.backgroundPath;
            string effect = null;
            int start = Progress.cueIndex;
            int checkpoint = start;
            for (int index = 0; index < scene.events.Length; ++index)
            {
                var cue = scene.events[index];
                var encounter = _campaign.EncounterAt(scene.id, index == 0 ? scene.line : cue.line) ??
                    _campaign.EncounterAt(scene.id, cue.line);
                if (index >= start && encounter?.Playable == true && !Progress.resolvedEncounters.Contains(encounter.id))
                    return new Segment(pages.ToArray(), encounter, index, audio.ToArray());

                switch (cue.kind)
                {
                    case "background":
                        map = cue.assetPath;
                        break;
                    case "standing":
                        RememberCast(cue, poses, outfits);
                        break;
                    case "direction":
                        if (index >= start) effect = VisualEffect(cue.text) ?? effect;
                        break;
                    case "music":
                    case "sound":
                    case "audio":
                        if (index >= start) audio.Add(cue);
                        break;
                }
                if (!cue.IsReadable || index < start) continue;
                var actor = cue.speakerKey;
                string cast = actor == "hero" ? null : Portrait(actor, outfits.GetValueOrDefault(actor ?? ""));
                if (cast == null)
                {
                    foreach (var stageActor in poses)
                        if (stageActor.Key != "hero") cast = Portrait(stageActor.Key, outfits.GetValueOrDefault(stageActor.Key));
                }
                pages.Add(new Page(checkpoint, cue, map, Portrait("hero", outfits.GetValueOrDefault("hero")),
                    cast, cue.cutsceneKey, effect, audio.ToArray()));
                checkpoint = index + 1;
                audio.Clear();
                effect = null;
            }
            return new Segment(pages.ToArray(), null, scene.events.Length, audio.ToArray());
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

        private void Save() => Progress.Save(_saveDirectory, _campaign);

        private static string EpisodeReward(int index) => $"episode_{index + 1:00}_clear";

        private static bool AddOnce<T>(List<T> values, T value)
        {
            if (values.Contains(value)) return false;
            values.Add(value);
            return true;
        }

        private static void RememberCast(BeyondDestinyCampaign.Cue cue, Dictionary<string, string> poses,
            Dictionary<string, string> outfits)
        {
            foreach (var member in cue.cast ?? Array.Empty<BeyondDestinyCampaign.Standing>())
            {
                if (poses != null) poses[member.speakerKey] = member.pose;
                string outfit = Outfit(member.speakerKey, member.pose);
                if (outfit != null) outfits[member.speakerKey] = outfit;
            }
        }

        private static string Outfit(string actor, string pose)
        {
            if (string.IsNullOrEmpty(pose)) return null;
            bool everyday = pose.Contains("일상") || pose.Contains("평상복") || pose.Contains("사복");
            if (actor == "aki" && pose.Contains("가면"))
                return pose.Contains("없") || pose.Contains("벗") || pose.Contains("해제") ? "" : "masked";
            if (actor == "aki" && everyday) return "casual";
            if (actor == "sherry" && pose.Contains("타락")) return "fallen";
            if (actor == "sherry" && everyday) return "";
            return null;
        }

        private static string Portrait(string actor, string outfit) => string.IsNullOrWhiteSpace(actor) ? null :
            "Characters_Standing/" + actor + (string.IsNullOrEmpty(outfit) ? "" : "_" + outfit);

        private static string VisualEffect(string text) =>
            text.Contains("흔들") || text.Contains("진동") ? "shake" :
            text.Contains("섬광") ? "flash" : text.Contains("암전") ? "fade" : null;
    }
}
