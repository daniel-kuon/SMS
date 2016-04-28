using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace SMS.Models
{
    public class Person:Entity
    {
        [Required]
        public string FirstName { get; set; }

        [Required]
        public string LastName { get; set; }

       // public List<Trip> Trips { get; set; } = new List<Trip>();
        public List<Job> Tasks { get; set; }=new List<Job>();

        public override bool RemoveFromContext(SmsDbContext context)
        {
            if (!base.RemoveFromContext(context))
                return false;
            context.Set<Crew>().Where(c=>c.PersonId==Id).ToList().ForEach(c=>c.RemoveFromContext(context));
            context.Set<Job>().Where(j=>j.AssignedToId==Id).ToList().ForEach(j=>j.RemoveFromContext(context));
            return true;
        }

        public override bool AddOrUpdate(SmsDbContext context)
        {
            return base.AddOrUpdate(context);
        }
    }
}