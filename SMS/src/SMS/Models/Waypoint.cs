using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace SMS.Models
{
    public class Waypoint : Entity
    {
        public int? WaypointNumber { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        [Required]
        public double Latitude { get; set; }

        [Required]
        public double Longitude { get; set; }

        public override bool RemoveFromContext(SmsDbContext context)
        {
            if (!base.RemoveFromContext(context))
                return false;
            context.Set<WaypointConnection>().Where(wC=>wC.Waypoint1Id==Id || wC.Waypoint2Id==Id).ToList().ForEach(wC=>wC.RemoveFromContext(context));
            return true;
        }
    }
}