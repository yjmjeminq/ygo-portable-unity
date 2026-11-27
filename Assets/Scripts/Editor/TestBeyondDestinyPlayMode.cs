using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.AI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    [InitializeOnLoad]
    internal static class TestBeyondDestinyPlayMode
    {
        private const string Active = "Ygo.BeyondDestiny.PlayCheck";
        private const string Profile = "Ygo.BeyondDestiny.PlayCheckProfile";
        private static double _deadline, _next;
        private static int _phase;
        private static bool _openingDialogueCaptured, _backlashCaptured;
        private static double _captureAt;
        private static string _captureName;
        private static Action _afterCapture;
        private static GameAppController _app;
        private static TacticalAI _driver;

        static TestBeyondDestinyPlayMode() => EditorApplication.playModeStateChanged += OnPlayMode;

        public static void Run()
        {
            if (Environment.GetEnvironmentVariable("YGO_BEYOND_QA_ISOLATED") != "1" ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("YGO_BEYOND_QA_OUTPUT")))
                throw new InvalidOperationException("The Play Mode check needs an isolated project and an output directory.");
            string profile = "BeyondDestinyQA-" + Guid.NewGuid().ToString("N");
            SessionState.SetString(Profile, profile);
            SessionState.SetString(Active + ".product", PlayerSettings.productName);
            SessionState.SetString(Active + ".company", PlayerSettings.companyName);
            SessionState.SetBool(Active, true);
            SessionState.SetInt(Active + ".exit", 1);
            PlayerSettings.companyName = "YGO Portable QA";
            PlayerSettings.productName = profile;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Beyond Destiny QA controller", typeof(GameAppController));
            new GameObject("Beyond Destiny QA listener", typeof(AudioListener));
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                _deadline = EditorApplication.timeSinceStartup + 150;
                _driver = new TacticalAI(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded(), searchCombos: false);
                EditorApplication.update += Step;
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= Step;
                PlayerSettings.productName = SessionState.GetString(Active + ".product", "");
                PlayerSettings.companyName = SessionState.GetString(Active + ".company", "");
                SessionState.SetBool(Active, false);
                EditorApplication.Exit(SessionState.GetInt(Active + ".exit", 1));
            }
        }

        private static void Step()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + .045;
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline)
                    throw new TimeoutException($"Beyond Destiny Play Mode did not finish phase {_phase}.");
                if (_captureName != null)
                {
                    if (EditorApplication.timeSinceStartup < _captureAt) return;
                    Capture(_captureName);
                    var after = _afterCapture;
                    _captureName = null;
                    _afterCapture = null;
                    after?.Invoke();
                    return;
                }
                if (Path.GetFileName(Application.persistentDataPath) != SessionState.GetString(Profile, ""))
                    throw new InvalidOperationException("The game did not start in its isolated QA save profile.");
                _app ??= UnityEngine.Object.FindFirstObjectByType<GameAppController>();
                if (_app == null || !CollectionProgress.Initialized) return;
                if (_phase == 0)
                {
                    if (!CollectionProgress.HasStarter && !CollectionProgress.TryChooseStarter(CollectionProgress.Starters[0].Id, out _, out string error))
                        throw new InvalidOperationException(error);
                    _app.ShowScreen(AppScreen.MainMenu);
                    Click("스토리 RPG");
                    CaptureAfter("00-campaign-picker", .35, () =>
                    {
                        Click("5D’s: Beyond Destiny");
                        CaptureAfter("01-city-field", .35, () =>
                        {
                            Click("이야기 시작");
                            _phase = 1;
                        });
                    });
                    return;
                }
                if (_phase == 1 && FindButton("듀얼 시작") is { } launch)
                {
                    launch.onClick.Invoke();
                    _phase = 2;
                    return;
                }
                if (_phase == 2 && Get<DuelSession>(_app, "_session") is { } duel)
                {
                    var field = Get<DuelFieldUI>(_app, "_duelField");
                    if (field.IsPresenting || duel.ActivePrompt == null) return;
                    Capture("03-first-duel");
                    _phase = 3;
                    return;
                }
                if (_phase == 3 && Get<DuelSession>(_app, "_session") is { } match)
                {
                    if (Get<DuelFieldUI>(_app, "_duelField").IsPresenting) return;
                    var prompt = match.ActivePrompt;
                    if (prompt == null || prompt.playerId != match.HumanPlayerIndex) return;
                    if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && prompt.canEp) match.SubmitResponseInt(7);
                    else if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced) match.SubmitResponseInt(-1);
                    else match.SubmitResponse(_driver.ComputeResponse(match, prompt));
                    return;
                }
                if (_phase == 3 && FindButton("다음 장면") is { } next)
                {
                    next.onClick.Invoke();
                    _phase = 4;
                    return;
                }
                if (_phase == 4 && FindButton("이야기 시작") is { } fieldEntry &&
                    Playback().Episode.number == 2)
                {
                    Capture("05-episode-02-arrival");
                    fieldEntry.onClick.Invoke();
                    _phase = 5;
                    return;
                }
                if (_phase == 5 && FindAdvance() != null)
                {
                    _app.ShowScreen(AppScreen.Campaigns);
                    Click("5D’s: Beyond Destiny");
                    if (Playback().Episode.number != 2 || FindAdvance() == null)
                        throw new InvalidOperationException("Reopening the campaign did not resume episode two.");
                    FindAdvance().onClick.Invoke();
                    CaptureAfter("06-episode-02-resume", 1.30, () =>
                    {
                        if (!_openingDialogueCaptured || !_backlashCaptured)
                            throw new InvalidOperationException("Opening story or Backlash artwork did not render.");
                        Debug.Log("[Beyond Destiny Play Mode] Picker, field, opening, native duel, Backlash and episode-two resume passed.");
                        Finish(0);
                    });
                    return;
                }

                var advance = FindAdvance();
                if (advance == null) return;
                var scene = Playback().Scene;
                advance.onClick.Invoke();
                if (!_openingDialogueCaptured && scene.number == 1)
                {
                    CaptureAfter("02-opening-dialogue", 1.30);
                    _openingDialogueCaptured = true;
                    return;
                }
                if (!_backlashCaptured && scene.id == "beyond_destiny_ep_01_scene_04" &&
                    UnityEngine.Object.FindObjectsByType<Image>(FindObjectsSortMode.None)
                        .Any(image => image.sprite != null && image.sprite.name == "ep01_backlash"))
                {
                    CaptureAfter("04-backlash", 1.30);
                    _backlashCaptured = true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(1);
            }
        }

        private static BeyondDestinyPlayback Playback() => Get<BeyondDestinyPlayback>(Get<BeyondDestinyUI>(_app, "_beyondDestiny"), "_playback");

        private static T Get<T>(object target, string field) where T : class =>
            (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static Button[] Buttons() => UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
        private static Button FindButton(string label) => Buttons().FirstOrDefault(button =>
            button.IsInteractable() && button.GetComponentInChildren<Text>()?.text == label);
        private static Button FindAdvance() => Buttons().FirstOrDefault(button => button.name == "AdvanceSurface" && button.IsInteractable());
        private static void Click(string label) => (FindButton(label) ??
            throw new InvalidOperationException($"Missing campaign control '{label}'.")).onClick.Invoke();

        private static void CaptureAfter(string name, double delay, Action after = null)
        {
            _captureName = name;
            _captureAt = EditorApplication.timeSinceStartup + delay;
            _afterCapture = after;
        }

        private static void Capture(string name)
        {
            string output = Environment.GetEnvironmentVariable("YGO_BEYOND_QA_OUTPUT");
            Directory.CreateDirectory(output);
            var canvas = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).First(item =>
                item.isRootCanvas && item.transform.Find("Screen_Campaigns") != null);
            var cameraObject = new GameObject("5D's QA camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            var frame = RenderTexture.GetTemporary(1600, 900, 24);
            var pixels = new Texture2D(frame.width, frame.height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var oldCamera = canvas.worldCamera;
            var mode = canvas.renderMode;
            try
            {
                camera.targetTexture = frame;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.012f, .018f, .025f);
                canvas.worldCamera = camera;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = frame;
                pixels.ReadPixels(new Rect(0, 0, frame.width, frame.height), 0, 0);
                File.WriteAllBytes(Path.Combine(output, name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = mode;
                canvas.worldCamera = oldCamera;
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(frame);
                UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static void Finish(int code)
        {
            EditorApplication.update -= Step;
            SessionState.SetInt(Active + ".exit", code);
            EditorApplication.ExitPlaymode();
        }
    }
}
