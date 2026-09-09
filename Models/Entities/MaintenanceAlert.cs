using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fleetify.Models.Entities
{
    public class MaintenanceAlert
    {
        [Key]
        public int AlertID { get; set; }

        [Required]
        public int VehicleID { get; set; }

        [ForeignKey("VehicleID")]
        public virtual Vehicle? Vehicle { get; set; }

        public DateTime AlertDate { get; set; } = DateTime.UtcNow;

        [Required, MaxLength(50)]
        public string AlertType { get; set; } = "Service Due"; // Service Due, Inspection Due, Critical Maintenance

        [Required, MaxLength(500)]
        public string Recommendation { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string AlertStatus { get; set; } = "New"; // New, Reviewed, Resolved

        public double CurrentMileageAtAlert { get; set; }
    }
}
