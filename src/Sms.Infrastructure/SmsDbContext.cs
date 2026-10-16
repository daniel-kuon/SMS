using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sms.Domain;

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

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        base.ConfigureConventions(b);
        // All enums as strings (readable, stable across renumbering).
        foreach (var enumType in typeof(Visibility).Assembly.GetTypes().Where(t => t.IsEnum))
        {
            b.Properties(enumType).HaveConversion(typeof(EnumToStringConverter<>).MakeGenericType(enumType)).HaveMaxLength(32);
        }
        b.Properties<decimal>().HavePrecision(18, 4);
        b.Properties<string>().HaveMaxLength(4000);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        base.OnModelCreating(m);
        m.HasPostgresExtension("postgis");
        m.HasPostgresExtension("pg_trgm");

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
            e.HasIndex(l => new { l.TripId, l.Order });
        });

        m.Entity<LogbookEntry>(e =>
        {
            e.HasIndex(l => new { l.ShipId, l.DepartedAt });
            e.HasIndex(l => l.TripId);
            e.HasOne(l => l.StartHarbour).WithMany().HasForeignKey(l => l.StartHarbourId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.EndHarbour).WithMany().HasForeignKey(l => l.EndHarbourId).OnDelete(DeleteBehavior.Restrict);
            e.Property(l => l.CalculatedRoute).HasColumnType("geography (linestring, 4326)");
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
        });
        m.Entity<HarbourWifi>(e => e.HasOne<Harbour>().WithMany(h => h.Wifis).HasForeignKey(w => w.HarbourId).OnDelete(DeleteBehavior.Cascade));
        m.Entity<HarbourReview>(e => e.HasOne<Harbour>().WithMany(h => h.Reviews).HasForeignKey(r => r.HarbourId).OnDelete(DeleteBehavior.Cascade));
        m.Entity<HarbourRevision>(e => e.HasIndex(r => r.HarbourId));

        m.Entity<TaskItem>(e =>
        {
            e.HasIndex(t => t.TripId);
            e.HasIndex(t => t.ShipId);
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
