namespace RubacCore.Models;

/// <summary>
/// Grants a user explicit access to a registered OAuth2/OIDC client application.
/// <para>
/// The <see cref="ApplicationClientId"/> matches the <c>client_id</c> registered
/// in OpenIddict (e.g. "dashboard-spa", "rubac-admin").
/// </para>
/// </summary>
public class UserApplication
{
    public long   UserId              { get; set; }
    public string ApplicationClientId { get; set; } = null!;

    public ApplicationUser User { get; set; } = null!;
}
