using Microsoft.EntityFrameworkCore;

namespace SMS.Models
{
    public class Crew:IEntityBase
    {
        public int? TackId { get; set; }
        public int? PersonId { get; set; }
        public Person Person { get; set; }
        public TackBase Tack { get; set; }


        public bool RemoveFromContext(SmsDbContext context)
        {
            if (context.Entry(this).State == EntityState.Deleted)
                return false;
            (Tack??new Tack() {Id = TackId}).RemoveFromContext(context);
            context.Remove(this);
            return true;
        }

        public bool AddOrUpdate(SmsDbContext context)
        {
            if (TackId == null || PersonId == null)
                context.Add(this);
            return true;
        }
    }
}