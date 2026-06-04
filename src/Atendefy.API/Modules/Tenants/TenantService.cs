using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Tenants.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Tenants;

public class TenantService(PublicDbContext db, ITenantProvisioner provisioner)
{
    public async Task<Result<Tenant>> RegisterAsync(RegisterTenantRequest request)
    {
        var subdomain = request.Subdomain.ToLowerInvariant().Trim();

        if (await db.Tenants.AnyAsync(t => t.Subdomain == subdomain))
            return Result<Tenant>.Fail($"O subdomínio '{subdomain}' já está em uso");

        var tenant = new Tenant { Name = request.CompanyName, Subdomain = subdomain };
        db.Tenants.Add(tenant);

        db.TenantUsers.Add(new TenantUser
        {
            TenantId = tenant.Id,
            Name = request.OwnerName,
            Email = request.OwnerEmail.ToLowerInvariant().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.OwnerPassword),
            Role = "Owner"
        });

        await db.SaveChangesAsync();
        await provisioner.ProvisionSchemaAsync(tenant.SchemaName);

        return Result<Tenant>.Ok(tenant);
    }
}
