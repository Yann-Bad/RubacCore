using RubacCore.Dtos;
using RubacCore.Models;

namespace RubacCore.Interfaces;

public interface ICentreService
{
    Task<IEnumerable<CentreDto>> GetAllAsync();
    Task<CentreTreeDto?>         GetTreeAsync();
    Task<CentreDto?>             GetByIdAsync(int id);
    Task<CentreDto>              CreateAsync(CreateCentreRequest request);
    Task<CentreDto?>             UpdateAsync(int id, UpdateCentreRequest request);
    Task<bool>                   DeleteAsync(int id);

    /// <summary>Returns the Centre codes assigned to a user (primary first).</summary>
    Task<(string? Primary, IEnumerable<string> All)> GetUserCentresAsync(long userId);

    Task<PagedResult<CentreUserDto>> GetCentreUsersAsync(int centreId, int page, int pageSize, string? search);
    Task<IEnumerable<UserCentreAssignmentDto>> GetCentresForUserAsync(long userId);
    Task AssignUserCentreAsync(AssignUserCentreRequest request);
    Task RemoveUserCentreAsync(long userId, int centreId);

    /// <summary>
    /// Looks up a centre by code and creates a primary UserCentre link.
    /// Used by EnsureLdapUserAsync to auto-assign from AD physicalDeliveryOfficeName.
    /// </summary>
    Task TryAssignCentreByCodeAsync(long userId, string centreCode);
}
