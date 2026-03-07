using Microsoft.AspNetCore.Identity;

namespace RubacCore.Models;

public class ApplicationUser : IdentityUser<long>
{
    public string? FirstName  { get; set; }
    public string? LastName   { get; set; }
    public bool    IsActive   { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // ── Active Directory / LDAP ────────────────────────────────────────────
    /// <summary>LDAP Distinguished Name. Null for local (Identity) users.</summary>
    public string? LdapDn       { get; set; }
    /// <summary>"local" for Identity users, "ldap" for AD users.</summary>
    public string  AuthProvider { get; set; } = "local";

    public ICollection<ApplicationUserRole> UserRoles   { get; set; } = [];

    // ── Centre assignments ─────────────────────────────────────────────────
    public ICollection<UserCentre>      UserCentres      { get; set; } = [];

    // ── Application access ────────────────────────────────────────────────
    public ICollection<UserApplication> UserApplications { get; set; } = [];
}
