# Human Takeover + Manual Send + Contacts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir que o tenant pause o bot e responda manualmente em conversas individuais, e ver/editar uma agenda de contatos com histórico de conversas.

**Architecture:** A. Campo `BotPaused` em `Conversation` controla o `ConversationWorker` (mensagens do usuário são salvas mas não respondidas quando pausado). Campo `AccountId` permite que o endpoint de envio manual localize a conta WhatsApp. B. Tabela `contacts` per-tenant (upsert automático no worker) alimenta endpoints GET + PATCH. Como o tenant schema usa SQL raw (não EF migrations), novos campos/tabelas precisam de `ALTER TABLE / CREATE TABLE IF NOT EXISTS` no startup para schemas de tenants existentes.

**Tech Stack:** ASP.NET Core minimal API, EF Core DbSet para Contact, raw SQL para schema migrations, React Query mutations, base-ui Dialog/components.

---

## Mapa de Arquivos

**Backend — modificar:**
- `src/Atendefy.API/Modules/Chatbot/Models/Conversation.cs` — add `BotPaused`, `AccountId`
- `src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs` — map new columns + Contact DbSet
- `src/Atendefy.API/Modules/Tenants/TenantProvisioner.cs` — add columns to CREATE TABLE, add contacts table
- `src/Atendefy.API/Program.cs` — startup ALTER TABLE for existing tenants
- `src/Atendefy.API/Modules/Chatbot/ConversationService.cs` — add `accountId` param, add `PersistUserOnlyAsync`
- `src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs` — check BotPaused, pass accountId, upsert contact
- `src/Atendefy.API/Modules/Chatbot/ConversationEndpoints.cs` — takeover/release/manual-send endpoints; include `botPaused` in GET responses

**Backend — criar:**
- `src/Atendefy.API/Modules/Chatbot/Models/Contact.cs` — Contact entity
- `src/Atendefy.API/Modules/Chatbot/ContactEndpoints.cs` — GET /contacts + PATCH /contacts/{phone}

**Frontend — modificar:**
- `src/Atendefy.Web/src/types/api.ts` — add `botPaused` to summary/detail, add contact types
- `src/Atendefy.Web/src/hooks/useConversations.ts` — add takeover/release/sendMessage mutations
- `src/Atendefy.Web/src/pages/ConversationsPage.tsx` — takeover/release button + manual send input
- `src/Atendefy.Web/src/App.tsx` — add /contacts route
- `src/Atendefy.Web/src/components/layout/Sidebar.tsx` — add Contatos nav item

**Frontend — criar:**
- `src/Atendefy.Web/src/hooks/useContacts.ts` — contacts query hooks
- `src/Atendefy.Web/src/pages/ContactsPage.tsx` — contacts list with inline name edit

---

### Task 1: Conversation entity — BotPaused + AccountId + schema migrations

**Files:**
- Modify: `src/Atendefy.API/Modules/Chatbot/Models/Conversation.cs`
- Modify: `src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs`
- Modify: `src/Atendefy.API/Modules/Tenants/TenantProvisioner.cs`
- Modify: `src/Atendefy.API/Program.cs`

- [ ] **Substituir `Conversation.cs` completo**

```csharp
namespace Atendefy.API.Modules.Chatbot.Models;

public class Conversation
{
    public Guid Id { get; set; }
    public string ContactPhone { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public int MessageCount { get; set; }
    public bool IsDeleted { get; set; }
    public bool BotPaused { get; set; }
    public Guid? AccountId { get; set; }
    public List<ConversationMessage> Messages { get; set; } = [];
}
```

- [ ] **Atualizar mapeamento em `TenantDbContext.cs`**

Dentro de `modelBuilder.Entity<Conversation>(e => { ... })`, adicionar após a linha do `HasQueryFilter`:

```csharp
e.Property(x => x.BotPaused).HasDefaultValue(false);
e.Property(x => x.AccountId);
```

