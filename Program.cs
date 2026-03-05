using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using RubacCore.Authorization;
using RubacCore.Data;
using RubacCore.Interfaces;
using RubacCore.Models;
using RubacCore.Repositories;
using RubacCore.Services;
using RubacCore.Workers;

var builder = WebApplication.CreateBuilder(args);

// ── 1. Database (SQL Server) ───────────────────────────────────────
builder.Services.AddDbContext<RubacDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("RubacConnection"));
    // Required by OpenIddict to store its entities in EF Core
    options.UseOpenIddict<long>();
});

// ── 2. ASP.NET Core Identity ───────────────────────────────────────
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.Password.RequiredLength         = 8;
    options.Password.RequireDigit           = true;
    options.Password.RequireUppercase       = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail         = true;
})
.AddEntityFrameworkStores<RubacDbContext>()
.AddDefaultTokenProviders();

// ── 3. OpenIddict (OAuth2 / OpenID Connect server) ─────────────────
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<RubacDbContext>()
               .ReplaceDefaultEntities<long>();
    })
    .AddServer(options =>
    {
        options.SetTokenEndpointUris("/connect/token")
               .SetAuthorizationEndpointUris("/connect/authorize")
               .SetUserinfoEndpointUris("/connect/userinfo")
               .SetIntrospectionEndpointUris("/connect/introspect")
               .SetLogoutEndpointUris("/connect/logout");

        options.AllowPasswordFlow()
               .AllowClientCredentialsFlow()
               .AllowRefreshTokenFlow()
               .AllowAuthorizationCodeFlow();

        // Dev: auto-generated ephemeral keys — use X509 certs in production
        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();

        // Emit access tokens as plain signed JWTs (JWS) rather than encrypted
        // JWEs. This is required for resource servers (e.g. DashboardCore) to
        // validate tokens using standard JWT Bearer without OpenIddict's SDK.
        // The token is still signed — tamper-proof — but its payload is
        // readable. Acceptable for internal APIs; add encryption in production
        // if the token payload is considered sensitive.
        options.DisableAccessTokenEncryption();

        options.UseAspNetCore()
               .EnableTokenEndpointPassthrough()
               .EnableAuthorizationEndpointPassthrough()
               .EnableUserinfoEndpointPassthrough()
               .EnableLogoutEndpointPassthrough()
                // Required when using the Angular proxy in development.
               // The proxy forwards requests over HTTP (localhost:5262).
               // In production this line is removed — HTTPS is enforced.
               .DisableTransportSecurityRequirement();

        // ── REMOVED DisableTransportSecurityRequirement() ──────
               // Now that `dotnet dev-certs https --trust` has been run,
               // the browser accepts the self-signed cert and HTTPS works.
               // Never disable transport security in production.

    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
        // Only accept tokens that carry the rubac_api audience.
        // This audience is added to tokens when the rubac scope is requested.
        options.AddAudiences("rubac_api");
    });

// ── 4. Repositories & Services (separated concerns) ────────────────
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();

// ── 5. Seed workers ────────────────────────────────────────────────
builder.Services.AddHostedService<OpenIddictSeedWorker>(); // registers OAuth2 apps & scopes
builder.Services.AddHostedService<DataSeedWorker>();       // seeds default roles & admin user

// ── 6. Controllers & OpenAPI ───────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// ── 7. Authorization policies (guards RubacCore’s own management API) ──────
//
// WHY does RubacCore need its own policies?
//  The auth server itself must be protected. Without this, any authenticated
//  user could call GET /api/users and list every account in the system.
//  Only SuperAdmin should manage users and roles.
// AddIdentity (step 2) internally sets DefaultAuthenticateScheme and
// DefaultChallengeScheme to Identity.Application (cookies). Calling
// AddAuthentication(scheme) here only sets DefaultScheme, which is used
// as fallback ONLY when the specific Authenticate/Challenge schemes are null.
// We must explicitly override all four to make [Authorize] on API controllers
// use OpenIddict bearer token validation instead of cookie auth.
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme            = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme   = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    options.DefaultForbidScheme      = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.ManageUsers, policy =>
        policy.RequireRole("SuperAdmin"));

    options.AddPolicy(Policies.ManageRoles, policy =>
        policy.RequireRole("SuperAdmin"));

    options.AddPolicy(Policies.SelfService, policy =>
        policy.RequireAuthenticatedUser());
});

// ── 8. CORS ──────────────────────────────────────────────────────────
// AllowAnyOrigin() does NOT work with AllowCredentials().
// Use explicit origins so the browser's preflight OPTIONS request
// receives the correct Access-Control-Allow-Origin header.
builder.Services.AddCors(options =>
    options.AddPolicy("AllowFront", policy =>
        policy
            .WithOrigins(
                "http://localhost:4200",   // Angular DashboardFront (ng serve)
                "https://localhost:4200",
                "http://localhost:4300",   // Angular RulesBacAdmin (ng serve)
                "https://localhost:4300"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// CORS must come BEFORE UseAuthentication — otherwise the browser's
// preflight OPTIONS request is rejected before the CORS headers are added.
app.UseCors("AllowFront");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
