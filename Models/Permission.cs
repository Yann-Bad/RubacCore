namespace RubacCore.Models;

/// <summary>
/// Represents a fine-grained permission that can be assigned to roles.
///
/// Permission names follow a "resource:action" convention:
///   "dashboard:read"    — read data in the Dashboard application
///   "dashboard:write"   — create/update data in Dashboard
///   "dashboard:admin"   — admin-only operations in Dashboard
///   "rubac:manage-users"  — manage users in RubacCore
///   "rubac:manage-roles"  — manage roles in RubacCore
///
/// This creates a proper RBAC three-layer model:
///   Scopes (OAuth2 audience) → Roles (coarse grouping) → Permissions (fine-grained)
/// </summary>
public class Permission
{
    public long    Id          { get; set; }

    /// <summary>The unique permission identifier, e.g. "dashboard:read".</summary>
    public string  Name        { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Which application this permission belongs to, e.g. "Dashboard".</summary>
    public string  Application { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