- [ ] **Atualizar `TenantProvisioner.cs` — conversations CREATE TABLE**

No bloco SQL, substituir a criação da tabela `conversations` por:

```sql
CREATE TABLE IF NOT EXISTS "{schemaName}".conversations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    contact_phone VARCHAR(30) NOT NULL,
    started_at TIMESTAMPTZ DEFAULT NOW(),
    message_count INT DEFAULT 0,
    is_deleted BOOLEAN DEFAULT FALSE,
    bot_paused BOOLEAN DEFAULT FALSE,
    account_id UUID
);
```

- [ ] **Adicionar tabela `contacts` ao `TenantProvisioner.cs`**

Logo após o bloco `usage_counters` no mesmo heredoc SQL, adicionar:

```sql
CREATE TABLE IF NOT EXISTS "{schemaName}".contacts (
    phone VARCHAR(30) PRIMARY KEY,
    name VARCHAR(200),
    created_at TIMESTAMPTZ DEFAULT NOW()
);
```

- [ ] **Startup migration para tenants existentes em `Program.cs`**

Adicionar `using Npgsql;` no topo do arquivo. Após o bloco de migração pública (`await db.Database.MigrateAsync()`), adicionar:

```csharp
// Tenant schema migrations (idempotent — safe to re-run on every startup)
using (var tenantMigScope = app.Services.CreateScope())
{
    var publicDb2 = tenantMigScope.ServiceProvider.GetRequiredService<PublicDbContext>();
    var tenants = await publicDb2.Tenants.ToListAsync();
    foreach (var t in tenants)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var migSql = $"""
            ALTER TABLE IF EXISTS "{t.SchemaName}".conversations
                ADD COLUMN IF NOT EXISTS bot_paused BOOLEAN DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS account_id UUID;
            CREATE TABLE IF NOT EXISTS "{t.SchemaName}".contacts (
                phone VARCHAR(30) PRIMARY KEY,
                name VARCHAR(200),
                created_at TIMESTAMPTZ DEFAULT NOW()
            );
            """;
        await using var cmd = new NpgsqlCommand(migSql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
```

- [ ] **Build**

```powershell
dotnet build src/Atendefy.API/Atendefy.API.csproj
```

Esperado: 0 erros.

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/Chatbot/Models/Conversation.cs `
      src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs `
      src/Atendefy.API/Modules/Tenants/TenantProvisioner.cs `
      src/Atendefy.API/Program.cs
git commit -m "feat: BotPaused+AccountId on Conversation, contacts table, tenant schema migration"
```

---

### Task 2: Contact entity + TenantDbContext

**Files:**
- Create: `src/Atendefy.API/Modules/Chatbot/Models/Contact.cs`
- Modify: `src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs`

Esta task deve vir antes da Task 3 porque o Worker (Task 3) usa `db.Contacts`.

- [ ] **Criar `Contact.cs`**

```csharp
namespace Atendefy.API.Modules.Chatbot.Models;

public class Contact
{
    public string Phone { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Adicionar `DbSet<Contact>` e mapeamento em `TenantDbContext.cs`**

Após `public DbSet<UsageCounter> UsageCounters => Set<UsageCounter>();`, adicionar:

```csharp
public DbSet<Contact> Contacts => Set<Contact>();
```

Dentro de `OnModelCreating`, após o bloco `UsageCounter`, adicionar:

```csharp
modelBuilder.Entity<Contact>(e =>
{
    e.ToTable("contacts");
    e.HasKey(x => x.Phone);
    e.Property(x => x.Phone).HasMaxLength(30);
    e.Property(x => x.Name).HasMaxLength(200);
});
```

- [ ] **Build**

```powershell
dotnet build src/Atendefy.API/Atendefy.API.csproj
```

Esperado: 0 erros.

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/Chatbot/Models/Contact.cs `
      src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs
