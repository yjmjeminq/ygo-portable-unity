using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
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
            var opponent = DeckRepository.PresetDecks.First(d => d.id == 30000);
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

                controller.AiDifficulty = AIDifficulty.Normal;
                controller.StartDuelMatch(DeckRepository.GetCharacter(opponent.charId), opponent, deck, playerGoesFirst: false);
                var firstDuel = (DuelSession)FindField(controller, "_session").GetValue(controller);
                var firstAi = (TacticalAI)FindField(controller, "_ai").GetValue(controller);
                Require(firstDuel.NativePtr != IntPtr.Zero && !firstDuel.IsFinished && firstDuel.HumanPlayerIndex == 1,
                    "The first duel did not start with the AI going first.");
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

        private static FieldInfo FindField(object owner, string name) => owner.GetType().GetField(name, PrivateInstance);
        private static void Invoke(object owner, string name) => owner.GetType().GetMethod(name, PrivateInstance).Invoke(owner, null);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException($"[REMATCH] {message}");
        }
    }
}
