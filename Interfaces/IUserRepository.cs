using RubacCore.Dtos;

namespace RubacCore.Interfaces;

/// <summary>
/// Abstracts all user persistence operations away from ASP.NET Core Identity.
///
/// WHY an interface over UserManager directly?
///  Controllers should not depend on Identity internals—they express intent
///  ("create a user") and the repository translates it to Identity calls.
///  This makes it easy to:
///    • Unit-test controllers with a mock IUserRepository
///    • Swap Identity for another provider without touching controllers
///    • Add cross-cutting logic (audit log, caching) in one place
/// </summary>
public interface IUserRepository
{
    // ── Queries ─────────────────────────────────────────────────────────────────────
    Task<UserDto?> GetByIdAsync(long id);
    Task<IEnumerable<UserDto>> GetAllAsync();
    Task<PagedResult<UserDto>> GetPagedAsync(int page, int pageSize, string? search);

    // ── Commands ───────────────────────────────────────────────────────────────────
    Task<UserDto> CreateAsync(RegisterDto dto);

    /// <summary>Update name / email. Password changes go through ChangePasswordAsync.</summary>
    Task<UserDto?> UpdateAsync(long id, UpdateUserDto dto);

    /// <summary>
    /// Activate or deactivate a user without deleting their account.
    /// Deactivated users are rejected by AuthService.ValidateCredentialsAsync
    /// so their tokens stop working at next login without revoking existing ones.
    /// </summary>
    Task<bool> SetActiveAsync(long id, bool isActive);

    // ── Role assignment ───────────────────────────────────────────────────────────
    Task<bool> AssignRoleAsync(long userId, string roleName);
    Task<bool> RemoveRoleAsync(long userId, string roleName);
}
