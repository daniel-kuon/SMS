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

        public double? Distance { get; set; }

        public Harbour Start { get; set; }

        public Harbour End { get; set; }

        [Required]
        public List<Crew> Crew { get; set; } = new List<Crew>();

        [NotMapped]
        public List<Person> Persons { get; set; } = new List<Person>();

        [Required]
        public int? EndId { get; set; }
        [Required]
        public int? StartId { get; set; }

        private void FixPersons(SmsDbContext context)
        {
            Crew = context.Set<Crew>().Where(c => c.TackId == Id).ToList();
            Crew.AddRange(Persons.Where(p=> Crew.All(c => c.PersonId != p.Id)).Select(p => new Crew() { PersonId = p.Id, Tack = this, TackId = Id }));
            Crew.Where(c=>Persons.All(p=>c.PersonId!=p.Id)).ToList().ForEach(c=>context.Remove(c));
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
            FixPersons(context);
            Crew.ForEach(c => AddOrUpdate(context));
            return true;
        }
    }
}