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
            VerifyMainPhaseAndStaleState();
            VerifyOpponentTurnActivation();
            VerifyNativeCombatReadout(useHonest: true);
            VerifyNativeCombatReadout(useHonest: false);
            VerifyDefensePositionBattleReadout(34496660, BattleStatSource.Defense, 1800);
            VerifyDefensePositionBattleReadout(75372290, BattleStatSource.Attack, 1550);
            VerifyEffectModifiedBattleReadout();
            VerifyNativeBattleDamageFeedback(reflected: false);
            VerifyNativeBattleDamageFeedback(reflected: true);
            VerifyNativeCounterReadout();
            VerifyNativeIdentityReadout();
            VerifyInspectedCardMoves(swapInPlace: true);
            VerifyInspectedCardMoves(swapInPlace: false);
            VerifyNativeSummonReadout(turnFaceDown: false);
            VerifyNativeSummonReadout(turnFaceDown: true);
            VerifyNativeLinkedZone();
            VerifyNativeMovingReadout();
            VerifyNativeTokenArrivalReadout();
            VerifyNativePendulumArrivalReadout();
            VerifyNativeXyzMaterialGather();
            VerifyNativeXyzMaterialReadout();
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

            var steps = (Queue<IEnumerator>)typeof(CardAnimationManager).GetField("_steps", PrivateInstance).GetValue(animation);
            if (!reflected)
            {
                var flip = steps.First(step => step.GetType().Name.StartsWith("<MoveCard>", StringComparison.Ordinal));
                Require(flip.MoveNext() && !flip.MoveNext(), "The defender did not flip before the damage display.");
            }
            var hud = ui.Field.Root.transform.Find(reflected ? "PlayerHUD" : "OpponentHUD");
            var anchor = (RectTransform)hud.Find("Status/Readout/Name");
            var total = hud.Find("Status/Readout/Value/Text").GetComponent<Text>();
            var advanceLife = typeof(DuelFieldUI).GetMethod("AdvanceLifeReadouts", PrivateInstance);
            TestCardPresentation.VerifyAttackFlight(animation, "1900", "0", "4", () =>
            {
                advanceLife.Invoke(ui.Field, new object[] { 1f });
                var popup = (RectTransform)animation.transform.Find("LPChange");
                if (animation.transform.Find("Strike") == null)
                {
                    Require(popup == null && total.text == "8000", "The LP changed before the actual strike.");
                    return;
                }
                Require(popup != null && popup.anchoredPosition == animation.GetPoint(anchor, Vector2.one * .5f) &&
                    popup.Find("Kind/Text").GetComponent<Text>().text == "전투 피해" &&
                    popup.Find("Value/Text").GetComponent<Text>().text == $"-{damage}" && total.text == (8000 - damage).ToString(),
                    "The result showed damage on the wrong HUD, or at the wrong time in the attack.");
            });
            Require(animation.transform.Find("LPChange") == null &&
                !steps.Any(step => step.GetType().Name.StartsWith("<PlayLifeChanges>", StringComparison.Ordinal)),
                "The same battle damage was queued again after contact.");
            Debug.Log($"[VERIFY DUEL LP FEEDBACK] Native {(reflected ? "reflected" : "piercing")} damage of {damage} reached seat {receiver} at the strike.");
        }

        private static void VerifyNativeCounterReadout()
        {
            const uint breaker = 71413901;
            const uint powerStone = 34029630;
            const string countPath = "CardReadout/Labels/Counters/Number/Value";
            var empty = Array.Empty<uint>();
            var main = Enumerable.Repeat(breaker, 20).Concat(Enumerable.Repeat(powerStone, 20)).ToArray();
            var opponent = Enumerable.Repeat(BlueEyes, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Live counter readout", main, empty, opponent, empty,
                true, session => session.ActivePrompt?.summonable.Exists(card => card.code == breaker) == true &&
                    session.ActivePrompt.sSetable.Exists(card => card.code == powerStone));
            using var ui = new HandUI(duel);
            var ai = new TacticalAI();
            var mine = duel.Players[duel.HumanPlayerIndex];

            void ReachMainPhase()
            {
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                {
                    if (prompt.messageType == YgoConstants.MSG_SELECT_CARD)
                    {
                        int target = prompt.candidates.FindIndex(card => card.code == powerStone);
                        Require(target >= 0, "The Breaker effect could not target Pitch-Black Power Stone.");
                        return DuelSession.PackCardSelection(target);
                    }
                    return prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && prompt.forced ? BitConverter.GetBytes(0) : null;
                });
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && duel.RetryCount == 0,
                    "The counter fixture could not reach the next native main phase.");
            }

            int setIndex = duel.ActivePrompt.sSetable.FindIndex(card => card.code == powerStone);
            duel.SubmitResponseInt((setIndex << 16) | 4);
            ReachMainPhase();
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
            var root = ui.Field.Root.transform;
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
            Require(label.text == "카운터 3개" && !label.raycastTarget, "Inspecting the trap lost its counter count or blocked input.");
            monsterSlot.GetComponent<CardDragTrigger>().OnRightClick();
            inspector.Find("ZoomHit").GetComponent<Button>().onClick.Invoke();
            var zoom = root.Find("Overlay_CardZoom/ZoomWin/Info/Counters/Number/Value").GetComponent<Text>();
            Require(label.text == "카운터 1개" && zoom.text == "카운터 1개", "Inspecting Breaker did not update both detail views.");

            Activate(breaker);
            ui.Field.Refresh();
            Require(mine.szone[trap.sequence] == null && mine.mzone[monster.sequence].counterCount == 0 &&
                mine.mzone[monster.sequence].attack == 1600 &&
                !trapCount.gameObject.activeInHierarchy && !monsterCount.gameObject.activeInHierarchy &&
                !label.gameObject.activeInHierarchy && !zoom.gameObject.activeInHierarchy &&
                monsterSlot.Find("CardReadout/Combat/Attack/Digits/Value").GetComponent<Text>().text == "1600",
                "Counter removal left stale readouts or attack values on the field or in card details.");
            Debug.Log("[VERIFY DUEL COUNTERS] Pitch-Black Power Stone and Breaker displayed their native counts. Spending a counter updated attack and hid the removed counts in both detail views.");
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
            var inspectorText = inspector.Find("Stats/Info/Text").GetComponent<Text>();
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
                Require(label.lineCount == 2 && label.characterCountVisible >= inspectorText.text.Length - 1 &&
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

        private static void VerifyInspectedCardMoves(bool swapInPlace)
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
            duel.SubmitResponseInt(0);
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
            var snapshot = PendingMoveReadouts(ui.Field)[((byte)duel.AiPlayerIndex, destination)];
            ui.Field.Refresh();
            Require(paired == swapInPlace && mine.mzone[origin]?.attack == 1700 && theirs.mzone[destination]?.attack == 2300 &&
                inspector.text == "2300" && zoom.text == "2300",
                "The inspector or details missed the new controller's modifier or switched to the wrong copy.");
            var move = OpenMove(animator, KaiserSeaHorse, snapshot);
            RequireTravelStats(move.Readout, "2700", "1650", "4", defense: false);
            move.Sample(.72f);
            RequireTravelStats(move.Readout, "2300", "1650", "4", defense: false);
            move.Sample(1f);
            Require(!move.Step.MoveNext() && move.Readout == null,
                "A control change left the moving stat panel behind.");
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
                inspector.text == "1700" && zoom.text == "1700" &&
                typeof(DuelFieldUI).GetField("_zoomFieldCard", PrivateInstance).GetValue(ui.Field) == null,
                "A face-down transfer kept the captured live stats in its details.");
            Debug.Log($"[VERIFY DUEL CARD FOLLOW] {(swapInPlace ? "Paired swap" : "Zone transfer")} kept controller focus and displayed 2700 before and 2300 after the transfer.");
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

            if (turnFaceDown)
            {
                Activate(bookOfMoon);
                ui.Field.Refresh();
                Require(duel.Players[captured.Key.Item1].mzone[captured.Key.Item2].position == YgoConstants.POS_FACEDOWN_DEFENSE &&
                        pending.Count == 0 && captured.Value.DestinationCard == null,
                    "A summon preview read the live stats of a card that was already hidden when the field refreshed.");
                Debug.Log("[VERIFY DUEL SUMMON STATS] A pending reveal discarded a card's live stats when the card became hidden.");
                return;
            }

            ui.Field.Refresh();
            var stats = captured.Value.DestinationCard;
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
            var snapshot = pending[(monster.controller, monster.sequence)];
            ui.Field.Refresh();
            Require(snapshot.DestinationCard?.IsDefense == true && snapshot.SourceCard?.position == YgoConstants.POS_FACEUP_ATTACK &&
                Vector2.Distance(snapshot.Movement.StartSize, focusedStats) < .1f && card.anchoredPosition == Vector2.zero,
                "A position change lost its starting stats or retained a raised card underneath its animation.");

            var motion = OpenMove(animation, monster.code, snapshot);
            Require(Vector2.Distance(motion.Readout.anchoredPosition, focusedPose.Position) < .2f,
                "The motion did not start at the raised card pose.");
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
            var revealed = pending[(monster.controller, monster.sequence)];
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
                    !AnimationSteps(animator).Any(step => step.GetType().Name.StartsWith("<PlaySpecialSummonGroup>", StringComparison.Ordinal)),
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
                step.GetType().Name.StartsWith("<PlaySpecialSummonGroup>", StringComparison.Ordinal));
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

        private static void VerifyNativeXyzMaterialGather()
        {
            const uint sparkman = 20721928, doubleSummon = 43422537, cowboy = 12014404;
            var main = Enumerable.Repeat(sparkman, 30).Concat(Enumerable.Repeat(doubleSummon, 10)).ToArray();
            var enemy = Enumerable.Repeat(BlueEyes, 40).ToArray();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Xyz material flights", main, new[] { cowboy }, enemy,
                Array.Empty<uint>(), true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return hand.Count(card => card.code == sparkman) >= 2 && hand.Any(card => card.code == doubleSummon);
                });
            ActivateFromMainPhase(duel, doubleSummon);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            for (int i = 0; i < 2; i++)
            {
                Require(duel.ActivePrompt.summonable.Count != 0, "The Xyz fixture ran out of normal summons.");
                duel.SubmitResponseInt(0);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
            }
            int summon = duel.ActivePrompt.spSummonable.FindIndex(card => card.code == cowboy);
            Require(summon >= 0, "The two monsters did not offer an Xyz Summon.");
            using var ui = new HandUI(duel);
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            Canvas.ForceUpdateCanvases();
            duel.SubmitResponseInt((summon << 16) | 1);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType switch
            {
                YgoConstants.MSG_SELECT_CARD => DuelSession.PackCardSelection(0, 1),
                YgoConstants.MSG_SELECT_POSITION => BitConverter.GetBytes((int)YgoConstants.POS_FACEUP_DEFENSE),
                _ => null,
            });
            var host = duel.Players[duel.HumanPlayerIndex].mzone.Single(card => card?.code == cowboy);
            Require(host.overlayCodes.Count == 2 && duel.RetryCount == 0, "The Xyz fixture did not give Cowboy two materials.");
            ui.Field.Refresh();

            var queued = AnimationSteps(animator).ToArray();
            int gather = Array.FindIndex(queued, step => step.GetType().Name.StartsWith("<GatherXyzMaterials>", StringComparison.Ordinal));
            var arrival = queued.Single(step => FlightCode(step) == cowboy);
            Require(gather >= 0 && gather < Array.IndexOf(queued, arrival) &&
                    queued.Count(step => FlightCode(step) == sparkman) == 0,
                "The engine's two Xyz materials did not converge before the host's arrival.");
            var step = queued[gather];
            Require(step.MoveNext() && step.Current is IEnumerator, "The Xyz materials did not share one moving frame.");
            var sample = TestCardPresentation.SampleAnimation((IEnumerator)step.Current);
            var cards = animator.GetComponentsInChildren<CompositeCardView>().Where(view => view.name == "MovingCard")
                .Select(view => (RectTransform)view.transform).ToArray();
            Require(cards.Length == 2, "The shared Xyz animation omitted a material.");
            Vector2[] start = cards.Select(card => card.anchoredPosition).ToArray();
            float distance = Vector2.Distance(start[0], start[1]);
            sample(.82f);
            Require(distance > 10f && Vector2.Distance(cards[0].anchoredPosition, cards[1].anchoredPosition) < distance * .5f &&
                    cards.Select((card, i) => Vector2.Distance(card.anchoredPosition, start[i])).All(moved => moved > 8f),
                "The Xyz materials did not travel together towards the destination.");
            sample(1f);
            Require(!step.MoveNext() && cards.All(card => !card.gameObject.activeInHierarchy) &&
                    !FieldSlot(ui.Field, host).Find("Card/CardView").gameObject.activeInHierarchy,
                "The Xyz materials lingered or the host arrived before they merged.");
            Require(arrival.MoveNext() && !arrival.MoveNext() &&
                    FieldSlot(ui.Field, host).Find("CardReadout/Labels/Materials/Value").GetComponent<Text>().text == "2",
                "The host did not land with the two assembled materials.");
            Debug.Log("[VERIFY DUEL XYZ MATERIAL] Two native materials converged together before Cowboy appeared with material count 2.");
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
                        detailCard.transform.Find("AspectBox/AtkDef/Stat").GetComponent<Text>().text == $"ATK/{attack}  DEF/{defense}",
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

        private static Dictionary<(byte, uint), CardMoveReadoutSnapshot> PendingMoveReadouts(DuelFieldUI field) =>
            (Dictionary<(byte, uint), CardMoveReadoutSnapshot>)typeof(DuelFieldUI)
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
            Require(combat.gameObject.activeInHierarchy &&
                combat.Find("Attack/Digits/Value").GetComponent<Text>().text == attack &&
                combat.Find("Defense/Digits/Value").GetComponent<Text>().text == defenseValue &&
                (defense ? defenseAccent > attackAccent : attackAccent > defenseAccent) &&
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

                var attackIcon = normal.Find("Combat/Attack/Symbol").GetComponent<Image>();
                var defenseIcon = normal.Find("Combat/Defense/Symbol").GetComponent<Image>();
                Require(attackIcon.sprite != null && defenseIcon.sprite != null && attackIcon.sprite != defenseIcon.sprite &&
                    !attackIcon.raycastTarget && !defenseIcon.raycastTarget,
                    "The compact readout did not have separate non-interactive attack and defense symbols.");
                mine.mzone[1].attack = mine.mzone[1].defense = int.MaxValue;
                mine.mzone[1].rank = 12;
                mine.mzone[1].counterCount = 12;
                mine.mzone[1].overlayCodes.AddRange(Enumerable.Repeat(BlueEyes, 8));
                Field.Refresh();

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
                    VerifyStatGlyphs(Label(normal, attack), "2900");
                    VerifyStatGlyphs(Label(normal, defense), "2800");
                    VerifyStatGlyphs(Label(normal, "Combat/Attack/Change/Arrow"), "▼");
                    VerifyStatGlyphs(Label(normal, "Combat/Defense/Change/Arrow"), "▲");
                    VerifyStatGlyphs(Label(link, attack), "2300");
                    VerifyStatGlyphs(Label(xyz, attack), int.MaxValue.ToString());
                    VerifyStatGlyphs(Label(xyz, defense), int.MaxValue.ToString());
                    VerifyStatGlyphs(Label(xyz, rating), "12");
                    VerifyStatGlyphs(Label(xyz, materials), "10");
                    VerifyStatGlyphs(Label(xyz, counters), "12");
                    VerifyStatGlyphs(Label(xyz, status), "무효");
                    VerifyStatGlyphs(Label(inspector, "Attack/Digits/Value"), "2900", .48f);
                    var caption = RectTransformUtility.CalculateRelativeRectTransformBounds(inspector, inspector.Find("Attack/Caption"));
                    var digits = RectTransformUtility.CalculateRelativeRectTransformBounds(inspector, inspector.Find("Attack/Digits"));
                    Require(digits.max.y < caption.min.y, "The larger inspection number overlapped its caption.");
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
                mine.mzone[1].counterCount = 0;
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

            private static void VerifyStatGlyphs(Text text, string expected, float minimumHeightShare = .78f)
            {
                Require(text.text == expected && text.horizontalOverflow == HorizontalWrapMode.Overflow,
                    "A readout truncated or wrapped its text.");
                using var mesh = new VertexHelper();
                var populate = typeof(Text).GetMethod("OnPopulateMesh", PrivateInstance, null, new[] { typeof(VertexHelper) }, null);
                populate.Invoke(text, new object[] { mesh });
                text.GetComponent<DuelStatDigitFit>().ModifyMesh(mesh);
                int glyphCount = expected.Count(character => !char.IsWhiteSpace(character));
                Require(mesh.currentVertCount == glyphCount * 4 && text.cachedTextGenerator.lineCount == 1,
                    $"The readout omitted or wrapped a glyph in '{expected}'.");
                Rect rect = text.GetPixelAdjustedRect();
                var vertex = default(UIVertex);
                float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
                for (int i = 0; i < mesh.currentVertCount; i++)
                {
                    mesh.PopulateUIVertex(ref vertex, i);
                    Require(vertex.position.x >= rect.xMin - .1f && vertex.position.x <= rect.xMax + .1f &&
                        vertex.position.y >= rect.yMin - .1f && vertex.position.y <= rect.yMax + .1f,
                        "A readout glyph rendered outside its allotted space.");
                    bottom = Mathf.Min(bottom, vertex.position.y);
                    top = Mathf.Max(top, vertex.position.y);
                }
                if (!text.resizeTextForBestFit)
                    Require(top - bottom >= rect.height * .68f, "The field label left too much space unused by its glyphs.");
                var cell = (RectTransform)text.transform.parent;
                if (cell.name is "Digits" or "Number") cell = (RectTransform)cell.parent;
                Require(rect.height / cell.rect.height > minimumHeightShare, "The readout text did not use enough of its allotted height.");
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
