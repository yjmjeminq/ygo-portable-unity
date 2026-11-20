using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using YGOPortable.AI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static class TestDuelZoneBrowser
    {
        private const uint PotOfDesires = 35261759;
        private const uint MoYe = 20001443;
        private const uint Longyuan = 56495147;
        private const uint Chixiao = 69248256;
        private static readonly uint[] ExtraDeck = { Chixiao, 44508094 };
        private static readonly TacticalAI Ai = new TacticalAI { Difficulty = AIDifficulty.Smart };

        internal static void Run()
        {
            VerifyMobileDuelSafeArea();
            int opens = 0;
            foreach (bool humanFirst in new[] { true, false })
            {
                opens += VerifyBanishAndGrave(humanFirst);
                opens += VerifyGraveEffectPicker(humanFirst);
            }
            opens += VerifyPromptAndExtraSummon();
            opens += VerifyNativeExtraActivation();
            VerifyNativeBanishedTrigger();
            string hitTest = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ? "raycast filters" : "GraphicRaycaster";
            Debug.Log($"[VERIFY ZONE BROWSER] {opens} successful opens. Pile counts and visible tops, tabs, card details, filtered action shortcuts, privacy and effect prompt field view ({hitTest}) verified.");
        }

        private static void VerifyMobileDuelSafeArea()
        {
            const uint dragon = 89631139, attacker = 17444133;
            var display = new Vector2Int(2400, 1080);
            var canvas = new GameObject("Mobile Duel Safe Area", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var screen = (RectTransform)canvas.transform;
            screen.sizeDelta = (Vector2)display;
            using var duel = new DuelSession();
            duel.Players[0].mzone[2] = new LiveCardState
            {
                code = dragon, type = YgoConstants.TYPE_MONSTER, position = YgoConstants.POS_FACEUP_ATTACK,
                sequence = 2, location = YgoConstants.LOCATION_MZONE, level = 7, attack = 3400, defense = 2500,
            };
            duel.Players[1].mzone[1] = new LiveCardState
            {
                code = attacker, type = YgoConstants.TYPE_MONSTER, position = YgoConstants.POS_FACEUP_ATTACK,
                controller = 1, sequence = 1, location = YgoConstants.LOCATION_MZONE, level = 4, attack = 1700, defense = 1650,
            };
            const uint pendulum = 11609969;
            var publicExtra = new LiveCardState
            {
                code = pendulum, controller = 1, location = YgoConstants.LOCATION_EXTRA, position = YgoConstants.POS_FACEUP_ATTACK,
            };
            duel.Players[0].deckCount = 36;
            duel.Players[1].extra.Add(publicExtra);
            var modals = new DuelPromptModals(screen, null, null);
            var field = new DuelFieldUI(null, screen, modals, mobileLayout: true);
            CanvasGroup battle = null;
            try
            {
                field.BindSession(duel, null, null);
                var viewport = field.Root.transform.Find("MobileSafeArea");
                var backdrop = field.Root.transform.Find("ArenaGround").GetComponent<DuelArenaGround>();
                var surface = viewport.Find("DuelBoard/SlabSurface").GetComponent<DuelMatSurface>();
                Require(backdrop.texture != null && backdrop.texture.name == "ObservatoryBackdrop" &&
                    surface.texture != null && surface.texture.name == "ObservatorySlab" &&
                    !backdrop.raycastTarget && !surface.raycastTarget,
                    "The arena did not use its bundled art or intercepted field input.");
                var left = viewport.Find("LeftInspector");
                var slot = (RectTransform)viewport.Find("DuelBoard/Zone_4_3");
                var actions = (RectTransform)viewport.Find("CardActions");
                actions.sizeDelta = new Vector2(100f, 40f);
                slot.GetComponent<CardDragTrigger>().OnRightClick();
                left.Find("CardPreview/Card/AspectBox/ZoomHit").GetComponent<Button>().onClick.Invoke();
                var overlay = field.Root.transform.Find("Overlay_CardZoom");
                var details = overlay.Find("MobileSafeArea");
                Require(details.Find("ZoomWin/Hdr/Related") == null,
                    "Duel card details still offer related-card browsing.");
                var animator = CardAnimationManager.Ensure(canvas);
                var myDeck = viewport.Find("DuelBoard/Zone_7_4");
                var opponentExtra = viewport.Find("DuelBoard/Zone_7_0");
                VerifyPileReadout(myDeck, 36, 0);
                VerifyPileReadout(opponentExtra, 1, pendulum);
                battle = animator.CreateBattleReadout(DuelFieldUI.MobileBattleStatsArea,
                    new BattleCardStats(1, YgoConstants.LOCATION_MZONE, 1, YgoConstants.POS_FACEUP_ATTACK, 1700, 1650),
                    new BattleCardStats(0, YgoConstants.LOCATION_MZONE, 2, YgoConstants.POS_FACEUP_ATTACK, 3400, 2500),
                    0, false, false, mobileLayout: true);

                Rect Bounds(Transform node)
                {
                    var rect = (RectTransform)node;
                    Vector2 min = (Vector2)screen.InverseTransformPoint(rect.TransformPoint(rect.rect.min)) - screen.rect.min;
                    Vector2 max = (Vector2)screen.InverseTransformPoint(rect.TransformPoint(rect.rect.max)) - screen.rect.min;
                    return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                }

                var placeActions = typeof(DuelFieldUI).GetMethod("PositionCardActions", BindingFlags.Instance | BindingFlags.NonPublic);
                var cardPose = typeof(DuelFieldUI).GetMethod("GetCardPose", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (var safe in new[] { new Rect(144, 36, 2256, 1044), new Rect(0, 36, 2256, 1044) })
                {
                    viewport.GetComponent<MobileSafeArea>().Reflow(safe, display);
                    details.GetComponent<MobileSafeArea>().Reflow(safe, display);
                    battle.GetComponent<MobileSafeArea>().Reflow(safe, display);
                    Canvas.ForceUpdateCanvases();
                    placeActions.Invoke(field, new object[] { slot, false });

                    foreach (var element in new[] { left, left.Find("CardDetails/ArtworkControls"), left.Find("CardDetails/CombatStats"), left.Find("CardDetails/Description/Viewport"),
                        slot.Find("CardReadout"), viewport.Find("DuelBoard"), viewport.Find("Turn/Dial"),
                        viewport.Find("PlayerHUD"), viewport.Find("OpponentHUD"),
                        viewport.Find("ChainConfirmation/Button"), viewport.Find("OpenLog"),
                        details.Find("ZoomWin"), battle.transform })
                    {
                        Rect bounds = Bounds(element);
                        Require(bounds.xMin >= safe.xMin - .1f && bounds.xMax <= safe.xMax + .1f &&
                            bounds.yMin >= safe.yMin - .1f && bounds.yMax <= safe.yMax + .1f && bounds.width > 0f,
                            $"{element.name} intersected the device cutout or home indicator.");
                    }
                    var chainControl = (RectTransform)viewport.Find("ChainConfirmation/Button");
                    float minimum = MobileSafeArea.MinimumTouchSize(chainControl);
                    Require(chainControl.rect.width >= minimum - .1f && chainControl.rect.height >= minimum - .1f,
                        "The chain confirmation toggle is too small after mobile safe area reflow.");

                    foreach (string hudName in new[] { "PlayerHUD", "OpponentHUD" })
                    {
                        var status = viewport.Find(hudName + "/Status");
                        var portrait = Bounds(status.Find("AvatarFrame/PortraitPlate"));
                        var readout = Bounds(status.Find("Readout"));
                        Require(!portrait.Overlaps(readout) && portrait.width > 0f,
                            $"{hudName} let its portrait overlap the name or LP.");
                    }

                    foreach (var (pile, controller, location) in new[]
                    {
                        (myDeck, (byte)0, YgoConstants.LOCATION_DECK), (opponentExtra, (byte)1, YgoConstants.LOCATION_EXTRA),
                    })
                    {
                        var caption = Bounds(pile.Find("Total/Caption"));
                        var count = Bounds(pile.Find("Total/Amount"));
                        Require(!caption.Overlaps(count) && count.width > 0f, "The pile count overlapped its label.");
                        var art = (RectTransform)pile.Find("TopCard/CardView/AspectBox");
                        var face = animator.GetVisualPose(art);
                        var flight = (CardPose)cardPose.Invoke(field,
                            new object[] { controller, location, 0u, YgoConstants.POS_FACEUP_ATTACK, -1 });
                        Require(flight.IsValid && Vector2.Distance(face.Position, flight.Position) < .15f &&
                            Vector2.Distance(face.Size, flight.Size) < .15f && Mathf.Abs(Mathf.DeltaAngle(face.Angle, flight.Angle)) < .1f,
                            "The card flight did not line up with its pile.");
                    }

                    var preview = Bounds(left);
                    var comparison = Bounds(battle.transform);
                    var hand = Bounds(viewport.Find("PlayerHand"));
                    Require(Mathf.Abs(preview.xMin - comparison.xMin) < .1f &&
                        comparison.yMax < preview.yMin && comparison.yMin > Bounds(viewport.Find("PlayerHUD")).yMax &&
                        comparison.xMax > preview.xMax && comparison.xMax < hand.xMin,
                        "The mobile combat comparison overlapped the inspector, player HUD, or hand.");
                    foreach (var (side, expectedRole, expectedValue) in new[]
                    {
                        ("Attacker", "상대 공격", "1700"), ("Defender", "내 대상", "3400"),
                    })
                    {
                        var segment = battle.transform.Find(side);
                        var title = segment.Find("Role/Text").GetComponent<Text>();
                        var number = segment.Find("Values/Attack/Digits/Value").GetComponent<Text>();
                        var symbol = segment.Find("Values/Attack/Symbol");
                        var digits = Bounds(number.rectTransform);
                        Require(title.text == expectedRole && number.text == expectedValue &&
                            !segment.Find("Values/Defense").gameObject.activeSelf &&
                            digits.width > comparison.width * .15f && digits.height > comparison.height * .75f &&
                            !digits.Overlaps(Bounds(title.rectTransform)) &&
                            !digits.Overlaps(Bounds(symbol)),
                            "The mobile comparison squeezed the relevant number or overlapped its role or symbol.");
                    }
                    Require(Mathf.Abs(Bounds(actions).center.x - Bounds(slot).center.x) < .1f,
                        "The card actions lost alignment with the selected card after a layout change.");
                    Require(Bounds(overlay).size == (Vector2)display && Bounds(field.Root.transform.Find("ArenaGround")).size == (Vector2)display,
                        "The full-screen background or card details backdrop moved into the safe area.");
                }

                publicExtra.position = YgoConstants.POS_FACEDOWN_DEFENSE;
                field.Refresh();
                VerifyPileReadout(opponentExtra, 1, 0);
                duel.Players[1].extra.Clear();
                field.Refresh();
                VerifyPileReadout(opponentExtra, 0, 0);
                Require(left.Find("CardDetails/CombatStats/Attack/Digits/Value").GetComponent<Text>().text == "3400" &&
                    details.Find("ZoomWin/Info/CombatStats/Attack/Digits/Value").GetComponent<Text>().text == "3400",
                    "The mobile layout lost the selected monster's live stats.");
                details.Find("ZoomWin/Hdr/Close/B").GetComponent<Button>().onClick.Invoke();
                duel.AttackerCon = 1;
                duel.AttackerLoc = duel.TargetLoc = YgoConstants.LOCATION_MZONE;
                duel.AttackerSeq = 1;
                duel.TargetSeq = 2;
                typeof(DuelSession).GetProperty(nameof(DuelSession.HasActiveAttack)).SetValue(duel, true);
                modals.ShowPrompt(duel, new DuelPrompt { messageType = YgoConstants.MSG_SELECT_EFFECTYN,
                    promptCardCode = dragon, promptLoc = YgoConstants.LOCATION_MZONE, promptSeq = 2 });
                field.Refresh();
                var arrow = viewport.Find("ResponseAttackArrow");
                Vector2 midpoint = (Bounds(viewport.Find("DuelBoard/Zone_5_1")).center + Bounds(slot).center) * .5f;
                Require(arrow.gameObject.activeInHierarchy && Vector2.Distance(Bounds(arrow).center, midpoint) < .2f,
                    "The response arrow ended up offset from the two cards after rotation.");
                Debug.Log("[VERIFY DUEL SAFE AREA] Both landscape insets kept the field, live stats, pile counts, pile flight endpoints and card details aligned.");
            }
            finally
            {
                if (battle != null) UnityEngine.Object.DestroyImmediate(battle.gameObject);
                field.UnbindSession();
                UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            }
        }

        internal static byte[] SelectFieldTarget(DuelSession duel, DuelPrompt prompt)
        {
            byte[] response = null;
            using var ui = new Browser(duel, bytes => response = bytes);
            ui.VerifyFieldTarget(prompt);
            Require(response != null && response.Length == 12 && BitConverter.ToInt32(response, 0) == 0 &&
                BitConverter.ToInt32(response, 4) == 1 && BitConverter.ToInt32(response, 8) == 1,
                "The field picker did not submit the original native index of the second target.");
            return response;
        }

        internal static void VerifyAttackTargetClicks(DuelSession duel, DuelPrompt prompt)
        {
            var responses = new List<byte[]>();
            using var ui = new Browser(duel, responses.Add);
            ui.VerifyAttackComparison(prompt);
            Require(responses.Count == 0, "Previewing an attack target submitted the attack.");
            foreach (int pointer in new[] { -1, 0 })
            {
                ui.ClickAttackTarget(prompt, pointer);
                Require(responses.Count == 1 && responses[0].SequenceEqual(DuelSession.PackCardSelection(0)),
                    "A mouse click or touch on the attack target did not submit its native index immediately.");
                responses.Clear();
            }
        }

        private static int VerifyBanishAndGrave(bool humanFirst)
        {
            var main = Enumerable.Repeat(PotOfDesires, 20).Concat(Enumerable.Repeat(89631139u, 20)).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Zone browser banish", main, ExtraDeck, main, ExtraDeck, humanFirst,
                session => session.Players.All(player => player.hand.Any(card => card.code == PotOfDesires)));
            using var ui = new Browser(duel);
            var mine = duel.Players[duel.HumanPlayerIndex];
            var opponent = duel.Players[duel.AiPlayerIndex];
            ui.VerifyPileReadout("_myGyTile", 0, 0);
            ui.VerifyPileReadout("_oppGyTile", 0, 0);
            ui.VerifyOwnZones(mine);
            ui.OpenStack("_oppExtraTile", opponent.extra.Select(_ => 0u));
            ui.Close();

            for (byte player = 0; player < 2; player++)
            {
                var prompt = ReachMainPhase(duel, player);
                ui.VerifyOwnZones(mine);
                Require(ReferenceEquals(duel.ActivePrompt, prompt), "Browsing before a play changed the current main-phase prompt.");
                if (player == duel.HumanPlayerIndex)
                    ui.VerifyPhasePicker(prompt);
                else
                {
                    ui.VerifyAiWaitsWhileBrowsing(mine);
                    ui.VerifyAiWaitsWhileReadingCard(mine.hand.First());
                }

                int activate = prompt.activatable.FindIndex(card => card.code == PotOfDesires);
                Require(activate >= 0, "The deterministic Pot of Desires fixture is missing its activation.");
                if (player == duel.HumanPlayerIndex)
                    ui.ActivateHandCardOverBoard(prompt.activatable[activate], touch: humanFirst);
                else
                    duel.SubmitResponseInt((activate << 16) | 5);
                prompt = ReachMainPhase(duel, player);
                var resolved = duel.Players[player];
                Require(resolved.grave.Any(card => card.code == PotOfDesires) &&
                        resolved.banished.Count == 10 && resolved.banished.All(card => !card.IsFaceUp && card.code != 0),
                    "Pot of Desires did not reach the expected face-down banish state.");
                ui.Field.Refresh();
                string side = player == duel.HumanPlayerIndex ? "_my" : "_opp";
                ui.VerifyPileReadout(side + "GyTile", resolved.grave.Count, PotOfDesires);
                ui.VerifyPileReadout(side + "BanTile", resolved.banished.Count, 0);
                ui.VerifyPileReadout(side + "DeckTile", resolved.deckCount, 0);
                ui.VerifyOwnZones(mine);
                ui.OpenStack("_oppBanTile", opponent.banished.Select(_ => 0u));
                ui.Close();
                Require(ReferenceEquals(duel.ActivePrompt, prompt), "Checking the stacks after an effect advanced the engine.");
                if (player == 0) duel.SubmitResponseInt(7);
            }
            ui.VerifyPileTabsAndDetails(mine, opponent);
            Require(duel.RetryCount == 0 && !duel.IsFinished, "Zone inspection left a retry or ended the duel.");
            return ui.Opens;
        }

        private static int VerifyGraveEffectPicker(bool humanFirst)
        {
            const uint foolishBurial = 81439173;
            const uint jetSynchron = 9742784;
            var empty = Array.Empty<uint>();
            var main = Enumerable.Repeat(foolishBurial, 12).Concat(Enumerable.Repeat(jetSynchron, 12))
                .Concat(Enumerable.Repeat(20721928u, 16)).ToArray();
            var opponent = Enumerable.Repeat(89631139u, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Grave effect shortcut", main, empty, opponent, empty,
                humanFirst, session => session.Players[session.HumanPlayerIndex].hand.Any(card => card.code == foolishBurial));
            byte human = (byte)duel.HumanPlayerIndex;
            var prompt = ReachMainPhase(duel, human);
            using var ui = new Browser(duel);
            ui.VerifyGraveEffectButton(visible: false);

            int burial = prompt.activatable.FindIndex(item => item.code == foolishBurial);
            Require(burial >= 0, "The fixture could not activate Foolish Burial.");
            duel.SubmitResponseInt((burial << 16) | 5);
            prompt = ReachMainPhase(duel, human, choice =>
            {
                int target = choice.candidates.FindIndex(item => item.code == jetSynchron && item.location == YgoConstants.LOCATION_DECK);
                if (target < 0) return null;
                return choice.messageType switch
                {
                    YgoConstants.MSG_SELECT_CARD => DuelSession.PackCardSelection(target),
                    YgoConstants.MSG_SELECT_UNSELECT_CARD => BitConverter.GetBytes(1).Concat(BitConverter.GetBytes(target)).ToArray(),
                    _ => null,
                };
            });
            Require(duel.Players[human].grave.Any(card => card.code == jetSynchron), "Foolish Burial did not send Jet Synchron.");
            ui.VerifyGraveEffectChoices(prompt, jetSynchron);
            ReachMainPhase(duel, human);
            ui.VerifyGraveEffectButton(visible: false);
            Require(duel.Players[human].mzone.Any(card => card?.code == jetSynchron) && duel.RetryCount == 0,
                "The native duel did not resolve the selected grave effect.");
            return ui.Opens;
        }

        private static void VerifyNativeBanishedTrigger()
        {
            const uint goldSarcophagus = 75500286, dotscaper = 18789533;
            var empty = Array.Empty<uint>();
            var main = Enumerable.Repeat(goldSarcophagus, 12).Concat(Enumerable.Repeat(dotscaper, 12))
                .Concat(Enumerable.Repeat(20721928u, 16)).ToArray();
            var opponent = Enumerable.Repeat(89631139u, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Banished effect confirmation", main, empty, opponent, empty, true,
                session => session.ActivePrompt?.activatable.Any(card => card.code == goldSarcophagus) == true);
            byte[] submitted = null;
            using var ui = new Browser(duel, bytes => submitted = bytes);
            int spell = duel.ActivePrompt.activatable.FindIndex(card => card.code == goldSarcophagus);
            duel.SubmitResponseInt((spell << 16) | 5);
            bool confirmed = false;
            ReachMainPhase(duel, 0, prompt =>
            {
                if (prompt.messageType == YgoConstants.MSG_SELECT_CARD)
                {
                    int index = prompt.candidates.FindIndex(card => card.code == dotscaper && card.location == YgoConstants.LOCATION_DECK);
                    if (index >= 0) return DuelSession.PackCardSelection(index);
                }

                bool trigger = prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == dotscaper ||
                    prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && prompt.candidates.Any(card => card.code == dotscaper);
                if (!trigger || confirmed) return null;
                ui.Modals.ShowPrompt(duel, prompt, ChainConfirmationMode.Off);
                ui.Field.Refresh();
                var shortcut = Private<Button>(ui.Field, "_banishedEffectButton");
                var glow = shortcut.transform.parent.parent.Find("PlayableGlow");
                Require(shortcut.gameObject.activeInHierarchy && glow.gameObject.activeInHierarchy,
                    "A live, optional banished trigger did not light its zone or show its action.");
                shortcut.onClick.Invoke();
                Require(ui.Modals.TryConfirm() && submitted != null, "The banished effect shortcut did not select the pending trigger.");
                confirmed = true;
                return submitted;
            });
            Require(confirmed && duel.Players[0].mzone.Any(card => card?.code == dotscaper) && duel.RetryCount == 0,
                "The banished Dotscaper did not return through the native effect confirmation.");
            Debug.Log("[VERIFY BANISHED ACTIVATE] Dotscaper kept its native optional trigger in Off, lit the pile and resolved from the action shortcut. Retry=0");
        }

        private static int VerifyPromptAndExtraSummon()
        {
            var main = Enumerable.Repeat(MoYe, 20).Concat(Enumerable.Repeat(Longyuan, 20)).ToArray();
            var otherMain = Enumerable.Repeat(20721928u, 40).ToArray();
            var synchroExtra = new[] { 44508094u, 96633955u, Chixiao };
            using var duel = YgoProjectBuilder.StartSeedMatching("Zone browser Synchro", main, synchroExtra, otherMain, ExtraDeck, true,
                session => session.Players[0].hand.Any(card => card.code == MoYe) &&
                           session.Players[0].hand.Any(card => card.code == Longyuan));
            Action<byte[]> submit = duel.SubmitResponse;
            using var ui = new Browser(duel, bytes => submit(bytes));
            var mine = duel.Players[0];
            var mainPrompt = ReachMainPhase(duel, 0);
            ui.VerifyNoExtraSummon();
            int normalSummon = mainPrompt.summonable.FindIndex(card => card.code == MoYe);
            Require(normalSummon >= 0, "Mo Ye is not available in the fixture.");
            duel.SubmitResponseInt(normalSummon << 16);

            bool inspectedDuringPrompt = false;
            bool choseSummon = false;
            bool choseMaterials = false;
            for (int step = 0; step < 40 && !mine.mzone.Any(card => card?.code == Chixiao); step++)
            {
                var prompt = duel.ActivePrompt;
                if (prompt == null)
                {
                    duel.StepUntilPrompt();
                    continue;
                }

                if (prompt.playerId == 0 && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
                    prompt.spSummonable.Any(card => card.code == Chixiao))
                {
                    ui.VerifyExtraSummonChoices(prompt, mine.extra);
                    choseSummon = true;
                    continue;
                }

                if (choseSummon && prompt.playerId == 0 && (prompt.messageType == YgoConstants.MSG_SELECT_SUM ||
                    prompt.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD))
                {
                    ui.SelectSynchroMaterials(prompt);
                    choseMaterials = true;
                    continue;
                }

                bool cardPrompt = prompt.messageType == YgoConstants.MSG_SELECT_CHAIN ||
                                  prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN ||
                                  prompt.messageType == YgoConstants.MSG_SELECT_CARD;
                if (!inspectedDuringPrompt && prompt.playerId == 0 && cardPrompt)
                {
                    ui.VerifyPromptFieldView(prompt);
                    inspectedDuringPrompt = true;
                }

                if (prompt.playerId == 0 && prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN)
                {
                    duel.SubmitResponseInt(1);
                }
                else if (prompt.playerId == 0 && prompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
                {
                    int index = prompt.candidates.FindIndex(card => card.code == MoYe);
                    duel.SubmitResponseInt(index >= 0 || !prompt.forced ? index : 0);
                }
                else
                {
                    duel.SubmitResponse(Ai.ComputeResponse(duel, prompt));
                }
            }

            Require(inspectedDuringPrompt && choseSummon && choseMaterials && mine.mzone.Any(card => card?.code == Chixiao) && duel.RetryCount == 0,
                "The Synchro summon or modal inspection could not finish.");
            ui.Field.Refresh();
            ui.VerifyOwnZones(mine);
            Require(mine.extra.All(card => card.code != Chixiao) && mine.grave.Any(card => card.code == MoYe),
                "The three stacks no longer match the native duel after the summon.");
            var summoned = mine.mzone.Single(card => card?.code == Chixiao);
            byte[] confirmation = null;
            submit = bytes => confirmation = bytes;
            ui.ConfirmFieldMaterials(summoned);
            Require(confirmation is { Length: 4 } && BitConverter.ToInt32(confirmation, 0) == -1,
                "The field confirmation did not submit the native finish response.");
            ui.VerifyAttackResponse(summoned);
            return ui.Opens;
        }

        private static int VerifyNativeExtraActivation()
        {
            const uint refrain = 64881644, bacha = 56208713, polymerization = 24094653, giantTrunade = 42703248;
            var main = Enumerable.Repeat(refrain, 20).Concat(Enumerable.Repeat(polymerization, 12))
                .Concat(Enumerable.Repeat(giantTrunade, 8)).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Extra Deck activation shortcut", main, new[] { bacha },
                Enumerable.Repeat(20721928u, 40).ToArray(), Array.Empty<uint>(), true, session =>
                {
                    var hand = session.Players[session.HumanPlayerIndex].hand;
                    return hand.Count(card => card.code == refrain) >= 2 && hand.Any(card => card.code == polymerization) &&
                        hand.Any(card => card.code == giantTrunade);
                });
            byte human = (byte)duel.HumanPlayerIndex;
            var player = duel.Players[human];
            var command = ReachMainPhase(duel, human);
            int summon = command.summonable.FindIndex(item => item.code == refrain);
            Require(summon >= 0, "Refrain was not available for the Extra Deck fixture.");
            duel.SubmitResponseInt(summon << 16);
            command = ReachMainPhase(duel, human);
            int spell = command.activatable.FindIndex(item => item.code == polymerization);
            Require(spell >= 0, "The Melodious fixture could not start the Fusion Summon.");
            duel.SubmitResponseInt((spell << 16) | 5);

            int ExtraResponse(DuelPrompt prompt)
            {
                if (prompt?.playerId != human) return -1;
                if (prompt.IsTriggerActivationConfirmation && prompt.promptLoc == YgoConstants.LOCATION_EXTRA &&
                    prompt.promptCardCode == refrain) return 1;
                return prompt.messageType == YgoConstants.MSG_SELECT_CHAIN
                    ? prompt.candidates.FindIndex(item => item.location == YgoConstants.LOCATION_EXTRA && item.code == refrain)
                    : -1;
            }

            for (int step = 0; step < 40 && !duel.IsFinished && ExtraResponse(duel.ActivePrompt) < 0; step++)
            {
                var prompt = duel.ActivePrompt;
                if (prompt == null)
                {
                    duel.StepUntilPrompt();
                    continue;
                }
                int fieldMaterial = prompt.candidates.FindIndex(item => item.code == refrain && item.location == YgoConstants.LOCATION_MZONE);
                int handMaterial = prompt.candidates.FindIndex(item => item.code == refrain && item.location == YgoConstants.LOCATION_HAND);
                if (prompt.messageType == YgoConstants.MSG_SELECT_CARD && fieldMaterial >= 0)
                    duel.SubmitResponse(prompt.min > 1 && handMaterial >= 0
                        ? DuelSession.PackCardSelection(fieldMaterial, handMaterial) : DuelSession.PackCardSelection(fieldMaterial));
                else if (prompt.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD && !prompt.finishable && fieldMaterial >= 0)
                    duel.SubmitResponse(BitConverter.GetBytes(1).Concat(BitConverter.GetBytes(fieldMaterial)).ToArray());
                else if (prompt.IsTriggerActivationConfirmation)
                    duel.SubmitResponseInt(0);
                else
                    duel.SubmitResponse(Ai.ComputeResponse(duel, prompt));
            }

            var activation = duel.ActivePrompt;
            int expected = ExtraResponse(activation);
            Require(expected >= 0 && player.extra.Any(card => card.code == refrain && card.IsFaceUp) &&
                player.mzone.Any(card => card?.code == bacha), "The engine did not offer Refrain's Extra Deck trigger.");
            byte[] selected = null;
            using var ui = new Browser(duel, response => selected = response, mobileLayout: true);
            ui.VerifyExtraChainSource(activation, refrain);
            Require(selected != null && BitConverter.ToInt32(selected, 0) == expected,
                "The Extra Deck shortcut selected a different effect in the native chain.");
            var animator = CardAnimationManager.Ensure(ui.Canvas);
            duel.SubmitResponse(selected);
            command = ReachMainPhase(duel, human);
            var scale = player.szone.SingleOrDefault(card => card?.code == refrain);
            Require(duel.RetryCount == 0 && scale != null, "The Extra Deck effect did not enter the Pendulum Zone.");
            TestHandGlow.VerifyPendulumScaleFlight(ui.Field, animator, scale, arriving: true);
            Require(!Private<Button>(ui.Field, "_extraEffectButton").gameObject.activeInHierarchy,
                "The resolved Extra Deck effect retained an expired indicator.");

            int returnSpell = command.activatable.FindIndex(item => item.code == giantTrunade);
            Require(returnSpell >= 0, "The native fixture could not return the scale to hand.");
            duel.SubmitResponseInt((returnSpell << 16) | 5);
            ReachMainPhase(duel, human);
            TestHandGlow.VerifyPendulumScaleFlight(ui.Field, animator, scale, arriving: false);
            Require(duel.RetryCount == 0 && player.szone[scale.sequence] == null &&
                    player.hand.Any(card => card.code == refrain),
                "The returning Pendulum card did not finish in hand.");
            Debug.Log("[VERIFY EXTRA DECK EFFECT] Refrain was offered in the Extra Deck and resolved in the Pendulum Zone.");
            return ui.Opens;
        }

        private static DuelPrompt ReachMainPhase(DuelSession duel, byte player, Func<DuelPrompt, byte[]> choose = null)
        {
            for (int step = 0; step < 30 && !duel.IsFinished; step++)
            {
                var prompt = duel.ActivePrompt;
                if (prompt == null)
                    duel.StepUntilPrompt();
                else if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && prompt.playerId == player)
                    return prompt;
                else
                    duel.SubmitResponse(choose?.Invoke(prompt) ?? Ai.ComputeResponse(duel, prompt));
            }
            throw new InvalidOperationException($"Could not reach main phase for player {player}.");
        }

        private static void VerifyPileReadout(Transform pile, int count, uint visibleCode)
        {
            var amount = pile.Find("Total/Amount/Count").GetComponent<Text>();
            var top = pile.Find("TopCard/CardView").GetComponent<CompositeCardView>();
            Require(amount.text == count.ToString() && top.gameObject.activeSelf == (count > 0),
                "The board pile did not track the native count.");
            if (count == 0) return;
            bool back = top.transform.Find("AspectBox/CardBack").gameObject.activeSelf;
            Require(top.CurrentCode == visibleCode && back == (visibleCode == 0),
                "The board pile lost a public top card or exposed a hidden one.");
        }

        private sealed class Browser : IDisposable
        {
            private readonly GameObject _canvas;
            internal Canvas Canvas => _canvas.GetComponent<Canvas>();
            private readonly DuelSession _duel;
            private readonly RectTransform _grid;
            private readonly GameAppController _controller;
            private readonly Camera _camera;
            private readonly RenderTexture _frame;

            internal DuelFieldUI Field { get; }
            internal DuelPromptModals Modals { get; }
            internal uint InspectedCode { get; private set; }
            internal int Opens { get; private set; }
            internal CompositeCardView[] VisibleCards => _grid.GetComponentsInChildren<CompositeCardView>();

            internal Browser(DuelSession duel, Action<byte[]> onSubmit = null, bool mobileLayout = false)
            {
                _duel = duel;
                _canvas = new GameObject("Zone Browser Verification", typeof(Canvas), typeof(GraphicRaycaster));
                var canvas = _canvas.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10;
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                {
                    _camera = new GameObject("Zone Browser Camera", typeof(Camera)).GetComponent<Camera>();
                    _camera.enabled = false;
                    _camera.orthographic = true;
                    _camera.orthographicSize = 450f;
                    _camera.aspect = 1600f / 900f;
                    _camera.transform.position = Vector3.back * 10f;
                    _frame = new RenderTexture(640, 360, 24);
                    _camera.targetTexture = _frame;
                    canvas.renderMode = RenderMode.WorldSpace;
                    canvas.worldCamera = _camera;
                    ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
                    canvas.transform.position = Vector3.zero;
                }
                Modals = new DuelPromptModals(_canvas.transform, code =>
                {
                    InspectedCode = code;
                    Field?.InspectCard(code);
                },
                    onSubmit ?? (_ => throw new InvalidOperationException("Browsing submitted a modal response.")));
                _controller = _canvas.AddComponent<GameAppController>();
                Field = new DuelFieldUI(_controller, _canvas.transform, Modals, mobileLayout);
                SetPrivate(_controller, "_session", _duel);
                SetPrivate(_controller, "_duelField", Field);
                SetPrivate(_controller, "_modals", Modals);
                Field.BindSession(_duel, null, null);
                _grid = Private<RectTransform>(Modals, "_zoneGrid");
            }

            internal void VerifyPromptFieldView(DuelPrompt prompt)
            {
                var mine = _duel.Players[_duel.HumanPlayerIndex];
                var theirs = _duel.Players[_duel.AiPlayerIndex];
                Field.Refresh();
                Modals.ShowPrompt(_duel, prompt);
                var modal = Modals.ModalRoot.transform;
                var panel = modal.Find("ChainWin").gameObject.activeSelf ? modal.Find("ChainWin") : modal.Find("Win");
                var fieldView = modal.Find("FieldView/B") as RectTransform;
                bool ShowingField() => Modals.IsPromptOpen && !modal.Find("Win").gameObject.activeSelf &&
                    !modal.Find("ChainWin").gameObject.activeSelf;
                Require(panel.gameObject.activeInHierarchy && fieldView != null,
                    "The effect prompt did not start with its chooser visible.");
                var selected = Private<HashSet<int>>(Modals, "_selectedIndices").ToArray();
                int chainIndex = Private<int>(Modals, "_chainSelection");
                uint inspected = Private<uint>(Field, "_inspectedCode");
                if (inspected == 0)
                {
                    inspected = mine.hand.First().code;
                    Field.InspectCard(inspected);
                }
                VerifyInspectorArtwork(inspected, prompt, pointerId: 0);
                Require(panel.gameObject.activeInHierarchy && Private<int>(Modals, "_chainSelection") == chainIndex &&
                    Private<HashSet<int>>(Modals, "_selectedIndices").SetEquals(selected),
                    "Reading the preview changed the pending chooser or its selection.");

                var extra = StackRoot("_myExtraTile");
                var blocked = Raycast(extra, out _);
                Require(blocked != null && blocked.transform.IsChildOf(modal),
                    "The active chooser let clicks reach the field underneath it.");
                ClickAt(fieldView);
                Require(ShowingField() && !Modals.TryConfirm(),
                    "Field view did not hide and lock the current chooser.");

                var deck = StackRoot("_myDeckTile");
                var phase = Field.Root.transform.Find("Turn/Dial") as RectTransform;
                foreach (var target in new[] { deck, phase })
                {
                    blocked = Raycast(target, out _);
                    Require(blocked != null && blocked.transform.IsChildOf(modal),
                        $"Field view returned an unexpected hit at {target.name}: {blocked?.name ?? "none"}.");
                }

                foreach (var (stack, codes) in new[]
                {
                    ("_myGyTile", mine.grave.AsEnumerable().Reverse().Select(card => card.code)),
                    ("_myExtraTile", mine.extra.Select(card => card.code)),
                    ("_myBanTile", mine.banished.AsEnumerable().Reverse().Select(card => card.code)),
                    ("_oppExtraTile", theirs.extra.Select(card => card.IsFaceUp || card.isPublic ? card.code : 0u)),
                })
                {
                    ClickAt(StackRoot(stack));
                    VerifyOpened(prompt, codes);
                    Require(Modals.IsPromptOpen && !Modals.TryConfirm() && !Modals.TryCancelOrPass(),
                        "The prompt accepted input while the stack viewer covered it.");
                    Close();
                    Require(ShowingField() && ReferenceEquals(_duel.ActivePrompt, prompt),
                        "Closing the stack viewer failed to return to the same field view.");
                }

                ClickAt(fieldView);
                Require(panel.gameObject.activeInHierarchy && ReferenceEquals(_duel.ActivePrompt, prompt) &&
                    Private<int>(Modals, "_chainSelection") == chainIndex &&
                    Private<HashSet<int>>(Modals, "_selectedIndices").SetEquals(selected),
                    "Opening the field changed the current prompt, choices or selection.");
                ClickAt(fieldView);
                Require(ShowingField() && Modals.TryCancelOrPass() && panel.gameObject.activeInHierarchy &&
                    ReferenceEquals(_duel.ActivePrompt, prompt),
                    "Cancel from field view sent an effect response or failed to restore its chooser.");
                Modals.HideAll();
            }

            internal void VerifyFieldTarget(DuelPrompt prompt)
            {
                Modals.ShowPrompt(_duel, prompt);
                Field.Refresh();
                var modal = Modals.ModalRoot.transform;
                Require(Modals.IsPromptOpen && !modal.Find("FieldView").gameObject.activeSelf &&
                    !modal.Find("Win/Body/Cards") && !Modals.TryConfirm(),
                    "The field-only prompt opened a card list or accepted an empty selection.");

                Require(prompt.candidates.Count >= 2, "The field fixture needs at least two candidates.");
                var first = CardSlot(prompt.candidates[0]);
                var second = CardSlot(prompt.candidates[1]);
                var blocked = Raycast(StackRoot("_oppGyTile"), out _);
                Require(first.GetComponent<Outline>().effectColor.a > 0f &&
                    second.GetComponent<Outline>().effectColor.a > 0f &&
                    blocked != null && blocked.transform.IsChildOf(modal),
                    "The picker did not highlight targets or block unrelated board actions.");

                ClickAt(first, pointerId: 0);
                var selected = Private<HashSet<int>>(Modals, "_selectedIndices");
                Require(selected.SetEquals(new[] { 0 }), "Clicking a field card did not select it.");
                ClickAt(first);
                Require(selected.Count == 0 && !Modals.TryConfirm(), "Clicking the selected card did not clear it.");
                ClickAt(first);
                ClickAt(second);
                Require(selected.SetEquals(new[] { 1 }) && Modals.TryConfirm() && !Modals.IsPromptOpen,
                    "Changing the target or confirming the field selection failed.");
            }

            internal void VerifyAttackComparison(DuelPrompt prompt)
            {
                var available = prompt.candidates.Single();
                var zones = _duel.Players[available.controller].mzone;
                int extra = Array.FindIndex(zones, 0, 5, card => card == null);
                Require(extra >= 0, "The attack comparison fixture had no open slot for a hidden target.");
                const uint hiddenCode = 89631139;
                var concealed = new SelectableCardItem
                {
                    code = hiddenCode, controller = available.controller, location = YgoConstants.LOCATION_MZONE,
                    sequence = (uint)extra, position = YgoConstants.POS_FACEDOWN_DEFENSE,
                };
                zones[extra] = new LiveCardState
                {
                    code = concealed.code, controller = concealed.controller, location = concealed.location,
                    sequence = concealed.sequence, position = concealed.position, attack = 3000, defense = 2500,
                };
                prompt.candidates.Add(concealed);
                try
                {
                    Modals.ShowPrompt(_duel, prompt);
                    Field.Refresh();
                    var body = Modals.ModalRoot.transform.Find("Win/Body");
                    var targetName = body.Find("AttackTarget/Heading/Name").GetComponent<Text>();
                    var combat = body.Find("AttackTarget/Combat");
                    var knownSlot = CardSlot(available);
                    var hiddenSlot = CardSlot(concealed);
                    var source = _duel.GetAttackSelectionSource(prompt);
                    Require(source != null && body.Find("AttackSource/Heading/Name").GetComponent<Text>().text ==
                            $"공격 · {CardDatabase.GetName(source.code)}" && targetName.text == "공격 대상",
                        "The comparison showed the wrong attacker or an unfocused target.");

                    void ExpectTarget(bool faceUp)
                    {
                        string expected = faceUp ? $"대상 · {CardDatabase.GetName(available.code)}" : "대상 · 뒷면 카드";
                        Require(targetName.text == expected && combat.gameObject.activeSelf == faceUp,
                            "Changing targets showed stale or private attack comparison numbers.");
                        Require(Private<uint>(Field, "_inspectedCode") == (faceUp ? available.code : 0u),
                            "The card inspector did not follow the public or face-down attack target.");
                        if (!faceUp) return;
                        var defender = zones[available.sequence];
                        Require(combat.Find("Attack/Digits/Value").GetComponent<Text>().text == defender.attack.ToString() &&
                                combat.Find("Defense/Digits/Value").GetComponent<Text>().text == defender.defense.ToString(),
                            "The attack comparison lost the focused opponent's native values.");
                    }

                    knownSlot.GetComponent<CardDragTrigger>().OnPointerEnterAction.Invoke();
                    ExpectTarget(faceUp: true);
                    var hiddenTrigger = hiddenSlot.GetComponent<CardDragTrigger>();
                    hiddenTrigger.OnPointerEnterAction.Invoke();
                    ExpectTarget(faceUp: false);
                    hiddenTrigger.OnPointerExitAction.Invoke();
                    Require(targetName.text == "공격 대상" && !combat.gameObject.activeSelf, "Leaving the target kept its preview.");

                    TestDuelPadNavigation.Pad(Field, direction: Vector2Int.down);
                    var focus = Private<RectTransform>(Field, "_duelPadFocus");
                    Require(focus == knownSlot || focus == hiddenSlot, "The controller did not focus either legal target.");
                    bool known = focus == knownSlot;
                    ExpectTarget(known);
                    var next = known ? hiddenSlot : knownSlot;
                    TestDuelPadNavigation.Pad(Field, direction: next.position.x > focus.position.x ? Vector2Int.right : Vector2Int.left);
                    ExpectTarget(!known);
                }
                finally
                {
                    prompt.candidates.RemoveAt(prompt.candidates.Count - 1);
                    zones[extra] = null;
                    Modals.HideAll();
                }
                Debug.Log("[VERIFY DUEL ATTACK SELECTION] Mouse and controller focus compared the chosen attacker with live public targets, hid face-down values and left the native response pending.");
            }

            internal void ClickAttackTarget(DuelPrompt prompt, int pointer)
            {
                Modals.ShowPrompt(_duel, prompt);
                Field.Refresh();
                Require(Modals.IsFieldCardSelection && !Modals.ModalRoot.transform.Find("Win/Footer/Btn_선택 완료") &&
                    !Modals.TryConfirm(), "The attack target still showed a separate confirmation.");
                ClickAt(CardSlot(prompt.candidates[0]), pointer);
                Require(!Modals.IsPromptOpen, "Selecting the target left its prompt open.");
            }

            private RectTransform CardSlot(SelectableCardItem card)
            {
                var slot = typeof(DuelFieldUI).GetMethod("GetSlot", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(Field, new object[] { card.controller, card.location, card.sequence });
                return (RectTransform)slot.GetType().GetField("root").GetValue(slot);
            }

            internal void ActivateHandCardOverBoard(SelectableCardItem card, bool touch)
            {
                Field.Refresh();
                var hand = Field.Root.transform.Find("PlayerHand");
                var source = hand.GetComponentsInChildren<CardDragTrigger>()
                    .Single(trigger => trigger.IndexGetter?.Invoke() == (int)card.sequence);
                var pointer = new PointerEventData(EventSystem.current)
                {
                    pointerId = touch ? 0 : -1,
                    button = PointerEventData.InputButton.Left,
                };
                var bar = (RectTransform)Field.Root.transform.Find("CardActions");
                ExecuteEvents.Execute(source.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                if (touch)
                {
                    Require(!bar.gameObject.activeSelf, "Touch opened card actions before the tap was released.");
                    ExecuteEvents.Execute(source.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                    ExecuteEvents.Execute(source.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
                    typeof(DuelFieldUI).GetField("_pointerActionLeftAt", PrivateInstance).SetValue(Field, float.NegativeInfinity);
                    typeof(DuelFieldUI).GetMethod("RefreshCardActions", PrivateInstance).Invoke(Field, null);
                    Require(bar.gameObject.activeInHierarchy, "The hand actions disappeared after lifting the finger.");
                    typeof(DuelFieldUI).GetMethod("DismissTouchCardActionsAt", PrivateInstance).Invoke(Field,
                        new object[] { new Vector2(-1000f, -1000f) });
                    Require(!bar.gameObject.activeSelf, "Tapping elsewhere did not dismiss the hand actions.");
                    ExecuteEvents.Execute(source.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                    var prompt = _duel.ActivePrompt;
                    Click(bar.Find("Details/Circle/Button")?.GetComponent<Button>());
                    VerifyCardDetails(card.code, prompt);
                    ExecuteEvents.Execute(source.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                }
                else ExecuteEvents.Execute(source.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                VerifyInspectorArtwork(card.code, _duel.ActivePrompt, pointer.pointerId);
                var hoverArea = (RectTransform)bar.Find("HoverArea");
                var activate = (RectTransform)bar.Find("ACTION/Circle/Button");
                Require(bar.gameObject.activeInHierarchy && activate != null, "The hovered hand card had no activation icon.");
                var behind = LotdUiKit.FillRect("CardBehindActions", bar.parent);
                behind.SetSiblingIndex(bar.GetSiblingIndex());
                LotdUiKit.AddImage(behind.gameObject, LotdUiKit.WhiteSprite, Color.white);
                try
                {
                    Canvas.ForceUpdateCanvases();
                    float inGap = hoverArea.offsetMin.y * .5f;
                    Require(inGap < -1f, "The activation icon is too close to the hand card for this fixture.");
                    Vector2 gapPoint = RectTransformUtility.WorldToScreenPoint(_camera, bar.TransformPoint(new Vector2(0f, inGap)));
                    float buttonGap = bar.rect.xMin + ((RectTransform)bar.GetChild(1)).rect.width + 2.5f;
                    Vector2 betweenActions = RectTransformUtility.WorldToScreenPoint(_camera,
                        bar.TransformPoint(new Vector2(buttonGap, 44f)));
                    Require(Raycast(gapPoint, out _) == hoverArea.gameObject &&
                        Raycast(betweenActions, out _) == hoverArea.gameObject,
                        "A card behind the action toolbar intercepted the pointer.");
                    ClickAt(activate);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(behind.gameObject);
                }
            }


            internal void VerifyNoExtraSummon()
            {
                Field.Refresh();
                Require(!Private<Button>(Field, "_extraSummonButton").gameObject.activeInHierarchy,
                    "The Extra Deck summon button was visible without a legal summon.");
            }

            internal void VerifyGraveEffectButton(bool visible)
            {
                Field.Refresh();
                Require(Private<Button>(Field, "_graveEffectButton").gameObject.activeInHierarchy == visible,
                    "The grave effect shortcut did not match the current activation availability.");
                Require(StackRoot("_myGyTile").Find("PlayableGlow").gameObject.activeInHierarchy == visible,
                    "The grave effect glow did not match the current activation availability.");
            }

            internal void VerifyGraveEffectChoices(DuelPrompt prompt, uint code)
            {
                VerifyGraveEffectButton(visible: true);
                var button = Private<Button>(Field, "_graveEffectButton");
                var grave = _duel.Players[_duel.HumanPlayerIndex].grave;
                var pile = StackRoot("_myGyTile");
                var badge = (RectTransform)button.transform;
                Require(badge.IsChildOf(pile) && (badge.parent as RectTransform).anchorMin.y > .5f,
                    "The activation indicator was not placed at the top of the player's graveyard.");
                var pointer = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(pile.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                Require(!Private<RectTransform>(Field, "_cardActionsBar").gameObject.activeSelf,
                    "Hovering over the graveyard duplicated the activation indicator.");
                ClickAt(pile, pointerId: 0);
                VerifyOpened(prompt, grave.AsEnumerable().Reverse().Select(card => card.code));
                Close();

                int index = prompt.activatable.FindIndex(item => item.code == code && item.location == YgoConstants.LOCATION_GRAVE);
                Require(index >= 0, "The fixture was missing its legal grave effect.");
                var effect = prompt.activatable[index];
                prompt.activatable.RemoveAt(index);
                try
                {
                    Click(button);
                    Require(!Modals.IsPromptOpen, "A stale grave effect shortcut opened an empty chooser.");
                    VerifyGraveEffectButton(visible: false);
                }
                finally
                {
                    prompt.activatable.Insert(index, effect);
                }
                VerifyGraveEffectButton(visible: true);

                ClickAt((RectTransform)button.transform, pointerId: 0);
                var picker = VerifyActivationPicker(prompt, "묘지 효과 발동", code);
                Require(picker.Find("Details/Description").GetComponentsInChildren<Text>()
                    .Any(label => label.text.StartsWith("패 1장을 묘지로 보내고") && label.text.Contains("특수 소환한다")),
                    "The graveyard picker did not describe the available revival effect.");
                Click(picker.Find("Footer/Pass/B").GetComponent<Button>());
                Require(!Modals.IsPromptOpen && ReferenceEquals(_duel.ActivePrompt, prompt),
                    "Closing the grave activation picker changed the native prompt.");
                VerifyGraveEffectButton(visible: true);

                SetPrivate(Field, "_usingDuelPad", true);
                SetPrivate(Field, "_duelPadFocus", pile);
                Field.HandleDuelPad(new DeckPadFrame(Vector2Int.up, DeckPadButtons.None));
                Require(Private<RectTransform>(Field, "_duelPadFocus") == badge,
                    "The controller could not reach the activation indicator from the graveyard.");
                Field.HandleDuelPad(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Submit));
                picker = VerifyActivationPicker(prompt, "묘지 효과 발동", code);
                var action = picker.Find("Footer/Activate/B").GetComponent<Button>();
                Require(action.GetComponentInChildren<Text>().text == "효과 발동", "The grave effect was not actionable in the chooser.");
                Click(action);
                Require(!Modals.IsPromptOpen && !ReferenceEquals(prompt, _duel.ActivePrompt),
                    "Activating a grave effect did not close the picker and continue the duel.");
            }

            private Transform VerifyActivationPicker(DuelPrompt prompt, string title, params uint[] expected)
            {
                var window = Modals.ModalRoot.transform.Find("ChainWin");
                Require(Modals.IsPromptOpen && !Modals.IsZoneViewerOpen && window.gameObject.activeInHierarchy &&
                    ReferenceEquals(_duel.ActivePrompt, prompt) &&
                    window.Find("Hdr/Title/Text").GetComponent<Text>().text == title &&
                    window.Find("Cards").GetComponentsInChildren<CompositeCardView>().Select(card => card.CurrentCode)
                        .SequenceEqual(expected),
                    "The activation picker did not show the legal cards in the horizontal strip.");
                Require(window.Find("Details/Name/Text").GetComponent<Text>().text == CardDatabase.GetName(expected[0]) &&
                    window.Find("Details/Description").GetComponentsInChildren<Text>().Any(label => label.text.Length > 0),
                    "The picker did not identify the card and its effect before confirmation.");
                Opens++;
                return window;
            }

            internal void VerifyExtraChainSource(DuelPrompt prompt, uint expected)
            {
                Modals.ShowPrompt(_duel, prompt);
                Field.Refresh();
                var button = Private<Button>(Field, "_extraEffectButton");
                var pile = StackRoot("_myExtraTile");
                Require(button.gameObject.activeInHierarchy && button.transform.IsChildOf(pile) &&
                    !Private<Button>(Field, "_extraSummonButton").gameObject.activeInHierarchy &&
                    !Private<Button>(Field, "_graveEffectButton").gameObject.activeInHierarchy,
                    "The chain did not identify exactly the Extra Deck as an activation source.");
                var view = (Button)Modals.FieldViewControl;
                Click(view);
                Require(Modals.IsShowingField && button.gameObject.activeInHierarchy,
                    "The Extra Deck activation was unavailable during field inspection.");
                ClickAt((RectTransform)button.transform, pointerId: 0);
                Require(!Modals.IsShowingField && ReferenceEquals(_duel.ActivePrompt, prompt),
                    "Tapping the Extra Deck should focus the pending choice before activation.");
                var picker = Modals.ModalRoot.transform.Find("ChainWin");
                Require(picker.Find("Details/Source/Text").GetComponent<Text>().text == "내 엑스트라 덱" &&
                    picker.Find("Details/Name/Text").GetComponent<Text>().text == CardDatabase.GetName(expected) &&
                    picker.Find("Details/Description").GetComponentsInChildren<Text>()
                        .Any(label => label.text == "이 카드를 자신의 펜듈럼 존에 놓는다"),
                    "The Extra Deck shortcut lost the card's effect or original zone.");
                Click(view);
                SetPrivate(Field, "_usingDuelPad", true);
                SetPrivate(Field, "_duelPadFocus", pile);
                Field.HandleDuelPad(new DeckPadFrame(Vector2Int.up, DeckPadButtons.None));
                Require(Private<RectTransform>(Field, "_duelPadFocus") == button.transform,
                    "A controller could not reach the Extra Deck effect while browsing the field.");
                Field.HandleDuelPad(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Submit));
                Require(!Modals.IsShowingField && ReferenceEquals(_duel.ActivePrompt, prompt),
                    "The controller did not return to the pending Extra Deck choice.");
                Click(picker.Find("Footer/Activate/B").GetComponent<Button>());
                Opens++;
            }

            internal void VerifyExtraSummonChoices(DuelPrompt prompt, IReadOnlyList<LiveCardState> all)
            {
                bool CanSummon(LiveCardState card) => prompt.spSummonable.Any(item => item.controller == card.controller &&
                    item.location == YgoConstants.LOCATION_EXTRA && item.sequence == card.sequence);
                var legal = all.Where(CanSummon).Select(card => card.code).ToArray();
                Require(legal.Contains(Chixiao) && legal.Length < all.Count, "The fixture needs both eligible and blocked monsters.");

                var blocked = all.First(card => !CanSummon(card));
                var effect = new SelectableCardItem { code = blocked.code, controller = blocked.controller,
                    location = blocked.location, sequence = blocked.sequence };
                prompt.activatable.Add(effect);
                try
                {
                    Field.Refresh();
                    var summonButton = Private<Button>(Field, "_extraSummonButton");
                    Require(summonButton.gameObject.activeInHierarchy, "The Extra Deck summon button was missing.");
                    var pile = StackRoot("_myExtraTile");
                    var indicator = (RectTransform)summonButton.transform;
                    var effectButton = Private<Button>(Field, "_extraEffectButton");
                    var effectPlate = (RectTransform)effectButton.transform.parent;
                    Require(indicator.IsChildOf(pile) && ((RectTransform)indicator.parent).anchorMin.y > .5f,
                        "Summon availability must be shown at the top of the player's Extra Deck.");
                    Require(effectButton.gameObject.activeInHierarchy && effectPlate.IsChildOf(pile) &&
                        effectPlate.anchorMin.y > .5f && ((RectTransform)indicator.parent).anchorMax.x < effectPlate.anchorMin.x,
                        "The Extra Deck should show separate non-overlapping summon and effect shortcuts.");
                    ClickAt((RectTransform)effectButton.transform, pointerId: 0);
                    var effectPicker = VerifyActivationPicker(prompt, "엑스트라 덱 효과 발동", blocked.code);
                    Click(effectPicker.Find("Footer/Pass/B").GetComponent<Button>());
                    Field.Refresh();
                    var available = prompt.spSummonable.ToArray();
                    prompt.spSummonable.RemoveAll(item => item.location == YgoConstants.LOCATION_EXTRA);
                    try
                    {
                        Click(summonButton);
                        Require(!Modals.IsZoneViewerOpen, "The stale Extra Deck indicator opened an empty summon picker.");
                        VerifyNoExtraSummon();
                    }
                    finally
                    {
                        prompt.spSummonable.Clear();
                        prompt.spSummonable.AddRange(available);
                    }
                    Field.Refresh();
                    ClickAt(pile, pointerId: 0);
                    VerifyOpened(prompt, all.Select(card => card.code));
                    var preview = VisibleCards.Single(card => card.CurrentCode == Chixiao).transform.parent.parent;
                    Click(preview.GetComponent<Button>());
                    Require(Modals.IsZoneViewerOpen && InspectedCode == Chixiao && ReferenceEquals(_duel.ActivePrompt, prompt) &&
                        !preview.Find("Act/B") && VisibleCards.Single(card => card.CurrentCode == blocked.code)
                            .transform.parent.parent.Find("Act/B"),
                        "The full list exposed a summon or failed to retain a separate Extra Deck effect.");
                    Close();

                    ClickAt((RectTransform)summonButton.transform, pointerId: 0);
                    VerifyOpened(prompt, legal);
                    Require(VisibleCards.All(card => card.transform.parent.parent.Find("Act/B")
                            .GetComponentInChildren<Text>().text == "특수 소환"),
                        "The summon list did not show only Special Summon buttons.");
                    Close();
                    Field.Refresh();

                    SetPrivate(Field, "_usingDuelPad", true);
                    SetPrivate(Field, "_duelPadFocus", pile);
                    Field.HandleDuelPad(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Submit));
                    VerifyOpened(prompt, all.Select(card => card.code));
                    Close();
                    Field.Refresh();

                    SetPrivate(Field, "_duelPadFocus", pile);
                    Field.HandleDuelPad(new DeckPadFrame(Vector2Int.up, DeckPadButtons.None));
                    Require(Private<RectTransform>(Field, "_duelPadFocus") == indicator,
                        "Controller navigation could not reach the Extra Deck summon button from the pile.");
                    Field.HandleDuelPad(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Submit));
                    VerifyOpened(prompt, legal);
                    var layout = _grid.GetComponent<GridLayoutGroup>();
                    Require(layout.constraint == GridLayoutGroup.Constraint.FixedColumnCount && layout.constraintCount == 1,
                        "The Extra Deck summon list was not shown as a vertical list.");
                }
                finally
                {
                    prompt.activatable.Remove(effect);
                    Field.Refresh();
                    Require(!Private<Button>(Field, "_extraEffectButton").gameObject.activeInHierarchy,
                        "The Extra Deck effect indicator survived an expired option.");
                }

                var tile = VisibleCards.Single(card => card.CurrentCode == Chixiao).transform.parent.parent;
                Click(tile.Find("Act/B").GetComponent<Button>());
                Require(!Modals.ZoneViewRoot.activeSelf && !ReferenceEquals(_duel.ActivePrompt, prompt),
                    "Selecting a legal summon in the filtered list did not advance the duel.");
            }

            internal void SelectSynchroMaterials(DuelPrompt prompt)
            {
                Modals.ShowPrompt(_duel, prompt);
                Field.Refresh();
                var modal = Modals.ModalRoot.transform;
                Require(Modals.IsPromptOpen && !modal.Find("FieldView").gameObject.activeSelf && !modal.Find("Win/Body/Cards"),
                    "The Synchro materials were not selectable on the field.");

                byte[] response = Ai.ComputeResponse(_duel, prompt);
                if (prompt.messageType == YgoConstants.MSG_SELECT_SUM)
                {
                    for (int i = 0; i < BitConverter.ToInt32(response, 4); i++)
                        ClickAt(CardSlot(prompt.candidates[BitConverter.ToInt32(response, 8 + i * 4)]));
                }
                else if (response.Length == 8)
                {
                    ClickAt(CardSlot(prompt.candidates[BitConverter.ToInt32(response, 4)]));
                    Require(!ReferenceEquals(_duel.ActivePrompt, prompt), "Clicking a material did not update the native selection.");
                    return;
                }

                ClickMaterialConfirm();
                Require(!ReferenceEquals(_duel.ActivePrompt, prompt), "The field confirmation did not complete the selection.");
            }

            internal void ConfirmFieldMaterials(LiveCardState material)
            {
                var prompt = new DuelPrompt
                {
                    messageType = YgoConstants.MSG_SELECT_UNSELECT_CARD, playerId = material.controller,
                    finishable = true, cancelable = true, min = 1, max = 2,
                };
                prompt.unselectCandidates.Add(new SelectableCardItem
                {
                    code = material.code, controller = material.controller,
                    location = material.location, sequence = material.sequence,
                });
                var activePrompt = typeof(DuelSession).GetProperty(nameof(DuelSession.ActivePrompt));
                var previous = _duel.ActivePrompt;
                activePrompt.SetValue(_duel, prompt);
                try
                {
                    Modals.ShowPrompt(_duel, prompt);
                    Field.Refresh();
                    ClickMaterialConfirm();
                }
                finally
                {
                    activePrompt.SetValue(_duel, previous);
                }
            }

            private void ClickMaterialConfirm()
            {
                var confirm = Modals.ModalRoot.transform.Find("MaterialConfirm/Button") as RectTransform;
                Require(confirm != null && confirm.gameObject.activeInHierarchy, "The material confirmation was missing from the field.");
                ClickAt(confirm);
            }

            internal void VerifyAttackResponse(LiveCardState defender)
            {
                byte opponent = (byte)_duel.AiPlayerIndex;
                _duel.Players[opponent].mzone[0] = new LiveCardState
                {
                    controller = opponent, location = YgoConstants.LOCATION_MZONE, code = 20721928,
                    position = YgoConstants.POS_FACEUP_ATTACK,
                };
                _duel.AttackerCon = opponent;
                _duel.AttackerLoc = YgoConstants.LOCATION_MZONE;
                _duel.AttackerSeq = 0;
                _duel.TargetCon = defender.controller;
                _duel.TargetLoc = defender.location;
                _duel.TargetSeq = defender.sequence;
                typeof(DuelSession).GetProperty(nameof(DuelSession.HasActiveAttack)).SetValue(_duel, true);
                var response = new DuelPrompt
                {
                    messageType = YgoConstants.MSG_SELECT_EFFECTYN, playerId = defender.controller,
                    promptCardCode = defender.code, promptCon = defender.controller,
                    promptLoc = defender.location, promptSeq = defender.sequence,
                };
                var title = Private<Text>(Modals, "_chainTitle");
                var arrow = Field.Root.transform.Find("ResponseAttackArrow");

                Modals.ShowPrompt(_duel, response);
                Field.Refresh();
                Require(arrow.gameObject.activeInHierarchy && title.text.Contains("공격 선언"),
                    "An attack response did not show the declaration and arrow.");

                _duel.TargetLoc = 0;
                Modals.ShowPrompt(_duel, response);
                Field.Refresh();
                Require(arrow.gameObject.activeInHierarchy && title.text.Contains("직접 공격"),
                    "A direct attack response did not show its own declaration and arrow.");
                Modals.HideAll();
                Field.Refresh();
                Require(!arrow.gameObject.activeSelf, "The attack response arrow remained after closing the prompt.");
            }


            private GameObject Raycast(RectTransform target, out PointerEventData pointer)
            {
                Canvas.ForceUpdateCanvases();
                Vector2 point = RectTransformUtility.WorldToScreenPoint(_camera, target.TransformPoint(target.rect.center));
                return Raycast(point, out pointer);
            }

            private GameObject Raycast(Vector2 point, out PointerEventData pointer)
            {
                pointer = new PointerEventData(EventSystem.current)
                {
                    position = point,
                    button = PointerEventData.InputButton.Left,
                };
                if (_camera != null)
                {
                    _camera.Render();
                    var hits = new List<RaycastResult>();
                    _canvas.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
                    return hits.Count > 0 ? hits[0].gameObject : null;
                }

                // The null graphics device assigns no CanvasRenderer depth.
                return _canvas.GetComponentsInChildren<Graphic>().Reverse().FirstOrDefault(graphic =>
                    graphic.raycastTarget && RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform, point) &&
                    graphic.Raycast(point, null))?.gameObject;
            }

            private void ClickAt(RectTransform target, int pointerId = -1, GameObject control = null)
            {
                var hit = Raycast(target, out var pointer);
                pointer.pointerId = pointerId;
                var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
                Require(handler == (control ?? target.gameObject),
                    $"A pointer click at {target.name} reached {handler?.name ?? hit?.name ?? "nothing"}.");
                ExecuteEvents.Execute(handler, pointer, ExecuteEvents.pointerClickHandler);
            }

            internal void VerifyPhasePicker(DuelPrompt prompt)
            {
                Field.Refresh();
                var dial = Field.Root.transform.Find("Turn/Dial") as RectTransform;
                var steps = dial.Find("PhaseSteps");
                Require(dial.Find("PhaseName/Value").GetComponent<Text>().text == "메인 1" &&
                    steps.Find("Main1").GetComponent<Image>().color.g > steps.Find("Battle").GetComponent<Image>().color.g,
                    "The turn HUD did not identify the current phase.");
                ClickAt(dial, _duel.HumanPlayerIndex == 0 ? -1 : 0);
                var picker = Field.Root.transform.Find("PhasePicker");
                Require(picker != null && picker.gameObject.activeInHierarchy, "The phase dial did not open its picker.");
                float previousX = float.NegativeInfinity;
                float? rowY = null;
                foreach (var (name, enabled) in new[]
                {
                    ("Draw", false), ("Standby", false), ("Main1", false),
                    ("Battle", prompt.canBp), ("Main2", false), ("End", prompt.canEp),
                })
                {
                    var choice = picker.Find($"PhaseControls/{name}/Button")?.GetComponent<Button>();
                    Require(choice != null && choice.IsInteractable() == enabled, $"The phase picker mishandled the {name} command.");
                    var position = ((RectTransform)choice.transform.parent).anchorMin;
                    Require(position.x > previousX && (rowY == null || Mathf.Approximately(position.y, rowY.Value)),
                        "Phase buttons were not shown in order on the same row.");
                    previousX = position.x;
                    rowY = position.y;
                }
                var current = picker.Find("PhaseControls/Main1/Button").GetComponent<Image>();
                Require(current.color.r > .9f && current.color.b < .5f,
                    "The current Main Phase was not visually distinct from available phases.");
                Click(picker.GetComponent<Button>());
                Require(!picker.gameObject.activeSelf && ReferenceEquals(_duel.ActivePrompt, prompt),
                    "Dismissing the phase picker submitted an action.");
            }

            internal void VerifyAiWaitsWhileBrowsing(PlayerFieldSnapshot mine)
            {
                OpenStack("_myExtraTile", mine.extra.Select(card => card.code));
                var prompt = _duel.ActivePrompt;
                SetPrivate(_controller, "_aiTimer", _controller.AiStepDelay + 1f);
                typeof(GameAppController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(_controller, null);
                Require(ReferenceEquals(_duel.ActivePrompt, prompt) && Modals.ZoneViewRoot.activeSelf,
                    "The AI advanced its action while the zone browser was open.");
                Close();
            }

            internal void VerifyAiWaitsWhileReadingCard(LiveCardState card)
            {
                var prompt = _duel.ActivePrompt;
                var art = Field.Root.transform.Find("PlayerHand").GetComponentsInChildren<CompositeCardView>()
                    .First(view => view.CurrentCode == card.code);
                var source = art.transform.parent.GetComponent<CardDragTrigger>();
                var pointer = new PointerEventData(EventSystem.current) { pointerId = 0 };
                ExecuteEvents.Execute(source.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                SetPrivate(_controller, "_aiTimer", _controller.AiStepDelay + 1f);
                typeof(GameAppController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(_controller, null);
                VerifyCardDetails(card.code, prompt);
                SetPrivate(_controller, "_aiTimer", 0f);
            }

            private void VerifyCardDetails(uint code, DuelPrompt prompt)
            {
                var zoom = Field.Root.transform.Find("Overlay_CardZoom");
                var desc = Private<Text>(Field, "_zoomDesc");
                Require(zoom.gameObject.activeInHierarchy && desc.text == CardDatabase.Get(code).desc &&
                    ReferenceEquals(_duel.ActivePrompt, prompt), "Touch did not open the correct card details without advancing the duel.");
                Click(zoom.GetComponent<Button>());
                Require(!zoom.gameObject.activeSelf && ReferenceEquals(_duel.ActivePrompt, prompt),
                    "Closing card details changed the active prompt.");
            }

            private void VerifyInspectorArtwork(uint code, DuelPrompt prompt, int pointerId)
            {
                var preview = Private<CompositeCardView>(Field, "_inspCard");
                var artwork = (RectTransform)preview.transform.Find("AspectBox/Artwork");
                var artworkHit = Private<Button>(Field, "_inspectorArtworkButton");
                var details = Private<Button>(Field, "_duelPadInspectButton");
                Require(preview.CurrentCode == code && artworkHit.IsInteractable() && details.IsInteractable(),
                    "The selected card did not populate both inspection controls.");
                Canvas.ForceUpdateCanvases();
                var card = (RectTransform)preview.transform.Find("AspectBox");
                var detailRect = (RectTransform)details.transform;
                Require(detailRect.TransformPoint(new Vector3(0f, detailRect.rect.yMax)).y <
                        card.TransformPoint(new Vector3(0f, card.rect.yMin)).y,
                    "The details button obstructed the card.");
                var zoom = Private<GameObject>(Field, "_cardZoomModal");
                foreach (var (target, button) in new[] { (artwork, artworkHit), (detailRect, details) })
                {
                    ClickAt(target, pointerId, button.gameObject);
                    Require(zoom.activeInHierarchy && Private<Text>(Field, "_zoomDesc").text == CardDatabase.Get(code).desc &&
                        ReferenceEquals(_duel.ActivePrompt, prompt), "An inspection control did not open the selected card's details.");
                    if (Modals.IsPromptOpen)
                        Require(!Modals.TryConfirm() && !Modals.TryCancelOrPass(), "The chooser handled a response while card details were open.");
                    ClickAt((RectTransform)Private<Button>(Field, "_zoomCloseButton").transform, pointerId);
                    Require(!zoom.activeSelf && ReferenceEquals(_duel.ActivePrompt, prompt), "Closing the details advanced the duel.");
                }
            }

            internal void VerifyOwnZones(PlayerFieldSnapshot player)
            {
                Field.Refresh();
                VerifyRepeated("_myGyTile", player.grave.AsEnumerable().Reverse());
                VerifyRepeated("_myExtraTile", player.extra);
                VerifyRepeated("_myBanTile", player.banished.AsEnumerable().Reverse());
            }

            internal void VerifyPileReadout(string pile, int count, uint visibleCode) =>
                TestDuelZoneBrowser.VerifyPileReadout(StackRoot(pile), count, visibleCode);

            internal void VerifyPileTabsAndDetails(PlayerFieldSnapshot mine, PlayerFieldSnapshot opponent)
            {
                var prompt = _duel.ActivePrompt;
                OpenStack("_myGyTile", mine.grave.AsEnumerable().Reverse().Select(card => card.code));
                var tabs = Private<RectTransform>(Modals, "_zoneTabs");
                var removed = (RectTransform)tabs.Find("Pile_1/Button");
                Require(removed.GetComponentInChildren<Text>().text == $"제외 {mine.banished.Count}",
                    "The pile tab did not show the selected owner's count.");
                ClickAt(removed);
                var expected = mine.banished.AsEnumerable().Reverse().Select(card => card.code).ToArray();
                VerifyOpened(prompt, expected);

                var first = _grid.GetChild(0).GetComponent<Button>();
                Click(first);
                var scroll = Private<ScrollRect>(Modals, "_zoneScroll");
                scroll.verticalNormalizedPosition = .43f;
                Canvas.ForceUpdateCanvases();
                var scrollPosition = scroll.content.anchoredPosition;
                var details = Private<Button>(Modals, "_zoneDetailsButton");
                ClickAt((RectTransform)details.transform, pointerId: 0);
                var zoom = Private<GameObject>(Field, "_cardZoomModal");
                Require(zoom.activeSelf && Modals.IsZoneViewerOpen &&
                    Private<CompositeCardView>(Field, "_zoomCardView").CurrentCode == expected[0] &&
                    ReferenceEquals(_duel.ActivePrompt, prompt), "Opening a pile card's details replaced the browser or the prompt.");

                var description = (RectTransform)zoom.transform.Find("ZoomWin/Info/DescBox");
                var hit = Raycast(description, out var pointer);
                var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
                Require(handler != null && handler != zoom && handler.transform.IsChildOf(zoom.transform),
                    "Tapping the card description could dismiss the panel or reach the pile below it.");
                ExecuteEvents.Execute(handler, pointer, ExecuteEvents.pointerClickHandler);
                Require(zoom.activeSelf, "A tap inside the description closed the card details.");
                ClickAt((RectTransform)Private<Button>(Field, "_zoomCloseButton").transform, pointerId: 0);
                Require(!zoom.activeSelf && Modals.IsZoneViewerOpen && _grid.GetChild(0).gameObject == first.gameObject &&
                    Vector2.Distance(scroll.content.anchoredPosition, scrollPosition) < .5f &&
                    Private<uint>(Modals, "_zoneSelectedCode") == expected[0],
                    "Closing details did not preserve the selected card and list position.");
                VerifyInspectorArtwork(expected[0], prompt, pointerId: 0);
                Require(Modals.IsZoneViewerOpen && Vector2.Distance(scroll.content.anchoredPosition, scrollPosition) < .5f,
                    "Opening details from the left artwork interrupted the pile browser.");

                ClickAt((RectTransform)tabs.Find("Opponent/Button"));
                VerifyOpened(prompt, opponent.banished.Select(_ => 0u));
                Require(!details.gameObject.activeInHierarchy && Private<uint>(Modals, "_zoneSelectedCode") == 0,
                    "Switching to the opponent's face-down banished cards retained a visible card's detail action.");
                ClickAt((RectTransform)tabs.Find("Pile_0/Button"));
                VerifyOpened(prompt, opponent.grave.AsEnumerable().Reverse().Select(card => card.code));
                Close();
            }

            private void VerifyRepeated(string stack, IEnumerable<LiveCardState> cards)
            {
                var expected = cards.Select(card => card.code).ToArray();
                for (int i = 0; i < 2; i++)
                {
                    OpenStack(stack, expected);
                    Close();
                }
            }

            internal void OpenStack(string stackField, IEnumerable<uint> expected)
                => Open(StackRoot(stackField).GetComponent<Button>(), expected);

            private RectTransform StackRoot(string stackField)
            {
                var stack = Private<object>(Field, stackField);
                return (RectTransform)stack.GetType().GetField("root")?.GetValue(stack);
            }

            internal void Open(Button button, IEnumerable<uint> expected)
            {
                var prompt = _duel.ActivePrompt;
                Click(button);
                VerifyOpened(prompt, expected);
            }

            private void VerifyOpened(DuelPrompt prompt, IEnumerable<uint> expected)
            {
                Require(Modals.ZoneViewRoot.activeSelf && Field.IsBrowsingZone, "The stack button did not open its zone browser.");
                uint[] wanted = expected.ToArray();
                Require(VisibleCards.Select(card => card.CurrentCode).SequenceEqual(wanted),
                    "The zone browser showed stale, incorrect or hidden card codes.");
                Require(ReferenceEquals(_duel.ActivePrompt, prompt), "Opening a browser consumed the prompt.");
                Opens++;
            }

            internal void Close()
            {
                var prompt = _duel.ActivePrompt;
                Modals.CloseZoneViewer();
                Require(!Modals.ZoneViewRoot.activeSelf && !Field.IsBrowsingZone && _grid.childCount == 0 &&
                        ReferenceEquals(_duel.ActivePrompt, prompt),
                    "Closing a zone browser left stale cards or changed the active prompt.");
            }

            public void Dispose()
            {
                Field.UnbindSession();
                SetPrivate(_controller, "_session", null);
                UnityEngine.Object.DestroyImmediate(_canvas);
                if (_camera != null) UnityEngine.Object.DestroyImmediate(_camera.gameObject);
                if (_frame != null) UnityEngine.Object.DestroyImmediate(_frame);
            }
        }

        private static T Private<T>(object owner, string field) =>
            (T)owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner);

        private static void SetPrivate(object owner, string field, object value) =>
            owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);

        private static void Click(Button button)
        {
            Require(button != null && button.gameObject.activeInHierarchy && button.IsInteractable(), "Expected an active zone UI button.");
            button.onClick.Invoke();
        }

        private static void Require(bool condition, string error)
        {
            if (!condition) throw new InvalidOperationException($"[ZONE BROWSER] {error}");
        }
    }
}