git commit -m "feat: Contact entity and TenantDbContext mapping"
```

---

### Task 3: ConversationService — accountId em PersistAsync + PersistUserOnlyAsync

**Files:**
- Modify: `src/Atendefy.API/Modules/Chatbot/ConversationService.cs`

Mudanças:
- `PersistAsync` recebe parâmetro opcional `Guid? accountId = null` e o seta na Conversation nova (ou preenche se ainda null)
- Novo `PersistUserOnlyAsync` que persiste apenas a mensagem do usuário (bot pausado — sem resposta da IA)
- Ambos continuam retornando `Guid` (sem quebrar chamadas existentes)

- [ ] **Substituir `ConversationService.cs` completo**

```csharp
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
```

- [ ] **Build**

```powershell
dotnet build src/Atendefy.API/Atendefy.API.csproj
```

Esperado: 0 erros.

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/Chatbot/ConversationService.cs
git commit -m "feat: PersistAsync accepts accountId, add PersistUserOnlyAsync for bot-paused path"
```

---

### Task 4: ConversationWorker — BotPaused check + accountId + upsert Contact

**Files:**
- Modify: `src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs`

Lógica nova:
1. Antes de chamar a IA, verificar se existe conversa com `BotPaused = true` para este `ContactPhone`
2. Se pausado: persistir só a mensagem do usuário via `PersistUserOnlyAsync`, upsert contact, emitir SSE, retornar
3. Caso normal: passar `accountId` para `PersistAsync`, upsert contact após persistir

- [ ] **Substituir `ConversationWorker.cs` completo**

```csharp
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

        var accountId = Guid.TryParse(msg.AccountId, out var parsedId) ? parsedId : (Guid?)null;

        // Check BotPaused before loading AI config to avoid unnecessary DB reads
        await using (var checkDb = tenantDbFactory.Create(msg.SchemaName))
        {
            var existing = await checkDb.Conversations
                .FirstOrDefaultAsync(c => c.ContactPhone == msg.ContactPhone);

            if (existing?.BotPaused == true)
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
            msg.MessageText, aiResult.Content, aiResult.TokensUsed,
            accountId);

        await UpsertContactAsync(msg.SchemaName, msg.ContactPhone);

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
```

- [ ] **Build**

```powershell
dotnet build src/Atendefy.API/Atendefy.API.csproj
```

Esperado: 0 erros.

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs
git commit -m "feat: ConversationWorker checks BotPaused, passes accountId, upserts contact"
```

---

### Task 5: Conversation endpoints — takeover, release, manual send, botPaused em GET

**Files:**
- Modify: `src/Atendefy.API/Modules/Chatbot/ConversationEndpoints.cs`

Novos endpoints:
- `PATCH /conversations/{id}/takeover` — sets `BotPaused = true`
- `PATCH /conversations/{id}/release` — sets `BotPaused = false`
- `POST /conversations/{id}/messages` — envia mensagem manual via WhatsApp, persiste com `role = "agent"`

GET existentes agora incluem `botPaused` na resposta.

O helper `ResolveSchemaAsync` é renomeado para `ResolveTenantAsync` e passa a retornar também `tenantIdStr` (necessário para o emitter SSE).

- [ ] **Substituir `ConversationEndpoints.cs` completo**

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Chatbot.Models;
using Atendefy.API.Modules.WhatsApp;
using Atendefy.API.Modules.WhatsApp.Models;
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
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
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
                    c.BotPaused,
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
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
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
                conversation.BotPaused,
                messages
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
        });

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
        });

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
                Role = "agent",
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
        });

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
```

- [ ] **Build**

```powershell
dotnet build src/Atendefy.API/Atendefy.API.csproj
```

