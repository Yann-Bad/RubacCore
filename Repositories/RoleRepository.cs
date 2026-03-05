using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RubacCore.Dtos;
using RubacCore.Interfaces;
using RubacCore.Models;

namespace RubacCore.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly RoleManager<ApplicationRole> _roleManager;

    public RoleRepository(RoleManager<ApplicationRole> roleManager)
        => _roleManager = roleManager;

    public async Task<IEnumerable<RoleDto>> GetAllAsync()
        => await _roleManager.Roles
            .Select(r => new RoleDto(r.Id, r.Name!, r.Description, r.Application))
            .ToListAsync();

    public async Task<PagedResult<RoleDto>> GetPagedAsync(int page, int pageSize, string? search)
    {
        var query = _roleManager.Roles.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(r =>
                (r.Name        != null && r.Name.ToLower().Contains(s)) ||
                (r.Application != null && r.Application.ToLower().Contains(s)));
        }

        var totalCount = await query.CountAsync();
        var roles = await query
            .OrderBy(r => r.Name)
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

        return new RoleDto(role.Id, role.Name!, role.Description, role.Application);
    }

    public async Task<bool> DeleteAsync(string name)
    {
        var role = await _roleManager.FindByNameAsync(name);
        if (role is null) return false;

        var result = await _roleManager.DeleteAsync(role);
        return result.Succeeded;
    }
}
