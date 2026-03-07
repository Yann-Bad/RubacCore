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

public record PermissionDto(
    long Id,
    string Name,
    string? Description,
    string Application
);

public record CreatePermissionDto(
    string Name,
    string? Description,
    string Application
);

public record AssignPermissionDto(
    long PermissionId
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
/// Payload for PATCH /api/users/{id}/active.
/// A wrapper object is required because ASP.NET Core's JSON binder cannot
/// deserialize a raw primitive (true/false) into a [FromBody] parameter.
/// </summary>
public record SetActiveDto(bool IsActive);

/// <summary>
/// Payload for PATCH /api/users/{id}/password.
/// Used by SuperAdmin to force-reset any user's password without knowing the current one.
/// </summary>
public record ResetPasswordDto(string NewPassword);

/// <summary>
/// Payload for PUT /api/roles/{name}.
/// Name cannot be changed (it is the identity key). Only metadata fields are editable.
/// </summary>
public record UpdateRoleDto(string? Description, string? Application);

/// <summary>
/// Payload for a SuperAdmin to reset any user's password, or for a user
/// changing their own (CurrentPassword is validated only in self-service flow).
/// </summary>
public record ChangePasswordDto(
    string? CurrentPassword,   // null when a SuperAdmin resets for someone else
    string NewPassword
);

/// <summary>
/// Represents an OAuth2/OIDC application that has been granted to a user.
/// </summary>
public record UserApplicationDto(
    string ClientId,
    string? DisplayName
);

/// <summary>
/// Payload to assign or revoke an application from a user.
/// </summary>
public record AssignUserApplicationDto(
    long   UserId,
    string ApplicationClientId
);

/// <summary>
/// Generic wrapper returned by all paginated list endpoints.
/// </summary>
/// <param name="Items">The items on the current page.</param>
/// <param name="TotalCount">Total number of matching records across all pages.</param>
/// <param name="Page">Current 1-based page number.</param>
/// <param name="PageSize">Number of items per page.</param>
public record PagedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize
);

/// <summary>Read model returned by GET /api/audit.</summary>
public record AuditLogDto(
    long             Id,
    DateTimeOffset   OccurredAt,
    string           Actor,
    string           Entity,
    string           Action,
    string?          TargetId,
    string?          Details
);

// ── OAuth2 client DTOs ─────────────────────────────────────────────────────

/// <summary>Read model returned by GET /api/clients.</summary>
public record ClientDto(
    string   ClientId,
    string?  DisplayName,
    string   ClientType,   // "public" | "confidential"
    string[] Permissions,
    string[] RedirectUris
);

/// <summary>Payload for POST /api/clients.</summary>
public record CreateClientDto(
    string   ClientId,
    string?  DisplayName,
    string   ClientType,
    string?  ClientSecret,   // required when ClientType == "confidential"
    string[] Permissions,
    string[] RedirectUris
);

/// <summary>Payload for PUT /api/clients/{clientId}.</summary>
public record UpdateClientDto(
    string?  DisplayName,
    string?  ClientSecret,   // null = keep existing; non-null = update
    string[] Permissions,
    string[] RedirectUris
);
