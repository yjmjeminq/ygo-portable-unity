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
        public string emotion;
        public string fx;
        public StoryStageAction[] stage;
    }

    [Serializable]
    public class StoryStageAction
    {
        public int actor;
        public string action;
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
        public StoryDialogueLine[] preDialogues;
        public StoryDialogueLine[] postWinDialogues;
        public StoryDialogueLine[] postLoseDialogues;
        public StoryDialogueOption[] choices;
        public int choiceAfterLine;
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
        private const string RpgSeriesId = "5ds";
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
        private readonly Button[] _episodeButtons = new Button[StoryCampaign.LegacyEpisodeCount];
        private readonly RectTransform _episodeRows;
        private readonly StoryScriptRoot _rpgScript;
        private readonly Dictionary<int, PresetDeckInfo> _rpgDecks;
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
        internal GameObject ActiveRoot => _dialogue.IsOpen ? _dialogue.ActiveRoot : Root;

        private StoryScriptRoot Script => _rpgWorld ? _rpgScript : _seriesTabs[_selectedSeries].Script;
        internal StoryModeUI(GameAppController app, Transform parent, System.Random turnOrderRandom = null)
        {
            _app = app;
            _turnOrderRandom = turnOrderRandom ?? new System.Random();
            _rpgScript = LoadRpgScript(Application.streamingAssetsPath);
            _rpgDecks = LoadRpgDecks(Application.streamingAssetsPath);
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

        internal void UseRpgCampaign(bool enabled, bool fromField = false)
        {
            _rpgWorld = enabled;
            FromRpgField = fromField;
        }

        internal void OpenFieldEpisode(int index)
        {
            if (FromRpgField && Script?.episodes != null && index >= 0 && index < Script.episodes.Length)
                OpenEpisode(index);
        }

        internal void ShowFieldDuelResult(StoryEpisodeData episode, bool won, bool draw, Func<string> settle)
        {
            string result = won ? "승리" : draw ? "무승부" : "패배";
            _finishFieldResult = () =>
            {
                _finishFieldResult = null;
                if (won && Application.isPlaying) DuelAudio.PlayWin();
                _app.ReturnToRpgField(settle());
            };
            PlayDialogue(episode, won ? episode.postWinDialogues : episode.postLoseDialogues,
                $"{TitleOf(episode)} · {result}", () => _finishFieldResult?.Invoke());
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
        }

        internal void Update(float delta)
        {
            _dialogue.Update(delta);
            if (_startAfterLoss != null && (_lossRevealTime -= delta) <= 0f)
                _startAfterLoss();
        }

        internal void Back()
        {
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
            else ReturnToEpisodes();
        }

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
            if (_rpgWorld && _rpgDecks.TryGetValue(episode.deckId, out var dedicated))
                return (dedicated, 0, 0);

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
            string path = Path.Combine(streamingAssets, seriesId == RpgSeriesId
                ? "story_5ds_classic_script.json" : $"story_{seriesId}_script.json");
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

        internal static StoryScriptRoot LoadRpgScript(string streamingAssets)
        {
            try
            {
                var script = JsonUtility.FromJson<StoryScriptRoot>(
                    File.ReadAllText(Path.Combine(streamingAssets, "story_5ds_script.json")));
                if (script?.seriesId?.Equals(RpgSeriesId, StringComparison.OrdinalIgnoreCase) != true ||
                    script.episodes?.Length != StoryCampaign.LegacyEpisodeCount)
                    throw new InvalidDataException("Expected the 50-episode RPG script.");
                for (int i = 0; i < script.episodes.Length; i++)
                {
                    var episode = script.episodes[i];
                    if (episode == null || episode.deckId != 59001 + i || string.IsNullOrWhiteSpace(episode.title) ||
                        !HasDialogue(episode.preDialogues) || !HasDialogue(episode.postWinDialogues) || !HasDialogue(episode.postLoseDialogues))
                        throw new InvalidDataException($"Invalid RPG episode {i + 1}.");
                    episode.id = $"5ds_rpg_ep_{i + 1:00}";
                    if (episode.gimmick == "none") episode.gimmick = null;
                }
                return script;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
                error is InvalidDataException)
            {
                Debug.LogWarning($"[StoryMode] Could not load the RPG campaign: {error.Message}");
                return null;
            }
        }

        internal static Dictionary<int, PresetDeckInfo> LoadRpgDecks(string streamingAssets)
        {
            var decks = new Dictionary<int, PresetDeckInfo>();
            try
            {
                var data = JsonUtility.FromJson<OpponentsDbWrapper>(
                    File.ReadAllText(Path.Combine(streamingAssets, "story_5ds_rpg_decks.json")));
                foreach (var deck in data?.decks ?? Array.Empty<PresetDeckInfo>())
                {
                    if (deck?.id is >= 59001 and <= 59050 && deck.main?.Length is >= 40 and <= 60 &&
                        deck.extra?.Length <= 15 && deck.side?.Length <= 15)
                        decks.TryAdd(deck.id, deck);
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
                int nextEpisode = Array.FindIndex(Script.episodes, episode => !CollectionProgress.IsStoryEpisodeCleared(episode.id));
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
                bool cleared = CollectionProgress.IsStoryEpisodeCleared(episode.id);
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
            _opponentDeck.text = deck == null ? "" : levelCount == 0 ? deck.title : $"Lv. {level}/{levelCount}  ·  {deck.title}";
            bool ready = opponent != null && deck != null;
            _start.interactable = CanPlay(current) && ready;
            string status = !CanPlay(current) ? "이전 에피소드를 클리어하면 열립니다." :
                !ready ? "상대 덱을 불러오지 못했습니다." :
                _playableDecks.Count == 0 ? DuelDeckAvailability.ExplainEmptyList(_deckId) : "";
            ShowStatus(GetRaid(current) is { } raidInfo
                ? string.IsNullOrEmpty(status) ? raidInfo.Summary : $"{raidInfo.Summary}\n{status}"
                : status);
        }

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
            var raid = GetRaid(episode);
            _stance = StoryStance.Neutral;
            _supports = RaidSupport.None;
            _opponentOpening = StoryOpponentOpening.Standard;
            var deck = GetOpponentMatch(episode).Deck;
            var choices = StoryDialogueBranch.Create(episode, raid.HasValue,
                deck?.sigCard is > 0 ? CardDatabase.GetName(deck.sigCard) : "");
            bool pickedInitialDeck = false;
            PlayDialogue(episode, episode.preDialogues, TitleOf(episode), ShowPreparation);
            _dialogue.SetBranch(StoryDialogueBranch.AfterLine(episode), () =>
                _dialogue.ShowChoices(() => "어떻게 답할까?", choices.Select(choice => new StoryChoice(
                    $"{choice.Reply}\n{StoryDialogueBranch.Effect(choice.Stance)}", () =>
                    {
                        _stance = choice.Stance;
                        _dialogue.AnswerBranch(choice.Reply, episode.opponentName, choice.Response);
                    })).ToArray()));

            void ShowPreparation()
            {
                _dialogue.ShowChoices(() => PreparationSummary(episode, raid, deck),
                    new("듀얼 개시!", () => ChooseTurnOrder(episode, ShowPreparation), () => _playableDecks.Count > 0),
                    new("내 덱 변경", PickDeck),
                    new("대화 다시 보기", () => OpenEpisode(index)),
                    new(FromRpgField ? "필드로 돌아가기" : "에피소드 목록으로", ReturnToEpisodes),
                    new("전술 서포트 장착 · 최대 2개", () => PickSupports(episode)),
                    new("상대 시작 조건 변경", () => PickOpponentOpening(episode, ShowPreparation)));
                if (_rpgWorld && !pickedInitialDeck)
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
            return $"{SelectedStoryDeckLabel()}{machine}\n내 LP {player.Life:N0} · 패 {player.Hand}장 / 상대 시작 패 {opponent.Hand}장{field}{supports}";
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

        private void PickDeck() => _dialogue.OpenDeckPicker(_playableDecks, _deckPicker.value, index =>
        {
            _deckPicker.SetValueWithoutNotify(index);
            _deckPicker.RefreshShownValue();
            SelectDeck(index);
            _dialogue.RefreshChoices();
        }, _app.OpenStoryDeckEditor, _deckId);

        private void ChooseTurnOrder(StoryEpisodeData episode, Action restore)
        {
            if (_playableDecks.Count == 0 || _returnFromTurnOrder != null) return;
            var myDeck = _playableDecks[_deckPicker.value];
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
                _app.StartStoryDuel(episode, myDeck, playerFirst, plan);
                if (Root.activeInHierarchy) restore();
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
            if (field != null)
            {
                if (field.Root.transform.GetSiblingIndex() > Root.transform.GetSiblingIndex())
                    field.Root.transform.SetSiblingIndex(Root.transform.GetSiblingIndex());
                field.BeginRpgDialogue(episode, Script.protagonist, lines);
                _backdrop.enabled = false;
                _browser.gameObject.SetActive(false);
            }
            _dialogue.Open(heading, Script.protagonist, DeckRepository.GetCharacter(episode.charId), lines, onFinish, _rpgWorld, field);
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
            ClearTurnOrder();
            _returnFromOpeningPicker = null;
            _finishFieldResult = null;
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
