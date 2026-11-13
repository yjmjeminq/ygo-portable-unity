using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            // 2. LOTD Resource sprites: ensure TextureImporterType.Sprite & mipmapEnabled=false
            string[] texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/LOTD" });
            var toReimport = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string guid in texGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (ti == null) continue;
                    if (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled)
                    {
                        ti.textureType = TextureImporterType.Sprite;
                        ti.spriteImportMode = SpriteImportMode.Single;
                        ti.spritePixelsPerUnit = 100f;
                        ti.mipmapEnabled = false;
                        ti.npotScale = TextureImporterNPOTScale.None;
                        ti.alphaIsTransparency = true;
                        ti.SaveAndReimport();
                        toReimport.Add(path);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            if (toReimport.Count > 0)
                Debug.Log($"[YgoProjectBuilder] Converted {toReimport.Count} LOTD textures to Sprite.");
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

            var canvasGo = new GameObject("LOTD_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            new GameObject("LOTD_App", typeof(GameAppController));

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

            CardDatabase.Load(streaming);
            AssertTrue(CardDatabase.AllCards.Count >= 14000, "CardDatabase count too low");

            // Verify 환상마족 (Illusion) card (e.g. 14307929 하얀 숲의 요마 디아벨) + icon
            var paidra = CardDatabase.Get(39931513);
            AssertTrue(paidra != null && (paidra.race & YgoConstants.RACE_DRAGON) != 0,
                "Card 39931513 (천배룡 파이도라) missing");
            var illusionCard = CardDatabase.Get(14307929);
            AssertTrue(illusionCard != null && (illusionCard.race & YgoConstants.RACE_ILLUSION) != 0,
                "Card 14307929 missing RACE_ILLUSION");
            Sprite illusionSp = LotdUiKit.GetRaceSprite(YgoConstants.RACE_ILLUSION);
            AssertTrue(illusionSp != null, "Missing LOTD/TypeIcons sprite for RACE_ILLUSION");

            // Verify CardArtLoader
            CardArtLoader.Initialize(streaming);
            AssertTrue(CardArtLoader.GetCardBackSprite() != null, "Missing card_back sprite");
            AssertTrue(CardArtLoader.GetArtSprite(89631139) != null, "Missing Blue-Eyes art sprite");
            AssertTrue(CardArtLoader.GetArtSprite(14307929) != null, "Missing Illusion art sprite");

            // Verify Characters (236) & Decks (870 = 20 meta + 850 original) & Bust portraits
            DeckRepository.Load(streaming, tempSave);
            AssertTrue(DeckRepository.Characters.Count >= 230, $"Expected >=230 chars, got {DeckRepository.Characters.Count}");
            AssertTrue(DeckRepository.PresetDecks.Count >= 865, $"Expected >=865 decks, got {DeckRepository.PresetDecks.Count}");
            int missingPortraits = 0;
            foreach (var ch in DeckRepository.Characters)
            {
                if (string.IsNullOrEmpty(ch.portrait) || LotdUiKit.LoadSprite($"LOTD/Busts/{ch.portrait}") == null)
                    missingPortraits++;
            }
            AssertTrue(missingPortraits == 0, $"{missingPortraits} character portraits failed to load");
            Debug.Log($"[VERIFY] DB verified: {DeckRepository.Characters.Count} chars (0 missing portraits), {DeckRepository.PresetDecks.Count} preset decks.");

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

            // Explicit test for older searchers finding newest 2025/2026 cards across Category/Type/Race/Stats/Level
            VerifyOldSearchersFindNewestCards(classicDecks[0].main);

            AssertTrue(luaErrors.Count == 0, $"Native/Lua error logs detected: {string.Join(" | ", luaErrors.Take(3))}");

            CardArtLoader.Shutdown();
            YgoNativeBridge.Shutdown();
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
                            string hintText = CardDatabase.ResolveDescription(session.LastHintMsg, 0);
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

        private static void AssertTrue(bool cond, string msg)
        {
            if (!cond) throw new Exception($"Assertion failed: {msg}");
        }
    }
}
