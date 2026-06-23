using Sirenix.OdinInspector;
using TJ.Scripts;
using UnityEngine;
using UnityEngine.SceneManagement;
using Utilities.EventBus;

namespace Game
{
    public class GameManager : Singleton<GameManager>
    {
        [Title("References")]
        [SerializeField] private GameConfigs _gameConfigs;
        [SerializeField] private GameplayController _gameplayController;
        [SerializeField] private RemoteConfigManager _remoteConfigManager;
        [SerializeField] private GameObject _gameWonPanel;
        [SerializeField] private GameObject _gameLostPanel;
        [SerializeField] private GameObject _shopPanel;
        [SerializeField] private GameObject _pausePanel;
        [SerializeField] private GameObject _pauseButton;

        public bool IsInitialized { get; private set; }
        public GameplayController GameplayController => _gameplayController;
        public bool IsShopOpen => _shopPanel != null && _shopPanel.activeSelf;
        public bool IsPaused => _pausePanel != null && _pausePanel.activeSelf;

        private EventBinding<GameplayStateChangedEvent> _gameplayStateChangedEventBinding;
        private bool _gameHasEnded;

        private void Start()
        {
            Application.targetFrameRate = 60;
            Initialize();
        }

        private void Initialize()
        {
            _gameplayStateChangedEventBinding = new EventBinding<GameplayStateChangedEvent>(OnGameplayStateChanged);
            EventBus<GameplayStateChangedEvent>.Subscribe(_gameplayStateChangedEventBinding);

            if (_gameConfigs == null)
            {
#if UNITY_EDITOR
                string[] guids = UnityEditor.AssetDatabase.FindAssets("t:GameConfigs");
                if (guids.Length > 0)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    _gameConfigs = UnityEditor.AssetDatabase.LoadAssetAtPath<GameConfigs>(path);
                }
#endif
            }

            if (_gameConfigs != null)
            {
                _gameConfigs.Initialize();
            }
            else
            {
                Debug.LogError("[GameManager] GameConfigs is missing! Please assign it in the Inspector.");
            }
            // All Awake() phases have completed before Start() runs, so the Dreamteck
            // SplineComputer has valid point data — Bounds and storage creation are safe here.
            _gameplayController.Initialize();
            _gameplayController.Prepare();

            _gameHasEnded = false;
            HideAllPanels();

            IsInitialized = true;

            _remoteConfigManager.Initialize(OnRemoteConfigReady);
        }

        private void OnRemoteConfigReady()
        {
            // Remote config values are now applied to GameConfigs.
            // The game is already initialized and prepared — just confirm we're in Gameplay state.
            EnsurePanelReferences();
            ChangeGameplayState(GameplayState.Gameplay);
        }

        private void EnsurePanelReferences()
        {
            if (_gameWonPanel == null)
                _gameWonPanel = GameObject.Find("Game won Panel");

            if (_gameLostPanel == null)
                _gameLostPanel = GameObject.Find("Game Lost Panel");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            EventBus<GameplayStateChangedEvent>.Unsubscribe(_gameplayStateChangedEventBinding);
        }

        private void OnGameplayStateChanged(GameplayStateChangedEvent e)
        {
            // Only re-prepare when transitioning FROM a terminal state (Win/Fail → restart).
            // Startup flow is handled by OnRemoteConfigReady to avoid a double-Prepare.
            if (e.NewState == GameplayState.Gameplay &&
                     (e.OldState == GameplayState.Win || e.OldState == GameplayState.Fail))
            {
                PrepareForLevel();
            }
        }

        public void ChangeGameplayState(GameplayState newState)
        {
            _gameplayController.ChangeGameplayState(newState);
        }

        public void PrepareForLevel()
        {
            _gameplayController.Prepare();
        }

        public void TryLevelAgain()
        {
            _gameHasEnded = false;
            ResumeGame();
            HideAllPanels();

            // Ensure all animations and lingering DOTween references are completely destroyed
            DG.Tweening.DOTween.KillAll();

            // Reload the actual current scene instead of a hardcoded string
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public void GoToMainMenu()
        {
            _gameHasEnded = false;
            ResumeGame();
            HideAllPanels();
            SceneManager.LoadScene("Main Menu");
        }

        /// <summary>
        /// Pauses the game by setting Time.timeScale to 0.
        /// Can be called from a Pause button via UnityEvent.
        /// </summary>
        public void PauseGame()
        {
            Time.timeScale = 0f;
        }

        /// <summary>
        /// Resumes the game by restoring Time.timeScale to 1.
        /// Can be called from a Resume button via UnityEvent.
        /// </summary>
        public void ResumeGame()
        {
            Time.timeScale = 1f;
        }

        private void Update()
        {
            if (!_gameHasEnded && IsInitialized)
                CheckForWinCondition();
        }

        private void CheckForWinCondition()
        {
            if (PlayerManager.instance == null || VehicleController.instance == null)
                return;

            bool noVehiclesLeft = VehicleController.instance.vehicles == null ||
                                  VehicleController.instance.vehicles.Length == 0;

            bool noPlayersLeft = PlayerManager.instance.playersInScene.Count == 0 &&
                                 PlayerManager.instance.totalPlayerList.Count == 0 &&
                                 PlayerManager.instance.activePlayerList.Count == 0;

            bool allBoardsBack = _gameplayController.AllBoardsAvailable;

            if (noVehiclesLeft && noPlayersLeft && allBoardsBack)
            {
                OnGameWon();
            }
        }

        public void OnStorageOverflow()
        {
            if (_gameHasEnded)
                return;

            _gameHasEnded = true;
            ShowPanelObject(_gameLostPanel);
            ChangeGameplayState(GameplayState.Fail);
        }

        private void OnGameWon()
        {
            if (_gameHasEnded)
                return;

            _gameHasEnded = true;
            ShowPanelObject(_gameWonPanel);
            ChangeGameplayState(GameplayState.Win);
        }

        private void ShowPanelObject(GameObject panel)
        {
            if (_gameWonPanel != null)
                _gameWonPanel.SetActive(panel == _gameWonPanel);

            if (_gameLostPanel != null)
                _gameLostPanel.SetActive(panel == _gameLostPanel);

            PauseGame();
        }

        public void OpenShopPanel()
        {
            if (_shopPanel != null)
            {
                _shopPanel.SetActive(true);
                if (_pauseButton != null) _pauseButton.SetActive(false);
                PauseGame();
            }
        }

        public void CloseShopPanel()
        {
            if (_shopPanel != null)
            {
                _shopPanel.SetActive(false);
                if (_pauseButton != null) _pauseButton.SetActive(true);
                ResumeGame();
            }
        }

        public void OpenPausePanel()
        {
            if (_pausePanel != null)
            {
                _pausePanel.SetActive(true);
                PauseGame();
            }
        }

        public void ClosePausePanel()
        {
            if (_pausePanel != null)
            {
                _pausePanel.SetActive(false);
                ResumeGame();
            }
        }

        private void HideAllPanels()
        {
            if (_gameWonPanel != null)
                _gameWonPanel.SetActive(false);

            if (_gameLostPanel != null)
                _gameLostPanel.SetActive(false);

            if (_shopPanel != null)
                _shopPanel.SetActive(false);

            if (_pausePanel != null)
                _pausePanel.SetActive(false);
        }
    }
}
