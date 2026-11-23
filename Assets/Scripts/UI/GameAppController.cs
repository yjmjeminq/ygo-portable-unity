using System;
using System.Collections;
using System.Collections.Generic;
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
        StoryMode,
        Tournament,
        Ranked,
    }

    public class GameAppController : MonoBehaviour
    {
        public float AiStepDelay { get; set; } = 0.25f;
        public AIDifficulty AiDifficulty { get; set; } = AIDifficulty.Smart;
        internal ChainConfirmationMode ChainConfirmation { get; private set; } = ChainConfirmationMode.Auto;
        private const string PersonalReplayPreference = "ygo.personal-duel-recording.v1";
        internal bool PersonalReplayEnabled { get; private set; }
        private LocalDuelReplayRecorder _personalReplay;

        private TitleAndMenuUI _titleAndMenu;
        private OpponentSelectUI _oppSelect;
        private DeckEditorUI _deckEditor;
        private DuelPromptModals _modals;
        private DuelFieldUI _duelField;
        private CollectionShopUI _collectionShop;
        private SecretPackToast _secretPackToast;
        private ArchetypeCodexUI _archetypeCodex;
        private StoryModeUI _storyMode;
        private DimensionTournamentUI _tournament;
        private OfflineRankedUI _ranked;
        private bool _storyDeckEditorOpen;
        private bool _rankedDeckEditorOpen;
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
        private StoryEpisodeData _activeStoryEpisode;
        private StoryBattlePlan _activeStoryPlan;
        private (int Seed, int Round, int Slots, int Opponent)? _activeTournament;
        private RankedMatch _activeRanked;

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
                if (Application.isMobilePlatform) Application.targetFrameRate = 60;
                LotdAudio.EnsureInit(gameObject);
                PersonalReplayEnabled = PlayerPrefs.GetInt(PersonalReplayPreference, 0) != 0;

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
                _secretPackToast = new SecretPackToast(canvas.transform);
                _archetypeCodex = new ArchetypeCodexUI(canvas.transform, () => ShowScreen(AppScreen.MainMenu));
                _storyMode = new StoryModeUI(this, canvas.transform);
                _tournament = new DimensionTournamentUI(this, canvas.transform);
                _ranked = new OfflineRankedUI(this, canvas.transform);

                Debug.Log("[GameApp] 6. Constructing OpponentSelectUI...");
                _oppSelect = new OpponentSelectUI(this, canvas.transform);

                Debug.Log("[GameApp] 7. Constructing DeckEditorUI...");
                _deckEditor = new DeckEditorUI(this, canvas.transform);

                Debug.Log("[GameApp] 8. Constructing DuelPromptModals...");
                _modals = new DuelPromptModals(canvas.transform, code => _duelField?.InspectCard(code), null,
                    TrySubmitHumanModalResponse);

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
            if (logType == 0)
            {
                _personalReplay?.NativeError();
                Debug.LogError($"[YGO-Core] {msg}");
            }
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
            if (screen != AppScreen.Tournament) _tournament.Close();
            if (screen != AppScreen.Ranked) _ranked.Close();
            if (screen == AppScreen.DeckEditor && _storyDeckEditorOpen) _storyMode.SuspendForDeckEditor();
            else if (screen != AppScreen.StoryMode) _storyMode.Close();
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
                _activeStoryEpisode = null;
                _activeTournament = null;
                _activeRanked = null;
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
            else if (screen == AppScreen.Tournament)
                _tournament.Open();
            else if (screen == AppScreen.Ranked)
                _ranked.Open();
            else if (screen == AppScreen.StoryMode)
            {
                if (_storyDeckEditorOpen && previousScreen == AppScreen.DeckEditor) _storyMode.ResumeFromDeckEditor();
                else _storyMode.Open();
            }
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
                    AppScreen.StoryMode => _storyMode.Root,
                    AppScreen.Tournament => _tournament.Root,
                    AppScreen.Ranked => _ranked.Root,
                    _ => null,
                };
                if (root != null) UiEntranceMotion.Play(root, .67f, .19f);
            }
            if (screen != AppScreen.DeckEditor) _storyDeckEditorOpen = false;
            if (screen != AppScreen.DeckEditor) _rankedDeckEditorOpen = false;
            SyncFrontMenuFocus();
        }

        internal void OpenStoryDeckEditor()
        {
            if (_screen != AppScreen.StoryMode) return;
            _storyDeckEditorOpen = true;
            ShowScreen(AppScreen.DeckEditor);
        }

        internal void OpenRankedDeckEditor()
        {
            if (_screen != AppScreen.Ranked) return;
            _rankedDeckEditorOpen = true;
            ShowScreen(AppScreen.DeckEditor);
        }

        internal void CloseDeckEditor() => ShowScreen(_rankedDeckEditorOpen ? AppScreen.Ranked :
            _storyDeckEditorOpen ? AppScreen.StoryMode : AppScreen.MainMenu);

        internal void OpenSecretPackShop(string id)
        {
            ShowScreen(AppScreen.CardShop);
            if (_screen == AppScreen.CardShop) _collectionShop.ShowPackFromLookup(id);
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

        internal void StartStoryDuel(StoryEpisodeData episode, UserDeck myDeck, StoryBattlePlan plan = default)
        {
            if (_screen != AppScreen.StoryMode || !_storyMode.CanPlay(episode)) return;
            if (!CollectionProgress.CanEquipRaidSupports(plan.Supports, _storyMode.GetRaid(episode).HasValue))
            {
                _storyMode.ShowStatus("장착한 아이템을 확인하세요.");
                return;
            }
            _activeStoryEpisode = episode;
            _activeStoryPlan = plan;
            var opponent = DeckRepository.GetCharacter(episode.charId);
            var (deck, level, levelCount) = _storyMode.GetOpponentMatch(episode);
            float deckStrength = levelCount > 1 ? (level - 1f) / (levelCount - 1f) : (episode.chapter - 1f) / 4f;
            float planningScale = _storyMode.GetRaid(episode) != null ? ComboSearch.MaxPlanningScale :
                Mathf.Lerp(1f, ComboSearch.MaxPlanningScale, deckStrength);
            StartDuelMatch(opponent, deck, myDeck, playerGoesFirst: true, isStoryDuel: true, planningScale: planningScale);
        }

        internal void StartTournamentDuel(DimensionTournament run, UserDeck myDeck)
        {
            var current = CollectionProgress.Tournament;
            if (_screen != AppScreen.Tournament || current?.Active != true || current.seed != run.seed ||
                current.round != run.round || current.Opponent <= 0 || current.PendingAiMatches().Any()) return;
            _activeTournament = (current.seed, current.round, current.PlayerNode, current.Opponent);
            var opponent = DeckRepository.GetCharacter(current.Opponent);
            var deck = DimensionTournament.HighestDeck(current.Opponent);
            StartDuelMatch(opponent, deck, myDeck, playerGoesFirst: true, isStoryDuel: false,
                planningScale: ComboSearch.MaxPlanningScale, isTournamentDuel: true);
        }

        internal void StartRankedDuel(RankedMatch match)
        {
            if (_screen != AppScreen.Ranked || match == null || !_ranked.CanStart(match)) return;
            _activeRanked = match;
            StartDuelMatch(match.Opponent.Character, match.Opponent.Deck, match.Deck, match.PlayerFirst,
                isStoryDuel: false, planningScale: match.Opponent.PlanningScale, isRankedDuel: true);
        }

        public void StartDuelMatch(OpponentCharacter oppChar, PresetDeckInfo oppDeck, UserDeck myDeck,
            bool playerGoesFirst, bool isStoryDuel = false) =>
            StartDuelMatch(oppChar, oppDeck, myDeck, playerGoesFirst, isStoryDuel,
                planningScale: isStoryDuel ? 1f : ComboSearch.MaxPlanningScale);

        private void StartDuelMatch(OpponentCharacter oppChar, PresetDeckInfo oppDeck, UserDeck myDeck,
            bool playerGoesFirst, bool isStoryDuel, float planningScale, bool isTournamentDuel = false, bool isRankedDuel = false)
        {
            bool isCampaignMatch = isStoryDuel || isTournamentDuel || isRankedDuel;
            if (oppChar == null || oppDeck == null || oppDeck.charId != oppChar.id ||
                (isStoryDuel && _activeStoryEpisode?.charId != oppChar.id) ||
                (isTournamentDuel && _activeTournament?.Opponent != oppChar.id) ||
                (isRankedDuel && (_activeRanked?.Opponent.Deck != oppDeck)) ||
                (!isCampaignMatch && !CollectionProgress.IsOpponentDeckUnlocked(oppChar.id, oppDeck.id)))
            {
                ShowDuelStartFailure(isStoryDuel, "목록에 있는 상대와 덱을 선택하세요.", isTournamentDuel, isRankedDuel);
                return;
            }
            if (!isCampaignMatch)
            {
                _activeStoryEpisode = null;
                _activeTournament = null;
                oppDeck = OpponentDeckLevels.GetDecks(oppChar.id).First(deck => deck.id == oppDeck.id);
            }
            var duelDeck = myDeck;
            string reason;
            bool deckReady = CollectionProgress.Initialized
                ? CollectionProgress.TryPrepareDeckForDuel(myDeck, out duelDeck, out reason)
                : CanStartWithDeck(myDeck, out reason);
            if (!deckReady)
            {
                ShowDuelStartFailure(isStoryDuel, reason, isTournamentDuel, isRankedDuel);
                return;
            }
            _activeOppChar = oppChar;
            _activeOppDeck = oppDeck;
            _activeMyDeck = myDeck;
            _activePlayerFirst = playerGoesFirst;

            _session?.Dispose();
            _personalReplay = null;
            if (PersonalReplayEnabled && !Application.isBatchMode)
            {
                try
                {
                    _personalReplay = new LocalDuelReplayRecorder(Application.persistentDataPath, myDeck.id,
                        oppChar.id, oppDeck.id, AiDifficulty.ToString(), ChainConfirmation.ToString());
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Personal replay] Could not enable local recording ({exception.GetType().Name}).");
                }
            }
            var difficulty = isCampaignMatch ? AIDifficulty.Smart : AiDifficulty;
            var raid = isStoryDuel ? _storyMode.GetRaid(_activeStoryEpisode) : null;
            var opening = isStoryDuel ? _activeStoryPlan.Apply(raid) : ((DuelOpening Player, DuelOpening Opponent)?)null;
            if (isTournamentDuel)
                opening = (new DuelOpening(8000, 5, 0), DimensionTournament.Opening(_activeTournament.Value.Slots, oppDeck));
            _session = new DuelSession(recordPlanningReplay: difficulty == AIDifficulty.Smart, playerReplay: _personalReplay);
            _ai = new TacticalAI(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded(),
                searchCombos: true, planningScale: planningScale) { Difficulty = difficulty };
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
                p0GoesFirst: playerGoesFirst, playerDeckAppearance: duelDeck, opponentOpening: opening?.Opponent,
                opponentDestiny: GetOpponentDestinyCards(oppDeck), playerOpening: opening?.Player))
            {
                ShowDuelStartFailure(isStoryDuel, "듀얼을 시작하지 못했습니다.", isTournamentDuel, isRankedDuel);
                return;
            }
            if (isStoryDuel && !CollectionProgress.TryConsumeRaidSupports(_activeStoryPlan.Supports, raid.HasValue, out string error))
            {
                ShowDuelStartFailure(isStoryDuel, error);
                return;
            }
            CollectionProgress.RememberDuelDeck(myDeck.id);
            _duelField.Refresh();
        }

        private static uint[] GetOpponentDestinyCards(PresetDeckInfo deck)
        {
            if (deck.main.Contains(deck.sigCard)) return new[] { deck.sigCard };
            return deck.main.Distinct().Select(CardDatabase.Get)
                .Where(card => card?.IsMonster == true && !card.IsExtra)
                .OrderByDescending(card => card.atk >= 2400 && (card.type & YgoConstants.TYPE_EFFECT) != 0)
                .ThenByDescending(card => CardRarityCatalog.GetRarity(card.code))
                .ThenByDescending(card => card.atk).Take(2).Select(card => card.code).ToArray();
        }

        private void ShowDuelStartFailure(bool isStoryDuel, string message, bool isTournamentDuel = false, bool isRankedDuel = false)
        {
            if (isRankedDuel)
            {
                _ranked.Close();
                ShowScreen(AppScreen.Ranked);
                _ranked.ShowStatus(message);
                return;
            }
            ShowScreen(isTournamentDuel ? AppScreen.Tournament : isStoryDuel ? AppScreen.StoryMode : AppScreen.OpponentSelect);
            if (isTournamentDuel) { _tournament.ShowStatus(message); return; }
            if (isStoryDuel) _storyMode.ShowStatus(message);
            else _oppSelect.ShowStatus(message, true);
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
            _secretPackToast?.Update(Time.unscaledDeltaTime);
            _personalReplay?.PollWarning();
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
            if (_screen == AppScreen.Ranked) _ranked?.Update(Time.unscaledDeltaTime);

            if (_session == null)
            {
                if (_screen == AppScreen.StoryMode) _storyMode?.Update(Time.unscaledDeltaTime);
                if (_screen == AppScreen.Tournament) _tournament?.Update(Time.unscaledDeltaTime);
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
                    if (_activeRanked is { } ranked)
                    {
                        bool hasResult = _session.Winner >= 0;
                        ShowScreen(AppScreen.Ranked);
                        if (hasResult) _ranked.ShowResult(ranked, won ? RankedOutcome.Win : draw ? RankedOutcome.Draw : RankedOutcome.Loss);
                        else _ranked.ShowStatus("듀얼 결과를 확인하지 못했습니다.");
                        SyncFrontMenuFocus();
                        return;
                    }
                    if (_activeTournament is { } match)
                    {
                        var opponent = _activeOppChar;
                        CollectionProgress.TryFinishTournamentRound(match.Seed, match.Round, match.Opponent, won,
                            out var prize, out string error);
                        ShowScreen(AppScreen.Tournament);
                        _tournament.ShowResult(opponent, match.Slots, won, draw, prize, error);
                        SyncFrontMenuFocus();
                        return;
                    }
                    if (_activeStoryEpisode is { } episode)
                    {
                        var raid = _storyMode.GetRaid(episode);
                        var plan = _activeStoryPlan;
                        string storyReward = CollectionProgress.TryAwardStoryDuel(episode.id, won, raid, _activeOppDeck, plan,
                            out var prize, out string error) ? FormatStoryPrize(prize) : error;
                        ShowScreen(AppScreen.StoryMode);
                        _storyMode.ShowDuelResult(episode, won, draw, storyReward, plan.Stance);
                        SyncFrontMenuFocus();
                        return;
                    }
                    string rewardText = "";
                    if (_session.Winner >= 0 && CollectionProgress.HasStarter)
                    {
                        rewardText = CollectionProgress.TryAwardDuel(won, _activeOppChar.id, _activeOppDeck.id,
                            out int reward, out string error)
                            ? $"듀얼 보상 +{reward:N0} DP  ·  보유 {CollectionProgress.Balance:N0} DP"
                            : error;
                    }
                    _modals.ShowResult(
                        won, draw, _activeOppChar, _session.TurnNumber,
                        Mathf.Max(0, _session.Players[_session.HumanPlayerIndex].lp),
                        Mathf.Max(0, _session.Players[_session.AiPlayerIndex].lp),
                        () => StartDuelMatch(_activeOppChar, _activeOppDeck, _activeMyDeck, _activePlayerFirst),
                        () => ShowScreen(AppScreen.OpponentSelect),
                        () => ShowScreen(AppScreen.MainMenu), rewardText);
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
                if (prompt.messageType == YgoConstants.MSG_DESTINY_DRAW)
                {
                    _session.SubmitAutomaticResponseInt(1, "opponent-destiny-draw");
                    _duelField.Refresh();
                    return;
                }
                _aiTimer += Time.deltaTime * DuelFieldUI.PlaybackRate;
                if (_ai.AdvanceComboPlanning(_session, prompt) && _aiTimer >= AiStepDelay)
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
                _session.SubmitAutomaticResponseInt(-1, "automatic-chain-pass");
                _duelField.Refresh();
                return;
            }

            // The core waits on this exact prompt until a response is accepted. Reopen it if an overlay hid it.
            bool needsModal = prompt.messageType is not (YgoConstants.MSG_SELECT_IDLECMD or YgoConstants.MSG_SELECT_BATTLECMD or
                YgoConstants.MSG_SELECT_PLACE or YgoConstants.MSG_SELECT_DISFIELD);
            if (_shownHumanPrompt != prompt || needsModal && !_modals.IsShowingNativePrompt(_session, prompt))
            {
                _shownHumanPrompt = prompt;
                _duelField.Refresh();

                if (needsModal)
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

        private static string FormatStoryPrize(CollectionProgress.StoryPrize prize)
        {
            var lines = new List<string> { $"듀얼 보상 +{prize.Dp:N0} DP · 가루 +{prize.Dust:N0}" };
            if (prize.RaidCard is { } raidCard) lines.Add(DescribeCard(raidCard));
            if (prize.ThemeCard is { } themeCard) lines.Add($"테마 드롭 · {DescribeCard(themeCard)}");
            if (prize.Bond > 0) lines.Add($"유대도 {prize.Bond:N0} (+1)");
            if (prize.Support != RaidSupport.None)
                lines.Add($"전술 서포트 · {RaidSupportCatalog.Items[RaidSupportCatalog.IndexOf(prize.Support)].Name}");
            return string.Join("\n", lines);

            static string DescribeCard(PackPull card) => $"{CardRarityCatalog.GetLabel(CardRarityCatalog.GetRarity(card.Code))} · " +
                $"{CardDatabase.GetName(card.Code)}{(card.Overframe ? " · OVERFRAME" : "")}";
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
                AppScreen.StoryMode => _storyMode.ActiveRoot,
                AppScreen.Tournament => _tournament.ActiveRoot,
                AppScreen.Ranked => _ranked.ActiveRoot,
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
            bool selectingOpponent = _screen == AppScreen.OpponentSelect;
            bool selectingDecks = selectingOpponent && _oppSelect.IsDeckSetupOpen;
            bool selectingStarter = _screen == AppScreen.StarterSelection;
            bool codex = _screen == AppScreen.ArchetypeCodex;
            bool collectionPage = selectingStarter || _screen == AppScreen.CardShop || codex;
            bool story = _screen == AppScreen.StoryMode;
            bool tournament = _screen == AppScreen.Tournament;
            bool ranked = _screen == AppScreen.Ranked;
            bool cancel = frame.Cancel || (escape && (cloudSave || settings || selectingOpponent || _menuPad.HasPopup || collectionPage || story || tournament || ranked));
            if (!frame.HasInput && !cancel) return;

            GameObject scope = cloudSave ? _saveSyncMenu.Root : _screen switch
            {
                AppScreen.Title => _titleAndMenu.TitleRoot,
                AppScreen.MainMenu => settings ? _titleAndMenu.SettingsRoot : _titleAndMenu.MenuRoot,
                AppScreen.OpponentSelect => janken ? _oppSelect.JankenRoot : _oppSelect.Root,
                AppScreen.StarterSelection => _collectionShop?.StarterRoot,
                AppScreen.CardShop => _collectionShop?.ActiveShopRoot,
                AppScreen.ArchetypeCodex => _archetypeCodex.Root,
                AppScreen.StoryMode => _storyMode.ActiveRoot,
                AppScreen.Tournament => _tournament.ActiveRoot,
                AppScreen.Ranked => _ranked.ActiveRoot,
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
                if (selectingOpponent && !janken && !selectingDecks && !cancel)
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
                else if (selectingDecks) _oppSelect.CloseDeckSetup();
                else if (selectingStarter) ShowScreen(AppScreen.Title);
                else if (_screen == AppScreen.CardShop)
                {
                    if (!_collectionShop.HandleShopBack()) ShowScreen(AppScreen.MainMenu);
                }
                else if (story) _storyMode.Back();
                else if (tournament) _tournament.Back();
                else if (ranked) _ranked.Back();
                else if (_screen is AppScreen.OpponentSelect or AppScreen.ArchetypeCodex) ShowScreen(AppScreen.MainMenu);
                else if (_screen == AppScreen.MainMenu) ShowScreen(AppScreen.Title);
            }
            _menuPad.Handle(scope.transform, frame.Direction, frame.Submit, cancel, Back);
            if (_screen == AppScreen.CardShop)
                _collectionShop.ScrollShopDescription(frame.DescriptionScroll);
        }

        private bool TrySubmitHumanModalResponse(DuelSession session, DuelPrompt prompt, byte[] response)
        {
            if (session == null || prompt == null || _session != session || session.IsFinished || !ReferenceEquals(session.ActivePrompt, prompt) ||
                prompt.playerId != session.HumanPlayerIndex || _duelField.IsPresenting || _duelField.IsPaused ||
                _duelField.IsReadingLog || _duelField.IsBrowsingZone || _duelField.IsReadingCardDetails)
                return false;
            _shownHumanPrompt = null;
            session.SubmitResponse(response);
            _duelField.Refresh();
            return true;
        }

        internal void SetPersonalReplayEnabled(bool enabled)
        {
            PersonalReplayEnabled = enabled;
            try
            {
                PlayerPrefs.SetInt(PersonalReplayPreference, enabled ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Personal replay] Could not persist the recording preference ({exception.GetType().Name}).");
            }
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
            _tournament?.Close();
            _secretPackToast?.Dispose();
            _saveSync?.Dispose();
            Debug.Log($"[GameApp] OnDestroy called! Stack:\n{Environment.StackTrace}");
            _session?.Dispose();
            _personalReplay?.Dispose();
            _personalReplay?.PollWarning();
            _session = null;
        }

        private void OnApplicationQuit()
        {
            Debug.Log($"[GameApp] OnApplicationQuit called! Stack:\n{Environment.StackTrace}");
            _session?.Dispose();
            _personalReplay?.Dispose();
            _personalReplay?.PollWarning();
            _session = null;
            CardArtLoader.Shutdown();
            YgoNativeBridge.Shutdown();
        }
    }
}
