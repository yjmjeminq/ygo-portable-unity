using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace YGOPortable.Core
{
    internal sealed class GitHubSaveSync : IDisposable
    {
        internal const string Repository = "yjmjeminq/ygo-portable-saves";
        private const string Endpoint = "https://api.github.com/repos/" + Repository;
        private const string SaveEndpoint = Endpoint + "/contents/save.json";

        [Serializable] private sealed class RepoInfo
        {
            public string full_name;
            public bool @private;
        }

        [Serializable] private sealed class RemoteFile
        {
            public string sha;
            public string encoding;
            public string content;
        }

        [Serializable] private sealed class UpdateFile
        {
            public string message = "Sync game save";
            public string content;
            public string sha;
        }

        private sealed class Conflict
        {
            internal string LocalHash;
            internal string RemoteSha;
        }

        private readonly MonoBehaviour _host;
        private readonly string _directory;
        private readonly SaveSyncFiles _files;
        private readonly Func<bool> _canRestore;
        private readonly Action _reload;
        private string _token;
        private bool _busy;
        private float _nextSync = float.PositiveInfinity;
        private Conflict _conflict;
        private Conflict _resolve;
        private bool _useCloud;
        private Coroutine _work;

        internal string Status { get; private set; } = "연결되지 않음";
        internal bool Connected => _token != null;
        internal bool Busy => _busy;
        internal bool HasConflict => _conflict != null;
        internal bool CloudExists => _conflict?.RemoteSha != null;

        internal GitHubSaveSync(MonoBehaviour host, string directory, SaveSyncFiles files, Func<bool> canRestore, Action reload)
        {
            _host = host;
            _directory = directory;
            _files = files;
            _canRestore = canRestore;
            _reload = reload;
            if (!SaveSyncCredential.Supported)
            {
                Status = "이 플랫폼에서는 클라우드 연결을 지원하지 않습니다.";
                return;
            }
            try { _token = SaveSyncCredential.Read(directory); }
            catch (Exception e) when (IsSaveError(e)) { Status = "GitHub 연결 정보를 다시 입력해 주세요."; }
            if (Connected) RequestSync();
            CollectionProgress.SaveChanged += OnSaveChanged;
            DeckRepository.SaveChanged += OnSaveChanged;
        }

        internal void Tick()
        {
            if (!Connected || _busy || HasConflict || Time.unscaledTime < _nextSync) return;
            _nextSync = Time.unscaledTime + 45f;
            _busy = true;
            _work = _host.StartCoroutine(Synchronize());
        }

        internal void RequestSync(float delay = 0f)
        {
            _nextSync = Mathf.Min(_nextSync, Time.unscaledTime + delay);
        }

        private void OnSaveChanged() => RequestSync(1f);

        internal void Connect(string input)
        {
            if (_busy || !SaveSyncCredential.Supported) return;
            string token = input.Trim();
            if (!token.StartsWith("github_pat_", StringComparison.Ordinal) || token.Length < 22 ||
                Array.Exists(token.ToCharArray(), c => !(c is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_')))
            {
                Status = "GitHub에서 만든 fine-grained 토큰을 붙여 넣으세요.";
                return;
            }
            _busy = true;
            _work = _host.StartCoroutine(ValidateConnection(token));
        }

        internal void Disconnect()
        {
            if (_busy) return;
            try
            {
                SaveSyncCredential.Write(_directory, null);
                _token = null;
                _conflict = _resolve = null;
                Status = "연결되지 않음";
            }
            catch (Exception e) when (IsSaveError(e)) { Status = "연결을 해제하지 못했습니다."; }
        }

        internal void Resolve(bool useCloud)
        {
            if (_busy || _conflict == null || useCloud && !CloudExists) return;
            _useCloud = useCloud;
            _resolve = _conflict;
            _conflict = null;
            RequestSync();
        }

        private IEnumerator ValidateConnection(string token)
        {
            try
            {
                Status = "비공개 저장소를 확인하는 중…";
                using var request = Get(Endpoint, token);
                yield return request.SendWebRequest();
                if (!IsPrivateRepository(request))
                {
                    Status = "저장소에 접근할 수 없습니다. 토큰의 저장소와 Contents 권한을 확인하세요.";
                    yield break;
                }
                try
                {
                    SaveSyncCredential.Write(_directory, token);
                    _token = token;
                    _conflict = _resolve = null;
                    RequestSync();
                    Status = "GitHub 연결 완료";
                }
                catch (Exception e) when (IsSaveError(e)) { Status = "이 기기에 연결 정보를 저장하지 못했습니다."; }
            }
            finally { _busy = false; }
        }

        private IEnumerator Synchronize()
        {
            try
            {
                Status = "클라우드 저장 확인 중…";
                using (var repo = Get(Endpoint, _token))
                {
                    yield return repo.SendWebRequest();
                    if (!IsPrivateRepository(repo))
                    {
                        Status = repo.result == UnityWebRequest.Result.ConnectionError
                            ? "오프라인 · 연결되면 다시 시도합니다."
                            : "비공개 저장소에 접근할 수 없습니다. GitHub 연결을 확인하세요.";
                        yield break;
                    }
                }

                RemoteFile info = null;
                string serialized = null;
                using (var request = Get(SaveEndpoint, _token, "application/vnd.github.object+json"))
                {
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        if (!TryReadRemote(request.downloadHandler.text, out info, out serialized))
                        {
                            Status = "클라우드 저장 파일 정보를 읽을 수 없습니다.";
                            yield break;
                        }
                    }
                    else if (request.responseCode != 404)
                    {
                        Status = "클라우드 저장을 받을 수 없습니다. 잠시 후 다시 시도합니다.";
                        yield break;
                    }
                }

                if (info != null && serialized == null)
                {
                    using var raw = Get(SaveEndpoint, _token, "application/vnd.github.raw+json");
                    yield return raw.SendWebRequest();
                    if (raw.result != UnityWebRequest.Result.Success || !MatchesBlob(raw.downloadHandler.data, info.sha))
                    {
                        Status = "클라우드 파일이 바뀌었습니다. 다시 확인합니다.";
                        RequestSync(3f);
                        yield break;
                    }
                    serialized = raw.downloadHandler.text;
                }

                string localJson = PrepareSync(info?.sha, serialized);
                if (localJson == null) yield break;
                var payload = new UpdateFile
                {
                    content = Convert.ToBase64String(Encoding.UTF8.GetBytes(localJson)),
                    sha = info?.sha,
                };
                // A new file must omit the SHA, which is only valid when replacing an existing blob.
                string body = payload.sha == null
                    ? "{\"message\":\"Sync game save\",\"content\":\"" + payload.content + "\"}"
                    : JsonUtility.ToJson(payload);
                using var put = Request(SaveEndpoint, "PUT", _token, "application/vnd.github+json", body);
                yield return put.SendWebRequest();
                if (put.result == UnityWebRequest.Result.Success)
                {
                    try { MarkSynced(SaveSyncSnapshot.Digest(localJson)); }
                    catch (Exception e) when (IsSaveError(e)) { Status = "클라우드에 저장했지만 이 기기에 동기화 기록을 남기지 못했습니다."; }
                }
                else if (put.responseCode == 409 || put.responseCode == 422)
                {
                    Status = "저장소 상태가 바뀌었습니다. 다시 확인합니다.";
                    RequestSync(5f);
                }
                else Status = "클라우드에 올리지 못했습니다. 기기 저장을 유지하고 다시 시도합니다.";
            }
            finally { _busy = false; }
        }

        private string PrepareSync(string remoteSha, string serialized)
        {
            try
            {
                var remote = serialized == null ? null : SaveSyncSnapshot.Parse(serialized);
                var local = _files.Capture();
                string localHash = local.Hash;
                string remoteHash = remote?.Hash;
                var action = SaveSyncSnapshot.Decide(local, localHash, remoteHash, _files.Baseline);
                var resolution = _resolve;
                _resolve = null;
                if (resolution != null && resolution.LocalHash == localHash && resolution.RemoteSha == remoteSha)
                    action = _useCloud ? SaveSyncAction.Download : SaveSyncAction.Upload;
                else resolution = null;

                switch (action)
                {
                    case SaveSyncAction.Conflict:
                        _conflict = new Conflict { LocalHash = localHash, RemoteSha = remoteSha };
                        Status = remote == null ? "클라우드 저장이 사라졌습니다. 이 기기의 저장을 다시 올릴 수 있습니다." :
                            "이 기기와 클라우드에 서로 다른 진행이 있습니다. 사용할 저장을 선택하세요.";
                        break;
                    case SaveSyncAction.Upload:
                        Status = "클라우드에 저장 중…";
                        return local.Serialize();
                    case SaveSyncAction.Download:
                        if (!_canRestore())
                        {
                            _resolve = resolution;
                            Status = "최신 클라우드 저장을 타이틀이나 메인 메뉴에서 적용합니다.";
                            RequestSync(10f);
                        }
                        else if (_files.Restore(remote, localHash))
                        {
                            _reload();
                            MarkSynced(remoteHash);
                        }
                        else RequestSync(2f);
                        break;
                    case SaveSyncAction.UpToDate:
                        if (remote != null) MarkSynced(localHash);
                        else Status = "연결됨 · 진행 상황을 저장하면 자동으로 올립니다.";
                        break;
                }
            }
            catch (Exception e) when (IsSaveError(e))
            {
                Status = "저장 파일을 확인할 수 없습니다. 기존 저장을 유지하고 다시 시도합니다.";
            }
            return null;
        }

        private void MarkSynced(string hash)
        {
            if (_files.Baseline != hash) _files.SetBaseline(hash);
            Status = "동기화 완료";
        }

        private static bool TryReadRemote(string body, out RemoteFile info, out string json)
        {
            info = null;
            json = null;
            try
            {
                info = JsonUtility.FromJson<RemoteFile>(body);
                if (info?.sha?.Length != 40) return false;
                if (info.encoding == "base64") json = Encoding.UTF8.GetString(Convert.FromBase64String(info.content));
                return json == null || MatchesBlob(Encoding.UTF8.GetBytes(json), info.sha);
            }
            catch (Exception e) when (IsSaveError(e)) { return false; }
        }

        private static bool MatchesBlob(byte[] bytes, string expected)
        {
            using var sha = SHA1.Create(); // GitHub identifies repository file blobs using Git's SHA-1 format.
            byte[] header = Encoding.ASCII.GetBytes(FormattableString.Invariant($"blob {bytes.Length}\0"));
            sha.TransformBlock(header, 0, header.Length, header, 0);
            sha.TransformFinalBlock(bytes, 0, bytes.Length);
            string actual = BitConverter.ToString(sha.Hash).Replace("-", "");
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPrivateRepository(UnityWebRequest request)
        {
            if (request.result != UnityWebRequest.Result.Success) return false;
            try
            {
                var repo = JsonUtility.FromJson<RepoInfo>(request.downloadHandler.text);
                return repo != null && repo.@private && string.Equals(repo.full_name, Repository, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
        }

        private static bool IsSaveError(Exception error) =>
            error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or CryptographicException;

        private static UnityWebRequest Get(string url, string token, string accept = "application/vnd.github+json") =>
            Request(url, "GET", token, accept, null);

        private static UnityWebRequest Request(string url, string method, string token, string accept, string body)
        {
            var request = new UnityWebRequest(url, method) { timeout = 14, redirectLimit = 0, downloadHandler = new DownloadHandlerBuffer() };
            request.SetRequestHeader("Authorization", "Bearer " + token);
            request.SetRequestHeader("Accept", accept);
            request.SetRequestHeader("X-GitHub-Api-Version", "2022-11-28");
            request.SetRequestHeader("User-Agent", "YGO-Portable-Save-Sync");
            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.uploadHandler.contentType = "application/json";
            }
            return request;
        }

        public void Dispose()
        {
            CollectionProgress.SaveChanged -= OnSaveChanged;
            DeckRepository.SaveChanged -= OnSaveChanged;
            if (_work != null) _host.StopCoroutine(_work);
            _token = null;
        }
    }
}
