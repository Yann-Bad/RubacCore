namespace RubacCore.Dtos;

public record RegisterDto(
    string UserName,
    string Email,
    string Password,
    string? FirstName,
    string? LastName
);

public record LoginDto(
    string UserName,
    string Password
);

public record CreateRoleDto(
    string Name,
    string? Description,
    string? Application
);

public record AssignRoleDto(
    long UserId,
    string RoleName
);

public record UserDto(
    long Id,
    string UserName,
    string Email,
    string? FirstName,
    string? LastName,
    bool IsActive,
    IEnumerable<string> Roles
);

public record RoleDto(
    long Id,
    string Name,
    string? Description,
    string? Application
);

/// <summary>
/// Payload for updating a user's profile fields (name, email).
/// Password is intentionally excluded — use ChangePasswordDto for that.
/// Separating concerns avoids accidentally clearing the password when editing a name.
/// </summary>
public record UpdateUserDto(
    string? FirstName,
    string? LastName,
    string? Email
);

/// <summary>
/// Payload for a SuperAdmin to reset any user's password, or for a user
/// changing their own (CurrentPassword is validated only in self-service flow).
/// </summary>
public record ChangePasswordDto(
    string? CurrentPassword,   // null when a SuperAdmin resets for someone else
    string NewPassword
);
