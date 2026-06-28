# Plano 004: Tornar o processamento de webhooks de cobrança idempotente

> **Instruções para o executor**: Siga este plano passo a passo. Execute cada verificação antes
> de avançar. Em condições de PARE, pare e reporte — não improvise.
>
> **Verificação de drift (execute primeiro)**:
> `git diff --stat e805859..HEAD -- src/Atendefy.API/Modules/Billing/BillingService.cs`
> Em caso de divergência com os trechos abaixo, trate como PARE.

## Status

- **Prioridade**: P1
- **Esforço**: M (Médio — ~1 dia incluindo testes)
- **Risco**: MÉDIO
- **Depende de**: nenhum
- **Categoria**: correção de bug
- **Escrito no commit**: `e805859`, 2026-06-28

## Por que isso importa

`BillingService.ProcessPaymentEventAsync` é chamado quando Stripe ou Asaas envia um webhook de
cobrança (pago, vencido, cancelado). Ambos os provedores re-enviam webhooks automaticamente se
não receberem confirmação rápida. O método atual não verifica o estado atual antes de atualizar:

- Se um webhook `payment_paid` for reenviado, ele atualiza `invoice.PaidAt = DateTime.UtcNow`
  com um timestamp diferente, o que é inofensivo mas gera ruído.
- **O caso crítico:** se um webhook `payment_overdue` chegar *após* um `payment_paid` (por
  reordenação de replay), ele rebaixa `subscription.Status` de `Active` para `PastDue`, o que
  **suspende o acesso de um cliente que já pagou**. Isso é possível pois webhooks de cobrança
  não garantem entrega ordenada.

O fix é adicionar guards de estado antes de cada mutação: não processar um evento se o estado
atual da fatura já reflete o resultado desse evento ou um estado mais avançado.

## Estado atual

**`src/Atendefy.API/Modules/Billing/BillingService.cs` — linhas 72-109:**
```csharp
public async Task ProcessPaymentEventAsync(WebhookEvent evt)
{
    var invoice = await db.Invoices
        .FirstOrDefaultAsync(i => i.ExternalId == evt.ExternalId);
    if (invoice is null) return;

    var subscription = await db.Subscriptions.FindAsync(invoice.SubscriptionId);
    if (subscription is null) return;

    if (evt.IsPaid)
    {
        invoice.Status = AppConstants.InvoiceStatus.Paid;
        invoice.PaidAt = DateTime.UtcNow;
        subscription.Status = AppConstants.SubscriptionStatus.Active;

        var tenant = await db.Tenants.FindAsync(subscription.TenantId);
        if (tenant is not null)
        {
            tenant.PlanId = subscription.PlanId;
            tenant.Status = AppConstants.TenantStatus.Active;
            tenant.UpdatedAt = DateTime.UtcNow;
        }
    }
    else if (evt.IsOverdue)
    {
        invoice.Status = AppConstants.InvoiceStatus.Overdue;
        subscription.Status = AppConstants.SubscriptionStatus.PastDue;
    }
    else if (evt.IsCancelled)
    {
        invoice.Status = AppConstants.InvoiceStatus.Cancelled;
        subscription.Status = AppConstants.SubscriptionStatus.Cancelled;
    }

    invoice.UpdatedAt = DateTime.UtcNow;
    subscription.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
}
```

**Constantes relevantes** (`AppConstants`):
- `InvoiceStatus.Paid`, `.Overdue`, `.Cancelled`, `.Pending`
- `SubscriptionStatus.Active`, `.PastDue`, `.Cancelled`, `.Pending`
- `TenantStatus.Active`

**Testes existentes:** `tests/Atendefy.Tests/Billing/BillingServiceTests.cs` — use como padrão
para os novos testes (InMemoryDbContext, BillingService criado com `new`, asserts via
`FluentAssertions`).

## Comandos necessários

| Propósito     | Comando                                                            | Esperado     |
|---------------|--------------------------------------------------------------------|--------------|
| Build         | `dotnet build Atendefy.slnx -c Release --no-restore`               | exit 0       |
| Testes Billing | `dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~Billing"` | todos passam |
| Todos testes  | `dotnet test Atendefy.slnx -c Release`                             | todos passam |

## Escopo

**Em escopo**:
- `src/Atendefy.API/Modules/Billing/BillingService.cs`
- `tests/Atendefy.Tests/Billing/BillingServiceTests.cs`

**Fora do escopo** (não tocar):
- Models de `Invoice`, `Subscription`, `Tenant` — sem mudança de schema
- Endpoints de webhook de billing — a lógica de guard fica no serviço
- `WebhookEvent` record — não muda

## Git workflow

- Branch: `advisor/004-billing-webhook-idempotency`
- Commits: um para o service, um para os testes
- Mensagem: `fix(billing): tornar ProcessPaymentEventAsync idempotente`

## Passos

### Passo 1: Adicionar guards de estado em `ProcessPaymentEventAsync`

Substitua o método `ProcessPaymentEventAsync` completo pela versão com guards:

