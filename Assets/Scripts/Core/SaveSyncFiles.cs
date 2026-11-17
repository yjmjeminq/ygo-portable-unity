using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_EDITOR
using System.Runtime.CompilerServices;
#endif
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
#endif

namespace YGOPortable.Core
{
    internal enum SaveSyncAction { UpToDate, Upload, Download, Conflict }

    [Serializable]
    internal sealed class SaveSyncSnapshot
    {
        [Serializable]
        internal sealed class Deck
        {
            public string id;
            public string json;
        }

        public int version = 1;
        public string profile;
        public List<Deck> decks = new List<Deck>();

        internal bool HasData => !string.IsNullOrEmpty(profile) || decks.Count != 0;
        internal string Serialize() => JsonUtility.ToJson(this);
        internal string Hash => Digest(Serialize());

        internal static string Digest(string text)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        internal static SaveSyncSnapshot Parse(string text)
        {
            var snapshot = JsonUtility.FromJson<SaveSyncSnapshot>(text);
            if (snapshot == null || snapshot.version != 1 || snapshot.decks == null)
                throw new InvalidDataException("Invalid cloud save format.");
            if (!string.IsNullOrEmpty(snapshot.profile)) CollectionProgress.ValidateSaveProfile(snapshot.profile);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in snapshot.decks)
            {
                if (entry == null || !ValidDeckId(entry.id) || !ids.Add(entry.id) || string.IsNullOrEmpty(entry.json))
                    throw new InvalidDataException("Invalid cloud deck entry.");
                var deck = JsonUtility.FromJson<UserDeck>(entry.json);
                if (deck == null || deck.id != entry.id)
                    throw new InvalidDataException("Cloud deck file and ID differ.");
            }
            snapshot.decks.Sort((left, right) => StringComparer.Ordinal.Compare(left.id, right.id));
            return snapshot;
        }

        internal static bool ValidDeckId(string id) =>
            !string.IsNullOrEmpty(id) && id.Length <= 128 &&
            id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_' or '-');

        internal static SaveSyncAction Decide(SaveSyncSnapshot local, string localHash, string remoteHash, string baseHash)
        {
            if (remoteHash == null)
                return !string.IsNullOrEmpty(baseHash) ? SaveSyncAction.Conflict :
                    local.HasData ? SaveSyncAction.Upload : SaveSyncAction.UpToDate;
            if (localHash == remoteHash) return SaveSyncAction.UpToDate;
            if (!local.HasData) return SaveSyncAction.Download;
            if (string.IsNullOrEmpty(baseHash)) return SaveSyncAction.Conflict;
            if (localHash == baseHash) return SaveSyncAction.Download;
            if (remoteHash == baseHash) return SaveSyncAction.Upload;
            return SaveSyncAction.Conflict;
        }
    }

    internal sealed class SaveSyncFiles
    {
        private readonly string _profile;
        private readonly string _decks;
        private readonly string _journal;
        private readonly string _backup;
        private readonly string _baseline;

        internal SaveSyncFiles(string directory)
        {
            _profile = Path.Combine(directory, CollectionProgress.ProfileFileName);
            _decks = Path.Combine(directory, "Decks");
            _journal = Path.Combine(directory, "cloud-save-restore.pending");
            _backup = Path.Combine(directory, "cloud-save-previous-local.json");
            _baseline = Path.Combine(directory, "cloud-save-baseline.txt");
        }

        internal string Baseline => ReadOptional(_baseline);
        internal void SetBaseline(string hash) => WriteAtomic(_baseline, hash);
        internal SaveSyncSnapshot Capture()
        {
            using var profileLock = LockProfile(_profile);
            return ReadSnapshot();
        }

        private SaveSyncSnapshot ReadSnapshot()
        {
            var snapshot = new SaveSyncSnapshot { profile = ReadOptional(_profile) };
            foreach (string path in DeckFiles())
                snapshot.decks.Add(new SaveSyncSnapshot.Deck
                {
                    id = Path.GetFileNameWithoutExtension(path),
                    json = File.ReadAllText(path),
                });
            return SaveSyncSnapshot.Parse(snapshot.Serialize());
        }

        internal void Recover()
        {
            if (!File.Exists(_journal)) return;
            using var profileLock = LockProfile(_profile);
            string pending = ReadOptional(_journal);
            if (pending == null) return;
            Apply(SaveSyncSnapshot.Parse(pending));
            File.Delete(_journal);
        }

        internal bool Restore(SaveSyncSnapshot desired, string expectedLocalHash)
        {
            using var profileLock = LockProfile(_profile);
            var before = ReadSnapshot();
            if (before.Hash != expectedLocalHash) return false;
            WriteAtomic(_backup, before.Serialize());
            WriteAtomic(_journal, desired.Serialize());
            try
            {
                Apply(desired);
                File.Delete(_journal);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                WriteAtomic(_journal, before.Serialize());
                Apply(before);
                File.Delete(_journal);
                throw;
            }
            return true;
        }

        internal static FileStream LockProfile(string profile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(profile));
            return new FileStream(profile + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        private IEnumerable<string> DeckFiles() =>
            Directory.Exists(_decks) ? Directory.EnumerateFiles(_decks, "*.json") : Enumerable.Empty<string>();

        private void Apply(SaveSyncSnapshot snapshot)
        {
            var keep = new HashSet<string>(snapshot.decks.Select(deck => deck.id), StringComparer.Ordinal);
            foreach (string path in DeckFiles().ToArray())
                if (!keep.Contains(Path.GetFileNameWithoutExtension(path))) File.Delete(path);
            foreach (var deck in snapshot.decks)
                WriteAtomic(Path.Combine(_decks, deck.id + ".json"), deck.json);
            if (string.IsNullOrEmpty(snapshot.profile)) File.Delete(_profile);
            else WriteAtomic(_profile, snapshot.profile);
        }

        internal static string ReadOptional(string path)
        {
            try { return File.ReadAllText(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        internal static void WriteAtomic(string path, string value)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string pending = Path.Combine(directory, Path.GetRandomFileName() + ".tmp");
            try
            {
                File.WriteAllText(pending, value, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
            }
            finally
            {
                if (File.Exists(pending)) File.Delete(pending);
            }
        }
    }
}