Esperado: 0 erros.

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/Chatbot/ConversationEndpoints.cs
git commit -m "feat: takeover/release/manual-send endpoints, botPaused in GET responses"
```

---

### Task 6: Contact endpoints

**Files:**
- Create: `src/Atendefy.API/Modules/Chatbot/ContactEndpoints.cs`
- Modify: `src/Atendefy.API/Program.cs`

- [ ] **Criar `ContactEndpoints.cs`**

```csharp
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
```

- [ ] **Registrar em `Program.cs`**

Após `app.MapConversationEndpoints();`, adicionar:

```csharp
app.MapContactEndpoints();
```

- [ ] **Build**

```powershell
dotnet build src/Atendefy.API/Atendefy.API.csproj
```

Esperado: 0 erros.

- [ ] **Testar manualmente**

```powershell
$token = (curl.exe -s -X POST "http://localhost:8080/auth/login" `
  -H "Content-Type: application/json" -H "X-Tenant-Key: jel" `
  -d '{"email":"josedudev@gmail.com","password":"Test@123"}' | ConvertFrom-Json).accessToken

curl.exe -s "http://localhost:8080/contacts?page=1&pageSize=20" `
  -H "Authorization: Bearer $token"
```

Esperado: `{"contacts":[],"total":0,"page":1,"pageSize":20}` (vazio até chegar a primeira mensagem WhatsApp).

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/Chatbot/ContactEndpoints.cs `
      src/Atendefy.API/Program.cs
git commit -m "feat: GET /contacts and PATCH /contacts/{phone} endpoints"
```

---

### Task 7: Frontend — tipos + hooks de conversa

**Files:**
- Modify: `src/Atendefy.Web/src/types/api.ts`
- Modify: `src/Atendefy.Web/src/hooks/useConversations.ts`

- [ ] **Atualizar `types/api.ts`**

Substituir as interfaces `ConversationSummary` e `ConversationDetail` (mantendo todo o restante do arquivo):

```typescript
export interface ConversationSummary {
  id: string;
  contactPhone: string;
  messageCount: number;
  startedAt: string;
  lastMessageAt: string;
  botPaused: boolean;
}

export interface ConversationDetail {
  id: string;
  contactPhone: string;
  startedAt: string;
  messageCount: number;
  botPaused: boolean;
  messages: ConversationMessage[];
}
```

Adicionar no final do arquivo:

```typescript
export interface ContactSummary {
  phone: string;
  name?: string;
  createdAt: string;
  conversationCount: number;
  lastActivity?: string;
}

export interface ContactsListResponse {
  contacts: ContactSummary[];
  total: number;
  page: number;
  pageSize: number;
}
```

- [ ] **Substituir `useConversations.ts` completo**

```typescript
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import type { ConversationsListResponse, ConversationDetail } from '@/types/api';

export function useConversations(page = 1, pageSize = 20) {
  return useQuery({
    queryKey: ['conversations', page, pageSize],
    queryFn: () =>
      apiClient
        .get<ConversationsListResponse>('/conversations', { params: { page, pageSize } })
        .then((r) => r.data),
  });
}

export function useConversationMessages(id: string | null) {
  return useQuery({
    queryKey: ['conversations', id, 'messages'],
    queryFn: () =>
      apiClient
        .get<ConversationDetail>(`/conversations/${id}/messages`)
        .then((r) => r.data),
    enabled: !!id,
  });
}

export function useTakeoverConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.patch(`/conversations/${id}/takeover`).then((r) => r.data),
    onSuccess: (_data, id) => {
      queryClient.invalidateQueries({ queryKey: ['conversations'] });
      queryClient.invalidateQueries({ queryKey: ['conversations', id, 'messages'] });
    },
  });
}

export function useReleaseConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.patch(`/conversations/${id}/release`).then((r) => r.data),
    onSuccess: (_data, id) => {
      queryClient.invalidateQueries({ queryKey: ['conversations'] });
      queryClient.invalidateQueries({ queryKey: ['conversations', id, 'messages'] });
    },
  });
}

export function useSendMessage() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, text }: { id: string; text: string }) =>
      apiClient
        .post(`/conversations/${id}/messages`, { text })
        .then((r) => r.data),
    onSuccess: (_data, { id }) => {
      queryClient.invalidateQueries({ queryKey: ['conversations', id, 'messages'] });
    },
  });
}
```

