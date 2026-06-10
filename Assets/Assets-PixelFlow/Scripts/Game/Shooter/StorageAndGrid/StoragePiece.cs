using TJ.Scripts;
using UnityEngine;

namespace Game
{
    public class StoragePiece : GridPiece
    {
        public Vehicle AssignedVehicle { get; private set; }

        /// <summary>Assigns a vehicle to occupy this storage slot.</summary>
        public void Assign(Vehicle vehicle)
        {
            AssignedVehicle = vehicle;
        }

        private void OnDestroy()
        {
            AssignedVehicle = null;
        }

        /// <summary>Clears the slot without touching the vehicle's state.</summary>
        public void Unassign()
        {
            if (AssignedVehicle == null)
                return;

            AssignedVehicle = null;
        }
    }
}
