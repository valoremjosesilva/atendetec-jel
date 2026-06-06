# Atendefy — Plano 3: Billing Module

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar billing com planos, assinaturas, cobranças via Asaas (boleto/Pix) e Stripe (cartão), e suspensão automática de tenants inadimplentes.

**Architecture:** Entidades `Plan`, `Subscription` e `Invoice` no schema público PostgreSQL. `BillingService` orquestra o ciclo de vida da assinatura. Dois gateways via HttpClient raw (`AsaasGateway`, `StripeGateway`) implementam `IBillingGateway`. `SuspensionWorker` (IHostedService) roda diariamente e suspende tenants com faturas vencidas há mais de 3 dias.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core 8 + Npgsql, HttpClient (raw — sem SDKs de vendor), xUnit + FluentAssertions + NSubstitute

---

## Planos Subsequentes

| Plano | Arquivo | Conteúdo |
|---|---|---|
| Plano 4 | `2026-06-04-atendefy-plano4-frontend.md` | React + Vite SPA, todas as telas |

---

## Contexto do Projeto

Padrões estabelecidos no projeto (não recriar):
- `BaseEntity` em `src/Atendefy.API/SharedKernel/BaseEntity.cs` — `Id (Guid, init = Guid.NewGuid())`, `CreatedAt`, `UpdatedAt`, `IsDeleted`, `DeletedAt`
- `Result<T>` em `src/Atendefy.API/SharedKernel/Result.cs` — `Result<T>.Ok(value)`, `Result<T>.Fail(error)`, `.IsSuccess`, `.Value`, `.Error`
- `Tenant` em `src/Atendefy.API/Modules/Tenants/Models/Tenant.cs` — tem `Status ("active"|"suspended"|"cancelled")` e `PlanId (Guid?)` já mapeados
- `PublicDbContext` em `src/Atendefy.API/Infrastructure/Database/PublicDbContext.cs` — schema "public"; padrão: `e.ToTable()`, `e.HasKey()`, `e.HasQueryFilter(x => !x.IsDeleted)`
- JWT claim: `tenant_id` (Guid) no token
- Endpoint pattern: `app.MapGroup("/path").WithTags("Tag").RequireAuthorization()`
- Tenant resolution nos endpoints autenticados: `ctx.User.FindFirst("tenant_id")?.Value`
- `MockHttpMessageHandler` já existe em `tests/Atendefy.Tests/Helpers/MockHttpMessageHandler.cs`
- Stripe API usa `application/x-www-form-urlencoded` (não JSON!) e `Authorization: Bearer sk_xxx`
- Asaas API usa JSON com header `access_token: $KEY`

---

## Mapa de Arquivos

```
src/Atendefy.API/
├── Modules/
│   └── Billing/
│       ├── Models/
│       │   ├── Plan.cs                       ← entity public schema
│       │   ├── Subscription.cs               ← entity public schema
│       │   ├── Invoice.cs                    ← entity public schema
│       │   ├── PlanLimits.cs                 ← record helper para deserializar limits_json
│       │   ├── BillingCharge.cs              ← DTO de retorno do gateway
│       │   └── CreateSubscriptionRequest.cs  ← request body para POST /billing/subscribe
│       ├── Gateways/
│       │   ├── IBillingGateway.cs            ← interface com 5 métodos
│       │   ├── AsaasGateway.cs               ← implementação Asaas (boleto/Pix)
│       │   ├── StripeGateway.cs              ← implementação Stripe (cartão, form-encoded)
│       │   └── BillingGatewayFactory.cs      ← resolve gateway por provider string
│       ├── BillingService.cs                 ← Subscribe, ProcessPaymentEvent, Cancel
│       ├── BillingEndpoints.cs               ← GET /billing/plans, POST /billing/subscribe, GET+DELETE /billing/subscription
│       ├── BillingWebhookEndpoints.cs        ← POST /billing/webhooks/asaas, POST /billing/webhooks/stripe
│       └── SuspensionWorker.cs               ← BackgroundService diário
├── Infrastructure/
│   └── Database/
│       ├── PublicDbContext.cs                ← (modify) add Plan, Subscription, Invoice DbSets
│       └── Migrations/                       ← new migration: AddBillingTables
tests/Atendefy.Tests/
└── Billing/
    ├── AsaasGatewayTests.cs
    ├── StripeGatewayTests.cs
    ├── BillingServiceTests.cs
    └── SuspensionWorkerTests.cs
```

---

## Task 1: Billing Entities + PublicDbContext + Migration

**Files:**
- Create: `src/Atendefy.API/Modules/Billing/Models/Plan.cs`
- Create: `src/Atendefy.API/Modules/Billing/Models/Subscription.cs`
- Create: `src/Atendefy.API/Modules/Billing/Models/Invoice.cs`
- Create: `src/Atendefy.API/Modules/Billing/Models/PlanLimits.cs`
- Create: `src/Atendefy.API/Modules/Billing/Models/BillingCharge.cs`
- Create: `src/Atendefy.API/Modules/Billing/Models/CreateSubscriptionRequest.cs`
- Modify: `src/Atendefy.API/Infrastructure/Database/PublicDbContext.cs`
- Create: migration via CLI

- [ ] **Step 1: Criar Plan.cs**

Criar `src/Atendefy.API/Modules/Billing/Models/Plan.cs`:

```csharp
using Atendefy.API.SharedKernel;

namespace Atendefy.API.Modules.Billing.Models;

public class Plan : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    public string LimitsJson { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 2: Criar Subscription.cs**

Criar `src/Atendefy.API/Modules/Billing/Models/Subscription.cs`:

```csharp
using Atendefy.API.SharedKernel;

namespace Atendefy.API.Modules.Billing.Models;

public class Subscription : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid PlanId { get; set; }
    public string Status { get; set; } = "pending";         // pending|active|past_due|suspended|cancelled
    public string BillingCycle { get; set; } = "monthly";   // monthly|yearly
    public string Provider { get; set; } = string.Empty;    // asaas|stripe
    public string? ExternalCustomerId { get; set; }         // customer ID no provider
    public string? ExternalId { get; set; }                 // last charge/payment ID no provider
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
}
```

- [ ] **Step 3: Criar Invoice.cs**

Criar `src/Atendefy.API/Modules/Billing/Models/Invoice.cs`:

```csharp
using Atendefy.API.SharedKernel;

namespace Atendefy.API.Modules.Billing.Models;

public class Invoice : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public Guid TenantId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "pending";         // pending|paid|overdue|cancelled
    public string Provider { get; set; } = string.Empty;    // asaas|stripe
    public string BillingType { get; set; } = string.Empty; // BOLETO|PIX|CREDIT_CARD
    public string? ExternalId { get; set; }                 // payment ID no provider
    public DateTime DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? BoletoUrl { get; set; }
    public string? BoletoBarcode { get; set; }
    public string? PixCopyPaste { get; set; }
    public string? ClientSecret { get; set; }               // Stripe Payment Intent client_secret
}
```

- [ ] **Step 4: Criar PlanLimits.cs**

Criar `src/Atendefy.API/Modules/Billing/Models/PlanLimits.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atendefy.API.Modules.Billing.Models;

public record PlanLimits(
    [property: JsonPropertyName("messages_per_month")] int MessagesPerMonth = 1000,
    [property: JsonPropertyName("whatsapp_accounts")] int WhatsAppAccounts = 1,
    [property: JsonPropertyName("team_members")] int TeamMembers = 3
)
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static PlanLimits FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new PlanLimits();
        try { return JsonSerializer.Deserialize<PlanLimits>(json, Opts) ?? new PlanLimits(); }
        catch { return new PlanLimits(); }
    }
}
```

- [ ] **Step 5: Criar BillingCharge.cs**

Criar `src/Atendefy.API/Modules/Billing/Models/BillingCharge.cs`:

```csharp
namespace Atendefy.API.Modules.Billing.Models;

