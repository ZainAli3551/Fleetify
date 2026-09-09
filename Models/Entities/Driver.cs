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

        public virtual ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
        public virtual ICollection<StatusUpdate> StatusUpdates { get; set; } = new List<StatusUpdate>();
    }
}
