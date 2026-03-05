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

    public UserRepository(UserManager<ApplicationUser> userManager)
        => _userManager = userManager;

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

    public async Task<PagedResult<UserDto>> GetPagedAsync(int page, int pageSize, string? search)
    {
        var query = _userManager.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(u =>
                (u.UserName != null && u.UserName.ToLower().Contains(s)) ||
                (u.Email    != null && u.Email.ToLower().Contains(s)));
        }

        var totalCount = await query.CountAsync();
        var users = await query
            .OrderBy(u => u.UserName)
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
        return result.Succeeded;
    }

    // ── Role assignment ────────────────────────────────────────────

    public async Task<bool> AssignRoleAsync(long userId, string roleName)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return false;

        var result = await _userManager.AddToRoleAsync(user, roleName);
        return result.Succeeded;
    }

    public async Task<bool> RemoveRoleAsync(long userId, string roleName)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return false;

        var result = await _userManager.RemoveFromRoleAsync(user, roleName);
        return result.Succeeded;
    }

    // ── Private helpers ────────────────────────────────────────────

    private static UserDto ToDto(ApplicationUser user, IEnumerable<string> roles)
        => new(user.Id, user.UserName!, user.Email!, user.FirstName,
               user.LastName, user.IsActive, roles);
}
