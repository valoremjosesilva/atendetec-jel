using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Infrastructure.Messaging;
using Atendefy.API.Infrastructure.RateLimiting;
using Atendefy.API.Modules.AI;
using Atendefy.API.Modules.AI.Models;
using Atendefy.API.Modules.Chatbot.Models;
using Atendefy.API.Modules.Scheduling.Models;
using Atendefy.API.Modules.Tenants;
using Atendefy.API.Modules.WhatsApp;
using Atendefy.API.Modules.WhatsApp.Models;
using Atendefy.API.SharedKernel.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Atendefy.API.Modules.Chatbot;

public class ConversationWorker(
    RedisStreamService streams,
    ConversationService conversationService,
    TenantDbContextFactory tenantDbFactory,
    AIProviderFactory aiFactory,
    WhatsAppProviderFactory whatsAppFactory,
    TenantRateLimiter rateLimiter,
    string encryptionKey,
    IConversationEventEmitter emitter,
    IServiceScopeFactory scopeFactory,
    Atendefy.API.Infrastructure.Cache.RedisService redis,
    ILogger<ConversationWorker> logger) : BackgroundService
{
    private const string StreamName = "messages.inbound";
    private const string GroupName = "conversation-workers";
    private const string ConsumerName = "worker-1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await streams.EnsureConsumerGroupAsync(StreamName, GroupName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var entries = await streams.ReadGroupAsync(StreamName, GroupName, ConsumerName);

                if (entries.Length == 0)
                {
                    await Task.Delay(500, stoppingToken);
                    continue;
                }

                foreach (var entry in entries)
                {
                    await ProcessEntryAsync(entry);
                    await streams.AcknowledgeAsync(StreamName, GroupName, entry.Id);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Erro no ConversationWorker");
                await Task.Delay(2000, stoppingToken);
            }
        }
    }

    private async Task ProcessEntryAsync(StackExchange.Redis.StreamEntry entry)
    {
        var fields = entry.Values.ToDictionary(v => v.Name.ToString(), v => v.Value.ToString());

        var msg = new InboundMessage(
            TenantId: fields["tenant_id"],
            SchemaName: fields["schema_name"],
            ContactPhone: fields["contact_phone"],
            MessageText: fields["message_text"],
            Provider: fields["provider"],
            AccountId: fields["account_id"]
        );

        if (!await rateLimiter.IsAllowedAsync(msg.TenantId))
        {
            logger.LogWarning("Rate limit atingido para tenant {TenantId}", msg.TenantId);
            return;
        }

        var accountId = Guid.TryParse(msg.AccountId, out var parsedId) ? parsedId : (Guid?)null;

        // Entitlements do plano do tenant (IA on/off, agenda on/off, etc.).
        var limits = await ResolveLimitsAsync(msg.TenantId);

        // IA desligada no plano: a mensagem do cliente é salva (atendimento humano em Conversas),
        // mas o bot não responde.
        if (!limits.AiEnabled)
        {
            await PersistWithoutReplyAsync(msg, accountId);
            logger.LogInformation("IA desativada no plano do tenant {TenantId} — mensagem salva sem resposta", msg.TenantId);
            return;
        }

        // Teto de mensagens/mês do plano: contador mensal por tenant no Redis. Atingido o limite,
        // a mensagem é salva (atendimento humano), mas a IA não responde até virar o mês.
        var monthlyUsed = await GetMonthlyUsageAsync(msg.TenantId);
        if (monthlyUsed >= limits.MessagesPerMonth)
        {
            await PersistWithoutReplyAsync(msg, accountId);
            logger.LogInformation(
                "Teto de mensagens/mês atingido para tenant {TenantId} ({Used}/{Limit}) — IA suspensa no mês",
                msg.TenantId, monthlyUsed, limits.MessagesPerMonth);
            return;
        }

        // Check BotPaused before loading AI config to avoid unnecessary DB reads
        await using (var checkDb = tenantDbFactory.Create(msg.SchemaName))
        {
            var existing = await checkDb.Conversations
                .FirstOrDefaultAsync(c => c.ContactPhone == msg.ContactPhone);

            if (existing?.BotPaused == true || existing?.IsResolved == true)
            {
                var convId = await ConversationService.PersistUserOnlyAsync(
                    tenantDbFactory, msg.SchemaName, msg.ContactPhone, msg.MessageText, accountId);
                await UpsertContactAsync(msg.SchemaName, msg.ContactPhone);
                emitter.Emit(msg.TenantId, JsonSerializer.Serialize(
                    new { type = "message_added", conversationId = convId }));
                logger.LogInformation("Bot pausado para {Phone} — mensagem salva sem resposta", msg.ContactPhone);
                return;
            }
        }

        await using var tenantDb = tenantDbFactory.Create(msg.SchemaName);
        var aiConfig = await tenantDb.AiConfigs.FirstOrDefaultAsync();
        if (aiConfig is null)
        {
            logger.LogWarning("Tenant {TenantId} sem config de IA", msg.TenantId);
            return;
        }

        var waAccount = await tenantDb.WhatsAppAccounts.FindAsync(accountId ?? Guid.Empty);
        if (waAccount?.ConfigJson is null)
        {
            logger.LogWarning("Conta WhatsApp {AccountId} não encontrada", msg.AccountId);
            return;
        }

        var history = await conversationService.GetOrCreateSessionAsync(msg.TenantId, msg.ContactPhone);
        var contextMessages = ConversationService.BuildContextMessages(history, msg.MessageText);

        // Agendamento (handoff por link): se a conta tem agenda ativa, injeta o link no system
        // prompt para o bot oferecê-lo quando o cliente quiser marcar.
        var systemPrompt = aiConfig.SystemPrompt ?? "Você é um assistente prestativo.";
        // Defensivo: schemas de tenants antigos podem não ter a tabela calendar_configs ainda
        // (criada por tenant no provisioner). Falha aqui => agendamento off, mensagem segue normal.
        CalendarConfig? calendar = null;
        try { calendar = await tenantDb.CalendarConfigs.FirstOrDefaultAsync(); }
        catch (Exception ex) { logger.LogWarning(ex, "calendar_configs indisponível para {Schema} — agendamento desativado", msg.SchemaName); }
        var schedulingOn = limits.SchedulingEnabled
            && calendar is { Enabled: true } && !string.IsNullOrWhiteSpace(calendar.BookingUrl);
        if (schedulingOn)
            systemPrompt += BuildSchedulingInstruction(calendar!);

        var decryptedKey = AesEncryption.Decrypt(aiConfig.ApiKeyEncrypted!, encryptionKey);
        var aiProvider = aiFactory.Create(aiConfig.Provider, decryptedKey);
        var aiResult = await aiProvider.CompleteAsync(new AICompletionRequest(
            SystemPrompt: systemPrompt,
            Messages: contextMessages,
            Model: aiConfig.Model ?? "gpt-4o-mini"
        ));

        // Rede de segurança: se o cliente pediu agendamento e o link não saiu na resposta, anexa.
        var replyText = aiResult.Content;
        if (schedulingOn && MentionsScheduling(msg.MessageText) && !replyText.Contains(calendar!.BookingUrl!))
            replyText += $"\n\nPara agendar, acesse: {calendar.BookingUrl}";

        contextMessages.Add(new("assistant", replyText));
        await conversationService.SaveSessionAsync(msg.TenantId, msg.ContactPhone, contextMessages);

        var conversationId = await ConversationService.PersistAsync(
            tenantDbFactory, msg.SchemaName, msg.ContactPhone,
            msg.MessageText, replyText, aiResult.TokensUsed,
            accountId);

        // Conta esta resposta da IA no teto mensal do plano.
        await IncrementMonthlyUsageAsync(msg.TenantId);

        await UpsertContactAsync(msg.SchemaName, msg.ContactPhone);

        emitter.Emit(msg.TenantId, JsonSerializer.Serialize(
            new { type = "message_added", conversationId }));

        logger.LogInformation("Mensagem processada para tenant {TenantId}, contato {Phone}",
            msg.TenantId, msg.ContactPhone);

        try
        {
            var waProvider = whatsAppFactory.Create(waAccount.Provider, waAccount.ConfigJson);
            await waProvider.SendMessageAsync(new OutboundMessage(msg.ContactPhone, replyText));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enviar resposta WhatsApp para {Phone} (conversa salva)", msg.ContactPhone);
        }
    }

    // O worker é singleton; EntitlementsService é scoped (usa PublicDbContext). Resolve num scope curto.
    // Em caso de falha, devolve o fallback "Free" (não bloqueia o atendimento por erro de infra).
    private async Task<Billing.Models.PlanLimits> ResolveLimitsAsync(string tenantId)
    {
        if (!Guid.TryParse(tenantId, out var tid)) return EntitlementsService.FreeFallback;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var entitlements = scope.ServiceProvider.GetRequiredService<EntitlementsService>();
            return await entitlements.GetForTenantAsync(tid);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao resolver entitlements do tenant {TenantId} — usando fallback", tenantId);
            return EntitlementsService.FreeFallback;
        }
    }

    // Salva a mensagem do cliente sem resposta da IA (bot pausado, IA off no plano, ou teto atingido)
    // e notifica o painel. Não conta no teto mensal (só respostas geradas pela IA contam).
    private async Task PersistWithoutReplyAsync(InboundMessage msg, Guid? accountId)
    {
        var convId = await ConversationService.PersistUserOnlyAsync(
            tenantDbFactory, msg.SchemaName, msg.ContactPhone, msg.MessageText, accountId);
        await UpsertContactAsync(msg.SchemaName, msg.ContactPhone);
        emitter.Emit(msg.TenantId, JsonSerializer.Serialize(
            new { type = "message_added", conversationId = convId }));
    }

    // Chave do contador mensal de uso da IA por tenant (expira sozinha após o mês).
    private static string MonthlyUsageKey(string tenantId) =>
        EntitlementsService.MonthlyUsageKey(tenantId, DateTime.UtcNow);

    private async Task<long> GetMonthlyUsageAsync(string tenantId)
    {
        try { return await redis.GetCounterAsync(MonthlyUsageKey(tenantId)); }
        catch (Exception ex)
        {
            // Falha de infra não deve bloquear o atendimento: assume 0 (não atingiu o teto).
            logger.LogWarning(ex, "Falha ao ler uso mensal do tenant {TenantId} — assumindo 0", tenantId);
            return 0;
        }
    }

    private async Task IncrementMonthlyUsageAsync(string tenantId)
    {
        try { await redis.IncrementWithTtlAsync(MonthlyUsageKey(tenantId), TimeSpan.FromDays(40)); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao incrementar uso mensal do tenant {TenantId}", tenantId);
        }
    }

    private static string BuildSchedulingInstruction(CalendarConfig cal) =>
        "\n\nAGENDAMENTO: quando o cliente quiser agendar, marcar, remarcar ou ver disponibilidade, " +
        $"envie EXATAMENTE este link de agendamento: {cal.BookingUrl}" +
        (string.IsNullOrWhiteSpace(cal.Instructions) ? "" : $"\n{cal.Instructions}");

    private static bool MentionsScheduling(string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            text, @"agend|hor[áa]rio|marcar|remarcar|consulta|disponibilidade|reservar",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private async Task UpsertContactAsync(string schemaName, string phone)
    {
        try
        {
            await using var db = tenantDbFactory.Create(schemaName);
            if (!await db.Contacts.AnyAsync(c => c.Phone == phone))
            {
                db.Contacts.Add(new Contact { Phone = phone });
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao upsert contact {Phone}", phone);
        }
    }
}
