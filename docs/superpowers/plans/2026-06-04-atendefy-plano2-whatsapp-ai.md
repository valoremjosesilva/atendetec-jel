# Atendefy — Plano 2: WhatsApp Gateway + AI Provider + Conversation Engine

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar o núcleo funcional do produto: recebimento de mensagens WhatsApp via webhook, processamento pela IA configurada por tenant, e envio da resposta de volta — com persistência completa no schema do tenant e rate limiting por Redis.

**Architecture:** Vertical Slice dentro do monolito existente. Webhooks publicam no Redis Stream `messages.inbound`. Um `IHostedService` (`ConversationWorker`) consome a fila, gerencia sessão de conversa no Redis (TTL 30 min), chama o AI provider e envia a resposta via WhatsApp Gateway. Roteamento de webhooks para o tenant correto via tabela `public.webhook_routes`. Toda persistência de conversa é no schema PostgreSQL do tenant (tabelas já existem via `TenantProvisioner`).

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core 8 + Npgsql, StackExchange.Redis, HttpClient (raw — sem SDKs de vendor), xUnit + FluentAssertions + NSubstitute

---

## Planos Subsequentes

| Plano | Arquivo | Conteúdo |
|---|---|---|
| Plano 3 | `2026-06-04-atendefy-plano3-billing.md` | Billing Module, Planos, Asaas, Stripe |
| Plano 4 | `2026-06-04-atendefy-plano4-frontend.md` | React + Vite SPA, todas as telas |

---

## Mapa de Arquivos

```
src/Atendefy.API/
├── Modules/
│   ├── WhatsApp/
│   │   ├── IWhatsAppProvider.cs              ← interface: SendMessageAsync
│   │   ├── MetaCloudProvider.cs              ← Meta Graph API via HttpClient
│   │   ├── EvolutionProvider.cs              ← Evolution REST API via HttpClient
│   │   ├── WhatsAppProviderFactory.cs        ← resolve provider por config do tenant
│   │   ├── WhatsAppAccountService.cs         ← CRUD de contas + registro em webhook_routes
│   │   ├── WhatsAppEndpoints.cs              ← POST/GET /whatsapp/accounts
│   │   └── Models/
│   │       ├── WhatsAppAccount.cs            ← EF entity para tenant schema
│   │       ├── OutboundMessage.cs            ← DTO de envio (phone + text)
│   │       ├── CreateAccountRequest.cs       ← request body
│   │       └── WhatsAppConfigJson.cs         ← helpers de (de)serialização de config_json
│   ├── Webhooks/
│   │   ├── WebhookEndpoints.cs               ← POST /webhooks/evolution, GET+POST /webhooks/meta
│   │   ├── MetaWebhookValidator.cs           ← HMAC-SHA256 de assinatura Meta
│   │   ├── EvolutionWebhookValidator.cs      ← lookup de token em webhook_routes
│   │   └── Models/
│   │       ├── MetaWebhookPayload.cs         ← record para desserialização
│   │       └── EvolutionWebhookPayload.cs    ← record para desserialização
│   ├── AI/
│   │   ├── IAIProvider.cs                    ← interface: CompleteAsync
│   │   ├── OpenAIProvider.cs                 ← OpenAI Chat Completions API
│   │   ├── AnthropicProvider.cs              ← Anthropic Messages API
│   │   ├── AIProviderFactory.cs              ← resolve provider por config do tenant
│   │   ├── AiConfigService.cs                ← CRUD de config de IA + criptografia de key
│   │   ├── AIEndpoints.cs                    ← PUT/GET /ai/config
│   │   └── Models/
│   │       ├── AiConfig.cs                   ← EF entity para tenant schema
│   │       ├── ChatMessage.cs                ← role + content (sessão e prompts)
│   │       ├── AICompletionRequest.cs        ← systemPrompt + messages + model
│   │       ├── AICompletionResult.cs         ← content + tokensUsed
│   │       └── AiConfigRequest.cs            ← request body para PUT /ai/config
│   └── Chatbot/
│       ├── ConversationService.cs            ← sessão Redis + persistência PostgreSQL
│       ├── ConversationWorker.cs             ← IHostedService: consome Redis Stream
│       └── Models/
│           ├── Conversation.cs               ← EF entity para tenant schema
│           ├── ConversationMessage.cs        ← EF entity para tenant schema (tabela messages)
│           ├── UsageCounter.cs               ← EF entity para tenant schema
│           └── InboundMessage.cs             ← evento publicado no Redis Stream
├── Infrastructure/
│   ├── Database/
│   │   ├── TenantDbContext.cs                ← (modify) adicionar DbSets das entities acima
│   │   ├── TenantDbContextFactory.cs         ← cria TenantDbContext para um schema
│   │   ├── PublicDbContext.cs                ← (modify) adicionar WebhookRoute
│   │   └── Migrations/                       ← nova migration: AddWebhookRoutes
│   └── RateLimiting/
│       └── TenantRateLimiter.cs              ← Redis sliding window por tenant
├── Program.cs                                ← (modify) registrar novos serviços
tests/Atendefy.Tests/
├── Helpers/
│   └── MockHttpMessageHandler.cs             ← helper para testar HttpClient
├── WhatsApp/
│   ├── MetaWebhookValidatorTests.cs
│   ├── MetaCloudProviderTests.cs
│   └── EvolutionProviderTests.cs
├── AI/
│   ├── OpenAIProviderTests.cs
│   └── AnthropicProviderTests.cs
├── Chatbot/
│   └── ConversationServiceTests.cs
└── Infrastructure/
    └── TenantRateLimiterTests.cs
```

> **Nota:** As tabelas do schema do tenant (`whatsapp_accounts`, `ai_configs`, `conversations`, `messages`, `usage_counters`) já existem — criadas pelo `TenantProvisioner`. Este plano apenas adiciona as EF entities para mapeá-las. Nenhuma migration de tenant schema é necessária.

---

## Task 1: WebhookRoute no Schema Público + Migration

**Por quê:** Webhooks chegam sem contexto de tenant. Precisamos de uma tabela pública de roteamento para encontrar o tenant correto em O(1) a partir de um `lookup_key` (phone_number_id do Meta ou token do Evolution).

**Files:**
- Create: `src/Atendefy.API/Modules/Webhooks/Models/WebhookRoute.cs`
- Modify: `src/Atendefy.API/Infrastructure/Database/PublicDbContext.cs`
- Create: `src/Atendefy.API/Infrastructure/Database/Migrations/` (nova migration gerada via CLI)

- [ ] **Step 1: Criar entidade WebhookRoute**

Criar `src/Atendefy.API/Modules/Webhooks/Models/WebhookRoute.cs`:

```csharp
using Atendefy.API.SharedKernel;

namespace Atendefy.API.Modules.Webhooks.Models;

public class WebhookRoute : BaseEntity
{
    public Guid TenantId { get; set; }
    public string Provider { get; set; } = string.Empty;   // "meta" | "evolution"
    public string LookupKey { get; set; } = string.Empty;  // phone_number_id ou token
    public Guid AccountId { get; set; }                    // whatsapp_accounts.id no schema do tenant
}
```

- [ ] **Step 2: Adicionar WebhookRoute ao PublicDbContext**

Modificar `src/Atendefy.API/Infrastructure/Database/PublicDbContext.cs` — adicionar o using, o DbSet e a configuração no `OnModelCreating`:

