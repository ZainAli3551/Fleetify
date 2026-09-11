using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fleetify.Models.Entities
{
    public class DeliveryRequest
    {
        [Key]
        public int RequestID { get; set; }

        [Required, MaxLength(40)]
        public string TrackingNumber { get; set; } = string.Empty; // e.g. FLT-2026-001240

        [Required]
        public int UserID { get; set; }

        [ForeignKey("UserID")]
        public virtual User? User { get; set; }

        public DateTime RequestDate { get; set; } = DateTime.UtcNow;

        [Required, MaxLength(250)]
        public string PickupLocation { get; set; } = string.Empty;

        [Required, MaxLength(250)]
        public string DropoffLocation { get; set; } = string.Empty;

        public double ParcelWeight { get; set; }

        public double Height { get; set; } = 1.0; // Height in meters/dimensions

        public double Width { get; set; } = 1.0; // Width in meters/dimensions

        [MaxLength(250)]
        public string ParcelDescription { get; set; } = string.Empty;

        [MaxLength(30)]
        public string VehicleType { get; set; } = "Truck"; // Default to Truck for customers

        [MaxLength(30)]
        public string RouteType { get; set; } = "Standard"; // Standard, Express, Fragile, HeavyCargo

        public double DistanceKm { get; set; } = 10.0;

        [Required, MaxLength(30)]
        public string RequestedStatus { get; set; } = "Pending"; // Pending, Approved, Assigned, In-Transit, Delivered, Cancelled

        public double EstimatedCost { get; set; }

        public virtual Assignment? Assignment { get; set; }
        public virtual CostEstimate? CostEstimate { get; set; }
    }
}
