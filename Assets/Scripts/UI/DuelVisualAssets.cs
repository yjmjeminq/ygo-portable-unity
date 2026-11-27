using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal static class DuelVisualAssets
    {
        private const int CutInCacheLimit = 8;

        private sealed class CutInEntry
        {
            internal Sprite Sprite;
            internal int Readers;
            internal ulong LastUse;
        }

        private static readonly Dictionary<string, CutInEntry> CutInCache = new(StringComparer.Ordinal);
        private static readonly HashSet<string> UnreadableCutIns = new(StringComparer.Ordinal);
        private static ulong _cutInUse;
        internal static Texture2D ArenaSurface { get; } = Resources.Load<Texture2D>("Duel/ObservatorySlab");
        internal static Texture2D ArenaBackdrop { get; } = Resources.Load<Texture2D>("Duel/AqueductGarden");
        internal static Sprite MasterDuelLogo { get; } = Resources.Load<Sprite>("Title/MasterDuelLogo") ??
            LoadHudSprite("MasterDuelLogo");
        internal static Sprite PauseIcon { get; } = LoadHudSprite("Pause");
        internal static Sprite LogIcon { get; } = LoadHudSprite("Log");
        internal static Sprite ChainLinks { get; } = LoadHudSprite("ChainLinks");
        internal static Sprite ChainFrame { get; } = LoadHudSprite("ChainFrame", Vector4.one * 48f);
        internal static Sprite ChainActive { get; } = LoadHudSprite("ChainActive");
        internal static Sprite ChainResolving { get; } = LoadHudSprite("ChainResolving");
        internal static Sprite ChainCardHighlight { get; } = LoadHudSprite("ChainCardHighlight");
        internal static Sprite ChainWinding { get; } = LoadHudSprite("ChainWinding");
        internal static Sprite SummonNamePlayer { get; } = LoadHudSprite("SummonNamePlayer");
        internal static Sprite SummonNameOpponent { get; } = LoadHudSprite("SummonNameOpponent");
        internal static Sprite SummonLevel { get; } = LoadHudSprite("SummonLevel");
        internal static Sprite SummonRank { get; } = LoadHudSprite("SummonRank");
        internal static Sprite SummonStatsPlayer { get; } = LoadHudSprite("SummonStatsPlayer");
        internal static Sprite SummonStatsOpponent { get; } = LoadHudSprite("SummonStatsOpponent");
        internal static Sprite GraveWell { get; } = LoadHudSprite("GraveWell");
        internal static Sprite BanishedWell { get; } = LoadHudSprite("BanishedWell");
        internal static Sprite CardStatsBase { get; } = LoadHudSprite("CardStatsBase");
        internal static Sprite CardStatsBaseAlt { get; } = LoadHudSprite("CardStatsBaseAlt");
        internal static Sprite LpPlayer { get; } = LoadHudSprite("LPPlayer");
        internal static Sprite LpOpponent { get; } = LoadHudSprite("LPOpponent");
        internal static Sprite HandGlow { get; } = LoadHudSprite("HandGlow", Vector4.one * 18f);
        internal static Sprite PromptGrave { get; } = LoadHudSprite("PromptGrave");
        internal static Sprite PromptGroupLabel { get; } = LoadHudSprite("PromptGroupLabel");
        internal static Sprite PromptFieldGlow { get; } = LoadHudSprite("PromptFieldGlow");
        internal static Sprite PromptFieldArrow { get; } = LoadHudSprite("PromptFieldArrow");
        private static readonly Sprite PromptBanished = LoadHudSprite("PromptBanished");
        private static readonly Sprite[] PlayerZoneIcons = LoadZoneIcons("Player");
        private static readonly Sprite[] OpponentZoneIcons = LoadZoneIcons("Opponent");
        internal static Sprite PhaseBannerPlayer { get; } = LoadHudSprite("PhaseBannerPlayer");
        internal static Sprite PhaseBannerOpponent { get; } = LoadHudSprite("PhaseBannerOpponent");
        internal static Sprite WinnerCrown { get; } = LoadHudSprite("WinnerCrown");
        internal static Sprite DuelStart { get; } = LoadHudSprite("DuelStart");
        internal static Sprite DuelVictory { get; } = LoadHudSprite("DuelVictory");
        internal static Sprite DuelDefeat { get; } = LoadHudSprite("DuelDefeat");
        internal static Sprite DuelDraw { get; } = LoadHudSprite("DuelDraw");
        internal static Sprite ZoneIcon(uint location, bool opponent)
        {
            var icons = opponent ? OpponentZoneIcons : PlayerZoneIcons;
            return location switch
            {
                YgoConstants.LOCATION_HAND => icons[0],
                YgoConstants.LOCATION_DECK => icons[1],
                YgoConstants.LOCATION_GRAVE => icons[2],
                YgoConstants.LOCATION_EXTRA => icons[3],
                YgoConstants.LOCATION_REMOVED => icons[4],
                YgoConstants.LOCATION_ONFIELD or YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE or
                    YgoConstants.LOCATION_FZONE or YgoConstants.LOCATION_PZONE => icons[5],
                _ => null,
            };
        }

        internal static Sprite PromptZoneIcon(uint location, bool opponent) =>
            (location == YgoConstants.LOCATION_REMOVED ? PromptBanished : null) ?? ZoneIcon(location, opponent);

        private static Sprite[] LoadZoneIcons(string side) => new[]
        {
            LoadHudSprite("ZoneHand" + side), LoadHudSprite("ZoneDeck" + side), LoadHudSprite("ZoneGrave" + side),
            LoadHudSprite("ZoneExtra" + side), LoadHudSprite("ZoneBanished" + side), LoadHudSprite("ZoneField" + side),
        };

        private static Sprite LoadHudSprite(string name, Vector4 border = default) =>
            AsSprite(LoadTexture(Path.Combine("Hud", name + ".png"), TextureWrapMode.Clamp), border);

        internal static Sprite Effect(DuelEffectKind kind) =>
            CardArtLoader.GetDuelVisualsSprite(Path.Combine("Effects", kind + ".png"));

        internal static Sprite SummonTexture(DuelEffectKind kind)
        {
            string name = kind switch
            {
                DuelEffectKind.Fusion => "Fusion_Vortex",
                DuelEffectKind.Synchro => "Synchro_Rings",
                DuelEffectKind.Xyz => "Xyz_Galaxy",
                DuelEffectKind.Link => "Link_Circuit",
                _ => null,
            };
            return name == null ? null : Effect(kind) ??
                CardArtLoader.GetDuelVisualsSprite(Path.Combine("Effects", name + ".png"));
        }

        internal static bool CanDisplayCutIn(uint artCode, int artVariant) => GetCutIn(artCode, artVariant) != null;

        internal static void ReloadCutIns()
        {
            TrimCutInCache(0);
            UnreadableCutIns.Clear();
        }

        internal static Sprite RetainCutIn(uint artCode, int artVariant)
        {
            var cutIn = GetCutIn(artCode, artVariant);
            if (cutIn == null) return null;
            cutIn.Readers++;
            return cutIn.Sprite;
        }

        internal static void ReleaseCutIn(Sprite sprite)
        {
            if (sprite == null) return;
            foreach (var cutIn in CutInCache.Values)
            {
                if (cutIn.Sprite != sprite) continue;
                cutIn.Readers--;
                break;
            }
            TrimCutInCache(CutInCacheLimit);
        }

        private static CutInEntry GetCutIn(uint artCode, int artVariant)
        {
            if (artCode == 0 || artVariant < -1) return null;
            // Bundled card art uses -1. Its matching default cut-in is stored as variant 0.
            string file = $"{artCode}-{(artVariant == -1 ? 0 : artVariant)}.png";
            return ReadCutIn(Path.Combine(CardArtLoader.ActiveDuelVisualsPath, "CutIns", file)) ??
                ReadCutIn(Path.Combine(CardArtLoader.PackagedDuelVisualsPath, "CutIns", file));
        }

        private static CutInEntry ReadCutIn(string path)
        {
            if (CutInCache.TryGetValue(path, out var cached))
            {
                cached.LastUse = ++_cutInUse;
                return cached;
            }

            if (UnreadableCutIns.Contains(path) || !File.Exists(path)) return null;
            var texture = LoadTextureFile(path, TextureWrapMode.Clamp, forCutIn: true);
            var sprite = texture == null ? null : CreateCutInSprite(texture);
            if (sprite == null)
            {
                UnreadableCutIns.Add(path);
                return null;
            }
            TrimCutInCache(CutInCacheLimit - 1);
            var entry = new CutInEntry { Sprite = sprite, LastUse = ++_cutInUse };
            CutInCache.Add(path, entry);
            return entry;
        }

        private static Sprite CreateCutInSprite(Texture2D texture)
        {
            int width = texture.width;
            int height = texture.height;
            var pixels = texture.GetPixels32();
            int left = width, right = -1, bottom = height, top = -1;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (pixels[row + x].a == 0) continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    bottom = Math.Min(bottom, y);
                    top = Math.Max(top, y);
                }
            }
            if (right < left)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
                else UnityEngine.Object.DestroyImmediate(texture);
                return null;
            }
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(left, bottom, right - left + 1, top - bottom + 1),
                new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
        }

        private static void TrimCutInCache(int capacity)
        {
            while (CutInCache.Count > capacity)
            {
                string oldestPath = null;
                CutInEntry oldest = null;
                foreach (var pair in CutInCache)
                {
                    if (pair.Value.Readers != 0 || oldest != null && pair.Value.LastUse >= oldest.LastUse) continue;
                    oldestPath = pair.Key;
                    oldest = pair.Value;
                }
                if (oldest == null) return;
                CutInCache.Remove(oldestPath);
                var texture = oldest.Sprite.texture;
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(oldest.Sprite);
                    UnityEngine.Object.Destroy(texture);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(oldest.Sprite);
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        private static Sprite AsSprite(Texture2D texture, Vector4 border = default) => texture == null ? null :
            Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(.5f, .5f),
                100f, 0, SpriteMeshType.FullRect, border);

        private static Texture2D LoadTexture(string name, TextureWrapMode wrapMode) =>
            LoadTextureFile(Path.Combine(CardArtLoader.ActiveDuelVisualsPath, name), wrapMode) ??
            LoadTextureFile(Path.Combine(CardArtLoader.PackagedDuelVisualsPath, name), wrapMode);

        private static Texture2D LoadTextureFile(string path, TextureWrapMode wrapMode, bool forCutIn = false)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var bytes = File.ReadAllBytes(path);
                string name = Path.GetFileNameWithoutExtension(path);
                if (forCutIn && name == "84815190-0")
                {
                    // One released import contains separated pieces of Baronne. A replacement can use the same filename.
                    using var hash = SHA256.Create();
                    if (BitConverter.ToString(hash.ComputeHash(bytes)) ==
                        "43-71-AC-BE-98-D4-0D-0A-61-A3-8E-76-0C-BD-68-0D-D3-1C-ED-AD-15-75-41-6A-56-F2-F7-39-B4-CF-0B-9A")
                        return null;
                }
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = name,
                    wrapMode = wrapMode,
                    filterMode = FilterMode.Bilinear,
                };
                if (texture.LoadImage(bytes, markNonReadable: !forCutIn)) return texture;
                if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
                else UnityEngine.Object.DestroyImmediate(texture);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[DuelVisualAssets] Cannot read {Path.GetFileName(path)}: {error.Message}");
            }
            return null;
        }

        internal static AudioClip LoadSound(string name)
        {
            string file = name + ".wav";
            return ReadSound(Path.Combine(Application.streamingAssetsPath, "TagForce", "Audio", file), name) ??
                ReadSound(Path.Combine(CardArtLoader.ActiveDuelVisualsPath, "Audio", file), name) ??
                ReadSound(Path.Combine(CardArtLoader.PackagedDuelVisualsPath, "Audio", file), name);
        }

        internal static string FindSummonVideo(DuelEffectKind kind) =>
            (CardDataPack.Root == null ? null : FindSummonVideo(kind, CardArtLoader.ActiveDuelVisualsPath, CardDataPack.Root)) ??
            FindSummonVideo(kind, CardArtLoader.ActiveDuelVisualsPath, Application.streamingAssetsPath);

        internal static string FindSummonVideo(DuelEffectKind kind, string visualPack, string streamingAssets)
        {
            if (!DuelEffectGraphic.HasSummonSignature(kind)) return null;
            string name = kind + ".mp4";
            string local = Path.Combine(visualPack, "Effects", name);
            if (File.Exists(local)) return local;

            // Packaged Android files are addressed inside the APK. Check the archive before handing Unity a URL.
            if (streamingAssets.StartsWith("jar:file:", StringComparison.OrdinalIgnoreCase))
            {
                int separator = streamingAssets.IndexOf("!/", StringComparison.Ordinal);
                if (separator < 0 || !Uri.TryCreate(streamingAssets.Substring(4, separator - 4),
                    UriKind.Absolute, out var archive) || !archive.IsFile) return null;
                string root = streamingAssets.Substring(separator + 2).TrimEnd('/');
                try
                {
                    using var stream = File.OpenRead(archive.LocalPath);
                    using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                    foreach (string directory in new[] { "DuelVisuals/Effects", "Effects" })
                        if (zip.GetEntry($"{root}/{directory}/{name}") != null)
                            return $"{streamingAssets.TrimEnd('/')}/{directory}/{name}";
                    return null;
                }
                catch (Exception error) when (error is IOException || error is InvalidDataException ||
                    error is UnauthorizedAccessException)
                {
                    return null;
                }
            }

            string packaged = Path.Combine(streamingAssets, "DuelVisuals", "Effects", name);
            if (File.Exists(packaged)) return packaged;
            string standalone = Path.Combine(streamingAssets, "Effects", name);
            return File.Exists(standalone) ? standalone : null;
        }

        internal static AudioClip ReadSound(string path, string name)
        {
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
