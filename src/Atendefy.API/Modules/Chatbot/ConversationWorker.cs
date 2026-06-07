using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Infrastructure.Messaging;
using Atendefy.API.Infrastructure.RateLimiting;
using Atendefy.API.Modules.AI;
using Atendefy.API.Modules.AI.Models;
using Atendefy.API.Modules.Chatbot.Models;
using Atendefy.API.Modules.WhatsApp;
using Atendefy.API.Modules.WhatsApp.Models;
using Atendefy.API.SharedKernel.Extensions;
using Microsoft.EntityFrameworkCore;
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

        await using var tenantDb = tenantDbFactory.Create(msg.SchemaName);
        var aiConfig = await tenantDb.AiConfigs.FirstOrDefaultAsync();
        if (aiConfig is null)
        {
            logger.LogWarning("Tenant {TenantId} sem config de IA", msg.TenantId);
            return;
        }

        var waAccount = await tenantDb.WhatsAppAccounts.FindAsync(Guid.Parse(msg.AccountId));
        if (waAccount?.ConfigJson is null)
        {
            logger.LogWarning("Conta WhatsApp {AccountId} não encontrada", msg.AccountId);
            return;
        }

        var history = await conversationService.GetOrCreateSessionAsync(msg.TenantId, msg.ContactPhone);
        var contextMessages = ConversationService.BuildContextMessages(history, msg.MessageText);

        var decryptedKey = AesEncryption.Decrypt(aiConfig.ApiKeyEncrypted!, encryptionKey);
        var aiProvider = aiFactory.Create(aiConfig.Provider, decryptedKey);
        var aiResult = await aiProvider.CompleteAsync(new AICompletionRequest(
            SystemPrompt: aiConfig.SystemPrompt ?? "Você é um assistente prestativo.",
            Messages: contextMessages,
            Model: aiConfig.Model ?? "gpt-4o-mini"
        ));

        contextMessages.Add(new("assistant", aiResult.Content));
        await conversationService.SaveSessionAsync(msg.TenantId, msg.ContactPhone, contextMessages);

        var conversationId = await ConversationService.PersistAsync(
            tenantDbFactory, msg.SchemaName, msg.ContactPhone,
            msg.MessageText, aiResult.Content, aiResult.TokensUsed);

        emitter.Emit(msg.TenantId, JsonSerializer.Serialize(
            new { type = "message_added", conversationId }));

        logger.LogInformation("Mensagem processada para tenant {TenantId}, contato {Phone}",
            msg.TenantId, msg.ContactPhone);

        try
        {
            var waProvider = whatsAppFactory.Create(waAccount.Provider, waAccount.ConfigJson);
            await waProvider.SendMessageAsync(new OutboundMessage(msg.ContactPhone, aiResult.Content));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enviar resposta WhatsApp para {Phone} (conversa salva)", msg.ContactPhone);
        }
    }
}
