using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Scheduling.Models;
using Atendefy.API.Modules.Tenants;
using Atendefy.API.Modules.Webhooks.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Scheduling;

public static class SchedulingEndpoints
{
    public static IEndpointRouteBuilder MapSchedulingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/scheduling")
            .WithTags("Scheduling")
            .RequireAuthorization();

        group.MapPut("/config", async (
            [FromBody] CalendarConfigRequest request,
            SchedulingService service,
            PublicDbContext publicDb,
            EntitlementsService entitlements,
            IConfiguration config,
            HttpContext ctx) =>
        {
            var (tenantId, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            // Trava por plano: Agenda só pode ser configurada se o plano permitir.
            var limits = await entitlements.GetForTenantAsync(tenantId);
            if (!limits.SchedulingEnabled)
                return Results.Json(
                    new { error = "Agenda não disponível no seu plano. Faça upgrade para habilitar." },
                    statusCode: StatusCodes.Status403Forbidden);

            var result = await service.UpsertAsync(schemaName, request);
            if (!result.IsSuccess) return Results.BadRequest(new { error = result.Error });

            // Fase 3: garante a rota do webhook (token -> tenant) quando há token.
            await EnsureWebhookRouteAsync(publicDb, tenantId, result.Value!.WebhookToken);

            return Results.Ok(Shape(result.Value!, config));
        });

        group.MapGet("/config", async (
            SchedulingService service,
            PublicDbContext publicDb,
            IConfiguration config,
            HttpContext ctx) =>
        {
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var cfg = await service.GetAsync(schemaName);
            return cfg is null
                ? Results.NotFound(new { error = "Agenda não configurada" })
                : Results.Ok(Shape(cfg, config));
        });

        group.MapGet("/appointments", async (
            SchedulingService service,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var items = await service.ListAppointmentsAsync(schemaName);
            return Results.Ok(items.Select(a => new
            {
                a.Id, a.Title, a.StartTime, a.EndTime,
                a.AttendeeName, a.AttendeeEmail, a.AttendeePhone, a.Status
            }));
        });

        return app;
    }

    private static object Shape(CalendarConfig c, IConfiguration config) => new
    {
        c.Provider,
        c.BookingUrl,
        c.Enabled,
        c.Instructions,
        WebhookUrl = BuildWebhookUrl(config, c.WebhookToken)
    };

    private static string? BuildWebhookUrl(IConfiguration config, string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var baseDomain = config["App:BaseDomain"];
        return string.IsNullOrEmpty(baseDomain)
            ? null
            : $"https://api.{baseDomain}/webhooks/calcom?token={token}";
    }

    private static async Task EnsureWebhookRouteAsync(PublicDbContext publicDb, Guid tenantId, string? token)
    {
        if (string.IsNullOrEmpty(token)) return;

        var exists = await publicDb.WebhookRoutes
            .AnyAsync(r => r.Provider == "calcom" && r.LookupKey == token);
        if (exists) return;

        publicDb.WebhookRoutes.Add(new WebhookRoute
        {
            TenantId = tenantId,
            Provider = "calcom",
            LookupKey = token,
            AccountId = Guid.Empty   // Cal.com não tem conta WhatsApp associada
        });
        await publicDb.SaveChangesAsync();
    }

    private static async Task<(Guid TenantId, string SchemaName, string? Error)> ResolveTenantAsync(
        HttpContext ctx, PublicDbContext publicDb)
    {
        var tenantIdStr = ctx.User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            return (Guid.Empty, string.Empty, "Token inválido");

        var tenant = await publicDb.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        return tenant is null
            ? (Guid.Empty, string.Empty, "Tenant não encontrado")
            : (tenant.Id, tenant.SchemaName, null);
    }
}
