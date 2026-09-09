using System.Collections.Generic;

namespace Fleetify.Models.Entities
{
    public class Admin : BaseUser
    {
        public virtual ICollection<Assignment> ManagedAssignments { get; set; } = new List<Assignment>();
    }
}
