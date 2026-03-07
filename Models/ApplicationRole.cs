using Microsoft.AspNetCore.Identity;

namespace RubacCore.Models;

public class ApplicationRole : IdentityRole<long>
{
    public string? Description { get; set; }
    public string? Application { get; set; } // "DashboardCore", "OtherApp"...

    public ICollection<ApplicationUserRole> UserRoles        { get; set; } = [];
    public ICollection<RolePermission>      RolePermissions  { get; set; } = [];
}
