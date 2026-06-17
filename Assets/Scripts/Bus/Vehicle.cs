using System;
using System.Collections.Generic;
using DG.Tweening;
using Game;
using UnityEngine;

namespace TJ.Scripts
{
    public class Vehicle : MonoBehaviour, IBoardOccupant
    {
        // ─── Serialized / Public Fields ──────────────────────────────────
        public ColorEnum vehicleColor;
        public List<Transform> seats;
        [SerializeField] private List<MeshRenderer> vehMesh;
        public List<GameObject> removableParts;
        public Transform pickUPPoint;

        // ─── Movement State ─────────────────────────────────────────────
        public Tween movingZdir;
        private float distance = 30f;
        private bool isCollided = false;
        private bool _isNavigating = false;
        public bool isFull = false;
        public Vector3 originalPosition;
        public Vector3 ogScale;
        public Vector3 newScale;
        public int playersInSeat = 0;
        private bool canCollideWitOtherVehicle = true;
        private readonly HashSet<Transform> _reservedSeats = new HashSet<Transform>();
        public static bool isMovingStraight = false;
        public static ColorEnum LastTouchedCarcolor;
        public bool isMovingForward = false;
        public int SeatCount => seats.Count;

        // ─── Board / Conveyor Attachment ────────────────────────────────
        private Transform _parentTransform;
        private Sequence _jumpSequence;
        private bool _isExiting = false;
        private bool _isBooked = false;
        private bool _isDrivingFromStorage = false;



        // ─── Events ────────────────────────────────────────────────────
        /// <summary>Fired when JumpToBoard animation completes.</summary>
        public event Action<Vehicle, ConveyorFollowerBoard> OnJumpToBoardCompleted;

        /// <summary>Fired when the conveyor board this vehicle was riding completes its path.</summary>
        public event Action<Vehicle> OnCompletedPath;

        /// <summary>Fired when a stored vehicle is tapped so GameplayController can send it back to a board.</summary>
        public event Action<Vehicle> OnJumpRequest;

        // ─── Properties ────────────────────────────────────────────────
        /// <summary>True while the vehicle is sitting in a grid visualizer storage slot.</summary>
        public bool IsInStorage { get; private set; }

        /// <summary>True when the vehicle still has unoccupied seats.</summary>
        public bool HasEmptySeats => !isFull;

        /// <summary>
        /// Set to true by GameplayController once the jump-to-board animation
        /// completes and the vehicle is riding the conveyor.
        /// </summary>
        public bool IsReadyForPassengerSearch { get; set; }

        // ─── Constants ─────────────────────────────────────────────────
        private const float TurnDuration = 0.3f;
        private const float WallFollowSpeed = 12f;
        private const float ConveyorBoardJumpDuration = 1.0f;
        private const float StorageMoveDuration = 1.0f;

        // ─── Vehicle-to-vehicle collision guard ─────────────────────────
        private static int _hitSoundCounter = 0;

        // ════════════════════════════════════════════════════════════════
        // Lifecycle
        // ════════════════════════════════════════════════════════════════

        void Start()
        {
            transform.position = new Vector3(transform.position.x, 1f, transform.position.z);
            _parentTransform = transform.parent;
            SetInitialPosition();
            ogScale = transform.localScale;
            Vector3 currentRotation = transform.rotation.eulerAngles;
            transform.rotation = Quaternion.Euler(0, currentRotation.y, 0);
        }

        private void OnValidate()
        {
            transform.position = new Vector3(transform.position.x, 1f, transform.position.z);
            Vector3 currentRotation = transform.rotation.eulerAngles;
            transform.rotation = Quaternion.Euler(0, currentRotation.y, 0);
        }

        private void OnDestroy()
        {
            if (_isBooked)
            {
                var gc = Game.GameManager.Instance?.GameplayController;
                if (gc != null) gc.UnbookBoard();
                _isBooked = false;
            }

            OnJumpToBoardCompleted = null;
            OnCompletedPath = null;
            OnJumpRequest = null;
            _jumpSequence?.Kill();
            DOTween.Kill(transform);
        }

