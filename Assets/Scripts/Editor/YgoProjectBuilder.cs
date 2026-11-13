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

            // Run 5 full AI vs AI matches
            var metaDecks = DeckRepository.PresetDecks.Where(d => d.isMeta2026 && d.main != null && d.main.Length >= 40).ToList();
            var classicDecks = DeckRepository.PresetDecks.Where(d => !d.isMeta2026 && d.main != null && d.main.Length >= 40).ToList();
            AssertTrue(metaDecks.Count >= 18, $"Need >=18 playable meta decks, got {metaDecks.Count}");
            AssertTrue(classicDecks.Count >= 100, $"Need >=100 playable classic decks, got {classicDecks.Count}");

            var pairings = new[]
            {
                (metaDecks[0], metaDecks[Math.Min(1, metaDecks.Count - 1)]),
                (metaDecks[Math.Min(2, metaDecks.Count - 1)], metaDecks[Math.Min(3, metaDecks.Count - 1)]),
                (metaDecks[Math.Min(4, metaDecks.Count - 1)], metaDecks[Math.Min(5, metaDecks.Count - 1)]),
                (metaDecks[0], classicDecks[0]),
                (classicDecks[10], classicDecks[25]),
            };

            int totalRetries = 0;
            for (int i = 0; i < pairings.Length; i++)
            {
                var (d0, d1) = pairings[i];
                var ai0 = new TacticalAI { Difficulty = AIDifficulty.Smart };
                var ai1 = new TacticalAI { Difficulty = AIDifficulty.Smart };
                using (var session = new DuelSession())
                {
                    ulong[] seed = { (ulong)(1001 + i), (ulong)(2002 + i * 7), (ulong)(3003 + i * 13), (ulong)(4004 + i * 19) };
                    bool started = session.StartNewDuel(d0.main, d0.extra, d1.main, d1.extra, true, 8000, seed);
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
                        var curAi = session.ActivePrompt.playerId == 0 ? ai0 : ai1;
                        byte[] resp = curAi.ComputeResponse(session, session.ActivePrompt);
                        session.SubmitResponse(resp);
                    }

                    totalRetries += session.RetryCount;
                    Debug.Log($"[VERIFY DUEL {i + 1}] '{d0.title}' vs '{d1.title}' => finished={session.IsFinished}, winner={session.Winner}, turns={session.TurnNumber}, steps={steps}, lp=({session.Players[0].lp},{session.Players[1].lp}), hands=({session.Players[0].hand.Count},{session.Players[1].hand.Count}), retries={session.RetryCount}");
                    AssertTrue(steps >= 5 && session.TurnNumber >= 2, $"Duel {i + 1} ended prematurely (turns={session.TurnNumber}, steps={steps})");
                    AssertTrue(session.RetryCount <= 4, $"Duel {i + 1} had excessive retries ({session.RetryCount})");
                }
            }

            Debug.Log($"[VERIFY] Total retries across 5 duels: {totalRetries}, native errors: {luaErrors.Count}");
            AssertTrue(luaErrors.Count == 0, $"Native/Lua error logs detected: {string.Join(" | ", luaErrors.Take(3))}");

            CardArtLoader.Shutdown();
            YgoNativeBridge.Shutdown();
        }

        private static void AssertTrue(bool cond, string msg)
        {
            if (!cond) throw new Exception($"Assertion failed: {msg}");
        }
    }
}
