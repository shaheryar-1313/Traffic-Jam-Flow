using System;
using DG.Tweening;
using Dreamteck.Splines;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game
{
    public class ConveyorFollowerBoard : MonoBehaviour
    {
        [Title("References")]
        [SerializeField] private GameObject _boardVisual;

        private SplineFollower _splineFollower;
        public bool IsInitialized { get; private set; }
        public bool IsBoardReadyForConveyor { get; private set; } = true;
        public bool IsBoardCompletedPath { get; private set; } = true;

        private IBoardOccupant _assignedOccupant;

        /// <summary>Returns the assigned occupant cast to Shooter, or null if the occupant is not a Shooter.</summary>
        public Shooter AssignedShooter => _assignedOccupant as Shooter;

        /// <summary>Returns the assigned occupant regardless of concrete type.</summary>
        public IBoardOccupant AssignedOccupant => _assignedOccupant;

        private Sequence _placeBoardToMachineSequence;
        private Sequence _placeBoardToConveyorSequence;
        private Tweener _startMoveTween;
        private Tweener _followSpeedTween;
        private float _followSpeed;
        private int _boardingRequests;
        private bool _isPausedForCollision = false;

        [SerializeField, Min(0f)]
        private float _passengerBoardingSpeedMultiplier = 0.5f;

        public event Action<ConveyorFollowerBoard> OnBoardCompletedPath;
        public event Action<ConveyorFollowerBoard> OnArrangeBoardsRequested;

        public void Initialize(SplineComputer spline)
        {
            _splineFollower = this.gameObject.AddComponent<SplineFollower>();
            _splineFollower.follow = false;
            _splineFollower.spline = spline;
            _splineFollower.followSpeed = 0f;
            _followSpeed = GameConfigs.Instance.boardFollowSpeed;
            // _followSpeed = 5f;
            _splineFollower.onEndReached += SplineFollower_OnEndReached;
            IsInitialized = true;
        }

        private void SplineFollower_OnEndReached(double obj)
        {
            if (IsBoardReadyForConveyor || IsBoardCompletedPath)
                return;
            OnCompletePath();
        }

        private void OnDestroy()
        {
            OnBoardCompletedPath = null;
            OnArrangeBoardsRequested = null;
            _startMoveTween?.Kill(false);
            _placeBoardToMachineSequence?.Kill(false);
            _placeBoardToConveyorSequence?.Kill(false);
        }

        public void CompletePath()
        {
            _splineFollower.SetPercent(1.0f);
            ResetBoard();
            IsBoardCompletedPath = true;
        }

        public void ForceCompletePath()
        {
            if (_assignedOccupant != null)
            {
                if (_assignedOccupant is UnityEngine.Object unityObj && unityObj != null)
                    _assignedOccupant.ResetParent();
                _assignedOccupant = null;
            }
            _splineFollower.SetPercent(1.0f);
            OnCompletePath();
        }

        public void PlaceBoardToMachine(int index)
        {
            float gapBetweenBoards = GameConfigs.Instance.gapBetweenBoards;
            // float gapBetweenBoards = 1.5f;

            Vector3 targetLocalPosition = new Vector3(-(index * gapBetweenBoards), 0f, 0f);
            Vector3 targetLocalAngles = new Vector3(0, 90, 0);
            
            transform.localPosition = targetLocalPosition;
            transform.localEulerAngles = targetLocalAngles;
            _boardVisual.transform.localPosition = Vector3.up * 0.75f;
            _boardVisual.transform.localRotation = Quaternion.identity;
            IsBoardReadyForConveyor = true;
            
            //_placeBoardToConveyorSequence?.Kill(false);
            //float duration = GameConfigs.Instance.boardConveyorToMachineTweenDuration;
            // _placeBoardToMachineSequence?.Kill(false);

            // _placeBoardToMachineSequence = DOTween.Sequence();
            // _placeBoardToMachineSequence.Insert(0f, transform.DOLocalMove(targetLocalPosition, duration).SetEase(Ease.Linear));
            // _placeBoardToMachineSequence.Insert(0f, transform.DOLocalRotate(targetLocalAngles, duration).SetEase(Ease.Linear));
            //
            // _placeBoardToMachineSequence.Insert(0f, _boardVisual.transform.DOLocalMoveY(0.75f, duration).SetEase(Ease.Linear));
            // _placeBoardToMachineSequence.Insert(0f, _boardVisual.transform.DOLocalRotateQuaternion(Quaternion.identity, duration).SetEase(Ease.Linear));
            //
            // _placeBoardToMachineSequence.OnComplete(() => { IsBoardReadyForConveyor = true; });
        }

        public void JumpToConveyor()
        {
            IsBoardReadyForConveyor = false;
            IsBoardCompletedPath = false;
            
            Vector3 splineStartPos = _splineFollower.EvaluatePosition(0.0f);
            float duration = GameConfigs.Instance.boardMachineToConveyorTweenDuration;
            // float duration = 0.5f;

            _startMoveTween?.Kill(false);
            _placeBoardToMachineSequence?.Kill(false);
            _placeBoardToConveyorSequence?.Kill(false);

            _placeBoardToConveyorSequence = DOTween.Sequence();
            _placeBoardToConveyorSequence.Insert(0f, transform.DOLocalRotate(new Vector3(0, 90, 0), duration));
            _placeBoardToConveyorSequence.Insert(0, _boardVisual.transform.DOLocalRotate(new Vector3(-90, 0, 0), duration * 0.75f, RotateMode.LocalAxisAdd));
            _placeBoardToConveyorSequence.Insert(0, _boardVisual.transform.DOLocalMove(Vector3.zero, duration));

            _startMoveTween = transform.DOMove(splineStartPos, duration).SetEase(Ease.Linear);

            _startMoveTween.OnComplete(() =>
            {
                _splineFollower.SetPercent(0.0);
                _splineFollower.follow = true;
            });
        }

        public void StartMove()
        {
            if (_isPausedForCollision) return;

            _splineFollower.followSpeed = _boardingRequests > 0
                ? _followSpeed * _passengerBoardingSpeedMultiplier
                : _followSpeed;
        }

        public void BeginPassengerBoarding(float duration = 0.12f)
        {
            _boardingRequests++;
            if (_boardingRequests != 1)
                return;

            SetFollowSpeed(_followSpeed * _passengerBoardingSpeedMultiplier, duration);
        }

        public void EndPassengerBoarding(float duration = 0.12f)
        {
            if (_boardingRequests <= 0)
            {
                _boardingRequests = 0;
                return;
            }

            _boardingRequests--;
            if (_boardingRequests != 0)
                return;

            SetFollowSpeed(_followSpeed, duration);
        }

        private void SetFollowSpeed(float targetSpeed, float duration)
        {
            _followSpeedTween?.Kill(false);
            if (_splineFollower == null)
            {
                return;
            }
            
            if (_isPausedForCollision) return;

            _followSpeedTween = DOTween.To(() => _splineFollower.followSpeed, x => _splineFollower.followSpeed = x,
                targetSpeed, duration)
                .SetEase(Ease.Linear);
        }

        private void OnCompletePath()
        {
            ResetBoard();
            OnBoardCompletedPath?.Invoke(this);
        }

        public void PauseMovement()
        {
            if (_isPausedForCollision) return;
            _isPausedForCollision = true;
            _followSpeedTween?.Kill(false);
            if (_splineFollower != null)
                _splineFollower.followSpeed = 0f;
            
            _startMoveTween?.Pause();
            _placeBoardToConveyorSequence?.Pause();
        }

        public void ResumeMovement()
        {
            if (!_isPausedForCollision) return;
            _isPausedForCollision = false;
            
            _startMoveTween?.Play();
            _placeBoardToConveyorSequence?.Play();

            StartMove();
        }

        public void SetAssignedShooter(Shooter shooter)
        {
            _assignedOccupant = shooter;
        }

        /// <summary>Assigns any IBoardOccupant (e.g. Vehicle) to this board.</summary>
        public void SetAssignedOccupant(IBoardOccupant occupant)
        {
            _assignedOccupant = occupant;
        }

        public void OnShooterExhausted()
        {
            ResetBoard();
            OnArrangeBoardsRequested?.Invoke(this);
        }

        private void ResetBoard()
        {
            _isPausedForCollision = false;
            _boardingRequests = 0;
            _followSpeedTween?.Kill(false);

            if (_assignedOccupant != null)
            {
                // Use Unity's overloaded == to guard against already-destroyed MonoBehaviours.
                if (_assignedOccupant is UnityEngine.Object unityObj && unityObj != null)
                    _assignedOccupant.ResetParent();
            }
            _assignedOccupant = null;
            IsBoardCompletedPath = true;
            _splineFollower.follow = false;
            _splineFollower.followSpeed = 0f;
        }
    }
}