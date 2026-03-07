using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RubacCore.Authorization;
using RubacCore.Data;
using RubacCore.Dtos;
using RubacCore.Models;

namespace RubacCore.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(Policy = Policies.ManageRoles)]
public class PermissionsController : ControllerBase
{
    private readonly RubacDbContext _db;

    public PermissionsController(RubacDbContext db) => _db = db;

    // ── GET /api/permissions ──────────────────────────────────────────────
    /// <summary>Returns all permissions, optionally filtered by application.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? application = null)
    {
        var query = _db.Permissions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(application))
            query = query.Where(p => p.Application == application);

        var list = await query
            .OrderBy(p => p.Application)
            .ThenBy(p => p.Name)
            .Select(p => new PermissionDto(p.Id, p.Name, p.Description, p.Application))
            .ToListAsync();

        return Ok(list);
    }

    // ── GET /api/permissions/{id} ─────────────────────────────────────────
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var p = await _db.Permissions.FindAsync(id);
        return p is null ? NotFound() : Ok(new PermissionDto(p.Id, p.Name, p.Description, p.Application));
    }

    // ── POST /api/permissions ─────────────────────────────────────────────
    /// <summary>Creates a new permission.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreatePermissionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { error = "Permission name is required." });
        if (string.IsNullOrWhiteSpace(dto.Application))
            return BadRequest(new { error = "Application is required." });

        var exists = await _db.Permissions.AnyAsync(p => p.Name == dto.Name);
        if (exists)
            return BadRequest(new { error = $"Permission '{dto.Name}' already exists." });

        var permission = new Permission
        {
            Name        = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            Application = dto.Application.Trim()
        };

        _db.Permissions.Add(permission);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = permission.Id },
            new PermissionDto(permission.Id, permission.Name, permission.Description, permission.Application));
    }

    // ── DELETE /api/permissions/{id} ──────────────────────────────────────
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var p = await _db.Permissions.FindAsync(id);
        if (p is null) return NotFound();

        _db.Permissions.Remove(p);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── GET /api/permissions/role/{roleId} ────────────────────────────────
    /// <summary>Returns all permissions currently assigned to a role.</summary>
    [HttpGet("role/{roleId:long}")]
    public async Task<IActionResult> GetForRole(long roleId)
    {
        var list = await _db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p)
            .OrderBy(p => p.Application)
            .ThenBy(p => p.Name)
            .Select(p => new PermissionDto(p.Id, p.Name, p.Description, p.Application))
            .ToListAsync();

        return Ok(list);
    }

    // ── POST /api/permissions/role/{roleId} ───────────────────────────────
    /// <summary>Assigns a permission to a role.</summary>
    [HttpPost("role/{roleId:long}")]
    public async Task<IActionResult> AssignToRole(long roleId, AssignPermissionDto dto)
    {
        var roleExists = await _db.Roles.AnyAsync(r => r.Id == roleId);
        if (!roleExists) return NotFound(new { error = "Role not found." });

        var permExists = await _db.Permissions.AnyAsync(p => p.Id == dto.PermissionId);
        if (!permExists) return NotFound(new { error = "Permission not found." });

        var alreadyAssigned = await _db.RolePermissions
            .AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == dto.PermissionId);
        if (alreadyAssigned)
            return BadRequest(new { error = "Permission already assigned to this role." });

        _db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = dto.PermissionId });
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── DELETE /api/permissions/role/{roleId}/{permissionId} ─────────────
    /// <summary>Removes a permission assignment from a role.</summary>
    [HttpDelete("role/{roleId:long}/{permissionId:long}")]
    public async Task<IActionResult> RemoveFromRole(long roleId, long permissionId)
    {
        var rp = await _db.RolePermissions
            .FirstOrDefaultAsync(x => x.RoleId == roleId && x.PermissionId == permissionId);

        if (rp is null) return NotFound();

        _db.RolePermissions.Remove(rp);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
