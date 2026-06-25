using Atendefy.API.Modules.Tenants.Models;
using Microsoft.AspNetCore.Mvc;

namespace Atendefy.API.Modules.Tenants;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tenants").WithTags("Tenants").AllowAnonymous();

        group.MapPost("/register", async (
            [FromBody] RegisterTenantRequest request,
            TenantService tenantService) =>
        {
            var result = await tenantService.RegisterAsync(request);
            return result.IsSuccess
                ? Results.Created($"/tenants/{result.Value!.Id}",
                    new { result.Value.Id, result.Value.Subdomain, result.Value.Name })
                : Results.BadRequest(new { error = result.Error });
        });

        // Admin: listar empresas pendentes de aprovação. Protegido por X-Admin-Key.
        group.MapGet("/pending", async (
            HttpContext ctx, TenantService tenantService, IConfiguration config) =>
        {
            if (!AdminAuth.IsAdmin(ctx, config)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Ok(await tenantService.ListPendingAsync());
        });

        // Admin: aprovar (ativar) uma empresa. Protegido por X-Admin-Key.
        group.MapPost("/{subdomain}/activate", async (
            string subdomain, HttpContext ctx, TenantService tenantService, IConfiguration config) =>
        {
            if (!AdminAuth.IsAdmin(ctx, config)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var result = await tenantService.ActivateAsync(subdomain);
            return result.IsSuccess
                ? Results.Ok(new { activated = subdomain })
                : Results.BadRequest(new { error = result.Error });
        });

        // /me — perfil + entitlements do plano + uso mensal da IA, para o painel saber o que exibir.
        app.MapGet("/me", async (
            HttpContext ctx,
            EntitlementsService entitlements,
            Atendefy.API.Infrastructure.Cache.RedisService redis) =>
        {
            var tenantIdStr = ctx.User.FindFirst("tenant_id")?.Value;
            if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
                return Results.Json(new { error = "Token inválido" }, statusCode: 401);

            var (planName, limits) = await entitlements.GetPlanForTenantAsync(tenantId);

            long messagesUsed = 0;
            try { messagesUsed = await redis.GetCounterAsync(
                EntitlementsService.MonthlyUsageKey(tenantIdStr, DateTime.UtcNow)); }
            catch { /* Redis indisponível: uso 0 não bloqueia a UI. */ }

            return Results.Ok(new
            {
                role = ctx.User.FindFirst("role")?.Value,
                planName,
                entitlements = new
                {
                    aiEnabled = limits.AiEnabled,
                    schedulingEnabled = limits.SchedulingEnabled,
                    whatsAppAccounts = limits.WhatsAppAccounts,
                    messagesPerMonth = limits.MessagesPerMonth,
                    teamMembers = limits.TeamMembers
                },
                usage = new { messagesUsed }
            });
        }).RequireAuthorization().WithTags("Tenants");

        return app;
    }
}
