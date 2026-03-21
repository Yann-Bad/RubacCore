using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using RubacCore.Dtos;
using RubacCore.Interfaces;
using RubacCore.Models;

namespace RubacCore.Services;

/// <summary>
/// Implements Active Directory write operations using <see cref="System.DirectoryServices.Protocols"/>.
///
/// This service uses the same low-level LDAP stack as <see cref="LdapService"/>
/// (cross-platform, no Windows-only dependency).
///
/// All public methods are async — the synchronous SDS.Protocols calls are
/// wrapped in <c>Task.Run</c> to avoid blocking ASP.NET Core thread-pool threads.
///
/// Password operations (create with password, reset password) require LDAPS
/// (Ldap:UseSsl = true, Port = 636). When SSL is disabled these operations
/// are skipped with a warning — the account is created disabled and the
/// caller must set the password through another channel.
/// </summary>
public class LdapWriteService : ILdapWriteService
{
    private readonly LdapSettings            _s;
    private readonly ILogger<LdapWriteService> _logger;

    // AD userAccountControl flag values
    private const int UacNormalAccount = 512;  // 0x0200
    private const int UacDisabled      = 2;    // 0x0002 — set this bit to disable

    public LdapWriteService(IOptions<LdapSettings> settings, ILogger<LdapWriteService> logger)
    {
        _s      = settings.Value;
        _logger = logger;
    }

    // ── Create ────────────────────────────────────────────────────────────────

    public Task<LdapWriteResult> CreateUserAsync(CreateAdUserDto dto) => Task.Run(() =>
    {
        try
        {
            using var conn = BindServiceAccount();

            // Check for duplicate sAMAccountName
            if (FindUserDn(conn, dto.SamAccountName) is not null)
            {
                _logger.LogWarning("CreateUser: sAMAccountName '{Sam}' already exists", dto.SamAccountName);
                return Fail($"Un compte avec le nom '{dto.SamAccountName}' existe déjà.");
            }

            var ouPath = string.IsNullOrWhiteSpace(_s.WriteOUPath) ? _s.SearchBase : _s.WriteOUPath;
            var cn     = dto.DisplayName.Replace("\"", "\\\""); // escape quotes in CN
            var newDn  = $"CN={cn},{ouPath}";

            var req = new AddRequest(newDn);

            // Core object classes required for a user account in AD
            req.Attributes.Add(new DirectoryAttribute("objectClass",
                "top", "person", "organizationalPerson", "user"));

            req.Attributes.Add(new DirectoryAttribute("cn",                 dto.DisplayName));
            req.Attributes.Add(new DirectoryAttribute("sAMAccountName",     dto.SamAccountName));
            req.Attributes.Add(new DirectoryAttribute("userPrincipalName",  $"{dto.SamAccountName}@{_s.Domain}"));
            req.Attributes.Add(new DirectoryAttribute("displayName",        dto.DisplayName));

            if (!string.IsNullOrWhiteSpace(dto.GivenName))
                req.Attributes.Add(new DirectoryAttribute("givenName", dto.GivenName));
            if (!string.IsNullOrWhiteSpace(dto.Surname))
                req.Attributes.Add(new DirectoryAttribute("sn", dto.Surname));
            if (!string.IsNullOrWhiteSpace(dto.Email))
                req.Attributes.Add(new DirectoryAttribute("mail", dto.Email));
            if (!string.IsNullOrWhiteSpace(dto.Description))
                req.Attributes.Add(new DirectoryAttribute("description", dto.Description));

            bool passwordSet = false;

            if (!string.IsNullOrWhiteSpace(dto.Password))
            {
                if (!_s.UseSsl)
                {
                    _logger.LogWarning(
                        "CreateUser: SSL is disabled — password cannot be set for '{Sam}'. " +
                        "Account will be created DISABLED. Enable Ldap:UseSsl and use port 636 to set passwords.",
                        dto.SamAccountName);
                }
                else
                {
                    // unicodePwd must be encoded as UTF-16LE surrounded by double-quotes
                    req.Attributes.Add(new DirectoryAttribute("unicodePwd",
                        EncodePassword(dto.Password)));

                    // Enable the account; force password change on first login if requested
                    req.Attributes.Add(new DirectoryAttribute("userAccountControl",
                        UacNormalAccount.ToString()));

                    if (dto.MustChangePasswordOnLogin)
                        req.Attributes.Add(new DirectoryAttribute("pwdLastSet", "0"));

                    passwordSet = true;
                }
            }

            if (!passwordSet)
            {
                // Creating without a password → ACCOUNTDISABLE + PASSWD_NOTREQD to allow creation
                req.Attributes.Add(new DirectoryAttribute("userAccountControl", "546")); // 512 | 2 | 32
            }

            conn.SendRequest(req);

            _logger.LogInformation(
                "CreateUser: account '{Sam}' created at '{Dn}' (passwordSet={PwdSet})",
                dto.SamAccountName, newDn, passwordSet);

            var suffix = passwordSet ? "" : " (DÉSACTIVÉ — aucun mot de passe défini, SSL requis pour activer)";
            return Ok($"Compte '{dto.SamAccountName}' créé avec succès.{suffix}");
        }
        catch (DirectoryOperationException ex)
        {
            _logger.LogError(ex, "CreateUser: LDAP operation error for '{Sam}'", dto.SamAccountName);
            return Fail($"Erreur LDAP lors de la création: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateUser: unexpected error for '{Sam}'", dto.SamAccountName);
            return Fail("Erreur inattendue lors de la création du compte AD.");
        }
    });

