using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RubacCore.Data;
using RubacCore.Dtos;
using RubacCore.Interfaces;
using RubacCore.Models;

namespace RubacCore.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser>   _userManager;
    private readonly SignInManager<ApplicationUser>  _signInManager;
    private readonly ILdapService                   _ldapService;
    private readonly LdapSettings                   _ldapSettings;
    private readonly RubacDbContext                 _db;
    private readonly ICentreService                 _centreService;

    public AuthService(
        UserManager<ApplicationUser>   userManager,
        SignInManager<ApplicationUser>  signInManager,
        ILdapService                   ldapService,
        IOptions<LdapSettings>         ldapOptions,
        RubacDbContext                 db,
        ICentreService                 centreService)
    {
        _userManager    = userManager;
        _signInManager  = signInManager;
        _ldapService    = ldapService;
        _ldapSettings   = ldapOptions.Value;
        _db             = db;
        _centreService  = centreService;
    }

    public async Task<bool> ValidateCredentialsAsync(string userName, string password)
    {
        // ── LDAP path: username ends with @<configured-domain> ─────────────
        if (IsLdapUser(userName))
        {
            var ldapInfo = await _ldapService.AuthenticateAsync(userName, password);
            if (ldapInfo is null) return false;

            // Shadow user is provisioned later in GetUserForClientAsync once
            // we have the clientId. Store ldapInfo temporarily via a second call.
            await EnsureLdapUserAsync(userName, ldapInfo);
            return true;
        }

        // ── Local Identity path (unchanged) ───────────────────────────────
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null || !user.IsActive) return false;

        var result = await _signInManager.CheckPasswordSignInAsync(
            user, password, lockoutOnFailure: true);
        return result.Succeeded;
    }

    public async Task<IEnumerable<string>> GetUserRolesAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        return user is null ? [] : await _userManager.GetRolesAsync(user);
    }

    public async Task<(string? Primary, IEnumerable<string> All)> GetUserCentresAsync(long userId) =>
        await _centreService.GetUserCentresAsync(userId);

    public async Task<IEnumerable<string>> GetRolesForClientAsync(long userId, string clientId)
    {
        // Join UserRoles → Roles and keep only:
        //   - global roles  (Application == null)  — apply to every app (e.g. SuperAdmin)
        //   - scoped roles  (Application == clientId) — specific to the requesting client
        return await _db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r)
            .Where(r => r.Application == null || r.Application == clientId)
            .Select(r => r.Name!)
            .ToListAsync();
    }

    public async Task<UserDto?> GetUserByNameAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null) return null;

        var roles = await _userManager.GetRolesAsync(user);
        return new UserDto(user.Id, user.UserName!, user.Email!,
                           user.FirstName, user.LastName, user.IsActive, roles);
    }

    public async Task<UserDto?> GetUserByIdAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return null;

        var roles = await _userManager.GetRolesAsync(user);
        return new UserDto(user.Id, user.UserName!, user.Email!,
                           user.FirstName, user.LastName, user.IsActive, roles);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private bool IsLdapUser(string userName) =>
        _ldapSettings.Enabled &&
        !string.IsNullOrEmpty(_ldapSettings.Domain) &&
        userName.EndsWith($"@{_ldapSettings.Domain}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a shadow <see cref="ApplicationUser"/> for an AD user on their first login,
    /// or updates the DN if the account was moved in the directory tree.
    /// </summary>
    private async Task EnsureLdapUserAsync(string upn, LdapUserInfo ldapInfo,
        string? clientId = null)
    {
        var existing = await _userManager.FindByNameAsync(upn);
        if (existing is not null)
        {
            // Sync DN in case the account was moved in AD
            if (existing.LdapDn != ldapInfo.DistinguishedName)
            {
                existing.LdapDn = ldapInfo.DistinguishedName;
                await _userManager.UpdateAsync(existing);
            }
            return;
        }

        // First login — provision shadow user without a password hash
        var newUser = new ApplicationUser
        {
            UserName       = upn,
            Email          = ldapInfo.Email ?? upn,
            EmailConfirmed = true,
            FirstName      = ldapInfo.GivenName ?? ldapInfo.DisplayName,
            LastName       = ldapInfo.Surname,
            IsActive       = true,
            LdapDn         = ldapInfo.DistinguishedName,
            AuthProvider   = "ldap",
            CreatedAt      = DateTimeOffset.UtcNow,
        };

        var result = await _userManager.CreateAsync(newUser);
        if (!result.Succeeded) return;

        // Auto-assign centre from AD physicalDeliveryOfficeName if provided
        if (!string.IsNullOrWhiteSpace(ldapInfo.OfficeName))
            await _centreService.TryAssignCentreByCodeAsync(newUser.Id, ldapInfo.OfficeName);

        if (string.IsNullOrEmpty(_ldapSettings.DefaultRole)) return;

        // Look up a role scoped to this client first; fall back to a global role.
        var roleName = _ldapSettings.DefaultRole;
        var scopedRole = clientId is not null
            ? await _db.Roles.FirstOrDefaultAsync(
                r => r.Name == roleName && r.Application == clientId)
            : null;
        var globalRole = scopedRole is null
            ? await _db.Roles.FirstOrDefaultAsync(
                r => r.Name == roleName && r.Application == null)
            : null;

        var roleToAssign = (scopedRole ?? globalRole)?.Name;
        if (roleToAssign is not null)
            await _userManager.AddToRoleAsync(newUser, roleToAssign);
    }

    /// <summary>
    /// Called during the LDAP path of ValidateCredentialsAsync. Passes the
    /// client_id so EnsureLdapUserAsync can scope the default role correctly.
    /// </summary>
    internal async Task ValidateLdapAndEnsureUserAsync(
        string userName, string password, string clientId, LdapUserInfo ldapInfo)
    {
        await EnsureLdapUserAsync(userName, ldapInfo, clientId);
    }
}
