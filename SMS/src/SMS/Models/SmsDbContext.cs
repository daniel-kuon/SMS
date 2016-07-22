
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SMS.Models
{
    public class SmsDbContext : DbContext
    {
        public SmsDbContext(DbContextOptions options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Waypoint>();
            modelBuilder.Entity<Harbour>();
            modelBuilder.Entity<Person>();
            modelBuilder.Entity<Album>();
            modelBuilder.Entity<AlbumImage>().HasKey(aI => new { aI.AlbumId, aI.ImageId });
            modelBuilder.Entity<WaypointConnection>().HasKey(w =>
                new
                {
                    w.Waypoint1Id,
                    w.Waypoint2Id
                });
            modelBuilder.Entity<TackBase>();
            modelBuilder.Entity<Tack>();
            modelBuilder.Entity<Trip>();
            modelBuilder.Entity<Crew>().HasKey(c => new { c.PersonId, c.TackId });
            modelBuilder.Entity<ContentPage>();
            modelBuilder.Entity<Comment>();
            modelBuilder.Entity<Location>();
            modelBuilder.Entity<LogBookEntry>();

            //base.OnModelCreating(modelBuilder);
            ////modelBuilder.Entity<Entity>()
            ////    .HasDiscriminator<string>("__type");
            //modelBuilder.Entity<Person>();
            //modelBuilder.Entity<Album>()
            //    .HasMany(a => a.AlbumImages)
            //    .WithOne(aI => aI.Album)
            //    .HasForeignKey(i => i.AlbumId)
            //    .OnDelete(DeleteBehavior.Cascade);
            //modelBuilder.Entity<AlbumImage>()
            //    .HasOne(aI => aI.Image)
            //    .WithMany()
            //    .HasForeignKey(aI => aI.ImageId)
            //    .OnDelete(DeleteBehavior.Cascade);
            //modelBuilder.Entity<AlbumImage>().HasKey(aI => new { aI.ImageId, aI.AlbumId });
            //modelBuilder.Entity<Entity>().HasOne(e => e.Album).WithOne().OnDelete(DeleteBehavior.Cascade);
            //modelBuilder.Entity<Entity>()
            //    .HasMany(e => e.Comments)
            //    .WithOne()
            //    .HasForeignKey(c => c.ParentId)
            //    .OnDelete(DeleteBehavior.Cascade);
            //modelBuilder.Entity<WaypointConnection>()
            //    .HasOne(w => w.Waypoint1)
            //    .WithMany()
            //    .HasForeignKey(w => w.Waypoint1Id)
            //    .OnDelete(DeleteBehavior.Restrict);
            //modelBuilder.Entity<WaypointConnection>()
            //    .HasOne(w => w.Waypoint2)
            //    .WithMany()
            //    .HasForeignKey(w => w.Waypoint2Id)
            //    .OnDelete(DeleteBehavior.Restrict);
            //modelBuilder.Entity<WaypointConnection>().HasKey(w =>
            //    new
            //    {
            //        w.Waypoint1Id,
            //        w.Waypoint2Id
            //    });
            //modelBuilder.Entity<Waypoint>()
            //    .HasDiscriminator<string>("__Discriminator")
            //    .HasValue<Waypoint>("Waypoint")
            //    .HasValue<Harbour>("Harbour");
            //modelBuilder.Entity<TackBase>()
            //    .HasDiscriminator()
            //    .HasValue<Tack>("Tack")
            //    .HasValue<Trip>("Trip")
            //    .HasValue<LogBookEntry>(nameof(LogBookEntry));
            //modelBuilder.Entity<TackBase>()
            //    .HasOne(t => t.End)
            //    .WithMany()
            //    .HasForeignKey(t => t.EndId)
            //    .OnDelete(DeleteBehavior.Restrict);
            //modelBuilder.Entity<TackBase>()
            //    .HasOne(t => t.Start)
            //    .WithMany()
            //    .HasForeignKey(t => t.StartId)
            //    .OnDelete(DeleteBehavior.Restrict);
            //modelBuilder.Entity<TackBase>().HasMany(t => t.Crew).WithOne(c => c.Tack).OnDelete(DeleteBehavior.Cascade);
            //modelBuilder.Entity<Trip>()
            //    .HasMany(t => t.Tacks)
            //    .WithOne()
            //    .HasForeignKey(t => t.TripId)
            //    .OnDelete(DeleteBehavior.Restrict);
            //modelBuilder.Entity<Crew>().HasKey(c => new { c.PersonId, c.TackId });
            //modelBuilder.Entity<Crew>()
            //    .HasOne(c => c.Tack)
            //    .WithMany(t => t.Crew)
            //    .HasForeignKey(c => c.TackId)
            //    .OnDelete(DeleteBehavior.Restrict);
            ////modelBuilder.Entity<Crew>().HasOne(c=>c.Person).WithMany().HasForeignKey(c=>c.PersonId).OnDelete(DeleteBehavior.Restrict);
            //modelBuilder.Entity<Job>().HasMany(j => j.SubJobs)
            //    .WithOne()
            //    .HasForeignKey(j => j.SuperJobId)
            //    .OnDelete(DeleteBehavior.Restrict);
            //modelBuilder.Entity<Harbour>()
            //    .HasMany(h => h.Wifis)
            //    .WithOne()
            //    .HasForeignKey(w => w.HarbourId);
            ////modelBuilder.Entity<ContentPage>();
        }
    }
}