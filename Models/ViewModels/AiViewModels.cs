using System.Collections.Generic;

namespace Fleetify.Models.ViewModels
{
    public class CostEstimateRequest
    {
        public string? PickupLocation { get; set; }
        public string? DropoffLocation { get; set; }
        public double DistanceKm { get; set; } = 15.0;
        public double ParcelWeight { get; set; } = 2.0;
        public string VehicleType { get; set; } = "Van";
        public string RouteType { get; set; } = "Standard";
    }

    public class CostEstimateResult
    {
        public double DistanceKm { get; set; }
        public double BaseRate { get; set; }
        public double WeightCharge { get; set; }
        public double TypeSurcharge { get; set; }
        public double DistanceCharge { get; set; }
        public double EstimatedTotal { get; set; }
        public string EstimatedDeliveryDays { get; set; } = "1-2 business days";
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
