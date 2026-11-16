using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.AI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public enum AppScreen
    {
        Title,
        MainMenu,
        OpponentSelect,
        DeckEditor,
        Duel,
    }

    public class GameAppController : MonoBehaviour
    {
        public float AiStepDelay { get; set; } = 0.25f;
        public AIDifficulty AiDifficulty { get; set; } = AIDifficulty.Smart;

        private TitleAndMenuUI _titleAndMenu;
        private OpponentSelectUI _oppSelect;
        private DeckEditorUI _deckEditor;
        private DuelPromptModals _modals;
        private DuelFieldUI _duelField;

        private DuelSession _session;
        private readonly TacticalAI _ai = new TacticalAI();
        private DuelPrompt _shownHumanPrompt;
        private float _aiTimer;
        private bool _resultShown;
        private bool _refreshAfterPresentation;

        private OpponentCharacter _activeOppChar;
        private PresetDeckInfo _activeOppDeck;
        private UserDeck _activeMyDeck;
        private bool _activePlayerFirst;

        private bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBeforeSceneLoad()
        {
            try
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game_runtime.log");
                File.WriteAllText(logFile, $"[RuntimeInit] BeforeSceneLoad at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nCmd: {Environment.CommandLine}\n");
                Application.logMessageReceived += (msg, stack, type) =>
                {
                    try { File.AppendAllText(logFile, $"[{type}] {msg}\n{(string.IsNullOrEmpty(stack) ? "" : stack + "\n")}"); } catch {}
                };
            }
            catch {}
        }

        private void Awake()
        {
            Debug.Log("[GameApp] Awake called.");
        }

        private void Start()
        {
            try
            {
                Debug.Log($"[GameApp] Start() starting... StreamingAssets: {Application.streamingAssetsPath}");
                Application.runInBackground = true;
                LotdAudio.EnsureInit(gameObject);

                if (!YgoNativeBridge.IsInitialized)
                {
                    int rc = YgoNativeBridge.Initialize(Application.streamingAssetsPath, OnNativeLog);
                    if (rc != 0) Debug.LogError($"[GameApp] YGO_Init failed rc={rc}");
                    else Debug.Log("[GameApp] YgoNativeBridge initialized.");
                }

                Debug.Log("[GameApp] 1. Loading CardDatabase...");
                if (!CardDatabase.IsLoaded)
                    CardDatabase.Load(Application.streamingAssetsPath);

                Debug.Log("[GameApp] 2. Initializing CardArtLoader...");
                CardArtLoader.Initialize(Application.streamingAssetsPath);

                Debug.Log("[GameApp] 3. Loading DeckRepository...");
                if (DeckRepository.PresetDecks.Count == 0)
                    DeckRepository.Load(Application.streamingAssetsPath, Application.persistentDataPath);

                Debug.Log("[GameApp] 4. Ensuring EventSystem & Canvas...");
                EnsureEventSystem();
                var canvas = EnsureMainCanvas();
                CardDragManager.Ensure(canvas);
                CardAnimationManager.Ensure(canvas);

                Debug.Log("[GameApp] 5. Constructing TitleAndMenuUI...");
                _titleAndMenu = new TitleAndMenuUI(this, canvas.transform);

                Debug.Log("[GameApp] 6. Constructing OpponentSelectUI...");
                _oppSelect = new OpponentSelectUI(this, canvas.transform);

                Debug.Log("[GameApp] 7. Constructing DeckEditorUI...");
                _deckEditor = new DeckEditorUI(this, canvas.transform);

                Debug.Log("[GameApp] 8. Constructing DuelPromptModals...");
                _modals = new DuelPromptModals(canvas.transform, code => _duelField?.InspectCard(code), OnHumanModalResponse);

                Debug.Log("[GameApp] 9. Constructing DuelFieldUI...");
                _duelField = new DuelFieldUI(this, canvas.transform, _modals);

                // Ensure Modals, Animation Layer, and Drag Ghost Layer render ABOVE DuelFieldUI and DeckEditorUI
                _modals.ModalRoot.transform.SetAsLastSibling();
                _modals.ZoneViewRoot.transform.SetAsLastSibling();
                _modals.ResultRoot.transform.SetAsLastSibling();
                CardAnimationManager.Instance?.BringToFront();

                _initialized = true;
                Debug.Log("[GameApp] 10. Showing Title screen...");
                ShowScreen(AppScreen.Title);

                Debug.Log("[GameApp] 11. Processing Command Line Args...");
                ProcessCommandLineArgs();

                Debug.Log("[GameApp] 12. Start() completed successfully. Ready for input!");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameApp] Fatal Exception in Start(): {ex}");
            }
        }

        private void OnNativeLog(int logType, string msg)
        {
            if (logType == 0) Debug.LogError($"[YGO-Core] {msg}");
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }

        private Canvas EnsureMainCanvas()
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var go = new GameObject("LOTD_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            var scaler = canvas.GetComponent<CanvasScaler>() ?? canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public void ShowScreen(AppScreen screen)
        {
            if (!_initialized) return;

            _modals.HideAll();
            _modals.HideResult();
            _titleAndMenu.SettingsRoot.SetActive(false);

            _titleAndMenu.TitleRoot.SetActive(screen == AppScreen.Title);
            _titleAndMenu.MenuRoot.SetActive(screen == AppScreen.MainMenu);
            _oppSelect.Root.SetActive(screen == AppScreen.OpponentSelect);
            _oppSelect.JankenRoot.SetActive(false);
            _deckEditor.Root.SetActive(screen == AppScreen.DeckEditor);
            _duelField.Root.SetActive(screen == AppScreen.Duel);

            if (screen == AppScreen.Duel)
            {
                LotdAudio.PlayDuelBgm();
            }
            else
            {
                LotdAudio.PlayMenuBgm();
                if (_session != null)
                {
                    _duelField.UnbindSession();
                    CardDragManager.Instance?.CancelDrag();
                    _session.Dispose();
                    _session = null;
                    _shownHumanPrompt = null;
                }
            }

            if (screen == AppScreen.OpponentSelect) _oppSelect.Open();
            else if (screen == AppScreen.DeckEditor) _deckEditor.Open();
        }

        public void StartDuelMatch(OpponentCharacter oppChar, PresetDeckInfo oppDeck, UserDeck myDeck, bool playerGoesFirst)
        {
            _activeOppChar = oppChar;
            _activeOppDeck = oppDeck;
            _activeMyDeck = myDeck;
            _activePlayerFirst = playerGoesFirst;

            _session?.Dispose();
            _session = new DuelSession();
            _ai.Difficulty = AiDifficulty;
            _shownHumanPrompt = null;
            _aiTimer = 0f;
            _resultShown = false;
            _refreshAfterPresentation = false;

            ShowScreen(AppScreen.Duel);
            _session.PreparePlayerIndices(playerGoesFirst);
            _duelField.BindSession(_session, oppChar, oppDeck);
            if (!_session.StartNewDuel(
                myDeck.main, myDeck.extra,
                oppDeck.main, oppDeck.extra,
                p0GoesFirst: playerGoesFirst))
            {
                ShowScreen(AppScreen.OpponentSelect);
                _oppSelect.ShowStatus("듀얼을 시작하지 못했습니다.", true);
                return;
            }
            _duelField.Refresh();
        }

        private void Update()
        {
            if (_session == null)
            {
                _deckEditor?.Update();
                return;
            }
            _duelField?.Update();
            if (_session == null || _duelField.IsReadingLog) return;

            if (_duelField.IsPresenting)
            {
                _refreshAfterPresentation = true;
                return;
            }
            if (_refreshAfterPresentation)
            {
                _refreshAfterPresentation = false;
                _duelField.Refresh();
            }

            if (_session.IsFinished)
            {
                if (!_resultShown)
                {
                    _resultShown = true;
                    _modals.HideAll();
                    _duelField.Refresh();
                    bool won = _session.Winner == _session.HumanPlayerIndex;
                    bool draw = _session.Winner == 2;
                    _modals.ShowResult(
                        won, draw, _activeOppChar, _session.TurnNumber,
                        Mathf.Max(0, _session.Players[_session.HumanPlayerIndex].lp),
                        Mathf.Max(0, _session.Players[_session.AiPlayerIndex].lp),
                        () => StartDuelMatch(_activeOppChar, _activeOppDeck, _activeMyDeck, _activePlayerFirst),
                        () => ShowScreen(AppScreen.OpponentSelect),
                        () => ShowScreen(AppScreen.MainMenu));
                }
                return;
            }

            if (_duelField.IsPaused) return;
            if (_duelField.TryAutoFulfillPreferredPrompt())
            {
                _duelField.Refresh();
                return;
            }

            var prompt = _session.ActivePrompt;
            if (prompt == null)
            {
                _session.StepUntilPrompt();
                _duelField.Refresh();
                return;
            }

            if (prompt.playerId == _session.AiPlayerIndex)
            {
                _aiTimer += Time.deltaTime;
                if (_aiTimer >= AiStepDelay)
                {
                    _aiTimer = 0f;
                    byte[] aiResp = _ai.ComputeResponse(_session, prompt);
                    _session.SubmitResponse(aiResp);
                    _duelField.Refresh();
                }
                return;
            }

            // Human prompt
            if (_shownHumanPrompt != prompt)
            {
                _shownHumanPrompt = prompt;
                _duelField.Refresh();

                if (prompt.messageType != YgoConstants.MSG_SELECT_IDLECMD &&
                    prompt.messageType != YgoConstants.MSG_SELECT_BATTLECMD &&
                    prompt.messageType != YgoConstants.MSG_SELECT_PLACE &&
                    prompt.messageType != YgoConstants.MSG_SELECT_DISFIELD)
                {
                    _modals.ShowPrompt(_session, prompt);
                }
                else
                {
                    _modals.HideAll();
                }
            }
        }

        private void OnHumanModalResponse(byte[] resp)
        {
            if (_session == null || _session.IsFinished) return;
            _shownHumanPrompt = null;
            if (_duelField.IsPresenting || _duelField.IsPaused) return;
            _session.SubmitResponse(resp);
            _duelField.Refresh();
        }

        private void ProcessCommandLineArgs()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-capture-tour" && i + 1 < args.Length)
                {
                    StartCoroutine(RunUiTourCapture(args[i + 1]));
                    break;
                }
            }
        }

        private IEnumerator RunUiTourCapture(string outDir)
        {
            Directory.CreateDirectory(outDir);
            yield return new WaitForSeconds(0.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "01_title.png"));
            yield return new WaitForSeconds(0.3f);

            ShowScreen(AppScreen.MainMenu);
            yield return new WaitForSeconds(0.4f);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "02_main_menu.png"));
            yield return new WaitForSeconds(0.3f);

            ShowScreen(AppScreen.OpponentSelect);
            yield return new WaitForSeconds(0.4f);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "03_opponents.png"));
            yield return new WaitForSeconds(0.3f);

            ShowScreen(AppScreen.DeckEditor);
            yield return new WaitForSeconds(0.4f);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "04_deck_editor.png"));
            yield return new WaitForSeconds(0.3f);

            var oppChar = DeckRepository.Characters.FirstOrDefault(c => c.isMeta2026) ?? DeckRepository.Characters.FirstOrDefault();
            var oppDeck = oppChar != null ? DeckRepository.GetDecksForCharacter(oppChar.id).FirstOrDefault() : DeckRepository.PresetDecks.FirstOrDefault();
            var myDeck = DeckRepository.UserDecks.FirstOrDefault();
            if (oppChar != null && oppDeck != null && myDeck != null)
            {
                AiStepDelay = 0.04f;
                StartDuelMatch(oppChar, oppDeck, myDeck, false);
                float t = 0f;
                while (t < 3.5f && _session != null && !_session.IsFinished &&
                       (_session.TurnNumber < 2 || _session.ActivePrompt == null || _session.ActivePrompt.playerId != _session.HumanPlayerIndex))
                {
                    t += Time.deltaTime;
                    yield return null;
                }
                var myHand = _session?.Players[_session.HumanPlayerIndex].hand;
                if (myHand != null && myHand.Count > 0)
                    _duelField?.InspectCard(myHand[0].code);
                yield return new WaitForSeconds(0.25f);
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "05_duel_field.png"));
                yield return new WaitForSeconds(0.35f);
            }

            Application.Quit();
        }

        private void OnDestroy()
        {
            Debug.Log($"[GameApp] OnDestroy called! Stack:\n{Environment.StackTrace}");
            _session?.Dispose();
            _session = null;
        }

        private void OnApplicationQuit()
        {
            Debug.Log($"[GameApp] OnApplicationQuit called! Stack:\n{Environment.StackTrace}");
            _session?.Dispose();
            _session = null;
            CardArtLoader.Shutdown();
            YgoNativeBridge.Shutdown();
        }
    }
}
