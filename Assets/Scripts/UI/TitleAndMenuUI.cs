using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.AI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public class TitleAndMenuUI
    {
        public GameObject TitleRoot { get; private set; }
        public GameObject MenuRoot { get; private set; }
        public GameObject SettingsRoot { get; private set; }

        private readonly GameAppController _app;
        private CanvasGroup _menuControls;
        private CanvasGroup _titleControls;
        private CanvasGroup _settingsControls;
        private Button _freeDuelButton;
        private Button _rankedButton;
        private GameObject _championBadge;
        private Button _storyButton;
        private Button _rpgWorldButton;
        private Button _deckEditorButton;
        private Button _shopButton;
        private Button _codexButton;
        private Text _shopBalance;
        private RectTransform _shopCardBacks;
        private Button _settingsOpenButton;
        private Button _settingsCloseButton;
        private Text _aiDifficultyLabel;
        private Text _duelArtStatus;
        private Text _duelArtToast;
        private Coroutine _duelArtToastTimer;
        private bool _importingDuelVisuals;
        private GameObject _settingsPanel;

        public TitleAndMenuUI(GameAppController app, Transform canvasParent)
        {
            _app = app;
            BuildTitleScreen(canvasParent);
            BuildMainMenuScreen(canvasParent);
            BuildSettingsOverlay(canvasParent);
        }

        private void OpenSettings()
        {
            _menuControls.interactable = false;
            RefreshAiDifficulty();
            RefreshDuelArtStatus();
            _duelArtToast.gameObject.SetActive(false);
            SettingsRoot.SetActive(true);
            UiEntranceMotion.Play(_settingsPanel, .57f);
            EventSystem.current?.SetSelectedGameObject(_settingsCloseButton.gameObject);
        }

        internal void CloseSettings(bool restoreFocus = true)
        {
            if (_importingDuelVisuals) return;
            SettingsRoot.SetActive(false);
            _menuControls.interactable = true;
            if (restoreFocus) EventSystem.current?.SetSelectedGameObject(_settingsOpenButton.gameObject);
        }

        internal void SetSaveSyncOpen(bool open)
        {
            _titleControls.interactable = !open;
            _settingsControls.interactable = !open && !_importingDuelVisuals;
            _menuControls.interactable = !open && !SettingsRoot.activeSelf;
        }

        internal void RefreshWallet()
        {
            _shopBalance.text = $"보유 {CollectionProgress.Balance:N0} DP";
            _championBadge.SetActive(CollectionProgress.IsRankedChampion);
        }

        internal void FocusMainMenu(AppScreen previousScreen)
        {
            RefreshWallet();
            var selected = previousScreen switch
            {
                AppScreen.CardShop => _shopButton,
                AppScreen.DeckEditor => _deckEditorButton,
                AppScreen.StoryMode => _storyButton,
                AppScreen.TagForceWorld => _rpgWorldButton,
                AppScreen.ArchetypeCodex => _codexButton,
                AppScreen.Ranked => _rankedButton,
                _ => _freeDuelButton,
            };
            EventSystem.current?.SetSelectedGameObject(selected.gameObject);
        }

        private void BuildTitleScreen(Transform parent)
        {
            var root = DuelUiKit.FillRect("Screen_Title", parent);
            TitleRoot = root.gameObject;
            _titleControls = TitleRoot.AddComponent<CanvasGroup>();
            AddTitleBackground(root);

            var logo = DuelUiKit.CreateRect("Logo", root, new Vector2(0.22f, 0.40f), new Vector2(0.78f, 0.83f),
                Vector2.zero, Vector2.zero);
            AddLogo(logo);
            var start = DuelUiKit.CreateRect("Start", root, new Vector2(0.375f, 0.205f),
                new Vector2(0.625f, 0.292f), Vector2.zero, Vector2.zero);
            MenuButtonMotion.Attach(DuelUiKit.CreateButton(start, "Button", "게임 시작",
                () => _app.ShowScreen(AppScreen.MainMenu), 25, true));
            var cloud = DuelUiKit.CreateRect("CloudSave", root, new Vector2(.72f, .078f),
                new Vector2(.91f, .148f), Vector2.zero, Vector2.zero);
            DuelUiKit.CreateButton(cloud, "Button", "클라우드 저장", _app.OpenSaveSync, 19);
        }

        private void BuildMainMenuScreen(Transform parent)
        {
            var root = DuelUiKit.FillRect("Screen_MainMenu", parent);
            MenuRoot = root.gameObject;
            _menuControls = MenuRoot.AddComponent<CanvasGroup>();
            AddTitleBackground(root);

            var logo = DuelUiKit.CreateRect("Logo", root, new Vector2(0.31f, 0.674f), new Vector2(0.69f, 0.938f),
                Vector2.zero, Vector2.zero);
            AddLogo(logo);
            var menu = DuelUiKit.CreateMenuPanel(root, "Menu", new Vector2(.205f, .132f),
                new Vector2(.538f, .643f));
            int row = 0;
            Button AddRow(string name, string label, Action action) =>
                AddMenuEntry(menu, name, .840f - row++ * .112f, label, action);
            _storyButton = AddRow("StoryMode", "스토리 모드", () => _app.OpenStoryCampaign(false));
            _rpgWorldButton = AddRow("RpgWorld", "RPG 월드", _app.OpenRpgField);
            _rankedButton = AddRow("Ranked", "랭크 매치", () => _app.ShowScreen(AppScreen.Ranked));
            _freeDuelButton = AddRow("FreeDuel", "프리 듀얼", () => _app.ShowScreen(AppScreen.OpponentSelect));
            _deckEditorButton = AddRow("DeckEditor", "덱 편집", () => _app.ShowScreen(AppScreen.DeckEditor));
            _codexButton = AddRow("ArchetypeCodex", "카드군 도감", () => _app.ShowScreen(AppScreen.ArchetypeCodex));
            _settingsOpenButton = AddRow("Settings", "환경 설정", OpenSettings);
            AddRow("ReturnToTitle", "타이틀로 돌아가기", () => _app.ShowScreen(AppScreen.Title));
            BuildShopTile(root);
            _championBadge = OfflineRankedUI.AddChampionBadge(root, new Vector2(.726f, .908f), new Vector2(.971f, .966f));
            _championBadge.SetActive(false);
        }

        private void BuildShopTile(Transform root)
        {
            var tile = DuelUiKit.CreateMenuPanel(root, "ShopTile", new Vector2(.555f, .132f), new Vector2(.795f, .643f));
            var hitbox = DuelUiKit.FillRect("Button", tile, 7f);
            var target = DuelUiKit.AddImage(hitbox.gameObject, DuelUiKit.WhiteSprite, Color.clear);
            _shopButton = hitbox.gameObject.AddComponent<Button>();
            _shopButton.targetGraphic = target;
            _shopButton.transition = Selectable.Transition.None;
            _shopButton.onClick.AddListener(() =>
            {
                DuelAudio.PlayClick();
                _app.ShowScreen(AppScreen.CardShop);
            });
            MenuButtonMotion.Attach(_shopButton);

            var cardBack = CardArtLoader.GetCardBackSprite();
            _shopCardBacks = DuelUiKit.CreateRect("Cards", tile, new Vector2(.12f, .385f), new Vector2(.88f, .885f),
                Vector2.zero, Vector2.zero);
            void AddCard(float left, float right, float angle, Color tint)
            {
                var card = DuelUiKit.CreateRect("Card", _shopCardBacks, new Vector2(left, .02f), new Vector2(right, .98f),
                    Vector2.zero, Vector2.zero);
                card.localRotation = Quaternion.Euler(0f, 0f, angle);
                DuelUiKit.AddImage(card.gameObject, cardBack, tint, true, false);
            }
            if (cardBack != null)
            {
                var sideTint = new Color(.75f, .81f, .94f);
                AddCard(.09f, .525f, 13f, sideTint);
                AddCard(.475f, .91f, -13f, sideTint);
                AddCard(.2825f, .7175f, 0f, Color.white);
            }

            var wallet = DuelUiKit.CreateRect("Wallet", tile, new Vector2(.12f, .277f), new Vector2(.88f, .365f),
                Vector2.zero, Vector2.zero);
            _shopBalance = DuelUiKit.AddFittedText(wallet, "Text", "", 23, 14, TextAnchor.MiddleCenter,
                DuelUiKit.GoldAccent, FontStyle.Bold);

            var action = DuelUiKit.CreateRect("Action", tile, new Vector2(.11f, .10f), new Vector2(.89f, .24f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddImage(action.gameObject, DuelUiChrome.Primary, Color.white, false, false).type = Image.Type.Sliced;
            DuelUiKit.AddFittedText(action, "Label", "카드 상점", 26, 17, TextAnchor.MiddleCenter,
                DuelUiKit.TextPrimary, FontStyle.Bold);
        }

        private static void AddTitleBackground(Transform root)
        {
            DuelUiKit.AddImage(DuelUiKit.FillRect("Backdrop", root).gameObject,
                DuelUiChrome.Backdrop, Color.white, false, false);
        }

        private static void AddLogo(RectTransform holder)
        {
            var logo = DuelVisualAssets.MasterDuelLogo;
            if (logo != null) DuelUiKit.AddImage(holder.gameObject, logo, Color.white, true, false);
            else DuelUiKit.AddFittedText(holder, "Title", "Yu-Gi-Oh!\nMASTER DUEL", 58, 21,
                TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        }

        private static Button AddMenuEntry(Transform parent, string name, float y, string label, Action action)
        {
            var row = DuelUiKit.CreateRect(name, parent, new Vector2(0.097f, y), new Vector2(0.91f, y + .106f),
                Vector2.zero, Vector2.zero);
            return MenuButtonMotion.Attach(DuelUiKit.CreateButton(row, "Button", label, action, 23));
        }

        private void RefreshAiDifficulty()
        {
            _aiDifficultyLabel.text = _app.AiDifficulty == AIDifficulty.Master
                ? "AI 난이도: 마스터 (0.6초 수읽기)" : "AI 난이도: 보통";
        }

        private void BuildSettingsOverlay(Transform parent)
        {
            var bg = DuelUiKit.FillRect("Overlay_Settings", parent);
            SettingsRoot = bg.gameObject;
            _settingsControls = SettingsRoot.AddComponent<CanvasGroup>();
            DuelUiKit.AddImage(bg.gameObject, DuelUiKit.WhiteSprite, new Color(0f, 0f, 0f, 0.72f));

            var win = DuelUiKit.CreateMenuPanel(bg, "SettingsWin",
                new Vector2(.22f, .08f), new Vector2(.78f, .92f));
            _settingsPanel = win.gameObject;

            var hdr = DuelUiKit.CreateRect("Hdr", win,
                new Vector2(0f, 0.87f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            DuelUiKit.AddImage(hdr.gameObject, DuelUiKit.WhiteSprite, DuelUiKit.PanelHeader);
            DuelUiKit.AddText(hdr, "T", "환경 설정", 22, TextAnchor.MiddleCenter, DuelUiKit.GoldAccent, FontStyle.Bold);

            // SFX Volume Row
            var row1 = DuelUiKit.CreateRect("SfxRow", win, new Vector2(.08f, .784f), new Vector2(.92f, .862f), Vector2.zero, Vector2.zero);
            var sfxLabel = DuelUiKit.AddText(row1, "L", "효과음 음량: 65%", 17, TextAnchor.MiddleLeft, DuelUiKit.TextPrimary);
            var toggleSfx = DuelUiKit.CreateRect("BtnMute", row1, new Vector2(0.60f, 0.1f), new Vector2(1f, 0.9f), Vector2.zero, Vector2.zero);
            DuelUiKit.CreateButton(toggleSfx, "B", "음소거 전환", () =>
            {
                DuelAudio.SfxVolume = DuelAudio.SfxVolume > 0.05f ? 0f : 0.65f;
                sfxLabel.text = DuelAudio.SfxVolume > 0.05f ? "효과음 음량: 65%" : "효과음 음량: 음소거 (0%)";
            }, 15);

            // BGM Volume Row
            var rowBgm = DuelUiKit.CreateRect("BgmRow", win, new Vector2(.08f, .696f), new Vector2(.92f, .774f), Vector2.zero, Vector2.zero);
            var bgmLabel = DuelUiKit.AddText(rowBgm, "L", "배경음악 음량: 35%", 17, TextAnchor.MiddleLeft, DuelUiKit.TextPrimary);
            var toggleBgm = DuelUiKit.CreateRect("BtnBgm", rowBgm, new Vector2(0.60f, 0.1f), new Vector2(1f, 0.9f), Vector2.zero, Vector2.zero);
            DuelUiKit.CreateButton(toggleBgm, "B", "음악 전환", () =>
            {
                DuelAudio.BgmVolume = DuelAudio.BgmVolume > 0.05f ? 0f : 0.35f;
                DuelAudio.UpdateBgmVolume();
                bgmLabel.text = DuelAudio.BgmVolume > 0.05f ? "배경음악 음량: 35%" : "배경음악: 음소거 (0%)";
            }, 15);

            // AI Speed / Level Row
            var row2 = DuelUiKit.CreateRect("AiRow", win, new Vector2(.08f, .608f), new Vector2(.92f, .686f), Vector2.zero, Vector2.zero);
            var aiLbl = DuelUiKit.AddText(row2, "L", $"AI 행동 간격: {_app.AiStepDelay:0.00}초", 17, TextAnchor.MiddleLeft, DuelUiKit.TextPrimary);
            var spdBtn = DuelUiKit.CreateRect("BtnSpd", row2, new Vector2(0.60f, 0.1f), new Vector2(1f, 0.9f), Vector2.zero, Vector2.zero);
            DuelUiKit.CreateButton(spdBtn, "B", "속도 전환 (빠름/보통)", () =>
            {
                _app.AiStepDelay = _app.AiStepDelay > 0.30f ? 0.15f : 0.45f;
                aiLbl.text = $"AI 행동 간격: {_app.AiStepDelay:0.00}초";
            }, 15);

            var difficulty = DuelUiKit.CreateRect("AiDiffRow", win, new Vector2(.08f, .520f), new Vector2(.92f, .598f),
                Vector2.zero, Vector2.zero);
            var difficultyLabel = DuelUiKit.CreateRect("Label", difficulty, Vector2.zero, new Vector2(.59f, 1f),
                Vector2.zero, Vector2.zero);
            _aiDifficultyLabel = DuelUiKit.AddFittedText(difficultyLabel, "Text", "", 17, 12,
                TextAnchor.MiddleLeft, DuelUiKit.TextPrimary);
            RefreshAiDifficulty();
            var toggleDifficulty = DuelUiKit.CreateRect("BtnDiff", difficulty, new Vector2(.60f, .1f),
                new Vector2(1f, .9f), Vector2.zero, Vector2.zero);
            DuelUiKit.CreateButton(toggleDifficulty, "B", "난이도 전환", () =>
            {
                _app.ToggleAiDifficulty();
                RefreshAiDifficulty();
            }, 15);

            var replay = DuelUiKit.CreateRect("PersonalReplay", win, new Vector2(.08f, .432f), new Vector2(.92f, .510f),
                Vector2.zero, Vector2.zero);
            var replayLabel = DuelUiKit.AddFittedText(DuelUiKit.CreateRect("Label", replay,
                new Vector2(0f, .40f), new Vector2(.59f, 1f), Vector2.zero, Vector2.zero), "Text",
                $"개인 대국 기록: {(_app.PersonalReplayEnabled ? "켜짐" : "꺼짐")}", 17, 12,
                TextAnchor.MiddleLeft, DuelUiKit.TextPrimary);
            DuelUiKit.AddFittedText(DuelUiKit.CreateRect("Hint", replay,
                Vector2.zero, new Vector2(.59f, .42f), Vector2.zero, Vector2.zero), "Text",
                "다음 대국부터 · 이 기기에만 저장", 12, 10, TextAnchor.MiddleLeft, DuelUiKit.TextMuted);
            DuelUiKit.CreateButton(DuelUiKit.CreateRect("Toggle", replay,
                new Vector2(.60f, .1f), new Vector2(1f, .9f), Vector2.zero, Vector2.zero), "Button", "기록 켜기 / 끄기", () =>
                {
                    _app.SetPersonalReplayEnabled(!_app.PersonalReplayEnabled);
                    replayLabel.text = $"개인 대국 기록: {(_app.PersonalReplayEnabled ? "켜짐" : "꺼짐")}";
                }, 15);

            var artwork = DuelUiKit.CreateRect("DuelArtwork", win, new Vector2(.08f, .217f), new Vector2(.92f, .417f),
                Vector2.zero, Vector2.zero);
            var status = DuelUiKit.CreateRect("DuelArtworkStatus", artwork, new Vector2(0f, .53f), new Vector2(.59f, 1f),
                Vector2.zero, Vector2.zero);
            _duelArtStatus = DuelUiKit.AddFittedText(status, "Label", "", 17, 12, TextAnchor.MiddleLeft,
                DuelUiKit.TextPrimary, FontStyle.Bold);
            _duelArtStatus.supportRichText = true;
            var hint = DuelUiKit.CreateRect("DuelArtworkHelp", artwork, Vector2.zero, new Vector2(.59f, .53f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddFittedText(hint, "Label", Application.platform == RuntimePlatform.IPhonePlayer
                ? "파일 앱 → 나의 iPhone → YGO Portable\nDuelVisuals 안에 cardart.pack 넣기"
                : "저장 폴더에 DuelVisuals를 넣은 뒤\n다시 불러오기를 누르세요.", 14, 11,
                TextAnchor.MiddleLeft, DuelUiKit.TextMuted);
            bool supportsZip = DuelVisualsZipPicker.Supported;
            var reload = DuelUiKit.CreateRect("ReloadDuelArtwork", artwork, new Vector2(.60f, .53f),
                new Vector2(supportsZip ? .80f : 1f, .96f), Vector2.zero, Vector2.zero);
            DuelUiKit.CreateButton(reload, "Button", "다시 불러오기", ReloadDuelArtwork, supportsZip ? 13 : 15);
            if (supportsZip)
            {
                var import = DuelUiKit.CreateRect("ImportDuelArtwork", artwork, new Vector2(.81f, .53f),
                    new Vector2(1f, .96f), Vector2.zero, Vector2.zero);
                DuelUiKit.CreateButton(import, "Button", "ZIP 가져오기", () => _app.StartCoroutine(ImportDuelArtwork()), 13);
            }
            var toast = DuelUiKit.CreateRect("ArtworkToast", artwork, new Vector2(.60f, 0f), new Vector2(1f, .49f),
                Vector2.zero, Vector2.zero);
            _duelArtToast = DuelUiKit.AddFittedText(toast, "Label", "", 14, 10, TextAnchor.MiddleCenter, DuelUiKit.GoldAccent);
            _duelArtToast.gameObject.SetActive(false);
            RefreshDuelArtStatus();

            var info = DuelUiKit.CreateRect("Rule", win, new Vector2(.08f, .125f), new Vector2(.51f, .20f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddFittedText(info, "Text", "신 마스터 룰 2020", 17, 12, TextAnchor.MiddleLeft, DuelUiKit.TextMuted);
            var cloud = DuelUiKit.CreateRect("CloudSave", win, new Vector2(.56f, .125f), new Vector2(.92f, .20f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.CreateButton(cloud, "Button", "클라우드 저장", _app.OpenSaveSync, 16);

            // Close & Quit
            var closeRt = DuelUiKit.CreateRect("Close", win, new Vector2(0.55f, 0.03f), new Vector2(0.92f, 0.12f), Vector2.zero, Vector2.zero);
            _settingsCloseButton = DuelUiKit.CreateButton(closeRt, "BtnClose", "닫기", () => CloseSettings(), 18, gold: true);

            var quitRt = DuelUiKit.CreateRect("Quit", win, new Vector2(0.08f, 0.03f), new Vector2(0.45f, 0.12f), Vector2.zero, Vector2.zero);
            DuelUiKit.CreateButton(quitRt, "BtnQuit", "게임 종료", () => Application.Quit(), 18);

            SettingsRoot.SetActive(false);
        }

        private void RefreshDuelArtStatus()
        {
            if (!CardArtLoader.IsDuelArtActive)
                _duelArtStatus.text = "<color=#FFAA00>카드 일러스트: 미연동</color>";
            else
            {
                string source = CardArtLoader.HasExternalDuelArt ? "별도 일러스트 팩" :
                    CardDataPack.Root != null ? "카드 데이터 ZIP" : "내장 팩";
                _duelArtStatus.text = $"<color=#55FF55>카드 일러스트: " +
                    $"{source} ({CardArtLoader.ActiveDuelArtCount:N0}장 감지)</color>";
            }
        }

        private void ReloadDuelArtwork()
        {
            CardArtLoader.Reload();
            DuelVisualAssets.ReloadCutIns();
            var sleeve = CardArtLoader.GetCardBackSprite();
            foreach (Transform card in _shopCardBacks) card.GetComponent<Image>().sprite = sleeve;
            RefreshDuelArtStatus();
            ShowDuelArtNotice(CardArtLoader.HasExternalDuelArt ? "일러스트를 다시 불러왔습니다."
                : CardDataPack.Root != null && CardArtLoader.IsDuelArtActive ? "카드 데이터 ZIP의 일러스트를 다시 불러왔습니다."
                : CardArtLoader.HasExternalDuelArtFile
                    ? "cardart.pack을 읽지 못했습니다."
                    : "DuelVisuals 안에 cardart.pack이 없습니다.");
        }

        private IEnumerator ImportDuelArtwork()
        {
            if (_importingDuelVisuals) yield break;
            _importingDuelVisuals = true;
            _settingsControls.interactable = false;
            ShowDuelArtNotice("ZIP을 가져오는 중입니다…", persistent: true);
            var import = DuelVisualsZipPicker.Import(CardArtLoader.LocalDuelVisualsPath);
            while (!import.IsCompleted) yield return null;
            _importingDuelVisuals = false;
            _settingsControls.interactable = true;

            if (import.IsFaulted)
            {
                Exception error = import.Exception.GetBaseException();
                Debug.LogWarning($"[DuelVisuals] ZIP import failed: {error.Message}");
                ShowDuelArtNotice(error is InvalidDataException ? error.Message
                    : "ZIP을 가져오지 못했습니다. 파일과 저장 공간을 확인하세요.", persistent: true);
            }
            else if (!import.IsCanceled && import.Result)
                ReloadDuelArtwork();
            else
                ShowDuelArtNotice("ZIP 선택을 취소했습니다.");
        }

        private void ShowDuelArtNotice(string message, bool persistent = false)
        {
            _duelArtToast.text = message;
            _duelArtToast.gameObject.SetActive(true);
            if (_duelArtToastTimer != null) _app.StopCoroutine(_duelArtToastTimer);
            _duelArtToastTimer = persistent ? null : _app.StartCoroutine(HideDuelArtToast());
        }

        private IEnumerator HideDuelArtToast()
        {
            yield return new WaitForSecondsRealtime(2.5f);
            _duelArtToast.gameObject.SetActive(false);
            _duelArtToastTimer = null;
        }
    }
}
