using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.Editor
{
    internal static class TestSecretShop
    {
        public static void Run()
        {
            int exit = 1;
            string streaming = Application.streamingAssetsPath;
            string save = Path.Combine(Path.GetTempPath(), "ygo-secret-shop-" + Guid.NewGuid().ToString("N"));
            try
            {
                CardDatabase.Load(streaming);
                DeckRepository.Load(streaming, save, seedDefaultDecks: false);
                CardArtLoader.Initialize(streaming, Path.Combine(save, "Art"));
                VerifyEconomy(save);
                TestShopModalInput.Run();
                Debug.Log("[VERIFY SECRET SHOP] Locking, acquisition, save roundtrip, guaranteed UR, mileage and redemption passed.");
                exit = 0;
            }
            catch (Exception error) { Debug.LogException(error); }
            finally
            {
                CardArtLoader.Shutdown();
                if (Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }

        private static void VerifyEconomy(string save)
        {
            CollectionProgress.Load(save);
            Require(CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out _, out string error), error);
            var locked = CollectionProgress.Packs.First(pack => pack.IsSecret &&
                pack.ThemeCards.Any(code => CardRarityCatalog.GetRarity(code) >= CardRarity.SuperRare));
            Require(!CollectionProgress.IsPackUnlocked(locked) &&
                !CollectionProgress.TryBuyPack(locked.Id, out _, out _), "A secret pack was purchasable before unlocking it.");
            uint trigger = locked.ThemeCards.First(code => CardRarityCatalog.GetRarity(code) >= CardRarity.SuperRare);
            int announcements = 0;
            void RecordUnlock(System.Collections.Generic.IReadOnlyList<CollectionPack> packs)
            {
                if (packs.Any(pack => pack.Id == locked.Id)) announcements++;
            }
            CollectionProgress.SecretPacksUnlocked += RecordUnlock;
            string file = Path.Combine(save, CollectionProgress.ProfileFileName);
            try
            {
                string profile = File.ReadAllText(file);
                File.WriteAllText(file, profile.Replace("\"craftDust\": \"0\"", "\"craftDust\": \"2000\"")
                    .Replace("\"mileage\": 0", "\"mileage\": 91"));
                CollectionProgress.Load(save);
                Require(CollectionProgress.TryCraftCard(trigger, 1, out error), error);
                Require(CollectionProgress.IsPackUnlocked(locked) && announcements == 1, "Crafting did not unlock or announce the matching pack.");
                CollectionProgress.Load(save);
                Require(CollectionProgress.IsPackUnlocked(locked) && announcements == 1, "The pack did not stay unlocked after reload.");
                var noUr = CollectionProgress.Packs.First(pack => !pack.IsSecret &&
                    pack.CardPool.All(code => CardRarityCatalog.GetRarity(code) != CardRarity.UltraRare) &&
                    pack.CardPool.Any(code => CardRarityCatalog.GetRarity(code) == CardRarity.SuperRare));
                Require(CollectionProgress.TryBuyPackContents(noUr.Id, 10, out var first, out error), error);
                Require(first.SelectMany(cards => cards).Any(card => CardRarityCatalog.GetRarity(card.Code) == CardRarity.SuperRare) &&
                    CollectionProgress.NextBundleGuaranteesUr && CollectionProgress.PackMileage == 1 &&
                    CollectionProgress.PackUrTickets == 1, "A bundle without UR did not trigger both guarantees.");
                Require(CollectionProgress.TryBuyPackContents(noUr.Id, 1, out _, out error) &&
                    CollectionProgress.NextBundleGuaranteesUr, "A single pack consumed the guaranteed UR bundle.");
                CollectionProgress.Load(save);
                Require(CollectionProgress.TryBuyPackContents(noUr.Id, 10, out var second, out error), error);
                Require(second.SelectMany(cards => cards).Any(card => CardRarityCatalog.GetRarity(card.Code) == CardRarity.UltraRare) &&
                    !CollectionProgress.NextBundleGuaranteesUr, "The next bundle did not deliver its guaranteed UR.");
                var general = CollectionProgress.Packs.Single(pack => pack.Id == "general-master");
                Require(CollectionProgress.TryRedeemPackTicket(general.Id, out var reward, out error), error);
                Require(general.CardPool.Contains(reward.Code) && CardRarityCatalog.GetRarity(reward.Code) == CardRarity.UltraRare &&
                    CollectionProgress.PackUrTickets == 0 && reward.OwnedCopies == 1,
                    "The ticket did not award an unowned UR from the selected pack.");
                Require(!CollectionProgress.TryRedeemPackTicket(general.Id, out _, out _), "A ticket was redeemed twice.");
            }
            finally { CollectionProgress.SecretPacksUnlocked -= RecordUnlock; }
        }

        private static void Require(bool condition, string error)
        {
            if (!condition) throw new InvalidOperationException(error);
        }
    }
}