        public void SetInitialPosition()
        {
            originalPosition = transform.position;
        }

        // ════════════════════════════════════════════════════════════════
        // Input
        // ════════════════════════════════════════════════════════════════

        private void OnMouseDown()
        {
            if (_isExiting) return;
            if (_isNavigating) return;

            if (Game.GameManager.Instance != null && Game.GameManager.Instance.IsShopOpen) return;

            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            if (!_isBooked && Game.GameManager.Instance != null && Game.GameManager.Instance.GameplayController != null)
            {
                var gc = Game.GameManager.Instance.GameplayController;
                if (gc.AvailableBoardCount - gc.BookedBoardCount <= 0)
                {
                    return;
                }
                gc.BookBoard();
                _isBooked = true;
            }

            // When sitting in a storage slot, tap sends the vehicle back to the conveyor.
            if (IsInStorage)
            {
                OnJumpRequest?.Invoke(this);
                return;
            }

            if (Helper.instance)
                Helper.instance.MoveHand();

            LastTouchedCarcolor = vehicleColor;

            if (CheckForVehicleInFront(out RaycastHit hitInfo))
            {
                isMovingStraight = true;
                isMovingForward = true;
                Vibration.Vibrate(40);

                Vector3 targetPosition =
                    transform.position +
                    transform.forward * (hitInfo.distance + 1);
                movingZdir = transform.DOMove(targetPosition, 0.2f).SetEase(Ease.InQuad).SetUpdate(UpdateType.Fixed);

                return;
            }

            MoveCarStraight();
        }



        // ════════════════════════════════════════════════════════════════
        // Player / Seat Management
        // ════════════════════════════════════════════════════════════════

        public void FillUpVehicle()
        {
            var players = PlayerManager.instance.playersInScene;
            for (int i = 0; i < players.Count; i++)
            {
                if (playersInSeat >= SeatCount)
                    break;

                if (players[i].color == vehicleColor)
                {
                    players[i].MoveToTruck(this, false);
                    i--;
                }
            }
            SoundController.Instance.PlayOneShot(SoundController.Instance.sort);
        }

        public void ChangeScale(bool shift)
        {
            newScale = Vector3.one;
            if (shift)
            {
                transform.localScale = newScale;
            }
            else
            {
                transform.localScale = ogScale;
            }
        }

        public Transform GetFreeSeat()
        {
            for (int i = seats.Count - 1; i >= 0; i--)
            {
                Transform seat = seats[i];
                if (seat.childCount == 0 && !_reservedSeats.Contains(seat))
                {
                    _reservedSeats.Add(seat);
                    playersInSeat++;
                    IsVehicleFull();
                    return seat;
                }
            }

            return null;
        }

        public void ReleaseSeatReservation(Transform seat)
        {
            if (seat == null)
                return;

            _reservedSeats.Remove(seat);
        }

        public void IsVehicleFull()
        {
            if (playersInSeat == seats.Count)
            {
                isFull = true;
            }
        }

