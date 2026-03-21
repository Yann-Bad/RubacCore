using RubacCore.Dtos;

namespace RubacCore.Interfaces;

/// <summary>
/// Defines write operations against Active Directory using LDAP.
///
/// All methods are async (Task.Run wraps the synchronous SDS.Protocols calls)
/// to avoid blocking ASP.NET Core thread-pool threads on I/O wait.
///
/// Prerequisites:
///   • The service account in Ldap:ServiceAccount must have
///     "Create/Delete Child Objects" and "Write All Properties"
///     on the target OU (Ldap:WriteOUPath).
///   • Password operations require LDAPS (Ldap:UseSsl = true, Port = 636).
/// </summary>
public interface ILdapWriteService
{
    /// <summary>
    /// Creates a new AD user account in the configured WriteOUPath.
    /// The account is enabled if a password is provided over LDAPS,
    /// otherwise it is created disabled until a password is set externally.
    /// </summary>
    Task<LdapWriteResult> CreateUserAsync(CreateAdUserDto dto);

    /// <summary>
    /// Updates mutable attributes of an existing AD account.
    /// Only non-null fields are written; others are left unchanged.
    /// </summary>
    Task<LdapWriteResult> UpdateUserAsync(string samAccountName, UpdateAdUserDto dto);

    /// <summary>
    /// Disables an AD account (sets userAccountControl |= ACCOUNTDISABLE).
    /// The account and its group memberships are preserved.
    /// Recommended soft step before permanent deletion.
    /// </summary>
    Task<LdapWriteResult> SuspendUserAsync(string samAccountName, string reason);

    /// <summary>
    /// Re-enables a previously suspended AD account.
    /// </summary>
    Task<LdapWriteResult> ReactivateUserAsync(string samAccountName);

    /// <summary>
    /// Permanently deletes an AD account.
    ///
    /// ⚠️  IRREVERSIBLE. The account must be suspended first
    ///     (the service enforces this guard).
    /// </summary>
    Task<LdapWriteResult> DeleteUserAsync(string samAccountName);

    /// <summary>
    /// Adds a user to an AD security or distribution group.
    /// </summary>
    /// <param name="groupDn">Full DN of the target group, e.g. "CN=GRP_IT,OU=Groups,DC=cnss,DC=cd".</param>
    Task<LdapWriteResult> AddToGroupAsync(string samAccountName, string groupDn);

    /// <summary>
    /// Removes a user from an AD security or distribution group.
    /// </summary>
    Task<LdapWriteResult> RemoveFromGroupAsync(string samAccountName, string groupDn);

    // ── Read operations (search / discover) ─────────────────────────────────

    /// <summary>
    /// Searches AD groups whose CN contains <paramref name="nameFragment"/>.
    /// Used by the frontend autocomplete — requires at least 2 characters to
    /// avoid returning thousands of results.
    ///
    /// Returns at most 50 results ordered by CN.
    /// </summary>
    Task<LdapQueryResult<List<AdGroupDto>>> SearchGroupsAsync(string nameFragment);

    /// <summary>
    /// Returns all AD groups the specified user is a direct member of,
    /// resolved from the user's <c>memberOf</c> attribute.
    ///
    /// Used to populate the "current groups" chip-list in the remove panel,
    /// allowing one-click selection instead of typing DNs manually.
    /// </summary>
    Task<LdapQueryResult<List<AdGroupDto>>> GetUserGroupsAsync(string samAccountName);
}
