using RubacCore.Dtos;

namespace RubacCore.Interfaces;

public interface IRoleRepository
{
    Task<IEnumerable<RoleDto>> GetAllAsync();
    Task<RoleDto?> GetByNameAsync(string name);
    Task<RoleDto> CreateAsync(CreateRoleDto dto);
    Task<bool> DeleteAsync(string name);
}