- [ ] **TypeScript check**

```powershell
npx tsc --noEmit --project src/Atendefy.Web/tsconfig.json
```

Esperado: 0 erros.

- [ ] **Commit**

```powershell
git add src/Atendefy.Web/src/types/api.ts `
      src/Atendefy.Web/src/hooks/useConversations.ts
git commit -m "feat: add botPaused to types, add takeover/release/sendMessage hooks"
```

---

### Task 8: Frontend — ConversationsPage com takeover + envio manual

**Files:**
- Modify: `src/Atendefy.Web/src/pages/ConversationsPage.tsx`

O painel direito ganha:
- Badge "Modo humano" / "Modo bot" no header
- Botão "Assumir" / "Liberar bot"
- Input de texto + botão "Enviar" no rodapé, visível apenas quando `botPaused === true`
- Mensagens com `role === "agent"` aparecem como bolha verde (diferente do bot azul)

- [ ] **Substituir `ConversationsPage.tsx` completo**

```typescript
import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Bot, MessageSquare, Phone, UserCheck } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import {
  useConversations,
  useConversationMessages,
  useTakeoverConversation,
  useReleaseConversation,
  useSendMessage,
} from '@/hooks/useConversations';
import { useAuthStore } from '@/stores/authStore';

function formatTime(dateStr: string): string {
  const d = new Date(dateStr);
  const now = new Date();
  const isToday = d.toDateString() === now.toDateString();
  if (isToday) return d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
  return d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' });
}

