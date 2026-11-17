using System;
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
                VerifyDisassembly(shop);
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
            Debug.Log("[VERIFY SHOP INPUT] Pack/crafting confirmation scopes, cancellation, repeat confirmation, pack hover/reveal and displayed card disassembly passed.");
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
            Call(shop, "HandleShopBack");
            Require(Scope(shop) == shop.Root && buy.IsInteractable(), "Leaving the results did not restore shop controls.");
        }

        private static void VerifyDisassembly(CollectionShopUI shop)
        {
            uint code = CollectionProgress.GetDisassembleLimits().Keys.First();
            int owned = CollectionProgress.GetOwnedCount(code);
            var rarity = CardRarityCatalog.GetRarity(code);
            var alternative = CardDatabase.AllCards.First(card => card.code != code && CollectionProgress.IsCraftableCard(card));
            ButtonAt(shop.Root, "Crafting/Button").onClick.Invoke();
            var catalog = Scope(shop);
            var search = catalog.transform.Find("Catalog/Search/Input").GetComponent<InputField>();
            search.text = code.ToString();
            var disassemble = ButtonAt(catalog, "Details/Disassemble/Button");
            disassemble.onClick.Invoke();
            var dialog = Scope(shop);
            var cancel = ButtonAt(dialog, "Window/Cancel/Button");
            var confirm = ButtonAt(dialog, "Window/Confirm/Button");
            Require(!search.IsInteractable() && !disassemble.IsInteractable() && confirm.IsInteractable(),
                "The crafting dialog allowed keyboard navigation behind its scrim.");
            VerifyNeighbors(cancel, dialog);
            cancel.onClick.Invoke();
            Require(search.IsInteractable() && disassemble.IsInteractable() && CollectionProgress.GetOwnedCount(code) == owned,
                "Disassembly cancellation changed the card or left catalog controls locked.");

            disassemble.onClick.Invoke();
            search.text = alternative.code.ToString();
            confirm.onClick.Invoke();
            Require(CollectionProgress.GetOwnedCount(code) == owned - 1 &&
                CollectionProgress.GetCraftPoints(rarity) == CollectionProgress.DisassemblePointValue &&
                search.IsInteractable() && Scope(shop) == catalog, "Disassembly failed to apply its displayed card exactly once.");
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
