using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fleetify.Models.Entities
{
    public class StatusUpdate
    {
        [Key]
        public int UpdateID { get; set; }

        [Required]
        public int AssignmentID { get; set; }

        [ForeignKey("AssignmentID")]
        public virtual Assignment? Assignment { get; set; }

        [Required]
        public int DriverID { get; set; }

        [ForeignKey("DriverID")]
        public virtual Driver? Driver { get; set; }

        [Required, MaxLength(30)]
        public string UpdateStatus { get; set; } = "Pending"; // Pending, In-Transit, Delivered, Cancelled

        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;

        [MaxLength(500)]
        public string Remarks { get; set; } = string.Empty;
    }
}
