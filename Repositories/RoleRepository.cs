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
