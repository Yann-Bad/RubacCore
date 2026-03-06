using Microsoft.EntityFrameworkCore;
using RubacCore.Data;
using RubacCore.Dtos;
using RubacCore.Interfaces;
using RubacCore.Models;

namespace RubacCore.Services;

public class AuditService : IAuditService
{
    private readonly RubacDbContext _db;

    public AuditService(RubacDbContext db) => _db = db;

    public async Task LogAsync(string actor, string entity, string action,
        string? targetId = null, string? details = null)
    {
        try
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Actor      = actor,
                Entity     = entity,
                Action     = action,
                TargetId   = targetId,
                Details    = details,
                OccurredAt = DateTimeOffset.UtcNow,
            });
            await _db.SaveChangesAsync();
        }
        catch
        {
            // Audit must never break the main operation — silently swallow.
        }
    }

    public async Task<PagedResult<AuditLogDto>> GetPagedAsync(
        int page, int pageSize,
        string? actor  = null,
        string? entity = null,
        string? action = null,
        string? search = null)
    {
        var query = _db.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(actor))
            query = query.Where(a => a.Actor.ToLower().Contains(actor.ToLower()));

        if (!string.IsNullOrWhiteSpace(entity))
            query = query.Where(a => a.Entity == entity);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(a =>
                a.Actor.ToLower().Contains(s)    ||
                a.Action.ToLower().Contains(s)   ||
                (a.Details != null && a.Details.ToLower().Contains(s)) ||
                (a.TargetId != null && a.TargetId.ToLower().Contains(s)));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto(a.Id, a.OccurredAt, a.Actor, a.Entity, a.Action, a.TargetId, a.Details))
            .ToListAsync();

        return new PagedResult<AuditLogDto>(items, totalCount, page, pageSize);
    }
}
