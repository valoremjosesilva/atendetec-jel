using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Tenants.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static Atendefy.API.SharedKernel.AppConstants;

namespace Atendefy.API.Modules.Tenants;

public class TenantService(PublicDbContext db, ITenantProvisioner provisioner, ILogger<TenantService> logger)
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

        if (!System.Text.RegularExpressions.Regex.IsMatch(subdomain, @"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$"))
            return Result<Tenant>.Fail("Subdomínio inválido. Use apenas letras minúsculas, números e hífens (ex: minha-empresa)");

        if (await db.Tenants.AnyAsync(t => t.Subdomain == subdomain))
            return Result<Tenant>.Fail($"O subdomínio '{subdomain}' já está em uso");

        // Nasce "pending": o login exige Status == Active, então a empresa só entra
        // após aprovação (ver ActivateAsync).
        var tenant = new Tenant
        {
            Name = request.CompanyName,
            Subdomain = subdomain,
            Status = TenantStatus.Pending
        };
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Schema provisioning failed for tenant {TenantId}, rolling back registration", tenant.Id);
            db.TenantUsers.Remove(owner);
            db.Tenants.Remove(tenant);
            await db.SaveChangesAsync();
            return Result<Tenant>.Fail("Falha ao provisionar o ambiente do tenant. Tente novamente.");
        }

        return Result<Tenant>.Ok(tenant);
    }

    public async Task<List<PendingTenant>> ListPendingAsync() =>
        await db.Tenants
            .Where(t => t.Status == TenantStatus.Pending)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new PendingTenant(t.Id, t.Subdomain, t.Name, t.CreatedAt))
            .ToListAsync();

    public async Task<Result> ActivateAsync(string subdomain)
    {
        var key = subdomain.ToLowerInvariant().Trim();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Subdomain == key);
        if (tenant is null) return Result.Fail("Empresa não encontrada");
        if (tenant.Status == TenantStatus.Active) return Result.Ok();

        tenant.Status = TenantStatus.Active;
        await db.SaveChangesAsync();
        logger.LogInformation("Tenant {Subdomain} ({TenantId}) ativado", tenant.Subdomain, tenant.Id);
        return Result.Ok();
    }
}

public record PendingTenant(Guid Id, string Subdomain, string Name, DateTime CreatedAt);
