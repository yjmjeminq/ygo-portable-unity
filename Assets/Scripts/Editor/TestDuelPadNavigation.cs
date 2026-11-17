using System;
using System.Collections;
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
            Debug.Log("[VERIFY DUEL PAD] Both seats passed hand focus/detail/reflow, summon and placement, piles during a chain, phase changes, attack, mouse log and pad pause.");
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

                var mouseCard = field.Root.transform.Find("PlayerHand").GetChild(0) as RectTransform;
                var mouseArt = (RectTransform)mouseCard.GetComponentInChildren<CompositeCardView>().transform;
                var pointer = new PointerEventData(events);
                Vector2 hitPosition = mouseCard.anchoredPosition;
                ExecuteEvents.Execute(mouseCard.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                var handActions = (RectTransform)field.Root.transform.Find("HandActions");
                Require(handActions.gameObject.activeSelf && handActions.Find("SUMMON/Button/Icon").GetComponent<Image>().sprite != null &&
                    handActions.Find("SETMONST/Button/Icon").GetComponent<Image>().sprite != null &&
                    handActions.TransformPoint(Vector3.zero).y > mouseCard.TransformPoint(Vector3.up * mouseCard.rect.yMax).y &&
                    ReferenceEquals(idle, session.ActivePrompt), "Hovering a monster did not show its legal hand actions above the card.");
                Invoke(field, "AdvanceDuelPadCardMotions", .06f);
                Require(mouseCard.anchoredPosition == hitPosition && mouseCard.localScale == Vector3.one &&
                    mouseArt.anchoredPosition.y > 0f,
                    "Pointer hover moved the card hitbox or did not lift the artwork.");
                uint originalHoveredCode = mine.hand[0].code;
                uint reboundCode = originalHoveredCode == Sparkman ? BurstLady : Sparkman;
                mine.hand[0].code = reboundCode;
                field.Refresh();
                Require(Get<uint>(field, "_inspectedCode") == reboundCode &&
                    Get<uint>(field, "_duelPointerCode") == reboundCode,
                    "Rebinding the card under a stationary mouse left the old card in the preview.");
                mine.hand[0].code = originalHoveredCode;
                field.Refresh();
                ExecuteEvents.Execute(mouseCard.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                typeof(DuelFieldUI).GetField("_pointerHandActionLeftAt", Private).SetValue(field, float.NegativeInfinity);
                Invoke(field, "RefreshHandActions");
                Invoke(field, "AdvanceDuelPadCardMotions", .5f);
                Require(mouseArt.anchoredPosition == Vector2.zero && mouseArt.localScale == Vector3.one,
                    "Leaving a hovered card did not settle the artwork.");

                Pad(field, LeftShoulder);
                var initial = Get<RectTransform>(field, "_duelPadFocus");
                var initialArt = (RectTransform)initial.GetComponentInChildren<CompositeCardView>().transform;
                Invoke(field, "AdvanceDuelPadCardMotions", .12f);
                Pad(field, direction: Vector2Int.right);
                var chosen = Get<RectTransform>(field, "_duelPadFocus");
                Require(chosen != initial && chosen.IsChildOf(field.Root.transform.Find("PlayerHand")),
                    "Moving right from the first card did not reach another card in the hand.");
                var chosenArt = (RectTransform)chosen.GetComponentInChildren<CompositeCardView>().transform;
                Invoke(field, "AdvanceDuelPadCardMotions", .06f);
                Require(chosenArt.anchoredPosition.y > 0f && chosenArt.localScale.x > 1f,
                    "The focused hand card did not rise and grow during the transition.");
                Invoke(field, "AdvanceDuelPadCardMotions", .5f);
                Require(initialArt.anchoredPosition == Vector2.zero && initialArt.localScale == Vector3.one,
                    "The previously focused hand card did not return to its original position and size.");
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
                var commandCard = Get<RectTransform>(field, "_duelPadFocus");
                var commandCardArt = (RectTransform)commandCard.GetComponentInChildren<CompositeCardView>().transform;
                Require(handActions.gameObject.activeSelf && !Get<GameObject>(field, "_actionPopup").activeSelf,
                    "Focusing a hand card with the controller did not show the action icons.");
                var summonIcon = handActions.Find("SUMMON/Button").gameObject;
                var setIcon = handActions.Find("SETMONST/Button").gameObject;
                Pad(field, A);
                Require(events.currentSelectedGameObject == summonIcon && ReferenceEquals(idle, session.ActivePrompt),
                    "Opening hand action choices submitted a command or selected the wrong icon.");
                Pad(field, direction: Vector2Int.right);
                Require(events.currentSelectedGameObject == setIcon, "The controller did not move to the Set icon.");
                Pad(field, B);
                Require(events.currentSelectedGameObject == commandCard.gameObject && ReferenceEquals(idle, session.ActivePrompt),
                    "Cancel did not return from the icon row to its card.");
                Pad(field, A);
                Invoke(field, "AdvanceDuelPadCardMotions", .12f);
                Require(commandCardArt.anchoredPosition.y > 0f && events.currentSelectedGameObject == summonIcon,
                    "Focusing a hand action did not keep the card raised.");
                if (playerFirst) Pad(field, A);
                else ExecuteEvents.Execute(summonIcon,
                    new PointerEventData(events) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
                Require(session.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_PLACE && !handActions.gameObject.activeSelf,
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

                VerifyHandArrivalFollowsCard(field, mine, (byte)session.HumanPlayerIndex);
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

        private static void VerifyHandArrivalFollowsCard(DuelFieldUI field, PlayerFieldSnapshot player, byte controller)
        {
            var views = Get<IList>(field, "_myHandViews");
            RectTransform HandRect(int index)
            {
                var view = views[index];
                return (RectTransform)view.GetType().GetField("Rect").GetValue(view);
            }

            var arriving = HandRect(1);
            var neighbor = HandRect(2);
            var complete = (Action)Invoke(field, "HideUntilArrival", controller, YgoConstants.LOCATION_HAND, 1u);
            player.hand.RemoveAt(0);
            Invoke(field, "RemoveHandView", controller, 0u);
            field.Refresh();
            Require(HandRect(0) == arriving && !arriving.gameObject.activeSelf && neighbor.gameObject.activeSelf,
                "When an earlier hand card left, the incoming card's animation hid the wrong card.");
            complete();
            Require(arriving.gameObject.activeSelf && neighbor.gameObject.activeSelf,
                "Completing a shifted hand arrival did not reveal the correct card.");
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
        private static object Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private).Invoke(owner, args);
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[DUEL PAD] " + message);
        }
    }
}
