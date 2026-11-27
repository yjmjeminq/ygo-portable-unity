using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    [Serializable]
    public class StoryDialogueLine
    {
        public string speaker;
        public string text;
        public string cutscene;
        public string sceneMap;
        public string sceneTime;
        public string memoryTitle;
        public string emotion;
        public string fx;
        public string sfx;
        public StoryStageAction[] stage;

        internal string SceneMapPath => !string.IsNullOrEmpty(sceneMap) &&
            sceneMap.All(c => char.IsLetterOrDigit(c) || c is '_' or '-') ? $"Maps/{sceneMap}.png" : null;
    }

    [Serializable]
    public class StoryStageAction
    {
        public int actor;
        public string action;
        public int step;
        public int target;
        public float x;
        public float y;
    }

    [Serializable]
    public class StoryEpisodeData
    {
        public string id;
        public int chapter;
        public string chapterTitle;
        public string title;
        public int charId;
        public string opponentName;
        public int deckId;
        public string regionId;
        public string fieldObjective;
        public bool isCutsceneOnly;
        public int playAsCharacterId;
        public int forcedPlayerDeckId;
        public StoryDialogueLine[] preDialogues;
        public StoryDialogueLine[] postWinDialogues;
        public StoryDialogueLine[] postLoseDialogues;
        public StoryDialogueOption[] choices;
        public int choiceAfterLine;
        public bool sceneOnApproach;
        public string gimmick;
        public uint raidCard;
        public int raidLife;
    }

    [Serializable]
    public class StoryScriptRoot
    {
        public string seriesId;
        public string seriesTitle;
        public string protagonist;
        public int totalChapters;
        public int totalEpisodes;
        public StoryEpisodeData[] episodes;
    }

    internal sealed partial class StoryModeUI
    {
        internal const int TotalStoryEpisodes = StoryCampaign.EpisodeCount;
        private const int ActCount = 5;
        internal const string RpgSeriesId = "5ds";
        internal const string GxRpgSeriesId = "gx";
        private const int GxRpgEpisodeCount = 53;
        private const string RpgMachine = "스트라이크 팽";
        private const float EpisodeRowHeight = 52f;
        private static readonly (string Id, string Label)[] Series =
        {
            ("dm", "DM"), ("gx", "GX"), ("5ds", "5D's"), ("zexal", "ZEXAL"), ("arcv", "ARC-V"), ("vrains", "VRAINS")
        };

        private readonly GameAppController _app;
        private static readonly string[] TurnOrderHands = { "가위", "바위", "보" };
        private readonly System.Random _turnOrderRandom;
        private readonly (string Id, Button Button, StoryScriptRoot Script)[] _seriesTabs;
        private readonly List<UserDeck> _playableDecks = new();
        private readonly Button[] _episodeButtons = new Button[GxRpgEpisodeCount];
        private readonly RectTransform _episodeRows;
        private readonly Dictionary<string, (StoryScriptRoot Script, Dictionary<int, PresetDeckInfo> Decks)> _rpgCampaigns;
        private string _rpgSeriesId = RpgSeriesId;
        private readonly ScrollRect _episodeScroll;
        private readonly CanvasGroup _browser;
        private readonly Image _backdrop;
        private readonly StoryDialogueView _dialogue;
        private readonly Button _mainMenu;
        private readonly Button _tournament;
        private readonly Button _start;
        private readonly Dropdown _deckPicker;
        private readonly Image _portrait;
        private readonly Text _seriesTitle;
        private readonly Text _episodeHeading;
        private readonly Text _episodeTitle;
        private readonly Text _opponentName;
        private readonly Text _opponentDeck;
        private readonly Text _status;

        private int _selectedSeries;
        private bool _rpgWorld;
        private Action _resumeAfterDeckEditor;
        private int _unlockedIndex;
        private int _selectedIndex;
        private string _deckId;
        private StoryStance _stance;
        private RaidSupport _supports;
        private StoryOpponentOpening _opponentOpening;
        private Action _returnFromOpeningPicker;
        private Action _returnFromTurnOrder;
        private Action _startAfterLoss;
        private float _lossRevealTime;

        internal GameObject Root { get; }
        internal GameObject ActiveRoot => _rpgFaceoff != null ? _rpgFaceoff.gameObject : _dialogue.IsOpen ? _dialogue.ActiveRoot : Root;

        private StoryScriptRoot Script => _rpgWorld ? GetRpgScript(_rpgSeriesId) : _seriesTabs[_selectedSeries].Script;

        internal StoryScriptRoot GetRpgScript(string seriesId) => _rpgCampaigns[seriesId].Script;
        internal StoryModeUI(GameAppController app, Transform parent, System.Random turnOrderRandom = null)
        {
            _app = app;
            _turnOrderRandom = turnOrderRandom ?? new System.Random();
            _rpgCampaigns = new[] { RpgSeriesId, GxRpgSeriesId }.ToDictionary(id => id,
                id => (LoadRpgScript(Application.streamingAssetsPath, id), LoadRpgDecks(Application.streamingAssetsPath, id)));
            var root = DuelUiKit.FillRect("Screen_StoryMode", parent);
            Root = root.gameObject;
            _backdrop = DuelUiKit.AddImage(root.gameObject, DuelUiChrome.Backdrop, Color.white, false, false);

            var browser = DuelUiKit.FillRect("StoryBrowser", root);
            _browser = browser.gameObject.AddComponent<CanvasGroup>();
            var content = Application.isMobilePlatform ? MobileSafeArea.Create(browser) : browser;
            var header = Region(content, "Header", .032f, .892f, .655f, .977f);
            DuelUiKit.AddImage(header.gameObject, DuelUiChrome.Header, Color.white, false, false).type = Image.Type.Sliced;
            _seriesTitle = DuelUiKit.AddFittedText(header, "Title", "", 32, 16,
                TextAnchor.MiddleCenter, DuelUiKit.TextPrimary, FontStyle.Bold);
            _seriesTabs = new (string, Button, StoryScriptRoot)[Series.Length];
            for (int i = 0; i < Series.Length; i++)
            {
                int index = i;
                var (id, label) = Series[i];
                float left = .032f + .156f * i;
                var tab = AddButton(content, $"Series_{id}", label, left, .827f, left + .147f, .886f, () => SelectSeries(index));
                _seriesTabs[i] = (id, tab, LoadScript(Application.streamingAssetsPath, id));
            }
            _selectedSeries = Mathf.Max(0, Array.FindIndex(_seriesTabs, tab => tab.Script != null));
            _mainMenu = AddButton(content, "MainMenu", "메인 메뉴", .83f, .897f, .968f, .973f,
                () => _app.ShowScreen(AppScreen.MainMenu));
            _tournament = AddButton(content, "DimensionTournament", "차원 대항전", .667f, .897f, .82f, .973f,
                () => _app.ShowScreen(AppScreen.Tournament), gold: true);

            var listing = DuelUiKit.CreateMenuPanel(content, "Episodes", new Vector2(.032f, .08f), new Vector2(.57f, .82f));
            var viewport = Region(listing, "Viewport", .04f, .043f, .946f, .957f);
            DuelUiKit.AddImage(viewport.gameObject, DuelUiKit.WhiteSprite, Color.clear);
            viewport.gameObject.AddComponent<RectMask2D>();
            var rows = _episodeRows = DuelUiKit.CreateRect("Rows", viewport, new Vector2(0f, 1f), Vector2.one, Vector2.zero, Vector2.zero);
            rows.pivot = new Vector2(.5f, 1f);
            rows.sizeDelta = new Vector2(0f, EpisodeRowHeight * TotalStoryEpisodes);
            _episodeScroll = listing.gameObject.AddComponent<ScrollRect>();
            _episodeScroll.viewport = viewport;
            _episodeScroll.content = rows;
            _episodeScroll.horizontal = false;
            _episodeScroll.movementType = ScrollRect.MovementType.Clamped;
            _episodeScroll.scrollSensitivity = EpisodeRowHeight;
            _episodeScroll.verticalScrollbar = DuelUiKit.AddVerticalScrollbar(Region(listing, "EpisodeScrollbar", .95f, .043f, .973f, .957f));
            LayoutEpisodeRows(TotalStoryEpisodes);

            var details = DuelUiKit.CreateMenuPanel(content, "EpisodeDetails",
                new Vector2(.587f, .08f), new Vector2(.968f, .82f));
            _episodeHeading = AddLabel(details, "Chapter", .07f, .88f, .93f, .955f, 21, color: DuelUiKit.GoldAccent);
            _episodeTitle = AddLabel(details, "EpisodeTitle", .07f, .758f, .93f, .885f, 26);
            var portrait = Region(details, "OpponentPortrait", .13f, .445f, .87f, .753f);
            _portrait = DuelUiKit.AddImage(portrait.gameObject, null, Color.white, true, false);
            _opponentName = AddLabel(details, "OpponentName", .07f, .379f, .93f, .442f, 23);
            _opponentDeck = AddLabel(details, "OpponentDeck", .07f, .317f, .93f, .375f, 19, color: DuelUiKit.TextMuted);
            AddLabel(details, "DeckLabel", .07f, .268f, .93f, .314f, 18,
                TextAnchor.MiddleLeft, DuelUiKit.TextMuted).text = "내 덱";
            var picker = Region(details, "DeckPicker", .07f, .19f, .93f, .268f);
            _deckPicker = DuelUiKit.CreateDropdown(picker, "SelectDeck", Array.Empty<string>(), SelectDeck, dynamicChoices: true);
            _deckPicker.captionText.resizeTextMaxSize = 22;
            _deckPicker.captionText.fontSize = 22;
            _start = AddButton(details, "StartEpisode", "이야기 시작", .16f, .062f, .84f, .163f, StartEpisode, gold: true);
            _status = AddLabel(content, "Status", .045f, .018f, .955f, .073f, 21, color: DuelUiKit.GoldAccent);

            _dialogue = new StoryDialogueView(root);

            Close();
        }

        private Action _finishFieldResult;
        internal bool FromRpgField { get; private set; }
        internal bool IsRpgCampaign => _rpgWorld;
        internal bool IsGxRpg => _rpgWorld && _rpgSeriesId == GxRpgSeriesId;

        internal static string RpgProgressId(StoryEpisodeData episode, string seriesId) =>
            seriesId == GxRpgSeriesId ? "gx_rpg/" + episode.id : episode.id;

        internal string ProgressIdFor(StoryEpisodeData episode) => _rpgWorld ? RpgProgressId(episode, _rpgSeriesId) : episode.id;

        internal UserDeck ForcedDeckFor(StoryEpisodeData episode)
        {
            if (!IsGxRpg || episode.forcedPlayerDeckId == 0 ||
                !_rpgCampaigns[_rpgSeriesId].Decks.TryGetValue(episode.forcedPlayerDeckId, out var deck) ||
                deck.charId != episode.playAsCharacterId)
                return null;
            return new UserDeck
            {
                id = $"gx-rpg-pov-{deck.id}", name = deck.title,
                main = deck.main.ToList(), extra = deck.extra.ToList(), side = deck.side.ToList(),
            };
        }

        private bool HasEpisodeDeck(StoryEpisodeData episode) =>
            episode.forcedPlayerDeckId != 0 ? ForcedDeckFor(episode) != null : _playableDecks.Count > 0;

        internal void UseRpgCampaign(bool enabled, bool fromField = false, string seriesId = RpgSeriesId)
        {
            _rpgSeriesId = seriesId;
            _rpgWorld = enabled;
            FromRpgField = fromField;
        }

        internal void OpenFieldEpisode(int index)
        {
            if (FromRpgField && Script?.episodes != null && index >= 0 && index < Script.episodes.Length)
                OpenEpisode(index);
        }

        internal void ShowFieldDuelResult(StoryEpisodeData episode, bool won, bool draw, Func<(bool Saved, string Message)> settle,
            StoryBattlePlan plan)
        {
            string result = won ? "승리" : draw ? "무승부" : "패배";
            int index = IndexOf(episode);
            if (index >= 0) SelectEpisode(index);
            string reward = null;
            bool gx = IsGxRpg;
            bool gxLoss = !won && gx;
            var resultLines = gx && draw
                ? new[] { new StoryDialogueLine { text = "듀얼 종료 신호가 울렸다. 이번 승부는 무승부였다." } }
                : won ? episode.postWinDialogues : episode.postLoseDialogues;
            _finishFieldResult = ReturnToField;
            PlayDialogue(episode, resultLines,
                $"{TitleOf(episode)} · {result}", won ? ReturnToField : FinishDefeat);
            if (gxLoss) FinishDefeat();

            void FinishDefeat()
            {
                reward ??= settle().Message ?? string.Empty;
                _finishFieldResult = gxLoss ? ReturnToField : null;
                _stance = plan.Stance;
                _supports = RaidSupport.None;
                _opponentOpening = plan.OpponentOpening;
                ShowRetryChoices();
            }

            void ReturnToField()
            {
                if (reward == null)
                {
                    var settlement = settle();
                    if (gx && won && !settlement.Saved)
                    {
                        _dialogue.FocusOnChoices();
                        _dialogue.ShowChoices(() => settlement.Message, new StoryChoice("진행도 다시 저장", ReturnToField));
                        return;
                    }
                    reward = settlement.Message;
                }
                _finishFieldResult = null;
                if (won && Application.isPlaying) DuelAudio.PlayWin();
                _app.ReturnToRpgField(reward, afterStoryEvent: gx && won);
            }

            void ShowRetryChoices()
            {
                string Summary() => string.IsNullOrEmpty(reward) ? result : $"{result} · {reward}";
                var retry = new StoryChoice("바로 재도전", Retry, () => HasEpisodeDeck(episode));
                if (gxLoss)
                {
                    _finishFieldResult = ReturnToField;
                    _dialogue.FocusOnChoices();
                    _dialogue.ShowChoices(Summary, retry,
                        new("덱 변경 후 재도전", () => PickDeck(Retry), () => episode.forcedPlayerDeckId == 0),
                        new("필드로 복귀", ReturnToField),
                        new(draw ? "무승부 장면 보기" : "패배 대화 보기", () =>
                        {
                            _finishFieldResult = FinishDefeat;
                            PlayDialogue(episode, resultLines, $"{TitleOf(episode)} · {result}", FinishDefeat);
                        }, () => resultLines?.Length > 0));
                }
                else _dialogue.ShowChoices(Summary, retry,
                    new("대화부터 다시", () => OpenEpisode(index), () => index >= 0),
                    new("덱 편집", _app.OpenStoryDeckEditor, () => episode.forcedPlayerDeckId == 0),
                    new("필드로 돌아가기", ReturnToField));
            }

            void Retry()
            {
                if (!HasEpisodeDeck(episode)) return;
                _finishFieldResult = null;
                ChooseTurnOrder(episode, ShowRetryChoices);
            }
        }

        private void LayoutEpisodeRows(int count)
        {
            _episodeRows.sizeDelta = new Vector2(0f, EpisodeRowHeight * count);
            for (int i = 0; i < _episodeButtons.Length; i++)
            {
                if (i >= count)
                {
                    if (_episodeButtons[i] != null) _episodeButtons[i].transform.parent.gameObject.SetActive(false);
                    continue;
                }
                int index = i;
                float top = 1f - (float)i / count;
                float bottom = top - .89f / count;
                if (_episodeButtons[i] == null)
                {
                    _episodeButtons[i] = AddButton(_episodeRows, $"Episode_{i + 1}", "", 0f, bottom, 1f, top,
                        () => SelectEpisode(index));
                    _episodeButtons[i].GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                }
                var row = (RectTransform)_episodeButtons[i].transform.parent;
                row.anchorMin = new Vector2(0f, bottom);
                row.anchorMax = new Vector2(1f, top);
                row.gameObject.SetActive(true);
            }
        }

        internal void Open()
        {
            Root.SetActive(true);
            ResetDialogue();
            CollectionProgress.MigrateStoryClears(_seriesTabs.Where(tab => tab.Script != null)
                .SelectMany(tab => Enumerable.Range(1, StoryCampaign.LegacyEpisodeCount)
                    .Select(number => StoryCampaign.LegacyEpisodeId(tab.Id, number))
                    .Where(id => PlayerPrefs.GetInt(ClearedKey(tab.Id, id), 0) != 0)));
            if (IsGxRpg && FromRpgField) _deckId = CollectionProgress.LastDuelDeckId;
            LoadDeckChoices();
            SelectSeries(_selectedSeries);
            _tournament.GetComponentInChildren<Text>().text = DimensionTournament.Unlocked ? "차원 대항전" : "차원 대항전 · 잠김";
            Focus(Script != null ? _episodeButtons[_selectedIndex] : _mainMenu);
        }

        internal void Close()
        {
            _deckPicker.Hide();
            ResetDialogue();
            Root.SetActive(false);
        }

        internal void SuspendForDeckEditor() => Root.SetActive(false);

        internal void ResumeFromDeckEditor()
        {
            Root.SetActive(true);
            LoadDeckChoices();
            _dialogue.CloseDeckPicker();
            _dialogue.RefreshChoices();
            var resume = _resumeAfterDeckEditor;
            _resumeAfterDeckEditor = null;
            resume?.Invoke();
        }

        internal void Update(float delta)
        {
            if (_app.RpgField?.RpgInterludeActive == true) return;
            _dialogue.Update(delta);
            UpdateRpgFaceoff(delta);
            if (_startAfterLoss != null && (_lossRevealTime -= delta) <= 0f)
                _startAfterLoss();
        }

        internal void Back()
        {
            if (_app.RpgField?.RpgInterludeActive == true || RestoreHiddenDialogue()) return;
            if (_rpgFaceoff != null) { FinishRpgFaceoff(); return; }
            if (!_dialogue.IsOpen)
            {
                _app.ShowScreen(AppScreen.MainMenu);
                return;
            }
            if (_dialogue.ClosePopup()) return;
            if (_finishFieldResult != null)
            {
                _finishFieldResult();
                return;
            }
            if (_returnFromTurnOrder != null) CancelTurnOrder();
            else if (_returnFromOpeningPicker != null) CloseOpeningPicker();
            // Returning to the field during an automatic scene immediately opens that same scene again.
            else if (IsGxRpg && FromRpgField && _app.RpgField.Root.activeSelf && Script.episodes[_selectedIndex].isCutsceneOnly)
                _app.ShowScreen(AppScreen.MainMenu);
            else ReturnToEpisodes();
        }

        internal bool RestoreHiddenDialogue() => _dialogue.HandleDisplayInput(restore: true);

        internal bool CanPlay(StoryEpisodeData episode)
        {
            int index = IndexOf(episode);
            return index >= 0 && index <= _unlockedIndex;
        }

        internal string RidingMachineFor(StoryEpisodeData episode) =>
            _rpgWorld && IndexOf(episode) >= 0 && StoryGimmickRuntime.IsRidingDuel(episode.gimmick) ? RpgMachine : null;

        internal void ShowDuelResult(StoryEpisodeData episode, bool won, bool draw, string reward, StoryBattlePlan plan = default)
        {
            int index = IndexOf(episode);
            if (index < 0) return;
            SelectEpisode(index);
            _stance = plan.Stance;
            _supports = RaidSupport.None;
            _opponentOpening = plan.OpponentOpening;
            string result = won ? "승리" : draw ? "무승부" : "패배";
            PlayDialogue(episode, won ? episode.postWinDialogues : episode.postLoseDialogues, $"{TitleOf(episode)} · {result}", () =>
            {
                if (won)
                {
                    if (Application.isPlaying) DuelAudio.PlayWin();
                    _dialogue.ShowChoices(() => reward,
                        new("다음 에피소드 바로 시작", () => OpenEpisode(index + 1), () =>
                            index + 1 < Script.episodes.Length && CanPlay(Script.episodes[index + 1])),
                        new("에피소드 목록으로", ReturnToEpisodes));
                }
                else ShowRetry();
            });

            void ShowRetry() => _dialogue.ShowChoices(() => reward,
                new("즉시 재도전", () => ChooseTurnOrder(episode, ShowRetry), () => _playableDecks.Count > 0),
                new("내 덱 변경 후 재도전", PickDeck),
                new("에피소드 목록으로", ReturnToEpisodes),
                new("전술 서포트 장착", () => PickSupports(episode)),
                new("상대 시작 조건 변경", () => PickOpponentOpening(episode, ShowRetry)));
        }

        internal void ShowStatus(string status) => _status.text = status;

        internal (PresetDeckInfo Deck, int Level, int LevelCount) GetOpponentMatch(StoryEpisodeData episode)
        {
            int episodeIndex = IndexOf(episode);
            if (episodeIndex < 0) return default;
            if (_rpgWorld) return (_rpgCampaigns[_rpgSeriesId].Decks.GetValueOrDefault(episode.deckId), 0, 0);

            var decks = OpponentDeckLevels.GetDecks(episode.charId);
            if (decks.Count == 0) decks = DeckRepository.GetDecksForCharacter(episode.charId);
            if (decks.Count == 0) return default;
            int level = SelectDeckLevel(Script.episodes, episodeIndex, decks.Count, GetRaid(episode) != null);
            return (decks[level - 1], level, decks.Count);
        }

        internal static int SelectDeckLevel(StoryEpisodeData[] episodes, int episodeIndex, int deckCount, bool raid)
        {
            int finalIndex = Math.Max(1, episodes.Length - 1);
            if (raid)
                return 1 + (episodeIndex * (deckCount - 1) + finalIndex - 1) / finalIndex;

            int characterId = episodes[episodeIndex].charId;
            int appearances = episodes.Count(entry => entry.charId == characterId);
            int remaining = appearances;
            int level = 0;
            for (int i = 0; i <= episodeIndex; i++)
            {
                if (episodes[i].charId != characterId) continue;
                remaining--;
                int progressLevel = 1 + (i * (deckCount - 1) + finalIndex / 2) / finalIndex;
                level = appearances <= deckCount
                    ? Math.Clamp(progressLevel, level + 1, deckCount - remaining)
                    : progressLevel;
            }
            return level;
        }

        internal StoryRaid? GetRaid(StoryEpisodeData episode)
        {
            int number = IndexOf(episode) + 1;
            return number > 0 && (episode.raidCard > 0 || episode.raidLife > 0)
                ? StoryRaid.ForEncounter(number, episode.raidCard, episode.raidLife, Script.episodes.Length) : null;
        }

        private static string ClearedKey(string seriesId, string episodeId) => $"ygo.story.{seriesId}.cleared.{episodeId}";

        private int IndexOf(StoryEpisodeData episode) =>
            Script == null || episode == null ? -1 : Array.IndexOf(Script.episodes, episode);

        [Serializable]
        private sealed class StoryLineup
        {
            public SeriesLineup[] series;
        }

        [Serializable]
        private sealed class SeriesLineup
        {
            public string id;
            public LineupEpisode[] episodes;
        }

        [Serializable]
        private sealed class LineupEpisode
        {
            public int source;
            public string title;
            public string gimmick;
            public uint raidCard;
            public int raidLife;
        }

        internal static StoryScriptRoot LoadScript(string streamingAssets, string seriesId)
        {
            string path = Path.Combine(streamingAssets, seriesId is RpgSeriesId or GxRpgSeriesId
                ? $"story_{seriesId}_classic_script.json" : $"story_{seriesId}_script.json");
            try
            {
                var script = JsonUtility.FromJson<StoryScriptRoot>(File.ReadAllText(path));
                var lineup = JsonUtility.FromJson<StoryLineup>(File.ReadAllText(Path.Combine(streamingAssets, "story_lineup.json")));
                var entries = lineup?.series?.SingleOrDefault(series => series?.id == seriesId)?.episodes;
                if (script?.seriesId != seriesId || script.episodes?.Length != StoryCampaign.LegacyEpisodeCount ||
                    entries?.Length != TotalStoryEpisodes)
                    throw new InvalidDataException("Expected a complete thirty-episode lineup and its dialogue source.");

                var episodes = new StoryEpisodeData[TotalStoryEpisodes];
                for (int i = 0; i < episodes.Length; i++)
                {
                    var entry = entries[i];
                    if (entry == null || entry.source is < 1 or > StoryCampaign.LegacyEpisodeCount)
                        throw new InvalidDataException($"Invalid dialogue source for episode {i + 1}.");
                    var source = script.episodes[entry.source - 1];
                    if (source == null || string.IsNullOrWhiteSpace(source.title) ||
                        !HasDialogue(source.preDialogues) || !HasDialogue(source.postWinDialogues) ||
                        !HasDialogue(source.postLoseDialogues))
                        throw new InvalidDataException($"Incomplete episode at index {i}.");
                    episodes[i] = new StoryEpisodeData
                    {
                        id = StoryCampaign.EpisodeId(seriesId, i + 1),
                        chapter = i * ActCount / TotalStoryEpisodes + 1,
                        chapterTitle = $"{i + 1:00}화",
                        title = string.IsNullOrWhiteSpace(entry.title) ? TitleOf(source) : entry.title,
                        charId = source.charId,
                        opponentName = source.opponentName,
                        preDialogues = source.preDialogues,
                        postWinDialogues = source.postWinDialogues,
                        postLoseDialogues = source.postLoseDialogues,
                        choices = source.choices,
                        choiceAfterLine = source.choiceAfterLine,
                        gimmick = entry.gimmick,
                        raidCard = entry.raidCard,
                        raidLife = entry.raidLife,
                    };
                }
                script.totalEpisodes = episodes.Length;
                script.totalChapters = 0;
                script.episodes = episodes;
                return script;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidDataException)
            {
                Debug.LogWarning($"[StoryMode] Could not load {seriesId} campaign: {ex.Message}");
                return null;
            }
        }

        internal static StoryScriptRoot LoadRpgScript(string streamingAssets, string seriesId = RpgSeriesId)
        {
            try
            {
                var script = JsonUtility.FromJson<StoryScriptRoot>(
                    File.ReadAllText(Path.Combine(streamingAssets, $"story_{seriesId}_script.json")));
                bool gx = seriesId == GxRpgSeriesId;
                if (script?.seriesId?.Equals(seriesId, StringComparison.OrdinalIgnoreCase) != true ||
                    script.episodes?.Length != (gx ? GxRpgEpisodeCount : StoryCampaign.LegacyEpisodeCount))
                    throw new InvalidDataException($"Incomplete {seriesId} RPG script.");
                int duel = 0;
                for (int i = 0; i < script.episodes.Length; i++)
                {
                    var episode = script.episodes[i];
                    if (episode == null || string.IsNullOrWhiteSpace(episode.title) || !HasDialogue(episode.preDialogues) ||
                        (gx && (string.IsNullOrWhiteSpace(episode.id) || string.IsNullOrWhiteSpace(episode.regionId))))
                        throw new InvalidDataException($"Invalid {seriesId} RPG episode {i + 1}.");
                    if (gx && episode.isCutsceneOnly)
                    {
                        if (episode.deckId != 0) throw new InvalidDataException($"Unexpected deck in {episode.id}.");
                        continue;
                    }
                    if (episode.deckId != (gx ? 69001 : 59001) + duel++ ||
                        !HasDialogue(episode.postWinDialogues) || !HasDialogue(episode.postLoseDialogues))
                        throw new InvalidDataException($"Invalid {seriesId} RPG duel at episode {i + 1}.");
                    if (!gx) episode.id = $"5ds_rpg_ep_{i + 1:00}";
                    if (episode.gimmick == "none" || string.IsNullOrWhiteSpace(episode.gimmick)) episode.gimmick = null;
                }
                if (gx && (duel != 34 || script.episodes.Select(episode => episode.id).Distinct().Count() != GxRpgEpisodeCount))
                    throw new InvalidDataException("Expected 34 duels and 19 unique GX cutscenes.");
                return script;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
                error is InvalidDataException)
            {
                Debug.LogWarning($"[StoryMode] Could not load the RPG campaign: {error.Message}");
                return null;
            }
        }

        internal static Dictionary<int, PresetDeckInfo> LoadRpgDecks(string streamingAssets, string seriesId = RpgSeriesId)
        {
            var decks = new Dictionary<int, PresetDeckInfo>();
            try
            {
                var data = JsonUtility.FromJson<OpponentsDbWrapper>(
                    File.ReadAllText(Path.Combine(streamingAssets, $"story_{seriesId}_rpg_decks.json")));
                foreach (var deck in data?.decks ?? Array.Empty<PresetDeckInfo>())
                {
                    if (deck == null || deck.main?.Length is not (>= 40 and <= 60) ||
                        deck.extra?.Length is not (>= 0 and <= 15) || deck.side?.Length is not (>= 0 and <= 15))
                        continue;
                    bool validId = seriesId == GxRpgSeriesId
                        ? deck.id is >= 69001 and <= 69034 or >= 69101 and <= 69103
                        : deck.id is >= 59001 and <= 59050;
                    if (validId) decks.TryAdd(deck.id, deck);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            {
                Debug.LogWarning($"[StoryMode] Could not load RPG opponent decks: {error.Message}");
            }
            return decks;
        }

        private static bool HasDialogue(StoryDialogueLine[] lines) => lines?.Length > 0 &&
            lines.All(line => line != null && !string.IsNullOrWhiteSpace(line.text));

        private void SelectSeries(int index)
        {
            if (!Root.activeSelf || _dialogue.IsOpen) return;
            if (!_rpgWorld) _selectedSeries = index;
            _seriesTitle.text = Script?.seriesTitle ?? "스토리 모드";
            for (int i = 0; i < _seriesTabs.Length; i++)
            {
                var tab = _seriesTabs[i];
                tab.Button.transform.parent.gameObject.SetActive(!_rpgWorld);
                tab.Button.interactable = !_rpgWorld && tab.Script != null;
                tab.Button.GetComponentInChildren<Text>().color = i == index ? DuelUiKit.GoldAccent : DuelUiKit.TextPrimary;
            }
            LayoutEpisodeRows(Script?.episodes.Length ?? 0);
            if (Script == null) _unlockedIndex = 0;
            else
            {
                int nextEpisode = Array.FindIndex(Script.episodes, episode => !CollectionProgress.IsStoryEpisodeCleared(ProgressIdFor(episode)));
                _unlockedIndex = nextEpisode >= 0 ? nextEpisode : Script.episodes.Length - 1;
            }
            SelectEpisode(_unlockedIndex);
            Canvas.ForceUpdateCanvases();
            _episodeScroll.verticalNormalizedPosition = 1f;
            if (Script != null) DeckPadUi.ScrollTo(_episodeButtons[_selectedIndex]);
        }

        private void SelectEpisode(int index)
        {
            if (Script == null)
            {
                _start.interactable = false;
                _deckPicker.interactable = false;
                ShowStatus("스토리 대본을 불러오지 못했습니다.");
                return;
            }

            _selectedIndex = index;
            for (int row = 0; row < Script.episodes.Length; row++)
            {
                int episodeIndex = row;
                var episode = Script.episodes[episodeIndex];
                _episodeButtons[row].interactable = true;
                bool cleared = CollectionProgress.IsStoryEpisodeCleared(ProgressIdFor(episode));
                bool locked = episodeIndex > _unlockedIndex;
                string state = cleared ? "클리어" : locked ? "잠김" : "도전 가능";
                var label = _episodeButtons[row].GetComponentInChildren<Text>();
                var raid = GetRaid(episode);
                string badge = raid.HasValue ? "◆ BOSS RAID  " : "";
                label.text = $"  {episodeIndex + 1:00}화  {badge}{TitleOf(episode)}  ·  {state}";
                label.color = episodeIndex == index ? DuelUiKit.GoldAccent : locked ? DuelUiKit.TextMuted :
                    raid.HasValue ? DuelUiKit.RedAccent : DuelUiKit.TextPrimary;
            }

            var current = Script.episodes[index];
            var opponent = DeckRepository.GetCharacter(current.charId);
            _episodeHeading.text = current.chapterTitle;
            _episodeTitle.text = TitleOf(current);
            int bond = CollectionProgress.GetStoryBond(current.charId);
            _opponentName.text = bond > 0 ? $"{current.opponentName} · 유대도 {bond:N0}" : current.opponentName;
            _portrait.sprite = DuelUiKit.LoadOpponentPortrait(opponent);
            _portrait.enabled = _portrait.sprite != null;
            var (deck, level, levelCount) = GetOpponentMatch(current);
            _opponentDeck.text = deck == null ? "" : IsGxRpg ? $"{GxDeckPreview(deck)}\n{deck.title}" :
                levelCount == 0 ? deck.title : $"Lv. {level}/{levelCount}  ·  {deck.title}";
            bool ready = current.isCutsceneOnly || (opponent != null && deck != null);
            _deckPicker.interactable = current.forcedPlayerDeckId == 0 && !current.isCutsceneOnly && _playableDecks.Count > 0;
            _start.interactable = CanPlay(current) && ready;
            string status = !CanPlay(current) ? "이전 에피소드를 클리어하면 열립니다." :
                !ready ? "상대 덱을 불러오지 못했습니다." :
                !current.isCutsceneOnly && !HasEpisodeDeck(current) ? DuelDeckAvailability.ExplainEmptyList(_deckId) : "";
            ShowStatus(GetRaid(current) is { } raidInfo
                ? string.IsNullOrEmpty(status) ? raidInfo.Summary : $"{raidInfo.Summary}\n{status}"
                : status);
        }

        internal static string GxDeckPreview(PresetDeckInfo deck) =>
            deck == null ? "" : $"★{deck.difficultyTier} · {deck.subtitle}";

        private void LoadDeckChoices()
        {
            _playableDecks.Clear();
            _playableDecks.AddRange(DeckRepository.UserDecks.Where(deck => GameAppController.CanStartWithDeck(deck, out _)));
            _deckPicker.ClearOptions();
            _deckPicker.AddOptions(_playableDecks.Count == 0
                ? new List<string> { "사용 가능한 덱 없음" }
                : _playableDecks.Select(deck => deck.name).ToList());
            _deckPicker.interactable = _playableDecks.Count > 0;
            int selected = _playableDecks.FindIndex(deck => deck.id == (_deckId ?? CollectionProgress.LastDuelDeckId));
            _deckPicker.SetValueWithoutNotify(Mathf.Max(0, selected));
            _deckPicker.RefreshShownValue();
        }

        private void SelectDeck(int index)
        {
            if (index >= 0 && index < _playableDecks.Count) _deckId = _playableDecks[index].id;
        }

        private void StartEpisode()
        {
            if (_start.interactable && !_dialogue.IsOpen) OpenEpisode(_selectedIndex);
        }

        private void OpenEpisode(int index)
        {
            SelectEpisode(index);
            var episode = Script.episodes[index];
            if (episode.isCutsceneOnly)
            {
                PlayDialogue(episode, episode.preDialogues, TitleOf(episode), CompleteScene);
                return;
            }
            var raid = GetRaid(episode);
            _stance = StoryStance.Neutral;
            _supports = RaidSupport.None;
            _opponentOpening = StoryOpponentOpening.Standard;
            var deck = GetOpponentMatch(episode).Deck;
            bool pickedInitialDeck = false;
            PlayDialogue(episode, episode.preDialogues, TitleOf(episode), ShowPreparation);
            if (!IsGxRpg)
            {
                var choices = StoryDialogueBranch.Create(episode, raid.HasValue,
                    deck?.sigCard is > 0 ? CardDatabase.GetName(deck.sigCard) : "");
                _dialogue.SetBranch(StoryDialogueBranch.AfterLine(episode), () =>
                    _dialogue.ShowChoices(() => "어떻게 답할까?", choices.Select(choice => new StoryChoice(
                        $"{choice.Reply}\n{StoryDialogueBranch.Effect(choice.Stance)}", () =>
                        {
                            _stance = choice.Stance;
                            _dialogue.AnswerBranch(choice.Reply, episode.opponentName, choice.Response);
                        })).ToArray()));
            }

            void CompleteScene()
            {
                if (CollectionProgress.TryClearStoryScene(ProgressIdFor(episode), out string error))
                    _app.ReturnToRpgField(afterStoryEvent: IsGxRpg && FromRpgField);
                else _dialogue.ShowChoices(() => error, new StoryChoice("진행도 다시 저장", CompleteScene));
            }

            void ShowPreparation()
            {
                var actions = new List<StoryChoice>
                {
                    new("듀얼 개시!", () => ChooseTurnOrder(episode, ShowPreparation), () => HasEpisodeDeck(episode)),
                };
                if (episode.forcedPlayerDeckId == 0) actions.Add(new("내 덱 변경", PickDeck));
                actions.Add(new("대화 다시 보기", () => OpenEpisode(index)));
                actions.Add(new(FromRpgField ? "필드로 돌아가기" : "에피소드 목록으로", ReturnToEpisodes));
                actions.Add(new("전술 서포트 장착 · 최대 2개", () => PickSupports(episode)));
                actions.Add(new("상대 시작 조건 변경", () => PickOpponentOpening(episode, ShowPreparation)));
                _dialogue.ShowChoices(() => PreparationSummary(episode, raid, deck), actions.ToArray());
                if (_rpgWorld && episode.forcedPlayerDeckId == 0 && !pickedInitialDeck)
                {
                    pickedInitialDeck = true;
                    PickDeck();
                }
            }
        }

        private StoryBattlePlan CurrentBattlePlan => new(_stance, _supports, _opponentOpening);

        private string PreparationSummary(StoryEpisodeData episode, StoryRaid? raid, PresetDeckInfo deck)
        {
            var (player, opponent) = CurrentBattlePlan.Apply(raid, deck, episode.gimmick);
            string field = string.Join(" · ", new[] { opponent.FieldCard, opponent.DeckMonster }
                .Concat(StoryGimmickRuntime.StartingCards(episode.gimmick).Select(card => card.Code))
                .Where(code => code != 0).Select(CardDatabase.GetName));
            if (field.Length > 0) field = $"\n선배치 카드 · {field}";
            string equipped = string.Join(" · ", RaidSupportCatalog.Items.Where(item => (_supports & item.Id) != 0)
                .Select(item => item.Name));
            string supports = equipped.Length > 0 ? $"\n장착 · {equipped}" : "";
            string machine = RidingMachineFor(episode) is { } ridingMachine ? $"\n카이 · D-휠 《{ridingMachine}》" : "";
            string loadout = ForcedDeckFor(episode) is { } pov ? $"아스카 시점 · {pov.name}" : SelectedStoryDeckLabel();
            string difficulty = IsGxRpg && deck != null ? $"\n상대 · {GxDeckPreview(deck)}" : "";
            return $"{loadout}{machine}{difficulty}\n내 LP {player.Life:N0} · 패 {player.Hand}장 / 상대 시작 패 {opponent.Hand}장{field}{supports}";
        }

        private void PickOpponentOpening(StoryEpisodeData episode, Action restore)
        {
            var raid = GetRaid(episode);
            var deck = GetOpponentMatch(episode).Deck;
            uint monster = StoryBattlePlan.SelectOpeningMonster(deck, raid, episode.gimmick);
            _returnFromOpeningPicker = restore;
            string hand = $"상대 시작 패 +{StoryBattlePlan.ExtraOpeningCards}장";
            string preplaced = monster == 0 ? "선배치할 몬스터 없음" : $"몬스터 선배치 · {CardDatabase.GetName(monster)}";
            var choices = new List<StoryChoice>();
            if (_stance != StoryStance.Challenge)
                choices.Add(Option(StoryOpponentOpening.Standard, "기본 시작 조건"));
            choices.Add(Option(StoryOpponentOpening.ExtraHand, hand));
            choices.Add(Option(StoryOpponentOpening.PreplacedMonster, preplaced, monster != 0));
            choices.Add(Option(StoryOpponentOpening.ExtraHandAndMonster, $"{hand}\n{preplaced}", monster != 0));
            choices.Add(new StoryChoice("돌아가기", CloseOpeningPicker));
            _dialogue.ShowChoices(() => PreparationSummary(episode, raid, deck), choices.ToArray());

            StoryChoice Option(StoryOpponentOpening opening, string label, bool available = true) =>
                new(label, () =>
                {
                    _opponentOpening = opening;
                    CloseOpeningPicker();
                }, () => available);
        }

        private void CloseOpeningPicker()
        {
            var restore = _returnFromOpeningPicker;
            _returnFromOpeningPicker = null;
            restore?.Invoke();
        }

        private void PickSupports(StoryEpisodeData episode) => _dialogue.OpenSupportPicker(GetRaid(episode).HasValue, _supports,
            items => { _supports = items; });

        private static string TitleOf(StoryEpisodeData episode) =>
            episode.title.StartsWith("[BOSS RAID] ", StringComparison.Ordinal) ? episode.title.Substring(12) : episode.title;

        private string SelectedStoryDeckLabel() => _playableDecks.Count == 0
            ? DuelDeckAvailability.ExplainEmptyList(_deckId) : $"내 덱 · {_playableDecks[_deckPicker.value].name}";

        private void PickDeck() => PickDeck(null);

        private void PickDeck(Action afterSelection) => _dialogue.OpenDeckPicker(_playableDecks, _deckPicker.value, index =>
        {
            _deckPicker.SetValueWithoutNotify(index);
            _deckPicker.RefreshShownValue();
            SelectDeck(index);
            if (afterSelection != null) afterSelection();
            else _dialogue.RefreshChoices();
        }, () =>
        {
            if (afterSelection != null) _resumeAfterDeckEditor = () => PickDeck(afterSelection);
            _app.OpenStoryDeckEditor();
        }, _deckId);

        private void ChooseTurnOrder(StoryEpisodeData episode, Action restore)
        {
            if (!HasEpisodeDeck(episode) || _returnFromTurnOrder != null) return;
            var myDeck = ForcedDeckFor(episode) ?? _playableDecks[_deckPicker.value];
            var rivalDeck = GetOpponentMatch(episode).Deck;
            if (rivalDeck == null) return;
            var plan = CurrentBattlePlan;
            _returnFromTurnOrder = restore;
            string departure = RidingMachineFor(episode) is { } machine ? $"《{machine}》 출격\n" : "";
            ShowHands(departure + "가위바위보 · 이기면 선공 또는 후공을 선택합니다.");

            void ShowHands(string summary) => _dialogue.ShowChoices(() => summary,
                new(TurnOrderHands[0], () => PlayHand(0)), new(TurnOrderHands[1], () => PlayHand(1)),
                new(TurnOrderHands[2], () => PlayHand(2)), new("돌아가기", CancelTurnOrder));

            void PlayHand(int hand)
            {
                int opponent = _turnOrderRandom.Next(TurnOrderHands.Length);
                string result = $"나 {TurnOrderHands[hand]} · 상대 {TurnOrderHands[opponent]}";
                if (hand == opponent)
                    ShowHands(result + "\n무승부 · 다시 선택하세요.");
                else if ((hand - opponent + TurnOrderHands.Length) % TurnOrderHands.Length == 1)
                    _dialogue.ShowChoices(() => result + "\n승리 · 선공 또는 후공을 선택하세요.",
                        new("선공", () => Start(true)), new("후공", () => Start(false)), new("돌아가기", CancelTurnOrder));
                else
                {
                    bool rivalFirst = OfflineRanked.OpponentTakesFirst(rivalDeck);
                    _startAfterLoss = () => Start(!rivalFirst);
                    _lossRevealTime = 1.1f;
                    _dialogue.ShowChoices(() => result + (rivalFirst ? "\n상대가 선공을 선택했습니다." : "\n상대가 후공을 선택했습니다."),
                        new("듀얼 시작", _startAfterLoss), new("돌아가기", CancelTurnOrder));
                }
            }

            void Start(bool playerFirst)
            {
                if (_returnFromTurnOrder == null) return;
                ClearTurnOrder();
                if (FromRpgField) PlayRpgFaceoff(episode, rivalDeck.title, Launch);
                else Launch();

                void Launch()
                {
                    _app.StartStoryDuel(episode, myDeck, playerFirst, plan);
                    if (Root.activeInHierarchy) restore();
                }
            }
        }

        private void CancelTurnOrder()
        {
            var restore = _returnFromTurnOrder;
            ClearTurnOrder();
            restore?.Invoke();
        }

        private void ClearTurnOrder()
        {
            _returnFromTurnOrder = null;
            _startAfterLoss = null;
        }

        private void PlayDialogue(StoryEpisodeData episode, StoryDialogueLine[] lines, string heading, Action onFinish)
        {
            _browser.interactable = false;
            _browser.blocksRaycasts = false;
            var field = FromRpgField ? _app.RpgField : null;
            string hero = episode.playAsCharacterId > 0 ? DeckRepository.GetCharacter(episode.playAsCharacterId)?.name ?? "아스카" :
                Script.protagonist;
            if (field != null)
            {
                if (field.Root.transform.GetSiblingIndex() > Root.transform.GetSiblingIndex())
                    field.Root.transform.SetSiblingIndex(Root.transform.GetSiblingIndex());
                field.BeginRpgDialogue(episode, hero, lines);
                _backdrop.enabled = false;
                _browser.gameObject.SetActive(false);
            }
            _dialogue.Open(heading, hero, DeckRepository.GetCharacter(episode.charId), lines, onFinish, _rpgWorld, field,
                rpgEpisode: RpgWardrobe.EpisodeNumber(episode), playAsCharacterId: episode.playAsCharacterId,
                originalProtagonist: Script.protagonist, opponentName: episode.opponentName,
                waitForOpponentGreeting: IsGxRpg && episode.id == "gx_cutscene_00_prologue");
        }

        private void ReturnToEpisodes()
        {
            if (FromRpgField)
            {
                _app.ReturnToRpgField();
                return;
            }
            ResetDialogue();
            Focus(_episodeButtons[_selectedIndex]);
            DeckPadUi.ScrollTo(_episodeButtons[_selectedIndex]);
        }

        private void ResetDialogue()
        {
            CancelRpgFaceoff();
            ClearTurnOrder();
            _returnFromOpeningPicker = null;
            _finishFieldResult = null;
            _resumeAfterDeckEditor = null;
            _dialogue.Close();
            _backdrop.enabled = true;
            _browser.gameObject.SetActive(true);
            _browser.interactable = true;
            _browser.blocksRaycasts = true;
        }

        private static RectTransform Region(Transform parent, string name, float left, float bottom, float right, float top) =>
            DuelUiKit.CreateRect(name, parent, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Text AddLabel(Transform parent, string name, float left, float bottom, float right, float top,
            int size, TextAnchor alignment = TextAnchor.MiddleCenter, Color? color = null) =>
            DuelUiKit.AddFittedText(Region(parent, name, left, bottom, right, top), "Label", "", size, 13,
                alignment, color ?? DuelUiKit.TextPrimary, FontStyle.Bold);

        private static Button AddButton(Transform parent, string name, string label,
            float left, float bottom, float right, float top, Action action, bool gold = false)
        {
            var button = DuelUiKit.CreateButton(Region(parent, name, left, bottom, right, top), "Button", label, action, 22, gold);
            var text = button.GetComponentInChildren<Text>();
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 13;
            text.resizeTextMaxSize = 22;
            text.supportRichText = false;
            return button;
        }

        private static void Focus(Selectable control) => EventSystem.current?.SetSelectedGameObject(control.gameObject);
    }
}
