using NetTopologySuite.Geometries;

namespace Sms.Domain;

/// <summary>Common aggregate base (spec 08): Guid ids, UTC timestamps, audit fields, soft delete.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedById { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedById { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

// ---------------------------------------------------------------- identity / social

public class UserProfile
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public Guid? AvatarMediaId { get; set; }
    public string? Bio { get; set; }
    public Guid? HomeHarbourId { get; set; }
    public string? Experience { get; set; }
    public ProfileVisibility ProfileVisibility { get; set; } = ProfileVisibility.Public;
    public Visibility DefaultContentVisibility { get; set; } = Visibility.Crew;
    public string Language { get; set; } = "de";
    public bool EmailNotifications { get; set; } = true;
    public bool IsAdmin { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ---------------------------------------------------------------- ships

public class Ship : Entity
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public decimal? LengthM { get; set; }
    public decimal? BeamM { get; set; }
    public decimal? DraftM { get; set; }
    public decimal? AirDraftM { get; set; }
    public string? SailNumber { get; set; }
    public string? CallSign { get; set; }
    public string? Mmsi { get; set; }
    public Guid? HomeHarbourId { get; set; }
    public Harbour? HomeHarbour { get; set; }
    public decimal CruisingSpeedKn { get; set; } = 5;
    public Guid? PhotoMediaId { get; set; }
    public string? Description { get; set; }
    public Visibility Visibility { get; set; } = Visibility.Public;
    public List<ShipMember> Members { get; set; } = [];
    public List<Trip> Trips { get; set; } = [];
}

public class ShipMember
{
    public Guid ShipId { get; set; }
    public Ship Ship { get; set; } = null!;
    public Guid UserId { get; set; }
    public ShipRole Role { get; set; }
    public DateTimeOffset Since { get; set; } = DateTimeOffset.UtcNow;
}

public class ShipInvitation : Entity
{
    public Guid ShipId { get; set; }
    public Ship Ship { get; set; } = null!;
    public string Email { get; set; } = "";
    public ShipRole Role { get; set; }
    public string Token { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
}

// ---------------------------------------------------------------- trips

public class Trip : Entity
{
    public Guid ShipId { get; set; }
    public Ship Ship { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string TimeZone { get; set; } = "Europe/Berlin";
    public Guid? PlannedStartHarbourId { get; set; }
    public Guid? PlannedEndHarbourId { get; set; }
    /// <summary>Crew or Public (spec 08).</summary>
    public Visibility Visibility { get; set; } = Visibility.Crew;
    public TripStatus Status { get; set; } = TripStatus.Planned;
    public string Currency { get; set; } = "EUR";
    public DateTimeOffset? CostsLockedAt { get; set; }
    /// <summary>Spec 12 §12.5: track visibility may be lower than the trip's.</summary>
    public Visibility TrackVisibility { get; set; } = Visibility.Crew;
    public int PublicPositionDelayMinutes { get; set; } = 120;
    public List<CrewMember> Crew { get; set; } = [];
    public List<PlannedLeg> PlannedLegs { get; set; } = [];
}

public class CrewMember : Entity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    /// <summary>null = guest crew without account.</summary>
    public Guid? UserId { get; set; }
    public string? GuestName { get; set; }
    public CrewRole Role { get; set; } = CrewRole.Crew;
    /// <summary>"Logbuch schreiben" permission; always true for the skipper.</summary>
    public bool CanWriteLogbook { get; set; }
    public CrewStatus Status { get; set; } = CrewStatus.Invited;
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    public bool IsAboardOn(DateOnly day) =>
        Status == CrewStatus.Accepted && (FromDate is null || FromDate <= day) && (ToDate is null || ToDate >= day);
}

public class TripInvitation : Entity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    public string? Email { get; set; }
    public string Token { get; set; } = "";
    public CrewRole Role { get; set; } = CrewRole.Crew;
    public DateTimeOffset ExpiresAt { get; set; }
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
}

public class PlannedLeg : Entity
{
    public Guid TripId { get; set; }
    public int Order { get; set; }
    public Guid FromHarbourId { get; set; }
    public Guid ToHarbourId { get; set; }
    public DateOnly? PlannedDate { get; set; }
    public decimal? DistanceNm { get; set; }
    public LineString? RouteGeometry { get; set; }
}

