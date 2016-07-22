
using Microsoft.EntityFrameworkCore;

namespace SMS.Models
{
    public class AlbumImage : IEntityBase
    {
        public int? AlbumId { get; set; }
        public int? Index { get; set; }
        public Album Album { get; set; }
        public Image Image { get; set; }
        public int? ImageId { get; set; }


        public bool RemoveFromContext(SmsDbContext context)
        {
            if (context.Entry(this).State == EntityState.Deleted)
                return false;
            context.Remove(this);
            return true;
        }

        public bool AddOrUpdate(SmsDbContext context)
        {
            if (ImageId == null || AlbumId == null)
                context.Add(this);
            Image?.AddOrUpdate(context);
            return true;
        }
    }
}