using System.Collections.Generic;
using Sirenix.OdinInspector;
using TJ.Scripts;
using UnityEngine;
using Utilities.EventBus;

// Side enum lives in Game namespace (GlobalEnums.cs)

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

        /// <summary>
        /// Stores the world position of each conveyor vehicle from the previous frame's
        /// <see cref="TryFindPassengerForVehicle"/> call.  Used by the sub-frame
        /// step-count anti-miss logic — mirrors <see cref="ShooterTargetData.LastCheckPosition"/>.
        /// </summary>
        private readonly Dictionary<Vehicle, Vector3> _vehicleLastCheckPositions = new();

        /// <summary>
        /// Tracks which grid columns (Bottom/Top sides) or rows (Left/Right sides) have already
        /// been checked for each vehicle during the current conveyor-side traversal.
        /// Reset when the vehicle transitions to a new side of the grid.
        /// </summary>
        private readonly Dictionary<Vehicle, HashSet<int>> _vehicleCheckedIndices = new();

        /// <summary>
        /// Stores the last conveyor side each vehicle was detected on, so we know when to
        /// reset <see cref="_vehicleCheckedIndices"/>.
        /// </summary>
        private readonly Dictionary<Vehicle, Side> _vehicleLastSide = new();


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

        /// <summary>
        /// Unsubscribes all in-flight vehicle events and empties the tracking list.
        /// </summary>
        private void CleanupMovingVehicles()
        {
            foreach (Vehicle vehicle in _currentlyMovingVehicles)
            {
                vehicle.OnCompletedPath -= Vehicle_OnCompletedPath;
                vehicle.OnJumpToBoardCompleted -= Vehicle_OnJumpToBoardCompleted;
                vehicle.IsReadyForPassengerSearch = false;
            }
            _currentlyMovingVehicles.Clear();
            _vehicleLastCheckPositions.Clear();
            _vehicleCheckedIndices.Clear();
            _vehicleLastSide.Clear();
        }

        // -------------------------------------------------------------------------
        // Per-frame passenger → vehicle matching (mirrors TryFindTargetForShooter)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Each frame, for every vehicle that has landed on the conveyor and still
        /// has empty seats, delegates to <see cref="PlayerManager.TryFindPassengerForVehicle"/>
        /// to find the closest colour-matched passenger in the aligned grid column or row.
        /// When a passenger is found she is dispatched immediately via
        /// <see cref="Player.MoveToTruck"/>.
        /// </summary>
        private void Update()
        {
            if (PlayerManager.instance == null) return;

            foreach (Vehicle vehicle in _currentlyMovingVehicles)
            {
                if (vehicle == null) continue;
                if (!vehicle.IsReadyForPassengerSearch) continue;
                if (vehicle.isFull) continue;

                Vector3? lastPos = _vehicleLastCheckPositions.TryGetValue(vehicle, out Vector3 stored)
                    ? stored
                    : (Vector3?)null;

                // Get or create the per-vehicle checked-indices set
                if (!_vehicleCheckedIndices.TryGetValue(vehicle, out HashSet<int> checkedSet))
                {
                    checkedSet = new HashSet<int>();
                    _vehicleCheckedIndices[vehicle] = checkedSet;
                }
                _vehicleLastSide.TryGetValue(vehicle, out Side prevSide);

                if (PlayerManager.instance.TryFindPassengerForVehicle(
                        vehicle, lastPos, checkedSet,
                        out Player passenger, out Vector3 checkedPos, out Side currentSide, out int checkedIndex))
                {
                    passenger.MoveToTruck(vehicle, true);
                }

                // If the vehicle moved to a new side, reset checked indices
                if (!_vehicleLastSide.ContainsKey(vehicle) || prevSide != currentSide)
                {
                    checkedSet.Clear();
                }
                // Record the index that was checked this frame
                if (checkedIndex >= 0)
                    checkedSet.Add(checkedIndex);

                _vehicleLastCheckPositions[vehicle] = checkedPos;
                _vehicleLastSide[vehicle] = currentSide;
            }
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
            vehicle.IsReadyForPassengerSearch = true;
            board.StartMove();
        }

        // -------------------------------------------------------------------------
        // Vehicle → Storage flow (vehicle completes a conveyor run with empty seats)
        // -------------------------------------------------------------------------

        private void Vehicle_OnCompletedPath(Vehicle vehicle)
        {
            vehicle.OnCompletedPath -= Vehicle_OnCompletedPath;
            vehicle.IsReadyForPassengerSearch = false;
            _currentlyMovingVehicles.Remove(vehicle);
            _vehicleLastCheckPositions.Remove(vehicle);
            _vehicleCheckedIndices.Remove(vehicle);
            _vehicleLastSide.Remove(vehicle);

            if (!vehicle.HasEmptySeats)
            {
                vehicle.VehicleGoing();
                return;
            }

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
