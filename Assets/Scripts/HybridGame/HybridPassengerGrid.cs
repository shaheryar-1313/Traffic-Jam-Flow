using System;
using System.Collections.Generic;
using UnityEngine;

namespace HybridGame
{
    /// <summary>
    /// Spawns and manages an N×N square grid of passengers at the top-centre of the scene.
    /// Passengers are created as primitives at runtime — no prefab dependency needed.
    /// </summary>
    public class HybridPassengerGrid : MonoBehaviour
    {
        public static HybridPassengerGrid Instance { get; private set; }

        [SerializeField] private int _gridSize = 4;
        [SerializeField] private float _cellSpacing = 1.0f;
        [SerializeField] private float _passengerScale = 0.55f;

        public List<HybridPassenger> Passengers { get; private set; } = new();
        public event Action OnAllPassengersBoarded;

        private int _boardedCount;

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// Clears any existing passengers and spawns a new grid with the given color assignments.
        /// The colors array length must equal gridSize * gridSize.
        /// </summary>
        public void Populate(ColorEnum[] colors, Color[] colorPalette)
        {
            ClearPassengers();
            _boardedCount = 0;

            int total = _gridSize * _gridSize;
            float halfExtent = (_gridSize - 1) * _cellSpacing * 0.5f;

            for (int i = 0; i < total; i++)
            {
                int row = i / _gridSize;
                int col = i % _gridSize;

                float x = col * _cellSpacing - halfExtent;
                float z = -row * _cellSpacing + halfExtent;

                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Passenger_{i}";
                go.transform.SetParent(transform);
                go.transform.localPosition = new Vector3(x, 0f, z);
                go.transform.localScale = Vector3.one * _passengerScale;

                // Disable physics — passengers are purely visual until boarded
                Collider col3d = go.GetComponent<Collider>();
                if (col3d != null) col3d.enabled = false;

                HybridPassenger passenger = go.AddComponent<HybridPassenger>();

                ColorEnum colorEnum = i < colors.Length ? colors[i] : ColorEnum.none;
                Color visualColor = GetVisualColor(colorEnum, colorPalette);
                passenger.Initialize(colorEnum, visualColor);

                Passengers.Add(passenger);
            }
        }

        /// <summary>Returns the first unboarded passenger matching the given color, or null.</summary>
        public HybridPassenger FindUnboardedByColor(ColorEnum color)
        {
            foreach (HybridPassenger p in Passengers)
                if (!p.IsBoarded && p.Color == color)
                    return p;
            return null;
        }

        /// <summary>Called by HybridGameplayController after each passenger completes boarding.</summary>
        public void NotifyPassengerBoarded()
        {
            _boardedCount++;
            if (_boardedCount >= Passengers.Count)
                OnAllPassengersBoarded?.Invoke();
        }

        private void ClearPassengers()
        {
            foreach (HybridPassenger p in Passengers)
                if (p != null) Destroy(p.gameObject);
            Passengers.Clear();
        }

        private Color GetVisualColor(ColorEnum color, Color[] palette)
        {
            int idx = (int)color;
            if (palette != null && idx >= 0 && idx < palette.Length)
                return palette[idx];
            return UnityEngine.Color.white;
        }
    }
}
