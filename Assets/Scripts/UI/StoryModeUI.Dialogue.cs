using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
            private static readonly Color HeroColor = new Color32(0x78, 0xDC, 0xFF, 255);
            private static readonly Color NarrationColor = new(.92f, .93f, .96f);
            private static readonly Regex DialogueTag = new(
                @"<(?<end>/)?(?<name>b|i|color|size)(?:=[^<>\r\n]+)?>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            private static readonly Color OpponentColor = new(1f, .76f, .43f);
            private static readonly Color Listening = new(.48f, .53f, .63f);
            private const float CharactersPerSecond = 34f;

            private readonly GameObject _root;
            private readonly Image _shade;
            private readonly CanvasGroup _scene;
            private readonly Button _advanceSurface;
            private readonly Text _heading;
            private readonly Text _heroIdentity;
            private readonly Image _hero;
            private readonly Image _opponent;
            private readonly Image _stage;
            private readonly CanvasGroup _portraitGroup;
            private readonly CanvasGroup _cutsceneOverlay;
            private readonly Image _cutsceneImage, _cutscenePrevious;
            private float _cutsceneBlend;
            private readonly AspectRatioFitter _cutsceneAspect;
            private readonly StoryDialogueFx _fx;
            private readonly Image _dialoguePanel;
            private readonly Image _speakerPlate;
            private readonly Image _speakerStripe;
            private readonly Image _headingPlate;
            private readonly GameObject _speakerBadge;
            private readonly RectTransform _rpgTools;
            private readonly Dictionary<string, Sprite> _rpgSprites = new(StringComparer.Ordinal);
            private readonly Text _speaker;
            private readonly Text _text;
            private readonly Text _caret;
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
            private Action _onSkip;
            private Func<string> _summary;
            private StoryChoice[] _choices;
            private Action<int> _confirmDeck;
            private Action _editDeck;
            private int[] _characters;
            private Match[] _formatting = Array.Empty<Match>();
            private int _index;
            private int _opponentPortraitFirstLine;
            private int _readThrough;
            private int _advanceFrame = -1;
            private float _advanceTime;
            private int _visible;
            private float _typed;
            private float _readTime;
            private int _autoSpeed;
            private bool _finished;
            private bool _heroSpeaking;
            private bool _opponentSpeaking;
            private bool _rpg;
            private int _rpgEpisode, _playAsCharacterId;
            private string _rpgAssetFolder;
            private bool _showCutscene;
            private bool _speakerUsesPaintedExpression;
            private readonly Image _heroPrevious, _opponentPrevious;
            private readonly Text _heroReaction, _opponentReaction;
            private float _heroFade, _opponentFade, _heroSlide, _opponentSlide;
            private TagForceWorldUI _field;
            private bool _stageCaption;
            private Sprite _defaultRpgStage;
            private OpponentCharacter _sceneOpponent;
            private string _sceneOpponentName;
            private string _protagonist;
            private string _originalProtagonist;
            private string _emotion;
            private float _poseTime;
            private Selectable _popupReturn;
            private int _branchLine;
            private Action _branch;
            private bool _waitingBranch;
            private bool IsGxDialogue => _rpg && _rpgAssetFolder == "GX";

            internal bool IsOpen => _root.activeSelf;
            internal GameObject ActiveRoot => _logRoot.activeSelf ? _logRoot : _pickerRoot.activeSelf ? _pickerRoot :
                _supports.Root.activeSelf ? _supports.Root : _root;

            internal StoryDialogueView(Transform parent)
            {
                var overlay = DuelUiKit.FillRect("StoryDialogue", parent);
                _root = overlay.gameObject;
                _shade = DuelUiKit.AddImage(_root, DuelUiKit.WhiteSprite, new Color(.014f, .026f, .058f, .95f));
                var surface = DuelUiKit.FillRect("AdvanceSurface", overlay);
                var pointer = DuelUiKit.AddImage(surface.gameObject, DuelUiKit.WhiteSprite, Color.clear);
                _advanceSurface = surface.gameObject.AddComponent<Button>();
                _advanceSurface.targetGraphic = pointer;
                _advanceSurface.transition = Selectable.Transition.None;
                _advanceSurface.navigation = new Navigation { mode = Navigation.Mode.None };
                _advanceSurface.onClick.AddListener(Advance);
                _shade.raycastTarget = true;

                var scene = DuelUiKit.FillRect("Scene", overlay);
                _stage = DuelUiKit.AddImage(DuelUiKit.FillRect("RpgStage", scene).gameObject, null,
                    new Color(.60f, .67f, .77f), false, false);
                var cutscene = DuelUiKit.FillRect("CutsceneOverlay", scene);
                DuelUiKit.AddImage(cutscene.gameObject, DuelUiKit.WhiteSprite, new Color(.014f, .026f, .058f), false, false);
                cutscene.gameObject.AddComponent<RectMask2D>();
                _cutsceneOverlay = cutscene.gameObject.AddComponent<CanvasGroup>();
                _cutsceneOverlay.blocksRaycasts = _cutsceneOverlay.interactable = false;
                _cutscenePrevious = DuelUiKit.AddImage(DuelUiKit.FillRect("PreviousCG", cutscene).gameObject,
                    null, Color.white, true, false);
                _cutsceneImage = DuelUiKit.AddImage(DuelUiKit.FillRect("CG", cutscene).gameObject, null, Color.white, false, false);
                _cutsceneAspect = _cutsceneImage.gameObject.AddComponent<AspectRatioFitter>();
                _cutsceneAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

                var content = Application.isMobilePlatform ? MobileSafeArea.Create(scene) : scene;
                _scene = content.gameObject.AddComponent<CanvasGroup>();
                _heading = AddLabel(content, "Episode", .06f, .88f, .94f, .965f, 29, color: DuelUiKit.GoldAccent);
                _headingPlate = DuelUiKit.AddImage(_heading.transform.parent.gameObject, null, Color.white, false, false);
                _heroIdentity = AddLabel(content, "ProtagonistIdentity", .065f, .837f, .495f, .884f, 17, color: HeroColor);
                var portraits = DuelUiKit.FillRect("Portraits", content);
                _portraitGroup = portraits.gameObject.AddComponent<CanvasGroup>();
                _portraitGroup.blocksRaycasts = _portraitGroup.interactable = false;
                var hero = Region(portraits, "Protagonist", .043f, .30f, .41f, .855f);
                var opponent = Region(portraits, "Opponent", .59f, .30f, .957f, .855f);
                _hero = DuelUiKit.AddImage(hero.gameObject, DuelProfileArt.Player, Color.white, true, false);
                _opponent = DuelUiKit.AddImage(opponent.gameObject, null, Listening, true, false);
                _heroPrevious = PortraitGhost(hero, "OutgoingHero");
                _opponentPrevious = PortraitGhost(opponent, "OutgoingOpponent");
                _heroReaction = Reaction(hero);
                _opponentReaction = Reaction(opponent);

                var dialogue = DuelUiKit.CreateMenuPanel(content, "DialogueBox",
                    new Vector2(.054f, .064f), new Vector2(.946f, .406f));
                _dialoguePanel = dialogue.GetComponent<Image>();
                var dialogueHit = dialogue.gameObject.AddComponent<Button>();
                dialogueHit.transition = Selectable.Transition.None;
                dialogueHit.navigation = new Navigation { mode = Navigation.Mode.None };
                dialogueHit.onClick.AddListener(Advance);
                var speakerPlate = Region(dialogue, "SpeakerBadge", .038f, .703f, .35f, .95f);
                _speakerBadge = speakerPlate.gameObject;
                _speakerPlate = DuelUiKit.AddImage(speakerPlate.gameObject, DuelUiKit.WhiteSprite,
                    new Color(.028f, .063f, .13f, .94f), false, false);
                _speakerStripe = DuelUiKit.AddImage(Region(speakerPlate, "Signature", .07f, .24f, .091f, .81f).gameObject,
                    DuelUiKit.WhiteSprite, HeroColor, false, false);
                _speakerStripe.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -16f);
                _speaker = AddLabel(speakerPlate, "Speaker", .128f, .05f, .94f, .95f, 23, TextAnchor.MiddleLeft);
                var body = Region(dialogue, "Text", .047f, .234f, .947f, .69f);
                _text = DuelUiKit.AddFittedText(body, "Label", "", 29, 13, TextAnchor.UpperLeft, DuelUiKit.TextPrimary);
                var shadow = _text.gameObject.AddComponent<Shadow>();
                shadow.effectDistance = new Vector2(1.5f, -1.5f);
                shadow.effectColor = new Color(0f, 0f, 0f, .85f);
                _caret = AddLabel(dialogue, "AdvanceCaret", .724f, .046f, .772f, .211f, 22, color: HeroColor);
                _caret.text = "▼";
                _counter = AddLabel(dialogue, "LineCount", .048f, .048f, .26f, .208f, 16, TextAnchor.MiddleLeft);
                _previous = AddButton(dialogue, "Previous", "◀ 이전 대사", .36f, .727f, .50f, .927f, () => UseDialogueTool(Previous));
                _logButton = AddButton(dialogue, "Log", "대화 로그", .51f, .727f, .647f, .927f, () => UseDialogueTool(OpenLog));
                _auto = AddButton(dialogue, "Auto", "오토 꺼짐", .657f, .727f, .787f, .927f, () => UseDialogueTool(ToggleAuto));
                _skip = AddButton(dialogue, "Skip", "건너뛰기", .797f, .727f, .952f, .927f, () =>
                    UseDialogueTool(_onSkip ?? Finish));
                _next = AddButton(dialogue, "Next", "다음 ▶", .778f, .042f, .952f, .214f, Advance, gold: true);
                _rpgTools = Region(content, "DialogueTools", .690f, .920f, .973f, .970f);
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
                Action onEnd, bool rpg = false, TagForceWorldUI field = null, bool backdropBelow = false,
                string rpgBackdrop = "Maps/spot_daedalus_bridge.png", Action onSkip = null,
                string preferredRpgBackdrop = null, int rpgEpisode = 0,
                int playAsCharacterId = 0, bool autoPlay = false, string rpgAssetFolder = null,
                string originalProtagonist = null, string opponentName = null, bool waitForOpponentGreeting = false)
            {
                Close();
                _field = field;
                bool onField = field != null || backdropBelow;
                _heading.text = heading;
                _heroIdentity.text = protagonist;
                _heroIdentity.gameObject.SetActive(!rpg && !onField);
                _portraitGroup.gameObject.SetActive(rpg || !onField);
                _shade.color = new Color(.014f, .026f, .058f, onField ? .14f : .95f);
                float dialogueTop = rpg ? .405f : onField ? .32f : .406f;
                _dialoguePanel.rectTransform.anchorMax = new Vector2(.946f, dialogueTop);
                _protagonist = protagonist;
                _originalProtagonist = originalProtagonist;
                _sceneOpponent = opponent;
                _sceneOpponentName = opponentName;
                int greeting = waitForOpponentGreeting && opponent != null
                    ? Array.FindIndex(lines, line => !IsNarration(line.speaker) && FindSpeaker(line.speaker)?.id == opponent.id) : 0;
                _opponentPortraitFirstLine = greeting < 0 ? int.MaxValue : greeting;
                _rpg = rpg;
                _rpgEpisode = rpgEpisode;
                _rpgAssetFolder = rpgAssetFolder ?? field?.RpgAssetFolder ?? "5DS";
                if (IsGxDialogue && Application.isPlaying) _advanceFrame = Time.frameCount;
                _playAsCharacterId = playAsCharacterId;
                ApplyDialogueStyle(rpg);
                if (autoPlay) ToggleAuto();
                _hero.rectTransform.anchorMin = rpg ? new Vector2(.025f, dialogueTop - .018f) : new Vector2(.07f, .26f);
                _hero.rectTransform.anchorMax = rpg ? new Vector2(onField ? .235f : .365f, .859f) : new Vector2(.493f, .855f);
                _opponent.rectTransform.anchorMin = rpg ? new Vector2(onField ? .765f : .635f, dialogueTop - .018f) : new Vector2(.508f, .26f);
                _opponent.rectTransform.anchorMax = rpg ? new Vector2(.975f, .859f) : new Vector2(.93f, .855f);
                bool gxPortraits = rpg && _rpgAssetFolder == "GX";
                _hero.rectTransform.pivot = _opponent.rectTransform.pivot = new Vector2(.5f, gxPortraits ? 0f : .5f);
                if (gxPortraits)
                {
                    float baseline = dialogueTop - .008f;
                    _hero.rectTransform.anchorMin = new Vector2(.066f, baseline);
                    _hero.rectTransform.anchorMax = new Vector2(.314f, .853f);
                    _opponent.rectTransform.anchorMin = new Vector2(.686f, baseline);
                    _opponent.rectTransform.anchorMax = new Vector2(.934f, .853f);
                }
                if (rpg && !onField)
                {
                    var preferred = preferredRpgBackdrop == null ? null : LoadRpgSprite(preferredRpgBackdrop);
                    _defaultRpgStage = preferred ?? LoadRpgSprite(rpgBackdrop);
                    SetSprite(_stage, _defaultRpgStage);
                }
                if (!rpg && !onField)
                {
                    SetSprite(_hero, DuelProfileArt.Player);
                    SetSprite(_opponent, DuelProfileArt.Opponent(opponent));
                }
                _dialogue = lines;
                _onEnd = onEnd;
                _onSkip = onSkip;
                _root.SetActive(true);
                _readThrough = 0;
                BindLine(0);
                if (rpg) UiEntranceMotion.Play(_dialoguePanel.gameObject, .86f, .18f);
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
                _field = null;
                RefreshStageCaption();
                _fx.ShowCaret(_caret, false);
                _cutsceneOverlay.alpha = 0f;
                _portraitGroup.alpha = 1f;
                SetSprite(_cutsceneImage, null);
                SetSprite(_cutscenePrevious, null);
                _cutsceneBlend = 1f;
                _cutsceneImage.color = Color.white;
                _defaultRpgStage = null;
                SetSprite(_stage, null);
                SetSprite(_hero, null);
                SetSprite(_opponent, null);
                SetSprite(_heroPrevious, null);
                SetSprite(_opponentPrevious, null);
                _heroReaction.enabled = _opponentReaction.enabled = false;
                _heroFade = _opponentFade = _heroSlide = _opponentSlide = 0f;
                _hero.rectTransform.localScale = _opponent.rectTransform.localScale = Vector3.one;
                _hero.rectTransform.anchoredPosition = _opponent.rectTransform.anchoredPosition = Vector2.zero;
                foreach (var sprite in _rpgSprites.Values)
                {
                    if (sprite == null) continue;
                    var texture = sprite.texture;
                    DestroyArtwork(sprite);
                    DestroyArtwork(texture);
                }
                _rpgSprites.Clear();
                _root.SetActive(false);
                _dialoguePanel.gameObject.SetActive(true);
                _logRoot.SetActive(false);
                _picker.Hide();
                _pickerRoot.SetActive(false);
                _choiceRoot.SetActive(false);
                _supports.Root.SetActive(false);
                _scene.interactable = _scene.blocksRaycasts = true;
                _dialogue = null;
                _advanceFrame = -1;
                _advanceTime = float.NegativeInfinity;
                _onEnd = null;
                _onSkip = null;
                _choices = null;
                _summary = null;
                _branch = null;
                _waitingBranch = false;
                _popupReturn = null;
                _finished = false;
                _autoSpeed = 0;
                _auto.GetComponentInChildren<Text>().text = "오토 꺼짐";
                _auto.interactable = _skip.interactable = _next.interactable = true;
                _advanceSurface.interactable = true;
            }

            internal void Update(float delta)
            {
                if (_dialogue == null || !_root.activeInHierarchy) return;
                _field?.UpdateRpgDialogue(delta);
                RefreshStageCaption();
                _poseTime += delta;
                _cutsceneOverlay.alpha = Mathf.MoveTowards(_cutsceneOverlay.alpha, _showCutscene ? 1f : 0f, delta / .4f);
                if (_cutsceneBlend < 1f)
                {
                    _cutsceneBlend = Mathf.MoveTowards(_cutsceneBlend, 1f, delta / .35f);
                    _cutsceneImage.color = new Color(1f, 1f, 1f, _cutsceneBlend);
                    if (_cutsceneBlend >= 1f) SetSprite(_cutscenePrevious, null);
                }
                float sceneAlpha = 1f - _cutsceneOverlay.alpha;
                float portraitAlpha = _field?.RpgStageRunning == true ? sceneAlpha * .16f : sceneAlpha;
                _portraitGroup.alpha = Mathf.MoveTowards(_portraitGroup.alpha, portraitAlpha, delta * 8f);
                _heading.canvasRenderer.SetAlpha(sceneAlpha);
                _headingPlate.canvasRenderer.SetAlpha(sceneAlpha);
                _heroIdentity.canvasRenderer.SetAlpha(sceneAlpha);
                AnimatePortrait(_hero, _heroPrevious, _heroReaction, _heroSpeaking, delta,
                    _heroSpeaking ? _emotion : null, _poseTime, -1f, ref _heroFade, ref _heroSlide,
                    usePaintedExpression: _rpgAssetFolder == "GX");
                AnimatePortrait(_opponent, _opponentPrevious, _opponentReaction, _opponentSpeaking, delta,
                    _opponentSpeaking ? _emotion : null, _poseTime, 1f, ref _opponentFade, ref _opponentSlide,
                    usePaintedExpression: _speakerUsesPaintedExpression);
                RefreshCaret();
                if (_stageCaption || _finished || _waitingBranch || _logRoot.activeSelf ||
                    _pickerRoot.activeSelf || _supports.Root.activeSelf) return;
                if (_visible < _characters.Length)
                {
                    _typed += delta * CharactersPerSecond * Mathf.Max(1, _autoSpeed);
                    Reveal(Mathf.Min(_characters.Length, (int)_typed));
                }
                else if (_autoSpeed > 0)
                {
                    _readTime += delta * _autoSpeed;
                    if (_readTime >= Mathf.Max(1.05f, _characters.Length * .037f)) Advance();
                }
            }

            internal void FocusOnChoices()
            {
                _dialoguePanel.gameObject.SetActive(false);
                _rpgTools.gameObject.SetActive(false);
                _advanceSurface.interactable = false;
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
                if (_choiceButtons.FirstOrDefault(button => button.gameObject.activeSelf && button.interactable) is { } available)
                    Focus(available);
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

            private void BindLine(int index, bool playStageMotion = true)
            {
                bool beforeOpponentGreeting = index < _opponentPortraitFirstLine;
                if (beforeOpponentGreeting && _index >= _opponentPortraitFirstLine)
                {
                    SetSprite(_opponent, null);
                    SetSprite(_opponentPrevious, null);
                    _opponentFade = _opponentSlide = 0f;
                }
                _index = index;
                _readThrough = Math.Max(_readThrough, index);
                var line = _dialogue[index];
                bool narration = IsNarration(line.speaker);
                _speakerBadge.SetActive(!narration);
                _speaker.text = narration ? "" : line.speaker;
                bool heroPreviouslySpeaking = _heroSpeaking;
                bool opponentPreviouslySpeaking = _opponentSpeaking;
                bool originalHero = !narration && (line.speaker == _originalProtagonist ||
                    line.speaker.StartsWith("주인공", StringComparison.Ordinal) ||
                    line.speaker.StartsWith("여주인공", StringComparison.Ordinal));
                bool previousHeroSpeaking = _playAsCharacterId > 0 && originalHero;
                _heroSpeaking = !narration && (line.speaker == _protagonist ||
                    (_playAsCharacterId > 0 ? line.speaker is "아스카" or "텐죠인 아스카" : originalHero));
                var speaker = !narration && !_heroSpeaking && _rpg
                    ? FindSpeaker(line.speaker) : null;
                _opponentSpeaking = !narration && !_heroSpeaking && (!_rpg || speaker != null || previousHeroSpeaking);
                _speaker.color = _heroSpeaking ? HeroColor : _rpg ? RpgUiStyle.SpeakerColor(speaker?.name ?? line.speaker) : OpponentColor;
                _speakerPlate.color = _rpg ? Color.white : new Color(.028f, .063f, .13f, .94f);
                _speakerStripe.color = _speaker.color;
                _text.color = _rpg && narration ? NarrationColor : DuelUiKit.TextPrimary;
                _emotion = _rpg ? line.emotion : null;
                _poseTime = 0f;
                _speakerUsesPaintedExpression = speaker?.id == 1 || (_rpgAssetFolder == "GX" && speaker?.id is 107 or 119);
                _opponentReaction.enabled = false;
                _field?.SetRpgDialogueLine(_dialogue, index, playStageMotion);
                if (_rpg && _field == null && _defaultRpgStage != null)
                    SetSprite(_stage, (line.SceneMapPath is string map ? LoadRpgSprite(map) : null) ?? _defaultRpgStage);
                if (_rpg)
                {
                    if (_heroSpeaking || _hero.sprite == null)
                        ShowBust(_hero, _heroPrevious, LoadHeroBust(_heroSpeaking ? _emotion : null), ref _heroFade, ref _heroSlide);
                    if (previousHeroSpeaking)
                        ShowBust(_opponent, _opponentPrevious, LoadOriginalHeroBust(_emotion),
                            ref _opponentFade, ref _opponentSlide);
                    else if ((speaker ?? (!beforeOpponentGreeting && _opponent.sprite == null ? _sceneOpponent : null)) is { } actor)
                    {
                        var bust = LoadActorBust(actor.id, speaker != null ? _emotion : null);
                        ShowBust(_opponent, _opponentPrevious, bust ?? DuelProfileArt.Opponent(actor), ref _opponentFade, ref _opponentSlide);
                    }
                    if (_heroSpeaking && !heroPreviouslySpeaking) _heroSlide = Mathf.Max(_heroSlide, .12f);
                    if (_opponentSpeaking && !opponentPreviouslySpeaking) _opponentSlide = Mathf.Max(_opponentSlide, .12f);
                }
                BindCutscene(_rpg ? line.cutscene : null);
                _fx.Play(_rpg ? line.fx : null);
                _formatting = _rpg ? DialogueTag.Matches(line.text).Cast<Match>().ToArray() : Array.Empty<Match>();
                _characters = StringInfo.ParseCombiningCharacters(line.text);
                if (_formatting.Length > 0)
                    _characters = _characters.Where(offset =>
                        !_formatting.Any(tag => offset >= tag.Index && offset < tag.Index + tag.Length)).ToArray();
                _text.text = "";
                _fx.ShowCaret(_caret, false);
                _counter.text = $"{index + 1} / {_dialogue.Length}";
                _typed = _readTime = 0f;
                _visible = 0;
                _previous.interactable = index > 0;
                RefreshStageCaption();
            }

            private void Reveal(int count)
            {
                if (_visible == count) return;
                _visible = count;
                string text = _dialogue[_index].text;
                if (count == _characters.Length)
                {
                    _text.text = text;
                }
                else
                {
                    int end = _characters[count];
                    string prefix = text.Substring(0, end);
                    if (_formatting.Length > 0)
                    {
                        var open = new Stack<string>();
                        foreach (var tag in _formatting)
                        {
                            if (tag.Index >= end) break;
                            bool closing = tag.Groups["end"].Success;
                            string name = tag.Groups["name"].Value.ToLowerInvariant();
                            if (!closing) open.Push(name);
                            else if (open.Count > 0 && open.Peek() == name) open.Pop();
                        }
                        prefix += string.Concat(open.Select(tag => $"</{tag}>"));
                    }
                    _text.text = prefix;
                }
                RefreshCaret();
            }

            private void RefreshCaret() => _fx.ShowCaret(_caret, _rpg && _dialogue != null &&
                !_stageCaption && !_finished && !_waitingBranch && _visible == _characters.Length &&
                !_logRoot.activeSelf && !_pickerRoot.activeSelf && !_supports.Root.activeSelf);

            private void RefreshStageCaption()
            {
                bool playing = _field?.RpgStageRunning == true;
                if (_stageCaption == playing) return;
                _stageCaption = playing;
                if (!_rpg) _next.GetComponentInChildren<Text>().text = playing ? "연출 중" : "다음 ▶";
            }

            private void ApplyDialogueStyle(bool rpg)
            {
                if (rpg) RpgUiStyle.Dialogue(_dialoguePanel.rectTransform);
                else _dialoguePanel.sprite = DuelUiChrome.Panel;
                foreach (var panel in new[] { (RectTransform)_choiceRoot.transform,
                    (RectTransform)_logRoot.transform.Find("Window"), (RectTransform)_pickerRoot.transform.Find("Window") })
                {
                    if (rpg) RpgUiStyle.Panel(panel);
                    else panel.GetComponent<Image>().sprite = DuelUiChrome.Panel;
                }
                foreach (var label in new[] { _text, _logText, _speaker, _heading, _heroIdentity, _counter, _choiceSummary })
                {
                    if (rpg)
                    {
                        if (label == _speaker || label == _heading) RpgUiStyle.Display(label);
                        else RpgUiStyle.Reading(label);
                    }
                    else
                    {
                        label.font = DuelUiKit.FontKR;
                        label.fontStyle = label == _logText ? FontStyle.Normal : FontStyle.Bold;
                    }
                }
                _text.resizeTextMinSize = rpg ? 23 : 13;
                _text.lineSpacing = rpg ? 1.28f : 1f;
                _text.supportRichText = rpg;
                foreach (var button in new[] { _previous, _logButton, _auto, _skip })
                    StyleButton(button);
                StyleButton(_next, primary: true);
                LayoutDialogueControls(rpg);
                for (int i = 0; i < _choiceButtons.Length; i++)
                    StyleButton(_choiceButtons[i], primary: i == 0);
                foreach (var popup in new[] { _logRoot, _pickerRoot })
                {
                    var title = popup.transform.Find("Window/Title/Label").GetComponent<Text>();
                    if (rpg) RpgUiStyle.Display(title);
                    else
                    {
                        title.font = DuelUiKit.FontKR;
                        title.fontStyle = FontStyle.Bold;
                    }
                    foreach (var button in popup.GetComponentsInChildren<Button>(true))
                        StyleButton(button, primary: button == _logClose || button == _pickerApply);
                }

                void StyleButton(Button button, bool primary = false)
                {
                    if (rpg)
                    {
                        RpgUiStyle.Control(button, primary, animated: button != _next);
                        return;
                    }
                    if (button.TryGetComponent<MenuButtonMotion>(out var motion)) motion.enabled = false;
                    var image = (Image)button.targetGraphic;
                    image.overrideSprite = null;
                    image.canvasRenderer.SetColor(Color.white);
                    DuelUiChrome.ApplyControl(button, image, primary);
                    var label = button.GetComponentInChildren<Text>(true);
                    label.font = DuelUiKit.FontKR;
                    label.fontStyle = FontStyle.Bold;
                    label.color = DuelUiKit.TextPrimary;
                }
            }

            private void LayoutDialogueControls(bool rpg)
            {
                var panel = _dialoguePanel.rectTransform;
                _rpgTools.gameObject.SetActive(rpg);
                _counter.gameObject.SetActive(!rpg);
                _speakerStripe.enabled = rpg;
                _headingPlate.enabled = rpg;
                _heading.alignment = rpg ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                Place(_heading.rectTransform.parent, panel.parent, rpg ? new Rect(.044f, .916f, .354f, .057f) : new Rect(.06f, .88f, .88f, .085f));
                _heading.rectTransform.anchorMin = rpg ? new Vector2(.057f, .05f) : Vector2.zero;
                _heading.rectTransform.anchorMax = rpg ? new Vector2(.957f, .95f) : Vector2.one;
                Place(_speakerBadge.transform, panel, rpg ? new Rect(.032f, .897f, .235f, .221f) : new Rect(.038f, .703f, .312f, .247f));
                Place(_text.rectTransform.parent, panel, rpg ? new Rect(.041f, .126f, .906f, .705f) : new Rect(.047f, .234f, .90f, .456f));
                Place(_caret.rectTransform.parent, panel, rpg ? new Rect(.905f, .066f, .050f, .142f) : new Rect(.724f, .046f, .048f, .165f));
                if (rpg)
                {
                    RpgUiStyle.Speaker(_speakerPlate.rectTransform);
                    RpgUiStyle.Panel(_headingPlate.rectTransform, gold: true);
                }
                else
                {
                    _speakerPlate.sprite = DuelUiKit.WhiteSprite;
                    _speakerPlate.type = Image.Type.Simple;
                }
                var tools = new[] { _previous, _logButton, _auto, _skip };
                var legacy = new[] { new Rect(.36f, .727f, .14f, .20f), new Rect(.51f, .727f, .137f, .20f),
                    new Rect(.657f, .727f, .13f, .20f), new Rect(.797f, .727f, .155f, .20f) };
                var captions = new[] { rpg ? "이전" : "◀ 이전 대사", rpg ? "로그" : "대화 로그", "오토 꺼짐", rpg ? "스킵" : "건너뛰기" };
                for (int i = 0; i < tools.Length; i++)
                {
                    Place(tools[i].transform.parent, rpg ? _rpgTools : panel,
                        rpg ? new Rect(i * .253f, .04f, .235f, .92f) : legacy[i]);
                    var label = tools[i].GetComponentInChildren<Text>();
                    label.text = captions[i];
                    label.fontSize = label.resizeTextMaxSize = rpg ? 18 : 22;
                }

                Place(_next.transform.parent, panel, rpg ? new Rect(0f, 0f, 1f, 1f) : new Rect(.778f, .042f, .174f, .172f));
                var hit = (Image)_next.targetGraphic;
                if (rpg)
                {
                    hit.color = Color.clear;
                    _next.transition = Selectable.Transition.None;
                    _next.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = _logButton };
                    for (int index = 0; index < tools.Length; index++)
                    {
                        tools[index].navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = _next,
                            selectOnLeft = index == 0 ? null : tools[index - 1],
                            selectOnRight = index == tools.Length - 1 ? null : tools[index + 1] };
                    }
                }
                else
                {
                    _next.navigation = new Navigation { mode = Navigation.Mode.Automatic };
                    foreach (var tool in tools) tool.navigation = new Navigation { mode = Navigation.Mode.Automatic };
                }
                _next.GetComponentInChildren<Text>().text = rpg ? string.Empty : "다음 ▶";

                static void Place(Transform child, Transform parent, Rect area)
                {
                    var rect = (RectTransform)child;
                    if (rect.parent != parent) rect.SetParent(parent, false);
                    rect.anchorMin = area.min;
                    rect.anchorMax = area.max;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                }
            }

            private void UseDialogueTool(Action action)
            {
                if (IsGxDialogue && _choiceRoot.activeSelf) return;
                if (IsGxDialogue && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                                     Input.GetKeyDown(KeyCode.KeypadEnter)))
                {
                    Advance();
                    return;
                }
                action();
            }

            private void Advance() => AdvanceAt(Time.unscaledTime, Application.isPlaying ? Time.frameCount : -1);

            private void AdvanceAt(float now, int frame)
            {
                if (_stageCaption || _finished || _waitingBranch || _dialogue == null || ActiveRoot != _root ||
                    (IsGxDialogue && _choiceRoot.activeSelf)) return;
                if (frame >= 0)
                {
                    if (_advanceFrame == frame || (IsGxDialogue && now - _advanceTime < .12f)) return;
                    _advanceFrame = frame;
                    _advanceTime = now;
                }
                if (_visible < _characters.Length) Reveal(_characters.Length);
                else if (_branch != null && _index == _branchLine) OfferBranch();
                else if (_index + 1 == _dialogue.Length) Finish();
                else BindLine(_index + 1);
            }

            private void Previous()
            {
                if (_index <= 0 || _finished || ActiveRoot != _root) return;
                BindLine(_index - 1, playStageMotion: false);
                Reveal(_characters.Length);
            }

            private void ToggleAuto()
            {
                _autoSpeed = _autoSpeed == 0 ? 1 : _rpg && _autoSpeed == 1 ? 2 : 0;
                _auto.GetComponentInChildren<Text>().text = _autoSpeed switch
                {
                    1 => "오토 켜짐",
                    2 => "오토 2배",
                    _ => "오토 꺼짐",
                };
            }

            private void Finish()
            {
                if (_finished || _waitingBranch || _dialogue == null) return;
                if (_branch != null)
                {
                    OfferBranch();
                    return;
                }
                BindLine(_dialogue.Length - 1, playStageMotion: false);
                Reveal(_characters.Length);
                _finished = true;
                _fx.ShowCaret(_caret, false);
                _advanceSurface.interactable = false;
                _previous.interactable = _auto.interactable = _next.interactable = _skip.interactable = false;
                var onEnd = _onEnd;
                _onEnd = null;
                onEnd?.Invoke();
            }

            internal void SetBranch(int line, Action present)
            {
                _branchLine = line;
                _branch = present;
            }

            private void OfferBranch()
            {
                BindLine(_branchLine, playStageMotion: false);
                Reveal(_characters.Length);
                _waitingBranch = true;
                _fx.ShowCaret(_caret, false);
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
                    string text = i == _index && !_finished ? _text.text : line.text;
                    history.AppendLine(_rpg ? DialogueTag.Replace(text, "") : text);
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
                _fx.ShowCaret(_caret, false);
                DeckPadUi.LimitKeyboardNavigation(popup.transform);
                Focus(focus);
            }

            private void HidePopup(GameObject popup)
            {
                popup.SetActive(false);
                _scene.interactable = _scene.blocksRaycasts = true;
                _advanceSurface.interactable = !_finished && !_waitingBranch;
                if (IsGxDialogue && !_choiceRoot.activeSelf && _next.interactable) Focus(_next);
                else if (_popupReturn != null && _popupReturn.IsInteractable()) Focus(_popupReturn);
                _popupReturn = null;
                RefreshCaret();
            }

            private static bool IsNarration(string speaker) =>
                string.IsNullOrWhiteSpace(speaker) || speaker is "설명" or "내레이션" or "나레이션";

            private OpponentCharacter FindSpeaker(string name)
            {
                if (_sceneOpponent != null && (name == _sceneOpponentName || name == _sceneOpponent.name))
                    return _sceneOpponent;
                return DeckRepository.Characters.FirstOrDefault(character => character.name == name) ??
                    DeckRepository.Characters.FirstOrDefault(character => character.aliases?.Contains(name) == true);
            }

            private static Image PortraitGhost(RectTransform image, string name)
            {
                return DuelUiKit.AddImage(DuelUiKit.FillRect(name, image).gameObject, null, Color.white, true, false);
            }

            private static Text Reaction(RectTransform portrait)
            {
                var label = AddLabel(portrait, "Reaction", .65f, .74f, .97f, .95f, 32, color: Color.white);
                label.raycastTarget = label.enabled = false;
                return label;
            }

            private static void ShowBust(Image image, Image ghost, Sprite bust, ref float fade, ref float slide)
            {
                if (image.sprite == bust || bust == null) return;
                SetSprite(ghost, image.sprite);
                ghost.color = image.color;
                fade = .12f;
                if (image.sprite == null)
                {
                    slide = .24f;
                    float side = image.rectTransform.anchorMin.x < .5f ? -1f : 1f;
                    image.rectTransform.anchoredPosition = Vector2.right * (47f * side);
                }
                SetSprite(image, bust);
                image.color = new Color(1f, 1f, 1f, 0f);
            }

            private Sprite LoadHeroBust(string emotion) =>
                (_playAsCharacterId > 0 ? LoadActorBust(_playAsCharacterId, emotion) : LoadOriginalHeroBust(emotion))
                ?? DuelProfileArt.Player;

            private Sprite LoadOriginalHeroBust(string emotion) => _rpgAssetFolder == "GX"
                ? LoadGxBust("campaign_protagonist", emotion)
                : LoadBust("Characters_Standing/full_protagonist", emotion);

            private Sprite LoadActorBust(int id, string emotion)
            {
                if (_rpgAssetFolder == "GX")
                    return LoadGxBust(id.ToString(), emotion) ??
                        (id == 104 ? LoadBust("TagForce/DM/Characters_Standing/full_104", emotion) : null);
                string outfit = RpgWardrobe.ForStory(id, _rpgEpisode);
                string stem = RpgWardrobe.PortraitStem(id, outfit);
                if (!RpgWardrobe.UsesBasePortrait(outfit))
                {
                    var bust = LoadExpression(stem, emotion) ?? LoadRpgSprite(RpgWardrobe.Preview(id, outfit));
                    if (bust != null) return bust;
                    stem = RpgWardrobe.PortraitStem(id, null);
                }
                return LoadBust(stem, emotion);
            }

            private Sprite LoadGxBust(string key, string emotion)
            {
                string stem = $"DialoguePortraits/bust_{key}";
                return LoadExpression(stem, emotion) ?? LoadRpgSprite(stem + ".png") ??
                    LoadBust($"Standing/full_{key}", emotion);
            }

            private Sprite LoadBust(string stem, string emotion)
            {
                return LoadExpression(stem, emotion) ?? LoadRpgSprite(stem + ".png", waistUp: true);
            }

            private Sprite LoadExpression(string stem, string emotion) =>
                emotion is "smile" or "blush" or "sad" or "determined" or "shock" or "hurt" or "angry"
                    ? LoadRpgSprite($"{stem}_{emotion}.png") : null;

            private void BindCutscene(string id)
            {
                bool continuing = _showCutscene;
                _showCutscene = false;
                if (string.IsNullOrEmpty(id) || id.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-')) return;
                var picture = LoadRpgSprite($"Cutscenes/{id}.{(_rpgAssetFolder == "GX" ? "png" : "jpg")}");
                if (picture == null) return;
                if (_cutsceneImage.sprite != picture)
                {
                    bool gx = _rpgAssetFolder == "GX";
                    if ((continuing || gx && _cutsceneOverlay.alpha > 0f) && _cutsceneImage.sprite != null)
                    {
                        // Keep a painted layer visible when previous/next interrupts a GX fade.
                        var outgoing = gx && _cutsceneBlend < .5f && _cutscenePrevious.enabled
                            ? _cutscenePrevious.sprite : _cutsceneImage.sprite;
                        SetSprite(_cutscenePrevious, outgoing);
                        _cutsceneBlend = 0f;
                        _cutsceneImage.color = new Color(1f, 1f, 1f, 0f);
                    }
                    else
                    {
                        _cutsceneOverlay.alpha = 0f;
                        _cutsceneBlend = 1f;
                        _cutsceneImage.color = Color.white;
                        SetSprite(_cutscenePrevious, null);
                    }
                    SetSprite(_cutsceneImage, picture);
                    _cutsceneAspect.aspectRatio = picture.rect.width / picture.rect.height;
                }
                _showCutscene = true;
            }

            private Sprite LoadRpgSprite(string name, bool waistUp = false)
            {
                string path = name.StartsWith("TagForce/", StringComparison.Ordinal)
                    ? Path.Combine(Application.streamingAssetsPath, name)
                    : Path.Combine(Application.streamingAssetsPath, $"TagForce/{_rpgAssetFolder}", name);
                if (_rpgSprites.TryGetValue(path, out var loaded)) return loaded;

                Sprite sprite = null;
                Texture2D texture = null;
                try
                {
                    if (File.Exists(path))
                    {
                        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (texture.LoadImage(File.ReadAllBytes(path), markNonReadable: !waistUp))
                        {
                            var rect = new Rect(0f, 0f, texture.width, texture.height);
                            sprite = waistUp ? RpgDialoguePortrait.Crop(texture, rect, Path.GetFileNameWithoutExtension(name)) :
                                Sprite.Create(texture, rect, Vector2.one * .5f, 100f, 0, SpriteMeshType.FullRect);
                            if (waistUp && sprite.texture == texture) texture.Apply(false, true);
                        }
                    }
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
                {
                    Debug.LogWarning($"[StoryMode] Could not load story illustration {name}: {error.Message}");
                }
                finally { if (texture != null && sprite?.texture != texture) DestroyArtwork(texture); }
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

            private void AnimatePortrait(Image image, Image ghost, Text reaction, bool speaking, float delta,
                string emotion, float elapsed, float side, ref float fade, ref float slide,
                bool usePaintedExpression = false)
            {
                if (!image.enabled) return;
                float mix = 1f - Mathf.Exp(-delta * 12f);
                Color mood = emotion switch
                {
                    "smile" => new Color(1f, .97f, .91f),
                    "blush" when !usePaintedExpression => new Color(1f, .91f, .94f),
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
                    "angry" => new Vector2(-8f * side * settle, 2f * settle),
                    "determined" => new Vector2(3f * settle, 0f),
                    _ => Vector2.zero,
                };
                fade = Mathf.Max(0f, fade - delta);
                slide = Mathf.Max(0f, slide - delta);
                float outgoing = fade / .12f;
                if (ghost.enabled)
                {
                    var previousTint = ghost.color;
                    previousTint.a = outgoing;
                    ghost.color = previousTint;
                    if (fade <= 0f) SetSprite(ghost, null);
                }
                var tint = Color.Lerp(image.color, speaking ? mood : Listening, mix);
                tint.a = 1f - outgoing;
                image.color = tint;
                float scale = 1f;
                if (!_rpg || _rpgAssetFolder != "GX")
                {
                    scale = speaking ? 1.05f : _rpg ? .96f : 1f;
                    if (emotion == "determined") scale += settle * .025f;
                    if (_rpg) scale += .015f * Mathf.Sin((1f - outgoing) * Mathf.PI);
                }
                image.rectTransform.localScale = Vector3.Lerp(image.rectTransform.localScale, Vector3.one * scale, mix);
                if (_rpg)
                {
                    motion.y += 1.7f * Mathf.Sin(Time.unscaledTime * 2.2f + side);
                    float entrance = slide / .24f;
                    motion.x += side * 47f * entrance * entrance * entrance;
                }
                image.rectTransform.anchoredPosition = Vector2.Lerp(image.rectTransform.anchoredPosition, motion, mix);

                var (glyph, color) = emotion switch
                {
                    "shock" => ("!?", new Color(1f, .82f, .54f)),
                    "determined" => ("✧", new Color(1f, .72f, .31f)),
                    "smile" => ("✦", new Color(1f, .91f, .55f)),
                    "sad" => ("…", new Color(.68f, .85f, .99f)),
                    "angry" => ("!", new Color(1f, .48f, .41f)),
                    "hurt" => ("✷", new Color(1f, .43f, .45f)),
                    "blush" => ("♥", new Color(1f, .48f, .69f)),
                    _ => ("", Color.clear),
                };
                float react = Mathf.Clamp01(elapsed / .72f);
                reaction.enabled = _rpg && !usePaintedExpression && glyph.Length != 0 && react < 1f;
                if (!reaction.enabled) return;
                reaction.text = glyph;
                reaction.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Lerp(-8f, 13f, react));
                color.a = Mathf.Lerp(1f, 0f, react);
                reaction.color = color;
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
        private Coroutine _effectAnimation;
        private Coroutine _caretAnimation;

        internal void Initialize(RectTransform dialogue, Image overlay)
        {
            _dialogue = dialogue;
            _restingPosition = dialogue.anchoredPosition;
            _overlay = overlay;
        }

        internal void ResetEffect()
        {
            if (_effectAnimation != null) StopCoroutine(_effectAnimation);
            _effectAnimation = null;
            _dialogue.anchoredPosition = _restingPosition;
            _overlay.color = Color.clear;
        }

        internal void Play(string name)
        {
            ResetEffect();
            if (Application.isPlaying && name is ("shake" or "flash" or "heartbeat" or "fade"))
                _effectAnimation = StartCoroutine(Animate(name));
        }

        internal void ShowCaret(Text caret, bool visible)
        {
            if (caret.enabled == visible) return;
            caret.enabled = visible;
            if (_caretAnimation != null) StopCoroutine(_caretAnimation);
            _caretAnimation = visible && Application.isPlaying ? StartCoroutine(PulseCaret(caret)) : null;
        }

        private static IEnumerator PulseCaret(Text caret)
        {
            Color glow = caret.color;
            for (float time = 0f; caret.enabled; time += Time.unscaledDeltaTime)
            {
                glow.a = .72f + .23f * Mathf.Sin(time * 5f);
                caret.color = glow;
                yield return null;
            }
        }

        private IEnumerator Animate(string name)
        {
            float duration = name switch { "heartbeat" => .72f, "fade" => .62f, _ => .36f };
            for (float time = 0f; time < duration; time += Time.unscaledDeltaTime)
            {
                DrawFrame(name, time / duration);
                yield return null;
            }
            _dialogue.anchoredPosition = _restingPosition;
            _overlay.color = Color.clear;
            _effectAnimation = null;
        }

        internal void DrawFrame(string name, float progress)
        {
            if (name == "shake")
                _dialogue.anchoredPosition = _restingPosition + Vector2.right * (Mathf.Sin(progress * 31.32f) * (1f - progress) * 13f);
            else if (name == "flash")
                _overlay.color = new Color(1f, 1f, 1f, .6f * (1f - progress));
            else if (name == "fade")
                _overlay.color = new Color(.018f, .032f, .064f, .75f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(progress)));
            else if (name == "heartbeat")
                _overlay.color = new Color(1f, .36f, .63f, .33f *
                    Mathf.Pow(Mathf.Max(0f, Mathf.Sin(progress * Mathf.PI * 4f)), 2f) * (1f - progress));
        }
    }
}
