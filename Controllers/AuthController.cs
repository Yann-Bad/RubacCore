using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using RubacCore.Interfaces;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace RubacCore.Controllers;

[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAuthService              _authService;
    private readonly IOpenIddictScopeManager   _scopeManager;

    public AuthController(IAuthService authService, IOpenIddictScopeManager scopeManager)
    {
        _authService  = authService;
        _scopeManager = scopeManager;
    }

    // ── POST /connect/token ────────────────────────────────────────
    [HttpPost("~/connect/token")]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict request cannot be retrieved.");

        // Password flow
        if (request.IsPasswordGrantType())
        {
            if (!await _authService.ValidateCredentialsAsync(request.Username!, request.Password!))
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error]
                            = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription]
                            = "Invalid credentials."
                    }));

            var user  = await _authService.GetUserByNameAsync(request.Username!);
            var roles = await _authService.GetRolesForClientAsync(user!.Id, request.ClientId!);
            var permissions = await _authService.GetPermissionsForClientAsync(user!.Id, request.ClientId!);
            var (centrePrimary, centres) = await _authService.GetUserCentresAsync(user.Id);
            return await BuildSignInResultAsync(user.Id.ToString(), user.UserName,
                                                user.Email, user.FirstName, user.LastName,
                                                roles, permissions, centrePrimary, centres, request);
        }

        // Refresh token flow
        if (request.IsRefreshTokenGrantType())
        {
            var result = await HttpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            var userId = result.Principal!.GetClaim(Claims.Subject)!;
            var user   = await _authService.GetUserByIdAsync(userId);

            if (user is null)
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error]
                            = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription]
                            = "Refresh token is no longer valid."
                    }));

            // Re-evaluate roles on every refresh so permission changes take
            // effect without requiring a full re-login.
            var roles = await _authService.GetRolesForClientAsync(user.Id, request.ClientId!);
            var permissions = await _authService.GetPermissionsForClientAsync(user.Id, request.ClientId!);
            var (centrePrimary, centres) = await _authService.GetUserCentresAsync(user.Id);
            return await BuildSignInResultAsync(user.Id.ToString(), user.UserName,
                                                user.Email, user.FirstName, user.LastName,
                                                roles, permissions, centrePrimary, centres, request);
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }

    // ── GET /connect/userinfo ──────────────────────────────────────
    [HttpGet("~/connect/userinfo")]
    public async Task<IActionResult> UserInfo()
    {
        var userId = User.GetClaim(Claims.Subject)!;
        var user   = await _authService.GetUserByIdAsync(userId);

        if (user is null)
            return Challenge(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        return Ok(new
        {
            sub            = user.Id.ToString(),
            name           = $"{user.FirstName} {user.LastName}".Trim(),
            email          = user.Email,
            email_verified = true,
            roles          = user.Roles
        });
    }

    // ── Private helpers ────────────────────────────────────────────
    private async Task<IActionResult> BuildSignInResultAsync(
        string userId, string userName, string email,
        string? firstName, string? lastName,
        IEnumerable<string> roles,
        IEnumerable<string> permissions,
        string? centrePrimary, IEnumerable<string> centres,
        OpenIddictRequest request)
    {
        var identity = new ClaimsIdentity(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject,    userId)
                .SetClaim(Claims.Name,       userName)
                .SetClaim(Claims.Email,      email)
                .SetClaim(Claims.GivenName,  firstName)
                .SetClaim(Claims.FamilyName, lastName);

        if (centrePrimary is not null)
            identity.SetClaim("centre_primary", centrePrimary);

        var centreList = centres.ToList();
        if (centreList.Count > 0)
            identity.SetClaims("centres", [.. centreList]);

        identity.SetClaims(Claims.Role, [.. roles]);

        var permissionList = permissions.ToList();
        if (permissionList.Count > 0)
            identity.SetClaims("permission", [.. permissionList]);

        identity.SetScopes(request.GetScopes());

        // Resolve the resource servers (audiences) for the granted scopes.
        // Without this, the access token has no "aud" claim and any resource
        // server that calls AddAudiences() will reject it with 401.
        var resources = new List<string>();
        await foreach (var resource in _scopeManager.ListResourcesAsync(identity.GetScopes()))
            resources.Add(resource);
        identity.SetResources(resources);

        foreach (var claim in identity.Claims)
            claim.SetDestinations(GetDestinations(claim, identity));

        return SignIn(
            new ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IEnumerable<string> GetDestinations(Claim claim, ClaimsIdentity identity)
    {
        switch (claim.Type)
        {
            case Claims.Name:
            case Claims.GivenName:
            case Claims.FamilyName:
                yield return Destinations.AccessToken;
                if (identity.HasScope(Scopes.Profile))
                    yield return Destinations.IdentityToken;
                yield break;
            case Claims.Email:
                yield return Destinations.AccessToken;
                if (identity.HasScope(Scopes.Email))
                    yield return Destinations.IdentityToken;
                yield break;
            case Claims.Role:
                yield return Destinations.AccessToken;
                if (identity.HasScope(Scopes.Roles))
                    yield return Destinations.IdentityToken;
                yield break;
            case "permission":
                yield return Destinations.AccessToken;
                yield break;
            case "centre_primary":
            case "centres":
                yield return Destinations.AccessToken;
                yield break;
            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
