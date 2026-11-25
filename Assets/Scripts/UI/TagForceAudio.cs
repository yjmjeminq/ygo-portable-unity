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

    public static partial class DuelAudio
    {
        private static readonly Dictionary<TagForceSfx, AudioClip> TagForceSfxClips = new();
        private static AudioClip _clipWorldReward, _clipWorldDayBgm, _clipWorldNightBgm;

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

        internal static void PlayTagForceReward()
        {
            if (_sfxSource != null && SfxVolume > 0.01f)
                Play(_clipWorldReward ??= DuelVisualAssets.LoadSound("Win") ?? MakeRewardJingle());
        }

        internal static void PlayTagForceBgm(bool night)
        {
            if (_bgmSource == null) return;
            var clip = night
                ? (_clipWorldNightBgm ??= MakeAmbientBgm(22050, "tag_force_night", 3f, new[] { 98f, 82.41f, 87.31f, 110f }))
                : (_clipWorldDayBgm ??= MakeAmbientBgm(22050, "tag_force_day", 1.6f, new[] { 146.83f, 130.81f, 164.81f, 196f }));
            if (_bgmSource.clip != clip)
            {
                _bgmSource.Stop();
                _bgmSource.clip = clip;
            }
            _bgmSource.loop = true;
            _bgmSource.volume = BgmVolume;
            if (BgmVolume >= 0.01f && !_bgmSource.isPlaying) _bgmSource.Play();
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

        private static AudioClip MakeAmbientBgm(int sr, string name, float secondsPerBar, float[] chords)
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

                samples[i] = Mathf.Clamp((pad + pluck) * loopFade, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
