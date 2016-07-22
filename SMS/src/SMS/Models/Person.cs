using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace SMS.Models
{
    public class Person: IEntity
    {
        [Required]
        public string FirstName { get; set; }

        [Required]
        public string LastName { get; set; }

       // public List<Trip> Trips { get; set; } = new List<Trip>();
        public List<Job> Tasks { get; set; }=new List<Job>();

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

       public  bool RemoveFromContext(SmsDbContext context)
        {
            if (Id == null || context.Entry(this).State == EntityState.Deleted)
                return false;
            context.Remove(this);
            context.Set<Crew>().Where(c=>c.PersonId==Id).ToList().ForEach(c=>c.RemoveFromContext(context));
            context.Set<Job>().Where(j=>j.AssignedToId==Id).ToList().ForEach(j=>j.RemoveFromContext(context));
            //Comments.ForEach(c => c.RemoveFromContext(context));
            //(Album ?? new Album() { Id = AlbumId })?.RemoveFromContext(context);
            return true;
        }

        public  bool AddOrUpdate(SmsDbContext context)
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
            //Album?.AddOrUpdate(context);
            //Comments.ForEach(c => c.AddOrUpdate(context));
            return true;
        }
    }
}