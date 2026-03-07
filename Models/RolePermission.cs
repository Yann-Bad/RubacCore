namespace RubacCore.Models;

/// <summary>
/// Join table between <see cref="ApplicationRole"/> and <see cref="Permission"/>.
/// Uses a composite primary key (RoleId, PermissionId).
/// </summary>
public class RolePermission
{
    public long RoleId       { get; set; }
    public long PermissionId { get; set; }

    public ApplicationRole Role       { get; set; } = null!;
    public Permission      Permission { get; set; } = null!;
}