    // ── Update ────────────────────────────────────────────────────────────────

    public Task<LdapWriteResult> UpdateUserAsync(string samAccountName, UpdateAdUserDto dto) => Task.Run(() =>
    {
        try
        {
            using var conn = BindServiceAccount();
            var dn = FindUserDn(conn, samAccountName);
            if (dn is null) return NotFound(samAccountName);

            var mods = new List<DirectoryAttributeModification>();

            void Replace(string attr, string? value)
            {
                if (value is null) return;
                var mod = new DirectoryAttributeModification
                    { Name = attr, Operation = DirectoryAttributeOperation.Replace };
                mod.Add(value);
                mods.Add(mod);
            }

            Replace("displayName", dto.DisplayName);
            Replace("givenName",   dto.GivenName);
            Replace("sn",          dto.Surname);
            Replace("mail",        dto.Email);
            Replace("description", dto.Description);

            if (!string.IsNullOrWhiteSpace(dto.NewPassword))
            {
                if (!_s.UseSsl)
                {
                    _logger.LogWarning(
                        "UpdateUser: SSL is disabled — password reset for '{Sam}' skipped. " +
                        "Enable Ldap:UseSsl = true and port 636.", samAccountName);
                }
                else
                {
                    var pwdMod = new DirectoryAttributeModification
                        { Name = "unicodePwd", Operation = DirectoryAttributeOperation.Replace };
                    pwdMod.Add(EncodePassword(dto.NewPassword));
                    mods.Add(pwdMod);

                    if (dto.MustChangePasswordOnLogin)
                    {
                        var pwdLastSet = new DirectoryAttributeModification
                            { Name = "pwdLastSet", Operation = DirectoryAttributeOperation.Replace };
                        pwdLastSet.Add("0");
                        mods.Add(pwdLastSet);
                    }
                }
            }

            if (mods.Count == 0)
                return Ok("Aucune modification à appliquer (tous les champs étaient null).");

            var req = new ModifyRequest(dn, [.. mods]);
            conn.SendRequest(req);

            _logger.LogInformation("UpdateUser: '{Sam}' updated ({Count} attribute(s))", samAccountName, mods.Count);
            return Ok($"Compte '{samAccountName}' mis à jour avec succès.");
        }
        catch (DirectoryOperationException ex)
        {
            _logger.LogError(ex, "UpdateUser: LDAP error for '{Sam}'", samAccountName);
            return Fail($"Erreur LDAP lors de la mise à jour: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateUser: unexpected error for '{Sam}'", samAccountName);
            return Fail("Erreur inattendue lors de la mise à jour du compte AD.");
        }
    });

