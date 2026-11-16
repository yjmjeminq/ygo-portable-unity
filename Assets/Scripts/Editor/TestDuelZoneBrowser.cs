using System;
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
            Debug.Log($"[VERIFY ZONE BROWSER] {opens} successful opens. Graveyard, Extra Deck, and banished cards verified for both seating orders, including effect prompt and Synchro summon.");
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
            using var ui = new Browser(duel);
            var mine = duel.Players[0];
            var mainPrompt = ReachMainPhase(duel, 0);
            int normalSummon = mainPrompt.summonable.FindIndex(card => card.code == MoYe);
            Require(normalSummon >= 0, "Mo Ye is not available in the fixture.");
            duel.SubmitResponseInt(normalSummon << 16);

            bool inspectedDuringPrompt = false;
            bool choseSummon = false;
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
                    var summonButton = popup.GetComponentsInChildren<Button>().Single(button =>
                        button.gameObject != popup &&
                        button.GetComponentsInChildren<Text>().Any(label => label.text == "특수 소환"));
                    Click(summonButton);
                    choseSummon = true;
                    continue;
                }

                bool cardPrompt = prompt.messageType == YgoConstants.MSG_SELECT_CHAIN ||
                                  prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN ||
                                  prompt.messageType == YgoConstants.MSG_SELECT_CARD;
                if (!inspectedDuringPrompt && prompt.playerId == 0 && cardPrompt)
                {
                    ui.Modals.ShowPrompt(duel, prompt);
                    var shortcuts = ui.Modals.ModalRoot.transform.Find("ZoneShortcuts");
                    Require(shortcuts != null, "There are no zone controls on the live effect prompt.");
                    foreach (var (path, codes) in new[]
                    {
                        ("Grave", mine.grave.AsEnumerable().Reverse().Select(card => card.code)),
                        ("Extra", mine.extra.Select(card => card.code)),
                        ("Banished", mine.banished.AsEnumerable().Reverse().Select(card => card.code)),
                    })
                    {
                        ui.Open(shortcuts.Find(path + "/B")?.GetComponent<Button>(), codes);
                        Require(ui.Modals.IsPromptOpen && !ui.Modals.TryConfirm() && !ui.Modals.TryCancelOrPass(),
                            "The prompt accepted input while the stack viewer covered it.");
                        ui.Close();
                        Require(ui.Modals.IsPromptOpen && ReferenceEquals(duel.ActivePrompt, prompt),
                            "Closing the stack viewer lost the previous effect prompt.");
                    }
                    ui.Modals.HideAll();
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

            Require(inspectedDuringPrompt && choseSummon && mine.mzone.Any(card => card?.code == Chixiao) && duel.RetryCount == 0,
                "The Synchro summon or modal inspection could not finish.");
            ui.Field.Refresh();
            ui.VerifyOwnZones(mine);
            Require(mine.extra.All(card => card.code != Chixiao) && mine.grave.Any(card => card.code == MoYe),
                "The three stacks no longer match the native duel after the summon.");
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

            internal DuelFieldUI Field { get; }
            internal DuelPromptModals Modals { get; }
            internal uint InspectedCode { get; private set; }
            internal int Opens { get; private set; }
            internal CompositeCardView[] VisibleCards => _grid.GetComponentsInChildren<CompositeCardView>();

            internal Browser(DuelSession duel)
            {
                _duel = duel;
                _canvas = new GameObject("Zone Browser Verification", typeof(Canvas));
                ((RectTransform)_canvas.transform).sizeDelta = new Vector2(1600, 900);
                Modals = new DuelPromptModals(_canvas.transform, code => InspectedCode = code,
                    _ => throw new InvalidOperationException("Browsing submitted a modal response."));
                _controller = _canvas.AddComponent<GameAppController>();
                Field = new DuelFieldUI(_controller, _canvas.transform, Modals);
                SetPrivate(_controller, "_session", _duel);
                SetPrivate(_controller, "_duelField", Field);
                SetPrivate(_controller, "_modals", Modals);
                Field.BindSession(_duel, null, null);
                _grid = Private<RectTransform>(Modals, "_zoneGrid");
            }

            internal void VerifyPhasePicker(DuelPrompt prompt)
            {
                Field.Refresh();
                Click(Field.Root.transform.Find("Turn/Dial")?.GetComponent<Button>());
                var picker = Field.Root.transform.Find("PhasePicker");
                Require(picker != null && picker.gameObject.activeInHierarchy, "The phase dial did not open its picker.");
                foreach (var (name, enabled) in new[] { ("Battle", prompt.canBp), ("Main2", false), ("End", prompt.canEp) })
                {
                    var choice = picker.Find($"PhaseControls/{name}/Button")?.GetComponent<Button>();
                    Require(choice != null && choice.IsInteractable() == enabled, $"The phase picker mishandled the {name} command.");
                }
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
            {
                var stack = Private<object>(Field, stackField);
                var root = (RectTransform)stack.GetType().GetField("root")?.GetValue(stack);
                Open(root?.GetComponent<Button>(), expected);
            }

            internal void Open(Button button, IEnumerable<uint> expected)
            {
                var prompt = _duel.ActivePrompt;
                Click(button);
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
