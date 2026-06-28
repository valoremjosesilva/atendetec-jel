using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Tenants.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static Atendefy.API.SharedKernel.AppConstants;

namespace Atendefy.API.Modules.Tenants;

public class TenantService(PublicDbContext db, ITenantProvisioner provisioner, ILogger<TenantService> logger)
{
    // Cria a empresa (pending) + o usuário dono (e-mail NÃO verificado). NÃO provisiona o schema
    // Postgres — isso só acontece na aprovação (ActivateAsync), evitando exaustão de recursos por
    // cadastros automatizados. O envio do e-mail de verificação é feito no endpoint.
    public async Task<Result<RegisteredTenant>> RegisterAsync(RegisterTenantRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName)
            || string.IsNullOrWhiteSpace(request.Subdomain)
            || string.IsNullOrWhiteSpace(request.OwnerName)
            || string.IsNullOrWhiteSpace(request.OwnerEmail)
            || string.IsNullOrWhiteSpace(request.OwnerPassword))
            return Result<RegisteredTenant>.Fail("Todos os campos são obrigatórios");

        if (request.OwnerPassword.Length > 72)
            return Result<RegisteredTenant>.Fail("Senha inválida");

        var subdomain = request.Subdomain.ToLowerInvariant().Trim();

        if (!System.Text.RegularExpressions.Regex.IsMatch(subdomain, @"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$"))
            return Result<RegisteredTenant>.Fail("Subdomínio inválido. Use apenas letras minúsculas, números e hífens (ex: minha-empresa)");

        if (await db.Tenants.AnyAsync(t => t.Subdomain == subdomain))
            return Result<RegisteredTenant>.Fail($"O subdomínio '{subdomain}' já está em uso");

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
            Role = "Owner",
            EmailVerified = false
        };

        db.Tenants.Add(tenant);
        db.TenantUsers.Add(owner);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Result<RegisteredTenant>.Fail($"O subdomínio '{subdomain}' já está em uso");
        }

        return Result<RegisteredTenant>.Ok(new RegisteredTenant(
            tenant.Id, tenant.Subdomain, tenant.Name, owner.Id, owner.Email, owner.Name));
    }

    public async Task<List<PendingTenant>> ListPendingAsync() =>
        await db.Tenants
            .Where(t => t.Status == TenantStatus.Pending)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new PendingTenant(
                t.Id, t.Subdomain, t.Name, t.CreatedAt,
                db.TenantUsers
                    .Where(u => u.TenantId == t.Id && u.Role == "Owner")
                    .Select(u => u.EmailVerified)
                    .FirstOrDefault()))
            .ToListAsync();

    // Marca o e-mail do usuário como verificado (chamado após o clique no link de confirmação).
    public async Task<Result> MarkEmailVerifiedAsync(Guid userId)
    {
        var user = await db.TenantUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Result.Fail("Usuário não encontrado");
        if (!user.EmailVerified)
        {
            user.EmailVerified = true;
            await db.SaveChangesAsync();
        }
        return Result.Ok();
    }

    // Busca o dono (não verificado) para reenviar o e-mail. Retorna null se não houver pendência —
    // o endpoint responde de forma genérica para não revelar se a conta existe.
    public async Task<RegisteredTenant?> FindOwnerForResendAsync(string subdomain, string email)
    {
        var key = subdomain.ToLowerInvariant().Trim();
        var mail = email.ToLowerInvariant().Trim();
        var row = await (
            from t in db.Tenants
            join u in db.TenantUsers on t.Id equals u.TenantId
            where t.Subdomain == key && u.Email == mail && !u.EmailVerified
            select new RegisteredTenant(t.Id, t.Subdomain, t.Name, u.Id, u.Email, u.Name))
            .FirstOrDefaultAsync();
        return row;
    }

    // Aprovação: provisiona o schema do tenant (idempotente) e ativa. Exige e-mail do dono
    // verificado. Se o provisionamento falhar, NÃO ativa.
    public async Task<Result> ActivateAsync(string subdomain)
    {
        var key = subdomain.ToLowerInvariant().Trim();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Subdomain == key);
        if (tenant is null) return Result.Fail("Empresa não encontrada");
        if (tenant.Status == TenantStatus.Active) return Result.Ok();

        var owner = await db.TenantUsers
            .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Role == "Owner");
        if (owner is { EmailVerified: false })
            return Result.Fail("O e-mail do responsável ainda não foi confirmado.");

        try
        {
            await provisioner.ProvisionSchemaAsync(tenant.SchemaName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Schema provisioning failed for tenant {TenantId} on activate", tenant.Id);
            return Result.Fail("Falha ao provisionar o ambiente do tenant. Tente novamente.");
        }

        tenant.Status = TenantStatus.Active;
        await db.SaveChangesAsync();
        logger.LogInformation("Tenant {Subdomain} ({TenantId}) ativado", tenant.Subdomain, tenant.Id);
        return Result.Ok();
    }
}

public record PendingTenant(Guid Id, string Subdomain, string Name, DateTime CreatedAt, bool EmailVerified);
public record RegisteredTenant(Guid Id, string Subdomain, string Name, Guid OwnerUserId, string OwnerEmail, string OwnerName);
