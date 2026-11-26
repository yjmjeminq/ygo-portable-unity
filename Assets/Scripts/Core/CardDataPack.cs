using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;

namespace YGOPortable.Core
{
    internal static class CardDataPack
    {
        internal const string ZipName = "CardData.zip";
        private const string Prefix = "CardData-v1/";
        private const string Visuals = "DuelVisuals/";
        private const string Current = "v1";
        private const string Previous = ".previous-v1";
        private const string Staging = ".importing-v1";
        private const long MaximumSize = 3L * 1024 * 1024 * 1024;
        private static readonly object InstallLock = new();

        internal static string Root { get; set; }
        internal static string RuntimePath(string file) => Path.Combine(Root ?? Application.streamingAssetsPath, file);

        internal static string[] ReadFileList()
        {
            var catalog = Resources.Load<TextAsset>("CardDataPackFiles");
            if (catalog == null) throw new InvalidOperationException("카드 데이터 파일 목록을 찾을 수 없습니다.");
            return catalog.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        internal static string FindInstalled(string folder, string[] required)
        {
            foreach (string location in new[] { Current, Previous })
            {
                string directory = Path.Combine(folder, location);
                if (required.All(name => File.Exists(Path.Combine(directory, name))) &&
                    File.Exists(Path.Combine(directory, Visuals, CardArtLoader.DuelArtFileName)))
                    return directory;
            }
            return null;
        }

        internal static void Install(string archive, string folder, string[] required)
        {
            lock (InstallLock)
            {
                string staging = Path.Combine(folder, Staging);
                string current = Path.Combine(folder, Current);
                string previous = Path.Combine(folder, Previous);
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                Directory.CreateDirectory(staging);
                try
                {
                    Extract(archive, staging, required);
                    if (Directory.Exists(current))
                    {
                        if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
                        Directory.Move(current, previous);
                    }
                    try { Directory.Move(staging, current); }
                    catch
                    {
                        if (!Directory.Exists(current) && Directory.Exists(previous))
                            Directory.Move(previous, current);
                        throw;
                    }
                    Cleanup(previous);
                }
                finally { Cleanup(staging); }
            }
        }

        private static void Extract(string archive, string staging, string[] required)
        {
            var pending = new HashSet<string>(required, StringComparer.Ordinal);
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var stream = File.OpenRead(archive);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            long size = 0;
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(Prefix, StringComparison.Ordinal) ||
                    entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    continue;
                string relative = entry.FullName.Substring(Prefix.Length);
                string[] parts = relative.Split('/');
                if (parts.Any(part => part.Length == 0 || part is "." or ".." ||
                    part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) ||
                    part.IndexOfAny(new[] { '\\', ':', '\0' }) >= 0))
                    throw new InvalidDataException("카드 데이터 ZIP에 올바르지 않은 경로가 있습니다.");

                if (!required.Contains(relative) && !(relative.StartsWith(Visuals, StringComparison.Ordinal) &&
                    DuelVisualsZipImport.IsVisualFile(parts.Skip(1).ToArray()))) continue;
                if (!written.Add(relative))
                    throw new InvalidDataException($"카드 데이터 ZIP에 중복 파일이 있습니다. {relative}");
                if (entry.Length <= 0 || entry.Length > MaximumSize - size || written.Count > 4096)
                    throw new InvalidDataException($"카드 데이터 ZIP의 파일 크기 또는 개수를 확인하세요. {relative}");
                size += entry.Length;

                string destination = Path.Combine(staging, Path.Combine(parts));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using var input = entry.Open();
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                input.CopyTo(output);
                if (output.Length != entry.Length)
                    throw new InvalidDataException($"카드 데이터 ZIP에 손상된 파일이 있습니다. {relative}");
                pending.Remove(relative);
            }
            if (pending.Count != 0)
                throw new InvalidDataException($"CardData.zip에 필요한 파일이 없습니다. {pending.First()}");
            string artwork = Path.Combine(staging, Visuals, CardArtLoader.DuelArtFileName);
            string overframe = Path.Combine(staging, Visuals, CardArtLoader.SourceOverframeFileName);
            if (!File.Exists(artwork) || !CardArtLoader.HasValidImportedArtwork(artwork, overframe: false))
                throw new InvalidDataException("CardData.zip의 카드 일러스트를 읽을 수 없습니다.");
            if (File.Exists(overframe) && !CardArtLoader.HasValidImportedArtwork(overframe, overframe: true))
                throw new InvalidDataException("CardData.zip의 오버프레임 일러스트를 읽을 수 없습니다.");
        }

        private static void Cleanup(string directory)
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[CardData] Could not remove temporary files: {error.Message}");
            }
        }
    }
}
