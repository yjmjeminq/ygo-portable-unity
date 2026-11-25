using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace YGOPortable.Core
{
    internal static class DuelVisualsZipImport
    {
        private const string Installed = ".zip-installed";
        private const string Previous = ".zip-previous";
        private const string Staging = ".zip-importing";
        private static readonly object InstallLock = new();

        internal static string FindInstalledFolder(string root)
        {
            string current = Path.Combine(root, Installed);
            string previous = Path.Combine(root, Previous);
            if (File.Exists(Path.Combine(current, CardArtLoader.DuelArtFileName))) return current;
            return File.Exists(Path.Combine(previous, CardArtLoader.DuelArtFileName)) ? previous : null;
        }

        internal static void Install(string zipPath, string root)
        {
            lock (InstallLock)
            {
                string staging = Path.Combine(root, Staging);
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                Directory.CreateDirectory(staging);
                try
                {
                    Extract(zipPath, staging);
                    string art = Path.Combine(staging, CardArtLoader.DuelArtFileName);
                    string overframe = Path.Combine(staging, CardArtLoader.SourceOverframeFileName);
                    if (!File.Exists(art)) throw new InvalidDataException("ZIP 안에 cardart.pack이 없습니다.");
                    if (!CardArtLoader.HasValidImportedArtwork(art, overframe: false))
                        throw new InvalidDataException("ZIP 안의 cardart.pack을 읽을 수 없습니다.");
                    if (File.Exists(overframe) && !CardArtLoader.HasValidImportedArtwork(overframe, overframe: true))
                        throw new InvalidDataException("ZIP 안의 overframeart.pack을 읽을 수 없습니다.");
                    Publish(root, staging);
                }
                finally { Cleanup(staging); }
            }
        }

        private static void Extract(string zipPath, string staging)
        {
            using var stream = File.OpenRead(zipPath);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                string name = entry.FullName;
                if (name.StartsWith("DuelVisuals/", StringComparison.Ordinal))
                    name = name.Substring("DuelVisuals/".Length);
                string[] parts = name.TrimEnd('/').Split('/');
                if (name.Length == 0) continue;
                if (Array.Exists(parts, part => part.Length == 0 || part is "." or ".." ||
                    part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) ||
                    part.IndexOfAny(new[] { '\\', ':', '\0' }) >= 0))
                    throw new InvalidDataException("ZIP 안에 올바르지 않은 파일 경로가 있습니다.");
                if (name.EndsWith("/", StringComparison.Ordinal) || !IsVisualFile(parts)) continue;
                if (!written.Add(name)) throw new InvalidDataException("ZIP 안에 같은 이름의 파일이 있습니다.");

                string destination = Path.Combine(staging, Path.Combine(parts));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using var input = entry.Open();
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                input.CopyTo(output);
                if (output.Length != entry.Length)
                    throw new InvalidDataException("ZIP에 손상된 파일이 있습니다.");
            }
        }

        private static bool IsVisualFile(string[] parts)
        {
            if (parts.Length == 1)
                return parts[0] is CardArtLoader.DuelArtFileName or CardArtLoader.SourceOverframeFileName or "Field.png" or "Ground.png";
            if (parts[0] is not ("CutIns" or "Audio" or "Hud" or "CardIcons" or "Effects")) return false;
            return Path.GetExtension(parts[parts.Length - 1]).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".wav" or ".mp4";
        }

        private static void Publish(string root, string staging)
        {
            string current = Path.Combine(root, Installed);
            string previous = Path.Combine(root, Previous);
            if (Directory.Exists(current))
            {
                if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
                Directory.Move(current, previous);
            }
            try { Directory.Move(staging, current); }
            catch
            {
                if (!Directory.Exists(current) && Directory.Exists(previous)) Directory.Move(previous, current);
                throw;
            }
            Cleanup(previous);
        }

        private static void Cleanup(string folder)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                UnityEngine.Debug.LogWarning($"[DuelVisuals] Could not remove temporary files: {error.Message}");
            }
        }
    }
}
