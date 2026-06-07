using Atendefy.API.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Chatbot;

public static class ContactEndpoints
{
    public static IEndpointRouteBuilder MapContactEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/contacts")
            .WithTags("Contacts")
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
            if (pageSize is < 1 or > 100) pageSize = 50;

            await using var db = dbFactory.Create(schemaName);

            var contacts = await db.Contacts
                .OrderBy(c => c.Phone)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    c.Phone,
                    c.Name,
                    c.CreatedAt,
                    ConversationCount = db.Conversations.Count(cv => cv.ContactPhone == c.Phone),
                    LastActivity = db.Conversations
                        .Where(cv => cv.ContactPhone == c.Phone)
                        .SelectMany(cv => cv.Messages)
                        .Max(m => (DateTime?)m.CreatedAt)
                })
                .ToListAsync();

            var total = await db.Contacts.CountAsync();

            return Results.Ok(new { contacts, total, page, pageSize });
        });

        group.MapPatch("/{phone}", async (
            string phone,
            [FromBody] UpdateContactRequest request,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            await using var db = dbFactory.Create(schemaName);
            var contact = await db.Contacts.FindAsync(phone);
            if (contact is null) return Results.NotFound();

            contact.Name = request.Name?.Trim();
            await db.SaveChangesAsync();
            return Results.Ok(new { contact.Phone, contact.Name });
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

public record UpdateContactRequest(string? Name);
