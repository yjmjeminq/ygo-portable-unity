using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class SaveSyncMenu
    {
        private readonly GitHubSaveSync _sync;
        private readonly InputField _token;
        private readonly Text _status;
        private readonly GameObject _setup;
        private readonly GameObject _connected;
        private readonly GameObject _conflict;
        private readonly Button _connect, _pasteConnect;
        private readonly Button _refresh;
        private readonly Button _disconnect;
        private readonly Button _local;
        private readonly Button _cloud;
        private readonly Button _close;
        internal GameObject Root { get; }

        internal SaveSyncMenu(Transform canvas, GitHubSaveSync sync, Action close)
        {
            _sync = sync;
            var root = DuelUiKit.FillRect("Overlay_SaveSync", canvas);
            Root = root.gameObject;
            DuelUiKit.AddImage(Root, DuelUiKit.WhiteSprite, new Color(0f, 0f, 0f, .76f));
            var win = DuelUiKit.CreateMenuPanel(root, "SaveSync",
                new Vector2(.23f, .23f), new Vector2(.77f, .77f));
            var header = Row(win, "Header", .055f, .79f, .945f, .95f);
            DuelUiKit.AddFittedText(header, "Title", "클라우드 저장", 32, 23, TextAnchor.MiddleLeft,
                DuelUiKit.GoldAccent, FontStyle.Bold);

            var status = Row(win, "Status", .055f, .59f, .945f, .78f);
            _status = DuelUiKit.AddFittedText(status, "Text", "", 21, 14, TextAnchor.MiddleLeft,
                DuelUiKit.TextPrimary, FontStyle.Bold);

            var setup = DuelUiKit.FillRect("Setup", win);
            _setup = setup.gameObject;
            var instructions = Row(setup, "Instructions", .055f, .40f, .945f, .58f);
            DuelUiKit.AddFittedText(instructions, "Text",
                "토큰은 " + GitHubSaveSync.Repository + " 전용으로 만들고 Contents 쓰기 권한을 주세요.\n복사한 토큰으로 한 번 연결하면 이 기기에 저장됩니다.",
                18, 13, TextAnchor.MiddleLeft, DuelUiKit.TextMuted);
            var entry = Row(setup, "Entry", .055f, .255f, .945f, .385f);
            _token = DuelUiKit.CreateInputField(entry, "GitHubToken", "fine-grained 토큰 붙여넣기", 18);
            _token.contentType = InputField.ContentType.Password;
            _token.characterLimit = 512;
            AddButton(setup, "TokenPage", "토큰 만들기", .055f, .32f,
                () => Application.OpenURL("https://github.com/settings/personal-access-tokens/new"));
            _connect = AddButton(setup, "Connect", "입력한 토큰 연결", .34f, .620f, () => Connect(_token.text));
            _pasteConnect = AddButton(setup, "PasteConnect", "복사한 토큰 연결", .64f, .945f,
                () => Connect(GUIUtility.systemCopyBuffer));

            var connected = DuelUiKit.FillRect("Connected", win);
            _connected = connected.gameObject;
            var details = Row(connected, "Details", .055f, .38f, .945f, .57f);
            DuelUiKit.AddFittedText(details, "Text", "카드, 재화, 대전 해금과 편집한 덱을 자동으로 동기화합니다.",
                19, 14, TextAnchor.MiddleLeft, DuelUiKit.TextMuted);
            _refresh = AddButton(connected, "Refresh", "지금 확인", .055f, .46f, () => _sync.RequestSync(), y: .22f);
            _disconnect = AddButton(connected, "Disconnect", "이 기기 연결 해제", .49f, .945f, _sync.Disconnect, y: .22f);

            var conflict = DuelUiKit.FillRect("Conflict", win);
            _conflict = conflict.gameObject;
            var notice = Row(conflict, "Notice", .055f, .38f, .945f, .57f);
            DuelUiKit.AddFittedText(notice, "Text", "클라우드 저장을 적용하면 이 기기의 기존 저장은 백업됩니다.",
                19, 14, TextAnchor.MiddleLeft, DuelUiKit.TextMuted);
            _local = AddButton(conflict, "UseLocal", "이 기기 저장 사용", .055f, .46f, () => _sync.Resolve(false), y: .22f);
            _cloud = AddButton(conflict, "UseCloud", "클라우드 저장 사용", .49f, .945f, () => _sync.Resolve(true), y: .22f);

            _close = DuelUiKit.CreateButton(Row(win, "Close", .69f, .825f, .945f, .96f),
                "Button", "닫기", close, 18);
            Root.SetActive(false);
        }

        internal void Open()
        {
            Root.transform.SetAsLastSibling();
            Root.SetActive(true);
            Refresh();
            EventSystem.current?.SetSelectedGameObject(_close.gameObject);
        }

        internal void Close()
        {
            _token.SetTextWithoutNotify("");
            _token.DeactivateInputField();
            Root.SetActive(false);
        }

        internal void Refresh()
        {
            if (!Root.activeSelf) return;
            _status.text = _sync.Status;
            _setup.SetActive(!_sync.Connected);
            _connected.SetActive(_sync.Connected && !_sync.HasConflict);
            _conflict.SetActive(_sync.HasConflict);
            _connect.interactable = _pasteConnect.interactable = !_sync.Busy && SaveSyncCredential.Supported;
            _token.interactable = !_sync.Busy && SaveSyncCredential.Supported;
            _refresh.interactable = !_sync.Busy;
            _disconnect.interactable = !_sync.Busy;
            _local.interactable = !_sync.Busy;
            _cloud.interactable = !_sync.Busy && _sync.CloudExists;
        }

        private void Connect(string value)
        {
            _token.SetTextWithoutNotify("");
            _token.DeactivateInputField();
            _sync.Connect(value);
            Refresh();
        }

        private static RectTransform Row(Transform parent, string name, float left, float bottom, float right, float top) =>
            DuelUiKit.CreateRect(name, parent, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Button AddButton(Transform parent, string name, string text, float left, float right, Action click, float y = .10f) =>
            DuelUiKit.CreateButton(Row(parent, name, left, y, right, y + .125f),
                "Button", text, click, 18);
    }
}
