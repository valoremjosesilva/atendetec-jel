using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Billing.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Tenants;

/// <summary>
/// Operações de superadmin: CRUD de planos e atribuição de plano às empresas.
/// </summary>
public class AdminService(PublicDbContext db)
{
    public async Task<List<PlanDto>> ListPlansAsync() =>
        await db.Plans
            .OrderBy(p => p.PriceMonthly)
            .Select(p => ToDto(p))
            .ToListAsync();

    public async Task<Result<PlanDto>> CreatePlanAsync(PlanRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Result<PlanDto>.Fail("Nome do plano é obrigatório.");

        var plan = new Plan
        {
            Name = req.Name.Trim(),
            PriceMonthly = req.PriceMonthly,
            PriceYearly = req.PriceYearly,
            LimitsJson = req.ToLimits().ToJson(),
            IsActive = req.IsActive
        };
        db.Plans.Add(plan);
        await db.SaveChangesAsync();
        return Result<PlanDto>.Ok(ToDto(plan));
    }

    public async Task<Result<PlanDto>> UpdatePlanAsync(Guid id, PlanRequest req)
    {
        var plan = await db.Plans.FindAsync(id);
        if (plan is null) return Result<PlanDto>.Fail("Plano não encontrado.");
        if (string.IsNullOrWhiteSpace(req.Name))
            return Result<PlanDto>.Fail("Nome do plano é obrigatório.");

        plan.Name = req.Name.Trim();
        plan.PriceMonthly = req.PriceMonthly;
        plan.PriceYearly = req.PriceYearly;
        plan.LimitsJson = req.ToLimits().ToJson();
        plan.IsActive = req.IsActive;
        plan.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Result<PlanDto>.Ok(ToDto(plan));
    }

    public async Task<List<AdminTenantDto>> ListTenantsAsync()
    {
        // join manual com Plans (PlanId é Guid? sem FK navigation no modelo)
        var tenants = await db.Tenants.OrderBy(t => t.Name).ToListAsync();
        var plans = await db.Plans.ToDictionaryAsync(p => p.Id, p => p.Name);
        // EmailVerified do dono de cada tenant (para o superadmin ver antes de aprovar).
        var ownerVerified = await db.TenantUsers
            .Where(u => u.Role == "Owner")
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Verified = g.Max(u => u.EmailVerified) })
            .ToDictionaryAsync(x => x.TenantId, x => x.Verified);
        return tenants.Select(t => new AdminTenantDto(
            t.Id, t.Subdomain, t.Name, t.Status, t.PlanId,
            t.PlanId is Guid pid && plans.TryGetValue(pid, out var n) ? n : null,
            ownerVerified.TryGetValue(t.Id, out var v) && v,
            t.CreatedAt)).ToList();
    }

    public async Task<Result> AssignPlanAsync(string subdomain, Guid planId)
    {
        var key = subdomain.ToLowerInvariant().Trim();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Subdomain == key);
        if (tenant is null) return Result.Fail("Empresa não encontrada.");
        if (!await db.Plans.AnyAsync(p => p.Id == planId))
            return Result.Fail("Plano não encontrado.");

        tenant.PlanId = planId;
        tenant.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Result.Ok();
    }

    private static PlanDto ToDto(Plan p)
    {
        var l = PlanLimits.FromJson(p.LimitsJson);
        return new PlanDto(p.Id, p.Name, p.PriceMonthly, p.PriceYearly, p.IsActive,
            l.WhatsAppAccounts, l.MessagesPerMonth, l.TeamMembers, l.AiEnabled, l.SchedulingEnabled);
    }
}

public record PlanRequest(
    string Name,
    decimal PriceMonthly,
    decimal PriceYearly,
    bool IsActive,
    int WhatsAppAccounts,
    int MessagesPerMonth,
    int TeamMembers,
    bool AiEnabled,
    bool SchedulingEnabled)
{
    public PlanLimits ToLimits() => new(
        MessagesPerMonth: MessagesPerMonth,
        WhatsAppAccounts: WhatsAppAccounts,
        TeamMembers: TeamMembers,
        AiEnabled: AiEnabled,
        SchedulingEnabled: SchedulingEnabled);
}

public record PlanDto(
    Guid Id,
    string Name,
    decimal PriceMonthly,
    decimal PriceYearly,
    bool IsActive,
    int WhatsAppAccounts,
    int MessagesPerMonth,
    int TeamMembers,
    bool AiEnabled,
    bool SchedulingEnabled);

public record AdminTenantDto(
    Guid Id,
    string Subdomain,
    string Name,
    string Status,
    Guid? PlanId,
    string? PlanName,
    bool EmailVerified,
    DateTime CreatedAt);

public record AssignPlanRequest(Guid PlanId);
