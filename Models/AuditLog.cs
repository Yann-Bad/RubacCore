namespace RubacCore.Models;

/// <summary>
/// Immutable audit trail record. Written once on every meaningful admin action,
/// never updated or deleted (append-only log).
/// </summary>
public class AuditLog
{
    public long   Id           { get; set; }

    /// <summary>UTC timestamp of the event.</summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Username (or system) that performed the action.</summary>
    public string Actor       { get; set; } = "system";

    /// <summary>Category of the action, e.g. User, Role.</summary>
    public string Entity      { get; set; } = string.Empty;

    /// <summary>
    /// Stable event identifier, e.g. user.created, user.deleted,
    /// role.assigned, password.reset.
    /// </summary>
    public string Action      { get; set; } = string.Empty;

    /// <summary>Identifier of the affected record (user id, role name, etc.).</summary>
    public string? TargetId   { get; set; }

    /// <summary>Human-readable description of what changed.</summary>
    public string? Details    { get; set; }
}
