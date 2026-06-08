# Conversation Resolve + Quick Replies Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir que o tenant marque conversas como resolvidas (bloqueando o bot automaticamente) e configure respostas rápidas reutilizáveis para o modo humano.

**Architecture:** Feature A adiciona `IsResolved`/`ResolvedAt` à entidade `Conversation` — o `ConversationWorker` passa a verificar ambos `BotPaused` e `IsResolved` antes de chamar a IA; o GET /conversations ganha filtro `?status=open|resolved|all`. Feature B cria tabela `quick_replies` per-tenant com CRUD completo; a `ConversationsPage` ganha um painel de templates que preenche o textarea ao clicar. Como nos planos anteriores, tenant schemas usam raw SQL (TenantProvisioner + startup ALTER/CREATE em Program.cs).

**Tech Stack:** ASP.NET Core minimal API, EF Core DbSet, Npgsql raw SQL para schema migrations, React Query mutations, lucide-react icons, shadcn/ui components.

---

## Mapa de Arquivos

**Backend — modificar:**
- `src/Atendefy.API/Modules/Chatbot/Models/Conversation.cs` — add `IsResolved`, `ResolvedAt`
- `src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs` — map new columns + QuickReply DbSet
- `src/Atendefy.API/Modules/Tenants/TenantProvisioner.cs` — add columns to conversations CREATE TABLE, add quick_replies table
- `src/Atendefy.API/Program.cs` — startup ALTER/CREATE for existing tenants
- `src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs` — check IsResolved in early-return condition
- `src/Atendefy.API/Modules/Chatbot/ConversationEndpoints.cs` — resolve/reopen endpoints + status filter

**Backend — criar:**
- `src/Atendefy.API/Modules/Chatbot/Models/QuickReply.cs` — QuickReply entity
- `src/Atendefy.API/Modules/Chatbot/QuickReplyEndpoints.cs` — CRUD endpoints

**Frontend — modificar:**
- `src/Atendefy.Web/src/types/api.ts` — add isResolved/resolvedAt to types, add QuickReply types
- `src/Atendefy.Web/src/hooks/useConversations.ts` — add status param, add useResolve/useReopen
- `src/Atendefy.Web/src/pages/ConversationsPage.tsx` — status filter tabs, resolve/reopen button, quick replies popover
- `src/Atendefy.Web/src/App.tsx` — add /quick-replies route
- `src/Atendefy.Web/src/components/layout/Sidebar.tsx` — add Respostas Rápidas nav

**Frontend — criar:**
- `src/Atendefy.Web/src/hooks/useQuickReplies.ts` — quick replies query + mutations
- `src/Atendefy.Web/src/pages/QuickRepliesPage.tsx` — settings page with inline CRUD

---

