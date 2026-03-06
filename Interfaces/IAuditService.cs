using RubacCore.Dtos;

namespace RubacCore.Interfaces;

public interface IAuditService
{
    /// <summary>Append an audit entry (fire-and-forget — does not throw).</summary>
    Task LogAsync(string actor, string entity, string action, string? targetId = null, string? details = null);

    Task<PagedResult<AuditLogDto>> GetPagedAsync(
        int page, int pageSize,
        string? actor  = null,
        string? entity = null,
        string? action = null,
        string? search = null);
}
