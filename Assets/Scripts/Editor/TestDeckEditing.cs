using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static class TestDeckEditing
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static void Run(string streamingPath)
        {
            bool openDecks = CollectionProgress.AllDeckCardsAvailable;
            CollectionProgress.AllDeckCardsAvailable = false;
            try
            {
                VerifyDeckPicker(streamingPath);
                VerifyDeckEditing(streamingPath);
            }
            finally { CollectionProgress.AllDeckCardsAvailable = openDecks; }
        }

        internal static void OpenSavedDeck(DeckEditorUI editor, int index = 0)
        {
            editor.Open();
            var rows = Field<RectTransform>(editor, "_deckPickerRows");
            if (rows.childCount == 0)
                Field<Button>(editor, "_deckPickerNew").onClick.Invoke();
            else
                rows.Find($"SavedDeck_{index}").GetComponent<Button>().onClick.Invoke();
            Require(Field<UserDeck>(editor, "_workingDeck") != null, "Selecting a deck did not open the editor.");
        }

        private static void VerifyDeckPicker(string streamingPath)
        {
            string save = Path.Combine(Path.GetTempPath(), "ygo-picker-" + Guid.NewGuid().ToString("N"));
            DeckRepository.Load(streamingPath, save, seedDefaultDecks: false);
            var previousEvents = EventSystem.current;
            var originalSelection = previousEvents?.currentSelectedGameObject;
            var events = previousEvents ?? new GameObject("Deck picker input", typeof(EventSystem)).GetComponent<EventSystem>();
            if (EventSystem.current == null) Call(events, "OnEnable");
            var canvas = new GameObject("Deck picker canvas", typeof(Canvas), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
            var editor = new DeckEditorUI(null, canvas.transform, mobileLayout: true);
            try
            {
                editor.Open();
                var picker = Field<RectTransform>(editor, "_deckPicker");
                var body = Field<GameObject>(editor, "_editorContent");
                var rows = Field<RectTransform>(editor, "_deckPickerRows");
                Require(picker.gameObject.activeInHierarchy && !body.activeInHierarchy && rows.childCount == 0 &&
                    DeckRepository.UserDecks.Count == 0 && Field<UserDeck>(editor, "_workingDeck") == null,
                    "Opening the deck editor skipped the empty picker or created an implicit deck.");
                Field<Button>(editor, "_deckPickerNew").onClick.Invoke();
                var first = Field<UserDeck>(editor, "_workingDeck");
                Require(first != null && first.main.Count == 0 && body.activeInHierarchy && !picker.gameObject.activeInHierarchy,
                    "Creating a deck from the picker did not open that empty deck.");

                var second = new UserDeck { name = "두 번째 덱", main = new List<uint> { 46986414 } };
                DeckRepository.SaveUserDeck(second);
                var back = body.transform.Find("MobileToolbar/Back/Button").GetComponent<Button>();
                Field<InputField>(editor, "_deckNameInput").text = "이름 바꾼 덱";
                back.onClick.Invoke();
                var confirmation = Field<GameObject>(editor, "_deckDialog");
                Require(confirmation.activeInHierarchy && !picker.gameObject.activeInHierarchy,
                    "Returning to the picker skipped the unsaved-edits confirmation.");
                Field<Button>(editor, "_deckDialogCancel").onClick.Invoke();
                Require(body.activeInHierarchy && Field<UserDeck>(editor, "_workingDeck") == first && !confirmation.activeInHierarchy,
                    "Canceling the confirmation lost the working deck.");
                back.onClick.Invoke();
                Field<Button>(editor, "_deckDialogPrimary").onClick.Invoke();
                Require(picker.gameObject.activeInHierarchy && rows.childCount == 2 &&
                    rows.Find("SavedDeck_0/Name/Text").GetComponent<Text>().text == "이름 바꾼 덱",
                    "Returning to the list after saving did not show the updated deck.");
                var secondRow = rows.Find("SavedDeck_1").GetComponent<Button>();
                var pad = Field<DeckPadUi>(editor, "_padUi");
                pad.FocusInScope(picker, secondRow);
                pad.Handle(picker, Vector2Int.zero, submit: true, cancel: false, onCancel: null, defaultFocus: secondRow);
                Require(!picker.gameObject.activeInHierarchy && Field<UserDeck>(editor, "_workingDeck").id == second.id &&
                    Field<UserDeck>(editor, "_workingDeck").main.SequenceEqual(second.main),
                    "Selecting the second deck with controller input entered a different deck.");
                editor.Close();
                editor.Open();
                Require(picker.gameObject.activeInHierarchy && Field<UserDeck>(editor, "_workingDeck") == null,
                    "Reopening the editor bypassed the list.");
                Debug.Log("[VERIFY DECK PICKER] Empty list, explicit creation, save/cancel on return, controller selection and reopen passed.");
            }
            finally
            {
                editor.Close();
                if (previousEvents != null) previousEvents.SetSelectedGameObject(originalSelection);
                else
                {
                    if (EventSystem.current == events) Call(events, "OnDisable");
                    UnityEngine.Object.DestroyImmediate(events.gameObject);
                }
                UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            }
        }

        private static void VerifyDeckEditing(string streamingPath)
        {
            string save = Path.Combine(Path.GetTempPath(), "ygo-deck-input-test-" + Guid.NewGuid().ToString("N"));
            DeckRepository.Load(streamingPath, save, seedDefaultDecks: false);
            DeckRepository.SaveUserDeck(new UserDeck { name = "Motion fixture", main = new List<uint> { 46986414 } });
            var previousEvents = EventSystem.current;
            var before = previousEvents?.currentSelectedGameObject;
            var events = previousEvents ?? new GameObject("Deck test events", typeof(EventSystem)).GetComponent<EventSystem>();
            if (EventSystem.current == null) Call(events, "OnEnable");
            var canvas = new GameObject("Deck test canvas", typeof(Canvas), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
            var editor = new DeckEditorUI(canvas.AddComponent<GameAppController>(), canvas.transform);
            try
            {
                OpenSavedDeck(editor);
                VerifyTransfer(editor);
                VerifyLegacyDeck(editor);
                VerifyConfirmation(editor, events);
                VerifyArchetypeBrowser(editor, events);
                VerifyGroupingAndTrunk(editor, streamingPath, Path.Combine(save, "trunk"));
                VerifyArchetypeSearch(editor);
                VerifyOpenDeckAccess(editor);
                VerifyArchetypeCodex(canvas.transform, events);
                VerifyMainSorting(editor, events);
                VerifyTouchDeckMenu(editor, events);
                VerifyCardPrintings(editor, events, streamingPath, Path.Combine(save, "printings"));
                VerifyOverframeSlots(editor, streamingPath, Path.Combine(save, "overframe"));
                VerifyArtworkSources(editor, streamingPath, Path.Combine(save, "artwork"));
            }
            finally
            {
                editor.Close();
                events.SetSelectedGameObject(before);
                UnityEngine.Object.DestroyImmediate(canvas);
                if (previousEvents == null)
                {
                    if (EventSystem.current == events) Call(events, "OnDisable");
                    UnityEngine.Object.DestroyImmediate(events.gameObject);
                }
            }
            Debug.Log("[VERIFY DECK EDITING] Deck row layout, vertical and horizontal scrolling, transfer continuity, modal focus, grouped copies, trunk counts, sorting and touch passed.");
        }

        private static void VerifyTransfer(DeckEditorUI editor)
        {
            const uint blueEyes = 89631139;
            Call(editor, "InspectCard", blueEyes);
            Canvas.ForceUpdateCanvases();
            var deck = Field<UserDeck>(editor, "_workingDeck");
            var source = (RectTransform)Field<CompositeCardView>(editor, "_previewCard").transform;
            var transfer = Field<RectTransform>(editor, "_transferRect");
            Call(editor, "TryAddCardToDeck", blueEyes, DragSourceType.DeckMain, source, null);
            Require(deck.main.Count == 2 && deck.main[1] == blueEyes && transfer.gameObject.activeSelf,
                "Adding a card did not bind the expected transfer and list entry.");
            Call(editor, "UpdateDeckTransfer", .085f);
            Vector2 visible = transfer.anchoredPosition;
            Call(editor, "MoveDeckCard", DragSourceType.DeckMain, 1, DragSourceType.DeckExtra, -1, null);
            Require(deck.main.Count == 2 && deck.extra.Count == 0 && transfer.anchoredPosition == visible,
                "Moving a main-deck card to an invalid section changed the deck or its pending transfer.");
            Call(editor, "MoveDeckCard", DragSourceType.DeckMain, 1, DragSourceType.DeckSide, -1, null);
            Require(deck.main.Count == 1 && deck.side.SequenceEqual(new[] { blueEyes }) &&
                (transfer.anchoredPosition - visible).sqrMagnitude < .1f,
                "Moving a card again started from its slot instead of its visible mid-flight pose.");
            Call(editor, "UpdateDeckTransfer", .5f);
            Call(editor, "RemoveDeckCard", DragSourceType.DeckSide, 0, null);
            Call(editor, "UpdateDeckTransfer", .5f);
            Require(deck.side.Count == 0 && !transfer.gameObject.activeSelf, "The removed card or transfer was left active.");
        }

        private static void VerifyLegacyDeck(DeckEditorUI editor)
        {
            var saved = Field<UserDeck>(editor, "_workingDeck");
            saved.main.AddRange(Enumerable.Repeat(46986414u, 39));
            saved.extra.AddRange(Enumerable.Repeat(23995346u, 9));
            saved.side.AddRange(Enumerable.Repeat(46986414u, 15));
            Call(editor, "RefreshDeckGrids");
            Canvas.ForceUpdateCanvases();
            var scroll = Field<ScrollRect>(editor, "_mainDeckScroll");
            var mainSlots = Field<RectTransform>(editor, "_mainDeckSlotGrid");
            var extraSlots = Field<RectTransform>(editor, "_extraGridRect");
            var sideSlots = Field<RectTransform>(editor, "_sideDeckSlotGrid");
            var sideScroll = Field<ScrollRect>(editor, "_sideDeckScroll");
            VerifyEightColumns(mainSlots, "Main");
            VerifyEightColumns(extraSlots, "Extra");
            var firstSideSlot = (RectTransform)sideSlots.Find("Card_0");
            var lastSideSlot = (RectTransform)sideSlots.Find("Card_14");
            Require(firstSideSlot.anchorMin.y == lastSideSlot.anchorMin.y && firstSideSlot.anchorMax.y == lastSideSlot.anchorMax.y &&
                lastSideSlot.anchorMin.x > firstSideSlot.anchorMin.x && sideScroll.horizontal && !sideScroll.vertical &&
                Mathf.Abs(sideScroll.content.rect.width - sideScroll.viewport.rect.width * 15f / 8f) < .1f,
                "The full Side Deck did not use a single horizontally scrolling row.");
            var trunk = Field<RectTransform>(editor, "_trunkContent");
            Require(((RectTransform)trunk.Find("TrunkCard/CardInset/Card/AspectBox")).rect.width >= 82f,
                "The trunk artwork became too small.");
            VerifyDeckArtwork(mainSlots, 85f, "Main");
            VerifyDeckArtwork(extraSlots, 73f, "Extra");
            VerifyDeckArtwork(sideSlots, 72f, "Side");
            float slotHeight = ((RectTransform)mainSlots.Find("Card_0")).rect.height;
            Require(Mathf.Abs(scroll.content.rect.height - scroll.viewport.rect.height * 5f / 3f) < .1f,
                "A 40-card deck did not have three visible rows and two scrollable rows.");
            Require(Mathf.Abs(scroll.content.anchoredPosition.y) < .1f, "The main deck did not start at its first row.");

            saved.main.AddRange(Enumerable.Repeat(46986414u, 21));
            Call(editor, "RefreshDeckGrids");
            Canvas.ForceUpdateCanvases();
            Require(Mathf.Abs(((RectTransform)mainSlots.Find("Card_0")).rect.height - slotHeight) < .1f,
                "The main deck cards changed size between the five-row and eight-row layouts.");
            var area = editor.GetType().GetField("_padArea", Instance);
            var main = Enum.Parse(area.FieldType, "Main");
            area.SetValue(editor, main);
            var indices = Field<int[]>(editor, "_padIndices");
            indices[(int)main] = 0;
            Call(editor, "NavigatePadCards", Vector2Int.down, false);
            Require(indices[(int)main] == 8, "Controller movement did not follow the main deck columns.");
            indices[(int)main] = 60;
            Call(editor, "DrawPadCardFocus");
            var lastSlot = (RectTransform)mainSlots.Find("Card_59");
            float lastSlotBottom = scroll.viewport.InverseTransformPoint(lastSlot.TransformPoint(lastSlot.rect.min)).y;
            Require(scroll.content.anchoredPosition.y > 0f && lastSlotBottom >= scroll.viewport.rect.yMin - .1f,
                "Controller focus did not scroll the eighth row into view.");
            Require(indices[(int)main] == 59 && saved.main.Count == 61,
                "An oversized legacy deck broke focus or dropped its unrendered card.");

            var extra = Enum.Parse(area.FieldType, "Extra");
            area.SetValue(editor, extra);
            indices[(int)extra] = 0;
            Call(editor, "NavigatePadCards", Vector2Int.down, false);
            Require(indices[(int)extra] == 8, "Controller focus did not reach the second Extra Deck row.");

            var side = Enum.Parse(area.FieldType, "Side");
            area.SetValue(editor, side);
            indices[(int)side] = 7;
            Call(editor, "NavigatePadCards", Vector2Int.right, false);
            Call(editor, "DrawPadCardFocus");
            Require(indices[(int)side] == 8 && sideScroll.content.anchoredPosition.x < 0f,
                "Controller focus did not scroll the Side Deck when it passed the visible cards.");
            indices[(int)side] = 14;
            Call(editor, "DrawPadCardFocus");
            float lastSideRight = sideScroll.viewport.InverseTransformPoint(lastSideSlot.TransformPoint(lastSideSlot.rect.max)).x;
            Require(lastSideRight > sideScroll.viewport.rect.xMin && lastSideRight <= sideScroll.viewport.rect.xMax + .1f,
                "The last Side Deck card remained clipped while focused.");

            var finger = new PointerEventData(EventSystem.current)
            {
                pointerId = 0,
                button = PointerEventData.InputButton.Left,
                pressPosition = new Vector2(320f, 200f),
                position = new Vector2(260f, 200f),
            };
            var trigger = lastSideSlot.GetComponent<CardDragTrigger>();
            trigger.OnBeginDrag(finger);
            Require(Field<ScrollRect>(trigger, "_parentScroll") == sideScroll, "A horizontal swipe over a Side Deck card did not scroll the row.");
            trigger.OnEndDrag(finger);

            saved.extra.Clear();
            saved.side.Clear();
            Call(editor, "RefreshDeckGrids");
            Require(Mathf.Abs(sideScroll.content.anchoredPosition.x) < .1f, "An empty Side Deck held on to its previous scroll offset.");
        }

        private static void VerifyDeckArtwork(Transform grid, float minHeight, string section)
        {
            var art = (RectTransform)grid.Find("Card_0/CardInset/Card/AspectBox");
            Require(art.rect.height >= minHeight, $"{section} artwork became too small.");
        }

        private static void VerifyEightColumns(Transform grid, string section)
        {
            var first = (RectTransform)grid.Find("Card_0");
            var eighth = (RectTransform)grid.Find("Card_7");
            var ninth = (RectTransform)grid.Find("Card_8");
            Require(Mathf.Approximately(first.anchorMin.y, eighth.anchorMin.y) &&
                Mathf.Approximately(first.anchorMin.x, ninth.anchorMin.x) &&
                ninth.anchorMax.y < first.anchorMax.y,
                $"{section} did not wrap after eight cards.");
        }

        private static void VerifyConfirmation(DeckEditorUI editor, EventSystem events)
        {
            var original = editor.Root.GetComponentsInChildren<Button>().First(button => button.IsInteractable());
            events.SetSelectedGameObject(original.gameObject);
            bool applied = false;
            Call(editor, "ShowDeckDialog", "Confirm", "Discard the changes?", "Apply", (Action)(() => applied = true), null, null);
            var cancel = Field<Button>(editor, "_deckDialogCancel");
            var confirm = Field<Button>(editor, "_deckDialogPrimary");
            Require(events.currentSelectedGameObject == cancel.gameObject &&
                cancel.FindSelectableOnRight() == confirm && confirm.FindSelectableOnLeft() == cancel &&
                cancel.FindSelectableOnUp() == null && confirm.FindSelectableOnDown() == null,
                "The confirmation did not start on Cancel or permitted native keyboard focus to leave.");
            cancel.onClick.Invoke();
            confirm.onClick.Invoke();
            Require(!applied && events.currentSelectedGameObject == original.gameObject,
                "A canceled dialog kept a stale action or failed to return focus.");
        }

        private static void VerifyArchetypeBrowser(DeckEditorUI editor, EventSystem events)
        {
            var before = events.currentSelectedGameObject;
            Call(editor, "OpenArchetypeModal");
            var popup = Field<GameObject>(editor, "_archModalRoot");
            var close = Field<Button>(editor, "_archCloseButton");
            var search = Field<InputField>(editor, "_archSearchInp");
            Require(popup.activeSelf && events.currentSelectedGameObject == close.gameObject &&
                search.navigation.mode == Navigation.Mode.Explicit &&
                popup.GetComponentsInChildren<Button>().Where(button => button.IsInteractable() && button.gameObject != popup)
                    .All(button => button.navigation.mode == Navigation.Mode.Explicit),
                "The archetype browser did not take focus or allowed keyboard navigation outside it.");
            close.onClick.Invoke();
            Require(!popup.activeSelf && events.currentSelectedGameObject == before,
                "Closing the archetype browser did not restore the original control.");
        }

        private static void VerifyArchetypeSearch(DeckEditorUI editor)
        {
            var deck = Field<UserDeck>(editor, "_workingDeck");
            var before = CollectionProgress.CopyDeck(deck);
            editor.Root.transform.Find("Editor/Trunk/SearchOptions/ArchetypeSearch/Button").GetComponent<Button>().onClick.Invoke();
            var popup = Field<GameObject>(editor, "_archModalRoot");
            var query = Field<InputField>(editor, "_archSearchInp");
            var matches = Field<List<ArchetypeInfo>>(editor, "_archResults");
            query.text = "상 검";
            Require(popup.activeSelf && matches.Any(group => group.baseName == "상검"),
                "The trunk archetype search missed Swordsoul with spacing in the query.");
            query.text = "D/D/D";
            int index = matches.FindIndex(group => group.setcodes.Contains(0x10af));
            Require(index >= 0 && matches[index].supportCards.Count > 0,
                "The trunk archetype search missed D/D/D or its support cards when entered with slashes.");
            var group = matches[index];
            var apply = popup.transform.Find($"Win/ArchScrollHolder/VP/Content/ArchTile_{index / 3}_{index % 3}/LoadBtn/B")
                .GetComponent<Button>();
            Require(apply.GetComponentInChildren<Text>().text == "카드 보기", "Trunk search exposed a deck import action.");
            apply.onClick.Invoke();
            var result = Field<List<CardInfo>>(editor, "_trunkResults");
            var expected = group.coreCards.Union(group.supportCards).ToHashSet();
            Require(!popup.activeSelf && Field<ArchetypeInfo>(editor, "_activeArchetypeFilter") == group &&
                result.ToHashSet().SetEquals(expected),
                $"Applying the archetype returned {result.Count} results for {group.name}, expected {expected.Count}.");
            Require(result.Any(card => CollectionProgress.GetOwnedCount(card.code) == 0),
                "The D/D/D scope omitted unowned cards from the Swordsoul player's search.");
            Require(deck.name == before.name && deck.main.SequenceEqual(before.main) && deck.extra.SequenceEqual(before.extra) &&
                deck.side.SequenceEqual(before.side), "Searching for an archetype changed the current deck.");
            Field<Button>(editor, "_showAllCards").onClick.Invoke();
            Require(Field<ArchetypeInfo>(editor, "_activeArchetypeFilter") == null &&
                result.All(card => CollectionProgress.GetOwnedCount(card.code) > 0), "The full trunk retained the archetype scope.");
        }

        private static void VerifyOpenDeckAccess(DeckEditorUI editor)
        {
            var query = Field<InputField>(editor, "_searchInp");
            var deck = Field<UserDeck>(editor, "_workingDeck");
            var unowned = CardDatabase.AllCards.First(card => card.alias == 0 && !card.IsExtra &&
                CollectionProgress.IsCraftableCard(card) && CollectionProgress.GetOwnedCount(card.code) == 0 &&
                !deck.main.Contains(card.code) && !deck.side.Contains(card.code));
            string originalQuery = query.text;
            CollectionProgress.AllDeckCardsAvailable = true;
            try
            {
                query.text = unowned.code.ToString();
                Require(Field<List<CardInfo>>(editor, "_trunkResults").Any(card => card.code == unowned.code) &&
                    (uint)CallResult(editor, "FindAddableCardCopy", unowned.code, false) == unowned.code,
                    "Open access did not display or allow adding an unowned card.");
                for (int i = 0; i < 3; i++) deck.main.Add(unowned.code);
                Require((uint)CallResult(editor, "FindAddableCardCopy", unowned.code, false) == 0,
                    "Open access bypassed the three-copy limit.");
                Debug.Log("[VERIFY OPEN TRUNK] Unowned card search and the three-copy limit passed.");
            }
            finally
            {
                deck.main.RemoveAll(code => code == unowned.code);
                CollectionProgress.AllDeckCardsAvailable = false;
                query.text = originalQuery;
            }
        }

        private static void VerifyArchetypeCodex(Transform canvas, EventSystem events)
        {
            var group = StarterArchetypeCatalog.Search("상검", includeSmallGroups: true).First(item => item.baseName == "상검");
            var memberKeys = group.coreCards.Select(CardDatabase.GetArchetypeCollectionKey).ToHashSet();
            var ownedKeys = CardDatabase.AllCards.Where(card => CollectionProgress.GetOwnedCount(card.code) > 0)
                .Select(CardDatabase.GetArchetypeCollectionKey).ToHashSet();
            int owned = memberKeys.Count(ownedKeys.Contains);
            Require(CardDatabase.GetArchetypeCollectionKey(CardDatabase.Get(89631134)) == 89631139 &&
                CardDatabase.GetArchetypeCollectionKey(CardDatabase.Get(295517)) == 295517,
                "The codex must group alternate artwork and keep distinct cards that share a rules name separate.");

            var before = events.currentSelectedGameObject;
            var codex = new ArchetypeCodexUI(canvas, () => { });
            try
            {
                codex.Open();
                codex.SearchInput.text = "상검";
                var directory = codex.Root.transform.Find("Directory");
                var row = directory.GetComponentsInChildren<PackShopButtonLabel>().Single(button => button.Label.text == group.name);
                events.SetSelectedGameObject(row.gameObject);
                var progress = Field<Text>(codex, "_progress");
                Require(owned > 0 && progress.text == $"소속 카드 수집 {owned} / {memberKeys.Count}종",
                    "The codex did not compute collection progress from owned member cards.");

                Field<Button>(codex, "_support").onClick.Invoke();
                var related = Field<uint[]>(codex, "_cards");
                Require(related.Length > 0 && !related.Any(memberKeys.Contains) &&
                    progress.text == $"소속 카드 수집 {owned} / {memberKeys.Count}종" &&
                    Field<CompositeCardView>(codex, "_preview").CurrentCode != 0,
                    "The support tab showed no preview or contributed to the member count.");

                codex.SearchInput.text = "__없는카드군42__";
                Require(!Field<GameObject>(codex, "_detail").activeSelf, "Empty search results left stale card details visible.");
            }
            finally
            {
                codex.Close();
                events.SetSelectedGameObject(before);
                UnityEngine.Object.DestroyImmediate(codex.Root);
            }
            Debug.Log("[VERIFY ARCHETYPE CODEX] Name search, trunk scope, member ownership, support tabs and alternate art passed.");
        }

        private static void VerifyGroupingAndTrunk(DeckEditorUI editor, string streamingPath, string save)
        {
            editor.Close();
            DeckRepository.Load(streamingPath, save, seedDefaultDecks: false);
            CollectionProgress.Load(save);
            var selection = CollectionProgress.Starters.First(option => option.Name.Contains("상검"));
            Require(CollectionProgress.TryChooseStarter(selection.Id, out var starter, out string error), error);
            string profile = Path.Combine(save, CollectionProgress.ProfileFileName);
            File.WriteAllText(profile, File.ReadAllText(profile).Replace("\"craftDust\": \"0\"", "\"craftDust\": \"900\""));
            CollectionProgress.Load(save);
            uint repeated = starter.main.GroupBy(code => code).First(group => group.Count() == 3).Key;
            uint other = starter.main.First(code => CollectionProgress.GetDeckCopyKey(code) != CollectionProgress.GetDeckCopyKey(repeated));
            var saved = new UserDeck { name = "Split copies fixture", main = new List<uint> { repeated, other, repeated } };
            DeckRepository.SaveUserDeck(saved);
            OpenSavedDeck(editor, DeckRepository.UserDecks.IndexOf(saved));

            var deck = Field<UserDeck>(editor, "_workingDeck");
            Require(deck.main.SequenceEqual(new[] { repeated, repeated, other }) &&
                saved.main.SequenceEqual(new[] { repeated, other, repeated }),
                "Loading separated copies did not group them without rewriting the saved deck.");
            Require(!(bool)CallResult(editor, "HasUnsavedChanges"), "Loading a deck marked it dirty just for grouping copies.");

            Field<InputField>(editor, "_searchInp").text = repeated.ToString();
            Call(editor, "InspectCard", repeated);
            Require(TrunkCount(editor, repeated) == "×1", "Two copies assigned to Main did not leave one in the trunk.");
            Call(editor, "MoveDeckCard", DragSourceType.DeckMain, 1, DragSourceType.DeckMain, 2, null);
            Require(deck.main.SequenceEqual(new[] { other, repeated, repeated }),
                "Reordering within Main split the group instead of moving it together.");
            Call(editor, "TryAddCardToDeck", repeated, DragSourceType.DeckMain, null, null);
            Require(deck.main.SequenceEqual(new[] { other, repeated, repeated, repeated }) &&
                TrunkCount(editor, repeated) == "×0", "An added copy did not join its group and consume the last trunk copy.");
            Call(editor, "TryAddCardToDeck", repeated, DragSourceType.DeckMain, null, null);
            Require(deck.main.Count == 4, "The zero trunk balance allowed another copy into the deck.");

            Call(editor, "MoveDeckCard", DragSourceType.DeckMain, 2, DragSourceType.DeckSide, -1, null);
            Require(deck.main.SequenceEqual(new[] { other, repeated, repeated }) && deck.side.SequenceEqual(new[] { repeated }) &&
                TrunkCount(editor, repeated) == "×0", "Moving one copy to Side changed the total available in the trunk.");
            Call(editor, "RemoveDeckCard", DragSourceType.DeckSide, 0, null);
            Require(TrunkCount(editor, repeated) == "×1" &&
                Field<Text>(editor, "_previewOwnership").text.Contains("보관함 1장"),
                "Removing a card did not update the trunk and preview immediately.");
            Require(CollectionProgress.GetOwnedCount(repeated) == 3, "Editing the deck changed card ownership.");
            VerifyUnownedBadges(editor, deck, repeated);
        }

        private static void VerifyUnownedBadges(DeckEditorUI editor, UserDeck deck, uint code)
        {
            deck.side.Add(code);
            Call(editor, "RefreshDeckGrids");
            var archetype = new ArchetypeInfo { name = "Ownership fixture" };
            archetype.coreCards.Add(CardDatabase.Get(code));
            Call(editor, "SetTrunkSource", 0u, archetype);

            var mainGrid = Field<RectTransform>(editor, "_mainDeckSlotGrid");
            var sideGrid = Field<RectTransform>(editor, "_sideDeckSlotGrid");
            var mainBadge = mainGrid.Find($"Card_{deck.main.IndexOf(code)}/CardInset/Card/AspectBox/Unowned");
            var secondBadge = mainGrid.Find($"Card_{deck.main.LastIndexOf(code)}/CardInset/Card/AspectBox/Unowned");
            var sideBadge = sideGrid.Find("Card_0/CardInset/Card/AspectBox/Unowned");
            var preview = Field<Text>(editor, "_previewOwnership");
            Call(editor, "InspectCard", code);
            Require(!mainBadge.gameObject.activeInHierarchy && !sideBadge.gameObject.activeInHierarchy &&
                TrunkCount(editor, code) == "×0", "Spending all available copies in the deck marked the card as unowned.");

            Call(editor, "OpenCardCrafting", code, CardTradeAction.Browse);
            Require(CollectionProgress.TryDisassembleCards(new Dictionary<uint, int> { [code] = 3 }, out string error), error);
            Call(editor, "ReturnFromCardCrafting");
            Require(mainBadge.gameObject.activeInHierarchy && sideBadge.gameObject.activeInHierarchy &&
                TrunkCount(editor, code) == "미보유" && preview.text.Contains("미보유"),
                "Disassembling the last copy did not refresh the deck, card list and preview.");

            Call(editor, "OpenCardCrafting", code, CardTradeAction.Browse);
            Require(CollectionProgress.TryCraftCard(code, 1, out error), error);
            Call(editor, "ReturnFromCardCrafting");
            Require(!mainBadge.gameObject.activeInHierarchy && secondBadge.gameObject.activeInHierarchy &&
                sideBadge.gameObject.activeInHierarchy &&
                Field<Text>(editor, "_mainCountText").text.Contains("부족 1장") &&
                Field<Text>(editor, "_sideCountText").text.Contains("부족 1장") &&
                TrunkCount(editor, code) == "×0" && preview.text.Contains("보유 1장"),
                "Crafting one copy should clear just one of the three missing copies across Main and Side.");

            Call(editor, "OpenCardCrafting", code, CardTradeAction.Browse);
            Require(CollectionProgress.TryCraftCard(code, 2, out error), error);
            Call(editor, "ReturnFromCardCrafting");
            Require(!secondBadge.gameObject.activeInHierarchy && !sideBadge.gameObject.activeInHierarchy &&
                !Field<Text>(editor, "_mainCountText").text.Contains("부족") &&
                !Field<Text>(editor, "_sideCountText").text.Contains("부족"),
                "Obtaining the remaining copies did not clear the badges and section counters.");
            Call(editor, "SetTrunkSource", 0u, null);
        }

        private static string TrunkCount(DeckEditorUI editor, uint code) =>
            Field<List<(RectTransform rt, CompositeCardView card, Text count, CardDragTrigger trigger)>>(editor, "_trunkPool")
                .Single(slot => slot.rt.gameObject.activeInHierarchy && slot.card.CurrentCode == code).count.text;

        private static void VerifyOverframeSlots(DeckEditorUI editor, string streamingPath, string save)
        {
            const uint masquerena = 65741786;
            var overframe = new DeckCardAppearance { overframe = true };
            editor.Close();
            DeckRepository.Load(streamingPath, save, seedDefaultDecks: false);
            CollectionProgress.Load(save);
            var starter = CollectionProgress.Starters.First(option => option.Name.Contains("상검"));
            Require(CollectionProgress.TryChooseStarter(starter.Id, out _, out string error), error);
            string path = Path.Combine(save, CollectionProgress.ProfileFileName);
            string profile = TestCollectionProgress.WithOwnership(File.ReadAllText(path), masquerena, 1);
            File.WriteAllText(path, Regex.Replace(profile, "\"overframeCards\"\\s*:\\s*\\[[^\\]]*\\]",
                $"\"overframeCards\":[{{\"code\":{masquerena},\"count\":1}}]"));
            CollectionProgress.Load(save);
            var saved = new UserDeck { name = "Overframe shortage fixture", extra = { masquerena, masquerena }, side = { masquerena } };
            saved.SetAppearance(saved.side, 0, overframe);
            DeckRepository.SaveUserDeck(saved);
            OpenSavedDeck(editor, DeckRepository.UserDecks.IndexOf(saved));

            var deck = Field<UserDeck>(editor, "_workingDeck");
            var extra = Field<RectTransform>(editor, "_extraGridRect");
            var side = Field<RectTransform>(editor, "_sideDeckSlotGrid");
            var extraBadge = extra.Find("Card_1/CardInset/Card/AspectBox/Unowned").gameObject;
            var sideBadge = side.Find("Card_0/CardInset/Card/AspectBox/Unowned").gameObject;
            Require(extraBadge.activeInHierarchy && !sideBadge.activeInHierarchy,
                "An explicitly assigned Overframe should be reserved before filling ordinary Extra Deck slots.");

            deck.SetAppearance(deck.side, 0, default);
            Call(editor, "RefreshDeckGrids");
            Require(!extraBadge.activeInHierarchy && sideBadge.activeInHierarchy,
                "An unused Overframe should fill an ordinary slot, with only the final excess copy marked.");

            deck.SetAppearance(deck.extra, 1, overframe);
            deck.SetAppearance(deck.side, 0, overframe);
            Call(editor, "RefreshDeckGrids");
            Require(!extraBadge.activeInHierarchy && sideBadge.activeInHierarchy,
                "Two explicit Overframe slots with one owned edition should flag the excess edition.");
            var slot = (RectTransform)extra.Find("Card_1");
            Field<Dictionary<uint, DeckCardAppearance>>(editor, "_trunkAppearances")[masquerena] = overframe;
            Field<InputField>(editor, "_searchInp").text = masquerena.ToString();
            var trunk = Field<List<(RectTransform rt, CompositeCardView card, Text count, CardDragTrigger trigger)>>(editor, "_trunkPool")
                .Single(item => item.rt.gameObject.activeInHierarchy && item.card.CurrentCode == masquerena).card;
            Call(editor, "SetPadCardVisual", slot, masquerena);
            Call(editor, "OpenExpandedDeckCard", masquerena);
            foreach (var view in new[]
            {
                slot.Find("CardInset/Card").GetComponent<CompositeCardView>(), trunk,
                Field<CompositeCardView>(editor, "_previewCard"), Field<CompositeCardView>(editor, "_expandedDeckArt"),
            })
            {
                Require(view.Appearance?.overframe == true && !view.HasOverframe &&
                    view.transform.Find("AspectBox/Artwork").GetComponent<Image>().preserveAspect &&
                    view.transform.Find("AspectBox/DeckEdition").gameObject.activeInHierarchy,
                    "A deck view should show an in-frame illustration and the Overframe edition badge.");
            }
            Call(editor, "CloseExpandedDeckCard");
            Debug.Log("[VERIFY DECK SHORTAGES] Partial ownership across sections, reserved Overframes, and printed edition views passed.");
        }

        private static void VerifyCardPrintings(DeckEditorUI editor, EventSystem events, string streamingPath, string save)
        {
            const uint blueEyes = 89631139, blueEyesArt = 89631140;
            const uint arcray = 6218704, arcrayArt = 6218705, zarc = 13331639;
            Require(CardDatabase.GetDeckPrintingKey(arcray) == CardDatabase.GetDeckPrintingKey(arcrayArt) &&
                CardDatabase.GetDeckPrintingKey(arcray) != CardDatabase.GetDeckPrintingKey(zarc),
                "Arcray's artworks did not group independently of Z-ARC.");
            Require(CardDatabase.GetDeckPrintingKey(80101899) == CardDatabase.GetDeckPrintingKey(80101900) &&
                CardDatabase.GetDeckPrintingKey(99267150) == CardDatabase.GetDeckPrintingKey(99267151),
                "Trap Trick's variant metadata or Five-Headed Dragon's translated names split their editions.");
            Require(CardDatabase.GetDeckPrintingKey(5405694) != CardDatabase.GetDeckPrintingKey(10000100) &&
                CardDatabase.GetDeckPrintingKey(22702055) != CardDatabase.GetDeckPrintingKey(295517),
                "Cards with different effects were merged because they share a name or rules alias.");

            editor.Close();
            DeckRepository.Load(streamingPath, save, seedDefaultDecks: false);
            CollectionProgress.Load(save);
            var starter = CollectionProgress.Starters.First(option => option.Name.Contains("상검"));
            Require(CollectionProgress.TryChooseStarter(starter.Id, out var starterDeck, out string error), error);
            string path = Path.Combine(save, CollectionProgress.ProfileFileName);
            string profile = TestCollectionProgress.WithOwnership(File.ReadAllText(path), blueEyes, 1);
            File.WriteAllText(path, TestCollectionProgress.WithOwnership(profile, blueEyesArt, 3));
            CollectionProgress.Load(save);
            uint other = starterDeck.main.First(code => CardDatabase.GetDeckPrintingKey(code) != blueEyes);
            var saved = new UserDeck
            {
                name = "Artwork fixture",
                main = new List<uint> { blueEyes, other, blueEyesArt },
                extra = new List<uint> { arcray, zarc, arcrayArt },
            };
            DeckRepository.SaveUserDeck(saved);
            OpenSavedDeck(editor, DeckRepository.UserDecks.IndexOf(saved));

            var deck = Field<UserDeck>(editor, "_workingDeck");
            Require(deck.main.SequenceEqual(new[] { blueEyes, blueEyesArt, other }) &&
                deck.extra.SequenceEqual(new[] { arcray, arcrayArt, zarc }) &&
                saved.extra.SequenceEqual(new[] { arcray, zarc, arcrayArt }) &&
                !(bool)CallResult(editor, "HasUnsavedChanges"), "Loading variants changed the saved deck or split their display order.");
            var search = Field<InputField>(editor, "_searchInp");
            var trunk = Field<List<CardInfo>>(editor, "_trunkResults");
            CardInfo BlueEyesEntry() => trunk.Single(card => CardDatabase.GetDeckPrintingKey(card.code) == blueEyes);
            search.text = CardDatabase.GetName(blueEyes);
            Require(BlueEyesEntry().code == blueEyes && TrunkCount(editor, blueEyes) == "×2",
                "The trunk did not show one Blue-Eyes entry and the combined unused copies.");
            search.text = blueEyesArt.ToString();
            Require(BlueEyesEntry().code == blueEyesArt && TrunkCount(editor, blueEyesArt) == "×2",
                "The alternate passcode did not find the same group with the requested artwork.");

            search.text = blueEyes.ToString();
            var slots = Field<List<(RectTransform rt, CompositeCardView card, Text count, CardDragTrigger trigger)>>(editor, "_trunkPool");
            var grouped = slots.Single(slot => slot.rt.gameObject.activeInHierarchy && slot.card.CurrentCode == blueEyes);
            var related = Field<Button>(editor, "_deckCardActionRelated");
            grouped.trigger.OnTouchHold();
            Require(related.gameObject.activeInHierarchy && related.IsInteractable(),
                "A touch hold did not expose related-card search.");
            Call(editor, "CloseDeckCardActions");
            grouped.trigger.OnRightClick();
            Require(related.gameObject.activeInHierarchy && related.IsInteractable() && deck.main.Count == 3,
                "Right-click did not expose related-card search without changing the deck.");
            Field<Button>(editor, "_deckCardActionMain").onClick.Invoke();
            Require(deck.main.SequenceEqual(new[] { blueEyes, blueEyesArt, blueEyesArt, other }) && TrunkCount(editor, blueEyes) == "×1",
                "Adding the grouped card did not consume an available owned artwork or keep its copies adjacent.");
            Call(editor, "TryAddCardToDeck", blueEyesArt, DragSourceType.DeckMain, null, null);
            Require(deck.main.Count == 4, "A different illustration bypassed the shared copy limit.");
            Call(editor, "MoveDeckCard", DragSourceType.DeckMain, 2, DragSourceType.DeckSide, -1, null);
            Require(deck.side.SequenceEqual(new[] { blueEyesArt }) && TrunkCount(editor, blueEyes) == "×1",
                "Moving an artwork to the side deck changed the available ownership.");
            Call(editor, "RemoveDeckCard", DragSourceType.DeckSide, 0, null);
            Require(TrunkCount(editor, blueEyes) == "×2", "Removing an artwork did not restore a copy to the grouped trunk entry.");

            search.text = CardDatabase.GetName(blueEyes);
            var row = slots.Single(slot => slot.rt.gameObject.activeInHierarchy && slot.card.CurrentCode == blueEyes);
            File.WriteAllText(path, profile);
            CollectionProgress.Load(save);
            Require(CollectionProgress.GetOwnedCount(blueEyes) == 1 && CollectionProgress.GetOwnedCount(blueEyesArt) == 0,
                "The alternate artwork fixture must own only one physical copy.");
            var deckCodes = deck.main.ToArray();
            string key = $"DuelArtwork.Family.{blueEyes}";
            string previous = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
            string previousArtwork = CardArtLoader.ActiveDuelVisualsPath;
            WriteLocalArtworkFixture(save, blueEyesArt);
            CardArtLoader.Shutdown();
            CardArtLoader.Initialize(streamingPath, save);
            var preferenceCache = (Dictionary<uint, (uint, int)>)typeof(CardArtLoader)
                .GetField("SelectedArtworkByFamily", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            try
            {
                var choices = CardArtLoader.GetSelectableArtwork(blueEyes);
                Require(choices.Any(choice => choice.Code == blueEyesArt), "One owned copy did not unlock its other printing.");
                row.trigger.OnTouchClick();
                var artwork = Field<Button>(editor, "_deckCardActionArtwork");
                Require(artwork.gameObject.activeInHierarchy && artwork.IsInteractable(), "The alternate artwork button was hidden.");
                events.SetSelectedGameObject(artwork.gameObject);
                var editorType = editor.GetType();
                editorType.GetField("_padMode", Instance).SetValue(editor, true);
                var frame = editorType.Assembly.GetType("YGOPortable.UI.DeckPadFrame", true);
                var buttons = editorType.Assembly.GetType("YGOPortable.UI.DeckPadButtons", true);
                var expected = choices[(CardArtLoader.ArtworkChoiceIndex(blueEyes, choices, -1) + 1) % choices.Length];
                Call(editor, "HandleDeckPad", Activator.CreateInstance(frame, Instance, null,
                    new object[] { Vector2Int.zero, Enum.Parse(buttons, "Submit"), 0f }, null));
                Require(BlueEyesEntry().code == blueEyes && deck.main.SequenceEqual(deckCodes) &&
                    events.currentSelectedGameObject == artwork.gameObject &&
                    CardArtLoader.GetPreferredArtwork(blueEyesArt) == expected,
                    "Changing artwork changed the physical printing, lost focus or did not share the preference.");
                preferenceCache.Remove(blueEyes);
                Require(CardArtLoader.GetPreferredArtwork(blueEyes) == expected,
                    "The artwork preference did not reload from persistent storage.");
                Call(editor, "CloseDeckCardActions");
                Call(editor, "OpenExpandedDeckCard", blueEyes);
                var expanded = Field<CompositeCardView>(editor, "_expandedDeckArt");
                var button = Field<Button>(editor, "_expandedDeckArtwork");
                Sprite before = expanded.ArtworkSprite;
                button.onClick.Invoke();
                Require(button.gameObject.activeInHierarchy && expanded.ArtworkSprite != before &&
                    expanded.CurrentCode == blueEyes && deck.main.SequenceEqual(deckCodes) &&
                    CollectionProgress.GetOwnedCount(blueEyes) == 1 && CollectionProgress.GetOwnedCount(blueEyesArt) == 0,
                    "The detail button did not immediately change artwork or affected ownership.");
                Call(editor, "CloseExpandedDeckCard");
            }
            finally
            {
                if (previous == null) PlayerPrefs.DeleteKey(key);
                else PlayerPrefs.SetString(key, previous);
                PlayerPrefs.Save();
                preferenceCache.Remove(blueEyes);
                CardArtLoader.Shutdown();
                CardArtLoader.Initialize(streamingPath, previousArtwork);
            }

            var variants = CardDatabase.AllCards.Where(card => CardDatabase.GetDeckPrintingKey(card.code) != card.code).ToArray();
            Debug.Log($"[VERIFY DECK PRINTINGS] {variants.Select(card => CardDatabase.GetDeckPrintingKey(card.code)).Distinct().Count()} " +
                $"groups, {variants.Length} duplicate catalogue rows. Ownership, search, deck order and artwork selection passed.");
        }

        private static void VerifyArtworkSources(DeckEditorUI editor, string streamingPath, string save)
        {
            const uint recoveredMain = 59106048, recoveredExtra = 82370493, localOnly = 11876803, missing = 68811206;
            const uint arcray = 6218704, arcrayAlt = 6218705, zarc = 13331639;
            uint[] recovered = { 16237004, 19491080, 22984000, 53754391, recoveredMain, 60516416, 80843006, recoveredExtra };
            editor.Close();
            DeckRepository.Load(streamingPath, save, seedDefaultDecks: false);
            CollectionProgress.Load(save);
            var starter = CollectionProgress.Starters.First(option => option.Name.Contains("상검"));
            Require(CollectionProgress.TryChooseStarter(starter.Id, out _, out string error), error);
            string path = Path.Combine(save, CollectionProgress.ProfileFileName);
            string profile = File.ReadAllText(path);
            foreach (uint code in new[] { recoveredMain, recoveredExtra, localOnly, missing, arcrayAlt })
                profile = TestCollectionProgress.WithOwnership(profile, code, 1);
            File.WriteAllText(path, profile);
            CollectionProgress.Load(save);
            var saved = new UserDeck
            {
                name = "Artwork source fixture",
                main = new List<uint> { recoveredMain },
                extra = new List<uint> { recoveredExtra, arcrayAlt },
                side = new List<uint> { localOnly },
            };
            DeckRepository.SaveUserDeck(saved);

            WriteLocalArtworkFixture(save, localOnly);
            CardArtLoader.Shutdown();
            CardArtLoader.Initialize(streamingPath, save);
            try
            {
                var fallback = CardArtLoader.GetFallbackArt();
                foreach (uint code in recovered)
                    Require(CardArtLoader.GetArtSprite(code) != fallback, $"Shared artwork {code} did not load from the build.");

                OpenSavedDeck(editor, DeckRepository.UserDecks.IndexOf(saved));
                CompositeCardView FirstSlot(string field) => Field<RectTransform>(editor, field)
                    .Find("Card_0/CardInset/Card").GetComponent<CompositeCardView>();
                Require(FirstSlot("_mainDeckSlotGrid").ArtworkSprite == CardArtLoader.GetArtSprite(recoveredMain) &&
                    FirstSlot("_extraGridRect").ArtworkSprite == CardArtLoader.GetArtSprite(recoveredExtra) &&
                    FirstSlot("_sideDeckSlotGrid").ArtworkSprite?.texture.width == 8,
                    "The Main, Extra, or Side Deck did not display the expected bundled or local illustration.");

                var alternate = CardArtLoader.GetArtSprite(arcrayAlt).texture.GetPixels32();
                Require(alternate.SequenceEqual(CardArtLoader.GetArtSprite(arcray).texture.GetPixels32()) &&
                    !alternate.SequenceEqual(CardArtLoader.GetArtSprite(zarc).texture.GetPixels32()),
                    "Arcray's missing alternate illustration borrowed artwork from a different card.");

                var search = Field<InputField>(editor, "_searchInp");
                var pool = Field<List<(RectTransform rt, CompositeCardView card, Text count, CardDragTrigger trigger)>>(editor, "_trunkPool");
                var preview = Field<CompositeCardView>(editor, "_previewCard");
                search.text = missing.ToString();
                var slot = pool.Single(slot => slot.rt.gameObject.activeInHierarchy);
                slot.trigger.OnLeftClick();
                var label = slot.card.transform.Find("AspectBox/Artwork/MissingArtwork");
                Require(slot.card.CurrentCode == missing && slot.card.ArtworkSprite == fallback &&
                    label != null && label.gameObject.activeSelf && preview.CurrentCode == missing &&
                    Field<Text>(editor, "_previewDesc").text == CardDatabase.Get(missing).desc,
                    "A missing illustration hid the card or its details.");

                search.text = recoveredMain.ToString();
                slot.trigger.OnLeftClick();
                Require(slot.card.CurrentCode == recoveredMain && preview.CurrentCode == recoveredMain &&
                    slot.card.ArtworkSprite == preview.ArtworkSprite && preview.ArtworkSprite != fallback &&
                    !label.gameObject.activeSelf,
                    "Reusing the trunk slot or preview did not replace its missing-art indicator.");
            }
            finally
            {
                editor.Close();
                CardArtLoader.Shutdown();
                CardArtLoader.Initialize(streamingPath);
            }
            Debug.Log("[VERIFY DECK ARTWORK] Bundled recovery, local artwork, equivalent printings, missing-art labels and reused deck views passed.");
        }

        private static void WriteLocalArtworkFixture(string folder, uint code)
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGB24, false);
            texture.SetPixels32(Enumerable.Repeat(new Color32(50, 110, 180, 255), 64).ToArray());
            texture.Apply();
            byte[] image = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            using var pack = new BinaryWriter(File.Create(Path.Combine(folder, "cardart.pack")));
            pack.Write(0x5241444Du); // MDAR
            pack.Write(1u); // version
            pack.Write(1u); // one entry
            pack.Write(code);
            pack.Write(0); // base illustration
            pack.Write(28u); // 12-byte header and one 16-byte entry
            pack.Write((uint)image.Length);
            pack.Write(image);
        }

        private static void VerifyMainSorting(DeckEditorUI editor, EventSystem events)
        {
            const uint blueEyes = 89631139;
            const uint mysticalElf = 15025844;
            const uint goblinAttackForce = 78658564;
            const uint reborn = 83764718;
            const uint mirrorForce = 44095762;
            uint unknownAttack = CardDatabase.AllCards.First(card => card.IsMonster && !card.IsExtra &&
                (card.type & YgoConstants.TYPE_EFFECT) != 0 && card.atk < 0 && card.def < 0).code;
            var deck = Field<UserDeck>(editor, "_workingDeck");
            deck.main = new List<uint> { mirrorForce, blueEyes, reborn, goblinAttackForce, unknownAttack, mysticalElf, blueEyes };
            deck.extra = CardDatabase.AllCards.Where(card => card.IsExtra).Take(2).Select(card => card.code).ToList();
            deck.side = new List<uint> { mirrorForce, reborn };
            var untouchedExtra = deck.extra.ToArray();
            var untouchedSide = deck.side.ToArray();
            Call(editor, "RefreshDeckGrids");

            var opener = Field<Button>(editor, "_mainSortButton");
            var overlay = Field<GameObject>(editor, "_mainSortMenu");
            var options = Field<Button[]>(editor, "_mainSortOptions");
            void Select(DeckMainSortMode mode)
            {
                events.SetSelectedGameObject(opener.gameObject);
                opener.onClick.Invoke();
                Require(overlay.activeSelf &&
                    EventSystem.current.currentSelectedGameObject.transform.IsChildOf(overlay.transform) &&
                    options.All(option => option.navigation.mode == Navigation.Mode.Explicit),
                    "The main sort picker did not take keyboard focus.");
                options[(int)mode].onClick.Invoke();
                Require(!overlay.activeSelf && events.currentSelectedGameObject == opener.gameObject,
                    "Choosing a main sort did not restore the opener focus.");
                Require(deck.extra.SequenceEqual(untouchedExtra) && deck.side.SequenceEqual(untouchedSide),
                    "Main sorting moved cards in Extra or Side.");
                Require(deck.main.LastIndexOf(blueEyes) == deck.main.IndexOf(blueEyes) + 1,
                    "Main sorting split identical copies.");
            }

            Select(DeckMainSortMode.Frame);
            Require(deck.main.IndexOf(blueEyes) < deck.main.IndexOf(goblinAttackForce) &&
                deck.main.IndexOf(goblinAttackForce) < deck.main.IndexOf(reborn) && deck.main.IndexOf(reborn) < deck.main.IndexOf(mirrorForce),
                "Frame sorting failed to separate normal, effect, spell and trap frames.");
            Select(DeckMainSortMode.Name);
            Require(deck.main.Select(CardDatabase.GetName).SequenceEqual(
                deck.main.Select(CardDatabase.GetName).OrderBy(name => name, StringComparer.Ordinal)), "Name sorting is not ascending.");
            Select(DeckMainSortMode.Attack);
            Require(deck.main.Take(4).SequenceEqual(new[] { blueEyes, blueEyes, goblinAttackForce, mysticalElf }),
                "Attack sorting did not rank known numbers ahead of unknown values.");
            var highestAttack = deck.main.ToArray();
            deck.main.Reverse();
            Call(editor, "RefreshDeckGrids");
            Select(DeckMainSortMode.Attack);
            Require(deck.main.SequenceEqual(highestAttack), "The same sort could not be applied again after changing the order.");
            Select(DeckMainSortMode.Defense);
            Require(deck.main.Take(3).SequenceEqual(new[] { blueEyes, blueEyes, mysticalElf }),
                "Defense sorting used the wrong stat.");
            Select(DeckMainSortMode.Level);
            Require(deck.main.Where(code => CardDatabase.Get(code).IsMonster).Select(code => CardDatabase.Get(code).level)
                .SequenceEqual(deck.main.Where(code => CardDatabase.Get(code).IsMonster).Select(code => CardDatabase.Get(code).level)
                    .OrderByDescending(level => level)), "Level sorting did not prioritize higher levels.");

            var editorType = editor.GetType();
            var padArea = editorType.GetField("_padArea", Instance);
            padArea.SetValue(editor, Enum.Parse(padArea.FieldType, "Main"));
            editorType.GetField("_padMode", Instance).SetValue(editor, true);
            int[] indices = Field<int[]>(editor, "_padIndices");
            int mainArea = (int)padArea.GetValue(editor);
            indices[mainArea] = deck.main.IndexOf(mysticalElf);
            var assembly = editorType.Assembly;
            var frame = assembly.GetType("YGOPortable.UI.DeckPadFrame", true);
            var buttons = assembly.GetType("YGOPortable.UI.DeckPadButtons", true);
            void Pad(string button) => Call(editor, "HandleDeckPad",
                Activator.CreateInstance(frame, Instance, null, new object[] { Vector2Int.zero, Enum.Parse(buttons, button), 0f }, null));
            Pad("Alternate");
            Require(overlay.activeSelf, "X did not open the main-deck sort picker.");
            events.SetSelectedGameObject(options[(int)DeckMainSortMode.Attack].gameObject);
            Pad("Submit");
            Require(!overlay.activeSelf && deck.main.SequenceEqual(highestAttack) &&
                deck.main[indices[mainArea]] == mysticalElf,
                "Controller sorting did not preserve the selected card or apply the highlighted mode.");
            padArea.SetValue(editor, Enum.Parse(padArea.FieldType, "Trunk"));
            var trunkSort = Field<Dropdown>(editor, "_sortFilter");
            int before = trunkSort.value;
            Pad("Alternate");
            Require(!overlay.activeSelf && trunkSort.value == (before + 1) % trunkSort.options.Count &&
                deck.main.SequenceEqual(highestAttack), "X changed the main deck while the trunk was selected.");

            Field<InputField>(editor, "_searchInp").text = "";
            var trunk = Field<List<CardInfo>>(editor, "_trunkResults");
            int frameOption = trunkSort.options.FindIndex(option => option.text == "카드 프레임별");
            Require(frameOption >= 0 && trunk.Select(card => card.FrameSpriteName).Distinct().Count() > 1,
                "The trunk did not expose frame sorting on a mixed collection.");
            var expectedTrunk = DeckMainSorter.Order(trunk.Select(card => card.code), DeckMainSortMode.Frame);
            trunkSort.value = frameOption;
            Require(trunk.Select(card => card.code).SequenceEqual(expectedTrunk) && deck.main.SequenceEqual(highestAttack),
                "Sorting the trunk by frame changed its contents or modified the main deck.");

            uint fusion = CardDatabase.AllCards.First(card => card.FrameSpriteName == "card_yugo").code;
            uint link = CardDatabase.AllCards.First(card => card.FrameSpriteName == "card_link").code;
            deck.extra = new List<uint> { link, fusion };
            deck.side = new List<uint> { mirrorForce, reborn, blueEyes };
            var expectedMain = DeckMainSorter.Order(deck.main, DeckMainSortMode.Frame);
            Call(editor, "RefreshDeckGrids");
            padArea.SetValue(editor, Enum.Parse(padArea.FieldType, "Controls"));
            var allSort = Field<Button>(editor, "_wholeSortButton");
            var allOptions = Field<Button[]>(editor, "_wholeSortOptions");
            events.SetSelectedGameObject(allSort.gameObject);
            allSort.onClick.Invoke();
            Require(overlay.activeSelf && allOptions.All(button => button.gameObject.activeInHierarchy) &&
                options.All(button => !button.gameObject.activeInHierarchy),
                "Whole-deck sort showed incorrect choices.");
            events.SetSelectedGameObject(allOptions[1].gameObject);
            Pad("Submit");
            Require(!overlay.activeSelf && events.currentSelectedGameObject == allSort.gameObject &&
                deck.main.SequenceEqual(expectedMain) && deck.extra.SequenceEqual(new[] { fusion, link }) &&
                deck.side.SequenceEqual(new[] { blueEyes, reborn, mirrorForce }),
                "Whole-deck frame sorting did not sort all three sections independently or restore focus.");

            allSort.onClick.Invoke();
            allOptions[0].onClick.Invoke();
            Require(!overlay.activeSelf && deck.side.Select(CardDatabase.GetName).SequenceEqual(
                deck.side.Select(CardDatabase.GetName).OrderBy(name => name, StringComparer.Ordinal)),
                "The original whole-deck sort did not remain available.");
        }

        private static void VerifyTouchDeckMenu(DeckEditorUI editor, EventSystem events)
        {
            var slots = Field<List<(RectTransform rt, CompositeCardView card, Text count, CardDragTrigger trigger)>>(editor, "_trunkPool");
            var card = slots.First(slot => slot.rt.gameObject.activeInHierarchy && slot.card.CurrentCode != 0);
            var overlay = Field<GameObject>(editor, "_deckCardActions");
            var finger = new PointerEventData(events) { pointerId = 0, button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(card.trigger.gameObject, finger, ExecuteEvents.pointerClickHandler);
            var details = Field<Button>(editor, "_deckCardActionDetails");
            Require(overlay.activeInHierarchy && events.currentSelectedGameObject == details.gameObject,
                "The card menu did not start on the safe inspection action.");
            var deck = Field<UserDeck>(editor, "_workingDeck");
            var before = deck.main.Concat(deck.extra).Concat(deck.side).ToArray();
            var assembly = editor.GetType().Assembly;
            var frame = assembly.GetType("YGOPortable.UI.DeckPadFrame", true);
            var buttons = assembly.GetType("YGOPortable.UI.DeckPadButtons", true);
            Call(editor, "HandleDeckPad", Activator.CreateInstance(frame, Instance, null,
                new object[] { Vector2Int.zero, Enum.Parse(buttons, "Submit"), 0f }, null));
            Require(!overlay.activeSelf && Field<RectTransform>(editor, "_expandedDeckOverlay").gameObject.activeInHierarchy &&
                before.SequenceEqual(deck.main.Concat(deck.extra).Concat(deck.side)),
                "Controller confirm on the default card action changed the deck instead of inspecting.");
            Call(editor, "CloseExpandedDeckCard");
            var mouse = new PointerEventData(events) { pointerId = -1, button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(card.trigger.gameObject, mouse, ExecuteEvents.pointerClickHandler);
            Require(!overlay.activeSelf, "Mouse preview unexpectedly opened the touch card menu.");

            var scroll = Field<ScrollRect>(editor, "_trunkScroll");
            var previousSize = scroll.content.sizeDelta;
            try
            {
                scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, scroll.viewport.rect.height + 120f);
                finger.pressPosition = new Vector2(180f, 200f);
                finger.position = new Vector2(180f, 260f);
                card.trigger.OnBeginDrag(finger);
                Require(Field<ScrollRect>(card.trigger, "_parentScroll") == scroll,
                    "A vertical swipe over a deck card did not scroll its list.");
                ExecuteEvents.Execute(card.trigger.gameObject, finger, ExecuteEvents.pointerClickHandler);
                Require(!overlay.activeSelf, "Finishing a swipe opened the card action menu.");
                card.trigger.OnEndDrag(finger);
            }
            finally
            {
                scroll.content.sizeDelta = previousSize;
            }
        }


        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Instance).GetValue(target);

        private static void Call(object target, string method, params object[] args) => CallResult(target, method, args);

        private static object CallResult(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Instance).Invoke(target, args);

        private static void Require(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
        }
    }
}
