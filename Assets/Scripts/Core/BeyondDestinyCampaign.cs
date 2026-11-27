using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace YGOPortable.Core
{
    internal sealed class BeyondDestinyCampaign
    {
        internal const string Id = "beyond_destiny";
        internal const string AssetFolder = "5DS";
        internal const int EpisodeCount = 37;
        internal const int SceneCount = 182;

        [Serializable]
        internal sealed class Episode
        {
            public string id, title;
            public int number;
            public Scene[] scenes;
        }

        [Serializable]
        internal sealed class Scene
        {
            public string id, title, backgroundKey, backgroundPath;
            public int number, line;
            public Cue[] events;
        }

        [Serializable]
        internal sealed class Cue
        {
            public string kind, text, speaker, speakerKey, tone, key, action, assetPath, cutsceneKey, audioAccent;
            public string heroPortrait, guestPortrait;
            public int line, speakerSide;
            public string[] clipKeys;
            public Standing[] cast;

            internal bool IsReadable => kind is "dialogue" or "thought" or "narration" or "system" or "prose";
        }

        [Serializable]
        internal sealed class Standing
        {
            public string speaker, speakerKey, pose;
        }

        [Serializable]
        internal sealed class Deck : PresetDeckInfo
        {
            public string key, charName;
        }

        [Serializable]
        internal sealed class Encounter
        {
            public string id, episodeId, sceneId, mode, resolution;
            public int line, playerDeckId, opponentDeckId;

            internal bool Playable => mode is "standard" or "riding";
            internal bool Riding => mode is "riding" or "cinematicRiding";
        }

        [Serializable]
        private sealed class ScriptFile
        {
            public string id, title;
            public int schemaVersion;
            public bool linear;
            public Episode[] episodes;
        }

        [Serializable]
        private sealed class DeckFile
        {
            public int version;
            public string series;
            public Deck[] decks;
            public Encounter[] encounters;
        }

        private readonly Dictionary<int, Deck> _decks = new();
        private readonly Dictionary<(string SceneId, int Line), Encounter> _encounters = new();

        internal string Title { get; }
        internal IReadOnlyList<Episode> Episodes { get; }

        internal Deck DeckById(int id) => _decks.GetValueOrDefault(id);

        internal Encounter EncounterAt(string sceneId, int line) => _encounters.GetValueOrDefault((sceneId, line));

        internal static BeyondDestinyCampaign Load(string streamingAssets)
        {
            var script = JsonUtility.FromJson<ScriptFile>(
                File.ReadAllText(Path.Combine(streamingAssets, "story_5ds_script.json")));
            var decks = JsonUtility.FromJson<DeckFile>(
                File.ReadAllText(Path.Combine(streamingAssets, "story_5ds_rpg_decks.json")));
            return new BeyondDestinyCampaign(script, decks);
        }

        private BeyondDestinyCampaign(ScriptFile script, DeckFile decks)
        {
            if (script?.id != Id || script.schemaVersion != 1 || !script.linear ||
                string.IsNullOrWhiteSpace(script.title) || script.episodes?.Length != EpisodeCount)
                throw new InvalidDataException("Beyond Destiny requires the complete 37-episode linear script.");
            if (decks?.series != AssetFolder || decks.version != 1 || decks.decks?.Length is not > 0 ||
                decks.encounters?.Length is not > 0)
                throw new InvalidDataException("The Beyond Destiny deck or encounter catalog is missing.");

            var scenes = new Dictionary<string, (Episode Episode, Scene Scene)>(StringComparer.Ordinal);
            var episodeIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < script.episodes.Length; ++i)
            {
                var episode = script.episodes[i];
                if (episode?.number != i + 1 || string.IsNullOrWhiteSpace(episode.id) ||
                    !episodeIds.Add(episode.id) || episode.scenes?.Length is not > 0)
                    throw new InvalidDataException($"Invalid Beyond Destiny episode at index {i}.");
                var backgrounds = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < episode.scenes.Length; ++j)
                {
                    var scene = episode.scenes[j];
                    if (scene?.number != j + 1 || string.IsNullOrWhiteSpace(scene.id) ||
                        !scenes.TryAdd(scene.id, (episode, scene)) || scene.events?.Length is not > 0)
                        throw new InvalidDataException($"Invalid scene {j + 1} of {episode.id}.");
                    int line = scene.line;
                    foreach (var cue in scene.events)
                    {
                        if (cue == null || cue.line <= line || string.IsNullOrWhiteSpace(cue.kind) ||
                            (cue.IsReadable && string.IsNullOrWhiteSpace(cue.text)))
                            throw new InvalidDataException($"Invalid cue in {scene.id} after source line {line}.");
                        if (cue.kind == "background" && (string.IsNullOrWhiteSpace(cue.key) || !backgrounds.Add(cue.key)))
                            throw new InvalidDataException($"Invalid background key in {scene.id}.");
                        line = cue.line;
                    }
                    if (!backgrounds.Contains(scene.backgroundKey ?? ""))
                        throw new InvalidDataException($"Undefined opening background in {scene.id}.");
                }
            }
            if (scenes.Count != SceneCount) throw new InvalidDataException("Beyond Destiny requires 182 scenes.");

            foreach (var deck in decks.decks)
            {
                if (deck?.id is not (>= 75001 and <= 75040) || deck.main?.Length is not (>= 40 and <= 60) ||
                    deck.extra?.Length is not (>= 0 and <= 15) || deck.side?.Length is not (>= 0 and <= 15) ||
                    !_decks.TryAdd(deck.id, deck))
                    throw new InvalidDataException($"Invalid Beyond Destiny deck {deck?.id}.");
            }
            foreach (var encounter in decks.encounters)
            {
                if (encounter == null || string.IsNullOrWhiteSpace(encounter.sceneId) ||
                    !scenes.TryGetValue(encounter.sceneId, out var target) || target.Episode.id != encounter.episodeId ||
                    (encounter.line != target.Scene.line && !Array.Exists(target.Scene.events, cue =>
                        cue.line == encounter.line && cue.kind == "system")) ||
                    !_decks.ContainsKey(encounter.playerDeckId) || !_decks.ContainsKey(encounter.opponentDeckId) ||
                    !_encounters.TryAdd((encounter.sceneId, encounter.line), encounter))
                    throw new InvalidDataException($"Invalid Beyond Destiny encounter {encounter?.id}.");
            }

            Title = script.title;
            Episodes = script.episodes;
        }
    }
}
