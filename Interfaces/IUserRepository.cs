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
    Task<PagedResult<UserDto>> GetPagedAsync(int page, int pageSize, string? search, string? sortBy = "userName", string? sortDir = "asc");

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

    /// <summary>
    /// Force-reset a user's password (SuperAdmin action, no current-password required).
    /// Uses Identity's token-based reset flow internally so all password validators run.
    /// </summary>
    Task<bool> ResetPasswordAsync(long id, string newPassword);

    /// <summary>
    /// Self-service password change: validates the current password before updating.
    /// Throws InvalidOperationException on wrong current password or policy violation.
    /// </summary>
    Task<bool> ChangePasswordAsync(long id, string currentPassword, string newPassword);

    // ── Role assignment ───────────────────────────────────────────────────────────
    Task<bool> AssignRoleAsync(long userId, string roleName);
    Task<bool> RemoveRoleAsync(long userId, string roleName);

    /// <summary>Permanently remove a user and all their Identity data from the database.</summary>
    Task<bool> DeleteAsync(long id);
}
