using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Infrastructure.Messaging;
using Atendefy.API.Modules.Chatbot.Models;
using Atendefy.API.Modules.Webhooks.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Atendefy.API.Modules.Webhooks;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/webhooks").WithTags("Webhooks");

        // Meta: webhook verification (GET)
        group.MapGet("/meta", (
            [FromQuery(Name = "hub.mode")] string? mode,
            [FromQuery(Name = "hub.challenge")] string? challenge,
            [FromQuery(Name = "hub.verify_token")] string? verifyToken,
            IConfiguration config) =>
        {
            var expectedToken = config["Meta:WebhookVerifyToken"];
            return mode == "subscribe" && verifyToken == expectedToken
                ? Results.Ok(int.Parse(challenge ?? "0"))
                : Results.Forbid();
        });

        // Meta: receive message (POST)
        group.MapPost("/meta", async (
            HttpContext ctx,
            PublicDbContext publicDb,
            RedisStreamService streams,
            MetaWebhookValidator validator) =>
        {
            ctx.Request.EnableBuffering();
            var body = await ReadAllBytesAsync(ctx.Request.Body);
            ctx.Request.Body.Position = 0;

            var signature = ctx.Request.Headers["X-Hub-Signature-256"].ToString();
            if (!validator.IsValid(body, signature))
                return Results.Forbid();

            var payload = JsonSerializer.Deserialize<MetaWebhookPayload>(
                body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload is null) return Results.Ok();

            foreach (var entry in payload.Entry)
            foreach (var change in entry.Changes.Where(c => c.Field == "messages"))
            foreach (var msg in change.Value.Messages ?? [])
            {
                if (msg.Type != "text" || msg.Text is null) continue;

                var route = await publicDb.WebhookRoutes
                    .FirstOrDefaultAsync(r => r.Provider == "meta"
                        && r.LookupKey == change.Value.Metadata.PhoneNumberId);
                if (route is null) continue;

                var tenant = await publicDb.Tenants.FindAsync(route.TenantId);
                if (tenant is null) continue;

                await PublishAsync(streams, new InboundMessage(
                    TenantId: tenant.Id.ToString(),
                    SchemaName: tenant.SchemaName,
                    ContactPhone: msg.From,
                    MessageText: msg.Text.Body,
                    Provider: "meta",
                    AccountId: route.AccountId.ToString()
                ));
            }

            return Results.Ok();
        });

        // Evolution: receive message (POST)
        group.MapPost("/evolution", async (
            HttpContext ctx,
            RedisStreamService streams,
            EvolutionWebhookValidator evolutionValidator,
            [FromQuery] string? token) =>
        {
            if (string.IsNullOrEmpty(token))
                return Results.Forbid();

            var route = await evolutionValidator.ResolveAsync(token);
            if (route is null) return Results.Forbid();

            var payload = await ctx.Request.ReadFromJsonAsync<EvolutionWebhookPayload>();
            if (payload is null || payload.Event != "messages.upsert") return Results.Ok();
            if (payload.Data.Key.FromMe) return Results.Ok();

            var messageText = payload.Data.Message?.Conversation;
            if (string.IsNullOrWhiteSpace(messageText)) return Results.Ok();

            var publicDb = ctx.RequestServices.GetRequiredService<PublicDbContext>();
            var tenant = await publicDb.Tenants.FindAsync(route.TenantId);
            if (tenant is null) return Results.Ok();

            var phone = payload.Data.Key.RemoteJid.Replace("@s.whatsapp.net", "");

            await PublishAsync(streams, new InboundMessage(
                TenantId: tenant.Id.ToString(),
                SchemaName: tenant.SchemaName,
                ContactPhone: phone,
                MessageText: messageText,
                Provider: "evolution",
                AccountId: route.AccountId.ToString()
            ));

            return Results.Ok();
        });

        return app;
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static Task PublishAsync(RedisStreamService streams, InboundMessage msg)
        => streams.PublishAsync("messages.inbound", new Dictionary<string, string>
        {
            ["tenant_id"]     = msg.TenantId,
            ["schema_name"]   = msg.SchemaName,
            ["contact_phone"] = msg.ContactPhone,
            ["message_text"]  = msg.MessageText,
            ["provider"]      = msg.Provider,
            ["account_id"]    = msg.AccountId
        });
}
