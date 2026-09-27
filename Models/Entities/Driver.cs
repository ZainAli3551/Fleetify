using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Fleetify.Models.Entities
{
    public class Driver : BaseUser
    {
        [MaxLength(50)]
        public string LicenseNumber { get; set; } = string.Empty;

        [MaxLength(20)]
        public string AvailabilityStatus { get; set; } = "Available"; // Available, Busy, Offline

        [MaxLength(30)]
        public string DriverType { get; set; } = "Company"; // Company, Backup

        [MaxLength(500)]
        public string? BackupContactNotes { get; set; }

        [MaxLength(100)]
        public string? VehicleOwned { get; set; }

        public virtual ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
        public virtual ICollection<StatusUpdate> StatusUpdates { get; set; } = new List<StatusUpdate>();
    }
}
