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
            Debug.Log("[VERIFY DECK EDITING] Transfer continuity, illegal moves, oversized deck navigation, modal focus, grouped copies and live trunk counts passed.");
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
            saved.main.AddRange(Enumerable.Repeat(46986414u, 60));
            Call(editor, "RefreshDeckGrids");
            var area = editor.GetType().GetField("_padArea", Instance);
            var main = Enum.Parse(area.FieldType, "Main");
            area.SetValue(editor, main);
            var indices = Field<int[]>(editor, "_padIndices");
            indices[(int)main] = 60;
            Call(editor, "DrawPadCardFocus");
            Require(indices[(int)main] == 59 && saved.main.Count == 61,
                "An oversized legacy deck broke focus or dropped its unrendered card.");
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
