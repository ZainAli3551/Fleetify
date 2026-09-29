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
        public string OriginType { get; set; } = "DoorstepPickup"; // DoorstepPickup, WarehouseDropoff
        public string DestinationType { get; set; } = "DoorstepDelivery"; // DoorstepDelivery, WarehousePickup
        public bool IsPrivateTransport { get; set; } = false;
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
        public double PetrolCharge { get; set; } = 0.0;
        public string DeliveryType { get; set; } = "Normal";
        public double DeliveryMultiplier { get; set; } = 1.0;
        public bool IsPrivateTransport { get; set; } = false;
        public double PrivateTransportSurcharge { get; set; } = 0.0;
        public bool IsSpecialDelivery { get; set; } = false;
        public double SpecialDeliverySurcharge { get; set; } = 0.0;
        public string OriginCity { get; set; } = string.Empty;
        public string DestinationCity { get; set; } = string.Empty;
        public bool HasOriginWarehouse { get; set; } = true;
        public bool HasDestinationWarehouse { get; set; } = true;
        public string? OriginWarehouseName { get; set; }
        public string? DestinationWarehouseName { get; set; }
        public string Currency { get; set; } = "Rs.";
        public double EstimatedTotal { get; set; }
        public string EstimatedDeliveryDays { get; set; } = "1-2 business days";
        public int DurationMinutes { get; set; }
        public string? RoutePolyline { get; set; }
        public double? OriginLat { get; set; }
        public double? OriginLng { get; set; }
        public double? DestinationLat { get; set; }
        public double? DestinationLng { get; set; }
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
