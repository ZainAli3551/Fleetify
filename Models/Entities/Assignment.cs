using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fleetify.Models.Entities
{
    public class Assignment
    {
        [Key]
        public int AssignmentID { get; set; }

        [Required]
        public int RequestID { get; set; }

        [ForeignKey("RequestID")]
        public virtual DeliveryRequest? DeliveryRequest { get; set; }

        [Required]
        public int DriverID { get; set; }

        [ForeignKey("DriverID")]
        public virtual Driver? Driver { get; set; }

        [Required]
        public int VehicleID { get; set; }

        [ForeignKey("VehicleID")]
        public virtual Vehicle? Vehicle { get; set; }

        public int? AdminID { get; set; }

        [ForeignKey("AdminID")]
        public virtual Admin? Admin { get; set; }

        public DateTime AssignedDate { get; set; } = DateTime.UtcNow;

        [Required, MaxLength(30)]
        public string AssignmentStatus { get; set; } = "Pending"; // Pending, Accepted, Rejected, Completed

        public virtual ICollection<StatusUpdate> StatusUpdates { get; set; } = new List<StatusUpdate>();
    }
}
