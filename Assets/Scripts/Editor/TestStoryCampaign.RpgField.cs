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
    internal static partial class TestStoryCampaign
    {
        private static void VerifyRpgField(UserDeck playerDeck, string save)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            string streaming = Application.streamingAssetsPath;
            var script = StoryModeUI.LoadRpgScript(streaming);
            var decks = StoryModeUI.LoadRpgDecks(streaming);
            var regions = TagForceWorldUI.RpgRegions;
            int[] gates = { 0, 0, 6, 9, 11, 19, 27, 35, 44 };
            Require(regions.Select(region => region.RequiredEpisode).SequenceEqual(gates),
                "The nine RPG region gates did not match the campaign.");
            for (int i = 0; i <= 50; i++)
            {
                int count = TagForceWorldUI.CountClearedRpgEpisodes(script, id =>
                    script.episodes.Take(i).Any(ep => ep.id == id));
                Require(count == i && regions[TagForceWorldUI.RegionForEpisode(i + 1)].IsOpen(i),
                    $"No reachable target after {i} RPG clears.");
                foreach (var region in regions)
                    Require(region.IsOpen(i) == (i >= region.RequiredEpisode), $"Bad unlock gate: {region.Id}.");
            }
            foreach (var region in regions)
                Require(File.Exists(Path.Combine(streaming, region.Background)), $"Missing 5D's map {region.Id}.");

            const string preference = "ygo.5ds-rpg.last-region";
            string oldPreference = PlayerPrefs.GetString(preference);
            bool hadPreference = PlayerPrefs.HasKey(preference);
            PlayerPrefs.DeleteKey(preference);
            var canvas = new GameObject("5D's field verification", typeof(Canvas));
            var events = new GameObject("5D's field controls", typeof(EventSystem));
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
            var app = canvas.AddComponent<GameAppController>();
            var story = new StoryModeUI(app, canvas.transform);
            var world = new TagForceWorldUI(app, canvas.transform);
            var menu = new TitleAndMenuUI(app, canvas.transform);
            var modals = new DuelPromptModals(canvas.transform, null, null);
            var panel = world.Root.transform.Find("World");
            var storyRoot = story.Root.transform.Find("StoryDialogue");
            var loaded = (StoryScriptRoot)typeof(StoryModeUI).GetField("_rpgScript", flags).GetValue(story);
            var episode6 = loaded.episodes[5];
            try
            {
                Set("_titleAndMenu", menu);
                Set("_tagForceWorld", world);
                Set("_storyMode", story);
                Set("_archetypeCodex", new ArchetypeCodexUI(canvas.transform, () => { }));
                Set("_tournament", new DimensionTournamentUI(app, canvas.transform));
                Set("_ranked", new OfflineRankedUI(app, canvas.transform));
                Set("_oppSelect", new OpponentSelectUI(app, canvas.transform));
                Set("_deckEditor", new DeckEditorUI(app, canvas.transform));
                Set("_modals", modals);
                Set("_duelField", new DuelFieldUI(app, canvas.transform, modals));
                Set("_initialized", true);

                app.ShowScreen(AppScreen.MainMenu);
                var entry = menu.MenuRoot.GetComponentsInChildren<Button>()
                    .Single(button => button.transform.parent.name == "RpgWorld");
                entry.onClick.Invoke();
                Require(world.Root.activeSelf && panel.Find("Player/Sprite").GetComponent<Image>().sprite != null &&
                    panel.Find("MainQuestNpc/MainQuestMarker").GetComponent<Image>().sprite != null &&
                    world.CanStartRpgEpisode(0) && !world.CanStartRpgEpisode(1),
                    "RPG did not start as a walkable field with the first main quest.");
                Press(panel, "FieldHud/FieldMap");
                Require(panel.Find("spot_kai_garage/Travel").GetComponent<Button>().interactable &&
                    panel.Find("spot_satellite_hideout/Travel").GetComponent<Button>().interactable &&
                    panel.Find("spot_facility_prison/MapLock").GetComponent<Image>().sprite != null &&
                    !world.EnterRpgRegion(2), "The world map allowed early entry to the prison.");
                Press(world.Root.transform, "RpgNavigation/Decks");
                app.CloseDeckEditor();
                Require(panel.Find("spot_facility_prison/MapLock") != null,
                    "Returning from deck editing did not restore the RPG world map.");
                world.Back();

                VerifySceneGuests();

                var local = panel.Cast<Transform>().First(child => child.name.StartsWith("Resident_", StringComparison.Ordinal));
                Require(local.Find("ResidentMarker").GetComponent<Image>().sprite != null, "The resident marker was missing.");
                Press(local, "Interact");
                world.Update(4f);
                Press(panel, "ResidentChoices/TalkDaily");
                Require(panel.Find("ResidentChoices/DailyLine/Label").GetComponent<Text>().text.Contains("정비소"),
                    "The resident did not offer local dialogue.");
                Press(panel, "ResidentChoices/Practice");
                int balance = CollectionProgress.Balance;
                int owned = decks[script.episodes[1].deckId].main.Distinct().Sum(CollectionProgress.GetTotalOwnedCount);
                Press(panel, "StartPractice");
                AwardDuel(win: true);
                Require(world.Root.activeSelf && world.RpgClears == 0 &&
                    CollectionProgress.Balance == balance + 300 &&
                    decks[script.episodes[1].deckId].main.Distinct().Sum(CollectionProgress.GetTotalOwnedCount) == owned + 1,
                    "A practice win did not award DP and a card without advancing the story.");

                foreach (var episode in script.episodes.Take(5))
                    Require(CollectionProgress.TryAwardStoryDuel(episode.id, true, null, decks[episode.deckId],
                        out _, out _, out _), $"Could not stage {episode.id}.");
                Require(world.EnterRpgRegion(1) && world.CanStartRpgEpisode(5), "Episode 6 NPC was not in the hideout.");
                OpenEpisode6();
                app.StartStoryDuel(episode6, playerDeck, true);
                AwardDuel(win: false);
                Require(storyRoot.gameObject.activeSelf && world.Root.activeSelf && !world.CanWalk && world.RpgClears == 5,
                    "A loss did not stage its dialogue in the locked field.");
                Press(storyRoot, "Scene/DialogueBox/Skip/Button");
                Require(world.Root.activeSelf && world.CanStartRpgEpisode(5) && !world.EnterRpgRegion(2),
                    "A loss changed the active NPC or unlocked the next region.");

                OpenEpisode6();
                app.StartStoryDuel(episode6, playerDeck, true);
                balance = CollectionProgress.Balance;
                AwardDuel(win: true);
                Require(storyRoot.gameObject.activeSelf && world.RpgClears == 5 && CollectionProgress.Balance == balance,
                    "The win was settled before the post-win dialogue.");
                Press(storyRoot, "Scene/DialogueBox/Skip/Button");
                Require(world.Root.activeSelf && world.RpgClears == 6 && CollectionProgress.Balance > balance &&
                    panel.Find("RegionUnlockToast/Label").GetComponent<Text>().text.Contains("시큐리티 수용소"),
                    "Winning did not save the clear, pay DP and announce the new region.");
                CollectionProgress.Load(save);
                Require(world.EnterRpgRegion(2) && world.CanStartRpgEpisode(6) && world.RpgClears == 6,
                    "The next quest or unlocked region was lost after reloading the save.");
                Debug.Log("[VERIFY 5DS FIELD] Map gates, guest entrances, movement lock, practice rewards, story result order and saved progression passed.");
            }
            finally
            {
                (Get("_session") as DuelSession)?.Dispose();
                world.Close();
                story.Close();
                UnityEngine.Object.DestroyImmediate(canvas);
                UnityEngine.Object.DestroyImmediate(events);
                if (hadPreference) PlayerPrefs.SetString(preference, oldPreference);
                else PlayerPrefs.DeleteKey(preference);
                PlayerPrefs.Save();
            }

            void VerifySceneGuests()
            {
                var player = (RectTransform)panel.Find("Player");
                var opponent = panel.Find("MainQuestNpc");
                Press(panel, "MainQuestNpc/Interact");
                world.Update(4f);
                Vector2 position = player.anchorMin;
                var cast = panel.Find("DialogueCast");
                var guest = (RectTransform)cast.Find("Guest_1");
                Require(storyRoot.gameObject.activeSelf && world.Root.activeSelf && !world.CanWalk &&
                    world.Root.transform.GetSiblingIndex() < story.Root.transform.GetSiblingIndex() &&
                    panel.Find("Player") == player && panel.Find("MainQuestNpc") == opponent &&
                    !storyRoot.Find("Scene/Portraits").gameObject.activeSelf &&
                    !guest.gameObject.activeSelf && guest.Find("Sprite").GetComponent<Image>().sprite != null &&
                    !world.Root.GetComponent<CanvasGroup>().blocksRaycasts,
                    "The conversation did not preserve the real field or hold Aki until her first line.");
                world.Update(.5f, Vector2.right);
                Require(player.anchorMin == position && !world.EnterRpgRegion(1) &&
                    !world.HandleFieldInput(new DeckPadFrame(Vector2Int.right, DeckPadButtons.Submit | DeckPadButtons.Search)),
                    "The player could move, interact or travel during the conversation.");

                int firstAkiLine = Array.FindIndex(loaded.episodes[0].preDialogues, line => line.speaker == "이자요이 아키");
                Require(firstAkiLine > 0, "The first field conversation lost Aki's entrance.");
                for (int i = 0; i < firstAkiLine; i++)
                {
                    Press(storyRoot, "Scene/DialogueBox/Next/Button");
                    Press(storyRoot, "Scene/DialogueBox/Next/Button");
                }
                Vector2 entry = guest.anchorMin;
                story.Update(.30f);
                Require(guest.gameObject.activeSelf && guest.anchorMin != entry &&
                    guest.Find("SpeakingCue") != null && opponent.gameObject.activeSelf && player.gameObject.activeSelf,
                    "Aki did not walk in as a separate speaker beside the player and the opponent.");
                Press(storyRoot, "Scene/DialogueBox/Previous/Button");
                Require(!guest.gameObject.activeSelf, "Rewinding retained a guest who had yet to arrive.");
                Press(storyRoot, "Scene/DialogueBox/Next/Button");
                story.Update(.65f);
                Require(guest.gameObject.activeSelf && guest.GetComponent<CanvasGroup>().alpha == 1f &&
                    guest.anchorMin != entry, "The guest did not reach her mark after replaying the entrance.");
                app.OpenStoryDeckEditor();
                Require(!world.Root.activeSelf, "The field remained over the deck editor.");
                app.CloseDeckEditor();
                Require(guest.gameObject.activeInHierarchy && !world.CanWalk, "Deck editing lost the active field scene.");
                story.Back();
                Require(world.CanWalk && panel.Find("DialogueCast") == null &&
                    ((RectTransform)panel.Find("Player")).anchorMin == position &&
                    world.Root.GetComponent<CanvasGroup>().blocksRaycasts,
                    "Leaving the conversation did not remove its cast and restore movement at the original location.");
            }

            void OpenEpisode6()
            {
                var resident = (RectTransform)panel.Find("Resident_1");
                Require(resident != null, "Aki was missing from the hideout before the conversation.");
                Vector2 residentPosition = resident.anchorMin;
                Press(panel, "MainQuestNpc/Interact");
                world.Update(4f);
                Require(storyRoot.gameObject.activeSelf && story.FromRpgField && world.Root.activeSelf && !world.CanWalk &&
                    story.GetOpponentMatch(episode6).Deck.id == 59006,
                    "The current field NPC did not open the corresponding cinematic and RPG deck.");
                Require(resident.gameObject.activeInHierarchy && panel.Find("DialogueCast/Guest_1") == null,
                    "The dialogue replaced an existing field resident with a duplicate.");
                Press(storyRoot, "Scene/DialogueBox/Skip/Button");
                Press(storyRoot, "Scene/Choices/Choice_0/Button");
                Press(storyRoot, "Scene/DialogueBox/Skip/Button");
                Require(resident.anchorMin != residentPosition, "The resident did not join the conversation.");
                Press(storyRoot, "DeckSelection/Window/Apply/Button");
            }

            void AwardDuel(bool win)
            {
                var duel = Get("_session") as DuelSession;
                Require(duel?.NativePtr != IntPtr.Zero, "The native duel never started.");
                duel.Surrender(win ? duel.AiPlayerIndex : duel.HumanPlayerIndex);
                typeof(GameAppController).GetMethod("Update", flags).Invoke(app, null);
                Require(Get("_session") == null && duel.NativePtr == IntPtr.Zero, "The native duel never ended.");
            }

            void Set(string field, object value) => typeof(GameAppController).GetField(field, flags).SetValue(app, value);
            object Get(string field) => typeof(GameAppController).GetField(field, flags).GetValue(app);
            void Press(Transform parent, string path)
            {
                var button = parent.Find(path)?.GetComponent<Button>();
                Require(button != null && button.IsInteractable(), $"Expected button {path}.");
                button.onClick.Invoke();
            }
        }
    }
}
