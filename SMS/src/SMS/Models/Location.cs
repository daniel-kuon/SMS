using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SMS.Models
{
    public class Location : IEntity
    {
        public int? HarbourId { get; set; }
        public Harbour Harbour { get; set; }

        public string Website { get; set; }
        public string Name { get; set; }

        public Address Address { get; set; } = new Address();
        public int? AddressId { get; set; }


        public double Rating { get; set; }
        //public LocationType LocationType { get; set; }

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
            (Address ?? new Address() { Id = AddressId }).RemoveFromContext(context);
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
            Address.AddOrUpdate(context);
            //Album?.AddOrUpdate(context);
            //Comments.ForEach(c => c.AddOrUpdate(context));
            return true;
        }
    }
}