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
        private const uint MysticalElf = 15025844;
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
            Debug.Log("[VERIFY DUEL PAD] Both seats passed hand focus/detail/reflow, summons, pile viewing, phase changes, LP updates, direct attacks and immediate field targeting by mouse, touch or controller.");
        }

        private static void VerifyDuel(bool playerFirst)
        {
            var main = Enumerable.Repeat(Sparkman, 20).Concat(Enumerable.Repeat(BurstLady, 20)).ToArray();
            var enemy = Enumerable.Repeat(playerFirst ? 89631139u : MysticalElf, 40).ToArray();
            int startingLp = playerFirst ? 8000 : 12000;
            using var session = new DuelSession();
            if (!session.StartNewDuel(main, new[] { 69248256u }, enemy, Array.Empty<uint>(), playerFirst, startingLp,
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
                if (!playerFirst)
                {
                    Advance(session, prompt => prompt.playerId == session.AiPlayerIndex &&
                        prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && prompt.summonable.Count > 0);
                    session.SubmitResponseInt(0);
                }
                Advance(session, prompt => prompt.playerId == session.HumanPlayerIndex && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD);
                field.BindSession(session, null, null);
                Canvas.ForceUpdateCanvases();
                var mine = session.Players[session.HumanPlayerIndex];
                var enemyField = session.Players[session.AiPlayerIndex];
                var idle = session.ActivePrompt;
                var myLp = Get<Text>(field, "_myLpText");
                var opponentLp = Get<Text>(field, "_oppLpText");
                Require(myLp.text == startingLp.ToString() && opponentLp.text == startingLp.ToString(),
                    "Binding a duel did not show its actual starting LP.");
                Require(Get<uint>(field, "_inspectedCode") == 0 &&
                    !Get<CompositeCardView>(field, "_inspCard").gameObject.activeSelf,
                    "The inspector started with art for a card the player had not selected.");
                Require(Get<Text>(field, "_turnBadgeText").text.StartsWith("내 턴") &&
                    Get<Text>(field, "_phaseControlHint").enabled,
                    "The current player's turn and the phase-change control were not visible.");
                foreach (string zone in new[] { "_myGyTile", "_oppGyTile", "_myBanTile", "_oppBanTile" })
                {
                    var tile = Get<object>(field, zone);
                    string label = ((Text)tile.GetType().GetField("label").GetValue(tile)).text;
                    string count = ((Text)tile.GetType().GetField("count").GetValue(tile)).text;
                    string expected = zone.EndsWith("GyTile") ? "묘지" : "제외";
                    Require(label == expected && int.TryParse(count, out _),
                        "A graveyard or banished pile was missing its location or count.");
                }

                var mouseCard = field.Root.transform.Find("PlayerHand").GetChild(0) as RectTransform;
                var mouseArt = (RectTransform)mouseCard.GetComponentInChildren<CompositeCardView>().transform;
                var pointer = new PointerEventData(events);
                Vector2 hitPosition = mouseCard.anchoredPosition;
                ExecuteEvents.Execute(mouseCard.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                var handActions = (RectTransform)field.Root.transform.Find("CardActions");
                Require(handActions.gameObject.activeSelf && handActions.Find("SUMMON/Button/Plate/Icon").GetComponent<Image>().sprite != null &&
                    handActions.Find("SETMONST/Button/Plate/Icon").GetComponent<Image>().sprite != null &&
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
                typeof(DuelFieldUI).GetField("_pointerActionLeftAt", Private).SetValue(field, float.NegativeInfinity);
                Invoke(field, "RefreshCardActions");
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
                Pad(field, LeftShoulder);
                var cardFocus = events.currentSelectedGameObject;
                Require(Get<Text>(modals, "_zoneTitle").text.StartsWith("내 엑스트라 덱") &&
                    cardFocus.transform.IsChildOf(Get<RectTransform>(modals, "_zoneGrid")),
                    "Controller LB did not switch the open graveyard to the Extra Deck and focus its card.");
                Pad(field, X);
                var detail = Get<GameObject>(field, "_cardZoomModal");
                Require(detail.activeSelf && modals.IsZoneViewerOpen && events.currentSelectedGameObject.transform.IsChildOf(detail.transform),
                    "Controller X did not put card details above the open pile browser.");
                Pad(field, B);
                Require(!detail.activeSelf && modals.IsZoneViewerOpen && events.currentSelectedGameObject == cardFocus &&
                    ReferenceEquals(idle, session.ActivePrompt), "Controller B did not restore the card's focus before leaving the pile.");
                Pad(field, B);
                Require(!modals.IsZoneViewerOpen, "The controller did not close the pile browser.");
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
                void ChooseSummon()
                {
                    if (playerFirst) Pad(field, A);
                    else ExecuteEvents.Execute(summonIcon,
                        new PointerEventData(events) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
                }

                ChooseSummon();
                var banner = field.Root.transform.Find("PlacementHint");
                var cancelPlacement = field.Root.transform.Find("PlacementCancel/Button").GetComponent<Button>();
                Require(ReferenceEquals(session.ActivePrompt, idle) && banner.gameObject.activeInHierarchy &&
                    banner.GetComponentInChildren<Text>().text.Contains(CardDatabase.GetName(mine.hand[0].code)) &&
                    cancelPlacement.gameObject.activeInHierarchy && !handActions.gameObject.activeSelf,
                    "The position prompt did not name the card or allow cancellation before committing the summon.");
                if (playerFirst) Pad(field, B);
                else ExecuteEvents.Execute(cancelPlacement.gameObject,
                    new PointerEventData(events) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
                Require(ReferenceEquals(session.ActivePrompt, idle) && mine.mzone.All(card => card == null) &&
                    !banner.gameObject.activeSelf && !cancelPlacement.gameObject.activeInHierarchy && session.RetryCount == 0,
                    "Canceling placement consumed the summon or left its controls visible.");

                Pad(field, A);
                summonIcon = handActions.Find("SUMMON/Button").gameObject;
                ChooseSummon();
                Pad(field, direction: Vector2Int.right);
                var placement = Get<RectTransform>(field, "_duelPadFocus");
                Require(placement.IsChildOf(field.Root.transform.Find("DuelBoard")) &&
                    placement.GetComponent<Image>().color.g > .8f &&
                    placement.GetComponent<Image>().color.b < .4f,
                    "A placement prompt did not focus and highlight a legal field zone.");
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
                var fieldCard = (RectTransform)placement.Find("Card");
                var fieldStats = (RectTransform)placement.Find("CardReadout");
                var fieldArt = (RectTransform)fieldCard.Find("CardView/AspectBox");
                Invoke(field, "AdvanceDuelPadCardMotions", .08f);
                Require(fieldCard.anchoredPosition.y > 0f && fieldStats.anchoredPosition == fieldCard.anchoredPosition &&
                    fieldStats.localScale == fieldArt.localScale,
                    "Controller focus did not lift the field card and its stats together.");
                var attackIcon = handActions.Find("ATTACK/Button");
                Require(handActions.gameObject.activeSelf && attackIcon != null &&
                    !Get<GameObject>(field, "_actionPopup").activeSelf,
                    "Focusing a field monster in the Battle Phase did not show the attack icon.");
                Pad(field, A);
                Invoke(field, "AdvanceDuelPadCardMotions", .08f);
                Require(events.currentSelectedGameObject == attackIcon.gameObject &&
                    session.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD &&
                    fieldCard.anchoredPosition.y > 0f && fieldStats.anchoredPosition == fieldCard.anchoredPosition,
                    "Moving from the monster to its attack icon submitted the command.");
                Pad(field, A);
                if (!playerFirst)
                {
                    var target = session.ActivePrompt;
                    Require(target?.IsAttackTargetSelection == true && target.candidates.Count == 1 &&
                        target.candidates[0].code == MysticalElf, "The native battle did not open a field attack target.");
                    TestDuelZoneBrowser.VerifyAttackTargetClicks(session, target);
                    modals.ShowPrompt(session, target);
                    field.Refresh();
                    Pad(field, A);
                    Require(!modals.IsPromptOpen && !ReferenceEquals(session.ActivePrompt, target),
                        "The controller attack target still required confirmation.");
                }
                Advance(session, prompt => prompt.playerId == session.HumanPlayerIndex && prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD);
                Require(enemyField.lp < startingLp && session.RetryCount == 0,
                    $"The selected attack caused no battle damage in seat {session.HumanPlayerIndex} (LP {enemyField.lp}, retries {session.RetryCount}).");
                // Advance the same display animation used by the game, before the field refresh can resync the LP.
                Invoke(field, "AdvanceLifeReadouts", .02f);
                Require(int.TryParse(opponentLp.text, out int between) && enemyField.lp < between && between < startingLp,
                    "Battle damage did not animate the opponent LP toward the native total.");
                Invoke(field, "AdvanceLifeReadouts", 1f);
                var opponentFill = Get<RectTransform>(field, "_oppLpFill");
                Require(opponentLp.text == enemyField.lp.ToString() && myLp.text == mine.lp.ToString() &&
                    Mathf.Approximately(opponentFill.anchorMax.x, Mathf.Clamp01(enemyField.lp / 8000f)),
                    "Battle damage settled on the wrong LP value or track length.");

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
            var complete = (Action)Invoke(field, "ReserveCardArrival", controller, YgoConstants.LOCATION_HAND, 1u, (Action)field.Refresh);
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

        internal static void Pad(DuelFieldUI field, int buttons = 0, Vector2Int direction = default)
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
