using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Infrastructure.Messaging;
using Atendefy.API.Infrastructure.RateLimiting;
using Atendefy.API.Modules.AI;
using Atendefy.API.Modules.AI.Models;
using Atendefy.API.Modules.Chatbot.Models;
using Atendefy.API.Modules.Scheduling.Horafy;
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
    BookingFlowService bookingFlow,
    AiConfigService aiConfigService,
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
            AccountId: fields["account_id"],
            ContactName: fields.TryGetValue("contact_name", out var cn) && !string.IsNullOrWhiteSpace(cn) ? cn : null
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
                await UpsertContactAsync(msg.SchemaName, msg.ContactPhone, msg.ContactName);
                emitter.Emit(msg.TenantId, JsonSerializer.Serialize(
                    new { type = "message_added", conversationId = convId }));
                logger.LogInformation("Bot pausado para {Phone} — mensagem salva sem resposta", msg.ContactPhone);
                return;
            }
        }

        // Config de IA via cache Redis (TTL 1h, invalidado no upsert) — evita uma
        // query por mensagem no caminho quente. Ver AiConfigService.GetAsync.
        var aiConfig = await aiConfigService.GetAsync(msg.SchemaName);
        if (aiConfig is null)
        {
            logger.LogWarning("Tenant {TenantId} sem config de IA", msg.TenantId);
            return;
        }

        await using var tenantDb = tenantDbFactory.Create(msg.SchemaName);
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

        // Agendamento via API (Horafy): assume a conversa quando há um fluxo ativo
        // ou quando o cliente demonstra intenção de agendar. Bypassa a IA genérica.
        if (limits.SchedulingEnabled && calendar is { Provider: "horafy", Enabled: true }
            && !string.IsNullOrEmpty(calendar.ApiBaseUrl) && !string.IsNullOrEmpty(calendar.TenantSlug)
            && !string.IsNullOrEmpty(calendar.ApiKeyEncrypted))
        {
            HorafyConnection? horafyConn = null;
            try
            {
                horafyConn = new HorafyConnection(
                    calendar.ApiBaseUrl!, calendar.TenantSlug!,
                    AesEncryption.Decrypt(calendar.ApiKeyEncrypted!, encryptionKey));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao montar conexão Horafy para {Schema}", msg.SchemaName);
            }

            if (horafyConn is not null
                && (await bookingFlow.HasActiveFlowAsync(msg.TenantId, msg.ContactPhone)
                    || MentionsScheduling(msg.MessageText)))
            {
                BookingFlowReply flowReply;
                try
                {
                    var aiProv = aiFactory.Create(aiConfig.Provider,
                        AesEncryption.Decrypt(aiConfig.ApiKeyEncrypted!, encryptionKey));
                    flowReply = await bookingFlow.HandleAsync(
                        horafyConn, calendar, msg.TenantId, msg.ContactPhone, msg.ContactName,
                        msg.MessageText, aiProv, aiConfig.Model ?? "gpt-4o-mini");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Erro no fluxo de agendamento Horafy para {Phone}", msg.ContactPhone);
                    flowReply = new BookingFlowReply("Tive um problema ao acessar a agenda agora. Pode tentar de novo em instantes?");
                }

                await DeliverReplyAsync(msg, accountId, waAccount, flowReply);
                return;
            }
        }
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

        await UpsertContactAsync(msg.SchemaName, msg.ContactPhone, msg.ContactName);

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
        await UpsertContactAsync(msg.SchemaName, msg.ContactPhone, msg.ContactName);
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

    // Persiste a troca (sem usar a sessão da IA), atualiza o contato, notifica o painel
    // e envia a resposta pelo WhatsApp (interativa quando disponível). Usado pelo fluxo de
    // agendamento (Horafy).
    private async Task DeliverReplyAsync(
        InboundMessage msg, Guid? accountId, WhatsAppAccount waAccount, BookingFlowReply reply)
    {
        var conversationId = await ConversationService.PersistAsync(
            tenantDbFactory, msg.SchemaName, msg.ContactPhone,
            msg.MessageText, reply.Text, tokensUsed: 0, accountId);

        await UpsertContactAsync(msg.SchemaName, msg.ContactPhone, msg.ContactName);

        emitter.Emit(msg.TenantId, JsonSerializer.Serialize(
            new { type = "message_added", conversationId }));

        try
        {
            var waProvider = whatsAppFactory.Create(waAccount.Provider, waAccount.ConfigJson!);
            if (reply.Interactive is not null)
                await waProvider.SendInteractiveAsync(msg.ContactPhone, reply.Interactive);
            else
                await waProvider.SendMessageAsync(new OutboundMessage(msg.ContactPhone, reply.Text));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enviar resposta WhatsApp para {Phone} (conversa salva)", msg.ContactPhone);
        }
    }

    private async Task UpsertContactAsync(string schemaName, string phone, string? name = null)
    {
        try
        {
            var trimmedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            await using var db = tenantDbFactory.Create(schemaName);
            var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Phone == phone);
            if (contact is null)
            {
                db.Contacts.Add(new Contact { Phone = phone, Name = trimmedName });
                await db.SaveChangesAsync();
            }
            else if (trimmedName is not null && contact.Name != trimmedName)
            {
                // Atualiza o nome quando o WhatsApp informa um pushName novo/diferente.
                contact.Name = trimmedName;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao upsert contact {Phone}", phone);
        }
    }
}
