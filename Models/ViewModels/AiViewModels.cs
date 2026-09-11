using System.Collections.Generic;

namespace Fleetify.Models.ViewModels
{
    public class CostEstimateRequest
    {
        public string? PickupLocation { get; set; }
        public string? DropoffLocation { get; set; }
        public double DistanceKm { get; set; } = 15.0;
        public double ParcelWeight { get; set; } = 2.0;
        public string VehicleType { get; set; } = "Truck";
        public string RouteType { get; set; } = "Normal";
    }

    public class CostEstimateResult
    {
        public double DistanceKm { get; set; }
        public double ParcelWeight { get; set; } = 2.0;
        public double RatePerKmKg { get; set; } = 1.50;
        public double BaseCost { get; set; }
        public double BaseRate { get; set; } = 0.00;
        public double DistanceCharge { get; set; }
        public double WeightCharge { get; set; }
        public double TypeSurcharge { get; set; }
        public double PetrolCharge { get; set; } = 500.0;
        public string DeliveryType { get; set; } = "Normal";
        public double DeliveryMultiplier { get; set; } = 1.0;
        public string Currency { get; set; } = "Rs.";
        public double EstimatedTotal { get; set; }
        public string EstimatedDeliveryDays { get; set; } = "1-2 business days";
        public int DurationMinutes { get; set; }
        public string? RoutePolyline { get; set; }
        public string Provider { get; set; } = "Default";
    }

    public class MaintenancePredictionRequest
    {
        public int VehicleId { get; set; }
        public double CurrentMileage { get; set; }
        public double LastServiceMileage { get; set; }
        public string ConditionStatus { get; set; } = "Good";
        public string VehicleType { get; set; } = "Van";
    }

    public class MaintenancePredictionResult
    {
        public int VehicleId { get; set; }
        public string AlertType { get; set; } = "Normal"; // Service Due, Inspection Due, Critical Maintenance, Normal
        public string Severity { get; set; } = "Low"; // Low, Medium, High, Critical
        public string Recommendation { get; set; } = string.Empty;
        public double MileageSinceLastService { get; set; }
        public bool RequiresImmediateAction { get; set; }
        public List<string> ActionItems { get; set; } = new();
    }
}
