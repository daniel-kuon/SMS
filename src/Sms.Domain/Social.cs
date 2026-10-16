namespace Sms.Domain.Social;

public enum TargetType { Post, Media, LogbookEntry, Trip, HarbourReview, User, Ship }
public enum ReactionType { Like }
public enum NotificationType { TripInvitation, ShipInvitation, Comment, Reaction, Expense, Settlement, Follow, Mention, Report }
public enum ReportStatus { Open, Resolved, Dismissed }

/// <summary>Spec 08 §8.1 "Identity and social". Comments/reactions inherit the visibility of their target.</summary>
public class Comment : Entity
{
    public TargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
    public Guid AuthorId { get; set; }
    public string Text { get; set; } = "";
    public bool Hidden { get; set; }
}

public class Reaction
{
    public TargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
    public Guid UserId { get; set; }
    public ReactionType Type { get; set; } = ReactionType.Like;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Follow
{
    public Guid FollowerId { get; set; }
    /// <summary>User or Ship.</summary>
    public TargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Notification
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = "";
    public string? Link { get; set; }
    public string? PayloadJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
    public bool EmailSent { get; set; }
}

public class Report : Entity
{
    public TargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
    public Guid ReporterId { get; set; }
    public string Reason { get; set; } = "";
    public ReportStatus Status { get; set; } = ReportStatus.Open;
    public string? Resolution { get; set; }
}

/// <summary>Moderation state on users (spec 07 E5).</summary>
public class UserBlock
{
    public Guid UserId { get; set; }
    public Guid BlockedById { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
