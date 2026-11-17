using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.AI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static class TestDuelPadNavigation
    {
        private const uint Sparkman = 20721928;
        private const uint BurstLady = 58932615;
        private const int A = 0x1000;
        private const int B = 0x2000;
        private const int X = 0x4000;
        private const int Y = 0x8000;
        private const int LeftShoulder = 0x0100;
        private const int RightShoulder = 0x0200;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type Frame = typeof(DuelFieldUI).Assembly.GetType("YGOPortable.UI.DeckPadFrame");
        private static readonly Type Buttons = typeof(DuelFieldUI).Assembly.GetType("YGOPortable.UI.DeckPadButtons");

        internal static void Run()
        {
            foreach (bool playerFirst in new[] { true, false })
                VerifyDuel(playerFirst);
            Debug.Log("[VERIFY DUEL PAD] Both seats passed hand focus/detail, summon and placement, piles during a chain, phase changes, attack, mouse log and pad pause.");
        }

        private static void VerifyDuel(bool playerFirst)
        {
            var main = Enumerable.Repeat(Sparkman, 20).Concat(Enumerable.Repeat(BurstLady, 20)).ToArray();
            var enemy = Enumerable.Repeat(89631139u, 40).ToArray();
            using var session = new DuelSession();
            if (!session.StartNewDuel(main, new[] { 69248256u }, enemy, Array.Empty<uint>(), playerFirst, 8000,
                new ulong[] { 718, 391, 27182, 31 }))
                throw new InvalidOperationException("Could not create the controller test duel.");

            var canvasObject = new GameObject("Duel Pad Verification", typeof(Canvas), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600, 900);
            var previousEvents = EventSystem.current;
            var previousFocus = previousEvents?.currentSelectedGameObject;
            GameObject localEvents = null;
            var events = previousEvents;
            if (events == null)
            {
                localEvents = new GameObject("Pad Event System", typeof(EventSystem));
                events = localEvents.GetComponent<EventSystem>();
                if (EventSystem.current == null) Invoke(events, "OnEnable");
            }

            DuelFieldUI field = null;
            try
            {
                var modals = new DuelPromptModals(canvas.transform, code => field.InspectCard(code), bytes => session.SubmitResponse(bytes));
                var app = canvasObject.AddComponent<GameAppController>();
                field = new DuelFieldUI(app, canvas.transform, modals);
                typeof(GameAppController).GetField("_session", Private).SetValue(app, session);
                typeof(GameAppController).GetField("_duelField", Private).SetValue(app, field);
                Advance(session, prompt => prompt.playerId == session.HumanPlayerIndex && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD);
                field.BindSession(session, null, null);
                Canvas.ForceUpdateCanvases();
                var mine = session.Players[session.HumanPlayerIndex];
                var enemyField = session.Players[session.AiPlayerIndex];
                var idle = session.ActivePrompt;

                Pad(field, LeftShoulder);
                var initial = Get<RectTransform>(field, "_duelPadFocus");
                Pad(field, direction: Vector2Int.right);
                var chosen = Get<RectTransform>(field, "_duelPadFocus");
                Require(chosen != initial && chosen.IsChildOf(field.Root.transform.Find("PlayerHand")),
                    "Moving right from the first card did not reach another card in the hand.");
                Pad(field, X);
                Require(Get<GameObject>(field, "_cardZoomModal").activeSelf, "The focused card could not be inspected.");
                Pad(field, B);
                Require(Get<RectTransform>(field, "_duelPadFocus") == chosen && ReferenceEquals(idle, session.ActivePrompt),
                    "Closing the card detail changed the selected card or advanced the duel.");

                var chainButton = field.Root.transform.Find("ChainConfirmation/Button").GetComponent<Button>();
                var chainLabel = chainButton.GetComponentInChildren<Text>();
                Require(chainLabel.text == "AUTO", "Chain confirmation did not default to AUTO.");
                Pad(field, RightShoulder);
                Require(chainLabel.text == "OFF", "Controller RB did not switch chain confirmation to OFF.");
                ExecuteEvents.Execute(chainButton.gameObject,
                    new PointerEventData(events) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
                Require(chainLabel.text == "ON", "The mouse did not switch chain confirmation to ON.");
                Pad(field, RightShoulder);
                Require(chainLabel.text == "AUTO" && ReferenceEquals(idle, session.ActivePrompt),
                    "Cycling chain confirmation changed a pending Main Phase command.");

                var fake = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_EFFECTYN, playerId = (byte)session.HumanPlayerIndex,
                    promptCon = (byte)session.HumanPlayerIndex, promptLoc = YgoConstants.LOCATION_HAND, promptCardCode = Sparkman };
                modals.ShowPrompt(session, fake);
                Pad(field, RightShoulder);
                Require(chainLabel.text == "OFF" && modals.IsPromptOpen && ReferenceEquals(idle, session.ActivePrompt),
                    "Controller RB could not switch chain confirmation during an effect prompt.");
                modals.ModalRoot.transform.Find("FieldView/B").GetComponent<Button>().onClick.Invoke();
                Pad(field, RightShoulder);
                Pad(field, A);
                Require(modals.IsZoneViewerOpen && ReferenceEquals(idle, session.ActivePrompt), "The graveyard was not browsable.");
                Pad(field, B);
                Require(!modals.IsZoneViewerOpen, "The controller did not close the graveyard.");
                Pad(field, LeftShoulder);
                Pad(field, A);
                Require(modals.IsZoneViewerOpen && ReferenceEquals(idle, session.ActivePrompt), "Field view could not open the Extra Deck with the controller.");
                Pad(field, B);
                Pad(field, B);
                Require(modals.IsPromptOpen && !modals.IsZoneViewerOpen && ReferenceEquals(idle, session.ActivePrompt),
                    "Closing the pile and field view did not return to the pending effect.");
                modals.HideAll();

                Pad(field, LeftShoulder);
                Pad(field, A);
                Require(Get<GameObject>(field, "_actionPopup").activeSelf && ReferenceEquals(idle, session.ActivePrompt),
                    "Opening command choices should not submit an action.");
                Pad(field, A);
                Require(session.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_PLACE,
                    "Confirming the first hand command did not reach legal placement.");
                Pad(field, direction: Vector2Int.right);
                var placement = Get<RectTransform>(field, "_duelPadFocus");
                Require(placement.IsChildOf(field.Root.transform.Find("DuelBoard")), "A placement prompt did not focus a field zone.");
                Pad(field, A);
                Require(mine.mzone.Count(card => card != null) == 1 && session.RetryCount == 0,
                    "Controller placement failed to complete the summon.");

                Advance(session, prompt => prompt.playerId == session.HumanPlayerIndex && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD);
                field.Refresh();
                if (!session.ActivePrompt.canBp)
                {
                    Pad(field, Y);
                    Pad(field, A);
                    Advance(session, prompt => prompt.playerId == session.HumanPlayerIndex && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD);
                    field.Refresh();
                }
                Require(session.ActivePrompt.canBp, "Battle Phase was unavailable on the next turn.");
                Pad(field, Y);
                Pad(field, A);
                Advance(session, prompt => prompt.playerId == session.HumanPlayerIndex && prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD);
                field.Refresh();
                Require(Get<RectTransform>(field, "_duelPadFocus") == placement, "The monster selection did not survive closing the phase picker.");
                Pad(field, A);
                Require(Get<GameObject>(field, "_actionPopup").activeSelf, "The live battle command could not be opened.");
                Pad(field, A);
                Advance(session, prompt => prompt.playerId == session.HumanPlayerIndex && prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD);
                Require(enemyField.lp < 8000 && session.RetryCount == 0, "Confirming the attack caused no battle damage.");

                field.Refresh();
                var log = Get<Button>(field, "_duelPadLogButton");
                ExecuteEvents.Execute(log.gameObject, new PointerEventData(events) { button = PointerEventData.InputButton.Left },
                    ExecuteEvents.pointerClickHandler);
                Require(field.IsReadingLog, "The mouse log control stopped working.");
                Pad(field, B);
                Require(!field.IsReadingLog, "The controller did not close a mouse-opened log.");
                Pad(field, B);
                Require(field.IsPaused, "The controller did not open pause.");
                Pad(field, A);
                Require(!field.IsPaused, "The controller did not resume from pause.");
            }
            finally
            {
                field?.UnbindSession();
                if (localEvents != null)
                {
                    if (EventSystem.current == events) Invoke(events, "OnDisable");
                    UnityEngine.Object.DestroyImmediate(localEvents);
                }
                else previousEvents.SetSelectedGameObject(previousFocus);
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        private static void Advance(DuelSession session, Func<DuelPrompt, bool> stop)
        {
            var ai = new TacticalAI();
            for (int action = 0; action < 80 && !session.IsFinished && session.RetryCount == 0; action++)
            {
                var prompt = session.ActivePrompt;
                if (prompt == null) session.StepUntilPrompt();
                else if (stop(prompt)) return;
                else if (prompt.playerId == session.AiPlayerIndex && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && prompt.canEp)
                    session.SubmitResponseInt(7);
                else session.SubmitResponse(ai.ComputeResponse(session, prompt));
            }
            throw new InvalidOperationException("The native duel could not reach the requested phase without retries.");
        }

        private static void Pad(DuelFieldUI field, int buttons = 0, Vector2Int direction = default)
        {
            var packet = Activator.CreateInstance(Frame, Private, null,
                new[] { (object)direction, Enum.ToObject(Buttons, buttons), 0f }, null);
            Invoke(field, "HandleDuelPad", packet);
        }

        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
        private static void Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private).Invoke(owner, args);
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[DUEL PAD] " + message);
        }
    }
}