```csharp
using Atendefy.API.Modules.Tenants.Models;
using Atendefy.API.Modules.Webhooks.Models;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Infrastructure.Database;

public class PublicDbContext(DbContextOptions<PublicDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<WebhookRoute> WebhookRoutes => Set<WebhookRoute>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");

        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Subdomain).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.Subdomain).IsUnique();
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(50);
            e.Ignore(x => x.SchemaName);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<TenantUser>(e =>
        {
            e.ToTable("tenant_users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).IsRequired().HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            e.Property(x => x.Role).HasMaxLength(50);
            e.HasQueryFilter(x => !x.IsDeleted);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WebhookRoute>(e =>
        {
            e.ToTable("webhook_routes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            e.Property(x => x.LookupKey).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.LookupKey).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

- [ ] **Step 3: Gerar migration**

```bash
cd src/Atendefy.API
dotnet ef migrations add AddWebhookRoutes --output-dir Infrastructure/Database/Migrations
cd ../..
```

Expected: `Build succeeded. Done.`

- [ ] **Step 4: Verificar build**

```bash
dotnet build Atendefy.sln
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/Atendefy.API/Modules/Webhooks/Models/WebhookRoute.cs
git add src/Atendefy.API/Infrastructure/Database/PublicDbContext.cs
git add src/Atendefy.API/Infrastructure/Database/Migrations/
git commit -m "feat: add WebhookRoute entity and migration for webhook tenant routing"
```

---

## Task 2: Tenant Schema EF Entities + TenantDbContextFactory

**Files:**
- Create: `src/Atendefy.API/Modules/WhatsApp/Models/WhatsAppAccount.cs`
- Create: `src/Atendefy.API/Modules/AI/Models/AiConfig.cs`
- Create: `src/Atendefy.API/Modules/Chatbot/Models/Conversation.cs`
- Create: `src/Atendefy.API/Modules/Chatbot/Models/ConversationMessage.cs`
- Create: `src/Atendefy.API/Modules/Chatbot/Models/UsageCounter.cs`
- Modify: `src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs`
- Create: `src/Atendefy.API/Infrastructure/Database/TenantDbContextFactory.cs`

- [ ] **Step 1: Criar WhatsAppAccount**

Criar `src/Atendefy.API/Modules/WhatsApp/Models/WhatsAppAccount.cs`:

```csharp
namespace Atendefy.API.Modules.WhatsApp.Models;

public class WhatsAppAccount
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;  // "meta" | "evolution"
    public string? Phone { get; set; }
    public string? ConfigJson { get; set; }               // JSON com credenciais do provider
    public string Status { get; set; } = "disconnected";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
```

- [ ] **Step 2: Criar AiConfig**

Criar `src/Atendefy.API/Modules/AI/Models/AiConfig.cs`:

```csharp
namespace Atendefy.API.Modules.AI.Models;

public class AiConfig
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;  // "openai" | "anthropic"
    public string? ApiKeyEncrypted { get; set; }           // AES-256 encrypted
    public string? Model { get; set; }
    public string? SystemPrompt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
```

- [ ] **Step 3: Criar Conversation e ConversationMessage**

Criar `src/Atendefy.API/Modules/Chatbot/Models/Conversation.cs`:

```csharp
namespace Atendefy.API.Modules.Chatbot.Models;

public class Conversation
{
    public Guid Id { get; set; }
    public string ContactPhone { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public int MessageCount { get; set; }
    public bool IsDeleted { get; set; }
    public List<ConversationMessage> Messages { get; set; } = [];
}
```

Criar `src/Atendefy.API/Modules/Chatbot/Models/ConversationMessage.cs`:

```csharp
namespace Atendefy.API.Modules.Chatbot.Models;

public class ConversationMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string Role { get; set; } = string.Empty;  // "user" | "assistant"
    public string Content { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 4: Criar UsageCounter**

Criar `src/Atendefy.API/Modules/Chatbot/Models/UsageCounter.cs`:

```csharp
namespace Atendefy.API.Modules.Chatbot.Models;

public class UsageCounter
{
    public string Month { get; set; } = string.Empty;  // formato "2026-06"
    public int MessagesSent { get; set; }
    public long TokensConsumed { get; set; }
    public decimal CostUsd { get; set; }
}
```

- [ ] **Step 5: Atualizar TenantDbContext com os DbSets e configurações**

Substituir o conteúdo de `src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs`:

```csharp
using Atendefy.API.Modules.AI.Models;
using Atendefy.API.Modules.Chatbot.Models;
using Atendefy.API.Modules.WhatsApp.Models;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Infrastructure.Database;

public class TenantDbContext(DbContextOptions<TenantDbContext> options, string schema) : DbContext(options)
{
    public DbSet<WhatsAppAccount> WhatsAppAccounts => Set<WhatsAppAccount>();
    public DbSet<AiConfig> AiConfigs => Set<AiConfig>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> Messages => Set<ConversationMessage>();
    public DbSet<UsageCounter> UsageCounters => Set<UsageCounter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(schema);

        modelBuilder.Entity<WhatsAppAccount>(e =>
        {
            e.ToTable("whatsapp_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(50);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<AiConfig>(e =>
        {
            e.ToTable("ai_configs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            e.Property(x => x.Model).HasMaxLength(100);
        });

        modelBuilder.Entity<Conversation>(e =>
        {
            e.ToTable("conversations");
            e.HasKey(x => x.Id);
            e.Property(x => x.ContactPhone).HasMaxLength(30).IsRequired();
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ConversationId);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ConversationMessage>(e =>
        {
            e.ToTable("messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).HasMaxLength(20).IsRequired();
            e.Property(x => x.Content).IsRequired();
        });

        modelBuilder.Entity<UsageCounter>(e =>
        {
            e.ToTable("usage_counters");
            e.HasKey(x => x.Month);
            e.Property(x => x.Month).HasMaxLength(7);
            e.Property(x => x.CostUsd).HasColumnType("decimal(10,4)");
        });
    }
}
```

- [ ] **Step 6: Criar TenantDbContextFactory**

Criar `src/Atendefy.API/Infrastructure/Database/TenantDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Infrastructure.Database;

public class TenantDbContextFactory(string connectionString)
{
    public TenantDbContext Create(string schemaName)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new TenantDbContext(options, schemaName);
    }
}
```

- [ ] **Step 7: Verificar build**

```bash
dotnet build Atendefy.sln
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 8: Commit**

```bash
git add src/Atendefy.API/Modules/WhatsApp/Models/
git add src/Atendefy.API/Modules/AI/Models/AiConfig.cs
git add src/Atendefy.API/Modules/Chatbot/Models/
git add src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs
git add src/Atendefy.API/Infrastructure/Database/TenantDbContextFactory.cs
git commit -m "feat: add tenant schema EF entities and TenantDbContextFactory"
```

---

## Task 3: WhatsApp Gateway Abstraction

**Files:**
- Create: `src/Atendefy.API/Modules/WhatsApp/IWhatsAppProvider.cs`
- Create: `src/Atendefy.API/Modules/WhatsApp/Models/OutboundMessage.cs`
- Create: `src/Atendefy.API/Modules/WhatsApp/Models/WhatsAppConfigJson.cs`
- Create: `src/Atendefy.API/Modules/WhatsApp/MetaCloudProvider.cs`
- Create: `src/Atendefy.API/Modules/WhatsApp/EvolutionProvider.cs`
- Create: `src/Atendefy.API/Modules/WhatsApp/WhatsAppProviderFactory.cs`
- Create: `tests/Atendefy.Tests/Helpers/MockHttpMessageHandler.cs`
- Create: `tests/Atendefy.Tests/WhatsApp/MetaCloudProviderTests.cs`
- Create: `tests/Atendefy.Tests/WhatsApp/EvolutionProviderTests.cs`

- [ ] **Step 1: Criar helper MockHttpMessageHandler**

Criar `tests/Atendefy.Tests/Helpers/MockHttpMessageHandler.cs`:

```csharp
namespace Atendefy.Tests.Helpers;

public class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(handler(request));
    }

