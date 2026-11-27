using System;
using System.Collections.Generic;
using UnityEngine;

namespace YGOPortable.UI
{
    internal enum TagForceSfx
    {
        Confirm, Cancel, Cursor, DialogueBlip, HeartUp, HeartDown, CardDraw, DuelEncounter,
        SandwichRoulette, SandwichWin, PartnerAccept,
    }

    internal enum RpgFieldMusic { Home, Outskirts, Circuit, Crisis }

    public static partial class DuelAudio
    {
        private static readonly Dictionary<TagForceSfx, AudioClip> TagForceSfxClips = new();
        private static readonly Dictionary<string, AudioClip> StorySfxClips = new(StringComparer.Ordinal);
        private static AudioClip _clipWorldReward, _clipWorldDayBgm, _clipWorldNightBgm, _clipRpgCircuit, _clipRpgCrisis;

        internal static AudioClip GetTagForceClip(TagForceSfx sfx)
        {
            if (TagForceSfxClips.TryGetValue(sfx, out var cached)) return cached;
            var (file, alternate) = sfx switch
            {
                TagForceSfx.Confirm => ("sfx_button_confirm", "Click"),
                TagForceSfx.Cancel => ("sfx_button_cancel", "Cancel"),
                TagForceSfx.Cursor => ("sfx_cursor_move", "Click"),
                TagForceSfx.DialogueBlip => ("sfx_dialogue_blip", "DialogueBlip"),
                TagForceSfx.HeartUp => ("sfx_heart_up", "HeartUp"),
                TagForceSfx.HeartDown => ("sfx_heart_down", "HeartDown"),
                TagForceSfx.CardDraw => ("sfx_card_draw", "Draw"),
                TagForceSfx.DuelEncounter => ("sfx_duel_encounter", "DuelEncounter"),
                TagForceSfx.SandwichRoulette => ("sfx_sandwich_roulette", "SandwichRoulette"),
                TagForceSfx.SandwichWin => ("sfx_sandwich_win", "SandwichWin"),
                TagForceSfx.PartnerAccept => ("sfx_partner_accept", "SandwichWin"),
                _ => throw new ArgumentOutOfRangeException(nameof(sfx)),
            };
            var clip = DuelVisualAssets.LoadSound(file) ?? DuelVisualAssets.LoadSound(alternate);
            TagForceSfxClips[sfx] = clip;
            return clip;
        }

        internal static void PlayTagForceSfx(TagForceSfx sfx, float volumeMultiplier = 1f)
        {
            if (_sfxSource == null || SfxVolume <= .01f || volumeMultiplier <= 0f) return;
            var fallback = sfx switch
            {
                TagForceSfx.DialogueBlip => _clipTick,
                TagForceSfx.CardDraw => _clipDraw,
                TagForceSfx.HeartUp or TagForceSfx.SandwichWin or TagForceSfx.PartnerAccept => _clipWin,
                TagForceSfx.DuelEncounter => _clipActivate,
                _ => _clipClick,
            };
            Play(GetTagForceClip(sfx) ?? fallback, volumeMultiplier);
        }

        private static readonly AudioClip[,] RpgSteps = new AudioClip[6, 2];

        internal static AudioClip GetRpgFootstep(RpgSurface surface, int foot)
        {
            int take = foot < 0 ? 0 : 1;
            return RpgSteps[(int)surface, take] ??= MakeRpgFootstep(surface, take);
        }

        internal static void PlayRpgFootstep(RpgSurface surface, int foot, float volume)
        {
            if (_sfxSource != null && SfxVolume > .01f) Play(GetRpgFootstep(surface, foot), volume);
        }

        internal static void PlayStorySfx(string cue)
        {
            if (_sfxSource == null || SfxVolume <= .01f) return;
            Play(GetStorySfx(cue), cue is "crowd" or "rain" ? .58f : .75f);
        }

        internal static AudioClip GetStorySfx(string cue)
        {
            if (string.IsNullOrEmpty(cue)) return null;
            if (cue == "footstep") return GetRpgFootstep(RpgSurface.Stone, -1);
            if (StorySfxClips.TryGetValue(cue, out var cached)) return cached;
            var (length, seed) = cue switch
            {
                "duel_disk" => (.27f, 11),
                "crowd" => (1.45f, 17),
                "rain" => (1.55f, 23),
                "thunder" => (1.25f, 31),
                "can_open" => (.42f, 37),
                "door_knock" => (.43f, 43),
                "shatter" => (.61f, 53),
                "alarm" => (.87f, 61),
                "transition" => (.52f, 71),
                _ => (0f, 0),
            };
            if (length <= 0f) return null;
            return StorySfxClips[cue] = DuelVisualAssets.LoadSound("story_" + cue) ??
                (cue == "duel_disk" ? _clipActivate ?? DuelVisualAssets.LoadSound("Activate") : null) ??
                MakeStoryFoley(cue, length, seed);
        }

