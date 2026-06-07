using Atendefy.API.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Chatbot;

public static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/conversations")
            .WithTags("Conversations")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            if (page < 1) page = 1;
            if (pageSize is < 1 or > 100) pageSize = 20;

            await using var db = dbFactory.Create(schemaName);

            var conversations = await db.Conversations
                .Select(c => new
                {
                    c.Id,
                    c.ContactPhone,
                    c.MessageCount,
                    c.StartedAt,
                    LastMessageAt = c.Messages.Max(m => (DateTime?)m.CreatedAt) ?? c.StartedAt
                })
                .OrderByDescending(c => c.LastMessageAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var total = await db.Conversations.CountAsync();

            return Results.Ok(new { conversations, total, page, pageSize });
        });

        group.MapGet("/{id:guid}/messages", async (
            Guid id,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            await using var db = dbFactory.Create(schemaName);

            var conversation = await db.Conversations
                .FirstOrDefaultAsync(c => c.Id == id);
            if (conversation is null) return Results.NotFound();

            var messages = await db.Messages
                .Where(m => m.ConversationId == id)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new { m.Id, m.Role, m.Content, m.TokensUsed, m.CreatedAt })
                .ToListAsync();

            return Results.Ok(new
            {
                conversation.Id,
                conversation.ContactPhone,
                conversation.StartedAt,
                conversation.MessageCount,
                messages
            });
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
        if (tenant is null) return (string.Empty, "Tenant não encontrado");

        return (tenant.SchemaName, null);
    }
}
