using Microsoft.AspNetCore.Identity;
using RubacCore.Models;

namespace RubacCore.Workers;

/// <summary>
/// Seeds default roles and an admin user into the database on first startup.
///
/// WHY a seed worker instead of manual setup?
///  - Every new environment (dev, staging, prod) gets the same baseline automatically
///  - No manual SQL scripts to maintain
///  - Idempotent: safe to run on every startup (checks before inserting)
///
/// ROLE HIERARCHY:
///  SuperAdmin   — cross-application god mode (manage users, roles, clients)
///  Admin        — full access to a specific application
///  Manager      — create/update within a specific application (no delete)
///  Consultant   — read-only access to a specific application
///
/// The "Application" field on a role lets RubacCore track which system each role
/// belongs to. This means two apps can both have an "Admin" role without conflict:
///   - Admin (Application="DashboardCore")  → controls DashboardCore
///   - Admin (Application="OtherApp")       → controls OtherApp
/// </summary>
public class DataSeedWorker : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DataSeedWorker> _logger;

    public DataSeedWorker(IServiceProvider serviceProvider, ILogger<DataSeedWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger          = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // ── Default roles ──────────────────────────────────────────
        // Each role is scoped to an Application so multiple apps can have
        // their own "Admin" without overlap.
        //
        // NOTE: Admin / Manager / Consultant apply to the full Dashboard
        // suite — both DashboardCore (API authorization policies) and
        // DashboardFront (UI visibility / route guards). They are stored
        // under Application = "Dashboard" to make that explicit.
        var roles = new[]
        {
            // Application = null → included in tokens for ANY client.
            // SuperAdmin manages the auth server itself; scoping it to "RubacCore"
            // would make GetRolesForClientAsync strip it from rubac-admin tokens.
            new ApplicationRole { Name = "SuperAdmin",  Description = "Manages users, roles and OAuth2 clients in RubacCore", Application = null },

            // Dashboard suite roles — enforced server-side by DashboardCore policies
            // AND client-side by Angular guards / template conditionals.
            new ApplicationRole { Name = "Admin",       Description = "Full access to Dashboard (API + frontend)",            Application = "Dashboard" },
            new ApplicationRole { Name = "Manager",     Description = "Create and update in Dashboard (API + frontend)",      Application = "Dashboard" },
            new ApplicationRole { Name = "Consultant",  Description = "Read-only access to Dashboard (API + frontend)",       Application = "Dashboard" },
        };

        foreach (var role in roles)
        {
            var existing = await roleManager.FindByNameAsync(role.Name!);
            if (existing is null)
            {
                // First run — create
                await roleManager.CreateAsync(role);
                _logger.LogInformation("Seeded role: {Role} ({App})", role.Name, role.Application);
            }
            else if (existing.Application != role.Application || existing.Description != role.Description)
            {
                // Subsequent runs — sync description and application if they changed
                existing.Application  = role.Application;
                existing.Description  = role.Description;
                await roleManager.UpdateAsync(existing);
                _logger.LogInformation("Updated role: {Role} ({App})", role.Name, role.Application);
            }
        }

        // ── Default admin user ─────────────────────────────────────
        // This user gets both SuperAdmin (manages RubacCore) and Admin (full
        // access to DashboardCore). In production you should change the password
        // immediately and ideally delete this seeded account.
        const string adminEmail    = "admin@rubac.local";
        const string adminPassword = "Admin@1234";

        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new ApplicationUser
            {
                UserName       = "admin",
                Email          = adminEmail,
                FirstName      = "Super",
                LastName       = "Admin",
                IsActive       = true,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, "SuperAdmin");
                await userManager.AddToRoleAsync(admin, "Admin");
                _logger.LogInformation("Seeded admin user: {Email}", adminEmail);
            }
            else
            {
                _logger.LogError("Failed to seed admin user: {Errors}",
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

