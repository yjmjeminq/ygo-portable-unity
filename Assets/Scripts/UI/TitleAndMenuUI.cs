using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    public class TitleAndMenuUI
    {
        public GameObject TitleRoot { get; private set; }
        public GameObject MenuRoot { get; private set; }
        public GameObject SettingsRoot { get; private set; }

        private readonly GameAppController _app;
        private CanvasGroup _menuControls;
        private Button _settingsOpenButton;
        private Button _settingsCloseButton;

        public TitleAndMenuUI(GameAppController app, Transform canvasParent)
        {
            _app = app;
            BuildTitleScreen(canvasParent);
            BuildMainMenuScreen(canvasParent);
            BuildSettingsOverlay(canvasParent);
        }

        internal void Update()
        {
            if (SettingsRoot.activeInHierarchy && Input.GetButtonDown("Cancel")) CloseSettings();
        }

        private void OpenSettings()
        {
            _menuControls.interactable = false;
            SettingsRoot.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(_settingsCloseButton.gameObject);
        }

        internal void CloseSettings(bool restoreFocus = true)
        {
            SettingsRoot.SetActive(false);
            _menuControls.interactable = true;
            if (restoreFocus) EventSystem.current?.SetSelectedGameObject(_settingsOpenButton.gameObject);
        }

        private void BuildTitleScreen(Transform parent)
        {
            var root = LotdUiKit.FillRect("Screen_Title", parent);
            TitleRoot = root.gameObject;
            AddTitleBackground(root);

            var logo = LotdUiKit.CreateRect("Logo", root, new Vector2(0.069f, 0.46f), new Vector2(0.61f, 0.88f),
                Vector2.zero, Vector2.zero);
            AddLogo(logo);
            var start = LotdUiKit.CreateRect("Start", root, new Vector2(0.166f, 0.26f),
                new Vector2(0.496f, 0.355f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(start, "Button", "게임 시작", () => _app.ShowScreen(AppScreen.MainMenu), 25, true);
        }

        private void BuildMainMenuScreen(Transform parent)
        {
            var root = LotdUiKit.FillRect("Screen_MainMenu", parent);
            MenuRoot = root.gameObject;
            _menuControls = MenuRoot.AddComponent<CanvasGroup>();
            AddTitleBackground(root);

            var logo = LotdUiKit.CreateRect("Logo", root, new Vector2(0.049f, 0.61f), new Vector2(0.503f, 0.938f),
                Vector2.zero, Vector2.zero);
            AddLogo(logo);
            var menu = LotdUiKit.CreateMenuPanel(root, "Menu", new Vector2(0.081f, 0.126f),
                new Vector2(0.489f, 0.611f));
            AddMenuEntry(menu, 0.745f, "프리 듀얼", () => _app.ShowScreen(AppScreen.OpponentSelect), true);
            AddMenuEntry(menu, 0.54f, "덱 편집", () => _app.ShowScreen(AppScreen.DeckEditor));
            _settingsOpenButton = AddMenuEntry(menu, 0.335f, "환경 설정", OpenSettings);
            AddMenuEntry(menu, 0.13f, "타이틀로 돌아가기", () => _app.ShowScreen(AppScreen.Title));
        }

        private static void AddTitleBackground(Transform root)
        {
            LotdUiKit.AddImage(LotdUiKit.FillRect("Backdrop", root).gameObject,
                LotdUiKit.LoadSprite("LOTD/Background/bg_vrains"), Color.white, false, false);
            var glow = LotdUiKit.CreateRect("Light", root, new Vector2(0.36f, 0.09f), new Vector2(0.99f, 0.90f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(glow.gameObject, LotdUiKit.LoadSprite("LOTD/Effects/summon_flare"),
                new Color(0.26f, 0.57f, 1f, 0.37f), true, false);

            var hero = LotdUiKit.CreateRect("Character", root, new Vector2(0.48f, -0.025f), new Vector2(0.998f, 0.96f),
                Vector2.zero, Vector2.zero);
            var artwork = LotdUiKit.LoadSprite("LOTD/Title/magician_girl") ?? LotdUiKit.LoadSprite("LOTD/Busts/atem_neutral");
            if (artwork != null) LotdUiKit.AddImage(hero.gameObject, artwork, Color.white, true, false);
        }

        private static void AddLogo(RectTransform holder)
        {
            var logo = DuelVisualAssets.MasterDuelLogo;
            if (logo != null) LotdUiKit.AddImage(holder.gameObject, logo, Color.white, true, false);
            else LotdUiKit.AddFittedText(holder, "Title", "Yu-Gi-Oh!\nMASTER DUEL", 58, 21,
                TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        }

        private static Button AddMenuEntry(Transform parent, float y, string label, Action action, bool selected = false)
        {
            var row = LotdUiKit.CreateRect("MenuEntry", parent, new Vector2(0.097f, y), new Vector2(0.91f, y + 0.159f),
                Vector2.zero, Vector2.zero);
            return LotdUiKit.CreateLotdButton(row, "Button", label, action, 23, selected);
        }

        private void BuildSettingsOverlay(Transform parent)
        {
            var bg = LotdUiKit.FillRect("Overlay_Settings", parent);
            SettingsRoot = bg.gameObject;
            LotdUiKit.AddImage(bg.gameObject, LotdUiKit.WhiteSprite, new Color(0f, 0f, 0f, 0.72f));

            var win = LotdUiKit.CreateMenuPanel(bg, "SettingsWin",
                new Vector2(0.28f, 0.22f), new Vector2(0.72f, 0.78f));

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

            // Rules Info
            var info = LotdUiKit.CreateRect("Info", win, new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.38f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddText(info, "I", "적용 룰: 신 마스터 룰 2020 (Master Rule 5)\n네트워크/멀티플레이: 완전히 비활성화됨 (100% 오프라인)",
                15, TextAnchor.MiddleCenter, LotdUiKit.TextMuted);

            // Close & Quit
            var closeRt = LotdUiKit.CreateRect("Close", win, new Vector2(0.55f, 0.05f), new Vector2(0.92f, 0.17f), Vector2.zero, Vector2.zero);
            _settingsCloseButton = LotdUiKit.CreateLotdButton(closeRt, "BtnClose", "닫기", () => CloseSettings(), 18, gold: true);

            var quitRt = LotdUiKit.CreateRect("Quit", win, new Vector2(0.08f, 0.05f), new Vector2(0.45f, 0.17f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(quitRt, "BtnQuit", "게임 종료", () => Application.Quit(), 18);

            SettingsRoot.SetActive(false);
        }
    }
}