        private static AudioClip MakeStoryFoley(string cue, float duration, int seed)
        {
            const int rate = 22050;
            const float tau = 2f * Mathf.PI;
            var samples = new float[Mathf.RoundToInt(duration * rate)];
            var random = new System.Random(seed);
            float low = 0f, mid = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                float noise = (float)random.NextDouble() * 2f - 1f;
                low += .019f * (noise - low);
                mid += .17f * (noise - mid);
                float value = cue switch
                {
                    "crowd" => (mid * .63f + low * 1.1f) * (.66f + .20f * Mathf.Sin(t * 19f) + .14f * Mathf.Sin(t * 37f)),
                    "rain" => mid * .37f + noise * .13f * (.4f + .6f * Mathf.Pow(Mathf.Abs(Mathf.Sin(131f * t)), 10f)),
                    "thunder" => (low * 2.8f + Mathf.Sin(tau * 44f * t) * .17f) * Mathf.Exp(-t * 3.4f),
                    "can_open" => (noise * .56f + Mathf.Sin(tau * 1450f * t) * .24f) * Mathf.Exp(-t * 75f) +
                        Mathf.Max(0f, noise) * .27f * Mathf.Exp(-t * 9f),
                    "door_knock" => Knock(t) + .8f * Knock(t - .157f),
                    "shatter" => (noise * .50f + Mathf.Sin(tau * 2097f * t) * .14f + Mathf.Sin(tau * 3389f * t) * .09f) *
                        Mathf.Exp(-t * 10f) * (.72f + .28f * Mathf.Sin(121f * t)),
                    "alarm" => Mathf.Sin(tau * (t % .29f < .12f ? 812f : 612f) * t) * .23f *
                        Mathf.Sin(Mathf.PI * Mathf.Clamp01(t % .29f / .19f)),
                    "transition" => (Mathf.Sin(tau * 329.63f * t) + .42f * Mathf.Sin(tau * 493.88f * t)) *
                        .19f * Mathf.Sin(Mathf.PI * t / duration),
                    _ => Mathf.Sin(tau * (190f * t + 2200f * t * t)) * .29f * Mathf.Exp(-t * 9f) + mid * .40f * Mathf.Exp(-t * 48f),
                };
                float edge = Mathf.Clamp01(t * 850f) * Mathf.Clamp01((duration - t) * 18f);
                samples[i] = Mathf.Clamp(value * edge, -.95f, .95f);

                float Knock(float strike) => strike < 0f ? 0f :
                    (Mathf.Sin(tau * 122f * strike) * .41f + mid * .29f) * Mathf.Exp(-strike * 59f);
            }
            var clip = AudioClip.Create("story_" + cue, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip MakeRpgFootstep(RpgSurface surface, int take)
        {
            var (duration, tone, decay, noise) = surface switch
            {
                RpgSurface.Soil => (.105f, 118f, 30f, .66f),
                RpgSurface.Sand => (.14f, 81f, 25f, .86f),
                RpgSurface.Wood => (.098f, 234f, 47f, .32f),
                RpgSurface.Rock => (.12f, 109f, 34f, .43f),
                RpgSurface.Metal => (.095f, 414f, 51f, .26f),
                _ => (.077f, 303f, 65f, .31f),
            };
            const int rate = 22050;
            var samples = new float[Mathf.RoundToInt(duration * rate)];
            var random = new System.Random(934 + (int)surface * 59 + take * 23);
            float filtered = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                float grain = (float)random.NextDouble() * 2f - 1f;
                filtered = Mathf.Lerp(filtered, grain, surface == RpgSurface.Sand ? .20f : .42f);
                float attack = Mathf.Clamp01(t * 550f);
                float release = Mathf.Clamp01((duration - t) * 90f);
                float impact = Mathf.Sin(t * tone * (take == 0 ? 6.28f : 6.04f)) * Mathf.Exp(-t * decay);
                float scuff = filtered * Mathf.Exp(-t * (decay * .72f));
                samples[i] = .49f * (impact * (1f - noise) + scuff * noise) * attack * release;
            }
            var clip = AudioClip.Create($"field_step_{surface}_{take}", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        internal static void PlayTagForceReward()
        {
            if (_sfxSource != null && SfxVolume > 0.01f)
                Play(_clipWorldReward ??= DuelVisualAssets.LoadSound("Win") ?? MakeRewardJingle());
        }

        internal static void PlayTagForceBgm(bool night) => PlayRpgFieldMusic(night ? RpgFieldMusic.Outskirts : RpgFieldMusic.Home);

        internal static AudioClip GetRpgFieldMusic(RpgFieldMusic music) => music switch
        {
            RpgFieldMusic.Home => _clipWorldDayBgm ??= MakeAmbientBgm(22050, "tag_force_day", 1.6f, new[] { 146.83f, 130.81f, 164.81f, 196f }),
            RpgFieldMusic.Outskirts => _clipWorldNightBgm ??= MakeAmbientBgm(22050, "tag_force_night", 3f, new[] { 98f, 82.41f, 87.31f, 110f }),
            RpgFieldMusic.Circuit => _clipRpgCircuit ??= DuelVisualAssets.LoadSound("RpgCircuitMusic") ??
                MakeAmbientBgm(22050, "5ds_rpg_circuit", 1.04f, new[] { 110f, 130.81f, 146.83f, 123.47f }, driving: true),
            RpgFieldMusic.Crisis => _clipRpgCrisis ??= DuelVisualAssets.LoadSound("RpgCrisisMusic") ??
                MakeAmbientBgm(22050, "5ds_rpg_crisis", 2.46f, new[] { 55f, 58.27f, 49f, 51.91f }, driving: true),
            _ => throw new ArgumentOutOfRangeException(nameof(music)),
        };

        internal static void PlayRpgFieldMusic(RpgFieldMusic music)
        {
            if (_bgmSource != null) PlayBgmClip(GetRpgFieldMusic(music));
        }

        private static AudioClip MakeRewardJingle()
        {
            const int sampleRate = 22050;
            const int note = 3000;
            const int spacing = 1800;
            float[] pitches = { 523.25f, 659.25f, 783.99f, 1046.50f };
            float[] samples = new float[note + spacing * (pitches.Length - 1)];
            for (int n = 0; n < pitches.Length; n++)
            {
                for (int i = 0; i < note; i++)
                {
                    float phase = i * pitches[n] * 2f * Mathf.PI / sampleRate;
                    float envelope = Mathf.Min(1f, i / 80f) * (1f - (float)i / note);
                    samples[n * spacing + i] += (Mathf.Sin(phase) + .22f * Mathf.Sin(phase * 2f)) * envelope * .21f;
                }
            }
            var clip = AudioClip.Create("tag_force_reward", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip MakeAmbientBgm(int sr, string name, float secondsPerBar, float[] chords, bool driving = false)
        {
            int n = Mathf.RoundToInt(sr * secondsPerBar * chords.Length);
            float[] samples = new float[n];

            double pChord = 0.0, pPluck = 0.0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sr;
                int bar = Mathf.Min((int)(t / secondsPerBar), chords.Length - 1);
                float f = chords[bar];

                pChord += 2.0 * Math.PI * f / sr;
                float pad = (float)(Math.Sin(pChord) * 0.20 + Math.Sin(pChord * 1.5) * 0.12 + Math.Sin(pChord * 2.0) * 0.08);

                float beat = t * 4f / secondsPerBar;
                int pStep = (int)beat % 2;
                float pf = f * (pStep == 0 ? 2f : 3f);
                pPluck += 2.0 * Math.PI * pf / sr;
                float pluckEnv = Mathf.Repeat(beat, 1f);
                float pluck = (float)Math.Sin(pPluck) * Mathf.Exp(-pluckEnv * 3.5f) * 0.12f;

                float loopFade = 1f;
                if (i < 1000) loopFade = i / 1000f;
                else if (i > n - 1000) loopFade = (n - i) / 1000f;

                float kick = driving ? Mathf.Sin(pluckEnv * 23f) * Mathf.Exp(-pluckEnv * 10f) * .19f : 0f;
                samples[i] = Mathf.Clamp((pad + pluck + kick) * loopFade, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
