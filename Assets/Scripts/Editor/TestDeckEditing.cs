using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
                editor.Open();
                VerifyTransfer(editor);
                VerifyLegacyDeck(editor);
                VerifyConfirmation(editor, events);
                VerifyArchetypeBrowser(editor, events);
                VerifyGroupingAndTrunk(editor, streamingPath, Path.Combine(save, "trunk"));
                VerifyMainSorting(editor, events);
                VerifyTouchPad(canvas.transform, events);
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
            Debug.Log("[VERIFY DECK EDITING] Transfer continuity, illegal moves, oversized deck navigation, modal focus, grouped copies, live trunk counts, main sort controls and touch pad passed.");
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
            Call(editor, "RefreshDeckGrids");
            Canvas.ForceUpdateCanvases();
            var scroll = Field<ScrollRect>(editor, "_mainDeckScroll");
            var mainSlots = Field<RectTransform>(editor, "_mainDeckSlotGrid");
            var sideSlots = Field<RectTransform>(editor, "_sideGridRect");
            VerifyEightColumns(mainSlots, "Main");
            VerifyEightColumns(sideSlots, "Side");
            float slotHeight = ((RectTransform)mainSlots.Find("Card_0")).rect.height;
            Require(Mathf.Abs(scroll.content.rect.height - scroll.viewport.rect.height * 1.25f) < .1f,
                "A 40-card deck did not have five rows at eight cards per row.");

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

        private static void VerifyGroupingAndTrunk(DeckEditorUI editor, string streamingPath, string save)
        {
            editor.Close();
            DeckRepository.Load(streamingPath, save, seedDefaultDecks: false);
            CollectionProgress.Load(save);
            var selection = CollectionProgress.Starters.First(option => option.Name.Contains("상검"));
            Require(CollectionProgress.TryChooseStarter(selection.Id, out var starter, out string error), error);
            uint repeated = starter.main.GroupBy(code => code).First(group => group.Count() == 3).Key;
            uint other = starter.main.First(code => CollectionProgress.GetDeckCopyKey(code) != CollectionProgress.GetDeckCopyKey(repeated));
            var saved = new UserDeck { name = "Split copies fixture", main = new List<uint> { repeated, other, repeated } };
            DeckRepository.SaveUserDeck(saved);
            editor.Open();

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
        }

        private static string TrunkCount(DeckEditorUI editor, uint code) =>
            Field<List<(RectTransform rt, CompositeCardView card, Text count, CardDragTrigger trigger)>>(editor, "_trunkPool")
                .Single(slot => slot.rt.gameObject.activeInHierarchy && slot.card.CurrentCode == code).count.text;

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

        private static void VerifyTouchPad(Transform canvas, EventSystem events)
        {
            const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
            var assembly = typeof(GameAppController).Assembly;
            var touchPad = assembly.GetType("YGOPortable.UI.TouchGamepad", true);
            var padInput = assembly.GetType("YGOPortable.UI.DeckPadInput", true);
            var buttons = assembly.GetType("YGOPortable.UI.DeckPadButtons", true);
            var pad = (Component)touchPad.GetMethod("Create", Static).Invoke(null, new object[] { canvas });
            try
            {
                var controls = pad.transform.Find("SafeArea/Controls");
                var toggle = pad.transform.Find("SafeArea/Toggle").gameObject;
                var firstFinger = new PointerEventData(events) { pointerId = 0 };
                var secondFinger = new PointerEventData(events) { pointerId = 1 };
                var input = Activator.CreateInstance(padInput, nonPublic: true);
                var read = padInput.GetMethod("AdvanceWithTouch", Instance);
                object Sample(float time, string physical = "None", bool related = true) => read.Invoke(input,
                    new object[] { Vector2.zero, Enum.Parse(buttons, physical), time, 0f, 0f, related });
                T Read<T>(object frame, string name) => (T)frame.GetType().GetProperty(name, Instance).GetValue(frame);
                void Down(string key, PointerEventData finger) =>
                    ExecuteEvents.Execute(controls.Find(key).gameObject, finger, ExecuteEvents.pointerDownHandler);
                void Up(string key, PointerEventData finger) =>
                    ExecuteEvents.Execute(controls.Find(key).gameObject, finger, ExecuteEvents.pointerUpHandler);

                Sample(0f);
                Down("Left", firstFinger);
                Down("A", secondFinger);
                var combined = Sample(.1f, "Search");
                Require(Read<bool>(combined, "Submit") && Read<bool>(combined, "Search") &&
                    Read<Vector2Int>(combined, "Direction") == Vector2Int.left,
                    "Touch directions and action did not combine with physical button input.");
                Up("A", secondFinger);
                var repeated = Sample(.5f);
                Require(!Read<bool>(repeated, "Submit") && Read<Vector2Int>(repeated, "Direction") == Vector2Int.left,
                    "Releasing one touch disturbed the other touch's direction repeat.");
                Up("Left", firstFinger);
                Down("X", firstFinger);
                Require(Read<bool>(Sample(.6f), "Alternate"), "The top face button did not use the deck sorting input.");
                Up("X", firstFinger);
                Down("ZR", firstFinger);
                Require(!Read<bool>(Sample(.7f, related: false), "RelatedCards") &&
                    Read<bool>(Sample(.8f), "RelatedCards"), "The related card shortcut was not scoped to the deck editor.");
                Down("ScrollUp", secondFinger);
                Require(Read<float>(Sample(.9f), "DescriptionScroll") > 0f, "The touch pad did not scroll card details.");
                ExecuteEvents.Execute(toggle, firstFinger, ExecuteEvents.pointerClickHandler);
                Require(!controls.gameObject.activeSelf && !Read<bool>(Sample(1f), "HasInput"),
                    "Hiding the touch pad left a virtual control held.");
                ExecuteEvents.Execute(toggle, firstFinger, ExecuteEvents.pointerClickHandler);
                Require(controls.gameObject.activeSelf && !Read<bool>(Sample(1.1f), "HasInput"),
                    "Showing the touch pad restored an earlier touch.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(pad.gameObject);
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
