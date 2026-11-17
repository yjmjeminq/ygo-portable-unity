using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.Editor
{
    internal static class TestCollectionProgress
    {
        private const string SupportPack = "support-staples";
        private const uint AshBlossom = 14558127;
        private static readonly CardRarity[] Rarities = (CardRarity[])Enum.GetValues(typeof(CardRarity));

        internal static void Run(string streamingPath)
        {
            string save = Path.Combine(Path.GetTempPath(), "ygo-collection-test-" + Guid.NewGuid().ToString("N"));
            string profile = Path.Combine(save, "collection-profile.json");
            DeckRepository.Load(streamingPath, save, seedDefaultDecks: false);
            CollectionProgress.Load(save);
            Require(!CollectionProgress.HasStarter && !CollectionProgress.TryBuyPack(SupportPack, out _, out _),
                "A fresh profile bought a pack without choosing a starter.");

            var starters = CollectionProgress.Starters;
            Require(starters.Count >= 400 && starters.Select(option => option.Id).Distinct().Count() == starters.Count &&
                starters.All(option => option.MainCount >= 40 && option.MainCount <= 60 && option.ExtraCount <= 15),
                "A starter pack was missing, duplicated, or had an illegal deck size.");
            var selection = starters.First(option => option.Name.Contains("상검"));
            Require(CollectionProgress.TryChooseStarter(selection.Id, out var starter, out string error), error);
            Require(CollectionProgress.Balance == CollectionProgress.StartingBalance &&
                CollectionProgress.CanUseDeck(starter, out _) && !CollectionProgress.TryChooseStarter(selection.Id, out _, out _),
                "Starter selection did not grant the deck and starting DP exactly once.");

            var materials = LoadLegacyProfile(save, profile);
            VerifyCrafting(materials, starter, profile);
            VerifyPurchasesAndRewards(save, profile);
            Debug.Log($"[VERIFY COLLECTION] {starters.Count} starter deck sizes, legacy saves, isolated CP, reserved cards, " +
                "single and bundle purchases, stale-save rollback and opponent progression passed.");
        }

        private static uint[] LoadLegacyProfile(string save, string profile)
        {
            var materials = Rarities.Select(rarity => CardDatabase.AllCards.First(card =>
                card.code != AshBlossom && card.alias == 0 && card.IsMonster && CollectionProgress.IsCraftableCard(card) &&
                CollectionProgress.GetDeckCopyLimit(card.code) == 3 && CollectionProgress.GetOwnedCount(card.code) == 0 &&
                CardRarityCatalog.GetRarity(card.code) == rarity).code).ToArray();

            string json = File.ReadAllText(profile);
            json = Regex.Replace(json, "\"version\"\\s*:\\s*\\d+", "\"version\": 3");
            json = Regex.Replace(json, "\"craftPoints\"\\s*:\\s*\\[[^\\]]*\\],\\s*", "");
            string cards = string.Join(",", materials.Select(code => $"{{\"code\":{code},\"count\":3}}"));
            json = Regex.Replace(json, "\"cards\"\\s*:\\s*\\[", "\"cards\":[" + cards + ",");
            File.WriteAllText(profile, json);
            CollectionProgress.Load(save);
            Require(CollectionProgress.HasStarter && CollectionProgress.Balance == CollectionProgress.StartingBalance &&
                Rarities.All(rarity => CollectionProgress.GetCraftPoints(rarity) == 0) &&
                materials.All(code => CollectionProgress.GetOwnedCount(code) == 3),
                "The legacy profile did not preserve DP and cards or initialize CP to zero.");
            Require(CardRarityCatalog.GetRarity(AshBlossom, out bool sourced) == CardRarity.UltraRare && sourced,
                "The bundled rarity catalog did not yield the expected UR test card.");
            return materials;
        }

        private static void VerifyCrafting(uint[] materials, UserDeck starter, string profile)
        {
            uint normal = materials[(int)CardRarity.Normal];
            uint rare = materials[(int)CardRarity.Rare];
            uint super = materials[(int)CardRarity.SuperRare];
            uint ultra = materials[(int)CardRarity.UltraRare];
            DeckRepository.SaveUserDeck(new UserDeck
            {
                name = "Collection reserve fixture",
                main = new List<uint> { normal },
                side = new List<uint> { normal },
            });
            DeckRepository.SaveUserDeck(new UserDeck
            {
                name = "Collection second reserve fixture",
                main = new List<uint> { normal },
            });
            Require(CollectionProgress.GetDisassembleLimit(normal) == 1 &&
                CollectionProgress.GetDisassembleLimit(starter.main[0]) == 0,
                "Card protection did not reserve the maximum used in one deck and the starter.");

            string before = File.ReadAllText(profile);
            Require(!CollectionProgress.TryDisassembleCards(new Dictionary<uint, int> { [rare] = 1, [normal] = 2 }, out _) &&
                File.ReadAllText(profile) == before && CollectionProgress.GetOwnedCount(rare) == 3,
                "A batch that included a protected card partially spent ownership.");
            Require(CollectionProgress.TryDisassembleCards(new Dictionary<uint, int>
            {
                [normal] = 1,
                [rare] = 3,
                [super] = 2,
                [ultra] = 3,
            }, out string error), error);
            int value = CollectionProgress.DisassemblePointValue;
            Require(CollectionProgress.GetCraftPoints(CardRarity.Normal) == value &&
                CollectionProgress.GetCraftPoints(CardRarity.Rare) == 3 * value &&
                CollectionProgress.GetCraftPoints(CardRarity.SuperRare) == 2 * value &&
                CollectionProgress.GetCraftPoints(CardRarity.UltraRare) == 3 * value,
                "Bulk disassembly mixed rarity balances.");
            Require(!CollectionProgress.TryCraftCard(AshBlossom, 4, out _) &&
                !CollectionProgress.TryCraftCard(AshBlossom, 0, out _), "Invalid craft quantities were accepted.");
            Require(CollectionProgress.TryCraftCard(AshBlossom, 1, out error) &&
                CollectionProgress.GetOwnedCount(AshBlossom) == 1 &&
                CollectionProgress.GetCraftPoints(CardRarity.UltraRare) == 0, error);
            Require(!CollectionProgress.TryCraftCard(AshBlossom, 1, out _) &&
                CollectionProgress.GetCraftPoints(CardRarity.Rare) == 3 * value &&
                CollectionProgress.Balance == CollectionProgress.StartingBalance,
                "Crafting fell back to a different currency or changed DP.");
        }

        private static void VerifyPurchasesAndRewards(string save, string profile)
        {
            int expectedDp = CollectionProgress.Balance;
            var pack = CollectionProgress.Packs.Single(item => item.Id == SupportPack);
            Require(CollectionProgress.TryBuyPack(SupportPack, out var single, out string error), error);
            expectedDp -= CollectionProgress.PackCost;
            Require(ValidPack(single, pack) && CollectionProgress.Balance == expectedDp,
                "A single pack did not contain eight cards from its own pool at the shown price.");
            Require(CollectionProgress.TryBuyPacks(SupportPack, CollectionProgress.PackBundleSize, out var bundle, out error), error);
            expectedDp -= CollectionProgress.PackBundleSize * CollectionProgress.PackCost;
            Require(bundle.Count == CollectionProgress.PackBundleSize && bundle.All(pulls => ValidPack(pulls, pack)) &&
                CollectionProgress.Balance == expectedDp && pack.CardPool.All(code =>
                    CollectionProgress.GetOwnedCount(code) <= CollectionProgress.GetDeckCopyLimit(code)),
                "A bundle did not respect its count, price, or collection caps.");

            var first = DeckRepository.Characters.Single(character => CollectionProgress.IsOpponentUnlocked(character.id));
            var levels = OpponentDeckLevels.GetDecks(first.id);
            Require(levels.Count > 1 && CollectionProgress.IsOpponentDeckUnlocked(first.id, levels[0].id) &&
                !CollectionProgress.IsOpponentDeckUnlocked(first.id, levels[1].id),
                "Fresh progression did not start on deck level one.");
            Require(!CollectionProgress.TryAwardDuel(true, first.id, levels[1].id, out _, out _, out _, out _),
                "An unplayed locked deck accepted a reward.");
            Require(CollectionProgress.TryAwardDuel(false, first.id, levels[0].id, out int loss, out _, out _, out error) &&
                loss == CollectionProgress.LossReward && !CollectionProgress.IsOpponentDeckUnlocked(first.id, levels[1].id), error);
            Require(CollectionProgress.TryAwardDuel(true, first.id, levels[0].id, out int win, out var opponent, out var advanced, out error) &&
                win == CollectionProgress.WinReward && opponent != null && advanced?.id == levels[1].id, error);
            expectedDp += loss + win;

            File.AppendAllText(profile, "\n");
            string changedExternally = File.ReadAllText(profile);
            Require(!CollectionProgress.TryAwardDuel(true, out _, out _) && CollectionProgress.Balance == expectedDp &&
                File.ReadAllText(profile) == changedExternally, "A stale profile overwrote a newer save.");
            CollectionProgress.Load(save);
            Require(CollectionProgress.Balance == expectedDp &&
                CollectionProgress.GetCraftPoints(CardRarity.Rare) == 3 * CollectionProgress.DisassemblePointValue &&
                CollectionProgress.GetOwnedCount(AshBlossom) > 0 &&
                CollectionProgress.IsOpponentDeckCleared(first.id, levels[0].id) &&
                CollectionProgress.IsOpponentDeckUnlocked(first.id, levels[1].id) &&
                CollectionProgress.IsOpponentUnlocked(opponent.id),
                "Reload lost minted ownership, CP, DP, or opponent progression.");
        }

        private static bool ValidPack(IReadOnlyList<uint> pulls, CollectionPack pack) =>
            pulls.Count == CollectionProgress.CardsPerPack && pulls.All(code =>
                pack.CardPool.Contains(code) && CollectionProgress.GetOwnedCount(code) > 0);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
