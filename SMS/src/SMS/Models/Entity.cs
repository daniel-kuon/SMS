using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.Data.Entity;

namespace SMS.Models
{
    public abstract class Entity : IEntity
    {
        [NotMapped]
        public int? ClientId { get; set; }

        [Key]
        public int? Id { get; set; }

        public virtual Album Album { get; set; }
        public int? AlbumId { get; set; }

        [NotMapped]
        public string Type => GetType().Name;

        public List<Comment> Comments { get; set; } = new List<Comment>();

        public virtual bool RemoveFromContext(SmsDbContext context)
        {
            if (Id==null || context.Entry(this).State == EntityState.Deleted)
                return false;
            context.Remove(this);
            Comments.ForEach(c => c.RemoveFromContext(context));
            (Album??new Album() {Id = AlbumId})?.RemoveFromContext(context);
            return true;
        }

        public virtual bool AddOrUpdate(SmsDbContext context)
        {
            if (Id == 0)
            {
                if (context.Entry(this).State == EntityState.Added)
                    return false;
                Album?.AddOrUpdate(context);
                Comments.ForEach(c => c.AddOrUpdate(context));
                context.Add(this);
            }
            else
            {
                if (context.Entry(this).State == EntityState.Modified)
                    return false;
                context.Update(this);
            }
            return true;
        }



    }
}