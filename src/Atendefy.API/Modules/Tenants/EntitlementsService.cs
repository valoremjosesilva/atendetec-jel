using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Tenants;

/// <summary>
/// Resolve os entitlements (PlanLimits) efetivos de um tenant a partir do plano atribuído
/// (Tenant.PlanId -> Plan.LimitsJson). Tenant sem plano cai num fallback conservador "Free".
/// </summary>
public class EntitlementsService(PublicDbContext db)
{
    // Fallback para tenants sem plano atribuído: 1 WhatsApp, sem agenda, IA on, 1000 msgs, 1 user.
    public static readonly PlanLimits FreeFallback = new(
        MessagesPerMonth: 1000, WhatsAppAccounts: 1, TeamMembers: 1,
        AiEnabled: true, SchedulingEnabled: false);

    // Chave do contador mensal de mensagens da IA por tenant (Redis). Expira sozinha após o mês.
    // Centralizada aqui para o worker (incrementa) e o /me (lê) usarem exatamente o mesmo formato.
    public static string MonthlyUsageKey(string tenantId, DateTime utcNow) =>
        $"usage:{tenantId}:{utcNow:yyyyMM}";

    public async Task<PlanLimits> GetForTenantAsync(Guid tenantId)
    {
        var planId = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.PlanId)
            .FirstOrDefaultAsync();

        if (planId is null) return FreeFallback;

        var limitsJson = await db.Plans
            .Where(p => p.Id == planId)
            .Select(p => p.LimitsJson)
            .FirstOrDefaultAsync();

        return limitsJson is null ? FreeFallback : PlanLimits.FromJson(limitsJson);
    }

    public async Task<(string? PlanName, PlanLimits Limits)> GetPlanForTenantAsync(Guid tenantId)
    {
        var planId = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.PlanId)
            .FirstOrDefaultAsync();

        if (planId is null) return (null, FreeFallback);

        var plan = await db.Plans
            .Where(p => p.Id == planId)
            .Select(p => new { p.Name, p.LimitsJson })
            .FirstOrDefaultAsync();

        return plan is null
            ? (null, FreeFallback)
            : (plan.Name, PlanLimits.FromJson(plan.LimitsJson));
    }
}
