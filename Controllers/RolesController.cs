using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RubacCore.Authorization;
using RubacCore.Dtos;
using RubacCore.Interfaces;

// ── Role management vs. Role assignment ─────────────────────────────────────────
//
//  This controller manages ROLE DEFINITIONS (what roles exist in the database).
//  Assigning roles TO users lives in UsersController (POST /api/users/assign-role).
//
//  Separation of concerns:
//    RolesController  → "what buckets exist?"   (CRUD on role definitions)
//    UsersController  → "who is in which bucket?" (assign/remove roles on users)
//
//  This mirrors how a typical admin UI works: one page manages available roles,
//  another page manages individual user memberships.
// ───────────────────────────────────────────────────────────────────────────────

namespace RubacCore.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(Policy = Policies.ManageRoles)] // all endpoints: SuperAdmin only
public class RolesController : ControllerBase
{
    private readonly IRoleRepository _roleRepository;

    public RolesController(IRoleRepository roleRepository)
        => _roleRepository = roleRepository;

    /// <summary>
    /// List all role definitions across all applications.
    /// Each role has an Application field so you can filter in the front-end
    /// (e.g. show only DashboardCore roles when managing DashboardCore users).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await _roleRepository.GetAllAsync());

    /// <summary>
    /// Get a single role by its normalised name.
    /// Useful to check if a role exists before assigning it to a user.
    /// </summary>
    [HttpGet("{name}")]
    public async Task<IActionResult> GetByName(string name)
    {
        var role = await _roleRepository.GetByNameAsync(name);
        return role is null ? NotFound() : Ok(role);
    }

    /// <summary>
    /// Create a new role definition.
    ///
    /// Best practice: include the Application name so different apps can
    /// have same-named roles without confusion:
    ///   POST { "name": "Admin", "application": "DashboardCore" }
    ///   POST { "name": "Admin", "application": "InvoicingApp"  }
    ///
    /// These are stored as separate rows; Identity normalises names per-row.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateRoleDto dto)
    {
        try
        {
            var role = await _roleRepository.CreateAsync(dto);
            return CreatedAtAction(nameof(GetByName), new { name = role.Name }, role);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a role definition.
    ///
    /// WARNING: Identity will also remove this role from ALL users who have it.
    /// This is a cascading operation. Deactivating users with that role first
    /// is recommended if you want to audit who was affected before deletion.
    /// </summary>
    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete(string name)
    {
        var success = await _roleRepository.DeleteAsync(name);
        return success ? Ok($"Role '{name}' deleted.") : NotFound();
    }
}