public class TaskItem : Entity
{
    public TaskScope Scope { get; set; }
    public Guid? TripId { get; set; }
    public Guid? ShipId { get; set; }
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = "";
    public string? Content { get; set; }
    public DateOnly? DueDate { get; set; }
    public Guid? AssigneeCrewMemberId { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public bool Done { get; set; }
}

// ---------------------------------------------------------------- logbook

public class LogbookEntry : Entity
{
    public Guid ShipId { get; set; }
    public Ship Ship { get; set; } = null!;
    public Guid? TripId { get; set; }
    public Trip? Trip { get; set; }
    public Guid StartHarbourId { get; set; }
    public Harbour StartHarbour { get; set; } = null!;
    public Guid EndHarbourId { get; set; }
    public Harbour EndHarbour { get; set; } = null!;
    public DateTimeOffset DepartedAt { get; set; }
    public DateTimeOffset ArrivedAt { get; set; }
    public decimal? EngineHoursStart { get; set; }
    public decimal? EngineHoursEnd { get; set; }
    public decimal? LogStart { get; set; }
    public decimal? LogEnd { get; set; }
    public string? WindSpeed { get; set; }
    public WindDirection? WindDirection { get; set; }
    public string? Weather { get; set; }
    public string? SeaState { get; set; }
    public decimal? SailedDistanceNm { get; set; }
    public string? SailsNotes { get; set; }
    public string? Notes { get; set; }
    public LineString? CalculatedRoute { get; set; }
    public decimal? CalculatedDistanceNm { get; set; }
    public Visibility Visibility { get; set; } = Visibility.Crew;
    public Guid AuthorId { get; set; }
    public List<LogbookEntryCrew> Crew { get; set; } = [];

    public TimeSpan Duration => ArrivedAt - DepartedAt;
}

public class LogbookEntryCrew
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid LogbookEntryId { get; set; }
    public Guid? CrewMemberId { get; set; }
    public Guid? UserId { get; set; }
    public string? GuestName { get; set; }
}

// ---------------------------------------------------------------- content & media

public class Post : Entity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    public Guid AuthorId { get; set; }
    /// <summary>Trip-local calendar day that groups the post into the timeline.</summary>
    public DateOnly Day { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = "";
    public Guid? HarbourId { get; set; }
    public Point? Location { get; set; }
    public LocationSource? LocationSource { get; set; }
    public Visibility Visibility { get; set; } = Visibility.Crew;
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Media> Media { get; set; } = [];
}

public class Media : Entity
{
    public Guid OwnerId { get; set; }
    public Guid? TripId { get; set; }
    public Guid? ShipId { get; set; }
    public Guid? PostId { get; set; }
    public Guid? LogbookEntryId { get; set; }
    public Guid? HarbourId { get; set; }
    public MediaKind Kind { get; set; } = MediaKind.Photo;
    public string StorageKey { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public DateTimeOffset? TakenAt { get; set; }
    public DateOnly? Day { get; set; }
    public Point? Location { get; set; }
    public LocationSource? LocationSource { get; set; }
    public string? Caption { get; set; }
    public Visibility Visibility { get; set; } = Visibility.Crew;
    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Pending;
}

// ---------------------------------------------------------------- costs (spec 09)

public class Expense : Entity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    public Guid PaidByCrewMemberId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public DateOnly PaidOn { get; set; }
    /// <summary>Rate from <see cref="Currency"/> to the trip currency.</summary>
    public decimal ExchangeRate { get; set; } = 1;
    public DateOnly RateDate { get; set; }
    public RateSource RateSource { get; set; } = RateSource.Same;
    public decimal AmountInTripCurrency { get; set; }
    public string Category { get; set; } = "Sonstiges";
    public string? Description { get; set; }
    public Guid? ReceiptMediaId { get; set; }
    public SplitMode SplitMode { get; set; } = SplitMode.Equal;
    public List<ExpenseShare> Shares { get; set; } = [];
}

public class ExpenseShare
{
    public Guid ExpenseId { get; set; }
    public Guid CrewMemberId { get; set; }
    public decimal? Weight { get; set; }
    public decimal? ExactAmount { get; set; }
    /// <summary>Share in trip currency, computed server-side.</summary>
    public decimal ComputedShare { get; set; }
}

public class ExchangeRate
{
    public DateOnly Date { get; set; }
    public string Currency { get; set; } = "";
    /// <summary>Units of <see cref="Currency"/> per 1 EUR.</summary>
    public decimal PerEur { get; set; }
    public RateSource Source { get; set; } = RateSource.Ecb;
}

public class Settlement : Entity
{
    public Guid TripId { get; set; }
    public Guid FromCrewMemberId { get; set; }
    public Guid ToCrewMemberId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset PaidAt { get; set; } = DateTimeOffset.UtcNow;
    public SettlementStatus Status { get; set; } = SettlementStatus.Reported;
    public string? Note { get; set; }
}

// ---------------------------------------------------------------- harbours (spec 10)

