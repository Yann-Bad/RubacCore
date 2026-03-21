namespace RubacCore.Dtos;

// ── Result wrapper ────────────────────────────────────────────────────────────

/// <summary>
/// Thin result type returned by every LdapWriteService method.
/// The controller maps Success/Message to the appropriate HTTP status code.
/// </summary>
public record LdapWriteResult(bool Success, string Message);

// ── Read DTO (returned on create / update) ────────────────────────────────────

/// <summary>
/// Read-only snapshot of an LDAP user account returned after write operations.
/// </summary>
public record AdUserInfoDto(
    string  DistinguishedName,
    string  SamAccountName,
    string? DisplayName,
    string? GivenName,
    string? Surname,
    string? Email,
    string? UserPrincipalName,
    bool    IsEnabled,
    string? Description);

// ── Write DTOs ────────────────────────────────────────────────────────────────

/// <summary>
/// Payload for creating a new Active Directory user account.
///
/// ⚠️  Password setting requires LDAPS (Ldap.UseSsl = true, Port = 636).
///     If SSL is disabled the account is created without a password and
///     disabled until a password is set through another channel.
/// </summary>
public record CreateAdUserDto(
    /// <summary>Windows logon name (sAMAccountName). Max 20 chars. Unique in domain.</summary>
    string SamAccountName,

    /// <summary>Full display name shown in AD / Exchange.</summary>
    string DisplayName,

    string? GivenName,
    string? Surname,

    /// <summary>Professional email (mail attribute).</summary>
    string? Email,

    /// <summary>
    /// Initial password. Must satisfy the domain password policy.
    /// Requires LDAPS — ignored (with a warning) when SSL is disabled.
    /// </summary>
    string? Password,

    /// <summary>
    /// When true, the user must change their password at the next login.
    /// Only applicable when Password is provided and LDAPS is enabled.
    /// </summary>
    bool MustChangePasswordOnLogin = true,

    string? Description = null);

/// <summary>
/// Payload for updating an existing AD account.
/// All fields are optional — null means "leave unchanged".
///
/// sAMAccountName and UPN are intentionally excluded; renaming an AD account
/// requires coordination with the IT directory team.
/// </summary>
public record UpdateAdUserDto(
    string? DisplayName  = null,
    string? GivenName    = null,
    string? Surname      = null,
    string? Email        = null,
    string? Description  = null,

    /// <summary>New password. Requires LDAPS. Null = do not change.</summary>
    string? NewPassword  = null,

    bool MustChangePasswordOnLogin = false);

/// <summary>
/// Payload for suspending (disabling) an AD account.
/// The reason is stored in the AD Description field for audit purposes.
/// </summary>
public record SuspendAdUserDto(
    /// <summary>
    /// Mandatory reason — stored as "[SUSPENDU yyyy-MM-dd] reason" in AD Description.
    /// Example: "Départ volontaire — contrat terminé le 2026-03-20"
    /// </summary>
    string Reason);

// ── Generic query result (used by search endpoints that return data) ────────

/// <summary>
/// Generic result wrapper for AD read operations that return data alongside
/// success/failure status. Keeps the same pattern as <see cref="LdapWriteResult"/>
/// so controllers stay consistent — check <c>Success</c> first, then use <c>Data</c>.
/// </summary>
public record LdapQueryResult<T>(bool Success, string Message, T? Data)
{
    /// <summary>Convenience factory for a successful result with data.</summary>
    public static LdapQueryResult<T> Ok(T data)           => new(true,  string.Empty, data);
    /// <summary>Convenience factory for a failed result — Data will be null.</summary>
    public static LdapQueryResult<T> Fail(string message) => new(false, message,      default);
}

// ── Group DTO (read-only, returned by search and memberOf queries) ────────────

/// <summary>
/// Lightweight representation of an Active Directory group.
/// Returned by <c>SearchGroupsAsync</c> (autocomplete) and
/// <c>GetUserGroupsAsync</c> (current-memberships panel).
///
/// The <see cref="Dn"/> is the key passed to add/remove operations;
/// the <see cref="Name"/> is what the user sees in the dropdown.
/// </summary>
public class AdGroupDto
{
    /// <summary>
    /// Full Distinguished Name — used as the authoritative key for add/remove.
    /// Example: "CN=GRP_RH,OU=Groupes,DC=cnss,DC=cd"
    /// </summary>
    public string  Dn          { get; set; } = string.Empty;

    /// <summary>Common Name (cn attribute) — human-readable group name.</summary>
    public string  Name        { get; set; } = string.Empty;

    /// <summary>Optional AD description attribute.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Number of direct members in the group.
    /// Populated by <c>SearchGroupsAsync</c>; 0 for <c>GetUserGroupsAsync</c>.
    /// </summary>
    public int MemberCount { get; set; }
}

/// <summary>
/// Payload for group membership operations (add / remove).
/// </summary>
public record GroupMembershipDto(
    /// <summary>
    /// Full Distinguished Name of the target AD group.
    /// Example: "CN=GRP_IT,OU=Groups,DC=cnss,DC=cd"
    /// </summary>
    string GroupDn);
