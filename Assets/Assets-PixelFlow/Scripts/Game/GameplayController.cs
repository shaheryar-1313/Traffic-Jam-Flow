using System.Collections.Generic;
using Sirenix.OdinInspector;
using TJ.Scripts;
using UnityEngine;
using Utilities.EventBus;

namespace Game
{
    public class GameplayController : MonoBehaviour
    {
        [Title("References")]
        [SerializeField] private MainConveyor _mainConveyor;
        [SerializeField] private LevelManager _levelManager;
        [SerializeField] private GridAndStorageVisualizer _gridAndStorageVisualizer;
        [SerializeField] private ShooterStorageController _shooterStorageController;

        public bool IsInitialized { get; private set; }
        public bool IsPrepared { get; private set; }

        public int AvailableBoardCount => _mainConveyor.GetAvailableBoardCount();

        private GameplayState _gameplayState;
        private readonly List<Vehicle> _currentlyMovingVehicles = new List<Vehicle>();

        public void Awake()
        {
            // Only clean up event subscriptions here.
            // Initialize() and Prepare() are driven by GameManager.Start()
            // to guarantee all Awake() phases (including Dreamteck SplineComputer) have run.
            CleanupMovingVehicles();
        }

        /// <summary>
        /// Full reset: cleans up active vehicles then re-initializes and re-prepares.
        /// Called externally (e.g. from a restart flow). Safe to call at any time after
        /// the first Initialize() has run.
        /// </summary>
        public void resetGameplay()
        {
            CleanupMovingVehicles();
            Initialize();
            Prepare();
        }

        /// <summary>Unsubscribes all in-flight vehicle events and empties the tracking list.</summary>
        private void CleanupMovingVehicles()
        {
            foreach (Vehicle vehicle in _currentlyMovingVehicles)
            {
                vehicle.OnCompletedPath -= Vehicle_OnCompletedPath;
                vehicle.OnJumpToBoardCompleted -= Vehicle_OnJumpToBoardCompleted;
            }
            _currentlyMovingVehicles.Clear();
        }

        public void Initialize()
        {
            IsInitialized = false;
            IsPrepared = false;

            _levelManager.Initialize();
            _mainConveyor.Initialize();
            Debug.Assert(_mainConveyor.Bounds.HasValue, "Main Conveyor Doesn't Have Bounds");

            _gridAndStorageVisualizer.Initialize();
            _shooterStorageController.Initialize();

            ChangeGameplayState(GameplayState.Gameplay);

            IsInitialized = true;
        }

        public void Prepare()
        {
            IsPrepared = false;

            _levelManager.Prepare();
            _mainConveyor.Prepare(_levelManager.CurrentLevelData.conveyorBoardCount);

            // Build the spatial grid used to position storage slots.
            // Order matters: LevelManager must be prepared first to supply CurrentLevelData.
            if (_mainConveyor.Bounds.HasValue)
            {
                GameGrid vehicleGrid = GridHelper.CreateShooterGrid(
                    LevelManager.Instance.CurrentLevelData,
                    _mainConveyor.Bounds.Value.min.z);

                _gridAndStorageVisualizer.Prepare(vehicleGrid);
                _shooterStorageController.Prepare(_gridAndStorageVisualizer.StorageVisualPieces);
            }

            IsPrepared = true;
        }

        public void ChangeGameplayState(GameplayState newState)
        {
            GameplayState oldState = _gameplayState;
            _gameplayState = newState;

            EventBus<GameplayStateChangedEvent>.Fire(new GameplayStateChangedEvent()
            {
                OldState = oldState,
                NewState = _gameplayState
            });

            Debug.Log("Gameplay state changed to " + newState);
        }

        // -------------------------------------------------------------------------
        // Vehicle → Board flow
        // -------------------------------------------------------------------------

        /// <summary>
        /// Requests an available conveyor board for a Vehicle and initiates the jump.
        /// Gets a board, sends it onto the conveyor, calls Vehicle.JumpToBoard,
        /// then starts the board moving once the jump animation lands.
        /// </summary>
        public void RequestBoardForVehicle(Vehicle vehicle)
        {
            if (!_mainConveyor.TryGetAvailableBoard(out ConveyorFollowerBoard board))
                return;

            _mainConveyor.BoardToConveyor(board);
            _currentlyMovingVehicles.Add(vehicle);

            vehicle.SetInStorage(false);
            vehicle.OnCompletedPath += Vehicle_OnCompletedPath;
            vehicle.OnJumpToBoardCompleted += Vehicle_OnJumpToBoardCompleted;
            vehicle.JumpToBoard(board);
        }

        private void Vehicle_OnJumpToBoardCompleted(Vehicle vehicle, ConveyorFollowerBoard board)
        {
            vehicle.OnJumpToBoardCompleted -= Vehicle_OnJumpToBoardCompleted;
            board.StartMove();
        }

        // -------------------------------------------------------------------------
        // Vehicle → Storage flow (vehicle completes a conveyor run with empty seats)
        // -------------------------------------------------------------------------

        private void Vehicle_OnCompletedPath(Vehicle vehicle)
        {
            vehicle.OnCompletedPath -= Vehicle_OnCompletedPath;
            _currentlyMovingVehicles.Remove(vehicle);

            if (!vehicle.HasEmptySeats)
                return;

            // Vehicle still has empty seats: send it to the grid visualizer storage box,
            // just as a shooter with remaining bullets would be stored after its run.
            if (!_shooterStorageController.TryConsumeVehicle(vehicle))
            {
                Debug.Log("FAIL — Vehicle storage overflow");
                ChangeGameplayState(GameplayState.Fail);
                return;
            }

            vehicle.SetInStorage(true);
            vehicle.OnJumpRequest += Vehicle_OnJumpRequestFromStorage;
        }

        // -------------------------------------------------------------------------
        // Storage → Board flow (player taps a stored vehicle)
        // -------------------------------------------------------------------------

        private void Vehicle_OnJumpRequestFromStorage(Vehicle vehicle)
        {
            vehicle.OnJumpRequest -= Vehicle_OnJumpRequestFromStorage;

            _shooterStorageController.ReleaseVehicle(vehicle);
            _shooterStorageController.ArrangeStorageVehicles();

            RequestBoardForVehicle(vehicle);
        }

        public void CHEAT_FinishGameplay(bool isSuccess)
        {
            ChangeGameplayState(isSuccess ? GameplayState.Win : GameplayState.Fail);
        }
    }
}