    // ── Suspend ───────────────────────────────────────────────────────────────

    public Task<LdapWriteResult> SuspendUserAsync(string samAccountName, string reason) => Task.Run(() =>
    {
        try
        {
            using var conn = BindServiceAccount();
            var dn = FindUserDn(conn, samAccountName);
            if (dn is null) return NotFound(samAccountName);

            var uac = GetUserAccountControl(conn, dn);
            if ((uac & UacDisabled) != 0)
            {
                _logger.LogInformation("SuspendUser: '{Sam}' already disabled", samAccountName);
                return Ok("Compte déjà désactivé.");
            }

            var newUac    = uac | UacDisabled;
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var newDesc   = $"[SUSPENDU {timestamp}] {reason}";

            var req = new ModifyRequest(dn,
                MakeReplace("userAccountControl", newUac.ToString()),
                MakeReplace("description",        newDesc));

            conn.SendRequest(req);

            _logger.LogInformation("SuspendUser: '{Sam}' suspended — reason: {Reason}", samAccountName, reason);
            return Ok($"Compte '{samAccountName}' suspendu avec succès.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SuspendUser: error for '{Sam}'", samAccountName);
            return Fail("Erreur lors de la suspension du compte AD.");
        }
    });

    // ── Reactivate ────────────────────────────────────────────────────────────

    public Task<LdapWriteResult> ReactivateUserAsync(string samAccountName) => Task.Run(() =>
    {
        try
        {
            using var conn = BindServiceAccount();
            var dn = FindUserDn(conn, samAccountName);
            if (dn is null) return NotFound(samAccountName);

            var uac = GetUserAccountControl(conn, dn);
            if ((uac & UacDisabled) == 0)
                return Ok("Compte déjà actif.");

            var newUac    = uac & ~UacDisabled; // clear the disable bit
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd");

            var req = new ModifyRequest(dn,
                MakeReplace("userAccountControl", newUac.ToString()),
                MakeReplace("description",        $"[RÉACTIVÉ {timestamp}]"));

            conn.SendRequest(req);

            _logger.LogInformation("ReactivateUser: '{Sam}' re-enabled", samAccountName);
            return Ok($"Compte '{samAccountName}' réactivé avec succès.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReactivateUser: error for '{Sam}'", samAccountName);
            return Fail("Erreur lors de la réactivation du compte AD.");
        }
    });

    // ── Delete ────────────────────────────────────────────────────────────────

    public Task<LdapWriteResult> DeleteUserAsync(string samAccountName) => Task.Run(() =>
    {
        try
        {
            using var conn = BindServiceAccount();
            var dn = FindUserDn(conn, samAccountName);
            if (dn is null) return NotFound(samAccountName);

            // Safety guard — account must be disabled before permanent deletion
            var uac = GetUserAccountControl(conn, dn);
            if ((uac & UacDisabled) == 0)
            {
                _logger.LogWarning(
                    "DeleteUser: refused — '{Sam}' is still enabled. Suspend first.", samAccountName);
                return Fail("Le compte doit être suspendu avant d'être supprimé définitivement.");
            }

            conn.SendRequest(new DeleteRequest(dn));

            _logger.LogInformation("DeleteUser: '{Sam}' permanently deleted (DN was: {Dn})", samAccountName, dn);
            return Ok($"Compte '{samAccountName}' supprimé définitivement.");
        }
        catch (DirectoryOperationException ex)
        {
            _logger.LogError(ex, "DeleteUser: LDAP error for '{Sam}'", samAccountName);
            return Fail($"Erreur LDAP lors de la suppression: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteUser: unexpected error for '{Sam}'", samAccountName);
            return Fail("Erreur inattendue lors de la suppression du compte AD.");
        }
    });

