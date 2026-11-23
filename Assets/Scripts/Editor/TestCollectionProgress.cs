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
            bool openDecks = CollectionProgress.AllDeckCardsAvailable;
            CollectionProgress.AllDeckCardsAvailable = false;
            try { VerifyCollectionProgress(streamingPath); }
            finally { CollectionProgress.AllDeckCardsAvailable = openDecks; }
        }

        private static void VerifyCollectionProgress(string streamingPath)
        {
            string save = Path.Combine(Path.GetTempPath(), "ygo-collection-test-" + Guid.NewGuid().ToString("N"));
            string profile = Path.Combine(save, "collection-profile.json");
            DeckRepository.Load(streamingPath, save, seedDefaultDecks: false);
            CollectionProgress.Load(save);
            Require(!CollectionProgress.HasStarter && !CollectionProgress.TryBuyPack(SupportPack, out _, out _),
                "A fresh profile bought a pack without choosing a starter.");
            VerifyOpponentAccess();

            var starters = CollectionProgress.Starters;
            Require(starters.Count >= 400 && starters.Select(option => option.Id).Distinct().Count() == starters.Count &&
                starters.All(option => option.MainCount >= 40 && option.MainCount <= 60 && option.ExtraCount <= 15),
                "A starter pack was missing, duplicated, or had an illegal deck size.");
            var selection = starters.First(option => option.Name.Contains("상검"));
            Require(CollectionProgress.TryChooseStarter(selection.Id, out var starter, out string error), error);
            Require(!CollectionProgress.TryChooseStarter(selection.Id, out _, out _) &&
                CollectionProgress.CanUseDeck(starter, out _) && CollectionProgress.Balance == 5_000,
                "Starter selection did not grant the deck and starting DP exactly once.");

            VerifySaveSync(save, starter);
            var materials = LoadLegacyProfile(save, profile);
            VerifyCrafting(materials, starter, profile);
            VerifyPurchasesAndRewards(save, profile);
            VerifyMyDeckPicker(starter, save);
            VerifyDeckCopyLimitAndFullDisassembly(starter);
            Debug.Log($"[VERIFY COLLECTION] {starters.Count} starter deck sizes, legacy saves, uncapped inventory, shared crafting dust, " +
                "deck copy limits, full disassembly, pack ownership, rollback and opponent access passed.");
        }

        private static void VerifyOpponentAccess()
        {
            var opponents = DeckRepository.Characters.Where(character => OpponentDeckLevels.GetDecks(character.id).Count > 0).ToArray();
            Require(opponents.Length > 0 && CollectionProgress.UnlockedOpponentCount == opponents.Length,
                "Some opponents are missing from the available roster.");
            foreach (var opponent in opponents)
            {
                var decks = OpponentDeckLevels.GetDecks(opponent.id);
                Require(CollectionProgress.GetUnlockedDeckCount(opponent.id) == decks.Count &&
                    decks.All(deck => CollectionProgress.IsOpponentDeckUnlocked(opponent.id, deck.id)),
                    $"Some decks for {opponent.name} require a prior win.");
            }
            Debug.Log($"[VERIFY OPPONENT ACCESS] {opponents.Length} opponents and " +
                $"{opponents.Sum(opponent => OpponentDeckLevels.GetDecks(opponent.id).Count)} decks available.");
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
            json = Regex.Replace(json, "\"balance\"\\s*:\\s*\\d+", "\"balance\": 3000");
            json = Regex.Replace(json, "\"craftDust\"\\s*:\\s*\"[^\"]*\",\\s*", "");
            for (int i = 0; i < materials.Length; i++)
                json = WithOwnership(json, materials[i], i == (int)CardRarity.UltraRare ? 15 : 3);
            foreach (uint code in DrawSpells) json = WithOwnership(json, code, 4);
            File.WriteAllText(profile, json);
            CollectionProgress.Load(save);
            Require(CollectionProgress.HasStarter && CollectionProgress.Balance == 3000 &&
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
            int balanceBeforeCrafting = CollectionProgress.Balance;
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
            Require(CollectionProgress.CraftDust == 4025 &&
                materials.Select(CollectionProgress.GetDisassemblyDustPerCard).SequenceEqual(new[] { 5, 20, 80, 250 }) &&
                Rarities.Select(CollectionProgress.GetCraftDustCost).SequenceEqual(new[] { 30, 100, 300, 900 }),
                "Bulk disassembly did not award shared dust or crafting costs did not increase by rarity.");
            Require(!CollectionProgress.TryCraftCard(AshBlossom, 0, out _), "A zero craft quantity was accepted.");
            Require(CollectionProgress.TryCraftCard(AshBlossom, 1, out error) &&
                CollectionProgress.GetOwnedCount(AshBlossom) == 1 &&
                CollectionProgress.CraftDust == 3125, error);
            Require(!CollectionProgress.TryCraftCard(rare, 32, out _) && CollectionProgress.CraftDust == 3125 &&
                CollectionProgress.GetOwnedCount(rare) == 0, "Crafting used more dust than the shared balance.");
            Require(CollectionProgress.TryCraftCard(normal, 104, out error) &&
                CollectionProgress.GetOwnedCount(normal) == 104 && CollectionProgress.CraftDust == 5, error);
            Require(!CollectionProgress.TryCraftCard(AshBlossom, 1, out _) &&
                CollectionProgress.Balance == balanceBeforeCrafting &&
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
            var beforePurchase = pack.CardPool.ToDictionary(code => code, CollectionProgress.GetTotalOwnedCount);
            Require(CollectionProgress.TryBuyPack(SupportPack, out var single, out string error), error);
            expectedDp -= CollectionProgress.PackCost;
            Require(ValidPack(single, pack) && CollectionProgress.Balance == expectedDp,
                "A single pack did not contain eight cards from its own pool at the shown price.");
            Require(CollectionProgress.TryBuyPacks(SupportPack, CollectionProgress.PackBundleSize, out var bundle, out error), error);
            expectedDp -= CollectionProgress.PackBundleSize * CollectionProgress.PackCost;
            Require(bundle.Count == CollectionProgress.PackBundleSize && bundle.All(pulls => ValidPack(pulls, pack)) &&
                bundle.SelectMany(pulls => pulls).Any(code => CardRarityCatalog.GetRarity(code) >= CardRarity.SuperRare) &&
                CollectionProgress.Balance == expectedDp &&
                single.Concat(bundle.SelectMany(pulls => pulls)).GroupBy(code => code).All(group =>
                    CollectionProgress.GetTotalOwnedCount(group.Key) == beforePurchase[group.Key] + group.Count()),
                "A bundle did not retain every pull above the former three-copy inventory cap.");

            var opponent = DeckRepository.Characters.Last(character => OpponentDeckLevels.GetDecks(character.id).Count > 1);
            var levels = OpponentDeckLevels.GetDecks(opponent.id);
            var finalDeck = levels[^1];
            const int retiredDeckId = int.MaxValue - 1;
            File.WriteAllText(profile, Regex.Replace(File.ReadAllText(profile), "\"opponentDeckProgress\"\\s*:\\s*\\[\\s*\\]",
                $"\"opponentDeckProgress\": [{{\"opponentId\":{opponent.id},\"unlockedDeckIds\":[],\"clearedDeckIds\":[{retiredDeckId}]}}]"));
            CollectionProgress.Load(save);
            VerifyOpponentAccess();
            Require(!CollectionProgress.TryAwardDuel(true, opponent.id, int.MaxValue, out _, out _) &&
                CollectionProgress.Balance == expectedDp, "An unknown deck accepted a reward.");
            Require(CollectionProgress.TryAwardDuel(false, opponent.id, finalDeck.id, out int loss, out error) &&
                loss == 150 && !CollectionProgress.IsOpponentDeckCleared(opponent.id, finalDeck.id), error);
            Require(CollectionProgress.TryAwardDuel(true, opponent.id, finalDeck.id, out int win, out error) &&
                win == 300 && CollectionProgress.IsOpponentCleared(opponent.id), error);
            expectedDp += loss + win;

            File.AppendAllText(profile, "\n");
            string changedExternally = File.ReadAllText(profile);
            Require(!CollectionProgress.TryAwardDuel(true, out _, out _) && CollectionProgress.Balance == expectedDp &&
                File.ReadAllText(profile) == changedExternally, "A stale profile overwrote a newer save.");
            CollectionProgress.Load(save);
            Require(CollectionProgress.Balance == expectedDp &&
                CollectionProgress.CraftDust == expectedDust &&
                CollectionProgress.GetOwnedCount(AshBlossom) > 0 &&
                CollectionProgress.IsOpponentDeckCleared(opponent.id, finalDeck.id) &&
                CollectionProgress.IsOpponentDeckCleared(opponent.id, retiredDeckId) &&
                !CollectionProgress.IsOpponentDeckCleared(opponent.id, levels[0].id) &&
                CollectionProgress.IsOpponentCleared(opponent.id),
                "Reload lost crafted ownership, dust, DP, or the completed duel.");
        }

        internal static string WithOwnership(string json, uint code, int count)
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

        private static void VerifyMyDeckPicker(UserDeck starter, string save)
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
                void BrowseAndSelect()
                {
                    ui.Open();
                    Require(!ui.IsDeckSetupOpen, "Opening Free Duel skipped the character browser.");
                    ui.Root.transform.Find("OpponentBrowser/CharGridPanel/GridArea/CharSlot_0_0")
                        .GetComponent<Button>().onClick.Invoke();
                    Require(ui.IsDeckSetupOpen, "Confirming a character did not open the deck choices.");
                }
                BrowseAndSelect();
                var setup = ui.Root.transform.Find("DuelSetup");
                var list = setup.Find("MyDeck/DeckList/Viewport/Content");
                var start = setup.Find("StartDuelBtn/B").GetComponent<Button>();
                Text RowName(int index) => list.GetChild(index).Find("Name/Value").GetComponent<Text>();
                Require(list.GetComponentsInChildren<Button>().Length == 2 && RowName(0).text == first.name &&
                    RowName(1).text == second.name && start.interactable,
                    "The list included an unplayable deck or omitted a playable deck.");

                list.GetChild(1).GetComponent<Button>().onClick.Invoke();
                Require(RowName(1).color == LotdUiKit.GoldAccent && string.IsNullOrEmpty(CollectionProgress.LastDuelDeckId),
                    "Browsing a deck did not highlight it, or was saved as a played deck.");
                CollectionProgress.RememberDuelDeck(second.id);
                CollectionProgress.Load(save);
                Require(CollectionProgress.LastDuelDeckId == second.id &&
                    new SaveSyncFiles(save).Capture().profile.Contains(second.id), "The last played deck did not round-trip through the profile.");
                var updated = Copy(second.id, "Renamed last played deck");
                DeckRepository.UserDecks.Remove(second);
                DeckRepository.UserDecks.Insert(1, updated);
                BrowseAndSelect();
                Require(RowName(0).text == updated.name && RowName(0).color == LotdUiKit.GoldAccent,
                    "Reopening the screen did not retain the selected deck after a reorder or edit.");

                updated.main.Clear();
                BrowseAndSelect();
                Require(list.GetComponentsInChildren<Button>().Length == 1 && RowName(0).text == first.name && start.interactable,
                    "The picker did not fall back to an eligible deck after the selection became unusable.");
                DeckRepository.UserDecks.Remove(first);
                BrowseAndSelect();
                Require(list.GetComponentsInChildren<Button>().Length == 0 && !start.interactable &&
                    setup.Find("MyDeck/DeckList/Empty/Value").GetComponent<Text>().text.Contains("사용 가능한 덱 없음"),
                    "The empty state did not disable Duel Start and explain that no deck can be used.");

                uint unowned = missingCard.main[0];
                string profileBeforeOpenAccess = File.ReadAllText(Path.Combine(save, CollectionProgress.ProfileFileName));
                CollectionProgress.AllDeckCardsAvailable = true;
                try
                {
                    BrowseAndSelect();
                    Require(list.GetComponentsInChildren<Button>().Length == 1 && RowName(0).text == missingCard.name &&
                        start.interactable && GameAppController.CanStartWithDeck(missingCard, out _) &&
                        CollectionProgress.GetDisassembleLimit(unowned) == 0 &&
                        File.ReadAllText(Path.Combine(save, CollectionProgress.ProfileFileName)) == profileBeforeOpenAccess,
                        "Open deck access did not admit the unowned card deck or affected the saved collection.");
                    Debug.Log("[VERIFY OPEN CARD ACCESS] Free Duel accepted a complete deck containing an unowned card.");
                }
                finally { CollectionProgress.AllDeckCardsAvailable = false; }

                var manyDecks = Enumerable.Range(0, 12).Select(i => Copy($"picker-many-{i}", $"Deck {i + 1}")).ToArray();
                DeckRepository.UserDecks.AddRange(manyDecks);
                CollectionProgress.RememberDuelDeck(manyDecks[^1].id);
                BrowseAndSelect();
                var scroll = setup.Find("MyDeck/DeckList").GetComponent<ScrollRect>();
                Require(RowName(manyDecks.Length - 1).color == LotdUiKit.GoldAccent && scroll.verticalNormalizedPosition < .01f,
                    "The list did not bring the last played deck into view.");
                Debug.Log("[VERIFY MY DECK PICKER] Playable decks, saved selection, empty state and scrolling passed.");
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
