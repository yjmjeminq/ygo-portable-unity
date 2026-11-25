using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
            private static readonly Color Listening = new(.48f, .53f, .63f);
            private const float CharactersPerSecond = 34f;
            private static Sprite _cheekGlow;

            private readonly GameObject _root;
            private readonly CanvasGroup _scene;
            private readonly Button _advanceSurface;
            private readonly Text _heading;
            private readonly Text _heroIdentity;
            private readonly Image _hero;
            private readonly Image _opponent;
            private readonly Image _heroSd;
            private readonly Image _opponentSd;
            private readonly Image _stage;
            private readonly CanvasGroup _portraitGroup;
            private readonly CanvasGroup _cutsceneOverlay;
            private readonly Image _cutsceneImage;
            private readonly AspectRatioFitter _cutsceneAspect;
            private readonly Image _blush;
            private readonly Text[] _hearts = new Text[3];
            private readonly StoryDialogueFx _fx;
            private readonly GameObject _speakerBadge;
            private readonly Dictionary<string, Sprite> _rpgSprites = new(StringComparer.Ordinal);
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
            private readonly Button[] _choiceButtons = new Button[6];
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
            private bool _rpg;
            private bool _showCutscene;
            private bool _akiBlush;
            private OpponentCharacter _sceneOpponent;
            private string _protagonist;
            private string _emotion;
            private float _poseTime;
            private Selectable _popupReturn;
            private int _branchLine;
            private Action _branch;
            private bool _waitingBranch;

            internal bool IsOpen => _root.activeSelf;
            internal GameObject ActiveRoot => _logRoot.activeSelf ? _logRoot : _pickerRoot.activeSelf ? _pickerRoot :
                _supports.Root.activeSelf ? _supports.Root : _root;

            internal StoryDialogueView(Transform parent)
            {
                var overlay = DuelUiKit.FillRect("StoryDialogue", parent);
                _root = overlay.gameObject;
                var back = DuelUiKit.AddImage(_root, DuelUiKit.WhiteSprite, new Color(.014f, .026f, .058f, .95f));
                var surface = DuelUiKit.FillRect("AdvanceSurface", overlay);
                var pointer = DuelUiKit.AddImage(surface.gameObject, DuelUiKit.WhiteSprite, Color.clear);
                _advanceSurface = surface.gameObject.AddComponent<Button>();
                _advanceSurface.targetGraphic = pointer;
                _advanceSurface.transition = Selectable.Transition.None;
                _advanceSurface.navigation = new Navigation { mode = Navigation.Mode.None };
                _advanceSurface.onClick.AddListener(Advance);
                back.raycastTarget = true;

                var scene = DuelUiKit.FillRect("Scene", overlay);
                _stage = DuelUiKit.AddImage(DuelUiKit.FillRect("RpgStage", scene).gameObject, null,
                    new Color(.60f, .67f, .77f), false, false);
                _heroSd = DuelUiKit.AddImage(Region(scene, "ProtagonistSD", .31f, .33f, .485f, .61f).gameObject,
                    null, Color.white, true, false);
                _opponentSd = DuelUiKit.AddImage(Region(scene, "OpponentSD", .515f, .33f, .69f, .61f).gameObject,
                    null, Color.white, true, false);
                _opponentSd.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
                var cutscene = DuelUiKit.FillRect("CutsceneOverlay", scene);
                cutscene.gameObject.AddComponent<RectMask2D>();
                _cutsceneOverlay = cutscene.gameObject.AddComponent<CanvasGroup>();
                _cutsceneOverlay.blocksRaycasts = _cutsceneOverlay.interactable = false;
                _cutsceneImage = DuelUiKit.AddImage(DuelUiKit.FillRect("CG", cutscene).gameObject, null, Color.white, false, false);
                _cutsceneAspect = _cutsceneImage.gameObject.AddComponent<AspectRatioFitter>();
                _cutsceneAspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;

                var content = Application.isMobilePlatform ? MobileSafeArea.Create(scene) : scene;
                _scene = content.gameObject.AddComponent<CanvasGroup>();
                _heading = AddLabel(content, "Episode", .06f, .88f, .94f, .965f, 29, color: DuelUiKit.GoldAccent);
                _heroIdentity = AddLabel(content, "ProtagonistIdentity", .065f, .837f, .495f, .884f, 17, color: HeroColor);
                var portraits = DuelUiKit.FillRect("Portraits", content);
                _portraitGroup = portraits.gameObject.AddComponent<CanvasGroup>();
                _portraitGroup.blocksRaycasts = _portraitGroup.interactable = false;
                var hero = Region(portraits, "Protagonist", .043f, .30f, .41f, .855f);
                var opponent = Region(portraits, "Opponent", .59f, .30f, .957f, .855f);
                _hero = DuelUiKit.AddImage(hero.gameObject, DuelProfileArt.Player, Color.white, true, false);
                _opponent = DuelUiKit.AddImage(opponent.gameObject, null, Listening, true, false);
                _blush = DuelUiKit.AddImage(Region(opponent, "Blush", .40f, .72f, .60f, .80f).gameObject,
                    null, new Color(1f, .36f, .60f, .28f), false, false);
                for (int i = 0; i < _hearts.Length; i++)
                {
                    _hearts[i] = AddLabel(opponent, $"Heart_{i}", .59f + i * .063f, .70f, .73f + i * .063f, .83f,
                        25 - i * 3, color: new Color(1f, .46f, .69f));
                    _hearts[i].text = "♥";
                    _hearts[i].raycastTarget = false;
                }

                var dialogue = DuelUiKit.CreateMenuPanel(content, "DialogueBox",
                    new Vector2(.054f, .064f), new Vector2(.946f, .406f));
                var dialogueHit = dialogue.gameObject.AddComponent<Button>();
                dialogueHit.transition = Selectable.Transition.None;
                dialogueHit.navigation = new Navigation { mode = Navigation.Mode.None };
                dialogueHit.onClick.AddListener(Advance);
                var speakerPlate = Region(dialogue, "SpeakerBadge", .038f, .703f, .35f, .95f);
                _speakerBadge = speakerPlate.gameObject;
                DuelUiKit.AddImage(speakerPlate.gameObject, DuelUiKit.WhiteSprite, new Color(.028f, .063f, .13f, .94f), false, false);
                _speaker = AddLabel(speakerPlate, "Speaker", .08f, .05f, .94f, .95f, 23, TextAnchor.MiddleLeft);
                _text = AddLabel(dialogue, "Text", .047f, .234f, .947f, .69f, 29, TextAnchor.UpperLeft);
                _text.supportRichText = false;
                _counter = AddLabel(dialogue, "LineCount", .048f, .048f, .26f, .208f, 16, TextAnchor.MiddleLeft);
                _previous = AddButton(dialogue, "Previous", "◀ 이전 대사", .36f, .727f, .50f, .927f, Previous);
                _logButton = AddButton(dialogue, "Log", "대화 로그", .51f, .727f, .647f, .927f, OpenLog);
                _auto = AddButton(dialogue, "Auto", "오토 꺼짐", .657f, .727f, .787f, .927f, ToggleAuto);
                _skip = AddButton(dialogue, "Skip", "건너뛰기", .797f, .727f, .952f, .927f, Finish);
                _next = AddButton(dialogue, "Next", "다음 ▶", .778f, .042f, .952f, .214f, Advance, gold: true);
                var flash = DuelUiKit.AddImage(DuelUiKit.FillRect("DialogueFlash", scene).gameObject, DuelUiKit.WhiteSprite,
                    Color.clear, false, false);
                _fx = scene.gameObject.AddComponent<StoryDialogueFx>();
                _fx.Initialize(dialogue, flash);

                var choices = DuelUiKit.CreateMenuPanel(content, "Choices", new Vector2(.261f, .405f), new Vector2(.739f, .869f));
                _choiceRoot = choices.gameObject;
                _choiceSummary = AddLabel(choices, "Summary", .065f, .762f, .935f, .966f, 21, color: DuelUiKit.GoldAccent);
                for (int i = 0; i < _choiceButtons.Length; i++)
                {
                    int index = i;
                    float top = .75f - i * .14f;
                    _choiceButtons[i] = AddButton(choices, $"Choice_{i}", "", .075f, top - .122f, .925f, top,
                        () => _choices[index].Select(), gold: i == 0);
                }

                var log = BuildPopup(overlay, "Backlog", .12f, .102f, .88f, .901f);
                _logRoot = log.Root;
                AddLabel(log.Panel, "Title", .075f, .85f, .927f, .954f, 26, color: DuelUiKit.GoldAccent).text = "대화 로그";
                var scrollHost = Region(log.Panel, "Scroll", .068f, .19f, .922f, .825f);
                _logScroll = scrollHost.gameObject.AddComponent<ScrollRect>();
                _logScroll.horizontal = false;
                _logScroll.movementType = ScrollRect.MovementType.Clamped;
                _logScroll.scrollSensitivity = 38f;
                var viewport = DuelUiKit.FillRect("Viewport", scrollHost);
                DuelUiKit.AddImage(viewport.gameObject, DuelUiKit.WhiteSprite, new Color(.009f, .023f, .048f, .92f));
                viewport.gameObject.AddComponent<RectMask2D>();
                var history = DuelUiKit.CreateRect("History", viewport, new Vector2(0f, 1f), Vector2.one,
                    new Vector2(14f, -4f), new Vector2(-20f, -4f));
                history.pivot = new Vector2(.5f, 1f);
                _logText = DuelUiKit.AddFittedText(history, "Text", "", 23, 23, TextAnchor.UpperLeft, DuelUiKit.TextPrimary);
                _logText.resizeTextForBestFit = false;
                _logText.supportRichText = false;
                _logText.verticalOverflow = VerticalWrapMode.Overflow;
                _logScroll.viewport = viewport;
                _logScroll.content = history;
                _logScroll.verticalScrollbar = DuelUiKit.AddVerticalScrollbar(scrollHost);
                AddButton(log.Panel, "LogUp", "위로", .075f, .065f, .225f, .163f, () => ScrollLog(.23f));
                AddButton(log.Panel, "LogDown", "아래로", .235f, .065f, .385f, .163f, () => ScrollLog(-.23f));
                _logClose = AddButton(log.Panel, "LogClose", "닫기", .735f, .065f, .925f, .163f, () => ClosePopup(), gold: true);

                var picker = BuildPopup(overlay, "DeckSelection", .219f, .212f, .781f, .789f);
                _pickerRoot = picker.Root;
                AddLabel(picker.Panel, "Title", .09f, .755f, .91f, .943f, 28, color: DuelUiKit.GoldAccent).text = "내 덱 변경";
                var dropdown = Region(picker.Panel, "Decks", .092f, .526f, .908f, .704f);
                _picker = DuelUiKit.CreateDropdown(dropdown, "StoryDeck", Array.Empty<string>(), _ => { }, dynamicChoices: true);
                _pickerStatus = AddLabel(picker.Panel, "DeckStatus", .092f, .469f, .908f, .532f, 18, color: DuelUiKit.TextMuted);
                _pickerApply = AddButton(picker.Panel, "Apply", "선택한 덱 적용", .517f, .315f, .908f, .472f, ApplyDeck, gold: true);
                _pickerEdit = AddButton(picker.Panel, "Edit", "덱 편집 열기", .092f, .315f, .483f, .472f, () => _editDeck?.Invoke());
                AddButton(picker.Panel, "Back", "돌아가기", .31f, .098f, .69f, .246f, CloseDeckPicker);
                _supports = new RaidSupportPanel(overlay, CloseSupportPicker);
                Close();
            }

            internal void Open(string heading, string protagonist, OpponentCharacter opponent, StoryDialogueLine[] lines,
                Action onEnd, bool rpg = false)
            {
                Close();
                _heading.text = heading;
                _heroIdentity.text = protagonist;
                _protagonist = protagonist;
                _sceneOpponent = opponent;
                _rpg = rpg;
                _hero.rectTransform.anchorMin = rpg ? new Vector2(.043f, .30f) : new Vector2(.07f, .26f);
                _hero.rectTransform.anchorMax = rpg ? new Vector2(.41f, .855f) : new Vector2(.493f, .855f);
                _opponent.rectTransform.anchorMin = rpg ? new Vector2(.59f, .30f) : new Vector2(.508f, .26f);
                _opponent.rectTransform.anchorMax = rpg ? new Vector2(.957f, .855f) : new Vector2(.93f, .855f);
                SetSprite(_stage, rpg ? LoadRpgSprite("Maps/spot_daedalus_bridge.png") : null);
                SetSprite(_heroSd, rpg ? LoadRpgSprite("Characters_SD/sd_protagonist.png") : null);
                SetSprite(_hero, rpg ? LoadHeroBust(null) : DuelProfileArt.Player);
                SetOpponent(opponent, null);
                _dialogue = lines;
                _onEnd = onEnd;
                _root.SetActive(true);
                _readThrough = 0;
                BindLine(0);
                Focus(_next);
            }

            internal void Open(string heading, string protagonist, Sprite opponent, StoryDialogueLine[] lines, Action onEnd)
            {
                Open(heading, protagonist, (OpponentCharacter)null, lines, onEnd);
                SetSprite(_opponent, opponent);
            }

            internal void Close()
            {
                _fx.ResetEffect();
                _showCutscene = false;
                _cutsceneOverlay.alpha = 0f;
                _portraitGroup.alpha = 1f;
                SetSprite(_cutsceneImage, null);
                SetSprite(_stage, null);
                SetSprite(_heroSd, null);
                SetSprite(_opponentSd, null);
                SetSprite(_hero, null);
                SetSprite(_opponent, null);
                foreach (var sprite in _rpgSprites.Values)
                {
                    if (sprite == null) continue;
                    var texture = sprite.texture;
                    DestroyArtwork(sprite);
                    DestroyArtwork(texture);
                }
                _rpgSprites.Clear();
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
                _poseTime += delta;
                _cutsceneOverlay.alpha = Mathf.MoveTowards(_cutsceneOverlay.alpha, _showCutscene ? 1f : 0f, delta / .4f);
                _portraitGroup.alpha = 1f - _cutsceneOverlay.alpha;
                _heading.canvasRenderer.SetAlpha(_portraitGroup.alpha);
                _heroIdentity.canvasRenderer.SetAlpha(_portraitGroup.alpha);
                AnimatePortrait(_hero, _heroSpeaking, delta, _heroSpeaking ? _emotion : null, _poseTime);
                AnimatePortrait(_opponent, _opponentSpeaking, delta, _opponentSpeaking ? _emotion : null, _poseTime);
                AnimateHearts();
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
                bool narration = IsNarration(line.speaker);
                _speakerBadge.SetActive(!narration);
                _speaker.text = narration ? "" : line.speaker;
                _heroSpeaking = !narration && (line.speaker == _protagonist ||
                    line.speaker.StartsWith("주인공", StringComparison.Ordinal) ||
                    line.speaker.StartsWith("여주인공", StringComparison.Ordinal));
                var speaker = !narration && !_heroSpeaking && _rpg
                    ? FindSpeaker(line.speaker) : null;
                _opponentSpeaking = !narration && !_heroSpeaking && (!_rpg || speaker != null);
                _speaker.color = _heroSpeaking ? HeroColor : OpponentColor;
                _emotion = _rpg ? line.emotion : null;
                _poseTime = 0f;
                _akiBlush = _rpg && speaker?.id == 1 && _emotion == "blush";
                if (_rpg)
                {
                    if (_heroSpeaking) SetSprite(_hero, LoadHeroBust(_emotion));
                    if (speaker != null) SetOpponent(speaker, _emotion);
                }
                AnimateHearts();
                BindCutscene(_rpg ? line.cutscene : null);
                _fx.Play(_rpg ? line.fx : null);
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
                    new StoryDialogueLine { speaker = _rpg ? _protagonist : "주인공", text = reply },
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
                    if (!IsNarration(line.speaker)) history.AppendLine(line.speaker);
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

            private static bool IsNarration(string speaker) =>
                string.IsNullOrWhiteSpace(speaker) || speaker is "설명" or "내레이션" or "나레이션";

            private OpponentCharacter FindSpeaker(string name)
            {
                if (_sceneOpponent?.name == name) return _sceneOpponent;
                return DeckRepository.Characters.FirstOrDefault(character => character.name == name) ??
                    DeckRepository.Characters.FirstOrDefault(character => character.aliases?.Contains(name) == true);
            }

            private void SetOpponent(OpponentCharacter character, string emotion)
            {
                string bust = character == null ? null : $"Characters_Standing/full_{character.id}";
                SetSprite(_opponent, _rpg && bust != null
                    ? LoadBust(bust, emotion) ?? DuelProfileArt.Opponent(character)
                    : DuelProfileArt.Opponent(character));
                SetSprite(_opponentSd, _rpg && character != null ? LoadRpgSprite($"Characters_SD/sd_{character.id}.png") : null);
            }

            private Sprite LoadHeroBust(string emotion) =>
                LoadBust("Characters_Standing/full_protagonist", emotion) ?? DuelProfileArt.Player;

            private Sprite LoadBust(string stem, string emotion)
            {
                if (emotion is "smile" or "blush" or "sad" or "determined" or "shock" or "hurt")
                {
                    var pose = LoadRpgSprite($"{stem}_{emotion}.png", waistUp: true);
                    if (pose != null) return pose;
                }
                return LoadRpgSprite(stem + ".png", waistUp: true);
            }

            private void BindCutscene(string id)
            {
                _showCutscene = false;
                if (string.IsNullOrEmpty(id) || id.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-')) return;
                var picture = LoadRpgSprite($"Cutscenes/{id}.jpg");
                if (picture == null) return;
                if (_cutsceneImage.sprite != picture)
                {
                    _cutsceneOverlay.alpha = 0f;
                    SetSprite(_cutsceneImage, picture);
                    _cutsceneAspect.aspectRatio = picture.rect.width / picture.rect.height;
                }
                _showCutscene = true;
            }

            private Sprite LoadRpgSprite(string name, bool waistUp = false)
            {
                string relative = "TagForce/5DS/" + name;
                string path = Path.Combine(Application.streamingAssetsPath, relative);
                if (_rpgSprites.TryGetValue(path, out var loaded)) return loaded;

                Sprite sprite = null;
                Texture2D texture = null;
                try
                {
                    if (File.Exists(path))
                    {
                        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (texture.LoadImage(File.ReadAllBytes(path), markNonReadable: true))
                        {
                            float bottom = waistUp ? Mathf.Floor(texture.height * .43f) : 0f;
                            sprite = Sprite.Create(texture, new Rect(0f, bottom, texture.width, texture.height - bottom),
                                new Vector2(.5f, .5f));
                        }
                    }
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
                {
                    Debug.LogWarning($"[StoryMode] Could not load story illustration {name}: {error.Message}");
                }
                finally { if (sprite == null && texture != null) DestroyArtwork(texture); }
                _rpgSprites.Add(path, sprite);
                return sprite;
            }

            private static void SetSprite(Image image, Sprite sprite)
            {
                image.sprite = sprite;
                image.enabled = sprite != null;
            }

            private static void DestroyArtwork(UnityEngine.Object asset)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(asset);
                else UnityEngine.Object.DestroyImmediate(asset);
            }

            private void AnimateHearts()
            {
                _blush.enabled = _akiBlush;
                if (_akiBlush && _blush.sprite == null)
                {
                    if (_cheekGlow == null)
                    {
                        const int size = 32;
                        var pixels = new Color32[size * size];
                        for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            float a = 1f - new Vector2((x + .5f) / size * 2f - 1f, (y + .5f) / size * 2f - 1f).magnitude;
                            pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Max(0f, a));
                        }
                        _cheekGlow = DuelUiChrome.CreateSprite("Story blush", size, size, pixels);
                    }
                    _blush.sprite = _cheekGlow;
                }
                for (int i = 0; i < _hearts.Length; i++)
                {
                    float life = Mathf.Clamp01((_poseTime - i * .13f) / .9f);
                    var heart = _hearts[i];
                    heart.enabled = _akiBlush && life is > 0f and < 1f;
                    heart.rectTransform.anchoredPosition = new Vector2(i * 4f * life, life * 44f);
                    heart.color = new Color(1f, .46f, .69f, Mathf.Sin(Mathf.PI * life) * .85f);
                }
            }

            private static void AnimatePortrait(Image image, bool speaking, float delta, string emotion, float elapsed)
            {
                float mix = 1f - Mathf.Exp(-delta * 12f);
                Color mood = emotion switch
                {
                    "smile" => new Color(1f, .97f, .91f),
                    "blush" => new Color(1f, .91f, .94f),
                    "determined" => new Color(1f, .94f, .85f),
                    "sad" => new Color(.76f, .85f, 1f),
                    "hurt" => new Color(.88f, .74f, .77f),
                    _ => Color.white,
                };
                float settle = Mathf.Exp(-elapsed * 7f);
                var motion = emotion switch
                {
                    "smile" => new Vector2(0f, 10f * Mathf.Sin(elapsed * 6f) * settle),
                    "blush" => new Vector2(0f, -4f * Mathf.Sin(elapsed * 7f) * settle),
                    "shock" => new Vector2(0f, 14f * Mathf.Sin(elapsed * 18f) * settle),
                    "hurt" => new Vector2(11f * Mathf.Sin(elapsed * 31f) * settle, -4f * settle),
                    "sad" => new Vector2(0f, -5f),
                    "determined" => new Vector2(3f * settle, 0f),
                    _ => Vector2.zero,
                };
                image.color = Color.Lerp(image.color, speaking ? mood : Listening, mix);
                image.rectTransform.localScale = Vector3.Lerp(image.rectTransform.localScale, Vector3.one * (speaking ? 1.05f : 1f), mix);
                image.rectTransform.anchoredPosition = Vector2.Lerp(image.rectTransform.anchoredPosition, motion, mix);
            }

            private static (GameObject Root, RectTransform Panel) BuildPopup(Transform parent, string name,
                float left, float bottom, float right, float top)
            {
                var overlay = DuelUiKit.FillRect(name, parent);
                DuelUiKit.AddImage(overlay.gameObject, DuelUiKit.WhiteSprite, new Color(.008f, .018f, .047f, .88f));
                var panel = DuelUiKit.CreateMenuPanel(overlay, "Window", new Vector2(left, bottom), new Vector2(right, top));
                return (overlay.gameObject, panel);
            }
        }
    }

    internal sealed class StoryDialogueFx : MonoBehaviour
    {
        private RectTransform _dialogue;
        private Image _overlay;
        private Vector2 _restingPosition;

        internal void Initialize(RectTransform dialogue, Image overlay)
        {
            _dialogue = dialogue;
            _restingPosition = dialogue.anchoredPosition;
            _overlay = overlay;
        }

        internal void ResetEffect()
        {
            StopAllCoroutines();
            _dialogue.anchoredPosition = _restingPosition;
            _overlay.color = Color.clear;
        }

        internal void Play(string name)
        {
            ResetEffect();
            if (Application.isPlaying && name is ("shake" or "flash" or "heartbeat")) StartCoroutine(Animate(name));
        }

        private IEnumerator Animate(string name)
        {
            float duration = name == "heartbeat" ? .72f : .36f;
            for (float time = 0f; time < duration; time += Time.unscaledDeltaTime)
            {
                DrawFrame(name, time / duration);
                yield return null;
            }
            _dialogue.anchoredPosition = _restingPosition;
            _overlay.color = Color.clear;
        }

        internal void DrawFrame(string name, float progress)
        {
            if (name == "shake")
                _dialogue.anchoredPosition = _restingPosition + Vector2.right * (Mathf.Sin(progress * 31.32f) * (1f - progress) * 13f);
            else if (name == "flash")
                _overlay.color = new Color(1f, 1f, 1f, .6f * (1f - progress));
            else if (name == "heartbeat")
                _overlay.color = new Color(1f, .36f, .63f, .33f *
                    Mathf.Pow(Mathf.Max(0f, Mathf.Sin(progress * Mathf.PI * 4f)), 2f) * (1f - progress));
        }
    }
}
