using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal static class DuelVisualAssets
    {
        private static readonly Dictionary<DuelEffectKind, Sprite> Effects = new();
        internal static Texture2D Field { get; } = LoadTexture("Field.png", TextureWrapMode.Clamp);
        internal static Texture2D Ground { get; } = LoadTexture("Ground.png", TextureWrapMode.Repeat);
        internal static Sprite PhaseBase { get; } = LoadHudSprite("PhaseBase");
        internal static Sprite PhaseRim { get; } = LoadHudSprite("PhaseRim");
        internal static Sprite PlayerLp { get; } = LoadHudSprite("LPPlayer");
        internal static Sprite OpponentLp { get; } = LoadHudSprite("LPOpponent");
        internal static Sprite DeckBack { get; } = LoadHudSprite("DeckBack");
        internal static Sprite PauseIcon { get; } = LoadHudSprite("Pause");
        internal static Sprite LogIcon { get; } = LoadHudSprite("Log");
        internal static Sprite HandGlow { get; } = LoadHudSprite("HandGlow", Vector4.one * 20f);
        internal static Sprite ChainLinks { get; } = LoadHudSprite("ChainLinks");
        internal static Sprite ChainFrame { get; } = LoadHudSprite("ChainFrame", Vector4.one * 48f);
        internal static Sprite PhaseBannerPlayer { get; } = LoadHudSprite("PhaseBannerPlayer");
        internal static Sprite PhaseBannerOpponent { get; } = LoadHudSprite("PhaseBannerOpponent");
        internal static Sprite ChainActive { get; } = LoadHudSprite("ChainActive");
        internal static Sprite ChainResolving { get; } = LoadHudSprite("ChainResolving");
        internal static Sprite ChainCardHighlight { get; } = LoadHudSprite("ChainCardHighlight");
        internal static Sprite CardStatsBase { get; } = LoadHudSprite("CardStatsBase");
        internal static Sprite CardStatsBaseAlt { get; } = LoadHudSprite("CardStatsBaseAlt");
        internal static Sprite WinnerCrown { get; } = LoadHudSprite("WinnerCrown");
        internal static Sprite DuelStart { get; } = LoadHudSprite("DuelStart");
        internal static Sprite DuelVictory { get; } = LoadHudSprite("DuelVictory");
        internal static Sprite DuelDefeat { get; } = LoadHudSprite("DuelDefeat");
        internal static Sprite DuelDraw { get; } = LoadHudSprite("DuelDraw");
        internal static Sprite GraveWell { get; } = LoadHudSprite("GraveWell");
        internal static Sprite BanishedWell { get; } = LoadHudSprite("BanishedWell");
        internal static Sprite SummonNamePlayer { get; } = LoadHudSprite("SummonNamePlayer");
        internal static Sprite SummonNameOpponent { get; } = LoadHudSprite("SummonNameOpponent");
        internal static Sprite SummonStatsPlayer { get; } = LoadHudSprite("SummonStatsPlayer");
        internal static Sprite SummonStatsOpponent { get; } = LoadHudSprite("SummonStatsOpponent");
        internal static Sprite SummonLevel { get; } = LoadHudSprite("SummonLevel");
        internal static Sprite SummonRank { get; } = LoadHudSprite("SummonRank");
        private static readonly Sprite DrawPhase = LoadHudSprite("PhaseDraw");
        private static readonly Sprite StandbyPhase = LoadHudSprite("PhaseStandby");
        private static readonly Sprite MainOnePhase = LoadHudSprite("PhaseMain1");
        private static readonly Sprite BattlePhase = LoadHudSprite("PhaseBattle");
        private static readonly Sprite MainTwoPhase = LoadHudSprite("PhaseMain2");
        private static readonly Sprite EndPhase = LoadHudSprite("PhaseEnd");

        internal static Sprite PhaseIcon(int phase) => phase switch
        {
            YgoConstants.PHASE_DRAW => DrawPhase,
            YgoConstants.PHASE_STANDBY => StandbyPhase,
            YgoConstants.PHASE_MAIN1 => MainOnePhase,
            YgoConstants.PHASE_MAIN2 => MainTwoPhase,
            YgoConstants.PHASE_END => EndPhase,
            YgoConstants.PHASE_BATTLE_START or YgoConstants.PHASE_BATTLE_STEP or YgoConstants.PHASE_BATTLE
                or YgoConstants.PHASE_DAMAGE or YgoConstants.PHASE_DAMAGE_CAL => BattlePhase,
            _ => null,
        };

        private static Sprite LoadHudSprite(string name, Vector4 border = default) =>
            AsSprite(LoadTexture(Path.Combine("Hud", name + ".png"), TextureWrapMode.Clamp), border);

        internal static Sprite Effect(DuelEffectKind kind)
        {
            if (!Effects.TryGetValue(kind, out var sprite))
            {
                sprite = AsSprite(LoadTexture(Path.Combine("Effects", kind + ".png"), TextureWrapMode.Clamp));
                Effects.Add(kind, sprite);
            }
            return sprite;
        }

        internal static Sprite SummonCutIn(uint code)
        {
            int variant = CardArtLoader.GetDuelArtworkVariant(code);
            if (variant < 0) return null;
            string path = Path.Combine("CutIns", $"{code}-{variant}.png");
            return AsSprite(LoadTexture(path, TextureWrapMode.Clamp));
        }

        private static Sprite AsSprite(Texture2D texture, Vector4 border = default) => texture == null ? null :
            Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(.5f, .5f),
                100f, 0, SpriteMeshType.FullRect, border);

        private static Texture2D LoadTexture(string name, TextureWrapMode wrapMode)
        {
            string path = Path.Combine(CardArtLoader.LocalDuelVisualsPath, name);
            if (!File.Exists(path)) return null;
            try
            {
                var bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = Path.GetFileNameWithoutExtension(name),
                    wrapMode = wrapMode,
                    filterMode = FilterMode.Bilinear,
                };
                if (texture.LoadImage(bytes, markNonReadable: true)) return texture;
                UnityEngine.Object.Destroy(texture);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[DuelVisualAssets] Cannot read {name}: {error.Message}");
            }
            return null;
        }

        internal static AudioClip LoadSound(string name)
        {
            string path = Path.Combine(CardArtLoader.LocalDuelVisualsPath, "Audio", name + ".wav");
            if (!File.Exists(path)) return null;
            try
            {
                using var reader = new BinaryReader(File.OpenRead(path));
                if (reader.BaseStream.Length < 44 || reader.ReadUInt32() != 0x46464952) return null; // RIFF
                reader.ReadUInt32();
                if (reader.ReadUInt32() != 0x45564157 || reader.ReadUInt32() != 0x20746D66 ||
                    reader.ReadUInt32() != 16 || reader.ReadUInt16() != 1) return null; // WAVE, PCM
                int channels = reader.ReadUInt16();
                int rate = reader.ReadInt32();
                reader.ReadUInt32();
                int frameBytes = reader.ReadUInt16();
                if (channels is < 1 or > 2 || rate is < 8000 or > 48000 || frameBytes != channels * 2 ||
                    reader.ReadUInt16() != 16 || reader.ReadUInt32() != 0x61746164) return null; // data
                int length = reader.ReadInt32();
                if (length <= 0 || length > reader.BaseStream.Length - 44 || length % frameBytes != 0) return null;

                var bytes = reader.ReadBytes(length);
                var samples = new float[length / 2];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = (short)(bytes[i * 2] | bytes[i * 2 + 1] << 8) / 32768f;
                var clip = AudioClip.Create(name, length / frameBytes, channels, rate, false);
                clip.SetData(samples, 0);
                return clip;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[DuelVisualAssets] Cannot read {name}: {error.Message}");
                return null;
            }
        }
    }
}
