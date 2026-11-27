using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class BeyondDestinyUI
    {
        private const string WorldArt = "TagForce/5DS/";
        private const string OpeningArt = "Maps/spot_neodomino_rain_alley.png";
        private readonly GameAppController _app;
        private readonly RectTransform _root, _panel;
        private readonly Image _background;
        private readonly Text _heading, _description;
        private readonly Button _primary, _secondary, _back;
        private readonly StoryModeUI.StoryDialogueView _dialogue;
        private readonly BeyondDestinyField _field;
        private readonly TagForceWorldImages _art = new();
        private BeyondDestinyCampaign _campaign;
        private BeyondDestinyPlayback _playback;
        private BeyondDestinyCampaign.Encounter _pending;
        private Action _primaryAction, _secondaryAction;

        internal GameObject Root => _root.gameObject;
        internal GameObject ActiveRoot => _dialogue.IsOpen ? _dialogue.ActiveRoot : _field.IsOpen ? _field.Root : _panel.gameObject;
        internal BeyondDestinyCampaign.Encounter Pending => _pending;

        internal BeyondDestinyUI(GameAppController app, Transform parent)
        {
            _app = app;
            _root = DuelUiKit.FillRect("Screen_Campaigns", parent);
            _background = DuelUiKit.AddImage(_root.gameObject, null, Color.white, false, false);
            var content = Application.isMobilePlatform ? MobileSafeArea.Create(_root) : _root;
            _panel = DuelUiKit.CreateMenuPanel(content, "CampaignSelection",
                new Vector2(.12f, .17f), new Vector2(.88f, .83f));
            RpgUiStyle.Panel(_panel);
            _heading = Label("Title", .73f, .91f, 42, FontStyle.Bold);
            _description = Label("Description", .47f, .71f, 26, FontStyle.Normal);
            _primary = Button("Primary", .21f, .30f, .79f, .44f, () => _primaryAction?.Invoke(), gold: true);
            _secondary = Button("Secondary", .21f, .15f, .79f, .28f, () => _secondaryAction?.Invoke());
            _back = Button("Back", .70f, .030f, .95f, .12f, Back);
            SetLabel(_back, "뒤로");
            _field = new BeyondDestinyField(_root);
            _dialogue = new StoryModeUI.StoryDialogueView(_root);
            Close();
        }

        private Text Label(string name, float bottom, float top, int size, FontStyle style) =>
            DuelUiKit.AddFittedText(DuelUiKit.CreateRect(name, _panel, new Vector2(.075f, bottom),
                new Vector2(.925f, top), Vector2.zero, Vector2.zero), "Text", "", size, 17,
                TextAnchor.MiddleCenter, DuelUiKit.TextPrimary, style);

        private Button Button(string name, float left, float bottom, float right, float top, Action click, bool gold = false) =>
            DuelUiKit.CreateButton(DuelUiKit.CreateRect(name, _panel, new Vector2(left, bottom),
                new Vector2(right, top), Vector2.zero, Vector2.zero), name + "Button", "", click, 26, gold);

        private static void SetLabel(Button button, string title) => button.GetComponentInChildren<Text>().text = title;

        internal void OpenSelection()
        {
            Root.SetActive(true);
            _pending = null;
            ShowPanel("스토리 RPG", "캠페인을 선택하세요.", "GX: 듀얼 아카데미아", _app.OpenGxRpgField,
                "5D’s: Beyond Destiny", () => _app.ShowScreen(AppScreen.BeyondDestiny));
        }

        internal void OpenCampaign()
        {
            Root.SetActive(true);
            try
            {
                _campaign ??= BeyondDestinyCampaign.Load(Application.streamingAssetsPath);
                _playback = new BeyondDestinyPlayback(_campaign, Application.persistentDataPath);
                Resume();
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException ||
                error is UnauthorizedAccessException || error is ArgumentException)
            {
                Debug.LogWarning($"[Beyond Destiny] {error.Message}");
                ShowPanel("5D’s: Beyond Destiny", "캠페인 데이터 또는 저장 파일을 읽을 수 없습니다.",
                    "다시 읽기", OpenCampaign);
            }
        }

        internal void Close()
        {
            _dialogue.Close();
            _field.Close();
            _background.sprite = null;
            _art.BeginPage();
            _art.Trim();
            _pending = null;
            Root.SetActive(false);
        }

        internal void Back()
        {
            if (_dialogue.HandleDisplayInput(restore: true) || _dialogue.ClosePopup()) return;
            _app.ShowScreen(_pending != null || _playback != null && _app.IsBeyondDestinyScreen
                ? AppScreen.Campaigns : AppScreen.MainMenu);
        }

        internal void Update(float delta, Vector2 direction)
        {
            _dialogue.Update(delta);
            _field.Update(delta, direction);
        }

        internal bool HandleFieldSubmit()
        {
            if (!_field.IsOpen) return false;
            _field.MoveToStory();
            return true;
        }
        internal bool RestoreHidden() => _dialogue.HandleDisplayInput(restore: true);

        private void Resume(bool fromField = false)
        {
            _pending = null;
            if (_playback.Progress.completed)
            {
                _app.RestoreBeyondDestinySoundscape(default);
                ShowPanel("5D’s: Beyond Destiny", "37화의 이야기를 모두 마쳤습니다.",
                    "캠페인 선택", () => _app.ShowScreen(AppScreen.Campaigns));
                return;
            }
            var chapter = _playback.Episode;
            var scene = _playback.Scene;
            var segment = _playback.Read();
            string location = Path.GetFileNameWithoutExtension(scene.backgroundPath);
            if (_playback.Progress.cueIndex == 0 && location != null && !_playback.Progress.unlockedLocations.Contains(location) &&
                _field.Open(scene.backgroundPath, scene.title, () =>
                {
                    if (Save(() => _playback.UnlockLocation(scene.backgroundPath))) Resume(fromField: true);
                }, _playback.FieldGuest()))
            {
                _panel.gameObject.SetActive(false);
                _dialogue.Close();
                var openingCues = segment.Pages.Length > 0 ? segment.Pages[0].Audio : segment.TailAudio;
                _app.RestoreBeyondDestinySoundscape(segment.InitialAudio.After(openingCues));
                return;
            }
            if (segment.Pages.Length == 0)
            {
                if (segment.Duel != null)
                {
                    _app.RestoreBeyondDestinySoundscape(segment.InitialAudio.After(segment.TailAudio));
                    ShowEncounter(segment.Duel);
                    return;
                }
                _app.PlayBeyondDestinyCues(segment.TailAudio);
                if (Save(() => _playback.Finish(segment))) Resume();
                return;
            }
            if (!fromField) _app.RestoreBeyondDestinySoundscape(segment.InitialAudio);
            _panel.gameObject.SetActive(false);
            var lines = segment.Pages.Select((page, index) => new StoryDialogueLine
            {
                speaker = page.Text.kind == "system" ? "시스템" : page.Text.speaker ?? "내레이션",
                text = page.Text.text,
                sceneMap = page.Map == null ? null : Path.GetFileNameWithoutExtension(page.Map),
                sceneTime = index == 0 ? scene.title : null,
                cutscene = page.Cutscene,
                fx = page.Effect,
                storyHeroPortrait = page.Text.heroPortrait,
                storyGuestPortrait = page.Text.guestPortrait,
                storySpeakerSide = page.Text.speakerSide,
            }).ToArray();
            _dialogue.Open($"제{chapter.number}화 · {chapter.title}", "주인공", (OpponentCharacter)null, lines, () =>
            {
                _app.PlayBeyondDestinyCues(segment.TailAudio);
                if (Save(() => _playback.Finish(segment))) Resume();
            }, rpg: true, rpgBackdrop: scene.backgroundPath ?? OpeningArt, rpgAssetFolder: BeyondDestinyCampaign.AssetFolder,
                onPage: index =>
                {
                    var page = segment.Pages[index];
                    Save(() =>
                    {
                        _playback.RememberPage(page.Index);
                        _playback.UnlockLocation(page.Map);
                    });
                    _app.PlayBeyondDestinyCues(page.Audio);
                });
        }

        private void ShowEncounter(BeyondDestinyCampaign.Encounter encounter, string message = null)
        {
            _pending = encounter;
            var player = _playback.Deck(encounter.playerDeckId);
            var opponent = _playback.Deck(encounter.opponentDeckId);
            string title = encounter.Riding ? "라이딩 듀얼" : "스토리 듀얼";
            int turn = BeyondDestinyPlayback.ObjectiveTurn(encounter.resolution);
            string objective = turn > 0 ? $"목표 · {turn}턴까지 듀얼 진행" :
                encounter.resolution == "sparring" ? "연습 듀얼" : "목표 · 듀얼 승리";
            string description = $"{player.charName}  VS  {opponent.charName}\n{objective}";
            if (!string.IsNullOrEmpty(message)) description += "\n" + message;
            ShowPanel(title, description, message == null ? "듀얼 시작" : "다시 도전",
                () => _app.StartBeyondDestinyDuel(encounter, player, opponent), map: BattleMap(encounter));
        }

        internal void LaunchFailed(string message)
        {
            var encounter = _playback?.Read().Duel;
            if (encounter != null) ShowEncounter(encounter, message);
        }

        internal void ShowOutcome(BeyondDestinyCampaign.Encounter encounter, bool won, bool validResult, bool objective)
        {
            if (!BeyondDestinyPlayback.CanContinue(encounter.resolution, won, validResult, objective))
            {
                ShowEncounter(encounter, validResult ? "이번 듀얼의 목표를 달성하지 못했습니다." : "듀얼 결과를 확인할 수 없습니다.");
                return;
            }
            _pending = encounter;
            string title = encounter.resolution is "interrupted" or "continues" or "canonicalOpponentWin" || objective
                ? "이야기 계속" : won ? "듀얼 승리" : "연습 듀얼 종료";
            string description = encounter.resolution == "canonicalOpponentWin" ? "대본의 결말로 이어집니다." :
                objective ? "이번 장면의 듀얼 목표를 달성했습니다." : "이번 장면의 듀얼을 마쳤습니다.";
            ShowPanel(title, description, "다음 장면", () =>
            {
                if (Save(() => _playback.Resolve(encounter))) Resume();
            }, map: BattleMap(encounter));
        }

        internal string BattleMap(BeyondDestinyCampaign.Encounter encounter) => encounter.Riding
            ? _playback.Episode.number is >= 29 and <= 31 ? "Maps/spot_wrgp_stadium.png" : "Maps/spot_night_circuit.png"
            : _playback.Scene.backgroundPath;

        internal Sprite Artwork(string path)
        {
            _art.BeginPage();
            var sprite = _art.Get(WorldArt + (path ?? OpeningArt));
            _art.Trim();
            return sprite;
        }

        internal (Sprite Map, Sprite Player, Sprite Opponent) DuelArt(string map, string player, string opponent)
        {
            _art.BeginPage();
            var backdrop = _art.Get(WorldArt + (map ?? OpeningArt));
            var hero = DuelPortrait(player);
            var rival = DuelPortrait(opponent);
            _art.Trim();
            return (backdrop, hero, rival);
        }

        private Sprite DuelPortrait(string key)
        {
            string actor = key switch
            {
                "hero_anti_synchro" or "rescue" => "hero",
                "ghost" => "ghost_a",
                "wrpg_anchor" => "opponent_anchor",
                "aki" when _playback.Episode.number == 1 => "aki_masked",
                "aki" when _playback.Episode.number is >= 22 and <= 31 => "aki_riding",
                "sherry" when _playback.Episode.number is 34 or 35 => "sherry_fallen",
                _ => key,
            };
            return _art.GetBust(WorldArt + "Characters_Standing/" + actor + ".png");
        }

        private bool Save(Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Beyond Destiny] {error.Message}");
                ShowPanel("저장 실패", "저장 경로를 확인한 뒤 다시 시도하세요.", "저장 상태 다시 읽기", OpenCampaign);
                return false;
            }
        }

        private void ShowPanel(string heading, string description, string primary, Action submit,
            string secondary = null, Action alternate = null, string map = null)
        {
            _dialogue.Close();
            _field.Close();
            _panel.gameObject.SetActive(true);
            _heading.text = heading;
            _description.text = description;
            _background.sprite = Artwork(map);
            _background.enabled = _background.sprite != null;
            _primaryAction = submit;
            _secondaryAction = alternate;
            SetLabel(_primary, primary);
            SetLabel(_secondary, secondary ?? "");
            _secondary.gameObject.SetActive(alternate != null);
            EventSystem.current?.SetSelectedGameObject(_primary.gameObject);
        }
    }
}
