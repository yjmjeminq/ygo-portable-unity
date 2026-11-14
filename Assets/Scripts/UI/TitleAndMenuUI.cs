using System;
using UnityEngine;
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

        public TitleAndMenuUI(GameAppController app, Transform canvasParent)
        {
            _app = app;
            BuildTitleScreen(canvasParent);
            BuildMainMenuScreen(canvasParent);
            BuildSettingsOverlay(canvasParent);
        }

        private void BuildTitleScreen(Transform parent)
        {
            var rt = LotdUiKit.FillRect("Screen_Title", parent);
            TitleRoot = rt.gameObject;

            // LOTD cyber tech background
            var bg = LotdUiKit.FillRect("Bg", rt);
            var bgSp = LotdUiKit.LoadSprite("LOTD/Background/noise_bgtec_small");
            LotdUiKit.AddImage(bg.gameObject, bgSp, bgSp != null ? new Color(0.65f, 0.85f, 1f, 1f) : LotdUiKit.BgDeep);

            // Subtle vignette & cyber frame
            var dim = LotdUiKit.FillRect("Vignette", rt);
            LotdUiKit.AddImage(dim.gameObject, LotdUiKit.WhiteSprite, new Color(0.01f, 0.04f, 0.11f, 0.55f));

            // Center title logo
            var logoRt = LotdUiKit.CreateRect("Logo", rt,
                new Vector2(0.18f, 0.42f), new Vector2(0.82f, 0.86f), Vector2.zero, Vector2.zero);
            var logoSp = LotdUiKit.LoadSprite("LOTD/Title/logo_us") ?? LotdUiKit.LoadSprite("LOTD/Title/logo_jp");
            if (logoSp != null)
            {
                LotdUiKit.AddImage(logoRt.gameObject, logoSp, Color.white, true);
            }
            else
            {
                LotdUiKit.AddText(logoRt, "TitleFallback", "유희왕! 레거시 오브 더 듀얼리스트\nPORTABLE 2026", 46, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);
            }

            // Subtitle badge
            var subRt = LotdUiKit.CreateBorderedPanel(rt, "SubBadge",
                new Vector2(0.27f, 0.31f), new Vector2(0.73f, 0.39f), Vector2.zero, Vector2.zero,
                new Color(0.03f, 0.10f, 0.24f, 0.90f), LotdUiKit.GoldAccent);
            LotdUiKit.AddText(subRt, "SubTxt", "LINK EVOLUTION 오프라인 한글판  •  2026.11 메타 & 오리지널 완전판",
                19, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

            // Play / Start button
            var btnHolder = LotdUiKit.CreateRect("PlayBtn", rt,
                new Vector2(0.35f, 0.13f), new Vector2(0.65f, 0.24f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(btnHolder, "Btn_Start", "게임 시작  (PRESS START)", () => _app.ShowScreen(AppScreen.MainMenu), 24, gold: true);

            // Footer info
            var ft = LotdUiKit.CreateRect("Footer", rt,
                new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.08f), Vector2.zero, Vector2.zero);
            int cardCnt = CardDatabase.IsLoaded ? CardDatabase.AllCards.Count : 0;
            int deckCnt = DeckRepository.PresetDecks.Count;
            LotdUiKit.AddText(ft, "FtTxt",
                $"완전 오프라인 독립 엔진 (OpenDuel + YGOPro-Core)  |  수록 카드 {cardCnt:N0}장  |  대전 상대 덱 {deckCnt:N0}개",
                15, TextAnchor.MiddleCenter, LotdUiKit.TextMuted);
        }

        private void BuildMainMenuScreen(Transform parent)
        {
            var rt = LotdUiKit.FillRect("Screen_MainMenu", parent);
            MenuRoot = rt.gameObject;

            var bg = LotdUiKit.FillRect("Bg", rt);
            var bgSp = LotdUiKit.LoadSprite("LOTD/Background/noise_bgtec_small");
            LotdUiKit.AddImage(bg.gameObject, bgSp, bgSp != null ? new Color(0.55f, 0.78f, 1f, 1f) : LotdUiKit.BgDeep);

            var overlay = LotdUiKit.FillRect("Dim", rt);
            LotdUiKit.AddImage(overlay.gameObject, LotdUiKit.WhiteSprite, new Color(0.02f, 0.06f, 0.15f, 0.60f));

            // Top LOTD Header bar
            var hdr = LotdUiKit.CreateBorderedPanel(rt, "TopBar",
                new Vector2(0f, 0.90f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero,
                LotdUiKit.PanelHeader, LotdUiKit.CyanAccent);
            LotdUiKit.AddText(hdr, "HdrTitle", "  유희왕! LEGACY OF THE DUELIST — 오프라인 메인 메뉴", 24, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);

            // Left Showcase Character & Blue-Eyes/Dark Magician Art Panel
            var showcase = LotdUiKit.CreateBorderedPanel(rt, "Showcase",
                new Vector2(0.05f, 0.10f), new Vector2(0.47f, 0.85f), Vector2.zero, Vector2.zero);

            var bustRt = LotdUiKit.CreateRect("HeroBust", showcase,
                new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.94f), Vector2.zero, Vector2.zero);
            var heroBust = LotdUiKit.LoadSprite("LOTD/Busts/yamiyugi_neutral") ?? LotdUiKit.LoadSprite("LOTD/Busts/atem_neutral");
            LotdUiKit.AddImage(bustRt.gameObject, heroBust, Color.white, true);

            var capRt = LotdUiKit.CreateRect("HeroCap", showcase,
                new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.22f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddText(capRt, "Info",
                "오프라인 AI 듀얼 & 덱 에디터 전용 에디션\n• 2026.11 최신 메타 덱 20종 + 원작 850개 덱 완비\n• 환상마족 포함 전 종족 & 14,884장 한글 데이터베이스",
                16, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary);

            // Right LOTD Menu Button Stack
            var menuFrame = LotdUiKit.CreateBorderedPanel(rt, "MenuList",
                new Vector2(0.51f, 0.10f), new Vector2(0.95f, 0.85f), Vector2.zero, Vector2.zero);

            AddMenuCardButton(menuFrame, "FreeDuel", 0.72f, 0.92f,
                "프리 듀얼  (오프라인 AI 대전)",
                "2026 메타 20강 및 DM~VRAINS 전 시리즈 상대와 즉시 대전",
                true, () => _app.ShowScreen(AppScreen.OpponentSelect));

            AddMenuCardButton(menuFrame, "DeckEdit", 0.49f, 0.69f,
                "덱 편집기  (트렁크 / 덱 수정)",
                "14,884장 한글 카드 검색 • 종족/속성/효과 필터 • 커스텀 덱 제작 & 프리셋 복제",
                false, () => _app.ShowScreen(AppScreen.DeckEditor));

            AddMenuCardButton(menuFrame, "Settings", 0.26f, 0.46f,
                "환경 설정  (AI / 사운드 / 규칙)",
                "AI 사고 레벨 • 효과음 볼륨 • 룰(Master Rule 5) 확인",
                false, () => SettingsRoot.SetActive(true));

            AddMenuCardButton(menuFrame, "BackTitle", 0.06f, 0.21f,
                "타이틀로 / 게임 종료",
                "타이틀 화면으로 돌아가거나 게임을 안전하게 종료합니다",
                false, () => _app.ShowScreen(AppScreen.Title));
        }

        private void AddMenuCardButton(Transform parent, string name, float yMin, float yMax,
            string title, string desc, bool gold, Action onClick)
        {
            var slot = LotdUiKit.CreateRect(name, parent,
                new Vector2(0.06f, yMin), new Vector2(0.94f, yMax), Vector2.zero, Vector2.zero);
            var btn = LotdUiKit.CreateLotdButton(slot, "Btn", title, onClick, 22, gold: gold);
            var lbl = btn.GetComponentInChildren<Text>();
            if (lbl != null) lbl.rectTransform.offsetMin = new Vector2(0f, 14f);

            var sub = LotdUiKit.CreateRect("Sub", slot,
                new Vector2(0.08f, 0.12f), new Vector2(0.92f, 0.34f), Vector2.zero, Vector2.zero);
            var txt = LotdUiKit.AddText(sub, "Desc", desc, 13, TextAnchor.MiddleCenter, LotdUiKit.TextMuted);
            txt.raycastTarget = false;
        }

        private void BuildSettingsOverlay(Transform parent)
        {
            var bg = LotdUiKit.FillRect("Overlay_Settings", parent);
            SettingsRoot = bg.gameObject;
            LotdUiKit.AddImage(bg.gameObject, LotdUiKit.WhiteSprite, new Color(0f, 0f, 0f, 0.72f));

            var win = LotdUiKit.CreateBorderedPanel(bg, "SettingsWin",
                new Vector2(0.28f, 0.22f), new Vector2(0.72f, 0.78f), Vector2.zero, Vector2.zero,
                LotdUiKit.PanelNavy, LotdUiKit.CyanAccent);

            var hdr = LotdUiKit.CreateRect("Hdr", win,
                new Vector2(0f, 0.84f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(hdr.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelHeader);
            LotdUiKit.AddText(hdr, "T", "환경 설정 (SETTINGS)", 22, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);

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
            LotdUiKit.CreateLotdButton(closeRt, "BtnClose", "닫기", () => SettingsRoot.SetActive(false), 18, gold: true);

            var quitRt = LotdUiKit.CreateRect("Quit", win, new Vector2(0.08f, 0.05f), new Vector2(0.45f, 0.17f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(quitRt, "BtnQuit", "게임 종료", () => Application.Quit(), 18);

            SettingsRoot.SetActive(false);
        }
    }
}