    public static MockHttpMessageHandler ReturnsJson(string json, int statusCode = 200)
        => new(_ => new HttpResponseMessage((System.Net.HttpStatusCode)statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
}
```

- [ ] **Step 2: Criar interface e modelos**

Criar `src/Atendefy.API/Modules/WhatsApp/Models/OutboundMessage.cs`:

```csharp
namespace Atendefy.API.Modules.WhatsApp.Models;

public record OutboundMessage(string ToPhone, string Text);
```

Criar `src/Atendefy.API/Modules/WhatsApp/Models/WhatsAppConfigJson.cs`:

```csharp
using System.Text.Json;

namespace Atendefy.API.Modules.WhatsApp.Models;

public record MetaConfig(string PhoneNumberId, string AccessToken)
{
    public static MetaConfig FromJson(string json)
    {
        var doc = JsonDocument.Parse(json);
        return new MetaConfig(
            doc.RootElement.GetProperty("phone_number_id").GetString()!,
            doc.RootElement.GetProperty("access_token").GetString()!
        );
    }

    public string ToJson() =>
        JsonSerializer.Serialize(new { phone_number_id = PhoneNumberId, access_token = AccessToken });
}

public record EvolutionConfig(string BaseUrl, string Instance, string ApiKey)
{
    public static EvolutionConfig FromJson(string json)
    {
        var doc = JsonDocument.Parse(json);
        return new EvolutionConfig(
            doc.RootElement.GetProperty("base_url").GetString()!,
            doc.RootElement.GetProperty("instance").GetString()!,
            doc.RootElement.GetProperty("api_key").GetString()!
        );
    }

    public string ToJson() =>
        JsonSerializer.Serialize(new { base_url = BaseUrl, instance = Instance, api_key = ApiKey });
}
```

Criar `src/Atendefy.API/Modules/WhatsApp/IWhatsAppProvider.cs`:

```csharp
using Atendefy.API.Modules.WhatsApp.Models;

namespace Atendefy.API.Modules.WhatsApp;

public interface IWhatsAppProvider
{
    Task SendMessageAsync(OutboundMessage message);
}
```

- [ ] **Step 3: Escrever testes para MetaCloudProvider**

Criar `tests/Atendefy.Tests/WhatsApp/MetaCloudProviderTests.cs`:

```csharp
using Atendefy.API.Modules.WhatsApp;
using Atendefy.API.Modules.WhatsApp.Models;
using Atendefy.Tests.Helpers;
using FluentAssertions;

namespace Atendefy.Tests.WhatsApp;

public class MetaCloudProviderTests
{
    [Fact]
    public async Task SendMessageAsync_ShouldPostToMetaGraphApi()
    {
        var handler = MockHttpMessageHandler.ReturnsJson("""{"messages":[{"id":"wamid.test"}]}""");
        var httpClient = new HttpClient(handler);
        var config = new MetaConfig("123456", "token_abc");
        var provider = new MetaCloudProvider(httpClient, config);

        await provider.SendMessageAsync(new OutboundMessage("5511999999999", "Olá!"));

        handler.Requests.Should().HaveCount(1);
        var req = handler.Requests[0];
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.ToString().Should().Contain("123456/messages");
        req.Headers.Authorization!.Parameter.Should().Be("token_abc");
        var body = await req.Content!.ReadAsStringAsync();
        body.Should().Contain("5511999999999");
        body.Should().Contain("Olá!");
    }
}
```

- [ ] **Step 4: Rodar teste (esperar falha)**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~MetaCloudProviderTests"
```

Expected: FAIL — `MetaCloudProvider` não existe

- [ ] **Step 5: Implementar MetaCloudProvider**

Criar `src/Atendefy.API/Modules/WhatsApp/MetaCloudProvider.cs`:

```csharp
using Atendefy.API.Modules.WhatsApp.Models;
using System.Net.Http.Json;

namespace Atendefy.API.Modules.WhatsApp;

public class MetaCloudProvider(HttpClient httpClient, MetaConfig config) : IWhatsAppProvider
{
    public async Task SendMessageAsync(OutboundMessage message)
    {
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.AccessToken);

        var payload = new
        {
            messaging_product = "whatsapp",
            to = message.ToPhone,
            type = "text",
            text = new { body = message.Text }
        };

        var response = await httpClient.PostAsJsonAsync(
            $"https://graph.facebook.com/v19.0/{config.PhoneNumberId}/messages",
            payload);

        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 6: Escrever teste para EvolutionProvider**

Criar `tests/Atendefy.Tests/WhatsApp/EvolutionProviderTests.cs`:

```csharp
using Atendefy.API.Modules.WhatsApp;
using Atendefy.API.Modules.WhatsApp.Models;
using Atendefy.Tests.Helpers;
using FluentAssertions;

namespace Atendefy.Tests.WhatsApp;

public class EvolutionProviderTests
{
    [Fact]
    public async Task SendMessageAsync_ShouldPostToEvolutionApi()
    {
        var handler = MockHttpMessageHandler.ReturnsJson("""{"key":{"id":"msg_001"}}""");
        var httpClient = new HttpClient(handler);
        var config = new EvolutionConfig("http://evolution:8080", "my-instance", "apikey_xyz");
        var provider = new EvolutionProvider(httpClient, config);

        await provider.SendMessageAsync(new OutboundMessage("5511988888888", "Oi!"));

        handler.Requests.Should().HaveCount(1);
        var req = handler.Requests[0];
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.ToString().Should().Contain("my-instance");
        req.Headers.GetValues("apikey").First().Should().Be("apikey_xyz");
        var body = await req.Content!.ReadAsStringAsync();
        body.Should().Contain("5511988888888");
        body.Should().Contain("Oi!");
    }
}
```

- [ ] **Step 7: Implementar EvolutionProvider**

Criar `src/Atendefy.API/Modules/WhatsApp/EvolutionProvider.cs`:

```csharp
using Atendefy.API.Modules.WhatsApp.Models;
using System.Net.Http.Json;

namespace Atendefy.API.Modules.WhatsApp;

public class EvolutionProvider(HttpClient httpClient, EvolutionConfig config) : IWhatsAppProvider
{
    public async Task SendMessageAsync(OutboundMessage message)
    {
        httpClient.DefaultRequestHeaders.Remove("apikey");
        httpClient.DefaultRequestHeaders.Add("apikey", config.ApiKey);

        var payload = new
        {
            number = message.ToPhone,
            text = message.Text
        };

        var response = await httpClient.PostAsJsonAsync(
            $"{config.BaseUrl.TrimEnd('/')}/message/sendText/{config.Instance}",
            payload);

        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 8: Criar WhatsAppProviderFactory**

Criar `src/Atendefy.API/Modules/WhatsApp/WhatsAppProviderFactory.cs`:

```csharp
using Atendefy.API.Modules.WhatsApp.Models;

namespace Atendefy.API.Modules.WhatsApp;

public class WhatsAppProviderFactory(IHttpClientFactory httpClientFactory)
{
    public IWhatsAppProvider Create(string provider, string configJson)
    {
        return provider switch
        {
            "meta" => new MetaCloudProvider(
                httpClientFactory.CreateClient("whatsapp"),
                MetaConfig.FromJson(configJson)),
            "evolution" => new EvolutionProvider(
                httpClientFactory.CreateClient("whatsapp"),
                EvolutionConfig.FromJson(configJson)),
            _ => throw new ArgumentException($"Provider desconhecido: {provider}")
        };
    }
}
```

- [ ] **Step 9: Rodar todos os testes WhatsApp**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~WhatsApp"
```

Expected: PASS — 2 testes passam

- [ ] **Step 10: Commit**

```bash
git add src/Atendefy.API/Modules/WhatsApp/
git add tests/Atendefy.Tests/Helpers/MockHttpMessageHandler.cs
git add tests/Atendefy.Tests/WhatsApp/
git commit -m "feat: add WhatsApp Gateway abstraction with Meta and Evolution providers"
```

---

## Task 4: WhatsApp Account Config Endpoints

**Files:**
- Create: `src/Atendefy.API/Modules/WhatsApp/Models/CreateAccountRequest.cs`
- Create: `src/Atendefy.API/Modules/WhatsApp/WhatsAppAccountService.cs`
- Create: `src/Atendefy.API/Modules/WhatsApp/WhatsAppEndpoints.cs`

- [ ] **Step 1: Criar request model**

Criar `src/Atendefy.API/Modules/WhatsApp/Models/CreateAccountRequest.cs`:

```csharp
namespace Atendefy.API.Modules.WhatsApp.Models;

public record CreateAccountRequest(
    string Provider,    // "meta" | "evolution"
    string Phone,
    string ConfigJson   // JSON com credenciais do provider
);
```

- [ ] **Step 2: Criar WhatsAppAccountService**

Criar `src/Atendefy.API/Modules/WhatsApp/WhatsAppAccountService.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Tenants.Models;
using Atendefy.API.Modules.Webhooks.Models;
using Atendefy.API.Modules.WhatsApp.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.WhatsApp;

public class WhatsAppAccountService(
    PublicDbContext publicDb,
    TenantDbContextFactory tenantDbFactory)
{
    private static readonly HashSet<string> ValidProviders = ["meta", "evolution"];

    public async Task<Result<WhatsAppAccount>> CreateAsync(
        Guid tenantId, string schemaName, CreateAccountRequest request)
    {
        if (!ValidProviders.Contains(request.Provider))
            return Result<WhatsAppAccount>.Fail("Provider inválido. Use 'meta' ou 'evolution'.");

        if (string.IsNullOrWhiteSpace(request.ConfigJson))
            return Result<WhatsAppAccount>.Fail("ConfigJson é obrigatório.");

        await using var db = tenantDbFactory.Create(schemaName);

        var account = new WhatsAppAccount
        {
            Provider = request.Provider,
            Phone = request.Phone,
            ConfigJson = request.ConfigJson,
            Status = "connected"
        };

        db.WhatsAppAccounts.Add(account);
        await db.SaveChangesAsync();

        // Registrar rota de webhook para o tenant
        var lookupKey = request.Provider == "evolution"
            ? account.Id.ToString("N")  // token único por conta Evolution
            : ExtractMetaPhoneNumberId(request.ConfigJson);

        publicDb.WebhookRoutes.Add(new WebhookRoute
        {
            TenantId = tenantId,
            Provider = request.Provider,
            LookupKey = lookupKey,
            AccountId = account.Id
        });
        await publicDb.SaveChangesAsync();

        return Result<WhatsAppAccount>.Ok(account);
    }

    public async Task<List<WhatsAppAccount>> ListAsync(string schemaName)
    {
        await using var db = tenantDbFactory.Create(schemaName);
        return await db.WhatsAppAccounts.ToListAsync();
    }

    private static string ExtractMetaPhoneNumberId(string configJson)
    {
        try { return MetaConfig.FromJson(configJson).PhoneNumberId; }
        catch { return Guid.NewGuid().ToString("N"); }
    }
}
```

- [ ] **Step 3: Criar WhatsAppEndpoints**

Criar `src/Atendefy.API/Modules/WhatsApp/WhatsAppEndpoints.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Tenants.Models;
using Atendefy.API.Modules.WhatsApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.WhatsApp;

public static class WhatsAppEndpoints
{
    public static IEndpointRouteBuilder MapWhatsAppEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/whatsapp/accounts")
            .WithTags("WhatsApp")
            .RequireAuthorization();

        group.MapPost("/", async (
            [FromBody] CreateAccountRequest request,
            WhatsAppAccountService service,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (tenantId, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var result = await service.CreateAsync(tenantId, schemaName, request);
            return result.IsSuccess
                ? Results.Created($"/whatsapp/accounts/{result.Value!.Id}",
                    new { result.Value.Id, result.Value.Provider, result.Value.Phone, result.Value.Status })
                : Results.BadRequest(new { error = result.Error });
        });

        group.MapGet("/", async (
            WhatsAppAccountService service,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var accounts = await service.ListAsync(schemaName);
            return Results.Ok(accounts.Select(a => new
            {
                a.Id, a.Provider, a.Phone, a.Status, a.CreatedAt
            }));
        });

        return app;
    }

    // Extrai tenant_id do JWT, busca o schema no banco
    private static async Task<(Guid TenantId, string SchemaName, string? Error)> ResolveTenantAsync(
        HttpContext ctx, PublicDbContext publicDb)
    {
        var tenantIdStr = ctx.User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            return (Guid.Empty, string.Empty, "Token inválido");

        var tenant = await publicDb.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null)
            return (Guid.Empty, string.Empty, "Tenant não encontrado");

        return (tenantId, tenant.SchemaName, null);
    }
}
```

- [ ] **Step 4: Verificar build**

```bash
dotnet build Atendefy.sln
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/Atendefy.API/Modules/WhatsApp/
git commit -m "feat: add WhatsApp account config endpoints"
```

---

## Task 5: AI Provider Abstraction

**Files:**
- Create: `src/Atendefy.API/Modules/AI/Models/ChatMessage.cs`
- Create: `src/Atendefy.API/Modules/AI/Models/AICompletionRequest.cs`
- Create: `src/Atendefy.API/Modules/AI/Models/AICompletionResult.cs`
- Create: `src/Atendefy.API/Modules/AI/IAIProvider.cs`
- Create: `src/Atendefy.API/Modules/AI/OpenAIProvider.cs`
- Create: `src/Atendefy.API/Modules/AI/AnthropicProvider.cs`
- Create: `src/Atendefy.API/Modules/AI/AIProviderFactory.cs`
- Create: `tests/Atendefy.Tests/AI/OpenAIProviderTests.cs`
- Create: `tests/Atendefy.Tests/AI/AnthropicProviderTests.cs`

- [ ] **Step 1: Criar modelos de IA**

Criar `src/Atendefy.API/Modules/AI/Models/ChatMessage.cs`:

```csharp
namespace Atendefy.API.Modules.AI.Models;

public record ChatMessage(string Role, string Content);
```

Criar `src/Atendefy.API/Modules/AI/Models/AICompletionRequest.cs`:

```csharp
namespace Atendefy.API.Modules.AI.Models;

public record AICompletionRequest(
    string SystemPrompt,
    IReadOnlyList<ChatMessage> Messages,
    string Model,
    int MaxTokens = 1000
);
```

Criar `src/Atendefy.API/Modules/AI/Models/AICompletionResult.cs`:

```csharp
namespace Atendefy.API.Modules.AI.Models;

public record AICompletionResult(string Content, int TokensUsed);
```

- [ ] **Step 2: Criar interface IAIProvider**

Criar `src/Atendefy.API/Modules/AI/IAIProvider.cs`:

```csharp
using Atendefy.API.Modules.AI.Models;

namespace Atendefy.API.Modules.AI;

public interface IAIProvider
{
    Task<AICompletionResult> CompleteAsync(AICompletionRequest request);
}
```

- [ ] **Step 3: Escrever testes para OpenAIProvider**

Criar `tests/Atendefy.Tests/AI/OpenAIProviderTests.cs`:

```csharp
using Atendefy.API.Modules.AI;
using Atendefy.API.Modules.AI.Models;
using Atendefy.Tests.Helpers;
using FluentAssertions;

namespace Atendefy.Tests.AI;

public class OpenAIProviderTests
{
    [Fact]
    public async Task CompleteAsync_ShouldCallOpenAIAndReturnContent()
    {
        var responseJson = """
            {
                "choices": [{"message": {"content": "Atendemos das 8h às 18h."}}],
                "usage": {"completion_tokens": 12}
            }
            """;
        var handler = MockHttpMessageHandler.ReturnsJson(responseJson);
        var provider = new OpenAIProvider(new HttpClient(handler), "sk-test");

        var result = await provider.CompleteAsync(new AICompletionRequest(
            SystemPrompt: "Você é um assistente.",
            Messages: [new ChatMessage("user", "Qual o horário?")],
            Model: "gpt-4o-mini"
        ));

        result.Content.Should().Be("Atendemos das 8h às 18h.");
        result.TokensUsed.Should().Be(12);
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("chat/completions");
    }

    [Fact]
    public async Task CompleteAsync_ShouldIncludeSystemPromptAsFirstMessage()
    {
        var responseJson = """{"choices":[{"message":{"content":"ok"}}],"usage":{"completion_tokens":1}}""";
        var handler = MockHttpMessageHandler.ReturnsJson(responseJson);
        var provider = new OpenAIProvider(new HttpClient(handler), "sk-test");

        await provider.CompleteAsync(new AICompletionRequest(
            "Prompt do sistema.", [new ChatMessage("user", "oi")], "gpt-4o-mini"));

        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        body.Should().Contain("system");
        body.Should().Contain("Prompt do sistema.");
    }
}
```

- [ ] **Step 4: Rodar testes (esperar falha)**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~OpenAIProviderTests"
```

Expected: FAIL

- [ ] **Step 5: Implementar OpenAIProvider**

Criar `src/Atendefy.API/Modules/AI/OpenAIProvider.cs`:

```csharp
using Atendefy.API.Modules.AI.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace Atendefy.API.Modules.AI;

public class OpenAIProvider(HttpClient httpClient, string apiKey) : IAIProvider
{
    public async Task<AICompletionResult> CompleteAsync(AICompletionRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var messages = new List<object>
        {
            new { role = "system", content = request.SystemPrompt }
        };
        messages.AddRange(request.Messages.Select(m => new { role = m.Role, content = m.Content }));

        var payload = new
        {
            model = request.Model,
            messages,
            max_tokens = request.MaxTokens
        };

        var response = await httpClient.PostAsJsonAsync(
            "https://api.openai.com/v1/chat/completions", payload);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var content = json.GetProperty("choices")[0]
                          .GetProperty("message")
                          .GetProperty("content")
                          .GetString() ?? string.Empty;
        var tokens = json.GetProperty("usage").GetProperty("completion_tokens").GetInt32();

        return new AICompletionResult(content, tokens);
    }
}
```

- [ ] **Step 6: Escrever testes para AnthropicProvider**

Criar `tests/Atendefy.Tests/AI/AnthropicProviderTests.cs`:

```csharp
using Atendefy.API.Modules.AI;
using Atendefy.API.Modules.AI.Models;
using Atendefy.Tests.Helpers;
using FluentAssertions;

namespace Atendefy.Tests.AI;

public class AnthropicProviderTests
{
    [Fact]
    public async Task CompleteAsync_ShouldCallAnthropicAndReturnContent()
    {
        var responseJson = """
            {
                "content": [{"text": "Olá! Como posso ajudar?"}],
                "usage": {"output_tokens": 8}
            }
            """;
        var handler = MockHttpMessageHandler.ReturnsJson(responseJson);
        var provider = new AnthropicProvider(new HttpClient(handler), "sk-ant-test");

        var result = await provider.CompleteAsync(new AICompletionRequest(
            SystemPrompt: "Você é assistente.",
            Messages: [new ChatMessage("user", "Oi!")],
            Model: "claude-haiku-4-5-20251001"
        ));

        result.Content.Should().Be("Olá! Como posso ajudar?");
        result.TokensUsed.Should().Be(8);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("messages");
        handler.Requests[0].Headers.GetValues("x-api-key").First().Should().Be("sk-ant-test");
    }
}
```

- [ ] **Step 7: Implementar AnthropicProvider**

Criar `src/Atendefy.API/Modules/AI/AnthropicProvider.cs`:

```csharp
using Atendefy.API.Modules.AI.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace Atendefy.API.Modules.AI;

public class AnthropicProvider(HttpClient httpClient, string apiKey) : IAIProvider
{
    public async Task<AICompletionResult> CompleteAsync(AICompletionRequest request)
    {
        httpClient.DefaultRequestHeaders.Remove("x-api-key");
        httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        httpClient.DefaultRequestHeaders.Remove("anthropic-version");
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var payload = new
        {
            model = request.Model,
            max_tokens = request.MaxTokens,
            system = request.SystemPrompt,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content })
        };

        var response = await httpClient.PostAsJsonAsync(
            "https://api.anthropic.com/v1/messages", payload);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var content = json.GetProperty("content")[0]
                          .GetProperty("text")
                          .GetString() ?? string.Empty;
        var tokens = json.GetProperty("usage").GetProperty("output_tokens").GetInt32();

        return new AICompletionResult(content, tokens);
    }
}
```

- [ ] **Step 8: Criar AIProviderFactory**

Criar `src/Atendefy.API/Modules/AI/AIProviderFactory.cs`:

```csharp
namespace Atendefy.API.Modules.AI;

public class AIProviderFactory(IHttpClientFactory httpClientFactory)
{
    public IAIProvider Create(string provider, string apiKey)
    {
        var client = httpClientFactory.CreateClient("ai");
        return provider switch
        {
            "openai" => new OpenAIProvider(client, apiKey),
            "anthropic" => new AnthropicProvider(client, apiKey),
            _ => throw new ArgumentException($"Provider de IA desconhecido: {provider}")
        };
    }
}
```

- [ ] **Step 9: Rodar todos os testes AI**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~AI"
```

Expected: PASS — 3 testes passam

- [ ] **Step 10: Commit**

```bash
git add src/Atendefy.API/Modules/AI/
git add tests/Atendefy.Tests/AI/
git commit -m "feat: add AI provider abstraction with OpenAI and Anthropic providers"
```

---

## Task 6: AI Config Endpoints

**Files:**
- Create: `src/Atendefy.API/Modules/AI/Models/AiConfigRequest.cs`
- Create: `src/Atendefy.API/Modules/AI/AiConfigService.cs`
- Create: `src/Atendefy.API/Modules/AI/AIEndpoints.cs`

- [ ] **Step 1: Criar request model**

Criar `src/Atendefy.API/Modules/AI/Models/AiConfigRequest.cs`:

```csharp
namespace Atendefy.API.Modules.AI.Models;

public record AiConfigRequest(
    string Provider,      // "openai" | "anthropic"
    string ApiKey,
    string Model,
    string SystemPrompt
);
```

- [ ] **Step 2: Criar AiConfigService**

Criar `src/Atendefy.API/Modules/AI/AiConfigService.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.AI.Models;
using Atendefy.API.SharedKernel;
using Atendefy.API.SharedKernel.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.AI;

public class AiConfigService(TenantDbContextFactory dbFactory, string encryptionKey)
{
    private static readonly HashSet<string> ValidProviders = ["openai", "anthropic"];

    public async Task<Result<AiConfig>> UpsertAsync(string schemaName, AiConfigRequest request)
    {
        if (!ValidProviders.Contains(request.Provider))
            return Result<AiConfig>.Fail("Provider inválido. Use 'openai' ou 'anthropic'.");
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return Result<AiConfig>.Fail("ApiKey é obrigatória.");
        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
            return Result<AiConfig>.Fail("SystemPrompt é obrigatório.");

        await using var db = dbFactory.Create(schemaName);

        var existing = await db.AiConfigs.FirstOrDefaultAsync();
        if (existing is not null)
        {
            existing.Provider = request.Provider;
            existing.ApiKeyEncrypted = AesEncryption.Encrypt(request.ApiKey, encryptionKey);
            existing.Model = request.Model;
            existing.SystemPrompt = request.SystemPrompt;
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Result<AiConfig>.Ok(existing);
        }

        var config = new AiConfig
        {
            Provider = request.Provider,
            ApiKeyEncrypted = AesEncryption.Encrypt(request.ApiKey, encryptionKey),
            Model = request.Model,
            SystemPrompt = request.SystemPrompt
        };
        db.AiConfigs.Add(config);
        await db.SaveChangesAsync();
        return Result<AiConfig>.Ok(config);
    }

    public async Task<AiConfig?> GetAsync(string schemaName)
    {
        await using var db = dbFactory.Create(schemaName);
        return await db.AiConfigs.FirstOrDefaultAsync();
    }
}
```

- [ ] **Step 3: Criar AIEndpoints**

Criar `src/Atendefy.API/Modules/AI/AIEndpoints.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.AI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.AI;

public static class AIEndpoints
{
    public static IEndpointRouteBuilder MapAIEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ai")
            .WithTags("AI")
            .RequireAuthorization();

        group.MapPut("/config", async (
            [FromBody] AiConfigRequest request,
            AiConfigService service,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var result = await service.UpsertAsync(schemaName, request);
            return result.IsSuccess
                ? Results.Ok(new { result.Value!.Provider, result.Value.Model, result.Value.SystemPrompt })
                : Results.BadRequest(new { error = result.Error });
        });

        group.MapGet("/config", async (
            AiConfigService service,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var config = await service.GetAsync(schemaName);
            return config is null
                ? Results.NotFound(new { error = "Configuração de IA não encontrada" })
                : Results.Ok(new { config.Provider, config.Model, config.SystemPrompt });
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
        return tenant is null
            ? (string.Empty, "Tenant não encontrado")
            : (tenant.SchemaName, null);
    }
}
```

- [ ] **Step 4: Verificar build**

```bash
dotnet build Atendefy.sln
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/Atendefy.API/Modules/AI/
git commit -m "feat: add AI config endpoints with provider and system prompt management"
```

---

## Task 7: Webhook Endpoints

**Files:**
- Create: `src/Atendefy.API/Modules/Webhooks/Models/MetaWebhookPayload.cs`
- Create: `src/Atendefy.API/Modules/Webhooks/Models/EvolutionWebhookPayload.cs`
- Create: `src/Atendefy.API/Modules/Webhooks/Models/InboundMessage.cs`
- Create: `src/Atendefy.API/Modules/Chatbot/Models/InboundMessage.cs`
- Create: `src/Atendefy.API/Modules/Webhooks/MetaWebhookValidator.cs`
- Create: `src/Atendefy.API/Modules/Webhooks/EvolutionWebhookValidator.cs`
- Create: `src/Atendefy.API/Modules/Webhooks/WebhookEndpoints.cs`
- Create: `tests/Atendefy.Tests/WhatsApp/MetaWebhookValidatorTests.cs`

- [ ] **Step 1: Criar modelos de payload**

Criar `src/Atendefy.API/Modules/Chatbot/Models/InboundMessage.cs`:

```csharp
namespace Atendefy.API.Modules.Chatbot.Models;

public record InboundMessage(
    string TenantId,
    string SchemaName,
    string ContactPhone,
    string MessageText,
    string Provider,
    string AccountId
);
```

Criar `src/Atendefy.API/Modules/Webhooks/Models/MetaWebhookPayload.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Atendefy.API.Modules.Webhooks.Models;

public record MetaWebhookPayload(
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("entry")] List<MetaEntry> Entry
);

public record MetaEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("changes")] List<MetaChange> Changes
);

public record MetaChange(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("value")] MetaChangeValue Value
);

