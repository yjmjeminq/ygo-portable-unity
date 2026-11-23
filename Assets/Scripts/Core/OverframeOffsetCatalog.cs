using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Xml;
using UnityEngine;

namespace YGOPortable.Core
{
    public static class OverframeOffsetCatalog
    {
        internal const string FileName = "overframe_offsets.json";
        private static readonly Dictionary<uint, OverframeOffset> Empty = new();
        private static Dictionary<uint, OverframeOffset> _offsets = Empty;

        private readonly struct OverframeOffset
        {
            internal readonly Vector2 Offset;
            internal readonly float Scale;

            internal OverframeOffset(Vector2 offset, float scale)
            {
                Offset = offset;
                Scale = scale;
            }
        }

        internal static void Load(string streamingAssetsPath)
        {
            _offsets = Empty;
            string path = Path.Combine(streamingAssetsPath, FileName);
            if (!File.Exists(path)) return;

            try
            {
                using var stream = File.OpenRead(path);
                using var reader = JsonReaderWriterFactory.CreateJsonReader(stream, new XmlDictionaryReaderQuotas());
                if (reader.MoveToContent() != XmlNodeType.Element || reader.GetAttribute("type") != "object")
                    throw new InvalidDataException("The offset catalog must be a JSON object.");
                var offsets = new Dictionary<uint, OverframeOffset>();
                reader.ReadStartElement();
                while (reader.MoveToContent() == XmlNodeType.Element)
                {
                    // The JSON reader maps property names starting with digits to an "item" attribute.
                    string key = reader.GetAttribute("item") ?? reader.LocalName;
                    if (!uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out uint code) ||
                        code == 0 || reader.GetAttribute("type") != "object")
                        throw new InvalidDataException($"Invalid offset for card {key}.");
                    if (!offsets.TryAdd(code, ReadOffset(reader)))
                        throw new InvalidDataException($"Duplicate offset for card {key}.");
                }
                reader.ReadEndElement();
                if (reader.MoveToContent() != XmlNodeType.None)
                    throw new InvalidDataException("Unexpected content after the offset catalog.");
                _offsets = offsets;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                XmlException or FormatException or ArgumentException or OverflowException)
            {
                Debug.LogWarning($"[OverframeOffsetCatalog] Could not read {FileName}: {error.Message}");
            }
        }

        private static OverframeOffset ReadOffset(XmlDictionaryReader reader)
        {
            var offset = Vector2.zero;
            float scale = 1f;
            reader.ReadStartElement();
            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "offsetX": offset.x = ReadNumber(reader); break;
                    case "offsetY": offset.y = ReadNumber(reader); break;
                    case "scale": scale = ReadNumber(reader); break;
                    default: reader.Skip(); break;
                }
            }
            reader.ReadEndElement();
            if (scale <= 0f) throw new InvalidDataException("Portrait scale must be positive.");
            return new OverframeOffset(offset, scale);
        }

        private static float ReadNumber(XmlDictionaryReader reader)
        {
            if (reader.GetAttribute("type") != "number")
                throw new InvalidDataException($"{reader.LocalName} must be a JSON number.");
            float value = reader.ReadElementContentAsFloat();
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidDataException("Portrait placement values must be finite.");
            return value;
        }

        // Offsets are measured in portrait image pixels, with positive Y pointing up.
        public static (Vector2 offset, float scale) GetOffset(uint code) =>
            _offsets.TryGetValue(code, out var placement) ? (placement.Offset, placement.Scale) : (Vector2.zero, 1f);
    }
}
