using Microsoft.AspNetCore.Identity;

namespace RubacCore.Models;

public class ApplicationUserRole : IdentityUserRole<long>
{
    public ApplicationUser? User { get; set; }
    public ApplicationRole? Role { get; set; }
}
