using Atendefy.API.Infrastructure.Cache;
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.AI.Models;
using Atendefy.API.Modules.Chatbot.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Atendefy.API.Modules.Chatbot;

public class ConversationService(RedisService redis)
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(30);

    private static string SessionKey(string tenantId, string phone)
        => $"session:{tenantId}:{phone}";

    public async Task<List<ChatMessage>> GetOrCreateSessionAsync(string tenantId, string phone)
    {
        var json = await redis.GetAsync(SessionKey(tenantId, phone));
        if (string.IsNullOrEmpty(json)) return [];
        return JsonSerializer.Deserialize<List<ChatMessage>>(json) ?? [];
    }

    public async Task SaveSessionAsync(string tenantId, string phone, List<ChatMessage> messages)
    {
        var json = JsonSerializer.Serialize(messages);
        await redis.SetAsync(SessionKey(tenantId, phone), json, SessionTtl);
    }

    public static List<ChatMessage> BuildContextMessages(
        List<ChatMessage> history, string newUserMessage)
    {
        var messages = new List<ChatMessage>(history) { new("user", newUserMessage) };
        if (messages.Count > 20)
            messages = messages.TakeLast(20).ToList();
        return messages;
    }

    public static async Task<Guid> PersistAsync(
        TenantDbContextFactory dbFactory,
        string schemaName,
        string contactPhone,
        string userMessage,
        string assistantReply,
        int tokensUsed,
        Guid? accountId = null)
    {
        await using var db = dbFactory.Create(schemaName);

        var conversation = await db.Conversations
            .FirstOrDefaultAsync(c => c.ContactPhone == contactPhone);

        if (conversation is null)
        {
            conversation = new Conversation { ContactPhone = contactPhone, AccountId = accountId };
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync();
        }
        else if (accountId.HasValue && conversation.AccountId is null)
        {
            conversation.AccountId = accountId;
        }

        db.Messages.AddRange(
            new ConversationMessage
            {
                ConversationId = conversation.Id,
                Role = "user",
                Content = userMessage
            },
            new ConversationMessage
            {
                ConversationId = conversation.Id,
                Role = "assistant",
                Content = assistantReply,
                TokensUsed = tokensUsed
            });

        conversation.MessageCount += 2;

        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var counter = await db.UsageCounters.FindAsync(month);
        if (counter is null)
        {
            counter = new UsageCounter { Month = month };
            db.UsageCounters.Add(counter);
        }
        counter.MessagesSent++;
        counter.TokensConsumed += tokensUsed;

        await db.SaveChangesAsync();
        return conversation.Id;
    }

    public static async Task<Guid> PersistUserOnlyAsync(
        TenantDbContextFactory dbFactory,
        string schemaName,
        string contactPhone,
        string userMessage,
        Guid? accountId = null)
    {
        await using var db = dbFactory.Create(schemaName);

        var conversation = await db.Conversations
            .FirstOrDefaultAsync(c => c.ContactPhone == contactPhone);

        if (conversation is null)
        {
            conversation = new Conversation { ContactPhone = contactPhone, AccountId = accountId };
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync();
        }
        else if (accountId.HasValue && conversation.AccountId is null)
        {
            conversation.AccountId = accountId;
        }

        db.Messages.Add(new ConversationMessage
        {
            ConversationId = conversation.Id,
            Role = "user",
            Content = userMessage
        });

        conversation.MessageCount++;
        await db.SaveChangesAsync();
        return conversation.Id;
    }
}
