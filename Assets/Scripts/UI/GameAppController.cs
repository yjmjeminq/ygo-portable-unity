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
        StarterSelection,
        CardShop,
        ArchetypeCodex,
    }

    public class GameAppController : MonoBehaviour
    {
        public float AiStepDelay { get; set; } = 0.25f;
        public AIDifficulty AiDifficulty { get; set; } = AIDifficulty.Smart;
        internal ChainConfirmationMode ChainConfirmation { get; private set; } = ChainConfirmationMode.Auto;

        private TitleAndMenuUI _titleAndMenu;
        private OpponentSelectUI _oppSelect;
        private DeckEditorUI _deckEditor;
        private DuelPromptModals _modals;
        private DuelFieldUI _duelField;
        private CollectionShopUI _collectionShop;
        private ArchetypeCodexUI _archetypeCodex;
        private GitHubSaveSync _saveSync;
        private SaveSyncMenu _saveSyncMenu;
        private GameObject _focusBeforeSaveSync;
        private bool _saveConflictShown;
        private readonly DeckPadInput _menuPadInput = new DeckPadInput();
        private DeckPadUi _menuPad;
        private AppScreen _screen;

        private DuelSession _session;
        private TacticalAI _ai;
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

                var saveFiles = new SaveSyncFiles(Application.persistentDataPath);
                saveFiles.Recover();
                Debug.Log("[GameApp] 3. Loading DeckRepository...");
                if (DeckRepository.PresetDecks.Count == 0)
                    DeckRepository.Load(Application.streamingAssetsPath, Application.persistentDataPath, seedDefaultDecks: false);
                CollectionProgress.Load(Application.persistentDataPath);
                RegisterStarterDeck(CollectionProgress.CreateStarterDeck());

                Debug.Log("[GameApp] 4. Ensuring EventSystem & Canvas...");
                EnsureEventSystem();
                var canvas = EnsureMainCanvas();
                CardDragManager.Ensure(canvas);
                CardAnimationManager.Ensure(canvas);

                Debug.Log("[GameApp] 5. Constructing TitleAndMenuUI...");
                _titleAndMenu = new TitleAndMenuUI(this, canvas.transform);
                _saveSync = new GitHubSaveSync(this, Application.persistentDataPath, saveFiles,
                    CanApplyCloudSave, ReloadCloudSave);
                _saveSyncMenu = new SaveSyncMenu(canvas.transform, _saveSync, () => CloseSaveSync());
                _collectionShop = new CollectionShopUI(canvas.transform,
                    () => ShowScreen(_screen == AppScreen.StarterSelection ? AppScreen.Title : AppScreen.MainMenu),
                    CompleteStarterSelection);
                _archetypeCodex = new ArchetypeCodexUI(canvas.transform, () => ShowScreen(AppScreen.MainMenu));

                Debug.Log("[GameApp] 6. Constructing OpponentSelectUI...");
                _oppSelect = new OpponentSelectUI(this, canvas.transform);

                Debug.Log("[GameApp] 7. Constructing DeckEditorUI...");
                _deckEditor = new DeckEditorUI(this, canvas.transform);

                Debug.Log("[GameApp] 8. Constructing DuelPromptModals...");
                _modals = new DuelPromptModals(canvas.transform, code => _duelField?.InspectCard(code), OnHumanModalResponse);

                Debug.Log("[GameApp] 9. Constructing DuelFieldUI...");
                _duelField = new DuelFieldUI(this, canvas.transform, _modals);
                _menuPad = new DeckPadUi(LotdUiKit.FillRect("MenuPad", canvas.transform),
                    () => _screen is AppScreen.CardShop or AppScreen.ArchetypeCodex);

                // Ensure Modals, Animation Layer, and Drag Ghost Layer render ABOVE DuelFieldUI and DeckEditorUI
                _modals.ModalRoot.transform.SetAsLastSibling();
                _modals.ZoneViewRoot.transform.SetAsLastSibling();
                _modals.ResultRoot.transform.SetAsLastSibling();
                CardAnimationManager.Instance?.BringToFront();
                if (Application.platform == RuntimePlatform.IPhonePlayer)
                    Input.simulateMouseWithTouches = false;

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
            var events = FindFirstObjectByType<EventSystem>();
            if (events == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                events = go.GetComponent<EventSystem>();
                DontDestroyOnLoad(go);
            }
            if (events.TryGetComponent<StandaloneInputModule>(out var input))
            {
                // Custom pad navigation owns controller edges; keep Unity's mouse and keyboard paths.
                input.horizontalAxis = "DeckUiHorizontal";
                input.verticalAxis = "DeckUiVertical";
                input.submitButton = "DeckUiSubmit";
                input.cancelButton = "DeckUiCancel";
            }
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
            if (CollectionProgress.Initialized && !CollectionProgress.HasStarter && screen != AppScreen.Title)
                screen = AppScreen.StarterSelection;

            var previousScreen = _screen;
            _screen = screen;
            _menuPad?.Clear();
            _menuPadInput.Reset();
            _modals.HideAll();
            _modals.HideResult();
            CloseSaveSync(restoreFocus: false);
            _titleAndMenu.CloseSettings(restoreFocus: false);
            if (CanApplyCloudSave()) _saveSync?.RequestSync();

            _titleAndMenu.TitleRoot.SetActive(screen == AppScreen.Title);
            _titleAndMenu.MenuRoot.SetActive(screen == AppScreen.MainMenu);
            _collectionShop?.StarterRoot.SetActive(false);
            _collectionShop?.CloseShop();
            _archetypeCodex.Close();
            _oppSelect.Root.SetActive(screen == AppScreen.OpponentSelect);
            _oppSelect.JankenRoot.SetActive(false);
            if (screen != AppScreen.DeckEditor) _deckEditor.Close();
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
            else if (screen == AppScreen.StarterSelection)
                _collectionShop.OpenStarter();
            else if (screen == AppScreen.CardShop)
                _collectionShop.OpenShop();
            else if (screen == AppScreen.ArchetypeCodex)
                _archetypeCodex.Open();
            else if (screen == AppScreen.MainMenu)
                _titleAndMenu.FocusMainMenu(previousScreen);
            else if (screen == AppScreen.Title)
            {
                var first = _titleAndMenu.TitleRoot.GetComponentInChildren<Button>();
                EventSystem.current?.SetSelectedGameObject(first != null ? first.gameObject : null);
            }

            if (previousScreen != screen || screen == AppScreen.Title)
            {
                var root = screen switch
                {
                    AppScreen.Title => _titleAndMenu.TitleRoot,
                    AppScreen.MainMenu => _titleAndMenu.MenuRoot,
                    AppScreen.OpponentSelect => _oppSelect.Root,
                    AppScreen.DeckEditor => _deckEditor.Root,
                    AppScreen.Duel => _duelField.Root,
                    AppScreen.StarterSelection => _collectionShop.StarterRoot,
                    AppScreen.CardShop => _collectionShop.Root,
                    AppScreen.ArchetypeCodex => _archetypeCodex.Root,
                    _ => null,
                };
                if (root != null) UiEntranceMotion.Play(root, .67f, .19f);
            }
            SyncFrontMenuFocus();
        }

        private void CompleteStarterSelection(UserDeck deck)
        {
            RegisterStarterDeck(deck);
            ShowScreen(AppScreen.MainMenu);
        }

        private static void RegisterStarterDeck(UserDeck starter)
        {
            if (starter == null) return;
            var existing = DeckRepository.UserDecks.Find(deck => deck.id == starter.id);
            if (existing == null)
            {
                try { DeckRepository.SaveUserDeck(starter); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"[GameApp] 스타터 덱 저장 실패. 다음 실행 시 수집 프로필에서 다시 불러옵니다. {error.Message}");
                }
                existing = starter;
            }

            DeckRepository.UserDecks.Remove(existing);
            DeckRepository.UserDecks.Insert(0, existing);
        }

        internal static bool CanStartWithDeck(UserDeck deck, out string reason)
        {
            if (CollectionProgress.Initialized) return CollectionProgress.CanUseDeck(deck, out reason);
            reason = deck == null ? "사용할 덱을 선택하세요." :
                deck.main.Count < 40 || deck.main.Count > 60 ? "메인 덱은 40~60장이어야 합니다." :
                deck.extra.Count > 15 ? "엑스트라 덱은 최대 15장입니다." :
                deck.side.Count > 15 ? "사이드 덱은 최대 15장입니다." : "";
            return reason.Length == 0;
        }

        public void StartDuelMatch(OpponentCharacter oppChar, PresetDeckInfo oppDeck, UserDeck myDeck, bool playerGoesFirst)
        {
            if (oppChar == null || oppDeck == null || oppDeck.charId != oppChar.id ||
                !CollectionProgress.IsOpponentDeckUnlocked(oppChar.id, oppDeck.id))
            {
                ShowScreen(AppScreen.OpponentSelect);
                _oppSelect.ShowStatus("해금된 상대와 덱을 선택하세요.", true);
                return;
            }
            if (CollectionProgress.Initialized)
                oppDeck = OpponentDeckLevels.GetDecks(oppChar.id).First(deck => deck.id == oppDeck.id);
            if (!CanStartWithDeck(myDeck, out string reason))
            {
                ShowScreen(AppScreen.OpponentSelect);
                _oppSelect.ShowStatus(reason, true);
                return;
            }
            _activeOppChar = oppChar;
            _activeOppDeck = oppDeck;
            _activeMyDeck = myDeck;
            _activePlayerFirst = playerGoesFirst;

            _session?.Dispose();
            _session = new DuelSession();
            _ai = new TacticalAI(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded()) { Difficulty = AiDifficulty };
            _shownHumanPrompt = null;
            _aiTimer = 0f;
            _resultShown = false;
            _refreshAfterPresentation = false;

            ShowScreen(AppScreen.Duel);
            _session.PreparePlayerIndices(playerGoesFirst);
            _duelField.BindSession(_session, oppChar, oppDeck);
            _duelField.RefreshChainConfirmation(ChainConfirmation);
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

        private bool CanApplyCloudSave() => _screen is AppScreen.Title or AppScreen.MainMenu;

        private void ReloadCloudSave()
        {
            DeckRepository.ReloadUserDecks();
            CollectionProgress.Load(Application.persistentDataPath);
            RegisterStarterDeck(CollectionProgress.CreateStarterDeck());
            _titleAndMenu.RefreshWallet();
        }

        internal void OpenSaveSync()
        {
            if (_saveSyncMenu == null || _saveSyncMenu.Root.activeSelf || !CanApplyCloudSave()) return;
            _focusBeforeSaveSync = EventSystem.current?.currentSelectedGameObject;
            _menuPad?.Clear();
            _titleAndMenu.SetSaveSyncOpen(true);
            _saveSyncMenu.Open();
        }

        private void CloseSaveSync(bool restoreFocus = true)
        {
            if (_saveSyncMenu == null || !_saveSyncMenu.Root.activeSelf) return;
            _saveSyncMenu.Close();
            _titleAndMenu.SetSaveSyncOpen(false);
            _menuPad?.Clear();
            if (restoreFocus && _focusBeforeSaveSync != null && _focusBeforeSaveSync.activeInHierarchy)
                EventSystem.current?.SetSelectedGameObject(_focusBeforeSaveSync);
            _focusBeforeSaveSync = null;
        }

        private void Update()
        {
            _saveSync?.Tick();
            if (_saveSync?.HasConflict == true)
            {
                if (!_saveConflictShown && CanApplyCloudSave())
                {
                    _saveConflictShown = true;
                    OpenSaveSync();
                }
            }
            else _saveConflictShown = false;
            _saveSyncMenu?.Refresh();

            if (_session == null)
            {
                if (_screen == AppScreen.DeckEditor) _deckEditor?.Update();
                else if (_menuPad != null)
                {
                    if (!_menuPad.HasPopup && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) ||
                        DeckPadInput.DirectTouchBegan()))
                        _menuPad.Clear(preserveSelection: true);
                    HandleMenuInput(_menuPadInput.Read(Time.unscaledTime), Input.GetKeyDown(KeyCode.Escape));
                    SyncFrontMenuFocus();
                }
                return;
            }
            _duelField?.Update();
            if (_session == null || _duelField.IsReadingLog || _duelField.IsBrowsingZone || _duelField.IsReadingCardDetails) return;

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
                    OpponentCharacter unlockedOpponent = null;
                    PresetDeckInfo unlockedDeck = null;
                    string rewardText = "";
                    if (_session.Winner >= 0 && CollectionProgress.HasStarter)
                    {
                        rewardText = CollectionProgress.TryAwardDuel(won, _activeOppChar.id, _activeOppDeck.id,
                            out int reward, out unlockedOpponent, out unlockedDeck, out string error)
                            ? $"듀얼 보상 +{reward:N0} DP  ·  보유 {CollectionProgress.Balance:N0} DP"
                            : error;
                    }
                    _modals.ShowResult(
                        won, draw, _activeOppChar, _session.TurnNumber,
                        Mathf.Max(0, _session.Players[_session.HumanPlayerIndex].lp),
                        Mathf.Max(0, _session.Players[_session.AiPlayerIndex].lp),
                        () => StartDuelMatch(_activeOppChar, _activeOppDeck, _activeMyDeck, _activePlayerFirst),
                        () =>
                        {
                            ShowScreen(AppScreen.OpponentSelect);
                            if (unlockedDeck != null) _oppSelect.FocusUnlockedDeck(unlockedDeck);
                        },
                        () => ShowScreen(AppScreen.MainMenu), rewardText, unlockedOpponent, unlockedDeck);
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
                _aiTimer += Time.deltaTime * DuelFieldUI.PlaybackRate;
                if (_aiTimer >= AiStepDelay)
                {
                    _aiTimer = 0f;
                    byte[] aiResp = _ai.ComputeResponse(_session, prompt);
                    _session.SubmitResponse(aiResp);
                    _duelField.Refresh();
                }
                return;
            }

            if (_session.ShouldPassChainPrompt(prompt, ChainConfirmation))
            {
                _shownHumanPrompt = null;
                _modals.HideAll();
                _session.SubmitResponseInt(-1);
                _duelField.Refresh();
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
                    _modals.ShowPrompt(_session, prompt, ChainConfirmation);
                    if (_modals.IsFieldCardSelection || _modals.IsChainPromptOpen) _duelField.Refresh();
                }
                else
                {
                    _modals.HideAll();
                }
            }
        }

        internal void CycleChainConfirmation()
        {
            if (_session == null || _session.IsFinished || _duelField.IsPaused ||
                _duelField.IsReadingLog || _duelField.IsBrowsingZone) return;

            var nextMode = ChainConfirmation switch
            {
                ChainConfirmationMode.Auto => ChainConfirmationMode.Off,
                ChainConfirmationMode.Off => ChainConfirmationMode.On,
                _ => ChainConfirmationMode.Auto,
            };
            var prompt = _shownHumanPrompt;
            if (prompt?.messageType == YgoConstants.MSG_SELECT_CHAIN &&
                prompt.candidates.Exists(candidate =>
                    _session.IsChainCandidateVisible(prompt, candidate, ChainConfirmation) !=
                    _session.IsChainCandidateVisible(prompt, candidate, nextMode)))
                _shownHumanPrompt = null;
            ChainConfirmation = nextMode;
            _duelField.RefreshChainConfirmation(ChainConfirmation);
        }

        private void SyncFrontMenuFocus()
        {
            if (_menuPad == null || _menuPad.HasPopup) return;
            var page = _saveSyncMenu?.Root.activeSelf == true ? _saveSyncMenu.Root : _screen switch
            {
                AppScreen.Title => _titleAndMenu.TitleRoot,
                AppScreen.MainMenu => _titleAndMenu.SettingsRoot.activeInHierarchy
                    ? _titleAndMenu.SettingsRoot : _titleAndMenu.MenuRoot,
                AppScreen.OpponentSelect => _oppSelect.JankenRoot.activeInHierarchy ? _oppSelect.JankenRoot : _oppSelect.Root,
                AppScreen.StarterSelection => _collectionShop?.StarterRoot,
                AppScreen.CardShop => _collectionShop?.ActiveShopRoot,
                AppScreen.ArchetypeCodex => _archetypeCodex.Root,
                _ => null,
            };
            if (page != null && page.activeInHierarchy)
                _menuPad.SyncSelectedFocus(page.transform);
        }

        private void HandleMenuInput(DeckPadFrame frame, bool escape)
        {
            bool cloudSave = _saveSyncMenu?.Root.activeSelf == true;
            bool settings = _titleAndMenu.SettingsRoot.activeInHierarchy;
            bool janken = _oppSelect.JankenRoot.activeInHierarchy;
            bool selectingStarter = _screen == AppScreen.StarterSelection;
            bool codex = _screen == AppScreen.ArchetypeCodex;
            bool collectionPage = selectingStarter || _screen == AppScreen.CardShop || codex;
            bool cancel = frame.Cancel || (escape && (cloudSave || settings || janken || _menuPad.HasPopup || collectionPage));
            if (!frame.HasInput && !cancel) return;

            GameObject scope = cloudSave ? _saveSyncMenu.Root : _screen switch
            {
                AppScreen.Title => _titleAndMenu.TitleRoot,
                AppScreen.MainMenu => settings ? _titleAndMenu.SettingsRoot : _titleAndMenu.MenuRoot,
                AppScreen.OpponentSelect => janken ? _oppSelect.JankenRoot : _oppSelect.Root,
                AppScreen.StarterSelection => _collectionShop?.StarterRoot,
                AppScreen.CardShop => _collectionShop?.ActiveShopRoot,
                AppScreen.ArchetypeCodex => _archetypeCodex.Root,
                _ => null,
            };
            if (scope == null || !scope.activeInHierarchy) return;
            if (!_menuPad.HasPopup)
            {
                if (_menuPad.HandleOpenedDropdown(scope.transform, frame.Direction, frame.Submit, cancel)) return;
                _menuPad.AdoptPointerFocus(scope.transform);
                if (collectionPage && !cancel)
                {
                    if (frame.Search)
                    {
                        _menuPad.OpenText(codex ? _archetypeCodex.SearchInput : _collectionShop.CatalogSearch(selectingStarter));
                        return;
                    }
                    if (frame.PreviousArea || frame.NextArea)
                    {
                        int delta = frame.PreviousArea ? -1 : 1;
                        if (codex) _archetypeCodex.ChangePage(delta);
                        else _collectionShop.ChangeCatalogPage(selectingStarter, delta);
                        _menuPad.AdoptPointerFocus(scope.transform);
                        return;
                    }
                }
                if (_screen == AppScreen.OpponentSelect && !janken && !cancel)
                {
                    if (frame.Search)
                    {
                        _menuPad.OpenText(_oppSelect.SearchInput);
                        return;
                    }
                    if (frame.PreviousArea || frame.NextArea)
                    {
                        _oppSelect.ChangePage(frame.PreviousArea ? -1 : 1, focusCharacter: true);
                        _menuPad.AdoptPointerFocus(scope.transform);
                        return;
                    }
                }
            }

            void Back()
            {
                if (cloudSave) CloseSaveSync();
                else if (settings) _titleAndMenu.CloseSettings();
                else if (janken) _oppSelect.CloseJanken();
                else if (selectingStarter) ShowScreen(AppScreen.Title);
                else if (_screen == AppScreen.CardShop)
                {
                    if (!_collectionShop.HandleShopBack()) ShowScreen(AppScreen.MainMenu);
                }
                else if (_screen is AppScreen.OpponentSelect or AppScreen.ArchetypeCodex) ShowScreen(AppScreen.MainMenu);
                else if (_screen == AppScreen.MainMenu) ShowScreen(AppScreen.Title);
            }
            _menuPad.Handle(scope.transform, frame.Direction, frame.Submit, cancel, Back);
            if (_screen == AppScreen.CardShop)
                _collectionShop.ScrollShopDescription(frame.DescriptionScroll);
        }

        private void OnHumanModalResponse(byte[] resp)
        {
            if (_session == null || _session.IsFinished ||
                _duelField.IsPresenting || _duelField.IsPaused || _duelField.IsBrowsingZone || _duelField.IsReadingCardDetails) return;
            _shownHumanPrompt = null;
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
            if (_screen == AppScreen.StarterSelection)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "02_starter.png"));
                yield break;
            }
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

            var opponents = DeckRepository.Characters.Where(character => CollectionProgress.IsOpponentUnlocked(character.id));
            var oppChar = opponents.FirstOrDefault(character => character.isMeta2026) ?? opponents.FirstOrDefault();
            var oppDeck = oppChar == null ? null : OpponentDeckLevels.GetDecks(oppChar.id)
                .FirstOrDefault(deck => CollectionProgress.IsOpponentDeckUnlocked(oppChar.id, deck.id));
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

        private void OnApplicationFocus(bool focused)
        {
            if (focused) _saveSync?.RequestSync();
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused) _saveSync?.RequestSync();
        }

        private void OnDestroy()
        {
            _saveSync?.Dispose();
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