public record BillingCharge(
    string ExternalId,
    string? BoletoUrl,
    string? BoletoBarcode,
    string? PixCopyPaste,
    string? ClientSecret    // Stripe Payment Intent client_secret
);
```

- [ ] **Step 6: Criar CreateSubscriptionRequest.cs**

Criar `src/Atendefy.API/Modules/Billing/Models/CreateSubscriptionRequest.cs`:

```csharp
namespace Atendefy.API.Modules.Billing.Models;

public record CreateSubscriptionRequest(
    Guid PlanId,
    string Provider,          // "asaas" | "stripe"
    string BillingType,       // "BOLETO" | "PIX" | "CREDIT_CARD"
    string BillingCycle,      // "monthly" | "yearly"
    string CpfCnpj,           // CPF ou CNPJ (obrigatório para Asaas)
    string? PaymentMethodId   // Stripe payment method ID (obrigatório para CREDIT_CARD)
);
```

- [ ] **Step 7: Adicionar DbSets ao PublicDbContext**

Modificar `src/Atendefy.API/Infrastructure/Database/PublicDbContext.cs` — adicionar os usings, DbSets e configurações no `OnModelCreating`:

```csharp
using Atendefy.API.Modules.Billing.Models;
using Atendefy.API.Modules.Tenants.Models;
using Atendefy.API.Modules.Webhooks.Models;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Infrastructure.Database;

