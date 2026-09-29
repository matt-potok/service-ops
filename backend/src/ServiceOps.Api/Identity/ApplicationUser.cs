using Microsoft.AspNetCore.Identity;

namespace ServiceOps.Api.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
