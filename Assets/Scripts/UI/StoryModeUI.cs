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
        public StoryDialogueLine[] preDialogues;
        public StoryDialogueLine[] postWinDialogues;
        public StoryDialogueLine[] postLoseDialogues;
        public StoryDialogueOption[] choices;
        public int choiceAfterLine;
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
        private const int ChapterCount = 5;
        private const int EpisodesPerChapter = 10;
        private static readonly (string Id, string Label)[] Series =
        {
            ("dm", "DM"), ("gx", "GX"), ("5ds", "5D's"), ("zexal", "ZEXAL"), ("arcv", "ARC-V"), ("vrains", "VRAINS")
        };

        private readonly GameAppController _app;
        private readonly (string Id, Button Button, StoryScriptRoot Script)[] _seriesTabs;
        private readonly List<UserDeck> _playableDecks = new();
        private readonly Button[] _chapterButtons = new Button[ChapterCount];
        private readonly Button[] _episodeButtons = new Button[EpisodesPerChapter];
        private readonly CanvasGroup _browser;
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
        private int _unlockedIndex;
        private int _selectedIndex;
        private string _deckId;
        private StoryStance _stance;
        private RaidSupport _supports;

        internal GameObject Root { get; }
        internal GameObject ActiveRoot => _dialogue.IsOpen ? _dialogue.ActiveRoot : Root;

        private StoryScriptRoot Script => _seriesTabs[_selectedSeries].Script;
        private string SeriesId => _seriesTabs[_selectedSeries].Id;

        internal StoryModeUI(GameAppController app, Transform parent)
        {
            _app = app;
            var root = LotdUiKit.FillRect("Screen_StoryMode", parent);
            Root = root.gameObject;
            LotdUiKit.AddImage(root.gameObject, LotdUiKit.LoadSprite("LOTD/Background/bg_vrains"),
                new Color(.36f, .44f, .58f), false, false);

            var browser = LotdUiKit.FillRect("StoryBrowser", root);
            _browser = browser.gameObject.AddComponent<CanvasGroup>();
            var content = Application.isMobilePlatform ? MobileSafeArea.Create(browser) : browser;
            var header = Region(content, "Header", .032f, .892f, .655f, .977f);
            LotdUiKit.AddImage(header.gameObject, LotdUiKit.LoadSprite("LOTD/Shared/menuheader"), Color.white, false, false);
            _seriesTitle = LotdUiKit.AddFittedText(header, "Title", "", 32, 16,
                TextAnchor.MiddleCenter, new Color(.025f, .075f, .14f), FontStyle.Bold);
            _seriesTabs = new (string, Button, StoryScriptRoot)[Series.Length];
            for (int i = 0; i < Series.Length; i++)
            {
                int index = i;
                var (id, label) = Series[i];
                float left = .032f + .156f * i;
                var tab = AddButton(content, $"Series_{id}", label, left, .817f, left + .147f, .886f, () => SelectSeries(index));
                _seriesTabs[i] = (id, tab, LoadScript(Application.streamingAssetsPath, id));
            }
            _selectedSeries = Mathf.Max(0, Array.FindIndex(_seriesTabs, tab => tab.Script != null));
            _mainMenu = AddButton(content, "MainMenu", "메인 메뉴", .83f, .897f, .968f, .973f,
                () => _app.ShowScreen(AppScreen.MainMenu));
            _tournament = AddButton(content, "DimensionTournament", "차원 대항전", .667f, .897f, .82f, .973f,
                () => _app.ShowScreen(AppScreen.Tournament), gold: true);

            const float tabWidth = .1872f;
            for (int chapter = 0; chapter < _chapterButtons.Length; chapter++)
            {
                int index = chapter;
                float left = .032f + index * tabWidth;
                _chapterButtons[index] = AddButton(content, $"Chapter_{index + 1}", "",
                    left, .742f, left + tabWidth - .007f, .809f, () => SelectChapter(index));
            }

            var listing = LotdUiKit.CreateMenuPanel(content, "Episodes", new Vector2(.032f, .08f), new Vector2(.57f, .73f));
            for (int row = 0; row < _episodeButtons.Length; row++)
            {
                int index = row;
                float top = .956f - row * .0915f;
                _episodeButtons[row] = AddButton(listing, $"Episode_{row + 1}", "", .049f, top - .08f, .951f, top,
                    () => SelectEpisode(_selectedIndex / EpisodesPerChapter * EpisodesPerChapter + index));
                _episodeButtons[row].GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }

            var details = LotdUiKit.CreateMenuPanel(content, "EpisodeDetails",
                new Vector2(.587f, .08f), new Vector2(.968f, .73f));
            _episodeHeading = AddLabel(details, "Chapter", .07f, .88f, .93f, .955f, 21, color: LotdUiKit.GoldAccent);
            _episodeTitle = AddLabel(details, "EpisodeTitle", .07f, .758f, .93f, .885f, 26);
            var portrait = Region(details, "OpponentPortrait", .13f, .445f, .87f, .753f);
            _portrait = LotdUiKit.AddImage(portrait.gameObject, null, Color.white, true, false);
            _opponentName = AddLabel(details, "OpponentName", .07f, .379f, .93f, .442f, 23);
            _opponentDeck = AddLabel(details, "OpponentDeck", .07f, .317f, .93f, .375f, 19, color: LotdUiKit.TextMuted);
            AddLabel(details, "DeckLabel", .07f, .268f, .93f, .314f, 18,
                TextAnchor.MiddleLeft, LotdUiKit.TextMuted).text = "내 덱";
            var picker = Region(details, "DeckPicker", .07f, .19f, .93f, .268f);
            _deckPicker = LotdUiKit.CreateLotdDropdown(picker, "SelectDeck", Array.Empty<string>(), SelectDeck, dynamicChoices: true);
            _deckPicker.captionText.resizeTextMaxSize = 22;
            _deckPicker.captionText.fontSize = 22;
            _start = AddButton(details, "StartEpisode", "이야기 시작", .16f, .062f, .84f, .163f, StartEpisode, gold: true);
            _status = AddLabel(content, "Status", .045f, .018f, .955f, .073f, 21, color: LotdUiKit.GoldAccent);

            _dialogue = new StoryDialogueView(root);

            Close();
        }

        internal void Open()
        {
            Root.SetActive(true);
            ResetDialogue();
            CollectionProgress.MigrateStoryClears(_seriesTabs.Where(tab => tab.Script != null)
                .SelectMany(tab => tab.Script.episodes.Where(episode =>
                    PlayerPrefs.GetInt(ClearedKey(tab.Id, episode.id), 0) != 0).Select(episode => episode.id)));
            LoadDeckChoices();
            SelectSeries(_selectedSeries);
            _tournament.GetComponentInChildren<Text>().text = DimensionTournament.Unlocked ? "차원 대항전" : "차원 대항전 · 잠김";
            Focus(Script != null ? _episodeButtons[_selectedIndex % EpisodesPerChapter] : _mainMenu);
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

        internal void Update(float delta) => _dialogue.Update(delta);

        internal void Back()
        {
            if (!_dialogue.IsOpen)
            {
                _app.ShowScreen(AppScreen.MainMenu);
                return;
            }
            if (!_dialogue.ClosePopup()) ReturnToEpisodes();
        }

        internal bool CanPlay(StoryEpisodeData episode)
        {
            int index = IndexOf(episode);
            return index >= 0 && index <= _unlockedIndex;
        }

        internal void ShowDuelResult(StoryEpisodeData episode, bool won, bool draw, string reward, StoryStance stance = StoryStance.Neutral)
        {
            int index = IndexOf(episode);
            if (index < 0) return;
            SelectEpisode(index);
            _stance = stance;
            _supports = RaidSupport.None;
            string result = won ? "승리" : draw ? "무승부" : "패배";
            PlayDialogue(episode, won ? episode.postWinDialogues : episode.postLoseDialogues, $"{TitleOf(episode)} · {result}", () =>
            {
                if (won)
                {
                    if (Application.isPlaying) LotdAudio.PlayWin();
                    _dialogue.ShowChoices(() => reward,
                        new("다음 에피소드 바로 시작", () => OpenEpisode(index + 1), () =>
                            index + 1 < Script.episodes.Length && CanPlay(Script.episodes[index + 1])),
                        new("에피소드 목록으로", ReturnToEpisodes));
                }
                else
                    _dialogue.ShowChoices(() => reward,
                        new("즉시 재도전", () => LaunchDuel(episode), () => _playableDecks.Count > 0),
                        new("내 덱 변경 후 재도전", PickDeck),
                        new("에피소드 목록으로", ReturnToEpisodes),
                        new("전술 서포트 장착", () => PickSupports(episode)));
            });
        }

        internal void ShowStatus(string status) => _status.text = status;

        internal (PresetDeckInfo Deck, int Level, int LevelCount) GetOpponentMatch(StoryEpisodeData episode)
        {
            int episodeIndex = IndexOf(episode);
            if (episodeIndex < 0) return default;

            var decks = OpponentDeckLevels.GetDecks(episode.charId);
            if (decks.Count == 0) decks = DeckRepository.GetDecksForCharacter(episode.charId);
            if (decks.Count == 0) return default;
            if (GetRaid(episode) != null)
            {
                int floor = (episode.chapter * (decks.Count - 1) + ChapterCount - 1) / ChapterCount;
                return (decks[floor], floor + 1, decks.Count);
            }

            var episodes = Script.episodes;
            int appearances = episodes.Count(entry => entry.charId == episode.charId);
            int remaining = appearances;
            int level = 0;
            int finalIndex = episodes.Length - 1;
            for (int i = 0; i <= episodeIndex; i++)
            {
                if (episodes[i].charId != episode.charId) continue;
                remaining--;
                int progressLevel = 1 + (i * (decks.Count - 1) + finalIndex / 2) / finalIndex;
                // Reserve a higher deck for each rematch when the opponent has enough levels.
                level = appearances <= decks.Count
                    ? Math.Clamp(progressLevel, level + 1, decks.Count - remaining)
                    : progressLevel;
            }

            return (decks[level - 1], level, decks.Count);
        }

        internal StoryRaid? GetRaid(StoryEpisodeData episode) => StoryRaid.For(SeriesId, IndexOf(episode) + 1);

        private static string ClearedKey(string seriesId, string episodeId) => $"ygo.story.{seriesId}.cleared.{episodeId}";

        private int IndexOf(StoryEpisodeData episode) =>
            Script == null || episode == null ? -1 : Array.IndexOf(Script.episodes, episode);

        internal static StoryScriptRoot LoadScript(string streamingAssets, string seriesId)
        {
            string path = Path.Combine(streamingAssets, $"story_{seriesId}_script.json");
            try
            {
                var script = JsonUtility.FromJson<StoryScriptRoot>(File.ReadAllText(path));
                int count = ChapterCount * EpisodesPerChapter;
                if (script == null || script.seriesId != seriesId || script.totalChapters != ChapterCount ||
                    script.totalEpisodes != count || script.episodes?.Length != count)
                    throw new InvalidDataException("Expected five chapters of ten episodes.");

                var ids = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    var episode = script.episodes[i];
                    if (episode == null || episode.chapter != i / EpisodesPerChapter + 1 ||
                        string.IsNullOrWhiteSpace(episode.id) || !ids.Add(episode.id) ||
                        string.IsNullOrWhiteSpace(episode.chapterTitle) || string.IsNullOrWhiteSpace(episode.title) ||
                        !HasDialogue(episode.preDialogues) || !HasDialogue(episode.postWinDialogues) ||
                        !HasDialogue(episode.postLoseDialogues))
                        throw new InvalidDataException($"Incomplete episode at index {i}.");
                }
                return script;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidDataException)
            {
                Debug.LogWarning($"[StoryMode] Could not load {seriesId} campaign: {ex.Message}");
                return null;
            }
        }

        private static bool HasDialogue(StoryDialogueLine[] lines) => lines?.Length > 0 &&
            lines.All(line => line != null && !string.IsNullOrWhiteSpace(line.speaker) && !string.IsNullOrWhiteSpace(line.text));

        private void SelectSeries(int index)
        {
            if (!Root.activeSelf || _dialogue.IsOpen) return;
            _selectedSeries = index;
            _seriesTitle.text = Script?.seriesTitle ?? "스토리 모드";
            for (int i = 0; i < _seriesTabs.Length; i++)
            {
                var tab = _seriesTabs[i];
                tab.Button.interactable = tab.Script != null;
                tab.Button.GetComponentInChildren<Text>().color = i == index ? LotdUiKit.GoldAccent : LotdUiKit.TextPrimary;
            }
            for (int i = 0; i < _chapterButtons.Length; i++)
            {
                _chapterButtons[i].interactable = Script != null;
                _chapterButtons[i].GetComponentInChildren<Text>().text = $"제{i + 1}장";
            }
            if (Script == null) _unlockedIndex = 0;
            else
            {
                int nextEpisode = Array.FindIndex(Script.episodes, episode => !CollectionProgress.IsStoryEpisodeCleared(episode.id));
                _unlockedIndex = nextEpisode >= 0 ? nextEpisode : Script.episodes.Length - 1;
            }
            SelectEpisode(_unlockedIndex);
        }

        private void SelectChapter(int chapter)
        {
            if (Script == null) return;
            SelectEpisode(Mathf.Clamp(_unlockedIndex, chapter * EpisodesPerChapter, (chapter + 1) * EpisodesPerChapter - 1));
        }

        private void SelectEpisode(int index)
        {
            if (Script == null)
            {
                foreach (var button in _chapterButtons) button.interactable = false;
                foreach (var button in _episodeButtons) button.interactable = false;
                _start.interactable = false;
                _deckPicker.interactable = false;
                ShowStatus("스토리 대본을 불러오지 못했습니다.");
                return;
            }

            _selectedIndex = index;
            int chapter = index / EpisodesPerChapter;
            for (int i = 0; i < _chapterButtons.Length; i++)
                _chapterButtons[i].GetComponentInChildren<Text>().color = i == chapter ? LotdUiKit.GoldAccent : LotdUiKit.TextPrimary;

            for (int row = 0; row < _episodeButtons.Length; row++)
            {
                int episodeIndex = chapter * EpisodesPerChapter + row;
                var episode = Script.episodes[episodeIndex];
                bool cleared = CollectionProgress.IsStoryEpisodeCleared(episode.id);
                bool locked = episodeIndex > _unlockedIndex;
                string state = cleared ? "클리어" : locked ? "잠김" : "도전 가능";
                var label = _episodeButtons[row].GetComponentInChildren<Text>();
                var raid = GetRaid(episode);
                string badge = raid is { } challenge ? $"◆ BOSS RAID {challenge.Opening.Life:N0} LP  " : "";
                label.text = $"  {episodeIndex + 1:00}화  {badge}{TitleOf(episode)}  ·  {state}";
                label.color = episodeIndex == index ? LotdUiKit.GoldAccent : locked ? LotdUiKit.TextMuted :
                    raid.HasValue ? LotdUiKit.RedAccent : LotdUiKit.TextPrimary;
            }

            var current = Script.episodes[index];
            var opponent = DeckRepository.GetCharacter(current.charId);
            _episodeHeading.text = current.chapterTitle;
            _episodeTitle.text = TitleOf(current);
            int bond = CollectionProgress.GetStoryBond(current.charId);
            _opponentName.text = bond > 0 ? $"{current.opponentName} · 유대도 {bond:N0}" : current.opponentName;
            _portrait.sprite = LotdUiKit.LoadOpponentPortrait(opponent);
            _portrait.enabled = _portrait.sprite != null;
            var (deck, level, levelCount) = GetOpponentMatch(current);
            _opponentDeck.text = deck == null ? "" : $"Lv. {level}/{levelCount}  ·  {deck.title}";
            bool ready = opponent != null && deck != null;
            _start.interactable = CanPlay(current) && ready;
            string status = !CanPlay(current) ? "이전 에피소드를 클리어하면 열립니다." :
                !ready ? "상대 덱을 불러오지 못했습니다." :
                _playableDecks.Count == 0 ? "덱 편집에서 사용할 수 있는 덱을 완성하세요." : "";
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
            var deck = GetOpponentMatch(episode).Deck;
            var choices = StoryDialogueBranch.Create(episode, raid.HasValue,
                deck?.sigCard is > 0 ? CardDatabase.GetName(deck.sigCard) : "");
            PlayDialogue(episode, episode.preDialogues, TitleOf(episode), () =>
                _dialogue.ShowChoices(() => PreparationSummary(raid),
                    new("듀얼 개시!", () => LaunchDuel(episode), () => _playableDecks.Count > 0),
                    new("내 덱 변경", PickDeck),
                    new("대화 다시 보기", () => OpenEpisode(index)),
                    new("에피소드 목록으로", ReturnToEpisodes),
                    new("전술 서포트 장착 · 최대 2개", () => PickSupports(episode))));
            _dialogue.SetBranch(StoryDialogueBranch.AfterLine(episode), () =>
                _dialogue.ShowChoices(() => "어떻게 답할까?", choices.Select(choice => new StoryChoice(
                    $"{choice.Reply}\n{StoryDialogueBranch.Effect(choice.Stance)}", () =>
                    {
                        _stance = choice.Stance;
                        _dialogue.AnswerBranch(choice.Reply, episode.opponentName, choice.Response);
                    })).ToArray()));
        }

        private string PreparationSummary(StoryRaid? raid)
        {
            var (player, opponent) = new StoryBattlePlan(_stance, _supports).Apply(raid);
            string field = raid.HasValue ? $"\n초기 필드 · {CardDatabase.GetName(opponent.FieldCard)}" : "";
            string equipped = string.Join(" · ", RaidSupportCatalog.Items.Where(item => (_supports & item.Id) != 0)
                .Select(item => item.Name));
            string supports = equipped.Length > 0 ? $"\n장착 · {equipped}" : "";
            return $"{SelectedStoryDeckLabel()}\n내 LP {player.Life:N0} · 패 {player.Hand}장 / 상대 LP {opponent.Life:N0} · 패 {opponent.Hand}장{field}{supports}";
        }

        private void PickSupports(StoryEpisodeData episode) => _dialogue.OpenSupportPicker(GetRaid(episode).HasValue, _supports,
            items => { _supports = items; });

        private static string TitleOf(StoryEpisodeData episode) =>
            episode.title.StartsWith("[BOSS RAID] ", StringComparison.Ordinal) ? episode.title.Substring(12) : episode.title;

        private string SelectedStoryDeckLabel() => _playableDecks.Count == 0
            ? "사용 가능한 덱이 없습니다." : $"내 덱 · {_playableDecks[_deckPicker.value].name}";

        private void PickDeck() => _dialogue.OpenDeckPicker(_playableDecks, _deckPicker.value, index =>
        {
            _deckPicker.SetValueWithoutNotify(index);
            _deckPicker.RefreshShownValue();
            SelectDeck(index);
            _dialogue.RefreshChoices();
        }, _app.OpenStoryDeckEditor);

        private void LaunchDuel(StoryEpisodeData episode)
        {
            if (_playableDecks.Count > 0)
                _app.StartStoryDuel(episode, _playableDecks[_deckPicker.value], new StoryBattlePlan(_stance, _supports));
        }

        private void PlayDialogue(StoryEpisodeData episode, StoryDialogueLine[] lines, string heading, Action onFinish)
        {
            _browser.interactable = false;
            _browser.blocksRaycasts = false;
            var portrait = LotdUiKit.LoadOpponentPortrait(DeckRepository.GetCharacter(episode.charId));
            _dialogue.Open(heading, Script.protagonist, portrait, lines, onFinish);
        }

        private void ReturnToEpisodes()
        {
            ResetDialogue();
            Focus(_episodeButtons[_selectedIndex % EpisodesPerChapter]);
        }

        private void ResetDialogue()
        {
            _dialogue.Close();
            _browser.interactable = true;
            _browser.blocksRaycasts = true;
        }

        private static RectTransform Region(Transform parent, string name, float left, float bottom, float right, float top) =>
            LotdUiKit.CreateRect(name, parent, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Text AddLabel(Transform parent, string name, float left, float bottom, float right, float top,
            int size, TextAnchor alignment = TextAnchor.MiddleCenter, Color? color = null) =>
            LotdUiKit.AddFittedText(Region(parent, name, left, bottom, right, top), "Label", "", size, 13,
                alignment, color ?? LotdUiKit.TextPrimary, FontStyle.Bold);

        private static Button AddButton(Transform parent, string name, string label,
            float left, float bottom, float right, float top, Action action, bool gold = false)
        {
            var button = LotdUiKit.CreateLotdButton(Region(parent, name, left, bottom, right, top), "Button", label, action, 22, gold);
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