```csharp
public async Task ProcessPaymentEventAsync(WebhookEvent evt)
{
    var invoice = await db.Invoices
        .FirstOrDefaultAsync(i => i.ExternalId == evt.ExternalId);
    if (invoice is null) return;

    var subscription = await db.Subscriptions.FindAsync(invoice.SubscriptionId);
    if (subscription is null) return;

    if (evt.IsPaid)
    {
        // Guard: já foi processado como pago; replay seguro de ignorar
        if (invoice.Status == AppConstants.InvoiceStatus.Paid) return;

        invoice.Status = AppConstants.InvoiceStatus.Paid;
        invoice.PaidAt = DateTime.UtcNow;
        subscription.Status = AppConstants.SubscriptionStatus.Active;

        var tenant = await db.Tenants.FindAsync(subscription.TenantId);
        if (tenant is not null)
        {
            tenant.PlanId = subscription.PlanId;
            tenant.Status = AppConstants.TenantStatus.Active;
            tenant.UpdatedAt = DateTime.UtcNow;
        }
    }
    else if (evt.IsOverdue)
    {
        // Guard: não rebaixar assinatura já paga ou cancelada
        if (invoice.Status is AppConstants.InvoiceStatus.Paid
                           or AppConstants.InvoiceStatus.Cancelled
                           or AppConstants.InvoiceStatus.Overdue) return;

        invoice.Status = AppConstants.InvoiceStatus.Overdue;
        subscription.Status = AppConstants.SubscriptionStatus.PastDue;
    }
    else if (evt.IsCancelled)
    {
        // Guard: já cancelado; replay seguro de ignorar
        if (invoice.Status == AppConstants.InvoiceStatus.Cancelled) return;

        invoice.Status = AppConstants.InvoiceStatus.Cancelled;
        subscription.Status = AppConstants.SubscriptionStatus.Cancelled;
    }
    else
    {
        // Evento desconhecido — não fazer nada; log pelo chamador
        return;
    }

    invoice.UpdatedAt = DateTime.UtcNow;
    subscription.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
}
```

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0

### Passo 2: Adicionar testes de idempotência em `BillingServiceTests.cs`

Adicione os seguintes testes ao arquivo de testes existente, seguindo o padrão dos testes
já presentes (InMemory DB, instância criada com `new BillingService(db, ...)`):

```csharp
[Fact]
public async Task ProcessPaymentEvent_WhenAlreadyPaid_DoesNotChangePaidAt()
{
    // Arrange: fatura já paga
    var originalPaidAt = DateTime.UtcNow.AddHours(-1);
    // ... setup com invoice.Status = "paid", invoice.PaidAt = originalPaidAt ...

    // Act: replay do evento paid
    await service.ProcessPaymentEventAsync(new WebhookEvent(ExternalId: "ext-123", IsPaid: true, ...));

    // Assert: PaidAt não mudou (guard retornou early)
    var invoice = await db.Invoices.FirstAsync();
    invoice.PaidAt.Should().BeCloseTo(originalPaidAt, TimeSpan.FromSeconds(1));
}

[Fact]
public async Task ProcessPaymentEvent_WhenOverdueArrivesAfterPaid_DoesNotDowngradeSubscription()
{
    // Arrange: assinatura ativa (já foi paga)
    // ... setup com invoice.Status = "paid", subscription.Status = "active" ...

    // Act: webhook overdue chega fora de ordem (replay tardio)
    await service.ProcessPaymentEventAsync(new WebhookEvent(ExternalId: "ext-123", IsOverdue: true, ...));

    // Assert: subscrição permanece ativa
    var sub = await db.Subscriptions.FirstAsync();
    sub.Status.Should().Be(AppConstants.SubscriptionStatus.Active);
}

[Fact]
public async Task ProcessPaymentEvent_WhenAlreadyCancelled_DoesNotReprocess()
{
    // Arrange: fatura já cancelada
    // ... setup com invoice.Status = "cancelled" ...

    // Act: replay do evento cancelled
    await service.ProcessPaymentEventAsync(new WebhookEvent(ExternalId: "ext-123", IsCancelled: true, ...));

    // Assert: UpdatedAt não mudou recentemente (ou asserte que SaveChanges não foi chamado)
    // A forma mais simples: verificar que subscription.Status continua "cancelled"
    var sub = await db.Subscriptions.FirstAsync();
    sub.Status.Should().Be(AppConstants.SubscriptionStatus.Cancelled);
}
```

Adapte a construção de `WebhookEvent` ao record/class existente no projeto
(`src/Atendefy.API/Modules/Billing/Models/`).

**Verificar**: `dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~Billing"` →
todos passam, incluindo os 3 novos

## Critérios de conclusão

- [ ] `dotnet build Atendefy.slnx -c Release --no-restore` exit 0
- [ ] `dotnet test Atendefy.slnx -c Release` exit 0, incluindo 3 novos testes
- [ ] `ProcessPaymentEventAsync` tem guard `if (invoice.Status == Paid) return` no branch `IsPaid`
- [ ] `ProcessPaymentEventAsync` tem guard contra rebaixamento para `Overdue` quando já `Paid`
- [ ] Apenas os 2 arquivos em escopo foram modificados (`git status`)
- [ ] Status atualizado em `plans/README.md`

## Condições de PARE

- `WebhookEvent` não tem os campos `IsPaid`, `IsOverdue`, `IsCancelled` — inspecione o model
  real e adapte os guards conforme a API existente
- `AppConstants.InvoiceStatus` não tem as constantes esperadas — use os valores reais encontrados
- O método já tem alguma lógica de guard — reporte o estado atual antes de substituir

## Notas de manutenção

- O pattern `if (status == targetState) return` é seguro mas não rastreia replays. Se no futuro
  houver necessidade de auditoria de webhooks recebidos (quantos replays por invoice), adicionar
  um campo `WebhookReceivedAt[]` ou uma tabela `billing_webhook_events` separada.
- Revisor do PR: confirme que todos os 3 branches (`IsPaid`, `IsOverdue`, `IsCancelled`) têm
  guard, e que `SaveChangesAsync` só é chamado quando há mutação real.
