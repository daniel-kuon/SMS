using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace SMS.Models
{
    public class Album : IEntity
    {

        public bool StandAlone { get; set; }

        public string Title { get; set; }

        [NotMapped]
        public List<Image> Images { get; set; } = new List<Image>();

        public List<AlbumImage> AlbumImages { get; set; } = new List<AlbumImage>();

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

        private void FixImages(SmsDbContext context)
        {
            AlbumImages = context.Set<AlbumImage>().Where(c => c.AlbumId == Id).ToList();
            AlbumImages.AddRange(Images.Where(i => AlbumImages.All(c => c.ImageId != i.Id)).Select(i => new AlbumImage() { ImageId = i.Id, Album = this, AlbumId = Id = Id, Image = i }));
            AlbumImages.Where(aI => Images.All(i => aI.ImageId != i.Id)).ToList().ForEach(aI => context.Remove(aI));
        }

        public bool RemoveFromContext(SmsDbContext context)
        {
            if (Id == null || context.Entry(this).State == EntityState.Deleted)
                return false;
            context.Remove(this);
            FixImages(context);
            context.Set<AlbumImage>().Where(aI => aI.AlbumId == Id).ToList().ForEach(aI => aI.RemoveFromContext(context));
            return true;
        }

        public bool AddOrUpdate(SmsDbContext context)
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
            if (context.Entry(this).State == EntityState.Modified)
                return false;
            UpdateDate = DateTime.Now;

            FixImages(context);
            AlbumImages.ForEach(aI => aI.AddOrUpdate(context));
            return true;
        }

    }
}