using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace SMS.Models
{
    public class Waypoint :  IEntity 
    {
        public int? WaypointNumber { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        [Required]
        public double Latitude { get; set; }

        [Required]
        public double Longitude { get; set; }

        [NotMapped]
        public  int? ClientId { get; set; }

        [NotMapped]
        public  bool? ProcessOnSever { get; set; }

        [Key]
        public  int? Id { get; set; }

        public  DateTime InsertDate { get; set; }
        public  DateTime UpdateDate { get; set; }

        public virtual Album Album { get; set; }
        public int? AlbumId { get; set; }

        [NotMapped]
        public  string Type => GetType().Name;

        public virtual bool RemoveFromContext(SmsDbContext context)
        {
            if (Id == null || context.Entry(this).State == EntityState.Deleted)
                return false;
            context.Remove(this);
            context.Set<WaypointConnection>().Where(wC=>wC.Waypoint1Id==Id || wC.Waypoint2Id==Id).ToList().ForEach(wC=>wC.RemoveFromContext(context));
            (Album ?? new Album() { Id = AlbumId })?.RemoveFromContext(context);
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
            Album?.AddOrUpdate(context);
            //Album?.AddOrUpdate(context);
            //Comments.ForEach(c => c.AddOrUpdate(context));
            return true;
        }
    }
}