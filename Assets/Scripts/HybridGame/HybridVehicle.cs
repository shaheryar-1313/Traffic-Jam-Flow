using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HybridGame
{
    /// <summary>
    /// A tappable vehicle in the selection queue.
    /// Auto-generates seat transforms along its length at Start.
    /// Once tapped, it signals HybridGameplayController to dispatch it onto the conveyor.
    /// </summary>
    public class HybridVehicle : MonoBehaviour
    {
        public ColorEnum vehicleColor;

        [SerializeField] private int _seatCount = 4;

        private List<Transform> _seats = new();
        private readonly HashSet<int> _occupiedSeatIndices = new();
        private Renderer _bodyRenderer;

        public int SeatCount => _seatCount;
        public int PassengersLoaded => _occupiedSeatIndices.Count;
        public bool IsFull => PassengersLoaded >= SeatCount;
        public bool IsDeployed { get; private set; }

        public event Action<HybridVehicle> OnTapped;

        private void Start()
        {
            _bodyRenderer = GetComponent<Renderer>();
            GenerateSeats();
        }

        private void OnMouseDown()
        {
            if (IsDeployed) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            IsDeployed = true;
            PlayTapFeedback();
            OnTapped?.Invoke(this);
        }

        /// <summary>Reserves and returns the next free seat transform. Returns null when full.</summary>
        public Transform GetFreeSeat()
        {
            for (int i = 0; i < _seats.Count; i++)
            {
                if (!_occupiedSeatIndices.Contains(i))
                {
                    _occupiedSeatIndices.Add(i);
                    return _seats[i];
                }
            }
            return null;
        }

        /// <summary>Applies the given color to this vehicle's body renderer via MaterialPropertyBlock.</summary>
        public void ApplyColor(Color color)
        {
            if (_bodyRenderer == null) return;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color);
            _bodyRenderer.SetPropertyBlock(mpb);
        }

        /// <summary>Punches the vehicle scale on tap for visual feedback.</summary>
        public void PlayTapFeedback()
        {
            transform.DOPunchScale(Vector3.one * 0.25f, 0.3f, 5, 0.5f);
        }

        private void GenerateSeats()
        {
            float totalLength = transform.localScale.x * 0.9f;
            float step = _seatCount > 1 ? totalLength / (_seatCount - 1) : 0f;
            float startX = -totalLength * 0.5f;

            for (int i = 0; i < _seatCount; i++)
            {
                GameObject seatGo = new GameObject($"Seat_{i}");
                seatGo.transform.SetParent(transform);
                seatGo.transform.localPosition = new Vector3(startX + i * step, 0.6f, 0f);
                _seats.Add(seatGo.transform);
            }
        }
    }
}