public record MetaChangeValue(
    [property: JsonPropertyName("metadata")] MetaMetadata Metadata,
    [property: JsonPropertyName("messages")] List<MetaMessage>? Messages
);

public record MetaMetadata(
    [property: JsonPropertyName("phone_number_id")] string PhoneNumberId
);

public record MetaMessage(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] MetaMessageText? Text
);

public record MetaMessageText(
    [property: JsonPropertyName("body")] string Body
);
```

Criar `src/Atendefy.API/Modules/Webhooks/Models/EvolutionWebhookPayload.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Atendefy.API.Modules.Webhooks.Models;

public record EvolutionWebhookPayload(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("instance")] string Instance,
    [property: JsonPropertyName("data")] EvolutionData Data
);

public record EvolutionData(
    [property: JsonPropertyName("key")] EvolutionKey Key,
    [property: JsonPropertyName("message")] EvolutionMessage? Message
);

public record EvolutionKey(
    [property: JsonPropertyName("remoteJid")] string RemoteJid,
    [property: JsonPropertyName("fromMe")] bool FromMe
);

public record EvolutionMessage(
    [property: JsonPropertyName("conversation")] string? Conversation
);
```

- [ ] **Step 2: Escrever testes para MetaWebhookValidator**

Criar `tests/Atendefy.Tests/WhatsApp/MetaWebhookValidatorTests.cs`:

```csharp
using Atendefy.API.Modules.Webhooks;
using FluentAssertions;
using System.Security.Cryptography;
using System.Text;

