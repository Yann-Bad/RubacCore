using Microsoft.AspNetCore.Identity;

namespace RubacCore.Models;

public class ApplicationUser : IdentityUser<long>
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ApplicationUserRole> UserRoles { get; set; } = [];
}
