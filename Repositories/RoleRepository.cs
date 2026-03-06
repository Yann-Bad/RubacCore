using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RubacCore.Dtos;
using RubacCore.Interfaces;
using RubacCore.Models;

namespace RubacCore.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IAuditService                _audit;
    private readonly IHttpContextAccessor         _http;

    public RoleRepository(
        RoleManager<ApplicationRole> roleManager,
        IAuditService audit,
        IHttpContextAccessor http)
    {
        _roleManager = roleManager;
        _audit       = audit;
        _http        = http;
    }

    private string Actor =>
        _http.HttpContext?.User.Identity?.Name ?? "system";

    public async Task<IEnumerable<RoleDto>> GetAllAsync()
        => await _roleManager.Roles
            .Select(r => new RoleDto(r.Id, r.Name!, r.Description, r.Application))
            .ToListAsync();

    public async Task<PagedResult<RoleDto>> GetPagedAsync(int page, int pageSize, string? search, string? sortBy = "name", string? sortDir = "asc")
    {
        var query = _roleManager.Roles.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(r =>
                (r.Name        != null && r.Name.ToLower().Contains(s)) ||
                (r.Application != null && r.Application.ToLower().Contains(s)));
        }

        bool desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = sortBy?.ToLower() switch
        {
            "application" => desc ? query.OrderByDescending(r => r.Application) : query.OrderBy(r => r.Application),
            "description" => desc ? query.OrderByDescending(r => r.Description) : query.OrderBy(r => r.Description),
            _             => desc ? query.OrderByDescending(r => r.Name)         : query.OrderBy(r => r.Name),
        };

        var totalCount = await query.CountAsync();
        var roles = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new RoleDto(r.Id, r.Name!, r.Description, r.Application))
            .ToListAsync();

        return new PagedResult<RoleDto>(roles, totalCount, page, pageSize);
    }

    public async Task<RoleDto?> GetByNameAsync(string name)
    {
        var role = await _roleManager.FindByNameAsync(name);
        return role is null ? null : new RoleDto(role.Id, role.Name!, role.Description, role.Application);
    }

    public async Task<RoleDto> CreateAsync(CreateRoleDto dto)
    {
        var role = new ApplicationRole
        {
            Name        = dto.Name,
            Description = dto.Description,
            Application = dto.Application
        };

        var result = await _roleManager.CreateAsync(role);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                string.Join(", ", result.Errors.Select(e => e.Description)));

        await _audit.LogAsync(Actor, "Role", "role.created",
            role.Name, $"Application: {role.Application}");

        return new RoleDto(role.Id, role.Name!, role.Description, role.Application);
    }

    public async Task<bool> DeleteAsync(string name)
    {
        var role = await _roleManager.FindByNameAsync(name);
        if (role is null) return false;

        var result = await _roleManager.DeleteAsync(role);
        if (result.Succeeded)
            await _audit.LogAsync(Actor, "Role", "role.deleted", name);
        return result.Succeeded;
    }

    public async Task<RoleDto?> UpdateAsync(string name, UpdateRoleDto dto)
    {
        var role = await _roleManager.FindByNameAsync(name);
        if (role is null) return null;

        role.Description = dto.Description;
        role.Application = dto.Application;

        var result = await _roleManager.UpdateAsync(role);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                string.Join(", ", result.Errors.Select(e => e.Description)));

        await _audit.LogAsync(Actor, "Role", "role.updated",
            role.Name, $"Application: {role.Application}, Description: {role.Description}");

        return new RoleDto(role.Id, role.Name!, role.Description, role.Application);
    }
}
