using System;
using DG.Tweening;
using UnityEngine;

namespace HybridGame
{
    /// <summary>
    /// A single passenger cell in the square grid. Knows its color and animates to a vehicle seat.
    /// </summary>
    public class HybridPassenger : MonoBehaviour
    {
        public ColorEnum Color { get; private set; }
        public bool IsBoarded { get; private set; }

        private Renderer _renderer;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
        }

        /// <summary>Sets the passenger's display color using a MaterialPropertyBlock (no material instancing).</summary>
        public void Initialize(ColorEnum color, Color visualColor)
        {
            Color = color;
            if (_renderer == null) return;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", visualColor);
            _renderer.SetPropertyBlock(mpb);
        }

        /// <summary>
        /// Arcs the passenger to the target seat transform.
        /// IsBoarded is set immediately to prevent double-booking across frames.
        /// </summary>
        public void BoardVehicle(Transform seat, Action onComplete)
        {
            if (IsBoarded) return;
            IsBoarded = true;

            Vector3 arcPeak = (transform.position + seat.position) * 0.5f + Vector3.up * 1.5f;
            Vector3[] path = { transform.position, arcPeak, seat.position };

            transform.DOPath(path, 0.55f, PathType.CatmullRom)
                .SetEase(Ease.InOutSine)
                .OnComplete(() =>
                {
                    transform.SetParent(seat);
                    transform.localPosition = Vector3.zero;
                    gameObject.SetActive(false);
                    onComplete?.Invoke();
                });
        }
    }
}
