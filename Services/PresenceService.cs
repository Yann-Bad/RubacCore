using System.Collections.Concurrent;
using RubacCore.Interfaces;
using RubacCore.Models;

namespace RubacCore.Services;

/// <summary>
/// Singleton in-memory store of active SignalR sessions.
/// Thread-safe via ConcurrentDictionary, keyed by ConnectionId.
/// </summary>
public class PresenceService : IPresenceService
{
    private readonly ConcurrentDictionary<string, UserSession> _sessions = new();

    public void AddOrUpdate(UserSession session)
        => _sessions[session.ConnectionId] = session;

    public void Remove(string connectionId)
        => _sessions.TryRemove(connectionId, out _);

    public IReadOnlyCollection<UserSession> GetAll()
        => _sessions.Values.ToList().AsReadOnly();

    public UserSession? GetByConnectionId(string connectionId)
        => _sessions.TryGetValue(connectionId, out var s) ? s : null;
}
