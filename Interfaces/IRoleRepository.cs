using RubacCore.Dtos;

namespace RubacCore.Interfaces;

public interface IRoleRepository
{
    Task<IEnumerable<RoleDto>> GetAllAsync();
    Task<PagedResult<RoleDto>> GetPagedAsync(int page, int pageSize, string? search);
    Task<RoleDto?> GetByNameAsync(string name);
    Task<RoleDto> CreateAsync(CreateRoleDto dto);
    Task<bool> DeleteAsync(string name);
}
