using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.AI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static class TestHandGlow
    {
        private const uint KaiserSeaHorse = 17444133;
        private const uint BlueEyes = 89631139;
        private const uint PotOfDesires = 35261759;
        private const uint AshBlossom = 14558127;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string GlowPath = "CardView/AspectBox/PlayableGlow";

        internal static void Run()
        {
            VerifyMainPhaseAndStaleState();
            VerifyOpponentTurnActivation();
            VerifyNativeCombatReadout();
            VerifyInspectedCardMoves(swapInPlace: true);
            VerifyInspectedCardMoves(swapInPlace: false);
            VerifyNativeSummonReadout(turnFaceDown: false);
            VerifyNativeSummonReadout(turnFaceDown: true);
            VerifyNativeLinkedZone();
            VerifyNativeMovingReadout();
            Debug.Log("[VERIFY HAND GLOW] Native summons and effects, distinct hand copies, blocked prompts, animation, off-turn activation and field-only monster ratings passed.");
        }

        private static void VerifyMainPhaseAndStaleState()
        {
            uint[] main = Enumerable.Repeat(KaiserSeaHorse, 16).Concat(Enumerable.Repeat(BlueEyes, 12))
                .Concat(Enumerable.Repeat(PotOfDesires, 12)).ToArray();
            uint[] opponent = Enumerable.Repeat(BlueEyes, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Hand glow main phase", main, Array.Empty<uint>(),
                opponent, Array.Empty<uint>(), true, s =>
                {
                    var hand = s.Players[s.HumanPlayerIndex].hand;
                    return s.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
                        hand.Count(card => card.code == KaiserSeaHorse) >= 2 &&
                        hand.Any(card => card.code == BlueEyes) && hand.Any(card => card.code == PotOfDesires);
                });
            using var ui = new HandUI(duel);
            byte player = (byte)duel.HumanPlayerIndex;
            var cards = duel.Players[player].hand;
            var duplicateSlots = cards.Where(card => card.code == KaiserSeaHorse).Select(card => card.sequence).ToArray();
            var original = duel.ActivePrompt;
            Require(original.summonable.Any(card => card.code == KaiserSeaHorse) &&
                original.activatable.Any(card => card.code == PotOfDesires),
                "The native fixture did not offer both a summon and an activation.");
            ui.Expect(cards.Where(card => card.code != BlueEyes).Select(card => card.sequence));
            var handRow = ui.Field.Root.transform.Find("PlayerHand");
            ui.VerifyFieldReadouts(duel);
            var actionBar = ui.Field.Root.transform.Find("CardActions");
            handRow.GetChild((int)cards.First(card => card.code == PotOfDesires).sequence)
                .GetComponent<CardDragTrigger>().OnPointerEnterAction.Invoke();
            Require(actionBar.gameObject.activeSelf && actionBar.Find("ACTION/Circle/Button/Icon") != null &&
                actionBar.Find("SET/Circle/Button/Icon") != null, "A playable spell did not show activation and set icons.");
            handRow.GetChild((int)cards.First(card => card.code == BlueEyes).sequence)
                .GetComponent<CardDragTrigger>().OnPointerEnterAction.Invoke();
            Require(!actionBar.gameObject.activeSelf, "The action row showed commands for an unplayable card.");

            // Inject a command snapshot to distinguish two physical copies with the same code.
            uint availableCopy = duplicateSlots[1];
            var command = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, playerId = player };
            command.activatable.Add(HandChoice(player, availableCopy, KaiserSeaHorse));
            command.activatable.Add(HandChoice((byte)duel.AiPlayerIndex, duplicateSlots[0], KaiserSeaHorse));
            command.activatable.Add(new SelectableCardItem
            {
                controller = player, location = YgoConstants.LOCATION_SZONE, sequence = duplicateSlots[0],
            });
            SetPrompt(duel, command);
            ui.Field.Refresh();
            ui.Expect(new[] { availableCopy });

            var mandatory = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, playerId = player, min = 1, max = 1 };
            mandatory.candidates.Add(HandChoice(player, availableCopy, KaiserSeaHorse));
            SetPrompt(duel, mandatory);
            ui.Field.Refresh();
            ui.Expect(Array.Empty<uint>());

            var confirmEffect = new DuelPrompt
            {
                messageType = YgoConstants.MSG_SELECT_EFFECTYN,
                playerId = player,
                promptCon = player,
                promptLoc = YgoConstants.LOCATION_HAND,
                promptSeq = duplicateSlots[0],
                promptCardCode = KaiserSeaHorse,
            };
            SetPrompt(duel, confirmEffect);
            ui.Field.Refresh();
            ui.Expect(new[] { duplicateSlots[0] });

            SetPrompt(duel, command);
            ui.Field.Refresh();
            ui.Modals.ShowCodeListViewer("Hand glow verification", Array.Empty<uint>());
            ui.Field.Update();
            ui.Expect(Array.Empty<uint>());
            ui.Modals.CloseZoneViewer();
            ui.Field.Update();
            ui.Expect(new[] { availableCopy });

            var animator = CardAnimationManager.Ensure(ui.Canvas);
            typeof(CardAnimationManager).GetMethod("QueueDuelStart", PrivateInstance).Invoke(animator, new object[] { Vector2.zero });
            ui.Field.Update();
            ui.Expect(Array.Empty<uint>());
            animator.Clear();
            ui.Field.Refresh();
            ui.Expect(new[] { availableCopy });

            var completeArrival = (Action)typeof(DuelFieldUI).GetMethod("HideUntilArrival", PrivateInstance)
                .Invoke(ui.Field, new object[] { player, YgoConstants.LOCATION_HAND, availableCopy });
            ui.Field.Refresh();
            ui.Expect(Array.Empty<uint>());
            completeArrival();
            ui.Expect(new[] { availableCopy });

            SetPrompt(duel, original);
        }

        private static void VerifyOpponentTurnActivation()
        {
            uint[] main = Enumerable.Repeat(AshBlossom, 20).Concat(Enumerable.Repeat(BlueEyes, 20)).ToArray();
            uint[] opponent = Enumerable.Repeat(PotOfDesires, 20).Concat(Enumerable.Repeat(BlueEyes, 20)).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Hand glow opponent turn", main, Array.Empty<uint>(),
                opponent, Array.Empty<uint>(), false, s =>
                    s.Players[s.HumanPlayerIndex].hand.Any(card => card.code == AshBlossom) &&
                    s.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
                    s.ActivePrompt.playerId == s.AiPlayerIndex &&
                    s.ActivePrompt.activatable.Any(card => card.code == PotOfDesires));
            using var ui = new HandUI(duel);
            ui.Expect(Array.Empty<uint>());

            int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == PotOfDesires);
            duel.SubmitResponseInt((activate << 16) | 5);
            var ai = new TacticalAI();
            for (int i = 0; i < 30 && !duel.IsFinished; i++)
            {
                var prompt = duel.ActivePrompt;
                if (IsAshBlossomPrompt(duel, prompt))
                    break;
                if (prompt == null)
                    duel.StepUntilPrompt();
                else
                    duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
            }
            var activation = duel.ActivePrompt;
            Require(IsAshBlossomPrompt(duel, activation) && duel.TurnPlayer == duel.AiPlayerIndex && duel.RetryCount == 0,
                "No human hand effect was offered on the opponent's turn.");
            uint[] expected = activation.messageType == YgoConstants.MSG_SELECT_EFFECTYN
                ? new[] { activation.promptSeq }
                : activation.candidates.Where(card => card.controller == duel.HumanPlayerIndex &&
                    card.location == YgoConstants.LOCATION_HAND).Select(card => card.sequence).Distinct().ToArray();
            ui.Field.Refresh();
            ui.Expect(expected);
            duel.SubmitResponseInt(activation.messageType == YgoConstants.MSG_SELECT_EFFECTYN ? 0 : -1);
            ui.Field.Refresh();
            ui.Expect(Array.Empty<uint>());
            Require(duel.RetryCount == 0, "Passing the native hand effect caused a retry.");
        }

        private static void VerifyNativeCombatReadout()
        {
            const uint axeOfDespair = 40619825;
            const uint mysticalElf = 15025844;
            uint[] main = Enumerable.Repeat(KaiserSeaHorse, 28).Concat(Enumerable.Repeat(axeOfDespair, 12)).ToArray();
            uint[] opponent = Enumerable.Repeat(mysticalElf, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Live combat readout", main, Array.Empty<uint>(),
                opponent, Array.Empty<uint>(), true, session => session.ActivePrompt?.summonable.Count > 0 &&
                    session.Players[session.HumanPlayerIndex].hand.Any(card => card.code == axeOfDespair));
            using var ui = new HandUI(duel);
            var ai = new TacticalAI();
            var own = duel.Players[duel.HumanPlayerIndex];

            void ReachMainPhase()
            {
                for (int step = 0; step < 16 && duel.ActivePrompt?.messageType != YgoConstants.MSG_SELECT_IDLECMD; step++)
                {
                    var prompt = duel.ActivePrompt;
                    if (prompt == null) duel.StepUntilPrompt();
                    else if (prompt.messageType == YgoConstants.MSG_SELECT_CARD)
                        duel.SubmitResponse(DuelSession.PackCardSelection(prompt.candidates.FindIndex(card => card.code == KaiserSeaHorse)));
                    else duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
                }
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    "The combat readout fixture could not resolve the native action.");
            }

            duel.SubmitResponseInt(0);
            ReachMainPhase();
            var live = own.mzone.Single(card => card?.code == KaiserSeaHorse);
            ui.Field.Refresh();
            var slot = ui.Field.Root.transform.Find($"DuelBoard/Zone_{live.sequence + 2}_3");
            var attack = slot.Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>();
            Require(attack.text == "1700", "The field did not show the native attack before the equip.");
            slot.GetComponent<CardDragTrigger>().OnRightClick();
            int equip = duel.ActivePrompt.activatable.FindIndex(card => card.code == axeOfDespair);
            Require(equip >= 0, "The native engine did not offer Axe of Despair.");
            duel.SubmitResponseInt((equip << 16) | 5);
            ReachMainPhase();
            ui.Field.Refresh();
            var inspector = ui.Field.Root.transform.Find("LeftInspector/CardDetails/CombatStats/Attack/Digits/Value").GetComponent<Text>();
            Require(own.mzone[live.sequence].attack == 2700 && attack.text == "2700" && inspector.text == "2700" &&
                slot.Find("CardReadout/Combat/Attack/Change/Arrow").GetComponent<Text>().text == "▲",
                "The field and inspector did not reflect the native equip bonus.");
            Debug.Log("[VERIFY DUEL EQUIPPED STATS] Axe of Despair updated the native field and inspected card from 1700 to 2700.");

            var attackCell = slot.Find("CardReadout/Combat/Attack");
            var pulse = attackCell.GetComponent<DuelReadoutPulse>();
            var sweep = (RectTransform)attackCell.Find("ChangePulse/Sweep");
            Require(pulse != null && pulse.enabled && sweep.gameObject.activeInHierarchy &&
                inspector.transform.parent.parent.Find("ChangePulse").gameObject.activeInHierarchy &&
                slot.Find("CardReadout/Combat/Defense/ChangePulse") == null,
                "The native equip bonus did not animate just the changed attack values.");
            float start = sweep.anchorMax.x;
            pulse.Advance(.20f);
            float moved = sweep.anchorMax.x;
            ui.Field.Refresh();
            Require(moved > start && sweep.anchorMax.x == moved, "An unchanged field refresh restarted the stat animation.");
            pulse.Advance(.30f);
            Require(!sweep.gameObject.activeInHierarchy && attack.text == "2700",
                "The attack pulse did not return to its unchanged, correct number.");
            Debug.Log("[VERIFY DUEL STAT MOTION] Native attack increase animated once and kept the current value throughout.");

            duel.SubmitResponseInt(7);
            ReachMainPhase();
            Require(duel.ActivePrompt.playerId == duel.AiPlayerIndex && duel.ActivePrompt.mSetable.Count > 0,
                "The native battle fixture could not set the defending monster.");
            duel.SubmitResponseInt(3);
            ReachMainPhase();
            Require(duel.Players[duel.AiPlayerIndex].mzone.Any(card => card?.position == YgoConstants.POS_FACEDOWN_DEFENSE),
                "The battle fixture did not start with a face-down defender.");
            duel.SubmitResponseInt(7);
            ReachMainPhase();
            ui.Field.Refresh();

            var animation = CardAnimationManager.Ensure(ui.Canvas);
            duel.SubmitResponseInt(6);
            Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD && duel.ActivePrompt.attackable.Count == 1,
                "The equipped monster could not enter battle.");
            duel.SubmitResponseInt(1);
            Require(duel.ActivePrompt?.IsAttackTargetSelection == true && duel.ActivePrompt.candidates.Count == 1,
                "The battle fixture did not select the face-down defender.");
            duel.SubmitResponse(DuelSession.PackCardSelection(0));
            var readout = animation.transform.Find("BattleReadout");
            Require(readout != null && duel.RetryCount == 0 &&
                readout.Find("Attacker/Values/Attack/Digits/Value").GetComponent<Text>().text == "2700" &&
                readout.Find("Defender/Values/Defense/Digits/Value").GetComponent<Text>().text == "2000",
                "The attack presentation lost the native equipped attack or the revealed defense.");
            Debug.Log("[VERIFY DUEL BATTLE STATS] Native battle displayed the equipped 2700 ATK and the revealed 2000 DEF.");
        }

        private static void VerifyInspectedCardMoves(bool swapInPlace)
        {
            const uint axe = 40619825, creatureSwap = 31036355, bookOfMoon = 14087893;
            uint[] main = Enumerable.Repeat(KaiserSeaHorse, 22).Concat(Enumerable.Repeat(axe, 6))
                .Concat(Enumerable.Repeat(creatureSwap, 6)).Concat(Enumerable.Repeat(bookOfMoon, 6)).ToArray();
            uint[] enemy = Enumerable.Repeat(KaiserSeaHorse, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Inspected card moves", main, Array.Empty<uint>(),
                enemy, Array.Empty<uint>(), true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return new[] { KaiserSeaHorse, axe, creatureSwap, bookOfMoon }.All(code => hand.Any(card => card.code == code));
                });
            var ai = new TacticalAI();

            void ReachIdle(Func<DuelPrompt, byte[]> choose = null)
            {
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => choose?.Invoke(prompt) ??
                    (prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.candidates.Count == 1
                        ? DuelSession.PackCardSelection(0) : null));
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    $"The native transfer fixture stopped at prompt {duel.ActivePrompt?.messageType}, with {duel.RetryCount} retries.");
            }

            void Activate(uint code)
            {
                int index = duel.ActivePrompt.activatable.FindIndex(card => card.code == code);
                Require(index >= 0, $"The transfer fixture could not activate {CardDatabase.GetName(code)}.");
                duel.SubmitResponseInt((index << 16) | 5);
            }

            var mine = duel.Players[duel.HumanPlayerIndex];
            var theirs = duel.Players[duel.AiPlayerIndex];
            duel.SubmitResponseInt(0);
            ReachIdle();
            Activate(axe);
            ReachIdle();
            duel.SubmitResponseInt(7);
            ReachIdle();
            duel.SubmitResponseInt(0);
            ReachIdle();
            duel.SubmitResponseInt(7);
            ReachIdle();
            uint origin = mine.mzone.Single(card => card != null).sequence;
            uint other = theirs.mzone.Single(card => card != null).sequence;
            uint destination = swapInPlace ? other : (other + 1) % 5;

            using var ui = new HandUI(duel);
            const string statPath = "Attack/Digits/Value";
            var inspector = ui.Field.Root.transform.Find($"LeftInspector/CardDetails/CombatStats/{statPath}").GetComponent<Text>();
            var zoom = ui.Field.Root.transform.Find($"Overlay_CardZoom/ZoomWin/Info/CombatStats/{statPath}").GetComponent<Text>();
            var source = ui.Field.Root.transform.Find($"DuelBoard/Zone_{origin + 2}_3");
            source.GetComponent<CardDragTrigger>().OnRightClick();
            var focus = typeof(DuelFieldUI).GetField("_duelPadFocus", PrivateInstance);
            typeof(DuelFieldUI).GetField("_usingDuelPad", PrivateInstance).SetValue(ui.Field, true);
            focus.SetValue(ui.Field, source);
            ui.Field.HandleDuelPad(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Alternate));
            Require(ui.Field.IsReadingCardDetails && inspector.text == "2700" && zoom.text == "2700",
                "The selected, equipped copy did not open in the controller's card-details view.");

            bool paired = false;
            duel.OnCardLocationsChanged += (_, partner) => paired |= partner != null;
            Activate(creatureSwap);
            ReachIdle(prompt =>
            {
                uint sequence = prompt.playerId == duel.HumanPlayerIndex ? origin : destination;
                if (prompt.messageType != YgoConstants.MSG_SELECT_PLACE || (prompt.placeFlag & (1u << (int)sequence)) != 0)
                    return null;
                return new[] { prompt.playerId, (byte)YgoConstants.LOCATION_MZONE, (byte)sequence };
            });
            ui.Field.Refresh();
            Require(paired == swapInPlace && mine.mzone[origin]?.attack == 1700 && theirs.mzone[destination]?.attack == 2700 &&
                inspector.text == "2700" && zoom.text == "2700",
                "The inspector or details switched to the identical card occupying the old zone.");
            ui.Field.HandleDuelPad(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Cancel));
            var expected = ui.Field.Root.transform.Find($"DuelBoard/Zone_{6 - destination}_1");
            Require(!ui.Field.IsReadingCardDetails && expected != null && (Transform)focus.GetValue(ui.Field) == expected &&
                inspector.text == "2700", "Controller focus did not return to the same card after the control change.");

            ui.Field.HandleDuelPad(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Alternate));
            Activate(bookOfMoon);
            ReachIdle(prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD
                ? DuelSession.PackCardSelection(prompt.candidates.FindIndex(card =>
                    card.controller == duel.AiPlayerIndex && card.sequence == destination)) : null);
            ui.Field.Refresh();
            Require(theirs.mzone[destination]?.position == YgoConstants.POS_FACEDOWN_DEFENSE &&
                inspector.text == "1700" && zoom.text == "1700" &&
                typeof(DuelFieldUI).GetField("_zoomFieldCard", PrivateInstance).GetValue(ui.Field) == null,
                "A face-down transfer kept the captured live stats in its details.");
            Debug.Log($"[VERIFY DUEL CARD FOLLOW] {(swapInPlace ? "Paired swap" : "Zone transfer")} kept the equipped copy's stats and controller focus, then released its hidden state.");
        }

        private static void VerifyNativeSummonReadout(bool turnFaceDown)
        {
            const uint ocean = 295517, instantFusion = 1845204, theseus = 96334243, bookOfMoon = 14087893;
            uint[] main = Enumerable.Repeat(KaiserSeaHorse, 22).Concat(Enumerable.Repeat(ocean, 6))
                .Concat(Enumerable.Repeat(instantFusion, 6)).Concat(Enumerable.Repeat(bookOfMoon, 6)).ToArray();
            uint[] enemy = Enumerable.Repeat(BlueEyes, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Native summon readout", main, new[] { theseus },
                enemy, Array.Empty<uint>(), true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return new[] { ocean, instantFusion, bookOfMoon }.All(code => hand.Any(card => card.code == code));
                });
            var ai = new TacticalAI();

            void Activate(uint code)
            {
                int index = duel.ActivePrompt.activatable.FindIndex(card => card.code == code);
                Require(index >= 0, $"The summon fixture could not activate {CardDatabase.GetName(code)}.");
                duel.SubmitResponseInt((index << 16) | 5);
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD &&
                    prompt.candidates.Count == 1 ? DuelSession.PackCardSelection(0) : null);
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    "The summon fixture could not resolve the preceding effect.");
            }

            Activate(ocean);
            using var ui = new HandUI(duel);
            var animation = CardAnimationManager.Ensure(ui.Canvas);
            Activate(instantFusion);
            var pendingField = typeof(DuelFieldUI).GetField("_pendingCardReadouts", PrivateInstance);
            var pending = (Dictionary<(byte, uint), CardMoveReadoutSnapshot>)pendingField.GetValue(ui.Field);
            Require(pending.Count == 1 && pending.Values.Single().Code == theseus,
                "The native Fusion Summon did not queue a readout for its own card.");
            var captured = pending.Single();

            if (turnFaceDown)
            {
                Activate(bookOfMoon);
                ui.Field.Refresh();
                Require(duel.Players[captured.Key.Item1].mzone[captured.Key.Item2].position == YgoConstants.POS_FACEDOWN_DEFENSE &&
                        pending.Count == 0 && captured.Value.Card == null,
                    "A summon preview read the live stats of a card that was already hidden when the field refreshed.");
                Debug.Log("[VERIFY DUEL SUMMON STATS] A pending reveal discarded a card's live stats when the card became hidden.");
                return;
            }

            ui.Field.Refresh();
            var stats = captured.Value.Card;
            Require(pending.Count == 0 && stats?.attack == 2400 && stats.defense == 2000 && stats.level == 4,
                "The summon readout missed a continuous effect applied before presentation.");
            var pose = new CardPose(Vector2.zero, new Vector2(160, 230));
            var flight = new CardFlight(theseus, pose, pose, true, false);
            var plate = animation.SummonPlate(flight, "융합 소환", Vector2.zero, stats);
            Require(plate.transform.Find("Caption/Value").GetComponent<Text>().text == "융합 소환 · 레벨 4" &&
                    plate.transform.Find("SummonStats/Attack/Digits/Value").GetComponent<Text>().text == "2400" &&
                    plate.transform.Find("SummonStats/Defense/Digits/Value").GetComponent<Text>().text == "2000",
                "The Fusion Summon banner and the queued live readout disagreed.");
            var proxy = CompositeCardView.Create(ui.Canvas.transform, "Summoned Card", detailedOverlay: true);
            proxy.SetCard(0, faceDown: true);
            CardAnimationManager.ApplySummonEntrance(proxy, flight, DuelEffectKind.Fusion, .25f, stats);
            Require(proxy.CurrentCode == 0 && proxy.transform.Find("AspectBox/CardBack").gameObject.activeSelf,
                "The moving card exposed its face before the reveal.");
            CardAnimationManager.ApplySummonEntrance(proxy, flight, DuelEffectKind.Fusion, .75f, stats);
            Require(proxy.transform.Find("AspectBox/AtkDef/Stat").GetComponent<Text>().text == "ATK/2400  DEF/2000" &&
                    proxy.transform.Find("AspectBox/LevelInfo/Lv").GetComponent<Text>().text == "★★★★",
                "The revealed card face did not keep the same numbers as the summon banner.");

            var (controller, sequence) = captured.Key;
            var location = (controller, YgoConstants.LOCATION_MZONE, sequence);
            typeof(DuelFieldUI).GetMethod("ShowArrival", PrivateInstance).Invoke(ui.Field, new object[] { location });
            var fieldSlot = typeof(DuelFieldUI).GetMethod("GetSlot", PrivateInstance).Invoke(ui.Field,
                new object[] { controller, YgoConstants.LOCATION_MZONE, sequence });
            var slot = (Transform)fieldSlot.GetType().GetField("root").GetValue(fieldSlot);
            Require(slot.Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>().text == "2400" &&
                    slot.Find("CardReadout/Combat/Defense/Digits/Value").GetComponent<Text>().text == "2000" &&
                    slot.Find("CardReadout/Labels/Rating/Value").GetComponent<Text>().text == "레벨 4",
                "The field did not show the same readout after the summon animation.");
            UnityEngine.Object.DestroyImmediate(plate.gameObject);
            Debug.Log("[VERIFY DUEL SUMMON STATS] Native Fusion Summon displayed 2400 ATK, 2000 DEF and Level 4 on the banner, card face and field.");
        }

        private static void VerifyNativeLinkedZone()
        {
            const uint linkSpider = 98978921, mysticalElf = 15025844;
            uint[] main = Enumerable.Repeat(mysticalElf, 40).ToArray();
            uint[] opponent = Enumerable.Repeat(BlueEyes, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Linked zone readout", main, new[] { linkSpider },
                opponent, Array.Empty<uint>(), true, session => session.ActivePrompt?.summonable.Count > 0);
            var ai = new TacticalAI();

            void ReachIdle()
            {
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD &&
                    prompt.min == 1 && prompt.candidates.Count != 0 ? DuelSession.PackCardSelection(0) : null);
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    $"The Link Spider fixture stopped at prompt {duel.ActivePrompt?.messageType}.");
            }

            duel.SubmitResponseInt(0);
            ReachIdle();
            int summon = duel.ActivePrompt.spSummonable.FindIndex(card => card.code == linkSpider);
            Require(summon >= 0, "The native engine did not offer the Link Spider summon.");
            duel.SubmitResponseInt((summon << 16) | 1);
            ReachIdle();
            var own = duel.Players[duel.HumanPlayerIndex];
            var live = own.mzone.Single(card => card?.code == linkSpider);
            Require(live.sequence is 5 or 6 && live.linkMarker == 0x002, "The native Link Spider did not expose its downward arrow.");
            uint target = live.sequence == 5 ? 1u : 3u;

            using var ui = new HandUI(duel);
            ui.Field.Root.transform.Find($"DuelBoard/Zone_{target + 2}_2").GetComponent<CardDragTrigger>().OnRightClick();
            ui.ExpectLinkedZones($"Zone_{target + 2}_3");
            int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == linkSpider);
            Require(activate >= 0, "Link Spider did not offer its linked-zone summon effect.");
            duel.SubmitResponseInt((activate << 16) | 5);
            ReachIdle();
            ui.Field.Refresh();
            Require(own.mzone[target]?.code == mysticalElf && own.mzone.Count(card => card?.code == mysticalElf) == 1,
                "The native effect summoned to a different zone than the selected Link arrow indicated.");
            ui.ExpectLinkedZones($"Zone_{target + 2}_3");
            Debug.Log("[VERIFY DUEL LINK ZONES] Link Spider's live downward marker identified the zone used by its native effect.");
        }

        private static void VerifyNativeMovingReadout()
        {
            const uint bookOfMoon = 14087893;
            uint[] main = Enumerable.Repeat(KaiserSeaHorse, 32).Concat(Enumerable.Repeat(bookOfMoon, 8)).ToArray();
            uint[] opponent = Enumerable.Repeat(BlueEyes, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Animated field readout", main, Array.Empty<uint>(),
                opponent, Array.Empty<uint>(), true, session => session.ActivePrompt?.summonable.Count > 0 &&
                    session.Players[session.HumanPlayerIndex].hand.Any(card => card.code == bookOfMoon));
            var ai = new TacticalAI();

            void ReachIdle()
            {
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD &&
                    prompt.candidates.Count == 1 ? DuelSession.PackCardSelection(0) : null);
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    "The native movement fixture did not return to the Main Phase.");
            }

            duel.SubmitResponseInt(0);
            ReachIdle();
            duel.SubmitResponseInt(7);
            ReachIdle();
            duel.SubmitResponseInt(7);
            ReachIdle();
            Require(duel.ActivePrompt.repositionable.Count == 1, "The native engine did not offer a position change.");
            var monster = duel.ActivePrompt.repositionable[0];
            using var ui = new HandUI(duel);
            var animation = CardAnimationManager.Ensure(ui.Canvas);
            duel.SubmitResponseInt(2);
            ReachIdle();
            var pending = (Dictionary<(byte, uint), CardMoveReadoutSnapshot>)typeof(DuelFieldUI)
                .GetField("_pendingCardReadouts", PrivateInstance).GetValue(ui.Field);
            var snapshot = pending[(monster.controller, monster.sequence)];
            ui.Field.Refresh();
            Require(snapshot.Card?.IsDefense == true && snapshot.Movement?.StartPosition == YgoConstants.POS_FACEUP_ATTACK,
                "A position change lost its starting and ending position.");

            var steps = (Queue<IEnumerator>)typeof(CardAnimationManager).GetField("_steps", PrivateInstance).GetValue(animation);
            var step = steps.Dequeue();
            Require(step.MoveNext() && step.Current is IEnumerator, "The field card did not enter its motion timeline.");
            Require(((IEnumerator)step.Current).MoveNext(), "The field card did not render its first moving frame.");
            var holder = (RectTransform)animation.transform.Find("MovingCardReadout");
            var combat = holder.Find("CardReadout/Combat");
            Require(combat.Find("Attack/Digits/Value").GetComponent<Text>().text == "1700" &&
                combat.Find("Defense/Digits/Value").GetComponent<Text>().text == "1650" &&
                combat.Find("Attack/Rail").GetComponent<Image>().color.a > combat.Find("Defense/Rail").GetComponent<Image>().color.a &&
                holder.Find("CardReadout/Labels/Rating/Value").GetComponent<Text>().text == "레벨 4",
                "The motion did not preserve the monster's stats and starting stance.");

            var readout = LotdUiKit.FillRect("Mid-motion Verification", animation.transform);
            var sample = new DuelMovingReadout(readout, snapshot, new DuelCardReadout(CardDatabase.Get(monster.code), snapshot.Card));
            var movingCard = (RectTransform)animation.transform.Find("MovingCard");
            movingCard.anchoredPosition += new Vector2(20f, 9f);
            movingCard.localRotation = Quaternion.Euler(0f, 0f, -58f);
            sample.Follow(movingCard, .66f);
            var rotated = readout.Find("CardReadout/Combat");
            Require(readout.anchoredPosition == movingCard.anchoredPosition && readout.localRotation == Quaternion.identity &&
                rotated.Find("Defense/Rail").GetComponent<Image>().color.a > rotated.Find("Attack/Rail").GetComponent<Image>().color.a,
                "A moving readout rotated its text or kept highlighting the old stance.");
            UnityEngine.Object.DestroyImmediate(readout.gameObject);

            Require(!step.MoveNext() && (holder == null || !holder.gameObject.activeSelf), "The travelling stats remained after landing.");
            UnityEngine.Object.DestroyImmediate(movingCard.gameObject);
            var slot = ui.Field.Root.transform.Find($"DuelBoard/Zone_{monster.sequence + 2}_3/CardReadout/Combat");
            Require(slot.gameObject.activeInHierarchy &&
                slot.Find("Defense/Rail").GetComponent<Image>().color.a > slot.Find("Attack/Rail").GetComponent<Image>().color.a,
                "The landing interrupted the defense readout.");

            int turnDown = duel.ActivePrompt.activatable.FindIndex(card => card.code == bookOfMoon);
            Require(turnDown >= 0, "The movement fixture could not activate its face-down effect.");
            duel.SubmitResponseInt((turnDown << 16) | 5);
            ReachIdle();
            Require(pending.Count == 0, "A face-down move queued live stats.");
            Debug.Log("[VERIFY DUEL MOVING STATS] Native Attack-to-Defense rotation kept upright ATK, DEF and Level during motion and hid face-down stats.");
        }

        private static bool IsAshBlossomPrompt(DuelSession duel, DuelPrompt prompt)
        {
            if (prompt == null || prompt.playerId != duel.HumanPlayerIndex) return false;
            return prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN
                ? prompt.promptCardCode == AshBlossom && prompt.promptLoc == YgoConstants.LOCATION_HAND
                : prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced &&
                  prompt.candidates.Any(card => card.code == AshBlossom && card.location == YgoConstants.LOCATION_HAND);
        }

        private static SelectableCardItem HandChoice(byte controller, uint sequence, uint code) =>
            new SelectableCardItem { controller = controller, sequence = sequence, code = code, location = YgoConstants.LOCATION_HAND };

        private static void SetPrompt(DuelSession duel, DuelPrompt prompt) =>
            typeof(DuelSession).GetProperty(nameof(DuelSession.ActivePrompt)).GetSetMethod(true).Invoke(duel, new object[] { prompt });

        private sealed class HandUI : IDisposable
        {
            internal Canvas Canvas { get; }
            internal DuelFieldUI Field { get; }
            internal DuelPromptModals Modals { get; }

            internal HandUI(DuelSession duel)
            {
                var root = new GameObject("Hand Glow Verification", typeof(Canvas));
                Canvas = root.GetComponent<Canvas>();
                ((RectTransform)root.transform).sizeDelta = new Vector2(1600, 900);
                Modals = new DuelPromptModals(root.transform, null, null);
                Field = new DuelFieldUI(null, root.transform, Modals);
                Field.BindSession(duel, null, null);
            }

            internal void Expect(IEnumerable<uint> expected)
            {
                uint[] positions = expected.OrderBy(sequence => sequence).ToArray();
                uint[] actual = ActiveGlows("_myHandViews", requireElement: true);
                Require(actual.SequenceEqual(positions), $"Expected hand glows [{string.Join(",", positions)}], got [{string.Join(",", actual)}].");
                Require(ActiveGlows("_oppHandViews", requireElement: false).Length == 0, "A hidden opponent card displayed a hand action.");
            }

            internal void ExpectLinkedZones(params string[] expected)
            {
                var board = Field.Root.transform.Find("DuelBoard");
                var active = board.GetComponentsInChildren<DuelZoneImage>()
                    .Where(zone => zone.LinkHighlighted).Select(zone => zone.name).OrderBy(name => name);
                Require(active.SequenceEqual(expected.OrderBy(name => name)), "The field highlighted the wrong Link zones.");
                var rims = board.GetComponentsInChildren<DuelLinkZoneRim>();
                Require(rims.Length == expected.Length && rims.All(rim => !rim.raycastTarget),
                    "A Link outline stayed visible after selection ended or blocked card input.");
            }

            internal void VerifyFieldReadouts(DuelSession duel)
            {
                var mine = duel.Players[duel.HumanPlayerIndex];
                var theirs = duel.Players[duel.AiPlayerIndex];
                foreach (string hand in new[] { "PlayerHand", "OpponentHand" })
                    Require(!Field.Root.transform.Find(hand).GetComponentsInChildren<Transform>(true)
                        .Any(node => node.name == "CardReadout"), "A hand card rendered a field readout.");

                Transform Slot(string fieldName, int index)
                {
                    var slots = (Array)typeof(DuelFieldUI).GetField(fieldName, PrivateInstance).GetValue(Field);
                    var slot = slots.GetValue(index);
                    return (Transform)slot.GetType().GetField("root").GetValue(slot);
                }
                const string attack = "Combat/Attack/Digits/Value", defense = "Combat/Defense/Digits/Value";
                const string rating = "Labels/Rating/Value", status = "Labels/Status/Value";
                Text Label(Transform root, string path) => root.Find(path).GetComponent<Text>();
                const uint monster = YgoConstants.TYPE_MONSTER;
                mine.mzone[0] = new LiveCardState
                {
                    code = BlueEyes, type = monster, level = 5, position = YgoConstants.POS_FACEUP_DEFENSE,
                    attack = 3700, baseAttack = 3000, defense = 2200, baseDefense = 2500,
                };
                mine.mzone[1] = new LiveCardState
                {
                    code = 84013237, type = monster | YgoConstants.TYPE_XYZ, rank = 0, effectsDisabled = true,
                    position = YgoConstants.POS_FACEUP_ATTACK, attack = 10000, defense = -2,
                    overlayCodes = new List<uint> { BlueEyes, KaiserSeaHorse },
                };
                mine.mzone[2] = new LiveCardState
                {
                    code = BlueEyes, type = monster, level = 8, position = YgoConstants.POS_FACEUP_ATTACK,
                    attack = 2800, baseAttack = 3000, defense = 2500, baseDefense = 2500,
                };
                theirs.mzone[6] = new LiveCardState
                {
                    code = 1861629, type = monster | YgoConstants.TYPE_LINK, link = 3,
                    linkMarker = 0x040 | 0x002 | 0x020,
                    position = YgoConstants.POS_FACEUP_ATTACK, attack = 2300, baseAttack = 2300,
                };
                mine.szone[0] = new LiveCardState
                {
                    code = 16178681, type = YgoConstants.TYPE_SPELL | YgoConstants.TYPE_PENDULUM,
                    position = YgoConstants.POS_FACEUP_ATTACK, lscale = 8, rscale = 4,
                };
                Field.Refresh();
                var normalSlot = Slot("_myMZone", 0);
                var normal = (RectTransform)normalSlot.Find("CardReadout");
                var xyz = (RectTransform)Slot("_myMZone", 1).Find("CardReadout");
                var link = (RectTransform)Slot("_emz", 0).Find("CardReadout");
                var scale = Slot("_mySZone", 0).Find("CardReadout");
                Require(new[] { normal, xyz, link }.All(readout => readout.gameObject.activeInHierarchy &&
                    !readout.GetComponentsInChildren<Graphic>().Any(graphic => graphic.raycastTarget)) &&
                    Label(normal, rating).text == "레벨 5" && Label(xyz, rating).text == "랭크 0" &&
                    Label(link, rating).text == "링크 3" && Label(xyz, status).text == "소재 2 · 무효" &&
                    Label(scale, rating).text == "스케일 8" && !scale.Find("Combat").gameObject.activeSelf,
                    "The field readouts did not show the current rating, materials, effects and scale.");
                Require(Label(normal, attack).text == "3700" && Label(normal, defense).text == "2200" &&
                    Label(normal, "Combat/Attack/Change/Arrow").text == "▲" && Label(normal, "Combat/Defense/Change/Arrow").text == "▼" &&
                    normal.Find("Combat/Defense/Rail").GetComponent<Image>().color.a > normal.Find("Combat/Attack/Rail").GetComponent<Image>().color.a &&
                    Label(xyz, attack).text == "10000" && Label(xyz, defense).text == "?" &&
                    Label(link, attack).text == "2300" && !link.Find("Combat/Defense").gameObject.activeSelf &&
                    ((RectTransform)link.Find("Combat")).anchorMin.y > ((RectTransform)normal.Find("Combat")).anchorMin.y,
                    "The field readouts lost a stat change, the active position, or the correct board-side placement.");
                var compass = link.Find("Combat/LinkDirections/Compass");
                Require(compass.Find("BottomRight").gameObject.activeSelf && compass.Find("Top").gameObject.activeSelf &&
                    compass.Find("Left").gameObject.activeSelf && !compass.Find("TopLeft").gameObject.activeSelf &&
                    !compass.Find("Bottom").gameObject.activeSelf && !compass.Find("Right").gameObject.activeSelf,
                    "The opponent's live Link markers were not turned to match the board.");
                Slot("_emz", 0).GetComponent<CardDragTrigger>().OnRightClick();
                ExpectLinkedZones("Zone_3_1", "Zone_4_3");
                theirs.mzone[6].linkMarker = 0x004;
                Field.Refresh();
                ExpectLinkedZones("Zone_2_1");

                var command = duel.ActivePrompt;
                SetPrompt(duel, new DuelPrompt { messageType = YgoConstants.MSG_SELECT_PLACE,
                    playerId = (byte)duel.HumanPlayerIndex, min = 1, placeFlag = ~(1u << 2) });
                Field.Refresh();
                ExpectLinkedZones();
                SetPrompt(duel, command);
                Field.Refresh();
                ExpectLinkedZones("Zone_2_1");

                normalSlot.GetComponent<CardDragTrigger>().OnRightClick();
                ExpectLinkedZones();
                var inspector = Field.Root.transform.Find("LeftInspector/CardDetails/CombatStats");
                Require(Label(inspector, "Attack/Digits/Value").text == "3700",
                    "Inspecting a field monster lost its current attack.");
                Slot("_myMZone", 2).GetComponent<CardDragTrigger>().OnRightClick();
                Require(Label(inspector, "Attack/Digits/Value").text == "2800" && inspector.Find("Attack/ChangePulse") == null,
                    "Inspecting a second copy used or animated the first copy's current stats.");
                var details = (Button)typeof(DuelFieldUI).GetField("_duelPadInspectButton", PrivateInstance).GetValue(Field);
                details.onClick.Invoke();
                var zoom = Field.Root.transform.Find("Overlay_CardZoom/ZoomWin/Info/CombatStats");
                mine.mzone[2].attack = 4200;
                Field.Refresh();
                Require(Label(zoom, "Attack/Digits/Value").text == "4200" &&
                    Label(inspector, "Attack/Digits/Value").text == "4200" &&
                    zoom.Find("Attack/ChangePulse").gameObject.activeInHierarchy,
                    "An open details panel did not follow a change to the selected card's live stats.");
                typeof(DuelFieldUI).GetMethod("CloseCardZoomModal", PrivateInstance).Invoke(Field, new object[] { false });
                normalSlot.GetComponent<CardDragTrigger>().OnRightClick();
                Require(!inspector.Find("Attack/ChangePulse").gameObject.activeInHierarchy,
                    "An unfinished stat animation followed the focus to a different copy.");

                mine.mzone[0].attack = 3200;
                mine.mzone[0].defense = 2800;
                mine.mzone[0].level = 6;
                Field.Refresh();
                var reduced = normal.Find("Combat/Attack");
                var reverse = (RectTransform)reduced.Find("ChangePulse/Sweep");
                float initial = reverse.anchorMin.x;
                reduced.GetComponent<DuelReadoutPulse>().Advance(.16f);
                Require(reverse.anchorMin.x < initial && Label(normal, attack).text == "3200" &&
                    Label(normal, defense).text == "2800" && normal.Find("Combat/Defense/ChangePulse").gameObject.activeInHierarchy &&
                    normal.Find("Labels/Rating/ChangePulse").gameObject.activeInHierarchy && Label(normal, rating).text == "레벨 6",
                    "The updated attack, defense or level did not animate with its current value.");

                var attackIcon = normal.Find("Combat/Attack/Symbol").GetComponent<Image>();
                var defenseIcon = normal.Find("Combat/Defense/Symbol").GetComponent<Image>();
                Require(attackIcon.sprite != null && defenseIcon.sprite != null && attackIcon.sprite != defenseIcon.sprite &&
                    !attackIcon.raycastTarget && !defenseIcon.raycastTarget,
                    "The compact readout did not have separate non-interactive attack and defense symbols.");
                mine.mzone[1].attack = mine.mzone[1].defense = int.MaxValue;
                Field.Refresh();

                var viewport = (RectTransform)Canvas.transform;
                var originalSize = viewport.sizeDelta;
                foreach (var size in new[] { new Vector2(1600, 900), new Vector2(1024, 768), new Vector2(1740, 805) })
                {
                    viewport.sizeDelta = size;
                    Canvas.ForceUpdateCanvases();
                    foreach (var readout in new[] { normal, xyz, link })
                    foreach (var path in new[] { attack, defense, rating })
                    {
                        var label = Label(readout, path);
                        if (!label.gameObject.activeInHierarchy) continue;
                        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(readout, label.rectTransform);
                        Require(readout.rect.Contains(bounds.min) && bounds.max.x <= readout.rect.xMax + .01f &&
                            bounds.max.y <= readout.rect.yMax + .01f && label.rectTransform.rect.height > 9f,
                            $"A field stat overflowed when the canvas resized to {size}.");
                    }
                    VerifyStatGlyphs(Label(normal, attack), "3200");
                    VerifyStatGlyphs(Label(xyz, attack), int.MaxValue.ToString());
                    VerifyStatGlyphs(Label(xyz, defense), int.MaxValue.ToString());
                }
                viewport.sizeDelta = originalSize;

                mine.mzone[4] = new LiveCardState
                {
                    code = 1861629, type = monster | YgoConstants.TYPE_LINK, link = 3,
                    position = YgoConstants.POS_FACEUP_ATTACK, attack = 2300, linkMarker = 0x040 | 0x008 | 0x001,
                };
                Field.Refresh();
                Slot("_myMZone", 4).GetComponent<CardDragTrigger>().OnRightClick();
                ExpectLinkedZones("Zone_5_2", "Zone_5_3", "Zone_5_4");
                mine.mzone[4].position = YgoConstants.POS_FACEDOWN_DEFENSE;

                mine.mzone[0].level = 0;
                theirs.mzone[6].position = YgoConstants.POS_FACEDOWN_DEFENSE;
                mine.mzone[1].overlayCodes.Clear();
                Field.Refresh();
                ExpectLinkedZones();
                Require(!Label(normal, rating).gameObject.activeInHierarchy && !link.gameObject.activeInHierarchy &&
                    Label(xyz, status).text == "소재 0 · 무효", "A field readout kept stale state when a card changed.");
                mine.mzone[0] = mine.mzone[1] = mine.mzone[2] = mine.mzone[4] = theirs.mzone[6] = mine.szone[0] = null;
                Field.Refresh();
                Require(!xyz.gameObject.activeInHierarchy && !scale.gameObject.activeInHierarchy,
                    "A vacated zone retained a rating or pendulum scale.");
                typeof(DuelFieldUI).GetMethod("ResetCardInspector", PrivateInstance).Invoke(Field, null);
                Debug.Log("[VERIFY DUEL CARD READOUT] Verified live combat stats, changing copies, ratings, scales, Link markers, face-down cards and three viewport sizes.");
                Debug.Log("[VERIFY DUEL STAT FIT] Attack and defense icons left room for larger digits. All ten digits stayed on one line across three viewports.");
            }

            private static void VerifyStatGlyphs(Text text, string expected)
            {
                Require(text.text == expected && text.horizontalOverflow == HorizontalWrapMode.Overflow,
                    "A stat field truncated or wrapped the full number.");
                using var mesh = new VertexHelper();
                var populate = typeof(Text).GetMethod("OnPopulateMesh", PrivateInstance, null, new[] { typeof(VertexHelper) }, null);
                populate.Invoke(text, new object[] { mesh });
                text.GetComponent<DuelStatDigitFit>().ModifyMesh(mesh);
                Require(mesh.currentVertCount == expected.Length * 4, "The stat field omitted rendered digits.");
                Rect rect = text.GetPixelAdjustedRect();
                var vertex = default(UIVertex);
                for (int i = 0; i < mesh.currentVertCount; i++)
                {
                    mesh.PopulateUIVertex(ref vertex, i);
                    Require(vertex.position.x >= rect.xMin - .1f && vertex.position.x <= rect.xMax + .1f &&
                        vertex.position.y >= rect.yMin - .1f && vertex.position.y <= rect.yMax + .1f,
                        "A stat digit rendered outside its allotted space.");
                }
                var cell = (RectTransform)text.transform.parent.parent;
                Require(rect.height / cell.rect.height > .78f, "The compact stat did not give its number most of the available height.");
            }

            private uint[] ActiveGlows(string fieldName, bool requireElement)
            {
                var views = (IEnumerable)typeof(DuelFieldUI).GetField(fieldName, PrivateInstance).GetValue(Field);
                var active = new List<uint>();
                uint index = 0;
                foreach (object view in views)
                {
                    var rect = (RectTransform)view.GetType().GetField("Rect").GetValue(view);
                    var glow = rect.Find(GlowPath);
                    Require(!requireElement || glow != null, "Missing glow under the visible hand artwork.");
                    if (glow != null && glow.gameObject.activeInHierarchy)
                        active.Add(index);
                    index++;
                }
                return active.ToArray();
            }

            public void Dispose()
            {
                Field.UnbindSession();
                UnityEngine.Object.DestroyImmediate(Canvas.gameObject);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException($"[HAND GLOW] {message}");
        }
    }
}
