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
                VerifyArchetypeSearch(editor);
                VerifyArchetypeCodex(canvas.transform, events);
                VerifyMainSorting(editor, events);
                VerifyTouchDeckMenu(editor, events);
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
            saved.extra.AddRange(Enumerable.Repeat(23995346u, 9));
            saved.side.Add(46986414u);
            Call(editor, "RefreshDeckGrids");
            Canvas.ForceUpdateCanvases();
            var scroll = Field<ScrollRect>(editor, "_mainDeckScroll");
            var mainSlots = Field<RectTransform>(editor, "_mainDeckSlotGrid");
            var extraSlots = Field<RectTransform>(editor, "_extraGridRect");
            var sideSlots = Field<RectTransform>(editor, "_sideGridRect");
            VerifyEightColumns(mainSlots, "Main");
            VerifyEightColumns(extraSlots, "Extra");
            VerifyEightColumns(sideSlots, "Side");
            var trunk = Field<RectTransform>(editor, "_trunkContent");
            Require(((RectTransform)trunk.Find("TrunkCard/CardInset/Card/AspectBox")).rect.width >= 82f,
                "The trunk artwork became too small.");
            VerifyDeckArtwork(mainSlots, 85f, "Main");
            VerifyDeckArtwork(extraSlots, 73f, "Extra");
            VerifyDeckArtwork(sideSlots, 72f, "Side");
            float slotHeight = ((RectTransform)mainSlots.Find("Card_0")).rect.height;
            Require(Mathf.Abs(scroll.content.rect.height - scroll.viewport.rect.height * 2.5f) < .1f,
                "A 40-card deck did not have five rows at eight cards per row.");
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
            saved.extra.Clear();
            saved.side.Clear();
            Call(editor, "RefreshDeckGrids");
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
            var sideGrid = Field<RectTransform>(editor, "_sideGridRect");
            var mainBadge = mainGrid.Find($"Card_{deck.main.IndexOf(code)}/CardInset/Card/AspectBox/Unowned");
            var sideBadge = sideGrid.Find("Card_0/CardInset/Card/AspectBox/Unowned");
            var preview = Field<Text>(editor, "_previewOwnership");
            Call(editor, "InspectCard", code);
            Require(!mainBadge.gameObject.activeInHierarchy && !sideBadge.gameObject.activeInHierarchy &&
                TrunkCount(editor, code) == "×0", "Spending all available copies in the deck marked the card as unowned.");

            Call(editor, "OpenCardCrafting", code);
            Require(CollectionProgress.TryDisassembleCards(new Dictionary<uint, int> { [code] = 3 }, out string error), error);
            Call(editor, "ReturnFromCardCrafting");
            Require(mainBadge.gameObject.activeInHierarchy && sideBadge.gameObject.activeInHierarchy &&
                TrunkCount(editor, code) == "미보유" && preview.text.Contains("미보유"),
                "Disassembling the last copy did not refresh the deck, card list and preview.");

            Call(editor, "OpenCardCrafting", code);
            Require(CollectionProgress.TryCraftCard(code, 1, out error), error);
            Call(editor, "ReturnFromCardCrafting");
            Require(!mainBadge.gameObject.activeInHierarchy && !sideBadge.gameObject.activeInHierarchy &&
                TrunkCount(editor, code) == "×0" && preview.text.Contains("보유 1장"),
                "Crafting a copy did not remove the unowned markers.");
            Call(editor, "SetTrunkSource", 0u, null);
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

        private static void VerifyTouchDeckMenu(DeckEditorUI editor, EventSystem events)
        {
            var slots = Field<List<(RectTransform rt, CompositeCardView card, Text count, CardDragTrigger trigger)>>(editor, "_trunkPool");
            var card = slots.First(slot => slot.rt.gameObject.activeInHierarchy && slot.card.CurrentCode != 0);
            var overlay = Field<GameObject>(editor, "_deckCardActions");
            var finger = new PointerEventData(events) { pointerId = 0, button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(card.trigger.gameObject, finger, ExecuteEvents.pointerClickHandler);
            Require(overlay.activeInHierarchy, "Tapping a card did not open its deck actions.");
            overlay.transform.Find("Window/Cancel/Button").GetComponent<Button>().onClick.Invoke();
            Require(!overlay.activeSelf, "The touch card menu could not be dismissed.");
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
