using System;
using DG.Tweening;
using UnityEngine;

namespace HybridGame
{
    /// <summary>
    /// A moving carrier that transports a HybridVehicle along the conveyor waypoint path.
    /// Created at runtime by HybridConveyorSystem — not a prefab asset.
    /// </summary>
    public class HybridConveyorBoard : MonoBehaviour
    {
        public HybridVehicle AssignedVehicle { get; private set; }

        /// <summary>True once the board has started its conveyor path (not just travelling to the entry point).</summary>
        public bool IsOnConveyor { get; private set; }

        public event Action<HybridConveyorBoard> OnExitedConveyor;

        private Tween _pathTween;

        /// <summary>Parents the vehicle to this board and aligns it.</summary>
        public void AssignVehicle(HybridVehicle vehicle)
        {
            AssignedVehicle = vehicle;
            vehicle.transform.SetParent(transform);
            vehicle.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            vehicle.transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Begins movement along the given world-space waypoints at the specified speed (units per second).
        /// </summary>
        public void StartConveyorPath(Vector3[] waypoints, float speed)
        {
            IsOnConveyor = true;

            float totalDist = 0f;
            for (int i = 1; i < waypoints.Length; i++)
                totalDist += Vector3.Distance(waypoints[i - 1], waypoints[i]);

            float duration = totalDist / Mathf.Max(speed, 0.01f);

            _pathTween = transform.DOPath(waypoints, duration, PathType.Linear)
                .SetEase(Ease.Linear)
                .OnComplete(() =>
                {
                    IsOnConveyor = false;
                    OnExitedConveyor?.Invoke(this);
                });
        }

        /// <summary>Kills any active tween, un-parents the vehicle, and marks as inactive.</summary>
        public void Release()
        {
            _pathTween?.Kill();
            IsOnConveyor = false;

            if (AssignedVehicle != null)
            {
                AssignedVehicle.transform.SetParent(null);
                AssignedVehicle = null;
            }
        }

        private void OnDestroy()
        {
            _pathTween?.Kill();
            OnExitedConveyor = null;
        }
    }
}
