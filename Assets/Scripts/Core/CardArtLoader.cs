using System;
using System.Collections.Generic;
using System.IO;
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
        private static readonly Dictionary<uint, (Sprite sprite, LinkedListNode<uint> node)> _spriteCache =
            new Dictionary<uint, (Sprite, LinkedListNode<uint>)>(MaxCacheCount);
        private static readonly LinkedList<uint> _lru = new LinkedList<uint>();
        private static readonly Dictionary<uint, int> _visibleArtwork = new Dictionary<uint, int>();
        private static Sprite _fallbackArt;
        private static Sprite _cardBackSprite;

        public static void Initialize(string streamingAssetsPath)
        {
            if (_index != null) return;
            _streamingDir = streamingAssetsPath;
            _index = new Dictionary<uint, ArtEntry>(16000);
            _packStreams = new Dictionary<int, FileStream>();

            string idxPath = Path.Combine(streamingAssetsPath, "cardart.idx");
            if (!File.Exists(idxPath))
            {
                Debug.LogWarning($"[CardArtLoader] Missing {idxPath}");
                return;
            }

            byte[] data = File.ReadAllBytes(idxPath);
            if (data.Length < 12) return;
            uint count = BitConverter.ToUInt32(data, 8);
            int pos = 12;
            for (uint i = 0; i < count && pos + 16 <= data.Length; i++, pos += 16)
            {
                uint pw = BitConverter.ToUInt32(data, pos);
                int pid = BitConverter.ToInt32(data, pos + 4);
                uint off = BitConverter.ToUInt32(data, pos + 8);
                uint len = BitConverter.ToUInt32(data, pos + 12);
                _index[pw] = new ArtEntry { packId = pid, offset = off, length = len };
            }
            Debug.Log($"[CardArtLoader] Indexed {_index.Count} card artworks.");
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

        public static Sprite GetArtSprite(uint code)
        {
            if (code == 0) return GetCardBackSprite();
            if (_spriteCache.TryGetValue(code, out var cached))
            {
                _lru.Remove(cached.node);
                if (cached.sprite != null)
                {
                    _lru.AddLast(cached.node);
                    return cached.sprite;
                }
                _spriteCache.Remove(code);
            }

            uint lookupCode = code;
            ArtEntry entry = default;
            bool found = false;
            if (_index != null && _index.TryGetValue(lookupCode, out entry))
            {
                found = true;
            }
            else
            {
                var info = CardDatabase.Get(code);
                if (info != null && info.alias != 0 && _index != null && _index.TryGetValue(info.alias, out var aliasEntry))
                {
                    entry = aliasEntry;
                    found = true;
                }
                else if (info != null && (info.type & YgoConstants.TYPE_TOKEN) != 0 && _index != null)
                {
                    for (uint sub = 1; sub <= 5; sub++)
                    {
                        if (code >= sub && _index.TryGetValue(code - sub, out var genEntry))
                        {
                            entry = genEntry;
                            found = true;
                            break;
                        }
                    }
                }
            }

            if (!found) return GetFallbackArt();

            try
            {
                if (!_packStreams.TryGetValue(entry.packId, out var fs))
                {
                    string packPath = Path.Combine(_streamingDir, $"cardart_{entry.packId}.pack");
                    if (!File.Exists(packPath)) return GetFallbackArt();
                    fs = new FileStream(packPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    _packStreams[entry.packId] = fs;
                }

                byte[] jpg = new byte[entry.length];
                fs.Seek(entry.offset, SeekOrigin.Begin);
                int readTotal = 0;
                while (readTotal < jpg.Length)
                {
                    int n = fs.Read(jpg, readTotal, jpg.Length - readTotal);
                    if (n <= 0) break;
                    readTotal += n;
                }

                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!tex.LoadImage(jpg))
                {
                    UnityEngine.Object.Destroy(tex);
                    return GetFallbackArt();
                }
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);

                TrimArtwork(MaxCacheCount - 1);
                _spriteCache[code] = (sp, _lru.AddLast(code));
                return sp;
            }
            catch
            {
                return GetFallbackArt();
            }
        }

        internal static Sprite RetainArtwork(uint code)
        {
            var sprite = GetArtSprite(code);
            if (_spriteCache.ContainsKey(code))
            {
                _visibleArtwork.TryGetValue(code, out int views);
                _visibleArtwork[code] = views + 1;
            }
            return sprite;
        }

        internal static void ReleaseArtwork(uint code)
        {
            if (!_visibleArtwork.TryGetValue(code, out int views)) return;
            if (views > 1) _visibleArtwork[code] = views - 1;
            else _visibleArtwork.Remove(code);
            TrimArtwork(MaxCacheCount);
        }

        private static void TrimArtwork(int capacity)
        {
            var node = _lru.First;
            while (_spriteCache.Count > capacity && node != null)
            {
                var next = node.Next;
                uint code = node.Value;
                if (!_visibleArtwork.ContainsKey(code))
                {
                    DestroyArtwork(_spriteCache[code].sprite);
                    _spriteCache.Remove(code);
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
            _index = null;
        }
    }
}