public class Harbour : Entity
{
    public string Name { get; set; } = "";
    public Point Location { get; set; } = null!;
    public string? Country { get; set; }
    public string? Website { get; set; }
    public string? Description { get; set; }
    public string? OsmType { get; set; }
    public long? OsmId { get; set; }
    public HarbourSource Source { get; set; } = HarbourSource.User;
    public string? Category { get; set; }
    /// <summary>JSON object with facility flags (fuel, water, power, toilets, showers, ...).</summary>
    public string? Facilities { get; set; }
    public string? Phone { get; set; }
    public string? VhfChannel { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? MergedIntoId { get; set; }
    public List<HarbourWifi> Wifis { get; set; } = [];
    public List<HarbourReview> Reviews { get; set; } = [];
}

public class HarbourRevision
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid HarbourId { get; set; }
    public Guid? ChangedById { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
    public string SnapshotJson { get; set; } = "";
}

public class HarbourWifi : Entity
{
    public Guid HarbourId { get; set; }
    public string Name { get; set; } = "";
    public string? Password { get; set; }
    /// <summary>0..3 (legacy "speed" levels).</summary>
    public int Speed { get; set; }
    public bool Free { get; set; }
}

public class HarbourReview : Entity
{
    public Guid HarbourId { get; set; }
    public Guid AuthorId { get; set; }
    public int Rating { get; set; }
    public string? Text { get; set; }
    public DateOnly? VisitedOn { get; set; }
}

/// <summary>OpenSeaMap sea mark (OSM seamark:* tags) rendered by the tile server; Location in EPSG:3857.</summary>
public class Seamark
{
    public long Id { get; set; }
    public string OsmType { get; set; } = "node";
    public long OsmId { get; set; }
    public string Type { get; set; } = "";
    public string? Category { get; set; }
    public string? Name { get; set; }
    public string? Colour { get; set; }
    public string? Shape { get; set; }
    /// <summary>Light characteristic as charted, e.g. "Oc(2)WRG.9s16m13-9M".</summary>
    public string? Light { get; set; }
    /// <summary>Light sectors "colour:start:end:range;…" (degrees true from seaward; empty start/end = all round).</summary>
    public string? Sectors { get; set; }
    public string TagsJson { get; set; } = "{}";
    public Point Location { get; set; } = null!;
    /// <summary>Line marks (navigation lines, recommended tracks, cables, pipelines, fairway edges): the way's geometry, EPSG:3857.</summary>
    public LineString? Line { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Manual routing fixes (spec 10): blocked areas (polygon) or passages (line) applied at graph build.</summary>
public class RoutingCorrection : Entity
{
    public CorrectionKind Kind { get; set; }
    public CorrectionSource Source { get; set; } = CorrectionSource.Manual;
    public Geometry Geometry { get; set; } = null!;
    public string? Note { get; set; }
    public bool Active { get; set; } = true;
    /// <summary>Bridge: vertical clearance in metres (null = unknown, no restriction). Passages under it are closed for ships with a larger air draft.</summary>
    public double? ClearanceM { get; set; }
}

/// <summary>Shops, restaurants, services from OSM within a few km of a harbour (PBF import): rendered by Sms.Tiles, details (opening hours, website) on click.</summary>
public class Poi
{
    public long Id { get; set; }
    public string OsmType { get; set; } = "node";
    public long OsmId { get; set; }
    /// <summary>OSM key that classifies it: amenity, shop, tourism, leisure, craft, office, healthcare.</summary>
    public string Class { get; set; } = "";
    /// <summary>The key's value: restaurant, supermarket, toilets, …</summary>
    public string Kind { get; set; } = "";
    public string? Name { get; set; }
    public string TagsJson { get; set; } = "{}";
    public Point Location { get; set; } = null!;
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Weather at a received live position (one per ship per 15 min / 1 nm), from the configured IWeatherProvider.</summary>
public class WeatherObservation
{
    public long Id { get; set; }
    public Guid ShipId { get; set; }
    public Guid? TripId { get; set; }
    public DateTimeOffset Time { get; set; }
    public Point Location { get; set; } = null!;
    public string Provider { get; set; } = "";
    public double? TemperatureC { get; set; }
    public double? WindKn { get; set; }
    public double? WindDirDeg { get; set; }
    public double? GustKn { get; set; }
    public double? PressureHpa { get; set; }
    public double? PrecipitationMm { get; set; }
    public int? WeatherCode { get; set; }
    public int? CloudCover { get; set; }
    public double? WaveHeightM { get; set; }
    public double? WaveDirDeg { get; set; }
    public double? WavePeriodS { get; set; }
}

/// <summary>Spec 12 §12.1 C: AIS provider settings per ship; the API key is stored data-protection encrypted and never returned.</summary>
public class ShipAisConfig
{
    public Guid ShipId { get; set; }
    public string Provider { get; set; } = "AisStream";
    public string? ApiKeyEncrypted { get; set; }
    public string? Mmsi { get; set; }
    public int PollIntervalMinutes { get; set; } = 30;
    public bool Enabled { get; set; }
    public DateTimeOffset? LastPolledAt { get; set; }
    public string? LastError { get; set; }
    public int CallsThisMonth { get; set; }
    public string? CallsMonth { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedById { get; set; }
}

/// <summary>Cache of computed harbour-to-harbour sea routes per graph version (spec 10 §10.2).</summary>
public class RouteCache
{
    public Guid FromHarbourId { get; set; }
    public Guid ToHarbourId { get; set; }
    public int GraphVersion { get; set; }
    public decimal DistanceNm { get; set; }
    public LineString Geometry { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ---------------------------------------------------------------- platform

public class ContentPage : Entity
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Language { get; set; } = "de";
    public bool ShowInFooter { get; set; } = true;
    public Guid? ParentId { get; set; }
    public int Order { get; set; }
    public bool ShowInNav { get; set; }
}
