using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Tenants.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Tenants;

public class TenantService(PublicDbContext db, ITenantProvisioner provisioner)
{
    public async Task<Result<Tenant>> RegisterAsync(RegisterTenantRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName)
            || string.IsNullOrWhiteSpace(request.Subdomain)
            || string.IsNullOrWhiteSpace(request.OwnerName)
            || string.IsNullOrWhiteSpace(request.OwnerEmail)
            || string.IsNullOrWhiteSpace(request.OwnerPassword))
            return Result<Tenant>.Fail("Todos os campos são obrigatórios");

        if (request.OwnerPassword.Length > 72)
            return Result<Tenant>.Fail("Senha inválida");

        var subdomain = request.Subdomain.ToLowerInvariant().Trim();

        if (await db.Tenants.AnyAsync(t => t.Subdomain == subdomain))
            return Result<Tenant>.Fail($"O subdomínio '{subdomain}' já está em uso");

        var tenant = new Tenant { Name = request.CompanyName, Subdomain = subdomain };
        var owner = new TenantUser
        {
            TenantId = tenant.Id,
            Name = request.OwnerName,
            Email = request.OwnerEmail.ToLowerInvariant().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.OwnerPassword),
            Role = "Owner"
        };

        db.Tenants.Add(tenant);
        db.TenantUsers.Add(owner);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Result<Tenant>.Fail($"O subdomínio '{subdomain}' já está em uso");
        }

        try
        {
            await provisioner.ProvisionSchemaAsync(tenant.SchemaName);
        }
        catch
        {
            db.TenantUsers.Remove(owner);
            db.Tenants.Remove(tenant);
            await db.SaveChangesAsync();
            return Result<Tenant>.Fail("Falha ao provisionar o ambiente do tenant. Tente novamente.");
        }

        return Result<Tenant>.Ok(tenant);
    }
}
