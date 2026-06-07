using Atendefy.API.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Chatbot;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dashboard/stats", async (
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            await using var db = dbFactory.Create(schemaName);

            var now = DateTime.UtcNow;
            var firstOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var currentMonth = now.ToString("yyyy-MM");

            var counts = await db.Conversations
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total     = g.Count(),
                    ThisMonth = g.Count(c => c.StartedAt >= firstOfMonth)
                })
                .FirstOrDefaultAsync();

            var totalConversations     = counts?.Total ?? 0;
            var conversationsThisMonth = counts?.ThisMonth ?? 0;

            var usage = await db.UsageCounters.FindAsync(currentMonth);

            var waAccount = await db.WhatsAppAccounts
                .Select(w => new { w.Status })
                .FirstOrDefaultAsync();

            return Results.Ok(new
            {
                totalConversations,
                conversationsThisMonth,
                messagesThisMonth  = usage?.MessagesSent ?? 0,
                tokensThisMonth    = usage?.TokensConsumed ?? 0,
                costThisMonth      = usage?.CostUsd ?? 0m,
                whatsAppStatus     = waAccount?.Status ?? "none"
            });
        })
        .WithTags("Dashboard")
        .RequireAuthorization();

        return app;
    }

    private static async Task<(string SchemaName, string? Error)> ResolveSchemaAsync(
        HttpContext ctx, PublicDbContext publicDb)
    {
        var tenantIdStr = ctx.User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            return (string.Empty, "Token inválido");
        var tenant = await publicDb.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return (string.Empty, "Tenant não encontrado");
        return (tenant.SchemaName, null);
    }
}
