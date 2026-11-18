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
            VerifyOverlayChoices(duel);
            VerifySingleEffect(duel);
            VerifyKoreanEffect(duel);
            VerifyEffectOptions(duel);
            VerifyBundledOptionDescriptions();
            VerifyKoreanEffectCatalogue();
            VerifyGroupedLocations(duel);
            VerifyScrollableChoices(duel);
            VerifyNavigation(duel);
            VerifyFilteredChainCandidates(duel);
            VerifyEmptyChain(duel);
            VerifyMaterialChoices(duel);
            Debug.Log("[VERIFY CHAIN CHOOSER] Grouped locations, native indices, compact effect options, zone overlay, input focus, empty chain and material selection passed.");
        }

        private static void VerifyKoreanEffect(DuelSession duel)
        {
            const uint kaleidoHeart = 28226490;
            const uint legendaryBlackLusterSoldier = 70551291;
            const uint cyberseCodeMagician = 64866;
            const uint spyralVortex = 35699;
            ulong untranslated = (ulong)spyralVortex << 20;
            string rulesText = CardDatabase.GetKoreanCardDescription(spyralVortex);
            Require(CardDatabase.ResolveOptionDescription(untranslated) == null &&
                    CardDatabase.ResolveEffectDescription(untranslated, spyralVortex) == rulesText,
                "An untranslated effect label did not fall back to the Korean card text.");
            ulong drawReplacement = (ulong)legendaryBlackLusterSoldier << 20;
            const string drawLabel = "이번 턴의 일반 드로우 대신 덱에서 의식 마법 카드 1장을 패에 넣는다";
            Require(CardDatabase.ResolveDescription(drawReplacement | 1) == "덱으로 되돌린다" &&
                    CardDatabase.ResolveOptionDescription(drawReplacement) == drawLabel,
                "The Korean choices for Legendary Black Luster Soldier did not resolve.");
            string baseMagicianLabel = CardDatabase.ResolveOptionDescription(64865UL << 20);
            Require(baseMagicianLabel != null && baseMagicianLabel.StartsWith("덱에서 사이버스족 몬스터 1장", StringComparison.Ordinal) &&
                    CardDatabase.ResolveOptionDescription((ulong)cyberseCodeMagician << 20) == baseMagicianLabel,
                "The alternate artwork did not inherit the new specific Korean effect label.");

            using var view = new ChainView();
            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
            prompt.candidates.Add(new SelectableCardItem
            {
                code = kaleidoHeart,
                controller = 0,
                location = YgoConstants.LOCATION_MZONE,
                description = (ulong)kaleidoHeart << 20,
            });
            view.Show(duel, prompt);
            view.SelectChoice(0, kaleidoHeart);
            var effect = view.Modals.ModalRoot.GetComponentsInChildren<Text>()
                .Single(label => label.gameObject.name == "Effect");
            Require(effect.text == "상대 필드의 카드 1장을 주인의 덱으로 되돌린다.",
                "Kaleido-Heart's chain preview did not show its Korean effect text.");

            view.Modals.ShowPrompt(duel, new DuelPrompt { messageType = YgoConstants.MSG_SELECT_EFFECTYN, playerId = 0,
                promptCardCode = legendaryBlackLusterSoldier, promptDesc = drawReplacement });
            effect = view.Modals.ModalRoot.GetComponentsInChildren<Text>()
                .Single(label => label.gameObject.name == "Effect");
            Require(effect.text == drawLabel, "The chain confirmation did not show the concise Korean draw-replacement effect.");

            view.Modals.ShowPrompt(duel, new DuelPrompt { messageType = YgoConstants.MSG_SELECT_EFFECTYN, playerId = 0,
                promptCardCode = spyralVortex, promptDesc = untranslated });
            effect = view.Modals.ModalRoot.GetComponentsInChildren<Text>()
                .Single(label => label.gameObject.name == "Effect");
            Require(effect.text == rulesText, "An untranslated English label escaped into the chain preview.");
            view.Modals.ShowPrompt(duel, new DuelPrompt { messageType = YgoConstants.MSG_SELECT_YESNO, playerId = 0,
                promptDesc = untranslated });
            Require(view.Modals.ModalRoot.GetComponentsInChildren<Text>().Single(label => label.name == "Question").text == rulesText,
                "An untranslated English label escaped into the yes/no confirmation.");
            var options = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_OPTION, playerId = 0 };
            options.options.Add(untranslated);
            options.options.Add(untranslated | 1);
            view.Modals.ShowPrompt(duel, options);
            Require(view.Modals.ModalRoot.GetComponentsInChildren<Text>().Single(label => label.name == "CardText").text == rulesText,
                "An unlabelled choice omitted the Korean source effect.");
        }

        private static void VerifyEffectOptions(DuelSession duel)
        {
            const ulong kashtiraDescription = 4928565UL << 20;
            using var view = new ChainView();
            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_OPTION, playerId = 0 };
            prompt.options.Add(kashtiraDescription | 3);
            prompt.options.Add(kashtiraDescription | 4);
            view.Modals.ShowPrompt(duel, prompt);
            Button Option(int index) => view.Modals.ModalRoot.GetComponentsInChildren<Button>()
                .Single(button => button.name == $"Opt_{index}");

            Require(Option(0).GetComponentInChildren<Text>().text == "내 덱 위에서 카드 3장을 묘지로 보낸다" &&
                    Option(1).GetComponentInChildren<Text>().text == "상대 덱 위에서 카드 3장을 묘지로 보낸다" &&
                    !view.Modals.ModalRoot.GetComponentsInChildren<Text>().Any(label => label.name == "CardText"),
                "Kashtira's effect options must identify their respective decks without repeating card rules.");
            Option(1).onClick.Invoke();

            prompt.options.RemoveAt(0);
            view.Modals.ShowPrompt(duel, prompt);
            Require(Option(0).GetComponentInChildren<Text>().text == "상대 덱 위에서 카드 3장을 묘지로 보낸다",
                "The label for the remaining option depended on its position.");
            Option(0).onClick.Invoke();
            Require(view.Responses.SequenceEqual(new[] { 1, 0 }),
                "The option buttons did not send the indices of the options offered by the engine.");

            view.Modals.ShowPrompt(duel, new DuelPrompt { messageType = YgoConstants.MSG_SELECT_YESNO,
                playerId = 0, promptDesc = kashtiraDescription | 1 });
            var labels = view.Modals.ModalRoot.GetComponentsInChildren<Text>();
            Require(labels.Single(label => label.name == "Question").text == "자신 또는 상대 덱 위에서 카드 3장을 묘지로 보낸다" &&
                    !labels.Any(label => label.name == "CardText"),
                "An effect confirmation included unrelated card rules.");

            view.Modals.ShowPrompt(duel, new DuelPrompt { messageType = YgoConstants.MSG_SELECT_EFFECTYN,
                playerId = 0, promptDesc = 31, promptCardCode = LadyLabrynth });
            Require(view.Modals.ModalRoot.GetComponentsInChildren<Text>()
                    .Single(label => label.name == "Question").text == "직접 공격을 선택하시겠습니까?",
                "An opponent-chosen direct attack was displayed as another effect activation.");
        }

        private static void VerifyBundledOptionDescriptions()
        {
            static string Option(uint cardCode, uint scriptIndex) =>
                CardDatabase.ResolveOptionDescription(((ulong)cardCode << 20) | scriptIndex);

            const uint roundTable = 55742055;
            Require(Option(roundTable, 1) == "덱에서 성기사 카드 1장을 묘지로 보낸다" &&
                    Option(roundTable, 2) == "패에서 성기사 몬스터 1장을 특수 소환한 뒤 패의 성검을 장착할 수 있다" &&
                    Option(roundTable, 3) == "내 묘지의 성기사 몬스터 1장을 패에 넣는다" &&
                    Option(roundTable, 4) == "1장 드로우한다",
                "Round Table's stale database labels did not match the effect branches in Lua.");

            const uint asutra = 1710647;
            Require(Option(asutra, 3) == "대상 몬스터의 레벨을 1 올린다" &&
                    Option(asutra, 4) == "대상 몬스터의 레벨을 1 내린다",
                "The two opposite Level adjustments did not have distinct labels.");

            const uint bingo = 99505609;
            Require(Option(bingo, 2).StartsWith("내 메인 몬스터 존", StringComparison.Ordinal) &&
                    Option(bingo, 3).StartsWith("상대 메인 몬스터 존", StringComparison.Ordinal) &&
                    Option(bingo, 4) == "메인 몬스터 존의 몬스터 5장을 파괴한다" &&
                    Option(bingo, 5) == "마법·함정 존의 카드 5장을 파괴한다",
                "Bingo Card's controller and zone prompts used the wrong branches.");

            const uint thorHammer = 59080;
            Require(Option(thorHammer, 1).Contains("묘지로 보낸다") && Option(thorHammer, 2).Contains("카운터를 2개 제거") &&
                    Option(thorHammer, 3).Contains("마법사족 몬스터 1장을 특수 소환") && Option(thorHammer, 4).Contains("필드의 다른 카드 1장을 제외"),
                "The Korean labels failed to distinguish the selectable costs and effects of Thor Hammer.");
            Require(Option(49238328, 0) == "카드 3장을 제외한다" && Option(49238328, 1) == "카드 6장을 제외한다" &&
                    Option(30453613, 2) == "자신의 필드 존에 놓는다" && Option(30453613, 3) == "상대의 필드 존에 놓는다",
                "The options did not retain the original quantity or player in Korean.");

            Require(CardDatabase.ResolveOptionDescription(94) == "이 카드의 추가 효과를 적용하시겠습니까?" &&
                    CardDatabase.ResolveOptionDescription(210) == "카드를 1장 더 선택하시겠습니까?",
                "A bundled system-level yes/no prompt lost its description.");
            Require(CardDatabase.ResolveOptionDescription(60) == "코인 앞면" &&
                    CardDatabase.ResolveOptionDescription(61) == "코인 뒷면" &&
                    CardDatabase.ResolveOptionDescription(62) == "코인 앞면 효과" &&
                    CardDatabase.ResolveOptionDescription(63) == "코인 뒷면 효과",
                "Coin calls and coin-effect choices must display which side each option represents.");

            ulong[] corePrompts = { 1, 10, 11, 12, 30, 31, 32, 80, 81, 90, 97, 99, 218, 219, 220 };
            var coreLabels = corePrompts.Select(CardDatabase.ResolveOptionDescription).ToArray();
            Require(coreLabels.All(label => !string.IsNullOrWhiteSpace(label)) &&
                    coreLabels.Distinct().Count() == corePrompts.Length &&
                    CardDatabase.ResolveOptionDescription(80) == "배틀 페이즈로 이동한다" &&
                    CardDatabase.ResolveOptionDescription(81) == "엔드 페이즈로 이동한다",
                "Native procedure, attack, phase or cost prompts lost their distinct descriptions.");
        }

        private static void VerifyKoreanEffectCatalogue()
        {
            int labels = 0, withDescriptions = 0;
            foreach (var card in CardDatabase.AllCards)
            {
                for (int i = 0; i < (card.strs?.Length ?? 0); i++)
                {
                    if (string.IsNullOrWhiteSpace(card.strs[i])) continue;
                    ulong id = ((ulong)card.code << 20) | (uint)i;
                    string label = CardDatabase.ResolveOptionDescription(id);
                    Require(label == null || label.Any(letter => letter is >= '가' and <= '힣'),
                        $"An English option label was exposed for {card.code}/{i}.");
                    string text = CardDatabase.ResolveEffectDescription(id, card.code);
                    Require(text != null && text.Any(letter => letter is >= '가' and <= '힣') &&
                        !text.Contains("[Pendulum Effect]"), $"The Korean fallback was missing for {card.code}/{i}.");
                    labels++;
                    if (label != null) withDescriptions++;
                }
            }
            Debug.Log($"[VERIFY KOREAN DUEL EFFECTS] {labels} bundled labels checked. {withDescriptions} Korean labels, " +
                $"{labels - withDescriptions} Korean card-text fallbacks.");
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
            Require(view.Choice(0).Find("EffectNumber/Text")?.GetComponent<Text>().text == "후보 1" &&
                    view.Choice(1).Find("EffectNumber/Text")?.GetComponent<Text>().text == "후보 2",
                "Same-card labels should identify response candidates rather than card effect numbers.");
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

        private static void VerifyOverlayChoices(DuelSession duel)
        {
            using var view = new ChainView();
            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
            for (uint material = 0; material < 2; material++)
            {
                prompt.candidates.Add(new SelectableCardItem
                {
                    code = LadyLabrynth,
                    controller = 0,
                    location = YgoConstants.LOCATION_MZONE | YgoConstants.LOCATION_OVERLAY,
                    sequence = 2,
                    position = material,
                    description = ((ulong)LadyLabrynth << 20) | material,
                });
            }
            view.Show(duel, prompt);
            Require(view.Choice(0).Find("EffectNumber") == null && view.Choice(1).Find("EffectNumber") == null,
                "Separate materials sharing an Xyz monster's field slot were grouped as one card.");
            view.SelectChoice(1, LadyLabrynth);
            var source = view.Modals.ModalRoot.transform.Find("ChainWin/Details/Source/Text")?.GetComponent<Text>();
            Require(source != null && source.text == "내 엑시즈 소재",
                "The overlay candidate did not identify its material source.");
            view.ConfirmChoice();
            Require(view.Responses.SequenceEqual(new[] { 1 }), "Selecting a separate Xyz material sent the wrong native index.");
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

        private static void VerifyFilteredChainCandidates(DuelSession duel)
        {
            using var view = new ChainView();
            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
            prompt.candidates.Add(new SelectableCardItem { code = 14087893, location = YgoConstants.LOCATION_HAND });
            prompt.candidates.Add(new SelectableCardItem
            {
                code = 94656263,
                location = YgoConstants.LOCATION_HAND,
                isTriggeredEffect = true,
            });
            prompt.candidates.Add(new SelectableCardItem
            {
                code = 14558127,
                location = YgoConstants.LOCATION_HAND,
                isEventSpecificEffect = true,
            });

            view.Modals.ShowPrompt(duel, prompt, ChainConfirmationMode.Off);
            Require(view.Modals.IsPromptOpen &&
                    view.Choice(0).GetComponentInChildren<CompositeCardView>().CurrentCode == 94656263 &&
                    !view.Modals.ModalRoot.GetComponentsInChildren<Transform>().Any(child => child.name == "Ch_1"),
                "The filtered chooser showed an unrelated fast effect.");
            view.SelectChoice(0, 94656263);
            view.ConfirmChoice();
            Require(view.Responses.SequenceEqual(new[] { 1 }), "The filtered chooser changed the native response index.");

            using var automatic = new ChainView();
            automatic.Modals.ShowPrompt(duel, prompt, ChainConfirmationMode.Auto);
            Require(automatic.Choice(0).GetComponentInChildren<CompositeCardView>().CurrentCode == 94656263,
                "AUTO hid a triggered effect in a quiet window.");
            automatic.SelectChoice(1, 14558127);
            automatic.ConfirmChoice();
            Require(automatic.Responses.SequenceEqual(new[] { 2 }),
                "AUTO hid a response specific to the current event or submitted the wrong native index.");
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
            foreach (uint loc in new[] { YgoConstants.LOCATION_GRAVE, YgoConstants.LOCATION_HAND,
                YgoConstants.LOCATION_GRAVE, YgoConstants.LOCATION_MZONE })
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
            Require(list.childCount == 3 && list.GetChild(0).Find("GroupFrame/Cards/Ch_0") != null &&
                list.GetChild(0).Find("GroupFrame/Cards/Ch_2") != null && list.GetChild(1).Find("GroupFrame/Cards/Ch_1") != null &&
                list.GetChild(2).Find("GroupFrame/Cards/Ch_3") != null,
                "Grouping by location did not retain the original candidate identities.");
            var icons = Enumerable.Range(0, list.childCount).Select(i =>
                list.GetChild(i).Find("LocationTab/Icon").GetComponent<Graphic>()).ToArray();
            Require(icons.All(glyph => !glyph.raycastTarget && glyph.rectTransform.rect.size == new Vector2(18f, 19f)) &&
                icons.Select(glyph => glyph.color).Distinct().Count() == 3,
                "The field, hand and graveyard glyphs were enlarged or lost their individual cues.");
            view.SelectChoice(2, LadyLabrynth);
            Require(view.Choice(2).GetComponent<Outline>().effectDistance.x > view.Choice(0).GetComponent<Outline>().effectDistance.x,
                "Moving the selection left the stronger outline on the previous card.");
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
                VerifyPadDialogs(duel, events);
            }
            finally
            {
                if (registeredManually)
                    typeof(EventSystem).GetMethod("OnDisable", lifecycle).Invoke(events, null);
                UnityEngine.Object.DestroyImmediate(host);
                if (previous != null) EventSystem.current = previous;
            }
        }

        private static void VerifyPadDialogs(DuelSession duel, EventSystem events)
        {
            var canvas = new GameObject("Duel Pad Dialog Verification", typeof(Canvas));
            try
            {
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600, 900);
                var responses = new List<byte[]>();
                var modals = new DuelPromptModals(canvas.transform, null, responses.Add);
                const uint dragon = 89631139;
                var page = modals.ModalRoot.transform.Find("Win");

                // Only the explicit action command should dispatch a move from the Extra Deck viewer.
                var extraCard = new LiveCardState { code = dragon, location = YgoConstants.LOCATION_EXTRA };
                LiveCardState openedCard = null;
                modals.ShowZoneViewer("엑스트라 덱", new[] { extraCard }, hasCardActions: _ => true,
                    onOpenCardActions: card => openedCard = card);
                Canvas.ForceUpdateCanvases();
                var preview = events.currentSelectedGameObject;
                Pad(modals, button: "Submit");
                Require(modals.IsZoneViewerOpen && openedCard == null && events.currentSelectedGameObject == preview,
                    "Pad submit on an Extra Deck preview did not preserve the separate action step.");
                Pad(modals, Vector2Int.down);
                Require(events.currentSelectedGameObject.transform.parent.name == "Act", "Pad navigation could not reach the selected card's action.");
                Pad(modals, button: "Submit");
                Require(openedCard == extraCard && !modals.IsZoneViewerOpen, "The focused Extra Deck action did not use the original card.");

                var sort = new DuelPrompt { messageType = YgoConstants.MSG_SORT_CHAIN, isChainSort = true };
                sort.candidates.Add(new SelectableCardItem { code = LadyLabrynth });
                sort.candidates.Add(new SelectableCardItem { code = dragon });
                modals.ShowPrompt(duel, sort);
                var firstRank = page.Find("Body/Cards/Viewport/Content/Sort_0/Order/Text").GetComponent<Text>();
                Require(firstRank.text == "발동 1 · 처리 2", "Chain order did not distinguish activation from resolution.");
                Pad(modals, button: "Submit");
                Require(events.currentSelectedGameObject.transform.parent.parent.name == "Sort_1",
                    "Reordering cards did not move pad focus with the moved card.");
                events.SetSelectedGameObject(page.Find("Footer/Btn_순서 확정/B").gameObject);
                Pad(modals, button: "Submit");
                Require(responses.Count == 1 && responses[0].SequenceEqual(new byte[] { 1, 0 }), "The reordered cards did not keep their native indices.");
                responses.Clear();

                var bottom = new DuelPrompt { messageType = YgoConstants.MSG_SORT_CARD };
                bottom.candidates.Add(new SelectableCardItem
                    { code = LadyLabrynth, controller = (byte)duel.AiPlayerIndex, location = YgoConstants.LOCATION_DECK, sequence = 0 });
                bottom.candidates.Add(new SelectableCardItem
                    { code = dragon, controller = (byte)duel.AiPlayerIndex, location = YgoConstants.LOCATION_DECK, sequence = 1 });
                modals.ShowPrompt(duel, bottom);
                Require(page.Find("Hdr/TitleBand/Title").GetComponent<Text>().text == "상대 덱 아래쪽 카드 순서" &&
                    page.Find("Body/Cards/Viewport/Content/Sort_0/Order/Text").GetComponent<Text>().text == "1번 · 맨 아래",
                    "A bottom-of-deck sort used the top-of-deck direction or hid the target player.");
                page.Find("Footer/Btn_원래 순서로 확정/B").GetComponent<Button>().onClick.Invoke();
                Require(responses.Count == 1 && responses[0].SequenceEqual(new byte[] { 255 }),
                    "Keeping the original arrangement no longer uses the original-order response.");
                responses.Clear();
                VerifyChoiceLocations(duel, modals, page, responses);

                var declare = new DuelPrompt { messageType = YgoConstants.MSG_ANNOUNCE_CARD };
                modals.ShowPrompt(duel, declare);
                var input = page.Find("Body/Search/Inp").GetComponent<InputField>();
                var keyboard = modals.ModalRoot.transform.Find("PadKeyboard");
                Require(events.currentSelectedGameObject == input.gameObject, "Declaration search did not receive focus.");
                Pad(modals, button: "Submit");
                Require(keyboard.gameObject.activeSelf && events.currentSelectedGameObject.transform.IsChildOf(keyboard),
                    "Pad submit on declaration search did not open the onscreen keyboard.");
                Pad(modals, button: "Cancel");
                Require(!keyboard.gameObject.activeSelf && modals.IsPromptOpen && events.currentSelectedGameObject == input.gameObject && responses.Count == 0,
                    "Canceling onscreen input submitted or left the declaration prompt.");

                Pad(modals, button: "Submit");
                void PressKey(string path)
                {
                    events.SetSelectedGameObject(keyboard.Find(path).gameObject);
                    Pad(modals, button: "Submit");
                }
                PressKey("Window/Page/Button");
                PressKey("Window/Page/Button");
                foreach (char digit in "89631139")
                    PressKey($"Window/Key{"1234567890".IndexOf(digit)}/Button");
                PressKey("Window/Apply/Button");
                Require(input.text == "89631139" && events.currentSelectedGameObject == input.gameObject && !keyboard.gameObject.activeSelf,
                    "Entering a passcode did not filter the declaration in place or restore input focus.");
                Pad(modals, Vector2Int.down);
                Require(events.currentSelectedGameObject.name == "Dec_89631139", "Pad navigation could not reach the refreshed declaration result.");
                Pad(modals, button: "Submit");
                Require(responses.Count == 0 && modals.IsPromptOpen, "Selecting a declaration result skipped explicit confirmation.");
                Pad(modals, Vector2Int.down);
                var declarationConfirm = page.Find("Footer/Btn_선언 확정/B").GetComponent<Button>();
                Require(events.currentSelectedGameObject == declarationConfirm.gameObject,
                    $"Pad down from the sole declaration card reached {events.currentSelectedGameObject.name} ({events.currentSelectedGameObject.transform.parent.name}) rather than confirm, available={declarationConfirm.interactable}.");
                Pad(modals, button: "Submit");
                Require(responses.Count == 1 && BitConverter.ToUInt32(responses[0], 0) == dragon && !modals.IsPromptOpen,
                    "Declaring a filtered card by pad did not submit its exact code.");

                int rematches = 0, opponentSelections = 0;
                modals.ShowResult(true, false, null, 2, 8000, 0, () => rematches++, () => opponentSelections++, null);
                Canvas.ForceUpdateCanvases();
                Pad(modals, Vector2Int.right);
                Pad(modals, button: "Submit");
                Require(opponentSelections == 1 && rematches == 0 && !modals.ResultRoot.activeSelf,
                    "The result screen replayed the duel instead of opening the focused opponent selection.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        private static void VerifyChoiceLocations(DuelSession duel, DuelPromptModals modals, Transform page,
            List<byte[]> responses)
        {
            const uint dragon = 89631139;
            var material = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_UNSELECT_CARD, finishable = true };
            var regions = new[] { YgoConstants.LOCATION_HAND, YgoConstants.LOCATION_MZONE, YgoConstants.LOCATION_DECK };
            foreach (uint region in regions)
                material.candidates.Add(new SelectableCardItem { code = dragon, controller = (byte)duel.HumanPlayerIndex, location = region });
            material.candidates.Add(new SelectableCardItem { code = 0, controller = (byte)duel.AiPlayerIndex, location = YgoConstants.LOCATION_HAND });
            material.unselectCandidates.Add(new SelectableCardItem
                { code = dragon, controller = (byte)duel.HumanPlayerIndex, location = YgoConstants.LOCATION_GRAVE });
            modals.ShowPrompt(duel, material);
            Canvas.ForceUpdateCanvases();

            var tiles = page.Find("Body/Cards/Viewport/Content");
            for (int i = 0; i <= material.candidates.Count; i++)
            {
                var badge = tiles.Find($"Imm_{i}/Source");
                var icon = badge.Find("LocationIcon").GetComponent<Graphic>();
                var tone = badge.GetComponent<Image>().color;
                bool isOpponent = i == material.candidates.Count - 1;
                Require(icon != null && !icon.raycastTarget && badge.GetComponentInChildren<Text>() == null &&
                    (isOpponent ? tone.r > tone.b : tone.b > tone.r),
                    "A material card did not show a location icon with the matching player's color.");
                if (icon is Image image)
                    Require(image.sprite != null, "A loaded location emblem did not have a sprite.");
                else
                {
                    using var mesh = new VertexHelper();
                    icon.GetType().GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new[] { typeof(VertexHelper) }, null).Invoke(icon, new object[] { mesh });
                    Require(mesh.currentVertCount > 0, "The bundled location glyph did not render.");
                }
            }

            var hidden = tiles.Find("Imm_3/Name/Text").GetComponent<Text>();
            var checkedCard = tiles.Find($"Imm_{material.candidates.Count}");
            Require(hidden.text == "뒷면 카드" && checkedCard.Find("Chk").gameObject.activeSelf,
                "Adding location icons changed hidden information or the selected-material marker.");
            checkedCard.GetComponent<Button>().onClick.Invoke();
            Require(responses.Count == 1 && BitConverter.ToInt32(responses[0], 0) == 1 &&
                BitConverter.ToInt32(responses[0], 4) == material.candidates.Count,
                "Clicking a selected material no longer sends its original index.");
            responses.Clear();

            var sum = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_SUM, min = 1, max = 1, acc = 7 };
            sum.mustSelect.Add(new SelectableCardItem
                { code = dragon, controller = (byte)duel.HumanPlayerIndex, location = YgoConstants.LOCATION_MZONE, sumParam = 4 });
            sum.candidates.Add(new SelectableCardItem
                { code = dragon, controller = (byte)duel.HumanPlayerIndex, location = YgoConstants.LOCATION_GRAVE, sumParam = 3 });
            modals.ShowPrompt(duel, sum);
            var mandatory = page.Find("Body/Cards/Viewport/Content/Tile_-1/Source");
            Require(mandatory.Find("LocationIcon") != null &&
                mandatory.Find("Detail/Text").GetComponent<Text>().text == "필수 · 4",
                "The material icon removed required selection details.");
            Debug.Log("[VERIFY PROMPT LOCATION ICONS] Material location, ownership color, hidden cards, and selection indices passed.");
        }

        private static void Pad(DuelPromptModals modals, Vector2Int direction = default, string button = "None")
        {
            var ui = typeof(DuelPromptModals).Assembly;
            var buttons = ui.GetType("YGOPortable.UI.DeckPadButtons", true);
            var frameType = ui.GetType("YGOPortable.UI.DeckPadFrame", true);
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            var frame = Activator.CreateInstance(frameType, instance, null, new object[] { direction, Enum.Parse(buttons, button), 0f }, null);
            bool handled = (bool)typeof(DuelPromptModals).GetMethod("HandlePad", instance).Invoke(modals, new[] { frame });
            Require(handled, "An open duel overlay did not claim its controller input.");
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
                Require(panel.rect.height < 450f && panel.anchorMax.y < .65f && scroll != null && scroll.horizontal &&
                        scroll.content.rect.width > scroll.viewport.rect.width, "Material choices did not fit a compact, scrollable panel.");
                var confirm = panel.Find("Footer/Btn_선택 완료/B").GetComponent<Button>();
                var first = scroll.content.Find("Tile_0").GetComponent<Button>();
                Require(((RectTransform)first.transform.Find("Card/V/AspectBox")).rect.height >= 170f,
                    "The material choice artwork became too small.");
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
                    bool single = prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN || prompt.candidates.Count == 1;
                    Require(panel.rect.height > 0 && panel.rect.height < size.y * .5f &&
                            panel.rect.width > 0 && panel.rect.width < size.x * (single ? .5f : .8f),
                        $"Chain chooser is not compact at {size.x}×{size.y}.");
                    var artwork = (RectTransform)Choice(0).Find("Card/V/AspectBox");
                    Require(artwork.rect.height >= (size.y >= 900f ? 160f : 136f),
                        $"Chain card artwork was only {artwork.rect.height:F1} high at {size.x}×{size.y}.");
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
                string text = CardDatabase.ResolveEffectDescription(source.description, source.code)
                    ?? "이 카드의 효과를 발동할 수 있습니다.";
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
