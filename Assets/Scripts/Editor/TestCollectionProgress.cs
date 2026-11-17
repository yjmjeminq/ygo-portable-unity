using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static class TestCollectionProgress
    {
        private const string SupportPack = "support-staples";
        private const uint AshBlossom = 14558127;
        private const uint GracefulCharity = 79571449;
        private static readonly uint[] DrawSpells = { 42664989, 511001126, GracefulCharity };
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

            VerifySaveSync(save, starter);
            var materials = LoadLegacyProfile(save, profile);
            VerifyCrafting(materials, starter, profile);
            VerifyPurchasesAndRewards(save, profile);
            VerifyMyDeckPicker(starter);
            VerifyDeckCopyLimitAndFullDisassembly(starter);
            Debug.Log($"[VERIFY COLLECTION] {starters.Count} starter deck sizes, legacy saves, uncapped inventory, shared crafting dust, " +
                "deck copy limits, full disassembly, pack ownership, rollback and opponent progression passed.");
        }

        private static void VerifySaveSync(string save, UserDeck starter)
        {
            string secondDevice = Path.Combine(save, "second-device");
            Directory.CreateDirectory(secondDevice);
            var source = new SaveSyncFiles(save);
            var destination = new SaveSyncFiles(secondDevice);
            var start = source.Capture();
            start.decks.Add(new SaveSyncSnapshot.Deck { id = starter.id, json = JsonUtility.ToJson(starter) });
            var empty = destination.Capture();
            Require(SaveSyncSnapshot.Decide(start, start.Hash, null, null) == SaveSyncAction.Upload &&
                SaveSyncSnapshot.Decide(empty, empty.Hash, start.Hash, null) == SaveSyncAction.Download,
                "A first connection could not seed the cloud or restore a fresh device.");
            Require(destination.Restore(start, empty.Hash) && destination.Capture().Hash == start.Hash,
                "The destination did not get the profile and all decks.");
            destination.SetBaseline(start.Hash);
            Require(destination.Baseline == start.Hash, "The last synced version did not persist.");

            var changed = SaveSyncSnapshot.Parse(start.Serialize());
            changed.decks.Clear();
            Require(SaveSyncSnapshot.Decide(changed, changed.Hash, start.Hash, start.Hash) == SaveSyncAction.Upload &&
                SaveSyncSnapshot.Decide(start, start.Hash, changed.Hash, start.Hash) == SaveSyncAction.Download &&
                SaveSyncSnapshot.Decide(changed, changed.Hash, start.Hash, null) == SaveSyncAction.Conflict &&
                SaveSyncSnapshot.Decide(changed, changed.Hash, start.Hash, empty.Hash) == SaveSyncAction.Conflict,
                "The sync rules did not distinguish one-sided changes from conflicts.");
            Require(!destination.Restore(changed, empty.Hash) && destination.Capture().Hash == start.Hash,
                "A stale download replaced a locally changed save.");
            Require(destination.Restore(changed, start.Hash) && destination.Capture().Hash == changed.Hash &&
                SaveSyncSnapshot.Parse(File.ReadAllText(Path.Combine(secondDevice, "cloud-save-previous-local.json"))).Hash == start.Hash,
                "Cloud deck deletion or local backup did not work.");

            SaveSyncFiles.WriteAtomic(Path.Combine(secondDevice, "cloud-save-restore.pending"), start.Serialize());
            destination.Recover();
            Require(destination.Capture().Hash == start.Hash && !File.Exists(Path.Combine(secondDevice, "cloud-save-restore.pending")),
                "Recovery left an interrupted cloud restore unfinished.");
            Require(!SaveSyncSnapshot.ValidDeckId("../outside") && !SaveSyncSnapshot.ValidDeckId("C:outside"),
                "A remote deck could escape the game's deck folder.");

            if (SaveSyncCredential.Supported)
            {
                const string placeholder = "fixture-credential-never-sent";
                SaveSyncCredential.Write(secondDevice, placeholder);
                var stored = File.ReadAllText(Path.Combine(secondDevice, "cloud-save-token.dpapi"));
                Require(!stored.Contains(placeholder) && SaveSyncCredential.Read(secondDevice) == placeholder,
                    "The Windows credential could not be read from encrypted storage.");
                SaveSyncCredential.Write(secondDevice, null);
                Require(SaveSyncCredential.Read(secondDevice) == null, "Disconnect did not remove the local credential.");
            }
            Debug.Log("[VERIFY SAVE SYNC] First launch, handoff, conflict, stale writes, restore recovery and credential storage passed.");
        }


        private static uint[] LoadLegacyProfile(string save, string profile)
        {
            var materials = Rarities.Select(rarity => CardDatabase.AllCards.First(card =>
                card.code != AshBlossom && card.alias == 0 && card.IsMonster && CollectionProgress.IsCraftableCard(card) &&
                CollectionProgress.GetOwnedCount(card.code) == 0 &&
                CardRarityCatalog.GetRarity(card.code) == rarity).code).ToArray();

            string json = File.ReadAllText(profile);
            json = Regex.Replace(json, "\"version\"\\s*:\\s*\\d+", "\"version\": 3");
            json = Regex.Replace(json, "\"craftDust\"\\s*:\\s*\"[^\"]*\",\\s*", "");
            for (int i = 0; i < materials.Length; i++)
                json = WithOwnership(json, materials[i], i == (int)CardRarity.UltraRare ? 15 : 3);
            foreach (uint code in DrawSpells) json = WithOwnership(json, code, 4);
            File.WriteAllText(profile, json);
            CollectionProgress.Load(save);
            Require(CollectionProgress.HasStarter && CollectionProgress.Balance == CollectionProgress.StartingBalance &&
                CollectionProgress.CraftDust == 0 &&
                CollectionProgress.GetOwnedCount(materials[(int)CardRarity.UltraRare]) == 15 &&
                DrawSpells.All(code => CollectionProgress.GetOwnedCount(code) == 4),
                "A legacy profile without crafting balances did not load correctly.");
            string legacy = Regex.Replace(json, "\"version\"\\s*:\\s*\\d+",
                "\"version\": 5,\n    \"craftPoints\": [5, 7, 12, 16]");
            string maxBalances = legacy.Replace("[5, 7, 12, 16]",
                $"[{long.MaxValue}, {long.MaxValue}, {long.MaxValue}, {long.MaxValue}]");
            File.WriteAllText(profile, maxBalances);
            CollectionProgress.Load(save);
            Require(CollectionProgress.CraftDust == 4m * long.MaxValue,
                "Adding the old rarity balances overflowed.");
            File.WriteAllText(profile, legacy);
            Require(new SaveSyncFiles(save).Capture().profile == legacy, "Cloud sync rejected an old crafting save.");
            CollectionProgress.Load(save);
            Require(CollectionProgress.CraftDust == 40, "The old rarity balances did not migrate to a single pool.");
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
            Require(CollectionProgress.GetDisassembleLimit(normal) == 3 &&
                CollectionProgress.GetDisassembleLimit(starter.main[0]) == CollectionProgress.GetOwnedCount(starter.main[0]),
                "Cards placed in a deck were excluded from disassembly.");

            string before = File.ReadAllText(profile);
            Require(!CollectionProgress.TryDisassembleCards(new Dictionary<uint, int> { [rare] = 1, [normal] = 4 }, out _) &&
                File.ReadAllText(profile) == before && CollectionProgress.GetOwnedCount(rare) == 3,
                "A batch that exceeded ownership partially disassembled cards.");
            Require(CollectionProgress.TryDisassembleCards(new Dictionary<uint, int>
            {
                [normal] = 3,
                [rare] = 3,
                [super] = 2,
                [ultra] = 15,
            }, out string error), error);
            Require(CollectionProgress.CraftDust == 40 + 23 * CollectionProgress.DisassemblyDustPerCard &&
                Rarities.Select(CollectionProgress.GetCraftDustCost).SequenceEqual(new[] { 30, 60, 90, 120 }),
                "Bulk disassembly did not award shared dust or crafting costs did not increase by rarity.");
            Require(!CollectionProgress.TryCraftCard(AshBlossom, 0, out _), "A zero craft quantity was accepted.");
            Require(CollectionProgress.TryCraftCard(AshBlossom, 1, out error) &&
                CollectionProgress.GetOwnedCount(AshBlossom) == 1 &&
                CollectionProgress.CraftDust == 150, error);
            Require(!CollectionProgress.TryCraftCard(rare, 3, out _) && CollectionProgress.CraftDust == 150 &&
                CollectionProgress.GetOwnedCount(rare) == 0, "Crafting used more dust than the shared balance.");
            Require(CollectionProgress.TryCraftCard(normal, 5, out error) &&
                CollectionProgress.GetOwnedCount(normal) == 5 && CollectionProgress.CraftDust == 0, error);
            Require(!CollectionProgress.TryCraftCard(AshBlossom, 1, out _) &&
                CollectionProgress.Balance == CollectionProgress.StartingBalance &&
                !File.ReadAllText(profile).Contains("\"craftPoints\""),
                "Crafting changed DP, accepted an overspend, or saved the old rarity balances.");
        }

        private static void VerifyPurchasesAndRewards(string save, string profile)
        {
            int expectedDp = CollectionProgress.Balance;
            decimal expectedDust = CollectionProgress.CraftDust;
            var pack = CollectionProgress.Packs.Single(item => item.Id == SupportPack);
            string stockedProfile = File.ReadAllText(profile);
            foreach (uint code in pack.CardPool)
                stockedProfile = WithOwnership(stockedProfile, code, Math.Max(3, CollectionProgress.GetOwnedCount(code)));
            File.WriteAllText(profile, stockedProfile);
            CollectionProgress.Load(save);
            var beforePurchase = pack.CardPool.ToDictionary(code => code, CollectionProgress.GetOwnedCount);
            Require(CollectionProgress.TryBuyPack(SupportPack, out var single, out string error), error);
            expectedDp -= CollectionProgress.PackCost;
            Require(ValidPack(single, pack) && CollectionProgress.Balance == expectedDp,
                "A single pack did not contain eight cards from its own pool at the shown price.");
            Require(CollectionProgress.TryBuyPacks(SupportPack, CollectionProgress.PackBundleSize, out var bundle, out error), error);
            expectedDp -= CollectionProgress.PackBundleSize * CollectionProgress.PackCost;
            Require(bundle.Count == CollectionProgress.PackBundleSize && bundle.All(pulls => ValidPack(pulls, pack)) &&
                CollectionProgress.Balance == expectedDp &&
                single.Concat(bundle.SelectMany(pulls => pulls)).GroupBy(code => code).All(group =>
                    CollectionProgress.GetOwnedCount(group.Key) == beforePurchase[group.Key] + group.Count()),
                "A bundle did not retain every pull above the former three-copy inventory cap.");

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
                CollectionProgress.CraftDust == expectedDust &&
                CollectionProgress.GetOwnedCount(AshBlossom) > 0 &&
                CollectionProgress.IsOpponentDeckCleared(first.id, levels[0].id) &&
                CollectionProgress.IsOpponentDeckUnlocked(first.id, levels[1].id) &&
                CollectionProgress.IsOpponentUnlocked(opponent.id),
                "Reload lost crafted ownership, dust, DP, or opponent progression.");
        }

        private static string WithOwnership(string json, uint code, int count)
        {
            var existing = new Regex("\\{\\s*\"code\"\\s*:\\s*" + code + "\\s*,\\s*\"count\"\\s*:\\s*\\d+\\s*\\}");
            string entry = $"{{\"code\":{code},\"count\":{count}}}";
            return existing.IsMatch(json) ? existing.Replace(json, entry) :
                new Regex("\"cards\"\\s*:\\s*\\[").Replace(json, "\"cards\":[" + entry + ",", 1);
        }

        private static void VerifyDeckCopyLimitAndFullDisassembly(UserDeck starter)
        {
            var deck = new UserDeck
            {
                main = starter.main.Where(code => !DrawSpells.Contains(code)).Take(31)
                    .Concat(DrawSpells.SelectMany(code => Enumerable.Repeat(code, 3))).ToList(),
            };
            Require(CollectionProgress.CanUseDeck(deck, out string reason),
                $"Three copies of the draw spells were rejected: {reason}");
            deck.side.Add(GracefulCharity);
            Require(!CollectionProgress.CanUseDeck(deck, out reason) && reason.Contains("3장"),
                "A fourth copy in Side bypassed the total deck copy limit.");
            deck.side.Clear();

            int copies = CollectionProgress.GetOwnedCount(GracefulCharity);
            Require(CollectionProgress.TryDisassembleCards(new Dictionary<uint, int> { [GracefulCharity] = copies },
                    out string error) && CollectionProgress.GetOwnedCount(GracefulCharity) == 0 &&
                !CollectionProgress.CanUseDeck(deck, out reason) && reason.Contains("보유 0장"),
                $"Full disassembly did not update deck eligibility: {error}");
        }

        private static void VerifyMyDeckPicker(UserDeck starter)
        {
            UserDeck Copy(string id, string name) => new UserDeck
            {
                id = id,
                name = name,
                main = starter.main.ToList(),
                extra = starter.extra.ToList(),
                side = starter.side.ToList(),
            };

            var first = Copy("picker-first", "First playable");
            var second = Copy("picker-second", "Second playable");
            var incomplete = new UserDeck { id = "picker-short", name = "Incomplete deck" };
            var missingCard = Copy("picker-unowned", "Unowned card deck");
            var presentKeys = new HashSet<uint>(starter.main.Concat(starter.extra).Concat(starter.side)
                .Select(CollectionProgress.GetDeckCopyKey));
            missingCard.main[0] = CardDatabase.AllCards.First(card => CollectionProgress.IsCraftableCard(card) &&
                !card.IsExtra && CollectionProgress.GetOwnedCount(card.code) == 0 &&
                !presentKeys.Contains(CollectionProgress.GetDeckCopyKey(card.code))).code;
            Require(!CollectionProgress.CanUseDeck(missingCard, out string reason) && reason.Contains("보유 0장"),
                "The ownership fixture must fail solely for a missing card.");

            var savedDecks = DeckRepository.UserDecks.ToArray();
            var canvas = new GameObject("Playable Deck Picker", typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
            try
            {
                DeckRepository.UserDecks.Clear();
                DeckRepository.UserDecks.AddRange(new[] { incomplete, first, missingCard, second });
                var ui = new OpponentSelectUI(null, canvas.transform);
                ui.Open();
                var picker = ui.Root.transform.Find("OppDetailPanel/MyDeckBox/MyDeckPicker/Dropdown").GetComponent<Dropdown>();
                var start = ui.Root.transform.Find("OppDetailPanel/StartDuelBtn/B").GetComponent<Button>();
                Require(picker.interactable && picker.options.Count == 2 && picker.options[0].text.StartsWith(first.name) &&
                    picker.options[1].text.StartsWith(second.name) && start.interactable,
                    "The list included an unplayable deck or omitted a playable deck.");

                picker.value = 1;
                var updated = Copy(second.id, "Renamed playable deck");
                DeckRepository.UserDecks.Remove(second);
                DeckRepository.UserDecks.Insert(0, updated);
                ui.Open();
                Require(picker.value == 0 && picker.captionText.text.StartsWith(updated.name),
                    "Reopening the screen did not retain the selected deck after a reorder or edit.");

                updated.main.Clear();
                ui.Open();
                Require(picker.options.Count == 1 && picker.captionText.text.StartsWith(first.name) && start.interactable,
                    "The picker did not fall back to an eligible deck after the selection became unusable.");
                DeckRepository.UserDecks.Remove(first);
                ui.Open();
                Require(picker.options.Count == 0 && !picker.interactable && !start.interactable &&
                    picker.captionText.text.Contains("사용 가능한 덱 없음"),
                    "The empty state did not disable Duel Start and explain that no deck can be used.");
                Debug.Log("[VERIFY MY DECK PICKER] Playable-only options, retained selection and empty state passed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
                DeckRepository.UserDecks.Clear();
                DeckRepository.UserDecks.AddRange(savedDecks);
            }
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
