using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Game;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TJ.Scripts
{
    public class PlayerManager : MonoBehaviour
    {
        public static PlayerManager instance;

        [Header("References")]
        public GameObject PlayerPrefab;
        public Transform spawnPoint;
        public Transform pickPoint;

        [Header("Passenger Grid")]
        /// <summary>
        /// World-space centre of the passenger grid.
        /// Assign an empty GameObject positioned at the scene centre (where the pixel targets were).
        /// Falls back to the midpoint between spawnPoint and pickPoint when null.
        /// </summary>
        public Transform gridCenter;
        [SerializeField] private int _gridColumns = 4;
        [SerializeField] private int _gridRows = 4;
        [SerializeField] private float _cellSize = 1.2f;

        [Header("Shuffle")]
        public bool canShuffle = true;
        [Range(0, 1)] public float shuffleIntensity = 0.5f;

        // Public lists kept for external access (Player.cs references playersInScene)
        public List<Player> playersInScene = new();
        public List<Player> totalPlayerList = new();
        public List<Player> activePlayerList = new();

        // Legacy serialized fields kept to avoid breaking existing prefab data
        public Vector3 midPoint;
        public List<Vector3> pointsBetweenMidAndPick;
        public List<Vector3> pointsBetweenMidAndSpawn;
        public List<Vector3> allPoints;

        public bool isColormatched;

        private Player[][] _playerGrid;
        private Vector3 _gridCenterPos;
        private Coroutine _rout;

        /// <summary>Tracks passengers already dispatched to a vehicle so they are never double-boarded.</summary>
        private readonly HashSet<Player> _boardingPassengers = new();

        private void Awake()
        {
            instance = this;
            _gridCenterPos = ResolveGridCenter();
        }

        private void OnEnable()
        {
            EventManager.OnNewVehArrived += AnyCarColorMatched;
        }

        private void OnDisable()
        {
            EventManager.OnNewVehArrived -= AnyCarColorMatched;
        }

        // ─── Spawning ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Entry point called by VehicleController. Derives one passenger per vehicle seat,
        /// optionally shuffles, then spawns them in a centre grid — mirroring the
        /// TargetObjectController grid layout used by the PixelFlow target area.
        /// </summary>
        public void InstantiatePlayers(Vehicle[] vehicles)
        {
            List<ColorEnum> colors = new();
            foreach (Vehicle v in vehicles)
                for (int i = 0; i < v.SeatCount; i++)
                    colors.Add(v.vehicleColor);

            if (canShuffle)
                colors = ShuffleColorsWithIntensity(colors, shuffleIntensity);

            SpawnPlayersInGrid(colors);
        }

        /// <summary>
        /// Positions passengers in an N×M grid centred on <see cref="gridCenter"/>.
        /// Columns advance along +X, rows advance along −Z — matching the
        /// TargetObjectController / GridHelper layout convention.
        /// Passengers beyond grid capacity are kept hidden in <see cref="totalPlayerList"/>.
        /// </summary>
        private void SpawnPlayersInGrid(List<ColorEnum> colors)
        {
            ClearAllPlayers();

            _gridCenterPos = ResolveGridCenter();
            _playerGrid = new Player[_gridColumns][];
            for (int col = 0; col < _gridColumns; col++)
                _playerGrid[col] = new Player[_gridRows];

            int gridCapacity = _gridColumns * _gridRows;
            float halfW = (_gridColumns - 1) * _cellSize * 0.5f;
            float halfH = (_gridRows - 1) * _cellSize * 0.5f;
            Transform parent = gridCenter != null ? gridCenter : transform;

            int spawnCount = Mathf.Min(colors.Count, gridCapacity);
            for (int i = 0; i < spawnCount; i++)
            {
                int col = i % _gridColumns;
                int row = i / _gridColumns;

                float x = _gridCenterPos.x + col * _cellSize - halfW;
                float z = _gridCenterPos.z + halfH - row * _cellSize;
                Vector3 pos = new Vector3(x, _gridCenterPos.y, z);

                Player player = CreatePlayer(pos, parent, colors[i]);
                playersInScene.Add(player);
                activePlayerList.Add(player);
                _playerGrid[col][row] = player;
            }

            // Overflow passengers wait hidden and enter the grid as slots open up
            for (int i = gridCapacity; i < colors.Count; i++)
            {
                Player player = CreatePlayer(_gridCenterPos, transform, colors[i]);
                player.gameObject.SetActive(false);
                totalPlayerList.Add(player);
            }
        }

        private Player CreatePlayer(Vector3 position, Transform parent, ColorEnum color)
        {
            GameObject obj = Instantiate(PlayerPrefab, position, Quaternion.identity, parent);
            Player plyr = obj.GetComponent<Player>();
            plyr.ChangeColor(color);
            return plyr;
        }

        private void ClearAllPlayers()
        {
            foreach (Player p in playersInScene)
                if (p != null) Destroy(p.gameObject);
            foreach (Player p in totalPlayerList)
                if (p != null) Destroy(p.gameObject);

            playersInScene.Clear();
            totalPlayerList.Clear();
            activePlayerList.Clear();
            _boardingPassengers.Clear();
        }

        // ─── Conveyor-driven passenger search (mirrors TryFindTargetForShooter) ──

        /// <summary>
        /// Called every frame by <see cref="Game.GameplayController"/> for each vehicle that is
        /// actively riding the conveyor belt and has empty seats.
        ///
        /// Logic mirrors <see cref="Game.TargetObjectController.TryFindTargetForShooter"/>:
        ///   1. Determine which side of the passenger grid the vehicle is on.
        ///   2. Project the vehicle's XZ position onto a grid column (Bottom/Top) or row (Left/Right).
        ///   3. Scan inward from the closest edge to find the first available, colour-matched passenger.
        ///   4. Use sub-frame step interpolation so fast-moving vehicles never skip a column.
        /// </summary>
        /// <param name="vehicle">Vehicle currently on the conveyor.</param>
        /// <param name="lastCheckPos">World position from the previous frame's check — updated on return.</param>
        /// <param name="passenger">The found passenger, or null when none matches.</param>
        /// <param name="checkedPos">The vehicle position recorded this frame (store and pass back next frame).</param>
        public bool TryFindPassengerForVehicle(Vehicle vehicle,
                                                Vector3? lastCheckPos,
                                                out Player passenger,
                                                out Vector3 checkedPos)
        {
            passenger = null;
            checkedPos = vehicle.transform.position;

            if (_playerGrid == null || vehicle == null || vehicle.isFull) return false;
            if (activePlayerList.Count == 0) return false;

            // ── Grid bounds ──────────────────────────────────────────────────────
            float halfW = (_gridColumns - 1) * _cellSize * 0.5f;
            float halfH = (_gridRows    - 1) * _cellSize * 0.5f;
            float minX = _gridCenterPos.x - halfW;
            float maxX = _gridCenterPos.x + halfW;
            float minZ = _gridCenterPos.z - halfH;
            float maxZ = _gridCenterPos.z + halfH;

            Vector3 vehiclePos = checkedPos;

            // Pad by one cell so the vehicle is detected while still approaching a column edge
            float pad       = _cellSize;
            bool isBetweenX = vehiclePos.x >= minX - pad && vehiclePos.x <= maxX + pad;
            bool isBetweenZ = vehiclePos.z >= minZ - pad && vehiclePos.z <= maxZ + pad;

            Side side;
            if      (vehiclePos.z < minZ && isBetweenX) side = Side.Bottom;
            else if (vehiclePos.z > maxZ && isBetweenX) side = Side.Top;
            else if (vehiclePos.x > maxX && isBetweenZ) side = Side.Right;
            else if (vehiclePos.x < minX && isBetweenZ) side = Side.Left;
            else                                          return false;   // inside or at corner

            // ── Sub-frame step count (anti-miss for fast vehicles) ────────────────
            int stepCount = 1;
            if (lastCheckPos.HasValue)
            {
                float dist = Vector3.Distance(lastCheckPos.Value, vehiclePos);
                if (dist > _cellSize)
                    stepCount = Mathf.CeilToInt(dist / _cellSize);
            }

            Vector3 lastPos = lastCheckPos ?? vehiclePos;

            // ── Scan interpolated positions ───────────────────────────────────────
            for (int step = 1; step <= stepCount; step++)
            {
                float   t       = (float)step / stepCount;
                Vector3 scanPos = Vector3.Lerp(lastPos, vehiclePos, t);

                if (TryFindPassengerAtScanPos(scanPos, side, vehicle.vehicleColor,
                                              minX, halfW, halfH, out passenger))
                {
                    _boardingPassengers.Add(passenger);   // reserve — prevents double-boarding
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Projects <paramref name="scanPos"/> onto the passenger grid and scans the resulting
        /// column (Bottom/Top) or row (Left/Right) inward from the edge closest to the vehicle,
        /// returning the first colour-matched available passenger.
        /// </summary>
        private bool TryFindPassengerAtScanPos(Vector3 scanPos, Side side, ColorEnum vehicleColor,
                                               float minX, float halfW, float halfH,
                                               out Player passenger)
        {
            passenger = null;
            int col, row, dx, dy, steps;

            if (side == Side.Bottom || side == Side.Top)
            {
                // Project X → column
                col = Mathf.RoundToInt((scanPos.x - minX) / _cellSize);
                col = Mathf.Clamp(col, 0, _gridColumns - 1);

                if (side == Side.Bottom)
                {
                    row = _gridRows - 1;  // bottom row: closest to a vehicle below the grid
                    dy  = -1;             // scan toward row 0 (top)
                }
                else
                {
                    row = 0;              // top row: closest to a vehicle above the grid
                    dy  = 1;             // scan toward row _gridRows-1 (bottom)
                }
                dx    = 0;
                steps = _gridRows;
            }
            else // Right or Left
            {
                // Project Z → row  (row 0 = highest Z)
                row = Mathf.RoundToInt((_gridCenterPos.z + halfH - scanPos.z) / _cellSize);
                row = Mathf.Clamp(row, 0, _gridRows - 1);

                if (side == Side.Right)
                {
                    col = _gridColumns - 1; // rightmost col: closest to a vehicle to the right
                    dx  = -1;               // scan toward col 0 (left)
                }
                else
                {
                    col = 0;                // leftmost col: closest to a vehicle to the left
                    dx  = 1;               // scan toward col _gridColumns-1 (right)
                }
                dy    = 0;
                steps = _gridColumns;
            }

            for (int i = 0; i < steps; i++, col += dx, row += dy)
            {
                if (col < 0 || col >= _gridColumns) break;
                if (_playerGrid[col] == null)        continue;
                if (row < 0 || row >= _gridRows)     break;

                Player cell = _playerGrid[col][row];

                if (cell == null) continue; // empty slot — transparent, keep scanning

                // Occupied slot: accept only an unboarded, colour-matched passenger.
                if (!_boardingPassengers.Contains(cell) && cell.color == vehicleColor)
                {
                    passenger = cell;
                    return true;
                }

                // Wrong colour or already boarding — blocks the line of sight.
                return false;
            }

            return false;
        }

        /// <summary>
        /// Returns the passenger at grid[<paramref name="col"/>][<paramref name="row"/>]
        /// if it exists, has not started boarding, and matches <paramref name="vehicleColor"/>.
        /// </summary>
        private bool TryGetAvailablePassengerAt(int col, int row, ColorEnum vehicleColor, out Player passenger)
        {
            passenger = null;

            if (col < 0 || col >= _gridColumns) return false;
            if (_playerGrid[col] == null)        return false;
            if (row < 0 || row >= _gridRows)     return false;

            Player candidate = _playerGrid[col][row];
            if (candidate == null)                              return false;
            if (_boardingPassengers.Contains(candidate))        return false;
            if (candidate.color != vehicleColor)                return false;

            passenger = candidate;
            return true;
        }

        // ─── Event-based fallback (ParkingManager path, kept for compatibility) ──

        /// <summary>
        /// Fallback boarding triggered by <see cref="EventManager.OnNewVehArrived"/>.
        /// Only active when <see cref="ParkingManager.Instance"/> is present in the scene.
        /// </summary>
        public void AnyCarColorMatched()
        {
            if (ParkingManager.Instance == null) return;

            var cars = ParkingManager.Instance.parkedVehicles;
            if (cars.Count <= 0)
            {
                isColormatched = false;
                return;
            }

            foreach (var car in cars)
            {
                if (car.isFull) continue;
                Player match = activePlayerList.FirstOrDefault(p => p != null && p.color == car.vehicleColor);
                if (match != null)
                {
                    isColormatched = true;
                    match.MoveToTruck(car, true);
                    return;
                }
            }

            isColormatched = false;
            if (_rout != null) StopCoroutine(_rout);
        }

        // ─── Post-boarding cleanup ────────────────────────────────────────────────

        /// <summary>
        /// Called by <see cref="Player.MoveToTruck"/> after a passenger finishes boarding.
        /// Removes the slot from the grid and slides in the next overflow passenger if available.
        /// </summary>
        public IEnumerator RepositionPlayers(Player player)
        {
            _boardingPassengers.Remove(player);
            RemoveFromAllLists(player);
            RemoveFromGrid(player);

            if (totalPlayerList.Count > 0 && TryFindEmptyGridSlot(out int col, out int row))
            {
                Player next = totalPlayerList[0];
                totalPlayerList.RemoveAt(0);

                float halfW = (_gridColumns - 1) * _cellSize * 0.5f;
                float halfH = (_gridRows - 1) * _cellSize * 0.5f;
                float x = _gridCenterPos.x + col * _cellSize - halfW;
                float z = _gridCenterPos.z + halfH - row * _cellSize;
                Vector3 targetPos = new Vector3(x, _gridCenterPos.y, z);

                next.gameObject.SetActive(true);
                next.anim.SetBool(Player.Walk, true);
                next.transform.DOMove(targetPos, 0.3f)
                    .OnComplete(() => next.anim.SetBool(Player.Walk, false));

                playersInScene.Add(next);
                activePlayerList.Add(next);
                _playerGrid[col][row] = next;
            }

            yield return null;
        }

        // ─── Grid helpers ─────────────────────────────────────────────────────────

        private void RemoveFromAllLists(Player player)
        {
            playersInScene.Remove(player);
            activePlayerList.Remove(player);
            totalPlayerList.Remove(player);
        }

        private void RemoveFromGrid(Player player)
        {
            if (_playerGrid == null) return;
            for (int col = 0; col < _gridColumns; col++)
            {
                if (_playerGrid[col] == null) continue;
                for (int row = 0; row < _gridRows; row++)
                {
                    if (_playerGrid[col][row] == player)
                    {
                        _playerGrid[col][row] = null;
                        return;
                    }
                }
            }
        }

        private bool TryFindEmptyGridSlot(out int col, out int row)
        {
            col = 0;
            row = 0;
            if (_playerGrid == null) return false;
            for (int c = 0; c < _gridColumns; c++)
            {
                if (_playerGrid[c] == null) continue;
                for (int r = 0; r < _gridRows; r++)
                {
                    if (_playerGrid[c][r] == null) { col = c; row = r; return true; }
                }
            }
            return false;
        }

        private Vector3 ResolveGridCenter()
        {
            if (gridCenter != null) return gridCenter.position;
            if (spawnPoint != null && pickPoint != null)
                return new Vector3(spawnPoint.position.x, pickPoint.position.y, pickPoint.position.z);
            return transform.position;
        }

        private List<ColorEnum> ShuffleColorsWithIntensity(List<ColorEnum> colors, float intensity)
        {
            var list = new List<ColorEnum>(colors);
            int n = list.Count;
            for (int i = 0; i < n - 1; i++)
            {
                int j = Mathf.Min(i + Mathf.FloorToInt(Random.Range(0f, 1f) * intensity * (n - i)), n - 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        // ─── Legacy stubs (kept for external compatibility) ───────────────────────

        public void GeneratePoints() { }

        public IEnumerator PlayerMovement() { yield return null; }

        public void ShufflePlayerList() { }

        public void UpdatePlayerPos(int posCount) { }
    }
}
