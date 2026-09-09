using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Fleetify.Models.Entities
{
    public class Vehicle
    {
        [Key]
        public int VehicleID { get; set; }

        [Required, MaxLength(30)]
        public string VehicleNumber { get; set; } = string.Empty; // e.g. LEA-2024-901

        [Required, MaxLength(30)]
        public string VehicleType { get; set; } = "Van"; // Bike, Car, Van, Truck

        [Required, MaxLength(100)]
        public string Model { get; set; } = string.Empty; // e.g. Toyota HiAce 2023

        public double CapacityKg { get; set; }

        [Required, MaxLength(30)]
        public string AvailabilityStatus { get; set; } = "Available"; // Available, Assigned, Under Service

        [Required, MaxLength(30)]
        public string ConditionStatus { get; set; } = "Good"; // Good, Warning, Critical

        public double CurrentMileage { get; set; }

        public double LastServiceMileage { get; set; }

        public virtual ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
        public virtual ICollection<MaintenanceAlert> MaintenanceAlerts { get; set; } = new List<MaintenanceAlert>();
    }
}
