using RubacCore.Dtos;

namespace RubacCore.Interfaces;

public interface IAuthService
{
    Task<bool> ValidateCredentialsAsync(string userName, string password);
    Task<IEnumerable<string>> GetUserRolesAsync(string userName);
    Task<UserDto?> GetUserByNameAsync(string userName);
    Task<UserDto?> GetUserByIdAsync(string userId);
}
