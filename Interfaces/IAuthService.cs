using RubacCore.Dtos;

namespace RubacCore.Interfaces;

public interface IAuthService
{
    Task<bool> ValidateCredentialsAsync(string userName, string password);
    Task<IEnumerable<string>> GetUserRolesAsync(string userName);
    Task<UserDto?> GetUserByNameAsync(string userName);
    Task<UserDto?> GetUserByIdAsync(string userId);

    /// <summary>
    /// Returns the role names assigned to a user that are scoped to a specific
    /// application client, plus any global roles (Application == null).
    /// This ensures that a token issued for "rubac-admin" only contains roles
    /// created for that client and does not leak roles from other applications.
    /// </summary>
    Task<IEnumerable<string>> GetRolesForClientAsync(long userId, string clientId);

    /// <summary>
    /// Returns all permission names for roles held by a user that are scoped to a
    /// specific application client (same application-filter logic as GetRolesForClientAsync).
    /// These are emitted as <c>permission</c> claims in the issued access token.
    /// </summary>
    Task<IEnumerable<string>> GetPermissionsForClientAsync(long userId, string clientId);

    /// <summary>
    /// Returns the primary centre code and all assigned centre codes for a user.
    /// Used to populate <c>centre_primary</c> and <c>centres</c> JWT claims.
    /// </summary>
    Task<(string? Primary, IEnumerable<string> All)> GetUserCentresAsync(long userId);
}
