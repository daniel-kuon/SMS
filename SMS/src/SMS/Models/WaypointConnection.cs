using Microsoft.EntityFrameworkCore;

namespace SMS.Models
{
    public class WaypointConnection : IEntityBase
    {
        public int Waypoint1Id { get; set; }
        public int Waypoint2Id { get; set; }

        public Waypoint Waypoint1 { get; set; }
        public Waypoint Waypoint2 { get; set; }
        public bool RemoveFromContext(SmsDbContext context)
        {
            if (context.Entry(this).State == EntityState.Deleted)
                return false;
            context.Remove(this);
            return true;
        }

        public bool AddOrUpdate(SmsDbContext context)
        {
            if (context.Entry(this).State == EntityState.Added)
                return false;
            context.Add(this);
            return true;
        }
    }
}