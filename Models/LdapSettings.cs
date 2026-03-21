namespace RubacCore.Models;

public class LdapSettings
{
    public bool   Enabled         { get; set; } = false;
    public string Host            { get; set; } = "";
    public int    Port            { get; set; } = 389;
    public bool   UseSsl          { get; set; } = false;

    /// <summary>Domain suffix used to detect enterprise logins, e.g. "corp.local".</summary>
    public string Domain          { get; set; } = "";

    /// <summary>LDAP search base, e.g. "DC=corp,DC=local".</summary>
    public string SearchBase      { get; set; } = "";

    /// <summary>
    /// Optional service-account UPN for the attribute-search bind.
    /// If empty, the authenticating user's credentials are reused.
    /// </summary>
    public string ServiceAccount  { get; set; } = "";
    public string ServicePassword { get; set; } = "";

    /// <summary>Role assigned to new AD users on their first login.</summary>
    public string DefaultRole     { get; set; } = "User";

    /// <summary>
    /// Target OU (Distinguished Name) where new user objects are created.
    /// Example: "OU=Utilisateurs,DC=cnss,DC=cd"
    /// Defaults to the SearchBase if left empty (creates in the root DC).
    /// </summary>
    public string WriteOUPath     { get; set; } = "";
}
