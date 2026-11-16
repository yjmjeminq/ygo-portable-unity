using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace YGOPortable.Core
{
    public static class CardArtLoader
    {
        private struct ArtEntry
        {
            public int packId;
            public uint offset;
            public uint length;
        }

        private static Dictionary<uint, ArtEntry> _index;
        private static Dictionary<int, FileStream> _packStreams;
        private static string _streamingDir;

        private const int MaxCacheCount = 500;
        private const int TokenArtPackId = -1;
        private const int DuelArtPackId = -2;
        private const int BundledArtwork = -1;
        private const string TokenArtFileName = "tokenart.pack";
        private const string DuelArtFileName = "cardart.pack";
        private static readonly Dictionary<(uint code, int variant), ArtEntry> _duelArt = new();
        private static readonly Dictionary<uint, List<int>> _duelVariants = new();
        private static readonly Dictionary<(uint, int), (Sprite sprite, LinkedListNode<(uint, int)> node)> _spriteCache =
            new(MaxCacheCount);
        private static readonly LinkedList<(uint, int)> _lru = new();
        private static readonly Dictionary<(uint, int), int> _visibleArtwork = new();
        private static Sprite _fallbackArt;
        private static Sprite _cardBackSprite;
        internal static string LocalDuelVisualsPath => Path.Combine(Application.persistentDataPath, "DuelVisuals");

        public static void Initialize(string streamingAssetsPath)
        {
            if (_index != null) return;
            _streamingDir = streamingAssetsPath;
            _index = new Dictionary<uint, ArtEntry>(16000);
            _packStreams = new Dictionary<int, FileStream>();

            string idxPath = Path.Combine(streamingAssetsPath, "cardart.idx");
            if (!ReadArtIndex(idxPath))
                Debug.LogWarning($"[CardArtLoader] Missing or invalid {idxPath}");
            ReadArtIndex(Path.Combine(streamingAssetsPath, TokenArtFileName));
            ReadDuelArtIndex();
            Debug.Log($"[CardArtLoader] Indexed {_index.Count} card artworks.");
        }

        private static void ReadDuelArtIndex()
        {
            string packPath = Path.Combine(LocalDuelVisualsPath, DuelArtFileName);
            if (!File.Exists(packPath)) return;
            FileStream stream = null;
            try
            {
                stream = File.OpenRead(packPath);
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                if (stream.Length < 12 || reader.ReadUInt32() != 0x5241444D || reader.ReadUInt32() != 1) return;
                uint count = reader.ReadUInt32();
                long indexEnd = 12L + count * 16L;
                if (indexEnd > stream.Length) return;
                for (uint i = 0; i < count; i++)
                {
                    uint code = reader.ReadUInt32();
                    int variant = reader.ReadInt32();
                    uint offset = reader.ReadUInt32();
                    uint length = reader.ReadUInt32();
                    if (code == 0 || variant < 0 || offset < indexEnd || length == 0 ||
                        (long)offset + length > stream.Length) continue;
                    if (!_duelArt.TryAdd((code, variant), new ArtEntry
                        { packId = DuelArtPackId, offset = offset, length = length })) continue;
                    if (!_duelVariants.TryGetValue(code, out var variants))
                        _duelVariants[code] = variants = new List<int>();
                    variants.Add(variant);
                }
                foreach (var (code, variants) in _duelVariants)
                {
                    variants.Sort();
                    if (variants[0] != 0 && TryGetArtEntry(code, out _)) variants.Insert(0, 0);
                }
                _packStreams[DuelArtPackId] = stream;
                stream = null;
                Debug.Log($"[CardArtLoader] Indexed {_duelArt.Count} local duel artworks for {_duelVariants.Count} cards.");
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                _duelArt.Clear();
                _duelVariants.Clear();
                Debug.LogWarning($"[CardArtLoader] Cannot read local duel artwork index: {error.Message}");
            }
            finally
            {
                stream?.Dispose();
            }
        }

        internal static IReadOnlyList<int> GetDuelArtworkChoices(uint code)
        {
            if (_duelVariants.TryGetValue(code, out var variants)) return variants;
            return Array.Empty<int>();
        }

        internal static int GetDuelArtworkVariant(uint code)
        {
            if (!_duelVariants.TryGetValue(code, out var variants)) return BundledArtwork;
            if (variants.Count == 1) return variants[0];
            int variant = PlayerPrefs.GetInt($"DuelArtwork.{code}", variants[0]);
            return variants.Contains(variant) ? variant : variants[0];
        }

        internal static void SetDuelArtworkVariant(uint code, int variant)
        {
            if (_duelVariants.TryGetValue(code, out var variants) && variants.Contains(variant))
                PlayerPrefs.SetInt($"DuelArtwork.{code}", variant);
        }

        private static bool ReadArtIndex(string path)
        {
            if (!File.Exists(path)) return false;
            using var reader = new BinaryReader(File.OpenRead(path));
            long length = reader.BaseStream.Length;
            if (length < 12 || reader.ReadUInt32() != 0x494F4759 || reader.ReadUInt32() != 1)
                return false;
            uint count = reader.ReadUInt32();
            if (count > (length - 12) / 16) return false;
            for (uint i = 0; i < count; i++)
            {
                uint code = reader.ReadUInt32();
                var entry = new ArtEntry { packId = reader.ReadInt32(), offset = reader.ReadUInt32(), length = reader.ReadUInt32() };
                _index.TryAdd(code, entry);
            }
            return true;
        }

        private static bool TryGetArtEntry(uint code, out ArtEntry entry)
        {
            entry = default;
            if (_index == null) return false;
            if (_index.TryGetValue(code, out entry)) return true;

            var info = CardDatabase.Get(code);
            if (info == null || info.alias == 0) return false;
            if ((info.type & YgoConstants.TYPE_TOKEN) != 0)
            {
                var sharedToken = CardDatabase.Get(info.alias);
                if (sharedToken == null || (sharedToken.type & YgoConstants.TYPE_TOKEN) == 0 ||
                    !string.Equals(info.name, sharedToken.name, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return _index.TryGetValue(info.alias, out entry);
        }

        public static Sprite GetCardBackSprite()
        {
            if (_cardBackSprite != null) return _cardBackSprite;
            _cardBackSprite = Resources.Load<Sprite>("LOTD/Frames/card_back");
            if (_cardBackSprite == null)
            {
                var tex = Resources.Load<Texture2D>("LOTD/Frames/card_back");
                if (tex != null)
                    _cardBackSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            return _cardBackSprite;
        }

        public static Sprite GetArtSprite(uint code) => GetArtSprite(code, BundledArtwork);

        private static Sprite GetArtSprite(uint code, int variant)
        {
            if (code == 0) return GetCardBackSprite();
            var key = (code, variant);
            if (_spriteCache.TryGetValue(key, out var cached))
            {
                _lru.Remove(cached.node);
                if (cached.sprite != null)
                {
                    _lru.AddLast(cached.node);
                    return cached.sprite;
                }
                _spriteCache.Remove(key);
            }

            Sprite sprite = null;
            if (variant >= 0 && _duelArt.TryGetValue(key, out var local)) sprite = DecodeArtwork(local);
            if (sprite == null && TryGetArtEntry(code, out var bundled)) sprite = DecodeArtwork(bundled);
            if (sprite == null) return GetFallbackArt();
            TrimArtwork(MaxCacheCount - 1);
            _spriteCache[key] = (sprite, _lru.AddLast(key));
            return sprite;
        }

        private static Sprite DecodeArtwork(ArtEntry entry)
        {
            try
            {
                if (!_packStreams.TryGetValue(entry.packId, out var fs))
                {
                    string packPath = entry.packId switch
                    {
                        TokenArtPackId => Path.Combine(_streamingDir, TokenArtFileName),
                        _ => Path.Combine(_streamingDir, $"cardart_{entry.packId}.pack"),
                    };
                    if (!File.Exists(packPath)) return null;
                    fs = new FileStream(packPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    _packStreams[entry.packId] = fs;
                }

                byte[] jpg = new byte[entry.length];
                fs.Seek(entry.offset, SeekOrigin.Begin);
                int readTotal = 0;
                while (readTotal < jpg.Length)
                {
                    int n = fs.Read(jpg, readTotal, jpg.Length - readTotal);
                    if (n <= 0) return null;
                    readTotal += n;
                }

                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!tex.LoadImage(jpg))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            catch
            {
                return null;
            }
        }

        internal static Sprite RetainArtwork(uint code, int variant = BundledArtwork)
        {
            var sprite = GetArtSprite(code, variant);
            var key = (code, variant);
            if (_spriteCache.ContainsKey(key))
            {
                _visibleArtwork.TryGetValue(key, out int views);
                _visibleArtwork[key] = views + 1;
            }
            return sprite;
        }

        internal static void ReleaseArtwork(uint code, int variant = BundledArtwork)
        {
            var key = (code, variant);
            if (!_visibleArtwork.TryGetValue(key, out int views)) return;
            if (views > 1) _visibleArtwork[key] = views - 1;
            else _visibleArtwork.Remove(key);
            TrimArtwork(MaxCacheCount);
        }

        private static void TrimArtwork(int capacity)
        {
            var node = _lru.First;
            while (_spriteCache.Count > capacity && node != null)
            {
                var next = node.Next;
                var key = node.Value;
                if (!_visibleArtwork.ContainsKey(key))
                {
                    DestroyArtwork(_spriteCache[key].sprite);
                    _spriteCache.Remove(key);
                    _lru.Remove(node);
                }
                node = next;
            }
        }

        private static void DestroyArtwork(Sprite sprite)
        {
            if (sprite == null) return;
            var texture = sprite.texture;
            UnityEngine.Object.Destroy(sprite);
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }

        public static Sprite GetFallbackArt()
        {
            if (_fallbackArt != null) return _fallbackArt;
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var cols = new Color32[64 * 64];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(28, 42, 68, 255);
            tex.SetPixels32(cols);
            tex.Apply();
            _fallbackArt = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
            return _fallbackArt;
        }

        public static void Shutdown()
        {
            if (_packStreams != null)
            {
                foreach (var fs in _packStreams.Values) fs?.Dispose();
                _packStreams.Clear();
            }
            foreach (var entry in _spriteCache.Values)
                DestroyArtwork(entry.sprite);
            _spriteCache.Clear();
            _lru.Clear();
            _visibleArtwork.Clear();
            _duelArt.Clear();
            _duelVariants.Clear();
            _index = null;
        }
    }
}
