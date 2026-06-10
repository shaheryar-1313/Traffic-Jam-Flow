using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace HybridGame
{
    /// <summary>
    /// Manages the conveyor belt. Accepts dispatched vehicles, places them on boards,
    /// and moves the boards along a waypoint path.
    /// Waypoints are auto-discovered from children named "Waypoint_0", "Waypoint_1", etc.
    /// </summary>
    public class HybridConveyorSystem : MonoBehaviour
    {
        public static HybridConveyorSystem Instance { get; private set; }

        [SerializeField] private float _conveyorSpeed = 3.5f;
        [SerializeField] private int _maxSimultaneousBoards = 3;
        [SerializeField] private float _entryAnimDuration = 0.45f;

        private Transform[] _waypoints;
        private Vector3[] _waypointPositions;
        private readonly List<HybridConveyorBoard> _activeBoards = new();

        public IReadOnlyList<HybridConveyorBoard> ActiveBoards => _activeBoards;
        public bool CanAcceptVehicle => _activeBoards.Count < _maxSimultaneousBoards;

        /// <summary>Fired when a board (and its vehicle) exits the far end of the conveyor.</summary>
        public event Action<HybridConveyorBoard> OnBoardExited;

        private void Awake()
        {
            Instance = this;
            DiscoverWaypoints();
        }

        private void DiscoverWaypoints()
        {
            var found = new List<Transform>();
            for (int i = 0; ; i++)
            {
                Transform wp = transform.Find($"Waypoint_{i}");
                if (wp == null) break;
                found.Add(wp);
            }

            _waypoints = found.ToArray();
            CacheWorldPositions();
        }

        private void CacheWorldPositions()
        {
            _waypointPositions = new Vector3[_waypoints.Length];
            for (int i = 0; i < _waypoints.Length; i++)
                _waypointPositions[i] = _waypoints[i].position;
        }

        /// <summary>
        /// Dispatches a vehicle onto the conveyor.
        /// The board (carrying the vehicle) animates from the vehicle's current world position
        /// to the conveyor entry point, then follows the full waypoint path.
        /// Returns null if the conveyor is at capacity or waypoints are missing.
        /// </summary>
        public HybridConveyorBoard SendVehicle(HybridVehicle vehicle)
        {
            if (!CanAcceptVehicle || _waypointPositions.Length < 2)
                return null;

            // Refresh world positions in case the scene was modified after Awake
            CacheWorldPositions();

            HybridConveyorBoard board = CreateBoard(vehicle.transform.position);
            board.AssignVehicle(vehicle);
            _activeBoards.Add(board);
            board.OnExitedConveyor += HandleBoardExited;

            Vector3 entryPoint = _waypointPositions[0];

            // Phase 1: animate board (+ vehicle) from queue position to conveyor entry
            board.transform
                .DOMove(entryPoint, _entryAnimDuration)
                .SetEase(Ease.OutQuad)
                .OnComplete(() =>
                {
                    // Phase 2: ride the conveyor path
                    board.StartConveyorPath(_waypointPositions, _conveyorSpeed);
                });

            return board;
        }

        private HybridConveyorBoard CreateBoard(Vector3 worldPosition)
        {
            // Board is a thin, slightly transparent platform — purely visual carrier
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ConveyorBoard";
            go.transform.SetParent(transform);
            go.transform.position = worldPosition;
            go.transform.localScale = new Vector3(2.2f, 0.08f, 1.2f);

            // Disable physics on the board — collision is not needed
            Collider col = go.GetComponent<Collider>();
            if (col != null) col.enabled = false;

            // Subtle gray color for the platform
            Renderer rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.55f, 1f));
                rend.SetPropertyBlock(mpb);
            }

            HybridConveyorBoard board = go.AddComponent<HybridConveyorBoard>();
            return board;
        }

        private void HandleBoardExited(HybridConveyorBoard board)
        {
            _activeBoards.Remove(board);
            board.OnExitedConveyor -= HandleBoardExited;
            OnBoardExited?.Invoke(board);
            Destroy(board.gameObject, 0.3f);
        }
    }
}
