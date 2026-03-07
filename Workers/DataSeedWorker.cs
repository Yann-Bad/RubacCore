using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RubacCore.Data;
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
        var db          = scope.ServiceProvider.GetRequiredService<RubacDbContext>();

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
            // Application = "RubacCore" → only included in tokens for the client
            // whose client_id is "RubacCore" (the RulesBacAdmin SPA).
            // This prevents the SuperAdmin role from leaking into Dashboard or other app tokens.
            new ApplicationRole { Name = "SuperAdmin",  Description = "Manages users, roles and OAuth2 clients in RubacCore", Application = "RubacCore" },

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

        // ── Default permissions ────────────────────────────────────
        // Permissions follow "resource:action" naming and are scoped to an
        // Application that matches the Role.Application field.
        //
        // Default assignment matrix:
        //   SuperAdmin  → rubac:manage-users, rubac:manage-roles
        //   Admin       → dashboard:read, dashboard:write, dashboard:admin
        //   Manager     → dashboard:read, dashboard:write
        //   Consultant  → dashboard:read
        var defaultPermissions = new[]
        {
            new Permission { Name = "rubac:manage-users", Description = "Create, update and deactivate users in RubacCore", Application = "RubacCore" },
            new Permission { Name = "rubac:manage-roles", Description = "Create, update and assign roles in RubacCore",      Application = "RubacCore" },
            new Permission { Name = "dashboard:read",     Description = "View data in the Dashboard application",            Application = "Dashboard" },
            new Permission { Name = "dashboard:write",    Description = "Create and update records in Dashboard",            Application = "Dashboard" },
            new Permission { Name = "dashboard:admin",    Description = "Admin-level operations in Dashboard",               Application = "Dashboard" },
        };

        foreach (var perm in defaultPermissions)
        {
            if (!await db.Permissions.AnyAsync(p => p.Name == perm.Name))
            {
                db.Permissions.Add(perm);
                _logger.LogInformation("Seeded permission: {Perm} ({App})", perm.Name, perm.Application);
            }
        }
        await db.SaveChangesAsync();

        // ── Assign permissions to roles ────────────────────────────
        // Look up each role by name, then assign the matching permissions
        // using the join table. Idempotent: skip if already assigned.
        var assignmentMap = new Dictionary<string, string[]>
        {
            ["SuperAdmin"] = ["rubac:manage-users", "rubac:manage-roles"],
            ["Admin"]      = ["dashboard:read", "dashboard:write", "dashboard:admin"],
            ["Manager"]    = ["dashboard:read", "dashboard:write"],
            ["Consultant"] = ["dashboard:read"],
        };

        foreach (var (roleName, permNames) in assignmentMap)
        {
            var roleEntity = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
            if (roleEntity is null) continue;

            foreach (var permName in permNames)
            {
                var permEntity = await db.Permissions.FirstOrDefaultAsync(p => p.Name == permName);
                if (permEntity is null) continue;

                var already = await db.RolePermissions
                    .AnyAsync(rp => rp.RoleId == roleEntity.Id && rp.PermissionId == permEntity.Id);
                if (!already)
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = roleEntity.Id, PermissionId = permEntity.Id });
                    _logger.LogInformation("Assigned {Perm} → {Role}", permName, roleName);
                }
            }
        }
        await db.SaveChangesAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

