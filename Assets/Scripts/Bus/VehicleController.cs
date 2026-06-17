using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace TJ.Scripts
{
    public class VehicleController : MonoBehaviour
    {
        public static VehicleController instance;
        public Vehicle[] vehicles;
        public int totalPlayersCount;
        public int playerCount;
        public TextMeshPro totalPlayerDisplay;

        [Header("Pathway Cubes")]
        public Transform upCube;
        public Transform downCube;
        public Transform leftCube;
        public Transform rightCube;
        public Transform transitCube;

        public PlayerManager playerManager;
        public MaterialHolder VehiclesMaterialHolder;
        public MaterialHolder stickmanMaterialHolder;
        public int totalSeats;
        public int totalVehicles;
        public bool shuffle = true;

        // Start is called before the first frame update
        private void Awake()
        {
            instance = this;
            VehiclesMaterialHolder.InitializeMaterialDictionary();
            stickmanMaterialHolder.InitializeMaterialDictionary();
            if (shuffle == true)
                vehicles = GetComponentsInChildren<Vehicle>(true);
            RandomVehColor();
            CalculatePlayersCount();
            CalculateTotalSeat();
        }

        private void CalculateTotalSeat()
        {
            totalSeats = vehicles.Sum(v => v.SeatCount);
        }

        private void Start()
        {
            playerCount = totalPlayersCount;
            totalPlayerDisplay.text = playerCount.ToString();
            totalVehicles = vehicles.Length;
        }

        public void UpdatePlayerCount()
        {
            playerCount--;
            totalPlayerDisplay.text = playerCount.ToString();
        }

        [ContextMenu("random")]
        public void RandomVehColor()
        {
            System.Random r = new System.Random();
            ColorEnum[] values = (ColorEnum[])Enum.GetValues(typeof(ColorEnum));
            List<ColorEnum> colors = new(values);
            colors.Remove(ColorEnum.none);
            colors = colors.OrderBy(x => r.Next()).ToList();

            int colorIndex = 0;
            for (int i = 0; i < vehicles.Length; i++)
            {
                if (colorIndex >= colors.Count)
                {
                    colorIndex = 0;
                }

                vehicles[i].ChangeColor(colors[colorIndex]);
                colorIndex++;
            }
        }

        public void RandomVehicleColors()
        {
            var groupedVehicles = vehicles.GroupBy(v => v.SeatCount);

            foreach (var group in groupedVehicles)
            {
                List<ColorEnum> existingColors = new List<ColorEnum>();
                foreach (var vehicle in group)
                {
                    existingColors.Add(vehicle.vehicleColor);
                }

                System.Random r = new System.Random();
                existingColors = existingColors.OrderBy(x => r.Next()).ToList();
                int index = 0;
                foreach (var vehicle in group)
                {
                    vehicle.ChangeColor(existingColors[index]);
                    index++;
                }
            }
        }

        [ContextMenu("changecolor of vehicles")]
        public void ChangeParkingCarsColor()
        {
            List<Vehicle> parkingVehicles = new List<Vehicle>();

            // Add all vehicles to the parkingVehicles list
            for (int i = 0; i < vehicles.Length; i++)
            {
                parkingVehicles.Add(vehicles[i]);
            }

            // Remove vehicles that are in parkedVehicles from parkingVehicles if needed

            // Group vehicles by SeatCount
            var groupedVehicles = parkingVehicles.GroupBy(v => v.SeatCount).ToList();

            System.Random r = new System.Random();

            // Interchange colors within each group
            foreach (var group in groupedVehicles)
            {
                var vehicleGroup = group.ToList();

                // Shuffle the group
                vehicleGroup = vehicleGroup.OrderBy(x => r.Next()).ToList();

                // Save the first vehicle's color to be used at the end
                ColorEnum firstVehicleColor = vehicleGroup[0].vehicleColor;

                // Interchange colors in a circular manner
                for (int i = 0; i < vehicleGroup.Count - 1; i++)
                {
                    vehicleGroup[i].ChangeColor(vehicleGroup[i + 1].vehicleColor);
                }

                // Assign the first vehicle's color to the last vehicle
                vehicleGroup[^1].ChangeColor(firstVehicleColor);
            }
        }

        public void RemoveVehicle(Vehicle vehicleToRemove)
        {
            // Convert the array to a list for easier manipulation
            List<Vehicle> vehicleList = vehicles.ToList();

            // Remove the specified vehicle
            vehicleList.Remove(vehicleToRemove);

            // Convert the list back to an array and update the vehicles array
            vehicles = vehicleList.ToArray();
        }

        public void CalculatePlayersCount()
        {
            for (int i = 0; i < vehicles.Length; i++)
            {
                totalPlayersCount += vehicles[i].SeatCount;
            }

            playerManager.InstantiatePlayers(vehicles);
        }
    }
}