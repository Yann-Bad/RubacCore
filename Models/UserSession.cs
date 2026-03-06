namespace RubacCore.Models;

/// <summary>
/// Represents one connected browser tab / client session tracked by the SignalR hub.
/// Held in-memory — no database persistence.
/// </summary>
public record UserSession(
    string          ConnectionId,
    string          UserId,
    string          UserName,
    string          Application,
    DateTimeOffset  ConnectedAt,
    DateTimeOffset  LastSeenAt
);
