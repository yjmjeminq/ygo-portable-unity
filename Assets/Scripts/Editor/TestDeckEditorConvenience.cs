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
                editor.Open();
                VerifyQuickSearch(editor);
                VerifyQuantitiesAndSort(editor);
                VerifyRecipeActions(editor);
                VerifyOpeningHandAndComposition(editor);
                editor.Close();

                mobile = new DeckEditorUI(app, canvas.transform, mobileLayout: true);
                mobile.Open();
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
            Debug.Log("[VERIFY DECK QOL] Related cards, chips, reference badges, quantities, autosort, recipe actions, test draws and mobile hit areas passed.");
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

            var search = Field<InputField>(editor, "_searchInp");
            var slots = Field<List<(RectTransform rt, CompositeCardView card, Text count, CardDragTrigger trigger)>>(editor, "_trunkPool");
            foreach (var (code, expected) in new[] { (Pot, "0"), (18144506u, "1"), (48130397u, "2"), (BlueEyes, "") })
            {
                search.text = code.ToString();
                var view = slots.Single(slot => slot.card.CurrentCode == code && slot.rt.gameObject.activeInHierarchy).card;
                var badge = view.transform.Find("AspectBox/Regulation");
                Require(expected.Length == 0 ? !badge.gameObject.activeSelf :
                    badge.gameObject.activeSelf && badge.Find("Label").GetComponent<Text>().text == expected,
                    $"Regulation badge was incorrect for {code}.");
            }
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
            Require(deck.main.Count(code => code == Pot) == 1, "The local one-copy rule did not survive a MAX edit.");
            var badge = main.Find($"Card_{deck.main.IndexOf(Pot)}/CardInset/Card/AspectBox/Regulation");
            Require(badge.gameObject.activeSelf && badge.Find("Label").GetComponent<Text>().text == "0",
                "The deck card lacked its reference badge.");

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
        }

        private static void VerifyOpeningHandAndComposition(DeckEditorUI editor)
        {
            var deck = Field<UserDeck>(editor, "_workingDeck");
            var originals = new[] { BlueEyes, BlueEyes, Magician, Ash, Pot, MirrorForce, 18144506u };
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
            deckDraw.Find("Window/Second/Button").GetComponent<Button>().onClick.Invoke();
            Require(cards.All(button => button.gameObject.activeSelf) &&
                Field<Text>(editor, "_openingHandDrawLabel").gameObject.activeSelf &&
                Field<uint[]>(editor, "_openingHand").SequenceEqual(sample),
                "Going second did not reveal the sixth card independently of the opening hand.");
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
        }

        private static void VerifyMobile(DeckEditorUI editor, EventSystem events)
        {
            var safe = editor.Root.transform.Find("MobileSafeArea");
            Require(safe != null && safe.Find("Editor") != null, "The mobile deck editor had no safe-area host.");
            var layout = safe.GetComponent<MobileSafeArea>();
            layout.Reflow(new Rect(112, 48, 1376, 812), new Vector2Int(1600, 900));
            Canvas.ForceUpdateCanvases();
            Call(editor, "RefreshDeckGrids");
            Call(editor, "ResizeTrunkGrid", true);

            float touch = MobileSafeArea.MinimumTouchSize(safe);
            foreach (string gridField in new[] { "_mainDeckSlotGrid", "_extraDeckSlotGrid", "_sideDeckSlotGrid" })
            {
                var slot = (RectTransform)Field<RectTransform>(editor, gridField).Find("Card_0");
                Require(slot.rect.width >= touch - .5f && slot.rect.height >= touch - .5f,
                    $"{gridField}: hit area {slot.rect.size} missed the {touch} minimum.");
            }
            var chip = (RectTransform)Field<Button[]>(editor, "_quickFilterButtons")[0].transform;
            Require(chip.rect.width >= touch - .5f && chip.rect.height >= touch - .5f,
                "A mobile filter chip did not allow a full touch target.");
            var main = Field<RectTransform>(editor, "_mainDeckSlotGrid");
            var trigger = main.Find("Card_0").GetComponent<CardDragTrigger>();
            var finger = new PointerEventData(events)
            {
                pointerId = 0, button = PointerEventData.InputButton.Left, position = new Vector2(400, 270),
            };
            trigger.OnPointerDown(finger);
            Require(!(bool)Call(trigger, "TryCompleteTouchHold", 0, finger.position, Field<float>(trigger, "_touchPressAt") + .15f),
                "A short touch incorrectly opened the expanded inspector.");
            Require((bool)Call(trigger, "TryCompleteTouchHold", 0, finger.position, Field<float>(trigger, "_touchPressAt") + .52f),
                "A stationary long press did not open the expanded inspector.");
            var panel = Field<RectTransform>(editor, "_expandedDeckPanel");
            Require(panel.anchoredPosition.x > panel.rect.width, "The expanded inspector did not start off-screen.");
            Call(editor, "UpdateExpandedDeckCard", .3f);
            Require(panel.anchoredPosition == Vector2.zero && panel.rect.width > 800 &&
                Field<Text>(editor, "_expandedDeckDescription").text == CardDatabase.Get(trigger.CardCodeGetter()).desc,
                "The inspector did not slide in to show readable card details.");
            trigger.OnPointerUp(finger);
            trigger.OnPointerClick(finger);
            Require(!Field<GameObject>(editor, "_deckCardActions").activeSelf, "Releasing a long press also opened the tap menu.");
            Call(editor, "CloseExpandedDeckCard");
            trigger.OnPointerDown(finger);
            Require(!(bool)Call(trigger, "TryCompleteTouchHold", 0, finger.position + Vector2.right * 80f,
                Field<float>(trigger, "_touchPressAt") + .60f), "A swipe fired the long-press action.");
            trigger.OnPointerUp(finger);
            trigger.OnPointerDown(finger);
            trigger.OnPointerUp(finger);
            trigger.OnPointerClick(finger);
            var overlay = Field<GameObject>(editor, "_deckCardActions");
            var plus = (RectTransform)Field<Button>(editor, "_deckQuantityPlus").transform;
            Require(overlay.activeSelf && plus.rect.height >= touch - .5f && plus.rect.width >= touch - .5f,
                $"A normal tap gave the mobile menu a quantity button of {plus.rect.size} instead of {touch}.");
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