export default function ConversationsPage() {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [messageText, setMessageText] = useState('');
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const { data, isLoading, isError } = useConversations();
  const { data: detail, isLoading: loadingMessages, isError: messagesError } =
    useConversationMessages(selectedId);

  const takeover = useTakeoverConversation();
  const release = useReleaseConversation();
  const sendMessage = useSendMessage();
  const queryClient = useQueryClient();

  useEffect(() => {
    const token = useAuthStore.getState().accessToken;
    if (!token) return;

    const url = `/api/conversations/stream?token=${encodeURIComponent(token)}`;
    const es = new EventSource(url);
    let failures = 0;

    es.onmessage = (e) => {
      try {
        const { conversationId } = JSON.parse(e.data) as { conversationId: string };
        queryClient.invalidateQueries({ queryKey: ['conversations'] });
        queryClient.invalidateQueries({ queryKey: ['conversations', conversationId, 'messages'] });
        queryClient.invalidateQueries({ queryKey: ['dashboard-stats'] });
        failures = 0;
      } catch {
        console.warn('[SSE] malformed event data:', e.data);
      }
    };

    es.onerror = () => {
      failures++;
      if (failures >= 5) { es.close(); return; }
    };

    return () => es.close();
  }, [queryClient]);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [detail?.messages.length]);

  const botPaused = detail?.botPaused ?? false;

  async function handleSend() {
    if (!selectedId || !messageText.trim()) return;
    const text = messageText.trim();
    setMessageText('');
    await sendMessage.mutateAsync({ id: selectedId, text });
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      void handleSend();
    }
  }

  return (
    <div className="flex h-[calc(100vh-8rem)] gap-4">
      {/* Left panel */}
      <div className="w-80 shrink-0 flex flex-col border rounded-lg overflow-hidden bg-card">
        <div className="p-4 border-b">
          <h1 className="text-lg font-semibold">Conversas</h1>
          {data && (
            <p className="text-xs text-muted-foreground mt-0.5">{data.total} conversa(s)</p>
          )}
        </div>

        <div className="flex-1 overflow-y-auto">
          {isLoading && <p className="p-4 text-sm text-muted-foreground">Carregando...</p>}
          {isError && <p className="p-4 text-sm text-destructive">Erro ao carregar conversas.</p>}
          {!isLoading && !isError && data?.conversations.length === 0 && (
            <div className="p-6 text-center text-sm text-muted-foreground">
              <MessageSquare className="h-8 w-8 mx-auto mb-2 opacity-30" />
              <p>Nenhuma conversa ainda.</p>
              <p className="mt-1 text-xs">Envie uma mensagem via WhatsApp para começar.</p>
            </div>
          )}
          {data?.conversations.map((conv) => (
            <button
              key={conv.id}
              type="button"
              className={cn(
                'w-full text-left px-4 py-3 border-b hover:bg-accent transition-colors',
                selectedId === conv.id && 'bg-accent'
              )}
              onClick={() => setSelectedId(conv.id)}
            >
              <div className="flex items-center justify-between mb-1">
                <span className="text-sm font-medium truncate">{conv.contactPhone}</span>
                <span className="text-xs text-muted-foreground shrink-0 ml-2">
                  {formatTime(conv.lastMessageAt)}
                </span>
              </div>
              <div className="flex items-center gap-2">
                <Badge variant="outline" className="text-xs py-0 h-5">
                  {conv.messageCount} msgs
                </Badge>
                {conv.botPaused && (
                  <Badge variant="secondary" className="text-xs py-0 h-5 gap-1">
                    <UserCheck className="h-3 w-3" />
                    humano
                  </Badge>
                )}
              </div>
            </button>
          ))}
        </div>
      </div>

      {/* Right panel */}
      <div className="flex-1 flex flex-col border rounded-lg overflow-hidden bg-card">
        {!selectedId ? (
          <div className="flex-1 flex items-center justify-center text-muted-foreground">
            <div className="text-center">
              <MessageSquare className="h-12 w-12 mx-auto mb-3 opacity-20" />
              <p className="text-sm">Selecione uma conversa</p>
            </div>
          </div>
        ) : (
          <>
            <div className="px-4 py-3 border-b flex items-center gap-2 shrink-0">
              <Phone className="h-4 w-4 text-muted-foreground" />
              <span className="font-medium text-sm">{detail?.contactPhone ?? '…'}</span>
              {detail && (
                <span className="text-xs text-muted-foreground">
                  desde {new Date(detail.startedAt).toLocaleDateString('pt-BR')}
                </span>
              )}
              <div className="ml-auto flex items-center gap-2">
                {botPaused ? (
                  <>
                    <Badge variant="secondary" className="gap-1">
                      <UserCheck className="h-3 w-3" />
                      Modo humano
                    </Badge>
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => release.mutate(selectedId)}
                      disabled={release.isPending}
                    >
                      <Bot className="h-4 w-4 mr-1" />
                      Liberar bot
                    </Button>
                  </>
                ) : (
                  <>
                    <Badge variant="outline" className="gap-1 text-muted-foreground">
                      <Bot className="h-3 w-3" />
                      Modo bot
                    </Badge>
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => takeover.mutate(selectedId)}
                      disabled={takeover.isPending}
                    >
                      <UserCheck className="h-4 w-4 mr-1" />
                      Assumir
                    </Button>
                  </>
                )}
              </div>
            </div>

            <div className="flex-1 overflow-y-auto p-4 space-y-3">
              {loadingMessages && (
                <p className="text-sm text-center text-muted-foreground py-4">Carregando mensagens…</p>
              )}
              {messagesError && (
                <p className="text-sm text-center text-destructive py-4">Erro ao carregar mensagens.</p>
              )}
              {detail?.messages.map((msg) => (
                <div
                  key={msg.id}
                  className={cn('flex', msg.role === 'user' ? 'justify-start' : 'justify-end')}
                >
                  <div
                    className={cn(
                      'max-w-[75%] rounded-2xl px-4 py-2 text-sm',
                      msg.role === 'user'
                        ? 'bg-muted rounded-tl-sm'
                        : msg.role === 'agent'
                          ? 'bg-emerald-600 text-white rounded-tr-sm'
                          : 'bg-primary text-primary-foreground rounded-tr-sm'
                    )}
                  >
                    <p className="whitespace-pre-wrap break-words">{msg.content}</p>
                    <p className="text-xs mt-1 opacity-70">
                      {formatTime(msg.createdAt)}
                      {msg.role === 'agent' && ' · você'}
                    </p>
                  </div>
                </div>
              ))}
              <div ref={messagesEndRef} />
            </div>

            {botPaused && (
              <div className="border-t p-3 flex gap-2 shrink-0">
                <textarea
                  className="flex-1 resize-none rounded-md border bg-background px-3 py-2 text-sm min-h-[40px] max-h-[120px] focus:outline-none focus:ring-2 focus:ring-ring"
                  placeholder="Digite uma mensagem… (Enter para enviar, Shift+Enter para nova linha)"
                  rows={1}
                  value={messageText}
                  onChange={(e) => setMessageText(e.target.value)}
                  onKeyDown={handleKeyDown}
                />
                <Button
                  size="sm"
                  onClick={handleSend}
                  disabled={!messageText.trim() || sendMessage.isPending}
                >
                  {sendMessage.isPending ? '…' : 'Enviar'}
                </Button>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
```

- [ ] **TypeScript check**

```powershell
npx tsc --noEmit --project src/Atendefy.Web/tsconfig.json
```

Esperado: 0 erros.

- [ ] **Commit**

```powershell
git add src/Atendefy.Web/src/pages/ConversationsPage.tsx
git commit -m "feat: takeover/release button and manual message input in ConversationsPage"
```

---

### Task 9: Frontend — ContactsPage + rota + sidebar

**Files:**
- Create: `src/Atendefy.Web/src/hooks/useContacts.ts`
- Create: `src/Atendefy.Web/src/pages/ContactsPage.tsx`
- Modify: `src/Atendefy.Web/src/App.tsx`
- Modify: `src/Atendefy.Web/src/components/layout/Sidebar.tsx`

- [ ] **Criar `useContacts.ts`**

```typescript
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import type { ContactsListResponse } from '@/types/api';

export function useContacts(page = 1, pageSize = 50) {
  return useQuery({
    queryKey: ['contacts', page, pageSize],
    queryFn: () =>
      apiClient
        .get<ContactsListResponse>('/contacts', { params: { page, pageSize } })
        .then((r) => r.data),
  });
}

export function useUpdateContactName() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ phone, name }: { phone: string; name: string }) =>
      apiClient
        .patch(`/contacts/${encodeURIComponent(phone)}`, { name })
        .then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['contacts'] }),
  });
}
```

- [ ] **Criar `ContactsPage.tsx`**

```typescript
import { useState } from 'react';
import { useContacts, useUpdateContactName } from '@/hooks/useContacts';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Check, Pencil, Phone, X } from 'lucide-react';

