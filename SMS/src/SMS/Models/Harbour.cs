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

        public List<Wifi> Wifis { get; set; } = new List<Wifi>();

        public override bool RemoveFromContext(SmsDbContext context)
        {
            if (!base.RemoveFromContext(context))
                return false;
            context.Set<Location>().Where(l => l.HarbourId == Id).ToList().ForEach(l => l.RemoveFromContext(context));
            context.Set<Wifi>().Where(w=>w.HarbourId==Id).ToList().ForEach(w=>w.RemoveFromContext(context));
            return true;
        }

        public override bool AddOrUpdate(SmsDbContext context)
        {
            if (!base.AddOrUpdate(context))
                return false;
            Locations.ForEach(l => l.AddOrUpdate(context));
            Wifis.ForEach(w=>w.AddOrUpdate(context));
            return true;
        }
    }
}