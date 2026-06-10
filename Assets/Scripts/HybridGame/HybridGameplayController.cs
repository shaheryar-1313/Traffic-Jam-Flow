using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using TMPro;

namespace HybridGame
{
    /// <summary>
    /// Central coordinator for the Bus-Conveyor hybrid game.
    /// Wires together the passenger grid, conveyor system, and vehicle queue.
    /// All system references are resolved at Start via FindObjectOfType,
    /// so no manual Inspector wiring is required for cross-system references.
    /// </summary>
    public class HybridGameplayController : MonoBehaviour
    {
        public static HybridGameplayController Instance { get; private set; }

        // ── Optional Inspector references (auto-resolved if left null) ──────────
        [Header("Systems (auto-discovered if null)")]
        [SerializeField] private HybridPassengerGrid _passengerGrid;
        [SerializeField] private HybridConveyorSystem _conveyorSystem;

        [Header("Level Data")]
        [Tooltip("16 color values for the 4×4 passenger grid. Uses a built-in default if left empty.")]
        [SerializeField] private ColorEnum[] _passengerColors;

        [Header("UI (optional)")]
        [SerializeField] private GameObject _winPanel;
        [SerializeField] private TMP_Text _winText;

        // ── Color palette indexed by (int)ColorEnum ─────────────────────────────
        private static readonly Color[] ColorPalette =
        {
            new Color(0.90f, 0.20f, 0.20f),  // Red
            new Color(0.20f, 0.75f, 0.25f),  // Green
            new Color(0.20f, 0.40f, 0.90f),  // Blue
            new Color(0.95f, 0.88f, 0.15f),  // Yellow
            new Color(0.95f, 0.50f, 0.10f),  // Orange
            new Color(0.90f, 0.35f, 0.70f),  // Pink
            new Color(0.45f, 0.22f, 0.08f),  // Brown
            Color.white,                      // None
        };

        // ── Default level layout (4×4, 4 colours × 4 passengers) ────────────────
        private static readonly ColorEnum[] DefaultPassengerColors =
        {
            ColorEnum.Red,    ColorEnum.Red,    ColorEnum.Blue,   ColorEnum.Blue,
            ColorEnum.Red,    ColorEnum.Red,    ColorEnum.Blue,   ColorEnum.Blue,
            ColorEnum.Yellow, ColorEnum.Yellow, ColorEnum.Green,  ColorEnum.Green,
            ColorEnum.Yellow, ColorEnum.Yellow, ColorEnum.Green,  ColorEnum.Green,
        };

        // ── Boarding throttle (seconds between successive boardings per board) ──
        private const float BoardingInterval = 0.25f;
        private readonly Dictionary<HybridConveyorBoard, float> _lastBoardingTime = new();

        private bool _isGameOver;

        // ────────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            ResolveReferences();
            SetupGame();
        }

        private void ResolveReferences()
        {
            if (_passengerGrid == null)
                _passengerGrid = FindFirstObjectByType<HybridPassengerGrid>();

            if (_conveyorSystem == null)
                _conveyorSystem = FindFirstObjectByType<HybridConveyorSystem>();

            if (_passengerGrid == null)
                Debug.LogError("[HybridGame] HybridPassengerGrid not found in scene.");

            if (_conveyorSystem == null)
                Debug.LogError("[HybridGame] HybridConveyorSystem not found in scene.");
        }

        private void SetupGame()
        {
            // Subscribe to all vehicles in the scene
            HybridVehicle[] vehicles = FindObjectsByType<HybridVehicle>(FindObjectsSortMode.None);
            foreach (HybridVehicle vehicle in vehicles)
            {
                vehicle.OnTapped += OnVehicleTapped;
                vehicle.ApplyColor(GetColor(vehicle.vehicleColor));
            }

            // Subscribe to conveyor exit events to clean up boarding timers
            if (_conveyorSystem != null)
                _conveyorSystem.OnBoardExited += b => _lastBoardingTime.Remove(b);

            // Populate the passenger grid
            ColorEnum[] colors = (_passengerColors != null && _passengerColors.Length > 0)
                ? _passengerColors
                : DefaultPassengerColors;

            if (_passengerGrid != null)
            {
                _passengerGrid.Populate(colors, ColorPalette);
                _passengerGrid.OnAllPassengersBoarded += HandleWin;
            }

            if (_winPanel != null) _winPanel.SetActive(false);
        }

        private void OnVehicleTapped(HybridVehicle vehicle)
        {
            if (_isGameOver || _conveyorSystem == null) return;

            if (!_conveyorSystem.CanAcceptVehicle)
            {
                Debug.Log("[HybridGame] Conveyor is at capacity. Wait for a slot to free up.");
                return;
            }

            HybridConveyorBoard board = _conveyorSystem.SendVehicle(vehicle);
            if (board != null)
                _lastBoardingTime[board] = Time.time - BoardingInterval; // allow immediate first boarding
        }

        private void Update()
        {
            if (_isGameOver || _conveyorSystem == null) return;
            ProcessPassengerBoarding();
        }

        /// <summary>
        /// Each frame, tries to board one colour-matching passenger per active conveyor board.
        /// The BoardingInterval stagger produces a satisfying sequential loading animation.
        /// </summary>
        private void ProcessPassengerBoarding()
        {
            IReadOnlyList<HybridConveyorBoard> boards = _conveyorSystem.ActiveBoards;

            for (int i = 0; i < boards.Count; i++)
            {
                HybridConveyorBoard board = boards[i];

                if (!board.IsOnConveyor) continue;
                if (board.AssignedVehicle == null || board.AssignedVehicle.IsFull) continue;

                // Throttle: one boarding event per board per interval
                if (_lastBoardingTime.TryGetValue(board, out float lastTime) &&
                    Time.time - lastTime < BoardingInterval)
                    continue;

                HybridVehicle vehicle = board.AssignedVehicle;
                HybridPassenger passenger = _passengerGrid != null
                    ? _passengerGrid.FindUnboardedByColor(vehicle.vehicleColor)
                    : null;

                if (passenger == null) continue;

                Transform seat = vehicle.GetFreeSeat();
                if (seat == null) continue;

                _lastBoardingTime[board] = Time.time;

                passenger.BoardVehicle(seat, () =>
                {
                    _passengerGrid?.NotifyPassengerBoarded();
                });
            }
        }

        private void HandleWin()
        {
            if (_isGameOver) return;
            _isGameOver = true;

            Debug.Log("[HybridGame] WIN — All passengers boarded!");

            if (_winPanel != null)
            {
                _winPanel.SetActive(true);
                _winPanel.transform.localScale = Vector3.zero;
                _winPanel.transform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack);
            }

            if (_winText != null)
                _winText.text = "All Aboard!";
        }

        /// <summary>Returns the Color for a given ColorEnum using the built-in palette.</summary>
        public static Color GetColor(ColorEnum color)
        {
            int idx = (int)color;
            return idx >= 0 && idx < ColorPalette.Length ? ColorPalette[idx] : Color.white;
        }
    }
}
