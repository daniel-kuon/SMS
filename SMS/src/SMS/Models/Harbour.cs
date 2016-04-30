using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using Microsoft.AspNet.Http.Features;

namespace SMS.Models
{
    public class Harbour : Waypoint
    {
        public string Website { get; set; }

        public string Content { get; set; }

        public List<Location> Locations { get; set; } = new List<Location>();

        public double Rating { get; set; }
        public override Album Album { get; set; } = new Album();

        public override bool RemoveFromContext(SmsDbContext context)
        {
            if (!base.RemoveFromContext(context))
                return false;
            context.Set<Location>().Where(l => l.HarbourId == Id).ToList().ForEach(l => l.RemoveFromContext(context));
            return true;
        }

        public override bool AddOrUpdate(SmsDbContext context)
        {
            if (!base.AddOrUpdate(context))
                return false;
            Locations.ForEach(l => l.AddOrUpdate(context));
            return true;
        }
    }
}