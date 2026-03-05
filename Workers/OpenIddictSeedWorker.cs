using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace RubacCore.Workers;

/// <summary>
/// Registers OAuth2 client applications and API scopes in OpenIddict's
/// database tables on every startup (create-or-recreate pattern).
///
/// PERMISSION RULES — why ID2052 (invalid_scope) occurs:
///  Every scope the Angular app sends in the "scope" parameter MUST have a
///  matching permission entry on the client AND (for non-built-in scopes)
///  a row in the OpenIddictScopes table.
///
///  Built-in scopes handled by OpenIddict internally (no DB row needed):
///    openid, offline_access
///
///  Scopes that look "standard" but still need explicit client permissions
///  AND a DB row in OpenIddictScopes:
///    profile, email, roles   ← use Permissions.Scopes.* typed constants
///
///  Custom scopes (API audiences):
///    dashboard               ← use Permissions.Prefixes.Scope + "dashboard"
///
/// WHY delete + recreate for applications instead of PopulateAsync + UpdateAsync?
///  PopulateAsync only sets properties present in the descriptor, silently
///  nulling required fields (e.g. ClientType) that are missing. This causes
///  the UpdateAsync validation to fail with "client type cannot be null".
///  Delete + recreate guarantees the DB row always exactly matches the code.
/// </summary>
public class OpenIddictSeedWorker : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OpenIddictSeedWorker> _logger;

    public OpenIddictSeedWorker(IServiceProvider serviceProvider,
                                ILogger<OpenIddictSeedWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger          = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();

        var appManager   = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        await SeedScopesAsync(scopeManager, cancellationToken);
        await SeedApplicationsAsync(appManager, cancellationToken);
    }

    // ── Scopes ─────────────────────────────────────────────────────────

    private async Task SeedScopesAsync(
        IOpenIddictScopeManager manager,
        CancellationToken cancellationToken)
    {
        // ── OpenIddict scope validation rules ──────────────────────────────
        // Every scope a client requests is validated in two steps:
        //   1. The client must have Permissions.Prefixes.Scope + "xxx" permission.
        //   2. The scope must be "supported" — either built-in OR present in
        //      the OpenIddictScopes table.
        //
        // Built-in (no DB row needed): openid, offline_access
        // Everything else — including standard OIDC scopes like profile and
        // email — requires a row in OpenIddictScopes even if they look standard.

        // "profile" — name, given_name, family_name claims
        await UpsertScopeAsync(manager, new OpenIddictScopeDescriptor
        {
            Name        = "profile",
            DisplayName = "User profile",
        }, cancellationToken);

        // "email" — email claim
        await UpsertScopeAsync(manager, new OpenIddictScopeDescriptor
        {
            Name        = "email",
            DisplayName = "Email address",
        }, cancellationToken);

        // "roles" — role claims in the access token
        await UpsertScopeAsync(manager, new OpenIddictScopeDescriptor
        {
            Name        = "roles",
            DisplayName = "User roles",
        }, cancellationToken);

        // "dashboard" — grants access to DashboardCore.
        // Resources becomes the "aud" claim in the token.
        // DashboardCore validates it via: options.AddAudiences("dashboard_api")
        await ForceRecreateScopeAsync(manager, new OpenIddictScopeDescriptor
        {
            Name        = "dashboard",
            DisplayName = "Dashboard API access",
            Resources   = { "dashboard_api" }
        }, cancellationToken);

        // "rubac" — grants access to RubacCore's own management API.
        // Without this, rubac-admin tokens have no aud claim and
        // OpenIddict's UseLocalServer() validation rejects them with 401.
        await ForceRecreateScopeAsync(manager, new OpenIddictScopeDescriptor
        {
            Name        = "rubac",
            DisplayName = "RubacCore API access",
            Resources   = { "rubac_api" }
        }, cancellationToken);
    }

    // ── Applications ───────────────────────────────────────────────────

    private async Task SeedApplicationsAsync(
        IOpenIddictApplicationManager manager,
        CancellationToken cancellationToken)
    {
        // ── dashboard-api (DashboardCore — confidential, client credentials) ──
        // Server-side only — has a secret. Used by DashboardCore to call
        // /connect/introspect and validate tokens presented by the Angular SPA.
        await RecreateAppAsync(manager, new OpenIddictApplicationDescriptor
        {
            ClientId     = "dashboard-api",
            ClientSecret = "dashboard-api-secret-change-in-prod",
            ClientType   = ClientTypes.Confidential,
            DisplayName  = "Dashboard API",
            Permissions  =
            {
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Introspection, // required for token validation
                Permissions.GrantTypes.ClientCredentials,
                Permissions.Prefixes.Scope + "dashboard"
            }
        }, cancellationToken);

        // ── dashboard-front (Angular SPA — public, password + refresh) ──────
        // Public client: no secret (cannot be kept secret in browser JavaScript).
        //
        // ⚠️  EVERY scope in Angular's login call MUST appear here:
        //       scope = "openid profile email roles dashboard offline_access"
        //                ──────┬──────────────────────────────────────────
        //                      └─ each token must have a matching permission below
        //
        // Use Permissions.Scopes.* typed constants for the well-known scopes
        // (profile, email, roles) — they expand to the same string as
        // Permissions.Prefixes.Scope + "xxx" but are less error-prone.
        await RecreateAppAsync(manager, new OpenIddictApplicationDescriptor
        {
            ClientId    = "dashboard-front",
            ClientType  = ClientTypes.Public,
            DisplayName = "Dashboard Frontend",
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Logout,

                // Grant types
                Permissions.GrantTypes.Password,      // username + password → access token
                Permissions.GrantTypes.RefreshToken,  // silent renewal via refresh token

                // Well-known OIDC scopes — use typed constants to avoid typos
                Permissions.Prefixes.Scope + "openid", // sub claim + id_token
                Permissions.Scopes.Profile,             // name, given_name, family_name
                Permissions.Scopes.Email,               // email claim
                Permissions.Scopes.Roles,               // role claims in access token

                // Custom scopes
                Permissions.Prefixes.Scope + "dashboard",      // DashboardCore API access
                Permissions.Prefixes.Scope + "offline_access", // enables refresh tokens
            }
        }, cancellationToken);

        // ── rubac-admin (RulesBacAdmin SPA — public, password + refresh) ────
        await RecreateAppAsync(manager, new OpenIddictApplicationDescriptor
        {
            ClientId    = "rubac-admin",
            ClientType  = ClientTypes.Public,
            DisplayName = "RulesBac Admin Frontend",
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Logout,
                Permissions.GrantTypes.Password,
                Permissions.GrantTypes.RefreshToken,
                Permissions.Prefixes.Scope + "openid",
                Permissions.Scopes.Profile,
                Permissions.Scopes.Email,
                Permissions.Scopes.Roles,
                Permissions.Prefixes.Scope + "rubac",         // rubac_api audience in the token
                Permissions.Prefixes.Scope + "offline_access",
            }
        }, cancellationToken);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the scope if it doesn't exist, updates it if it does.
    /// Safe to use with PopulateAsync because scopes have no required fields
    /// that can be accidentally nulled out (unlike ClientType on applications).
    /// Do NOT use this for scopes that have Resources — use ForceRecreateScopeAsync.
    /// </summary>
    private async Task UpsertScopeAsync(
        IOpenIddictScopeManager manager,
        OpenIddictScopeDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var existing = await manager.FindByNameAsync(descriptor.Name!, cancellationToken);
        if (existing is null)
        {
            await manager.CreateAsync(descriptor, cancellationToken);
            _logger.LogInformation("[OpenIddict] Created scope: {Name}", descriptor.Name);
        }
        else
        {
            await manager.PopulateAsync(existing, descriptor, cancellationToken);
            await manager.UpdateAsync(existing, cancellationToken);
            _logger.LogInformation("[OpenIddict] Updated scope: {Name}", descriptor.Name);
        }
    }

    /// <summary>
    /// Deletes the scope if it exists, then recreates it from the descriptor.
    /// Use this instead of UpsertScopeAsync for scopes that carry Resources,
    /// because PopulateAsync+UpdateAsync does not reliably update the Resources
    /// JSON column when the row was originally created without Resources.
    /// </summary>
    private async Task ForceRecreateScopeAsync(
        IOpenIddictScopeManager manager,
        OpenIddictScopeDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var existing = await manager.FindByNameAsync(descriptor.Name!, cancellationToken);
        if (existing is not null)
        {
            await manager.DeleteAsync(existing, cancellationToken);
            _logger.LogInformation("[OpenIddict] Deleted scope for recreation: {Name}", descriptor.Name);
        }
        await manager.CreateAsync(descriptor, cancellationToken);
        _logger.LogInformation("[OpenIddict] Created scope: {Name}", descriptor.Name);
    }

    /// <summary>
    /// Creates the application if it doesn't exist, or updates it in place.
    /// Uses PopulateAsync + UpdateAsync — avoids DeleteAsync which calls
    /// EF Core's ExecuteDeleteAsync internally. That method changed signature
    /// between EF Core 8 and EF Core 10, causing a MissingMethodException
    /// at runtime when OpenIddict 5.5.0 (compiled against EF Core 8) runs
    /// on top of EF Core 10.
    ///
    /// REQUIREMENT: every field that has a NOT NULL constraint in OpenIddict's
    /// validation (e.g. ClientType) MUST be present in the descriptor,
    /// otherwise PopulateAsync will null it out and UpdateAsync will throw.
    /// </summary>
    private async Task RecreateAppAsync(
        IOpenIddictApplicationManager manager,
        OpenIddictApplicationDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var existing = await manager.FindByClientIdAsync(
            descriptor.ClientId!, cancellationToken);

        if (existing is null)
        {
            await manager.CreateAsync(descriptor, cancellationToken);
            _logger.LogInformation("[OpenIddict] Created app: {ClientId}", descriptor.ClientId);
        }
        else
        {
            await manager.PopulateAsync(existing, descriptor, cancellationToken);
            await manager.UpdateAsync(existing, cancellationToken);
            _logger.LogInformation("[OpenIddict] Updated app: {ClientId}", descriptor.ClientId);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
