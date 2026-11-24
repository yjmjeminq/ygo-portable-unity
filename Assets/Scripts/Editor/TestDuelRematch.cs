using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.AI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static class TestDuelRematch
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static void Run()
        {
            var opponent = OpponentDeckLevels.GetDecks(104).First();
            var player = DeckRepository.PresetDecks.First(d => d.charId != opponent.charId);
            var deck = new UserDeck { name = player.title, main = player.main.ToList(), extra = player.extra.ToList() };
            var canvas = new GameObject("Duel Rematch Verification", typeof(Canvas));
            DuelFieldUI field = null;
            try
            {
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600, 900);
                var controller = canvas.AddComponent<GameAppController>();
                var modals = new DuelPromptModals(canvas.transform, code => field?.InspectCard(code),
                    _ => throw new InvalidOperationException("Rematch unexpectedly submitted a player response."));
                field = new DuelFieldUI(controller, canvas.transform, modals);
                FindField(controller, "_duelField").SetValue(controller, field);
                FindField(controller, "_modals").SetValue(controller, modals);

                VerifyDifficultySettings(controller, canvas.transform);
                var character = DeckRepository.GetCharacter(opponent.charId);
                controller.StartDuelMatch(character, opponent, deck, playerGoesFirst: false);
                var masterDuel = (DuelSession)FindField(controller, "_session").GetValue(controller);
                var masterAi = (TacticalAI)FindField(controller, "_ai").GetValue(controller);
                Require(masterAi.Difficulty == AIDifficulty.Master && masterAi.UsesTrainedPolicy && masterAi.HasLinkPolicy,
                    "The default gameplay opponent did not use Master and its included policies.");
                VerifyMainPhaseTiming(controller, masterDuel, master: true);

                controller.AiDifficulty = AIDifficulty.Normal;
                var ranked = new RankedMatch(new RankedSave(), new RankedOpponent(character, opponent, 1, 1f), deck, true);
                var rankedUi = new OfflineRankedUI(controller, canvas.transform);
                FindField(controller, "_ranked").SetValue(controller, rankedUi);
                FindField(controller, "_screen").SetValue(controller, AppScreen.Ranked);
                FindField(rankedUi, "_match").SetValue(rankedUi, ranked);
                rankedUi.Root.SetActive(true);
                ((RectTransform)FindField(rankedUi, "_versus").GetValue(rankedUi)).gameObject.SetActive(true);
                controller.StartRankedDuel(ranked);
                var rankedAi = (TacticalAI)FindField(controller, "_ai").GetValue(controller);
                Require(rankedAi != masterAi && rankedAi.Difficulty == AIDifficulty.Normal && !rankedAi.UsesTrainedPolicy,
                    "The ranked opponent overrode the selected Normal difficulty.");
                rankedUi.Close();
                FindField(controller, "_activeRanked").SetValue(controller, null);
                FindField(controller, "_screen").SetValue(controller, AppScreen.Duel);

                controller.StartDuelMatch(character, opponent, deck, playerGoesFirst: false);
                var firstDuel = (DuelSession)FindField(controller, "_session").GetValue(controller);
                var firstAi = (TacticalAI)FindField(controller, "_ai").GetValue(controller);
                Require(firstDuel.NativePtr != IntPtr.Zero && !firstDuel.IsFinished && firstDuel.HumanPlayerIndex == 1,
                    "The first duel did not start with the AI going first.");
                VerifyMainPhaseTiming(controller, firstDuel, master: false);
                firstAi.Difficulty = AIDifficulty.Smart;
                Require(firstAi.UsesTrainedPolicy && firstAi.HasLinkPolicy, "The gameplay opponent could not load the included duel policies.");
                firstAi.Difficulty = AIDifficulty.Normal;

                // Exhaust this optional effect's per-turn allowance without submitting a synthetic prompt to the core.
                var effect = new DuelPrompt
                {
                    messageType = YgoConstants.MSG_SELECT_EFFECTYN,
                    playerId = (byte)firstDuel.AiPlayerIndex,
                    promptCardCode = opponent.sigCard,
                };
                for (int i = 0; i < 3; i++)
                    Require(BitConverter.ToInt32(firstAi.ComputeResponse(firstDuel, effect), 0) == 1, "Could not prime the AI's turn state.");
                Require(BitConverter.ToInt32(firstAi.ComputeResponse(firstDuel, effect), 0) == 0,
                    "The original duel did not consume the expected effect allowance.");

                Invoke(field, "OnPauseClicked");
                Require(field.IsPaused, "The pause menu did not enter its paused state.");
                firstDuel.Surrender(firstDuel.HumanPlayerIndex);
                Invoke(controller, "Update");
                Require(modals.ResultRoot.activeSelf, "The surrendered duel did not show its result.");
                modals.TriggerRematch();

                var rematch = (DuelSession)FindField(controller, "_session").GetValue(controller);
                var rematchAi = (TacticalAI)FindField(controller, "_ai").GetValue(controller);
                Require(rematch != firstDuel && firstDuel.NativePtr == IntPtr.Zero && rematch.NativePtr != IntPtr.Zero && !rematch.IsFinished,
                    "Rematching did not release and replace the old duel.");
                Require(rematch.HumanPlayerIndex == 1 && rematch.TurnNumber == firstDuel.TurnNumber && !field.IsPaused && !modals.ResultRoot.activeSelf,
                    "The rematch did not preserve player order with a cleared pause and result.");
                Require(rematchAi.Difficulty == AIDifficulty.Normal && BitConverter.ToInt32(rematchAi.ComputeResponse(rematch, effect), 0) == 1,
                    "The new duel inherited the old AI's optional-effect allowance.");
                Debug.Log("[VERIFY REMATCH] Surrender and same-turn rematch preserved turn order, released the old duel, and reset pause and AI state.");
            }
            finally
            {
                field?.UnbindSession();
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        private static void VerifyDifficultySettings(GameAppController controller, Transform root)
        {
            const string key = "ygo.ai-difficulty.v1";
            int previous = PlayerPrefs.GetInt(key, int.MinValue);
            try
            {
                PlayerPrefs.DeleteKey(key);
                Require(controller.AiDifficulty == AIDifficulty.Master && GameAppController.ReadAiDifficulty() == AIDifficulty.Master,
                    "The default AI difficulty was not Master.");
                var menu = new TitleAndMenuUI(controller, root);
                menu.MenuRoot.transform.Find("Menu/Settings/Button").GetComponent<Button>().onClick.Invoke();
                var panel = menu.SettingsRoot.transform.Find("SettingsWin");
                var speed = (RectTransform)panel.Find("AiRow");
                var difficulty = (RectTransform)panel.Find("AiDiffRow");
                var replay = (RectTransform)panel.Find("PersonalReplay");
                var label = difficulty.Find("Label/Text").GetComponent<Text>();
                var toggle = difficulty.Find("BtnDiff/B").GetComponent<Button>();
                Require(toggle.IsActive() && toggle.IsInteractable() && label.text.Contains("마스터 (0.6초 수읽기)") &&
                    speed.anchorMin.y > difficulty.anchorMax.y && difficulty.anchorMin.y > replay.anchorMax.y,
                    "The difficulty control was missing, overlapped other rows, or showed the wrong default.");
                toggle.onClick.Invoke();
                Require(controller.AiDifficulty == AIDifficulty.Normal && GameAppController.ReadAiDifficulty() == AIDifficulty.Normal &&
                    label.text.Contains("보통"), "One click did not apply, save and display Normal difficulty.");
                menu.CloseSettings();
                menu.MenuRoot.transform.Find("Menu/Settings/Button").GetComponent<Button>().onClick.Invoke();
                Require(label.text.Contains("보통"), "Reopening Settings lost the selected difficulty.");
                toggle.onClick.Invoke();
                Require(controller.AiDifficulty == AIDifficulty.Master && GameAppController.ReadAiDifficulty() == AIDifficulty.Master &&
                    label.text.Contains("마스터"), "One click did not restore, save and display Master difficulty.");
                menu.CloseSettings();
            }
            finally
            {
                if (previous == int.MinValue) PlayerPrefs.DeleteKey(key);
                else PlayerPrefs.SetInt(key, previous);
                PlayerPrefs.Save();
            }
        }

        private static void VerifyMainPhaseTiming(GameAppController controller, DuelSession duel, bool master)
        {
            var tick = typeof(GameAppController).GetMethod("AdvanceAiDecision", PrivateInstance);
            var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, playerId = (byte)duel.AiPlayerIndex };
            bool Tick(DuelPrompt prompt, float delta, double now) => (bool)tick.Invoke(controller, new object[] { prompt, delta, now });
            Require(Tick(idle, 1f, 0) == !master, "The selected difficulty did not control the initial AI response.");
            if (!master) return;
            Require(!Tick(idle, 1f, .599) && Tick(idle, 0f, .6), "The Master main-phase decision did not wait at least 0.6 real seconds.");
            var option = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_OPTION, playerId = idle.playerId };
            Require(Tick(option, 1f, .6), "The Master minimum also delayed a card's resolution prompt.");
            Debug.Log("[VERIFY AI DIFFICULTY] Settings toggle, saved preference, default Master and 600 ms Main Phase floor passed.");
        }

        private static FieldInfo FindField(object owner, string name) => owner.GetType().GetField(name, PrivateInstance);
        private static void Invoke(object owner, string name) => owner.GetType().GetMethod(name, PrivateInstance).Invoke(owner, null);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException($"[REMATCH] {message}");
        }
    }
}
