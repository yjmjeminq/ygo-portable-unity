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
    internal static class TestShopModalInput
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static void Run()
        {
            string save = Path.Combine(Path.GetTempPath(), "ygo-shop-input-test-" + Guid.NewGuid().ToString("N"));
            DeckRepository.Load(Application.streamingAssetsPath, save, seedDefaultDecks: false);
            CollectionProgress.Load(save);
            var starter = CollectionProgress.Starters.First(item => item.Name.Contains("상검"));
            Require(CollectionProgress.TryChooseStarter(starter.Id, out _, out string error), error);

            var previousEvents = EventSystem.current;
            var previousFocus = previousEvents?.currentSelectedGameObject;
            GameObject localEvents = null;
            var events = previousEvents;
            if (events == null)
            {
                localEvents = new GameObject("Shop Navigation Events", typeof(EventSystem));
                events = localEvents.GetComponent<EventSystem>();
                if (EventSystem.current == null) Call(events, "OnEnable");
            }
            var canvasObject = new GameObject("Shop Navigation Canvas", typeof(Canvas), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(1600f, 900f);
            try
            {
                var shop = new CollectionShopUI(canvasObject.transform, () => { }, _ => { });
                shop.OpenShop();
                Canvas.ForceUpdateCanvases();
                VerifyPack(shop, events);
                Require(shop.Root.transform.Find("Crafting") == null, "The pack shop still exposes card crafting.");
                Call(shop, "CloseShop");

                var editor = new DeckEditorUI(canvasObject.AddComponent<GameAppController>(), canvasObject.transform);
                editor.Open();
                try
                {
                    VerifyDeckDisassembly(editor, events);
                    VerifyExcessDisassembly(editor, events);
                }
                finally { editor.Close(); }
            }
            finally
            {
                events.SetSelectedGameObject(previousFocus);
                UnityEngine.Object.DestroyImmediate(canvasObject);
                if (localEvents != null)
                {
                    if (EventSystem.current == events) Call(events, "OnDisable");
                    UnityEngine.Object.DestroyImmediate(localEvents);
                }
            }
            Debug.Log("[VERIFY SHOP INPUT] Pack/crafting confirmation scopes, cancellation, repeat confirmation, pack hover/reveal and excess-copy disassembly passed.");
        }

        private static void VerifyPack(CollectionShopUI shop, EventSystem events)
        {
            var buy = ButtonAt(shop.Root, "Details/Buy/Button");
            var tile = ButtonAt(shop.Root, "Catalog/Pack_0/Button");
            buy.onClick.Invoke();
            var dialog = Scope(shop);
            var confirm = ButtonAt(dialog, "Window/Confirm/Button");
            var cancel = ButtonAt(dialog, "Window/Cancel/Button");
            Require(!buy.IsInteractable() && !tile.IsInteractable() && confirm.IsInteractable() &&
                events.currentSelectedGameObject == cancel.gameObject, "Pack confirmation did not isolate keyboard focus.");
            VerifyNeighbors(cancel, dialog);
            cancel.onClick.Invoke();
            Require(Scope(shop) == shop.Root && buy.IsInteractable() && tile.IsInteractable() &&
                CollectionProgress.Balance == CollectionProgress.StartingBalance, "Cancel did not restore the shop or changed DP.");

            buy.onClick.Invoke();
            confirm.onClick.Invoke();
            confirm.onClick.Invoke();
            Require(CollectionProgress.Balance == CollectionProgress.StartingBalance - CollectionProgress.PackCost,
                "Rapid confirmation charged the wrong price.");
            Call(shop, "HandleShopBack");
            var results = Scope(shop);
            var viewer = Field(shop, "_openingView");
            var card = ButtonAt(results, "Cards/Card_0/Select");
            ExecuteEvents.Execute(card.gameObject, new PointerEventData(events), ExecuteEvents.pointerEnterHandler);
            ButtonAt(results, "RevealAll/Button").onClick.Invoke();
            Call(viewer, "AdvanceResults", .20f);
            Require(card.transform.Find("Face/View/AspectBox/CardBack").gameObject.activeSelf &&
                (int)Field(viewer, "_inspected") == -1, "An unopened card reacted before arriving.");
            Call(viewer, "AdvanceResults", .09f);
            Require((int)Field(viewer, "_inspected") == 0, "A card already under the pointer did not become inspected on arrival.");
            Call(viewer, "AdvanceResults", 2f);
            Require((int)Field(viewer, "_revealed") == CollectionProgress.CardsPerPack, "The staggered reveal did not finish.");
            Canvas.ForceUpdateCanvases();
            var nextRow = ButtonAt(results, "Cards/Card_4/Select");
            Require(((RectTransform)card.transform.Find("Face/View/AspectBox")).rect.height >= 205f &&
                card.FindSelectableOnDown() == nextRow && nextRow.FindSelectableOnUp() == card,
                "Pack cards did not render at the larger size or the second row was unreachable.");
            Call(shop, "HandleShopBack");
            Require(Scope(shop) == shop.Root && buy.IsInteractable(), "Leaving the results did not restore shop controls.");
        }

        private static void VerifyDeckDisassembly(DeckEditorUI editor, EventSystem events)
        {
            uint code = CollectionProgress.GetDisassembleLimits().Keys.First();
            int owned = CollectionProgress.GetOwnedCount(code);
            var rarity = CardRarityCatalog.GetRarity(code);
            var alternative = CardDatabase.AllCards.First(card => card.code != code && CollectionProgress.IsCraftableCard(card));
            var working = (UserDeck)Field(editor, "_workingDeck");
            int originalSize = working.main.Count;
            working.main.AddRange(Enumerable.Repeat(code, owned));
            Call(editor, "InspectCard", code);
            var craftingButton = ButtonAt(editor.Root, "Editor/CardDetails/Crafting/Button");
            events.SetSelectedGameObject(craftingButton.gameObject);
            craftingButton.onClick.Invoke();

            var crafting = Field(editor, "_cardCrafting");
            var catalog = CraftingScope(crafting);
            var art = (CompositeCardView)Field(crafting, "_art");
            Require(editor.Root.activeSelf && !craftingButton.gameObject.activeInHierarchy && art.CurrentCode == code,
                "Crafting did not open from the selected card without exposing the deck editor controls.");
            var craft = ButtonAt(catalog, "Details/Craft/Button");
            Require(craft.GetComponentInChildren<Text>().text == $"제작 · 가루 {CollectionProgress.GetCraftDustCost(rarity)}",
                "The crafting button did not show the cost for the selected rarity.");
            var disassemble = ButtonAt(catalog, "Details/Disassemble/Button");
            int dustPerCard = CollectionProgress.GetDisassemblyDustPerCard(code);
            Require(disassemble.GetComponentInChildren<Text>().text == $"분해 · 가루 +{dustPerCard}",
                "The disassembly button did not show the reward for the selected rarity.");
            disassemble.onClick.Invoke();
            var usedCardDialog = CraftingScope(crafting);
            Require(usedCardDialog != catalog && (int)Field(crafting, "_maxQuantity") == owned,
                "Single-card disassembly did not offer every copy used by the edited deck.");
            Call(crafting, "Back");

            ButtonAt(catalog, "Bulk/Button").onClick.Invoke();
            var bulk = Field(crafting, "_bulk");
            var bulkCatalog = CraftingScope(crafting);
            ButtonAt(bulkCatalog, "Details/SelectAll/Button").onClick.Invoke();
            var selected = (Dictionary<uint, int>)Field(bulk, "_selected");
            var available = CollectionProgress.GetDisassembleLimits();
            Require(selected.Count == available.Count && selected[code] == owned &&
                available.All(pair => selected.TryGetValue(pair.Key, out int quantity) && quantity == pair.Value),
                "Select all missed eligible owned copies.");
            string reward = $"가루 +{available.Sum(entry => (long)entry.Value * CollectionProgress.GetDisassemblyDustPerCard(entry.Key)):N0}";
            Require(bulkCatalog.transform.Find("Details/Dust/Text").GetComponent<Text>().text == reward,
                "The bulk catalog did not total the shared dust reward.");
            ButtonAt(bulkCatalog, "Details/Review/Button").onClick.Invoke();
            var bulkReview = CraftingScope(crafting);
            Require(bulkReview != bulkCatalog && bulkReview.transform.Find("Details/Dust/Text").GetComponent<Text>().text == reward,
                "The bulk confirmation did not show the same shared dust reward.");
            Call(crafting, "Back");
            Call(crafting, "Back");
            ButtonAt(catalog, "Back/Button").onClick.Invoke();
            Require(craftingButton.gameObject.activeInHierarchy && events.currentSelectedGameObject == craftingButton.gameObject &&
                ReferenceEquals(Field(editor, "_workingDeck"), working) && working.main.Count == originalSize + owned,
                "Returning from crafting did not restore the original working deck and focus.");

            working.main.RemoveRange(originalSize, owned);
            craftingButton.onClick.Invoke();
            catalog = CraftingScope(crafting);
            var search = catalog.transform.Find("Catalog/Search/Input").GetComponent<InputField>();
            disassemble.onClick.Invoke();
            var dialog = CraftingScope(crafting);
            var cancel = ButtonAt(dialog, "Window/Cancel/Button");
            var confirm = ButtonAt(dialog, "Window/Confirm/Button");
            Require(!search.IsInteractable() && !disassemble.IsInteractable() && confirm.IsInteractable(),
                "The crafting dialog allowed keyboard navigation behind its scrim.");
            VerifyNeighbors(cancel, dialog);
            cancel.onClick.Invoke();
            Require(search.IsInteractable() && disassemble.IsInteractable() && CollectionProgress.GetOwnedCount(code) == owned,
                "Disassembly cancellation changed the card or left catalog controls locked.");

            disassemble.onClick.Invoke();
            if (owned > 1) ButtonAt(CraftingScope(crafting), "Window/Maximum/Button").onClick.Invoke();
            search.text = alternative.code.ToString();
            confirm.onClick.Invoke();
            Require(CollectionProgress.GetOwnedCount(code) == 0 &&
                CollectionProgress.CraftDust == (long)owned * dustPerCard &&
                catalog.transform.Find("Details/Balance/Text").GetComponent<Text>().text ==
                    $"보유 가루 {CollectionProgress.CraftDust:N0}" &&
                search.IsInteractable() && CraftingScope(crafting) == catalog,
                "Disassembly failed to apply its displayed card exactly once.");
            ButtonAt(catalog, "Back/Button").onClick.Invoke();
            Require(((Text)Field(editor, "_previewOwnership")).text.Contains("미보유"),
                "The editor did not refresh the card's ownership after disassembly.");
        }

        private static void VerifyExcessDisassembly(DeckEditorUI editor, EventSystem events)
        {
            var owned = CollectionProgress.GetDisassembleLimits();
            uint reserved = owned.First(entry => entry.Value == 3).Key;
            uint FindUnownedCard(CardRarity rarity) => CardDatabase.AllCards.First(card => card.alias == 0 &&
                card.IsMonster && !owned.ContainsKey(card.code) && CollectionProgress.IsCraftableCard(card) &&
                CardRarityCatalog.GetRarity(card.code) == rarity).code;
            uint normal = FindUnownedCard(CardRarity.Normal);
            uint rare = FindUnownedCard(CardRarity.Rare);
            var materials = owned.Where(entry => entry.Key != reserved).ToDictionary(entry => entry.Key, entry => entry.Value);
            Require(CollectionProgress.TryDisassembleCards(materials, out string error), error);
            Require(CollectionProgress.TryCraftCard(normal, 4, out error), error);
            Require(CollectionProgress.TryCraftCard(rare, 5, out error), error);

            Call(editor, "InspectCard", reserved);
            ButtonAt(editor.Root, "Editor/CardDetails/Crafting/Button").onClick.Invoke();
            var crafting = Field(editor, "_cardCrafting");
            var catalog = CraftingScope(crafting);
            ButtonAt(catalog, "Bulk/Button").onClick.Invoke();
            var bulkCatalog = CraftingScope(crafting);
            var excess = ButtonAt(bulkCatalog, "Details/SelectExcess/Button");
            var search = bulkCatalog.transform.Find("Catalog/Search/Input").GetComponent<InputField>();
            search.text = reserved.ToString();
            ButtonAt(bulkCatalog, "Details/SelectAll/Button").onClick.Invoke();

            decimal before = CollectionProgress.CraftDust;
            int total = CollectionProgress.GetDisassemblyDustPerCard(normal) + 2 * CollectionProgress.GetDisassemblyDustPerCard(rare);
            Require(excess.IsInteractable(), "A card search disabled the collection-wide excess action.");
            ExecuteEvents.Execute(excess.gameObject, new PointerEventData(events) { button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler);
            var review = CraftingScope(crafting);
            var bulk = Field(crafting, "_bulk");
            var selection = (Dictionary<uint, int>)Field(bulk, "_selected");
            Require(review != bulkCatalog && selection.Count == 2 && selection[normal] == 1 && selection[rare] == 2 &&
                review.transform.Find("Details/Dust/Text").GetComponent<Text>().text == $"가루 +{total:N0}" &&
                events.currentSelectedGameObject == ButtonAt(review, "Details/Cancel/Button").gameObject,
                "The excess action did not review only copies beyond the third or displayed an incorrect reward.");
            Call(editor, "HandleDeckPad", new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Cancel));
            Require(CraftingScope(crafting) == bulkCatalog && CollectionProgress.CraftDust == before &&
                CollectionProgress.GetOwnedCount(normal) == 4 && CollectionProgress.GetOwnedCount(rare) == 5,
                "Cancelling the excess review changed the collection.");

            events.SetSelectedGameObject(excess.gameObject);
            Call(editor, "HandleDeckPad", new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Submit));
            Require(CraftingScope(crafting) == review, "Controller submit did not open the excess review.");
            var confirm = ButtonAt(review, "Details/Confirm/Button");
            confirm.onClick.Invoke();
            confirm.onClick.Invoke();
            Require(CraftingScope(crafting) == bulkCatalog && !excess.IsInteractable() &&
                CollectionProgress.CraftDust == before + total && new[] { normal, rare, reserved }.All(code =>
                    CollectionProgress.GetOwnedCount(code) == 3), "Excess confirmation did not preserve three copies or applied twice.");
            Call(crafting, "Back");
            Call(crafting, "Back");
        }

        private static void VerifyNeighbors(Button control, GameObject dialog)
        {
            var neighbors = new[] { control.FindSelectableOnUp(), control.FindSelectableOnDown(),
                control.FindSelectableOnLeft(), control.FindSelectableOnRight() };
            Require(neighbors.All(candidate => candidate == null || candidate.transform.IsChildOf(dialog.transform)),
                "Unity keyboard navigation could leave the confirmation.");
        }

        private static GameObject Scope(CollectionShopUI shop) =>
            (GameObject)shop.GetType().GetProperty("ActiveShopRoot", Instance).GetValue(shop);

        private static GameObject CraftingScope(object crafting) =>
            (GameObject)crafting.GetType().GetProperty("ActiveRoot", Instance).GetValue(crafting);

        private static Button ButtonAt(GameObject root, string path) => root.transform.Find(path).GetComponent<Button>();
        private static object Field(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
        private static object Call(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, Instance).Invoke(target, args);

        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
    }
}
