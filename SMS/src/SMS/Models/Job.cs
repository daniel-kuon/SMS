using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace SMS.Models
{
    public class Job:Entity
    {
        public DateTime DueTo { get; set; }

        public Person AssignedTo { get; set; }
        public int? AssignedToId { get; set; }

        [Required]
        public string Title { get; set; }

        public string Content { get; set; }

        public bool Done { get; set; }

        public Job SuperJob { get; set; }
        public int? SuperJobId { get; set; }
        public List<Job> SubJobs { get; set; }=new List<Job>();

        public Trip Trip { get; set; }
        public int? TripId { get; set; }

        public override bool RemoveFromContext(SmsDbContext context)
        {
            if (!base.RemoveFromContext(context))
                return false;
            context.Set<Job>().Where(j=>j.SuperJobId==Id).ToList().ForEach(j=>j.RemoveFromContext(context));
            return true;
        }

    }
}