function formatDate(dateStr?: string): string {
  if (!dateStr) return '—';
  return new Date(dateStr).toLocaleDateString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
}

export default function ContactsPage() {
  const { data, isLoading, isError } = useContacts();
  const updateName = useUpdateContactName();
  const [editingPhone, setEditingPhone] = useState<string | null>(null);
  const [editValue, setEditValue] = useState('');

  function startEdit(phone: string, currentName?: string) {
    setEditingPhone(phone);
    setEditValue(currentName ?? '');
  }

  function cancelEdit() {
    setEditingPhone(null);
    setEditValue('');
  }

  async function saveEdit(phone: string) {
    await updateName.mutateAsync({ phone, name: editValue });
    setEditingPhone(null);
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Contatos</h1>
        {data && <p className="text-sm text-muted-foreground">{data.total} contato(s)</p>}
      </div>

      {isLoading && <p className="text-muted-foreground">Carregando…</p>}
      {isError && <p className="text-destructive">Erro ao carregar contatos.</p>}

      {!isLoading && data?.contacts.length === 0 && (
        <div className="text-center py-12 text-muted-foreground">
          <Phone className="h-10 w-10 mx-auto mb-3 opacity-30" />
          <p className="text-sm">Nenhum contato ainda.</p>
          <p className="text-xs mt-1">
            Contatos aparecem automaticamente quando alguém envia uma mensagem pelo WhatsApp.
          </p>
        </div>
      )}

      {data && data.contacts.length > 0 && (
        <div className="border rounded-lg overflow-hidden">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50">
              <tr>
                <th className="text-left px-4 py-3 font-medium">Telefone</th>
                <th className="text-left px-4 py-3 font-medium">Nome</th>
                <th className="text-left px-4 py-3 font-medium">Conversas</th>
                <th className="text-left px-4 py-3 font-medium">Última atividade</th>
                <th className="w-10 px-4 py-3" />
              </tr>
            </thead>
            <tbody>
              {data.contacts.map((contact) => (
                <tr key={contact.phone} className="border-b last:border-0 hover:bg-muted/30">
                  <td className="px-4 py-3 font-mono text-xs">{contact.phone}</td>
                  <td className="px-4 py-3">
                    {editingPhone === contact.phone ? (
                      <div className="flex items-center gap-2">
                        <Input
                          className="h-7 text-sm w-40"
                          value={editValue}
                          onChange={(e) => setEditValue(e.target.value)}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter') void saveEdit(contact.phone);
                            if (e.key === 'Escape') cancelEdit();
                          }}
                          autoFocus
                        />
                        <Button
                          size="icon"
                          variant="ghost"
                          className="h-7 w-7"
                          onClick={() => void saveEdit(contact.phone)}
                          disabled={updateName.isPending}
                        >
                          <Check className="h-3 w-3" />
                        </Button>
                        <Button
                          size="icon"
                          variant="ghost"
                          className="h-7 w-7"
                          onClick={cancelEdit}
                        >
                          <X className="h-3 w-3" />
                        </Button>
                      </div>
                    ) : (
                      <span className={contact.name ? '' : 'text-muted-foreground italic'}>
                        {contact.name ?? 'sem nome'}
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">{contact.conversationCount}</td>
                  <td className="px-4 py-3 text-muted-foreground">{formatDate(contact.lastActivity)}</td>
                  <td className="px-4 py-3">
                    {editingPhone !== contact.phone && (
                      <Button
                        size="icon"
                        variant="ghost"
                        className="h-7 w-7"
                        onClick={() => startEdit(contact.phone, contact.name)}
                      >
                        <Pencil className="h-3 w-3" />
                      </Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Atualizar `App.tsx`**

Adicionar import:
```typescript
import ContactsPage from '@/pages/ContactsPage';
```

Dentro do array `children` do AppLayout, após a rota `/conversations`, adicionar:
```typescript
{ path: '/contacts', element: <ContactsPage /> },
```

- [ ] **Atualizar `Sidebar.tsx`**

Adicionar `Users` ao import do lucide-react:
```typescript
import {
  Bot,
  CreditCard,
  LayoutDashboard,
  LogOut,
  MessageSquare,
  Users,
  Wifi,
} from 'lucide-react';
```

Adicionar ao array `navItems` após o item de conversas:
```typescript
{ to: '/contacts', label: 'Contatos', icon: Users },
```

- [ ] **TypeScript check**

```powershell
npx tsc --noEmit --project src/Atendefy.Web/tsconfig.json
```

Esperado: 0 erros.

- [ ] **Commit**

```powershell
git add src/Atendefy.Web/src/hooks/useContacts.ts `
      src/Atendefy.Web/src/pages/ContactsPage.tsx `
      src/Atendefy.Web/src/App.tsx `
      src/Atendefy.Web/src/components/layout/Sidebar.tsx
git commit -m "feat: ContactsPage with inline name edit, route and sidebar nav"
```
