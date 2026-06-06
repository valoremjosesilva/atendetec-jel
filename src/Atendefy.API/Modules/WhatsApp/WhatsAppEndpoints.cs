using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.WhatsApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.WhatsApp;

public static class WhatsAppEndpoints
{
    public static IEndpointRouteBuilder MapWhatsAppEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/whatsapp/accounts")
            .WithTags("WhatsApp")
            .RequireAuthorization();

        group.MapPost("/", async (
            [FromBody] CreateAccountRequest request,
            WhatsAppAccountService service,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (tenantId, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var result = await service.CreateAsync(tenantId, schemaName, request);
            return result.IsSuccess
                ? Results.Created($"/whatsapp/accounts/{result.Value!.Id}",
                    new { result.Value.Id, result.Value.Provider, result.Value.Phone, result.Value.Status })
                : Results.BadRequest(new { error = result.Error });
        });

        group.MapGet("/", async (
            WhatsAppAccountService service,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var accounts = await service.ListAsync(schemaName);
            return Results.Ok(accounts.Select(a => new
            {
                a.Id, a.Provider, a.Phone, a.Status, a.CreatedAt
            }));
        });

        return app;
    }

    private static async Task<(Guid TenantId, string SchemaName, string? Error)> ResolveTenantAsync(
        HttpContext ctx, PublicDbContext publicDb)
    {
        var tenantIdStr = ctx.User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            return (Guid.Empty, string.Empty, "Token inválido");

        var tenant = await publicDb.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null)
            return (Guid.Empty, string.Empty, "Tenant não encontrado");

        return (tenantId, tenant.SchemaName, null);
    }
}
