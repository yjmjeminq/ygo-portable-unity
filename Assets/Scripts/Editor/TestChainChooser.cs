using System;
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
    internal static class TestChainChooser
    {
        private const uint LadyLabrynth = 81497285;

        internal static void Run()
        {
            using var duel = new DuelSession();
            VerifyDuplicateEffectsAndZoneViewer(duel);
            VerifyActivationChoicesAcrossReopen(duel);
            VerifyOverlayChoices(duel);
            VerifySingleEffect(duel);
            VerifyKoreanEffect(duel);
            VerifyEffectOptions(duel);
            VerifyKoreanPendulumReadouts(duel);
            VerifyLightningStormOptions();
            VerifyCyberRaiderOptions();
            VerifyRunickTipOptions();
            VerifyDreadnoughtServantConfirmation();
            VerifyGagagaLevelInstruction();
            VerifyCardTrooperCount();
            VerifyDragonShrineConfirmation();
            VerifyPerlereinoConfirmation();
            VerifyPaidraSelectionHint();
            VerifyChickenGameOptions();
            VerifyRamificationsOptions();
            VerifyOddEyesDamageConfirmation();
            VerifyTrapMonsterPosition();
            VerifyBundledOptionDescriptions();
            VerifyKoreanEffectCatalogue();
            VerifyGroupedLocations(duel);
            VerifyScrollableChoices(duel);
            VerifyNavigation(duel);
            VerifyFilteredChainCandidates(duel);
            VerifyEmptyChain(duel);
            VerifyActivationSourcesAndDelayedConfirmation(duel);
            VerifyChainOrderByTapping(duel);
            VerifyMobileChainTouchTargets(duel);
            VerifyMaterialChoices(duel);
            VerifyCandidateReadouts();
            Debug.Log("[VERIFY CHAIN CHOOSER] Grouped locations, native indices, compact effect options, zone overlay, input focus, empty chain and material selection passed.");
        }

        private static void VerifyActivationSourcesAndDelayedConfirmation(DuelSession duel)
        {
            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
            prompt.candidates.Add(new SelectableCardItem { location = YgoConstants.LOCATION_HAND, sequence = 3 });
            prompt.candidates.Add(new SelectableCardItem { location = YgoConstants.LOCATION_GRAVE, sequence = 8, isTriggeredEffect = true });
            prompt.candidates.Add(new SelectableCardItem { location = YgoConstants.LOCATION_REMOVED, sequence = 1 });
            prompt.candidates.Add(new SelectableCardItem { location = YgoConstants.LOCATION_MZONE | YgoConstants.LOCATION_OVERLAY,
                sequence = 4, position = 2 });
            bool Offers(ChainConfirmationMode mode, uint location, uint index) =>
                duel.HasEffectActivationAt(prompt, mode, 0, location, index);
            Require(Offers(ChainConfirmationMode.On, YgoConstants.LOCATION_HAND, 3) &&
                    Offers(ChainConfirmationMode.On, YgoConstants.LOCATION_MZONE, 4) &&
                    Offers(ChainConfirmationMode.On, YgoConstants.LOCATION_REMOVED, 1) &&
                    Offers(ChainConfirmationMode.Off, YgoConstants.LOCATION_GRAVE, 8) &&
                    !Offers(ChainConfirmationMode.Off, YgoConstants.LOCATION_HAND, 3) &&
                    !Offers(ChainConfirmationMode.On, YgoConstants.LOCATION_GRAVE, 7),
                "Activation highlighting disagrees with the native card slots or the selected chain mode.");

            var confirmation = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_EFFECTYN, playerId = 0,
                promptLoc = YgoConstants.LOCATION_GRAVE, promptSeq = 8, promptCardCode = LadyLabrynth };
            var canvas = new GameObject("Pending confirmation test", typeof(Canvas));
            try
            {
                bool ready = false;
                int attempts = 0;
                var modals = new DuelPromptModals(canvas.transform, null, null, (session, pending, response) =>
                {
                    Require(session == duel && pending == confirmation && BitConverter.ToInt32(response, 0) == 1,
                        "The confirmation lost its originating duel, prompt or affirmative response.");
                    attempts++;
                    return ready;
                });
                modals.ShowPrompt(duel, confirmation, ChainConfirmationMode.Off);
                modals.TryConfirm();
                Require(attempts == 1 && modals.IsShowingNativePrompt(duel, confirmation) &&
                        duel.HasEffectActivationAt(confirmation, ChainConfirmationMode.Off, 0, YgoConstants.LOCATION_GRAVE, 8),
                    "A blocked response dismissed a pending Graveyard trigger.");
                ready = true;
                modals.TryConfirm();
                Require(attempts == 2 && !modals.IsPromptOpen, "The pending trigger did not dismiss after acceptance.");
            }
            finally { UnityEngine.Object.DestroyImmediate(canvas); }
        }

        private static void VerifyChainOrderByTapping(DuelSession duel)
        {
            var canvas = new GameObject("Chain order test", typeof(Canvas));
            try
            {
                byte[] submitted = null;
                var modals = new DuelPromptModals(canvas.transform, null, bytes => submitted = bytes);
                var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SORT_CHAIN, playerId = 0, isChainSort = true };
                foreach (uint code in new uint[] { LadyLabrynth, 14087893, 14558127 })
                    prompt.candidates.Add(new SelectableCardItem { code = code, location = YgoConstants.LOCATION_GRAVE });

                modals.ShowPrompt(duel, prompt);
                void Pick(int position)
                {
                    var tile = modals.ModalRoot.GetComponentsInChildren<Transform>().Single(rt => rt.name == $"Sort_{position}");
                    tile.Find("Card/PickOrder").GetComponent<Button>().onClick.Invoke();
                }
                Pick(2);
                Pick(1);
                modals.ModalRoot.transform.Find("Win/Footer/Btn_순서 확정/B").GetComponent<Button>().onClick.Invoke();
                Require(submitted != null && submitted.SequenceEqual(new byte[] { 1, 2, 0 }),
                    "Tap ordering sent display indices instead of the native order permutation.");
            }
            finally { UnityEngine.Object.DestroyImmediate(canvas); }
        }

        private static void VerifyMobileChainTouchTargets(DuelSession duel)
        {
            var canvas = new GameObject("Mobile chain layout test", typeof(Canvas));
            try
            {
                canvas.GetComponent<Canvas>().scaleFactor = 1.5f;
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
                var modals = new DuelPromptModals(canvas.transform, null, null, mobileLayout: true);
                var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
                prompt.candidates.Add(new SelectableCardItem { code = LadyLabrynth, location = YgoConstants.LOCATION_HAND });
                modals.ShowPrompt(duel, prompt);
                var safe = (RectTransform)modals.ModalRoot.transform.Find("MobileSafeArea");
                safe.GetComponent<MobileSafeArea>().Reflow(new Rect(107f, 37f, 1382f, 823f), new Vector2Int(1600, 900));
                Canvas.ForceUpdateCanvases();
                modals.ShowPrompt(duel, prompt);
                Canvas.ForceUpdateCanvases();
                foreach (string path in new[] { "ChainWin/Footer/Pass/B", "ChainWin/Footer/Activate/B", "FieldView/B" })
                {
                    var target = (RectTransform)safe.Find(path);
                    float minimum = MobileSafeArea.MinimumTouchSize(target);
                    Require(target.rect.width >= minimum - .2f && target.rect.height >= minimum - .2f,
                        $"{path} does not meet the mobile hit target ({target.rect.size}, need {minimum}).");
                    var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(safe, target);
                    Require(safe.rect.Contains(bounds.min) && safe.rect.Contains(bounds.max),
                        $"{path} exceeds the safe area.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(canvas); }
        }

        private static void VerifyKoreanEffect(DuelSession duel)
        {
            const uint kaleidoHeart = 28226490;
            const uint legendaryBlackLusterSoldier = 70551291;
            const uint cyberseCodeMagician = 64866;
            const uint spyralVortex = 35699;
            ulong destruction = (ulong)spyralVortex << 20;
            ulong unknown = destruction | 14;
            string rulesText = CardDatabase.GetKoreanCardDescription(spyralVortex);
            string extracted = CardDatabase.ExtractIndexedEffectDescription(spyralVortex, 0);
            Require(CardDatabase.ResolveOptionDescription(unknown) == null &&
                    extracted != null && extracted.Length <= 60 && extracted != rulesText &&
                    CardDatabase.ResolveEffectDescription(unknown, spyralVortex) == extracted,
                "An unknown effect label did not fall back to its first concise Korean effect.");
            const uint chamber = 32600024;
            var second = CardDatabase.ExtractIndexedEffectDescription(chamber, 1);
            Require(CardDatabase.ResolveDescription((ulong)chamber << 20).StartsWith("덱에서", StringComparison.Ordinal) &&
                    second.StartsWith("이 카드를 패로 되돌리고", StringComparison.Ordinal) && second.Length <= 60 &&
                    CardDatabase.ResolveDescription(((ulong)chamber << 20) | 1) == second &&
                    CardDatabase.ExtractIndexedEffectDescription(chamber, 14) == null,
                "The printed card numbering did not resolve the corresponding zero-based effect.");
            Require(CardDatabase.ExtractIndexedEffectDescription(967928, 1) == "이 턴, 상대는 마법/함정 카드를 발동할 수 없다.",
                "The printed bullet-only card did not resolve its second effect.");
            Require(CardDatabase.ExtractIndexedEffectDescription(25311006, 2).Contains("컨트롤"),
                "A printed choice indexed after its parent effect did not resolve to the corresponding bullet.");
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

            const uint mirrorjade = 44146295, ashBlossom = 14558127;
            ulong mirrorjadeEffect = (ulong)mirrorjade << 20;
            view.Modals.ShowPrompt(duel, new DuelPrompt { messageType = YgoConstants.MSG_SELECT_EFFECTYN, playerId = 0,
                promptCardCode = mirrorjade, promptEffectDesc = mirrorjadeEffect });
            string mirrorjadePreview = view.Modals.ModalRoot.GetComponentsInChildren<Text>()
                .Single(label => label.name == "Effect").text;
            Require(mirrorjadePreview.Contains("알버스의 낙윤") && mirrorjadePreview.Contains("엑스트라 덱에서 묘지로") &&
                mirrorjadePreview.Contains("필드의 몬스터 1장을 골라 제외") && mirrorjadePreview.Contains("다음 턴") &&
                !mirrorjadePreview.Contains("엔드 페이즈") &&
                CardDatabase.ResolveEffectDescription(mirrorjadeEffect | 1, mirrorjade).Contains("엔드 페이즈"),
                "Mirrorjade's banish and End Phase effects must display their own conditions and results.");

            view.Modals.ShowPrompt(duel, new DuelPrompt { messageType = YgoConstants.MSG_SELECT_EFFECTYN, playerId = 0,
                promptCardCode = ashBlossom, promptEffectDesc = (ulong)ashBlossom << 20 });
            string ashPreview = view.Modals.ModalRoot.GetComponentsInChildren<Text>()
                .Single(label => label.name == "Effect").text;
            Require(ashPreview.Contains("패에서 버리고") && ashPreview.Contains("무효로 한다") &&
                ashPreview.Contains("덱에서 몬스터를 특수 소환하는 효과"),
                "A short chain label should be expanded with its matching printed effect and cost.");

            view.Modals.ShowPrompt(duel, new DuelPrompt { messageType = YgoConstants.MSG_SELECT_EFFECTYN, playerId = 0,
                promptCardCode = legendaryBlackLusterSoldier, promptEffectDesc = drawReplacement });
            effect = view.Modals.ModalRoot.GetComponentsInChildren<Text>()
                .Single(label => label.gameObject.name == "Effect");
            Require(effect.text == drawLabel, "The chain confirmation did not show the concise Korean draw-replacement effect.");

            view.Modals.ShowPrompt(duel, new DuelPrompt { messageType = YgoConstants.MSG_SELECT_EFFECTYN, playerId = 0,
                promptCardCode = spyralVortex, promptEffectDesc = destruction });
            Require(view.Modals.ModalRoot.GetComponentsInChildren<Text>().Single(label => label.name == "Effect").text ==
                    "자신 필드의 스파이랄 카드 1장과 상대 필드의 카드 최대 2장을 파괴한다",
                "SPYRAL Sleeper should show the selected destruction effect without repeating its entire rules text.");

            view.Modals.ShowPrompt(duel, new DuelPrompt { messageType = YgoConstants.MSG_SELECT_EFFECTYN, playerId = 0,
                promptCardCode = spyralVortex, promptEffectDesc = unknown });
            effect = view.Modals.ModalRoot.GetComponentsInChildren<Text>()
                .Single(label => label.gameObject.name == "Effect");
            Require(effect.text == extracted, "An unlocalized chain confirmation did not show a concise Korean effect.");
            view.Modals.ShowPrompt(duel, new DuelPrompt { messageType = YgoConstants.MSG_SELECT_YESNO, playerId = 0,
                promptDesc = unknown });
            Require(view.Modals.ModalRoot.GetComponentsInChildren<Text>().Single(label => label.name == "Question").text == extracted,
                "An untranslated English label escaped into the yes/no confirmation.");
            var options = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_OPTION, playerId = 0 };
            const ulong tripleTacticsTalent = 25311006UL << 20;
            options.options.Add(tripleTacticsTalent | 102);
            options.options.Add(tripleTacticsTalent | 100);
            options.options.Add(tripleTacticsTalent | 101);
            view.Modals.ShowPrompt(duel, options);
            var buttons = view.Modals.ModalRoot.GetComponentsInChildren<Button>();
            Button PrintedOption(int index) => buttons.Single(button => button.name == $"Opt_{index}");
            Require(PrintedOption(0).GetComponentInChildren<Text>().text.StartsWith("상대의 패를 확인", StringComparison.Ordinal) &&
                    PrintedOption(1).GetComponentInChildren<Text>().text == "자신은 2장 드로우한다." &&
                    PrintedOption(2).GetComponentInChildren<Text>().text.Contains("컨트롤") &&
                    !view.Modals.ModalRoot.GetComponentsInChildren<Text>().Any(label => label.name == "CardText"),
                "Unlabelled options did not show their printed bullets individually.");
            PrintedOption(1).onClick.Invoke();
            Require(view.Responses.SequenceEqual(new[] { 1 }),
                "Printed option labels altered the response index.");
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
                playerId = 0, promptDesc = YgoConstants.DESC_DIRECT_ATTACK, promptCardCode = LadyLabrynth });
            var attackChoice = view.Modals.ModalRoot.transform.Find("Win");
            Require(!view.Modals.IsChainPromptOpen && attackChoice.Find("Footer/Btn_직접 공격/B") != null &&
                    attackChoice.Find("Footer/Btn_몬스터 공격/B") != null,
                "An opponent-chosen direct attack was displayed as another effect activation.");

            const uint hugin = 55990317;
            view.Modals.ShowPrompt(duel, new DuelPrompt { messageType = YgoConstants.MSG_SELECT_EFFECTYN,
                playerId = 0, promptDesc = 96, promptCardCode = hugin });
            var replacement = view.Modals.ModalRoot.transform.Find("Win");
            Require(!view.Modals.IsChainPromptOpen &&
                    replacement.Find("Hdr/SubtitleBand/Sub").GetComponent<Text>().text == CardDatabase.GetName(hugin) &&
                    replacement.GetComponentsInChildren<Text>().Single(label => label.name == "Question").text ==
                        "이 카드의 대체 효과를 적용하시겠습니까?",
                "A destruction replacement did not show the translated question and source card.");
            Require(view.Modals.TryConfirm() && view.Responses.Last() == 1,
                "A yes/no replacement no longer accepted the engine's affirmative response.");
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

            const uint rulkallos = 84330567;
            Require(Option(rulkallos, 0).Contains("패 또는 필드의 앞면 표시 카드 중 티아라멘츠 카드 1장을 묘지로 보낸다") &&
                    Option(rulkallos, 1) == "융합 소환했던 이 카드를 묘지에서 특수 소환한다",
                "Rulkallos' two effect descriptions did not identify their different actions.");

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

            const uint thrust = 35269904, machinex = 46593546, blackCat = 24140059;
            Require(Option(thrust, 1) == "덱에서 세트하거나 패에 넣을 일반 마법·함정 카드를 선택하십시오." &&
                    Option(thrust, 2) == "세트하는 대신 선택한 카드를 패에 넣으시겠습니까?" &&
                    Option(machinex, 4) == "이 카드의 엑시즈 소재를 2개 제거한다" &&
                    Option(machinex, 5) == "자신 필드의 계약서 카드 1장을 파괴한다",
                "Thrust and Machinex must distinguish their native destinations and costs.");
            Require(Option(blackCat, 0) == "패에 넣는다" &&
                    CardDatabase.ResolveDescription(578) == "엑시즈 소재로 할 카드를 선택하십시오.",
                "A common confirmation or attach-material instruction lost its Korean wording.");

            foreach (uint zeus in new[] { 90448279u, 90448280u, 90448282u })
                Require(Option(zeus, 0) == "엑시즈 몬스터가 전투한 턴에 자신 필드의 엑시즈 몬스터 위에 겹쳐 엑시즈 소환한다" &&
                        CardDatabase.ResolveEffectDescription((ulong)zeus << 20, zeus) ==
                            "엑시즈 소재를 2개 제거해 필드의 다른 카드를 전부 묘지로 보낸다" &&
                        Option(zeus, 1) == "패·덱·엑스트라 덱의 카드 1장을 이 카드의 엑시즈 소재로 한다",
                    "The shared Zeus description or an alternate printing mixed its summoning and activated effects.");
            foreach (uint liebe in new[] { 26096328u, 26096329u })
                Require(Option(liebe, 0) == "랭크 10 기계족 엑시즈 몬스터 위에 겹쳐 엑시즈 소환한다" &&
                        CardDatabase.ResolveEffectDescription((ulong)liebe << 20, liebe) ==
                            "엑시즈 소재를 1개 제거해 공격력과 수비력을 2000 올린다",
                    "Liebe reused its summoning text in the activated effect.");
            Require(Option(80666118, 1)?.StartsWith("이 카드의 공격력 이하인", StringComparison.Ordinal) == true &&
                    Option(58600555, 0) == "엑시즈 소재를 1개 제거해 상대의 수비 표시 몬스터 1장을 덱으로 되돌린다" &&
                    Option(58600555, 2)?.EndsWith("겹쳐 엑시즈 소환한다", StringComparison.Ordinal) == true,
                "The stale database slots for Scarlight or Corebage overrode the script's descriptions.");
        }

        private static void VerifyKoreanPendulumReadouts(DuelSession duel)
        {
            const uint kepler = 11609969;
            var data = CardDatabase.Get(kepler);
            string rules = data?.desc;
            Require(rules?.Contains("[펜듈럼 효과]") == true, "The loaded card text kept the English Pendulum heading.");

            using var view = new ChainView();
            var field = new DuelFieldUI(null, view.Modals.ModalRoot.transform.parent, view.Modals, mobileLayout: false);
            try
            {
                field.BindSession(duel, null, null);
                field.InspectCard(kepler);
                var root = field.Root.transform;
                var inspect = root.Find("LeftInspector/CardDetails");
                Require(inspect.GetComponentsInChildren<Text>().Any(text => text.text == rules),
                    "The duel inspector lost the localized card rules.");
                var left = inspect.Find("Scales/Left/Number")?.GetComponent<Text>();
                var right = inspect.Find("Scales/Right/Number")?.GetComponent<Text>();
                Require(left?.text == data.lscale.ToString() && right?.text == data.rscale.ToString() &&
                    left.font == DuelTypography.NumberFont && right.font == DuelTypography.NumberFont && left.color != right.color,
                    "The inspector did not give both printed Pendulum scales their own numeric positions.");
                inspect.Find("ArtworkControls/Details/B").GetComponent<Button>().onClick.Invoke();
                var detail = root.Find("Overlay_CardZoom");
                Require(detail.gameObject.activeInHierarchy && detail.GetComponentsInChildren<Text>().Any(text => text.text == rules),
                    "The card detail window lost the localized card rules.");
                Require(detail.transform.Find("ZoomWin/Info/Scales/Left/Kind/Label")?.GetComponent<Text>().text == "왼쪽" &&
                    detail.transform.Find("ZoomWin/Info/Scales/Right/Kind/Label")?.GetComponent<Text>().text == "오른쪽",
                    "The detail window did not identify the two scale sides.");

                var card = CompositeCardView.Create(root, "RulesPreview", detailedOverlay: true);
                card.SetCard(kepler);
                Require(card.transform.Find("AspectBox/Rules/Text").GetComponent<Text>().text == rules,
                    "The card face kept the unlocalized text.");
            }
            finally
            {
                field.UnbindSession();
            }
        }

        private static void VerifyLightningStormOptions()
        {
            const uint storm = 14532163, sparkman = 20721928, jarOfGreed = 83968380;
            ulong description = (ulong)storm << 20;
            var player = Enumerable.Repeat(storm, 40).ToArray();
            var opponent = Enumerable.Repeat(sparkman, 20).Concat(Enumerable.Repeat(jarOfGreed, 20)).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            string[] labels =
            {
                "상대 필드의 공격 표시 몬스터를 전부 파괴한다",
                "상대 필드의 마법·함정 카드를 전부 파괴한다",
            };

            for (int choice = 0; choice < labels.Length; choice++)
            {
                using var duel = YgoProjectBuilder.StartSeedMatching("Lightning Storm options", player, empty, opponent, empty,
                    false, session =>
                    {
                        var hand = session.Players[session.AiPlayerIndex].hand;
                        return hand.Any(card => card.code == sparkman) && hand.Any(card => card.code == jarOfGreed);
                    });
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                int summon = duel.ActivePrompt.summonable.FindIndex(card => card.code == sparkman);
                Require(summon >= 0 && duel.ActivePrompt.playerId == duel.AiPlayerIndex, "Lightning Storm fixture could not summon its target.");
                duel.SubmitResponseInt(summon << 16);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                int set = duel.ActivePrompt.sSetable.FindIndex(card => card.code == jarOfGreed);
                Require(set >= 0, "Lightning Storm fixture could not set its target.");
                duel.SubmitResponseInt((set << 16) | 4);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);

                int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == storm);
                Require(activate >= 0 && duel.ActivePrompt.playerId == duel.HumanPlayerIndex, "Lightning Storm was not available.");
                duel.SubmitResponseInt((activate << 16) | 5);
                using var view = new ChainView();
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                {
                    if (prompt.messageType != YgoConstants.MSG_SELECT_OPTION) return null;
                    Require(prompt.options.SequenceEqual(new[] { description, description | 1 }),
                        "Lightning Storm did not offer both expected native choices.");
                    view.Modals.ShowPrompt(duel, prompt);
                    var buttons = view.Modals.ModalRoot.GetComponentsInChildren<Button>();
                    for (int i = 0; i < labels.Length; i++)
                    {
                        var option = buttons.Single(button => button.name == $"Opt_{i}");
                        Require(option.GetComponentInChildren<Text>().text == labels[i], "Lightning Storm displayed an ambiguous label.");
                    }
                    buttons.Single(button => button.name == $"Opt_{choice}").onClick.Invoke();
                    Require(view.Responses.SequenceEqual(new[] { choice }), "The localized option changed the native response index.");
                    return BitConverter.GetBytes(view.Responses[0]);
                });

                var target = duel.Players[duel.AiPlayerIndex];
                Require(view.Responses.Count == 1 && duel.RetryCount == 0 &&
                    target.grave.Any(card => card.code == (choice == 0 ? sparkman : jarOfGreed)) &&
                    target.mzone.Any(card => card?.code == sparkman) == (choice == 1) &&
                    target.szone.Any(card => card?.code == jarOfGreed) == (choice == 0),
                    "The Korean choice did not match the destroyed cards.");
            }
            Debug.Log("[VERIFY KOREAN NATIVE OPTIONS] Lightning Storm offered both Korean choices and resolved the chosen effect twice.");
        }

        private static void VerifyCyberRaiderOptions()
        {
            const uint raider = 39978267, sparkman = 20721928, equip = 56747793;
            ulong description = (ulong)raider << 20;
            var main = Enumerable.Repeat(raider, 13).Concat(Enumerable.Repeat(sparkman, 14))
                .Concat(Enumerable.Repeat(equip, 13)).ToArray();
            var opponent = Enumerable.Repeat(sparkman, 40).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            string[] labels = { "필드의 장착 카드 1장을 파괴한다", "필드의 장착 카드 1장을 이 카드에 장착한다" };

            for (int choice = 0; choice < labels.Length; choice++)
            {
                using var duel = YgoProjectBuilder.StartSeedMatching("Cyber Raider options", main, empty, opponent, empty,
                    true, session =>
                    {
                        var hand = session.Players[session.HumanPlayerIndex].hand;
                        return hand.Any(card => card.code == raider) && hand.Any(card => card.code == sparkman) &&
                            hand.Any(card => card.code == equip);
                    });
                void Summon(uint code)
                {
                    int index = duel.ActivePrompt.summonable.FindIndex(card => card.code == code);
                    Require(index >= 0, $"Cyber Raider fixture could not summon {code}.");
                    duel.SubmitResponseInt(index << 16);
                }

                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                Summon(sparkman);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == equip);
                Require(activate >= 0, "Cyber Raider fixture could not activate its Equip Spell.");
                duel.SubmitResponseInt((activate << 16) | 5);
                byte[] SelectCard(DuelPrompt prompt) =>
                    prompt.messageType == YgoConstants.MSG_SELECT_CARD ? ai.ComputeResponse(duel, prompt) : null;
                YgoProjectBuilder.DriveUntilIdle(duel, ai, SelectCard);
                var player = duel.Players[duel.HumanPlayerIndex];
                Require(player.mzone.Any(card => card?.code == sparkman && card.attack == 2400),
                    "The Equip Spell did not boost the original monster.");

                for (int turn = 0; turn < 2; turn++)
                {
                    duel.SubmitResponseInt(7);
                    YgoProjectBuilder.DriveUntilIdle(duel, ai);
                }
                Summon(raider);
                using var view = new ChainView();
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                {
                    if (prompt.messageType != YgoConstants.MSG_SELECT_OPTION) return SelectCard(prompt);
                    Require(prompt.options.SequenceEqual(new[] { description | 1, description | 2 }),
                        "Cyber Raider did not offer both native equipment actions.");
                    view.Modals.ShowPrompt(duel, prompt);
                    var buttons = view.Modals.ModalRoot.GetComponentsInChildren<Button>();
                    for (int i = 0; i < labels.Length; i++)
                    {
                        var option = buttons.Single(button => button.name == $"Opt_{i}");
                        Require(option.GetComponentInChildren<Text>().text == labels[i], "An equipment action lost its Korean label.");
                    }
                    buttons.Single(button => button.name == $"Opt_{choice}").onClick.Invoke();
                    Require(view.Responses.SequenceEqual(new[] { choice }), "The localized option changed the native response index.");
                    return BitConverter.GetBytes(view.Responses[0]);
                });
                int raiderAttack = CardDatabase.Get(raider).atk;
                Require(view.Responses.Count == 1 && duel.RetryCount == 0 &&
                    player.mzone.Any(card => card?.code == sparkman && card.attack == 1600) &&
                    player.mzone.Any(card => card?.code == raider && card.attack == raiderAttack + (choice == 0 ? 0 : 1600)) &&
                    player.grave.Any(card => card.code == equip) == (choice == 0),
                    "The translated choice did not destroy or move the Equip Spell as labeled.");
            }
            Debug.Log("[VERIFY KOREAN NATIVE OPTIONS] Cyber Raider destroyed and moved the Equip Spell using its respective Korean choices.");
        }

        private static void VerifyRunickTipOptions()
        {
            const uint tip = 31562086, fountain = 92107604, geri = 28373620, sparkman = 20721928;
            var main = Enumerable.Repeat(tip, 20).Concat(Enumerable.Repeat(fountain, 20)).ToArray();
            var opponent = Enumerable.Repeat(sparkman, 40).ToArray();
            var ai = new TacticalAI();
            string[] labels = {
                "덱에서 다른 루닉 카드 1장을 패에 넣은 뒤 상대 덱 위에서 카드 1장을 제외한다",
                "엑스트라 덱에서 루닉 몬스터 1장을 엑스트라 몬스터 존에 특수 소환한다",
            };

            for (int choice = 0; choice < labels.Length; choice++)
            {
                using var duel = YgoProjectBuilder.StartSeedMatching("Runick Tip options", main, new[] { geri },
                    opponent, Array.Empty<uint>(), true,
                    session =>
                    {
                        YgoProjectBuilder.DriveUntilIdle(session, ai);
                        return session.ActivePrompt?.activatable.Exists(card => card.code == tip) == true;
                    });
                var player = duel.Players[duel.HumanPlayerIndex];
                int fountains = player.hand.Count(card => card.code == fountain);
                int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == tip);
                duel.SubmitResponseInt((activate << 16) | 5);
                using var view = new ChainView();
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                {
                    if (prompt.messageType == YgoConstants.MSG_SELECT_CARD) return ai.ComputeResponse(duel, prompt);
                    if (prompt.messageType != YgoConstants.MSG_SELECT_OPTION) return null;
                    Require(prompt.options.SequenceEqual(new[] { (ulong)tip << 20, ((ulong)tip << 20) | 1 }),
                        "Runick Tip did not offer both branches.");
                    view.Modals.ShowPrompt(duel, prompt);
                    var buttons = view.Modals.ModalRoot.GetComponentsInChildren<Button>();
                    for (int i = 0; i < labels.Length; i++)
                        Require(buttons.Single(button => button.name == $"Opt_{i}").GetComponentInChildren<Text>().text ==
                            labels[i], "Runick Tip did not display the processing and destination of each effect.");
                    buttons.Single(button => button.name == $"Opt_{choice}").onClick.Invoke();
                    Require(view.Responses.SequenceEqual(new[] { choice }), "The Runick choice submitted the wrong index.");
                    return BitConverter.GetBytes(view.Responses[0]);
                });
                Require(view.Responses.Count == 1 && duel.RetryCount == 0 &&
                        player.hand.Count(card => card.code == fountain) == fountains + (choice == 0 ? 1 : 0) &&
                        duel.Players[duel.AiPlayerIndex].banished.Count == (choice == 0 ? 1 : 0) &&
                        player.mzone.Skip(5).Any(card => card?.code == geri) == (choice == 1),
                    "The Runick option did not search and banish or summon to an Extra Monster Zone as labeled.");
            }
            Debug.Log("[VERIFY KOREAN NATIVE OPTIONS] Runick Tip presented and resolved its distinct Korean search and summon options.");
        }

        private static void VerifyDreadnoughtServantConfirmation()
        {
            const uint servant = 98931282, umi = 22702055, polymerization = 24094653, sparkman = 20721928;
            var main = Enumerable.Repeat(servant, 13).Concat(Enumerable.Repeat(umi, 13))
                .Concat(Enumerable.Repeat(polymerization, 14)).ToArray();
            var opponent = Enumerable.Repeat(sparkman, 40).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            foreach (bool accept in new[] { true, false })
            {
                using var duel = YgoProjectBuilder.StartSeedMatching("Dreadnought Servant", main, empty, opponent, empty,
                    true, session => session.Players[session.HumanPlayerIndex].hand.Exists(card => card.code == servant) &&
                        session.ActivePrompt?.activatable.Exists(card => card.code == umi) == true);
                var player = duel.Players[duel.HumanPlayerIndex];
                int fusions = player.hand.Count(card => card.code == polymerization);
                void Activate(uint code)
                {
                    int index = duel.ActivePrompt.activatable.FindIndex(card => card.code == code);
                    Require(index >= 0, $"Dreadnought Servant fixture could not activate {code}.");
                    duel.SubmitResponseInt((index << 16) | 5);
                }
                Activate(umi);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                Activate(servant);
                using var view = new ChainView();
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                {
                    if (prompt.messageType == YgoConstants.MSG_SELECT_CARD) return ai.ComputeResponse(duel, prompt);
                    if (prompt.messageType != YgoConstants.MSG_SELECT_YESNO) return null;
                    Require(prompt.promptDesc == (((ulong)servant << 20) | 2),
                        "Unexpected Servant confirmation description.");
                    view.Modals.ShowPrompt(duel, prompt);
                    Require(view.Modals.ModalRoot.GetComponentsInChildren<Text>().Single(label => label.name == "Question")
                        .text == "자신 필드의 카드 1장을 파괴하고 덱에서 융합 1장을 패에 넣으시겠습니까?",
                        "A missing native label was replaced with an ambiguous full rules text.");
                    bool answered = accept ? view.Modals.TryConfirm() : view.Modals.TryCancelOrPass();
                    Require(answered && view.Responses.SequenceEqual(new[] { accept ? 1 : 0 }),
                        "The yes/no response changed with the Korean description.");
                    return BitConverter.GetBytes(view.Responses[0]);
                });
                Require(view.Responses.Count == 1 && duel.RetryCount == 0 &&
                        player.hand.Count(card => card.code == polymerization) == fusions + (accept ? 1 : 0) &&
                        player.grave.Any(card => card.code == servant || card.code == umi) == accept,
                    "The displayed confirmation did not match the optional destruction and search.");
            }
            Debug.Log("[VERIFY KOREAN NATIVE OPTIONS] Dreadnought Servant showed its Korean confirmation and resolved both responses.");
        }

        private static void VerifyGagagaLevelInstruction()
        {
            const uint magician = 26082117, sparkman = 20721928, axe = 40619825;
            var main = Enumerable.Repeat(magician, 20).Concat(Enumerable.Repeat(axe, 20)).ToArray();
            var opponent = Enumerable.Repeat(sparkman, 40).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Gagaga level instruction", main, empty, opponent, empty,
                true, session => session.ActivePrompt?.summonable.Exists(card => card.code == magician) == true &&
                    session.Players[session.HumanPlayerIndex].hand.Exists(card => card.code == axe));
            int summon = duel.ActivePrompt.summonable.FindIndex(card => card.code == magician);
            duel.SubmitResponseInt(summon << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            int equip = duel.ActivePrompt.activatable.FindIndex(card => card.code == axe);
            Require(equip >= 0, "The level instruction fixture could not equip the active monster.");
            duel.SubmitResponseInt((equip << 16) | 5);
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD
                ? DuelSession.PackCardSelection(prompt.candidates.FindIndex(card => card.code == magician)) : null);
            int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == magician);
            Require(activate >= 0, "Gagaga Magician had no level-changing action.");
            duel.SubmitResponseInt((activate << 16) | 5);
            using var view = new ChainView();
            var field = view.AttachField(duel);
            var inspector = field.Root.transform.Find("LeftInspector/CardDetails");
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                if (prompt.messageType != YgoConstants.MSG_ANNOUNCE_NUMBER) return null;
                int index = prompt.options.IndexOf(8);
                Require(index >= 0 && prompt.SelectionHint == (((ulong)magician << 20) | 1),
                    "Gagaga Magician did not ask for the expected level.");
                view.Modals.ShowPrompt(duel, prompt);
                var window = view.Modals.ModalRoot.transform.Find("Win");
                Require(window.Find("Hdr/TitleBand/Title").GetComponent<Text>().text ==
                        "1부터 8까지 중 이 카드에 적용할 레벨을 선택하세요",
                    "The number list did not display the action indicated by the native hint.");
                Require(inspector.Find("CombatStats/Attack/Digits/Value").GetComponent<Text>().text == "2500",
                    "The number choice inspected the printed copy instead of the live equipped monster.");
                window.GetComponentsInChildren<Button>().Single(button => button.name == $"Opt_{index}").onClick.Invoke();
                Require(view.Responses.SequenceEqual(new[] { index }), "The numeric selection sent the number instead of its index.");
                return BitConverter.GetBytes(view.Responses[0]);
            });
            Require(view.Responses.Count == 1 && duel.RetryCount == 0 &&
                    duel.Players[duel.HumanPlayerIndex].mzone.Any(card => card?.code == magician && card.level == 8),
                "Gagaga Magician did not apply the selected level.");
            field.Refresh();
            Require(inspector.Find("Stats/Rating/Number").GetComponent<Text>().text == "8" &&
                    inspector.Find("CombatStats/Attack/Digits/Value").GetComponent<Text>().text == "2500",
                "The inspector did not keep the resolving monster after the number choice.");
            var monster = duel.Players[duel.HumanPlayerIndex].mzone.Single(card => card?.code == magician);
            var slot = field.Root.transform.Find($"DuelBoard/Zone_{monster.sequence + 2}_3");
            var fieldRating = slot.Find("CardReadout/Labels/Rating");
            var current = fieldRating.Find("Value").GetComponent<Text>();
            fieldRating.GetComponent<DuelReadoutPulse>()?.Advance(1f);
            Require(current.text == "8" && current.color == DuelStyle.IncreasedValue &&
                    fieldRating.Find("Symbol").GetComponent<Image>().color == DuelStyle.Gold &&
                    inspector.Find("Stats/Rating/Number").GetComponent<Text>().color == current.color,
                "The increased level lost its distinct appearance after the number animation.");
            inspector.Find("ArtworkControls/Details/B").GetComponent<Button>().onClick.Invoke();
            var zoom = field.Root.transform.Find("Overlay_CardZoom") ?? field.Root.transform.parent.Find("Overlay_CardZoom");
            var rating = zoom.Find("ZoomWin/Info/Stats/Rating");
            var printed = rating.Find("Printed/Label")?.GetComponent<Text>();
            var enlarged = rating.Find("Number").GetComponent<Text>();
            Require(printed != null && printed.gameObject.activeInHierarchy && printed.text == "기본 4" &&
                    enlarged.text == "8" && enlarged.color == current.color,
                "The detail view did not show both the applied and printed levels.");
            Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD,
                "The level fixture did not return to Main Phase.");
            duel.SubmitResponseInt(7);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            field.Refresh();
            Require(duel.RetryCount == 0 && current.text == "4" && current.color == DuelStyle.Gold &&
                    enlarged.text == "4" && enlarged.color == DuelStyle.Gold && !printed.gameObject.activeInHierarchy,
                "The expired level modifier remained visible after the turn ended.");
            zoom.Find("ZoomWin/Hdr/Close/B").GetComponent<Button>().onClick.Invoke();
            Debug.Log("[VERIFY KOREAN NATIVE OPTIONS] Gagaga Magician displayed its level change and restored the printed level.");
        }

        private static void VerifyCardTrooperCount()
        {
            const uint trooper = 85087012, sparkman = 20721928;
            var main = Enumerable.Repeat(trooper, 40).ToArray();
            var opponent = Enumerable.Repeat(sparkman, 40).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Card Trooper count", main, empty, opponent, empty,
                true, session => session.ActivePrompt?.summonable.Exists(card => card.code == trooper) == true);
            duel.SubmitResponseInt(duel.ActivePrompt.summonable.FindIndex(card => card.code == trooper) << 16);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == trooper);
            Require(activate >= 0, "Card Trooper had no effect to send cards to the graveyard.");
            duel.SubmitResponseInt((activate << 16) | 5);
            using var view = new ChainView();
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                if (prompt.messageType != YgoConstants.MSG_ANNOUNCE_NUMBER) return null;
                int index = prompt.options.IndexOf(2);
                Require(index >= 0 && prompt.SelectionHint == (((ulong)trooper << 20) | 2),
                    "Card Trooper did not issue its own count instruction.");
                view.Modals.ShowPrompt(duel, prompt);
                var window = view.Modals.ModalRoot.transform.Find("Win");
                Require(window.Find("Hdr/TitleBand/Title").GetComponent<Text>().text == "덱 위에서 몇 장을 묘지로 보낼지 고르세요",
                    "The localized count prompt did not explain what the number does.");
                window.GetComponentsInChildren<Button>().Single(button => button.name == $"Opt_{index}").onClick.Invoke();
                Require(view.Responses.SequenceEqual(new[] { index }), "The count prompt submitted the number instead of its index.");
                return BitConverter.GetBytes(view.Responses[0]);
            });
            var player = duel.Players[duel.HumanPlayerIndex];
            Require(duel.RetryCount == 0 && view.Responses.Count == 1 && player.grave.Count == 2 &&
                    player.mzone.Any(card => card?.code == trooper && card.attack == 1400),
                "The chosen count did not send two cards and add 1000 ATK.");
            Debug.Log("[VERIFY KOREAN NATIVE OPTIONS] Card Trooper explained the count and applied the corresponding attack increase.");
        }

        private static void VerifyDragonShrineConfirmation()
        {
            const uint shrine = 41620959, dragon = 89631139, sparkman = 20721928;
            var main = Enumerable.Repeat(shrine, 13).Concat(Enumerable.Repeat(dragon, 27)).ToArray();
            var opponent = Enumerable.Repeat(sparkman, 40).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            foreach (bool accept in new[] { false, true })
            {
                using var duel = YgoProjectBuilder.StartSeedMatching("Dragon Shrine confirmation", main, empty, opponent, empty,
                    true, session => session.ActivePrompt?.activatable.Exists(card => card.code == shrine) == true);
                int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == shrine);
                duel.SubmitResponseInt((activate << 16) | 5);
                using var view = new ChainView();
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                {
                    if (prompt.messageType == YgoConstants.MSG_SELECT_CARD) return ai.ComputeResponse(duel, prompt);
                    if (prompt.messageType != YgoConstants.MSG_SELECT_YESNO) return null;
                    Require(prompt.promptDesc == ((ulong)shrine << 20), "Unexpected second Dragon Shrine decision.");
                    view.Modals.ShowPrompt(duel, prompt);
                    var question = view.Modals.ModalRoot.GetComponentsInChildren<Text>().Single(label => label.name == "Question");
                    Require(question.text == "덱에서 드래곤족 몬스터 1장을 더 묘지로 보내시겠습니까?",
                        "Dragon Shrine did not explain the optional second send.");
                    bool answered = accept ? view.Modals.TryConfirm() : view.Modals.TryCancelOrPass();
                    Require(answered && view.Responses.SequenceEqual(new[] { accept ? 1 : 0 }),
                        "Dragon Shrine submitted the wrong yes/no response.");
                    return BitConverter.GetBytes(view.Responses[0]);
                });
                Require(view.Responses.Count == 1 && duel.RetryCount == 0 &&
                        duel.Players[duel.HumanPlayerIndex].grave.Count(card => card.code == dragon) == (accept ? 2 : 1),
                    "Dragon Shrine did not resolve the chosen yes/no action.");
            }
            Debug.Log("[VERIFY KOREAN NATIVE OPTIONS] Dragon Shrine showed the optional send in Korean and resolved both responses.");
        }

        private static void VerifyPerlereinoConfirmation()
        {
            const uint fieldSpell = 77103950, reinoheart = 73956664, sparkman = 20721928;
            var main = Enumerable.Repeat(fieldSpell, 14).Concat(Enumerable.Repeat(reinoheart, 26)).ToArray();
            var opponent = Enumerable.Repeat(sparkman, 40).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            foreach (bool accept in new[] { false, true })
            {
                using var duel = YgoProjectBuilder.StartSeedMatching("Perlereino confirmation", main, empty, opponent, empty,
                    true, session => session.ActivePrompt?.activatable.Exists(card => card.code == fieldSpell) == true);
                var player = duel.Players[duel.HumanPlayerIndex];
                int before = player.hand.Count(card => card.code == reinoheart);
                int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == fieldSpell);
                duel.SubmitResponseInt((activate << 16) | 5);
                using var view = new ChainView();
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                {
                    if (prompt.messageType == YgoConstants.MSG_SELECT_CARD) return ai.ComputeResponse(duel, prompt);
                    if (prompt.messageType != YgoConstants.MSG_SELECT_YESNO) return null;
                    Require(prompt.promptDesc == ((ulong)fieldSpell << 20), "Unexpected Perlereino decision.");
                    view.Modals.ShowPrompt(duel, prompt);
                    var question = view.Modals.ModalRoot.GetComponentsInChildren<Text>().Single(label => label.name == "Question");
                    Require(question.text == "덱에서 티아라멘츠 몬스터 또는 비서스＝스타프로스트 1장을 패에 넣으시겠습니까?",
                        "Perlereino did not explain the optional search.");
                    Require(accept ? view.Modals.TryConfirm() : view.Modals.TryCancelOrPass(), "Perlereino could not answer.");
                    Require(view.Responses.SequenceEqual(new[] { accept ? 1 : 0 }), "Perlereino sent the wrong answer.");
                    return BitConverter.GetBytes(view.Responses[0]);
                });
                Require(view.Responses.Count == 1 && duel.RetryCount == 0 &&
                        player.hand.Count(card => card.code == reinoheart) == before + (accept ? 1 : 0),
                    "Perlereino did not resolve the localized search confirmation.");
            }
            Debug.Log("[VERIFY KOREAN NATIVE OPTIONS] Perlereino showed the Korean search confirmation and resolved both responses.");
        }

        private static void VerifyPaidraSelectionHint()
        {
            const uint paidra = 39931513, summoning = 30336082, sparkman = 20721928;
            var main = Enumerable.Repeat(paidra, 20).Concat(Enumerable.Repeat(summoning, 20)).ToArray();
            var opponent = Enumerable.Repeat(sparkman, 40).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Paidra selection hint", main, empty, opponent, empty,
                true, session => session.ActivePrompt?.summonable.Exists(card => card.code == paidra) == true);
            var player = duel.Players[duel.HumanPlayerIndex];
            int before = player.hand.Count(card => card.code == summoning);
            duel.SubmitResponseInt(duel.ActivePrompt.summonable.FindIndex(card => card.code == paidra) << 16);
            using var view = new ChainView();
            bool sawHint = false;
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == paidra)
                    return BitConverter.GetBytes(1);
                if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
                    return BitConverter.GetBytes(prompt.candidates.FindIndex(card => card.code == paidra));
                if (prompt.messageType != YgoConstants.MSG_SELECT_CARD) return null;

                Require(prompt.SelectionHint == (((ulong)paidra << 20) | 2), "Unexpected Paidra selection hint.");
                view.Modals.ShowPrompt(duel, prompt);
                var title = view.Modals.ModalRoot.GetComponentsInChildren<Text>().First(label => label.name == "Title");
                Require(title.text == "덱에서 패에 넣거나 세트할 찬환 마법·함정 카드 1장을 선택하세요.",
                    "Paidra did not explain the destinations of the selected card in Korean.");
                sawHint = true;
                return ai.ComputeResponse(duel, prompt);
            });
            Require(sawHint && duel.RetryCount == 0 && player.hand.Count(card => card.code == summoning) == before + 1,
                "Paidra did not resolve the selection described by its Korean hint.");
            Debug.Log("[VERIFY KOREAN NATIVE OPTIONS] Paidra displayed the Korean selection hint and added the chosen card to hand.");
        }

        private static void VerifyChickenGameOptions()
        {
            const uint chicken = 67616300, sparkman = 20721928;
            var main = Enumerable.Repeat(chicken, 40).ToArray();
            var opponent = Enumerable.Repeat(sparkman, 40).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            using var duel = YgoProjectBuilder.StartSeedMatching("Chicken Game options", main, empty, opponent, empty,
                true, session => session.ActivePrompt?.activatable.Exists(card => card.code == chicken) == true);
            int place = duel.ActivePrompt.activatable.FindIndex(card => card.code == chicken);
            duel.SubmitResponseInt((place << 16) | 5);
            YgoProjectBuilder.DriveUntilIdle(duel, ai);
            int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == chicken && card.location != YgoConstants.LOCATION_HAND);
            Require(activate >= 0, "Chicken Game did not offer its on-field effect.");
            duel.SubmitResponseInt((activate << 16) | 5);
            using var view = new ChainView();
            YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
            {
                if (prompt.messageType != YgoConstants.MSG_SELECT_OPTION) return null;
                ulong effect = (ulong)chicken << 20;
                Require(prompt.options.SequenceEqual(new[] { effect | 1, effect | 2, effect | 3 }),
                    "Chicken Game did not offer its three expected options.");
                view.Modals.ShowPrompt(duel, prompt);
                Canvas.ForceUpdateCanvases();
                var panel = (RectTransform)view.Modals.ModalRoot.transform.Find("Win");
                var scroll = panel.Find("Body/Scroll").GetComponent<ScrollRect>();
                Require(scroll.content.rect.height <= scroll.viewport.rect.height,
                    "The third option required scrolling on a three-choice prompt.");
                var fieldViewTab = (RectTransform)view.Modals.FieldViewControl.transform.parent;
                Require(fieldViewTab.anchorMin.y >= panel.anchorMax.y,
                    "The field-view tab covered the option rows after expanding the panel.");
                var buttons = view.Modals.ModalRoot.GetComponentsInChildren<Button>();
                string[] labels = { "카드를 1장 드로우한다", "이 카드를 파괴한다", "상대가 1000 LP를 회복한다" };
                for (int index = 0; index < labels.Length; index++)
                    Require(buttons.Single(button => button.name == $"Opt_{index}").GetComponentInChildren<Text>().text == labels[index],
                        "Chicken Game did not show a distinct Korean description for every option.");
                buttons.Single(button => button.name == "Opt_2").onClick.Invoke();
                Require(view.Responses.SequenceEqual(new[] { 2 }), "The third effect did not submit its native index.");
                return BitConverter.GetBytes(view.Responses[0]);
            });
            Require(view.Responses.Count == 1 && duel.RetryCount == 0 &&
                    duel.Players[duel.HumanPlayerIndex].lp == 7000 && duel.Players[duel.AiPlayerIndex].lp == 9000,
                "Chicken Game did not charge 1000 LP and restore 1000 LP to the opponent.");
            Debug.Log("[VERIFY KOREAN NATIVE OPTIONS] Chicken Game displayed all three effects and applied the selected LP recovery.");
        }

        private static void VerifyRamificationsOptions()
        {
            const uint spell = 81615450, gorgon = 43426903, spawn = 21179143, sparkman = 20721928;
            var main = Enumerable.Repeat(spell, 8).Concat(Enumerable.Repeat(gorgon, 8))
                .Concat(Enumerable.Repeat(spawn, 8)).Concat(Enumerable.Repeat(sparkman, 16)).ToArray();
            string[] labels =
            {
                "덱에서 레프티레스 몬스터 1장을 패에 넣는다",
                "덱에서 레프티레스 래미피케이션 이외의 레프티레스 마법·함정 카드 1장을 패에 넣는다",
                "상대 필드의 몬스터 1장의 공격력을 0으로 한다",
            };
            ulong description = (ulong)spell << 20;
            ulong[] effects = { description, description | 1, description | 2 };
            var ai = new TacticalAI();
            for (int first = 0; first < effects.Length; first++)
            {
                int second = (first + 1) % effects.Length;
                using var duel = StartAgainstSparkman("Reptilianne Ramifications", main, ai,
                    player => player.hand.Exists(card => card.code == spell) && player.hand.Exists(card => card.code == sparkman));
                var player = duel.Players[duel.HumanPlayerIndex];
                int monsters = player.hand.Count(card => card.code == gorgon);
                int spells = player.hand.Count(card => card.code == spawn);
                int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == spell);
                Require(activate >= 0, "Ramifications could not offer its two effect selections.");
                duel.SubmitResponseInt((activate << 16) | 5);

                using var view = new ChainView();
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                {
                    if (prompt.messageType == YgoConstants.MSG_SELECT_CARD)
                    {
                        int discard = prompt.candidates.FindIndex(card => card.location == YgoConstants.LOCATION_HAND && card.code == sparkman);
                        return DuelSession.PackCardSelection(discard >= 0 ? discard : 0);
                    }
                    if (prompt.messageType != YgoConstants.MSG_SELECT_OPTION) return null;
                    bool firstChoice = view.Responses.Count == 0;
                    var offered = firstChoice ? effects : effects.Where(effect => effect != effects[first]).ToArray();
                    Require(prompt.options.SequenceEqual(offered), "Ramifications lost or reordered a native option.");
                    view.Modals.ShowPrompt(duel, prompt);
                    var buttons = view.Modals.ModalRoot.GetComponentsInChildren<Button>();
                    for (int i = 0; i < offered.Length; i++)
                    {
                        int effectIndex = Array.IndexOf(effects, offered[i]);
                        Require(buttons.Single(button => button.name == $"Opt_{i}").GetComponentInChildren<Text>().text == labels[effectIndex],
                            "A Ramifications option did not display its distinct Korean action.");
                    }
                    int choice = Array.IndexOf(offered, effects[firstChoice ? first : second]);
                    buttons.Single(button => button.name == $"Opt_{choice}").onClick.Invoke();
                    Require(view.Responses.Last() == choice, "A localized follow-up changed the native option index.");
                    return BitConverter.GetBytes(choice);
                });

                var opponent = duel.Players[duel.AiPlayerIndex].mzone.Single(card => card?.code == sparkman);
                Require(view.Responses.Count == 2 && duel.RetryCount == 0 &&
                        player.hand.Count(card => card.code == gorgon) == monsters + (first == 0 || second == 0 ? 1 : 0) &&
                        player.hand.Count(card => card.code == spawn) == spells + (first == 1 || second == 1 ? 1 : 0) &&
                        opponent.attack == (first == 2 || second == 2 ? 0 : 1600),
                    "The two selected Korean effects did not match the cards added or the changed attack.");
            }
            Debug.Log("[VERIFY KOREAN NATIVE OPTIONS] Reptilianne Ramifications presented all three Korean effects and resolved each pair.");
        }

        private static void VerifyOddEyesDamageConfirmation()
        {
            const uint oddEyes = 16178681, seaDragoons = 82114013, sparkman = 20721928;
            var main = Enumerable.Repeat(oddEyes, 8).Concat(Enumerable.Repeat(seaDragoons, 32)).ToArray();
            var ai = new TacticalAI();
            foreach (bool accept in new[] { true, false })
            {
                using var duel = StartAgainstSparkman("Odd-Eyes damage confirmation", main, ai,
                    player => player.hand.Exists(card => card.code == oddEyes) && player.hand.Exists(card => card.code == seaDragoons));
                int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == oddEyes);
                Require(activate >= 0, "Odd-Eyes could not be placed in the Pendulum Zone.");
                duel.SubmitResponseInt((activate << 16) | 5);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                int summon = duel.ActivePrompt.summonable.FindIndex(card => card.code == seaDragoons);
                Require(summon >= 0, "A Pendulum monster could not be summoned for the damage confirmation.");
                duel.SubmitResponseInt(summon << 16);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                duel.SubmitResponseInt(6);
                Require(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD &&
                        duel.ActivePrompt.attackable.Count == 1, "The Pendulum monster could not declare an attack.");
                duel.SubmitResponseInt(1);

                using var view = new ChainView();
                var field = view.AttachField(duel);
                YgoProjectBuilder.DriveUntilIdle(duel, ai, prompt =>
                {
                    if (prompt.IsAttackTargetSelection)
                    {
                        Require(prompt.candidates.Single().code == sparkman, "Odd-Eyes fixture selected an unexpected defender.");
                        return DuelSession.PackCardSelection(0);
                    }
                    if (prompt.messageType != YgoConstants.MSG_SELECT_EFFECTYN || prompt.promptCardCode != oddEyes)
                        return null;
                    Require(prompt.promptDesc == (((ulong)oddEyes << 20) | 1) && !prompt.IsTriggerActivationConfirmation,
                        "A continuous effect's rule question was classified as a trigger.");
                    view.Modals.ShowPrompt(duel, prompt);
                    Require(!view.Modals.IsChainPromptOpen &&
                            view.Modals.ModalRoot.GetComponentsInChildren<Text>().Single(label => label.name == "Question").text ==
                            "이번 전투에서 자신이 받을 전투 데미지를 0으로 하시겠습니까?",
                        "The engine's damage prevention question did not appear as a Korean confirmation.");
                    var details = field.Root.transform.Find("LeftInspector/CardDetails");
                    Require(details.Find("Stats/Identity/Text").GetComponent<Text>().text == "펜듈럼 마법" &&
                        details.Find("Stats/Rating/Number").GetComponent<Text>().text == "4" &&
                        details.Find("Stats/Rating/Symbol").GetComponent<Image>().sprite == DuelStatSymbols.Rating(DuelRatingKind.Scale) &&
                        !details.Find("CombatStats").gameObject.activeSelf,
                        "The yes/no confirmation lost the actual Pendulum Zone source and showed its printed monster stats.");
                    bool submitted = accept ? view.Modals.TryConfirm() : view.Modals.TryCancelOrPass();
                    Require(submitted && view.Responses.SequenceEqual(new[] { accept ? 1 : 0 }),
                        "The Korean confirmation changed the native yes/no response.");
                    return BitConverter.GetBytes(view.Responses[0]);
                });
                Require(view.Responses.Count == 1 && duel.RetryCount == 0 &&
                        duel.Players[duel.HumanPlayerIndex].lp == (accept ? 8000 : 6600),
                    "The damage received did not match the selected answer.");
            }
            Debug.Log("[VERIFY KOREAN NATIVE OPTIONS] Odd-Eyes displayed its Korean damage question and verified both LP results.");
        }

        private static DuelSession StartAgainstSparkman(string label, uint[] main, TacticalAI ai,
            Func<PlayerFieldSnapshot, bool> opening)
        {
            const uint sparkman = 20721928;
            var empty = Array.Empty<uint>();
            var opponent = Enumerable.Repeat(sparkman, 40).ToArray();
            var duel = YgoProjectBuilder.StartSeedMatching(label, main, empty, opponent, empty, false,
                session => opening(session.Players[session.HumanPlayerIndex]));
            try
            {
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                Require(duel.ActivePrompt?.playerId == duel.AiPlayerIndex && duel.ActivePrompt.summonable[0].code == sparkman,
                    $"The opponent could not summon Sparkman for {label}.");
                duel.SubmitResponseInt(0);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                Require(duel.ActivePrompt?.playerId == duel.HumanPlayerIndex, $"The player did not get a turn for {label}.");
                return duel;
            }
            catch
            {
                duel.Dispose();
                throw;
            }
        }

        private static void VerifyTrapMonsterPosition()
        {
            const uint apophis = 28649820, statue = 23626223, sparkman = 20721928;
            const uint attack = YgoConstants.POS_FACEUP_ATTACK, defense = YgoConstants.POS_FACEUP_DEFENSE;
            var enemy = Enumerable.Repeat(sparkman, 40).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            foreach (var (code, level, atk, def, chosenPosition) in new[]
            {
                (apophis, 4u, 1600, 1800, attack), (statue, 7u, 0, 2500, defense),
            })
            {
                var main = Enumerable.Repeat(code, 40).ToArray();
                using var duel = YgoProjectBuilder.StartSeedMatching("Trap Monster position", main, empty, enemy, empty, true,
                    session => session.ActivePrompt?.sSetable.Exists(card => card.code == code) == true);
                duel.SubmitResponseInt(4);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                int activate = duel.ActivePrompt?.activatable.FindIndex(card => card.code == code) ?? -1;
                Require(activate >= 0, "The Trap Monster could not activate on the next turn.");
                duel.SubmitResponseInt((activate << 16) | 5);
                for (int i = 0; i < 12 && duel.ActivePrompt?.messageType != YgoConstants.MSG_SELECT_POSITION; i++)
                {
                    var pending = duel.ActivePrompt;
                    if (pending == null) duel.StepUntilPrompt();
                    else duel.SubmitResponse(ai.ComputeResponse(duel, pending));
                }
                var prompt = duel.ActivePrompt;
                Require(prompt?.messageType == YgoConstants.MSG_SELECT_POSITION && prompt.promptCardCode == code &&
                    prompt.allowedPositions == (attack | defense) && duel.RetryCount == 0,
                    "The Trap Monster did not offer both face-up positions.");
                var source = duel.SelectionChainLink;
                var registered = source == null ? null : duel.GetVisibleFieldCard(code, source.triggerPlayer, source.triggerLoc, source.triggerSeq);
                Require(registered != null && registered.level == level && registered.attack == atk && registered.defense == def,
                    "The engine did not publish the source's registered Trap Monster stats before the choice.");
                using var view = new ChainView();
                var field = view.AttachField(duel);
                view.Modals.ShowPrompt(duel, prompt);
                var window = view.Modals.ModalRoot.transform.Find("Win");
                Require(window.Find("Hdr/SubtitleBand/Sub").GetComponent<Text>().text == CardDatabase.GetName(code) &&
                    window.Find("Hdr/SubtitleBand/Rating/Number")?.GetComponent<Text>().text == level.ToString(),
                    "The position picker did not display the Trap Monster's level.");
                void CheckCombat(Transform combat)
                {
                    Require(combat != null && combat.gameObject.activeInHierarchy &&
                        combat.Find("Attack/Digits/Value").GetComponent<Text>().text == atk.ToString() &&
                        combat.Find("Defense/Digits/Value").GetComponent<Text>().text == def.ToString(),
                        "A Trap Monster preview did not display its registered combat values.");
                    Require(combat.Find("Attack/Change/Delta")?.gameObject.activeSelf != true &&
                        combat.Find("Defense/Change/Delta")?.gameObject.activeSelf != true,
                        "Trap Monster registration was shown as a combat modifier.");
                }
                foreach (uint position in new[] { attack, defense })
                    CheckCombat(window.Find($"Body/Pos_{position}/CombatStats"));
                var inspector = field.Root.transform.Find("LeftInspector/CardDetails");
                var portrait = field.Root.transform.Find("LeftInspector/CardPreview/Card").GetComponent<CompositeCardView>();
                Require(portrait?.ArtworkSprite != null && portrait.CurrentCode == code,
                    "The selected Trap Monster's inspector did not load its artwork on the first render.");
                Require(inspector.Find("Stats/Rating/Number").GetComponent<Text>().text == level.ToString() &&
                    inspector.Find("Stats/Info/Text").GetComponent<Text>().text.Contains("함정 몬스터 · 표시 형식 선택"),
                    "The inspector did not describe the planned Trap Monster before a position was chosen.");
                CheckCombat(inspector.Find("CombatStats"));
                inspector.Find("ArtworkControls/Details/B").GetComponent<Button>().onClick.Invoke();
                var zoom = field.Root.transform.parent.Find("Overlay_CardZoom/ZoomWin");
                CheckCombat(zoom.Find("Info/CombatStats"));
                Require(zoom.Find("Info/Stats/Rating/Number").GetComponent<Text>().text == level.ToString() &&
                    zoom.Find("Info/Stats/Rating/Kind/Label").GetComponent<Text>().text == "레벨" &&
                    !zoom.Find("Info/Stats/Rating/Printed").gameObject.activeSelf,
                    "The zoom did not keep the position preview.");
                zoom.Find("Hdr/Close/B").GetComponent<Button>().onClick.Invoke();
                var fieldView = (Button)view.Modals.FieldViewControl;
                fieldView.onClick.Invoke();
                Require(view.Modals.IsShowingField && !inspector.Find("CombatStats").gameObject.activeSelf &&
                    inspector.Find("Stats/Info/Text").GetComponent<Text>().text == "지속 함정",
                    "Field view showed the projected monster before its summon.");
                fieldView.onClick.Invoke();
                CheckCombat(inspector.Find("CombatStats"));
                window.Find($"Body/Pos_{chosenPosition}").GetComponent<Button>().onClick.Invoke();
                Require(view.Responses.SequenceEqual(new[] { (int)chosenPosition }), "The preview changed the native position response.");
                duel.SubmitResponseInt(view.Responses.Single());
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                var monster = duel.Players[duel.HumanPlayerIndex].mzone.Single(card => card?.code == code);
                Require(monster.position == chosenPosition && monster.level == level && monster.attack == atk && monster.defense == def,
                    "The live Trap Monster did not match the preview.");
                field.Refresh();
                CheckCombat(inspector.Find("CombatStats"));
                string shown = inspector.Find("Stats/Info/Text").GetComponent<Text>().text;
                Require(shown.Contains(chosenPosition == attack ? "공격 표시" : "수비 표시") && !shown.Contains("표시 형식 선택"),
                    "The inspector did not follow the same Trap Monster into its chosen position.");
            }
            Debug.Log("[VERIFY DUEL TRAP POSITION] Both native Trap Monster previews matched their selected form and final live values.");
        }

        private static void VerifyKoreanEffectCatalogue()
        {
            int labels = 0, withDescriptions = 0;
            foreach (var card in CardDatabase.AllCards)
            {
                string rules = card.desc ?? "";
                Require(rules.Any(letter => letter is >= '가' and <= '힣') &&
                    !rules.Contains("[Pendulum Effect]", StringComparison.OrdinalIgnoreCase) &&
                    !rules.Contains("[Monster Effect]", StringComparison.OrdinalIgnoreCase),
                    $"Korean card rules or headings were missing for {card.code}.");
                for (int i = 0; i < (card.strs?.Length ?? 0); i++)
                {
                    if (string.IsNullOrWhiteSpace(card.strs[i])) continue;
                    ulong id = ((ulong)card.code << 20) | (uint)i;
                    string label = CardDatabase.ResolveOptionDescription(id);
                    Require(label == null || label.Any(letter => letter is >= '가' and <= '힣'),
                        $"An English option label was exposed for {card.code}/{i}.");
                    string text = CardDatabase.ResolveEffectDescription(id, card.code);
                    bool flavorOnly = (card.type & (YgoConstants.TYPE_NORMAL | YgoConstants.TYPE_TOKEN)) != 0 && !card.IsPendulum;
                    Require(text == null && flavorOnly || text != null && text.Any(letter => letter is >= '가' and <= '힣') &&
                        !text.Contains("[Pendulum Effect]"), $"The Korean fallback was missing for {card.code}/{i}.");
                    labels++;
                    if (label != null) withDescriptions++;
                }
            }
            Debug.Log($"[VERIFY KOREAN DUEL EFFECTS] {labels} bundled labels checked. {withDescriptions} Korean labels, " +
                $"{labels - withDescriptions} Korean card-text fallbacks.");
        }

        internal static byte[] ChooseKitkallosDestination(DuelSession duel, bool toGrave)
        {
            var prompt = duel.ActivePrompt;
            Require(prompt?.messageType == YgoConstants.MSG_SELECT_OPTION &&
                prompt.options.SequenceEqual(new[] { 573UL, 574UL }) && duel.SelectionChainLink?.code == 92731385,
                "The expected native hand and graveyard options were not offered by Kitkallos.");

            using var view = new ChainView();
            view.Modals.ShowPrompt(duel, prompt);
            Button Option(int index) => view.Modals.ModalRoot.GetComponentsInChildren<Button>()
                .Single(button => button.name == $"Opt_{index}");
            string hand = Option(0).GetComponentInChildren<Text>().text;
            string grave = Option(1).GetComponentInChildren<Text>().text;
            Require(hand == "패에 넣는다" && grave == "묘지로 보낸다",
                $"Kitkallos showed the wrong destination labels: [{hand}] and [{grave}].");
            Require(view.Modals.ModalRoot.transform.Find("Win/Hdr/SubtitleBand/Sub").GetComponent<Text>().text ==
                CardDatabase.GetName(92731385), "The destination prompt omitted the resolving card's name.");

            int chosen = toGrave ? 1 : 0;
            Option(chosen).onClick.Invoke();
            Require(view.Responses.SequenceEqual(new[] { chosen }), "The chosen destination submitted an incorrect native index.");
            return BitConverter.GetBytes(view.Responses[0]);
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
            Require(view.Choice(0).GetComponentInChildren<CompositeCardView>().CurrentCode == LadyLabrynth &&
                    view.FindChoice(1) == null &&
                    view.EffectOption(0).GetComponentInChildren<Text>().text.StartsWith("① ", StringComparison.Ordinal) &&
                    view.EffectOption(1).GetComponentInChildren<Text>().text.StartsWith("② ", StringComparison.Ordinal),
                "The same card did not present both numbered effects beside a single artwork tile.");
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

        private static void VerifyActivationChoicesAcrossReopen(DuelSession duel)
        {
            using var view = new ChainView();
            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
            prompt.candidates.Add(new SelectableCardItem { code = 14087893, location = YgoConstants.LOCATION_HAND });
            prompt.candidates.Add(Repeated(0));
            prompt.candidates.Add(new SelectableCardItem { code = 14558127, location = YgoConstants.LOCATION_HAND, sequence = 1 });
            prompt.candidates.Add(Repeated(1));
            view.Modals.ShowPrompt(duel, prompt, ChainConfirmationMode.Off);
            var previous = view.EffectOption(1).onClick;
            view.SelectChoice(1, LadyLabrynth);
            view.VerifyEffect(prompt.candidates[3]);
            view.ConfirmChoice();
            Require(view.Responses.SequenceEqual(new[] { 3 }),
                "A filtered duplicate effect did not submit its noncontiguous engine index.");

            var reopened = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
            reopened.candidates.Add(prompt.candidates[1]);
            reopened.candidates.Add(prompt.candidates[0]);
            reopened.candidates.Add(prompt.candidates[3]);
            view.Modals.ShowPrompt(duel, reopened, ChainConfirmationMode.On);
            previous.Invoke();
            view.VerifyEffect(reopened.candidates[0]);
            view.EffectOption(2).GetComponent<DuelPromptSelection>().OnSelect(null);
            view.VerifyEffect(reopened.candidates[2]);
            var chosen = reopened.candidates[2];
            reopened.candidates[2] = reopened.candidates[1];
            view.ConfirmChoice();
            Require(view.Modals.IsPromptOpen && view.Responses.SequenceEqual(new[] { 3 }),
                "The chooser sent a replacement candidate at the selected engine index.");
            reopened.candidates[2] = chosen;
            ulong description = chosen.description;
            chosen.description ^= 1;
            view.ConfirmChoice();
            Require(view.Modals.IsPromptOpen && view.Responses.SequenceEqual(new[] { 3 }),
                "The chooser submitted an effect whose meaning changed after rendering.");
            chosen.description = description;
            view.ConfirmChoice();
            Require(view.Responses.SequenceEqual(new[] { 3, 2 }),
                "Reopening the chooser carried the previous prompt's native candidate mapping into the new choice.");

            SelectableCardItem Repeated(uint effect) => new SelectableCardItem
            {
                code = LadyLabrynth, controller = 0, location = YgoConstants.LOCATION_MZONE, sequence = 2,
                description = ((ulong)LadyLabrynth << 20) | effect, isTriggeredEffect = true,
            };
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
                promptDesc = 221,
                promptEffectDesc = (ulong)LadyLabrynth << 20,
            };

            using var view = new ChainView();
            view.Show(duel, prompt);
            view.VerifyPassAvailability(true);
            Canvas.ForceUpdateCanvases();
            var activate = (RectTransform)view.Modals.ModalRoot.transform.Find("ChainWin/Footer/Activate/B");
            var icon = activate.Find("Icon").GetComponent<Image>();
            Require(activate.Find("Label").GetComponent<Text>().text == "발동" && activate.rect.height <= 56f &&
                icon.sprite == DuelUiIcons.Action("ACTION") && icon.rectTransform.rect.width <= 26f &&
                icon.rectTransform.rect.height <= 26f,
                "The compact activation button must show 발동 with a small icon.");
            view.VerifyEffect(new SelectableCardItem
            {
                code = LadyLabrynth,
                location = YgoConstants.LOCATION_HAND,
                description = prompt.promptEffectDesc,
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
                VerifyPositionChoices(duel, modals, (RectTransform)canvas.transform, responses, events);

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

        private static void VerifyPositionChoices(DuelSession session, DuelPromptModals modals, RectTransform canvas,
            List<byte[]> responses, EventSystem events)
        {
            const uint cyberDragon = 70095154, mysticalElf = 15025844;
            const uint attack = YgoConstants.POS_FACEUP_ATTACK, defense = YgoConstants.POS_FACEUP_DEFENSE;
            var empty = Array.Empty<uint>();
            var main = Enumerable.Repeat(cyberDragon, 40).ToArray();
            var opponent = Enumerable.Repeat(mysticalElf, 40).ToArray();
            var ai = new TacticalAI();
            var window = modals.ModalRoot.transform.Find("Win");
            var body = window.Find("Body");
            Vector2 initialSize = canvas.sizeDelta;
            foreach (bool usePad in new[] { false, true })
            {
                using var duel = YgoProjectBuilder.StartSeedMatching("Summon position UI", main, empty, opponent, empty,
                    false, current => current.ActivePrompt?.summonable.Exists(card => card.code == mysticalElf) == true);
                duel.SubmitResponseInt(0);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                duel.SubmitResponseInt(7);
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                int summon = duel.ActivePrompt?.spSummonable.FindIndex(card => card.code == cyberDragon) ?? -1;
                Require(summon >= 0, "The native fixture could not Special Summon Cyber Dragon.");
                duel.SubmitResponseInt((summon << 16) | 1);
                for (int step = 0; step < 8 && duel.ActivePrompt?.messageType != YgoConstants.MSG_SELECT_POSITION; step++)
                {
                    if (duel.ActivePrompt is { } pending) duel.SubmitResponse(ai.ComputeResponse(duel, pending));
                    else duel.StepUntilPrompt();
                }
                var prompt = duel.ActivePrompt;
                Require(prompt?.messageType == YgoConstants.MSG_SELECT_POSITION &&
                        prompt.allowedPositions == (attack | defense) && prompt.playerId == duel.HumanPlayerIndex,
                    "The native summon did not offer both face-up poses to the player.");

                modals.ShowPrompt(duel, prompt);
                var title = window.Find("Hdr/TitleBand/Title").GetComponent<Text>();
                var subtitle = window.Find("Hdr/SubtitleBand/Sub").GetComponent<Text>();
                var rating = window.Find("Hdr/SubtitleBand/Rating");
                Require(subtitle.text == CardDatabase.GetName(cyberDragon) && rating?.Find("Number").GetComponent<Text>().text == "5" &&
                    rating.Find("Symbol").GetComponent<Image>().sprite == DuelStatSymbols.Rating(DuelRatingKind.Level),
                    "The position picker lost the summon rating.");
                var attackChoice = body.Find($"Pos_{attack}");
                var defenseChoice = body.Find($"Pos_{defense}");
                Require(body.GetComponentsInChildren<Button>().Length == 2, "The position picker showed an illegal choice.");
                foreach (var choice in new[] { attackChoice, defenseChoice })
                {
                    var entry = CardDatabase.Get(cyberDragon);
                    Require(choice.Find("Attribute")?.GetComponent<Image>().sprite == DuelUiKit.GetAttributeSprite(entry.attr) &&
                        choice.Find("Race")?.GetComponent<Image>().sprite == DuelUiKit.GetRaceSprite(entry.race),
                        "A face-up summon choice did not display its attribute and race badges.");
                    var stats = choice.Find("CombatStats");
                    Image atkRail = stats.Find("Attack/Rail").GetComponent<Image>();
                    Image defRail = stats.Find("Defense/Rail").GetComponent<Image>();
                    Require(stats.Find("Attack/Digits/Value").GetComponent<Text>().text == "2100" &&
                            stats.Find("Defense/Digits/Value").GetComponent<Text>().text == "1600" &&
                            (atkRail.color.a > defRail.color.a) == (choice == attackChoice) &&
                            !stats.GetComponentsInChildren<Graphic>().Any(g => g.raycastTarget),
                        "The position option did not emphasize the correct combat value or blocked the button.");
                }

                foreach (var size in new[] { new Vector2(1600, 900), new Vector2(1024, 768), new Vector2(852, 393) })
                {
                    canvas.sizeDelta = size;
                    Canvas.ForceUpdateCanvases();
                    VerifyPositionArtworkBounds(attackChoice, sideways: false);
                    VerifyPositionArtworkBounds(defenseChoice, sideways: true);
                    foreach (var label in new[] { title, subtitle })
                    {
                        var generator = label.cachedTextGenerator;
                        generator.Populate(label.text, label.GetGenerationSettings(label.rectTransform.rect.size));
                        Require(generator.lineCount == 1 && generator.characterCountVisible >= label.text.Length,
                            $"The position picker hid {label.name} at {size.x}×{size.y} ({generator.characterCountVisible} rendered characters).");
                    }
                }
                canvas.sizeDelta = initialSize;
                Canvas.ForceUpdateCanvases();
                if (usePad)
                {
                    Require(events.currentSelectedGameObject == attackChoice.gameObject, "The controller did not focus the first position.");
                    Pad(modals, Vector2Int.right);
                    Require(events.currentSelectedGameObject == defenseChoice.gameObject, "The controller could not focus defense.");
                    Pad(modals, button: "Submit");
                }
                else
                {
                    ExecuteEvents.Execute(attackChoice.gameObject,
                        new PointerEventData(events) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
                }

                uint expected = usePad ? defense : attack;
                Require(!modals.IsPromptOpen && responses.Count == 1 && BitConverter.ToUInt32(responses[0], 0) == expected,
                    "The position choice did not preserve the native response.");
                duel.SubmitResponse(responses[0]);
                responses.Clear();
                YgoProjectBuilder.DriveUntilIdle(duel, ai);
                Require(duel.RetryCount == 0 && duel.Players[duel.HumanPlayerIndex].mzone
                        .Any(card => card?.code == cyberDragon && card.position == expected),
                    "The engine did not apply the selected position.");
            }

            var hidden = new DuelPrompt
            {
                messageType = YgoConstants.MSG_SELECT_POSITION, allowedPositions = (byte)YgoConstants.POS_FACEDOWN_DEFENSE,
            };
            modals.ShowPrompt(session, hidden);
            var hiddenChoice = body.Find($"Pos_{YgoConstants.POS_FACEDOWN_DEFENSE}");
            var hiddenCard = hiddenChoice.GetComponentInChildren<CompositeCardView>().transform.Find("AspectBox");
            Require(hiddenChoice.Find("CombatStats") == null && hiddenChoice.Find("Race") == null &&
                hiddenChoice.Find("Attribute") == null && hiddenCard.Find("CardBack").gameObject.activeSelf &&
                !window.Find("Hdr/SubtitleBand/Rating").gameObject.activeSelf,
                "An unknown face-down choice exposed a card face or combat values.");
            hiddenChoice.GetComponent<Button>().onClick.Invoke();
            Require(responses.Count == 1 && BitConverter.ToUInt32(responses[0], 0) == YgoConstants.POS_FACEDOWN_DEFENSE,
                "The single face-down option did not preserve the native response.");
            responses.Clear();
            Debug.Log("[VERIFY DUEL POSITIONS] Native Cyber Dragon position choices retained Level 5 and highlighted 2100 ATK or 1600 DEF. Pointer, controller, three viewport sizes and hidden art passed.");
        }

        private static void VerifyPositionArtworkBounds(Transform choice, bool sideways)
        {
            var area = (RectTransform)choice.Find("Card");
            var art = area.GetComponentInChildren<CompositeCardView>().transform.Find("AspectBox");
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(area, art);
            Require(bounds.min.x >= area.rect.xMin - .5f && bounds.max.x <= area.rect.xMax + .5f &&
                    bounds.min.y >= area.rect.yMin - .5f && bounds.max.y <= area.rect.yMax + .5f &&
                    Mathf.Min(bounds.size.x, bounds.size.y) > 45f && (bounds.size.x > bounds.size.y) == sideways,
                "A position preview overlapped its neighboring labels or lost its orientation.");
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
                prompt.candidates.Add(new SelectableCardItem { code = LadyLabrynth, sumParam = 2 | (4u << 16) });
                modals.ShowPrompt(duel, prompt);
                Canvas.ForceUpdateCanvases();

                var panel = (RectTransform)modals.ModalRoot.transform.Find("Win");
                var scroll = panel.Find("Body/Cards")?.GetComponent<ScrollRect>();
                Require(panel.rect.height < 450f && panel.anchorMax.y < .65f && scroll != null && scroll.horizontal &&
                        scroll.content.rect.width > scroll.viewport.rect.width, "Material choices did not fit a compact, scrollable panel.");
                var confirm = panel.Find("Footer/Btn_선택 완료/B").GetComponent<Button>();
                var subtitle = panel.Find("Hdr/SubtitleBand/Sub").GetComponent<Text>();
                Require(subtitle.text == "합계 4 · 목표 8", "The total omitted a required material.");
                Require(scroll.content.Find("Tile_11/Source/Detail/Text").GetComponent<Text>().text == "2/4",
                    "The material list lost one of the supplied contribution values.");
                var first = scroll.content.Find("Tile_0").GetComponent<Button>();
                Require(((RectTransform)first.transform.Find("Card/V/AspectBox")).rect.height >= 170f,
                    "The material choice artwork became too small.");
                first.onClick.Invoke();
                Require(!confirm.interactable && subtitle.text == "합계 6 · 목표 8",
                    "An insufficient selection showed the wrong total or enabled confirmation.");
                confirm.onClick.Invoke();
                Require(responses.Count == 0, "A disabled material confirmation submitted a response.");
                first.onClick.Invoke();
                Require(modals.IsPromptOpen && responses.Count == 0, "Clicking an invalid material twice submitted a response.");

                first.onClick.Invoke();
                scroll.horizontalNormalizedPosition = 1f;
                scroll.content.Find("Tile_11").GetComponent<Button>().onClick.Invoke();
                Require(scroll.content.Find("Tile_-1/Chk").gameObject.activeSelf &&
                        !scroll.content.Find("Tile_0/Chk").gameObject.activeSelf && confirm.interactable &&
                        subtitle.text == "최소 6 · 최대 8 · 목표 8",
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

        private static void VerifyCandidateReadouts()
        {
            const uint code = 89631139;
            const uint powerStone = 34029630;
            const string card = "Card/V/AspectBox";
            const string attack = "Attack/Digits/Value";
            const string counters = "CardReadout/Counters";
            using var duel = new DuelSession();
            var canvas = new GameObject("Candidate Readout Verification", typeof(Canvas));
            var modals = new DuelPromptModals(canvas.transform, null, null);
            var field = new DuelFieldUI(null, canvas.transform, modals);
            try
            {
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600, 900);
                duel.Players[0].mzone[0] = Monster(3700, YgoConstants.POS_FACEUP_ATTACK);
                duel.Players[0].mzone[1] = Monster(2100, YgoConstants.POS_FACEUP_DEFENSE);
                duel.Players[0].mzone[1].effectsDisabled = true;
                duel.Players[0].mzone[1].Counters = new[] { ((ushort)1, (ushort)2) };
                duel.Players[1].mzone[0] = Monster(4950, YgoConstants.POS_FACEDOWN_DEFENSE);
                duel.Players[1].mzone[0].Counters = new[] { ((ushort)1, (ushort)4) };
                duel.Players[0].szone[0] = new LiveCardState
                {
                    code = powerStone, type = YgoConstants.TYPE_TRAP | YgoConstants.TYPE_CONTINUOUS,
                    position = YgoConstants.POS_FACEUP_ATTACK, Counters = new[] { ((ushort)1, (ushort)3) },
                };
                field.BindSession(duel, null, null);

                var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, playerId = 0, min = 1, max = 2 };
                prompt.candidates.Add(new SelectableCardItem { code = code, location = YgoConstants.LOCATION_MZONE });
                prompt.candidates.Add(new SelectableCardItem { code = code, location = YgoConstants.LOCATION_MZONE, sequence = 1 });
                prompt.candidates.Add(new SelectableCardItem { code = code, location = YgoConstants.LOCATION_MZONE, controller = 1 });
                prompt.candidates.Add(new SelectableCardItem { code = code, location = YgoConstants.LOCATION_MZONE | YgoConstants.LOCATION_OVERLAY });
                prompt.candidates.Add(new SelectableCardItem { code = powerStone, location = YgoConstants.LOCATION_SZONE });
                modals.ShowPrompt(duel, prompt);
                Canvas.ForceUpdateCanvases();

                var cards = modals.ModalRoot.transform.Find("Win/Body/Cards/Viewport/Content");
                var first = cards.Find("Tile_0");
                var second = cards.Find("Tile_1");
                var hidden = cards.Find("Tile_2").Find(card);
                var material = cards.Find("Tile_3");
                var trap = cards.Find($"Tile_4/{card}");
                Require(first.Find($"{card}/CardReadout/Combat/{attack}").GetComponent<Text>().text == "3700" &&
                        second.Find($"{card}/CardReadout/Combat/{attack}").GetComponent<Text>().text == "2100",
                    "Card choices did not use the correct instance's current attack.");
                Require(first.Find($"{card}/CardReadout/Labels/Rating/Value").GetComponent<Text>().text == "8" &&
                        first.Find($"{card}/CardReadout/Labels/Rating/Symbol").GetComponent<Image>().sprite ==
                            DuelStatSymbols.Rating(DuelRatingKind.Level),
                    "The compact card choice lost the level value or symbol.");
                Require(hidden.Find("CardBack").gameObject.activeSelf && hidden.Find("CardReadout") == null &&
                        material.Find($"{card}/CardReadout") == null,
                    "A hidden card or Xyz material exposed the live stats of a field monster.");
                Require(second.Find($"{card}/{counters}/Number/Value").GetComponent<Text>().text == "2" &&
                        trap.Find($"{counters}/Number/Value").GetComponent<Text>().text == "3" &&
                        trap.Find($"{counters}/Symbol").GetComponent<Image>().sprite == DuelStatSymbols.Counter &&
                        !trap.Find("CardReadout/Combat").gameObject.activeSelf,
                    "The selection list lost the monster or trap counter count.");

                var inspector = field.Root.transform.Find($"LeftInspector/CardDetails/CombatStats/{attack}").GetComponent<Text>();
                first.GetComponent<CardDragTrigger>().OnPointerEnterAction();
                Require(inspector.text == "3700", "Hovering a target lost its live attack.");
                second.GetComponent<DuelPromptSelection>().OnSelect(null);
                Require(inspector.text == "2100", "Focusing a second copy inspected the wrong field card.");
                second.GetComponent<Button>().onClick.Invoke();
                Canvas.ForceUpdateCanvases();
                var status = RectTransformUtility.CalculateRelativeRectTransformBounds(second, second.Find($"{card}/CardReadout/Labels/Status"));
                var check = RectTransformUtility.CalculateRelativeRectTransformBounds(second, second.Find("Chk"));
                var counter = RectTransformUtility.CalculateRelativeRectTransformBounds(second, second.Find($"{card}/{counters}"));
                Require(status.max.x <= check.min.x || status.max.y <= check.min.y,
                    "The selection check covered the card status.");
                Require(counter.max.y <= status.min.y + .1f && (counter.max.x <= check.min.x || counter.max.y <= check.min.y),
                    "The selection check or status covered the counter count.");
                material.GetComponent<Button>().onClick.Invoke();
                Require(inspector.text == "3000", "Inspecting a material inherited the host's attack.");

                var chain = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0, forced = true };
                chain.candidates.Add(prompt.candidates[1]);
                modals.ShowPrompt(duel, chain);
                var choice = modals.ModalRoot.GetComponentsInChildren<Button>().Single(button => button.name == "Ch_0");
                Require(inspector.text == "2100" && choice.transform.Find($"{card}/CardReadout/Combat/{attack}") != null,
                    "Choosing a face-up chain source did not keep its live readout.");
                Debug.Log("[VERIFY DUEL CANDIDATE READOUT] Current stats, pointer and controller inspection, hidden cards, materials and chain sources passed.");
            }
            finally
            {
                field.UnbindSession();
                UnityEngine.Object.DestroyImmediate(canvas);
            }

            LiveCardState Monster(int value, uint position) => new LiveCardState
            {
                code = code, type = YgoConstants.TYPE_MONSTER, attack = value, baseAttack = 3000,
                defense = 2500, baseDefense = 2500, position = position, level = 8,
            };
        }

        private sealed class ChainView : IDisposable
        {
            private readonly GameObject _canvas = new GameObject("Chain Chooser Verification", typeof(Canvas));
            private readonly RectTransform _canvasRect;
            private readonly List<uint> _inspected = new();
            private DuelFieldUI _field;
            internal readonly DuelPromptModals Modals;
            internal readonly List<int> Responses = new();

            internal ChainView()
            {
                _canvasRect = (RectTransform)_canvas.transform;
                _canvasRect.sizeDelta = new Vector2(1600, 900);
                Modals = new DuelPromptModals(_canvas.transform, code =>
                {
                    _inspected.Add(code);
                    _field?.InspectCard(code);
                }, response =>
                {
                    Require(response.Length == sizeof(int), "Chain chooser sent a non-integer response.");
                    Responses.Add(BitConverter.ToInt32(response, 0));
                });
            }

            internal DuelFieldUI AttachField(DuelSession duel)
            {
                _field = new DuelFieldUI(null, _canvas.transform, Modals, mobileLayout: false);
                _field.BindSession(duel, null, null);
                return _field;
            }

            internal void Show(DuelSession duel, DuelPrompt prompt)
            {
                Modals.ShowPrompt(duel, prompt);
                Require(Modals.IsPromptOpen, "A chain with available choices did not open.");
                Canvas.ForceUpdateCanvases();
                for (int i = 0; i < prompt.candidates.Count; i++)
                {
                    var tile = FindChoice(i);
                    Require(tile != null
                            ? tile.GetComponentInChildren<CompositeCardView>().CurrentCode == prompt.candidates[i].code
                            : EffectOption(i) != null,
                        $"Wrong source card or missing effect for chain choice {i}.");
                }
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

            internal Transform FindChoice(int index) =>
                Modals.ModalRoot.GetComponentsInChildren<Transform>().SingleOrDefault(child => child.name == $"Ch_{index}");

            internal Transform Choice(int index) => FindChoice(index) ??
                throw new InvalidOperationException($"Missing card tile {index}.");

            internal Button EffectOption(int index) => Modals.ModalRoot.GetComponentsInChildren<Button>(true)
                .SingleOrDefault(button => button.name == $"Effect_{index}");

            internal void SelectChoice(int index, uint source)
            {
                int previousResponses = Responses.Count;
                var option = EffectOption(index);
                var button = option;
                if (option != null && !option.gameObject.activeInHierarchy)
                {
                    var tiles = (Button[])typeof(DuelPromptModals).GetField("_activationButtons",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Modals);
                    tiles[index].onClick.Invoke();
                }
                else if (option == null) button = Choice(index).GetComponent<Button>();
                Require(button != null && button.IsInteractable() && button.gameObject.activeInHierarchy,
                    $"Chain choice {index} cannot be selected.");
                button.onClick.Invoke();
                Require(Modals.IsPromptOpen && Responses.Count == previousResponses && _inspected.LastOrDefault() == source,
                    $"Previewing chain choice {index} did not inspect the corresponding card.");
            }

            internal void VerifyEffect(SelectableCardItem source)
            {
                string text = CardDatabase.ResolveEffectDescription(source.description, source.code)
                    ?? "한글 효과 설명 없음";
                string location = YgoConstants.GetLocationNameKR(source.location);
                var visibleText = Modals.ModalRoot.GetComponentsInChildren<Text>();
                var labels = visibleText.Select(label => label.text).ToArray();
                var effect = visibleText.SingleOrDefault(label => label.gameObject.name == "Effect");
                bool chosenEffectVisible = effect != null && effect.text.Contains(text) ||
                    Modals.ModalRoot.GetComponentsInChildren<Button>()
                        .Where(button => button.name.StartsWith("Effect_", StringComparison.Ordinal))
                        .Any(button => button.GetComponent<Outline>().effectDistance.x > 1f &&
                            button.GetComponentInChildren<Text>().text.Contains(text));
                Require(labels.Any(label => label.Contains(CardDatabase.GetName(source.code))) &&
                        labels.Any(label => label.Contains(location)) && !string.IsNullOrWhiteSpace(text) &&
                        chosenEffectVisible,
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

            public void Dispose()
            {
                _field?.UnbindSession();
                UnityEngine.Object.DestroyImmediate(_canvas);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException($"[CHAIN CHOOSER] {message}");
        }
    }
}
