using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.AI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.AI;

public static class AIEndpoints
{
    public static IEndpointRouteBuilder MapAIEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ai")
            .WithTags("AI")
            .RequireAuthorization();

        group.MapPut("/config", async (
            [FromBody] AiConfigRequest request,
            AiConfigService service,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var result = await service.UpsertAsync(schemaName, request);
            return result.IsSuccess
                ? Results.Ok(new { result.Value!.Provider, result.Value.Model, result.Value.SystemPrompt })
                : Results.BadRequest(new { error = result.Error });
        });

        group.MapGet("/config", async (
            AiConfigService service,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var config = await service.GetAsync(schemaName);
            return config is null
                ? Results.NotFound(new { error = "Configuração de IA não encontrada" })
                : Results.Ok(new { config.Provider, config.Model, config.SystemPrompt });
        });

        return app;
    }

    private static async Task<(string SchemaName, string? Error)> ResolveSchemaAsync(
        HttpContext ctx, PublicDbContext publicDb)
    {
        var tenantIdStr = ctx.User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            return (string.Empty, "Token inválido");

        var tenant = await publicDb.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        return tenant is null
            ? (string.Empty, "Tenant não encontrado")
            : (tenant.SchemaName, null);
    }
}
