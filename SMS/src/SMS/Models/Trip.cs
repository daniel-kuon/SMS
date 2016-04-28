using System.Collections.Generic;
using System.Linq;

namespace SMS.Models
{
    public class Trip : TackBase
    {
        public string Name { get; set; }

        public string Content { get; set; }

        public List<Tack> Tacks { get; set; } = new List<Tack>();

        public bool IsDummy { get; set; }

        public override bool RemoveFromContext(SmsDbContext context)
        {
            if (!base.RemoveFromContext(context))
                return false;
            context.Set<Tack>().Where(t=>t.TripId==Id).ToList().ForEach(t=>t.RemoveFromContext(context));
            return true;
        }

        public override bool AddOrUpdate(SmsDbContext context)
        {
            if (!base.AddOrUpdate(context))
                return false;
            if (Id==null)
                Tacks.ForEach(t=>t.AddOrUpdate(context));
            return true;
        }
    }
}