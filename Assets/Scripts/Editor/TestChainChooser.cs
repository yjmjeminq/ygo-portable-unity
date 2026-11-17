using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static class TestChainChooser
    {
        private const uint LadyLabrynth = 81497285;

        internal static void Run()
        {
            using var duel = new DuelSession();
            VerifyDuplicateEffectsAndZoneViewer(duel);
            VerifySingleEffect(duel);
            VerifyGroupedLocations(duel);
            VerifyScrollableChoices(duel);
            VerifyNavigation(duel);
            VerifyEmptyChain(duel);
            VerifyMaterialChoices(duel);
            Debug.Log("[VERIFY CHAIN CHOOSER] Grouped locations, native indices, duplicate effects, zone overlay, input focus, empty chain and material selection passed.");
        }

        internal static void SubmitNativeChoice(DuelSession duel, int index)
        {
            using var view = new ChainView();
            var prompt = duel.ActivePrompt;
            view.Show(duel, prompt);
            view.VerifyPassAvailability(!prompt.forced);
            if (prompt.forced)
                Require(!view.Modals.TryCancelOrPass() && view.Responses.Count == 0,
                    "A required chain accepted cancel.");
            if (index < 0)
            {
                view.Pass();
            }
            else
            {
                var source = prompt.candidates[index];
                view.SelectChoice(index, source.code);
                view.VerifyEffect(source);
                view.ConfirmChoice();
            }

            Require(view.Responses.SequenceEqual(new[] { index }), $"Native chain choice sent a response other than {index}.");
            Require(!view.Modals.IsPromptOpen, "Chain chooser stayed open after a response.");
            duel.SubmitResponseInt(view.Responses[0]);
            Require(duel.RetryCount == 0, "The native engine rejected the chain chooser response.");
        }

        private static void VerifyDuplicateEffectsAndZoneViewer(DuelSession duel)
        {
            using var view = new ChainView();
            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
            for (int i = 0; i < 2; i++)
            {
                prompt.candidates.Add(new SelectableCardItem
                {
                    code = LadyLabrynth,
                    controller = 0,
                    location = YgoConstants.LOCATION_MZONE,
                    sequence = 2,
                    description = ((ulong)LadyLabrynth << 20) | (uint)i,
                });
            }

            view.Show(duel, prompt);
            view.VerifyPassAvailability(true);
            view.SelectChoice(0, LadyLabrynth);
            view.VerifyEffect(prompt.candidates[0]);
            view.SelectChoice(1, LadyLabrynth);
            view.VerifyEffect(prompt.candidates[1]);
            Require(!view.Modals.TryConfirm() && view.Responses.Count == 0,
                "Inspecting a chain option or confirming from the global shortcut submitted an effect.");

            var activation = view.ConfirmButton;
            view.Modals.ShowCodeListViewer("내 묘지", new[] { LadyLabrynth });
            activation.onClick.Invoke();
            Require(view.Modals.IsPromptOpen && view.Modals.IsZoneViewerOpen &&
                    !view.Modals.TryConfirm() && !view.Modals.TryCancelOrPass() && view.Responses.Count == 0,
                "Opening a zone list did not protect the pending chain choice.");
            view.Modals.CloseZoneViewer();
            Require(view.Modals.IsPromptOpen && !view.Modals.IsZoneViewerOpen,
                "Closing a zone list dismissed the pending chain choice.");
            view.VerifyEffect(prompt.candidates[1]);
            view.ConfirmChoice();
            Require(view.Responses.SequenceEqual(new[] { 1 }), "Same-card effects were not submitted by their distinct indices.");
        }

        private static void VerifySingleEffect(DuelSession duel)
        {
            var prompt = new DuelPrompt
            {
                messageType = YgoConstants.MSG_SELECT_EFFECTYN,
                playerId = 0,
                promptCon = 0,
                promptLoc = YgoConstants.LOCATION_HAND,
                promptCardCode = LadyLabrynth,
                promptDesc = (ulong)LadyLabrynth << 20,
            };

            using var view = new ChainView();
            view.Show(duel, prompt);
            view.VerifyPassAvailability(true);
            view.VerifyEffect(new SelectableCardItem
            {
                code = LadyLabrynth,
                location = YgoConstants.LOCATION_HAND,
                description = prompt.promptDesc,
            });
            view.ConfirmChoice();
            Require(view.Responses.SequenceEqual(new[] { 1 }), "A single-effect activation did not submit YES.");

            using var declined = new ChainView();
            declined.Show(duel, prompt);
            declined.Pass();
            Require(declined.Responses.SequenceEqual(new[] { 0 }), "A single-effect pass did not submit NO.");
        }

        private static void VerifyEmptyChain(DuelSession duel)
        {
            using var view = new ChainView();
            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
            view.Modals.ShowPrompt(duel, prompt);
            Require(!view.Modals.IsPromptOpen && !view.Modals.TryConfirm(),
                "An optional chain with no candidates opened an empty chooser.");
            Require(view.Responses.SequenceEqual(new[] { -1 }),
                "An empty optional chain submitted an invalid response.");
        }

        private static void VerifyGroupedLocations(DuelSession duel)
        {
            using var view = new ChainView();
            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
            foreach (uint loc in new[] { YgoConstants.LOCATION_GRAVE, YgoConstants.LOCATION_HAND, YgoConstants.LOCATION_GRAVE })
            {
                prompt.candidates.Add(new SelectableCardItem
                {
                    code = LadyLabrynth,
                    location = loc,
                    sequence = (uint)prompt.candidates.Count,
                    description = (ulong)LadyLabrynth << 20,
                });
            }
            view.Show(duel, prompt);
            var list = view.Modals.ModalRoot.transform.Find("ChainWin/Cards/Viewport/Content");
            Require(list.childCount == 2 && list.GetChild(0).Find("GroupFrame/Cards/Ch_0") != null &&
                list.GetChild(0).Find("GroupFrame/Cards/Ch_2") != null && list.GetChild(1).Find("GroupFrame/Cards/Ch_1") != null,
                "Grouping by location did not retain the original candidate identities.");
            view.SelectChoice(2, LadyLabrynth);
            view.VerifyEffect(prompt.candidates[2]);
            view.ConfirmChoice();
            Require(view.Responses.SequenceEqual(new[] { 2 }), "A grouped chain option sent its visual index to the engine.");
        }

        private static void VerifyScrollableChoices(DuelSession duel)
        {
            using var view = new ChainView();
            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
            for (uint i = 0; i < 12; i++)
            {
                prompt.candidates.Add(new SelectableCardItem
                {
                    code = LadyLabrynth,
                    location = YgoConstants.LOCATION_GRAVE,
                    sequence = i,
                    description = ((ulong)LadyLabrynth << 20) | 1,
                });
            }

            view.Show(duel, prompt);
            var scroll = view.Modals.ModalRoot.transform.Find("ChainWin/Cards")?.GetComponent<ScrollRect>();
            Require(scroll != null && scroll.horizontal && !scroll.vertical &&
                    scroll.content.rect.width > scroll.viewport.rect.width && scroll.viewport.GetComponent<RectMask2D>() != null,
                "A long chain did not scroll horizontally inside its compact card list.");
            scroll.horizontalNormalizedPosition = 1f;
            view.SelectChoice(11, LadyLabrynth);
            view.ConfirmChoice();
            Require(view.Responses.SequenceEqual(new[] { 11 }), "Scrolling changed the displayed chain candidate indices.");
        }

        private static void VerifyNavigation(DuelSession duel)
        {
            var previous = EventSystem.current;
            var host = new GameObject("Duel Choice Navigation Verification", typeof(EventSystem));
            var events = host.GetComponent<EventSystem>();
            const BindingFlags lifecycle = BindingFlags.Instance | BindingFlags.NonPublic;
            bool registeredManually = !Application.isPlaying;
            try
            {
                if (registeredManually)
                    typeof(EventSystem).GetMethod("OnEnable", lifecycle).Invoke(events, null);
                EventSystem.current = events;
                using var view = new ChainView();
                var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
                for (uint i = 0; i < 12; i++)
                {
                    prompt.candidates.Add(new SelectableCardItem
                    {
                        code = LadyLabrynth,
                        location = YgoConstants.LOCATION_GRAVE,
                        sequence = i,
                        description = ((ulong)LadyLabrynth << 20) | (i % 2),
                    });
                }

                view.Show(duel, prompt);
                Require(events.currentSelectedGameObject == view.Choice(0).gameObject, "New chain prompt did not receive input focus.");
                ExecuteEvents.Execute(events.currentSelectedGameObject,
                    new AxisEventData(events) { moveDir = MoveDirection.Right, moveVector = Vector2.right }, ExecuteEvents.moveHandler);
                Require(events.currentSelectedGameObject == view.Choice(1).gameObject, "Right navigation did not focus the next effect.");
                view.VerifyEffect(prompt.candidates[1]);
                var last = view.Choice(11).gameObject;
                events.SetSelectedGameObject(last);
                var scroll = last.GetComponentInParent<ScrollRect>();
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, last.transform);
                Require(bounds.max.x <= scroll.viewport.rect.xMax + 0.5f, "A focused effect scrolled beyond the visible strip.");
                ExecuteEvents.Execute(last, new BaseEventData(events), ExecuteEvents.submitHandler);
                Require(view.Responses.Count == 0, "Submitting on a focused effect skipped its review step.");

                view.Modals.ShowCodeListViewer("내 묘지", new[] { LadyLabrynth });
                Require(events.currentSelectedGameObject.transform.IsChildOf(view.Modals.ZoneViewRoot.transform),
                    "The zone viewer failed to take input focus.");
                ExecuteEvents.Execute(events.currentSelectedGameObject, new BaseEventData(events), ExecuteEvents.cancelHandler);
                Require(!view.Modals.IsZoneViewerOpen && events.currentSelectedGameObject == last && view.Responses.Count == 0,
                    "Leaving a zone viewer did not restore the pending chain choice and its focus.");

                var fieldView = view.Modals.ModalRoot.transform.Find("FieldView/B").GetComponent<Button>();
                fieldView.onClick.Invoke();
                view.Modals.ShowCodeListViewer("내 묘지", Array.Empty<uint>());
                Require(events.currentSelectedGameObject.transform.parent.name == "Close",
                    "An empty zone did not focus its close button.");
                ExecuteEvents.Execute(events.currentSelectedGameObject, new BaseEventData(events), ExecuteEvents.cancelHandler);
                Require(events.currentSelectedGameObject == fieldView.gameObject, "The empty zone did not return focus to field view.");
                ExecuteEvents.Execute(events.currentSelectedGameObject, new BaseEventData(events), ExecuteEvents.cancelHandler);
                Require(events.currentSelectedGameObject == last && view.Responses.Count == 0,
                    "Returning from field view changed or activated the pending chain choice.");

                events.SetSelectedGameObject(view.ConfirmButton.gameObject);
                ExecuteEvents.Execute(events.currentSelectedGameObject, new BaseEventData(events), ExecuteEvents.submitHandler);
                Require(view.Responses.SequenceEqual(new[] { 11 }), "Submitting the focused confirm sent the wrong chain candidate.");
            }
            finally
            {
                if (registeredManually)
                    typeof(EventSystem).GetMethod("OnDisable", lifecycle).Invoke(events, null);
                UnityEngine.Object.DestroyImmediate(host);
                if (previous != null) EventSystem.current = previous;
            }
        }

        private static void VerifyMaterialChoices(DuelSession duel)
        {
            var canvas = new GameObject("Material Chooser Verification", typeof(Canvas));
            try
            {
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600, 900);
                var responses = new List<byte[]>();
                var modals = new DuelPromptModals(canvas.transform, null, responses.Add);
                var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_SUM, acc = 8, min = 1, max = 1 };
                var fixedMaterial = new SelectableCardItem { code = LadyLabrynth, sumParam = 4 };
                prompt.mustSelect.Add(fixedMaterial);
                for (int i = 0; i < 11; i++)
                    prompt.candidates.Add(new SelectableCardItem { code = LadyLabrynth, sumParam = 2 });
                prompt.candidates.Add(fixedMaterial);
                modals.ShowPrompt(duel, prompt);
                Canvas.ForceUpdateCanvases();

                var panel = (RectTransform)modals.ModalRoot.transform.Find("Win");
                var scroll = panel.Find("Body/Cards")?.GetComponent<ScrollRect>();
                Require(panel.rect.height < 450f && panel.anchorMax.y < .5f && scroll != null && scroll.horizontal &&
                        scroll.content.rect.width > scroll.viewport.rect.width, "Material choices did not fit a compact, scrollable panel.");
                var confirm = panel.Find("Footer/Btn_선택 완료/B").GetComponent<Button>();
                var first = scroll.content.Find("Tile_0").GetComponent<Button>();
                first.onClick.Invoke();
                Require(!confirm.interactable, "A material that failed the required sum enabled confirmation.");
                confirm.onClick.Invoke();
                Require(responses.Count == 0, "A disabled material confirmation submitted a response.");
                first.onClick.Invoke();
                Require(modals.IsPromptOpen && responses.Count == 0, "Clicking an invalid material twice submitted a response.");

                first.onClick.Invoke();
                scroll.horizontalNormalizedPosition = 1f;
                scroll.content.Find("Tile_11").GetComponent<Button>().onClick.Invoke();
                Require(scroll.content.Find("Tile_-1/Chk").gameObject.activeSelf &&
                        !scroll.content.Find("Tile_0/Chk").gameObject.activeSelf && confirm.interactable,
                    "Selecting the valid last material did not keep the fixed card and replace the previous choice.");
                Require(modals.TryConfirm() && !modals.IsPromptOpen && responses.Count == 1 &&
                        responses[0].Length == 12 && BitConverter.ToInt32(responses[0], 0) == 0 &&
                        BitConverter.ToInt32(responses[0], 4) == 1 && BitConverter.ToInt32(responses[0], 8) == 11,
                    "The valid material selection did not keep its native index after scrolling.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        private sealed class ChainView : IDisposable
        {
            private readonly GameObject _canvas = new GameObject("Chain Chooser Verification", typeof(Canvas));
            private readonly RectTransform _canvasRect;
            private readonly List<uint> _inspected = new();
            internal readonly DuelPromptModals Modals;
            internal readonly List<int> Responses = new();

            internal ChainView()
            {
                _canvasRect = (RectTransform)_canvas.transform;
                _canvasRect.sizeDelta = new Vector2(1600, 900);
                Modals = new DuelPromptModals(_canvas.transform, _inspected.Add, response =>
                {
                    Require(response.Length == sizeof(int), "Chain chooser sent a non-integer response.");
                    Responses.Add(BitConverter.ToInt32(response, 0));
                });
            }

            internal void Show(DuelSession duel, DuelPrompt prompt)
            {
                Modals.ShowPrompt(duel, prompt);
                Require(Modals.IsPromptOpen, "A chain with available choices did not open.");
                Canvas.ForceUpdateCanvases();
                for (int i = 0; i < prompt.candidates.Count; i++)
                    Require(Choice(i).GetComponentInChildren<CompositeCardView>().CurrentCode == prompt.candidates[i].code,
                        $"Wrong source card for chain choice {i}.");
                if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN)
                    Require(Choice(0).GetComponentInChildren<CompositeCardView>().CurrentCode == prompt.promptCardCode,
                        "Single-effect chooser displayed the wrong card.");
                foreach (var size in new[] { new Vector2(1600, 900), new Vector2(1024, 768) })
                {
                    _canvasRect.sizeDelta = size;
                    Canvas.ForceUpdateCanvases();
                    var panel = (RectTransform)Modals.ModalRoot.transform.Find("ChainWin");
                    Require(panel != null && panel.gameObject.activeInHierarchy, "The compact chain panel did not open.");
                    Require(panel.rect.height > 0 && panel.rect.height < size.y * .5f &&
                            panel.rect.width > 0 && panel.rect.width < size.x * .8f,
                        $"Chain chooser is not compact at {size.x}×{size.y}.");
                }
            }

            internal void VerifyPassAvailability(bool optional)
            {
                var button = FooterButton("Pass");
                bool canPass = button != null && button.isActiveAndEnabled && button.interactable;
                Require(canPass == optional, "The pass button did not match the engine's optional/forced flag.");
            }

            internal Transform Choice(int index) =>
                Modals.ModalRoot.GetComponentsInChildren<Transform>().Single(child => child.name == $"Ch_{index}");

            internal void SelectChoice(int index, uint source)
            {
                var button = Choice(index).GetComponent<Button>();
                Require(button != null && button.interactable, $"Chain choice {index} cannot be selected.");
                button.onClick.Invoke();
                Require(Modals.IsPromptOpen && Responses.Count == 0 && _inspected.LastOrDefault() == source,
                    $"Previewing chain choice {index} did not inspect the corresponding card.");
            }

            internal void VerifyEffect(SelectableCardItem source)
            {
                string text = CardDatabase.ResolveEffectDescription(source.description, source.code);
                string location = YgoConstants.GetLocationNameKR(source.location);
                var visibleText = Modals.ModalRoot.GetComponentsInChildren<Text>();
                var labels = visibleText.Select(label => label.text).ToArray();
                var effect = visibleText.SingleOrDefault(label => label.gameObject.name == "Effect");
                Require(labels.Any(label => label.Contains(CardDatabase.GetName(source.code))) &&
                        labels.Any(label => label.Contains(location)) && !string.IsNullOrWhiteSpace(text) &&
                        effect != null && effect.text.Contains(text),
                    "The preview did not show the selected card, source zone and effect description.");
            }

            private Button FooterButton(string name) =>
                Modals.ModalRoot.transform.Find($"ChainWin/Footer/{name}/B")?.GetComponent<Button>();

            internal Button ConfirmButton => FooterButton("Activate");

            internal void ConfirmChoice() => ClickFooter("Activate");

            internal void Pass() => ClickFooter("Pass");

            private void ClickFooter(string name)
            {
                Button button = FooterButton(name);
                Require(button != null && button.isActiveAndEnabled && button.interactable,
                    $"Chain footer button '{name}' is not available.");
                button.onClick.Invoke();
            }

            public void Dispose() => UnityEngine.Object.DestroyImmediate(_canvas);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException($"[CHAIN CHOOSER] {message}");
        }
    }
}
