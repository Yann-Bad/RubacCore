using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RubacCore.Interfaces;
using RubacCore.Models;

namespace RubacCore.Hubs;

[Authorize]
public class PresenceHub : Hub
{
    private readonly IPresenceService _presence;
    private readonly ILogger<PresenceHub> _logger;

    public PresenceHub(IPresenceService presence, ILogger<PresenceHub> logger)
    {
        _presence = presence;
        _logger   = logger;
    }

    // ── Connection lifecycle ─────────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        // SuperAdmins join the Admins group and receive the current snapshot
        if (Context.User?.IsInRole("SuperAdmin") == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
            await Clients.Caller.SendAsync("SnapshotReceived", _presence.GetAll());
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _presence.Remove(Context.ConnectionId);
        await Clients.Group("Admins").SendAsync("UserLeft", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // ── Client → Server methods ──────────────────────────────────────────────

    /// <summary>
    /// Called by any client immediately after connecting to announce its identity
    /// and the application it belongs to (e.g. "rubac-admin", "dashboard-spa").
    /// </summary>
    public async Task Announce(string application)
    {
        var userId   = Context.UserIdentifier ?? Context.ConnectionId;
        var userName = Context.User?.Identity?.Name ?? userId;

        var session = new UserSession(
            ConnectionId: Context.ConnectionId,
            UserId:       userId,
            UserName:     userName,
            Application:  application,
            ConnectedAt:  DateTimeOffset.UtcNow,
            LastSeenAt:   DateTimeOffset.UtcNow
        );

        _presence.AddOrUpdate(session);
        _logger.LogInformation("Presence: {User} joined from [{App}]", userName, application);

        await Clients.Group("Admins").SendAsync("UserJoined", session);
    }

    /// <summary>
    /// Heartbeat called every ~30 s by the client to keep the session fresh
    /// and update LastSeenAt. Admins receive the updated session.
    /// </summary>
    public async Task Heartbeat()
    {
        var existing = _presence.GetByConnectionId(Context.ConnectionId);
        if (existing is null) return;

        var updated = existing with { LastSeenAt = DateTimeOffset.UtcNow };
        _presence.AddOrUpdate(updated);
        await Clients.Group("Admins").SendAsync("UserUpdated", updated);
    }
}
