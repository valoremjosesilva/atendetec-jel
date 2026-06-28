using Atendefy.API.SharedKernel;

namespace Atendefy.API.Modules.Tenants.Models;

public class TenantUser : BaseEntity
{
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "Owner"; // Owner | Admin | Viewer
    public string Name { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
}
