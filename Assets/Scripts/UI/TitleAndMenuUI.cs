using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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
        private Button _deckEditorButton;
        private Button _shopButton;
        private Text _shopBalance;
        private Button _settingsOpenButton;
        private Button _settingsCloseButton;
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
            SettingsRoot.SetActive(true);
            UiEntranceMotion.Play(_settingsPanel, .57f);
            EventSystem.current?.SetSelectedGameObject(_settingsCloseButton.gameObject);
        }

        internal void CloseSettings(bool restoreFocus = true)
        {
            SettingsRoot.SetActive(false);
            _menuControls.interactable = true;
            if (restoreFocus) EventSystem.current?.SetSelectedGameObject(_settingsOpenButton.gameObject);
        }

        internal void SetSaveSyncOpen(bool open)
        {
            _titleControls.interactable = !open;
            _settingsControls.interactable = !open;
            _menuControls.interactable = !open && !SettingsRoot.activeSelf;
        }

        internal void RefreshWallet() => _shopBalance.text = $"보유 {CollectionProgress.Balance:N0} DP";

        internal void FocusMainMenu(AppScreen previousScreen)
        {
            RefreshWallet();
            var selected = previousScreen switch
            {
                AppScreen.CardShop => _shopButton,
                AppScreen.DeckEditor => _deckEditorButton,
                _ => _freeDuelButton,
            };
            EventSystem.current?.SetSelectedGameObject(selected.gameObject);
        }

        private void BuildTitleScreen(Transform parent)
        {
            var root = LotdUiKit.FillRect("Screen_Title", parent);
            TitleRoot = root.gameObject;
            _titleControls = TitleRoot.AddComponent<CanvasGroup>();
            AddTitleBackground(root);

            var logo = LotdUiKit.CreateRect("Logo", root, new Vector2(0.22f, 0.40f), new Vector2(0.78f, 0.83f),
                Vector2.zero, Vector2.zero);
            AddLogo(logo);
            var start = LotdUiKit.CreateRect("Start", root, new Vector2(0.375f, 0.205f),
                new Vector2(0.625f, 0.292f), Vector2.zero, Vector2.zero);
            MenuButtonMotion.Attach(LotdUiKit.CreateLotdButton(start, "Button", "게임 시작",
                () => _app.ShowScreen(AppScreen.MainMenu), 25, true));
            var cloud = LotdUiKit.CreateRect("CloudSave", root, new Vector2(.72f, .078f),
                new Vector2(.91f, .148f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(cloud, "Button", "클라우드 저장", _app.OpenSaveSync, 19);
        }

        private void BuildMainMenuScreen(Transform parent)
        {
            var root = LotdUiKit.FillRect("Screen_MainMenu", parent);
            MenuRoot = root.gameObject;
            _menuControls = MenuRoot.AddComponent<CanvasGroup>();
            AddTitleBackground(root);

            var logo = LotdUiKit.CreateRect("Logo", root, new Vector2(0.31f, 0.674f), new Vector2(0.69f, 0.938f),
                Vector2.zero, Vector2.zero);
            AddLogo(logo);
            var menu = LotdUiKit.CreateMenuPanel(root, "Menu", new Vector2(.205f, .132f),
                new Vector2(.538f, .643f));
            _freeDuelButton = AddMenuEntry(menu, "FreeDuel", .745f, "프리 듀얼", () => _app.ShowScreen(AppScreen.OpponentSelect));
            _deckEditorButton = AddMenuEntry(menu, "DeckEditor", .54f, "덱 편집", () => _app.ShowScreen(AppScreen.DeckEditor));
            _settingsOpenButton = AddMenuEntry(menu, "Settings", .335f, "환경 설정", OpenSettings);
            AddMenuEntry(menu, "ReturnToTitle", .13f, "타이틀로 돌아가기", () => _app.ShowScreen(AppScreen.Title));
            BuildShopTile(root);
        }

        private void BuildShopTile(Transform root)
        {
            var tile = LotdUiKit.CreateMenuPanel(root, "ShopTile", new Vector2(.555f, .132f), new Vector2(.795f, .643f));
            var hitbox = LotdUiKit.FillRect("Button", tile, 7f);
            var target = LotdUiKit.AddImage(hitbox.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            _shopButton = hitbox.gameObject.AddComponent<Button>();
            _shopButton.targetGraphic = target;
            _shopButton.transition = Selectable.Transition.None;
            _shopButton.onClick.AddListener(() =>
            {
                LotdAudio.PlayClick();
                _app.ShowScreen(AppScreen.CardShop);
            });
            MenuButtonMotion.Attach(_shopButton);

            var cardBack = CardArtLoader.GetCardBackSprite();
            var cards = LotdUiKit.CreateRect("Cards", tile, new Vector2(.12f, .385f), new Vector2(.88f, .885f),
                Vector2.zero, Vector2.zero);
            void AddCard(float left, float right, float angle, Color tint)
            {
                var card = LotdUiKit.CreateRect("Card", cards, new Vector2(left, .02f), new Vector2(right, .98f),
                    Vector2.zero, Vector2.zero);
                card.localRotation = Quaternion.Euler(0f, 0f, angle);
                LotdUiKit.AddImage(card.gameObject, cardBack, tint, true, false);
            }
            if (cardBack != null)
            {
                var sideTint = new Color(.75f, .81f, .94f);
                AddCard(.09f, .525f, 13f, sideTint);
                AddCard(.475f, .91f, -13f, sideTint);
                AddCard(.2825f, .7175f, 0f, Color.white);
            }

            var wallet = LotdUiKit.CreateRect("Wallet", tile, new Vector2(.12f, .277f), new Vector2(.88f, .365f),
                Vector2.zero, Vector2.zero);
            _shopBalance = LotdUiKit.AddFittedText(wallet, "Text", "", 23, 14, TextAnchor.MiddleCenter,
                LotdUiKit.GoldAccent, FontStyle.Bold);

            var action = LotdUiKit.CreateRect("Action", tile, new Vector2(.11f, .10f), new Vector2(.89f, .24f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(action.gameObject, LotdUiKit.LoadSprite("LOTD/Buttons_Yellow/ds_btn_inactive01"),
                Color.white, false, false);
            LotdUiKit.AddFittedText(action, "Label", "카드 상점", 26, 17, TextAnchor.MiddleCenter,
                LotdUiKit.TextPrimary, FontStyle.Bold);
        }

        private static void AddTitleBackground(Transform root)
        {
            LotdUiKit.AddImage(LotdUiKit.FillRect("Backdrop", root).gameObject,
                LotdUiKit.LoadSprite("LOTD/Background/bg_vrains"), new Color(0.53f, 0.64f, 0.80f), false, false);
        }

        private static void AddLogo(RectTransform holder)
        {
            var logo = DuelVisualAssets.MasterDuelLogo;
            if (logo != null) LotdUiKit.AddImage(holder.gameObject, logo, Color.white, true, false);
            else LotdUiKit.AddFittedText(holder, "Title", "Yu-Gi-Oh!\nMASTER DUEL", 58, 21,
                TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        }

        private static Button AddMenuEntry(Transform parent, string name, float y, string label, Action action)
        {
            var row = LotdUiKit.CreateRect(name, parent, new Vector2(0.097f, y), new Vector2(0.91f, y + .137f),
                Vector2.zero, Vector2.zero);
            return MenuButtonMotion.Attach(LotdUiKit.CreateLotdButton(row, "Button", label, action, 23));
        }

        private void BuildSettingsOverlay(Transform parent)
        {
            var bg = LotdUiKit.FillRect("Overlay_Settings", parent);
            SettingsRoot = bg.gameObject;
            _settingsControls = SettingsRoot.AddComponent<CanvasGroup>();
            LotdUiKit.AddImage(bg.gameObject, LotdUiKit.WhiteSprite, new Color(0f, 0f, 0f, 0.72f));

            var win = LotdUiKit.CreateMenuPanel(bg, "SettingsWin",
                new Vector2(0.28f, 0.22f), new Vector2(0.72f, 0.78f));
            _settingsPanel = win.gameObject;

            var hdr = LotdUiKit.CreateRect("Hdr", win,
                new Vector2(0f, 0.84f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(hdr.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelHeader);
            LotdUiKit.AddText(hdr, "T", "환경 설정", 22, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

            // SFX Volume Row
            var row1 = LotdUiKit.CreateRect("SfxRow", win, new Vector2(0.08f, 0.67f), new Vector2(0.92f, 0.79f), Vector2.zero, Vector2.zero);
            var sfxLabel = LotdUiKit.AddText(row1, "L", "효과음 음량: 65%", 17, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary);
            var toggleSfx = LotdUiKit.CreateRect("BtnMute", row1, new Vector2(0.60f, 0.1f), new Vector2(1f, 0.9f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(toggleSfx, "B", "음소거 전환", () =>
            {
                LotdAudio.SfxVolume = LotdAudio.SfxVolume > 0.05f ? 0f : 0.65f;
                sfxLabel.text = LotdAudio.SfxVolume > 0.05f ? "효과음 음량: 65%" : "효과음 음량: 음소거 (0%)";
            }, 15);

            // BGM Volume Row
            var rowBgm = LotdUiKit.CreateRect("BgmRow", win, new Vector2(0.08f, 0.52f), new Vector2(0.92f, 0.64f), Vector2.zero, Vector2.zero);
            var bgmLabel = LotdUiKit.AddText(rowBgm, "L", "배경음악 음량: 35%", 17, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary);
            var toggleBgm = LotdUiKit.CreateRect("BtnBgm", rowBgm, new Vector2(0.60f, 0.1f), new Vector2(1f, 0.9f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(toggleBgm, "B", "음악 전환", () =>
            {
                LotdAudio.BgmVolume = LotdAudio.BgmVolume > 0.05f ? 0f : 0.35f;
                LotdAudio.UpdateBgmVolume();
                bgmLabel.text = LotdAudio.BgmVolume > 0.05f ? "배경음악 음량: 35%" : "배경음악: 음소거 (0%)";
            }, 15);

            // AI Speed / Level Row
            var row2 = LotdUiKit.CreateRect("AiRow", win, new Vector2(0.08f, 0.37f), new Vector2(0.92f, 0.49f), Vector2.zero, Vector2.zero);
            var aiLbl = LotdUiKit.AddText(row2, "L", $"AI 사고 속도: {_app.AiStepDelay:0.00}초", 17, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary);
            var spdBtn = LotdUiKit.CreateRect("BtnSpd", row2, new Vector2(0.60f, 0.1f), new Vector2(1f, 0.9f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(spdBtn, "B", "속도 전환 (빠름/보통)", () =>
            {
                _app.AiStepDelay = _app.AiStepDelay > 0.30f ? 0.15f : 0.45f;
                aiLbl.text = $"AI 사고 속도: {_app.AiStepDelay:0.00}초";
            }, 15);

            var info = LotdUiKit.CreateRect("Rule", win, new Vector2(.08f, .22f), new Vector2(.51f, .34f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(info, "Text", "신 마스터 룰 2020", 17, 12, TextAnchor.MiddleLeft, LotdUiKit.TextMuted);
            var cloud = LotdUiKit.CreateRect("CloudSave", win, new Vector2(.56f, .22f), new Vector2(.92f, .34f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(cloud, "Button", "클라우드 저장", _app.OpenSaveSync, 16);

            // Close & Quit
            var closeRt = LotdUiKit.CreateRect("Close", win, new Vector2(0.55f, 0.05f), new Vector2(0.92f, 0.17f), Vector2.zero, Vector2.zero);
            _settingsCloseButton = LotdUiKit.CreateLotdButton(closeRt, "BtnClose", "닫기", () => CloseSettings(), 18, gold: true);

            var quitRt = LotdUiKit.CreateRect("Quit", win, new Vector2(0.08f, 0.05f), new Vector2(0.45f, 0.17f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(quitRt, "BtnQuit", "게임 종료", () => Application.Quit(), 18);

            SettingsRoot.SetActive(false);
        }
    }
}
