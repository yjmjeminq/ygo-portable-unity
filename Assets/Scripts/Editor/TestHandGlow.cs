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

                normalSlot.GetComponent<CardDragTrigger>().OnRightClick();
                var inspector = Field.Root.transform.Find("LeftInspector/CardDetails/CombatStats");
                Require(Label(inspector, "Attack/Digits/Value").text == "3700",
                    "Inspecting a field monster lost its current attack.");
                Slot("_myMZone", 2).GetComponent<CardDragTrigger>().OnRightClick();
                Require(Label(inspector, "Attack/Digits/Value").text == "2800",
                    "Inspecting the second copy of a card kept the first copy's current stats.");
                var details = (Button)typeof(DuelFieldUI).GetField("_duelPadInspectButton", PrivateInstance).GetValue(Field);
                details.onClick.Invoke();
                var zoom = Field.Root.transform.Find("Overlay_CardZoom/ZoomWin/Info/CombatStats");
                mine.mzone[2].attack = 4200;
                Field.Refresh();
                Require(Label(zoom, "Attack/Digits/Value").text == "4200" &&
                    Label(inspector, "Attack/Digits/Value").text == "4200",
                    "An open details panel did not follow a change to the selected card's live stats.");
                typeof(DuelFieldUI).GetMethod("CloseCardZoomModal", PrivateInstance).Invoke(Field, new object[] { false });

                var viewport = (RectTransform)Canvas.transform;
                var originalSize = viewport.sizeDelta;
                foreach (var size in new[] { new Vector2(1600, 900), new Vector2(1024, 768), new Vector2(1740, 805) })
                {
                    viewport.sizeDelta = size;
                    Canvas.ForceUpdateCanvases();
                    foreach (var readout in new[] { normal, xyz, link })
                    foreach (var path in new[] { attack, defense, rating, "Combat/Attack/Caption/Label", "Combat/Defense/Caption/Label" })
                    {
                        var label = Label(readout, path);
                        if (!label.gameObject.activeInHierarchy) continue;
                        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(readout, label.rectTransform);
                        Require(readout.rect.Contains(bounds.min) && bounds.max.x <= readout.rect.xMax + .01f &&
                            bounds.max.y <= readout.rect.yMax + .01f && label.rectTransform.rect.height > 9f,
                            $"A field stat overflowed when the canvas resized to {size}.");
                    }
                }
                viewport.sizeDelta = originalSize;

                mine.mzone[0].level = 0;
                theirs.mzone[6].position = YgoConstants.POS_FACEDOWN_DEFENSE;
                mine.mzone[1].overlayCodes.Clear();
                Field.Refresh();
                Require(!Label(normal, rating).gameObject.activeInHierarchy && !link.gameObject.activeInHierarchy &&
                    Label(xyz, status).text == "소재 0 · 무효", "A field readout kept stale state when a card changed.");
                mine.mzone[0] = mine.mzone[1] = mine.mzone[2] = theirs.mzone[6] = mine.szone[0] = null;
                Field.Refresh();
                Require(!xyz.gameObject.activeInHierarchy && !scale.gameObject.activeInHierarchy,
                    "A vacated zone retained a rating or pendulum scale.");
                typeof(DuelFieldUI).GetMethod("ResetCardInspector", PrivateInstance).Invoke(Field, null);
                Debug.Log("[VERIFY DUEL CARD READOUT] Verified live combat stats, changing copies, ratings, scales, Link markers, face-down cards and three viewport sizes.");
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
