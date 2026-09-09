using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fleetify.Models.Entities
{
    public class CostEstimate
    {
        [Key]
        public int EstimateID { get; set; }

        public int? RequestID { get; set; }

        [ForeignKey("RequestID")]
        public virtual DeliveryRequest? DeliveryRequest { get; set; }

        public double DistanceKm { get; set; }
        public double ParcelWeight { get; set; }

        [Required, MaxLength(30)]
        public string VehicleType { get; set; } = "Van";

        [Required, MaxLength(30)]
        public string RouteType { get; set; } = "Standard";

        public double BaseFare { get; set; }
        public double DistanceCharge { get; set; }
        public double WeightCharge { get; set; }
        public double TypeSurcharge { get; set; }
        public double EstimatedAmount { get; set; }

        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}
