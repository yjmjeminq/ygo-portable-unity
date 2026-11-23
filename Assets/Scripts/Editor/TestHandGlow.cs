using System;
using System.Collections;
using System.Collections.Generic;
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
            VerifyNumberTypeface();
            VerifyMainPhaseAndStaleState();
            VerifyOpponentTurnActivation();
            VerifyNativeCombatReadout(useHonest: true);
            VerifyNativeCombatReadout(useHonest: false);
            VerifyDirectAttackChoice();
            VerifyDefenderChoosesAttackTarget();
            VerifyAttackReplayChoice(continueAttack: false);
            VerifyAttackReplayChoice(continueAttack: true);
            VerifyNativeEquipHandoff();
            VerifyOpponentEquipmentFocus();
            VerifyNativeMonsterEquip();
            VerifyNativeChainReadouts();
            VerifyNativeFieldTargetContinuity();
            VerifyNativeChainCardFace(0);
            VerifyNativeChainCardFace(70046172);
            VerifyNativeChainCardFace(14087893);
            VerifyOpponentActivationInspector();
            VerifyNativeNegationOutcome(activationNegated: false);
            VerifyNativeNegationOutcome(activationNegated: true);
            VerifyNativeImpermanenceRay(setFirst: true);
            VerifyNativeImpermanenceRay(setFirst: false);
            VerifyMobileUnavailableEffectDetails();
            VerifyDragColumnAndPhaseShortcut();
            VerifyDuelOverframeLongPress();
            VerifyNativeFieldSpellDrag();
            VerifyDestroyedCopiesKeepTheirReadouts();
            VerifyFieldReplacementReadouts();
            VerifyTurnAndDrawReadouts();
            VerifyDefensePositionBattleReadout(34496660, BattleStatSource.Defense, 1800);
            VerifyDefensePositionBattleReadout(75372290, BattleStatSource.Attack, 1550);
            VerifyEffectModifiedBattleReadout();
            VerifyNativeBattleDamageFeedback(reflected: false);
            VerifyNativeBattleDamageFeedback(reflected: true);
            VerifyNativeCounterReadout();
            VerifyNativeIdentityReadout();
            VerifyNativeCardName(mobileLayout: false);
            VerifyNativeCardName(mobileLayout: true);
            VerifyInspectedCardMoves(swapInPlace: true);
            VerifyInspectedCardMoves(swapInPlace: false);
            VerifyInspectedCardMoves(swapInPlace: false, opponentSets: true);
            VerifyNativeSummonReadout(turnFaceDown: false);
            VerifyNativeSummonReadout(turnFaceDown: true);
            VerifyNativeLinkedZone();
            VerifyNativeCoLinkedZone();
            VerifyNativeMovingReadout();
            VerifyMovingReadoutAfterLayoutChange();
            VerifyCrowdedFieldAfterLayoutChange();
            VerifyNativePositionGroup();
            VerifyNativeTemporaryBanishment();
            VerifyNativeTokenArrivalReadout();
            VerifyNativePendulumArrivalReadout();
            VerifyPendulumScalePair();
            VerifyNativeChangingPendulumScale(0);
            VerifyNativeChangingPendulumScale(4);
            VerifyNativeFieldMaterials(xyz: true);
            VerifyNativeFieldMaterials(xyz: false);
            VerifyNativeSynchroMaterials();
            VerifyNativeFieldSumChoice(setSecondMaterial: false);
            VerifyNativeFieldSumChoice(setSecondMaterial: true);
            VerifyNativeMixedMaterials(ritual: false);
            VerifyNativeMixedMaterials(ritual: true);
            VerifyNativeGraveFusionMaterials();
            VerifyNativeXyzMaterialReadout();
            Debug.Log("[VERIFY HAND GLOW] Native summons and effects, distinct hand copies, blocked prompts, animation, off-turn activation and field-only monster ratings passed.");
        }

        private static void VerifyNumberTypeface()
        {
            const string figures = "0123456789";
            var font = DuelTypography.NumberFont;
            Require(font != LotdUiKit.FontKR && figures.All(font.HasCharacter) && "+-−?/".All(font.HasCharacter),
                "The bundled numeric font could not render all duel values.");
            font.RequestCharactersInTexture(figures, 24, FontStyle.Normal);
            Require(font.GetCharacterInfo('0', out var reference, 24) && reference.advance > 0,
                "The numeric font did not render its tabular figures.");
            foreach (char digit in figures)
                Require(font.GetCharacterInfo(digit, out var glyph, 24) && glyph.advance == reference.advance,
                    $"The glyph {digit} moved a tabular number out of alignment.");
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
            var inactive = handRow.GetChild((int)duplicateSlots[0]);
            var shade = inactive.GetComponentInChildren<CompositeCardView>().transform.Find("AspectBox/UnavailableShade");
            Require(shade != null && shade.gameObject.activeInHierarchy && !shade.GetComponent<Image>().raycastTarget,
                "A hand effect without a legal action was not dimmed.");
            inactive.GetComponent<CardDragTrigger>().OnLeftClick();
            var notice = ui.Field.Root.transform.Find("ActivationNotice");
            Require(notice.gameObject.activeInHierarchy && notice.Find("Reason").GetComponent<Text>().text ==
                    "발동 조건을 만족하지 않았습니다.",
                "An unavailable hand effect did not explain the failed activation.");

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

            var completeArrival = (Action)typeof(DuelFieldUI).GetMethod("ReserveCardArrival", PrivateInstance)
                .Invoke(ui.Field, new object[] { player, YgoConstants.LOCATION_HAND, availableCopy, (Action)ui.Field.Refresh });
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

        private static void VerifyNativeCombatReadout(bool useHonest)
        {
            const uint axeOfDespair = 40619825;
            const uint honest = 37742478;
            const uint mysticalElf = 15025844;
            uint[] main = Enumerable.Repeat(KaiserSeaHorse, 24).Concat(Enumerable.Repeat(axeOfDespair, 8))
                .Concat(Enumerable.Repeat(useHonest ? honest : KaiserSeaHorse, 8)).ToArray();
            uint[] opponent = Enumerable.Repeat(mysticalElf, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Live combat readout", main, Array.Empty<uint>(),
                opponent, Array.Empty<uint>(), true, session => session.ActivePrompt?.summonable.Any(card => card.code == KaiserSeaHorse) == true &&
                    session.Players[session.HumanPlayerIndex].hand.Any(card => card.code == axeOfDespair) &&
                    (!useHonest || session.Players[session.HumanPlayerIndex].hand.Any(card => card.code == honest)));
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

            int summon = duel.ActivePrompt.summonable.FindIndex(card => card.code == KaiserSeaHorse);
            duel.SubmitResponseInt(summon << 16);
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
            var inspectorPulse = inspector.transform.parent.parent.GetComponent<DuelReadoutPulse>();
            var previousAttack = attackCell.Find("Digits/PreviousValue").GetComponent<Text>();
            var previousInspector = inspector.transform.parent.Find("PreviousValue").GetComponent<Text>();
            Require(previousAttack.gameObject.activeInHierarchy && previousInspector.gameObject.activeInHierarchy &&
                    previousAttack.text == "1700" && previousInspector.text == "1700" &&
                    previousAttack.canvasRenderer.GetAlpha() > .99f,
                "The previous number did not remain visible at the start of the handoff.");
            float entry = attack.rectTransform.anchoredPosition.y;
            Require(entry < 0f && inspector.rectTransform.anchoredPosition.y < 0f &&
                attack.rectTransform.localScale.x < 1f && inspector.rectTransform.localScale.x < 1f,
                "The equipped attack values changed without moving into place.");
            float start = sweep.anchorMax.x;
            pulse.Advance(.12f);
            inspectorPulse.Advance(.12f);
            float moved = sweep.anchorMax.x;
            float rising = attack.rectTransform.anchoredPosition.y;
            ui.Field.Refresh();
            Require(moved > start && sweep.anchorMax.x == moved && rising > entry && rising < 0f &&
                attack.rectTransform.anchoredPosition.y == rising && attack.text == "2700" && inspector.text == "2700" &&
                previousAttack.canvasRenderer.GetAlpha() > 0f && attack.canvasRenderer.GetAlpha() > 0f,
                "An unchanged refresh restarted the number motion or produced an in-between stat.");
            pulse.Advance(.40f);
            inspectorPulse.Advance(.40f);
            Require(!sweep.gameObject.activeInHierarchy && !previousAttack.gameObject.activeSelf &&
                !previousInspector.gameObject.activeSelf && attack.canvasRenderer.GetAlpha() > .99f &&
                new[] { attack, inspector }.All(label => label.text == "2700" &&
                    label.rectTransform.localScale == Vector3.one && label.rectTransform.anchoredPosition == Vector2.zero),
                "The attack numbers did not finish centered at their full size.");
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
            ui.Modals.ShowPrompt(duel, duel.ActivePrompt);
            var picking = ui.Modals.ModalRoot.transform.Find("Win/Body");
            Require(picking.Find("AttackSource/Combat/Attack/Digits/Value").GetComponent<Text>().text == "2700" &&
                    picking.Find("AttackTarget/Heading/Name").GetComponent<Text>().text == "대상 · 뒷면 카드" &&
                    !picking.Find("AttackTarget/Combat").gameObject.activeSelf,
                "The attack picker lost the equipped attack or revealed the face-down target's values.");
            var target = duel.ActivePrompt.candidates.Single();
            var defender = ui.Field.Root.transform.Find($"DuelBoard/Zone_{6 - (int)target.sequence}_1");
            ui.Modals.HideAll();
            duel.SubmitResponse(DuelSession.PackCardSelection(0));
            ui.Field.Refresh();
            var attackerFace = slot.Find("Card/CardView");
            var defenderFace = defender.Find("Card/CardView").GetComponent<CompositeCardView>();
            Require(attackerFace.gameObject.activeInHierarchy && defenderFace.gameObject.activeInHierarchy &&
                defenderFace.CurrentCode == 0 && (defender.Find("CardReadout") == null ||
                    !defender.Find("CardReadout").gameObject.activeInHierarchy),
                "The attack declaration hid a participant or revealed the defense before its flip.");

            var steps = (Queue<IEnumerator>)typeof(CardAnimationManager).GetField("_steps", PrivateInstance).GetValue(animation);
            var flip = steps.First(step => step.GetType().Name.StartsWith("<MoveCard>", StringComparison.Ordinal));
            Require(flip.MoveNext() && !defenderFace.gameObject.activeInHierarchy &&
                flip.Current is IEnumerator flipFrames && flipFrames.MoveNext() &&
                animation.transform.Find("MovingCard").GetComponent<CompositeCardView>().CurrentCode == 0,
                "The battle flip did not start from the hidden face.");
            Require(!flip.MoveNext(), "The defender's flip did not finish.");
            Require(defenderFace.gameObject.activeInHierarchy && defenderFace.CurrentCode == mysticalElf &&
                defender.Find("CardReadout/Combat/Defense/Digits/Value").GetComponent<Text>().text == "2000",
                "The revealed defender disappeared between its flip and the attack.");

            bool usedHonest = false;
            for (int turn = 0; turn < 12 && duel.ActivePrompt is { messageType: YgoConstants.MSG_SELECT_CHAIN } chain; turn++)
            {
                int index = !useHonest || usedHonest || chain.playerId != duel.HumanPlayerIndex
                    ? -1 : chain.candidates.FindIndex(card => card.code == honest && card.location == YgoConstants.LOCATION_HAND);
                usedHonest |= index >= 0;
                duel.SubmitResponseInt(index);
            }
            var readout = animation.transform.Find("BattleReadout");
            string expectedAttack = useHonest ? "3500" : "2700";
            Require(usedHonest == useHonest && readout != null && duel.RetryCount == 0 &&
                attack.text == "2700" && readout.Find("Attacker/Values/Attack/Digits/Value").GetComponent<Text>().text == expectedAttack &&
                readout.Find("Defender/Values/Defense/Digits/Value").GetComponent<Text>().text == "2000" &&
                readout.Find("Defender/Role/Text").GetComponent<Text>().text == "상대 수비" &&
                !readout.Find("Attacker/Values/Defense").gameObject.activeSelf &&
                !readout.Find("Defender/Values/Attack").gameObject.activeSelf,
                "The attack presentation lost the native damage-step increase or the revealed defense.");
            ui.Field.Refresh();
            Require(attackerFace.gameObject.activeInHierarchy && slot.Find("CardReadout").gameObject.activeInHierarchy &&
                duel.Players[duel.AiPlayerIndex].mzone[target.sequence] == null && defenderFace.gameObject.activeInHierarchy,
                "The waiting attacker or battle-destroyed defender was removed before the attack began.");
            // The frame sampler skips the preceding animations. Apply their queued card readouts before sampling the strike.
            foreach (var step in steps.TakeWhile(step => !step.GetType().Name.StartsWith("<Attack>", StringComparison.Ordinal)))
                if (step.GetType().Name.StartsWith("<ApplyFieldReadoutUpdate>", StringComparison.Ordinal))
                    Require(!step.MoveNext(), "The damage-step effect did not release its card readout.");
            Require(attack.text == expectedAttack, "The chain's attack value did not reach the field before combat began.");
            TestCardPresentation.VerifyAttackFlight(animation, expectedAttack, "1650", "4", () =>
            {
                var overlay = animation.transform.Find("DefendingCardReadout");
                var impact = animation.transform.Find("Strike");
                Require(!attackerFace.gameObject.activeInHierarchy && defenderFace.gameObject.activeInHierarchy &&
                    !defender.Find("CardReadout").gameObject.activeInHierarchy && overlay != null &&
                    overlay.Find("CardReadout/Combat/Defense/Digits/Value").GetComponent<Text>().text == "2000" &&
                    (impact == null || overlay.GetSiblingIndex() > impact.GetSiblingIndex()),
                    "The attack duplicated its stat readouts or hid the defender's values under the impact.");
            });
            Require(slot.Find("CardReadout").gameObject.activeInHierarchy && attack.text == expectedAttack &&
                defender.Find("CardReadout").gameObject.activeInHierarchy && animation.transform.Find("DefendingCardReadout") == null,
                "The card and its current stats did not return to the field.");
            var destroy = steps.Last(step => step.GetType().Name.StartsWith("<MoveCard>", StringComparison.Ordinal));
            Require(destroy.MoveNext() && defenderFace.gameObject.activeInHierarchy,
                "The destroyed defender vanished before its break effect.");
            Require(destroy.MoveNext() && !defenderFace.gameObject.activeInHierarchy &&
                destroy.Current is IEnumerator depart && depart.MoveNext() &&
                animation.transform.Find("MovingCard").GetComponent<CompositeCardView>().CurrentCode == mysticalElf,
                "The defeated defender did not pass from its zone to its destruction animation.");
            Require(!destroy.MoveNext() && !defenderFace.gameObject.activeInHierarchy &&
                ((IDictionary)typeof(DuelFieldUI).GetField("_battleCardDisplays", PrivateInstance).GetValue(ui.Field)).Count == 0,
                "A destroyed defender or queued battle display remained on the field.");
            Debug.Log($"[VERIFY DUEL BATTLE STATS] A set defender stayed hidden until its flip, showed 2000 DEF through the {expectedAttack} ATK strike, and left at destruction.");
        }

        private static void VerifyDirectAttackChoice()
        {
            const uint wattgiraffe = 402568, axe = 40619825, sparkman = 20721928;
            var deck = Enumerable.Repeat(wattgiraffe, 24).Concat(Enumerable.Repeat(axe, 16)).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Direct or monster attack", deck, empty,
                Enumerable.Repeat(sparkman, 40).ToArray(), empty, false,
                session => session.ActivePrompt?.playerId == session.AiPlayerIndex &&
                    session.ActivePrompt.summonable.Exists(card => card.code == sparkman) &&
                    new[] { wattgiraffe, axe }.All(code => session.Players[session.HumanPlayerIndex].hand.Exists(card => card.code == code)));
            duel.SubmitResponseInt(0);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            duel.SubmitResponseInt(7);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            int summon = duel.ActivePrompt.summonable.FindIndex(card => card.code == wattgiraffe);
            Require(summon >= 0 && duel.ActivePrompt.playerId == duel.HumanPlayerIndex,
                "The player could not summon the direct attacker.");
            duel.SubmitResponseInt(summon << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            ActivateFromMainPhase(duel, axe);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD
                ? SelectCard(prompt, wattgiraffe) : null);
            duel.SubmitResponseInt(6);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            var battle = duel.ActivePrompt;
            Require(battle?.messageType == YgoConstants.MSG_SELECT_BATTLECMD && battle.attackable.Count == 1 &&
                battle.attackable[0].code == wattgiraffe && battle.attackable[0].directAttackable,
                "The direct-attack fixture had no legal attack.");
            duel.SubmitResponseInt(1);
            var choice = duel.ActivePrompt;
            Require(choice?.messageType == YgoConstants.MSG_SELECT_YESNO && choice.IsDirectAttackChoice,
                "The native core did not offer a choice between the two attacks.");
            var source = duel.GetAttackSelectionSource(choice);
            Require(source?.code == wattgiraffe && source.attack == 2200,
                "Direct attack selection lost the attacking monster's live values.");

            byte[] response = null;
            using var ui = new HandUI(duel, mobileLayout: true, submitResponse: bytes => response = bytes);
            ((RectTransform)ui.Canvas.transform).sizeDelta = new Vector2(1740f, 805f);
            var events = EventSystem.current;
            var oldFocus = events?.currentSelectedGameObject;
            GameObject localEvents = null;
            if (events == null)
            {
                localEvents = new GameObject("Attack choice controller events", typeof(EventSystem));
                events = localEvents.GetComponent<EventSystem>();
                if (EventSystem.current == null) typeof(EventSystem).GetMethod("OnEnable", PrivateInstance).Invoke(events, null);
            }
            try
            {
                ui.Modals.ShowPrompt(duel, choice);
                ui.Field.Refresh();
                var window = ui.Modals.ModalRoot.transform.Find("Win");
                Require(window.Find("Hdr/TitleBand/Title").GetComponent<Text>().text == "공격 방식 선택" &&
                    window.Find("Body/AttackSource/Heading/Name").GetComponent<Text>().text == $"공격 · {CardDatabase.GetName(wattgiraffe)}" &&
                    window.Find("Body/AttackSource/Combat/Attack/Digits/Value").GetComponent<Text>().text == "2200",
                    "The choice window did not show the attacker's active values.");
                events.SetSelectedGameObject(null);
                Require(!ui.Modals.TryConfirm() && response == null, "An unfocused submit picked a direct attack.");
                window.Find("Footer/Btn_몬스터 공격/B").GetComponent<Button>().onClick.Invoke();
                Require(response?.Length == 4 && BitConverter.ToInt32(response, 0) == 0,
                    "Monster attack did not submit the native alternative.");
                duel.SubmitResponse(response);
                var target = duel.ActivePrompt;
                Require(target?.IsAttackTargetSelection == true && target.candidates.Single().code == sparkman,
                    "Choosing monster attack did not open its native target selection.");
                response = null;
                ui.Modals.ShowPrompt(duel, target);
                Require(ui.Modals.TryCancelOrPass() && response?.Length == 4 && BitConverter.ToInt32(response, 0) == -1,
                    "The normal attack could not be canceled at target selection.");
                duel.SubmitResponse(response);
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD &&
                    duel.ActivePrompt.attackable.Count == 1, "Canceling the target spent the attack.");

                duel.SubmitResponseInt(1);
                choice = duel.ActivePrompt;
                Require(choice?.IsDirectAttackChoice == true, "The direct option did not return after canceling.");
                response = null;
                ui.Modals.ShowPrompt(duel, choice);
                Canvas.ForceUpdateCanvases();
                Require(events.currentSelectedGameObject == window.Find("Footer/Btn_몬스터 공격/B").gameObject,
                    "Controller focus did not start on the first attack option.");
                ui.Modals.HandlePad(new DeckPadFrame(Vector2Int.right, DeckPadButtons.None));
                Require(events.currentSelectedGameObject == window.Find("Footer/Btn_직접 공격/B").gameObject,
                    "Controller focus could not reach the direct attack.");
                ui.Modals.HandlePad(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Submit));
                Require(response?.Length == 4 && BitConverter.ToInt32(response, 0) == 1,
                    "Direct attack did not submit the native affirmative response.");
                bool direct = false;
                duel.OnAttackDeclared += (_, _, _, _, location, _) => direct |= location == 0;
                duel.SubmitResponse(response);
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && prompt.forced
                    ? BitConverter.GetBytes(0) : null);
                Require(direct && duel.Players[duel.AiPlayerIndex].lp == 5800 && duel.RetryCount == 0,
                    "The selected direct attack did not resolve against the player.");
            }
            finally
            {
                if (localEvents != null)
                {
                    if (EventSystem.current == events) typeof(EventSystem).GetMethod("OnDisable", PrivateInstance).Invoke(events, null);
                    UnityEngine.Object.DestroyImmediate(localEvents);
                }
                else events.SetSelectedGameObject(oldFocus);
            }
            Debug.Log("[VERIFY DUEL DIRECT CHOICE] Live stats, focused controls and both native attack branches passed.");
        }

        private static void VerifyDefenderChoosesAttackTarget()
        {
            const uint patrician = 19153634, mummy = 4861205, sparkman = 20721928, wattgiraffe = 402568, axe = 40619825;
            var required = new[] { patrician, mummy, sparkman };
            var deck = required.SelectMany(code => Enumerable.Repeat(code, 13)).Append(patrician).ToArray();
            var empty = Array.Empty<uint>();
            var enemy = Enumerable.Repeat(wattgiraffe, 24).Concat(Enumerable.Repeat(axe, 16)).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Defender chooses the attack target", deck, empty, enemy, empty, true,
                session => required.All(code => session.Players[session.HumanPlayerIndex].hand.Exists(card => card.code == code)) &&
                    new[] { wattgiraffe, axe }.All(code => session.Players[session.AiPlayerIndex].hand.Exists(card => card.code == code)));

            void StepToMainPhase(uint pick = 0)
            {
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                {
                    if (pick != 0 && prompt.messageType == YgoConstants.MSG_SELECT_CARD) return SelectCard(prompt, pick);
                    if (pick == patrician && prompt.messageType == YgoConstants.MSG_SELECT_POSITION)
                        return BitConverter.GetBytes(YgoConstants.POS_FACEUP_DEFENSE);
                    return null;
                });
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    "The defender-controlled attack fixture did not resolve its activation.");
            }

            ActivateFromMainPhase(duel, mummy);
            StepToMainPhase();
            int summonEffect = duel.ActivePrompt.activatable.FindIndex(card => card.code == mummy &&
                card.location == YgoConstants.LOCATION_SZONE);
            Require(summonEffect >= 0, "Call of the Mummy did not offer its field effect.");
            duel.SubmitResponseInt((summonEffect << 16) | 5);
            StepToMainPhase(patrician);
            int set = duel.ActivePrompt.mSetable.FindIndex(card => card.code == sparkman);
            Require(set >= 0, "The defender could not set the alternative battle target.");
            duel.SubmitResponseInt((set << 16) | 3);
            StepToMainPhase();
            duel.SubmitResponseInt(7);
            StepToMainPhase();
            Require(duel.ActivePrompt.playerId == duel.AiPlayerIndex &&
                duel.ActivePrompt.summonable.Exists(card => card.code == wattgiraffe), "The opponent could not summon its attacker.");
            int normal = duel.ActivePrompt.summonable.FindIndex(card => card.code == wattgiraffe);
            duel.SubmitResponseInt(normal << 16);
            StepToMainPhase();
            ActivateFromMainPhase(duel, axe);
            StepToMainPhase(wattgiraffe);
            duel.SubmitResponseInt(6);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            var battle = duel.ActivePrompt;
            Require(battle?.messageType == YgoConstants.MSG_SELECT_BATTLECMD && battle.attackable.Count == 1 &&
                battle.attackable[0].code == wattgiraffe, "The opponent did not get its native attack command.");
            duel.SubmitResponseInt(1);
            var choice = duel.ActivePrompt;
            Require(choice?.messageType == YgoConstants.MSG_SELECT_EFFECTYN && choice.IsDirectAttackChoice &&
                choice.playerId == duel.HumanPlayerIndex && choice.promptCardCode == wattgiraffe,
                "The defender did not get the native direct-attack choice.");

            byte[] response = null;
            using var ui = new HandUI(duel, mobileLayout: true, submitResponse: bytes => response = bytes);
            ((RectTransform)ui.Canvas.transform).sizeDelta = new Vector2(1740f, 805f);
            ui.Modals.ShowPrompt(duel, choice);
            var window = ui.Modals.ModalRoot.transform.Find("Win");
            Require(window.Find("Hdr/TitleBand/Title").GetComponent<Text>().text == "상대 공격 방식 선택" &&
                window.Find("Body/AttackSource/Combat/Attack/Digits/Value").GetComponent<Text>().text == "2200" &&
                window.Find("Body/AttackSource/Heading/Rating/Number")?.GetComponent<Text>().text == "4",
                "The defender's choice lost the native opponent's combat value.");
            window.Find("Footer/Btn_몬스터 공격/B").GetComponent<Button>().onClick.Invoke();
            Require(response?.Length == 4 && BitConverter.ToInt32(response, 0) == 0,
                "The defender's monster attack decision did not submit the native response.");
            duel.SubmitResponse(response);
            response = null;
            var target = duel.ActivePrompt;
            Require(target?.IsAttackTargetSelection == true && target.playerId == duel.HumanPlayerIndex &&
                target.candidates.Count == 2 && target.candidates.All(card => card.controller == duel.HumanPlayerIndex),
                $"Patrician of Darkness did not give the defender two native targets. " +
                $"Prompt {target?.messageType}, player {target?.playerId}, hint {target?.SelectionHint}, " +
                $"candidates {target?.candidates.Count}. Defending monsters: " +
                string.Join(", ", duel.Players[duel.HumanPlayerIndex].mzone.Where(card => card != null)
                    .Select(card => $"{card.code}/{card.position}")));
            var source = duel.GetAttackSelectionSource(target);
            Require(source?.code == wattgiraffe && source.controller == duel.AiPlayerIndex && source.attack == 2200,
                "The attack preview lost the attacker when the other player selected its target.");

            ui.Modals.ShowPrompt(duel, target);
            ui.Field.Refresh();
            var body = ui.Modals.ModalRoot.transform.Find("Win/Body");
            var sourceTitle = body.Find("AttackSource/Heading/Name").GetComponent<Text>();
            var targetTitle = body.Find("AttackTarget/Heading/Name").GetComponent<Text>();
            var targetCombat = body.Find("AttackTarget/Combat");
            var sourceRating = body.Find("AttackSource/Heading/Rating");
            var targetRating = body.Find("AttackTarget/Heading/Rating");
            Require(sourceTitle.text == $"상대 공격 · {CardDatabase.GetName(wattgiraffe)}" && sourceTitle.color == DuelStyle.Opponent &&
                body.Find("AttackSource/Combat/Attack/Digits/Value").GetComponent<Text>().text == "2200",
                "The mobile battle comparison used the defender's side or a stale attack value.");
            Require(sourceRating?.Find("Number").GetComponent<Text>().text == source.level.ToString() &&
                sourceRating.Find("Symbol").GetComponent<Image>().sprite == DuelStatSymbols.Rating(DuelRatingKind.Level) &&
                !targetRating.gameObject.activeSelf, "The comparison lost the attacker's level or showed an unselected target's rating.");

            var mine = duel.Players[duel.HumanPlayerIndex];
            var defender = mine.mzone.Single(card => card?.code == patrician);
            FieldSlot(ui.Field, defender).GetComponent<CardDragTrigger>().OnPointerEnterAction.Invoke();
            Require(defender.IsDefense && targetTitle.text == $"대상 · {CardDatabase.GetName(patrician)}" &&
                targetTitle.color == DuelStyle.Player && targetCombat.gameObject.activeInHierarchy &&
                targetCombat.Find("Defense/Digits/Value").GetComponent<Text>().text == defender.defense.ToString() &&
                targetCombat.Find("Defense/Rail").GetComponent<Image>().color.a > targetCombat.Find("Attack/Rail").GetComponent<Image>().color.a,
                "The comparison did not show the legal target's controller and current defense.");
            Require(targetRating.gameObject.activeInHierarchy &&
                targetRating.Find("Number").GetComponent<Text>().text == defender.level.ToString(),
                "The focused defender did not show its own live level.");
            var viewport = (RectTransform)ui.Canvas.transform;
            foreach (var size in new[] { new Vector2(1740, 805), new Vector2(852, 393) })
            {
                viewport.sizeDelta = size;
                Canvas.ForceUpdateCanvases();
                foreach (var (title, rating) in new[] { (sourceTitle, sourceRating), (targetTitle, targetRating) })
                {
                    var heading = (RectTransform)rating.parent;
                    var nameBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(heading, title.rectTransform);
                    var badgeBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(heading, rating);
                    var generator = title.cachedTextGenerator;
                    generator.Populate(title.text, title.GetGenerationSettings(title.rectTransform.rect.size));
                    Require(nameBounds.max.x < badgeBounds.min.x && badgeBounds.max.x <= heading.rect.xMax &&
                            generator.lineCount == 1 && generator.characterCountVisible >= title.text.Length &&
                            !rating.GetComponentsInChildren<Graphic>().Any(graphic => graphic.raycastTarget),
                        $"The comparison did not fit '{title.text}' at {size} " +
                        $"({generator.characterCountVisible}/{title.text.Length} characters, {generator.lineCount} lines, " +
                        $"heading {heading.rect.size}, title {title.rectTransform.rect.size}, gap {badgeBounds.min.x - nameBounds.max.x:F1}).");
                }
            }

            var setTarget = mine.mzone.Single(card => card?.code == sparkman);
            var setTrigger = FieldSlot(ui.Field, setTarget).GetComponent<CardDragTrigger>();
            setTrigger.OnPointerEnterAction.Invoke();
            Require(targetTitle.text == "대상 · 뒷면 카드" && !targetCombat.gameObject.activeSelf && !targetRating.gameObject.activeSelf &&
                targetTitle.rectTransform.offsetMax.x == 0f &&
                sourceTitle.text.Contains(CardDatabase.GetName(wattgiraffe)) && response == null,
                "Previewing a set target showed stale values or submitted an attack.");
            setTrigger.OnLeftClick.Invoke();
            int selected = target.candidates.FindIndex(card => card.sequence == setTarget.sequence);
            Require(response != null && selected >= 0 && response.SequenceEqual(DuelSession.PackCardSelection(selected)),
                "Clicking the set target did not immediately submit its native index.");
            duel.SubmitResponse(response);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            Require(mine.grave.Exists(card => card.code == sparkman) && duel.RetryCount == 0,
                "The native attack did not resolve on the set card chosen by the defender.");
            Debug.Log("[VERIFY DUEL DEFENDER CHOICE] Native target transfer, live attacker, defense, set-card privacy and immediate selection passed.");
        }

        private static void VerifyAttackReplayChoice(bool continueAttack)
        {
            const uint axe = 40619825, doubleSummon = 43422537, evacuation = 94192409,
                sparkman = 20721928, mysticalElf = 15025844;
            var deck = Enumerable.Repeat(KaiserSeaHorse, 24).Concat(Enumerable.Repeat(axe, 16)).ToArray();
            var required = new[] { doubleSummon, evacuation, sparkman, mysticalElf };
            var opponent = required.SelectMany(code => Enumerable.Repeat(code, 10)).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Attack replay after a target leaves", deck, empty, opponent, empty,
                false, session => required.All(code => session.Players[session.AiPlayerIndex].hand.Exists(card => card.code == code)) &&
                    new[] { KaiserSeaHorse, axe }.All(code => session.Players[session.HumanPlayerIndex].hand.Exists(card => card.code == code)));

            void ReachMainPhase(uint pick = 0)
            {
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => pick != 0 && prompt.messageType == YgoConstants.MSG_SELECT_CARD
                    ? SelectCard(prompt, pick) : null);
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    "The attack replay fixture could not reach the main phase.");
            }
            void Summon(uint code)
            {
                int index = duel.ActivePrompt.summonable.FindIndex(card => card.code == code);
                Require(index >= 0, $"The replay fixture could not summon {code}.");
                duel.SubmitResponseInt(index << 16);
                ReachMainPhase();
            }

            Summon(sparkman);
            ActivateFromMainPhase(duel, doubleSummon);
            ReachMainPhase();
            Summon(mysticalElf);
            int set = duel.ActivePrompt.sSetable.FindIndex(card => card.code == evacuation);
            Require(set >= 0, "The defender could not set Compulsory Evacuation Device.");
            duel.SubmitResponseInt((set << 16) | 4);
            ReachMainPhase();
            duel.SubmitResponseInt(7);
            ReachMainPhase();
            Require(duel.ActivePrompt.playerId == duel.HumanPlayerIndex, "The attack replay fixture did not switch turns.");
            Summon(KaiserSeaHorse);
            ActivateFromMainPhase(duel, axe);
            ReachMainPhase(KaiserSeaHorse);

            duel.SubmitResponseInt(6);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD &&
                duel.ActivePrompt.attackable.Single().code == KaiserSeaHorse, "The replay fixture did not enter battle.");
            duel.SubmitResponseInt(1);
            Require(duel.ActivePrompt?.IsAttackTargetSelection == true && duel.ActivePrompt.candidates.Count == 2,
                "The attacker did not have both starting targets.");
            duel.SubmitResponse(SelectCard(duel.ActivePrompt, sparkman));

            bool returned = false;
            for (int step = 0; step < 20; step++)
            {
                var prompt = duel.ActivePrompt;
                if (prompt == null) duel.StepUntilPrompt();
                else if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
                {
                    int answer = returned ? -1 : prompt.candidates.FindIndex(card => card.code == evacuation);
                    returned |= answer >= 0;
                    duel.SubmitResponseInt(answer);
                }
                else if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN)
                {
                    bool useTrap = !returned && prompt.promptCardCode == evacuation;
                    returned |= useTrap;
                    duel.SubmitResponseInt(useTrap ? 1 : 0);
                }
                else if (returned && prompt.messageType == YgoConstants.MSG_SELECT_CARD)
                    duel.SubmitResponse(SelectCard(prompt, sparkman));
                else break;
            }
            var replay = duel.ActivePrompt;
            Require(returned && replay?.IsAttackReplayChoice == true &&
                duel.Players[duel.AiPlayerIndex].hand.Exists(card => card.code == sparkman) && duel.RetryCount == 0,
                $"The target removal did not trigger a native attack replay (prompt {replay?.messageType}/{replay?.promptDesc}).");
            var source = duel.GetAttackSelectionSource(replay);
            Require(source?.code == KaiserSeaHorse && source.attack == 2700,
                "The attack replay lost the original attacker and its live equipment bonus.");

            byte[] response = null;
            using var ui = new HandUI(duel, mobileLayout: true, submitResponse: bytes => response = bytes);
            ((RectTransform)ui.Canvas.transform).sizeDelta = new Vector2(1740f, 805f);
            var defender = duel.Players[duel.AiPlayerIndex];
            int initialLp = defender.lp;
            ui.Modals.ShowPrompt(duel, replay);
            ui.Field.Refresh();
            var window = ui.Modals.ModalRoot.transform.Find("Win");
            Require(window.Find("Hdr/TitleBand/Title").GetComponent<Text>().text == "공격 대상 변경" &&
                window.Find("Body/AttackSource/Heading/Name").GetComponent<Text>().text == $"공격 · {CardDatabase.GetName(KaiserSeaHorse)}" &&
                window.Find("Body/AttackSource/Heading/Rating/Number")?.GetComponent<Text>().text == source.level.ToString() &&
                window.Find("Body/AttackSource/Combat/Attack/Digits/Value").GetComponent<Text>().text == "2700",
                "The replay window did not identify the reason and actual attacker.");
            string option = continueAttack ? "공격 계속" : "공격 중단";
            window.Find($"Footer/Btn_{option}/B").GetComponent<Button>().onClick.Invoke();
            Require(response?.Length == 4 && BitConverter.ToInt32(response, 0) == (continueAttack ? 1 : 0),
                "The replay decision did not preserve the native response.");
            duel.SubmitResponse(response);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);

            if (continueAttack)
            {
                var next = duel.ActivePrompt;
                Require(next?.IsAttackTargetSelection == true && next.candidates.Single().code == mysticalElf &&
                    duel.GetAttackSelectionSource(next)?.code == KaiserSeaHorse,
                    "Continuing did not offer the remaining native target.");
                response = null;
                ui.Modals.ShowPrompt(duel, next);
                ui.Field.Refresh();
                var elf = defender.mzone.Single(card => card?.code == mysticalElf);
                FieldSlot(ui.Field, elf).GetComponent<CardDragTrigger>().OnLeftClick.Invoke();
                Require(response != null && response.SequenceEqual(DuelSession.PackCardSelection(0)),
                    "The replay target still required a second confirmation.");
                duel.SubmitResponse(response);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                Require(defender.grave.Exists(card => card.code == mysticalElf) && defender.lp == initialLp - 1900,
                    "The original attack did not resolve against the new target.");
            }
            else
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD && !duel.HasActiveAttack &&
                    defender.mzone.Any(card => card?.code == mysticalElf) && defender.lp == initialLp,
                    "Stopping the replay still attacked a monster or deducted life points.");
            Require(duel.RetryCount == 0, "The replay decision caused an engine retry.");
            Debug.Log($"[VERIFY DUEL ATTACK REPLAY] The live attacker and native {(continueAttack ? "continue" : "stop")} path passed.");
        }

        private static void VerifyNativeEquipHandoff()
        {
            const uint unity = 56747793, typhoon = 5318639, doubleSummon = 43422537, sparkman = 20721928;
            var main = new[] { unity, typhoon, doubleSummon, KaiserSeaHorse, sparkman }
                .SelectMany(code => Enumerable.Repeat(code, 8)).ToArray();
            var opponent = Enumerable.Repeat(BlueEyes, 40).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Equip presentation", main, empty, opponent, empty, true,
                session => session.Players[session.HumanPlayerIndex].hand.Select(card => card.code).Distinct().Count() == 5);
            var own = duel.Players[duel.HumanPlayerIndex];

            void ReachIdle(uint choose = 0)
            {
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => choose != 0 && prompt.messageType == YgoConstants.MSG_SELECT_CARD
                    ? SelectCard(prompt, choose) : null);
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    "The equipment fixture did not resolve its native action.");
            }
            void Summon(uint code)
            {
                int index = duel.ActivePrompt.summonable.FindIndex(card => card.code == code);
                Require(index >= 0, "The equipment fixture did not offer both potential targets.");
                duel.SubmitResponseInt(index << 16);
                ReachIdle();
            }
            Summon(sparkman);
            ActivateFromMainPhase(duel, doubleSummon);
            ReachIdle();
            Summon(KaiserSeaHorse);

            var target = own.mzone.Single(card => card?.code == KaiserSeaHorse);
            var other = own.mzone.Single(card => card?.code == sparkman);
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            var steps = AnimationSteps(animator);
            var slot = FieldSlot(ui.Field, target);
            slot.GetComponent<CardDragTrigger>().OnRightClick();
            ui.Field.Root.transform.Find("LeftInspector/CardDetails/ArtworkControls/Details/B").GetComponent<Button>().onClick.Invoke();
            Require(ui.Field.IsReadingCardDetails, "The target's details could not open for the equipment fixture.");
            var inspector = ui.Field.Root.transform.Find("LeftInspector/CardDetails/CombatStats");
            var zoom = ui.Field.Root.transform.Find("Overlay_CardZoom/ZoomWin/Info/CombatStats");
            void CheckValues(string attack, string defense, string delta = "")
            {
                foreach (var values in new[] { slot.Find("CardReadout/Combat"), inspector, zoom })
                {
                    string shownAttack = values.Find("Attack/Digits/Value").GetComponent<Text>().text;
                    string shownDefense = values.Find("Defense/Digits/Value").GetComponent<Text>().text;
                    Require(shownAttack == attack && shownDefense == defense,
                        $"{values.parent.name} showed {shownAttack}/{shownDefense} during equipment, expected {attack}/{defense}.");
                }
                Require(FieldSlot(ui.Field, other).Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>().text == "1600",
                    "Equipment changed the readout of the other eligible target.");
                foreach (var detail in new[] { inspector, zoom })
                foreach (string kind in new[] { "Attack", "Defense" })
                {
                    var label = detail.Find($"{kind}/Change/Delta")?.GetComponent<Text>();
                    Require(label != null && label.text == delta &&
                        label.gameObject.activeInHierarchy == (delta.Length != 0),
                        $"{detail.parent.name} did not show the native equipment adjustment {delta} at the correct time.");
                }
            }
            static Action<float> BeginFrames(IEnumerator step)
            {
                Require(step.MoveNext() && step.Current is IEnumerator, "The expected equipment movement had no animation frames.");
                return TestCardPresentation.SampleAnimation((IEnumerator)step.Current);
            }

            LiveCardState equipped = null;
            duel.OnCardEquipped += (source, host, changes) =>
            {
                Require(equipped == null && source.code == unity && source.EquipTarget == (target.controller, target.sequence) &&
                    host.code == KaiserSeaHorse && host.sequence == target.sequence && changes?.Count == 1 &&
                    changes[0].Before.attack == 1700 && changes[0].After.attack == 3300 && changes[0].After.defense == 3250,
                    "The equipment packet did not carry the chosen target and exact native stat change.");
                equipped = source;
            };
            ActivateFromMainPhase(duel, unity);
            ReachIdle(KaiserSeaHorse);
            Require(equipped != null && own.szone[equipped.sequence].EquipTarget == (target.controller, target.sequence),
                "United We Stand did not retain its native equipment target.");
            ui.Field.Refresh();
            CheckValues("1700", "1650");

            var queued = steps.ToArray();
            var placement = queued.Single(step => FlightCode(step) == unity);
            var cue = queued.Single(step => step.GetType().Name.StartsWith("<PlayEquip>", StringComparison.Ordinal));
            Require(Array.IndexOf(queued, placement) < Array.IndexOf(queued, cue), "The equip link started before the card reached its zone.");
            BeginFrames(placement)(1f);
            Require(!placement.MoveNext(), "The equipment card did not finish its placement.");
            var equipmentArt = FieldSlot(ui.Field, equipped).GetComponentInChildren<CompositeCardView>();
            Require(equipmentArt?.CurrentCode == unity, "The equipment link started on an empty zone.");
            var sample = BeginFrames(cue);
            sample(.41f);
            CheckValues("1700", "1650");
            var graphic = animator.GetComponentsInChildren<DuelEffectGraphic>().Single(effect => effect.Kind == DuelEffectKind.EquipLink);
            var track = graphic.rectTransform;
            var targetArt = slot.GetComponentInChildren<CompositeCardView>();
            var left = track.TransformPoint(new Vector3(track.rect.xMin, 0f));
            var right = track.TransformPoint(new Vector3(track.rect.xMax, 0f));
            var sourceAnchor = (RectTransform)equipmentArt.transform.Find("AspectBox");
            var targetAnchor = (RectTransform)targetArt.transform.Find("AspectBox");
            Require(Vector3.Distance(left, sourceAnchor.TransformPoint(sourceAnchor.rect.center)) < .02f &&
                Vector3.Distance(right, targetAnchor.TransformPoint(targetAnchor.rect.center)) < .02f &&
                !graphic.raycastTarget, "The equip cue missed a card or blocked the board's input.");
            sample(DuelEffectGraphic.EquipContactProgress);
            CheckValues("3300", "3250", "+1600");
            Require(slot.Find("CardReadout/Combat/Attack/Change/Delta") == null,
                "The compact field added details-only text to the card.");
            sample(1f);
            Require(!cue.MoveNext() && graphic == null, "The equipment indicator stayed over the settled field.");
            steps.Clear();

            var equipmentSlot = FieldSlot(ui.Field, equipped);
            var equipmentTrigger = equipmentSlot.GetComponent<CardDragTrigger>();
            var targetTrigger = slot.GetComponent<CardDragTrigger>();
            var otherSlot = FieldSlot(ui.Field, other);
            void ExpectEquipmentFocus(bool highlighted)
            {
                Require(equipmentSlot.GetComponent<DuelZoneImage>().EquipmentHighlighted == highlighted &&
                    slot.GetComponent<DuelZoneImage>().EquipmentHighlighted == highlighted &&
                    !otherSlot.GetComponent<DuelZoneImage>().EquipmentHighlighted,
                    "The field did not identify exactly the active equipment relationship for the focused card.");
            }

            ui.Field.Root.transform.Find("Overlay_CardZoom/ZoomWin/Hdr/Close/B").GetComponent<Button>().onClick.Invoke();
            ExpectEquipmentFocus(false);
            targetTrigger.OnPointerEnterAction.Invoke();
            ExpectEquipmentFocus(true);
            targetTrigger.OnPointerExitAction.Invoke();
            ExpectEquipmentFocus(false);
            equipmentTrigger.OnPointerEnterAction.Invoke();
            ExpectEquipmentFocus(true);
            equipmentTrigger.OnPointerExitAction.Invoke();
            ExpectEquipmentFocus(false);
            targetTrigger.OnRightClick();
            ui.Field.Root.transform.Find("LeftInspector/CardDetails/ArtworkControls/Details/B").GetComponent<Button>().onClick.Invoke();

            CardMoveEvent removed = null;
            duel.OnCardMoved += move => { if (move.code == unity) removed = move; };
            ActivateFromMainPhase(duel, typhoon);
            ReachIdle(unity);
            Require(removed?.SourceReadout?.EquipTarget == (target.controller, target.sequence) &&
                own.grave.Any(card => card.code == unity) && own.mzone[target.sequence].attack == 1700,
                "The destroyed card lost its final public equipped state or its live bonus did not expire.");
            ui.Field.Refresh();
            CheckValues("3300", "3250", "+1600");
            Require(equipmentArt.gameObject.activeInHierarchy && equipmentArt.CurrentCode == unity,
                "The equip card vanished before its destruction cue.");
            queued = steps.ToArray();
            var leave = queued.Single(step => FlightCode(step) == unity);
            var update = queued.Single(step => step.GetType().Name.StartsWith("<ApplyFieldReadoutUpdate>", StringComparison.Ordinal));
            Require(Array.IndexOf(queued, update) == Array.IndexOf(queued, leave) + 1,
                "The lost equipment values were released outside the equipment departure.");
            Require(leave.MoveNext() && leave.Current is IEnumerator, "The destruction cue did not start.");
            var burst = (IEnumerator)leave.Current;
            Require(burst.MoveNext() && burst.Current is IEnumerator, "The destruction cue had no frames.");
            var impact = TestCardPresentation.SampleAnimation((IEnumerator)burst.Current);
            impact(.78f);
            CheckValues("3300", "3250", "+1600");
            Require(equipmentArt.gameObject.activeInHierarchy, "The spell was hidden while its destruction effect was still playing.");
            impact(1f);
            Require(!burst.MoveNext() && leave.MoveNext(), "The destroyed equipment did not enter its departure.");
            TestCardPresentation.SampleAnimation((IEnumerator)leave.Current)(1f);
            Require(!leave.MoveNext() && !update.MoveNext(), "The equipment departure and stat update did not finish.");
            CheckValues("1700", "1650");
            ExpectEquipmentFocus(false);
            Require(equipmentArt.CurrentCode != unity || !equipmentArt.gameObject.activeInHierarchy,
                "The destroyed Equip Spell remained visible on the field.");
            Debug.Log("[VERIFY DUEL EQUIP HANDOFF] United We Stand changed both stats at contact and restored them after its destruction.");
        }

        private static void VerifyOpponentEquipmentFocus()
        {
            const uint axe = 40619825, sparkman = 20721928;
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Shared equipment target", Enumerable.Repeat(axe, 40).ToArray(),
                empty, Enumerable.Repeat(sparkman, 40).ToArray(), empty, false,
                session => session.ActivePrompt?.playerId == session.AiPlayerIndex &&
                    session.ActivePrompt.summonable.Exists(card => card.code == sparkman));
            duel.SubmitResponseInt(0);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            var monster = duel.Players[duel.AiPlayerIndex].mzone.Single(card => card?.code == sparkman);
            duel.SubmitResponseInt(7);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            Require(duel.ActivePrompt?.playerId == duel.HumanPlayerIndex, "The opponent equipment fixture did not switch turns.");

            using var ui = new HandUI(duel, mobileLayout: true);
            ((RectTransform)ui.Canvas.transform).sizeDelta = new Vector2(1740f, 805f);
            for (int copy = 0; copy < 2; copy++)
            {
                ActivateFromMainPhase(duel, axe);
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD
                    ? SelectCard(prompt, sparkman) : null);
            }
            var equipment = duel.Players[duel.HumanPlayerIndex].szone.Where(card => card?.code == axe).ToArray();
            monster = duel.Players[duel.AiPlayerIndex].mzone[monster.sequence];
            Require(duel.RetryCount == 0 && monster.attack == 3600 && equipment.Length == 2 &&
                equipment.All(card => card.EquipTarget == (monster.controller, monster.sequence)),
                "The native duel could not equip both copies to the opponent's monster.");

            ui.Field.Refresh();
            var host = FieldSlot(ui.Field, monster);
            var first = FieldSlot(ui.Field, equipment[0]);
            var second = FieldSlot(ui.Field, equipment[1]);
            var board = ui.Field.Root.transform.Find("MobileSafeArea/DuelBoard");
            void Expect(params Transform[] expected)
            {
                var active = board.GetComponentsInChildren<DuelZoneImage>().Where(zone => zone.EquipmentHighlighted)
                    .Select(zone => zone.transform).ToHashSet();
                Require(active.SetEquals(expected), "The equipment highlight showed the wrong controller, copy or focus.");
            }

            Expect();
            host.GetComponent<CardDragTrigger>().OnPointerEnterAction.Invoke();
            Expect(host, first, second);
            host.GetComponent<CardDragTrigger>().OnPointerExitAction.Invoke();
            first.GetComponent<CardDragTrigger>().OnPointerEnterAction.Invoke();
            Expect(host, first);
            first.GetComponent<CardDragTrigger>().OnPointerExitAction.Invoke();
            Expect();

            var focus = typeof(DuelFieldUI).GetField("_duelPadFocus", PrivateInstance);
            typeof(DuelFieldUI).GetField("_usingDuelPad", PrivateInstance).SetValue(ui.Field, true);
            focus.SetValue(ui.Field, host);
            ui.Field.Refresh();
            Expect(host, first, second);
            focus.SetValue(ui.Field, second);
            ui.Field.Refresh();
            Expect(host, second);
            ui.Field.HandleDuelPad(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Alternate));
            ui.Field.Update();
            Require(ui.Field.IsReadingCardDetails, "The controller did not open the focused Equip Spell.");
            Expect();
            Debug.Log("[VERIFY DUEL EQUIPMENT FOCUS] Two native copies equipped to the opponent lit the right zones for pointer and controller focus.");
        }

        private static void VerifyNativeMonsterEquip()
        {
            const uint fusion = 1845204, restrict = 63519819, sparkman = 20721928;
            var main = Enumerable.Repeat(fusion, 12).Concat(Enumerable.Repeat(KaiserSeaHorse, 28)).ToArray();
            var opponent = Enumerable.Repeat(sparkman, 40).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Monster equipment", main, new[] { restrict }, opponent,
                Array.Empty<uint>(), false, session => session.Players[session.HumanPlayerIndex].hand.Exists(card => card.code == fusion));
            void ReachIdle(uint choose = 0)
            {
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => choose != 0 && prompt.messageType == YgoConstants.MSG_SELECT_CARD
                    ? SelectCard(prompt, choose) : null);
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    "The monster equipment fixture did not resolve its native action.");
            }
            duel.SubmitResponseInt(0);
            ReachIdle();
            duel.SubmitResponseInt(7);
            ReachIdle();
            ActivateFromMainPhase(duel, fusion);
            ReachIdle(restrict);

            var player = duel.Players[duel.HumanPlayerIndex];
            var target = player.mzone.Single(card => card?.code == restrict);
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            var field = FieldSlot(ui.Field, target);
            var attack = field.Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>();
            var defense = field.Find("CardReadout/Combat/Defense/Digits/Value").GetComponent<Text>();
            ActivateFromMainPhase(duel, restrict);
            ReachIdle(sparkman);
            var equipment = player.szone.Single(card => card?.code == sparkman);
            Require(equipment.EquipTarget == (target.controller, target.sequence) && (equipment.type & YgoConstants.TYPE_EQUIP) != 0 &&
                    player.mzone[target.sequence].attack == 1600 && player.mzone[target.sequence].defense == 1400,
                "The opponent's monster did not become the expected equipment in the native engine.");

            ui.Field.Refresh();
            Require(attack.text == "0" && defense.text == "0", "The absorbed values appeared while the monster was still traveling.");
            var queue = AnimationSteps(animator).ToArray();
            var placement = queue.Single(step => FlightCode(step) == sparkman);
            var cue = queue.Single(step => step.GetType().Name.StartsWith("<PlayEquip>", StringComparison.Ordinal));
            Require(Array.IndexOf(queue, placement) < Array.IndexOf(queue, cue) && placement.MoveNext(),
                "The absorbed monster did not travel from the opponent's field before becoming equipment.");
            TestCardPresentation.SampleAnimation((IEnumerator)placement.Current)(1f);
            Require(!placement.MoveNext() && cue.MoveNext(), "The absorbed monster did not reach its equipment cue.");
            var sample = TestCardPresentation.SampleAnimation((IEnumerator)cue.Current);
            sample(DuelEffectGraphic.EquipContactProgress);
            Require(attack.text == "1600" && defense.text == "1400", "The absorbed values did not reach the field at equipment contact.");
            sample(1f);
            Require(!cue.MoveNext(), "The absorbed monster left an equipment indicator on the field.");
            Debug.Log("[VERIFY DUEL EQUIP HANDOFF] Thousand-Eyes Restrict gained the other player's monster's 1600/1400 at contact.");
        }

        private static void VerifyNativeChainReadouts()
        {
            const uint gagagigo = 49003308, ocean = 295517, rush = 70046172;
            var main = Enumerable.Repeat(gagagigo, 16).Concat(Enumerable.Repeat(ocean, 12))
                .Concat(Enumerable.Repeat(rush, 12)).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Chain combat and level display", main, Array.Empty<uint>(),
                Enumerable.Repeat(gagagigo, 40).ToArray(), Array.Empty<uint>(), true, session =>
                {
                    YgoProjectBuilder.DriveUntilIdle(session, ai);
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return new[] { gagagigo, ocean, rush }.All(code => hand.Any(card => card.code == code));
                });
            for (int turn = 0; turn < 2; turn++)
            {
                Require(duel.ActivePrompt?.summonable.Count > 0 && duel.ActivePrompt.playerId == turn,
                    "The chain fixture could not summon the two copies.");
                duel.SubmitResponseInt(0);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
            }
            var human = duel.Players[duel.HumanPlayerIndex];
            var opponent = duel.Players[duel.AiPlayerIndex];
            var mine = human.mzone.Single(card => card?.code == gagagigo);
            var theirs = opponent.mzone.Single(card => card?.code == gagagigo);
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            var ownTile = FieldSlot(ui.Field, mine);
            var rivalTile = FieldSlot(ui.Field, theirs);
            ownTile.GetComponent<CardDragTrigger>().OnRightClick();

            string Value(Transform tile, string path) => tile.Find($"CardReadout/{path}").GetComponent<Text>().text;
            void Verify(string ownAttack, string rivalAttack, string defense, string level)
            {
                const string attackPath = "Combat/Attack/Digits/Value", defensePath = "Combat/Defense/Digits/Value",
                    levelPath = "Labels/Rating/Value";
                var inspector = ui.Field.Root.transform.Find("LeftInspector/CardDetails/CombatStats");
                Require(Value(ownTile, attackPath) == ownAttack && Value(rivalTile, attackPath) == rivalAttack &&
                        Value(ownTile, defensePath) == defense && Value(rivalTile, defensePath) == defense &&
                        Value(ownTile, levelPath) == level && Value(rivalTile, levelPath) == level &&
                        inspector.Find("Attack/Digits/Value").GetComponent<Text>().text == ownAttack,
                    $"The chain showed the wrong values for either copy. Expected ATK {ownAttack}/{rivalAttack}, DEF {defense}, Level {level}.");
            }

            bool chained = false;
            ActivateFromMainPhase(duel, ocean);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                if (prompt.messageType == YgoConstants.MSG_SELECT_CARD)
                {
                    int selected = prompt.candidates.FindIndex(card => card.code == gagagigo && card.controller == duel.HumanPlayerIndex);
                    Require(selected >= 0, "Rush Recklessly lost its own field target.");
                    return DuelSession.PackCardSelection(selected);
                }
                if (chained || prompt.playerId != duel.HumanPlayerIndex) return null;
                if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == rush)
                {
                    chained = true;
                    return BitConverter.GetBytes(1);
                }
                if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
                {
                    int index = prompt.candidates.FindIndex(card => card.code == rush);
                    if (index < 0) return null;
                    chained = true;
                    return BitConverter.GetBytes(index);
                }
                return null;
            });
            Require(chained && duel.RetryCount == 0 && duel.SolvedChainLinks.SequenceEqual(new[] { (2, rush), (1, ocean) }) &&
                    human.mzone[mine.sequence].attack == 2750 && opponent.mzone[theirs.sequence].attack == 2050 &&
                    human.mzone[mine.sequence].level == 3, "The native two-link sequence did not end with the expected stats.");
            ui.Field.Refresh();
            Verify("1850", "1850", "1000", "4");

            var steps = AnimationSteps(animator).Where(step =>
                step.GetType().Name.StartsWith("<ChainResolution>", StringComparison.Ordinal) ||
                step.GetType().Name.StartsWith("<ApplyFieldReadoutUpdate>", StringComparison.Ordinal)).ToArray();
            Require(steps.Length == 4, "Each resolved link must publish its own card values.");
            for (int index = 0; index < steps.Length; index += 2)
            {
                Require(steps[index].GetType().Name.StartsWith("<ChainResolution>", StringComparison.Ordinal) &&
                        steps[index + 1].GetType().Name.StartsWith("<ApplyFieldReadoutUpdate>", StringComparison.Ordinal),
                    "The chain queued its new values before showing the resolving card.");
                Require(steps[index].MoveNext() && steps[index].Current is IEnumerator, "A link did not have its own resolution animation.");
                var sample = TestCardPresentation.SampleAnimation((IEnumerator)steps[index].Current);
                sample(.66f);
                Verify(index == 0 ? "1850" : "2550", "1850", "1000", "4");
                sample(1f);
                Require(!steps[index].MoveNext() && !steps[index + 1].MoveNext(), "The resolved values added a separate delay.");
                if (index == 0) Verify("2550", "1850", "1000", "4");
            }
            Verify("2750", "2050", "1200", "3");
            Debug.Log("[VERIFY DUEL CHAIN READOUT] Rush changed only one copy to 2550 at link 2. Ocean changed both copies' ATK/DEF and Level at link 1.");
        }

        private static void VerifyNativeFieldTargetContinuity()
        {
            const uint compulsory = 94192409;
            var main = Enumerable.Repeat(KaiserSeaHorse, 24).Concat(Enumerable.Repeat(compulsory, 16)).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Field target and bounce", main, Array.Empty<uint>(),
                Enumerable.Repeat(BlueEyes, 40).ToArray(), Array.Empty<uint>(), true, session =>
                    session.ActivePrompt?.summonable.Exists(card => card.code == KaiserSeaHorse) == true &&
                    session.ActivePrompt.sSetable.Exists(card => card.code == compulsory));
            duel.SubmitResponseInt(duel.ActivePrompt.summonable.FindIndex(card => card.code == KaiserSeaHorse) << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            duel.SubmitResponseInt((duel.ActivePrompt.sSetable.FindIndex(card => card.code == compulsory) << 16) | 4);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            for (int turn = 0; turn < 2; turn++)
            {
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
            }
            ActivateFromMainPhase(duel, compulsory);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            var prompt = duel.ActivePrompt;
            Require(prompt?.messageType == YgoConstants.MSG_SELECT_CARD && prompt.candidates.Count == 1,
                "The native bounce fixture did not open a single-target field prompt.");

            var player = duel.Players[duel.HumanPlayerIndex];
            var target = player.mzone.Single(card => card?.code == KaiserSeaHorse);
            var source = player.szone.Single(card => card?.code == compulsory);
            using var ui = new HandUI(duel, mobileLayout: true, submitResponse: duel.SubmitResponse);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            var targetSlot = FieldSlot(ui.Field, target);
            var sourceSlot = FieldSlot(ui.Field, source);
            var targetView = targetSlot.Find("Card/CardView").GetComponent<CompositeCardView>();
            var sourceView = sourceSlot.Find("Card/CardView").GetComponent<CompositeCardView>();
            var attack = targetSlot.Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>();
            var targetAddress = (target.controller, target.location, target.sequence);
            var sourceAddress = (source.controller, source.location, source.sequence);
            int packets = 0;
            duel.OnCardsTargeted += (origin, targets) =>
            {
                if (origin == sourceAddress && targets.SequenceEqual(new[] { targetAddress })) packets++;
            };

            ui.Modals.ShowPrompt(duel, prompt);
            ui.Field.Refresh();
            var markers = ui.Field.Root.GetComponentsInChildren<DuelEffectGraphic>().Where(effect => effect.TargetChoice).ToArray();
            Require(markers.Length == 1 && markers[0].transform.IsChildOf(targetView.transform) &&
                !markers[0].raycastTarget && !markers[0].TargetLocked && targetView.gameObject.activeInHierarchy,
                "The field prompt marked an ineligible card or replaced its original view.");
            var originalPose = ((RectTransform)targetView.transform).anchoredPosition;
            targetSlot.GetComponent<CardDragTrigger>().OnTouchClick();
            Require(markers[0].TargetLocked && ((RectTransform)targetView.transform).anchoredPosition == originalPose &&
                targetView.gameObject.activeInHierarchy && attack.text == "1700",
                "Locking a target moved the field card or cleared its stats.");
            Require(ui.Modals.TryConfirm(), "The selected target could not be confirmed.");
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            ui.Modals.HideAll();
            ui.Field.Refresh();
            Require(packets == 1 && duel.RetryCount == 0 &&
                    player.hand.Any(card => card.code == KaiserSeaHorse) && player.mzone[target.sequence] == null,
                "The actual engine did not announce and return the target.");
            Require(targetView.gameObject.activeInHierarchy && sourceView.gameObject.activeInHierarchy &&
                    attack.gameObject.activeInHierarchy && attack.text == "1700" && !markers[0].gameObject.activeSelf,
                "The engine consumed the cards or their readouts before presentation caught up.");

            var steps = AnimationSteps(animator).ToArray();
            var locked = steps.Single(step => step.GetType().Name.StartsWith("<PlayTargetLocks>", StringComparison.Ordinal));
            var bounce = steps.Single(step => FlightCode(step) == KaiserSeaHorse);
            var leaveField = steps.Single(step => FlightCode(step) == compulsory);
            Require(Array.IndexOf(steps, locked) < Array.IndexOf(steps, bounce) && locked.MoveNext(),
                "The effect had no target presentation before departure.");
            var mark = TestCardPresentation.SampleAnimation((IEnumerator)locked.Current);
            mark(.55f);
            var drawn = animator.GetComponentsInChildren<DuelEffectGraphic>();
            Require(drawn.Count(effect => effect.Kind == DuelEffectKind.TargetReticle) == 1 &&
                    drawn.Count(effect => effect.Kind == DuelEffectKind.TargetBeam) == 1 &&
                    targetView.gameObject.activeInHierarchy && attack.text == "1700" && sourceView.gameObject.activeInHierarchy,
                "Target feedback hid a card or missed its beam.");
            var populate = typeof(DuelEffectGraphic).GetMethod("OnPopulateMesh", PrivateInstance | BindingFlags.DeclaredOnly);
            foreach (var effect in drawn.Where(effect => effect.Kind is DuelEffectKind.TargetReticle or DuelEffectKind.TargetBeam))
            {
                using var mesh = new VertexHelper();
                populate.Invoke(effect, new object[] { mesh });
                Require(mesh.currentVertCount > 24 && !effect.raycastTarget, "The target feedback did not draw visible, non-blocking geometry.");
            }
            mark(1f);
            Require(!locked.MoveNext() && !animator.GetComponentsInChildren<DuelEffectGraphic>()
                    .Any(effect => effect.Kind is DuelEffectKind.TargetReticle or DuelEffectKind.TargetBeam),
                "The confirmed target left its effect overlay active.");

            void VerifyDeparture(IEnumerator step, CompositeCardView fieldView, uint code)
            {
                var art = (RectTransform)fieldView.transform.Find("AspectBox");
                var departure = animator.GetVisualPose(art);
                Require(fieldView.gameObject.activeInHierarchy && departure.IsValid && step.MoveNext(),
                    "The card disappeared before its actual flight step.");
                var proxy = animator.GetComponentsInChildren<CompositeCardView>().Single(view =>
                    view.name == "MovingCard" && view.CurrentCode == code);
                Require(!fieldView.gameObject.activeInHierarchy &&
                        Vector2.Distance(((RectTransform)proxy.transform).anchoredPosition, departure.Position) < .1f,
                    "The source slot cleared before its moving card was in place.");
                TestCardPresentation.SampleAnimation((IEnumerator)step.Current)(1f);
                Require(!step.MoveNext() && !proxy.gameObject.activeSelf && !fieldView.gameObject.activeSelf,
                    "The source or moving view was left active after arrival.");
            }

            VerifyDeparture(bounce, targetView, KaiserSeaHorse);
            VerifyDeparture(leaveField, sourceView, compulsory);
            Debug.Log("[VERIFY DUEL TARGET CONTINUITY] Native candidate, stable lock and beam, bounce, and spell-zone departure passed.");
        }

        private static void VerifyOpponentActivationInspector()
        {
            const uint pot = 55144522, upstart = 70368879, ash = 14558127;
            var ai = new TacticalAI();
            var empty = Array.Empty<uint>();
            uint[] rival = Enumerable.Range(0, 40).Select(i => i % 2 == 0 ? pot : upstart).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Opponent activation inspector",
                Enumerable.Repeat(ash, 40).ToArray(), empty, rival, empty, false, session =>
                    session.Players[session.AiPlayerIndex].hand.Any(card => card.code == pot) &&
                    session.Players[session.AiPlayerIndex].hand.Any(card => card.code == upstart));
            using var ui = new HandUI(duel, mobileLayout: true);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            var inspector = ui.Field.Root.transform.Find("MobileSafeArea/LeftInspector");
            var preview = inspector.Find("CardPreview/Card").GetComponent<CompositeCardView>();
            var rules = inspector.Find("CardDetails/Description/Viewport/Content").GetComponent<Text>();
            ui.Field.InspectCard(BlueEyes);

            ActivateFromMainPhase(duel, pot);
            for (int i = 0; i < 12 && !IsAshResponse(duel.ActivePrompt); i++)
            {
                var pending = duel.ActivePrompt;
                if (pending == null) duel.StepUntilPrompt();
                else duel.SubmitResponse(ai.ComputeResponse(duel, pending));
            }
            var prompt = duel.ActivePrompt;
            Require(IsAshResponse(prompt) && preview.CurrentCode == BlueEyes,
                "The opponent spell did not create a response window or changed the inspector before its animation.");
            ShowActivation(pot);
            ui.Modals.ShowPrompt(duel, prompt);
            Require(preview.CurrentCode == pot && rules.text.Contains(CardDatabase.GetKoreanCardDescription(pot)),
                "The response picker replaced the opponent's description before the player inspected a choice.");
            var description = (RectTransform)inspector.Find("CardDetails/Description");
            Vector2 scrollPoint = RectTransformUtility.WorldToScreenPoint(null, description.TransformPoint(description.rect.center));
            Require(!ui.Modals.ModalRoot.GetComponentInChildren<DuelPromptBackdrop>().IsRaycastLocationValid(scrollPoint, null),
                "The response prompt prevented scrolling the opponent's description.");
            ui.Modals.ModalRoot.GetComponentsInChildren<Button>().First(button => button.name.StartsWith("Ch_", StringComparison.Ordinal))
                .onClick.Invoke();
            Require(preview.CurrentCode == ash, "The player could not manually inspect a response after the opponent's activation.");
            ui.Modals.HideAll();
            duel.SubmitResponseInt(-1);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            animator.Clear();
            ActivateFromMainPhase(duel, upstart);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            ShowActivation(upstart);
            ui.Field.InspectCard(upstart);
            Require(rules.text == CardDatabase.Get(upstart).desc && duel.RetryCount == 0,
                "Manual inspection retained a previous activation heading or the native duel retried.");
            Debug.Log("[VERIFY OPPONENT ACTIVATION INSPECTOR] Native opponent spells updated the sidebar in presentation order and preserved the response preview until explicit selection.");

            bool IsAshResponse(DuelPrompt pending) => pending?.messageType == YgoConstants.MSG_SELECT_CHAIN &&
                pending.playerId == duel.HumanPlayerIndex && pending.candidates.Any(candidate => candidate.code == ash);

            void ShowActivation(uint code)
            {
                var animation = AnimationSteps(animator).Single(step =>
                    step.GetType().Name.StartsWith("<ChainActivation>", StringComparison.Ordinal));
                Require(animation.MoveNext(), "The opponent's chain animation never began.");
                Require(preview.CurrentCode == code && rules.text.StartsWith("상대 발동 · 체인 1", StringComparison.Ordinal) &&
                    rules.text.Contains(CardDatabase.GetKoreanCardDescription(code)),
                    "The inspector did not show the opponent's active card and rules.");
                animator.Clear();
            }
        }

        private static void VerifyNativeChainCardFace(uint responseCode)
        {
            const uint breaker = 71413901, axe = 40619825, rush = 70046172, book = 14087893;
            var main = Enumerable.Repeat(breaker, 20).Concat(Enumerable.Repeat(axe, 10))
                .Concat(Enumerable.Repeat(responseCode == 0 ? breaker : responseCode, 10)).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Buffed chain card face", main, empty,
                Enumerable.Repeat(BlueEyes, 40).ToArray(), empty, true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return hand.Exists(card => card.code == breaker) && hand.Exists(card => card.code == axe) &&
                        (responseCode == 0 || hand.Exists(card => card.code == responseCode));
                });
            int summon = duel.ActivePrompt.summonable.FindIndex(card => card.code == breaker);
            Require(summon >= 0, "The chain face fixture could not summon Breaker.");
            duel.SubmitResponseInt(summon << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && prompt.forced ? BitConverter.GetBytes(0) : null);
            ActivateFromMainPhase(duel, axe);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                prompt.messageType == YgoConstants.MSG_SELECT_CARD ? SelectCard(prompt, breaker) : null);
            var player = duel.Players[duel.HumanPlayerIndex];
            Require(player.mzone.Single(card => card?.code == breaker).attack == 2900,
                "The native counter and Axe did not give Breaker its two bonuses.");

            using var ui = new HandUI(duel, mobileLayout: responseCode != 0);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            LiveCardState activation = null;
            LiveCardState resolution = null;
            duel.OnChainActivated += link => { if (link.code == breaker) activation = link.ActivationReadout; };
            duel.OnChainResolving += link => { if (link.code == breaker) resolution = link.ResolutionReadout; };
            bool responded = false;
            ActivateFromMainPhase(duel, breaker);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                if (prompt.messageType == YgoConstants.MSG_SELECT_CARD)
                    return SelectCard(prompt, responded ? breaker : axe);
                if (responded || responseCode == 0 || prompt.playerId != duel.HumanPlayerIndex) return null;
                if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == responseCode)
                {
                    responded = true;
                    return BitConverter.GetBytes(1);
                }
                if (prompt.messageType != YgoConstants.MSG_SELECT_CHAIN) return null;
                int response = prompt.candidates.FindIndex(card => card.code == responseCode);
                if (response < 0) return null;
                responded = true;
                return BitConverter.GetBytes(response);
            });
            var after = player.mzone.Single(card => card?.code == breaker);
            Require(duel.RetryCount == 0 && responded == (responseCode != 0) &&
                (responseCode == book ? !after.IsFaceUp : after.attack == (responseCode == rush ? 2300 : 1600)) &&
                player.grave.Any(card => card.code == axe), "Breaker did not pay the counter and destroy its own Axe.");
            Require(activation?.attack >= 2600, "The chain did not capture Breaker's equipped attack before resolution.");
            int resolvingAttack = responseCode == rush ? 3300 : responseCode == book ? activation.attack : 2600;
            Require(resolution?.attack == resolvingAttack,
                $"The engine snapshot before the first effect was {resolution?.attack}, with {resolvingAttack} ATK expected.");
            Require(responseCode != book || ReferenceEquals(activation, resolution),
                "A concealed source was queried for a new public chain face.");

            var steps = AnimationSteps(animator).ToArray();
            var activations = steps.Where(cue => cue.GetType().Name.StartsWith("<ChainActivation>", StringComparison.Ordinal)).ToArray();
            var resolutions = steps.Where(cue => cue.GetType().Name.StartsWith("<ChainResolution>", StringComparison.Ordinal)).ToArray();
            int linkCount = responseCode == 0 ? 1 : 2;
            Require(activations.Length == linkCount && resolutions.Length == linkCount, "The native chain lost an activation or resolution cue.");
            foreach (string phase in new[] { "ChainActivation", "ChainResolution" })
            {
                bool resolving = phase == "ChainResolution";
                var step = resolving ? resolutions[^1] : activations[0];
                if (resolving)
                    foreach (var update in steps.TakeWhile(cue => !ReferenceEquals(cue, step)).Where(cue =>
                        cue.GetType().Name.StartsWith("<ApplyFieldReadoutUpdate>", StringComparison.Ordinal)))
                        Require(!update.MoveNext(), "An earlier link's stat update added a separate animation.");
                Require(step.MoveNext() && step.Current is IEnumerator, $"The {phase} cue did not show its card.");
                var sample = TestCardPresentation.SampleAnimation((IEnumerator)step.Current);
                sample(.57f);
                var card = animator.GetComponentsInChildren<CompositeCardView>().Single(view => view.CurrentCode == breaker);
                bool compact = resolving && responseCode != 0;
                var face = compact ? animator.transform.Find("ChainResolution/ResolutionReadout") : card.transform.Find("AspectBox");
                string combatPath = compact ? "Combat" : "AtkDef";
                string ratingPath = compact ? "Rating" : "LevelInfo/Rating";
                var attack = face?.Find(combatPath + "/Attack/Digits/Value")?.GetComponent<Text>();
                var defense = face?.Find(combatPath + "/Defense/Digits/Value")?.GetComponent<Text>();
                var level = face?.Find(ratingPath + "/Number")?.GetComponent<Text>();
                int expectedAttack = resolving ? resolvingAttack : activation.attack;
                Require(attack?.text == expectedAttack.ToString() && attack.font == DuelTypography.NumberFont &&
                    defense?.text == "1000" && level?.text == "4",
                    $"The {phase} card showed {attack?.text}/{defense?.text}, level {level?.text}, with {expectedAttack} ATK expected.");
                if (resolving && responseCode == rush)
                {
                    var fieldAttack = FieldSlot(ui.Field, after).Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>();
                    Require(fieldAttack.text == attack.text, "The resolving card and presented field disagreed about the earlier buff.");
                }
                var counter = animator.GetComponentsInChildren<RectTransform>().Single(rect => rect.name == "ChainCount");
                var edge = counter.Find("NeonEdge").GetComponent<Image>();
                Require(counter.Find("Halo") != null && counter.Find("MetalInset") != null &&
                    edge.color == (resolving ? DuelStyle.ChainGold : DuelStyle.ChainCyan),
                    "The three chain medallion layers did not use the activation or resolution tone.");
                if (!resolving)
                {
                    sample(.21f);
                    Require(Mathf.Abs(counter.localScale.x - 1.60f) < .01f &&
                        Mathf.Abs(Mathf.DeltaAngle(card.transform.localEulerAngles.x, 0f)) > 1f &&
                        animator.transform.Find("Chain").GetComponent<DuelEffectGraphic>().Tint == DuelStyle.ChainGold,
                        "The activation missed the badge impact, card tilt or golden radial aura.");
                    var wave = animator.transform.Find("ChainImpactWave").GetComponent<DuelEffectGraphic>();
                    Require(wave.Tint == DuelStyle.ChainCyan && !wave.raycastTarget &&
                        wave.rectTransform.anchoredPosition == counter.anchoredPosition,
                        "The player's shockwave did not originate at the cyan badge.");
                    sample(.48f);
                    float initialWave = EffectExtent(wave);
                    sample(.88f);
                    Require(initialWave > 0f && EffectExtent(wave) > initialWave + 5f,
                        "The chain medallion's shockwave did not expand during the impact.");
                    sample(.98f);
                    Require(Mathf.Abs(counter.localScale.x - 1f) < .01f, "The chain badge did not settle at full size.");
                    sample(.57f);
                }
                else
                {
                    var outcome = animator.transform.Find("ChainResolution/ChainOutcome")?.GetComponentInChildren<DuelEffectGraphic>();
                    Require(outcome != null && !outcome.raycastTarget &&
                        outcome.Kind is DuelEffectKind.ChainApplied or DuelEffectKind.Break,
                        "The resolved native effect did not show a non-interactive outcome.");
                }
                if (compact)
                {
                    var playArea = (RectTransform)ui.Field.Root.transform.Find("MobileSafeArea");
                    var safeArea = playArea.GetComponent<MobileSafeArea>();
                    var metrics = (RectTransform)face;
                    Vector2 originalPosition = metrics.anchoredPosition;
                    Vector2 originalSize = metrics.sizeDelta;
                    var viewport = (RectTransform)ui.Canvas.transform;
                    var display = new Vector2Int(1334, 750);
                    viewport.sizeDelta = (Vector2)display;
                    safeArea.Reflow(new Rect(0f, 22f, 1180f, 728f), display);
                    Canvas.ForceUpdateCanvases();
                    sample(.68f);
                    Canvas.ForceUpdateCanvases();
                    Require(metrics.anchoredPosition != originalPosition && metrics.sizeDelta != originalSize,
                        "The mobile chain resolution retained its former position or size after reflow.");
                    foreach (var node in new[] { metrics, counter, (RectTransform)card.transform })
                    {
                        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(playArea, node);
                        Require(bounds.min.x >= playArea.rect.xMin && bounds.max.x <= playArea.rect.xMax &&
                            bounds.min.y >= playArea.rect.yMin && bounds.max.y <= playArea.rect.yMax,
                            "The mobile chain resolution intersected the device cutout after reflow.");
                    }
                    Require(attack.text == expectedAttack.ToString(), "The viewport change replaced the saved resolution value.");
                }
                Canvas.ForceUpdateCanvases();
                var counterBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(animator.transform, counter);
                foreach (string path in new[] { combatPath, ratingPath })
                {
                    var valueBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(animator.transform, face.Find(path));
                    Require(!counterBounds.Intersects(valueBounds), $"The {phase} chain count covered {path}.");
                }
                foreach (var child in animator.transform.Cast<Transform>().ToArray())
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            animator.Clear();
            Debug.Log($"[VERIFY DUEL CHAIN CARD FACE] Verified the native activation and resolution previews with response {responseCode}.");
        }

        private static float EffectExtent(DuelEffectGraphic effect)
        {
            Canvas.ForceUpdateCanvases();
            using var mesh = new VertexHelper();
            var populate = typeof(DuelEffectGraphic).GetMethod("OnPopulateMesh", PrivateInstance, types: new[] { typeof(VertexHelper) },
                binder: null, modifiers: null);
            populate.Invoke(effect, new object[] { mesh });
            var vertex = new UIVertex();
            float extent = 0f;
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                extent = Mathf.Max(extent, vertex.position.magnitude);
            }
            return extent;
        }

        private static void VerifyDestroyedCopiesKeepTheirReadouts()
        {
            const uint gagagigo = 49003308, darkHole = 53129443, rush = 70046172;
            var main = Enumerable.Repeat(gagagigo, 16).Concat(Enumerable.Repeat(darkHole, 12))
                .Concat(Enumerable.Repeat(rush, 12)).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Destroyed copies", main, Array.Empty<uint>(),
                Enumerable.Repeat(gagagigo, 40).ToArray(), Array.Empty<uint>(), true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return new[] { gagagigo, darkHole, rush }.All(code => hand.Any(card => card.code == code));
                });
            for (int turn = 0; turn < 4; turn++)
            {
                Require(duel.ActivePrompt?.playerId == (turn & 1), "The destruction fixture reached the wrong turn.");
                if (turn < 2 || turn == 3)
                {
                    int options = turn == 3 ? duel.ActivePrompt.mSetable.Count : duel.ActivePrompt.summonable.Count;
                    Require(options > 0, "The destruction fixture could not place a monster.");
                    duel.SubmitResponseInt(turn == 3 ? 3 : 0);
                    YgoProjectBuilder.DriveUntilIdle(duel, ai);
                }
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
            }

            var mine = duel.Players[duel.HumanPlayerIndex].mzone.Single(card => card?.code == gagagigo);
            var enemy = duel.Players[duel.AiPlayerIndex];
            var rival = enemy.mzone.Single(card => card?.code == gagagigo && card.IsFaceUp);
            var covered = enemy.mzone.Single(card => card != null && !card.IsFaceUp);
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            var ownTile = FieldSlot(ui.Field, mine);
            var rivalTile = FieldSlot(ui.Field, rival);
            var coveredTile = FieldSlot(ui.Field, covered);
            var coveredView = coveredTile.Find("Card/CardView").GetComponent<CompositeCardView>();
            ownTile.GetComponent<CardDragTrigger>().OnRightClick();
            var inspector = ui.Field.Root.transform.Find("LeftInspector/CardDetails/CombatStats/Attack/Digits/Value").GetComponent<Text>();

            void VerifyWaiting(Transform tile, string attack)
            {
                Require(tile.Find("Card/CardView").gameObject.activeInHierarchy &&
                        tile.Find("CardReadout").gameObject.activeInHierarchy &&
                        tile.Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>().text == attack,
                    $"A monster stopped displaying its {attack} ATK before its departure animation.");
            }

            bool chained = false;
            var destroyed = new List<CardMoveEvent>();
            var resolvedLinks = new List<ChainLinkInfo>();
            duel.OnChainResolved += resolvedLinks.Add;
            duel.OnCardMoved += move =>
            {
                if (move.fromLoc == YgoConstants.LOCATION_MZONE && (move.reason & YgoConstants.REASON_DESTROY) != 0)
                    destroyed.Add(move);
            };
            ActivateFromMainPhase(duel, darkHole);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                if (prompt.messageType == YgoConstants.MSG_SELECT_CARD)
                {
                    int target = prompt.candidates.FindIndex(card => card.code == gagagigo && card.controller == duel.HumanPlayerIndex);
                    Require(target >= 0, "Rush Recklessly could not target the intended copy.");
                    return DuelSession.PackCardSelection(target);
                }
                if (chained || prompt.playerId != duel.HumanPlayerIndex) return null;
                if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == rush)
                {
                    chained = true;
                    return BitConverter.GetBytes(1);
                }
                if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
                {
                    int option = prompt.candidates.FindIndex(card => card.code == rush);
                    if (option < 0) return null;
                    chained = true;
                    return BitConverter.GetBytes(option);
                }
                return null;
            });
            Require(resolvedLinks.Count == 2 && resolvedLinks.All(link => link.Resolved) &&
                !resolvedLinks[0].DestroyedCard && resolvedLinks[1].DestroyedCard,
                "Destruction was attributed to the wrong native chain link.");
            Require(chained && duel.RetryCount == 0 && duel.SolvedChainLinks.SequenceEqual(new[] { (2, rush), (1, darkHole) }) &&
                    duel.Players[duel.HumanPlayerIndex].grave.Count(card => card.code == gagagigo) == 1 &&
                    enemy.grave.Count(card => card.code == gagagigo) == 2 && destroyed.Count == 3 &&
                    destroyed[0].PresentationGroup != null &&
                    destroyed.All(move => ReferenceEquals(move.PresentationGroup, destroyed[0].PresentationGroup)),
                "The native chain did not destroy all three cards with one operation after applying the buff.");
            ui.Field.Refresh();
            VerifyWaiting(ownTile, "1850");
            VerifyWaiting(rivalTile, "1850");

            var steps = AnimationSteps(animator);
            var update = steps.Single(step => step.GetType().Name.StartsWith("<ApplyFieldReadoutUpdate>", StringComparison.Ordinal));
            Require(!update.MoveNext(), "The buff readout added a delay.");
            VerifyWaiting(ownTile, "2550");
            VerifyWaiting(rivalTile, "1850");
            Require(inspector.text == "2550", "The selected card lost its live inspector value before it left the field.");

            var departure = steps.Single(step => step.GetType().Name.StartsWith("<PlayFieldDepartures>", StringComparison.Ordinal));
            Require(departure.MoveNext(), "The destruction did not start with its impact.");
            Require(animator.GetComponentsInChildren<DuelEffectGraphic>().Count(effect => effect.Kind == DuelEffectKind.Break) == 3,
                "The single effect did not start all three impacts together.");
            var impact = TestCardPresentation.SampleAnimation((IEnumerator)departure.Current);
            impact(.62f);
            VerifyWaiting(ownTile, "2550");
            VerifyWaiting(rivalTile, "1850");
            Require(coveredView.gameObject.activeInHierarchy && coveredView.CurrentCode == 0 &&
                    coveredTile.Find("CardReadout") == null, "The covered card revealed its name or stats before the impact.");
            impact(1f);
            Require(departure.MoveNext(), "The destroyed cards did not leave together.");
            var sample = TestCardPresentation.SampleAnimation((IEnumerator)departure.Current);
            sample(.32f);
            var proxies = animator.GetComponentsInChildren<CompositeCardView>().Where(view => view.name == "MovingCard").ToArray();
            Require(proxies.Length == 3 && proxies.Count(view => view.CurrentCode == 0) == 1 &&
                    new[] { ownTile, rivalTile, coveredTile }.All(tile => !tile.Find("Card/CardView").gameObject.activeInHierarchy),
                "The three cards did not start their flights on the same frame with the set card concealed.");
            var readouts = Enumerable.Range(0, animator.transform.childCount).Select(index => animator.transform.GetChild(index))
                .OfType<RectTransform>().Where(rect => rect.name == "MovingCardReadout").ToArray();
            Require(readouts.Length == 2, "The moving group displayed live stats for a set card or lost a public readout.");
            foreach (var readout in readouts)
            {
                string attack = readout.Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>().text;
                RequireTravelStats(readout, attack, "1000", "4", defense: false);
                Require(proxies.Any(view => view.CurrentCode == gagagigo &&
                    ((RectTransform)view.transform).anchoredPosition == readout.anchoredPosition),
                    "A moving readout was detached from its copy.");
            }
            Require(readouts.Select(rect => rect.Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>().text)
                .OrderBy(value => value).SequenceEqual(new[] { "1850", "2550" }),
                "The grouped animation mixed the combat values of the same-named monsters.");
            sample(.62f);
            Require(proxies.All(view => view.CurrentCode == gagagigo), "The set card did not turn face-up on the way to the graveyard.");
            sample(1f);
            Require(!departure.MoveNext() && animator.transform.Find("MovingCardReadout") == null,
                "The departure left live readouts in a pile.");
            Require(inspector.text == "1850", "The inspector kept a field-only buff after the card left.");
            Debug.Log("[VERIFY DUEL GROUP DESTRUCTION] Three native Dark Hole targets departed together. The two public copies kept 2550 and 1850 ATK while the set card stayed covered until its flip.");
        }

        private static void VerifyFieldReplacementReadouts()
        {
            const uint gagagigo = 49003308, magePower = 83746708, umi = 22702055;
            var main = Enumerable.Repeat(gagagigo, 10).Concat(Enumerable.Repeat(magePower, 8))
                .Concat(Enumerable.Repeat(umi, 22)).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Field replacement combat values", main, Array.Empty<uint>(),
                Enumerable.Repeat(gagagigo, 40).ToArray(), Array.Empty<uint>(), true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return hand.Any(card => card.code == gagagigo) && hand.Any(card => card.code == magePower) &&
                        hand.Count(card => card.code == umi) >= 2;
                });
            int summon = duel.ActivePrompt.summonable.FindIndex(card => card.code == gagagigo);
            Require(summon >= 0, "Gagagigo was not available for the field replacement fixture.");
            duel.SubmitResponseInt(summon << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            ActivateFromMainPhase(duel, magePower);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                prompt.messageType == YgoConstants.MSG_SELECT_CARD ? DuelSession.PackCardSelection(0) : null);

            void SetFieldSpell()
            {
                int option = duel.ActivePrompt.sSetable.FindIndex(card => card.code == umi);
                Require(option >= 0, "Umi was not available to set in the Field Zone.");
                duel.SubmitResponseInt((option << 16) | 4);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    "Setting the Field Spell did not return to the main phase.");
            }

            SetFieldSpell();
            var own = duel.Players[duel.HumanPlayerIndex];
            var monster = own.mzone.Single(card => card?.code == gagagigo);
            Require(monster.attack == 2850 && monster.defense == 2000 && own.szone[5]?.code == umi,
                "Mage Power did not count the first Field Spell.");
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            var tile = FieldSlot(ui.Field, monster);
            tile.GetComponent<CardDragTrigger>().OnRightClick();
            var inspector = ui.Field.Root.transform.Find("LeftInspector/CardDetails/CombatStats");

            void Verify(string attack, string defense)
            {
                Require(tile.Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>().text == attack &&
                        tile.Find("CardReadout/Combat/Defense/Digits/Value").GetComponent<Text>().text == defense &&
                        inspector.Find("Attack/Digits/Value").GetComponent<Text>().text == attack &&
                        inspector.Find("Defense/Digits/Value").GetComponent<Text>().text == defense,
                    $"The field and inspector did not both show ATK {attack} and DEF {defense} during the replacement.");
            }

            SetFieldSpell();
            Require(own.grave.Count(card => card.code == umi) == 1 && own.szone[5]?.code == umi &&
                    own.mzone[monster.sequence].attack == 2850 && own.mzone[monster.sequence].defense == 2000,
                "The native set did not replace the first Field Spell and restore Mage Power's full increase.");
            ui.Field.Refresh();
            Verify("2850", "2000");

            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            bool IsReadoutUpdate(IEnumerator step) => step.GetType().Name.StartsWith("<ApplyFieldReadoutUpdate>", StringComparison.Ordinal);
            var steps = AnimationSteps(animator).Where(step => IsReadoutUpdate(step) ||
                step.GetType().GetField("flight", fields)?.GetValue(step) is CardFlight flight && flight.Code == umi).ToArray();
            Require(steps.Length == 4 && IsReadoutUpdate(steps[1]) && IsReadoutUpdate(steps[3]),
                "Replacing a Field Spell did not queue a stat update after each native move.");
            for (int index = 0; index < steps.Length; index += 2)
            {
                var move = steps[index];
                Require(move.MoveNext() && move.Current is IEnumerator, "The Field Spell did not enter its move animation.");
                var sample = TestCardPresentation.SampleAnimation((IEnumerator)move.Current);
                sample(.68f);
                if (index == 0) Verify("2850", "2000");
                else Verify("2350", "1500");
                sample(1f);
                while (move.MoveNext()) { }
                Require(!steps[index + 1].MoveNext(), "The stat update added a separate delay after the move.");
            }
            Verify("2850", "2000");
            Debug.Log("[VERIFY DUEL FIELD REPLACEMENT] Mage Power displayed 2850/2000, then 2350/1500 as the old Field Spell left, and restored 2850/2000 on the replacement.");
        }

        private static void VerifyTurnAndDrawReadouts()
        {
            const uint muka = 46657337, rush = 70046172;
            var main = Enumerable.Repeat(muka, 20).Concat(Enumerable.Repeat(rush, 20)).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Draw and temporary attack values", main, Array.Empty<uint>(),
                Enumerable.Repeat(BlueEyes, 40).ToArray(), Array.Empty<uint>(), true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return hand.Any(card => card.code == muka) && hand.Any(card => card.code == rush);
                });
            int normal = duel.ActivePrompt.summonable.FindIndex(card => card.code == muka);
            Require(normal >= 0, "Muka Muka could not be summoned.");
            duel.SubmitResponseInt(normal << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            ActivateFromMainPhase(duel, rush);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                prompt.messageType == YgoConstants.MSG_SELECT_CARD ? DuelSession.PackCardSelection(0) : null);
            var own = duel.Players[duel.HumanPlayerIndex];
            var original = own.mzone.Single(card => card?.code == muka);
            Require(original.attack == 2200 && original.defense == 1200, "Muka Muka did not receive the initial boosts.");

            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            var tile = FieldSlot(ui.Field, original);
            tile.GetComponent<CardDragTrigger>().OnRightClick();
            var combat = tile.Find("CardReadout/Combat");
            var attack = combat.Find("Attack/Digits/Value").GetComponent<Text>();
            var defense = combat.Find("Defense/Digits/Value").GetComponent<Text>();
            void Verify(string atk, string def)
            {
                Require(attack.text == atk && defense.text == def,
                    $"The duel displayed {attack.text}/{defense.text} instead of Muka Muka's {atk}/{def}.");
            }

            for (int turn = 0; turn < 2; turn++)
            {
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
            }
            Require(duel.TurnPlayer == duel.HumanPlayerIndex && duel.RetryCount == 0 &&
                    own.mzone[original.sequence].attack == 1800 && own.mzone[original.sequence].defense == 1500,
                "The native turn transition and draw did not produce the expected combat stats.");
            ui.Field.Refresh();
            Verify("2200", "1200");

            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var steps = AnimationSteps(animator).ToArray();
            var updates = steps.Where(step => step.GetType().Name.StartsWith("<ApplyFieldReadoutUpdate>", StringComparison.Ordinal)).ToArray();
            int ownDraw = Array.FindIndex(steps, step => step.GetType().GetField("flights", fields)?.GetValue(step) is
                IReadOnlyList<CardFlight> flights && flights.Any(flight => flight.Code != 0));
            Require(updates.Length == 2 && ownDraw >= 0 && ownDraw + 1 < steps.Length &&
                    Array.IndexOf(steps, updates[0]) < ownDraw && steps[ownDraw + 1] == updates[1],
                "The temporary boost and the draw did not each have a stat update at their own presentation boundary.");
            Require(!updates[0].MoveNext(), "Finishing the turn added a delay before expiring the boost.");
            Verify("1500", "1200");

            var draw = steps[ownDraw];
            Require(draw.MoveNext(), "The draw had no flying card.");
            Verify("1500", "1200");
            draw.GetType().GetFields(fields).Single(field => field.Name.StartsWith("<elapsed>", StringComparison.Ordinal))
                .SetValue(draw, 1f);
            Require(draw.MoveNext() && !draw.MoveNext(), "The drawn card did not reach the hand.");
            Require(!updates[1].MoveNext(), "Drawing added a delay before updating combat stats.");
            Verify("1800", "1500");
            Debug.Log("[VERIFY DUEL DRAW READOUT] Rush Recklessly expired at turn change. Muka Muka gained 300 ATK/DEF after the next draw.");
        }

        private static void VerifyDefensePositionBattleReadout(uint attacker, BattleStatSource source, int damage)
        {
            const uint sparkman = 20721928;
            var deck = Enumerable.Repeat(sparkman, 20).Concat(Enumerable.Repeat(attacker, 20)).ToArray();
            var empty = Array.Empty<uint>();
            using var duel = YgoProjectBuilder.StartSeedMatching("Defense-position attack", deck, empty,
                Enumerable.Repeat(sparkman, 40).ToArray(), empty, true,
                session => session.Players[session.HumanPlayerIndex].hand.Any(card => card.code == attacker) &&
                    session.ActivePrompt?.summonable.Any(card => card.code == sparkman) == true);
            using var ui = new HandUI(duel);
            var ai = new TacticalAI();
            var opponent = duel.Players[duel.AiPlayerIndex];
            int lifeBefore = opponent.lp;
            BattleComparisonValue? compared = null;
            duel.OnBattleResolved += (attack, _) => compared = attack.ComparedValue;

            byte[] Respond(DuelPrompt prompt)
            {
                if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == attacker)
                    return BitConverter.GetBytes(1);
                if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
                {
                    int index = prompt.candidates.FindIndex(card => card.code == attacker);
                    if (index >= 0) return BitConverter.GetBytes(index);
                }
                return prompt.messageType == YgoConstants.MSG_SELECT_TRIBUTE ? ai.ComputeResponse(duel, prompt) : null;
            }

            void Summon(uint code)
            {
                int index = duel.ActivePrompt.summonable.FindIndex(card => card.code == code);
                Require(index >= 0, $"The defense attack fixture could not summon {code}.");
                duel.SubmitResponseInt(index << 16);
                YgoProjectBuilder.DriveUntilIdle(duel, ai, Respond);
            }

            Summon(sparkman);
            for (int turn = 0; turn < 2; turn++)
            {
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
            }
            Summon(attacker);
            var card = duel.Players[duel.HumanPlayerIndex].mzone.Single(slot => slot?.code == attacker);
            Require(card.position == YgoConstants.POS_FACEUP_DEFENSE, "The attacker did not change to face-up defense.");

            var animation = CardAnimationManager.Ensure(ui.Canvas);
            duel.SubmitResponseInt(6);
            int attack = duel.ActivePrompt.attackable.FindIndex(candidate => candidate.code == attacker);
            Require(attack >= 0, "The native engine did not offer the defense-position attack.");
            duel.SubmitResponseInt((attack << 16) | 1);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, Respond);

            var comparison = animation.transform.Find("BattleReadout");
            var attackValue = comparison?.Find("Attacker/Values/Attack/Digits/Value").GetComponent<Text>();
            var defenseValue = comparison?.Find("Attacker/Values/Defense/Digits/Value").GetComponent<Text>();
            bool usesDefense = source == BattleStatSource.Defense;
            var activeValue = usesDefense ? defenseValue : attackValue;
            var inactiveValue = usesDefense ? attackValue : defenseValue;
            Require(opponent.lp == lifeBefore - damage && duel.RetryCount == 0 &&
                compared?.Source == source && compared?.Value == (uint)damage &&
                comparison != null && comparison.Find("Defender") == null &&
                comparison.Find("Attacker/Role/Text").GetComponent<Text>().text == "내 직접 공격" &&
                activeValue.gameObject.activeInHierarchy && activeValue.text == damage.ToString() &&
                !inactiveValue.gameObject.activeInHierarchy,
                "The defense-position comparison disagreed with the native stat source or LP result.");
            TestCardPresentation.VerifyAttackFlight(animation, card.attack.ToString(), card.defense.ToString(), card.level.ToString(), () =>
            {
                var values = animation.transform.Find("AttackingCardReadout/CardReadout/Combat");
                float attackRail = values.Find("Attack/Rail").GetComponent<Image>().color.a;
                float defenseRail = values.Find("Defense/Rail").GetComponent<Image>().color.a;
                Require((defenseRail > attackRail) == usesDefense, "The attacking card highlighted the wrong stat.");
            });
            Debug.Log($"[VERIFY DUEL BATTLE STATS] {attacker} attacked in defense using {source} {damage}, matching the LP result.");
        }

        private static void VerifyEffectModifiedBattleReadout()
        {
            const uint lamp = 42596828, sparkman = 20721928;
            const ulong useDefense = ((ulong)lamp << 20) | 1;
            var empty = Array.Empty<uint>();
            using var duel = YgoProjectBuilder.StartSeedMatching("Effect-modified combat",
                Enumerable.Repeat(lamp, 40).ToArray(), empty, Enumerable.Repeat(sparkman, 40).ToArray(), empty,
                true, session => session.ActivePrompt?.summonable.Count > 0);
            using var ui = new HandUI(duel);
            var ai = new TacticalAI();
            var animation = CardAnimationManager.Ensure(ui.Canvas);
            BattleCardStats attacker = default, defender = default;
            duel.OnBattleResolved += (source, target) => { attacker = source; defender = target; };
            var opponent = duel.Players[duel.AiPlayerIndex];
            int lifeBefore = opponent.lp;

            for (int turn = 0; turn < 2; turn++)
            {
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
                    duel.ActivePrompt.summonable.Count > 0, "The battle-stat effect fixture could not summon its monster.");
                duel.SubmitResponseInt(0);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
            }
            duel.SubmitResponseInt(6);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD &&
                duel.ActivePrompt.attackable.Count == 1,
                $"Dracoon Lamp could not enter combat at prompt {duel.ActivePrompt?.messageType}.");
            duel.SubmitResponseInt(1);
            Require(duel.ActivePrompt?.IsAttackTargetSelection == true && duel.ActivePrompt.candidates.Count == 1,
                "Dracoon Lamp could not select its target.");
            duel.SubmitResponse(DuelSession.PackCardSelection(0));

            bool activated = false;
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN)
                {
                    bool use = !activated && prompt.playerId == duel.HumanPlayerIndex &&
                        (prompt.promptDesc == useDefense || prompt.promptEffectDesc == useDefense);
                    activated |= use;
                    return BitConverter.GetBytes(use ? 1 : 0);
                }
                if (prompt.messageType != YgoConstants.MSG_SELECT_CHAIN || activated ||
                    prompt.playerId != duel.HumanPlayerIndex) return null;
                int index = prompt.candidates.FindIndex(card => card.code == lamp && card.description == useDefense);
                activated = index >= 0;
                return BitConverter.GetBytes(index);
            });
            Require(activated && attacker.Attack == 500 && defender.Attack == 1600 &&
                attacker.ComparedValue?.Source == BattleStatSource.Effect && attacker.ComparedValue?.Value == 2000 &&
                defender.ComparedValue?.Source == BattleStatSource.Effect && defender.ComparedValue?.Value == 1400 &&
                opponent.lp == lifeBefore - 600 && duel.RetryCount == 0,
                "The final battle values did not match Dracoon Lamp's resolved effect and native damage.");
            var comparison = animation.transform.Find("BattleReadout");
            foreach (var (side, value) in new[] { ("Attacker", "2000"), ("Defender", "1400") })
            {
                var values = comparison.Find(side + "/Values");
                Require(values.Find("Effect/Digits/Value").GetComponent<Text>().text == value &&
                    values.Find("Effect/Symbol").GetComponent<Image>().sprite == DuelStatSymbols.BattleEffect &&
                    !values.Find("Attack").gameObject.activeSelf && !values.Find("Defense").gameObject.activeSelf,
                    "A battle-stat effect was labeled with the unmodified stat.");
            }
            Debug.Log("[VERIFY DUEL BATTLE STATS] Dracoon Lamp compared 2000 and 1400 by effect while its printed attack values remained 500 and 1600.");
        }

        private static void VerifyNativeBattleDamageFeedback(bool reflected)
        {
            const uint spearDragon = 31553716, dimensionWall = 67095270, sparkman = 20721928;
            var empty = Array.Empty<uint>();
            using var duel = YgoProjectBuilder.StartSeedMatching("Battle damage feedback",
                Enumerable.Repeat(spearDragon, 40).ToArray(), empty,
                Enumerable.Repeat(reflected ? dimensionWall : sparkman, 40).ToArray(), empty, true,
                session => session.ActivePrompt?.summonable.Count > 0);
            var ai = new TacticalAI();
            duel.SubmitResponseInt(0);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            duel.SubmitResponseInt(7);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            var set = duel.ActivePrompt;
            Require(set?.playerId == duel.AiPlayerIndex &&
                (reflected ? set.sSetable.Any(card => card.code == dimensionWall) : set.mSetable.Any(card => card.code == sparkman)),
                "The opponent could not set the battle damage fixture.");
            duel.SubmitResponseInt(reflected ? 4 : 3);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            duel.SubmitResponseInt(7);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);

            using var ui = new HandUI(duel);
            var animation = CardAnimationManager.Ensure(ui.Canvas);
            var changes = new List<(byte Player, int Delta, int Lp, LifePointChangeKind Kind)>();
            duel.OnLifePointChange += (player, delta, lp, kind) => changes.Add((player, delta, lp, kind));
            (uint Source, uint TargetLocation, uint TargetSequence)? attack = null;
            duel.OnAttackDeclared += (_, _, sequence, _, targetLocation, targetSequence) =>
                attack = (sequence, targetLocation, targetSequence);
            duel.SubmitResponseInt(6);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD &&
                duel.ActivePrompt.attackable.Count == 1, "Spear Dragon could not start the native battle.");
            duel.SubmitResponseInt(1);
            if (!reflected)
            {
                Require(duel.ActivePrompt?.IsAttackTargetSelection == true &&
                    duel.ActivePrompt.candidates.Count == 1, "Spear Dragon could not target the set monster.");
                duel.SubmitResponse(DuelSession.PackCardSelection(0));
            }
            bool usedTrap = false;
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                if (!reflected || usedTrap || prompt.playerId != duel.AiPlayerIndex) return null;
                if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == dimensionWall)
                {
                    usedTrap = true;
                    return BitConverter.GetBytes(1);
                }
                if (prompt.messageType != YgoConstants.MSG_SELECT_CHAIN) return null;
                int index = prompt.candidates.FindIndex(card => card.code == dimensionWall);
                usedTrap = index >= 0;
                return BitConverter.GetBytes(index);
            });

            byte receiver = (byte)(reflected ? duel.HumanPlayerIndex : duel.AiPlayerIndex);
            int damage = reflected ? 1900 : 500;
            Require(usedTrap == reflected && duel.RetryCount == 0 && changes.Count == 1 &&
                changes[0] == (receiver, -damage, 8000 - damage, LifePointChangeKind.BattleDamage) &&
                duel.Players[receiver].lp == 8000 - damage,
                "The native recipient or amount of reflected or piercing damage was wrong.");

            var steps = AnimationSteps(animation);
            Require(attack.HasValue && (attack.Value.TargetLocation == 0) == reflected,
                "The native attack announced the wrong kind of target.");
            var cue = steps.Single(step => step.GetType().Name.StartsWith("<AttackArrow>", StringComparison.Ordinal));
            Require(cue.MoveNext() && cue.Current is IEnumerator, "The native declaration did not show a targeting line.");
            var sampleArrow = TestCardPresentation.SampleAnimation((IEnumerator)cue.Current);
            var arrow = animation.transform.Find("AttackPath").GetComponent<DuelEffectGraphic>();
            var fieldRoot = ui.Field.Root.transform;
            var source = (RectTransform)fieldRoot.Find($"DuelBoard/Zone_{attack.Value.Source + 2}_3");
            var destination = reflected
                ? (RectTransform)fieldRoot.Find("OpponentHUD/Status/Readout/Value/Text")
                : (RectTransform)fieldRoot.Find($"DuelBoard/Zone_{6 - attack.Value.TargetSequence}_1");
            Canvas.ForceUpdateCanvases();
            sampleArrow(.63f);
            var from = animation.GetPoint(source, Vector2.one * .5f);
            var to = animation.GetPoint(destination, Vector2.one * .5f);
            Require(Vector2.Distance(arrow.rectTransform.anchoredPosition, (from + to) * .5f) < .16f &&
                    Mathf.Abs(arrow.rectTransform.sizeDelta.x - Vector2.Distance(from, to)) < .16f &&
                    arrow.Tint.r > arrow.Tint.g && !arrow.raycastTarget,
                "The declared attack did not track the native target or the directly attacked opponent's LP.");
            Require(!cue.MoveNext(), "The attack declaration left its targeting visual active.");
            if (!reflected)
            {
                var flip = steps.First(step => step.GetType().Name.StartsWith("<MoveCard>", StringComparison.Ordinal));
                Require(flip.MoveNext() && !flip.MoveNext(), "The defender did not flip before the damage display.");
            }
            var hud = ui.Field.Root.transform.Find(reflected ? "PlayerHUD" : "OpponentHUD");
            var anchor = (RectTransform)hud.Find("Status/Readout/Name");
            var total = hud.Find("Status/Readout/Value/Text").GetComponent<Text>();
            var advanceLife = typeof(DuelFieldUI).GetMethod("AdvanceLifeReadouts", PrivateInstance);
            var board = (RectTransform)ui.Field.Root.transform.Find("DuelBoard");
            Vector2 boardRest = board.anchoredPosition;
            bool shook = false;
            bool verifiedRoll = false;
            TestCardPresentation.VerifyAttackFlight(animation, "1900", "0", "4", () =>
            {
                var popup = (RectTransform)animation.transform.Find("LPChange");
                if (animation.transform.Find("Strike") == null)
                {
                    advanceLife.Invoke(ui.Field, new object[] { .016f });
                    Require(popup == null && total.text == "8000", "The LP changed before the actual strike.");
                    return;
                }
                if (!verifiedRoll)
                {
                    advanceLife.Invoke(ui.Field, new object[] { .20f });
                    int longFrame = int.Parse(total.text);
                    Require(longFrame > 8000 - damage && longFrame < 8000, "The damage did not start a visible LP countdown.");
                    typeof(DuelFieldUI).GetField(reflected ? "_myDisplayLp" : "_oppDisplayLp", PrivateInstance)
                        .SetValue(ui.Field, 8000d);
                    for (int i = 0; i < 20; i++)
                        advanceLife.Invoke(ui.Field, new object[] { .01f });
                    Require(Math.Abs(int.Parse(total.text) - longFrame) <= 1,
                        "The LP countdown speed changed significantly with frame rate.");
                    verifiedRoll = true;
                }
                advanceLife.Invoke(ui.Field, new object[] { 1f });
                Require(popup != null && popup.anchoredPosition == animation.GetPoint(anchor, Vector2.one * .5f) &&
                    popup.Find("Kind/Text").GetComponent<Text>().text == "전투 피해" &&
                    popup.Find("Value/Text").GetComponent<Text>().text == $"-{damage}" && total.text == (8000 - damage).ToString(),
                    "The result showed damage on the wrong HUD, or at the wrong time in the attack.");
                shook |= (board.anchoredPosition - boardRest).sqrMagnitude > .1f;
                var sparks = popup.Find("LifeSparks").GetComponent<DuelEffectGraphic>();
                Require(sparks.Kind == DuelEffectKind.LifeSparks && sparks.Tint.r > sparks.Tint.g && !sparks.raycastTarget,
                    "Damage did not generate red particles on the correct HUD.");
            });
            Require(verifiedRoll && shook && (board.anchoredPosition - boardRest).sqrMagnitude < .0001f,
                "Damage did not roll the LP, shake the playing area and return it to rest.");
            Require(animation.transform.Find("LPChange") == null &&
                !steps.Any(step => step.GetType().Name.StartsWith("<PlayLifeChanges>", StringComparison.Ordinal)),
                "The same battle damage was queued again after contact.");
            animation.Clear();
            animation.QueueLifeChange(anchor, 900, LifePointChangeKind.Recovery, () => { });
            var recovery = steps.Dequeue();
            Require(recovery.MoveNext() && recovery.Current is IEnumerator, "Recovery did not open its HUD feedback.");
            TestCardPresentation.SampleAnimation((IEnumerator)recovery.Current)(.34f);
            var recoverySparks = animation.transform.Find("LPChange/LifeSparks").GetComponent<DuelEffectGraphic>();
            Require(recoverySparks.Tint.g > recoverySparks.Tint.r && !recovery.MoveNext() &&
                animation.transform.Find("LPChange") == null, "Recovery particles used the wrong color or stayed on screen.");
            Debug.Log($"[VERIFY DUEL LP FEEDBACK] Native {(reflected ? "reflected" : "piercing")} damage of {damage} reached seat {receiver} at the strike.");
        }

        private static void VerifyNativeCounterReadout()
        {
            const uint breaker = 71413901;
            const uint powerStone = 34029630;
            const uint predaplanning = 44536921;
            const uint scorpio = 35272499;
            const uint antiSpell = 53112492;
            const uint potOfGreed = 55144522;
            const string countPath = "CardReadout/Labels/Counters/Number/Value";
            var empty = Array.Empty<uint>();
            var main = Enumerable.Repeat(breaker, 10).Concat(Enumerable.Repeat(powerStone, 10))
                .Concat(Enumerable.Repeat(predaplanning, 9)).Concat(Enumerable.Repeat(antiSpell, 8))
                .Concat(Enumerable.Repeat(scorpio, 3)).ToArray();
            var opponent = Enumerable.Repeat(potOfGreed, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Live counter readout", main, empty, opponent, empty,
                true, session => session.ActivePrompt?.summonable.Exists(card => card.code == breaker) == true &&
                    new[] { powerStone, predaplanning, antiSpell }
                        .All(code => session.ActivePrompt.sSetable.Exists(card => card.code == code)));
            byte[] counterResponse = null;
            using var ui = new HandUI(duel, mobileLayout: true, submitResponse: bytes => counterResponse = bytes);
            var ai = new TacticalAI();
            var mine = duel.Players[duel.HumanPlayerIndex];

            void ReachMainPhase()
            {
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                {
                    if (prompt.messageType == YgoConstants.MSG_SELECT_CARD)
                    {
                        uint targetCode = prompt.candidates.Any(card => card.location == YgoConstants.LOCATION_DECK)
                            ? scorpio : powerStone;
                        int target = prompt.candidates.FindIndex(card => card.code == targetCode);
                        Require(target >= 0, $"The counter fixture could not select {targetCode}.");
                        return DuelSession.PackCardSelection(target);
                    }
                    return prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && prompt.forced ? BitConverter.GetBytes(0) : null;
                });
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    "The counter fixture could not reach the next native main phase.");
            }

            foreach (uint code in new[] { powerStone, predaplanning, antiSpell })
            {
                int setIndex = duel.ActivePrompt.sSetable.FindIndex(card => card.code == code);
                Require(setIndex >= 0, $"The counter fixture could not set {code}.");
                duel.SubmitResponseInt((setIndex << 16) | 4);
                ReachMainPhase();
            }
            duel.SubmitResponseInt(7);
            ReachMainPhase();
            Require(duel.ActivePrompt.playerId == duel.AiPlayerIndex, "The counter fixture did not switch turns.");
            duel.SubmitResponseInt(7);
            ReachMainPhase();

            void Activate(uint code)
            {
                int index = duel.ActivePrompt.activatable.FindIndex(card => card.code == code);
                Require(index >= 0, $"The counter fixture could not activate card {code}.");
                duel.SubmitResponseInt((index << 16) | 5);
                ReachMainPhase();
            }

            Activate(powerStone);
            var trap = mine.szone.Single(card => card?.code == powerStone);
            int summon = duel.ActivePrompt.summonable.FindIndex(card => card.code == breaker);
            Require(summon >= 0, "The counter fixture could not summon Breaker.");
            duel.SubmitResponseInt(summon << 16);
            ReachMainPhase();
            var monster = mine.mzone.Single(card => card?.code == breaker);
            ui.Field.Refresh();
            var fieldRoot = ui.Field.Root.transform;
            var root = fieldRoot.Find("MobileSafeArea");
            var trapSlot = root.Find($"DuelBoard/Zone_{trap.sequence + 2}_4");
            var monsterSlot = root.Find($"DuelBoard/Zone_{monster.sequence + 2}_3");
            var trapCount = trapSlot.Find(countPath)?.GetComponent<Text>();
            var monsterCount = monsterSlot.Find(countPath)?.GetComponent<Text>();
            Require(mine.szone[trap.sequence].counterCount == 3 && monster.counterCount == 1 && monster.attack == 1900 &&
                trapCount?.text == "3" && monsterCount?.text == "1" &&
                !trapSlot.Find("CardReadout/Combat").gameObject.activeSelf,
                $"The native trap and monster counters did not match their readouts ({mine.szone[trap.sequence].counterCount}, {monster.counterCount}).");

            trapSlot.GetComponent<CardDragTrigger>().OnRightClick();
            var inspector = root.Find("LeftInspector/CardPreview/Card/AspectBox");
            var label = root.Find("LeftInspector/CardDetails/ArtworkControls/Counters/Number/Value").GetComponent<Text>();
            Require(label.text == "마력 카운터 3개" && !label.raycastTarget, "Inspecting the trap lost its counter type or blocked input.");
            Require(!root.Find("LeftInspector/CardDetails/Stats/Rating").gameObject.activeSelf,
                "The plain trap acquired a monster rating.");
            monsterSlot.GetComponent<CardDragTrigger>().OnRightClick();
            inspector.Find("ZoomHit").GetComponent<Button>().onClick.Invoke();
            var zoomWindow = fieldRoot.Find("Overlay_CardZoom/MobileSafeArea/ZoomWin");
            var zoom = zoomWindow.Find("Info/Counters/Number/Value").GetComponent<Text>();
            Require(label.text == "마력 카운터 1개" && zoom.text == "마력 카운터 1개", "Inspecting Breaker did not update both detail views.");
            var inspectorLevel = root.Find("LeftInspector/CardDetails/Stats/Rating/Number")?.GetComponent<Text>();
            var zoomLevel = zoomWindow.Find("Info/Stats/Rating/Number")?.GetComponent<Text>();
            Require(inspectorLevel?.text == "4" && inspectorLevel.font == DuelTypography.NumberFont &&
                zoomLevel?.text == "4" && zoomLevel.font == DuelTypography.NumberFont &&
                zoomWindow.Find("Info/Stats/Rating/Kind/Label").GetComponent<Text>().text == "레벨",
                "The opened card views did not give the live level its own readout.");

            Activate(predaplanning);
            ui.Field.Refresh();
            var mixed = mine.mzone[monster.sequence];
            Require(mixed.Counters.AsSpan().SequenceEqual(new (ushort, ushort)[] { (1, 1), (0x1041, 1) }) &&
                mixed.level == 1 && monsterCount.text == "2",
                "Predaplanning did not put a second native counter type on Breaker.");
            var fieldLevel = monsterSlot.Find("CardReadout/Labels/Rating/Value").GetComponent<Text>();
            Require(new[] { fieldLevel, inspectorLevel, zoomLevel }.All(level => level.text == "1" &&
                    level.rectTransform.anchoredPosition.y > 0f) &&
                inspectorLevel.transform.parent.Find("ChangePulse")?.gameObject.activeInHierarchy == true &&
                zoomLevel.transform.parent.Find("ChangePulse")?.gameObject.activeInHierarchy == true,
                "The native level change did not reach and animate both open card views.");
            var breakdown = zoomWindow.Find("Info/DescBox/VP/C/CounterTypes")?.GetComponent<Text>();
            Require(label.text == "카운터 2종" && breakdown != null && breakdown.gameObject.activeInHierarchy &&
                breakdown.text == "마력 카운터 1개\n포식 카운터 1개",
                $"The native mixed counters were only shown as a total ({zoom.text}).");
            zoomWindow.Find("Hdr/Close/B").GetComponent<Button>().onClick.Invoke();

            duel.SubmitResponseInt(7);
            ReachMainPhase();
            Require(duel.ActivePrompt.playerId == duel.AiPlayerIndex, "The spell activation fixture did not switch turns.");
            int spell = duel.ActivePrompt.activatable.FindIndex(card => card.code == potOfGreed);
            Require(spell >= 0, "The opponent could not activate Pot of Greed.");
            duel.SubmitResponseInt((spell << 16) | 5);
            for (int step = 0; step < 16 && duel.ActivePrompt?.messageType != YgoConstants.MSG_SELECT_COUNTER; step++)
            {
                var pending = duel.ActivePrompt;
                if (pending == null) duel.StepUntilPrompt();
                else if (pending.messageType == YgoConstants.MSG_SELECT_PLACE) duel.SubmitResponse(ai.ComputeResponse(duel, pending));
                else if (pending.messageType == YgoConstants.MSG_SELECT_CHAIN)
                    duel.SubmitResponseInt(pending.candidates.FindIndex(card => card.code == antiSpell));
                else break;
            }
            var payment = duel.ActivePrompt;
            Require(payment?.messageType == YgoConstants.MSG_SELECT_COUNTER && payment.counterType == 1 &&
                payment.min == 2 && payment.candidates.Count == 2 && payment.playerId == duel.HumanPlayerIndex,
                "Anti-Spell did not ask which cards would pay its two Spell Counters.");
            int breakerIndex = payment.candidates.FindIndex(card => card.code == breaker && card.currentCounter == 1);
            int stoneIndex = payment.candidates.FindIndex(card => card.code == powerStone && card.currentCounter == 3);
            Require(breakerIndex >= 0 && stoneIndex >= 0, "The native payment included a different counter type.");

            ui.Modals.ShowPrompt(duel, payment);
            var window = ui.Modals.ModalRoot.transform.Find("Win");
            var tiles = window.Find("Body/Cards/Viewport/Content");
            var breakerTile = tiles.Find($"Cnt_{breakerIndex}");
            var stoneTile = tiles.Find($"Cnt_{stoneIndex}");
            var counterLabel = breakerTile.Find("Lbl/L").GetComponent<Text>();
            var confirm = window.Find("Footer/Btn_확정/B").GetComponent<Button>();
            Require(window.Find("Hdr/TitleBand/Title").GetComponent<Text>().text == "마력 카운터 제거" &&
                counterLabel.text == "0 / 1" &&
                breakerTile.Find("Card/V/AspectBox/CardReadout/Counters") == null && !confirm.interactable,
                "The chooser did not display the correct removable counter type and quantity.");
            var addBreaker = breakerTile.Find("P/B").GetComponent<Button>();
            addBreaker.onClick.Invoke();
            addBreaker.onClick.Invoke();
            Require(counterLabel.text == "1 / 1" && !confirm.interactable,
                "The chooser accepted a second counter of the wrong type.");
            breakerTile.Find("M/B").GetComponent<Button>().onClick.Invoke();
            var addStone = stoneTile.Find("P/B").GetComponent<Button>();
            addStone.onClick.Invoke();
            addStone.onClick.Invoke();
            addStone.onClick.Invoke();
            Require(confirm.interactable && stoneTile.Find("Lbl/L").GetComponent<Text>().text == "2 / 3",
                "The chooser did not cap allocation at the requested payment.");
            confirm.onClick.Invoke();
            Require(counterResponse?.Length == 4 && BitConverter.ToUInt16(counterResponse, breakerIndex * 2) == 0 &&
                BitConverter.ToUInt16(counterResponse, stoneIndex * 2) == 2,
                "The UI did not preserve native counter payment indices.");
            duel.SubmitResponse(counterResponse);
            ReachMainPhase();
            ui.Field.Refresh();
            Require(trapCount.text == "1" && monsterCount.text == "2",
                "Anti-Spell did not spend only the selected Spell Counters.");

            duel.SubmitResponseInt(7);
            ReachMainPhase();
            Activate(breaker);
            ui.Field.Refresh();
            monsterSlot.GetComponent<CardDragTrigger>().OnRightClick();
            inspector.Find("ZoomHit").GetComponent<Button>().onClick.Invoke();
            var remaining = mine.mzone[monster.sequence];
            Require(mine.szone[trap.sequence] == null && remaining.Counters.AsSpan().SequenceEqual(
                    new (ushort, ushort)[] { (0x1041, 1) }) && remaining.attack == 1600 && remaining.level == 1 &&
                !trapCount.gameObject.activeInHierarchy && monsterCount.text == "1" && !breakdown.gameObject.activeInHierarchy &&
                label.text == "포식 카운터 1개" && zoom.text == "포식 카운터 1개",
                "Spending Breaker's Spell Counter did not preserve the Predator Counter and its remaining level change.");
            Debug.Log("[VERIFY DUEL COUNTERS] Native mixed details, type-limited payment, and the remaining Predator Counter passed.");
        }

        private static void VerifyNativeIdentityReadout()
        {
            const uint zombieWorld = 4064256, lairOfDarkness = 59160188, mysticalSpaceTyphoon = 5318639;
            var empty = Array.Empty<uint>();
            uint[] required = { KaiserSeaHorse, zombieWorld, lairOfDarkness, mysticalSpaceTyphoon };
            var main = required.SelectMany(code => Enumerable.Repeat(code, 10)).ToArray();
            var opponent = Enumerable.Repeat(BlueEyes, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Live card identity", main, empty, opponent, empty,
                true, session => required.All(code => session.Players[session.HumanPlayerIndex].hand.Exists(card => card.code == code)));
            using var ui = new HandUI(duel);
            var ai = new TacticalAI();
            var mine = duel.Players[duel.HumanPlayerIndex];
            int summon = duel.ActivePrompt.summonable.FindIndex(card => card.code == KaiserSeaHorse);
            Require(summon >= 0, "The identity fixture could not summon Kaiser Sea Horse.");
            duel.SubmitResponseInt(summon << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            var monster = mine.mzone.Single(card => card?.code == KaiserSeaHorse);
            uint sequence = monster.sequence;
            ui.Field.Refresh();

            var root = ui.Field.Root.transform;
            root.Find($"DuelBoard/Zone_{sequence + 2}_3").GetComponent<CardDragTrigger>().OnRightClick();
            root.Find("LeftInspector/CardPreview/Card/AspectBox/ZoomHit").GetComponent<Button>().onClick.Invoke();
            var inspector = root.Find("LeftInspector/CardDetails");
            var zoom = root.Find("Overlay_CardZoom/ZoomWin/Info");
            var inspectorText = inspector.Find("Stats/Identity/Text").GetComponent<Text>();
            var zoomText = zoom.Find("Stats/S").GetComponent<Text>();
            var inspectorRace = inspector.Find("Race").GetComponent<Image>();
            var zoomRace = zoom.Find("RaceIc").GetComponent<Image>();
            var inspectorAttribute = inspector.Find("Attribute").GetComponent<Image>();
            var zoomAttribute = zoom.Find("AttributeIc").GetComponent<Image>();

            void ExpectShown(ulong race, uint attribute)
            {
                string identity = $"{YgoConstants.RaceNamesKR[race]} · {YgoConstants.AttributeNamesKR[attribute]}";
                Sprite raceSprite = LotdUiKit.GetRaceSprite(race);
                Sprite attrSprite = LotdUiKit.GetAttributeSprite(attribute);
                Require(inspectorText.text.Contains(identity) && zoomText.text.Contains(identity) &&
                        raceSprite != null && inspectorRace.sprite == raceSprite && zoomRace.sprite == raceSprite &&
                        attrSprite != null && inspectorAttribute.sprite == attrSprite && zoomAttribute.sprite == attrSprite &&
                        new[] { inspectorRace, zoomRace, inspectorAttribute, zoomAttribute }.All(icon => icon.gameObject.activeInHierarchy),
                    $"The open card views showed the wrong race or attribute for {identity}.");
                Canvas.ForceUpdateCanvases();
                var label = inspectorText.cachedTextGenerator;
                label.Populate(inspectorText.text, inspectorText.GetGenerationSettings(inspectorText.rectTransform.rect.size));
                Require(label.lineCount == 1 && label.characterCountVisible >= inspectorText.text.Length - 1 &&
                        label.fontSizeUsedForBestFit >= 12,
                    "The compact card identity left its words clipped or too small.");
            }

            void Activate(uint code, uint target = 0)
            {
                int index = duel.ActivePrompt.activatable.FindIndex(card => card.code == code);
                Require(index >= 0, $"The identity fixture could not activate card {code}.");
                duel.SubmitResponseInt((index << 16) | 5);
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                {
                    if (target == 0 || prompt.messageType != YgoConstants.MSG_SELECT_CARD) return null;
                    int choice = prompt.candidates.FindIndex(card => card.code == target);
                    Require(choice >= 0, "Mystical Space Typhoon could not target Lair of Darkness.");
                    return DuelSession.PackCardSelection(choice);
                });
                Require(duel.RetryCount == 0 && duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD,
                    "The native identity change did not finish.");
                ui.Field.Refresh();
            }

            ExpectShown(YgoConstants.RACE_SEASERPENT, YgoConstants.ATTRIBUTE_LIGHT);
            Activate(zombieWorld);
            Require(mine.mzone[sequence].race == YgoConstants.RACE_ZOMBIE, "Zombie World did not change the native race.");
            ExpectShown(YgoConstants.RACE_ZOMBIE, YgoConstants.ATTRIBUTE_LIGHT);
            Activate(lairOfDarkness);
            Require(mine.mzone[sequence].attribute == YgoConstants.ATTRIBUTE_DARK, "Lair of Darkness did not change the native attribute.");
            ExpectShown(YgoConstants.RACE_SEASERPENT, YgoConstants.ATTRIBUTE_DARK);
            Activate(mysticalSpaceTyphoon, lairOfDarkness);
            ExpectShown(YgoConstants.RACE_SEASERPENT, YgoConstants.ATTRIBUTE_LIGHT);

            monster = mine.mzone[sequence];
            monster.attribute |= YgoConstants.ATTRIBUTE_DARK;
            ui.Field.Refresh();
            Require(inspectorText.text.Contains("빛/어둠") && zoomText.text.Contains("빛/어둠") &&
                    !inspectorAttribute.gameObject.activeSelf && !zoomAttribute.gameObject.activeSelf,
                "A second attribute disappeared from the details or left a misleading single icon.");
            monster.race = YgoConstants.RACE_ZOMBIE;
            monster.position = YgoConstants.POS_FACEDOWN_DEFENSE;
            ui.Field.Refresh();
            ExpectShown(YgoConstants.RACE_SEASERPENT, YgoConstants.ATTRIBUTE_LIGHT);
            Debug.Log("[VERIFY DUEL IDENTITY] An inspected monster's race and attribute icons followed Zombie World and Lair of Darkness, then returned to normal. Multiple attributes and face-down visibility passed.");
        }

        private static void VerifyNativeCardName(bool mobileLayout)
        {
            const uint heroMask = 75141056, avian = 21844576;
            var empty = Array.Empty<uint>();
            var main = Enumerable.Repeat(KaiserSeaHorse, 20).Concat(Enumerable.Repeat(heroMask, 14))
                .Concat(Enumerable.Repeat(avian, 6)).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Live card name", main, empty,
                Enumerable.Repeat(BlueEyes, 40).ToArray(), empty, true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return hand.Any(card => card.code == KaiserSeaHorse) && hand.Any(card => card.code == heroMask);
                });
            int summon = duel.ActivePrompt.summonable.FindIndex(card => card.code == KaiserSeaHorse);
            Require(summon >= 0, "The name change fixture could not summon Kaiser Sea Horse.");
            duel.SubmitResponseInt(summon << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);

            var player = duel.Players[duel.HumanPlayerIndex];
            var monster = player.mzone.Single(card => card?.code == KaiserSeaHorse);
            using var ui = new HandUI(duel, mobileLayout);
            if (mobileLayout) ((RectTransform)ui.Canvas.transform).sizeDelta = new Vector2(1740f, 805f);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            var root = ui.Field.Root.transform;
            var playArea = root.Find("MobileSafeArea") ?? root;
            var slot = FieldSlot(ui.Field, monster);
            slot.GetComponent<CardDragTrigger>().OnRightClick();
            playArea.Find("LeftInspector/CardPreview/Card/AspectBox/ZoomHit").GetComponent<Button>().onClick.Invoke();
            var inspectorTitle = playArea.Find("LeftInspector/CardDetails/Name/Content");
            var zoom = root.Find("Overlay_CardZoom");
            var zoomInfo = (zoom.Find("MobileSafeArea") ?? zoom).Find("ZoomWin/Info");
            var zoomTitle = zoomInfo.Find("Name");

            ActivateFromMainPhase(duel, heroMask);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                if (prompt.messageType != YgoConstants.MSG_SELECT_CARD) return null;
                int index = prompt.candidates.FindIndex(card => card.code is KaiserSeaHorse or avian);
                Require(index >= 0, "Hero Mask lost the face-up target or the HERO in the deck.");
                return DuelSession.PackCardSelection(index);
            });
            var changed = player.mzone[monster.sequence];
            Require(duel.RetryCount == 0 && changed.code == KaiserSeaHorse && changed.alias == avian &&
                changed.attack == monster.attack && changed.defense == monster.defense && changed.level == monster.level,
                "The native fixture did not change only the rules name.");
            var steps = AnimationSteps(animator).ToArray();
            var changes = steps.Where(step =>
                step.GetType().Name.StartsWith("<ApplyFieldReadoutUpdate>", StringComparison.Ordinal)).ToArray();
            Require(changes.Length == 1, "The native name change did not queue a field readout update.");
            int resolving = Array.FindIndex(steps, step => step.GetType().Name.StartsWith("<ChainResolution>", StringComparison.Ordinal));
            Require(resolving >= 0 && Array.IndexOf(steps, changes[0]) > resolving,
                "The new name was presented ahead of its resolving chain link.");

            var inspectorName = inspectorTitle.Find("CurrentName")?.GetComponent<Text>();
            var zoomName = zoomTitle.Find("CurrentName")?.GetComponent<Text>();
            Require(inspectorName != null && zoomName != null, "The card details have no current-name label.");
            var printed = CardDatabase.Get(KaiserSeaHorse);
            string effective = $"현재 카드명 · {CardDatabase.GetName(avian)}";
            ui.Field.Refresh();
            Require(!inspectorName.gameObject.activeInHierarchy && !zoomName.gameObject.activeInHierarchy,
                "The changed name appeared before the effect resolved visually.");
            Require(!changes[0].MoveNext() && inspectorName.text == effective && zoomName.text == effective &&
                inspectorName.gameObject.activeInHierarchy && zoomName.gameObject.activeInHierarchy &&
                inspectorTitle.Find("Text").GetComponent<Text>().text == printed.name &&
                zoomTitle.Find("N").GetComponent<Text>().text == printed.name &&
                zoomInfo.Find("DescBox/VP/C/EffectText").GetComponent<Text>().text == printed.desc,
                "The card details did not display both the printed and rules names after resolution.");
            Canvas.ForceUpdateCanvases();
            foreach (var label in new[] { inspectorName, zoomName })
            {
                var generator = label.cachedTextGenerator;
                generator.Populate(label.text, label.GetGenerationSettings(label.rectTransform.rect.size));
                Require(generator.characterCountVisible >= label.text.Length && !label.raycastTarget,
                    $"The rules name was clipped or obstructed interaction at {ui.Canvas.GetComponent<RectTransform>().rect.size}.");
            }

            int queued = AnimationSteps(animator).Count;
            duel.SubmitResponseInt(7);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            Require(player.mzone[monster.sequence].alias == KaiserSeaHorse && duel.RetryCount == 0,
                "The rules name did not expire at the end of the turn.");
            var resets = AnimationSteps(animator).Skip(queued).Where(step =>
                step.GetType().Name.StartsWith("<ApplyFieldReadoutUpdate>", StringComparison.Ordinal)).ToArray();
            Require(resets.Length == 1 && !resets[0].MoveNext() &&
                !inspectorName.gameObject.activeInHierarchy && !zoomName.gameObject.activeInHierarchy,
                "The expired name stayed visible in card details.");

            const uint blueEyesPrinting = 89631135, legendaryOcean = 295517, umi = 22702055;
            var alternate = new LiveCardState { code = blueEyesPrinting, alias = BlueEyes, position = YgoConstants.POS_FACEUP_ATTACK };
            Require(new DuelCardReadout(CardDatabase.Get(blueEyesPrinting), alternate).RulesName == null,
                "Alternate artwork was mistaken for a changed rules name.");
            var ocean = new LiveCardState { code = legendaryOcean, alias = umi, position = YgoConstants.POS_FACEUP_ATTACK };
            Require(new DuelCardReadout(CardDatabase.Get(legendaryOcean), ocean).RulesName == CardDatabase.GetName(umi),
                "A distinct card treated as Umi was mistaken for another printing.");
            ocean.position = YgoConstants.POS_FACEDOWN_DEFENSE;
            Require(new DuelCardReadout(CardDatabase.Get(legendaryOcean), ocean).RulesName == null,
                "A face-down card exposed its rules name.");
            Debug.Log($"[VERIFY DUEL CARD NAME] Name-only effects, duration and visibility passed in {(mobileLayout ? "mobile" : "desktop")} layout.");
        }

        private static void VerifyInspectedCardMoves(bool swapInPlace, bool opponentSets = false)
        {
            const uint axe = 40619825, creatureSwap = 31036355, bookOfMoon = 14087893, burden = 44947065;
            uint[] main = Enumerable.Repeat(KaiserSeaHorse, 16).Concat(Enumerable.Repeat(burden, 6)).Concat(Enumerable.Repeat(axe, 6))
                .Concat(Enumerable.Repeat(creatureSwap, 6)).Concat(Enumerable.Repeat(bookOfMoon, 6)).ToArray();
            uint[] enemy = Enumerable.Repeat(KaiserSeaHorse, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Inspected card moves", main, Array.Empty<uint>(),
                enemy, Array.Empty<uint>(), true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return new[] { KaiserSeaHorse, axe, creatureSwap, bookOfMoon, burden }.All(code => hand.Any(card => card.code == code));
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
            Activate(burden);
            ReachIdle();
            duel.SubmitResponseInt(7);
            ReachIdle();
            duel.SubmitResponseInt(opponentSets ? 3 : 0);
            ReachIdle();
            duel.SubmitResponseInt(7);
            ReachIdle();
            uint origin = mine.mzone.Single(card => card != null).sequence;
            uint other = theirs.mzone.Single(card => card != null).sequence;
            uint destination = swapInPlace ? other : (other + 1) % 5;

            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            const string statPath = "Attack/Digits/Value";
            var inspector = ui.Field.Root.transform.Find($"LeftInspector/CardDetails/CombatStats/{statPath}").GetComponent<Text>();
            var zoom = ui.Field.Root.transform.Find($"Overlay_CardZoom/ZoomWin/Info/CombatStats/{statPath}").GetComponent<Text>();
            var source = ui.Field.Root.transform.Find($"DuelBoard/Zone_{origin + 2}_3");
            var otherSource = ui.Field.Root.transform.Find($"DuelBoard/Zone_{6 - other}_1");
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
            var pending = PendingMoveReadouts(ui.Field);
            var snapshot = pending[((byte)duel.AiPlayerIndex, YgoConstants.LOCATION_MZONE, destination)];
            pending.TryGetValue(((byte)duel.HumanPlayerIndex, YgoConstants.LOCATION_MZONE, origin), out var reciprocal);
            ui.Field.Refresh();
            Require(paired && snapshot.SourceCard.attack == 2700 && snapshot.DestinationCard.attack == 2300 &&
                (opponentSets ? reciprocal == null && mine.mzone[origin]?.position == YgoConstants.POS_FACEDOWN_DEFENSE :
                    reciprocal?.SourceCard.attack == 1300 && reciprocal.DestinationCard.attack == 1700) &&
                inspector.text == "2700" && zoom.text == "2700",
                "The exchange lost a card's original stats or exposed the new controller's modifier before departure.");
            Require(source.GetComponentInChildren<CompositeCardView>() != null &&
                otherSource.GetComponentInChildren<CompositeCardView>() != null,
                "The cards should remain in their original slots until the shared departure.");

            var step = AnimationSteps(animator).Single(move => move.GetType().Name.StartsWith("<PlayFieldSwap>", StringComparison.Ordinal));
            Require(step.MoveNext() && step.Current is IEnumerator, "The two native control changes did not enter one animation.");
            var sample = TestCardPresentation.SampleAnimation((IEnumerator)step.Current);
            sample(.5f);
            var readouts = animator.transform.Cast<RectTransform>().Where(rect => rect.name == "MovingCardReadout").ToArray();
            var proxies = animator.GetComponentsInChildren<CompositeCardView>().Where(card => card.name == "MovingCard").ToArray();
            Require(proxies.Length == 2 && readouts.Length == (opponentSets ? 1 : 2) &&
                proxies.Count(proxy => proxy.CurrentCode == 0) == (opponentSets ? 1 : 0) &&
                source.GetComponentInChildren<CompositeCardView>() == null && otherSource.GetComponentInChildren<CompositeCardView>() == null,
                "The field exchange showed a duplicate card, lost a moving card, or revealed the face-down card.");
            Require(readouts.All(readout => proxies.Count(proxy =>
                ((RectTransform)proxy.transform).anchoredPosition == readout.anchoredPosition) == 1),
                "The stat displays did not stay on their moving cards.");
            var outgoing = readouts.Single(readout => ReadAttack(readout) == "2700");
            var incoming = readouts.SingleOrDefault(readout => readout != outgoing);
            RequireTravelStats(outgoing, "2700", "1650", "4", defense: false);
            if (incoming != null)
            {
                RequireTravelStats(incoming, "1300", "1650", "4", defense: false);
                Require(!Bounds(outgoing).Overlaps(Bounds(incoming)), "The moving stat plates covered each other.");
            }
            Require(inspector.text == "2700" && zoom.text == "2700",
                "The selected copy's stats changed at the crossing.");
            sample(.92f);
            RequireTravelStats(outgoing, "2300", "1650", "4", defense: false);
            if (incoming != null) RequireTravelStats(incoming, "1700", "1650", "4", defense: false);
            Require(inspector.text == "2300" && zoom.text == "2300",
                "The inspector and detail view missed the selected card's new stats.");
            sample(1f);
            Require(!step.MoveNext() && animator.transform.Find("MovingCardReadout") == null,
                "The swap left a moving stat plate behind.");
            ui.Field.HandleDuelPad(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Cancel));
            var expected = ui.Field.Root.transform.Find($"DuelBoard/Zone_{6 - destination}_1");
            Require(!ui.Field.IsReadingCardDetails && expected != null && (Transform)focus.GetValue(ui.Field) == expected &&
                inspector.text == "2300", "Controller focus did not return to the same card after the control change.");

            ui.Field.HandleDuelPad(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Alternate));
            Activate(bookOfMoon);
            ReachIdle(prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD
                ? DuelSession.PackCardSelection(prompt.candidates.FindIndex(card =>
                    card.controller == duel.AiPlayerIndex && card.sequence == destination)) : null);
            ui.Field.Refresh();
            Require(theirs.mzone[destination]?.position == YgoConstants.POS_FACEDOWN_DEFENSE &&
                inspector.text == "2300" && zoom.text == "2300",
                "The selected copy lost its live numbers before turning face-down.");
            var conceal = OpenMove(animator, KaiserSeaHorse);
            conceal.Sample(.5f);
            Require(inspector.text == "1700" && zoom.text == "1700" && conceal.Proxy.CurrentCode == 0,
                "A face-down transfer kept the captured live stats after the turn.");
            conceal.Sample(1f);
            Require(!conceal.Step.MoveNext(), "The selected book target did not complete its position change.");
            Debug.Log($"[VERIFY DUEL CARD FOLLOW] Simultaneous swap in {(swapInPlace ? "the original" : "different")} zones. " +
                $"Face-down opponent: {opponentSets}. Controller focus and the 2700-to-2300 readout followed the selected card.");

            static string ReadAttack(RectTransform view) =>
                view.Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>().text;

            static Rect Bounds(RectTransform view) => new Rect(view.anchoredPosition - view.sizeDelta * .5f, view.sizeDelta);
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
            var pending = PendingMoveReadouts(ui.Field);
            Require(pending.Count == 1 && pending.Values.Single().Code == theseus,
                "The native Fusion Summon did not queue a readout for its own card.");
            var captured = pending.Single();
            var summon = AnimationSteps(animation).First(step => FlightCode(step) == theseus);
            void FinishSummon()
            {
                Require(summon.MoveNext() && summon.Current is IEnumerator, "The native arrival did not start.");
                var reveal = (IEnumerator)summon.Current;
                while (reveal.MoveNext()) TestCardPresentation.SampleAnimation((IEnumerator)reveal.Current)(1f);
                Require(!summon.MoveNext(), "The native arrival did not release its own reservation.");
            }

            if (turnFaceDown)
            {
                Activate(bookOfMoon);
                ui.Field.Refresh();
                var hidden = duel.Players[captured.Key.Controller].mzone[captured.Key.Sequence];
                Require(hidden.position == YgoConstants.POS_FACEDOWN_DEFENSE && pending.Count == 0 &&
                        captured.Value.DestinationCard == null,
                    "A summon preview read the live stats of a card that was already hidden when the field refreshed.");
                var queued = AnimationSteps(animation).ToArray();
                var flip = queued.Skip(Array.IndexOf(queued, summon) + 1).First(step =>
                    FlightCode(step) == theseus || step.GetType().Name.StartsWith("<PlayPositionChanges>", StringComparison.Ordinal));
                var card = FieldSlot(ui.Field, hidden).Find("Card/CardView").GetComponent<CompositeCardView>();
                Require(!card.gameObject.activeSelf, "The queued flip put the card in the slot before the summon arrived.");
                FinishSummon();
                Require(card.gameObject.activeInHierarchy && card.CurrentCode == theseus,
                    "The older reservation concealed the card between its summon and its next movement.");
                Require(flip.MoveNext() && flip.Current is IEnumerator && !card.gameObject.activeSelf,
                    "The queued position change did not take over the same slot.");
                TestCardPresentation.SampleAnimation((IEnumerator)flip.Current)(1f);
                Require(!flip.MoveNext() && card.gameObject.activeInHierarchy && card.CurrentCode == 0,
                    "The position change did not leave the slot with its private card back.");
                Debug.Log("[VERIFY DUEL SUMMON STATS] Queued summon and concealment kept their distinct field handoffs without showing private stats.");
                return;
            }

            ui.Field.Refresh();
            var stats = captured.Value.DestinationCard;
            Require(pending.Count == 0 && stats?.attack == 2400 && stats.defense == 2000 && stats.level == 4,
                "The summon readout missed a continuous effect applied before presentation.");
            var pose = new CardPose(Vector2.zero, new Vector2(160, 230));
            var flight = new CardFlight(theseus, pose, pose, true, false);
            var (plate, _) = animation.SummonPlate(flight, "융합 소환", Vector2.zero, stats);
            Require(plate.transform.Find("Caption/Value").GetComponent<Text>().text == "융합 소환" &&
                    plate.transform.Find("Caption/Rating/Number")?.GetComponent<Text>().text == "4" &&
                    plate.transform.Find("SummonStats/Attack/Digits/Value").GetComponent<Text>().text == "2400" &&
                    plate.transform.Find("SummonStats/Defense/Digits/Value").GetComponent<Text>().text == "2000",
                "The Fusion Summon banner and the queued live readout disagreed.");
            var proxy = CompositeCardView.Create(ui.Canvas.transform, "Summoned Card", detailedOverlay: true);
            proxy.SetCard(0, faceDown: true);
            CardAnimationManager.ApplySummonEntrance(proxy, flight, DuelEffectKind.Fusion, .25f, stats);
            Require(proxy.CurrentCode == 0 && proxy.transform.Find("AspectBox/CardBack").gameObject.activeSelf,
                "The moving card exposed its face before the reveal.");
            CardAnimationManager.ApplySummonEntrance(proxy, flight, DuelEffectKind.Fusion, .75f, stats);
            Require(proxy.transform.Find("AspectBox/AtkDef/Attack/Digits/Value")?.GetComponent<Text>().text == "2400" &&
                    proxy.transform.Find("AspectBox/AtkDef/Defense/Digits/Value")?.GetComponent<Text>().text == "2000" &&
                    proxy.transform.Find("AspectBox/LevelInfo/Rating/Number")?.GetComponent<Text>().text == "4",
                "The revealed card face did not keep the same numbers as the summon banner.");

            var (controller, _, sequence) = captured.Key;
            FinishSummon();
            ui.Field.Refresh();
            var fieldSlot = typeof(DuelFieldUI).GetMethod("GetSlot", PrivateInstance).Invoke(ui.Field,
                new object[] { controller, YgoConstants.LOCATION_MZONE, sequence });
            var slot = (Transform)fieldSlot.GetType().GetField("root").GetValue(fieldSlot);
            Require(slot.Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>().text == "2400" &&
                    slot.Find("CardReadout/Combat/Defense/Digits/Value").GetComponent<Text>().text == "2000" &&
                    slot.Find("CardReadout/Labels/Rating/Value").GetComponent<Text>().text == "4" &&
                    slot.Find("CardReadout/Labels/Rating/Symbol").GetComponent<Image>().sprite == DuelStatSymbols.Rating(DuelRatingKind.Level),
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
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            duel.SubmitResponseInt((summon << 16) | 1);
            ReachIdle();
            var own = duel.Players[duel.HumanPlayerIndex];
            var live = own.mzone.Single(card => card?.code == linkSpider);
            Require(live.sequence is 5 or 6 && live.linkMarker == 0x002, "The native Link Spider did not expose its downward arrow.");
            uint target = live.sequence == 5 ? 1u : 3u;

            var rayStep = AnimationSteps(animator).Single(step => step.GetType().Name.StartsWith("<FieldRays>", StringComparison.Ordinal));
            Require(rayStep.MoveNext() && rayStep.Current is IEnumerator, "A native Link Summon did not queue its arrow beam.");
            TestCardPresentation.SampleAnimation((IEnumerator)rayStep.Current)(.64f);
            var ray = (RectTransform)animator.transform.Find("LinkRay");
            var board = ui.Field.Root.transform.Find("DuelBoard");
            var origin = animator.GetPoint((RectTransform)board.Find($"Zone_{target + 2}_2"), Vector2.one * .5f);
            var destination = animator.GetPoint((RectTransform)board.Find($"Zone_{target + 2}_3"), Vector2.one * .5f);
            Require(Vector2.Distance(ray.anchoredPosition, (origin + destination) * .5f) < .1f &&
                Mathf.Abs(ray.sizeDelta.x - (origin - destination).magnitude) < .1f,
                "The Link Spider beam did not point at the exact marker destination.");
            animator.Clear();
            ui.Field.BindSession(duel, null, null);
            var linkSlot = ui.Field.Root.transform.Find($"DuelBoard/Zone_{target + 2}_2");
            var links = linkSlot.Find("Card/CardView/AspectBox/LinkBeams").GetComponent<DuelEffectGraphic>();
            Require(links.isActiveAndEnabled && links.DirectionMask == live.linkMarker && !links.raycastTarget,
                "A public Link Monster did not keep its live markers lit.");
            linkSlot.GetComponent<CardDragTrigger>().OnRightClick();
            ui.ExpectLinkedZones($"Zone_{target + 2}_3");
            int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == linkSpider);
            Require(activate >= 0, "Link Spider did not offer its linked-zone summon effect.");
            duel.SubmitResponseInt((activate << 16) | 5);
            ReachIdle();
            animator.Clear();
            ui.Field.Refresh();
            Require(own.mzone[target]?.code == mysticalElf && own.mzone.Count(card => card?.code == mysticalElf) == 1,
                "The native effect summoned to a different zone than the selected Link arrow indicated.");
            ui.ExpectLinkedZones($"Zone_{target + 2}_3");
            Debug.Log("[VERIFY DUEL LINK ZONES] Link Spider's live downward marker identified the zone used by its native effect.");
        }

        private static void VerifyNativeNegationOutcome(bool activationNegated)
        {
            const uint judgment = 41420027;
            uint response = activationNegated ? judgment : AshBlossom;
            var player = Enumerable.Repeat(PotOfDesires, 21).Concat(Enumerable.Repeat(BlueEyes, 19)).ToArray();
            var enemy = Enumerable.Repeat(response, 40).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Native negation presentation", player, empty, enemy, empty,
                !activationNegated, session => session.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
                    session.Players[session.HumanPlayerIndex].hand.Count(card => card.code == PotOfDesires) >= 2);
            if (activationNegated)
            {
                int set = duel.ActivePrompt.sSetable.FindIndex(card => card.code == judgment);
                Require(set >= 0, "Solemn Judgment could not be set in the native fixture.");
                duel.SubmitResponseInt((set << 16) | 4);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
            }
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            var resolved = new List<ChainLinkInfo>();
            duel.OnChainResolved += resolved.Add;
            int spell = duel.ActivePrompt.activatable.FindIndex(card => card.code == PotOfDesires);
            Require(spell >= 0, "The native fixture could not activate Pot of Desires.");
            duel.SubmitResponseInt((spell << 16) | 5);
            bool sent = false;
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                if (sent || prompt.playerId != duel.AiPlayerIndex || prompt.messageType != YgoConstants.MSG_SELECT_CHAIN) return null;
                int index = prompt.candidates.FindIndex(card => card.code == response);
                if (index < 0) return null;
                sent = true;
                return BitConverter.GetBytes(index);
            });
            Require(sent && duel.RetryCount == 0 && resolved.Select(link => link.chainNumber).SequenceEqual(new[] { 2, 1 }),
                "Native negation did not publish each outcome in reverse chain order.");
            var negated = resolved[1];
            Require(negated.code == PotOfDesires && negated.negated && negated.ActivationNegated == activationNegated,
                "The chain did not distinguish effect negation from activation negation.");
            Require(negated.IsCardActivation && negated.HasOncePerTurnActivationLimit &&
                resolved[0].IsCardActivation == activationNegated, "Native activation flags did not distinguish a spell, Counter Trap and monster effect.");
            if (!activationNegated)
                Require(duel.HasUsedSharedTurnEffect((byte)duel.AiPlayerIndex, AshBlossom),
                    "The native hand effect was not recorded for once-per-turn feedback.");

            var opposingCutIn = AnimationSteps(animator).Last(step =>
                step.GetType().Name.StartsWith("<ChainActivation>", StringComparison.Ordinal));
            Require(opposingCutIn.MoveNext() && opposingCutIn.Current is IEnumerator, "The opposing activation did not appear.");
            TestCardPresentation.SampleAnimation((IEnumerator)opposingCutIn.Current)(.67f);
            var wave = animator.transform.Find("ChainImpactWave").GetComponent<DuelEffectGraphic>();
            var edge = animator.transform.Find("ChainCount/NeonEdge").GetComponent<Image>();
            Require(wave.Tint == DuelStyle.ChainRed && edge.color == DuelStyle.ChainRed && EffectExtent(wave) > 0f,
                "The opposing activation did not use a crimson badge and wave.");
            while (opposingCutIn.MoveNext()) { }

            var outcome = AnimationSteps(animator).Last(step => step.GetType().Name.StartsWith("<ChainOutcome>", StringComparison.Ordinal));
            Require(outcome.MoveNext() && outcome.Current is IEnumerator, "The negated link did not get a separate outcome.");
            var sampleOutcome = TestCardPresentation.SampleAnimation((IEnumerator)outcome.Current);
            sampleOutcome(.52f);
            var result = animator.transform.Find("ChainOutcome");
            var impact = result.GetComponentInChildren<DuelEffectGraphic>();
            Require(impact.Kind == (activationNegated ? DuelEffectKind.ChainShatter : DuelEffectKind.ChainNegated) &&
                result.Find("Status/Text").GetComponent<Text>().text == "무효 · NEGATED", "The wrong negation animation or stamp appeared.");
            Require(activationNegated
                    ? negated.NegatedGraveController == duel.HumanPlayerIndex && impact.ShardTravel.HasValue
                    : negated.NegatedGraveController == null && !impact.ShardTravel.HasValue,
                "The red shards did not follow a confirmed grave destination or invented a grave movement.");
            if (activationNegated)
            {
                sampleOutcome(.87f);
                Canvas.ForceUpdateCanvases();
                using var mesh = new VertexHelper();
                var populate = typeof(DuelEffectGraphic).GetMethod("OnPopulateMesh", PrivateInstance, types: new[] { typeof(VertexHelper) },
                    binder: null, modifiers: null);
                populate.Invoke(impact, new object[] { mesh });
                Require(mesh.currentVertCount > 0, "The shards disappeared before reaching the grave.");
                var vertex = default(UIVertex);
                Vector2 destination = impact.rectTransform.rect.center + impact.ShardTravel.Value;
                for (int i = 0; i < mesh.currentVertCount; i++)
                {
                    mesh.PopulateUIVertex(ref vertex, i);
                    Require(vertex.color.a > 100 && Vector2.Distance(vertex.position, destination) < impact.rectTransform.rect.width * .10f,
                        "A shard faded early or overshot the grave.");
                }
            }
            Require(!outcome.MoveNext(), "The negation animation did not release its transient objects.");

            byte human = (byte)duel.HumanPlayerIndex;
            var prompt = duel.ActivePrompt;
            Require(prompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
                prompt.activatable.Any(card => card.code == PotOfDesires) == activationNegated &&
                duel.HasUsedSharedTurnActivation(human, PotOfDesires) != activationNegated,
                "The once-per-turn notice disagreed with the native Oath limit after negation.");
            if (!activationNegated)
            {
                animator.Clear();
                ui.Field.Refresh();
                var copy = prompt.sSetable.First(card => card.code == PotOfDesires);
                var hand = ui.Field.Root.transform.Find("PlayerHand");
                var trigger = hand.GetChild((int)copy.sequence).GetComponent<CardDragTrigger>();
                trigger.OnTouchClick();
                var notice = ui.Field.Root.transform.Find("ActivationNotice");
                var actions = ui.Field.Root.transform.Find("CardActions");
                Require(notice.gameObject.activeInHierarchy && notice.Find("Reason").GetComponent<Text>().text ==
                        "이 턴에는 이 카드명으로 더 이상 발동할 수 없습니다." &&
                        actions.gameObject.activeInHierarchy && actions.Find("SET/Circle/Button/Icon") != null &&
                        actions.Find("ACTION") == null && ReferenceEquals(duel.ActivePrompt, prompt),
                    "The turn limit notice hid the legal Set or offered an illegal activation.");
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                Require(!duel.HasUsedSharedTurnActivation(human, PotOfDesires), "The turn activation limit notice survived the turn change.");
            }
            Debug.Log($"[VERIFY DUEL NEGATION] Native {(activationNegated ? "activation" : "effect")} negation resolved CL2 before CL1 with the matching impact.");
        }

        private static void VerifyNativeImpermanenceRay(bool setFirst)
        {
            const uint impermanence = 10045474;
            uint[] player = Enumerable.Repeat(impermanence, 40).ToArray();
            uint[] opponent = Enumerable.Repeat(KaiserSeaHorse, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Impermanence column ray", player, Array.Empty<uint>(),
                opponent, Array.Empty<uint>(), setFirst, session => session.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD);
            var ai = new TacticalAI();
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            if (setFirst)
            {
                int set = duel.ActivePrompt.sSetable.FindIndex(item => item.code == impermanence);
                Require(set >= 0, "The fixture must be able to set Infinite Impermanence.");
                duel.SubmitResponseInt((set << 16) | 4);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
            }
            var idle = duel.ActivePrompt;
            Require(idle?.playerId == duel.AiPlayerIndex && idle.summonable.Any(card => card.code == KaiserSeaHorse),
                "The opposite seat did not reach a legal target summon.");
            duel.SubmitResponseInt(idle.summonable.FindIndex(card => card.code == KaiserSeaHorse) << 16);
            SelectableCardItem activation = null;
            ChainLinkInfo columnEffect = null;
            duel.OnChainResolved += link =>
            {
                if (link.code == impermanence) columnEffect = link;
            };
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                if (activation == null && prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && prompt.playerId == duel.HumanPlayerIndex)
                {
                    int index = prompt.candidates.FindIndex(card => card.code == impermanence);
                    if (index >= 0)
                    {
                        activation = prompt.candidates[index];
                        return BitConverter.GetBytes(index);
                    }
                }
                return prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.min == 1 && prompt.candidates.Count != 0
                    ? DuelSession.PackCardSelection(0) : null;
            });
            Require(activation != null && duel.RetryCount == 0, "The engine did not accept the expected Infinite Impermanence activation.");
            var rays = AnimationSteps(animator).Where(step => step.GetType().Name.StartsWith("<FieldRays>", StringComparison.Ordinal)).ToArray();
            Require(rays.Length == (setFirst ? 1 : 0), "Only a previously Set Infinite Impermanence should produce the column beam.");
            if (setFirst)
            {
                var step = rays[0];
                Require(step.MoveNext() && step.Current is IEnumerator, "The column effect failed to produce a beam.");
                TestCardPresentation.SampleAnimation((IEnumerator)step.Current)(.60f);
                var rect = (RectTransform)animator.transform.Find("ColumnRay");
                var board = ui.Field.Root.transform.Find("DuelBoard");
                var top = animator.GetPoint((RectTransform)board.Find($"Zone_{activation.sequence + 2}_0"), new Vector2(.5f, .93f));
                var bottom = animator.GetPoint((RectTransform)board.Find($"Zone_{activation.sequence + 2}_4"), new Vector2(.5f, .07f));
                Require(Vector2.Distance(rect.anchoredPosition, (top + bottom) * .5f) < .1f &&
                        Mathf.Abs(rect.sizeDelta.x - Vector2.Distance(top, bottom)) < .1f,
                    "The beam did not span the activated trap's column.");
            }
            Require(columnEffect != null && columnEffect.AppliedColumnNegation == setFirst,
                "The native column hint did not match Infinite Impermanence's eligible resolution.");
            var field = ui.Field.Root.transform.Find("DuelBoard");
            if (setFirst)
            {
                typeof(DuelFieldUI).GetMethod("ApplyColumnNegation", PrivateInstance).Invoke(ui.Field, new object[] { columnEffect });
                var marked = field.GetComponentsInChildren<DuelZoneImage>().Where(zone => zone.ColumnSealed)
                    .Select(zone => zone.name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                Require(marked.SequenceEqual(new[] { $"Zone_{activation.sequence + 2}_0", $"Zone_{activation.sequence + 2}_4" }),
                    $"The red seal covered the wrong spell and trap zones: {string.Join(", ", marked)}.");
                typeof(DuelFieldUI).GetMethod("ClearColumnNegations", PrivateInstance).Invoke(ui.Field, null);
                Require(field.GetComponentsInChildren<DuelZoneImage>().All(zone => !zone.ColumnSealed),
                    "The red zone seals did not clear at the next turn.");
            }
            animator.Clear();
            ui.Field.BindSession(duel, null, null);
            var target = duel.Players[duel.AiPlayerIndex].mzone.Single(card => card?.code == KaiserSeaHorse);
            Require(target.effectsDisabled, "The native target was not disabled.");
            var targetSlot = field.Find($"Zone_{6 - target.sequence}_1");
            Require(targetSlot?.Find("Card/CardView/AspectBox/DisabledChains")?.gameObject.activeInHierarchy == true,
                "A native disabled monster did not receive its field chains.");
            targetSlot.GetComponent<CardDragTrigger>().OnRightClick();
            var badge = ui.Field.Root.transform.Find("LeftInspector/CardPreview/Card/AspectBox/EffectDisabled");
            Require(badge.gameObject.activeInHierarchy && badge.Find("Label").GetComponent<Text>().text == "효과 무효화됨",
                "The inspector did not display the disabled effect badge.");
            Debug.Log($"[VERIFY DUEL COLUMN] Native Infinite Impermanence from {(setFirst ? "Set" : "hand")}, matching beam and seal count {rays.Length}.");
        }

        private static void VerifyMobileUnavailableEffectDetails()
        {
            const uint impermanence = 10045474;
            var player = Enumerable.Repeat(KaiserSeaHorse, 40).ToArray();
            var enemy = Enumerable.Repeat(impermanence, 40).ToArray();
            var empty = Array.Empty<uint>();
            using var duel = YgoProjectBuilder.StartSeedMatching("Disabled card touch details", player, empty, enemy, empty, true,
                session => session.ActivePrompt?.summonable.Any(option => option.code == KaiserSeaHorse) == true);
            int summon = duel.ActivePrompt.summonable.FindIndex(option => option.code == KaiserSeaHorse);
            duel.SubmitResponseInt(summon << 16);
            bool activated = false;
            YgoProjectBuilder.DriveUntilIdle(duel, new TacticalAI(), prompt =>
            {
                if (!activated && prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && prompt.playerId == duel.AiPlayerIndex)
                {
                    int index = prompt.candidates.FindIndex(option => option.code == impermanence);
                    if (index >= 0)
                    {
                        activated = true;
                        return BitConverter.GetBytes(index);
                    }
                }
                return prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.min == 1 && prompt.candidates.Count == 1
                    ? DuelSession.PackCardSelection(0) : null;
            });
            var owner = duel.Players[duel.HumanPlayerIndex];
            var monster = owner.mzone.Single(card => card?.code == KaiserSeaHorse);
            Require(activated && monster.effectsDisabled && duel.RetryCount == 0 &&
                    duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD,
                "A native Infinite Impermanence did not disable the player's monster during the Main Phase.");

            using var ui = new HandUI(duel, mobileLayout: true);
            var root = ui.Field.Root.transform;
            var safeArea = root.Find("MobileSafeArea");
            var home = safeArea.Find("ActivationNotice");
            var detail = root.Find("Overlay_CardZoom/MobileSafeArea/ZoomWin");
            var close = detail.Find("Hdr/Close/B").GetComponent<Button>();
            var slot = safeArea.Find($"DuelBoard/Zone_{monster.sequence + 2}_3");
            var trigger = slot.GetComponent<CardDragTrigger>();
            var current = duel.ActivePrompt;
            trigger.OnTouchClick();
            var badge = detail.Find("CardHolder/ZoomCard/AspectBox/EffectDisabled");
            Require(detail.gameObject.activeInHierarchy && badge.gameObject.activeInHierarchy &&
                    detail.Find("Hdr/ActivationNotice") == home &&
                    home.Find("Reason").GetComponent<Text>().text == "이 카드의 효과가 무효화되어 있습니다.",
                "One tap on the disabled monster did not show its detailed face, badge and activation reason together.");
            Canvas.ForceUpdateCanvases();
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(detail, home);
            var closeBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(detail, close.transform);
            Require(!bounds.Intersects(closeBounds) && !home.GetComponent<CanvasGroup>().blocksRaycasts,
                "The activation reason overlapped or blocked the detail close control.");
            close.onClick.Invoke();
            Require(!detail.gameObject.activeInHierarchy && !home.gameObject.activeSelf && home.parent == safeArea &&
                    Mathf.Approximately(((RectTransform)detail.Find("Hdr/Title")).anchorMax.x, .82f) &&
                    ReferenceEquals(duel.ActivePrompt, current),
                "Closing the details left a stale notice, a narrow header or submitted a duel command.");

            var handCard = safeArea.Find("PlayerHand/Cards").GetChild((int)owner.hand[0].sequence);
            handCard.GetComponent<CardDragTrigger>().OnTouchClick();
            Require(detail.gameObject.activeInHierarchy && !badge.gameObject.activeInHierarchy && home.parent == detail.Find("Hdr") &&
                    home.Find("Reason").GetComponent<Text>().text == "발동 조건을 만족하지 않았습니다.",
                "An unavailable hand card did not open its own face and reason.");
            typeof(DuelFieldUI).GetField("_activationNoticeUntil", PrivateInstance).SetValue(ui.Field, -1f);
            typeof(DuelFieldUI).GetMethod("UpdateNegativeFeedback", PrivateInstance).Invoke(ui.Field, null);
            Require(detail.gameObject.activeInHierarchy && !home.gameObject.activeSelf && home.parent == safeArea,
                "Expiring the notice prematurely closed or distorted the card details.");
            Debug.Log("[VERIFY DUEL TOUCH DETAILS] Native disabled monster and unavailable hand card showed their reasons without obstructing inspection.");
        }

        private static void VerifyDuelOverframeLongPress()
        {
            const uint excel = 9674034;
            uint[] player = Enumerable.Repeat(excel, 40).ToArray();
            uint[] opponent = Enumerable.Repeat(KaiserSeaHorse, 40).ToArray();
            var empty = Array.Empty<uint>();
            using var duel = YgoProjectBuilder.StartSeedMatching("Duel overframe on touch", player, empty, opponent, empty,
                true, session => session.ActivePrompt?.summonable.Any(card => card.code == excel) == true);
            using var ui = new HandUI(duel, mobileLayout: true);
            var root = ui.Field.Root.transform;
            var safe = root.Find("MobileSafeArea");
            var window = root.Find("Overlay_CardZoom/MobileSafeArea/ZoomWin");
            var view = window.Find("CardHolder/ZoomCard").GetComponent<CompositeCardView>();
            var trigger = safe.Find("PlayerHand/Cards").GetChild(0).GetComponent<CardDragTrigger>();
            Hold(trigger);
            Require(window.gameObject.activeInHierarchy && view.CurrentCode == excel && !view.HasOverframe &&
                view.ArtworkSprite != CardArtLoader.GetFallbackArt(),
                "Long-pressing the hand did not reveal the card's printed artwork.");
            window.Find("Hdr/Close/B").GetComponent<Button>().onClick.Invoke();

            int index = duel.ActivePrompt.summonable.FindIndex(card => card.code == excel);
            duel.SubmitResponseInt(index << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, new TacticalAI());
            CardAnimationManager.Instance?.Clear();
            ui.Field.Refresh();
            var own = duel.Players[duel.HumanPlayerIndex];
            var card = own.mzone.Single(c => c?.code == excel);
            var zone = safe.Find($"DuelBoard/Zone_{card.sequence + 2}_3");
            var fieldCard = zone.GetComponent<CardDragTrigger>();
            fieldCard.OnRightClick();
            Hold(fieldCard);
            var inspected = (CompositeCardView)typeof(DuelFieldUI).GetField("_inspCard", PrivateInstance).GetValue(ui.Field);
            Require(window.gameObject.activeInHierarchy && view.CurrentCode == excel && !view.HasOverframe &&
                inspected.CurrentCode == excel && !inspected.HasOverframe && inspected.ArtworkSprite == view.ArtworkSprite &&
                duel.RetryCount == 0, "The duel inspector and zoom did not show the same printed artwork.");
            Debug.Log("[VERIFY PRINTED DUEL FACES] Hand and field long presses opened the same mobile detail view.");
            VerifyStyledDuelSurfaces();
            VerifyNativeStyledActivations();

            static void Hold(CardDragTrigger source)
            {
                var pointer = new PointerEventData(EventSystem.current) { pointerId = 5, button = PointerEventData.InputButton.Left };
                source.OnPointerDown(pointer);
                float started = (float)typeof(CardDragTrigger).GetField("_touchPressAt", PrivateInstance).GetValue(source);
                Require(source.TryCompleteTouchHold(5, pointer.position, started + .5f), "The card did not accept a stationary long press.");
                source.OnPointerUp(pointer);
                source.OnPointerClick(pointer);
            }
        }

        private static void VerifyStyledDuelSurfaces()
        {
            const uint excel = 9674034;
            var deck = new UserDeck();
            for (int i = 0; i < 40; i++)
                deck.InsertCard(deck.main, i, excel, new DeckCardAppearance
                {
                    overframe = i % 2 == 0,
                    artwork = i % 2 == 0 ? CardArtLoader.FirstOverframeVariant(excel) + 1 : 0,
                });
            using var duel = new DuelSession();
            Require(duel.StartNewDuel(deck.main, deck.extra, Enumerable.Repeat(KaiserSeaHorse, 40).ToArray(),
                Array.Empty<uint>(), seed: new ulong[] { 718, 391, 27182, 31 }, playerDeckAppearance: deck),
                "Could not start the styled field fixture.");
            if (duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_CHAIN)
                duel.SubmitResponseInt(-1);
            using var ui = new HandUI(duel, mobileLayout: true);
            var safe = ui.Field.Root.transform.Find("MobileSafeArea");
            var row = safe.Find("PlayerHand/Cards");
            var mine = duel.Players[duel.HumanPlayerIndex];
            Require(mine.hand.Any(card => card.Appearance?.overframe == false) &&
                mine.hand.Any(card => card.Appearance?.overframe == true), "The opening hand lacked both editions.");
            for (int i = 0; i < mine.hand.Count; i++)
            {
                var face = row.GetChild(i).GetComponentInChildren<CompositeCardView>();
                Require(!face.HasOverframe && Nullable.Equals(face.Appearance, mine.hand[i].Appearance) &&
                    face.transform.Find("AspectBox/Artwork").GetComponent<Image>().preserveAspect,
                    "The hand showed a cut-in, distorted its art or mixed up the physical copies.");
            }
            int choice = duel.ActivePrompt.summonable.FindIndex(card => mine.hand[(int)card.sequence].Appearance?.overframe == true);
            Require(choice >= 0, "The special copy could not be summoned in the field fixture.");
            var chosen = row.GetChild((int)duel.ActivePrompt.summonable[choice].sequence);
            chosen.GetComponent<CardDragTrigger>().OnPointerEnterAction.Invoke();
            typeof(DuelFieldUI).GetMethod("AdvanceDuelPadCardMotions", PrivateInstance).Invoke(ui.Field, new object[] { .25f });
            var focused = (RectTransform)chosen.GetComponentInChildren<CompositeCardView>().transform;
            Require(focused.anchoredPosition.y > 1f && focused.Find("AspectBox/Overframe") == null,
                "Focusing a special copy did not lift the printed card, or exposed a cut-in.");
            var mask = row.parent.GetComponent<RectMask2D>();
            Require(mask.padding.x == 0f && mask.padding.z == 0f,
                "The mobile hand lost its horizontal clipping.");

            var animator = CardAnimationManager.Ensure(ui.Canvas);
            duel.SubmitResponseInt(choice << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, new TacticalAI());
            var move = AnimationSteps(animator).Single(step => FlightCode(step) == excel &&
                step.GetType().Name.StartsWith("<MoveCard>", StringComparison.Ordinal));
            Require(move.MoveNext() && move.Current is IEnumerator, "The native summon did not start its card movement.");
            var travel = (IEnumerator)move.Current;
            if (travel.GetType().Name.StartsWith("<RevealSummon>", StringComparison.Ordinal))
            {
                while (travel.MoveNext()) TestCardPresentation.SampleAnimation((IEnumerator)travel.Current)(1f);
            }
            else TestCardPresentation.SampleAnimation(travel)(1f);
            Require(move.MoveNext() && move.Current is IEnumerator, "The native summon did not hand off to the landing cue.");
            var placed = mine.mzone.Single(card => card?.code == excel);
            var slot = safe.Find($"DuelBoard/Zone_{placed.sequence + 2}_3");
            var fieldFace = slot.GetComponentInChildren<CompositeCardView>();
            Require(fieldFace.CurrentCode == excel && !fieldFace.HasOverframe &&
                fieldFace.Appearance?.overframe == true && duel.RetryCount == 0,
                "The selected physical copy did not keep its edition on the actual duel field.");
            var landing = (IEnumerator)move.Current;
            Require(landing.MoveNext(), "The field landing did not animate.");
            var board = (RectTransform)safe.Find("DuelBoard");
            Vector2 rest = board.anchoredPosition;
            TestCardPresentation.SampleAnimation((IEnumerator)landing.Current)(.18f);
            Require(Vector2.Distance(board.anchoredPosition, rest) > .1f && !fieldFace.HasOverframe,
                "The destination field did not react to the landing, or introduced an extra portrait.");
            animator.Clear();
            Require(Vector2.Distance(board.anchoredPosition, rest) < .01f,
                "Interrupting the animation left the board offset.");
            Debug.Log("[VERIFY OVERFRAME SURFACES] Printed hand and field faces preserved edition, focus and landing.");
        }

        private static void VerifyNativeStyledActivations()
        {
            const uint burial = 81439173, beast = 3717252, jar = 83968380;
            var deck = new UserDeck();
            foreach (uint code in Enumerable.Repeat(burial, 17).Concat(Enumerable.Repeat(jar, 17)).Concat(Enumerable.Repeat(beast, 6)))
                deck.InsertCard(deck.main, deck.main.Count, code, new DeckCardAppearance { overframe = true });
            using var duel = OpenFixture();
            using var ui = new HandUI(duel, mobileLayout: true);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            var ai = new TacticalAI();
            var links = new List<ChainLinkInfo>();
            bool spellFromHand = false;
            duel.OnChainActivated += links.Add;
            duel.OnCardMoved += move => spellFromHand |= move.code == burial && move.fromLoc == YgoConstants.LOCATION_HAND;

            ActivateFromMainPhase(duel, burial);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD
                ? SelectCard(prompt, beast) : ReplyTo(prompt, beast));
            Require(spellFromHand && links.Any(link => link.code == burial && link.Appearance?.overframe == true) &&
                links.Any(link => link.code == beast && link.triggerLoc == YgoConstants.LOCATION_GRAVE &&
                    link.Appearance?.overframe == true), "A native activation from the hand or graveyard lost its edition.");

            int set = duel.ActivePrompt.sSetable.FindIndex(card => card.code == jar);
            Require(set >= 0, "The special trap was unavailable to set.");
            duel.SubmitResponseInt((set << 16) | 4);
            for (int phase = 0; phase < 5 && !links.Any(link => link.code == jar); phase++)
            {
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => ReplyTo(prompt, jar));
                if (links.Any(link => link.code == jar)) break;
                var prompt = duel.ActivePrompt;
                Require(prompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD, "The native trap fixture stalled.");
                if (prompt.playerId == duel.HumanPlayerIndex && duel.TurnNumber > 1 &&
                    prompt.activatable.Any(card => card.code == jar)) ActivateFromMainPhase(duel, jar);
                else duel.SubmitResponseInt(7);
            }
            Require(links.Any(link => link.code == jar && link.triggerLoc == YgoConstants.LOCATION_SZONE &&
                link.Appearance?.overframe == true) && duel.RetryCount == 0,
                "A native field trap lost its edition or could not activate.");
            var cues = AnimationSteps(animator).Where(cue =>
                cue.GetType().Name.StartsWith("<ChainActivation>", StringComparison.Ordinal)).ToArray();
            Require(cues.Length == 3, "The native activation events did not all enter the duel interface queue.");
            foreach (var cue in cues)
            {
                Require(cue.MoveNext(), "A queued native activation omitted its presentation.");
                TestCardPresentation.SampleAnimation((IEnumerator)cue.Current)(.94f);
                Require(animator.transform.Find("OverframeActivation") != null,
                    "A native activation did not show its special edition flare.");
                while (cue.MoveNext()) TestCardPresentation.SampleAnimation((IEnumerator)cue.Current)(1f);
            }
            animator.Clear();
            Debug.Log("[VERIFY OVERFRAME NATIVE ACTIVATIONS] Hand spell, graveyard monster and set trap kept their edition.");

            DuelSession OpenFixture()
            {
                for (ulong seed = 1; seed <= 16; seed++)
                {
                    var session = new DuelSession();
                    if (session.StartNewDuel(deck.main, deck.extra, Enumerable.Repeat(KaiserSeaHorse, 40).ToArray(),
                        Array.Empty<uint>(), seed: new[] { seed * 29, seed * 47, seed * 71, seed * 97 },
                        playerDeckAppearance: deck) && session.ActivePrompt?.activatable.Any(card => card.code == burial) == true &&
                        session.ActivePrompt.sSetable.Any(card => card.code == jar)) return session;
                    session.Dispose();
                }
                throw new InvalidOperationException("An opening with both the spell and trap could not be found.");
            }

            static byte[] ReplyTo(DuelPrompt prompt, uint code)
            {
                if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == code)
                    return BitConverter.GetBytes(1);
                if (prompt.messageType != YgoConstants.MSG_SELECT_CHAIN) return null;
                int index = prompt.candidates.FindIndex(card => card.code == code);
                return index < 0 ? null : BitConverter.GetBytes(index);
            }
        }

        private static void VerifyDragColumnAndPhaseShortcut()
        {
            var cards = Enumerable.Repeat(KaiserSeaHorse, 40).ToArray();
            var empty = Array.Empty<uint>();
            using var duel = YgoProjectBuilder.StartSeedMatching("Field column placement", cards, empty, cards, empty,
                true, session => session.ActivePrompt?.summonable.Count > 0);
            using var ui = new HandUI(duel);
            var drag = CardDragManager.Ensure(ui.Canvas);
            var board = ui.Field.Root.transform.Find("DuelBoard");
            var slot = (RectTransform)board.Find("Zone_4_3");
            Canvas.ForceUpdateCanvases();
            var point = RectTransformUtility.WorldToScreenPoint(null, slot.TransformPoint(slot.rect.center));
            var option = duel.ActivePrompt.summonable[0];
            drag.StartDrag(DragSourceType.Hand, option.code, (int)option.sequence, screenPosition: point);
            var type = typeof(DuelFieldUI);
            type.GetMethod("HandleDuelDragStarted", PrivateInstance).Invoke(ui.Field,
                new object[] { DragSourceType.Hand, option.code, (int)option.sequence, null });
            type.GetMethod("UpdateDragColumnGuide", PrivateInstance).Invoke(ui.Field, null);
            var guided = board.GetComponentsInChildren<DuelZoneImage>().Where(zone => zone.ColumnGuided).Select(zone => zone.name).ToArray();
            Require(slot.GetComponent<DuelZoneImage>().DragCandidate &&
                board.GetComponentsInChildren<DuelZoneImage>().Count(zone => zone.DragCandidate) == 5,
                "The dragged summon did not animate each legal Main Monster Zone.");
            Require(guided.Length == 4 && guided.All(zone => zone.StartsWith("Zone_4_", StringComparison.Ordinal)),
                "Dragging a legal hand card did not light all four zones in the projected column.");
            drag.CancelDrag();
            type.GetMethod("HandleDuelDragCancelled", PrivateInstance).Invoke(ui.Field, null);
            Require(board.GetComponentsInChildren<DuelZoneImage>().All(zone => !zone.ColumnGuided && !zone.DragCandidate),
                "The canceled drag left a lit placement zone.");

            int human = duel.HumanPlayerIndex;
            var stillWaiting = duel.ActivePrompt;
            var fieldLock = typeof(DuelSession).GetProperty(nameof(DuelSession.FieldDisabledMask));
            uint baseline = duel.FieldDisabledMask;
            fieldLock.SetValue(duel, baseline | (1u << (human * 16 + 2)));
            drag.StartDrag(DragSourceType.Hand, option.code, (int)option.sequence, screenPosition: point);
            type.GetMethod("HandleDuelDragStarted", PrivateInstance).Invoke(ui.Field,
                new object[] { DragSourceType.Hand, option.code, (int)option.sequence, null });
            Require(!slot.GetComponent<DuelZoneImage>().DragCandidate &&
                board.GetComponentsInChildren<DuelZoneImage>().Count(zone => zone.DragCandidate) == 4,
                "A disabled monster zone was indicated as a valid drop destination.");
            drag.CancelDrag();
            type.GetMethod("HandleDuelCardDrop", PrivateInstance).Invoke(ui.Field, new object[] { new DragContext
            {
                SourceType = DragSourceType.Hand, SourceIndex = (int)option.sequence, CardCode = option.code,
                DropPosition = point,
            } });
            Require(ReferenceEquals(stillWaiting, duel.ActivePrompt) &&
                !ui.Field.Root.transform.Find("CardCommands").gameObject.activeSelf,
                "Dropping into an invalid zone submitted an action or opened a misleading chooser.");
            fieldLock.SetValue(duel, baseline);

            var end = ui.Field.Root.transform.Find("EndShortcut").GetComponent<Button>();
            Require(end.gameObject.activeInHierarchy, "A legal End Phase shortcut did not appear.");
            end.onClick.Invoke();
            YgoProjectBuilder.DriveUntilIdle(duel, new TacticalAI());
            Require(duel.TurnNumber == 2 && duel.TurnPlayer != human && duel.RetryCount == 0,
                "The direct End Phase shortcut did not transfer the native turn.");
            Debug.Log("[VERIFY DUEL SHORTCUT] Drag column cleared and direct End Phase advanced the native duel.");
        }

        private static void VerifyNativeFieldSpellDrag()
        {
            const uint umi = 22702055;
            var cards = Enumerable.Repeat(umi, 40).ToArray();
            var enemy = Enumerable.Repeat(KaiserSeaHorse, 40).ToArray();
            var empty = Array.Empty<uint>();
            using var duel = YgoProjectBuilder.StartSeedMatching("Field Zone replacement gesture", cards, empty,
                enemy, empty, true, session => session.ActivePrompt?.sSetable.Any(option => option.code == umi) == true);
            var ai = new TacticalAI();
            int set = duel.ActivePrompt.sSetable.FindIndex(option => option.code == umi);
            duel.SubmitResponseInt((set << 16) | 4);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            var prompt = duel.ActivePrompt;
            var copy = prompt.sSetable.First(option => option.code == umi);
            var human = duel.Players[duel.HumanPlayerIndex];
            Require(human.szone[5]?.code == umi, "The native fixture did not have an occupied Field Zone.");
            using var ui = new HandUI(duel);
            var drag = CardDragManager.Ensure(ui.Canvas);
            var type = typeof(DuelFieldUI);
            var board = ui.Field.Root.transform.Find("DuelBoard");
            var drop = (RectTransform)board.Find("Zone_4_4");
            Canvas.ForceUpdateCanvases();
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, drop.TransformPoint(drop.rect.center));
            drag.StartDrag(DragSourceType.Hand, copy.code, (int)copy.sequence, screenPosition: point);
            type.GetMethod("HandleDuelDragStarted", PrivateInstance).Invoke(ui.Field,
                new object[] { DragSourceType.Hand, copy.code, (int)copy.sequence, null });
            var legal = board.GetComponentsInChildren<DuelZoneImage>().Where(zone => zone.DragCandidate).ToArray();
            var fieldZone = (RectTransform)board.Find("Zone_1_3");
            Require(legal.Length == 1 && legal[0].transform == fieldZone,
                $"A Field Spell marked [{string.Join(", ", legal.Select(zone => zone.name))}] instead of its occupied Field Zone.");
            drag.CancelDrag();
            type.GetMethod("HandleDuelCardDrop", PrivateInstance).Invoke(ui.Field, new object[] { new DragContext
            {
                SourceType = DragSourceType.Hand, SourceIndex = (int)copy.sequence, CardCode = umi, DropPosition = point,
            } });
            Require(ReferenceEquals(prompt, duel.ActivePrompt) && !ui.Field.Root.transform.Find("CardCommands").gameObject.activeSelf,
                "Dropping a Field Spell into a standard Spell and Trap Zone triggered a command.");
            int handBefore = human.hand.Count;
            int graveBefore = human.grave.Count;
            Vector2 fieldPoint = RectTransformUtility.WorldToScreenPoint(null, fieldZone.TransformPoint(fieldZone.rect.center));
            type.GetMethod("HandleDuelCardDrop", PrivateInstance).Invoke(ui.Field, new object[] { new DragContext
            {
                SourceType = DragSourceType.Hand, SourceIndex = (int)copy.sequence, CardCode = umi, DropPosition = fieldPoint,
            } });
            var chooser = ui.Field.Root.transform.Find("CardCommands");
            if (chooser.gameObject.activeSelf)
            {
                var setAction = chooser.GetComponentsInChildren<Button>().Single(button => button.transform != chooser &&
                    button.GetComponentsInChildren<Text>().Any(text => text.text == "마법·함정 세트"));
                setAction.onClick.Invoke();
            }
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            Require(human.hand.Count == handBefore - 1 && human.grave.Count == graveBefore + 1 &&
                    human.szone[5]?.code == umi && duel.RetryCount == 0,
                "Dropping onto the valid occupied Field Zone did not replace the native card.");
            Debug.Log("[VERIFY DUEL HAND PLACEMENT] The only highlighted zone accepted Field Spell replacement and the invalid drop submitted nothing.");
        }

        private static void VerifyNativeCoLinkedZone()
        {
            const uint digitron = 32295838, secretary = 63528891, disciple = 32995276, devotee = 48068378, linkToken = 48068379;
            var main = Enumerable.Repeat(digitron, 28).Concat(Enumerable.Repeat(secretary, 12)).ToArray();
            var opponent = Enumerable.Repeat(BlueEyes, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Co-linked zones", main, new[] { disciple, devotee }, opponent,
                Array.Empty<uint>(), true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return hand.Any(card => card.code == digitron) && hand.Any(card => card.code == secretary);
                });
            int seat = duel.HumanPlayerIndex;
            var own = duel.Players[seat];
            var ai = new TacticalAI();

            void ReachIdle(int selectZone = -1)
            {
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                {
                    if (prompt.messageType == YgoConstants.MSG_SELECT_PLACE && selectZone >= 0 &&
                        prompt.playerId == seat && (prompt.placeFlag & (1u << selectZone)) == 0)
                        return new[] { (byte)seat, (byte)YgoConstants.LOCATION_MZONE, (byte)selectZone };
                    if (prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.min == 1)
                        return DuelSession.PackCardSelection(0);
                    if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == devotee)
                        return BitConverter.GetBytes(1);
                    if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
                    {
                        int trigger = prompt.candidates.FindIndex(card => card.code == devotee);
                        if (trigger >= 0) return BitConverter.GetBytes(trigger);
                    }
                    return null;
                });
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    "The co-link fixture did not return to the main phase.");
            }

            void SpecialSummon(uint code, int selectZone = -1)
            {
                int option = duel.ActivePrompt.spSummonable.FindIndex(card => card.code == code);
                Require(option >= 0, $"The engine did not offer the summon of {code}.");
                duel.SubmitResponseInt((option << 16) | 1);
                ReachIdle(selectZone);
            }

            int normal = duel.ActivePrompt.summonable.FindIndex(card => card.code == digitron);
            Require(normal >= 0, "The engine did not offer Digitron's normal summon.");
            duel.SubmitResponseInt(normal << 16);
            ReachIdle();
            SpecialSummon(disciple);
            var origin = own.mzone.Single(card => card?.code == disciple);
            Require(origin.sequence is 5 or 6, "Link Disciple did not enter an Extra Monster Zone.");
            int target = origin.sequence == 5 ? 1 : 3;
            string connected = $"Zone_{target + 2}_3";

            using var ui = new HandUI(duel);
            ui.Field.Root.transform.Find($"DuelBoard/Zone_{target + 2}_2").GetComponent<CardDragTrigger>().OnRightClick();
            ui.ExpectLinkedZones(connected);
            ui.ExpectLinkHighlight(connected, DuelLinkHighlight.Linked, Vector2Int.down);

            SpecialSummon(secretary, target);
            ui.Field.Refresh();
            Require(own.mzone[target]?.code == secretary, "Backup Secretary did not enter the indicated linked zone.");
            ui.ExpectLinkHighlight(connected, DuelLinkHighlight.Linked, Vector2Int.zero);

            SpecialSummon(devotee, target);
            ui.Field.Refresh();
            Require(own.mzone[target]?.code == devotee, "Link Devotee did not enter the linked Main Monster Zone.");
            ui.ExpectLinkHighlight(connected, DuelLinkHighlight.CoLinked, Vector2Int.zero);

            int tribute = duel.ActivePrompt.activatable.FindIndex(card => card.code == disciple);
            Require(tribute >= 0, "Link Disciple could not tribute its linked monster.");
            duel.SubmitResponseInt((tribute << 16) | 5);
            ReachIdle();
            ui.Field.Refresh();
            Require(own.mzone.Count(card => card?.code == linkToken) == 2,
                "Link Devotee did not produce tokens after being tributed while co-linked.");
            ui.ExpectLinkHighlight(connected, DuelLinkHighlight.Linked,
                own.mzone[target] == null ? Vector2Int.down : Vector2Int.zero);
            Debug.Log("[VERIFY DUEL CO-LINK] Direction and occupancy changed correctly. Link Devotee's native co-link effect produced two tokens.");
        }

        private static void VerifyNativeMovingReadout()
        {
            const uint bookOfMoon = 14087893, bookOfTaiyou = 38699854;
            uint[] main = Enumerable.Repeat(KaiserSeaHorse, 26).Concat(Enumerable.Repeat(bookOfMoon, 7))
                .Concat(Enumerable.Repeat(bookOfTaiyou, 7)).ToArray();
            uint[] opponent = Enumerable.Repeat(BlueEyes, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Animated field readout", main, Array.Empty<uint>(),
                opponent, Array.Empty<uint>(), true, session => session.ActivePrompt?.summonable.Count > 0 &&
                    new[] { bookOfMoon, bookOfTaiyou }.All(code =>
                        session.Players[session.HumanPlayerIndex].hand.Any(card => card.code == code)));
            var ai = new TacticalAI();

            void ReachIdle()
            {
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD &&
                    prompt.candidates.Count == 1 ? DuelSession.PackCardSelection(0) : null);
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    "The native movement fixture did not return to the Main Phase.");
            }

            using var ui = new HandUI(duel);
            var animation = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            var pending = PendingMoveReadouts(ui.Field);
            duel.SubmitResponseInt(0);
            ReachIdle();
            var summoned = pending.Values.Single();
            ui.Field.Refresh();
            var arrival = OpenMove(animation, KaiserSeaHorse, summoned);
            Require(!arrival.Readout.Find("CardReadout").gameObject.activeInHierarchy,
                "The regular summon displayed stats while the monster was leaving the hand.");
            arrival.Sample(.78f);
            RequireTravelStats(arrival.Readout, "1700", "1650", "4", defense: false);
            arrival.Sample(1f);
            Require(arrival.Step.MoveNext() && arrival.Readout == null &&
                    !arrival.Step.MoveNext(), "The regular summon did not hand off to the field before its final effect.");
            duel.SubmitResponseInt(7);
            ReachIdle();
            duel.SubmitResponseInt(7);
            ReachIdle();
            Require(duel.ActivePrompt.repositionable.Count == 1, "The native engine did not offer a position change.");
            var monster = duel.ActivePrompt.repositionable[0];
            Canvas.ForceUpdateCanvases();
            var slot = (RectTransform)ui.Field.Root.transform.Find($"DuelBoard/Zone_{monster.sequence + 2}_3");
            var card = (RectTransform)slot.Find("Card");
            var art = (RectTransform)card.Find("CardView/AspectBox");
            var restingReadout = (RectTransform)slot.Find("CardReadout");
            var trigger = slot.GetComponent<CardDragTrigger>();
            var pointer = new PointerEventData(EventSystem.current) { pointerId = -1 };
            var advanceFocus = typeof(DuelFieldUI).GetMethod("AdvanceDuelPadCardMotions", PrivateInstance);
            void AdvanceFocus(float time) => advanceFocus.Invoke(ui.Field, new object[] { time });

            var hitArea = slot.rect;
            var restingPosition = slot.anchoredPosition;
            trigger.OnPointerEnter(pointer);
            AdvanceFocus(.045f);
            var shadow = art.Find("FocusShadow")?.GetComponent<Image>();
            Require(card.anchoredPosition.y > 0f && restingReadout.anchoredPosition == card.anchoredPosition &&
                art.localScale.x > 1f && art.localScale.x < 1.05f && restingReadout.localScale == art.localScale &&
                shadow != null && shadow.enabled && !shadow.raycastTarget && slot.rect == hitArea && slot.anchoredPosition == restingPosition,
                "Pointing at a monster did not smoothly raise the art and stats without moving its hit area.");
            var focusedPose = animation.GetVisualPose(art);
            Vector2 focusedStats = animation.GetPoint(restingReadout, Vector2.one) - animation.GetPoint(restingReadout, Vector2.zero);
            duel.SubmitResponseInt(2);
            ReachIdle();
            var snapshot = pending[(monster.controller, monster.location, monster.sequence)];
            ui.Field.Refresh();
            Require(snapshot.DestinationCard?.IsDefense == true && snapshot.SourceCard?.position == YgoConstants.POS_FACEUP_ATTACK &&
                Vector2.Distance(snapshot.Movement.StartSize, focusedStats) < .1f &&
                Vector2.Distance(animation.GetVisualPose(art).Position, focusedPose.Position) < .2f &&
                restingReadout.gameObject.activeInHierarchy,
                "A queued position change did not retain the raised card and its attack-position readout.");

            var motion = OpenMove(animation, monster.code, snapshot);
            Require(Vector2.Distance(motion.Readout.anchoredPosition, focusedPose.Position) < .2f,
                "The motion did not start at the raised card pose.");
            Require(card.anchoredPosition == Vector2.zero && !restingReadout.gameObject.activeInHierarchy,
                "The field kept a duplicate card or readout after its motion started.");
            RequireTravelStats(motion.Readout, "1700", "1650", "4", defense: false);
            motion.Sample(.66f);
            Require(motion.Readout.anchoredPosition == ((RectTransform)motion.Proxy.transform).anchoredPosition &&
                motion.Readout.localRotation == Quaternion.identity,
                "A moving readout rotated its text or kept highlighting the old stance.");
            RequireTravelStats(motion.Readout, "1700", "1650", "4", defense: true);
            motion.Sample(1f);
            Require(!motion.Step.MoveNext() && motion.Readout == null,
                "The travelling stats remained after landing.");
            var landed = restingReadout.Find("Combat");
            Require(landed.gameObject.activeInHierarchy &&
                landed.Find("Defense/Rail").GetComponent<Image>().color.a > landed.Find("Attack/Rail").GetComponent<Image>().color.a,
                "The landing interrupted the defense readout.");

            trigger.OnPointerEnter(pointer);
            AdvanceFocus(.12f);
            Require(card.anchoredPosition.y > 0f && restingReadout.localRotation == Quaternion.identity &&
                restingReadout.anchoredPosition == card.anchoredPosition,
                "Hovering a defense-position card displaced or turned its digits.");
            trigger.OnPointerExit(pointer);
            typeof(DuelFieldUI).GetField("_pointerActionLeftAt", PrivateInstance).SetValue(ui.Field, float.NegativeInfinity);
            typeof(DuelFieldUI).GetMethod("RefreshCardActions", PrivateInstance).Invoke(ui.Field, null);
            AdvanceFocus(.5f);
            Require(card.anchoredPosition == Vector2.zero && restingReadout.anchoredPosition == Vector2.zero &&
                restingReadout.localScale == Vector3.one && art.localScale == Vector3.one && !shadow.enabled,
                "The selected monster and its readout did not settle after the pointer left.");

            int turnDown = duel.ActivePrompt.activatable.FindIndex(card => card.code == bookOfMoon);
            Require(turnDown >= 0, "The movement fixture could not activate its face-down effect.");
            duel.SubmitResponseInt((turnDown << 16) | 5);
            ReachIdle();
            Require(pending.Count == 0, "A face-down move queued live stats.");
            ui.Field.Refresh();
            var conceal = OpenMove(animation, monster.code);
            RequireTravelStats(conceal.Readout, "1700", "1650", "4", defense: true);
            conceal.Sample(.5f);
            Require(conceal.Proxy.CurrentCode == 0 && !conceal.Readout.Find("CardReadout").gameObject.activeInHierarchy &&
                conceal.Readout.GetComponent<CanvasGroup>().alpha == 0f, "Turning to the back leaked the old live stats.");
            conceal.Sample(1f);
            Require(!conceal.Step.MoveNext(), "The face-down move did not finish.");

            int turnUp = duel.ActivePrompt.activatable.FindIndex(card => card.code == bookOfTaiyou);
            Require(turnUp >= 0, "The movement fixture could not reveal the set card.");
            duel.SubmitResponseInt((turnUp << 16) | 5);
            ReachIdle();
            var revealed = pending[(monster.controller, monster.location, monster.sequence)];
            ui.Field.Refresh();
            var reveal = OpenMove(animation, monster.code, revealed);
            reveal.Sample(.49f);
            Require(reveal.Proxy.CurrentCode == 0 && !reveal.Readout.Find("CardReadout").gameObject.activeInHierarchy,
                "The reverse flip exposed the monster before its back had turned.");
            reveal.Sample(.74f);
            Require(reveal.Proxy.CurrentCode == KaiserSeaHorse, "The reverse flip did not reveal the monster.");
            RequireTravelStats(reveal.Readout, "1700", "1650", "4", defense: false);
            reveal.Sample(1f);
            Require(!reveal.Step.MoveNext() && restingReadout.gameObject.activeInHierarchy,
                "The revealed card did not keep its stats after landing.");
            Debug.Log("[VERIFY DUEL MOVING STATS] Native regular summon, pointer focus, rotation and both flip directions preserved public ATK, DEF and Level.");
        }

        private static void VerifyMovingReadoutAfterLayoutChange()
        {
            var main = Enumerable.Repeat(KaiserSeaHorse, 40).ToArray();
            var opponent = Enumerable.Repeat(BlueEyes, 40).ToArray();
            var empty = Array.Empty<uint>();
            using var duel = YgoProjectBuilder.StartSeedMatching("Responsive moving readout", main, empty, opponent, empty, true,
                session => session.ActivePrompt?.summonable.Count > 0);
            using var ui = new HandUI(duel, mobileLayout: true);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            var screen = (RectTransform)ui.Canvas.transform;
            var viewport = ui.Field.Root.transform.Find("MobileSafeArea");
            var safeArea = viewport.GetComponent<MobileSafeArea>();
            void Reflow(Vector2Int display, Rect safe)
            {
                screen.sizeDelta = (Vector2)display;
                safeArea.Reflow(safe, display);
                Canvas.ForceUpdateCanvases();
            }

            Reflow(new Vector2Int(1680, 800), new Rect(0, 18, 1570, 762));
            ui.Field.Refresh();
            duel.SubmitResponseInt(0);
            YgoProjectBuilder.DriveUntilIdle(duel, new TacticalAI());
            Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                "The responsive summon did not resolve.");
            var live = duel.Players[duel.HumanPlayerIndex].mzone.Single(card => card?.code == KaiserSeaHorse);
            var slot = (RectTransform)FieldSlot(ui.Field, live);
            var pending = PendingMoveReadouts(ui.Field).Values.Single();
            Vector2 initialDestination = animator.GetPoint(slot, Vector2.one * .5f);

            // The phone changes which side the cutout occupies while the summon is queued.
            Reflow(new Vector2Int(1680, 800), new Rect(110, 18, 1570, 762));
            ui.Field.Refresh();
            var arrival = OpenMove(animator, KaiserSeaHorse, pending);
            arrival.Sample(.87f);
            RequireTravelStats(arrival.Readout, "1700", "1650", "4", defense: false);

            // Resize again while the card and its stats are visible in flight.
            Reflow(new Vector2Int(1320, 720), new Rect(92, 25, 1174, 674));
            arrival.Sample(1f);
            var expected = animator.GetPoint(slot, Vector2.one * .5f);
            var proxy = (RectTransform)arrival.Proxy.transform;
            Require(Vector2.Distance(expected, initialDestination) > 20f &&
                Vector2.Distance(proxy.anchoredPosition, expected) < .08f,
                $"The summon landed at {proxy.anchoredPosition} after reflow, expected {expected}.");
            Require(Vector2.Distance(arrival.Readout.anchoredPosition, proxy.anchoredPosition) < .08f &&
                Vector2.Distance(arrival.Readout.sizeDelta, animator.GetAreaSize(slot)) < .08f,
                "The moving stats did not stay aligned with the resized summon destination.");
            RequireTravelStats(arrival.Readout, "1700", "1650", "4", defense: false);
            Require(arrival.Step.MoveNext() && arrival.Readout == null && arrival.Step.Current is IEnumerator,
                "The resized summon did not release its flight into the occupied slot.");
            var burst = (IEnumerator)arrival.Step.Current;
            Require(burst.MoveNext(), "The summon effect did not begin.");
            var sampleBurst = TestCardPresentation.SampleAnimation((IEnumerator)burst.Current);
            Reflow(new Vector2Int(1560, 736), new Rect(0, 20, 1428, 704));
            sampleBurst(.52f);
            var effect = animator.GetComponentsInChildren<DuelEffectGraphic>().Single(graphic => graphic.Kind == DuelEffectKind.Summon);
            Require(Vector2.Distance(effect.rectTransform.anchoredPosition, animator.GetPoint(slot, Vector2.one * .5f)) < .08f,
                "The settled summon effect stayed at its former location after another resize.");
            sampleBurst(1f);
            Require(!burst.MoveNext() && !arrival.Step.MoveNext(), "The resized summon did not finish cleanly.");
            Require(slot.Find("Card/CardView").gameObject.activeInHierarchy &&
                slot.Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>().text == "1700",
                "The field did not take over the moving readout after reflow.");
            Debug.Log("[VERIFY DUEL RESPONSIVE FLIGHT] A queued native summon followed both cutout and viewport changes with its printed card and live stats.");
        }

        private static void VerifyCrowdedFieldAfterLayoutChange()
        {
            const uint utopia = 84013237, linkSpider = 98978921;
            uint[] codes = { BlueEyes, KaiserSeaHorse, utopia, linkSpider, KaiserSeaHorse };
            using var duel = new DuelSession();
            for (byte player = 0; player < 2; player++)
            for (uint sequence = 0; sequence < codes.Length; sequence++)
            {
                var card = CardDatabase.Get(codes[sequence]);
                bool link = card.code == linkSpider;
                duel.Players[player].mzone[sequence] = new LiveCardState
                {
                    code = card.code, controller = player, location = YgoConstants.LOCATION_MZONE, sequence = sequence,
                    position = link ? YgoConstants.POS_FACEUP_ATTACK : YgoConstants.POS_FACEUP_DEFENSE,
                    type = card.type, level = card.level, rank = card.code == utopia ? card.level : 0,
                    link = link ? card.level : 0, linkMarker = card.link,
                    attack = card.atk, defense = card.def, baseAttack = card.atk, baseDefense = card.def,
                };
            }
            using var ui = new HandUI(duel, mobileLayout: true);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            var screen = (RectTransform)ui.Canvas.transform;
            var viewport = ui.Field.Root.transform.Find("MobileSafeArea");
            var safeArea = viewport.GetComponent<MobileSafeArea>();
            screen.sizeDelta = new Vector2(2220f, 900f);
            safeArea.Reflow(new Rect(150, 20, 2070, 855), new Vector2Int(2220, 900));
            Canvas.ForceUpdateCanvases();
            ui.Field.Refresh();
            var board = viewport.Find("DuelBoard");
            var focused = (RectTransform)board.Find("Zone_4_3");
            focused.GetComponent<CardDragTrigger>().OnPointerEnter(new PointerEventData(EventSystem.current) { pointerId = -1 });
            typeof(DuelFieldUI).GetMethod("AdvanceDuelPadCardMotions", PrivateInstance).Invoke(ui.Field, new object[] { .25f });
            var focusArt = (RectTransform)focused.Find("Card/CardView/AspectBox");
            Require(focusArt.localScale.x > 1f, "The occupied field did not retain focus during the layout fixture.");

            screen.sizeDelta = new Vector2(1300f, 890f);
            safeArea.Reflow(new Rect(0, 32, 1190, 838), new Vector2Int(1300, 890));
            Canvas.ForceUpdateCanvases();
            ui.Field.Update();
            Canvas.ForceUpdateCanvases();

            foreach (var cards in duel.Players.Select(player => player.mzone))
            foreach (var live in cards.Where(card => card != null))
            {
                var slot = (RectTransform)FieldSlot(ui.Field, live);
                var art = (RectTransform)slot.Find("Card/CardView/AspectBox");
                var pose = animator.GetVisualPose(art);
                float radians = pose.Angle * Mathf.Deg2Rad;
                float visibleWidth = Mathf.Abs(Mathf.Cos(radians)) * pose.Size.x + Mathf.Abs(Mathf.Sin(radians)) * pose.Size.y;
                Require(visibleWidth <= animator.GetAreaSize(slot).x + .1f,
                    $"The resized defense card {live.code} covered a neighboring slot.");
                var plate = slot.Find("CardReadout");
                Require(plate.Find("Combat/Attack/Digits/Value").GetComponent<Text>().text == live.attack.ToString() &&
                    plate.Find("Labels/Rating/Value").GetComponent<Text>().text == (live.link > 0 ? live.link : live.rank > 0 ? live.rank : live.level).ToString(),
                    "A live number or rating was lost in the crowded field update.");
            }
            Require(focusArt.localScale.x > 1f, "Reflow dropped the focused defensive card.");
            Debug.Log("[VERIFY DUEL RESPONSIVE FIELD] Ten occupied slots kept their art and live numbers after a narrow layout and focus change.");
        }

        private static void VerifyNativePositionGroup()
        {
            const uint doubleSummon = 43422537, earthquake = 82828051, eclipse = 35480699, spark = 81777047, sparkman = 20721928;
            var main = Enumerable.Repeat(KaiserSeaHorse, 16).Concat(Enumerable.Repeat(doubleSummon, 6))
                .Concat(Enumerable.Repeat(earthquake, 6)).Concat(Enumerable.Repeat(eclipse, 6)).Concat(Enumerable.Repeat(spark, 6)).ToArray();
            var enemy = Enumerable.Repeat(KaiserSeaHorse, 20).Concat(Enumerable.Repeat(sparkman, 12))
                .Concat(Enumerable.Repeat(doubleSummon, 8)).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();

            void PutPair(DuelSession session, uint first, uint second)
            {
                ActivateFromMainPhase(session, doubleSummon);
                YgoProjectBuilder.DriveUntilIdle(session, ai);
                foreach (uint code in new[] { first, second })
                {
                    int index = session.ActivePrompt.summonable.FindIndex(card => card.code == code);
                    Require(index >= 0, "The position fixture could not Normal Summon its monster.");
                    session.SubmitResponseInt(index << 16);
                    YgoProjectBuilder.DriveUntilIdle(session, ai);
                }
                session.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(session, ai);
            }

            using var duel = YgoProjectBuilder.StartSeedMatching("Group position changes", main, empty, enemy, empty, true, session =>
            {
                var mine = session.Players[session.HumanPlayerIndex].hand;
                var theirs = session.Players[session.AiPlayerIndex].hand;
                if (mine.Count(card => card.code == KaiserSeaHorse) < 2 || !mine.Exists(card => card.code == doubleSummon) ||
                    !new[] { KaiserSeaHorse, sparkman, doubleSummon }.All(code => theirs.Exists(card => card.code == code)))
                    return false;
                PutPair(session, KaiserSeaHorse, KaiserSeaHorse);
                PutPair(session, KaiserSeaHorse, sparkman);
                return session.ActivePrompt?.playerId == session.HumanPlayerIndex &&
                    new[] { earthquake, eclipse, spark }.All(code =>
                        session.Players[session.HumanPlayerIndex].hand.Exists(card => card.code == code));
            });
            ActivateFromMainPhase(duel, spark);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            var sources = duel.Players.SelectMany(player => player.mzone).Where(card => card != null).ToArray();
            Require(sources.Length == 4 && sources.All(card => card.position == YgoConstants.POS_FACEUP_ATTACK),
                "The position fixture did not set up four public attacking monsters.");
            var focused = sources.Single(card => card.code == sparkman);
            Require(focused.attack == 2100 && focused.defense == 1000, "Luminous Spark did not alter the inspected monster's live values.");
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            FieldSlot(ui.Field, focused).GetComponent<CardDragTrigger>().OnRightClick();
            ui.Field.Root.transform.Find("LeftInspector/CardDetails/ArtworkControls/Details/B").GetComponent<Button>().onClick.Invoke();
            Require(ui.Field.IsReadingCardDetails, "The grouped position fixture did not open the inspected card.");
            var moves = new List<CardMoveEvent>();
            duel.OnCardMoved += move => { if (move.ChangesPositionInPlace) moves.Add(move); };
            var steps = AnimationSteps(animator);

            void CheckInspector(string attack, string defense, bool defending)
            {
                var root = ui.Field.Root.transform;
                foreach (string path in new[] { "LeftInspector/CardDetails/CombatStats", "Overlay_CardZoom/ZoomWin/Info/CombatStats" })
                {
                    var stats = root.Find(path);
                    Require(stats.Find("Attack/Digits/Value").GetComponent<Text>().text == attack &&
                        stats.Find("Defense/Digits/Value").GetComponent<Text>().text == defense &&
                        Defending(stats) == defending, "The selected card's details did not follow the visible face and stance.");
                }
            }

            (IEnumerator Step, Action<float> Sample, CompositeCardView[] Cards, RectTransform[] Plates)
                BeginGroup(int count, uint from, uint to)
            {
                Require(moves.Count == count && moves[0].PresentationGroup != null &&
                    moves.All(move => ReferenceEquals(move.PresentationGroup, moves[0].PresentationGroup) &&
                        move.fromPos == from && move.toPos == to), "The engine did not identify one simultaneous position change.");
                ui.Field.Refresh();
                foreach (var move in moves)
                {
                    var slot = FieldSlot(ui.Field, sources.Single(card => card.controller == move.fromCon && card.sequence == move.fromSeq));
                    var card = slot.Find("Card/CardView").GetComponent<CompositeCardView>();
                    Require(card.gameObject.activeInHierarchy && card.CurrentCode == ((from & YgoConstants.POS_FACEUP) != 0 ? move.code : 0),
                        "A position change was visible on the field before the shared animation started.");
                }
                var step = steps.Single(cue => cue.GetType().Name.StartsWith("<PlayPositionChanges>", StringComparison.Ordinal));
                Require(step.MoveNext() && step.Current is IEnumerator, "The shared position motion did not start.");
                var sample = TestCardPresentation.SampleAnimation((IEnumerator)step.Current);
                var cards = animator.GetComponentsInChildren<CompositeCardView>().Where(card => card.name == "MovingCard").ToArray();
                var plates = animator.transform.Cast<RectTransform>().Where(panel => panel.name == "MovingCardReadout").ToArray();
                Require(cards.Length == count && plates.Length == count &&
                    plates.All(panel => cards.Count(card => ((RectTransform)card.transform).anchoredPosition == panel.anchoredPosition) == 1),
                    "A monster or its readout was missing or attached to the wrong motion.");
                return (step, sample, cards, plates);
            }

            void Complete((IEnumerator Step, Action<float> Sample, CompositeCardView[] Cards, RectTransform[] Plates) change)
            {
                change.Sample(1f);
                Require(!change.Step.MoveNext() && change.Cards.All(card => !card.gameObject.activeInHierarchy) &&
                    change.Plates.All(plate => plate == null), "The shared position motion left a duplicate card or readout.");
                steps.Clear();
                moves.Clear();
            }

            static bool Defending(Transform stats) =>
                stats.Find("Defense/Rail").GetComponent<Image>().color.a > stats.Find("Attack/Rail").GetComponent<Image>().color.a;

            ActivateFromMainPhase(duel, earthquake);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            var turn = BeginGroup(4, YgoConstants.POS_FACEUP_ATTACK, YgoConstants.POS_FACEUP_DEFENSE);
            CheckInspector("2100", "1000", false);
            turn.Sample(.46f);
            Require(turn.Plates.All(plate => !Defending(plate.Find("CardReadout/Combat"))), "The stance highlight switched before the shared rotation.");
            turn.Sample(.62f);
            CheckInspector("2100", "1000", true);
            Require(turn.Plates.All(plate => Defending(plate.Find("CardReadout/Combat"))), "The stance highlight missed the shared rotation.");
            Complete(turn);

            ActivateFromMainPhase(duel, eclipse);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            var cover = BeginGroup(4, YgoConstants.POS_FACEUP_DEFENSE, YgoConstants.POS_FACEDOWN_DEFENSE);
            cover.Sample(.43f);
            CheckInspector("2100", "1000", true);
            cover.Sample(.5f);
            Require(cover.Cards.All(card => card.CurrentCode == 0) &&
                cover.Plates.All(plate => !plate.Find("CardReadout").gameObject.activeInHierarchy),
                "A concealed monster or one of its stats was still public during the group flip.");
            CheckInspector("1600", "1400", false);
            Complete(cover);

            var draws = new List<int>();
            duel.OnCardsDrawn += (controller, cards) => { if (controller == duel.AiPlayerIndex) draws.Add(cards.Count); };
            duel.SubmitResponseInt(7);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            var reveal = BeginGroup(2, YgoConstants.POS_FACEDOWN_DEFENSE, YgoConstants.POS_FACEUP_DEFENSE);
            reveal.Sample(.49f);
            CheckInspector("1600", "1400", false);
            Require(reveal.Cards.All(card => card.CurrentCode == 0) &&
                reveal.Plates.All(plate => !plate.Find("CardReadout").gameObject.activeInHierarchy),
                "A set opponent monster's live information appeared before it was revealed.");
            reveal.Sample(.78f);
            CheckInspector("2100", "1000", true);
            foreach (var move in moves)
            {
                Require(move.SourceReadout == null && move.DestinationReadout?.code == move.code &&
                    move.DestinationReadout.position == YgoConstants.POS_FACEUP_DEFENSE, "A reveal did not carry its frozen new public values.");
                var card = reveal.Cards.Single(view => view.CurrentCode == move.code);
                var readout = reveal.Plates.Single(panel => panel.anchoredPosition == ((RectTransform)card.transform).anchoredPosition);
                var before = sources.Single(source => source.code == move.code && source.controller == duel.AiPlayerIndex);
                RequireTravelStats(readout, before.attack.ToString(), before.defense.ToString(), "4", defense: true);
            }
            var queued = steps.ToArray();
            Require(draws.Contains(2) && Array.FindIndex(queued, cue =>
                    cue.GetType().Name.StartsWith("<FlyCards>", StringComparison.Ordinal)) > Array.IndexOf(queued, reveal.Step),
                "Book of Eclipse drew before both monsters were revealed.");
            Complete(reveal);
            Require(duel.RetryCount == 0 && duel.Players[duel.HumanPlayerIndex].mzone.Count(card => card != null && !card.IsFaceUp) == 2,
                "The end-phase reveal modified the wrong duelist's set monsters.");
            Debug.Log("[VERIFY DUEL POSITION GROUP] Earthquake rotated four public monsters, Eclipse concealed all four together, then revealed two with live values before the draw.");
        }

        private static void VerifyNativeTemporaryBanishment()
        {
            const uint littleKnight = 29301450, doubleSummon = 43422537, darkZone = 18161786, axe = 40619825;
            const uint goblin = 70368879, earthquake = 82828051;
            var main = Enumerable.Repeat(KaiserSeaHorse, 16).Concat(Enumerable.Repeat(doubleSummon, 8))
                .Concat(Enumerable.Repeat(darkZone, 8)).Concat(Enumerable.Repeat(axe, 8)).ToArray();
            var enemy = Enumerable.Repeat(KaiserSeaHorse, 10).Concat(Enumerable.Repeat(PotOfDesires, 10))
                .Concat(Enumerable.Repeat(earthquake, 10)).Concat(Enumerable.Repeat(goblin, 10)).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Temporary banishment", main, new[] { littleKnight }, enemy,
                Array.Empty<uint>(), true, session =>
                {
                    var mine = session.Players[session.HumanPlayerIndex].hand;
                    var theirs = session.Players[session.AiPlayerIndex].hand;
                    return mine.Count(card => card.code == KaiserSeaHorse) >= 2 &&
                        new[] { doubleSummon, darkZone, axe }.All(code => mine.Exists(card => card.code == code)) &&
                        new[] { KaiserSeaHorse, earthquake, goblin, PotOfDesires }.All(code => theirs.Exists(card => card.code == code));
                });
            void ReachIdle(Func<DuelPrompt, byte[]> choose = null)
            {
                YgoProjectBuilder.DriveUntilIdle(duel, ai, choose);
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    $"The temporary-banishment fixture stopped at prompt {duel.ActivePrompt?.messageType}.");
            }
            void Activate(uint code, Func<DuelPrompt, byte[]> choose = null)
            {
                ActivateFromMainPhase(duel, code);
                ReachIdle(choose);
            }
            void SummonKaiser()
            {
                int index = duel.ActivePrompt.summonable.FindIndex(card => card.code == KaiserSeaHorse);
                Require(index >= 0, "The temporary-banishment fixture could not summon Kaiser Sea Horse.");
                duel.SubmitResponseInt(index << 16);
                ReachIdle();
            }

            Activate(doubleSummon);
            SummonKaiser();
            SummonKaiser();
            int summon = duel.ActivePrompt.spSummonable.FindIndex(card => card.code == littleKnight);
            Require(summon >= 0, "The two effect monsters did not offer the Link Summon.");
            duel.SubmitResponseInt((summon << 16) | 1);
            ReachIdle(prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.candidates.Count == 2
                ? DuelSession.PackCardSelection(0, 1) : null);
            Activate(darkZone);
            Activate(axe, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD ? SelectCard(prompt, littleKnight) : null);
            duel.SubmitResponseInt(7);
            ReachIdle();
            SummonKaiser();
            Activate(earthquake);
            Activate(PotOfDesires);

            var mineOnField = duel.Players[duel.HumanPlayerIndex];
            var theirsOnField = duel.Players[duel.AiPlayerIndex];
            var knight = mineOnField.mzone.Single(card => card?.code == littleKnight);
            var kaiser = theirsOnField.mzone.Single(card => card?.code == KaiserSeaHorse);
            Require(knight.attack == 3100 && kaiser.IsDefense && theirsOnField.banished.Count == 10 &&
                theirsOnField.banished.All(card => !card.IsFaceUp), "The banish fixture did not establish its live and hidden cards.");
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            var knightSlot = FieldSlot(ui.Field, knight);
            var kaiserSlot = FieldSlot(ui.Field, kaiser);
            knightSlot.GetComponent<CardDragTrigger>().OnRightClick();
            var inspected = ui.Field.Root.transform.Find("LeftInspector/CardDetails/CombatStats/Attack/Digits/Value").GetComponent<Text>();
            Transform Pile(string name)
            {
                var tile = typeof(DuelFieldUI).GetField(name, PrivateInstance).GetValue(ui.Field);
                return (Transform)tile.GetType().GetField("root").GetValue(tile);
            }
            var ownPile = Pile("_myBanTile");
            var rivalPile = Pile("_oppBanTile");
            void CheckPile(Transform pile, int count, uint top)
            {
                var card = pile.Find("TopCard/CardView").GetComponent<CompositeCardView>();
                Require(pile.Find("Total/Amount/Count").GetComponent<Text>().text == count.ToString() &&
                    card.gameObject.activeSelf == (count > 0) && (count == 0 || card.CurrentCode == top),
                    $"The banished pile had the wrong cards while {count} should have been present.");
            }
            void CheckRivalBrowser(int hidden, bool showKaiser)
            {
                rivalPile.GetComponent<Button>().onClick.Invoke();
                var grid = (RectTransform)typeof(DuelPromptModals).GetField("_zoneGrid", PrivateInstance).GetValue(ui.Modals);
                var cards = grid.GetComponentsInChildren<CompositeCardView>();
                Require(ui.Modals.IsZoneViewerOpen && cards.Length == hidden + (showKaiser ? 1 : 0) &&
                    cards.Count(card => card.CurrentCode == 0) == hidden &&
                    cards.Any(card => card.CurrentCode == KaiserSeaHorse) == showKaiser,
                    "The banished list skipped a presented card or exposed a hidden card.");
                ui.Modals.CloseZoneViewer();
            }
            var departures = new List<CardMoveEvent>();
            var returns = new List<CardMoveEvent>();
            duel.OnCardMoved += move =>
            {
                if (move.TemporarilyBanished) departures.Add(move);
                if (move.ReturnEffectId.HasValue) returns.Add(move);
            };
            Activate(goblin, prompt =>
            {
                if (prompt.playerId == duel.HumanPlayerIndex && prompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
                {
                    int chain = prompt.candidates.FindIndex(card => card.code == littleKnight);
                    if (chain >= 0) return BitConverter.GetBytes(chain);
                }
                if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == littleKnight)
                    return BitConverter.GetBytes(1);
                if (prompt.messageType == YgoConstants.MSG_SELECT_CARD)
                    return DuelSession.PackCardSelection(0, 1);
                return null;
            });
            Require(departures.Count == 2 && departures[0].PresentationGroup != null &&
                departures.All(move => ReferenceEquals(move.PresentationGroup, departures[0].PresentationGroup)),
                "Little Knight's two removals were not one native group.");
            ui.Field.Refresh();
            CheckPile(ownPile, 0, 0);
            CheckPile(rivalPile, 10, 0);
            var steps = AnimationSteps(animator);
            var banish = steps.Single(step => step.GetType().Name.StartsWith("<PlayFieldDepartures>", StringComparison.Ordinal));
            Require(banish.MoveNext() && banish.Current is IEnumerator, "The temporary removal did not start its shared cue.");
            TestCardPresentation.SampleAnimation((IEnumerator)banish.Current)(1f);
            Require(knightSlot.GetComponentInChildren<CompositeCardView>()?.CurrentCode == littleKnight &&
                kaiserSlot.GetComponentInChildren<CompositeCardView>()?.CurrentCode == KaiserSeaHorse,
                "A monster left before the exile cue.");
            Require(banish.MoveNext() && banish.Current is IEnumerator, "Both cards did not enter the removal flight.");
            var flight = TestCardPresentation.SampleAnimation((IEnumerator)banish.Current);
            flight(.25f);
            CheckMovingNumbers("3100");
            Require(inspected.text == "3100", "Little Knight lost its inspected live numbers before reaching the pile.");
            CheckPile(ownPile, 0, 0);
            CheckPile(rivalPile, 10, 0);
            flight(1f);
            Require(!banish.MoveNext(), "The temporary removal did not finish.");
            CheckPile(ownPile, 1, littleKnight);
            CheckPile(rivalPile, 11, KaiserSeaHorse);
            steps.Clear();
            CheckRivalBrowser(10, showKaiser: true);

            bool checkedBetweenPlacements = false;
            Text zoomAttack = null;
            duel.SubmitResponseInt(7);
            ReachIdle(prompt =>
            {
                if (prompt.messageType != YgoConstants.MSG_SELECT_PLACE) return null;
                if (returns.Count == 1)
                {
                    Require(animator.IsFieldArrivalReserved(returns[0].toCon, returns[0].toLoc, returns[0].toSeq) &&
                        steps.All(step => !step.GetType().Name.StartsWith("<PlayFieldArrivalGroup>", StringComparison.Ordinal)),
                        "One returning card landed before the next placement choice.");
                    steps.Clear();
                    ui.Field.Refresh();
                    CheckPile(rivalPile, 11, KaiserSeaHorse);
                    CheckRivalBrowser(10, showKaiser: true);
                    ui.Field.Root.transform.Find("LeftInspector/CardDetails/ArtworkControls/Details/B").GetComponent<Button>().onClick.Invoke();
                    Require(ui.Field.IsReadingCardDetails, "The inspected banished card's details could not open.");
                    zoomAttack = ui.Field.Root.transform.Find("Overlay_CardZoom/ZoomWin/Info/CombatStats/Attack/Digits/Value").GetComponent<Text>();
                    checkedBetweenPlacements = true;
                }
                uint sequence = prompt.playerId == duel.HumanPlayerIndex ? 4u : 1u;
                Require((prompt.placeFlag & (1u << (int)sequence)) == 0, "The desired new return zone was unavailable.");
                return new[] { prompt.playerId, (byte)YgoConstants.LOCATION_MZONE, (byte)sequence };
            });
            Require(checkedBetweenPlacements && returns.Count == 2 && returns[0].ReturnEffectId > 0 &&
                returns.All(move => move.ReturnEffectId == returns[0].ReturnEffectId && !move.summonType.HasValue &&
                    move.TemporaryBanishCardId.HasValue && departures.Any(outbound => outbound.code == move.code &&
                        outbound.TemporaryBanishCardId == move.TemporaryBanishCardId)),
                "The true return metadata did not match both placements.");
            ui.Field.Refresh();
            CheckPile(ownPile, 1, littleKnight);
            CheckPile(rivalPile, 11, KaiserSeaHorse);
            var arrival = steps.Single(step => step.GetType().Name.StartsWith("<PlayFieldArrivalGroup>", StringComparison.Ordinal));
            Require(arrival.MoveNext() && arrival.Current is IEnumerator &&
                animator.GetComponentsInChildren<DuelEffectGraphic>().Count(effect => effect.Kind == DuelEffectKind.BanishTransit) == 2,
                "Returning monsters used a summon animation or did not enter together.");
            CheckPile(ownPile, 0, 0);
            CheckPile(rivalPile, 10, 0);
            var comingBack = TestCardPresentation.SampleAnimation((IEnumerator)arrival.Current);
            comingBack(.4f);
            Require(inspected.text == "1600" && zoomAttack.text == "1600",
                "The live numbers in the details changed before the inspected copy returned.");
            Require(animator.transform.Cast<RectTransform>().Where(rect => rect.name == "MovingCardReadout")
                .All(rect => !rect.Find("CardReadout").gameObject.activeInHierarchy),
                "A returned monster displayed field stats while still leaving the banished pile.");
            comingBack(.88f);
            CheckMovingNumbers("2100");
            Require(inspected.text == "2100" && zoomAttack.text == "2100",
                "The selected copy's current values failed to reach the inspector and details together.");
            comingBack(1f);
            Require(!arrival.MoveNext() && mineOnField.mzone[4]?.code == littleKnight && theirsOnField.mzone[1]?.IsDefense == true,
                "The cards did not finish in their selected slots and original stance.");
            steps.Clear();
            ui.Field.HandleDuelPad(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Cancel));
            CheckRivalBrowser(10, showKaiser: false);
            Debug.Log("[VERIFY DUEL TEMPORARY BANISH] S:P left with 3100 ATK and returned with 2100. Both arrivals shared a cue, and the ten concealed cards remained private.");

            void CheckMovingNumbers(string knightAttack)
            {
                var cards = animator.GetComponentsInChildren<CompositeCardView>().Where(view => view.name == "MovingCard").ToArray();
                var panels = animator.transform.Cast<RectTransform>().Where(rect => rect.name == "MovingCardReadout").ToArray();
                Require(cards.Length == 2 && panels.Length == 2, "The two cards did not travel with separate readouts.");
                RectTransform Panel(uint code)
                {
                    var card = cards.Single(view => view.CurrentCode == code);
                    return panels.Single(panel => panel.anchoredPosition == ((RectTransform)card.transform).anchoredPosition);
                }
                var link = Panel(littleKnight).Find("CardReadout");
                Require(link.Find("Combat/Attack/Digits/Value").GetComponent<Text>().text == knightAttack &&
                    !link.Find("Combat/Defense").gameObject.activeSelf &&
                    link.Find("Labels/Rating/Value").GetComponent<Text>().text == "2",
                    "Little Knight did not show its current ATK and Link rating during motion.");
                RequireTravelStats(Panel(KaiserSeaHorse), "1700", "1650", "4", defense: true);
            }
        }

        private static void VerifyNativeTokenArrivalReadout()
        {
            const uint scapegoat = 73915051, sheepToken = 73915052, gaiaPower = 56594520;
            var main = Enumerable.Repeat(scapegoat, 20).Concat(Enumerable.Repeat(gaiaPower, 20)).ToArray();
            var rival = Enumerable.Repeat(BlueEyes, 40).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Animated token readout", main, empty, rival, empty, true,
                session =>
                {
                    YgoProjectBuilder.DriveUntilIdle(session, ai);
                    return session.Players[session.HumanPlayerIndex].hand.Any(c => c.code == scapegoat) &&
                        session.ActivePrompt?.activatable.Any(c => c.code == gaiaPower) == true;
                });
            ActivateFromMainPhase(duel, gaiaPower);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            var moves = new List<CardMoveEvent>();
            duel.OnCardMoved += move => { if (move.summonType.HasValue) moves.Add(move); };
            ActivateFromMainPhase(duel, scapegoat);
            int reserved = 0;
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                reserved = Math.Max(reserved, VerifySummonReservations(ui.Field, animator, prompt));
                return null;
            });
            var tokens = PendingMoveReadouts(ui.Field).Values.OrderBy(move => move.Code).ToArray();
            ui.Field.Refresh();
            Require(moves.Count == 4 && tokens.Select(move => move.Code).SequenceEqual(
                        new[] { sheepToken, sheepToken + 1, sheepToken + 2, sheepToken + 3 }) &&
                    tokens.All(move => move.DestinationCard?.attack == 500 && move.DestinationCard.defense == 0 &&
                        move.DestinationCard.level == 1 && move.DestinationCard.IsDefense) &&
                    reserved > 0 && duel.RetryCount == 0,
                "One of the four native Sheep Tokens missed its live values or interrupted placement of the other tokens.");
            VerifySpecialSummonGroup(ui.Field, animator, tokens, DuelEffectKind.Token);
            Debug.Log("[VERIFY DUEL GROUP SUMMON] Four Sheep Tokens kept the native placement prompts and arrived together with 500 ATK, 0 DEF and Level 1.");
        }

        private static void VerifyPendulumScalePair()
        {
            const uint oddEyes = 16178681;
            using var duel = new DuelSession();
            byte player = (byte)duel.HumanPlayerIndex;
            var material = new LiveCardState
            {
                code = oddEyes, controller = player, location = YgoConstants.LOCATION_MZONE, sequence = 2,
                type = CardDatabase.Get(oddEyes).type, level = 7, lscale = 0, rscale = 12,
                position = YgoConstants.POS_FACEUP_ATTACK, attack = 2500, defense = 2000,
                baseAttack = 2500, baseDefense = 2000,
            };
            duel.Players[player].mzone[material.sequence] = material;
            using var ui = new HandUI(duel, mobileLayout: true);
            FieldSlot(ui.Field, material).GetComponent<CardDragTrigger>().OnRightClick();
            var root = ui.Field.Root.transform;
            var sidebar = root.Find("MobileSafeArea/LeftInspector/CardDetails");
            root.Find("MobileSafeArea/LeftInspector/CardPreview/Card/AspectBox/ZoomHit").GetComponent<Button>().onClick.Invoke();
            var window = root.Find("Overlay_CardZoom/MobileSafeArea/ZoomWin");
            var details = window.Find("Info");
            var panels = new[] { sidebar, details };
            Text Scale(Transform panel, string side) => panel.Find($"Scales/{side}/Number").GetComponent<Text>();
            Require(panels.All(panel => Scale(panel, "Left").text == "0" && Scale(panel, "Right").text == "12" &&
                    Scale(panel, "Left").gameObject.activeInHierarchy && Scale(panel, "Right").gameObject.activeInHierarchy &&
                    panel.Find("Stats/Rating/Number").GetComponent<Text>().text == "7"),
                "The two Pendulum amounts were exchanged, hidden at zero or substituted for the monster's level.");

            var viewport = (RectTransform)ui.Canvas.transform;
            foreach (var size in new[] { new Vector2(1740f, 805f), new Vector2(1200f, 680f) })
            {
                viewport.sizeDelta = size;
                Canvas.ForceUpdateCanvases();
                foreach (var panel in panels)
                {
                    var row = RectTransformUtility.CalculateRelativeRectTransformBounds(panel, panel.Find("Scales"));
                    var combat = RectTransformUtility.CalculateRelativeRectTransformBounds(panel, panel.Find("CombatStats"));
                    var description = RectTransformUtility.CalculateRelativeRectTransformBounds(panel,
                        panel.Find(panel == sidebar ? "Description" : "DescBox"));
                    Require(description.max.y < row.min.y && row.max.y < combat.min.y,
                        $"The Pendulum scales covered the combat values or effect text at {size}.");
                }
            }

            material.rscale = 9;
            ui.Field.Refresh();
            Require(panels.All(panel => Scale(panel, "Left").text == "0" &&
                    Scale(panel, "Left").transform.parent.Find("ChangePulse") == null &&
                    Scale(panel, "Right").text == "9" &&
                    Scale(panel, "Right").transform.parent.Find("ChangePulse")?.gameObject.activeInHierarchy == true),
                "An independent right-scale change moved or replaced the left scale.");
            window.Find("Hdr/Close/B").GetComponent<Button>().onClick.Invoke();
            ui.Field.InspectCard(KaiserSeaHorse);
            Require(!sidebar.Find("Scales").gameObject.activeSelf &&
                ((RectTransform)sidebar.Find("Description")).anchorMax.y == .275f,
                "Inspecting a non-Pendulum card kept the previous scale row or shortened the effect area.");
            Debug.Log("[VERIFY PENDULUM SCALE UI] Asymmetric amounts, zero, independent changes and view bounds passed.");
        }

        private static void VerifyNativeChangingPendulumScale(uint sequence)
        {
            const uint kepler = 11609969;
            var ai = new TacticalAI();
            var empty = Array.Empty<uint>();
            using var duel = YgoProjectBuilder.StartSeedMatching("Changing Pendulum scale",
                Enumerable.Repeat(kepler, 40).ToArray(), empty, Enumerable.Repeat(BlueEyes, 40).ToArray(), empty,
                true, session => session.ActivePrompt?.activatable.Exists(card => card.code == kepler) == true);
            var player = duel.Players[duel.HumanPlayerIndex];
            byte[] ChoosePlacementOrEffect(DuelPrompt prompt)
            {
                if (prompt.messageType == YgoConstants.MSG_SELECT_PLACE && prompt.playerId == duel.HumanPlayerIndex)
                {
                    Require((prompt.placeFlag & (1u << (8 + (int)sequence))) == 0,
                        "The requested Pendulum Zone was not available.");
                    return new[] { (byte)duel.HumanPlayerIndex, (byte)YgoConstants.LOCATION_SZONE, (byte)sequence };
                }
                if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && prompt.forced) return BitConverter.GetBytes(0);
                return prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == kepler
                    ? BitConverter.GetBytes(1) : null;
            }

            bool right = sequence == 4;
            uint Value() => right ? player.szone[sequence].rscale : player.szone[sequence].lscale;
            ActivateFromMainPhase(duel, kepler);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, ChoosePlacementOrEffect);
            Require(player.szone[sequence]?.code == kepler && Value() == 10,
                "Kepler did not enter the specified Pendulum Zone at scale 10.");

            using var ui = new HandUI(duel, mobileLayout: true);
            var slot = FieldSlot(ui.Field, player.szone[sequence]);
            slot.GetComponent<CardDragTrigger>().OnRightClick();
            var root = ui.Field.Root.transform;
            var inspector = root.Find("MobileSafeArea/LeftInspector/CardDetails");
            root.Find("MobileSafeArea/LeftInspector/CardPreview/Card/AspectBox/ZoomHit").GetComponent<Button>().onClick.Invoke();
            var details = root.Find("Overlay_CardZoom/MobileSafeArea/ZoomWin/Info");
            var field = slot.Find("CardReadout/Labels/Rating/Value").GetComponent<Text>();
            var preview = inspector.Find("Stats/Rating/Number").GetComponent<Text>();
            var zoom = details.Find("Stats/Rating/Number").GetComponent<Text>();
            Require(details.Find("Stats/Rating/Kind/Label").GetComponent<Text>().text == (right ? "오른쪽" : "왼쪽") &&
                !details.Find("Stats/Rating/Printed").gameObject.activeSelf &&
                !inspector.Find("Scales").gameObject.activeSelf && !details.Find("Scales").gameObject.activeSelf &&
                !details.Find("CombatStats").gameObject.activeSelf &&
                new[] { field, preview, zoom }.All(label => label.text == "10" && label.color == zoom.color),
                "A Pendulum Spell did not show its own side's scale alone and in the same color.");

            for (int turns = 0; turns < 2 && Value() == 10; turns++)
            {
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD,
                    "The scale fixture did not reach the next Main Phase.");
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai, ChoosePlacementOrEffect);
            }
            ui.Field.Refresh();
            Require(duel.RetryCount == 0 && Value() == 8 &&
                new[] { field, preview, zoom }.All(label => label.text == "8" &&
                    label.transform.parent.Find("ChangePulse")?.gameObject.activeInHierarchy == true),
                "The native scale reduction did not reach the field and both inspected readings.");
            foreach (var label in new[] { field, preview, zoom })
                label.transform.parent.GetComponent<DuelReadoutPulse>().Advance(1f);
            var printed = details.Find("Stats/Rating/Printed/Label").GetComponent<Text>();
            Require(new[] { field, preview, zoom }.All(label => label.color == DuelStyle.DecreasedValue) &&
                    printed.text == "기본 10" && printed.gameObject.activeInHierarchy &&
                    details.Find("Stats/Rating/Kind/Label").GetComponent<Text>().text == (right ? "오른쪽" : "왼쪽"),
                "A reduced scale lost its original value or side after the animation.");
            Debug.Log($"[VERIFY PENDULUM SCALE UI] Kepler's {(right ? "right" : "left")} scale changed from 10 to 8 in all views.");
        }

        private static void VerifyNativePendulumArrivalReadout()
        {
            const uint lowScale = 15146890, highScale = 51531505, sparkman = 20721928, linkSpider = 98978921;
            var main = Enumerable.Repeat(lowScale, 15).Concat(Enumerable.Repeat(highScale, 10))
                .Concat(Enumerable.Repeat(sparkman, 15)).ToArray();
            var opponent = Enumerable.Repeat(BlueEyes, 40).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Pendulum arrival", main, new[] { linkSpider },
                opponent, Array.Empty<uint>(), true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return session.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
                        hand.Count(card => card.code == lowScale) >= 2 && hand.Any(card => card.code == highScale) &&
                        hand.Any(card => card.code == sparkman);
                });
            var board = duel.Players[duel.HumanPlayerIndex];
            int normal = duel.ActivePrompt.summonable.FindIndex(card => card.code == lowScale);
            Require(normal >= 0, "The fixture could not put a Pendulum Monster on the field.");
            duel.SubmitResponseInt(normal << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            int link = duel.ActivePrompt.spSummonable.FindIndex(card => card.code == linkSpider);
            Require(link >= 0, "The fixture could not Link Summon using the low scale as material.");
            duel.SubmitResponseInt((link << 16) | 1);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD
                && prompt.min == 1 && prompt.candidates.Count == 1 ? DuelSession.PackCardSelection(0) : null);
            Require(board.extra.Any(card => card.code == lowScale && card.IsFaceUp),
                "The material did not enter the Extra Deck face-up.");
            ActivateFromMainPhase(duel, highScale);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            ActivateFromMainPhase(duel, lowScale);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);

            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            var moves = new List<CardMoveEvent>();
            duel.OnCardMoved += move => { if (move.summonType.HasValue) moves.Add(move); };
            int pendulum = duel.ActivePrompt.spSummonable.FindIndex(card => card.location == YgoConstants.LOCATION_SZONE);
            Require(pendulum >= 0, "The two scales did not offer a Pendulum Summon.");
            duel.SubmitResponseInt((pendulum << 16) | 1);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                VerifySummonReservations(ui.Field, animator, prompt);
                if (prompt.messageType != YgoConstants.MSG_SELECT_UNSELECT_CARD) return null;
                if (prompt.unselectCandidates.Count == 2) return BitConverter.GetBytes(-1);
                int index = prompt.unselectCandidates.Count == 0
                    ? prompt.candidates.FindIndex(card => card.code == lowScale && card.location == YgoConstants.LOCATION_EXTRA)
                    : prompt.candidates.FindIndex(card => card.code == sparkman && card.location == YgoConstants.LOCATION_HAND);
                Require(index >= 0, "The Pendulum selection did not offer the hand and Extra Deck sources.");
                return BitConverter.GetBytes(1).Concat(BitConverter.GetBytes(index)).ToArray();
            });
            var monsters = PendingMoveReadouts(ui.Field).Values.ToArray();
            ui.Field.Refresh();
            Require(duel.RetryCount == 0 && monsters.Length == 2 && moves.Count == 2 &&
                    moves.Any(move => move.fromLoc == YgoConstants.LOCATION_HAND && move.code == sparkman) &&
                    moves.Any(move => move.fromLoc == YgoConstants.LOCATION_EXTRA && move.code == lowScale) &&
                    moves.All(move => (move.summonType & YgoConstants.SUMMON_METHOD_MASK) == YgoConstants.SUMMON_TYPE_PENDULUM),
                "The native Pendulum Summon did not retain both source zones and its actual summon method.");
            VerifySpecialSummonGroup(ui.Field, animator, monsters, DuelEffectKind.Pendulum);
            Debug.Log("[VERIFY DUEL GROUP SUMMON] A native Pendulum Summon from the hand and the face-up Extra Deck shared one Pendulum arrival.");
        }

        private static void ActivateFromMainPhase(DuelSession duel, uint code)
        {
            Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD, "The summon fixture left the Main Phase.");
            int index = duel.ActivePrompt.activatable.FindIndex(card => card.code == code);
            Require(index >= 0, $"The summon fixture could not activate {code}.");
            duel.SubmitResponseInt((index << 16) | 5);
        }

        private static int VerifySummonReservations(DuelFieldUI field, CardAnimationManager animator, DuelPrompt prompt)
        {
            if (prompt.messageType is not (YgoConstants.MSG_SELECT_PLACE or YgoConstants.MSG_SELECT_POSITION)) return 0;
            var pending = PendingMoveReadouts(field);
            field.Refresh();
            int reserved = field.Root.GetComponentsInChildren<DuelZoneImage>().Count(zone => zone.SummonReserved);
            Require(reserved == pending.Count && pending.Values.All(move => move.DestinationCard == null) &&
                    !AnimationSteps(animator).Any(step => step.GetType().Name.StartsWith("<PlayFieldArrivalGroup>", StringComparison.Ordinal)),
                "A placement hid the next native choice or showed field numbers before the summon was ready.");
            return reserved;
        }

        private static void VerifySpecialSummonGroup(DuelFieldUI field, CardAnimationManager animator,
            IReadOnlyList<CardMoveReadoutSnapshot> arrivals, DuelEffectKind effect)
        {
            Transform Slot(LiveCardState card) => field.Root.transform.Find($"DuelBoard/Zone_{card.sequence + 2}_3");
            Require(arrivals.All(arrival => arrival.DestinationCard != null &&
                    !Slot(arrival.DestinationCard).Find("Card/CardView").gameObject.activeInHierarchy &&
                    !Slot(arrival.DestinationCard).GetComponent<DuelZoneImage>().SummonReserved),
                "A prepared summon retained a placement mark or reached the field before its moving card.");
            var wave = AnimationSteps(animator).Single(step =>
                step.GetType().Name.StartsWith("<PlayFieldArrivalGroup>", StringComparison.Ordinal));
            Require(wave.MoveNext() && wave.Current is IEnumerator, "The summon group did not share one animation.");
            var sample = TestCardPresentation.SampleAnimation((IEnumerator)wave.Current);
            var views = animator.GetComponentsInChildren<CompositeCardView>().Where(card => card.name == "MovingCard").ToArray();
            var readouts = animator.transform.Cast<RectTransform>().Where(rect => rect.name == "MovingCardReadout").ToArray();
            Require(views.Length == arrivals.Count && readouts.Length == arrivals.Count &&
                    animator.GetComponentsInChildren<DuelEffectGraphic>().Count(graphic => graphic.Kind == effect) == arrivals.Count &&
                    readouts.All(readout => !readout.Find("CardReadout").gameObject.activeInHierarchy),
                "A grouped summon omitted a card or revealed its stats too soon.");
            sample(.82f);
            for (int i = 0; i < views.Length; i++)
            {
                var card = arrivals.Single(arrival => arrival.Code == views[i].CurrentCode).DestinationCard;
                RequireTravelStats(readouts[i], card.attack.ToString(), card.defense.ToString(), card.level.ToString(), card.IsDefense);
                Require(readouts[i].anchoredPosition == ((RectTransform)views[i].transform).anchoredPosition &&
                        !Slot(card).Find("Card/CardView").gameObject.activeInHierarchy,
                    "A summon showed duplicate stat values or separated the readout from its moving card.");
            }
            sample(1f);
            Require(!wave.MoveNext() && readouts.All(readout => readout == null) &&
                    arrivals.All(arrival => Slot(arrival.DestinationCard).Find("Card/CardView").gameObject.activeInHierarchy),
                "The group did not land together or left its moving readouts on screen.");
        }

        private static void VerifyNativeFieldMaterials(bool xyz)
        {
            const uint sparkman = 20721928, doubleSummon = 43422537, rush = 70046172, cowboy = 12014404, proxy = 22862454;
            uint result = xyz ? cowboy : proxy;
            var main = Enumerable.Repeat(sparkman, 24).Concat(Enumerable.Repeat(doubleSummon, 8)).Concat(Enumerable.Repeat(rush, 8)).ToArray();
            var enemy = Enumerable.Repeat(BlueEyes, 40).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Field material flights", main, new[] { result }, enemy,
                Array.Empty<uint>(), true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return hand.Count(card => card.code == sparkman) >= 2 &&
                        hand.Any(card => card.code == doubleSummon) && hand.Any(card => card.code == rush);
                });
            ActivateFromMainPhase(duel, doubleSummon);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            for (int i = 0; i < 2; i++)
            {
                Require(duel.ActivePrompt.summonable.Count != 0, "The material fixture ran out of normal summons.");
                duel.SubmitResponseInt(0);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
            }
            ActivateFromMainPhase(duel, rush);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD
                ? SelectCard(prompt, sparkman) : null);
            var player = duel.Players[duel.HumanPlayerIndex];
            var sources = player.mzone.Where(card => card?.code == sparkman).ToArray();
            Require(sources.Select(card => card.attack).OrderBy(value => value).SequenceEqual(new[] { 1600, 2300 }),
                "The two identical materials did not start with distinct live stats.");
            int summon = duel.ActivePrompt.spSummonable.FindIndex(card => card.code == result);
            Require(summon >= 0, "The two monsters did not offer the expected summon.");
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            var focused = sources.Single(card => card.attack == 2300);
            FieldSlot(ui.Field, focused).GetComponent<CardDragTrigger>().OnRightClick();
            ui.Field.Root.transform.Find("LeftInspector/CardDetails/ArtworkControls/Details/B").GetComponent<Button>().onClick.Invoke();
            Require(ui.Field.IsReadingCardDetails, "The moving material fixture did not open the selected card's details.");
            var send = new List<CardMoveEvent>();
            duel.OnCardMoved += move => { if (move.MaterialMethod != 0) send.Add(move); };
            duel.SubmitResponseInt((summon << 16) | 1);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType switch
            {
                YgoConstants.MSG_SELECT_CARD => prompt.min == 2 ? DuelSession.PackCardSelection(0, 1) : DuelSession.PackCardSelection(0),
                YgoConstants.MSG_SELECT_POSITION => BitConverter.GetBytes((int)YgoConstants.POS_FACEUP_DEFENSE),
                _ => null,
            });
            var host = player.mzone.Single(card => card?.code == result);
            Require(duel.RetryCount == 0 && (xyz ? host.overlayCodes.Count == 2 : host.link == 2),
                "The material fixture did not complete the intended summon.");
            if (!xyz) VerifyNativeSendGroup(send, YgoConstants.REASON_LINK);
            var slot = VerifyMaterialTimeline(ui.Field, animator, host, sources, count: 2, inspected: focused);
            if (xyz)
                Require(slot.Find("CardReadout/Labels/Materials/Value").GetComponent<Text>().text == "2",
                    "The Xyz monster did not land with both assembled materials.");
            else
                Require(player.grave.Count(card => card.code == sparkman) == 2,
                    "The Link materials did not reach the Graveyard as shown.");
            Debug.Log($"[VERIFY DUEL SUMMON MATERIAL] {(xyz ? "Xyz" : "Link")} kept the 1600 and 2300 copies distinct until departure and displayed the result.");
        }

        private static void VerifyNativeSynchroMaterials()
        {
            const uint moYe = 20001443, token = 20001444, wyrm = 56495147, chixiao = 69248256;
            var main = Enumerable.Repeat(moYe, 20).Concat(Enumerable.Repeat(wyrm, 20)).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Swordsoul material flights", main, new[] { chixiao },
                Enumerable.Repeat(BlueEyes, 40).ToArray(), Array.Empty<uint>(), true,
                session => session.ActivePrompt?.summonable.Exists(card => card.code == moYe) == true &&
                    session.Players[session.HumanPlayerIndex].hand.Exists(card => card.code == wyrm));
            duel.SubmitResponseInt(duel.ActivePrompt.summonable.FindIndex(card => card.code == moYe) << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType switch
            {
                YgoConstants.MSG_SELECT_EFFECTYN when prompt.promptCardCode == moYe => BitConverter.GetBytes(1),
                YgoConstants.MSG_SELECT_CHAIN => BitConverter.GetBytes(prompt.candidates.FindIndex(card => card.code == moYe)),
                YgoConstants.MSG_SELECT_CARD => SelectCard(prompt, wyrm),
                _ => null,
            });
            var player = duel.Players[duel.HumanPlayerIndex];
            var sources = player.mzone.Where(card => card != null).ToArray();
            Require(sources.Length == 2 && sources.Any(card => card.code == token && card.level == 4 && card.IsFaceUp),
                "The Synchro fixture did not create its Tuner Token.");
            int summon = duel.ActivePrompt.spSummonable.FindIndex(card => card.code == chixiao);
            Require(summon >= 0, "The Tuner Token did not offer the Synchro Summon.");
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();

            var send = new List<CardMoveEvent>();
            duel.OnCardMoved += move => { if (move.MaterialMethod != 0) send.Add(move); };
            duel.SubmitResponseInt((summon << 16) | 1);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType is YgoConstants.MSG_SELECT_SUM or YgoConstants.MSG_SELECT_CARD
                ? ai.ComputeResponse(duel, prompt) : null);
            VerifyNativeSendGroup(send, YgoConstants.REASON_SYNCHRO);
            var host = player.mzone.Single(card => card?.code == chixiao);
            Require(duel.RetryCount == 0 && host.level == 8 && send.Any(move => move.code == token && move.toLoc == 0) &&
                    player.grave.Any(card => card.code == moYe) && player.grave.All(card => card.code != token),
                "The Synchro send did not remove the Tuner Token and send Mo Ye to the Graveyard.");
            VerifyMaterialTimeline(ui.Field, animator, host, sources, count: 2, vanishes: token);
            Debug.Log("[VERIFY DUEL SUMMON MATERIAL] Synchro kept both Level 4 readouts, faded the Tuner Token and sent Mo Ye to the Graveyard.");
        }

        private static void VerifyNativeFieldSumChoice(bool setSecondMaterial)
        {
            const uint ritual = 39996157, benten = 77235086, sparkman = 20721928, doubleSummon = 43422537;
            uint[] main = Enumerable.Repeat(sparkman, 16).Concat(Enumerable.Repeat(ritual, 8))
                .Concat(Enumerable.Repeat(benten, 8)).Concat(Enumerable.Repeat(doubleSummon, 8)).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Field ritual sum", main, empty,
                Enumerable.Repeat(BlueEyes, 40).ToArray(), empty, true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return hand.Count(card => card.code == sparkman) == 2 &&
                        new[] { ritual, benten, doubleSummon }.All(code => hand.Exists(card => card.code == code));
                });
            ActivateFromMainPhase(duel, doubleSummon);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            for (int i = 0; i < 2; i++)
            {
                bool set = setSecondMaterial && i == 1;
                var choices = set ? duel.ActivePrompt.mSetable : duel.ActivePrompt.summonable;
                int place = choices.FindIndex(card => card.code == sparkman);
                Require(place >= 0, "The ritual fixture could not place its field material.");
                duel.SubmitResponseInt((place << 16) | (set ? 3 : 0));
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
            }

            ActivateFromMainPhase(duel, ritual);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, request => request.messageType == YgoConstants.MSG_SELECT_CARD
                ? SelectCard(request, benten) : null);
            var prompt = duel.ActivePrompt;
            Require(prompt?.messageType == YgoConstants.MSG_SELECT_SUM && prompt.sumExactCountZeroAny &&
                (prompt.acc & 0xFFFF) == 6 && prompt.candidates.Count == 2 &&
                prompt.candidates.All(card => card.location == YgoConstants.LOCATION_MZONE && card.sumParam == 4),
                "Machine Angel Ritual did not offer its two Level 4 field materials for the required total of 6.");

            byte[] response = null;
            using var ui = new HandUI(duel, mobileLayout: true, submitResponse: bytes => response = bytes);
            ui.Modals.ShowPrompt(duel, prompt);
            ui.Field.Refresh();
            var window = ui.Modals.ModalRoot.transform.Find("Win");
            var subtitle = window.Find("Hdr/SubtitleBand/Sub").GetComponent<Text>();
            var amount = window.Find("Body/SumAmount/Digits/Value")?.GetComponent<Text>();
            var target = window.Find("Body/SumTarget/Digits/Value")?.GetComponent<Text>();
            var amountCaption = window.Find("Body/SumAmount/Heading/Label")?.GetComponent<Text>();
            var targetCaption = window.Find("Body/SumTarget/Heading/Label")?.GetComponent<Text>();
            var confirm = window.Find("Footer/Btn_선택 완료/B").GetComponent<Button>();
            Require(ui.Modals.IsFieldCardSelection && amount?.text == "0" && target?.text == "6" &&
                amountCaption?.text == "합계" && targetCaption?.text == "최소 필요" && subtitle.text == "선택 0장" &&
                amount.font == DuelTypography.NumberFont && target.font == DuelTypography.NumberFont && !confirm.interactable,
                "The empty field selection did not show the native minimum requirement and current total.");

            var player = duel.Players[duel.HumanPlayerIndex];
            var materials = prompt.candidates.Select(card => player.mzone[card.sequence]).ToArray();
            Require(materials.Count(card => !card.IsFaceUp) == (setSecondMaterial ? 1 : 0),
                "The ritual prompt did not retain the fixture's set material.");
            var slots = materials.Select(card => FieldSlot(ui.Field, card)).ToArray();
            var amounts = slots.Select(slot => slot.Find("CardReadout/Labels/Contribution/Value")?.GetComponent<Text>()).ToArray();
            Require(amounts.All(value => value?.text == "4" && value.font == DuelTypography.NumberFont &&
                    value.gameObject.activeInHierarchy && !value.raycastTarget),
                "The legal field materials did not show the native contribution.");
            for (int i = 0; i < slots.Length; i++)
            {
                var rating = slots[i].Find("CardReadout/Labels/Rating/Value").GetComponent<Text>();
                Require(materials[i].IsFaceUp ? rating.text == "4" && rating.gameObject.activeInHierarchy :
                        !rating.gameObject.activeInHierarchy && !slots[i].Find("CardReadout/Combat").gameObject.activeSelf &&
                        slots[i].GetComponentInChildren<CompositeCardView>().CurrentCode == 0,
                    "The contribution replaced a live level or disclosed a set card's face or stats.");
            }

            void Toggle(int index) => slots[index].GetComponent<CardDragTrigger>().OnLeftClick.Invoke();
            Toggle(0);
            Require(amount.text == "4" && subtitle.text == "선택 1장" && !confirm.interactable &&
                amounts[0].color != amounts[1].color, "Selecting a material did not update the subtotal and emphasis.");
            Toggle(1);
            Require(amount.text == "8" && amount.color == DuelStyle.Gold && subtitle.text == "선택 2장" && confirm.interactable,
                "The valid combined field value did not enable completion.");
            var viewport = (RectTransform)ui.Canvas.transform;
            foreach (var size in new[] { new Vector2(1740f, 805f), new Vector2(852f, 393f) })
            {
                viewport.sizeDelta = size;
                Canvas.ForceUpdateCanvases();
                foreach (var text in new[] { subtitle, amountCaption, targetCaption })
                {
                    var generator = text.cachedTextGenerator;
                    generator.Populate(text.text, text.GetGenerationSettings(text.rectTransform.rect.size));
                    Require(generator.lineCount == 1 && generator.characterCountVisible >= text.text.Length,
                        $"The label '{text.text}' clipped at {size} ({generator.characterCountVisible} characters, {generator.lineCount} lines).");
                }
                foreach (var slot in slots)
                {
                    var level = slot.Find("CardReadout/Labels/Rating");
                    var rating = RectTransformUtility.CalculateRelativeRectTransformBounds(slot, level);
                    var contribution = RectTransformUtility.CalculateRelativeRectTransformBounds(slot, slot.Find("CardReadout/Labels/Contribution"));
                    Require((!level.gameObject.activeSelf || rating.max.x < contribution.min.x) &&
                            contribution.max.x <= ((RectTransform)slot).rect.xMax,
                        $"The material value overlapped the level or neighboring slot at {size}.");
                }
            }
            Toggle(0);
            Require(amount.text == "4" && subtitle.text == "선택 1장" && !confirm.interactable && response == null,
                "Deselecting a material left an inflated total or submitted a response.");
            Toggle(0);
            Require(ui.Modals.TryConfirm() && response != null && response.SequenceEqual(DuelSession.PackCardSelection(0, 1)),
                "The sum readout changed the native choice indices.");
            duel.SubmitResponse(response);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            ui.Field.Refresh();
            Require(duel.RetryCount == 0 && player.mzone.Any(card => card?.code == benten) &&
                amounts.All(value => !value.gameObject.activeInHierarchy), "The ritual did not resolve or left a stale contribution.");
            Debug.Log($"[VERIFY DUEL FIELD SUM] Native field choices totaled 0, 4 and 8 for a minimum of 6 (set material {setSecondMaterial}).");
        }

        private static void VerifyNativeMixedMaterials(bool ritual)
        {
            const uint sparkman = 20721928, avian = 21844576, burstinatrix = 58932615;
            const uint polymerization = 24094653, flameWingman = 35809262, ritualSpell = 55761792, blackLuster = 5405694;
            uint material = ritual ? sparkman : avian;
            uint spell = ritual ? ritualSpell : polymerization;
            uint result = ritual ? blackLuster : flameWingman;
            var main = ritual
                ? Enumerable.Repeat(spell, 14).Concat(Enumerable.Repeat(result, 10)).Concat(Enumerable.Repeat(material, 16)).ToArray()
                : Enumerable.Repeat(spell, 14).Concat(Enumerable.Repeat(material, 10)).Concat(Enumerable.Repeat(burstinatrix, 16)).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Mixed material flights", main, ritual ? Array.Empty<uint>() : new[] { result },
                Enumerable.Repeat(BlueEyes, 40).ToArray(), Array.Empty<uint>(), true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return hand.Any(card => card.code == spell) && hand.Count(card => card.code == material) == (ritual ? 2 : 1) &&
                        (ritual ? hand.Count(card => card.code == result) == 1 : hand.Any(card => card.code == burstinatrix));
                });

            var prompt = duel.ActivePrompt;
            var choices = ritual ? prompt.summonable : prompt.mSetable;
            int place = choices.FindIndex(card => card.code == material);
            Require(place >= 0, "The mixed fixture could not place the field material.");
            duel.SubmitResponseInt((place << 16) | (ritual ? 0 : 3));
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            var player = duel.Players[duel.HumanPlayerIndex];
            var source = player.mzone.Single(card => card != null);
            Require(source.IsFaceUp == ritual, "The mixed fixture did not set the expected visibility.");
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();

            var send = new List<CardMoveEvent>();
            duel.OnCardMoved += move => { if (move.MaterialMethod != 0) send.Add(move); };
            ActivateFromMainPhase(duel, spell);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, request => request.messageType is YgoConstants.MSG_SELECT_SUM or YgoConstants.MSG_SELECT_CARD
                ? ai.ComputeResponse(duel, request) : null);
            VerifyNativeSendGroup(send, ritual ? YgoConstants.REASON_RITUAL : YgoConstants.REASON_FUSION);
            var host = player.mzone.Single(card => card?.code == result);
            Require(duel.RetryCount == 0 &&
                    send.Any(move => move.fromLoc == YgoConstants.LOCATION_HAND && move.toLoc == YgoConstants.LOCATION_GRAVE) &&
                    send.Any(move => move.fromLoc == YgoConstants.LOCATION_MZONE && move.toLoc == YgoConstants.LOCATION_GRAVE),
                "The effect did not use one hand card and one field card.");
            VerifyMaterialTimeline(ui.Field, animator, host, new[] { source }, count: 2, faceDown: ritual ? 0 : 1);
            Debug.Log($"[VERIFY DUEL SUMMON MATERIAL] {(ritual ? "Ritual" : "Fusion")} kept the hand and field origins and respected the set card's visibility.");
        }

        private static void VerifyNativeGraveFusionMaterials()
        {
            const uint sparkman = 20721928, foolish = 81439173, miracle = 45906428, shining = 22061412;
            var main = Enumerable.Repeat(sparkman, 20).Concat(Enumerable.Repeat(foolish, 10))
                .Concat(Enumerable.Repeat(miracle, 10)).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Fusion material from the graveyard", main, new[] { shining },
                Enumerable.Repeat(BlueEyes, 40).ToArray(), Array.Empty<uint>(), true, session =>
                    session.ActivePrompt?.summonable.Exists(card => card.code == sparkman) == true &&
                    session.ActivePrompt.activatable.Exists(card => card.code == foolish) &&
                    session.Players[session.HumanPlayerIndex].hand.Exists(card => card.code == miracle));
            ActivateFromMainPhase(duel, foolish);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD
                ? SelectCard(prompt, sparkman) : null);
            duel.SubmitResponseInt(duel.ActivePrompt.summonable.FindIndex(card => card.code == sparkman) << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            var player = duel.Players[duel.HumanPlayerIndex];
            var source = player.mzone.Single(card => card?.code == sparkman);
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            var sent = new List<CardMoveEvent>();
            duel.OnCardMoved += move => { if (move.MaterialMethod == YgoConstants.REASON_FUSION) sent.Add(move); };
            ActivateFromMainPhase(duel, miracle);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD
                ? ai.ComputeResponse(duel, prompt) : null);
            var host = player.mzone.Single(card => card?.code == shining);
            Require(duel.RetryCount == 0 && sent.Count == 2 &&
                sent.Any(move => move.fromLoc == YgoConstants.LOCATION_GRAVE && move.toLoc == YgoConstants.LOCATION_REMOVED) &&
                sent.Any(move => move.fromLoc == YgoConstants.LOCATION_MZONE && move.toLoc == YgoConstants.LOCATION_REMOVED),
                "Miracle Fusion did not use its native field and graveyard materials.");
            VerifyMaterialTimeline(ui.Field, animator, host, new[] { source }, count: 2);
            Debug.Log("[VERIFY DUEL GRAVE MATERIAL] Miracle Fusion flew both visible copies from the field and graveyard into the vortex.");
        }

        private static void VerifyNativeSendGroup(IReadOnlyList<CardMoveEvent> moves, uint method)
        {
            Require(moves.Count == 2 && moves[0].PresentationGroup != null &&
                moves.All(move => move.MaterialMethod == method && ReferenceEquals(move.PresentationGroup, moves[0].PresentationGroup)),
                "The two real material moves did not share an authoritative engine group.");
        }

        private static Transform VerifyMaterialTimeline(DuelFieldUI field, CardAnimationManager animator, LiveCardState host,
            IReadOnlyList<LiveCardState> sources, int count, int faceDown = 0, uint vanishes = 0,
            LiveCardState inspected = null)
        {
            void CheckInspectedAttack(int attack)
            {
                const string value = "CombatStats/Attack/Digits/Value";
                var root = field.Root.transform;
                Require(root.Find($"LeftInspector/CardDetails/{value}").GetComponent<Text>().text == attack.ToString() &&
                    root.Find($"Overlay_CardZoom/ZoomWin/Info/{value}").GetComponent<Text>().text == attack.ToString(),
                    $"The inspector and detailed view did not show the material's {attack} ATK at the right time.");
            }

            field.Refresh();
            var slots = sources.Select(card => FieldSlot(field, card)).ToArray();
            var queued = AnimationSteps(animator).ToArray();
            var kind = CardAnimationManager.SummonEffect(host.code, CardMotion.ExtraDeckSummon).kind;
            bool hasSignature = DuelEffectGraphic.HasSummonSignature(kind);
            var step = queued.Single(cue => cue.GetType().Name.StartsWith("<PlaySummonMaterials>", StringComparison.Ordinal));
            var arrival = queued.Single(cue => FlightCode(cue) == host.code);
            Require(Array.IndexOf(queued, step) < Array.IndexOf(queued, arrival),
                "The material group was sequenced after the new monster.");
            Require(step.MoveNext() && step.Current is IEnumerator, "The shared material cue did not start.");
            var cueFrame = TestCardPresentation.SampleAnimation((IEnumerator)step.Current);
            cueFrame(.8f);
            Require(animator.GetComponentsInChildren<DuelEffectGraphic>().Count(effect => effect.Kind == DuelEffectKind.SummonMaterial) == count,
                "A material missed the shared summon cue.");
            for (int index = 0; index < sources.Count; index++)
            {
                var card = sources[index];
                var art = slots[index].GetComponentInChildren<CompositeCardView>();
                Require(art != null && art.CurrentCode == (card.IsFaceUp ? card.code : 0),
                    "A material left its slot or revealed a hidden card during the cue.");
                if (card.IsFaceUp)
                    Require(slots[index].Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>().text == card.attack.ToString(),
                        "A material lost its live stats at the start of the cue.");
            }
            if (inspected != null) CheckInspectedAttack(inspected.attack);

            cueFrame(1f);
            Require(step.MoveNext() && step.Current is IEnumerator, "The materials did not move together.");
            var sample = TestCardPresentation.SampleAnimation((IEnumerator)step.Current);
            var cards = animator.GetComponentsInChildren<CompositeCardView>().Where(view => view.name == "MovingCard")
                .Select(view => (RectTransform)view.transform).ToArray();
            var plates = animator.transform.Cast<RectTransform>().Where(rect => rect.name == "MovingCardReadout").ToArray();
            sample(.2f);
            if (inspected != null) CheckInspectedAttack(inspected.attack);
            Require(cards.Length == count && cards.Count(card => card.GetComponent<CompositeCardView>().CurrentCode == 0) == faceDown &&
                    plates.Length == sources.Count(card => card.IsFaceUp) &&
                    slots.All(slot => slot.GetComponentInChildren<CompositeCardView>() == null),
                "The shared flight omitted a material, duplicated its field view, or revealed private stats.");
            foreach (var source in sources.Where(card => card.IsFaceUp))
            {
                var readout = plates.Single(plate => plate.Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>().text ==
                    source.attack.ToString());
                RequireTravelStats(readout, source.attack.ToString(), source.defense.ToString(), source.level.ToString(), source.IsDefense);
                Require(cards.Count(card => card.anchoredPosition == readout.anchoredPosition) == 1,
                    "A material's live values did not follow its artwork.");
            }
            if (hasSignature)
            {
                sample(.46f);
                var portal = animator.GetComponentsInChildren<DuelEffectGraphic>()
                    .Single(graphic => graphic.SummonSignature && !graphic.SummonFinale);
                Require(portal.Kind == kind, "A real summon used the wrong material portal.");
                var orbs = animator.GetComponentsInChildren<DuelEffectGraphic>()
                    .Where(graphic => graphic.Kind == DuelEffectKind.SummonOrb).ToArray();
                if (kind == DuelEffectKind.Synchro)
                {
                    int tunerLevels = sources.Where(card => (card.type & YgoConstants.TYPE_TUNER) != 0)
                        .Sum(card => (int)card.level);
                    int otherLevels = sources.Sum(card => (int)card.level) - tunerLevels;
                    Require(portal.SummonUnits == tunerLevels && orbs.Sum(orb => orb.SummonUnits) == otherLevels,
                        "The Synchro corridor did not match the native tuner and other material levels.");
                }
                if (kind == DuelEffectKind.Xyz)
                    Require(orbs.Length == count && orbs.All(orb => orb.SummonUnits == 1),
                        "The Xyz overlay missed a material orb.");
                if (kind == DuelEffectKind.Link)
                {
                    uint first = DuelEffectGraphic.LitLinkMarkers(portal.DirectionMask, .18f);
                    Require(orbs.Length == count && portal.DirectionMask == host.linkMarker && first != 0 &&
                            (first & (first - 1)) == 0 && DuelEffectGraphic.LitLinkMarkers(portal.DirectionMask, .62f) == host.linkMarker,
                        "The Link circuit did not illuminate the real markers one at a time.");
                }
                if (kind == DuelEffectKind.Fusion)
                {
                    float radius = cards.Max(card => Vector2.Distance(card.anchoredPosition, portal.rectTransform.anchoredPosition));
                    sample(.87f);
                    Require(cards.All(card => Vector2.Distance(card.anchoredPosition, portal.rectTransform.anchoredPosition) < radius * .35f),
                        "The Fusion materials did not spiral into their shared center.");
                }
                sample(.20f);
            }
            if (hasSignature)
            {
                var portal = animator.GetComponentsInChildren<DuelEffectGraphic>()
                    .Single(graphic => graphic.SummonSignature && !graphic.SummonFinale);
                Vector2 center = portal.rectTransform.anchoredPosition;
                float distance = cards.Average(card => Vector2.Distance(card.anchoredPosition, center));
                float size = cards.Sum(card => card.rect.height * card.localScale.y);
                sample(.76f);
                float remaining = cards.Average(card => Vector2.Distance(card.anchoredPosition, center));
                Require(remaining < distance && cards.Sum(card => card.rect.height * card.localScale.y) < size &&
                    cards.All(card => card.GetComponent<CanvasGroup>().alpha < .6f) &&
                    (vanishes == 0 || cards.Any(card => card.GetComponent<CompositeCardView>().CurrentCode == vanishes)),
                    $"The {kind} materials did not reach the center before absorption ({distance:F1} to {remaining:F1}).");
            }
            sample(1f);
            var hostSlot = FieldSlot(field, host);
            Require(!step.MoveNext() && cards.All(card => !card.gameObject.activeInHierarchy) &&
                    !hostSlot.Find("Card/CardView").gameObject.activeInHierarchy,
                "The materials lingered or the new monster arrived during their departure.");
            if (inspected != null) CheckInspectedAttack(CardDatabase.Get(inspected.code).atk);
            Require(arrival.MoveNext(), "The summon did not start its reveal.");
            if (animator.GetComponentInChildren<DuelCutInCinematic>() is { } cinematic)
            {
                var wide = (IEnumerator)arrival.Current;
                Require(wide.MoveNext() && wide.Current is IEnumerator, "The native cinematic did not open its timeline.");
                var frames = (IEnumerator)wide.Current;
                Require(frames.MoveNext(), "The native cinematic did not show its first frame.");
                cinematic.Pose(.90f);
                Require(cinematic.transform.Find("Impact").GetComponent<Image>().color.a > .15f,
                    "The native cinematic did not play its impact flash.");
                ExecuteEvents.Execute(cinematic.gameObject, new PointerEventData(EventSystem.current)
                    { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerDownHandler);
                Require(!frames.MoveNext() && !wide.MoveNext(), "The native cut-in did not finish after a tap.");
            }
            else if (hasSignature)
            {
                var reveal = (IEnumerator)arrival.Current;
                Require(reveal.MoveNext(), "The signature finale did not start.");
                TestCardPresentation.SampleAnimation((IEnumerator)reveal.Current)(.96f);
                var finale = animator.GetComponentsInChildren<DuelEffectGraphic>().Single(graphic => graphic.SummonFinale);
                var flash = animator.transform.Find("SummonPeakFlash").GetComponent<Image>();
                Require(finale.Kind == kind && flash.color.a > .1f &&
                        ((RectTransform)animator.transform).anchoredPosition.sqrMagnitude > .01f,
                    "The signature finale missed its flash or shake.");
                if (kind == DuelEffectKind.Xyz)
                    Require(finale.SummonUnits == host.overlayCodes.Count, "The finale did not orbit the actual Xyz materials.");
                if (kind == DuelEffectKind.Link)
                    Require(finale.DirectionMask == host.linkMarker && animator.transform.Find("LinkCircuitClosed") != null,
                        "The Link finale omitted its circuit confirmation.");
                using var mesh = new VertexHelper();
                var populate = typeof(DuelEffectGraphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                populate.Invoke(finale, new object[] { mesh });
                Require(mesh.currentVertCount is > 100 and < 12000, "The signature finale produced an empty or excessive mesh.");
                while (reveal.MoveNext()) TestCardPresentation.SampleAnimation((IEnumerator)reveal.Current)(1f);
                Require(animator.transform.Find("SummonPeakFlash") == null &&
                        ((RectTransform)animator.transform).anchoredPosition.sqrMagnitude < .001f,
                    "The finale left a flash or moved the stage after the animation.");
                Debug.Log($"[VERIFY SIGNATURE SUMMON] {kind} material transition and finale passed.");
            }
            Require(!arrival.MoveNext() &&
                    hostSlot.GetComponentInChildren<CompositeCardView>()?.CurrentCode == host.code,
                "The summoned monster did not land once the materials cleared.");
            return hostSlot;
        }

        private static void VerifyNativeXyzMaterialReadout()
        {
            const uint tigermortar = 11510448, thoroughblade = 77150143, ram = 4145852, foolish = 81439173;
            var main = Enumerable.Repeat(thoroughblade, 14).Concat(Enumerable.Repeat(ram, 14))
                .Concat(Enumerable.Repeat(foolish, 12)).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Live Xyz material count", main, new[] { tigermortar },
                Enumerable.Repeat(BlueEyes, 40).ToArray(), Array.Empty<uint>(), true, session =>
                    session.ActivePrompt?.summonable.Exists(card => card.code == thoroughblade) == true &&
                    session.ActivePrompt.activatable.Exists(card => card.code == foolish));
            ActivateFromMainPhase(duel, foolish);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD
                ? SelectCard(prompt, ram) : null);
            int summon = duel.ActivePrompt.summonable.FindIndex(card => card.code == thoroughblade);
            Require(summon >= 0, "The Xyz effect fixture could not summon Thoroughblade.");
            duel.SubmitResponseInt(summon << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            int xyz = duel.ActivePrompt.spSummonable.FindIndex(card => card.code == tigermortar);
            Require(xyz >= 0, "The Xyz effect fixture could not summon Tigermortar.");
            duel.SubmitResponseInt((xyz << 16) | 1);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD
                ? SelectCard(prompt, thoroughblade) : null);

            var player = duel.Players[duel.HumanPlayerIndex];
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            var host = player.mzone.Single(card => card?.code == tigermortar);
            var slot = FieldSlot(ui.Field, host);
            slot.GetComponent<CardDragTrigger>().OnRightClick();
            ui.Field.Root.transform.Find("LeftInspector/CardDetails/ArtworkControls/Details/B").GetComponent<Button>().onClick.Invoke();
            var details = ui.Field.Root.transform.Find("Overlay_CardZoom");
            Require(details.gameObject.activeInHierarchy, "The Xyz details did not open for the active copy.");
            var detailCard = details.GetComponentsInChildren<CompositeCardView>().Single(view => view.name == "ZoomCard");

            void VerifyNumbers(string count, string attack, string defense)
            {
                Require(slot.Find("CardReadout/Labels/Materials/Value").GetComponent<Text>().text == count &&
                        slot.Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>().text == attack &&
                        slot.Find("CardReadout/Combat/Defense/Digits/Value").GetComponent<Text>().text == defense &&
                        ui.Field.Root.transform.Find("LeftInspector/CardDetails/CombatStats/Attack/Digits/Value")
                            .GetComponent<Text>().text == attack &&
                        detailCard.transform.Find("AspectBox/AtkDef/Attack/Digits/Value")?.GetComponent<Text>().text == attack &&
                        detailCard.transform.Find("AspectBox/AtkDef/Defense/Digits/Value")?.GetComponent<Text>().text == defense,
                    $"Tigermortar did not present {count} materials and {attack}/{defense} combat values.");
            }

            VerifyNumbers("1", "1600", "0");
            ActivateFromMainPhase(duel, tigermortar);
            if (duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_CARD && duel.LastHintMsg == 519)
                duel.SubmitResponse(SelectCard(duel.ActivePrompt, thoroughblade));
            Require(player.mzone[host.sequence].overlayCodes.Count == 0 && player.mzone[host.sequence].attack == 0,
                "The native cost did not remove the original Zoodiac material.");
            ui.Field.Refresh();
            VerifyNumbers("1", "1600", "0");

            var detach = AnimationSteps(animator).Single(step => FlightCode(step) == thoroughblade);
            Require(detach.MoveNext() && detach.Current is IEnumerator, "The detached material did not leave its host.");
            var detachFrame = TestCardPresentation.SampleAnimation((IEnumerator)detach.Current);
            VerifyNumbers("0", "0", "0");
            detachFrame(1f);
            Require(!detach.MoveNext(), "The detached material did not finish its movement.");

            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                if (prompt.messageType != YgoConstants.MSG_SELECT_CARD) return null;
                return SelectCard(prompt, prompt.candidates.Exists(card => card.code == tigermortar) ? tigermortar : ram);
            });
            host = player.mzone[host.sequence];
            Require(duel.RetryCount == 0 && host.overlayCodes.SequenceEqual(new[] { ram }) &&
                    host.attack == 400 && host.defense == 2000, "Tigermortar did not gain Ram's native combat values.");
            ui.Field.Refresh();
            VerifyNumbers("0", "0", "0");

            var attach = AnimationSteps(animator).Single(step => FlightCode(step) == ram);
            Require(attach.MoveNext() && attach.Current is IEnumerator, "The new material did not travel to its host.");
            var attachFrame = TestCardPresentation.SampleAnimation((IEnumerator)attach.Current);
            attachFrame(.81f);
            Require(animator.GetComponentsInChildren<DuelEffectGraphic>().Any(graphic => graphic.Kind == DuelEffectKind.XyzMaterial),
                "A material transfer used the full Xyz Summon effect over the field values.");
            VerifyNumbers("0", "0", "0");
            attachFrame(1f);
            Require(!attach.MoveNext(), "The new material did not finish its movement.");
            VerifyNumbers("1", "400", "2000");
            Debug.Log("[VERIFY DUEL XYZ MATERIAL] Tigermortar kept 1600/0 before detach, showed 0/0 on departure, then gained 400/2000 when Ram attached.");
        }

        private static uint FlightCode(IEnumerator step)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return step.GetType().GetField("flight", fields)?.GetValue(step) is CardFlight flight ? flight.Code : 0;
        }

        private static byte[] SelectCard(DuelPrompt prompt, uint code)
        {
            int index = prompt.candidates.FindIndex(card => card.code == code);
            Require(index >= 0, $"The Xyz fixture could not select {code}.");
            return DuelSession.PackCardSelection(index);
        }

        private static Transform FieldSlot(DuelFieldUI field, LiveCardState card)
        {
            var slot = typeof(DuelFieldUI).GetMethod("GetSlot", PrivateInstance)
                .Invoke(field, new object[] { card.controller, card.location, card.sequence });
            return (Transform)slot.GetType().GetField("root").GetValue(slot);
        }

        private static Queue<IEnumerator> AnimationSteps(CardAnimationManager animator) =>
            (Queue<IEnumerator>)typeof(CardAnimationManager).GetField("_steps", PrivateInstance).GetValue(animator);

        internal static void VerifyPendulumScaleFlight(DuelFieldUI field, CardAnimationManager animator,
            LiveCardState scale, bool arriving)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            field.Refresh();
            var snapshot = AnimationSteps(animator).Select(step =>
                    step.GetType().GetField("readout", fields)?.GetValue(step) as CardMoveReadoutSnapshot)
                .SingleOrDefault(move => (arriving ? move?.DestinationCard : move?.SourceCard) is { } card &&
                    card.code == scale.code && card.location == scale.location && card.sequence == scale.sequence);
            Require(snapshot != null, $"The Pendulum card had no {(arriving ? "arrival" : "departure")} scale readout.");
            var slot = FieldSlot(field, scale);
            var (step, proxy, readout, sample) = OpenMove(animator, scale.code, snapshot);
            var cardReadout = readout.Find("CardReadout");
            var expected = new DuelCardReadout(CardDatabase.Get(scale.code), scale).InspectionRating;
            sample(.29f);
            CheckScale(!arriving);
            sample(arriving ? .79f : .91f);
            CheckScale(arriving);
            sample(1f);
            Require(!step.MoveNext() && animator.transform.Find("MovingCardReadout") == null,
                "The Pendulum move left a duplicate readout behind.");
            Require(slot.Find("CardReadout")?.gameObject.activeInHierarchy == arriving,
                "The scale did not hand off to the field at the correct time.");

            void CheckScale(bool visible)
            {
                Require(cardReadout.gameObject.activeInHierarchy == visible,
                    "The moving scale appeared on the wrong side of the transition.");
                if (!visible) return;
                var displayed = cardReadout.Find("Labels/Rating/Value").GetComponent<Text>();
                Require(!cardReadout.Find("Combat").gameObject.activeSelf &&
                        displayed.text == expected.Value.ToString() &&
                        displayed.color == expected.ValueColor(DuelStatSymbols.RatingColor(expected.Kind, expected.RightScale)) &&
                        cardReadout.Find("Labels/Rating/Symbol").GetComponent<Image>().sprite ==
                            DuelStatSymbols.Rating(DuelRatingKind.Scale) &&
                        Vector2.Distance(readout.anchoredPosition, ((RectTransform)proxy.transform).anchoredPosition) < .1f,
                    "The applied Pendulum scale did not follow the moving artwork.");
            }
        }

        private static Dictionary<(byte Controller, uint Location, uint Sequence), CardMoveReadoutSnapshot> PendingMoveReadouts(DuelFieldUI field) =>
            (Dictionary<(byte, uint, uint), CardMoveReadoutSnapshot>)typeof(DuelFieldUI)
                .GetField("_pendingCardReadouts", PrivateInstance).GetValue(field);

        private static (IEnumerator Step, CompositeCardView Proxy, RectTransform Readout, Action<float> Sample) OpenMove(
            CardAnimationManager animator, uint code, CardMoveReadoutSnapshot snapshot = null)
        {
            var steps = AnimationSteps(animator);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var step = steps.Single(move => move.GetType().GetField("flight", fields)?.GetValue(move) is CardFlight flight &&
                flight.Code == code && (snapshot == null ? flight.EndsFaceDown :
                    ReferenceEquals(move.GetType().GetField("readout", fields)?.GetValue(move), snapshot)));
            Require(step.MoveNext() && step.Current is IEnumerator, "The native card did not enter its motion timeline.");
            var sample = TestCardPresentation.SampleAnimation((IEnumerator)step.Current);
            var proxy = animator.GetComponentsInChildren<CompositeCardView>().Single(card => card.name == "MovingCard");
            var readout = (RectTransform)animator.transform.Find("MovingCardReadout");
            Require(readout != null, "The moving card had no stat display.");
            return (step, proxy, readout, sample);
        }

        private static void RequireTravelStats(RectTransform readout, string attack, string defenseValue, string level, bool defense)
        {
            var combat = readout.Find("CardReadout/Combat");
            var rating = readout.Find("CardReadout/Labels/Rating");
            float attackAccent = combat.Find("Attack/Rail").GetComponent<Image>().color.a;
            float defenseAccent = combat.Find("Defense/Rail").GetComponent<Image>().color.a;
            float attackWidth = ((RectTransform)combat.Find("Attack")).rect.width;
            float defenseWidth = ((RectTransform)combat.Find("Defense")).rect.width;
            Require(combat.gameObject.activeInHierarchy &&
                combat.Find("Attack/Digits/Value").GetComponent<Text>().text == attack &&
                combat.Find("Defense/Digits/Value").GetComponent<Text>().text == defenseValue &&
                (defense ? defenseAccent > attackAccent : attackAccent > defenseAccent) &&
                (defense ? defenseWidth > attackWidth : attackWidth > defenseWidth) &&
                rating.Find("Value").GetComponent<Text>().text == level &&
                rating.Find("Symbol").GetComponent<Image>().sprite == DuelStatSymbols.Rating(DuelRatingKind.Level) &&
                readout.GetComponentsInChildren<Graphic>().All(graphic => !graphic.raycastTarget),
                $"The moving readout did not display ATK {attack}, DEF {defenseValue}, Level {level} and the current stance.");
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

            internal HandUI(DuelSession duel, bool mobileLayout = false, Action<byte[]> submitResponse = null)
            {
                var root = new GameObject("Hand Glow Verification", typeof(Canvas));
                Canvas = root.GetComponent<Canvas>();
                ((RectTransform)root.transform).sizeDelta = new Vector2(1600, 900);
                Modals = new DuelPromptModals(root.transform, null, submitResponse);
                Field = new DuelFieldUI(null, root.transform, Modals, mobileLayout);
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

            internal void ExpectLinkHighlight(string name, DuelLinkHighlight kind, Vector2Int direction)
            {
                var zone = Field.Root.transform.Find($"DuelBoard/{name}").GetComponent<DuelZoneImage>();
                Require(zone.LinkHighlight == kind && zone.LinkDirection == direction,
                    $"{name} showed {zone.LinkHighlight} ({zone.LinkDirection}) instead of {kind} ({direction}).");
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
                const string materials = "Labels/Materials/Value", counters = "Labels/Counters/Number/Value";
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
                var xyzSlot = (RectTransform)Slot("_myMZone", 1);
                var xyz = (RectTransform)xyzSlot.Find("CardReadout");
                var link = (RectTransform)Slot("_emz", 0).Find("CardReadout");
                var scale = Slot("_mySZone", 0).Find("CardReadout");
                Require(new[] { normal, xyz, link }.All(readout => readout.gameObject.activeInHierarchy &&
                    !readout.GetComponentsInChildren<Graphic>().Any(graphic => graphic.raycastTarget)) &&
                    Label(normal, rating).text == "5" && Label(xyz, rating).text == "0" &&
                    Label(link, rating).text == "3" && Label(xyz, materials).text == "2" && Label(xyz, status).text == "무효" &&
                    Label(scale, rating).text == "8" && !scale.Find("Combat").gameObject.activeSelf,
                    "The field readouts did not show the current rating, materials, effects and scale.");
                var ratingSymbols = new[] { normal, xyz, link, scale }.Select(readout => readout.Find("Labels/Rating/Symbol").GetComponent<Image>()).ToArray();
                Require(ratingSymbols.Select(icon => icon.sprite).SequenceEqual(new[]
                    {
                        DuelStatSymbols.Rating(DuelRatingKind.Level), DuelStatSymbols.Rating(DuelRatingKind.Rank),
                        DuelStatSymbols.Rating(DuelRatingKind.Link), DuelStatSymbols.Rating(DuelRatingKind.Scale),
                    }) && ratingSymbols.All(icon => icon.sprite != null && !icon.raycastTarget) &&
                    ratingSymbols.Select(icon => icon.sprite).Distinct().Count() == 4,
                    "The field did not show four distinct non-interactive rating symbols.");
                Require(Label(normal, rating).color == DuelStyle.DecreasedValue &&
                    Label(xyz, rating).color == DuelStyle.DecreasedValue &&
                    Label(link, rating).color == ratingSymbols[2].color &&
                    Label(scale, rating).color == DuelStyle.IncreasedValue,
                    "The current ratings did not show their differences from the printed values.");
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
                var inspectorRating = Field.Root.transform.Find("LeftInspector/CardDetails/Stats/Rating");
                xyzSlot.GetComponent<CardDragTrigger>().OnRightClick();
                Require(Label(inspectorRating, "Number").text == "0" && inspectorRating.gameObject.activeInHierarchy &&
                    inspectorRating.Find("Symbol").GetComponent<Image>().sprite == DuelStatSymbols.Rating(DuelRatingKind.Rank),
                    "The inspector treated a valid Rank 0 as a missing level.");
                Slot("_emz", 0).GetComponent<CardDragTrigger>().OnRightClick();
                Require(Label(inspectorRating, "Number").text == "3" &&
                    inspectorRating.Find("Symbol").GetComponent<Image>().sprite == DuelStatSymbols.Rating(DuelRatingKind.Link),
                    "The inspector showed a level symbol for the native Link rating.");
                ExpectLinkedZones("Zone_3_1", "Zone_4_3");
                ExpectLinkHighlight("Zone_3_1", DuelLinkHighlight.Linked, Vector2Int.up);
                var previousNeighbor = mine.mzone[2];
                mine.mzone[2] = new LiveCardState
                {
                    code = 1861629, type = monster | YgoConstants.TYPE_LINK, position = YgoConstants.POS_FACEUP_ATTACK,
                    link = 3, linkMarker = 0x040, attack = 2300,
                };
                Field.Refresh();
                ExpectLinkHighlight("Zone_4_3", DuelLinkHighlight.CoLinked, Vector2Int.zero);
                mine.mzone[2].linkMarker = 0x004;
                Field.Refresh();
                ExpectLinkHighlight("Zone_4_3", DuelLinkHighlight.Linked, Vector2Int.zero);
                mine.mzone[2] = previousNeighbor;
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
                var detailAttack = Label(zoom, "Attack/Digits/Value");
                var previewAttack = Label(inspector, "Attack/Digits/Value");
                Require(new[] { detailAttack, previewAttack }.All(label => label.text == "4200" &&
                        label.rectTransform.anchoredPosition.y < 0f) &&
                    zoom.Find("Attack/ChangePulse").gameObject.activeInHierarchy,
                    "An open details panel did not follow a change to the selected card's live stats.");
                mine.mzone[2].attack = 3300;
                Field.Refresh();
                Require(new[] { detailAttack, previewAttack }.All(label => label.text == "3300" &&
                        label.rectTransform.anchoredPosition.y > 0f),
                    "A second stat change reused the outgoing number or the first direction of motion.");
                typeof(DuelFieldUI).GetMethod("CloseCardZoomModal", PrivateInstance).Invoke(Field, new object[] { false });
                normalSlot.GetComponent<CardDragTrigger>().OnRightClick();
                Require(!inspector.Find("Attack/ChangePulse").gameObject.activeInHierarchy &&
                    previewAttack.text == "3700" && previewAttack.rectTransform.anchoredPosition == Vector2.zero &&
                    previewAttack.rectTransform.localScale == Vector3.one,
                    "An unfinished stat animation followed the focus to a different copy.");

                mine.mzone[0].attack = 2900;
                mine.mzone[0].defense = 2800;
                mine.mzone[0].level = 6;
                Field.Refresh();
                var reduced = normal.Find("Combat/Attack");
                var reverse = (RectTransform)reduced.Find("ChangePulse/Sweep");
                float initial = reverse.anchorMin.x;
                reduced.GetComponent<DuelReadoutPulse>().Advance(.16f);
                Require(reverse.anchorMin.x < initial && Label(normal, attack).text == "2900" &&
                    Label(normal, defense).text == "2800" && normal.Find("Combat/Defense/ChangePulse").gameObject.activeInHierarchy &&
                    normal.Find("Labels/Rating/ChangePulse").gameObject.activeInHierarchy && Label(normal, rating).text == "6",
                    "The updated attack, defense or level did not animate with its current value.");
                var attackDelta = Label(inspector, "Attack/Change/Delta");
                Require(attackDelta.text == "−100" && Label(inspector, "Defense/Change/Delta").text == "+300",
                    "The card details lost the direction or amount of a modifier.");
                var inspectedAttack = inspector.Find("Attack");
                DuelReadoutPulse.Cancel(inspectedAttack);
                mine.mzone[0].baseAttack = 3200;
                Field.Refresh();
                Require(attackDelta.text == "−300" && Label(inspector, "Attack/Digits/Value").text == "2900" &&
                    Label(inspector, "Attack/Digits/Value").rectTransform.anchoredPosition == Vector2.zero &&
                    inspectedAttack.Find("ChangePulse").gameObject.activeInHierarchy,
                    "A base-only change did not update the signed modifier.");
                mine.mzone[0].baseAttack = -2;
                Field.Refresh();
                Require(!attackDelta.gameObject.activeInHierarchy,
                    "An unknown original attack produced a false modifier.");
                mine.mzone[0].baseAttack = 3000;
                Field.Refresh();

                var attackIcon = normal.Find("Combat/Attack/Symbol").GetComponent<Image>();
                var defenseIcon = normal.Find("Combat/Defense/Symbol").GetComponent<Image>();
                Require(attackIcon.sprite != null && defenseIcon.sprite != null && attackIcon.sprite != defenseIcon.sprite &&
                    !attackIcon.raycastTarget && !defenseIcon.raycastTarget,
                    "The compact readout did not have separate non-interactive attack and defense symbols.");
                mine.mzone[1].attack = mine.mzone[1].defense = int.MaxValue;
                mine.mzone[1].rank = 12;
                mine.mzone[1].Counters = new[] { ((ushort)1, (ushort)12) };
                mine.mzone[1].overlayCodes.AddRange(Enumerable.Repeat(BlueEyes, 8));
                Field.Refresh();
                var counterBadge = xyz.Find("Labels/Counters");
                DuelReadoutPulse.Cancel(counterBadge);
                mine.mzone[1].Counters = new[] { ((ushort)0x1041, (ushort)12) };
                Field.Refresh();
                Require(counterBadge.Find("ChangePulse").gameObject.activeSelf && Label(xyz, counters).text == "12",
                    "A simultaneous counter-type change did not animate when the total stayed the same.");

                var viewport = (RectTransform)Canvas.transform;
                var originalSize = viewport.sizeDelta;
                var applyFocus = typeof(DuelFieldUI).GetMethod("ApplyFieldCardFocus", BindingFlags.NonPublic | BindingFlags.Static);
                var fieldSlots = (Array)typeof(DuelFieldUI).GetField("_myMZone", PrivateInstance).GetValue(Field);
                var xyzView = fieldSlots.GetValue(1);
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
                    var attackInk = VerifyStatGlyphs(Label(normal, attack), "2900");
                    var defenseInk = VerifyStatGlyphs(Label(normal, defense), "2800");
                    Require(defenseInk.height >= attackInk.height * 1.12f,
                        $"The active defense value did not stand out at {size} ({defenseInk.height:F1} vs {attackInk.height:F1}).");
                    VerifyStatGlyphs(Label(normal, "Combat/Attack/Change/Arrow"), "▼");
                    VerifyStatGlyphs(Label(normal, "Combat/Defense/Change/Arrow"), "▲");
                    VerifyStatGlyphs(Label(link, attack), "2300");
                    VerifyStatGlyphs(Label(xyz, attack), int.MaxValue.ToString(), minimumInkShare: .16f);
                    VerifyStatGlyphs(Label(xyz, defense), int.MaxValue.ToString(), minimumInkShare: .16f);
                    VerifyStatGlyphs(Label(xyz, rating), "12");
                    VerifyStatGlyphs(Label(xyz, materials), "10");
                    VerifyStatGlyphs(Label(xyz, counters), "12");
                    VerifyStatGlyphs(Label(xyz, status), "무효");
                    VerifyStatGlyphs(Label(inspector, "Attack/Digits/Value"), "2900", .48f);
                    VerifyStatGlyphs(attackDelta, "−100");
                    VerifyStatGlyphs(Label(inspector, "Defense/Change/Delta"), "+300");
                    var caption = RectTransformUtility.CalculateRelativeRectTransformBounds(inspector, inspector.Find("Attack/Caption"));
                    var digits = RectTransformUtility.CalculateRelativeRectTransformBounds(inspector, inspector.Find("Attack/Digits"));
                    var modifier = RectTransformUtility.CalculateRelativeRectTransformBounds(inspector, inspector.Find("Attack/Change"));
                    Require(digits.max.y < caption.min.y && caption.max.x < modifier.min.x,
                        "The inspection value, caption or modifier overlapped.");
                    foreach (string side in new[] { "Attack", "Defense" })
                    {
                        var cell = normal.Find($"Combat/{side}");
                        var symbol = RectTransformUtility.CalculateRelativeRectTransformBounds(cell, cell.Find("Symbol"));
                        var arrow = RectTransformUtility.CalculateRelativeRectTransformBounds(cell, cell.Find("Change"));
                        var number = RectTransformUtility.CalculateRelativeRectTransformBounds(cell, cell.Find("Digits"));
                        Require(arrow.max.y < symbol.min.y && Mathf.Max(symbol.max.x, arrow.max.x) < number.min.x,
                            "The field's type icon, change arrow and number overlapped.");
                    }

                    applyFocus.Invoke(null, new[] { xyzView, (object)1f, 1.05f });
                    Canvas.ForceUpdateCanvases();
                    Bounds Chip(Transform root, string name) => RectTransformUtility.CalculateRelativeRectTransformBounds(
                        xyzSlot, root.Find("Labels/" + name));
                    Bounds rank = Chip(xyz, "Rating"), material = Chip(xyz, "Materials");
                    Bounds counter = Chip(xyz, "Counters"), statusBadge = Chip(xyz, "Status");
                    Require(xyz.Find("Labels/Counters/Symbol").GetComponent<Image>().sprite == DuelStatSymbols.Counter &&
                        xyz.Find("Labels/Materials/Symbol").GetComponent<DuelPromptLocationIcon>() != null &&
                        rank.max.x < material.min.x && counter.max.x < statusBadge.min.x &&
                        Mathf.Max(counter.max.y, statusBadge.max.y) < Mathf.Min(rank.min.y, material.min.y) &&
                        Mathf.Max(rank.max.y, material.max.y) < Chip(link, "Rating").min.y &&
                        rank.max.y < xyzSlot.rect.yMax,
                        "The focused readout covered another badge or the neighboring Extra Monster Zone.");
                    applyFocus.Invoke(null, new[] { xyzView, (object)0f, 1f });
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
                mine.mzone[1].Counters = Array.Empty<(ushort, ushort)>();
                Field.Refresh();
                ExpectLinkedZones();
                Require(!Label(normal, rating).gameObject.activeInHierarchy && !link.gameObject.activeInHierarchy &&
                    Label(xyz, materials).text == "0" && Label(xyz, status).text == "무효" &&
                    !xyz.Find("Labels/Counters").gameObject.activeSelf, "A field readout kept stale state when a card changed.");
                mine.mzone[0] = mine.mzone[1] = mine.mzone[2] = mine.mzone[4] = theirs.mzone[6] = mine.szone[0] = null;
                Field.Refresh();
                Require(!xyz.gameObject.activeInHierarchy && !scale.gameObject.activeInHierarchy,
                    "A vacated zone retained a rating or pendulum scale.");
                typeof(DuelFieldUI).GetMethod("ResetCardInspector", PrivateInstance).Invoke(Field, null);
                Debug.Log("[VERIFY DUEL CARD READOUT] Verified live combat stats, changing copies, ratings, scales, Link markers, face-down cards and three viewport sizes.");
                Debug.Log("[VERIFY DUEL STAT FIT] Field values, Link attack, change arrows, and inspected stats used the available glyph space without overlap. All ten digits stayed on one line across three viewports.");
                Debug.Log("[VERIFY DUEL RATING BADGES] Distinct rating, material, counter and negation badges stayed separate from the neighboring zone at full focus across three viewports.");
            }

            private static Rect VerifyStatGlyphs(Text text, string expected, float minimumHeightShare = .78f,
                float minimumInkShare = .49f)
            {
                Require(text.text == expected && text.horizontalOverflow == HorizontalWrapMode.Overflow,
                    "A readout truncated or wrapped its text.");
                using var mesh = new VertexHelper();
                var populate = typeof(Text).GetMethod("OnPopulateMesh", PrivateInstance, null, new[] { typeof(VertexHelper) }, null);
                populate.Invoke(text, new object[] { mesh });
                Vector2 originalSize = BoundsOfText().size;
                text.GetComponent<DuelStatDigitFit>().ModifyMesh(mesh);
                int glyphCount = expected.Count(character => !char.IsWhiteSpace(character));
                Require(mesh.currentVertCount == glyphCount * 4 && text.cachedTextGenerator.lineCount == 1,
                    $"The readout omitted or wrapped a glyph in '{expected}'.");
                Rect rect = text.GetPixelAdjustedRect();
                var fitted = BoundsOfText();
                Require(fitted.xMin >= rect.xMin - .1f && fitted.xMax <= rect.xMax + .1f &&
                    fitted.yMin >= rect.yMin - .1f && fitted.yMax <= rect.yMax + .1f,
                    $"'{expected}' rendered outside its allotted space.");
                Require(Mathf.Abs(fitted.width / originalSize.x - fitted.height / originalSize.y) < .015f,
                    $"'{expected}' changed the typeface's shape to fit its bounds.");
                if (expected.All(char.IsDigit))
                    Require(text.font == DuelTypography.NumberFont && text.fontStyle == FontStyle.Normal,
                        $"'{expected}' bypassed the duel numeric font.");
                if (!text.resizeTextForBestFit)
                    Require(fitted.height >= rect.height * minimumInkShare,
                        $"'{expected}' only used {fitted.height:F1} of {rect.height:F1} available vertical units.");
                var cell = (RectTransform)text.transform.parent;
                if (cell.name is "Digits" or "Number") cell = (RectTransform)cell.parent;
                Require(rect.height / cell.rect.height > minimumHeightShare, "The readout text did not use enough of its allotted height.");
                return fitted;

                Rect BoundsOfText()
                {
                    var min = Vector2.positiveInfinity;
                    var max = Vector2.negativeInfinity;
                    var vertex = default(UIVertex);
                    for (int i = 0; i < mesh.currentVertCount; i++)
                    {
                        mesh.PopulateUIVertex(ref vertex, i);
                        min = Vector2.Min(min, vertex.position);
                        max = Vector2.Max(max, vertex.position);
                    }
                    return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                }
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
