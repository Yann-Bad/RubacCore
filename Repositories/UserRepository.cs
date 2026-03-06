using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RubacCore.Dtos;
using RubacCore.Interfaces;
using RubacCore.Models;

namespace RubacCore.Repositories;

/// <summary>
/// Wraps ASP.NET Core Identity's UserManager with application-level semantics.
///
/// WHY wrap UserManager instead of injecting it directly into controllers?
///  UserManager is a complex class with 80+ methods. Wrapping it means:
///    • Controllers express domain intent: "create a user", "deactivate a user"
///    • Identity plumbing (hashing, tokens, claims) stays here, invisible upstairs
///    • Mocking is trivial: mock IUserRepository, not UserManager<T>
///    • Cross-cutting concerns (logging, audit) added once here, not in every controller
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService                _audit;
    private readonly IHttpContextAccessor         _http;

    public UserRepository(
        UserManager<ApplicationUser> userManager,
        IAuditService audit,
        IHttpContextAccessor http)
    {
        _userManager = userManager;
        _audit       = audit;
        _http        = http;
    }

    private string Actor =>
        _http.HttpContext?.User.Identity?.Name ?? "system";

    // ── Queries ────────────────────────────────────────────────────

    public async Task<UserDto?> GetByIdAsync(long id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return null;

        var roles = await _userManager.GetRolesAsync(user);
        return ToDto(user, roles);
    }

    public async Task<IEnumerable<UserDto>> GetAllAsync()
    {
        var users  = await _userManager.Users.ToListAsync();
        var result = new List<UserDto>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            result.Add(ToDto(user, roles));
        }
        return result;
    }

    public async Task<PagedResult<UserDto>> GetPagedAsync(int page, int pageSize, string? search, string? sortBy = "userName", string? sortDir = "asc")
    {
        var query = _userManager.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(u =>
                (u.UserName != null && u.UserName.ToLower().Contains(s)) ||
                (u.Email    != null && u.Email.ToLower().Contains(s)));
        }

        bool desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = sortBy?.ToLower() switch
        {
            "email"     => desc ? query.OrderByDescending(u => u.Email)    : query.OrderBy(u => u.Email),
            "firstname" => desc ? query.OrderByDescending(u => u.FirstName) : query.OrderBy(u => u.FirstName),
            "lastname"  => desc ? query.OrderByDescending(u => u.LastName)  : query.OrderBy(u => u.LastName),
            _           => desc ? query.OrderByDescending(u => u.UserName)  : query.OrderBy(u => u.UserName),
        };

        var totalCount = await query.CountAsync();
        var users = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = new List<UserDto>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            items.Add(ToDto(user, roles));
        }

        return new PagedResult<UserDto>(items, totalCount, page, pageSize);
    }

    // ── Commands ───────────────────────────────────────────────────

    public async Task<UserDto> CreateAsync(RegisterDto dto)
    {
        var user = new ApplicationUser
        {
            UserName  = dto.UserName,
            Email     = dto.Email,
            FirstName = dto.FirstName,
            LastName  = dto.LastName
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                string.Join(", ", result.Errors.Select(e => e.Description)));

        await _audit.LogAsync(Actor, "User", "user.created",
            user.Id.ToString(), $"Username: {user.UserName}, Email: {user.Email}");

        return ToDto(user, []);
    }

    /// <summary>
    /// Updates profile fields (name, email) without touching password or roles.
    /// Email change triggers Identity's duplicate-email check automatically.
    /// </summary>
    public async Task<UserDto?> UpdateAsync(long id, UpdateUserDto dto)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return null;

        user.FirstName = dto.FirstName ?? user.FirstName;
        user.LastName  = dto.LastName  ?? user.LastName;

        if (dto.Email is not null && dto.Email != user.Email)
        {
            // SetEmailAsync also resets EmailConfirmed — intentional:
            // a changed e-mail should be re-confirmed in a production flow.
            var emailResult = await _userManager.SetEmailAsync(user, dto.Email);
            if (!emailResult.Succeeded)
                throw new InvalidOperationException(
                    string.Join(", ", emailResult.Errors.Select(e => e.Description)));
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                string.Join(", ", result.Errors.Select(e => e.Description)));

        await _audit.LogAsync(Actor, "User", "user.updated",
            id.ToString(), $"Username: {user.UserName}");

        var roles = await _userManager.GetRolesAsync(user);
        return ToDto(user, roles);
    }

    /// <summary>
    /// Soft-disable a user without deleting their data.
    ///
    /// WHY soft-disable instead of delete?
    ///  • Audit trails remain intact (who created what, when)
    ///  • Foreign keys in other tables don't break
    ///  • Re-activating is instant — no need to recreate credentials
    ///
    /// Effect: AuthService.ValidateCredentialsAsync returns false for inactive users,
    /// so they cannot obtain new tokens. Existing tokens remain valid until they expire
    /// (typically 1 hour). For immediate revocation, combine with token invalidation later.
    /// </summary>
    public async Task<bool> SetActiveAsync(long id, bool isActive)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return false;

        user.IsActive = isActive;
        var result = await _userManager.UpdateAsync(user);
        if (result.Succeeded)
            await _audit.LogAsync(Actor, "User", isActive ? "user.activated" : "user.deactivated",
                id.ToString(), $"Username: {user.UserName}");
        return result.Succeeded;
    }

    /// <summary>
    /// Force-reset a user's password without requiring their current password.
    /// Uses Identity's built-in token-based reset flow so all password validators
    /// (length, complexity) still run — the token is generated and redeemed immediately,
    /// no email round-trip is needed for an admin-driven reset.
    /// </summary>
    public async Task<bool> ResetPasswordAsync(long id, string newPassword)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return false;

        var token  = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                string.Join(", ", result.Errors.Select(e => e.Description)));

        await _audit.LogAsync(Actor, "User", "password.reset",
            id.ToString(), $"Username: {user.UserName}");
        return true;
    }

    /// <summary>
    /// Self-service password change: verifies the current password first,
    /// then updates to the new one. Uses ChangePasswordAsync which runs all
    /// Identity password validators (length, complexity).
    /// </summary>
    public async Task<bool> ChangePasswordAsync(long id, string currentPassword, string newPassword)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return false;

        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                string.Join(", ", result.Errors.Select(e => e.Description)));

        await _audit.LogAsync(Actor, "User", "password.changed",
            id.ToString(), $"Username: {user.UserName}");
        return true;
    }

    // ── Role assignment ────────────────────────────────────────────

    public async Task<bool> AssignRoleAsync(long userId, string roleName)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return false;

        var result = await _userManager.AddToRoleAsync(user, roleName);
        if (result.Succeeded)
            await _audit.LogAsync(Actor, "User", "role.assigned",
                userId.ToString(), $"Username: {user.UserName}, Role: {roleName}");
        return result.Succeeded;
    }

    public async Task<bool> RemoveRoleAsync(long userId, string roleName)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return false;

        var result = await _userManager.RemoveFromRoleAsync(user, roleName);
        if (result.Succeeded)
            await _audit.LogAsync(Actor, "User", "role.removed",
                userId.ToString(), $"Username: {user.UserName}, Role: {roleName}");
        return result.Succeeded;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return false;

        var result = await _userManager.DeleteAsync(user);
        if (result.Succeeded)
            await _audit.LogAsync(Actor, "User", "user.deleted",
                id.ToString(), $"Username: {user.UserName}");
        return result.Succeeded;
    }

    // ── Private helpers ────────────────────────────────────────────

    private static UserDto ToDto(ApplicationUser user, IEnumerable<string> roles)
        => new(user.Id, user.UserName!, user.Email!, user.FirstName,
               user.LastName, user.IsActive, roles);
}