namespace Atendefy.Tests.WhatsApp;

public class MetaWebhookValidatorTests
{
    private const string Secret = "meu_app_secret";
    private readonly MetaWebhookValidator _validator = new(Secret);

    [Fact]
    public void IsValid_WithCorrectSignature_ShouldReturnTrue()
    {
        var body = """{"object":"whatsapp_business_account","entry":[]}""";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var hash = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), bodyBytes)).ToLower();
        var signature = $"sha256={hash}";

        _validator.IsValid(bodyBytes, signature).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithWrongSignature_ShouldReturnFalse()
    {
        var body = Encoding.UTF8.GetBytes("some body");
        _validator.IsValid(body, "sha256=invalidsignature").Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithMissingPrefix_ShouldReturnFalse()
    {
        var body = Encoding.UTF8.GetBytes("body");
        _validator.IsValid(body, "invalidsignature").Should().BeFalse();
    }
}
```

- [ ] **Step 3: Rodar testes (esperar falha)**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~MetaWebhookValidatorTests"
```

Expected: FAIL

- [ ] **Step 4: Implementar MetaWebhookValidator**

Criar `src/Atendefy.API/Modules/Webhooks/MetaWebhookValidator.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Atendefy.API.Modules.Webhooks;

public class MetaWebhookValidator(string appSecret)
{
    public bool IsValid(byte[] body, string signature)
    {
        if (!signature.StartsWith("sha256=")) return false;

        var expectedHash = signature[7..];
        var actualHash = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body)).ToLower();

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(expectedHash),
            Convert.FromHexString(actualHash));
    }
}
```

