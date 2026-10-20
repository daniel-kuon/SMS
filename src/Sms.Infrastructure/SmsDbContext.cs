using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sms.Domain;
using Sms.Domain.Cms;
using Sms.Domain.Social;
using Sms.Domain.Tracking;

namespace Sms.Infrastructure;

public class SmsDbContext(DbContextOptions<SmsDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<UserProfile> Profiles => Set<UserProfile>();
    public DbSet<Ship> Ships => Set<Ship>();
    public DbSet<ShipMember> ShipMembers => Set<ShipMember>();
    public DbSet<ShipInvitation> ShipInvitations => Set<ShipInvitation>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<CrewMember> CrewMembers => Set<CrewMember>();
    public DbSet<TripInvitation> TripInvitations => Set<TripInvitation>();
    public DbSet<PlannedLeg> PlannedLegs => Set<PlannedLeg>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<LogbookEntry> LogbookEntries => Set<LogbookEntry>();
    public DbSet<LogbookEntryCrew> LogbookEntryCrew => Set<LogbookEntryCrew>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Media> Media => Set<Media>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseShare> ExpenseShares => Set<ExpenseShare>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<Harbour> Harbours => Set<Harbour>();
    public DbSet<HarbourRevision> HarbourRevisions => Set<HarbourRevision>();
    public DbSet<HarbourWifi> HarbourWifis => Set<HarbourWifi>();
    public DbSet<HarbourReview> HarbourReviews => Set<HarbourReview>();
    public DbSet<ContentPage> ContentPages => Set<ContentPage>();
    public DbSet<RoutingCorrection> RoutingCorrections => Set<RoutingCorrection>();
    public DbSet<FeedbackGroup> FeedbackGroups => Set<FeedbackGroup>();
    public DbSet<FeedbackItem> FeedbackItems => Set<FeedbackItem>();
    public DbSet<ShipAisConfig> ShipAisConfigs => Set<ShipAisConfig>();
    public DbSet<WeatherObservation> WeatherObservations => Set<WeatherObservation>();
    public DbSet<RouteCache> RouteCaches => Set<RouteCache>();
    public DbSet<Seamark> Seamarks => Set<Seamark>();
    public DbSet<Poi> Pois => Set<Poi>();
    public DbSet<ShipPage> ShipPages => Set<ShipPage>();
    public DbSet<ShipDomain> ShipDomains => Set<ShipDomain>();
    public DbSet<TrackingDevice> TrackingDevices => Set<TrackingDevice>();
    public DbSet<TrackPoint> TrackPoints => Set<TrackPoint>();
    public DbSet<TrackImport> TrackImports => Set<TrackImport>();
    public DbSet<TrackSegment> TrackSegments => Set<TrackSegment>();
    public DbSet<PrivacyZone> PrivacyZones => Set<PrivacyZone>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        base.ConfigureConventions(b);
        // All enums as strings (readable, stable across renumbering).
        foreach (var enumType in typeof(Visibility).Assembly.GetTypes().Where(t => t.IsEnum))
        {
            b.Properties(enumType).HaveConversion(typeof(EnumToStringConverter<>).MakeGenericType(enumType)).HaveMaxLength(32);
        }
        // Npgsql only writes UTC to timestamptz: an API client sending "2016-07-01T10:00:00+02:00" made SaveChanges throw
        b.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        b.Properties<decimal>().HavePrecision(18, 4);
        b.Properties<string>().HaveMaxLength(4000);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        base.OnModelCreating(m);
        m.HasPostgresExtension("postgis");
        m.HasPostgresExtension("pg_trgm");
        m.HasPostgresExtension("pgrouting");

        m.Entity<UserProfile>(e =>
        {
            e.HasKey(p => p.UserId);
            e.Property(p => p.DisplayName).HasMaxLength(100);
            e.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<Ship>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(200);
            e.HasIndex(s => s.Name);
            e.HasOne(s => s.HomeHarbour).WithMany().HasForeignKey(s => s.HomeHarbourId).OnDelete(DeleteBehavior.SetNull);
        });
        m.Entity<ShipMember>(e =>
        {
            e.HasKey(x => new { x.ShipId, x.UserId });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<ShipInvitation>(e => e.HasIndex(x => x.Token).IsUnique());

        m.Entity<Trip>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(200);
            e.Property(t => t.Currency).HasMaxLength(3);
            e.Property(t => t.TimeZone).HasMaxLength(64);
            e.HasIndex(t => new { t.ShipId, t.StartDate });
        });
        m.Entity<CrewMember>(e =>
        {
            e.HasIndex(c => new { c.TripId, c.UserId });
            e.Property(c => c.GuestName).HasMaxLength(200);
        });
        m.Entity<TripInvitation>(e => e.HasIndex(x => x.Token).IsUnique());
        m.Entity<PlannedLeg>(e =>
        {
            e.Property(l => l.RouteGeometry).HasColumnType("geography (linestring, 4326)");
            e.HasIndex(l => new { l.TripId, l.Order, l.Alternative });
        });

        m.Entity<LogbookEntry>(e =>
        {
            e.Property(p => p.Notes).HasMaxLength(100_000); // rich text (HTML); the old portal's notes ran past 4000 characters
            e.HasIndex(l => new { l.ShipId, l.DepartedAt });
            e.HasIndex(l => l.TripId);
            e.HasOne(l => l.StartHarbour).WithMany().HasForeignKey(l => l.StartHarbourId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.EndHarbour).WithMany().HasForeignKey(l => l.EndHarbourId).OnDelete(DeleteBehavior.Restrict);
            e.Property(l => l.CalculatedRoute).HasColumnType("geography (linestring, 4326)");
            e.Property(l => l.RouteGeometry).HasColumnType("geography (linestring, 4326)");
            e.HasQueryFilter(l => l.DeletedAt == null);
        });
        m.Entity<LogbookEntryCrew>(e =>
        {
            e.HasIndex(c => c.LogbookEntryId);
            e.HasOne<LogbookEntry>().WithMany(l => l.Crew).HasForeignKey(c => c.LogbookEntryId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<Post>(e =>
        {
            e.HasIndex(p => new { p.TripId, p.Day });
            e.Property(p => p.Content).HasMaxLength(100_000);
            e.Property(p => p.Location).HasColumnType("geography (point, 4326)");
            e.HasQueryFilter(p => p.DeletedAt == null);
        });
        m.Entity<Media>(e =>
        {
            e.HasIndex(x => new { x.TripId, x.Day });
            e.HasIndex(x => x.PostId);
            e.Property(x => x.Location).HasColumnType("geography (point, 4326)");
            e.HasOne<Post>().WithMany(p => p.Media).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        m.Entity<Expense>(e =>
        {
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.ExchangeRate).HasPrecision(18, 8);
            e.Property(x => x.Category).HasMaxLength(50);
            e.HasIndex(x => new { x.TripId, x.PaidOn });
            e.HasQueryFilter(x => x.DeletedAt == null);
        });
        m.Entity<ExpenseShare>(e =>
        {
            e.HasKey(s => new { s.ExpenseId, s.CrewMemberId });
            e.HasOne<Expense>().WithMany(x => x.Shares).HasForeignKey(s => s.ExpenseId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<ExchangeRate>(e =>
        {
            e.HasKey(r => new { r.Date, r.Currency, r.Source });
            e.Property(r => r.Currency).HasMaxLength(3);
            e.Property(r => r.PerEur).HasPrecision(18, 8);
        });
        m.Entity<Settlement>(e => e.HasIndex(s => s.TripId));

        m.Entity<Harbour>(e =>
        {
            e.Property(h => h.Name).HasMaxLength(200);
            e.Property(h => h.Location).HasColumnType("geography (point, 4326)");
            e.HasIndex(h => h.Location).HasMethod("gist");
            e.HasIndex(h => new { h.OsmType, h.OsmId });
            e.Property(h => h.Description).HasMaxLength(100_000);
            e.Property(h => h.Services).HasColumnType("jsonb");
        });
        m.Entity<HarbourWifi>(e => e.HasOne<Harbour>().WithMany(h => h.Wifis).HasForeignKey(w => w.HarbourId).OnDelete(DeleteBehavior.Cascade));
        m.Entity<HarbourReview>(e => e.HasOne<Harbour>().WithMany(h => h.Reviews).HasForeignKey(r => r.HarbourId).OnDelete(DeleteBehavior.Cascade));
        m.Entity<HarbourRevision>(e => e.HasIndex(r => r.HarbourId));

        m.Entity<TaskItem>(e =>
        {
            e.HasIndex(t => t.TripId);
            e.HasIndex(t => t.ShipId);
        });
        m.Entity<Poi>(e => { e.Property(p => p.Location).HasColumnType("geometry(Point,3857)"); e.HasIndex(p => p.Location).HasMethod("gist"); e.HasIndex(p => new { p.OsmType, p.OsmId }).IsUnique(); e.Property(p => p.TagsJson).HasColumnType("jsonb"); e.Property(p => p.Class).HasMaxLength(30); e.Property(p => p.Kind).HasMaxLength(200); });
        m.Entity<Seamark>(e =>
        {
            e.Property(s => s.Location).HasColumnType("geometry(Point,3857)");
            e.Property(s => s.Line).HasColumnType("geometry(LineString,3857)"); e.HasIndex(s => s.Line).HasMethod("gist"); e.Property(s => s.Sectors).HasMaxLength(4000);
            e.HasIndex(s => s.Location).HasMethod("gist");
            e.HasIndex(s => new { s.OsmType, s.OsmId }).IsUnique();
            e.HasIndex(s => s.Type);
            e.Property(s => s.TagsJson).HasColumnType("jsonb");
        });
        m.Entity<RoutingCorrection>(e => e.Property(c => c.Geometry).HasColumnType("geometry(Geometry,4326)"));
        m.Entity<FeedbackGroup>(e => { e.Property(g => g.Name).HasMaxLength(200); e.HasMany(g => g.Items).WithOne(i => i.Group).HasForeignKey(i => i.GroupId).OnDelete(DeleteBehavior.Cascade); });
        m.Entity<FeedbackItem>(e => { e.Property(i => i.Geometry).HasColumnType("geometry(Geometry,4326)"); e.Property(i => i.ObjectSnapshot).HasColumnType("jsonb"); e.Property(i => i.ObjectRef).HasMaxLength(100); });
        m.Entity<WeatherObservation>(e => { e.Property(o => o.Location).HasColumnType("geography (point, 4326)"); e.HasIndex(o => new { o.ShipId, o.Time }); e.HasIndex(o => new { o.TripId, o.Time }); e.Property(o => o.Provider).HasMaxLength(40); });
        m.Entity<ShipAisConfig>(e => { e.HasKey(c => c.ShipId); e.HasOne<Ship>().WithOne().HasForeignKey<ShipAisConfig>(c => c.ShipId).OnDelete(DeleteBehavior.Cascade); e.Property(c => c.Provider).HasMaxLength(40); e.Property(c => c.Mmsi).HasMaxLength(20); });
        m.Entity<RouteCache>(e =>
        {
            e.HasKey(r => new { r.FromHarbourId, r.ToHarbourId, r.GraphVersion });
            e.Property(r => r.Geometry).HasColumnType("geometry(LineString,4326)");
        });
        m.Entity<TrackingDevice>(e => { e.HasIndex(d => d.TokenHash).IsUnique(); e.HasIndex(d => d.ShipId); });
        m.Entity<TrackPoint>(e =>
        {
            e.Property(p => p.Location).HasColumnType("geography (point, 4326)");
            e.HasIndex(p => new { p.ShipId, p.Time });
            e.HasIndex(p => new { p.TripId, p.Time });
            e.HasIndex(p => new { p.DeviceId, p.Time });
        });
        m.Entity<TrackSegment>(e =>
        {
            e.Property(s => s.Geometry).HasColumnType("geography (linestring, 4326)");
            e.Property(s => s.GeometryCoarse).HasColumnType("geography (linestring, 4326)");
            e.HasIndex(s => new { s.TripId, s.Day });
        });
        m.Entity<PrivacyZone>(e => { e.Property(z => z.Center).HasColumnType("geography (point, 4326)"); e.HasIndex(z => z.ShipId); });
        m.Entity<Comment>(e => e.HasIndex(c => new { c.TargetType, c.TargetId }));
        m.Entity<Reaction>(e => e.HasKey(r => new { r.TargetType, r.TargetId, r.UserId }));
        m.Entity<Follow>(e => { e.HasKey(f => new { f.FollowerId, f.TargetType, f.TargetId }); e.HasIndex(f => new { f.TargetType, f.TargetId }); });
        m.Entity<Notification>(e => e.HasIndex(n => new { n.UserId, n.ReadAt }));
        m.Entity<Report>(e => e.HasIndex(r => r.Status));
        m.Entity<UserBlock>(e => e.HasKey(b => b.UserId));
        m.Entity<ShipPage>(e =>
        {
            e.Property(p => p.Slug).HasMaxLength(80);
            e.Property(p => p.Content).HasMaxLength(200_000);
            e.HasIndex(p => new { p.ShipId, p.ParentId, p.Slug }).IsUnique();
            e.HasOne<Ship>().WithMany().HasForeignKey(p => p.ShipId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<ShipDomain>(e =>
        {
            e.Property(d => d.Host).HasMaxLength(253);
            e.HasIndex(d => d.Host).IsUnique();
            e.HasOne<Ship>().WithMany().HasForeignKey(d => d.ShipId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<ContentPage>(e =>
        {
            e.HasIndex(p => new { p.Slug, p.Language }).IsUnique();
            e.Property(p => p.Content).HasMaxLength(200_000);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Modified) entry.Entity.UpdatedAt = now;
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = entry.Entity.UpdatedAt = now;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Lets `dotnet ef migrations add` run without the API host (connection string is irrelevant at design time).</summary>
public class SmsDbContextFactory : IDesignTimeDbContextFactory<SmsDbContext>
{
    public SmsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SmsDbContext>()
            .UseNpgsql("Host=localhost;Database=sms;Username=postgres;Password=postgres", o => o.UseNetTopologySuite())
            .Options;
        return new SmsDbContext(options);
    }
}

/// <summary>Stores every DateTimeOffset as UTC (timestamptz accepts only offset 0 in Npgsql).</summary>
public sealed class UtcDateTimeOffsetConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v);
