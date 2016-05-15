using Microsoft.Data.Entity;
using Microsoft.Data.Entity.ChangeTracking;
using Microsoft.Data.Entity.Infrastructure;
using Microsoft.Data.Entity.Metadata;

namespace SMS.Models
{
    public class SmsDbContext : DbContext
    {
        public SmsDbContext(DbContextOptions options) : base(options)
        {
            //Database.EnsureCreated();
            // Database.Migrate();
        }

        public DbSet<Person> Persons { get; set; }

        public DbSet<Harbour> Harbours { get; set; }

        public DbSet<Waypoint> Waypoints { get; set; }

        public DbSet<Tack> Tacks { get; set; }

        public DbSet<Trip> Trips { get; set; }

        public DbSet<Job> Job { get; set; }
        public DbSet<Album> Albums { get; set; }

        public DbSet<WaypointConnection> WaypointConnections { get; set; }
        public DbSet<Location> Locations { get; set; }

        public DbSet<Address> Addresses { get; set; }
        public DbSet<Image> Images { get; set; }
        public DbSet<Comment> Comments { get; set; }


        public void AddEntity(Entity entity)
        {
            Add(entity);
            if (entity.Album != null)
                AddAlbum(entity.Album);
            foreach (var comment in Comments)
            {
                AddComment(comment);
            }
        }


        public void AddComment(Comment comment)
        {
            AddEntity(comment);
        }

        public void AddAlbum(Album album)
        {
            AddEntity(album);
            album.Images?.ForEach(AddImage);
        }

        public void AddImage(Image image)
        {
            AddEntity(image);
        }

        public void AddWaypoint(Waypoint waypoint)
        {
            AddEntity(waypoint);
        }

        public void AddHarbour(Harbour harbour)
        {
            AddWaypoint(harbour);
            harbour.Locations?.ForEach(AddLocation);
        }

        public void AddLocation(Location location)
        {
            AddEntity(location);
            if (location.Address != null)
                AddAddress(location.Address);
        }

        public void AddAddress(Address address)
        {
            AddEntity(address);
        }

        public override EntityEntry<TEntity> Add<TEntity>(TEntity entity,
            GraphBehavior behavior = GraphBehavior.IncludeDependents)
        {
            return base.Add(entity, behavior);
        }

        public void Add(Person person)
        {
            Persons.Add(person);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            //modelBuilder.Entity<Entity>()
            //    .HasDiscriminator<string>("__type");
            modelBuilder.Entity<Person>();
            modelBuilder.Entity<Album>()
                .HasMany(a => a.AlbumImages)
                .WithOne(aI => aI.Album)
                .HasForeignKey(i => i.AlbumId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<AlbumImage>()
                .HasOne(aI => aI.Image)
                .WithMany()
                .HasForeignKey(aI => aI.ImageId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<AlbumImage>().HasKey(aI => new {aI.ImageId, aI.AlbumId});
            modelBuilder.Entity<Entity>().HasOne(e => e.Album).WithOne().OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Entity>()
                .HasMany(e => e.Comments)
                .WithOne()
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<WaypointConnection>()
                .HasOne(w => w.Waypoint1)
                .WithMany()
                .HasForeignKey(w => w.Waypoint1Id)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<WaypointConnection>()
                .HasOne(w => w.Waypoint2)
                .WithMany()
                .HasForeignKey(w => w.Waypoint2Id)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<WaypointConnection>().HasKey(w =>
                new
                {
                    w.Waypoint1Id,
                    w.Waypoint2Id
                });
            modelBuilder.Entity<Waypoint>()
                .HasDiscriminator<string>("__Discriminator")
                .HasValue<Waypoint>("Waypoint")
                .HasValue<Harbour>("Harbour");
            modelBuilder.Entity<TackBase>()
                .HasDiscriminator()
                .HasValue<Tack>("Tack")
                .HasValue<Trip>("Trip")
                .HasValue<LogBookEntry>(nameof(LogBookEntry));
            modelBuilder.Entity<TackBase>()
                .HasOne(t => t.End)
                .WithMany()
                .HasForeignKey(t => t.EndId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<TackBase>()
                .HasOne(t => t.Start)
                .WithMany()
                .HasForeignKey(t => t.StartId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<TackBase>().HasMany(t => t.Crew).WithOne(c => c.Tack).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Trip>()
                .HasMany(t => t.Tacks)
                .WithOne()
                .HasForeignKey(t => t.TripId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Crew>().HasKey(c => new {c.PersonId, c.TackId});
            modelBuilder.Entity<Crew>()
                .HasOne(c => c.Tack)
                .WithMany(t => t.Crew)
                .HasForeignKey(c => c.TackId)
                .OnDelete(DeleteBehavior.Restrict);
            //modelBuilder.Entity<Crew>().HasOne(c=>c.Person).WithMany().HasForeignKey(c=>c.PersonId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Job>().HasMany(j => j.SubJobs)
                .WithOne()
                .HasForeignKey(j => j.SuperJobId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}