### Task 1: Conversation entity — IsResolved + ResolvedAt + schema migrations

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
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public List<ConversationMessage> Messages { get; set; } = [];
}
```

- [ ] **Atualizar mapeamento em `TenantDbContext.cs`**

Dentro do bloco `modelBuilder.Entity<Conversation>(e => { ... })`, após a linha `e.Property(x => x.AccountId);`, adicionar:

```csharp
e.Property(x => x.IsResolved).HasDefaultValue(false);
e.Property(x => x.ResolvedAt);
```

- [ ] **Atualizar `TenantProvisioner.cs` — conversations CREATE TABLE**

Substituir a criação da tabela `conversations` por (adicionar as duas últimas colunas):

```sql
CREATE TABLE IF NOT EXISTS "{schemaName}".conversations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    contact_phone VARCHAR(30) NOT NULL,
    started_at TIMESTAMPTZ DEFAULT NOW(),
    message_count INT DEFAULT 0,
    is_deleted BOOLEAN DEFAULT FALSE,
    bot_paused BOOLEAN DEFAULT FALSE,
    account_id UUID,
    is_resolved BOOLEAN DEFAULT FALSE,
    resolved_at TIMESTAMPTZ
);
```

- [ ] **Atualizar startup migration em `Program.cs`**

Encontrar o bloco `var migSql = $"""` dentro do loop de tenants existentes e substituir por (estendendo o ALTER TABLE):

```csharp
var migSql = $"""
    ALTER TABLE IF EXISTS "{t.SchemaName}".conversations
        ADD COLUMN IF NOT EXISTS bot_paused BOOLEAN DEFAULT FALSE,
        ADD COLUMN IF NOT EXISTS account_id UUID,
        ADD COLUMN IF NOT EXISTS is_resolved BOOLEAN DEFAULT FALSE,
        ADD COLUMN IF NOT EXISTS resolved_at TIMESTAMPTZ;
    CREATE TABLE IF NOT EXISTS "{t.SchemaName}".contacts (
        phone VARCHAR(30) PRIMARY KEY,
        name VARCHAR(200),
        created_at TIMESTAMPTZ DEFAULT NOW()
    );
    """;
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
git commit -m "feat: IsResolved+ResolvedAt on Conversation, schema migration"
```

---

### Task 2: QuickReply entity + TenantDbContext + schema migrations

**Files:**
- Create: `src/Atendefy.API/Modules/Chatbot/Models/QuickReply.cs`
- Modify: `src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs`
- Modify: `src/Atendefy.API/Modules/Tenants/TenantProvisioner.cs`
- Modify: `src/Atendefy.API/Program.cs`

- [ ] **Criar `QuickReply.cs`**

```csharp
namespace Atendefy.API.Modules.Chatbot.Models;

public class QuickReply
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Adicionar DbSet e mapeamento em `TenantDbContext.cs`**

Após `public DbSet<Contact> Contacts => Set<Contact>();`, adicionar:

```csharp
public DbSet<QuickReply> QuickReplies => Set<QuickReply>();
```

Dentro de `OnModelCreating`, após o bloco `Contact`, adicionar:

```csharp
modelBuilder.Entity<QuickReply>(e =>
{
    e.ToTable("quick_replies");
    e.HasKey(x => x.Id);
    e.Property(x => x.Title).HasMaxLength(100).IsRequired();
    e.Property(x => x.Body).IsRequired();
});
```

- [ ] **Adicionar tabela `quick_replies` ao `TenantProvisioner.cs`**

Após o bloco CREATE TABLE para `contacts`, adicionar:

```sql
CREATE TABLE IF NOT EXISTS "{schemaName}".quick_replies (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title VARCHAR(100) NOT NULL,
    body TEXT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
```

- [ ] **Atualizar startup migration em `Program.cs`**

No mesmo bloco `var migSql`, após o CREATE TABLE de `contacts`, adicionar:

```csharp
var migSql = $"""
    ALTER TABLE IF EXISTS "{t.SchemaName}".conversations
        ADD COLUMN IF NOT EXISTS bot_paused BOOLEAN DEFAULT FALSE,
        ADD COLUMN IF NOT EXISTS account_id UUID,
        ADD COLUMN IF NOT EXISTS is_resolved BOOLEAN DEFAULT FALSE,
        ADD COLUMN IF NOT EXISTS resolved_at TIMESTAMPTZ;
    CREATE TABLE IF NOT EXISTS "{t.SchemaName}".contacts (
        phone VARCHAR(30) PRIMARY KEY,
        name VARCHAR(200),
        created_at TIMESTAMPTZ DEFAULT NOW()
    );
    CREATE TABLE IF NOT EXISTS "{t.SchemaName}".quick_replies (
        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
        title VARCHAR(100) NOT NULL,
        body TEXT NOT NULL,
        created_at TIMESTAMPTZ DEFAULT NOW()
    );
    """;
```

- [ ] **Build**

```powershell
dotnet build src/Atendefy.API/Atendefy.API.csproj
```

Esperado: 0 erros.

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/Chatbot/Models/QuickReply.cs `
      src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs `
      src/Atendefy.API/Modules/Tenants/TenantProvisioner.cs `
      src/Atendefy.API/Program.cs
git commit -m "feat: QuickReply entity and schema migration"
```

---

### Task 3: ConversationWorker — IsResolved check

**Files:**
- Modify: `src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs`

Mudança mínima: expandir a condição de early-return para incluir `IsResolved`.

- [ ] **Editar `ConversationWorker.cs`**

Localizar o bloco `await using (var checkDb = ...)` e modificar apenas a condição:

Antes:
```csharp
if (existing?.BotPaused == true)
{
```

Depois:
```csharp
if (existing?.BotPaused == true || existing?.IsResolved == true)
{
```

O restante do bloco (PersistUserOnlyAsync, UpsertContactAsync, emitter.Emit, logger, return) permanece idêntico.

- [ ] **Build**

```powershell
dotnet build src/Atendefy.API/Atendefy.API.csproj
```

Esperado: 0 erros.

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs
git commit -m "feat: ConversationWorker skips AI when conversation IsResolved"
```

---

### Task 4: ConversationEndpoints — resolve/reopen + status filter

**Files:**
- Modify: `src/Atendefy.API/Modules/Chatbot/ConversationEndpoints.cs`

Substituir o arquivo completo:

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
            if (statusFilter == "resolved")
                query = query.Where(c => c.IsResolved);
            else if (statusFilter != "all")
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
                conversation.IsResolved,
                conversation.ResolvedAt,
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
        });

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
git commit -m "feat: resolve/reopen endpoints, status filter on GET /conversations"
```

---

### Task 5: QuickReply endpoints + Program.cs registration

**Files:**
- Create: `src/Atendefy.API/Modules/Chatbot/QuickReplyEndpoints.cs`
- Modify: `src/Atendefy.API/Program.cs`

- [ ] **Criar `QuickReplyEndpoints.cs`**

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Chatbot.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Chatbot;

public static class QuickReplyEndpoints
{
    public static IEndpointRouteBuilder MapQuickReplyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/quick-replies")
            .WithTags("QuickReplies")
            .RequireAuthorization();

        group.MapGet("/", async (
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            await using var db = dbFactory.Create(schemaName);

            var quickReplies = await db.QuickReplies
                .OrderBy(q => q.CreatedAt)
                .Select(q => new { q.Id, q.Title, q.Body, q.CreatedAt })
                .ToListAsync();

            return Results.Ok(new { quickReplies });
        });

        group.MapPost("/", async (
            [FromBody] CreateQuickReplyRequest request,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { error = "Título não pode ser vazio." });
            if (string.IsNullOrWhiteSpace(request.Body))
                return Results.BadRequest(new { error = "Corpo não pode ser vazio." });

            await using var db = dbFactory.Create(schemaName);

            var quickReply = new QuickReply
            {
                Title = request.Title.Trim(),
                Body = request.Body.Trim()
            };
            db.QuickReplies.Add(quickReply);
            await db.SaveChangesAsync();

            return Results.Created($"/quick-replies/{quickReply.Id}",
                new { quickReply.Id, quickReply.Title, quickReply.Body, quickReply.CreatedAt });
        });

        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateQuickReplyRequest request,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            await using var db = dbFactory.Create(schemaName);
            var quickReply = await db.QuickReplies.FindAsync(id);
            if (quickReply is null) return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(request.Title))
                quickReply.Title = request.Title.Trim();
            if (!string.IsNullOrWhiteSpace(request.Body))
                quickReply.Body = request.Body.Trim();

            await db.SaveChangesAsync();
            return Results.Ok(new { quickReply.Id, quickReply.Title, quickReply.Body });
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            await using var db = dbFactory.Create(schemaName);
            var quickReply = await db.QuickReplies.FindAsync(id);
            if (quickReply is null) return Results.NotFound();

            db.QuickReplies.Remove(quickReply);
            await db.SaveChangesAsync();
            return Results.NoContent();
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

public record CreateQuickReplyRequest(string Title, string Body);
public record UpdateQuickReplyRequest(string? Title, string? Body);
```

- [ ] **Registrar em `Program.cs`**

Após `app.MapContactEndpoints();`, adicionar:

```csharp
app.MapQuickReplyEndpoints();
```

- [ ] **Build**

```powershell
dotnet build src/Atendefy.API/Atendefy.API.csproj
```

Esperado: 0 erros.

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/Chatbot/QuickReplyEndpoints.cs `
      src/Atendefy.API/Program.cs
git commit -m "feat: quick-replies CRUD endpoints"
```

---

### Task 6: Frontend — tipos + hooks

**Files:**
- Modify: `src/Atendefy.Web/src/types/api.ts`
- Modify: `src/Atendefy.Web/src/hooks/useConversations.ts`
- Create: `src/Atendefy.Web/src/hooks/useQuickReplies.ts`

- [ ] **Atualizar `types/api.ts`**

1. Adicionar `isResolved: boolean` a `ConversationSummary`:

```typescript
export interface ConversationSummary {
  id: string;
  contactPhone: string;
  messageCount: number;
  startedAt: string;
  lastMessageAt: string;
  botPaused: boolean;
  isResolved: boolean;
}
```

2. Adicionar `isResolved` e `resolvedAt` a `ConversationDetail`:

```typescript
export interface ConversationDetail {
  id: string;
  contactPhone: string;
  startedAt: string;
  messageCount: number;
  botPaused: boolean;
  isResolved: boolean;
  resolvedAt?: string;
  messages: ConversationMessage[];
}
```

3. Adicionar ao final do arquivo:

```typescript
export interface QuickReply {
  id: string;
  title: string;
  body: string;
  createdAt: string;
}

export interface QuickRepliesListResponse {
  quickReplies: QuickReply[];
}
```

- [ ] **Substituir `useConversations.ts` completo**

```typescript
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import type { ConversationsListResponse, ConversationDetail } from '@/types/api';

export function useConversations(
  page = 1,
  pageSize = 20,
  status: 'open' | 'resolved' | 'all' = 'open'
) {
  return useQuery({
    queryKey: ['conversations', page, pageSize, status],
    queryFn: () =>
      apiClient
        .get<ConversationsListResponse>('/conversations', { params: { page, pageSize, status } })
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

export function useResolveConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.patch(`/conversations/${id}/resolve`).then((r) => r.data),
    onSuccess: (_data, id) => {
      queryClient.invalidateQueries({ queryKey: ['conversations'] });
      queryClient.invalidateQueries({ queryKey: ['conversations', id, 'messages'] });
    },
  });
}

export function useReopenConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.patch(`/conversations/${id}/reopen`).then((r) => r.data),
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

- [ ] **Criar `useQuickReplies.ts`**

```typescript
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import type { QuickRepliesListResponse } from '@/types/api';

export function useQuickReplies() {
  return useQuery({
    queryKey: ['quick-replies'],
    queryFn: () =>
      apiClient.get<QuickRepliesListResponse>('/quick-replies').then((r) => r.data),
  });
}

export function useCreateQuickReply() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ title, body }: { title: string; body: string }) =>
      apiClient.post('/quick-replies', { title, body }).then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['quick-replies'] }),
  });
}

export function useUpdateQuickReply() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, title, body }: { id: string; title?: string; body?: string }) =>
      apiClient.patch(`/quick-replies/${id}`, { title, body }).then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['quick-replies'] }),
  });
}

export function useDeleteQuickReply() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/quick-replies/${id}`).then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['quick-replies'] }),
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
      src/Atendefy.Web/src/hooks/useConversations.ts `
      src/Atendefy.Web/src/hooks/useQuickReplies.ts
git commit -m "feat: isResolved types, resolve/reopen hooks, useQuickReplies"
```

---

### Task 7: ConversationsPage — status filter + resolve + quick replies

**Files:**
- Modify: `src/Atendefy.Web/src/pages/ConversationsPage.tsx`

Substituir o arquivo completo:

- [ ] **Substituir `ConversationsPage.tsx` completo**

```typescript
import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Bot, CheckCircle, MessageSquare, Phone, UserCheck, Zap } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import {
  useConversations,
  useConversationMessages,
  useTakeoverConversation,
  useReleaseConversation,
  useResolveConversation,
  useReopenConversation,
  useSendMessage,
} from '@/hooks/useConversations';
import { useQuickReplies } from '@/hooks/useQuickReplies';
import { useAuthStore } from '@/stores/authStore';

type StatusFilter = 'open' | 'resolved' | 'all';

function formatTime(dateStr: string): string {
  const d = new Date(dateStr);
  const now = new Date();
  const isToday = d.toDateString() === now.toDateString();
  if (isToday) return d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
  return d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' });
}

export default function ConversationsPage() {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('open');
  const [messageText, setMessageText] = useState('');
  const [showQuickReplies, setShowQuickReplies] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const { data, isLoading, isError } = useConversations(1, 20, statusFilter);
  const { data: detail, isLoading: loadingMessages, isError: messagesError } =
    useConversationMessages(selectedId);
  const { data: quickRepliesData } = useQuickReplies();

  const takeover = useTakeoverConversation();
  const release = useReleaseConversation();
  const resolve = useResolveConversation();
  const reopen = useReopenConversation();
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
  const isResolved = detail?.isResolved ?? false;

  function handleFilterChange(f: StatusFilter) {
    setStatusFilter(f);
    setSelectedId(null);
  }

  async function handleSend() {
    if (!selectedId || !messageText.trim()) return;
    const text = messageText.trim();
    setMessageText('');
    setShowQuickReplies(false);
    await sendMessage.mutateAsync({ id: selectedId, text });
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      void handleSend();
    }
  }

  const filterLabels: Record<StatusFilter, string> = {
    open: 'Abertas',
    resolved: 'Resolvidas',
    all: 'Todas',
  };

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

        {/* Status filter tabs */}
        <div className="flex gap-1 p-2 border-b">
          {(['open', 'resolved', 'all'] as const).map((f) => (
            <button
              key={f}
              type="button"
              onClick={() => handleFilterChange(f)}
              className={cn(
                'flex-1 text-xs py-1.5 rounded transition-colors',
                statusFilter === f
                  ? 'bg-primary text-primary-foreground'
                  : 'hover:bg-accent text-muted-foreground'
              )}
            >
              {filterLabels[f]}
            </button>
          ))}
        </div>

        <div className="flex-1 overflow-y-auto">
          {isLoading && <p className="p-4 text-sm text-muted-foreground">Carregando...</p>}
          {isError && <p className="p-4 text-sm text-destructive">Erro ao carregar conversas.</p>}
          {!isLoading && !isError && data?.conversations.length === 0 && (
            <div className="p-6 text-center text-sm text-muted-foreground">
              <MessageSquare className="h-8 w-8 mx-auto mb-2 opacity-30" />
              <p>Nenhuma conversa {statusFilter === 'open' ? 'aberta' : statusFilter === 'resolved' ? 'resolvida' : ''} ainda.</p>
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
              <div className="flex items-center gap-1.5 flex-wrap">
                <Badge variant="outline" className="text-xs py-0 h-5">
                  {conv.messageCount} msgs
                </Badge>
                {conv.botPaused && !conv.isResolved && (
                  <Badge variant="secondary" className="text-xs py-0 h-5 gap-1">
                    <UserCheck className="h-3 w-3" />
                    humano
                  </Badge>
                )}
                {conv.isResolved && (
                  <Badge variant="outline" className="text-xs py-0 h-5 gap-1 text-green-600 border-green-200">
                    <CheckCircle className="h-3 w-3" />
                    encerrada
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
            {/* Header */}
            <div className="px-4 py-3 border-b flex items-center gap-2 shrink-0">
              <Phone className="h-4 w-4 text-muted-foreground" />
              <span className="font-medium text-sm">{detail?.contactPhone ?? '…'}</span>
              {detail && (
                <span className="text-xs text-muted-foreground">
                  desde {new Date(detail.startedAt).toLocaleDateString('pt-BR')}
                </span>
              )}
              <div className="ml-auto flex items-center gap-2">
                {isResolved ? (
                  <>
                    <Badge variant="outline" className="gap-1 text-green-600 border-green-200">
                      <CheckCircle className="h-3 w-3" />
                      Resolvida
                    </Badge>
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => reopen.mutate(selectedId)}
                      disabled={reopen.isPending}
                    >
                      Reabrir
                    </Button>
                  </>
                ) : (
                  <>
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
                    <Button
                      size="sm"
                      variant="ghost"
                      className="text-muted-foreground"
                      onClick={() => resolve.mutate(selectedId)}
                      disabled={resolve.isPending}
                    >
                      <CheckCircle className="h-4 w-4 mr-1" />
                      Resolver
                    </Button>
                  </>
                )}
              </div>
            </div>

            {/* Messages */}
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

            {/* Send footer — only when human has control and conversation is open */}
            {botPaused && !isResolved && (
              <div className="border-t p-3 flex flex-col gap-2 shrink-0">
                {/* Quick replies panel */}
                {showQuickReplies && (
                  <div className="max-h-48 overflow-y-auto border rounded-md bg-popover shadow-sm">
                    {!quickRepliesData?.quickReplies.length ? (
                      <p className="p-3 text-xs text-muted-foreground text-center">
                        Nenhuma resposta rápida configurada. Acesse{' '}
                        <a href="/quick-replies" className="underline">Respostas Rápidas</a>.
                      </p>
                    ) : (
                      quickRepliesData.quickReplies.map((qr) => (
                        <button
                          key={qr.id}
                          type="button"
                          className="w-full text-left px-3 py-2 text-sm hover:bg-accent transition-colors border-b last:border-0"
                          onClick={() => {
                            setMessageText(qr.body);
                            setShowQuickReplies(false);
                          }}
                        >
                          <p className="font-medium text-xs text-muted-foreground mb-0.5">{qr.title}</p>
                          <p className="truncate text-xs">{qr.body}</p>
                        </button>
                      ))
                    )}
                  </div>
                )}
                {/* Input row */}
                <div className="flex gap-2">
                  <textarea
                    className="flex-1 resize-none rounded-md border bg-background px-3 py-2 text-sm min-h-[40px] max-h-[120px] focus:outline-none focus:ring-2 focus:ring-ring"
                    placeholder="Digite uma mensagem… (Enter para enviar)"
                    rows={1}
                    value={messageText}
                    onChange={(e) => setMessageText(e.target.value)}
                    onKeyDown={handleKeyDown}
                  />
                  <div className="flex flex-col gap-1">
                    <Button
                      size="sm"
                      variant="outline"
                      type="button"
                      onClick={() => setShowQuickReplies((v) => !v)}
                      className={cn(showQuickReplies && 'bg-accent')}
                    >
                      <Zap className="h-4 w-4" />
                    </Button>
                    <Button
                      size="sm"
                      onClick={() => void handleSend()}
                      disabled={!messageText.trim() || sendMessage.isPending}
                    >
                      {sendMessage.isPending ? '…' : 'Enviar'}
                    </Button>
                  </div>
                </div>
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
git commit -m "feat: status filter, resolve/reopen button, quick replies popover in ConversationsPage"
```

---

### Task 8: QuickRepliesPage + App.tsx + Sidebar.tsx

**Files:**
- Create: `src/Atendefy.Web/src/pages/QuickRepliesPage.tsx`
- Modify: `src/Atendefy.Web/src/App.tsx`
- Modify: `src/Atendefy.Web/src/components/layout/Sidebar.tsx`

- [ ] **Criar `QuickRepliesPage.tsx`**

```typescript
import { useState } from 'react';
import {
  useQuickReplies,
  useCreateQuickReply,
  useUpdateQuickReply,
  useDeleteQuickReply,
} from '@/hooks/useQuickReplies';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Check, Pencil, Plus, Trash2, X, Zap } from 'lucide-react';

export default function QuickRepliesPage() {
  const { data, isLoading, isError } = useQuickReplies();
  const createReply = useCreateQuickReply();
  const updateReply = useUpdateQuickReply();
  const deleteReply = useDeleteQuickReply();

  const [creating, setCreating] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [newBody, setNewBody] = useState('');

  const [editingId, setEditingId] = useState<string | null>(null);
  const [editTitle, setEditTitle] = useState('');
  const [editBody, setEditBody] = useState('');

  async function handleCreate() {
    if (!newTitle.trim() || !newBody.trim()) return;
    await createReply.mutateAsync({ title: newTitle.trim(), body: newBody.trim() });
    setNewTitle('');
    setNewBody('');
    setCreating(false);
  }

  function startEdit(id: string, title: string, body: string) {
    setEditingId(id);
    setEditTitle(title);
    setEditBody(body);
  }

  async function saveEdit(id: string) {
    if (!editTitle.trim() || !editBody.trim()) return;
    await updateReply.mutateAsync({ id, title: editTitle.trim(), body: editBody.trim() });
    setEditingId(null);
  }

  function cancelEdit() {
    setEditingId(null);
    setEditTitle('');
    setEditBody('');
  }

  return (
    <div className="space-y-6 max-w-3xl">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">Respostas Rápidas</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Templates de texto para agilizar o atendimento manual.
          </p>
        </div>
        {!creating && (
          <Button onClick={() => setCreating(true)} size="sm">
            <Plus className="h-4 w-4 mr-1" />
            Nova Resposta
          </Button>
        )}
      </div>

      {isLoading && <p className="text-muted-foreground text-sm">Carregando…</p>}
      {isError && <p className="text-destructive text-sm">Erro ao carregar respostas.</p>}

      {/* Create form */}
      {creating && (
        <div className="border rounded-lg p-4 space-y-3 bg-muted/30">
          <p className="text-sm font-medium">Nova resposta rápida</p>
          <Input
            placeholder="Título (ex: Saudação inicial)"
            value={newTitle}
            onChange={(e) => setNewTitle(e.target.value)}
            maxLength={100}
          />
          <textarea
            className="w-full resize-none rounded-md border bg-background px-3 py-2 text-sm min-h-[80px] focus:outline-none focus:ring-2 focus:ring-ring"
            placeholder="Texto completo da resposta…"
            value={newBody}
            onChange={(e) => setNewBody(e.target.value)}
            rows={3}
          />
          <div className="flex gap-2">
            <Button
              size="sm"
              onClick={() => void handleCreate()}
              disabled={!newTitle.trim() || !newBody.trim() || createReply.isPending}
            >
              <Check className="h-4 w-4 mr-1" />
              {createReply.isPending ? 'Salvando…' : 'Salvar'}
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() => { setCreating(false); setNewTitle(''); setNewBody(''); }}
            >
              <X className="h-4 w-4 mr-1" />
              Cancelar
            </Button>
          </div>
        </div>
      )}

      {/* Replies list */}
      {!isLoading && data?.quickReplies.length === 0 && !creating && (
        <div className="text-center py-12 text-muted-foreground">
          <Zap className="h-10 w-10 mx-auto mb-3 opacity-30" />
          <p className="text-sm">Nenhuma resposta rápida ainda.</p>
          <p className="text-xs mt-1">
            Crie templates de texto para usar ao atender clientes manualmente.
          </p>
        </div>
      )}

      {data && data.quickReplies.length > 0 && (
        <div className="border rounded-lg divide-y overflow-hidden">
          {data.quickReplies.map((qr) =>
            editingId === qr.id ? (
              <div key={qr.id} className="p-4 space-y-2 bg-muted/30">
                <Input
                  value={editTitle}
                  onChange={(e) => setEditTitle(e.target.value)}
                  placeholder="Título"
                  maxLength={100}
                />
                <textarea
                  className="w-full resize-none rounded-md border bg-background px-3 py-2 text-sm min-h-[80px] focus:outline-none focus:ring-2 focus:ring-ring"
                  value={editBody}
                  onChange={(e) => setEditBody(e.target.value)}
                  rows={3}
                  onKeyDown={(e) => {
                    if (e.key === 'Escape') cancelEdit();
                  }}
                />
                <div className="flex gap-2">
                  <Button
                    size="sm"
                    onClick={() => void saveEdit(qr.id)}
                    disabled={!editTitle.trim() || !editBody.trim() || updateReply.isPending}
                  >
                    <Check className="h-4 w-4 mr-1" />
                    Salvar
                  </Button>
                  <Button size="sm" variant="outline" onClick={cancelEdit}>
                    <X className="h-4 w-4 mr-1" />
                    Cancelar
                  </Button>
                </div>
              </div>
            ) : (
              <div key={qr.id} className="flex items-start gap-3 p-4 hover:bg-muted/30">
                <Zap className="h-4 w-4 text-muted-foreground mt-0.5 shrink-0" />
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium">{qr.title}</p>
                  <p className="text-xs text-muted-foreground mt-0.5 whitespace-pre-wrap">{qr.body}</p>
                </div>
                <div className="flex gap-1 shrink-0">
                  <Button
                    size="icon"
                    variant="ghost"
                    className="h-8 w-8"
                    onClick={() => startEdit(qr.id, qr.title, qr.body)}
                  >
                    <Pencil className="h-3.5 w-3.5" />
                  </Button>
                  <Button
                    size="icon"
                    variant="ghost"
                    className="h-8 w-8 text-destructive hover:text-destructive"
                    onClick={() => void deleteReply.mutateAsync(qr.id)}
                    disabled={deleteReply.isPending}
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </Button>
                </div>
              </div>
            )
          )}
        </div>
      )}
    </div>
  );
}
```

- [ ] **Atualizar `App.tsx`**

Adicionar import:
```typescript
import QuickRepliesPage from '@/pages/QuickRepliesPage';
```

Dentro dos `children` do AppLayout, após `/contacts`, adicionar:
```typescript
{ path: '/quick-replies', element: <QuickRepliesPage /> },
```

- [ ] **Atualizar `Sidebar.tsx`**

Adicionar `Zap` ao import do lucide-react:
```typescript
import {
  Bot,
  CreditCard,
  LayoutDashboard,
  LogOut,
  MessageSquare,
  Users,
  Wifi,
  Zap,
} from 'lucide-react';
```

Adicionar ao array `navItems`, após Contatos:
```typescript
{ to: '/quick-replies', label: 'Respostas Rápidas', icon: Zap },
```

- [ ] **TypeScript check**

```powershell
npx tsc --noEmit --project src/Atendefy.Web/tsconfig.json
```

Esperado: 0 erros.

- [ ] **Commit**

```powershell
git add src/Atendefy.Web/src/pages/QuickRepliesPage.tsx `
      src/Atendefy.Web/src/App.tsx `
      src/Atendefy.Web/src/components/layout/Sidebar.tsx
git commit -m "feat: QuickRepliesPage with inline CRUD, route and sidebar nav"
```
