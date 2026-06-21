using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Scheduling.Models;
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
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var result = await service.UpsertAsync(schemaName, request);
            return result.IsSuccess
                ? Results.Ok(Shape(result.Value!))
                : Results.BadRequest(new { error = result.Error });
        });

        group.MapGet("/config", async (
            SchedulingService service,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var config = await service.GetAsync(schemaName);
            return config is null
                ? Results.NotFound(new { error = "Agenda não configurada" })
                : Results.Ok(Shape(config));
        });

        return app;
    }

    private static object Shape(CalendarConfig c) =>
        new { c.Provider, c.BookingUrl, c.Enabled, c.Instructions };

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
