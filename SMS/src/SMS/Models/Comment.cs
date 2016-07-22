using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SMS.Models
{
    public class Comment: IEntity
    {
        public string Title { get; set; }
        public string Content { get; set; }
        public double Rating { get; set; }
        [Required]
        public int? ParentId { get; set; }

        public Comment Parent { get; set; }

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