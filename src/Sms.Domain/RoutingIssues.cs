using NetTopologySuite.Geometries;

namespace Sms.Domain;

/// <summary>Relevance order: the detection's types (spec-admin-issues).</summary>
public enum IssueType { UncertainWater, BridgeUnknown, FairwayGap, FairwayDeadEnd, FairwayIsolated, HarbourOffWater }
public enum IssueStatus { Fixed, Ignored }

/// <summary>One finding of the latest detection run (RoutingIssues.DetectAsync), replaced as a whole by the next run.</summary>
public class RoutingIssue
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Stable across rebuilds and re-imports while the thing is the same ("water:i:j", "bridge:…", "gap:…", "deadend:…", "harbour:&lt;id&gt;").</summary>
    public string Key { get; set; } = "";
    public IssueType Type { get; set; }
    public int GraphVersion { get; set; }
    public double Priority { get; set; }
    public string Title { get; set; } = "";
    /// <summary>The numbers behind the priority, German.</summary>
    public string? Detail { get; set; }
    /// <summary>Source row ids: the derived bridge correction, the fairway line(s), the harbour.</summary>
    public string? RefId { get; set; }
    public Geometry Geometry { get; set; } = null!;
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// The admin's verdict on an issue key: fixed (with the Manual correction it created) or "kein Problem". Imports and rebuilds never
/// touch it; RoutingIssues.CheckAsync marks it outdated when the data it was based on (fingerprint) changes.
/// </summary>
public class IssueDecision : Entity
{
    public string Key { get; set; } = "";
    public IssueType Type { get; set; }
    public IssueStatus Status { get; set; }
    public Guid? CorrectionId { get; set; }
    public Geometry Geometry { get; set; } = null!;
    public string Title { get; set; } = "";
    public string? Note { get; set; }
    /// <summary>depth | bridge | fairway | harbour.</summary>
    public string BasisKind { get; set; } = "";
    /// <summary>The source ids / tags the fingerprint covers ("ft: 12, kartverket: 3; ids …").</summary>
    public string? BasisRefs { get; set; }
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset BasedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CheckedAt { get; set; }
    public DateTimeOffset? OutdatedAt { get; set; }
    public string? OutdatedReason { get; set; }
}