        /// <summary>
        /// Called when all seats are filled and the vehicle has finished its conveyor run.
        /// Detaches from the board, drives off-screen, then self-destructs.
        /// </summary>
        public void VehicleGoing()
        {
            _isExiting = true;
            ResetParent();

            VehicleController vc = VehicleController.instance;
            if (vc != null && vc.transitCube != null)
            {
                Transform transit = vc.transitCube;
                Vector3 toTransit = transit.position - transform.position;
                toTransit.y = 0;

                Sequence exitSequence = DOTween.Sequence();

                if (toTransit.sqrMagnitude > 0.01f)
                {
                    Quaternion faceTransitRotation = Quaternion.LookRotation(toTransit);
                    exitSequence.Append(transform.DORotateQuaternion(faceTransitRotation, 0.15f));
                }

                exitSequence.Append(transform.DOMove(transit.position, 0.3f).SetEase(Ease.Linear));

                // Once at the transit, turn to face left (exit direction) and drive off-screen
                Quaternion exitRotation = Quaternion.LookRotation(Vector3.left);
                exitSequence.Append(transform.DORotateQuaternion(exitRotation, 0.15f));

                Vector3 exitTarget = transit.position + Vector3.left * 15f;
                exitSequence.Append(transform.DOMove(exitTarget, 0.6f).SetEase(Ease.InQuad));

                exitSequence.OnComplete(() => Destroy(gameObject));
                exitSequence.SetLink(gameObject);
            }
            else
            {
                // Fallback: drive forward then off-screen
                Vector3 exitTarget = transform.position + transform.forward * 25f;
                transform.DOMove(exitTarget, 0.8f).SetEase(Ease.InQuad)
                    .OnComplete(() => Destroy(gameObject))
                    .SetLink(gameObject);
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Color
        // ════════════════════════════════════════════════════════════════

        public void ChangeColor(ColorEnum colorEnum)
        {
            this.vehicleColor = colorEnum;
            Material mats = VehicleController.instance.VehiclesMaterialHolder.FindMaterialByName(colorEnum);
            if (mats != null)
            {
                for (int i = 0; i < vehMesh.Count; i++)
                {
                    vehMesh[i].material = mats;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Forward Driving
        // ════════════════════════════════════════════════════════════════

        public void MoveCarStraight()
        {
            Vibration.Vibrate(40);
            isMovingStraight = true;
            isMovingForward = true;
            Vector3 localPosition = transform.localPosition;
            Vector3 localForwardDirection = transform.localRotation * Vector3.forward;

            Vector3 pointAtDistance = localPosition + localForwardDirection * distance;

            Vector3 worldPoint = transform.parent.TransformPoint(pointAtDistance);

            Debug.DrawLine(transform.position, worldPoint, Color.green);
            movingZdir = transform.DOMove(worldPoint, 12f).SetSpeedBased().SetUpdate(UpdateType.Fixed);
            GetComponent<AudioSource>().enabled = true;
        }

        public bool CheckForObstacles()
        {
            float offset = 1.0f;
            float rayDistance = Mathf.Infinity;

            Vector3 leftRayDirection = transform.TransformDirection(Vector3.forward + Vector3.left * offset);
            Vector3 rightRayDirection = transform.TransformDirection(Vector3.forward + Vector3.right * offset);

            if (Physics.Raycast(transform.position, leftRayDirection, out RaycastHit hitInfoLeft, rayDistance) &&
                Physics.Raycast(transform.position, rightRayDirection, out RaycastHit hitInfoRight, rayDistance))
            {
                if (hitInfoLeft.collider != null && hitInfoLeft.collider.TryGetComponent(out Vehicle vehicleLeft) &&
                    vehicleLeft.canCollideWitOtherVehicle && !vehicleLeft.isMovingForward)
                {
                    return true;
                }

                if (hitInfoRight.collider != null && hitInfoRight.collider.TryGetComponent(out Vehicle vehicleRight) &&
                    vehicleRight.canCollideWitOtherVehicle && !vehicleRight.isMovingForward)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CheckForVehicleInFront(out RaycastHit hitInfo)
        {
            Vector3 forward = transform.TransformDirection(Vector3.forward);
            float rayDistance = Mathf.Infinity;

            if (Physics.Raycast(transform.position, forward, out hitInfo, rayDistance))
            {
                if (hitInfo.collider.TryGetComponent(out Vehicle vehicle) && vehicle.canCollideWitOtherVehicle &&
                    !vehicle.isMovingForward)
                {
                    return true;
                }
            }

            return false;
        }

        public void ShakeVehicle()
        {
            transform.DOShakeRotation(0.2f, transform.forward * 2, vibrato: 10, randomness: 90).SetEase(Ease.InBounce);
        }

        // ════════════════════════════════════════════════════════════════
        // Wall-Based Pathway Navigation
        // ════════════════════════════════════════════════════════════════
        //
        // Flow:  Tap → MoveCarStraight → hits wall → follows wall → hits corner
        //        → follows next wall → … → hits Transit → requests board.
        //
        // Tags used on cubes:
        //   "WallUp"    – top boundary wall
        //   "WallDown"  – bottom boundary wall
        //   "WallLeft"  – left boundary wall
        //   "WallRight" – right boundary wall
        //   "Transit"   – transit destination cube
        //
        // All wall cubes must have Box Colliders set as Triggers.
        // ════════════════════════════════════════════════════════════════

        private void OnTriggerEnter(Collider other)
        {
            // Skip trigger-based navigation while the scripted storage-to-conveyor path is running
            if (_isDrivingFromStorage) return;

            string tag = other.tag;

            // ── Transit cube reached ────────────────────────────────────
            if (tag == "Transit" && _isNavigating)
            {
                HandleTransitReached();
                return;
            }

            // ── Wall cube hit (first hit or corner turn) ────────────────
            if (tag == "WallUp" || tag == "WallDown" || tag == "WallLeft" || tag == "WallRight")
            {
                // First hit: vehicle must be actively driving forward
                // Corner hit: vehicle must already be navigating walls
                if ((!isCollided && isMovingForward) || _isNavigating)
                {
                    HandleWallHit(other);
                }
                return;
            }

            // ── Vehicle-to-vehicle collision ────────────────────────────
            if (canCollideWitOtherVehicle && !_isNavigating &&
                other.TryGetComponent(out Vehicle vehicle) &&
                vehicle.canCollideWitOtherVehicle)
            {
                BounceBackToStart(other, vehicle);
            }
        }

        private void BounceBackToStart(Collider obstacleCollider = null, Vehicle otherVehicle = null)
        {
            movingZdir?.Pause();

            if (!isCollided)
            {
                if (isMovingStraight && _hitSoundCounter == 0 && isMovingForward)
                {
                    _hitSoundCounter++;

                    GetComponent<AudioSource>().enabled = false;
                    SoundController.Instance.PlayOneShot(SoundController.Instance.hitSound);
                    
                    if (obstacleCollider != null)
                    {
                        EffectsManager.instance.PlayEffect(EffectsManager.instance.hitEffect,
                            obstacleCollider.ClosestPoint(transform.position + new Vector3(0, 0.25f, 0)),
                            Quaternion.identity);
                    }
                }

                if (otherVehicle != null)
                    otherVehicle.ShakeVehicle();

                transform.DOMove(originalPosition, 0.3f).SetEase(Ease.OutBack)
                    .OnComplete(() =>
                    {
                        _hitSoundCounter = 0;
                        isMovingStraight = false;
                        isMovingForward = false;

                        if (_isBooked)
                        {
                            var gc = Game.GameManager.Instance?.GameplayController;
                            if (gc != null) gc.UnbookBoard();
                            _isBooked = false;
                        }
                    });
            }
        }

        /// <summary>
        /// Handles the vehicle hitting a wall cube. On first hit, transitions from
        /// "driving forward" to "navigating walls". On corner hits, turns and
        /// continues along the new wall toward the transit cube.
        /// </summary>
        private void HandleWallHit(Collider wallCollider)
        {
            // Stop current movement
            movingZdir?.Kill();

            if (!_isNavigating)
            {
                // First wall hit — enter navigation mode
                _isNavigating = true;
                isCollided = true;
                isMovingStraight = false;
                isMovingForward = false;
                canCollideWitOtherVehicle = false;

                AudioSource audio = GetComponent<AudioSource>();
                if (audio != null) audio.enabled = false;

                VehicleController.instance.RemoveVehicle(this);
            }

            VehicleController vc = VehicleController.instance;
            Vector3 transitPos = vc.transitCube.position;
            Vector3 currentPos = transform.position;

            string tag = wallCollider.tag;

            // Determine direction along this wall toward the transit cube.
            // Left/Right walls run vertically (Z axis).
            // Up/Down walls run horizontally (X axis).
            Vector3 moveDirection;

            if (tag == "WallLeft" || tag == "WallRight")
            {
                float deltaZ = transitPos.z - currentPos.z;
                if (Mathf.Abs(deltaZ) < 0.5f)
                {
                    // Already aligned on Z — head directly to transit
                    DriveTowardTransit();
                    return;
                }
                moveDirection = new Vector3(0f, 0f, Mathf.Sign(deltaZ));
            }
            else // WallUp or WallDown
            {
                float deltaX = transitPos.x - currentPos.x;
                if (Mathf.Abs(deltaX) < 0.5f)
                {
                    // Already aligned on X — head directly to transit
                    DriveTowardTransit();
                    return;
                }
                moveDirection = new Vector3(Mathf.Sign(deltaX), 0f, 0f);
            }

            // Smooth turn then drive along the wall.
            // The large overshoot (100 units) guarantees we hit another wall or transit trigger.
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            Vector3 targetPos = currentPos + moveDirection * 100f;
            targetPos.y = currentPos.y;

            Sequence seq = DOTween.Sequence();
            seq.Append(transform.DORotateQuaternion(targetRotation, TurnDuration).SetEase(Ease.InOutSine));
            seq.Append(transform.DOMove(targetPos, WallFollowSpeed).SetSpeedBased().SetEase(Ease.Linear));
            seq.SetUpdate(UpdateType.Fixed);
            seq.SetLink(gameObject);

            movingZdir = seq;
        }

        /// <summary>
        /// When the vehicle is already on the correct axis, drive directly toward
        /// the transit cube position. The Transit trigger will fire on arrival.
        /// </summary>
        private void DriveTowardTransit()
        {
            VehicleController vc = VehicleController.instance;
            Vector3 transitPos = vc.transitCube.position;
            transitPos.y = transform.position.y;

            Vector3 direction = transitPos - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.01f)
            {
                // Already at transit
                HandleTransitReached();
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction);

            Sequence seq = DOTween.Sequence();
            seq.Append(transform.DORotateQuaternion(targetRotation, TurnDuration).SetEase(Ease.InOutSine));
            seq.Append(transform.DOMove(transitPos, WallFollowSpeed).SetSpeedBased().SetEase(Ease.Linear));
            seq.SetUpdate(UpdateType.Fixed);
            seq.SetLink(gameObject);

            movingZdir = seq;
        }

        /// <summary>
        /// Called when the vehicle enters the Transit cube trigger.
        /// Stops wall navigation, deactivates the roof, and immediately requests a conveyor board.
        /// </summary>
        private void HandleTransitReached()
        {
            movingZdir?.Kill();
            _isNavigating = false;
            isMovingForward = false;
            isMovingStraight = false;

            DeactivateRoof();

            if (Game.GameManager.Instance != null && Game.GameManager.Instance.GameplayController != null)
            {
                if (_isBooked)
                {
                    Game.GameManager.Instance.GameplayController.UnbookBoard();
                    _isBooked = false;
                }
                Game.GameManager.Instance.GameplayController.RequestBoardForVehicle(this);
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Transit → Conveyor Board
        // ════════════════════════════════════════════════════════════════

        public void DeactivateRoof()
        {
            foreach (var parts in removableParts)
            {
                parts.SetActive(false);
            }
        }



        // ════════════════════════════════════════════════════════════════
        // Conveyor Board / Storage
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Parents this vehicle to the given ConveyorFollowerBoard and animates it into place.
        /// Fires OnJumpToBoardCompleted on completion.
        /// </summary>
        public void JumpToBoard(ConveyorFollowerBoard board)
        {
            movingZdir?.Kill();
            _jumpSequence?.Kill();
            DOTween.Kill(transform);

            transform.SetParent(board.transform);

            float duration = Mathf.Max(GameConfigs.Instance.shooterJumpToConveyorDuration, ConveyorBoardJumpDuration);
            float power = GameConfigs.Instance.shooterJumpToConveyorPower;

            Vector3 boardLocalTarget = new Vector3(0f, 1f, 1f);
            _jumpSequence = DOTween.Sequence();
            _jumpSequence.Insert(0f, transform.DOLocalJump(boardLocalTarget, power, 1, duration));
            _jumpSequence.Insert(0f, transform.DOLocalRotate(Vector3.zero, duration));
            _jumpSequence.OnComplete(() =>
            {
                _jumpSequence = null;
                OnJumpToBoardCompleted?.Invoke(this, board);
            });
            _jumpSequence.SetLink(gameObject);

            board.SetAssignedOccupant(this);
            board.OnBoardCompletedPath += Board_OnBoardCompletedPath;
        }

        private void Board_OnBoardCompletedPath(ConveyorFollowerBoard board)
        {
            board.OnBoardCompletedPath -= Board_OnBoardCompletedPath;
            OnCompletedPath?.Invoke(this);
        }

        /// <summary>Sets whether this vehicle is currently occupying a storage slot.</summary>
        public void SetInStorage(bool inStorage)
        {
            IsInStorage = inStorage;
        }

        /// <summary>
        /// Parents this vehicle to the given storage slot and snaps it into place.
        /// </summary>
        public void JumpToStorage(StoragePiece storage)
        {
            movingZdir?.Kill();
            _jumpSequence?.Kill();
            DOTween.Kill(transform);

            // Parent the vehicle to the storage piece so it's formally stored,
            // but we will animate its world position to follow the cubes.
            transform.SetParent(storage.transform);

            Vector3 startPos = transform.position;
            Vector3 finalPos = storage.transform.TransformPoint(GridAndStorageVisualizer.StoredVehicleOffset);
            
            VehicleController vc = VehicleController.instance;
            
            // Fallback to old behavior if cubes aren't assigned
            if (vc == null || vc.leftCube == null || vc.upCube == null)
            {
                _jumpSequence = DOTween.Sequence();
                _jumpSequence.Append(transform.DOLocalMove(GridAndStorageVisualizer.StoredVehicleOffset, StorageMoveDuration).SetEase(Ease.InOutSine));
                _jumpSequence.Join(transform.DOLocalRotate(Vector3.zero, StorageMoveDuration).SetEase(Ease.InOutSine));
                _jumpSequence.OnComplete(() => _jumpSequence = null);
                _jumpSequence.SetLink(gameObject);
                return;
            }

            float leftX = vc.leftCube.position.x;
            float upZ = vc.upCube.position.z;

            // Define the waypoints
            Vector3 ptB = new Vector3(leftX, startPos.y, startPos.z);
            Vector3 ptC = new Vector3(leftX, startPos.y, upZ);
            Vector3 ptD = new Vector3(finalPos.x, startPos.y, upZ);
            Vector3 ptE = finalPos;

            _jumpSequence = DOTween.Sequence();
            Vector3 currentPos = startPos;
            Vector3[] points = new Vector3[] { ptB, ptC, ptD, ptE };

            float speed = 18f; // Move a bit faster when going to storage
            float turnDuration = 0.15f;

            for (int i = 0; i < points.Length; i++)
            {
                Vector3 pt = points[i];
                Vector3 dir = pt - currentPos;
                
                // For rotation, ignore Y difference so it doesn't tilt up/down while driving
                Vector3 rotDir = dir;
                rotDir.y = 0;
                
                float dist = dir.magnitude;
                
                if (dist > 0.1f)
                {
                    if (rotDir.sqrMagnitude > 0.01f)
                    {
                        Quaternion rot = Quaternion.LookRotation(rotDir);
                        _jumpSequence.Append(transform.DORotateQuaternion(rot, turnDuration).SetEase(Ease.InOutSine));
                    }
                    
                    _jumpSequence.Append(transform.DOMove(pt, dist / speed).SetEase(Ease.Linear));
                    currentPos = pt;
                }
            }

            // At the end, snap rotation to be perfectly aligned with the storage
            _jumpSequence.Append(transform.DOLocalRotate(Vector3.zero, 0.15f).SetEase(Ease.InOutSine));

            _jumpSequence.OnComplete(() => _jumpSequence = null);
            _jumpSequence.SetUpdate(UpdateType.Fixed);
            _jumpSequence.SetLink(gameObject);
        }

        /// <summary>
        /// Drives the vehicle from storage back to the conveyor board.
        /// Path: storage position → drive backward to WallUp (upCube Z) → follow walls to transitCube → request board.
        /// </summary>
        public void DriveFromStorageToConveyor()
        {
            movingZdir?.Kill();
            _jumpSequence?.Kill();
            DOTween.Kill(transform);

            IsInStorage = false;

            // Detach from storage parent so we can move freely in world space
            ResetParent();

            VehicleController vc = VehicleController.instance;

            // Fallback: if cubes aren't assigned, go directly to transit
            if (vc == null || vc.upCube == null || vc.leftCube == null || vc.transitCube == null)
            {
                HandleTransitReached();
                return;
            }

            Vector3 startPos = transform.position;
            float upZ = vc.upCube.position.z;
            float leftX = vc.leftCube.position.x;
            Vector3 transitPos = vc.transitCube.position;

            // Waypoints: reverse to WallUp → follow WallUp to WallLeft corner → follow WallLeft down to transit
            Vector3 ptA = new Vector3(startPos.x, startPos.y, upZ);       // straight back to WallUp
            Vector3 ptB = new Vector3(leftX, startPos.y, upZ);            // along WallUp to WallLeft corner
            Vector3 ptC = new Vector3(leftX, startPos.y, transitPos.z);   // down WallLeft to transit Z
            Vector3 ptD = new Vector3(transitPos.x, startPos.y, transitPos.z); // to transit cube

            _isDrivingFromStorage = true;
            _isNavigating = true;
            isCollided = true;
            isMovingStraight = false;
            isMovingForward = false;
            canCollideWitOtherVehicle = false;

            float speed = 18f;
            float turnDur = 0.15f;

            _jumpSequence = DOTween.Sequence();

            // Phase 1: Drive BACKWARD to WallUp (no rotation — just reverse)
            float distToWall = Vector3.Distance(startPos, ptA);
            if (distToWall > 0.1f)
            {
                _jumpSequence.Append(transform.DOMove(ptA, distToWall / speed).SetEase(Ease.Linear));
            }

            // Phase 2: Turn and follow the remaining waypoints (ptB → ptC → ptD) toward transit
            Vector3 currentPos = ptA;
            Vector3[] remainingPoints = new Vector3[] { ptB, ptC, ptD };

            for (int i = 0; i < remainingPoints.Length; i++)
            {
                Vector3 pt = remainingPoints[i];
                Vector3 dir = pt - currentPos;
                Vector3 rotDir = dir;
                rotDir.y = 0;
                float dist = dir.magnitude;

                if (dist > 0.1f)
                {
                    if (rotDir.sqrMagnitude > 0.01f)
                    {
                        Quaternion rot = Quaternion.LookRotation(rotDir);
                        _jumpSequence.Append(transform.DORotateQuaternion(rot, turnDur).SetEase(Ease.InOutSine));
                    }
                    _jumpSequence.Append(transform.DOMove(pt, dist / speed).SetEase(Ease.Linear));
                    currentPos = pt;
                }
            }

            _jumpSequence.OnComplete(() =>
            {
                _jumpSequence = null;
                _isDrivingFromStorage = false;
                _isNavigating = false;
                HandleTransitReached();
            });
            _jumpSequence.SetUpdate(UpdateType.Fixed);
            _jumpSequence.SetLink(gameObject);
        }

        /// <summary>Detach from the board and restore the original parent (IBoardOccupant).</summary>
        public void ResetParent()
        {
            transform.SetParent(_parentTransform);
        }
    }
}
