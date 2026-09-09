using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Fleetify.Models.Entities
{
    public class User : BaseUser
    {
        [MaxLength(250)]
        public string Address { get; set; } = string.Empty;

        [MaxLength(20)]
        public string AccountStatus { get; set; } = "Active"; // Active, Inactive, Blocked

        public virtual ICollection<DeliveryRequest> DeliveryRequests { get; set; } = new List<DeliveryRequest>();
    }
}
