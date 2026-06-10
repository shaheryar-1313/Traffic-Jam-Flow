using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Game;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TJ.Scripts
{
    public class Vehicle : MonoBehaviour, IBoardOccupant
    {
        public ColorEnum vehicleColor;
        public List<Transform> seats;
        [SerializeField] private List<MeshRenderer> vehMesh;
        public Tween movingZdir;
        private float distance = 30f;
        private bool isCollided = false;
        public bool isFull = false;
        public Vector3 originalPosition;
        public List<GameObject> removableParts;
        public Vector3 ogScale;
        public Vector3 newScale;
        public int playersInSeat = 0;
        private bool canCollideWitOtherVehicle = true;
        public static bool isMovingStraight = false;
        public static ColorEnum LastTouchedCarcolor;
        public bool isMovingForward = false;
        public Transform pickUPPoint;
        public int SeatCount => seats.Count;

        // Board attachment
        private Transform _parentTransform;
        private Sequence _jumpSequence;

        /// <summary>
        /// True once this vehicle has performed its initial forward drive at least once.
        /// Subsequent taps skip the drive animation and go directly to the board.
        /// </summary>
        private bool _hasStartedMoving = false;

        /// <summary>
        /// Fired when JumpToBoard animation completes and the vehicle is fully settled on the board.
        /// </summary>
        public event Action<Vehicle, ConveyorFollowerBoard> OnJumpToBoardCompleted;

        /// <summary>Fired when the conveyor board this vehicle was riding completes its path.</summary>
        public event Action<Vehicle> OnCompletedPath;

        /// <summary>Fired when a stored vehicle is tapped so GameplayController can send it back to a board.</summary>
        public event Action<Vehicle> OnJumpRequest;

        /// <summary>True while the vehicle is sitting in a grid visualizer storage slot.</summary>
        public bool IsInStorage { get; private set; }

        /// <summary>True when the vehicle still has unoccupied seats (bus has capacity remaining).</summary>
        public bool HasEmptySeats => !isFull;

        /// <summary>
        /// Set to true by <see cref="Game.GameplayController"/> once the jump-to-board animation
        /// completes and the vehicle is riding the conveyor. Cleared when the board path ends.
        /// Mirrors <see cref="Game.Shooter.IsReadyForSearchForTarget"/>.
        /// </summary>
        public bool IsReadyForPassengerSearch { get; set; }

        void Start()
        {
            transform.position = new Vector3(transform.position.x, 1f, transform.position.z);
            _parentTransform = transform.parent;
            SetInitialPosition();
            ogScale = transform.localScale;
            isMovingStraight = false;
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

        private void OnMouseDown()
        {
            // When sitting in a storage slot, tap sends the vehicle back to the conveyor.
            if (IsInStorage)
            {
                OnJumpRequest?.Invoke(this);
                return;
            }

            // After the first forward drive, skip the drive animation entirely and
            // place the vehicle directly onto the next available conveyor board.
            if (_hasStartedMoving)
            {
                SendDirectlyToBoard();
                return;
            }

            if (Helper.instance)
                Helper.instance.MoveHand();

            LastTouchedCarcolor = vehicleColor;
            _hasStartedMoving = true;

            if (CheckForVehicleInFront(out RaycastHit hitInfo))
            {
                isMovingStraight = true;
                isMovingForward = true;
                Vibration.Vibrate(40);

                Vector3 targetPosition =
                    transform.position +
                    transform.forward * (hitInfo.distance + 1);
                movingZdir = transform.DOMove(targetPosition, 0.2f).SetEase(Ease.InQuad);

                return;
            }

            MoveCarStraight();
        }

        /// <summary>
        /// Bypasses the drive-forward phase: kills active tweens, strips the roof,
        /// removes the vehicle from VehicleController, then requests a conveyor board.
        /// Used on every tap after the first drive has already been triggered.
        /// </summary>
        private void SendDirectlyToBoard()
        {
            movingZdir?.Kill();
            _jumpSequence?.Kill();
            DOTween.Kill(transform);

            isMovingStraight = false;
            isMovingForward = false;

            DeactivateRoof();

            if (VehicleController.instance != null)
                VehicleController.instance.RemoveVehicle(this);

            if (Game.GameManager.Instance != null && Game.GameManager.Instance.GameplayController != null)
                Game.GameManager.Instance.GameplayController.RequestBoardForVehicle(this);
        }

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
                if (seats[i].childCount == 0)
                {
                    playersInSeat++;
                    IsVehicleFull();
                    return seats[i];
                }
            }

            return null;
        }

        public void IsVehicleFull()
        {
            if (playersInSeat == seats.Count)
            {
                isFull = true;
                // VehicleGoing() is intentionally NOT called here.
                // GameplayController.Vehicle_OnCompletedPath triggers it once the
                // conveyor run finishes, ensuring the vehicle is never destroyed
                // while still riding the board.
            }
        }

        /// <summary>
        /// Called when all seats are filled and the vehicle has finished its conveyor run.
        /// Detaches from the board, drives off-screen along its forward axis, then self-destructs.
        /// </summary>
        public void VehicleGoing()
        {
            ResetParent();

            const float exitDistance = 15f;
            const float exitDuration = 1.2f;

            Vector3 exitTarget = transform.position + transform.forward * exitDistance;
            transform.DOMove(exitTarget, exitDuration)
                .SetEase(Ease.InQuad)
                .OnComplete(() => Destroy(gameObject));
        }

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
            movingZdir = transform.DOMove(worldPoint, 12f).SetSpeedBased();
            GetComponent<AudioSource>().enabled = true;
        }

        public bool CheckForObstacles()
        {
            float offset = 1.0f;
            float rayDistance = Mathf.Infinity;

            Vector3 forward = transform.TransformDirection(Vector3.forward);

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

        private void StrikeAndMoveBack(Vehicle targetVehicle)
        {
            Vector3 targetPosition = targetVehicle.transform.position;

            transform.DOMove(targetPosition, 0.5f).OnComplete(() =>
            {
                targetVehicle.ShakeVehicle();
                transform.DOMove(originalPosition, 0.5f);
            });
        }

        public void ShakeVehicle()
        {
            transform.DOShakeRotation(0.2f, transform.forward * 2, vibrato: 10, randomness: 90).SetEase(Ease.InBounce);
        }

        private static int counter = 0;
        private bool toggle;

        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.CompareTag("Down"))
            {
                canCollideWitOtherVehicle = false;
                movingZdir.Pause();

                toggle = !toggle;

                MoveToSideBorder(VehicleController.instance.leftCollider, -20f);

                return;
            }

            if (canCollideWitOtherVehicle && other.TryGetComponent(out Vehicle vehicle) &&
                vehicle.canCollideWitOtherVehicle)
            {
                movingZdir.Pause();

                if (!isCollided)
                {
                    if (isMovingStraight && counter == 0 && isMovingForward)
                    {
                        counter++;

                        GetComponent<AudioSource>().enabled = false;
                        SoundController.Instance.PlayOneShot(SoundController.Instance.hitSound);
                        EffectsManager.instance.PlayEffect(EffectsManager.instance.hitEffect,
                            other.ClosestPoint(transform.position + new Vector3(0, 0.25f, 0)), Quaternion.identity);
                    }

                    vehicle.ShakeVehicle();
                    transform.DOMove(originalPosition, 0.3f).SetEase(Ease.OutBack)
                        .OnComplete(() =>
                        {
                            counter = 0;
                            isMovingStraight = false;
                            isMovingForward = false;
                        });
                }
            }

            if (other.gameObject.CompareTag("Border") && !isCollided)
            {
                isCollided = true;
                isMovingStraight = false;
                canCollideWitOtherVehicle = false;

                MoveToTargetFromBorder();
                VehicleController.instance.RemoveVehicle(this);
                movingZdir.Pause();
            }

            if (other.gameObject.CompareTag("Upborder") && !isCollided)
            {
                isCollided = true;
                isMovingStraight = false;
                canCollideWitOtherVehicle = false;
                MoveToTargetFromUpBorder();
                VehicleController.instance.RemoveVehicle(this);
                movingZdir.Pause();
            }
        }

        public void MoveToSideBorder(Transform collider, float distance)
        {
            isMovingStraight = false;
            canCollideWitOtherVehicle = false;

            Transform cube = collider.transform;
            Vector3 cubePos = cube.position;

            Vector3 directionToCube = new Vector3(cubePos.x - transform.position.x, 0, 0);

            Quaternion targetRotation = Quaternion.LookRotation(directionToCube, Vector3.up);

            transform.DORotateQuaternion(targetRotation, 0.1f);
            transform.DOLocalMoveX(distance, 0.8f);
            VehicleController.instance.RemoveVehicle(this);
        }

        public void MoveToTargetFromBorder()
        {
            Transform road = VehicleController.instance.Road;
            Vector3 roadPos = road.position;

            Vector3[] path = new Vector3[]
            {
                transform.position,
                new Vector3(transform.position.x, transform.position.y, road.position.z)
            };

            transform.DORotate(Vector3.zero, 0.1f);
            transform.DOPath(path, 0.3f, PathType.Linear).SetEase(Ease.Linear).OnComplete(() =>
            {
                transform.DOLookAt(roadPos, 0.1f);
                DeactivateRoof();

                // Request a conveyor board from the PixelFlow GameplayController.
                // The vehicle will be parented to the board inside JumpToBoard.
                if (Game.GameManager.Instance != null && Game.GameManager.Instance.GameplayController != null)
                    Game.GameManager.Instance.GameplayController.RequestBoardForVehicle(this);
            });
        }

        public void DeactivateRoof()
        {
            foreach (var parts in removableParts)
            {
                parts.SetActive(false);
            }
        }

        public void MoveToTargetFromUpBorder()
        {
            DeactivateRoof();

            // Request a conveyor board from the PixelFlow GameplayController.
            if (Game.GameManager.Instance != null && Game.GameManager.Instance.GameplayController != null)
                Game.GameManager.Instance.GameplayController.RequestBoardForVehicle(this);
        }

        /// <summary>
        /// Parents this vehicle to the given ConveyorFollowerBoard and animates it into place,
        /// mirroring the Shooter.JumpToBoard flow. Fires OnJumpToBoardCompleted on completion.
        /// </summary>
        public void JumpToBoard(ConveyorFollowerBoard board)
        {
            movingZdir?.Kill();
            _jumpSequence?.Kill();
            DOTween.Kill(transform);

            transform.SetParent(board.transform);

            float duration = GameConfigs.Instance.shooterJumpToConveyorDuration;
            float power = GameConfigs.Instance.shooterJumpToConveyorPower;

            _jumpSequence = DOTween.Sequence();
            _jumpSequence.Insert(0f, transform.DOLocalJump(Vector3.zero, power, 1, duration));
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
        /// Parents this vehicle to the given storage slot and snaps it into place,
        /// mirroring Shooter.JumpToStorage.
        /// </summary>
        public void JumpToStorage(StoragePiece storage)
        {
            movingZdir?.Kill();
            _jumpSequence?.Kill();
            DOTween.Kill(transform);

            transform.SetParent(storage.transform);
            transform.localPosition = Vector3.zero;
            transform.localEulerAngles = Vector3.zero;
        }

        /// <summary>Detach from the board and restore the original parent (IBoardOccupant).</summary>
        public void ResetParent()
        {
            transform.SetParent(_parentTransform);
        }
    }
}
