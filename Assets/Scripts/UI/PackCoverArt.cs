using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal static class PackCoverArt
    {
        // Offline HD wrapper textures from Master Duel, keyed by the artwork's card passcode.
        // Entry 0 contains the transparent title mark for locally composed wrappers.
        private const string CoverFileName = "packcovers.pack";
        private const uint CoverMagic = 0x43504759; // YGPC
        private const int CoverWidth = 172;
        private const int CoverHeight = 344;
        private const int MaxCacheCount = 32;
        private static readonly Color[] WrapperColors =
        {
            new Color(.20f, .43f, .78f), new Color(.46f, .28f, .71f), new Color(.17f, .56f, .60f),
            new Color(.67f, .34f, .49f), new Color(.77f, .52f, .25f), new Color(.29f, .53f, .41f),
            new Color(.42f, .52f, .68f), new Color(.72f, .28f, .25f),
        };

        private static Dictionary<uint, (int offset, int length)> _entries;
        private static Texture2D _titleMark;
        private static readonly Dictionary<string, (Sprite sprite, LinkedListNode<string> node)> Cache = new();
        private static readonly LinkedList<string> Recent = new();

        internal static Sprite Get(string packId, uint featuredCode, IReadOnlyList<uint> cardPool = null)
        {
            if (string.IsNullOrEmpty(packId)) return null;
            if (Cache.TryGetValue(packId, out var cached))
            {
                Recent.Remove(cached.node);
                Recent.AddLast(cached.node);
                return cached.sprite;
            }

            _entries ??= ReadIndex();
            uint sourceCode = FindSourceCover(packId, featuredCode, cardPool);
            Texture2D texture = sourceCode != 0 ? ReadTexture(sourceCode, true) : null;
            texture ??= ComposeCover(packId, featuredCode, cardPool);
            texture.name = packId + "-wrapper";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = packId + "-cover";

            if (Cache.Count >= MaxCacheCount)
            {
                string oldId = Recent.First.Value;
                var oldCover = Cache[oldId].sprite;
                Cache.Remove(oldId);
                Recent.RemoveFirst();
                var oldTexture = oldCover.texture;
                Release(oldCover);
                Release(oldTexture);
            }
            Cache.Add(packId, (sprite, Recent.AddLast(packId)));
            return sprite;
        }

        private static uint FindSourceCover(string packId, uint featuredCode, IReadOnlyList<uint> cardPool)
        {
            uint cover = CoverCode(featuredCode);
            if (cover != 0 || cardPool == null) return cover;

            const string archetypePrefix = "archetype-";
            ushort theme = 0;
            bool isArchetype = packId.StartsWith(archetypePrefix, StringComparison.Ordinal) &&
                ushort.TryParse(packId.Substring(archetypePrefix.Length), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out theme);
            if (!isArchetype && packId != "support-staples") return 0;

            int bestRarity = -1;
            foreach (uint code in cardPool)
            {
                uint candidate = CoverCode(code);
                if (candidate == 0) continue;
                if (isArchetype && !MatchesTheme(CardDatabase.Get(code)?.setcode ?? 0, theme)) continue;

                int rarity = (int)CardRarityCatalog.GetRarity(code);
                if (rarity <= bestRarity) continue;
                cover = candidate;
                bestRarity = rarity;
            }
            return cover;
        }

        private static bool MatchesTheme(ulong cardSetcodes, ushort theme)
        {
            while (cardSetcodes != 0)
            {
                if ((cardSetcodes & 0xFFFF) == (uint)theme || (cardSetcodes & 0x0FFF) == (uint)(theme & 0x0FFF))
                    return true;
                cardSetcodes >>= 16;
            }
            return false;
        }

        private static uint CoverCode(uint passcode)
        {
            if (passcode == 0 || _entries.Count == 0) return 0;
            if (_entries.ContainsKey(passcode)) return passcode;
            uint alias = CardDatabase.Get(passcode)?.alias ?? 0;
            return alias != 0 && _entries.ContainsKey(alias) ? alias : 0;
        }

        private static Dictionary<uint, (int offset, int length)> ReadIndex()
        {
            var entries = new Dictionary<uint, (int, int)>();
            try
            {
                using var reader = new BinaryReader(File.OpenRead(Path.Combine(Application.streamingAssetsPath, CoverFileName)));
                if (reader.BaseStream.Length < 12 || reader.ReadUInt32() != CoverMagic || reader.ReadUInt32() != 1)
                    return entries;
                int count = reader.ReadInt32();
                if (count < 1 || count > (reader.BaseStream.Length - 12) / 12) return entries;
                long dataStart = 12L + count * 12;
                for (int i = 0; i < count; i++)
                {
                    uint code = reader.ReadUInt32();
                    int offset = reader.ReadInt32();
                    int length = reader.ReadInt32();
                    if (offset >= dataStart && length > 0 && length <= 512 * 1024 &&
                        (long)offset + length <= reader.BaseStream.Length)
                        entries.TryAdd(code, (offset, length));
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[PackCoverArt] Could not load {CoverFileName}: {error.Message}");
            }
            return entries;
        }

        private static Texture2D ReadTexture(uint passcode, bool releasePixels)
        {
            if (!_entries.TryGetValue(passcode, out var entry)) return null;
            try
            {
                using var reader = new BinaryReader(File.OpenRead(Path.Combine(Application.streamingAssetsPath, CoverFileName)));
                reader.BaseStream.Position = entry.offset;
                byte[] data = reader.ReadBytes(entry.length);
                if (data.Length != entry.length) return null;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(data, releasePixels)) return texture;
                Release(texture);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[PackCoverArt] Could not read pack cover for {passcode}: {error.Message}");
            }
            return null;
        }

        private static Texture2D ComposeCover(string packId, uint featuredCode, IReadOnlyList<uint> cardPool)
        {
            uint hash = 2166136261;
            foreach (char letter in packId) hash = unchecked((hash ^ letter) * 16777619);
            Color accent = WrapperColors[hash % (uint)WrapperColors.Length];
            var pixels = new Color32[CoverWidth * CoverHeight];
            for (int y = 0; y < CoverHeight; y++)
            {
                for (int x = 0; x < CoverWidth; x++)
                {
                    int edge = Mathf.Min(x, CoverWidth - 1 - x);
                    if (edge < 2 || (edge < 3 && (y < 4 || y >= CoverHeight - 4))) continue;

                    float shine = .48f + .13f * Mathf.Cos(.14f * x + .012f * y) +
                        .07f * Mathf.Cos(.039f * (x + y)) + .32f * y / CoverHeight;
                    if (y < 17 || y >= CoverHeight - 17)
                        shine = (y & 3) == 0 ? 1.16f : (x & 3) == 0 ? .69f : .92f;
                    else if (edge <= 5)
                        shine += edge == 3 ? .38f : -.15f;

                    Color foil = Color.Lerp(new Color(.022f, .048f, .095f), accent * 1.2f, shine);
                    foil.a = 1f;
                    pixels[y * CoverWidth + x] = foil;
                }
            }

            var illustration = FindIllustration(featuredCode, cardPool);
            if (illustration != null) BlendImage(pixels, illustration, new RectInt(5, 48, 162, 239), fade: true);
            else DrawFoilCrest(pixels, accent, hash);

            _titleMark ??= ReadTexture(0, false);
            if (_titleMark != null) BlendImage(pixels, _titleMark, new RectInt(14, 26, 144, 63), fade: false);

            var texture = new Texture2D(CoverWidth, CoverHeight, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
        }

        private static Texture2D FindIllustration(uint featuredCode, IReadOnlyList<uint> cardPool)
        {
            var fallback = CardArtLoader.GetFallbackArt().texture;
            Texture2D Load(uint code)
            {
                var texture = code != 0 ? CardArtLoader.GetArtSprite(code)?.texture : null;
                return texture != null && texture != fallback && texture.isReadable ? texture : null;
            }

            var artwork = Load(featuredCode);
            if (artwork != null || cardPool == null) return artwork;
            foreach (uint code in cardPool)
            {
                if (code == featuredCode) continue;
                artwork = Load(code);
                if (artwork != null) return artwork;
            }
            return null;
        }

        private static void DrawFoilCrest(Color32[] pixels, Color accent, uint hash)
        {
            var center = new Vector2(.5f, .53f);
            float angle = (hash % 60) * Mathf.Deg2Rad;
            Vector2 axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var light = Color.Lerp(accent, Color.white, .70f);
            for (int y = 64; y < 280; y++)
            for (int x = 8; x < CoverWidth - 8; x++)
            {
                var point = new Vector2(x / (float)CoverWidth, y / (float)CoverHeight) - center;
                point.y *= 1.36f;
                float radial = point.magnitude;
                float rotated = Mathf.Abs(Vector2.Dot(point, axis)) + Mathf.Abs(Vector2.Dot(point, Vector2.Perpendicular(axis)));
                float ring = Mathf.Max(1f - Mathf.Abs(radial - .258f) * 110f, 1f - Mathf.Abs(rotated - .34f) * 130f);
                float glint = Mathf.Clamp01(1f - rotated * 4f);
                int index = y * CoverWidth + x;
                pixels[index] = Color32.Lerp(pixels[index], light, Mathf.Max(Mathf.Clamp01(ring) * .78f, glint * glint * .72f));
            }
        }

        private static void BlendImage(Color32[] target, Texture2D source, RectInt area, bool fade)
        {
            var sourcePixels = source.GetPixels32();
            float ratio = Mathf.Max(area.width / (float)source.width, area.height / (float)source.height);
            float left = (source.width - area.width / ratio) * .5f;
            float bottom = (source.height - area.height / ratio) * .5f;
            for (int y = 0; y < area.height; y++)
            {
                int sourceY = Mathf.Clamp((int)(bottom + (y + .5f) / ratio), 0, source.height - 1);
                float fadeY = fade ? Mathf.Clamp01(Mathf.Min(y / 34f, (area.height - 1 - y) / 40f)) : 1f;
                for (int x = 0; x < area.width; x++)
                {
                    int sourceX = Mathf.Clamp((int)(left + (x + .5f) / ratio), 0, source.width - 1);
                    Color32 pixel = sourcePixels[sourceY * source.width + sourceX];
                    float fadeX = fade ? Mathf.Clamp01(Mathf.Min(x, area.width - 1 - x) / 8f) : 1f;
                    int index = (area.y + y) * CoverWidth + area.x + x;
                    target[index] = Color32.Lerp(target[index], pixel, pixel.a / 255f * fadeX * fadeY);
                    target[index].a = 255;
                }
            }
        }

        private static void Release(UnityEngine.Object resource)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(resource);
            else UnityEngine.Object.DestroyImmediate(resource);
        }
    }
}