- [ ] **Step 5: Implementar EvolutionWebhookValidator**

Criar `src/Atendefy.API/Modules/Webhooks/EvolutionWebhookValidator.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Webhooks.Models;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Webhooks;

public class EvolutionWebhookValidator(PublicDbContext publicDb)
{
    public async Task<WebhookRoute?> ResolveAsync(string token)
    {
        return await publicDb.WebhookRoutes
            .FirstOrDefaultAsync(r => r.Provider == "evolution" && r.LookupKey == token);
    }
}
```

- [ ] **Step 6: Criar WebhookEndpoints**

Criar `src/Atendefy.API/Modules/Webhooks/WebhookEndpoints.cs`:

```csharp
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

        // Meta: verificação do webhook (GET)
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

        // Meta: recebimento de mensagem (POST)
        group.MapPost("/meta", async (
            HttpContext ctx,
            PublicDbContext publicDb,
            RedisStreamService streams,
            MetaWebhookValidator validator) =>
        {
            // Lê body bruto para validar assinatura
            ctx.Request.EnableBuffering();
            var body = await ctx.Request.BodyReader.ReadAllBytesAsync();
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

                await PublishInboundAsync(streams, new InboundMessage(
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

        // Evolution: recebimento de mensagem (POST)
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

            await PublishInboundAsync(streams, new InboundMessage(
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

    private static Task PublishInboundAsync(RedisStreamService streams, InboundMessage msg)
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
```

- [ ] **Step 7: Rodar testes de validação**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~MetaWebhookValidatorTests"
```

Expected: PASS — 3 testes passam

- [ ] **Step 8: Verificar build**

```bash
dotnet build Atendefy.sln
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 9: Commit**

```bash
git add src/Atendefy.API/Modules/Webhooks/
git add src/Atendefy.API/Modules/Chatbot/Models/InboundMessage.cs
git add tests/Atendefy.Tests/WhatsApp/MetaWebhookValidatorTests.cs
git commit -m "feat: add webhook endpoints for Meta and Evolution with HMAC validation"
```

---

## Task 8: Rate Limiter por Tenant

**Files:**
- Create: `src/Atendefy.API/Infrastructure/RateLimiting/TenantRateLimiter.cs`
- Create: `tests/Atendefy.Tests/Infrastructure/TenantRateLimiterTests.cs`

- [ ] **Step 1: Escrever testes para TenantRateLimiter**

Criar `tests/Atendefy.Tests/Infrastructure/TenantRateLimiterTests.cs`:

