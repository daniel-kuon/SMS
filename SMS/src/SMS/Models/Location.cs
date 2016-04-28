using System.ComponentModel.DataAnnotations.Schema;

namespace SMS.Models
{
    public class Location : Entity
    {
        public int? HarbourId { get; set; }

        public string Website { get; set; }
        public string Name { get; set; }

        public Address Address { get; set; } = new Address();
        public int? AddressId { get; set; }


        public double Rating { get; set; }
        public override Album Album { get; set; }=new Album();
        public LocationType Type { get; set; }

        public override bool RemoveFromContext(SmsDbContext context)
        {
            if (!base.RemoveFromContext(context))
                return false;
            (Address ?? new Address() {Id = AddressId}).RemoveFromContext(context);
            return true;
        }

        public override bool AddOrUpdate(SmsDbContext context)
        {
            if (!base.AddOrUpdate(context))
                return false;
            if (Id == null)
                Address.AddOrUpdate(context);
            return true;
        }
    }
}