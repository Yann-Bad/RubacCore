using RubacCore.Dtos;

namespace RubacCore.Interfaces;

public interface IRoleRepository
{
    Task<IEnumerable<RoleDto>> GetAllAsync();
    Task<PagedResult<RoleDto>> GetPagedAsync(int page, int pageSize, string? search, string? sortBy = "name", string? sortDir = "asc");
    Task<RoleDto?> GetByNameAsync(string name);
    Task<RoleDto> CreateAsync(CreateRoleDto dto);
    Task<RoleDto?> UpdateAsync(string name, UpdateRoleDto dto);
    Task<bool> DeleteAsync(string name);
}
