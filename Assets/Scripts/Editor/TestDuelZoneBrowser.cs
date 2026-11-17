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
            int opens = 0;
            foreach (bool humanFirst in new[] { true, false })
                opens += VerifyBanishAndGrave(humanFirst);
            opens += VerifyPromptAndExtraSummon();
            string hitTest = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ? "raycast filters" : "GraphicRaycaster";
            Debug.Log($"[VERIFY ZONE BROWSER] {opens} successful opens. Graveyard, Extra Deck, and banished cards verified for both seating orders, including effect prompt field view ({hitTest}), a Synchro summon, and an attack response.");
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

        private static int VerifyBanishAndGrave(bool humanFirst)
        {
            var main = Enumerable.Repeat(PotOfDesires, 20).Concat(Enumerable.Repeat(89631139u, 20)).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Zone browser banish", main, ExtraDeck, main, ExtraDeck, humanFirst,
                session => session.Players.All(player => player.hand.Any(card => card.code == PotOfDesires)));
            using var ui = new Browser(duel);
            var mine = duel.Players[duel.HumanPlayerIndex];
            var opponent = duel.Players[duel.AiPlayerIndex];
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
                    ui.VerifyAiWaitsWhileBrowsing(mine);

                int activate = prompt.activatable.FindIndex(card => card.code == PotOfDesires);
                Require(activate >= 0, "The deterministic Pot of Desires fixture is missing its activation.");
                duel.SubmitResponseInt((activate << 16) | 5);
                prompt = ReachMainPhase(duel, player);
                var resolved = duel.Players[player];
                Require(resolved.grave.Any(card => card.code == PotOfDesires) &&
                        resolved.banished.Count == 10 && resolved.banished.All(card => !card.IsFaceUp && card.code != 0),
                    "Pot of Desires did not reach the expected face-down banish state.");
                ui.Field.Refresh();
                ui.VerifyOwnZones(mine);
                ui.OpenStack("_oppBanTile", opponent.banished.Select(_ => 0u));
                ui.Close();
                Require(ReferenceEquals(duel.ActivePrompt, prompt), "Checking the stacks after an effect advanced the engine.");
                if (player == 0) duel.SubmitResponseInt(7);
            }
            Require(duel.RetryCount == 0 && !duel.IsFinished, "Zone inspection left a retry or ended the duel.");
            return ui.Opens;
        }

        private static int VerifyPromptAndExtraSummon()
        {
            var main = Enumerable.Repeat(MoYe, 20).Concat(Enumerable.Repeat(Longyuan, 20)).ToArray();
            var otherMain = Enumerable.Repeat(20721928u, 40).ToArray();
            using var duel = YgoProjectBuilder.StartSeedMatching("Zone browser Synchro", main, ExtraDeck, otherMain, ExtraDeck, true,
                session => session.Players[0].hand.Any(card => card.code == MoYe) &&
                           session.Players[0].hand.Any(card => card.code == Longyuan));
            Action<byte[]> submit = duel.SubmitResponse;
            using var ui = new Browser(duel, bytes => submit(bytes));
            var mine = duel.Players[0];
            var mainPrompt = ReachMainPhase(duel, 0);
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
                    ui.Field.Refresh();
                    ui.OpenStack("_myExtraTile", mine.extra.Select(card => card.code));
                    var tile = ui.VisibleCards.Single(card => card.CurrentCode == Chixiao).transform.parent.parent;
                    Click(tile.GetComponent<Button>());
                    Require(ui.Modals.ZoneViewRoot.activeSelf && ReferenceEquals(duel.ActivePrompt, prompt) &&
                            ui.InspectedCode == Chixiao, "Inspecting a summonable card acted on the duel or closed the browser.");
                    Click(tile.Find("Act/B")?.GetComponent<Button>());
                    Require(!ui.Modals.ZoneViewRoot.activeSelf && ReferenceEquals(duel.ActivePrompt, prompt),
                        "Opening the Extra Deck command already submitted a summon.");
                    var popup = Private<GameObject>(ui.Field, "_actionPopup");
                    Require(popup.activeSelf, "The explicit Extra Deck command did not open the action popup.");
                    Click(popup.GetComponent<Button>());
                    ui.SummonFromPileShortcut(prompt);
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

        private static DuelPrompt ReachMainPhase(DuelSession duel, byte player)
        {
            for (int step = 0; step < 30 && !duel.IsFinished; step++)
            {
                var prompt = duel.ActivePrompt;
                if (prompt == null)
                    duel.StepUntilPrompt();
                else if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && prompt.playerId == player)
                    return prompt;
                else
                    duel.SubmitResponse(Ai.ComputeResponse(duel, prompt));
            }
            throw new InvalidOperationException($"Could not reach main phase for player {player}.");
        }

        private sealed class Browser : IDisposable
        {
            private readonly GameObject _canvas;
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

            internal Browser(DuelSession duel, Action<byte[]> onSubmit = null)
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
                Modals = new DuelPromptModals(_canvas.transform, code => InspectedCode = code,
                    onSubmit ?? (_ => throw new InvalidOperationException("Browsing submitted a modal response.")));
                _controller = _canvas.AddComponent<GameAppController>();
                Field = new DuelFieldUI(_controller, _canvas.transform, Modals);
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

                ClickAt(first);
                var selected = Private<HashSet<int>>(Modals, "_selectedIndices");
                Require(selected.SetEquals(new[] { 0 }), "Clicking a field card did not select it.");
                ClickAt(first);
                Require(selected.Count == 0 && !Modals.TryConfirm(), "Clicking the selected card did not clear it.");
                ClickAt(first);
                ClickAt(second);
                Require(selected.SetEquals(new[] { 1 }) && Modals.TryConfirm() && !Modals.IsPromptOpen,
                    "Changing the target or confirming the field selection failed.");
            }

            private RectTransform CardSlot(SelectableCardItem card)
            {
                var slot = typeof(DuelFieldUI).GetMethod("GetSlot", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(Field, new object[] { card.controller, card.location, card.sequence });
                return (RectTransform)slot.GetType().GetField("root").GetValue(slot);
            }

            internal void SummonFromPileShortcut(DuelPrompt prompt)
            {
                var extra = StackRoot("_myExtraTile");
                extra.GetComponent<CardDragTrigger>().OnPointerEnterAction.Invoke();
                var icon = Field.Root.transform.Find("CardActions/SUMMONSP/Circle/Button") as RectTransform;
                Require(icon != null && icon.gameObject.activeInHierarchy, "The Extra Deck shortcut was missing.");
                ClickAt(icon);

                var layout = _grid.GetComponent<GridLayoutGroup>();
                var window = Private<RectTransform>(Modals, "_zoneWindow");
                Require(ReferenceEquals(_duel.ActivePrompt, prompt) && Modals.ZoneViewRoot.activeSelf &&
                    layout.constraint == GridLayoutGroup.Constraint.FixedColumnCount && layout.constraintCount == 1 &&
                    window.anchorMin.x > .8f, "The shortcut did not open a vertical action list on the right.");
                var cards = VisibleCards;
                Require(cards.Length > 0 && cards.All(card => card.transform.parent.parent.Find("Act/B")),
                    "The action list included a card without a legal command.");
                var tile = cards.Single(card => card.CurrentCode == Chixiao).transform.parent.parent;
                Click(tile.Find("Act/B").GetComponent<Button>());
                Require(!Modals.ZoneViewRoot.activeSelf && !ReferenceEquals(_duel.ActivePrompt, prompt),
                    "Selecting a legal summon in the shortcut did not advance the duel.");
                Opens++;
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

            private void ClickAt(RectTransform target)
            {
                var hit = Raycast(target, out var pointer);
                var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
                Require(handler == target.gameObject,
                    $"A pointer click at {target.name} reached {handler?.name ?? hit?.name ?? "nothing"}.");
                ExecuteEvents.Execute(handler, pointer, ExecuteEvents.pointerClickHandler);
            }

            internal void VerifyPhasePicker(DuelPrompt prompt)
            {
                Field.Refresh();
                Click(Field.Root.transform.Find("Turn/Dial")?.GetComponent<Button>());
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

            internal void VerifyOwnZones(PlayerFieldSnapshot player)
            {
                Field.Refresh();
                VerifyRepeated("_myGyTile", player.grave.AsEnumerable().Reverse());
                VerifyRepeated("_myExtraTile", player.extra);
                VerifyRepeated("_myBanTile", player.banished.AsEnumerable().Reverse());
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