    // ── Group membership ──────────────────────────────────────────────────────

    public Task<LdapWriteResult> AddToGroupAsync(string samAccountName, string groupDn) => Task.Run(() =>
    {
        try
        {
            using var conn = BindServiceAccount();
            var userDn = FindUserDn(conn, samAccountName);
            if (userDn is null) return NotFound(samAccountName);

            var mod = new DirectoryAttributeModification
                { Name = "member", Operation = DirectoryAttributeOperation.Add };
            mod.Add(userDn);

            conn.SendRequest(new ModifyRequest(groupDn, mod));

            _logger.LogInformation("AddToGroup: '{Sam}' added to '{Group}'", samAccountName, groupDn);
            return Ok($"'{samAccountName}' ajouté au groupe avec succès.");
        }
        catch (DirectoryOperationException ex) when (ex.Response is ModifyResponse mr &&
            mr.ResultCode == ResultCode.AttributeOrValueExists)
        {
            return Ok($"'{samAccountName}' est déjà membre du groupe.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddToGroup: error for '{Sam}'/'{Group}'", samAccountName, groupDn);
            return Fail("Erreur lors de l'ajout au groupe AD.");
        }
    });

    public Task<LdapWriteResult> RemoveFromGroupAsync(string samAccountName, string groupDn) => Task.Run(() =>
    {
        try
        {
            using var conn = BindServiceAccount();
            var userDn = FindUserDn(conn, samAccountName);
            if (userDn is null) return NotFound(samAccountName);

            var mod = new DirectoryAttributeModification
                { Name = "member", Operation = DirectoryAttributeOperation.Delete };
            mod.Add(userDn);

            conn.SendRequest(new ModifyRequest(groupDn, mod));

            _logger.LogInformation("RemoveFromGroup: '{Sam}' removed from '{Group}'", samAccountName, groupDn);
            return Ok($"'{samAccountName}' retiré du groupe avec succès.");
        }
        catch (DirectoryOperationException ex) when (ex.Response is ModifyResponse mr &&
            mr.ResultCode == ResultCode.NoSuchAttribute)
        {
            return Ok($"'{samAccountName}' n'est pas membre du groupe.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RemoveFromGroup: error for '{Sam}'/'{Group}'", samAccountName, groupDn);
            return Fail("Erreur lors du retrait du groupe AD.");
        }
    });

    // ── Group search & discovery ──────────────────────────────────────────────

    /// <summary>
    /// Searches AD groups whose CN contains <paramref name="nameFragment"/>.
    ///
    /// LDAP filter: (&(objectClass=group)(cn=*{fragment}*))
    /// Requests: cn, distinguishedName, description, member attributes.
    /// Returns at most 50 groups ordered alphabetically by CN.
    ///
    /// Requires at least 2 characters to avoid full-directory scans.
    /// </summary>
    public Task<LdapQueryResult<List<AdGroupDto>>> SearchGroupsAsync(string nameFragment) => Task.Run(() =>
    {
        try
        {
            if (string.IsNullOrWhiteSpace(nameFragment) || nameFragment.Length < 2)
                return LdapQueryResult<List<AdGroupDto>>.Fail("Fragment trop court (min 2 caractères).");

            using var conn = BindServiceAccount();

            // Wildcard search on CN — EscapeFilter protects against LDAP injection
            var filter   = $"(&(objectClass=group)(cn=*{EscapeFilter(nameFragment)}*))";
            var request  = new SearchRequest(
                _s.SearchBase, filter, SearchScope.Subtree,
                "cn", "distinguishedName", "description", "member");

            // Cap at 50 results so the dropdown stays usable
            request.SizeLimit = 50;

            var response = (SearchResponse)conn.SendRequest(request);

            var groups = response.Entries
                .Cast<SearchResultEntry>()
                .Select(e => new AdGroupDto
                {
                    Dn          = e.DistinguishedName,
                    Name        = GetAttr(e, "cn") ?? e.DistinguishedName,
                    Description = GetAttr(e, "description"),
                    // member attribute stores one value per member — use Count as member count
                    MemberCount = e.Attributes["member"]?.Count ?? 0,
                })
                .OrderBy(g => g.Name)
                .ToList();

            _logger.LogDebug("SearchGroups: '{Fragment}' → {Count} group(s)", nameFragment, groups.Count);
            return LdapQueryResult<List<AdGroupDto>>.Ok(groups);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearchGroups: error for fragment '{F}'", nameFragment);
            return LdapQueryResult<List<AdGroupDto>>.Fail("Erreur lors de la recherche des groupes AD.");
        }
    });

