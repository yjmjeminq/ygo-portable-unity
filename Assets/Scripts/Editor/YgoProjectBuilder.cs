using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.AI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    public static class YgoProjectBuilder
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string DllAssetPath = "Assets/Plugins/x86_64/ygoengine.dll";
        private const string WinBuildPath = "Builds/Windows/YGOPortable.exe";

        [MenuItem("YGO Portable/1. Configure Assets & Create Main Scene")]
        public static void ConfigureAllAndScene()
        {
            ConfigureAssets();
            CreateMainScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[YgoProjectBuilder] Assets and Main.unity configured.");
        }

        [MenuItem("YGO Portable/2. Run Headless Verification (Engine + 5 AI Duels)")]
        public static void VerifyAllHeadless()
        {
            try
            {
                ConfigureAssets();
                CreateMainScene();
                RunVerificationSuite();
                Debug.Log("[VERIFY] ALL HEADLESS CHECKS PASSED");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VERIFY FAILED] {ex}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        public static void VerifyDuelInteractionHeadless()
        {
            var errors = new List<string>();
            int exit = 1;
            try
            {
                string streaming = Application.streamingAssetsPath;
                AssertTrue(YgoNativeBridge.Initialize(streaming, (kind, message) =>
                {
                    if (kind == 0) errors.Add(message);
                }) == 0, "Could not initialize the duel engine.");
                CardDatabase.Load(streaming);
                DeckRepository.Load(streaming, Path.Combine(Path.GetTempPath(), "ygo_duel_ui_verify"), false);
                CardArtLoader.Initialize(streaming);
                TestCardPresentation.VerifyInterfaceIcons();
                VerifyFastChainConfirmationModes();
                TestTokenAndComplexEffects.RunTokenSuiteHeadless();
                TestChainChooser.Run();
                TestDuelZoneBrowser.Run();
                TestHandGlow.Run();
                TestCardPresentation.VerifyOverframePaths();
                TestDuelPadNavigation.Run();
                TestDuelAiTactics.Run();
                AssertTrue(errors.Count == 0, string.Join(" | ", errors.Take(3)));
                Debug.Log("[VERIFY DUEL UI] Native prompts, touchscreen geometry, controller navigation and presentation passed. ScriptErrors=0");
                exit = 0;
            }
            catch (Exception error) { Debug.LogException(error); }
            finally
            {
                CardArtLoader.Shutdown();
                YgoNativeBridge.Shutdown();
                if (Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }

        public static void VerifyDeckEditingHeadless()
        {
            int exit = 1;
            try
            {
                string streaming = Application.streamingAssetsPath;
                CardDatabase.Load(streaming);
                CardArtLoader.Initialize(streaming);
                TestDeckEditing.Run(streaming);
                TestDeckEditorConvenience.Run(streaming);
                TestCollectionProgress.Run(streaming);
                Debug.Log("[VERIFY DECK EDITOR] All deck editor checks passed.");
                exit = 0;
            }
            catch (Exception error) { Debug.LogException(error); }
            finally
            {
                CardArtLoader.Shutdown();
                if (Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }

        public static void VerifyOpponentSelectionHeadless()
        {
            int exit = 1;
            var canvas = new GameObject("Opponent Selection Verification", typeof(Canvas));
            try
            {
                string streaming = Application.streamingAssetsPath;
                string saves = Path.Combine(Path.GetTempPath(), "ygo-opponent-selection-" + Guid.NewGuid().ToString("N"));
                CardDatabase.Load(streaming);
                CardArtLoader.Initialize(streaming);
                DeckRepository.Load(streaming, saves, seedDefaultDecks: false);
                AssertTrue(DeckRepository.PresetDecks.All(deck => deck.sigCard != 0 &&
                    (deck.main.Contains(deck.sigCard) || deck.extra.Contains(deck.sigCard))),
                    "A preset deck displayed an ace that is not in its main or Extra Deck.");
                var portraits = DeckRepository.Characters.Select(DuelUiKit.LoadOpponentPortrait).ToArray();
                AssertTrue(portraits.All(sprite => sprite != null) &&
                    portraits.Count(sprite => sprite != DuelProfileArt.Player) >= 250,
                    "The opponent roster did not resolve its ace portraits.");
                VerifyOpponentNavigation(canvas);
                Debug.Log($"[VERIFY OPPONENT SELECTION] {portraits.Length} avatars, mouse and controller flow, and turn-order selection passed.");
                exit = 0;
            }
            catch (Exception error) { Debug.LogException(error); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
                CardArtLoader.Shutdown();
                if (Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }

        [MenuItem("YGO Portable/3. Build Windows Standalone (64-bit)")]
        public static void BuildWindows()
        {
            ConfigureAssets();
            CreateMainScene();
            AssetDatabase.SaveAssets();

            string outDir = Path.GetDirectoryName(WinBuildPath);
            if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { MainScenePath },
                locationPathName = WinBuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[BUILD FAILED] result={summary.result} errors={summary.totalErrors}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // Patch boot.config to disable unstable gfx-jobs on Intel integrated GPUs
            string bootConfigPath = Path.Combine(Path.GetDirectoryName(WinBuildPath), "YGOPortable_Data", "boot.config");
            if (File.Exists(bootConfigPath))
            {
                string bootCfg = File.ReadAllText(bootConfigPath);
                bootCfg = bootCfg.Replace("gfx-enable-gfx-jobs=1", "gfx-enable-gfx-jobs=0");
                bootCfg = bootCfg.Replace("gfx-threading-mode=6", "gfx-threading-mode=0");
                File.WriteAllText(bootConfigPath, bootCfg);
            }

            Debug.Log($"[BUILD SUCCEEDED] {WinBuildPath} ({summary.totalSize / (1024f * 1024f):F1} MB, warnings={summary.totalWarnings})");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static void ConfigureAssets()
        {
            // 1. Native Plugin DLL
            var pi = AssetImporter.GetAtPath(DllAssetPath) as PluginImporter;
            if (pi != null)
            {
                bool dirty = !pi.GetCompatibleWithEditor() || !pi.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64);
                pi.SetCompatibleWithAnyPlatform(false);
                pi.SetCompatibleWithEditor(true);
                pi.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                pi.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
                pi.SetEditorData("CPU", "x86_64");
                if (dirty) pi.SaveAndReimport();
            }
            else
            {
                Debug.LogWarning($"[YgoProjectBuilder] Native plugin not found at {DllAssetPath}");
            }
        }

        public static void CreateMainScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MainScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.065f, 0.135f, 1f);
            cam.orthographic = true;

            var canvasGo = new GameObject("DuelCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            new GameObject("DuelApp", typeof(GameAppController));

            EditorSceneManager.SaveScene(scene, MainScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
        }

        private static void RunVerificationSuite()
        {
            string streaming = Application.streamingAssetsPath;
            string tempSave = Path.Combine(Path.GetTempPath(), "ygo_verify_saves");
            Directory.CreateDirectory(tempSave);

            var luaErrors = new List<string>();
            int rc = YgoNativeBridge.Initialize(streaming, (t, msg) =>
            {
                if (t == 0) luaErrors.Add(msg);
            });
            AssertTrue(rc == 0, $"YGO_Init returned {rc}");

            int cardCnt = YgoNativeBridge.YGO_GetCardCount();
            int scriptCnt = YgoNativeBridge.YGO_GetScriptCount();
            Debug.Log($"[VERIFY] Native loaded cards={cardCnt}, scripts={scriptCnt}");
            AssertTrue(cardCnt >= 14000, $"Expected >= 14000 cards, got {cardCnt}");
            AssertTrue(scriptCnt >= 13000, $"Expected >= 13000 scripts, got {scriptCnt}");
            AssertTrue(YgoNativeBridge.YGO_HasScript("c88264978.lua") == 1,
                "Missing Red-Eyes Darkness Metal Dragon effect script");

            CardDatabase.Load(streaming);
            AssertTrue(CardDatabase.AllCards.Count >= 14000, "CardDatabase count too low");
            var withoutKorean = CardDatabase.AllCards.FirstOrDefault(card => CardDatabase.GetKoreanCardDescription(card.code) == null);
            AssertTrue(withoutKorean == null, $"Missing Korean rules text for card {withoutKorean?.code}");
            var gearfried = CardDatabase.Get(423705);
            var alternateGearfried = CardDatabase.Get(423706);
            AssertTrue(gearfried != null && alternateGearfried != null &&
                alternateGearfried.name == gearfried.name && alternateGearfried.desc == gearfried.desc,
                "A Gearfried illustration did not inherit the Korean text of the identical card.");
            AssertTrue(CardDatabase.ResolveDescription((81497285UL << 20) | 1) == "덱에서 이름이 다른 일반 함정 카드 1장을 세트한다",
                "Could not resolve the Labrynth Lady's encoded effect description");
            AssertTrue(CardDatabase.ResolveDescription(71) == "마법 카드" && CardDatabase.ResolveDescription(72) == "함정 카드",
                "Spell and Trap declaration choices did not resolve");

            // Verify 환상마족 (Illusion) card (e.g. 14307929 하얀 숲의 요마 디아벨) + icon
            var paidra = CardDatabase.Get(39931513);
            AssertTrue(paidra != null && (paidra.race & YgoConstants.RACE_DRAGON) != 0,
                "Card 39931513 (천배룡 파이도라) missing");
            var illusionCard = CardDatabase.Get(14307929);
            AssertTrue(illusionCard != null && (illusionCard.race & YgoConstants.RACE_ILLUSION) != 0,
                "Card 14307929 missing RACE_ILLUSION");
            Sprite illusionSp = DuelUiKit.GetRaceSprite(YgoConstants.RACE_ILLUSION);
            AssertTrue(illusionSp != null, "Missing interface icon for RACE_ILLUSION");

            // Verify CardArtLoader
            CardArtLoader.Initialize(streaming);
            AssertTrue(CardArtLoader.GetCardBackSprite() != null, "Missing card_back sprite");
            AssertTrue(CardArtLoader.GetArtSprite(89631139) != null, "Missing Blue-Eyes art sprite");
            AssertTrue(CardArtLoader.GetArtSprite(14307929) != null, "Missing Illusion art sprite");

            // Verify the roster and deck picker before dueling with the presets.
            DeckRepository.Load(streaming, tempSave);
            VerifyOpponentRoster();

            // Run 6 full AI vs AI matches (5 going-first + 1 going-second HumanPlayerIndex=1)
            // and verify effects, chains, Extra/GY/Banished zone indexing, combat, and prompts
            var metaDecks = DeckRepository.PresetDecks.Where(d => d.isMeta2026 && d.main != null && d.main.Length >= 40).ToList();
            var classicDecks = DeckRepository.PresetDecks.Where(d => !d.isMeta2026 && d.main != null && d.main.Length >= 40).ToList();
            AssertTrue(metaDecks.Count >= 18, $"Need >=18 playable meta decks, got {metaDecks.Count}");
            AssertTrue(classicDecks.Count >= 100, $"Need >=100 playable classic decks, got {classicDecks.Count}");

            var pairings = new (PresetDeckInfo d0, PresetDeckInfo d1, bool p0First)[]
            {
                (metaDecks[0], metaDecks[Math.Min(1, metaDecks.Count - 1)], true),
                (metaDecks[Math.Min(2, metaDecks.Count - 1)], metaDecks[Math.Min(3, metaDecks.Count - 1)], true),
                (metaDecks[Math.Min(4, metaDecks.Count - 1)], metaDecks[Math.Min(5, metaDecks.Count - 1)], true),
                (metaDecks[0], classicDecks[0], true),
                (classicDecks[10], classicDecks[25], true),
                (metaDecks[1], metaDecks[0], false),
            };

            int totalRetries = 0;
            int totalChains = 0;
            int totalSpSummons = 0;
            int totalNormalSummons = 0;
            int totalAttacks = 0;
            int totalDamage = 0;
            int extraZoneActionsVerified = 0;
            int graveZoneActionsVerified = 0;
            int removedZoneActionsVerified = 0;
            int zoneSyncMismatches = 0;
            var promptHistogram = new Dictionary<int, int>();

            for (int i = 0; i < pairings.Length; i++)
            {
                var (d0, d1, p0First) = pairings[i];
                var ai0 = new TacticalAI { Difficulty = AIDifficulty.Smart };
                var ai1 = new TacticalAI { Difficulty = AIDifficulty.Smart };
                using (var session = new DuelSession())
                {
                    ulong[] seed = { (ulong)(1001 + i), (ulong)(2002 + i * 7), (ulong)(3003 + i * 13), (ulong)(4004 + i * 19) };
                    bool started = session.StartNewDuel(d0.main, d0.extra, d1.main, d1.extra, p0First, 8000, seed);
                    AssertTrue(started, $"Duel {i} failed StartNewDuel");

                    int steps = 0;
                    const int MaxSteps = 3500;
                    while (!session.IsFinished && session.TurnNumber <= 40 && steps < MaxSteps)
                    {
                        steps++;
                        if (session.ActivePrompt == null)
                        {
                            session.StepUntilPrompt();
                            continue;
                        }

                        var pr = session.ActivePrompt;
                        if (pr.messageType == YgoConstants.MSG_SELECT_CHAIN)
                            AssertTrue(pr.candidates.Count > 0, $"Duel {i + 1} exposed a chain prompt without any choices");
                        promptHistogram[pr.messageType] = promptHistogram.TryGetValue(pr.messageType, out int curCnt) ? curCnt + 1 : 1;

                        // Verify Extra/GY/Banished zone items match RefreshZonesFromCore snapshots
                        void CheckZoneItems(List<SelectableCardItem> items)
                        {
                            if (items == null) return;
                            foreach (var it in items)
                            {
                                if (it.location != YgoConstants.LOCATION_EXTRA &&
                                    it.location != YgoConstants.LOCATION_GRAVE &&
                                    it.location != YgoConstants.LOCATION_REMOVED)
                                    continue;

                                var ps = session.Players[it.controller];
                                var list = it.location == YgoConstants.LOCATION_EXTRA ? ps.extra
                                         : it.location == YgoConstants.LOCATION_GRAVE ? ps.grave
                                         : ps.banished;
                                bool matched = list.Any(c => c != null && c.controller == it.controller && c.location == it.location && c.sequence == it.sequence && c.code == it.code);
                                if (!matched)
                                    zoneSyncMismatches++;
                                else if (it.location == YgoConstants.LOCATION_EXTRA)
                                    extraZoneActionsVerified++;
                                else if (it.location == YgoConstants.LOCATION_GRAVE)
                                    graveZoneActionsVerified++;
                                else
                                    removedZoneActionsVerified++;
                            }
                        }
                        if (pr.messageType == YgoConstants.MSG_SELECT_IDLECMD || pr.messageType == YgoConstants.MSG_SELECT_BATTLECMD)
                        {
                            CheckZoneItems(pr.spSummonable);
                            CheckZoneItems(pr.activatable);
                        }

                        var curAi = pr.playerId == 0 ? ai0 : ai1;
                        byte[] resp = curAi.ComputeResponse(session, pr);
                        session.SubmitResponse(resp);
                    }

                    int dChains = session.Logs.Count(l => l.StartsWith("체인 ") && l.Contains("발동"));
                    int dSp = session.Logs.Count(l => l.StartsWith("특수 소환:"));
                    int dNorm = session.Logs.Count(l => l.StartsWith("일반 소환:"));
                    int dAtk = session.Logs.Count(l => l.Contains("공격 선언!"));
                    int dDmg = session.Logs.Count(l => l.Contains("데미지 -"));
                    totalChains += dChains;
                    totalSpSummons += dSp;
                    totalNormalSummons += dNorm;
                    totalAttacks += dAtk;
                    totalDamage += dDmg;

                    totalRetries += session.RetryCount;
                    Debug.Log($"[VERIFY DUEL {i + 1}] '{d0.title}' vs '{d1.title}' (p0First={p0First}) => finished={session.IsFinished}, winner={session.Winner}, turns={session.TurnNumber}, steps={steps}, lp=({session.Players[0].lp},{session.Players[1].lp}), chains={dChains}, sp={dSp}, atk={dAtk}, dmg={dDmg}, retries={session.RetryCount}");
                    AssertTrue(steps >= 5 && session.TurnNumber >= 2, $"Duel {i + 1} ended prematurely (turns={session.TurnNumber}, steps={steps})");
                    AssertTrue(session.RetryCount <= 4, $"Duel {i + 1} had excessive retries ({session.RetryCount})");
                }
            }

            promptHistogram.TryGetValue(YgoConstants.MSG_SELECT_CARD, out int cardPrompts);
            promptHistogram.TryGetValue(YgoConstants.MSG_SELECT_CHAIN, out int chainPrompts);
            promptHistogram.TryGetValue(YgoConstants.MSG_SELECT_EFFECTYN, out int effYnPrompts);
            promptHistogram.TryGetValue(YgoConstants.MSG_SELECT_PLACE, out int placePrompts);
            promptHistogram.TryGetValue(YgoConstants.MSG_SELECT_POSITION, out int posPrompts);
            Debug.Log($"[VERIFY EFFECTS] chains={totalChains}, spSummons={totalSpSummons}, normalSummons={totalNormalSummons}, attacks={totalAttacks}, damageEvents={totalDamage}, prompts(CARD={cardPrompts}, CHAIN={chainPrompts}, EFFECTYN={effYnPrompts}, PLACE={placePrompts}, POS={posPrompts}), zoneActionSync(EX={extraZoneActionsVerified}, GY={graveZoneActionsVerified}, BAN={removedZoneActionsVerified}, mismatches={zoneSyncMismatches})");
            Debug.Log($"[VERIFY] Total retries across {pairings.Length} duels: {totalRetries}, native errors: {luaErrors.Count}");

            AssertTrue(totalChains >= 3, $"Expected >= 3 effect/chain activations across test duels, got {totalChains}");
            AssertTrue(totalSpSummons >= 2, $"Expected >= 2 Special Summons across test duels, got {totalSpSummons}");
            AssertTrue(totalAttacks >= 3, $"Expected >= 3 attack declarations across test duels, got {totalAttacks}");
            AssertTrue(totalDamage >= 2, $"Expected >= 2 LP damage events across test duels, got {totalDamage}");
            AssertTrue(cardPrompts > 0 && chainPrompts > 0, $"Expected MSG_SELECT_CARD ({cardPrompts}) and MSG_SELECT_CHAIN ({chainPrompts}) prompts to fire");
            AssertTrue(zoneSyncMismatches == 0, $"Detected {zoneSyncMismatches} Extra/GY/Banished zone sequence sync mismatches");

            // Explicit test for User scenario: E-Emergency Call (213326) -> pick 3rd card (idx=2) from Deck
            VerifyDeckSearchEmergencyCall(classicDecks[0].main);
            VerifyBingoMachineAutoPick();
            VerifyAnimeCardOfSanctity();
            VerifyFastChainConfirmationModes();

            // Explicit test for older searchers finding newest 2025/2026 cards across Category/Type/Race/Stats/Level
            VerifyOldSearchersFindNewestCards(classicDecks[0].main);

            // Explicit test for non-search effects & prompt handlers (announce, chain negate, destroy/banish, equip/counter/coin/dice/token, reborn/tribute, extra/ritual sum, RPS)
            VerifyOtherCardEffects(classicDecks[0].main);
            VerifyTearlamentsKashtiraDeckChoice();
            VerifyKitkallosDestinationChoice();
            VerifyArcanaReadingCoinChoice();
            VerifyDeckSortDrawOrder(classicDecks[0].main);
            VerifyPossessedDestiny(classicDecks[0].main);
            VerifyMandatoryBattleAttack();
            VerifyAiTacticalChoices();
            VerifyAiDefense();
            VerifyAiFieldAndTributeChoices();
            VerifyAiKeepsActiveFieldSpell();
            VerifyAiFieldReplacementChoices();
            VerifyAiAromaGardenReplacement();
            VerifyDddTurnPlanning();
            TestOtherDeckTurnPlans.Run();
            LinkComboTraining.VerifyIncluded();
            TestComboSearch.Run();
            ComboRosterAudit.VerifyLinkMaterialProtection();
            ComboRosterAudit.VerifyOtherComboRoutes();
            VerifyRainbowDarkDragonMatch();

            // Explicit test for Token generation, effect tokens (Swordsoul Tuner), dynamic/stat tokens, and token disappearance
            TestTokenAndComplexEffects.RunTokenSuiteHeadless();
            TestDuelZoneBrowser.Run();
            TestCardPresentation.Run();
            TestHandGlow.Run();
            TestChainChooser.Run();
            TestDuelPadNavigation.Run();
            TestDuelRematch.Run();
            TestDeckEditing.Run(streaming);
            TestCollectionProgress.Run(streaming);
            TestShopModalInput.Run();

            AssertTrue(luaErrors.Count == 0, $"Native/Lua error logs detected: {string.Join(" | ", luaErrors.Take(3))}");

            CardArtLoader.Shutdown();
            YgoNativeBridge.Shutdown();
        }

        private static void VerifyOpponentRoster()
        {
            var singleCopyCards = new HashSet<uint> { 55144522, 79571449, 12580477, 18144506 };
            AssertTrue(DeckRepository.Characters.Count >= 250, "Opponent roster is incomplete");
            AssertTrue(DeckRepository.Characters.Select(c => c.id).Distinct().Count() == DeckRepository.Characters.Count,
                "Duplicate opponent IDs");
            AssertTrue(DeckRepository.PresetDecks.Select(d => d.id).Distinct().Count() == DeckRepository.PresetDecks.Count,
                "Duplicate preset deck IDs");
            foreach (var character in DeckRepository.Characters)
            {
                AssertTrue(DuelUiKit.LoadOpponentPortrait(character) != null, $"Missing portrait for {character.name}");
                var decks = DeckRepository.GetDecksForCharacter(character.id);
                var signatures = new HashSet<string>();
                foreach (var deck in decks)
                {
                    AssertTrue(deck.seriesIdx == character.seriesIdx, $"Wrong series for deck {deck.id}");
                    AssertTrue(deck.main.Length >= 40 && deck.main.Length <= 60 && deck.extra.Length <= 15 && deck.side.Length <= 15,
                        $"Wrong deck size for {deck.id}");
                    AssertTrue(deck.main.All(code => CardDatabase.Get(code) is CardInfo card && (card.type & YgoConstants.TYPE_EXTRA) == 0),
                        $"Invalid main deck card in {deck.id}");
                    AssertTrue(deck.extra.All(code => CardDatabase.Get(code) is CardInfo card && (card.type & YgoConstants.TYPE_EXTRA) != 0),
                        $"Invalid extra deck card in {deck.id}");
                    AssertTrue(deck.main.Contains(deck.sigCard) || deck.extra.Contains(deck.sigCard), $"Missing ace in deck {deck.id}");
                    var copies = deck.main.Concat(deck.extra).Concat(deck.side).GroupBy(code =>
                        CardDatabase.Get(code)?.alias is uint alias && alias != 0 ? alias : code);
                    AssertTrue(copies.All(group => group.Count() <= (singleCopyCards.Contains(group.Key) ? 1 : 3)),
                        $"Too many copies of a card in opponent deck {deck.id}");
                    string signature = string.Join(",", deck.main.OrderBy(code => code)) + "|" + string.Join(",", deck.extra.OrderBy(code => code));
                    AssertTrue(signatures.Add(signature), $"Duplicate playable deck for {character.name}");
                }
                AssertTrue(signatures.Count >= 2, $"{character.name} has fewer than two different decks");
            }

            PresetDeckInfo Preset(int id) => DeckRepository.PresetDecks.Single(deck => deck.id == id);
            AssertTrue(Preset(107).main.Count(code => code == 7394770) == 3,
                "Brilliant Fusion should retain three copies as dedicated Gem-Knight support.");
            AssertTrue(Preset(527).extra.Contains(22593417u) && !Preset(486).extra.Contains(22593417u),
                "Gumblar should be allowed in the Topologic preset and subject to the normal ban elsewhere.");
            AssertTrue(Preset(19).main.Count(code => code == 99243014) == 1,
                "The generic Synchro tag should not waive the limit on Synchro Overtake.");

            var yugiPortrait = DuelUiKit.LoadOpponentPortrait(DeckRepository.GetCharacter(104));
            var kaibaPortrait = DuelUiKit.LoadOpponentPortrait(DeckRepository.GetCharacter(94));
            AssertTrue(yugiPortrait != null && kaibaPortrait != null && yugiPortrait != DuelProfileArt.Player &&
                kaibaPortrait != DuelProfileArt.Player && yugiPortrait != kaibaPortrait,
                "The first two opponents did not show distinct ace portraits.");

            var canvas = new GameObject("Opponent Roster Test", typeof(Canvas));
            try
            {
                var ui = new OpponentSelectUI(null, canvas.transform);
                ui.Open();
                var preview = ui.Root.GetComponentInChildren<CompositeCardView>(true);
                var search = ui.SearchInput;
                AssertTrue(DeckRepository.GetDecksForCharacter(104).Take(3).Select(d => d.id).SequenceEqual(new[] { 30000, 30001, 30002 }),
                    "Yugi's Gaia, Dark Magician and Chaos decks should lead the list");
                var yugiDecks = OpponentDeckLevels.GetDecks(104);
                search.text = yugiDecks[0].title;
                AssertTrue(preview.CurrentCode == yugiDecks[0].sigCard, "Deck search should select the match.");
                search.text = "Yami Yugi";
                AssertTrue(preview.CurrentCode == yugiDecks[0].sigCard, "Yugi's original name should be searchable.");
                search.text = "zz-no-opponent";
                AssertTrue(preview.CurrentCode == 0,
                    "An empty search should clear the previous selection");
                search.text = "";
                AssertTrue(preview.CurrentCode != 0 && !ui.IsDeckSetupOpen,
                    "Clearing the search should restore the opponent list");
                ui.Root.SetActive(false);
                VerifyOpponentNavigation(canvas);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
            }
            Debug.Log($"[VERIFY ROSTER] {DeckRepository.Characters.Count} opponents, {DeckRepository.PresetDecks.Count} decks and opponent selection passed.");
        }

        private static void VerifyOpponentNavigation(GameObject canvas)
        {
            var events = EventSystem.current;
            GameObject localEvents = null;
            if (events == null)
            {
                localEvents = new GameObject("Navigation Verification", typeof(EventSystem));
                events = localEvents.GetComponent<EventSystem>();
                if (EventSystem.current == null)
                    typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(events, null);
            }
            var selectedBefore = events.currentSelectedGameObject;
            var sample = DeckRepository.PresetDecks[0];
            var ownDeck = new UserDeck { id = "opponent-navigation-fixture", name = sample.title, main = sample.main.ToList(), extra = sample.extra.ToList() };
            var startedOrders = new List<bool>();
            var ui = new OpponentSelectUI(canvas.transform, () => { },
                (opponent, deck, playerDeck, playerGoesFirst) => startedOrders.Add(playerGoesFirst));
            DeckRepository.UserDecks.Insert(0, ownDeck);
            try
            {
                Button ButtonAt(GameObject page, string path) => page.transform.Find(path).GetComponent<Button>();
                void Submit(Button button)
                {
                    events.SetSelectedGameObject(button.gameObject);
                    ExecuteEvents.Execute(button.gameObject, new BaseEventData(events), ExecuteEvents.submitHandler);
                }

                ui.Open();
                const string grid = "OpponentBrowser/CharGridPanel";
                string firstCell = grid + "/GridArea/CharSlot_0_0";
                AssertTrue(events.currentSelectedGameObject == ui.Root.transform.Find(firstCell).gameObject,
                    "The opponent controller focus should start on the selected character.");
                var secondCell = ui.Root.transform.Find(grid + "/GridArea/CharSlot_0_1");
                var nextOpponent = CollectionProgress.AvailableOpponents[1];
                events.SetSelectedGameObject(secondCell.gameObject);
                AssertTrue(!ui.IsDeckSetupOpen, "Moving focus opened the matchup before confirming the opponent.");
                Submit(secondCell.GetComponent<Button>());
                var preview = ui.Root.GetComponentInChildren<CompositeCardView>();
                var levels = OpponentDeckLevels.GetDecks(nextOpponent.id);
                AssertTrue(ui.IsDeckSetupOpen && preview.CurrentCode == levels[0].sigCard,
                    "Confirming the opponent should show its first deck.");
                Submit(ButtonAt(ui.Root, "DuelSetup/OpponentDeck/PreviousDeck/B"));
                AssertTrue(preview.CurrentCode == levels[^1].sigCard,
                    "The opponent's final deck was not available on the previous arrow from level one.");
                Submit(ButtonAt(ui.Root, "DuelSetup/BackToOpponents/B"));
                AssertTrue(!ui.IsDeckSetupOpen && events.currentSelectedGameObject == secondCell.gameObject,
                    "Returning from deck selection should focus the same character.");
                var next = ButtonAt(ui.Root, grid + "/PageBar/Next/B");
                Submit(next);
                int pageSize = ui.Root.transform.Find(grid + "/GridArea").childCount;
                var pageOpponent = CollectionProgress.AvailableOpponents[pageSize];
                Submit(ButtonAt(ui.Root, firstCell));
                AssertTrue(preview.CurrentCode == OpponentDeckLevels.GetDecks(pageOpponent.id)[0].sigCard,
                    "The selected opponent stayed on the previous page.");

                var start = ButtonAt(ui.Root, "DuelSetup/StartDuelBtn/B");
                Submit(start);
                var cancelToss = ButtonAt(ui.TurnOrderRoot, "TurnOrder/Cancel/B");
                var first = ButtonAt(ui.TurnOrderRoot, "TurnOrder/Order/First/B");
                var second = ButtonAt(ui.TurnOrderRoot, "TurnOrder/Order/Second/B");
                AssertTrue(ui.TurnOrderRoot.activeSelf && !start.IsInteractable() &&
                    events.currentSelectedGameObject == cancelToss.gameObject &&
                    !first.gameObject.activeInHierarchy && !second.gameObject.activeInHierarchy,
                    "Opening the toss should focus Cancel and hide the turn controls while the coin spins.");
                Submit(cancelToss);
                ui.UpdateTurnOrder(TurnOrderCoin.LaunchAt + .1f);
                AssertTrue(!ui.TurnOrderRoot.activeSelf && start.IsInteractable() &&
                    events.currentSelectedGameObject == start.gameObject && startedOrders.Count == 0,
                    "Canceling the toss should return focus to Duel Start and prevent a late match launch.");

                var beginToss = typeof(OpponentSelectUI).GetMethod("BeginCoinToss", BindingFlags.Instance | BindingFlags.NonPublic);
                Submit(start);
                beginToss.Invoke(ui, new object[] { true });
                ui.UpdateTurnOrder(TurnOrderCoin.RevealAt);
                AssertTrue(first.gameObject.activeInHierarchy && second.gameObject.activeInHierarchy &&
                    events.currentSelectedGameObject == first.gameObject && startedOrders.Count == 0,
                    "Winning the toss should let the player choose the turn order.");
                Submit(second);
                AssertTrue(!ui.TurnOrderRoot.activeSelf && startedOrders.SequenceEqual(new[] { false }),
                    "Choosing second after winning the toss should launch exactly one duel.");
                startedOrders.Clear();

                var opponentDeck = OpponentDeckLevels.GetDecks(pageOpponent.id)[0];
                uint[] originalMain = opponentDeck.main;
                try
                {
                    opponentDeck.main = new[] { 89631139u };
                    Submit(start);
                    beginToss.Invoke(ui, new object[] { false });
                    ui.UpdateTurnOrder(TurnOrderCoin.RevealAt);
                    AssertTrue(!first.gameObject.activeInHierarchy && startedOrders.Count == 0,
                        "Losing the toss should show the opponent's choice without opening player controls.");
                    ui.UpdateTurnOrder(TurnOrderCoin.LaunchAt);
                    ui.UpdateTurnOrder(TurnOrderCoin.LaunchAt);
                    AssertTrue(!ui.TurnOrderRoot.activeSelf && startedOrders.SequenceEqual(new[] { false }),
                        "An AI selecting first should start exactly one duel with the player going second.");

                    ui.Open();
                    Submit(ButtonAt(ui.Root, firstCell));
                    opponentDeck.main = new[] { 41418852u };
                    Submit(start);
                    beginToss.Invoke(ui, new object[] { false });
                    ui.UpdateTurnOrder(TurnOrderCoin.LaunchAt);
                    AssertTrue(!ui.TurnOrderRoot.activeSelf && startedOrders.SequenceEqual(new[] { false, true }),
                        "A Numeron AI selecting second should start with the player going first.");
                }
                finally
                {
                    opponentDeck.main = originalMain;
                }

                ui.Root.SetActive(false);
                var app = canvas.AddComponent<GameAppController>();
                var menu = new TitleAndMenuUI(app, canvas.transform);
                menu.TitleRoot.SetActive(false);
                var settings = ButtonAt(menu.MenuRoot, "Menu/Settings/Button");
                Submit(settings);
                var close = ButtonAt(menu.SettingsRoot, "SettingsWin/Close/BtnClose");
                AssertTrue(menu.SettingsRoot.activeSelf && !settings.IsInteractable() && events.currentSelectedGameObject == close.gameObject,
                    "Settings did not take focus or prevent navigation to the main menu.");
                Submit(close);
                AssertTrue(!menu.SettingsRoot.activeSelf && settings.IsInteractable() && events.currentSelectedGameObject == settings.gameObject,
                    "Closing settings should restore main menu focus.");
                VerifyMenuPadInput(app, menu, ui, events, settings, canvas.transform);
                Debug.Log("[VERIFY OPPONENT NAVIGATION] Opponent paging, coin toss, manual order after wins, automatic order after losses, and settings focus passed.");
            }
            finally
            {
                DeckRepository.UserDecks.Remove(ownDeck);
                events.SetSelectedGameObject(selectedBefore);
                if (localEvents != null)
                {
                    if (EventSystem.current == events)
                        typeof(EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(events, null);
                    UnityEngine.Object.DestroyImmediate(localEvents);
                }
            }
        }

        private static void VerifyMenuPadInput(GameAppController app, TitleAndMenuUI menu, OpponentSelectUI opponents,
            EventSystem events, Button settings, Transform canvas)
        {
            const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            var appType = typeof(GameAppController);
            var padFrame = appType.Assembly.GetType("YGOPortable.UI.DeckPadFrame");
            var padButtons = appType.Assembly.GetType("YGOPortable.UI.DeckPadButtons");
            var handle = appType.GetMethod("HandleMenuInput", privateInstance);
            var layer = DuelUiKit.FillRect("MenuPadTest", canvas);
            var padField = appType.GetField("_menuPad", privateInstance);
            padField.SetValue(app, Activator.CreateInstance(padField.FieldType, privateInstance, null, new object[] { layer }, null));
            appType.GetField("_titleAndMenu", privateInstance).SetValue(app, menu);
            appType.GetField("_oppSelect", privateInstance).SetValue(app, opponents);
            var screen = appType.GetField("_screen", privateInstance);

            void Pad(string button)
            {
                var frame = Activator.CreateInstance(padFrame, privateInstance, null,
                    new object[] { Vector2Int.zero, Enum.Parse(padButtons, button), 0f }, null);
                handle.Invoke(app, new[] { frame, (object)false });
            }

            screen.SetValue(app, AppScreen.MainMenu);
            events.SetSelectedGameObject(settings.gameObject);
            Pad("Submit");
            AssertTrue(menu.SettingsRoot.activeSelf, "Gamepad confirmation did not adopt the pointer-selected Settings button.");
            Pad("Cancel");
            AssertTrue(!menu.SettingsRoot.activeSelf && events.currentSelectedGameObject == settings.gameObject,
                "Gamepad cancel did not return from Settings to its previous focus.");

            menu.MenuRoot.SetActive(false);
            screen.SetValue(app, AppScreen.OpponentSelect);
            opponents.Open();
            Pad("NextArea");
            AssertTrue(events.currentSelectedGameObject?.name == "CharSlot_0_0",
                "Turning an opponent page with the controller should focus its first visible character.");
            Pad("Search");
            var keyboard = layer.Find("PadKeyboard");
            var keyboardFocus = events.currentSelectedGameObject?.GetComponent<Selectable>();
            AssertTrue(keyboard.gameObject.activeSelf && keyboardFocus != null &&
                keyboardFocus.transform.IsChildOf(keyboard) && keyboardFocus.navigation.mode == Navigation.Mode.Explicit,
                "Opening the opponent keyboard should move focus into a scoped keyboard immediately.");
            var keyboardControls = keyboard.GetComponentsInChildren<Selectable>();
            AssertTrue(keyboardControls.All(control =>
            {
                var navigation = control.navigation;
                return new[] { navigation.selectOnUp, navigation.selectOnDown, navigation.selectOnLeft, navigation.selectOnRight }
                    .All(neighbor => neighbor == null || neighbor.transform.IsChildOf(keyboard));
            }), "Native keyboard navigation can escape the opponent search popup.");
            Pad("Submit");
            Pad("Cancel");
            AssertTrue(!keyboard.gameObject.activeSelf && opponents.Root.activeSelf &&
                events.currentSelectedGameObject == opponents.SearchInput.gameObject,
                "Canceling the opponent keyboard should immediately restore focus to the search field.");
            var character = opponents.Root.transform.Find("OpponentBrowser/CharGridPanel/GridArea/CharSlot_0_0");
            events.SetSelectedGameObject(character.gameObject);
            Pad("Submit");
            AssertTrue(opponents.IsDeckSetupOpen &&
                events.currentSelectedGameObject.transform.IsChildOf(opponents.Root.transform.Find("DuelSetup/MyDeck")),
                "Controller confirmation should enter deck selection and focus the chosen deck.");
            Pad("Cancel");
            AssertTrue(!opponents.IsDeckSetupOpen && events.currentSelectedGameObject == character.gameObject,
                "Controller cancel should return to the selected character.");

            opponents.Root.SetActive(false);
            var codex = new ArchetypeCodexUI(canvas, () => { });
            appType.GetField("_archetypeCodex", privateInstance).SetValue(app, codex);
            screen.SetValue(app, AppScreen.ArchetypeCodex);
            codex.Open();
            Pad("NextArea");
            AssertTrue(codex.Root.transform.Find("Directory/Page/Text").GetComponent<Text>().text.StartsWith("2 / ") &&
                events.currentSelectedGameObject.transform.parent.name == "Group_0",
                "The codex should page through archetypes and focus the first visible row.");
            Pad("Search");
            AssertTrue(keyboard.gameObject.activeSelf, "The codex controller shortcut did not open search.");
            Pad("Cancel");
            AssertTrue(!keyboard.gameObject.activeSelf && events.currentSelectedGameObject == codex.SearchInput.gameObject,
                "Canceling the codex keyboard did not restore search focus.");
            codex.Close();
            menu.MenuRoot.SetActive(true);
            menu.FocusMainMenu(AppScreen.ArchetypeCodex);
            AssertTrue(events.currentSelectedGameObject == menu.MenuRoot.transform.Find("Menu/ArchetypeCodex/Button").gameObject,
                "Returning from the codex should focus its main menu entry.");
            Debug.Log("[VERIFY MENU PAD] Pointer-to-controller handoff, Settings, opponent and codex paging and search keyboard passed.");
        }

        private static void VerifyBingoMachineAutoPick()
        {
            const uint bingo = 93437091;
            const uint filler = 46986414;
            uint[] offeredCodes = { 89631139, 38517737, 30576089 };
            uint[] deck = offeredCodes.Concat(new[] { bingo })
                .SelectMany(code => Enumerable.Repeat(code, 3))
                .Concat(Enumerable.Repeat(filler, 28)).ToArray();
            using var session = StartSeedMatching("Bingo Machine", deck, Array.Empty<uint>(),
                Enumerable.Repeat(filler, 40).ToArray(), Array.Empty<uint>(), true,
                duel => duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
                    duel.ActivePrompt.activatable.Any(card => card.code == bingo));

            var ai = new TacticalAI();
            var player = session.Players[0];
            int deckBefore = player.deckCount;
            int offeredInHandBefore = player.hand.Count(card => offeredCodes.Contains(card.code));
            int activation = session.ActivePrompt.activatable.FindIndex(card => card.code == bingo);
            session.SubmitResponseInt((activation << 16) | 5);

            bool revealedCards = false;
            DriveUntilIdle(session, ai, prompt =>
            {
                if (prompt.messageType != YgoConstants.MSG_SELECT_CARD) return null;
                AssertTrue(!revealedCards && prompt.playerId == 0 && prompt.min == 3 && prompt.max == 3,
                    "Bingo Machine should only ask the activating player to choose three cards to reveal.");
                int[] picks = offeredCodes.Select(code => prompt.candidates.FindIndex(card => card.code == code)).ToArray();
                AssertTrue(picks.All(index => index >= 0), "Bingo Machine did not offer all three Blue-Eyes cards.");
                revealedCards = true;
                byte[] response = new byte[20];
                Buffer.BlockCopy(new[] { 0, picks.Length, picks[0], picks[1], picks[2] }, 0, response, 0, response.Length);
                return response;
            });

            AssertTrue(revealedCards && session.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
                player.deckCount == deckBefore - 1 && player.grave.Any(card => card.code == bingo) &&
                player.hand.Count(card => offeredCodes.Contains(card.code)) == offeredInHandBefore + 1 &&
                session.RetryCount == 0, "Bingo Machine should put one revealed card in hand without an opponent prompt.");
            Debug.Log("[VERIFY BINGO] One revealed card was automatically added to hand without an opponent prompt.");
        }

        private static void VerifyAnimeCardOfSanctity()
        {
            const uint sanctity = 511001126;
            const uint printedSanctity = 42664989;
            const uint oneDayOfPeace = 33782437;
            const uint potOfGreed = 55144522;
            const uint filler = 46986414;
            var anime = CardDatabase.Get(sanctity);
            var search = new DeckCardFilter("하늘의 선물", "", DeckCardKind.Spell, effectCategories: 1UL << 4);
            AssertTrue(anime != null && anime.alias == 0 && search.Matches(anime) && search.Matches(CardDatabase.Get(printedSanctity)),
                "Both versions of Card of Sanctity should appear in the deck editor's draw-spell search.");
            AssertTrue(CardArtLoader.GetArtSprite(sanctity) != CardArtLoader.GetFallbackArt(),
                "Anime Card of Sanctity could not load its bundled artwork.");

            uint[] empty = Array.Empty<uint>();
            uint[] deck = Enumerable.Repeat(sanctity, 3)
                .Concat(Enumerable.Repeat(oneDayOfPeace, 9))
                .Concat(Enumerable.Repeat(potOfGreed, 8))
                .Concat(Enumerable.Repeat(filler, 20)).ToArray();
            uint[] opponentDeck = Enumerable.Repeat(filler, 40).ToArray();
            var ai = new TacticalAI();

            foreach (var (peaceActivations, potActivations) in new[] { (0, 0), (2, 0), (2, 1), (0, 2) })
            {
                using var session = StartSeedMatching("Anime Card of Sanctity", deck, empty, opponentDeck, empty, true,
                    duel => duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
                        duel.ActivePrompt.activatable.Any(card => card.code == sanctity) &&
                        duel.Players[0].hand.Count(card => card.code == oneDayOfPeace) >= peaceActivations &&
                        duel.Players[0].hand.Count(card => card.code == potOfGreed) >= potActivations);

                void Activate(uint code)
                {
                    int index = session.ActivePrompt.activatable.FindIndex(card => card.code == code);
                    AssertTrue(index >= 0, $"Expected to activate {CardDatabase.GetName(code)}.");
                    session.SubmitResponseInt((index << 16) | 5);
                    DriveUntilIdle(session, ai);
                    AssertTrue(session.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD,
                        $"Expected to return to the main phase after activating {CardDatabase.GetName(code)}.");
                }

                for (int i = 0; i < peaceActivations; i++) Activate(oneDayOfPeace);
                for (int i = 0; i < potActivations; i++) Activate(potOfGreed);

                var player = session.Players[0];
                var opponent = session.Players[1];
                int playerHandBefore = 5 + potActivations;
                int opponentHandBefore = 5 + peaceActivations;
                AssertTrue(player.hand.Count == playerHandBefore && opponent.hand.Count == opponentHandBefore,
                    $"Anime Card of Sanctity: expected hands of {playerHandBefore} and {opponentHandBefore} before activation.");
                int playerDeckBefore = player.deckCount;
                int opponentDeckBefore = opponent.deckCount;
                Activate(sanctity);

                int playerHandAtResolution = playerHandBefore - 1;
                AssertTrue(player.grave.Any(card => card.code == sanctity) && session.RetryCount == 0,
                    "Anime Card of Sanctity did not resolve cleanly.");
                AssertTrue(player.hand.Count == 6 && player.deckCount == playerDeckBefore - (6 - playerHandAtResolution) &&
                    opponent.hand.Count == Math.Max(6, opponentHandBefore) &&
                    opponent.deckCount == opponentDeckBefore - Math.Max(0, 6 - opponentHandBefore),
                    $"Anime Card of Sanctity drew the wrong number of cards for hands of {playerHandAtResolution} and {opponentHandBefore}.");
                Debug.Log($"[VERIFY SANCTITY] Hands of {playerHandAtResolution} and {opponentHandBefore} resolved to {player.hand.Count} and {opponent.hand.Count}.");
            }
        }

        private static void VerifyFastChainConfirmationModes()
        {
            const uint book = 14087893;
            const uint upstart = 70368879;
            const uint sparkman = 20721928;
            uint[] main = BuildDeck40((book, 9), (upstart, 9), (sparkman, 9));
            uint[] opponent = BuildDeck40();
            uint[] empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            using var session = StartSeedMatching("Chain confirmation", main, empty, opponent, empty, true, duel =>
                duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
                duel.ActivePrompt.summonable.Any(card => card.code == sparkman) &&
                duel.ActivePrompt.sSetable.Any(card => card.code == upstart) &&
                duel.Players[0].hand.Any(card => card.code == book));

            bool OffersBook(DuelPrompt prompt) => prompt?.messageType == YgoConstants.MSG_SELECT_CHAIN &&
                prompt.playerId == session.HumanPlayerIndex && prompt.candidates.Any(card => card.code == book);

            void VerifyBookPolicy(DuelPrompt prompt, string timing, bool autoOffers)
            {
                AssertTrue(OffersBook(prompt), $"Expected a native Book of Moon response window {timing}.");
                AssertTrue(!session.ShouldPassChainPrompt(prompt, ChainConfirmationMode.Auto) == autoOffers &&
                    session.ShouldPassChainPrompt(prompt, ChainConfirmationMode.Off) &&
                    !session.ShouldPassChainPrompt(prompt, ChainConfirmationMode.On),
                    $"Unexpected AUTO/OFF/ON policy {timing}.");
            }

            void VerifyBookWindow(string timing, bool autoOffers)
            {
                var prompt = session.ActivePrompt;
                if (prompt?.messageType == YgoConstants.MSG_SELECT_PLACE)
                    session.SubmitResponse(ai.ComputeResponse(session, prompt));
                VerifyBookPolicy(session.ActivePrompt, timing, autoOffers);
                session.SubmitResponseInt(-1);
                DriveUntilIdle(session, ai);
                AssertTrue(session.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD && session.RetryCount == 0,
                    $"Passing on Book of Moon {timing} did not return to the Main Phase.");
            }

            int summon = session.ActivePrompt.summonable.FindIndex(card => card.code == sparkman);
            session.SubmitResponseInt(summon << 16);
            VerifyBookWindow("after a Normal Summon", true);

            int set = session.ActivePrompt.sSetable.FindIndex(card => card.code == upstart);
            session.SubmitResponseInt((set << 16) | 4);
            VerifyBookWindow("after setting a Spell", false);

            int activate = session.ActivePrompt.activatable.FindIndex(card => card.code == upstart);
            AssertTrue(activate >= 0, "Expected to activate the set Normal Spell.");
            session.SubmitResponseInt((activate << 16) | 5);
            VerifyBookWindow("in response to a Spell effect", true);

            int setBook = session.ActivePrompt.sSetable.FindIndex(card => card.code == book);
            AssertTrue(setBook >= 0, "Book of Moon was unavailable to set for the opponent's turn.");
            session.SubmitResponseInt((setBook << 16) | 4);
            DriveUntilIdle(session, ai);
            session.SubmitResponseInt(7);
            bool sawDraw = false;
            bool sawStandby = false;
            bool sawEnd = false;
            for (int step = 0; step < 32 && !sawEnd && !session.IsFinished; step++)
            {
                var prompt = session.ActivePrompt;
                if (prompt == null) session.StepUntilPrompt();
                else if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && prompt.playerId == session.AiPlayerIndex && prompt.canEp)
                    session.SubmitResponseInt(7);
                else if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
                {
                    if (session.TurnPlayer == session.AiPlayerIndex && OffersBook(prompt))
                    {
                        if (session.CurrentPhase == YgoConstants.PHASE_DRAW)
                        {
                            VerifyBookPolicy(prompt, "in the opponent's Draw Phase", false);
                            sawDraw = true;
                        }
                        else if (session.CurrentPhase == YgoConstants.PHASE_STANDBY)
                        {
                            AssertTrue(prompt.speCount > 0, "The prior hint heuristic would not offer Book of Moon in this Standby Phase.");
                            VerifyBookPolicy(prompt, "in the opponent's Standby Phase", false);
                            sawStandby = true;
                        }
                        else if (session.CurrentPhase == YgoConstants.PHASE_END)
                        {
                            VerifyBookPolicy(prompt, "in the opponent's End Phase", true);
                            sawEnd = true;
                            session.SubmitResponseInt(prompt.candidates.FindIndex(card => card.code == book));
                            for (int reply = 0; reply < 16 && !session.Players[0].grave.Any(card => card.code == book); reply++)
                            {
                                var follow = session.ActivePrompt;
                                if (follow == null) session.StepUntilPrompt();
                                else if (follow.messageType == YgoConstants.MSG_SELECT_CHAIN && !follow.forced)
                                    session.SubmitResponseInt(-1);
                                else session.SubmitResponse(ai.ComputeResponse(session, follow));
                            }
                            continue;
                        }
                    }
                    session.SubmitResponseInt(-1);
                }
                else throw new InvalidOperationException($"Unexpected prompt testing opponent response windows: {prompt.messageType}.");
            }
            AssertTrue(sawDraw && sawStandby && sawEnd && session.RetryCount == 0 &&
                session.Players[0].grave.Any(card => card.code == book) &&
                session.Players[0].mzone.Any(card => card?.code == sparkman && !card.IsFaceUp),
                "The native phase confirmation did not remain available or Book of Moon failed to resolve.");
            Debug.Log("[VERIFY CHAIN CONFIRMATION] Auto, Off and On passed native summon, set, spell, draw and phase windows. Retry=0");
        }

        private static void VerifyDeckSearchEmergencyCall(uint[] oppDeckMain)
        {
            const uint EMERG_CALL = 213326; // E－이머전시 콜
            uint[] heroTargets = { 20721928, 21844576, 40044918, 50720316, 58932615, 45195443, 59392529 };
            var deckList = new List<uint>(40);
            for (int r = 0; r < 3; r++) deckList.Add(EMERG_CALL);
            foreach (uint h in heroTargets)
                for (int r = 0; r < 3; r++) deckList.Add(h);
            while (deckList.Count < 40) deckList.Add(89631139); // 푸른 눈의 백룡 filler
            uint[] heroMain = deckList.ToArray();

            for (uint seedBase = 1; seedBase <= 60; seedBase++)
            {
                using (var session = new DuelSession())
                {
                    ulong[] seed = { seedBase * 11, seedBase * 23, seedBase * 37, seedBase * 53 };
                    AssertTrue(session.StartNewDuel(heroMain, Array.Empty<uint>(), oppDeckMain, Array.Empty<uint>(), true, 8000, seed),
                        "VerifyDeckSearch failed StartNewDuel");

                    int guard = 0;
                    while (!session.IsFinished && guard++ < 64)
                    {
                        if (session.ActivePrompt == null) { session.StepUntilPrompt(); continue; }
                        var p = session.ActivePrompt;
                        if (p.messageType == YgoConstants.MSG_SELECT_IDLECMD && p.playerId == 0)
                        {
                            int actIdx = p.activatable.FindIndex(a => a.code == EMERG_CALL);
                            if (actIdx < 0) break; // try next seed to get 213326 in opening hand

                            int deckBefore = session.Players[0].deckCount;
                            session.SubmitResponseInt((actIdx << 16) | 5);

                            DuelPrompt searchPrompt = null;
                            int chainGuard = 0;
                            while (!session.IsFinished && chainGuard++ < 32)
                            {
                                if (session.ActivePrompt == null) { session.StepUntilPrompt(); continue; }
                                var cp = session.ActivePrompt;
                                if (cp.messageType == YgoConstants.MSG_SELECT_CARD && cp.playerId == 0)
                                {
                                    searchPrompt = cp;
                                    break;
                                }
                                if (cp.messageType == YgoConstants.MSG_SELECT_PLACE)
                                    session.SubmitResponse(new TacticalAI().ComputeResponse(session, cp));
                                else if (cp.messageType == YgoConstants.MSG_SELECT_CHAIN || cp.messageType == YgoConstants.MSG_SELECT_EFFECTYN)
                                    session.SubmitResponseInt(cp.messageType == YgoConstants.MSG_SELECT_CHAIN ? -1 : 0);
                                else
                                    AssertTrue(false, $"Unexpected prompt {cp.messageType} while activating E-Emergency Call");
                            }

                            AssertTrue(searchPrompt != null, "E-Emergency Call did not open MSG_SELECT_CARD");
                            AssertTrue(session.LastHintMsg == 506, $"Expected HINTMSG_ATOHAND (506), got {session.LastHintMsg}");
                            string hintText = CardDatabase.ResolveDescription(session.LastHintMsg);
                            AssertTrue(hintText == "패에 넣을 카드를 선택하십시오.", $"Unexpected hint text: {hintText}");
                            AssertTrue(searchPrompt.candidates.Count >= 3, $"Expected >=3 deck search candidates, got {searchPrompt.candidates.Count}");
                            AssertTrue(searchPrompt.candidates[2].location == YgoConstants.LOCATION_DECK, "3rd candidate is not from LOCATION_DECK");

                            uint thirdCode = searchPrompt.candidates[2].code;
                            int handCopiesBefore = session.Players[0].hand.Count(c => c.code == thirdCode);

                            // Send exact UI payload matching DuelPromptModals.OnConfirmMultiSelect() for 3rd card (index = 2)
                            byte[] pickThirdResp = new byte[12];
                            Buffer.BlockCopy(BitConverter.GetBytes(0), 0, pickThirdResp, 0, 4);
                            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, pickThirdResp, 4, 4);
                            Buffer.BlockCopy(BitConverter.GetBytes(2), 0, pickThirdResp, 8, 4);
                            session.SubmitResponse(pickThirdResp);

                            int postGuard = 0;
                            while (!session.IsFinished && postGuard++ < 32)
                            {
                                if (session.ActivePrompt == null) { session.StepUntilPrompt(); continue; }
                                if (session.ActivePrompt.messageType == YgoConstants.MSG_SELECT_IDLECMD) break;
                                if (session.ActivePrompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
                                    session.SubmitResponseInt(-1);
                                else
                                    break;
                            }

                            int handCopiesAfter = session.Players[0].hand.Count(c => c.code == thirdCode);
                            int deckAfter = session.Players[0].deckCount;
                            string pickedName = CardDatabase.GetName(thirdCode);

                            AssertTrue(session.RetryCount == 0, $"E-Emergency Call search triggered RetryCount={session.RetryCount}");
                            AssertTrue(handCopiesAfter == handCopiesBefore + 1, $"3rd card [{pickedName}] ({thirdCode}) not added to hand ({handCopiesBefore}->{handCopiesAfter})");
                            AssertTrue(deckAfter == deckBefore - 1, $"Deck count did not decrease by 1 ({deckBefore}->{deckAfter})");
                            AssertTrue(session.Players[0].grave.Any(c => c.code == EMERG_CALL), "E-Emergency Call did not resolve to Graveyard");
                            AssertTrue(session.Logs.Any(l => l.Contains("카드 확인:") && l.Contains(pickedName)), $"Missing confirm log for [{pickedName}]");

                            Debug.Log($"[VERIFY SEARCH] E-이머전시 콜(213326) -> candidates={searchPrompt.candidates.Count}, 3rd pick=[{pickedName} ({thirdCode})], handCopies {handCopiesBefore}->{handCopiesAfter}, deck {deckBefore}->{deckAfter}, hint='{hintText}', retries={session.RetryCount}");
                            return;
                        }
                        if (p.messageType == YgoConstants.MSG_SELECT_CHAIN)
                            session.SubmitResponseInt(-1);
                        else
                            break;
                    }
                }
            }
            AssertTrue(false, "Could not draw E-Emergency Call across 60 seeds");
        }

        private readonly struct SearcherTestCase
        {
            public readonly uint SearcherCode;
            public readonly uint[] NewestTargets;
            public readonly bool NormalSummon;
            public readonly bool SpecialSummonToField;

            public SearcherTestCase(uint searcherCode, uint[] newestTargets, bool normalSummon = false, bool specialSummonToField = false)
            {
                SearcherCode = searcherCode;
                NewestTargets = newestTargets;
                NormalSummon = normalSummon;
                SpecialSummonToField = specialSummonToField;
            }
        }

        private static void VerifyOldSearchersFindNewestCards(uint[] oppDeckMain)
        {
            var cases = new[]
            {
                // Archetype setcode (0x3008 E-HERO -> 0x3008 Spellcaster without name string)
                new SearcherTestCase(213326,   new uint[] { 30875635 }),
                // Race + Level (Lv<=4 Warrior -> 2026 D-HERO & Warrior)
                new SearcherTestCase(32807846, new uint[] { 98931282, 62542673, 76660178 }),
                // Card Subtype (TYPE_FIELD 0x80002 -> 2026 Field Spells from cards.json fallback)
                new SearcherTestCase(73628505, new uint[] { 39513225, 38391684, 2906939, 4663194 }),
                // Race + Level (Lv<=6 Dinosaur -> newest 2025/2026 Dinos)
                new SearcherTestCase(47325505, new uint[] { 29927283, 47647354 }),
                // Race + Level -> Field SS (Lv<=3 Psychic -> newest X-Saber Bruno)
                new SearcherTestCase(67723438, new uint[] { 45462306 }, specialSummonToField: true),
                // Race + ATK/DEF stats (Dragon ATK>=3000, DEF<=2500 -> newest Dragons incl. k=23348)
                new SearcherTestCase(48800175, new uint[] { 30397786, 54725177, 97818130 }),
                // Archetype inheritance via Monster Trigger (0x8 HERO -> 0xc008 Destiny HERO & 0x3008 E-HERO)
                new SearcherTestCase(40044918, new uint[] { 98931282, 62542673, 30875635 }, normalSummon: true),
                // Archetype setcode Spell/Trap (SET_SEVENTH 0x177 & SET_BARIANS 0x1178 -> newest Field/Counter/Normal S/T)
                new SearcherTestCase(23153227, new uint[] { 39513225, 38001022, 75518330 }),
            };

            foreach (var tc in cases)
                VerifySingleOldSearcher(tc, oppDeckMain);
        }

        private static void VerifySingleOldSearcher(SearcherTestCase tc, uint[] oppDeckMain)
        {
            const uint NEUTRAL_FILLER = 83764718; // 죽은 자의 소생 (0x2 Normal Spell: never matched by Monster/Field/Seventh/Barian searchers)
            var deckList = new List<uint>(40);
            for (int r = 0; r < 3; r++) deckList.Add(tc.SearcherCode);
            foreach (uint target in tc.NewestTargets)
                for (int r = 0; r < 3; r++) deckList.Add(target);
            while (deckList.Count < 40) deckList.Add(NEUTRAL_FILLER);
            uint[] mainDeck = deckList.ToArray();

            for (uint seedBase = 1; seedBase <= 120; seedBase++)
            {
                using (var session = new DuelSession())
                {
                    ulong[] seed = { seedBase * 17 + tc.SearcherCode, seedBase * 31, seedBase * 43, seedBase * 59 };
                    AssertTrue(session.StartNewDuel(mainDeck, Array.Empty<uint>(), oppDeckMain, Array.Empty<uint>(), true, 8000, seed),
                        $"VerifyOldSearcher({tc.SearcherCode}) failed StartNewDuel");

                    int guard = 0;
                    while (!session.IsFinished && guard++ < 64)
                    {
                        if (session.ActivePrompt == null) { session.StepUntilPrompt(); continue; }
                        var p = session.ActivePrompt;
                        if (p.messageType == YgoConstants.MSG_SELECT_CHAIN)
                        {
                            session.SubmitResponseInt(-1);
                            continue;
                        }
                        if (p.messageType != YgoConstants.MSG_SELECT_IDLECMD || p.playerId != 0)
                            break;

                        int idleIdx = tc.NormalSummon
                            ? p.summonable.FindIndex(c => c.code == tc.SearcherCode)
                            : p.activatable.FindIndex(a => a.code == tc.SearcherCode);
                        if (idleIdx < 0) break;

                        // Require at least 1 of the 3 copies of every expected newest target still in Deck
                        if (!tc.NewestTargets.All(t => session.Players[0].hand.Count(h => h.code == t) < 3))
                            break;

                        uint primaryTarget = tc.NewestTargets[0];
                        int beforeZoneCount = tc.SpecialSummonToField
                            ? session.Players[0].mzone.Count(c => c != null && c.code == primaryTarget)
                            : session.Players[0].hand.Count(c => c.code == primaryTarget);

                        int idleAction = tc.NormalSummon ? 0 : 5;
                        session.SubmitResponseInt((idleIdx << 16) | idleAction);

                        DuelPrompt searchPrompt = null;
                        int stepGuard = 0;
                        while (!session.IsFinished && stepGuard++ < 40)
                        {
                            if (session.ActivePrompt == null) { session.StepUntilPrompt(); continue; }
                            var cp = session.ActivePrompt;
                            if (cp.messageType == YgoConstants.MSG_SELECT_CARD && cp.playerId == 0
                                && cp.candidates.Any(c => c.location == YgoConstants.LOCATION_DECK))
                            {
                                searchPrompt = cp;
                                break;
                            }
                            if (cp.messageType == YgoConstants.MSG_SELECT_CARD && cp.playerId == 0)
                            {
                                // Discard/hand cost (e.g., Melody of Awakening Dragon): discard filler first
                                int fillerIdx = cp.candidates.FindIndex(c => c.code == NEUTRAL_FILLER);
                                byte[] costResp = new byte[12];
                                Buffer.BlockCopy(BitConverter.GetBytes(0), 0, costResp, 0, 4);
                                Buffer.BlockCopy(BitConverter.GetBytes(1), 0, costResp, 4, 4);
                                Buffer.BlockCopy(BitConverter.GetBytes(fillerIdx >= 0 ? fillerIdx : 0), 0, costResp, 8, 4);
                                session.SubmitResponse(costResp);
                            }
                            else if (cp.messageType == YgoConstants.MSG_SELECT_EFFECTYN || cp.messageType == YgoConstants.MSG_SELECT_YESNO)
                            {
                                session.SubmitResponseInt(1); // Accept trigger (e.g., Airman search)
                            }
                            else if (cp.messageType == YgoConstants.MSG_SELECT_OPTION)
                            {
                                session.SubmitResponseInt(0);
                            }
                            else if (cp.messageType == YgoConstants.MSG_SELECT_CHAIN)
                            {
                                session.SubmitResponseInt(-1);
                            }
                            else if (cp.messageType == YgoConstants.MSG_SELECT_PLACE || cp.messageType == YgoConstants.MSG_SELECT_POSITION)
                            {
                                session.SubmitResponse(new TacticalAI().ComputeResponse(session, cp));
                            }
                            else
                            {
                                AssertTrue(false, $"Searcher {tc.SearcherCode}: unexpected prompt {cp.messageType} waiting for Deck search");
                            }
                        }

                        AssertTrue(searchPrompt != null, $"Searcher {tc.SearcherCode} ({CardDatabase.GetName(tc.SearcherCode)}) did not open Deck search");
                        AssertTrue(searchPrompt.candidates.All(c => c.code != NEUTRAL_FILLER),
                            $"Searcher {tc.SearcherCode} unexpectedly offered NEUTRAL_FILLER");

                        foreach (uint expectedCode in tc.NewestTargets)
                        {
                            AssertTrue(searchPrompt.candidates.Any(c => c.code == expectedCode && c.location == YgoConstants.LOCATION_DECK),
                                $"Old searcher [{CardDatabase.GetName(tc.SearcherCode)} ({tc.SearcherCode})] missed newest target [{CardDatabase.GetName(expectedCode)} ({expectedCode})] in Deck");
                        }

                        int pickIdx = searchPrompt.candidates.FindIndex(c => c.code == primaryTarget && c.location == YgoConstants.LOCATION_DECK);
                        byte[] pickResp = new byte[12];
                        Buffer.BlockCopy(BitConverter.GetBytes(0), 0, pickResp, 0, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(1), 0, pickResp, 4, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(pickIdx), 0, pickResp, 8, 4);
                        session.SubmitResponse(pickResp);

                        int postGuard = 0;
                        while (!session.IsFinished && postGuard++ < 40)
                        {
                            if (session.ActivePrompt == null) { session.StepUntilPrompt(); continue; }
                            var pp = session.ActivePrompt;
                            if (pp.messageType == YgoConstants.MSG_SELECT_IDLECMD) break;
                            if (pp.messageType == YgoConstants.MSG_SELECT_OPTION)
                                session.SubmitResponseInt(0); // Seventh Ascension: choose Add to Hand over Deck top
                            else if (pp.messageType == YgoConstants.MSG_SELECT_POSITION || pp.messageType == YgoConstants.MSG_SELECT_PLACE)
                                session.SubmitResponse(new TacticalAI().ComputeResponse(session, pp));
                            else if (pp.messageType == YgoConstants.MSG_SELECT_CHAIN || pp.messageType == YgoConstants.MSG_SELECT_EFFECTYN)
                                session.SubmitResponseInt(pp.messageType == YgoConstants.MSG_SELECT_CHAIN ? -1 : 0);
                            else
                                break;
                        }

                        int afterZoneCount = tc.SpecialSummonToField
                            ? session.Players[0].mzone.Count(c => c != null && c.code == primaryTarget)
                            : session.Players[0].hand.Count(c => c.code == primaryTarget);

                        AssertTrue(session.RetryCount == 0, $"Searcher {tc.SearcherCode} had RetryCount={session.RetryCount}");
                        AssertTrue(afterZoneCount == beforeZoneCount + 1,
                            $"Searcher {tc.SearcherCode} failed to move [{CardDatabase.GetName(primaryTarget)} ({primaryTarget})] ({beforeZoneCount}->{afterZoneCount})");

                        string foundSummary = string.Join(", ", tc.NewestTargets.Select(t => $"{CardDatabase.GetName(t)}({t})"));
                        Debug.Log($"[VERIFY OLD->NEW SEARCH] [{CardDatabase.GetName(tc.SearcherCode)}({tc.SearcherCode})] -> candidates={searchPrompt.candidates.Count}, verified={foundSummary}, retries={session.RetryCount}");
                        return;
                    }
                }
            }
            AssertTrue(false, $"Could not set up hand+deck for old searcher {tc.SearcherCode} across 120 seeds");
        }

        private static uint[] BuildDeck40(params (uint code, int count)[] entries)
        {
            var list = new List<uint>(40);
            foreach (var (code, count) in entries)
                for (int i = 0; i < count && list.Count < 40; i++) list.Add(code);
            while (list.Count < 40) list.Add(89631139); // 푸른 눈의 백룡
            return list.ToArray();
        }

        private static byte[] EncodeSelectIndices(params int[] indices)
        {
            byte[] resp = new byte[8 + indices.Length * 4];
            Buffer.BlockCopy(BitConverter.GetBytes(0), 0, resp, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(indices.Length), 0, resp, 4, 4);
            for (int i = 0; i < indices.Length; i++)
                Buffer.BlockCopy(BitConverter.GetBytes(indices[i]), 0, resp, 8 + i * 4, 4);
            return resp;
        }

        internal static void DriveUntilIdle(DuelSession session, TacticalAI ai, Func<DuelPrompt, byte[]> onPrompt = null, int maxSteps = 64)
        {
            int guard = 0;
            while (!session.IsFinished && guard++ < maxSteps)
            {
                if (session.ActivePrompt == null) { session.StepUntilPrompt(); continue; }
                var p = session.ActivePrompt;
                if (onPrompt != null)
                {
                    byte[] custom = onPrompt(p);
                    if (custom != null) { session.SubmitResponse(custom); continue; }
                }
                if (p.messageType == YgoConstants.MSG_SELECT_IDLECMD) break;
                if (p.messageType == YgoConstants.MSG_SELECT_CHAIN) session.SubmitResponseInt(-1);
                else if (p.messageType == YgoConstants.MSG_SELECT_EFFECTYN || p.messageType == YgoConstants.MSG_SELECT_YESNO || p.messageType == YgoConstants.MSG_SELECT_OPTION)
                    session.SubmitResponseInt(0);
                else if (p.messageType == YgoConstants.MSG_SELECT_PLACE || p.messageType == YgoConstants.MSG_SELECT_POSITION
                    || p.messageType == YgoConstants.MSG_SELECT_COUNTER || p.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD)
                    session.SubmitResponse(ai.ComputeResponse(session, p));
                else
                    break;
            }
        }

        internal static DuelSession StartSeedMatching(string label, uint[] main0, uint[] extra0, uint[] main1, uint[] extra1, bool humanFirst, Func<DuelSession, bool> setupAndCheck, int maxSeeds = 600)
        {
            for (uint seedBase = 1; seedBase <= maxSeeds; seedBase++)
            {
                var session = new DuelSession();
                ulong[] seed = { seedBase * 29 + 101, seedBase * 47 + 7, seedBase * 71 + 13, seedBase * 97 + 3 };
                AssertTrue(session.StartNewDuel(main0, extra0, main1, extra1, humanFirst, 8000, seed), $"{label}: StartNewDuel failed");
                if (setupAndCheck(session)) return session;
                session.Dispose();
            }
            AssertTrue(false, $"{label}: Could not find matching opening setup across {maxSeeds} seeds");
            return null;
        }

        private static void VerifyTearlamentsKashtiraDeckChoice()
        {
            const uint kashtira = 4928565;
            ulong ownDeck = ((ulong)kashtira << 20) | 3;
            ulong opponentDeck = ((ulong)kashtira << 20) | 4;
            uint[] main = Enumerable.Repeat(kashtira, 40).ToArray();
            uint[] opponent = Enumerable.Repeat(89631139u, 40).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();

            foreach (ulong option in new[] { ownDeck, opponentDeck })
            {
                using var duel = StartSeedMatching("Tearlaments Kashtira deck choice", main, empty, opponent, empty,
                    true, session =>
                    {
                        DriveUntilIdle(session, ai);
                        return session.ActivePrompt?.activatable.Any(card => card.code == kashtira) == true;
                    });
                int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == kashtira);
                duel.SubmitResponseInt((activate << 16) | 5);
                for (int step = 0; step < 16 && duel.ActivePrompt?.messageType != YgoConstants.MSG_SELECT_OPTION; step++)
                {
                    var prompt = duel.ActivePrompt;
                    if (prompt == null) duel.StepUntilPrompt();
                    else if (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN)
                        duel.SubmitResponseInt(prompt.promptCardCode == kashtira ? 1 : 0);
                    else if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
                        duel.SubmitResponseInt(prompt.candidates.FindIndex(card => card.code == kashtira));
                    else if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD) break;
                    else duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
                }

                var choices = duel.ActivePrompt;
                AssertTrue(choices?.messageType == YgoConstants.MSG_SELECT_OPTION && choices.options.Count == 2 &&
                    choices.options.Contains(ownDeck) && choices.options.Contains(opponentDeck),
                    "Tearlaments Kashtira did not offer the two expected deck choices.");
                int[] before = duel.Players.Select(player => player.grave.Count).ToArray();
                duel.SubmitResponseInt(choices.options.IndexOf(option));
                int target = option == ownDeck ? duel.HumanPlayerIndex : duel.AiPlayerIndex;
                AssertTrue(duel.Players[target].grave.Count == before[target] + 3 &&
                    duel.Players[1 - target].grave.Count == before[1 - target] && duel.RetryCount == 0,
                    "Tearlaments Kashtira did not send three cards from the selected deck.");
            }
            Debug.Log("[VERIFY TEARLAMENTS KASHTIRA] Both deck selections sent three cards from the selected deck without retries.");
        }

        private static void VerifyKitkallosDestinationChoice()
        {
            const uint instantFusion = 1845204, kitkallos = 92731385, cryme = 1329620;
            uint[] main = Enumerable.Repeat(instantFusion, 20).Concat(Enumerable.Repeat(cryme, 20)).ToArray();
            uint[] opponent = Enumerable.Repeat(89631139u, 40).ToArray();
            var ai = new TacticalAI();
            foreach (bool toGrave in new[] { false, true })
            {
                using var duel = StartSeedMatching("Kitkallos destination", main, new[] { kitkallos }, opponent,
                    Array.Empty<uint>(), true, session => session.ActivePrompt?.activatable.Any(card => card.code == instantFusion) == true);
                var own = duel.Players[duel.HumanPlayerIndex];
                int handBefore = own.hand.Count(card => card.code == cryme);
                bool submitted = false;
                int activate = duel.ActivePrompt.activatable.FindIndex(card => card.code == instantFusion);
                duel.SubmitResponseInt((activate << 16) | 5);
                DriveUntilIdle(duel, ai, prompt =>
                {
                    switch (prompt.messageType)
                    {
                        case YgoConstants.MSG_SELECT_CHAIN:
                            return BitConverter.GetBytes(prompt.candidates.FindIndex(card => card.code == kitkallos));
                        case YgoConstants.MSG_SELECT_EFFECTYN when prompt.promptCardCode == kitkallos:
                            return BitConverter.GetBytes(1);
                        case YgoConstants.MSG_SELECT_CARD:
                            int index = prompt.candidates.FindIndex(card => card.code is kitkallos or cryme);
                            return index >= 0 ? EncodeSelectIndices(index) : null;
                        case YgoConstants.MSG_SELECT_OPTION:
                            AssertTrue(!submitted, "Kitkallos opened an unexpected second destination choice.");
                            submitted = true;
                            return TestChainChooser.ChooseKitkallosDestination(duel, toGrave);
                        default:
                            return null;
                    }
                });
                AssertTrue(submitted && duel.RetryCount == 0 && own.mzone.Any(card => card?.code == kitkallos) &&
                    own.hand.Count(card => card.code == cryme) == handBefore + (toGrave ? 0 : 1) &&
                    own.grave.Count(card => card.code == cryme) == (toGrave ? 1 : 0),
                    "Kitkallos did not send the chosen card to the destination shown on the selected button.");
            }
            Debug.Log("[VERIFY KITKALLOS CHOICE] Both native options displayed the source and sent the selected card to the shown destination.");
        }

        private static void VerifyArcanaReadingCoinChoice()
        {
            const uint reading = 11819473;
            const uint barrier = 73206827;
            uint[] main = Enumerable.Repeat(reading, 12).Concat(Enumerable.Repeat(barrier, 12))
                .Concat(Enumerable.Repeat(89631139u, 16)).ToArray();
            uint[] opponent = Enumerable.Repeat(89631139u, 40).ToArray();
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();

            foreach (ulong side in new ulong[] { 62, 63 })
            {
                using var duel = StartSeedMatching("Arcana Reading coin choice", main, empty, opponent, empty, true,
                    session =>
                    {
                        DriveUntilIdle(session, ai);
                        var available = session.ActivePrompt?.activatable;
                        return available != null && available.Any(card => card.code == reading) &&
                            available.Any(card => card.code == barrier);
                    });
                int barrierIndex = duel.ActivePrompt.activatable.FindIndex(card => card.code == barrier);
                duel.SubmitResponseInt((barrierIndex << 16) | 5);
                DriveUntilIdle(duel, ai);
                int readingIndex = duel.ActivePrompt.activatable.FindIndex(card => card.code == reading);
                AssertTrue(readingIndex >= 0, "Arcana Reading could not be played after Light Barrier.");
                duel.SubmitResponseInt((readingIndex << 16) | 5);
                if (duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_PLACE)
                    duel.SubmitResponse(ai.ComputeResponse(duel, duel.ActivePrompt));

                var prompt = duel.ActivePrompt;
                AssertTrue(prompt?.messageType == YgoConstants.MSG_SELECT_OPTION && prompt.options.Count == 2 &&
                    prompt.options.Contains(62) && prompt.options.Contains(63) &&
                    CardDatabase.ResolveOptionDescription(62) == "코인 앞면 효과" &&
                    CardDatabase.ResolveOptionDescription(63) == "코인 뒷면 효과",
                    $"Arcana Reading coin choices: prompt {prompt?.messageType}, " +
                    $"options [{(prompt == null ? "" : string.Join(", ", prompt.options))}], " +
                    $"field spell {duel.Players[duel.HumanPlayerIndex].szone[5]?.code}, retries {duel.RetryCount}.");
                duel.SubmitResponseInt(prompt.options.IndexOf(side));
                int expectedChooser = side == 62 ? duel.HumanPlayerIndex : duel.AiPlayerIndex;
                AssertTrue(duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_CARD &&
                    duel.ActivePrompt.playerId == expectedChooser && duel.RetryCount == 0,
                    "Arcana Reading let the wrong player choose a card after selecting a coin side.");
            }
            Debug.Log("[VERIFY ARCANA READING] Light Barrier let each coin effect reach the correct card chooser.");
        }

        private static byte[] VerifyAnnounceCardSelection(DuelSession session, DuelPrompt prompt, uint code)
        {
            var canvas = new GameObject("Card Declaration Test", typeof(Canvas));
            try
            {
                byte[] response = null;
                var modals = new DuelPromptModals(canvas.transform, _ => { }, bytes => response = bytes);
                modals.ShowPrompt(session, prompt);
                var window = modals.ModalRoot.transform.Find("Win");
                var input = window.Find("Body/Search/Inp").GetComponent<InputField>();
                var confirm = window.Find("Footer/Btn_선언 확정/B").GetComponent<Button>();
                var list = window.Find("Body/List");

                Button FindChoice() => list.GetComponentsInChildren<Button>()
                    .SingleOrDefault(button => button.name == $"Dec_{code}");

                confirm.onClick.Invoke();
                AssertTrue(!confirm.interactable && response == null && modals.IsPromptOpen,
                    "Card declaration confirmed a hidden default before selecting a card.");

                input.text = "증식의G";
                var byName = FindChoice();
                AssertTrue(byName != null, "Normalized Korean card name did not return Maxx C.");
                byName.onClick.Invoke();
                AssertTrue(confirm.interactable && response == null, "Selecting Maxx C did not enable explicit confirmation.");

                input.text = "결과 없는 검색어 31415927";
                confirm.onClick.Invoke();
                AssertTrue(FindChoice() == null && !confirm.interactable && response == null &&
                    modals.IsPromptOpen && ReferenceEquals(session.ActivePrompt, prompt),
                    "An empty search retained a stale selection or consumed the declaration prompt.");

                input.text = code.ToString();
                var byPasscode = FindChoice();
                AssertTrue(byPasscode != null && !confirm.interactable,
                    "Exact passcode search did not find the card without selecting it.");
                byPasscode.onClick.Invoke();
                confirm.onClick.Invoke();
                AssertTrue(response != null && response.Length == sizeof(uint) && BitConverter.ToUInt32(response, 0) == code &&
                    !modals.IsPromptOpen && ReferenceEquals(session.ActivePrompt, prompt),
                    "Explicit declaration confirmation did not send the native card passcode.");
                Debug.Log("[VERIFY ANNOUNCE CARD UI] Normalized name and exact passcode search passed; empty queries cannot confirm hidden or stale choices.");
                return response;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        private static void VerifyDeckSortDrawOrder(uint[] opponentMain)
        {
            const uint organization = 96677818;
            const uint potOfGreed = 55144522;
            var ai = new TacticalAI();
            uint[] main = BuildDeck40((organization, 3), (potOfGreed, 3), (46986414, 12), (15025844, 10));
            using var duel = StartSeedMatching("Deck sort draw order", main, Array.Empty<uint>(), opponentMain,
                Array.Empty<uint>(), true, session =>
                {
                    DriveUntilIdle(session, ai);
                    var idle = session.ActivePrompt;
                    int sortSpell = idle?.activatable.FindIndex(card => card.code == organization) ?? -1;
                    if (sortSpell < 0 || !idle.activatable.Any(card => card.code == potOfGreed)) return false;
                    session.SubmitResponseInt((sortSpell << 16) | 5);
                    DriveUntilIdle(session, ai);
                    var sort = session.ActivePrompt;
                    return sort?.messageType == YgoConstants.MSG_SORT_CARD && sort.candidates.Count == 3 &&
                        sort.candidates[0].code != sort.candidates[2].code;
                });

            var prompt = duel.ActivePrompt;
            var expected = new[] { prompt.candidates[2].code, prompt.candidates[0].code };
            var canvas = new GameObject("Deck Sort Draw Test", typeof(Canvas));
            try
            {
                byte[] response = null;
                var modals = new DuelPromptModals(canvas.transform, null, bytes => response = bytes);
                modals.ShowPrompt(duel, prompt);
                var window = modals.ModalRoot.transform.Find("Win");
                var tiles = window.Find("Body/Cards/Viewport/Content");
                Button Earlier(int index) => tiles.Find($"Sort_{index}/Earlier/B").GetComponent<Button>();
                Button Later(int index) => tiles.Find($"Sort_{index}/Later/B").GetComponent<Button>();
                AssertTrue(tiles.Find("Sort_0/Order/Text").GetComponent<Text>().text == "1번 · 맨 위" &&
                    !Earlier(0).interactable && Later(0).interactable &&
                    Earlier(2).transform.Find("Arrow").GetComponent<Image>().sprite != null && !Later(2).interactable,
                    "Deck sort did not identify the first draw or show the available movement controls.");
                Earlier(2).onClick.Invoke();
                Earlier(1).onClick.Invoke();
                window.Find("Footer/Btn_순서 확정/B").GetComponent<Button>().onClick.Invoke();
                AssertTrue(response != null && response.SequenceEqual(new byte[] { 1, 2, 0 }),
                    "Deck sort sent the displayed positions in the wrong native order.");

                duel.SubmitResponse(response);
                DriveUntilIdle(duel, ai);
                int drawSpell = duel.ActivePrompt?.activatable.FindIndex(card => card.code == potOfGreed) ?? -1;
                AssertTrue(drawSpell >= 0, "The draw spell was unavailable after rearranging the deck.");
                IReadOnlyList<uint> drawn = null;
                duel.OnCardsDrawn += (player, codes) => { if (player == duel.HumanPlayerIndex) drawn = codes; };
                duel.SubmitResponseInt((drawSpell << 16) | 5);
                DriveUntilIdle(duel, ai);
                AssertTrue(drawn != null && drawn.SequenceEqual(expected) && duel.RetryCount == 0,
                    "The first two drawn cards did not match the leftmost two cards shown by the sort prompt.");
                Debug.Log("[VERIFY DECK SORT ORDER] Reordered the top cards with arrows and drew them from left to right.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        private static void VerifyOtherCardEffects(uint[] oppDeckMain)
        {
            var ai = new TacticalAI();

            // 0. Bridge & UI validation assertions (MSG_ANNOUNCE_CARD full coverage + MSG_SELECT_SUM mustSelect/overflow)
            uint[] allDeclarable = YgoNativeBridge.FilterDeclarableCards(Array.Empty<ulong>(), 20000);
            AssertTrue(allDeclarable.Length >= 14000, $"Expected >=14000 declarable cards, got {allDeclarable.Length}");
            foreach (uint staple in new uint[] { 14558127, 23434538, 10045474, 89631139 })
                AssertTrue(Array.IndexOf(allDeclarable, staple) >= 0, $"FilterDeclarableCards missing staple {staple}");

            var sumExactPrompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_SUM, min = 1, max = 1, acc = 8, sumExactCountZeroAny = false };
            sumExactPrompt.mustSelect.Add(new SelectableCardItem { sumParam = 4 });
            sumExactPrompt.candidates.Add(new SelectableCardItem { sumParam = 4 });
            sumExactPrompt.candidates.Add(new SelectableCardItem { sumParam = 2 });
            AssertTrue(!DuelPromptModals.CheckSumSelectionValid(sumExactPrompt, new HashSet<int>(), 8), "CheckSumSelectionValid: empty should be invalid");
            AssertTrue(DuelPromptModals.CheckSumSelectionValid(sumExactPrompt, new HashSet<int> { 0 }, 8), "CheckSumSelectionValid: mustSelect+1 candidate (min=1,max=1) should be valid");
            AssertTrue(!DuelPromptModals.CheckSumSelectionValid(sumExactPrompt, new HashSet<int> { 1 }, 8), "CheckSumSelectionValid: 4+2 != 8 should be invalid");
            AssertTrue(!DuelPromptModals.CheckSumSelectionValid(sumExactPrompt, new HashSet<int> { 0, 1 }, 8), "CheckSumSelectionValid: count>max should be invalid");

            var sumOverflowPrompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_SUM, min = 1, max = 1, acc = 8, sumExactCountZeroAny = true };
            sumOverflowPrompt.candidates.Add(new SelectableCardItem { sumParam = 6 });
            sumOverflowPrompt.candidates.Add(new SelectableCardItem { sumParam = 4 });
            sumOverflowPrompt.candidates.Add(new SelectableCardItem { sumParam = 4 });
            AssertTrue(!DuelPromptModals.CheckSumSelectionValid(sumOverflowPrompt, new HashSet<int> { 0 }, 8), "Overflow SUM: 6<8 must be invalid");
            AssertTrue(DuelPromptModals.CheckSumSelectionValid(sumOverflowPrompt, new HashSet<int> { 0, 1 }, 8), "Overflow SUM: 6+4>=8 must be valid");
            AssertTrue(!DuelPromptModals.CheckSumSelectionValid(sumOverflowPrompt, new HashSet<int> { 0, 1, 2 }, 8), "Overflow SUM: 6+4+4 overspend must be invalid");

            // 1. Card Name Declaration: 0-opcode Prohibition (43711255) & filtered Crossout Designator (65681983 -> banish Ash Blossom 14558127)
            uint[] annDeck = BuildDeck40((43711255, 3), (65681983, 3), (14558127, 3), (23434538, 3));
            using (var s = StartSeedMatching("CardAnnounce", annDeck, Array.Empty<uint>(), oppDeckMain, Array.Empty<uint>(), true, sess =>
            {
                DriveUntilIdle(sess, ai);
                var p = sess.ActivePrompt;
                return p != null && p.messageType == YgoConstants.MSG_SELECT_IDLECMD && p.playerId == 0
                    && p.activatable.Any(a => a.code == 43711255) && p.activatable.Any(a => a.code == 65681983)
                    && sess.Players[0].hand.Count(h => h.code == 14558127) < 3;
            }))
            {
                int act1 = s.ActivePrompt.activatable.FindIndex(a => a.code == 43711255);
                s.SubmitResponseInt((act1 << 16) | 5);
                DriveUntilIdle(s, ai, p =>
                {
                    if (p.messageType != YgoConstants.MSG_ANNOUNCE_CARD) return null;
                    uint[] valid = YgoNativeBridge.FilterDeclarableCards(p.options.ToArray(), 20000);
                    AssertTrue(Array.IndexOf(valid, 23434538u) >= 0, "Prohibition (0-opcode) failed to list Maxx C (23434538)");
                    return VerifyAnnounceCardSelection(s, p, 23434538u);
                });
                int act2 = s.ActivePrompt.activatable.FindIndex(a => a.code == 65681983);
                AssertTrue(act2 >= 0, "Crossout Designator not activatable after Prohibition");
                s.SubmitResponseInt((act2 << 16) | 5);
                DriveUntilIdle(s, ai, p =>
                {
                    if (p.messageType == YgoConstants.MSG_ANNOUNCE_CARD)
                    {
                        uint[] valid = YgoNativeBridge.FilterDeclarableCards(p.options.ToArray(), 20000);
                        AssertTrue(Array.IndexOf(valid, 14558127u) >= 0, "Crossout Designator failed to list Ash Blossom (14558127)");
                        return BitConverter.GetBytes((int)14558127);
                    }
                    if (p.messageType == YgoConstants.MSG_SELECT_CARD)
                        return EncodeSelectIndices(0);
                    return null;
                });
                AssertTrue(s.RetryCount == 0 && s.Players[0].szone.Any(c => c != null && c.code == 43711255) && s.Players[0].banished.Any(c => c.code == 14558127),
                    "CardNameAnnounce effects did not resolve properly");
            }

            // 2. Chain Negation (Hand Trap): ROTA (32807846) negated by P1 Ash Blossom (14558127)
            uint[] chainDeck0 = BuildDeck40((32807846, 3), (40044918, 3));
            uint[] chainDeck1 = BuildDeck40((14558127, 3));
            using (var s = StartSeedMatching("ChainNegate", chainDeck0, Array.Empty<uint>(), chainDeck1, Array.Empty<uint>(), true, sess =>
            {
                DriveUntilIdle(sess, ai);
                var p = sess.ActivePrompt;
                if (p == null || p.messageType != YgoConstants.MSG_SELECT_IDLECMD || p.playerId != 0) return false;
                if (!sess.Players[1].hand.Any(h => h.code == 14558127)) return false;
                int act = p.activatable.FindIndex(a => a.code == 32807846);
                if (act < 0) return false;
                sess.SubmitResponseInt((act << 16) | 5);
                for (int step = 0; step < 8 && !sess.IsFinished; step++)
                {
                    if (sess.ActivePrompt == null) { sess.StepUntilPrompt(); continue; }
                    var cp = sess.ActivePrompt;
                    if (cp.messageType == YgoConstants.MSG_SELECT_PLACE || cp.messageType == YgoConstants.MSG_SELECT_POSITION)
                        sess.SubmitResponse(ai.ComputeResponse(sess, cp));
                    else if (cp.messageType == YgoConstants.MSG_SELECT_CHAIN && cp.playerId == 1 && cp.candidates.Any(c => c.code == 14558127))
                        return true;
                    else
                        return false;
                }
                return false;
            }))
            {
                int handBefore = s.Players[0].hand.Count(c => c.code == 40044918);
                int ashIdx = s.ActivePrompt.candidates.FindIndex(c => c.code == 14558127);
                s.SubmitResponseInt(ashIdx);
                DriveUntilIdle(s, ai);
                AssertTrue(s.RetryCount == 0 && s.Players[0].hand.Count(c => c.code == 40044918) == handBefore,
                    "Ash Blossom did not prevent ROTA search");
                AssertTrue(s.Logs.Any(l => l.Contains("체인 2") && l.Contains("하루 우라라")) && s.Logs.Any(l => l.Contains("체인 1 무효화")),
                    "Missing Chain 2 Ash Blossom / Chain 1 Negated logs");
                AssertTrue(s.TurnInterruptions > 0, "Ash Blossom did not interrupt the current turn's plan.");
            }

            // 3. Field targeting (Mystical Space Typhoon), targeted banish (Cosmic Cyclone) and mass removal
            uint[] oppFieldDeck = BuildDeck40((20721928, 3), (4148264, 3), (5464695, 3), (7359741, 3), (7459013, 3),
                (5318639, 3), (55144522, 3), (24094653, 3), (40619825, 3), (81439173, 3), (83764718, 3), (37812118, 3));
            uint[] removalDeck = BuildDeck40((5318639, 3), (8267140, 3), (18144506, 3), (12580477, 3));
            using (var s = StartSeedMatching("DestroyBanish", removalDeck, Array.Empty<uint>(), oppFieldDeck, Array.Empty<uint>(), true, sess =>
            {
                // P0 passes Turn 1; P1 Turn 2 summons 1 monster & sets 3 S/T -> P0 Turn 3 resolves all 4 removals
                DriveUntilIdle(sess, ai);
                if (sess.ActivePrompt == null || sess.ActivePrompt.playerId != 0) return false;
                sess.SubmitResponseInt(7); // End Turn 1
                DriveUntilIdle(sess, ai);
                if (sess.ActivePrompt == null || sess.ActivePrompt.playerId != 1 || sess.ActivePrompt.summonable.Count == 0 || sess.ActivePrompt.sSetable.Count == 0) return false;
                sess.SubmitResponseInt(0); // Summon first monster
                DriveUntilIdle(sess, ai);
                for (int k = 0; k < 3; k++)
                {
                    if (sess.ActivePrompt == null || sess.ActivePrompt.playerId != 1 || sess.ActivePrompt.sSetable.Count == 0) return false;
                    sess.SubmitResponseInt(4); // Set S/T
                    DriveUntilIdle(sess, ai);
                }
                if (sess.ActivePrompt == null || sess.ActivePrompt.playerId != 1 || !sess.ActivePrompt.canEp) return false;
                sess.SubmitResponseInt(7); // End Turn 2
                DriveUntilIdle(sess, ai);
                var p0 = sess.ActivePrompt;
                return p0 != null && p0.messageType == YgoConstants.MSG_SELECT_IDLECMD && p0.playerId == 0
                    && p0.activatable.Any(a => a.code == 5318639) && p0.activatable.Any(a => a.code == 8267140)
                    && p0.activatable.Any(a => a.code == 18144506) && p0.activatable.Any(a => a.code == 12580477);
            }))
            {
                int destroyedSequence = -1;
                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 5318639) << 16) | 5);
                DriveUntilIdle(s, ai, p =>
                {
                    if (p.messageType != YgoConstants.MSG_SELECT_CARD) return null;
                    AssertTrue(s.LastHintMsg == 502, "Mystical Space Typhoon did not issue a destroy target prompt");
                    byte[] response = TestDuelZoneBrowser.SelectFieldTarget(s, p);
                    destroyedSequence = (int)p.candidates[1].sequence;
                    return response;
                });
                AssertTrue(destroyedSequence >= 0 && s.Players[1].szone[destroyedSequence] == null &&
                    s.Players[1].szone.Count(card => card != null) == 2 && s.RetryCount == 0,
                    "Confirming the field target did not destroy the chosen card");

                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 8267140) << 16) | 5);
                DriveUntilIdle(s, ai, p => p.messageType == YgoConstants.MSG_SELECT_CARD ? EncodeSelectIndices(0) : null);
                AssertTrue(s.Players[0].lp == 7000 && s.Players[1].banished.Count == 1, $"Cosmic Cyclone failed: LP={s.Players[0].lp}, oppBanished={s.Players[1].banished.Count}");

                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 18144506) << 16) | 5);
                DriveUntilIdle(s, ai);
                AssertTrue(s.Players[1].szone.All(c => c == null) && s.Players[1].grave.Count >= 1, "Harpie's Feather Duster failed to wipe opp S/T");

                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 12580477) << 16) | 5);
                DriveUntilIdle(s, ai);
                AssertTrue(s.RetryCount == 0 && s.Players[1].mzone.All(c => c == null), "Raigeki failed to wipe opp Monster");
            }

            // 4a. Equip (+1000 ATK + MSG_EQUIP), Draw (Pot of Greed), Counter (Royal Magical Library)
            uint[] counterDeck = BuildDeck40((70791313, 3), (40619825, 3), (55144522, 3), (37812118, 3), (98645731, 3), (81439173, 3), (20721928, 3));
            using (var s = StartSeedMatching("EquipCounter", counterDeck, Array.Empty<uint>(), oppDeckMain, Array.Empty<uint>(), true, sess =>
            {
                DriveUntilIdle(sess, ai);
                var p = sess.ActivePrompt;
                return p != null && p.messageType == YgoConstants.MSG_SELECT_IDLECMD && p.playerId == 0
                    && p.summonable.Any(m => m.code == 70791313) && sess.Players[0].hand.Any(h => h.code == 40619825)
                    && p.activatable.Any(a => a.code == 55144522) && p.activatable.Count >= 2;
            }))
            {
                s.SubmitResponseInt((s.ActivePrompt.summonable.FindIndex(m => m.code == 70791313) << 16) | 0);
                DriveUntilIdle(s, ai);
                var lib = s.Players[0].mzone.FirstOrDefault(c => c != null && c.code == 70791313);
                AssertTrue(lib != null && lib.attack == 0, "Royal Magical Library summon failed");

                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 55144522) << 16) | 5);
                DriveUntilIdle(s, ai);
                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 40619825) << 16) | 5);
                DriveUntilIdle(s, ai, p => p.messageType == YgoConstants.MSG_SELECT_CARD ? EncodeSelectIndices(0) : null);

                // Activate 3rd Spell (Cup of Ace / Pot of Greed / Pot of Duality / Foolish Burial) for 3rd Spell Counter
                uint[] thirdCandidates = { 37812118, 55144522, 98645731, 81439173 };
                int act3 = s.ActivePrompt.activatable.FindIndex(a => Array.IndexOf(thirdCandidates, a.code) >= 0);
                AssertTrue(act3 >= 0, "No 3rd activatable Spell found after Pot of Greed draw");
                s.SubmitResponseInt((act3 << 16) | 5);
                DriveUntilIdle(s, ai, p => p.messageType == YgoConstants.MSG_SELECT_CARD ? EncodeSelectIndices(0) : null);

                lib = s.Players[0].mzone.FirstOrDefault(c => c != null && c.code == 70791313);
                AssertTrue(lib != null && lib.attack == lib.baseAttack + 1000, $"Axe of Despair ATK boost failed: {lib?.attack}");
                AssertTrue(s.Logs.Any(l => l.Contains("장착:")), "Missing MSG_EQUIP log");

                int handBeforeLib = s.Players[0].hand.Count;
                int libAct = s.ActivePrompt.activatable.FindIndex(a => a.code == 70791313 && a.location == YgoConstants.LOCATION_MZONE);
                AssertTrue(libAct >= 0, "Royal Magical Library 3-counter draw effect not activatable");
                s.SubmitResponseInt((libAct << 16) | 5);
                DriveUntilIdle(s, ai);
                AssertTrue(s.RetryCount == 0 && s.Players[0].hand.Count == handBeforeLib + 1, "Royal Magical Library failed to remove 3 counters & draw");
            }

            // 4b. Decktop Confirm (Pot of Duality 98645731) + Coin Toss (Cup of Ace 37812118) + Dice Toss (Dicephoon 3493058) + Tokens (Scapegoat 73915051)
            uint[] ddtDeck = BuildDeck40((98645731, 3), (37812118, 3), (3493058, 3));
            using (var s = StartSeedMatching("DeckTopCoinDice", ddtDeck, Array.Empty<uint>(), oppDeckMain, Array.Empty<uint>(), true, sess =>
            {
                DriveUntilIdle(sess, ai);
                var p = sess.ActivePrompt;
                return p != null && p.messageType == YgoConstants.MSG_SELECT_IDLECMD && p.playerId == 0
                    && p.activatable.Any(a => a.code == 98645731) && p.activatable.Any(a => a.code == 37812118) && p.activatable.Any(a => a.code == 3493058);
            }))
            {
                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 98645731) << 16) | 5);
                DriveUntilIdle(s, ai, p => p.messageType == YgoConstants.MSG_SELECT_CARD ? EncodeSelectIndices(0) : null);
                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 37812118) << 16) | 5);
                DriveUntilIdle(s, ai);
                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 3493058) << 16) | 5);
                DriveUntilIdle(s, ai, p => p.messageType == YgoConstants.MSG_SELECT_CARD ? EncodeSelectIndices(0) : null);
                AssertTrue(s.RetryCount == 0 && s.Logs.Any(l => l.Contains("덱 위 확인:")) && s.Logs.Any(l => l.Contains("코인 토스:")) && s.Logs.Any(l => l.Contains("주사위:")),
                    "Missing Decktop Confirm, Coin Toss, or Dice Toss log");
            }

            uint[] tokenDeck = BuildDeck40((73915051, 3));
            using (var s = StartSeedMatching("ScapegoatTokens", tokenDeck, Array.Empty<uint>(), oppDeckMain, Array.Empty<uint>(), true, sess =>
            {
                DriveUntilIdle(sess, ai);
                return sess.ActivePrompt?.activatable.Any(a => a.code == 73915051) == true;
            }))
            {
                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 73915051) << 16) | 5);
                DriveUntilIdle(s, ai);
                int tokCnt = s.Players[0].mzone.Count(c => c != null && c.code >= 73915052 && c.code <= 73915055);
                AssertTrue(s.RetryCount == 0 && tokCnt == 4, $"Scapegoat expected 4 sheep tokens (73915052..73915055), got {tokCnt}");
            }

            // 5. Foolish Burial (81439173) -> Monster Reborn (83764718, POS) -> Tribute Summon Summoned Skull (70781052, MSG_SELECT_TRIBUTE)
            uint[] rebDeck = BuildDeck40((81439173, 3), (83764718, 3), (70781052, 3), (20721928, 3));
            using (var s = StartSeedMatching("RebornTribute", rebDeck, Array.Empty<uint>(), oppDeckMain, Array.Empty<uint>(), true, sess =>
            {
                DriveUntilIdle(sess, ai);
                var p = sess.ActivePrompt;
                return p != null && p.messageType == YgoConstants.MSG_SELECT_IDLECMD && p.playerId == 0
                    && p.activatable.Any(a => a.code == 81439173) && sess.Players[0].hand.Any(h => h.code == 83764718)
                    && sess.Players[0].hand.Any(h => h.code == 70781052) && sess.Players[0].hand.Count(h => h.code == 20721928) < 3;
            }))
            {
                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 81439173) << 16) | 5);
                DriveUntilIdle(s, ai, p => p.messageType == YgoConstants.MSG_SELECT_CARD
                    ? EncodeSelectIndices(Math.Max(0, p.candidates.FindIndex(c => c.code == 20721928))) : null);
                AssertTrue(s.Players[0].grave.Any(c => c.code == 20721928), "Foolish Burial failed to send monster to GY");

                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 83764718) << 16) | 5);
                bool sawPos = false;
                DriveUntilIdle(s, ai, p =>
                {
                    if (p.messageType == YgoConstants.MSG_SELECT_CARD) return EncodeSelectIndices(Math.Max(0, p.candidates.FindIndex(c => c.code == 20721928)));
                    if (p.messageType == YgoConstants.MSG_SELECT_POSITION) { sawPos = true; return BitConverter.GetBytes((int)YgoConstants.POS_FACEUP_ATTACK); }
                    return null;
                });
                AssertTrue(sawPos && s.Players[0].mzone.Any(c => c != null && c.code == 20721928), "Monster Reborn failed to revive monster");

                int tribSummon = s.ActivePrompt.summonable.FindIndex(m => m.code == 70781052);
                AssertTrue(tribSummon >= 0, "Summoned Skull not Tribute Summonable");
                bool sawTrib = false;
                s.SubmitResponseInt((tribSummon << 16) | 0);
                DriveUntilIdle(s, ai, p =>
                {
                    if (p.messageType == YgoConstants.MSG_SELECT_TRIBUTE) { sawTrib = true; return EncodeSelectIndices(0); }
                    return null;
                });
                AssertTrue(sawTrib && s.RetryCount == 0 && s.Players[0].mzone.Any(c => c != null && c.code == 70781052),
                    "Tribute Summon Summoned Skull failed");
            }

            // 6a. Ritual Overflow MSG_SELECT_SUM (Machine Angel Ritual 39996157 -> Cyber Angel Benten 77235086 using Lv8 Blue-Eyes)
            uint[] ritDeck = BuildDeck40((39996157, 3), (77235086, 3));
            using (var s = StartSeedMatching("RitualSum", ritDeck, Array.Empty<uint>(), oppDeckMain, Array.Empty<uint>(), true, sess =>
            {
                DriveUntilIdle(sess, ai);
                var p = sess.ActivePrompt;
                return p != null && p.activatable.Any(a => a.code == 39996157) && sess.Players[0].hand.Any(h => h.code == 77235086)
                    && sess.Players[0].hand.Any(h => h.code == 89631139);
            }))
            {
                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 39996157) << 16) | 5);
                bool sawRitualSum = false;
                DriveUntilIdle(s, ai, p =>
                {
                    if (p.messageType == YgoConstants.MSG_SELECT_CARD) return EncodeSelectIndices(0);
                    if (p.messageType == YgoConstants.MSG_SELECT_SUM)
                    {
                        sawRitualSum = true;
                        AssertTrue(p.sumExactCountZeroAny, "Expected sumExactCountZeroAny=true for Machine Angel Ritual");
                        AssertTrue(DuelPromptModals.CheckSumSelectionValid(p, new HashSet<int> { 0 }, (int)(p.acc & 0xFFFF)),
                            "UI CheckSumSelectionValid disagreed with valid overflow Ritual tribute");
                        return EncodeSelectIndices(0);
                    }
                    return null;
                });
                AssertTrue(sawRitualSum && s.RetryCount == 0 && s.Players[0].mzone.Any(c => c != null && c.code == 77235086),
                    "Ritual Summon Cyber Angel Benten failed");
            }

            // 6b. Synchro Exact MSG_SELECT_SUM (Angel Trumpeter Lv4 Tuner 87979586 + Double Summon 43422537 + Lv4 -> Stardust Dragon 44508094)
            uint[] syncDeck = BuildDeck40((87979586, 3), (43422537, 3), (4148264, 3), (5464695, 3), (7359741, 3), (7459013, 3), (20721928, 3));
            uint[] syncExtra = { 44508094 };
            using (var s = StartSeedMatching("SynchroSum", syncDeck, syncExtra, oppDeckMain, Array.Empty<uint>(), true, sess =>
            {
                DriveUntilIdle(sess, ai);
                var p = sess.ActivePrompt;
                return p != null && p.summonable.Any(m => m.code == 87979586) && p.activatable.Any(a => a.code == 43422537)
                    && p.summonable.Any(m => m.code != 87979586 && m.code != 43422537);
            }))
            {
                s.SubmitResponseInt((s.ActivePrompt.summonable.FindIndex(m => m.code == 87979586) << 16) | 0);
                DriveUntilIdle(s, ai);
                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 43422537) << 16) | 5);
                DriveUntilIdle(s, ai);
                s.SubmitResponseInt((s.ActivePrompt.summonable.FindIndex(m => m.code != 87979586) << 16) | 0);
                DriveUntilIdle(s, ai);

                int spSyn = s.ActivePrompt.spSummonable.FindIndex(m => m.code == 44508094);
                AssertTrue(spSyn >= 0, "Stardust Dragon not Synchro Summonable");
                bool sawSyncMatPrompt = false;
                s.SubmitResponseInt((spSyn << 16) | 1);
                DriveUntilIdle(s, ai, p =>
                {
                    if (p.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD)
                    {
                        sawSyncMatPrompt = true;
                        if (p.finishable && (p.unselectCandidates.Count > 0 || p.candidates.Count == 0))
                            return BitConverter.GetBytes(-1);
                        if (p.candidates.Count > 0)
                        {
                            byte[] resp = new byte[8];
                            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, resp, 0, 4);
                            Buffer.BlockCopy(BitConverter.GetBytes(0), 0, resp, 4, 4);
                            return resp;
                        }
                        return BitConverter.GetBytes(-1);
                    }
                    if (p.messageType == YgoConstants.MSG_SELECT_SUM)
                    {
                        sawSyncMatPrompt = true;
                        var chosen = new HashSet<int>();
                        for (int i = 0; i < p.candidates.Count && chosen.Count < Math.Max(1, (int)p.min); i++) chosen.Add(i);
                        AssertTrue(DuelPromptModals.CheckSumSelectionValid(p, chosen, (int)(p.acc & 0xFFFF)),
                            "UI CheckSumSelectionValid disagreed with valid Synchro Sum selection");
                        return EncodeSelectIndices(chosen.ToArray());
                    }
                    return null;
                });
                AssertTrue(sawSyncMatPrompt && s.RetryCount == 0 && s.Players[0].mzone.Any(c => c != null && c.code == 44508094),
                    $"Synchro Summon Stardust Dragon failed (sawMat={sawSyncMatPrompt}, retry={s.RetryCount}, synZone={s.Players[0].mzone.Any(c => c != null && c.code == 44508094)})");
            }

            // 6b-2. Exact MSG_SELECT_SUM (Tokusano Shinkyojin 50357013: exact sum Lv4+Lv6=10, sumExactCountZeroAny=false)
            uint[] exactSumDeck = BuildDeck40((50357013, 3), (20721928, 3), (70781052, 3));
            using (var s = StartSeedMatching("ExactSumSelect", exactSumDeck, Array.Empty<uint>(), oppDeckMain, Array.Empty<uint>(), true, sess =>
            {
                DriveUntilIdle(sess, ai);
                var p = sess.ActivePrompt;
                return p != null && p.activatable.Any(a => a.code == 50357013)
                    && sess.Players[0].hand.Any(h => h.code == 20721928) && sess.Players[0].hand.Any(h => h.code == 70781052);
            }))
            {
                int handBefore = s.Players[0].hand.Count;
                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 50357013) << 16) | 5);
                bool sawExactSum = false;
                DriveUntilIdle(s, ai, p =>
                {
                    if (p.messageType == YgoConstants.MSG_SELECT_SUM)
                    {
                        sawExactSum = true;
                        AssertTrue(!p.sumExactCountZeroAny && (p.acc & 0xFFFF) == 10,
                            $"Expected exact sum acc=10, got overflow={p.sumExactCountZeroAny}, acc={p.acc & 0xFFFF}");
                        int idx4 = p.candidates.FindIndex(c => c.code == 20721928);
                        int idx6 = p.candidates.FindIndex(c => c.code == 70781052);
                        AssertTrue(idx4 >= 0 && idx6 >= 0, "Expected Lv4 and Lv6 candidates in MSG_SELECT_SUM");
                        AssertTrue(!DuelPromptModals.CheckSumSelectionValid(p, new HashSet<int> { idx4 }, 10), "Lv4 alone should fail acc=10");
                        AssertTrue(DuelPromptModals.CheckSumSelectionValid(p, new HashSet<int> { idx4, idx6 }, 10), "Lv4+Lv6 should pass acc=10");
                        return EncodeSelectIndices(idx4, idx6);
                    }
                    return null;
                });
                // Cost sent 2 monsters + activated 1 spell (-3), resolved Draw 2 (+2) => net handBefore - 1; 2 monsters in GY
                AssertTrue(sawExactSum && s.RetryCount == 0 && s.Players[0].hand.Count == handBefore - 1
                    && s.Players[0].grave.Any(g => g.code == 20721928) && s.Players[0].grave.Any(g => g.code == 70781052),
                    $"Tokusano Shinkyojin exact MSG_SELECT_SUM failed (saw={sawExactSum}, retry={s.RetryCount}, hand={handBefore}->{s.Players[0].hand.Count})");
            }

            // 6c. Xyz Summon + Overlay Detach (Gagaga Cowboy 12014404 Defense Mode burn -800 LP)
            uint[] xyzDeck = BuildDeck40((43422537, 3), (4148264, 3), (5464695, 3), (7359741, 3), (7459013, 3), (20721928, 3));
            uint[] xyzExtra = { 12014404 };
            using (var s = StartSeedMatching("XyzDetach", xyzDeck, xyzExtra, oppDeckMain, Array.Empty<uint>(), true, sess =>
            {
                DriveUntilIdle(sess, ai);
                var p = sess.ActivePrompt;
                return p != null && p.activatable.Any(a => a.code == 43422537) && p.summonable.Count >= 2;
            }))
            {
                s.SubmitResponseInt((0 << 16) | 0);
                DriveUntilIdle(s, ai);
                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 43422537) << 16) | 5);
                DriveUntilIdle(s, ai);
                s.SubmitResponseInt((0 << 16) | 0);
                DriveUntilIdle(s, ai);

                int spXyz = s.ActivePrompt.spSummonable.FindIndex(m => m.code == 12014404);
                AssertTrue(spXyz >= 0, "Gagaga Cowboy not Xyz Summonable");
                s.SubmitResponseInt((spXyz << 16) | 1);
                DriveUntilIdle(s, ai, p =>
                {
                    if (p.messageType == YgoConstants.MSG_SELECT_CARD) return EncodeSelectIndices(0, 1);
                    if (p.messageType == YgoConstants.MSG_SELECT_POSITION) return BitConverter.GetBytes((int)YgoConstants.POS_FACEUP_DEFENSE);
                    return null;
                });

                var xyz = s.Players[0].mzone.FirstOrDefault(c => c != null && c.code == 12014404);
                AssertTrue(xyz != null && xyz.overlayCodes.Count == 2, $"Expected 2 overlay materials on Gagaga Cowboy, got {xyz?.overlayCodes.Count}");

                int actXyz = s.ActivePrompt.activatable.FindIndex(a => a.code == 12014404);
                AssertTrue(actXyz >= 0, "Gagaga Cowboy detach effect not activatable");
                s.SubmitResponseInt((actXyz << 16) | 5);
                DriveUntilIdle(s, ai, p => p.messageType == YgoConstants.MSG_SELECT_CARD ? EncodeSelectIndices(0) : null);

                xyz = s.Players[0].mzone.FirstOrDefault(c => c != null && c.code == 12014404);
                AssertTrue(s.RetryCount == 0 && xyz != null && xyz.overlayCodes.Count == 1 && s.Players[1].lp == 7200,
                    $"Gagaga Cowboy detach failed: overlays={xyz?.overlayCodes.Count}, oppLP={s.Players[1].lp}");
            }

            // 6d. Fusion Summon (Polymerization 24094653 -> E-HERO Flame Wingman 35809262)
            uint[] fusDeck = BuildDeck40((24094653, 3), (21844576, 3), (58932615, 3));
            uint[] fusExtra = { 35809262 };
            using (var s = StartSeedMatching("FusionSummon", fusDeck, fusExtra, oppDeckMain, Array.Empty<uint>(), true, sess =>
            {
                DriveUntilIdle(sess, ai);
                var p = sess.ActivePrompt;
                return p != null && p.activatable.Any(a => a.code == 24094653)
                    && sess.Players[0].hand.Any(h => h.code == 21844576) && sess.Players[0].hand.Any(h => h.code == 58932615);
            }))
            {
                s.SubmitResponseInt((s.ActivePrompt.activatable.FindIndex(a => a.code == 24094653) << 16) | 5);
                DriveUntilIdle(s, ai, p =>
                {
                    if (p.messageType == YgoConstants.MSG_SELECT_CARD)
                        return p.min <= 1 ? EncodeSelectIndices(0) : EncodeSelectIndices(0, 1);
                    return null;
                });
                AssertTrue(s.RetryCount == 0 && s.Players[0].mzone.Any(c => c != null && c.code == 35809262), "Fusion Summon Flame Wingman failed");
            }

            // 7a. DNA Surgery (74701381) Race Announce -> RACE_ILLUSION (0x2000000) transforms Warrior into Illusion
            uint[] dnaDeck = BuildDeck40((74701381, 3), (20721928, 3), (5265750, 3));
            using (var s = StartSeedMatching("DnaIllusion", dnaDeck, Array.Empty<uint>(), oppDeckMain, Array.Empty<uint>(), true, sess =>
            {
                DriveUntilIdle(sess, ai);
                var p = sess.ActivePrompt;
                return p != null && p.sSetable.Any(a => a.code == 74701381) && p.summonable.Count >= 1;
            }))
            {
                s.SubmitResponseInt((0 << 16) | 0); // Normal Summon Warrior
                DriveUntilIdle(s, ai);
                s.SubmitResponseInt((s.ActivePrompt.sSetable.FindIndex(a => a.code == 74701381) << 16) | 4);
                DriveUntilIdle(s, ai);
                s.SubmitResponseInt(7); // End Turn -> P1 Turn 2 Draw triggers DNA Surgery chain
                bool sawRaceAnnounce = false;
                DriveUntilIdle(s, ai, p =>
                {
                    if (p.messageType == YgoConstants.MSG_SELECT_CHAIN && p.playerId == 0 && p.candidates.Any(c => c.code == 74701381))
                        return BitConverter.GetBytes(p.candidates.FindIndex(c => c.code == 74701381));
                    if (p.messageType == YgoConstants.MSG_ANNOUNCE_RACE)
                    {
                        sawRaceAnnounce = true;
                        AssertTrue((p.announceMask & YgoConstants.RACE_ILLUSION) != 0, "DNA Surgery announceMask missing RACE_ILLUSION");
                        return BitConverter.GetBytes(YgoConstants.RACE_ILLUSION);
                    }
                    return null;
                });
                var transformed = s.Players[0].mzone.FirstOrDefault(c => c != null);
                AssertTrue(sawRaceAnnounce && s.RetryCount == 0 && transformed != null && transformed.race == YgoConstants.RACE_ILLUSION,
                    $"DNA Surgery failed to change monster race to RACE_ILLUSION (got {transformed?.race})");
            }

            // 7b. Rock-Paper-Scissors Trap: Transmission Gear (58297729) -> MSG_ROCK_PAPER_SCISSORS & MSG_HAND_RES
            uint[] rpsDeck0 = BuildDeck40((58297729, 3), (4148264, 3), (5464695, 3), (7359741, 3), (7459013, 3));
            uint[] rpsDeck1 = BuildDeck40((20721928, 3), (5265750, 3), (5053103, 3), (2311603, 3), (5388481, 3));
            using (var s = StartSeedMatching("RockPaperScissors", rpsDeck0, Array.Empty<uint>(), rpsDeck1, Array.Empty<uint>(), true, sess =>
            {
                // P0 Turn 1 sets Transmission Gear & summons attacker; wait for P1 Turn 2 monster summon, then P0 Turn 3 attacks
                DriveUntilIdle(sess, ai);
                var p0 = sess.ActivePrompt;
                return p0 != null && p0.messageType == YgoConstants.MSG_SELECT_IDLECMD && p0.playerId == 0
                    && p0.sSetable.Any(a => a.code == 58297729) && p0.summonable.Any(m => m.code != 89631139);
            }))
            {
                s.SubmitResponseInt((s.ActivePrompt.sSetable.FindIndex(a => a.code == 58297729) << 16) | 4);
                DriveUntilIdle(s, ai);
                s.SubmitResponseInt((s.ActivePrompt.summonable.FindIndex(m => m.code != 89631139) << 16) | 0);
                DriveUntilIdle(s, ai);
                s.SubmitResponseInt(7); // End Turn 1 -> P1 Turn 2
                bool p1Summoned = false, p0Rps = false, p1Rps = false;
                int turnGuard = 0;
                while (!s.IsFinished && turnGuard++ < 80)
                {
                    if (s.ActivePrompt == null) { s.StepUntilPrompt(); continue; }
                    var p = s.ActivePrompt;
                    if (p.messageType == YgoConstants.MSG_ROCK_PAPER_SCISSORS)
                    {
                        if (p.playerId == 0) { p0Rps = true; s.SubmitResponseInt(2); } // 바위
                        else { p1Rps = true; s.SubmitResponseInt(1); } // 가위
                        if (p0Rps && p1Rps) { DriveUntilIdle(s, ai); break; }
                    }
                    else if (p.messageType == YgoConstants.MSG_SELECT_CHAIN && p.playerId == 0 && p.candidates.Any(c => c.code == 58297729))
                        s.SubmitResponseInt(p.candidates.FindIndex(c => c.code == 58297729));
                    else if (p.messageType == YgoConstants.MSG_SELECT_IDLECMD && p.playerId == 1 && !p1Summoned && p.summonable.Any(m => m.code != 89631139))
                    {
                        p1Summoned = true;
                        s.SubmitResponseInt((p.summonable.FindIndex(m => m.code != 89631139) << 16) | 0);
                    }
                    else if (p.messageType == YgoConstants.MSG_SELECT_IDLECMD && p.playerId == 0 && p.canBp && p1Summoned)
                        s.SubmitResponseInt(6); // Enter Battle Phase
                    else if (p.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                        s.SubmitResponseInt(7); // End Turn
                    else if (p.messageType == YgoConstants.MSG_SELECT_BATTLECMD && p.playerId == 0 && p.attackable.Count > 0)
                        s.SubmitResponseInt(1); // Attack!
                    else if (p.messageType == YgoConstants.MSG_SELECT_BATTLECMD)
                        s.SubmitResponseInt(3); // End Battle
                    else if (p.messageType == YgoConstants.MSG_SELECT_CHAIN)
                        s.SubmitResponseInt(-1);
                    else
                        s.SubmitResponse(ai.ComputeResponse(s, p));
                }
                AssertTrue(p0Rps && p1Rps && s.RetryCount == 0 && s.Players[1].banished.Count >= 1,
                    $"Transmission Gear RPS failed: p0={p0Rps}, p1={p1Rps}, oppBanished={s.Players[1].banished.Count}");
                AssertTrue(s.Logs.Any(l => l.Contains("가위바위보 결과: P0(바위) vs P1(가위)")), "Missing MSG_HAND_RES log");
            }

            Debug.Log("[VERIFY OTHER EFFECTS] All 15 non-search effect & UI prompt suites passed (Bridge+SumUI, CardAnnounce, ChainNegate, DestroyBanish, Equip+Coin+Counter, DeckTop+Dice, ScapegoatTokens, Reborn+Tribute, RitualOverflowSUM, SynchroUnselect, ExactSumSelect, XyzDetach, Fusion, DNA-IllusionRace, RockPaperScissors) with 0 retries.");
        }

        private static void VerifyPossessedDestiny(uint[] opponentMain)
        {
            const uint destiny = 45508030;
            const uint aussa = 31887905;
            var main = BuildDeck40((destiny, 14), (aussa, 26));
            var empty = Array.Empty<uint>();
            var ai = new TacticalAI();
            using var duel = StartSeedMatching("Possessed Destiny", main, empty, opponentMain, empty, true, session =>
                session.ActivePrompt?.summonable.Any(c => c.code == aussa) == true &&
                session.Players[0].hand.Any(c => c.code == destiny) &&
                session.Players[0].hand.Count(c => c.code == aussa) >= 2);

            int normal = duel.ActivePrompt.summonable.FindIndex(c => c.code == aussa);
            duel.SubmitResponseInt(normal << 16);
            DriveUntilIdle(duel, ai);
            int activate = duel.ActivePrompt?.activatable.FindIndex(c => c.code == destiny) ?? -1;
            AssertTrue(activate >= 0, "Possessed Destiny is unavailable with a matching-Attribute monster");
            duel.SubmitResponseInt((activate << 16) | 5);
            DriveUntilIdle(duel, ai, prompt =>
            {
                if (prompt.messageType == YgoConstants.MSG_SELECT_CARD)
                {
                    int target = prompt.candidates.FindIndex(c => c.code == aussa);
                    AssertTrue(target >= 0, "Possessed Destiny did not offer the matching-Attribute monster");
                    return EncodeSelectIndices(target);
                }
                if (prompt.messageType == YgoConstants.MSG_SELECT_POSITION)
                {
                    AssertTrue((prompt.allowedPositions & YgoConstants.POS_FACEUP_ATTACK) != 0 &&
                               (prompt.allowedPositions & YgoConstants.POS_FACEDOWN_DEFENSE) != 0,
                        "Possessed Destiny did not offer both legal Special Summon positions");
                    return BitConverter.GetBytes(YgoConstants.POS_FACEUP_ATTACK);
                }
                return null;
            });
            AssertTrue(duel.RetryCount == 0 && duel.Players[0].mzone.Count(c => c != null && c.code == aussa) == 2,
                "Possessed Destiny failed to Special Summon to the activating player's field");
            Debug.Log("[VERIFY POSSESSED DESTINY] Matching-Attribute Special Summon completed without retries.");
        }

        private static void VerifyRainbowDarkDragonMatch()
        {
            var rainbow = DeckRepository.PresetDecks.Single(deck => deck.id == 340);
            var earthbound = DeckRepository.PresetDecks.Single(deck => deck.id == 36);
            ulong[] seed = { 31157, 114085, 128029, 241025 };
            using var session = new DuelSession();
            var players = new[] { new TacticalAI(), new TacticalAI() };
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            AssertTrue(session.StartNewDuel(rainbow.main, rainbow.extra, earthbound.main, earthbound.extra, true, 8000, seed),
                "Could not start the Rainbow Dark Dragon regression duel");
            for (int step = 0; !session.IsFinished && step < 650; step++)
            {
                var prompt = session.ActivePrompt;
                if (prompt == null)
                    session.StepUntilPrompt();
                else
                    session.SubmitResponse(players[prompt.playerId].ComputeResponse(session, prompt));
                AssertTrue(elapsed.Elapsed.TotalSeconds < 12,
                    $"Rainbow Dark Dragon match spent {elapsed.Elapsed.TotalSeconds:F1}s by turn {session.TurnNumber}");
            }
            AssertTrue(session.IsFinished && session.RetryCount == 0, "Rainbow Dark Dragon regression duel stalled or retried");
            Debug.Log($"[VERIFY RAINBOW DARK DRAGON] Completed {session.TurnNumber} turns in {elapsed.ElapsedMilliseconds} ms.");
        }

        private static void VerifyDddTurnPlanning()
        {
            const uint gilgamesh = 9024198, machinex = 46593546, siegfried = 44852429;
            var deck = DeckRepository.PresetDecks.SingleOrDefault(candidate => candidate.id == 34043);
            AssertTrue(deck != null && deck.charId == 43 && deck.main.Length == 40 && deck.extra.Length == 15 &&
                deck.extra.Contains(gilgamesh) && deck.extra.Contains(machinex), "The planned D/D/D deck is not selectable.");

            using var session = new DuelSession();
            var own = session.Players[0];
            var rival = session.Players[1];
            own.deckCount = 28;
            LiveCardState FaceUp(uint code) => new LiveCardState
            {
                code = code,
                position = YgoConstants.POS_FACEUP_ATTACK,
            };
            own.mzone[0] = FaceUp(74583607);
            own.mzone[1] = FaceUp(28406301);
            own.hand.Add(FaceUp(19580308));
            own.extra.AddRange(new[] { FaceUp(gilgamesh), FaceUp(machinex), FaceUp(siegfried) });

            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, playerId = 0, canEp = true };
            foreach (uint code in new[] { machinex, gilgamesh, siegfried })
                prompt.spSummonable.Add(new SelectableCardItem { code = code, location = YgoConstants.LOCATION_EXTRA });

            int Pick() => BitConverter.ToInt32(new TacticalAI().ComputeResponse(session, prompt), 0);
            AssertTrue(Pick() == ((1 << 16) | 1), "D/D/D did not open the Pendulum route before overlaying.");

            own.szone[6] = FaceUp(28406301);
            own.szone[7] = FaceUp(11609969);
            own.szone[1] = FaceUp(46372010);
            rival.mzone[0] = FaceUp(89631139);
            AssertTrue(Pick() == 1, "D/D/D did not choose Machinex against a public monster with interaction available.");
            rival.szone[2] = new LiveCardState { position = YgoConstants.POS_FACEDOWN_DEFENSE };
            AssertTrue(Pick() == ((2 << 16) | 1), "D/D/D did not adapt its boss choice to the opponent's backrow.");

            own.szone[6] = own.szone[7] = null;
            own.mzone[1] = null;
            prompt.spSummonable.RemoveRange(1, 2);
            own.hand.Add(FaceUp(45206713));
            prompt.activatable.Add(new SelectableCardItem { code = 45206713, location = YgoConstants.LOCATION_HAND });
            AssertTrue(Pick() == 5, "D/D/D did not use the available fusion route after losing its Link materials.");

            session.CurrentChain.Add(new ChainLinkInfo { code = gilgamesh, description = (ulong)gilgamesh << 20 });
            var scales = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_UNSELECT_CARD, min = 2, max = 2 };
            SelectableCardItem Scale(uint code) => new SelectableCardItem { code = code, location = YgoConstants.LOCATION_DECK };
            scales.unselectCandidates.Add(Scale(28406301));
            scales.candidates.Add(Scale(46796664));
            scales.candidates.Add(Scale(11609969));
            AssertTrue(BitConverter.ToInt32(new TacticalAI().ComputeResponse(session, scales), 4) == 1,
                "D/D/D chose two equal scales over a scale pair that can summon the current hand.");

            own.szone[1] = null;
            var kepler = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_OPTION };
            kepler.options.AddRange(new[] { (11609969UL << 20) | 2UL, (11609969UL << 20) | 3UL });
            AssertTrue(CardDatabase.ResolveDescription((11609969UL << 20) | 1).Contains("계약서") &&
                CardDatabase.ResolveDescription(kepler.options[1]) == "덱에서 계약서 카드 1장을 패에 넣는다",
                "Kepler should preview its two alternatives and describe the actual search option in Korean.");
            AssertTrue(BitConverter.ToInt32(new TacticalAI().ComputeResponse(session, kepler), 0) == 1,
                "D/D/D bounced a monster instead of searching for an unavailable Contract.");
            Debug.Log("[VERIFY DDD TURN PLANNING] Board-based boss choice, fusion recovery, reachable Pendulum scales and Kepler search.");
        }

        private static void VerifyAiFieldAndTributeChoices()
        {
            using var duel = new DuelSession();
            var ai = new TacticalAI();
            foreach (byte current in new byte[] { 0, 1 })
            {
                var place = new DuelPrompt
                {
                    messageType = YgoConstants.MSG_SELECT_PLACE,
                    playerId = current,
                    min = 1,
                    placeFlag = ~(1u << 13),
                };
                AssertTrue(ai.ComputeResponse(duel, place).SequenceEqual(new byte[] { current, (byte)YgoConstants.LOCATION_SZONE, 5 }),
                    "AI could not select its own Field Zone");
                place.placeFlag = ~(1u << 29);
                AssertTrue(ai.ComputeResponse(duel, place).SequenceEqual(new byte[] { (byte)(1 - current), (byte)YgoConstants.LOCATION_SZONE, 5 }),
                    "AI could not select the opponent's Field Zone");
            }

            var tribute = new DuelPrompt
            {
                messageType = YgoConstants.MSG_SELECT_TRIBUTE,
                min = 2,
                max = 2,
            };
            tribute.candidates.Add(new SelectableCardItem { code = 17444133, releaseParam = 2 });
            tribute.candidates.Add(new SelectableCardItem { code = 46986414, releaseParam = 1 });
            var response = ai.ComputeResponse(duel, tribute);
            AssertTrue(response.Length == 12 && BitConverter.ToInt32(response, 4) == 1 && BitConverter.ToInt32(response, 8) == 0,
                "AI did not count the double tribute as two tributes");
            Debug.Log("[VERIFY AI CHOICES] Field Zones for both players and weighted tributes passed.");
        }

        private static void VerifyAiKeepsActiveFieldSpell()
        {
            const uint yami = 59197169;
            var fieldDeck = Enumerable.Repeat(yami, 40).ToArray();
            var empty = Array.Empty<uint>();
            foreach (bool humanFirst in new[] { true, false })
            {
                using var duel = new DuelSession();
                var ai = new TacticalAI();
                AssertTrue(duel.StartNewDuel(fieldDeck, empty, fieldDeck, empty, humanFirst, 8000, new ulong[] { 7, 19, 31, 41 }),
                    "Could not start the duplicate Field Spell duel.");
                DriveUntilIdle(duel, ai);
                if (humanFirst)
                {
                    var opening = duel.ActivePrompt;
                    int activate = opening.activatable.FindIndex(card => card.code == yami);
                    AssertTrue(activate >= 0, "The first player could not activate Yami.");
                    duel.SubmitResponseInt((activate << 16) | 5);
                    DriveUntilIdle(duel, ai);
                    duel.SubmitResponseInt(7);
                    DriveUntilIdle(duel, ai);
                }

                var prompt = duel.ActivePrompt;
                AssertTrue(prompt?.playerId == duel.AiPlayerIndex, "Expected the AI's Main Phase.");
                int response = BitConverter.ToInt32(ai.ComputeResponse(duel, prompt), 0);
                AssertTrue((response & 0xffff) == 5 && prompt.activatable[response >> 16].code == yami,
                    "The AI did not activate Yami into its empty Field Zone.");
                duel.SubmitResponseInt(response);
                DriveUntilIdle(duel, ai);

                prompt = duel.ActivePrompt;
                var own = duel.Players[duel.AiPlayerIndex];
                AssertTrue(own.szone[5]?.IsFaceUp == true && own.szone[5].code == yami &&
                    prompt.canEp && prompt.activatable.Any(card => card.code == yami && card.location == YgoConstants.LOCATION_HAND),
                    "The engine did not offer the expected duplicate of the active Field Spell.");
                response = BitConverter.ToInt32(ai.ComputeResponse(duel, prompt), 0);
                AssertTrue(response == 7 && duel.RetryCount == 0,
                    "The AI replaced its active Yami with an identical copy instead of keeping it in hand.");
            }
            Debug.Log("[VERIFY AI FIELD SPELL] Both seats kept Yami in play and held the redundant copy.");
        }

        private static void VerifyAiFieldReplacementChoices()
        {
            const uint ravine = 62265044, bootSector = 36668118;
            using (var duel = new DuelSession())
            {
                var ai = new TacticalAI();
                var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };
                duel.Players[0].szone[5] = new LiveCardState { code = ravine, position = YgoConstants.POS_FACEUP_ATTACK };
                prompt.activatable.Add(new SelectableCardItem { code = bootSector, location = YgoConstants.LOCATION_HAND });
                prompt.activatable.Add(new SelectableCardItem { code = ravine, location = YgoConstants.LOCATION_SZONE, sequence = 5 });
                AssertTrue(BitConverter.ToInt32(ai.ComputeResponse(duel, prompt), 0) == ((1 << 16) | 5),
                    "The AI replaced Dragon Ravine before using its available effect.");
                AssertTrue(BitConverter.ToInt32(ai.ComputeResponse(duel, prompt), 0) == 5,
                    "The AI could not change to a different Field Spell after using Dragon Ravine.");
            }

            const uint lightStage = 35371948, candina = 61283655;
            var aiDeck = Enumerable.Repeat(lightStage, 9).Concat(Enumerable.Repeat(candina, 2))
                .Concat(Enumerable.Repeat(89631139u, 29)).ToArray();
            var otherDeck = Enumerable.Repeat(89631139u, 40).ToArray();
            var empty = Array.Empty<uint>();
            var stageAi = new TacticalAI();
            using var searchDuel = StartSeedMatching("Light Stage duplicate search", otherDeck, empty, aiDeck, empty, false,
                session =>
                {
                    DriveUntilIdle(session, stageAi);
                    var hand = session.Players[session.AiPlayerIndex].hand;
                    return hand.Count(card => card.code == lightStage) >= 3 && hand.All(card => card.code != candina);
                });
            var own = searchDuel.Players[searchDuel.AiPlayerIndex];
            for (int expected = 1; expected <= 2; expected++)
            {
                var prompt = searchDuel.ActivePrompt;
                int response = BitConverter.ToInt32(stageAi.ComputeResponse(searchDuel, prompt), 0);
                AssertTrue((response & 0xffff) == 5 && prompt.activatable[response >> 16].code == lightStage,
                    "The AI skipped a Light Stage activation with a valid search target.");
                searchDuel.SubmitResponseInt(response);
                DriveUntilIdle(searchDuel, stageAi, next => next.messageType is YgoConstants.MSG_SELECT_CARD or YgoConstants.MSG_SELECT_YESNO
                    ? stageAi.ComputeResponse(searchDuel, next) : null);
                AssertTrue(own.hand.Count(card => card.code == candina) == expected,
                    "Light Stage did not add the expected Trickstar monster.");
            }

            var ending = searchDuel.ActivePrompt;
            AssertTrue(ending.activatable.Any(card => card.code == lightStage && card.location == YgoConstants.LOCATION_HAND),
                "The exhausted Light Stage copy was not offered by the engine.");
            // Check the empty search pool independently of per-turn action limits.
            int last = BitConverter.ToInt32(new TacticalAI().ComputeResponse(searchDuel, ending), 0);
            AssertTrue((last & 0xffff) != 5 && searchDuel.RetryCount == 0,
                "The AI used a third Light Stage after its search targets ran out.");
            Debug.Log("[VERIFY AI FIELD SPELL] Used the existing field effect first and replaced Light Stage only for another search.");
        }

        private static void VerifyAiAromaGardenReplacement()
        {
            const uint garden = 5050644, cananga = 22174866;
            var aiDeck = Enumerable.Repeat(garden, 20).Concat(Enumerable.Repeat(cananga, 20)).ToArray();
            var otherDeck = Enumerable.Repeat(89631139u, 40).ToArray();
            var empty = Array.Empty<uint>();
            foreach (int lp in new[] { 2200, 3100 })
            {
                using var duel = new DuelSession();
                var ai = new TacticalAI();
                AssertTrue(duel.StartNewDuel(otherDeck, empty, aiDeck, empty, true, lp, new ulong[] { 7, 19, 31, 41 }),
                    "Could not start the Aroma Garden duel.");
                DriveUntilIdle(duel, ai);
                duel.SubmitResponseInt(7);
                DriveUntilIdle(duel, ai);
                var opening = duel.ActivePrompt;
                int summon = opening.summonable.FindIndex(card => card.code == cananga);
                AssertTrue(opening.playerId == duel.AiPlayerIndex && opening.canBp && summon >= 0 &&
                    opening.activatable.Count(card => card.code == garden) >= 2,
                    "The Aroma Garden fixture needs a monster, two Fields and the Battle Phase.");
                duel.SubmitResponseInt(summon << 16);
                DriveUntilIdle(duel, ai);

                void ActivateGarden(uint location)
                {
                    var prompt = duel.ActivePrompt;
                    int response = BitConverter.ToInt32(ai.ComputeResponse(duel, prompt), 0);
                    AssertTrue((response & 0xffff) == 5 && prompt.activatable[response >> 16].code == garden &&
                        prompt.activatable[response >> 16].location == location,
                        $"The AI did not use Aroma Garden at {location} with the opponent at {lp} LP.");
                    duel.SubmitResponseInt(response);
                    DriveUntilIdle(duel, ai);
                }

                var own = duel.Players[duel.AiPlayerIndex];
                ActivateGarden(YgoConstants.LOCATION_HAND);
                ActivateGarden(YgoConstants.LOCATION_SZONE);
                AssertTrue(own.mzone.Any(card => card?.attack == 1900), "The first boost did not resolve.");

                if (lp == 2200)
                {
                    ActivateGarden(YgoConstants.LOCATION_HAND);
                    ActivateGarden(YgoConstants.LOCATION_SZONE);
                    AssertTrue(own.mzone.Any(card => card?.attack == 2400) && own.grave.Count(card => card.code == garden) == 1,
                        "The replacement copy did not supply the extra 500 ATK.");
                    for (int step = 0; step < 12 && !duel.IsFinished; step++)
                    {
                        var prompt = duel.ActivePrompt;
                        if (prompt == null) duel.StepUntilPrompt();
                        else duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
                    }
                    AssertTrue(duel.IsFinished && duel.Winner == duel.AiPlayerIndex && own.grave.Count(card => card.code == garden) == 1,
                        "The AI did not finish the duel after the second boost.");
                }
                else
                {
                    int response = BitConverter.ToInt32(ai.ComputeResponse(duel, duel.ActivePrompt), 0);
                    AssertTrue(response == 6 && !own.grave.Any(card => card.code == garden),
                        "The AI replaced Aroma Garden when another boost would still not finish the duel.");
                }
                AssertTrue(duel.RetryCount == 0, "The Aroma Garden decisions should all be legal.");
            }
            Debug.Log("[VERIFY AI FIELD SPELL] Reused Aroma Garden for enough attack to win and held the extra copy otherwise.");
        }

        private static void VerifyMandatoryBattleAttack()
        {
            using var duel = new DuelSession();
            duel.Players[0].mzone[0] = new LiveCardState { attack = 1000, position = YgoConstants.POS_FACEUP_ATTACK };
            duel.Players[1].mzone[0] = new LiveCardState { attack = 3000, position = YgoConstants.POS_FACEUP_ATTACK };
            var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_BATTLECMD, playerId = 0 };
            prompt.attackable.Add(new SelectableCardItem { sequence = 0 });
            var ai = new TacticalAI();
            for (int i = 0; i < 26; i++)
                AssertTrue(BitConverter.ToInt32(ai.ComputeResponse(duel, prompt), 0) == 1,
                    "AI attempted to leave the Battle Phase during a mandatory attack");
            prompt.canEp = true;
            AssertTrue(BitConverter.ToInt32(ai.ComputeResponse(duel, prompt), 0) == 3,
                "AI did not end the Battle Phase when allowed");
            Debug.Log("[VERIFY MANDATORY ATTACK] AI attacked when both phase transitions were unavailable.");
        }

        private static void VerifyAiTacticalChoices()
        {
            using var duel = new DuelSession();
            TacticalAI ai = new TacticalAI(null);
            duel.Players[0].mzone[0] = new LiveCardState { attack = 2300, position = YgoConstants.POS_FACEUP_ATTACK };
            duel.Players[1].mzone[0] = new LiveCardState { attack = 3000, position = YgoConstants.POS_FACEUP_ATTACK };
            duel.Players[1].mzone[1] = new LiveCardState { attack = 1600, position = YgoConstants.POS_FACEUP_ATTACK };

            var battle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_BATTLECMD, canM2 = true };
            battle.attackable.Add(new SelectableCardItem { sequence = 0 });
            AssertTrue(BitConverter.ToInt32(ai.ComputeResponse(duel, battle), 0) == 1,
                "The AI skipped a profitable attack.");

            var targets = new DuelPrompt
            {
                messageType = YgoConstants.MSG_SELECT_CARD, min = 1, max = 1, SelectionHint = YgoConstants.HINTMSG_ATTACKTARGET,
            };
            targets.candidates.Add(new SelectableCardItem { controller = 1, location = YgoConstants.LOCATION_MZONE, sequence = 0 });
            targets.candidates.Add(new SelectableCardItem { controller = 1, location = YgoConstants.LOCATION_MZONE, sequence = 1 });
            var response = ai.ComputeResponse(duel, targets);
            AssertTrue(response.Length == 12 && BitConverter.ToInt32(response, 8) == 1,
                "The AI attacked the stronger monster despite having a winning legal target.");

            targets.SelectionHint = DuelTactics.Banish;
            targets.candidates[0] = new SelectableCardItem { location = YgoConstants.LOCATION_MZONE, sequence = 0 };
            AssertTrue(BitConverter.ToInt32(ai.ComputeResponse(duel, targets), 8) == 1,
                "The AI banished its own card while an opposing target was available.");

            duel.Players[0].mzone[1] = new LiveCardState { attack = 100, position = YgoConstants.POS_FACEUP_ATTACK };
            targets.SelectionHint = 533; // Link Material
            targets.candidates[1] = new SelectableCardItem { location = YgoConstants.LOCATION_MZONE, sequence = 1 };
            AssertTrue(BitConverter.ToInt32(ai.ComputeResponse(duel, targets), 8) == 1,
                "The AI spent its stronger monster as material with a cheaper choice available.");

            var main = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };
            main.activatable.Add(new SelectableCardItem { code = 53129443, location = YgoConstants.LOCATION_HAND });
            Array.Clear(duel.Players[1].mzone, 0, duel.Players[1].mzone.Length);
            AssertTrue(BitConverter.ToInt32(ai.ComputeResponse(duel, main), 0) == 7,
                "The AI used Dark Hole to clear only its own monsters.");
            duel.Players[1].mzone[0] = new LiveCardState { attack = 3000, position = YgoConstants.POS_FACEUP_ATTACK };
            AssertTrue(BitConverter.ToInt32(ai.ComputeResponse(duel, main), 0) == 5,
                "The AI did not consider Dark Hole when it could remove an opposing monster.");
            Debug.Log("[VERIFY AI TACTICS] Legal battle targets, banishing, material choice and avoiding an own-only board wipe passed.");
        }

        private static void VerifyAiDefense()
        {
            var policy = DuelAiPolicy.LoadIncluded();
            var linkPolicy = LinkComboPolicy.LoadIncluded();
            TacticalAI CreateAi() => new TacticalAI(policy, linkPolicy, searchCombos: true);
            VerifyAiSummonDefense(CreateAi(), boosted: true);
            VerifyAiSummonDefense(CreateAi(), boosted: false);
            VerifyAiDefensiveReposition(CreateAi(), main2: false);
            VerifyAiDefensiveReposition(CreateAi(), main2: true);
            Debug.Log("[VERIFY AI DEFENSE] Native summon positions, defensive sets and position changes in both Main Phases passed.");
        }

        private static void VerifyAiSummonDefense(TacticalAI ai, bool boosted)
        {
            const uint warwolf = 69247929, axeOfDespair = 40619825, cyberDragon = 70095154, jerryBeansMan = 23635815;
            uint[] empty = Array.Empty<uint>();
            uint[] human = Enumerable.Repeat(warwolf, 32).Concat(Enumerable.Repeat(axeOfDespair, 8)).ToArray();
            uint[] computer = new[] { cyberDragon }.Concat(Enumerable.Repeat(jerryBeansMan, 39)).ToArray();
            using var duel = StartSeedMatching("AI summon defense", human, empty, computer, empty, true, session =>
                session.ActivePrompt?.summonable.Any(card => card.code == warwolf) == true &&
                session.Players[0].hand.Any(card => card.code == axeOfDespair) &&
                session.Players[1].hand.Any(card => card.code == cyberDragon));

            duel.SubmitResponseInt(duel.ActivePrompt.summonable.FindIndex(card => card.code == warwolf) << 16);
            DriveUntilIdle(duel, ai);
            if (boosted)
            {
                int equip = duel.ActivePrompt.activatable.FindIndex(card => card.code == axeOfDespair);
                AssertTrue(equip >= 0, "The native defense fixture did not offer Axe of Despair.");
                duel.SubmitResponseInt((equip << 16) | 5);
                DriveUntilIdle(duel, ai, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD ? EncodeSelectIndices(0) : null);
            }

            AssertTrue(duel.Players[0].mzone.Any(card => card?.attack == (boosted ? 3000 : 2000)),
                "The native defense fixture did not establish the expected opposing ATK.");
            if (boosted)
            {
                var position = new DuelPrompt
                {
                    messageType = YgoConstants.MSG_SELECT_POSITION,
                    playerId = (byte)duel.AiPlayerIndex,
                    promptCardCode = cyberDragon,
                    allowedPositions = (byte)(YgoConstants.POS_FACEUP_ATTACK | YgoConstants.POS_FACEUP_DEFENSE),
                };
                AssertTrue(BitConverter.ToUInt32(ai.ComputeResponse(duel, position), 0) == YgoConstants.POS_FACEUP_DEFENSE,
                    "An off-turn summon must consider a stronger opposing attacker.");
                position.allowedPositions = (byte)YgoConstants.POS_FACEUP_ATTACK;
                AssertTrue(BitConverter.ToUInt32(ai.ComputeResponse(duel, position), 0) == YgoConstants.POS_FACEUP_ATTACK,
                    "The AI must honor an Attack Position-only summon.");
            }
            duel.SubmitResponseInt(7);
            DriveUntilIdle(duel, ai);
            int summon = duel.ActivePrompt.spSummonable.FindIndex(card => card.code == cyberDragon);
            AssertTrue(duel.ActivePrompt.playerId == duel.AiPlayerIndex && summon >= 0,
                "The AI does not have a legal Cyber Dragon Special Summon.");
            duel.SubmitResponseInt((summon << 16) | 1);
            uint expectedPosition = boosted ? YgoConstants.POS_FACEUP_DEFENSE : YgoConstants.POS_FACEUP_ATTACK;
            DriveUntilIdle(duel, ai);
            var own = duel.Players[duel.AiPlayerIndex];
            AssertTrue(own.mzone.Any(card => card?.code == cyberDragon && card.position == expectedPosition),
                $"Cyber Dragon chose the wrong position against {(boosted ? 3000 : 2000)} ATK.");

            if (boosted)
            {
                var prompt = duel.ActivePrompt;
                int action = BitConverter.ToInt32(ai.ComputeResponse(duel, prompt), 0);
                AssertTrue((action & 0xffff) == 3 && prompt.mSetable[action >> 16].code == jerryBeansMan,
                    "The AI exposed a weaker Normal Monster when a defensive Set was legal.");
                duel.SubmitResponseInt(action);
                DriveUntilIdle(duel, ai);
                AssertTrue(own.mzone.Any(card => card?.code == jerryBeansMan && card.position == YgoConstants.POS_FACEDOWN_DEFENSE),
                    "The engine did not apply the AI's defensive Set.");
            }
            AssertTrue(duel.RetryCount == 0, "The AI's summon positions must remain legal.");
        }

        private static void VerifyAiDefensiveReposition(TacticalAI ai, bool main2)
        {
            const uint warwolf = 69247929, jerryBeansMan = 23635815, blueEyes = 89631139;
            uint[] empty = Array.Empty<uint>();
            uint[] computer = new[] { jerryBeansMan }.Concat(Enumerable.Repeat(blueEyes, 39)).ToArray();
            using var duel = StartSeedMatching("AI defensive reposition", Enumerable.Repeat(warwolf, 40).ToArray(), empty,
                computer, empty, true,
                session => session.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
                    session.Players[1].hand.Any(card => card.code == jerryBeansMan));

            // The AI summons first, and Warwolf enters on the following turn.
            duel.SubmitResponseInt(7);
            DriveUntilIdle(duel, ai);
            duel.SubmitResponseInt(0);
            DriveUntilIdle(duel, ai);
            duel.SubmitResponseInt(7);
            DriveUntilIdle(duel, ai);
            duel.SubmitResponseInt(0);
            DriveUntilIdle(duel, ai);
            duel.SubmitResponseInt(7);
            DriveUntilIdle(duel, ai);
            if (main2)
            {
                duel.SubmitResponseInt(6);
                AssertTrue(duel.ActivePrompt?.canM2 == true, "The native defense fixture cannot enter Main Phase 2.");
                duel.SubmitResponseInt(2);
                DriveUntilIdle(duel, ai);
            }

            var prompt = duel.ActivePrompt;
            AssertTrue(prompt?.playerId == duel.AiPlayerIndex &&
                duel.CurrentPhase == (main2 ? YgoConstants.PHASE_MAIN2 : YgoConstants.PHASE_MAIN1),
                "The native defense fixture reached the wrong Main Phase.");
            int index = prompt.repositionable.FindIndex(card => card.code == jerryBeansMan);
            AssertTrue(index >= 0, "The weaker Attack Position monster must be eligible for a position change.");
            // Main Phase 2 also exercises the path where a combo plan already picked End Phase.
            byte[] response = main2 ? ai.ComputeComboResponse(duel, prompt, 7) : ai.ComputeResponse(duel, prompt);
            int action = BitConverter.ToInt32(response, 0);
            AssertTrue(action == ((index << 16) | 2), "The AI ended its turn with a weaker monster exposed to battle damage.");
            duel.SubmitResponseInt(action);
            DriveUntilIdle(duel, ai);
            var own = duel.Players[duel.AiPlayerIndex];
            AssertTrue(own.mzone.Any(card => card?.code == jerryBeansMan && card.position == YgoConstants.POS_FACEUP_DEFENSE),
                "The engine did not put the weaker monster in Defense Position.");

            duel.SubmitResponseInt(7);
            DriveUntilIdle(duel, ai);
            duel.SubmitResponseInt(6);
            AssertTrue(duel.ActivePrompt?.attackable.Count == 1, "The native defense fixture cannot declare an attack.");
            duel.SubmitResponseInt(1);
            DriveUntilIdle(duel, ai, pick => pick.IsAttackTargetSelection ? EncodeSelectIndices(0) : null);
            AssertTrue(duel.RetryCount == 0 && own.mzone.All(card => card == null) && own.lp == 8000,
                $"Defense Position should prevent the LP loss on destruction. LP: {own.lp}, retries: {duel.RetryCount}, prompt: {duel.ActivePrompt?.messageType}.");
        }

        private static void AssertTrue(bool cond, string msg)
        {
            if (!cond) throw new Exception($"Assertion failed: {msg}");
        }
    }
}
