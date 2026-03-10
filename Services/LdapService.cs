using System.DirectoryServices.Protocols;
using System.Net;
using Microsoft.Extensions.Options;
using RubacCore.Interfaces;
using RubacCore.Models;

namespace RubacCore.Services;

public class LdapService : ILdapService
{
    private readonly LdapSettings         _settings;
    private readonly ILogger<LdapService> _logger;

    public LdapService(IOptions<LdapSettings> settings, ILogger<LdapService> logger)
    {
        _settings = settings.Value;
        _logger   = logger;
    }

    public async Task<LdapUserInfo?> AuthenticateAsync(string upn, string password)
    {
        var samAccount = upn.Contains('@') ? upn.Split('@')[0] : upn;

        try
        {
            return await Task.Run(() =>
            {
                // Step 1 — bind with the user's own credentials to verify the password
                using var userConn = CreateConnection();
                userConn.Bind(new NetworkCredential(upn, password));

                // Step 2 — search for attributes using a service account (or the user itself)
                using var searchConn = CreateConnection();
                if (!string.IsNullOrEmpty(_settings.ServiceAccount))
                {
                    try
                    {
                        searchConn.Bind(new NetworkCredential(
                            _settings.ServiceAccount, _settings.ServicePassword));
                    }
                    catch (LdapException ex)
                    {
                        _logger.LogWarning(
                            "Service account bind failed (code {Code}): {Msg}. Falling back to user credentials for attribute search.",
                            ex.ErrorCode, ex.Message);
                        searchConn.Bind(new NetworkCredential(upn, password));
                    }
                }
                else
                    searchConn.Bind(new NetworkCredential(upn, password));

                var filter  = $"(&(objectClass=user)(sAMAccountName={EscapeLdapFilter(samAccount)}))";
                var request = new SearchRequest(
                    _settings.SearchBase, filter, SearchScope.Subtree,
                    "distinguishedName", "cn", "mail", "givenName", "sn",
                    "physicalDeliveryOfficeName");

                var response = (SearchResponse)searchConn.SendRequest(request);

                if (response.Entries.Count == 0)
                {
                    // Password was valid (bind succeeded) but no matching entry found.
                    // Return minimal info so the local shadow user can still be created.
                    return new LdapUserInfo(
                        DistinguishedName: $"uid={samAccount},{_settings.SearchBase}",
                        SamAccountName:    samAccount,
                        DisplayName:       samAccount,
                        Email:             upn,
                        GivenName:         null,
                        Surname:           null);
                }

                var entry = response.Entries[0];
                return new LdapUserInfo(
                    DistinguishedName: entry.DistinguishedName,
                    SamAccountName:    samAccount,
                    DisplayName:       GetAttr(entry, "cn"),
                    Email:             GetAttr(entry, "mail") ?? upn,
                    GivenName:         GetAttr(entry, "givenName"),
                    Surname:           GetAttr(entry, "sn"),
                    OfficeName:        GetAttr(entry, "physicalDeliveryOfficeName"));
            });
        }
        catch (LdapException ex)
        {
            // InvalidCredentials (49) is the normal "wrong password" response — debug, not warning
            _logger.LogDebug("LDAP auth failed for {Upn}: result={Code} {Msg}",
                upn, ex.ErrorCode, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected LDAP error for {Upn}", upn);
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private LdapConnection CreateConnection()
    {
        var conn = new LdapConnection(
            new LdapDirectoryIdentifier(_settings.Host, _settings.Port));

        conn.AuthType = AuthType.Basic;
        conn.SessionOptions.ProtocolVersion = 3;

        if (_settings.UseSsl)
            conn.SessionOptions.SecureSocketLayer = true;

        return conn;
    }

    private static string? GetAttr(SearchResultEntry entry, string attr) =>
        entry.Attributes[attr]?.GetValues(typeof(string)).Cast<string>().FirstOrDefault();

    /// <summary>Escapes special characters in LDAP search filter values (RFC 4515).</summary>
    private static string EscapeLdapFilter(string value) =>
        value
            .Replace("\\", "\\5c")
            .Replace("*",  "\\2a")
            .Replace("(",  "\\28")
            .Replace(")",  "\\29")
            .Replace("\0", "\\00");
}
