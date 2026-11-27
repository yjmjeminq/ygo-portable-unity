using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class BeyondDestinyAudio : MonoBehaviour
    {
        private Loop _music, _ambience;
        private AudioSource _effects;
        private BeyondDestinySoundscape _state;
        private readonly Dictionary<string, AudioClip> _effectClips = new(StringComparer.Ordinal);

        internal string MusicPath => _music?.Path;
        internal string AmbiencePath => _ambience?.Path;
        internal AudioSource MusicSource => _music?.Current;
        internal AudioSource AmbienceSource => _ambience?.Current;
        internal AudioSource EffectsSource => _effects;

        private void Awake()
        {
            _music = new Loop(NewSource(), NewSource());
            _ambience = new Loop(NewSource(), NewSource());
            _effects = NewSource();
        }

        private AudioSource NewSource()
        {
            var voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0;
            return voice;
        }

        internal void Restore(BeyondDestinySoundscape state) => Set(state, .32f, .28f);

        internal void Apply(BeyondDestinyCampaign.Cue[] cues)
        {
            var timeline = _state;
            float musicFade = .36f, ambientFade = .32f;
            foreach (var cue in cues)
            {
                timeline = timeline.After(cue);
                if (cue.kind is "audio" or "music")
                    musicFade = cue.action switch
                    {
                        "cut" or "stop" => 0f,
                        "crossfade" => .7f,
                        "fadeOut" => .55f,
                        _ => .36f,
                    };
                if (cue.kind == "audio" || cue.kind == "sound" && cue.action == "silence")
                {
                    _effects.Stop();
                    ambientFade = cue.action == "fadeOut" ? .55f : 0f;
                }
                if (cue.kind == "sound" && cue.action == "play") Effect(cue.assetPath);
                if (cue.audioAccent != null) Effect(cue.audioAccent);
            }
            Set(timeline, musicFade, ambientFade);
        }

        private void Set(BeyondDestinySoundscape state, float musicFade, float ambientFade)
        {
            _state = state;
            _music.Change(state.Music, musicFade, DuelAudio.BgmVolume);
            _ambience.Change(state.Ambience, ambientFade, DuelAudio.SfxVolume * .52f);
        }

        private void Effect(string path)
        {
            if (path == null) return;
            if (!_effectClips.TryGetValue(path, out var clip))
                _effectClips[path] = clip = LoadClip(path);
            if (clip != null)
            {
                _effects.volume = DuelAudio.SfxVolume;
                _effects.PlayOneShot(clip, .82f);
            }
        }

        private static AudioClip LoadClip(string path)
        {
            var full = Path.Combine(Application.streamingAssetsPath, "TagForce", BeyondDestinyCampaign.AssetFolder, path);
            return DuelVisualAssets.ReadSound(full, Path.GetFileNameWithoutExtension(path));
        }

        private void Update()
        {
            float elapsed = Time.unscaledDeltaTime;
            _music.Tick(elapsed, DuelAudio.BgmVolume);
            _ambience.Tick(elapsed, DuelAudio.SfxVolume * .52f);
            _effects.volume = DuelAudio.SfxVolume;
        }

        internal void Stop()
        {
            _state = default;
            _music?.Stop();
            _ambience?.Stop();
            if (_effects != null) _effects.Stop();
            foreach (var clip in _effectClips.Values)
                if (clip != null) Destroy(clip);
            _effectClips.Clear();
        }

        private void OnDestroy() => Stop();

        private sealed class Loop
        {
            private AudioSource _current, _outgoing;
            private float _duration, _elapsed, _outgoingGain, _currentGain;
            internal string Path { get; private set; }
            internal AudioSource Current => _current;

            internal Loop(AudioSource first, AudioSource second)
            {
                _current = first;
                _outgoing = second;
                first.loop = second.loop = true;
            }

            internal void Change(string path, float duration, float volume)
            {
                if (string.Equals(path, Path, StringComparison.Ordinal)) return;
                var next = path == null ? null : LoadClip(path);
                Release(_outgoing);
                (_current, _outgoing) = (_outgoing, _current);
                Path = next == null ? null : path;
                _outgoingGain = _currentGain;
                _currentGain = duration <= 0f ? 1f : 0f;
                _duration = duration;
                _elapsed = 0f;
                _current.clip = next;
                _current.volume = _currentGain * volume;
                if (next != null) _current.Play();
                if (_duration <= 0f) Release(_outgoing);
            }

            internal void Tick(float delta, float volume)
            {
                _elapsed += delta;
                float progress = _duration <= 0f ? 1f : Mathf.Clamp01(_elapsed / _duration);
                _currentGain = Mathf.SmoothStep(0f, 1f, progress);
                _current.volume = volume * _currentGain;
                if (_outgoing.clip != null)
                {
                    _outgoing.volume = volume * _outgoingGain * (1f - _currentGain);
                    if (progress >= 1f) Release(_outgoing);
                }
            }

            internal void Stop()
            {
                Path = null;
                Release(_current);
                Release(_outgoing);
                _duration = _elapsed = _currentGain = _outgoingGain = 0f;
            }

            private static void Release(AudioSource source)
            {
                if (source == null) return;
                source.Stop();
                var clip = source.clip;
                source.clip = null;
                if (clip != null) Destroy(clip);
            }
        }
    }
}
