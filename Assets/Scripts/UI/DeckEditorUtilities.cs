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
    public partial class DeckEditorUI
    {
        private static readonly Color[] CompositionColors =
        {
            new Color(.99f, .54f, .24f),
            new Color(.19f, .83f, .73f),
            new Color(.86f, .35f, .68f),
        };

        private readonly RectTransform[] _compositionSegments = new RectTransform[3];
        private readonly Text[] _compositionCounts = new Text[3];
        private readonly CompositeCardView[] _openingHandViews = new CompositeCardView[6];
        private readonly Button[] _openingHandButtons = new Button[6];
        private readonly System.Random _openingHandRandom = new();
        private const int StarterPageSize = 6;
        private static readonly Color TurnDrawEdge = new Color(.48f, .32f, .07f);
        private readonly HashSet<uint> _markedStarters = new();
        private readonly (Button Button, CompositeCardView Art, Text Caption)[] _starterPickerSlots =
            new (Button, CompositeCardView, Text)[StarterPageSize];
        private (uint Code, int Copies)[] _starterChoices = Array.Empty<(uint, int)>();
        private UserDeck _starterDeck;
        private int _starterPage;
        private RectTransform _starterPickerOverlay;
        private Button _pickStartersButton;
        private Button _starterPickerDone;
        private Button _starterPrevious;
        private Button _starterNext;
        private Text _starterPickerPageLabel;
        private Text _openingHandOdds;
        private uint[] _openingHand = Array.Empty<uint>();
        private bool _drawGoingSecond;
        private Text _openingHandTitle;
        private Text _openingHandFirst;
        private Text _openingHandSecond;
        private Text _openingHandDrawLabel;
        private Button _openDrawButton;
        private Button _redrawButton;
        private Button _deckToolsClose;
        private Button _drawClose;
        private RectTransform _deckToolsOverlay;
        private RectTransform _deckDrawOverlay;
        private RectTransform _utilityOverlay;
        private Selectable _utilityDefault;
        private GameObject _utilityReturnFocus;
        private InputField _deckRecipeInput;
        private Text _deckRecipeStatus;

        private static RectTransform DeckArea(Transform parent, string name, float x1, float y1, float x2, float y2) =>
            LotdUiKit.CreateRect(name, parent, new Vector2(x1, y1), new Vector2(x2, y2), Vector2.zero, Vector2.zero);

        private static Button DeckButton(Transform parent, string name, string label, float x1, float y1,
            float x2, float y2, Action action, bool accent = false, int fontSize = 18) =>
            DeckEditorStyle.Button(DeckArea(parent, name, x1, y1, x2, y2), "Button", label, action, fontSize, accent);

        private void BuildComposition()
        {
            var holder = (RectTransform)_mainCountText.rectTransform.parent;
            _mainCountText.rectTransform.anchorMin = new Vector2(.55f, .22f);
            var rail = DeckArea(holder, "Composition", .0f, .03f, 1f, .21f);
            LotdUiKit.AddImage(rail.gameObject, LotdUiKit.WhiteSprite, DeckEditorStyle.Surface, false, false);
            for (int i = 0; i < _compositionSegments.Length; i++)
            {
                var segment = DeckArea(rail, $"Type_{i}", 0f, 0f, 0f, 1f);
                LotdUiKit.AddImage(segment.gameObject, LotdUiKit.WhiteSprite, CompositionColors[i], false, false);
                _compositionSegments[i] = segment;
                var value = DeckArea(holder, $"Count_{i}", i * .182f, .22f, (i + 1) * .182f, 1f);
                _compositionCounts[i] = LotdUiKit.AddFittedText(value, "Text", "", 14, 8,
                    TextAnchor.MiddleLeft, CompositionColors[i], FontStyle.Bold);
                _compositionCounts[i].raycastTarget = false;
            }
        }

        private void RefreshComposition()
        {
            int monsters = 0, spells = 0, traps = 0;
            foreach (uint code in _workingDeck.main)
            {
                var card = CardDatabase.Get(code);
                if (card?.IsMonster == true) monsters++;
                else if (card?.IsSpell == true) spells++;
                else if (card?.IsTrap == true) traps++;
            }
            int total = _workingDeck.main.Count;
            _mainCountText.text = $"{total}장 · 40–60";
            _extraCountText.text = $"{_workingDeck.extra.Count} / {OtherDeckCapacity}장 · 남은 {Mathf.Max(0, OtherDeckCapacity - _workingDeck.extra.Count)}칸";
            _sideCountText.text = $"{_workingDeck.side.Count} / {OtherDeckCapacity}장 · 남은 {Mathf.Max(0, OtherDeckCapacity - _workingDeck.side.Count)}칸";
            _compositionCounts[0].text = $"몬스터 {monsters}";
            _compositionCounts[1].text = $"마법 {spells}";
            _compositionCounts[2].text = $"함정 {traps}";
            float divisor = Mathf.Max(1, total);
            SetCompositionSegment(0, 0f, monsters / divisor);
            SetCompositionSegment(1, monsters / divisor, (monsters + spells) / divisor);
            SetCompositionSegment(2, (monsters + spells) / divisor, (monsters + spells + traps) / divisor);
            _openDrawButton.interactable = total > 0;
        }

        private void SetCompositionSegment(int segment, float start, float end)
        {
            _compositionSegments[segment].anchorMin = new Vector2(start, 0f);
            _compositionSegments[segment].anchorMax = new Vector2(end, 1f);
            _compositionSegments[segment].gameObject.SetActive(end > start);
        }

        private void BuildDeckUtilities(Transform root)
        {
            _deckToolsOverlay = BuildDeckOverlay(root, "DeckTools", out var tools);
            LotdUiKit.AddFittedText(DeckArea(tools, "Title", .06f, .847f, .73f, .955f),
                "Text", "덱 관리 · 레시피", 27, 16, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);
            _deckToolsClose = DeckButton(tools, "Close", "닫기", .79f, .847f, .945f, .948f, CloseDeckUtility);
            DeckButton(tools, "Duplicate", "덱 복제", .055f, .705f, .255f, .812f, DuplicateWorkingDeck, true);
            DeckButton(tools, "CopyYdk", "YDK 복사", .272f, .705f, .47f, .812f, () => CopyDeckRecipe(ydk: true));
            DeckButton(tools, "CopyText", "텍스트 복사", .487f, .705f, .701f, .812f, () => CopyDeckRecipe(ydk: false));
            DeckButton(tools, "Clear", "덱 비우기", .72f, .705f, .945f, .812f, ConfirmClearWorkingDeck);

            _deckRecipeInput = DeckEditorStyle.Input(
                DeckArea(tools, "Recipe", .055f, .243f, .945f, .681f), "Input",
                "YDK 또는 카드 이름과 수량을 붙여넣으세요.", 19);
            _deckRecipeInput.lineType = InputField.LineType.MultiLineNewline;
            _deckRecipeInput.characterLimit = 24000;
            _deckRecipeInput.textComponent.alignment = TextAnchor.UpperLeft;
            if (_deckRecipeInput.placeholder is Text placeholder) placeholder.alignment = TextAnchor.UpperLeft;

            _deckRecipeStatus = LotdUiKit.AddFittedText(DeckArea(tools, "Status", .055f, .151f, .945f, .225f),
                "Text", "", 17, 12, TextAnchor.MiddleLeft, LotdUiKit.TextMuted);
            _deckRecipeStatus.supportRichText = false;
            DeckButton(tools, "Paste", "클립보드 붙여넣기", .055f, .048f, .324f, .139f, PasteDeckRecipe);
            DeckButton(tools, "Shuffle", "메인 덱 섞기", .346f, .048f, .622f, .139f, () =>
            {
                CloseDeckUtility();
                ShuffleWorkingDeck();
            });
            DeckButton(tools, "Import", "새 덱으로 불러오기", .644f, .048f, .945f, .139f, ImportDeckRecipe, true);

            _deckDrawOverlay = BuildDeckOverlay(root, "TestDraw", out var draw);
            _openingHandTitle = LotdUiKit.AddFittedText(DeckArea(draw, "Title", .055f, .847f, .73f, .955f),
                "Text", "", 28, 17, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);
            _drawClose = DeckButton(draw, "Close", "닫기", .79f, .847f, .945f, .948f, CloseDeckUtility);
            _openingHandFirst = DeckButton(draw, "First", "선공", .055f, .706f, .18f, .81f,
                () => SetOpeningHandOrder(false)).GetComponentInChildren<Text>();
            _openingHandSecond = DeckButton(draw, "Second", "후공", .19f, .706f, .315f, .81f,
                () => SetOpeningHandOrder(true)).GetComponentInChildren<Text>();
            _openingHandOdds = LotdUiKit.AddFittedText(DeckArea(draw, "StarterOdds", .34f, .699f, .699f, .815f),
                "Text", "", 18, 11, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);
            _pickStartersButton = DeckButton(draw, "PickStarters", "초동 카드 선택", .713f, _mobileLayout ? .661f : .706f,
                .945f, _mobileLayout ? .846f : .81f, OpenStarterPicker, fontSize: 17);
            for (int i = 0; i < _openingHandViews.Length; i++)
            {
                int slot = i;
                float x = .051f + i * .146f + (i == 5 ? .024f : 0f);
                var cell = DeckArea(draw, $"Card_{i}", x, .218f, x + .132f, _mobileLayout ? .651f : .676f);
                var surface = LotdUiKit.AddImage(cell.gameObject, LotdUiKit.WhiteSprite,
                    i == 5 ? TurnDrawEdge : DeckEditorStyle.Surface);
                var hit = cell.gameObject.AddComponent<Button>();
                hit.targetGraphic = surface;
                hit.onClick.AddListener(() =>
                {
                    if (slot < _openingHand.Length) OpenExpandedDeckCard(_openingHand[slot]);
                });
                _openingHandButtons[i] = hit;
                _openingHandViews[i] = CompositeCardView.Create(LotdUiKit.FillRect("CardInset", cell, 5f), "Card");
            }
            _openingHandDrawLabel = LotdUiKit.AddFittedText(DeckArea(draw, "TurnDraw", .798f, .158f, .946f, .209f),
                "Text", "후공 첫 턴 드로우", 18, 12, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);
            _redrawButton = DeckButton(draw, "Redraw", "다시 뽑기", .366f, .049f, .635f, .172f,
                RedrawOpeningHand, true, 21);
            BuildStarterPicker(root);
        }

        private RectTransform BuildDeckOverlay(Transform root, string name, out RectTransform panel)
        {
            var dim = LotdUiKit.FillRect(name, root);
            DeckEditorStyle.Fill(dim, DeckEditorStyle.Scrim);
            panel = DeckEditorStyle.Panel(dim, "Window", _mobileLayout ? new Vector2(.048f, .048f) : new Vector2(.105f, .123f),
                _mobileLayout ? new Vector2(.952f, .952f) : new Vector2(.895f, .877f));
            dim.gameObject.SetActive(false);
            return dim;
        }

        private void OpenDeckTools()
        {
            _deckRecipeInput.SetTextWithoutNotify("");
            _deckRecipeStatus.text = "";
            OpenDeckUtility(_deckToolsOverlay, _deckToolsClose);
        }

        private void OpenDeckUtility(RectTransform overlay, Selectable first)
        {
            if (_utilityOverlay != null) DismissDeckUtility(restoreFocus: true);
            CancelDeckDrag();
            EndDeckTransfer();
            _utilityReturnFocus = EventSystem.current?.currentSelectedGameObject;
            ShowDeckUtility(overlay, first);
        }

        private void ShowDeckUtility(RectTransform overlay, Selectable first)
        {
            if (_utilityOverlay != null) _utilityOverlay.gameObject.SetActive(false);
            _padUi.Clear();
            _padCardFocus.Hide();
            _utilityOverlay = overlay;
            _utilityDefault = first;
            overlay.SetAsLastSibling();
            overlay.gameObject.SetActive(true);
            DeckPadUi.LimitKeyboardNavigation(overlay);
            EventSystem.current?.SetSelectedGameObject(first.gameObject);
            UiEntranceMotion.Play(overlay.Find("Window").gameObject);
        }

        private void CloseDeckUtility()
        {
            if (_utilityOverlay == _starterPickerOverlay && _starterPickerOverlay != null)
            {
                RefreshOpeningHand();
                ShowDeckUtility(_deckDrawOverlay, _pickStartersButton);
                return;
            }
            DismissDeckUtility(restoreFocus: true);
        }

        private void DismissDeckUtility(bool restoreFocus)
        {
            if (_utilityOverlay == null) return;
            _padUi.Clear();
            _utilityOverlay.gameObject.SetActive(false);
            _utilityOverlay = null;
            _utilityDefault = null;
            var focus = _utilityReturnFocus;
            _utilityReturnFocus = null;
            if (restoreFocus && focus != null && focus.activeInHierarchy)
                EventSystem.current?.SetSelectedGameObject(focus);
        }

        private void OpenTestDraw()
        {
            if (_workingDeck.main.Count == 0) return;
            if (!ReferenceEquals(_starterDeck, _workingDeck))
            {
                _starterDeck = _workingDeck;
                _markedStarters.Clear();
                _starterPage = 0;
            }
            _starterChoices = _workingDeck.main.GroupBy(CardDatabase.GetDeckPrintingKey)
                .Select(group => (group.Key, group.Count())).ToArray();
            _markedStarters.IntersectWith(_starterChoices.Select(card => card.Code));
            _drawGoingSecond = false;
            RedrawOpeningHand();
            OpenDeckUtility(_deckDrawOverlay, _redrawButton);
        }

        private void SetOpeningHandOrder(bool second)
        {
            _drawGoingSecond = second;
            RefreshOpeningHand();
        }

        private void RedrawOpeningHand()
        {
            _openingHand = DeckRecipe.DrawOpeningCards(_workingDeck.main, _openingHandRandom);
            RefreshOpeningHand();
            LotdAudio.PlayDraw();
        }

        private void RefreshOpeningHand()
        {
            int initial = Mathf.Min(5, _openingHand.Length);
            bool turnDraw = _drawGoingSecond && _openingHand.Length > 5;
            _openingHandTitle.text = $"테스트 드로우 · 시작 패 {initial}장" + (turnDraw ? " + 첫 턴 1장" : "");
            _openingHandFirst.color = _drawGoingSecond ? LotdUiKit.TextPrimary : LotdUiKit.GoldAccent;
            _openingHandSecond.color = _drawGoingSecond ? LotdUiKit.GoldAccent : LotdUiKit.TextPrimary;
            for (int i = 0; i < _openingHandViews.Length; i++)
            {
                bool visible = i < initial || i == 5 && turnDraw;
                _openingHandButtons[i].gameObject.SetActive(visible);
                if (!visible) continue;
                uint code = _openingHand[i];
                bool starter = _markedStarters.Contains(CardDatabase.GetDeckPrintingKey(code));
                _openingHandViews[i].SetCard(code);
                _openingHandButtons[i].image.color = starter ? LotdUiKit.GoldAccent :
                    i == 5 ? TurnDrawEdge : DeckEditorStyle.Surface;
            }
            int starterCount = CountOpeningStarters();
            int cardsSeen = initial + (turnDraw ? 1 : 0);
            int hits = _openingHand.Take(cardsSeen).Count(code => _markedStarters.Contains(CardDatabase.GetDeckPrintingKey(code)));
            double miss = DeckRecipe.OpeningHandMissChance(_workingDeck.main.Count, starterCount, cardsSeen);
            _openingHandOdds.text = starterCount == 0 ? "초동을 고르면 확률을 표시합니다" :
                $"초동 미포함 {miss * 100d:0.0}%\n이번 패에 초동 {hits}장";
            _openingHandDrawLabel.gameObject.SetActive(turnDraw);
            if (_deckDrawOverlay.gameObject.activeInHierarchy)
                DeckPadUi.LimitKeyboardNavigation(_deckDrawOverlay);
        }

        private int CountOpeningStarters() =>
            _starterChoices.Where(card => _markedStarters.Contains(card.Code)).Sum(card => card.Copies);

        private void BuildStarterPicker(Transform root)
        {
            _starterPickerOverlay = BuildDeckOverlay(root, "StarterPicker", out var picker);
            LotdUiKit.AddFittedText(DeckArea(picker, "Title", .055f, .843f, .72f, .953f),
                "Text", "초동 카드 선택", 28, 17, TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);
            _starterPickerDone = DeckButton(picker, "Done", "돌아가기", .765f, _mobileLayout ? .765f : .832f,
                .945f, .954f, CloseDeckUtility, true);
            LotdUiKit.AddFittedText(DeckArea(picker, "Instructions", .055f, .724f, .746f, .817f), "Text",
                "첫 턴을 시작하는 데 쓸 카드를 고르세요. 같은 카드의 장수도 계산에 반영됩니다.",
                20, 12, TextAnchor.MiddleLeft, LotdUiKit.TextMuted);
            for (int i = 0; i < _starterPickerSlots.Length; i++)
            {
                int slot = i;
                float x = .052f + i * .151f;
                var cell = DeckArea(picker, $"Card_{i}", x, .198f, x + .140f, .707f);
                var surface = LotdUiKit.AddImage(cell.gameObject, LotdUiKit.WhiteSprite, DeckEditorStyle.Surface);
                var button = cell.gameObject.AddComponent<Button>();
                button.targetGraphic = surface;
                button.onClick.AddListener(() => ToggleOpeningStarter(slot));
                var art = CompositeCardView.Create(DeckArea(cell, "Art", .04f, .176f, .96f, .99f), "Card");
                var caption = LotdUiKit.AddFittedText(DeckArea(cell, "Caption", .04f, .015f, .96f, .177f),
                    "Text", "", 15, 9, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);
                caption.supportRichText = false;
                _starterPickerSlots[i] = (button, art, caption);
            }

            float footerBottom = _mobileLayout ? .013f : .046f;
            float footerTop = _mobileLayout ? .194f : .171f;
            _starterPrevious = DeckButton(picker, "Previous", "이전", .055f, footerBottom, .23f, footerTop,
                () => ShowStarterPage(_starterPage - 1));
            _starterNext = DeckButton(picker, "Next", "다음", .771f, footerBottom, .945f, footerTop,
                () => ShowStarterPage(_starterPage + 1));
            _starterPickerPageLabel = LotdUiKit.AddFittedText(DeckArea(picker, "Page", .252f, .052f, .748f, .156f),
                "Text", "", 19, 12, TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);
        }

        private void OpenStarterPicker()
        {
            ShowDeckUtility(_starterPickerOverlay, _starterPickerDone);
            ShowStarterPage(_starterPage);
        }

        private void ToggleOpeningStarter(int slot)
        {
            int index = _starterPage * StarterPageSize + slot;
            if (index >= _starterChoices.Length) return;
            uint code = _starterChoices[index].Code;
            if (!_markedStarters.Add(code)) _markedStarters.Remove(code);
            ShowStarterPage(_starterPage);
        }

        private void ShowStarterPage(int page)
        {
            int pages = Mathf.Max(1, (_starterChoices.Length + StarterPageSize - 1) / StarterPageSize);
            _starterPage = Mathf.Clamp(page, 0, pages - 1);
            for (int i = 0; i < _starterPickerSlots.Length; i++)
            {
                int index = _starterPage * StarterPageSize + i;
                var (button, art, caption) = _starterPickerSlots[i];
                bool visible = index < _starterChoices.Length;
                button.gameObject.SetActive(visible);
                if (!visible) continue;
                var (code, copies) = _starterChoices[index];
                bool selected = _markedStarters.Contains(code);
                art.SetCard(code);
                button.image.color = selected ? TurnDrawEdge : DeckEditorStyle.Surface;
                caption.color = selected ? LotdUiKit.GoldAccent : LotdUiKit.TextPrimary;
                caption.text = $"{CardDatabase.GetName(code)} · {copies}장" + (selected ? " ✓" : "");
            }
            _starterPrevious.interactable = _starterPage > 0;
            _starterNext.interactable = _starterPage + 1 < pages;
            int starterCount = CountOpeningStarters();
            int cardsSeen = Mathf.Min(_drawGoingSecond ? 6 : 5, _workingDeck.main.Count);
            double miss = DeckRecipe.OpeningHandMissChance(_workingDeck.main.Count, starterCount, cardsSeen);
            _starterPickerPageLabel.text = $"{_starterPage + 1} / {pages}  ·  초동 {starterCount}장";
            if (starterCount > 0) _starterPickerPageLabel.text += $"  ·  미포함 {miss * 100d:0.0}%";
            DeckPadUi.LimitKeyboardNavigation(_starterPickerOverlay);
            var focus = EventSystem.current?.currentSelectedGameObject?.GetComponent<Button>();
            if (focus != null && (!focus.gameObject.activeInHierarchy || !focus.interactable))
                EventSystem.current.SetSelectedGameObject(_starterPickerSlots[0].Button.gameObject);
        }

        private void CopyDeckRecipe(bool ydk)
        {
            var snapshot = CopyDeck(_workingDeck);
            snapshot.name = DeckNameForSave;
            GUIUtility.systemCopyBuffer = ydk ? DeckRepository.ExportToYdk(snapshot) : DeckRecipe.ExportText(snapshot);
            _deckRecipeStatus.text = ydk ? "YDK를 클립보드에 복사했습니다." : "카드 이름이 포함된 목록을 복사했습니다.";
        }

        private void PasteDeckRecipe()
        {
            string text = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(text))
                _deckRecipeStatus.text = "클립보드에 붙여넣을 카드 목록이 없습니다.";
            else if (text.Length > _deckRecipeInput.characterLimit)
                _deckRecipeStatus.text = "카드 목록은 24,000자까지 붙여넣을 수 있습니다.";
            else
            {
                _deckRecipeInput.SetTextWithoutNotify(text);
                _deckRecipeStatus.text = "목록을 붙여넣었습니다. 새 덱으로 불러올 수 있습니다.";
            }
        }

        private void ImportDeckRecipe()
        {
            if (!DeckRecipe.TryImport(_deckRecipeInput.text, out var deck, out string error))
            {
                _deckRecipeStatus.text = error.Replace('\n', ' ');
                return;
            }
            BeforeReplacingEdits(() => SaveNewDeckFromUtility(deck, $"'{deck.name}' 덱을 가져왔습니다."));
        }

        private void DuplicateWorkingDeck()
        {
            var copy = CopyDeck(_workingDeck);
            copy.name = $"{DeckNameForSave} (복제)";
            copy.id = null;
            SaveNewDeckFromUtility(copy, $"'{copy.name}' 덱을 만들었습니다.");
        }

        private void SaveNewDeckFromUtility(UserDeck deck, string feedback)
        {
            try
            {
                DeckRepository.SaveUserDeck(deck);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[DeckEditor] Could not create a recipe deck: {ex.Message}");
                _deckRecipeStatus.text = "덱을 저장할 수 없습니다. 저장 폴더를 확인하세요.";
                return;
            }
            CloseDeckUtility();
            _userDeckIdx = DeckRepository.UserDecks.FindIndex(saved => saved.id == deck.id);
            LoadWorkingDeckFromCurrent();
            ShowToast(feedback);
        }

        private void ConfirmClearWorkingDeck()
        {
            if (_workingDeck.main.Count + _workingDeck.extra.Count == 0)
            {
                _deckRecipeStatus.text = "메인 덱과 엑스트라 덱이 이미 비어 있습니다.";
                return;
            }
            ShowDeckDialog("덱 비우기", "메인 덱과 엑스트라 덱의 카드를 모두 빼시겠습니까?", "비우기", () =>
            {
                _workingDeck.ClearCards(_workingDeck.main);
                _workingDeck.ClearCards(_workingDeck.extra);
                FinishDeckEdit();
                CloseDeckDialog(restoreFocus: false);
                CloseDeckUtility();
                ShowToast("메인 덱과 엑스트라 덱을 비웠습니다.");
            });
        }
    }
}
