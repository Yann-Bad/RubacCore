namespace RubacCore.Interfaces;

public interface ILdapService
{
    /// <summary>
    /// Validates the UPN + password against LDAP.
    /// Returns user attributes on success, null on failure.
    /// </summary>
    Task<LdapUserInfo?> AuthenticateAsync(string upn, string password);
}

public record LdapUserInfo(
    string  DistinguishedName,
    string  SamAccountName,
    string? DisplayName,
    string? Email,
    string? GivenName,
    string? Surname,
    /// <summary>AD physicalDeliveryOfficeName → matched to Centre.Code on first login.</summary>
    string? OfficeName = null);
