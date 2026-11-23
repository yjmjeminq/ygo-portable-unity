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

    internal sealed class StoryModeUI
    {
        internal const string UnlockedKey = "ygo.story.gx.unlocked_idx";
        private const int EpisodesPerChapter = 10;
        private static readonly string[] ChapterNames =
        {
            "1장 기숙사", "2장 세븐스타즈 삼환마", "3장 빛의결사 제넥스", "4장 이차원", "5장 다크니스 졸업",
        };

        private readonly GameAppController _app;
        private readonly StoryScriptRoot _script;
        private readonly List<UserDeck> _playableDecks = new();
        private readonly Button[] _chapterButtons = new Button[ChapterNames.Length];
        private readonly Button[] _episodeButtons = new Button[EpisodesPerChapter];
        private readonly CanvasGroup _browser;
        private readonly GameObject _dialogue;
        private readonly Button _mainMenu;
        private readonly Button _start;
        private readonly Button _next;
        private readonly Button _skip;
        private readonly Dropdown _deckPicker;
        private readonly Image _portrait;
        private readonly Image _dialoguePortrait;
        private readonly Text _episodeHeading;
        private readonly Text _episodeTitle;
        private readonly Text _opponentName;
        private readonly Text _status;
        private readonly Text _dialogueHeading;
        private readonly Text _speaker;
        private readonly Text _line;
        private readonly Text _counter;

        private int _unlockedIndex;
        private int _selectedIndex;
        private string _deckId;
        private StoryDialogueLine[] _dialogueLines;
        private int _lineIndex;
        private Action _afterDialogue;

        internal GameObject Root { get; }
        internal GameObject ActiveRoot => _dialogue.activeSelf ? _dialogue : Root;

        internal StoryModeUI(GameAppController app, Transform parent)
        {
            _app = app;
            _script = LoadScript();
            var root = LotdUiKit.FillRect("Screen_StoryMode", parent);
            Root = root.gameObject;
            LotdUiKit.AddImage(root.gameObject, LotdUiKit.LoadSprite("LOTD/Background/bg_vrains"),
                new Color(.36f, .44f, .58f), false, false);

            var browser = LotdUiKit.FillRect("StoryBrowser", root);
            _browser = browser.gameObject.AddComponent<CanvasGroup>();
            var content = Application.isMobilePlatform ? MobileSafeArea.Create(browser) : browser;
            var header = Region(content, "Header", .032f, .865f, .79f, .963f);
            LotdUiKit.AddImage(header.gameObject, LotdUiKit.LoadSprite("LOTD/Shared/menuheader"), Color.white, false, false);
            LotdUiKit.AddFittedText(header, "Title", _script?.seriesTitle ?? "유희왕 GX: 홍련의 듀얼 퀸", 32, 18,
                TextAnchor.MiddleCenter, new Color(.025f, .075f, .14f), FontStyle.Bold);
            _mainMenu = AddButton(content, "MainMenu", "메인 메뉴", .81f, .874f, .968f, .954f,
                () => _app.ShowScreen(AppScreen.MainMenu));

            const float tabWidth = .1872f;
            for (int chapter = 0; chapter < _chapterButtons.Length; chapter++)
            {
                int index = chapter;
                float left = .032f + index * tabWidth;
                _chapterButtons[index] = AddButton(content, $"Chapter_{index + 1}", ChapterNames[index],
                    left, .773f, left + tabWidth - .007f, .848f, () => SelectChapter(index));
            }

            var listing = LotdUiKit.CreateMenuPanel(content, "Episodes", new Vector2(.032f, .08f), new Vector2(.57f, .751f));
            for (int row = 0; row < _episodeButtons.Length; row++)
            {
                int index = row;
                float top = .956f - row * .0915f;
                _episodeButtons[row] = AddButton(listing, $"Episode_{row + 1}", "", .049f, top - .08f, .951f, top,
                    () => SelectEpisode(_selectedIndex / EpisodesPerChapter * EpisodesPerChapter + index));
                _episodeButtons[row].GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }

            var details = LotdUiKit.CreateMenuPanel(content, "EpisodeDetails",
                new Vector2(.587f, .08f), new Vector2(.968f, .751f));
            _episodeHeading = AddLabel(details, "Chapter", .07f, .88f, .93f, .955f, 21, color: LotdUiKit.GoldAccent);
            _episodeTitle = AddLabel(details, "EpisodeTitle", .07f, .758f, .93f, .885f, 26);
            var portrait = Region(details, "OpponentPortrait", .13f, .392f, .87f, .753f);
            _portrait = LotdUiKit.AddImage(portrait.gameObject, null, Color.white, true, false);
            _opponentName = AddLabel(details, "OpponentName", .07f, .327f, .93f, .39f, 23);
            AddLabel(details, "DeckLabel", .07f, .271f, .93f, .323f, 18,
                TextAnchor.MiddleLeft, LotdUiKit.TextMuted).text = "내 덱";
            var picker = Region(details, "DeckPicker", .07f, .19f, .93f, .268f);
            _deckPicker = LotdUiKit.CreateLotdDropdown(picker, "SelectDeck", Array.Empty<string>(), SelectDeck, dynamicChoices: true);
            _deckPicker.captionText.resizeTextMaxSize = 22;
            _deckPicker.captionText.fontSize = 22;
            _start = AddButton(details, "StartDuel", "듀얼 시작", .16f, .062f, .84f, .163f, StartEpisode, gold: true);
            _status = AddLabel(content, "Status", .045f, .018f, .955f, .073f, 21, color: LotdUiKit.GoldAccent);

            var overlay = LotdUiKit.FillRect("StoryDialogue", root);
            _dialogue = overlay.gameObject;
            LotdUiKit.AddImage(_dialogue, LotdUiKit.WhiteSprite, new Color(.018f, .028f, .045f, .85f));
            var dialogueContent = Application.isMobilePlatform ? MobileSafeArea.Create(overlay) : overlay;
            _dialogueHeading = AddLabel(dialogueContent, "Episode", .08f, .83f, .92f, .95f, 27, color: LotdUiKit.GoldAccent);
            var character = Region(dialogueContent, "OpponentPortrait", .26f, .33f, .74f, .845f);
            _dialoguePortrait = LotdUiKit.AddImage(character.gameObject, null, Color.white, true, false);
            var textbox = LotdUiKit.CreateMenuPanel(dialogueContent, "DialogueBox",
                new Vector2(.095f, .072f), new Vector2(.905f, .38f));
            _speaker = AddLabel(textbox, "Speaker", .055f, .72f, .945f, .925f, 29,
                TextAnchor.MiddleLeft, LotdUiKit.GoldAccent);
            _line = AddLabel(textbox, "Line", .055f, .26f, .945f, .73f, 29, TextAnchor.UpperLeft);
            _counter = AddLabel(textbox, "LineCount", .055f, .06f, .3f, .23f, 20, TextAnchor.MiddleLeft);
            _skip = AddButton(textbox, "Skip", "스킵 ⏭", .57f, .073f, .746f, .248f, FinishDialogue);
            _next = AddButton(textbox, "Next", "다음 ▶", .765f, .073f, .945f, .248f, AdvanceDialogue, gold: true);

            Close();
        }

        internal void Open()
        {
            Root.SetActive(true);
            ResetDialogue();
            LoadDeckChoices();
            _unlockedIndex = Mathf.Clamp(PlayerPrefs.GetInt(UnlockedKey, 0), 0, (_script?.episodes.Length ?? 1) - 1);
            SelectEpisode(_unlockedIndex);
            Focus(_script != null ? _episodeButtons[_selectedIndex % EpisodesPerChapter] : _mainMenu);
        }

        internal void Close()
        {
            _deckPicker.Hide();
            ResetDialogue();
            Root.SetActive(false);
        }

        internal void Back()
        {
            if (!_dialogue.activeSelf)
            {
                _app.ShowScreen(AppScreen.MainMenu);
                return;
            }
            ResetDialogue();
            Focus(_start);
        }

        internal bool CanPlay(StoryEpisodeData episode)
        {
            int index = IndexOf(episode);
            return index >= 0 && index <= _unlockedIndex;
        }

        internal void RecordWin(StoryEpisodeData episode)
        {
            if (!CanPlay(episode)) return;
            int index = IndexOf(episode);
            _unlockedIndex = Mathf.Max(_unlockedIndex, Mathf.Min(index + 1, _script.episodes.Length - 1));
            PlayerPrefs.SetInt(ClearedKey(episode.id), 1);
            PlayerPrefs.SetInt(UnlockedKey, _unlockedIndex);
            PlayerPrefs.Save();
        }

        internal void ShowDuelResult(StoryEpisodeData episode, bool won, bool draw, string reward)
        {
            int index = IndexOf(episode);
            if (index < 0) return;
            SelectEpisode(index);
            string result = won ? "승리" : draw ? "무승부" : "패배";
            if (!string.IsNullOrWhiteSpace(reward)) result += $"  ·  {reward}";
            ShowStatus(result);
            PlayDialogue(episode, won ? episode.postWinDialogues : episode.postLoseDialogues, result, () =>
            {
                if (won && index + 1 == _unlockedIndex) SelectEpisode(index + 1);
                Focus(_episodeButtons[_selectedIndex % EpisodesPerChapter]);
                ShowStatus(result);
            });
        }

        internal void ShowStatus(string status) => _status.text = status;

        internal static PresetDeckInfo GetOpponentDeck(int characterId) =>
            OpponentDeckLevels.GetDecks(characterId).FirstOrDefault() ??
            DeckRepository.GetDecksForCharacter(characterId).FirstOrDefault();

        internal static string ClearedKey(string episodeId) => $"ygo.story.gx.cleared.{episodeId}";

        private int IndexOf(StoryEpisodeData episode) =>
            _script == null || episode == null ? -1 : Array.IndexOf(_script.episodes, episode);

        private static StoryScriptRoot LoadScript()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "story_gx_script.json");
            try
            {
                var script = JsonUtility.FromJson<StoryScriptRoot>(File.ReadAllText(path));
                int count = ChapterNames.Length * EpisodesPerChapter;
                if (script == null || script.seriesId != "gx" || script.totalChapters != ChapterNames.Length ||
                    script.totalEpisodes != count || script.episodes?.Length != count)
                    throw new InvalidDataException("Expected five chapters of ten episodes.");

                var ids = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    var episode = script.episodes[i];
                    if (episode == null || episode.chapter != i / EpisodesPerChapter + 1 ||
                        string.IsNullOrWhiteSpace(episode.id) || !ids.Add(episode.id) ||
                        !HasDialogue(episode.preDialogues) || !HasDialogue(episode.postWinDialogues) ||
                        !HasDialogue(episode.postLoseDialogues))
                        throw new InvalidDataException($"Incomplete episode at index {i}.");
                }
                return script;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidDataException)
            {
                Debug.LogWarning($"[StoryMode] Could not load GX campaign: {ex.Message}");
                return null;
            }
        }

        private static bool HasDialogue(StoryDialogueLine[] lines) => lines?.Length > 0 &&
            lines.All(line => line != null && !string.IsNullOrWhiteSpace(line.speaker) && !string.IsNullOrWhiteSpace(line.text));

        private void SelectChapter(int chapter)
        {
            if (_script == null) return;
            SelectEpisode(Mathf.Clamp(_unlockedIndex, chapter * EpisodesPerChapter, (chapter + 1) * EpisodesPerChapter - 1));
        }

        private void SelectEpisode(int index)
        {
            if (_script == null)
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
                var episode = _script.episodes[episodeIndex];
                bool cleared = PlayerPrefs.GetInt(ClearedKey(episode.id), 0) != 0;
                bool locked = episodeIndex > _unlockedIndex;
                string state = cleared ? "클리어" : locked ? "잠김" : "도전 가능";
                var label = _episodeButtons[row].GetComponentInChildren<Text>();
                label.text = $"  {episodeIndex + 1:00}화  {episode.title}  ·  {state}";
                label.color = episodeIndex == index ? LotdUiKit.GoldAccent : locked ? LotdUiKit.TextMuted : LotdUiKit.TextPrimary;
            }

            var current = _script.episodes[index];
            var opponent = DeckRepository.GetCharacter(current.charId);
            _episodeHeading.text = $"{ChapterNames[chapter]}  ·  {index + 1:00}화";
            _episodeTitle.text = current.title;
            _opponentName.text = current.opponentName;
            _portrait.sprite = LotdUiKit.LoadOpponentPortrait(opponent);
            _portrait.enabled = _portrait.sprite != null;
            bool ready = opponent != null && GetOpponentDeck(current.charId) != null;
            _start.interactable = CanPlay(current) && ready && _playableDecks.Count > 0;
            ShowStatus(!CanPlay(current) ? "이전 에피소드를 클리어하면 열립니다." :
                !ready ? "상대 덱을 불러오지 못했습니다." :
                _playableDecks.Count == 0 ? "덱 편집에서 사용할 수 있는 덱을 완성하세요." : "");
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
            if (!_start.interactable || _dialogue.activeSelf) return;
            var episode = _script.episodes[_selectedIndex];
            var deck = _playableDecks[_deckPicker.value];
            PlayDialogue(episode, episode.preDialogues, episode.title, () => _app.StartStoryDuel(episode, deck));
        }

        private void PlayDialogue(StoryEpisodeData episode, StoryDialogueLine[] lines, string heading, Action onFinish)
        {
            _dialogueHeading.text = heading;
            _dialoguePortrait.sprite = LotdUiKit.LoadOpponentPortrait(DeckRepository.GetCharacter(episode.charId));
            _dialoguePortrait.enabled = _dialoguePortrait.sprite != null;
            _dialogueLines = lines;
            _lineIndex = 0;
            _afterDialogue = onFinish;
            _browser.interactable = false;
            _browser.blocksRaycasts = false;
            _dialogue.SetActive(true);
            SetDialogueLine();
            Focus(_next);
        }

        private void SetDialogueLine()
        {
            var line = _dialogueLines[_lineIndex];
            _speaker.text = line.speaker;
            _line.text = line.text;
            _counter.text = $"{_lineIndex + 1} / {_dialogueLines.Length}";
        }

        private void AdvanceDialogue()
        {
            if (_dialogueLines == null) return;
            if (++_lineIndex == _dialogueLines.Length) FinishDialogue();
            else SetDialogueLine();
        }

        private void FinishDialogue()
        {
            var next = _afterDialogue;
            ResetDialogue();
            next?.Invoke();
        }

        private void ResetDialogue()
        {
            _dialogueLines = null;
            _afterDialogue = null;
            _dialogue.SetActive(false);
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
