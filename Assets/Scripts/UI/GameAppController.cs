using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        TagForceWorld,
        Campaigns,
        BeyondDestiny,
    }

    public partial class GameAppController : MonoBehaviour
    {
        public float AiStepDelay { get; set; } = 0.25f;
        public AIDifficulty AiDifficulty { get; set; } = AIDifficulty.Master;
        internal ChainConfirmationMode ChainConfirmation { get; private set; } = ChainConfirmationMode.Auto;
        private const string AiDifficultyPreference = "ygo.ai-difficulty.v1";
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
        private TagForceWorldUI _tagForceWorld;
        private bool _storyDeckEditorOpen;
        private bool _rankedDeckEditorOpen;
        private bool _tagForceDeckEditorOpen;
        private bool _rpgCardShopOpen;
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
        private DuelPrompt _timedAiPrompt;
        private double _aiPromptStartedAt;
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
        private TagForceDuel _activeTagForce;

        private bool _initialized;
        private RectTransform _cardDataSetup;
        private CanvasGroup _cardDataControls;
        private Text _cardDataStatus;
        private string _cardDataFolder;
        private string[] _cardDataFiles;

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
            if (Application.platform == RuntimePlatform.IPhonePlayer &&
                !File.Exists(Path.Combine(Application.streamingAssetsPath, "cards_kr.json")))
            {
                _cardDataFolder = Path.Combine(Application.persistentDataPath, "CardData");
                _cardDataFiles = CardDataPack.ReadFileList();
                string installed = CardDataPack.FindInstalled(_cardDataFolder, _cardDataFiles);
                if (installed != null)
                    StartWithCardData(installed);
                else
                    StartCoroutine(ImportCardData(selectFile: false));
                return;
            }

            try { InitializeGame(Application.streamingAssetsPath); }
            catch (Exception error) { Debug.LogError($"[GameApp] Fatal Exception in Start(): {error}"); }
        }

        private void StartWithCardData(string directory)
        {
            if (directory == null)
            {
                ShowCardDataSetup();
                _cardDataStatus.text = "설치된 카드 데이터를 찾지 못했습니다. CardData.zip을 다시 가져오세요.";
                return;
            }
            CardDataPack.Root = directory;
            try
            {
                InitializeGame(directory);
                if (_cardDataSetup != null)
                {
                    _cardDataSetup.gameObject.SetActive(false);
                    Destroy(_cardDataSetup.gameObject);
                }
            }
            catch (Exception error)
            {
                Debug.LogError($"[CardData] Startup failed: {error}");
                if (YgoNativeBridge.IsInitialized && !CardDatabase.IsLoaded)
                    YgoNativeBridge.Shutdown();
                ShowCardDataSetup();
                _cardDataSetup.SetAsLastSibling();
                _cardDataStatus.text = "카드 데이터를 불러오지 못했습니다. 올바른 CardData.zip을 다시 가져오세요.";
            }
        }

        private void ShowCardDataSetup()
        {
            if (_cardDataSetup != null) return;
            EnsureEventSystem();
            var canvas = EnsureMainCanvas();
            _cardDataSetup = DuelUiKit.FillRect("CardDataSetup", canvas.transform);
            _cardDataSetup.gameObject.AddComponent<Image>().color = new Color(.025f, .055f, .10f, 1f);
            var panel = DuelUiKit.CreateRect("CardDataPanel", _cardDataSetup, new Vector2(.18f, .20f),
                new Vector2(.82f, .80f), Vector2.zero, Vector2.zero);
            _cardDataControls = panel.gameObject.AddComponent<CanvasGroup>();
            var title = DuelUiKit.CreateRect("Title", panel, new Vector2(0f, .71f), Vector2.one, Vector2.zero, Vector2.zero);
            DuelUiKit.AddFittedText(title, "Label", "카드 데이터 설치", 42, 30,
                TextAnchor.MiddleCenter, DuelUiKit.TextPrimary, FontStyle.Bold);
            var hint = DuelUiKit.CreateRect("Hint", panel, new Vector2(0f, .47f), new Vector2(1f, .70f), Vector2.zero, Vector2.zero);
            DuelUiKit.AddFittedText(hint, "Label", "파일 앱에서 CardData.zip을 선택하세요.\n카드 정보와 일러스트를 한 번 설치하면 오프라인에서도 사용할 수 있습니다.", 25, 16,
                TextAnchor.MiddleCenter, DuelUiKit.TextMuted);
            var status = DuelUiKit.CreateRect("Status", panel, new Vector2(0f, .28f), new Vector2(1f, .46f), Vector2.zero, Vector2.zero);
            _cardDataStatus = DuelUiKit.AddFittedText(status, "Label", "", 24, 15, TextAnchor.MiddleCenter, DuelUiKit.GoldAccent);
            var import = DuelUiKit.CreateRect("Import", panel, new Vector2(.12f, .06f), new Vector2(.50f, .24f), Vector2.zero, Vector2.zero);
            DuelUiKit.CreateButton(import, "Button", "ZIP 가져오기", () => StartCoroutine(ImportCardData(selectFile: true)), 24, gold: true);
            var retry = DuelUiKit.CreateRect("Retry", panel, new Vector2(.52f, .06f), new Vector2(.88f, .24f), Vector2.zero, Vector2.zero);
            DuelUiKit.CreateButton(retry, "Button", "앱 폴더 다시 확인", () => StartCoroutine(ImportCardData(selectFile: false)), 22);
        }

        private IEnumerator ImportCardData(bool selectFile)
        {
            ShowCardDataSetup();
            if (!_cardDataControls.interactable) yield break;
            string localArchive = Path.Combine(Application.persistentDataPath, CardDataPack.ZipName);
            if (!selectFile && !File.Exists(localArchive))
            {
                _cardDataStatus.text = "아직 카드 데이터가 설치되지 않았습니다.";
                yield break;
            }

            _cardDataControls.interactable = false;
            _cardDataStatus.text = "카드 데이터와 일러스트를 가져오는 중입니다…";
            void Install(string archive) => CardDataPack.Install(archive, _cardDataFolder, _cardDataFiles);
            var job = selectFile ? DuelVisualsZipPicker.Import(Install) : Task.Run(() =>
            {
                Install(localArchive);
                return true;
            });
            while (!job.IsCompleted) yield return null;

            _cardDataControls.interactable = true;
            if (job.IsFaulted)
            {
                Exception error = job.Exception.GetBaseException();
                Debug.LogWarning($"[CardData] Import failed: {error}");
                _cardDataStatus.text = error is InvalidDataException ? error.Message :
                    "ZIP을 읽지 못했습니다. 다운로드 상태와 저장 공간을 확인하세요.";
            }
            else if (job.IsCanceled || !job.Result)
                _cardDataStatus.text = "ZIP 선택을 취소했습니다.";
            else
                StartWithCardData(CardDataPack.FindInstalled(_cardDataFolder, _cardDataFiles));
        }

        private void InitializeGame(string cardDataPath)
        {
            Debug.Log($"[GameApp] Start() starting... StreamingAssets: {Application.streamingAssetsPath}");
            Application.runInBackground = true;
            if (Application.isMobilePlatform) Application.targetFrameRate = 60;
            DuelAudio.EnsureInit(gameObject);
            PersonalReplayEnabled = PlayerPrefs.GetInt(PersonalReplayPreference, 0) != 0;
            AiDifficulty = ReadAiDifficulty();

            if (!YgoNativeBridge.IsInitialized)
            {
                int rc = YgoNativeBridge.Initialize(cardDataPath, OnNativeLog);
                if (rc != 0 && CardDataPack.Root != null)
                    throw new InvalidDataException($"카드 효과 데이터를 초기화하지 못했습니다. ({rc})");
                if (rc != 0) Debug.LogError($"[GameApp] YGO_Init failed rc={rc}");
                else Debug.Log("[GameApp] YgoNativeBridge initialized.");
            }

            Debug.Log("[GameApp] 1. Loading CardDatabase...");
            if (!CardDatabase.IsLoaded)
                CardDatabase.Load(cardDataPath);

            Debug.Log("[GameApp] 2. Initializing CardArtLoader...");
            CardArtLoader.Initialize(cardDataPath);

            var saveFiles = new SaveSyncFiles(Application.persistentDataPath);
            saveFiles.Recover();
            Debug.Log("[GameApp] 3. Loading DeckRepository...");
            if (DeckRepository.PresetDecks.Count == 0)
                DeckRepository.Load(Application.streamingAssetsPath, Application.persistentDataPath, seedDefaultDecks: false);
            CollectionProgress.Load(Application.persistentDataPath);

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
                CloseCollectionShop,
                _ => ShowScreen(AppScreen.MainMenu));
            _secretPackToast = new SecretPackToast(canvas.transform);
            _archetypeCodex = new ArchetypeCodexUI(canvas.transform, () => ShowScreen(AppScreen.MainMenu));
            _storyMode = new StoryModeUI(this, canvas.transform);
            _tournament = new DimensionTournamentUI(this, canvas.transform);
            _ranked = new OfflineRankedUI(this, canvas.transform);
            _tagForceWorld = new TagForceWorldUI(this, canvas.transform);
            _beyondDestiny = new BeyondDestinyUI(this, canvas.transform);

            Debug.Log("[GameApp] 6. Constructing OpponentSelectUI...");
            _oppSelect = new OpponentSelectUI(this, canvas.transform);

            Debug.Log("[GameApp] 7. Constructing DeckEditorUI...");
            _deckEditor = new DeckEditorUI(this, canvas.transform);

            Debug.Log("[GameApp] 8. Constructing DuelPromptModals...");
            _modals = new DuelPromptModals(canvas.transform, code => _duelField?.InspectCard(code), null,
                TrySubmitHumanModalResponse);

            Debug.Log("[GameApp] 9. Constructing DuelFieldUI...");
            _duelField = new DuelFieldUI(this, canvas.transform, _modals);
            _menuPad = new DeckPadUi(DuelUiKit.FillRect("MenuPad", canvas.transform),
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
                var go = new GameObject("DuelCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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

        internal void OpenGxRpgField()
        {
            if (!_tagForceWorld.PrepareRpgField(openIntro: true)) return;
            ShowScreen(AppScreen.TagForceWorld);
        }

        internal void OpenRpgFieldEpisode(int index)
        {
            if (_screen != AppScreen.TagForceWorld || !_tagForceWorld.CanStartRpgEpisode(index)) return;
            _storyMode.UseRpgCampaign(true, fromField: true);
            ShowScreen(AppScreen.StoryMode);
            _storyMode.OpenFieldEpisode(index);
        }

        internal void ReturnToRpgField(string message = "", bool afterStoryEvent = false)
        {
            if (afterStoryEvent && _tagForceWorld.BeginGxStoryReturn(ShowField)) return;
            ShowField();

            void ShowField()
            {
                ShowScreen(AppScreen.TagForceWorld);
                _tagForceWorld.ShowStatus(message);
            }
        }

        internal TagForceWorldUI RpgField => _tagForceWorld;

        internal void OpenRpgCardShop()
        {
            if (_screen != AppScreen.TagForceWorld) return;
            _rpgCardShopOpen = true;
            ShowScreen(AppScreen.CardShop);
        }

        private void CloseCollectionShop() => ShowScreen(_screen == AppScreen.StarterSelection ? AppScreen.Title :
            _rpgCardShopOpen ? AppScreen.TagForceWorld : AppScreen.MainMenu);

        internal void OpenRpgRecap() => OpenRpgRecap(_tagForceWorld.RpgClears - 1);

        internal void OpenRpgRecap(int episode, string cutscene = null)
        {
            if (_screen != AppScreen.TagForceWorld || episode < 0 || episode >= _tagForceWorld.RpgClears) return;
            _storyMode.UseRpgCampaign(true, fromField: true);
            ShowScreen(AppScreen.StoryMode);
            _storyMode.OpenFieldRecap(episode, cutscene);
        }

        public void ShowScreen(AppScreen screen)
        {
            if (!_initialized) return;
            if (CollectionProgress.Initialized && !CollectionProgress.HasStarter && screen != AppScreen.Title)
                screen = AppScreen.StarterSelection;

            var previousScreen = _screen;
            bool fieldStory = screen == AppScreen.StoryMode && _storyMode.FromRpgField;
            bool fieldStoryDeckEditor = screen == AppScreen.DeckEditor && _storyDeckEditorOpen && _storyMode.FromRpgField;
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
            if (screen is not (AppScreen.Campaigns or AppScreen.BeyondDestiny)) _beyondDestiny?.Close();
            if (screen != AppScreen.BeyondDestiny) StopBeyondDestinyAudio();
            if (fieldStoryDeckEditor) _tagForceWorld.Root.SetActive(false);
            else if (screen != AppScreen.TagForceWorld && !fieldStory) _tagForceWorld?.Close();
            if (screen == AppScreen.DeckEditor && _storyDeckEditorOpen) _storyMode.SuspendForDeckEditor();
            else if (screen != AppScreen.StoryMode) _storyMode.Close();
            _oppSelect.Root.SetActive(screen == AppScreen.OpponentSelect);
            _oppSelect.TurnOrderRoot.SetActive(false);
            if (screen != AppScreen.DeckEditor) _deckEditor.Close();
            _duelField.Root.SetActive(screen == AppScreen.Duel);

            if (screen == AppScreen.Duel)
            {
                DuelAudio.PlayDuelBgm();
            }
            else
            {
                if (screen != AppScreen.TagForceWorld && !fieldStory && !fieldStoryDeckEditor) DuelAudio.PlayMenuBgm();
                _activeStoryEpisode = null;
                _activeTournament = null;
                _activeRanked = null;
                _activeTagForce = null;
                _activeBeyondDestiny = null;
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
            else if (screen == AppScreen.Campaigns)
                _beyondDestiny.OpenSelection();
            else if (screen == AppScreen.BeyondDestiny)
                _beyondDestiny.OpenCampaign();
            else if (screen == AppScreen.TagForceWorld)
                _tagForceWorld.Open(previousScreen is AppScreen.DeckEditor or AppScreen.Duel or AppScreen.TagForceWorld,
                    restoreRpgPage: previousScreen == AppScreen.DeckEditor && _tagForceDeckEditorOpen);
            else if (screen == AppScreen.StoryMode)
            {
                if (_storyDeckEditorOpen && previousScreen == AppScreen.DeckEditor)
                {
                    if (fieldStory) _tagForceWorld.Root.SetActive(true);
                    _storyMode.ResumeFromDeckEditor();
                }
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
                    AppScreen.TagForceWorld => _tagForceWorld.Root,
                    AppScreen.Campaigns or AppScreen.BeyondDestiny => _beyondDestiny.Root,
                    _ => null,
                };
                if (root != null) UiEntranceMotion.Play(root, .67f, .19f);
            }
            if (screen != AppScreen.DeckEditor) _storyDeckEditorOpen = false;
            if (screen != AppScreen.DeckEditor) _rankedDeckEditorOpen = false;
            if (screen != AppScreen.DeckEditor) _tagForceDeckEditorOpen = false;
            if (screen != AppScreen.CardShop) _rpgCardShopOpen = false;
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

        internal void OpenTagForceDeckEditor()
        {
            if (_screen != AppScreen.TagForceWorld) return;
            _tagForceDeckEditorOpen = true;
            ShowScreen(AppScreen.DeckEditor);
        }

        internal void CloseDeckEditor() => ShowScreen(_tagForceDeckEditorOpen ? AppScreen.TagForceWorld :
            _rankedDeckEditorOpen ? AppScreen.Ranked :
            _storyDeckEditorOpen ? AppScreen.StoryMode : AppScreen.MainMenu);

        internal void OpenSecretPackShop(string id)
        {
            ShowScreen(AppScreen.CardShop);
            if (_screen == AppScreen.CardShop) _collectionShop.ShowPackFromLookup(id);
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

        internal void StartStoryDuel(StoryEpisodeData episode, UserDeck myDeck, bool playerGoesFirst, StoryBattlePlan plan = default)
        {
            if (_screen != AppScreen.StoryMode || !_storyMode.CanPlay(episode) || episode.isCutsceneOnly) return;
            if (episode.forcedPlayerDeckId != 0)
            {
                myDeck = _storyMode.ForcedDeckFor(episode);
                if (myDeck == null) { _storyMode.ShowStatus("시점 전용 덱을 불러오지 못했습니다."); return; }
            }
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
            bool gxRpg = _storyMode.IsGxRpg;
            bool elite = levelCount > 0 && level >= Math.Min(9, levelCount);
            float planningScale = elite || _storyMode.GetRaid(episode) != null ? ComboSearch.MaxPlanningScale :
                gxRpg ? GxStoryPlanningScale(deck?.difficultyTier ?? 1) : Mathf.Lerp(1f, ComboSearch.MaxPlanningScale, deckStrength);
            StartDuelMatch(opponent, deck, myDeck, playerGoesFirst, isStoryDuel: true, planningScale: planningScale,
                opponentDifficulty: gxRpg || elite ? AIDifficulty.Master : AiDifficulty);
        }

        internal static float GxStoryPlanningScale(int difficultyTier) =>
            Mathf.Lerp(1f, ComboSearch.MaxPlanningScale, (Mathf.Clamp(difficultyTier, 1, 4) - 1f) / 3f);

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

        internal void StartTagForceDuel(TagForceDuel duel, UserDeck player)
        {
            if (_screen != AppScreen.TagForceWorld || duel == null) return;
            var opponent = DeckRepository.GetCharacter(duel.Form.formCharId);
            _activeTagForce = duel;
            StartDuelMatch(opponent, duel.Deck, player, playerGoesFirst: true, isStoryDuel: false,
                planningScale: Mathf.Lerp(1f, ComboSearch.MaxPlanningScale,
                    (duel.Level - 1f) / Mathf.Max(1, duel.Form.Decks.Count - 1)), isTagForceDuel: true);
        }

        public void StartDuelMatch(OpponentCharacter oppChar, PresetDeckInfo oppDeck, UserDeck myDeck,
            bool playerGoesFirst, bool isStoryDuel = false) =>
            StartDuelMatch(oppChar, oppDeck, myDeck, playerGoesFirst, isStoryDuel,
                planningScale: isStoryDuel ? 1f : ComboSearch.MaxPlanningScale);

        private void StartDuelMatch(OpponentCharacter oppChar, PresetDeckInfo oppDeck, UserDeck myDeck,
            bool playerGoesFirst, bool isStoryDuel, float planningScale, bool isTournamentDuel = false, bool isRankedDuel = false,
            bool isTagForceDuel = false, AIDifficulty? opponentDifficulty = null, bool isBeyondDestiny = false)
        {
            bool rpgOpponent = (isStoryDuel && _storyMode.IsRpgCampaign) ||
                (isTagForceDuel && TagForceWorldUI.IsRpgPractice(_activeTagForce?.SeriesId));
            AIDifficulty duelDifficulty = rpgOpponent ? AIDifficulty.Master : opponentDifficulty ?? AiDifficulty;
            if (rpgOpponent) planningScale = ComboSearch.MaxPlanningScale;
            bool isCampaignMatch = isStoryDuel || isTournamentDuel || isRankedDuel || isTagForceDuel || isBeyondDestiny;
            if (oppChar == null || oppDeck == null || oppDeck.charId != oppChar.id ||
                (isStoryDuel && _activeStoryEpisode?.charId != oppChar.id) ||
                (isTournamentDuel && _activeTournament?.Opponent != oppChar.id) ||
                (isRankedDuel && (_activeRanked?.Opponent.Deck != oppDeck)) ||
                (isTagForceDuel && _activeTagForce?.Deck != oppDeck) ||
                (isBeyondDestiny && _activeBeyondDestiny?.opponentDeckId != oppDeck.id) ||
                (!isCampaignMatch && !CollectionProgress.IsOpponentDeckUnlocked(oppChar.id, oppDeck.id)))
            {
                ShowDuelStartFailure(isStoryDuel, "목록에 있는 상대와 덱을 선택하세요.", isTournamentDuel, isRankedDuel, isTagForceDuel);
                return;
            }
            if (!isCampaignMatch)
            {
                _activeStoryEpisode = null;
                _activeTournament = null;
                _activeTagForce = null;
                oppDeck = OpponentDeckLevels.GetDecks(oppChar.id).First(deck => deck.id == oppDeck.id);
            }
            var duelDeck = myDeck;
            bool forcedStoryDeck = isBeyondDestiny || isStoryDuel && _storyMode.IsGxRpg && _activeStoryEpisode.forcedPlayerDeckId != 0;
            string reason = "시점 전용 덱의 카드 구성을 확인하세요.";
            bool deckReady = forcedStoryDeck ? myDeck?.IsPlayable == true &&
                    myDeck.main.Concat(myDeck.extra).All(code => CardDatabase.Get(code) != null)
                : CollectionProgress.Initialized
                    ? CollectionProgress.TryPrepareDeckForDuel(myDeck, out duelDeck, out reason)
                    : CanStartWithDeck(myDeck, out reason);
            if (!deckReady)
            {
                ShowDuelStartFailure(isStoryDuel, reason, isTournamentDuel, isRankedDuel, isTagForceDuel);
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
                        oppChar.id, oppDeck.id, duelDifficulty.ToString(), ChainConfirmation.ToString());
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Personal replay] Could not enable local recording ({exception.GetType().Name}).");
                }
            }
            bool planCombos = duelDifficulty is AIDifficulty.Smart or AIDifficulty.Master;
            var raid = isStoryDuel ? _storyMode.GetRaid(_activeStoryEpisode) : null;
            string storyGimmick = isStoryDuel ? _activeStoryEpisode.gimmick : null;
            string ridingMachine = isStoryDuel ? _storyMode.RidingMachineFor(_activeStoryEpisode) : null;
            var opening = isStoryDuel ? _activeStoryPlan.Apply(raid, oppDeck, storyGimmick) : ((DuelOpening Player, DuelOpening Opponent)?)null;
            if (isTournamentDuel)
                opening = (new DuelOpening(8000, 5, 0), DimensionTournament.Opening(_activeTournament.Value.Slots, oppDeck));
            if (isBeyondDestiny)
            {
                opening = BeyondDestinyOpening(_activeBeyondDestiny);
                if (_activeBeyondDestiny.Riding)
                {
                    storyGimmick = BeyondDestinyPlayback.RidingRule(_activeBeyondDestiny);
                    ridingMachine = "D-휠";
                }
            }
            _session = new DuelSession(recordPlanningReplay: planCombos, playerReplay: _personalReplay);
            _ai = new TacticalAI(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded(),
                searchCombos: planCombos, planningScale: planningScale,
                responseLimit: rpgOpponent ? ComboSearch.RpgResponseLimit : ComboSearch.DefaultResponseLimit)
                { Difficulty = duelDifficulty };
            _shownHumanPrompt = null;
            _timedAiPrompt = null;
            _resultShown = false;
            _refreshAfterPresentation = false;

            ShowScreen(AppScreen.Duel);
            _session.PreparePlayerIndices(playerGoesFirst);
            _session.OnCardsShuffled += DuelAudio.PlayShuffle;
            _duelField.BindSession(_session, oppChar, oppDeck);
            if (isStoryDuel && _storyMode.IsGxRpg)
            {
                int character = _activeStoryEpisode.playAsCharacterId;
                _duelField.SetStoryPlayer(character > 0 ? DeckRepository.GetCharacter(character)?.name : _tagForceWorld.RpgHeroName,
                    _tagForceWorld.RpgDuelPortrait(character));
                _duelField.SetStoryOpponent(_activeStoryEpisode.opponentName,
                    _tagForceWorld.RpgDuelPortrait(TagForceWorldUI.StageOpponentId(_activeStoryEpisode)));
            }
            else _duelField.SetStoryPlayer(null, null);
            if (isBeyondDestiny)
            {
                var art = _beyondDestiny.DuelArt(_beyondDestinyBattleMap, _beyondDestinyPlayerKey, _beyondDestinyOpponentKey);
                _duelField.SetStoryPlayer(_beyondDestinyDuelist, art.Player);
                _duelField.SetStoryOpponent(oppChar.name, art.Opponent);
                _duelField.SetRidingPresentation(ridingMachine != null, art.Map);
            }
            _duelField.RefreshChainConfirmation(ChainConfirmation);
            if (!_session.StartNewDuel(
                duelDeck.main, duelDeck.extra,
                oppDeck.main, oppDeck.extra,
                p0GoesFirst: playerGoesFirst, playerDeckAppearance: duelDeck, opponentOpening: opening?.Opponent,
                opponentDestiny: GetOpponentDestinyCards(oppDeck),
                playerOpening: isTagForceDuel ? _activeTagForce.Opening : opening?.Player, storyGimmick: storyGimmick))
            {
                ShowDuelStartFailure(isStoryDuel, "듀얼을 시작하지 못했습니다.", isTournamentDuel, isRankedDuel, isTagForceDuel);
                return;
            }
            if (ridingMachine != null) _session.RecordRidingLaunch(ridingMachine);
            if (isStoryDuel && !CollectionProgress.TryConsumeRaidSupports(_activeStoryPlan.Supports, raid.HasValue, out string error))
            {
                ShowDuelStartFailure(isStoryDuel, error);
                return;
            }
            if (!forcedStoryDeck) CollectionProgress.RememberDuelDeck(myDeck.id);
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

        private void ShowDuelStartFailure(bool isStoryDuel, string message, bool isTournamentDuel = false, bool isRankedDuel = false,
            bool isTagForceDuel = false)
        {
            if (_activeBeyondDestiny != null)
            {
                ShowScreen(AppScreen.BeyondDestiny);
                _beyondDestiny.LaunchFailed(message);
                return;
            }
            if (isTagForceDuel)
            {
                ShowScreen(AppScreen.TagForceWorld);
                _tagForceWorld.ShowStatus(message);
                return;
            }
            if (isRankedDuel)
            {
                _ranked.Close();
                ShowScreen(AppScreen.Ranked);
                _ranked.ShowStatus(message);
                return;
            }
            if (isStoryDuel && _storyMode.FromRpgField)
            {
                ReturnToRpgField(message);
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
            if (_screen == AppScreen.OpponentSelect) _oppSelect?.UpdateTurnOrder(Time.unscaledDeltaTime);

            if (_session == null)
            {
                if ((_screen is AppScreen.StoryMode or AppScreen.TagForceWorld) && _tagForceWorld.RpgInterludeActive)
                {
                    if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                        Input.GetKeyDown(KeyCode.KeypadEnter) || _menuPadInput.Read(Time.unscaledTime).Submit)
                        _tagForceWorld.SkipRpgInterludeTitle();
                    _tagForceWorld.UpdateRpgInterlude(Time.unscaledDeltaTime);
                    return;
                }
                if (_screen == AppScreen.StoryMode) _storyMode?.Update(Time.unscaledDeltaTime);
                if (_screen == AppScreen.BeyondDestiny) _beyondDestiny?.Update(Time.unscaledDeltaTime, _menuPadInput.HeldDirection);
                if (_screen == AppScreen.Tournament) _tournament?.Update(Time.unscaledDeltaTime);
                if (_screen == AppScreen.DeckEditor) _deckEditor?.Update();
                else if (_menuPad != null)
                {
                    if (!_menuPad.HasPopup && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) ||
                        DeckPadInput.DirectTouchBegan()))
                        _menuPad.Clear(preserveSelection: true);
                    HandleMenuInput(_menuPadInput.Read(Time.unscaledTime), Input.GetKeyDown(KeyCode.Escape));
                    if (_screen == AppScreen.TagForceWorld)
                        _tagForceWorld.Update(Time.unscaledDeltaTime, _menuPadInput.HeldDirection);
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

            if (TryFinishBeyondDestinyDuel()) return;
            if (_session.IsFinished)
            {
                if (!_resultShown)
                {
                    _resultShown = true;
                    _modals.HideAll();
                    _duelField.Refresh();
                    bool won = _session.Winner == _session.HumanPlayerIndex;
                    bool draw = _session.Winner == 2;
                    if (_activeTagForce is { } worldDuel)
                    {
                        string result = "듀얼 결과가 기록되지 않았습니다.";
                        int previousRank = CollectionProgress.RpgRank;
                        bool rewarded = _session.Winner >= 0 &&
                            (TagForceWorldUI.IsRpgPractice(worldDuel.SeriesId)
                                ? CollectionProgress.TryAwardRpgPractice(worldDuel.Deck, won, draw, out result,
                                    awardPartnerAffinity: worldDuel.SeriesId == TagForceWorldUI.RpgPracticeId)
                                : TagForceManager.TryFinishDuel(worldDuel, won, draw, out result));
                        if (rewarded && TagForceWorldUI.IsRpgPractice(worldDuel.SeriesId))
                            _tagForceWorld.QueueRpgRankNotice(previousRank);
                        ShowScreen(AppScreen.TagForceWorld);
                        _tagForceWorld.ShowResult(worldDuel, won, result, rewarded);
                        SyncFrontMenuFocus();
                        return;
                    }
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
                        var opponentDeck = _activeOppDeck;
                        bool fromField = _storyMode.FromRpgField;
                        (bool Saved, string Message) Settle()
                        {
                            int previousRank = CollectionProgress.RpgRank;
                            int previousXp = CollectionProgress.RpgExperience;
                            if (!CollectionProgress.TryAwardStoryDuel(_storyMode.ProgressIdFor(episode), won, raid, opponentDeck, plan,
                                out var prize, out string error, rpgField: fromField, awardPartnerAffinity: !_storyMode.IsGxRpg)) return (false, error);
                            if (fromField) _tagForceWorld.QueueRpgRankNotice(previousRank);
                            int gained = CollectionProgress.RpgExperience - previousXp;
                            return (true, FormatStoryPrize(prize) + (gained > 0 ? $" · +{gained} EXP" : ""));
                        }
                        string storyReward = fromField ? null : Settle().Message;
                        ShowScreen(AppScreen.StoryMode);
                        if (fromField) _storyMode.ShowFieldDuelResult(episode, won, draw, Settle, plan);
                        else _storyMode.ShowDuelResult(episode, won, draw, storyReward, plan);
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
                if (AdvanceAiDecision(prompt, Time.deltaTime * DuelFieldUI.PlaybackRate, Time.realtimeSinceStartupAsDouble))
                {
                    _timedAiPrompt = null;
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
                YgoConstants.MSG_SELECT_PLACE or YgoConstants.MSG_SELECT_DISFIELD or YgoConstants.MSG_STORY_CRISIS);
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
                AppScreen.OpponentSelect => _oppSelect.TurnOrderRoot.activeInHierarchy ? _oppSelect.TurnOrderRoot : _oppSelect.Root,
                AppScreen.StarterSelection => _collectionShop?.StarterRoot,
                AppScreen.CardShop => _collectionShop?.ActiveShopRoot,
                AppScreen.ArchetypeCodex => _archetypeCodex.Root,
                AppScreen.StoryMode => _storyMode.ActiveRoot,
                AppScreen.Tournament => _tournament.ActiveRoot,
                AppScreen.Ranked => _ranked.ActiveRoot,
                AppScreen.TagForceWorld => _tagForceWorld.Root,
                AppScreen.Campaigns or AppScreen.BeyondDestiny => _beyondDestiny.ActiveRoot,
                _ => null,
            };
            if (page != null && page.activeInHierarchy)
                _menuPad.SyncSelectedFocus(page.transform);
        }

        private void HandleMenuInput(DeckPadFrame frame, bool escape)
        {
            bool cloudSave = _saveSyncMenu?.Root.activeSelf == true;
            bool settings = _titleAndMenu.SettingsRoot.activeInHierarchy;
            bool choosingTurnOrder = _oppSelect.TurnOrderRoot.activeInHierarchy;
            bool selectingOpponent = _screen == AppScreen.OpponentSelect;
            bool selectingDecks = selectingOpponent && _oppSelect.IsDeckSetupOpen;
            bool selectingStarter = _screen == AppScreen.StarterSelection;
            bool codex = _screen == AppScreen.ArchetypeCodex;
            bool collectionPage = selectingStarter || _screen == AppScreen.CardShop || codex;
            bool story = _screen == AppScreen.StoryMode;
            bool tournament = _screen == AppScreen.Tournament;
            bool ranked = _screen == AppScreen.Ranked;
            bool tagForce = _screen == AppScreen.TagForceWorld;
            bool campaignPicker = _screen is AppScreen.Campaigns or AppScreen.BeyondDestiny;
            bool cancel = frame.Cancel || (escape && (cloudSave || settings || selectingOpponent || _menuPad.HasPopup || collectionPage || story || tournament || ranked || tagForce || campaignPicker));
            if (!frame.HasInput && !cancel) return;
            if (story && !cloudSave && !_menuPad.HasPopup && frame.Submit && _storyMode.RestoreHiddenDialogue()) return;
            if (campaignPicker && !cloudSave && !_menuPad.HasPopup && frame.Submit && _beyondDestiny.RestoreHidden()) return;
            if (campaignPicker && !cloudSave && !_menuPad.HasPopup && !cancel && frame.Submit && _beyondDestiny.HandleFieldSubmit()) return;

            GameObject scope = cloudSave ? _saveSyncMenu.Root : _screen switch
            {
                AppScreen.Title => _titleAndMenu.TitleRoot,
                AppScreen.MainMenu => settings ? _titleAndMenu.SettingsRoot : _titleAndMenu.MenuRoot,
                AppScreen.OpponentSelect => choosingTurnOrder ? _oppSelect.TurnOrderRoot : _oppSelect.Root,
                AppScreen.StarterSelection => _collectionShop?.StarterRoot,
                AppScreen.CardShop => _collectionShop?.ActiveShopRoot,
                AppScreen.ArchetypeCodex => _archetypeCodex.Root,
                AppScreen.StoryMode => _storyMode.ActiveRoot,
                AppScreen.Tournament => _tournament.ActiveRoot,
                AppScreen.Ranked => _ranked.ActiveRoot,
                AppScreen.TagForceWorld => _tagForceWorld.RpgFieldMenuScope,
                AppScreen.Campaigns or AppScreen.BeyondDestiny => _beyondDestiny.ActiveRoot,
                _ => null,
            };
            if (scope == null || !scope.activeInHierarchy) return;
            if (tagForce && !cloudSave && !_menuPad.HasPopup && !cancel && _tagForceWorld.HandleFieldInput(frame)) return;
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
                if (selectingOpponent && !choosingTurnOrder && !selectingDecks && !cancel)
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
                else if (choosingTurnOrder) _oppSelect.CloseTurnOrder();
                else if (selectingDecks) _oppSelect.CloseDeckSetup();
                else if (selectingStarter) ShowScreen(AppScreen.Title);
                else if (_screen == AppScreen.CardShop)
                {
                    if (!_collectionShop.HandleShopBack()) CloseCollectionShop();
                }
                else if (story) _storyMode.Back();
                else if (tournament) _tournament.Back();
                else if (ranked) _ranked.Back();
                else if (tagForce) _tagForceWorld.Back();
                else if (campaignPicker) _beyondDestiny.Back();
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

        internal static AIDifficulty ReadAiDifficulty() =>
            PlayerPrefs.GetInt(AiDifficultyPreference, (int)AIDifficulty.Master) == (int)AIDifficulty.Normal
                ? AIDifficulty.Normal : AIDifficulty.Master;

        internal void ToggleAiDifficulty()
        {
            AiDifficulty = AiDifficulty == AIDifficulty.Master ? AIDifficulty.Normal : AIDifficulty.Master;
            PlayerPrefs.SetInt(AiDifficultyPreference, (int)AiDifficulty);
            PlayerPrefs.Save();
        }

        private bool AdvanceAiDecision(DuelPrompt prompt, float playbackDelta, double now)
        {
            if (!ReferenceEquals(_timedAiPrompt, prompt))
            {
                _timedAiPrompt = prompt;
                _aiPromptStartedAt = now;
                _aiTimer = 0f;
            }

            _aiTimer += playbackDelta;
            bool masterMainPhase = _ai.Difficulty == AIDifficulty.Master &&
                prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD;
            return _ai.AdvanceComboPlanning(_session, prompt) && _aiTimer >= AiStepDelay &&
                (!masterMainPhase || now - _aiPromptStartedAt >= ComboSearch.MasterPlanningMilliseconds / 1000d);
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
            _tagForceWorld?.Close();
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
