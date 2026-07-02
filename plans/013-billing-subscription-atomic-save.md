# Plan 013: Persistir Subscription+Invoice atomicamente (um SaveChanges)

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `plans/README.md`.
>
> **Drift check (run first)**: `git diff --stat f809720..HEAD -- src/Atendefy.API/Modules/Billing/BillingService.cs`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P2
- **Effort**: S
- **Risk**: LOW
- **Depends on**: none
- **Category**: bug
- **Planned at**: commit `f809720`, 2026-07-02

## Why this matters

Na criação de assinatura, `BillingService` salva a `Subscription` com um
`SaveChangesAsync` e depois a `Invoice` com OUTRO `SaveChangesAsync`. Se o
segundo falhar (constraint, queda de conexão), fica uma assinatura órfã sem
fatura — estado de billing inconsistente que exige correção manual. Como
`BaseEntity` gera o `Id` no client (`Guid.NewGuid()` — ver
`src/Atendefy.API/SharedKernel/BaseEntity.cs:5`), a `Invoice` pode referenciar
`subscription.Id` ANTES de salvar: basta adicionar as duas entidades e chamar
`SaveChangesAsync` UMA vez — o EF envolve tudo numa única transação
automaticamente. Sem `BeginTransaction` explícito (que quebraria o provider
InMemory usado nos testes).

## Current state

Arquivo: `src/Atendefy.API/Modules/Billing/BillingService.cs` — método de criação
de assinatura, trecho das linhas 35–70 hoje:

```csharp
var now = DateTime.UtcNow;
var subscription = new Subscription
{
    TenantId = tenantId,
    PlanId = plan.Id,
    Status = AppConstants.SubscriptionStatus.Pending,
    BillingCycle = request.BillingCycle,
    Provider = request.Provider,
    ExternalCustomerId = customerId,
    ExternalId = charge.ExternalId,
    CurrentPeriodStart = now,
    CurrentPeriodEnd = request.BillingCycle == AppConstants.BillingCycle.Yearly ? now.AddYears(1) : now.AddMonths(1)
};
db.Subscriptions.Add(subscription);
await db.SaveChangesAsync();          // ← 1º save

var invoice = new Invoice
{
    SubscriptionId = subscription.Id,
    TenantId = tenantId,
    Amount = amount,
    Status = AppConstants.InvoiceStatus.Pending,
    Provider = request.Provider,
    BillingType = request.BillingType,
    ExternalId = charge.ExternalId,
    DueDate = dueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
    BoletoUrl = charge.BoletoUrl,
    BoletoBarcode = charge.BoletoBarcode,
    PixCopyPaste = charge.PixCopyPaste,
    ClientSecret = charge.ClientSecret
};
db.Invoices.Add(invoice);
await db.SaveChangesAsync();          // ← 2º save (janela de inconsistência)

return Result<Invoice>.Ok(invoice);
```

Fato-chave: `Subscription` e `Invoice` herdam de `BaseEntity`
(`SharedKernel/BaseEntity.cs`), cujo `Id` é `Guid.NewGuid()` no construtor —
`subscription.Id` já tem valor válido ANTES do save.

Convenções: `Result<T>` para erros de negócio; testes de billing em
`tests/Atendefy.Tests/Billing/` (exemplar de estilo:
`tests/Atendefy.Tests/Billing/BillingServiceTests.cs` se existir, senão
`ProcessPaymentEventAsync` tem teste de idempotência em arquivo próximo — use-o
como padrão de setup InMemory).

## Commands you will need

| Purpose | Command | Expected on success |
|---------|---------|---------------------|
| Build   | `dotnet build Atendefy.slnx -c Release` | exit 0 |
| Testes  | `dotnet test Atendefy.slnx -c Release`  | todos passam |

## Scope

**In scope**:
- `src/Atendefy.API/Modules/Billing/BillingService.cs` (somente o método de criação de assinatura)
- `tests/Atendefy.Tests/Billing/` (1 teste novo)