    /// <summary>
    /// Returns all groups the user is a direct member of, resolved from
    /// the <c>memberOf</c> attribute on the user object.
    ///
    /// Flow:
    ///   1. Locate the user by sAMAccountName → read memberOf[].
    ///   2. For each DN in memberOf, fetch the group's cn and description.
    /// </summary>
    public Task<LdapQueryResult<List<AdGroupDto>>> GetUserGroupsAsync(string samAccountName) => Task.Run(() =>
    {
        try
        {
            using var conn = BindServiceAccount();

            // ── Step 1: find the user and retrieve their memberOf list ─────────
            var userFilter   = $"(&(objectClass=user)(sAMAccountName={EscapeFilter(samAccountName)}))";
            var userRequest  = new SearchRequest(
                _s.SearchBase, userFilter, SearchScope.Subtree, "memberOf");
            var userResponse = (SearchResponse)conn.SendRequest(userRequest);

            if (userResponse.Entries.Count == 0)
                return LdapQueryResult<List<AdGroupDto>>.Fail(
                    $"Utilisateur '{samAccountName}' introuvable dans l'AD.");

            var memberOfAttr = userResponse.Entries[0].Attributes["memberOf"];

            // User belongs to no groups — return empty list (not an error)
            if (memberOfAttr == null || memberOfAttr.Count == 0)
                return LdapQueryResult<List<AdGroupDto>>.Ok(new List<AdGroupDto>());

            // ── Step 2: resolve each group DN to a friendly name ──────────────
            var groups = new List<AdGroupDto>();

            foreach (string groupDn in memberOfAttr.GetValues(typeof(string)).Cast<string>())
            {
                try
                {
                    // Scope.Base searches exactly the entry at this DN
                    var grpReq  = new SearchRequest(groupDn, "(objectClass=*)",
                        SearchScope.Base, "cn", "description");
                    var grpResp = (SearchResponse)conn.SendRequest(grpReq);

                    if (grpResp.Entries.Count == 0) continue;

                    var entry = grpResp.Entries[0];
                    groups.Add(new AdGroupDto
                    {
                        Dn          = groupDn,
                        Name        = GetAttr(entry, "cn") ?? groupDn,
                        Description = GetAttr(entry, "description"),
                        MemberCount = 0,   // not needed here — avoids extra attribute fetch
                    });
                }
                catch (Exception ex)
                {
                    // A group DN may have been deleted or moved — skip it with a warning
                    _logger.LogWarning(ex,
                        "GetUserGroups: could not resolve group DN '{Dn}' for '{Sam}'",
                        groupDn, samAccountName);
                }
            }

            _logger.LogDebug("GetUserGroups: '{Sam}' belongs to {Count} group(s)",
                samAccountName, groups.Count);

            return LdapQueryResult<List<AdGroupDto>>.Ok(
                groups.OrderBy(g => g.Name).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetUserGroups: error for '{Sam}'", samAccountName);
            return LdapQueryResult<List<AdGroupDto>>.Fail(
                "Erreur lors de la récupération des groupes de l'utilisateur.");
        }
    });

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Opens a new LDAP connection bound with the service account credentials.
    /// Caller is responsible for disposing.
    /// </summary>
    private LdapConnection BindServiceAccount()
    {
        var conn = new LdapConnection(
            new LdapDirectoryIdentifier(_s.Host, _s.Port));

        conn.AuthType = AuthType.Basic;
        conn.SessionOptions.ProtocolVersion = 3;

        if (_s.UseSsl)
            conn.SessionOptions.SecureSocketLayer = true;

        conn.Bind(new NetworkCredential(_s.ServiceAccount, _s.ServicePassword));
        return conn;
    }

    /// <summary>
    /// Searches AD for a user by sAMAccountName and returns their Distinguished Name,
    /// or null if not found.
    /// </summary>
    private string? FindUserDn(LdapConnection conn, string samAccountName)
    {
        var filter   = $"(&(objectClass=user)(sAMAccountName={EscapeFilter(samAccountName)}))";
        var request  = new SearchRequest(_s.SearchBase, filter, SearchScope.Subtree, "distinguishedName");
        var response = (SearchResponse)conn.SendRequest(request);
        return response.Entries.Count > 0 ? response.Entries[0].DistinguishedName : null;
    }

    /// <summary>
    /// Reads the current <c>userAccountControl</c> integer value for the given DN.
    /// </summary>
    private int GetUserAccountControl(LdapConnection conn, string dn)
    {
        var request  = new SearchRequest(dn, "(objectClass=*)", SearchScope.Base, "userAccountControl");
        var response = (SearchResponse)conn.SendRequest(request);

        if (response.Entries.Count == 0)
            throw new InvalidOperationException($"Entry not found: {dn}");

        var raw = response.Entries[0].Attributes["userAccountControl"]?
            .GetValues(typeof(string)).Cast<string>().FirstOrDefault();

        return int.TryParse(raw, out var val) ? val : UacNormalAccount;
    }

    /// <summary>
    /// Builds a single-attribute <see cref="DirectoryAttributeModification"/> with Replace operation.
    /// </summary>
    private static DirectoryAttributeModification MakeReplace(string attrName, string value)
    {
        var mod = new DirectoryAttributeModification
            { Name = attrName, Operation = DirectoryAttributeOperation.Replace };
        mod.Add(value);
        return mod;
    }

    /// <summary>
    /// Encodes a plain-text password as UTF-16LE surrounded by double-quotes,
    /// as required by the AD <c>unicodePwd</c> attribute.
    /// Only works over an SSL/TLS-protected LDAP connection.
    /// </summary>
    private static byte[] EncodePassword(string password) =>
        Encoding.Unicode.GetBytes($"\"{password}\"");

    /// <summary>Escapes special characters in LDAP filter values (RFC 4515).</summary>
    private static string EscapeFilter(string value) =>
        value
            .Replace("\\", "\\5c")
            .Replace("*",  "\\2a")
            .Replace("(",  "\\28")
            .Replace(")",  "\\29")
            .Replace("\0", "\\00");

    /// <summary>
    /// Reads the first string value of an attribute from a search result entry.
    /// Returns null if the attribute is absent (avoids null-reference on missing attrs).
    /// Shared by AuthenticateAsync and the new group-search methods.
    /// </summary>
    private static string? GetAttr(SearchResultEntry entry, string attr) =>
        entry.Attributes[attr]?.GetValues(typeof(string)).Cast<string>().FirstOrDefault();

    // ── Result factory helpers ─────────────────────────────────────────────────

    private static LdapWriteResult Ok(string message)     => new(true, message);
    private static LdapWriteResult Fail(string message)   => new(false, message);
    private static LdapWriteResult NotFound(string sam)   => new(false, $"Compte '{sam}' introuvable dans l'AD.");
}