```csharp
using Atendefy.API.Infrastructure.Cache;
using Atendefy.API.Infrastructure.RateLimiting;
using FluentAssertions;
using NSubstitute;

namespace Atendefy.Tests.Infrastructure;

public class TenantRateLimiterTests
{
    private readonly RedisService _redis = Substitute.For<RedisService>(
        Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());

    [Fact]
    public async Task IsAllowedAsync_WhenUnderLimit_ShouldReturnTrue()
    {
        _redis.IncrementAsync(Arg.Any<string>(), 1).Returns(Task.FromResult(1L));
        _redis.ExistsAsync(Arg.Any<string>()).Returns(Task.FromResult(false));
        var limiter = new TenantRateLimiter(_redis, limit: 60);

        var result = await limiter.IsAllowedAsync("tenant_abc");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAllowedAsync_WhenOverLimit_ShouldReturnFalse()
    {
        _redis.IncrementAsync(Arg.Any<string>(), 1).Returns(Task.FromResult(61L));
        var limiter = new TenantRateLimiter(_redis, limit: 60);

        var result = await limiter.IsAllowedAsync("tenant_abc");

        result.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Rodar testes (esperar falha)**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~TenantRateLimiterTests"
```

Expected: FAIL

- [ ] **Step 3: Implementar TenantRateLimiter**

Criar `src/Atendefy.API/Infrastructure/RateLimiting/TenantRateLimiter.cs`:

```csharp
using Atendefy.API.Infrastructure.Cache;

namespace Atendefy.API.Infrastructure.RateLimiting;

public class TenantRateLimiter(RedisService redis, int limit = 60)
{
    public async Task<bool> IsAllowedAsync(string tenantId)
    {
        var minute = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var key = $"ratelimit:{tenantId}:{minute}";

        var count = await redis.IncrementAsync(key);

        // Define TTL de 2 minutos na primeira vez
        if (count == 1)
            await redis.SetAsync($"{key}:ttl", "1", TimeSpan.FromMinutes(2));

        return count <= limit;
    }
}
```

- [ ] **Step 4: Rodar testes**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~TenantRateLimiterTests"
```

Expected: PASS — 2 testes passam

- [ ] **Step 5: Commit**

```bash
git add src/Atendefy.API/Infrastructure/RateLimiting/
git add tests/Atendefy.Tests/Infrastructure/TenantRateLimiterTests.cs
git commit -m "feat: add Redis-based tenant rate limiter"
```

---

## Task 9: Conversation Engine

**Files:**
- Create: `src/Atendefy.API/Modules/Chatbot/ConversationService.cs`
- Create: `src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs`
- Create: `tests/Atendefy.Tests/Chatbot/ConversationServiceTests.cs`

- [ ] **Step 1: Escrever testes para ConversationService**

Criar `tests/Atendefy.Tests/Chatbot/ConversationServiceTests.cs`:

```csharp
using Atendefy.API.Infrastructure.Cache;
using Atendefy.API.Modules.AI.Models;
using Atendefy.API.Modules.Chatbot;
using FluentAssertions;
using NSubstitute;
using StackExchange.Redis;

namespace Atendefy.Tests.Chatbot;

public class ConversationServiceTests
{
    private readonly RedisService _redis;
    private readonly ConversationService _sut;

    public ConversationServiceTests()
    {
        var db = Substitute.For<IDatabase>();
        var conn = Substitute.For<IConnectionMultiplexer>();
        conn.GetDatabase().Returns(db);
        _redis = new RedisService(conn);

        // GetAsync retorna null por padrão (sessão nova)
        db.StringGetAsync(Arg.Any<RedisKey>()).Returns(new RedisValue());

        _sut = new ConversationService(_redis);
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_WhenNewSession_ShouldReturnEmptyList()
    {
        var messages = await _sut.GetOrCreateSessionAsync("tenant_abc", "5511999999999");
        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildContextMessages_ShouldPrependUserMessage()
    {
        var history = new List<ChatMessage>
        {
            new("user", "Qual o horário?"),
            new("assistant", "Das 8h às 18h.")
        };

        var context = ConversationService.BuildContextMessages(history, "Nova pergunta");

        context.Should().HaveCount(3);
        context.Last().Role.Should().Be("user");
        context.Last().Content.Should().Be("Nova pergunta");
    }
}
```

- [ ] **Step 2: Rodar testes (esperar falha)**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~ConversationServiceTests"
```

Expected: FAIL

- [ ] **Step 3: Implementar ConversationService**

Criar `src/Atendefy.API/Modules/Chatbot/ConversationService.cs`:

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
        var messages = new List<ChatMessage>(history)
        {
            new("user", newUserMessage)
        };
        // Manter até 20 mensagens para controlar tamanho do contexto
        if (messages.Count > 20)
            messages = messages.TakeLast(20).ToList();
        return messages;
    }

    public static async Task PersistAsync(
        TenantDbContextFactory dbFactory,
        string schemaName,
        string contactPhone,
        string userMessage,
        string assistantReply,
        int tokensUsed)
    {
        await using var db = dbFactory.Create(schemaName);

        var conversation = await db.Conversations
            .FirstOrDefaultAsync(c => c.ContactPhone == contactPhone);

        if (conversation is null)
        {
            conversation = new Conversation { ContactPhone = contactPhone };
            db.Conversations.Add(conversation);
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

        // Atualizar contador de uso
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
    }
}
```

- [ ] **Step 4: Implementar ConversationWorker**

Criar `src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs`:

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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Atendefy.API.Modules.Chatbot;

public class ConversationWorker(
    RedisStreamService streams,
    ConversationService conversationService,
    TenantDbContextFactory tenantDbFactory,
    PublicDbContext publicDb,
    AIProviderFactory aiFactory,
    WhatsAppProviderFactory whatsAppFactory,
    TenantRateLimiter rateLimiter,
    string encryptionKey,
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

        // Rate limiting
        if (!await rateLimiter.IsAllowedAsync(msg.TenantId))
        {
            logger.LogWarning("Rate limit atingido para tenant {TenantId}", msg.TenantId);
            return;
        }

        // Carregar config de IA
        await using var tenantDb = tenantDbFactory.Create(msg.SchemaName);
        var aiConfig = await tenantDb.AiConfigs.FirstOrDefaultAsync();
        if (aiConfig is null)
        {
            logger.LogWarning("Tenant {TenantId} sem config de IA", msg.TenantId);
            return;
        }

        // Carregar config de WhatsApp
        var waAccount = await tenantDb.WhatsAppAccounts.FindAsync(Guid.Parse(msg.AccountId));
        if (waAccount?.ConfigJson is null)
        {
            logger.LogWarning("Conta WhatsApp {AccountId} não encontrada", msg.AccountId);
            return;
        }

        // Construir contexto da conversa
        var history = await conversationService.GetOrCreateSessionAsync(msg.TenantId, msg.ContactPhone);
        var contextMessages = ConversationService.BuildContextMessages(history, msg.MessageText);

        // Chamar IA
        var decryptedKey = AesEncryption.Decrypt(aiConfig.ApiKeyEncrypted!, encryptionKey);
        var aiProvider = aiFactory.Create(aiConfig.Provider, decryptedKey);
        var aiResult = await aiProvider.CompleteAsync(new AICompletionRequest(
            SystemPrompt: aiConfig.SystemPrompt ?? "Você é um assistente prestativo.",
            Messages: contextMessages,
            Model: aiConfig.Model ?? "gpt-4o-mini"
        ));

        // Enviar resposta via WhatsApp
        var waProvider = whatsAppFactory.Create(waAccount.Provider, waAccount.ConfigJson);
        await waProvider.SendMessageAsync(new OutboundMessage(msg.ContactPhone, aiResult.Content));

        // Atualizar sessão Redis
        contextMessages.Add(new("assistant", aiResult.Content));
        await conversationService.SaveSessionAsync(msg.TenantId, msg.ContactPhone, contextMessages);

        // Persistir no banco do tenant
        await ConversationService.PersistAsync(
            tenantDbFactory, msg.SchemaName, msg.ContactPhone,
            msg.MessageText, aiResult.Content, aiResult.TokensUsed);

        logger.LogInformation("Mensagem processada para tenant {TenantId}, contato {Phone}",
            msg.TenantId, msg.ContactPhone);
    }
}
```

- [ ] **Step 5: Rodar testes**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~ConversationServiceTests"
```

Expected: PASS — 2 testes passam

- [ ] **Step 6: Verificar build completo**

```bash
dotnet build Atendefy.sln
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add src/Atendefy.API/Modules/Chatbot/
git add tests/Atendefy.Tests/Chatbot/
git commit -m "feat: add ConversationService and ConversationWorker background service"
```

---

## Task 10: Wire up Program.cs

**Files:**
- Modify: `src/Atendefy.API/Program.cs`
- Modify: `src/Atendefy.API/appsettings.json`
- Modify: `src/Atendefy.API/appsettings.Development.json`

- [ ] **Step 1: Atualizar appsettings.json com novas configurações**

Adicionar ao objeto raiz de `src/Atendefy.API/appsettings.json`:

```json
{
  "Serilog": { ... },
  "ConnectionStrings": { ... },
  "Jwt": { ... },
  "Encryption": { ... },
  "App": { ... },
  "Meta": {
    "WebhookVerifyToken": ""
  },
  "RateLimit": {
    "MessagesPerMinute": 60
  }
}
```

- [ ] **Step 2: Atualizar appsettings.Development.json**

```json
{
  "Serilog": {
    "MinimumLevel": { "Default": "Debug" }
  },
  "App": {
    "BaseDomain": "localhost"
  },
  "Meta": {
    "WebhookVerifyToken": "dev_verify_token"
  }
}
```

- [ ] **Step 3: Atualizar Program.cs com os novos serviços**

Substituir o conteúdo de `src/Atendefy.API/Program.cs`:

```csharp
using Atendefy.API.Infrastructure.Cache;
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Infrastructure.Messaging;
using Atendefy.API.Infrastructure.RateLimiting;
using Atendefy.API.Modules.AI;
using Atendefy.API.Modules.Auth;
using Atendefy.API.Modules.Chatbot;
using Atendefy.API.Modules.Tenants;
using Atendefy.API.Modules.Webhooks;
using Atendefy.API.Modules.WhatsApp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using StackExchange.Redis;
using System.Text;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, config) =>
    config.ReadFrom.Configuration(ctx.Configuration).ReadFrom.Services(services));

