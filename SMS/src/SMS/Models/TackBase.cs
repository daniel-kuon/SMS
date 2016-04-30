using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace SMS.Models
{
    public abstract class TackBase : Entity
    {
        [Required]
        public DateTime StartDate { get; set; }

        [Required]
        public DateTime EndDate { get; set; }

        [Required]
        public double? Distance { get; set; }

        [Required]
        public Harbour Start { get; set; }

        [Required]
        public Harbour End { get; set; }

        [Required]
        public List<Crew> Crew { get; set; } = new List<Crew>();

        [NotMapped]
        public List<Person> Persons { get; set; } = new List<Person>();

        public int? EndId { get; set; }
        public int? StartId { get; set; }
        public override Album Album { get; set; } = new Album();

        private void FixPersons()
        {
            Crew.AddRange(Persons.Select(p => new Crew() { PersonId = p.Id, Tack = this, TackId = Id }));
        }

        public override bool RemoveFromContext(SmsDbContext context)
        {
            if (!base.RemoveFromContext(context)) return false;
            context.Set<Crew>().Where(c => c.TackId == Id).ToList().ForEach(c => c.RemoveFromContext(context));
            return true;
        }

        public override bool AddOrUpdate(SmsDbContext context)
        {

            if (!base.AddOrUpdate(context))
                return false;
            FixPersons();
            Crew.ForEach(c => AddOrUpdate(context));
            return true;
        }
    }
}