using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed partial class StoryModeUI
    {
        internal readonly struct StoryChoice
        {
            internal readonly string Label;
            internal readonly Action Select;
            internal readonly Func<bool> Available;

            internal StoryChoice(string label, Action select, Func<bool> available = null)
            {
                Label = label;
                Select = select;
                Available = available;
            }
        }

        internal sealed class StoryDialogueView
        {
            private static readonly Color HeroColor = new(.47f, .85f, 1f);
            private static readonly Color OpponentColor = new(1f, .76f, .43f);
            private static readonly Color NarratorColor = new(.75f, .72f, .91f);
            private static readonly Color Listening = new(.48f, .53f, .63f);
            private const float CharactersPerSecond = 34f;

            private readonly GameObject _root;
            private readonly CanvasGroup _scene;
            private readonly Button _advanceSurface;
            private readonly Text _heading;
            private readonly Text _heroIdentity;
            private readonly Image _hero;
            private readonly Image _opponent;
            private readonly Text _speaker;
            private readonly Text _text;
            private readonly Text _counter;
            private readonly Button _next;
            private readonly Button _previous;
            private readonly Button _auto;
            private readonly Button _skip;
            private readonly Button _logButton;
            private readonly GameObject _choiceRoot;
            private readonly Text _choiceSummary;
            private readonly Button[] _choiceButtons = new Button[5];
            private readonly RaidSupportPanel _supports;
            private readonly GameObject _logRoot;
            private readonly Text _logText;
            private readonly ScrollRect _logScroll;
            private readonly Button _logClose;
            private readonly GameObject _pickerRoot;
            private readonly Dropdown _picker;
            private readonly Text _pickerStatus;
            private readonly Button _pickerApply;
            private readonly Button _pickerEdit;
            private StoryDialogueLine[] _dialogue;
            private Action _onEnd;
            private Func<string> _summary;
            private StoryChoice[] _choices;
            private Action<int> _confirmDeck;
            private Action _editDeck;
            private int[] _characters;
            private int _index;
            private int _readThrough;
            private int _visible;
            private float _typed;
            private float _readTime;
            private bool _autoPlay;
            private bool _finished;
            private bool _heroSpeaking;
            private bool _opponentSpeaking;
            private Selectable _popupReturn;
            private int _branchLine;
            private Action _branch;
            private bool _waitingBranch;

            internal bool IsOpen => _root.activeSelf;
            internal GameObject ActiveRoot => _logRoot.activeSelf ? _logRoot : _pickerRoot.activeSelf ? _pickerRoot :
                _supports.Root.activeSelf ? _supports.Root : _root;

            internal StoryDialogueView(Transform parent)
            {
                var overlay = LotdUiKit.FillRect("StoryDialogue", parent);
                _root = overlay.gameObject;
                var back = LotdUiKit.AddImage(_root, LotdUiKit.WhiteSprite, new Color(.014f, .026f, .058f, .95f));
                var surface = LotdUiKit.FillRect("AdvanceSurface", overlay);
                var pointer = LotdUiKit.AddImage(surface.gameObject, LotdUiKit.WhiteSprite, Color.clear);
                _advanceSurface = surface.gameObject.AddComponent<Button>();
                _advanceSurface.targetGraphic = pointer;
                _advanceSurface.transition = Selectable.Transition.None;
                _advanceSurface.navigation = new Navigation { mode = Navigation.Mode.None };
                _advanceSurface.onClick.AddListener(Advance);
                back.raycastTarget = true;

                var scene = LotdUiKit.FillRect("Scene", overlay);
                var content = Application.isMobilePlatform ? MobileSafeArea.Create(scene) : scene;
                _scene = content.gameObject.AddComponent<CanvasGroup>();
                _heading = AddLabel(content, "Episode", .06f, .88f, .94f, .965f, 29, color: LotdUiKit.GoldAccent);
                _heroIdentity = AddLabel(content, "ProtagonistIdentity", .065f, .837f, .495f, .884f, 17, color: HeroColor);
                var hero = Region(content, "Protagonist", .07f, .26f, .493f, .855f);
                var opponent = Region(content, "Opponent", .508f, .26f, .93f, .855f);
                _hero = LotdUiKit.AddImage(hero.gameObject, LotdUiKit.LoadSprite("Story/Protagonist"), Color.white, true, false);
                _opponent = LotdUiKit.AddImage(opponent.gameObject, null, Listening, true, false);

                var dialogue = LotdUiKit.CreateMenuPanel(content, "DialogueBox",
                    new Vector2(.054f, .064f), new Vector2(.946f, .406f));
                var dialogueHit = dialogue.gameObject.AddComponent<Button>();
                dialogueHit.transition = Selectable.Transition.None;
                dialogueHit.navigation = new Navigation { mode = Navigation.Mode.None };
                dialogueHit.onClick.AddListener(Advance);
                var speakerPlate = Region(dialogue, "SpeakerBadge", .038f, .703f, .35f, .95f);
                LotdUiKit.AddImage(speakerPlate.gameObject, LotdUiKit.WhiteSprite, new Color(.028f, .063f, .13f, .94f), false, false);
                _speaker = AddLabel(speakerPlate, "Speaker", .08f, .05f, .94f, .95f, 23, TextAnchor.MiddleLeft);
                _text = AddLabel(dialogue, "Text", .047f, .234f, .947f, .69f, 29, TextAnchor.UpperLeft);
                _text.supportRichText = false;
                _counter = AddLabel(dialogue, "LineCount", .048f, .048f, .26f, .208f, 16, TextAnchor.MiddleLeft);
                _previous = AddButton(dialogue, "Previous", "◀ 이전 대사", .36f, .727f, .50f, .927f, Previous);
                _logButton = AddButton(dialogue, "Log", "대화 로그", .51f, .727f, .647f, .927f, OpenLog);
                _auto = AddButton(dialogue, "Auto", "오토 꺼짐", .657f, .727f, .787f, .927f, ToggleAuto);
                _skip = AddButton(dialogue, "Skip", "건너뛰기", .797f, .727f, .952f, .927f, Finish);
                _next = AddButton(dialogue, "Next", "다음 ▶", .778f, .042f, .952f, .214f, Advance, gold: true);

                var choices = LotdUiKit.CreateMenuPanel(content, "Choices", new Vector2(.261f, .408f), new Vector2(.739f, .833f));
                _choiceRoot = choices.gameObject;
                _choiceSummary = AddLabel(choices, "Summary", .065f, .762f, .935f, .966f, 21, color: LotdUiKit.GoldAccent);
                for (int i = 0; i < _choiceButtons.Length; i++)
                {
                    int index = i;
                    float top = .75f - i * .14f;
                    _choiceButtons[i] = AddButton(choices, $"Choice_{i}", "", .075f, top - .122f, .925f, top,
                        () => _choices[index].Select(), gold: i == 0);
                }

                var log = BuildPopup(overlay, "Backlog", .12f, .102f, .88f, .901f);
                _logRoot = log.Root;
                AddLabel(log.Panel, "Title", .075f, .85f, .927f, .954f, 26, color: LotdUiKit.GoldAccent).text = "대화 로그";
                var scrollHost = Region(log.Panel, "Scroll", .068f, .19f, .922f, .825f);
                _logScroll = scrollHost.gameObject.AddComponent<ScrollRect>();
                _logScroll.horizontal = false;
                _logScroll.movementType = ScrollRect.MovementType.Clamped;
                _logScroll.scrollSensitivity = 38f;
                var viewport = LotdUiKit.FillRect("Viewport", scrollHost);
                LotdUiKit.AddImage(viewport.gameObject, LotdUiKit.WhiteSprite, new Color(.009f, .023f, .048f, .92f));
                viewport.gameObject.AddComponent<RectMask2D>();
                var history = LotdUiKit.CreateRect("History", viewport, new Vector2(0f, 1f), Vector2.one,
                    new Vector2(14f, -4f), new Vector2(-20f, -4f));
                history.pivot = new Vector2(.5f, 1f);
                _logText = LotdUiKit.AddFittedText(history, "Text", "", 23, 23, TextAnchor.UpperLeft, LotdUiKit.TextPrimary);
                _logText.resizeTextForBestFit = false;
                _logText.supportRichText = false;
                _logText.verticalOverflow = VerticalWrapMode.Overflow;
                _logScroll.viewport = viewport;
                _logScroll.content = history;
                _logScroll.verticalScrollbar = LotdUiKit.AddVerticalScrollbar(scrollHost);
                AddButton(log.Panel, "LogUp", "위로", .075f, .065f, .225f, .163f, () => ScrollLog(.23f));
                AddButton(log.Panel, "LogDown", "아래로", .235f, .065f, .385f, .163f, () => ScrollLog(-.23f));
                _logClose = AddButton(log.Panel, "LogClose", "닫기", .735f, .065f, .925f, .163f, () => ClosePopup(), gold: true);

                var picker = BuildPopup(overlay, "DeckSelection", .219f, .212f, .781f, .789f);
                _pickerRoot = picker.Root;
                AddLabel(picker.Panel, "Title", .09f, .755f, .91f, .943f, 28, color: LotdUiKit.GoldAccent).text = "내 덱 변경";
                var dropdown = Region(picker.Panel, "Decks", .092f, .526f, .908f, .704f);
                _picker = LotdUiKit.CreateLotdDropdown(dropdown, "StoryDeck", Array.Empty<string>(), _ => { }, dynamicChoices: true);
                _pickerStatus = AddLabel(picker.Panel, "DeckStatus", .092f, .469f, .908f, .532f, 18, color: LotdUiKit.TextMuted);
                _pickerApply = AddButton(picker.Panel, "Apply", "선택한 덱 적용", .517f, .315f, .908f, .472f, ApplyDeck, gold: true);
                _pickerEdit = AddButton(picker.Panel, "Edit", "덱 편집 열기", .092f, .315f, .483f, .472f, () => _editDeck?.Invoke());
                AddButton(picker.Panel, "Back", "돌아가기", .31f, .098f, .69f, .246f, CloseDeckPicker);
                _supports = new RaidSupportPanel(overlay, CloseSupportPicker);
                Close();
            }

            internal void Open(string heading, string protagonist, Sprite opponent, StoryDialogueLine[] lines, Action onEnd)
            {
                Close();
                _heading.text = heading;
                _heroIdentity.text = protagonist;
                _opponent.sprite = opponent;
                _opponent.enabled = opponent != null;
                _dialogue = lines;
                _onEnd = onEnd;
                _root.SetActive(true);
                _readThrough = 0;
                BindLine(0);
                Focus(_next);
            }

            internal void Close()
            {
                _root.SetActive(false);
                _logRoot.SetActive(false);
                _picker.Hide();
                _pickerRoot.SetActive(false);
                _choiceRoot.SetActive(false);
                _supports.Root.SetActive(false);
                _scene.interactable = _scene.blocksRaycasts = true;
                _dialogue = null;
                _onEnd = null;
                _choices = null;
                _summary = null;
                _branch = null;
                _waitingBranch = false;
                _popupReturn = null;
                _finished = false;
                _autoPlay = false;
                _auto.GetComponentInChildren<Text>().text = "오토 꺼짐";
                _auto.interactable = _skip.interactable = _next.interactable = true;
                _advanceSurface.interactable = true;
            }

            internal void Update(float delta)
            {
                if (_dialogue == null || !_root.activeInHierarchy) return;
                AnimatePortrait(_hero, _heroSpeaking, delta);
                AnimatePortrait(_opponent, _opponentSpeaking, delta);
                if (_finished || _waitingBranch || _logRoot.activeSelf || _pickerRoot.activeSelf || _supports.Root.activeSelf) return;
                if (_visible < _characters.Length)
                {
                    _typed += delta * CharactersPerSecond;
                    Reveal(Mathf.Min(_characters.Length, (int)_typed));
                }
                else if (_autoPlay)
                {
                    _readTime += delta;
                    if (_readTime >= Mathf.Max(1.05f, _characters.Length * .037f)) Advance();
                }
            }

            internal void ShowChoices(Func<string> summary, params StoryChoice[] choices)
            {
                _choices = choices;
                _summary = summary;
                _choiceRoot.SetActive(true);
                RefreshChoices();
                UiEntranceMotion.Play(_choiceRoot, .78f, .18f);
            }

            internal void RefreshChoices()
            {
                if (_choices == null) return;
                _choiceSummary.text = _summary?.Invoke() ?? "";
                for (int i = 0; i < _choiceButtons.Length; i++)
                {
                    var button = _choiceButtons[i];
                    button.gameObject.SetActive(i < _choices.Length);
                    if (i >= _choices.Length) continue;
                    var row = (RectTransform)button.transform.parent;
                    float step = .70f / Mathf.Max(3, _choices.Length);
                    float top = .75f - i * step;
                    row.anchorMin = new Vector2(.075f, top - step + .015f);
                    row.anchorMax = new Vector2(.925f, top);
                    button.GetComponentInChildren<Text>().text = _choices[i].Label;
                    button.interactable = _choices[i].Available?.Invoke() != false;
                }
                Focus(_choiceButtons.First(button => button.gameObject.activeSelf && button.interactable));
            }

            internal void OpenDeckPicker(IReadOnlyList<UserDeck> decks, int selected, Action<int> apply, Action edit,
                string preferredId)
            {
                _confirmDeck = apply;
                _editDeck = edit;
                _picker.ClearOptions();
                _picker.AddOptions(decks.Count == 0 ? new List<string> { "사용 가능한 덱 없음" } : decks.Select(deck => deck.name).ToList());
                _picker.SetValueWithoutNotify(decks.Count == 0 ? 0 : Mathf.Clamp(selected, 0, decks.Count - 1));
                _picker.RefreshShownValue();
                _pickerStatus.text = decks.Count == 0 ? DuelDeckAvailability.ExplainEmptyList(preferredId) : "";
                _picker.interactable = _pickerApply.interactable = decks.Count > 0;
                OpenPopup(_pickerRoot, decks.Count == 0 ? _pickerEdit : _picker);
            }

            private void ApplyDeck()
            {
                if (!_pickerApply.interactable) return;
                int selection = _picker.value;
                var apply = _confirmDeck;
                CloseDeckPicker();
                apply?.Invoke(selection);
            }

            internal void CloseDeckPicker()
            {
                _picker.Hide();
                if (_pickerRoot.activeSelf) HidePopup(_pickerRoot);
                _confirmDeck = null;
                _editDeck = null;
            }

            internal bool ClosePopup()
            {
                if (_logRoot.activeSelf) HidePopup(_logRoot);
                else if (_pickerRoot.activeSelf) CloseDeckPicker();
                else if (_supports.Root.activeSelf) CloseSupportPicker();
                else return false;
                return true;
            }

            private void BindLine(int index)
            {
                _index = index;
                _readThrough = Math.Max(_readThrough, index);
                var line = _dialogue[index];
                _speaker.text = line.speaker;
                _heroSpeaking = line.speaker.StartsWith("주인공", StringComparison.Ordinal) ||
                    line.speaker.StartsWith("여주인공", StringComparison.Ordinal);
                _opponentSpeaking = !_heroSpeaking && line.speaker != "내레이션" && line.speaker != "나레이션";
                _speaker.color = _heroSpeaking ? HeroColor : _opponentSpeaking ? OpponentColor : NarratorColor;
                _characters = StringInfo.ParseCombiningCharacters(line.text);
                _text.text = "";
                _counter.text = $"{index + 1} / {_dialogue.Length}";
                _typed = _readTime = 0f;
                _visible = 0;
                _previous.interactable = index > 0;
            }

            private void Reveal(int count)
            {
                if (_visible == count) return;
                _visible = count;
                string text = _dialogue[_index].text;
                _text.text = count == _characters.Length ? text : text.Substring(0, _characters[count]);
            }

            private void Advance()
            {
                if (_finished || _waitingBranch || _dialogue == null) return;
                if (_visible < _characters.Length) Reveal(_characters.Length);
                else if (_branch != null && _index == _branchLine) OfferBranch();
                else if (_index + 1 == _dialogue.Length) Finish();
                else BindLine(_index + 1);
            }

            private void Previous()
            {
                if (_index <= 0 || _finished) return;
                BindLine(_index - 1);
                Reveal(_characters.Length);
            }

            private void ToggleAuto()
            {
                _autoPlay = !_autoPlay;
                _auto.GetComponentInChildren<Text>().text = _autoPlay ? "오토 켜짐" : "오토 꺼짐";
            }

            private void Finish()
            {
                if (_finished || _waitingBranch || _dialogue == null) return;
                if (_branch != null)
                {
                    OfferBranch();
                    return;
                }
                BindLine(_dialogue.Length - 1);
                Reveal(_characters.Length);
                _finished = true;
                _advanceSurface.interactable = false;
                _previous.interactable = _auto.interactable = _next.interactable = _skip.interactable = false;
                _onEnd?.Invoke();
                _onEnd = null;
            }

            internal void SetBranch(int line, Action present)
            {
                _branchLine = line;
                _branch = present;
            }

            private void OfferBranch()
            {
                BindLine(_branchLine);
                Reveal(_characters.Length);
                _waitingBranch = true;
                _advanceSurface.interactable = false;
                _previous.interactable = _auto.interactable = _next.interactable = _skip.interactable = false;
                _branch.Invoke();
            }

            internal void AnswerBranch(string reply, string opponent, string response)
            {
                if (!_waitingBranch) return;
                _dialogue = _dialogue.Take(_branchLine + 1).Concat(new[]
                {
                    new StoryDialogueLine { speaker = "주인공", text = reply },
                    new StoryDialogueLine { speaker = opponent, text = response },
                }).Concat(_dialogue.Skip(_branchLine + 1)).ToArray();
                _branch = null;
                _waitingBranch = false;
                _choiceRoot.SetActive(false);
                _choices = null;
                _summary = null;
                _auto.interactable = _next.interactable = _skip.interactable = _advanceSurface.interactable = true;
                BindLine(_branchLine + 1);
                Focus(_next);
            }

            internal void OpenSupportPicker(bool raid, RaidSupport selection, Action<RaidSupport> save)
            {
                _supports.Prepare(raid, selection, save);
                OpenPopup(_supports.Root, _supports.DefaultFocus);
            }

            private void CloseSupportPicker()
            {
                HidePopup(_supports.Root);
                RefreshChoices();
            }

            private void OpenLog()
            {
                var history = new StringBuilder();
                for (int i = 0; i <= _readThrough; i++)
                {
                    var line = _dialogue[i];
                    history.AppendLine(line.speaker);
                    history.AppendLine(i == _index && !_finished ? _text.text : line.text);
                    history.AppendLine();
                }
                _logText.text = history.ToString();
                OpenPopup(_logRoot, _logClose);
                Canvas.ForceUpdateCanvases();
                _logScroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    Mathf.Max(_logScroll.viewport.rect.height, _logText.preferredHeight + 20f));
                Canvas.ForceUpdateCanvases();
                _logScroll.verticalNormalizedPosition = 0f;
            }

            private void ScrollLog(float amount)
            {
                _logScroll.StopMovement();
                _logScroll.verticalNormalizedPosition = Mathf.Clamp01(_logScroll.verticalNormalizedPosition + amount);
            }

            private void OpenPopup(GameObject popup, Selectable focus)
            {
                _popupReturn = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject?.GetComponent<Selectable>();
                _scene.interactable = _scene.blocksRaycasts = false;
                _advanceSurface.interactable = false;
                popup.SetActive(true);
                DeckPadUi.LimitKeyboardNavigation(popup.transform);
                Focus(focus);
            }

            private void HidePopup(GameObject popup)
            {
                popup.SetActive(false);
                _scene.interactable = _scene.blocksRaycasts = true;
                _advanceSurface.interactable = !_finished && !_waitingBranch;
                if (_popupReturn != null && _popupReturn.IsInteractable()) Focus(_popupReturn);
                _popupReturn = null;
            }

            private static void AnimatePortrait(Image image, bool speaking, float delta)
            {
                float mix = 1f - Mathf.Exp(-delta * 12f);
                image.color = Color.Lerp(image.color, speaking ? Color.white : Listening, mix);
                image.rectTransform.localScale = Vector3.Lerp(image.rectTransform.localScale, Vector3.one * (speaking ? 1.05f : 1f), mix);
            }

            private static (GameObject Root, RectTransform Panel) BuildPopup(Transform parent, string name,
                float left, float bottom, float right, float top)
            {
                var overlay = LotdUiKit.FillRect(name, parent);
                LotdUiKit.AddImage(overlay.gameObject, LotdUiKit.WhiteSprite, new Color(.008f, .018f, .047f, .88f));
                var panel = LotdUiKit.CreateMenuPanel(overlay, "Window", new Vector2(left, bottom), new Vector2(right, top));
                return (overlay.gameObject, panel);
            }
        }
    }
}