public class PublicDbContext(DbContextOptions<PublicDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<WebhookRoute> WebhookRoutes => Set<WebhookRoute>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Invoice> Invoices => Set<Invoice>();

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

        modelBuilder.Entity<Plan>(e =>
        {
            e.ToTable("plans");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.PriceMonthly).HasColumnType("decimal(10,2)");
            e.Property(x => x.PriceYearly).HasColumnType("decimal(10,2)");
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Subscription>(e =>
        {
            e.ToTable("subscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(50).IsRequired();
            e.Property(x => x.BillingCycle).HasMaxLength(20).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            e.Property(x => x.ExternalCustomerId).HasMaxLength(200);
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Invoice>(e =>
        {
            e.ToTable("invoices");
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("decimal(10,2)");
            e.Property(x => x.Status).HasMaxLength(50).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            e.Property(x => x.BillingType).HasMaxLength(50).IsRequired();
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.HasOne<Subscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
```

- [ ] **Step 8: Gerar migration**

```bash
cd src/Atendefy.API
dotnet ef migrations add AddBillingTables --context PublicDbContext --output-dir Infrastructure/Database/Migrations
cd ../..
```

Expected: `Build succeeded. Done.`

- [ ] **Step 9: Verificar build**

```bash
dotnet build Atendefy.slnx
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 10: Commit**

```bash
git add src/Atendefy.API/Modules/Billing/Models/
git add src/Atendefy.API/Infrastructure/Database/PublicDbContext.cs
git add src/Atendefy.API/Infrastructure/Database/Migrations/
git commit -m "feat: add billing entities (Plan, Subscription, Invoice) and migration"
```

---

## Task 2: IBillingGateway + AsaasGateway

**Files:**
- Create: `src/Atendefy.API/Modules/Billing/Gateways/IBillingGateway.cs`
- Create: `src/Atendefy.API/Modules/Billing/Gateways/AsaasGateway.cs`
- Create: `src/Atendefy.API/Modules/Billing/Gateways/BillingGatewayFactory.cs`
- Create: `tests/Atendefy.Tests/Billing/AsaasGatewayTests.cs`

**Sobre a API Asaas:**
- Sandbox: `https://sandbox.asaas.com/api/v3`
- Produção: `https://api.asaas.com/v3`
- Auth: header `access_token: $API_KEY`
- Webhook: header `asaas-access-token: $WEBHOOK_TOKEN`
- Eventos relevantes: `PAYMENT_RECEIVED`, `PAYMENT_OVERDUE`, `PAYMENT_DELETED`

- [ ] **Step 1: Criar IBillingGateway**

Criar `src/Atendefy.API/Modules/Billing/Gateways/IBillingGateway.cs`:

```csharp
using Atendefy.API.Modules.Billing.Models;

namespace Atendefy.API.Modules.Billing.Gateways;

public record CreateChargeArgs(
    string CustomerExternalId,
    decimal Amount,
    string BillingType,     // BOLETO | PIX | CREDIT_CARD
    string Description,
    DateOnly DueDate,
    string? PaymentMethodId  // Stripe only
);

public record WebhookEvent(
    string ExternalId,      // payment/charge ID no provider
    bool IsPaid,
    bool IsOverdue,
    bool IsCancelled
);

public interface IBillingGateway
{
    Task<string> CreateCustomerAsync(string name, string email, string cpfCnpj);
    Task<BillingCharge> CreateChargeAsync(CreateChargeArgs args);
    Task CancelChargeAsync(string externalId);
    bool ValidateWebhook(byte[] payload, string headerValue);
    WebhookEvent? ParseWebhookEvent(string json);
}
```

- [ ] **Step 2: Escrever testes para AsaasGateway**

Criar `tests/Atendefy.Tests/Billing/AsaasGatewayTests.cs`:

```csharp
using Atendefy.API.Modules.Billing.Gateways;
using Atendefy.Tests.Helpers;
using FluentAssertions;
using System.Text;

namespace Atendefy.Tests.Billing;

public class AsaasGatewayTests
{
    [Fact]
    public async Task CreateCustomerAsync_ShouldPostToAsaasAndReturnId()
    {
        var handler = MockHttpMessageHandler.ReturnsJson("""{"id":"cus_abc123","name":"Empresa Teste"}""");
        var gateway = new AsaasGateway(new HttpClient(handler), "sk_sandbox_key", "webhook_token", isSandbox: true);

        var id = await gateway.CreateCustomerAsync("Empresa Teste", "empresa@teste.com", "12345678000190");

        id.Should().Be("cus_abc123");
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("/customers");
        handler.Requests[0].Headers.GetValues("access_token").First().Should().Be("sk_sandbox_key");
    }

    [Fact]
    public async Task CreateChargeAsync_Boleto_ShouldReturnBoletoData()
    {
        var response = """
            {
                "id": "pay_xyz789",
                "status": "PENDING",
                "bankSlipUrl": "https://asaas.com/b/pdf/pay_xyz789",
                "identificationField": "1234.5678 9012.3456"
            }
            """;
        var handler = MockHttpMessageHandler.ReturnsJson(response);
        var gateway = new AsaasGateway(new HttpClient(handler), "sk_sandbox_key", "webhook_token", isSandbox: true);

        var charge = await gateway.CreateChargeAsync(new CreateChargeArgs(
            "cus_abc123", 99.90m, "BOLETO", "Plano Starter - Mensal",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), null));

        charge.ExternalId.Should().Be("pay_xyz789");
        charge.BoletoUrl.Should().Be("https://asaas.com/b/pdf/pay_xyz789");
        charge.BoletoBarcode.Should().Be("1234.5678 9012.3456");
    }

    [Fact]
    public async Task CreateChargeAsync_Pix_ShouldReturnPixData()
    {
        var response = """
            {
                "id": "pay_pix001",
                "status": "PENDING",
                "pixTransaction": {
                    "qrCode": {
                        "payload": "00020126360014br.gov.bcb.pix...",
                        "encodedImage": "base64img"
                    }
                }
            }
            """;
        var handler = MockHttpMessageHandler.ReturnsJson(response);
        var gateway = new AsaasGateway(new HttpClient(handler), "sk_sandbox_key", "webhook_token", isSandbox: true);

        var charge = await gateway.CreateChargeAsync(new CreateChargeArgs(
            "cus_abc123", 99.90m, "PIX", "Plano Starter - Mensal",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), null));

        charge.ExternalId.Should().Be("pay_pix001");
        charge.PixCopyPaste.Should().Contain("00020126");
    }

    [Fact]
    public void ValidateWebhook_WithCorrectToken_ShouldReturnTrue()
    {
        var gateway = new AsaasGateway(new HttpClient(), "sk_key", "meu_token_secreto", isSandbox: true);
        var body = Encoding.UTF8.GetBytes("""{"event":"PAYMENT_RECEIVED"}""");

        gateway.ValidateWebhook(body, "meu_token_secreto").Should().BeTrue();
        gateway.ValidateWebhook(body, "token_errado").Should().BeFalse();
    }

    [Fact]
    public void ParseWebhookEvent_PaymentReceived_ShouldReturnPaid()
    {
        var gateway = new AsaasGateway(new HttpClient(), "sk", "tok", isSandbox: true);
        var json = """{"event":"PAYMENT_RECEIVED","payment":{"id":"pay_001","status":"RECEIVED"}}""";

        var evt = gateway.ParseWebhookEvent(json);

        evt.Should().NotBeNull();
        evt!.ExternalId.Should().Be("pay_001");
        evt.IsPaid.Should().BeTrue();
        evt.IsOverdue.Should().BeFalse();
    }

    [Fact]
    public void ParseWebhookEvent_PaymentOverdue_ShouldReturnOverdue()
    {
        var gateway = new AsaasGateway(new HttpClient(), "sk", "tok", isSandbox: true);
        var json = """{"event":"PAYMENT_OVERDUE","payment":{"id":"pay_002","status":"OVERDUE"}}""";

        var evt = gateway.ParseWebhookEvent(json);

        evt!.IsOverdue.Should().BeTrue();
        evt.IsPaid.Should().BeFalse();
    }
}
```

- [ ] **Step 3: Rodar testes (esperar falha)**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~AsaasGatewayTests"
```

Expected: FAIL — `AsaasGateway` não existe

- [ ] **Step 4: Implementar AsaasGateway**

Criar `src/Atendefy.API/Modules/Billing/Gateways/AsaasGateway.cs`:

```csharp
using Atendefy.API.Modules.Billing.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace Atendefy.API.Modules.Billing.Gateways;

public class AsaasGateway(HttpClient httpClient, string apiKey, string webhookToken, bool isSandbox = false)
    : IBillingGateway
{
    private string BaseUrl => isSandbox
        ? "https://sandbox.asaas.com/api/v3"
        : "https://api.asaas.com/v3";

    private void SetAuth() => httpClient.DefaultRequestHeaders.Remove("access_token") switch
    {
        _ => httpClient.DefaultRequestHeaders.TryAddWithoutValidation("access_token", apiKey)
    };

    public async Task<string> CreateCustomerAsync(string name, string email, string cpfCnpj)
    {
        SetAuth();
        var payload = new { name, email, cpfCnpj };
        var response = await httpClient.PostAsJsonAsync($"{BaseUrl}/customers", payload);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetString()!;
    }

    public async Task<BillingCharge> CreateChargeAsync(CreateChargeArgs args)
    {
        SetAuth();
        var payload = new
        {
            customer = args.CustomerExternalId,
            billingType = args.BillingType,
            value = args.Amount,
            dueDate = args.DueDate.ToString("yyyy-MM-dd"),
            description = args.Description
        };
        var response = await httpClient.PostAsJsonAsync($"{BaseUrl}/payments", payload);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("id").GetString()!;

        string? boletoUrl = null, boletoBarcode = null, pixCopyPaste = null;

        if (json.TryGetProperty("bankSlipUrl", out var bsUrl))
            boletoUrl = bsUrl.GetString();

        if (json.TryGetProperty("identificationField", out var idField))
            boletoBarcode = idField.GetString();

        if (json.TryGetProperty("pixTransaction", out var pixTx) &&
            pixTx.TryGetProperty("qrCode", out var qr) &&
            qr.TryGetProperty("payload", out var pixPayload))
            pixCopyPaste = pixPayload.GetString();

        return new BillingCharge(id, boletoUrl, boletoBarcode, pixCopyPaste, ClientSecret: null);
    }

    public async Task CancelChargeAsync(string externalId)
    {
        SetAuth();
        var response = await httpClient.DeleteAsync($"{BaseUrl}/payments/{externalId}");
        response.EnsureSuccessStatusCode();
    }

    public bool ValidateWebhook(byte[] payload, string headerValue)
        => headerValue == webhookToken;

    public WebhookEvent? ParseWebhookEvent(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var evt = doc.RootElement.GetProperty("event").GetString() ?? "";
            var payment = doc.RootElement.GetProperty("payment");
            var id = payment.GetProperty("id").GetString()!;

            return new WebhookEvent(
                ExternalId: id,
                IsPaid: evt == "PAYMENT_RECEIVED",
                IsOverdue: evt == "PAYMENT_OVERDUE",
                IsCancelled: evt == "PAYMENT_DELETED"
            );
        }
        catch
        {
            return null;
        }
    }
}
```

> **Nota:** O método `SetAuth()` usa um pattern switch como workaround para chamar dois métodos em sequência em uma expressão. Se o compilador reclamar, substitua por:
> ```csharp
> private void SetAuth()
> {
>     httpClient.DefaultRequestHeaders.Remove("access_token");
>     httpClient.DefaultRequestHeaders.TryAddWithoutValidation("access_token", apiKey);
> }
> ```

- [ ] **Step 5: Criar BillingGatewayFactory**

Criar `src/Atendefy.API/Modules/Billing/Gateways/BillingGatewayFactory.cs`:

```csharp
namespace Atendefy.API.Modules.Billing.Gateways;

public class BillingGatewayFactory(
    IHttpClientFactory httpClientFactory,
    string asaasApiKey,
    string asaasWebhookToken,
    bool asaasSandbox,
    string stripeSecretKey,
    string stripeWebhookSecret)
{
    public IBillingGateway Create(string provider) => provider switch
    {
        "asaas" => new AsaasGateway(
            httpClientFactory.CreateClient("billing"),
            asaasApiKey, asaasWebhookToken, asaasSandbox),
        "stripe" => new StripeGateway(
            httpClientFactory.CreateClient("billing"),
            stripeSecretKey, stripeWebhookSecret),
        _ => throw new ArgumentException($"Billing provider desconhecido: {provider}")
    };
}
```

- [ ] **Step 6: Rodar testes**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~AsaasGatewayTests"
```

Expected: PASS — 6 testes passam

- [ ] **Step 7: Commit**

```bash
git add src/Atendefy.API/Modules/Billing/Gateways/
git add tests/Atendefy.Tests/Billing/AsaasGatewayTests.cs
git commit -m "feat: add IBillingGateway, AsaasGateway and BillingGatewayFactory"
```

---

## Task 3: StripeGateway

**Files:**
- Create: `src/Atendefy.API/Modules/Billing/Gateways/StripeGateway.cs`
- Create: `tests/Atendefy.Tests/Billing/StripeGatewayTests.cs`

**Sobre a API Stripe:**
- Base URL: `https://api.stripe.com/v1`
- Auth: `Authorization: Bearer sk_xxx`
- Content-Type: `application/x-www-form-urlencoded` (NÃO JSON!)
- Webhook signature header: `Stripe-Signature: t=timestamp,v1=hash`
- Validação: `HMAC-SHA256("{timestamp}.{payload}", signing_secret)`
- Eventos: `payment_intent.succeeded`, `payment_intent.payment_failed`
- Valor em centavos (BRL → multiply by 100)

- [ ] **Step 1: Escrever testes para StripeGateway**

Criar `tests/Atendefy.Tests/Billing/StripeGatewayTests.cs`:

```csharp
using Atendefy.API.Modules.Billing.Gateways;
using Atendefy.Tests.Helpers;
using FluentAssertions;
using System.Security.Cryptography;
using System.Text;

namespace Atendefy.Tests.Billing;

public class StripeGatewayTests
{
    [Fact]
    public async Task CreateCustomerAsync_ShouldPostFormEncodedAndReturnId()
    {
        var handler = MockHttpMessageHandler.ReturnsJson("""{"id":"cus_stripe123","email":"test@test.com"}""");
        var gateway = new StripeGateway(new HttpClient(handler), "sk_test_key", "whsec_test");

        var id = await gateway.CreateCustomerAsync("Test Corp", "test@test.com", "12345678000190");

        id.Should().Be("cus_stripe123");
        var req = handler.Requests[0];
        req.RequestUri!.ToString().Should().Contain("/v1/customers");
        req.Content!.Headers.ContentType!.MediaType.Should().Be("application/x-www-form-urlencoded");
        var body = await req.Content.ReadAsStringAsync();
        body.Should().Contain("name=Test+Corp").Or.Contain("name=Test%20Corp");
    }

    [Fact]
    public async Task CreateChargeAsync_ShouldCreatePaymentIntentAndReturnClientSecret()
    {
        var response = """{"id":"pi_abc","status":"requires_payment_method","client_secret":"pi_abc_secret_xyz"}""";
        var handler = MockHttpMessageHandler.ReturnsJson(response);
        var gateway = new StripeGateway(new HttpClient(handler), "sk_test_key", "whsec_test");

        var charge = await gateway.CreateChargeAsync(new CreateChargeArgs(
            "cus_stripe123", 99.90m, "CREDIT_CARD", "Plano Starter - Mensal",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), "pm_card_visa"));

        charge.ExternalId.Should().Be("pi_abc");
        charge.ClientSecret.Should().Be("pi_abc_secret_xyz");
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        body.Should().Contain("amount=9990");  // 99.90 * 100
        body.Should().Contain("currency=brl");
    }

    [Fact]
    public void ValidateWebhook_WithCorrectSignature_ShouldReturnTrue()
    {
        var secret = "whsec_testsecret";
        var gateway = new StripeGateway(new HttpClient(), "sk_test", secret);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var payload = """{"type":"payment_intent.succeeded"}""";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signedPayload = $"{timestamp}.{payload}";
        var hash = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signedPayload))).ToLower();
        var header = $"t={timestamp},v1={hash}";

        gateway.ValidateWebhook(payloadBytes, header).Should().BeTrue();
    }

    [Fact]
    public void ValidateWebhook_WithWrongSignature_ShouldReturnFalse()
    {
        var gateway = new StripeGateway(new HttpClient(), "sk_test", "whsec_real");
        var body = Encoding.UTF8.GetBytes("payload");
        gateway.ValidateWebhook(body, "t=12345,v1=invalidsignatureinvalidsignatureinvalidsig000000000000000").Should().BeFalse();
    }

    [Fact]
    public void ParseWebhookEvent_PaymentIntentSucceeded_ShouldReturnPaid()
    {
        var gateway = new StripeGateway(new HttpClient(), "sk_test", "whsec");
        var json = """{"type":"payment_intent.succeeded","data":{"object":{"id":"pi_001","status":"succeeded"}}}""";

        var evt = gateway.ParseWebhookEvent(json);

        evt!.ExternalId.Should().Be("pi_001");
        evt.IsPaid.Should().BeTrue();
        evt.IsOverdue.Should().BeFalse();
    }

    [Fact]
    public void ParseWebhookEvent_PaymentIntentFailed_ShouldReturnOverdue()
    {
        var gateway = new StripeGateway(new HttpClient(), "sk_test", "whsec");
        var json = """{"type":"payment_intent.payment_failed","data":{"object":{"id":"pi_002","status":"requires_payment_method"}}}""";

        var evt = gateway.ParseWebhookEvent(json);

        evt!.IsOverdue.Should().BeTrue();
        evt.IsPaid.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Rodar testes (esperar falha)**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~StripeGatewayTests"
```

Expected: FAIL — `StripeGateway` não existe

- [ ] **Step 3: Implementar StripeGateway**

Criar `src/Atendefy.API/Modules/Billing/Gateways/StripeGateway.cs`:

```csharp
using Atendefy.API.Modules.Billing.Models;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Atendefy.API.Modules.Billing.Gateways;

public class StripeGateway(HttpClient httpClient, string secretKey, string webhookSigningSecret)
    : IBillingGateway
{
    private const string BaseUrl = "https://api.stripe.com/v1";

    private void SetAuth()
    {
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", secretKey);
    }

    public async Task<string> CreateCustomerAsync(string name, string email, string cpfCnpj)
    {
        SetAuth();
        var form = new FormUrlEncodedContent([
            new("name", name),
            new("email", email),
            new("metadata[cpfCnpj]", cpfCnpj)
        ]);
        var response = await httpClient.PostAsync($"{BaseUrl}/customers", form);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetString()!;
    }

    public async Task<BillingCharge> CreateChargeAsync(CreateChargeArgs args)
    {
        SetAuth();
        var amountCents = (long)(args.Amount * 100);
        var fields = new List<KeyValuePair<string, string>>
        {
            new("amount", amountCents.ToString()),
            new("currency", "brl"),
            new("customer", args.CustomerExternalId),
            new("description", args.Description)
        };
        if (!string.IsNullOrEmpty(args.PaymentMethodId))
            fields.Add(new("payment_method", args.PaymentMethodId));

        var form = new FormUrlEncodedContent(fields);
        var response = await httpClient.PostAsync($"{BaseUrl}/payment_intents", form);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("id").GetString()!;
        var clientSecret = json.TryGetProperty("client_secret", out var cs) ? cs.GetString() : null;

        return new BillingCharge(id, BoletoUrl: null, BoletoBarcode: null, PixCopyPaste: null, ClientSecret: clientSecret);
    }

    public async Task CancelChargeAsync(string externalId)
    {
        SetAuth();
        var form = new FormUrlEncodedContent([]);
        var response = await httpClient.PostAsync($"{BaseUrl}/payment_intents/{externalId}/cancel", form);
        response.EnsureSuccessStatusCode();
    }

    public bool ValidateWebhook(byte[] payload, string headerValue)
    {
        // Header: "t=timestamp,v1=signature"
        try
        {
            var parts = headerValue.Split(',');
            var tPart = parts.FirstOrDefault(p => p.StartsWith("t="))?[2..];
            var v1Part = parts.FirstOrDefault(p => p.StartsWith("v1="))?[3..];
            if (tPart is null || v1Part is null) return false;

            var signedPayload = $"{tPart}.{Encoding.UTF8.GetString(payload)}";
            var expectedHash = Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(webhookSigningSecret),
                    Encoding.UTF8.GetBytes(signedPayload))).ToLower();

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(v1Part.ToLower()),
                Encoding.UTF8.GetBytes(expectedHash));
        }
        catch
        {
            return false;
        }
    }

    public WebhookEvent? ParseWebhookEvent(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var type = doc.RootElement.GetProperty("type").GetString() ?? "";
            var obj = doc.RootElement.GetProperty("data").GetProperty("object");
            var id = obj.GetProperty("id").GetString()!;

            return new WebhookEvent(
                ExternalId: id,
                IsPaid: type == "payment_intent.succeeded",
                IsOverdue: type == "payment_intent.payment_failed",
                IsCancelled: type == "payment_intent.canceled"
            );
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Rodar testes**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~StripeGatewayTests"
```

Expected: PASS — 5 testes passam

- [ ] **Step 5: Commit**

```bash
git add src/Atendefy.API/Modules/Billing/Gateways/StripeGateway.cs
git add tests/Atendefy.Tests/Billing/StripeGatewayTests.cs
git commit -m "feat: add StripeGateway with Payment Intents and webhook signature validation"
```

---

## Task 4: BillingService

**Files:**
- Create: `src/Atendefy.API/Modules/Billing/BillingService.cs`
- Create: `tests/Atendefy.Tests/Billing/BillingServiceTests.cs`

- [ ] **Step 1: Escrever testes para BillingService**

Criar `tests/Atendefy.Tests/Billing/BillingServiceTests.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Billing;
using Atendefy.API.Modules.Billing.Gateways;
using Atendefy.API.Modules.Billing.Models;
using Atendefy.API.Modules.Tenants.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Atendefy.Tests.Billing;

public class BillingServiceTests
{
    private static PublicDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<PublicDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PublicDbContext(opts);
    }

    [Fact]
    public async Task SubscribeAsync_WithValidPlan_ShouldCreateSubscriptionAndInvoice()
    {
        var db = CreateDb();
        var plan = new Plan { Name = "Starter", PriceMonthly = 99.90m, PriceYearly = 999m, LimitsJson = "{}" };
        var tenant = new Tenant { Name = "Empresa ABC", Subdomain = "empresa-abc" };
        db.Plans.Add(plan);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var gateway = Substitute.For<IBillingGateway>();
        gateway.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
               .Returns("cus_ext_001");
        gateway.CreateChargeAsync(Arg.Any<CreateChargeArgs>())
               .Returns(new BillingCharge("pay_ext_001", "https://boleto.url", "1234.5678", null, null));

        var gatewayFactory = Substitute.For<BillingGatewayFactory>(null!, "", "", false, "", "");
        gatewayFactory.Create("asaas").Returns(gateway);

        var svc = new BillingService(db, gatewayFactory);
        var request = new CreateSubscriptionRequest(plan.Id, "asaas", "BOLETO", "monthly", "12345678000190", null);

        var result = await svc.SubscribeAsync(tenant.Id, tenant.Name, "cto@empresa.com", request);

        result.IsSuccess.Should().BeTrue();
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.TenantId == tenant.Id);
        sub.Should().NotBeNull();
        sub!.Status.Should().Be("pending");
        sub.ExternalCustomerId.Should().Be("cus_ext_001");

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.SubscriptionId == sub.Id);
        invoice.Should().NotBeNull();
        invoice!.Status.Should().Be("pending");
        invoice.ExternalId.Should().Be("pay_ext_001");
        invoice.BoletoUrl.Should().Be("https://boleto.url");
    }

    [Fact]
    public async Task ProcessPaymentEventAsync_WhenPaid_ShouldActivateSubscription()
    {
        var db = CreateDb();
        var plan = new Plan { Name = "Pro", PriceMonthly = 199m, PriceYearly = 1990m, LimitsJson = "{}" };
        var tenant = new Tenant { Name = "Empresa XYZ", Subdomain = "empresa-xyz" };
        db.Plans.Add(plan);
        db.Tenants.Add(tenant);

        var sub = new Subscription
        {
            TenantId = tenant.Id,
            PlanId = plan.Id,
            Status = "pending",
            Provider = "asaas",
            BillingCycle = "monthly",
            ExternalCustomerId = "cus_ext_002"
        };
        db.Subscriptions.Add(sub);

        var invoice = new Invoice
        {
            SubscriptionId = sub.Id,
            TenantId = tenant.Id,
            Amount = 199m,
            Status = "pending",
            Provider = "asaas",
            BillingType = "BOLETO",
            ExternalId = "pay_ext_002",
            DueDate = DateTime.UtcNow.AddDays(3)
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var svc = new BillingService(db, Substitute.For<BillingGatewayFactory>(null!, "", "", false, "", ""));
        await svc.ProcessPaymentEventAsync(new WebhookEvent("pay_ext_002", IsPaid: true, IsOverdue: false, IsCancelled: false));

        var updatedInvoice = await db.Invoices.FindAsync(invoice.Id);
        updatedInvoice!.Status.Should().Be("paid");
        updatedInvoice.PaidAt.Should().NotBeNull();

        var updatedSub = await db.Subscriptions.FindAsync(sub.Id);
        updatedSub!.Status.Should().Be("active");

        var updatedTenant = await db.Tenants.FindAsync(tenant.Id);
        updatedTenant!.PlanId.Should().Be(plan.Id);
    }

    [Fact]
    public async Task ProcessPaymentEventAsync_WhenOverdue_ShouldMarkInvoiceOverdue()
    {
        var db = CreateDb();
        var tenant = new Tenant { Name = "Empresa Late", Subdomain = "empresa-late" };
        var plan = new Plan { Name = "Starter", PriceMonthly = 99m, LimitsJson = "{}" };
        db.Tenants.Add(tenant);
        db.Plans.Add(plan);

        var sub = new Subscription
        {
            TenantId = tenant.Id, PlanId = plan.Id,
            Status = "active", Provider = "asaas", BillingCycle = "monthly"
        };
        db.Subscriptions.Add(sub);

        var invoice = new Invoice
        {
            SubscriptionId = sub.Id, TenantId = tenant.Id, Amount = 99m,
            Status = "pending", Provider = "asaas", BillingType = "BOLETO",
            ExternalId = "pay_ext_late", DueDate = DateTime.UtcNow.AddDays(-1)
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var svc = new BillingService(db, Substitute.For<BillingGatewayFactory>(null!, "", "", false, "", ""));
        await svc.ProcessPaymentEventAsync(new WebhookEvent("pay_ext_late", false, IsOverdue: true, false));

        var updatedInvoice = await db.Invoices.FindAsync(invoice.Id);
        updatedInvoice!.Status.Should().Be("overdue");

        var updatedSub = await db.Subscriptions.FindAsync(sub.Id);
        updatedSub!.Status.Should().Be("past_due");
    }
}
```

> **Nota:** `BillingGatewayFactory` tem parâmetros no construtor. Para NSubstitute.For<> funcionar, a classe precisa ser não-selada e seus métodos virtuais. Se o teste falhar por isso, use uma interface `IBillingGatewayFactory` ou injete o gateway diretamente via um overload do construtor de `BillingService`. Veja a implementação abaixo — o construtor aceita `IBillingGatewayFactory`.

- [ ] **Step 2: Rodar testes (esperar falha)**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~BillingServiceTests"
```

Expected: FAIL

- [ ] **Step 3: Implementar BillingService**

Criar `src/Atendefy.API/Modules/Billing/BillingService.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Billing.Gateways;
using Atendefy.API.Modules.Billing.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Billing;

public interface IBillingGatewayFactory
{
    IBillingGateway Create(string provider);
}

public class BillingService(PublicDbContext db, IBillingGatewayFactory gatewayFactory)
{
    private static readonly HashSet<string> ValidProviders = ["asaas", "stripe"];
    private static readonly HashSet<string> ValidCycles = ["monthly", "yearly"];

    public async Task<Result<Invoice>> SubscribeAsync(
        Guid tenantId, string tenantName, string email, CreateSubscriptionRequest request)
    {
        if (!ValidProviders.Contains(request.Provider))
            return Result<Invoice>.Fail("Provider inválido. Use 'asaas' ou 'stripe'.");
        if (!ValidCycles.Contains(request.BillingCycle))
            return Result<Invoice>.Fail("Ciclo inválido. Use 'monthly' ou 'yearly'.");

        var plan = await db.Plans.FindAsync(request.PlanId);
        if (plan is null) return Result<Invoice>.Fail("Plano não encontrado.");

        var gateway = gatewayFactory.Create(request.Provider);
        var customerId = await gateway.CreateCustomerAsync(tenantName, email, request.CpfCnpj);

        var amount = request.BillingCycle == "yearly" ? plan.PriceYearly : plan.PriceMonthly;
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var description = $"{plan.Name} - {(request.BillingCycle == "yearly" ? "Anual" : "Mensal")}";

        var charge = await gateway.CreateChargeAsync(new CreateChargeArgs(
            customerId, amount, request.BillingType, description, dueDate, request.PaymentMethodId));

        var now = DateTime.UtcNow;
        var subscription = new Subscription
        {
            TenantId = tenantId,
            PlanId = plan.Id,
            Status = "pending",
            BillingCycle = request.BillingCycle,
            Provider = request.Provider,
            ExternalCustomerId = customerId,
            ExternalId = charge.ExternalId,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = request.BillingCycle == "yearly"
                ? now.AddYears(1) : now.AddMonths(1)
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            SubscriptionId = subscription.Id,
            TenantId = tenantId,
            Amount = amount,
            Status = "pending",
            Provider = request.Provider,
            BillingType = request.BillingType,
            ExternalId = charge.ExternalId,
            DueDate = dueDate.ToDateTime(TimeOnly.MinValue),
            BoletoUrl = charge.BoletoUrl,
            BoletoBarcode = charge.BoletoBarcode,
            PixCopyPaste = charge.PixCopyPaste,
            ClientSecret = charge.ClientSecret
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        return Result<Invoice>.Ok(invoice);
    }

    public async Task ProcessPaymentEventAsync(WebhookEvent evt)
    {
        var invoice = await db.Invoices
            .FirstOrDefaultAsync(i => i.ExternalId == evt.ExternalId);
        if (invoice is null) return;

        var subscription = await db.Subscriptions.FindAsync(invoice.SubscriptionId);
        if (subscription is null) return;

        if (evt.IsPaid)
        {
            invoice.Status = "paid";
            invoice.PaidAt = DateTime.UtcNow;
            subscription.Status = "active";

            var tenant = await db.Tenants.FindAsync(subscription.TenantId);
            if (tenant is not null)
            {
                tenant.PlanId = subscription.PlanId;
                tenant.Status = "active";
                tenant.UpdatedAt = DateTime.UtcNow;
            }
        }
        else if (evt.IsOverdue)
        {
            invoice.Status = "overdue";
            subscription.Status = "past_due";
        }
        else if (evt.IsCancelled)
        {
            invoice.Status = "cancelled";
            subscription.Status = "cancelled";
        }

        invoice.UpdatedAt = DateTime.UtcNow;
        subscription.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<Result> CancelAsync(Guid tenantId)
    {
        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Status != "cancelled");
        if (subscription is null) return Result.Fail("Assinatura ativa não encontrada.");

        if (!string.IsNullOrEmpty(subscription.ExternalId))
        {
            var gateway = gatewayFactory.Create(subscription.Provider);
            await gateway.CancelChargeAsync(subscription.ExternalId);
        }

        subscription.Status = "cancelled";
        subscription.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Result.Ok();
    }
}
```

Também adicionar a implementação da factory ao `BillingGatewayFactory` — ela deve implementar `IBillingGatewayFactory`. Modificar `src/Atendefy.API/Modules/Billing/Gateways/BillingGatewayFactory.cs`:

```csharp
namespace Atendefy.API.Modules.Billing.Gateways;

public class BillingGatewayFactory(
    IHttpClientFactory httpClientFactory,
    string asaasApiKey,
    string asaasWebhookToken,
    bool asaasSandbox,
    string stripeSecretKey,
    string stripeWebhookSecret) : IBillingGatewayFactory
{
    public IBillingGateway Create(string provider) => provider switch
    {
        "asaas" => new AsaasGateway(
            httpClientFactory.CreateClient("billing"),
            asaasApiKey, asaasWebhookToken, asaasSandbox),
        "stripe" => new StripeGateway(
            httpClientFactory.CreateClient("billing"),
            stripeSecretKey, stripeWebhookSecret),
        _ => throw new ArgumentException($"Billing provider desconhecido: {provider}")
    };
}
```

Atualizar os testes para usar `IBillingGatewayFactory`:

```csharp
// Substituir no BillingServiceTests:
var gatewayFactory = Substitute.For<IBillingGatewayFactory>();
gatewayFactory.Create("asaas").Returns(gateway);

var svc = new BillingService(db, gatewayFactory);
```

- [ ] **Step 4: Rodar testes**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~BillingServiceTests"
```

Expected: PASS — 3 testes passam

- [ ] **Step 5: Build completo**

```bash
dotnet build Atendefy.slnx
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/Atendefy.API/Modules/Billing/BillingService.cs
git add src/Atendefy.API/Modules/Billing/Gateways/BillingGatewayFactory.cs
git add tests/Atendefy.Tests/Billing/BillingServiceTests.cs
git commit -m "feat: add BillingService with subscribe, processPaymentEvent and cancel"
```

---

## Task 5: Billing Endpoints

**Files:**
- Create: `src/Atendefy.API/Modules/Billing/BillingEndpoints.cs`

- [ ] **Step 1: Criar BillingEndpoints**

Criar `src/Atendefy.API/Modules/Billing/BillingEndpoints.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Billing.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Billing;

public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/billing").WithTags("Billing");

        // Listar planos disponíveis (público)
        group.MapGet("/plans", async (PublicDbContext db) =>
        {
            var plans = await db.Plans
                .Where(p => p.IsActive)
                .Select(p => new
                {
                    p.Id, p.Name, p.PriceMonthly, p.PriceYearly, p.LimitsJson
                })
                .ToListAsync();
            return Results.Ok(plans);
        });

        // Assinar um plano
        group.MapPost("/subscribe", async (
            [FromBody] CreateSubscriptionRequest request,
            BillingService billingService,
            PublicDbContext db,
            HttpContext ctx) =>
        {
            var (tenantId, tenant, error) = await ResolveTenantAsync(ctx, db);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var result = await billingService.SubscribeAsync(
                tenantId, tenant!.Name, ctx.User.FindFirst("email")?.Value ?? "", request);

            return result.IsSuccess
                ? Results.Ok(new
                {
                    result.Value!.Id,
                    result.Value.Status,
                    result.Value.BoletoUrl,
                    result.Value.BoletoBarcode,
                    result.Value.PixCopyPaste,
                    result.Value.ClientSecret,
                    result.Value.DueDate
                })
                : Results.BadRequest(new { error = result.Error });
        }).RequireAuthorization();

        // Consultar assinatura atual
        group.MapGet("/subscription", async (
            PublicDbContext db,
            HttpContext ctx) =>
        {
            var (tenantId, _, error) = await ResolveTenantAsync(ctx, db);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var sub = await db.Subscriptions
                .Where(s => s.TenantId == tenantId && s.Status != "cancelled")
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (sub is null) return Results.NotFound(new { error = "Nenhuma assinatura ativa encontrada" });

            var plan = await db.Plans.FindAsync(sub.PlanId);
            var lastInvoice = await db.Invoices
                .Where(i => i.SubscriptionId == sub.Id)
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefaultAsync();

            return Results.Ok(new
            {
                sub.Id, sub.Status, sub.BillingCycle, sub.Provider,
                sub.CurrentPeriodStart, sub.CurrentPeriodEnd,
                Plan = plan is null ? null : new { plan.Id, plan.Name },
                LastInvoice = lastInvoice is null ? null : new
                {
                    lastInvoice.Id, lastInvoice.Status, lastInvoice.Amount,
                    lastInvoice.DueDate, lastInvoice.PaidAt
                }
            });
        }).RequireAuthorization();

        // Cancelar assinatura
        group.MapDelete("/subscription", async (
            BillingService billingService,
            PublicDbContext db,
            HttpContext ctx) =>
        {
            var (tenantId, _, error) = await ResolveTenantAsync(ctx, db);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var result = await billingService.CancelAsync(tenantId);
            return result.IsSuccess
                ? Results.Ok(new { message = "Assinatura cancelada com sucesso" })
                : Results.BadRequest(new { error = result.Error });
        }).RequireAuthorization();

        return app;
    }

    private static async Task<(Guid TenantId, Atendefy.API.Modules.Tenants.Models.Tenant? Tenant, string? Error)>
        ResolveTenantAsync(HttpContext ctx, PublicDbContext db)
    {
        var tenantIdStr = ctx.User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            return (Guid.Empty, null, "Token inválido");

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        return tenant is null
            ? (Guid.Empty, null, "Tenant não encontrado")
            : (tenantId, tenant, null);
    }
}
```

- [ ] **Step 2: Verificar build**

```bash
dotnet build Atendefy.slnx
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/Atendefy.API/Modules/Billing/BillingEndpoints.cs
git commit -m "feat: add billing endpoints (plans, subscribe, subscription)"
```

---

## Task 6: Billing Webhook Endpoints

**Files:**
- Create: `src/Atendefy.API/Modules/Billing/BillingWebhookEndpoints.cs`

- [ ] **Step 1: Criar BillingWebhookEndpoints**

Criar `src/Atendefy.API/Modules/Billing/BillingWebhookEndpoints.cs`:

```csharp
using Atendefy.API.Modules.Billing.Gateways;

namespace Atendefy.API.Modules.Billing;

public static class BillingWebhookEndpoints
{
    public static IEndpointRouteBuilder MapBillingWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/billing/webhooks").WithTags("Billing");

        // Asaas: validação via header "asaas-access-token"
        group.MapPost("/asaas", async (
            HttpContext ctx,
            BillingService billingService,
            IBillingGatewayFactory gatewayFactory) =>
        {
            ctx.Request.EnableBuffering();
            var body = await ReadBytesAsync(ctx.Request.Body);
            ctx.Request.Body.Position = 0;

            var token = ctx.Request.Headers["asaas-access-token"].ToString();
            var gateway = gatewayFactory.Create("asaas");
            if (!gateway.ValidateWebhook(body, token))
                return Results.Forbid();

            var json = System.Text.Encoding.UTF8.GetString(body);
            var evt = gateway.ParseWebhookEvent(json);
            if (evt is not null)
                await billingService.ProcessPaymentEventAsync(evt);

            return Results.Ok();
        });

        // Stripe: validação via header "Stripe-Signature"
        group.MapPost("/stripe", async (
            HttpContext ctx,
            BillingService billingService,
            IBillingGatewayFactory gatewayFactory) =>
        {
            ctx.Request.EnableBuffering();
            var body = await ReadBytesAsync(ctx.Request.Body);
            ctx.Request.Body.Position = 0;

            var signature = ctx.Request.Headers["Stripe-Signature"].ToString();
            var gateway = gatewayFactory.Create("stripe");
            if (!gateway.ValidateWebhook(body, signature))
                return Results.Forbid();

            var json = System.Text.Encoding.UTF8.GetString(body);
            var evt = gateway.ParseWebhookEvent(json);
            if (evt is not null)
                await billingService.ProcessPaymentEventAsync(evt);

            return Results.Ok();
        });

        return app;
    }

    private static async Task<byte[]> ReadBytesAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }
}
```

- [ ] **Step 2: Verificar build**

```bash
dotnet build Atendefy.slnx
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/Atendefy.API/Modules/Billing/BillingWebhookEndpoints.cs
git commit -m "feat: add billing webhook endpoints for Asaas and Stripe"
```

---

## Task 7: SuspensionWorker

**Files:**
- Create: `src/Atendefy.API/Modules/Billing/SuspensionWorker.cs`
- Create: `tests/Atendefy.Tests/Billing/SuspensionWorkerTests.cs`

**Lógica:** Roda uma vez ao dia. Busca invoices com `status = "overdue"` e `due_date < hoje - 3 dias` (grace period). Suspende os tenants correspondentes que ainda estão com `status = "active"`.

- [ ] **Step 1: Escrever testes para SuspensionWorker**

Criar `tests/Atendefy.Tests/Billing/SuspensionWorkerTests.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Billing;
using Atendefy.API.Modules.Billing.Models;
using Atendefy.API.Modules.Tenants.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atendefy.Tests.Billing;

public class SuspensionWorkerTests
{
    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<PublicDbContext>(opt =>
            opt.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunAsync_WhenInvoiceOverduePastGracePeriod_ShouldSuspendTenant()
    {
        var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PublicDbContext>();

        var tenant = new Tenant { Name = "Inadimplente", Subdomain = "inadimplente", Status = "active" };
        var plan = new Plan { Name = "Starter", PriceMonthly = 99m, LimitsJson = "{}" };
        db.Tenants.Add(tenant);
        db.Plans.Add(plan);

        var sub = new Subscription
        {
            TenantId = tenant.Id, PlanId = plan.Id,
            Status = "past_due", Provider = "asaas", BillingCycle = "monthly"
        };
        db.Subscriptions.Add(sub);

        db.Invoices.Add(new Invoice
        {
            SubscriptionId = sub.Id, TenantId = tenant.Id, Amount = 99m,
            Status = "overdue", Provider = "asaas", BillingType = "BOLETO",
            DueDate = DateTime.UtcNow.AddDays(-4)  // past grace period of 3 days
        });
        await db.SaveChangesAsync();

        var worker = new SuspensionWorker(sp, NullLogger<SuspensionWorker>.Instance);
        await worker.RunOnceAsync();

        var updated = await db.Tenants.FindAsync(tenant.Id);
        updated!.Status.Should().Be("suspended");
    }

    [Fact]
    public async Task RunAsync_WhenInvoiceOverdueWithinGracePeriod_ShouldNotSuspend()
    {
        var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PublicDbContext>();

        var tenant = new Tenant { Name = "Ainda no Prazo", Subdomain = "prazo", Status = "active" };
        var plan = new Plan { Name = "Pro", PriceMonthly = 199m, LimitsJson = "{}" };
        db.Tenants.Add(tenant);
        db.Plans.Add(plan);

        var sub = new Subscription
        {
            TenantId = tenant.Id, PlanId = plan.Id,
            Status = "past_due", Provider = "asaas", BillingCycle = "monthly"
        };
        db.Subscriptions.Add(sub);

        db.Invoices.Add(new Invoice
        {
            SubscriptionId = sub.Id, TenantId = tenant.Id, Amount = 199m,
            Status = "overdue", Provider = "asaas", BillingType = "BOLETO",
            DueDate = DateTime.UtcNow.AddDays(-1)  // still within grace period
        });
        await db.SaveChangesAsync();

        var worker = new SuspensionWorker(sp, NullLogger<SuspensionWorker>.Instance);
        await worker.RunOnceAsync();

        var updated = await db.Tenants.FindAsync(tenant.Id);
        updated!.Status.Should().Be("active");  // not suspended yet
    }

    [Fact]
    public async Task RunAsync_WhenTenantAlreadySuspended_ShouldNotChangeStatus()
    {
        var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PublicDbContext>();

        var tenant = new Tenant { Name = "Já Suspenso", Subdomain = "ja-suspenso", Status = "suspended" };
        var plan = new Plan { Name = "Starter", PriceMonthly = 99m, LimitsJson = "{}" };
        db.Tenants.Add(tenant);
        db.Plans.Add(plan);

        var sub = new Subscription
        {
            TenantId = tenant.Id, PlanId = plan.Id,
            Status = "past_due", Provider = "asaas", BillingCycle = "monthly"
        };
        db.Subscriptions.Add(sub);

        db.Invoices.Add(new Invoice
        {
            SubscriptionId = sub.Id, TenantId = tenant.Id, Amount = 99m,
            Status = "overdue", Provider = "asaas", BillingType = "BOLETO",
            DueDate = DateTime.UtcNow.AddDays(-10)
        });
        await db.SaveChangesAsync();

        var worker = new SuspensionWorker(sp, NullLogger<SuspensionWorker>.Instance);
        await worker.RunOnceAsync();

        var updated = await db.Tenants.FindAsync(tenant.Id);
        updated!.Status.Should().Be("suspended");  // unchanged
        updated.UpdatedAt.Should().BeNull();        // not touched
    }
}
```

- [ ] **Step 2: Rodar testes (esperar falha)**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~SuspensionWorkerTests"
```

Expected: FAIL

- [ ] **Step 3: Implementar SuspensionWorker**

Criar `src/Atendefy.API/Modules/Billing/SuspensionWorker.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atendefy.API.Modules.Billing;

public class SuspensionWorker(IServiceProvider serviceProvider, ILogger<SuspensionWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync();
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    public async Task RunOnceAsync()
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PublicDbContext>();

        var gracePeriodCutoff = DateTime.UtcNow.AddDays(-3);

        var overdueTenantsIds = await db.Invoices
            .Where(i => i.Status == "overdue" && i.DueDate < gracePeriodCutoff)
            .Select(i => i.TenantId)
            .Distinct()
            .ToListAsync();

        if (overdueTenantsIds.Count == 0) return;

        var tenantsToSuspend = await db.Tenants
            .Where(t => overdueTenantsIds.Contains(t.Id) && t.Status == "active")
            .ToListAsync();

        foreach (var tenant in tenantsToSuspend)
        {
            tenant.Status = "suspended";
            tenant.UpdatedAt = DateTime.UtcNow;
            logger.LogWarning("Tenant {TenantId} ({Name}) suspenso por inadimplência", tenant.Id, tenant.Name);
        }

        if (tenantsToSuspend.Count > 0)
            await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Rodar testes**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~SuspensionWorkerTests"
```

Expected: PASS — 3 testes passam

- [ ] **Step 5: Commit**

```bash
git add src/Atendefy.API/Modules/Billing/SuspensionWorker.cs
git add tests/Atendefy.Tests/Billing/SuspensionWorkerTests.cs
git commit -m "feat: add SuspensionWorker to auto-suspend tenants with overdue invoices past grace period"
```

---

## Task 8: Wire up Program.cs

**Files:**
- Modify: `src/Atendefy.API/Program.cs`
- Modify: `src/Atendefy.API/appsettings.json`
- Modify: `src/Atendefy.API/appsettings.Development.json`

**Novas configurações necessárias:**
- `Asaas:ApiKey` — chave da API Asaas
- `Asaas:WebhookToken` — token de validação dos webhooks Asaas
- `Asaas:Sandbox` — `true` em desenvolvimento, `false` em produção
- `Stripe:SecretKey` — `sk_test_xxx` em dev, `sk_live_xxx` em produção
- `Stripe:WebhookSecret` — `whsec_xxx` para validação de webhooks

- [ ] **Step 1: Atualizar appsettings.json**

Adicionar ao `src/Atendefy.API/appsettings.json` (manter tudo que já existe, adicionar seções):

```json
{
  "Asaas": {
    "ApiKey": "",
    "WebhookToken": "",
    "Sandbox": true
  },
  "Stripe": {
    "SecretKey": "",
    "WebhookSecret": ""
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
  },
  "Asaas": {
    "ApiKey": "dev_asaas_sandbox_key",
    "WebhookToken": "dev_asaas_webhook_token",
    "Sandbox": true
  },
  "Stripe": {
    "SecretKey": "sk_test_placeholder",
    "WebhookSecret": "whsec_placeholder"
  }
}
```

- [ ] **Step 3: Atualizar Program.cs**

Adicionar estas linhas ao `src/Atendefy.API/Program.cs` (inserir APÓS o bloco `// Webhooks` existente e ANTES do bloco `// Chatbot`):

Primeiro, adicionar os usings no topo (após os existentes):
```csharp
using Atendefy.API.Modules.Billing;
using Atendefy.API.Modules.Billing.Gateways;
```

Depois, adicionar as variáveis de config logo após `var rateLimit = ...`:
```csharp
var asaasKey        = builder.Configuration["Asaas:ApiKey"] ?? string.Empty;
var asaasWebhook    = builder.Configuration["Asaas:WebhookToken"] ?? string.Empty;
var asaasSandbox    = builder.Configuration.GetValue<bool>("Asaas:Sandbox", true);
var stripeKey       = builder.Configuration["Stripe:SecretKey"] ?? string.Empty;
var stripeWebhook   = builder.Configuration["Stripe:WebhookSecret"] ?? string.Empty;
```

Depois, adicionar o bloco de serviços de billing ANTES do bloco `// Chatbot`:
```csharp
// Billing
builder.Services.AddSingleton<IBillingGatewayFactory>(sp =>
    new BillingGatewayFactory(
        sp.GetRequiredService<IHttpClientFactory>(),
        asaasKey, asaasWebhook, asaasSandbox,
        stripeKey, stripeWebhook));
builder.Services.AddScoped<BillingService>();
builder.Services.AddHostedService<SuspensionWorker>();
```

Adicionar o mapeamento de endpoints após `app.MapWebhookEndpoints()`:
```csharp
app.MapBillingEndpoints();
app.MapBillingWebhookEndpoints();
```

- [ ] **Step 4: Rodar todos os testes**

```bash
dotnet test Atendefy.slnx
```

Expected: todos os testes passam (39 existentes + novos de billing)

- [ ] **Step 5: Build e smoke test no Docker**

```bash
cd infra
docker compose build atendefy-api
docker compose up -d
```

Aguardar ~10 segundos e validar:
```bash
curl http://localhost:8080/health
# Expected: {"status":"healthy","timestamp":"..."}

curl http://localhost:8080/billing/plans
# Expected: [] (lista vazia — nenhum plano cadastrado ainda)
```

- [ ] **Step 6: Commit**

```bash
git add src/Atendefy.API/Program.cs
git add src/Atendefy.API/appsettings*.json
git commit -m "feat: wire up billing services, endpoints and SuspensionWorker in Program.cs"
```

---

## Verificação Final do Plano 3

Após completar as 8 tasks, verificar:

- [ ] `dotnet test Atendefy.slnx` — todos os testes passam
- [ ] `GET /billing/plans` → retorna lista de planos (pode estar vazia)
- [ ] `POST /billing/subscribe` (com JWT válido) → cria Subscription + Invoice, retorna dados de pagamento
- [ ] `GET /billing/subscription` (com JWT válido) → retorna assinatura ativa do tenant
- [ ] `DELETE /billing/subscription` (com JWT válido) → cancela assinatura
- [ ] `POST /billing/webhooks/asaas` (com token correto) → processa evento, retorna 200
- [ ] `POST /billing/webhooks/asaas` (com token errado) → retorna 403
- [ ] `POST /billing/webhooks/stripe` (com assinatura HMAC válida) → processa evento, retorna 200
- [ ] SuspensionWorker: tenant com invoice overdue há 4 dias fica com `status = "suspended"`
- [ ] Docker rodando sem crash

---

## Próximos Planos

**Plano 4 — Frontend React**
- Projeto React + Vite + TypeScript + shadcn/ui
- Telas: Dashboard, WhatsApp, Chatbot (editor de prompt), IA, Conversas, Billing
- Painel Super Admin
- Onboarding wizard
