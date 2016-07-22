using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace SMS.Models
{
    public abstract class TackBase : IEntity
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
        
        public virtual Album Album { get; set; }
        public int? AlbumId { get; set; }

        [Required]
        public int? EndId { get; set; }
        [Required]
        public int? StartId { get; set; }

        [NotMapped]
        public  int? ClientId { get; set; }

        [NotMapped]
        public  bool? ProcessOnSever { get; set; }

        [Key]
        public  int? Id { get; set; }

        public  DateTime InsertDate { get; set; }
        public  DateTime UpdateDate { get; set; }

        [NotMapped]
        public  string Type => GetType().Name;

        private void FixPersons(SmsDbContext context)
        {

            //Crew.AddRange(Persons.Where(p=> Crew.All(c => c.PersonId != p.Id)).Select(p => new Crew() { PersonId = p.Id, Tack = this, TackId = Id }));

            //Crew.Where(c=>Persons.All(p=>c.PersonId!=p.Id)).ToList().ForEach(c=>context.Remove(c));
        }

        public virtual bool RemoveFromContext(SmsDbContext context)
        {
            if (Id == null || context.Entry(this).State == EntityState.Deleted)
                return false;
            context.Remove(this);
            context.Set<Crew>().Where(c => c.TackId == Id).ToList().ForEach(c => c.RemoveFromContext(context));
            (Album ?? new Album() { Id = AlbumId })?.RemoveFromContext(context);
            //Comments.ForEach(c => c.RemoveFromContext(context));
            //(Album ?? new Album() { Id = AlbumId })?.RemoveFromContext(context);
            return true;
        }

        public virtual bool AddOrUpdate(SmsDbContext context)
        {
            if (ProcessOnSever == false)
                return false;
            if (Id == null)
            {
                if (context.Entry(this).State == EntityState.Added)
                    return false;
                context.Add(this);
                InsertDate = DateTime.Now;
            }
            else if (Id < 0)
                return false;
            else
            {
                if (context.Entry(this).State == EntityState.Modified)
                    return false;
                UpdateDate = DateTime.Now;
                context.Update(this);
            }
            var uploadedCrew=new List<Crew>(Crew);
            Crew = context.Set<Crew>().Where(c => c.TackId == Id).ToList();
            context.AddRange(uploadedCrew.Where(c => !Crew.Any(eC => eC.PersonId == c.PersonId && eC.TackId == c.TackId)));
            Crew.AddRange(uploadedCrew.Where(c => !Crew.Any(eC => eC.PersonId == c.PersonId && eC.TackId == c.TackId)));
            Crew.RemoveAll(eC => !uploadedCrew.Any(c => eC.PersonId == c.PersonId && eC.TackId == c.TackId));
            //foreach (
            //    var crew in uploadedCrew.Where(c => !Crew.Any(eC => eC.PersonId == c.PersonId && eC.TackId == c.TackId))
            //)
            //    context.Entry(crew).State=EntityState.Added;
            //List<Crew> removedCrew = existingCrew.Where(eC => !Crew.Any(c => eC.PersonId == c.PersonId && eC.TackId == c.TackId)).ToList();
            //Crew.AddRange(removedCrew);
            //context.RemoveRange(removedCrew);

            //Crew.Clear();
            Album?.AddOrUpdate(context);
            //Album?.AddOrUpdate(context);
            //Comments.ForEach(c => c.AddOrUpdate(context));
            return true;
        }
    }
}