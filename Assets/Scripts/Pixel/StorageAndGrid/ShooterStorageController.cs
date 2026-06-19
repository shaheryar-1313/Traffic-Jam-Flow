using TJ.Scripts;
using UnityEngine;
using UnityEngine.Pool;

namespace Game
{
    public class ShooterStorageController : MonoBehaviour
    {
        public bool IsInitialized { get; private set; }
        public bool IsPrepared { get; private set; }
        private StoragePiece[] _storageVisualPieces;

        public void Initialize()
        {
            IsInitialized = true;
        }

        public void Prepare(StoragePiece[] storageVisualPieces)
        {
            _storageVisualPieces = storageVisualPieces;
            IsPrepared = true;
        }

        /// <summary>
        /// Tries to place a vehicle into the first available storage slot.
        /// Returns false if all slots are occupied (triggers fail condition upstream).
        /// </summary>
        public bool TryConsumeVehicle(Vehicle vehicle)
        {
            if (_storageVisualPieces == null || _storageVisualPieces.Length == 0)
            {
                Debug.LogWarning("[ShooterStorageController] Storage pieces not prepared yet — cannot consume vehicle.");
                return false;
            }

            foreach (StoragePiece storage in _storageVisualPieces)
            {
                if (storage.AssignedVehicle != null)
                    continue;

                vehicle.JumpToStorage(storage);
                storage.Assign(vehicle);

                return true;
            }

            return false;
        }

        /// <summary>Removes the vehicle from whichever storage slot it occupies.</summary>
        public void ReleaseVehicle(Vehicle vehicle)
        {
            foreach (StoragePiece storage in _storageVisualPieces)
            {
                if (storage.AssignedVehicle == vehicle)
                {
                    storage.Unassign();
                    break;
                }
            }
        }

        /// <summary>Returns true and the matching storage slot if the vehicle is currently stored.</summary>
        public bool IsVehicleInStorage(Vehicle vehicle, out StoragePiece storagePiece)
        {
            storagePiece = null;
            foreach (StoragePiece storage in _storageVisualPieces)
            {
                if (storage.AssignedVehicle == vehicle)
                {
                    storagePiece = storage;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Compacts stored vehicles to fill from the front after one is removed,
        /// mirroring the original shooter storage arrange behaviour.
        /// </summary>
        public void ArrangeStorageVehicles()
        {
            // Removed shifting logic as requested to prevent vehicles moving to previous slots
            // This also fixes the visual bug causing multiple cars to end up in the same slot
            // due to tween conflicts when shifting!
        }
    }
}