**Out of scope**:
- `ProcessPaymentEventAsync` (já idempotente — plano 004, DONE).
- Gateways Asaas/Stripe e a chamada `CreateChargeAsync` — a cobrança externa já
  aconteceu antes deste trecho; compensação de charge órfão é problema diferente
  (registrar em maintenance notes, não resolver aqui).
- Não introduzir `BeginTransactionAsync` — quebraria os testes com InMemory.

## Git workflow

- Branch: `advisor/013-billing-atomic-save`
- Conventional commit em português (ex.: `fix(billing): salvar Subscription e Invoice num único SaveChanges`)
- Não fazer push nem abrir PR sem instrução do operador.

## Steps

### Step 1: Unificar os dois saves

No método de criação de assinatura em `BillingService.cs`: remover o primeiro
`await db.SaveChangesAsync();` (logo após `db.Subscriptions.Add(subscription);`)
e manter apenas o save final, de modo que o fluxo fique:

```csharp
db.Subscriptions.Add(subscription);

var invoice = new Invoice
{
    SubscriptionId = subscription.Id,   // Id já gerado no client (BaseEntity)
    ...
};
db.Invoices.Add(invoice);
// Um único SaveChanges: EF persiste ambos na mesma transação — ou tudo, ou nada.
await db.SaveChangesAsync();
```

**Verify**: `dotnet build Atendefy.slnx -c Release` → exit 0.

### Step 2: Teste de atomicidade lógica

Em `tests/Atendefy.Tests/Billing/`, adicionar teste (no arquivo de testes do
BillingService existente, ou criar `BillingServiceSubscriptionTests.cs` seguindo
o padrão de setup dos testes de billing vizinhos):

- `CreateSubscription_PersistsSubscriptionAndInvoiceTogether`: chamar o método
  com um gateway fake/mock que retorna charge válido; assert que existe 1
  Subscription E 1 Invoice com `Invoice.SubscriptionId == subscription.Id`.
- Se o construtor do BillingService exigir gateway factory difícil de mockar,
  siga exatamente o padrão de arrange do teste de idempotência existente
  (`ProcessPaymentEventAsync`) — ele já resolve essas dependências.

**Verify**: `dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~CreateSubscription_Persists"` → passa.

### Step 3: Suíte completa

**Verify**: `dotnet test Atendefy.slnx -c Release` → todos passam.

## Test plan

- 1 teste novo (Step 2), modelado nos testes de billing existentes em
  `tests/Atendefy.Tests/Billing/`.
- Suíte completa verde.

## Done criteria

- [ ] `dotnet build Atendefy.slnx -c Release` → exit 0
- [ ] `dotnet test Atendefy.slnx -c Release` → exit 0, incluindo o teste novo
- [ ] No método de criação de assinatura resta exatamente UM `SaveChangesAsync`
      (`grep -c "SaveChangesAsync" src/Atendefy.API/Modules/Billing/BillingService.cs`
      caiu em 1 em relação ao valor antes da mudança)
- [ ] `git status` — só arquivos in-scope
- [ ] Linha do plano 013 atualizada em `plans/README.md`

## STOP conditions

- Excerpt não bate com o código (drift).
- Descobrir que algum código entre os dois saves LÊ `subscription.Id` do banco
  (não do objeto) ou depende do estado salvo — não deveria, mas se houver, reporte.
- O teste do Step 2 exigir mudanças no BillingService além do escopo (ex.:
  injetar interface nova) — reporte em vez de refatorar.

## Maintenance notes

- Falha residual conhecida (fora deste plano): se `SaveChangesAsync` falhar APÓS
  `CreateChargeAsync` ter cobrado no gateway, existe cobrança externa sem registro
  local. Mitigação futura: job de reconciliação por `ExternalId` — registrar como
  finding em rodada futura se billing escalar.
- Revisor: conferir que nenhuma lógica dependia do timing do 1º save (hooks,
  interceptors — o repo não tem, mas confira).
