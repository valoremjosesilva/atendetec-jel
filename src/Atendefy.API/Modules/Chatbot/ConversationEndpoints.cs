using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Infrastructure.RateLimiting;
using Atendefy.API.Modules.Chatbot.Models;
using Atendefy.API.Modules.WhatsApp;
using Atendefy.API.Modules.WhatsApp.Models;
using Atendefy.API.SharedKernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Threading.Channels;

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
            [FromQuery] string? status,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            if (page < 1) page = 1;
            if (pageSize is < 1 or > 100) pageSize = 20;

            await using var db = dbFactory.Create(schemaName);

            var query = db.Conversations.AsQueryable();
            var statusFilter = status?.ToLowerInvariant();
            if (statusFilter == AppConstants.ConversationStatus.Resolved)
                query = query.Where(c => c.IsResolved);
            else if (statusFilter != AppConstants.ConversationStatus.All)
                query = query.Where(c => !c.IsResolved);

            var total = await query.CountAsync();

            var conversations = await query
                .Select(c => new
                {
                    c.Id,
                    c.ContactPhone,
                    c.MessageCount,
                    c.StartedAt,
                    c.BotPaused,
                    c.IsResolved,
                    LastMessageAt = c.Messages.Max(m => (DateTime?)m.CreatedAt) ?? c.StartedAt
                })
                .OrderByDescending(c => c.LastMessageAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Results.Ok(new { conversations, total, page, pageSize });
        });

        group.MapGet("/{id:guid}/messages", async (
            Guid id,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx,
            [FromQuery] int limit = 0,
            [FromQuery] DateTime? before = null) =>
        {
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            if (limit <= 0 || limit > 100) limit = 50;

            await using var db = dbFactory.Create(schemaName);

            var conversation = await db.Conversations
                .FirstOrDefaultAsync(c => c.Id == id);
            if (conversation is null) return Results.NotFound();

            var query = db.Messages.Where(m => m.ConversationId == id);
            if (before.HasValue)
                query = query.Where(m => m.CreatedAt < before.Value);

            var raw = await query
                .OrderByDescending(m => m.CreatedAt)
                .Take(limit + 1)
                .Select(m => new { m.Id, m.Role, m.Content, m.TokensUsed, m.CreatedAt })
                .ToListAsync();

            var hasMore = raw.Count > limit;
            var messages = raw.Take(limit).OrderBy(m => m.CreatedAt).ToList();

            return Results.Ok(new
            {
                conversation.Id,
                conversation.ContactPhone,
                conversation.StartedAt,
                conversation.MessageCount,
                conversation.BotPaused,
                conversation.IsResolved,
                conversation.ResolvedAt,
                messages,
                hasMore
            });
        });

        group.MapPatch("/{id:guid}/takeover", async (
            Guid id,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            await using var db = dbFactory.Create(schemaName);
            var conversation = await db.Conversations.FindAsync(id);
            if (conversation is null) return Results.NotFound();

            conversation.BotPaused = true;
            await db.SaveChangesAsync();
            return Results.Ok(new { conversation.Id, conversation.BotPaused });
        }).AddEndpointFilter<ApiRateLimitFilter>();

        group.MapPatch("/{id:guid}/release", async (
            Guid id,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            await using var db = dbFactory.Create(schemaName);
            var conversation = await db.Conversations.FindAsync(id);
            if (conversation is null) return Results.NotFound();

            conversation.BotPaused = false;
            await db.SaveChangesAsync();
            return Results.Ok(new { conversation.Id, conversation.BotPaused });
        }).AddEndpointFilter<ApiRateLimitFilter>();

        group.MapPatch("/{id:guid}/resolve", async (
            Guid id,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            await using var db = dbFactory.Create(schemaName);
            var conversation = await db.Conversations.FindAsync(id);
            if (conversation is null) return Results.NotFound();

            conversation.IsResolved = true;
            conversation.ResolvedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { conversation.Id, conversation.IsResolved, conversation.ResolvedAt });
        }).AddEndpointFilter<ApiRateLimitFilter>();

        group.MapPatch("/{id:guid}/reopen", async (
            Guid id,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            await using var db = dbFactory.Create(schemaName);
            var conversation = await db.Conversations.FindAsync(id);
            if (conversation is null) return Results.NotFound();

            conversation.IsResolved = false;
            conversation.ResolvedAt = null;
            await db.SaveChangesAsync();
            return Results.Ok(new { conversation.Id, conversation.IsResolved });
        }).AddEndpointFilter<ApiRateLimitFilter>();

        group.MapPost("/{id:guid}/messages", async (
            Guid id,
            [FromBody] SendMessageRequest request,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            WhatsAppProviderFactory whatsAppFactory,
            IConversationEventEmitter emitter,
            HttpContext ctx) =>
        {
            var (tenantIdStr, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            if (string.IsNullOrWhiteSpace(request.Text))
                return Results.BadRequest(new { error = "Texto não pode ser vazio." });

            await using var db = dbFactory.Create(schemaName);
            var conversation = await db.Conversations.FindAsync(id);
            if (conversation is null) return Results.NotFound();

            if (conversation.AccountId is null)
                return Results.BadRequest(new { error = "Conversa sem conta WhatsApp associada." });

            var waAccount = await db.WhatsAppAccounts.FindAsync(conversation.AccountId.Value);
            if (waAccount?.ConfigJson is null)
                return Results.BadRequest(new { error = "Conta WhatsApp não encontrada." });

            var message = new ConversationMessage
            {
                ConversationId = id,
                Role = AppConstants.MessageRole.Agent,
                Content = request.Text
            };
            db.Messages.Add(message);
            conversation.MessageCount++;
            await db.SaveChangesAsync();

            emitter.Emit(tenantIdStr, JsonSerializer.Serialize(
                new { type = "message_added", conversationId = id }));

            try
            {
                var provider = whatsAppFactory.Create(waAccount.Provider, waAccount.ConfigJson);
                await provider.SendMessageAsync(new OutboundMessage(conversation.ContactPhone, request.Text));
            }
            catch (Exception)
            {
                // Message persisted; WhatsApp delivery failure is non-fatal
            }

            return Results.Ok(new { message.Id, message.Role, message.Content, message.CreatedAt });
        }).AddEndpointFilter<ApiRateLimitFilter>();

        group.MapGet("/stream", async (
            HttpContext ctx,
            IConversationEventEmitter emitter,
            CancellationToken ct) =>
        {
            ctx.Response.Headers["Content-Type"]      = "text/event-stream";
            ctx.Response.Headers["Cache-Control"]     = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            var tenantIdStr = ctx.User.FindFirst("tenant_id")?.Value;
            if (string.IsNullOrEmpty(tenantIdStr))
            {
                ctx.Response.StatusCode = 401;
                return;
            }

            var channel = Channel.CreateBounded<string>(
                new BoundedChannelOptions(50) { FullMode = BoundedChannelFullMode.DropOldest });

            emitter.Subscribe(tenantIdStr, channel.Writer);

            try
            {
                await ctx.Response.WriteAsync(": connected\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);

                while (!ct.IsCancellationRequested)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(15_000);

                    try
                    {
                        var data = await channel.Reader.ReadAsync(cts.Token);
                        await ctx.Response.WriteAsync($"data: {data}\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        await ctx.Response.WriteAsync(": ping\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                emitter.Unsubscribe(tenantIdStr, channel.Writer);
            }
        });

        return app;
    }

    private static async Task<(string TenantIdStr, string SchemaName, string? Error)> ResolveTenantAsync(
        HttpContext ctx, PublicDbContext publicDb)
    {
        var tenantIdStr = ctx.User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            return (string.Empty, string.Empty, "Token inválido");

        var tenant = await publicDb.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return (string.Empty, string.Empty, "Tenant não encontrado");

        return (tenantIdStr, tenant.SchemaName, null);
    }
}

public record SendMessageRequest(string Text);
