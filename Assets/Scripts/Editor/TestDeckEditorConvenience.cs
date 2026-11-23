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
    internal static class TestDeckEditorConvenience
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const uint Magician = 46986414;
        private const uint BlueEyes = 89631139;
        private const uint Pot = 55144522;
        private const uint Ash = 14558127;
        private const uint MirrorForce = 44095762;
        private const uint DragonKnight = 41721210;
        private const uint Excel = 9674034;
        private const uint Diabellstar = 72270339;

        internal static void Run(string streaming)
        {
            bool previousAccess = CollectionProgress.AllDeckCardsAvailable;
            string save = Path.Combine(Path.GetTempPath(), "ygo-deck-qol-" + Guid.NewGuid().ToString("N"));
            DeckRepository.Load(streaming, save, seedDefaultDecks: false);
            DeckRepository.SaveUserDeck(new UserDeck { name = "QoL fixture", main = { Magician } });
            CollectionProgress.AllDeckCardsAvailable = true;
            var canvas = new GameObject("Deck convenience canvas", typeof(Canvas), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600, 900);
            var events = new GameObject("Deck convenience input", typeof(EventSystem)).GetComponent<EventSystem>();
            if (EventSystem.current == null) Call(events, "OnEnable");
            DeckEditorUI editor = null;
            DeckEditorUI mobile = null;
            try
            {
                var app = canvas.AddComponent<GameAppController>();
                editor = new DeckEditorUI(app, canvas.transform);
                TestDeckEditing.OpenSavedDeck(editor);
                VerifyDesktopLayout(editor);
                VerifyDesktopCardDescription(editor, (RectTransform)canvas.transform);
                VerifyQuickSearch(editor);
                VerifyQuantitiesAndSort(editor);
                VerifyRecipeActions(editor);
                VerifyOpeningHandAndComposition(editor);
                editor.Close();

                mobile = new DeckEditorUI(app, canvas.transform, mobileLayout: true);
                TestDeckEditing.OpenSavedDeck(mobile);
                VerifyMobile(mobile, events);
            }
            finally
            {
                mobile?.Close();
                editor?.Close();
                UnityEngine.Object.DestroyImmediate(events.gameObject);
                UnityEngine.Object.DestroyImmediate(canvas);
                CollectionProgress.AllDeckCardsAvailable = previousAccess;
            }
            Debug.Log("[VERIFY DECK QOL] Desktop pane navigation, related cards, chips, quantities, autosort, recipe actions, test draws and mobile hit areas passed.");
        }

        private static void VerifyDesktopLayout(DeckEditorUI editor)
        {
            var details = (RectTransform)editor.Root.transform.Find("Editor/CardDetails");
            var deck = Field<RectTransform>(editor, "_deckPanel");
            var trunk = Field<RectTransform>(editor, "_trunkPanel");
            Require(details.anchorMax.x < deck.anchorMin.x && deck.anchorMax.x < trunk.anchorMin.x,
                "The desktop editor must place the inspector, deck, and collection in that order.");
            Require(Field<Text>(editor, "_dustBalance").text == $"제작 가루 {CollectionProgress.CraftDust:N0}",
                "The deck editor wallet did not show the shared crafting balance.");

            Call(editor, "NavigatePadCards", Vector2Int.left, false);
            Require(Field<object>(editor, "_padArea").ToString() == "Main",
                "Moving left from the collection did not focus the center deck.");
            Call(editor, "NavigatePadCards", Vector2Int.right, false);
            Require(Field<object>(editor, "_padArea").ToString() == "Trunk",
                "Moving right from the deck did not return to the collection.");
        }

        private static void VerifyDesktopCardDescription(DeckEditorUI editor, RectTransform canvas)
        {
            var scroll = Field<ScrollRect>(editor, "_previewScroll");
            var effect = Field<Text>(editor, "_previewDesc");
            var ownership = Field<Text>(editor, "_previewOwnership").transform.parent;
            var track = (RectTransform)scroll.verticalScrollbar.transform;
            var longCard = CardDatabase.AllCards.First(card => card.desc?.Length > 500);
            var originalSize = canvas.sizeDelta;
            uint originalCard = Field<uint>(editor, "_inspectedCode");
            try
            {
                foreach (var size in new[] { new Vector2(1600f, 900f), new Vector2(1280f, 720f) })
                {
                    canvas.sizeDelta = size;
                    Canvas.ForceUpdateCanvases();
                    Call(editor, "InspectCard", Magician);
                    Call(editor, "InspectCard", longCard.code);
                    Require(ownership.gameObject.activeInHierarchy && scroll.viewport.GetComponent<RectMask2D>() != null &&
                        scroll.viewport.rect.height >= 5f * effect.fontSize,
                        $"The effect preview did not have five lines of space with ownership shown at {size}.");
                    Require(track.offsetMax.y == scroll.viewport.offsetMax.y && track.offsetMin.y == scroll.viewport.offsetMin.y,
                        "The scrollbar did not align to the description below the ownership row.");
                    Require(effect.text == longCard.desc && scroll.verticalScrollbar.gameObject.activeInHierarchy,
                        "The full effect text was not available to scroll.");
                    scroll.verticalNormalizedPosition = 0f;
                    Require(scroll.content.anchoredPosition.y > 0f, "The effect preview could not scroll to its end.");
                    Call(editor, "InspectCard", Magician);
                    Require(Mathf.Approximately(scroll.content.anchoredPosition.y, 0f),
                        "Changing cards retained the previous card's scroll position.");
                }
            }
            finally
            {
                canvas.sizeDelta = originalSize;
                Canvas.ForceUpdateCanvases();
                Call(editor, "InspectCard", originalCard);
            }
            Debug.Log("[VERIFY DESKTOP DESCRIPTION] Long effects, ownership, and scrolling passed at 1600x900 and 1280x720.");
        }

        private static void VerifyQuickSearch(DeckEditorUI editor)
        {
            var trunk = Field<List<CardInfo>>(editor, "_trunkResults");
            var inspector = editor.Root.transform.Find("Editor/CardDetails");
            Call(editor, "InspectCard", Magician);
            inspector.Find("RelatedCards/Button").GetComponent<Button>().onClick.Invoke();
            Require(new[] { Magician, 47222536u, 48680970u, DragonKnight }.All(
                code => trunk.Any(card => CardDatabase.GetDeckPrintingKey(card.code) == code)),
                "Dark Magician scope missed its spell, trap or fusion support.");
            Call(editor, "FilterRelatedCards", Excel);
            Require(new[] { Excel, Diabellstar, 89023486u, 80845034u, 45663742u }.All(
                code => trunk.Any(card => CardDatabase.GetDeckPrintingKey(card.code) == code)),
                "Snake-Eye scope missed Diabellstar, Sinful Spoils or other Snake-Eye cards.");

            Call(editor, "ResetTrunkFilters");
            var chips = Field<Button[]>(editor, "_quickFilterButtons");
            chips[1].onClick.Invoke();
            Require(new[] { Ash, 23434538u, 10045474u, 97268402u, 27204311u }.All(code => trunk.Any(card => card.code == code)) &&
                trunk.All(card => !card.IsExtra), "Hand-trap quick filter is inaccurate.");
            chips[2].onClick.Invoke();
            Require(new[] { 25311006u, 15693423u, 14532163u, 18144506u }.All(code => trunk.Any(card => card.code == code)) &&
                !trunk.Any(card => card.code == Ash), "Board-breaker quick filter is inaccurate.");
            var types = new[] { YgoConstants.TYPE_TUNER, YgoConstants.TYPE_FUSION, YgoConstants.TYPE_SYNCHRO,
                YgoConstants.TYPE_XYZ, YgoConstants.TYPE_LINK };
            for (int i = 0; i < types.Length; i++)
            {
                chips[i + 3].onClick.Invoke();
                Require(trunk.Count > 0 && trunk.All(card => (card.type & types[i]) != 0),
                    $"Quick type filter {i} returned incorrect card types.");
            }

            var strip = chips[0].GetComponentInParent<ScrollRect>();
            var pad = Field<DeckPadUi>(editor, "_padUi");
            strip.horizontalNormalizedPosition = 0f;
            pad.FocusInScope(editor.Root.transform, chips[^1]);
            Canvas.ForceUpdateCanvases();
            Require(strip.horizontalNormalizedPosition > 0f, "Controller focus did not reveal a clipped filter chip.");
            Call(editor, "ResetTrunkFilters");

            var trunkPanel = Field<RectTransform>(editor, "_trunkPanel");
            var search = Field<InputField>(editor, "_searchInp");
            search.text = Pot.ToString();
            var slots = Field<List<(RectTransform rt, CompositeCardView card, Text count, CardDragTrigger trigger)>>(editor, "_trunkPool");
            var view = slots.Single(slot => slot.card.CurrentCode == Pot && slot.rt.gameObject.activeInHierarchy).card;
            Require(trunkPanel.Find("RegulationLegend") == null &&
                view.transform.Find("AspectBox/Regulation") == null,
                "The trunk still showed an OCG reference block.");
            Call(editor, "ResetTrunkFilters");
        }

        private static void VerifyQuantitiesAndSort(DeckEditorUI editor)
        {
            var deck = Field<UserDeck>(editor, "_workingDeck");
            deck.main.Clear();
            deck.side.Clear();
            Call(editor, "RefreshDeckGrids");
            var search = Field<InputField>(editor, "_searchInp");
            search.text = Magician.ToString();
            var pool = Field<List<(RectTransform rt, CompositeCardView card, Text count, CardDragTrigger trigger)>>(editor, "_trunkPool");
            var trunk = pool.Single(slot => slot.rt.gameObject.activeInHierarchy && slot.card.CurrentCode == Magician);
            var max = Field<Button>(editor, "_deckQuantityMax");
            var plus = Field<Button>(editor, "_deckQuantityPlus");
            var minus = Field<Button>(editor, "_deckQuantityMinus");
            trunk.trigger.OnTouchClick();
            plus.onClick.Invoke();
            Require(deck.main.SequenceEqual(new[] { Magician }), "The trunk + did not add exactly one copy.");
            trunk.trigger.OnTouchClick();
            max.onClick.Invoke();
            Require(deck.main.Count == 3 && deck.main.All(code => code == Magician), "MAX did not add all available copies.");
            var main = Field<RectTransform>(editor, "_mainDeckSlotGrid");
            main.Find("Card_1").GetComponent<CardDragTrigger>().OnTouchClick();
            Require(!plus.interactable && !max.interactable && minus.interactable, "The same-name copy limit did not disable quantity buttons.");
            minus.onClick.Invoke();
            Require(deck.main.Count == 2, "The deck − did not remove one selected copy.");
            Call(editor, "MoveDeckCard", DragSourceType.DeckMain, 1, DragSourceType.DeckSide, -1, null);
            trunk.trigger.OnTouchClick();
            max.onClick.Invoke();
            Require(deck.main.Count == 2 && deck.side.Count == 1, "MAX did not count Side Deck copies.");
            Call(editor, "AddDeckCopies", Pot, DragSourceType.DeckMain, 3, null, null);
            Require(deck.main.Count(code => code == Pot) == 3,
                "MAX did not preserve the existing local three-copy rule.");
            var badge = main.Find($"Card_{deck.main.IndexOf(Pot)}/CardInset/Card/AspectBox/Regulation");
            Require(badge == null, "The deck card still showed an OCG reference badge.");
            var playable = new UserDeck
            {
                main = Enumerable.Repeat(Pot, 3).Concat(CardDatabase.AllCards.Where(card => !card.IsExtra &&
                    card.alias == 0 && card.code != Pot && CollectionProgress.IsCraftableCard(card))
                    .Select(card => card.code).Distinct().Take(37)).ToList(),
            };
            Require(CollectionProgress.CanUseDeck(playable, out string reason), $"The local deck could not duel. {reason}");
            playable.side.Add(Pot);
            Require(!CollectionProgress.CanUseDeck(playable, out reason) && reason.Contains("3장"),
                "The same-name cap did not include the Side Deck during duel validation.");

            uint AnyCard(uint flags) => CardDatabase.AllCards.First(card => card.alias == 0 &&
                (card.type & flags) == flags && CollectionProgress.IsCraftableCard(card)).code;
            var spellFlags = new[] { 0u, YgoConstants.TYPE_QUICKPLAY, YgoConstants.TYPE_CONTINUOUS,
                YgoConstants.TYPE_EQUIP, YgoConstants.TYPE_FIELD, YgoConstants.TYPE_RITUAL };
            uint[] spells = spellFlags.Select(flag => flag == 0 ? Pot : AnyCard(YgoConstants.TYPE_SPELL | flag)).ToArray();
            var trapFlags = new[] { 0u, YgoConstants.TYPE_CONTINUOUS, YgoConstants.TYPE_COUNTER };
            uint[] traps = trapFlags.Select(flag => flag == 0 ? MirrorForce : AnyCard(YgoConstants.TYPE_TRAP | flag)).ToArray();
            uint[] extra = { DragonKnight, AnyCard(YgoConstants.TYPE_SYNCHRO), AnyCard(YgoConstants.TYPE_XYZ),
                AnyCard(YgoConstants.TYPE_LINK) };
            uint[] expected = new[] { BlueEyes, Magician, 74677422u, Ash }.Concat(spells).Concat(traps).ToArray();
            deck.main = expected.Reverse().ToList();
            deck.extra = extra.Reverse().ToList();
            editor.Root.transform.Find("Editor/Deck/EditControls/AutoSort/Button").GetComponent<Button>().onClick.Invoke();
            Require(deck.main.SequenceEqual(expected) && deck.extra.SequenceEqual(extra),
                "The automatic sort did not apply the requested monster, spell, trap and extra-deck order.");
        }

        private static void VerifyRecipeActions(DeckEditorUI editor)
        {
            var source = DeckRepository.UserDecks.Single();
            Call(editor, "OpenDeckTools");
            var tools = Field<RectTransform>(editor, "_deckToolsOverlay");
            tools.Find("Window/Duplicate/Button").GetComponent<Button>().onClick.Invoke();
            var copy = DeckRepository.UserDecks[1];
            Require(source.main.SequenceEqual(new[] { Magician }) && copy.id != source.id &&
                copy.main.Count > source.main.Count && copy.side.Count == 1 && copy.extra.Count == 4,
                "Duplicating did not preserve the saved original or all copied sections.");

            var working = Field<UserDeck>(editor, "_workingDeck");
            Require(DeckRecipe.TryImport(DeckRecipe.ExportText(copy), out var fromText, out string textError) &&
                fromText.main.SequenceEqual(copy.main) && fromText.extra.SequenceEqual(copy.extra) &&
                fromText.side.SequenceEqual(copy.side) && fromText.name == copy.name,
                $"Readable recipe export/import did not round-trip the source. {textError}");
            Require(DeckRecipe.TryImport(DeckRepository.ExportToYdk(copy), out var fromYdk, out _) &&
                fromYdk.main.SequenceEqual(copy.main) && fromYdk.extra.SequenceEqual(copy.extra) &&
                fromYdk.side.SequenceEqual(copy.side), "YDK recipe export/import did not round-trip the code sections.");
            Require(DeckRecipe.TryImport("메인 덱 (4장)\n2x 블랙 매지션\n푸른 눈의 백룡 × 2\n엑스트라 덱\n용기사 블랙 매지션",
                out var byName, out _) && byName.main.SequenceEqual(new[] { Magician, Magician, BlueEyes, BlueEyes }) &&
                byName.extra.SequenceEqual(new[] { DragonKnight }), "Community card names and quantity formats were not parsed.");
            Require(!DeckRecipe.TryImport("#main\n46986414\nDoes not exist", out var invalid, out _) && invalid == null,
                "A bad recipe returned a deck with only part of its cards.");
            Require(!DeckRecipe.TryImport("#extra\n46986414", out _, out _) &&
                !DeckRecipe.TryImport("#main\n61x 46986414", out _, out _),
                "A bad recipe ignored its section or maximum size.");

            Call(editor, "OpenDeckTools");
            tools.Find("Window/Clear/Button").GetComponent<Button>().onClick.Invoke();
            Require(working.main.Count > 0 && working.extra.Count > 0, "The clear action bypassed confirmation.");
            Field<Button>(editor, "_deckDialogCancel").onClick.Invoke();
            Require(working.main.Count > 0 && working.extra.Count > 0, "Cancelling clear changed the deck.");
            tools.Find("Window/Clear/Button").GetComponent<Button>().onClick.Invoke();
            Field<Button>(editor, "_deckDialogPrimary").onClick.Invoke();
            Require(working.main.Count == 0 && working.extra.Count == 0 && working.side.Count == 1 && copy.main.Count > 0,
                "Clearing cards affected the side deck or modified the saved copy before Save.");

            Call(editor, "OpenDeckTools");
            Field<InputField>(editor, "_deckRecipeInput").SetTextWithoutNotify(DeckRecipe.ExportText(copy));
            tools.Find("Window/Import/Button").GetComponent<Button>().onClick.Invoke();
            Field<Button>(editor, "_deckDialogSecondary").onClick.Invoke();
            var imported = Field<UserDeck>(editor, "_workingDeck");
            Require(DeckRepository.UserDecks.Count == 3 && imported.id != copy.id && imported.main.SequenceEqual(copy.main),
                "Importing with pending edits did not create a separate saved deck.");
            DeckRepository.ReloadUserDecks();
            var persisted = DeckRepository.UserDecks.Single(deck => deck.id == imported.id);
            Require(persisted.main.SequenceEqual(imported.main) && persisted.extra.SequenceEqual(imported.extra) &&
                persisted.side.SequenceEqual(imported.side) &&
                DeckRepository.UserDecks.Single(deck => deck.id == source.id).main.SequenceEqual(source.main),
                "The imported deck or its original changed when reading saved files back.");
        }

        private static void VerifyOpeningHandAndComposition(DeckEditorUI editor)
        {
            var deck = Field<UserDeck>(editor, "_workingDeck");
            const uint blueEyesPrinting = 89631140;
            var originals = new[] { BlueEyes, blueEyesPrinting, Magician, Ash, Pot, MirrorForce, 18144506u };
            deck.main = new List<uint>(originals);
            Call(editor, "RefreshDeckGrids");
            var counts = Field<Text[]>(editor, "_compositionCounts");
            var spans = Field<RectTransform[]>(editor, "_compositionSegments");
            Require(counts[0].text == "몬스터 4" && counts[1].text == "마법 2" && counts[2].text == "함정 1" &&
                Mathf.Abs(spans[0].anchorMax.x - 4f / 7f) < .001f && Mathf.Abs(spans[2].anchorMin.x - 6f / 7f) < .001f,
                "Deck composition labels or ratios did not reflect current edits.");
            Require(Field<Text>(editor, "_extraCountText").text.EndsWith("남은 11칸") &&
                Field<Text>(editor, "_sideCountText").text.EndsWith("남은 14칸"),
                "Extra and Side Deck capacity did not reflect current edits.");
            Field<Button>(editor, "_openDrawButton").onClick.Invoke();
            var deckDraw = Field<RectTransform>(editor, "_deckDrawOverlay");
            var cards = Field<Button[]>(editor, "_openingHandButtons");
            uint[] sample = Field<uint[]>(editor, "_openingHand").ToArray();
            Require(cards.Count(button => button.gameObject.activeSelf) == 5 && !cards[5].gameObject.activeSelf, "The first hand did not show five positions.");

            var odds = Field<Text>(editor, "_openingHandOdds");
            var standby = Field<Button>(editor, "_pickStartersButton");
            Require(odds.text == "초동을 고르면 확률을 표시합니다", "A deck without marked starters reported a misleading miss percentage.");
            standby.onClick.Invoke();
            var picker = Field<RectTransform>(editor, "_starterPickerOverlay");
            var starter = picker.Find("Window/Card_0").GetComponent<Button>();
            Require(picker.gameObject.activeInHierarchy && !deckDraw.gameObject.activeSelf &&
                starter.transform.Find("Caption/Text").GetComponent<Text>().text.EndsWith("2장"),
                "Alternate printings did not share one starter entry, or the starter picker did not open.");
            starter.onClick.Invoke();
            Require(Field<Text>(editor, "_starterPickerPageLabel").text.EndsWith($"미포함 {100d / 21d:0.0}%"),
                "Changing the starter selection did not update the percentage immediately.");
            Field<Button>(editor, "_starterPickerDone").onClick.Invoke();
            Require(!picker.gameObject.activeSelf && deckDraw.gameObject.activeInHierarchy &&
                EventSystem.current.currentSelectedGameObject == standby.gameObject &&
                odds.text.StartsWith($"초동 미포함 {100d / 21d:0.0}%"),
                "Selecting the two printed copies did not calculate the 5-of-7 miss rate or restore controller focus.");
            var shown = sample.Take(5).ToArray();
            Require(Enumerable.Range(0, 5).All(i => (cards[i].image.color == DuelUiKit.GoldAccent) ==
                (CardDatabase.GetDeckPrintingKey(shown[i]) == BlueEyes)), "The sampled hand did not highlight the marked starter copies.");
            deckDraw.Find("Window/Second/Button").GetComponent<Button>().onClick.Invoke();
            Require(cards.All(button => button.gameObject.activeSelf) &&
                Field<Text>(editor, "_openingHandDrawLabel").gameObject.activeSelf &&
                Field<uint[]>(editor, "_openingHand").SequenceEqual(sample),
                "Going second did not reveal the sixth card independently of the opening hand.");
            Require(odds.text.StartsWith($"초동 미포함 {0d:0.0}%") &&
                odds.text.EndsWith($"초동 {sample.Count(code => CardDatabase.GetDeckPrintingKey(code) == BlueEyes)}장"),
                "The sixth card was omitted from the second player's sample or miss rate.");
            for (int i = 0; i < 10; i++)
            {
                Field<Button>(editor, "_redrawButton").onClick.Invoke();
                var draw = Field<uint[]>(editor, "_openingHand");
                Require(draw.Length == 6 && draw.GroupBy(code => code).All(group =>
                    group.Count() <= originals.Count(code => code == group.Key)), "A sample drew more physical copies than the main deck contains.");
            }
            Require(deck.main.SequenceEqual(originals), "Sample draws changed the editor's current cards.");
            cards[0].onClick.Invoke();
            Require(Field<RectTransform>(editor, "_expandedDeckOverlay").gameObject.activeSelf,
                "A sample hand card did not open its expanded inspector.");
            Call(editor, "CloseExpandedDeckCard");
            Call(editor, "CloseDeckUtility");
            deck.main.Add(74677422u);
            Field<Button>(editor, "_openDrawButton").onClick.Invoke();
            standby.onClick.Invoke();
            var next = Field<Button>(editor, "_starterNext");
            Require(next.interactable && starter.transform.Find("Caption/Text").GetComponent<Text>().text.EndsWith("✓"),
                "Adding a seventh choice lost the preserved selection or did not enable paging.");
            next.onClick.Invoke();
            Require(!next.interactable && !picker.Find("Window/Card_1").gameObject.activeSelf &&
                starter.GetComponentInChildren<CompositeCardView>().CurrentCode == 74677422u,
                "The next page omitted the final choice or showed stale cards.");
            Call(editor, "CloseDeckUtility");
            Call(editor, "CloseDeckUtility");
        }

        private static void VerifyMobile(DeckEditorUI editor, EventSystem events)
        {
            var safe = (RectTransform)editor.Root.transform.Find("MobileSafeArea");
            var root = safe.Find("Editor");
            var layout = safe.GetComponent<MobileSafeArea>();
            var area = new Rect(112, 48, 1376, 812);
            layout.Reflow(area, new Vector2Int(1600, 900));
            Canvas.ForceUpdateCanvases();
            Call(editor, "LayoutMobileEditor");
            var deck = Field<UserDeck>(editor, "_workingDeck");
            deck.main.Clear();
            deck.side.Clear();
            deck.extra.Clear();
            deck.main.Add(Magician);
            deck.extra.Add(DragonKnight);
            deck.side.Add(Pot);
            Call(editor, "RefreshDeckGrids");
            Canvas.ForceUpdateCanvases();
            var main = Field<RectTransform>(editor, "_mainDeckSlotGrid");
            var mainGrid = Field<RectTransform>(editor, "_mainGridRect");
            var extraGrid = Field<RectTransform>(editor, "_extraGridRect");
            var sideGrid = Field<RectTransform>(editor, "_sideGridRect");
            var trunkPane = Field<RectTransform>(editor, "_trunkPanel");
            var deckPane = Field<RectTransform>(editor, "_deckPanel");
            Require(deckPane.gameObject.activeInHierarchy && trunkPane.gameObject.activeInHierarchy &&
                !root.Find("CardDetails").gameObject.activeInHierarchy, "The mobile editor did not show the trunk and deck together.");
            Require(mainGrid.gameObject.activeInHierarchy && extraGrid.gameObject.activeInHierarchy && sideGrid.gameObject.activeInHierarchy,
                "The mobile editor did not show all three deck sections by default.");
            var sections = Field<Button[]>(editor, "_mobileSectionButtons");
            sections[1].onClick.Invoke();
            Require(!extraGrid.gameObject.activeInHierarchy && mainGrid.gameObject.activeInHierarchy &&
                sideGrid.gameObject.activeInHierarchy, "Collapsing the Extra Deck changed the other sections.");
            sections[1].onClick.Invoke();
            Require(extraGrid.gameObject.activeInHierarchy, "The Extra Deck could not be reopened.");
            var canvas = (RectTransform)editor.Root.transform.parent;
            VerifyMobileOverview(editor, canvas, layout, safe, new Rect(92, 22, 1150, 698), new Vector2Int(1334, 750));
            VerifyMobileOverview(editor, canvas, layout, safe, new Rect(0, 0, 1600, 900), new Vector2Int(1600, 900));
            VerifyMobileOverview(editor, canvas, layout, safe, area, new Vector2Int(1600, 900));
            Canvas.ForceUpdateCanvases();
            var deckArt = (RectTransform)main.Find("Card_0/CardInset/Card/AspectBox");
            Require(deckArt.Find("DeckCopies") == null, "The deck grid still covered its artwork with a duplicate count.");
            var finger = new PointerEventData(events)
            {
                pointerId = 0, button = PointerEventData.InputButton.Left, position = new Vector2(400, 270),
            };
            void Tap(CardDragTrigger trigger)
            {
                trigger.OnPointerDown(finger);
                trigger.OnPointerUp(finger);
                trigger.OnPointerClick(finger);
            }

            var sheet = Field<RectTransform>(editor, "_expandedDeckOverlay");
            var panel = Field<RectTransform>(editor, "_expandedDeckPanel");
            var effect = Field<Text>(editor, "_expandedDeckDescription");
            var actions = Field<GameObject>(editor, "_deckCardActions");
            var operations = panel.Find("Operations/Button").GetComponent<Button>();
            var close = Field<Button>(editor, "_expandedDeckClose");
            foreach (var (zone, grid, code) in new[]
            {
                (DragSourceType.DeckMain, main, Magician),
                (DragSourceType.DeckExtra, Field<RectTransform>(editor, "_extraDeckSlotGrid"), DragonKnight),
                (DragSourceType.DeckSide, Field<RectTransform>(editor, "_sideDeckSlotGrid"), Pot),
            })
            {
                Tap(grid.Find("Card_0").GetComponent<CardDragTrigger>());
                Require(sheet.gameObject.activeInHierarchy && !actions.activeSelf && effect.text == CardDatabase.Get(code).desc &&
                    Field<DragSourceType>(editor, "_mobileSelectedSource") == zone &&
                    deck.main.SequenceEqual(new[] { Magician }) && deck.extra.SequenceEqual(new[] { DragonKnight }) &&
                    deck.side.SequenceEqual(new[] { Pot }), "Tapping a deck card did not inspect its original zone without edits.");
                close.onClick.Invoke();
            }

            Tap(main.Find("Card_0").GetComponent<CardDragTrigger>());
            operations.onClick.Invoke();
            Require(actions.activeInHierarchy && !sheet.gameObject.activeSelf && deck.main.SequenceEqual(new[] { Magician }),
                "Opening deck operations did not preserve the selected copy.");
            Field<Button>(editor, "_deckCardActionAlternate").onClick.Invoke();
            Require(deck.main.Count == 0, "Explicitly removing the inspected card did not remove one copy.");

            var trunk = Field<RectTransform>(editor, "_trunkViewport");
            Require(deckPane.gameObject.activeInHierarchy && trunkPane.gameObject.activeInHierarchy,
                "Removing a card hid one of the two panes.");
            var chips = Field<Button[]>(editor, "_quickFilterButtons");
            Button Chip(string label) => chips.Single(button => button.GetComponentInChildren<Text>().text.TrimStart('•', ' ') == label);
            var results = Field<List<CardInfo>>(editor, "_trunkResults");
            Chip("마법").onClick.Invoke();
            Require(results.Count > 0 && results.All(card => (card.type & YgoConstants.TYPE_SPELL) != 0), "Spell chip did not filter.");
            Chip("전체").onClick.Invoke();
            Chip("속성 어둠").onClick.Invoke();
            Chip("마법사족").onClick.Invoke();
            Require(results.Count > 0 && results.All(card => card.attr == YgoConstants.ATTRIBUTE_DARK &&
                card.race == YgoConstants.RACE_SPELLCASTER), "Attribute and race chips did not combine.");
            Chip("전체").onClick.Invoke();
            foreach (var (name, type) in new[] { ("함정", YgoConstants.TYPE_TRAP), ("융합", YgoConstants.TYPE_FUSION),
                ("싱크로", YgoConstants.TYPE_SYNCHRO), ("엑시즈", YgoConstants.TYPE_XYZ), ("링크", YgoConstants.TYPE_LINK) })
            {
                Chip(name).onClick.Invoke();
                Require(results.Count > 0 && results.All(card => (card.type & type) != 0), $"{name} chip did not filter.");
                Chip("전체").onClick.Invoke();
            }

            var keyboard = new Rect(0f, 0f, 1600f, 400f);
            Call(editor, "ApplyMobileKeyboardLayout", keyboard, area);
            Canvas.ForceUpdateCanvases();
            var searchRow = (RectTransform)trunkPane.Find("SearchRow");
            float SafeBottom(RectTransform rect) =>
                safe.InverseTransformPoint(rect.TransformPoint(new Vector3(rect.rect.xMin, rect.rect.yMin, 0f))).y;
            float keyboardTop = safe.rect.yMin + (keyboard.yMax - area.yMin) * safe.rect.height / area.height;
            Require(SafeBottom(trunk) >= keyboardTop && SafeBottom(searchRow) >= keyboardTop &&
                trunk.rect.height > 200f && !deckPane.gameObject.activeInHierarchy && trunkPane.rect.width > .9f * safe.rect.width,
                $"The keyboard top {keyboardTop:F1} crossed the list at {SafeBottom(trunk):F1} or search at {SafeBottom(searchRow):F1}.");
            Call(editor, "ApplyMobileKeyboardLayout", Rect.zero, area);
            Require(deckPane.gameObject.activeInHierarchy && trunkPane.rect.width < .5f * safe.rect.width,
                "Dismissing the keyboard did not restore both panes.");
            var search = Field<InputField>(editor, "_searchInp");
            search.text = Magician.ToString();
            var pool = Field<List<(RectTransform rt, CompositeCardView card, Text count, CardDragTrigger trigger)>>(editor, "_trunkPool");
            var tile = pool.First(slot => slot.rt.gameObject.activeInHierarchy && slot.card.CurrentCode == Magician);
            string available = tile.count.text;
            Tap(tile.trigger);
            Require(sheet.gameObject.activeInHierarchy && effect.text == CardDatabase.Get(Magician).desc &&
                Field<DragSourceType>(editor, "_mobileSelectedSource") == DragSourceType.Trunk &&
                deck.main.Count == 0 && tile.count.text == available,
                "A trunk tap changed deck ownership instead of opening card details.");
            close.onClick.Invoke();
            Tap(tile.trigger);
            operations.onClick.Invoke();
            Require(actions.activeInHierarchy && deck.main.Count == 0, "Opening trunk operations changed the deck.");
            Field<Button>(editor, "_deckCardActionMain").onClick.Invoke();
            Require(deck.main.SequenceEqual(new[] { Magician }) && tile.count.text == "×2",
                "Explicitly adding the inspected card did not update the remaining count.");
            var quantities = Field<Button[]>(editor, "_mobileQuantity");
            quantities[2].onClick.Invoke();
            Tap(tile.trigger);
            Require(sheet.gameObject.activeInHierarchy && deck.main.Count == 3,
                "Inspecting a card after increasing its quantity changed the number of copies.");
            for (int slotIndex = 0; slotIndex < 3; slotIndex++)
            {
                var copy = main.Find($"Card_{slotIndex}/CardInset/Card").GetComponent<CompositeCardView>();
                Require(copy.CurrentCode == Magician && copy.transform.Find("AspectBox/DeckCopies") == null,
                    "Copies should occupy separate deck slots without an artwork badge.");
            }
            close.onClick.Invoke();
            quantities[1].onClick.Invoke();
            Require(deck.main.Count == 2, "The two-copy stepper did not set the exact quantity.");
            quantities[3].onClick.Invoke();
            Require(deck.main.Count == 0, "The remove stepper did not clear the selected card.");
            quantities[0].onClick.Invoke();
            Require(deck.main.SequenceEqual(new[] { Magician }), "The one-copy stepper did not restore the selected card.");

            tile.trigger.OnPointerDown(finger);
            Require(!(bool)Call(tile.trigger, "TryCompleteTouchHold", 0, finger.position + Vector2.right * 80f,
                Field<float>(tile.trigger, "_touchPressAt") + .60f), "A swipe fired the long press.");
            tile.trigger.OnPointerUp(finger);
            tile.trigger.OnPointerDown(finger);
            Require((bool)Call(tile.trigger, "TryCompleteTouchHold", 0, finger.position,
                Field<float>(tile.trigger, "_touchPressAt") + .60f), "A stationary long press did not show card details.");
            Require(panel.anchoredPosition.y < -panel.rect.height && Mathf.Approximately(panel.anchoredPosition.x, 0f),
                "The mobile card sheet did not start below the viewport.");
            Call(editor, "UpdateExpandedDeckCard", .3f);
            Require(panel.anchoredPosition == Vector2.zero && effect.fontSize >= 30 && effect.text == CardDatabase.Get(Magician).desc,
                "The sheet did not rise with readable card details.");
            tile.trigger.OnPointerUp(finger);
            tile.trigger.OnPointerClick(finger);
            Require(deck.main.Count == 1 && panel.anchoredPosition == Vector2.zero,
                "Releasing a long press edited the deck or opened the details a second time.");
            panel.Find("Related/Button").GetComponent<Button>().onClick.Invoke();
            Require(Field<uint>(editor, "_relatedFilterCode") == Magician && trunkPane.gameObject.activeInHierarchy &&
                deckPane.gameObject.activeInHierarchy && results.Any(card => card.code == DragonKnight),
                "Related-card search did not filter the trunk while leaving the deck visible.");

            deck.main.Clear();
            deck.main.AddRange(new[] { Pot, Magician, Ash, MirrorForce, Excel, Diabellstar });
            root.Find("MobileToolbar/AutoSort/Button").GetComponent<Button>().onClick.Invoke();
            Require(CardDatabase.Get(deck.main[0]).IsMonster && CardDatabase.Get(deck.main[^1]).IsTrap,
                "The top autosort did not order cards by frame.");
            root.Find("MobileToolbar/Save/Button").GetComponent<Button>().onClick.Invoke();
            Require(!(bool)Call(editor, "HasUnsavedChanges"), "The top save button did not persist the edited deck.");
            Field<Button>(editor, "_openDrawButton").onClick.Invoke();
            var hand = Field<Button[]>(editor, "_openingHandButtons");
            Require(Field<RectTransform>(editor, "_deckDrawOverlay").gameObject.activeInHierarchy &&
                hand.Count(button => button.gameObject.activeInHierarchy) == 5, "The top draw button did not show five cards.");
            var firstHand = Field<uint[]>(editor, "_openingHand");
            Field<Button>(editor, "_redrawButton").onClick.Invoke();
            Require(!ReferenceEquals(firstHand, Field<uint[]>(editor, "_openingHand")) &&
                hand.Count(button => button.gameObject.activeInHierarchy) == 5, "The mobile first-hand tool did not redraw.");
            Call(editor, "CloseDeckUtility");
            Debug.Log("[VERIFY MOBILE DECK] Shared deck and trunk, compact card rows, touch inspection before explicit edits, keyboard inset, and top actions passed.");
        }

        private static void VerifyMobileOverview(DeckEditorUI editor, RectTransform canvas, MobileSafeArea layout,
            RectTransform safe, Rect area, Vector2Int display)
        {
            canvas.sizeDelta = (Vector2)display;
            layout.Reflow(area, display);
            Canvas.ForceUpdateCanvases();
            Call(editor, "LayoutMobileEditor");
            Canvas.ForceUpdateCanvases();
            var trunkPane = Field<RectTransform>(editor, "_trunkPanel");
            var deckPane = Field<RectTransform>(editor, "_deckPanel");
            var trunk = Field<RectTransform>(editor, "_trunkViewport");
            int trunkColumns = Field<int>(editor, "_trunkColumns");
            int mainColumns = Field<int>(editor, "_mainColumns");
            int rows = Mathf.FloorToInt(trunk.rect.height / Field<float>(editor, "_rowHeight"));
            float rightOfTrunk = safe.InverseTransformPoint(trunkPane.TransformPoint(new Vector3(trunkPane.rect.xMax, 0f))).x;
            float leftOfDeck = safe.InverseTransformPoint(deckPane.TransformPoint(new Vector3(deckPane.rect.xMin, 0f))).x;
            Require(rightOfTrunk < leftOfDeck && trunkPane.gameObject.activeInHierarchy && deckPane.gameObject.activeInHierarchy,
                $"{display} did not fit both mobile panes side by side.");
            Require(trunkColumns >= 4 && rows >= 2 && mainColumns >= 7 &&
                Field<int>(editor, "_mainVisibleRows") == 3 && Field<int>(editor, "_extraVisibleRows") == 2,
                $"{display} showed only {trunkColumns}×{rows} trunk cards or {mainColumns} main-deck columns.");
            var trunkArt = (RectTransform)Field<RectTransform>(editor, "_trunkContent").Find("TrunkCard/CardInset/Card/AspectBox");
            Require(trunkArt.rect.width >= 88f, $"{display} trunk artwork became too small ({trunkArt.rect.width:F1}).");
            var mainArt = (RectTransform)Field<RectTransform>(editor, "_mainDeckSlotGrid").Find("Card_0/CardInset/Card/AspectBox");
            Require(mainArt.rect.width >= 44f, $"{display} deck artwork became too small ({mainArt.rect.width:F1}).");
        }

        private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, Instance).GetValue(source);

        private static object Call(object source, string name, params object[] arguments) =>
            source.GetType().GetMethod(name, Instance).Invoke(source, arguments);

        private static void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException(message);
        }
    }
}