var connStr        = builder.Configuration.GetConnectionString("Postgres")!;
var redisConn      = builder.Configuration.GetConnectionString("Redis")!;
var jwtSecret      = builder.Configuration["Jwt:Secret"]!;
var jwtIssuer      = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience    = builder.Configuration["Jwt:Audience"]!;
var baseDomain     = builder.Configuration["App:BaseDomain"]!;
var encryptionKey  = builder.Configuration["Encryption:Key"]!;
var metaAppSecret  = builder.Configuration["Meta:AppSecret"] ?? string.Empty;
var rateLimit      = builder.Configuration.GetValue<int>("RateLimit:MessagesPerMinute", 60);

// Database
builder.Services.AddDbContext<PublicDbContext>(opt => opt.UseNpgsql(connStr));
builder.Services.AddSingleton(new TenantDbContextFactory(connStr));

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));
builder.Services.AddSingleton<RedisService>();
builder.Services.AddSingleton<RedisStreamService>();

// Rate Limiting
builder.Services.AddSingleton(sp =>
    new TenantRateLimiter(sp.GetRequiredService<RedisService>(), rateLimit));

// Tenant
builder.Services.AddSingleton(new TenantResolver(baseDomain));
builder.Services.AddScoped<TenantService>();
builder.Services.AddSingleton<ITenantProvisioner>(_ => new TenantProvisioner(connStr));

// Auth
builder.Services.AddSingleton(new JwtService(jwtSecret, jwtIssuer, jwtAudience));
builder.Services.AddScoped<AuthService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.MapInboundClaims = false;
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true, ValidIssuer = jwtIssuer,
            ValidateAudience = true, ValidAudience = jwtAudience,
            ValidateLifetime = true, ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

// WhatsApp
builder.Services.AddHttpClient("whatsapp");
builder.Services.AddSingleton<WhatsAppProviderFactory>();
builder.Services.AddScoped<WhatsAppAccountService>();

// AI
builder.Services.AddHttpClient("ai");
builder.Services.AddSingleton<AIProviderFactory>();
builder.Services.AddScoped(sp =>
    new AiConfigService(sp.GetRequiredService<TenantDbContextFactory>(), encryptionKey));

// Webhooks
builder.Services.AddSingleton(new MetaWebhookValidator(metaAppSecret));
builder.Services.AddScoped<EvolutionWebhookValidator>();

// Chatbot
builder.Services.AddSingleton<ConversationService>();
builder.Services.AddHostedService(sp => new ConversationWorker(
    sp.GetRequiredService<RedisStreamService>(),
    sp.GetRequiredService<ConversationService>(),
    sp.GetRequiredService<TenantDbContextFactory>(),
    sp.GetRequiredService<PublicDbContext>(),
    sp.GetRequiredService<AIProviderFactory>(),
    sp.GetRequiredService<WhatsAppProviderFactory>(),
    sp.GetRequiredService<TenantRateLimiter>(),
    encryptionKey,
    sp.GetRequiredService<ILogger<ConversationWorker>>()));

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS
var allowedOrigins = new List<string> { $"https://app.{baseDomain}" };
if (builder.Environment.IsDevelopment())
    allowedOrigins.Add("http://localhost:5173");

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins.ToArray())
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (ctx, next) =>
{
    var resolver = ctx.RequestServices.GetRequiredService<TenantResolver>();
    var tenantId = resolver.Resolve(ctx);
    if (tenantId is not null)
        ctx.Items["TenantId"] = tenantId;
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .WithTags("System");

app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapWhatsAppEndpoints();
app.MapAIEndpoints();
app.MapWebhookEndpoints();

// Automatic migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PublicDbContext>();
    try { await db.Database.MigrateAsync(); }
    catch (Exception ex) { Log.Fatal(ex, "Database migration failed"); throw; }
}

app.Run();

public partial class Program { }
```

- [ ] **Step 4: Rodar todos os testes**

```bash
dotnet test Atendefy.sln
```

Expected: todos os testes passam

- [ ] **Step 5: Build e smoke test no Docker**

```bash
cd infra
docker compose build atendefy-api
docker compose up -d
```

Após subir, validar:
```bash
curl http://localhost:8080/health
# Expected: {"status":"healthy","timestamp":"..."}

curl -s http://localhost:8080/swagger/index.html | head -5
# Expected: HTML da página Swagger
```

- [ ] **Step 6: Commit**

```bash
git add src/Atendefy.API/Program.cs
git add src/Atendefy.API/appsettings*.json
git commit -m "feat: wire up WhatsApp, AI, Webhooks and ConversationWorker in Program.cs"
```

---

## Verificação Final do Plano 2

Após completar as 10 tasks, verificar:

- [ ] `dotnet test Atendefy.sln` — todos os testes passam
- [ ] `POST /whatsapp/accounts` (com JWT válido) → cria conta e registra em `webhook_routes`
- [ ] `PUT /ai/config` (com JWT válido) → salva config de IA com API key criptografada
- [ ] `GET /ai/config` (com JWT válido) → retorna provider e model (SEM expor a key)
- [ ] `GET /webhooks/meta?hub.mode=subscribe&hub.verify_token=dev_verify_token&hub.challenge=1234` → retorna `1234`
- [ ] `POST /webhooks/evolution?token={token_valido}` → retorna 200 e publica no Redis Stream
- [ ] Redis Stream `messages.inbound` recebe mensagem após webhook
- [ ] ConversationWorker processa mensagem (verificar logs: "Mensagem processada para tenant...")

---

## Próximos Planos

**Plano 3 — Billing Module**
- Entidades `Plan`, `Subscription`, `Invoice`
- Limites via `limits_json` por plano
- Webhooks Asaas (boleto, Pix) + Stripe (cartão internacional)
- Suspensão automática por inadimplência
- Endpoints de billing no painel do cliente

**Plano 4 — Frontend React**
- Projeto React + Vite + TypeScript + shadcn/ui
- Telas: Dashboard, WhatsApp, Chatbot (editor de prompt), IA, Conversas, Billing
- Painel Super Admin
- Onboarding wizard
