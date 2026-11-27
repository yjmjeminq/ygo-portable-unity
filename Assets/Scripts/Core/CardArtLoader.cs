using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private static string _localDuelVisualsRoot;
        private static string _activeDuelVisualsPath;

        private const int MaxCacheCount = 500;
        private const int TokenArtPackId = -1;
        private const int DuelArtPackId = -2;
        private const int SupplementalArtPackId = -3;
        private const int PackagedDuelArtPackId = -4;
        private const int SourceOverframePackId = -5;
        private const int PackagedSourceOverframePackId = -6;
        private const int StoryTrackArtId = -7;
        private const string StoryTrackArtFile = "TagForce/5DS/DuelCards/speed_world_2.png";
        private const int BundledArtwork = -1;
        internal const int SourceOverframeFace = -2;
        private static readonly Dictionary<uint, (uint Code, int Variant)[]> ArtworkChoicesByFamily = new();
        private static readonly Dictionary<uint, (uint Code, int Variant)> SelectedArtworkByFamily = new();
        private const string TokenArtFileName = "tokenart.pack";
        internal const string DuelArtFileName = "cardart.pack";
        internal const string SourceOverframeFileName = "overframeart.pack";
        private const string SupplementalArtFileName = "supplementalart.pack";
        private static readonly Dictionary<(uint code, int variant), ArtEntry> _duelArt = new();
        private static readonly Dictionary<uint, List<int>> _duelVariants = new();
        private static readonly Dictionary<uint, HashSet<int>> _overframeVariants = new();
        private static readonly Dictionary<uint, (int Illustration, ArtEntry Face)> _sourceOverframes = new();
        private static readonly Dictionary<(uint, int), (Sprite sprite, LinkedListNode<(uint, int)> node, (uint Code, int Variant) source)> _spriteCache =
            new(MaxCacheCount);
        private static readonly LinkedList<(uint, int)> _lru = new();
        private static readonly Dictionary<(uint, int), int> _visibleArtwork = new();
        private static readonly Dictionary<string, Sprite> DuelVisualSprites = new(StringComparer.Ordinal);
        private static Sprite _fallbackArt;
        private static Sprite _cardBackSprite;
        internal const string CardSleeveResource = "Duel/CardSleeve";
        internal const string CardSleeveFile = "Hud/DeckBack.png";
        internal static string LocalDuelVisualsPath => Path.Combine(Application.persistentDataPath, "DuelVisuals");
        internal static string ActiveDuelVisualsPath => _activeDuelVisualsPath ?? LocalDuelVisualsPath;
        internal static string PackagedDuelVisualsPath =>
            Path.Combine(_streamingDir ?? Application.streamingAssetsPath, "DuelVisuals");

        public static bool IsDuelArtActive => ActiveDuelArtCount > 0;
        public static int ActiveDuelArtCount => _duelArt.Count;
        internal static bool HasExternalDuelArt => _duelArt.Values.Any(entry => entry.packId == DuelArtPackId);
        internal static bool HasExternalDuelArtFile => File.Exists(Path.Combine(ActiveDuelVisualsPath, DuelArtFileName));

        public static void Reload()
        {
            string streamingAssetsPath = _streamingDir ?? Application.streamingAssetsPath;
            string localArtworkPath = _localDuelVisualsRoot ?? LocalDuelVisualsPath;
            Shutdown();
            Initialize(streamingAssetsPath, localArtworkPath);
        }

        public static void Initialize(string streamingAssetsPath) => Initialize(streamingAssetsPath, LocalDuelVisualsPath);

        internal static void Initialize(string streamingAssetsPath, string localArtworkPath)
        {
            if (_index != null) return;
            if (Application.platform == RuntimePlatform.IPhonePlayer)
                EnsureLocalDuelVisualsFolder(localArtworkPath);
            OverframeOffsetCatalog.Load(streamingAssetsPath);
            _streamingDir = streamingAssetsPath;
            _localDuelVisualsRoot = localArtworkPath;
            _activeDuelVisualsPath = ResolveLocalDuelVisuals(localArtworkPath);
            _index = new Dictionary<uint, ArtEntry>(16000);
            _packStreams = new Dictionary<int, FileStream>();

            string idxPath = Path.Combine(streamingAssetsPath, "cardart.idx");
            if (!ReadArtIndex(idxPath))
                Debug.LogWarning($"[CardArtLoader] Missing or invalid {idxPath}");
            ReadArtIndex(Path.Combine(streamingAssetsPath, TokenArtFileName));
            ReadArtIndex(Path.Combine(streamingAssetsPath, SupplementalArtFileName));
            var storyTrack = new FileInfo(Path.Combine(Application.streamingAssetsPath, StoryTrackArtFile));
            if (storyTrack.Exists && storyTrack.Length is > 0 and < int.MaxValue)
                _index[StoryGimmickRuntime.SpeedWorld] = new ArtEntry { packId = StoryTrackArtId, length = (uint)storyTrack.Length };
            ReadDuelArtIndex(Path.Combine(ActiveDuelVisualsPath, DuelArtFileName), DuelArtPackId);
            ReadDuelArtIndex(Path.Combine(PackagedDuelVisualsPath, DuelArtFileName), PackagedDuelArtPackId);
            ReadDuelArtIndex(Path.Combine(ActiveDuelVisualsPath, SourceOverframeFileName), SourceOverframePackId);
            ReadDuelArtIndex(Path.Combine(PackagedDuelVisualsPath, SourceOverframeFileName), PackagedSourceOverframePackId);
            foreach (var (code, variants) in _duelVariants)
            {
                variants.Sort();
                if (variants[0] != 0 && (_index.ContainsKey(code) || RelatedPrintings(code).Any(_index.ContainsKey)))
                    variants.Insert(0, 0);
            }
            if (_sourceOverframes.Count == 0)
            {
                ReadLegacyOverframeIndex(ActiveDuelVisualsPath);
                ReadLegacyOverframeIndex(PackagedDuelVisualsPath);
            }
            Debug.Log($"[CardArtLoader] Indexed {_index.Count} card artworks, {_duelArt.Count} duel artworks and {_sourceOverframes.Count} source Overframes.");
        }

        internal static void EnsureLocalDuelVisualsFolder(string root)
        {
            try { Directory.CreateDirectory(root); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[CardArtLoader] Cannot create the local DuelVisuals folder: {error.Message}");
            }
        }

        private static string ResolveLocalDuelVisuals(string root)
        {
            string imported = DuelVisualsZipImport.FindInstalledFolder(root);
            if (imported != null) return imported;

            // Files can wrap the ZIP's own DuelVisuals folder in another folder of the same name.
            string nested = Path.Combine(root, "DuelVisuals");
            return !File.Exists(Path.Combine(root, DuelArtFileName)) &&
                File.Exists(Path.Combine(nested, DuelArtFileName)) ? nested : root;
        }

        private static void ReadLegacyOverframeIndex(string directory)
        {
            string cutIns = Path.Combine(directory, "CutIns");
            if (!Directory.Exists(cutIns)) return;

            try
            {
                foreach (string path in Directory.EnumerateFiles(cutIns, "*.png"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    int separator = name.IndexOf('-');
                    if (separator <= 0 || !uint.TryParse(name.AsSpan(0, separator), out uint code) || code == 0 ||
                        !int.TryParse(name.AsSpan(separator + 1), out int variant) || variant < 0) continue;
                    if (!_overframeVariants.TryGetValue(code, out var variants))
                        _overframeVariants[code] = variants = new HashSet<int>();
                    variants.Add(variant);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[CardArtLoader] Cannot read overframe artwork index: {error.Message}");
            }
        }

        internal static bool HasSourceOverframe(uint code) => _sourceOverframes.ContainsKey(code);

        internal static bool SupportsOverframe(uint code) =>
            HasSourceOverframe(code) || _overframeVariants.ContainsKey(code);

        internal static bool SupportsOverframe(uint code, int variant) =>
            _sourceOverframes.TryGetValue(code, out var source) ? source.Illustration == variant :
                _overframeVariants.TryGetValue(code, out var choices) && choices.Contains(Math.Max(0, variant));

        internal static int FirstOverframeVariant(uint code) =>
            _sourceOverframes.TryGetValue(code, out var source) ? source.Illustration :
                _overframeVariants.TryGetValue(code, out var choices) ? choices.Min() : -1;

        internal static bool HasDuelArtwork(uint code, int variant) => _duelArt.ContainsKey((code, variant));

        private static void ReadDuelArtIndex(string packPath, int packId)
        {
            if (!File.Exists(packPath)) return;
            FileStream stream = null;
            try
            {
                stream = new FileStream(packPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                bool fullFace = packId is SourceOverframePackId or PackagedSourceOverframePackId;
                var entries = ReadDuelArtEntries(stream, packId, fullFace);
                if (entries == null) return;
                foreach (var (key, entry) in entries)
                {
                    if (fullFace)
                    {
                        _sourceOverframes.TryAdd(key.Code, (key.Variant, entry));
                        continue;
                    }
                    if (!_duelArt.TryAdd(key, entry)) continue;
                    if (!_duelVariants.TryGetValue(key.Code, out var variants))
                        _duelVariants[key.Code] = variants = new List<int>();
                    variants.Add(key.Variant);
                }
                _packStreams.Add(packId, stream);
                stream = null;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[CardArtLoader] Cannot read duel artwork index: {error.Message}");
            }
            finally
            {
                stream?.Dispose();
            }
        }

        internal static bool HasValidImportedArtwork(string path, bool overframe)
        {
            using var stream = File.OpenRead(path);
            return ReadDuelArtEntries(stream, overframe ? SourceOverframePackId : DuelArtPackId, overframe)?.Count > 0;
        }

        private static List<((uint Code, int Variant) Key, ArtEntry Entry)> ReadDuelArtEntries(
            Stream stream, int packId, bool fullFace)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            uint marker = fullFace ? 0x464F444Du : 0x5241444Du;
            if (stream.Length < 12 || reader.ReadUInt32() != marker || reader.ReadUInt32() != 1) return null;
            uint count = reader.ReadUInt32();
            long indexEnd = 12L + count * 16L;
            if (indexEnd > stream.Length) return null;
            var entries = new List<((uint Code, int Variant) Key, ArtEntry Entry)>();
            for (uint i = 0; i < count; i++)
            {
                uint code = reader.ReadUInt32();
                int variant = reader.ReadInt32();
                uint offset = reader.ReadUInt32();
                uint length = reader.ReadUInt32();
                if (code == 0 || variant < 0 || offset < indexEnd || length == 0 ||
                    (long)offset + length > stream.Length) continue;
                entries.Add(((code, variant), new ArtEntry { packId = packId, offset = offset, length = length }));
            }
            return entries;
        }

        internal static IReadOnlyList<int> GetDuelArtworkChoices(uint code)
        {
            if (_duelVariants.TryGetValue(code, out var variants) && !HasDedicatedTokenArt(code)) return variants;
            return Array.Empty<int>();
        }

        private static bool HasDedicatedTokenArt(uint code) =>
            _index?.ContainsKey(code) == true && ((CardDatabase.Get(code)?.type ?? 0) & YgoConstants.TYPE_TOKEN) != 0;

        internal static int GetDuelArtworkVariant(uint code)
        {
            if (!_duelVariants.TryGetValue(code, out var variants) || HasDedicatedTokenArt(code)) return BundledArtwork;
            if (variants.Count == 1) return variants[0];
            int variant = PlayerPrefs.GetInt($"DuelArtwork.{code}", variants[0]);
            return variants.Contains(variant) ? variant : variants[0];
        }

        internal static void SetDuelArtworkVariant(uint code, int variant)
        {
            if (_duelVariants.TryGetValue(code, out var variants) && variants.Contains(variant))
                PlayerPrefs.SetInt($"DuelArtwork.{code}", variant);
        }

        internal static (uint Code, int Variant)[] GetSelectableArtwork(uint code, bool overframe = false)
        {
            if (code == 0 || HasDedicatedTokenArt(code) || overframe && HasSourceOverframe(code))
                return Array.Empty<(uint, int)>();
            if (CollectionProgress.RequiresOwnedCardsForDecks && CollectionProgress.GetTotalOwnedCount(code) == 0 &&
                (!CardDatabase.TryGetDeckPrintings(code, out var printings) ||
                    !printings.Any(printing => CollectionProgress.GetTotalOwnedCount(printing.code) > 0)))
                return Array.Empty<(uint, int)>();
            return ArtworkChoicesForFamily(CardDatabase.GetDeckPrintingKey(code));
        }

        internal static (uint Code, int Variant) GetPreferredArtwork(uint code, int? savedVariant = null)
        {
            uint family = CardDatabase.GetDeckPrintingKey(code);
            if (!SelectedArtworkByFamily.TryGetValue(family, out var choice))
            {
                string saved = PlayerPrefs.GetString($"DuelArtwork.Family.{family}");
                var parts = saved.Split(':');
                if (parts.Length == 2 && uint.TryParse(parts[0], out uint artCode) &&
                    int.TryParse(parts[1], out int variant) && ArtworkChoicesForFamily(family).Contains((artCode, variant)))
                    choice = (artCode, variant);
                SelectedArtworkByFamily[family] = choice;
            }
            return choice.Code != 0 ? choice : (code, savedVariant ?? GetDuelArtworkVariant(code));
        }

        internal static bool SetDuelArtworkVariant(uint code, (uint Code, int Variant) choice)
        {
            if (!GetSelectableArtwork(code).Contains(choice)) return false;
            uint family = CardDatabase.GetDeckPrintingKey(code);
            SelectedArtworkByFamily[family] = choice;
            PlayerPrefs.SetString($"DuelArtwork.Family.{family}", $"{choice.Code}:{choice.Variant}");
            PlayerPrefs.Save();
            return true;
        }

        internal static int ArtworkChoiceIndex(uint code, (uint Code, int Variant)[] choices, int? savedVariant = null)
        {
            var current = GetPreferredArtwork(code, savedVariant);
            int index = Array.IndexOf(choices, current);
            if (index < 0 && current.Variant == BundledArtwork) index = Array.IndexOf(choices, (current.Code, 0));
            return Mathf.Max(0, index);
        }

        private static (uint Code, int Variant)[] ArtworkChoicesForFamily(uint family)
        {
            if (ArtworkChoicesByFamily.TryGetValue(family, out var cached)) return cached;
            if (_index == null) return Array.Empty<(uint, int)>();
            var printings = CardDatabase.TryGetDeckPrintings(family, out var cards)
                ? cards.Select(card => card.code) : new[] { family };
            var choices = new List<(uint, int)>();
            foreach (uint printing in printings)
            {
                if (!HasDuelArtwork(printing, 0) && _index.ContainsKey(printing))
                    choices.Add((printing, BundledArtwork));
                foreach (int variant in GetDuelArtworkChoices(printing))
                    if (HasDuelArtwork(printing, variant)) choices.Add((printing, variant));
            }
            cached = choices.ToArray();
            ArtworkChoicesByFamily[family] = cached;
            return cached;
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

        internal static IEnumerable<uint> RelatedPrintings(uint code)
        {
            if (CardDatabase.TryGetDeckPrintings(code, out var printings))
            {
                foreach (var printing in printings)
                    if (printing.code != code) yield return printing.code;
                yield break;
            }

            var info = CardDatabase.Get(code);
            if (info == null || info.alias == 0 || info.alias == code) yield break;
            var original = CardDatabase.Get(info.alias);
            if (original != null && ((original.type ^ info.type) & YgoConstants.TYPE_TOKEN) == 0 &&
                string.Equals(original.name, info.name, StringComparison.OrdinalIgnoreCase))
                yield return original.code;
        }

        public static Sprite GetCardBackSprite() => GetDuelVisualsSprite(CardSleeveFile) ?? (_cardBackSprite ??= LoadCardSleeve());

        internal static Sprite GetDuelVisualsSprite(string relativePath)
        {
            if (DuelVisualSprites.TryGetValue(relativePath, out var sprite)) return sprite;

            string local = Path.Combine(ActiveDuelVisualsPath, relativePath);
            string packaged = Path.Combine(PackagedDuelVisualsPath, relativePath);
            sprite = LoadDuelVisualsSprite(local);
            if (sprite == null && !string.Equals(local, packaged, StringComparison.Ordinal))
                sprite = LoadDuelVisualsSprite(packaged);
            DuelVisualSprites.Add(relativePath, sprite);
            return sprite;
        }

        private static Sprite LoadDuelVisualsSprite(string path)
        {
            if (!File.Exists(path)) return null;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[CardArtLoader] Cannot read {Path.GetFileName(path)}: {error.Message}");
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = Path.GetFileNameWithoutExtension(path),
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            if (texture.LoadImage(bytes, markNonReadable: true))
                return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * .5f);

            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
            Debug.LogWarning($"[CardArtLoader] Cannot decode {Path.GetFileName(path)}.");
            return null;
        }

        private static Sprite LoadCardSleeve()
        {
            var sleeve = Resources.Load<Sprite>(CardSleeveResource);
            if (sleeve != null) return sleeve;

            var texture = Resources.Load<Texture2D>(CardSleeveResource);
            return texture != null
                ? Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * .5f)
                : CardSleeveArt.Create();
        }

        public static Sprite GetArtSprite(uint code) => GetArtSprite(code, BundledArtwork, out _);

        internal static (uint Code, int Variant) GetArtworkSource(uint code, int variant)
        {
            GetArtSprite(code, variant, out var source);
            return source;
        }

        private static Sprite GetArtSprite(uint code, int variant, out (uint Code, int Variant) source)
        {
            source = default;
            if (code == 0) return GetCardBackSprite();
            var key = (code, variant);
            if (_spriteCache.TryGetValue(key, out var cached))
            {
                _lru.Remove(cached.node);
                if (cached.sprite != null)
                {
                    _lru.AddLast(cached.node);
                    source = cached.source;
                    return cached.sprite;
                }
                _spriteCache.Remove(key);
            }

            if (_index == null) return GetFallbackArt();
            uint artCode = code;
            int artVariant = variant;
            bool fullFace = variant == SourceOverframeFace;
            Sprite sprite = fullFace
                ? _sourceOverframes.TryGetValue(code, out var official) ? DecodeArtwork(official.Face, opaque: true) : null
                : LoadExactArtwork(code, variant, out artVariant);
            if (sprite == null && !fullFace)
            {
                // A rules alias may identify a different card. Use the same card's other printings.
                foreach (uint relatedCode in RelatedPrintings(code))
                {
                    sprite = LoadExactArtwork(relatedCode, BundledArtwork, out artVariant);
                    if (sprite == null) continue;
                    artCode = relatedCode;
                    break;
                }
            }
            if (sprite == null) return GetFallbackArt();
            source = (artCode, artVariant);
            TrimArtwork(MaxCacheCount - 1);
            _spriteCache[key] = (sprite, _lru.AddLast(key), source);
            return sprite;
        }

        private static Sprite LoadExactArtwork(uint code, int variant, out int artVariant)
        {
            artVariant = BundledArtwork;
            if (variant >= 0 && _duelArt.TryGetValue((code, variant), out var preferred) &&
                DecodeArtwork(preferred) is Sprite preferredSprite)
            {
                artVariant = variant;
                return preferredSprite;
            }
            if (_index.TryGetValue(code, out var bundled) && DecodeArtwork(bundled) is Sprite bundledSprite)
                return bundledSprite;

            if (_duelVariants.TryGetValue(code, out var variants))
            {
                foreach (int alternative in variants)
                {
                    if (alternative == variant || !_duelArt.TryGetValue((code, alternative), out var local) ||
                        DecodeArtwork(local) is not Sprite localSprite) continue;
                    artVariant = alternative;
                    return localSprite;
                }
            }
            return null;
        }

        private static Sprite DecodeArtwork(ArtEntry entry, bool opaque = false)
        {
            try
            {
                if (!_packStreams.TryGetValue(entry.packId, out var fs))
                {
                    string packPath = entry.packId switch
                    {
                        TokenArtPackId => Path.Combine(_streamingDir, TokenArtFileName),
                        SupplementalArtPackId => Path.Combine(_streamingDir, SupplementalArtFileName),
                        StoryTrackArtId => Path.Combine(Application.streamingAssetsPath, StoryTrackArtFile),
                        _ => Path.Combine(_streamingDir, $"cardart_{entry.packId}.pack"),
                    };
                    if (!File.Exists(packPath)) return null;
                    fs = new FileStream(packPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    _packStreams[entry.packId] = fs;
                }

                if (entry.length == 0 || entry.length > int.MaxValue || (long)entry.offset + entry.length > fs.Length)
                    return null;
                byte[] image = new byte[entry.length];
                fs.Seek(entry.offset, SeekOrigin.Begin);
                int readTotal = 0;
                while (readTotal < image.Length)
                {
                    int n = fs.Read(image, readTotal, image.Length - readTotal);
                    if (n <= 0) return null;
                    readTotal += n;
                }

                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!tex.LoadImage(image))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                if (opaque)
                {
                    // The original alpha separates the foreground for effects. RGB contains the complete printed face.
                    var colors = tex.GetPixels32();
                    for (int i = 0; i < colors.Length; i++) colors[i].a = byte.MaxValue;
                    tex.SetPixels32(colors);
                    tex.Apply(false);
                }
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f,
                    0, opaque ? SpriteMeshType.FullRect : SpriteMeshType.Tight);
            }
            catch
            {
                return null;
            }
        }

        internal static Sprite RetainArtwork(uint code, int variant = BundledArtwork) => RetainArtwork(code, variant, out _);

        internal static Sprite RetainArtwork(uint code, int variant, out (uint Code, int Variant) source)
        {
            var sprite = GetArtSprite(code, variant, out source);
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
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(sprite);
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(sprite);
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            }
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
            foreach (var sprite in DuelVisualSprites.Values)
                DestroyArtwork(sprite);
            DuelVisualSprites.Clear();
            _spriteCache.Clear();
            _lru.Clear();
            _visibleArtwork.Clear();
            _duelArt.Clear();
            _duelVariants.Clear();
            _overframeVariants.Clear();
            _sourceOverframes.Clear();
            ArtworkChoicesByFamily.Clear();
            SelectedArtworkByFamily.Clear();
            _localDuelVisualsRoot = null;
            _activeDuelVisualsPath = null;
            _streamingDir = null;
            _index = null;
        }
    }
}
