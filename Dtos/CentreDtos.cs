using RubacCore.Models;

namespace RubacCore.Dtos;

public record CentreDto(
    int                   Id,
    string?               Code,
    string?               Name,
    bool                  IsActive,
    CodeSubdivisionCentre SubdivisionAdministrative,
    int?                  ParentId,
    string?               ParentName);

public record CentreTreeDto(
    int                   Id,
    string?               Code,
    string?               Name,
    bool                  IsActive,
    CodeSubdivisionCentre SubdivisionAdministrative,
    int?                  ParentId,
    List<CentreTreeDto>   Children);

public record CreateCentreRequest(
    string?               Code,
    string?               Name,
    bool                  IsActive,
    CodeSubdivisionCentre SubdivisionAdministrative,
    int?                  ParentId);

public record UpdateCentreRequest(
    string?               Code,
    string?               Name,
    bool                  IsActive,
    CodeSubdivisionCentre SubdivisionAdministrative,
    int?                  ParentId);

public record AssignUserCentreRequest(long UserId, int CentreId, bool IsPrimary);

public record CentreUserDto(
    long    UserId,
    string  UserName,
    string? Email,
    bool    IsPrimary);

public record UserCentreAssignmentDto(
    int                   CentreId,
    string?               Code,
    string?               Name,
    CodeSubdivisionCentre SubdivisionAdministrative,
    bool                  IsPrimary);
