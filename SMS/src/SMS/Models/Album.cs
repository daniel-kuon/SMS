using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Drawing.Printing;
using System.Linq;
using System.Net.NetworkInformation;
using Microsoft.Data.Entity;
using Remotion.Linq.Parsing.Structure.NodeTypeProviders;

namespace SMS.Models
{
    public class Album : Entity
    {

        public bool StandAlone { get; set; }

        public string Title { get; set; }

        [NotMapped]
        public List<Image> Images { get; set; } = new List<Image>();

        public List<AlbumImage> AlbumImages { get; set; } = new List<AlbumImage>();

        private void FixImages(SmsDbContext context)
        {
            AlbumImages = context.Set<AlbumImage>().Where(c => c.AlbumId == Id).ToList();
            AlbumImages.AddRange(Images.Where(i => AlbumImages.All(c => c.ImageId != i.Id)).Select(i => new AlbumImage() { ImageId = i.Id, Album = this, AlbumId = Id = Id, Image = i}));
            AlbumImages.Where(aI => Images.All(i => aI.ImageId != i.Id)).ToList().ForEach(aI => context.Remove(aI));
        }

        public override bool RemoveFromContext(SmsDbContext context)
        {
            if (!base.RemoveFromContext(context))
                return false;
            FixImages(context);
            context.Set<AlbumImage>().Where(aI => aI.AlbumId == Id).ToList().ForEach(aI => aI.RemoveFromContext(context));
            return true;
        }

        public override bool AddOrUpdate(SmsDbContext context)
        {
            if (!base.AddOrUpdate(context))
                return false;
            FixImages(context);
            AlbumImages.ForEach(aI => aI.AddOrUpdate(context));
            return true;
        }
    }
}