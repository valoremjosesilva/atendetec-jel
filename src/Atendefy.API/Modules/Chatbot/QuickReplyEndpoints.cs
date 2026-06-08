using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Infrastructure.RateLimiting;
using Atendefy.API.Modules.Chatbot.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Chatbot;

public static class QuickReplyEndpoints
{
    public static IEndpointRouteBuilder MapQuickReplyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/quick-replies")
            .WithTags("QuickReplies")
            .RequireAuthorization();

        group.MapGet("/", async (
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            await using var db = dbFactory.Create(schemaName);

            var quickReplies = await db.QuickReplies
                .OrderBy(q => q.CreatedAt)
                .Select(q => new { q.Id, q.Title, q.Body, q.CreatedAt })
                .ToListAsync();

            return Results.Ok(new { quickReplies });
        });

        group.MapPost("/", async (
            [FromBody] CreateQuickReplyRequest request,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { error = "Título não pode ser vazio." });
            if (string.IsNullOrWhiteSpace(request.Body))
                return Results.BadRequest(new { error = "Corpo não pode ser vazio." });

            await using var db = dbFactory.Create(schemaName);

            var quickReply = new QuickReply
            {
                Title = request.Title.Trim(),
                Body = request.Body.Trim()
            };
            db.QuickReplies.Add(quickReply);
            await db.SaveChangesAsync();

            return Results.Created($"/quick-replies/{quickReply.Id}",
                new { quickReply.Id, quickReply.Title, quickReply.Body, quickReply.CreatedAt });
        }).AddEndpointFilter<ApiRateLimitFilter>();

        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateQuickReplyRequest request,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            await using var db = dbFactory.Create(schemaName);
            var quickReply = await db.QuickReplies.FindAsync(id);
            if (quickReply is null) return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(request.Title))
                quickReply.Title = request.Title.Trim();
            if (!string.IsNullOrWhiteSpace(request.Body))
                quickReply.Body = request.Body.Trim();

            await db.SaveChangesAsync();
            return Results.Ok(new { quickReply.Id, quickReply.Title, quickReply.Body });
        }).AddEndpointFilter<ApiRateLimitFilter>();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            await using var db = dbFactory.Create(schemaName);
            var quickReply = await db.QuickReplies.FindAsync(id);
            if (quickReply is null) return Results.NotFound();

            db.QuickReplies.Remove(quickReply);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).AddEndpointFilter<ApiRateLimitFilter>();

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

public record CreateQuickReplyRequest(string Title, string Body);
public record UpdateQuickReplyRequest(string? Title, string? Body);
