using Atendefy.API.Infrastructure.Cache;
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Infrastructure.Messaging;
using Atendefy.API.Modules.Chatbot;
using Atendefy.API.Modules.Chatbot.Models;
using Atendefy.API.Modules.Scheduling;
using Atendefy.API.Modules.Scheduling.Models;
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
            MetaWebhookValidator validator,
            RedisService redis) =>
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
                // Texto comum ou resposta interativa (lista/botão) — usamos o id selecionado.
                var messageText = msg.Type switch
                {
                    "text" => msg.Text?.Body,
                    "interactive" => msg.Interactive?.ButtonReply?.Id ?? msg.Interactive?.ListReply?.Id,
                    _ => null
                };
                if (string.IsNullOrWhiteSpace(messageText)) continue;

                // Deduplicar: Meta replica webhooks em retry. Pular se já processado.
                if (!string.IsNullOrEmpty(msg.Id)
                    && await IsDuplicateAsync(redis, "meta", msg.Id))
                    continue;

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
                    MessageText: messageText,
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
            [FromQuery] string? token,
            RedisService redis) =>
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

            // Deduplicar: Evolution pode reenviar o mesmo webhook após restart.
            if (!string.IsNullOrEmpty(payload.Data.Key.Id)
                && await IsDuplicateAsync(redis, "evolution", payload.Data.Key.Id))
                return Results.Ok();

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
                AccountId: route.AccountId.ToString(),
                ContactName: payload.Data.PushName
            ));

            return Results.Ok();
        });

        // Cal.com: write-back de agendamentos (Fase 3). Roteado por ?token= (webhook_routes).
        group.MapPost("/calcom", async (
            HttpContext ctx,
            PublicDbContext publicDb,
            SchedulingService scheduling,
            ILoggerFactory loggerFactory,
            [FromQuery] string? token) =>
        {
            var logger = loggerFactory.CreateLogger("CalcomWebhook");
            if (string.IsNullOrEmpty(token)) return Results.Forbid();

            var route = await publicDb.WebhookRoutes
                .FirstOrDefaultAsync(r => r.Provider == "calcom" && r.LookupKey == token);
            if (route is null) return Results.Forbid();

            var tenant = await publicDb.Tenants.FindAsync(route.TenantId);
            if (tenant is null) return Results.Ok();

            try
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
                var appt = CalcomPayloadParser.Parse(doc.RootElement);
                if (appt is not null)
                    await scheduling.UpsertAppointmentAsync(tenant.SchemaName, appt);
            }
            catch (Exception ex)
            {
                // Nunca devolve erro (evita retry-storm do Cal.com); só registra.
                logger.LogWarning(ex, "Falha ao processar webhook Cal.com para tenant {TenantId}", route.TenantId);
            }

            return Results.Ok();
        });

        // Horafy: write-back de agendamentos (Fase 4). Roteado por ?token= (webhook_routes),
        // com validação de assinatura HMAC quando o segredo está configurado.
        group.MapPost("/horafy", async (
            HttpContext ctx,
            PublicDbContext publicDb,
            SchedulingService scheduling,
            IConversationEventEmitter emitter,
            ILoggerFactory loggerFactory,
            [FromQuery] string? token) =>
        {
            var logger = loggerFactory.CreateLogger("HorafyWebhook");
            if (string.IsNullOrEmpty(token)) return Results.Forbid();

            var route = await publicDb.WebhookRoutes
                .FirstOrDefaultAsync(r => r.Provider == "horafy" && r.LookupKey == token);
            if (route is null) return Results.Forbid();

            var tenant = await publicDb.Tenants.FindAsync(route.TenantId);
            if (tenant is null) return Results.Ok();

            ctx.Request.EnableBuffering();
            var body = await ReadAllBytesAsync(ctx.Request.Body);
            ctx.Request.Body.Position = 0;

            // HMAC: valida quando há segredo configurado para o tenant.
            var secret = await scheduling.GetHorafyWebhookSecretAsync(tenant.SchemaName);
            var signature = ctx.Request.Headers["X-Horafy-Signature"].ToString();
            if (!string.IsNullOrEmpty(secret) && !HorafyWebhook.VerifySignature(secret, body, signature))
            {
                logger.LogWarning("Assinatura HMAC inválida no webhook Horafy (tenant {TenantId})", route.TenantId);
                return Results.Unauthorized();
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var appt = HorafyWebhook.Parse(doc.RootElement);
                if (appt is not null)
                {
                    await scheduling.UpsertAppointmentAsync(tenant.SchemaName, appt);
                    emitter.Emit(tenant.Id.ToString(),
                        JsonSerializer.Serialize(new { type = "appointment_updated" }));
                }
            }
            catch (Exception ex)
            {
                // Nunca devolve erro de parse (evita retry-storm); só registra.
                logger.LogWarning(ex, "Falha ao processar webhook Horafy para tenant {TenantId}", route.TenantId);
            }

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
            ["account_id"]    = msg.AccountId,
            ["contact_name"]  = msg.ContactName ?? string.Empty
        });

    private static async Task<bool> IsDuplicateAsync(
        RedisService redis, string provider, string messageId)
    {
        var key = $"webhook:dedup:{provider}:{messageId}";
        if (await redis.ExistsAsync(key)) return true;
        await redis.SetAsync(key, "1", TimeSpan.FromHours(24));
        return false;
    }
}
